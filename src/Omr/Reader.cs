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
    /// <summary>The parts whose music ran in threes on an earlier page (TripletsInThrees): a page turn does not stop it.</summary>
    public HashSet<int> InThrees = new();

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
        if (Img.LastLean != null) Console.WriteLine($"  page {page}: {Img.LastLean}, stood up");
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
        // (a head seen at two sizes, as a black head and as a grace note's, each under the threshold: one head)
        if (det != null) { Detector.JoinSizes(det, s); det.RemoveAll(Detector.IsWeakHollowGrace); }
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
        Symbols.DebugPageNow = page;
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
                             && both.Any(p => Fills(tail.Evs[p].Concat(head.Evs[p]).ToList(), tail.BarTicks))
                             && !HandsOver(tail) && !HandsOver(head);
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
            void Weigh(Measure ms) { doubt |= Unfilled(ms) && !HandsOver(ms) && !(splitHead && weighed == 0); weighed++; over = Short(ms) ? null : over + Overflow(ms); }
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
        TripletsInThrees(res, state);
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
        }
        CrossBeams(ms.Evs, ms.Single, s, ms.BarTicks);
        HandedGroups(ms.Evs, ms.Single, s, ms.BarTicks);
        for (int p = 0; p < n; p++)
        {
            var st = sys.Staves.FirstOrDefault(x => x.Part == p);
            if (st == null) continue;
            // (a measure built again, a barline taken out, forgets what the first build read)
            sym.Unprinted.Remove((b, p));
            RestsBeforeSixteenths(ms.Evs[p], ms.Single[p], ms.BarTicks);
            if (UnprintedTriplets(ms.Evs[p], ms.Single[p], ms.BarTicks, time, st: st) is { } changed) sym.Unprinted[(b, p)] = (st, changed);
            else MixedThrees(ms.Evs[p], ms.Single[p], ms.BarTicks, time);
        }
        return ms;
    }

    /// <summary>
    /// A beam group drawn on two staves: a left hand's broken chord climbing to the right hand's
    /// staff in mid group, one beam over both. Its notes are read staff by
    /// staff, each by its own staff's clef and signs, which is right for their pitches and wrong for
    /// the part: each hand is short of the bar. A group of one ink piece with notes on two
    /// neighbouring parts' staves, stems the same way and tips along one beam, is the part's whose
    /// staff its first note stands on; the others are written there with @cross. So are the
    /// number's scale and group, read over the notes under it alone.
    /// </summary>
    /// <param name="barTicks">the bar's length (0: not known yet, the time being checked)</param>
    static void CrossBeams(List<Ev>[] parts, List<Ev>[] singles, double s, int barTicks = 0)
    {
        for (int p = 0; p + 1 < parts.Length; p++)
        {
            int q = p + 1;
            if (parts[p] == null || parts[q] == null) continue;
            // (both staves' bars full as read: nothing is on the wrong one, whatever the ink joins)
            if (barTicks > 0 && Fills(parts[p], barTicks) && Fills(parts[q], barTicks)) continue;
            var shared = parts[p].Where(e => e.Beam != 0).Select(e => e.Beam).Intersect(parts[q].Where(e => e.Beam != 0).Select(e => e.Beam))
                                 .OrderBy(beam => parts[p].Concat(parts[q]).Where(e => e.Beam == beam).Min(e => e.X)).ToList();
            // (the groups on the staves' shared beams that fill the bar together are one line drawn on the two staves by
            // turns: Widmung's left hand, bes8 des'8 under the staff and fes'8. g'16 over it, g'8 fes'8 over and des'8 bes8
            // under, g8 bes8 and fes'8 g'8. Its home is its first note's staff, whichever staff a later group opens on)
            int chainHome = -1;
            if (barTicks > 0 && shared.Count > 1)
            {
                var chain = parts[p].Concat(parts[q]).Where(e => shared.Contains(e.Beam) && !e.IsRest).ToList();
                if (chain.Sum(e => e.Ticks) == barTicks) chainHome = LineHome(parts, p, q, chain, barTicks, shared);
            }
            // (whether a group ran in threes: ThreesBesideKnee once the bar's groups have all moved -- weighed after the
            // first alone, the line is still short of its later groups' notes on the other staff, and "does not fill")
            foreach (int beam in shared)
            {
                if (KneeBeam(parts, singles, p, q, beam, s, barTicks, chainHome, shared)) continue;
                // (stems one way: the ink may hold another voice's note, its stem the other way against this one's head)
                foreach (int side in new[] { 1, -1 })
                {
                    var upper = parts[p].Where(e => e.Beam == beam && e.Stem == side).ToList();
                    var lower = parts[q].Where(e => e.Beam == beam && e.Stem == side).ToList();
                    var all = upper.Concat(lower).OrderBy(e => e.X).ToList();
                    if (upper.Count == 0 || lower.Count == 0) continue;
                    // (a group climbing from the lower staff only, stems up or, the beam under it, down. A first note on the upper staff is as a
                    // rule a chord there whose stem touches the lower staff's beam: lang36-3's MuseScore page, "<bes f'>4"
                    // over the left hand's sixteenths, taken for their first. A group falling to the lower staff with every
                    // stem one way has nothing to test it on yet: a kneed one is KneeBeam's, a detached one HandedGroups')
                    if (upper.Contains(all[0])) continue;
                    // (and single notes climb: a chord on the upper staff is that hand's, its stem on the beam's ink --
                    // lang36-3's bars 54-55, "<ees' g'>16" among the left hand's sixteenths)
                    if (upper.Any(e => e.Chord != null)) continue;
                    double Tip(Ev e) => side > 0 ? e.StemLo : e.StemHi;
                    double slope = (Tip(all[^1]) - Tip(all[0])) / Math.Max(1, all[^1].X - all[0].X);
                    double Line(double x) => Tip(all[0]) + slope * (x - all[0].X);
                    bool Between(Ev e) => !e.IsRest && e.Beam == beam && e.Stem == -side && e.X > all[0].X - s && e.X < all[^1].X + s;
                    // (at least three: a beam's two notes on two staves say nothing of whose they are)
                    if (all.Count < 3) continue;
                    // (written value alike -- a number over some of them has scaled those; one in three may differ: the long
                    // stem up from the far staff under a sloping beam counted three beams where the others have two,
                    // and takes theirs -- and the tips along one line: not a stem of another voice that touches the ink)
                    static int Written(Ev e) => e.Ticks * e.Scale.Den / e.Scale.Num;
                    int value = all.GroupBy(Written).OrderByDescending(g => g.Count()).First().Key;
                    if (3 * all.Count(e => Written(e) != value) > all.Count) continue;
                    // (two spaces: a stem read to the inner of two beams only, a sixteenth taken for an eighth)
                    if (all.Any(e => Math.Abs(Line(e.X) - Tip(e)) > 2 * s)) continue;
                    // (a head of the group read twice, as two voices' shared head: the melody's stem down beside it,
                    // taken for a second stem of its own. threes011's fis under "cis''4")
                    bool Twin(Ev e) => Between(e) && all.Any(o => Math.Abs(o.X - e.X) < 0.3 * s && o.P == e.P);
                    // (and a note of another voice whose stem only ends on the beam has no beam of its own: the melody's
                    // quarter, its stem down onto the group's beam, read as a sixteenth. threes010's "c''4")
                    bool Touching(Ev e) => Between(e) && Math.Abs((side > 0 ? e.StemHi : e.StemLo) - Line(e.X)) < 1.2 * s;
                    // (with no such twin, a head on the far side of the beam with its stem read the other way is the
                    // group's own: threes010's a under the melody's stem, the sixteenth's "a8" left on the staff it is drawn on)
                    bool Stray(Ev e) => Between(e) && (side > 0 ? e.Y > Line(e.X) + s : e.Y < Line(e.X) - s);
                    int home = upper.Contains(all[0]) ? p : q, away = home == p ? q : p;
                    // (on the staff the group climbs to only: on its own staff a note the other way is a voice of its
                    // own, the bass under the group or sharing a head with its first note)
                    foreach (var list in new[] { parts[away], singles[away] })
                    {
                        list.RemoveAll(Twin);
                        // (the steep end of the climbing beam, seen by the detector as a rhythm slash)
                        list.RemoveAll(e => e.Slash && e.X > all[0].X - s && e.X < all[^1].X + s);
                        foreach (var e in list.ToList())
                            if (Stray(e)) e.Stem = side;
                            else if (Touching(e)) { e.Beam = 0; e.Ticks = Dur.Of(4, 0) * e.Scale.Num / e.Scale.Den; }
                    }
                    MoveHome(parts, home, away, beam, side, value);
                    MoveHome(singles, home, away, beam, side, value);
                    if (all.Count % 3 == 0)
                    {
                        ThreesBesideKnee(parts, p, q, barTicks);
                        ThreesBesideKnee(singles, p, q, barTicks);
                    }
                }
            }
        }
    }

    /// <summary>
    /// A kneed beam between two staves: the lower staff's notes with stems up to it, the upper's
    /// with stems down to it, the group the part's whose staff its first note stands on -- the left
    /// hand's climbing to the right hand's staff (Op.27-2's bars 23 and 30), or the right hand's falling
    /// to the left hand's (Schumann's Op.25-9, bars 3, 19 and 35: fis'8 d'8 over the staff, b8 fis8 under
    /// it, one beam between; Widmung's left hand, bar 10). A melody's stem down onto a climbing group's
    /// beam looks the same, and stands in a column the group already has a note in, or between two of
    /// its columns; the group's own heads make even columns with the lower staff's. The same tests keep
    /// a melody's note from opening a falling group (lang36-3's chord over the sixteenths is excepted as
    /// a chord); one note over two under the staff is a falling group as gen-fall draws it.
    /// </summary>
    /// <param name="chainHome">the home of a line the staves' shared groups make together (CrossBeams), or -1: the group's own (LineHome)</param>
    /// <param name="shared">the beams with notes on both staves (CrossBeams)</param>
    static bool KneeBeam(List<Ev>[] parts, List<Ev>[] singles, int p, int q, int beam, double s, int barTicks, int chainHome, ICollection<int> shared)
    {
        bool Low(Ev e) => e.Beam == beam && e.Stem > 0 && !e.IsRest;
        var lower = parts[q].Where(Low).ToList();        if (lower.Count == 0) return false;
        // (the upper staff's own notes with stems up on the ink: another voice's, its beam one piece with the group's through
        // a head both share -- lang36-3's bar 40, the right hand's f'8 <aes' c''>8 over the left hand's sixteenths, f'16
        // drawn in the chord's column. Only notes longer than every one under the lower staff, and only for the lower
        // staff's line: they keep a beam of their own)
        var foreign = parts[p].Where(e => e.Beam == beam && e.Stem > 0).ToList();
        if (foreign.Count > 0 && !foreign.All(e => !e.IsRest && Written(e) > lower.Max(Written))) return false;
        bool Head(Ev e) => e.Beam == beam && e.Stem < 0 && !e.IsRest && e.Chord == null && !lower.Any(l => Math.Abs(l.X - e.X) < 0.7 * s);
        var upper = parts[p].Where(Head).ToList();
        var all = lower.Concat(upper).OrderBy(e => e.X).ToList();
        if (upper.Count == 0 || all.Count < 3) return false;
        bool falling = upper.Contains(all[0]);
        // (falling: the lower staff's notes with stems down are not its)
        if (falling && parts[q].Any(e => e.Beam == beam && e.Stem < 0 && !e.IsRest)) return false;
        // (the tips along one line, a beam's thickness apart at most)
        static double Tip(Ev e) => e.Stem > 0 ? e.StemLo : e.StemHi;
        double slope = (Tip(all[^1]) - Tip(all[0])) / Math.Max(1, all[^1].X - all[0].X);
        if (all.Any(e => Math.Abs(Tip(all[0]) + slope * (e.X - all[0].X) - Tip(e)) > 1.2 * s)) return false;
        // (columns not much under the usual gap: a stem down and the next one up close in, to 0.41 of it in threes011's
        // --knee, but a melody's stem right beside one of the group's comes closer)
        var gaps = all.Zip(all.Skip(1), (a, b) => b.X - a.X).OrderBy(g => g).ToList();
        if (gaps[0] < 0.3 * gaps[gaps.Count / 2]) return false;
        static int Written(Ev e) => e.Ticks * e.Scale.Den / e.Scale.Num;
        int value = all.GroupBy(Written).OrderByDescending(g => g.Count()).First().Key;
        // (one written value, one in three allowed to differ (CrossBeams); or a dotted note and its complement among
        // notes of the value, kept as written: Widmung's bes8 des'8 fes'8. g'16 under one beam)
        bool dotted = all.Sum(Written) % value == 0 && all.All(e => Written(e) >= value / 2 && Written(e) <= value * 3 / 2) && all.Any(e => Written(e) != value);
        if (3 * all.Count(e => Written(e) != value) > all.Count && !dotted) return false;
        // (whose line: the line's the shared groups make together (chainHome) -- Widmung's g'8 fes'8 over the staff then
        // des'8 bes8 under it, the left hand's figure between two groups of its own -- or the group's own, LineHome)
        // (--at 0,0: every kneed beam's working on the page)
        bool debug = Symbols.DebugAt.HasValue && (Symbols.DebugPage == null || Symbols.DebugPage == Symbols.DebugPageNow)
                     && (Symbols.DebugAt.Value == (0, 0) || Symbols.DebugAt.Value.X >= all[0].X - 2 * s && Symbols.DebugAt.Value.X <= all[^1].X + 2 * s
                                                             && Math.Abs(Symbols.DebugAt.Value.Y - all.Average(e => e.Y)) < 12 * s);
        int home = chainHome >= 0 ? chainHome : LineHome(parts, p, q, all, barTicks, shared, debug);
        bool lowerChord = lower.Any(e => e.Chord != null);
        if (debug) Console.WriteLine($"kneed beam at x {all[0].X:F0}-{all[^1].X:F0}: {upper.Count} over the upper staff, {lower.Count} under the lower{(lowerChord ? " (a chord among them)" : "")}, {(falling ? "falling" : "climbing")}, {(dotted ? "a dotted pair, " : "")}line of the shared groups' home {chainHome}, home part {home}");
        // (a chord under the lower staff is that hand's own: the group is the lower staff's line, its notes over the upper
        // staff come down -- lang36-3's bars 40, 42 and 65, the left hand's c'16 aes16 f'16 <aes c'>16, c' and f' drawn over
        // the right hand's staff. Not the right hand's falling to a chord of the left hand's)
        if ((lowerChord || foreign.Count > 0) && home != q) return false;
        if (foreign.Count > 0)
        {
            int own = beam + 1_000_000;
            foreach (var list in new[] { parts[p], singles[p] })
                foreach (var e in list.Where(e => e.Beam == beam && e.Stem > 0)) e.Beam = own;
        }
        int away = home == p ? q : p, side = home == p ? -1 : 1;
        foreach (var list in new[] { parts[away], singles[away] })
        {
            // (the notes moved take the group's stems: MoveHome goes by them)
            if (home == p) foreach (var e in list.Where(Low).ToList()) e.Stem = -1;
            else
            {
                foreach (var e in list.Where(Head).ToList()) e.Stem = 1;
                // (what else the ink holds on that staff is a melody's, its stem ending on the beam -- when the group
                // climbs to it. A falling group opens there: what is left of it there is its own, a note in a column
                // with one under the staff, not a quarter of a melody's. Dichterliebe's tenth song, 2026-11-08)
                if (!falling) foreach (var e in list.Where(e => e.Beam == beam && e.Stem < 0)) { e.Beam = 0; e.Ticks = Dur.Of(4, 0) * e.Scale.Num / e.Scale.Den; }
            }
            list.RemoveAll(e => e.Slash && e.X > all[0].X - s && e.X < all[^1].X + s);
        }
        MoveHome(parts, home, away, beam, side, value, !dotted);
        MoveHome(singles, home, away, beam, side, value, !dotted);
        // (a group that itself runs in threes: BWV 610's tenor climbs four to a beam among plain sixteenths. A climbing
        // group only: a falling one's three are as often a beat's sixteenths after the other voice's rest (Dichterliebe's
        // tenth song, r16 over the staff's bes''16 d''16 d'16) as triplets, and where they are triplets the staff is too long
        // as plain notes, which UnprintedTriplets reads -- gen-fall's 99.2% with and without the threes read here)
        if (all.Count % 3 == 0 && !falling)
        {
            ThreesBesideKnee(parts, p, q, barTicks);
            ThreesBesideKnee(singles, p, q, barTicks);
        }
        return true;
    }

    /// <summary>
    /// A line's last notes drawn on the other hand's staff with a beam of their own, nothing joining them
    /// to it on the page but a slur (MuseScore's cross-staff notes: Schumann's Op.25-15, the right hand's
    /// eighths running down, the last two under the left hand's chord and rests, one slur from the first to
    /// the last). Each staff read by itself, the line is short of the bar by just the group, which stands
    /// where the line has nothing (past its last note, or before its first); on its own staff the group
    /// fills what the other voices leave, as the engraver's hidden rest there does. A sure slur end on its
    /// last note that no start on that staff pairs with (or such a start on its first) tells the two: the
    /// group is the line's, written there with @cross. Not while the time is being checked (no bar length).
    /// </summary>
    static void HandedGroups(List<Ev>[] parts, List<Ev>[] singles, double s, int barTicks)
    {
        if (barTicks <= 0) return;
        for (int p = 0; p + 1 < parts.Length; p++)
            foreach (var (home, away) in new[] { (p, p + 1), (p + 1, p) })
            {
                if (parts[home] == null || parts[away] == null || parts[home].Count == 0) continue;
                var voices = Voices.Separate(parts[home], barTicks, 0);
                if (voices.Any(v => v.Sum(e => e.Ticks) > barTicks)) continue;
                int left = barTicks - voices.Max(v => v.Sum(e => e.Ticks));
                if (left <= 0) continue;
                double first = parts[home].Min(e => e.X), last = parts[home].Max(e => e.X);
                foreach (var g in parts[away].Where(e => e.Beam != 0 && !e.IsRest).GroupBy(e => e.Beam).Select(g => g.OrderBy(e => e.X).ToList()).ToList())
                {
                    // (not a group moved here already: its staff, short now by just it, would take it back)
                    if (g.Count < 2 || g.Any(e => e.Chord != null || e.Stem != g[0].Stem || e.CrossStaff) || g.Sum(e => e.Ticks) != left) continue;
                    bool after = g[0].X > last + 0.5 * s, before = g[^1].X < first - 0.5 * s;
                    // (the slur's end on the last note, from the line before the group; or its start on the first, to the line after it)
                    if (!(after && g[^1].SlurEndAcross || before && g[0].SlurStartAcross)) continue;
                    // (nothing else of its staff among them: a chord of that hand's under the beam is that hand's own group)
                    if (parts[away].Any(e => !g.Contains(e) && e.X > g[0].X - 0.5 * s && e.X < g[^1].X + 0.5 * s)) continue;
                    int beam = g[0].Beam;
                    foreach (var arr in new[] { parts, singles })
                    {
                        var moved = arr[away].Where(e => e.Beam == beam && !e.IsRest).ToList();
                        arr[away].RemoveAll(moved.Contains);
                        foreach (var e in moved) e.DrawnOn = away;
                        arr[home] = arr[home].Concat(moved).OrderBy(e => e.X).ToList();
                    }
                    break;
                }
            }
    }

    /// <summary>
    /// The groups of a bar with a kneed beam: moved, each staff's beam may fill the bar as plain notes or fall short
    /// of it with nothing past it, and <see cref="UnprintedTriplets"/>, which goes by a bar too long, leaves them
    /// plain. Op.27-2's bar 23: the left hand's six under the knee and the right hand's six after it, "r4" and six
    /// eighths filling 2/2 by chance. Three, six, nine or twelve eighths or sixteenths under one beam there are triplets,
    /// unless that beam alone is the bar: six eighths in 3/4. Not while the time is being checked (no bar length yet).
    /// </summary>
    static void ThreesBesideKnee(List<Ev>[] parts, int p, int q, int barTicks)
    {
        if (barTicks <= 0) return;
        foreach (int r in new[] { p, q })
            foreach (var g in parts[r].Where(e => e.Beam != 0 && !e.IsRest && e.Scale == (1, 1)).GroupBy(e => (e.Beam, e.Stem)).ToList())
            {
                var run = g.OrderBy(e => e.X).ToList();
                int ticks = run[0].Ticks;
                if (run.Count % 3 != 0 || run.Any(e => e.Ticks != ticks) || ticks != Dur.Of(8, 0) && ticks != Dur.Of(16, 0) || run.Count * ticks == barTicks) continue;
                foreach (var (three, k) in run.Chunk(3).Select((c, k) => (c, k)))
                    foreach (var e in three)
                    {
                        e.Ticks = ticks * 2 / 3;
                        e.Scale = (2, 3);
                        e.Tuplet = -(2 * g.Key.Beam + (g.Key.Stem > 0 ? 1 : 0)) - 1_000_000 * k;
                    }
            }
    }

    /// <summary>
    /// Whose line a group drawn on two staves is, or a line of such groups: the part's whose staff its first note
    /// stands on (Op.25-9's right hand, short of the bar by the group, as the left hand under it is too), unless
    /// that staff's own voices fill the bar without the line and the other staff's do not: the line is the other
    /// hand's (lang36-3's bar 40, the left hand's c'16 drawn on the right hand's staff by its ledger line, the right
    /// hand full with g'4 f'8 <aes' c''>8). Both full, it is a voice beside theirs, the hand's whose staff more of
    /// its notes are drawn on (that bar's six under the staff to two over it), the first note's when as many
    /// (Widmung's six and six). While the time is being checked (no bar length): the first note's.
    /// </summary>
    /// <param name="shared">the beams with notes on both staves: a staff's own is what stands on none of them, and what
    /// stands on one with its stem the staff's own way -- the right hand's f'8 beamed to its chord, stems up, in the ink
    /// piece of the left hand's beam under it (one head with the left hand's f'16 drawn there, its stem down to that
    /// beam: lang36-3's bar 40); not the f'16, a stray of a group the left hand's chord keeps whole</param>
    static int LineHome(List<Ev>[] parts, int p, int q, List<Ev> line, int barTicks, ICollection<int> shared, bool debug = false)
    {
        int first = parts[p].Contains(line.MinBy(e => e.X)) ? p : q, other = first == p ? q : p;
        if (barTicks <= 0) return first;
        bool Full(int r)
        {
            var rest = parts[r].Where(e => !line.Contains(e) && !(shared.Contains(e.Beam) && e.Stem == (r == p ? -1 : 1))).ToList();
            bool full = rest.Count > 0 && Voices.Separate(rest, barTicks, 0).All(v => v.Sum(e => e.Ticks) == barTicks);
            if (debug) Console.WriteLine($"  part {r} without the line: {string.Join(" ", rest.Select(e => e.Show()))} -> {(full ? "fills the bar" : "does not fill the bar")}");
            return full;
        }
        if (!Full(first)) return first;
        if (!Full(other)) return other;
        int onFirst = line.Count(e => parts[first].Contains(e));
        if (debug) Console.WriteLine($"  both full: {onFirst} of the line's {line.Count} notes on the first note's staff");
        return line.Count - onFirst > onFirst ? other : first;
    }

    /// <summary>The events of the beam group on the staff away go to the part home, marked; the group's tuplet is one for all of it.</summary>
    /// <param name="even">a note of the group read shorter than the value (a beam more counted on its long stem) takes the value;
    /// one read longer keeps its own, an eighth ending a run of sixteenths (BWV 610's bar 13). False: as written, a dotted pair among them</param>
    static void MoveHome(List<Ev>[] parts, int home, int away, int beam, int side, int value, bool even = true)
    {
        bool Mine(Ev e) => e.Beam == beam && e.Stem == side;
        var moved = parts[away].Where(Mine).ToList();
        parts[away].RemoveAll(Mine);
        foreach (var e in moved) e.DrawnOn = away;
        parts[home].AddRange(moved);
        parts[home] = parts[home].OrderBy(e => e.X).ToList();
        var group = parts[home].Where(Mine).ToList();
        if (even) foreach (var e in group.Where(e => e.Ticks * e.Scale.Den / e.Scale.Num < value)) e.Ticks = value * e.Scale.Num / e.Scale.Den;
        // (the number stands over the notes on one staff: each three of the beam a group of its own, six under one piece
        // of beam two groups, a group the number was not read over being a triplet all the same)
        if (group.Count % 3 == 0 && group.FirstOrDefault(e => e.Scale != (1, 1)) is { } any)
        {
            if (Symbols.DebugAt == (0, 0)) Console.WriteLine($"moved home: the group at x {group[0].X:F0} has a tuplet's scale on {any.Show()}: all of it in threes");
            foreach (var (three, k) in group.Chunk(3).Select((c, k) => (c, k)))
            {
                var under = three.FirstOrDefault(e => e.Scale != (1, 1));
                int tuplet = under?.Tuplet ?? -(1000 * Math.Abs(beam) + k + 1);
                foreach (var e in three.Where(e => e.Scale == (1, 1)))
                {
                    e.Ticks = e.Ticks * any.Scale.Num / any.Scale.Den;
                    e.Scale = any.Scale;
                    e.Tuplet = tuplet;
                }
            }
        }
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
    /// <param name="inThrees">the part's music runs in threes on the page (<see cref="TripletsInThrees"/>): a pair
    /// alone under a beam is a group a note of which went unseen, and when no choice fills the bar, every group
    /// is a triplet where that leaves no voice past the bar and none short of it by a group or more</param>
    /// <returns>The events made triplets' (null: none).</returns>
    /// <param name="st">the staff, when known: a bar with a group drawn on two staves is laid in voices with its geometry to tell whether it is too long</param>
    static List<Ev> UnprintedTriplets(List<Ev> evs, List<Ev> single, int barTicks, string time, bool inThrees = false, Staff st = null)
    {
        // (in simple time: in 6/8 or 9/8 three beamed eighths are a beat of the meter's own)
        int den = int.Parse(time.Split('/')[1]), beat = Dur.Whole / den;
        if (NoUnprinted || den is not (2 or 4) || barTicks <= 0 || evs.Count < 3 || Fills(evs, barTicks)) return null;
        // (a group is a beam's notes with their stems one way: two voices' beams the stems of a
        // shared chord join are one piece of ink -- seed 11's piano002, bar 6)
        static (int, int) Key(Ev e) => (e.Beam, e.Stem);
        // the groups, each the indices of its events in evs (a rest among them has no beam of its own)
        var groups = new List<List<int>>();
        // (the groups of two under a beam and a rest, before a rest is given to one only)
        var restPairs = new List<List<int>>();
        // (inThrees: the groups that are a pair alone under a beam, a group with a note unseen or no group at all;
        // and the groups with no beam, triplets or a melody's notes)
        var pairsAlone = new List<int>();
        // (a bar too long as read, some voice past the bar: a beam of six split into triplets only then -- a bar a
        // voice short would fill by the voices run into one, kinkel19-2's 3/4 bar of "<des f>4" under six eighths)
        bool tooLong = Voices.Separate(evs.Select(e => e.Copy()).ToList(), barTicks, 0).Any(v => v.Sum(e => e.Ticks) > barTicks);
        // (so too a bar of one line, no two events in a column, longer than the bar: a voice's beams with
        // their stems each way, taken for two voices each short of the bar -- gen-threes' threes009, 3/4's
        // eighteen sixteenths, six to a beam, the third beam's stems down)
        bool oneLine = OneLine(evs);
        // (only in music that runs in threes: Bohemian's bar 31, 6/4 read as 4/4, its second violin's twelve
        // eighths on two beams, up and down, were read as four triplets to fill the wrong time)
        tooLong |= inThrees && oneLine && evs.Any(e => e.Stem > 0) && evs.Any(e => e.Stem < 0) && evs.Sum(e => e.Ticks) > barTicks;
        // (a staff with a group drawn on two staves, @cross, weighed with its geometry when known: two lines the stems
        // lump into one too long -- Dichterliebe's tenth song, "s16 bes''8." over "r16 bes''16 d''16 d'16", the d' on the
        // left hand's staff -- are laid apart by the voice search, and the bar is not too long. Only a layout that keeps
        // every beam in one voice counts: gen-knee's twelve eighths would lie in two voices with holes. Not where the
        // music is known to run in threes, nor while the time is checked (no staff given): the groups moved are triplets)
        if (tooLong && !inThrees && st != null && evs.Any(e => e.CrossStaff))
        {
            var laid = Voices.Separate(evs.Select(e => e.Copy()).ToList(), barTicks, st.Mid, st.S);
            if (laid.All(v => v.Sum(e => e.Ticks) <= barTicks) && !laid.SelectMany(v => v.Where(e => e.Beam != 0).Select(e => e.Beam).Distinct()).GroupBy(b => b).Any(g => g.Count() > 1)) tooLong = false;
        }
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
            if (before >= 0 && !Between(evs[before].X, xr, before)) restPairs.Add(new List<int> { before, idx[0], idx[1] });
            else if (after >= 0 && !Between(xl, evs[after].X, after)) restPairs.Add(new List<int> { idx[0], idx[1], after });
            else if (inThrees && tooLong) { pairsAlone.Add(groups.Count); groups.Add(idx); }
        }
        // (a rest two pairs would take is the one nearer its height's: the other a pair of another voice's
        // eighths over the triplets' rest -- gen-threes' threes008, "g''8 ees''8" over "r8 g'8 bes'8")
        foreach (var pair in restPairs)
        {
            int r = pair.Single(i => evs[i].IsRest);
            double Off(List<int> g) => Math.Abs(evs[r].Y - g.Where(i => !evs[i].IsRest).Average(i => evs[i].Y));
            if (!restPairs.Any(o => o != pair && o.Contains(r) && Off(o) < Off(pair))) groups.Add(pair);
        }
        // (and, in a bar too long as read, notes of one value with no beam, quarters or flagged eighths, in threes
        // along their side of the staff, a rest of their value among them: a triplet's bracket left off where the
        // music plainly runs in threes -- gen-threes' 2/2 pieces, "r4 b,4 e4" twice a bar. A rest goes with the
        // side whose note is nearest it in height; a run of a length not of threes is no group. In a bar of one line,
        // no two events in a column, one side: one voice's stems go each way with its notes' height, and the bar
        // too long as it is one line, not as two voices of the stems' sides)
        if (tooLong || oneLine && evs.Sum(e => e.Ticks) > barTicks)
        {
            bool Loose(Ev e) => e.Beam == 0 && !e.FullBar && e.Scale == (1, 1) && (e.Ticks == Dur.Of(4, 0) || e.Ticks == Dur.Of(8, 0) && (e.IsRest || e.Stem != 0));
            var loose = Enumerable.Range(0, evs.Count).Where(i => Loose(evs[i])).ToList();
            var notes = loose.Where(i => !evs[i].IsRest).ToList();
            // (a rest in a column with notes of one side only is the other side's, whatever its height: a group's
            // rest under a melody's quarter -- seed 11's threes035, "r4 e'4 cis''4" under "d''4 e''4")
            var noteXs = notes.Select(i => evs[i].X).OrderBy(x => x).ToList();
            var noteSteps = noteXs.Zip(noteXs.Skip(1), (a, b) => b - a).OrderBy(g => g).ToList();
            double column = noteSteps.Count == 0 ? 0 : 0.25 * noteSteps[noteSteps.Count / 2];
            int Side(int i)
            {
                if (oneLine) return 0;
                if (!evs[i].IsRest) return evs[i].Stem;
                if (notes.Count == 0) return 0;
                var by = notes.Where(j => Math.Abs(evs[j].X - evs[i].X) < column).Select(j => evs[j].Stem).Distinct().ToList();
                if (by.Count == 1 && by[0] != 0 && notes.Any(j => evs[j].Stem == -by[0])) return -by[0];
                return evs[notes.MinBy(j => Math.Abs(evs[j].Y - evs[i].Y))].Stem;
            }
            foreach (var side in notes.Select(Side).Distinct().ToList())
            {
                var mine = loose.Where(i => Side(i) == side).OrderBy(i => evs[i].X).ToList();
                // (a rest in a column with an event of the side is another voice's: a melody's "r2 e''2" over 2/2's
                // quarters in threes, its rest beside the group's first note or rest -- seed 11's threes029, threes035)
                var steps = mine.Zip(mine.Skip(1), (a, b) => evs[b].X - evs[a].X).OrderBy(g => g).ToList();
                double step = steps.Count == 0 ? 0 : steps[steps.Count / 2];
                bool Aside(Ev e) => e.IsRest && mine.Any(i => evs[i] != e && Math.Abs(evs[i].X - e.X) < 0.25 * step);
                var run = new List<int>();
                // (inThrees: such a group may as well be a melody's three quarters over the triplets, so it is one
                // the last resort below may leave out, as a pair alone -- Op.27-2's bar 30, "cis''4 e''4 cis''4")
                void Close()
                {
                    if (run.Count >= 3 && run.Count % 3 == 0 && run.Any(i => !evs[i].IsRest))
                        foreach (var c in run.Chunk(3))
                        {
                            if (inThrees) pairsAlone.Add(groups.Count);
                            groups.Add(c.ToList());
                        }
                    run.Clear();
                }
                foreach (int i in mine)
                {
                    // (one value, and nothing of their side between: no other note, no rest)
                    if (run.Count > 0 && (evs[i].Ticks != evs[run[^1]].Ticks
                                          || evs.Any(e => e.X > evs[run[^1]].X && e.X < evs[i].X && (oneLine || e.Stem == side || e.IsRest && !Aside(e)) && !mine.Contains(evs.IndexOf(e))))) Close();
                    run.Add(i);
                }
                Close();
            }
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
        (List<List<Ev>> Voices, Dictionary<Ev, int> Group) Scaled(int mask, bool line = false)
        {
            var trial = evs.Select(e => e.Copy()).ToList();
            var group = new Dictionary<Ev, int>();
            for (int i = 0; i < trial.Count; i++)
            {
                group[trial[i]] = groupOf[i];
                if (In(mask, groupOf[i])) trial[i].Ticks = trial[i].Ticks * 2 / 3;
            }
            if (line) return (new List<List<Ev>> { trial.OrderBy(e => e.X).ToList() }, group);
            return (Voices.Separate(trial, barTicks, 0), group);
        }
        // (a bar of one line may be one voice, its stems each way with its notes' height: four groups down and four
        // up taken for two voices, each half the bar -- seed 11's threes073, "r16 b'16 d''16" eight times in a C bar.
        // The voices as separated first: a melody's note on a group's head is one event of the line)
        bool Works(int mask) => WorksAs(mask, false) || oneLine && WorksAs(mask, true);
        bool WorksAs(int mask, bool line)
        {
            var (voices, group) = Scaled(mask, line);
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
        // (in music that runs in threes, a bar a note or two of which went unseen: every group, when no voice
        // runs past the bar so and none falls short of it by a group -- gen-threes-scan, a head lost to the
        // toner, its beam's two others read as eighths and the bar left as twelve)
        // (the voices the groups are in: another's note unseen is no matter of the triplets -- a melody's half note.
        // A pair alone may be a melody's two eighths: the most of them that do, all first)
        if (chosen < 0 && inThrees && pairsAlone.Count <= 4)
        {
            int pairBits = pairsAlone.Sum(k => 1 << k);
            foreach (int drop in Enumerable.Range(0, 1 << pairsAlone.Count).OrderBy(d => System.Numerics.BitOperations.PopCount((uint)d)))
            {
                int mask = all & ~pairsAlone.Where((k, i) => (drop >> i & 1) == 1).Sum(k => 1 << k);
                if ((mask & ~pairBits) == 0) break;
                // (a group's length, of the groups read so: not a melody's quarters left out)
                int most = 2 * Enumerable.Range(0, groups.Count).Where(k => In(mask, k)).Max(k => evs[groups[k][0]].Ticks);
                var (voices, group) = Scaled(mask);
                if (voices.Where(v => v.Any(e => In(mask, group[e]))).All(v => v.Sum(e => e.Ticks) <= barTicks && barTicks - v.Sum(e => e.Ticks) < most)) { chosen = mask; break; }
            }
        }
        if (chosen < 0) return null;
        if (Symbols.TupletVerbose) Console.WriteLine($"      -> {System.Numerics.BitOperations.PopCount((uint)chosen)} of the {groups.Count} groups triplets");
        var changed = new List<Ev>();
        // (a group's beam and side; a group with no beam, beam 0 and its notes' side)
        (int, int) GroupKey(int k) => groups[k].FirstOrDefault(i => evs[i].Beam != 0, -1) is int i and >= 0 ? Key(evs[i]) : (0, evs[groups[k].First(i => !evs[i].IsRest)].Stem);
        for (int k = 0; k < groups.Count; k++)
        {
            if ((chosen >> k & 1) == 0) continue;
            var key = GroupKey(k);
            // (the group's events, and their copies in single: a beamed note's by the beam and its place
            // on it -- a beam of six holds two groups -- a rest's by its place, a note with no beam's by the
            // nearest such note)
            var mine = groups[k].Select(i => evs[i]).ToList();
            // (a single copy is the group's when the nearest event on its beam is: a group may span two pieces of beam)
            var keys = mine.Where(e => !e.IsRest && e.Beam != 0).Select(Key).ToHashSet();
            bool Mine(Ev e) => mine.Contains(evs.Where(o => !o.IsRest && o.Beam != 0 && Key(o) == Key(e)).MinBy(o => Math.Abs(o.X - e.X)));
            bool MineLoose(Ev e) => mine.Contains(evs.Where(o => !o.IsRest && o.Beam == 0 && o.Stem == e.Stem).MinBy(o => Math.Abs(o.X - e.X)));
            var alike = single.Where(e => e.Beam != 0 ? keys.Contains(Key(e)) && Mine(e)
                                        : e.IsRest ? mine.Any(r => r.IsRest && e.X == r.X && e.Ticks == r.Ticks)
                                        : key.Item1 == 0 && MineLoose(e)).ToList();
            // (the how-manieth group on its beam)
            int nth = Enumerable.Range(0, k).Count(j => key.Item1 != 0 ? groups[j].Any(i => evs[i].Beam != 0 && Key(evs[i]) == key) : GroupKey(j) == key);
            foreach (var e in mine.Concat(alike))
            {
                e.Ticks = e.Ticks * 2 / 3;
                e.Scale = (2, 3);
                // (a number of its own: the beam's piece, its stems' side, and the group's place on the beam)
                e.Tuplet = -(2 * key.Item1 + (key.Item2 > 0 ? 1 : 0)) - 1_000_000 * nth;
                changed.Add(e);
            }
        }
        if (Symbols.DebugAt == (0, 0) && changed.Count > 0) Console.WriteLine($"unprinted triplets: the bar at x {evs.Min(e => e.X):F0}-{evs.Max(e => e.X):F0}, {(tooLong ? "too long" : "not too long")}{(inThrees ? ", in threes" : "")}: {changed.Count} notes read as triplets");
        return changed;
    }

    /// <summary>
    /// A triplet of unlike values with no number, beside a printed one in the bar: kinkel19-1's bar 8,
    /// "tuplet { f'8 e'8 d'8 } e'8 bes'8. e'16", the second group a pair on a beam and a flagged note.
    /// One line a third of a triplet too long, and only one run of notes on a beat that makes three
    /// eighths: no beam cut by it, and not a beam's or flags' equal notes (<see cref="UnprintedTriplets"/>'s).
    /// </summary>
    static void MixedThrees(List<Ev> evs, List<Ev> single, int barTicks, string time)
    {
        int den = int.Parse(time.Split('/')[1]), beat = Dur.Whole / den, third = Dur.Of(8, 0);
        if (NoUnprinted || den != 4 || barTicks <= 0 || evs.Count < 3 || !OneLine(evs)) return;
        if (evs.Sum(e => e.Ticks) != barTicks + third || evs.All(e => e.Scale == (1, 1))) return;
        var line = evs.OrderBy(e => e.X).ToList();
        var runs = new List<List<Ev>>();
        int at = 0;
        for (int i = 0; i < line.Count; at += line[i].Ticks, i++)
        {
            if (at % beat != 0) continue;
            var run = new List<Ev>();
            for (int j = i; j < line.Count && line[j].Scale == (1, 1) && !line[j].FullBar && run.Sum(e => e.Ticks) < 3 * third; j++) run.Add(line[j]);
            if (run.Count < 2 || run.Sum(e => e.Ticks) != 3 * third || run.All(e => e.IsRest)) continue;
            if (run.Any(e => e.Beam != 0 && line.Any(o => o.Beam == e.Beam && o.Stem == e.Stem && !run.Contains(o)))) continue;
            if (run.Select(e => e.Ticks).Distinct().Count() == 1 && run.Select(e => e.Beam).Distinct().Count() == 1) continue;
            runs.Add(run);
        }
        if (runs.Count != 1) return;
        var mine = runs[0];
        int id = -1_500_000_000 - (int)mine[0].X;
        foreach (var e in mine.Concat(single.Where(e => mine.Contains(line.MinBy(o => Math.Abs(o.X - e.X)))).ToList()))
        {
            e.Ticks = e.Ticks * 2 / 3;
            e.Scale = (2, 3);
            e.Tuplet = id;
        }
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
        // (or music in threes from end to end: two bars or more every beamed eighth and sixteenth of which
        // reads as a triplet, and fills the time so. A time misread leaves some of them plain, 9/8 read as
        // 4/4 one group of three in three -- gen-threes' one-staff pieces, every bar triplets under a melody)
        int eighth = Dur.Of(8, 0), sixteenth = Dur.Of(16, 0);
        shown |= kept.Count(u => res.Voices[u.Key].All(v => v.Sum(e => e.Ticks) == Dur.OfTime(state.BarTime[u.Key.Bar]))
                                 && !res.Voices[u.Key].Any(v => v.Any(e => !e.IsRest && e.Beam != 0 && e.Scale == (1, 1) && (e.Ticks == eighth || e.Ticks == sixteenth)))) >= 2;
        // (a part that ran in threes on an earlier page runs on in them: a page of two bars has one such bar
        // where the other's note went unseen -- gen-threes' threes010 in a scan)
        foreach (var part in kept.GroupBy(u => u.Key.Part).Where(g => (g.Count() == 1 || !shown) && !state.InThrees.Contains(g.Key)))
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

    /// <summary>
    /// A part whose music runs in threes on the page -- two bars or more that fill the time with every
    /// beamed eighth and sixteenth a triplet, the number printed or not -- reads its bars that still do not
    /// fill as triplets too, where a note went unseen and no choice of groups fills the bar exactly, or a
    /// voice's beams with their stems each way were taken for two voices
    /// (<see cref="UnprintedTriplets"/>'s inThrees). Beethoven's Op.27-2 is one in a scan: a bar of
    /// eleven eighths read as eleven eighths, the triplet with a head lost two plain ones.
    /// </summary>
    static void TripletsInThrees(PageResult res, ReaderState state)
    {
        if (NoUnprinted) return;
        int eighth = Dur.Of(8, 0), sixteenth = Dur.Of(16, 0);
        bool Threes(KeyValuePair<(int Bar, int Part), List<List<Ev>>> kv)
        {
            // (a triplet's notes with no beam too: 2/2's quarters three to a half. Only the voices with triplets in
            // them: a melody's two beamed eighths over them are no sign of a misread time -- threes010's bar 15)
            var beamed = kv.Value.Where(v => v.Any(e => e.Scale == (2, 3)))
                                 .SelectMany(v => v).Where(e => !e.IsRest && (e.Scale == (2, 3) || e.Beam != 0 && (e.Ticks == eighth || e.Ticks == sixteenth))).ToList();
            return beamed.Count >= 3 && beamed.All(e => e.Scale == (2, 3)) && kv.Value.All(v => v.Sum(e => e.Ticks) == Dur.OfTime(state.BarTime[kv.Key.Bar]));
        }
        // (or one, on a page after one where the part ran in threes: a page of two bars, the last a note unseen --
        // gen-threes' threes010 in a scan)
        var running = res.Voices.GroupBy(kv => kv.Key.Part).Where(g => g.Count(Threes) >= (state.InThrees.Contains(g.Key) ? 1 : 2)).ToList();
        state.InThrees.UnionWith(running.Select(g => g.Key));
        if (Symbols.TupletVerbose)
            foreach (var g in res.Voices.GroupBy(kv => kv.Key.Part)) Console.WriteLine($"  page {res.Page} part {g.Key}: {g.Count(Threes)} bars in threes{(state.InThrees.Contains(g.Key) ? ", running" : "")}");
        foreach (var part in running)
            foreach (var (b, p) in part.Where(kv => !Threes(kv)).Select(kv => kv.Key).ToList())
            {
                int barTicks = Dur.OfTime(state.BarTime[b]);
                // (a bar that does not fill: too long, or a voice's beams taken for two voices each short)
                if (!state.Played.TryGetValue((b, p), out var played) || res.Voices[(b, p)].All(v => v.Sum(e => e.Ticks) == barTicks)) continue;
                var sys = res.Systems.FirstOrDefault(x => x.Measures > 0 && b >= x.FirstBar && b < x.BarOf(x.Measures));
                var st = sys?.Staves.FirstOrDefault(x => x.Part == p);
                if (Symbols.TupletVerbose) Console.WriteLine($"  in threes? bar {b} part {p}");
                if (st == null || UnprintedTriplets(played.Evs, played.Single, barTicks, state.BarTime[b], inThrees: true) == null) continue;
                res.Voices[(b, p)] = Voices.Separate(played.Evs, barTicks, st.Mid, st.S);
                res.Notes.Add($"page {res.Page}: bar {b} part {p}: read in threes as the part's other bars, a note of a triplet unseen");
            }
        HandedThrees(res, state);
    }

    /// <summary>
    /// A line of triplets one hand starts and the other ends, in music that runs in threes, no number over it
    /// and nothing to join the two on the page (the groups do not cross): each hand is short of the bar, or
    /// long, as plain notes, and neither hand alone says more. As triplets the two make the bar together
    /// (<see cref="Voices.HandedOver"/>), and that decides. The bar's length, no threshold.
    /// </summary>
    static void HandedThrees(PageResult res, ReaderState state)
    {
        foreach (var b in res.Voices.Keys.Select(k => k.Bar).Distinct().ToList())
        {
            var sys = res.Systems.FirstOrDefault(x => x.Measures > 0 && b >= x.FirstBar && b < x.BarOf(x.Measures));
            if (sys == null || !state.BarTime.TryGetValue(b, out var time)) continue;
            int barTicks = Dur.OfTime(time);
            foreach (var p in res.Voices.Keys.Where(k => k.Bar == b).Select(k => k.Part).Order().ToList())
            {
                int q = p + 1;
                if (!state.InThrees.Contains(p) && !state.InThrees.Contains(q)) continue;
                if (!res.Voices.ContainsKey((b, q)) || !state.Played.TryGetValue((b, p), out var up) || !state.Played.TryGetValue((b, q), out var down)) continue;
                if (sys.Staves.FirstOrDefault(x => x.Part == p) is not { } sp || sys.Staves.FirstOrDefault(x => x.Part == q) is not { } sq) continue;
                if (res.Voices[(b, p)].Concat(res.Voices[(b, q)]).All(v => v.Sum(e => e.Ticks) == barTicks)) continue;
                var trial = new[] { up.Evs.Select(e => e.Copy()).ToList(), down.Evs.Select(e => e.Copy()).ToList() };
                ThreesBesideKnee(trial, 0, 1, barTicks);
                var voices = new List<List<List<Ev>>> { Voices.Separate(trial[0], barTicks, sp.Mid, sp.S), Voices.Separate(trial[1], barTicks, sq.Mid, sq.S) };
                var whole = Voices.HandedOver(voices, barTicks).Whole;
                if (whole.Count == 0 || !voices.SelectMany(vs => vs).All(v => v.Sum(e => e.Ticks) == barTicks || whole.Contains(v))) continue;
                var (evs, singles) = (new[] { up.Evs, down.Evs }, new[] { up.Single, down.Single });
                ThreesBesideKnee(evs, 0, 1, barTicks);
                ThreesBesideKnee(singles, 0, 1, barTicks);
                res.Voices[(b, p)] = Voices.Separate(up.Evs, barTicks, sp.Mid, sp.S);
                res.Voices[(b, q)] = Voices.Separate(down.Evs, barTicks, sq.Mid, sq.S);
                res.Notes.Add($"page {res.Page}: bar {b} parts {p} and {q}: triplets one hand starts and the other ends");
            }
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
    /// <param name="measuring">the measure read to tell its length (barTicks 0): a cue's small notes are its notes</param>
    static (List<Ev> Evs, List<Ev> Single) StaffEvents(PageSymbols sym, Staff st, double x0, double x1, double s, int mid, int[] keyAlt, int barTicks, List<Sig> clefs = null, bool octaveClef = false, bool measuring = false)
    {
        var evs = new List<Ev>();
        var single = new List<Ev>();
        var accs = new Dictionary<int, int>();
        var items = new List<(double X, object O)>();
        // A cue: another instrument's notes printed small over the part's own full-bar rest, a whole
        // rest in the bar's middle (a trumpet part's "Vln. 1" and "Ob. 1" bars). Half its heads and
        // more are small, under 1.1 spaces wide where a head is 1.25 and more (a hollow head is often
        // given the usual width, its ink not measured: the Eroica's flat b2. in bar 394), or grace heads -- which are then
        // the cue's notes: all of them are small (the Eroica part's c8 in bar 244, dropped as a grace
        // note with no note after it)
        var inBar = sym.Heads.Where(h => h.St == st && h.X > x0 && h.X < x1 && !h.Slash && !h.Cross).ToList();
        var graceHeads = sym.Graces.Where(g => g.St == st && g.X > x0 && g.X < x1).ToList();
        var wholes = sym.Rests.Where(r => r.St == st && r.X > x0 && r.X < x1 && r.Kind == RestKind.Whole).ToList();
        bool cue = (barTicks > 0 || measuring) && wholes.Count == 1 && Math.Abs(wholes[0].X - (x0 + x1) / 2) < 0.25 * (x1 - x0) && inBar.Count + graceHeads.Count > 0;
        if (cue)
        {
            // (small beside the page's black heads where they are wider: a photo's 12-pixel space measures
            // its heads 1.37 wide and the cue's 1.05-1.13 -- the Eroica part's photographed page 2)
            var widths = sym.Heads.Where(h => !h.Hollow && !h.Slash && !h.Cross).Select(h => h.R - h.L + 1).OrderBy(w => w).ToList();
            double under = widths.Count >= 8 ? Math.Max(1.1 * s, 0.85 * widths[widths.Count / 2]) : 1.1 * s;
            cue = 2 * (graceHeads.Count + inBar.Count(h => h.R - h.L + 1 < under)) >= inBar.Count + graceHeads.Count;
        }
        // (in a cue, a "rest" on a head's column, a space from it, is the small note's head and stem
        // taken for a quarter rest -- the Eroica part's "Vln. 1" ais' in bar 187; and a grace head's
        // dot, which no grace note has, is left among the unread pieces: the cue's gis''4. in bar 245)
        if (cue)
            foreach (var g in graceHeads.Where(g => g.Dots == 0))
                if (sym.Others.Any(o => o.Class == "dot" && o.St == st && o.C.MinX + o.C.W / 2.0 - g.X > 0.5 * s && o.C.MinX + o.C.W / 2.0 - g.X < 2 * s
                                        && Math.Abs(o.C.MinY + o.C.H / 2.0 - g.Y) < 0.7 * s))
                    g.Dots = 1;
        foreach (var h in sym.Heads) if (h.St == st && h.X > x0 && h.X < x1) items.Add((h.X, h));
        foreach (var r in sym.Rests)
            if (r.St == st && r.X > x0 && r.X < x1
                && !(cue && r.Kind != RestKind.Whole && inBar.Concat(graceHeads).Any(h => Math.Abs(h.X - r.X) < 0.6 * s && Math.Abs(h.Y - r.Y) < 2 * s)))
                items.Add((r.X, r));
        // (a grace note as a marker: its pitch kept for the next note's head, within four spaces of it)
        foreach (var g in graceHeads) items.Add((g.X, cue ? g : new GraceMark(g)));
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
                        c.StemX = cx + (f.StemX - f.X); c.X = cx; c.Beam = beam; c.Tied = null; c.SlurStart = c.SlurEnd = false; c.Alts = null; c.Graces = null; c.GraceKind = null;
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
                    Beam = h.HasStem && h.Beams > 0 && h.Cluster != 0 && !h.Held ? h.Cluster : 0, Held = h.Held,
                    Stem = h.HasStem ? (h.Up ? 1 : -1) : 0, StemX = h.StemX, Y = h.Y, PosLo = h.Pos, PosHi = h.Pos, Tied = h.Tie ? new List<Pitch> { pitch } : null,
                    StemLo = h.HasStem ? Math.Min(h.Y, h.Tip) : h.Y, StemHi = h.HasStem ? Math.Max(h.Y, h.Tip) : h.Y,
                    SlurStart = h.SlurStart, SlurEnd = h.SlurEnd, SlurStartAcross = h.SlurStartAcross, SlurEndAcross = h.SlurEndAcross, Slash = h.Slash, Alts = others?.Count > 0 ? others : null,
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
                // (another voice's held note on the chord's stem is its own event: Head.Held, HeldNotes)
                var mate = h.Held ? null : evs.LastOrDefault(e => !e.IsRest && !e.Slash && !ev.Slash && !e.Held && e.Stem == ev.Stem &&
                    (ev.Stem != 0 ? Math.Abs(e.StemX - ev.StemX) < 0.3 * s && !Apart(e) : Math.Abs(e.X - x) < 0.35 * s));
                // (a clef just before it is written before it, or before its chord)
                if (clefBefore != null) { if (mate != null) mate.ClefBefore ??= clefBefore; else ev.ClefBefore = clefBefore; clefBefore = null; }
                if (Symbols.DebugAt.HasValue && Math.Abs(x - Symbols.DebugAt.Value.X) < 2 * s && Math.Abs(h.Y - Symbols.DebugAt.Value.Y) < 3 * s)
                    Console.WriteLine($"event from the head at {x:F0},{h.Y:F0}: staff {st.System + 1}/{st.Part} pos {h.Pos}, middle line {mid} -> {pitch.Lys()}, {h.Beams} beams, {ev.Ticks} ticks{(mate != null ? $", on the chord at x {mate.X:F0}" : "")}");
                single.Add(ev.Copy());
                if (mate != null)
                {
                    if (ev.Tie) mate.TieAt(pitch);
                    mate.StemLo = Math.Min(mate.StemLo, ev.StemLo); mate.StemHi = Math.Max(mate.StemHi, ev.StemHi);
                    mate.PosLo = Math.Min(mate.PosLo, h.Pos); mate.PosHi = Math.Max(mate.PosHi, h.Pos);
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
        if (barTicks > 0 && evs.Any(e => e.Held)) HeldNotes(evs, single, barTicks, s);
        // a lone whole rest fills the bar
        if (barTicks > 0)
            foreach (var list in new[] { evs, single })
            {
                if (list.Count == 1 && list[0].IsRest && list[0].Ticks == Dur.Whole)
                {
                    list[0].FullBar = true;
                    list[0].Ticks = barTicks;
                }
                // and so does a whole rest in the bar's middle under another voice's bar, the bar's only whole
                // rest: a part's cue notes over its own rest (a trumpet part's "Vln. 1" bars in 3/4, read as r1;
                // the cue's own rests beside it)
                else if (list.Count(e => e.IsRest && e.Ticks == Dur.Whole) == 1 && list.Single(e => e.IsRest && e.Ticks == Dur.Whole) is { Stem: 0 } r
                         && Math.Abs(r.X - (x0 + x1) / 2) < 0.25 * (x1 - x0) && list.Where(e => e != r).Sum(e => e.Ticks) <= barTicks)
                {
                    r.FullBar = true;
                    r.Ticks = barTicks;
                }
            }
        // (a cue's: every event but the part's own rest)
        if (cue && evs.Any(e => e.FullBar))
            foreach (var e in evs.Concat(single).Where(e => !e.FullBar)) e.Cue = true;
        return (evs, single);
    }

    /// <summary>
    /// Another voice's held note written on a beamed chord's stem (Head.Held), its value told by the
    /// bar's time: the beamed voice (the stems the chord's way) sets the grid when it fills the bar
    /// exactly without the held note, the held note's voice is the stems the other way, and the time
    /// from the chord to that voice's next note (the beamed voice's note in its column; the bar's end
    /// when there is none) is its value when that is one value with its dot. kinkel7-1's scan, bar 19:
    /// d'''4.~ over <c'' f''>8 <c'' f''>8 <c'' f''>8, the dotted d''' on the first chord's stem, the
    /// upper voice's d'''8 on the fourth. A held note the bar's time does not bear out goes back into
    /// its chord, dotted, as a chord's dot was read before.
    /// </summary>
    static void HeldNotes(List<Ev> evs, List<Ev> single, int barTicks, double s)
    {
        foreach (var e in evs.Where(e => e.Held).ToList())
        {
            var o = evs.Where(x => x != e && !x.IsRest && !x.Held && x.Stem != 0 && x.Beam != 0 && Math.Abs(x.StemX - e.StemX) < 0.3 * s && Math.Abs(x.X - e.X) < 0.6 * s)
                       .OrderBy(x => Math.Abs(x.X - e.X)).FirstOrDefault();
            bool dbg = Symbols.DebugAt.HasValue && Math.Abs(e.X - Symbols.DebugAt.Value.X) < 2 * s && Math.Abs(e.Y - Symbols.DebugAt.Value.Y) < 3 * s;
            int dots = Dur.Text(e.Ticks)?.Count(c => c == '.') ?? 1;
            string why = null;
            int ticks = 0;
            if (o == null) why = "no beamed chord on its stem";
            else
            {
                var grid = evs.Where(x => !x.Held && !x.IsRest && x.Stem == o.Stem).OrderBy(x => x.X).ToList();
                var voice = evs.Where(x => x != e && !x.Held && !x.IsRest && x.Stem == -o.Stem).ToList();
                int sum = grid.Sum(x => x.Ticks);
                if (sum != barTicks) why = $"the beamed voice is {Dur.Text(sum) ?? sum.ToString()} of the bar's {Dur.Text(barTicks) ?? barTicks.ToString()}";
                else if (voice.Count == 0 && !e.Tie) why = "no other note of its voice";
                else
                {
                    int t0 = grid.Take(grid.IndexOf(o)).Sum(x => x.Ticks);
                    var next = voice.Where(x => x.X > e.X + 0.5 * s).OrderBy(x => x.X).FirstOrDefault();
                    int at = next == null ? grid.Count : grid.FindIndex(x => Math.Abs(x.X - next.X) < 0.6 * s);
                    if (at < 0) why = "its voice's next note in no column of the beamed voice";
                    else
                    {
                        ticks = grid.Take(at).Sum(x => x.Ticks) - t0;
                        string text = ticks > 0 ? Dur.Text(ticks) : null;
                        if (text == null || text.Count(c => c == '.') != dots) why = $"{ticks} ticks to its voice's next note is no value with {dots} dot{(dots == 1 ? "" : "s")}";
                    }
                }
            }
            if (dbg) Console.WriteLine($"held note {e.Show()} at {e.X:F0},{e.Y:F0}: {(why == null ? $"{Dur.Text(ticks)} by the bar's time, in the voice of the stems {(o.Stem > 0 ? "down" : "up")}" : why + (o != null ? ", back into the chord" : ", left as read"))}");
            var copies = single.Where(c => c.Held && !c.IsRest && c.P == e.P && Math.Abs(c.X - e.X) < 0.01).ToList();
            if (why == null)
            {
                e.Ticks = ticks; e.Stem = -o.Stem; e.Beam = 0;
                foreach (var c in copies) { c.Ticks = ticks; c.Stem = e.Stem; c.Beam = 0; }
                continue;
            }
            e.Held = false;
            foreach (var c in copies) c.Held = false;
            if (o == null) continue;
            // (as the head would have joined its chord above)
            if (e.Tie) o.TieAt(e.P);
            o.StemLo = Math.Min(o.StemLo, e.StemLo); o.StemHi = Math.Max(o.StemHi, e.StemHi);
            o.SlurStart |= e.SlurStart; o.SlurEnd |= e.SlurEnd;
            if (e.Alts != null) o.Alts = (o.Alts ?? new()).Concat(e.Alts).ToList();
            if (e.Bare != null) o.Bare = (o.Bare ?? new()).Concat(e.Bare).ToList();
            o.Chord ??= new List<Pitch> { o.P };
            o.Chord.Add(e.P);
            o.Chord.Sort((p1, p2) => p1.Diatonic.CompareTo(p2.Diatonic));
            o.P = o.Chord[^1];
            o.Ticks = e.Ticks;
            evs.Remove(e);
            foreach (var c in single.Where(c => !c.IsRest && c.Stem == o.Stem && Math.Abs(c.StemX - o.StemX) < 0.3 * s && o.Chord.Contains(c.P))) c.Ticks = o.Ticks;
        }
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
    /// <summary>A measure's events staff by staff, to measure lengths with: a beam group on two staves counted on its first note's (<see cref="CrossBeams"/>).</summary>
    static List<List<Ev>> StaffMeasures(PageSymbols sym, SystemInfo sys, int m, double s)
    {
        var read = sys.Staves.Select(st => StaffEvents(sym, st, sys.Bars[m], sys.Bars[m + 1], s, Clefs.MiddleLine("treble"), new int[7], 0, measuring: true)).ToList();
        var evs = read.Select(r => r.Evs).ToArray();
        CrossBeams(evs, read.Select(r => r.Single).ToArray(), s);
        return evs.Select(Measured).ToList();
    }

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
                foreach (var evs in StaffMeasures(sym, sys, m, s))
                {
                    if (evs.Count == 0 || (evs.Count == 1 && evs[0].IsRest && evs[0].Ticks == Dur.Whole)) continue;
                    filling.Add(evs);
                }
            int quarter = Dur.Whole / 4, best = 4;
            int Fill(int k) => filling.Count(evs => Fills(evs, k * quarter));
            for (int k = 1; k <= 12; k++) if (Fill(k) > Fill(best)) best = k;
            // (4/4 stands where more measures fill it in threes than fill the best as read)
            if (best != 4 && filling.Count(evs => FillsInThrees(evs, 4 * quarter, 4)) > Fill(best)) best = 4;
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
                foreach (var evs in StaffMeasures(sym, sys, m, s))
                {
                    if (evs.Count == 0 || (evs.Count == 1 && evs[0].IsRest && evs[0].Ticks == Dur.Whole)) continue;
                    contents.Add(evs);
                }
            if (contents.Count < 2) continue;
            int Filled(int ticks) => contents.Count(evs => Fills(evs, ticks));
            // the upper number the most staff-measures fill; a tie goes to the one most of the signs read
            int best = readings.Where(r => r.Den == den).GroupBy(r => r.Num).OrderByDescending(grp => grp.Count()).First().Key;
            int read = best;
            for (int k = 1; k <= 16; k++) if (Filled(k * unit) > Filled(best * unit)) best = k;
            // (the sign read stands where more measures fill it in threes than fill the best as read; or as many,
            // each of two voices, two events in a column: the best as read the voices run into one -- gen-threes'
            // 2/4 under a melody taken for 5/4; or as many under a C, no misreading of figures -- seed 11's threes073,
            // "r16 b'16 d''16" eight times a bar taken for 6/4. Not 6/8 read as 2/4, its one voice's eighths filling 3/4)
            var inThrees = contents.Where(evs => FillsInThrees(evs, read * unit, den)).ToList();
            static bool TwoVoices(List<Ev> evs, double s) => evs.Any(a => !a.IsRest && evs.Any(b => b != a && !b.IsRest && Math.Abs(a.X - b.X) < 0.3 * s));
            // (under a C or a cut C half of them will do: a bar's beams one stems up and one down, six eighths each, are
            // two voices to this count and not in threes -- --knee's threes011, a cut C taken for 3/2)
            if (best != read && (inThrees.Count > Filled(best * unit)
                                 || inThrees.Count == Filled(best * unit) && (inThrees.All(evs => TwoVoices(evs, s)) || group.All(g => g.Sign))
                                 || group.All(g => g.Sign) && 2 * inThrees.Count >= Filled(best * unit))) best = read;
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
        // the steps held into this bar: a tied note's, with no event after it at that step
        var held = new Dictionary<int, int>();
        for (int i = 0; i < prev.Count; i++)
        {
            var e = prev[i];
            if (e.IsRest || e.Slash || !e.Tie) continue;
            foreach (var p in e.Tied)
                if (!prev.Skip(i + 1).Any(x => !x.IsRest && !x.Slash && x.Pitches.Any(q => q.Diatonic == p.Diatonic)))
                    held[p.Diatonic] = p.Alter;
        }
        foreach (var e in cur)
        {
            if (held.Count == 0) return;
            if (e.IsRest || e.Slash) continue;
            var ps = e.Pitches;
            for (int k = 0; k < ps.Count; k++)
            {
                int dia = ps[k].Diatonic;
                if (!held.Remove(dia, out int alt)) continue;
                if (e.Bare?.Contains(dia) != true || ps[k].Alter != keyAlt[Pitch.Mod7(dia)] || alt == ps[k].Alter) continue;
                var old = ps[k];
                var now = new Pitch(dia, alt);
                if (e.Chord != null) { e.Chord[k] = now; e.P = e.Chord[^1]; }
                else e.P = now;
                e.Respell(old, now);
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
            // (or the two fill it in threes, no number printed: a triplet's first stem, from its beam over
            // the staff to a low head, taken for a barline -- gen-threes' threes007, 4/4's twelve eighths)
            if (!Fills(joined, ticks) && !(ea.Count > 0 && eb.Count > 0 && a.Time?.Split('/') is [_, var d] && int.TryParse(d, out int den) && FillsInThrees(joined, ticks, den)))
            { if (ea.Count > 0 && eb.Count > 0 && Entered(joined)) entered++; continue; }
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

    /// <summary>
    /// Each staff short because one hand ends the line the other starts, all the notes of the one before the
    /// other's: the measure is whole in itself, no sign of a lost note, a split bar or a count gone astray.
    /// (Two in a row looked split at the system break, and every bar after was one on; a 15 misread as a 13
    /// on a scan was believed after one, the count being in doubt)
    /// </summary>
    static bool HandsOver(Measure m)
    {
        var notes = m.Evs.Select(evs => evs.Where(e => !e.IsRest).ToList()).Where(n => n.Count > 0).ToList();
        // (two hands alone with notes in the measure: a band's bars run together on a page photographed askew pair up too, band002)
        if (notes.Count != 2) return false;
        // (a line's length as read, or with its beamed notes triplets: those with no number are not yet read so here)
        static int[] Lengths(IEnumerable<Ev> v) => new[] { v.Sum(e => e.Ticks), v.Sum(e => e.Beam != 0 && e.Scale == (1, 1) ? e.Ticks * 2 / 3 : e.Ticks) };
        // (the first hand's a running figure, three beamed notes at least: kinkel7-1's bar split at its repeat, "a8 r8 r8 r8 :|", is no such
        // line. And the two hands' lines make the bar: a page photographed askew, a barline missed, reads as staves one after the other too, band002)
        return notes.Any(a => a.Count(e => e.Beam != 0) >= 3 && notes.Any(b => a != b && a.Where(e => e.Beam != 0).Max(e => e.X) < b.Min(e => e.X)
            && Lengths(a).Any(la => b.GroupBy(e => e.Stem).Any(g => Lengths(g).Contains(m.BarTicks - la)))));
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

    /// <summary>
    /// A measure's events as its length is measured before the time is known: a bar's only whole rest
    /// among notes is the other voice's bar of rest, any length (a part's rest under a cue, read with its
    /// small notes as events (StaffEvents' measuring): its four quarters added to the cue's 4/4, or the
    /// cue's notes no time at all, made gen-cue's chords001 5/4 with three cue bars).
    /// </summary>
    static List<Ev> Measured(List<Ev> evs)
    {
        var wholes = evs.Where(e => e.IsRest && e.Ticks == Dur.Whole && e.Stem == 0).ToList();
        return wholes.Count == 1 && evs.Any(e => !e.IsRest) ? evs.Where(e => e != wholes[0]).ToList() : evs;
    }

    /// <summary>Whether the events make a measure of exactly the given length, voice by voice.</summary>
    static bool Fills(List<Ev> evs, int barTicks)
    {
        if (evs.Count == 0) return false;
        return Voices.Separate(evs, barTicks, 0).All(v => v.Sum(e => e.Ticks) == barTicks);
    }

    /// <summary>
    /// Whether the events fill a measure of the given length as they are, or with their groups of
    /// three read as triplets with no number printed (<see cref="UnprintedTriplets"/>, on copies): a
    /// time checked against the measures. Music in threes from end to end has no bar that fills
    /// the time as read, and its two voices run into one filled a length of their own -- a C sign
    /// over a melody and twelve beamed eighths taken for 10/4 (gen-threes' threes001, S55).
    /// </summary>
    static bool FillsInThrees(List<Ev> evs, int barTicks, int den)
    {
        if (Fills(evs, barTicks)) return true;
        var copy = evs.Select(e => e.Copy()).ToList();
        // (a bar of one line filling it as one voice: its stems each way are no two voices -- seed 11's threes073)
        return UnprintedTriplets(copy, new List<Ev>(), barTicks, $"{barTicks * den / Dur.Whole}/{den}") != null
               && (Fills(copy, barTicks) || OneLine(copy) && copy.Sum(e => e.Ticks) == barTicks);
    }

    /// <summary>
    /// Whether a staff's bar is one line, no two events in a column: the narrowest gap between
    /// them not much under the usual one.
    /// </summary>
    static bool OneLine(List<Ev> evs)
    {
        var xs = evs.Select(e => e.X).OrderBy(x => x).ToList();
        var gaps = xs.Zip(xs.Skip(1), (a, b) => b - a).OrderBy(g => g).ToList();
        return gaps.Count > 0 && gaps[0] > 0.25 * gaps[gaps.Count / 2];
    }
}
