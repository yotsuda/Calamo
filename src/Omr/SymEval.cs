using System.Text;

namespace Omr;

/// <summary>
/// Scores what the reader found on a page against what Lily# drew there (its SVG): symbol by
/// symbol, so a miss says which stage failed and where, not only that a note came out wrong.
/// </summary>
sealed class SymEval
{
    // per check: how many the truth has, how many came out right
    public readonly SortedDictionary<string, (int All, int Ok)> Counts = new();
    public readonly SortedDictionary<string, int> Extra = new();     // found with no truth behind it
    public readonly List<string> Diffs = new();
    public readonly StringBuilder Samples = new();                    // loose symbols, labelled from the truth
    public readonly List<(int Page, Staff St, double X, double Y)> Errors = new();   // where each diff is (for Verify)

    /// <summary>
    /// Every loose ink piece the reader saw, labelled with the glyph Lily# drew there: the glyph's
    /// name when the piece is most of it, "part" when it is a scrap of one, "none" when no glyph is there.
    /// </summary>
    void Sample(PageResult res, SvgPage svg)
    {
        double s = res.S;
        foreach (var o in res.Sym.Others)
        {
            var c = o.C;
            double area = c.W * (double)c.H, bestOv = 0;
            TGlyph best = null;
            foreach (var g in svg.Glyphs)
            {
                double ox = Math.Min(c.MaxX + 1, g.R) - Math.Max(c.MinX, g.L), oy = Math.Min(c.MaxY + 1, g.B) - Math.Max(c.MinY, g.T);
                if (ox <= 0 || oy <= 0) continue;
                if (ox * oy > bestOv) { bestOv = ox * oy; best = g; }
            }
            string label = "none";
            if (best != null && bestOv >= 0.6 * area)
                label = c.W >= 0.6 * (best.R - best.L) && c.H >= 0.6 * (best.B - best.T) ? best.Name : "part";
            Samples.AppendLine(GlyphKnn.Row(label, GlyphKnn.Features(res.Sym, c, s)));
        }
        // the places a time signature could stand: what Lily# put there (full-size glyphs only:
        // a tuplet number is no time signature)
        var timeGlyphs = svg.Glyphs.Where(g => g.Full && (g.Name.Length == 1 || g.Name.StartsWith("timesig."))).ToList();
        foreach (var (kind, box, f) in res.Sym.TimeSamples)
        {
            var inside = timeGlyphs.Where(g => g.Cx > box.MinX - 0.2 * s && g.Cx < box.MaxX + 0.2 * s && g.Cy > box.MinY - 0.2 * s && g.Cy < box.MaxY + 0.2 * s).ToList();
            string label;
            if (kind == "t")
                label = inside.Count == 0 ? "t:none" : inside.Any(g => g.Name.StartsWith("timesig.")) ? "t:" + inside.First(g => g.Name.StartsWith("timesig.")).Name : "t:num";
            else
            {
                // a digit: the one glyph whose middle it holds
                var one = inside.Where(g => g.Name.Length == 1).ToList();
                label = one.Count == 1 ? "d:" + one[0].Name : "d:none";
            }
            Samples.AppendLine(GlyphKnn.Row(label, f));
        }
        // the boxes over the systems that hold one piece of ink: the letter Lily# set in the box (a
        // rehearsal mark's), or nothing of the kind
        foreach (var fr in Tempo.FramesOf(res).Where(f => f.Letter != null))
        {
            var r = fr.Dark;
            var truth = svg.Rehearsals.Where(t => (t.L + t.R) / 2 > r.Left && (t.L + t.R) / 2 < r.Right && (t.T + t.B) / 2 > r.Top && (t.T + t.B) / 2 < r.Bottom).ToList();
            if (truth.Count > 1 || truth.Count == 1 && truth[0].Text.Length != 1) continue;
            Samples.AppendLine(GlyphKnn.Row(truth.Count == 1 ? "l:" + truth[0].Text : "l:none", fr.F));
        }
    }

    void Count(string key, bool ok)
    {
        var c = Counts.GetValueOrDefault(key);
        Counts[key] = (c.All + 1, c.Ok + (ok ? 1 : 0));
    }

    void Miss(PageResult res, Staff st, string what, double x, double y)
    {
        string where = st == null ? "" : $"s{st.System + 1} part {st.Part} ";
        Diffs.Add($"p{res.Page} {where}{what} at {x:F0},{y:F0}");
        Errors.Add((res.Page, st, x, y));
    }

    sealed class THead
    {
        public TGlyph G;
        public Staff St;
        public int Pos;
        public bool Hollow, Stem, Up;
        public int Chord;            // heads on one stem (stemless: at one x) share it
        public int Beams, Dots;
        public int? Acc;
        public Head Found;
    }

    public void Add(PageResult res, SvgPage svg)
    {
        double s = res.S;
        Sample(res, svg);
        var staves = res.Systems.SelectMany(x => x.Staves).ToList();
        // (a symbol on a TAB or a one-line staff, which are not read, is none of a part's: a TAB's rests)
        var every = res.Systems.SelectMany(x => x.All.Count > 0 ? x.All : x.Staves).ToList();
        Staff StaffOf(double y) => Symbols.Nearest(every, y, s) is { Kind: "" } st ? st : null;
        var music = svg.Glyphs.Where(g => g.Full).ToList();   // not cue-sized text (tuplet numbers)

        // ---- note heads, with what belongs to each: stem, beams or flags, dots, accidental
        var heads = new List<THead>();
        // (a rhythm slash is no note: the reader leaves it, and the truth's .notes has none)
        foreach (var g in music.Where(g => g.Name.StartsWith("noteheads.") && !g.Name.EndsWith("slash")))
        {
            var st = StaffOf(g.Cy);
            if (st == null) continue;
            // (between two staves, the one its ledger lines run to, as the reader places it)
            st = Symbols.ByLedgers(res.Dark, staves, st, g.Cx, g.Cy, s);
            heads.Add(new THead { G = g, St = st, Pos = (int)Math.Round(st.Pos(g.Cy)), Hollow = !g.Name.StartsWith("noteheads.s2") });
        }
        var stems = svg.Lines.Where(l => Math.Abs(l.X1 - l.X2) < 0.5 && Math.Abs(l.Y1 - l.Y2) > 1.5 * s).ToList();
        foreach (var h in heads)
        {
            // a stem starts at the head (or passes it: a chord's inner heads) at its left or right edge.
            // Two voices a second apart put an up stem and a down stem in one column, each ending at
            // its own head: of the stems that reach this head, its own is the one that ends nearest it.
            h.Chord = -1 - (int)Math.Round(h.G.X / (0.8 * s));
            int best = -1;
            double bestEnd = double.MaxValue;
            // (two voices' unison: one head drawn twice, a stem each way -- the second takes the other)
            var twins = heads.TakeWhile(o => o != h).Where(o => o.Stem && Math.Abs(o.G.Cx - h.G.Cx) < 0.3 * s && Math.Abs(o.G.Cy - h.G.Cy) < 0.3 * s)
                             .Select(o => o.Chord).ToHashSet();
            for (int li = 0; li < stems.Count; li++)
            {
                if (twins.Contains(li)) continue;
                var l = stems[li];
                double top = Math.Min(l.Y1, l.Y2), bot = Math.Max(l.Y1, l.Y2);
                if (l.X1 < h.G.L - 0.25 * s || l.X1 > h.G.R + 0.25 * s) continue;
                if (h.G.Cy < top - 0.7 * s || h.G.Cy > bot + 0.7 * s) continue;
                double end = Math.Min(Math.Abs(top - h.G.Cy), Math.Abs(bot - h.G.Cy));
                if (end < bestEnd) { bestEnd = end; best = li; }
            }
            if (best >= 0)
            {
                int li = best;
                var l = stems[li];
                double top = Math.Min(l.Y1, l.Y2), bot = Math.Max(l.Y1, l.Y2);
                h.Stem = true;
                h.Chord = li;
                // the stem grows from the end where the chord's heads are, its tip the other end: a
                // chord's inner head may stand nearer the tip (an octave's upper head, the stem
                // passing it to a beam a space and a half above); a single head is at its root
                bool HeadAt(double y) => heads.Any(o => Math.Abs(o.G.Cy - y) < 0.7 * s && l.X1 >= o.G.L - 0.25 * s && l.X1 <= o.G.R + 0.25 * s);
                bool atTop = HeadAt(top), atBot = HeadAt(bot);
                h.Up = atBot != atTop ? atBot : Math.Abs(top - h.G.Cy) > Math.Abs(bot - h.G.Cy);
                double tip = h.Up ? top : bot;
                // flags at the tip, or beams crossing the stem near it
                foreach (var f in music.Where(f => f.Name.StartsWith("flags.") && Math.Abs(f.X - l.X1) < 0.4 * s && Math.Abs(f.Y - tip) < 0.8 * s))
                    h.Beams = Math.Max(h.Beams, f.Name.EndsWith('3') ? 1 : f.Name.EndsWith('4') ? 2 : f.Name.EndsWith('5') ? 3 : 1);
                int beams = 0;
                foreach (var p in svg.Polys)
                {
                    if (p.Count != 4) continue;
                    var (y0, y1) = PolyAt(p, l.X1);
                    if (double.IsNaN(y0) || y1 - y0 > 0.8 * s) continue;
                    double ym = (y0 + y1) / 2;
                    // from the tip back toward the head, within the room three beams take
                    double into = h.Up ? ym - tip : tip - ym;
                    if (into > -0.5 * s && into < 2.4 * s) beams++;
                }
                h.Beams = Math.Max(h.Beams, beams);
            }
        }
        // dots: a rest's come from its own source token; a note's stand in columns
        // right of the chord (every head of a chord has the chord's dots)
        // (a multi-bar rest's church rests, drawn with no source position, are scored with its count below, as "mmrest":
        // a longa, a breve, and the whole rest beside them for an odd count -- a lone whole rest with none is a bar's own)
        var church = music.Where(g => g.Src < 0 && g.Name is "rests.M1" or "rests.M2").ToList();
        var rests = music.Where(g => g.Name.StartsWith("rests.") && !church.Contains(g)
            && !(g.Src < 0 && g.Name == "rests.0" && church.Any(c => Math.Abs(c.Cx - g.Cx) < 4 * s && Math.Abs(c.Cy - g.Cy) < 2 * s))).ToList();
        var restDots = new Dictionary<TGlyph, int>();
        var allDots = music.Where(g => g.Name == "dots.dot").ToList();
        foreach (var r in rests)
            restDots[r] = allDots.Count(d => d.Src == r.Src && r.Src >= 0 && d.L > r.Cx);
        // (a chord's dots carry the source position of the chord, not of any one head)
        var restSrc = rests.Where(r => r.Src >= 0).Select(r => r.Src).ToHashSet();
        var noteDots = allDots.Where(d => !restSrc.Contains(d.Src)).ToList();
        // a dot belongs to the nearest chord on its left whose heads it stands level with: a dot
        // sits at its head's height, or half a space off it when the head is on a line
        var chords = heads.GroupBy(h => (h.St, h.Chord))
            .Select(c => (Heads: c.ToList(), Right: c.Max(h => h.G.R), Top: c.Min(h => h.G.Cy), Bot: c.Max(h => h.G.Cy))).ToList();
        var chordDots = new Dictionary<int, List<double>>();
        foreach (var d in noteDots)
        {
            int best = -1;
            for (int i = 0; i < chords.Count; i++)
            {
                var c = chords[i];
                if (d.L < c.Right - 0.2 * s || d.L - c.Right > 2.5 * s) continue;
                if (d.Cy < c.Top - 0.75 * s || d.Cy > c.Bot + 0.75 * s) continue;
                if (best < 0 || c.Right > chords[best].Right) best = i;
            }
            if (best < 0) continue;
            if (!chordDots.TryGetValue(best, out var xs)) chordDots[best] = xs = new List<double>();
            xs.Add(d.L);
        }
        foreach (var (i, xs) in chordDots)
        {
            // columns: a second dot stands half a space or so after the first
            int cols = 0; double last = double.MinValue;
            foreach (var x in xs.OrderBy(x => x)) if (x - last > 0.3 * s) { cols++; last = x; }
            foreach (var h in chords[i].Heads) h.Dots = cols;
        }
        // an accidental comes from the same source token as its note
        var keyAccs = new List<TGlyph>();
        foreach (var a in music.Where(g => g.Name.StartsWith("accidentals.")))
        {
            var mine = heads.Where(h => h.G.Src == a.Src && a.Src >= 0 && h.G.L > a.L).OrderBy(h => Math.Abs(h.G.Cy - a.Y)).FirstOrDefault();
            if (mine == null) { keyAccs.Add(a); continue; }
            int? acc = a.Name switch
            {
                "accidentals.sharp" => 1, "accidentals.flat" => -1, "accidentals.natural" => 0,
                "accidentals.doublesharp" => 2, "accidentals.flatflat" => -2, _ => null,
            };
            // (a unison's head drawn twice, one per voice: the sign is both notes')
            foreach (var h in heads.Where(h => Math.Abs(h.G.Cx - mine.G.Cx) < 0.3 * s && Math.Abs(h.G.Cy - mine.G.Cy) < 0.3 * s)) h.Acc = acc;
        }

        // match found heads to true ones, nearest first
        var pairs = new List<(double D, THead T, Head F)>();
        foreach (var t in heads)
            foreach (var f in res.Sym.Heads.Where(f => !f.Slash))
            {
                // (two voices' unison is one head drawn twice, read twice: the stems' directions pair them)
                double dd = Math.Abs(f.X - t.G.Cx) + Math.Abs(f.Y - t.G.Cy) + (t.Stem && f.HasStem && f.Up != t.Up ? 0.05 * s : 0);
                if (Math.Abs(f.X - t.G.Cx) < 0.7 * s && Math.Abs(f.Y - t.G.Cy) < 0.4 * s) pairs.Add((dd, t, f));
            }
        var used = new HashSet<Head>();
        foreach (var (_, t, f) in pairs.OrderBy(p => p.D))
            if (t.Found == null && !used.Contains(f)) { t.Found = f; used.Add(f); }
        foreach (var t in heads)
        {
            var f = t.Found;
            string name = $"{t.G.Name[10..]} pos {t.Pos}";
            Count("head", f != null);
            if (f == null) { Miss(res, t.St, $"head missed ({name})", t.G.Cx, t.G.Cy); continue; }
            void Check(string key, bool ok, string what)
            {
                Count(key, ok);
                if (!ok) Miss(res, t.St, $"{key}: {what} ({name})", t.G.Cx, t.G.Cy);
            }
            Check("head.pos", f.Pos == t.Pos, $"pos {f.Pos}");
            Check("head.hollow", f.Hollow == t.Hollow, f.Hollow ? "hollow" : "filled");
            Check("head.stem", f.HasStem == t.Stem && (!t.Stem || f.Up == t.Up), f.HasStem ? (f.Up ? "up" : "down") : "none");
            if (t.Stem && !t.Hollow) Check("head.beams", f.Beams == t.Beams, $"{f.Beams} for {t.Beams}");
            Check("head.dots", f.Dots == t.Dots, $"{f.Dots} for {t.Dots}");
            Check("head.acc", f.Acc == t.Acc, $"{Show(f.Acc)} for {Show(t.Acc)}");
        }
        foreach (var f in res.Sym.Heads.Where(f => !used.Contains(f) && !f.Slash))
        {
            Extra["head"] = Extra.GetValueOrDefault("head") + 1;
            Miss(res, f.St, $"head extra (pos {f.Pos})", f.X, f.Y);
        }

        // ---- rests
        var usedR = new HashSet<Rest>();
        foreach (var r in rests)
        {
            var st = StaffOf(r.Cy);
            if (st == null) continue;
            var kind = r.Name switch
            {
                "rests.0" => RestKind.Whole, "rests.1" => RestKind.Half, "rests.2" => RestKind.Quarter,
                "rests.3" => RestKind.Eighth, _ => RestKind.Sixteenth,
            };
            var f = res.Sym.Rests.Where(x => !usedR.Contains(x) && Math.Abs(x.X - r.Cx) < 0.8 * s && Math.Abs(x.Y - r.Cy) < 1.5 * s)
                .OrderBy(x => Math.Abs(x.X - r.Cx)).FirstOrDefault();
            Count("rest", f != null);
            if (f == null) { Miss(res, st, $"rest missed ({kind})", r.Cx, r.Cy); continue; }
            usedR.Add(f);
            Count("rest.kind", f.Kind == kind);
            if (f.Kind != kind) Miss(res, st, $"rest.kind: {f.Kind} for {kind}", r.Cx, r.Cy);
            int dots = restDots.GetValueOrDefault(r);
            Count("rest.dots", f.Dots == dots);
            if (f.Dots != dots) Miss(res, st, $"rest.dots: {f.Dots} for {dots}", r.Cx, r.Cy);
        }
        // (a rest in a measure read as a multi-bar rest is one of its church rests: scored as "mmrest")
        bool InMultiRest(Rest f) => res.Systems.Any(y => y.Staves.Contains(f.St)
            && Enumerable.Range(0, y.Measures).Any(m => y.Rests.ContainsKey(y.Bars[m]) && y.Bars[m] < f.X && f.X < y.Bars[m + 1]));
        foreach (var f in res.Sym.Rests.Where(f => !usedR.Contains(f) && !InMultiRest(f)))
        {
            Extra["rest"] = Extra.GetValueOrDefault("rest") + 1;
            Miss(res, f.St, $"rest extra ({f.Kind})", f.X, f.Y);
        }

        // ---- clefs, keys, times: each against the found signature of its kind nearest in x
        foreach (var c in music.Where(g => g.Name.StartsWith("clefs.")))
        {
            var st = StaffOf(c.Y);
            if (st == null) continue;
            int pos = (int)Math.Round(st.Pos(c.Y));
            string clef = c.Name switch
            {
                "clefs.G" or "clefs.G_change" => "treble",
                "clefs.F" or "clefs.F_change" => "bass",
                "clefs.percussion" or "clefs.percussion_change" => "percussion",
                _ => pos switch { 0 => "alto", 2 => "tenor", -2 => "mezzosoprano", -4 => "soprano", 4 => "baritone", _ => "C" + pos },
            };
            var f = res.Sym.Sigs.Where(g => g.St == st && g.Clef != null && Math.Abs(g.X - c.L) < 2 * s).FirstOrDefault();
            string got = f?.Clef?.Replace("_8", "");
            Count("clef", got == clef);
            if (got != clef) Miss(res, st, $"clef: {got ?? "none"} for {clef}", c.L, c.Y);
        }
        // (a key's signs stand in the staff: a chord name's flat above a vocal staff is drawn with the same glyph)
        foreach (var grp in keyAccs.GroupBy(a => (a.Src, St: StaffOf(a.Y))).Where(g => g.Key.St != null && g.All(a => Math.Abs(g.Key.St.Pos(a.Y)) <= 5)))
        {
            var st = grp.Key.St;
            // a key change may cancel the old one with naturals before the new accidentals
            var list = grp.OrderBy(a => a.L).ToList();
            // one source token draws the key on every system: split where the run breaks
            var runs = new List<List<TGlyph>>();
            foreach (var a in list)
                if (runs.Count > 0 && a.L - runs[^1][^1].R < 1.5 * s) runs[^1].Add(a); else runs.Add(new List<TGlyph> { a });
            foreach (var run in runs)
            {
                int sharps = run.Count(a => a.Name == "accidentals.sharp"), flats = run.Count(a => a.Name == "accidentals.flat");
                int fifths = sharps > 0 ? sharps : -flats;
                var f = res.Sym.Sigs.Where(g => g.St == st && g.Fifths != null && Math.Abs(g.X - run[0].L) < 2.5 * s).FirstOrDefault();
                Count("key", f?.Fifths == fifths);
                if (f?.Fifths != fifths) Miss(res, st, $"key: {(f == null ? "none" : f.Fifths.ToString())} for {fifths}", run[0].L, run[0].Y);
            }
        }
        var times = new List<(Staff St, double X, double Y, string Time)>();
        foreach (var grp in music.Where(g => g.Name.Length == 1 || g.Name.StartsWith("timesig.")).GroupBy(g => (g.Src, St: StaffOf(g.Y))))
        {
            if (grp.Key.St == null) continue;
            var st = grp.Key.St;
            var list = grp.OrderBy(g => g.L).ToList();
            var runs = new List<List<TGlyph>>();
            foreach (var g in list)
                if (runs.Count > 0 && g.L - runs[^1].Max(x => x.R) < 1.0 * s) runs[^1].Add(g); else runs.Add(new List<TGlyph> { g });
            foreach (var run in runs)
            {
                string time;
                if (run[0].Name == "timesig.C44") time = "4/4";
                else if (run[0].Name == "timesig.C22") time = "2/2";
                else
                {
                    double mid = st.Mid;
                    string num = string.Concat(run.Where(g => g.Y < mid + 0.3 * s).OrderBy(g => g.L).Select(g => g.Name));
                    string den = string.Concat(run.Where(g => g.Y >= mid + 0.3 * s).OrderBy(g => g.L).Select(g => g.Name));
                    // a multi-bar rest's count: digits over the staff with no source position: the measure
                    // the reader took for that many bars' rest. (With a source position: the 2 over a two-bar
                    // percent repeat, '%2', not scored)
                    if (den == "" && run.All(g => g.B < st.Top + 0.5 * s) && run.Any(g => g.Src >= 0)) continue;
                    if (den == "" && run.All(g => g.Src < 0 && g.B < st.Top + 0.5 * s))
                    {
                        var sys = res.Systems.FirstOrDefault(y => y.Staves.Contains(st));
                        double cx = (run.Min(g => g.L) + run.Max(g => g.R)) / 2;
                        int got = sys == null ? 0 : sys.Rests.Where(r => r.Key < cx && Enumerable.Range(0, sys.Measures).Any(m => sys.Bars[m] == r.Key && cx < sys.Bars[m + 1]))
                            .Select(r => r.Value).FirstOrDefault();
                        Count("mmrest", got.ToString() == num);
                        if (got.ToString() != num) Miss(res, st, $"mmrest: {got} for {num}", cx, run[0].Y);
                        continue;
                    }
                    time = $"{num}/{den}";
                }
                times.Add((st, run.Min(g => g.L), run[0].Y, time));
            }
        }
        foreach (var (st, x, y, time) in times)
        {
            var f = res.Sym.Sigs.Where(g => g.St == st && g.Time != null && Math.Abs(g.X - x) < 2.5 * s).FirstOrDefault();
            Count("time", f?.Time == time);
            if (f?.Time != time) Miss(res, st, $"time: {f?.Time ?? "none"} for {time}", x, y);
        }
        // (a time the measures told, no sign read, is no glyph to be spare)
        foreach (var g in res.Sym.Sigs.Where(g => g.Time != null && !g.Guessed && !times.Any(t => t.St == g.St && Math.Abs(g.X - t.X) < 2.5 * s)))
        {
            Extra["time"] = Extra.GetValueOrDefault("time") + 1;
            Miss(res, g.St, $"time extra ({g.Time})", g.X, g.St.Mid);
        }

        // ---- ties: each starts at a head; that head, when found, must carry the tie, and no other may
        var tied = new HashSet<THead>();
        // A tie starts by its head (measured over the ties suite's 290 ties: 0.2 to 1.8 spaces right
        // of the head's centre, the far ones past a dot; from the head's right edge, -0.5 to 1.1).
        // A chord's ties stack, one to a head, and an inner one lies between two heads, nearer to
        // the wrong one as often as not (ties001's middle tie of three: a third of a space under the
        // top head, two thirds over its own; and the chord's ties do not all start at one x): each
        // goes to the nearest head first, then those that fell on one chord are dealt out again to
        // its heads, one each, in order of height (Symbols.Deal, as the reading deals the detected ends).
        var ties = svg.Ties;
        var startAt = new Dictionary<int, THead>();
        for (int i = 0; i < ties.Count; i++)
        {
            var t = ties[i];
            var from = heads.Where(h => Math.Abs(h.G.Cy - t.Y1) < 1.3 * s && t.X1 - h.G.R > -0.9 * s && t.X1 - h.G.R < 1.4 * s)
                            .OrderBy(h => Math.Abs(h.G.Cy - t.Y1) + 0.3 * Math.Abs(h.G.Cx - t.X1)).FirstOrDefault();
            if (from != null) startAt[i] = from;     // (none: a hanging half at a system's start)
        }
        foreach (var g in startAt.GroupBy(kv => kv.Value.Chord).ToList())
        {
            var chord = heads.Where(h => h.Chord == g.Key).OrderBy(h => h.G.Cy).ToList();
            var onIt = g.Select(kv => kv.Key).OrderBy(i => ties[i].Y1).ToList();
            if (onIt.Count < 2) continue;
            var deal = Symbols.Deal(onIt.Select(i => ties[i].Y1).ToList(), chord.Select(h => h.G.Cy).ToList());
            if (deal != null) for (int k = 0; k < onIt.Count; k++) startAt[onIt[k]] = chord[deal[k]];
        }
        foreach (var (i, from) in startAt.OrderBy(kv => kv.Key).Select(kv => (kv.Key, kv.Value)))
        {
            tied.Add(from);
            bool ok = from.Found != null && from.Found.Tie;
            Count("tie", ok);
            if (!ok) Miss(res, from.St, $"tie missed ({from.G.Name[10..]} pos {from.Pos})", ties[i].X1, ties[i].Y1);
        }
        foreach (var t in heads.Where(t => t.Found != null && t.Found.Tie && !tied.Contains(t)))
        {
            Extra["tie"] = Extra.GetValueOrDefault("tie") + 1;
            Miss(res, t.St, $"tie extra (pos {t.Pos})", t.G.Cx, t.G.Cy);
        }

        // ---- slurs: one runs from the note before its '(' to the note before its ')' (from the
        // source, not the curve: a slur's ends stand over or under its notes, or by a stem's tip).
        // It is read when a head of the first chord was found starting a slur and a head of the
        // last ending one; a slur broken at a system's end (two curves, one source) counts once
        var bySrc = heads.Where(h => h.G.Src >= 0).OrderBy(h => h.G.Src).ToList();
        List<THead> ChordBefore(int src)
        {
            var h = bySrc.LastOrDefault(h => h.G.Src < src);
            return h == null ? new() : heads.Where(o => o.Chord == h.Chord).ToList();
        }
        var slurFrom = new HashSet<int>();
        foreach (var sl in svg.Slurs.GroupBy(t => t.Src).Select(g => g.First()))
        {
            var a = ChordBefore(sl.Src);
            var b = sl.EndSrc >= 0 ? ChordBefore(sl.EndSrc) : new();
            if (a.Count == 0) continue;
            foreach (var h in a) slurFrom.Add(h.Chord);
            bool start = a.Any(h => h.Found != null && h.Found.SlurStart), end = b.Count == 0 || b.Any(h => h.Found != null && h.Found.SlurEnd);
            Count("slur", start && end);
            if (!(start && end)) Miss(res, a[0].St, $"slur {(start ? "end" : end ? "start" : "")} missed (pos {a[0].Pos})".Replace("  ", " "), sl.X1, sl.Y1);
        }
        foreach (var t in heads.Where(t => t.Found != null && t.Found.SlurStart && !slurFrom.Contains(t.Chord)).GroupBy(t => t.Chord).Select(g => g.First()))
        {
            Extra["slur"] = Extra.GetValueOrDefault("slur") + 1;
            Miss(res, t.St, $"slur extra (pos {t.Pos})", t.G.Cx, t.G.Cy);
        }

        // ---- barlines: a tall thin rect; the system's measures lie between them
        foreach (var sys in res.Systems)
        {
            double top = sys.Staves[0].Top, bot = sys.Staves[^1].Bottom;
            double left = sys.Staves.Min(t => t.Left), right = sys.Staves.Max(t => t.Right);
            var tx = new List<double>();
            foreach (var r in svg.Rects.Where(r => r.W < 0.8 * s && r.H > 3 * s && r.Y < bot && r.Y + r.H > top).OrderBy(r => r.X))
            {
                double c = r.X + r.W / 2;
                if (c - left < 3 * s || right - c < 0.8 * s) continue;    // the opening line, the closing one
                if (tx.Count > 0 && c - tx[^1] < 1.0 * s) tx[^1] = c; else tx.Add(c);
            }
            var fx = sys.Bars.Skip(1).Where(b => right - b >= 0.8 * s).ToList();
            var usedB = new HashSet<double>();
            foreach (var c in tx)
            {
                var f = fx.Where(b => !usedB.Contains(b) && Math.Abs(b - c) < 0.8 * s).DefaultIfEmpty(double.NaN).First();
                bool ok = !double.IsNaN(f);
                Count("barline", ok);
                if (ok) usedB.Add(f); else Miss(res, sys.Staves[0], "barline missed", c, top);
            }
            foreach (var b in fx.Where(b => !usedB.Contains(b)))
            {
                Extra["barline"] = Extra.GetValueOrDefault("barline") + 1;
                Miss(res, sys.Staves[0], "barline extra", b, top);
            }
        }
    }

    static string Show(int? acc) => acc switch { null => "none", 1 => "#", -1 => "b", 0 => "n", 2 => "x", -2 => "bb", _ => acc.ToString() };

    /// <summary>A quadrilateral's vertical extent at x (NaN when x is outside it).</summary>
    /// <summary>
    /// Every stem's tip and how many beams or flags it carries (0 to 3), for the detector's
    /// training data (S11): a stem is a thin vertical line longer than a space with a head at one
    /// end; its tip is the other end; a beam is a four-cornered polygon crossing the stem's column
    /// within the room three beams take, a flag a glyph at the tip (as the head check above counts them).
    /// </summary>
    public static List<(double X, double Y, int Tails)> StemTails(SvgPage svg, double s, bool graces = false) =>
        StemTailsSrc(svg, s, graces).Select(t => (t.X, t.Y, t.Tails)).ToList();

    /// <summary>The stems' tips as above, each with the source position of a head on the stem (Verify places it by that).
    /// With <paramref name="graces"/>, the grace notes' stems instead: the small heads' and flags' (S21).</summary>
    public static List<(double X, double Y, int Tails, int Src)> StemTailsSrc(SvgPage svg, double s, bool graces = false)
    {
        var music = svg.Glyphs.Where(g => graces ? g.Small : g.Full).ToList();
        var heads = music.Where(g => g.Name.StartsWith("noteheads.")).ToList();
        var flags = music.Where(g => g.Name.StartsWith("flags.")).ToList();
        var beams = svg.Polys.Where(p => p.Count == 4).ToList();
        var tails = new List<(double X, double Y, int Tails, int Src)>();
        foreach (var l in svg.Lines.Where(l => Math.Abs(l.X1 - l.X2) < 0.5 && Math.Abs(l.Y1 - l.Y2) > 1.5 * s))
        {
            double top = Math.Min(l.Y1, l.Y2), bot = Math.Max(l.Y1, l.Y2);
            // (a rhythm slash's stem hangs from its lower left corner, a space under its centre: S17)
            var mine = heads.Where(h => l.X1 >= h.L - 0.25 * s && l.X1 <= h.R + 0.25 * s
                                        && h.Cy >= top - (h.Name.Contains("slash") ? 1.3 : 0.7) * s && h.Cy <= bot + (h.Name.Contains("slash") ? 1.3 : 0.7) * s).ToList();
            if (mine.Count == 0) continue;
            // the tip is the end away from the heads
            double mid = mine.Average(h => h.Cy);
            bool up = Math.Abs(top - mid) > Math.Abs(bot - mid);
            double tip = up ? top : bot;
            int n = 0;
            foreach (var f in flags.Where(f => Math.Abs(f.X - l.X1) < 0.4 * s && Math.Abs(f.Y - tip) < 0.8 * s))
                n = Math.Max(n, f.Name.EndsWith('3') ? 1 : f.Name.EndsWith('4') ? 2 : f.Name.EndsWith('5') ? 3 : 1);
            int crossing = 0;
            foreach (var p in beams)
            {
                var (y0, y1) = PolyAt(p, l.X1);
                if (double.IsNaN(y0) || y1 - y0 > 0.8 * s) continue;
                double ym = (y0 + y1) / 2;
                double into = up ? ym - tip : tip - ym;
                if (into > -0.5 * s && into < 2.4 * s) crossing++;
            }
            tails.Add((l.X1, tip, Math.Min(3, Math.Max(n, crossing)), mine.Max(h => h.Src)));
        }
        return tails;
    }

    static (double, double) PolyAt(List<(double X, double Y)> p, double x)
    {
        double lo = double.MaxValue, hi = double.MinValue;
        for (int i = 0; i < p.Count; i++)
        {
            var a = p[i]; var b = p[(i + 1) % p.Count];
            if (a.X == b.X || x < Math.Min(a.X, b.X) || x > Math.Max(a.X, b.X)) continue;
            double y = a.Y + (b.Y - a.Y) * (x - a.X) / (b.X - a.X);
            lo = Math.Min(lo, y); hi = Math.Max(hi, y);
        }
        return lo == double.MaxValue ? (double.NaN, double.NaN) : (lo, hi);
    }

    public string Summary()
    {
        var sb = new StringBuilder("symbols:");
        foreach (var (k, c) in Counts) sb.Append($" {k} {c.Ok}/{c.All};");
        foreach (var (k, n) in Extra) sb.Append($" {k}+ {n};");
        return sb.ToString();
    }
}
