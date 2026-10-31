"""Sum a sets table (out/audiveris/eval-sets.tsv, dev-sets.tsv, or a copy under out/pseudo2) by the kind of
page, and compare two: the way two detectors are compared on the real sets (ROADMAP 7.4; run-audiveris's
better/worse at the end counts sets, not notes).

python setsum.py <sets.tsv> [<other.tsv>]        (from tools/train-model)
e.g. python setsum.py ..\\..\\out\\pseudo2\\d45-eval-sets.tsv ..\\..\\docs\\eval-sets.tsv

Kinds: scan (a set whose name has -scan), ms (a name ending in -ms: a song's MuseScore engraving, the twin of
its scan), other (the rest: the dl-ms-NN pieces, Mutopia, Dorico, the band scores). For each: the
notes found, the ties found and wrong (tieOmr - tieRight), the slur starts found and wrong. With a second
table, its sums and the first's minus them, then the sets whose notes found differ most.
"""
import csv, sys
from collections import defaultdict

COLS = ['found', 'tieFound', 'tieWrong', 'slurStartFound', 'slurWrong']


def kind(name):
    return 'scan' if '-scan' in name else 'ms' if name.endswith('-ms') else 'other'


def load(path):
    rows = {}
    with open(path, newline='', encoding='utf-8') as f:
        for r in csv.DictReader(f, delimiter='\t'):
            v = {k: int(r[k] or 0) for k in r if k != 'set'}
            v['tieWrong'] = v.get('tieOmr', 0) - v.get('tieRight', 0)
            v['slurWrong'] = v.get('slurStartOmr', 0) - v.get('slurStartRight', 0)
            rows[r['set']] = v
    return rows


def sums(rows):
    t = defaultdict(lambda: defaultdict(int))
    for name, v in rows.items():
        for c in COLS:
            t[kind(name)][c] += v.get(c, 0)
    return t


def show(title, t, ref=None):
    print(title)
    print('%-6s' % '' + ''.join('%16s' % c for c in COLS))
    for k in ('scan', 'ms', 'other'):
        line = '%-6s' % k
        for c in COLS:
            line += '%16s' % (str(t[k][c]) if ref is None else '%d (%+d)' % (t[k][c], t[k][c] - ref[k][c]))
        print(line)


a = load(sys.argv[1])
ta = sums(a)
if len(sys.argv) < 3:
    show(sys.argv[1], ta)
else:
    b = load(sys.argv[2])
    tb = sums(b)
    show(sys.argv[2], tb)
    show(sys.argv[1] + '  (and its difference from the above)', ta, tb)
    d = [(a[n]['found'] - b[n]['found'], n) for n in a if n in b and a[n]['found'] != b[n]['found']]
    d.sort()
    print('sets whose notes found differ most (first minus second):')
    for v, n in d[:8] + ([('...', '')] if len(d) > 16 else []) + d[-8:] if len(d) > 16 else d:
        print('  %+d  %s' % (v, n) if v != '...' else '  ...')
