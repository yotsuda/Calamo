"""Cut a development set's bar out of the page the reader read, its neighbours either side, to look at.

python cropbar.py <out dir> <set> <bar> <part> [<bar> <part> ...]      (from tools/train-model, the .venv's python)
e.g. python cropbar.py ..\..\out\crops kinkel10-4-scan 9 voice 10 rh

<set> is a folder under out/audiveris (dev-<piece>-<kind>, the dev- left off): the bar's box and staff come from
its scored-own/omr.omr.json (measures: measure, part, page, box, staff), the page from pages/pNN.png. The cut is
the bar and a bar either side on its staff, the staff's height above and below, the bar in a red frame, scaled
to 1600 px wide at most. The part is matched by its start ("voice" for "voice_").
The bars to look at come from scored-own/tie-slur-diffs.txt and diffs.txt (a bar number and a part a line).
"""
import json, os, sys
from PIL import Image, ImageDraw

root = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..'))
outdir, piece = sys.argv[1], sys.argv[2]
pairs = list(zip(sys.argv[3::2], sys.argv[4::2]))
aud = os.path.join(root, 'out', 'audiveris', f'dev-{piece}')
j = json.load(open(os.path.join(aud, 'scored-own', 'omr.omr.json'), encoding='utf-8'))
ms = j['measures']
os.makedirs(outdir, exist_ok=True)
for bar, part in pairs:
    bar = int(bar)
    here = [m for m in ms if m['measure'] == bar and m['part'].startswith(part)]
    if not here:
        print('no measure', bar, part, 'parts:', sorted({m['part'] for m in ms})); continue
    m = here[0]
    page = m['page']
    x0, y0, x1, y1 = m['box']
    sx0, sy0, sx1, sy1 = m['staff']
    sh = sy1 - sy0
    neigh = [n for n in ms if n['page'] == page and n['part'] == m['part'] and abs(n['measure'] - bar) <= 1 and abs(n['staff'][1] - sy0) < sh]
    im = Image.open(os.path.join(aud, 'pages', f'p{page:02d}.png')).convert('RGB')
    X0 = max(0, min(n['box'][0] for n in neigh)); X1 = min(im.width, max(n['box'][2] for n in neigh))
    Y0 = max(0, int(sy0 - 1.5 * sh)); Y1 = min(im.height, int(sy1 + 1.5 * sh))
    crop = im.crop((X0, Y0, X1, Y1))
    ImageDraw.Draw(crop).rectangle((x0 - X0, y0 - Y0, x1 - X0, y1 - Y0), outline=(255, 0, 0), width=2)
    if crop.width > 1600:
        crop = crop.resize((1600, int(crop.height * 1600 / crop.width)))
    name = f'{piece}-b{bar}-{part}.png'
    crop.save(os.path.join(outdir, name))
    print(name, 'page', page, 'box', m['box'], 'crop', (X0, Y0, X1, Y1))
