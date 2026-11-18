import os
# staves.py: the staves of a printed page, found from the page's pixels alone (never from a reader's output) -- shared by
# ruled.py (pitch names beside the staves) and barboxes.py (the bar boxes of a truth's side file). For the fair sets'
# truths only, never to debug the reader.
#
# staves(g) -> list of staves top to bottom; each staff is five lines, each line (y at x = 0, slope in px per px), so
# a line's y at x is y0 + slope * x. at(line, x) gives it.

def at(line, x):
    return line[0] + line[1] * x

def staves(g, dark=None):
    """g: the page as a PIL 'L' image. dark: the cut for a dark pixel (RULED_DARK, default 128; a pale print needs
    a lighter one, e.g. 175 for Taubert Op.136-1 page 2)."""
    if dark is None:
        dark = int(os.environ.get('RULED_DARK', '128'))
    # the share of a window's width a staff line's row must be dark over, counted every other pixel (so 0.25 is half
    # the samples). RULED_ROW: a thin, broken print needs less (Wuerst Op.52-3's 1500 px wide scan: 0.18 with dark 175)
    row = float(os.environ.get('RULED_ROW', '0.25'))
    px = g.load(); W, H = g.size

    def find_staves(cx0, cx1):  # in the window cx0..cx1: rows dark across a quarter of it, merged, then runs of five evenly spaced
        w = cx1 - cx0; rows = []
        for y in range(H):
            if sum(1 for x in range(cx0, cx1, 2) if px[x, y] < dark) > row * w: rows.append(y)
        lines = []
        for y in rows:
            if lines and y - lines[-1][-1] <= 3: lines[-1].append(y)
            else: lines.append([y])
        ys = [sum(l) / len(l) for l in lines]; hs = [len(l) for l in lines]; out = []; i = 0
        while i + 4 < len(ys):
            gp = [ys[i + k + 1] - ys[i + k] for k in range(4)]
            h = sorted(hs[i:i + 5])  # a beam across the window is a row run several lines thick: not a staff line
            if 6 < min(gp) and max(gp) < 1.25 * min(gp) and h[4] <= 2 * h[2] + 1: out.append(ys[i:i + 5]); i += 5
            else: i += 1
        return out

    # a tilted or bowed page: the staves found in five windows, matched to the middle one's, each line fitted along x
    wins = [(W * a // 20, min(W, W * a // 20 + 300)) for a in (2, 6, 10, 14, 17)]
    found = [find_staves(a, b) for a, b in wins]; xs = [(a + b) / 2 for a, b in wins]
    # the staves of all windows pooled by y lead: any window can lose a staff to a hairpin, a beam or a run of chords, and
    # on a crowded page no single window finds them all (Lack's page 2: 6, 7, 7, 8, 4 of 10)
    leads = []
    for s in sorted((s for f in found for s in f), key=lambda s: s[0]):
        if leads and s[0] - leads[-1][-1][0] < 0.75 * (s[4] - s[0]): leads[-1].append(s)
        else: leads.append([s])
    fitted = []
    for st in (gr[len(gr) // 2] for gr in leads):
        pts = []
        for f, x in zip(found, xs):
            near = [s for s in f if abs(s[0] - st[0]) < 2 * (st[4] - st[0]) / 4 * 3]
            if near: pts.append((x, min(near, key=lambda s: abs(s[0] - st[0]))))
        # robust to a window that caught the wrong rows: the median of the pairwise slopes of the middle line, then
        # each line's intercept the median over the windows
        pair = sorted((b[1][2] - a[1][2]) / (b[0] - a[0]) for i, a in enumerate(pts) for b in pts[i + 1:])
        sl = pair[len(pair) // 2] if pair else 0.0
        def fit(k):
            c = sorted(s[k] - sl * x for x, s in pts); return (c[len(c) // 2], sl)
        fitted.append(([fit(k) for k in range(5)], len(pts), pts))
    # a staff seen in fewer than three windows (words, beams, chords over its lines) takes the page's median tilt
    good = [s for s in fitted if s[1] >= 3]
    med = sorted(st[0][0][1] for st in good)[len(good) // 2] if good else 0.0
    out = []
    for i, (lines, n, pts) in enumerate(fitted):
        sl = lines[0][1]
        nb = [fitted[j][0][0][1] for j in (i - 1, i + 1) if 0 <= j < len(fitted) and fitted[j][1] >= 3]
        if n < 3:
            out.append([(y + (s - med) * xs[2], med) for y, s in lines])
        elif len(nb) == 2 and abs(nb[0] - nb[1]) < 0.004 and all(abs(sl - b) > 0.004 for b in nb):
            # a tilt far off both its neighbours' while they agree (Kücken Op.47-3 page 1, system 2: 21.9 px per 1000
            # between 0.7 and 2.0): the windows caught other rows; refit each line with the neighbours' tilt. A bowed
            # page's tilt changes staff by staff (Krause page 2: -2 to 13) and is left alone.
            t = (nb[0] + nb[1]) / 2
            out.append([(sorted(s[k] - t * x for x, s in pts)[len(pts) // 2], t) for k in range(5)])
        else:
            out.append(lines)
    # two leads can fit to the same staff (Kücken Op.47-3 page 6, system 4: tops 5683 and 5683): keep the first
    dedup = []
    for st in out:
        if dedup and abs(at(st[0], W / 2) - at(dedup[-1][0], W / 2)) < 0.5 * (at(st[4], W / 2) - at(st[0], W / 2)):
            continue
        dedup.append(st)
    return dedup
