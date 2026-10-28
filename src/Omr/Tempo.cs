using System.Drawing;
using System.Text.RegularExpressions;

namespace Omr;

/// <summary>
/// The words over each system's first staff, read with Windows' own OCR as the header is (Header):
/// a tempo ("Andantino (♩. = 52)", "Più mosso, agitato", "♩ = 120") and a rehearsal mark (a word or
/// letter in a box), each at the bar it stands over. A metronome mark's note is a glyph the OCR does
/// not read: its value is the meter's beat (a dotted quarter in 6/8, a half in 2/2), as most music
/// sets it.
/// </summary>
static class Tempo
{
    /// <summary>Every line read over the systems, on the console (--words).</summary>
    public static bool Verbose;

    /// <summary>What stands over a bar: a tempo's words and metronome number (either may be missing), or a rehearsal mark's words.</summary>
    public sealed record Mark(int Bar, string Words, int? Bpm, string Rehearsal, RectangleF Box)
    {
        /// <summary>An octave line's: octaves up (8va 1, 15ma 2; 8vb -1, 15mb -2), the staff's part, where its dashes end (pixels of the page as loaded) and in which bar.</summary>
        public int Ottava; public int Part; public double EndX; public int EndBar;
        /// <summary>An octave line's (the detector's, Ottavas): where the notes under it start (page pixels; NaN: a little left of Box), whether it runs on into the next system unclosed, and whether it is the piece going on from one (not started again).</summary>
        public double StartX = double.NaN; public bool NoEnd, Continued;
        /// <summary>An octave line's system's last bar: the notes it moves are its system's (the next system's stand at the same x).</summary>
        public int LastBar = int.MaxValue;
        /// <summary>An octave line run out past its page's last system (left open for the next page): the events its end goes on should that page not go on with it.</summary>
        public List<Ev> Ends;
        /// <summary>A jump's or an end's words over the staff (S22), as the form writes them: fine, dcalfine, dsalfine, dsalcoda, dcalcoda, tocoda, ds, dc; Bar is the bar they follow.</summary>
        public string Nav;
        /// <summary>Whether this is a tempo marking (not a rehearsal mark, an octave line or a jump's words).</summary>
        public bool IsTempo => Rehearsal == null && Ottava == 0 && Nav == null;
    }

    /// <summary>
    /// A jump's or an end's words (S22), as the form writes them: "Fine" → fine, "D.C. al Fine" →
    /// dcalfine, "D.S. al Coda" → dsalcoda, "To Coda" → tocoda, "D.S." → ds, "D.C." → dc, and the
    /// words written out (Da Capo, Dal Segno); null for any other text. Compared without case, points
    /// and spaces, with the OCR's figures for letters (a1 for al), a letter's change allowed in the
    /// long ones; a line with a character of the Japanese engine's own script is no reading of them.
    /// </summary>
    public static string NavOf(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        string t = text.ToLowerInvariant().Trim();
        bool junk = Regex.IsMatch(t, $"[{Header.Cjk}]");
        string Letters(string x) => Regex.Replace(x, @"[^a-z0-9]", "").Replace('1', 'l').Replace('0', 'o');
        string letters = Letters(t);
        // "D.C." or "D.S." and what follows: "al Coda" (which a Japanese engine reads, the al as a kanji
        // now and then: "D. S. 記 Coda"), "al Fine" (of which it makes kanji and an e: "D. C. al 窺れe",
        // "D. C. e"), or nothing
        var m = Regex.Match(t, @"^[\s.,]*d[\s.,]*([cs])[\s.,]*(.*)$");
        if (m.Success && letters.Length >= 2)
        {
            string p = "d" + m.Groups[1].Value, tail = Letters(m.Groups[2].Value);
            if (tail.StartsWith("al")) tail = tail[2..];
            if (tail.Length == 0 && !junk) return p;
            if (Near(tail, "coda") || tail.EndsWith("coda")) return p + "alcoda";
            if (Near(tail, "fine") || tail.EndsWith("fine") || junk || tail.EndsWith("e")) return p + "alfine";
            return p;
        }
        if (letters.StartsWith("dalsegno") || letters.StartsWith("dacapo"))
        {
            string p = letters.StartsWith("dalsegno") ? "ds" : "dc", tail = letters[(p == "ds" ? 8 : 6)..];
            if (tail.StartsWith("al")) tail = tail[2..];
            return tail.Length == 0 ? p : Near(tail, "coda") ? p + "alcoda" : Near(tail, "fine") ? p + "alfine" : p;
        }
        if (letters == "tocoda" || (letters.EndsWith("coda") && letters.Length <= 7 && letters.StartsWith("t"))) return "tocoda";
        if (Near(letters, "fine")) return "fine";
        // ("Fine" as the Japanese engine reads it: kanji or kana for the Fin and an e, "窺れe")
        if (junk && letters.Length <= 2 && letters.EndsWith("e")) return "fine";
        return null;
    }

    // (a word within a letter's change of the one meant, as long as it within one)
    static bool Near(string a, string b) => a == b || (Math.Abs(a.Length - b.Length) <= 1 && Distance(a, b) <= 1);

    /// <summary>
    /// A row of letters read by itself for a jump's or an end's words (<see cref="NavOf"/>): cut out
    /// on white at its own size and at one and a half, two and three times it (the band's OCR passes
    /// over a row of small italic Latin print at the page's scale, 25 pixels tall: a Japanese engine
    /// reads nothing of "D.S. al Coda" there, and reads it at its own size and larger). The readings
    /// together: one with the Coda or the Fine read says more than one of "D.S." alone.
    /// </summary>
    static string NavAlone(Bitmap page, RectangleF row, IEnumerable<RectangleF> masks)
    {
        var kinds = new List<string>();
        foreach (double k in new[] { 1.0, 1.5, 2.0, 3.0 })
        {
            string again = Header.Alone(page, row, k, 0.5, masks);
            if (Verbose) Console.WriteLine($"  nav again at {k:F1}: \"{again}\" -> {NavOf(again) ?? "-"}");
            if (NavOf(again) is string nav) kinds.Add(nav);
        }
        if (kinds.Count == 0) return null;
        return kinds.FirstOrDefault(x => x.EndsWith("alcoda")) ?? kinds.FirstOrDefault(x => x.EndsWith("alfine")) ?? kinds.GroupBy(x => x).OrderByDescending(g => g.Count()).First().Key;
    }

    /// <summary>
    /// A box over a system's first staff (a rehearsal mark's): its system, its rules' outer edges on
    /// the working image and on the page as loaded, the rules' thickness, and the one piece of ink
    /// inside it when there is one (a letter: a word is several), with that piece's features for the
    /// classifier (family "l:").
    /// </summary>
    /// <param name="Grid">a box with a grid in it, a lead sheet's chord diagram: no mark, but what the band over the staff reaches over</param>
    public sealed record Frame(int System, Rectangle Dark, RectangleF Box, double Rule, Comp Letter, double[] F, bool Grid = false);

    /// <summary>
    /// The nearest-sample distance a letter's classification is trusted within: the generated
    /// pages' letters are at 0, a scanned band chart's (Fantasy) at 2.0-4.3, every one of them
    /// right (the time-signature digits' bound is 3.0).
    /// </summary>
    public static double LetterDist = 5.0;

    /// <summary>The boxes over every system of a page, found once (<see cref="Frames"/>).</summary>
    public static List<Frame> FramesOf(PageResult res)
    {
        if (res.Frames != null) return res.Frames;
        return res.Frames = FramesOf(res.Dark, res.S, res.Systems);
    }

    /// <summary>The boxes over every system of a page (<see cref="Frames"/>), from the dark image and the systems (Symbols.Find, before there is a PageResult).</summary>
    public static List<Frame> FramesOf(Img dark, double s, List<SystemInfo> systems)
    {
        var frames = new List<Frame>();
        Staff above = null;     // the system before's lowest staff
        foreach (var sys in systems.Where(x => x.Measures > 0 && x.Staves.Count > 0))
        {
            frames.AddRange(Frames(dark, s, sys, above));
            above = sys.Staves[^1];
        }
        return frames;
    }

    /// <summary>
    /// Whether a line of text may be an octave line's sign, and how many octaves: "8va", "8ve", "15ma"
    /// as read (Sure), or a short word starting with 8 or 15 (the OCR reads a bold italic 8va as
    /// "820"), which only its dashes then confirm.
    /// </summary>
    public static (int Up, bool Sure) OttavaOf(string text)
    {
        string t = text.Trim();
        var sure = Regex.Match(t, @"^(8|15)\s*(v[aeα]|m[aα])\b", RegexOptions.IgnoreCase);
        if (sure.Success) return (sure.Groups[1].Value == "15" ? 2 : 1, true);
        var maybe = Regex.Match(t, @"^(15|8)\S{0,3}$");
        return maybe.Success ? (maybe.Groups[1].Value == "15" ? 2 : 1, false) : (0, false);
    }

    /// <summary>
    /// Where an octave line's dashes end: from its sign rightwards, along the band of the sign's
    /// height, the last ink before a gap of three of its heights (a dash's gap is less than one).
    /// </summary>
    static double DashesEnd(Bitmap page, RectangleF sign)
    {
        bool Dark(int x, int y) => x >= 0 && y >= 0 && x < page.Width && y < page.Height && page.GetPixel(x, y).GetBrightness() < 0.5f;
        int y0 = (int)(sign.Top + 0.3 * sign.Height), y1 = (int)sign.Bottom, gap = (int)(3 * sign.Height);
        double last = sign.Right;
        for (int x = (int)sign.Right + 1; x < page.Width && x - last < gap; x++)
            for (int y = y0; y <= y1; y++)
                if (Dark(x, y)) { last = x; break; }
        return last;
    }

    // (words that begin a tempo marking, small, any language the music is in)
    static readonly string[] Words =
    {
        "adagi", "andant", "allegr", "lento", "lent ", "largo", "larghetto", "moderat", "presto", "prestissimo", "vivace", "vivo", "grave",
        "maestoso", "animato", "agitato", "più", "piu ", "meno", "tempo", "langsam", "mäßig", "massig", "lebhaft", "schnell", "ruhig",
        "bewegt", "rasch", "innig", "sehr ", "ziemlich", "nicht ", "con moto", "comodo", "sostenuto", "allant", "modéré", "vif", "animé",
        "très", "assez", "ゆっくり", "ゆつくり", "速く", "中くらい", "やや",
        "slow", "fast", "bright", "lively", "gentl", "freely", "rubato", "broad", "moderately", "quick", "calm", "majestic", "swing", "steady",
    };

    // (a metronome mark's figures as the Japanese engine reads them: its = as 一 or 二, a 0 among the
    // figures as の or O: "一 132)", "= 24の")
    static string Figures(string text) => Regex.Replace(text, @"(?<=\d)[のO〇o](?=\d|\b)", "0");
    const string EqualsSign = @"[=一二]";

    /// <summary>Whether a line of text is a tempo marking: a metronome mark, or words that begin one ("a tempo", a return to it, is not).</summary>
    public static bool Is(string text)
    {
        string t = Figures(text).Trim().ToLowerInvariant();
        if (Regex.IsMatch(t, EqualsSign + @"\s*\d{2,3}\b")) return true;
        // (short of what the OCR put before the words: "0 Largo")
        t = Regex.Replace(t, @"^[^\p{L}]+", "");
        if (t.StartsWith("a tempo")) return false;
        // ("a tempo" as the Japanese engine reads its italic now and then, "0 temPO": tempo alone, after a
        // character or none, is no tempo marking; "Tempo I" and "Tempo primo" are)
        if (Regex.IsMatch(text.Trim(), @"^\S?\s*tempo$", RegexOptions.IgnoreCase)) return false;
        // (a word the OCR broke in two, "Alle gretto": as joined too)
        string joined = Regex.Replace(t, @"(?<=\p{L})\s+(?=\p{L})", "");
        return Words.Any(w => t.StartsWith(w) || (t + " ").StartsWith(w) || joined.StartsWith(w));
    }

    /// <summary>A tempo line's words and number: "Andantino (J. = 52)" → ("Andantino", 52); "J = 120" → (null, 120).</summary>
    public static (string Words, int? Bpm) Parse(string text)
    {
        text = Figures(text);
        // (in a line of tempo words, a dash before the figures is the = too: "Vivaceひ - 240")
        var m = Regex.Match(text, EqualsSign + @"\s*(\d{2,3})\b");
        if (!m.Success) m = Regex.Match(text, @"(?<=\s)[-–—]\s*(\d{2,3})\b");
        int? bpm = m.Success ? int.Parse(m.Groups[1].Value) : null;
        // (the words: up to the metronome mark's bracket or its note; what the OCR made of the note dropped)
        string words = Regex.Replace(text, @"\s*[\(（].*$", "");
        if (m.Success) words = words.Length > m.Index ? words[..m.Index] : words;
        words = Regex.Replace(words, @"\s*\S{0,3}\s*" + EqualsSign + @"\s*\d{2,3}.*$", "");
        // (and the OCR's stray marks at the ends: "\llegro", a bracket, a dash under it)
        words = words.Trim(' ', ',', '-', '–', '—', '\\', '/', '|', '"', '\'', '.', ':', ';', '~', '_', '「', '」', '。', '、', '・', '(', ')', '[', ']');
        if (!Regex.IsMatch(words, @"\p{L}{2}")) words = null;
        // (words none of which is the vocabulary's, with a character of the Japanese engine's own
        // script or punctuation among the letters, are no reading of the print: "VI、。。。" for Vivace)
        if (words != null && Known(words) == 0 && Regex.IsMatch(words, $"[A-Za-z][{Header.Cjk}。、・「」]|[{Header.Cjk}。、・「」][A-Za-z]")) words = null;
        // (a word the OCR broke in two, "Mo derato", "Adagi 0": the vocabulary's word when joined it
        // is one, letter for letter but for a 0 as an o (not within a letter's change: "Con moto" is
        // no "Comodo"); and a lone 0 at either end of known words is the OCR's, "Allegretto 0")
        if (words != null && Regex.Replace(words, @"(?<=[\p{L}0])\s+(?=[\p{L}0])", "") is string joined && joined != words
            && Vocabulary.FirstOrDefault(v => Bare(v) == Bare(joined)) is string whole) words = (char.IsUpper(words[0]) ? char.ToUpperInvariant(whole[0]) : char.ToLowerInvariant(whole[0])) + whole[1..];
        if (words != null && Regex.Replace(words, @"^0\s+|\s+0$", "") is string bare && bare != words && Known(bare) > 0) words = bare;
        // (a tempo marking starts with a capital: "con moto" the OCR's; "Tempo I", the first tempo again, is set with the numeral, which the OCR reads as 1 or l)
        if (words != null) { words = Snap(words); if (char.IsLower(words[0])) words = char.ToUpperInvariant(words[0]) + words[1..]; }
        if (words != null) words = Regex.Replace(words, @"\b(Tempo) [1l]\b", "$1 I");
        return (words, bpm);
    }

    // the words of tempo and character the OCR is put right to (a Japanese engine reading Latin print:
    // "Andant、00", "agltato", "Piü")
    static readonly string[] Vocabulary =
    {
        "Adagio", "Adagietto", "Andante", "Andantino", "Allegro", "Allegretto", "Lento", "Largo", "Larghetto", "Moderato", "Presto",
        "Prestissimo", "Vivace", "Vivo", "Grave", "Maestoso", "Animato", "Agitato", "agitato", "Più", "più", "mosso", "Meno", "meno", "Tempo",
        "tempo", "primo", "Langsam", "langsam", "Mäßig", "mäßig", "bewegt", "Lebhaft", "lebhaft", "Schnell", "schnell", "Ruhig", "ruhig",
        "Rasch", "Innig", "innig", "Sehr", "sehr", "Ziemlich", "ziemlich", "Nicht", "nicht", "moto", "Con", "con", "comodo", "sostenuto",
        "cantabile", "espressivo", "dolce", "tranquillo", "giocoso", "scherzando", "rubato", "molto", "poco", "assai", "troppo", "valse",
        "Theme", "Coda", "Trio", "Intro", "Introduction", "Return", "Reprise", "Finale", "Verse", "Chorus", "Bridge",
        "Outro", "Interlude", "Refrain", "Solo", "Ending", "Tag", "Vamp", "Cadenza", "Exposition", "Development", "Recapitulation",
        "Variation", "Var", "Scherzo", "Minore", "Maggiore", "Rondo", "Menuetto", "Minuet", "Head",
        "brio", "fuoco", "spirito", "anima", "energico", "grazioso", "leggiero", "pesante", "risoluto", "sempre", "subito",
        "ritenuto", "accelerando", "rallentando", "ritardando", "Doppio", "movimento", "Alla", "alla", "marcia", "Marcia",
        "misurato", "furioso", "appassionato", "semplice", "brillante", "deciso", "feroce", "nobile", "giusto", "largamente",
        "Allegramente", "Allegrissimo", "Adagissimo", "Vivacissimo", "Pastorale", "Marciale", "Burlesca", "Capriccio",
        "Slowly", "Slower", "Faster", "Brightly", "Lively", "Gently", "Freely", "Rubato", "Broader", "Moderately", "Quickly", "Calmly",
        "Majestic", "Swing", "Steady", "With", "with", "feeling", "motion", "energy",
    };

    /// <summary>
    /// Each word within a letter's change of a word of the vocabulary (two, at seven letters or more;
    /// an accent only, at three), spelt as it, its first letter's case as read.
    /// </summary>
    public static string Snap(string text) => Regex.Replace(text, @"[\p{L}\p{N}、]{3,}", m =>
    {
        string w = m.Value;
        if (Vocabulary.Contains(w)) return w;
        int limit = w.Length >= 7 ? 2 : w.Length >= 4 ? 1 : 0;
        var best = Vocabulary.Select(v => (v, d: Bare(w) == Bare(v) ? 0 : Distance(Bare(w), Bare(v)))).Where(x => x.d <= limit)
                             .OrderBy(x => x.d).ThenBy(x => x.v[0] == w[0] ? 0 : 1).FirstOrDefault();
        if (best.v == null) return w;
        return (char.IsUpper(w[0]) ? char.ToUpperInvariant(best.v[0]) : char.ToLowerInvariant(best.v[0])) + best.v[1..];
    });

    // (a word without its accents and case: "Piü" and "più" alike, and the OCR's figures for letters,
    // "Adag10" as "adagio". By a table: the reader runs with invariant globalization, where Unicode's
    // decomposition leaves an accented letter as it is)
    const string Accented = "àáâäãåāèéêëēìíîïīòóôöõøōùúûüūñçýÿ01、", Plain = "aaaaaaaeeeeeiiiiiooooooouuuuuncyyoii";
    static string Bare(string w) => new string(w.ToLowerInvariant().Select(c => Accented.IndexOf(c) is int i and >= 0 ? Plain[i] : c).ToArray());

    // (the edit distance, letters compared without case)
    static int Distance(string a, string b)
    {
        a = a.ToLowerInvariant(); b = b.ToLowerInvariant();
        var d = new int[a.Length + 1, b.Length + 1];
        for (int i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (int j = 0; j <= b.Length; j++) d[0, j] = j;
        for (int i = 1; i <= a.Length; i++)
            for (int j = 1; j <= b.Length; j++)
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
        return d[a.Length, b.Length];
    }

    /// <summary>The meter's beat as a .lys duration: a dotted quarter in 3/8, 6/8, 9/8, 12/8, else the meter's unit (a half in 2/2).</summary>
    public static string Beat(string time)
    {
        var p = time.Split('/');
        int num = int.Parse(p[0]), den = int.Parse(p[1]);
        // (3/8 too, a bar to the beat, as a dotted quarter)
        if (den == 8 && num % 3 == 0) return "4.";
        if (den == 16 && num % 3 == 0) return "8.";
        return den.ToString();
    }

    /// <summary>The .lys tempo statement's arguments: "\"Andante\" 4 = 72", "\"Andante\"", "4 = 72".</summary>
    public static string Lys(string words, int? bpm, string time) =>
        string.Join(" ", new[] { words == null ? null : Header.Quote(words), bpm == null ? null : $"{Beat(time)} = {bpm}" }.Where(x => x != null));

    /// <summary>
    /// The tempo markings and rehearsal marks over each system of a page (its file as loaded and its
    /// reading): the band from sixteen spaces (short of three spaces under the system before, and of
    /// the headings over the first system, <paramref name="header"/>) to a third of a space over the
    /// system's first staff, read system by system; a line's bar the one its left end stands over (the
    /// system's first, left of it).
    /// </summary>
    public static List<Mark> Read(string path, PageResult res, Header header = null)
    {
        var marks = new List<Mark>();
        if (res.Systems.Count == 0) return marks;
        double s = res.S;
        try
        {
            using var page = new Bitmap(path);
            var allFrames = FramesOf(res);
            Staff above = null;     // the system before's lowest staff: the band stops three spaces under it (its pedal marks, its words)
            var systemsOn = res.Systems.Where(x => x.Measures > 0 && x.Staves.Count > 0).ToList();
            foreach (var sys in systemsOn)
            {
                var top = sys.Staves[0];
                var grids = allFrames.Where(f => f.System == sys.Index && f.Grid).ToList();
                var frames = allFrames.Where(f => f.System == sys.Index && !f.Grid).ToList();
                // (a lead sheet's chord diagrams over the staff: its chord names, tempo and marks stand
                // over them, and the band reaches sixteen spaces over the diagrams)
                // (sixteen spaces: a tempo stacked over a rehearsal mark's box, over a bar of notes high
                // on ledger lines, stands eight to twelve spaces over the staff, the Lab's piano-collide's
                // "Allegro con brio" and "Vivace"; nine cut them off)
                double over = grids.Count > 0 ? Math.Min(top.Top, grids.Min(g => g.Dark.Top)) : top.Top;
                double reach = Math.Min(16 * s + (top.Top - over), above == null ? double.MaxValue : top.Top - above.Bottom - 3 * s);
                above = sys.Staves[^1];
                var n0 = res.Dark.ToSource(top.Left, top.Top - 4 * s);
                double sp = Math.Abs(res.Dark.ToSource(top.Left, top.Top - 5 * s).Y - n0.Y);     // (a space on the page as loaded)
                // (the bars' left ends on the page as loaded, of this system and of the next)
                List<double> BarsOf(SystemInfo x) => x.Bars.Select(v => res.Dark.ToSource(v, x.Staves[0].Top).X).ToList();
                var bars = BarsOf(sys);
                int BarAt(double x) { int m = 0; while (m + 1 < sys.Measures && x >= bars[m + 1] - 0.5 * s) m++; return sys.BarOf(m); }
                var next = systemsOn.ElementAtOrDefault(systemsOn.IndexOf(sys) + 1);
                // (the boxes beside a row, painted white in its cut: the sonatina's "Exposition" box read into the tempo beside it)
                var masks = frames.Select(f => f.Box).ToList();
                // a jump's or an end's words (S22), "Fine", "D.C. al Fine", "D.S. al Coda": a row of letters
                // whose right end stands at a barline (within two spaces; not the system's first), read by
                // itself (NavAlone), the mark after the bar before that line. Lily# sets the jumps' words
                // under the system's lowest staff and "Fine" and "To Coda" over the first, their right end
                // at the barline of the bar they follow: a Fine found under a system, within five spaces
                // of the next, is the next system's. A row the band of the system before marked already
                // (the bands overlap between the systems) is marked once
                bool NavRow(RectangleF row, string read, bool under)
                {
                    if (marks.Any(m => m.Nav != null && RectangleF.Intersect(m.Box, row) is var o && o.Width * o.Height > 0.5 * row.Width * row.Height)) return true;
                    int Line(List<double> xb) { int j = Enumerable.Range(1, xb.Count - 1).OrderBy(i => Math.Abs(xb[i] - row.Right)).First(); return Math.Abs(xb[j] - row.Right) <= 2 * sp ? j : -1; }
                    int here = Line(bars);
                    var nextBars = next == null ? null : BarsOf(next);
                    int there = under && next != null && res.Dark.ToSource(next.Staves[0].Left, next.Staves[0].Top).Y - row.Bottom <= 5 * sp ? Line(nextBars) : -1;
                    if (here < 0 && there < 0) return false;
                    string nav = NavOf(read) ?? NavAlone(page, row, masks);
                    if (nav == null) return false;
                    var (target, k) = nav is "fine" or "tocoda" && there >= 0 ? (next, there) : here >= 0 ? (sys, here) : (next, there);
                    if (Verbose) Console.WriteLine($"  nav s{target.Index + 1}: {nav} at {row.X:F0},{row.Y:F0} {row.Width:F0}x{row.Height:F0} -> after bar {target.BarOf(k - 1)}");
                    marks.Add(new Mark(target.BarOf(k - 1), null, null, null, row) { Nav = nav });
                    return true;
                }
                // (the band under the lowest staff: from a third of a space under it to sixteen, short of
                // the next system's first staff and of the page's foot. Sixteen: the jump's words stand
                // under the staff's dynamics, hairpins and words, roadsong's "D.S. al Coda" nine spaces
                // under the last system. The rows at a barline only: the dynamics and the pedal marks
                // under the staff stand at their notes)
                {
                    var bottom = sys.Staves[^1];
                    double under = Math.Min(Math.Min(bottom.Bottom + 16 * s, res.Dark.H - 1), next == null ? double.MaxValue : next.Staves[0].Top - 0.3 * s);
                    if (under - bottom.Bottom >= 2 * s)
                        foreach (var row in Rows(res, (int)bottom.Left, (int)(bottom.Bottom + 0.3 * s), bottom.Right, (int)under, allFrames))
                        {
                            if (Verbose) Console.WriteLine($"  row under s{sys.Index + 1}: at {row.X:F0},{row.Y:F0} {row.Width:F0}x{row.Height:F0}");
                            NavRow(row, null, under: true);
                        }
                }
                if (reach < 2 * s) continue;
                var a = res.Dark.ToSource(top.Left - 6 * s, top.Top - reach);
                var b = res.Dark.ToSource(top.Right, top.Top - 0.3 * s);
                var crop = Rectangle.FromLTRB((int)Math.Floor(Math.Min(a.X, b.X)), (int)Math.Floor(Math.Min(a.Y, b.Y)), (int)Math.Ceiling(Math.Max(a.X, b.X)), (int)Math.Ceiling(Math.Max(a.Y, b.Y)));
                // (over the first system, short of the headings the header placed, the title and the
                // subtitle: the tempo stands under them. Not the composer's or the poet's line: a tempo
                // may stand level with them)
                if (header != null && sys == res.Systems.First(x => x.Measures > 0 && x.Staves.Count > 0) && header.Lines.Any(l => l.Role is "title" or "subtitle"))
                {
                    int floor = (int)Math.Ceiling(header.Lines.Where(l => l.Role is "title" or "subtitle").Max(l => l.Box.Bottom) + 0.3 * sp);
                    if (floor > crop.Top) crop = Rectangle.FromLTRB(crop.Left, floor, crop.Right, Math.Max(floor, crop.Bottom));
                    if (crop.Height < 2 * sp) continue;
                }
                // (a boxed line is a rehearsal mark as it is: the others joined by rows)
                var lines = Header.Recognize(page, crop);
                if (Verbose) Console.WriteLine($"  band s{sys.Index + 1}: {crop.Left},{crop.Top}-{crop.Right},{crop.Bottom}: {string.Join(" / ", lines.Select(l => $"\"{l.Text}\" at {l.Box.X:F0},{l.Box.Y:F0}"))}");
                // the band just over the staff again in windows twenty spaces wide, five spaces overlapping:
                // in the whole band, among the notes, the OCR passes over a short line (a metronome mark
                // alone, "♩ = 116"), which it reads cut out. A window's line the band's lines do not
                // already cover is taken, and one with a metronome number takes the place of one without
                for (double x = crop.Left; x < crop.Right - 5 * sp; x += 15 * sp)
                {
                    var tile = Rectangle.FromLTRB((int)x, (int)n0.Y, (int)Math.Min(crop.Right, x + 20 * sp), crop.Bottom);
                    foreach (var t in Header.Recognize(page, tile))
                    {
                        static float Area(RectangleF r) => r.Width * r.Height;
                        int i = lines.FindIndex(l => RectangleF.Intersect(l.Box, t.Box) is var o && Area(o) > 0.3 * Math.Min(Area(l.Box), Area(t.Box)));
                        bool number = Regex.IsMatch(Figures(t.Text), EqualsSign + @"\s*\d{2,3}");
                        if (i < 0) lines.Add(t);
                        else if (number && !Regex.IsMatch(Figures(lines[i].Text), EqualsSign + @"\s*\d{2,3}")) lines[i] = t;
                    }
                }
                // the band's rows of letters as the ink shows them (Rows), each read by itself, cut out on
                // white, at the page's scale and larger (Clearest): in the whole band the Japanese engine
                // loses a bold tempo among the music's ink (the Lab's piano-collide: "Meno mosso (♩ = 96)"
                // read as "= 96)" alone, "Vivace" as "VI、。。。", "Allegro" cut to "leg"), and reads it
                // clean by itself. The row's reading takes the place of the band's lines it covers when
                // it is as clear (Clarity) as the clearest of them
                static float AreaOf(RectangleF r) => r.Width * r.Height;
                foreach (var row in Rows(res, (int)(top.Left - 6 * s), (int)(top.Top - reach), top.Right, (int)(top.Top - 0.3 * s), allFrames))
                {
                    // (not a row mostly over the band's top, the title's; a tempo's metronome note reaches a little over it)
                    if ((row.Top + row.Bottom) / 2 < crop.Top) continue;
                    string t = Clearest(page, null, row, sp, masks);
                    if (Verbose) Console.WriteLine($"  row s{sys.Index + 1}: \"{t}\" at {row.X:F0},{row.Y:F0} {row.Width:F0}x{row.Height:F0}");
                    // (a jump's or an end's words over the staff, "Fine", at a barline: NavRow. Not a row read as a tempo, nor a printed bar number)
                    if (!(t.Length > 0 && Is(t)) && !Regex.IsMatch(t, @"^\d+$") && NavRow(row, t, under: false))
                    {
                        lines.RemoveAll(l => RectangleF.Intersect(l.Box, row) is var o && AreaOf(o) > 0.3 * Math.Min(AreaOf(l.Box), AreaOf(row)));
                        continue;
                    }
                    if (t.Length == 0) continue;
                    // (against the band's lines it covers, joined as Joined will join them: a line broken at the metronome mark's note)
                    var covered = lines.Where(l => RectangleF.Intersect(l.Box, row) is var o && AreaOf(o) > 0.3 * Math.Min(AreaOf(l.Box), AreaOf(row))).ToList();
                    if (covered.Count > 0 && Clarity(string.Join(" ", covered.OrderBy(l => l.Box.Left).Select(l => l.Text))) >= Clarity(t)) continue;
                    lines.RemoveAll(covered.Contains);
                    lines.Add((t, row));
                }
                // (what the OCR makes of the chord diagrams, "置", is no word of the music's)
                lines.RemoveAll(l => grids.Any(g => RectangleF.Intersect(l.Box, g.Box) is var o && o.Width * o.Height > 0.3 * l.Box.Width * l.Box.Height));
                // (not a chord of whole notes high over the staff: its ledger lines are rules over and under
                // what the OCR makes of its heads, the Lab's piano-collide's "00・")
                var heads = res.Sym.Heads.Where(h => h.St.System == sys.Index).Select(h => res.Dark.ToSource(h.X, h.Y)).ToList();
                // (a rehearsal mark is centred on its bar's barline: the bar that barline begins. One not
                // centred on any, within a space, starts at its bar's first note, as at a system's start,
                // after the clef and key: the bar its left end stands over)
                int BarOfMark(RectangleF box)
                {
                    double c = (box.Left + box.Right) / 2;
                    int m = Enumerable.Range(0, sys.Measures).OrderBy(i => Math.Abs(bars[i] - c)).First();
                    return Math.Abs(bars[m] - c) <= sp ? sys.BarOf(m) : BarAt(box.Left);
                }
                // a rehearsal mark's box found as ink (Frames) and its words read by themselves: the
                // inside of the box, short of its rules, on white with a space round it. In the whole
                // band the OCR passes over a boxed word now and then (the Lab's ballade's "Return",
                // read in any other cut of the band), and with a rule in the cut it reads it as a
                // letter ("IReturn", "kAgitato"); alone on white it reads it. What the band's OCR read
                // in the box gives way to it; it stays for a box read empty
                foreach (var fr in frames.Where(f => !heads.Any(h => f.Box.Contains((float)h.X, (float)h.Y))))
                {
                    var (frame, rule) = (fr.Box, fr.Rule);
                    // one piece of ink in the box, a letter: the classifier's, learnt from the letters
                    // Lily# sets in the generated band charts' boxes (the OCR reads no lone letter: a
                    // Japanese engine makes a kanji of it, "囚" for A, or nothing)
                    string text = "";
                    if (fr.Letter != null && GlyphKnn.Enabled)
                    {
                        var (label, dist) = GlyphKnn.Classify(fr.F, family: "l:");
                        if (Verbose) Console.WriteLine($"  letter s{sys.Index + 1}: {label} at {dist:F2} ({fr.Letter.W / s:F2}x{fr.Letter.H / s:F2} spaces)");
                        if (label.StartsWith("l:") && label != "l:none" && dist < LetterDist) text = label[2..];
                    }
                    bool letter = text.Length > 0;
                    // (read again with another space round it or larger when it reads nothing: the OCR
                    // passes over "Var. V" with a space round it and reads it with two; and when a
                    // Japanese engine salts a Latin word with a kana, "O てo" for "Outro", for a reading without)
                    foreach (var (margin, k) in new[] { (1.0, 1.0), (2.0, 1.0), (1.0, 1.5), (0.5, 1.0) })
                    {
                        if (letter) break;
                        string t = Alone(page, frame, rule + 0.15 * sp, margin * sp, k);
                        if (t.Length == 0) continue;
                        bool mixed = Regex.IsMatch(t, $"[{Header.Cjk}]") && Regex.IsMatch(t, @"[A-Za-z]");
                        if (text.Length == 0 || !mixed) text = t;
                        if (!mixed) break;
                    }
                    // (read empty alone, what the band's OCR read in the box, if anything with a letter or
                    // a figure: a scan's lone "F" it reads among the music now and then)
                    if (text.Length == 0 && lines.FirstOrDefault(l => RectangleF.Intersect(l.Box, frame) is var o && o.Width * o.Height > 0.5 * l.Box.Width * l.Box.Height
                                                                     && Regex.IsMatch(l.Text, @"[\p{L}\p{N}-[ー一]]")) is { Text: not null } inner)
                        text = inner.Text;
                    // (a lone letter the OCR made a kanji of is no name)
                    if (fr.Letter != null && !Regex.IsMatch(text, @"[A-Za-z0-9]")) text = "";
                    if (Verbose) Console.WriteLine($"  box s{sys.Index + 1}: \"{text}\" at {frame.X:F0},{frame.Y:F0} {frame.Width:F0}x{frame.Height:F0} rule {rule:F0}");
                    // (a box read empty is a mark still, with no name (the section is "Mark"): a Japanese
                    // engine reads no lone Latin letter, a band chart's A, B, C. Not one over the clef at
                    // the system's start, where a bar number stands, boxed in some editions)
                    if (text.Length == 0 && (frame.Left + frame.Right) / 2 < bars[0] + 3 * sp) continue;
                    marks.Add(new Mark(BarOfMark(frame), null, null, text.Length == 0 ? "" : Snap(text), frame));
                    lines.RemoveAll(l => RectangleF.Intersect(l.Box, frame) is var o && o.Width * o.Height > 0.3 * l.Box.Width * l.Box.Height);
                }
                // (with a letter or a figure in it: a volta bracket's rule over "1." under a staff line reads as a dash, "ー")
                var boxed = lines.Where(l => Regex.IsMatch(l.Text, @"[\p{L}\p{N}-[ー一]]") && Boxed(page, l.Box) && !heads.Any(h => l.Box.Contains((float)h.X, (float)h.Y))).ToList();
                foreach (var (text, box) in boxed) marks.Add(new Mark(BarOfMark(box), null, null, Snap(text), box));
                foreach (var (text, box) in Joined(lines.Except(boxed).ToList()))
                {
                    if (Verbose) Console.WriteLine($"  words s{sys.Index + 1}: \"{text}\" at {box.X:F0},{box.Y:F0} {box.Width:F0}x{box.Height:F0}{(Is(text) ? " tempo" : "")}");
                    // an octave line over the staff: its notes sound so many octaves up, to the dashes' end
                    // (a sign read unsurely is one only with its dashes running on, four of its heights or more,
                    // and clear of the staff's left end, where a printed bar number stands: s4's "15")
                    // (within nine spaces of the staff: higher up, the band reaches the system before's
                    // octave line under its lowest staff)
                    if (OttavaOf(text) is (int up and > 0, bool sure) && (sure || box.Left > bars[0] + 2 * sp) && box.Bottom > crop.Bottom - 9 * sp
                        && DashesEnd(page, box) is var end && (sure || end - box.Right > 4 * box.Height))
                    {
                        marks.Add(new Mark(BarAt(box.Left), null, null, null, box) { Ottava = up, Part = top.Part, EndX = end, EndBar = BarAt(end), LastBar = sys.FirstBar + sys.BarCount - 1 });
                        continue;
                    }
                    if (Is(text))
                    {
                        var (words, bpm) = Parse(Clearest(page, text, box, sp, masks));
                        if (words != null || bpm != null) marks.Add(new Mark(BarAt(box.Left), words, bpm, null, box));
                    }
                }
            }
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            Console.WriteLine($"tempo: no OCR ({(string.IsNullOrWhiteSpace(e.Message) ? $"0x{e.HResult:X8}" : e.Message.Trim())})");
        }
        return marks;
    }

    /// <summary>
    /// A tempo line's clearest reading: as read in the band, or read again by itself at a larger
    /// scale (1.4, 2 and 2.8 times the page's, up to 2) when that reading has more words of the
    /// vocabulary and fewer oddities. The Japanese engine reads a bold tempo in the band of a page
    /// drawn at 560 dpi as "Me110 mOSSO" and "leg, 0 00。br10 (", and the same line alone at
    /// twice and three times the scale as "Meno mosso" and "Allegro。。。brio" (the Lab's piano-collide).
    /// </summary>
    /// <param name="text">the band's reading of the line, or null for a row of letters found in the ink (<see cref="Rows"/>), read at the page's scale first</param>
    /// <param name="masks">the boxes of the rehearsal marks beside it (page pixels), painted white in the cut</param>
    static string Clearest(Bitmap page, string text, RectangleF box, double sp, IEnumerable<RectangleF> masks = null)
    {
        string best = text ?? "";
        int bestScore = text == null ? int.MinValue : Clarity(text);
        // (a line whose every word is the vocabulary's is read as well as it will be)
        if (text != null && Known(text) >= Tokens(text).Count) return text;
        double k0 = Header.Scale(page.Width);
        var scales = text == null ? new[] { k0, 1.4 * k0, 2 * k0, 2.8 * k0 } : new[] { 1.4 * k0, 2 * k0, 2.8 * k0 };
        // (a row of the ink with half an em of white round it and with a whole one: the engine reads
        // nothing of "Meno mosso (♩ = 96)" at some scales with the one and reads it with the other)
        var pads = text == null ? new[] { 0.5, 1.0 } : new[] { 0.5 };
        int tries = 0, empty = 0;
        foreach (double k in scales.Where(k => k <= 2).Distinct())
            foreach (double pad in pads)
            {
                string again = Header.Alone(page, box, k, pad, masks);
                tries++;
                // (a row read as nothing, as figures alone (a bar number) or as no tempo marking (a
                // composer's name) twice over at the page's scale is no tempo: the reading goes no
                // further. Each reading costs about 20 ms)
                if (again.Length == 0 || !Regex.IsMatch(again, @"\p{L}") || (text == null && !Is(again) && Known(again) == 0)) { if (++empty == 2 && tries == 2) return best; continue; }
                int score = Clarity(again);
                if (Verbose) Console.WriteLine($"  tempo again at {k:F2} pad {pad}: \"{again}\" ({score}; so far \"{best}\" {bestScore})");
                if (score > bestScore) { best = again; bestScore = score; }
                if (Complete(best)) return best;
            }
        return best;
    }

    /// <summary>
    /// How clear a reading of a line is: its words of the vocabulary count two each, a metronome
    /// figure one, its being a tempo marking (<see cref="Is"/>: "ゆっくりと", a word of no vocabulary,
    /// against "っくりと") one, its oddities (Header.Oddities) one against.
    /// </summary>
    static int Clarity(string text) => 2 * Known(text) + (HasFigure(text) ? 1 : 0) + (Is(text) ? 1 : 0) - Header.Oddities(text);

    static bool HasFigure(string text) => Regex.IsMatch(Figures(text), EqualsSign + @"\s*\d{2,3}\b");

    /// <summary>Whether a reading is as good as it gets: every word of it the vocabulary's, and a metronome figure.</summary>
    static bool Complete(string text) => Known(text) >= Tokens(text).Count && HasFigure(text);

    /// <summary>The words of a line: runs of three letters and figures and more, with a letter among them ("br10" is one, the OCR's "brio").</summary>
    static List<string> Tokens(string text) => Regex.Matches(text, @"[\p{L}\p{N}、]{3,}").Select(m => m.Value).Where(w => Regex.IsMatch(w, @"\p{L}")).ToList();

    /// <summary>How many words of a line are the vocabulary's, as <see cref="Snap"/> reads them.</summary>
    static int Known(string text) =>
        Tokens(text).Count(w => Vocabulary.Any(v => string.Equals(v, Snap(w), StringComparison.OrdinalIgnoreCase)));

    /// <summary>
    /// The rows of letters in the band over a system's first staff, as the ink shows them (on the
    /// page as loaded; the band on the working image from x0,y0 to x1,y1): the pieces of ink half
    /// a space to three and a half tall and up to eight wide (letters and figures, a word of letters
    /// touching, a metronome mark's note; not a chord with its stem and beam, a slur, a box or what
    /// is in it), in rows of four and more that overlap by half their height and stand within a
    /// space and a half of each other, three spaces wide and more, their pieces seven tenths of a
    /// space tall and more at the median (a row of accents is lower). The pieces stand within three
    /// spaces of each other: a metronome mark's = is two strokes too thin to be pieces, and its note
    /// stands that far from its figures.
    /// </summary>
    static List<RectangleF> Rows(PageResult res, int x0, int y0, int x1, int y1, List<Frame> frames)
    {
        var rows = new List<RectangleF>();
        double s = res.S;
        var dark = res.Dark;
        x0 = Math.Max(0, x0); y0 = Math.Max(0, y0); x1 = Math.Min(dark.W - 1, x1); y1 = Math.Min(dark.H - 1, y1);
        int w = x1 - x0 + 1, h = y1 - y0 + 1;
        if (w < 2 * s || h < 2 * s) return rows;
        var (_, comps) = Components.Label(w, h, i => dark[x0 + i % w, y0 + i / w], eight: true);
        var pieces = comps.Skip(1).Where(c => c.H >= 0.5 * s && c.H <= 3.5 * s && c.W <= 8 * s)
            .Where(c => !frames.Any(f => f.Dark.IntersectsWith(Rectangle.FromLTRB(x0 + c.MinX, y0 + c.MinY, x0 + c.MaxX + 1, y0 + c.MaxY + 1))))
            .OrderBy(c => c.MinX).ToList();
        var groups = new List<List<Comp>>();
        foreach (var c in pieces)
        {
            List<Comp> best = null;
            double bestOverlap = 0;
            foreach (var g in groups)
            {
                // (three spaces: a metronome mark's = is two strokes too thin to be pieces, and its note stands that far from its figures)
                if (c.MinX - g.Max(o => o.MaxX) > 3 * s) continue;
                int gTop = g.Min(o => o.MinY), gBot = g.Max(o => o.MaxY);
                double overlap = (Math.Min(c.MaxY, gBot) - Math.Max(c.MinY, gTop) + 1) / (double)Math.Min(c.H, gBot - gTop + 1);
                if (overlap >= 0.5 && overlap > bestOverlap) { best = g; bestOverlap = overlap; }
            }
            if (best == null) groups.Add(best = new List<Comp>());
            best.Add(c);
        }
        foreach (var g in groups)
        {
            int left = g.Min(o => o.MinX), right = g.Max(o => o.MaxX), top = g.Min(o => o.MinY), bottom = g.Max(o => o.MaxY);
            var heights = g.Select(o => o.H).OrderBy(v => v).ToList();
            if (g.Count < 4 || right - left + 1 < 3 * s || heights[heights.Count / 2] < 0.7 * s) continue;
            var a = dark.ToSource(x0 + left, y0 + top);
            var b = dark.ToSource(x0 + right + 1, y0 + bottom + 1);
            rows.Add(RectangleF.FromLTRB((float)Math.Min(a.X, b.X), (float)Math.Min(a.Y, b.Y), (float)Math.Max(a.X, b.X), (float)Math.Max(a.Y, b.Y)));
        }
        return rows;
    }

    /// <summary>
    /// The lines of one row joined: the OCR breaks a tempo at its metronome mark's note ("Andantino (",
    /// "= 52)"). Two lines join when they overlap by half the lower one's height and stand within two
    /// of their heights of each other, left to right.
    /// </summary>
    static List<(string Text, RectangleF Box)> Joined(List<(string Text, RectangleF Box)> lines)
    {
        var rest = lines.OrderBy(l => l.Box.Left).ToList();
        var joined = new List<(string Text, RectangleF Box)>();
        while (rest.Count > 0)
        {
            var (text, box) = rest[0];
            rest.RemoveAt(0);
            for (int i = 0; i < rest.Count; i++)
            {
                var o = rest[i].Box;
                float overlap = Math.Min(box.Bottom, o.Bottom) - Math.Max(box.Top, o.Top), h = Math.Max(box.Height, o.Height);
                if (overlap < 0.5f * Math.Min(box.Height, o.Height) || o.Left - box.Right > 2 * h || o.Left < box.Left) continue;
                text += " " + rest[i].Text;
                box = RectangleF.Union(box, o);
                rest.RemoveAt(i--);
            }
            joined.Add((text, box));
        }
        return joined;
    }

    /// <summary>
    /// The boxes over a system's first staff (a rehearsal mark's), on the page as loaded: in the band
    /// from sixteen spaces over the staff (short of a space under the system before) to a third of a
    /// space over it, each a component of the ink a space and a half to five tall and up to twenty-five
    /// wide, whose pixels run along most of all four sides of its bounding box and fill little of it
    /// (a chord's ledger lines have no sides; a box a slur or a pedal line touches is no component
    /// of its own, and is left to the band's OCR). With each, the one piece of ink inside its rules
    /// (a letter) when there is exactly one half a space or taller.
    /// </summary>
    /// <summary>
    /// Whether a piece of ink (<paramref name="c"/>, among <paramref name="labels"/> of a window
    /// <paramref name="w"/> wide) is a chord diagram's lattice: a space and a fifth to 25 wide, a
    /// space and a half to seven tall, under three tenths inked, with three or more rules across it
    /// (its nut and frets: rows holding four fifths of its width) and three or more down it (its
    /// strings: columns holding seven tenths of its height), each rule half a space and more from
    /// the last. A mark's box has two of each, its edges, and the letter inside is a piece of ink of
    /// its own.
    /// </summary>
    internal static bool IsLattice(int[] labels, int w, Comp c, double s)
    {
        if (c.W < 1.2 * s || c.W > 25 * s || c.H < 1.5 * s || c.H > 7 * s || c.Fill > 0.3) return false;
        int RowN(int y) { int n = 0; for (int x = c.MinX; x <= c.MaxX; x++) if (labels[y * w + x] == c.Id) n++; return n; }
        int ColN(int x) { int n = 0; for (int y = c.MinY; y <= c.MaxY; y++) if (labels[y * w + x] == c.Id) n++; return n; }
        int Rules(Func<int, int> n, int from, int to, double full)
        {
            int count = 0, last = int.MinValue;
            for (int i = from; i <= to; i++)
                if (n(i) >= full) { if (i - last > 0.5 * s) count++; last = i; }
            return count;
        }
        return Rules(RowN, c.MinY, c.MaxY, 0.8 * c.W) >= 3 && Rules(ColN, c.MinX, c.MaxX, 0.7 * c.H) >= 3;
    }

    static List<Frame> Frames(Img dark, double s, SystemInfo sys, Staff above)
    {
        var top = sys.Staves[0];
        int x0 = Math.Max(0, (int)(top.Left - 6 * s)), x1 = Math.Min(dark.W - 1, top.Right);
        // (up to sixteen spaces over the staff, short of a space under the system before: an octave
        // line and a tempo under the box lift it, the Lab's sonatina's "Cadenza" to twelve)
        int floor = above == null ? 0 : (int)(above.Bottom + s);
        int Y0(double over) => Math.Max(floor, (int)(over - 16 * s));
        var frames = Find(Y0(top.Top));
        // (a lead sheet's chord diagrams over the staff, its chord names, tempo and marks over them:
        // the band reaches sixteen spaces over the diagrams, leadsheet-collide's "Intro" eighteen over the staff)
        var grids = frames.Where(f => f.Grid).ToList();
        if (grids.Count > 0 && Y0(grids.Min(g => g.Dark.Top)) < Y0(top.Top)) frames = Find(Y0(grids.Min(g => g.Dark.Top)));
        return frames;

        List<Frame> Find(int y0)
        {
            int y1 = Math.Min(dark.H - 1, (int)(top.Top - 0.3 * s));
            int w = x1 - x0 + 1, h = y1 - y0 + 1;
            var found = new List<Frame>();
            if (w < 2 * s || h < 2 * s) return found;
            var (labels, comps) = Components.Label(w, h, i => dark[x0 + i % w, y0 + i / w], eight: true);
            foreach (var c in comps.Skip(1))
            {
                // (a chord diagram runs to seven spaces tall, leadsheet-collide's 5.2: a box that tall is one only as a grid, below)
                if (Verbose && c.W >= 2 * s && c.H >= 2 * s && (c.W > 25 * s || c.H > 7 * s || c.Fill > 0.3))
                    Console.WriteLine($"  frame? at {x0 + c.MinX},{y0 + c.MinY} {c.W}x{c.H} fill {c.Fill:F2}: too large or too full");
                if (c.W < 1.2 * s || c.W > 25 * s || c.H < 1.5 * s || c.H > 7 * s || c.Fill > 0.3) continue;
                int RowN(int y) { int n = 0; for (int x = c.MinX; x <= c.MaxX; x++) if (labels[y * w + x] == c.Id) n++; return n; }
                int ColN(int x) { int n = 0; for (int y = c.MinY; y <= c.MaxY; y++) if (labels[y * w + x] == c.Id) n++; return n; }
                // a chord diagram: a lattice of ink (IsLattice). It is no mark, but what the band over
                // the staff reaches over: a lead sheet sets its chord names, tempo and marks over the
                // diagrams (leadsheet-collide: 5.2 spaces tall, open at the bottom, the barre's dots
                // out past the strings)
                if (IsLattice(labels, w, c, s))
                {
                    if (Verbose) Console.WriteLine($"  frame? at {x0 + c.MinX},{y0 + c.MinY} {c.W}x{c.H}: a lattice (a chord diagram), no mark");
                    var ga = dark.ToSource(x0 + c.MinX, y0 + c.MinY);
                    var gb = dark.ToSource(x0 + c.MaxX + 1, y0 + c.MaxY + 1);
                    var gbox = RectangleF.FromLTRB((float)Math.Min(ga.X, gb.X), (float)Math.Min(ga.Y, gb.Y), (float)Math.Max(ga.X, gb.X), (float)Math.Max(ga.Y, gb.Y));
                    found.Add(new Frame(sys.Index, Rectangle.FromLTRB(x0 + c.MinX, y0 + c.MinY, x0 + c.MaxX + 1, y0 + c.MaxY + 1), gbox, 0, null, null, Grid: true));
                    continue;
                }
                if (c.H > 5 * s) continue;
                bool Row(int y) => RowN(y) >= 0.85 * c.W;
                bool Col(int x) => ColN(x) >= 0.85 * c.H;
                // (each rule within a fifth of a space of the component's edge: a scanned box's corner
                // sticks out a pixel or two)
                int k = Math.Max(2, (int)(0.2 * s));
                int ty = Enumerable.Range(c.MinY, Math.Min(k, c.H)).FirstOrDefault(Row, -1), by = Enumerable.Range(0, Math.Min(k, c.H)).Select(i => c.MaxY - i).FirstOrDefault(Row, -1);
                int lx = Enumerable.Range(c.MinX, Math.Min(k, c.W)).FirstOrDefault(Col, -1), rx = Enumerable.Range(0, Math.Min(k, c.W)).Select(i => c.MaxX - i).FirstOrDefault(Col, -1);
                if (Verbose) Console.WriteLine($"  frame? at {x0 + c.MinX},{y0 + c.MinY} {c.W}x{c.H} fill {c.Fill:F2} sides {RowN(c.MinY) / (double)c.W:F2} {RowN(c.MaxY) / (double)c.W:F2} {ColN(c.MinX) / (double)c.H:F2} {ColN(c.MaxX) / (double)c.H:F2} rules {ty},{by},{lx},{rx}");
                // a box another piece of ink touches is one component with it (the key signature's
                // sharp under its corner, where Lily# sets a mark at a system's start over the key: the
                // band charts' E and F boxes): the box is the rules within the component. Its top rule
                // is the first row's run of ink a letter's box wide or more; its bottom the last row
                // full across that run; its sides the run's end columns, full between the two
                if (ty < 0 || by < 0 || lx < 0 || rx < 0)
                {
                    if (!Within(c, out ty, out by, out lx, out rx)) continue;
                    if (Verbose) Console.WriteLine($"  frame? at {x0 + c.MinX},{y0 + c.MinY}: a box touched by other ink, its rules within at {x0 + lx},{y0 + ty} {rx - lx + 1}x{by - ty + 1}");
                }
                int RowIn(int y) { int n = 0; for (int x = lx; x <= rx; x++) if (labels[y * w + x] == c.Id) n++; return n; }
                int ColIn(int x) { int n = 0; for (int y = ty; y <= by; y++) if (labels[y * w + x] == c.Id) n++; return n; }
                bool RowFull(int y) => RowIn(y) >= 0.85 * (rx - lx + 1);
                bool ColFull(int x) => ColIn(x) >= 0.85 * (by - ty + 1);
                // (the rules' thickness: how far in from each side the rows and columns stay full)
                int t = 1;
                while (t < (by - ty) / 2 && t < (rx - lx) / 2 && RowFull(ty + t) && RowFull(by - t) && ColFull(lx + t) && ColFull(rx - t)) t++;
                var a = dark.ToSource(x0 + lx, y0 + ty);
                var b = dark.ToSource(x0 + rx + 1, y0 + by + 1);
                var box = RectangleF.FromLTRB((float)Math.Min(a.X, b.X), (float)Math.Min(a.Y, b.Y), (float)Math.Max(a.X, b.X), (float)Math.Max(a.Y, b.Y));
                // (the pieces inside the rules, short of them by a sixth of a space: one is a letter)
                double inset = t + 0.15 * s;
                var inside = comps.Skip(1).Where(o => o != c && o.H >= 0.5 * s && o.MinX > lx + inset && o.MaxX < rx - inset && o.MinY > ty + inset && o.MaxY < by - inset).ToList();
                Comp letter = null;
                double[] f = null;
                if (inside.Count == 1)
                {
                    var o = inside[0];
                    letter = new Comp { Id = o.Id, N = o.N, MinX = x0 + o.MinX, MaxX = x0 + o.MaxX, MinY = y0 + o.MinY, MaxY = y0 + o.MaxY, Sx = o.Sx + (long)o.N * x0, Sy = o.Sy + (long)o.N * y0 };
                    f = GlyphKnn.BoxFeatures(dark, letter, s);
                }
                found.Add(new Frame(sys.Index, Rectangle.FromLTRB(x0 + lx, y0 + ty, x0 + rx + 1, y0 + by + 1), box, t, letter, f));
            }
            return found;

            // the rules of a box within a component other ink touches (above)
            bool Within(Comp c, out int ty, out int by, out int lx, out int rx)
            {
                int top = -1, bottom = -1, left = -1, right = -1;
                ty = by = lx = rx = -1;
                for (int y = c.MinY; y <= c.MaxY && top < 0; y++)
                {
                    // the row's longest run
                    int bestA = -1, bestB = -2;
                    for (int x = c.MinX; x <= c.MaxX; x++)
                    {
                        if (labels[y * w + x] != c.Id) continue;
                        int a = x;
                        while (x + 1 <= c.MaxX && labels[y * w + x + 1] == c.Id) x++;
                        if (x - a > bestB - bestA) { bestA = a; bestB = x; }
                    }
                    if (bestB - bestA + 1 >= 1.2 * s) { top = y; left = bestA; right = bestB; }
                }
                if (top < 0) return false;
                int wd = right - left + 1;
                int Across(int y) { int n = 0; for (int x = left; x <= right; x++) if (labels[y * w + x] == c.Id) n++; return n; }
                for (int y = c.MaxY; y > top && bottom < 0; y--) if (Across(y) >= 0.85 * wd) bottom = y;
                if (bottom < 0 || bottom - top + 1 < 1.5 * s) return false;
                int ht = bottom - top + 1;
                int Down(int x) { int n = 0; for (int y = top; y <= bottom; y++) if (labels[y * w + x] == c.Id) n++; return n; }
                if (Down(left) < 0.85 * ht || Down(right) < 0.85 * ht) return false;
                (ty, by, lx, rx) = (top, bottom, left, right);
                return true;
            }
        }
    }

    /// <summary>
    /// The words in a box read by themselves: the box's inside, <paramref name="inset"/> within its
    /// edges, copied onto white with <paramref name="margin"/> round it and read at the page's scale;
    /// the lines joined left to right ("" for none).
    /// </summary>
    static string Alone(Bitmap page, RectangleF box, double inset, double margin, double k = 1)
    {
        var inner = Rectangle.FromLTRB((int)Math.Round(box.Left + inset), (int)Math.Round(box.Top + inset), (int)Math.Round(box.Right - inset), (int)Math.Round(box.Bottom - inset));
        inner.Intersect(new Rectangle(0, 0, page.Width, page.Height));
        int m = (int)Math.Ceiling(margin);
        if (inner.Width < 4 || inner.Height < 4) return "";
        using var alone = new Bitmap(inner.Width + 2 * m, inner.Height + 2 * m, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(alone))
        {
            g.Clear(Color.White);
            g.DrawImage(page, new Rectangle(m, m, inner.Width, inner.Height), inner, GraphicsUnit.Pixel);
        }
        // (read as it is: the caller tries the margins and scales itself when the engine mixes kana into the word)
        var lines = Joined(Header.Recognize(alone, new Rectangle(0, 0, alone.Width, alone.Height), k * Header.Scale(page.Width), again: false));
        return string.Join(" ", lines.OrderBy(l => l.Box.Left).Select(l => l.Text));
    }

    /// <summary>
    /// Whether a line of text stands in a box (a rehearsal mark): a dark rule over it and under it as
    /// long as most of it, and one either side as tall, within half its height of it.
    /// </summary>
    static bool Boxed(Bitmap page, RectangleF r)
    {
        bool Dark(int x, int y) => x >= 0 && y >= 0 && x < page.Width && y < page.Height && page.GetPixel(x, y).GetBrightness() < 0.5f;
        int reach = (int)Math.Ceiling(0.6 * r.Height);
        bool Row(int y) { int n = 0, all = 0; for (int x = (int)r.Left; x <= (int)r.Right; x += 2, all++) if (Dark(x, y)) n++; return all > 0 && n >= 0.85 * all; }
        bool Col(int x) { int n = 0, all = 0; for (int y = (int)r.Top; y <= (int)r.Bottom; y += 2, all++) if (Dark(x, y)) n++; return all > 0 && n >= 0.85 * all; }
        // (from a third of its height inside it outwards: the OCR's box may take in a box set tight round the words)
        int inside = (int)Math.Ceiling(0.3 * r.Height);
        bool Any(Func<int, bool> line, int from, int step) { for (int d = -inside; d <= reach; d++) if (line(from + step * d)) return true; return false; }
        return Any(Row, (int)r.Top, -1) && Any(Row, (int)r.Bottom, 1) && Any(Col, (int)r.Left, -1) && Any(Col, (int)r.Right, 1);
    }
}
