using System.Text.RegularExpressions;

namespace Omr;

/// <summary>
/// The hand transcription (notes.md format): a line "N ..." opens bar N, then "part: music".
/// Only pitch and duration are kept; ties, slurs, dynamics and marks are dropped.
/// </summary>
sealed class Truth
{
    public Dictionary<(int Bar, string Part), List<Ev>> Bars = new();
    public Dictionary<(int Bar, string Part), List<List<Ev>>> Voices = new();

    // an annotation's argument is a quoted string or a number: "@p(" is a dynamic plus a slur
    static readonly Regex Annot = new(@"@!?[A-Za-z]+(\(""[^""]*""\)|\(\d+\))?(\.(up|down))?");
    static readonly Regex Tok = new(@"tuplet\s+(\d+)/(\d+)\s*\{|\}|<([^>]*)>(\d+\.*)?|R(\d+)(\.*)|r(\d+\.*)?|([a-g](?:is|es)*[',]*)(\d+\.*)?");

    public static Truth Load(string path)
    {
        var t = new Truth();
        int bar = 0;
        var lastDur = new Dictionary<string, string>();
        foreach (var raw in File.ReadAllLines(path))
        {
            var mb = Regex.Match(raw, @"^(\d+)\b");
            if (mb.Success) { bar = int.Parse(mb.Groups[1].Value); continue; }
            var mp = Regex.Match(raw, @"^(\w+):\s*(.*)$");
            if (!mp.Success || bar == 0) continue;
            string part = mp.Groups[1].Value;
            string music = Annot.Replace(mp.Groups[2].Value, " ");
            var evs = new List<Ev>();
            var scale = new Stack<(int Num, int Den)>();
            string last = lastDur.GetValueOrDefault(part, "4");
            foreach (Match m in Tok.Matches(music))
            {
                if (m.Value.StartsWith("tuplet")) { scale.Push((int.Parse(m.Groups[2].Value), int.Parse(m.Groups[1].Value))); continue; }
                if (m.Value == "}") { if (scale.Count > 0) scale.Pop(); continue; }
                int Ticks(string d)
                {
                    if (!string.IsNullOrEmpty(d)) last = d;
                    int dots = last.Count(c => c == '.');
                    int ticks = Dur.Of(int.Parse(last.TrimEnd('.')), dots);
                    foreach (var (num, den) in scale) ticks = ticks * num / den;
                    return ticks;
                }
                if (m.Groups[3].Success)
                {
                    var ps = m.Groups[3].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(Pitch.Parse).Where(p => p != null).ToList();
                    var chord = ps.Select(p => p.Value).OrderBy(p => p.Diatonic).ToList();
                    evs.Add(new Ev { P = chord[^1], Chord = chord.Count > 1 ? chord : null, Ticks = Ticks(m.Groups[4].Value) });
                }
                else if (m.Groups[5].Success)
                    evs.Add(new Ev { IsRest = true, FullBar = true, Ticks = Dur.Of(int.Parse(m.Groups[5].Value), m.Groups[6].Value.Length) });
                else if (m.Value.StartsWith("r"))
                    evs.Add(new Ev { IsRest = true, Ticks = Ticks(m.Groups[7].Value) });
                else if (m.Groups[8].Success)
                    evs.Add(new Ev { P = Pitch.Parse(m.Groups[8].Value).Value, Ticks = Ticks(m.Groups[9].Value) });
            }
            lastDur[part] = last;
            t.Bars[(bar, part)] = evs;
        }
        return t;
    }
}

/// <summary>
/// The truth from LilyPond's event listener (a .notes file: time, "note", MIDI pitch, duration,
/// duration as a fraction of a whole). Bars come from the time and a fixed bar length. Pitches
/// are MIDI numbers, so only the sounding pitch is compared, not its spelling. Grace notes
/// (times with a "-grace" part) are left out.
/// </summary>
static class NotesTruth
{
    /// <summary>
    /// Voices: one .notes file per staff and voice (the pattern's {part} is the staff, a "*"
    /// matches the voice). Each file's bar becomes one voice of that staff's bar.
    /// </summary>
    public static void LoadVoices(string pattern, IEnumerable<string> parts, double barWholes, Truth into)
    {
        foreach (var part in parts)
        {
            string p = pattern.Replace("{part}", part);
            string dir = Path.GetDirectoryName(p);
            if (string.IsNullOrEmpty(dir)) dir = ".";
            foreach (var file in Directory.GetFiles(dir, Path.GetFileName(p)).OrderBy(f => f))
            {
                var one = Load(file, part, barWholes);
                foreach (var (key, evs) in one.Bars)
                {
                    if (!into.Voices.TryGetValue(key, out var vs)) into.Voices[key] = vs = new List<List<Ev>>();
                    vs.Add(evs);
                }
            }
        }
    }

    /// <summary>A pickup's length in wholes (--pickup): LilyPond's moments start at the pickup, the
    /// page counts it as its first measure.</summary>
    public static double Pickup;

    /// <summary>The meter's changes after bar 1 (the .score's "bar N time a/b"): bar, its length in
    /// wholes from there on. A song may change its meter halfway.</summary>
    public static SortedDictionary<int, double> Meters = new();

    static int BarOf(double time, double barWholes)
    {
        int bar = 1;
        double start = 0, len = barWholes;
        foreach (var (b, w) in Meters)
        {
            if (b <= bar) continue;
            double at = start + (b - bar) * len;
            if (time < at - 1e-6) break;
            (start, bar, len) = (at, b, w);
        }
        return bar + (int)Math.Floor((time - start) / len + 1e-6);
    }

    public static Truth Load(string path, string part, double barWholes, bool poly = false, Truth into = null)
    {
        var t = into ?? new Truth();
        foreach (var line in File.ReadAllLines(path))
        {
            var f = line.Split('\t');
            if (f.Length < 4 || (f[1] != "note" && f[1] != "rest")) continue;
            if (f[0].IndexOf('-', 1) > 0) continue;   // grace
            double time = double.Parse(f[0], System.Globalization.CultureInfo.InvariantCulture);
            if (Pickup > 0) time += barWholes - Pickup;
            int bar = BarOf(time, barWholes);
            var ev = new Ev { MidiOnly = true };
            if (f[1] == "rest")
            {
                ev.IsRest = true;
                ev.Ticks = (int)Math.Round(double.Parse(f[3], System.Globalization.CultureInfo.InvariantCulture) * Dur.Whole);
            }
            else
            {
                ev.P = Pitch.FromMidi(int.Parse(f[2]));
                ev.Ticks = (int)Math.Round(double.Parse(f[4], System.Globalization.CultureInfo.InvariantCulture) * Dur.Whole);
            }
            if (!t.Bars.TryGetValue((bar, part), out var list)) t.Bars[(bar, part)] = list = new List<Ev>();
            // a chord arrives as notes at one time: keep them as one event
            if (!poly && !ev.IsRest && list.Count > 0 && !list[^1].IsRest && Math.Abs(list[^1].X - time) < 1e-9)
            {
                var last = list[^1];
                last.Chord ??= new List<Pitch> { last.P };
                last.Chord.Add(ev.P);
                last.Chord.Sort((a, b) => a.Midi().CompareTo(b.Midi()));
                last.P = last.Chord[^1];
                continue;
            }
            ev.X = time;
            list.Add(ev);
        }
        return t;
    }
}

/// <summary>
/// The truth from MusicXML (B4a: the pages MuseScore draws from Lily#'s export; the band scores'
/// hand-written .lys exported the same way). Every staff of every part, in order, is one part of
/// the config. Bars are the measures in order (not their printed numbers). Pitches keep their
/// spelling. Grace and cue notes and invisible rests (a spacer's) are left out. The staff's
/// voices go to Voices, chords as one event; with poly, Bars holds each note of a chord on its own
/// (as the .notes truth does).
/// </summary>
static class MusicXmlTruth
{
    /// <param name="barBase">the bars before this file's (pieces read as one: a song cycle printed on through)</param>
    public static Truth Load(string path, IReadOnlyList<string> parts, bool poly, Truth into = null, int barBase = 0)
    {
        var t = into ?? new Truth();
        var doc = System.Xml.Linq.XDocument.Load(path);
        int staffBase = 0;
        foreach (var part in doc.Root.Elements("part"))
        {
            int divisions = 1, staves = 1, bar = barBase;
            foreach (var measure in part.Elements("measure"))
            {
                bar++;
                // per staff and voice: the events in order, and where the voice's time stands
                var voices = new SortedDictionary<(int Staff, int Voice), List<Ev>>();
                int now = 0, lastStart = 0;
                foreach (var el in measure.Elements())
                {
                    switch (el.Name.LocalName)
                    {
                        case "attributes":
                            if (el.Element("divisions") is { } d) divisions = int.Parse(d.Value);
                            if (el.Element("staves") is { } s) staves = int.Parse(s.Value);
                            break;
                        case "backup": now -= Ticks(el); break;
                        case "forward": now += Ticks(el); break;
                        case "note":
                            if (el.Element("grace") != null || el.Element("cue") != null) break;
                            bool chord = el.Element("chord") != null;
                            int ticks = Ticks(el);
                            int start = chord ? lastStart : now;
                            if (!chord) { lastStart = now; now += ticks; }
                            if (el.Element("rest") != null && (string)el.Attribute("print-object") == "no") break;
                            int staff = int.Parse(el.Element("staff")?.Value ?? "1"), voice = int.Parse(el.Element("voice")?.Value ?? "1");
                            if (!voices.TryGetValue((staff, voice), out var list)) voices[(staff, voice)] = list = new List<Ev>();
                            var ev = new Ev { Ticks = ticks, X = start };
                            if (el.Element("rest") is { } rest) { ev.IsRest = true; ev.FullBar = (string)rest.Attribute("measure") == "yes"; }
                            else
                            {
                                var p = el.Element("pitch");
                                int step = "CDEFGAB".IndexOf(p.Element("step").Value[0]);
                                ev.P = new Pitch(int.Parse(p.Element("octave").Value) * 7 + step, (int)Math.Round(double.Parse(p.Element("alter")?.Value ?? "0", System.Globalization.CultureInfo.InvariantCulture)));
                                // (an arpeggio, on each note of its chord: Scorer.AddArpeggios counts it once a chord)
                                if (el.Elements("notations").Any(n => n.Element("arpeggiate") != null)) ev.Articulations = new List<string> { "arpeggio" };
                            }
                            list.Add(ev);
                            break;
                    }
                }
                // (no divisions given: no time -- one of Audiveris's movements, lang36-3-ms's first page, crashed the scoring)
                int Ticks(System.Xml.Linq.XElement e) => divisions <= 0 ? 0 : int.Parse(e.Element("duration")?.Value ?? "0") * Dur.Whole / (4 * divisions);
                for (int s = 1; s <= staves; s++)
                {
                    if (staffBase + s - 1 >= parts.Count) break;
                    string id = parts[staffBase + s - 1];
                    var all = new List<Ev>();
                    foreach (var (_, evs) in voices.Where(v => v.Key.Staff == s))
                    {
                        // one voice's line: a chord's notes (same start) as one event
                        var line = new List<Ev>();
                        foreach (var e in evs)
                        {
                            if (line.Count > 0 && !e.IsRest && !line[^1].IsRest && line[^1].X == e.X)
                            {
                                var last = line[^1];
                                last.Chord ??= new List<Pitch> { last.P };
                                last.Chord.Add(e.P);
                                last.Chord.Sort((a, b) => a.Diatonic.CompareTo(b.Diatonic));
                                last.P = last.Chord[^1];
                                if (e.Articulations != null) last.Articulations ??= e.Articulations;
                            }
                            else line.Add(new Ev { IsRest = e.IsRest, FullBar = e.FullBar, P = e.P, Ticks = e.Ticks, X = e.X, Articulations = e.Articulations });
                        }
                        if (!t.Voices.TryGetValue((bar, id), out var vs)) t.Voices[(bar, id)] = vs = new List<List<Ev>>();
                        vs.Add(line);
                        all.AddRange(poly ? evs : line);
                    }
                    t.Bars[(bar, id)] = all.OrderBy(e => e.X).ToList();   // (stable: a voice's order kept at one time)
                }
            }
            staffBase += staves;
        }
        return t;
    }
}

/// <summary>
/// Which bar of the truth each measure read stands for (--align). Scored by number, or in the order
/// read, one measure lost or one too many (a page another reader read nothing of, a movement split in
/// two) puts every bar after it off by one, and the rest of the piece scores as wrong. The measures
/// read and the truth's bars are matched in order instead, as two lines of events are within a bar:
/// the matching that agrees most over all parts (Scorer.Likeness), a measure on either side free to
/// stand for nothing.
/// </summary>
static class BarAlign
{
    /// <summary>For each measure read, in order, the truth's bar it stands for, or 0.</summary>
    public static int[] Align(Truth truth, IReadOnlyList<int> bars, IReadOnlyList<string> parts, IReadOnlyList<Func<string, List<Ev>>> read)
    {
        int n = bars.Count, m = read.Count;
        var like = new int[n, m];
        for (int i = 0; i < n; i++)
            for (int j = 0; j < m; j++)
                foreach (var p in parts)
                    if (truth.Bars.TryGetValue((bars[i], p), out var t)) like[i, j] += Scorer.Likeness(t, read[j](p) ?? new List<Ev>());
        var dp = new int[n + 1, m + 1];
        for (int i = n - 1; i >= 0; i--)
            for (int j = m - 1; j >= 0; j--)
                dp[i, j] = Math.Max(Math.Max(dp[i + 1, j], dp[i, j + 1]), dp[i + 1, j + 1] + like[i, j]);
        var map = new int[m];
        int a = 0, b = 0;
        while (a < n && b < m)
        {
            if (like[a, b] > 0 && dp[a, b] == dp[a + 1, b + 1] + like[a, b]) { map[b] = bars[a]; a++; b++; }
            else if (dp[a, b] == dp[a + 1, b]) a++;
            else b++;
        }
        return map;
    }
}

/// <summary>Aligns OMR events with the truth, bar by bar, and counts what agrees.</summary>
sealed class Scorer
{
    public int TruthEvents, OmrEvents, Exact, PitchOk, DurOk, Aligned, Bars, BarsExact;
    public List<string> Diffs = new();
    public HashSet<(int Bar, string Part)> Wrong = new();    // the part-bars that are not exact (for Verify)

    // arpeggios: a chord's, matched within the part-bar by a note they share (a real print's truth: its MusicXML's arpeggiate)
    public int ArpTruth, ArpOmr, ArpFound, ArpRight;
    public List<string> ArpDiffs = new();

    /// <summary>The arpeggios of a part-bar: one per chord (the events on one stem, or at one x: one per head with --poly), as the notes marked.</summary>
    static List<HashSet<int>> Arpeggios(List<Ev> evs) =>
        evs.Where(e => !e.IsRest && e.Articulations?.Contains("arpeggio") == true).GroupBy(e => e.Stem != 0 ? e.StemX : e.X)
           .Select(g => g.SelectMany(e => e.Chord ?? new List<Pitch> { e.P }).Select(p => p.Midi()).ToHashSet()).ToList();

    void AddArpeggios(int bar, string part, List<Ev> truth, List<Ev> omr)
    {
        var t = Arpeggios(truth);
        var o = Arpeggios(omr);
        // (with --poly the reader marks a chord's arpeggio on some of its heads' events: a note in common is the chord;
        // and a chord the reader splits into two voices has its wave on each: one truth chord found, both read right)
        // (found: one to one, a bar's chords repeated over one wave read are found once)
        var used = new bool[o.Count];
        int found = 0, right = o.Count(k => t.Any(c => c.Overlaps(k)));
        foreach (var c in t)
            for (int j = 0; j < o.Count; j++)
                if (!used[j] && c.Overlaps(o[j])) { used[j] = true; found++; break; }
        ArpTruth += t.Count; ArpOmr += o.Count; ArpFound += found; ArpRight += right;
        string Show(List<HashSet<int>> a) => string.Join(" ", a.Select(h => string.Join(",", h.Order())));
        if (found < t.Count || right < o.Count) ArpDiffs.Add($"{bar,4} {part,-5} arpeggios: truth {t.Count}, read {o.Count}, found {found}, read right {right} ({Show(t)} / {Show(o)})");
    }

    public string ArpeggioSummary()
    {
        double P(int x, int of) => of == 0 ? 0 : 100.0 * x / of;
        return $"arpeggios: truth {ArpTruth}, omr {ArpOmr}, found {ArpFound}, read right {ArpRight} (recall {P(ArpFound, ArpTruth):F1}%, precision {P(ArpRight, ArpOmr):F1}%)";
    }

    public void Add(int bar, string part, List<Ev> truth, List<Ev> omr)
    {
        AddArpeggios(bar, part, truth, omr);
        Bars++;
        TruthEvents += truth.Count;
        OmrEvents += omr.Count;
        int n = truth.Count, m = omr.Count;
        var dp = new int[n + 1, m + 1];
        for (int i = n - 1; i >= 0; i--)
            for (int j = m - 1; j >= 0; j--)
                dp[i, j] = Math.Max(Math.Max(dp[i + 1, j], dp[i, j + 1]), dp[i + 1, j + 1] + Score(truth[i], omr[j]));
        int a = 0, b = 0, exact = 0;
        while (a < n && b < m)
        {
            int sc = Score(truth[a], omr[b]);
            if (sc > 0 && dp[a, b] == dp[a + 1, b + 1] + sc)
            {
                Aligned++;
                bool pOk = SamePitch(truth[a], omr[b]), dOk = SameDur(truth[a], omr[b]);
                if (pOk) PitchOk++;
                if (dOk) DurOk++;
                if (pOk && dOk) { Exact++; exact++; }
                a++; b++;
            }
            else if (dp[a, b] == dp[a + 1, b]) a++;
            else b++;
        }
        if (exact == n && n == m) BarsExact++;
        else { Wrong.Add((bar, part)); Diffs.Add($"{bar,4} {part,-4} T: {string.Join(" ", truth.Select(e => e.Show()))}\n          O: {string.Join(" ", omr.Select(e => e.Show()))}"); }
    }

    static bool SamePitch(Ev t, Ev o)
    {
        if (t.IsRest != o.IsRest) return false;
        if (t.IsRest) return true;
        if (t.MidiOnly)
        {
            var a = t.Chord ?? new List<Pitch> { t.P };
            var b = (o.Chord ?? new List<Pitch> { o.P }).OrderBy(p => p.Midi()).ToList();
            return a.Select(p => p.Midi()).SequenceEqual(b.Select(p => p.Midi()));
        }
        return t.Chord == null && o.Chord == null ? t.P == o.P : t.Chord != null && o.Chord != null && t.Chord.SequenceEqual(o.Chord);
    }
    static bool SameDur(Ev t, Ev o) => (t.FullBar && o.FullBar) || t.Ticks == o.Ticks;
    static int Score(Ev t, Ev o) => (SamePitch(t, o) ? 2 : 0) + (SameDur(t, o) ? 1 : 0) + (t.IsRest == o.IsRest ? 1 : 0);

    // multiset scoring: for several voices on a staff, the order of events is not the question
    public int MsTruth, MsOmr, MsExact, MsPitch;

    static string Key(Ev e, bool dur) => (e.IsRest ? "r" : e.P.Midi().ToString()) + (dur ? ":" + e.Ticks : "");
    static int Common(List<Ev> truth, List<Ev> omr, bool dur)
    {
        var bag = truth.GroupBy(e => Key(e, dur)).ToDictionary(g => g.Key, g => g.Count());
        int n = 0;
        foreach (var e in omr)
            if (bag.TryGetValue(Key(e, dur), out int c) && c > 0) { bag[Key(e, dur)] = c - 1; n++; }
        return n;
    }

    /// <summary>How much two measures' events agree, in any order: each event found with its pitch and
    /// duration counts 2, with its pitch only 1 (BarAlign's likeness).</summary>
    public static int Likeness(List<Ev> truth, List<Ev> omr) =>
        truth.Count == 0 || omr.Count == 0 ? 0 : Common(truth, omr, true) + Common(truth, omr, false);

    /// <summary>A measure read that stands for none of the truth's (BarAlign left it over): all its events are extra.</summary>
    public void AddExtra(int at, string part, List<Ev> omr)
    {
        if (omr.Count == 0) return;
        AddArpeggios(at, part, new List<Ev>(), omr);
        OmrEvents += omr.Count; MsOmr += omr.Count;
        Diffs.Add($"  +{at,-3} {part,-5} O: {string.Join(" ", omr.Select(e => e.Show()))}   (read measure {at}: no bar of the truth)");
    }

    public void AddMultiset(int bar, string part, List<Ev> truth, List<Ev> omr)
    {
        AddArpeggios(bar, part, truth, omr);
        Bars++;
        int exact = Common(truth, omr, true), pitch = Common(truth, omr, false);
        MsTruth += truth.Count; MsOmr += omr.Count; MsExact += exact; MsPitch += pitch;
        if (exact == truth.Count && omr.Count == truth.Count) BarsExact++;
        else { Wrong.Add((bar, part)); Diffs.Add($"{bar,4} {part,-5} T: {string.Join(" ", truth.OrderBy(e => e.IsRest).ThenBy(e => e.IsRest ? 0 : e.P.Midi()).Select(e => e.Show()))}\n           O: {string.Join(" ", omr.OrderBy(e => e.IsRest).ThenBy(e => e.IsRest ? 0 : e.P.Midi()).Select(e => e.Show()))}"); }
    }

    // voices: each truth voice matched with the OMR voice that agrees with it best
    public int VBars, VBarsExact, VCountOk, VTruth, VOmr, VExact;
    public List<string> VDiffs = new();

    public void AddVoices(int bar, string part, List<List<Ev>> t, List<List<Ev>> o)
    {
        VBars++;
        VTruth += t.Sum(v => v.Count);
        VOmr += o.Sum(v => v.Count);
        if (t.Count == o.Count) VCountOk++;
        var score = new int[t.Count, o.Count];
        for (int i = 0; i < t.Count; i++)
            for (int j = 0; j < o.Count; j++) score[i, j] = ExactMatches(t[i], o[j]);
        int best = 0;
        var used = new bool[o.Count];
        void Walk(int i, int sum)
        {
            if (i == t.Count) { best = Math.Max(best, sum); return; }
            Walk(i + 1, sum);
            for (int j = 0; j < o.Count; j++)
                if (!used[j]) { used[j] = true; Walk(i + 1, sum + score[i, j]); used[j] = false; }
        }
        Walk(0, 0);
        VExact += best;
        int tn = t.Sum(v => v.Count), on = o.Sum(v => v.Count);
        if (t.Count == o.Count && best == tn && on == tn) VBarsExact++;
        else
        {
            string Show(List<List<Ev>> vs) => string.Join("  //  ", vs.Select(v => string.Join(" ", v.Select(e => e.Show()))));
            VDiffs.Add($"{bar,4} {part,-5} T: {Show(t)}\n           O: {Show(o)}");
        }
    }

    /// <summary>Events that agree (pitch and duration) along the best alignment of two lines.</summary>
    static int ExactMatches(List<Ev> t, List<Ev> o)
    {
        int n = t.Count, m = o.Count;
        var dp = new int[n + 1, m + 1];
        for (int i = n - 1; i >= 0; i--)
            for (int j = m - 1; j >= 0; j--)
                dp[i, j] = Math.Max(Math.Max(dp[i + 1, j], dp[i, j + 1]), dp[i + 1, j + 1] + (SamePitch(t[i], o[j]) && SameDur(t[i], o[j]) ? 1 : 0));
        return dp[0, 0];
    }

    public string VoiceSummary()
    {
        double P(int x, int of) => of == 0 ? 0 : 100.0 * x / of;
        return $"voices: staff-bars {VBars}, right number of voices {VCountOk} ({P(VCountOk, VBars):F1}%), all voices exact {VBarsExact} ({P(VBarsExact, VBars):F1}%)\n" +
               $"        events in voices: truth {VTruth}, omr {VOmr}, in the right voice and order {VExact} ({P(VExact, VTruth):F1}%)";
    }

    public string MultisetSummary()
    {
        double P(int x, int of) => of == 0 ? 0 : 100.0 * x / of;
        return $"staff-bars {Bars}, exact {BarsExact} ({P(BarsExact, Bars):F1}%)\n" +
               $"notes+rests: truth {MsTruth}, omr {MsOmr}; pitch+duration found {MsExact} (recall {P(MsExact, MsTruth):F1}%, precision {P(MsExact, MsOmr):F1}%); pitch found {MsPitch} (recall {P(MsPitch, MsTruth):F1}%)";
    }

    public string Summary()
    {
        double P(int x, int of) => of == 0 ? 0 : 100.0 * x / of;
        return $"bars {Bars}, exact bars {BarsExact} ({P(BarsExact, Bars):F1}%)\n" +
               $"events: truth {TruthEvents}, omr {OmrEvents}, aligned {Aligned}\n" +
               $"exact events {Exact} ({P(Exact, TruthEvents):F1}% of truth), pitch ok {PitchOk} ({P(PitchOk, Aligned):F1}% of aligned), duration ok {DurOk} ({P(DurOk, Aligned):F1}% of aligned)";
    }
}
