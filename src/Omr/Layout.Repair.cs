namespace Omr;

static partial class Layout
{
    /// <summary>
    /// The staves the row test and the comb missed, found from what the learnt detector saw. On a
    /// scan a staff's lines can print so faint and broken that the binarized page barely holds
    /// them (chords006's second system: its fifth line a quarter as dark as its first; basic003's
    /// lower two: the lines a light grey that falls above the threshold), while the detector,
    /// reading the grey page, still marks the barlines crossing that staff, each at the staff's
    /// middle. Two or more barlines at one height clear of every staff found are a staff there:
    /// its five lines are the comb, a space apart, whose rows are darkest in the grey page about
    /// that height, and its extent runs from its clef to its last barline. Adds the staves found
    /// to <paramref name="staves"/>, in page order.
    /// </summary>
    public static void RecoverStaves(Img d, List<Staff> staves, double s, List<Detection> det, List<Staff> extras = null)
    {
        if (det == null || d.Gray == null || s <= 0) return;
        // (a TAB's barlines are no staff's: its lines are found. A one-line staff's stand on its line: a
        // barline a space and more off it is a staff's whose lines faded about the one taken for it -- a
        // phone's photo of a page, a glare across a treble staff leaving its second and fifth lines, each
        // taken for a one-line staff: tabi.jpg's third system)
        var bars = det.Where(t => t.Class == "barline" && !staves.Any(st => Math.Abs(t.Y - st.Mid) < 2.5 * s)
                                  && !(extras ?? new()).Any(st => st.Kind == "line" ? Math.Abs(t.Y - st.Mid) < 0.6 * s
                                                                  : t.Y > st.Top - 2.5 * s && t.Y < st.Bottom + 2.5 * s)).OrderBy(t => t.Y).ToList();
        int w = d.W, h = d.H;
        var groups = new List<List<Detection>>();
        for (int i = 0; i < bars.Count;)
        {
            int j = i;
            while (j + 1 < bars.Count && bars[j + 1].Y - bars[i].Y < 1.0 * s) j++;
            if (j - i + 1 >= 2) groups.Add(bars.GetRange(i, j - i + 1));
            i = j + 1;
        }
        double lowest = Math.Max(staves.Count > 0 ? staves.Max(st => st.Mid) : 0, groups.Count > 0 ? groups.Max(g => g.Average(t => t.Y)) : 0);
        foreach (var group in groups)
        {
            double mid = group.Average(t => t.Y);
            // the staff's extent: from its clef (the leftmost the detector sees at its height, two
            // spaces before the clef's centre) to its last barline; with no clef seen, from the
            // leftmost of the other staves (the systems below the first are not indented). The
            // detector misses a barline now and then, so a staff above another ends where the
            // page's other staves end; only the page's last may end early
            var clef = det.Where(t => t.Class.StartsWith("clef_") && Math.Abs(t.Y - mid) < 2.5 * s).OrderBy(t => t.X).FirstOrDefault();
            int left = clef != null ? (int)(clef.X - 2.0 * s) : staves.Count > 0 ? staves.Min(st => st.Left) : (int)group.Min(t => t.X);
            int right = (int)(group.Max(t => t.X) + 0.1 * s);
            if (mid < lowest - s && staves.Count > 0) right = Math.Max(right, staves.Max(st => st.Right));
            left = Math.Clamp(left, 0, w - 1); right = Math.Clamp(right, left + 1, w - 1);
            // the grey page's darkness per row over that extent
            var dark = new double[h];
            for (int y = 0; y < h; y++)
            {
                double sum = 0;
                int o = y * w;
                for (int x = left; x <= right; x++) sum += 255 - d.Gray[o + x];
                dark[y] = sum;
            }
            double D(int y) => y >= 0 && y < h ? dark[y] : 0;
            double D3(int y) => Math.Max(D(y), Math.Max(D(y - 1), D(y + 1)));
            // the comb: five rows a space apart (the page's spacing, give or take eight percent),
            // the middle one about the barlines' height, where the rows are darkest
            // (the top kept unrounded: rounded, the fifth row drifts by the four spacings'
            // fractions, past the one row's give of the search below)
            double bestScore = -1, bestP = s, bestTop = 0;
            for (double p = 0.92 * s; p <= 1.08 * s + 1e-9; p += Math.Max(0.25, 0.01 * s))
                for (int dy = -(int)(0.75 * s); dy <= (int)(0.75 * s); dy++)
                {
                    double y0 = mid - 2 * p + dy, score = 0;
                    for (int k = 0; k < 5; k++) score += D3((int)Math.Round(y0 + k * p));
                    if (score > bestScore) { bestScore = score; bestP = p; bestTop = y0; }
                }
            // (each row then settled on its darkest neighbour: a scanned line is two rows thick,
            // and which of the two the comb's row rounds to varies from line to line -- a finer
            // fit of the five rows with no give, tried instead, put a line on the lesser of its two
            // rows and failed the peak test on chords006)
            var ls = new Line[5];
            var rise = new double[5];
            bool peaks = true;
            for (int k = 0; k < 5 && peaks; k++)
            {
                int yk = (int)Math.Round(bestTop + k * bestP), at = yk;
                for (int dy = -1; dy <= 1; dy++) if (D(yk + dy) > D(at)) at = yk + dy;
                // a line's row is a peak among the rows around it (a third of a space either way)
                for (int dy = -(int)(bestP / 3); dy <= (int)(bestP / 3) && peaks; dy++) if (D(at + dy) > D(at)) peaks = false;
                // the line's thickness: the rows about the peak at least half as dark as it, over
                // the band's floor (the symbols' ink, which darkens every row)
                double floor = double.MaxValue;
                for (int dy = -(int)(bestP / 2); dy <= (int)(bestP / 2); dy++) floor = Math.Min(floor, D(at + dy));
                double half = floor + 0.5 * (D(at) - floor);
                int top = at, bot = at;
                while (top > 0 && D(top - 1) >= half) top--;
                while (bot < h - 1 && D(bot + 1) >= half) bot++;
                ls[k] = new Line { Top = top, Bot = bot };
                rise[k] = D(at) - floor;
            }
            // no one line far darker than the rest: a one-line staff whose line printed faint has
            // its barlines too, and the comb about its one line finds four "peaks" in the bare paper
            // around it (band009 degraded, page 3: the weakest rising a hundredth of the line's
            // height). A faint five-line staff's weakest rises half as high as its strongest
            // (basic003); one whose lines the toner left none of, its "lines" the symbols' ink, an
            // eighth (band010's page 3, still a staff there worth recovering)
            bool lines = peaks && rise.Min() >= rise.Max() / 20;
            // (and five lines, each apart from the one above and thinner than half a space, as
            // FindStaves' comb asks: the row of a lead sheet's chord diagrams, two of their strings
            // seen as barlines, gave a "line" that spread over the band's sparse ink, up past the one above)
            bool thin = peaks && !Enumerable.Range(0, 5).Any(k => ls[k].Bot - ls[k].Top + 1 > bestP / 2 || k > 0 && ls[k].Top <= ls[k - 1].Bot);
            // (and not the row of a lead sheet's chord diagrams: their strings seen as barlines by the
            // detector, their frets a space apart like the staves' lines, five "lines" of four spaces
            // at each diagram and bare paper between (leadsheet-collide's page 1, with detector32).
            // The middle line's ink lies in lattices -- pieces of ink with three and more rules
            // across and down, a diagram's nut, frets and strings, Tempo.IsLattice -- for half of its
            // inked columns and more; a staff's line, even one a black-and-white scan broke into dots
            // (the printed basic003's lower two), lies in no lattice. The stretches the lines run on
            // for, tried first, shut out those dotted lines: 3.6 to 14 spaces against the diagrams' 5)
            double inLattice = 0;
            if (peaks)
            {
                int by0 = Math.Max(0, ls[0].Top - (int)(0.5 * s)), by1 = Math.Min(h - 1, ls[4].Bot + (int)(0.5 * s));
                int bw = right - left + 1, bh = by1 - by0 + 1;
                var (labels, comps) = Components.Label(bw, bh, i => d[left + i % bw, by0 + i / bw], true);
                var grids = comps.Skip(1).Where(c => Tempo.IsLattice(labels, bw, c, s)).Select(c => c.Id).ToHashSet();
                int inked = 0, gridded = 0;
                for (int x = 0; x < bw; x++)
                    for (int yy = Math.Max(0, ls[2].Top - by0); yy <= Math.Min(bh - 1, ls[2].Bot - by0); yy++)
                    {
                        int l = labels[yy * bw + x];
                        if (l == 0) continue;
                        inked++;
                        if (grids.Contains(l)) gridded++;
                        break;
                    }
                inLattice = inked > 0 ? gridded / (double)inked : 0;
            }
            bool inked5 = peaks && inLattice < 0.5;
            Console.WriteLine($"  staff at y {mid:F0} (x {left}-{right}) from {group.Count} detected barlines: " +
                (!peaks ? $"no five lines a space apart there (comb top {bestTop:F0} spacing {bestP:F1} of {s:F1}: {string.Join(" ", Enumerable.Range(0, 5).Select(k => D((int)Math.Round(bestTop + k * bestP)).ToString("F0")))})"
                 : !lines ? $"one line far darker than the rest, no five-line staff (rising {string.Join(" ", rise.Select(r => r.ToString("F0")))})"
                 : !thin ? $"a line thicker than half a space or into the one above, no five-line staff (rows {string.Join(" ", ls.Select(l => $"{l.Top}-{l.Bot}"))})"
                 : !inked5 ? $"the middle line's ink lies in lattices (chord diagrams) for {inLattice:F2} of it, no staff"
                 : $"recovered, lines at {string.Join(" ", ls.Select(l => l.Y.ToString("F0")))}{(inLattice > 0 ? $" ({inLattice:F2} of the middle line's ink in lattices)" : "")}"));
            if (!lines || !thin || !inked5) continue;
            staves.Add(new Staff { Lines = ls, Left = left, Right = right });
            // (the one-line staves taken from its lines that were left are no staves of their own)
            extras?.RemoveAll(st => st.Kind == "line" && st.Mid > ls[0].Y - 0.5 * s && st.Mid < ls[4].Y + 0.5 * s);
        }
        staves.Sort((a, b) => a.Top.CompareTo(b.Top));
    }

    /// <summary>
    /// The one-line staves the row test missed (a scan's percussion line, broken by the toner into
    /// stretches too short for it), found from the learnt detector's barlines as <see cref="RecoverStaves"/>
    /// finds the five-line staves: two or more barlines at one height clear of every staff and TAB found,
    /// and about that height one row, and one only, dark across most of the staves' width -- a line,
    /// thinner than two fifths of a space, with no other such row within two spaces and a half (a
    /// five-line staff whose lines the toner left little of has several rows alike, and is not this).
    /// Adds the lines found to <paramref name="extras"/>. For omr read's guess of the staves.
    /// </summary>
    public static void RecoverLines(Img d, List<Staff> staves, List<Staff> extras, double s, List<Detection> det)
    {
        if (det == null || s <= 0 || staves.Count == 0) return;
        bool Near(Staff st, double y) => st.Kind == "" && !st.OneLine ? Math.Abs(y - st.Mid) < 2.5 * s : y > st.Top - 2.5 * s && y < st.Bottom + 2.5 * s;
        bool Clear(Detection t) => t.Score > Detector.Threshold && !staves.Any(st => Near(st, t.Y)) && !extras.Any(st => Near(st, t.Y));
        var bars = det.Where(t => t.Class == "barline" && Clear(t)).OrderBy(t => t.Y).ToList();
        // (the detector sees few barlines on a percussion line -- none, or one; it sees the notes, and a
        // percussion line's notes all sit on the line: five or more heads at one height, within half a
        // space, clear of every staff -- the degraded band pages' lost lines left 10 to 20 heads so, where
        // a lost five-line staff's heads stand at every height)
        var heads = det.Where(t => t.Class is "head_black" or "head_x" or "head_half" && Clear(t)).OrderBy(t => t.Y).ToList();
        var groups = new List<(List<Detection> At, bool ByHeads)>();
        for (int i = 0; i < bars.Count;)
        {
            int j = i;
            while (j + 1 < bars.Count && bars[j + 1].Y - bars[i].Y < 1.0 * s) j++;
            if (j - i + 1 >= 2) groups.Add((bars.GetRange(i, j - i + 1), false));
            i = j + 1;
        }
        for (int i = 0; i < heads.Count;)
        {
            int j = i;
            while (j + 1 < heads.Count && heads[j + 1].Y - heads[i].Y < 0.5 * s) j++;
            if (j - i + 1 >= 5)
            {
                var at = heads.GetRange(i, j - i + 1);
                double y = at.Average(t => t.Y);
                if (!groups.Any(g => Math.Abs(g.At.Average(t => t.Y) - y) < 1.5 * s)) groups.Add((at, true));
            }
            i = j + 1;
        }
        // (the line runs the width of the staves: the page's usual start and end)
        var lefts = staves.Select(st => st.Left).OrderBy(v => v).ToList();
        var rights = staves.Select(st => st.Right).OrderBy(v => v).ToList();
        int left = Math.Clamp(lefts[lefts.Count / 2], 0, d.W - 1), right = Math.Clamp(rights[rights.Count / 2], left + 1, d.W - 1);
        int width = right - left + 1;
        // (how much of the width the row's ink covers in stretches of a quarter space and longer, the row
        // and its two neighbours together: a broken line is stretches, a speckled page's rows are specks --
        // which, counted, made every row of Leland band009 as dark as its line)
        int least_run = Math.Max(3, (int)(0.25 * s));
        double Cover(int y)
        {
            if (y < 1 || y >= d.H - 1) return 0;
            int n = 0, run = 0;
            for (int x = left; x <= right + 1; x++)
            {
                if (x <= right && (d[x, y] || d[x, y - 1] || d[x, y + 1])) run++;
                else { if (run >= least_run) n += run; run = 0; }
            }
            return (double)n / width;
        }
        foreach (var (group, byHeads) in groups.OrderBy(g => g.At.Average(t => t.Y)))
        {
            double mid = group.Average(t => t.Y);
            // (MuseScore hangs a percussion line's heads under it, two spaces and a half -- Petaluma band007,
            // the barlines' middle 58 rows over the heads at 23.5 to the space: the line is looked for from
            // three spaces over the heads to one under them; and a line the heads vouch for may be broken
            // to a third of the width -- 0.42 there)
            double over = byHeads ? 3 * s : 1.5 * s, under = byHeads ? 1 * s : 1.5 * s, least = byHeads ? 0.33 : 0.5;
            int y0 = (int)(mid - over), y1 = (int)(mid + under), best = -1;
            var cover = new Dictionary<int, double>();
            for (int y = Math.Max(1, (int)(mid - over - 2.5 * s)); y <= Math.Min(d.H - 2, (int)(mid + under + 2.5 * s)); y++) cover[y] = Cover(y);
            for (int y = Math.Max(1, y0); y <= Math.Min(d.H - 2, y1); y++) if (best < 0 || cover[y] > cover[best]) best = y;
            if (best < 0) continue;
            double c = cover[best];
            // (the line's rows: those about the peak half as covered; its thickness under two fifths of a space)
            int top = best, bot = best;
            while (cover.TryGetValue(top - 1, out var ct) && ct >= 0.5 * c) top--;
            while (cover.TryGetValue(bot + 1, out var cb) && cb >= 0.5 * c) bot++;
            bool alone = !cover.Any(kv => kv.Key >= best - 2.5 * s && kv.Key <= best + 2.5 * s && (kv.Key < top - 0.5 * s || kv.Key > bot + 0.5 * s) && kv.Value >= 0.5 * c);
            bool line = c >= least && bot - top + 1 <= 0.42 * s && alone;
            Console.WriteLine($"  line at y {mid:F0} (x {left}-{right}) from {group.Count} detected {(byHeads ? "heads at one height" : "barlines")}: " +
                (line ? $"recovered, the row {best} covering {c:F2}" : c < least ? $"no row covers {least:F2} of the width (the most {c:F2} at {best})" : !alone ? $"other rows as dark about it, no one-line staff" : $"the row {top}-{bot} too thick for a line"));
            if (!line) continue;
            extras.Add(new Staff { Lines = new[] { new Line { Top = top, Bot = bot } }, Kind = "line", Left = left, Right = right });
        }
        extras.Sort((a, b) => a.Top.CompareTo(b.Top));
    }

    /// <summary>
    /// A staff starts before its clef. Where the row test found a staff's left edge at or beyond
    /// the clef the learnt detector sees at its height (on a scan the lines under the clef and
    /// key print too faint, and the edge lands on the first chord: chords003's second system, its
    /// clef and key then "left of the staff" and the chord "the clef's ink"), the edge moves to two
    /// spaces before the clef, as a recovered staff's does.
    /// </summary>
    public static void StartAtClefs(List<Staff> staves, double s, List<Detection> det)
    {
        if (det == null) return;
        var lefts = staves.Select(o => o.Left).ToList();
        foreach (var st in staves)
        {
            var clefs = det.Where(t => t.Class.StartsWith("clef_") && t.Score >= Symbols.TrustScore && Math.Abs(t.Y - st.Mid) < 2.5 * s && t.X < st.Right).ToList();
            // a degraded page's noise is seen as a clef at 0.52-0.53: a "clef" left of every other
            // staff's edge on the page, in the margin, is taken only when sure (the noise about
            // the part's names, Gonville band007's first page and Bravura band003's last drum
            // staff; voices2007's tenor clef of a staff below an indented one, 0.99)
            int others = staves.Count > 1 ? Enumerable.Range(0, staves.Count).Where(i => staves[i] != st).Min(i => lefts[i]) : 0;
            var clef = clefs.Where(t => t.X < st.Left + 1.0 * s && (t.X >= others - 1.0 * s || t.Score >= Symbols.SureScore))
                            .OrderBy(t => t.X).FirstOrDefault();
            if (clef == null) continue;
            int left = Math.Max(0, (int)(clef.X - 2.0 * s));
            Console.WriteLine($"  staff at y {st.Mid:F0}: its left edge {st.Left} at or past the detected clef at x {clef.X:F0}, moved to {left}");
            st.Left = left;
        }
        // A clef the detector does not know (an old print's bass clef, kinkel19-5's first page): the key's
        // or the time's signs it sees at or past the edge, and the staff next above or below it -- its
        // system's -- starting well before them, the edge moves to that staff's
        foreach (var st in staves)
        {
            var sign = det.Where(t => t.Class is "flat" or "sharp" or "timesig_c" || t.Class.StartsWith("digit_"))
                          .Where(t => t.Score >= Symbols.TrustScore && Math.Abs(t.Y - st.Mid) < 2.5 * s && t.X < st.Left && t.X > st.Left - 8 * s)
                          .OrderBy(t => t.X).FirstOrDefault();
            if (sign == null) continue;
            var next = staves.Where(o => o != st && o.Kind == st.Kind && Math.Abs(o.Mid - st.Mid) < 14 * s && o.Left < sign.X - 2.5 * s && o.Left > sign.X - 6 * s)
                             .OrderBy(o => Math.Abs(o.Mid - st.Mid)).FirstOrDefault();
            if (next == null || staves.Any(o => o != st && o != next && (o.Mid - st.Mid) * (o.Mid - next.Mid) < 0)) continue;
            Console.WriteLine($"  staff at y {st.Mid:F0}: its left edge {st.Left} at or past the detected {sign.Class} at x {sign.X:F0}, moved to the next staff's {next.Left}");
            st.Left = next.Left;
        }
    }

    /// <summary>
    /// A staff ends at its last barline. On a speckled page the lines of every staff can be bridged
    /// on into the speckle past the system's end, the page's usual end with them (FindStaves can
    /// then tell none of them), and a barline is read there: a measure of speckle. Where the
    /// learnt detector sees a barline on the staff, nothing past it, and the staff's lines have no
    /// more ink past it than the rows between them, the staff ends at that barline (FinaleBroadway
    /// band008's first page, every staff 7 spaces on). A staff whose lines the toner took away
    /// still has its notes past its last barline seen but one, and keeps its end.
    /// </summary>
    public static void EndAtBarlines(Img d, List<Staff> staves, double s, List<Detection> det, List<Staff> extras = null)
    {
        if (det == null) return;
        bool Ink(int x, int y) => d[x, y] || d[x, y - 1] || d[x, y + 1];
        foreach (var st in extras == null ? staves : staves.Concat(extras))
        {
            double sp = st.S > 0 ? st.S : s;
            var bar = det.Where(t => t.Class == "barline" && t.Score > Detector.Threshold && Math.Abs(t.Y - st.Mid) < 1.5 * sp && t.X > st.Left && t.X < st.Right)
                         .OrderByDescending(t => t.X).FirstOrDefault();
            if (bar == null || st.Right - bar.X < 2 * sp) continue;
            int from = (int)(bar.X + 0.5 * sp);
            if (det.Any(t => t.Score > Detector.Threshold && t.X > from && t.X < st.Right && t.Y > st.Top - 2 * sp && t.Y < st.Bottom + 2 * sp)) continue;
            // (the rows between the lines: a one-line staff's, half a space above and below)
            var ys = st.Lines.Select(ln => (int)Math.Round(ln.Y)).ToArray();
            var gaps = ys.Length > 1 ? Enumerable.Range(1, ys.Length - 1).Select(k => (ys[k - 1] + ys[k]) / 2).ToArray()
                                     : new[] { ys[0] - (int)(0.5 * sp), ys[0] + (int)(0.5 * sp) };
            double lead = 0;
            for (int x = from + 1; x <= st.Right; x++)
                lead += (double)ys.Count(y => Ink(x, y)) / ys.Length - (double)gaps.Count(y => Ink(x, y)) / gaps.Length;
            if (lead >= 0.1 * (st.Right - from)) continue;
            int end = from;
            while (end > st.Left && !ys.Any(y => Ink(end, y))) end--;
            Console.WriteLine($"  staff at y {st.Mid:F0}: its end {st.Right} past its last detected barline at x {bar.X:F0}, nothing seen and no lines' ink there, moved to {end}");
            st.Right = end;
        }
    }
}
