import json, sys, os
from PIL import Image
# strips.py <name> <part> <above> <below> <pieces> <zoom>: one part's staff of each system, enlarged, in pieces left to right
name, part = sys.argv[1], sys.argv[2]; above, below, pieces = int(sys.argv[3]), int(sys.argv[4]), int(sys.argv[5]); zoom = float(sys.argv[6])
j = json.load(open(f'out/fair/{name}/omr.omr.json', encoding='utf-8'))
out = f'out/fair-truth/{name}'; os.makedirs(out, exist_ok=True)
pages = {p['page']: p for p in j['pages']}
rows = {}
for m in j['measures']:
    if m['part'] == part: rows.setdefault((m['page'], round(m['staff'][1] / 60)), []).append(m)
n = 0
for key in sorted(rows):
    ms = rows[key]; im = Image.open(pages[key[0]]['image']); n += 1
    first = min(m['measure'] for m in ms); last = max(m['measure'] for m in ms)
    x0 = max(0, min(m['staff'][0] for m in ms) - 20); x1 = min(im.width, max(m['staff'][2] for m in ms) + 20)
    y0 = max(0, min(m['staff'][1] for m in ms) - above); y1 = min(im.height, max(m['staff'][3] for m in ms) + below)
    w = x1 - x0
    for h in range(pieces):
        a = x0 + int(w * h / pieces) - (30 if h else 0); b = x0 + int(w * (h + 1) / pieces) + (30 if h < pieces - 1 else 0)
        c = im.crop((max(0, a), y0, min(im.width, b), y1)); c = c.resize((int(c.width * zoom), int(c.height * zoom)), Image.LANCZOS)
        f = f'{out}/{part}{n:02d}-b{first:02d}-{last:02d}-{chr(97 + h)}.png'; c.save(f); print(f, c.size)
