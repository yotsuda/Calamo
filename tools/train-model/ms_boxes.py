"""The detector's training boxes from a page MuseScore drew (B4a: other fonts).

MuseScore's SVG names every element's class (Note, Rest, Accidental, Stem, Beam, BarLine, TieSegment,
...) but not which glyph it is: a black and a half head are both a Note. A glyph's outline is the
same wherever the font draws it, so its shape (the path's commands, the number of subpaths, its size
in staff spaces) names it through a table made once per font by eye (data/ms-glyphs.tsv, from the
sheets --survey draws). The names are Emmentaler's, as Lily#'s SVG gives them, so dataset.py reads
the boxes as it reads the generated pages'.

  python ms_boxes.py <out dir>                       every <piece>/pNN.svg -> <piece>/omr/boxes-pNN.tsv
  python ms_boxes.py --survey <out dir> <sheet dir>  the shapes the table does not know, as numbered sheets and a list

The font is the piece's directory under <out dir> (run-ms.ps1 lays them out <font>/<set>-<suite>/<piece>).
A TAB's frets are drawn in a text face of their own, which run-ms.ps1 -Band leaves in the piece's
fret.txt; their shapes are data/ms-frets.tsv (ms_frets.py), and each digit is boxed as Lily#'s
truth boxes it (tab.N, "12" as a 1 and a 2).
"""
import os, re, sys, glob, csv, hashlib, collections
from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
TABLE = os.path.join(HERE, '..', '..', 'data', 'ms-glyphs.tsv')
FRET_TABLE = os.path.join(HERE, '..', '..', 'data', 'ms-frets.tsv')
ELEMENT = re.compile(r'<(path|polyline)\s+class="(\w*)"([^>]*)/>')
TOKEN = re.compile(r'[A-Za-z]|-?\d+(?:\.\d+)?(?:e-?\d+)?')
# the classes whose glyph the table names (a dot is a dot in every font)
NAMED = ('Note', 'Rest', 'Accidental', 'KeySig', 'Clef', 'TimeSig', 'Hook', 'Tuplet', 'MeasureRepeat')
TOLERANCE = 0.06                    # staff spaces a shape's width or height may differ by and still match


def attr(rest, name):
    m = re.search(r'\s%s="([^"]*)"' % name, rest)
    return m.group(1) if m else None


def outline(d):
    """The path's letters and its points, the curves sampled (a control point is not on the outline)."""
    letters, pts, cur, start = [], [], (0.0, 0.0), (0.0, 0.0)
    toks = TOKEN.findall(d)
    i, cmd = 0, None
    def num():
        nonlocal i
        v = float(toks[i]); i += 1
        return v
    while i < len(toks):
        if toks[i].isalpha():
            cmd = toks[i]; i += 1; letters.append(cmd)
            if cmd in 'Zz':
                cur = start
                continue
        if cmd == 'M':
            cur = start = (num(), num()); pts.append(cur); cmd = 'L'
        elif cmd == 'L':
            cur = (num(), num()); pts.append(cur)
        elif cmd == 'C':
            p1, p2, p3 = (num(), num()), (num(), num()), (num(), num())
            for k in range(1, 11):
                t = k / 10; u = 1 - t
                pts.append((u*u*u*cur[0] + 3*u*u*t*p1[0] + 3*u*t*t*p2[0] + t*t*t*p3[0],
                            u*u*u*cur[1] + 3*u*u*t*p1[1] + 3*u*t*t*p2[1] + t*t*t*p3[1]))
            cur = p3
        elif cmd == 'Q':
            p1, p2 = (num(), num()), (num(), num())
            for k in range(1, 11):
                t = k / 10; u = 1 - t
                pts.append((u*u*cur[0] + 2*u*t*p1[0] + t*t*p2[0], u*u*cur[1] + 2*u*t*p1[1] + t*t*p2[1]))
            cur = p2
        else:
            raise ValueError('path command %s' % cmd)
    return ''.join(letters), pts


class Page:
    def __init__(self, path):
        text = open(path, encoding='utf-8').read()
        vb = re.search(r'viewBox="0 0 ([\d.]+) ([\d.]+)"', text)
        self.width = float(vb.group(1))
        self.items = []             # (class, letters, points, stroke width)
        for m in ELEMENT.finditer(text):
            tag, cls, rest = m.groups()
            if tag == 'polyline':
                pts = [tuple(map(float, p.split(','))) for p in attr(rest, 'points').split()]
                self.items.append((cls, 'line', pts, float(attr(rest, 'stroke-width') or 0)))
            else:
                d = attr(rest, 'd')
                if d:
                    letters, pts = outline(d)
                    if pts:
                        self.items.append((cls, letters, pts, 0.0))
        # staves: runs of horizontal staff lines evenly spaced
        lines = sorted((p[0][1], min(p[0][0], p[-1][0]), max(p[0][0], p[-1][0])) for c, _, p, _ in self.items if c == 'StaffLines')
        self.staves = []
        run = []
        # the page's usual gap between lines: a one-line staff (a percussion line) ends where the next
        # line lies further than a TAB's lines would
        steps = sorted(b[0] - a[0] for a, b in zip(lines, lines[1:]) if b[0] > a[0])
        usual = steps[len(steps) // 2] if steps else 1e9
        for ln in lines:
            if run and (ln[0] - run[-1][0] > 1.5 * (run[1][0] - run[0][0] if len(run) > 1 else 1.5 * usual) or abs(ln[1] - run[-1][1]) > 1):
                self.staves.append(run); run = []
            run.append(ln)
        if run:
            self.staves.append(run)
        # (top, left, right, bottom, line gap)
        five = [(r[-1][0] - r[0][0]) / 4 for r in self.staves if len(r) == 5]
        self.staves = [(r[0][0], r[0][1], r[0][2], r[-1][0], (r[-1][0] - r[0][0]) / (len(r) - 1)) for r in self.staves if len(r) >= 2]
        # the five-line staves' (a TAB's lines are wider apart)
        gaps = sorted(five or [s[4] for s in self.staves])
        self.space = gaps[len(gaps) // 2] if gaps else None
        # a TAB's lines are wider apart (1.5 spaces)
        self.tabs = [(s[0], s[3]) for s in self.staves if self.space and s[4] > 1.2 * self.space]

    def on_tab(self, y):
        return any(top - 0.5 * self.space <= y <= bottom + 0.5 * self.space for top, bottom in self.tabs)

    def shape(self, letters, pts):
        """A glyph's shape: its commands (hashed), subpaths, size in staff spaces, and where in its box
        the outline's points lie on average (a font may draw a 9 as its 6 turned round: Bravura)."""
        xs, ys = [p[0] for p in pts], [p[1] for p in pts]
        h = hashlib.md5(letters.encode()).hexdigest()[:8]
        w, hh = max(xs) - min(xs), max(ys) - min(ys)
        cx = (sum(xs) / len(xs) - min(xs)) / w if w else 0.5
        cy = (sum(ys) / len(ys) - min(ys)) / hh if hh else 0.5
        return h, letters.count('M'), w / self.space, hh / self.space, cx, cy

    def glyphs(self):
        """(class, shape, box l t r b) of every glyph the table may name, and the dots. A note on a TAB
        is its fret number (class Fret)."""
        for cls, letters, pts, _ in self.items:
            if cls in NAMED or cls == 'NoteDot':
                xs, ys = [p[0] for p in pts], [p[1] for p in pts]
                if cls == 'Note' and self.on_tab((min(ys) + max(ys)) / 2):
                    cls = 'Fret'
                yield cls, self.shape(letters, pts), (min(xs), min(ys), max(xs), max(ys))


def load_table():
    table = collections.defaultdict(list)
    for path in (TABLE, FRET_TABLE):
        if os.path.exists(path):
            with open(path, newline='', encoding='utf-8') as f:
                for r in csv.DictReader((l for l in f if not l.startswith('#')), delimiter='\t'):
                    table[(r['font'], r['hash'], int(r['subpaths']))].append(
                        (float(r['w']), float(r['h']), float(r['cx']), float(r['cy']), r['name']))
    return table


FRET_SIZE = 9                       # MuseScore's tabFretNumberFontSize, at which ms_frets.py draws the table


def fret_font(face, style=''):
    """The table's font for frets in a face and style (bold, italic): fret:<face>[|<style>]."""
    return 'fret:' + (face or 'default') + ('|' + style if style and style != 'regular' else '')


def fret_face(svg):
    """The table's font for the piece's frets and their size against the table's, from its fret.txt
    (run-ms.ps1 -Band): the face, then (if given) the size in points and the style."""
    p = os.path.join(os.path.dirname(svg), 'fret.txt')
    lines = open(p, encoding='utf-8').read().split('\n') if os.path.exists(p) else ['']
    lines = [l.strip() for l in lines] + ['', '']
    size = float(lines[1]) if lines[1] else FRET_SIZE
    return fret_font(lines[0], lines[2]), size / FRET_SIZE


def fret_shape(shape, scale):
    """A fret's shape as the table has it (drawn at FRET_SIZE)."""
    h, m, w, hh, cx, cy = shape
    return h, m, w / scale, hh / scale, cx, cy


def digit_widths(table, face):
    """A face's single digits' widths in staff spaces (a fret of two digits is split by them)."""
    return {e[4][5:]: e[0] for k, v in table.items() if k[0] == face for e in v if len(e[4]) == 6}


def lookup(table, font, shape):
    h, m, w, hh, cx, cy = shape
    best = None
    for tw, th, tx, ty, name in table.get((font, h, m), []):
        e = max(abs(tw - w), abs(th - hh), abs(tx - cx), abs(ty - cy))
        if e <= TOLERANCE and (best is None or e < best[0]):
            best = (e, name)
    return best[1] if best else None


def poly_at(p, x):
    """A four-cornered beam's vertical extent at x (None outside it)."""
    lo, hi = None, None
    for i in range(len(p)):
        a, b = p[i], p[(i + 1) % len(p)]
        if a[0] == b[0] or x < min(a[0], b[0]) or x > max(a[0], b[0]):
            continue
        y = a[1] + (b[1] - a[1]) * (x - a[0]) / (b[0] - a[0])
        lo = y if lo is None else min(lo, y); hi = y if hi is None else max(hi, y)
    return None if lo is None else (lo, hi)


def boxes(page, font, table, unknown, fret=('fret:default', 1.0)):
    """The rows of boxes-pNN.tsv in SVG units (scaled to the PNG by the caller). fret: the frets'
    table font and size against the table's (fret_face)."""
    s = page.space
    face, fscale = fret
    rows, heads, flags, frets = [], [], [], []
    widths = None
    for cls, shape, (l, t, r, b) in page.glyphs():
        if cls == 'Fret':
            shape = fret_shape(shape, fscale)
            name = lookup(table, face, shape)
            if name is None:
                unknown[(face, cls) + shape[:2] + tuple(round(v, 2) for v in shape[2:])] += 1
                continue
            digits = name[5:]
            if widths is None:
                widths = digit_widths(table, face)
            if len(digits) == 1:
                rows.append(('glyph', 'tab.' + digits, l, t, r, b))
            else:
                # the first digit from the left edge, the second from the right (the outlines are the single digits')
                rows.append(('glyph', 'tab.' + digits[0], l, t, l + widths[digits[0]] * fscale * s, b))
                rows.append(('glyph', 'tab.' + digits[1], r - widths[digits[1]] * fscale * s, t, r, b))
            # a stem drawn through the TAB hangs from the fret (stem tips below)
            frets.append((l, t, r, b))
            continue
        name = 'dots.dot' if cls == 'NoteDot' else lookup(table, font, shape)
        if name is None and cls == 'Tuplet' and shape[2] > 1.2:
            continue                # a bracket (its length varies with the notes under it)
        if name is None:
            unknown[(font, cls) + shape[:2] + tuple(round(v, 2) for v in shape[2:])] += 1
            continue
        if name == 'none':
            continue
        if name == 'clefs.G_8':
            # one glyph with its 8 below; Lily#'s truth boxes the G clef alone (the font's own G clef's height)
            g = [e[1] for k, v in table.items() if k[0] == font for e in v if e[4] == 'clefs.G']
            if g:
                b = t + min(g) * s
            name = 'clefs.G'
        rows.append(('glyph', name, l, t, r, b))
        if name.startswith('noteheads.'):
            heads.append((l, t, r, b))
        elif name.startswith('flags.'):
            flags.append((l, t, r, b, name))
    beams = [p for c, _, p, _ in page.items if c == 'Beam' and len(p) >= 4]
    beams = [p[:4] for p in beams]
    # a stem's tip, with the beams or flags at it (as SymEval.StemTails does on Lily#'s pages)
    for cls, _, p, _ in page.items:
        if cls != 'Stem':
            continue
        x = p[0][0]; top, bot = min(p[0][1], p[-1][1]), max(p[0][1], p[-1][1])
        mine = [h for h in heads if h[0] - 0.25 * s <= x <= h[2] + 0.25 * s and top - 0.7 * s <= (h[1] + h[3]) / 2 <= bot + 0.7 * s]
        if mine:
            mid = sum((h[1] + h[3]) / 2 for h in mine) / len(mine)
            up = abs(top - mid) > abs(bot - mid)
        elif any(f[0] <= x <= f[2] and f[3] - 0.3 * s <= top <= f[3] + 1.2 * s for f in frets):
            up = False              # down from the lowest fret of a chord on a TAB
        else:
            continue
        tip = top if up else bot
        n = 0
        for f in flags:
            # a flag hangs from the tip on the stem's right
            if abs(f[0] - x) < 0.4 * s and (f[1] - 0.8 * s <= tip <= f[3] + 0.8 * s):
                n = max(n, {'3': 1, '4': 2, '5': 3}.get(f[4][-1], 1))
        crossing = 0
        for bp in beams:
            ext = poly_at(bp, x)
            if ext is None or ext[1] - ext[0] > 0.8 * s:
                continue
            ym = (ext[0] + ext[1]) / 2
            into = ym - tip if up else tip - ym
            if -0.5 * s < into < 2.4 * s:
                crossing += 1
        k = min(3, max(n, crossing))
        rows.append(('glyph', 'stem.%d' % k, x - 0.5 * s, tip - 0.5 * s, x + 0.5 * s, tip + 0.5 * s))
    # a barline: one per staff it crosses, at the staff's middle (thin lines only, as on Lily#'s pages;
    # not the line that opens a system at the staves' left end, which Lily#'s truth does not count)
    for cls, _, p, sw in page.items:
        if cls != 'BarLine' or sw > 0.3 * s:
            continue
        x = p[0][0]; y0, y1 = min(p[0][1], p[-1][1]), max(p[0][1], p[-1][1])
        for (top, left, right, bottom, _) in page.staves:
            if left + 0.5 * s <= x <= right + s and top >= y0 - 0.5 * s and bottom <= y1 + 0.5 * s:
                ym = (top + bottom) / 2
                rows.append(('glyph', 'barline', x - 0.5 * s, ym - 0.5 * s, x + 0.5 * s, ym + 0.5 * s))
    # a tie's or slur's ends: the outline's leftmost and rightmost points
    for cls, _, p, _ in page.items:
        if cls in ('TieSegment', 'SlurSegment'):
            a, b = min(p), max(p)
            rows.append(('tie' if cls == 'TieSegment' else 'slur', 'tie' if cls == 'TieSegment' else 'slur', a[0], a[1], b[0], b[1]))
    return rows


def font_of(out, svg):
    return os.path.relpath(svg, out).split(os.sep)[0]


def pages(out):
    return sorted(glob.glob(os.path.join(out, '**', 'p[0-9][0-9].svg'), recursive=True))


def write_all(out):
    table = load_table()
    unknown = collections.Counter()
    n = 0
    for svg in pages(out):
        png = svg[:-4] + '.png'
        if not os.path.exists(png):
            continue
        page = Page(svg)
        if not page.space:
            continue
        with Image.open(png) as im:
            k = im.width / page.width
        rows = boxes(page, font_of(out, svg), table, unknown, fret_face(svg))
        omr = os.path.join(os.path.dirname(svg), 'omr')
        os.makedirs(omr, exist_ok=True)
        with open(os.path.join(omr, 'boxes-%s.tsv' % os.path.basename(svg)[:-4]), 'w', newline='') as f:
            f.write('kind\tname\tl\tt\tr\tb\tsrc\n')
            for kind, name, l, t, r, b in rows:
                f.write('%s\t%s\t%.1f\t%.1f\t%.1f\t%.1f\t-1\n' % (kind, name, l * k, t * k, r * k, b * k))
            for i, (top, left, right, bottom, gap) in enumerate(page.staves):
                if gap > 1.2 * page.space:
                    continue        # a TAB (dataset.py takes the page's scale from these rows)
                f.write('staff\ts1p%d\t%.1f\t%.1f\t%.1f\t%.1f\t%.2f\n' % (i, left * k, top * k, right * k, bottom * k, gap * k))
        n += 1
    print('%d pages boxed' % n)
    if unknown:
        print('shapes the table does not name (run --survey):')
        for key, c in unknown.most_common(40):
            print('  %-60s %d' % (key, c))


def survey(out, sheets):
    """Every shape the table does not name, once per font: a numbered crop on a sheet and a row to fill in."""
    table = load_table()
    found = collections.OrderedDict()
    for svg in pages(out):
        png = svg[:-4] + '.png'
        if not os.path.exists(png):
            continue
        page = Page(svg)
        if not page.space:
            continue
        font = font_of(out, svg)
        for cls, shape, box in page.glyphs():
            if cls in ('NoteDot', 'Fret') or lookup(table, font, shape) is not None:
                continue
            key = (font, shape[0], shape[1]) + tuple(round(v / TOLERANCE) for v in shape[2:])
            if key in found:
                found[key][3] += 1
            else:
                found[key] = [cls, shape, (png, box, page.width, page.space), 1]
    os.makedirs(sheets, exist_ok=True)
    cell, per = 150, 40
    items = list(found.items())
    with open(os.path.join(sheets, 'survey.tsv'), 'w', newline='', encoding='utf-8') as f:
        f.write('font\tclass\thash\tsubpaths\tw\th\tcx\tcy\tcount\tsheet\tname\n')
        for i, (key, (cls, shape, (png, box, width, space), count)) in enumerate(items):
            f.write('%s\t%s\t%s\t%d\t%.3f\t%.3f\t%.3f\t%.3f\t%d\t%d\t\n' % ((key[0], cls) + shape + (count, i)))
    for s0 in range(0, len(items), per):
        sheet = Image.new('L', (cell * 8, (cell + 14) * ((min(per, len(items) - s0) + 7) // 8)), 255)
        draw = ImageDraw.Draw(sheet)
        for j, (key, (cls, shape, (png, box, width, space), count)) in enumerate(items[s0:s0 + per]):
            with Image.open(png) as im:
                k = im.width / width
                pad = 0.6 * space
                crop = im.convert('L').crop((int((box[0] - pad) * k), int((box[1] - pad) * k), int((box[2] + pad) * k) + 1, int((box[3] + pad) * k) + 1))
            # the same scale for every crop (a staff space is 24 pixels), so sizes compare
            f = 24 / (space * k)
            w0 = crop.width
            crop = crop.resize((max(1, int(crop.width * f)), max(1, int(crop.height * f))))
            crop.thumbnail((cell - 4, cell - 4))
            x, y = (j % 8) * cell, (j // 8) * (cell + 14)
            sheet.paste(crop, (x + 2, y + 14))
            draw.text((x + 2, y), '%d %s' % (s0 + j, cls[:6]), fill=0)
            # the glyph's own box, so a crop's neighbours do not pass for it (sheet pixels per SVG unit: q)
            q = k * crop.width / w0
            draw.rectangle((x + 2 + pad * q, y + 14 + pad * q, x + 2 + (box[2] - box[0] + pad) * q, y + 14 + (box[3] - box[1] + pad) * q), outline=128)
            draw.rectangle((x, y, x + cell - 1, y + cell + 13), outline=160)
        sheet.save(os.path.join(sheets, 'survey-%03d.png' % s0))
    print('%d shapes not named -> %s' % (len(items), sheets))


if __name__ == '__main__':
    if sys.argv[1] == '--survey':
        survey(sys.argv[2], sys.argv[3])
    else:
        write_all(sys.argv[1])
