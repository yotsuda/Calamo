using System.Drawing;
using System.Drawing.Imaging;
using System.Text.RegularExpressions;

namespace Omr;

/// <summary>
/// The form's signs (S22): the repeat barlines (|: and :|), the volta endings' brackets, and the
/// segno and coda signs, each put to its bar, for Program to cut the sections at and to write the
/// form line from (form { ~A |: ~B [1. ~C] :| [2. ~D] ~E }). An ending's number is read from the
/// figures by its bracket's left hook (<see cref="ReadNumbers"/>: [1-2. ~C] :| [3. ~D]).
///
/// With the learnt detector: a repeat's dots (rep_open, rep_close) stand in a staff, in the measure
/// that begins at the |: or ends at the :|; a bracket's start (volta_l, the left hook's corner, the
/// number beside it) and end (volta_r, the right hook's) stand over the system's first staff at a
/// barline, as the segno and coda signs do (over the bar they open; a coda sign with "To" written
/// left of it is "to coda", after the bar before it). Without the detector, the rules' repeat dots
/// (Symbols.RepeatDots) on their side of the barline, and a bracket as one piece of ink over the
/// first staff, long and thin, hooked down at one or both ends.
/// </summary>
static class Form
{
    public static bool Verbose;

    /// <summary>One sign at one bar: Kind is rep_open, rep_close, volta_l, volta_r, segno, coda or tocoda.
    /// rep_open, volta_l, segno and coda are before Bar's music; rep_close, volta_r and tocoda after it.
    /// Number: an ending's number as the form spells it ("1", "1-2", "1-3", "1,3"), read from the figures
    /// by its left hook (<see cref="ReadNumbers"/>); null for a volta_l whose figures were not read.</summary>
    public sealed record Sign(string Kind, int Bar, int Page, double X, double Y, double Score, string Number = null);

    /// <summary>Whether a sign of this kind stands after its bar's music (a :|, an ending's end, a jump's or an end's words: Tempo.NavOf), not before it.</summary>
    public static bool After(string kind) => kind is "rep_close" or "volta_r" or "tocoda" or "fine" or "ds" or "dc" or "dsalcoda" or "dcalcoda" or "dsalfine" or "dcalfine";

    // the jumps' and ends' words as the form writes them, by the kind of their sign
    static readonly (string Kind, string Text)[] NavWords =
    {
        ("fine", "fine"), ("dsalcoda", "ds al coda"), ("dcalcoda", "dc al coda"), ("dsalfine", "ds al fine"), ("dcalfine", "dc al fine"), ("ds", "ds"), ("dc", "dc"),
    };

    /// <param name="ds">whether the words of a jump back to the segno ("D.S.", "D.S. al Coda", "D.S. al Fine") were read on this page or one before:
    /// a segno the detector is unsure of (over Detector.WeakSegno) is then taken where it saw none sure</param>
    public static List<Sign> Find(PageResult res, bool ds = false)
    {
        var signs = new List<Sign>();
        double s = res.S;
        var ink = res.Dark;
        var staves = res.Systems.SelectMany(x => x.Staves).ToList();
        // the system a sign over a first staff belongs to: the nearest first staff under it (within
        // twelve spaces), or the one it stands in
        SystemInfo Over(double x, double y)
        {
            SystemInfo best = null;
            double bd = double.MaxValue;
            foreach (var sys in res.Systems.Where(sys => sys.Staves.Count > 0 && sys.Measures > 0))
            {
                var st = sys.Staves[0];
                if (x < sys.Bars[0] - 2 * s || x > sys.Bars[^1] + 2 * s) continue;
                // (over the first staff: the system under the sign, not the one it hangs below. A lead
                // sheet's bracket stands over its chord diagrams and names, twenty spaces up: leadsheet-collide)
                double d = st.Top - y;
                if (d > -1 * s && d < 40 * s && d < bd) { bd = d; best = sys; }
            }
            return best;
        }
        // the measure of x in sys (-1: none), and the boundary nearest x (its index in Bars)
        static int MeasureOf(SystemInfo sys, double x) { for (int m = 0; m < sys.Measures; m++) if (x >= sys.Bars[m] && x < sys.Bars[m + 1]) return m; return -1; }
        static int BoundaryNear(SystemInfo sys, double x) { int k = 0; for (int i = 1; i < sys.Bars.Count; i++) if (Math.Abs(sys.Bars[i] - x) < Math.Abs(sys.Bars[k] - x)) k = i; return k; }
        void Add(string kind, int bar, double x, double y, double score)
        {
            if (bar < 0) return;
            if (signs.Any(g => g.Kind == kind && g.Bar == bar)) return;
            signs.Add(new Sign(kind, bar, res.Page, x, y, score));
            if (Verbose) Console.WriteLine($"  form: {kind} at {x:F0},{y:F0} ({score:F2}) -> bar {bar}");
        }
        // the dots of a repeat barline: the measure they stand in (a |: opens it, a :| closes it)
        void Dots(string kind, double x, double y, double score)
        {
            var st = Symbols.Nearest(staves, y, s, 3);
            if (st == null) return;
            var sys = res.Systems[st.System];
            int m = MeasureOf(sys, x);
            if (m < 0) return;
            // (the dots lie within two spaces of their barline: nothing else is a repeat. A |: after
            // a system's clef and key is its first measure's, the line before it dropped: Reader)
            double bar = kind == "rep_open" ? sys.Bars[m] : sys.Bars[m + 1];
            if (Math.Abs(x - bar) > 2.5 * s && !(kind == "rep_open" && m == 0)) return;
            Add(kind, kind == "rep_open" ? sys.BarOf(m) : sys.BarOf(m + 1) - 1, x, y, score);
        }
        // a sign over the system at a barline: the bar after the boundary (before: the bar before it)
        void AtBar(string kind, double x, double y, double score, bool after)
        {
            var sys = Over(x, y);
            if (sys == null) return;
            int k = BoundaryNear(sys, x);
            // (a bracket's hook well inside a measure is nobody's: within two spaces of a barline. The
            // segno and coda signs are drawn centred past the line, within three spaces; one further
            // in stands at its bar's first note, past the clef and key at a system's start, or past a
            // rehearsal mark's box set at the barline (roadsong's "To Coda" six spaces short of the
            // line, before the "Bridge" box): a sign in a measure's first half opens that bar, one in
            // its second half ends it ("to coda") or opens the next)
            double tol = (kind is "segno" or "coda" or "tocoda" ? 3 : 2) * s;
            if (Math.Abs(sys.Bars[k] - x) > tol)
            {
                if (!after && x < sys.Bars[1]) k = 0;
                else if (kind is not ("segno" or "coda" or "tocoda")) return;
                else
                {
                    int m = MeasureOf(sys, x);
                    if (m < 0) return;
                    bool firstHalf = x < (sys.Bars[m] + sys.Bars[m + 1]) / 2;
                    k = after ? m + 1 : firstHalf ? m : m + 1;
                    if (Verbose) Console.WriteLine($"  form: {kind} at {x:F0} is {Math.Abs(sys.Bars[BoundaryNear(sys, x)] - x) / s:F1} spaces from a barline: in measure {m + 1}'s {(firstHalf ? "first" : "second")} half -> boundary {k}");
                }
            }
            if (after) { if (k == 0) return; Add(kind, sys.BarOf(k) - 1, x, y, score); }
            else { if (k == sys.Bars.Count - 1) return; Add(kind, sys.BarOf(k), x, y, score); }
        }
        bool detector = res.Sym.Detections != null;
        if (detector)
        {
            foreach (var t in res.Sym.Detections.Where(t => t.Score >= Detector.Threshold).OrderByDescending(t => t.Score))
                switch (t.Class)
                {
                    case "rep_open": case "rep_close": Dots(t.Class, t.X, t.Y, t.Score); break;
                    case "volta_l": case "segno": AtBar(t.Class, t.X, t.Y, t.Score, after: false); break;
                    case "volta_r": AtBar(t.Class, t.X, t.Y, t.Score, after: true); break;
                    case "coda":
                        // "To" left of the sign (a word two spaces wide, a space off the sign): ink in
                        // the rows of the sign's middle, between one and four spaces left of it
                        int cols = 0;
                        for (int xx = (int)(t.X - 4 * s); xx < t.X - 1.2 * s; xx++)
                        {
                            if (xx < 0 || xx >= ink.W) continue;
                            bool any = false;
                            for (int yy = (int)(t.Y - 0.8 * s); yy <= t.Y + 0.8 * s && !any; yy++) any = yy >= 0 && yy < ink.H && ink[xx, yy];
                            if (any) cols++;
                        }
                        bool to = cols >= 1.2 * s;
                        if (Verbose) Console.WriteLine($"  form: coda sign at {t.X:F0},{t.Y:F0}: {cols} inked columns left of it -> {(to ? "to coda" : "coda")}");
                        AtBar(to ? "tocoda" : "coda", t.X, t.Y, t.Score, after: to);
                        break;
                }
            // (a "D.S." read with no segno seen sure: the surest segno the detector was unsure of,
            // over Detector.WeakSegno -- roadsong's, at a system's start by the clef, seen at 0.8 by
            // one training of the detector and under the threshold by the next)
            if (ds && !signs.Any(g => g.Kind == "segno"))
                foreach (var t in res.Sym.WeakSegnos.Where(t => t.Score > Detector.WeakSegno).OrderByDescending(t => t.Score))
                {
                    if (Verbose) Console.WriteLine($"  form: a D.S. was read and no segno seen sure: the detector's unsure segno at {t.X:F0},{t.Y:F0} ({t.Score:F2}) taken");
                    AtBar("segno", t.X, t.Y, t.Score, after: false);
                    if (signs.Any(g => g.Kind == "segno")) break;
                }
        }
        // the rules: the repeat dots found by their barline (Symbols.RepeatDots), one pair a staff
        foreach (var o in res.Sym.Others.Where(o => o.Class == "repeat_dot" && !detector))
        {
            var sys = res.Systems[o.St.System];
            int k = BoundaryNear(sys, o.C.Cx);
            bool near = Math.Abs(sys.Bars[k] - o.C.Cx) <= 2.5 * s;
            // (a |: after a system's clef and key: its line was dropped with the measure of signatures before it, Reader)
            if (!near && !(sys.Measures > 0 && o.C.Cx < sys.Bars[1])) continue;
            Dots(!near || o.C.Cx > sys.Bars[k] ? "rep_open" : "rep_close", o.C.Cx, o.St.Mid, 1);
        }
        // and a volta bracket: one piece of ink over a system's first staff, six spaces or longer, under
        // three spaces tall, thin (little ink in its box), hooked down at its left end and/or its right
        foreach (var o in res.Sym.Others.Where(o => o.C.W >= 6 * s && o.C.H >= 0.8 * s && o.C.H <= 3 * s && o.C.Fill < 0.3))
        {
            var sys = Over(o.C.Cx, o.C.MinY);
            if (sys == null || o.C.MinY > sys.Staves[0].Top - 1.5 * s) continue;
            // a hook: ink down from the rule in the end's columns for most of the piece's height
            bool Hook(int x0)
            {
                int rows = 0;
                for (int yy = o.C.MinY; yy <= o.C.MaxY; yy++)
                    for (int xx = Math.Max(0, x0 - (int)(0.15 * s)); xx <= Math.Min(ink.W - 1, x0 + 0.15 * s); xx++) if (ink[xx, yy]) { rows++; break; }
                return rows >= 0.7 * o.C.H;
            }
            bool left = Hook(o.C.MinX), right = Hook(o.C.MaxX);
            // (with the detector, only a bracket it saw one end of: it misses the start of a second
            // ending right after the first's end, the two hooks a space apart (chords006, piano002),
            // while a piece of ink the rules take for a bracket on a speckled scan is no ending)
            bool seen = !detector || signs.Any(g => g.Kind.StartsWith("volta_") && Math.Abs(g.Y - o.C.MinY) < 2 * s
                                                     && (Math.Abs(g.X - o.C.MinX) < 2 * s || Math.Abs(g.X - o.C.MaxX) < 2 * s));
            if (Verbose) Console.WriteLine($"  form: a bracket? at {o.C.MinX}-{o.C.MaxX} y {o.C.MinY} ({o.C.W / s:F1} x {o.C.H / s:F1} spaces): hooks {(left ? "left" : "")} {(right ? "right" : "")}{(seen ? "" : ", no end the detector saw")}");
            if (!seen) continue;
            if (left) AtBar("volta_l", o.C.MinX, o.C.MinY, 1, after: false);
            if (right) AtBar("volta_r", o.C.MaxX, o.C.MinY, 1, after: true);
        }
        return signs;
    }

    /// <summary>
    /// The endings' numbers (S22, third step): the bold figures Lily# sets inside a bracket right of
    /// its left hook ("1.", "1. 2." for [1-2., "1.–3." for [1-3., "1. 3." for [1,3.: Lily#'s
    /// SyntaxFacts.VoltaPrintedText, as LilyPond prints them), read by Windows' OCR from the page as
    /// loaded (<paramref name="path"/>) and kept on the volta_l signs as the form spells them
    /// (<see cref="Spelling"/>: 1, 1-2, 1-3, 1,3). The figures' ink: the pieces under the rule and
    /// right of the hook, a digit's height (three fifths of a space to two) and not touching the
    /// window's edges (the rule, the hook, the music under), the first within two and a half spaces
    /// of the hook, the rest in a row on its baseline with no gap over two spaces and a half; the
    /// points and a dash among them go with them. The engine passes over one short word by itself
    /// ("1." alone reads as nothing at any scale): the cut is set three times in a row, three ems
    /// apart, and the reading two of the three agree on is taken (<see cref="Thrice"/>).
    /// </summary>
    public static void ReadNumbers(string path, PageResult res, List<Sign> signs)
    {
        if (!signs.Any(g => g.Kind == "volta_l")) return;
        double s = res.S;
        var ink = res.Dark;
        using var page = new Bitmap(path);
        for (int i = 0; i < signs.Count; i++)
        {
            var g = signs[i];
            if (g.Kind != "volta_l") continue;
            int rx = Math.Max(0, (int)(g.X + 0.5 * s)), ry = Math.Max(0, (int)(g.Y + 0.2 * s));
            int rw = Math.Min((int)(10 * s), ink.W - rx), rh = Math.Min((int)(2.8 * s), ink.H - ry);
            if (rw < s || rh < s) continue;
            var (_, comps) = Components.Label(rw, rh, k => ink[rx + k % rw, ry + k / rw], true);
            var digits = comps.Skip(1).Where(c => c.H >= 0.6 * s && c.H <= 2.0 * s && c.W <= 1.6 * s && c.Fill > 0.2
                                                  && c.MinX > 0 && c.MinY > 0 && c.MaxX < rw - 1 && c.MaxY < rh - 1)
                              .OrderBy(c => c.MinX).ToList();
            if (digits.Count == 0 || digits[0].MinX > 2.5 * s)
            {
                if (Verbose) Console.WriteLine($"  form: ending's number by the hook at {g.X:F0},{g.Y:F0}: no figures");
                continue;
            }
            var row = new List<Comp> { digits[0] };
            // (the next figure within two spaces and a half: "1.–3." has a point, a dash and their
            // spaces between the 1 and the 3, a space and three quarters)
            foreach (var c in digits.Skip(1))
            {
                if (c.MinX - row[^1].MaxX > 2.5 * s) break;
                if (Math.Abs(c.Cy - row[0].Cy) <= 0.5 * s) row.Add(c);
            }
            int x0 = row.Min(c => c.MinX), x1 = row.Max(c => c.MaxX), y0 = row.Min(c => c.MinY), y1 = row.Max(c => c.MaxY);
            foreach (var c in comps.Skip(1).Where(c => c.MinX >= x0 - 0.2 * s && c.MaxX <= x1 + 1.0 * s && c.MinY >= y0 - 0.3 * s && c.MaxY <= y1 + 0.3 * s))
            { x0 = Math.Min(x0, c.MinX); x1 = Math.Max(x1, c.MaxX); y0 = Math.Min(y0, c.MinY); y1 = Math.Max(y1, c.MaxY); }
            var a = ink.ToSource(rx + x0, ry + y0);
            var b = ink.ToSource(rx + x1 + 1, ry + y1 + 1);
            var box = RectangleF.FromLTRB((float)Math.Min(a.X, b.X), (float)Math.Min(a.Y, b.Y), (float)Math.Max(a.X, b.X), (float)Math.Max(a.Y, b.Y));
            string number = Thrice(page, box);
            if (Verbose) Console.WriteLine($"  form: ending's number by the hook at {g.X:F0},{g.Y:F0}: figures at {box.X:F0},{box.Y:F0} {box.Width:F0}x{box.Height:F0} -> {number ?? "unread"}");
            if (number != null) signs[i] = g with { Number = number };
        }
    }

    /// <summary>
    /// The figures in <paramref name="box"/> (the page as loaded) read three times in a row: the cut,
    /// with half an em round it, set three times three ems apart on white, read at the page's scale
    /// and at twice and one and a half times it, each tile's line spelt (<see cref="Spelling"/>); the
    /// spelling two tiles agree on, or null.
    /// </summary>
    static string Thrice(Bitmap page, RectangleF box)
    {
        // (the figures a space and a half apart: the engine takes the three for one line of words --
        // further apart, for three lone words, and reads none; Header.Recognize parts the line
        // between words over an em and a fifth apart)
        int em = (int)Math.Ceiling(box.Height), pad = (int)Math.Ceiling(0.25 * box.Height), gap = 0;
        var inner = Rectangle.FromLTRB((int)Math.Floor(box.Left - 0.5 * em), (int)Math.Floor(box.Top - 0.35 * em), (int)Math.Ceiling(box.Right + 0.5 * em), (int)Math.Ceiling(box.Bottom + 0.35 * em));
        inner.Intersect(new Rectangle(0, 0, page.Width, page.Height));
        if (inner.Width < 4 || inner.Height < 4) return null;
        int tw = inner.Width + 2 * pad, th = inner.Height + 2 * pad;
        using var bmp = new Bitmap(3 * tw + 2 * gap, th, PixelFormat.Format24bppRgb);
        using (var gr = Graphics.FromImage(bmp))
        {
            gr.Clear(Color.White);
            for (int t = 0; t < 3; t++) gr.DrawImage(page, new Rectangle(t * (tw + gap) + pad, pad, inner.Width, inner.Height), inner, GraphicsUnit.Pixel);
        }
        foreach (double k in new[] { 1.0, 2.0, 1.5 })
        {
            var lines = Header.Recognize(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height), k, false);
            var read = lines.Select(l => Spelling(l.Text)).Where(x => x != null).ToList();
            // (the three read as one line, "1. 2. 1. 2. 1. 2.": its words in three groups)
            if (read.Count == 0 && lines.Count == 1)
            {
                var words = lines[0].Text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (words.Length % 3 == 0)
                    read = Enumerable.Range(0, 3).Select(t => Spelling(string.Join(" ", words.Skip(t * words.Length / 3).Take(words.Length / 3)))).Where(x => x != null).ToList();
            }
            if (Verbose) Console.WriteLine($"  form: figures thrice at {k:F1}: {string.Join(" / ", lines.Select(l => $"\"{l.Text}\""))} -> {string.Join(" ", read)}");
            var best = read.GroupBy(x => x).OrderByDescending(x => x.Count()).FirstOrDefault();
            if (best != null && best.Count() >= 2) return best.Key;
        }
        return null;
    }

    /// <summary>
    /// An ending's number as the form spells it, from the OCR's reading of its figures: "1." → 1,
    /// "1.2." or "1. 2." → 1-2, "1.–3." → 1-3, "1. 3." → 1,3 (a run of consecutive passes as
    /// first-last, the runs joined by commas; a dash between two figures spans them). The figures
    /// rising, one to thirty, six at most; an l, an I or a bar for a 1 and an o for a 0. Null for
    /// anything else.
    /// </summary>
    internal static string Spelling(string text)
    {
        string t = text.Replace('l', '1').Replace('I', '1').Replace('|', '1').Replace('O', '0').Replace('o', '0');
        var ms = Regex.Matches(t, @"\d+");
        if (ms.Count == 0 || ms.Count > 6) return null;
        var nums = new List<int>();
        for (int i = 0; i < ms.Count; i++)
        {
            if (!int.TryParse(ms[i].Value, out int n) || n < 1 || n > 30) return null;
            if (i > 0 && n <= nums[^1]) return null;
            if (i > 0 && Regex.IsMatch(t[(ms[i - 1].Index + ms[i - 1].Length)..ms[i].Index], "[-–—~]"))
                for (int m = nums[^1] + 1; m < n; m++) nums.Add(m);
            nums.Add(n);
        }
        var runs = new List<string>();
        for (int i = 0; i < nums.Count;)
        {
            int j = i;
            while (j + 1 < nums.Count && nums[j + 1] == nums[j] + 1) j++;
            runs.Add(j > i ? $"{nums[i]}-{nums[j]}" : nums[i].ToString());
            i = j + 1;
        }
        return string.Join(",", runs);
    }

    /// <summary>The last pass an ending's spelling names ("1-2" → 2, "1,3" → 3).</summary>
    static int LastPass(string number) => int.Parse(Regex.Matches(number, @"\d+")[^1].Value);

    /// <summary>
    /// The signs of the whole piece completed by what the form's structure says: a first ending
    /// closed by a :| at bar b, and an ending's end seen after it with no ending's start between,
    /// is a second ending the detector missed the start of (its hook at a system's start by the
    /// clef, or a space from the first ending's end: the degraded chords006), beginning at b + 1.
    /// </summary>
    public static void Complete(List<Sign> signs)
    {
        foreach (var close in signs.Where(g => g.Kind == "rep_close").ToList())
        {
            bool first = signs.Any(g => g.Kind == "volta_l" && g.Bar <= close.Bar) && signs.Any(g => g.Kind == "volta_r" && g.Bar == close.Bar);
            var end = signs.Where(g => g.Kind == "volta_r" && g.Bar > close.Bar).OrderBy(g => g.Bar).FirstOrDefault();
            if (!first || end == null || signs.Any(g => g.Kind == "volta_l" && g.Bar > close.Bar && g.Bar <= end.Bar)) continue;
            if (Verbose) Console.WriteLine($"  form: a second ending's start missed: the first closes at bar {close.Bar}, an ending ends at bar {end.Bar} -> volta_l at bar {close.Bar + 1}");
            signs.Add(new Sign("volta_l", close.Bar + 1, end.Page, end.X, end.Y, end.Score));
        }
    }

    /// <summary>
    /// The form line from the sections (each from its first bar) and the signs: the repeat and its
    /// endings closed where the signs say, or where they must be (a |: that no :| closes is an error
    /// to Lily#: closed at the last ending's end, or at the piece's end; an ending left open ends
    /// before the next cut). The jumps' and ends' words the OCR read ("Fine", "D.C. al Fine", "D.S. al
    /// Coda": Tempo.NavOf) are written after their bar's section; with none read, a segno, "to coda"
    /// and a coda sign together are a D.S. al Coda, written after the section before the coda.
    /// </summary>
    public static string Line(List<(string Name, string Label, int From, bool Shown)> sections, List<Sign> signs, int lastBar)
    {
        bool Has(string kind, int bar) => signs.Any(g => g.Kind == kind && g.Bar == bar);
        var sb = new System.Text.StringBuilder("form {");
        bool open = false;
        int endings = 0;
        bool ds = signs.Any(g => g.Kind == "segno") && signs.Any(g => g.Kind == "tocoda") && signs.Any(g => g.Kind == "coda")
                  && !signs.Any(g => g.Kind is "dsalcoda" or "dcalcoda" or "dsalfine" or "dcalfine");
        for (int si = 0; si < sections.Count; si++)
        {
            int from = sections[si].From, to = si + 1 < sections.Count ? sections[si + 1].From - 1 : lastBar;
            if (Has("segno", from)) sb.Append(" segno");
            if (Has("coda", from)) sb.Append(" coda");
            if (Has("rep_open", from))
            {
                // (a repeat opened while one is open: the one before closes here, as a :|: divides)
                if (open) sb.Append(" :|");
                sb.Append(" |:"); open = true; endings = 0;
            }
            // (an ending is one section: the cuts come from the brackets' ends. A second ending while
            // the repeat is still open closes it first, the :| unseen; one after the :| goes on with it)
            // (its number as read by its hook, when it follows the endings before it; else the next pass)
            var ending = signs.FirstOrDefault(g => g.Kind == "volta_l" && g.Bar == from);
            if (ending != null)
            {
                if (open && endings > 0) { sb.Append(" :|"); open = false; }
                string number = ending.Number != null && int.Parse(Regex.Match(ending.Number, @"\d+").Value) > endings ? ending.Number : (endings + 1).ToString();
                sb.Append($" [{number}.");
                endings = LastPass(number);
            }
            sb.Append(' ').Append(sections[si].Shown ? "" : "~").Append(sections[si].Name);
            if (ending != null) sb.Append(']');
            if (Has("rep_close", to) || open && si + 1 == sections.Count) { sb.Append(" :|"); open = false; }
            if (Has("tocoda", to)) sb.Append(" to coda");
            foreach (var (kind, text) in NavWords) if (Has(kind, to)) sb.Append(' ').Append(text);
            if (ds && si + 1 < sections.Count && Has("coda", sections[si + 1].From)) sb.Append(" ds al coda");
        }
        sb.Append(" }");
        return sb.ToString();
    }

    /// <summary>
    /// The sections (by index) a span may not be carried into from the one before: where the form
    /// line has a sign between the two (a repeat's |: or :|, an ending's [n. or ], a segno, a coda,
    /// "to coda", a jump's words), what is played before the section differs from pass to pass, and
    /// lysc refuses a slur, hairpin, pedal or octave line across it (LYS4023, an error).
    /// </summary>
    public static HashSet<int> RepeatCuts(string line, List<(string Name, string Label, int From, bool Shown)> sections)
    {
        var cuts = new HashSet<int>();
        int open = line.IndexOf('{'), close = line.LastIndexOf('}');
        if (open < 0 || close < open) return cuts;
        int si = -1;
        bool between = false;
        // (a sign before the first section -- a segno or |: at the start -- has no section before it to carry from)
        foreach (var t in line[(open + 1)..close].Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            // (an ending's section is written "~C]", its bracket closing on it)
            string name = t.TrimStart('~').TrimEnd(']');
            if (si + 1 < sections.Count && name == sections[si + 1].Name)
            {
                if (si >= 0 && between) cuts.Add(si + 1);
                si++; between = t.EndsWith(']');
            }
            else if (si >= 0) between = true;
        }
        return cuts;
    }
}
