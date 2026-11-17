import json, sys
from PIL import Image
# cropbar.py <set dir> <bar> <out.png> [zoom] [pad]: all staves of one bar of a fair set (<set dir> = out/audiveris/fair-<set>,
# the bar boxes from scored-own/omr.omr.json), enlarged. The bar is the READER's number: where the reader merged or dropped a
# bar (align: "75 of 75 ... of the truth's 76") it runs one behind the truth's from there on, so check the content.
# For checking the truth only (suspects.ps1), never to debug the reader.
d, bar, out = sys.argv[1], int(sys.argv[2]), sys.argv[3]
zoom = float(sys.argv[4]) if len(sys.argv) > 4 else 1.0
pad = int(sys.argv[5]) if len(sys.argv) > 5 else 60
j = json.load(open(f'{d}/scored-own/omr.omr.json', encoding='utf-8'))
ms = [m for m in j['measures'] if m['measure'] == bar]
if not ms: sys.exit(f'no bar {bar}')
page = ms[0]['page']; ms = [m for m in ms if m['page'] == page]
x0 = min(m['box'][0] for m in ms) - pad; x1 = max(m['box'][2] for m in ms) + pad
y0 = min(m['box'][1] for m in ms) - 40; y1 = max(m['box'][3] for m in ms) + 40
im = Image.open([p for p in j['pages'] if p['page'] == page][0]['image'])
c = im.crop((max(0, x0), max(0, y0), min(im.width, x1), min(im.height, y1)))
if zoom != 1.0: c = c.resize((int(c.width * zoom), int(c.height * zoom)), Image.LANCZOS)
c.save(out); print(out, page, (x0, y0, x1, y1), c.size)
