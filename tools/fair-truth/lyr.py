import json, sys, os
from PIL import Image
# lyr.py <name> <zoom>: the voice staff and the words under it, each system, in one strip
name = sys.argv[1]; zoom = float(sys.argv[2])
j = json.load(open(f'out/fair/{name}/omr.omr.json', encoding='utf-8'))
out = f'out/fair-truth/{name}'; os.makedirs(out, exist_ok=True)
pages = {p['page']: p for p in j['pages']}
rows = {}
for m in j['measures']:
    rows.setdefault((m['page'], m['measure']), {})[m['part']] = m
systems = {}
for (page, bar), parts in rows.items():
    v = parts.get('voice_'); r = parts.get('rh')
    if not v or not r: continue
    systems.setdefault((page, round(v['staff'][1] / 60)), []).append((bar, v, r))
for n, key in enumerate(sorted(systems), 1):
    ms = systems[key]; im = Image.open(pages[key[0]]['image'])
    x0 = max(0, min(v['staff'][0] for _, v, _ in ms) - 20); x1 = min(im.width, max(v['staff'][2] for _, v, _ in ms) + 20)
    y0 = min(v['staff'][1] for _, v, _ in ms) - 50; y1 = min(r['staff'][1] for _, _, r in ms) - 30
    c = im.crop((x0, y0, x1, y1)); c = c.resize((int(c.width * zoom), int(c.height * zoom)), Image.LANCZOS)
    f = f'{out}/lyr{n:02d}-b{min(b for b, _, _ in ms):02d}-{max(b for b, _, _ in ms):02d}.png'; c.save(f); print(f, c.size)