"""Sum up slurcount.py's table: how surely each detector sees the two ends of the development set's scan slurs.

python slurtab.py <slurcount out.tsv>        (from tools/train-model)

Over the placed slurs (both ends at a chord read, on one system), per detector: slurs with both ends scored
0.5 and more, both 0.25 and more, one 0.5 and the other 0.2 and more (the reader's sure end and weak end,
Detector.WeakSlur), the mean of the end scores (an end not seen counts 0), and the ends scored 0.5 and more
(HANDOFF 2026-11-01's table).
"""
import csv, sys

rows = list(csv.DictReader(open(sys.argv[1], newline='', encoding='utf-8'), delimiter='\t'))
placed = [r for r in rows if r['kind'] not in ('unplaced', 'across systems')]
models = sorted({k[:-2] for k in rows[0] if k.endswith('.l')}, key=lambda m: (len(m), m))
print('%d slurs, %d placed' % (len(rows), len(placed)))
print('%-8s %12s %13s %22s %10s %10s' % ('detector', 'both >= 0.5', 'both >= 0.25', 'one 0.5, other 0.2', 'mean end', 'ends 0.5+'))


def score(v):
    return float(v.split(':')[1]) if v and v != '-' else 0.0


for m in models:
    ls = [score(r[m + '.l']) for r in placed]
    rs = [score(r[m + '.r']) for r in placed]
    both5 = sum(1 for l, r in zip(ls, rs) if l >= 0.5 and r >= 0.5)
    both25 = sum(1 for l, r in zip(ls, rs) if l >= 0.25 and r >= 0.25)
    mixed = sum(1 for l, r in zip(ls, rs) if (l >= 0.5 and r >= 0.2) or (r >= 0.5 and l >= 0.2))
    mean = (sum(ls) + sum(rs)) / max(1, 2 * len(placed))
    ends5 = sum(1 for v in ls + rs if v >= 0.5)
    print('%-8s %12d %13d %22d %10.3f %10d' % (m, both5, both25, mixed, mean, ends5))
