namespace Omr;

static partial class Layout
{
    /// <summary>
    /// The page's staves in systems of one staff per part. A score may hide a part's staff in the
    /// systems where it only rests (a song's voice in the piano's introduction): then a system shows
    /// fewer staves, and counting the staves off by the parts throws every system after out. A
    /// system's staves are joined at their left end by the line that opens it; when the staves do
    /// not fall in whole systems so joined, they are grouped by that line instead, and a system
    /// short of staves has its parts told later (<see cref="AssignParts"/>; until then its staves
    /// take the last parts).
    /// </summary>
    /// <param name="det">the learnt detector's symbols, when it read the page: a staff no line joins to a
    /// neighbour is the neighbour's system's where their barlines stand at the same columns (<see cref="SharedDetectedBars"/>)</param>
    public static List<SystemInfo> GroupSystems(List<Staff> staves, int partsPerSystem, Img d, double s, List<Detection> det = null)
    {
        if (partsPerSystem < 2 || staves.Count == 0) return GroupSystems(staves, partsPerSystem);
        var joined = new bool[staves.Count];     // joined[i]: staff i and the one below it open one system
        for (int i = 0; i + 1 < staves.Count; i++) joined[i] = Opened(d, staves[i], staves[i + 1], s) >= 0;
        bool whole = staves.Count % partsPerSystem == 0;
        for (int i = 0; whole && i < staves.Count; i++)
            if (i % partsPerSystem != partsPerSystem - 1 && !joined[i]) whole = false;
        if (whole) return GroupSystems(staves, partsPerSystem);
        var groups = new List<List<Staff>> { new() { staves[0] } };
        for (int i = 1; i < staves.Count; i++)
        {
            if (joined[i - 1]) groups[^1].Add(staves[i]);
            else groups.Add(new() { staves[i] });
        }
        if (TraceJoins) Console.WriteLine($"  opening lines group the staves {string.Join(" ", groups.Select(g => g.Count))} of {partsPerSystem} parts");
        // (a staff no line joins to its neighbours: an old print's voice over its piano, the piano's
        // staves braced and the voice's alone above them -- Kinkel Op.7, Brandt 1838. It is the system's
        // of the neighbouring staves it makes a system with, with the one below first (the hidden
        // staves are the upper ones), or whichever the detector's barlines put it with)
        for (int i = 0; i < groups.Count; i++)
        {
            if (groups[i].Count != 1) continue;
            int below = i + 1 < groups.Count && groups[i + 1].Count + 1 <= partsPerSystem ? i + 1 : -1;
            int above = i > 0 && groups[i - 1].Count + 1 <= partsPerSystem ? i - 1 : -1;
            int with = below >= 0 ? below : above;
            if (det != null && below >= 0 && above >= 0 && !SharedDetectedBars(groups[i][0], groups[below][0], det, s) && SharedDetectedBars(groups[above][^1], groups[i][0], det, s)) with = above;
            if (with < 0) continue;
            if (TraceJoins) Console.WriteLine($"  the staff at y {groups[i][0].Top:F0} alone: {(with == below ? "the system's below" : "the system's above")}");
            if (with == below) { groups[with].Insert(0, groups[i][0]); groups.RemoveAt(i); i--; }
            else { groups[with].Add(groups[i][0]); groups.RemoveAt(i); i -= 2; }
        }
        // (a line that opens no system, or one missed: what the opening lines say is not a score of these parts.
        // How far apart the staves stand tells nothing: a system's staves stand wider apart than two
        // systems where high and low notes come between them -- lied003, lied010. A system of one staff
        // is a scan's opening line broken, more likely than a score that hides all its staves but one:
        // the colour scan piano002 read by the rules alone, its staves missed, went from pairs to singles)
        if (groups.Any(g => g.Count > partsPerSystem || g.Count == 1)) return GroupSystems(staves, partsPerSystem);
        var systems = new List<SystemInfo>();
        foreach (var g in groups)
        {
            var sys = new SystemInfo { Index = systems.Count };
            for (int k = 0; k < g.Count; k++)
            {
                g[k].System = sys.Index;
                g[k].Part = partsPerSystem - g.Count + k;
                sys.Staves.Add(g[k]);
            }
            sys.All.AddRange(sys.Staves);
            systems.Add(sys);
        }
        return systems;
    }

    /// <summary>
    /// The staves one opening line joins, most on the page, by kind top to bottom ("" a five-line
    /// staff, "tab", "line"): the parts, and the staves beside them, of a score with no score file
    /// (omr read). Of two runs as long, the one with more five-line staves.
    /// </summary>
    /// <param name="joins">per pair of neighbours in the run returned: what joins them left of the opening line (<see cref="Joiner"/>: "brace", "bracket", "both" or "")</param>
    /// <param name="det">the learnt detector's symbols, when it read the page: two staves whose opening line the
    /// toner broke are one system's where their barlines stand at the same columns (<see cref="SharedDetectedBars"/>)</param>
    public static List<string> SystemKinds(Img d, List<Staff> staves, List<Staff> extras, double s, List<string> joins = null, List<Detection> det = null)
    {
        var all = staves.Concat(extras).OrderBy(st => st.Top).ToList();
        var best = new List<string>();
        var bestJoins = new List<string>();
        var run = new List<string>();
        var runJoins = new List<string>();
        // (the page's usual start: a scan's staff whose first spaces the toner took starts late, or early
        // where speckle bridged into its margin, and the line stands at the usual start, outside a window
        // about the two staves' own -- Emmentaler band010, Left 178 for 207)
        var lefts = all.Select(st => st.Left).OrderBy(v => v).ToList();
        int usual = lefts.Count > 0 ? lefts[lefts.Count / 2] : -1;
        // (the whole lines first: a broken one is looked for where the page's whole lines stand, within a
        // space -- the toner breaks a line here and there, not every line of the page; and a column of
        // ink elsewhere in the window, the strings of a lead sheet's chord diagrams over an indented first
        // system, is no line)
        var opened = new int[all.Count];
        for (int i = 1; i < all.Count; i++) opened[i] = Opened(d, all[i - 1], all[i], s, usual);
        var xs = opened.Skip(1).Where(x => x >= 0).OrderBy(x => x).ToList();
        int xLine = xs.Count > 0 ? xs[xs.Count / 2] : -1;
        for (int i = 0; i < all.Count; i++)
        {
            if (i > 0)
            {
                int xo = opened[i];
                // (no whole line between them: a scan's broken one, or two systems. What is left of a broken
                // line tells first, then the detector's barlines; with neither, two systems. What joins the
                // two beyond a line the barlines alone vouch for is left unknown)
                bool broken = xo < 0;
                if (broken) xo = OpenedBroken(d, all[i - 1], all[i], s, usual, xLine);
                if (xo >= 0) runJoins.Add(Joiner(d, xo, all[i - 1], all[i], s, broken));
                else if (det != null && SharedDetectedBars(all[i - 1], all[i], det, s)) runJoins.Add("");
                else { run = new List<string>(); runJoins = new List<string>(); }
            }
            run.Add(all[i].Kind);
            if (run.Count > best.Count || run.Count == best.Count && run.Count(k => k == "") > best.Count(k => k == "")) { best = new List<string>(run); bestJoins = new List<string>(runJoins); }
        }
        // (a system opens with a part's staff: a TAB or a line above the first part is none of the score's)
        while (best.Count > 0 && best[0] != "") { best.RemoveAt(0); if (bestJoins.Count > 0) bestJoins.RemoveAt(0); }
        joins?.AddRange(bestJoins);
        return best;
    }

    /// <summary>
    /// The line that opens a system runs from the upper staff down to the lower at their left end:
    /// the first column of the rightmost run of columns that do, or -1. (A brace's middle, left of
    /// the line, runs down the gap too where the gap is short -- MuseScore's brace over a guitar's
    /// staff and its TAB, 0.7 spaces left of the line -- so the leftmost run is not always the line's.)
    /// </summary>
    /// <param name="usual">the page's usual staff start, when known: the window takes it in too</param>
    static int Opened(Img d, Staff a, Staff b, double s, int usual = -1)
    {
        // (the window: a space and a half left of the nearer start to a space right of the further --
        // a TAB starts at its "TAB" letters, 1.1 spaces left of the line, and the line fell outside
        // a window about its start alone: MuseScore's band002 and band008)
        int left = Math.Min(a.Left, b.Left), right = Math.Max(a.Left, b.Left);
        if (usual >= 0) { left = Math.Min(left, usual); right = Math.Max(right, usual); }
        int x0 = Math.Max(0, (int)(left - 1.5 * s));
        for (int x = Math.Min(d.W - 1, (int)(right + 1.0 * s)); x >= x0; x--)
            if (Spans(d, x, a, b))
            {
                while (x - 1 >= x0 && Spans(d, x - 1, a, b)) x--;
                return x;
            }
        // (a scan's line that leans: no one column is dark down the gap, the line drifting a third of a
        // space over a voice's lyrics -- Kinkel Op.10 no.6's second page, 276 to 285 over 385 rows)
        for (int x = Math.Min(d.W - 2, (int)(right + 1.0 * s)); x >= Math.Max(1, x0); x--)
            if (SpansLeaning(d, x, a, b, s)) return x;
        return -1;
    }

    /// <summary>Whether a line from column x at the upper staff's bottom runs down to the lower's top, following its ink a column aside a row (half a space at most), dark nine rows in ten.</summary>
    static bool SpansLeaning(Img d, int x, Staff a, Staff b, double s)
    {
        int y0 = (int)a.Bottom + 2, y1 = (int)b.Top - 2, dark = 0, cx = x;
        if (y1 <= y0 || !d[x, y0]) return false;
        for (int y = y0; y <= y1; y++)
        {
            if (d[cx, y]) dark++;
            else if (cx - 1 >= 0 && Math.Abs(cx - 1 - x) <= 0.5 * s && d[cx - 1, y]) { cx--; dark++; }
            else if (cx + 1 < d.W && Math.Abs(cx + 1 - x) <= 0.5 * s && d[cx + 1, y]) { cx++; dark++; }
        }
        return dark >= 0.9 * (y1 - y0 + 1);
    }

    /// <summary>
    /// The opening line a scan's toner broke, where <see cref="Opened"/> finds no column dark nine rows in
    /// ten: the darkest column of the same window, when it is dark on a row in four between the staves
    /// and no stretch of it is white for two spaces (the toner's breaks are short, a line's end is not), or -1.
    /// (Degraded MuseScore band pages: the broken lines left 26 to 89 percent of their rows, the columns
    /// between two systems 2 to 19 -- a one-line staff's to the next system's first the most.)
    /// For omr read's guess of the staves alone: the reading proper has the score file's parts to go by.
    /// </summary>
    /// <param name="xLine">the column of the page's whole opening lines, when any was found: the broken one stands within a space of it</param>
    static int OpenedBroken(Img d, Staff a, Staff b, double s, int usual = -1, int xLine = -1)
    {
        int left = Math.Min(a.Left, b.Left), right = Math.Max(a.Left, b.Left);
        if (usual >= 0) { left = Math.Min(left, usual); right = Math.Max(right, usual); }
        int x0 = Math.Max(0, (int)(left - 1.5 * s)), x1 = Math.Min(d.W - 1, (int)(right + 1.0 * s));
        if (xLine >= 0) { x0 = Math.Max(0, (int)(xLine - 1.0 * s)); x1 = Math.Min(d.W - 1, (int)(xLine + 1.0 * s)); }
        int y0 = (int)a.Bottom + 2, y1 = (int)b.Top - 2, rows = y1 - y0 + 1;
        // (two staves of one system stand within a dozen spaces -- Leland band004, 11.7 with a verse of
        // lyrics between; 35 spaces apart they are two systems' with the staves between them lost, and
        // the two systems' lines with the short gap between them read as one broken line: Leland band009)
        if (rows < 4 || rows > 16 * s) return -1;
        // (the toner breaks a line into short gaps; a long white stretch is no break but the line's end:
        // a system's line ends at its last staff, and where that is a one-line staff the finder missed,
        // the line reaches half way down the gap to the next system -- Bravura band010's fourth page,
        // 164 of 355 rows, the lower 190 white)
        int bestX = -1, bestDark = -1, bestGap = 0;
        for (int x = x0; x <= x1; x++)
        {
            int dark = 0, gap = 0, longest = 0;
            for (int y = y0; y <= y1; y++)
                if (d[x, y]) { dark++; gap = 0; }
                else longest = Math.Max(longest, ++gap);
            if (dark > bestDark) { bestDark = dark; bestX = x; bestGap = longest; }
        }
        // (the toner's breaks ran to 15 rows, under a space; a line's end 120-190 rows, 6 spaces and more --
        // the next system's line counted with it where the staves between were lost: FinaleMaestro band010)
        bool opens = bestX >= 0 && bestDark >= 0.25 * rows && bestGap <= 2 * s;
        if (TraceJoins) Console.WriteLine($"  no whole line opens {a.Kind}@{a.Top:F0}-{b.Kind}@{b.Top:F0}: the window x {x0}-{x1} is darkest at x {bestX}, {bestDark} of {rows} rows, the longest break {bestGap} -> {(opens ? "a broken line" : "no line")}");
        return opens ? bestX : -1;
    }

    /// <summary>
    /// Whether the learnt detector's barlines say the two neighbouring staves are one system's: the
    /// barlines it sees on the one stand at the same columns as the other's (within three tenths of a
    /// space), two of them at least and half of the fewer staff's, the first and last barline of each
    /// left out -- every system of a page starts and ends at the same columns, and two systems' staves
    /// would agree there. (The opening line itself a scan's toner broke, which Opened then misses.)
    /// </summary>
    static bool SharedDetectedBars(Staff a, Staff b, List<Detection> det, double s)
    {
        List<double> Bars(Staff st) => det.Where(t => t.Class == "barline" && t.Score > Detector.Threshold
                                                      && (st.Kind == "tab" ? t.Y > st.Top - 0.5 * s && t.Y < st.Bottom + 0.5 * s : Math.Abs(t.Y - st.Mid) < 1.5 * s))
                                          .Select(t => t.X).OrderBy(x => x).ToList();
        var xa = Bars(a); var xb = Bars(b);
        if (xa.Count < 3 || xb.Count < 3) { if (TraceJoins) Console.WriteLine($"  barlines {a.Kind}@{a.Top:F0} {xa.Count}, {b.Kind}@{b.Top:F0} {xb.Count}: too few to tell one system from two"); return false; }
        // (the ends: the outermost barlines, and any within a space of them -- a repeat's double bar)
        List<double> Inner(List<double> xs) => xs.Where(x => x > xs[0] + 1.0 * s && x < xs[^1] - 1.0 * s).ToList();
        var ia = Inner(xa); var ib = Inner(xb);
        int shared = ia.Count(x => ib.Any(y => Math.Abs(x - y) <= 0.3 * s));
        bool one = shared >= 2 && shared >= 0.5 * Math.Min(ia.Count, ib.Count);
        if (TraceJoins) Console.WriteLine($"  barlines {a.Kind}@{a.Top:F0} {ia.Count} inner of {xa.Count}, {b.Kind}@{b.Top:F0} {ib.Count} of {xb.Count}: {shared} at the same columns -> {(one ? "one system" : "not one system")}");
        return one;
    }

    /// <summary>
    /// What joins two staves beyond the opening line that runs between them, at its column
    /// <paramref name="xo"/>: a brace (a grand staff's, "grandStaff"), a bracket (a group's,
    /// "staffGroup"), both (a piano within a group: the brace inside the bracket), or nothing
    /// but the line (""). The bracket is a bar left of the line, a third of a space or thicker
    /// (the line is under a fifth), whose hooks reach out past the staves' outer lines (a thick
    /// barline's ends at them). The brace stands in the three and a half spaces left of the
    /// line (or of the bracket's bar): on nearly every row from the upper staff's top to the
    /// lower's bottom, the ink nearest the line is a thin curve, from a tenth of a space at its
    /// tips and its middle point to four tenths in its two lobes (MuseJazz's hand-drawn brace,
    /// a quarter to nine tenths), bulging left by half a space and more (a part's name there is a few rows
    /// tall; the Lab's piano-collide and sonatina, 0.8-0.9 spaces, widths 4-33 pixels at 56 to
    /// the space; MuseScore's eight fonts, 0.05-0.87 spaces wide, bulging 0.9-1.1).
    /// </summary>
    /// <summary>With it, FindStaves tells what it found and dropped, and Joiner what it measured between each two staves (omr read --parts-only, on stderr).</summary>
    public static bool TraceJoins;

    /// <param name="broken">the line at <paramref name="xo"/> is one a scan's toner broke (<see cref="OpenedBroken"/>): the brace
    /// beside it is broken too, and is seen on seven rows in ten rather than 85 in 100 (the degraded MuseScore band
    /// pages' braces 74-83 percent; the ink left of a line with no brace, a TAB's letters, 58 at most)</param>
    static string Joiner(Img d, int xo, Staff a, Staff b, double s, bool broken = false)
    {
        int yTop = (int)Math.Round(a.Top), yBot = (int)Math.Round(b.Bottom);
        // the line's own run of columns dark all the way between the staves
        int xe = xo;
        while (xe + 1 < d.W && Spans(d, xe + 1, a, b)) xe++;
        int lineW = xe - xo + 1;
        // the bracket's bar: the first run of its own left of the line, within a space and a half,
        // a third of a space or thicker and half again as thick as the line (Lily#: 0.45 spaces,
        // standing 0.8 left of a 0.16 line; a scan's blur thickens the line to 0.3 and the
        // line's own run, hooked or not, is no bar under two fifths); a thinner run there is a
        // brace's middle, which runs down a short gap too
        int xb0 = -1, xb1 = -1;
        for (int x = xo - 1; x >= Math.Max(0, xo - 1.5 * s); x--)
            if (Spans(d, x, a, b))
            {
                int l = x;
                while (l - 1 >= 0 && Spans(d, l - 1, a, b)) l--;
                if (x - l + 1 >= 0.3 * s && x - l + 1 >= 1.5 * lineW) { xb0 = l; xb1 = x; }
                break;
            }
        if (xb0 < 0 && lineW >= 0.4 * s) { xb0 = xo; xb1 = xe; }
        bool bracket = false;
        if (xb0 >= 0)
        {
            // (its hooks: ink half a space past the outer lines, in the bar's columns or curling
            // rightwards from them, short of the line -- which runs on past an inner pair's staves
            // whatever joins them)
            int reach = (int)(0.5 * s);
            int xh = xb0 == xo ? xe + reach : Math.Min(xb1 + reach, xo - 1);
            bool Hook(int y) { for (int x = xb0; x <= xh && x < d.W; x++) if (d[x, y]) return true; return false; }
            bracket = Hook(yTop - reach) && Hook(yBot + reach);
        }
        // the curve left of the line, or of the bar
        int xl = xb0 >= 0 ? xb0 : xo;
        int x1 = xl - Math.Max(2, (int)(0.2 * s)), x0 = Math.Max(0, xl - (int)(3.5 * s));
        int rows = 0, found = 0;
        var runs = new List<(int Y, int L, int W)>();
        for (int y = yTop; y <= yBot; y += 2)
        {
            rows++;
            int r = -1;
            for (int x = x1; x >= x0; x--) if (d[x, y]) { r = x; break; }
            if (r < 0) continue;
            int l = r;
            while (l - 1 >= x0 && d[l - 1, y]) l--;
            found++;
            runs.Add((y, l, r - l + 1));
        }
        // (a scan's blur thickens the thinnest part to a third of a space and the lobes past a space,
        // or thins a light brace's lobes to a quarter: the lobes are twice the thinnest part, where
        // a bar is one width throughout)
        // (a brace's left edge is a curve: from row to row it moves a pixel or two, and over the whole
        // brace about four times its bulge -- out and back in each half. A speckled scan's rows have
        // ink somewhere left of the line on every row, at random: their edges jump, many times the bulge)
        double wander = 0;
        for (int i = 1; i < runs.Count; i++) wander += Math.Abs(runs[i].L - runs[i - 1].L);
        double bulge = runs.Count > 0 ? runs.Max(q => q.L) - runs.Min(q => q.L) : 0;
        // (clean braces wander 4.3 bulges, degraded ones 11-19; a speckled page's rows with no brace 27-78)
        bool brace = rows > 0 && found >= (broken ? 0.7 : 0.85) * rows
            && runs.Min(q => q.W) <= 0.4 * s && runs.Max(q => q.W) >= Math.Max(0.25 * s, 2 * runs.Min(q => q.W)) && runs.Max(q => q.W) <= 1.2 * s
            && bulge >= 0.5 * s && wander <= 22 * bulge;
        if (TraceJoins)
            Console.WriteLine($"  join {a.Kind}@{yTop}-{b.Kind}@{yBot} s {s:F1}: line x {xo}-{xe} ({lineW / s:F2} sp){(xb0 >= 0 ? $", bar x {xb0}-{xb1} ({(xb1 - xb0 + 1) / s:F2} sp){(bracket ? " hooked" : "")}" : "")}; "
                              + $"left: {found}/{rows} rows, width {(runs.Count > 0 ? runs.Min(q => q.W) / s : 0):F2}-{(runs.Count > 0 ? runs.Max(q => q.W) / s : 0):F2} sp, "
                              + $"left edge moves {bulge / s:F2} sp"
                              + (runs.Count > 0 ? $" (x {runs.Min(q => q.L)}-{runs.Max(q => q.L)}), wandering {wander:F0} = {(bulge > 0 ? wander / bulge : 0):F1} bulges" : "") + $" -> {(brace && bracket ? "both" : brace ? "brace" : bracket ? "bracket" : "none")}");
        return brace && bracket ? "both" : brace ? "brace" : bracket ? "bracket" : "";
    }

    /// <summary>Whether the column x is dark from the upper staff's bottom line to the lower's top line (nine rows in ten: a scan's line breaks).</summary>
    static bool Spans(Img d, int x, Staff a, Staff b)
    {
        int y0 = (int)a.Bottom + 2, y1 = (int)b.Top - 2, dark = 0;
        if (y1 <= y0) return false;
        for (int y = y0; y <= y1; y++) if (d[x, y]) dark++;
        return dark >= 0.9 * (y1 - y0 + 1);
    }

    /// <summary>
    /// Whether two staves of a system share their barlines (a grand staff's, a group's): one of the
    /// system's barlines inside it runs on from the one down to the other. Null with no barline inside.
    /// (Not most of them: the barlines here are those found before the measures are checked, and a
    /// stem taken for one in a staff's long bar of 12/8 outvoted the grand staff's one real barline.
    /// Staves that do not share theirs have no column dark all the way between them.)
    /// </summary>
    static bool? SharedBars(Img d, SystemInfo sys, Staff a, Staff b, double s)
    {
        int n = 0, shared = 0;
        for (int i = 1; i + 1 < sys.Bars.Count; i++, n++)
        {
            int x = (int)Math.Round(sys.Bars[i]);
            for (int dx = -(int)(0.3 * s); dx <= 0.3 * s; dx++)
                if (x + dx >= 0 && x + dx < d.W && Spans(d, x + dx, a, b)) { shared++; break; }
        }
        return n == 0 ? null : shared > 0;
    }

    /// <summary>
    /// The parts of the staves of a system that shows fewer staves than the score has parts (the
    /// others hidden where they rest). Of the ways to give its staves parts in order, the one where
    /// the clefs read are the parts' clefs, and staves that share their barlines are parts that do
    /// (as the systems that show every staff tell, <paramref name="shares"/>, kept from page to page),
    /// or else neighbours. Called once the clefs are read, before anything goes by part.
    /// </summary>
    /// <param name="clefOf">the clef a part has (in force, or as the score file says)</param>
    /// <param name="shares">per part p: how often p and p+1 were seen sharing their barlines, and not</param>
    public static void AssignParts(Img d, List<SystemInfo> systems, List<Sig> sigs, double s, int parts, Func<int, string> clefOf,
                                   Dictionary<int, (int Yes, int No)> shares, List<string> notes)
    {
        static string Kind(string clef) => clef?.Split('_')[0];
        foreach (var sys in systems.Where(x => x.Staves.Count == parts))
            for (int k = 0; k + 1 < parts; k++)
                if (SharedBars(d, sys, sys.Staves[k], sys.Staves[k + 1], s) is bool sh)
                {
                    var (yes, no) = shares.GetValueOrDefault(k);
                    shares[k] = sh ? (yes + 1, no) : (yes, no + 1);
                }
        foreach (var sys in systems.Where(x => x.Staves.Count < parts))
        {
            int n = sys.Staves.Count;
            var clefs = sys.Staves.Select(st => Kind(sigs.Where(g => g.St == st && g.Clef != null && !g.Mid).OrderBy(g => g.X).FirstOrDefault()?.Clef)).ToList();
            var joined = Enumerable.Range(0, n - 1).Select(k => SharedBars(d, sys, sys.Staves[k], sys.Staves[k + 1], s)).ToList();
            int[] best = null;
            int bestScore = int.MinValue;
            var pick = new int[n];
            void Walk(int k, int from)
            {
                if (k == n)
                {
                    int score = 0;
                    for (int i = 0; i < n; i++)
                        if (clefs[i] != null && Kind(clefOf(pick[i])) is string want) score += clefs[i] == want ? 2 : -2;
                    for (int i = 0; i + 1 < n; i++)
                    {
                        if (joined[i] is not bool j) continue;
                        bool next = pick[i + 1] == pick[i] + 1;
                        if (next && shares.TryGetValue(pick[i], out var seen) && seen.Yes != seen.No) score += j == seen.Yes > seen.No ? 2 : -2;
                        else if (next && j) score += 1;
                        else if (!next && j) score -= 1;
                    }
                    // (alike, the staves hidden are the upper ones: a song's voice over its piano)
                    if (score > bestScore || score == bestScore && pick.Sum() > best.Sum()) { bestScore = score; best = (int[])pick.Clone(); }
                    return;
                }
                for (int p = from; p <= parts - (n - k); p++) { pick[k] = p; Walk(k + 1, p + 1); }
            }
            Walk(0, 0);
            for (int k = 0; k < n; k++) sys.Staves[k].Part = best[k];
            notes.Add($"system {sys.Index + 1}: {n} of {parts} staves, parts {string.Join(" ", best)} (clefs {string.Join(" ", clefs.Select(c => c ?? "-"))}, barlines shared {string.Join(" ", joined.Select(j => j is bool b ? (b ? "yes" : "no") : "-"))})");
        }
    }

    public static List<SystemInfo> GroupSystems(List<Staff> staves, int partsPerSystem)
    {
        var systems = new List<SystemInfo>();
        for (int i = 0; i + partsPerSystem <= staves.Count; i += partsPerSystem)
        {
            var sys = new SystemInfo { Index = systems.Count };
            for (int k = 0; k < partsPerSystem; k++)
            {
                var st = staves[i + k];
                st.System = sys.Index;
                st.Part = k;
                sys.Staves.Add(st);
            }
            sys.All.AddRange(sys.Staves);
            systems.Add(sys);
        }
        return systems;
    }

    /// <summary>
    /// Systems of a score whose systems hold TABs or one-line staves (<paramref name="kinds"/>, the
    /// config's staves top to bottom). The staves of every kind are taken in page order, slot by
    /// slot: a TAB or a line the page lacks leaves its slot empty, one the score does not have
    /// there is skipped. A system is kept when all its parts' staves are there.
    /// </summary>
    /// <param name="parts">per entry of <paramref name="kinds"/>: the part the staff is read as, or -1 for one set aside
    /// (a "line" that is a part's is read on five lines imagined about its one: <see cref="Staff.FiveLinesAbout"/>,
    /// and it moves from <paramref name="extras"/> to <paramref name="staves"/>)</param>
    public static List<SystemInfo> GroupSystems(List<Staff> staves, List<Staff> extras, List<string> kinds, List<int> parts, List<string> notes, double s)
    {
        var all = staves.Concat(extras).OrderBy(st => st.Top).ToList();
        var systems = new List<SystemInfo>();
        int i = 0;
        while (i < all.Count)
        {
            var sys = new SystemInfo { Index = systems.Count };
            int part = 0;
            for (int ki = 0; ki < kinds.Count; ki++)
            {
                string kind = kinds[ki];
                bool read = parts[ki] >= 0;
                // (the system's staves; a staff of another kind in a part's slot is an extra the
                // score does not have there, skipped)
                while (i < all.Count && kind == "" && all[i].Kind != "")
                {
                    notes.Add($"system {sys.Index + 1}: a {all[i].Kind} staff at y {all[i].Top:F0} where the score has part {part}'s staff, skipped");
                    i++;
                }
                if (i >= all.Count) break;
                var st = all[i];
                if (st.Kind != kind)
                {
                    if (kind != "")
                    {
                        notes.Add($"system {sys.Index + 1}: no {kind} staff after part {part - 1} (a {(st.Kind == "" ? "five-line" : st.Kind)} staff at y {st.Top:F0})");
                        // (the slot left empty; a part's staff missing is a part the system hides)
                        if (st.Kind == "") { if (read) part++; continue; }
                    }
                    i++;                                // a line for a TAB or the other way: skipped
                    continue;
                }
                i++;
                if (read && kind == "line")
                {
                    var five = Staff.FiveLinesAbout(st, s);
                    extras.Remove(st);
                    staves.Add(st = five);
                }
                st.System = sys.Index;
                sys.All.Add(st);
                if (read) { st.Part = part++; sys.Staves.Add(st); }
            }
            if (part < parts.Count(p => p >= 0)) break;
            systems.Add(sys);
        }
        staves.Sort((a, b) => a.Top.CompareTo(b.Top));
        return systems;
    }
}
