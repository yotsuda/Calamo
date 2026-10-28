namespace Omr;

static partial class Symbols
{
    /// <summary>
    /// The heads the learnt detector found, as the rules' own heads: the kind from its class, the
    /// edges from the ink on the middle row (out of a hollow head's hole and through its rim, or
    /// through a black head), where the stem is looked for.
    /// </summary>
    /// <param name="d">the page's ink with the staff lines (for the ledger lines)</param>
    /// <param name="rivals">the detector's half heads under its threshold: one takes the place of a black head where it wins (Beats)</param>
    static void DetectedHeads(Img d, Img ink, List<Detection> det, List<Staff> staves, double s, List<Head> heads, List<Detection> rivals = null)
    {
        // (a drum's cross head, plain or circled, times as a black head: its stem, beams and flags tell)
        // (a guitar's rhythm slash likewise: its stem, beams, flags and dots tell its duration, B3)
        // (a grace note's small head likewise, marked Grace: Find sets it apart, S21)
        static bool IsHead(Detection t) => t.Class is "head_black" or "head_half" or "head_whole" or "head_x" or "slash_black" or "slash_hollow" or "grace_head";
        // one head seen as two kinds (the toner hollows a black head, and a half head's rim
        // looks filled): the likelier kind. Both taken, the black one's stem dropped the other
        // as a hollow head on a black chord's stem (tuplets009 on fill: head_half 0.75 against
        // head_black 0.70, a half note read as a quarter)
        // (a black and a half head within a tenth of each other: the ink of the head's middle tells, the
        // staff line through it taken out -- an old print's half note, a thin rim round a wide hole, seen
        // as black 0.53 and half 0.50, the half under the threshold: kinkel19-5's scan, the bass's c,2)
        // (and a middle with no ink at all lets the half head win however less surely it is seen: the same
        // scan's bass cis,2, black 0.87 and half 0.27)
        bool Beats(Detection u, Detection t)
        {
            if (u.Class is "head_black" or "head_half" && t.Class is "head_black" or "head_half"
                && (Math.Abs(u.Score - t.Score) < 0.1 || InkCore(ink, (u.X + t.X) / 2, (u.Y + t.Y) / 2, 0.15 * s) < 0.1))
            {
                double core = InkCore(ink, (u.X + t.X) / 2, (u.Y + t.Y) / 2, 0.15 * s);
                if (DebugAt.HasValue && Math.Abs(t.X - DebugAt.Value.X) < 2 * s && Math.Abs(t.Y - DebugAt.Value.Y) < 3 * s)
                    Console.WriteLine($"core of {t.Class} {t.Score:F2} against {u.Class} {u.Score:F2} at {t.X:F0},{t.Y:F0}: {core:F2}");
                if (core < 0.3) return u.Class == "head_half";
                if (core > 0.8) return u.Class == "head_black";
            }
            return u.Score > t.Score || u.Score == t.Score && string.CompareOrdinal(u.Class, t.Class) < 0;
        }
        bool Rivals(Detection u, Detection t) => u != t && IsHead(u) && u.Class != t.Class && Math.Abs(u.X - t.X) < 0.3 * s && Math.Abs(u.Y - t.Y) < 0.3 * s;
        foreach (var seen in det)
        {
            var t = seen;
            if (t.Class == "head_black" && rivals?.FirstOrDefault(u => Rivals(u, t) && Beats(u, t)) is Detection hollow) t = hollow;
            bool slash = t.Class.StartsWith("slash_"), grace = t.Class == "grace_head";
            bool cross = t.Class == "head_x", black = t.Class == "head_black" || cross || t.Class == "slash_black" || grace, whole = t.Class == "head_whole";
            if (!black && !whole && t.Class != "head_half" && t.Class != "slash_hollow") continue;
            if (det.Any(u => Rivals(u, t) && Beats(u, t))) continue;
            // (a note on seven ledger lines and more stands past Nearest's reach: piano-collide's
            // fis''' and dis'''' under an 8va, on 7 and 9. Taken only on ledger lines all the way out)
            var st = Nearest(staves, t.Y, s);
            bool far = st == null && !slash && !cross && (st = Nearest(staves, t.Y, s, 12)) != null;
            if (st == null) continue;
            // (a head between two staves is the one its ledger lines run to: ByLedgers)
            if (!slash && !cross && ByLedgers(d, staves, st, t.X, t.Y, s) is var by && by != st)
            {
                if (DebugAt.HasValue && Math.Abs(t.X - DebugAt.Value.X) < 2 * s && Math.Abs(t.Y - DebugAt.Value.Y) < 3 * s)
                    Console.WriteLine($"detected {t.Class} at {t.X:F0},{t.Y:F0} ({t.Score:F2}): its ledger lines run to the staff at {by.Top:F0}, not the nearest at {st.Top:F0}");
                st = by; far = t.Y < by.Top - 6.5 * s || t.Y > by.Bottom + 6.5 * s;
            }
            // a slash seen weakly by a slur's end is the slur rising from its head (slurs003 in
            // MuseScore's fonts: 0.60, 1.2 spaces right of and 1.6 above the slur's start). (Not
            // the middle line: Fantasy's guitar sets its slashes in the staff's lower half.)
            if (slash && t.Score < SureScore
                && det.Any(u => u.Class is "slur_l" or "slur_r" && u.Score >= TrustScore && Math.Abs(u.X - t.X) < 2 * s && Math.Abs(u.Y - t.Y) < 2 * s))
            {
                if (DebugAt.HasValue && Math.Abs(t.X - DebugAt.Value.X) < 2 * s && Math.Abs(t.Y - DebugAt.Value.Y) < 3 * s)
                    Console.WriteLine($"detected {t.Class} at {t.X:F0},{t.Y:F0} ({t.Score:F2}) by a slur's end: dropped");
                continue;
            }
            int y0 = (int)Math.Round(t.Y), x0 = (int)Math.Round(t.X);
            int xl = x0, xr = x0;
            if (!black)
            {
                while (!ink[xl - 1, y0] && x0 - xl < 0.7 * s) xl--;
                while (!ink[xr + 1, y0] && xr - x0 < 0.7 * s) xr++;
            }
            // (a neighbour touching it, a second or another voice's unison, must not stretch it)
            while (ink[xl - 1, y0] && x0 - xl < 0.75 * s) xl--;
            while (ink[xr + 1, y0] && xr - x0 < 0.75 * s) xr++;
            // (the middle row missed the hole, or the ink is broken: a head's usual width then; a grace note's is seven tenths)
            double half = whole ? 0.8 * s : grace ? 0.45 * s : 0.65 * s;
            if (xr - xl + 1 < (grace ? 0.6 : 0.9) * s) { xl = (int)Math.Round(x0 - half); xr = (int)Math.Round(x0 + half); }
            // a slash leans across two spaces, its stem at a corner: down from its lower left, up
            // from its upper right. Its edges: the ink running left in its lower half, right in its
            // upper half (a beamed neighbour's slash lies in the other half there)
            if (slash)
            {
                bool Inked(int x, int ya, int yb) { for (int y = ya; y <= yb; y++) if (ink[x, y]) return true; return false; }
                int lo = (int)Math.Round(1.3 * s);
                xl = x0; xr = x0;
                while (x0 - xl < 1.6 * s && Inked(xl - 1, y0, y0 + lo)) xl--;
                while (xr - x0 < 1.6 * s && Inked(xr + 1, y0 - lo, y0)) xr++;
            }
            if (DebugAt.HasValue && Math.Abs(t.X - DebugAt.Value.X) < 2 * s && Math.Abs(t.Y - DebugAt.Value.Y) < 3 * s)
                Console.WriteLine($"detected {t.Class} at {t.X:F0},{t.Y:F0} ({t.Score:F2}): L {xl} R {xr}");
            heads.Add(new Head
            {
                St = st, X = t.X, Y = t.Y, Hollow = !black, WholeShape = whole, Score = t.Score, Cross = cross, Slash = slash, Grace = grace,
                L = xl, R = xr, OuterW = xr - xl + 1, Pos = (int)Math.Round(st.Pos(t.Y)), Far = far,
            });
        }
    }

    // the share of a square r out from (x, y) that is ink
    static double InkCore(Img ink, double x, double y, double r)
    {
        int x0 = (int)Math.Round(x), y0 = (int)Math.Round(y), c = Math.Max(1, (int)Math.Round(r)), n = 0, all = 0;
        for (int yy = y0 - c; yy <= y0 + c; yy++)
            for (int xx = x0 - c; xx <= x0 + c; xx++)
            {
                if (xx < 0 || yy < 0 || xx >= ink.W || yy >= ink.H) continue;
                all++;
                if (ink[xx, yy]) n++;
            }
        return all == 0 ? 0.5 : (double)n / all;
    }

    /// <summary>
    /// A grace note's black head, drawn at about seven tenths of a head's size: under 0.85 spaces
    /// tall and 1.05 wide, where a head fills the space between two lines and is 1.2 spaces wide
    /// or more (Lily#'s grace heads 0.77 by 0.9, its heads 1.0-1.1 by 1.2-1.3). Its height stops
    /// where the ink widens again past its top or bottom: a ledger line through it (lied006's
    /// appoggiatura) or a neighbour touching it. Only a head solid in its core: the toner hollows
    /// a scan's heads, and their middle column breaks.
    /// </summary>
    static bool GraceHead(Img ink, Head h, double s, bool narrowOnly = false)
    {
        if (h.Hollow || h.Cross || h.Slash) return false;
        // (about the middle of its edges, the tallest column: the rules may set a head's x near its
        // stem, where the oval narrows, as on the scans piano002 and ties004)
        int y0 = (int)Math.Round(h.Y), c = (int)Math.Round(0.2 * s);
        int xm = (int)Math.Round(h.R - h.L > 0.6 * s ? (h.L + h.R) / 2 : h.X);
        if (xm - 2 * s < 0 || xm + 2 * s >= ink.W || y0 - 2 * s < 0 || y0 + 2 * s >= ink.H) return false;
        int core = 0;
        for (int y = y0 - c; y <= y0 + c; y++)
            for (int x = xm - c; x <= xm + c; x++)
                if (ink[x, y]) core++;
        if (core < 0.9 * (2 * c + 1) * (2 * c + 1)) return false;
        // the ink's run along a row through column x, and the rows from y0 out over which it does
        // not widen again (an oval narrows to its top and bottom: a ledger line or a neighbour
        // touching it widens the run; two rows running wider, past a quarter space out: a scan's
        // ragged edge widens one row)
        int Run(int x, int y) { int l = x, r = x; while (ink[l - 1, y] && x - l < 1.5 * s) l--; while (ink[r + 1, y] && r - x < 1.5 * s) r++; return r - l + 1; }
        int Edge(int x, int dy)
        {
            int y = y0, least = Run(x, y0);
            while (ink[x, y + dy] && Math.Abs(y + dy - y0) < 1.5 * s)
            {
                int run = Run(x, y + dy);
                if (Math.Abs(y + dy - y0) > 0.25 * s && run > least + 0.15 * s && ink[x, y + 2 * dy] && Run(x, y + 2 * dy) > least + 0.15 * s) break;
                y += dy; least = Math.Min(least, run);
            }
            return y;
        }
        int ya = y0, yb = y0, w = 0;
        for (int x = xm - c; x <= xm + c; x++)
        {
            if (!ink[x, y0]) continue;
            int a = Edge(x, -1), b = Edge(x, 1);
            if (b - a > yb - ya) { ya = a; yb = b; }
        }
        // (the middle rows' median: a slur through the head widens a few of them -- lied004's grace
        // notes under their slur)
        var runs = new List<int>();
        for (int y = y0 - c; y <= y0 + c; y++) if (ink[xm, y]) runs.Add(Run(xm, y));
        if (runs.Count > 0) { runs.Sort(); w = runs[runs.Count / 2]; }
        if (DebugAt.HasValue && Math.Abs(h.X - DebugAt.Value.X) < s && Math.Abs(h.Y - DebugAt.Value.Y) < s)
            Console.WriteLine($"grace? head at {h.X:F0},{h.Y:F0}: {(yb - ya + 1) / s:F2} tall ({ya}-{yb}), {w / s:F2} wide at x {xm}");
        // (narrow only, its height left unread: a weak detection by a stem, its grace slur under it and
        // its slash over it joining it -- the ballade's acciaccatura, 1.35 tall, 0.85 wide)
        return (narrowOnly || yb - ya + 1 < 0.85 * s) && w < (narrowOnly ? 1.0 : 1.05) * s;
    }

    /// <summary>
    /// A tempo marking's note (♩ = 104) is no note of the music: the head with an equals sign
    /// just to its right, two short flat bars one over the other about its height (lied003's
    /// "Lebhaft (♩ = 44)", lied008's half note). Ledger lines lie a space apart, a beam joins
    /// a stem, a hairpin slants.
    /// </summary>
    static bool MetronomeMark(Img d, Head h, double s)
    {
        // (on the page as drawn: removing the lines takes the sign's upper bar for a ledger line)
        // (as far as four spaces: a dotted note's sign stands past its dot -- tabi.jpg's "♩. = 60")
        int x0 = (int)Math.Round(h.X + 0.8 * s), x1 = (int)Math.Round(h.X + 4.0 * s), most = (int)Math.Round(2.5 * s);
        int y0 = (int)Math.Round(h.Y - 0.8 * s), y1 = (int)Math.Round(h.Y + 0.8 * s);
        if (x0 < 1 || x1 + most >= d.W || y0 < 0 || y1 >= d.H) return false;
        // the rows' runs that start in the window and are a bar's length, then the bars: rows in a
        // row with runs starting together
        var bars = new List<(int Top, int Bottom, int Start, int Len)>();
        for (int y = y0; y <= y1; y++)
        {
            for (int x = x0; x <= x1; x++)
            {
                if (!d[x, y] || d[x - 1, y]) continue;
                int r = x; while (d[r + 1, y] && r - x < most) r++;
                int len = r - x + 1;
                if (len < 0.8 * s || len > 2.2 * s) { x = r; continue; }
                int i = bars.FindIndex(b => b.Bottom == y - 1 && Math.Abs(b.Start - x) <= 0.15 * s);
                if (i >= 0) bars[i] = (bars[i].Top, y, bars[i].Start, Math.Max(bars[i].Len, len));
                else bars.Add((y, y, x, len));
                x = r;
            }
        }
        // (a bar stands clear: white just above and below it all along -- a stretch of staff line
        // between two stems, or a slanting beam's rows, do not)
        bool Clear(int y, int from, int len) { int n = 0; for (int x = from; x < from + len; x++) if (d[x, y]) n++; return n <= 0.05 * len; }
        // (two rows off: an edge row may hold a shorter run)
        bars.RemoveAll(b => b.Bottom - b.Top + 1 > 0.4 * s || b.Top - 2 < 0 || b.Bottom + 2 >= d.H
                            || !Clear(b.Top - 2, b.Start, b.Len) || !Clear(b.Bottom + 2, b.Start, b.Len));
        foreach (var a in bars)
            foreach (var b in bars)
            {
                double gap = (b.Top + b.Bottom - a.Top - a.Bottom) / 2.0;
                if (gap < 0.25 * s || gap > 0.7 * s) continue;
                if (Math.Abs(a.Start - b.Start) > 0.2 * s || Math.Abs(a.Len - b.Len) > 0.25 * s) continue;
                if (Math.Abs((a.Top + b.Bottom) / 2.0 - h.Y) < 0.6 * s) return true;
            }
        return false;
    }

    /// <param name="rulesRead">the rules read the page's ink: they find nine in ten of the detected black heads</param>
    static string RulesHollowHeads(Img d, Img ink, List<Comp> inkHoles, int[] labels, List<Comp> comps, List<Staff> staves, double s, PageSymbols ps, out bool rulesRead)
    {
        // Only where the rules can read the page: the toner hollows black heads, beams and clefs into
        // holes of every size, and there the rules' hollow heads are mostly none (fill 99 -> 88-96).
        // How well the rules read the ink is seen from the heads both look for: the detected black
        // heads the rules' own black-head search finds as well.
        rulesRead = false;
        var black = ps.Heads.Where(h => !h.Hollow).ToList();
        if (black.Count < 10) return "";
        var filled = new List<Head>();
        FindFilledHeads(ink, staves, s, filled);
        int agree = black.Count(h => filled.Any(f => Math.Abs(f.X - h.X) < 0.5 * s && Math.Abs(f.Y - h.Y) < 0.5 * s));
        double share = agree / (double)black.Count;
        if (share < 0.9) return $"rules' hollow heads not looked for (rules find {share:P0} of the black heads)";
        rulesRead = true;
        // (the detected heads go in first: a hollow head touching a black one is measured against it)
        var found = new List<Head>(ps.Heads);
        FindHollowHeads(ink, inkHoles, labels, comps, staves, s, found);
        FindHollowHeads(ink, SpacedHoles(d, staves, s), labels, comps, staves, s, found);
        int added = 0;
        var at = new List<string>();
        foreach (var h in found.Skip(ps.Heads.Count))
        {
            // (a hole in something the detector sees: a clef's loop stands off the clef's centre, a
            // double flat's off the pair's, the white between two beams by a stem's tip -- meters006,
            // piano007; a tie's or slur's end stands by a head)
            // (the weak crosses and percent signs, not yet confirmed, are not the detector seeing something)
            // (nor a hollow head a third off: its chord's other note, two whole notes in a third
            // drawn as one 8 and seen as one -- MuseJazz's and Finale Broadway's slurs008, <c, e,>1)
            if (ps.Detections.Any(t => t.Score <= Detector.Threshold ? false : t.Class.StartsWith("clef")
                    ? Math.Abs(t.X - h.X) < 1.8 * s && Math.Abs(t.Y - h.Y) < 4.5 * s
                    : !t.Class.StartsWith("tie") && !t.Class.StartsWith("slur")
                      && !(t.Class is "head_whole" or "head_half" && h.Hollow && Math.Abs(t.Y - h.Y) > 0.75 * s)
                      && Math.Abs(t.X - h.X) < 1.0 * s && Math.Abs(t.Y - h.Y) < 1.2 * s)) continue;
            if (h.X < h.St.Left || h.X > h.St.Right) continue;
            // (a barline as its rim: the white between two lines closed by a flag and the barline --
            // lied004's grace note just before one; no head touches a barline)
            if (ps.Detections.Any(t => t.Class == "barline" && t.Score > Detector.Threshold && Math.Abs(t.Y - h.Y) < 6 * s
                                       && (Math.Abs(t.X - h.R) < 0.3 * s || Math.Abs(t.X - h.L) < 0.3 * s))) continue;
            if (ps.Heads.Skip(ps.Heads.Count - added).Any(o => Math.Abs(o.X - h.X) < 0.5 * s && Math.Abs(o.Y - h.Y) < 0.5 * s)) continue;
            if (DebugAt.HasValue && Math.Abs(h.X - DebugAt.Value.X) < 2 * s && Math.Abs(h.Y - DebugAt.Value.Y) < 3 * s)
                Console.WriteLine($"rules' hollow head at {h.X:F0},{h.Y:F0} pos {h.Pos}{(h.WholeShape ? " (whole)" : "")}, nothing detected there");
            ps.Heads.Add(h);
            added++;
            if (at.Count < 12) at.Add($"{h.X:F0},{h.Y:F0}");
        }
        return $"rules' hollow heads {added}{(added > 0 ? " at " + string.Join(" ", at) : "")} (rules find {share:P0} of the black heads)";
    }

    /// <summary>
    /// A head in a space has its thin rim on the staff lines, and removing the lines opens it: the
    /// holes of the page with the lines in place, oval and centred in a space.
    /// </summary>
    static List<Comp> SpacedHoles(Img d, List<Staff> staves, double s) => Components.Holes(d).Where(c =>
    {
        var st = Nearest(staves, c.Cy, s);
        if (st == null) return false;
        int pos = (int)Math.Round(st.Pos(c.Cy));
        if (DebugAt.HasValue && Math.Abs(c.Cx - DebugAt.Value.X) < s && Math.Abs(c.Cy - DebugAt.Value.Y) < s)
            Console.WriteLine($"dark hole at {c.Cx:F0},{c.Cy:F0}: {c.W / s:F2} x {c.H / s:F2} pos {pos} fill {c.Fill:F2} top {RowWidth(d, c, c.MinY + 1) / (double)c.W:F2} bottom {RowWidth(d, c, c.MaxY - 1) / (double)c.W:F2}");
        if (Math.Abs(pos) > 15 || (pos & 1) == 0 || c.Fill > 0.88) return false;
        // an oval narrows at its top and bottom rows; a gap closed by staff lines is flat there
        return RowWidth(d, c, c.MinY + 1) < 0.72 * c.W && RowWidth(d, c, c.MaxY - 1) < 0.72 * c.W;
    }).ToList();

    static void FindFilledHeads(Img ink, List<Staff> staves, double s, List<Head> heads)
    {
        int w = ink.W, h = ink.H;
        int r = (int)Math.Round(s * 0.28), side = 2 * r + 1, full = side * side;
        var integ = new int[(w + 1) * (h + 1)];
        for (int y = 0; y < h; y++)
        {
            int row = 0;
            for (int x = 0; x < w; x++)
            {
                row += ink.D[y * w + x] ? 1 : 0;
                integ[(y + 1) * (w + 1) + x + 1] = integ[y * (w + 1) + x + 1] + row;
            }
        }
        var solid = new bool[w * h];
        for (int y = r; y < h - r - 1; y++)
            for (int x = r; x < w - r - 1; x++)
            {
                int x0 = x - r, y0 = y - r, x1 = x + r + 1, y1 = y + r + 1;
                int sum = integ[y1 * (w + 1) + x1] - integ[y0 * (w + 1) + x1] - integ[y1 * (w + 1) + x0] + integ[y0 * (w + 1) + x0];
                solid[y * w + x] = sum == full;
            }
        var (_, labelled) = Components.Label(w, h, i => solid[i], eight: false);
        // heads stacked a third apart touch, and so do their cores: cut a tall core at its necks
        // (or, with none, into as many heads as its height holds, a space apart)
        var comps = new List<Comp> { null };
        foreach (var c in labelled.Skip(1))
        {
            if (c.W < 0.3 * s || c.W > 1.3 * s || c.H <= 0.8 * s || c.H > 5 * s) { comps.Add(c); continue; }
            var width = new int[c.H];
            for (int y = 0; y < c.H; y++)
                for (int x = c.MinX; x <= c.MaxX; x++) if (solid[(c.MinY + y) * w + x]) width[y]++;
            // a neck is a run of thin rows (at a low resolution it can be several rows long): the
            // heads are the runs of wide rows between the necks, and the neck rows belong to none
            int max = width.Max();
            var runs = new List<(int From, int To)>();
            for (int y = 0; y < c.H;)
            {
                if (width[y] <= 0.55 * max) { y++; continue; }
                int a = y;
                while (y < c.H && width[y] > 0.55 * max) y++;
                runs.Add((a, y));
            }
            // no neck at all: as many heads as its height holds, a space apart
            if (runs.Count == 1)
            {
                int k = (int)Math.Round((c.H / s + 0.56) / 1.0);
                runs.Clear();
                for (int j = 0; j < k; j++) runs.Add((c.H * j / k, c.H * (j + 1) / k));
            }
            if (DebugAt.HasValue && Math.Abs(c.Cx - DebugAt.Value.X) < 2 * s && Math.Abs(c.Cy - DebugAt.Value.Y) < 3 * s)
                Console.WriteLine($"tall core at {c.Cx:F0},{c.Cy:F0}: {c.W / s:F2} x {c.H / s:F2}, widths {string.Join(" ", width)}, runs {string.Join(" ", runs.Select(r => $"{r.From}-{r.To}"))}");
            foreach (var (from, to) in runs)
            {
                var seg = new Comp();
                for (int y = from; y < to; y++)
                    for (int x = c.MinX; x <= c.MaxX; x++)
                        if (solid[(c.MinY + y) * w + x])
                        {
                            seg.N++; seg.Sx += x; seg.Sy += c.MinY + y;
                            seg.MinX = Math.Min(seg.MinX, x); seg.MaxX = Math.Max(seg.MaxX, x);
                            seg.MinY = Math.Min(seg.MinY, c.MinY + y); seg.MaxY = Math.Max(seg.MaxY, c.MinY + y);
                        }
                if (seg.N > 0 && seg.H >= 0.15 * s) comps.Add(seg);
            }
        }
        // (a beam a hair thicker than the window leaves a sliver of a core: a head's is a third of a space)
        var cores = comps.Skip(1).Where(c => c.W >= 0.3 * s && c.W <= 1.3 * s && c.H >= 0.2 * s && c.H <= 0.8 * s).ToList();
        for (int i = 1; i < comps.Count; i++)
        {
            var c = comps[i];
            if (DebugAt.HasValue && Math.Abs(c.Cx - DebugAt.Value.X) < 2 * s && Math.Abs(c.Cy - DebugAt.Value.Y) < 3 * s)
                Console.WriteLine($"core at {c.Cx:F0},{c.Cy:F0}: {c.W / s:F2} x {c.H / s:F2}");
            if (c.W < 0.3 * s || c.W > 1.3 * s || c.H < 0.2 * s || c.H > 0.8 * s) continue;
            var st = Nearest(staves, c.Cy, s);
            if (st == null) continue;
            var hd = new Head { St = st, X = c.Cx, Y = c.Cy, Hollow = false };
            int y0 = (int)Math.Round(c.Cy);
            int xl = (int)Math.Round(c.Cx), xr = xl;
            // a head is about 1.2 spaces wide; a neighbour touching it (a second, or another
            // voice's head in unison) must not stretch it
            while (ink[xl - 1, y0] && c.Cx - xl < 0.75 * s) xl--;
            while (ink[xr + 1, y0] && xr - c.Cx < 0.75 * s) xr++;
            hd.L = xl; hd.R = xr;
            // ink running on past a head's width is a beam or a clef's body, unless it runs into
            // another head (a second, or a unison in another voice). A ledger line through the
            // head's middle row runs on too (into a flag or a dot beside it): a beam is thick, so
            // the rows a fifth of a space above and below tell the two apart
            int Run(int yy)
            {
                int fl = xl, fr = xr;
                while (ink[fl - 1, yy] && c.Cx - fl < 1.2 * s) fl--;
                while (ink[fr + 1, yy] && fr - c.Cx < 1.2 * s) fr++;
                return fr - fl;
            }
            int q = (int)(0.2 * s);
            if (Math.Min(Run(y0 - q), Run(y0 + q)) > 1.8 * s && !cores.Any(o => o != c && Math.Abs(o.Cy - c.Cy) < 0.8 * s && Math.Abs(o.Cx - c.Cx) < 1.6 * s)) continue;
            hd.Pos = (int)Math.Round(st.Pos(c.Cy));
            heads.Add(hd);
        }
    }

    static void FindHollowHeads(Img ink, List<Comp> holes, int[] labels, List<Comp> comps, List<Staff> staves, double s, List<Head> heads)
    {
        foreach (var c in holes)
        {
            if (DebugAt.HasValue && Math.Abs(c.Cx - DebugAt.Value.X) < s && Math.Abs(c.Cy - DebugAt.Value.Y) < s)
                Console.WriteLine($"hole at {c.Cx:F0},{c.Cy:F0}: {c.W / s:F2} x {c.H / s:F2}");
            if (c.W < 0.3 * s || c.W > 1.25 * s || c.H < 0.25 * s || c.H > 0.95 * s) continue;
            var st = Nearest(staves, c.Cy, s);
            if (st == null) continue;
            // (a rim is thin: stop before a touching neighbour's ink adds to it)
            int row = (int)Math.Round(c.Cy), col = (int)Math.Round(c.Cx), cap = (int)(0.8 * s);
            int rl = 0; while (rl < cap && ink[c.MinX - 1 - rl, row]) rl++;
            int rr = 0; while (rr < cap && ink[c.MaxX + 1 + rr, row]) rr++;
            int ru = 0; while (ru < cap && ink[col, c.MinY - 1 - ru]) ru++;
            int rd = 0; while (rd < cap && ink[col, c.MaxY + 1 + rd]) rd++;
            // a hollow head a third away in a chord touches this one, and its rim joins the top or
            // bottom run: where such a head stands (its hole, head-sized, a space off), the run
            // counts only as far as a rim goes
            int vrim = (int)(0.22 * s);
            // (the touching head may be black, too: another voice's note on the chord)
            bool HoleAt(int dir) => holes.Any(o => o != c && o.W >= 0.9 * s && o.W <= 1.25 * s && o.H >= 0.6 * s && o.H <= 0.95 * s
                                                 && Math.Abs(o.Cx - c.Cx) < 0.4 * s
                                                 && (o.Cy - c.Cy) * dir > 0.6 * s && (o.Cy - c.Cy) * dir < 1.4 * s)
                                 || (c.W >= 0.9 * s && c.H >= 0.6 * s     // (this hole is itself head-sized, not a gap between black heads)
                                     && heads.Any(o => !o.Hollow && Math.Abs(o.X - c.Cx) < 0.5 * s
                                                    && (o.Y - c.Cy) * dir > 0.6 * s && (o.Y - c.Cy) * dir < 1.4 * s));
            if (ru > vrim && HoleAt(-1)) ru = vrim;
            if (rd > vrim && HoleAt(1)) rd = vrim;
            double ow = c.W + rl + rr, oh = c.H + ru + rd;
            if (DebugAt.HasValue && Math.Abs(c.Cx - DebugAt.Value.X) < s && Math.Abs(c.Cy - DebugAt.Value.Y) < s)
                Console.WriteLine($"  rings l{rl / s:F2} r{rr / s:F2} u{ru / s:F2} d{rd / s:F2} outer {ow / s:F2} x {oh / s:F2}");
            if (rl < 0.06 * s || rr < 0.06 * s) continue;
            // a whole note: the ink around the hole is a note-sized blob, the hole near its middle
            int lab = labels[row * ink.W + c.MinX - 1];
            var ring = lab > 0 ? comps[lab] : null;
            if (DebugAt.HasValue && ring != null && Math.Abs(c.Cx - DebugAt.Value.X) < s && Math.Abs(c.Cy - DebugAt.Value.Y) < s)
                Console.WriteLine($"  ring comp {ring.W / s:F2} x {ring.H / s:F2} at {ring.MinX},{ring.MinY}");
            if (ring != null && ring.W >= 1.3 * s && ring.W <= 2.0 * s && ring.H >= 0.85 * s && ring.H <= 1.4 * s
                && ring.W >= 1.3 * ring.H   // a whole note is wide; the loop of a digit is not
                && Math.Abs(c.Cx - (ring.MinX + ring.MaxX) / 2.0) < 0.25 * ring.W && Math.Abs(c.Cy - (ring.MinY + ring.MaxY) / 2.0) < 0.25 * ring.H)
            {
                heads.Add(new Head
                {
                    St = st, X = c.Cx, Y = c.Cy, Hollow = true, WholeShape = true,
                    L = ring.MinX, R = ring.MaxX, OuterW = ring.W,
                    Pos = (int)Math.Round(st.Pos(c.Cy)),
                });
                continue;
            }
            if (ow < 0.95 * s || ow > 2.2 * s || oh < 0.7 * s || oh > 1.35 * s) continue;
            // the head's own edges: a rim is thin, so a touching neighbour (a second, a unison in
            // another voice) is cut off where the stem is looked for
            int rim = (int)(0.45 * s);
            heads.Add(new Head
            {
                St = st, X = c.Cx, Y = c.Cy, Hollow = true,
                L = c.MinX - Math.Min(rl, rim), R = c.MaxX + Math.Min(rr, rim), OuterW = ow,
                Pos = (int)Math.Round(st.Pos(c.Cy)),
            });
        }
    }

    static void Dedup(List<Head> heads, double s)
    {
        for (int i = 0; i < heads.Count; i++)
            for (int j = i + 1; j < heads.Count && heads[j].X - heads[i].X < 0.4 * s; j++)
                if (heads[j].St == heads[i].St && Math.Abs(heads[j].Y - heads[i].Y) < 0.4 * s
                    // (two voices' unison: one head, a stem each way)
                    && !(heads[i].HasStem && heads[j].HasStem && heads[i].Up != heads[j].Up && Math.Abs(heads[i].StemX - heads[j].StemX) >= 0.6 * s))
                {
                    heads.RemoveAt(j);
                    j--;
                }
    }
}
