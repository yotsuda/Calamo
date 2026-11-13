import sys
from PIL import Image
# lines.py <image> <x0> <x1> [y0 y1]: the staff lines of a page between x0 and x1 (rows dark across 70% of the width),
# printed as y and grouped in fives -- to name the pitch of a head from its y when writing the truth by eye.
im = Image.open(sys.argv[1]).convert('L'); x0, x1 = int(sys.argv[2]), int(sys.argv[3])
y0, y1 = (int(sys.argv[4]), int(sys.argv[5])) if len(sys.argv) > 5 else (0, im.height)
w = x1 - x0; px = im.load(); rows = []
for y in range(y0, y1):
    if sum(1 for x in range(x0, x1) if px[x, y] < 128) > 0.7 * w: rows.append(y)
lines = []
for y in rows:
    if lines and y - lines[-1][-1] <= 1: lines[-1].append(y)
    else: lines.append([y])
mids = [sum(l) / len(l) for l in lines]
for i in range(0, len(mids), 5): print(' '.join(f'{m:.0f}' for m in mids[i:i + 5]))
