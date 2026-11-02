"""How sure the detector is, against how often it is right (S53): every peak a model finds down to a low
score on pages it never learnt from (not seed 1's nor seed 7's), each marked a hit when it takes a true
symbol of its class within half a space (the surest first), and per class the hit rate as the score goes.

  python calibrate.py curves <model.pt> <set.npz>[,<set.npz>...] <out.tsv>
        one row per peak: class, score, hit (the raw material, for any curve fitted later)
  python calibrate.py table <out.tsv> [<out.tsv> ...]
        per class, each model's hit rate in score bins (hits / peaks)
  python calibrate.py fit <ref.tsv>[,...] <model.tsv>[,...] <model.calib.tsv>
        the map from a model's scores to the reference's (detector42's, which the rules' thresholds
        were set on): per class, the score over which the reference finds as many peaks.

The hit rate alone cannot be the map: over 0.8 every model is right nearly always, while how many of
the true symbols each puts over 0.9 or 0.95 (Symbols.SureScore's side) differs most (cal-scan's black
heads over 0.95: detector42 3042, detector47 2349).

S53 (2026-11-02) tried the map in the reader (Detector.Peaks reading each class's scores through it) and
took it out again: the maps of detector47, 48 and 49 to 42 are all within a few hundredths of the
identity, and read through them the evaluation set's notes moved by -32, -30 and +1 (49, 42's making
with another seed, stays 181 under 42). The models differ in which symbols they are sure of, not in
their scale: a map of the scores cannot undo that. Only the articulations came nearer 42 (+40, +50).
"""
import sys, math, csv
import numpy as np
import torch
from dataset import CLASSES, SPACE
from train import Detector, detect_page, load

LOW = 0.05     # peaks kept down to this score (every Weak* threshold of the reader is above it)


def curves(model, npz, out):
    dev = 'cuda' if torch.cuda.is_available() else 'cpu'
    net = Detector().to(dev); net.load_state_dict(torch.load(model, map_location=dev)); net.eval()
    images, targets, names = load(npz)
    with open(out, 'w', newline='') as f:
        w = csv.writer(f, delimiter='\t')
        w.writerow(['page', 'class', 'score', 'hit'])
        for img, tg, name in zip(images, targets, names):
            found = sorted(detect_page(net, img, dev, LOW), key=lambda d: -d[3])
            truth = [(int(c), cx, cy) for c, cx, cy in tg]
            used = [False] * len(truth)
            for c, fx, fy, score in found:
                best, bd = -1, 0.5 * SPACE
                for i, (tc, tx, ty) in enumerate(truth):
                    if used[i] or tc != c: continue
                    d = math.hypot(fx - tx, fy - ty)
                    if d < bd: bd, best = d, i
                if best >= 0: used[best] = True
                w.writerow([name, CLASSES[c], '%.4f' % score, 1 if best >= 0 else 0])
            # (a true symbol no peak took: score 0, for the recall)
            for i, (tc, _, _) in enumerate(truth):
                if not used[i]: w.writerow([name, CLASSES[tc], '0', 1])


def read(path):
    by = {}
    with open(path) as f:
        for r in csv.DictReader(f, delimiter='\t'):
            by.setdefault(r['class'], []).append((float(r['score']), int(r['hit'])))
    return by


def peak_scores(paths):
    """Per class, the scores of the peaks (right or wrong; not the true symbols no peak took), over the
    curves of several sets, and how many true symbols there were."""
    by, truth = {}, {}
    for p in paths:
        for c, rows in read(p).items():
            by.setdefault(c, []).extend(s for s, h in rows if s > 0)
            truth[c] = truth.get(c, 0) + sum(h for s, h in rows)
    return {c: np.sort(np.array(v)) for c, v in by.items()}, truth


# the model's scores the map is written at (the reader interpolates between them)
KNOTS = [0.05, 0.08, 0.1, 0.12, 0.15, 0.2, 0.25, 0.3, 0.35, 0.4, 0.45, 0.5, 0.55, 0.6, 0.65, 0.7, 0.75, 0.8, 0.85, 0.9, 0.93, 0.95, 0.97, 0.98, 0.99]
MIN_TRUTH = 200     # a class with fewer true symbols on the sets keeps its scores as they are
BAND, NEAR = 0.05, 40   # a knot moves fully with NEAR peaks within BAND of it in both models' scores


def fit(ref, model, out):
    """A model's score v -> the reference's score over which the reference finds as many peaks of the
    class as the model finds over v (the same pages read by both). The rules' thresholds were set on
    the reference's scores; read through the map, a threshold passes as many peaks with every model.
    High up the peaks are nearly all true symbols, so a sure threshold keeps as many of them; low down
    they are mostly not, so a weak one lets in as much dirt. (Matching the true symbols alone lifted
    dirt: few true symbols are seen low, and an eighth rest's 0.06 came out 0.69.)"""
    (r, rt), (m, mt) = peak_scores(ref), peak_scores(model)
    with open(out, 'w', newline='') as f:
        f.write('# class, then raw:mapped knots (calibrate.py fit)\n')
        for c in CLASSES[1:]:
            if c not in r or c not in m or rt.get(c, 0) < MIN_TRUTH or rt[c] != mt.get(c): continue
            rs, ms = r[c], m[c]
            knots, last = [], LOW
            for v in KNOTS:
                q = int((ms >= v).sum())                       # the model's peaks at v or more
                # the reference's q-th highest score (none so high: 1; fewer peaks in all: as low as kept)
                u = 1.0 if q == 0 else float(rs[len(rs) - q]) if q <= len(rs) else LOW
                # (where either has few peaks near, the count is a handful's and jumps over a gap in the
                # other's scores -- an eighth rest's 0.40 came out 0.69 -- so the map keeps to v there)
                near = min(int((abs(ms - v) < BAND).sum()), int((abs(rs - u) < BAND).sum()))
                u = v + (u - v) * min(1.0, near / NEAR)
                u = max(u, last)
                last = u
                knots.append('%.3f:%.4f' % (v, u))
            f.write(c + '\t' + ' '.join(knots) + '\n')


BINS = [0.05, 0.1, 0.15, 0.2, 0.25, 0.3, 0.4, 0.5, 0.6, 0.7, 0.8, 0.9, 0.95, 1.0001]


def table(paths):
    models = [read(p) for p in paths]
    for c in CLASSES[1:]:
        if not any(c in m for m in models): continue
        print('%s' % c)
        for p, m in zip(paths, models):
            rows = m.get(c, [])
            n = sum(1 for s, h in rows if h)
            cells = []
            for lo, hi in zip(BINS, BINS[1:]):
                hits = [h for s, h in rows if lo <= s < hi]
                cells.append('%3d/%-4d' % (round(100 * sum(hits) / len(hits)), len(hits)) if hits else '   -    ')
            print('  %-14s truth %5d  %s' % (p.split('\\')[-1].split('/')[-1][:14], n, ' '.join(cells)))
    print('bins from ' + ' '.join('%.2f' % b for b in BINS[:-1]))


if __name__ == '__main__':
    if sys.argv[1] == 'curves': curves(sys.argv[2], sys.argv[3], sys.argv[4])
    elif sys.argv[1] == 'table': table(sys.argv[2:])
    elif sys.argv[1] == 'fit': fit(sys.argv[2].split(','), sys.argv[3].split(','), sys.argv[4])
