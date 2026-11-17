import sys
import numpy as np
from PIL import Image, ImageDraw, ImageFilter
# heads.py <page.png> <clefs> <x0> <y0> <x1> <y1> [staff]: the note heads in a box of a page, each named by the staff it
# is nearest to (or by staff number <staff>, 0-based on the page, for a head on ledger lines between two staves) in the
# clef given for it in <clefs> (one letter per staff of the page, t or b). Small holes filled (half notes), opened with a
# square about half a space wide (stems, lines, slurs gone), blobs of a head's size kept; heads a second apart come out
# as one tall blob, named top..bottom. For the truth only: to settle a head by its measured y instead of by eye.
args = sys.argv[1:]
src = open(__file__.replace('heads.py', 'ruled.py'), encoding='utf-8').read().split('def at(line, x)')[0]
sys.argv = ['ruled', args[0], 'x', 't', '0', '1', '1']
exec(src)  # -> staves: per staff five (intercept, slope) lines (and its own x0, x1: read ours after)
page, clefs = args[0], args[1]
x0, y0, x1, y1 = (int(v) for v in args[2:6]); force = int(args[6]) if len(args) > 6 else None
def at(line, x): return line[0] + line[1] * x
def blobs(b):  # 4-connected components of a bool array: lists of (y, x)
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
mid = staves[0]; sp = (at(mid[4], (x0 + x1) / 2) - at(mid[0], (x0 + x1) / 2)) / 4  # a space in px
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
top = {'t': 10, 'b': -2}
for x, y, bw, bh in sorted(out):
    cx = x + bw / 2
    si = force if force is not None else min(range(len(staves)), key=lambda i: abs(y - (at(staves[i][0], cx) + at(staves[i][4], cx)) / 2))
    s = staves[si]; t0, t4 = at(s[0], cx), at(s[4], cx); half = (t4 - t0) / 8
    n = int(round(bh / (2 * half)))  # heads stacked in the blob (seconds)
    k = (y - t0) / half
    if n <= 1: tag = step_name(top[clefs[si]] - round(k))
    else:  # a cluster: name its top and bottom head
        kt = (y - bh / 2 + half - t0) / half; kb = (y + bh / 2 - half - t0) / half
        tag = f'{step_name(top[clefs[si]] - round(kt))}..{step_name(top[clefs[si]] - round(kb))} ({n} high)'
    print(f'x {int(cx):5d} y {int(y):5d} staff {si} k {k:5.1f}  {tag}' + (f'  w{bw / sp:.1f}' if bw > 1.6 * sp else ''))
