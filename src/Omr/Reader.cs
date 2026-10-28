namespace Omr;

/// <summary>Turns a page's symbols into events per bar and part.</summary>
sealed class PageResult
{
    public int Page;
    public Img Dark, Ink;
    public double S;
    public List<SystemInfo> Systems;
    public PageSymbols Sym;
    public Dictionary<(int Bar, int Part), List<Ev>> Bars = new();
    public Dictionary<(int Bar, int Part), List<List<Ev>>> Voices = new();
    public int FirstBar, LastBar;
    public List<string> Notes = new();
    /// <summary>Where a part's TAB and its staff disagree and neither was put right (B5): for the redraw check's "? verify".</summary>
    public List<(int Bar, int Part, string Note)> TabQuarrels = new();
    /// <summary>The boxes over the systems (a rehearsal mark's), found once asked for (<see cref="Tempo.FramesOf"/>).</summary>
    public List<Tempo.Frame> Frames;
    /// <summary>The lines run out past this page's last system, for the next page to close (<see cref="Continuation"/>).</summary>
    public List<Continuation.Open> Open = new();
    /// <summary>The page before's lines run out past its last system, for this page's readers to close.</summary>
    public List<Continuation.Open> Before = new();
}

/// <summary>What carries from page to page: the clef, key and time in force, and what was used per bar.</summary>
sealed class ReaderState
{
    public bool Auto;                       // use what the page shows, not the config
    public bool Poly;                       // every head its own event (several voices on a staff)
    public int LastSystemBar;               // the first bar of the previous system
    public int PickupOffset;                // 1 when the piece opens with a pickup: the printed bar numbers leave it out
    public int SplitOffset;                 // bars split at a closed system break so far: the print counts each once, the count twice
    public bool PickupSeen;                 // the piece's first measure looks like a pickup (borne out by the next printed number)
    public (int Printed, int Counted, int Measures)? LastPrinted;   // the previous system's printed number (fairly matched), its count and measures
    public bool PageMissed;                 // a page since the last system read had no system found: the count is a page's bars off
    public double Space;                    // the previous page's staff space
    public string Time;
    public int?[] Fifths;
    public string[] Clefs;
    public Dictionary<int, string> BarTime = new();
    public Dictionary<(int Bar, int Part), int> BarKey = new();
    public Dictionary<(int Bar, int Part), string> BarClef = new();
    /// <summary>Each part-bar as read (or repeated), with its measure's x span: what a percent repeat copies.</summary>
    public Dictionary<(int Bar, int Part), (List<Ev> Evs, List<Ev> Single, double X0, double X1)> Played = new();
    /// <summary>Per part with a TAB: the octaves between staff and TAB its columns have voted for (B5).</summary>
    public Dictionary<int, Dictionary<int, int>> TabVotes = new();
    /// <summary>The part-bars a two-bar percent sign on a system's last barline fills on the next system.</summary>
    public HashSet<(int Bar, int Part)> TwoBarCarried = new();
    /// <summary>Per part p: how often the systems showing every staff had p and p+1 share their barlines, and not.</summary>
    public Dictionary<int, (int Yes, int No)> SharedBars = new();

    public ReaderState(int parts) { Fifths = new int?[parts]; Clefs = new string[parts]; }
}

/// <summary>One measure of a system as read between two barlines, before it is given a bar number.</summary>
sealed class Measure
{
    public string Time;
    public int BarTicks;
    public int[] Fifths;
    public string[] Clefs;
    public List<Ev>[] Evs;      // chords: heads on one stem are one event
    public List<Ev>[] Single;   // every head its own event
}

static class Reader
{
    public static bool NoFill;      // --no-fill: leave a scan's pinholes (to tell what the filling does)
    public static bool NoTab;       // --no-tab: the TABs check no pitches (to tell what B5 does)
    public static bool NoUnprinted; // --no-unprinted: no triplets read from a bar's length (to tell what UnprintedTriplets does)

    public static PageResult Read(string path, int page, int firstBar, ScoreConfig cfg, ReaderState state)
    {
        var d = Img.Load(path);
        if (d.Zoom != 1) Console.WriteLine($"  page {page}: a space of {Img.ZoomTo / d.Zoom:F1} pixels: read enlarged {d.Zoom:F2} times");
        // (a score with TABs or one-line staves: those are found too, and set aside)
        var extras = cfg.HasExtras ? new List<Staff>() : null;
        var staves = Layout.FindStaves(d, extras);
        if (Math.Abs(Img.LastSkew) >= 0.03) Console.WriteLine($"  page {page}: turned by {-Img.LastSkew:F2} degrees");
        if (Img.LastBow != null) Console.WriteLine($"  page {page}: bowed, its strips shifted {string.Join(" / ", Img.LastBow.Select(band => string.Join(" ", band.Select(v => v.ToString("F0")))))} rows (bands of rows, left to right), levelled");
        // (a black-and-white scan whose staff lines the cleaning took away, dots that they were:
        // with no clear staff on the cleaned page, the staves are found on the page as binarized
        // too, and the reading that finds more of them stands)
        if (Layout.LastGuessed && d.Raw != null)
        {
            var rawExtras = cfg.HasExtras ? new List<Staff>() : null;
            var found = Layout.FindStaves(d.Raw, rawExtras);
            if (found.Count + (rawExtras?.Count ?? 0) > staves.Count + (extras?.Count ?? 0))
            {
                Console.WriteLine($"  page {page}: {staves.Count} staves on the cleaned page, {found.Count} on the page as binarized");
                staves = found; extras = rawExtras;
            }
        }
        // (a page whose staves all went unfound -- a scan's toner broke every line -- has the space of
        // the page before: the detector's barlines may yet recover its staves)
        double s = staves.Count > 0 ? staves.Average(t => t.S) : state.Space;
        if (s <= 0)
        {
            state.PageMissed = true;
            return new PageResult { Page = page, Dark = d, S = s, Systems = new(), Sym = new PageSymbols(), FirstBar = firstBar, LastBar = firstBar - 1,
                                    Notes = { $"page {page}: no staff found" } };
        }
        state.Space = s;
        // a scan's black is not solid: toner leaves pinholes in a head, and a head with a hole in
        // it is no black head. Holes far smaller than any drawn one (a hollow head's, a flat's bowl) are filled.
        if (!NoFill && Img.LastGrainy) Layout.FillPinholes(d, s);
        // the learnt detector (--detector) reads the whole page once: the barlines it found tell
        // of the staves too faint for the row test (a scan's), then go to FindBars; the rest to Symbols.Find
        var det = Detector.Enabled && d.Gray != null ? Detector.Detect(d.Gray, d.W, d.H, s) : null;
        // (TAB digits under the threshold are only the TAB's: rivals to a fret's other reading, Tab.Apply)
        var weakTabs = det?.Where(Detector.IsWeakTab).ToList() ?? new List<Detection>();
        det?.RemoveAll(Detector.IsWeakTab);
        // (dots under the threshold likewise: only in the column of a surer dot, Symbols.ColumnDots)
        var weakDots = det?.Where(Detector.IsWeakDot).ToList();
        det?.RemoveAll(Detector.IsWeakDot);
        // (black heads under the threshold likewise: only a grace note's, small and solid by a stem, Symbols.Find)
        var weakHeads = det?.Where(Detector.IsWeakHead).ToList();
        det?.RemoveAll(Detector.IsWeakHead);
        // (half heads under the threshold likewise: only a rival to a black head seen about as surely, or a half note whose stem's tip is seen, Symbols.Find)
        var weakHalves = det?.Where(Detector.IsWeakHalf).ToList();
        det?.RemoveAll(Detector.IsWeakHalf);
        // (a hairpin's ends under the threshold likewise: only where the ink holds its two lines, Expressions)
        var weakHairpins = det?.Where(Detector.IsWeakHairpin).ToList() ?? new List<Detection>();
        det?.RemoveAll(Detector.IsWeakHairpin);
        // (a pedal bracket's end hooks and notches and a pedal's * likewise: only where the pedal is down and the bracket's line runs into them, Expressions)
        var weakPedals = det?.Where(Detector.IsWeakPedal).ToList() ?? new List<Detection>();
        det?.RemoveAll(Detector.IsWeakPedal);
        // (a rhythm slash under the threshold likewise: only on a one-line rhythm staff, alone, the ink leaning as a slash, Symbols.Find)
        var weakSlashes = det?.Where(Detector.IsWeakSlash).ToList();
        det?.RemoveAll(Detector.IsWeakSlash);
        // (an octave line's sign under the threshold likewise: only where its dashes run on from it in the ink, Ottavas)
        var weakOttavas = det?.Where(Detector.IsWeakOttava).ToList() ?? new List<Detection>();
        det?.RemoveAll(Detector.IsWeakOttava);
        // (a segno under the threshold likewise: only where the words of a jump back to it were read and no sure segno was seen, Form)
        var weakSegnos = det?.Where(Detector.IsWeakSegno).ToList() ?? new List<Detection>();
        det?.RemoveAll(Detector.IsWeakSegno);
        // (a trill's "tr" under the threshold likewise: only where a trill line's wave follows it, Articulations)
        var weakTrills = det?.Where(Detector.IsWeakTrill).ToList() ?? new List<Detection>();
        det?.RemoveAll(Detector.IsWeakTrill);
        // (a clef under the threshold likewise: only inside a staff, on a clef's shape in the ink, Signatures.MidClefs)
        var weakClefs = det?.Where(Detector.IsWeakClef).ToList() ?? new List<Detection>();
        det?.RemoveAll(Detector.IsWeakClef);
        // (a rest under the threshold likewise: only as a bar's one reading that fills every voice, Fixes)
        var weakRests = det?.Where(Detector.IsWeakRest).ToList() ?? new List<Detection>();
        det?.RemoveAll(Detector.IsWeakRest);
        // (a natural under the threshold likewise: only on a scan, by a head with no sign, on ink no rule named, Symbols.WeakNaturals)
        var weakNaturals = det?.Where(Detector.IsWeakNatural).ToList() ?? new List<Detection>();
        det?.RemoveAll(Detector.IsWeakNatural);
        Layout.RecoverStaves(d, staves, s, det, extras);
        Layout.StartAtClefs(staves, s, det);
        Layout.EndAtBarlines(d, staves, s, det, extras);
        var ink = Layout.MakeInk(d, staves, s, extras);
        if (Img.SaveInk != null) ink.SavePng(Path.Combine(Path.GetDirectoryName(Img.SaveInk) ?? "", Path.GetFileNameWithoutExtension(Img.SaveInk) + $"-p{page:D2}" + Path.GetExtension(Img.SaveInk)));
        var groupNotes = new List<string>();
        var systems = extras == null ? Layout.GroupSystems(staves, cfg.Parts.Count, d, s, det) : Layout.GroupSystems(staves, extras, cfg.Staves, cfg.StaffParts, groupNotes, s);
        foreach (var sys in systems) Layout.FindBars(d, sys, s, det);
        // (a system that hides the staves of parts that rest: its staves' parts, told by their clefs)
        var sym = Symbols.Find(d, ink, staves, systems, s, det, systems.Any(x => x.Staves.Count < cfg.Parts.Count)
            ? ps => Layout.AssignParts(d, systems, ps.Sigs, s, cfg.Parts.Count, p => state.Clefs[p] ?? cfg.Parts[p].Clef, state.SharedBars, groupNotes)
            : null, weakDots, weakHeads, weakSlashes, weakOttavas, weakClefs, weakHalves, weakNaturals);
        sym.WeakHairpins = weakHairpins;
        sym.WeakPedals = weakPedals;
        sym.WeakOttavas = weakOttavas;
        sym.WeakSegnos = weakSegnos;
        sym.WeakTrills = weakTrills;
        sym.WeakRests = weakRests;
        sym.Scan = d.Raw != null || d.Bowed != null;
        // a head or rest nearest a TAB or a one-line staff is that staff's, not a part's (a
        // percussion line's notes read as a vocal staff's seven spaces up, the next system's)
        // (only one the systems took: a "line" left over is something else, a long beam under a bass's notes)
        if (extras != null && extras.Count > 0)
        {
            var every = staves.Concat(systems.SelectMany(x => x.All).Where(x => x.Kind != "")).Distinct().ToList();
            // (and on it: a bass's low note just under its staff stands nearer a TAB set close below)
            bool Aside(double y) => Symbols.Nearest(every, y, s) is { Kind: not "" } x
                                    && (x.Kind == "tab" ? y > x.Top - 0.3 * s && y < x.Bottom + 0.3 * s : Math.Abs(y - x.Mid) < 2 * s);
            sym.Heads.RemoveAll(h => Aside(h.Y));
            sym.Rests.RemoveAll(r => Aside(r.Y));
        }
        var res = new PageResult { Page = page, Dark = d, Ink = ink, S = s, Systems = systems, Sym = sym, FirstBar = firstBar, Frames = sym.Frames };
        if (sym.Note != null) res.Notes.Add($"page {page}: {sym.Note}");
        foreach (var n in groupNotes) res.Notes.Add($"page {page} {n}");
        if (extras != null) res.Notes.Add($"page {page}: staves {string.Join(" ", staves.Concat(extras).OrderBy(st => st.Top).Select(st => (st.OneLine ? "L" : st.Kind == "" ? "5" : st.Kind == "tab" ? $"T{st.Lines.Length}" : "L") + $"@{(st.OneLine ? st.Mid : st.Top):F0}"))}, space {s:F1}");
        // the system's opening line (or a bracket) read as a barline, with the staff's left edge
        // found too far left (an instrument name at the staff's height): a system's first measure
        // holds its clefs, so a "barline" standing before a clef is no barline, and what stands
        // before the clef (the name's letters read as rests) is nothing of the music's
        foreach (var sys in systems)
        {
            // (each staff's first clef; the edge may be off by the width of the name, several spaces)
            var clefs = sym.Sigs.Where(g => g.Clef != null && !g.Mid && sys.Staves.Contains(g.St)).GroupBy(g => g.St)
                .Select(grp => grp.OrderBy(g => g.X).First()).Where(g => g.X < sys.Bars[0] + 14 * s).ToList();
            if (clefs.Count == 0) continue;
            while (sys.Bars.Count > 2 && clefs.Any(g => g.X > sys.Bars[1] - 0.5 * s))
            {
                res.Notes.Add($"page {page} system {sys.Index + 1}: barline at x {sys.Bars[1]:F0} stands before the clef, removed");
                sys.Bars.RemoveAt(1);
            }
            foreach (var g in clefs)
            {
                sym.Rests.RemoveAll(r => r.St == g.St && r.X < g.X);
                sym.Sigs.RemoveAll(o => o.St == g.St && o.Clef == null && o.X < g.X);
            }
        }
        // a time signature's upright read as a barline: Finale Broadway's 4 has a straight stem, and
        // 4/4 on every staff stacks into a line through the system (piano007, ties005: an empty
        // first measure, every bar after one off). A barline is not inside the time signature read
        // on every staff (a change of meter stands after its barline, not around it)
        foreach (var sys in systems)
            for (int m = 1; m + 1 < sys.Bars.Count; m++)
            {
                double x = sys.Bars[m];
                if (!sys.Staves.All(st => sym.Sigs.Any(g => g.St == st && g.Time != null && x > g.X - 0.2 * s && x < g.X + 2 * s))) continue;
                res.Notes.Add($"page {page} system {sys.Index + 1}: barline at x {x:F0} stands in the time signature, removed");
                sys.Bars.RemoveAt(m);
                m--;
            }
        // after the last barline, a courtesy key or time signature with no notes or rests is not a measure
        foreach (var sys in systems)
        {
            if (sys.Bars.Count < 3) continue;
            double a = sys.Bars[^2], b = sys.Bars[^1];
            bool noHeads = !sym.Heads.Any(h => sys.Staves.Contains(h.St) && h.X > a && h.X < b);
            bool empty = noHeads && !sym.Rests.Any(r => sys.Staves.Contains(r.St) && r.X > a && r.X < b);
            // (a courtesy key's flats may read as rests: no heads and a key or time read there, or a
            // sliver under six spaces with no heads, will do -- Fantasy p08's, 5.3 spaces, no key read)
            bool courtesy = noHeads && sym.Sigs.Any(g => sys.Staves.Contains(g.St) && g.X > a && g.X < b && (g.Fifths != null || g.Time != null));
            // (with no rests either, a wider one: a key's naturals cancelling three flats, four
            // sharps and a 5/8, 11.4 spaces -- piano-collide's end of bar 8)
            if (empty && b - a < 8 * s || courtesy && b - a < (empty ? 18 : 10) * s || noHeads && b - a < 6 * s) sys.Bars.RemoveAt(sys.Bars.Count - 1);
        }
        // a repeat's opening barline after the clef and key: what stands before it, signatures only,
        // is no measure (Fantasy p09)
        foreach (var sys in systems)
        {
            if (sys.Bars.Count < 3) continue;
            double a = sys.Bars[0], b = sys.Bars[1];
            bool empty = !sym.Heads.Any(h => sys.Staves.Contains(h.St) && h.X > a && h.X < b)
                      && !sym.Rests.Any(r => sys.Staves.Contains(r.St) && r.X > a && r.X < b);
            // (with the repeat's dots right after the line, a wider one: a clef, seven flats and a C
            // sign stand 13.7 spaces -- gen-rep's chords001)
            // (the rules' dots, or the detector's opening sign: with it, the rules' dots there are
            // dropped before RepeatDots sees them, as no dot of a note's)
            bool dotted = sym.Others.Any(o => o.Class == "repeat_dot" && sys.Staves.Contains(o.St) && o.C.MinX > b && o.C.MinX < b + 2.5 * s)
                       || sym.Detections != null && sym.Detections.Any(t => t.Class == "rep_open" && t.Score >= Detector.Threshold && t.X > b && t.X < b + 2.5 * s
                                                                             && t.Y > sys.Staves[0].Top - s && t.Y < sys.Staves[^1].Bottom + s);
            if (empty && b - a < (dotted ? 18 : 12) * s)
            {
                res.Notes.Add($"page {page} system {sys.Index + 1}: barline at x {b:F0} ends a measure of signatures only, removed");
                sys.Bars.RemoveAt(1);
                // (the opening's thin line read as a barline of its own, its thick one apart from it,
                // ends the same sliver: kinkel7-1's system 3, 1.1 spaces apart)
                if (sys.Bars.Count >= 3 && sys.Bars[1] - b < 1.5 * s)
                {
                    res.Notes.Add($"page {page} system {sys.Index + 1}: barline at x {sys.Bars[1]:F0}, the opening's other line, removed");
                    sys.Bars.RemoveAt(1);
                }
            }
        }
        // a system that stops and starts again (a coda after a gap in the staves): the final
        // barline and the new opening line hold an empty sliver between them, no measure
        // (Time After Time p07); the new clefs go with the measure after
        foreach (var sys in systems)
            for (int m = 1; m + 2 < sys.Bars.Count; m++)
            {
                double a = sys.Bars[m], b = sys.Bars[m + 1];
                if (b - a >= 8 * s) continue;
                if (sym.Heads.Any(h => sys.Staves.Contains(h.St) && h.X > a && h.X < b) || sym.Rests.Any(r => sys.Staves.Contains(r.St) && r.X > a && r.X < b)) continue;
                // (the staff lines break there: the first staff's lines hold ink in under half the columns between)
                var st0 = sys.Staves[0];
                int cols = 0, inked = 0;
                for (int x = (int)(a + 0.5 * s); x < b - 0.5 * s; x++, cols++)
                    if (st0.Lines.Count(ln => d[x, (int)Math.Round(ln.Y)] || d[x, (int)Math.Round(ln.Y) - 1] || d[x, (int)Math.Round(ln.Y) + 1]) >= 3) inked++;
                if (cols == 0 || inked >= 0.5 * cols) continue;
                res.Notes.Add($"page {page} system {sys.Index + 1}: the staves break between the barlines at x {a:F0} and {b:F0}, the second removed");
                sys.Bars.RemoveAt(m + 1);
                m--;
            }

        // a measure that stands for several bars of rest (MultiRests): the count runs on by them all
        MultiRests.Find(res, ink, s);
        int bar = firstBar;
        // whether the count is in doubt: the system before had a measure that does not fill its
        // time (a barline missed or invented). Before the first system of the page it always is.
        bool doubt = true;
        // how far the measures of the system before overflow their time, in bars (null before the
        // page's first system, or when a measure there falls short: nothing to weigh)
        double? over = null;
        if (systems.Count == 0) state.PageMissed = true;
        // the system before's last measure, and that system: a bar an old print splits across the
        // system break is joined from them (below)
        Measure tail = null;
        SystemInfo tailSys = null;
        foreach (var sys in systems)
        {
            // the printed bar number, when there is one, overrides the running count. A piece that
            // opens with a pickup prints the bar after it as 1: the count, which takes the pickup as
            // bar 1, is one ahead (taken as printed, the second system's number wrote over the first
            // system's last measure, and every bar after was one off)
            // (a first measure read short by mistake looks like a pickup too: the offset is taken only
            // when the first number printed after it is the count less one)
            // a bar split at a closed system break (a repeat opening the next system: kinkel7-1's bar 6,
            // "a8 r8 r8 r8 :|" there and "|: r8 r8 c4." here): the system before's last measure and
            // this one's first, each short of its time, together filling it exactly, are two measures to
            // the count and one bar to the print. Every number printed from here on is one behind the count
            // (a time signature's upper number, against the measures it governs: before any measure of
            // the system is built, a trial one too -- a repeat's thick line read as 1/1 must be gone)
            CheckTimes(res, page, sys, sym, s, state);
            bool splitHead = false;
            if (tail != null && sys.Measures > 0 && Short(tail) && !EndsOpen(d, tailSys, s))
            {
                var kept = sym.Unprinted.Where(u => u.Key.Bar == bar).ToList();
                var head = BuildMeasure(sys, sym, sys.Bars[0], sys.Bars[1], bar, s, cfg, state);
                foreach (var u in kept) sym.Unprinted[u.Key] = u.Value;
                foreach (int p in Enumerable.Range(0, cfg.Parts.Count).Where(p => !kept.Any(u => u.Key.Part == p))) sym.Unprinted.Remove((bar, p));
                // (a staff the system before hides says nothing: the voice rests the bar's first half there.
                // Every staff with something on both sides is short on both, and one at least makes the bar
                // exactly: kinkel7-1's right hand reads a chord's lowest head, its stem down, as a quarter)
                var both = Enumerable.Range(0, cfg.Parts.Count).Where(p => !Hidden(tail.Evs[p]) && head.Evs[p].Count > 0).ToList();
                bool split = head.BarTicks == tail.BarTicks && both.Count > 0
                             && both.All(p => tail.Evs[p].Sum(e => e.Ticks) < tail.BarTicks && head.Evs[p].Sum(e => e.Ticks) < head.BarTicks)
                             && both.Any(p => Fills(tail.Evs[p].Concat(head.Evs[p]).ToList(), tail.BarTicks));
                static bool Hidden(List<Ev> evs) => evs.Count == 0 || evs.Count == 1 && evs[0].Blank;
                if (split)
                {
                    splitHead = true;
                    state.SplitOffset++;
                    res.Notes.Add($"page {page} system {sys.Index + 1}: its first measure and the system before's last make one bar split at a closed break: the numbers printed one more behind the count");
                }
            }
            var raw = Digits.BarNumber(d, sys, s);
            double digitScore = Digits.LastScore;
            if (state.PickupSeen && state.PickupOffset == 0 && raw == bar - 1 && Digits.LastScore >= 0.5) state.PickupOffset = 1;
            if (raw.HasValue) state.PickupSeen = false;
            var printed = raw is int pn ? pn + state.PickupOffset + state.SplitOffset : (int?)null;
            // a number that agrees with the count teaches this score's digit shapes
            if (printed.HasValue && printed.Value == bar) Digits.Learn(d, Digits.LastDigits, printed.Value);
            res.Notes.Add($"page {page} system {sys.Index + 1}: printed {(printed.HasValue ? printed.Value.ToString() : "-")}, counted {bar}, {sys.Measures} measures");
            // a misread number would throw everything after it out of line: trust it only near the
            // count, only when the count itself is in doubt (on a scan a 9 reads as an 8, and the
            // count of a system whose every measure fills its time is right), and only when every
            // digit matched its shape well: the toner closes a 9's tail and it reads as an 8 at
            // 0.43, while a digit read right scores 0.55 and up. (Weighing the doubt's direction,
            // a measure overflowing against two in a row falling short, was tried and helped only
            // the rules alone on a colour scan, while the fill set lost 5 points.)
            // (and a system's number is past the one before it: a 9 read as a 6 must not win)
            // A number past the count says barlines were missed in the system before, each merging two
            // measures into one that holds a bar too much: the measures there must overflow by about as
            // many bars (the toner fills a 6's bowl and it reads as an 8 at 0.55, the system before
            // overflowing by an eighth: tuplets006 on fill)
            bool missedBars = !printed.HasValue || printed.Value < bar || over is not double o || o >= printed.Value - bar - 0.5;
            // Two numbers in a row that run on by the measures between them are not misread, however
            // poorly their digits match: the count lost its way before them (a degraded page whose
            // staves went unfound: every bar after it one system off, Petaluma's band010 on the scan set).
            // Ahead of the count (systems missed) by a page's bars at most, and not one digit's shape
            // misread alike in both: that chains as well (Fugue 1's 22, 25 as 32, 35; a font's 10, 13
            // as 70, 73; scan-ties004's 10, 12 as 0, 2, the 1 lost)
            bool chained = printed.HasValue && printed.Value > bar && printed.Value - bar <= 16 && digitScore >= 0.4
                           && state.LastPrinted is var (lp, lc, lm) && lp + lm == printed.Value
                           && !(Swap(lc, lp) is { } sw && sw == Swap(bar, printed.Value));
            // (after a page with no system found, ahead of the count by a page's bars: FinaleBroadway
            // band008's second page unfound on the older degraded board, its third page's 9 counted 4)
            int reach = state.PageMissed ? 16 : 4;
            // (behind the count by four bars at most: past a 20 bars' rest, 35 read as 15 must not win)
            if (printed.HasValue && printed.Value != bar && (Math.Abs(printed.Value - bar) <= 4 || printed.Value > bar && printed.Value - bar <= reach) && printed.Value > state.LastSystemBar && doubt
                && Digits.LastScore >= 0.5 && missedBars)
            {
                res.Notes.Add($"page {page} system {sys.Index + 1}: counted bar {bar}, printed {printed}");
                bar = printed.Value;
            }
            else if (chained)
            {
                res.Notes.Add($"page {page} system {sys.Index + 1}: counted bar {bar}, printed {printed} (and the system before printed {state.LastPrinted.Value.Printed}, counted {state.LastPrinted.Value.Counted}, with {state.LastPrinted.Value.Measures} measures)");
                bar = printed.Value;
            }
            else if (printed.HasValue && printed.Value != bar)
                res.Notes.Add($"page {page} system {sys.Index + 1}: printed {printed} not trusted (digits matched {Digits.LastScore:F2}{(doubt ? "" : ", count not in doubt")}{(missedBars ? "" : $", the system before overflows by {over:F2} bars")}), counted {bar} kept");
            sys.FirstBar = bar;
            state.LastSystemBar = bar;
            state.PageMissed = false;
            doubt = false;
            over = 0;
            // an old print splits a bar across the system break (Trautwein 1838, kinkel19-6: "bes4 a8 bes8"
            // ends the first system with no barline at its end, "d'4. c'8 |" opens the second): the system
            // before's last measure and this one's first, each short of its time, together filling it exactly
            // on every staff with anything in it, are one bar -- this one's first measure ends the bar before.
            // (A page's first system has no tail to join: the page before's last measure is not kept)
            bool continued = false;
            if (tail != null && sys.Measures > 0 && EndsOpen(d, tailSys, s))
            {
                // (the trial build records nothing: what it found of triplets under the tail's number is put back)
                var kept = sym.Unprinted.Where(u => u.Key.Bar == bar - 1).ToList();
                var head = BuildMeasure(sys, sym, sys.Bars[0], sys.Bars[1], bar - 1, s, cfg, state);
                foreach (var u in kept) sym.Unprinted[u.Key] = u.Value;
                if (Layout.TraceJoins)
                    Console.WriteLine($"  page {page} system {sys.Index + 1}: the system before ends open; its last measure holds {string.Join(" ", tail.Evs.Select(e => e.Sum(x => x.Ticks)))}, this one's first {string.Join(" ", head.Evs.Select(e => e.Sum(x => x.Ticks)))} of {head.BarTicks}");
                if (head.BarTicks == tail.BarTicks && Short(tail) && Short(head) && IsSplitMeasure(tail, head))
                {
                    continued = true;
                    bar--;
                    sys.FirstBar = bar;
                    res.Notes.Add($"page {page} system {sys.Index + 1}: its first measure ends bar {bar}, split at the end of the system before");
                }
                else foreach (int p in Enumerable.Range(0, cfg.Parts.Count).Where(p => !kept.Any(u => u.Key.Part == p))) sym.Unprinted.Remove((bar - 1, p));
            }
            Measure prev = null;
            // (a measure short of its time has lost what it holds, and a merged one with it would
            // not overflow: then the overflow says nothing -- the rules alone on a colour scan)
            // (the second half of a bar split at a closed break, short by design, puts no doubt on the count)
            int weighed = 0;
            void Weigh(Measure ms) { doubt |= Unfilled(ms) && !(splitHead && weighed == 0); weighed++; over = Short(ms) ? null : over + Overflow(ms); }
            // the fret numbers on the system's TABs check their parts' pitches (B5)
            var tabs = NoTab ? new() : Tab.Read(sys, sym.Detections, weakTabs, s, p => cfg.Parts[p].Clef);
            void CheckTab(Measure ms, int m, int b)
            {
                foreach (var (p, notes) in tabs)
                {
                    if (!state.TabVotes.TryGetValue(p, out var votes)) state.TabVotes[p] = votes = new();
                    var quarrels = new List<string>();
                    // (the key most of the other pitched staves read in the measure, when most agree)
                    var keys = Enumerable.Range(0, ms.Fifths.Length).Where(q => q != p && cfg.Parts[q].Clef != "percussion").Select(q => ms.Fifths[q]).ToList();
                    var common = keys.GroupBy(k => k).OrderByDescending(g => g.Count()).FirstOrDefault();
                    int? systemFifths = common != null && 2 * common.Count() > keys.Count ? common.Key : null;
                    var (changed, added) = Tab.Apply(ms.Evs[p], ms.Single[p], notes, sys.Bars[m], sys.Bars[m + 1], s, ms.Fifths[p], votes, quarrels, systemFifths);
                    if (changed + added > 0) res.Notes.Add($"page {page} system {sys.Index + 1}: bar {b} part {p}: the TAB changes {changed} pitches, adds {added}");
                    if (quarrels.Count > 0)
                    {
                        res.Notes.Add($"page {page} system {sys.Index + 1}: bar {b} part {p}: {string.Join("; ", quarrels)}");
                        res.TabQuarrels.Add((b, p, string.Join("; ", quarrels)));
                    }
                }
            }
            for (int m = 0, b = bar; m < sys.Measures; m++, b++)
            {
                var cur = BuildMeasure(sys, sym, sys.Bars[m], sys.Bars[m + 1], b, s, cfg, state);
                if (m == 0 && continued)
                {
                    // the tail's events first, this measure's after them (their x moved past the tail's end, so that
                    // everything that orders a bar's events by x -- the voices, the triplets -- keeps them in time order)
                    double shift = tailSys.Bars[^1] - sys.Bars[0] + s;
                    for (int p = 0; p < cur.Evs.Length; p++)
                    {
                        foreach (var e in cur.Evs[p].Concat(cur.Single[p])) { e.X += shift; e.StemX += shift; }
                        if (tail.Evs[p].Count == 1 && tail.Evs[p][0].Blank) continue;   // (its staff hidden there)
                        cur.Evs[p] = tail.Evs[p].Concat(cur.Evs[p]).ToList();
                        cur.Single[p] = tail.Single[p].Concat(cur.Single[p]).ToList();
                        var st = sys.Staves.FirstOrDefault(x => x.Part == p);
                        if (st == null) continue;
                        // (triplets with no number read from the whole bar's length, not from either half's)
                        sym.Unprinted.Remove((b, p));
                        if (UnprintedTriplets(cur.Evs[p], cur.Single[p], cur.BarTicks, cur.Time) is { } changed) sym.Unprinted[(b, p)] = (st, changed);
                    }
                }
                // a multi-bar rest: every staff rests its bars, the first bar holding the count (R1*n)
                if (sys.Span(m) is int span && span > 1)
                {
                    for (int k = 0; k < span; k++)
                    {
                        for (int p = 0; p < cur.Evs.Length; p++)
                        {
                            Ev Rest() => new() { IsRest = true, FullBar = true, MultiBars = k == 0 ? span : -1, Ticks = cur.BarTicks, X = (sys.Bars[m] + sys.Bars[m + 1]) / 2 };
                            cur.Evs[p] = new() { Rest() };
                            cur.Single[p] = new() { Rest() };
                        }
                        Store(res, state, sys, b + k, cur, m);
                    }
                    res.Notes.Add($"page {page} system {sys.Index + 1}: bars {b}-{b + span - 1} a multi-bar rest");
                    b += span - 1;
                    prev = cur;
                    continue;
                }
                Repeat(res, page, sys, sym, m, b, cur, s, state);
                // the piece's first measure short of its time by as much on every staff with something
                // on it: a pickup, the printed numbers one behind the count if the next one says so
                // (short on every staff, not by as much on each: a voice's pickup is a rest and a note where
                // the piano's shows one rest, its other invisible -- kinkel19-6 drawn by MuseScore. The next
                // number printed still has to bear it out)
                var lengths = cur.Evs.Where(e => e.Count > 0).Select(e => e.Sum(x => x.Ticks)).Distinct().ToList();
                if (b == 1 && m == 0 && lengths.Count >= 1 && lengths.All(l => l < cur.BarTicks))
                {
                    state.PickupSeen = true;
                    res.Notes.Add($"page {page} system {sys.Index + 1}: bar 1 may be a pickup ({string.Join("/", lengths)} of {cur.BarTicks} ticks on the staves)");
                }
                // a stem that spans the staff, a beam or a chord's head at its tip, looks like a barline.
                // What it splits are not two measures: neither fills the time, together they fill it
                // exactly. No threshold decides this, the bar length does.
                // Two such stems in a row cut a measure in three (ledger003: a dotted quarter's and its
                // eighth's stems, as thick as a thin barline, up through the staff from below it):
                // the first two pieces do not fill the time together, all three do.
                int split = 0;
                if (prev != null && IsStem(sys, sym, sys.Bars[m], s))
                {
                    if (IsSplitMeasure(prev, cur)) split = 1;
                    else if (m + 1 < sys.Measures && IsStem(sys, sym, sys.Bars[m + 1], s))
                    {
                        var next = BuildMeasure(sys, sym, sys.Bars[m + 1], sys.Bars[m + 2], b + 1, s, cfg, state);
                        var rest = new Measure { BarTicks = next.BarTicks, Evs = cur.Evs.Select((e, p) => e.Concat(next.Evs[p]).ToList()).ToArray() };
                        if (cur.BarTicks == next.BarTicks && Unfilled(cur) && Unfilled(next) && IsSplitMeasure(prev, rest)) split = 2;
                    }
                }
                if (split > 0)
                {
                    for (int k = 0; k < split; k++)
                    {
                        res.Notes.Add($"page {page} system {sys.Index + 1}: barline at x {sys.Bars[m]:F0} splits bar {b - 1}, removed");
                        sys.Bars.RemoveAt(m);
                    }
                    m--;
                    cur = BuildMeasure(sys, sym, sys.Bars[m], sys.Bars[m + 1], b - 1, s, cfg, state);
                    Repeat(res, page, sys, sym, m, b - 1, cur, s, state);
                    CheckTab(cur, m, b - 1);
                    Store(res, state, sys, b - 1, cur, m);
                    prev = cur;
                    Weigh(cur);
                    b--;
                    continue;
                }
                CheckTab(cur, m, b);
                Store(res, state, sys, b, cur, m);
                prev = cur;
                Weigh(cur);
            }
            state.LastPrinted = printed is int pr && digitScore >= 0.4 ? (pr, bar, sys.BarCount) : null;
            bar += sys.BarCount;
            tail = prev;
            tailSys = sys;
        }
        LoneTriplets(res, sym, state);
        res.LastBar = bar - 1;
        return res;
    }

    /// <summary>Where and how a printed number differs from the count when it differs in one digit only (its place from the right, the count's digit, the printed one).</summary>
    static (int Place, char From, char To)? Swap(int counted, int printed)
    {
        string a = counted.ToString(), b = printed.ToString();
        if (a.Length != b.Length) return null;
        var diff = Enumerable.Range(0, a.Length).Where(i => a[i] != b[i]).ToList();
        return diff.Count == 1 ? (a.Length - 1 - diff[0], a[diff[0]], b[diff[0]]) : null;
    }

    /// <summary>
    /// Reads the measure between x0 and x1 on every staff of the system: the signatures standing
    /// at its start, then the notes and rests as events. Updates the running clef, key and time.
    /// </summary>
    static Measure BuildMeasure(SystemInfo sys, PageSymbols sym, double x0, double x1, int b, double s, ScoreConfig cfg, ReaderState state)
    {
        // (per part: a part whose staff the system hides has nothing read, its clef and key held)
        int n = cfg.Parts.Count;
        // signatures standing at the start of this measure, before its first note or rest
        var times = new List<string>();
        var keyMoves = new List<(int? From, int? To)>();
        foreach (var st in sys.Staves)
        {
            int p = st.Part;
            double first = sym.Heads.Where(h => h.St == st && h.X > x0 && h.X < x1).Select(h => h.L)
                .Concat(sym.Rests.Where(r => r.St == st && r.X > x0 && r.X < x1).Select(r => r.L)).DefaultIfEmpty(x1).Min();
            foreach (var sg in sym.Sigs.Where(g => g.St == st && g.X > x0 - 0.5 * s && g.X < first))
            {
                if (sg.Clef != null) state.Clefs[p] = sg.Clef;
                if (sg.Fifths != null) { keyMoves.Add((state.Fifths[p], sg.Fifths)); state.Fifths[p] = sg.Fifths; }
                if (sg.Time != null) times.Add(sg.Time);
            }
        }
        // a change of key the staves shown all make alike (from one key to one other) is the hidden
        // parts' too where they held the old key: a song changes key while its voice rests
        if (keyMoves.Count > 0 && keyMoves.Distinct().Count() == 1 && keyMoves[0].From is int from && keyMoves[0].To != from
            && keyMoves.Count == sys.Staves.Count)
            for (int p = 0; p < cfg.Parts.Count; p++)
                if (!sys.Staves.Any(x => x.Part == p) && state.Fifths[p] == from) state.Fifths[p] = keyMoves[0].To;
        // and a part hidden from the first system on has the key the staves shown agree on
        var shown = sys.Staves.Select(x => state.Fifths[x.Part]).Distinct().ToList();
        if (shown.Count == 1 && shown[0] != null)
            for (int p = 0; p < cfg.Parts.Count; p++)
                if (state.Fifths[p] == null && !sys.Staves.Any(x => x.Part == p)) state.Fifths[p] = shown[0];
        // a one-line staff (a lead sheet's rhythm staff) prints no key at its start, only at a change,
        // and its slashes have no pitch: its part has the key most of the system's pitched staves have
        var pitched = sys.Staves.Where(x => !x.OneLine && cfg.Parts[x.Part].Clef != "percussion").Select(x => state.Fifths[x.Part]).Where(f => f != null).ToList();
        if (pitched.Count > 0 && pitched.GroupBy(f => f).OrderByDescending(g => g.Count()).First() is var mostly && 2 * mostly.Count() > pitched.Count)
            foreach (var st in sys.Staves.Where(x => x.OneLine)) state.Fifths[st.Part] = mostly.Key;
        if (times.Count > 0) state.Time = times.GroupBy(t => t).OrderByDescending(g => g.Count()).First().Key;
        string time = state.Auto ? state.Time ?? "4/4" : cfg.TimeAt(b);
        var ms = new Measure { Time = time, BarTicks = Dur.OfTime(time), Fifths = new int[n], Clefs = new string[n], Evs = new List<Ev>[n], Single = new List<Ev>[n] };
        for (int p = 0; p < n; p++)
        {
            var st = sys.Staves.FirstOrDefault(x => x.Part == p);
            int fifths = state.Auto ? state.Fifths[p] ?? 0 : cfg.KeyAt(b);
            // (no clef read yet for the part: the clef the layout gave it, as AssignParts goes by --
            // kinkel19-2's first system, its F clef one piece with the opening line and unseen by
            // the detector, read as treble for six bars)
            string clefName = state.Auto ? state.Clefs[p] ?? cfg.Parts[p].Clef ?? "treble" : cfg.Parts[p].Clef;
            ms.Fifths[p] = fifths;
            ms.Clefs[p] = clefName;
            if (st == null) { ms.Evs[p] = new(); ms.Single[p] = new(); continue; }
            // (the clefs inside the measure, Signatures.MidClefs -- before its first note too, as lang36-3's right
            // hand opens in the bass, its F clef printed right after the time signature: the events after each
            // read by it, and the last holds for the measures after)
            var midClefs = sym.Sigs.Where(g => g.St == st && g.Clef != null && g.Mid && g.X >= x0 && g.X < x1).OrderBy(g => g.X).ToList();
            (ms.Evs[p], ms.Single[p]) = StaffEvents(sym, st, x0, x1, s, Clefs.MiddleLine(clefName), Keys.Alterations(fifths), ms.BarTicks, midClefs, clefName.EndsWith("_8"));
            if (midClefs.Count > 0 && state.Auto) state.Clefs[p] = midClefs[^1].Clef;
            // (a measure built again, a barline taken out, forgets what the first build read)
            sym.Unprinted.Remove((b, p));
            RestsBeforeSixteenths(ms.Evs[p], ms.Single[p], ms.BarTicks);
            if (UnprintedTriplets(ms.Evs[p], ms.Single[p], ms.BarTicks, time) is { } changed) sym.Unprinted[(b, p)] = (st, changed);
        }
        return ms;
    }

    /// <summary>
    /// Triplets with no number printed (an engraver prints the first few, or none where the music
    /// plainly runs in threes: Schumann's Op.15-1). A staff's bar too long for its time, with groups
    /// of three notes of one value under one beam (eighths, sixteenths), is put right by reading
    /// such groups as triplets: the groups that make every voice fill the bar exactly, each group
    /// starting on its own length in its voice (a triplet of eighths on a quarter's beat). All the
    /// groups when they do; else the most of them that do, when only one choice of that many does.
    /// No threshold: the bar's length decides (ROADMAP §2's structure before shapes).
    /// </summary>
    /// <returns>The events made triplets' (null: none).</returns>
    static List<Ev> UnprintedTriplets(List<Ev> evs, List<Ev> single, int barTicks, string time)
    {
        // (in simple time: in 6/8 or 9/8 three beamed eighths are a beat of the meter's own)
        int den = int.Parse(time.Split('/')[1]), beat = Dur.Whole / den;
        if (NoUnprinted || den is not (2 or 4) || barTicks <= 0 || evs.Count < 3 || Fills(evs, barTicks)) return null;
        // (a group is a beam's notes with their stems one way: two voices' beams the stems of a
        // shared chord join are one piece of ink -- seed 11's piano002, bar 6)
        static (int, int) Key(Ev e) => (e.Beam, e.Stem);
        // the groups, each the indices of its events in evs (a rest among them has no beam of its own)
        var groups = new List<List<int>>();
        // (a bar too long as read, some voice past the bar: a beam of six split into triplets only then -- a bar a
        // voice short would fill by the voices run into one, kinkel19-2's 3/4 bar of "<des f>4" under six eighths)
        bool tooLong = Voices.Separate(evs.Select(e => e.Copy()).ToList(), barTicks, 0).Any(v => v.Sum(e => e.Ticks) > barTicks);
        foreach (var g in Enumerable.Range(0, evs.Count).Where(i => evs[i].Beam != 0).GroupBy(i => Key(evs[i])))
        {
            var idx = g.OrderBy(i => evs[i].X).ToList();
            var first = evs[idx[0]];
            if (!idx.All(i => !evs[i].IsRest && evs[i].Scale == (1, 1) && evs[i].Ticks == first.Ticks) || !(first.Ticks == Dur.Of(8, 0) || first.Ticks == Dur.Of(16, 0))) continue;
            // (three in a row on their side of the staff: nothing of their stems' direction between them)
            bool Clear(double xa, double xb) => !evs.Any(e => e.Stem == first.Stem && e.X > xa && e.X < xb && !idx.Contains(evs.IndexOf(e)));
            if (idx.Count == 3) { if (Clear(first.X, evs[idx[2]].X)) groups.Add(idx); continue; }
            // (six, nine or twelve under one piece of beam: an old print's beams of one triplet and the
            // next run together -- kinkel19-6's scan, its right hand "bes,8 d8 g8 bes8 g8 d8" twice a
            // bar on one beam. Each three in a row a group of its own)
            if (tooLong && idx.Count is 6 or 9 or 12)
            {
                if (Clear(first.X, evs[idx[^1]].X)) groups.AddRange(idx.Chunk(3).Select(c => c.ToList()));
                continue;
            }
            // (two under a beam and a rest of their value just before them, or just after: the beam
            // does not reach the triplet's rest -- kinkel19-6's right hand, "r8 ees8 g8" four times
            // over a bar of four quarters. Nothing stands between the rest and the pair)
            if (idx.Count != 2) continue;
            double xl = first.X, xr = evs[idx[1]].X;
            int before = -1, after = -1;
            for (int i = 0; i < evs.Count; i++)
            {
                var e = evs[i];
                if (!e.IsRest || e.FullBar || e.Beam != 0 || e.Scale != (1, 1) || e.Ticks != first.Ticks) continue;
                if (e.X < xl && (before < 0 || e.X > evs[before].X)) before = i;
                if (e.X > xr && (after < 0 || e.X < evs[after].X)) after = i;
            }
            bool Between(double xa, double xb, int r) => evs.Any(e => e.X > xa && e.X < xb && !idx.Contains(evs.IndexOf(e)) && evs.IndexOf(e) != r && (e.Stem == first.Stem || e.IsRest));
            if (before >= 0 && !Between(evs[before].X, xr, before)) groups.Add(new List<int> { before, idx[0], idx[1] });
            else if (after >= 0 && !Between(xl, evs[after].X, after)) groups.Add(new List<int> { idx[0], idx[1], after });
        }
        // (and, in a bar too long as read, the beamed notes no group took, in threes along their side of the staff
        // where a beam broke apart: a scan's beam cut in two, its triplet two under one piece and one under another --
        // kinkel19-6's bar 17, "<bes, d>8 f8 bes8" on two pieces between two whole triplets. Pieces of two and more)
        if (tooLong)
        {
            var used = groups.SelectMany(g => g).ToHashSet();
            foreach (var side in evs.Where(e => e.Beam != 0 && !e.IsRest).Select(e => e.Stem).Distinct().ToList())
            {
                var left = Enumerable.Range(0, evs.Count).Where(i => !used.Contains(i) && evs[i].Beam != 0 && !evs[i].IsRest && evs[i].Stem == side && evs[i].Scale == (1, 1)
                                                                    && (evs[i].Ticks == Dur.Of(8, 0) || evs[i].Ticks == Dur.Of(16, 0))).OrderBy(i => evs[i].X).ToList();
                var run = new List<int>();
                void Close()
                {
                    if (run.Count >= 3 && run.Count % 3 == 0 && run.Select(i => Key(evs[i])).Distinct().Count() >= 2) groups.AddRange(run.Chunk(3).Select(c => c.ToList()));
                    run.Clear();
                }
                foreach (int i in left)
                {
                    // (one value, and nothing of their side between: no other note, no rest)
                    if (run.Count > 0 && (evs[i].Ticks != evs[run[^1]].Ticks
                                          || evs.Any(e => e.X > evs[run[^1]].X && e.X < evs[i].X && (e.Stem == side || e.IsRest) && !left.Contains(evs.IndexOf(e))))) Close();
                    run.Add(i);
                }
                Close();
            }
        }
        if (Symbols.TupletVerbose)
            Console.WriteLine($"    unprinted triplets? bar at x {evs[0].X:F0}: {string.Join(" ", evs.Select(e => $"{e.Show()}{(e.Stem > 0 ? "^" : e.Stem < 0 ? "_" : "")}#{e.Beam}@{e.X:F0}"))}; {groups.Count} groups");
        if (groups.Count == 0 || groups.Count > 10) return null;
        // (per event of evs: its group, or -1)
        var groupOf = new int[evs.Count];
        Array.Fill(groupOf, -1);
        for (int k = 0; k < groups.Count; k++) foreach (int i in groups[k]) groupOf[i] = k;
        bool In(int mask, int k) => k >= 0 && (mask >> k & 1) == 1;
        // the bar's voices with the groups of the mask read as triplets (each trial event's group as evs')
        (List<List<Ev>> Voices, Dictionary<Ev, int> Group) Scaled(int mask)
        {
            var trial = evs.Select(e => e.Copy()).ToList();
            var group = new Dictionary<Ev, int>();
            for (int i = 0; i < trial.Count; i++)
            {
                group[trial[i]] = groupOf[i];
                if (In(mask, groupOf[i])) trial[i].Ticks = trial[i].Ticks * 2 / 3;
            }
            return (Voices.Separate(trial, barTicks, 0), group);
        }
        bool Works(int mask)
        {
            var (voices, group) = Scaled(mask);
            if (!voices.All(v => v.Sum(e => e.Ticks) == barTicks)) return false;
            // each group on its own length's beat in its voice
            foreach (var v in voices)
            {
                int at = 0;
                for (int i = 0; i < v.Count; i++)
                {
                    if (In(mask, group[v[i]]) && (i == 0 || group[v[i - 1]] != group[v[i]]) && at % (3 * v[i].Ticks) != 0) return false;
                    at += v[i].Ticks;
                }
            }
            return true;
        }
        // (of two choices that fill the bar alike, the one whose other notes start on their own
        // value's grid, a beat at most: three beamed eighths, a dotted quarter and three more in a
        // 4/4 bar, the first three a triplet, the quarter on the second beat -- not the last three,
        // the quarter off the beat. Seed 11's tuplets008)
        int OffGrid(int mask)
        {
            var (voices, group) = Scaled(mask);
            int off = 0;
            foreach (var v in voices)
            {
                int at = 0;
                foreach (var e in v)
                {
                    if (!In(mask, group[e]) && e.Scale == (1, 1) && Dur.Text(e.Ticks) is string t && int.TryParse(t.TrimEnd('.'), out int basis)
                        && at % Math.Min(Dur.Whole / basis, beat) != 0) off++;
                    at += e.Ticks;
                }
            }
            return off;
        }
        int all = (1 << groups.Count) - 1, chosen = -1;
        // (a group with a rest in it only where every group reads as a triplet: a bar that runs in threes
        // throughout. Among a second voice's rests, one such group alone made a bar of plain sixteenths
        // fill by the voices' rearrangement -- kinkel10-2's right hand, "r16 x x r16 r16 r16")
        int restBits = 0;
        for (int k = 0; k < groups.Count; k++) if (groups[k].Any(i => evs[i].IsRest)) restBits |= 1 << k;
        if (Works(all)) chosen = all;
        else
            for (int size = groups.Count - 1; size > 0 && chosen < 0; size--)
            {
                var fits = Enumerable.Range(1, all).Where(m => (m & restBits) == 0 && System.Numerics.BitOperations.PopCount((uint)m) == size && Works(m))
                                     .Select(m => (Mask: m, Off: OffGrid(m))).OrderBy(f => f.Off).ToList();
                if (fits.Count == 1 || fits.Count > 1 && fits[0].Off < fits[1].Off) chosen = fits[0].Mask;
                else if (fits.Count > 1) return null;
            }
        if (chosen < 0) return null;
        if (Symbols.TupletVerbose) Console.WriteLine($"      -> {System.Numerics.BitOperations.PopCount((uint)chosen)} of the {groups.Count} groups triplets");
        var changed = new List<Ev>();
        for (int k = 0; k < groups.Count; k++)
        {
            if ((chosen >> k & 1) == 0) continue;
            var key = Key(evs[groups[k].First(i => evs[i].Beam != 0)]);
            // (the group's events, and their copies in single: a beamed note's by the beam and its place
            // on it -- a beam of six holds two groups -- a rest's by its place)
            var mine = groups[k].Select(i => evs[i]).ToList();
            // (a single copy is the group's when the nearest event on its beam is: a group may span two pieces of beam)
            var keys = mine.Where(e => !e.IsRest && e.Beam != 0).Select(Key).ToHashSet();
            bool Mine(Ev e) => mine.Contains(evs.Where(o => !o.IsRest && o.Beam != 0 && Key(o) == Key(e)).MinBy(o => Math.Abs(o.X - e.X)));
            var alike = single.Where(e => e.Beam != 0 ? keys.Contains(Key(e)) && Mine(e) : mine.Any(r => r.IsRest && e.IsRest && e.X == r.X && e.Ticks == r.Ticks)).ToList();
            // (the how-manieth group on its beam)
            int nth = Enumerable.Range(0, k).Count(j => groups[j].Any(i => evs[i].Beam != 0 && Key(evs[i]) == key));
            foreach (var e in mine.Concat(alike))
            {
                e.Ticks = e.Ticks * 2 / 3;
                e.Scale = (2, 3);
                // (a number of its own: the beam's piece, its stems' side, and the group's place on the beam)
                e.Tuplet = -(2 * key.Item1 + (key.Item2 > 0 ? 1 : 0)) - 1_000_000 * nth;
                changed.Add(e);
            }
        }
        return changed;
    }

    /// <summary>
    /// A sixteenth rest an old print draws with its hooks run together, read as an eighth's (the
    /// detector too, 0.66-0.85 an eighth to 0.16-0.51 a sixteenth: kinkel19-1's right hand, "r16 a,16
    /// c16 f16" twice a bar). Each such rest stands just before a beam of three sixteenths, the beat's
    /// rest, and makes the bar a sixteenth too long -- which UnprintedTriplets would put right by
    /// reading the three as a triplet. When reading every such rest as a sixteenth makes every voice
    /// fill the bar, it is one (before the triplets: a rest and three sixteenths is the plainer music).
    /// </summary>
    static void RestsBeforeSixteenths(List<Ev> evs, List<Ev> single, int barTicks)
    {
        if (barTicks <= 0 || evs.Count < 4 || Fills(evs, barTicks)) return;
        int eighth = Dur.Of(8, 0), sixteenth = Dur.Of(16, 0);
        var rests = new List<Ev>();
        foreach (var r in evs.Where(e => e.IsRest && !e.FullBar && e.Beam == 0 && e.Scale == (1, 1) && e.Ticks == eighth))
        {
            // (the next three events, nothing between: one beam's sixteenths, stems one way, the beam's whole)
            var next = evs.Where(e => e.X > r.X).OrderBy(e => e.X).Take(3).ToList();
            if (next.Count < 3 || next.Any(e => e.IsRest || e.Beam == 0 || e.Beam != next[0].Beam || e.Stem != next[0].Stem || e.Scale != (1, 1) || e.Ticks != sixteenth)) continue;
            if (evs.Count(e => e.Beam == next[0].Beam && e.Stem == next[0].Stem) != 3) continue;
            rests.Add(r);
        }
        if (rests.Count == 0) return;
        var trial = evs.Select(e => rests.Contains(e) ? e.Copy() : e).ToList();
        foreach (var e in trial.Where(e => !evs.Contains(e))) e.Ticks = sixteenth;
        if (!Fills(trial, barTicks)) return;
        foreach (var r in rests.Concat(single.Where(e => e.IsRest && rests.Any(r => r.X == e.X && r.Ticks == e.Ticks)).ToList())) r.Ticks = sixteenth;
    }

    /// <summary>
    /// Music that runs in threes with no number printed shows it in more than one bar: a part's
    /// triplets read from a bar's length (<see cref="UnprintedTriplets"/>) stand where the page has
    /// two such bars of the part or more. A lone one is undone, its bar too long as read: another
    /// misreading (a scan's speck read as a rest, a grace note read as a note) made it too long by as
    /// much as a triplet's third (gen-scan-fill's slurs003, gen-grace's piano006). So are all of
    /// them on a page where no bar fills the time as read: the time is what is misread.
    /// </summary>
    static void LoneTriplets(PageResult res, PageSymbols sym, ReaderState state)
    {
        // (only the bars as stored: a measure built for a test and thrown away is no bar)
        var kept = sym.Unprinted.Where(u => res.Bars.TryGetValue(u.Key, out var stored) && u.Value.Evs.Any(e => stored.Contains(e) || res.Voices[u.Key].Any(v => v.Contains(e)))).ToList();
        // (and only on a page that shows its meter: some staff's bar of notes that fills the time
        // as read with no triplet read into it. A page none of whose bars does has its time misread,
        // and in 9/8 taken for 4/4 every bar is an eighth too long, as much as a triplet's third,
        // under beams of three eighths: gen-b3-rnd's basic010)
        var unprinted = kept.Select(u => u.Key).ToHashSet();
        bool shown = res.Voices.Any(kv => !unprinted.Contains(kv.Key) && kv.Value.Any(v => v.Any(e => !e.IsRest))
                                          && kv.Value.All(v => v.Sum(e => e.Ticks) == Dur.OfTime(state.BarTime[kv.Key.Bar])));
        foreach (var part in kept.GroupBy(u => u.Key.Part).Where(g => g.Count() == 1 || !shown))
            foreach (var ((b, p), (st, evs)) in part)
            {
                // (a lone bar two or more of whose groups read as triplets runs in threes: one misreading
                // lengthens a bar by a third of a triplet, not by two -- kinkel19-6's left hand, the page's
                // last bar the first of a figure that runs on over the next page)
                if (shown && evs.Select(e => e.Tuplet).Distinct().Count() >= 2) continue;
                // (or the same bar of another staff runs in threes too: the right hand's three groups over
                // the left hand's one, kinkel19-6's bar 8)
                if (shown && kept.Any(u => u.Key.Bar == b && u.Key.Part != p)) continue;
                foreach (var e in evs) { e.Ticks = e.Ticks * 3 / 2; e.Scale = (1, 1); e.Tuplet = 0; }
                res.Voices[(b, p)] = Voices.Separate(state.Played[(b, p)].Evs, Dur.OfTime(state.BarTime[b]), st.Mid, st.S);
                res.Notes.Add($"page {res.Page}: bar {b} part {p}: {(shown ? "a lone bar of triplets with no number" : "triplets with no number on a page no bar of which fills the time")}, left as read");
            }
    }

    sealed record GraceMark(Head Head);

    /// <summary>
    /// The events on one staff between x0 and x1, in order: heads on one stem (or stemless at one
    /// x) as a chord in Evs, every head its own event in Single. A lone whole rest fills the bar
    /// (barTicks; left as a whole when barTicks is 0, for a bar whose length is still in question).
    /// </summary>
    /// <param name="clefs">the clefs inside the measure, left to right (Sig.Mid): each item past one is read by it, and the first such item carries it (Ev.ClefBefore)</param>
    /// <param name="octaveClef">the measure opens under an octave clef (treble_8, bass_8): its notes are Ev.OctaveClef, until a clef inside it (only treble_8 is one there: the .lys writes a bass_8 inside the music as bass)</param>
    static (List<Ev> Evs, List<Ev> Single) StaffEvents(PageSymbols sym, Staff st, double x0, double x1, double s, int mid, int[] keyAlt, int barTicks, List<Sig> clefs = null, bool octaveClef = false)
    {
        var evs = new List<Ev>();
        var single = new List<Ev>();
        var accs = new Dictionary<int, int>();
        var items = new List<(double X, object O)>();
        foreach (var h in sym.Heads) if (h.St == st && h.X > x0 && h.X < x1) items.Add((h.X, h));
        foreach (var r in sym.Rests) if (r.St == st && r.X > x0 && r.X < x1) items.Add((r.X, r));
        // (a grace note as a marker: its pitch kept for the next note's head, within four spaces of it)
        foreach (var g in sym.Graces) if (g.St == st && g.X > x0 && g.X < x1) items.Add((g.X, new GraceMark(g)));
        foreach (var b in sym.BeatRepeats) if (b.St == st && (b.L + b.R) / 2 > x0 && (b.L + b.R) / 2 < x1) items.Add(((b.L + b.R) / 2, b));
        items.Sort((a, c) => a.X.CompareTo(c.X));
        // the beat repeats' copies, mark by mark, and the group the marks in a row play again
        var repeats = new List<(List<Ev> Evs, List<Ev> Single, List<Ev> Of, int Strokes, BeatRepeat Mark)>();
        List<Ev> repeated = null;
        var graces = new List<(double X, Pitch P, Head H)>();
        int ci = 0;
        string clefBefore = null;
        foreach (var (x, o) in items)
        {
            while (clefs != null && ci < clefs.Count && clefs[ci].X < x) { mid = Clefs.MiddleLine(clefs[ci].Clef); clefBefore = clefs[ci].Clef; octaveClef = clefBefore == "treble_8"; ci++; }
            if (o is BeatRepeat br)
            {
                br.Played = null;
                // the beam group before it (a mark straight after another plays the same group): its
                // notes again, spread over the mark, beamed as a group of their own
                // (the group: the beamed notes of the last one's stem back to the mark before or an
                // unbeamed event -- not its beam's piece of ink: a scan breaks a beam into several)
                if (repeated == null)
                {
                    var last = evs.LastOrDefault(e => !e.IsRest && e.Beam > 0);
                    if (last != null)
                    {
                        double from = repeats.Count > 0 ? repeats[^1].Evs[^1].X : double.MinValue;
                        repeated = new List<Ev>();
                        foreach (var e in Enumerable.Reverse(evs))
                        {
                            if (e.X <= from || e.Beam < 0) break;
                            if (e.Stem != last.Stem) continue;
                            if (e.IsRest || e.Beam == 0) break;
                            repeated.Insert(0, e);
                        }
                    }
                }
                bool dbgB = Symbols.DebugAt.HasValue && Math.Abs(x - Symbols.DebugAt.Value.X) < 3 * s && Math.Abs(st.Mid - Symbols.DebugAt.Value.Y) < 6 * s;
                if (dbgB) Console.WriteLine($"beat repeat at {br.L:F0}-{br.R:F0}, {br.Strokes} strokes: {(repeated == null ? "no beam group before it" : string.Join(" ", repeated.Select(e => e.Show())))}");
                if (repeated == null || repeated.Count == 0 || repeated.Any(e => e.Scale != (1, 1))) continue;
                double gl = repeated[0].X, gr = repeated[^1].X;
                var copies = new List<Ev>();
                var singles = new List<Ev>();
                int beam = -1000 - repeats.Count;
                for (int k = 0; k < repeated.Count; k++)
                {
                    var e = repeated[k];
                    double cx = br.L + (br.R - br.L) * (k + 0.5) / repeated.Count;
                    Ev Twin(Ev f)
                    {
                        var c = f.Copy();
                        c.StemX = cx + (f.StemX - f.X); c.X = cx; c.Beam = beam; c.Tie = false; c.SlurStart = c.SlurEnd = false; c.Alts = null; c.Graces = null; c.GraceKind = null;
                        c.ClefBefore = null; c.Tremolo = 0; c.TremoloFirst = false;
                        return c;
                    }
                    copies.Add(Twin(e));
                    foreach (var f in single.Where(f => !f.IsRest && f.Stem == e.Stem && Math.Abs(f.X - e.X) < 0.3 * s && (e.Stem == 0 || Math.Abs(f.StemX - e.StemX) < 0.3 * s))) singles.Add(Twin(f));
                }
                repeats.Add((copies, singles, repeated, br.Strokes, br));
                evs.AddRange(copies);
                single.AddRange(singles);
                continue;
            }
            repeated = null;
            if (o is GraceMark gm)
            {
                // (its pitch as the key and the bar's signs before it have it, or its own sign, which
                // then holds in the bar as a note's does)
                int gd = mid + gm.Head.Pos;
                if (gm.Head.Acc.HasValue) accs[gd] = gm.Head.Acc.Value;
                graces.Add((x, new Pitch(gd, accs.TryGetValue(gd, out var ga) ? ga : keyAlt[Pitch.Mod7(gd)]), gm.Head));
                if (Symbols.DebugAt.HasValue && Math.Abs(x - Symbols.DebugAt.Value.X) < 2 * s && Math.Abs(gm.Head.Y - Symbols.DebugAt.Value.Y) < 3 * s)
                    Console.WriteLine($"grace mark at {x:F0},{gm.Head.Y:F0}: {graces[^1].P.Lys()}, {(gm.Head.HasStem ? $"stem {(gm.Head.Up ? "up" : "down")} {gm.Head.Beams} beams" : "no stem")}, {(gm.Head.Slashed ? "slashed" : "no slash")}");
                continue;
            }
            if (o is Head h)
            {
                // accidentals hold to the bar's end on the staff, whichever voice wrote them
                int dia = mid + h.Pos;
                int held = accs.TryGetValue(dia, out var a0) ? a0 : keyAlt[Pitch.Mod7(dia)];   // with no sign of its own
                bool bare = !h.Acc.HasValue && !accs.ContainsKey(dia);
                if (h.Acc.HasValue) accs[dia] = h.Acc.Value;
                int alt = accs.TryGetValue(dia, out var a) ? a : keyAlt[Pitch.Mod7(dia)];
                var pitch = new Pitch(dia, alt);
                // the other readings of the head: its pitch with another sign, a dot more or less
                var others = h.Alts?.Select(x => new EvAlt
                {
                    What = x.What, From = pitch, To = x.SetAcc ? new Pitch(dia, x.Acc ?? held) : pitch, DotsBy = x.DotsBy, Score = x.Score,
                    L = h.L - (x.SetAcc ? 2.5 * s : 0), T = h.Y - s, R = h.R + (x.DotsBy != 0 ? 1.5 * s : 0), B = h.Y + s,
                }).Where(x => x.To != x.From || x.DotsBy != 0).ToList();
                int basis = h.Hollow ? (h.HasStem || h.TremoloStem ? 2 : 1) : 4 << h.Beams;
                var ev = new Ev
                {
                    // (a two-note tremolo's two notes alternate for the time of one: each plays half its written value)
                    P = pitch, Ticks = Dur.Of(basis, h.Dots) * h.Scale.Num / h.Scale.Den / (h.Tremolo > 0 ? 2 : 1), X = x, Scale = h.Scale, Tuplet = h.Tuplet,
                    Tremolo = h.Tremolo, TremoloFirst = h.TremoloFirst,
                    Beam = h.HasStem && h.Beams > 0 && h.Cluster != 0 ? h.Cluster : 0,
                    Stem = h.HasStem ? (h.Up ? 1 : -1) : 0, StemX = h.StemX, Y = h.Y, Tie = h.Tie,
                    StemLo = h.HasStem ? Math.Min(h.Y, h.Tip) : h.Y, StemHi = h.HasStem ? Math.Max(h.Y, h.Tip) : h.Y,
                    SlurStart = h.SlurStart, SlurEnd = h.SlurEnd, Slash = h.Slash, Alts = others?.Count > 0 ? others : null,
                    Bare = bare && !h.Slash ? new List<int> { dia } : null, OctaveClef = octaveClef,
                };
                // heads on one stem (or stemless heads at one x) make a chord (a slash is no chord's)
                // (and not two values, beamed or flagged both, on stems one past the other's end in one column: a
                // stem ending in its beams over a head whose own stem, flagged, starts under them -- lang36-3's
                // scan, the bass's ees,,8 hung under the other voice's sixteenths, read as their chord. A
                // chord's stem seen short of its far head, its beams missed there, stays one: lang36-3's
                // <c,, c,>16 read with a quarter's head)
                bool Along(Ev e) => ev.Y >= e.StemLo - 0.5 * s && ev.Y <= e.StemHi + 0.5 * s || e.Y >= ev.StemLo - 0.5 * s && e.Y <= ev.StemHi + 0.5 * s;
                bool Apart(Ev e) => e.StemHi > e.StemLo && e.Ticks != ev.Ticks && e.Ticks < Dur.Whole / 4 && ev.Ticks < Dur.Whole / 4 && !Along(e);
                var mate = evs.LastOrDefault(e => !e.IsRest && !e.Slash && !ev.Slash && e.Stem == ev.Stem &&
                    (ev.Stem != 0 ? Math.Abs(e.StemX - ev.StemX) < 0.3 * s && !Apart(e) : Math.Abs(e.X - x) < 0.35 * s));
                // (a clef just before it is written before it, or before its chord)
                if (clefBefore != null) { if (mate != null) mate.ClefBefore ??= clefBefore; else ev.ClefBefore = clefBefore; clefBefore = null; }
                if (Symbols.DebugAt.HasValue && Math.Abs(x - Symbols.DebugAt.Value.X) < 2 * s && Math.Abs(h.Y - Symbols.DebugAt.Value.Y) < 3 * s)
                    Console.WriteLine($"event from the head at {x:F0},{h.Y:F0}: staff {st.System + 1}/{st.Part} pos {h.Pos}, middle line {mid} -> {pitch.Lys()}, {h.Beams} beams, {ev.Ticks} ticks{(mate != null ? $", on the chord at x {mate.X:F0}" : "")}");
                single.Add(ev.Copy());
                if (mate != null)
                {
                    mate.Tie |= ev.Tie;
                    mate.StemLo = Math.Min(mate.StemLo, ev.StemLo); mate.StemHi = Math.Max(mate.StemHi, ev.StemHi);
                    mate.SlurStart |= ev.SlurStart;
                    mate.SlurEnd |= ev.SlurEnd;
                    // (a new list: the first head's single copy shares the old one; a chord's dot is one)
                    if (ev.Alts != null)
                        mate.Alts = (mate.Alts ?? new()).Concat(ev.Alts.Where(x => x.To != x.From || !(mate.Alts ?? new()).Any(y => y.DotsBy == x.DotsBy))).ToList();
                    if (ev.Bare != null) mate.Bare = (mate.Bare ?? new()).Concat(ev.Bare).ToList();
                    mate.Chord ??= new List<Pitch> { mate.P };
                    mate.Chord.Add(pitch);
                    mate.Chord.Sort((p1, p2) => p1.Diatonic.CompareTo(p2.Diatonic));
                    mate.P = mate.Chord[^1];
                    continue;
                }
                // the grace notes just before it are its (a group of them a few spaces wide, the last within
                // eight spaces of it: a chord's stacked signs stand between them and the heads). Where two
                // voices' grace notes stand together (their stems up in the one, down in the other), the
                // note takes those with its stem's direction, and the others wait for the other voice's
                // note at its x (Lily# sets a lone voice's grace stems up whichever way the note's goes)
                var mine = graces;
                if (ev.Stem != 0 && graces.Any(g => g.H.HasStem && g.H.Up) && graces.Any(g => g.H.HasStem && !g.H.Up))
                    mine = graces.Where(g => !g.H.HasStem || g.H.Up == (ev.Stem > 0)).ToList();
                if (Symbols.DebugAt.HasValue && graces.Count > 0 && Math.Abs(x - Symbols.DebugAt.Value.X) < 10 * s && Math.Abs(h.Y - Symbols.DebugAt.Value.Y) < 6 * s)
                    Console.WriteLine($"head at {x:F0},{h.Y:F0} (stem {ev.Stem}): {graces.Count} grace notes before it, {mine.Count} with its stem's direction, the last {(x - (mine.Count > 0 ? mine[^1].X : graces[^1].X)) / s:F1} spaces left");
                if (mine.Count > 0 && x - mine[^1].X < 8 * s)
                {
                    var rest = graces.Except(mine).ToList();
                    graces = mine;
                    ev.Graces = graces.Select(g => g.P).ToList();
                    // how they are written (S21): a slash makes an acciaccatura, else one is an appoggiatura
                    // and several a grace group; the value from the flags or beams at the stem (one an eighth,
                    // two a sixteenth). With no stem read (the rules alone), one a slashed eighth and several
                    // sixteenths, as most music prints them
                    bool seen = graces.Any(g => g.H.HasStem && g.H.Beams > 0);
                    // (with the detector, a slash it and the ink did not see is none: the plain eighth, an appoggiatura)
                    ev.GraceKind = graces.Any(g => g.H.Slashed) ? "acciaccatura" : graces.Count > 1 ? "grace" : seen || sym.Detections != null ? "appoggiatura" : "acciaccatura";
                    ev.GraceValue = seen ? 4 << Math.Max(1, graces.Max(g => g.H.Beams)) : graces.Count == 1 ? 8 : 16;
                    graces = rest;
                }
                // (the other voice's grace notes wait for its note at this x; past it they are nobody's)
                else if (graces.Count > 0 && items.Any(it => it.O is Head o && o != h && Math.Abs(it.X - x) < 0.5 * s && it.X > x)) { }
                else graces.Clear();
                evs.Add(ev);
            }
            else if (o is Rest r)
            {
                // (a rest just past the grace notes with a head after it within their reach is the
                // other voice's, standing between them and their note: piano007's bars 3 and 7)
                bool theirs = graces.Count > 0 && items.Any(it => it.O is Head o && it.X > x && it.X - graces[^1].X < 8 * s);
                if (Symbols.DebugAt.HasValue && graces.Count > 0 && Math.Abs(x - Symbols.DebugAt.Value.X) < 10 * s && Math.Abs(r.Y - Symbols.DebugAt.Value.Y) < 6 * s)
                    Console.WriteLine($"rest at {x:F0},{r.Y:F0}: {graces.Count} grace notes before it {(theirs ? "wait for the head past it" : "are nobody's")}");
                if (!theirs) graces.Clear();
                int basis = r.Kind switch { RestKind.Whole => 1, RestKind.Half => 2, RestKind.Quarter => 4, RestKind.Eighth => 8, _ => 16 };
                var ev = new Ev { IsRest = true, Ticks = Dur.Of(basis, r.Dots) * r.Scale.Num / r.Scale.Den, X = x, Scale = r.Scale, Tuplet = r.Tuplet, Y = r.Y, ClefBefore = clefBefore };
                clefBefore = null;
                evs.Add(ev);
                single.Add(ev.Copy());
            }
        }
        // the beat repeats stand where the bar has room for them: the voice of the group they play
        // (its stem's) at most the bar's length, the last marks dropped till it is (a slash that was
        // something else, a group misread longer than printed). The strokes are the group's beams: a
        // bar that fills its time with the groups' notes at the strokes' value (a scan's 32nds read as
        // 16ths, a beam unseen) is read so
        if (barTicks > 0 && repeats.Count > 0)
        {
            int Voice() => evs.Where(e => e.Stem == repeats[0].Evs[0].Stem || e.IsRest && e.Stem == 0).Sum(e => e.Ticks);
            if (Voice() != barTicks)
            {
                var was = new Dictionary<Ev, int>();
                foreach (var (cs, ss, of, n, _) in repeats)
                {
                    int value = Dur.Of(4 << n, 0);
                    var heads = of.SelectMany(e => single.Where(f => !f.IsRest && f.Stem == e.Stem && Math.Abs(f.X - e.X) < 0.3 * s));
                    foreach (var e in of.Concat(cs).Concat(ss).Concat(heads))
                        if (e.Ticks > value && e.Scale == (1, 1) && !was.ContainsKey(e)) { was[e] = e.Ticks; e.Ticks = value; }
                }
                if (Voice() != barTicks) foreach (var (e, t) in was) e.Ticks = t;
            }
            while (repeats.Count > 0 && Voice() > barTicks)
            {
                evs.RemoveAll(repeats[^1].Evs.Contains);
                single.RemoveAll(repeats[^1].Single.Contains);
                repeats.RemoveAt(repeats.Count - 1);
            }
        }
        foreach (var (_, _, of, _, mark) in repeats) mark.Played = (of[0].X, of[^1].X);
        // a two-note tremolo stands when its two notes are in the bar and, at their written values, overrun
        // it where at half they do not (the voice of their stems, or the staff's events when a stem is
        // missing): strokes seen between two halves that fill the bar as written are something else's
        // (an old print's slur, a speck), and so are a pair the barline parts
        if (barTicks > 0)
        {
            var trem = evs.Where(e => e.Tremolo > 0).ToList();
            for (int k = 0; k < trem.Count; k++)
            {
                var (a, b) = (trem[k], k + 1 < trem.Count ? trem[k + 1] : null);
                bool pair = a.TremoloFirst && b != null && !b.TremoloFirst;
                bool keep = false;
                if (pair)
                {
                    var voice = a.Stem != 0 && a.Stem == b.Stem ? evs.Where(e => e.Stem == a.Stem).ToList() : evs;
                    int now = voice.Sum(e => e.Ticks);
                    keep = now <= barTicks && now + a.Ticks + b.Ticks > barTicks;
                }
                foreach (var e in pair ? new[] { a, b } : new[] { a })
                {
                    if (keep) continue;
                    foreach (var c in single.Where(c => c.Tremolo > 0 && Math.Abs(c.X - e.X) < 0.5 * s && c.Stem == e.Stem)) { c.Ticks *= 2; c.Tremolo = 0; c.TremoloFirst = false; }
                    e.Ticks *= 2; e.Tremolo = 0; e.TremoloFirst = false;
                }
                if (pair) k++;
            }
        }
        // a lone whole rest fills the bar
        if (barTicks > 0)
            foreach (var list in new[] { evs, single })
                if (list.Count == 1 && list[0].IsRest && list[0].Ticks == Dur.Whole)
                {
                    list[0].FullBar = true;
                    list[0].Ticks = barTicks;
                }
        return (evs, single);
    }

    /// <summary>
    /// The time signatures read on the system, checked against what the measures after each hold.
    /// The rules' shape reading and the learnt detector both err on a digit now and then (a scan's
    /// 9 read as a 3 at 0.96; a "12" whose 2 went unseen read as 1; the rules' 8/8 for 9/8), and
    /// a wrong bar length throws every barline test after it. The measures know: nine eighths in
    /// each, under a 3/8 sign, make it a 9/8 sign. The upper number is corrected to the length
    /// most staff-measures fill exactly, voice by voice, when that is more than half of them and
    /// more than fill the length read. The lower number cannot be told this way (6/8 and 3/4
    /// measure alike) and stands as read. A sign after its measure's last note (a courtesy sign
    /// for the next system) is left alone. A sign whose upper number went unseen ("?/4") takes
    /// the length the measures fill, or is dropped when they fill none.
    /// </summary>
    static void CheckTimes(PageResult res, int page, SystemInfo sys, PageSymbols sym, double s, ReaderState state)
    {
        var sigs = sym.Sigs.Where(g => g.Time != null && sys.Staves.Contains(g.St)).OrderBy(g => g.X).ToList();
        if (sys.Measures < 1) { sym.Sigs.RemoveAll(g => sigs.Contains(g) && g.Time.StartsWith('?')); return; }
        // no time signature read anywhere yet (an old print's figures, unknown to the classifier
        // and the detector: kinkel19-2's 3/4): the measures tell. The length most of the system's
        // staff-measures fill exactly, in quarters, is the time when they are half of them and more
        // (4/4 is what stands without a sign; the lower number cannot be told, 6/8 filling as 3/4)
        if (state.Time == null && sigs.Count == 0 && sys.Measures >= 2)
        {
            var filling = new List<List<Ev>>();
            for (int m = 0; m < sys.Measures; m++)
                foreach (var st in sys.Staves)
                {
                    var (evs, _) = StaffEvents(sym, st, sys.Bars[m], sys.Bars[m + 1], s, Clefs.MiddleLine("treble"), new int[7], 0);
                    if (evs.Count == 0 || (evs.Count == 1 && evs[0].IsRest && evs[0].Ticks == Dur.Whole)) continue;
                    filling.Add(evs);
                }
            int quarter = Dur.Whole / 4, best = 4;
            int Fill(int k) => filling.Count(evs => Fills(evs, k * quarter));
            for (int k = 1; k <= 12; k++) if (Fill(k) > Fill(best)) best = k;
            if (filling.Count >= 2 && 2 * Fill(best) >= filling.Count && best != 4)
            {
                res.Notes.Add($"page {page} system {sys.Index + 1}: no time signature read; {Fill(best)} of {filling.Count} staff-measures fill {best}/4: {best}/4 taken");
                sym.Sigs.Add(new Sig { St = sys.Staves[0], X = sys.Bars[0] + 0.1 * s, Time = $"{best}/4", Guessed = true });
            }
            return;
        }
        string inForce = state.Time;     // the time in force ahead of the sign
        // (an upper number the detector did not see is "?": 0, for the measures to tell)
        static (int Num, int Den)? Parse(string time)
        {
            var p = time.Split('/');
            int n = 0;
            return p.Length == 2 && (p[0] == "?" || int.TryParse(p[0], out n) && n > 0) && int.TryParse(p[1], out int d) && d > 0 ? (n, d) : null;
        }
        for (int i = 0; i < sigs.Count;)
        {
            // the signs standing at one place (one per staff), and the measures from there to the next place
            int j = i + 1;
            while (j < sigs.Count && sigs[j].X - sigs[i].X < 3 * s) j++;
            var group = sigs.GetRange(i, j - i);
            double x = group[0].X;
            int m0 = MeasureAt(sys, x, s), m1 = j < sigs.Count ? MeasureAt(sys, sigs[j].X, s) : sys.Measures;
            i = j;
            bool courtesy = !sym.Heads.Any(h => sys.Staves.Contains(h.St) && h.X > x && h.X < sys.Bars[m0 + 1])
                         && !sym.Rests.Any(r => sys.Staves.Contains(r.St) && r.X > x && r.X < sys.Bars[m0 + 1]);
            if (courtesy) continue;
            // the lower number most of the signs read (the staves may differ: one read by the rules, one by the detector)
            var readings = group.Select(g => Parse(g.Time)).Where(p => p.HasValue).Select(p => p.Value).ToList();
            int den = readings.Count == 0 ? 0 : readings.GroupBy(r => r.Den).OrderByDescending(grp => grp.Count()).ThenBy(grp => grp.Key).First().Key;
            string was = inForce;
            inForce = group[0].Time;
            if (den < 1 || den > 16 || (den & (den - 1)) != 0) continue;
            int unit = Dur.Whole / den;
            // every staff's events in every measure of the stretch (a lone whole rest fits any length: left out)
            var contents = new List<List<Ev>>();
            for (int m = m0; m < m1; m++)
                foreach (var st in sys.Staves)
                {
                    var (evs, _) = StaffEvents(sym, st, sys.Bars[m], sys.Bars[m + 1], s, Clefs.MiddleLine("treble"), new int[7], 0);
                    if (evs.Count == 0 || (evs.Count == 1 && evs[0].IsRest && evs[0].Ticks == Dur.Whole)) continue;
                    contents.Add(evs);
                }
            if (contents.Count < 2) continue;
            int Filled(int ticks) => contents.Count(evs => Fills(evs, ticks));
            // the upper number the most staff-measures fill; a tie goes to the one most of the signs read
            int best = readings.Where(r => r.Den == den).GroupBy(r => r.Num).OrderByDescending(grp => grp.Count()).First().Key;
            for (int k = 1; k <= 16; k++) if (Filled(k * unit) > Filled(best * unit)) best = k;
            if (Signatures.Verbose)
                Console.WriteLine($"    time {string.Join(" ", group.Select(g => g.Time).Distinct())} at x {x:F0}, system {sys.Index + 1}: of {contents.Count} staff-measures {Filled(best * unit)} fill {best}/{den}"
                                  + string.Concat(readings.Select(r => (r.Num, r.Den)).Distinct().Select(r => $", {Filled(Dur.Whole * r.Num / r.Den)} fill {r.Num}/{r.Den}")));
            string time = $"{best}/{den}";
            if (2 * Filled(best * unit) < contents.Count)
            {
                // no length over this lower number fits: when the time in force still fills the
                // measures, the sign is none (a rest and a stray mark read as a pair of numbers).
                // So too when the sign stands on under half the system's staves (a change of time is
                // printed on every staff), fills no measure, and the time in force fills some (Fantasy
                // p7's 4/1 on one staff of seven, where the time in force filled 6 of 14)
                bool lone = 2 * group.Select(g => g.St).Distinct().Count() < sys.Staves.Count
                            && readings.All(r => Filled(Dur.Whole * r.Num / r.Den) == 0);
                if (was != null && was != group[0].Time && Parse(was) is { } w
                    && (2 * Filled(Dur.Whole * w.Num / w.Den) >= contents.Count || lone && Filled(Dur.Whole * w.Num / w.Den) > 0))
                {
                    res.Notes.Add($"page {page} system {sys.Index + 1}: time {group[0].Time} read at x {x:F0}, but the measures still fill {was}: not a time signature");
                    foreach (var g in group) sym.Sigs.Remove(g);
                    inForce = was;
                }
                continue;
            }
            foreach (var g in group)
            {
                if (g.Time == time) continue;
                var r = Parse(g.Time);
                if (r.HasValue && Filled(Dur.Whole * r.Value.Num / r.Value.Den) >= Filled(best * unit)) continue;
                res.Notes.Add($"page {page} system {sys.Index + 1}: time {g.Time} read on staff {g.St.Part}, but {Filled(best * unit)} of {contents.Count} staff-measures fill {time}: {time} taken");
                g.Time = time;
            }
            inForce = time;
        }
        // a sign whose upper number the measures did not tell is none
        foreach (var g in sigs.Where(g => g.Time.StartsWith('?')))
        {
            res.Notes.Add($"page {page} system {sys.Index + 1}: time {g.Time} at x {g.X:F0}: the measures tell no upper number, dropped");
            sym.Sigs.Remove(g);
        }
    }

    /// <summary>The measure of the system a sign at x stands in (from half a space before its barline).</summary>
    static int MeasureAt(SystemInfo sys, double x, double s)
    {
        int m = 0;
        while (m + 1 < sys.Measures && sys.Bars[m + 1] - 0.5 * s <= x) m++;
        return m;
    }

    /// <summary>
    /// Percent repeats (B3): a staff's measure with no heads in it and the learnt detector's
    /// percent sign on the staff repeats what was played before. A one-bar sign (percent_1) stands
    /// in the measure and repeats the bar before; a two-bar sign (percent_2) stands on the barline
    /// between the two measures it fills, and each of them repeats the bar two before it. What
    /// the rules read in the measure (a sign's dots as rests) goes. The repeated events are moved
    /// into this measure's span, keeping their places in it.
    /// </summary>
    static void Repeat(PageResult res, int page, SystemInfo sys, PageSymbols sym, int m, int b, Measure ms, double s, ReaderState state)
    {
        if (sym.Detections == null) return;
        double x0 = sys.Bars[m], x1 = sys.Bars[m + 1];
        // (a sign seen as both kinds at one place is the kind seen more surely: detector24 sees Time
        // After Time's two-bar signs at 0.89 and as one-bar signs beside them at 0.59, and the one-bar
        // reading, tested first, repeated the bar before)
        var signs = sym.Detections.Where(d => d.Class is "percent_1" or "percent_2"
            && !sym.Detections.Any(o => o.Class is "percent_1" or "percent_2" && o.Class != d.Class && o.Score > d.Score
                                        && Math.Abs(o.X - d.X) < 1.5 * s && Math.Abs(o.Y - d.Y) < 1.0 * s)).ToList();
        foreach (var st in sys.Staves)
        {
            int p = st.Part;
            if (ms.Evs[p].Any(e => !e.IsRest)) continue;
            // (a two-bar sign the detector is unsure of counts on the barline itself, on the staff's middle line)
            bool OnStaff(Detection d) => d.Score >= Symbols.TrustScore && Math.Abs(d.Y - st.Mid) < 2 * s
                                         || d.Class == "percent_2" && d.Score > Detector.WeakPercent && Math.Abs(d.Y - st.Mid) < 1.0 * s
                                            && (Math.Abs(d.X - x0) < 1.0 * s || Math.Abs(d.X - x1) < 1.0 * s);
            int back = 0;
            // (a two-bar sign across a system break stands at the second system's start, past its
            // clef and key: the first measure there, and the last one of the system before)
            var next = m + 1 == sys.Measures ? res.Systems.FirstOrDefault(x => x.Index == sys.Index + 1) : null;
            bool Opening(Detection d, SystemInfo x, Staff on) => d.Class == "percent_2" && d.Score >= Symbols.TrustScore && Math.Abs(d.Y - on.Mid) < 2 * s
                                                                && d.X > x.Bars[0] && d.X < x.Bars[1];
            // (a sign the detector is unsure of counts where a one-bar sign stands: the measure's
            // middle third, on the staff's middle line -- Fantasy's drums' sign at 0.35)
            bool Centred(Detection d) => d.Score > Detector.WeakPercent && Math.Abs(d.Y - st.Mid) < 1.0 * s
                                         && Math.Abs(d.X - (x0 + x1) / 2) < (x1 - x0) / 6;
            // (MuseScore sets a two-bar sign across a system break on the first system's last barline:
            // the first measure of the next system, empty, is the sign's second)
            bool atEnd = m + 1 == sys.Measures && signs.Any(d => d.Class == "percent_2" && OnStaff(d) && Math.Abs(d.X - x1) < 1.5 * s);
            // (a four-bar sign -- Time After Time's "////" with a 4 over it -- stands on the middle
            // barline of the four measures it fills: two either side. The 4 over the staff on that
            // barline tells it from a two-bar sign; its strokes are seen as percent signs or slashes.
            // The 4 may be over another staff of the system: the signs on one barline are of a kind --
            // Time After Time p6's left hand's 4, which the detector misses, the guitar's it sees)
            bool Strokes(Staff on, double xb) => sym.Detections.Any(d => (d.Class.StartsWith("percent_") || d.Class.StartsWith("slash_")) && d.Score > Detector.WeakPercent
                                                                         && Math.Abs(d.X - xb) < 2.5 * s && Math.Abs(d.Y - on.Mid) < 1.5 * s);
            bool Four(double xb) => Strokes(st, xb) && sys.Staves.Any(o => Strokes(o, xb)
                && sym.Detections.Any(d => d.Class is "tab_4" or "digit_4" && d.Score >= Symbols.TrustScore && Math.Abs(d.X - xb) < 1.0 * s
                                           && d.Y < o.Mid - 2.3 * s && d.Y > o.Mid - 6 * s));
            if (Enumerable.Range(m - 1, 4).Any(j => j >= 1 && j < sys.Measures && Four(sys.Bars[j]))) back = 4;
            else if (signs.Any(d => d.Class == "percent_1" && (OnStaff(d) && d.X > x0 && d.X < x1 || Centred(d)))) back = 1;
            else if (atEnd || signs.Any(d => d.Class == "percent_2" && OnStaff(d) && (Math.Abs(d.X - x0) < 1.5 * s && m > 0 || Math.Abs(d.X - x1) < 1.5 * s && m + 1 < sys.Measures)
                                             || m == 0 && Opening(d, sys, st)
                                             || next != null && next.Measures > 0 && next.Staves.FirstOrDefault(x => x.Part == p) is { } on && Opening(d, next, on))) back = 2;
            else if (m == 0 && state.TwoBarCarried.Contains((b, p))) back = 2;
            if (back == 0)
            {
                // a band score leaves a part's tacet measures blank, no rest written (揺れる想い):
                // nothing read and no ink on the staff is a measure's rest
                if (ms.Evs[p].Count == 0 && Blank(sym, st, x0, x1, s))
                {
                    Ev Rest() => new() { IsRest = true, FullBar = true, Blank = true, Ticks = ms.BarTicks, X = (x0 + x1) / 2, Y = st.Mid };
                    ms.Evs[p] = new() { Rest() };
                    ms.Single[p] = new() { Rest() };
                }
                continue;
            }
            if (atEnd && back == 2) state.TwoBarCarried.Add((b + 1, p));
            if (!state.Played.TryGetValue((b - back, p), out var src))
            {
                res.Notes.Add($"page {page} system {sys.Index + 1}: bar {b} part {p} repeats bar {b - back}, which was not read");
                continue;
            }
            double k = (x1 - x0) / Math.Max(1, src.X1 - src.X0);
            List<Ev> Moved(List<Ev> evs) => evs.Select(e =>
            {
                var c = e.Copy();
                c.X = x0 + (e.X - src.X0) * k;
                if (c.StemX != 0) c.StemX = x0 + (e.StemX - src.X0) * k;
                return c;
            }).ToList();
            ms.Evs[p] = Moved(src.Evs);
            ms.Single[p] = Moved(src.Single);
            res.Notes.Add($"page {page} system {sys.Index + 1}: bar {b} part {p} repeats bar {b - back} (percent sign)");
        }
    }

    /// <summary>
    /// No ink on the staff between the barlines (clear of them by half a space) and past the
    /// signatures at the measure's start, from a space over its top line to a space under its
    /// bottom one. The staff lines are taken out of the ink, but not all of a bowed page's: a run
    /// of ink along a row two spaces long is a staff line's, a column across the staff a barline's
    /// (a double barline's thin line inside the measure).
    /// </summary>
    static bool Blank(PageSymbols sym, Staff st, double x0, double x1, double s)
    {
        int w = sym.LabelW, h = sym.Labels.Length / w, n = 0;
        int y0 = Math.Max(0, (int)(st.Top - s)), y1 = Math.Min(h - 1, (int)(st.Bottom + s));
        // (a key's X is about its first sign's left: each sign about a space wide)
        double from = sym.Sigs.Where(g => g.St == st && g.X > x0 - 0.5 * s && g.X < x1 && !g.Mid)
            .Select(g => g.X + (g.Fifths is int f ? 1.1 * Math.Abs(f) * s + 1.0 * s : g.Clef != null ? 3.0 * s : 2.0 * s))
            .Append(x0).Max() + 0.5 * s;
        int xa = Math.Max(0, (int)from), xb = Math.Min(w, (int)(x1 - 0.5 * s));
        if (xb <= xa) return false;
        var cols = new int[xb - xa];
        for (int y = y0; y <= y1; y++)
            for (int x = xa; x < xb; x++)
                if (sym.Labels[y * w + x] != 0) cols[x - xa]++;
        for (int y = y0; y <= y1; y++)
            for (int x = xa; x < xb;)
            {
                if (sym.Labels[y * w + x] == 0) { x++; continue; }
                int run = x;
                while (run < xb && sym.Labels[y * w + run] != 0) run++;
                if (run - x < 2 * s)
                    for (; x < run; x++) if (cols[x - xa] < 0.9 * (st.Bottom - st.Top)) n++;
                x = run;
            }
        return n < 0.5 * s * s;
    }

    /// <summary>Records the measure as bar b: what was in force, and its events per part.</summary>
    static void Store(PageResult res, ReaderState state, SystemInfo sys, int b, Measure ms, int m)
    {
        state.BarTime[b] = ms.Time;
        for (int p = 0; p < ms.Evs.Length; p++)
        {
            // a part whose staff the system hides rests the measure
            var st = sys.Staves.FirstOrDefault(x => x.Part == p);
            if (st == null)
            {
                Ev Rest() => new() { IsRest = true, FullBar = true, Blank = true, Ticks = ms.BarTicks, X = (sys.Bars[m] + sys.Bars[m + 1]) / 2 };
                ms.Evs[p] = new() { Rest() };
                ms.Single[p] = new() { Rest() };
            }
            else if (state.Played.TryGetValue((b - 1, p), out var before))
            {
                var keyAlt = Keys.Alterations(ms.Fifths[p]);
                CarryTies(before.Evs, ms.Evs[p], keyAlt);
                CarryTies(before.Single, ms.Single[p], keyAlt);
            }
            state.Played[(b, p)] = (ms.Evs[p], ms.Single[p], sys.Bars[m], sys.Bars[m + 1]);
            state.BarKey[(b, p)] = ms.Fifths[p];
            state.BarClef[(b, p)] = ms.Clefs[p];
            res.Bars[(b, p)] = state.Poly ? ms.Single[p] : ms.Evs[p];
            res.Voices[(b, p)] = Voices.Separate(ms.Evs[p], ms.BarTicks, st?.Mid ?? 0, st?.S ?? 0);
        }
    }

    /// <summary>
    /// A note tied over the barline keeps the alteration of the note it is tied from: its sign is
    /// not written again (ties007 bar 16: the a natural tied from bar 15, not the key's a flat).
    /// Only the bar's first note at that step, and only when it shows no sign of its own.
    /// </summary>
    static void CarryTies(List<Ev> prev, List<Ev> cur, int[] keyAlt)
    {
        static List<Pitch> Pitches(Ev e) => e.Chord ?? new List<Pitch> { e.P };
        // the steps held into this bar: a tied event's, with no event after it at that step
        var held = new Dictionary<int, int>();
        for (int i = 0; i < prev.Count; i++)
        {
            var e = prev[i];
            if (e.IsRest || e.Slash || !e.Tie) continue;
            foreach (var p in Pitches(e))
                if (!prev.Skip(i + 1).Any(x => !x.IsRest && !x.Slash && Pitches(x).Any(q => q.Diatonic == p.Diatonic)))
                    held[p.Diatonic] = p.Alter;
        }
        foreach (var e in cur)
        {
            if (held.Count == 0) return;
            if (e.IsRest || e.Slash) continue;
            var ps = Pitches(e);
            for (int k = 0; k < ps.Count; k++)
            {
                int dia = ps[k].Diatonic;
                if (!held.Remove(dia, out int alt)) continue;
                if (e.Bare?.Contains(dia) != true || ps[k].Alter != keyAlt[Pitch.Mod7(dia)] || alt == ps[k].Alter) continue;
                var old = ps[k];
                var now = new Pitch(dia, alt);
                if (e.Chord != null) { e.Chord[k] = now; e.P = e.Chord[^1]; }
                else e.P = now;
                if (e.Alts != null)
                    foreach (var x in e.Alts)
                    {
                        if (x.From == old) x.From = now;
                        if (x.To == old) x.To = now;
                    }
            }
        }
    }

    /// <summary>
    /// A barline never stands where a note's stem is: a "barline" at a stem's x is that stem.
    /// (The stems are found from the heads, without regard to the barlines.)
    /// </summary>
    /// (In a system of four staves or more, within 1.3 spaces of a stem or a head: notes stand a
    /// space or more clear of a true barline, and a column of them down a band score's system was
    /// taken for one a space off its stems -- 揺れる想い p6 and p8. Not in a small system, where
    /// the bar length of a poor reading can fill by chance: the colour scan voices2006 read by the
    /// rules alone lost a true barline so.)
    static bool IsStem(SystemInfo sys, PageSymbols sym, double x, double s) =>
        sys.Staves.Count >= 4
            ? sym.Heads.Any(h => sys.Staves.Contains(h.St) && (h.HasStem && Math.Abs(h.StemX - x) < 1.3 * s || Math.Abs(h.X - x) < 1.3 * s))
            : sym.Heads.Any(h => h.HasStem && sys.Staves.Contains(h.St) && Math.Abs(h.StemX - x) < 0.3 * s);

    /// <summary>
    /// Two neighbouring measures are one measure cut by a false barline when, on every staff
    /// with anything in it, their events together fill the time exactly (voice by voice), and
    /// on some staff they do not fill it apart. A measure with a note missed does not qualify:
    /// its neighbour is whole, and the two together overflow. In a system of many staves (a band
    /// score's eleven) one staff read wrong is the rule, not the exception: there two thirds of
    /// the staves with something on them suffice, so long as no staff fills both measures apart
    /// (揺れる想い p8, the stems of the second beat's off-beat in line down the system).
    /// </summary>
    static bool IsSplitMeasure(Measure a, Measure b)
    {
        if (a.BarTicks != b.BarTicks) return false;
        int ticks = a.BarTicks;
        int staves = 0, joins = 0, entered = 0;
        bool broken = false;
        // (a blank staff's rest, read from no ink, counts as nothing: as it was before it was read)
        static List<Ev> Read(List<Ev> evs) => evs.Count == 1 && evs[0].Blank ? new() : evs;
        // (a voice that enters after the break: one voice fills the bar, the other only its second part --
        // kinkel19-1's right hand, sixteenths on through the bar split over two systems and "c'4 bes8. a16"
        // over the second half. One such staff, the others joined exactly)
        bool Entered(List<Ev> joined) => Voices.Separate(joined, ticks, 0) is { Count: > 1 } vs
                                         && vs.Count(v => v.Sum(e => e.Ticks) == ticks) == 1 && vs.All(v => v.Sum(e => e.Ticks) <= ticks);
        for (int p = 0; p < a.Evs.Length; p++)
        {
            var ea = Read(a.Evs[p]);
            var eb = Read(b.Evs[p]);
            if (ea.Count == 0 && eb.Count == 0) continue;
            staves++;
            bool fa = Fills(ea, ticks), fb = Fills(eb, ticks);
            // two measures each whole on some staff: a barline
            if (fa && fb) return false;
            var joined = ea.Concat(eb).ToList();
            // a lone whole rest on one side is the whole measure; joined with nothing it stays so
            if (!Fills(joined, ticks)) { if (ea.Count > 0 && eb.Count > 0 && Entered(joined)) entered++; continue; }
            joins++;
            broken = true;
        }
        if (staves == 0 || !broken) return false;
        return staves <= 3 ? joins == staves || entered == 1 && joins == staves - 1 : 3 * joins >= 2 * staves;
    }

    /// <summary>
    /// Whether the system ends with no closing barline: no column within a space of a staff's right
    /// end is dark down the staff. (An old print runs a bar on to the next system; a staff's lines
    /// alone darken a column a fifth of the way.)
    /// </summary>
    static bool EndsOpen(Img d, SystemInfo sys, double s)
    {
        foreach (var st in sys.Staves)
        {
            int top = (int)Math.Round(st.Top), bottom = (int)Math.Round(st.Bottom);
            for (int x = Math.Max(0, (int)(st.Right - 1.0 * s)); x <= Math.Min(d.W - 1, (int)(st.Right + 0.5 * s)); x++)
            {
                int dark = 0;
                for (int y = Math.Max(0, top); y <= Math.Min(d.H - 1, bottom); y++) if (d[x, y]) dark++;
                if (dark >= 0.9 * (bottom - top + 1)) return false;
            }
        }
        return true;
    }

    /// <summary>A measure with a staff that has something on it and does not fill the time.</summary>
    static bool Unfilled(Measure m) => m.Evs.Any(evs => evs.Count > 0 && !Fills(evs, m.BarTicks));

    /// <summary>A measure with a staff that holds less than its time.</summary>
    static bool Short(Measure m) => m.Evs.Any(evs => evs.Count > 0 && evs.Sum(e => e.Ticks) < m.BarTicks);

    /// <summary>
    /// How many bars a measure holds past its time: two measures merged by a missed barline hold
    /// one too many on every staff. The least over the staves with something on them, since a
    /// staff of two voices holds two bars' worth of a measure read right.
    /// </summary>
    static double Overflow(Measure m)
    {
        var totals = m.Evs.Where(evs => evs.Count > 0 && !(evs.Count == 1 && evs[0].Blank)).Select(evs => evs.Sum(e => e.Ticks)).ToList();
        if (totals.Count == 0 || m.BarTicks <= 0) return 0;
        return Math.Max(0, totals.Min() - m.BarTicks) / (double)m.BarTicks;
    }

    /// <summary>Whether the events make a measure of exactly the given length, voice by voice.</summary>
    static bool Fills(List<Ev> evs, int barTicks)
    {
        if (evs.Count == 0) return false;
        return Voices.Separate(evs, barTicks, 0).All(v => v.Sum(e => e.Ticks) == barTicks);
    }
}
