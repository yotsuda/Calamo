import sys
import numpy as np
from PIL import Image, ImageDraw, ImageFilter
# blobs.py <page> <x0> <y0> <x1> <y1> <top line y> <space px> <t|b>: heads.py's head finding with the staff given by hand,
# for a staff ruled.py does not find (Kuecken page 2: the right hand's staves under the 32nd-note beams). Read the top
# line's y and the space with col.py in a clear column. Heads above ledger lines come out about half a step high.
page = sys.argv[1]; x0, y0, x1, y1 = (int(v) for v in sys.argv[2:6]); TOP = float(sys.argv[6]); SP = float(sys.argv[7]); clef = sys.argv[8]
im = Image.open(page).convert('RGB'); g = im.convert('L'); px = g.load()
def blobs(b):
    h, w = b.shape; seen = np.zeros_like(b); res = []
    for yy, xx in zip(*np.nonzero(b)):
        if seen[yy, xx]: continue
        st = [(yy, xx)]; seen[yy, xx] = True; pts = []
        while st:
            cy, cx = st.pop(); pts.append((cy, cx))
            for ny, nx in ((cy + 1, cx), (cy - 1, cx), (cy, cx + 1), (cy, cx - 1)):
                if 0 <= ny < h and 0 <= nx < w and b[ny, nx] and not seen[ny, nx]: seen[ny, nx] = True; st.append((ny, nx))
        res.append(pts)
    return res
sp = SP
box = g.crop((x0, y0, x1, y1)).point(lambda v: 0 if v < 128 else 255)
filled = Image.new('L', (box.width + 2, box.height + 2), 255); filled.paste(box, (1, 1))  # a white rim to flood from
ImageDraw.floodfill(filled, (0, 0), 128); a = np.array(filled)[1:-1, 1:-1]
black = a == 0
for pts in blobs(a == 255):  # enclosed white: the inside of a half note's head is small, a staff's space between bars is not
    if len(pts) < 0.6 * sp * sp:
        for p in pts: black[p] = True
k = max(3, int(sp * 0.55) | 1)
im2 = Image.fromarray(np.where(black, 0, 255).astype(np.uint8)).filter(ImageFilter.MaxFilter(k)).filter(ImageFilter.MinFilter(k))
out = []
for pts in blobs(np.array(im2) < 128):
    ys = [p[0] for p in pts]; xs_ = [p[1] for p in pts]
    bw, bh = max(xs_) - min(xs_) + 1, max(ys) - min(ys) + 1
    if not (0.9 * sp <= bw <= 2.2 * sp and 0.6 * sp <= bh <= 4 * sp): continue
    # the centre from the rows at least 0.7 of the widest: a ledger line's stub under the head's narrow end is left out
    rw = {}
    for yy, xx in pts: rw[yy] = rw.get(yy, 0) + 1
    if bh < 1.5 * sp:
        wide = [yy for yy, c in rw.items() if c >= 0.7 * max(rw.values())]; cy = (min(wide) + max(wide)) / 2
    else: cy = (min(ys) + max(ys)) / 2
    out.append((min(xs_) + x0, cy + y0, bw, bh))
names = 'CDEFGAB'
def step_name(s):
    o = s // 7; n = names[s % 7]
    return n.lower() + ("'" * (o + 1) if o >= 0 else "," * (-o - 1))
t = {'t': 10, 'b': -2}[clef]
for x, y, bw, bh in sorted(out):
    k = (y - TOP) / (SP / 2); n = int(round(bh / SP))
    print(f'x {int(x):5d} y {int(y):5d} k {k:5.1f} {step_name(t - round(k))}' + (f' ({n} high)' if n > 1 else ''))