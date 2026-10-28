using System.Diagnostics;
using System.Globalization;
using System.Text;
using Omr;

// omr --config <score> --pages <dir with pNN.png> --from N --to M --out <dir> [--truth <notes.md|.notes|.xml>] [--overlay]
// (the user's way in: omr read <image>... --out <dir>, and omr --version --json; ReadCommand.
// omr verify <x.lys>: a corrected reading checked against the pages again; VerifyCommand)
if (args.Length > 0 && args[0] == "--version") { ReadCommand.PrintVersion(); return; }
if (args.Length > 0 && args[0] == "verify") { Environment.Exit(VerifyCommand.Run(args[1..])); }
ReadCommand readCmd = null;
var opt = new Dictionary<string, string>();
if (args.Length > 0 && args[0] == "read")
{
    (readCmd, opt) = ReadCommand.Prepare(args[1..]);
    AppDomain.CurrentDomain.UnhandledException += (_, e) => { readCmd.Crash((Exception)e.ExceptionObject); Environment.Exit(3); };
}
else
    for (int i = 0; i < args.Length; i++)
        if (args[i].StartsWith("--")) opt[args[i][2..]] = i + 1 < args.Length && !args[i + 1].StartsWith("--") ? args[++i] : "true";

// the loose-symbol classifier, learnt from generated scores (--noknn: the hand-written rules only)
string knnPath = opt.GetValueOrDefault("knn", Path.Combine(AppContext.BaseDirectory, "glyph-train.tsv"));
if (!opt.ContainsKey("noknn") && File.Exists(knnPath)) GlyphKnn.Load(knnPath);
// --detector [<model.onnx>]: the learnt symbol detector (S10) finds the heads; the rules then
// check them (stems, ledger lines) as they check their own. Without a path, the model beside the exe.
// (--threads N: the detector's threads; the batch scripts run a reader per core and give each 1)
// (omr read loaded the one beside the exe already, for its guess of the staves)
if (opt.TryGetValue("detector", out var detPath) && !(detPath == "true" && Detector.Enabled))
    Detector.Load(detPath == "true" ? Path.Combine(AppContext.BaseDirectory, "detector.onnx") : detPath,
        int.Parse(opt.GetValueOrDefault("threads", "0")));
// --sure <score>: a detected head this sure stays whatever the rules say (2 = the rules decide every head)
if (opt.TryGetValue("sure", out var sure)) Symbols.SureScore = double.Parse(sure, CultureInfo.InvariantCulture);
// --trust <score>: a detection this sure fills in where the rules read nothing (see Symbols.TrustScore)
if (opt.TryGetValue("trust", out var trust)) Symbols.TrustScore = double.Parse(trust, CultureInfo.InvariantCulture);
// --knn-test <samples.tsv>: how well the classifier labels samples it was not trained on
if (opt.TryGetValue("knn-test", out var testPath))
{
    var confusion = new SortedDictionary<string, SortedDictionary<string, int>>();
    foreach (var line in File.ReadLines(testPath))
    {
        var p = line.Split('\t');
        var f = p.Skip(1).Select(v => double.Parse(v, CultureInfo.InvariantCulture)).ToArray();
        int colon = p[0].IndexOf(':');
        var (got, _) = GlyphKnn.Classify(f, family: colon < 0 ? "" : p[0][..(colon + 1)]);
        if (!confusion.TryGetValue(p[0], out var row)) confusion[p[0]] = row = new();
        row[got] = row.GetValueOrDefault(got) + 1;
    }
    foreach (var (truthLabel, row) in confusion)
    {
        int n = row.Values.Sum(), ok = row.GetValueOrDefault(truthLabel);
        Console.WriteLine($"{truthLabel,-26} {ok,5}/{n,-5} {string.Join(", ", row.Where(r => r.Key != truthLabel).OrderByDescending(r => r.Value).Select(r => $"{r.Key} {r.Value}"))}");
    }
    return;
}

// --ocr-file <png> [--scale k]: every line Windows' OCR reads in that image (Header.Recognize), to try a cut of a page
if (opt.TryGetValue("ocr-file", out var ocrFile))
{
    using var bmp = new System.Drawing.Bitmap(ocrFile);
    double k = double.Parse(opt.GetValueOrDefault("scale", "0"), CultureInfo.InvariantCulture);
    Header.Trace = true;
    foreach (var (text, box) in Header.Recognize(bmp, new System.Drawing.Rectangle(0, 0, bmp.Width, bmp.Height), k))
        Console.WriteLine($"  ocr: \"{text}\" at {box.X:F0},{box.Y:F0} {box.Width:F0}x{box.Height:F0}");
    return;
}

var cfg = ScoreConfig.Load(opt["config"]);
int from = int.Parse(opt.GetValueOrDefault("from", "1")), to = int.Parse(opt.GetValueOrDefault("to", from.ToString()));
string outDir = opt.GetValueOrDefault("out", "out");
Directory.CreateDirectory(outDir);
// --pickup a/b: the piece starts with a pickup this long (the .notes truth's bars are counted from it)
if (opt.TryGetValue("pickup", out var pickup)) { var pp = pickup.Split('/'); NotesTruth.Pickup = double.Parse(pp[0]) / double.Parse(pp[1]); }
// (the .notes truth's bars follow the meter's changes the config lists)
foreach (var (bar, time) in cfg.Times.Where(kv => kv.Key > 1))
    NotesTruth.Meters[bar] = double.Parse(time.Split('/')[0]) / double.Parse(time.Split('/')[1]);
// the truth: a hand transcription (notes.md format) or LilyPond's event-listener output (.notes)
Truth truth = null;
if (opt.TryGetValue("truth", out var truthPath))
{
    if (truthPath.EndsWith(".notes"))
    {
        // one file per staff when the path holds {part}
        var tp = cfg.TimeAt(1).Split('/');
        double barWholes = double.Parse(tp[0]) / double.Parse(tp[1]);
        truth = new Truth();
        foreach (var p in truthPath.Contains("{part}") ? cfg.Parts.Select(q => q.Id) : new[] { cfg.Parts[0].Id })
        {
            // a "*" gathers every voice file of the staff
            string one = truthPath.Replace("{part}", p);
            string dir = Path.GetDirectoryName(one);
            var files = one.Contains('*') ? Directory.GetFiles(string.IsNullOrEmpty(dir) ? "." : dir, Path.GetFileName(one)) : new[] { one };
            foreach (var f in files) NotesTruth.Load(f, p, barWholes, opt.ContainsKey("poly"), truth);
        }
    }
    // MusicXML: the notes and the voices of every staff (B4a). "a.xml+b.xml": pieces printed on
    // through, read as one (their measures numbered on)
    else if (truthPath.EndsWith(".xml") || truthPath.EndsWith(".musicxml"))
    {
        truth = new Truth();
        foreach (var one in truthPath.Split('+'))
            MusicXmlTruth.Load(one, cfg.Parts.Select(q => q.Id).ToList(), opt.ContainsKey("poly"), truth, truth.Bars.Keys.Select(k => k.Bar).DefaultIfEmpty(0).Max());
    }
    else truth = Truth.Load(truthPath);
}
// the truth's voices: one .notes file per staff and voice ({part} = staff, * = voice)
if (opt.TryGetValue("voices", out var voicePattern))
{
    var tp = cfg.TimeAt(1).Split('/');
    truth ??= new Truth();
    NotesTruth.LoadVoices(voicePattern, cfg.Parts.Select(q => q.Id), double.Parse(tp[0]) / double.Parse(tp[1]), truth);
}
// --pred <a.xml|a.mxl>[,<b.xml>...]: another reader's MusicXML (Audiveris), one file per page from
// --from ("-" for a page it read nothing of), scored against the truth as this reader's reading is
// (the reader is not run). A page's measures follow the page before's (each file counts from 1),
// or start at the config's first bar for the page.
if (opt.TryGetValue("pred", out var predPaths) && truth != null)
{
    var ids = cfg.Parts.Select(q => q.Id).ToList();
    bool poly = opt.ContainsKey("poly");
    var read = new Dictionary<(int Bar, string Part), List<Ev>>();
    var seq = new List<Dictionary<string, List<Ev>>>();    // every measure read, in order (--align)
    int barAt = 1, pg = from;
    // (a page it split into movements: their files joined with "+", in order)
    foreach (var page in predPaths.Split(','))
    {
        if (cfg.PageFirstBar.TryGetValue(pg, out var pfb)) barAt = pfb;
        foreach (var f in page.Split('+'))
        {
            var one = f == "-" ? new Truth() : MusicXmlTruth.Load(XmlOf(f), ids, poly);
            foreach (var ((b, id), evs) in one.Bars) read[(barAt + b - 1, id)] = evs;
            int count = one.Bars.Keys.Select(k => k.Bar).DefaultIfEmpty(0).Max();
            for (int b = 1; b <= count; b++) seq.Add(ids.ToDictionary(id => id, id => one.Bars.GetValueOrDefault((b, id))));
            barAt += count;
        }
        pg++;
    }
    int first0 = cfg.PageFirstBar.TryGetValue(from, out var s0) ? s0 : 1;
    int last0 = cfg.PageFirstBar.TryGetValue(to + 1, out var n0) ? n0 - 1 : truth.Bars.Keys.Select(k => k.Bar).DefaultIfEmpty(0).Max();
    var psc = new Scorer();
    if (opt.ContainsKey("align"))
    {
        var tbars = truth.Bars.Keys.Select(k => k.Bar).Where(b => b >= first0 && b <= last0).Distinct().Order().ToList();
        var map = BarAlign.Align(truth, tbars, ids, seq.Select(d => (Func<string, List<Ev>>)(p => d[p])).ToList());
        read.Clear();
        for (int j = 0; j < seq.Count; j++)
            foreach (var id in ids)
                if (map[j] > 0) read[(map[j], id)] = seq[j][id];
                else if (truth.Bars.Keys.Any(k => k.Part == id)) psc.AddExtra(j + 1, id, seq[j][id] ?? new List<Ev>());
        Console.WriteLine($"align: {map.Count(x => x > 0)} of {seq.Count} measures read stand for one of the truth's {tbars.Count} bars");
    }
    foreach (var q in cfg.Parts.Where(q => truth.Bars.Keys.Any(k => k.Part == q.Id)))
        for (int b = first0; b <= last0; b++)
        {
            var tev = truth.Bars.GetValueOrDefault((b, q.Id)) ?? new List<Ev>();
            var pev = read.GetValueOrDefault((b, q.Id)) ?? new List<Ev>();
            if (poly) psc.AddMultiset(b, q.Id, tev, pev);
            else if (truth.Bars.ContainsKey((b, q.Id))) psc.Add(b, q.Id, tev, pev);
        }
    Console.WriteLine($"pred: {read.Keys.Select(k => k.Bar).Distinct().Count()} measures read, bars {first0}-{last0} scored");
    Console.WriteLine(poly ? psc.MultisetSummary() : psc.Summary());
    Directory.CreateDirectory(opt.GetValueOrDefault("out", "out"));
    File.WriteAllText(Path.Combine(opt.GetValueOrDefault("out", "out"), "diffs.txt"), string.Join("\n", psc.Diffs));
    return;
}
// (a compressed MusicXML is a zip: its score is the root file the container names)
static string XmlOf(string path)
{
    if (!path.EndsWith(".mxl", StringComparison.OrdinalIgnoreCase)) return path;
    using var zip = System.IO.Compression.ZipFile.OpenRead(path);
    var container = zip.GetEntry("META-INF/container.xml");
    string root = container == null ? null
        : (string)System.Xml.Linq.XDocument.Load(container.Open()).Descendants().FirstOrDefault(e => e.Name.LocalName == "rootfile")?.Attribute("full-path");
    var entry = (root == null ? null : zip.GetEntry(root)) ?? zip.Entries.First(e => !e.FullName.StartsWith("META-INF") && e.FullName.EndsWith(".xml"));
    string tmp = Path.Combine(Path.GetTempPath(), $"pred-{Guid.NewGuid():N}.xml");
    using (var src = entry.Open())
    using (var dst = File.Create(tmp))
        src.CopyTo(dst);
    return tmp;
}
// --det-threshold x: the detector's threshold (0.5; lower to see what it is unsure of with --detections)
if (opt.TryGetValue("det-threshold", out var dth)) Detector.Threshold = double.Parse(dth, CultureInfo.InvariantCulture);
if (opt.TryGetValue("at", out var at)){ var xy = at.Split(','); Symbols.DebugAt = (double.Parse(xy[0]), double.Parse(xy[1])); }

var all = new SortedDictionary<int, List<Ev>[]>();
var allVoices = new Dictionary<(int Bar, int Part), List<List<Ev>>>();
var systemStarts = new Dictionary<int, int>();   // each system's first bar, to its page (the .lys breaks there)
var sidecar = new Sidecar();
var csv = new StringBuilder("page,system,part,class,x,y,w_s,h_s,fill,pos\n");
var sw = Stopwatch.StartNew();
int next = 1;
var symEval = new SymEval();
// --verify [<lysc.exe>]: the .lys written is engraved again and compared, bar by bar, with what
// the page shows (S12; Verify). --verify-truth: compared with the truth's SVG instead (checks the check).
// Without a path, lysc beside this program or on the PATH; with none there, no check (and it says so)
Verify verify = null;
if (opt.TryGetValue("verify", out var lyscPath))
{
    Verify.LyscPath = lyscPath != "true" ? lyscPath : ReadCommand.FindLysc(null);
    if (Verify.LyscPath != null) verify = new Verify { Full = opt.ContainsKey("verify-all") };
    else Console.WriteLine("verify: no lysc found (--verify <lysc>, beside the OMR, or on the PATH): no redraw check");
}
// (the page's side is gathered whether or not the check runs now: x.omr.json keeps it for omr verify)
var pageSide = verify ?? new Verify();
// clefs, keys and times are read from the page; --given takes them from the config instead
var state = new ReaderState(cfg.Parts.Count) { Auto = !opt.ContainsKey("given"), Poly = opt.ContainsKey("poly") };
Signatures.Verbose = opt.ContainsKey("sigs");
Digits.Verbose = opt.ContainsKey("barno");
Symbols.TupletVerbose = opt.ContainsKey("tuplets");
Tab.Verbose = opt.ContainsKey("tab");
MultiRests.Verbose = opt.ContainsKey("mmrests");
// --denoise 0|1|2 (none, lone pixels, median) and --fixed-threshold: how the page is binarized
if (opt.TryGetValue("denoise", out var dn)) Img.Denoise = int.Parse(dn);
if (opt.ContainsKey("fixed-threshold")) Img.OtsuThreshold = false;
if (opt.TryGetValue("save-bin", out var sb)) Img.SaveBinarized = sb;
if (opt.TryGetValue("save-gray", out var sg)) Img.SaveGray = sg;
if (opt.TryGetValue("save-ink", out var inkPath)) Img.SaveInk = inkPath;
Img.NoZoom = opt.ContainsKey("no-zoom");Reader.NoFill = opt.ContainsKey("no-fill");
Layout.TraceJoins = Img.Trace = opt.ContainsKey("trace");   // --trace: how the page was levelled, the staves, their extents and the systems found
Reader.NoTab = opt.ContainsKey("no-tab");
Reader.NoUnprinted = opt.ContainsKey("no-unprinted");
// --no-tremolos: no two-note tremolos read (to tell what Symbols.Tremolos does)
Symbols.NoTremolos = opt.ContainsKey("no-tremolos");
// --no-beat-repeats: no beat repeats read (to tell what Symbols.BeatRepeats does)
Symbols.NoBeatRepeats = opt.ContainsKey("no-beat-repeats");
// --no-fix: the doubted bars left as read (to tell what Fixes does); --fixes: every candidate weighed
Fixes.Off = opt.ContainsKey("no-fix");
Fixes.Verbose = opt.ContainsKey("fixes");
// --calibrate: the score teaches the classifier its own font. A first reading by the rules (which
// check where each sign stands) gathers samples; the reading proper then knows these shapes.
// --knn-only: no rules for the shapes the classifier is unsure of (how far the classifier alone goes)
if (opt.ContainsKey("calibrate") && GlyphKnn.Enabled)
{
    Symbols.Collect = true;
    var pre = new ReaderState(cfg.Parts.Count) { Auto = state.Auto, Poly = state.Poly };
    int nx = 1;
    for (int page = from; page <= to; page++)
    {
        int fb = cfg.PageFirstBar.TryGetValue(page, out var f0) ? f0 : nx;
        nx = Reader.Read(Path.Combine(opt["pages"], $"p{page:D2}.png"), page, fb, cfg, pre).LastBar + 1;
    }
    Symbols.Collect = false;
    Console.WriteLine($"calibrated: {GlyphKnn.LocalCount} samples from the score itself");
}
Symbols.KnnOnly = opt.ContainsKey("knn-only");
Header header = null;
var textMarks = new List<Tempo.Mark>();
var formSigns = new List<Form.Sign>();   // the repeat barlines, volta brackets, segno and coda signs, by bar (S22)
var expressions = new Expressions();
Expressions.Verbose = Articulations.Verbose = Arpeggios.Verbose = Glissandi.Verbose = Ottavas.Verbose = opt.ContainsKey("marks");
Form.Verbose |= opt.ContainsKey("marks") || opt.ContainsKey("form");    // (|=: omr read's --form set it before)
var carried = new List<Continuation.Open>();   // the lines run out past a page's last system, for the next page's readers (Continuation)
var readings = new Dictionary<(int Bar, int Part), List<Fixes.Reading>>();   // per doubted part-bar: its other readings weighed (Fixes), for its length todo
readCmd?.Stage("read");
for (int page = from; page <= to; page++)
{
    string path = Path.Combine(opt["pages"], $"p{page:D2}.png");
    int first = cfg.PageFirstBar.TryGetValue(page, out var fb) ? fb : next;
    var t0 = sw.Elapsed;
    var res = Reader.Read(path, page, first, cfg, state);
    next = res.LastBar + 1;
    res.Before = carried;
    // the doubted bars put right by their length and the detector's evidence (S33, stage 1): kept in x.omr.json as "fixed";
    // the other readings of the bars left doubted go with their length todos (x.omr.json "readings")
    foreach (var fx in Fixes.Apply(res, state, readings, (x, y) => { var p = res.Dark.ToSource(x, y); return ((int)Math.Round(p.X), (int)Math.Round(p.Y)); }))
        sidecar.AddTodo(fx.Bar, fx.Part, "fixed", $"{fx.Was} read as {fx.Now}: {fx.Why} (evidence {fx.Evidence:F2}); the bar fills its time now", 1);
    // the title, subtitle, composer and poet over the first page's music (Header: Windows' OCR); the
    // read command always, a run of the reader with --header
    bool words = (readCmd != null || opt.ContainsKey("header")) && !opt.ContainsKey("no-header");
    Tempo.Verbose = opt.ContainsKey("words");
    if (page == from && words)
    {
        header = Header.Read(path, res);
        foreach (var l in header?.Lines ?? new()) Console.WriteLine($"  header {(l.Role == "" ? "-" : l.Role)}: \"{l.Text}\" at {l.Box.X:F0},{l.Box.Y:F0} h {l.Box.Height:F0}");
    }
    // and over every system, the tempo markings and rehearsal marks (Tempo)
    // (the header's lines bound the first page's band only: they are that page's)
    var pageMarks = words ? Tempo.Read(path, res, page == from ? header : null) : new List<Tempo.Mark>();
    // the octave lines the detector sees (S18); an 8va the OCR read where the detector saw the sign is the same line
    var octaveLines = Ottavas.Find(res);
    if (octaveLines.Count > 0)
        pageMarks.RemoveAll(m => m.Ottava != 0 && octaveLines.Any(o => Math.Abs(o.Box.Left - m.Box.Left) < 4 * res.S && Math.Abs(o.Box.Top - m.Box.Top) < 3 * res.S));
    pageMarks.AddRange(octaveLines);
    // the form's signs: the repeat barlines, the endings' brackets, the segno and coda signs (S22)
    // (a "D.S." read, here or on a page before, lets an unsure segno stand where none was seen sure)
    static bool Ds(string kind) => kind is "ds" or "dsalcoda" or "dsalfine";
    var pageSigns = Form.Find(res, pageMarks.Any(m => m.Nav != null && Ds(m.Nav)) || formSigns.Any(g => Ds(g.Kind)));
    // (and the endings' numbers by their brackets' hooks, read by the OCR as the words are)
    if (words) Form.ReadNumbers(path, res, pageSigns);
    formSigns.AddRange(pageSigns);
    // (and the jumps' and ends' words the OCR read over the staff, "Fine", "D.C. al Fine", "D.S. al Coda": after the bar they follow)
    foreach (var m in pageMarks.Where(m => m.Nav != null))
        if (!formSigns.Any(g => g.Kind == m.Nav && g.Bar == m.Bar)) formSigns.Add(new Form.Sign(m.Nav, m.Bar, page, m.Box.X, m.Box.Y, 1));
    foreach (var m in pageMarks)
    {
        textMarks.Add(m);
        Console.WriteLine($"  bar {m.Bar}: {(m.Ottava != 0 ? $"octave line {m.Ottava} up to x {m.EndX:F0}" : m.Nav != null ? $"form words {m.Nav}" : m.Rehearsal != null ? $"rehearsal mark \"{m.Rehearsal}\"" : $"tempo \"{m.Words}\" = {m.Bpm}")} at {m.Box.X:F0},{m.Box.Y:F0}");
    }
    foreach (var m in pageMarks.Where(m => m.Ottava != 0)) Ottavas.Apply(res, m);
    // the dynamics, hairpins and pedal the detector sees, on their notes (S14)
    expressions.Apply(res);
    // the articulations it sees, on their chords (S15)
    Articulations.Apply(res, pageMarks);
    // the arpeggios' wavy lines left of the chords, by rule
    Arpeggios.Apply(res);
    // and the glissandi's straight lines from a note to the next, by rule
    if (res.Dark != null && res.Ink != null) Glissandi.Apply(res, Glissandi.Find(res.Dark, res.Ink, res.Sym.Heads, res.S, res.Sym.Rests));
    // and the words of expression the OCR reads over and under the staves, "dolce", "rit.", on their notes
    Texts.Verbose |= opt.ContainsKey("words") || opt.ContainsKey("texts");    // (|=: omr read's --texts set it before)
    Texts.Dump = opt.GetValueOrDefault("texts-dump");
    var tt = sw.Elapsed;
    if (words) Texts.Apply(path, res, pageMarks);
    if (Texts.Verbose) Console.WriteLine($"  texts: {(sw.Elapsed - tt).TotalSeconds:F2} s, {Texts.Cuts} cuts, cleaning {Texts.CleanTicks / (double)System.Diagnostics.Stopwatch.Frequency:F2} s");
    // (the lines the page before ran out past its last system that this page's readers found no
    // continuation of: read as they were without a next page; this page's own go on to the next)
    foreach (var o in res.Before) o.Close(null, null);
    carried = res.Open;
    readCmd?.Page(page, res);
    foreach (var note in res.Notes) Console.WriteLine("  " + note);
    if (opt.ContainsKey("sigs"))
        foreach (var g in res.Sym.Sigs)
            Console.WriteLine($"  sig s{g.St.System + 1} part {g.St.Part} x {g.X:F0}: {g.Clef}{g.Fifths}{g.Time}");
    // --detections [x0,y0,x1,y1]: everything the learnt detector saw (in that box of the page, if given)
    if (opt.TryGetValue("detections", out var detBox) && res.Sym.Detections != null)
    {
        var b = detBox == "true" ? null : detBox.Split(',').Select(v => double.Parse(v, CultureInfo.InvariantCulture)).ToArray();
        foreach (var t in res.Sym.Detections.Where(t => b == null || (t.X >= b[0] && t.Y >= b[1] && t.X <= b[2] && t.Y <= b[3])).OrderBy(t => t.X))
            Console.WriteLine($"  det {t.Class} at {t.X:F0},{t.Y:F0} {t.Score:F2}"
                              + (t.Digits == null ? "" : " (" + string.Join(" ", t.Digits.Select((v, k) => (v, k)).Where(a => a.v >= 0.05).OrderByDescending(a => a.v).Select(a => $"{a.k}:{a.v:F2}")) + ")"));
    }
    // --ocr x0,y0,x1,y1[,scale]: every line Windows' OCR reads in that box of the page as loaded (Header.Recognize, as Tempo reads a band)
    if (opt.TryGetValue("ocr", out var ocrBox))
    {
        var b = ocrBox.Split(',').Select(v => double.Parse(v, CultureInfo.InvariantCulture)).ToArray();
        using var pageBmp = new System.Drawing.Bitmap(path);
        Header.Trace = true;
        foreach (var (text, box) in Header.Recognize(pageBmp, System.Drawing.Rectangle.FromLTRB((int)b[0], (int)b[1], (int)b[2], (int)b[3]), b.Length > 4 ? b[4] : 0))
            Console.WriteLine($"  ocr: \"{text}\" at {box.X:F0},{box.Y:F0} {box.Width:F0}x{box.Height:F0}");
    }
    if (opt.ContainsKey("debug"))
        foreach (var sys in res.Systems)
            foreach (var st in sys.Staves.Take(1).Select(st => { Console.WriteLine($"  s{sys.Index + 1} bars {string.Join(" ", sys.Bars.Select(b => b.ToString("F0")))}"); return st; }).Concat(sys.Staves.Skip(1)))
                Console.WriteLine($"  s{sys.Index + 1} part {st.Part}: lines {string.Join(" ", st.Lines.Select(l => $"{l.Top}-{l.Bot}"))} x {st.Left}-{st.Right}");
    Console.WriteLine($"page {page}: bars {res.FirstBar}-{res.LastBar}, systems {res.Systems.Count} ({string.Join(",", res.Systems.Select(x => x.Measures))} measures), heads {res.Sym.Heads.Count}, rests {res.Sym.Rests.Count}, {(sw.Elapsed - t0).TotalSeconds:F1}s");
    // the page's layout, system by system: its staves by kind (5, T<lines>, L) and its measures (B2; run-band.ps1 checks it)
    if (cfg.HasExtras)
        Console.WriteLine($"layout {page}: {string.Join(" | ", res.Systems.Select(x => string.Join(",", x.All.Select(st => st.OneLine ? "L" : st.Kind == "" ? "5" : st.Kind == "tab" ? $"T{st.Lines.Length}" : "L")) + " " + x.Measures))}");
    foreach (var ((bar, part), evs) in res.Bars)
    {
        if (!all.TryGetValue(bar, out var arr)) all[bar] = arr = new List<Ev>[cfg.Parts.Count];
        arr[part] = evs;
    }
    foreach (var (key, vs) in res.Voices) allVoices[key] = vs;
    foreach (var sys in res.Systems.Where(x => x.Measures > 0)) systemStarts[sys.FirstBar] = page;
    sidecar.AddPage(path, res);
    // --export-read <dir>: the reader's reading of the page, to teach the detector a real print's shapes (ReadExport)
    if (opt.TryGetValue("export-read", out var exportDir)) ReadExport.Write(exportDir, page, res, cfg);
    // what Lily# drew, symbol by symbol ({page}: the page number; page 1 may have none)
    SvgPage svg = null;
    if (opt.TryGetValue("svg", out var svgPattern))
    {
        string svgPath = svgPattern.Replace("{page}", page == 1 ? "" : $"-page{page}");
        // (lysc svg stacks a several-page score's pages in one file: the page is taken from it)
        int svgPageNo = 0;
        if (!File.Exists(svgPath) && to > 1 && File.Exists(svgPattern.Replace("{page}", "")))
        {
            svgPath = svgPattern.Replace("{page}", "");
            svgPageNo = page;
        }
        else if (page == 1 && to > 1) svgPageNo = 1;
        if (File.Exists(svgPath))
        {
            svg = SvgPage.Load(svgPath, res.Dark.W, svgPageNo);
            symEval.Add(res, svg);
            // --export-boxes: the truth as training data for the learnt detector (S10): every glyph's
            // box and name, every stem's tip with the number of beams or flags at it (stem.N, a box a
            // space wide around the tip), every tie's ends, and the staves' geometry, in page pixels
            if (opt.ContainsKey("export-boxes"))
            {
                var tsv = new StringBuilder("kind\tname\tl\tt\tr\tb\tsrc\n");
                foreach (var g in svg.Glyphs.Where(g => g.Full))
                    tsv.Append("glyph\t").Append(g.Name).Append('\t').Append(F(g.L)).Append('\t').Append(F(g.T)).Append('\t').Append(F(g.R)).Append('\t').Append(F(g.B)).Append('\t').Append(g.Src).Append('\n');
                // a grace note's head, small (grace.head: S21), and its accidental, small too but the same sign (its
                // slash, where it has one, is among the marks as grace.slash); its stem's tip with its flags or beams
                // is a stem's like any other (stem.N: the count tells the grace note's value)
                foreach (var g in svg.Glyphs.Where(g => g.Small && (g.Name.StartsWith("noteheads.") || g.Name.StartsWith("accidentals."))))
                    tsv.Append("glyph\t").Append(g.Name.StartsWith("noteheads.") ? "grace.head" : g.Name).Append('\t').Append(F(g.L)).Append('\t').Append(F(g.T)).Append('\t').Append(F(g.R)).Append('\t').Append(F(g.B)).Append('\t').Append(g.Src).Append('\n');
                double hs = 0.5 * res.S;
                foreach (var (x, y, n) in SymEval.StemTails(svg, res.S).Concat(SymEval.StemTails(svg, res.S, graces: true)))
                    tsv.Append("glyph\tstem.").Append(n).Append('\t').Append(F(x - hs)).Append('\t').Append(F(y - hs)).Append('\t').Append(F(x + hs)).Append('\t').Append(F(y + hs)).Append("\t-1\n");
                // a tuplet number: small italic text centred at its point (a box a space around it)
                foreach (var tn in svg.Tuplets)
                    tsv.Append("glyph\ttuplet.").Append(tn.Text).Append('\t').Append(F(tn.X - hs)).Append('\t').Append(F(tn.Y - hs)).Append('\t').Append(F(tn.X + hs)).Append('\t').Append(F(tn.Y + hs)).Append('\t').Append(tn.Src).Append('\n');
                // a barline: a thin rectangle a staff tall, one per staff it crosses (its centre is the
                // staff's middle). Measured in the SVG's own staff space: a scan's print may be
                // smaller than the page the SVG is scaled to (ties004), and the reader's space is then not the SVG's
                double u = svg.Ppu;
                // (a TAB's barline is taller, as the TAB is: level with a TAB clef. Not any other tall
                // thin rectangle: a grand staff's barline runs on between its staves in pieces 5 and 6.8 spaces tall)
                var tabs = svg.Glyphs.Where(g => g.Name == "clefs.tab").Select(g => g.Cy).ToList();
                // (a one-line staff's barline is two spaces tall, centred on its line, since lysc 0.12.0;
                // four before: its line a long one with no other within two spaces)
                var lines = svg.Lines.Where(l => Math.Abs(l.Y2 - l.Y1) < 0.01 * u && l.X2 - l.X1 > 10 * u).Select(l => l.Y1).ToList();
                var lone = lines.Where(y => !lines.Any(y2 => Math.Abs(y2 - y) > 0.01 * u && Math.Abs(y2 - y) < 2 * u)).ToList();
                foreach (var r in svg.Rects.Where(r => r.W < 0.35 * u && (r.H > 3.5 * u && r.H < 4.4 * u
                    || r.H >= 4.4 * u && r.H < 8 * u && tabs.Any(y => Math.Abs(y - (r.Y + r.H / 2)) < u)
                    || r.H > 1.5 * u && r.H < 2.5 * u && lone.Any(y => Math.Abs(y - (r.Y + r.H / 2)) < 0.1 * u))))
                    tsv.Append("glyph\tbarline\t").Append(F(r.X + r.W / 2 - hs)).Append('\t').Append(F(r.Y + r.H / 2 - hs)).Append('\t').Append(F(r.X + r.W / 2 + hs)).Append('\t').Append(F(r.Y + r.H / 2 + hs)).Append('\t').Append(r.Src).Append('\n');
                // a percent repeat (percent.1, percent.2: its slashes), a box two spaces around its centre;
                // a TAB fret number, digit by digit (tab.0 ... tab.9: B3)
                foreach (var pc in svg.Percents)
                    tsv.Append("glyph\tpercent.").Append(pc.Slashes).Append('\t').Append(F(pc.X - 2 * hs)).Append('\t').Append(F(pc.Y - 2 * hs)).Append('\t').Append(F(pc.X + 2 * hs)).Append('\t').Append(F(pc.Y + 2 * hs)).Append('\t').Append(pc.Src).Append('\n');
                foreach (var td in svg.TabDigits)
                    tsv.Append("glyph\ttab.").Append(td.Digit).Append('\t').Append(F(td.L)).Append('\t').Append(F(td.T)).Append('\t').Append(F(td.R)).Append('\t').Append(F(td.B)).Append('\t').Append(td.Src).Append('\n');
                // a dynamic's letters (dyn.p ... dyn.n: M3), each its own box; a hairpin's tip and open
                // end, a pedal bracket's hooks and notch, a box a space around each point
                foreach (var d in svg.Dynamics)
                    tsv.Append("glyph\tdyn.").Append(d.Letter).Append('\t').Append(F(d.L)).Append('\t').Append(F(d.T)).Append('\t').Append(F(d.R)).Append('\t').Append(F(d.B)).Append('\t').Append(d.Src).Append('\n');
                // an octave line's sign (ott.8va, ott.8vb, ott.15ma, ott.15mb: S18), its letters' box; the hook at its dashes' end is among the marks (ott.end)
                foreach (var o in svg.Ottavas)
                    tsv.Append("glyph\tott.").Append(o.Text).Append('\t').Append(F(o.L)).Append('\t').Append(F(o.T)).Append('\t').Append(F(o.R)).Append('\t').Append(F(o.B)).Append('\t').Append(o.Src).Append('\n');
                foreach (var mk in svg.Marks)
                    tsv.Append("glyph\t").Append(mk.Kind).Append('\t').Append(F(mk.X - hs)).Append('\t').Append(F(mk.Y - hs)).Append('\t').Append(F(mk.X + hs)).Append('\t').Append(F(mk.Y + hs)).Append('\t').Append(mk.Src).Append('\n');
                foreach (var t in svg.Ties)
                    tsv.Append("tie\ttie\t").Append(F(t.X1)).Append('\t').Append(F(t.Y1)).Append('\t').Append(F(t.X2)).Append('\t').Append(F(t.Y2)).Append('\t').Append(t.Src).Append('\n');
                foreach (var t in svg.Slurs)
                    tsv.Append("slur\tslur\t").Append(F(t.X1)).Append('\t').Append(F(t.Y1)).Append('\t').Append(F(t.X2)).Append('\t').Append(F(t.Y2)).Append('\t').Append(t.Src).Append('\n');
                foreach (var sys in res.Systems)
                    foreach (var st in sys.Staves)
                        tsv.Append("staff\ts").Append(sys.Index + 1).Append('p').Append(st.Part).Append('\t').Append(st.Left).Append('\t').Append(F(st.Top)).Append('\t').Append(st.Right).Append('\t').Append(F(st.Bottom)).Append('\t').Append(F(st.S)).Append('\n');
                File.WriteAllText(Path.Combine(outDir, $"boxes-p{page:D2}.tsv"), tsv.ToString());
            }
        }
        else Console.WriteLine($"  no {svgPath}");
    }
    // the page's side of the redraw check: the detector's symbols (the truth's with --verify-truth, the reader's own without either)
    {
        IEnumerable<(string, double, double)> syms;
        if (opt.ContainsKey("verify-truth") && svg != null) { pageSide.Source = "truth"; syms = Verify.SvgSymbols(svg, res.S).Select(t => (t.Class, t.X, t.Y)); }
        else if (res.Sym.Detections != null) { pageSide.Source = "detector"; syms = Verify.Detected(res); }
        else { pageSide.Source = "reader"; syms = Verify.ReadSymbols(res); }
        // (a metronome mark's note, in a tempo's words, is no note of the music: the detector sees a head, a dot, a stem there)
        var tempoBoxes = pageMarks.Where(m => m.IsTempo).Select(m => System.Drawing.RectangleF.Inflate(m.Box, 0.1f * m.Box.Height, 0.1f * m.Box.Height)).ToList();
        if (tempoBoxes.Count > 0)
            syms = syms.Where(t => res.Dark.ToSource(t.Item2, t.Item3) is var p && !tempoBoxes.Any(b => b.Contains((float)p.X, (float)p.Y))).ToList();
        // (nor is a chord diagram's string a stem: a lead sheet's diagrams over the staff, seen as stems and given the note under them)
        var grids = Tempo.FramesOf(res).Where(f => f.Grid).Select(f => System.Drawing.Rectangle.Inflate(f.Dark, (int)(0.5 * res.S), (int)(0.5 * res.S))).ToList();
        if (grids.Count > 0) syms = syms.Where(t => !grids.Any(g => g.Contains((int)t.Item2, (int)t.Item3))).ToList();
        pageSide.AddPage(path, res, cfg, syms, symEval.Errors.Where(e => e.Page == page).Select(e => (e.St, e.X, e.Y)));
        foreach (var (b, p, note) in res.TabQuarrels) pageSide.MarkTab(b, p, note);
        pageSide.Checked |= svg != null;
    }
    foreach (var o in res.Sym.Others)
        csv.AppendLine(string.Join(",", page, o.St.System + 1, o.St.Part, o.Class, o.C.MinX, o.C.MinY,
            (o.C.W / res.S).ToString("F2", CultureInfo.InvariantCulture), (o.C.H / res.S).ToString("F2", CultureInfo.InvariantCulture),
            o.C.Fill.ToString("F2", CultureInfo.InvariantCulture), o.St.Pos(o.C.MinY + o.C.H / 2.0).ToString("F1", CultureInfo.InvariantCulture)));
    if (opt.ContainsKey("overlay")) Overlay.Save(path, res, cfg, Path.Combine(outDir, "overlay"));
}
// (and the last page's: no page follows)
foreach (var o in carried) o.Close(null, null);
File.WriteAllText(Path.Combine(outDir, "others.csv"), csv.ToString());
if (opt.ContainsKey("svg"))
{
    Console.WriteLine(symEval.Summary());
    File.WriteAllText(Path.Combine(outDir, "sym-diffs.txt"), string.Join("\n", symEval.Diffs));
    File.WriteAllText(Path.Combine(outDir, "glyph-samples.tsv"), symEval.Samples.ToString());
}

// .lys
expressions.Finish(all, allVoices);
var lys = new StringBuilder();
lys.AppendLine($"// Read by Lily# Scan Import {ReadCommand.Version} from page images {from}-{to}.");
lys.AppendLine($"title {Header.Quote(header?.Title ?? cfg.Title)}");
if (header?.Subtitle != null) lys.AppendLine($"subtitle {Header.Quote(header.Subtitle)}");
if (header?.Composer != null) lys.AppendLine($"composer {Header.Quote(header.Composer)}");
if (header?.Poet != null) lys.AppendLine($"poet {Header.Quote(header.Poet)}");
lys.AppendLine("octave absolute");
// the page's systems and pages, as read: lines and pages break only at the break and pageBreak below
lys.AppendLine("paper { breaksOnly }");
// (no bar read on any page, no staff found: the .lys is the parts with no music, at the score file's
// time, key and clefs, and the truth's bars all count as lost)
int firstBar = all.Count > 0 ? all.Keys.First() : cfg.PageFirstBar.TryGetValue(from, out var fe) ? fe : 1;
if (all.Count == 0)
{
    state.BarTime.TryAdd(firstBar, cfg.TimeAt(firstBar));
    for (int pi = 0; pi < cfg.Parts.Count; pi++)
    {
        state.BarKey.TryAdd((firstBar, pi), cfg.KeyAt(firstBar));
        state.BarClef.TryAdd((firstBar, pi), cfg.Parts[pi].Clef ?? "treble");
    }
}
// (the tempo over the first bar is the piece's; the others change it at their bar, in the first part)
var firstTempo = textMarks.FirstOrDefault(m => m.Bar == firstBar && m.IsTempo);
if (firstTempo != null) lys.AppendLine($"tempo {Tempo.Lys(firstTempo.Words, firstTempo.Bpm, state.BarTime[firstBar])}");
lys.AppendLine($"time {state.BarTime[firstBar]}");
lys.AppendLine($"key {Keys.MajorName(state.BarKey[(firstBar, 0)])}");
lys.AppendLine();
for (int pi = 0; pi < cfg.Parts.Count; pi++)
{
    // (a part on a one-line staff, a lead sheet's rhythm staff: no clef, as the staff shows none)
    if (cfg.Parts[pi].Lines == 1) { lys.AppendLine($"part {cfg.LysId(pi)} {{ instrument guitar }}"); continue; }
    string clef = state.BarClef[(firstBar, pi)];
    string pedal = expressions.PedalStyle(pi);
    lys.AppendLine($"part {cfg.LysId(pi)} {{ clef {clef}{(pedal != null ? " pedal " + pedal : "")} }}");
}
lys.AppendLine();
// the sections: one from each rehearsal mark (words in a box: Lily# prints a section's name so, in
// the form), the music before the first mark "Intro", its name hidden (~); no mark, one "Piece", hidden
// (a mark whose words are no name of a section -- a number, "1", or words with a point, "Var. V" --
// is a section with a declared label, section Mark1 "1" { }: Lily# prints the label in the box)
var sections = new List<(string Name, string Label, int From, bool Shown)>();
var sectionNames = new HashSet<string>();
foreach (var m in textMarks.Where(m => m.Rehearsal != null).GroupBy(m => m.Bar).Select(g => g.First()).OrderBy(m => m.Bar))
{
    if (sections.Count == 0 && m.Bar > firstBar) sections.Add((SectionName("Intro", sectionNames), "", firstBar, false));
    string name = SectionName(m.Rehearsal, sectionNames), words = m.Rehearsal.Trim();
    string label = words.Length > 0 && words != name && !words.Any(ch => ch == '"' || ch == '\\' || char.IsControl(ch)) ? $" \"{words}\"" : "";
    sections.Add((name, label, m.Bar, true));
}
// and one at each of the form's signs: where a repeat opens, an ending begins, after one ends or a
// repeat closes, at a segno or coda sign, after a "to coda" (the sections are named by letter, hidden)
{
    int lastBar = all.Keys.DefaultIfEmpty(firstBar).Last();
    Form.Complete(formSigns);
    var cuts = new SortedSet<int>();
    foreach (var g in formSigns)
    {
        int b = Form.After(g.Kind) ? g.Bar + 1 : g.Bar;
        if (b > firstBar && b <= lastBar) cuts.Add(b);
    }
    if (cuts.Count > 0 && sections.Count == 0) sections.Add((SectionName("Intro", sectionNames), "", firstBar, false));
    int letter = 0;
    foreach (int b in cuts)
        if (!sections.Any(x => x.From == b))
            sections.Add((SectionName(((char)('A' + letter++ % 26)).ToString(), sectionNames), "", b, false));
    sections.Sort((x, y) => x.From.CompareTo(y.From));
}
if (sections.Count == 0) sections.Add(("Piece", "", firstBar, false));
// a pickup: the piece's first bar short of its time on every staff with something on it, by as much on
// each (what the length todo took for "a pickup bar?"), or by the most on one where the next printed
// number bore it out (a voice's rest and note, the piano's one rest, its other hidden: kinkel19-6).
// Lily# takes it as the first section's partial, and that bar is so long; a staff short of it is
// filled with spacer rests. (A staff's whole-bar rest there rests the pickup)
int pickupTicks = 0;
// (each voice's length in the bar, on the staves with something in it)
List<int> VoiceSums(int bar)
{
    var sums = new List<int>();
    for (int pi = 0; pi < cfg.Parts.Count; pi++)
    {
        var vs = allVoices.GetValueOrDefault((bar, pi)) ?? new List<List<Ev>> { all[bar][pi] ?? new List<Ev>() };
        if (vs.All(v => v.Count == 0 || v.All(e => e.Blank || e.FullBar))) continue;
        sums.AddRange(vs.Where(v => v.Count > 0).Select(v => v.Sum(e => e.Ticks)));
    }
    return sums;
}
// (a staff's whole-bar rest in a short bar rests that bar)
void RestsFor(int bar, int ticks)
{
    foreach (var e in Enumerable.Range(0, cfg.Parts.Count).SelectMany(pi => (all[bar][pi] ?? new List<Ev>()).Concat((allVoices.GetValueOrDefault((bar, pi)) ?? new()).SelectMany(v => v))))
        if (e.FullBar) e.Ticks = ticks;
}
if (firstBar == 1 && all.ContainsKey(firstBar))
{
    int full = Dur.OfTime(state.BarTime[firstBar]);
    var sums = VoiceSums(firstBar);
    if (sums.Count > 0 && sums.All(x => x > 0 && x < full) && (sums.Distinct().Count() == 1 || state.PickupOffset == 1) && Dur.Text(sums.Max()) != null)
    {
        pickupTicks = sums.Max();
        RestsFor(firstBar, pickupTicks);
    }
}
// and the piece's last bar, short by the pickup on every staff (the longest exactly so, the others no
// longer): it completes the pickup, as music ends after a pickup. No todo; Lily# takes a short last bar
// as it is, as LilyPond does. (Any other short last bar keeps its todo: a note or rest missed there)
int closingBar = -1, closingTicks = 0;
if (pickupTicks > 0 && all.Count > 1)
{
    int last = all.Keys.Last(), full = Dur.OfTime(state.BarTime[last]);
    var sums = VoiceSums(last);
    if (full > pickupTicks && sums.Count > 0 && sums.All(x => x > 0 && x <= full - pickupTicks) && sums.Max() == full - pickupTicks)
    {
        closingBar = last;
        closingTicks = full - pickupTicks;
        RestsFor(last, closingTicks);
    }
}
int valid = 0, total = 0;
var spans = new Dictionary<(int Bar, int Part), (int Start, int End)>();   // where each part-bar's music is in the text (Verify)
var musicAt = new Dictionary<(int Bar, int Part), int>();                 // where its music starts, after any time, key or clef (the @todo's note)
var doubted = new List<(int Bar, int Part, Ev Ev, List<EvAlt> Alts)>();   // the events with a note's todo, and the readings offered
var offered = new HashSet<EvAlt>();
var lengthIssues = new SortedDictionary<int, List<(int Part, string Id, List<List<Ev>> Voices)>>();   // per bar: its parts whose music does not fill the time
// (each part's bar before, and the page of its system, carried from section to section)
var prevBars = Enumerable.Repeat(-1, cfg.Parts.Count).ToArray();
var sysPages = Enumerable.Repeat(-1, cfg.Parts.Count).ToArray();
// (the bar after each: a system's last bar ends its line with the break)
var nextBars = all.Keys.Zip(all.Keys.Skip(1)).ToDictionary(p => p.First, p => p.Second);
// a span carried over a cut of the form's (a repeat sign, an ending, a jump: what is played before the
// section differs from pass to pass) is lysc's error LYS4023, and one left open at the piece's end
// (a pedal never released) LYS4018: close it on the last note before the cut (or the end), drop the close
// after the cut, and make the part-bar a todo, the mark may run on in print (the comparison with other
// OMRs, 2026-10-26, 4.4)
string formLine = Form.Line(sections, formSigns, all.Keys.DefaultIfEmpty(firstBar).Last());
{
    var cutBars = Form.RepeatCuts(formLine, sections).Select(si => sections[si].From).ToHashSet();
    var families = new (string Name, Func<Ev, bool> Opens, Func<Ev, bool> Closes, Action<Ev, Ev> Close, Action<Ev> Unopen, Action<Ev> Unclose, bool AtEnd)[]
    {
        ("slur", e => e.SlurStart, e => e.SlurEnd, (e, o) => e.SlurEnd = true, e => e.SlurStart = false, e => e.SlurEnd = false, false),
        ("hairpin", e => e.HairpinStart != null, e => e.HairpinEnd != null || e.Dynamic != null, (e, o) => e.HairpinEnd = o.HairpinStart, e => e.HairpinStart = null, e => e.HairpinEnd = null, true),
        ("pedal", e => e.PedalDown, e => e.PedalUp, (e, o) => e.PedalUp = true, e => e.PedalDown = false, e => e.PedalUp = false, true),
        ("octave line", e => e.OttavaStart != 0, e => e.OttavaEnd != 0, (e, o) => e.OttavaEnd = o.OttavaStart, e => e.OttavaStart = 0, e => e.OttavaEnd = 0, true),
        Named("trill line", "trillSpan", "!trillSpan"),
        Named("rit. line", "rit", "!rit"), Named("accel. line", "accel", "!accel"), Named("rall. line", "rall", "!rall"),
        Named("text line", "textSpan", "!textSpan"),
    };
    // (the spans written among the articulations, by their names: @trillSpan … @!trillSpan, @rit … @!rit, @textSpan("…") … @!textSpan)
    static (string, Func<Ev, bool>, Func<Ev, bool>, Action<Ev, Ev>, Action<Ev>, Action<Ev>, bool) Named(string name, string open, string close)
    {
        static bool Is(string a, string n) => a == n || a.StartsWith(n + "(");
        return (name, e => e.Articulations?.Any(a => Is(a, open)) == true, e => e.Articulations?.Any(a => Is(a, close)) == true,
                (e, o) => (e.Articulations ??= new List<string>()).Insert(0, close), e => e.Articulations.RemoveAll(a => Is(a, open)),
                e => e.Articulations.RemoveAll(a => Is(a, close)), true);
    }
    for (int pi = 0; pi < cfg.Parts.Count && all.Count > 0; pi++)
    {
        List<List<Ev>> Voices(int bar) => allVoices.GetValueOrDefault((bar, pi)) ?? new List<List<Ev>> { all[bar][pi] ?? new List<Ev>() };
        // (a trill line's end, on the note its wave ends before: its @!trillSpan on the event before that in
        // its voice, the last under the line, maybe in a bar or a page before, the next line starting on the
        // note or not. On a note: a trill is a note's, the rests after it are passed over, as a whole-bar rest
        // or a slash, which writes no mark)
        var lastIn = new List<Ev>();
        foreach (int bar in all.Keys)
        {
            var vs = Voices(bar);
            for (int vi = 0; vi < vs.Count; vi++)
                foreach (var e in vs[vi])
                {
                    if (e.Articulations?.Remove(Articulations.TrillEndsBefore) == true
                        && (vi < lastIn.Count ? lastIn[vi] : lastIn.FirstOrDefault()) is { } before)
                        (before.Articulations ??= new List<string>()).Add("!trillSpan");
                    if (e.IsRest || e.FullBar || e.Slash) continue;
                    while (lastIn.Count <= vi) lastIn.Add(null);
                    lastIn[vi] = e;
                }
            foreach (var e in all[bar][pi] ?? new List<Ev>()) e.Articulations?.Remove(Articulations.TrillEndsBefore);
        }
        // (the staff's every voice left to right: lysc pairs a slur opened in a bar's second voice with
        // the close in a bar of one voice after it, kinkel10-4's right hand)
        foreach (var f in families)
        {
            Ev open = null, last = null;
            int lastBar = -1, prevBar = int.MinValue;
            bool drop = false;
            void Shut(string why)
            {
                if (last == null || last == open) f.Unopen(open); else f.Close(last, open);
                if (lastBar >= 0) sidecar.AddTodo(lastBar, pi, "span", $"a {f.Name} {why}: closed here, it may run on in print", 0.5);
                open = null;
            }
            foreach (int bar in all.Keys)
            {
                // (a cut between the bar before and this one -- the section's first bar may be none read; and
                // after it, a close with nothing open, its opening not read: lysc takes it for one carried over)
                if (cutBars.Any(c => c > prevBar && c <= bar)) { if (open != null) Shut("carried over a repeat sign, an ending or a jump"); drop = true; }
                prevBar = bar;
                // (a whole-bar rest's or a rhythm slash's marks are not written: no span opens or closes there)
                foreach (var e in Voices(bar).SelectMany(v => v).Where(e => !e.FullBar && !e.Slash).OrderBy(e => e.X))
                {
                    // (an octave or trill line over this one event: opened and closed on it, its end the last
                    // event under the line)
                    if (f.Name is "octave line" or "trill line" && f.Opens(e) && f.Closes(e)) { open = null; drop = false; }
                    else
                    {
                        if (f.Closes(e)) { if (open != null) open = null; else if (drop && e.Dynamic == null) f.Unclose(e); drop = false; }
                        if (f.Opens(e)) { open = e; drop = false; }
                    }
                    // (a slur ends on a note; the others on any event)
                    if (!(f.Name == "slur" && (e.IsRest || e.FullBar))) { last = e; lastBar = bar; }
                }
            }
            if (open != null && f.AtEnd) Shut("left open at the end");
        }
    }
}
for (int si = 0; si < sections.Count; si++)
{
    int secFrom = sections[si].From, secTo = si + 1 < sections.Count ? sections[si + 1].From - 1 : int.MaxValue;
    lys.AppendLine($"section {sections[si].Name}{sections[si].Label} {{");
    if (si == 0 && pickupTicks > 0) lys.AppendLine($"  partial {Dur.Text(pickupTicks)}");
    for (int pi = 0; pi < cfg.Parts.Count; pi++)
    {
        lys.AppendLine($"  {cfg.LysId(pi)} {{");
        int prevBar = prevBars[pi], sysPage = sysPages[pi];
        foreach (var (bar, arr) in all.Where(kv => kv.Key >= secFrom && kv.Key <= secTo))
        {
            // (a multi-bar rest's bars after its first are written with it, R1*n: its line ends where its last bar does)
            if (arr[pi] is [{ MultiBars: -1 }]) { prevBar = bar; continue; }
            int endBar = arr[pi] is [{ MultiBars: > 1 } multi] ? bar + multi.MultiBars - 1 : bar;
            var pre = new List<string>();
            if (systemStarts.TryGetValue(bar, out int barPage)) sysPage = barPage;
            string post = nextBars.TryGetValue(endBar, out int nextBar) && systemStarts.TryGetValue(nextBar, out int nextPage)
                ? (nextPage != sysPage ? " pageBreak" : " break") : "";
            if (prevBar >= 0 && state.BarTime[bar] != state.BarTime[prevBar]) pre.Add("time " + state.BarTime[bar]);
            if (prevBar >= 0 && state.BarKey[(bar, pi)] != state.BarKey[(prevBar, pi)]) pre.Add("key " + Keys.MajorName(state.BarKey[(bar, pi)]));
            if (prevBar >= 0 && cfg.Parts[pi].Lines != 1 && state.BarClef[(bar, pi)] != state.BarClef[(prevBar, pi)] && MidClef(state.BarClef[(bar, pi)]) != MidClef(state.BarClef[(prevBar, pi)]))
                pre.Add("clef " + MidClef(state.BarClef[(bar, pi)]));
            if (pi == 0 && firstBar == bar && state.BarKey[(bar, pi)] != state.BarKey[(firstBar, 0)]) pre.Add("key " + Keys.MajorName(state.BarKey[(bar, pi)]));
            if (pi != 0 && prevBar < 0 && state.BarKey[(bar, pi)] != state.BarKey[(firstBar, 0)]) pre.Add("key " + Keys.MajorName(state.BarKey[(bar, pi)]));
            if (pi == 0 && textMarks.FirstOrDefault(m => m.Bar == bar && m.IsTempo && m != firstTempo) is { } tm)
                pre.Add("tempo " + Tempo.Lys(tm.Words, tm.Bpm, state.BarTime[bar]));
            prevBar = bar;
            // (the pickup, and the last bar that completes it: each as long as it is)
            bool shortBar = bar == firstBar && pickupTicks > 0 || bar == closingBar;
            int barTicks = bar == closingBar ? closingTicks : shortBar ? pickupTicks : Dur.OfTime(state.BarTime[bar]);
            var voices = allVoices.GetValueOrDefault((bar, pi)) ?? new List<List<Ev>> { arr[pi] ?? new List<Ev>() };
            if (voices.Count == 0) voices.Add(new List<Ev>());
            total++;
            string music, note = "";
            double offBy = 0;   // (by how much of the bar its worst voice is off: the check's confidence)
            bool overfull;      // a bar the events cannot fill as written (the redraw check flags it whatever the drawings show)
            var marks = new List<(Ev Ev, int At)>();
            if (voices.Count == 1)
            {
                int sum = voices[0].Sum(e => e.Ticks);
                // (the comment on the bar's own line: it says what, not which bar, as the @todo memos do)
                // (a staff short of the pickup or the last bar: spacer rests where the page shows the others' notes)
                if (sum == barTicks || shortBar && sum < barTicks) valid++; else note = $"  // {Memo.BarLength(new[] { sum }, barTicks, state.BarTime[bar])}";
                overfull = note != "";
                offBy = overfull ? Math.Abs(sum - barTicks) / (double)barTicks : 0;
                music = shortBar && sum < barTicks ? LineText(voices[0], barTicks, pad: true, Anchors(bar, barTicks), marks) : LineText(voices[0], barTicks, pad: false, marks: marks);
            }
            else
            {
                // two voices on the staff: each a line of its own, a short one filled with spacer rests
                // (a voice that enters late is short by design, beside a full one: a voice over the bar's
                // length, or no voice filling it, is wrong for sure)
                var sums = voices.Select(v => v.Sum(e => e.Ticks)).ToList();
                if (sums.All(x => x == barTicks || shortBar && x < barTicks)) valid++; else note = $"  // {Memo.BarLength(sums, barTicks, state.BarTime[bar])}";
                overfull = sums.Any(x => x > barTicks) || !shortBar && !sums.Any(x => x == barTicks);
                offBy = sums.Max(x => Math.Abs(x - barTicks)) / (double)barTicks;
                music = "voice";
                foreach (var v in voices)
                {
                    var vm = new List<(Ev Ev, int At)>();
                    string line = LineText(v, barTicks, pad: true, Anchors(bar, barTicks), vm);
                    marks.AddRange(vm.Select(m => (m.Ev, music.Length + 3 + m.At)));
                    music += " { " + line + " }";
                }
            }
            int start = lys.Length;
            string head = $"    {string.Join(" ", pre)}{(pre.Count > 0 ? " " : "")}";
            lys.AppendLine($"{head}{music} |{post}{note}");
            spans[(bar, pi)] = (start, lys.Length);
            musicAt[(bar, pi)] = start + head.Length;
            // the events read with a doubt: an @todo each, its candidates the event as read and read the other ways
            foreach (var (ev, evAt) in marks)
            {
                // (named as the note would be: "F♯", "F♮"; a dot's as "dotted", "no dot")
                static string Name(Pitch p) => Memo.Name(p);
                var pitchOnly = ev.Alts.All(a => a.To != a.From) && ev.Alts.Select(a => a.From).Distinct().Count() == 1;
                var cands = new List<(string Label, string Text)> { (pitchOnly ? $"{Name(ev.Alts[0].From)} (as read)" : "as read", EvCore(ev, barTicks)) };
                var used = new List<EvAlt>();
                // (a reading offered once: a bar repeated by a % sign has its events' copies)
                foreach (var a in ev.Alts.Where(offered.Add))
                    if (AltEv(ev, a) is Ev v && cands.All(c => c.Text != EvCore(v, barTicks)))
                    {
                        cands.Add((a.To != a.From ? Name(a.To) : a.DotsBy > 0 ? "dotted" : "no dot", EvCore(v, barTicks)));
                        used.Add(a);
                    }
                if (used.Count > 0) { sidecar.AddNoteTodo(bar, pi, start + head.Length + evAt, used, cands); doubted.Add((bar, pi, ev, used)); }
            }
            if (overfull) verify?.MarkLength(bar, pi, note.Trim().TrimStart('/').Trim(), Math.Min(1, offBy));
            // (a bar's parts that are off: one todo for the bar, once the redraw check has said where the drawings differ)
            if (note != "")
            {
                if (!lengthIssues.TryGetValue(bar, out var off)) lengthIssues[bar] = off = new();
                off.Add((pi, cfg.LysId(pi), voices));
            }
        }
        prevBars[pi] = prevBar; sysPages[pi] = sysPage;
        lys.AppendLine("  }");
    }
    lys.AppendLine("}");
}
lys.AppendLine();
lys.AppendLine(formLine);
lys.AppendLine();
lys.AppendLine("score {");
{
    // (the staves within the groups the score file names, a brace's grandStaff within a bracket's
    // staffGroup: omr read guesses them from the page's braces and brackets)
    var opens = new Dictionary<int, List<(string Kind, int Last)>>();
    foreach (var (kind, gids) in cfg.Groups)
    {
        var idx = gids.Select(id => cfg.Parts.FindIndex(p => p.Id == id)).Where(k => k >= 0).ToList();
        if (idx.Count < 2) continue;
        if (!opens.TryGetValue(idx.Min(), out var opened)) opens[idx.Min()] = opened = new();
        opened.Add((kind, idx.Max()));
    }
    var open = new List<(int Last, int Depth)>();
    int depth = 1;
    for (int pi = 0; pi < cfg.Parts.Count; pi++)
    {
        if (opens.TryGetValue(pi, out var gs))
            foreach (var (kind, last) in gs.OrderByDescending(g => g.Last).ThenBy(g => g.Kind == "staffGroup" ? 0 : 1))
            {
                lys.AppendLine($"{new string(' ', 2 * depth)}{kind} {{");
                open.Add((last, depth++));
            }
        lys.AppendLine($"{new string(' ', 2 * depth)}staff {cfg.LysId(pi)} \"{cfg.Parts[pi].Name}\"{(cfg.Parts[pi].Lines == 1 ? " as lines 1" : "")}");
        while (open.Count > 0 && open[^1].Last <= pi)
        {
            depth = open[^1].Depth;
            open.RemoveAt(open.Count - 1);
            lys.AppendLine($"{new string(' ', 2 * depth)}}}");
        }
    }
}
lys.AppendLine("}");
File.WriteAllText(Path.Combine(outDir, "omr.lys"), lys.ToString());
Console.WriteLine($"bar length ok: {valid}/{total} part-bars; {sw.Elapsed.TotalSeconds:F1}s total");
if (Tab.Seen > 0) Console.WriteLine($"tab frets borne out by a head: {Tab.BorneOut}/{Tab.Seen}, {Tab.Reread} more read as the detector's other digit");

if (state.Auto && cfg.Times.Count > 0)
{
    // what the page showed against what the config says
    var miss = new List<string>();
    int checks = 0;
    foreach (var bar in all.Keys)
    {
        checks++;
        if (state.BarTime[bar] != cfg.TimeAt(bar)) miss.Add($"bar {bar}: time {state.BarTime[bar]}, config {cfg.TimeAt(bar)}");
        for (int pi = 0; pi < cfg.Parts.Count; pi++)
        {
            checks += 2;
            // (a percussion staff shows no key: the score's key is not its)
            if (cfg.Parts[pi].Clef == "percussion") checks--;
            else if (state.BarKey[(bar, pi)] != cfg.KeyAt(bar)) miss.Add($"bar {bar} {cfg.Parts[pi].Id}: key {state.BarKey[(bar, pi)]}, config {cfg.KeyAt(bar)}");
            if (state.BarClef[(bar, pi)] != cfg.Parts[pi].Clef) miss.Add($"bar {bar} {cfg.Parts[pi].Id}: clef {state.BarClef[(bar, pi)]}, config {cfg.Parts[pi].Clef}");
        }
    }
    Console.WriteLine($"signatures read: {checks - miss.Count}/{checks} agree with the config");
    foreach (var m in miss.Take(30)) Console.WriteLine("  " + m);
}

// (the bars as read, in order, and the truth's bar each is scored as (0: none, an extra measure):
// the todos, written with the bars as read, are held against the scoring in todos-truth.txt)
var readBars = all.Keys.ToList();
var scoredAs = readBars.ToArray();
HashSet<(int Bar, string Part)> wrongBars = null;
var lostBars = new List<int>();
// --by-order: the measures read are scored in the order read, numbered from the first page's first
// bar, as another reader's MusicXML is (--pred). The printed bar numbers the reader goes by leave out
// a pickup that the truth counts: the measures after the first system's would all be a bar off.
if (truth != null && opt.ContainsKey("by-order") && all.Count > 0)
{
    int firstNo = cfg.PageFirstBar.TryGetValue(from, out var fb0) ? fb0 : 1;
    var renum = all.Keys.Select((b, i) => (Old: b, New: firstNo + i)).ToList();
    scoredAs = renum.Select(r => r.New).ToArray();
    var allNew = new SortedDictionary<int, List<Ev>[]>();
    var voicesNew = new Dictionary<(int Bar, int Part), List<List<Ev>>>();
    foreach (var (o, n) in renum)
    {
        allNew[n] = all[o];
        for (int pi = 0; pi < cfg.Parts.Count; pi++)
            if (allVoices.TryGetValue((o, pi), out var vs)) voicesNew[(n, pi)] = vs;
    }
    all = allNew;
    allVoices = voicesNew;
}
// --align: the measures read, in order, matched with the truth's bars (BarAlign); a measure that
// stands for none is scored as extra
var extras = new List<(int At, int Part, List<Ev> Evs)>();
if (truth != null && opt.ContainsKey("align") && all.Count > 0)
{
    int first1 = cfg.PageFirstBar.TryGetValue(from, out var f1) ? f1 : 1;
    int last1 = cfg.PageFirstBar.TryGetValue(to + 1, out var n1) ? n1 - 1 : truth.Bars.Keys.Select(k => k.Bar).DefaultIfEmpty(0).Max();
    var tbars = truth.Bars.Keys.Select(k => k.Bar).Where(b => b >= first1 && b <= last1).Distinct().Order().ToList();
    var order = all.ToList();
    var ids = cfg.Parts.Select(q => q.Id).ToList();
    var map = BarAlign.Align(truth, tbars, ids, order.Select(kv => (Func<string, List<Ev>>)(p => kv.Value[ids.IndexOf(p)]?.Where(e => !e.Slash).ToList())).ToList());
    var allNew = new SortedDictionary<int, List<Ev>[]>();
    var voicesNew = new Dictionary<(int Bar, int Part), List<List<Ev>>>();
    for (int j = 0; j < order.Count; j++)
    {
        var (o, arr) = order[j];
        if (map[j] == 0)
        {
            for (int pi = 0; pi < cfg.Parts.Count; pi++) extras.Add((j + 1, pi, arr[pi]?.Where(e => !e.Slash).ToList() ?? new List<Ev>()));
            continue;
        }
        allNew[map[j]] = arr;
        for (int pi = 0; pi < cfg.Parts.Count; pi++)
            if (allVoices.TryGetValue((o, pi), out var vs)) voicesNew[(map[j], pi)] = vs;
    }
    Console.WriteLine($"align: {map.Count(x => x > 0)} of {order.Count} measures read stand for one of the truth's {tbars.Count} bars");
    all = allNew;
    allVoices = voicesNew;
    scoredAs = map;   // (order is readBars' order: --by-order renumbers in order)
}

if (truth != null)
{
    var sc = new Scorer();
    foreach (var (xAt, xPart, xEvs) in extras)
        if (truth.Bars.Keys.Any(k => k.Part == cfg.Parts[xPart].Id)) sc.AddExtra(xAt, cfg.Parts[xPart].Id, xEvs);
    // The truth's bars on the pages read that the reader produced nothing for (a system whose
    // staves were not found: chords006's scan has a second system the reader never saw) count as
    // read with nothing in them. Left out, a lost system would not show in the score at all.
    // The pages' bars run from the first page's first bar to the bar before the next page's
    // (the last page: to the truth's last bar).
    int startBar = cfg.PageFirstBar.TryGetValue(from, out var sb0) ? sb0 : from == 1 ? 1 : all.Keys.DefaultIfEmpty(1).Min();
    int endBar = cfg.PageFirstBar.TryGetValue(to + 1, out var nb) ? nb - 1 : truth.Bars.Keys.Select(k => k.Bar).DefaultIfEmpty(0).Max();
    var lost = Enumerable.Range(startBar, Math.Max(0, endBar - startBar + 1)).Where(b => !all.ContainsKey(b) && cfg.Parts.Any(q => truth.Bars.ContainsKey((b, q.Id)))).ToList();
    if (lost.Count > 0) Console.WriteLine($"bars not read (no system found for them): {string.Join(" ", lost)}");
    foreach (var bar in lost) all[bar] = new List<Ev>[cfg.Parts.Count];   // (the .lys is written above; all serves the scoring only from here)
    lostBars = lost;
    // a part the truth says nothing of (a band's drums: no .notes) is not scored
    var scored = cfg.Parts.Select(q => truth.Bars.Keys.Any(k => k.Part == q.Id)).ToArray();
    // (a rhythm slash is no note: the truth has none)
    static List<Ev> Notes(IEnumerable<Ev> evs) => evs?.Where(e => !e.Slash).ToList() ?? new List<Ev>();
    foreach (var (bar, arr) in all)
        for (int pi = 0; pi < cfg.Parts.Count; pi++)
        {
            if (!scored[pi]) continue;
            var tev = truth.Bars.GetValueOrDefault((bar, cfg.Parts[pi].Id)) ?? new List<Ev>();
            if (state.Poly) sc.AddMultiset(bar, cfg.Parts[pi].Id, tev, Notes(arr[pi]));
            else if (truth.Bars.ContainsKey((bar, cfg.Parts[pi].Id))) sc.Add(bar, cfg.Parts[pi].Id, tev, Notes(arr[pi]));
        }
    Console.WriteLine(state.Poly ? sc.MultisetSummary() : sc.Summary());
    // (a truth that marks no arpeggio, and a reading that finds none: nothing to say)
    if (sc.ArpTruth + sc.ArpOmr > 0) Console.WriteLine(sc.ArpeggioSummary());
    File.WriteAllText(Path.Combine(outDir, "diffs.txt"), string.Join("\n", sc.Diffs));
    File.WriteAllText(Path.Combine(outDir, "arpeggio-diffs.txt"), string.Join("\n", sc.ArpDiffs));
    wrongBars = sc.Wrong;
    // (--export-read: each bar as read, the truth's bar it was scored as, and per part whether it was right: which bars to learn from)
    if (opt.TryGetValue("export-read", out var exportDir))
    {
        var rows = new StringBuilder("read\tscored\tpart\tright\n");
        for (int j = 0; j < readBars.Count; j++)
            for (int pi = 0; pi < cfg.Parts.Count; pi++)
                if (scored[pi] && scoredAs[j] > 0 && truth.Bars.ContainsKey((scoredAs[j], cfg.Parts[pi].Id)))
                    rows.Append(readBars[j]).Append('\t').Append(scoredAs[j]).Append('\t').Append(cfg.Parts[pi].Id).Append('\t').Append(sc.Wrong.Contains((scoredAs[j], cfg.Parts[pi].Id)) ? 0 : 1).Append('\n');
        File.WriteAllText(Path.Combine(exportDir, "read-bars.tsv"), rows.ToString());
    }
    if (truth.Voices.Count > 0)
    {
        foreach (var bar in all.Keys)
            for (int pi = 0; pi < cfg.Parts.Count; pi++)
            {
                if (!scored[pi]) continue;
                var tv =truth.Voices.GetValueOrDefault((bar, cfg.Parts[pi].Id)) ?? new List<List<Ev>>();
                var ov = (allVoices.GetValueOrDefault((bar, pi)) ?? new List<List<Ev>>()).Select(Notes).Where(v => v.Count > 0).ToList();
                sc.AddVoices(bar, cfg.Parts[pi].Id, tv, ov);
            }
        Console.WriteLine(sc.VoiceSummary());
        File.WriteAllText(Path.Combine(outDir, "voice-diffs.txt"), string.Join("\n", sc.VDiffs));
    }
    // the note todos against the truth: of each reading offered, does the bar read the other way
    // match more of the truth's notes (pitch and length; "other"), fewer ("as read"), or as many
    // (todos.txt is written every time, empty too: no old one is left to count)
    if (!opt.ContainsKey("by-order") && !opt.ContainsKey("align"))
    {
        int other = 0, asRead = 0, neither = 0;
        var report = new List<string>();
        static List<(int, int)> Heads(IEnumerable<Ev> evs) => evs.Where(e => !e.IsRest && !e.Slash).SelectMany(e => (e.Chord ?? new List<Pitch> { e.P }).Select(p => (p.Midi(), e.Ticks))).ToList();
        static int Matched(List<(int, int)> read, List<(int, int)> truthHeads)
        {
            var left = truthHeads.ToList();
            return read.Count(h => left.Remove(h));
        }
        foreach (var (bar, pi, ev, alts) in doubted)
        {
            var tev = truth.Bars.GetValueOrDefault((bar, cfg.Parts[pi].Id));
            if (tev == null || !all.TryGetValue(bar, out var arr) || arr[pi] == null) continue;
            var want = Heads(tev);
            var read = Heads(arr[pi]);
            foreach (var a in alts)
            {
                var v = AltEv(ev, a);
                var mine = Heads(new[] { ev });
                var changed = read.ToList();
                foreach (var h in mine) changed.Remove(h);
                changed.AddRange(Heads(new[] { v }));
                int gain = Matched(changed, want) - Matched(read, want);
                string verdict = gain > 0 ? "other" : gain < 0 ? "as read" : "neither";
                if (verdict == "other") other++; else if (verdict == "as read") asRead++; else neither++;
                report.Add($"bar {bar} {cfg.Parts[pi].Id} {ev.Show()} -> {v.Show()}: {a.What}: {verdict}");
            }
        }
        if (report.Count > 0) Console.WriteLine($"note todos: {report.Count} readings offered: the truth's the other {other}, as read {asRead}, neither {neither}");
        File.WriteAllText(Path.Combine(outDir, "todos.txt"), string.Join("\n", report));
    }
    // (a bar the truth scores wrong is one the redraw check should flag)
    if (verify != null)
    {
        verify.Checked = true;
        foreach (var (b, p) in sc.Wrong) verify.MarkWrong(b, cfg.Parts.FindIndex(q => q.Id == p));
    }
}

// the redraw check (--verify): the .lys engraved again, against the pages
if (verify != null)
{
    var report = new List<string>();
    readCmd?.Stage("verify");
    var lines = verify.Compare(Path.Combine(outDir, "omr.lys"), spans, cfg, report);
    foreach (var l in lines) Console.WriteLine(l);
    File.WriteAllText(Path.Combine(outDir, "verify.txt"), string.Join("\n", lines.Concat(report)));
}
// the todos: in a bar where the drawings differ, the check's flags (they take in the lengths there);
// elsewhere a bar that does not fill its time, one todo for each of its parts that is off: quiet
// when the check found the bar's drawing as the page's
var differing = verify?.Places.Where(p => p.Diff).Select(p => p.Bar).ToHashSet() ?? new HashSet<int>();
if (verify != null)
    foreach (var p in verify.Places.Where(p => differing.Contains(p.Bar))) sidecar.AddTodo(p.Bar, p.Part, "verify", p.Notes, p.Confidence);
foreach (var (bar, off) in lengthIssues.Where(kv => !differing.Contains(kv.Key)))
    sidecar.AddLengthTodo(bar, off, Dur.OfTime(state.BarTime[bar]), state.BarTime[bar], bar == firstBar, quiet: verify?.Drawn == true, readings);
// --todo-threshold x: a todo this sure is kept in x.omr.json only (0.8; 1: every todo goes into the .lys)
if (opt.TryGetValue("todo-threshold", out var tth)) sidecar.Threshold = double.Parse(tth, CultureInfo.InvariantCulture);
// the todos as @todo(key "memo") on each bar's first note or rest (Lily# warns, and draws them red)
int todoMarks = sidecar.Annotate(Path.Combine(outDir, "omr.lys"), spans, musicAt, cfg);
if (todoMarks > 0 || sidecar.QuietCount > 0) Console.WriteLine($"todo: {todoMarks} @todo in omr.lys, {sidecar.QuietCount} kept in the side file only");
// the todos against the truth (todos-truth.txt, tools/score-todos.ps1): does each mark a part-bar
// the truth scores wrong, and which wrong part-bars carry no mark
if (wrongBars != null)
{
    var scoredBar = readBars.Select((b, i) => (b, i)).ToDictionary(x => x.b, x => scoredAs[x.i]);
    var scoredParts = cfg.Parts.Select(q => truth.Bars.Keys.Any(k => k.Part == q.Id)).ToList();
    File.WriteAllText(Path.Combine(outDir, "todos-truth.txt"), string.Join("\n", sidecar.AgainstTruth(scoredBar, wrongBars, lostBars, scoredParts, cfg)));
}
// for the user: the todos' boxes on their pages (--annotate-all: the quiet ones too)
if (verify != null || readCmd != null)
{
    var marks = sidecar.SaveMarks(outDir, cfg, opt.ContainsKey("annotate-all"));
    if (marks.Count > 0) Console.WriteLine($"todo: boxes on {string.Join(", ", marks.Select(Path.GetFileName))}");
}
// beside the .lys, for an editor: the pages, each part-bar's box and line, the todos
// (the read command: under the input's name, the pages as the user gave them)
if (readCmd != null)
{
    foreach (var (page, source) in readCmd.Sources) sidecar.ImageOf[page] = source;
    sidecar.Write(Path.Combine(outDir, readCmd.Name + ".lys"), lys.ToString(), spans, cfg, pageSide.Saved(cfg));
    Environment.Exit(readCmd.Finish(outDir, sidecar.Written, sidecar.QuietCount));
}
sidecar.Write(Path.Combine(outDir, "omr.lys"), lys.ToString(), spans, cfg, pageSide.Saved(cfg));

// A bar's timeline: where each event of a complete line (one that fills the bar) stands,
// on any staff of the system, and when it starts. Staves are aligned, so x tells time.
List<(double X, int At)> Anchors(int bar, int barTicks)
{
    var list = new List<(double, int)>();
    for (int pi = 0; pi < cfg.Parts.Count; pi++)
        foreach (var v in allVoices.GetValueOrDefault((bar, pi)) ?? new List<List<Ev>>())
        {
            if (v.Sum(e => e.Ticks) != barTicks) continue;
            int t = 0;
            foreach (var e in v) { list.Add((e.X, t)); t += e.Ticks; }
        }
    list.Sort((a, b) => a.Item1.CompareTo(b.Item1));
    return list;
}

static string F(double v) => v.ToString("F1", CultureInfo.InvariantCulture);

// (marks: where each event with other readings ends in the text, before any tie or slur: its @todo's place)
static string LineText(List<Ev> evs, int barTicks, bool pad, List<(double X, int At)> anchors = null, List<(Ev Ev, int At)> marks = null)
{
    var parts = new List<string>();
    int now = 0, length = 0;
    void Add(string text, Ev e = null, int inner = 0)
    {
        if (parts.Count > 0) length++;
        if (e?.Alts != null) marks?.Add((e, length + inner));
        parts.Add(text);
        length += text.Length;
    }
    void Spacer(int ticks)
    {
        foreach (int b in new[] { 1, 2, 4, 8, 16, 32, 64 })
            while (ticks >= Dur.Whole / b) { Add("s" + b); ticks -= Dur.Whole / b; }
    }
    // the grace notes before an event, as the page has them (S21: a slash makes an acciaccatura, the
    // flags or beams give the value); with nothing read of that, one an eighth with a slash, as most
    // music prints a lone one, several sixteenths
    // a clef printed inside the measure, before the event (an old print's left hand changing clef within the bar)
    void Clef(Ev e) { if (e.ClefBefore != null) Add("clef " + MidClef(e.ClefBefore)); }
    void Graces(Ev e)
    {
        if (e.Graces == null || e.Graces.Count == 0) return;
        string kind = e.GraceKind ?? (e.Graces.Count == 1 ? "acciaccatura" : "grace");
        int value = e.GraceValue > 0 ? e.GraceValue : e.Graces.Count == 1 ? 8 : 16;
        Add($"{kind} {{ {string.Join(" ", e.Graces.Select(p => Sounding(p, e) + value))} }}");
    }
    for (int i = 0; i < evs.Count;)
    {
        // a voice that enters late (or comes back from another staff) waits with spacer rests
        if (pad && anchors != null && anchors.Count > 0)
        {
            var before = anchors.Where(a => a.X <= evs[i].X + 8).ToList();
            int at = before.Count > 0 ? before[^1].At : 0;
            // (no other line's event in its column: its time between the ones left and right of it, by its x,
            // on the grid of its own value -- a voice's last eighth on the half beat, between the piano's
            // triplet eighths, the nearer of which put it a 64th and a 32nd off: kinkel19-6's song, bar 2)
            if (before.Count > 0 && anchors.FirstOrDefault(a => a.X > evs[i].X + 8) is var (ax, aAt) && ax > 0 && evs[i].X - before[^1].X > 8 && aAt > at)
            {
                double t = at + (evs[i].X - before[^1].X) / (ax - before[^1].X) * (aAt - at);
                for (int g = Dur.Whole; g >= Dur.Whole / 32; g /= 2)
                {
                    if (g > evs[i].Ticks) continue;
                    int snapped = (int)Math.Round(t / g) * g;
                    if (snapped > at && snapped < aAt) { at = snapped; break; }
                }
            }
            if (at > now) { Spacer(at - now); now = at; }
        }
        var sc = evs[i].Scale;
        // a two-note tremolo: the two alternate in notes of the strokes' value (one an eighth, two a sixteenth)
        // for the time of the two, repeat tremolo 6 { c''16 fis'16 } for two dotted halves in 6/8
        if (sc == (1, 1) && evs[i].TremoloFirst && i + 1 < evs.Count && evs[i + 1].Tremolo == evs[i].Tremolo && !evs[i + 1].TremoloFirst
            && evs[i + 1].Scale == (1, 1) && evs[i + 1].Ticks == evs[i].Ticks)
        {
            int note = Dur.Whole / (4 << evs[i].Tremolo), all = evs[i].Ticks + evs[i + 1].Ticks;
            if (all % (2 * note) == 0)
            {
                var (a, b) = (evs[i].Copy(), evs[i + 1].Copy());
                a.Ticks = b.Ticks = note;
                Clef(evs[i]); Graces(evs[i]);
                Add($"repeat tremolo {all / (2 * note)} {{");
                Add(EvText(a, barTicks), evs[i], EvCore(a, barTicks).Length);
                Add(EvText(b, barTicks), evs[i + 1], EvCore(b, barTicks).Length);
                Add("}");
                now += all; i += 2;
                continue;
            }
        }
        if (sc == (1, 1)) { Clef(evs[i]); Graces(evs[i]); Add(EvText(evs[i], barTicks), evs[i], EvCore(evs[i], barTicks).Length); now += evs[i].Ticks; i++; continue; }
        // one group per tuplet number as written: two triplets in a row are not one sextuplet (the redraw would show one number)
        int tid = evs[i].Tuplet;
        Add($"tuplet {sc.Den}/{sc.Num} {{");
        while (i < evs.Count && evs[i].Scale == sc && evs[i].Tuplet == tid) { Clef(evs[i]); Graces(evs[i]); Add(EvText(evs[i], barTicks), evs[i], EvCore(evs[i], barTicks).Length); now += evs[i].Ticks; i++; }
        Add("}");
    }
    // spacer rests fill a voice that stops short (a voice that only enters, or a misread value)
    if (pad && barTicks > now) Spacer(barTicks - now);
    return string.Join(" ", parts);
}

static string EvText(Ev e, int barTicks) => EvCore(e, barTicks)
    // (a rhythm slash takes its articulations, /4@accent; no dynamics or pedal are given it)
    + (e.FullBar ? "" : string.Concat((e.Articulations ?? new()).Select(a => "@" + a)))
    + (e.FullBar || e.Slash ? "" : (e.Dynamic != null ? "@" + e.Dynamic : "") + (e.HairpinEnd != null ? "@!" + e.HairpinEnd : "")
        + (e.HairpinStart != null ? "@" + e.HairpinStart : "") + (e.PedalUp ? "@!sustain" : "") + (e.PedalDown ? "@sustain" : ""))
    + (e.OttavaStart != 0 ? "@" + OttavaName(e.OttavaStart) : "") + (e.OttavaEnd != 0 ? "@!" + OttavaName(e.OttavaEnd) : "")
    + (e.FullBar || e.Slash || e.IsRest ? "" : (e.Tie ? "~" : "") + (e.SlurEnd ? ")" : "") + (e.SlurStart ? "(" : ""));

// an octave line by the letters printed (1 8va, -1 8vb, 2 15ma, -2 15mb): Lily#'s names for it since 2026-10-28
static string OttavaName(int ottava) => (Math.Abs(ottava) == 2 ? "15m" : "8v") + (ottava > 0 ? "a" : "b");

// the event itself, without its tie or slur: what an @todo is put after, and what a candidate replaces
static string EvCore(Ev e, int barTicks) => e.FullBar ? FullRest(barTicks, e.MultiBars)
    : e.Slash ? "/" + (Dur.Text(e.Ticks * e.Scale.Den / e.Scale.Num) ?? "4")
    : (e.IsRest ? "r" : e.Chord != null ? "<" + string.Join(" ", e.Chord.Select(p => Sounding(p, e))) + ">" : Sounding(e.P, e))
      + (Dur.Text(e.Ticks * e.Scale.Den / e.Scale.Num) ?? "4");

// a pitch as the .lys writes it: as it sounds, under an octave clef an octave below the place it is printed
// (the clef's 8 draws it an octave up)
static string Sounding(Pitch p, Ev e) => (e.OctaveClef ? new Pitch(p.Diatonic - 7, p.Alter) : p).Lys();

// the event read the other way (null when it cannot be written: its head's pitch was changed after, a dot too few)
static Ev AltEv(Ev e, EvAlt a)
{
    var v = e.Copy();
    if (a.To != a.From)
    {
        if (v.Chord != null)
        {
            int k = v.Chord.IndexOf(a.From);
            if (k < 0 || v.Chord.Contains(a.To)) return null;
            v.Chord[k] = a.To;
            v.Chord.Sort((p1, p2) => p1.Diatonic != p2.Diatonic ? p1.Diatonic.CompareTo(p2.Diatonic) : p1.Alter.CompareTo(p2.Alter));
            v.P = v.Chord[^1];
        }
        else if (v.P == a.From) v.P = a.To;
        else return null;
    }
    if (a.DotsBy != 0)
    {
        var text = Dur.Text(e.Ticks * e.Scale.Den / e.Scale.Num);
        if (text == null) return null;
        int basis = int.Parse(text.TrimEnd('.')), dots = text.Length - text.TrimEnd('.').Length + a.DotsBy;
        if (dots < 0 || dots > 2) return null;
        v.Ticks = Dur.Of(basis, dots) * e.Scale.Num / e.Scale.Den;
    }
    return v;
}

// (a rehearsal mark's words as a section's name: a letter first, letters, figures and _ after; one
// starting with a figure, a number in a box, "Mark12"; a name already taken, "A_2")
static string SectionName(string words, HashSet<string> taken)
{
    string name = System.Text.RegularExpressions.Regex.Replace(words.Trim(), @"[^\p{L}\p{N}_]+", "_").Trim('_');
    if (name.Length == 0) name = "Mark";
    else if (!char.IsLetter(name[0])) name = "Mark" + name;
    string unique = name;
    for (int k = 2; !taken.Add(unique); k++) unique = $"{name}_{k}";
    return unique;
}

// (a clef inside the music: lysc takes alto, bass, tenor, treble and treble_8 there, the others only in
// a part's header; bass_8 is drawn as bass, its notes where they stand, the rest as treble)
static string MidClef(string clef) => clef is "alto" or "bass" or "tenor" or "treble" or "treble_8" ? clef : clef.StartsWith("bass") ? "bass" : "treble";

// (a multi-bar rest's: its count after the *, times the one a meter's bar has, "R8*10" for two bars of 5/8)
static string FullRest(int ticks, int bars = 1)
{
    string one = FullRest1(ticks);
    if (bars <= 1) return one;
    int star = one.IndexOf('*');
    return star < 0 ? $"{one}*{bars}" : $"{one[..star]}*{int.Parse(one[(star + 1)..]) * bars}";
}

static string FullRest1(int ticks) => ticks switch
{
    Dur.Whole => "R1",
    Dur.Whole * 3 / 2 => "R1.",
    Dur.Whole * 3 / 4 => "R2.",
    Dur.Whole / 2 => "R2",
    // (another meter's bar: one value when one fits, "R4." for 3/8; else so many of the largest that
    // divides it, "R8*5" for 5/8 — lysc takes a whole number after the *, not R1*720/1920)
    _ when Dur.Text(ticks) is string one => "R" + one,
    _ => Enumerable.Range(0, 7).Select(k => 1 << k).Where(b => ticks % (Dur.Whole / b) == 0).Select(b => $"R{b}*{ticks / (Dur.Whole / b)}").DefaultIfEmpty("R1").First(),
};
