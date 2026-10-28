using System.Drawing;
using System.Drawing.Imaging;
using System.Text;
using System.Text.RegularExpressions;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace Omr;

/// <summary>
/// The words over the music on the first page: title, subtitle, composer, poet, read with Windows'
/// own OCR (Windows.Media.Ocr: no library to ship, nothing sent anywhere; the languages are the
/// user's). Only the page above the first system is read, short of the six spaces just over its
/// first staff (a tempo, a rehearsal mark, an expression). The lines are placed as engravers set
/// them: the largest centred line the title, the centred line under it the subtitle, a line to the
/// right the composer, one to the left the poet.
/// </summary>
sealed class Header
{
    public string Title, Subtitle, Composer, Poet;
    /// <summary>Every line read, with its role ("" for none): the run's report.</summary>
    public readonly List<(string Text, string Role, RectangleF Box)> Lines = new();

    public bool Any => Title != null || Subtitle != null || Composer != null || Poet != null;

    /// <summary>The header of a page (its file as loaded and its reading: the first system's place); null when it has no system or the OCR cannot run.</summary>
    public static Header Read(string path, PageResult res)
    {
        var sys = res.Systems.FirstOrDefault();
        if (sys == null || sys.Staves.Count == 0) return null;
        var top = sys.Staves[0];
        double s = res.S;
        // (the first staff's top, its left and right, on the page as loaded: the reading levels and scales it)
        // (read down to two spaces over it: under that a tempo, a rehearsal mark or an expression
        // stands, read with the system (Tempo), and notes in the band muddle the OCR's lines; a page
        // short of room sets its title three spaces over the music. A line reaching into the band
        // over the system is a heading only at an edge: a composer's or a poet's)
        // (a lead sheet's chord diagrams over the first staff, with its chord names, tempo and marks
        // over them: the music starts over the diagrams, and the OCR reads nothing of them)
        var grids = Tempo.FramesOf(res).Where(f => f.System == sys.Index && f.Grid).ToList();
        double over = grids.Count > 0 ? Math.Min(top.Top, grids.Min(g => g.Dark.Top)) : top.Top;
        var a = res.Dark.ToSource(top.Left, over - 2 * s);
        var b = res.Dark.ToSource(top.Right, over - 2 * s);
        var c = res.Dark.ToSource(top.Left, over - 1 * s);
        int bottom = (int)Math.Floor(b.Y);
        if (a.Y < 0.02 * res.Dark.H) return null;
        double left = Math.Min(a.X, b.X), right = Math.Max(a.X, b.X), space = Math.Abs(c.Y - a.Y);
        List<(string Text, RectangleF Box)> lines;
        try { using var page = new Bitmap(path); lines = Recognize(page, new Rectangle(0, 0, page.Width, bottom)); }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            Console.WriteLine($"header: no OCR ({(string.IsNullOrWhiteSpace(e.Message) ? $"0x{e.HResult:X8}" : e.Message.Trim())})");
            return null;
        }
        // (a rehearsal mark's box over the first system, its words at the staff's left end, is the
        // system's mark, read with it (Tempo): no poet -- the sonatina's "Exposition")
        var boxes = Tempo.FramesOf(res).Where(f => f.System == sys.Index && !f.Grid).Select(f => f.Box).ToList();
        lines = lines.Where(l => !boxes.Any(bx => bx.Contains((l.Box.Left + l.Box.Right) / 2, (l.Box.Top + l.Box.Bottom) / 2))).ToList();
        return Place(lines, left, right, a.Y, space);
    }

    /// <summary>A chord name as the OCR reads one: a root, its accidental, a quality and figures, a bass after a slash ("Bb", "c7", "Dm/C", "Gm7", "F/A").</summary>
    public static bool ChordName(string text) =>
        Regex.IsMatch(text.Trim(), @"^[A-Ga-g][b#♭♯]?(?:m|maj|min|dim|aug|sus|add|M|\+|°|ø|[0-9])*(?:/[A-Ga-g][b#♭♯]?)?$");

    /// <summary>
    /// The roles by place: the music's span from <paramref name="left"/> to <paramref name="right"/>,
    /// the band of a tempo and marks from <paramref name="near"/> down (pixels of the page as loaded;
    /// <paramref name="space"/>, a staff space there).
    /// </summary>
    static Header Place(List<(string Text, RectangleF Box)> all, double left, double right, double near, double space)
    {
        var h = new Header();
        double mid = (left + right) / 2, width = right - left;
        var roles = new Dictionary<int, string>();
        // what is no heading: a copyright line, and a paragraph (a book's notes on the piece: lines of
        // one size, one under another from one left edge, three or more)
        bool Copyright(string t) => Regex.IsMatch(t, @"©|Ⓒ|\(c\)|◎|copyright|\bINC\b|JASRAC|All Rights Reserved", RegexOptions.IgnoreCase);
        bool InParagraph(RectangleF r) => all.Count(o => o.Box != r && Math.Abs(o.Box.Left - r.Left) < 0.02 * width
            && Math.Abs(o.Box.Height - r.Height) < 0.2 * r.Height && Math.Abs(o.Box.Top - r.Top) < 3.5 * r.Height) >= 2;
        var lines = all.Select((l, i) => (l.Text, l.Box, I: i)).Where(l => !Copyright(l.Text) && !InParagraph(l.Box)).ToList();
        // a credit says what it is: "Music by", "Words & Music by", "作曲", "作詞" (its place then does not matter)
        foreach (var l in lines)
        {
            var m = Regex.Match(l.Text, @"^(?:(?<both>(?:words|lyrics)\s*(?:&|and)\s*music|music\s*(?:&|and)\s*(?:words|lyrics))|(?<music>music|composed)|(?<words>words|lyrics|text))\s+by\s+(?<who>.+)$", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                string who = m.Groups["who"].Value.Trim();
                if (!m.Groups["words"].Success) h.Composer ??= who;
                if (!m.Groups["music"].Success) h.Poet ??= who;
                roles[l.I] = m.Groups["both"].Success ? "composer, poet" : m.Groups["music"].Success ? "composer" : "poet";
                continue;
            }
            foreach (Match j in Regex.Matches(l.Text, @"(?<role>作詞|作曲)\s*[:：・]?\s*(?<who>[^作]+?)(?=\s*(?:作詞|作曲|編曲|$))"))
            {
                if (j.Groups["role"].Value == "作曲") h.Composer ??= j.Groups["who"].Value.Trim(); else h.Poet ??= j.Groups["who"].Value.Trim();
                roles[l.I] = "credit";
            }
        }
        lines = lines.Where(l => !roles.ContainsKey(l.I)).ToList();
        // (a line centred on the music: its middle within a tenth of the width of the music's; to one
        // side: wholly past the middle, out to a tenth of the width from that side's edge)
        // In the band over the system only a line to an edge is a heading: the composer's ending at the
        // staff's right end, the poet's starting left of the staff (a tempo starts over the notes)
        bool Near(RectangleF r) => r.Bottom > near;
        bool Centred(RectangleF r) => !Near(r) && Math.Abs((r.Left + r.Right) / 2 - mid) < 0.1 * width;
        bool Right(RectangleF r) => r.Left > mid && (Near(r) ? Math.Abs(r.Right - right) < 2 * space : r.Right > right - 0.1 * width);
        bool Left(RectangleF r) => r.Right < mid && (Near(r) ? r.Left < left + space : r.Left < left + 0.1 * width);
        // (a tempo starts at the staff's left end too, when no part's name stands there: it is no poet)
        lines = lines.Where(l => !Tempo.Is(l.Text)).ToList();
        // (a lead sheet's chord names stand over its diagrams, high over the first staff: "Bb" was the
        // largest centred line of leadsheet-collide, over its title; a line that reads as a chord
        // name is no heading)
        lines = lines.Where(l => !ChordName(l.Text)).ToList();
        var centred = lines.Where(l => Centred(l.Box)).ToList();
        // (the largest print, as the size of its type: a Japanese or Chinese line's box is about the
        // em; a Latin one's runs from its tallest letter to its lowest, the capitals and ascenders
        // seven tenths of the em over the line, small letters a half, descenders a fifth under it)
        static double Size((string Text, RectangleF Box, int I) l)
        {
            string t = l.Text;
            if (Regex.Matches(t, $"[{Cjk}]").Count * 2 > t.Count(char.IsLetter)) return l.Box.Height / 0.92;
            double up = Regex.IsMatch(t, @"[\p{Lu}\dbdfhklt]") ? 0.72 : 0.5, down = Regex.IsMatch(t, @"[gjpqy,;()]") ? 0.22 : 0;
            return l.Box.Height / (up + down);
        }
        var title = centred.OrderByDescending(Size).ThenBy(l => l.Box.Top).FirstOrDefault();
        if (title.Text != null)
        {
            h.Title = title.Text; roles[title.I] = "title";
            // the subtitle: the centred line under the title, else the one over it (a cycle's name over a song's)
            var sub = centred.Where(l => l.I != title.I && l.Box.Top > title.Box.Top).OrderBy(l => l.Box.Top).FirstOrDefault();
            if (sub.Text == null) sub = centred.Where(l => l.I != title.I).OrderByDescending(l => l.Box.Top).FirstOrDefault();
            if (sub.Text != null) { h.Subtitle = sub.Text; roles[sub.I] = "subtitle"; }
        }
        // (to one side by place only under a title: a page with none is a later page, its corner a running head)
        if (h.Title == null) lines.Clear();
        var composer = lines.Where(l => Right(l.Box)).OrderBy(l => l.Box.Top).FirstOrDefault();
        if (composer.Text != null && h.Composer == null) { h.Composer = composer.Text; roles[composer.I] = "composer"; }
        var poet = lines.Where(l => Left(l.Box)).OrderBy(l => l.Box.Top).FirstOrDefault();
        if (poet.Text != null && h.Poet == null) { h.Poet = poet.Text; roles[poet.I] = "poet"; }
        // (a title starts with a capital; a name's words do, but for its particles)
        if (h.Title is { Length: > 0 } t && char.IsLower(t[0])) h.Title = char.ToUpperInvariant(t[0]) + t[1..];
        h.Composer = Name(h.Composer); h.Poet = Name(h.Poet);
        for (int i = 0; i < all.Count; i++) h.Lines.Add((all[i].Text, roles.GetValueOrDefault(i, ""), all[i].Box));
        return h;
    }

    static readonly HashSet<string> Particles = new() { "von", "van", "der", "den", "de", "di", "da", "du", "la", "le", "zu", "y", "and", "und", "et", "arr.", "by" };

    // (a name the OCR gave a small initial, "Emil vogt", where its other words have capitals)
    static string Name(string name)
    {
        if (name == null) return null;
        var words = name.Split(' ');
        if (!words.Any(w => w.Length > 0 && char.IsUpper(w[0]))) return name;
        return string.Join(" ", words.Select(w => w.Length > 1 && char.IsLower(w[0]) && !Particles.Contains(w) ? char.ToUpperInvariant(w[0]) + w[1..] : w));
    }

    /// <summary>The scale a page is read at: as a page about 2400 pixels wide, 200 to 300 dpi.</summary>
    public static double Scale(int pageWidth) => 2400.0 / pageWidth;

    /// <summary>
    /// The OCR's lines in a part of the page: their text and box (pixels of the page as loaded).
    /// <paramref name="scale"/>: the page's (Scale), for a cut of it in a bitmap of its own.
    /// </summary>
    /// <summary>Dev: every engine's lines printed as read, before the languages are reconciled (--ocr, --ocr-file).</summary>
    public static bool Trace;

    public static List<(string Text, RectangleF Box)> Recognize(Bitmap page, Rectangle crop, double scale = 0) => Recognize(page, crop, scale, true);

    /// <summary>
    /// A line read again by itself: its box widened by half an em and the height by a third, copied
    /// onto white with half an em round it, read at scale <paramref name="k"/>; its lines joined left
    /// to right ("" for none). A Japanese engine reads a Latin line clean alone where in the band
    /// of the whole page it lost a letter or put a character of its own script in a word.
    /// </summary>
    /// <param name="pad">the white round it, in ems (half a one as a rule)</param>
    /// <param name="masks">what is painted white in the cut (page pixels): a rehearsal mark's box beside a tempo, which the cut's margin reaches into</param>
    internal static string Alone(Bitmap page, RectangleF box, double k, double pad = 0.5, IEnumerable<RectangleF> masks = null)
    {
        int em = (int)Math.Ceiling(box.Height), m = (int)Math.Ceiling(pad * box.Height);
        var inner = Rectangle.FromLTRB((int)Math.Floor(box.Left - 0.5 * em), (int)Math.Floor(box.Top - 0.35 * em), (int)Math.Ceiling(box.Right + 0.5 * em), (int)Math.Ceiling(box.Bottom + 0.35 * em));
        inner.Intersect(new Rectangle(0, 0, page.Width, page.Height));
        if (inner.Width < 4 || inner.Height < 4) return "";
        using var alone = new Bitmap(inner.Width + 2 * m, inner.Height + 2 * m, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(alone))
        {
            g.Clear(Color.White);
            g.DrawImage(page, new Rectangle(m, m, inner.Width, inner.Height), inner, GraphicsUnit.Pixel);
            // (a box the line's own box reaches over too: a tempo's metronome note stands up level
            // with the box the tempo is stacked over, roadsong's "Intro"; the line's letters are
            // never inside a box, Tempo.Rows leaves a box's pieces out)
            foreach (var mask in masks ?? Enumerable.Empty<RectangleF>())
            {
                var r = RectangleF.Inflate(mask, 2, 2);
                g.FillRectangle(Brushes.White, r.Left - inner.Left + m, r.Top - inner.Top + m, r.Width, r.Height);
            }
        }
        var lines = Recognize(alone, new Rectangle(0, 0, alone.Width, alone.Height), k, false);
        return string.Join(" ", lines.OrderBy(l => l.Box.Left).Select(l => l.Text));
    }

    /// <param name="again">Whether a Japanese engine's line with its script in a Latin word is read again alone (not for a cut read alone already: Tempo.Alone).</param>
    public static List<(string Text, RectangleF Box)> Recognize(Bitmap page, Rectangle crop, double scale, bool again)
    {
        var engine = OcrEngine.TryCreateFromUserProfileLanguages() ?? throw new InvalidOperationException("no OCR language installed");
        crop.Intersect(new Rectangle(0, 0, page.Width, page.Height));
        if (crop.Width < 8 || crop.Height < 8) return new();
        int w = crop.Width, h = crop.Height;
        // (scaled as a page about 2400 pixels wide, 200 to 300 dpi: small print is read better larger,
        // and a title of 150 pixels on a page drawn at 600 dpi worse than smaller)
        double k = Math.Min(scale > 0 ? scale : Scale(page.Width), OcrEngine.MaxImageDimension / (double)Math.Max(w, h));
        using var band = new Bitmap(Math.Max(1, (int)(w * k)), Math.Max(1, (int)(h * k)), PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(band))
        {
            g.Clear(Color.White);
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.DrawImage(page, new Rectangle(0, 0, band.Width, band.Height), crop, GraphicsUnit.Pixel);
        }
        using var ms = new MemoryStream();
        band.Save(ms, ImageFormat.Png);
        ms.Position = 0;
        using var ras = ms.AsRandomAccessStream();
        var decoder = BitmapDecoder.CreateAsync(ras).AsTask().GetAwaiter().GetResult();
        using var bmp = decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied).AsTask().GetAwaiter().GetResult();
        List<(string Text, RectangleF Box)> Lines(OcrEngine e)
        {
            var lines = new List<(string, RectangleF)>();
            foreach (var line in e.RecognizeAsync(bmp).AsTask().GetAwaiter().GetResult().Lines)
            {
                if (line.Words.Count == 0) continue;
                var all = line.Words.ToList();
                double em = all.Max(q => q.BoundingRect.Height);
                // (a line broken where its words stand more than an em and a fifth apart, four times a
                // word space: the OCR runs a rehearsal mark into the tempo beside it, a poet's line into
                // the composer's across the page)
                // (and where a rule stands upright between two words: a rehearsal mark's box)
                bool Ruled(Windows.Foundation.Rect a, Windows.Foundation.Rect b)
                {
                    int ya = (int)(crop.Y + Math.Min(a.Y, b.Y) / k), yb = (int)(crop.Y + Math.Max(a.Y + a.Height, b.Y + b.Height) / k);
                    for (int x = (int)(crop.X + (a.X + a.Width) / k) + 1; x < (int)(crop.X + b.X / k); x++)
                    {
                        int dark = 0;
                        for (int y = ya; y <= yb; y++) if (x < page.Width && y < page.Height && page.GetPixel(x, y).GetBrightness() < 0.5f) dark++;
                        if (dark >= 0.85 * (yb - ya + 1)) return true;
                    }
                    return false;
                }
                int from = 0;
                for (int j = 1; j <= all.Count; j++)
                {
                    if (j < all.Count && all[j].BoundingRect.X - (all[j - 1].BoundingRect.X + all[j - 1].BoundingRect.Width) <= 1.2 * em
                        && !Ruled(all[j - 1].BoundingRect, all[j].BoundingRect)) continue;
                    var words = all.GetRange(from, j - from);
                    from = j;
                    var r = words.Select(x => x.BoundingRect).ToList();
                    float x0 = (float)(crop.X + r.Min(q => q.X) / k), y0 = (float)(crop.Y + r.Min(q => q.Y) / k);
                    float x1 = (float)(crop.X + r.Max(q => q.X + q.Width) / k), y1 = (float)(crop.Y + r.Max(q => q.Y + q.Height) / k);
                    // (the words joined by their gaps: the OCR makes a word of each Japanese character, a space
                    // between two of them only where the print leaves a wider gap than between characters)
                    var text = new StringBuilder(words[0].Text);
                    for (int w = 1; w < words.Count; w++)
                    {
                        string prev = words[w - 1].Text, cur = words[w].Text;
                        double gap = r[w].X - (r[w - 1].X + r[w - 1].Width);
                        bool close = (IsCjk(prev[^1]) || IsCjk(cur[0])) && gap < 0.3 * em;
                        text.Append(close ? "" : " ").Append(cur);
                    }
                    string tidy = Tidy(text.ToString());
                    if (Trace) Console.WriteLine($"  ocr[{e.RecognizerLanguage.LanguageTag}]: \"{text}\" -> \"{tidy}\" at {x0:F0},{y0:F0} {x1 - x0:F0}x{y1 - y0:F0}");
                    if (tidy.Length > 0) lines.Add((tidy, new RectangleF(x0, y0, x1 - x0, y1 - y0)));
                }
            }
            return lines;
        }
        var read = Lines(engine);
        // the user's language first; where another installed one reads a line too, a line of few
        // Japanese or Chinese characters is taken from a language that writes none (a Japanese
        // engine reads "Jesu, meine Freude" as "es 収 meine Freude"; with English installed, English reads it)
        static bool CjkLanguage(string tag) => tag.StartsWith("ja") || tag.StartsWith("zh") || tag.StartsWith("ko");
        if (!CjkLanguage(engine.RecognizerLanguage.LanguageTag)) return read;
        // (a Japanese PC mostly has no other engine: a line of the Japanese engine's with a character
        // of its script set in a Latin word, "es収 meine Freude" for the title "Jesu, meine Freude"
        // in the band over bwv610's first system, is read again alone, and the lone reading taken
        // when it holds no such character: by itself the line reads "Jesu, meine Freude")
        // (the same engine reads "Tempo di Marcia." as "TemD0 di Marcia." in the band over a scan's
        // first system and "Tempo di 1Ma1 ℃ ia." in the band of the tempo, right by itself: a
        // line with any such oddity is read alone, and the lone reading taken when it has fewer)
        if (again)
            for (int i = 0; i < read.Count; i++)
            {
                int odd = Oddities(read[i].Text);
                if (odd == 0) continue;
                string alone = Alone(page, read[i].Box, scale > 0 ? scale : Scale(page.Width));
                if (Trace) Console.WriteLine($"  ocr: \"{read[i].Text}\" ({odd} odd) read alone: \"{alone}\" ({(alone.Length > 0 ? Oddities(alone) : 0)} odd)");
                if (alone.Length > 0 && Oddities(alone) < odd) read[i] = (alone, read[i].Box);
            }
        foreach (var lang in OcrEngine.AvailableRecognizerLanguages.Where(l => !CjkLanguage(l.LanguageTag)))
        {
            if (OcrEngine.TryCreateFromLanguage(lang) is not OcrEngine other) continue;
            var theirs = Lines(other);
            for (int i = 0; i < read.Count; i++)
            {
                int cjk = Regex.Matches(read[i].Text, $"[{Cjk}]").Count, letters = Regex.Matches(read[i].Text, @"\p{L}").Count;
                if (cjk * 4 > letters) continue;
                var box = read[i].Box;
                var match = theirs.Where(t => RectangleF.Intersect(t.Box, box) is var x && x.Width * x.Height > 0.5 * box.Width * box.Height).ToList();
                if (match.Count == 1) read[i] = (match[0].Text, box);
            }
            break;
        }
        return read;
    }

    public const string Cjk = @"\p{IsCJKUnifiedIdeographs}\p{IsHiragana}\p{IsKatakana}ー々";

    /// <summary>
    /// What a Japanese engine's reading of a Latin line gets wrong, counted: a character of its own
    /// script against a Latin letter ("es収"), a digit against a letter inside a word ("TemD0",
    /// "1Ma1"; not 8va, 15ma or an ordinal, 1st), a symbol against a letter ("℃ia").
    /// </summary>
    internal static int Oddities(string text)
    {
        int n = Regex.Matches(text, $"[A-Za-z][{Cjk}]|[{Cjk}][A-Za-z]").Count;
        n += Regex.Matches(text, @"[A-Za-z][^\p{L}\p{N}\p{P}\s]|[^\p{L}\p{N}\p{P}\s][A-Za-z]").Count;
        foreach (var word in text.Split((char[])null, StringSplitOptions.RemoveEmptyEntries))
        {
            string w = word.Trim('.', ',', ';', ':', '(', ')', '"', '\'');
            if (Regex.IsMatch(w, @"^(8v[ab]|15m[ab]|\d+(st|nd|rd|th))$", RegexOptions.IgnoreCase)) continue;
            n += Regex.Matches(w, @"[A-Za-z]\d|\d[A-Za-z]").Count;
        }
        return n;
    }

    // (the OCR's Roman numerals as one character each, "Ⅱ", to letters: a section's name is an identifier)
    static readonly Dictionary<char, string> Roman = new()
    {
        ['Ⅰ'] = "I", ['Ⅱ'] = "II", ['Ⅲ'] = "III", ['Ⅳ'] = "IV", ['Ⅴ'] = "V", ['Ⅵ'] = "VI", ['Ⅶ'] = "VII", ['Ⅷ'] = "VIII", ['Ⅸ'] = "IX", ['Ⅹ'] = "X", ['Ⅺ'] = "XI", ['Ⅻ'] = "XII",
        ['ⅰ'] = "i", ['ⅱ'] = "ii", ['ⅲ'] = "iii", ['ⅳ'] = "iv", ['ⅴ'] = "v", ['ⅵ'] = "vi", ['ⅶ'] = "vii", ['ⅷ'] = "viii", ['ⅸ'] = "ix", ['ⅹ'] = "x", ['ⅺ'] = "xi", ['ⅻ'] = "xii",
    };

    // (no space before Japanese closing punctuation or after its opening. In a Latin word the OCR
    // mistakes 0 for o, and l, I and 1 for one another: I between
    // small letters or ending a word after one is an l ("DeI"), so is I between a capital and a small
    // letter ("FIöten"), 1 between letters an l, a word's l before a consonant an I ("lch", "lm"); a
    // capital ending a word of small letters is small ("BarriO"), one split from its word joins it
    // ("S chumann"). A dash run at a line's end is a rule under the title)
    static bool IsCjk(char c) => Regex.IsMatch(c.ToString(), $"[{Cjk}、。，．・「」『』（）]");

    static string Tidy(string text)
    {
        text = string.Concat(text.Select(c => Roman.TryGetValue(c, out var r) ? r : c.ToString()));
        text = Regex.Replace(text, @"(?<=\s|^)\s+|\s+(?=[、。，．」』）])", "");
        text = Regex.Replace(text, @"(?<=[「『（])\s+", "");
        text = Regex.Replace(text, @"(?<=\p{L})0(?=\p{L})", "o");
        // (a small letter split from the rest of its word: "p astorale"; not a, e, i, o, y, words of their own)
        text = Regex.Replace(text, @"(?<=\b[b-df-hj-np-xz])\s(?=\p{Ll}{2,})", "");
        text = Regex.Replace(text, @"(?<=\p{Ll})I+(?=\p{Ll}|\b)", m => new string('l', m.Length));
        text = Regex.Replace(text, @"(?<=[a-zA-Z])、(?=[a-z0-9])", "i");
        text = Regex.Replace(text, @"(?<=\b\p{L})0\b", "o");
        text = Regex.Replace(text, @"(?<=\b\p{Lu})I(?=\p{Ll})", "l");
        // (a capital split from the rest of its word: "S chumann"; not A, I or O, words of their own)
        text = Regex.Replace(text, @"(?<=\b[B-HJ-NP-Z])\s(?=\p{Ll}{2,})", "");
        text = Regex.Replace(text, @"(?<=\p{L})1(?=\p{L})", "l");
        text = Regex.Replace(text, @"\bl(?=[bcdfghjkmnpqrstvwxz])", "I");
        text = Regex.Replace(text, @"(?<=\b\p{Lu}?\p{Ll}{2,})\p{Lu}\b", m => m.Value.ToLowerInvariant());
        text = Regex.Replace(text, @"\s*[—–\-_]{2,}\s*$", "");
        // (a dash between Latin words the Japanese engine reads as a long vowel mark, "Tchaikovsky -ーAlbum")
        text = Regex.Replace(text, @"(?<=[A-Za-z0-9)])\s*[-‐]?ー\s*(?=[A-Za-z0-9(])", " — ");
        // (spaces the OCR puts inside brackets: "( 1810 - 1849 )")
        text = Regex.Replace(text, @"\(\s+", "(");
        text = Regex.Replace(text, @"\s+(?=[),.;:!?])", "");
        return Regex.Replace(text, @"\s+", " ").Trim();
    }

    /// <summary>A .lys string's text: quotes and backslashes escaped.</summary>
    public static string Quote(string text) => "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
}
