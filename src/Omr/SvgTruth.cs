using System.Globalization;
using System.Text.RegularExpressions;

namespace Omr;

/// <summary>A music-font glyph as Lily# placed it, in page-image pixels.</summary>
sealed class TGlyph
{
    public string Name;          // feta name: noteheads.s2, accidentals.flat, clefs.C, 4, ...
    public double X, Y;          // the glyph's origin
    public double L, T, R, B;    // its box
    public double Size;          // font size in staff spaces x 4
    public int Src = -1;         // data-pos: where in the .lys source it comes from (-1: none)
    // full-sized music, not a grace note's (0.71), a tuplet number's (0.79) or a tempo mark's
    // note (0.89); a head scaled by NoteHead.scale 0.92 (OmrGen --vary's least) still is
    public bool Full => Size > 0.905;
    // a grace note's: its head and flag at seven tenths (0.71), its accidental at 0.63 (S21). A tuplet
    // number is text, not a glyph; a tempo mark's note (0.89) is neither full nor small
    public bool Small => Size > 0.55 && Size < 0.8;
    public double Cx => (L + R) / 2;
    public double Cy => (T + B) / 2;
}

/// <summary>What Lily# drew on one page, read back from its SVG: the symbol-level truth.</summary>
sealed class SvgPage
{
    public double Ppu;           // image pixels per SVG unit (one staff space)
    public string MusicFont;     // the music font Lily# drew the page in (Emmentaler, Bravura, ...)
    public List<TGlyph> Glyphs = new();
    public List<(double X1, double Y1, double X2, double Y2, double W)> Lines = new();
    public List<(double X, double Y, double W, double H, int Src)> Rects = new();
    public List<List<(double X, double Y)>> Polys = new();
    // ties and slurs: a closed curve from one end to the other and back; the ends, and the source
    // position of the '~' or '(' that makes it (a chord's ties share the chord's; a slur broken at
    // a system's end is two curves with one). Told apart by that character in the .lys beside the
    // SVG; with no .lys every curve is taken for a tie
    public List<(double X1, double Y1, double X2, double Y2, int Src)> Ties = new();
    // (a slur's EndSrc: where its ')' is, -1 when there is none; the notes it spans lie between)
    public List<(double X1, double Y1, double X2, double Y2, int Src, int EndSrc)> Slurs = new();
    // tuplet numbers: small italic text (not a music glyph), centred at X, Y
    public List<(double X, double Y, string Text, int Src)> Tuplets = new();
    // percent repeats (B3): a slanted bar or two (%%: a two-bar repeat) between two dots, centred at
    // X, Y; the source position is the "repeat" that makes it (every % of one repeat shares it), or the % mark's
    public List<(double X, double Y, int Slashes, int Src)> Percents = new();
    // TAB fret numbers (B3): bold upright text on a TAB staff, one entry per digit, its box in pixels
    public List<(double L, double T, double R, double B, char Digit, int Src)> TabDigits = new();
    // dynamics (M3): one entry per letter (p m f r s z n), its box in pixels; the source position is
    // the '@' of its mark (@mf). Emmentaler's are set as bold italic text, a SMuFL font's as its glyphs
    public List<(double L, double T, double R, double B, char Letter, int Src)> Dynamics = new();
    // points of the marks drawn as lines (M3): a hairpin's tip and the middle of its open end
    // (hairpin.tip, hairpin.open), a pedal bracket's start and end hooks and its notch where the
    // pedal changes (ped.l, ped.r, ped.notch); the source position is the mark's '@'
    public List<(double X, double Y, string Kind, int Src)> Marks = new();
    // octave lines (M3, S18): the sign's letters' box and what they say (8va, 8vb, 15ma, 15mb; "(8va)"
    // where a line goes on from a system's start is 8va); the source position is the mark's '@'. The
    // hook where its dashes end is among Marks as ott.end
    public List<(double L, double T, double R, double B, string Text, int Src)> Ottavas = new();
    // rehearsal marks: the words in a box over a system (a section's name in the form, "section A"),
    // the box in pixels; the source position is the section's name's
    public List<(double L, double T, double R, double B, string Text, int Src)> Rehearsals = new();
    // (an Emmentaler dynamic letter at text font size 1, in staff spaces: its width as the PNG inks
    // it, and its top and bottom from the baseline, y down; measured on a render's boxes)
    static readonly Dictionary<char, (double W, double Top, double Bottom)> dynamicLetter = new()
    {
        ['p'] = (0.71, -0.48, 0.205), ['m'] = (0.875, -0.48, 0), ['f'] = (0.6, -0.735, 0.205), ['r'] = (0.475, -0.48, 0),
        ['s'] = (0.425, -0.48, 0), ['z'] = (0.475, -0.48, 0), ['n'] = (0.65, -0.48, 0),
    };
    // (a TAB digit's size in staff spaces at Lily#'s font size 3: measured on a render)
    const double TabDigitWidth = 1.52, TabDigitHeight = 2.13;
    // (today's plain digits, in ems of their font size: the advance from a two-digit fret's x's, the height as above)
    const double TabDigitAdvance = 0.45, TabDigitEmHeight = 0.71;

    // a music font's glyphs by code point: Emmentaler's own (emmentaler-bbox.tsv), a SMuFL font's by
    // SMuFL code point under the same feta names (smufl-bbox-<font>.tsv, tools/smufl-tables.ps1)
    static readonly Dictionary<string, Dictionary<string, (string Name, double X0, double Y0, double X1, double Y1)>> fonts = new();
    // time-signature digits: the table has their advance only; they stand 2 spaces tall on the baseline
    static readonly double[] digitAdvance = { 1.464, 1.268, 1.464, 1.332, 1.600, 1.348, 1.356, 1.348, 1.464, 1.356 };

    static Dictionary<string, (string Name, double X0, double Y0, double X1, double Y1)> Boxes(string font)
    {
        lock (fonts)
        {
            if (fonts.TryGetValue(font, out var boxes)) return boxes;
            boxes = new();
            bool emmentaler = font == "Emmentaler";
            string path = Path.Combine(AppContext.BaseDirectory, emmentaler ? "emmentaler-bbox.tsv" : $"smufl-bbox-{font}.tsv");
            if (!File.Exists(path)) throw new FileNotFoundException($"no glyph table for the music font {font}", path);
            foreach (var line in File.ReadAllLines(path))
            {
                if (line.StartsWith('#') || line.Length == 0) continue;
                var p = line.Split('\t');
                double D(int i) => double.Parse(p[i], CultureInfo.InvariantCulture);
                boxes[p[0]] = (p[1], D(2), D(3), D(4), D(5));
            }
            if (emmentaler)
                for (int d = 0; d <= 9; d++) boxes[$"{0x30 + d:X4}"] = (d.ToString(), 0, 0, digitAdvance[d], 2);
            fonts[font] = boxes;
            return boxes;
        }
    }

    static readonly Regex tag = new(@"<(/?)(\w+)([^>]*?)(/?)>", RegexOptions.Compiled);
    static readonly Regex attr = new(@"([\w-]+)=""([^""]*)""", RegexOptions.Compiled);
    static double N(string v) => double.Parse(v, CultureInfo.InvariantCulture);

    /// <summary>
    /// What Lily# drew. <paramref name="pageNo"/> &gt; 0 takes only that page of a drawing whose pages
    /// are stacked in one file (lysc svg writes a several-page score so: a group of class "page" a
    /// page, one under the other), placed as on its own page image; 0 takes the whole file.
    /// </summary>
    public static SvgPage Load(string path, int imageWidth, int pageNo = 0)
    {
        string s = File.ReadAllText(path);
        // the page's music font (.music in the style); a glyph the font lacks names its own first
        // (font-family="Emmentaler, Leland, serif")
        var mf = Regex.Match(s, @"\.music\s*\{\s*font-family:\s*'([^']+)'");
        string pageFont = mf.Success ? mf.Groups[1].Value : "Emmentaler";
        var pageBoxes = Boxes(pageFont);
        var page = new SvgPage { MusicFont = pageFont };
        string lysPath = Regex.Replace(Path.ChangeExtension(path, ".lys"), @"-page\d+\.lys$", ".lys");
        string lys = File.Exists(lysPath) ? File.ReadAllText(lysPath) : null;
        var stack = new Stack<(double Tx, double Ty, double Sx, double Sy, bool Skip)>();
        var tr = (Tx: 0.0, Ty: 0.0, Sx: 1.0, Sy: 1.0);
        bool skip = false;   // inside another page's group
        int pagesSeen = 0;
        var percentBars = new List<(List<(double X, double Y)> Pts, int Src)>();
        // (a hairpin's and a pedal bracket's lines, by the mark that draws them: a hairpin or a pedal)
        var markLines = new List<(double X1, double Y1, double X2, double Y2, bool Pedal, int Src)>();
        // (an octave line's sign and its lines: the dashes and the hook at their end)
        var ottTexts = new List<(double X, double Y, double Em, string Text, int Src)>();
        var ottLines = new List<(double X1, double Y1, double X2, double Y2, bool Dashed, int Src)>();
        // (a rehearsal mark's box: a white rectangle with a black stroke, drawn before its words, at the same source position)
        var boxed = new List<(double L, double T, double R, double B, int Src)>();
        // (an acciaccatura's slash: a short slanted stroke with no source, through the grace note's stem or beam)
        var strokes = new List<(double X, double Y)>();
        // (a repeat barline's dots: circles at the form's |: or :|; and a volta bracket's lines, at the form's '[')
        var repDots = new List<(double X, double Y, int Src)>();
        var voltaLines = new List<(double X1, double Y1, double X2, double Y2, int Src)>();
        bool FormSign(int src, params string[] signs) => lys != null && src >= 0 && signs.Any(sg => string.CompareOrdinal(lys, src, sg, 0, sg.Length) == 0);
        // the mark a source position starts: "@cresc", "@!sustain", or "" (not a mark's '@')
        string MarkAt(int src) => lys != null && src >= 0 && src < lys.Length && lys[src] == '@'
            ? Regex.Match(lys.Substring(src, Math.Min(24, lys.Length - src)), @"^@!?[A-Za-z]+").Value : "";
        foreach (Match m in tag.Matches(s))
        {
            string name = m.Groups[2].Value;
            bool close = m.Groups[1].Value == "/", selfClose = m.Groups[4].Value == "/";
            if (close)
            {
                if (name == "g" && stack.Count > 0) { var top = stack.Pop(); tr = (top.Tx, top.Ty, top.Sx, top.Sy); skip = top.Skip; }
                continue;
            }
            var a = new Dictionary<string, string>();
            foreach (Match am in attr.Matches(m.Groups[3].Value)) a[am.Groups[1].Value] = am.Groups[2].Value;
            // (lysc 0.12.0 sets a glyph's own font in its style, style="font-family: 'Emmentaler-23',
            // 'Emmentaler', serif", where the earlier ones wrote font-family="Emmentaler-23, Emmentaler, serif")
            if (!a.ContainsKey("font-family") && a.TryGetValue("style", out var st))
            {
                var sf = Regex.Match(st, @"font-family:\s*([^;]+)");
                if (sf.Success) a["font-family"] = sf.Groups[1].Value.Trim();
            }
            if (name == "g" && !selfClose && pageNo > 0 && a.GetValueOrDefault("class") == "page")
            {
                // a page of the stack: the one asked for stands at the top, the others are left out
                stack.Push((tr.Tx, tr.Ty, tr.Sx, tr.Sy, skip));
                skip = ++pagesSeen != pageNo;
                continue;
            }
            if (skip && name != "g") continue;
            (double X, double Y) P(double x, double y) => ((tr.Tx + tr.Sx * x) * page.Ppu, (tr.Ty + tr.Sy * y) * page.Ppu);
            int Src() => a.TryGetValue("data-pos", out var v) ? int.Parse(v) : -1;
            switch (name)
            {
                case "svg":
                    var vb = a["viewBox"].Split(' ');
                    page.Ppu = imageWidth / N(vb[2]);
                    break;
                case "g":
                    if (selfClose) break;
                    stack.Push((tr.Tx, tr.Ty, tr.Sx, tr.Sy, skip));
                    if (a.TryGetValue("transform", out var t))
                    {
                        // (a page group of a several-page drawing writes "translate(0, 169.01)", with a space)
                        var mt = Regex.Match(t, @"translate\(([-\d.]+),\s*([-\d.]+)\)");
                        var ms = Regex.Match(t, @"scale\(([-\d.]+),\s*([-\d.]+)\)");
                        double tx = mt.Success ? N(mt.Groups[1].Value) : 0, ty = mt.Success ? N(mt.Groups[2].Value) : 0;
                        double sx = ms.Success ? N(ms.Groups[1].Value) : 1, sy = ms.Success ? N(ms.Groups[2].Value) : 1;
                        tr = (tr.Tx + tr.Sx * tx, tr.Ty + tr.Sy * ty, tr.Sx * sx, tr.Sy * sy);
                    }
                    break;
                case "line":
                {
                    var p1 = P(N(a["x1"]), N(a["y1"]));
                    var p2 = P(N(a["x2"]), N(a["y2"]));
                    // (a volta bracket's rule and hooks, at the form's '[': no staff line; kept for the marks)
                    if (FormSign(Src(), "[")) { voltaLines.Add((p1.X, p1.Y, p2.X, p2.Y, Src())); break; }
                    // (a pedal bracket's long level line is no staff line: kept out of Lines)
                    string mark = MarkAt(Src());
                    // (an octave line's dashes and its end hook: no staff line, no one-line staff; kept for Ottavas)
                    if (mark.TrimStart('@', '!') is "ottava" or "quindicesima")
                    {
                        ottLines.Add((Math.Min(p1.X, p2.X), p1.X <= p2.X ? p1.Y : p2.Y, Math.Max(p1.X, p2.X), p1.X <= p2.X ? p2.Y : p1.Y, a.ContainsKey("stroke-dasharray"), Src()));
                        break;
                    }
                    // (a short slanted stroke with no source: an acciaccatura's slash, if a grace note's head stands by it -- below)
                    if (!a.ContainsKey("data-pos") && Math.Abs(p2.X - p1.X) > 0.5 * page.Ppu && Math.Abs(p2.X - p1.X) < 2.5 * page.Ppu
                        && Math.Abs(p2.Y - p1.Y) > 0.4 * page.Ppu && Math.Abs(p2.Y - p1.Y) < 2.5 * page.Ppu)
                    {
                        strokes.Add(((p1.X + p2.X) / 2, (p1.Y + p2.Y) / 2));
                        break;
                    }
                    bool pedal = mark.TrimStart('@', '!') is "sustain" or "sostenuto" or "unaCorda" or "treCorde";
                    if (pedal || mark.TrimStart('@', '!') is "cresc" or "decresc" or "dim")
                        markLines.Add((Math.Min(p1.X, p2.X), p1.X <= p2.X ? p1.Y : p2.Y, Math.Max(p1.X, p2.X), p1.X <= p2.X ? p2.Y : p1.Y, pedal, Src()));
                    if (!pedal) page.Lines.Add((p1.X, p1.Y, p2.X, p2.Y, N(a.GetValueOrDefault("stroke-width", "0")) * tr.Sx * page.Ppu));
                    break;
                }
                case "rect":
                {
                    if (a.ContainsKey("fill") && a["fill"] == "#FFFFFF")
                    {
                        if (a.ContainsKey("stroke") && Src() >= 0)
                        {
                            var q = P(N(a["x"]), N(a["y"]));
                            boxed.Add((q.X, q.Y, q.X + N(a["width"]) * tr.Sx * page.Ppu, q.Y + N(a["height"]) * tr.Sy * page.Ppu, Src()));
                        }
                        break;
                    }
                    var p1 = P(N(a["x"]), N(a["y"]));
                    page.Rects.Add((p1.X, p1.Y, N(a["width"]) * tr.Sx * page.Ppu, N(a["height"]) * tr.Sy * page.Ppu, Src()));
                    break;
                }
                case "circle":
                {
                    // a repeat barline's dot (at the form's |:, or at the last note before its :|), or a percent
                    // sign's: FindForm keeps the pairs beside a thick barline
                    var c = P(N(a["cx"]), N(a["cy"]));
                    repDots.Add((c.X, c.Y, Src()));
                    break;
                }
                case "path":
                {
                    // "M x0,y0 C c1 c2 x1,y1 C ... Z": the first curve runs from one end to the other
                    var mp = Regex.Match(a.GetValueOrDefault("d", ""), @"M\s*([-\d.]+),([-\d.]+)\s*C\s*[-\d.]+,[-\d.]+\s+[-\d.]+,[-\d.]+\s+([-\d.]+),([-\d.]+)");
                    if (!mp.Success) break;
                    var p1 = P(N(mp.Groups[1].Value), N(mp.Groups[2].Value));
                    var p2 = P(N(mp.Groups[3].Value), N(mp.Groups[4].Value));
                    if (p2.X < p1.X) (p1, p2) = (p2, p1);
                    int src = Src();
                    // (the slur Lily# draws from an acciaccatura or appoggiatura to its note is nothing
                    // written: no tie, no slur. Its source is the word's)
                    if (lys != null && src >= 0 && (string.CompareOrdinal(lys, src, "acciaccatura", 0, 12) == 0 || string.CompareOrdinal(lys, src, "appoggiatura", 0, 12) == 0)) break;
                    if (lys != null && src >= 0 && src < lys.Length && lys[src] == '(')
                        page.Slurs.Add((p1.X, p1.Y, p2.X, p2.Y, src, SlurEnd(lys, src)));
                    else page.Ties.Add((p1.X, p1.Y, p2.X, p2.Y, src));
                    break;
                }
                case "polygon":
                {
                    var pts = new List<(double, double)>();
                    var nums = a["points"].Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
                    for (int i = 0; i + 1 < nums.Length; i += 2) pts.Add(P(N(nums[i]), N(nums[i + 1])));
                    // a percent repeat's slanted bar (its dots are circles, left out): not a beam. Its source
                    // position the old "repeat percent"'s, or (Lily# 0.14, "| % |", "| %2 |") the mark's own
                    int src = Src();
                    if (lys != null && src >= 0 && src < lys.Length && (lys[src] == '%' || string.CompareOrdinal(lys, src, "repeat", 0, 6) == 0)) percentBars.Add((pts, src));
                    else page.Polys.Add(pts);
                    break;
                }
                case "text":
                {
                    if (selfClose) break;
                    int end = s.IndexOf("</text>", m.Index + m.Length, StringComparison.Ordinal);
                    string content = s.Substring(m.Index + m.Length, end - m.Index - m.Length);
                    if (a.GetValueOrDefault("class") != "music")
                    {
                        // a rehearsal mark: the words in the box drawn just before them
                        if (content.Length > 0 && boxed.FindIndex(b => b.Src == Src()) is int bi and >= 0)
                        {
                            var b = boxed[bi];
                            page.Rehearsals.Add((b.L, b.T, b.R, b.B, content, b.Src));
                            break;
                        }
                        // an octave line's sign: bold italic 8va, 8vb, 15ma or 15mb (in brackets where the
                        // line goes on from a system's start), set from its left on its baseline, at the mark's '@'
                        if (a.GetValueOrDefault("font-style") == "italic" && a.GetValueOrDefault("font-weight") == "bold"
                            && Regex.IsMatch(content, @"^\(?(8|15)(va|vb|ma|mb)\)?$") && MarkAt(Src()).TrimStart('@') is "ottava" or "quindicesima")
                        {
                            var o = P(N(a["x"]), N(a["y"]));
                            ottTexts.Add((o.X, o.Y, N(a["font-size"]) * tr.Sx * page.Ppu, content, Src()));
                            break;
                        }
                        // a dynamic in Emmentaler: bold italic letters, anchored at the middle of their
                        // baseline, at a mark's '@' (the PNG inks Emmentaler's own letters there: their
                        // widths, a little kerned together, split the word into its letters)
                        if (a.GetValueOrDefault("font-style") == "italic" && a.GetValueOrDefault("font-weight") == "bold" && content.Length > 0
                            && content.All(dynamicLetter.ContainsKey) && MarkAt(Src()).Length > 1)
                        {
                            double em = N(a["font-size"]) * tr.Sx * page.Ppu, emY = N(a["font-size"]) * tr.Sy * page.Ppu;
                            var o = P(N(a["x"]), N(a["y"]));
                            double Kern(int i) => content[i - 1] == 'f' && content[i] == 'f' ? 0.21 : 0.07;
                            double width = content.Sum(ch => dynamicLetter[ch].W) - Enumerable.Range(1, content.Length - 1).Sum(Kern);
                            double left = (a.GetValueOrDefault("text-anchor") == "middle" ? -width / 2 : 0);
                            for (int i = 0; i < content.Length; i++)
                            {
                                if (i > 0) left -= Kern(i);
                                var (w, top, bottom) = dynamicLetter[content[i]];
                                page.Dynamics.Add((o.X + left * em, o.Y + top * emY, o.X + (left + w) * em, o.Y + bottom * emY, content[i], Src()));
                                left += w;
                            }
                            break;
                        }
                        // a TAB fret number: bold upright digits, anchored at the middle of their baseline
                        if (a.GetValueOrDefault("font-weight") == "bold" && !a.ContainsKey("font-style") && content.Length > 0
                            && content.All(char.IsDigit) && Math.Abs(N(a.GetValueOrDefault("font-size", "0")) - 3) < 0.01)
                        {
                            var o = P(N(a["x"]), N(a["y"]));
                            double w = TabDigitWidth * tr.Sx * page.Ppu, h = TabDigitHeight * tr.Sy * page.Ppu;
                            double left = o.X - content.Length * w / 2;
                            for (int i = 0; i < content.Length; i++)
                                page.TabDigits.Add((left + i * w, o.Y - h, left + (i + 1) * w, o.Y, content[i], Src()));
                            break;
                        }
                        // the same as today's lysc sets it (2026-10-09): plain upright digits of the text font, 2.3 or
                        // 2.8 high by the TAB's line gap, a single digit anchored at its middle, a fret of two set digit
                        // by digit from each one's left (a bar number is bold, a chord name names its font family, the
                        // page number has no source)
                        if (!a.ContainsKey("font-weight") && !a.ContainsKey("font-style") && !a.ContainsKey("font-family") && a.ContainsKey("data-pos")
                            && content.Length > 0 && content.All(char.IsDigit) && N(a.GetValueOrDefault("font-size", "0")) is > 2.25 and < 3.2)
                        {
                            double em = N(a["font-size"]);
                            var o = P(N(a["x"]), N(a["y"]));
                            double w = TabDigitAdvance * em * tr.Sx * page.Ppu, h = TabDigitEmHeight * em * tr.Sy * page.Ppu;
                            double left = a.GetValueOrDefault("text-anchor") == "middle" ? o.X - content.Length * w / 2 : o.X;
                            for (int i = 0; i < content.Length; i++)
                                page.TabDigits.Add((left + i * w, o.Y - h, left + (i + 1) * w, o.Y, content[i], Src()));
                            break;
                        }
                        // a tuplet number: a small italic digit or two, anchored at its middle
                        if (a.GetValueOrDefault("font-style") == "italic" && N(a.GetValueOrDefault("font-size", "9")) < 2.5
                            && content.Length <= 2 && content.All(char.IsDigit)
                            // (not the 8 of a treble_8 clef, set as the same small italic text)
                            && !(lys != null && Src() >= 0 && Regex.IsMatch(lys.Substring(Src(), Math.Min(12, lys.Length - Src())), @"^\w+_8")))
                        {
                            var o = P(N(a["x"]), N(a["y"]));
                            page.Tuplets.Add((o.X, o.Y, content, Src()));
                        }
                        break;
                    }
                    double size = N(a["font-size"]) / 4;
                    double x = N(a["x"]), y = N(a["y"]);
                    var boxes = pageBoxes;
                    if (a.TryGetValue("font-family", out var ff))
                    {
                        string font = ff.Split(',')[0].Trim().Trim('\'');
                        // (a scaled note head, NoteHead.scale, is set in Emmentaler's design for its
                        // size, Emmentaler-23 or -18 as LilyPond's: the same glyphs, font-size scales them)
                        if (Regex.IsMatch(font, @"^Emmentaler-\d+$")) font = "Emmentaler";
                        boxes = font == "Emmentaler" || File.Exists(Path.Combine(AppContext.BaseDirectory, $"smufl-bbox-{font}.tsv")) ? Boxes(font) : new();
                    }
                    foreach (char ch in content)
                    {
                        string cp = $"{(int)ch:X4}";
                        if (!boxes.TryGetValue(cp, out var b)) continue;
                        var o = P(x, y);
                        double k = size * tr.Sx * page.Ppu;
                        page.Glyphs.Add(new TGlyph
                        {
                            Name = b.Name, X = o.X, Y = o.Y, Size = size, Src = Src(),
                            L = o.X + b.X0 * k, R = o.X + b.X1 * k, T = o.Y - b.Y1 * k, B = o.Y - b.Y0 * k,
                        });
                        x += b.X1 - b.X0;   // (glyph runs are single glyphs in practice)
                    }
                    break;
                }
            }
        }
        // the percent signs: the slanted bars of one repeat that stand side by side (a %% has two,
        // a space or so apart) make one sign; its centre is the bars' box's
        var bars = percentBars.Select(b => (L: b.Pts.Min(p => p.X), T: b.Pts.Min(p => p.Y), R: b.Pts.Max(p => p.X), B: b.Pts.Max(p => p.Y), b.Src))
            .OrderBy(b => b.L).ToList();
        var used = new bool[bars.Count];
        for (int i = 0; i < bars.Count; i++)
        {
            if (used[i]) continue;
            var (l, t, r, b, src) = bars[i];
            int n = 1;
            for (int j = i + 1; j < bars.Count; j++)
                if (!used[j] && bars[j].Src == src && bars[j].L < r + 0.5 * page.Ppu && Math.Abs(bars[j].T - t) < 0.5 * page.Ppu)
                {
                    used[j] = true; n++;
                    l = Math.Min(l, bars[j].L); r = Math.Max(r, bars[j].R); t = Math.Min(t, bars[j].T); b = Math.Max(b, bars[j].B);
                }
            // (a drum staff's two voices each carry the repeat: the sign is drawn twice in one place)
            double cx = (l + r) / 2, cy = (t + b) / 2;
            if (page.Percents.Any(q => Math.Abs(q.X - cx) < page.Ppu && Math.Abs(q.Y - cy) < page.Ppu)) continue;
            page.Percents.Add((cx, cy, n, src));
        }
        // a SMuFL font's dynamic letters are its glyphs (named by the letter, as Emmentaler's text): dynamics, not glyphs
        foreach (var g in page.Glyphs.Where(g => g.Name.Length == 1 && dynamicLetter.ContainsKey(g.Name[0]) && MarkAt(g.Src).Length > 1).ToList())
        {
            page.Dynamics.Add((g.L, g.T, g.R, g.B, g.Name[0], g.Src));
            page.Glyphs.Remove(g);
        }
        FindMarks(page, markLines);
        FindOttavas(page, ottTexts, ottLines);
        FindForm(page, repDots, voltaLines);
        // an acciaccatura's slash: a short slanted stroke by a grace note's head (within two spaces
        // and a half across, three and a half up or down: through a stem, or the beam of two), among
        // Marks as grace.slash (S21)
        foreach (var (x, y) in strokes)
            if (page.Glyphs.Any(g => g.Small && g.Name.StartsWith("noteheads.") && Math.Abs(g.Cx - x) < 2.5 * page.Ppu && Math.Abs(g.Cy - y) < 3.5 * page.Ppu))
                page.Marks.Add((x, y, "grace.slash", -1));
        return page;
    }

    /// <summary>
    /// The octave lines (<see cref="SvgPage.Ottavas"/>, their hooks among <see cref="SvgPage.Marks"/>) from
    /// their signs and lines. A sign's box runs from its text's left to half a space short of its dashes
    /// (Lily# sets the dashes that far from the letters; with no dashes of its own, as wide as its letters
    /// are), and from the capitals' height over the baseline (0.72 em of bold italic) to a little under it.
    /// The hook is the short upright line of the mark's at the dashes' right end; the mark is where they meet.
    /// </summary>
    static void FindOttavas(SvgPage page, List<(double X, double Y, double Em, string Text, int Src)> texts, List<(double X1, double Y1, double X2, double Y2, bool Dashed, int Src)> lines)
    {
        double u = page.Ppu;
        foreach (var t in texts)
        {
            // (the sign's own dashes: the mark's dashed line at the letters' height, starting just right of them)
            var dash = lines.Where(l => l.Dashed && l.Src == t.Src && l.X1 > t.X && l.X1 < t.X + 5 * t.Em && Math.Abs(l.Y1 - (t.Y - 0.35 * t.Em)) < 0.5 * t.Em).OrderBy(l => l.X1).FirstOrDefault();
            string text = t.Text.Trim('(', ')');
            double right = dash.Dashed ? dash.X1 - 0.5 * u : t.X + (text.StartsWith("15") ? 2.7 : 1.7) * t.Em;
            page.Ottavas.Add((t.X, t.Y - 0.72 * t.Em, right, t.Y + 0.05 * t.Em, text, t.Src));
        }
        foreach (var h in lines.Where(l => !l.Dashed && Math.Abs(l.X1 - l.X2) < 0.05 * u && Math.Abs(l.Y1 - l.Y2) > 0.3 * u))
        {
            var d = lines.FirstOrDefault(l => l.Dashed && l.Src == h.Src && Math.Abs(l.X2 - h.X1) < 0.1 * u);
            if (!d.Dashed) continue;
            page.Marks.Add((h.X1, Math.Abs(h.Y1 - d.Y2) < Math.Abs(h.Y2 - d.Y2) ? h.Y1 : h.Y2, "ott.end", h.Src));
        }
    }

    /// <summary>
    /// The form's signs (<see cref="Marks"/>, S22): a repeat barline's dots, two circles one space apart
    /// in a staff's middle spaces, as one point between them (rep.open right of its thick line, rep.close
    /// left of it: a :|: has both); and a volta bracket's start and end, the corners where its rule
    /// meets its hooks (volta.l at the left hook, where the number stands; volta.r at the right one. A
    /// bracket going on from a system's start has no left hook, one running past the system's end no
    /// right one). The segno and coda signs are glyphs (scripts.segno, scripts.coda), among Glyphs.
    /// </summary>
    static void FindForm(SvgPage page, List<(double X, double Y, int Src)> dots, List<(double X1, double Y1, double X2, double Y2, int Src)> lines)
    {
        double u = page.Ppu;
        var used = new bool[dots.Count];
        for (int i = 0; i < dots.Count; i++)
        {
            if (used[i]) continue;
            // (two dots one over the other a space apart: a percent sign's stand side by side)
            int j = Enumerable.Range(i + 1, dots.Count - i - 1).FirstOrDefault(k => !used[k]
                && Math.Abs(dots[k].X - dots[i].X) < 0.1 * u && Math.Abs(Math.Abs(dots[k].Y - dots[i].Y) - u) < 0.2 * u, -1);
            if (j < 0) continue;
            double x = dots[i].X, y = (dots[i].Y + dots[j].Y) / 2;
            // (the sign's thick line, a staff tall, within two spaces, the dots on its inner side; the
            // source position is the |:'s own, or the last note's before a :|, so it tells nothing)
            var thick = page.Rects.Where(r => r.W >= 0.4 * u && r.H < 8 * u && Math.Abs(r.X + r.W / 2 - x) < 2 * u && y > r.Y && y < r.Y + r.H)
                .OrderBy(r => Math.Abs(r.X + r.W / 2 - x)).FirstOrDefault();
            if (thick.W == 0) continue;
            used[i] = used[j] = true;
            page.Marks.Add((x, y, thick.X > x ? "rep.close" : "rep.open", dots[i].Src));
        }
        var rules = lines.Where(l => Math.Abs(l.Y1 - l.Y2) < 0.05 * u && Math.Abs(l.X2 - l.X1) > 0.3 * u).ToList();
        foreach (var h in lines.Where(l => Math.Abs(l.X1 - l.X2) < 0.05 * u && Math.Abs(l.Y1 - l.Y2) > 0.3 * u))
        {
            double x = h.X1, top = Math.Min(h.Y1, h.Y2);
            var rule = rules.Where(r => r.Src == h.Src && Math.Abs(Math.Min(r.Y1, r.Y2) - top) < 0.1 * u && (Math.Abs(Math.Min(r.X1, r.X2) - x) < 0.1 * u || Math.Abs(Math.Max(r.X1, r.X2) - x) < 0.1 * u)).ToList();
            if (rule.Count == 0) continue;
            bool left = rule.Any(r => Math.Abs(Math.Min(r.X1, r.X2) - x) < 0.1 * u);
            page.Marks.Add((x, top, left ? "volta.l" : "volta.r", h.Src));
        }
    }

    /// <summary>
    /// The points of the hairpins and pedal brackets (<see cref="Marks"/>) from their lines, each
    /// running left to right. A hairpin is two lines of one mark over the same stretch: where they
    /// meet is its tip, where they stand apart its open end (a hairpin broken at a system's end is
    /// open at both ends of a piece, and has no tip there). A bracket's hook is a short upright line
    /// at the end of its level line: the start where the level line runs on to the right, the end where
    /// it came from the left; a change is two slanted lines meeting at the top of a notch (each from
    /// its own mark: the pedal released and the pedal pressed again).
    /// </summary>
    static void FindMarks(SvgPage page, List<(double X1, double Y1, double X2, double Y2, bool Pedal, int Src)> lines)
    {
        double u = page.Ppu;
        var hairpins = lines.Where(l => !l.Pedal).ToList();
        var used = new bool[hairpins.Count];
        for (int i = 0; i < hairpins.Count; i++)
        {
            if (used[i]) continue;
            var a = hairpins[i];
            int j = Enumerable.Range(i + 1, hairpins.Count - i - 1).FirstOrDefault(k => !used[k] && hairpins[k].Src == a.Src
                && Math.Abs(hairpins[k].X1 - a.X1) < 0.1 * u && Math.Abs(hairpins[k].X2 - a.X2) < 0.1 * u, -1);
            if (j < 0) continue;
            used[i] = used[j] = true;
            var b = hairpins[j];
            double gapL = Math.Abs(a.Y1 - b.Y1), gapR = Math.Abs(a.Y2 - b.Y2);
            void End(double x, double y1, double y2, double gap, bool narrower)
            {
                if (narrower && gap < 0.25 * u) page.Marks.Add((x, (y1 + y2) / 2, "hairpin.tip", a.Src));
                else if (!narrower && gap > 0.4 * u) page.Marks.Add((x, (y1 + y2) / 2, "hairpin.open", a.Src));
            }
            End(a.X1, a.Y1, b.Y1, gapL, gapL < gapR);
            End(a.X2, a.Y2, b.Y2, gapR, gapR < gapL);
        }
        var pedal = lines.Where(l => l.Pedal).ToList();
        var level = pedal.Where(l => Math.Abs(l.Y1 - l.Y2) < 0.05 * u && l.X2 - l.X1 > 0.3 * u).ToList();
        foreach (var l in pedal.Where(l => Math.Abs(l.X1 - l.X2) < 0.05 * u && Math.Abs(l.Y1 - l.Y2) > 0.3 * u))
        {
            double x = l.X1, y = Math.Max(l.Y1, l.Y2);
            if (level.Any(h => Math.Abs(h.Y1 - y) < 0.1 * u && Math.Abs(h.X1 - x) < 0.1 * u)) page.Marks.Add((x, y, "ped.l", l.Src));
            else if (level.Any(h => Math.Abs(h.Y1 - y) < 0.1 * u && Math.Abs(h.X2 - x) < 0.1 * u)) page.Marks.Add((x, y, "ped.r", l.Src));
        }
        var slants = pedal.Where(l => Math.Abs(l.X1 - l.X2) >= 0.05 * u && Math.Abs(l.Y1 - l.Y2) > 0.3 * u).ToList();
        foreach (var l in slants)
        {
            // (the notch's top: the higher end, where the other slanted line ends too; one point a notch)
            var top = l.Y1 < l.Y2 ? (X: l.X1, Y: l.Y1) : (X: l.X2, Y: l.Y2);
            if (!slants.Any(o => o != l && (Math.Abs(o.X1 - top.X) < 0.1 * u && Math.Abs(o.Y1 - top.Y) < 0.1 * u || Math.Abs(o.X2 - top.X) < 0.1 * u && Math.Abs(o.Y2 - top.Y) < 0.1 * u))) continue;
            if (page.Marks.Any(m => m.Kind == "ped.notch" && Math.Abs(m.X - top.X) < 0.2 * u && Math.Abs(m.Y - top.Y) < 0.2 * u)) continue;
            page.Marks.Add((top.X, top.Y, "ped.notch", l.Src));
        }
    }

    /// <summary>The ')' that closes the slur opened at <paramref name="open"/> (slurs nest), or -1.</summary>
    static int SlurEnd(string lys, int open)
    {
        int depth = 0;
        for (int i = open; i < lys.Length; i++)
        {
            if (lys[i] == '/' && i + 1 < lys.Length && lys[i + 1] == '/') { i = lys.IndexOf('\n', i); if (i < 0) break; continue; }
            if (lys[i] == '(') depth++;
            else if (lys[i] == ')' && --depth == 0) return i;
        }
        return -1;
    }
}
