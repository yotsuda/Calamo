namespace Omr;

/// <summary>
/// What the user tells the reader up front: the title and the parts (one per staff of a
/// system). Clefs, keys and times are read from the page; when the file also states them
/// ("bar" lines, the clef after a part id), the reader reports where the page disagrees, and
/// --given makes it use them instead. Format, one directive per line ('#' starts a comment):
///   title Bohemian Rhapsody
///   part vn1 treble "Violins1"
///   bar 1 time 9/8 key bes
///   page 14 98            (optional: the first bar on page 14)
///   tab                   (a TAB staff below the part before: found and set aside, not read; B2)
///   line                  (a one-line staff below the part before, as a percussion line)
///   line gtr "Guitar"     (a one-line staff that is a part: a lead sheet's rhythm staff, read as
///                          slashes, written "staff gtr as lines 1"; S17)
///   grandStaff rh lh      (the parts a brace joins on the page: written as Lily#'s grandStaff { ... };
///   staffGroup vn1 vn2 va  a bracket's as staffGroup { ... }. omr read guesses them from the page)
/// </summary>
sealed class ScoreConfig
{
    public string Title = "";
    /// <summary>The parts in order: Lines is 5, or 1 for a part on a one-line staff ("line gtr").</summary>
    public List<(string Id, string Clef, string Name, int Lines)> Parts = new();
    /// <summary>A system's staves top to bottom: "" for each part's five-line staff, "tab" or "line" for the others.</summary>
    public List<string> Staves = new();
    /// <summary>Per entry of <see cref="Staves"/>: the part read on it, or -1 for a staff set aside.</summary>
    public List<int> StaffParts = new();
    public bool HasExtras => Staves.Any(k => k != "");
    /// <summary>The parts a brace ("grandStaff") or a bracket ("staffGroup") joins, by id, in order: the .lys's score block nests them.</summary>
    public List<(string Kind, List<string> Ids)> Groups = new();

    public void AddPart(string id, string clef, string name, int lines = 5)
    {
        Parts.Add((id, clef, name, lines));
        Staves.Add(lines == 1 ? "line" : "");
        StaffParts.Add(Parts.Count - 1);
    }
    public SortedDictionary<int, string> Times = new();
    public SortedDictionary<int, int> KeyFifths = new();
    public Dictionary<int, int> PageFirstBar = new();

    // Lily#'s words a part may not be named (SyntaxFacts.PartNameReservedVocabulary): "part voice" is an error
    static readonly HashSet<string> Reserved = new()
    {
        "voice", "lyrics", "chords", "section", "part", "phrase", "form", "score", "tab", "staff", "ossia",
        "grandStaff", "staffGroup", "choirStaff", "condensedStaff", "combinedStaff", "layout", "paper", "fonts",
        "grace", "acciaccatura", "appoggiatura", "cue", "key", "time", "tempo", "partial", "override", "revert", "once",
        "using", "title", "subtitle", "composer", "poet", "segno", "fine", "coda", "dc", "ds", "al", "to",
        "break", "noBreak", "pageBreak", "noPageBreak",
    };

    /// <summary>The part's name as the .lys (and its sidecar) writes it: a bare word, not starting with a digit, none of Lily#'s reserved ones.</summary>
    public string LysId(int part)
    {
        string id = System.Text.RegularExpressions.Regex.Replace(Parts[part].Id, @"[^\p{L}\p{Nd}_]", "_");
        if (id.Length == 0 || char.IsDigit(id[0])) id = "p" + id;
        return Reserved.Contains(id) ? id + "_" : id;
    }

    public static ScoreConfig Load(string path)
    {
        var cfg = new ScoreConfig();
        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Split('#')[0].Trim();
            if (line.Length == 0) continue;
            var t = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            switch (t[0])
            {
                case "title": cfg.Title = line.Substring(6).Trim(); break;
                case "part":
                    // part <id> [clef] ["Name"]: the clef is only a check (clefs are read from the page)
                    string name = line.Contains('"') ? line.Split('"')[1] : t[1];
                    string clef = t.Length > 2 && !t[2].StartsWith('"') ? t[2] : "treble";
                    cfg.AddPart(t[1], clef, name);
                    break;
                case "tab":
                case "line":
                    // (line <id> ["Name"]: a part on the one-line staff, read as rhythm slashes)
                    if (t[0] == "line" && t.Length > 1) { cfg.AddPart(t[1], "treble", line.Contains('"') ? line.Split('"')[1] : t[1], lines: 1); break; }
                    cfg.Staves.Add(t[0]);
                    cfg.StaffParts.Add(-1);
                    break;
                case "grandStaff":
                case "staffGroup":
                    if (t.Length > 2) cfg.Groups.Add((t[0], t.Skip(1).ToList()));
                    break;
                case "bar":
                    int bar = int.Parse(t[1]);
                    for (int i = 2; i + 1 < t.Length; i += 2)
                    {
                        if (t[i] == "time") cfg.Times[bar] = t[i + 1];
                        if (t[i] == "key") cfg.KeyFifths[bar] = Keys.ParseMajor(t[i + 1]);
                    }
                    break;
                case "page": cfg.PageFirstBar[int.Parse(t[1])] = int.Parse(t[2]); break;
            }
        }
        return cfg;
    }

    public string TimeAt(int bar) => Times.Where(kv => kv.Key <= bar).Select(kv => kv.Value).LastOrDefault() ?? "4/4";
    public int KeyAt(int bar) => KeyFifths.Where(kv => kv.Key <= bar).Select(kv => kv.Value).LastOrDefault();
}
