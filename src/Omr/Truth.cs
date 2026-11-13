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
    // (a MusicXML truth's: the marks of each part-bar, its key, and which staves are one part's -- a grand staff's two)
    public Dictionary<(int Bar, string Part), List<Mark>> Marks = new();
    public Dictionary<(int Bar, string Part), int> Fifths = new();
    public Dictionary<string, int> Group = new();

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
/// (as the .notes truth does). A note's ties and slurs are kept (Tied, SlurStart, SlurEnd); a chord's
/// in Voices holds the notes tied (Tied: some of them, or all) and the slur of any of its notes.
/// </summary>
static class MusicXmlTruth
{
    /// <param name="barBase">the bars before this file's (pieces read as one: a song cycle printed on through)</param>
    public static Truth Load(string path, IReadOnlyList<string> parts, bool poly, Truth into = null, int barBase = 0)
    {
        var t = into ?? new Truth();
        var doc = System.Xml.Linq.XDocument.Load(path);
        int staffBase = 0, partNo = 0;
        // (a grand staff written as two parts under a brace -- Lily#'s export of a piano -- is one group, as
        // MuseScore's one part of two staves is: a dynamic between the staves is either's)
        var braced = new Dictionary<string, int>();
        string brace = null; int braces = 0;
        foreach (var el in doc.Root.Element("part-list")?.Elements() ?? Enumerable.Empty<System.Xml.Linq.XElement>())
        {
            if (el.Name.LocalName == "part-group")
            {
                string number = (string)el.Attribute("number") ?? "1";
                if ((string)el.Attribute("type") == "start" && el.Element("group-symbol")?.Value == "brace") { brace = number; braces++; }
                else if ((string)el.Attribute("type") == "stop" && number == brace) brace = null;
            }
            else if (el.Name.LocalName == "score-part" && brace != null) braced[(string)el.Attribute("id") ?? ""] = -braces;
        }
        foreach (var part in doc.Root.Elements("part"))
        {
            int divisions = 1, staves = 1, bar = barBase, fifths = 0;
            partNo++;
            int group = braced.TryGetValue((string)part.Attribute("id") ?? "", out int bg) ? bg : partNo;
            foreach (var measure in part.Elements("measure"))
            {
                bar++;
                // per staff and voice: the events in order, and where the voice's time stands
                var voices = new SortedDictionary<(int Staff, int Voice), List<Ev>>();
                // (and the marks a direction sets at a time of a staff: dynamics, hairpins, the pedal)
                var directions = new List<(int Staff, int Time, string Family, string Label)>();
                int now = 0, lastStart = 0;
                foreach (var el in measure.Elements())
                {
                    switch (el.Name.LocalName)
                    {
                        case "attributes":
                            if (el.Element("divisions") is { } d) divisions = int.Parse(d.Value);
                            if (el.Element("staves") is { } s) staves = int.Parse(s.Value);
                            if (el.Element("key")?.Element("fifths") is { } k) fifths = int.Parse(k.Value);
                            break;
                        case "backup": now -= Ticks(el); break;
                        case "forward": now += Ticks(el); break;
                        case "direction":
                        {
                            int at = now + (el.Element("offset") is { } off && divisions > 0
                                ? (int)Math.Round(double.Parse(off.Value, System.Globalization.CultureInfo.InvariantCulture) * Dur.Whole / (4 * divisions)) : 0);
                            int dStaff = int.Parse(el.Element("staff")?.Value ?? "1");
                            foreach (var dt in el.Elements("direction-type"))
                            {
                                foreach (var dyn in dt.Elements("dynamics"))
                                    foreach (var c in dyn.Elements())
                                        directions.Add((dStaff, at, "dynamics", c.Name.LocalName == "other-dynamics" ? c.Value.Trim() : c.Name.LocalName));
                                if (dt.Element("wedge") is { } w && Mark.Wedges.TryGetValue((string)w.Attribute("type") ?? "", out var kind))
                                    directions.Add((dStaff, at, "hairpins", kind));
                                if (dt.Element("pedal") is { } pd && Mark.Pedals.TryGetValue((string)pd.Attribute("type") ?? "", out var how))
                                    directions.Add((dStaff, at, "pedals", how));
                            }
                            break;
                        }
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
                                var notations = el.Elements("notations").ToList();
                                if (notations.Any(n => n.Element("arpeggiate") != null)) ev.Articulations = new List<string> { "arpeggio" };
                                // (its articulations, ornaments and fermata, by the names the .lys writes; a dynamic set on the note itself)
                                foreach (var mk in notations.SelectMany(n => n.Elements("articulations").Concat(n.Elements("ornaments")).Concat(n.Elements("technical")).SelectMany(g => g.Elements()).Concat(n.Elements("fermata"))))
                                    if (Mark.Names.TryGetValue(mk.Name.LocalName, out var name)) (ev.Articulations ??= new List<string>()).Add(name);
                                if (notations.SelectMany(n => n.Elements("dynamics")).SelectMany(g => g.Elements()).FirstOrDefault() is { } nd)
                                    ev.Dynamic = nd.Name.LocalName == "other-dynamics" ? nd.Value.Trim() : nd.Name.LocalName;
                                // (a tie on the note it starts at; a slur on one note of its chord: Scorer.AddTiesSlurs counts them a chord)
                                bool Has(string name, string type) => notations.Any(n => n.Elements(name).Any(x => (string)x.Attribute("type") == type));
                                if (Has("tied", "start")) ev.Tied = new List<Pitch> { ev.P };
                                ev.SlurStart = Has("slur", "start");
                                ev.SlurEnd = Has("slur", "stop");
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
                    t.Group[id] = group;
                    t.Fifths[(bar, id)] = fifths;
                    var all = new List<Ev>();
                    var chords = new List<Ev>();
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
                                if (e.Articulations != null) { last.Articulations ??= new List<string>(); last.Articulations.AddRange(e.Articulations.Except(last.Articulations)); }
                                last.Dynamic ??= e.Dynamic;
                                if (e.Tie) last.TieAt(e.P);
                                last.SlurStart |= e.SlurStart; last.SlurEnd |= e.SlurEnd;
                            }
                            else line.Add(new Ev { IsRest = e.IsRest, FullBar = e.FullBar, P = e.P, Ticks = e.Ticks, X = e.X, Articulations = e.Articulations == null ? null : new List<string>(e.Articulations), Dynamic = e.Dynamic, Tied = e.Tied == null ? null : new List<Pitch>(e.Tied), SlurStart = e.SlurStart, SlurEnd = e.SlurEnd });
                        }
                        if (!t.Voices.TryGetValue((bar, id), out var vs)) t.Voices[(bar, id)] = vs = new List<List<Ev>>();
                        vs.Add(line);
                        all.AddRange(poly ? evs : line);
                        chords.AddRange(line.Where(e => !e.IsRest));
                    }
                    t.Bars[(bar, id)] = all.OrderBy(e => e.X).ToList();   // (stable: a voice's order kept at one time)
                    t.Marks[(bar, id)] = Mark.OfTruth(chords, directions.Where(m => m.Staff == s).Select(m => (m.Time, m.Family, m.Label)).ToList());
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

/// <summary>
/// A mark scored on the real prints (S52): its family, what it says (mf, cresc, staccato, down), and the
/// notes (MIDI) of the chord it is on. The truth's MusicXML puts a dynamic, a hairpin and the pedal at a
/// time of a staff (a direction): they are on the chord that starts there or next (the last one before,
/// at the bar's end), and a release on the chord sounding then as well as the next (the reader marks the
/// note under the sign, MuseScore's sign may stand at the end of the note before it). A release and a
/// press at one time are a change, the press alone, as the reader writes it.
/// </summary>
sealed record Mark(string Family, string Label, HashSet<int> Notes)
{
    public static readonly string[] Families = { "dynamics", "hairpins", "articulations", "ornaments", "fermatas", "pedals" };
    /// <summary>The families a direction sets, which may stand on either staff of a grand staff.</summary>
    public static readonly HashSet<string> OfTime = new() { "dynamics", "hairpins", "pedals" };

    // MusicXML's elements, as the .lys names them (Articulations.Names)
    public static readonly Dictionary<string, string> Names = new()
    {
        ["staccato"] = "staccato", ["accent"] = "accent", ["tenuto"] = "tenuto", ["strong-accent"] = "marcato",
        ["staccatissimo"] = "staccatissimo", ["detached-legato"] = "portato", ["stopped"] = "stopped", ["fermata"] = "fermata",
        ["trill-mark"] = "trill", ["mordent"] = "mordent", ["inverted-mordent"] = "prall", ["turn"] = "turn", ["inverted-turn"] = "reverseTurn",
    };
    static readonly HashSet<string> Ornaments = new() { "trill", "mordent", "prall", "turn", "reverseTurn", "pralltriller" };
    static readonly HashSet<string> Articulations = new() { "staccato", "accent", "tenuto", "marcato", "staccatissimo", "portato", "stopped" };
    public static readonly Dictionary<string, string> Wedges = new() { ["crescendo"] = "cresc", ["diminuendo"] = "decresc" };
    public static readonly Dictionary<string, string> Pedals = new() { ["start"] = "down", ["change"] = "down", ["resume"] = "down", ["stop"] = "up" };

    static string FamilyOf(string label) => label == "fermata" ? "fermatas" : Ornaments.Contains(label) ? "ornaments" : Articulations.Contains(label) ? "articulations" : null;

    static IEnumerable<int> Midis(Ev e) => e.Pitches.Select(p => p.Midi());

    /// <summary>A chord's own marks (its articulations, ornaments, fermata, a dynamic on it), at its x.</summary>
    static IEnumerable<(string Family, string Label, double X, Ev E)> Own(Ev e)
    {
        foreach (var a in e.Articulations ?? new List<string>())
            if (FamilyOf(a) is { } f) yield return (f, a, e.X, e);
        if (e.Dynamic != null) yield return ("dynamics", e.Dynamic.Replace(".up", "").Replace(".down", ""), e.X, e);
    }

    /// <summary>The marks at one x (a chord's copies in two voices, one mark on two voices' chords): one mark, their notes.</summary>
    static List<Mark> Gather(IEnumerable<(string Family, string Label, double X, IEnumerable<int> Notes)> marks) =>
        marks.GroupBy(m => (m.Family, m.Label)).SelectMany(g =>
        {
            var one = new List<(double X, HashSet<int> Notes)>();
            foreach (var m in g.OrderBy(m => m.X))
                if (one.Count > 0 && m.X - one[^1].X < 1) one[^1].Notes.UnionWith(m.Notes);
                else one.Add((m.X, m.Notes.ToHashSet()));
            return one.Select(o => new Mark(g.Key.Family, g.Key.Label, o.Notes));
        }).ToList();

    /// <summary>A truth's part-bar: its chords (X: their start) and the directions of its staff.</summary>
    public static List<Mark> OfTruth(List<Ev> chords, List<(int Time, string Family, string Label)> directions)
    {
        var marks = chords.SelectMany(Own).Select(m => (m.Family, m.Label, m.X, Notes: Midis(m.E))).ToList();
        var onsets = chords.Select(c => c.X).Distinct().Order().ToList();
        IEnumerable<int> At(double x) => chords.Where(c => c.X == x).SelectMany(Midis);
        foreach (var (time, family, label) in directions)
        {
            if (onsets.Count == 0) continue;
            if (family == "pedals" && label == "up" && directions.Any(d => d.Time == time && d.Family == "pedals" && d.Label == "down")) continue;
            double? after = onsets.Where(x => x >= time).Cast<double?>().FirstOrDefault(), before = onsets.Where(x => x < time).Cast<double?>().LastOrDefault();
            var notes = family == "pedals" && label == "up"
                ? (before is { } b ? At(b) : []).Concat(after is { } a ? At(a) : [])
                : At(after ?? before.Value);
            marks.Add((family, label, after ?? before.Value, notes));
        }
        return Gather(marks);
    }

    /// <summary>A part-bar as read: its voices' chords' marks, and the hairpins and the pedal the reader hung on them.</summary>
    public static List<Mark> OfRead(IEnumerable<List<Ev>> voices)
    {
        var marks = new List<(string Family, string Label, double X, IEnumerable<int> Notes)>();
        foreach (var e in voices.SelectMany(v => v).Where(e => !e.IsRest && !e.Slash))
        {
            foreach (var m in Own(e)) marks.Add((m.Family, m.Label, m.X, Midis(e)));
            if (e.HairpinStart != null) marks.Add(("hairpins", e.HairpinStart, e.X, Midis(e)));
            if (e.PedalDown) marks.Add(("pedals", "down", e.X, Midis(e)));
            if (e.PedalUp) marks.Add(("pedals", "up", e.X, Midis(e)));
        }
        return Gather(marks);
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
        var (found, right) = MatchChords(t, o);
        ArpTruth += t.Count; ArpOmr += o.Count; ArpFound += found; ArpRight += right;
        if (found < t.Count || right < o.Count) ArpDiffs.Add($"{bar,4} {part,-5} arpeggios: truth {t.Count}, read {o.Count}, found {found}, read right {right} ({ShowChords(t)} / {ShowChords(o)})");
    }

    /// <summary>A part-bar's marked chords, the truth's and those read, matched by a note in common: how many of
    /// the truth's are found (one to one: a bar's chords repeated over one mark read are found once), and how
    /// many of those read stand for one of the truth's.</summary>
    static (int Found, int Right) MatchChords(List<HashSet<int>> t, List<HashSet<int>> o)
    {
        var used = new bool[o.Count];
        int found = 0, right = o.Count(k => t.Any(c => c.Overlaps(k)));
        foreach (var c in t)
            for (int j = 0; j < o.Count; j++)
                if (!used[j] && c.Overlaps(o[j])) { used[j] = true; found++; break; }
        return (found, right);
    }

    static string ShowChords(List<HashSet<int>> a) => string.Join(" ", a.Select(h => string.Join(",", h.Order())));

    // ties and slurs. A tie is a note's (the truth's MusicXML marks each note it holds, the reading each head an arc
    // leaves; a chord's tie, every note of it): matched within the part-bar by the note. A slur is a chord's (an
    // event of a voice: the .lys writes "(" and ")" a chord; the truth's MusicXML marks one note of the chord),
    // matched within the part-bar by a note they share, as arpeggios are.
    static readonly (string Name, Func<Ev, bool> Has)[] TieSlurMarks = { ("ties", e => e.Tie), ("slur starts", e => e.SlurStart), ("slur ends", e => e.SlurEnd) };
    public readonly int[,] TieSlur = new int[3, 4];   // per mark (ties, slur starts, slur ends): truth, omr, found, read right
    public List<string> TieSlurDiffs = new();

    public void AddTiesSlurs(int bar, string part, List<List<Ev>> truth, List<List<Ev>> omr)
    {
        for (int k = 0; k < TieSlurMarks.Length; k++)
        {
            var has = TieSlurMarks[k].Has;
            List<HashSet<int>> Chords(List<List<Ev>> vs) => k == 0
                ? vs.SelectMany(v => v).Where(e => !e.IsRest && e.Tie).SelectMany(e => e.Tied.Select(p => new HashSet<int> { p.Midi() })).ToList()
                : vs.SelectMany(v => v).Where(e => !e.IsRest && has(e)).Select(e => e.Pitches.Select(p => p.Midi()).ToHashSet()).ToList();
            var t = Chords(truth);
            var o = Chords(omr);
            var (found, right) = MatchChords(t, o);
            TieSlur[k, 0] += t.Count; TieSlur[k, 1] += o.Count; TieSlur[k, 2] += found; TieSlur[k, 3] += right;
            if (found < t.Count || right < o.Count) TieSlurDiffs.Add($"{bar,4} {part,-5} {TieSlurMarks[k].Name}: truth {t.Count}, read {o.Count}, found {found}, read right {right} ({ShowChords(t)} / {ShowChords(o)})");
        }
    }

    public string TieSlurSummary()
    {
        double P(int x, int of) => of == 0 ? 0 : 100.0 * x / of;
        return string.Join("\n", TieSlurMarks.Select((m, k) =>
            $"{m.Name}: truth {TieSlur[k, 0]}, omr {TieSlur[k, 1]}, found {TieSlur[k, 2]}, read right {TieSlur[k, 3]} (recall {P(TieSlur[k, 2], TieSlur[k, 0]):F1}%, precision {P(TieSlur[k, 3], TieSlur[k, 1]):F1}%)"));
    }

    // the marks of a MusicXML truth (Mark): matched one to one within the part-bar by what they say and a note in
    // common; then a dynamic, hairpin or pedal mark of the truth's left over by one the reader hung on the other
    // staff of its part (a grand staff's dynamics stand between the two, the truth's staff for them is MuseScore's guess)
    public readonly Dictionary<string, int[]> MarkCount = new();   // per family, and per "family label": truth, omr, found
    public List<string> MarkDiffs = new();

    public void AddMarks(int bar, Truth truth, IReadOnlyList<(string Part, List<List<Ev>> Voices)> parts)
    {
        var t = parts.Select(p => (truth.Marks.GetValueOrDefault((bar, p.Part)) ?? new List<Mark>()).ToList()).ToList();
        var o = parts.Select(p => Mark.OfRead(p.Voices)).ToList();
        void Count(string key, int k) { if (!MarkCount.TryGetValue(key, out var c)) MarkCount[key] = c = new int[3]; c[k]++; }
        for (int i = 0; i < parts.Count; i++)
        {
            foreach (var m in t[i]) { Count(m.Family, 0); Count($"{m.Family} {m.Label}", 0); }
            foreach (var m in o[i]) { Count(m.Family, 1); Count($"{m.Family} {m.Label}", 1); }
        }
        bool Take(int i, int j, Func<Mark, bool> which)
        {
            bool any = false;
            foreach (var m in t[i].Where(which).ToList())
                if (o[j].FirstOrDefault(r => r.Family == m.Family && r.Label == m.Label && r.Notes.Overlaps(m.Notes)) is { } hit)
                {
                    t[i].Remove(m); o[j].Remove(hit); Count(m.Family, 2); Count($"{m.Family} {m.Label}", 2); any = true;
                }
            return any;
        }
        for (int i = 0; i < parts.Count; i++) Take(i, i, _ => true);
        for (int i = 0; i < parts.Count; i++)
            for (int j = 0; j < parts.Count; j++)
                if (j != i && truth.Group.TryGetValue(parts[i].Part, out var gi) && truth.Group.TryGetValue(parts[j].Part, out var gj) && gi == gj)
                    Take(i, j, m => Mark.OfTime.Contains(m.Family));
        static string Show(IEnumerable<Mark> ms) => string.Join(" ", ms.Select(m => $"{m.Label}({string.Join(",", m.Notes.Order())})"));
        for (int i = 0; i < parts.Count; i++)
            foreach (var f in Mark.Families)
            {
                var missed = t[i].Where(m => m.Family == f).ToList();
                var extra = o[i].Where(m => m.Family == f).ToList();
                if (missed.Count + extra.Count > 0) MarkDiffs.Add($"{bar,4} {parts[i].Part,-5} {f}: missed {Show(missed)} / extra {Show(extra)}");
            }
    }

    public string MarkSummary()
    {
        double P(int x, int of) => of == 0 ? 0 : 100.0 * x / of;
        var lines = Mark.Families.Where(MarkCount.ContainsKey).Select(f =>
        {
            var c = MarkCount[f];
            return $"{f}: truth {c[0]}, omr {c[1]}, found {c[2]}, read right {c[2]} (recall {P(c[2], c[0]):F1}%, precision {P(c[2], c[1]):F1}%)";
        }).ToList();
        var kinds = MarkCount.Where(kv => kv.Key.Contains(' ')).OrderByDescending(kv => kv.Value[0]).Select(kv => $"{kv.Key.Split(' ')[1]} {kv.Value[2]}/{kv.Value[0]} (read {kv.Value[1]})");
        lines.Add("marks by kind, found/truth: " + string.Join(", ", kinds));
        return string.Join("\n", lines);
    }

    // why a part-bar is wrong (S52): its events and the reading's paired in order, the exact ones first, then by what
    // they share (a note's pitch: its value wrong, by a tuplet's or not; its value: its pitch wrong, by a key
    // signature's sign, another accidental, an octave, a shift the bar's notes share (a clef), or else), the rest
    // of the truth's missed and of the reading extra. A part-bar whose notes are all found but not in their voices
    // counts its voice; a bar no system was found for, its notes lost. Counted in events (in order, so a little
    // more than the multiset score's errors where it finds a note far off in the bar).
    public static readonly string[] CauseNames = { "lost", "missed", "extra", "extraMeasure", "value", "tuplet", "key", "accidental", "octave", "clef", "pitch", "voice" };
    public readonly Dictionary<string, int> Causes = CauseNames.ToDictionary(c => c, _ => 0);
    public List<string> CauseDiffs = new();
    public HashSet<int> Lost = new();
    public Truth Truth;   // (its keys: a sign of the key missed or too many)

    // (--evidence: each wrong part-bar's notes, one row each, with the cause it was counted under)
    public System.Text.StringBuilder Evidence;
    void EvidenceRow(int bar, string part, string row, Ev t, Ev o)
    {
        if (Evidence == null) return;
        string T = t == null ? "\t\t\t\t" : FormattableString.Invariant($"{(t.IsRest ? 1 : 0)}\t{(t.IsRest ? 0 : t.P.Diatonic)}\t{(t.IsRest ? 0 : t.P.Alter)}\t{t.Ticks}\t{t.X:F6}");
        string O = o == null ? "\t\t\t\t\t\t\t\t\t" : FormattableString.Invariant($"{(o.IsRest ? 1 : 0)}\t{(o.IsRest ? 0 : o.P.Diatonic)}\t{(o.IsRest ? 0 : o.P.Alter)}\t{o.Ticks}\t{o.X:F1}\t{o.Stem}\t{o.StemX:F1}\t{o.StemLo:F1}\t{o.StemHi:F1}\t{o.Tuplet}");
        Evidence.Append($"{bar}\t{part}\t{row}\t{T}\t{O}\n");
    }

    void Classify(int bar, string part, List<Ev> truth, List<Ev> omr)
    {
        var here = CauseNames.ToDictionary(c => c, _ => 0);
        foreach (var e in truth) EvidenceRow(bar, part, "=truth", e, null);
        foreach (var e in omr) EvidenceRow(bar, part, "=read", null, e);
        if (Lost.Contains(bar)) here["lost"] = truth.Count;
        else
        {
            static int Midi(Ev e) => e.IsRest ? -1 : e.P.Midi();
            var t = truth.OrderBy(e => e.X).ThenBy(Midi).ToList();
            var o = omr.OrderBy(e => e.X).ThenBy(Midi).ToList();
            // (a pitch's height: by step where the truth spells its notes, by semitone where it knows the MIDI note only)
            bool spelled = t.Any(e => !e.IsRest && !e.MidiOnly);
            int H(Ev e) => spelled ? e.P.Diatonic : e.P.Midi();
            // the bar's notes shifted as one (a clef or an octave line misread): the shift that pairs most of them with
            // notes read of their value, half the bar's notes at least and three or more, and more than are read right.
            // Looked for before anything is paired: paired first, the notes of an octave read low would take others of
            // their name an octave on, and the shift the bar shares would be lost (a chord's heads stand in no order of x)
            int notes = t.Count(e => !e.IsRest);
            List<(Ev T, Ev O)> ShiftedBy(int s)
            {
                var left = o.Where(e => !e.IsRest).ToList();
                var got = new List<(Ev T, Ev O)>();
                foreach (var e in t.Where(e => !e.IsRest))
                    if (left.FirstOrDefault(r => H(r) - H(e) == s && SameDur(e, r)) is { } hit) { left.Remove(hit); got.Add((e, hit)); }
                return got;
            }
            var shifts = t.Where(e => !e.IsRest).SelectMany(e => o.Where(r => !r.IsRest && SameDur(e, r)).Select(r => H(r) - H(e))).Where(s => s != 0).Distinct()
                          .Select(s => (S: s, Pairs: ShiftedBy(s))).OrderByDescending(x => x.Pairs.Count).FirstOrDefault();
            if (shifts.Pairs != null && shifts.Pairs.Count >= Math.Max(3, (notes + 1) / 2) && shifts.Pairs.Count > ShiftedBy(0).Count)
            {
                bool octave = shifts.S % (spelled ? 7 : 12) == 0;
                here[octave ? "octave" : "clef"] += shifts.Pairs.Count;
                foreach (var (x, y) in shifts.Pairs) { t.Remove(x); o.Remove(y); EvidenceRow(bar, part, octave ? "octave" : "clef", x, y); }
            }
            // the rest paired off: the exact ones in any order, as the multiset score finds them, then in order by what they share
            foreach (var e in t.ToList())
                if (o.FirstOrDefault(r => r.IsRest == e.IsRest && Midi(r) == Midi(e) && SameDur(e, r)) is { } hit) { t.Remove(e); o.Remove(hit); }
            static int W(Ev a, Ev b) => a.IsRest != b.IsRest ? 0 : a.IsRest ? 2 : a.P.Midi() == b.P.Midi() || a.Ticks == b.Ticks ? 2 : 0;
            int n = t.Count, m = o.Count;
            var dp = new int[n + 1, m + 1];
            for (int i = n - 1; i >= 0; i--)
                for (int j = m - 1; j >= 0; j--)
                    dp[i, j] = Math.Max(Math.Max(dp[i + 1, j], dp[i, j + 1]), W(t[i], o[j]) > 0 ? dp[i + 1, j + 1] + W(t[i], o[j]) : 0);
            var pairs = new List<(Ev T, Ev O)>();
            int a = 0, b = 0;
            while (a < n && b < m)
            {
                if (W(t[a], o[b]) > 0 && dp[a, b] == dp[a + 1, b + 1] + W(t[a], o[b])) { pairs.Add((t[a], o[b])); a++; b++; }
                else if (dp[a, b] == dp[a + 1, b]) a++;
                else b++;
            }
            here["missed"] = n - pairs.Count;
            here["extra"] = m - pairs.Count;
            if (Evidence != null)
            {
                foreach (var e in t.Where(e => !pairs.Any(p => ReferenceEquals(p.T, e)))) EvidenceRow(bar, part, "missed", e, null);
                foreach (var e in o.Where(e => !pairs.Any(p => ReferenceEquals(p.O, e)))) EvidenceRow(bar, part, "extra", null, e);
            }
            int fifths = Truth?.Fifths.GetValueOrDefault((bar, part)) ?? 0;
            foreach (var p in pairs)
            {
                string cause;
                if (p.T.IsRest || p.T.P.Midi() == p.O.P.Midi())
                    cause = !p.T.IsRest && (Dur.Text(p.T.Ticks) == null || Dur.Text(p.O.Ticks) == null) ? "tuplet" : "value";
                else
                {
                    int d = H(p.O) - H(p.T), dm = p.O.P.Midi() - p.T.P.Midi();
                    if (spelled ? d == 0 : Math.Abs(d) <= 2)
                        cause = spelled && KeySign(fifths, Pitch.Mod7(p.T.P.Diatonic), p.T.P.Alter, p.O.P.Alter) ? "key" : "accidental";
                    else if (dm % 12 == 0 && (!spelled || d % 7 == 0)) cause = "octave";
                    else cause = "pitch";
                }
                here[cause]++;
                EvidenceRow(bar, part, cause, p.T, p.O);
            }
        }
        foreach (var (c, k) in here) Causes[c] += k;
        if (here.Values.Any(k => k > 0)) CauseDiffs.Add($"{bar,4} {part,-5} {string.Join(", ", here.Where(kv => kv.Value > 0).Select(kv => $"{kv.Key} {kv.Value}"))}");
    }

    /// <summary>A note read with another alteration where a key signature of a sign more or less would give it.</summary>
    static bool KeySign(int fifths, int step, int truthAlter, int readAlter)
    {
        if (Math.Abs(fifths) > 7 || Keys.Alterations(fifths)[step] != truthAlter) return false;
        return new[] { fifths - 1, fifths + 1 }.Any(f => Math.Abs(f) <= 7 && Keys.Alterations(f)[step] == readAlter);
    }

    public string CauseSummary() => "causes: " + string.Join(", ", CauseNames.Select(c => $"{c} {Causes[c]}"));

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
        else { Wrong.Add((bar, part)); Classify(bar, part, truth, omr); Diffs.Add($"{bar,4} {part,-4} T: {string.Join(" ", truth.Select(e => e.Show()))}\n          O: {string.Join(" ", omr.Select(e => e.Show()))}"); }
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
        Causes["extraMeasure"] += omr.Count;
        Diffs.Add($"  +{at,-3} {part,-5} O: {string.Join(" ", omr.Select(e => e.Show()))}   (read measure {at}: no bar of the truth)");
    }

    public void AddMultiset(int bar, string part, List<Ev> truth, List<Ev> omr)
    {
        AddArpeggios(bar, part, truth, omr);
        Bars++;
        int exact = Common(truth, omr, true), pitch = Common(truth, omr, false);
        MsTruth += truth.Count; MsOmr += omr.Count; MsExact += exact; MsPitch += pitch;
        if (exact == truth.Count && omr.Count == truth.Count)
        {
            BarsExact++;
            foreach (var e in omr) EvidenceRow(bar, part, "=read", null, e);
        }
        else { Wrong.Add((bar, part)); Classify(bar, part, truth, omr); Diffs.Add($"{bar,4} {part,-5} T: {string.Join(" ", truth.OrderBy(e => e.IsRest).ThenBy(e => e.IsRest ? 0 : e.P.Midi()).Select(e => e.Show()))}\n           O: {string.Join(" ", omr.OrderBy(e => e.IsRest).ThenBy(e => e.IsRest ? 0 : e.P.Midi()).Select(e => e.Show()))}"); }
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
            // (its notes all found, in the wrong voices: the voices' fault; a part-bar wrong is counted by its notes)
            if (!Wrong.Contains((bar, part)) && tn > best) { Causes["voice"] += tn - best; CauseDiffs.Add($"{bar,4} {part,-5} voice {tn - best}"); }
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
