import argparse, json, os, sys
from PIL import Image, ImageDraw, ImageFont
from staves import staves as find_staves, at
# barboxes.py: the bar boxes of a printed score, from the page's pixels alone -- staves by staves.py, systems by the line
# that joins their staves at the left, barlines as thin vertical lines through every staff of a system at one x -- and the
# side file Lily#'s Show Original Scan reads beside a copy of a truth (ROADMAP §0 "正解の台の作り方"): `version` 1, `pages`,
# `measures` (one per staff per bar: `measure`, `part`, `page`, `box`, `staff`), `todos` (the spots the owner is asked to
# look at, from --todos). NEVER made from a reader's output, so a reserve set stays unread. The bars are numbered in page
# order from --first; check the count per system against the truth (--expect) -- a pickup, a short bar before a repeat
# and the like are counted as the print draws them.
#
#   barboxes.py --pages p01.png p02.png ... --parts voice_ rh lh --out x.omr.json [--expect 5,6,5,7/...] [--todos t.json]
#               [--draw DIR] [--first 1] [--add-staff PAGE:Y ...]
#
# A system printed with fewer staves than --parts (a voice part removeEmpty hides) takes the LAST parts.
# --todos: a JSON list of {"key", "measure", "part", "memo", "candidates": [{"label", "text"}], optional "box": [l,t,r,b]
# on that bar's page}; a todo without a box is framed by its bar's box (its part's, or the whole system's bar without one).
# --add-staff PAGE:Y adds a staff staves.py missed on that page (Y = its top line's y at the page's middle; the spacing and
# tilt are taken from the nearest staff found).

ap = argparse.ArgumentParser()
ap.add_argument('--pages', nargs='+', required=True)
ap.add_argument('--parts', nargs='+', required=True)
ap.add_argument('--out', required=True)
ap.add_argument('--first', type=int, default=1)
ap.add_argument('--expect', default='')
ap.add_argument('--todos', default='')
ap.add_argument('--draw', default='')
ap.add_argument('--add-staff', nargs='*', default=[])
ap.add_argument('--truth', default='', help="the truth's .lys: --expect from its breaks (bars per system per page)")
ap.add_argument('--truth-part', default='', help='the part whose breaks count (default: the last of --parts)')
a = ap.parse_args()

CUT = int(os.environ.get('RULED_DARK', '128'))  # the cut for a dark pixel, the same as staves.py's (a pale print needs more)

def dark_at(px, W, H, x, y, cut=CUT):
    x, y = int(round(x)), int(round(y))
    return 0 <= x < W and 0 <= y < H and px[x, y] < cut

def staff_ends(px, W, H, st):
    """The x range where the staff's lines run: columns where at least four of its five lines are dark (within 2 px)."""
    sp = (at(st[4], W / 2) - at(st[0], W / 2)) / 4
    on = []
    for x in range(W):
        n = 0
        for ln in st:
            y = at(ln, x)
            if any(dark_at(px, W, H, x, y + d) for d in (-3, -2, -1, 0, 1, 2, 3)): n += 1
        on.append(n >= 3)
    best, run, start = (0, 0), 0, 0  # the longest run, allowing gaps of up to four spaces (white heads, a chord's ink)
    gap = 0
    for x in range(W):
        if on[x]:
            if run == 0: start = x
            run = x - start + 1; gap = 0
            if run > best[1] - best[0]: best = (start, x)
        elif run:
            gap += 1
            if gap > 4 * sp: run = 0; gap = 0
    return best

def column_fill(px, W, H, x, y0, y1):
    n = sum(1 for y in range(int(y0), int(y1) + 1) if dark_at(px, W, H, x, y))
    return n / max(1, int(y1) - int(y0) + 1)

def joined(px, W, H, upper, lower, xa, xb):
    """A vertical line joins the two staves somewhere in xa..xb: the system's start line (or a grand staff's barline)."""
    for x in range(max(0, xa), min(W, xb)):
        y0 = at(upper[4], x); y1 = at(lower[0], x)
        # a line not quite vertical: the best of x-1..x+1 at each row
        n = sum(1 for y in range(int(y0), int(y1) + 1) if any(dark_at(px, W, H, x + d, y) for d in (-1, 0, 1)))
        if n / max(1, int(y1) - int(y0) + 1) >= 0.85: return True
    return False

def truth_systems(path, part):
    """Bars per system, systems per page, as the truth's .lys breaks them: the part's blocks in file order, `|` ends a
    bar, `break` a system, `pageBreak` a system and a page. A pickup or a short bar is a bar of its own."""
    text = open(path, encoding='utf-8').read()
    text = '\n'.join(l.split('//')[0] for l in text.splitlines())
    pages, systems, bars = [], [], 0
    i = 0
    import re
    for m in re.finditer(r'(?m)^\s*' + re.escape(part) + r'\s*\{', text):
        depth, j = 1, m.end()
        while depth and j < len(text):
            depth += {'{': 1, '}': -1}.get(text[j], 0); j += 1
        for tok in text[m.end():j - 1].split():
            if tok in ('|', '|.', '||', ':|', '|:', ':|:'): bars += 1
            elif tok == 'break': systems.append(bars); bars = 0
            elif tok == 'pageBreak': systems.append(bars); bars = 0; pages.append(systems); systems = []
    if bars: systems.append(bars)
    if systems: pages.append(systems)
    return pages

def barlines(px, W, H, sys_staves, xa, xb, sp):
    """Runs of x where every staff of the system is dark from its top line to its bottom line; runs closer than 1.5
    spaces merged (a double or final barline). Each run (x0, x1)."""
    slant = (-2, -1, 0, 1, 2)  # a line a bowed page leans (Krause page 6: bars 116 and 119) is found row by row x-2..x+2
    def through(x, st):  # dark down the staff, a quarter space in from its outer lines
        y0 = int(at(st[0], x) + sp / 4); y1 = int(at(st[4], x) - sp / 4)
        n = sum(1 for y in range(y0, y1 + 1) if any(dark_at(px, W, H, x + d, y) for d in slant))
        return n / max(1, y1 - y0 + 1) >= 0.95
    def group(xs):
        runs = []
        for x in xs:
            if runs and x - runs[-1][1] <= 1: runs[-1][1] = x
            else: runs.append([x, x])
        return runs
    def gap(x, up, dn):
        y0 = int(at(up[4], x)); y1 = int(at(dn[0], x))
        n = sum(1 for y in range(y0, y1 + 1) if any(dark_at(px, W, H, x + d, y) for d in slant))
        return n / max(1, y1 - y0 + 1) >= 0.6  # a faint print breaks a line in the gap (Kücken page 6, bar 67: 0.63)
    ok = [[through(x, st) for x in range(xa, xb)] for st in sys_staves]  # ok[staff][x - xa]
    def lines_of(staves_idx):  # runs where every staff named is crossed at the same x; wider is ink, not a line
        xs = [xa + k for k in range(xb - xa) if all(ok[i][k] for i in staves_idx)]
        return [q for q in group(xs) if q[1] - q[0] + 1 <= 0.8 * sp]
    # staves ruled together (a grand staff): at least two lines through both also run solid through the gap between
    # them (a count, not a share: where the hands' stems often meet, they outnumber the barlines -- Kücken page 2).
    # There a line must cross both at one x and the gap too -- else it is two stems that happen to meet (Krause page 2:
    # a chord's stem down in the right hand over one up in the left)
    def solid(x, up, dn):
        y0 = int(at(up[4], x)); y1 = int(at(dn[0], x))
        n = sum(1 for y in range(y0, y1 + 1) if any(dark_at(px, W, H, x + d, y) for d in slant))
        return n / max(1, y1 - y0 + 1) >= 0.9
    groups = [[0]]
    for i in range(1, len(sys_staves)):
        both = lines_of([i - 1, i])
        crossing = [q for q in both if any(solid(x, sys_staves[i - 1], sys_staves[i]) for x in range(q[0], q[1] + 1))]
        if len(crossing) >= 2: groups[-1].append(i)
        else: groups.append([i])
    def clear(x, y0, y1):  # mostly white from y0 to y1 at x-1..x+1
        n = sum(1 for y in range(int(y0), int(y1) + 1) if any(dark_at(px, W, H, x + d, y) for d in (-1, 0, 1)))
        return n / max(1, int(y1) - int(y0) + 1) <= 0.5
    def ends_at_staff(q, top, bot):  # a barline stops at the lines; stems that meet run on to beams or heads on both
        # sides (Kücken page 5, bar 60: both hands' tremolo stems at one x, crossing the gap too). One side clear is
        # enough: a note on ledger lines can stand right after a barline (Kücken page 2, bar 14)
        return any(clear(x, at(top[0], x) - 1.5 * sp, at(top[0], x) - 0.5 * sp) or
                   clear(x, at(bot[4], x) + 0.5 * sp, at(bot[4], x) + 1.5 * sp) for x in range(q[0], q[1] + 1))
    per_group = []
    for g_ in groups:
        runs_g = lines_of(g_)
        for i in g_[1:]:
            runs_g = [q for q in runs_g if any(gap(x, sys_staves[i - 1], sys_staves[i]) for x in range(q[0], q[1] + 1))]
        if os.environ.get('BARBOXES_DEBUG'):
            print(f'    group {g_}: lines {runs_g}, ending at the staff {[q for q in runs_g if ends_at_staff(q, sys_staves[g_[0]], sys_staves[g_[-1]])]}')
        runs_g = [q for q in runs_g if ends_at_staff(q, sys_staves[g_[0]], sys_staves[g_[-1]])]
        per_group.append(runs_g)
    # across groups ruled apart (a voice staff over the piano's), a line in each within an eighth of a space: the
    # voice's barline is ruled on its own and can stand a few pixels off the piano's (Kücken page 3: 3 px)
    r = max(3, int(sp / 8))
    runs = per_group[-1]
    for other in per_group[:-1]:
        runs = [[min(q[0], o[0]), max(q[1], o[1])] for q in runs for o in other if o[0] - r <= q[1] and q[0] <= o[1] + r]
    runs.sort()
    merged = []
    for r in runs:
        if merged and r[0] - merged[-1][1] <= 1.5 * sp: merged[-1][1] = r[1]
        else: merged.append(list(r))
    return merged

expect = [[int(n) for n in p.split(',') if n] for p in a.expect.split('/')] if a.expect else []
if a.truth:
    expect = truth_systems(a.truth, a.truth_part or a.parts[-1])
    print('truth: bars per system', expect, 'total', sum(map(sum, expect)))
adds = {}
for s in a.add_staff:
    p, y = s.split(':'); adds.setdefault(int(p), []).append(float(y))

side = {'version': 1, 'pages': [], 'measures': [], 'todos': []}
outdir = os.path.dirname(os.path.abspath(a.out))
measure = a.first
problems = []
for pi, path in enumerate(a.pages, start=1):
    im = Image.open(path); g = im.convert('L'); px = g.load(); W, H = g.size
    st = find_staves(g)
    for y in adds.get(pi, []):  # a staff staves.py missed: spacing and tilt from the nearest one found
        near = min(st, key=lambda s: abs(at(s[0], W / 2) - y))
        sl = near[0][1]; spc = (at(near[4], W / 2) - at(near[0], W / 2)) / 4
        st.append([(y + k * spc - sl * W / 2, sl) for k in range(5)])
        st.sort(key=lambda s: at(s[0], W / 2))
    # a cue-sized staff (an ossia above a system, Wuerst Op.52-3 page 4: spacing 7.8 among 9.6-9.9) has no bars of the
    # truth's parts: dropped
    spc = sorted((at(s[4], W / 2) - at(s[0], W / 2)) / 4 for s in st)
    if spc:
        med = spc[len(spc) // 2]
        st = [s for s in st if (at(s[4], W / 2) - at(s[0], W / 2)) / 4 > 0.85 * med]
    ends = [staff_ends(px, W, H, s) for s in st]
    sps = [(at(s[4], W / 2) - at(s[0], W / 2)) / 4 for s in st]
    # systems: staves joined at their left end
    systems = [[0]]
    for i in range(1, len(st)):
        # the window spans both staves' left ends (a brace or a clef can move one of them) and two spaces past
        xa = min(ends[i - 1][0], ends[i][0]) - int(2 * sps[i]); xb = max(ends[i - 1][0], ends[i][0]) + int(2 * sps[i])
        if joined(px, W, H, st[i - 1], st[i], xa, xb): systems[-1].append(i)
        else: systems.append([i])
    rel = os.path.relpath(os.path.abspath(path), outdir)
    side['pages'].append({'page': pi, 'image': rel.replace('\\', '/'), 'width': W, 'height': H})
    draw = None
    if a.draw:
        ov = im.convert('RGB'); draw = ImageDraw.Draw(ov)
        try: font = ImageFont.truetype('arial.ttf', 40)
        except OSError: font = ImageFont.load_default()
    counts = []
    for si, idx in enumerate(systems):
        ss = [st[i] for i in idx]; sp = sum(sps[i] for i in idx) / len(idx)
        left = min(ends[i][0] for i in idx); right = max(ends[i][1] for i in idx)  # a staff's run can be cut short
        bl = barlines(px, W, H, ss, left, right + 1, sp)
        # the start line (within three spaces of where the lines begin; the brace can stretch that leftward) is the
        # system's edge, not a barline
        while bl and bl[0][0] <= left + 3 * sp:
            left = bl[0][1]; bl = bl[1:]
        edges = [(left, left)] + [tuple(b) for b in bl]
        if right - edges[-1][1] > 2 * sp: edges.append((right, right))  # a system that ends without a barline
        nbars = len(edges) - 1; counts.append(nbars)
        if os.environ.get('BARBOXES_DEBUG'):
            print(f'  page {pi} system {si + 1}: lines {left}..{right}, space {sp:.1f}, barlines {edges[1:]}')
        parts = a.parts[len(a.parts) - len(idx):] if len(idx) <= len(a.parts) else a.parts + [f'?{k}' for k in range(len(idx) - len(a.parts))]
        if len(idx) != len(a.parts):
            problems.append(f'page {pi} system {si + 1}: {len(idx)} staves for {len(a.parts)} parts -> {" ".join(parts)}')
        for b in range(nbars):
            x0 = edges[b][1] + 1; x1 = edges[b + 1][0] - 1; xm = (x0 + x1) / 2
            for k, (i, part) in enumerate(zip(idx, parts)):
                s = st[i]; top = at(s[0], xm); bot = at(s[4], xm)
                up = at(st[idx[k - 1]][4], xm) if k > 0 else top - 8 * sp
                dn = at(st[idx[k + 1]][0], xm) if k + 1 < len(idx) else bot + 8 * sp
                bt = max((top + up) / 2, top - 3 * sp); bb = min((bot + dn) / 2, bot + 3 * sp)
                side['measures'].append({'measure': measure, 'part': part, 'page': pi,
                                         'box': [int(x0), int(bt), int(x1), int(bb)],
                                         'staff': [int(x0), int(round(top)), int(x1), int(round(bot))]})
                if draw:
                    draw.rectangle([x0, bt, x1, bb], outline=(220, 0, 0), width=3)
            if draw:
                draw.text((x0 + 6, at(st[idx[0]][0], xm) - 3 * sp - 44), str(measure), fill=(0, 0, 220), font=font)
            measure += 1
    print(f'page {pi}: {len(st)} staves, systems {[len(i) for i in systems]}, bars per system {counts}')
    if expect and pi <= len(expect) and expect[pi - 1] != counts:
        problems.append(f'page {pi}: bars per system {counts}, expected {expect[pi - 1]}')
    if draw:
        os.makedirs(a.draw, exist_ok=True)
        ov.thumbnail((1600, 2400)); ov.save(os.path.join(a.draw, f'bars-p{pi:02}.png'))

print(f'bars {a.first}..{measure - 1} ({measure - a.first})')
if a.todos:
    for t in json.load(open(a.todos, encoding='utf-8')):
        bars = [m for m in side['measures'] if m['measure'] == t['measure'] and (not t.get('part') or m['part'] == t['part'])]
        if not bars:
            problems.append(f'todo {t["key"]}: no bar {t["measure"]} {t.get("part", "")}'); continue
        page = t.get('page', bars[0]['page'])
        box = t.get('box') or [min(b['box'][0] for b in bars), min(b['box'][1] for b in bars),
                               max(b['box'][2] for b in bars), max(b['box'][3] for b in bars)]
        e = {'key': t['key'], 'measure': t['measure'], 'page': page, 'box': box, 'kind': t.get('kind', 'note')}
        if t.get('part'): e['part'] = t['part']; e['bars'] = [{'measure': t['measure'], 'part': t['part']}]
        if t.get('memo'): e['memo'] = t['memo']
        if t.get('candidates'): e['candidates'] = t['candidates']
        side['todos'].append(e)
os.makedirs(outdir, exist_ok=True)
with open(a.out, 'w', encoding='utf-8') as f:
    json.dump(side, f, ensure_ascii=False, indent=1)
for p in problems: print('CHECK:', p)
