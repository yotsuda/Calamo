import sys, os
from PIL import Image, ImageDraw, ImageFont
# ruled.py <page.png> <out-prefix> <clefs> <x0> <x1> <zoom> [pieces]: each system of a page (staves found in five
# windows across the page and each line fitted along x, so a tilted scan is named right; grouped by <clefs>, e.g. "tTb" =
# voice treble, piano treble, piano bass), cut x0..x1 into pieces, enlarged, with the pitch of every line and space
# written in red every 330 px along each staff -- to name heads by eye when writing the truth. A narrow x0..x1 with a
# large zoom is the way to settle one head. The page is held out: for the truth, never to debug the reader.
page, prefix, clefs = sys.argv[1], sys.argv[2], sys.argv[3]
x0, x1, zoom = int(sys.argv[4]), int(sys.argv[5]), float(sys.argv[6]); pieces = int(sys.argv[7]) if len(sys.argv) > 7 else 2
im = Image.open(page).convert('RGB'); g = im.convert('L'); px = g.load()
def find_staves(cx0, cx1):  # in the window cx0..cx1: rows dark across half of it, merged, then runs of five evenly spaced
    w = cx1 - cx0; rows = []
    for y in range(im.height):
        if sum(1 for x in range(cx0, cx1, 2) if px[x, y] < 128) > 0.25 * w: rows.append(y)
    lines = []
    for y in rows:
        if lines and y - lines[-1][-1] <= 3: lines[-1].append(y)
        else: lines.append([y])
    ys = [sum(l) / len(l) for l in lines]; out = []; i = 0
    while i + 4 < len(ys):
        g = [ys[i + k + 1] - ys[i + k] for k in range(4)]
        if 12 < min(g) and max(g) < 1.25 * min(g): out.append(ys[i:i + 5]); i += 5
        else: i += 1
    return out
# a tilted or bowed page: the staves found in five windows, matched to the middle one's, each line fitted along x
wins = [(im.width * a // 20, im.width * a // 20 + 300) for a in (2, 6, 10, 14, 17)]
found = [find_staves(a, b) for a, b in wins]; xs = [(a + b) / 2 for a, b in wins]
staves = []
for st in found[2]:
    pts = []
    for f, x in zip(found, xs):
        near = [s for s in f if abs(s[0] - st[0]) < 2 * (st[4] - st[0]) / 4 * 3]
        if near: pts.append((x, min(near, key=lambda s: abs(s[0] - st[0]))))
    # robust to a window that caught the wrong rows: the median of the pairwise slopes of the middle line, then
    # each line's intercept the median over the windows
    pair = sorted((b[1][2] - a[1][2]) / (b[0] - a[0]) for i, a in enumerate(pts) for b in pts[i + 1:])
    sl = pair[len(pair) // 2] if pair else 0.0
    def fit(k):
        c = sorted(s[k] - sl * x for x, s in pts); return (c[len(c) // 2], sl)
    staves.append(([fit(k) for k in range(5)], len(pts)))
# a staff seen in fewer than three windows (words, beams, chords over its lines) takes the page's median tilt
med = sorted(st[0][0][1] for st in staves if st[1] >= 3)[len([s for s in staves if s[1] >= 3]) // 2]
staves = [lines if n >= 3 else [(y + (sl - med) * xs[2], med) for y, sl in lines] for lines, n in staves]
print('staves', len(staves), 'tilt px/1000:', ' '.join(f'{s[0][1] * 1000:.1f}' for s in staves))
def at(line, x): return line[0] + line[1] * x
names = 'CDEFGAB'
def step_name(s):  # s: diatonic step, 0 = c' (middle C)
    o = s // 7; n = names[s % 7]
    return n.lower() + ("'" * (o + 1) if o >= 0 else "," * (-o - 1))
top = {'t': 10, 'b': -2}  # step of the top line: treble f'' (10), bass a (-2)
per = len(clefs); n = 0
step = (at(staves[0][4], im.width / 2) - at(staves[0][0], im.width / 2)) / 8  # one staff step in px: the labels' size
try: font = ImageFont.truetype('arial.ttf', max(10, int(step * 1.1)))
except OSError: font = ImageFont.load_default()
for si in range(0, len(staves) - per + 1, per):
    sys_staves = staves[si:si + per]; n += 1
    ya = int(at(sys_staves[0][0], x0) - 260); yb = int(at(sys_staves[-1][4], x0) + 260)
    print(f'system {n}: crop top y {ya} (piece y = (page y - {ya}) * zoom); staff tops', ' '.join(str(int(at(s[0], x0))) for s in sys_staves))
    out = im.copy(); d = ImageDraw.Draw(out)
    for st, c in zip(sys_staves, clefs):
        t = top[c.lower()]
        for k in range(-2, 15):  # k = 4 is the top line, 12 the bottom: six steps beyond each
            name = step_name(t - (k - 4))
            for gx in range(x0, x1, 330):
                sp = (at(st[4], gx) - at(st[0], gx)) / 8; y = at(st[0], gx) + (k - 4) * sp
                d.line([(gx, y), (gx + 12, y)], fill=(255, 150, 150), width=1)
                d.text((gx + 14, y - 11), name, fill=(220, 0, 0), font=font)
    W = x1 - x0
    for h in range(pieces):
        a = x0 + int(W * h / pieces) - (60 if h else 0); b = x0 + int(W * (h + 1) / pieces) + (60 if h < pieces - 1 else 0)
        c = out.crop((max(0, a), max(0, ya), min(im.width, b), min(im.height, yb)))
        c = c.resize((int(c.width * zoom), int(c.height * zoom)), Image.LANCZOS)
        f = f'{prefix}-s{n}{chr(97 + h)}.png'; c.save(f); print(f, c.size)
