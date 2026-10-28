"""MuseScore's pages drawn again with every glyph's shape varied (B4: a scan's font is none of the
eight MuseScore draws, so the detector is shown a family of shapes around them).

The SVG MuseScore writes is paths and lines only, and the shape table (ms_boxes.py) names each
glyph, so the page is drawn again here with Pillow: the lines and beams as they are (their widths
varied per page), every named glyph scaled, stretched, slanted, turned, thickened or thinned and a
little wobbled about its box's centre. The detector's target is a glyph's class and centre only, so
the boxes ms_boxes.py writes for the new page stay true. Each page is its own "font": one set of
changes per kind of glyph (every cross head alike), a little play per glyph on top. The page is
drawn at a staff space drawn at random (a band score's scan is 13.5 pixels a space, the MuseScore
pages 21).

  python ms_restyle.py <src out dir> <dst out dir> [--seed N] [--space lo,hi] [--jobs N]

<src> is laid out as run-ms.ps1 leaves it (<font>/<set>-<suite>/<piece>/pNN.svg); <dst> gets the
same layout, each pNN.svg copied and its pNN.png drawn. Then run ms_boxes.py on <dst> (run-restyle.ps1).
"""
import os, re, sys, glob, math, shutil, random
from multiprocessing import Pool
from PIL import Image, ImageDraw, ImageFilter, ImageChops
import ms_boxes

ELEMENT = re.compile(r'<(path|polyline)\s+class="(\w*)"([^>]*)/>')
SS = 3                              # supersampling: drawn at 3x, reduced with a box filter

# how far each kind of glyph may vary: (scale, aspect, slant, turn in degrees, stroke in spaces)
# a head may not grow much (it sits between two lines), a drum's cross may (Fantasy's is 1.3 spaces)
RANGES = {
    'head':  ((0.9, 1.15), (0.85, 1.2), 0.15, 15, (-0.04, 0.05)),
    'cross': ((0.85, 1.4), (0.8, 1.25), 0.15, 10, (-0.05, 0.04)),
    'rest':  ((0.8, 1.3), (0.75, 1.3), 0.25, 12, (-0.05, 0.06)),
    'acc':   ((0.85, 1.2), (0.8, 1.2), 0.15, 6, (-0.04, 0.06)),
    'clef':  ((0.85, 1.15), (0.85, 1.15), 0.1, 4, (-0.04, 0.05)),
    'digit': ((0.85, 1.25), (0.8, 1.25), 0.2, 6, (-0.05, 0.06)),
    # a band score's frets are often lettered by hand (Time After Time's) or in a face of the engraver's own
    'fret':  ((0.8, 1.3), (0.75, 1.3), 0.3, 10, (-0.05, 0.08)),
    'other': ((0.85, 1.2), (0.8, 1.2), 0.2, 8, (-0.04, 0.05)),
}


def kind(name):
    if 'cross' in name or 'xcircle' in name:
        return 'cross', name
    if name.startswith('noteheads.'):
        return 'head', name
    if name.startswith('rests.'):
        return 'rest', name
    if name.startswith('accidentals.'):
        return 'acc', name
    if name.startswith('clefs.'):
        return 'clef', name
    if name.startswith('fret.'):
        return 'fret', 'fret'       # a TAB's frets: one face
    if len(name) == 1 or name.startswith('timesig.'):
        return 'digit', 'digit'     # a time signature's digits share one style
    return 'other', name


def subpaths(d):
    """The path's closed outlines as point lists (curves sampled)."""
    out, pts, cur, start = [], [], (0.0, 0.0), (0.0, 0.0)
    toks = ms_boxes.TOKEN.findall(d)
    i, cmd = 0, None
    def num():
        nonlocal i
        v = float(toks[i]); i += 1
        return v
    while i < len(toks):
        if toks[i].isalpha():
            cmd = toks[i]; i += 1
            if cmd in 'Zz':
                cur = start
                continue
        if cmd == 'M':
            if len(pts) > 2:
                out.append(pts)
            cur = start = (num(), num()); pts = [cur]; cmd = 'L'
        elif cmd == 'L':
            cur = (num(), num()); pts.append(cur)
        elif cmd == 'C':
            p1, p2, p3 = (num(), num()), (num(), num()), (num(), num())
            for k in range(1, 9):
                t = k / 8; u = 1 - t
                pts.append((u*u*u*cur[0] + 3*u*u*t*p1[0] + 3*u*t*t*p2[0] + t*t*t*p3[0],
                            u*u*u*cur[1] + 3*u*u*t*p1[1] + 3*u*t*t*p2[1] + t*t*t*p3[1]))
            cur = p3
        elif cmd == 'Q':
            p1, p2 = (num(), num()), (num(), num())
            for k in range(1, 9):
                t = k / 8; u = 1 - t
                pts.append((u*u*cur[0] + 2*u*t*p1[0] + t*t*p2[0], u*u*cur[1] + 2*u*t*p1[1] + t*t*p2[1]))
            cur = p2
        else:
            raise ValueError('path command %s' % cmd)
    if len(pts) > 2:
        out.append(pts)
    return out


N_REST = 0.3                        # the share of pages whose quarter rests are a copyist's N


def style(rng, k, name=None):
    (s0, s1), (a0, a1), sl, turn, (w0, w1) = RANGES[k]
    st = dict(scale=rng.uniform(s0, s1), aspect=rng.uniform(a0, a1), slant=rng.uniform(-sl, sl),
              turn=rng.uniform(-turn, turn), stroke=rng.uniform(w0, w1), wobble=rng.uniform(0, 0.04))
    if name == 'rests.2' and rng.random() < N_REST:
        # a copyist's quarter rest written as an N, three strokes of a broad pen (Time After Time's
        # drums): its width, height and pen in spaces, how far its strokes lean
        st['n'] = dict(w=rng.uniform(1.3, 1.9), h=rng.uniform(0.9, 1.3), pen=rng.uniform(0.18, 0.3),
                       lean=rng.uniform(0.15, 0.4), tail=rng.uniform(0.0, 0.25))
    return st


def n_rest(cx, cy, space, n):
    """The N's outline about (cx, cy), in the SVG's units: the zigzag up, down and up again, stroked
    as one closed polygon (its sides offset by half the pen at each corner)."""
    w, h, lean = n['w'] * space, n['h'] * space, n['lean'] * space
    # bottom left, top, bottom, top right; each stroke leaning right as it rises
    pts = [(-w / 2, h / 2 + n['tail'] * space), (-w / 6 + lean, -h / 2), (w / 6, h / 2), (w / 2 + lean, -h / 2)]
    pts = [(cx + x - lean / 2, cy + y) for x, y in pts]
    half = n['pen'] * space / 2
    left, right = [], []
    for i, (x, y) in enumerate(pts):
        # the corner's normal: the mean of its strokes' normals, lengthened to keep the pen's width
        ns = []
        strokes = ([(pts[i - 1], (x, y))] if i else []) + ([((x, y), pts[i + 1])] if i + 1 < len(pts) else [])
        for a, b in strokes:
            dx, dy = b[0] - a[0], b[1] - a[1]
            d = math.hypot(dx, dy)
            ns.append((-dy / d, dx / d))
        nx, ny = sum(v[0] for v in ns) / len(ns), sum(v[1] for v in ns) / len(ns)
        d = math.hypot(nx, ny)
        nx, ny = nx / d, ny / d
        f = half / max(0.35, abs(nx * ns[0][0] + ny * ns[0][1]))
        left.append((x + nx * f, y + ny * f)); right.append((x - nx * f, y - ny * f))
    return [left + right[::-1]]


def fill(outlines, f, pad):
    """The outlines (already in page pixels) filled even-odd on their own small mask: (mask, left, top)."""
    xs = [p[0] for o in outlines for p in o]; ys = [p[1] for o in outlines for p in o]
    l, t = int(math.floor(min(xs))) - pad, int(math.floor(min(ys))) - pad
    w, h = int(math.ceil(max(xs))) + pad - l + 1, int(math.ceil(max(ys))) + pad - t + 1
    mask = Image.new('L', (w, h), 0)
    for o in outlines:
        one = Image.new('L', (w, h), 0)
        ImageDraw.Draw(one).polygon([(x - l, y - t) for x, y in o], fill=255)
        mask = ImageChops.logical_xor(mask.convert('1'), one.convert('1')).convert('L')
    if f > 0:
        mask = mask.filter(ImageFilter.MaxFilter(2 * f + 1))
    elif f < 0:
        mask = mask.filter(ImageFilter.MinFilter(-2 * f + 1))
    return mask, l, t


def draw_page(svg, png, rng, space_px):
    page = ms_boxes.Page(svg)
    if not page.space:
        return False
    font = ms_boxes.font_of(SRC, svg)
    face, fscale = ms_boxes.fret_face(svg)
    text = open(svg, encoding='utf-8').read()
    k = space_px / page.space * SS         # SVG units -> supersampled pixels
    sp = space_px * SS                     # a staff space in supersampled pixels
    W, H = int(page.width * k), int(float(re.search(r'viewBox="0 0 [\d.]+ ([\d.]+)"', text).group(1)) * k)
    ink = Image.new('L', (W, H), 0)
    draw = ImageDraw.Draw(ink)
    styles = {}
    line_w = {c: rng.uniform(0.7, 1.6) for c in ('StaffLines', 'Stem', 'BarLine', 'LedgerLine', 'other')}
    for m in ELEMENT.finditer(text):
        tag, cls, rest = m.groups()
        if ms_boxes.attr(rest, 'fill') == '#ffffff':
            continue                        # the paper
        if tag == 'polyline':
            pts = [tuple(float(v) * k for v in p.split(',')) for p in ms_boxes.attr(rest, 'points').split()]
            sw = float(ms_boxes.attr(rest, 'stroke-width') or 0) * k * line_w.get(cls, line_w['other'])
            draw.line(pts, fill=255, width=max(1, int(round(sw))))
            continue
        d = ms_boxes.attr(rest, 'd')
        if not d:
            continue
        outlines = subpaths(d)
        if not outlines:
            continue
        if ms_boxes.attr(rest, 'fill') == 'none':
            # a frame drawn with a pen, not filled (a rehearsal mark's box)
            sw = float(ms_boxes.attr(rest, 'stroke-width') or 0) * k * line_w['other']
            for o in outlines:
                draw.line([(x * k, y * k) for x, y in o] + [(o[0][0] * k, o[0][1] * k)], fill=255, width=max(1, int(round(sw))))
            continue
        name = None
        if cls in ms_boxes.NAMED:
            letters, pts = ms_boxes.outline(d)
            ys = [p[1] for p in pts]
            fret = cls == 'Note' and page.on_tab((min(ys) + max(ys)) / 2)
            shape = page.shape(letters, pts)
            name = ms_boxes.lookup(TABLE, face, ms_boxes.fret_shape(shape, fscale)) if fret else ms_boxes.lookup(TABLE, font, shape)
        if name and name != 'none':
            kd, key = kind(name)
            if key not in styles:
                styles[key] = style(rng, kd, key)
            st = styles[key]
            # this glyph's play about the page's style for its kind
            sc = st['scale'] * rng.uniform(0.97, 1.03)
            sx, sy = sc * math.sqrt(st['aspect']), sc / math.sqrt(st['aspect'])
            a = math.radians(st['turn'] + rng.uniform(-2, 2))
            ca, sa = math.cos(a), math.sin(a)
            xs = [p[0] for o in outlines for p in o]; ys = [p[1] for o in outlines for p in o]
            cx, cy = (min(xs) + max(xs)) / 2, (min(ys) + max(ys)) / 2
            if 'n' in st:
                outlines = n_rest(cx, cy, page.space, st['n'])
            # a slow wobble of the outline (a pen's, not a machine's), in spaces
            wa, ph1, ph2 = st['wobble'] * page.space, rng.uniform(0, 6.3), rng.uniform(0, 6.3)
            fq = rng.uniform(1.5, 3.0) / page.space
            moved = []
            for o in outlines:
                q = []
                for x, y in o:
                    u, v = (x - cx) * sx, (y - cy) * sy
                    u += st['slant'] * -v
                    u, v = u * ca - v * sa, u * sa + v * ca
                    u += wa * math.sin(fq * y + ph1); v += wa * math.sin(fq * x + ph2)
                    q.append(((cx + u) * k, (cy + v) * k))
                moved.append(q)
            f = int(round(st['stroke'] * sp / 2))
            mask, l, t = fill(moved, f, abs(f) + 2)
        else:
            mask, l, t = fill([[(x * k, y * k) for x, y in o] for o in outlines], 0, 1)
        box = (l, t, l + mask.width, t + mask.height)
        if box[2] <= 0 or box[3] <= 0 or box[0] >= W or box[1] >= H:
            continue
        region = ink.crop(box)
        ink.paste(ImageChops.lighter(region, mask), box)
    out = ImageChops.invert(ink).resize((W // SS, H // SS), Image.Resampling.BOX)
    out.save(png)
    return True


SRC, TABLE = None, None


def work(args):
    global SRC, TABLE
    src, svg, dst, seed, lo, hi = args
    if TABLE is None:
        SRC, TABLE = src, ms_boxes.load_table()
    rng = random.Random(seed)
    rel = os.path.relpath(svg, src)
    out = os.path.join(dst, rel)
    os.makedirs(os.path.dirname(out), exist_ok=True)
    shutil.copyfile(svg, out)
    fret = os.path.join(os.path.dirname(svg), 'fret.txt')
    if os.path.exists(fret):
        shutil.copyfile(fret, os.path.join(os.path.dirname(out), 'fret.txt'))
    ok = draw_page(svg, out[:-4] + '.png', rng, rng.uniform(lo, hi))
    if not ok:
        os.remove(out)
    return ok


if __name__ == '__main__':
    args = [a for a in sys.argv[1:] if not a.startswith('--')]
    opts = {sys.argv[i][2:]: sys.argv[i + 1] for i in range(1, len(sys.argv) - 1) if sys.argv[i].startswith('--')}
    src, dst = os.path.abspath(args[0]), os.path.abspath(args[1])
    seed = int(opts.get('seed', 1))
    lo, hi = map(float, opts.get('space', '13,22').split(','))
    svgs = ms_boxes.pages(src)
    if 'limit' in opts:
        svgs = svgs[:int(opts['limit'])]
    jobs = [(src, s, dst, seed * 100003 + i, lo, hi) for i, s in enumerate(svgs)]
    with Pool(int(opts.get('jobs', os.cpu_count()))) as pool:
        done = sum(pool.imap_unordered(work, jobs, chunksize=4))
    print('%d of %d pages drawn -> %s' % (done, len(svgs), dst))
