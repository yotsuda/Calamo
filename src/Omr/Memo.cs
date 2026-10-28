namespace Omr;

/// <summary>
/// The words of an @todo (and of the bar's "// …" length comment): what the reading doubts, said
/// as a musician would say it, for the user correcting the .lys against the scan. Note values and
/// note names, not ticks and detector classes (those stay in the run's own report). What, not
/// where: the mark is on the note and its key names the bar and part (Lily#'s request of 2026-10-17).
/// </summary>
static class Memo
{
    /// <summary>A note's name without its octave: "F♯", "B♭"; a natural as "F♮" when <paramref name="natural"/> (beside another reading of it), else "F".</summary>
    public static string Name(Pitch p, bool natural = true) =>
        char.ToUpperInvariant(p.Lys()[0]) + (p.Alter switch { 1 => "♯", -1 => "♭", 2 => "𝄪", -2 => "𝄫", _ => natural ? "♮" : "" });

    /// <summary>A length as note values: "a dotted quarter", "an eighth"; else as a count of the meter's unit ("3 eighths", "1½ quarters").</summary>
    public static string Length(int ticks, string time)
    {
        if (One(ticks) is string one) return one;
        // (two values, the larger first: "a quarter and a 16th")
        for (int b = 1; b <= 64; b *= 2)
            if (Dur.Whole / b < ticks && One(Dur.Whole / b) is string first && One(ticks - Dur.Whole / b) is string second)
                return $"{first} and {second}";
        int den = int.Parse(time.Split('/')[1]), unit = Dur.Whole / den;
        return $"{Count(ticks, unit)} {Value(den, true)}";
    }

    // (one note value, dotted or not: "a dotted quarter")
    static string One(int ticks)
    {
        if (Dur.Text(ticks) is not string t) return null;
        int b = int.Parse(t.TrimEnd('.')), dots = t.Length - t.TrimEnd('.').Length;
        string name = (dots == 1 ? "dotted " : dots == 2 ? "double-dotted " : "") + Value(b, false);
        return ("aeiou".Contains(name[0]) ? "an " : "a ") + name;
    }

    static string Value(int b, bool plural) => b switch
    {
        1 => plural ? "whole notes" : "whole note",
        2 => plural ? "halves" : "half note",
        4 => plural ? "quarters" : "quarter",
        8 => plural ? "eighths" : "eighth",
        _ => $"{b}th{(plural ? "s" : "")}",
    };

    // (a count with a common fraction: "1½", "¾", else "7/5")
    static string Count(int ticks, int unit)
    {
        int whole = ticks / unit, rest = ticks % unit;
        if (rest == 0) return whole.ToString();
        int g = Gcd(rest, unit), n = rest / g, d = unit / g;
        string frac = (n, d) switch
        {
            (1, 2) => "½", (1, 3) => "⅓", (2, 3) => "⅔", (1, 4) => "¼", (3, 4) => "¾",
            (1, 6) => "⅙", (5, 6) => "⅚", (1, 8) => "⅛", (3, 8) => "⅜", (5, 8) => "⅝", (7, 8) => "⅞",
            _ => null,
        };
        return frac != null ? (whole > 0 ? whole.ToString() : "") + frac : $"{ticks / (double)unit:0.##}";
    }

    static int Gcd(int a, int b) => b == 0 ? a : Gcd(b, a % b);

    /// <summary>A bar whose voices do not fill its time (each voice's length in ticks).</summary>
    public static string BarLength(IReadOnlyList<int> sums, int barTicks, string time)
    {
        if (sums.Count == 1)
        {
            int sum = sums[0];
            if (sum == 0) return "nothing read in this bar";
            return sum < barTicks
                ? $"{Length(barTicks - sum, time)} short of {time}: a note or rest missed, or a duration misread?"
                : $"{Length(sum - barTicks, time)} too long for {time}: an extra note, or a duration misread?";
        }
        // (every voice off by the same: said once, as of one voice)
        if (sums.Distinct().Count() == 1) return BarLength(new[] { sums[0] }, barTicks, time);
        string Voice(int v) => sums.Count == 2 ? (v == 0 ? "upper" : "lower") : $"voice {v + 1}";
        // only the voices that are off: a full one is not news
        var off = Enumerable.Range(0, sums.Count).Where(v => sums[v] != barTicks).Select(v =>
            sums[v] == 0 ? $"the {Voice(v)} voice has nothing"
            : sums[v] > barTicks ? $"the {Voice(v)} voice is {Length(sums[v] - barTicks, time)} too long"
            : $"the {Voice(v)} voice is {Length(barTicks - sums[v], time)} short");
        return string.Join(", ", off) + $" (bar of {time})";
    }

    /// <summary>A bar that holds <paramref name="bars"/> bars of its time and is as many times as wide as its neighbours (Sidecar.BarsInOne).</summary>
    public static string MissedBarLine(int bars, string time) =>
        $"a bar line may be missed here: the bar holds {bars} bars of {time} and is {(bars == 2 ? "twice" : $"{bars} times")} as wide as its neighbours";

    /// <summary>
    /// lysc's word on a bar's length ("Measure duration 3/4 is less than time signature 4/4", "… exceeds …")
    /// as BarLength says it, and by how much of the bar it is off (0..1); else as lysc says it, off by half.
    /// </summary>
    public static (string Memo, double Off) LyscLength(string message)
    {
        var m = System.Text.RegularExpressions.Regex.Match(message, @"^Measure duration (\d+)(?:/(\d+))? (?:is less than|exceeds) time signature ((\d+)/(\d+))$");
        if (!m.Success) return (message, 0.5);
        int sum = Dur.Whole * int.Parse(m.Groups[1].Value) / (m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : 1);
        int bar = Dur.OfTime(m.Groups[3].Value);
        return (BarLength(new[] { sum }, bar, m.Groups[3].Value), Math.Min(1.0, Math.Abs(sum - bar) / (double)bar));
    }

    /// <summary>A note read with a doubt (Symbols.Doubts): the other reading the scan half supports.</summary>
    public static string Doubt(EvAlt a)
    {
        if (a.To != a.From)
            return a.What.Contains("faintly")
                ? $"{Name(a.From)} or {Name(a.To)}? a faint {Sign(a.To.Alter)} before it on the scan"
                : $"{Name(a.From)} or {Name(a.To)}? the sign on the scan is unclear";
        return a.DotsBy > 0 ? "dotted? a faint dot after it on the scan"
            : "no dot? the dot may be a speck on the scan";
    }

    static string Sign(int alter) => alter switch { 1 => "♯", -1 => "♭", 2 => "𝄪", -2 => "𝄫", _ => "♮" };

    /// <summary>A TAB fret that disagrees with the staff's notes above it (Tab.Apply).</summary>
    public static string Tab(int fret, Pitch tab, IEnumerable<Pitch> staff) =>
        $"the TAB's fret {fret} is {Name(tab, false)}, the staff above reads {string.Join(" ", staff.Select(p => Name(p, false)))}";

    // ---- the redraw check: what the .lys drawn again has that the scan does not show, and the other way

    /// <summary>
    /// The symbols only the drawn .lys has (<paramref name="text"/>) and only the scan shows
    /// (<paramref name="scan"/>), as (class, staff position): a head moved, a head of another
    /// kind, a stem's beams said as such, the rest listed on each side. <paramref name="named"/>, when
    /// given, names the note at a staff position: a head listed on either side says it ("a filled note
    /// head (f,)"), a second's head dropped from a chord found by its pitch.
    /// </summary>
    public static string Differences(List<(string Class, int Pos)> text, List<(string Class, int Pos)> scan, bool across = false, Func<int, string> named = null)
    {
        text = new(text); scan = new(scan);
        var said = new List<string>();
        // (a bar's stems and tuplet numbers are compared over the system: said apart from its staff's symbols, beside which they are written)
        string lead = across ? "stems and tuplets: " : "";
        // a head of one kind at one place on each side: moved
        foreach (var kind in text.Where(t => IsHead(t.Class)).Select(t => t.Class).Distinct().ToList())
        {
            var a = text.Where(t => t.Class == kind).ToList();
            var b = scan.Where(t => t.Class == kind).ToList();
            if (a.Count != 1 || b.Count != 1) continue;
            int d = b[0].Pos - a[0].Pos;
            said.Add($"{Thing(kind, false)} sits {Math.Abs(d)} step{(Math.Abs(d) == 1 ? "" : "s")} {(d > 0 ? "higher" : "lower")} on the scan");
            text.Remove(a[0]); scan.Remove(b[0]);
        }
        // a head of another kind at the same place (counted by the two kinds)
        var swaps = new List<(string Text, string Scan)>();
        foreach (var a in text.Where(t => IsHead(t.Class)).ToList())
            if (scan.FirstOrDefault(b => IsHead(b.Class) && b.Pos == a.Pos) is var b && b.Class != null)
            {
                swaps.Add((a.Class, b.Class));
                text.Remove(a); scan.Remove(b);
            }
        foreach (var g in swaps.GroupBy(w => w))
            said.Add(g.Count() == 1 ? $"{Thing(g.Key.Scan, false)} on the scan where the text has {Thing(g.Key.Text, false)}"
                : $"{g.Count()} {Thing(g.Key.Scan, true)} on the scan where the text has {Thing(g.Key.Text, true)}");
        // a stem's beams or flags
        var sa = text.Where(t => t.Class.StartsWith("stem_")).ToList();
        var sb = scan.Where(t => t.Class.StartsWith("stem_")).ToList();
        if (sa.Count == 1 && sb.Count == 1)
        {
            said.Add($"{Beams(sb[0].Class)} on the scan, {Beams(sa[0].Class)} in the text");
            text.Remove(sa[0]); scan.Remove(sb[0]);
        }
        // (most of a bar unmatched is a bar read wrong as a whole: a list of it helps no one)
        if (scan.Count + text.Count > 8)
            return across ? "the stems and beams do not match the scan" : "much of this bar differs from the scan: read it again";
        if (scan.Count > 0) said.Add("on the scan, not in the text: " + List(scan, named));
        if (text.Count > 0) said.Add("in the text, not on the scan: " + List(text, named));
        return lead + string.Join("; ", said);
    }

    static bool IsHead(string c) => c.StartsWith("head_");

    static string Beams(string stem) => int.Parse(stem["stem_".Length..]) switch
    {
        0 => "a plain stem",
        1 => "1 beam or flag",
        int n => $"{n} beams or flags",
    };

    static string List(List<(string Class, int Pos)> items, Func<int, string> named = null) =>
        string.Join(", ", items.GroupBy(t => t.Class).OrderBy(g => Order(g.Key))
            .Select(g => (g.Count() == 1 ? Thing(g.Key, false) : $"{g.Count()} {Thing(g.Key, true)}") + Names(g, named)));

    // (the heads' notes, low to high: "(f, ×4)", "(e, f,)")
    static string Names(IEnumerable<(string Class, int Pos)> g, Func<int, string> named) =>
        named == null || !IsHead(g.First().Class) ? ""
        : " (" + string.Join(" ", g.GroupBy(t => t.Pos).OrderBy(p => p.Key).Select(p => named(p.Key) + (p.Count() > 1 ? $" ×{p.Count()}" : ""))) + ")";

    static int Order(string c) => IsHead(c) ? 0 : c.StartsWith("rest_") ? 1 : c is "sharp" or "flat" or "natural" or "dsharp" or "dflat" ? 2 : c == "dot" ? 3 : 4;

    /// <summary>A detector class as words: "a half note", "a ♯", "an eighth rest" (plural: "half notes", "♯s"…).</summary>
    static string Thing(string c, bool plural)
    {
        string one = c switch
        {
            "head_black" => "a filled note head", "head_half" => "a half note", "head_whole" => "a whole note",
            "head_x" => "an x-shaped note head", "slash_black" => "a rhythm slash", "slash_hollow" => "a hollow rhythm slash",
            "percent_1" => "a one-bar repeat sign", "percent_2" => "a two-bar repeat sign",
            "sharp" => "a ♯", "flat" => "a ♭", "natural" => "a ♮", "dsharp" => "a 𝄪", "dflat" => "a 𝄫",
            "dot" => "a dot", "tie_l" => "a tie", "slur_l" => "a slur",
            "grace_head" => "a grace note", "grace_slash" => "a grace note's slash",
            "rep_open" => "a repeat's opening dots", "rep_close" => "a repeat's closing dots", "volta_l" => "an ending's bracket", "volta_r" => "an ending's end",
            "segno" => "a segno", "coda" => "a coda sign",
            "orn_trill" => "a trill", "orn_mordent" => "a mordent", "orn_prall" => "a prall", "orn_turn" => "a turn", "orn_reverseturn" => "a reverse turn",
            "art_portato" => "a portato", "art_stopped" => "a stopped note's +", "orn_prallprall" => "a pralltriller",
            "ott_8va" => "an 8va sign", "ott_8vb" => "an 8vb sign", "ott_15ma" => "a 15ma sign", "ott_15mb" => "a 15mb sign", "ott_end" => "an octave line's end",
            "rest_1" => "a whole rest", "rest_2" => "a half rest", "rest_4" => "a quarter rest", "rest_8" => "an eighth rest",
            _ when c.StartsWith("rest_") => $"a {c[5..]}th rest",
            _ when c.StartsWith("tuplet_") => $"a tuplet {c[7..]}",
            _ when c.StartsWith("stem_") => Beams(c) == "a plain stem" ? "a plain stem" : $"a stem with {Beams(c)}",
            _ => c,
        };
        if (!plural) return one;
        string bare = one.StartsWith("an ") ? one[3..] : one.StartsWith("a ") ? one[2..] : one;
        return bare.StartsWith("stem with") ? "stems with" + bare["stem with".Length..] : bare + "s";
    }
}
