using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Omr;

/// <summary>
/// <c>omr verify &lt;x.lys&gt; [--lysc path] [--todo-threshold x] [--progress json] [--candidate tmp.lys] [--bars 12,13] [--no-write]</c>:
/// the redraw check again, on a .lys the user has corrected since it was read (Lily#'s verify on save, B5). No page is read again:
/// x.omr.json keeps what each page showed bar by bar (Verify.Saved). The .lys as it stands is drawn
/// by lysc, held against that, and x.omr.json's todos from the check (source "verify") and its
/// measures' lines are written anew. A bar's length is lysc's own word (LYS2001) now: the
/// reading's todos on lengths go with the check's of before (a new flag takes the key of the @todo
/// the .lys still has on its bar). The note todos stay while the .lys still carries their @todo.
/// The .lys is not touched.
/// For Lily#'s AI fix (the proposal of 2026-10-23, "verify-bars" in --version --json's features): --candidate
/// holds another text (a proposal written to a temp file, the edit not yet saved) against x.lys's side file
/// and pages; --bars 12,13 asks after those bars (the reader's numbers, as measures[].measure) and answers
/// a "bar" event each, part by part (part null: the bar's stems and tuplet numbers), before done; --no-write
/// leaves x.omr.json and the annotated pages as they are (so does --candidate: its lines are another text's).
/// stdout and exit codes as the read command's: 0 checked, 2 the input (no .lys, no sidecar, a
/// .lys lysc cannot draw), 3 the checker itself.
/// </summary>
static class VerifyCommand
{
    static TextWriter stdout;
    static bool progress;
    static string work;

    public static int Run(string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        stdout = Console.Out;
        Console.SetOut(Console.Error);
        try { return Check(args); }
        catch (Exception e) when (e is not Failed)
        {
            Console.Error.WriteLine(e);
            Emit(new { @event = "error", code = "internal", message = e.Message }, always: true);
            return 3;
        }
        catch (Failed) { return 2; }
        finally
        {
            try { if (work != null && Directory.Exists(work)) Directory.Delete(work, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    static int Check(string[] args)
    {
        string lys = null, lysc = null, candidate = null;
        List<int> askedBars = null;      // --bars: the bars asked after (null: every bar, no "bar" events)
        bool noWrite = false;
        double threshold = 0.8;     // (a flag this sure is a quiet todo, as omr read's --todo-threshold)
        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];
            string Value() => i + 1 < args.Length ? args[++i] : throw Fail($"{a} wants a value");
            switch (a)
            {
                case "--lysc": lysc = Value(); break;
                case "--candidate": candidate = Value(); noWrite = true; break;
                case "--no-write": noWrite = true; break;
                case "--bars":
                    string list = Value();
                    askedBars = new List<int>();
                    foreach (var item in list.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                        if (int.TryParse(item, out int bn) && bn > 0) askedBars.Add(bn);
                        else throw Fail($"--bars wants bar numbers separated by commas, not {list}");
                    break;
                case "--todo-threshold":
                    string v = Value();
                    if (!double.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out threshold) || threshold < 0 || threshold > 1)
                        throw Fail($"--todo-threshold wants a number from 0 to 1, not {v}");
                    break;
                case "--progress": progress = Value() == "json"; break;
                default:
                    if (a.StartsWith("--")) throw Fail($"unknown option {a}");
                    if (lys != null) throw Fail("one .lys to verify");
                    lys = a;
                    break;
            }
        }
        if (lys == null) throw Fail("no .lys to verify");
        if (!File.Exists(lys)) throw Fail($"no file {lys}");
        lys = Path.GetFullPath(lys);
        string jsonPath = Path.ChangeExtension(lys, ".omr.json");
        if (!File.Exists(jsonPath)) throw Fail($"no {Path.GetFileName(jsonPath)} beside {Path.GetFileName(lys)}: verify checks a reading of this OMR");
        JsonObject root;
        try { root = JsonNode.Parse(File.ReadAllText(jsonPath))?.AsObject(); }
        catch (JsonException e) { throw Fail($"{Path.GetFileName(jsonPath)}: not JSON ({e.Message})"); }
        if (root == null || (int?)root["version"] != 1) throw Fail($"{Path.GetFileName(jsonPath)}: not a sidecar of version 1");
        if (root["verify"] is not JsonObject saved) throw Fail($"{Path.GetFileName(jsonPath)} keeps no page symbols (read by an older OMR): read the pages again");
        if (lysc != null && !File.Exists(lysc)) throw Fail($"no lysc at {lysc}");
        if (candidate != null && !File.Exists(candidate)) throw Fail($"no candidate .lys at {candidate}");
        string lyscPath = ReadCommand.FindLysc(lysc) ?? throw Fail("no lysc found (--lysc, beside the OMR, or on the PATH)");

        // (the pages as the side file names them: relative to its folder, or absolute)
        var images = root["pages"].AsArray().ToDictionary(p => (int)p["page"], p => Sidecar.Resolve((string)p["image"], Path.GetDirectoryName(lys)));
        var verify = Verify.Load(JsonDocument.Parse(saved.ToJsonString()).RootElement, images, out var parts);
        verify.LengthsFromLysc = true;
        Verify.LyscPath = lyscPath;
        var cfg = new ScoreConfig();
        foreach (var id in parts) cfg.AddPart(id, "treble", id);
        Emit(new { @event = "start", pages = images.Count });

        // each part-bar of the .lys as it stands (or of the candidate text): the reading's bars of the part, in order
        string text = File.ReadAllText(candidate ?? lys);
        var lineStarts = new List<int> { 0 };
        for (int i = 0; i < text.Length; i++) if (text[i] == '\n') lineStarts.Add(i + 1);
        int Line(int offset) { int k = lineStarts.BinarySearch(offset); return (k >= 0 ? k : ~k - 1) + 1; }
        var written = Bars(text);
        var spans = new Dictionary<(int Bar, int Part), (int Start, int End)>();
        var barLine = new Dictionary<(int Bar, int Part), int>();
        var measures = root["measures"].AsArray();
        for (int pi = 0; pi < parts.Count; pi++)
        {
            var numbers = measures.Where(m => (string)m["part"] == parts[pi]).Select(m => (int)m["measure"]).Distinct().Order().ToList();
            var bars = written.GetValueOrDefault(parts[pi]) ?? new();
            if (bars.Count != numbers.Count)
                Emit(new { @event = "warning", message = $"part {parts[pi]}: {bars.Count} bars in the .lys, {numbers.Count} read: compared in order" });
            for (int k = 0; k < Math.Min(bars.Count, numbers.Count); k++)
            {
                spans[(numbers[k], pi)] = (bars[k].Start, bars[k].End);
                barLine[(numbers[k], pi)] = Line(bars[k].At);
            }
        }

        // drawn in a work folder (lysc writes its SVG beside the .lys), the text as it is: the drawing's source offsets are its
        work = Path.Combine(Path.GetTempPath(), "omr-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        string copy = Path.Combine(work, "omr.lys");
        File.WriteAllText(copy, text);
        var report = new List<string>();
        var lines = verify.Compare(copy, spans, cfg, report);
        foreach (var l in lines.Concat(report)) Console.WriteLine(l);
        // (a .lys with errors, as one is halfway through an edit: x.omr.json is left as it was)
        if (!verify.Drawn)
            throw Fail(verify.LyscError != null ? $"the .lys has errors: {verify.LyscError}"
                : $"lysc drew nothing: {File.ReadAllText(Path.Combine(work, "verify-lysc.log")).Trim().Split('\n')[0]}");

        // the bars asked after: a line each part-bar the text has of them (and the bar's stems, part null), whether the
        // drawings differ there, how sure the place needs no correcting, and the check's words
        int asked = 0, differing = 0;
        if (askedBars != null)
        {
            var flaggedAt = verify.Places.ToDictionary(p => (p.Bar, p.Part));
            foreach (int bar in askedBars.Distinct().Order())
                foreach (int part in Enumerable.Range(0, parts.Count).Where(pi => spans.ContainsKey((bar, pi))).Append(-1))
                {
                    bool has = flaggedAt.TryGetValue((bar, part), out var place);
                    if (part < 0 && !has) continue;   // (the stems' line only where they were flagged)
                    asked++;
                    if (has) differing++;
                    Emit(new { @event = "bar", measure = bar, part = part >= 0 ? parts[part] : null, differs = has,
                               confidence = has ? place.Confidence : 1.0, memo = $"bar {bar}: {(has ? place.Notes : "(nothing to say)")}" });
                }
        }
        if (noWrite)
        {
            Emit(new { @event = "done", lys, candidate, bars = askedBars != null ? asked : (int?)null, differing = askedBars != null ? differing : (int?)null });
            return 0;
        }

        // the flagged bars on their pages, as the read command leaves them (base.omr-pNN.png)
        string dir = Path.GetDirectoryName(lys), name = Path.GetFileNameWithoutExtension(lys);
        foreach (var old in Directory.GetFiles(dir, name + ".omr-p*.png").Where(f => Regex.IsMatch(Path.GetFileName(f), @"\.omr-p\d+\.png$"))) File.Delete(old);
        var annotated = verify.SaveMarks(work).Order().Select(f =>
        {
            string to = Path.Combine(dir, name + ".omr-" + Path.GetFileName(f)["verify-".Length..]);
            File.Move(f, to, true);
            return to;
        }).ToList();

        // x.omr.json: the measures' lines, the todos
        List<int> LinesOf(int bar, string part) => barLine.Where(kv => kv.Key.Bar == bar && (part == null || parts[kv.Key.Part] == part)).Select(kv => kv.Value).Order().ToList();
        foreach (var m in measures)
        {
            int pi = parts.IndexOf((string)m["part"]);
            if (barLine.TryGetValue(((int)m["measure"], pi), out int l)) m["line"] = l;
        }
        var todos = new JsonArray();
        var keys = new HashSet<string>();
        var marked = new Dictionary<(int Bar, string Part), string>();   // a bar's todo of before whose @todo the .lys still carries: its key
        foreach (var t in (root["todos"]?.AsArray() ?? new JsonArray()).Select(t => t.DeepClone()))
        {
            string key = (string)t["key"];
            int at = MarkAt(text, key);
            // (the check's own are found again below, and the reading's word on a bar's length is lysc's now)
            bool checks = (string)t["source"] is "verify" or "length";
            // (one kept in the side file only, never in the .lys, stays as it was: its place is the reading's)
            if ((bool?)t["inSource"] == false && !checks) { todos.Add(t); keys.Add(key); continue; }
            if (at < 0) continue;
            // (another stays while its mark does)
            if (checks) { marked[((int)t["measure"], (string)t["part"])] = key; continue; }
            t["lines"] = (string)t["kind"] == "note" ? new JsonArray(Line(at)) : new JsonArray(LinesOf((int)t["measure"], (string)t["part"]).Select(l => (JsonNode)l).ToArray());
            todos.Add(t);
            keys.Add(key);
        }
        // a flag on a bar's stems or tuplet numbers joins its parts' flags there: one place to look at
        var flags = verify.Places.ToList();
        var places = flags.Where(f => f.Part >= 0).Select(f => (f.Bar, f.Part, f.Notes, f.Confidence)).ToList();
        foreach (var f in flags.Where(f => f.Part < 0))
        {
            int i = places.FindIndex(p => p.Bar == f.Bar);
            if (i >= 0) places[i] = places[i] with { Notes = places[i].Notes + "; " + f.Notes, Confidence = Math.Min(places[i].Confidence, f.Confidence) };
            else places.Add((f.Bar, f.Part, f.Notes, f.Confidence));
        }
        foreach (var (bar, part, notes, confidence) in places.OrderBy(p => p.Bar).ThenBy(p => p.Part))
        {
            string partId = part >= 0 ? parts[part] : null;
            // (under the key of the @todo the .lys has there, if any: the editor goes by it; a bar's length todo's is the bar's)
            string key = marked.GetValueOrDefault((bar, partId)) ?? marked.GetValueOrDefault((bar, null)) ?? Sidecar.BarKey(bar, partId);
            if (keys.Contains(key)) key += "-v";
            keys.Add(key);
            var b = verify.BoxOf(bar, part);
            todos.Add(new JsonObject
            {
                ["key"] = key, ["kind"] = "bar", ["source"] = "verify", ["measure"] = bar, ["part"] = partId,
                ["lines"] = new JsonArray(LinesOf(bar, partId).Select(l => (JsonNode)l).ToArray()),
                ["page"] = b?.Page, ["box"] = b == null ? null : new JsonArray(b.Value.Box.Select(v => (JsonNode)v).ToArray()),
                ["memo"] = notes, ["confidence"] = confidence, ["candidates"] = new JsonArray(),
            });
            // (one the .lys still marks stays in it, however sure)
            if (confidence >= threshold && MarkAt(text, key) < 0) todos[^1]["inSource"] = false;
        }
        root["todos"] = todos;
        // (written under a passing name, then renamed: an editor reading it meanwhile sees the old one whole)
        File.WriteAllText(jsonPath + ".part", root.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
        File.Move(jsonPath + ".part", jsonPath, true);
        int quiet = todos.Count(t => (bool?)t["inSource"] == false);
        Emit(new { @event = "done", lys, json = jsonPath, annotated, todos = todos.Count - quiet, quiet, bars = askedBars != null ? asked : (int?)null, differing = askedBars != null ? differing : (int?)null });
        return 0;
    }

    /// <summary>Where the .lys carries a todo's mark: @todo(key …), or the "// todo key:" comment of a bar with no note to carry it (-1: gone).</summary>
    static int MarkAt(string text, string key)
    {
        var m = Regex.Match(text, $@"@todo\(\s*{Regex.Escape(key)}(?=[\s)])|// todo {Regex.Escape(key)}:");
        return m.Success ? m.Index : -1;
    }

    /// <summary>
    /// The bars of each part's music in the .lys: in a section, a part's block split at its own
    /// barlines ('|'). A bar runs from after the barline before it to after its own; At is its first
    /// character that is neither a space nor a comment. Strings and comments are passed over.
    /// </summary>
    public static Dictionary<string, List<(int Start, int End, int At)>> Bars(string text)
    {
        var parts = new Dictionary<string, List<(int, int, int)>>();
        int depth = 0, sectionDepth = -1, partDepth = -1, start = 0, at = -1;
        string part = null, word = null, prevWord = null;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '/' && i + 1 < text.Length && text[i + 1] == '/') { while (i < text.Length && text[i] != '\n') i++; continue; }
            if (c == '/' && i + 1 < text.Length && text[i + 1] == '*') { int e = text.IndexOf("*/", i + 2, StringComparison.Ordinal); i = e < 0 ? text.Length : e + 1; continue; }
            if (char.IsWhiteSpace(c)) continue;
            if (part != null && depth >= partDepth && at < 0) at = i;
            if (c == '"')
            {
                for (i++; i < text.Length && text[i] != '"'; i++) if (text[i] == '\\') i++;
                word = prevWord = null;
                continue;
            }
            if (char.IsLetter(c) || c == '_')
            {
                int j = i;
                while (j < text.Length && (char.IsLetterOrDigit(text[j]) || text[j] == '_')) j++;
                prevWord = word; word = text[i..j];
                // (a break after a bar line ends its line: the next bar starts on the next)
                if (at == i && word is "break" or "pageBreak") at = -1;
                i = j - 1;
                continue;
            }
            if (c == '{')
            {
                depth++;
                if (sectionDepth < 0 && depth == 1 && prevWord == "section") sectionDepth = depth;
                else if (sectionDepth > 0 && part == null && depth == sectionDepth + 1 && word != null)
                {
                    part = word; partDepth = depth; start = i + 1; at = -1;
                    if (!parts.ContainsKey(part)) parts[part] = new();
                }
            }
            else if (c == '}')
            {
                if (part != null && depth == partDepth) { part = null; partDepth = -1; }
                else if (depth == sectionDepth) sectionDepth = -1;
                depth--;
            }
            else if (c == '|' && part != null && depth == partDepth)
            {
                parts[part].Add((start, i + 1, at < 0 ? i : at));
                start = i + 1; at = -1;
            }
            word = c == '{' ? null : word;
            if (c != '{') prevWord = null;
        }
        return parts;
    }

    sealed class Failed : Exception { public Failed(string m) : base(m) { } }

    static void Emit(object o, bool always = false)
    {
        if (!progress && !always) return;
        stdout.WriteLine(JsonSerializer.Serialize(o, new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
        stdout.Flush();
    }

    /// <summary>A bad input: the error line (whether or not progress was asked for: it is the answer), exit code 2.</summary>
    static Exception Fail(string message)
    {
        Emit(new { @event = "error", code = "input", message }, always: true);
        Console.Error.WriteLine("omr verify: " + message);
        return new Failed(message);
    }
}
