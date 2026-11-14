import json, sys, os
from PIL import Image, ImageDraw, ImageFont
# ruledj.py <name> <per> <zoom> [first last]: like ruled.py (the pitch of every line and space written beside the staves),
# but the staves are taken from the reading's measure boxes (out/fair/<name>/omr.omr.json: only where the staves are,
# never what was read), for pages where ruled.py's own staff search misses staves. Each system is cut into pieces of
# <per> measures; the names are written at each measure's left edge and middle, from that measure's own staff box, so a
# tilted page stays right within a few px. Writes out/fair-truth/<name>/r-bNN-MM.png. The page is held out: for the truth,
# never to debug the reader.
name = sys.argv[1]; per = int(sys.argv[2]); zoom = float(sys.argv[3])
lo, hi = (int(sys.argv[4]), int(sys.argv[5])) if len(sys.argv) > 5 else (0, 10 ** 6)
j = json.load(open(f'out/fair/{name}/omr.omr.json', encoding='utf-8'))
out = f'out/fair-truth/{name}'; os.makedirs(out, exist_ok=True)
pages = {p['page']: p for p in j['pages']}
parts = []
for m in j['measures']:
    if m['part'] not in parts: parts.append(m['part'])
clef0 = {'rh': 't', 'lh': 'b'}
by = {}
for m in j['measures']: by.setdefault(m['measure'], {})[m['part']] = m
names = 'CDEFGAB'
def step_name(s):
    o = s // 7; n = names[s % 7]
    return n.lower() + ("'" * (o + 1) if o >= 0 else "," * (-o - 1))
top = {'t': 10, 'b': -2}
# systems: runs of measures on one page whose first part's staff top stays within 60 px
systems = []
for k in sorted(by):
    m = by[k][parts[0]]
    if systems and systems[-1][-1][1]['page'] == m['page'] and abs(systems[-1][-1][1]['staff'][1] - m['staff'][1]) < 60:
        systems[-1].append((k, m))
    else: systems.append([(k, m)])
clefs = {p: (sys.argv[6] if len(sys.argv) > 6 else 'tb')[i] for i, p in enumerate(parts)}
for sysm in systems:
    ks = [k for k, _ in sysm]
    for i in range(0, len(ks), per):
        group = ks[i:i + per]
        if group[-1] < lo or group[0] > hi: continue
        ms = [by[k][p] for k in group for p in parts if p in by[k]]
        im = Image.open(pages[ms[0]['page']]['image']).convert('RGB')
        d = ImageDraw.Draw(im)
        for k in group:
            for p in parts:
                if p not in by[k]: continue
                m = by[k][p]; x0, yt, x1, yb = m['staff']; sp = (yb - yt) / 8
                font = ImageFont.truetype('arial.ttf', max(10, int(sp * 1.3)))
                t = top[clefs[p]]
                for kk in range(-6, 15):
                    y = yt + kk * sp
                    if kk % 2 == 0 and not 0 <= kk <= 8:  # where ledger lines fall: a faint line across the measure
                        d.line([(x0, y), (x1, y)], fill=(150, 190, 255), width=1)
                    if kk % 2 == 0:
                        d.text((x0 + 2, y - sp * 0.75), step_name(t - kk), fill=(220, 0, 0), font=font)
        x0 = min(m['staff'][0] for m in ms) - 20; x1 = max(m['staff'][2] for m in ms) + 20
        y0 = min(m['staff'][1] for m in ms) - 90; y1 = max(m['staff'][3] for m in ms) + 90
        c = im.crop((max(0, x0), max(0, y0), min(im.width, x1), min(im.height, y1)))
        c = c.resize((int(c.width * zoom), int(c.height * zoom)), Image.LANCZOS)
        f = f'{out}/r-b{group[0]:02d}-{group[-1]:02d}.png'; c.save(f); print(f, c.size, 'crop', max(0, x0), max(0, y0))
