"""Training pages from real prints the reader read (S50): the development set's scans, where no SVG
truth is, taught by the reader's own reading of the bars it read right (scored against the truth).

The old prints' shapes (an old engraver's F clef, a dot far from its head, a small flat) are not in
the generated sets, and dirtying the generated pages (degrade.py) does not teach them. The reader,
with its rules and its checks across systems, reads many such bars right that the detector sees only
weakly: those bars teach the detector.

  omr ... --detector --align --by-order --export-read <dir>   (read-pNN.png/.tsv, read-bars.tsv)
  python pseudo.py <out.npz> <model.pt>[,<model.pt>...] <export dir> [<export dir> ...]

A page here is taught only in regions (dataset.GROUPS): each staff's bars read right (their heads,
rests, signs, dots, stems' tips, barlines, clefs) and its system's start where its first bar is right
(the clef, key and time). Everything else on the page is left out of the loss, and so is every symbol
the reader read in a bar read wrong, every mark it did not place (grace notes, tremolos, beat repeats),
and every sign the detectors see there that the reader did not take (a cautionary accidental).

A label's place is the detectors' (the models given, read down to 0.1): a symbol's centre is where the
generated sets' truth puts it, and the reader's own points are not that (a dot a space right of its
head, a sign a space left). Where no detector saw the symbol, the reader's point stands for a head, a
rest, a stem's tip and a barline (the rules' pieces of ink are where they are); a sign or dot is
looked for among the rules' named pieces, and failing that its place is left out of the loss.
"""
import os, sys, csv, math, glob
from collections import defaultdict
import numpy as np
from PIL import Image
import torch
from dataset import CLASSES, SPACE
from train import Detector, detect_page

CI = {c: i for i, c in enumerate(CLASSES)}
SIGNS = ('sharp', 'flat', 'natural', 'dsharp', 'dflat')
CLEF_CLASS = {'treble': 'clef_g', 'treble_8': 'clef_g', 'bass': 'clef_f', 'bass_8': 'clef_f', 'alto': 'clef_c', 'tenor': 'clef_c',
              'soprano': 'clef_c', 'mezzosoprano': 'clef_c', 'baritone': 'clef_c'}
STATS = defaultdict(int)


def num(v):
    return None if v in ('-', '') else float(v)


def read_page(tsv):
    rows = defaultdict(list)
    with open(tsv, newline='', encoding='utf-8') as f:
        for r in csv.DictReader(f, delimiter='\t'):
            rows[r['kind']].append(r)
    return rows


def candidates(nets, gray, s, dev):
    """Every model's peaks over the page down to 0.1: (class name, x, y, score) in the page's pixels."""
    scale = SPACE / s
    page = Image.fromarray(gray)
    w, h = max(1, int(round(page.width * scale))), max(1, int(round(page.height * scale)))
    img = np.array(page.resize((w, h), Image.Resampling.BOX), dtype=np.uint8)
    found = []
    for net in nets:
        for c, x, y, score in detect_page(net, img, dev, 0.1):
            found.append((CLASSES[c], x / scale, y / scale, score))
    return img, scale, found


def nearest(cands, classes, x0, y0, x1, y1, px, py):
    """The surest candidate of these classes in the box, nearest (px, py) among the sure ones."""
    best = None
    for c, x, y, sc in cands:
        if c in classes and x0 <= x <= x1 and y0 <= y <= y1:
            key = (round(sc, 1), -math.hypot(x - px, y - py))
            if best is None or key > best[0]:
                best = (key, (c, x, y, sc))
    return best[1] if best else None


def build_set(d, nets, dev):
    bars = {}
    with open(os.path.join(d, 'read-bars.tsv'), newline='') as f:
        for r in csv.DictReader(f, delimiter='\t'):
            bars[(int(r['read']), r['part'])] = r['right'] == '1'
    out = []
    pages = sorted(glob.glob(os.path.join(d, 'read-p*.tsv')))
    clef_offsets = defaultdict(list)     # per clef class: (dx, dy) of the found ones from the staff's left end and middle, in spaces
    staged = []
    for tsv in pages:
        rows = read_page(tsv)
        s = float(rows['space'][0]['x'])
        gray = np.array(Image.open(tsv[:-4] + '.png').convert('L'))
        img, scale, cands = candidates(nets, gray, s, dev)
        staged.append((tsv, rows, s, img, scale, cands))
        # (first pass: where this print's clefs stand, from the ones the detectors saw)
        for st in rows['staff']:
            if st['class'] != '5':
                continue
            left, top, bot = float(st['x']), float(st['y']), float(st['b'])
            mid = (top + bot) / 2
            for g in rows['sig']:
                if g['sys'] == st['sys'] and g['part'] == st['part'] and g['class'] in CLEF_CLASS and g['c'] == '0':
                    cl = CLEF_CLASS[g['class']]
                    k = nearest(cands, {cl}, left - 1.5 * s, top - 3 * s, left + 5 * s, bot + 3 * s, left + 1.5 * s, mid)
                    if k and k[3] >= 0.3:
                        clef_offsets[cl].append(((k[1] - left) / s, (k[2] - mid) / s))
    offset = {cl: (float(np.median([o[0] for o in v])), float(np.median([o[1] for o in v]))) for cl, v in clef_offsets.items() if len(v) >= 2}
    for tsv, rows, s, img, scale, cands in staged:
        targets, regions = [], []

        def label(c, x, y, how):
            targets.append((CI[c], x * scale, y * scale)); STATS[c + ' ' + how] += 1

        def ignore(x0, y0, x1, y1):
            regions.append((x0 * scale, y0 * scale, x1 * scale, y1 * scale, 2))

        def open_(x0, y0, x1, y1, g):
            regions.append((x0 * scale, y0 * scale, x1 * scale, y1 * scale, g))

        staves = [st for st in rows['staff']]
        systems = defaultdict(list)
        for st in staves:
            systems[st['sys']].append(st)
        measures = defaultdict(list)
        for m in rows['measure']:
            measures[m['sys']].append((float(m['x']), float(m['y']), int(float(m['a']))))
        all_tops = sorted((float(st['y']), float(st['b'])) for st in staves)

        def band(st):
            """The staff's rows: halfway to the staves above and below it, five spaces at most."""
            top, bot = float(st['y']), float(st['b'])
            up = max([b for t, b in all_tops if b < top] or [-1e9]); down = min([t for t, b in all_tops if t > bot] or [1e9])
            return max(top - 5 * s, (up + top) / 2), min(bot + 5 * s, (bot + down) / 2)

        def where(r):
            """(right?, bar) of a symbol's row: its staff's part, its x's measure."""
            for x0, x1, b in measures.get(r['sys'], []):
                if x0 <= float(r['x']) < x1:
                    return bars.get((b, r['part'])), b
            return None, None

        # the symbols the reader read, by whether their bar is right
        heads, rests = rows['head'], rows['rest']
        for kind in ('head', 'acc', 'dots', 'stem', 'rest', 'rdots', 'grace', 'tremolo', 'beatrepeat'):
            for r in rows[kind]:
                ok, _ = where(r)
                x, y = float(r['x']), float(r['y'])
                if kind in ('grace', 'tremolo', 'beatrepeat'):
                    # (marks the detector reads otherwise than plain notes: left out)
                    x1 = num(r['a']) or x
                    ignore(x - 3 * s, y - 5 * s, x1 + 3 * s, y + 5 * s); continue
                if not ok:
                    if kind == 'acc': ignore(float(r['a']) - 4 * s, y - 2 * s, x, y + 2 * s)
                    elif kind in ('dots', 'rdots'): ignore(x, y - 1.5 * s, float(r['a']) + 3.5 * s, y + 1.5 * s)
                    else: ignore(x - 1.2 * s, y - 1.2 * s, x + 1.2 * s, y + 1.2 * s)
                    continue
                if kind == 'head':
                    c = r['class']
                    if c not in ('head_black', 'head_half', 'head_whole'):
                        ignore(x - 1.5 * s, y - 1.5 * s, x + 1.5 * s, y + 1.5 * s); continue
                    k = nearest(cands, {c}, x - 0.4 * s, y - 0.4 * s, x + 0.4 * s, y + 0.4 * s, x, y)
                    label(c, k[1], k[2], 'det') if k else label(c, x, y, 'reader')
                elif kind == 'rest':
                    c = r['class']
                    k = nearest(cands, {'rest_long', 'rest_4', 'rest_8', 'rest_16'}, x - 1.2 * s, y - 1.5 * s, x + 1.2 * s, y + 1.5 * s, x, y)
                    label(c, k[1], k[2], 'det') if k else label(c, x, y, 'reader')
                elif kind == 'stem':
                    pass   # (below: one to a stem, chords' heads share theirs)
                elif kind == 'acc':
                    c, hl = r['class'], float(r['a'])
                    k = nearest(cands, {c}, hl - 3.5 * s, y - 1.3 * s, hl + 0.2 * s, y + 1.0 * s, hl - 0.9 * s, y - (0.4 * s if c == 'flat' else 0))
                    if k: label(c, k[1], k[2], 'det'); continue
                    o = [g for g in rows['other'] if g['class'].removeprefix('key-') == c and hl - 3.5 * s <= float(g['x']) <= hl + 0.2 * s and abs(float(g['y']) - y) < 1.3 * s]
                    if o:
                        g = min(o, key=lambda g: abs(float(g['x']) - (hl - 0.9 * s)))
                        label(c, float(g['x']), float(g['y']), 'rules')
                    else:
                        STATS[c + ' lost'] += 1; ignore(hl - 4 * s, y - 2 * s, hl + 0.2 * s, y + 2 * s)
                elif kind in ('dots', 'rdots'):
                    n, right = int(r['class']), float(r['a'])
                    pos = num(r['b'])
                    dy = y - 0.5 * s if kind == 'dots' and pos is not None and round(pos) % 2 == 0 else y
                    if kind == 'rdots': dy = y - 0.5 * s
                    got = []
                    for c, cx, cy, sc in sorted(cands, key=lambda t: -t[3]):
                        if c == 'dot' and right - 0.2 * s <= cx <= right + 3.5 * s and abs(cy - dy) < 0.9 * s and all(math.hypot(cx - gx, cy - gy) > 0.4 * s for gx, gy in got):
                            got.append((cx, cy))
                    got = [(gx, gy, 'det') for gx, gy in sorted(got)[:n]]
                    if len(got) < n:
                        o = [(float(g['x']), float(g['y'])) for g in rows['other'] if g['class'].rstrip('*') == 'dot'
                             and right - 0.2 * s <= float(g['x']) <= right + 3.5 * s and abs(float(g['y']) - dy) < 0.9 * s]
                        for p in sorted(o):
                            if len(got) < n and all(math.hypot(p[0] - gx, p[1] - gy) > 0.4 * s for gx, gy, _ in got):
                                got.append((p[0], p[1], 'rules'))
                    if len(got) == n:
                        for gx, gy, how in got: label('dot', gx, gy, how)
                    else:
                        STATS['dot lost'] += n; ignore(right - 0.2 * s, dy - 1.5 * s, right + 3.5 * s, dy + 1.5 * s)
        # stems: one per tip, its count the most beams of the heads that share it
        tips = {}
        for r in rows['stem']:
            ok, _ = where(r)
            if not ok: continue
            x, y = float(r['x']), float(r['y'])
            key = (r['sys'], r['part'], round(x / (0.3 * s)), r['a'])
            n = int(r['class'][5:])
            if key not in tips or n > tips[key][2]: tips[key] = (x, y, n)
        for x, y, n in tips.values():
            k = nearest(cands, {'stem_0', 'stem_1', 'stem_2', 'stem_3'}, x - 0.8 * s, y - 0.8 * s, x + 0.8 * s, y + 0.8 * s, x, y)
            label('stem_%d' % n, k[1], k[2], 'det') if k else label('stem_%d' % n, x, y, 'reader')

        for sid, sts in systems.items():
            ms = sorted(measures.get(sid, []))
            if not ms: continue
            five = [st for st in sts if st['class'] == '5']
            for st in five:
                p, left, top, bot = st['part'], float(st['x']), float(st['y']), float(st['b'])
                mid = (top + bot) / 2
                b0, b1 = band(st)
                mine = lambda kind: [r for r in rows[kind] if r['sys'] == sid and r['part'] == p]
                first = min([float(r['x']) for r in mine('head') + mine('rest') if float(r['x']) > ms[0][0]]
                            + [float(r['a']) - 1.5 * s for r in mine('acc') if float(r['x']) > ms[0][0]] or [ms[0][1]])
                head_end = first - 0.8 * s
                # the bars read right: open
                for k, (x0, x1, b) in enumerate(ms):
                    if bars.get((b, p)):
                        open_(head_end if k == 0 else x0 - 0.3 * s, b0, x1 + 0.6 * s, b1, 0)
                # its barlines (one at each measure's end, at the staff's middle), where a bar either side is right
                for k, (x0, x1, b) in enumerate(ms):
                    if not (bars.get((b, p)) or (k + 1 < len(ms) and bars.get((ms[k + 1][2], p)))): continue
                    c = nearest(cands, {'barline'}, x1 - 0.8 * s, mid - 1.5 * s, x1 + 0.8 * s, mid + 1.5 * s, x1, mid)
                    if c: label('barline', c[1], c[2], 'det')
                    elif k + 1 < len(ms): label('barline', x1, mid, 'reader')
                    else: ignore(x1 - 1.5 * s, mid - 3 * s, x1 + 1.5 * s, mid + 3 * s)
                # the clefs changed within the staff: the detectors' place, or none
                for g in mine('sig'):
                    if g['c'] == '1' and g['class'] in CLEF_CLASS:
                        gx = float(g['x'])
                        ok = any(bars.get((b, p)) for x0, x1, b in ms if x0 - 0.5 * s <= gx < x1)
                        k = nearest(cands, {CLEF_CLASS[g['class']]}, gx - 1 * s, top - 4 * s, gx + 4 * s, bot + 4 * s, gx + 1 * s, mid)
                        if ok and k: label(CLEF_CLASS[g['class']], k[1], k[2], 'det')
                        else: ignore(gx - 1.5 * s, top - 4 * s, gx + 4 * s, bot + 4 * s)
                    elif g['c'] == '0' and float(g['x']) > head_end + s:
                        # (a key or time changed within the staff: left out)
                        ignore(float(g['x']) - 1.5 * s, top - 3 * s, float(g['x']) + 8 * s, bot + 3 * s)
                # the system's start: where the first bar is right, its clef, key and time
                if not bars.get((ms[0][2], p)): continue
                sigs = [g for g in mine('sig') if g['c'] == '0' and float(g['x']) <= head_end + s]
                clef = next((g['class'] for g in sigs if g['class'] in CLEF_CLASS), None)
                if clef is None or head_end <= left + 2 * s: continue
                open_(left - 2 * s, b0, head_end, b1, 1)
                cl = CLEF_CLASS[clef]
                k = nearest(cands, {cl}, left - 1.5 * s, top - 3 * s, left + 5 * s, bot + 3 * s, left + 1.5 * s, mid)
                clef_end = left + 3.5 * s
                if k:
                    label(cl, k[1], k[2], 'det'); clef_end = k[1] + 1.5 * s
                elif cl in offset:
                    label(cl, left + offset[cl][0] * s, mid + offset[cl][1] * s, 'print'); clef_end = left + (offset[cl][0] + 1.5) * s
                else:
                    STATS[cl + ' lost'] += 1; ignore(left - 2 * s, top - 3 * s, left + 4 * s, bot + 3 * s)
                fifths = next((int(g['a']) for g in sigs if g['a'] not in ('-', '')), 0)
                # (a time printed on one staff of the system is printed on all: the reader may keep it on one)
                time = next((g['b'] for g in sigs if g['b'] not in ('-', '') and g['d'] == '0'), None) \
                    or next((g['b'] for g in rows['sig'] if g['sys'] == sid and g['c'] == '0' and g['b'] not in ('-', '') and g['d'] == '0'), None)
                # the key's signs, between the clef and the first note (the time's digits among them, if printed)
                want = 'sharp' if fifths > 0 else 'flat'
                got = []
                for c, cx, cy, sc in sorted(cands, key=lambda t: -t[3]):
                    if c == want and clef_end - 0.5 * s <= cx <= head_end and top - 1.5 * s <= cy <= bot + 1.5 * s and sc >= 0.1 \
                            and all(math.hypot(cx - gx, cy - gy) > 0.5 * s for gx, gy in got):
                        got.append((cx, cy))
                ok = len(got) == abs(fifths) or (fifths == 0 and not any(c in ('sharp', 'flat') and sc >= 0.3 and clef_end - 0.5 * s <= cx <= head_end and top - 1.5 * s <= cy <= bot + 1.5 * s for c, cx, cy, sc in cands))
                if ok and fifths:
                    for gx, gy in got: label(want, gx, gy, 'det')
                key_end = max([gx for gx, gy in got] + [clef_end]) + 0.6 * s if ok else clef_end
                if not ok:
                    STATS['key lost'] += 1; ignore(clef_end - 0.5 * s, top - 2 * s, head_end, bot + 2 * s); continue
                found = [(c, cx, cy) for c, cx, cy, sc in cands if (c.startswith('digit_') or c == 'timesig_c') and key_end - 0.3 * s <= cx <= head_end
                         and top - 1 * s <= cy <= bot + 1 * s and sc >= 0.1]
                if not time and found:
                    # (a time the reader kept on another staff only: the digits seen here are left out)
                    STATS['time lost'] += 1; ignore(key_end - 0.5 * s, top - 2 * s, head_end, bot + 2 * s)
                elif time:
                    digits = [CLASSES.index('digit_' + ch) for ch in time.replace('/', '') if ch in '123456789']
                    cs = [t for t in found if t[0] == 'timesig_c']
                    ds = [t for t in found if t[0].startswith('digit_')]
                    if cs and time in ('4/4', '2/2') and not ds:
                        label('timesig_c', cs[0][1], cs[0][2], 'det')
                    elif sorted(CI[t[0]] for t in ds) == sorted(digits) and not cs:
                        for c, cx, cy in ds: label(c, cx, cy, 'det')
                    else:
                        STATS['time lost'] += 1; ignore(key_end - 0.5 * s, top - 2 * s, head_end, bot + 2 * s)
        # the signs the detectors see in the bars read right that the reader did not take (a cautionary
        # accidental, a sign of the other voice's): left out, as the reader's reading says nothing of them
        for c, cx, cy, sc in cands:
            if c in SIGNS and sc >= 0.3 and not any(t[0] in [CI[z] for z in SIGNS] and math.hypot(t[1] / scale - cx, t[2] / scale - cy) < 0.6 * s for t in targets):
                ignore(cx - 0.8 * s, cy - 1.2 * s, cx + 0.8 * s, cy + 1.2 * s)
        name = '%s/%s' % (os.path.basename(d.rstrip('\\/')), os.path.basename(tsv)[5:8])
        out.append((img, np.array(targets, dtype=np.float32).reshape(-1, 3), np.array(regions, dtype=np.float32).reshape(-1, 5), name))
        print('  %s: %d labels, %d regions' % (name, len(targets), len(regions)), flush=True)
    return out


if __name__ == '__main__':
    out, models, dirs = sys.argv[1], sys.argv[2].split(','), sys.argv[3:]
    dev = 'cuda' if torch.cuda.is_available() else 'cpu'
    nets = []
    for m in models:
        net = Detector().to(dev); net.load_state_dict(torch.load(m, map_location=dev)); net.eval(); nets.append(net)
    pages = []
    for d in dirs:
        pages += build_set(d, nets, dev)

    def ragged(items):
        a = np.empty(len(items), dtype=object)
        for i, it in enumerate(items):
            a[i] = it
        return a
    np.savez_compressed(out, images=ragged([p[0] for p in pages]), targets=ragged([p[1] for p in pages]),
                        regions=ragged([p[2] for p in pages]), names=np.array([p[3] for p in pages]))
    print('%d pages, %d labels -> %s' % (len(pages), sum(len(p[1]) for p in pages), out))
    for k in sorted(STATS):
        print('  %-24s %6d' % (k, STATS[k]))
