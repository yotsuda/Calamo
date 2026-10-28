namespace Omr;

/// <summary>
/// Articulations (M3, S15), from what the learnt detector sees: staccato, accent, tenuto, marcato,
/// staccatissimo and the fermata; and the ornaments (S23): the trill, mordent, prall, turn and
/// reverse turn, which Lily# sets above the staff whatever the stem. Each is put on the chord it
/// stands at, in every list of the part's events (a bar's events and its voices' are copies of one
/// another), as the .lys writes them: @staccato, @accent, @trill, @reverseTurn ...
///
/// Whose a mark is: the chord whose heads its x is centred on (Lily# centres an articulation on its
/// note's head), and among those the one it stands nearest above or below, by its outer head or its
/// stem's tip (a mark opposite the stem stands by the head, one on the stem's side beyond the tip).
/// </summary>
static class Articulations
{
    public static bool Verbose;

    // the detector's classes and the names the .lys writes
    static readonly Dictionary<string, string> Names = new()
    {
        ["art_staccato"] = "staccato", ["art_accent"] = "accent", ["art_tenuto"] = "tenuto",
        ["art_marcato"] = "marcato", ["art_staccatissimo"] = "staccatissimo", ["fermata"] = "fermata",
        ["orn_trill"] = "trill", ["orn_mordent"] = "mordent", ["orn_prall"] = "prall", ["orn_turn"] = "turn", ["orn_reverseturn"] = "reverseTurn",
        ["art_portato"] = "portato", ["art_stopped"] = "stopped", ["orn_prallprall"] = "pralltriller",
    };
    public static bool IsOrnament(Detection t) => t.Class.StartsWith("orn_");

    public static void Apply(PageResult res, IReadOnlyList<Tempo.Mark> words = null)
    {
        if (res.Sym.Detections == null) return;
        double s = res.S;
        var marks = res.Sym.Detections.Where(t => t.Score > Detector.Threshold && Names.ContainsKey(t.Class)).OrderBy(t => t.X).ToList();
        // (no tenuto within a tempo marking's words: the = of its metronome mark is one to the detector,
        // over the note under it: the Lab's piano-collide, "Meno mosso (♩ = 96)". Tenutos only: the OCR
        // may take in what follows the words, the quartet-collide's bar number and an accent on it)
        var tempos = (words ?? []).Where(m => m.IsTempo).Select(m => m.Box).ToList();
        foreach (var t in marks.Where(t => t.Class == "art_tenuto" && tempos.Any(b => b.Contains((float)t.X, (float)t.Y))).ToList())
        {
            marks.Remove(t);
            if (Verbose) res.Notes.Add($"articulation {Names[t.Class]} {t.Score:F2} at {t.X:F0},{t.Y:F0}: within a tempo marking");
        }
        // (no tenuto that is a ledger line: the lone ledger line between a high note's head and the staff
        // is a short stroke to the detector, 0.53 in piano-collide's bar 5, and went on the other voice's
        // note under it. A tenuto is a stroke about a space long; a ledger line runs two spaces and more)
        foreach (var t in marks.Where(t => t.Class == "art_tenuto" && Stroke(res.Dark, t, s) is var run && run > 1.6 * s).ToList())
        {
            marks.Remove(t);
            if (Verbose) res.Notes.Add($"articulation {Names[t.Class]} {t.Score:F2} at {t.X:F0},{t.Y:F0}: a stroke {Stroke(res.Dark, t, s) / s:F1} spaces long, a ledger line");
        }
        // (one mark seen as two kinds at one place is the kind seen more surely: piano004's staccato at 0.88
        // and a staccatissimo at 0.52 on it; a portato's dot, off its centre, seen as a staccato a third of a
        // space from it: piano004's at 0.50 in gen-s2)
        static bool Dot(Detection a, Detection b) => a.Class == "art_portato" && b.Class == "art_staccato" || b.Class == "art_portato" && a.Class == "art_staccato";
        marks = marks.Where(t => !marks.Any(o => o.Class != t.Class && o.Score > t.Score && Math.Abs(o.X - t.X) < 0.3 * s && Math.Abs(o.Y - t.Y) < (Dot(o, t) ? 0.6 : 0.3) * s)).ToList();
        // (a trill's wavy line after its "tr": a trill line, @startTrillSpan; an ornament the detector sees
        // in the wave, a prall's or a mordent's zigzag, is the wave's)
        var waves = new Dictionary<Detection, (double X0, double X1, double Y)>();
        var ink = InkOf(res);
        // (a "tr" seen unsurely, from a fifth, when a wave follows it)
        var weak = res.Sym.WeakTrills;
        foreach (var t in marks.Where(t => t.Class == "orn_trill").Concat(weak).ToList())
        {
            // (the detector's centre of a "tr" wanders: its wave begins half a space to a space past it)
            // (joined to the "tr"'s r, not the next note's ornament after a gap; a long one with its
            // crests a space apart)
            var w = Wave(ink, t.X + 0.3 * s, t.X + 1.6 * s, t.Y, s);
            // (to before the next line's "tr", whose curl it runs into: piano004's line ending where the next starts)
            if (w is { } cut && marks.Concat(weak).Where(o => o != t && o.Class == "orn_trill" && o.X > cut.X0 + s && o.X - s < cut.X1 && Math.Abs(o.Y - t.Y) < 0.8 * s)
                                     .Select(o => o.X - 1.5 * s).DefaultIfEmpty(double.MaxValue).Min() is var stop && stop < cut.X1)
                w = (cut.X0, stop);
            // (one element past the "tr" measures some two spaces; the r's own tail half a space)
            if (w is { } v && v.X1 - Math.Max(v.X0, t.X + 0.66 * s) >= 1.4 * s && Joined(ink, v.X0, t.Y, s)
                && (v.X1 - v.X0 < 1.6 * s || Regular(ink, v.X0, v.X1, t.Y, s, 2, tolerant: true)))
            {
                waves[t] = (v.X0, v.X1, t.Y);
                if (!marks.Contains(t)) marks.Add(t);
            }
            else if (Verbose) res.Notes.Add($"trill {t.Score:F2} at {t.X:F0},{t.Y:F0}: " + (w is { } u ? $"a wave {u.X0:F0} to {u.X1:F0} only" + (Joined(ink, u.X0, t.Y, s) ? "" : ", not joined to the tr") + (Regular(ink, u.X0, u.X1, t.Y, s, 2, tolerant: true) ? "" : $", not regular (crests {LastCrests})") : "no wave"));
        }
        // (or in a wave that runs on further than a prall's zigzag, a line's continuation over the next system)
        // (a zigzag: a prall's or a mordent's, not a turn's; twice its own length, the pralltriller's two and
        // a half spaces: its own zigzag and a stem's ink beside it ran 3.5, ledger005's bar 15 in gen-s2)
        bool InWave(Detection t) => waves.Values.Any(w => t.X > w.X0 && t.X < w.X1 + 0.3 * s && Math.Abs(t.Y - w.Y) < 0.8 * s)
                                    || t.Class is "orn_prall" or "orn_prallprall" or "orn_mordent" && Wave(ink, t.X - 0.3 * s, t.X + 0.3 * s, t.Y, s, back: true) is { } a
                                       && a.X1 - a.X0 > (t.Class == "orn_prallprall" ? 5 : 3) * s
                                       // (its crests a space apart, as the trill element's: not turns and mordents on notes side by side)
                                       && Regular(ink, a.X0, a.X1, t.Y, s, 4);
        var orphans = new List<Detection>();
        foreach (var t in marks.Where(t => IsOrnament(t) && t.Class != "orn_trill" && InWave(t)).ToList())
        {
            marks.Remove(t);
            if (Verbose) res.Notes.Add($"articulation {Names[t.Class]} {t.Score:F2} at {t.X:F0},{t.Y:F0}: within a trill's wave"
                                       + (Wave(ink, t.X - 0.3 * s, t.X + 0.3 * s, t.Y, s, back: true) is { } e ? $" ({e.X0:F0} to {e.X1:F0})" : ""));
            if (!waves.Values.Any(w => t.X > w.X0 && t.X < w.X1 + 0.3 * s && Math.Abs(t.Y - w.Y) < 0.8 * s)) orphans.Add(t);
        }
        // (a wave no "tr" the detector saw stands after: the "tr" it missed, when its ink is there just
        // before the wave and the wave does not begin a system, as a line's continuation does)
        foreach (var t in orphans)
        {
            if (Wave(ink, t.X - 0.3 * s, t.X + 0.3 * s, t.Y, s, back: true) is not { } a
                || waves.Values.Any(w => w.X0 < a.X1 && a.X0 < w.X1 && Math.Abs(w.Y - t.Y) < 0.8 * s)) continue;
            // (or the rest of a line's wave past something crossing it: piano005's left-hand line through the
            // right hand's lower voice's beam, which Lily# does not keep it clear of)
            if (waves.Keys.Where(k => waves[k].X1 < a.X0 && a.X0 - waves[k].X1 < 1.5 * s && Math.Abs(waves[k].Y - t.Y) < 0.3 * s).MaxBy(k => waves[k].X1) is { } line)
            {
                if (Verbose) res.Notes.Add($"trill line at {line.X:F0},{line.Y:F0}: its wave on past {waves[line].X1:F0}, {a.X0:F0} to {a.X1:F0}");
                waves[line] = (waves[line].X0, a.X1, waves[line].Y);
                continue;
            }
            var sys = res.Systems.Where(q => q.Measures > 0 && q.Staves.Count > 0 && a.X0 > q.Bars[0] && a.X0 < q.Bars[^1] && q.Staves[0].Top > t.Y - s)
                                 .MinBy(q => q.Staves[0].Top - t.Y);
            if (sys == null || res.Sym.Heads.Where(h => h.St == sys.Staves[0] && !h.Grace).Select(h => h.L).DefaultIfEmpty(sys.Bars[^1]).Min() > a.X0) continue;
            if (!TrInk(res.Dark, a.X0, t.Y, s)) continue;
            var tr = new Detection { Class = "orn_trill", X = a.X0 - 0.7 * s, Y = t.Y, Score = t.Score };
            waves[tr] = (a.X0, a.X1, t.Y);
            marks.Add(tr);
            if (Verbose) res.Notes.Add($"trill at {tr.X:F0},{tr.Y:F0}: unseen, before the wave {a.X0:F0} to {a.X1:F0}");
        }
        // (a rhythm slash on a one-line staff takes its marks too, /4@accent, /2@fermata: S17)
        var heads = res.Sym.Heads.Where(h => !h.Cross && (!h.Slash || h.St.OneLine)).ToList();
        // the chord of each mark: by its own place, or (a mark stacked on another, as Lily# sets two
        // marks of one note outward from it: an accent over a staccato) the mark's next to it inward
        var chordOf = new Dictionary<Detection, List<Head>>();
        var left = new List<Detection>();
        foreach (var t in marks)
        {
            // (a fermata is wider: centred on its note too, over a whole note's wider head; an ornament likewise)
            double reach = (t.Class == "fermata" || IsOrnament(t) ? 1.2 : 0.9) * s;
            // the chords at its x: heads on one stem (or stemless heads at one x), by their nearest end to it
            var column = heads.Where(h => Math.Abs(h.X - t.X) < reach).ToList();
            var chords = column.GroupBy(h => (h.St, Key: h.HasStem ? Math.Round(h.StemX / (0.3 * s)) * 10 + (h.Up ? 1 : 0) : Math.Round(h.X / (0.35 * s)) * 10 + 5))
                .Select(g => (Heads: g.ToList(), Dist: Gap(g.ToList(), t.Y))).OrderBy(c => c.Dist).ToList();
            // (an ornament stands over its note whatever the stem, unless the note is a lower voice's, whose
            // ornament Lily# sets under it: piano005's trill over a left-hand chord, under the right hand's
            // single-voice chord whose stem reached down to it, nearer to its tip than to the chord's top head)
            if (IsOrnament(t))
            {
                bool Under(List<Head> c) => c.Max(h => h.HasStem ? Math.Max(h.Y, h.Tip) : h.Y) > t.Y;
                var over = chords.Where(c => Under(c.Heads) || LowerVoice(res, c.Heads[0], s)).ToList();
                // (a trill line between a grand staff's staves: the lower staff's, over its note, before the upper
                // staff's lower voice's, under it -- Lily# draws both so, the one as likely as far; a line is
                // mostly the upper voice's or a lone voice's: piano003's left-hand line over a down-stemmed beam)
                if (waves.ContainsKey(t) && over.Where(c => Under(c.Heads)).ToList() is { Count: > 0 } plain && plain[0].Dist <= 10 * s) over = plain;
                if (over.Count > 0) chords = over;
            }
            // (a trill line's "tr" may stand far over its note: Lily# raises the whole line over what is under
            // any of it, a tuplet's number further on)
            if (chords.Count > 0 && chords[0].Dist <= (waves.ContainsKey(t) ? 10 : 2.5) * s) chordOf[t] = chords[0].Heads;
            else
            {
                left.Add(t);
                if (Verbose) res.Notes.Add($"articulation {Names[t.Class]} {t.Score:F2} at {t.X:F0},{t.Y:F0}: no note" + (chords.Count > 0 ? $" (the nearest {chords[0].Dist / s:F1} spaces)" : ""));
            }
        }
        for (bool more = true; more;)
        {
            more = false;
            foreach (var t in left.ToList())
            {
                var inner = chordOf.Keys.Where(o => Math.Abs(o.X - t.X) < 0.5 * s && Math.Abs(o.Y - t.Y) < 1.8 * s).OrderBy(o => Math.Abs(o.Y - t.Y)).FirstOrDefault();
                if (inner == null) continue;
                chordOf[t] = chordOf[inner];
                left.Remove(t);
                more = true;
                if (Verbose) res.Notes.Add($"articulation {Names[t.Class]} at {t.X:F0},{t.Y:F0}: stacked on the {Names[inner.Class]} at {inner.X:F0},{inner.Y:F0}");
            }
        }
        // (Lily# since ba4120841 prints, as LilyPond does, the "tr" again at the start of a line's continuation
        // over the next system, centred on the first note it covers there -- before that it drew the wave
        // alone. That "tr" and its wave are the line going on, not a line of their own: a trill mark on the
        // first chord of a part's staff in a system, when the same part's line in the system before, or on
        // the page before, ran out to its end. Its wave is kept for WaveEnd, and it is put as no mark)
        var contAt = new Dictionary<Staff, (double X0, double X1)>();
        var staffOf = waves.Keys.Where(chordOf.ContainsKey).ToDictionary(k => k, k => chordOf[k][0].St);
        foreach (var k in staffOf.Keys.OrderBy(k => k.Y).ThenBy(k => k.X).ToList())
        {
            var st = staffOf[k];
            var sys = res.Systems.FirstOrDefault(q => q.Staves.Contains(st));
            if (sys == null) continue;
            double first = res.Sym.Heads.Where(h => h.St == st && !h.Grace).Select(h => h.L).DefaultIfEmpty(double.MaxValue).Min();
            if (chordOf[k].Min(h => h.L) > first + 0.5 * s) continue;
            bool goingOn;
            if (Continuation.Next(res, sys, st.Part, out var prev, out var on, by: -1))
                goingOn = staffOf.Where(p => p.Key != k && p.Value == on).Any(p => RunsOut(res, on, prev, waves[p.Key].X1, s));
            else goingOn = Continuation.First(res, st.Part, out var fs, out _) && fs == sys && Continuation.Going(res, "trill").Any(o => o.Part == st.Part && o.RunsOut);
            if (!goingOn) continue;
            contAt[st] = (waves[k].X0, waves[k].X1);
            chordOf.Remove(k);
            if (Verbose) res.Notes.Add($"trill {k.Score:F2} at {k.X:F0},{k.Y:F0}: the \"tr\" of a line's continuation over this system, its wave {waves[k].X0:F0} to {waves[k].X1:F0}");
        }
        foreach (var t in marks.Where(chordOf.ContainsKey))
        {
            string name = Names[t.Class];
            var chord = chordOf[t];
            var evs = Events(res, chord[0], s);
            // (a trill line: from its note to the note its wave ends before)
            Head end = null;
            bool off = false, runsOut = false;
            if (waves.TryGetValue(t, out var wave))
            {
                // (a wave of one element, Lily#'s least, may run on over the next note's head: ledger002's
                // line over two sixteenths, the second a ledger line down; it ends on the note after its own)
                // (the end in the line's own voice, its note's height: piano004's line over the upper voice ending
                // over the lower voice's note in its column; and on the note the next line starts on, not on with
                // that line over the next system)
                var starts = waves.Keys.Where(k => k != t && chordOf.ContainsKey(k)).SelectMany(k => chordOf[k]).ToHashSet();
                end = WaveEnd(res, chord[0].St, wave.X1, s, out off, out runsOut, wave.X1 - wave.X0 < 2.6 * s ? chord.Max(h => h.R) : double.NaN, chord.Average(h => h.Y), starts, contAt);
                if (Verbose) res.Notes.Add($"trill line {t.Score:F2} at {t.X:F0},{t.Y:F0}: the wave {wave.X0:F0} to {wave.X1:F0}, "
                                           + (end == null ? "no note it ends at" : $"ending at the note at {end.X:F0},{end.Y:F0}") + (off ? ", on past the page's end" : ""));
            }
            if (end != null || off) name = "startTrillSpan";
            if (Verbose) res.Notes.Add($"articulation {name} {t.Score:F2} at {t.X:F0},{t.Y:F0}: part {chord[0].St.Part}, " + (evs.Count == 0 ? "no event" : $"the note at {evs[0].X:F0}"));
            // (a line run out past the page's last system: on the next page, or as it is without one)
            if (off)
            {
                var here = res;
                var fallback = end;
                res.Open.Add(new Continuation.Open("trill", chord[0].St.Part, t.X, (at, e) => { if (e != null) PutLine(evs, at, e); else PutLine(evs, here, fallback, Names[t.Class]); }) { RunsOut = runsOut });
            }
            else if (end != null) PutLine(evs, res, end);
            else Put(evs, name);
        }
        // the trill lines the page before ran out past its last system: their wave over the part's staff
        // in this page's first system, as over a next system's (no "tr" before it)
        foreach (var o in Continuation.Going(res, "trill"))
        {
            if (!Continuation.First(res, o.Part, out var sys, out var on)) continue;
            double lead = res.Sym.Heads.Where(h => h.St == on && !h.Grace).Select(h => h.L).DefaultIfEmpty(sys.Bars[^1]).Min();
            if ((ContinuedWave(res, on, sys, lead, s) ?? (contAt.TryGetValue(on, out var c) ? c : null)) is not { } cont)
            {
                // (none drawn: a line run out to the end of the page before ends on this page's first note,
                // its piece here too short to draw: piano003's bar 13 in seed 11)
                if (o.RunsOut && res.Sym.Heads.Where(h => h.St == on && !h.Grace).MinBy(h => h.L) is { } first)
                {
                    if (Verbose) res.Notes.Add($"trill line from the page before: no wave, run out there, ending at the first note at {first.X:F0},{first.Y:F0}");
                    o.Close(res, first);
                }
                continue;
            }
            var end = WaveEnd(res, on, cont.X1, s, out bool off, out _, contAt: contAt);
            if (Verbose) res.Notes.Add($"trill line from the page before: a wave {cont.X0:F0} to {cont.X1:F0}, " + (end == null ? "no end" : $"ending at the note at {end.X:F0},{end.Y:F0}") + (off ? ", on past the page's end" : ""));
            if (off) o.On(res, end);
            else if (end != null) o.Close(res, end);
        }
    }

    // an articulation's name on a chord's events, once
    static void Put(List<Ev> evs, string name)
    {
        foreach (var e in evs)
        {
            e.Articulations ??= new List<string>();
            if (!e.Articulations.Contains(name)) e.Articulations.Add(name);
        }
    }

    // a trill line from a chord's events to the note it ends at (at: that note's page); with no end, the
    // trill alone (name)
    static void PutLine(List<Ev> evs, PageResult at, Head end, string name = "trill")
    {
        if (end == null) { Put(evs, name); return; }
        foreach (var e in Events(at, end, at.S))
        {
            e.Articulations ??= new List<string>();
            // (before anything else on it: the next line may start on the note this one ends on)
            if (!e.Articulations.Contains("stopTrillSpan")) e.Articulations.Insert(0, "stopTrillSpan");
        }
        Put(evs, "startTrillSpan");
    }

    /// <summary>The page's ink without its staff lines' rows, for following a wave: a staff line runs
    /// as short a stroke up and down as a wave's, and on and on (a reverse turn over the top line).</summary>
    internal readonly record struct Ink(Img D, bool[] Lines)
    {
        public bool this[int x, int y] => D[x, y] && !((uint)y < (uint)Lines.Length && Lines[y]);
    }

    internal static Ink InkOf(PageResult res)
    {
        var lines = new bool[res.Dark.H];
        foreach (var l in res.Systems.SelectMany(q => q.All.Count > 0 ? q.All : q.Staves).SelectMany(st => st.Lines))
            for (int y = Math.Max(0, l.Top - 1); y <= Math.Min(lines.Length - 1, l.Bot + 1); y++) lines[y] = true;
        return new Ink(res.Dark, lines);
    }

    /// <summary>
    /// A trill's wavy line (Lily#'s trill spanner: scripts.trill_element repeated a space apart after
    /// the "tr", joined to its r, about seven tenths of a space high, level with the "tr"'s middle):
    /// from the first column of wave in x0 to xs on, the columns whose ink about y (half a space up,
    /// four tenths down) runs short (a slope of the wave, 0.75 spaces at most) and stays within a space
    /// of it -- a stem or a letter crossing it runs
    /// beyond -- with gaps of a quarter of a space at most (and back from it too, with back). Null for no wave.
    /// </summary>
    static (double X0, double X1)? Wave(Ink d, double x0, double xs, double y, double s, bool back = false)
    {
        int lo = (int)Math.Round(y - 0.55 * s), hi = (int)Math.Round(y + 0.45 * s);
        bool Good(int x) => WaveColumn(d, x, y, s);
        double Top(int x)
        {
            for (int r = lo; r <= hi; r++) if (d[x, r]) return r;
            return hi;
        }
        int start = -1;
        for (int x = (int)x0; x <= xs && start < 0; x++) if (Good(x)) start = x;
        if (start < 0) return null;
        int last = start;
        // (and turning: the wave's top rises and falls each half a space, a beam's or a ledger line's
        // runs on straight; the walk stops a space and a fifth past the wave's last turn)
        double peak = Top(start), h = 0.1 * s;
        int dir = 0, turn = start;
        for (int x = start + 1; x - last <= 0.25 * s; x++)
        {
            if (!Good(x)) continue;
            double v = Top(x);
            if (dir == 0) { if (Math.Abs(v - peak) > h) { dir = Math.Sign(v - peak); peak = v; turn = x; } }
            else if ((v - peak) * dir > 0) peak = v;
            else if ((peak - v) * dir > h) { dir = -dir; peak = v; turn = x; }
            if (x - turn > 1.2 * s) { last = Math.Min(last, turn + (int)(0.6 * s)); break; }
            last = x;
        }
        if (back)
            for (int x = start - 1, first = start; first - x <= 0.25 * s; x--) if (Good(x)) start = first = x;
        return (start, last);
    }

    // whether a column's ink about y is a wave's: some, each run short (a slope of the wave) and within
    // a space of y (a stem, a barline or a letter crossing it runs beyond)
    static bool WaveColumn(Ink d, int x, double y, double s)
    {
        int lo = (int)Math.Round(y - 0.55 * s), hi = (int)Math.Round(y + 0.45 * s);
        bool any = false;
        for (int r = lo; r <= hi; r++)
        {
            if (!d[x, r]) continue;
            int a = r, b = r;
            while (d[x, a - 1]) a--;
            while (d[x, b + 1]) b++;
            if (b - a + 1 > 0.85 * s || a < y - 0.9 * s || b > y + 0.8 * s) return false;
            any = true;
            r = b;
        }
        return any;
    }

    /// <summary>
    /// The note a trill line ends on (its @stopTrillSpan): Lily# runs the wave in whole elements to
    /// before that note's column, a quarter of a space to a space short of its head. A wave run out to
    /// the system's end goes on in the next system over the same part's staff, from before its first
    /// note, without the "tr": its end is read there. So does one whose wave ends before the system's
    /// last note when a continuation begins the next (Lily# ends the first part before the last
    /// column: piano005's bar 4); with none, a wave run out to the end ends on the next system's first
    /// note. One run out past the page's last system (off) may go on on the next page: what it returns
    /// is its end should none, and runsOut whether its wave runs out to the end (no end on the page: it
    /// ends on the next page's first note should that page draw no continuation). With past (the right
    /// of the line's own note), any head past it may be the end.
    /// Of the heads in the end's column, the one nearest y (the line's note's height); a note another
    /// line starts on (starts) is the end, the system's last or not. A continuation may be drawn with
    /// its own "tr" on the next system's first note (contAt, by staff: Lily# since ba4120841).
    /// </summary>
    static Head WaveEnd(PageResult res, Staff st, double x1, double s, out bool off, out bool runsOut, double past = double.NaN, double y = double.NaN, HashSet<Head> starts = null,
                        Dictionary<Staff, (double X0, double X1)> contAt = null)
    {
        off = runsOut = false;
        var sys = res.Systems.FirstOrDefault(q => q.Staves.Contains(st));
        if (sys == null) return null;
        for (int hop = 0; hop < 4; hop++)
        {
            double from = hop == 0 && !double.IsNaN(past) ? Math.Min(x1 - 0.3 * s, past) : x1 - 0.3 * s;
            var after = res.Sym.Heads.Where(h => h.St == st && !h.Grace && h.L > from && h.L < sys.Bars[^1]).OrderBy(h => h.L).ToList();
            // (at the system's end: Lily# stops a broken line's wave a space and a half short of it, and no note follows)
            bool runs = sys.Bars[^1] - x1 < 1.5 * s || after.Count == 0;
            var end = runs ? null : after[0];
            if (end != null && hop == 0 && !double.IsNaN(y)) end = after.Where(h => h.L - end.L < 0.5 * s).MinBy(h => Math.Abs(h.Y - y));
            // (a note after the end note within the system: the line ends there)
            if (end != null && after.Any(h => h.L > end.R)) return end;
            if (end != null && starts != null && after.Any(h => h.L - end.L < 0.5 * s && starts.Contains(h))) return end;
            if (!Continuation.Next(res, sys, st.Part, out var next, out var on))
            {
                off = Continuation.LastOnPage(res, sys);
                runsOut = off && end == null;
                return end ?? Continuation.LastHead(res, st, sys);
            }
            // (the continuation: a wave over the staff, starting before its first head)
            double lead = res.Sym.Heads.Where(h => h.St == on && !h.Grace).Select(h => h.L).DefaultIfEmpty(next.Bars[^1]).Min();
            var cont = ContinuedWave(res, on, next, lead, s) ?? (contAt != null && contAt.TryGetValue(on, out var c) ? c : null);
            if (Verbose) res.Notes.Add($"trill line on into system {next.Index + 1}: " + (cont == null ? "no wave" : $"a wave {cont.Value.X0:F0} to {cont.Value.X1:F0}"));
            // (none: the line ends on its last note, or on the next system's first note, Lily# running its wave out to the end of this one)
            if (cont == null) return end ?? res.Sym.Heads.Where(h => h.St == on && !h.Grace).MinBy(h => h.L);
            st = on;
            sys = next;
            x1 = cont.Value.X1;
        }
        return null;
    }

    // whether a line's wave over a system's staff runs out to the system's end (as WaveEnd reads it: within
    // a space and a half of the end, or no note after it): its continuation is drawn over the next system
    static bool RunsOut(PageResult res, Staff st, SystemInfo sys, double x1, double s)
        => sys.Bars[^1] - x1 < 1.5 * s || !res.Sym.Heads.Any(h => h.St == st && !h.Grace && h.L > x1 - 0.3 * s && h.L < sys.Bars[^1]);

    // a trill line's continuation over a staff: a wave a space long and more, beginning before the
    // staff's first head, over the staff (the lowest found, walking the levels up in quarters of a
    // space: it may run into the next line's "tr", so not as a piece of ink by itself)
    static (double X0, double X1)? ContinuedWave(PageResult res, Staff on, SystemInfo sys, double lead, double s)
    {
        // (up to ten spaces, not into the system above: its lower staff's notes)
        double ceiling = on.Top - 10 * s;
        foreach (var above in res.Systems.Where(q => q.Staves.Count > 0 && q.Staves[^1].Bottom < on.Top)) ceiling = Math.Max(ceiling, above.Staves[^1].Bottom + 3 * s);
        if (sys.Staves.Where(x => x.Bottom < on.Top).Select(x => x.Bottom).DefaultIfEmpty(double.MinValue).Max() is var over && over > ceiling) ceiling = over + 0.5 * s;
        for (double y = on.Top - 0.6 * s; y > ceiling; y -= 0.25 * s)
            // (past the clef, whose curl over the staff turns as a wave does: Lily# starts the line after the clef and the key)
            // (and the key: the line starts some two spaces before the first head)
            if (Wave(InkOf(res), Math.Max(sys.Bars[0] + 3.6 * s, lead - 3 * s), lead, y, s) is { } wave && wave.X1 - wave.X0 >= s) return wave;
        return null;
    }

    // whether a run of ink in the column comes into the band about y a wave is looked for in from beyond it (a
    // barline through it, a stem ending in it: piano005's right-hand beam's last stem over the left hand's line)
    static bool Through(Ink d, int x, double y, double s)
    {
        int lo = (int)Math.Round(y - 0.55 * s), hi = (int)Math.Round(y + 0.45 * s);
        for (int r = lo; r <= hi; r++)
        {
            if (!d[x, r]) continue;
            int a = r, b = r;
            while (d[x, a - 1]) a--;
            while (d[x, b + 1]) b++;
            if (a < y - 0.9 * s || b > y + 0.8 * s) return true;
            r = b;
        }
        return false;
    }

    // the top of the ink in a column, about y as Wave looks (half a space up, four tenths down); NaN for none
    static double Top(Ink d, int x, double y, double s)
    {
        int lo = (int)Math.Round(y - 0.55 * s), hi = (int)Math.Round(y + 0.45 * s);
        for (int r = lo; r <= hi; r++) if (d[x, r]) return r;
        return double.NaN;
    }

    // whether a wave from x0 is joined to the ink before it (the "tr"'s r): ink in one of the two columns before
    static bool Joined(Ink d, double x0, double y, double s)
        => !double.IsNaN(Top(d, (int)x0 - 1, y, s)) || !double.IsNaN(Top(d, (int)x0 - 2, y, s));

    /// <summary>
    /// Whether a wave's crests (the top of its ink highest, a tenth of a space over the troughs either
    /// side) stand a space apart, as the trill element repeats: at least so many, and every gap but one
    /// (the first, which may start mid-element, not counted) 0.75 to 1.25 spaces, and all but the first
    /// at one height. With tolerant (a wave after a "tr", which may be a scan's), a crest within four
    /// tenths of a space of the end and one between two an element apart are not counted; not for the
    /// zigzag an ornament is looked for in, which a turn and a mordent side by side would then pass.
    /// </summary>
    static bool Regular(Ink d, double x0, double x1, double y, double s, int least, bool tolerant = false)
    {
        var crests = new List<int>();
        var tops = new List<double>();
        double h = 0.1 * s, ext = double.NaN;
        int dir = -1, extX = 0;
        for (int x = (int)x0; x <= x1; x++)
        {
            // (not a column something comes into the wave in from beyond: a grand staff's barline through the gap,
            // under a lower staff's line. A mordent's upright stays within, and breaks a turn and a mordent side by side)
            if (Through(d, x, y, s)) continue;
            double v = Top(d, x, y, s);
            if (double.IsNaN(v)) continue;
            if (double.IsNaN(ext)) { ext = v; extX = x; continue; }
            if (dir < 0)
            {
                if (v < ext) { ext = v; extX = x; }
                else if (v - ext > h) { crests.Add(extX); tops.Add(ext); dir = 1; ext = v; extX = x; }
            }
            else
            {
                if (v > ext) { ext = v; extX = x; }
                else if (ext - v > h) { dir = -1; ext = v; extX = x; }
            }
        }
        // (nor the last, when it stands within four tenths of a space of the wave's end: a part of an element, or the
        // wave's last slope run into a barline a scan's blur joins it to, lower than the crests -- gen-trill-scan's
        // piano001, its left hand's line in bar 2 ending four pixels under the rest at the bar's end)
        if (tolerant && crests.Count > 0 && x1 - crests[^1] < 0.4 * s) { crests.RemoveAt(crests.Count - 1); tops.RemoveAt(tops.Count - 1); }
        // (nor one crest between two an element apart: a speck of a scan's noise, or a stem's end, on a slope of
        // the wave, which cut one gap in two -- gen-trill-scan's piano005, 006 and 007 after the redraw)
        if (tolerant)
            for (int k = 1; k + 1 < crests.Count; k++)
                if (crests[k] - crests[k - 1] < 0.75 * s && crests[k + 1] - crests[k - 1] >= 0.75 * s && crests[k + 1] - crests[k - 1] <= 1.25 * s) { crests.RemoveAt(k); tops.RemoveAt(k); k--; }
        if (Verbose) LastCrests = string.Join(" ", crests.Zip(tops, (c, t) => $"{c}:{t:F0}"));
        if (crests.Count < least) return false;
        // (and level: the elements' crests at one height, a turn's and a mordent's side by side not)
        if (tops.Skip(1).DefaultIfEmpty(0).Max() - tops.Skip(1).DefaultIfEmpty(0).Min() > 0.15 * s) return false;
        int odd = 0;
        for (int k = 2; k < crests.Count; k++)
            if (crests[k] - crests[k - 1] < 0.75 * s || crests[k] - crests[k - 1] > 1.25 * s) odd++;
        return odd <= 1;
    }
    // (the crests the last Regular found, x:top, for the verbose notes)
    static string LastCrests = "";

    /// <summary>The detector's least score of a "tr" read as a trill line's, a wave after it.</summary>
    public static double WeakTrill = 0.2;

    // whether a "tr" stands just before a wave beginning at x0, level y: ink over a fifth of the box
    // a space and a half wide, a space over to six tenths under, with a stroke of eight tenths of a
    // space and more up and down in it (the t)
    static bool TrInk(Img d, double x0, double y, double s)
    {
        int l = (int)(x0 - 1.5 * s), r = (int)(x0 - 0.1 * s), top = (int)(y - 1.1 * s), bottom = (int)(y + 0.6 * s);
        int ink = 0, tall = 0;
        for (int x = l; x <= r; x++)
        {
            int run = 0;
            for (int yy = top; yy <= bottom; yy++)
            {
                if (d[x, yy]) { ink++; run++; tall = Math.Max(tall, run); } else run = 0;
            }
        }
        return ink > 0.2 * (r - l + 1) * (bottom - top + 1) && tall >= 0.8 * s;
    }

    // the longest run of ink along a row through the mark, within a quarter of a space of its centre
    static double Stroke(Img d, Detection t, double s)
    {
        int best = 0, x = (int)Math.Round(t.X);
        for (int y = (int)(t.Y - 0.25 * s); y <= t.Y + 0.25 * s; y++)
        {
            if (!d[x, y]) continue;
            int l = x, r = x;
            while (d[l - 1, y]) l--;
            while (d[r + 1, y]) r++;
            best = Math.Max(best, r - l + 1);
        }
        return best;
    }

    // how far a mark at y stands from a chord: off its outer heads (above the top one or below the
    // bottom one), or off its stem's tip; or, for a chord within its staff on the mark's side, off
    // the staff's edge (Lily# sets an accent or a marcato outside the staff: piano005's accents over
    // the left hand's staff, 2.7 to 4.2 spaces off their heads)
    static double Gap(List<Head> chord, double y)
    {
        double top = chord.Min(h => h.Y), bottom = chord.Max(h => h.Y);
        double gap = Math.Min(Math.Abs(y - top), Math.Abs(y - bottom));
        var stem = chord.FirstOrDefault(h => h.HasStem);
        if (stem != null)
        {
            gap = Math.Min(gap, Math.Abs(y - stem.Tip));
            top = Math.Min(top, stem.Tip); bottom = Math.Max(bottom, stem.Tip);
        }
        var st = chord[0].St;
        double half = st.S / 2;
        if (y < st.Top && top > st.Top - half) gap = Math.Min(gap, st.Top - y);
        if (y > st.Bottom && bottom < st.Bottom + half) gap = Math.Min(gap, y - st.Bottom);
        return gap;
    }

    // the head's event in each list of its part's bar: the bar's and one of its voices' (Texts too)
    internal static List<Ev> Events(PageResult res, Head h, double s)
    {
        var found = new List<Ev>();
        if (BarOf(res, h) is not int bar) return found;
        if (res.Bars.TryGetValue((bar, h.St.Part), out var evs) && evs.FirstOrDefault(e => Same(e, h, s)) is { } one) found.Add(one);
        if (res.Voices.TryGetValue((bar, h.St.Part), out var vs) && vs.Select(v => v.FirstOrDefault(e => Same(e, h, s))).FirstOrDefault(e => e != null) is { } inVoice)
            found.Add(inVoice);
        return found;
    }

    // whether the head's chord is a lower voice's (the second or a later of its bar's voices)
    static bool LowerVoice(PageResult res, Head h, double s)
        => BarOf(res, h) is int bar && res.Voices.TryGetValue((bar, h.St.Part), out var vs) && vs.Count > 1 && vs.Skip(1).Any(v => v.Any(e => Same(e, h, s)));

    static int? BarOf(PageResult res, Head h)
    {
        var sys = res.Systems.FirstOrDefault(q => q.Staves.Contains(h.St));
        if (sys == null) return null;
        int k = Math.Clamp(sys.Bars.FindLastIndex(b => b <= h.X), 0, sys.Measures - 1);
        return sys.BarOf(k);
    }

    // whether the event is the head's chord: on the same stem, or stemless at the same x
    static bool Same(Ev e, Head h, double s)
    {
        int stem = h.HasStem ? (h.Up ? 1 : -1) : 0;
        return !e.IsRest && !e.FullBar && e.Slash == h.Slash
            && (stem != 0 ? e.Stem == stem && Math.Abs(e.StemX - h.StemX) < 0.3 * s : e.Stem == 0 && Math.Abs(e.X - h.X) < 0.35 * s);
    }
}
