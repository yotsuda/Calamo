using System.Diagnostics;
using System.Text;

namespace Omr;

/// <summary>
/// The redraw-and-compare check (S12): the .lys the reader wrote is engraved again by Lily#, and
/// what that drawing shows in each bar is held against what the input page shows there. A bar
/// where the two disagree is a place to look at. The input's side is the learnt detector's
/// symbols (what the page shows, read without the rules), or the truth's SVG when the check
/// itself is being checked (--verify-truth), or the reader's own symbols when neither is there.
/// Symbols are compared as a multiset per part-bar: a head by its kind and staff position, an
/// accidental by kind and position (a half-space of give), a dot and a rest by kind; a stem by
/// the beams or flags at its tip and a tuplet number by its presence, both over the system's bar
/// as a whole (they stand between the staves); a tie by its left end, counted when a head stands
/// just before it (the piece of a tie broken at a line's end that begins the next line has none,
/// and the two drawings break their lines in different places). A printed accidental the reading does not draw is
/// no difference when the reading's note there already has that alteration (a courtesy
/// accidental). A bar whose events cannot fill its time is flagged whatever the drawings show.
/// Where the two drawings differ in layout does not matter: the bar is the unit. The report goes
/// to verify.txt with each difference's place on the input page. With a truth to score against,
/// the flagged bars are held against the bars scored wrong (verify vs truth).
/// </summary>
sealed class Verify
{
    /// <summary>The lysc that draws the .lys again: as given (--lysc, --verify path), beside this program, or on the PATH (ReadCommand.FindLysc).</summary>
    public static string LyscPath;
    public string Source = "";       // detector, truth, reader
    const int AllParts = -1;         // the part of a bar's stems and tuplet numbers: counted over the system, not per staff

    // per part-bar: what the input shows there, and where that is on its page
    readonly SortedDictionary<(int Bar, int Part), List<(string Class, int Pos)>> input = new();
    readonly Dictionary<(int Bar, int Part), string> where = new();
    readonly Dictionary<(int Bar, int Part), (int Page, double X0, double Y0, double X1, double Y1)> box = new();   // the same place, as a box on its page
    readonly Dictionary<int, string> pagePaths = new();
    // what Compare found at each flagged part-bar: its words, its kind (length, tab, diff) and, for a diff, the symbols that do not match
    // (Matched: the symbols that do match there; Off: a length's, by how much of the bar)
    readonly SortedDictionary<(int Bar, int Part), List<(string Note, string Kind, int Count, int Matched, double Off)>> flags = new();
    readonly HashSet<(int Bar, int Part)> wrong = new();    // where the truth says the reading is wrong (to check the check)
    public bool Checked;                                     // whether a truth was there to fill wrong
    public bool Full;                                        // the report lists every part-bar's two sides, not the differing ones only
    public readonly List<string> Trace = new();              // with Full: how the symbols standing off their staff were placed

    readonly Dictionary<(int Bar, int Part), string> lengths = new();   // part-bars whose events do not fill the time (the writer's note)

    public void MarkWrong(int bar, int part) => wrong.Add((bar, part));
    /// <summary>A part-bar whose events do not fill the time: why, and by how much of the bar (the worst voice's, 0..1).</summary>
    public void MarkLength(int bar, int part, string note, double off) { lengths[(bar, part)] = note; lengthOff[(bar, part)] = off; }
    readonly Dictionary<(int Bar, int Part), double> lengthOff = new();
    readonly Dictionary<(int Bar, int Part), string> tabs = new();      // part-bars whose TAB and staff disagree (B5, Tab.Apply)
    public void MarkTab(int bar, int part, string note) => tabs[(bar, part)] = note;
    /// <summary>The bars' lengths from lysc's own warnings (LYS2001) instead of the reading's (omr verify: the .lys as corrected).</summary>
    public bool LengthsFromLysc;
    /// <summary>Whether the last Compare had a drawing to compare (lysc drew the .lys, with no error).</summary>
    public bool Drawn;
    /// <summary>lysc's first error on the .lys, if any (the line of its log).</summary>
    public string LyscError;

    // ---- kept beside the .lys (x.omr.json's "verify"): the input's side, for omr verify to hold a corrected .lys against

    /// <summary>The input's side as x.omr.json keeps it: per part-bar (part null: the system's stems and tuplet numbers) its box on the page as loaded and what the page shows there.</summary>
    public object Saved(ScoreConfig cfg) => new
    {
        source = Source,
        parts = Enumerable.Range(0, cfg.Parts.Count).Select(cfg.LysId).ToArray(),
        bars = box.Keys.OrderBy(k => k.Bar).ThenBy(k => k.Part).Select(k =>
        {
            var b = box[k];
            return new
            {
                measure = k.Bar,
                part = k.Part == AllParts ? null : cfg.LysId(k.Part),
                page = b.Page,
                box = new[] { (int)Math.Floor(b.X0), (int)Math.Floor(b.Y0), (int)Math.Ceiling(b.X1), (int)Math.Ceiling(b.Y1) },
                symbols = (input.GetValueOrDefault(k) ?? new()).Select(t => $"{t.Class}@{t.Pos}").ToArray(),
            };
        }).ToArray(),
    };

    /// <summary>The input's side as x.omr.json kept it (<see cref="Saved"/>), the pages' images by number; the parts' names in order.</summary>
    public static Verify Load(System.Text.Json.JsonElement saved, Dictionary<int, string> images, out List<string> parts)
    {
        var v = new Verify { Source = saved.GetProperty("source").GetString() };
        parts = saved.GetProperty("parts").EnumerateArray().Select(p => p.GetString()).ToList();
        foreach (var bar in saved.GetProperty("bars").EnumerateArray())
        {
            var pe = bar.GetProperty("part");
            int part = pe.ValueKind == System.Text.Json.JsonValueKind.Null ? AllParts : parts.IndexOf(pe.GetString());
            var key = (bar.GetProperty("measure").GetInt32(), part);
            var b = bar.GetProperty("box").EnumerateArray().Select(e => e.GetDouble()).ToArray();
            int page = bar.GetProperty("page").GetInt32();
            v.box[key] = (page, b[0], b[1], b[2], b[3]);
            v.where[key] = $"p{page} {(part == AllParts ? "" : $"part {parts[part]} ")}box {b[0]:F0},{b[1]:F0}-{b[2]:F0},{b[3]:F0} (as loaded)";
            v.input[key] = bar.GetProperty("symbols").EnumerateArray().Select(e =>
            {
                string sym = e.GetString();
                int at = sym.LastIndexOf('@');
                return (sym[..at], int.Parse(sym[(at + 1)..]));
            }).ToList();
        }
        foreach (var (page, path) in images) v.pagePaths[page] = path;
        return v;
    }

    /// <summary>The page box of a part-bar (part -1: the system's), as kept.</summary>
    public (int Page, int[] Box)? BoxOf(int bar, int part) =>
        box.TryGetValue((bar, part), out var b) ? (b.Page, new[] { (int)b.X0, (int)b.Y0, (int)Math.Ceiling(b.X1), (int)Math.Ceiling(b.Y1) }) : null;

    // ---- the input's side

    /// <summary>The part-bar a symbol on the page belongs to: the staff's system and the barline before it.</summary>
    public static (int Bar, int Part)? Locate(PageResult res, Staff st, double x, double s)
    {
        // (a reader's head or number can stand on a staff of no kept system: a band page's last
        // system missing a part's staff is dropped, its staves keep the number it would have had)
        if (st == null || st.System >= res.Systems.Count || !res.Systems[st.System].All.Contains(st)) return null;
        var sys = res.Systems[st.System];
        if (sys.Bars.Count < 2) return null;
        int m = 0;
        while (m + 1 < sys.Measures && sys.Bars[m + 1] - 0.3 * s <= x) m++;
        return (sys.BarOf(m), st.Part);
    }

    /// <summary>Adds a page: its symbols (class, x, y in page pixels), and where the truth found the reading wrong.</summary>
    public void AddPage(string pagePath, PageResult res, ScoreConfig cfg, IEnumerable<(string Class, double X, double Y)> syms, IEnumerable<(Staff St, double X, double Y)> errors)
    {
        double s = res.S;
        pagePaths[res.Page] = pagePath;
        // (the boxes are drawn on the page as loaded: the levelled page's corners taken back to it)
        (int, double, double, double, double) Box(double x0, double y0, double x1, double y1)
        {
            var c = new[] { res.Dark.ToSource(x0, y0), res.Dark.ToSource(x1, y0), res.Dark.ToSource(x0, y1), res.Dark.ToSource(x1, y1) };
            return (res.Page, c.Min(p => p.X), c.Min(p => p.Y), c.Max(p => p.X), c.Max(p => p.Y));
        }
        var staves = res.Systems.SelectMany(x => x.Staves).ToList();
        foreach (var sys in res.Systems)
            for (int m = 0; m < sys.Measures; m++)
                foreach (var st in sys.Staves)
                {
                    var key = (sys.BarOf(m), st.Part);
                    if (!input.ContainsKey(key)) input[key] = new();
                    where[key] = $"p{res.Page} s{sys.Index + 1} part {cfg.Parts[st.Part].Id} x {sys.Bars[m]:F0}-{sys.Bars[m + 1]:F0} y {st.Top - 3 * s:F0}-{st.Bottom + 3 * s:F0}";
                    box[key] = Box(sys.Bars[m], st.Top - 3 * s, sys.Bars[m + 1], st.Bottom + 3 * s);
                }
        foreach (var sys in res.Systems)
            for (int m = 0; m < sys.Measures; m++)
            {
                where[(sys.BarOf(m), AllParts)] = $"p{res.Page} s{sys.Index + 1} x {sys.Bars[m]:F0}-{sys.Bars[m + 1]:F0} y {sys.Staves[0].Top - 3 * s:F0}-{sys.Staves[^1].Bottom + 3 * s:F0}";
                box[(sys.BarOf(m), AllParts)] = Box(sys.Bars[m], sys.Staves[0].Top - 3 * s, sys.Bars[m + 1], sys.Staves[^1].Bottom + 3 * s);
            }
        var heads = syms.Where(t => t.Class.StartsWith("head_")).ToList();
        var claimed = new HashSet<Head>();     // the reader's stems already matched to an input stem (two tips can meet between systems)
        foreach (var (cls, x, y) in syms)
        {
            Staff st;
            if (cls.StartsWith("stem_"))
            {
                // a stem's tip stands off its staff, often nearer another (the next system's, a
                // piano's other staff): its staff is that of the reader's head whose stem ends there,
                // or else of the nearest head in its column
                // (the reader's tip may stop at the near edge of the beams, the drawn line runs to the far one)
                var mine = res.Sym.Heads.Where(h => h.HasStem && !claimed.Contains(h) && Math.Abs(h.StemX - x) < 0.5 * s && Math.Abs(h.Tip - y) < 1.5 * s)
                    .OrderBy(h => Math.Abs(h.Tip - y)).FirstOrDefault();
                if (mine != null)
                {
                    st = mine.St;
                    foreach (var h in res.Sym.Heads) if (h.HasStem && h.St == mine.St && h.Up == mine.Up && Math.Abs(h.StemX - mine.StemX) < 0.3 * s) claimed.Add(h);
                }
                else
                {
                    // (an up stem stands at its head's right edge, a down stem at the left: a head of
                    // the other staff in the column has the tip on the wrong side of it)
                    var h = heads.Where(t => Math.Abs(t.X - x) < 1.0 * s && Math.Abs(t.Y - y) < 9 * s && (y < t.Y ? x > t.X + 0.15 * s : x < t.X - 0.15 * s))
                        .OrderBy(t => Math.Abs(t.Y - y)).FirstOrDefault();
                    if (h.Class == null) { if (Full) Trace.Add($"p{res.Page} {cls} at {x:F0},{y:F0}: no reader's stem there, no head in its column -> left out"); continue; }
                    st = Symbols.Nearest(staves, h.Y, s);
                    if (Full) Trace.Add($"p{res.Page} {cls} at {x:F0},{y:F0}: no reader's stem there; head at {h.X:F0},{h.Y:F0} -> s{st?.System + 1}");
                }
            }
            else if (cls.StartsWith("tuplet_"))
            {
                // a tuplet number stands off its staff too, on the stems' side, and a piece of ink
                // the reader read as one knows its staff; else the staff of the nearest head under or over it
                var mine = res.Sym.Tuplets.Where(o => Math.Abs(o.C.Cx - x) < 1.0 * s && Math.Abs(o.C.Cy - y) < 1.0 * s).FirstOrDefault();
                if (mine != null) st = mine.St;
                else
                {
                    var h = heads.Where(t => Math.Abs(t.X - x) < 2 * s && Math.Abs(t.Y - y) < 9 * s).OrderBy(t => Math.Abs(t.Y - y)).FirstOrDefault();
                    st = h.Class != null ? Symbols.Nearest(staves, h.Y, s) : Symbols.Nearest(staves, y, s);
                }
                if (Full)
                {
                    var near = res.Sym.Tuplets.OrderBy(o => Math.Abs(o.C.Cx - x) + Math.Abs(o.C.Cy - y)).FirstOrDefault();
                    Trace.Add($"p{res.Page} tuplet number at {x:F0},{y:F0}: {(mine != null ? "the reader's number" : "no reader's number within a space")}{(near != null ? $" (nearest at {near.C.Cx:F0},{near.C.Cy:F0} s{near.St.System + 1})" : "")} -> {(st == null ? "no staff" : $"s{st.System + 1} part {st.Part}")}");
                }
            }
            else
            {
                st = Symbols.Nearest(staves, y, s);
                // (a head the reader gave the next staff, its beam or ledgers being that staff's
                // (Symbols.BeamStaves), counts there; so do its dot and its sign)
                var moved = res.Sym.Heads.FirstOrDefault(h => h.Was != null && h.Was == st && Math.Abs(h.Y - y) < 0.6 * s
                    && (cls.StartsWith("head_") ? Math.Abs(h.X - x) < 0.6 * s : x > h.L - 3 * s && x < h.R + 2 * s));
                if (moved != null) st = moved.St;
            }
            var key = Locate(res, st, x, s);
            if (key == null) continue;
            // (stems and tuplet numbers are counted for the system's bar as a whole: a piano's
            // cross-staff stems belong to neither staff alone, and a number under one staff is nearer the next)
            if (cls.StartsWith("stem_") || cls.StartsWith("tuplet_")) key = (key.Value.Bar, AllParts);
            int pos = (int)Math.Round(st.Pos(y));
            string c = cls;
            // a long rest hangs under a line (whole) or sits on one (half): told apart by which side of the line it is
            if (c == "rest_long") { double p = st.Pos(y); c = p - 2 * Math.Round(p / 2) < 0 ? "rest_1" : "rest_2"; }
            if (!input.TryGetValue(key.Value, out var list)) input[key.Value] = list = new();
            list.Add(Item(c, pos));
        }
        foreach (var (st, x, y) in errors)
        {
            var key = Locate(res, st, x, s);
            if (key != null) wrong.Add(key.Value);
        }
    }

    /// <summary>What the learnt detector saw, in the classes compared (a key signature's accidentals left out).</summary>
    public static List<(string Class, double X, double Y)> Detected(PageResult res)
    {
        double s = res.S;
        var staves = res.Systems.SelectMany(x => x.Staves).ToList();
        var dets = res.Sym.Detections.Where(t => t.Score >= Detector.Threshold).ToList();
        var list = new List<(string, double, double)>();
        var headPts = dets.Where(t => t.Class.StartsWith("head_")).Select(t => (t.X, t.Y)).ToList();
        // a system's first measure starts with the key: the accidentals well before its first note or rest are the key's
        var firstX = new Dictionary<Staff, double>();
        foreach (var t in dets.Where(t => t.Class.StartsWith("head_") || t.Class.StartsWith("rest_")))
        {
            var st = Symbols.Nearest(staves, t.Y, s);
            if (st == null) continue;
            var sys = res.Systems[st.System];
            if (sys.Bars.Count > 1 && t.X > sys.Bars[1] - 0.3 * s) continue;
            if (!firstX.TryGetValue(st, out var fx) || t.X < fx) firstX[st] = t.X;
        }
        // a key the reader found within a system (a change of key, its naturals and its new signs): the
        // accidentals from it to a space and three quarters before the staff's next note are the key's
        var keys = res.Sym.Sigs.Where(g => g.Fifths != null && g.St != null).Select(g =>
        {
            double next = dets.Where(t => t.Class.StartsWith("head_") && t.X > g.X && Symbols.Nearest(staves, t.Y, s) == g.St).Select(t => t.X).DefaultIfEmpty(g.X + 12 * s).Min();
            return (g.St, From: g.X - 0.5 * s, To: next - 1.75 * s);
        }).ToList();
        // (a grace note the reader took is written as one, and its drawing's small head and stem are no
        // note's: the detector's head there, and a stem by it, are left out)
        // (but a stem the reader's note there has, its tip where the detector sees one, is that note's)
        bool Grace(Detection t) => res.Sym.Graces.Any(g => t.Class.StartsWith("head_") || t.Class == "grace_head" ? Math.Abs(g.X - t.X) < 0.6 * s && Math.Abs(g.Y - t.Y) < 0.6 * s
            : t.Class == "grace_slash" ? Math.Abs(g.X - t.X) < 2.5 * s && Math.Abs(g.Y - t.Y) < 4.5 * s
            : t.Class.StartsWith("stem_") && Math.Abs(g.X - t.X) < 1.2 * s && Math.Abs(g.Y - t.Y) < 4 * s
              && !res.Sym.Heads.Any(h => h.HasStem && Math.Abs(h.StemX - t.X) < 0.5 * s && Math.Abs(h.Tip - t.Y) < 1.5 * s));
        foreach (var t in dets)
        {
            string c = t.Class;
            if (res.Sym.Graces.Count > 0 && Grace(t)) continue;
            bool acc = c is "sharp" or "flat" or "natural" or "dsharp" or "dflat";
            if (c is "sharp" or "flat" or "natural" && keys.Count > 0 && Symbols.Nearest(staves, t.Y, s) is { } ks && keys.Any(k => k.St == ks && t.X >= k.From && t.X < k.To)) continue;
            // (a one-line rhythm staff's slashes carry no accidental: a sign on it is a key change's, which the reading writes by the melody's key)
            if (acc && Symbols.Nearest(staves, t.Y, s) is { OneLine: true }) continue;
            if (!(c.StartsWith("head_") || c.StartsWith("rest_") || acc || c == "dot" || c.StartsWith("stem_") || c.StartsWith("tuplet_") || c == "tie_l" || c == "slur_l")) continue;
            if (c == "tie_l" && !HeadBefore(headPts, t.X, t.Y, s)) continue;
            if (c == "slur_l")
            {
                if (!NoteBy(headPts, t.X, t.Y, s)) continue;
                // (in the bar of the note it leaves: past a last note's flag it can stand beyond the barline)
                var h = headPts.Where(h => Math.Abs(h.Y - t.Y) < 6 * s).OrderBy(h => Math.Abs(h.X - (t.X - 0.2 * s))).FirstOrDefault();
                list.Add((c, h == default ? t.X : h.X, t.Y));
                continue;
            }
            // (a key is sharps or flats only: a double or a natural is a note's whatever its place)
            if (c is "sharp" or "flat")
            {
                var st = Symbols.Nearest(staves, t.Y, s);
                if (st == null) continue;
                var sys = res.Systems[st.System];
                bool inFirst = sys.Bars.Count < 2 || t.X < sys.Bars[1] - 0.3 * s;
                // (a note's accidental stands about 1.2 spaces before its head's centre, a key's last sign 2.2 and more;
                // a double flat, wide, stands 1.8 before it)
                if (inFirst && (!firstX.TryGetValue(st, out var fx) ? t.X < st.Left + 10 * s : t.X < fx - 1.75 * s)) continue;
            }
            // (the detector was taught no stems on rhythm slashes -- the truth's stems went by the heads'
            // centres, a space off a slash's stem -- and sees them by halves: on a one-line rhythm staff the
            // reader's stems stand for the page's, as the rules trace them from the slash's corner: S17)
            if (c.StartsWith("stem_") && Symbols.Nearest(staves, t.Y, s) is { OneLine: true }) continue;
            // (nor is a dot with no slash before it on such a staff a note's: the period of a verse's
            // number, "2.", between the melody's staff and the rhythm staff)
            if (c == "dot" && Symbols.Nearest(staves, t.Y, s) is { OneLine: true } ls
                && !res.Sym.Heads.Any(h => h.St == ls && t.X > h.L && t.X < h.R + 2.5 * s && Math.Abs(t.Y - h.Y) < 1.0 * s)) continue;
            if (Symbols.DebugAt is { } at && Math.Abs(t.X - at.X) < 3 * s && Math.Abs(t.Y - at.Y) < 6 * s) Console.WriteLine($"redraw check's input: {c} at {t.X:F0},{t.Y:F0} ({t.Score:F2})");
            list.Add((c, t.X, t.Y));
        }
        var slashStems = new HashSet<(Staff, int, bool)>();
        foreach (var h in res.Sym.Heads.Where(h => h.Slash && h.HasStem && h.St.OneLine))
            if (slashStems.Add((h.St, (int)Math.Round(h.StemX / (0.3 * s)), h.Up)))
            {
                if (Symbols.DebugAt is { } at && Math.Abs(h.X - at.X) < 3 * s && Math.Abs(h.Y - at.Y) < 6 * s) Console.WriteLine($"redraw check's input: the reader's slash stem at {h.StemX:F0},{h.Tip:F0}");
                list.Add(($"stem_{Math.Min(3, res.Sym.Heads.Where(g => g.HasStem && g.St == h.St && g.Up == h.Up && Math.Abs(g.StemX - h.StemX) < 0.3 * s).Max(g => g.Beams))}", h.StemX, h.Tip));
            }
        // (and a weak dot in the column of a sure one: the other voice's, lined up with a chord's)
        foreach (var t in Symbols.ColumnDots(res.Sym, s)) list.Add(("dot", t.X, t.Y));
        // (a beat repeat the reader wrote out shows its group again: the group's heads, stems and dots
        // moved onto the mark -- not its accidentals, which hold through the bar and are not drawn again)
        var shown = list.ToList();
        foreach (var b in res.Sym.BeatRepeats.Where(b => b.Played != null))
        {
            var (l, r) = b.Played.Value;
            double gl = l - 0.8 * s, gr = r + 0.8 * s;
            foreach (var (c, x, y) in shown)
                if ((c.StartsWith("head_") || c.StartsWith("stem_") || c == "dot") && x > gl && x < gr && Symbols.Nearest(staves, y, s) == b.St)
                {
                    // (squeezed onto the mark: in the bar, whichever side of it the group stood)
                    double nx = b.L + (x - gl) / (gr - gl) * (b.R - b.L);
                    if (Symbols.DebugAt is { } at && Math.Abs(b.L - at.X) < 3 * s && Math.Abs(y - at.Y) < 6 * s) Console.WriteLine($"redraw check's input: {c} at {x:F0},{y:F0} again at {nx:F0} (the beat repeat at {b.L:F0}-{b.R:F0})");
                    list.Add((c, nx, y));
                }
        }
        return list;
    }

    /// <summary>The reader's own symbols, in the classes compared (what the .lys was written from).</summary>
    public static List<(string Class, double X, double Y)> ReadSymbols(PageResult res)
    {
        double s = res.S;
        var list = new List<(string, double, double)>();
        var stems = new HashSet<(Staff, int, bool)>();
        foreach (var h in res.Sym.Heads)
        {
            // (a rhythm slash is compared by its stem and dots only, as the redraw's slashes are no heads compared)
            if (!h.Slash) list.Add((h.Hollow ? (h.HasStem ? "head_half" : "head_whole") : "head_black", h.X, h.Y));
            if (h.Acc.HasValue) list.Add((AccName(h.Acc.Value), h.X - s, h.Y));
            // (at the head itself: a tie mostly leaves the bar's last note, and a space to the right is past the barline)
            if (h.Tie) list.Add(("tie_l", h.X, h.Y));
            // (one to a chord: its heads all carry the slur's start)
            if (h.SlurStart && !res.Sym.Heads.Any(o => o != h && o.SlurStart && o.St == h.St && Math.Abs(o.X - h.X) < 0.5 * s && (o.Y < h.Y || o.Y == h.Y && o.X < h.X)))
                list.Add(("slur_l", h.X, h.Y));
            // a dot stands in the space at the head, or the one above a head on a line
            for (int i = 0; i < h.Dots; i++) list.Add(("dot", h.X + s, h.St.Mid - (h.Pos % 2 == 0 ? h.Pos + 1 : h.Pos) * s / 2));
            if (h.HasStem)
            {
                var k = (h.St, (int)Math.Round(h.StemX / (0.3 * s)), h.Up);
                if (stems.Add(k)) list.Add(($"stem_{Math.Min(3, res.Sym.Heads.Where(g => g.HasStem && g.St == h.St && g.Up == h.Up && Math.Abs(g.StemX - h.StemX) < 0.3 * s).Max(g => g.Beams))}", h.StemX, h.Tip));
            }
        }
        foreach (var r in res.Sym.Rests)
        {
            list.Add((r.Kind switch { RestKind.Whole => "rest_1", RestKind.Half => "rest_2", RestKind.Quarter => "rest_4", RestKind.Eighth => "rest_8", _ => "rest_16" }, r.X, r.Y));
            for (int i = 0; i < r.Dots; i++) list.Add(("dot", r.X + s, r.St.Mid - s / 2));
        }
        return list;
    }

    /// <summary>
    /// A symbol as compared: a rest, a dot, a stem's tip, a tuplet number or a tie counts by kind alone
    /// (a second voice's rests and their dots move a space or two; a dot on the wrong note shows in
    /// the bar's length or its stems; a tie's end is not on its head's line).
    /// </summary>
    static (string Class, int Pos) Item(string c, int pos) => (c, ByKind(c) ? 0 : pos);

    static bool ByKind(string c) => c.StartsWith("rest_") || c == "dot" || c.StartsWith("stem_") || c.StartsWith("tuplet_") || c.StartsWith("tie_") || c.StartsWith("slur_");

    /// <summary>
    /// Whether a head stands just before a point: a tie's left end is counted only then. The piece
    /// of a tie broken at a line's end that begins the next line starts at the clef, from no head.
    /// (Lily# starts a tie 0.2 to 1.0 space right of the head's centre, up to a space above or below it.)
    /// </summary>
    static bool HeadBefore(List<(double X, double Y)> heads, double x, double y, double s) =>
        heads.Any(h => h.X < x && x - h.X < 2.0 * s && Math.Abs(h.Y - y) <= 1.25 * s);

    /// <summary>
    /// Whether a note stands by a point: a slur's left end is counted only then (within a space and a
    /// half of the note's x, over or under it, past its stem's tip). The piece of a slur broken at a
    /// line's end that begins the next line starts before the first note.
    /// </summary>
    static bool NoteBy(List<(double X, double Y)> heads, double x, double y, double s) =>
        heads.Any(h => Math.Abs(h.X - x) < 1.5 * s && Math.Abs(h.Y - y) < 6 * s);

    static int? AccAlter(string c) => c switch { "sharp" => 1, "flat" => -1, "natural" => 0, "dsharp" => 2, "dflat" => -2, _ => null };

    static readonly System.Text.RegularExpressions.Regex noteToken = new(@"\G[a-g]((?:is|es)*)", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>The alteration of the note token at a source position of the text (null when no note starts there).</summary>
    static int? TokenAlter(string text, int src)
    {
        if (src < 0 || src >= text.Length) return null;
        var m = noteToken.Match(text, src);
        if (!m.Success) return null;
        string acc = m.Groups[1].Value;
        int alter = 0;
        for (int i = 0; i + 1 < acc.Length; i += 2) alter += acc.Substring(i, 2) == "is" ? 1 : -1;
        return alter;
    }

    static readonly System.Text.RegularExpressions.Regex stepToken = new(@"\G([a-g])(?:is|es)*([',]*)", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>The note token's step at a source position of an absolute-octave text, c' = 28 (null when no note starts there).</summary>
    static int? TokenStep(string text, int src)
    {
        if (src < 0 || src >= text.Length) return null;
        var m = stepToken.Match(text, src);
        if (!m.Success) return null;
        int oct = 3 + m.Groups[2].Value.Count(c => c == '\'') - m.Groups[2].Value.Count(c => c == ',');
        return 7 * oct + "cdefgab".IndexOf(m.Groups[1].Value[0]);
    }

    /// <summary>A step as the text writes the note (no alteration): 28 is c', 22 is d.</summary>
    static string StepName(int step)
    {
        int oct = (int)Math.Floor(step / 7.0);
        return "cdefgab"[step - 7 * oct] + (oct > 3 ? new string('\'', oct - 3) : new string(',', 3 - oct));
    }

    static string AccName(int acc) => acc switch { 1 => "sharp", -1 => "flat", 0 => "natural", 2 => "dsharp", -2 => "dflat", _ => "sharp" };

    // ---- the drawing's side (the truth's SVG or the reading's)

    /// <summary>
    /// The symbols Lily# drew, in the classes compared, each with the source position it came
    /// from: heads, the accidentals of notes (a key signature's share no note's source), dots,
    /// rests, stems (the source of a head on them), tuplet numbers, ties (their left end, from a head).
    /// </summary>
    public static List<(string Class, double X, double Y, int Src)> SvgSymbols(SvgPage svg, double s)
    {
        var list = new List<(string, double, double, int)>();
        var music = svg.Glyphs.Where(g => g.Full).ToList();
        var heads = music.Where(g => g.Name.StartsWith("noteheads.")).ToList();
        var headSrc = heads.Where(h => h.Src >= 0).Select(h => h.Src).ToHashSet();
        // a dot carries no source position: it takes that of the head or rest it stands after
        // (the nearest on its left, level with it: a head's dot sits in the space at or above it, a rest's above its middle)
        var dotted = music.Where(g => g.Src >= 0 && (g.Name.StartsWith("noteheads.") || g.Name.StartsWith("rests."))).ToList();
        int DotSrc(TGlyph d)
        {
            TGlyph best = null;
            foreach (var g in dotted)
            {
                // (a rhythm slash's dot is set at its upper right end, within the slash's own extent: S17)
                if (d.L < g.R - (g.Name.Contains("slash") ? 0.8 : 0.2) * s || d.L - g.R > 2.5 * s) continue;
                if (Math.Abs(d.Cy - g.Cy) > (g.Name.StartsWith("rests.") ? 1.5 : 0.75) * s) continue;
                if (best == null || g.R > best.R) best = g;
            }
            return best?.Src ?? -1;
        }
        foreach (var g in music)
        {
            string c = g.Name switch
            {
                "noteheads.s2" => "head_black", "noteheads.s1" => "head_half", "noteheads.s0" => "head_whole",
                "rests.0" => "rest_1", "rests.1" => "rest_2", "rests.2" => "rest_4", "rests.3" => "rest_8", "rests.4" => "rest_16", "rests.5" => "rest_32",
                "accidentals.sharp" => "sharp", "accidentals.flat" => "flat", "accidentals.natural" => "natural",
                "accidentals.doublesharp" => "dsharp", "accidentals.flatflat" => "dflat",
                "dots.dot" => "dot",
                _ => null,
            };
            if (c == null) continue;
            if (c is "sharp" or "flat" or "natural" or "dsharp" or "dflat" && !headSrc.Contains(g.Src)) continue;
            int src = g.Src;
            if (src < 0 && c == "dot") src = DotSrc(g);
            // a full-bar rest (R1) carries no source position either: the barline closing its bar does
            if (src < 0 && c.StartsWith("rest_")) src = BarlineSrc(svg, g, s);
            // (two voices' unison is one head drawn twice: the page shows it once)
            if (c.StartsWith("head_") && list.Any(t => t.Item1 == c && Math.Abs(t.Item2 - g.Cx) < 0.3 * s && Math.Abs(t.Item3 - g.Cy) < 0.3 * s)) continue;
            list.Add((c, g.Cx, g.Cy, src));
        }
        foreach (var (x, y, n, src) in SymEval.StemTailsSrc(svg, s)) list.Add(($"stem_{n}", x, y, src));
        foreach (var t in svg.Tuplets) list.Add(($"tuplet_{t.Text}", t.X, t.Y, t.Src));
        var headPts = heads.Select(h => (h.Cx, h.Cy)).ToList();
        foreach (var t in svg.Ties)
            if (HeadBefore(headPts, t.X1, t.Y1, s)) list.Add(("tie_l", t.X1, t.Y1, t.Src));
        foreach (var t in svg.Slurs)
            if (NoteBy(headPts, t.X1, t.Y1, s)) list.Add(("slur_l", t.X1, t.Y1, t.Src));
        return list;
    }

    /// <summary>The source position of the nearest barline right of a glyph on its staff (-1: none).</summary>
    static int BarlineSrc(SvgPage svg, TGlyph g, double s)
    {
        double u = svg.Ppu;
        int src = -1; double best = double.MaxValue;
        foreach (var r in svg.Rects)
        {
            if (r.Src < 0 || r.W > 0.35 * u || r.H < 3.5 * u || r.H > 4.5 * u) continue;
            double cx = r.X + r.W / 2, cy = r.Y + r.H / 2;
            if (cx < g.Cx || Math.Abs(cy - g.Cy) > 3 * s) continue;
            if (cx - g.Cx < best) { best = cx - g.Cx; src = r.Src; }
        }
        return src;
    }

    /// <summary>A staff of the drawing: five long horizontal lines a space apart, or one line on its own (a rhythm staff, "as lines 1").</summary>
    sealed class DStaff { public double Top, Mid, S; public int Index; public double Pos(double y) => (Mid - y) / (S / 2); }

    static List<DStaff> DrawnStaves(SvgPage svg, double s)
    {
        var ys = svg.Lines.Where(l => Math.Abs(l.Y1 - l.Y2) < 0.01 * s && Math.Abs(l.X2 - l.X1) > 10 * s).Select(l => l.Y1).OrderBy(y => y).ToList();
        var staves = new List<DStaff>();
        for (int i = 0; i < ys.Count;)
        {
            bool five = i + 4 < ys.Count;
            for (int k = 1; k <= 4 && five; k++) five = Math.Abs(ys[i + k] - ys[i + k - 1] - s) < 0.1 * s;
            if (five) { staves.Add(new DStaff { Top = ys[i], Mid = ys[i + 2], S = (ys[i + 4] - ys[i]) / 4, Index = staves.Count }); i += 5; }
            // (a line with no other within two spaces: a one-line staff, its positions by the page's space -- S17)
            else if ((i == 0 || ys[i] - ys[i - 1] > 2 * s) && (i + 1 >= ys.Count || ys[i + 1] - ys[i] > 2 * s))
            { staves.Add(new DStaff { Top = ys[i] - 2 * s, Mid = ys[i], S = s, Index = staves.Count }); i++; }
            else i++;
        }
        return staves;
    }

    /// <summary>
    /// Engraves the .lys again and compares. spans: where in the .lys each part-bar's music is
    /// (character offsets; Lily#'s data-pos). Returns the summary lines; the diffs go to the report.
    /// </summary>
    public List<string> Compare(string lysPath, Dictionary<(int Bar, int Part), (int Start, int End)> spans, ScoreConfig cfg, List<string> report)
    {
        var lines = new List<string>();
        string dir = Path.GetDirectoryName(Path.GetFullPath(lysPath));
        string svgPath = Path.Combine(dir, Path.GetFileNameWithoutExtension(lysPath) + ".svg");
        if (File.Exists(svgPath)) File.Delete(svgPath);
        var sw = Stopwatch.StartNew();
        var psi = new ProcessStartInfo(LyscPath) { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        psi.ArgumentList.Add("svg"); psi.ArgumentList.Add("-d"); psi.ArgumentList.Add(dir); psi.ArgumentList.Add(Path.GetFullPath(lysPath));
        string log;
        using (var p = Process.Start(psi))
        {
            p.StandardInput.Close();
            var outTask = p.StandardOutput.ReadToEndAsync();
            var errTask = p.StandardError.ReadToEndAsync();
            p.WaitForExit();
            log = outTask.Result + errTask.Result;
        }
        File.WriteAllText(Path.Combine(dir, "verify-lysc.log"), log);
        // (lysc draws what parsed of a file with errors too: after a parser's or a declaration's error
        // such a drawing is no reading to compare. A bar's error (LYS2xxx: a bar line missed, LYS2017)
        // or a span's (LYS4xxx: a slur across a repeat, a pedal left open) leaves the music drawn whole,
        // every note kept: compared all the same, the place it names is a todo -- the comparison with
        // other OMRs, 2026-10-26: refusing them left the check off on 33 of the 66 held-out sets, the hard ones)
        LyscError = log.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => System.Text.RegularExpressions.Regex.IsMatch(l, @"\berror LYS\d+"));
        var errors = System.Text.RegularExpressions.Regex.Matches(log, @"\berror LYS(\d{4}): (.+?)(?: at \[(\d+)\.\.\d+\))?\r?$", System.Text.RegularExpressions.RegexOptions.Multiline)
            .Select(m => (Code: m.Groups[1].Value, Text: m.Groups[2].Value, At: m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : -1)).ToList();
        Drawn = File.Exists(svgPath) && errors.All(e => e.Code[0] is '2' or '4' && e.At >= 0);
        if (!Drawn)
        {
            lines.Add($"verify: lysc drew nothing{(LyscError != null ? " to compare (errors)" : "")} for {lysPath} ({sw.Elapsed.TotalSeconds:F1}s); see verify-lysc.log");
            return lines;
        }
        var svg = SvgPage.Load(svgPath, 1000);
        double s = svg.Ppu;      // Lily#'s unit is the staff space
        var staves = DrawnStaves(svg, s);
        int parts = cfg.Parts.Count;
        var spanList = spans.Select(kv => (kv.Value.Start, kv.Value.End, kv.Key)).OrderBy(t => t.Start).ToList();
        (int Bar, int Part)? SpanOf(int src)
        {
            if (src < 0) return null;
            int lo = 0, hi = spanList.Count - 1;
            while (lo <= hi)
            {
                int mid = (lo + hi) / 2;
                if (src < spanList[mid].Start) hi = mid - 1;
                else if (src >= spanList[mid].End) lo = mid + 1;
                else return spanList[mid].Key;
            }
            return null;
        }
        // the reading, per part-bar; and its heads' alterations (from their tokens in the text: a
        // printed accidental the reading lacks is a courtesy one when the reading's note at that
        // place already has that alteration from the key or the bar)
        var reading = new SortedDictionary<(int Bar, int Part), List<(string Class, int Pos)>>();
        var alters = new Dictionary<(int Bar, int Part), List<(int Pos, int Alter)>>();
        // (and their steps, which name a head only the scan shows by its place beside them: a
        // second's head dropped from a chord. Only an absolute-octave text, as the reader writes)
        var steps = new Dictionary<(int Bar, int Part), List<(int Pos, int Step)>>();
        string text = File.ReadAllText(lysPath);
        bool absolute = System.Text.RegularExpressions.Regex.IsMatch(text, @"(?m)^\s*octave\s+absolute\b");
        int unplaced = 0;
        foreach (var (c, x, y, src) in SvgSymbols(svg, s))
        {
            var key = SpanOf(src);
            if (key == null) { if (c.StartsWith("head_")) unplaced++; continue; }
            if (c.StartsWith("stem_") || c.StartsWith("tuplet_"))
            {
                var sk = (key.Value.Bar, AllParts);
                if (!reading.TryGetValue(sk, out var sl)) reading[sk] = sl = new();
                sl.Add(Item(c, 0));
                continue;
            }
            // the staff of that part nearest the symbol (staves come in systems of one staff per part)
            DStaff st = null;
            double bd = double.MaxValue;
            foreach (var d in staves)
            {
                if (staves.Count % parts == 0 && d.Index % parts != key.Value.Part) continue;
                double dd = Math.Abs(d.Mid - y);
                if (dd < bd) { bd = dd; st = d; }
            }
            if (st == null) continue;
            if (!reading.TryGetValue(key.Value, out var list)) reading[key.Value] = list = new();
            int pos = (int)Math.Round(st.Pos(y));
            list.Add(Item(c, pos));
            if (c.StartsWith("head_") && TokenAlter(text, src) is { } alter)
            {
                if (!alters.TryGetValue(key.Value, out var al)) alters[key.Value] = al = new();
                al.Add((pos, alter));
            }
            if (absolute && c.StartsWith("head_") && TokenStep(text, src) is { } step)
            {
                if (!steps.TryGetValue(key.Value, out var sl)) steps[key.Value] = sl = new();
                sl.Add((pos, step));
            }
        }
        foreach (var key in spans.Keys) if (!reading.ContainsKey(key)) reading[key] = new();
        if (Full) report.AddRange(Trace);
        // (a bar lysc finds too short or too long: "warning LYS2001: Measure duration 3/4 is less than time signature 4/4 at [112..123)",
        // LYS2002 "… exceeds time signature 4/4"; said as the reading says it)
        if (LengthsFromLysc)
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(log, @"warning LYS200[12]: (.+?) at \[(\d+)\.\."))
                if (SpanOf(int.Parse(m.Groups[2].Value)) is { } lk) (lengths[lk], lengthOff[lk]) = Memo.LyscLength(m.Groups[1].Value);

        // compare, part-bar by part-bar
        var flagged = new SortedSet<(int Bar, int Part)>();
        int onlyReading = 0, onlyInput = 0, byLength = 0, byTab = 0;
        var skip = Source == "reader" ? new HashSet<string> { "tuplet" } : new HashSet<string>();
        foreach (var key in reading.Keys.Union(input.Keys).OrderBy(k => k))
        {
            // (the detector knows the numbers 3, 5, 6 and 7 only: another the reading draws, a 4 over a quadruplet, it cannot show)
            var a = (reading.GetValueOrDefault(key) ?? new()).Where(t => !skip.Contains(t.Class.Split('_')[0])
                && !(Source == "detector" && t.Class.StartsWith("tuplet_") && !Detector.Classes.Contains(t.Class))).ToList();
            var b = (input.GetValueOrDefault(key) ?? new()).Where(t => !skip.Contains(t.Class.Split('_')[0])).ToList();
            var (ra, rb) = Unmatched(a, b);
            // a printed accidental the reading does not draw, on a note the reading already alters so: a courtesy accidental
            if (alters.TryGetValue(key, out var al))
                rb.RemoveAll(t => AccAlter(t.Class) is { } acc && al.Any(h => Math.Abs(h.Pos - t.Pos) <= 1 && h.Alter == acc));
            string partId = key.Part == AllParts ? "(stems and tuplet numbers)" : key.Part < cfg.Parts.Count ? cfg.Parts[key.Part].Id : key.Part.ToString();
            if (Full) report.Add($"bar {key.Bar} {partId}: reading {Show(a)} | input {Show(b)}");
            // a bar whose events do not fill the time is wrong somewhere, whatever the drawings say
            if (lengths.TryGetValue(key, out var len))
            {
                flagged.Add(key); byLength++;
                Flag(key, len, "length", 0, 0, lengthOff.GetValueOrDefault(key, 0.5));
                report.Add($"bar {key.Bar} {partId}: {len} | {where.GetValueOrDefault(key, "not on the pages read")}");
            }
            // a staff the TAB under it disagrees with (what the drawings compare cannot see: the
            // reading is drawn as read, and a head's pitch is not among the classes the input shows)
            if (tabs.TryGetValue(key, out var tab))
            {
                flagged.Add(key); byTab++;
                Flag(key, tab, "tab");
                report.Add($"bar {key.Bar} {partId}: {tab} | {where.GetValueOrDefault(key, "not on the pages read")}");
            }
            if (ra.Count == 0 && rb.Count == 0) continue;
            flagged.Add(key);
            onlyReading += ra.Count; onlyInput += rb.Count;
            // (what kind of difference: a note, rest, sign or stem is the music read wrong; a slur or tie
            // the reading missed or added changes no note; a dot only the scan shows is as often a staccato)
            int serious = ra.Count(t => t.Class is not ("slur_l" or "tie_l")) + rb.Count(t => t.Class is not ("slur_l" or "tie_l" or "dot"));
            // (a bar the scan shows nothing in, the text a whole-bar rest for it: nothing to correct)
            bool blank = rb.Count == 0 && ra.Count > 0 && ra.All(t => t.Class == "rest_1") && (!input.TryGetValue(key, out var shown) || shown.Count == 0);
            string kind = blank ? "blank" : serious > 0 ? "diff" : ra.Concat(rb).Any(t => t.Class == "tie_l") ? "tie" : rb.Any(t => t.Class == "dot") ? "dot" : "slur";
            if (blank) serious = 0;
            // (and how much of the part-bar does match: one symbol wrong among twenty is surer to be a slip than three among four;
            // a difference that changes no note counts its own symbols)
            Func<int, string> named = null;
            if (steps.TryGetValue(key, out var known) && known.Count > 0)
                named = pos => { var r = known.MinBy(k => Math.Abs(k.Pos - pos)); return StepName(r.Step + pos - r.Pos); };
            Flag(key, Memo.Differences(ra, rb, key.Part == AllParts, named), kind, serious > 0 ? serious : ra.Count + rb.Count, a.Count - ra.Count);
            report.Add($"bar {key.Bar} {partId}: reading has {Show(ra)}; input shows {Show(rb)} | {where.GetValueOrDefault(key, "not on the pages read")}");
        }
        // a part-bar lysc finds an error in: a bar line missed (two bars' worth between two lines, LYS2017) is
        // the reading's slip there, near certain; a slur or pedal it will not draw is a mark to look at
        int byLysc = 0;
        foreach (var e in errors)
        {
            if (SpanOf(e.At) is not { } ek) continue;
            if (flags.TryGetValue(ek, out var had) && had.Any(f => f.Kind is "barline" or "lysc")) continue;
            flagged.Add(ek); byLysc++;
            string partId = ek.Part < cfg.Parts.Count ? cfg.Parts[ek.Part].Id : ek.Part.ToString();
            if (e.Code == "2017") Flag(ek, "two bars' worth with no bar line between: a bar line missed? (lysc LYS2017)", "barline");
            else Flag(ek, $"lysc LYS{e.Code}: {(e.Text.Length > 80 ? e.Text[..80] + "..." : e.Text)}", "lysc");
            report.Add($"bar {ek.Bar} {partId}: lysc error LYS{e.Code} | {where.GetValueOrDefault(ek, "not on the pages read")}");
        }
        // (a flag on a bar's stems or tuplet numbers is one place to look at with the bar's part flags, not another)
        var places = flagged.Where(k => k.Part != AllParts || !flagged.Any(f => f.Bar == k.Bar && f.Part != AllParts)).ToList();
        lines.Add($"verify: input = {Source}; {reading.Keys.Count(k => k.Part != AllParts)} part-bars, {places.Count} flagged ({byLength} by length{(byTab > 0 ? $", {byTab} by TAB" : "")}{(byLysc > 0 ? $", {byLysc} by lysc's errors" : "")}); the reading has {onlyReading} symbols the input does not show, the input {onlyInput} the reading lacks{(unplaced > 0 ? $"; {unplaced} drawn heads from no bar" : "")}; lysc {sw.Elapsed.TotalSeconds:F1}s");
        if (Checked)
        {
            // (a flag on a bar's stems or tuplet numbers hits when any part of the bar is wrong)
            bool Hit((int Bar, int Part) k) => k.Part == AllParts ? wrong.Any(w => w.Bar == k.Bar) : wrong.Contains(k);
            bool Flagged((int Bar, int Part) k) => flagged.Contains(k) || flagged.Contains((k.Bar, AllParts));
            int both = places.Count(Hit);
            lines.Add($"verify vs truth: {wrong.Count} part-bars wrong, {places.Count} flagged, {both} both");
            var missed = wrong.Where(k => !Flagged(k)).OrderBy(k => k).ToList();
            var spare = flagged.Where(k => !Hit(k)).ToList();
            string Id(int part) => part == AllParts ? "stems" : cfg.Parts[part].Id;
            if (missed.Count > 0) report.Add("wrong, not flagged: " + string.Join(" ", missed.Select(k => $"{k.Bar}/{Id(k.Part)}")));
            if (spare.Count > 0) report.Add("flagged, not wrong: " + string.Join(" ", spare.Select(k => $"{k.Bar}/{Id(k.Part)}")));
        }
        return lines;
    }

    /// <summary>The flagged part-bars and what was found there (part -1: the bar's stems or tuplet numbers, over the system).</summary>
    public IEnumerable<(int Bar, int Part, string Notes)> Flagged => flags.Select(kv => (kv.Key.Bar, kv.Key.Part, string.Join("; ", kv.Value.Select(f => f.Note))));

    /// <summary>
    /// The flagged part-bars as todos: their words, how likely they need no correcting
    /// (Confidence), and whether the drawings differ there (Diff: a symbol or the TAB, not the length alone).
    /// A flag on the system's stems and tuplet numbers alone is 0.8 at the least, kept in the side file
    /// only: it rarely points at a bar read wrong (the development set's engraved pages 3 of 46, its scans
    /// 10 of 33, no better than any bar: a tremolo or a beat repeat drawn otherwise than printed, a stem the
    /// detector sees not as drawn), where a part's own flag in the bar says what is wrong.
    /// </summary>
    public IEnumerable<(int Bar, int Part, string Notes, double Confidence, bool Diff)> Places =>
        flags.Select(kv => (kv.Key.Bar, kv.Key.Part, string.Join("; ", kv.Value.Select(f => f.Note)),
                            kv.Key.Part == AllParts ? Math.Max(0.8, Confidence(kv.Value)) : Confidence(kv.Value), kv.Value.Any(f => f.Kind != "length")));

    /// <summary>
    /// How likely a flagged part-bar needs no correcting (0..1, README "How sure a todo is"): a
    /// note, rest, sign or stem of the .lys's drawing not matching the page is strong evidence, so
    /// 0.3 at most, times the share of the part-bar's symbols that do match; a TAB fret against
    /// its note 0.3; the length alone 0.3 down to 0.1 as it is off by more. A difference that
    /// changes no note: a tie 0.6-0.7, a dot only the scan shows (a staccato as often as not)
    /// 0.8-0.85, a slur 0.85-0.9, a whole-bar rest the text has for a bar the scan shows nothing in 0.85-0.9.
    /// lysc's errors in the .lys: two bars' worth with no bar line between (LYS2017) 0.15, as near certain a
    /// slip as a note drawn wrong; another (a slur across a repeat, a pedal left open) 0.5.
    /// </summary>
    public static double Confidence(IEnumerable<(string Note, string Kind, int Count, int Matched, double Off)> found)
    {
        // (graded, not stepped: two bars wrong alike would otherwise share one value, and a threshold
        // shows or hides them all at once. By the share of the part-bar's symbols that match, a
        // half-symbol given to each side so that none wrong ... all wrong still tell apart: one wrong
        // among twenty 0.28, half 0.15, one with nothing matching 0.08, ten with nothing 0.01)
        static double Share(int wrong, int matched) => (matched + 0.5) / (wrong + matched + 1);
        var diff = found.Where(f => f.Kind == "diff").ToList();
        if (diff.Count > 0) return Math.Round(0.3 * Share(diff.Sum(f => f.Count), diff.Sum(f => f.Matched)), 2);
        var kinds = found.Select(f => f.Kind).ToHashSet();
        double Of(string kind, double from, double span) =>
            found.Where(f => f.Kind == kind).Select(f => Math.Round(from + span * Share(f.Count, f.Matched), 2)).DefaultIfEmpty(1).Min();
        // (the length alone: 0.3 off by little, down to 0.1 off by a bar)
        double length = found.Where(f => f.Kind == "length").Select(f => Math.Round(0.3 - 0.2 * Math.Clamp(f.Off, 0, 1), 2)).DefaultIfEmpty(0.2).Min();
        // (a difference that changes no note, within its band by the same share: a tie 0.6-0.7, a dot
        // 0.8-0.85, a slur or a blank bar 0.85-0.9; a dot the scan shows in a bar too short is the dot
        // the length lacks: the length says it)
        return new[] { ("barline", 0.15), ("lysc", 0.5), ("tab", 0.3), ("length", length), ("tie", Of("tie", 0.6, 0.1)), ("dot", Of("dot", 0.8, 0.05)), ("slur", Of("slur", 0.85, 0.05)), ("blank", Of("blank", 0.85, 0.05)) }
            .Where(k => kinds.Contains(k.Item1)).Select(k => k.Item2).DefaultIfEmpty(0.2).Min();
    }

    void Flag((int Bar, int Part) key, string note, string kind, int count = 0, int matched = 0, double off = 0)
    {
        if (!flags.TryGetValue(key, out var list)) flags[key] = list = new();
        list.Add((note, kind, count, matched, off));
    }

    // ---- for the user: where to look

    /// <summary>
    /// Draws the flagged part-bars on their pages (a red box and what was found there) and saves
    /// the pages as verify-pNN.png. Returns the files written.
    /// </summary>
    public List<string> SaveMarks(string outDir)
    {
        var written = new List<string>();
        foreach (var page in flags.Keys.Where(box.ContainsKey).Select(k => box[k].Page).Distinct().OrderBy(p => p))
        {
            if (!pagePaths.TryGetValue(page, out var path)) continue;
            var marks = flags.Where(kv => box.TryGetValue(kv.Key, out var b) && b.Page == page).Select(kv =>
            {
                var b = box[kv.Key];
                // (a stems flag spans the system: its label goes under it, the parts' over theirs)
                return (new[] { b.X0, b.Y0, b.X1, b.Y1 }, $"{kv.Key.Bar}: {string.Join("; ", kv.Value.Select(f => f.Note))}", Confidence(kv.Value), kv.Key.Part == AllParts);
            });
            string file = Path.Combine(outDir, $"verify-p{page:D2}.png");
            DrawMarks(path, file, marks);
            written.Add(file);
        }
        return written;
    }

    /// <summary>
    /// A page with boxes to look at, saved as <paramref name="file"/>: each a red outline (no fill:
    /// the music shows through), thicker the less sure the todo is (confidence 0: 3 px, 1: 1 px, on
    /// a 2500 px page), and its words over it (under it, for a box spanning the system).
    /// </summary>
    public static void DrawMarks(string pagePath, string file, IEnumerable<(double[] Box, string Label, double Confidence, bool Below)> marks)
    {
        using var src = new System.Drawing.Bitmap(pagePath);
        using var bmp = new System.Drawing.Bitmap(src.Width, src.Height, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
        using (var g = System.Drawing.Graphics.FromImage(bmp))
        {
            g.DrawImage(src, 0, 0, src.Width, src.Height);
            float size = Math.Max(10f, src.Width / 110f), scale = Math.Max(1f, src.Width / 2500f);
            using var font = new System.Drawing.Font("Arial", size, System.Drawing.GraphicsUnit.Pixel);
            foreach (var (b, text, confidence, below) in marks)
            {
                var r = new System.Drawing.RectangleF((float)b[0], (float)b[1], (float)(b[2] - b[0]), (float)(b[3] - b[1]));
                using var pen = new System.Drawing.Pen(System.Drawing.Color.Red, Math.Max(1f, (float)(3 - 2 * Math.Clamp(confidence, 0, 1)) * scale));
                g.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height);
                string label = text.Length > 80 ? text[..77] + "..." : text;
                float ty = below ? r.Bottom + 2 : r.Y - size - 2;
                g.DrawString(label, font, System.Drawing.Brushes.Red, r.X, Math.Max(0, ty));
            }
        }
        bmp.Save(file, System.Drawing.Imaging.ImageFormat.Png);
    }

    /// <summary>
    /// What is in one list and not the other: exact matches cancel first, then an accidental of
    /// the same kind within a half-space (its drawn centre is not its note's line).
    /// </summary>
    static (List<(string Class, int Pos)> A, List<(string Class, int Pos)> B) Unmatched(List<(string Class, int Pos)> a, List<(string Class, int Pos)> b)
    {
        var ra = new List<(string, int)>(a);
        var rb = new List<(string, int)>(b);
        for (int i = ra.Count - 1; i >= 0; i--)
        {
            int j = rb.IndexOf(ra[i]);
            if (j >= 0) { ra.RemoveAt(i); rb.RemoveAt(j); }
        }
        static bool Loose(string c) => c is "sharp" or "flat" or "natural" or "dsharp" or "dflat";
        for (int i = ra.Count - 1; i >= 0; i--)
        {
            if (!Loose(ra[i].Item1)) continue;
            int j = rb.FindIndex(t => t.Item1 == ra[i].Item1 && Math.Abs(t.Item2 - ra[i].Item2) <= 1);
            if (j >= 0) { ra.RemoveAt(i); rb.RemoveAt(j); }
        }
        // a slur the reading drew where the scan shows a tie's start and no tie was read: the reader
        // takes a tie's start that ties nothing for a slur's (Symbols.DetectedSlurs), and so does this
        for (int i = ra.Count - 1; i >= 0; i--)
        {
            if (ra[i].Item1 != "slur_l") continue;
            int j = rb.FindIndex(t => t.Item1 == "tie_l");
            if (j >= 0) { ra.RemoveAt(i); rb.RemoveAt(j); }
        }
        return (ra, rb);
    }

    static string Show(List<(string Class, int Pos)> items) => items.Count == 0 ? "-"
        : string.Join(" ", items.GroupBy(t => ByKind(t.Class) ? t.Class : $"{t.Class}@{t.Pos}")
            .OrderBy(g => g.Key).Select(g => g.Count() > 1 ? $"{g.Key}x{g.Count()}" : g.Key));
}
