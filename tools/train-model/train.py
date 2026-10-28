"""The symbol detector (S10): a small fully convolutional net that marks, on a band of the page,
the centre of every symbol with a heat map per class (the CenterNet idea, without box sizes: a
symbol's size follows from its class and the staff space).

  python train.py train <train.npz> <model.pt> [epochs] [--degrade p] [--seed n]   train (GPU if there is one);
                                                                 p of the windows dirtied by degrade.py;
                                                                 n seeds the windows (1 if not given) and
                                                                 the weights (unseeded if not given)
  python train.py eval  <model.pt> <eval.npz>                    precision/recall per class
  python train.py onnx  <model.pt> <model.onnx>                  export for omr

Input: a grey band, one staff space = 16 px, 224 px tall (14 spaces), any width. Output: K heat
maps at stride 4. A symbol is a peak above 0.5 within half a space of its true centre.

The detector knows nothing of the staves: it is trained on windows cut anywhere from the page
(the staff wherever it falls in the window, a neighbouring staff's symbols at the window's edge
counted like any other), and a page is read as bands 14 spaces tall, 7 spaces apart, each band
answering for its middle 7 spaces (the first and last for their outer edge, too).
"""
import sys, math, time
import numpy as np
import torch, torch.nn as nn, torch.nn.functional as F
from PIL import Image
from dataset import CLASSES, SPACE, HEIGHT, GROUPS
from degrade import degrade

# the share of training windows dirtied by degrade.py (train ... --degrade 0.5); 0 trains as before
DEGRADE = 0.0
# train ... --seed n: the windows drawn and the weights' start (a control learnt as another was, but for chance)
SEED = None

STRIDE = 4
K = len(CLASSES)
CROP = 512          # training crop width
STEP = HEIGHT // 2  # bands: 7 spaces apart


class Block(nn.Module):
    def __init__(self, c_in, c_out, stride=1):
        super().__init__()
        self.a = nn.Conv2d(c_in, c_out, 3, stride, 1, bias=False); self.na = nn.BatchNorm2d(c_out)
        self.b = nn.Conv2d(c_out, c_out, 3, 1, 1, bias=False); self.nb = nn.BatchNorm2d(c_out)
        self.skip = None if (c_in == c_out and stride == 1) else nn.Conv2d(c_in, c_out, 1, stride, bias=False)

    def forward(self, x):
        y = F.relu(self.na(self.a(x)))
        y = self.nb(self.b(y))
        return F.relu(y + (x if self.skip is None else self.skip(x)))


class Detector(nn.Module):
    """/4 resolution with a wide receptive field (a clef is 7 spaces tall = 112 px)."""
    def __init__(self):
        super().__init__()
        self.stem = nn.Sequential(nn.Conv2d(1, 32, 3, 1, 1, bias=False), nn.BatchNorm2d(32), nn.ReLU())
        self.down1 = Block(32, 48, 2)      # /2
        self.down2 = Block(48, 64, 2)      # /4
        self.body = nn.Sequential(Block(64, 96, 1), Block(96, 96, 1), Block(96, 128, 2), Block(128, 128, 1))   # /8 for context
        self.up = nn.ConvTranspose2d(128, 64, 2, 2)
        self.merge = Block(128, 96, 1)
        self.head = nn.Conv2d(96, K, 1)
        self.head.bias.data.fill_(-2.2)    # rare peaks: start near "nothing here"

    def forward(self, x):
        x = self.stem(x)
        x = self.down1(x)
        f4 = self.down2(x)
        f8 = self.body(f4)
        u = self.up(f8)
        u = u[:, :, :f4.shape[2], :f4.shape[3]]
        return self.head(self.merge(torch.cat([f4, u], 1)))


def heatmaps(targets, h, w):
    """Gaussian peaks at the symbol centres, one map per class, at STRIDE."""
    hm = np.zeros((K, h // STRIDE, w // STRIDE), dtype=np.float32)
    sigma = 0.3 * SPACE / STRIDE
    r = int(math.ceil(3 * sigma))
    for c, cx, cy in targets:
        c = int(c)
        # (the peak sits on the grid point nearest the centre and is exactly 1 there: the loss
        # counts a positive only where the target is 1)
        xi, yi = int(round(cx / STRIDE)), int(round(cy / STRIDE))
        if not (0 <= yi < hm.shape[1] and 0 <= xi < hm.shape[2]):
            continue
        for yy in range(max(0, yi - r), min(hm.shape[1], yi + r + 1)):
            for xx in range(max(0, xi - r), min(hm.shape[2], xi + r + 1)):
                v = math.exp(-((xx - xi) ** 2 + (yy - yi) ** 2) / (2 * sigma * sigma))
                if v > hm[c, yy, xx]:
                    hm[c, yy, xx] = v
    return hm


def focal_loss(pred, gt, mask=None):
    """CenterNet's penalty-reduced focal loss over the sigmoid heat maps (only where mask is 1: a page
    read by the reader is taught only its bars read right, and only the classes it reads; pseudo.py)."""
    p = torch.sigmoid(pred).clamp(1e-4, 1 - 1e-4)
    pos = gt.eq(1).float(); neg = 1 - pos
    if mask is not None:
        pos = pos * mask; neg = neg * mask
    pos_loss = torch.log(p) * (1 - p) ** 2 * pos
    neg_loss = torch.log(1 - p) * p ** 2 * (1 - gt) ** 4 * neg
    n = pos.sum()
    return -(pos_loss.sum() + neg_loss.sum()) / max(1.0, float(n))


def mask_of(regions, x0, y0, dy):
    """The loss's mask over a window cut at x0, y0 and moved down dy: a page's regions (x0 y0 x1 y1 group,
    in page pixels) open their group's classes (GROUPS), and an ignore box (group 2) shuts every class
    (the window's small turn is left out: a grid point at a region's edge either way)."""
    m = np.zeros((K, HEIGHT // STRIDE, CROP // STRIDE), dtype=np.float32)
    for shut in (False, True):
        for rx0, ry0, rx1, ry1, g in regions:
            if (g == 2) != shut:
                continue
            gx0 = max(0, int(math.ceil((rx0 - x0) / STRIDE))); gx1 = min(m.shape[2], int(math.floor((rx1 - x0) / STRIDE)) + 1)
            gy0 = max(0, int(math.ceil((ry0 - y0 + dy) / STRIDE))); gy1 = min(m.shape[1], int(math.floor((ry1 - y0 + dy) / STRIDE)) + 1)
            if gx1 <= gx0 or gy1 <= gy0:
                continue
            if shut:
                m[:, gy0:gy1, gx0:gx1] = 0
            else:
                m[GROUPS[int(g)], gy0:gy1, gx0:gx1] = 1
    return m


def load(npz):
    """The pages of one .npz, or of several given with commas (a set added without rebuilding the rest).
    "x.npz*2" lists x's pages twice, so training draws them twice as often (B4: Lily#'s own pages
    kept from being drowned by the other fonts')."""
    images, targets, names = [], [], []
    for one in npz.split(','):
        one, _, times = one.partition('*')
        d = np.load(one, allow_pickle=True)
        im, tg, nm = list(d['images']), list(d['targets']), list(d['names'])
        # (a page the reader read, pseudo.py: its regions, which say where the loss looks; others are taught everywhere)
        rg = list(d['regions']) if 'regions' in d.files else [None] * len(im)
        # (the same arrays listed again, not read again: a npz is decompressed afresh at every access)
        n = int(times or 1)
        images += im * n; targets += tg * n; names += nm * n; REGIONS.extend(rg * n)
    return images, targets, names


REGIONS = []      # per page loaded, its regions (pseudo.py) or None, in load's order


def crops(images, targets, rng, batch):
    """Windows HEIGHT x CROP cut from the pages, wherever: most of them around a symbol (the margins
    are wide and empty), a few anywhere, so the detector meets the blank paper, titles and names."""
    xs, ys, ms = [], [], []
    while len(xs) < batch:
        i = rng.integers(len(images))
        img, tg = images[i], targets[i]
        regions = REGIONS[i] if i < len(REGIONS) else None
        h, w = img.shape
        if len(tg) and rng.uniform() < 0.8:
            c, cx, cy = tg[rng.integers(len(tg))]
            x0 = int(cx - rng.uniform(0, CROP)); y0 = int(cy - rng.uniform(0, HEIGHT))
        else:
            x0 = int(rng.integers(-CROP // 2, w)); y0 = int(rng.integers(-HEIGHT // 2, h))
        x0 = max(-CROP // 2, min(w - CROP // 2, x0)); y0 = max(-HEIGHT // 2, min(h - HEIGHT // 2, y0))
        crop = np.full((HEIGHT, CROP), 255, dtype=np.uint8)
        sx0, sy0 = max(0, x0), max(0, y0); sx1, sy1 = min(w, x0 + CROP), min(h, y0 + HEIGHT)
        if sx1 > sx0 and sy1 > sy0:
            crop[sy0 - y0:sy1 - y0, sx0 - x0:sx1 - x0] = img[sy0:sy1, sx0:sx1]
        t = [(c, cx - x0, cy - y0) for c, cx, cy in tg if x0 <= cx < x0 + CROP and y0 <= cy < y0 + HEIGHT]
        # a scanned page is never quite level nor quite where the print put it: the training windows
        # get the same play, a shift of up to half a space and a turn of up to 0.4 degrees
        dy = rng.uniform(-0.5, 0.5) * SPACE
        ang = rng.uniform(-0.4, 0.4)
        im = Image.fromarray(crop)
        im = im.rotate(ang, resample=Image.Resampling.BILINEAR, center=(CROP / 2, HEIGHT / 2), fillcolor=255)
        im = im.transform(im.size, Image.Transform.AFFINE, (1, 0, 0, 0, 1, -dy), resample=Image.Resampling.BILINEAR, fillcolor=255)
        crop = np.array(im)
        ca, sa = math.cos(math.radians(ang)), math.sin(math.radians(ang))
        moved = []
        for c, cx, cy in t:
            rx, ry = cx - CROP / 2, cy - HEIGHT / 2
            # (PIL turns counter-clockwise for a positive angle, in image coordinates: y down)
            nx, ny = CROP / 2 + rx * ca + ry * sa, HEIGHT / 2 - rx * sa + ry * ca + dy
            if 0 <= nx < CROP and 0 <= ny < HEIGHT:
                moved.append((c, nx, ny))
        # a little contrast and brightness play, as paper and ink vary
        g = crop.astype(np.float32) / 255.0
        # (and, for a share of the windows, an old print's or a worn scan's dirt, drawn afresh: degrade.py)
        if DEGRADE and rng.uniform() < DEGRADE:
            j = rng.integers(len(images)); oi = images[j]
            other = np.full((HEIGHT, CROP), 255, dtype=np.uint8)
            ox, oy = int(rng.integers(0, max(1, oi.shape[1] - CROP))), int(rng.integers(0, max(1, oi.shape[0] - HEIGHT)))
            part = oi[oy:oy + HEIGHT, ox:ox + CROP]; other[:part.shape[0], :part.shape[1]] = part
            g = degrade(g, rng, other.astype(np.float32) / 255.0)
        g = np.clip((g - 0.5) * rng.uniform(0.8, 1.2) + 0.5 + rng.uniform(-0.1, 0.1), 0, 1)
        xs.append(1.0 - g)                      # ink = 1
        ys.append(heatmaps(moved, HEIGHT, CROP))
        ms.append(np.ones((K, HEIGHT // STRIDE, CROP // STRIDE), dtype=np.float32) if regions is None else mask_of(regions, x0, y0, dy))
    return torch.tensor(np.stack(xs))[:, None], torch.tensor(np.stack(ys)), torch.tensor(np.stack(ms))


def train(npz, out, epochs):
    dev = 'cuda' if torch.cuda.is_available() else 'cpu'
    images, targets, _ = load(npz)
    if SEED is not None:
        torch.manual_seed(SEED)
    net = Detector().to(dev)
    opt = torch.optim.AdamW(net.parameters(), lr=1e-3, weight_decay=1e-4)
    steps = 200
    sched = torch.optim.lr_scheduler.OneCycleLR(opt, max_lr=2e-3, total_steps=epochs * steps)
    rng = np.random.default_rng(1 if SEED is None else SEED)
    t0 = time.time()
    for ep in range(epochs):
        net.train(); tot = 0.0
        for _ in range(steps):
            x, y, m = crops(images, targets, rng, 16)
            x, y, m = x.to(dev), y.to(dev), m.to(dev)
            loss = focal_loss(net(x), y, m)
            opt.zero_grad(); loss.backward(); opt.step(); sched.step()
            tot += loss.item()
        print('epoch %d loss %.4f  %.0fs' % (ep + 1, tot / steps, time.time() - t0), flush=True)
        torch.save(net.state_dict(), out)


def peaks(hm, thresh=0.5):
    """(class, x, y, score) in band pixels: local maxima over 3x3 above the threshold, each placed at
    the centre of mass of its 3x3 neighbourhood (a quarter of a space between grid points is too
    coarse to tell a line from a space)."""
    p = torch.sigmoid(hm)
    m = F.max_pool2d(p, 3, 1, 1)
    keep = (p == m) & (p > thresh)
    out = []
    pk, ph, pw = p.shape
    for c, y, x in keep.nonzero().tolist():
        y0, y1, x0, x1 = max(0, y - 1), min(ph, y + 2), max(0, x - 1), min(pw, x + 2)
        win = p[c, y0:y1, x0:x1]
        tot = float(win.sum())
        ys = torch.arange(y0, y1, dtype=torch.float32)[:, None].expand_as(win)
        xs = torch.arange(x0, x1, dtype=torch.float32)[None, :].expand_as(win)
        fy, fx = float((win * ys).sum()) / tot, float((win * xs).sum()) / tot
        # (grid point i stands for pixel i * STRIDE: the target peak sits at round(centre / STRIDE))
        out.append((c, fx * STRIDE, fy * STRIDE, float(p[c, y, x]), y * STRIDE))
    return out


def detect_page(net, img, dev, thresh=0.5):
    """(class, x, y, score) over a page (uint8, SPACE per staff space): bands HEIGHT tall, STEP apart,
    each answering for its middle STEP rows (the first band for its top rows, the last for its bottom ones)."""
    h, w = img.shape
    w4 = (w + STRIDE - 1) // STRIDE * STRIDE
    page = np.pad(1.0 - img.astype(np.float32) / 255.0, ((0, 0), (0, w4 - w)))
    tops = list(range(0, max(1, h - HEIGHT + STEP), STEP))
    out = []
    with torch.no_grad():
        for k, top in enumerate(tops):
            band = page[top:top + HEIGHT]
            if band.shape[0] < HEIGHT:
                band = np.pad(band, ((0, HEIGHT - band.shape[0]), (0, 0)))
            hm = net(torch.tensor(band)[None, None].to(dev))[0].cpu()
            lo = 0 if k == 0 else STEP // 2
            hi = HEIGHT if k == len(tops) - 1 else STEP // 2 + STEP
            # (the band by the peak's grid row, not its refined centre: a peak where two bands meet
            # was kept by both when each put its centre on its own side of the line; Detector.cs)
            for c, fx, fy, score, gy in peaks(hm, thresh):
                if lo <= gy < hi:
                    out.append((c, fx, fy + top, score))
    return out


OFFSETS = []      # (dx, dy) of every match, in pixels: a bias here would put a head on the wrong line


def match(found, tg):
    """Pairs the detections with the truth (same class, within half a space): (tp, fn, fp) per class."""
    tp = np.zeros(K); fn = np.zeros(K); fp = np.zeros(K)
    used = [False] * len(found)
    for c, cx, cy in tg:
        c = int(c); best = -1; bd = 0.5 * SPACE
        for i, (fc, fx, fy, _) in enumerate(found):
            if used[i] or fc != c: continue
            d = math.hypot(fx - cx, fy - cy)
            if d < bd: bd = d; best = i
        if best >= 0: used[best] = True; tp[c] += 1; OFFSETS.append((found[best][1] - cx, found[best][2] - cy))
        else: fn[c] += 1
    for i, (fc, _, _, _) in enumerate(found):
        if not used[i]: fp[fc] += 1
    return tp, fn, fp


def report(tp, fn, fp):
    if OFFSETS:
        o = np.array(OFFSETS) / SPACE
        print('offset (spaces): mean dx %+.3f dy %+.3f, |dx| 99%% %.3f, |dy| 99%% %.3f, max |dy| %.3f' % (
            o[:, 0].mean(), o[:, 1].mean(), np.percentile(np.abs(o[:, 0]), 99), np.percentile(np.abs(o[:, 1]), 99), np.abs(o[:, 1]).max()))
    print('%-10s %7s %7s %7s %7s %7s' % ('class', 'truth', 'found', 'missed', 'extra', 'recall'))
    for c in range(1, K):
        n = tp[c] + fn[c]
        if n or fp[c]:
            print('%-10s %7d %7d %7d %7d %6.1f%%' % (CLASSES[c], n, tp[c], fn[c], fp[c], 100 * tp[c] / max(1, n)))
    n = tp[1:].sum() + fn[1:].sum()
    print('all        %7d %7d %7d %7d %6.1f%%  precision %.1f%%' % (n, tp[1:].sum(), fn[1:].sum(), fp[1:].sum(), 100 * tp[1:].sum() / max(1, n), 100 * tp[1:].sum() / max(1, tp[1:].sum() + fp[1:].sum())))


def evaluate(model, npz):
    dev = 'cuda' if torch.cuda.is_available() else 'cpu'
    net = Detector().to(dev); net.load_state_dict(torch.load(model, map_location=dev)); net.eval()
    images, targets, names = load(npz)
    tp = np.zeros(K); fn = np.zeros(K); fp = np.zeros(K)
    for img, tg in zip(images, targets):
        a, b, c = match(detect_page(net, img, dev), tg)
        tp += a; fn += b; fp += c
    report(tp, fn, fp)


def export(model, out):
    net = Detector(); net.load_state_dict(torch.load(model, map_location='cpu')); net.eval()
    x = torch.zeros(1, 1, HEIGHT, 512)
    torch.onnx.export(net, x, out, input_names=['strip'], output_names=['heat'], dynamic_axes={'strip': {3: 'width'}, 'heat': {3: 'width4'}}, opset_version=17, dynamo=False)
    print('->', out)


if __name__ == '__main__':
    cmd = sys.argv[1]
    if '--degrade' in sys.argv:
        k = sys.argv.index('--degrade'); DEGRADE = float(sys.argv[k + 1]); del sys.argv[k:k + 2]
    if '--seed' in sys.argv:
        k = sys.argv.index('--seed'); SEED = int(sys.argv[k + 1]); del sys.argv[k:k + 2]
    if cmd == 'train': train(sys.argv[2], sys.argv[3], int(sys.argv[4]) if len(sys.argv) > 4 else 10)
    elif cmd == 'eval': evaluate(sys.argv[2], sys.argv[3])
    elif cmd == 'onnx': export(sys.argv[2], sys.argv[3])
