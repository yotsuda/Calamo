import sys, os
from PIL import Image, ImageDraw, ImageFont
# ruled.py <page.png> <out-prefix> <clefs> <x0> <x1> <zoom> [pieces]: each system of a page (staves found as in lines.py,
# grouped by <clefs>, e.g. "tTb" = voice treble, piano treble, piano bass), cut x0..x1 into pieces, enlarged, with
# the pitch of every line and space written in red at the left and right edges of each staff and faint guides every
# 250 px -- to name heads by eye when writing the truth. The page is held out: for the truth, never to debug the reader.
page, prefix, clefs = sys.argv[1], sys.argv[2], sys.argv[3]
x0, x1, zoom = int(sys.argv[4]), int(sys.argv[5]), float(sys.argv[6]); pieces = int(sys.argv[7]) if len(sys.argv) > 7 else 2
im = Image.open(page).convert('RGB'); g = im.convert('L'); px = g.load()
cx0, cx1 = 1700, 2400; w = cx1 - cx0; rows = []
for y in range(im.height):
    if sum(1 for x in range(cx0, cx1, 2) if px[x, y] < 128) > 0.35 * w: rows.append(y)
lines = []
for y in rows:
    if lines and y - lines[-1][-1] <= 1: lines[-1].append(y)
    else: lines.append([y])
mids = [sum(l) / len(l) for l in lines]
staves = [mids[i:i + 5] for i in range(0, len(mids) - 4, 5)]
names = 'CDEFGAB'
def step_name(s):  # s: diatonic step, 0 = c' (middle C)
    o = s // 7; n = names[s % 7]
    return n.lower() + ("'" * (o + 1) if o >= 0 else "," * (-o - 1))
top = {'t': 10, 'b': -2}  # step of the top line: treble f'' (10), bass a (-2)
per = len(clefs); n = 0
try: font = ImageFont.truetype('arial.ttf', 22)
except OSError: font = ImageFont.load_default()
for si in range(0, len(staves) - per + 1, per):
    sys_staves = staves[si:si + per]; n += 1
    ya = int(sys_staves[0][0] - 260); yb = int(sys_staves[-1][4] + 260)
    out = im.copy(); d = ImageDraw.Draw(out)
    for st, c in zip(sys_staves, clefs):
        sp = (st[4] - st[0]) / 8; t = top[c.lower()]
        for k in range(-2, 15):  # k = 4 is the top line, 12 the bottom: six steps beyond each
            y = st[0] + (k - 4) * sp
            name = step_name(t - (k - 4))
            for gx in range(x0, x1, 330):
                d.line([(gx, y), (gx + 12, y)], fill=(255, 150, 150), width=1)
                d.text((gx + 14, y - 11), name, fill=(220, 0, 0), font=font)
    W = x1 - x0
    for h in range(pieces):
        a = x0 + int(W * h / pieces) - (60 if h else 0); b = x0 + int(W * (h + 1) / pieces) + (60 if h < pieces - 1 else 0)
        c = out.crop((max(0, a), max(0, ya), min(im.width, b), min(im.height, yb)))
        c = c.resize((int(c.width * zoom), int(c.height * zoom)), Image.LANCZOS)
        f = f'{prefix}-s{n}{chr(97 + h)}.png'; c.save(f); print(f, c.size)
