"""Count the development set's scan slurs by kind and whether each detector sees their ends.

python slurcount.py <out.tsv> <model.pt>[,<model.pt>...] <export dir> [<export dir> ...]   (from tools/train-model)
(export dirs: out/pseudo2/dev-*-scan, from tools/train-model/export-dev.ps1)

Each truth slur (read-marks.tsv, paired per part in time order) whose two ends have a chord read
(both in bars read right) is put in a kind: the chords it spans (heads read between its ends on the
staff, by distinct x), whether it crosses a barline, and its side (below where the start's stem is up).
Each end is looked for as pseudo.py does (the chord's window, x +-2 spaces, rows +-3 spaces), per model:
the surest slur end (or tie end) there, its class and score.
"""
import os, sys, csv, glob
from collections import defaultdict
import numpy as np
from PIL import Image
import torch
from train import Detector
from pseudo import candidates, nearest, read_page, num

out_tsv, models, dirs = sys.argv[1], sys.argv[2].split(','), sys.argv[3:]
dev = torch.device('cuda' if torch.cuda.is_available() else 'cpu')
nets = []
for m in models:
    net = Detector().to(dev); net.load_state_dict(torch.load(m, map_location=dev)); net.eval(); nets.append(net)

rows_out = []
for d in dirs:
    piece = os.path.basename(d.rstrip('\\/'))
    marks = []
    with open(os.path.join(d, 'read-marks.tsv'), newline='') as f:
        for r in csv.DictReader(f, delimiter='\t'):
            if r['kind'] in ('slur_start', 'slur_end'):
                marks.append(r)
    # where each (read bar, part) stands: page, system, the staff's rows
    where = {}
    pages = {}
    for tsv in sorted(glob.glob(os.path.join(d, 'read-p*.tsv'))):
        rows = read_page(tsv)
        s = float(rows['space'][0]['x'])
        staff = {(st['sys'], st['part']): (float(st['y']), float(st['b'])) for st in rows['staff']}
        for m in rows['measure']:
            b = int(float(m['a']))
            for (sy, p), tb in staff.items():
                if sy == m['sys']:
                    where[(b, p)] = (tsv, sy, tb)
        pages[tsv] = (rows, s)
    # pair per part: (read, t) order, a stack
    seq = defaultdict(list)
    for r in marks:
        seq[r['part']].append(r)
    slurs = []
    for p, ms in seq.items():
        ms.sort(key=lambda r: (int(r['read']), int(r['t']), r['kind'] == 'slur_start'))
        stack = []
        for r in ms:
            if r['kind'] == 'slur_start':
                stack.append(r)
            elif stack:
                slurs.append((p, stack.pop(), r))
            else:
                slurs.append((p, None, r))
        for r in stack:
            slurs.append((p, r, None))
    cands_of = {}
    for p, a, b in slurs:
        rec = {'piece': piece, 'part': p}
        if a is None or b is None or num(a['x']) is None or num(b['x']) is None:
            rec['kind'] = 'unplaced'
            rows_out.append(rec); continue
        wa, wb = where.get((int(a['read']), p)), where.get((int(b['read']), p))
        if not wa or not wb or wa[0] != wb[0] or wa[1] != wb[1]:
            rec['kind'] = 'across systems'
            rows_out.append(rec); continue
        tsv, sy, (top, bot) = wa
        rows, s = pages[tsv]
        xa, xb = num(a['x']), num(b['x'])
        xs = sorted(float(h['x']) for h in rows['head'] if h['sys'] == sy and h['part'] == p and xa - 0.5 * s <= float(h['x']) <= xb + 0.5 * s)
        chords = 0; last = -1e9
        for x in xs:
            if x - last > 0.6 * s:
                chords += 1
            last = x
        rec['chords'] = chords
        rec['bars'] = int(b['read']) - int(a['read'])
        rec['side'] = 'below' if a['stem'] == '1' else 'above' if a['stem'] == '-1' else '?'
        rec['span'] = round((xb - xa) / s, 1)
        rec['page'] = tsv[:-4] + '.png'; rec['xa'] = round(xa); rec['xb'] = round(xb)
        rec['y'] = round((num(a['lo']) + num(a['hi'])) / 2); rec['s'] = s
        rec['kind'] = ('2' if chords <= 2 else '3-4' if chords <= 4 else '5+') + (' over bar' if rec['bars'] else '')
        if tsv not in cands_of:
            gray = np.array(Image.open(tsv[:-4] + '.png').convert('L'))
            cands_of[tsv] = []
            for net in nets:
                _, _, c = candidates([net], gray, s, dev)
                cands_of[tsv].append(c)
        for mi, m in enumerate(models):
            name = os.path.basename(m)[8:-3]
            for end, r, cls in (('l', a, {'slur_l', 'tie_l'}), ('r', b, {'slur_r', 'tie_r'})):
                x, sx, lo, hi = num(r['x']), num(r['stemx']), num(r['lo']), num(r['hi'])
                xs2 = [x] + ([sx] if sx else [])
                c = nearest(cands_of[tsv][mi], cls, min(xs2) - 2 * s, lo - 3 * s, max(xs2) + 2 * s, hi + 3 * s, x, (lo + hi) / 2)
                rec[f'{name}.{end}'] = f'{c[0]}:{c[3]:.2f}' if c else '-'
        rows_out.append(rec)
    print(piece, len(slurs), file=sys.stderr)

keys = ['piece', 'part', 'kind', 'chords', 'bars', 'side', 'span', 'page', 'xa', 'xb', 'y', 's'] + [f'{os.path.basename(m)[8:-3]}.{e}' for m in models for e in 'lr']
with open(out_tsv, 'w', newline='', encoding='utf-8') as f:
    w = csv.writer(f, delimiter='\t'); w.writerow(keys)
    for r in rows_out:
        w.writerow([r.get(k, '') for k in keys])
