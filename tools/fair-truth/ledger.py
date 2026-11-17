import sys
from PIL import Image
# ledger.py <page.png> <y0> <y1> <x> [<x> ...]: for a head centred near each x, the rows of the lines that run on BOTH
# sides of it (staff or ledger lines: dark 14-20 px left and right of the head) and the head's own dark rows at x, so a
# head on or between ledger lines is named by counting lines, not by its measured centre (heads above ledger lines are
# engraved high in old prints: the centre alone reads a step too high).
g = Image.open(sys.argv[1]).convert('L'); px = g.load(); y0, y1 = int(sys.argv[2]), int(sys.argv[3])
for a in sys.argv[4:]:
    X = int(a)
    # the head's horizontal extent at its darkest rows
    col = [y for y in range(y0, y1) if px[X, y] < 128]
    rows = []
    for y in range(y0, y1):
        l = sum(1 for x in range(X - 22, X - 15) if px[x, y] < 128) >= 5
        r = sum(1 for x in range(X + 15, X + 22) if px[x, y] < 128) >= 5
        if l and r: rows.append(y)
    lines = []
    for y in rows:
        if lines and y - lines[-1][-1] <= 2: lines[-1].append(y)
        else: lines.append([y])
    runs = []; s = None
    for y in range(y0, y1):
        b = px[X, y] < 128
        if b and s is None: s = y
        if not b and s is not None: runs.append((s, y - 1)); s = None
    print(f'x {X}: lines {[round(sum(l) / len(l)) for l in lines]} | runs at x {runs}')
