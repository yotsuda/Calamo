namespace Omr;

/// <summary>
/// Doubted bars put right by the reader itself (ROADMAP S33, stage 1: no model). A staff's bar
/// whose voices do not fill the time is the reading's own warning that something in it is
/// misread. For such a bar the other readings of one event at a time are listed: a note or rest
/// a value longer or shorter (a flag or beam more or less, a half note for a quarter), a dot more
/// or less, a rest the detector half saw where the reading has none, a note or rest the detector
/// sees nothing of taken out. Each is kept only when the page half supports it (the detector's
/// score for the symbol it adds, against the one it takes away: the evidence) and when, with it,
/// every voice of the bar fills the time exactly (Voices.Separate run again). The bar is put
/// right when exactly one such reading is left; with two or more, or none, it keeps its todo.
/// Nothing is done on a page most of whose bars are off (the meter is what is misread there),
/// nor in a bar every voice of which is off alike (a pickup, or the meter again).
/// </summary>
static class Fixes
{
    public static bool Off;        // --no-fix: leave the doubted bars as read (to tell what this does)
    public static bool Verbose;    // --fixes: every candidate weighed, and why it was taken or not

    /// <summary>A bar put right: the part-bar, the event as read and as put right, the reading's name, and the page's evidence for it.</summary>
    public sealed record Fix(int Bar, int Part, string Was, string Now, string Why, double Evidence);

    /// <summary>
    /// One other reading of a doubted part-bar, for x.omr.json's "readings" (Lily#'s AI fix shows them
    /// to its model: the proposal of 2026-10-23): the event as read and as it might be ("-": none), why,
    /// the page's evidence, whether every voice fills the bar with it, and where the event is on the page as loaded.
    /// </summary>
    public sealed record Reading(string Why, string Was, string Now, double Evidence, bool Fills, int X, int Y);

    // one candidate reading of a bar: the events with it, how to say it, and how the page supports it
    sealed record Cand(string Why, string Was, string Now, double Evidence, Action Apply, List<Ev> Trial, double X = 0, double Y = 0);

    /// <summary>
    /// The doubted bars of a page put right, in place (res.Bars, res.Voices and state.Played share the
    /// events). Every bar weighed leaves its readings in <paramref name="readings"/> (with --no-fix too,
    /// and on a page whose meter is in doubt), the ones that fill the bar first, the surer first; a bar
    /// put right leaves none (its todo says what was done).
    /// </summary>
    /// <param name="toSource">a point of the page as read, on the page as loaded (Sidecar's boxes are mapped so)</param>
    public static List<Fix> Apply(PageResult res, ReaderState state, Dictionary<(int Bar, int Part), List<Reading>> readings, Func<double, double, (int X, int Y)> toSource)
    {
        var fixes = new List<Fix>();
        if (res.Sym.Detections == null) return fixes;
        bool meterDoubted = false;
        double s = res.S;
        var dets = res.Sym.Detections;
        var staves = res.Systems.SelectMany(x => x.Staves).ToList();
        // the page's bars that fill their time as read, of those with music, against the bars that
        // would fill another length over the same lower number: when another length fills more of
        // them, the meter is what is misread (1/4 read where 3/4 is played, 4/4 standing where no
        // sign was read), not the bars. A page few of whose bars fill any length (an old print
        // scanned, kinkel10-1: 8 of 24 part-bars) is bad bars, and each is weighed on its own.
        var filled = new Dictionary<string, int[]>();   // the time in force -> the part-bars filling k of its units, k = 1..16
        int music = 0;
        foreach (var ((b, p), evs) in res.Bars)
        {
            if (evs.Count == 0 || evs.Any(e => e.FullBar || e.Blank || e.Slash)) continue;
            if (!res.Voices.TryGetValue((b, p), out var vs) || !state.BarTime.TryGetValue(b, out var tm)) continue;
            music++;
            if (!filled.TryGetValue(tm, out var counts)) filled[tm] = counts = new int[17];
            int unit = Dur.Whole / int.Parse(tm.Split('/')[1]);
            for (int k = 1; k <= 16; k++) if (vs.All(v => v.Sum(e => e.Ticks) == k * unit)) counts[k]++;
        }
        if (music == 0) return fixes;
        foreach (var (tm, counts) in filled)
        {
            int num = int.Parse(tm.Split('/')[0]);
            int other = Enumerable.Range(1, 16).Where(k => k != num).Max(k => counts[k]);
            if (num <= 16 && other <= counts[num]) continue;
            if (Verbose) Console.WriteLine($"  fixes: page {res.Page}: {counts[Math.Min(num, 16)]} part-bars fill {tm}, {other} fill another length over {tm.Split('/')[1]}: the meter is in doubt, no bar put right");
            meterDoubted = true;
        }
        foreach (int b in res.Bars.Keys.Select(k => k.Bar).Distinct().OrderBy(b => b))
        {
            if (!state.BarTime.TryGetValue(b, out var time)) continue;
            int barTicks = Dur.OfTime(time);
            // the parts off, and by how much each voice is: every voice of every part off alike is no bar's fault
            var deltas = new List<int>();
            var offParts = new List<int>();
            foreach (var ((bar, p), vs) in res.Voices.Where(kv => kv.Key.Bar == b))
            {
                if (!state.Played.TryGetValue((b, p), out var played) || played.Evs.Count == 0 || played.Evs.Any(e => e.FullBar || e.Blank || e.Slash)) continue;
                var ds = vs.Select(v => v.Sum(e => e.Ticks) - barTicks).ToList();
                deltas.AddRange(ds);
                if (ds.Any(d => d != 0)) offParts.Add(p);
            }
            if (offParts.Count == 0 || deltas.Distinct().Count() == 1) continue;
            foreach (int p in offParts)
            {
                var sys = res.Systems.FirstOrDefault(x => x.Measures > 0 && b >= x.FirstBar && b < x.BarOf(x.Measures));
                var st = sys?.Staves.FirstOrDefault(x => x.Part == p);
                if (st == null) continue;
                var (chords, single, x0, x1) = state.Played[(b, p)];
                var cands = Candidates(res, st, chords, barTicks, x0, x1, s, dets, staves);
                var fitting = cands.Where(c => Voices.Separate(c.Trial, barTicks, st.Mid, st.S).All(v => v.Sum(e => e.Ticks) == barTicks)).ToList();
                if (Verbose)
                {
                    Console.WriteLine($"  fixes: bar {b} part {p} ({string.Join(" ", chords.Select(e => e.Show()))}; {Memo.BarLength(res.Voices[(b, p)].Select(v => v.Sum(e => e.Ticks)).ToList(), barTicks, time)}): {cands.Count} candidates, {fitting.Count} fill the bar");
                    foreach (var c in cands) Console.WriteLine($"    {(fitting.Contains(c) ? "fits " : "     ")} {c.Why}: {c.Was} -> {c.Now} at {c.X:F0},{c.Y:F0}, evidence {c.Evidence:F2}");
                }
                bool take = !Off && !meterDoubted && fitting.Count == 1;
                if (!take && cands.Count > 0)
                    readings[(b, p)] = cands.OrderByDescending(c => fitting.Contains(c)).ThenByDescending(c => c.Evidence).Take(8)
                        .Select(c => { var (x, y) = toSource(c.X, c.Y); return new Reading(c.Why, c.Was, c.Now, c.Evidence, fitting.Contains(c), x, y); }).ToList();
                if (!take) continue;
                var fix = fitting[0];
                fix.Apply();
                // (the single copies, every head its own event, follow the chords; the voices are split again)
                Resync(chords, single, s);
                res.Voices[(b, p)] = Voices.Separate(chords, barTicks, st.Mid, st.S);
                fixes.Add(new Fix(b, p, fix.Was, fix.Now, fix.Why, fix.Evidence));
                res.Notes.Add($"page {res.Page}: bar {b} part {p}: {fix.Was} read as {fix.Now} ({fix.Why}, evidence {fix.Evidence:F2}): the bar fills its {time} now");
            }
        }
        return fixes;
    }

    // the other readings of the bar, one event changed each
    static List<Cand> Candidates(PageResult res, Staff st, List<Ev> chords, int barTicks, double x0, double x1, double s, List<Detection> dets, List<Staff> staves)
    {
        var cands = new List<Cand>();
        double Best(string cls, double x, double y, double dx, double dy) =>
            dets.Where(t => t.Class == cls && Math.Abs(t.X - x) < dx && Math.Abs(t.Y - y) < dy).Select(t => t.Score).DefaultIfEmpty(0).Max();
        // (a dot more weighs the detector's dots under its threshold too: an old print's dot hung from a
        // line, joined to its stub as the line is taken out, seen at 0.21-0.44 -- kinkel19-5's scan, the voice's d'4.)
        // (on a scan only: a clean page's dots are seen surely, and a weak one there, in a bar a hidden rest leaves
        // short, gave a chord a dot -- kinkel7-4's MuseScore page, <e, b, e>2. at 0.35)
        double BestDot(double x, double y, double dx, double dy, bool weak) =>
            (weak && res.Sym.Scan ? dets.Concat(res.Sym.WeakDots) : dets).Where(t => t.Class == "dot" && Math.Abs(t.X - x) < dx && Math.Abs(t.Y - y) < dy).Select(t => t.Score).DefaultIfEmpty(0).Max();
        List<Head> HeadsOf(Ev e) => res.Sym.Heads.Where(h => h.St == st && !h.Grace && (e.Stem != 0 ? Math.Abs(h.StemX - e.StemX) < 0.3 * s && h.HasStem && h.Up == (e.Stem > 0) : Math.Abs(h.X - e.X) < 0.35 * s) && Math.Abs(h.X - e.X) < 1.5 * s).ToList();
        static int BeamsOf(int basis) => basis switch { 8 => 1, 16 => 2, 32 => 3, >= 64 => 3, _ => 0 };
        static string RestClass(int basis) => basis switch { 4 => "rest_4", 8 => "rest_8", 16 => "rest_16", _ => null };
        // how surely the detector sees each event (a head's own score, or the best head or rest detection at it)
        double Seen(Ev e, List<Head> heads, int basis) => e.IsRest ? (RestClass(basis) is string rc ? Best(rc, e.X, e.Y, 1.0 * s, 1.5 * s) : 1)
            : heads.Count == 0 ? 0 : heads.Max(h => Math.Max(h.Score, Math.Max(Best("head_black", h.X, h.Y, 0.6 * s, 0.6 * s), Best("head_half", h.X, h.Y, 0.6 * s, 0.6 * s))));
        var seen = new double[chords.Count];
        var headsOf = new List<Head>[chords.Count];
        for (int i = 0; i < chords.Count; i++)
        {
            var e = chords[i];
            headsOf[i] = e.IsRest ? new List<Head>() : HeadsOf(e);
            string t = Dur.Text(e.Ticks * e.Scale.Den / e.Scale.Num);
            seen[i] = e.FullBar || e.Blank || e.Slash || t == null ? 1 : Seen(e, headsOf[i], int.Parse(t.TrimEnd('.')));
        }
        for (int i = 0; i < chords.Count; i++)
        {
            var e = chords[i];
            if (e.FullBar || e.Blank || e.Slash) continue;
            string text = Dur.Text(e.Ticks * e.Scale.Den / e.Scale.Num);
            if (text == null) continue;
            int basis = int.Parse(text.TrimEnd('.')), dots = text.Length - text.TrimEnd('.').Length;
            var heads = headsOf[i];
            if (!e.IsRest && heads.Count == 0) continue;
            double hx = e.IsRest ? e.X : heads.Max(h => h.X), hr = e.IsRest ? e.X + 0.7 * s : heads.Max(h => h.R);
            double hy = e.IsRest ? e.Y : heads.Average(h => h.Y);
            // the value a step longer or shorter, the dots one more or fewer
            foreach (var (nb, nd, why) in new[] { (basis * 2, dots, "a value shorter"), (basis / 2, dots, "a value longer"), (basis, dots + 1, "a dot more"), (basis, dots - 1, "a dot fewer") })
            {
                // (no second dot: a double-dotted note is rare, a staccato dot or a speck beyond the dot is not)
                if (nb < 1 || nb > 64 || nd < 0 || nd > 1) continue;
                if (nb != basis && (nb == 1 || basis == 1)) continue;   // (whole notes: a shape of their own, left alone)
                double evidence;
                if (nd != dots)
                {
                    // a dot stands right of the head (or the rest), in its space or the one above: within
                    // a space and three quarters, or further in an old print (kinkel19-2's dots stand two
                    // spaces and a half out), short of the next event to the right (not the other voice's
                    // note on the same stem, which follows in the list: kinkel19-5's bes4. over an e4 lost
                    // its dot to a window seven pixels wide); a chord's dots at each of its heads
                    double next = chords.Skip(i + 1).Where(x => x.X > e.X + 0.5 * s).Select(x => x.X).DefaultIfEmpty(x1).Min() - 0.6 * s;
                    double far = Math.Max(hr + 1.0 * s, Math.Min(hr + 2.6 * s, next));
                    // (the dots under the threshold only for a dot more: a dot taken away stands on the sure ones, as
                    // before -- kinkel7-1's and 7-5's scans, dots read on specks taken away rightly)
                    bool weak = nd > dots;
                    double sDot = e.IsRest ? BestDot((hr + far) / 2, hy - 0.25 * s, (far - hr) / 2, 1.2 * s, weak)
                        : heads.Max(h => BestDot((hr + far) / 2, h.Y - 0.25 * s, (far - hr) / 2, 0.9 * s, weak));
                    evidence = nd > dots ? sDot : 0.5 - sDot;
                    if (Verbose) Console.WriteLine($"      dots of {e.Show()} at {e.X:F0}: heads {string.Join(" ", heads.Select(h => $"{h.X:F0},{h.Y:F0}"))}, dot window x {hr:F0}-{far:F0}, best dot {sDot:F2}");
                    if (nd > dots && sDot < 0.15) continue;
                    // (a dot taken away on the detector's silence alone: only in a bar it sees the rest of)
                    if (nd < dots && (sDot >= 0.3 || seen.Where((v, k) => k != i).Any(v => v < Detector.Threshold))) continue;
                }
                else if (e.IsRest)
                {
                    string cNew = RestClass(nb), cOld = RestClass(basis);
                    if (cNew == null || cOld == null) continue;
                    double sNew = Best(cNew, e.X, e.Y, 1.0 * s, 1.5 * s), sOld = Best(cOld, e.X, e.Y, 1.0 * s, 1.5 * s);
                    evidence = sNew - sOld;
                    if (sNew < 0.2 || evidence < 0.1) continue;
                }
                else if (nb >= 4 && basis >= 4)
                {
                    // a black note's value is at its stem's tip: so many beams or flags
                    var h = heads[0];
                    if (!h.HasStem) continue;
                    double sNew = Best($"stem_{BeamsOf(nb)}", h.StemX, h.Tip, 0.6 * s, 1.5 * s), sOld = Best($"stem_{BeamsOf(basis)}", h.StemX, h.Tip, 0.6 * s, 1.5 * s);
                    evidence = sNew - sOld;
                    if (sNew < 0.2 || evidence < 0.1) continue;
                }
                else
                {
                    // a half note for a quarter, or the other way: the head's kind
                    string cNew = nb == 2 ? "head_half" : "head_black", cOld = basis == 2 ? "head_half" : "head_black";
                    double sNew = heads.Max(h => Best(cNew, h.X, h.Y, 0.6 * s, 0.6 * s)), sOld = heads.Max(h => Best(cOld, h.X, h.Y, 0.6 * s, 0.6 * s));
                    evidence = sNew - sOld;
                    if (sNew < 0.2 || evidence < 0.1) continue;
                }
                int newTicks = Dur.Of(nb, nd) * e.Scale.Num / e.Scale.Den;
                var trial = chords.Select(x => x.Copy()).ToList();
                trial[i].Ticks = newTicks;
                int ei = i, nbb = nb, ndd = nd;
                var ev = e;
                cands.Add(new Cand(why, e.Show(), trial[i].Show(), Math.Round(evidence, 2), () =>
                {
                    ev.Ticks = newTicks;
                    foreach (var h in heads) { h.Dots = ndd; if (nbb >= 4) h.Beams = BeamsOf(nbb); h.Hollow = nbb < 4; }
                }, trial, e.X, hy));
            }
            // an event the page shows nothing of, taken out (a bar too long): only in a bar the
            // detector sees the rest of (an old print's rests it knows no shape for are not nothing)
            if (seen[i] < 0.3 && seen.Where((v, k) => k != i).All(v => v >= Detector.Threshold))
            {
                var trial = chords.Where((x, k) => k != i).Select(x => x.Copy()).ToList();
                var ev = e;
                cands.Add(new Cand("nothing seen there", e.Show(), "-", Math.Round(0.5 - seen[i], 2), () => chords.Remove(ev), trial, e.X, e.IsRest ? e.Y : heads.Average(h => h.Y)));
            }
        }
        // a rest the detector half saw where the reading has none (a bar too short)
        // (and the detector's rests under its threshold, kept apart for this: an old print's, kinkel19-5's)
        var halfSeen = new List<(Ev R, double Score)>();
        foreach (var t in dets.Concat(res.Sym.WeakRests).Where(t => t.Class.StartsWith("rest_") && t.Score >= 0.15 && t.X > x0 + 0.3 * s && t.X < x1 - 0.3 * s && Symbols.Nearest(staves, t.Y, s) == st).OrderByDescending(t => t.Score))
        {
            if (res.Sym.Rests.Any(r => r.St == st && Math.Abs(r.X - t.X) < 0.8 * s) || chords.Any(e => e.IsRest && Math.Abs(e.X - t.X) < 0.8 * s)) continue;
            double pos = st.Pos(t.Y);
            int basis = t.Class switch { "rest_4" => 4, "rest_8" => 8, "rest_16" => 16, _ => pos - 2 * Math.Round(pos / 2) < 0 ? 1 : 2 };
            var rest = new Ev { IsRest = true, Ticks = Dur.Of(basis, 0), X = t.X, Y = t.Y };
            halfSeen.Add((rest, t.Score));
            var trial = chords.Select(x => x.Copy()).Append(rest.Copy()).OrderBy(x => x.X).ToList();
            cands.Add(new Cand("a rest half seen", "-", rest.Show(), Math.Round(t.Score, 2), () =>
            {
                int at = chords.FindIndex(x => x.X > rest.X);
                chords.Insert(at < 0 ? chords.Count : at, rest);
            }, trial, t.X, t.Y));
        }
        // two of them together: an old print's quarter rests, a z the detector half knows, two in a bar
        // (kinkel19-2's scan, the right hand's aes4 r4 r4); the weaker one's score is the evidence
        for (int a = 0; a < halfSeen.Count; a++)
            for (int c = a + 1; c < halfSeen.Count; c++)
            {
                if (Math.Abs(halfSeen[a].R.X - halfSeen[c].R.X) < 0.8 * s) continue;   // (one rest seen as two classes)
                var (r1, r2) = halfSeen[a].R.X < halfSeen[c].R.X ? (halfSeen[a].R, halfSeen[c].R) : (halfSeen[c].R, halfSeen[a].R);
                var trial = chords.Select(x => x.Copy()).Append(r1.Copy()).Append(r2.Copy()).OrderBy(x => x.X).ToList();
                cands.Add(new Cand("two rests half seen", "-", $"{r1.Show()} {r2.Show()}", Math.Round(Math.Min(halfSeen[a].Score, halfSeen[c].Score), 2), () =>
                {
                    foreach (var rest in new[] { r1, r2 })
                    {
                        int at = chords.FindIndex(x => x.X > rest.X);
                        chords.Insert(at < 0 ? chords.Count : at, rest);
                    }
                }, trial, r1.X, r1.Y));
            }
        return cands;
    }

    // the single copies (every head its own event) brought level with the chords after a fix
    static void Resync(List<Ev> chords, List<Ev> single, double s)
    {
        single.RemoveAll(x => !chords.Any(e => Same(e, x, s)));
        foreach (var e in chords)
        {
            var mine = single.Where(x => Same(e, x, s)).ToList();
            if (mine.Count == 0) { single.Add(e.Copy()); continue; }
            foreach (var x in mine) { x.Ticks = e.Ticks; x.Scale = e.Scale; x.Tuplet = e.Tuplet; }
        }
        single.Sort((a, b) => a.X.CompareTo(b.X));
    }

    // (a chord's single copies stand at their own heads' x, either side of the stem: matched by the stem, or by x where there is none)
    static bool Same(Ev chord, Ev one, double s) => chord.IsRest == one.IsRest && chord.Stem == one.Stem
        && (chord.IsRest ? Math.Abs(chord.X - one.X) < 0.01
            : (chord.Stem != 0 ? Math.Abs(chord.StemX - one.StemX) < 0.3 * s : Math.Abs(chord.X - one.X) < 0.35 * s)
              && (chord.Chord?.Contains(one.P) == true || chord.P == one.P));
}
