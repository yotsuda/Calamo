import sys
from PIL import Image
# col.py <page.png> <y0> <y1> <x> [<x> ...]: for each x, the staff (and ledger) lines found in a clear column near it
# (rows dark across 12 px, looked for 25-60 px to either side) and the dark runs down the column at x itself -- to settle
# a head's step by measuring it against its own staff where the page is bowed and ruled.py's fitted lines drift.
g = Image.open(sys.argv[1]).convert('L'); px = g.load(); y0, y1 = int(sys.argv[2]), int(sys.argv[3])
def runs(X):
    r = []; s = None
    for y in range(y0, y1):
        b = px[X, y] < 128
        if b and s is None: s = y
        if not b and s is not None: r.append((s, y - 1)); s = None
    return r
def lines(X):
    best = []
    for d in (-60, -45, -30, 25, 40, 55):
        rows = [y for y in range(y0, y1) if all(px[x, y] < 140 for x in range(X + d, X + d + 12))]
        ls = []
        for y in rows:
            if ls and y - ls[-1][-1] <= 2: ls[-1].append(y)
            else: ls.append([y])
        c = [round(sum(l) / len(l)) for l in ls if len(l) <= 5]
        if len(c) > len(best): best = c
    return best
for a in sys.argv[4:]:
    X = int(a); L = lines(X)
    heads = [(s, e) for s, e in runs(X) if e - s >= 9]
    out = []
    for s, e in heads:
        c = (s + e) / 2
        if len(L) >= 2:
            sp = sorted(L[i + 1] - L[i] for i in range(len(L) - 1))[0]
            # step of the head's centre counted from the top line, in half spaces (0 = top line, 8 = bottom of 5)
            out.append(f'{s}-{e} c{c:.0f} k{(c - L[0]) / (sp / 2):+.1f}')
        else: out.append(f'{s}-{e}')
    print(f'x {X}: lines {L} | heads {out}')
