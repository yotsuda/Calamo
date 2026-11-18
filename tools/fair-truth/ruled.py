import sys, os
from PIL import Image, ImageDraw, ImageFont
from staves import staves as find_page_staves, at
# ruled.py <page.png> <out-prefix> <clefs> <x0> <x1> <zoom> [pieces]: each system of a page (staves found in five
# windows across the page and each line fitted along x, so a tilted scan is named right -- staves.py; grouped by <clefs>,
# e.g. "tTb" = voice treble, piano treble, piano bass), cut x0..x1 into pieces, enlarged, with the pitch of every line and
# space written in red every 330 px along each staff -- to name heads by eye when writing the truth. A narrow x0..x1 with
# a large zoom is the way to settle one head. The page is held out: for the truth, never to debug the reader.
# RULED_DARK: a pale print (Taubert Op.136-1 page 2) needs a lighter cut, e.g. 175.
page, prefix, clefs = sys.argv[1], sys.argv[2], sys.argv[3]
x0, x1, zoom = int(sys.argv[4]), int(sys.argv[5]), float(sys.argv[6]); pieces = int(sys.argv[7]) if len(sys.argv) > 7 else 2
im = Image.open(page).convert('RGB'); g = im.convert('L')
staves = find_page_staves(g)
print('staves', len(staves), 'tilt px/1000:', ' '.join(f'{s[0][1] * 1000:.1f}' for s in staves))
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
