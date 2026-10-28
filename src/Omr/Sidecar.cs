using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Omr;

/// <summary>
/// What the reading leaves beside the .lys for an editor (x.lys → x.omr.json; the Lily#
/// proposal of 2026-10-04, D): the pages, where each part-bar is on its page as loaded and on
/// which line of the .lys, and the places to look at (todos). Lily# only reads it.
/// </summary>
sealed class Sidecar
{
    sealed record PageInfo(int Page, string Image, int Width, int Height);
    // (Staff: a part-bar's staff alone, top line to bottom line, as Box is mapped: Lily# shades it. Lily#'s proposal of 2026-10-14)
    sealed record BarBox(int Page, int[] Box, int[] Staff = null);

    readonly List<PageInfo> pages = new();
    readonly Dictionary<(int Bar, int Part), BarBox> boxes = new();
    readonly Dictionary<int, BarBox> systemBoxes = new();     // per bar: the whole system's column
    readonly Dictionary<int, (int Page, int System, bool Head)> places = new();     // per bar: its system, and whether it opens it (clef and key)
    // (a note's todo: At, where its event ends in the .lys text; the readings it stands for; the candidates, as read first.
    // Confidence: how likely it needs no correcting, 0..1 (README "How sure a todo is"). Quiet: kept here, not written into the .lys.
    // A part-bar's length todo: Lengths, that part and by how much per voice, in ticks (a pickup's, part -1: every part's))
    // (Readings: a length todo's other readings of its part-bar, Fixes' candidates, for Lily#'s AI fix)
    sealed record Todo(int Bar, int Part, string Source, string Memo, int At = -1, List<EvAlt> Alts = null, List<(string Label, string Text)> Cands = null, int N = 0,
        double Confidence = 0, bool Quiet = false, List<(int Part, List<int> Deltas)> Lengths = null, List<Fixes.Reading> Readings = null);
    List<Todo> todos = new();
    /// <summary>Per page: the image to name for it, when not the file read (omr read's input, copied to a work folder).</summary>
    public readonly Dictionary<int, string> ImageOf = new();
    /// <summary>A todo this sure (or surer) is kept in x.omr.json only, not written into the .lys (omr read --todo-threshold; 1: every todo is written).</summary>
    public double Threshold = 0.8;
    /// <summary>The todos written into the .lys as @todo (after Annotate).</summary>
    public int Written => todos.Count(t => !t.Quiet);
    /// <summary>The todos kept in x.omr.json only.</summary>
    public int QuietCount => todos.Count(t => t.Quiet);

    // a bar's first note, chord or rest, to its duration (a spacer rest when it has nothing else):
    // the @todo goes after it, before any tie or slur
    public static readonly Regex FirstEvent = new(@"(?<=^|[\s{])(?:<[^>]*>|[a-g](?:is|es)*[',]*|[rR])\d+\.*(?:\*\d+/\d+)?");
    static readonly Regex FirstSpacer = new(@"(?<=^|[\s{])s\d+\.*");

    /// <summary>Adds a page: each measure's box per staff and over the system, in the pixels of the page as loaded.</summary>
    public void AddPage(string path, PageResult res)
    {
        pages.Add(new PageInfo(res.Page, Path.GetFullPath(path), res.Dark.W, res.Dark.H));
        double s = res.S;
        foreach (var (sys, si) in res.Systems.Select((sys, si) => (sys, si)))
            for (int m = 0; m < sys.Measures; m++)
            {
                double x0 = sys.Bars[m], x1 = sys.Bars[m + 1];
                // (a multi-bar rest's bars all have its measure's box)
                for (int bar = sys.BarOf(m); bar < sys.BarOf(m) + sys.Span(m); bar++)
                {
                    foreach (var st in sys.Staves)
                        boxes[(bar, st.Part)] = new BarBox(res.Page, Source(res.Dark, x0, st.Top - 2 * s, x1, st.Bottom + 2 * s),
                                                           Source(res.Dark, x0, st.Top, x1, st.Bottom));
                    systemBoxes[bar] = new BarBox(res.Page, Source(res.Dark, x0, sys.Staves[0].Top - 2 * s, x1, sys.Staves[^1].Bottom + 2 * s));
                    places[bar] = (res.Page, si, m == 0);
                }
            }
        // the other readings' places (an event's copies share them)
        foreach (var ev in res.Bars.Values.Concat(res.Voices.Values.SelectMany(v => v)).SelectMany(e => e))
            foreach (var a in ev.Alts ?? new())
                if (a.Box == null) { a.Page = res.Page; a.Box = Source(res.Dark, a.L, a.T, a.R, a.B); }
    }

    /// <summary>A note's todo: the event ending at <paramref name="at"/> in the .lys text, read with a doubt (alts), and what it might be.</summary>
    public void AddNoteTodo(int bar, int part, int at, List<EvAlt> alts, List<(string Label, string Text)> cands)
    {
        int n = todos.Count(t => t.Bar == bar && t.Part == part && t.At >= 0) + 1;
        // (how sure the reading is of what it wrote, against the most likely other reading the detector
        // offers; a rules' dot it sees nothing of, the two readers at odds: a half)
        double confidence = Math.Round(alts.Min(a => a.DotsBy < 0 && a.Score == 0 ? 0.5 : 1 - a.Score), 2);
        // (the memo says what to look at, not where: the mark is on the note, its key names the bar and part. Lily#'s request of 2026-10-17)
        todos.Add(new Todo(bar, part, "detector", string.Join("; ", alts.Select(Memo.Doubt).Distinct()), at, alts, cands, n, confidence));
    }

    /// <summary>A place to look at: a part-bar (part -1: the bar's stems and tuplets, over the system), where it came from (verify), why, and how sure.</summary>
    public void AddTodo(int bar, int part, string source, string memo, double confidence) =>
        todos.Add(new Todo(bar, part, source, memo, Confidence: confidence));

    /// <summary>
    /// A bar whose music does not fill its time: one todo for each part that is off (key b{bar}-{part},
    /// on that part's first note, with its voices' lengths: Lily#'s request of 2026-10-17, so that
    /// each part's mark is on its own notes and its own box), or one for the bar (key b{bar}, every
    /// part) when the first bar is short alike on every staff, a pickup. Quiet when the redraw check
    /// found the bar's drawing as the page's.
    /// </summary>
    /// <param name="readings">per doubted part-bar, the other readings the reader weighed and did not take (Fixes): the part's go with its todo</param>
    public void AddLengthTodo(int bar, List<(int Part, string Id, List<List<Ev>> Voices)> parts, int barTicks, string time, bool first, bool quiet,
                              Dictionary<(int Bar, int Part), List<Fixes.Reading>> readings = null)
    {
        var sums = parts.Select(p => p.Voices.Select(v => v.Sum(e => e.Ticks)).ToList()).ToList();
        var deltas = parts.Select((p, i) => (p.Part, sums[i].Select(x => x - barTicks).ToList())).ToList();
        // (the first bar short alike on every staff: a pickup the reading cannot know of)
        if (first && sums.SelectMany(s => s).Distinct().Count() == 1 && sums[0][0] is int s0 && s0 > 0 && s0 < barTicks)
        {
            todos.Add(new Todo(bar, -1, "length", $"{Memo.Length(s0, time)} of {time} on every staff: a pickup bar?", Confidence: 0.8, Quiet: quiet, Lengths: deltas));
            return;
        }
        foreach (var (p, i) in parts.Select((p, i) => (p, i)))
            todos.Add(new Todo(bar, p.Part, "length",
                BarsInOne(bar, p.Part, sums[i], barTicks) is int k and > 1 ? Memo.MissedBarLine(k, time) : Memo.BarLength(sums[i], barTicks, time),
                Confidence: LengthConfidence(p.Voices, barTicks), Quiet: quiet, Lengths: new() { deltas[i] },
                Readings: readings != null && readings.TryGetValue((bar, p.Part), out var r) ? r : null));
    }

    /// <summary>
    /// How many bars a part-bar holds when a bar line looks missed in it (Lily#'s request of
    /// 2026-10-27): every voice a whole number of bars long, and its box about as many times as
    /// wide as its neighbours in the system (those opening a system, wider by the clef and key,
    /// not counted). Else 0.
    /// </summary>
    int BarsInOne(int bar, int part, List<int> sums, int barTicks)
    {
        if (sums.Distinct().Count() != 1 || sums[0] % barTicks != 0 || sums[0] / barTicks < 2) return 0;
        int k = sums[0] / barTicks;
        if (!places.TryGetValue(bar, out var at) || !boxes.TryGetValue((bar, part), out var box)) return 0;
        var widths = new[] { bar - 1, bar + 1 }
            .Where(n => places.TryGetValue(n, out var q) && q.Page == at.Page && q.System == at.System && !q.Head && boxes.ContainsKey((n, part)))
            .Select(n => boxes[(n, part)].Box).Where(b => b != box.Box).Select(b => b[2] - b[0]).ToList();
        if (widths.Count == 0) return 0;
        double times = (box.Box[2] - box.Box[0]) / widths.Average();
        return Math.Abs(times / k - 1) <= 0.2 ? k : 0;
    }

    /// <summary>
    /// How likely a part's bar that does not fill its time needs no correcting (README "How sure a
    /// todo is"), by its kind, then within the kind the less the more it is off (graded, so that a
    /// threshold does not show or hide a whole kind at once): the top voice full and the others only
    /// short 0.9 down to 0.8 (most often a rest not read, the notes right); every voice that is off put
    /// right by one dot or one tuplet 0.55 down to 0.4; the top voice off, or every voice, 0.3 down to
    /// 0.1; nothing read, or off by a bar or more, 0.
    /// </summary>
    static double LengthConfidence(List<List<Ev>> voices, int barTicks)
    {
        var sums = voices.Select(v => v.Sum(e => e.Ticks)).ToList();
        if (sums.All(x => x == 0) || sums.Any(x => Math.Abs(x - barTicks) >= barTicks)) return 0;
        double f = sums.Max(x => Math.Abs(x - barTicks)) / (double)barTicks;     // (0..1: how much of the bar the worst voice is off)
        return Math.Round(LengthKind(voices, sums, barTicks) switch { 0.8 => 0.9 - 0.1 * f, 0.5 => 0.55 - 0.15 * f, _ => 0.3 - 0.2 * f }, 2);
    }

    // (the kind: 0.8 the top voice full and the others only short, 0.5 every voice off put right by one dot or tuplet, else 0.2)
    static double LengthKind(List<List<Ev>> voices, List<int> sums, int barTicks)
    {
        if (voices.Count > 1 && sums[0] == barTicks && sums.Skip(1).All(x => x <= barTicks)) return 0.8;
        static bool Dotted(Ev e) => Dur.Text(e.Ticks)?.EndsWith('.') == true;
        bool OneFix(List<Ev> v, int sum)
        {
            int d = sum - barTicks;
            if (d == 0) return true;
            // short: a dot not read (it adds half its note); long: a dot too many (a third of the dotted
            // note), or a tuplet not read (three notes of a length played in the time of two: one too many)
            return d < 0 ? v.Any(e => !Dotted(e) && e.Ticks / 2 == -d)
                : v.Any(e => Dotted(e) && e.Ticks / 3 == d) || v.Count(e => e.Ticks == d) >= 3;
        }
        return voices.Select((v, i) => OneFix(v, sums[i])).All(ok => ok) ? 0.5 : 0.2;
    }

    /// <summary>The todo's key: the same in the .lys's @todo and in x.omr.json (Lily#'s quick fixes go by it).</summary>
    static string Key(Todo t, ScoreConfig cfg) => BarKey(t.Bar, t.Part >= 0 ? cfg.LysId(t.Part) : null) + (t.At >= 0 ? $"-n{t.N}" : "");

    /// <summary>A part-bar's todo key (part null: the whole bar's).</summary>
    public static string BarKey(int bar, string part) => part != null ? $"b{bar}-{Regex.Replace(part, "[^A-Za-z0-9_-]", "_")}" : $"b{bar}";

    /// <summary>
    /// Writes the todos into the .lys as @todo(key "memo") on the first note or rest of their bar
    /// (a whole bar's: its first part's), in place of the bar's "// …" length comment when the memo
    /// says the same. A flag on a bar's stems or tuplet numbers joins its parts' flags there, if
    /// any: one place to look at. Called after the redraw check, which reads the text as written.
    /// Returns the marks written.
    /// </summary>
    public int Annotate(string lysPath, Dictionary<(int Bar, int Part), (int Start, int End)> spans, Dictionary<(int Bar, int Part), int> musicAt, ScoreConfig cfg)
    {
        // (a todo this sure is kept in x.omr.json only: the .lys carries the places most likely wrong)
        var quiet = todos.Where(t => t.Quiet || t.Confidence >= Threshold).Select(t => t with { Quiet = true }).ToList();
        var merged = todos.Where(t => !t.Quiet && t.Confidence < Threshold && (t.Part >= 0 || t.Lengths != null)).OrderBy(t => t.Bar).ThenBy(t => t.Part).ToList();
        foreach (var t in todos.Where(t => !t.Quiet && t.Confidence < Threshold && t.Part < 0 && t.Lengths == null))
        {
            int i = merged.FindIndex(m => m.Bar == t.Bar && m.At < 0 && m.Lengths == null);
            if (i >= 0) merged[i] = merged[i] with { Memo = merged[i].Memo + "; " + t.Memo, Confidence = Math.Min(merged[i].Confidence, t.Confidence) };
            else merged.Add(t);
        }
        // (and a quiet flag on the system's stems, in a bar a part's todo is written in: one place still, its
        // words with the part's, the part's confidence as it is -- Verify.Places)
        quiet.RemoveAll(q =>
        {
            if (q.Part >= 0 || q.Lengths != null || q.At >= 0) return false;
            int i = merged.FindIndex(m => m.Bar == q.Bar && m.At < 0 && m.Lengths == null);
            if (i < 0) return false;
            merged[i] = merged[i] with { Memo = merged[i].Memo + "; " + q.Memo };
            return true;
        });
        todos = merged.OrderBy(t => t.Bar).ThenBy(t => t.Part).ThenBy(t => t.At).ToList();
        if (todos.Count == 0) { todos = quiet; return 0; }
        // (where a bar's todo goes: its part's first event; a length todo's, its first part that is off; the stems', any part's)
        IEnumerable<int> PartsOf(Todo t) => t.Part >= 0 ? new[] { t.Part } : t.Lengths?.Select(l => l.Part) ?? Enumerable.Range(0, cfg.Parts.Count);

        string text = File.ReadAllText(lysPath);
        int? At((int Bar, int Part) k)
        {
            if (!spans.TryGetValue(k, out var sp) || !musicAt.TryGetValue(k, out var from)) return null;
            int bar = text.LastIndexOf('|', sp.End - 1, sp.End - from);
            string music = text[from..(bar > from ? bar : sp.End)];
            // (grace notes are no event of the bar's: blanked, of the same length)
            music = Regex.Replace(music, @"(?:acciaccatura|appoggiatura|grace)\s*\{[^}]*\}", g => new string(' ', g.Length));
            var m = FirstEvent.Match(music);
            if (!m.Success) m = FirstSpacer.Match(music);
            return m.Success ? from + m.Index + m.Length : null;
        }
        // a bar's todo on the event a note's todo is on joins it: one mark an event
        (int, int) PlaceOf(Todo t) => PartsOf(t).Select(p => (t.Bar, p)).FirstOrDefault(q => At(q) != null, (t.Bar, PartsOf(t).First()));
        foreach (var t in todos.Where(t => t.At < 0).ToList())
        {
            var k = PlaceOf(t);
            int i = At(k) is int at ? todos.FindIndex(n => n.At == at) : -1;
            if (i < 0) continue;
            todos[i] = todos[i] with { Memo = t.Memo + "; " + todos[i].Memo, Confidence = Math.Min(t.Confidence, todos[i].Confidence) };
            todos.Remove(t);
        }
        var edits = new List<(int At, int Remove, string Insert)>();
        int written = 0;
        foreach (var t in todos)
        {
            var k = PlaceOf(t);
            if (!spans.TryGetValue(k, out var sp)) continue;
            string memo = t.Memo.Replace('"', '\'').Replace('\\', '/');
            // the line's own comment, when the memo says it (a part's length todo says it, or says better: a bar line missed)
            int c = text.IndexOf("  // ", sp.Start, sp.End - sp.Start, StringComparison.Ordinal);
            if (c >= 0 && (memo.Contains(text[(c + 5)..sp.End].TrimEnd()) || t.Source == "length" && t.Part >= 0))edits.Add((c, text[c..sp.End].TrimEnd('\r', '\n').Length, ""));
            if ((t.At >= 0 ? t.At : At(k)) is int at) { edits.Add((at, 0, $"@todo({Key(t, cfg)} \"{memo}\")")); written++; }
            else edits.Add((text[sp.Start..sp.End].TrimEnd('\r', '\n').Length + sp.Start, 0, $" // todo {Key(t, cfg)}: {memo}"));
        }
        var sb = new StringBuilder(text);
        foreach (var (at, remove, insert) in edits.OrderByDescending(e => e.At)) sb.Remove(at, remove).Insert(at, insert);
        File.WriteAllText(lysPath, sb.ToString());
        todos = todos.Concat(quiet).OrderBy(t => t.Bar).ThenBy(t => t.Part).ThenBy(t => t.At).ToList();
        return written;
    }

    /// <summary>
    /// The pages with the todos' boxes (Verify.DrawMarks), as base.omr-pNN.png: the todos written
    /// into the .lys, or every one (<paramref name="all"/>, omr read --annotate-all). Returns the files.
    /// </summary>
    public List<string> SaveMarks(string outDir, ScoreConfig cfg, bool all)
    {
        var files = new List<string>();
        var marks = todos.Where(t => all || !t.Quiet).Select(t => (Todo: t, Box: BoxOf(t))).Where(m => m.Box != null).ToList();
        foreach (var page in pages.Where(p => marks.Any(m => m.Box.Page == p.Page)))
        {
            string file = Path.Combine(outDir, $"verify-p{page.Page:D2}.png");
            Verify.DrawMarks(page.Image, file, marks.Where(m => m.Box.Page == page.Page).Select(m =>
                (m.Box.Box.Select(v => (double)v).ToArray(), m.Todo.Memo, m.Todo.Confidence, false)));
            files.Add(file);
        }
        return files;
    }

    /// <summary>A todo's box on its page as loaded: a note's around its heads' other readings; a length todo's over its parts that are off; else its part-bar's, or the system's.</summary>
    BarBox BoxOf(Todo t)
    {
        var placed = t.Alts?.Where(a => a.Box != null).ToList();
        if (placed?.Count > 0)
            return new BarBox(placed[0].Page, new[] { placed.Min(a => a.Box[0]), placed.Min(a => a.Box[1]), placed.Max(a => a.Box[2]), placed.Max(a => a.Box[3]) });
        if (t.Lengths != null)
        {
            var bs = t.Lengths.Select(l => boxes.GetValueOrDefault((t.Bar, l.Part))).Where(b => b != null).ToList();
            if (bs.Count == 0) return systemBoxes.GetValueOrDefault(t.Bar);
            // (a bar's staves are on one page; one off on another, as a part's system broken over the page, keeps its own)
            var on = bs.Where(b => b.Page == bs[0].Page).ToList();
            return new BarBox(on[0].Page, new[] { on.Min(b => b.Box[0]), on.Min(b => b.Box[1]), on.Max(b => b.Box[2]), on.Max(b => b.Box[3]) });
        }
        return t.Part >= 0 ? boxes.GetValueOrDefault((t.Bar, t.Part)) : systemBoxes.GetValueOrDefault(t.Bar);
    }

    // (a length in whole notes, as a fraction: "-1/2", "3/8", "0")
    static string Wholes(int ticks)
    {
        int g = Gcd(Math.Abs(ticks), Dur.Whole);
        static int Gcd(int a, int b) => b == 0 ? a : Gcd(b, a % b);
        return ticks == 0 ? "0" : Dur.Whole / g == 1 ? $"{ticks / g}" : $"{ticks / g}/{Dur.Whole / g}";
    }

    /// <summary>The box of the levelled page, as the box around its corners on the page as loaded.</summary>
    static int[] Source(Img page, double x0, double y0, double x1, double y1)
    {
        var c = new[] { page.ToSource(x0, y0), page.ToSource(x1, y0), page.ToSource(x0, y1), page.ToSource(x1, y1) };
        return new[] { (int)Math.Floor(c.Min(p => p.X)), (int)Math.Floor(c.Min(p => p.Y)), (int)Math.Ceiling(c.Max(p => p.X)), (int)Math.Ceiling(c.Max(p => p.Y)) };
    }

    /// <summary>
    /// The todos held against the truth's scoring (todos-truth.txt; tools/score-todos.ps1 counts them
    /// over the sets), after Annotate: a line a todo, "todo key source kind bar scoredBar part
    /// confidence written|quiet verdict", the verdict wrong when any part-bar it covers is scored
    /// wrong, right when every one is scored right, extra when its measure stands for no bar of the
    /// truth's, unscored when the truth says nothing of its parts; then a line "unmarked bar part
    /// [lost]" for each part-bar scored wrong that no todo covers (lost: a bar no system was found for).
    /// scoredBar: the bar each bar read is scored as (0: none); scoredParts: whether the truth has each part.
    /// </summary>
    public List<string> AgainstTruth(Dictionary<int, int> scoredBar, HashSet<(int Bar, string Part)> wrong, List<int> lost, List<bool> scoredParts, ScoreConfig cfg)
    {
        var lines = new List<string>();
        var covered = new HashSet<(int Bar, string Part)>();
        foreach (var t in todos)
        {
            var parts = t.At >= 0 || t.Part >= 0 ? new List<int> { t.Part } : t.Lengths?.Select(l => l.Part).ToList() ?? Enumerable.Range(0, cfg.Parts.Count).ToList();
            int scored = scoredBar.GetValueOrDefault(t.Bar, 0);
            var keys = parts.Where(p => p >= 0 && p < cfg.Parts.Count && scoredParts[p]).Select(p => (scored, cfg.Parts[p].Id)).ToList();
            string verdict = scored == 0 ? "extra" : keys.Count == 0 ? "unscored" : keys.Any(wrong.Contains) ? "wrong" : "right";
            if (scored > 0) foreach (var k in keys) covered.Add(k);
            string part = t.Part >= 0 ? cfg.Parts[t.Part].Id : "-";
            lines.Add($"todo\t{Key(t, cfg)}\t{t.Source}\t{(t.At >= 0 ? "note" : "bar")}\t{t.Bar}\t{scored}\t{part}\t{t.Confidence.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)}\t{(t.Quiet ? "quiet" : "written")}\t{verdict}");
        }
        foreach (var (bar, part) in wrong.Where(w => !covered.Contains(w)).OrderBy(w => w.Bar).ThenBy(w => w.Part))
            lines.Add($"unmarked\t{bar}\t{part}{(lost.Contains(bar) ? "\tlost" : "")}");
        return lines;
    }

    /// <summary>
    /// Writes x.omr.json beside the .lys; spans: where each part-bar's line is in its text; pageSide:
    /// what the pages show, bar by bar (Verify.Saved: omr verify holds the corrected .lys against it. Lily# does not read it).
    /// </summary>
    public void Write(string lysPath, string lysText, Dictionary<(int Bar, int Part), (int Start, int End)> spans, ScoreConfig cfg, object pageSide)
    {
        // (a line number, from 1: the "? verify" comments are added on the line, so the numbers stay)
        var lineStarts = new List<int> { 0 };
        for (int i = 0; i < lysText.Length; i++) if (lysText[i] == '\n') lineStarts.Add(i + 1);
        int Line(int offset) { int k = lineStarts.BinarySearch(offset); return (k >= 0 ? k : ~k - 1) + 1; }

        var measures = spans.OrderBy(kv => kv.Key.Part).ThenBy(kv => kv.Key.Bar).Select(kv => new
        {
            measure = kv.Key.Bar,
            part = cfg.LysId(kv.Key.Part),
            line = Line(kv.Value.Start),
            page = boxes.TryGetValue(kv.Key, out var b) ? b.Page : (int?)null,
            box = b?.Box,
            staff = b?.Staff,
        });
        var todoList = todos.Select(t =>
        {
            var b = BoxOf(t);
            // the part-bars the todo covers (its box is their union, BoxOf): a note's, its own; a
            // length todo's, the parts that are off; a part's, that part's; a system's, every part's
            var covered = t.At >= 0 ? spans.Keys.Where(k => k.Bar == t.Bar && k.Part == t.Part).ToList()
                : spans.Keys.Where(k => k.Bar == t.Bar && (t.Lengths != null ? t.Lengths.Any(l => l.Part == k.Part) : t.Part < 0 || k.Part == t.Part)).OrderBy(k => k.Part).ToList();
            var lines = t.At >= 0 ? new List<int> { Line(t.At) } : covered.Select(k => Line(spans[k].Start)).OrderBy(l => l).ToList();
            return new
            {
                key = Key(t, cfg),
                kind = t.At >= 0 ? "note" : "bar",
                source = t.Source,
                measure = t.Bar,
                part = t.Part >= 0 ? cfg.LysId(t.Part) : null,
                lines,
                // (Lily# boxes these in the preview, the same part-bars as the box on the page: Lily#'s request of 2026-10-17)
                bars = covered.Select(k => new { measure = k.Bar, part = cfg.LysId(k.Part) }).ToArray(),
                page = b?.Page,
                box = b?.Box,
                memo = t.Memo,
                confidence = Math.Round(t.Confidence, 2),
                // (kept here only: not in the .lys; absent = in the .lys)
                inSource = t.Quiet ? false : (bool?)null,
                // (a length todo: each part that is off, by how much, in whole notes: its worst voice's, and every voice's when it has several)
                parts = t.Lengths?.Select(l => new
                {
                    part = cfg.LysId(l.Part),
                    delta = Wholes(l.Deltas.OrderByDescending(Math.Abs).First()),
                    voices = l.Deltas.Count > 1 ? l.Deltas.Select(Wholes).ToArray() : null,
                }).ToArray(),
                candidates = (t.Cands ?? new()).Select(c => new { label = c.Label, text = c.Text }).ToArray(),
                // (a length todo's other readings of its part-bar, the reader's own candidates (Fixes), the ones that fill the bar first:
                // Lily#'s AI fix tells its model of them. Absent where there are none)
                readings = t.Readings?.Count > 0 ? t.Readings.Select(r => new { why = r.Why, was = r.Was, now = r.Now, evidence = r.Evidence, fills = r.Fills, x = r.X, y = r.Y }).ToArray() : null,
            };
        });
        string dir = Path.GetDirectoryName(Path.GetFullPath(lysPath));
        var doc = new
        {
            version = 1,
            omr = $"Lily# Scan Import {ReadCommand.Version}",
            lys = Path.GetFileName(lysPath),
            pages = pages.Select(p => new { page = p.Page, image = Relative(ImageOf.GetValueOrDefault(p.Page, p.Image), dir), width = p.Width, height = p.Height }),
            measures,
            todos = todoList,
            verify = pageSide,
        };
        var json = JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        File.WriteAllText(Path.ChangeExtension(lysPath, ".omr.json"), json);
    }

    /// <summary>
    /// A file's path as x.omr.json names it: relative to the side file's folder (with '/') when it is in that
    /// folder or under it, so that the folder moves whole to another place or PC (Lily#'s request of
    /// 2026-10-23: the owner works on several PCs); elsewhere, absolute as before.
    /// </summary>
    public static string Relative(string path, string dir)
    {
        if (path == null) return null;
        string full = Path.GetFullPath(path), root = Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return full;
        return full[root.Length..].Replace(Path.DirectorySeparatorChar, '/');
    }

    /// <summary>A path x.omr.json names, as a file: a relative one is from the side file's folder.</summary>
    public static string Resolve(string path, string dir) => path == null || Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(dir, path));
}
