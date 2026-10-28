namespace Omr;

sealed class Line
{
    public int Top, Bot;
    public double Y => (Top + Bot) / 2.0;
}

sealed class Staff
{
    public Line[] Lines;
    public int Left, Right;
    public int System, Part;
    /// <summary>"" for a five-line staff that is read; "tab" (a TAB's four to six lines) or "line"
    /// (a one-line percussion staff) for one that is found and set aside (B2).</summary>
    public string Kind = "";
    /// <summary>A one-line staff read as a part (a lead sheet's rhythm staff, "line gtr" in the score
    /// file): its five lines are imagined about the one printed, a page's space apart, so that the
    /// positions, the signatures and the barlines read as on any staff (<see cref="FiveLinesAbout"/>).</summary>
    public bool OneLine;
    public double S => Lines.Length > 1 ? (Lines[^1].Y - Lines[0].Y) / (Lines.Length - 1) : 0;
    public double Top => Lines[0].Y;
    public double Bottom => Lines[^1].Y;
    public double Mid => Lines.Length == 5 ? Lines[2].Y : (Top + Bottom) / 2;
    /// <summary>Staff position in half-spaces from the middle line, + up.</summary>
    public double Pos(double y) => (Mid - y) / (S / 2);

    /// <summary>
    /// The one-line staff <paramref name="line"/> as a five-line staff of the page's spacing
    /// <paramref name="s"/>, its printed line the middle one (the other four as thin as it,
    /// imagined where no ink is: the ink was already cleared of the one real line).
    /// </summary>
    public static Staff FiveLinesAbout(Staff line, double s)
    {
        var real = line.Lines[0];
        int thick = real.Bot - real.Top;
        var lines = new Line[5];
        for (int k = 0; k < 5; k++)
        {
            int top = (int)Math.Round(real.Top + (k - 2) * s);
            lines[k] = k == 2 ? real : new Line { Top = top, Bot = top + thick };
        }
        return new Staff { Lines = lines, Left = line.Left, Right = line.Right, System = line.System, Part = line.Part, OneLine = true };
    }
}

sealed class SystemInfo
{
    public int Index;
    public List<Staff> Staves = new();
    /// <summary>Every staff of the system top to bottom, the TABs and one-line staves among the parts' (B2).</summary>
    public List<Staff> All = new();
    /// <summary>Measure boundaries: staff start, each barline, staff end.</summary>
    public List<double> Bars = new();
    public int FirstBar;
    public int Measures => Bars.Count - 1;
    /// <summary>Multi-bar rests (MultiRests): the measure that starts at that x stands for so many bars.</summary>
    public Dictionary<double, int> Rests = new();
    /// <summary>How many bars measure m stands for: 1, or a multi-bar rest's count.</summary>
    public int Span(int m) => Rests.TryGetValue(Bars[m], out int n) ? n : 1;
    /// <summary>The bar number of measure m (of its first bar, for a multi-bar rest).</summary>
    public int BarOf(int m)
    {
        int b = FirstBar;
        for (int k = 0; k < m; k++) b += Span(k);
        return b;
    }
    /// <summary>The bars the system holds: its measures, a multi-bar rest's each counted.</summary>
    public int BarCount => BarOf(Measures) - FirstBar;
}

static partial class Layout
{
    /// <summary>The page FindStaves read last had no clear staff: the comb's spacing was guessed.</summary>
    public static bool LastGuessed;
    /// <summary>The staff spacing FindStaves last went by: the clear staves' mean, or the guess from the rows'
    /// agreement (0 with none) -- a page with no staff found still has one for the detector to read it at.</summary>
    public static double LastSpacing;

    /// <summary>
    /// The page's five-line staves. With <paramref name="extras"/> (a score that has TABs or
    /// one-line staves, B2), the staves of other kinds found go there, in page order.
    /// </summary>
    public static List<Staff> FindStaves(Img d, List<Staff> extras = null)
    {
        int w = d.W, h = d.H;
        var rows = new List<int>();
        var count = new int[h];
        var longest = new int[h];
        int most = 0;
        for (int y = 0; y < h; y++)
        {
            // (a scan's black edges, the runs of ink from the page's sides, are no line's: whole
            // black rows at the top and bottom of kinkel19-2's scan set the darkest row to the
            // page's width, and the lines of its piano staves, broken, fell under a third of it)
            var (from, to) = d.EdgeFree(y);
            int c = 0, run = 0, best = 0, o = y * w;
            for (int x = from; x < to; x++)
            {
                if (d.D[o + x]) { c++; run++; best = Math.Max(best, run); }
                else run = 0;
            }
            count[y] = c; longest[y] = best;
            most = Math.Max(most, c);
        }
        // a staff line's row is dark across the staff's width, in one stretch: the darkest rows
        // are staff lines, and a line is at least a third as dark as those (a scan's staves fill
        // only part of the page). A row of ledger lines above a staff, a space apart from its top
        // line, can be as dark, but in many short stretches; a broken scanned line is short
        // stretches too, and such a staff is found by the comb below
        // (a clear staff's lines are dark across most of the page, in one stretch; anything
        // fainter or more broken is left to the comb below, which the clear staves' spacing guides)
        int bar = Math.Max((int)(w * 0.12), most / 3);
        for (int y = 0; y < h; y++) if (count[y] > bar) rows.Add(y);
        // the longest stretch of columns dark in any of the rows y0..y1, over gaps of a 250th of
        // the page at most (a scan's noise nicks a line every few hundred pixels, and the cleaning
        // of lone pixels widens the nicks to 9 -- MuseJazz band008, 2283 wide; ledger lines and the
        // stretches of a broken line stand a space or more apart). The stretch is mostly ink, four
        // columns in five: a black-and-white scan's dotted line, a pixel every 5 or 6, is not a
        // clear line but the comb's (FinaleMaestro band007)
        int maxGap = Math.Max(2, w / 250);
        int Stretch(int y0, int y1)
        {
            int run = 0, ink = 0, gap = 0, best = 0;
            for (int x = 0; x < w; x++)
            {
                bool dark = false;
                for (int y = y0; y <= y1 && !dark; y++) dark = d.D[y * w + x];
                if (dark)
                {
                    run = run > 0 ? run + gap + 1 : 1; ink++; gap = 0;
                    if (ink >= 0.8 * run) best = Math.Max(best, run);
                }
                else if (++gap > maxGap) run = ink = gap = 0;
            }
            return best;
        }
        var lines = new List<Line>();
        var linePeaks = new List<(int Peak, int Max, bool Nicked)>();
        for (int i = 0; i < rows.Count;)
        {
            int j = i;
            while (j + 1 < rows.Count && rows[j + 1] - rows[j] <= 1) j++;
            // a beam lying along a line joins the run: keep only the rows as dark as the line itself
            int max = 0, peak = rows[i];
            for (int k = i; k <= j; k++) if (count[rows[k]] > max) { max = count[rows[k]]; peak = rows[k]; }
            // (the run's darkest row must be one long stretch: a row of ledger lines is not, and
            // neither is a broken scanned line, which the comb below finds. But a black-and-white
            // scan's whole line, nicked by noise and stepping between two rows where it tilts, is
            // only short stretches a row -- Emmentaler band005 259-800 pixels, MuseJazz band008
            // 277-890, to a quarter page of 570-744: so the stretch is also measured over the
            // darkest row and the run's rows beside it, across small gaps)
            bool nicked = longest[peak] < 0.25 * w;
            if (nicked && Stretch(Math.Max(rows[i], peak - 1), Math.Min(rows[j], peak + 1)) < 0.25 * w)
            {
                if (TraceJoins) Console.WriteLine($"  row {peak}: {max} dark, longest stretch {longest[peak]} of {w}: no clear line");
                i = j + 1; continue;
            }
            int top = -1, bot = -1;
            for (int k = i; k <= j; k++)
                if (count[rows[k]] >= 0.9 * max) { if (top < 0) top = rows[k]; bot = rows[k]; }
            lines.Add(new Line { Top = top, Bot = bot });
            linePeaks.Add((peak, max, nicked));
            i = j + 1;
        }
        // (a staff with a nicked line is a scan's, its lines as thick as the rows half as dark as
        // their darkest, as the comb measures them: a scanned line's ink spreads over rows paler
        // than its peak, and the staff-line removal leaves those as specks read for notes -- the
        // colour scans voices2006 and ties004, whose staves the comb found before)
        Line[] StaffLines(int i, int n)
        {
            var ls = lines.GetRange(i, n).ToArray();
            if (!linePeaks.Skip(i).Take(n).Any(p => p.Nicked)) return ls;
            for (int k = 0; k < n; k++)
            {
                var (peak, max, _) = linePeaks[i + k];
                int top = Math.Min(ls[k].Top, peak), bot = Math.Max(ls[k].Bot, peak);
                while (top > 0 && peak - top < 4 && count[top - 1] >= 0.5 * max) top--;
                while (bot < h - 1 && bot - peak < 4 && count[bot + 1] >= 0.5 * max) bot++;
                ls[k] = new Line { Top = top, Bot = bot };
            }
            return ls;
        }
        var staves = new List<Staff>();
        // the run of lines equally spaced from line i: how many, and their spacing (the first gap
        // picks the run; the spacing is the run's mean, the first gap being a row or so off)
        (int N, double G) Run(int i)
        {
            int j = i;
            double g0 = i + 1 < lines.Count ? lines[i + 1].Y - lines[i].Y : 0;
            if (g0 > 8)
                while (j + 1 < lines.Count && Math.Abs(lines[j + 1].Y - lines[j].Y - g0) <= g0 * 0.15) j++;
            return (j - i + 1, j > i ? (lines[j].Y - lines[i].Y) / (j - i) : g0);
        }
        // a TAB (only looked for when the score has them: without, the staves are found as before):
        // four to six lines further apart than the staves' (a string's width: Fantasy's TABs 17
        // pixels to its staves' 13.5). The count alone does not tell: a staff whose one line the
        // row test misses is a run of four, left to the comb below. Six lines evenly apart are a
        // TAB's alone, and need less: 揺れる想い's TABs are 13.8 to its staves' 11.5, a first gap of
        // 13.5 fell under 1.15 times them, and the TAB was read as a staff and a one-line staff
        double staffGap = 0;
        if (extras != null)
        {
            // (the staves' spacing from the runs of five, where there are any: not the TABs')
            var gaps = new List<(int N, double G)>();
            for (int i = 0; i < lines.Count; i++) { var (n, g) = Run(i); if (n >= 5) { gaps.Add((n, g)); i += n - 1; } }
            var fives = gaps.Where(r => r.N == 5).Select(r => r.G).ToList();
            var all = fives.Count > 0 ? fives : gaps.Select(r => r.G).ToList();
            if (all.Count > 0) staffGap = all.OrderBy(v => v).ElementAt(all.Count / 2);
        }
        for (int i = 0; i < lines.Count;)
        {
            var (n, g0) = Run(i);
            if (staffGap > 0 && n >= 4 && n <= 6 && g0 > (n == 6 ? 1.08 : 1.15) * staffGap)
            {
                extras.Add(new Staff { Lines = StaffLines(i, n), Kind = "tab" });
                i += n;
                continue;
            }
            if (n >= 5)
            {
                // (more than five lines evenly apart are a staff and rows of ledger lines a space
                // above or below it, which pass the row test where the notes stand close: their
                // ledger lines, a few pixels apart, measure as one stretch across the gaps --
                // quartet-collide's Violin I, a first bar all high notes, took the two rows above
                // its staff for its top lines. The staff is the five whose faintest is darkest: its
                // lines cross the whole system, the ledger rows only where the notes are)
                int at = i;
                if (n > 5 && n < 10)
                {
                    double Faintest(int o) => Enumerable.Range(o, 5).Min(k => linePeaks[k].Max);
                    for (int o = i + 1; o + 5 <= i + n; o++) if (Faintest(o) > Faintest(at)) at = o;
                }
                staves.Add(new Staff { Lines = StaffLines(at, 5) });
                i = n > 5 && n < 10 ? i + n : at + 5;
            }
            else i++;
        }
        // (five lines evenly apart that are no staff's: the extender lines of five verses of words
        // under a voice's staff, run the width of a bar, each as dark as a line's row and 2.4 spaces
        // apart -- kinkel19-6 drawn by MuseScore, its third page, where the staff so found threw the
        // page's systems off by one staff and every part into the next. A staff's spacing is the
        // page's: a run spaced over one and a half times the others' is no staff. Not the other way
        // round: a cue staff, an ossia, is the smaller one)
        if (staves.Count >= 2)
        {
            double usualS = staves.Select(st => st.S).OrderBy(v => v).ElementAt((staves.Count - 1) / 2);
            foreach (var st in staves.Where(st => st.S > 1.6 * usualS).ToList())
            {
                if (TraceJoins) Console.WriteLine($"  five lines at y {st.Top:F0}-{st.Bottom:F0} spaced {st.S:F1} to the page's {usualS:F1}: no staff");
                staves.Remove(st);
            }
        }
        // a staff whose lines are too broken for the row test is still five rows a space apart:
        // with the spacing the clear staves show, a comb of five rows is scored by its ink, and
        // one that scores a fair share of the best comb's, clear of the staves found, is a staff
        // (with no clear staff at all, the spacing is where the rows' ink repeats: the smallest
        // shift at which the row counts agree with themselves, five lines a space apart)
        double spacing = staves.Count > 0 ? staves.Average(st => st.S) : 0;
        bool guessed = staves.Count == 0;
        LastGuessed = guessed;
        var combed = new HashSet<Staff>();     // (the staves the comb finds: checked for their width below)
        var pixels = guessed ? (int[])count.Clone() : count;
        if (guessed)
        {
            // (no clear staff: the lines may be dots, a black-and-white scan's faint lines, each row
            // with fewer pixels than a beam's or a row of chords' but spread over the staff's whole
            // width. A row's darkness is then how far its ink spreads: the cells of 16 columns it
            // touches, as a solid line of that width would count -- Bravura band003, the dotted
            // lines 94-106 cells of 148, the beam a space under the staff 40)
            CellCounts(d, count);
            spacing = SpacingByAgreement(count);
        }
        LastSpacing = spacing;
        if (spacing > 0)
        {
            // (a spacing from the agreement is a guess to a row or so: each comb tries the spacings
            // eight percent either side, and keeps its best)
            int span = (int)Math.Round(4 * spacing);
            var score = new double[h];
            var combP = new double[h];
            for (int y = 0; y < h - span - 1; y++)
            {
                combP[y] = spacing;
                for (double q = guessed ? 0.92 * spacing : spacing; q <= (guessed ? 1.08 * spacing : spacing) + 1e-9; q += 0.25)
                {
                    if (y + 4 * q >= h - 2) break;
                    double sum = 0;
                    for (int k = 0; k < 5; k++)
                    {
                        int yk = (int)Math.Round(y + k * q);
                        sum += Math.Max(count[yk], Math.Max(count[Math.Max(0, yk - 1)], count[Math.Min(h - 1, yk + 1)]));
                    }
                    if (sum > score[y]) { score[y] = sum; combP[y] = q; }
                }
            }
            double best = score.Max();
            for (int y = 0; y < h - span - 1; y++)
            {
                if (score[y] < 0.15 * best) continue;
                bool peak = true;
                for (int k = Math.Max(0, y - span); k <= Math.Min(h - span - 2, y + span) && peak; k++) if (score[k] > score[y]) peak = false;
                if (!peak) continue;
                double p = combP[y];
                if (staves.Concat(extras ?? Enumerable.Empty<Staff>()).Any(st => y < st.Bottom + 2 * p && y + span > st.Top - 2 * p)) continue;
                var ls = new Line[5];
                int lo = int.MaxValue, hi = 0;
                bool peaks = true;
                for (int k = 0; k < 5; k++)
                {
                    // (settled on the darkest row within two: a tilted scan's gaps run 12, 14.5, 13.5,
                    // 12, and the comb's fifth row fell two rows off its line -- Fantasy p06)
                    int yk = (int)Math.Round(y + k * p), at = yk;
                    for (int dy = -2; dy <= 2; dy++) if (count[Math.Clamp(yk + dy, 0, h - 1)] > count[at]) at = Math.Clamp(yk + dy, 0, h - 1);
                    // (the line's thickness: the rows about the peak at least half as dark as it, over
                    // the floor of the rows a half space either way -- the notes' ink, which darkens
                    // every row of a staff full of chords and, when its lines are dots, rivals them;
                    // the higher of the two sides', an outer line's other side being bare paper)
                    int above = int.MaxValue, below = int.MaxValue;
                    for (int dy = 1; dy <= (int)(p / 2); dy++)
                    {
                        above = Math.Min(above, count[Math.Clamp(at - dy, 0, h - 1)]);
                        below = Math.Min(below, count[Math.Clamp(at + dy, 0, h - 1)]);
                    }
                    // (over the floor on a page with clear staves too, where the rows about the
                    // line hold a third of its ink and more: an old print's broken lines, left to
                    // the comb, stand over rows of chords, beams and words that hold half as much
                    // ink as the lines, and measured from zero every line was thicker than half a
                    // space -- kinkel19-2's scan, the piano staves of its first system. Not over
                    // a lower floor: a clean page's comb staff then came a row thinner, its middle
                    // row beside the line, and the staff's start was lost -- piano010 at scale 3)
                    int floor = guessed || Math.Max(above, below) >= 0.3 * count[at] ? Math.Max(above, below) : 0;
                    double half = floor + 0.5 * (count[at] - floor);
                    // (and a dotted line stands well clear of that floor: the rows of a TAB's digits
                    // between its lines are peaks too, 1.2 times their floor where a line is 2 to 4)
                    if (guessed && count[at] < 1.6 * floor) peaks = false;
                    int top = at, bot = at;
                    while (top > 0 && count[top - 1] >= half) top--;
                    while (bot < h - 1 && count[bot + 1] >= half) bot++;
                    // (where the line is ink enough to be found by its pixels -- a toner-broken line,
                    // not dots -- it is placed by them, the darkest row and those half as dark: where
                    // the staff-line removal cuts, as before the spread was looked at; but not where
                    // that runs into a beam along the line, thicker than a quarter space -- the scan of
                    // basic003, a bottom line 12 rows thick at a space of 27)
                    if (guessed)
                    {
                        int px = Math.Clamp(yk, 0, h - 1);
                        for (int dy = -2; dy <= 2; dy++) if (pixels[Math.Clamp(yk + dy, 0, h - 1)] > pixels[px]) px = Math.Clamp(yk + dy, 0, h - 1);
                        int pt = px, pb = px;
                        while (pt > 0 && pixels[pt - 1] >= 0.5 * pixels[px]) pt--;
                        while (pb < h - 1 && pixels[pb + 1] >= 0.5 * pixels[px]) pb++;
                        if (pb - pt + 1 <= p / 4 && Math.Abs(px - at) <= 2) { top = pt; bot = pb; }
                    }
                    ls[k] = new Line { Top = top, Bot = bot };
                    lo = Math.Min(lo, count[at]); hi = Math.Max(hi, count[at]);
                    // a line's row is a peak among the rows around it (a third of a space either way)
                    for (int dy = -(int)(p / 3); dy <= (int)(p / 3) && peaks; dy++)
                        if (count[Math.Clamp(at + dy, 0, h - 1)] > count[at]) peaks = false;
                }
                // five lines are alike: the faintest at least a third as dark as the darkest (the
                // rows of a title, or of beams and ledger lines above a staff, are nothing alike)
                if (!peaks || lo < 0.35 * hi)
                {
                    if (TraceJoins) Console.WriteLine($"  comb at y {y} (spacing {p:F1}, score {score[y]:F0} of {best:F0}): {(!peaks ? "a row is no peak" : $"faintest line {lo} of {hi}")}, no staff");
                    continue;
                }
                // five lines, each apart from the one above and thinner than half a space (on a page
                // with no clear staff, a comb in a grey smudge took one dark band for two lines, 40
                // rows thick: a staff 5 pixels a space, and the detector read the page at four times its size)
                if (Enumerable.Range(0, 5).Any(k => ls[k].Bot - ls[k].Top + 1 > p / 2 || k > 0 && ls[k].Top <= ls[k - 1].Bot))
                {
                    if (TraceJoins) Console.WriteLine($"  comb at y {y} (spacing {p:F1}): a line thicker than half a space or into the one above (rows {string.Join(" ", ls.Select(l => $"{l.Top}-{l.Bot}"))}), no staff");
                    continue;
                }
                if (TraceJoins) Console.WriteLine($"  comb at y {y} (spacing {p:F1}): a staff, lines at {string.Join(" ", ls.Select(l => l.Y.ToString("F0")))}");
                var comb = new Staff { Lines = ls };
                staves.Add(comb);
                combed.Add(comb);
            }
            staves.Sort((a, b) => a.Top.CompareTo(b.Top));
        }
        // (a page whose staves only the comb found, by a guessed spacing, may have TABs the row
        // test found none of: they are looked for at any spacing a TAB has, 1.15 to 1.8 times the
        // staves' -- Bravura band003's 30.4 to 19.7)
        bool tabsGuessed = extras != null && extras.Count == 0 && staves.Count > 0 && guessed;
        if (extras != null && (extras.Count > 0 || tabsGuessed) && staves.Count > 0)
        {
            // the TABs the row test missed (a TAB's digits stand on its lines and break them): combs
            // of six rows, or four, at the spacing of the TABs found, clear of every staff, each row
            // a peak and as dark as a third of the staves' lines
            double tg = extras.Count > 0 ? extras.Select(t => t.S).OrderBy(v => v).ElementAt(extras.Count / 2) : 0;
            double tgLo = tabsGuessed ? 1.15 * spacing : 0.9 * tg, tgHi = tabsGuessed ? 1.8 * spacing : 1.1 * tg;
            double lineDark = staves.SelectMany(st => st.Lines).Select(l => (double)count[(int)Math.Round(l.Y)]).OrderBy(v => v).ElementAt(staves.Count * 5 / 2);
            // (a row settles on the darkest within two: a scanned TAB's gaps run 15, 16, 18 -- p08)
            int At(int yk)
            {
                int at = Math.Clamp(yk, 0, h - 1);
                for (int dy = -2; dy <= 2; dy++) if (count[Math.Clamp(yk + dy, 0, h - 1)] > count[at]) at = Math.Clamp(yk + dy, 0, h - 1);
                return at;
            }
            // (a bass TAB's lines are a little closer than a guitar TAB's: Fantasy's 16.2 to 17)
            Staff Comb(int y, int n)
            {
                for (double p = tgLo; p <= tgHi + 1e-9; p += 0.25)
                    if (CombAt(y, n, p) is Staff t) return t;
                return null;
            }
            Staff CombAt(int y, int n, double tg)
            {
                if (y + (n - 1) * tg >= h - 2) return null;
                if (staves.Concat(extras).Any(st => y < st.Bottom + 1.0 * tg && y + (n - 1) * tg > st.Top - 1.0 * tg)) return null;
                var ls = new Line[n];
                for (int k = 0; k < n; k++)
                {
                    int at = At((int)Math.Round(y + k * tg));
                    if (count[at] < 0.33 * lineDark) return null;
                    for (int dy = -(int)(tg / 3); dy <= (int)(tg / 3); dy++)
                        if (count[Math.Clamp(at + dy, 0, h - 1)] > count[at]) return null;
                    int top = at, bot = at;
                    while (top > 0 && count[top - 1] >= 0.5 * count[at]) top--;
                    while (bot < h - 1 && count[bot + 1] >= 0.5 * count[at]) bot++;
                    ls[k] = new Line { Top = top, Bot = bot };
                }
                // (its lines are alike: the beams under a bass TAB are a third as dark as its lines)
                if (ls.Min(l => count[(int)Math.Round(l.Y)]) < 0.5 * ls.Max(l => count[(int)Math.Round(l.Y)])) return null;
                // (the rows just outside are no line of it: a four-row comb inside a six-line TAB)
                foreach (double yo in new[] { y - tg, y + n * tg })
                {
                    // (a line of it as dark as its own; the beams above a bass TAB are half as dark: p08)
                    int o = At((int)Math.Round(yo));
                    if (o > 0 && o < h - 1 && count[o] >= 0.33 * lineDark && count[o] >= 0.7 * ls.Min(l => count[(int)Math.Round(l.Y)])) return null;
                }
                return new Staff { Lines = ls, Kind = "tab" };
            }
            for (int y = 0; y < h; y++)
                foreach (int n in new[] { 6, 4 })
                {
                    var tab = Comb(y, n);
                    if (tab == null) continue;
                    // (the best-placed comb about here: the rows of the lines, not a row beside them)
                    Console.WriteLine($"  TAB of {n} lines at y {tab.Top:F0} from the comb (spacing {tab.S:F1})");
                    extras.Add(tab);
                    y = (int)tab.Bottom;
                    break;
                }
            // a TAB of four lines may be a six-line one whose two outer lines the frets broke (a first
            // system's TAB is short, its lines' stretches shorter still): the rows a spacing and two
            // beyond it, peaks as dark as its own lines (the comb's test for a line of it; the beams
            // over a bass TAB are half as dark), are its lines
            foreach (var tab in extras.Where(t => t.Kind == "tab" && t.Lines.Length == 4))
            {
                double g = tab.S, own = tab.Lines.Min(l => count[(int)Math.Round(l.Y)]);
                bool IsLine(int o)
                {
                    if (o <= 0 || o >= h - 1 || count[o] < 0.33 * lineDark || count[o] < 0.7 * own) return false;
                    for (int dy = -(int)(g / 3); dy <= (int)(g / 3); dy++)
                        if (count[Math.Clamp(o + dy, 0, h - 1)] > count[o]) return false;
                    return !staves.Concat(extras).Any(st => st != tab && o > st.Top - 0.5 * g && o < st.Bottom + 0.5 * g);
                }
                foreach (int dir in new[] { -1, 1 })
                {
                    int o1 = At((int)Math.Round((dir < 0 ? tab.Top : tab.Bottom) + dir * g)), o2 = At((int)Math.Round((dir < 0 ? tab.Top : tab.Bottom) + 2 * dir * g));
                    if (!IsLine(o1) || !IsLine(o2)) continue;
                    var add = new[] { o1, o2 }.Select(at =>
                    {
                        int top = at, bot = at;
                        while (top > 0 && count[top - 1] >= 0.5 * count[at]) top--;
                        while (bot < h - 1 && count[bot + 1] >= 0.5 * count[at]) bot++;
                        return new Line { Top = top, Bot = bot };
                    });
                    tab.Lines = tab.Lines.Concat(add).OrderBy(l => l.Y).ToArray();
                    Console.WriteLine($"  TAB of 4 lines at y {tab.Top:F0} takes two more {(dir < 0 ? "above" : "below")}");
                    break;
                }
            }
            // a TAB of five lines is a six-line one with a line missed (digits on it): the sixth is
            // a row a spacing above or below, a peak as dark as a third of the staves' lines
            foreach (var tab in extras.Where(t => t.Lines.Length == 5))
            {
                double g = tab.S;
                var ends = new[] { tab.Top - g, tab.Bottom + g }.Select(yo => At((int)Math.Round(yo))).Where(o => o > 0 && o < h - 1 && count[o] >= 0.33 * lineDark)
                                                               .OrderByDescending(o => count[o]).ToList();
                if (ends.Count == 0) continue;
                int at = ends[0], top = at, bot = at;
                while (top > 0 && count[top - 1] >= 0.5 * count[at]) top--;
                while (bot < h - 1 && count[bot + 1] >= 0.5 * count[at]) bot++;
                tab.Lines = tab.Lines.Append(new Line { Top = top, Bot = bot }).OrderBy(l => l.Y).ToArray();
            }
        }
        if (extras != null && spacing > 0 && staves.Count > 0)
        {
            // a one-line staff (a percussion line): a row darker than any within half a space, as dark
            // as a quarter of the staves' lines (it prints thin: Fantasy p05's a third), clear of every
            // staff, thin as a line, with no two such rows a space apart on one side of it (a staff's
            // line), below a five-line staff. Not the row test's lines: a faint scanned line breaks
            // into stretches too short for it (Fantasy p02). Its width is checked below, and rows of
            // it a TAB's spacing apart are a TAB the row test missed
            var found = staves.Concat(extras).ToList();
            double lineDark = staves.SelectMany(st => st.Lines).Select(l => (double)count[(int)Math.Round(l.Y)]).OrderBy(v => v).ElementAt(staves.Count * 5 / 2);
            // (three rows summed: a faint line on a tilted page spreads over three or four, p05)
            int half = (int)(0.5 * spacing), one = (int)Math.Round(spacing);
            var c3 = new int[h];
            for (int y = 1; y < h - 1; y++) c3[y] = count[y - 1] + count[y] + count[y + 1];
            for (int y = half; y < h - half; y++)
            {
                if (c3[y] < 0.4 * lineDark) continue;
                bool peak = true;
                for (int dy = -half; dy <= half && peak; dy++) if (c3[y + dy] > c3[y] || (dy < 0 && c3[y + dy] == c3[y])) peak = false;
                if (!peak) continue;
                // (its rows: those at least half as dark as the darkest of the three)
                int dark = Math.Max(count[y - 1], Math.Max(count[y], count[y + 1])), top = y, bot = y;
                while (top > 0 && count[top - 1] >= 0.5 * dark) top--;
                while (bot < h - 1 && count[bot + 1] >= 0.5 * dark) bot++;
                bool Line(int yy) { for (int dy = -1; dy <= 1; dy++) if (yy + dy >= 0 && yy + dy < h && c3[yy + dy] >= 0.5 * c3[y]) return true; return false; }
                string why = null;
                // (a TAB's line the row test and the comb both missed stands a TAB's spacing off it. The
                // row's own rows, not its darkest alone: the beams stacked over a keyboard's staff, the
                // hi-hat's x heads over a drum staff, reach its outer line with their darkest row half a
                // space off it -- MuseScore's band010 and band004)
                if (found.Any(st => bot > st.Top - (st.Kind == "tab" ? 1.6 : 0.5) * spacing && top < st.Bottom + (st.Kind == "tab" ? 1.6 : 0.5) * spacing)) why = "within a staff";
                // (a staff's line, the staff itself missed, has two more lines a space apart on one
                // side of it; a percussion line's noteheads under it are one row a space off --
                // Emmentaler's band008, the line's rows unmatched past its heads' and lost)
                else if (Line(y - one) && Line(y - 2 * one) || Line(y + one) && Line(y + 2 * one)) why = "two lines a space apart on one side";
                // (a lead sheet's slash staff stands under the voice's three verses of words, 13.6
                // spaces below its staff; Fantasy's percussion lines within 12)
                else if (!staves.Any(st => y > st.Bottom && y - st.Bottom < 20 * spacing)) why = "under no staff";
                // a line is thinner than a beam, half a space (Emmentaler's 0.48, a scan's 0.44-0.67):
                // a scanned line's blur spreads it over three to five rows, Fantasy's 0.22-0.37; the row
                // of a percussion line's noteheads, stems and beams merged with it, or of words, is a space and more
                else if (bot - top + 1 > 0.42 * spacing) why = $"{(bot - top + 1) / spacing:F2} spaces thick";
                if (why != null) { if (TraceJoins) Console.WriteLine($"  row {y} ({top}-{bot}): no one-line staff, {why}"); continue; }
                extras.Add(new Staff { Lines = new[] { new Line { Top = top, Bot = bot } }, Kind = "line" });
            }
            extras.Sort((a, b) => a.Top.CompareTo(b.Top));
        }
        foreach (var st in extras == null ? staves : staves.Concat(extras))
        {
            // (a one-line staff has no spacing of its own: the page's)
            double sp = st.S > 0 ? st.S : spacing;
            // the staff's width: the longest stretch where any of its five lines has ink, gaps of
            // up to two spaces bridged (a scan's lines break up, each in its own places)
            int gapMax = (int)(2 * sp);
            int best = 0, a = -1, last = -1;
            for (int x = 0; x <= w; x++)
            {
                // (ink on a line's row that is not part of something tall: a brace or a bracket
                // joining the staves, an instrument's name, stand left of the staff and would be
                // bridged into it; a line's own pixels have white half a space above and below)
                bool dark = false;
                if (x < w)
                    foreach (var ln in st.Lines)
                    {
                        int y = (int)Math.Round(ln.Y), half = (int)(0.5 * sp);
                        if ((d[x, y] || d[x, y - 1] || d[x, y + 1]) && !(d[x, y - half] && d[x, y + half])) { dark = true; break; }
                    }
                if (dark) { if (a < 0 || x - last > gapMax) a = x; last = x; }
                if (a >= 0 && (x == w || x - last > gapMax))
                {
                    if (last - a + 1 > best) { best = last - a + 1; st.Left = a; st.Right = last; }
                    a = -1;
                }
            }
            // a clean page: the middle line runs unbroken from the staff's start to its end, and a
            // brace, a bracket or a name left of it is not joined to it. That stretch is the staff
            // when it is most of the bridged one; a broken line's stretches are short, and then the
            // bridged extent stands
            // a clean page: the middle line runs unbroken from the staff's start to its end (a
            // scan's small breaks, under a third of a space, count as unbroken), and a brace, a
            // bracket or a name left of it stands clear of it by more than that. That stretch is
            // the staff when it is most of the bridged one; a badly broken line's stretches are
            // short, and then the bridged extent stands
            // The staff begins at the first column where most of its five lines are there, and
            // the ink is not one stroke down the whole staff (the brace or bracket that joins the
            // staves of a system, or the system's own opening barline, stand at or before the
            // start). A name left of the staff is never five lines tall. The bridged extent's end
            // is the staff's end (a scanned line breaks, and nothing joins staves on the right).
            // (a clean page first: the middle line unbroken - small breaks allowed - over nearly
            // the whole bridged extent starts where the staff starts, whatever stands left of it)
            // (a TAB's middle is between two lines: its middle line is the one below)
            int mid = (int)Math.Round(st.Lines[st.Lines.Length / 2].Y), small = (int)(0.3 * sp);
            int runBest = 0, runL = 0, ra = -1, rlast = -1;
            for (int x = 0; x <= w; x++)
            {
                bool on = x < w && (d[x, mid] || d[x, mid - 1] || d[x, mid + 1]);
                if (on) { if (ra < 0 || x - rlast > small) ra = x; rlast = x; }
                if (ra >= 0 && (x == w || x - rlast > small))
                {
                    if (rlast - ra + 1 > runBest) { runBest = rlast - ra + 1; runL = ra; }
                    ra = -1;
                }
            }
            // (a line's own pixel has white half a space above and below it; a brace's, a
            // bracket's or a barline's has ink there, and does not count)
            int Lit(int x)
            {
                int lit = 0, half = (int)(0.5 * sp);
                foreach (var ln in st.Lines)
                {
                    int y = (int)Math.Round(ln.Y);
                    if ((d[x, y] || d[x, y - 1] || d[x, y + 1]) && !(d[x, y - half] && d[x, y + half])) lit++;
                }
                return lit;
            }
            int need = Math.Min(3, st.Lines.Length);
            if (runBest >= 0.8 * (st.Right - st.Left + 1))
            {
                // (unless the staff's lines go on left of the run, most columns there lit: a scan's
                // middle line broken once by the clef's body and the first notes -- kinkel10-4's left
                // hand, its unbroken run from the first beat's third note, the clef and key left of the staff)
                int litFrom = Enumerable.Range(st.Left, Math.Max(0, runL - st.Left)).Where(x => Lit(x) >= need).DefaultIfEmpty(runL).First();
                int lit = litFrom >= runL ? 0 : Enumerable.Range(litFrom, runL - litFrom).Count(x => Lit(x) >= need);
                if (runL - litFrom > 3 * sp && lit >= 0.5 * (runL - litFrom))
                {
                    if (TraceJoins) Console.WriteLine($"  staff at y {st.Mid:F0}: bridged extent {st.Left}-{st.Right}, the middle line unbroken from {runL} for {runBest}, but three lines lit over {lit} of the {runL - litFrom} columns from {litFrom}: starts at {litFrom}");
                    st.Left = litFrom; continue;
                }
                if (TraceJoins) Console.WriteLine($"  staff at y {st.Mid:F0}: bridged extent {st.Left}-{st.Right}, the middle line unbroken from {runL} for {runBest}: starts there");
                st.Left = runL; continue;
            }
            int bridged = st.Left;
            for (int x = st.Left; x < st.Right; x++)
            {
                if (Lit(x) < need) continue;
                st.Left = x;
                break;
            }
            if (TraceJoins) Console.WriteLine($"  staff at y {st.Mid:F0}: bridged extent {bridged}-{st.Right}, the middle line's longest run {runBest} from {runL}: starts at {st.Left}, where three lines are lit");
        }
        // a comb's staff on a page that has clear staves is about as wide as they are (a first
        // system's indent, a ragged last system): the row of a lead sheet's chord diagrams, five
        // lines a space apart across the page, scores a comb, but each diagram's lines run four
        // spaces and nothing bridges the gaps between the diagrams (leadsheet-collide: a staff of
        // one measure, read as a part's, above every system)
        if (combed.Count > 0 && combed.Count < staves.Count)
        {
            var clear = staves.Where(st => !combed.Contains(st)).Select(st => (double)(st.Right - st.Left + 1)).OrderBy(v => v).ToList();
            double usualW = clear[clear.Count / 2];
            foreach (var st in combed.Where(st => st.Right - st.Left + 1 < 0.2 * usualW).ToList())
            {
                Console.WriteLine($"  staff at y {st.Mid:F0} from the comb: {st.Right - st.Left + 1} wide to the clear staves' {usualW:F0}, not a staff");
                staves.Remove(st);
            }
        }
        // a system's staves all end alike: a staff whose end runs on past the page's usual end
        // without its lines' ink there - a degraded page's speckle, bridged - ends at the usual end.
        // Speckle inks the rows between the lines as often as the lines' own, a staff's lines far
        // more often (Bravura band003's drum staff, run on into the speckle and a barline found
        // there; where the toner left a whole page's lines faint, all its staves end alike)
        if (staves.Count >= 3)
        {
            // (the lower median: one system of two run on whole is half the page's staves, the
            // detector seeing no barline in its speckle - Petaluma band002's second page; a short
            // last system's lower end keeps the longer staves by their lines' ink)
            int usual = staves.Select(st => st.Right).OrderBy(v => v).ElementAt((staves.Count - 1) / 2);
            foreach (var st in staves.Where(st => st.Lines.Length >= 4 && st.Right > usual + 2 * st.S))
            {
                int nl = st.Lines.Length;
                var ys = st.Lines.Select(ln => (int)Math.Round(ln.Y)).ToArray();
                bool Ink(int x, int y) => d[x, y] || d[x, y - 1] || d[x, y + 1];
                double lead = 0;
                int from = usual + (int)st.S;
                for (int x = from + 1; x <= st.Right; x++)
                    lead += (double)ys.Count(y => Ink(x, y)) / nl - (double)Enumerable.Range(1, nl - 1).Count(k => Ink(x, (ys[k - 1] + ys[k]) / 2)) / (nl - 1);
                if (lead >= 0.1 * (st.Right - from)) continue;
                int end = from;
                while (end > st.Left && !ys.Any(y => Ink(end, y))) end--;
                Console.WriteLine($"  staff at y {st.Mid:F0}: its end {st.Right} past the page's usual {usual} without its lines' ink, moved to {end}");
                st.Right = end;
            }
        }
        // a one-line staff runs from the staves' start to their end (a lyric's extender line or a
        // volta's bracket, lines on their own too, run a few measures from a measure's start; a
        // system's staves all start and end alike, the first system's indent aside)
        if (extras != null && staves.Count > 0)
        {
            double sp = staves.Average(st => st.S);
            // the staff the row stands under: a line, or a TAB, runs the width of its own system's
            // staves (a first system's indented, a short last system's), whatever the page's usual
            // (a staff's own ends vary where its lines break; the start is looser, a line's first
            // column found past the bracket's foot)
            Staff Above(Staff ln) => staves.Where(st => st.Top < ln.Top).OrderByDescending(st => st.Top).First();
            var found = staves.Concat(extras).Where(st => st.Kind != "line").ToList();
            double lineDark = staves.SelectMany(st => st.Lines).Select(l => (double)count[(int)Math.Round(l.Y)]).OrderBy(v => v).ElementAt(staves.Count * 5 / 2);
            // (a faint line breaks into stretches further apart than the bridging: how much of that
            // staff's width has the line's ink, with white half a space above and below)
            double Cover(Staff ln)
            {
                var st = Above(ln);
                int hs = (int)(0.5 * sp), n = 0, y0 = ln.Lines[0].Top - 1, y1 = ln.Lines[0].Bot + 1;
                for (int x = st.Left; x <= st.Right; x++)
                {
                    bool ink = false;
                    for (int y = y0; y <= y1 && !ink; y++) ink = d[x, y];
                    if (ink && !d[x, y0 - hs] && !d[x, y1 + hs]) n++;
                }
                return (double)n / (st.Right - st.Left + 1);
            }
            var cover = extras.Where(e => e.Kind == "line").ToDictionary(e => e, Cover);
            // a TAB the row test never found (its lines broken by the digits on them, Bravura's
            // band008; a short last system's, under a quarter of the page) and the comb never looked
            // for (it looks only where the rows found one): four to six of these rows evenly spaced
            // at a TAB's spacing, 1.15 to 1.8 times the staves', each with most of its staff's width
            // (a row of words is thicker, and stands alone or two or three verses apart)
            var rows1 = extras.Where(e => e.Kind == "line").OrderBy(e => e.Top).ToList();
            var taken = new HashSet<Staff>();       // (the rows of the TABs made here, and the rows among them: a beam's, the digits' tops)
            for (int i = 0; i < rows1.Count; i++)
            {
                if (taken.Contains(rows1[i])) continue;
                // the longest chain of rows a TAB's spacing apart from this one, other rows between
                // them passed over (the digits' tops a space under a line -- Emmentaler's band010)
                List<Staff> run = null;
                for (int k = i + 1; k < rows1.Count; k++)
                {
                    if (taken.Contains(rows1[k])) continue;
                    double gk = rows1[k].Top - rows1[i].Top;
                    if (gk < 1.15 * sp) continue;
                    if (gk > 1.8 * sp) break;
                    var chain = new List<Staff> { rows1[i], rows1[k] };
                    for (double next = rows1[k].Top + gk; ; next += gk)
                    {
                        var r = rows1.FirstOrDefault(q => !taken.Contains(q) && Math.Abs(q.Top - next) <= 0.15 * gk);
                        if (r == null) break;
                        chain.Add(r);
                    }
                    if (run == null || chain.Count > run.Count) run = chain;
                }
                if (run == null || run.Count < 3) continue;
                int n = run.Count;
                double g = (run[n - 1].Top - run[0].Top) / (n - 1);
                var lines1 = run.Select(r => r.Lines[0]).ToList();
                // (an outer line of it lost in the beams along it, which the bass's eighths lay on
                // the bottom line -- Emmentaler's band004: the row a spacing beyond the run, as dark
                // as a third of the staves' lines, is its line, however thick the beams make it)
                if (n >= 3 && n <= 5 && run.All(r => cover[r] >= 0.4))
                    foreach (double yo in new[] { run[0].Top - g, run[n - 1].Top + g })
                    {
                        int at = Math.Clamp((int)Math.Round(yo), 0, h - 1);
                        for (int dy = -2; dy <= 2; dy++) if (count[Math.Clamp(at + dy, 0, h - 1)] > count[at]) at = Math.Clamp(at + dy, 0, h - 1);
                        if (count[at] >= 0.33 * lineDark && !found.Any(st => at > st.Top - 0.5 * sp && at < st.Bottom + 0.5 * sp)) { lines1.Add(new Line { Top = at - 1, Bot = at + 1 }); n++; }
                    }
                lines1.Sort((p, q) => p.Y.CompareTo(q.Y));
                if (n >= 4 && n <= 6 && run.All(r => cover[r] >= 0.4) && run.Average(r => cover[r]) >= 0.6)
                {
                    var over = Above(run[0]);
                    var tab = new Staff { Lines = lines1.ToArray(), Kind = "tab", Left = over.Left, Right = over.Right };
                    if (TraceJoins) Console.WriteLine($"  TAB of {n} lines at y {tab.Top:F0} from the rows (spacing {tab.S:F1}, covering {run.Min(r => cover[r]):F2}-{run.Max(r => cover[r]):F2})");
                    // (its rows, and whatever other rows stand within it, are no one-line staff)
                    taken.UnionWith(rows1.Where(q => q.Top > tab.Top - 0.5 * sp && q.Top < tab.Bottom + 0.5 * sp));
                    extras.Add(tab);
                }
            }
            foreach (var r in taken) { extras.Remove(r); cover.Remove(r); }
            // (Fantasy's percussion lines cover 0.80-0.96, a line under sixteenths on every beat
            // 0.75-0.77 (band008's, MuseJazz's heads the widest); the extenders, brackets and rows
            // of text that pass the rest 0.58 at most, a scanned page's beams along a staff 0.66-0.67;
            // of two rows within half a space, the one covering more)
            extras.RemoveAll(ln => ln.Kind == "line" && (cover[ln] < 0.7 || cover.Any(o => o.Key != ln && Math.Abs(o.Key.Top - ln.Top) < 0.5 * sp
                                                                                          && (o.Value > cover[ln] || o.Value == cover[ln] && o.Key.Top < ln.Top))));
            if (TraceJoins)
                foreach (var (ln, c) in cover)
                    Console.WriteLine($"  line at y {ln.Lines[0].Top}-{ln.Lines[0].Bot} ({(ln.Lines[0].Bot - ln.Lines[0].Top + 1) / sp:F2} sp thick): covers {c:F2} of its staff's width -> {(extras.Contains(ln) ? "a one-line staff" : "dropped")}");
            foreach (var ln in extras.Where(e => e.Kind == "line")) { var st = Above(ln); ln.Left = st.Left; ln.Right = st.Right; }
            extras.Sort((a, b) => a.Top.CompareTo(b.Top));
        }
        if (TraceJoins)
            foreach (var st in (extras == null ? staves : staves.Concat(extras)).OrderBy(st => st.Top))
                Console.WriteLine($"  {(st.Kind == "" ? "staff" : st.Kind)} y {st.Top:F0}-{st.Bottom:F0} ({st.Lines.Length} lines, spacing {st.S:F1}) x {st.Left}-{st.Right}");
        return staves;
    }

    /// <summary>Per row, how far its ink spreads: the cells of 16 columns it touches, as a solid line of that width would count (into <paramref name="count"/>).</summary>
    static void CellCounts(Img d, int[] count)
    {
        const int cell = 16;
        int w = d.W;
        for (int y = 0; y < d.H; y++)
        {
            int cells = 0, o = y * w;
            for (int x0 = 0; x0 + cell <= w; x0 += cell)
                for (int x = x0; x < x0 + cell; x++) if (d.D[o + x]) { cells++; break; }
            count[y] = cells * cell;
        }
    }

    /// <summary>
    /// The staff spacing of a page with no clear staff, from where the rows' ink repeats: the smallest
    /// shift at which the row counts agree with themselves (five lines a space apart), or 0.
    /// (The counts less their mean, so that the agreement is in the lines, not in the page's general
    /// darkness; the spacing is a peak of the agreement over the shifts a fifth either side: near no
    /// shift at all the rows of every thick thing agree, and on a page whose lines are dots that slope
    /// outweighs the lines' peak, Bravura's 9,900 at 8 to 8,700 at 20. Of the peaks, the first at
    /// least half the highest: twice the spacing peaks as high.)
    /// </summary>
    static double SpacingByAgreement(int[] count)
    {
        int h = count.Length;
        double mean = count.Average();
        var corr = new double[101];
        for (int p = 1; p <= 100; p++)
            for (int y = 0; y + p < h; y++) corr[p] += (count[y] - mean) * (count[y + p] - mean);
        var rise = new double[81];
        for (int p = 8; p <= 80; p++)
        {
            int k = Math.Max(2, (int)Math.Round(p / 5.0));
            rise[p] = corr[p] - 0.5 * (corr[p - k] + corr[p + k]);
        }
        double top = rise.Max();
        for (int p = 8; p <= 80 && top > 0; p++)
            if (rise[p] >= 0.5 * top && rise[p] >= rise[p - 1] && (p == 80 || rise[p] >= rise[p + 1]))
            {
                // (to a fraction of a row, by the parabola through the peak and its neighbours:
                // a whole row off is a line off in four spaces)
                double a = corr[p - 1], b = corr[p], c = corr[p + 1], den = a - 2 * b + c;
                return p + (den < 0 ? Math.Clamp(0.5 * (a - c) / den, -0.5, 0.5) : 0);
            }
        return 0;
    }

    /// <summary>The page's staff spacing by the rows' agreement alone (<see cref="SpacingByAgreement"/>), whatever staves were found: for a page whose staves found are scraps.</summary>
    public static double GuessSpacing(Img d)
    {
        var count = new int[d.H];
        CellCounts(d, count);
        return SpacingByAgreement(count);
    }
}
