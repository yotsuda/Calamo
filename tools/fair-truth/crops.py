import json, sys, os
from PIL import Image
# crops.py <name> <pieces> <zoom>: each system of a fair-set page (out/fair/<name>/omr.omr.json: the reading's
# measure boxes, only to find the systems), enlarged, in pieces left to right -- to write the truth from by eye.
# ruled.py: each system with the pitch of every line and space written beside the staves (names heads at a glance);
# ruledj.py: the same names, the staves taken from the reading's measure boxes (where ruled.py misses staves);
# lines.py: the staff lines' y; strips.py: one part's staff; lyr.py: the voice staff and its words; zoom.py: any region; transpose.py: a truth moved to
# another key. All write to out/fair-truth/<name>/. The pages are held out: to write and check the truth, never to debug the reader.
name = sys.argv[1]; halves = int(sys.argv[2]); zoom = float(sys.argv[3])
j = json.load(open(f'out/fair/{name}/omr.omr.json', encoding='utf-8'))
out = f'out/fair-truth/{name}'; os.makedirs(out, exist_ok=True)
pages = {p['page']: p for p in j['pages']}
systems = {}
for m in j['measures']:
    systems.setdefault(m['page'], []).append(m)
n = 0
for pg in sorted(systems):
    im = Image.open(pages[pg]['image'])
    ms = systems[pg]
    # group measures into systems by the first part's boxes: a system is a run of measures on one row
    rows = []
    for m in sorted(ms, key=lambda m: (m['box'][1], m['box'][0])):
        for r in rows:
            if abs(r['top0'] - m['box'][1]) < 40 and r['part0'] == m['part']: r['ms'].append(m); break
        else:
            if m['part'] == ms[0]['part']: rows.append({'top0': m['box'][1], 'part0': m['part'], 'ms': [m]})
    rows.sort(key=lambda r: r['top0'])
    for i, r in enumerate(rows):
        first = min(x['measure'] for x in r['ms']); last = max(x['measure'] for x in r['ms'])
        allm = [x for x in ms if first <= x['measure'] <= last]
        x0 = min(x['box'][0] for x in allm) - 130; x1 = max(x['box'][2] for x in allm) + 40
        y0 = min(x['box'][1] for x in allm) - 70; y1 = max(x['box'][3] for x in allm) + 90
        x0 = max(0, x0); y0 = max(0, y0); x1 = min(im.width, x1); y1 = min(im.height, y1)
        n += 1
        w = x1 - x0
        for h in range(halves):
            a = x0 + int(w * h / halves) - (40 if h else 0); b = x0 + int(w * (h + 1) / halves) + (40 if h < halves - 1 else 0)
            c = im.crop((max(0, a), y0, min(im.width, b), y1))
            c = c.resize((int(c.width * zoom), int(c.height * zoom)), Image.LANCZOS)
            f = f'{out}/s{n:02d}-b{first:02d}-{last:02d}-{chr(97 + h)}.png'
            c.save(f); print(f, c.size)
