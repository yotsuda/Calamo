using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Omr;

/// <summary>
/// <c>omr read &lt;image&gt;... --out &lt;dir&gt; [--name base] [--lysc path] [--verify] [--todo-threshold x] [--annotate-all] [--progress json]</c>:
/// the user's way in, as Lily#'s import calls it (docs/repro/lilysharp-omr-distribution-2026-10-05.md).
/// No score file: the parts are guessed from the pages (the most staves a system's opening line
/// joins). The images go to a work folder as the p01.png ... the reading takes, the reading runs as
/// ever, and its files are moved to the output folder under the input's name: base.lys,
/// base.omr.json, base.omr-pNN.png. stdout carries JSON lines only; the reading's own lines go to stderr.
/// Exit codes: 0 read, 1 some page not read (the .lys is there), 2 the input, 3 the reader itself.
/// </summary>
sealed class ReadCommand
{
    public const string Version = "0.4.0";
    /// <summary>The contract's version (arguments, stdout, .omr.json): raised when it changes incompatibly.</summary>
    public const int Protocol = 1;
    static readonly string[] Inputs = { "png", "jpg", "jpeg", "tif", "tiff", "bmp", "pdf" };
    /// <summary>The commands this program answers (Lily# calls verify only when it is listed).</summary>
    static readonly string[] Commands = { "read", "verify" };
    /// <summary>What the commands can do beyond the contract's first form (Lily# looks before asking): verify-bars = omr verify --candidate, --bars, --no-write and its "bar" events (the proposal of 2026-10-23); stages = omr read's "stage" events (the request of 2026-10-26).</summary>
    static readonly string[] Features = { "verify-bars", "stages" };

    public string Name;
    public int PageCount;
    /// <summary>Per page (from 1): the input file it came from.</summary>
    public readonly Dictionary<int, string> Sources = new();
    /// <summary>Per page of a file of several (a PDF, a TIFF): its page there (from 1). An editor shows these pages as the PNG the reading took.</summary>
    readonly Dictionary<int, int> sourcePage = new();
    string work, dest;
    bool progress;
    readonly List<int> failed = new();
    static TextWriter stdout;
    static ReadCommand current;     // (its work folder goes when an input fails)

    public static void PrintVersion()
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        Console.WriteLine(Json(new { version = Version, protocol = Protocol, commands = Commands, inputs = Inputs, features = Features }));
    }

    /// <summary>The options of the reading proper for the read command's arguments; on a bad input, an error line and exit code 2.</summary>
    public static (ReadCommand, Dictionary<string, string>) Prepare(string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        stdout = Console.Out;
        Console.SetOut(Console.Error);
        var cmd = new ReadCommand();
        var files = new List<string>();
        string lysc = null, threshold = null;
        bool verify = false, partsOnly = false, annotateAll = false;
        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];
            string Value() => i + 1 < args.Length ? args[++i] : throw Fail($"{a} wants a value");
            switch (a)
            {
                case "--out": cmd.dest = Value(); break;
                case "--name": cmd.Name = Value(); break;
                case "--lysc": lysc = Value(); break;
                case "--verify": verify = true; break;
                case "--progress": cmd.progress = Value() == "json"; break;
                // (a todo this sure is kept in the .omr.json only, not written into the .lys: 0.8; 1 writes every one)
                case "--todo-threshold":
                    threshold = Value();
                    if (!double.TryParse(threshold, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double th) || th < 0 || th > 1)
                        throw Fail($"--todo-threshold wants a number from 0 to 1, not {threshold}");
                    break;
                case "--annotate-all": annotateAll = true; break;     // (the pages' boxes for the quiet todos too)
                case "--parts-only": partsOnly = true; break;     // (the guess of the parts alone, on stdout: to test it)
                case "--form": Form.Verbose = true; break;        // (the form's signs as they are put to their bars, on stdout: to test it)
                case "--texts": Texts.Verbose = true; break;      // (the rows of letters read for words of expression, on stdout: to test it)
                default:
                    if (a.StartsWith("--")) throw Fail($"unknown option {a}");
                    files.Add(a);
                    break;
            }
        }
        if (files.Count == 0) throw Fail("no image to read");
        if (cmd.dest == null) throw Fail("no --out folder");
        foreach (var f in files)
        {
            if (!File.Exists(f)) throw Fail($"no file {f}");
            string ext = Path.GetExtension(f).TrimStart('.').ToLowerInvariant();
            if (!Inputs.Contains(ext)) throw Fail($"{f}: not an image or PDF this reads ({string.Join(", ", Inputs)})");
        }
        cmd.Name ??= Path.GetFileNameWithoutExtension(files[0]);
        cmd.work = Path.Combine(Path.GetTempPath(), "omr-" + Guid.NewGuid().ToString("N"));
        current = cmd;
        string pages = Path.Combine(cmd.work, "pages");
        Directory.CreateDirectory(pages);

        // the pages as p01.png ...: a file as it is (the reading opens any image by its content),
        // a TIFF of several pages frame by frame, a PDF's pages drawn at 300 dpi
        foreach (var f in files)
        {
            if (Path.GetExtension(f).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
            {
                int first = cmd.PageCount, count;
                try { count = Pdf.Render(f, k => Path.Combine(pages, $"p{first + k:D2}.png")); }
                // (Windows' errors may come without a message: their code then)
                catch (Exception e) when (e is not OutOfMemoryException)
                { throw Fail($"{f}: not a PDF this reads ({(string.IsNullOrWhiteSpace(e.Message) ? $"0x{e.HResult:X8}" : e.Message.Trim())})"); }
                if (count == 0) throw Fail($"{f}: a PDF of no pages");
                for (int k = 1; k <= count; k++) { cmd.Sources[++cmd.PageCount] = Path.GetFullPath(f); cmd.sourcePage[cmd.PageCount] = k; }
                continue;
            }
            int frames;
            try { using var img = Image.FromFile(f); frames = img.GetFrameCount(FrameDimension.Page); }
            catch (Exception e) when (e is OutOfMemoryException or ArgumentException or ExternalException) { throw Fail($"{f}: not an image ({e.Message})"); }
            // (a TIFF of one page too goes through as PNG: an editor shows that, not a TIFF)
            if (frames <= 1 && !Path.GetExtension(f).StartsWith(".tif", StringComparison.OrdinalIgnoreCase))
            {
                cmd.Sources[++cmd.PageCount] = Path.GetFullPath(f);
                File.Copy(f, Path.Combine(pages, $"p{cmd.PageCount:D2}.png"));
                continue;
            }
            using var multi = Image.FromFile(f);
            for (int k = 0; k < frames; k++)
            {
                multi.SelectActiveFrame(FrameDimension.Page, k);
                cmd.Sources[++cmd.PageCount] = Path.GetFullPath(f);
                cmd.sourcePage[cmd.PageCount] = k + 1;
                using var one = new Bitmap(multi);
                one.Save(Path.Combine(pages, $"p{cmd.PageCount:D2}.png"), ImageFormat.Png);
            }
        }
        cmd.Emit(new { @event = "start", pages = cmd.PageCount });

        // the parts: the most staves one opening line joins on any page (a system may hide a
        // part's staff where it rests, never show more than the score has), by kind: a TAB or a
        // one-line staff among them (a lead sheet's slash staff under the voice, a band score's
        // TABs and percussion lines) is found and set aside, as the score file's "tab" and "line" say (B2)
        var kinds = new List<string>();
        var joins = new List<string>();     // (between each two: what joins them, a brace or a bracket)
        Layout.TraceJoins = partsOnly;
        // (the learnt detector beside the exe reads the pages for the guess too, a second or so a page: a
        // scan's staves whose lines the toner broke, and the systems whose opening line it broke, S20)
        string onnx = Path.Combine(AppContext.BaseDirectory, "detector.onnx");
        if (File.Exists(onnx)) Detector.Load(onnx);
        for (int p = 1; p <= cmd.PageCount; p++)
        {
            cmd.Stage("layout", p);
            var (found, foundJoins) = GuessLayout(Path.Combine(pages, $"p{p:D2}.png"));
            if (found.Count > kinds.Count || found.Count == kinds.Count && found.Count(k => k == "") > kinds.Count(k => k == "")) { kinds = found; joins = foundJoins; }
        }
        int parts = kinds.Count(k => k == "");
        if (parts == 0) throw Fail("no staff found on any page");
        string layout = string.Join(",", kinds.Select(k => k == "" ? "5" : k == "tab" ? "T" : "L"));
        Console.WriteLine($"read: {cmd.PageCount} pages, {parts} staves to a system" + (kinds.Count > parts ? $" ({layout})" : ""));
        if (partsOnly)
        {
            // (and what joins each two staves beyond the opening line, when anything does: "joins=,brace,,bracket")
            string joined = joins.Any(j => j != "") ? $" joins={string.Join(",", joins)}" : "";
            stdout.WriteLine((kinds.Count > parts ? $"{parts} {layout}" : parts.ToString()) + joined);
            cmd.Cleanup();
            Environment.Exit(0);
        }
        var score = new StringBuilder($"title {cmd.Name}\n");
        int part = 0, lines = 0;
        var ids = new List<string>();       // (per staff: its part's id, or null for one set aside)
        // (a one-line staff is a part too: a lead sheet's rhythm staff, read as slashes -- S17)
        foreach (var kind in kinds)
        {
            string id = kind == "" ? $"p{++part}" : kind == "line" ? $"gtr{(++lines > 1 ? lines.ToString() : "")}" : null;
            ids.Add(id);
            score.Append(kind == "" ? $"part {id} treble \"Part {part}\"\n" : kind == "line" ? $"line {id} \"Guitar{(lines > 1 ? " " + lines : "")}\"\n" : kind + "\n");
        }
        // the parts a brace joins are a grand staff's (a piano's), a bracket's a group's: the .lys's
        // score block nests them (Layout.Joiner, "both" a brace within a bracket)
        foreach (var (name, join) in new[] { ("grandStaff", "brace"), ("staffGroup", "bracket") })
            for (int i = 0; i < ids.Count; i++)
            {
                if (ids[i] == null) continue;
                int j = i;
                while (j + 1 < ids.Count && j < joins.Count && (joins[j] == join || joins[j] == "both") && ids[j + 1] != null) j++;
                if (j == i) continue;
                score.Append($"{name} {string.Join(" ", ids.GetRange(i, j - i + 1))}\n");
                i = j;
            }
        string config = Path.Combine(cmd.work, "omr.score");
        File.WriteAllText(config, score.ToString());

        var opt = new Dictionary<string, string>
        {
            ["config"] = config, ["pages"] = pages, ["from"] = "1", ["to"] = cmd.PageCount.ToString(),
            ["out"] = Path.Combine(cmd.work, "out"),
        };
        if (Detector.Enabled) opt["detector"] = "true";
        if (threshold != null) opt["todo-threshold"] = threshold;
        if (annotateAll) opt["annotate-all"] = "true";
        if (verify && FindLysc(lysc) is string lyscPath) opt["verify"] = lyscPath;
        else if (verify)
        {
            Console.WriteLine("read: no lysc found, no redraw check");
            cmd.Emit(new { @event = "warning", message = "no lysc found (--lysc, beside the OMR, or on the PATH): the reading was not drawn again and checked" });
        }
        return (cmd, opt);
    }

    /// <summary>The staff spacing of the pages guessed so far: a page whose staves all went unfound is read by the detector at it.</summary>
    static double lastSpace;

    /// <summary>The staves of the page's largest system by kind, as its opening line joins them, and what joins each two beyond the line (Layout.SystemKinds); empty with no staff.</summary>
    static (List<string> Kinds, List<string> Joins) GuessLayout(string path)
    {
        var d = Img.Load(path);
        var extras = new List<Staff>();
        var staves = Layout.FindStaves(d, extras);
        double guessedSpace = Layout.LastSpacing;
        if (Layout.LastGuessed && d.Raw != null)
        {
            var rawExtras = new List<Staff>();
            var found = Layout.FindStaves(d.Raw, rawExtras);
            // (the reading that finds more staves of any kind stands, as in Reader.Read: a TAB is found only on the one page or the other)
            if (found.Count + rawExtras.Count > staves.Count + extras.Count) { staves = found; extras = rawExtras; d.Raw.Gray = d.Gray; d = d.Raw; guessedSpace = Layout.LastSpacing; }
        }
        // the learnt detector, as the reading proper has it (Reader.Read): the staves the toner took the
        // lines of, recovered about its barlines; the staves' starts before the clefs it sees; and its
        // barlines tell two staves of one system apart from two systems where their opening line broke
        // (a page with no staff found is read at the spacing of the pages before, or the rows' guess)
        List<Detection> det = null;
        // (a page the toner wrecked has the comb's staves with a few spaces' width or none -- the lines'
        // ink that is left, a start anywhere, an end moved in before the start: those tell nothing of the
        // page's spacing, and a staff with no extent is nothing)
        staves.RemoveAll(st => st.Right <= st.Left);
        extras.RemoveAll(st => st.Right <= st.Left);
        double Median(IEnumerable<double> vs) { var l = vs.OrderBy(v => v).ToList(); return l.Count == 0 ? 0 : l[l.Count / 2]; }
        // (with no staff a quarter of the page wide, the staves found are scraps and their spacings
        // noise -- MuseJazz band005's second page, 8 to 10 pixels for 23: the spacing is where the
        // rows' ink repeats, as for a page with no clear staff, or the pages' before)
        var wide = staves.Where(st => st.Right - st.Left >= 0.25 * d.W).ToList();
        double s = wide.Count > 0 ? Median(wide.Select(t => t.S)) : Layout.GuessSpacing(d) is double g && g > 0 ? g : lastSpace > 0 ? lastSpace : guessedSpace;
        // (a page with next to no ink -- a blank page, a page of a few words -- has no staff the toner hid:
        // the detector is not swept over it. Its paper's grain passes for a small page's spacing and it is
        // enlarged, and seven goes over it took two minutes: Schirmer's Clementi, its empty second page.
        // Staff lines alone are 1.5 % of a page's pixels and more)
        double ink = wide.Count > 0 ? 1 : Ink(d);
        if (Layout.TraceJoins) Console.WriteLine($"  guess: {staves.Count} staves found ({wide.Count} a quarter of the page wide), {extras.Count} TABs and lines; spacing {s:F1} (the page {d.W}x{d.H}, enlarged {d.Zoom:F2}{(wide.Count > 0 ? "" : $", ink {100 * ink:F2} %")})");
        if (wide.Count == 0 && ink < 0.002) return (new List<string>(), new List<string>());
        if (Detector.Enabled && d.Gray != null && (s > 0 || wide.Count == 0))
        {
            if (wide.Count == 0)
            {
                // (the rows' agreement is noise too where the toner left little of the lines -- 9 to 49
                // pixels for 20 to 25: the detector reads the page at each likely spacing, a second a go,
                // and the one it sees the most symbols at is the page's. Detections at the wrong scale
                // are few: a staff read at twice its size has heads too large for the model)
                int Seen(List<Detection> ds) => ds.Count(t => t.Score > Detector.Threshold);
                foreach (double sp in new[] { 14.0, 17, 20, 24, 28, 33, 40 })
                {
                    var ds = Detector.Detect(d.Gray, d.W, d.H, sp);
                    if (Layout.TraceJoins) Console.WriteLine($"  guess: at spacing {sp:F0} the detector sees {Seen(ds)} symbols ({ds.Count(t => t.Class == "barline")} barlines; {ds.Count} at all)");
                    if (det == null || Seen(ds) > Seen(det)) { det = ds; s = sp; }
                }
            }
            else det = Detector.Detect(d.Gray, d.W, d.H, s);
            if (Layout.TraceJoins) Console.WriteLine($"  guess: at spacing {s:F1} the detector sees {det.Count(t => t.Class == "barline")} barlines, {det.Count(t => t.Class.StartsWith("clef_"))} clefs, {det.Count} symbols in all");
            // a staff found a few spaces wide (under a quarter of the page) is set aside for the detector:
            // one recovered about its barlines, at the staff's whole extent, takes its place; one not
            // recovered stays as found. A TAB as narrow runs the width of the staff above it, or is nothing
            var narrow = staves.Where(st => !wide.Contains(st)).ToList();
            staves.RemoveAll(narrow.Contains);
            foreach (var tab in extras.Where(t => t.Kind == "tab" && t.Right - t.Left < 0.25 * d.W).ToList())
            {
                // (MuseScore sets a TAB up to seven spaces under its staff: Gonville band001, 6.6)
                var over = wide.Where(st => st.Bottom < tab.Top && tab.Top - st.Bottom < 10 * s).OrderByDescending(st => st.Top).FirstOrDefault();
                // (and its lines stand further apart than the staves': rows of speckle a pixel apart are no TAB)
                if (over == null || tab.S < 1.1 * s) extras.Remove(tab); else { tab.Left = over.Left; tab.Right = over.Right; }
            }
            Layout.RecoverStaves(d, staves, s, det, extras);
            foreach (var st in narrow) if (!staves.Any(r => Math.Abs(r.Mid - st.Mid) < 2.5 * s)) staves.Add(st);
            staves.Sort((a, b) => a.Top.CompareTo(b.Top));
            // (and the percussion lines the toner broke, at the barlines left where no staff was found)
            Layout.RecoverLines(d, staves, extras, s, det);
            if (Layout.TraceJoins)
            {
                // (what the detector sees clear of every staff found, by height: what a lost staff leaves)
                bool Near(Staff st, double y) => st.Kind == "" ? Math.Abs(y - st.Mid) < 2.5 * s : y > st.Top - 2.5 * s && y < st.Bottom + 2.5 * s;
                var loose = det.Where(t => t.Score > Detector.Threshold && !staves.Any(st => Near(st, t.Y)) && !extras.Any(st => Near(st, t.Y))).OrderBy(t => t.Y).ToList();
                for (int i = 0; i < loose.Count;)
                {
                    int j = i;
                    while (j + 1 < loose.Count && loose[j + 1].Y - loose[i].Y < 2 * s) j++;
                    var grp = loose.GetRange(i, j - i + 1);
                    Console.WriteLine($"  guess: clear of the staves at y {grp.Average(t => t.Y):F0}: {string.Join(" ", grp.GroupBy(t => t.Class).OrderByDescending(c => c.Count()).Select(c => $"{c.Key} {c.Count()}"))}");
                    i = j + 1;
                }
            }
            Layout.StartAtClefs(staves, s, det);
        }
        var joins = new List<string>();
        if (staves.Count == 0) return (new List<string>(), joins);
        s = wide.Count > 0 ? Median(wide.Select(t => t.S)) : Median(staves.Select(t => t.S));
        lastSpace = s;
        var kinds = Layout.SystemKinds(d, staves, extras, s, joins, det);
        return (kinds, joins);
    }

    /// <summary>The share of the page's pixels that are ink (every other row and column).</summary>
    static double Ink(Img d)
    {
        long n = 0, all = 0;
        for (int y = 0; y < d.H; y += 2)
            for (int x = 0; x < d.W; x += 2) { all++; if (d[x, y]) n++; }
        return all == 0 ? 0 : (double)n / all;
    }

    /// <summary>lysc for the redraw check: as given, beside this program, or on the PATH.</summary>
    public static string FindLysc(string given)
    {
        if (given != null) return File.Exists(given) ? Path.GetFullPath(given) : throw Fail($"no lysc at {given}");
        string exe = OperatingSystem.IsWindows() ? "lysc.exe" : "lysc";
        string beside = Path.Combine(AppContext.BaseDirectory, exe);
        if (File.Exists(beside)) return beside;
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            if (File.Exists(Path.Combine(dir, exe))) return Path.Combine(dir, exe);
        return null;
    }

    /// <summary>
    /// A stage of the reading begun, for the progress line (Lily#'s request of 2026-10-26: the guess of
    /// the layout was eleven minutes with no word): "layout" at each page's guess (page, of), "read"
    /// before the first page is read, "verify" before the redraw check, "write" before the files go out.
    /// </summary>
    public void Stage(string stage, int page = 0)
    {
        if (page > 0) Emit(new { @event = "stage", stage, page, of = PageCount });
        else Emit(new { @event = "stage", stage });
    }

    /// <summary>A page read: its progress line; a page with no system read is one the .lys lacks.</summary>
    public void Page(int page, PageResult res)
    {
        Emit(new { @event = "page", page, of = PageCount });
        if (res.Systems.Count > 0) return;
        failed.Add(page);
        Emit(new { @event = "warning", page, message = $"page {page}: no system found" });
    }

    /// <summary>The reading's files moved to the output folder under the name; the exit code.</summary>
    public int Finish(string outDir, int todos, int quiet)
    {
        Stage("write");
        Directory.CreateDirectory(dest);
        // (never over a reading already there: its .lys may have been corrected since. x.lys, x-2.lys, ...)
        string name = Name;
        for (int k = 2; File.Exists(Path.Combine(dest, name + ".lys")) || File.Exists(Path.Combine(dest, name + ".omr.json")); k++) name = $"{Name}-{k}";
        string Move(string from, string name)
        {
            string to = Path.GetFullPath(Path.Combine(dest, name));
            // (copied under a passing name, then renamed: no half-written file under the right name)
            File.Copy(from, to + ".part", true);
            File.Move(to + ".part", to, true);
            return to;
        }
        string lys = Move(Path.Combine(outDir, "omr.lys"), name + ".lys");
        string jsonFrom = Path.Combine(outDir, Name + ".omr.json");
        {
            // (the sidecar names its .lys; a page of a PDF or TIFF is shown as the PNG the reading
            // took, base.pNN.png, its file and page there beside it; the files it names are relative
            // to its own folder when they are in it or under it -- the sidecar was written in the
            // work folder, from where nothing is relative)
            var node = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(jsonFrom));
            node["lys"] = name + ".lys";
            foreach (var p in node["pages"].AsArray())
            {
                int page = (int)p["page"];
                if (sourcePage.TryGetValue(page, out int k))
                {
                    p["image"] = Move(Path.Combine(work, "pages", $"p{page:D2}.png"), $"{name}.p{page:D2}.png");
                    p["source"] = Sidecar.Relative(Sources[page], dest);
                    p["sourcePage"] = k;
                }
                p["image"] = Sidecar.Relative((string)p["image"], dest);
            }
            File.WriteAllText(jsonFrom, node.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
        }
        string json = Move(jsonFrom, name + ".omr.json");
        var annotated = Directory.GetFiles(outDir, "verify-p*.png").Order()
            .Select(f => Move(f, name + ".omr-" + Path.GetFileName(f)["verify-".Length..])).ToList();
        // (todos: the marks in the .lys; quiet: those kept in the .omr.json only)
        Emit(new { @event = "done", lys, json, annotated, todos, quiet, pagesNotRead = failed });
        Cleanup();
        return failed.Count > 0 ? 1 : 0;
    }

    /// <summary>The reader itself failed: an error line, exit code 3.</summary>
    public void Crash(Exception e)
    {
        Console.Error.WriteLine(e);
        stdout.WriteLine(Json(new { @event = "error", code = "internal", message = e.Message }));
        stdout.Flush();
        Cleanup();
    }

    void Cleanup()
    {
        try { if (work != null && Directory.Exists(work)) Directory.Delete(work, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    void Emit(object o) { if (progress) { stdout.WriteLine(Json(o)); stdout.Flush(); } }

    static string Json(object o) => JsonSerializer.Serialize(o, new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });

    /// <summary>A bad input: the error line (whether or not progress was asked for: it is the answer), exit code 2.</summary>
    static Exception Fail(string message)
    {
        stdout.WriteLine(Json(new { @event = "error", code = "input", message }));
        stdout.Flush();
        Console.Error.WriteLine("omr read: " + message);
        current?.Cleanup();
        Environment.Exit(2);
        return null;
    }
}
