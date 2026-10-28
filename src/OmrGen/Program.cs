using System.Globalization;
using System.Text;

namespace OmrGen;

// OmrGen --out <dir> --suite <basic|ledger|tuplets|chords|voices2|piano|...|all|band|lied> --count N --seed S --bars B [--lyrics] [--fonts Emmentaler,Bravura,...] [--vary] [--headers] [--marks] [--artic] [--mmrests] [--tuplets] [--ottava [--ottava-15 <share>]] [--graces] [--repeats] [--ornaments] [--scripts2] [--spans] [--trills] [--arpeggios] [--glissandi] [--beams3] [--figures]
// ("all" is Gen.Suites; "band", the band scores' pieces, and "lied", songs whose voice's staff
// is hidden where it rests, are asked for by name; --lyrics puts words under lied's voice)
//
// Writes test scores for the OMR: per piece a folder with <name>.lys (engraved by Lily#),
// <name>.score (the OMR's part list, plus the clefs/key/time it should read), and the truth,
// one .notes file per staff and voice in LilyPond event-listener form (time, note, MIDI pitch,
// duration, duration as a fraction of a whole; rests likewise).
static class Program
{
    static int Main(string[] args)
    {
        var opt = new Dictionary<string, string>();
        for (int i = 0; i < args.Length; i++)
            if (args[i].StartsWith("--")) opt[args[i][2..]] = i + 1 < args.Length && !args[i + 1].StartsWith("--") ? args[++i] : "true";
        string outDir = opt.GetValueOrDefault("out", "gen");
        int count = int.Parse(opt.GetValueOrDefault("count", "5"));
        int seed = int.Parse(opt.GetValueOrDefault("seed", "1"));
        int bars = int.Parse(opt.GetValueOrDefault("bars", "16"));
        Gen.Lyrics = opt.ContainsKey("lyrics");
        Gen.Dynamics = opt.ContainsKey("dynamics");
        Gen.Texts = opt.ContainsKey("texts");
        Gen.Piano = opt.ContainsKey("piano");
        Gen.Changes = opt.ContainsKey("changes");
        Gen.Graces = opt.ContainsKey("grace");
        // --fonts Emmentaler,Bravura,...: each piece drawn in one of these music fonts (Lily#'s P6)
        var fonts = opt.TryGetValue("fonts", out var fl) ? fl.Split(',') : null;
        // --vary: each piece engraved with its own staff size, spacing, line thicknesses, stem
        // length, beam slope, head size and gaps (Lily#'s P1-P4), three pieces in four
        bool vary = opt.ContainsKey("vary");
        // --marks: dynamics, hairpins and the pedal on every suite's music (S14)
        bool marks = opt.ContainsKey("marks");
        // --artic: staccato, accent, tenuto, marcato, staccatissimo and fermata on every suite's notes (S15)
        bool artic = opt.ContainsKey("artic");
        // --mmrests: stretches of bars where every part rests, written as multi-bar rests (R1*N)
        bool mmrests = opt.ContainsKey("mmrests");
        // --tuplets: quintuplets, sextuplets and septuplets (5:4, 6:4, 7:4 sixteenths in a quarter)
        // among the triplets, half of the groups
        bool tuplets = opt.ContainsKey("tuplets");
        // --ottava: octave lines (8va, 8vb, 15ma, 15mb) over stretches of the notes, which are then
        // written as they sound (S18)
        bool ottava = opt.ContainsKey("ottava");
        // --ottava-15 <share>: the share of the lines that are 15ma or 15mb (0.2 if not given; the
        // draws stay in step, so the same seed lays the same lines, more of them two octaves)
        if (opt.TryGetValue("ottava-15", out var o15)) Gen.Ottava15 = double.Parse(o15, CultureInfo.InvariantCulture);
        // --graces: grace notes (acciaccaturas, appoggiaturas, two or three sixteenths) before notes
        // of every suite (S21; lied's --grace is its own, a few before the voice's and right hand's)
        bool graces = opt.ContainsKey("graces");
        // --repeats: the piece's form: a stretch played twice (|: :|), with a first and second ending,
        // or a jump (segno, to coda, D.S. al Coda, coda); the bars cut into sections for it (S22)
        bool repeats = opt.ContainsKey("repeats");
        // --ornaments: a trill, mordent, prall, turn or reverse turn on a note in ten (S23)
        bool ornaments = opt.ContainsKey("ornaments");
        // --scripts2: a portato, stopped (+) or pralltriller on a note in twelve
        bool scripts2 = opt.ContainsKey("scripts2");
        // --spans: rit., accel., rall. and other words with a dashed line over stretches of notes (S25)
        bool spans = opt.ContainsKey("spans");
        // --trills: a "tr" with a wavy line over stretches of notes
        bool trills = opt.ContainsKey("trills");
        // --arpeggios: an arpeggio's wavy line before a chord in four
        bool arpeggios = opt.ContainsKey("arpeggios");
        // --glissandi: a glissando's straight line from a note to the next, now and then
        bool glissandi = opt.ContainsKey("glissandi");
        // --beams3: runs of plain eighths or sixteenths beamed by hand in threes, not by the beat
        // (three of one value under a beam that are no triplet: the test of Reader.UnprintedTriplets)
        Writer.Beams3 = opt.ContainsKey("beams3");
        // --figures: runs of four sixteenths (or eighths) now and then opened by a rest (r16 c16 d16 e16)
        bool figures = opt.ContainsKey("figures");
        var suites = opt.GetValueOrDefault("suite", "all") == "all"
            ? Gen.Suites
            : opt["suite"].Split(',');
        var list = new StringBuilder();
        foreach (var suite in suites)
            for (int n = 1; n <= count; n++)
            {
                // a stable seed per suite and piece (string.GetHashCode changes between runs)
                int suiteKey = suite.Aggregate(17, (h, ch) => h * 31 + ch) & 0xffff;
                var rng = new Random(seed * 7919 + n * 104729 + suiteKey);
                string name = $"{suite}{n:D3}", dir = Path.Combine(outDir, suite, name);
                Directory.CreateDirectory(dir);
                // (the font from a stream of its own: the same seed writes the same music with or without --fonts)
                Writer.MusicFont = fonts?[new Random(seed * 7919 + n * 104729 + suiteKey + 1).Next(fonts.Length)];
                // (the engraving's settings likewise: --vary)
                Writer.Style = vary ? Writer.DrawStyle(new Random(seed * 7919 + n * 104729 + suiteKey + 2)) : null;
                // (--headers: a title, subtitle, composer, poet and tempo, from a stream of their own likewise)
                Writer.HeaderRng = opt.ContainsKey("headers") ? new Random(seed * 7919 + n * 104729 + suiteKey + 3) : null;
                // (--tuplets: which kind a tuplet group is, from a stream of its own likewise: the
                // same seed writes the same music, its triplets turned into the other kinds)
                Gen.TupletRng = tuplets ? new Random(seed * 7919 + n * 104729 + suiteKey + 7) : null;
                // (--figures: which runs open on a rest, likewise)
                Gen.FigureRng = figures ? new Random(seed * 7919 + n * 104729 + suiteKey + 16) : null;
                // (the band suite has its own parts and writer: B3)
                if (suite == "band") Band.Write(name, rng, bars, dir);
                else
                {
                    var piece = Gen.Make(suite, name, rng, bars);
                    // (--mmrests: bars where every part rests, from a stream of their own likewise)
                    if (mmrests) Gen.AddMultiRests(piece, new Random(seed * 7919 + n * 104729 + suiteKey + 6));
                    // (--marks: dynamics, hairpins and the pedal, from a stream of their own likewise)
                    if (marks) Gen.AddMarks(piece, new Random(seed * 7919 + n * 104729 + suiteKey + 4));
                    // (--artic: articulations, likewise)
                    if (artic) Gen.AddArticulations(piece, new Random(seed * 7919 + n * 104729 + suiteKey + 5));
                    // (--ornaments: likewise)
                    if (ornaments) Gen.AddOrnaments(piece, new Random(seed * 7919 + n * 104729 + suiteKey + 11));
                    // (--scripts2: likewise)
                    if (scripts2) Gen.AddScripts2(piece, new Random(seed * 7919 + n * 104729 + suiteKey + 17));
                    // (--spans: likewise)
                    if (spans) Gen.AddSpans(piece, new Random(seed * 7919 + n * 104729 + suiteKey + 12));
                    // (--trills: likewise)
                    if (trills) Gen.AddTrills(piece, new Random(seed * 7919 + n * 104729 + suiteKey + 13));
                    // (--arpeggios: likewise)
                    if (arpeggios) Gen.AddArpeggios(piece, new Random(seed * 7919 + n * 104729 + suiteKey + 14));
                    // (--glissandi: likewise)
                    if (glissandi) Gen.AddGlissandi(piece, new Random(seed * 7919 + n * 104729 + suiteKey + 15));
                    // (--ottava: octave lines, likewise; after the rests are put in, which move the bars)
                    // (--graces: grace notes, likewise; before the octave lines, which pass over no note with one)
                    if (graces) Gen.AddGraces(piece, new Random(seed * 7919 + n * 104729 + suiteKey + 9));
                    if (ottava) Gen.AddOttavas(piece, new Random(seed * 7919 + n * 104729 + suiteKey + 8));
                    // (--repeats: the form, likewise; last, since a tie or a slur across one of its cuts is undone)
                    if (repeats) Gen.AddRepeats(piece, new Random(seed * 7919 + n * 104729 + suiteKey + 10));
                    Writer.Write(piece, dir);
                }
                list.AppendLine(Path.GetFullPath(Path.Combine(dir, name + ".lys")) + "\t" + Path.GetFullPath(dir));
            }
        File.WriteAllText(Path.Combine(outDir, "batch.txt"), list.ToString());
        Console.WriteLine($"wrote {suites.Length * count} pieces; lysc batch list: {Path.Combine(outDir, "batch.txt")}");
        return 0;
    }
}

/// <summary>A pitch: diatonic number (C4 = 28) and alteration.</summary>
readonly record struct GPitch(int Dia, int Alter)
{
    static readonly int[] Semis = { 0, 2, 4, 5, 7, 9, 11 };
    public int Step => ((Dia % 7) + 7) % 7;
    public int Octave => (int)Math.Floor(Dia / 7.0);
    public int Midi => (Octave + 1) * 12 + Semis[Step] + Alter;
    /// <summary>As an absolute-octave .lys writes it: LilyPond's octaves, c' middle C.</summary>
    public string Lys()
    {
        var sb = new StringBuilder();
        sb.Append("cdefgab"[Step]);
        for (int i = 0; i < Alter; i++) sb.Append("is");
        for (int i = 0; i < -Alter; i++) sb.Append("es");
        int o = Octave - 3;
        sb.Append(o > 0 ? new string('\'', o) : new string(',', -o));
        return sb.ToString();
    }
}

/// <summary>One event: a note or chord (pitches) or a rest (none), and its written value.</summary>
sealed class GEv
{
    public List<GPitch> Pitches = new();   // empty = rest
    public int Ticks;                      // written value (whole = 1920); a tuplet's is scaled at output
    public int TupletN, TupletD;           // part of an N:D group (3:2 a triplet; 5:4, 6:4, 7:4 with --tuplets); 0: none
    public bool Triplet => TupletN > 0;    // in a tuplet group of any kind
    public bool Tie;                       // held into the next event (same pitches)
    public bool Measure;                   // a full-measure rest (R)
    public bool SlurStart, SlurEnd;        // a slur starts at it / ends at it
    public string Marks = "";              // annotations as .lys writes them after the value (@p@cresc)
    public string Grace = "";              // grace notes before it, as .lys writes them (acciaccatura { d''8 }); no truth
    public bool IsRest => Pitches.Count == 0;
    // (a septuplet's sixteenth is no whole number of ticks: the truth's times are fractions of a whole)
    public double Sounding => TupletN > 0 ? Ticks * (double)TupletD / TupletN : Ticks;
    /// <summary>A copy an octave lower (its grace text as it was: a band's guitar has none).</summary>
    public GEv OctaveDown()
    {
        var e = (GEv)MemberwiseClone();
        e.Pitches = Pitches.Select(q => new GPitch(q.Dia - 7, q.Alter)).ToList();
        return e;
    }
}

sealed class GPart
{
    public string Id, Clef, Name;
    public string RemoveEmpty;             // the score's hara-kiri for the staff: "true" (the first system kept), "all", or null
    public bool InGrand;                   // one of the grand staff's (the piano's in a song)
    public string Pedal;                   // how its pedal is drawn: "text" (Ped. *), "bracket", "mixed", or null (Lily#'s default)
    public List<string> Lyrics;            // the words sung under the staff, each bar's syllables (a song's voice), or null
    // bars[b] = voices, each a list of events
    public List<List<List<GEv>>> Bars = new();
}

sealed class GPiece
{
    public string Name, Suite, Time;
    public string Tempo;                   // the tempo line's marking ("Langsam" 4 = 60), or null
    public int Fifths;
    public List<GPart> Parts = new();
    public bool Grand;
    public int BarTicks
    {
        get { var p = Time.Split('/'); return 1920 * int.Parse(p[0]) / int.Parse(p[1]); }
    }
    // a change of key, of meter or of both at one bar (0-based; -1: none): Fifths2, Time2 from there on
    public int ChangeAt = -1;
    public int Fifths2;
    public string Time2;
    // the form (OmrGen --repeats): the bars cut into sections (From inclusive, To exclusive, in
    // the bars' written order), and the form line's text; null: one section, played once
    public List<(string Name, int From, int To)> Sections;
    public string Form;
    /// <summary>The piece as bar b stands: its key and meter.</summary>
    public GPiece At(int b) => ChangeAt < 0 || b < ChangeAt ? this
        : new GPiece { Name = Name, Suite = Suite, Time = Time2, Fifths = Fifths2, Tempo = Tempo, Grand = Grand };
}

static class Writer
{
    static readonly string[] KeyNames = { "ces", "ges", "des", "aes", "ees", "bes", "f", "c", "g", "d", "a", "e", "b", "fis", "cis" };

    /// <summary>The music font of the piece being written (OmrGen --fonts), or null: Lily#'s default.</summary>
    public static string MusicFont;
    public static void Fonts(StringBuilder lys)
    {
        if (MusicFont != null) lys.AppendLine($"fonts {{ music \"{MusicFont}\" }}");
        if (Style != null) lys.Append(Style);
    }

    /// <summary>The engraving settings of the piece being written (OmrGen --vary): paper and
    /// layout blocks, or null: Lily#'s defaults.</summary>
    public static string Style;

    /// <summary>A piece's engraving settings, drawn: a quarter of the pieces keep the defaults, the
    /// others each setting with odds of two in three. The ranges are the proposal's (Lily# P2-P4,
    /// docs/repro/lilysharp-proposal-2026-10-02.md), in Lily#'s units: thicknesses in staff line
    /// thicknesses (lineThickness 0.1 space), so a stem of 0.08-0.2 spaces is 0.8-2.0 over the
    /// staff line's own factor.</summary>
    public static string DrawStyle(Random rng)
    {
        if (rng.NextDouble() < 0.25) return null;
        var inv = CultureInfo.InvariantCulture;
        double U(double lo, double hi) => lo + (hi - lo) * rng.NextDouble();
        bool Pick() => rng.NextDouble() < 2.0 / 3;
        var paper = new List<string>();
        var layout = new List<string>();
        // (every value drawn whether used or not: the draws stay in step)
        double space = U(1.45, 2.0), sds = U(1.5, 3.0), inc = U(0.9, 1.7);
        double staffLine = U(0.7, 1.8), stem = U(0.08, 0.2), stemLen = U(3.0, 4.0), beam = U(0.4, 0.55);
        double damping = Math.Exp(U(Math.Log(0.5), Math.Log(6))), ledger = U(1.6, 2.2), bar = U(1.4, 2.6);
        // (heads from 0.92: smaller, the truth takes them for a tempo mark's note, 0.89)
        double head = U(0.92, 1.15), dots = U(0.315, 0.585), acc = U(0.045, 0.255);
        if (Pick()) paper.Add(string.Format(inv, "staffSpace {0:F3}mm", space));
        if (Pick()) paper.Add(string.Format(inv, "shortestDurationSpace {0:F2}", sds));
        if (Pick()) paper.Add(string.Format(inv, "spacingIncrement {0:F2}", inc));
        bool lines = Pick();
        if (lines) layout.Add(string.Format(inv, "StaffLine.thickness {0:F2}", staffLine));
        // (stems and ledger lines are stated in the staff line's thickness: divide it back out)
        double line = 0.1 * (lines ? staffLine : 1.0);
        if (Pick()) layout.Add(string.Format(inv, "Stem.thickness {0:F2}", stem / line));
        if (Pick()) layout.Add(string.Format(inv, "Stem.lengthFraction {0:F3}", stemLen / 3.5));
        if (Pick()) layout.Add(string.Format(inv, "Beam.thickness {0:F3}", beam));
        if (Pick()) layout.Add(string.Format(inv, "Beam.damping {0:F2}", damping));
        // (a black head's ledger line is 1.3042 spaces wide times 1 + 2 x the fraction)
        if (Pick()) layout.Add(string.Format(inv, "LedgerLine.lengthFraction {0:F3}", (ledger / 1.3042 - 1) / 2));
        if (Pick()) layout.Add(string.Format(inv, "BarLine.thinThickness {0:F2}", bar));
        if (Pick()) layout.Add(string.Format(inv, "NoteHead.scale {0:F3}", head));
        if (Pick()) layout.Add(string.Format(inv, "Dots.padding {0:F3}", dots));
        if (Pick()) layout.Add(string.Format(inv, "Accidental.rightPadding {0:F3}", acc));
        var sb = new StringBuilder();
        if (paper.Count > 0) sb.AppendLine("paper { " + string.Join("  ", paper) + " }");
        if (layout.Count > 0) sb.AppendLine("layout { " + string.Join("  ", layout) + " }");
        return sb.Length > 0 ? sb.ToString() : null;
    }

    /// <summary>The piece's words over the music (OmrGen --headers), drawn from this; null: the title is the piece's name.</summary>
    public static Random HeaderRng;

    static readonly string[] Titles = { "Nocturne in E-flat", "Romance", "Song Without Words", "Prelude No. 3", "Am Abend", "Lullaby",
        "Valse sentimentale", "Waltz in A minor", "Étude", "Barcarolle", "Ständchen", "The Quiet Hour", "Reverie", "Elegy", "Morning Song",
        "Sonatina in G", "Impromptu", "Canzonetta", "Mazurka", "Berceuse", "Abendlied", "Chanson triste", "Les Jeux d'eau", "Air", "Pastorale",
        "Im Frühling", "Spring Rain", "Little March", "Albumblatt", "Serenade", "春の小径", "夕焼けの歌", "月の光に", "小さな舟歌" };
    static readonly string[] Subtitles = { "for piano", "Op. 12, No. 3", "from Album for the Young", "to my teacher", "Op. 7", "für Klavier",
        "second version", "from Scenes of Childhood", "No. 2", "ピアノのための小品", "第2番" };
    static readonly string[] Composers = { "Johann Weber", "Clara Hartmann", "Frédéric Lenoir", "Anna Sørensen", "Pietro Rossi (1801-1867)",
        "Emil Vogt", "Marie Duval", "Henryk Nowak", "Thomas Ashworth", "Sofia Lindqvist", "J. S. Mertens", "山田 花子", "鈴木 健一" };
    static readonly string[] Poets = { "Heinrich Vogel", "Emily Clarke", "Joseph von Arnim", "Victor Marchand", "Rosa Keller", "佐藤 一郎" };
    static readonly string[] TempoNames = { "Andante", "Allegro moderato", "Adagio", "Andantino", "Moderato", "Lento", "Allegretto", "Langsam",
        "Mäßig bewegt", "Tempo di valse", "Con moto", "Larghetto", "ゆっくりと" };

    // (the words, all drawn whether used or not: the draws stay in step)
    static void Header(StringBuilder lys, GPiece p, Random rng)
    {
        string title = Titles[rng.Next(Titles.Length)], subtitle = Subtitles[rng.Next(Subtitles.Length)];
        string composer = Composers[rng.Next(Composers.Length)], poet = Poets[rng.Next(Poets.Length)];
        double s = rng.NextDouble(), c = rng.NextDouble(), w = rng.NextDouble(), t = rng.NextDouble();
        string tempo = TempoNames[rng.Next(TempoNames.Length)];
        int bpm = rng.Next(10, 31) * 4;
        lys.AppendLine($"title \"{title}\"");
        if (s < 0.5) lys.AppendLine($"subtitle \"{subtitle}\"");
        if (c < 0.9) lys.AppendLine($"composer \"{composer}\"");
        if (w < 0.4) lys.AppendLine($"poet \"{poet}\"");
        // a tempo over the first system, unless the piece has its own: a word, a word and a metronome mark, or a mark alone
        if (p.Tempo == null)
        {
            string unit = p.Time.EndsWith("/8") ? "4." : p.Time == "2/2" ? "2" : "4";
            p.Tempo = t < 0.4 ? $"\"{tempo}\"" : t < 0.8 ? $"\"{tempo}\" {unit} = {bpm}" : t < 0.9 ? $"{unit} = {bpm}" : null;
        }
    }

    public static void Write(GPiece p, string dir)
    {
        var lys = new StringBuilder();
        lys.AppendLine($"// Generated by OmrGen ({p.Suite}).");
        if (HeaderRng != null) Header(lys, p, HeaderRng);
        else lys.AppendLine($"title \"{p.Name}\"");
        Fonts(lys);
        lys.AppendLine("octave absolute");
        if (p.Tempo != null) lys.AppendLine($"tempo {p.Tempo}");
        lys.AppendLine($"time {p.Time}");
        lys.AppendLine($"key {KeyNames[p.Fifths + 7]} major");
        lys.AppendLine();
        foreach (var part in p.Parts) lys.AppendLine($"part {part.Id} {{ clef {part.Clef}{(part.Pedal != null ? " pedal " + part.Pedal : "")} }}");
        lys.AppendLine();
        // the sections: one, or the form's (--repeats), each its bars of every part
        var sections = p.Sections ?? new List<(string Name, int From, int To)> { ("Piece", 0, p.Parts[0].Bars.Count) };
        foreach (var (secName, from, to) in sections)
        {
            lys.AppendLine($"section {secName} {{");
            foreach (var part in p.Parts)
            {
                lys.AppendLine($"  {part.Id} {{");
                for (int b = from; b < to; b++)
                {
                    var voices = part.Bars[b];
                    // bars where every part rests, one multi-bar rest (not across a change, nor the section's end)
                    int run = 1;
                    while (AllRest(p, b) && b + run < to && b + run != p.ChangeAt && AllRest(p, b + run)) run++;
                    string music = run > 1 ? MultiRest(p.At(b).BarTicks, run)
                        : voices.Count == 1
                        ? Line(voices[0])
                        : "voice " + string.Join(" ", voices.Select(v => "{ " + Line(v) + " }"));
                    // (a change is written in every part, before the bar's music)
                    string change = b != p.ChangeAt ? ""
                        : (p.Fifths2 != p.Fifths ? $"key {KeyNames[p.Fifths2 + 7]} major " : "") + (p.Time2 != p.Time ? $"time {p.Time2} " : "");
                    lys.AppendLine($"    {change}{music} |");
                    b += run - 1;
                }
                lys.AppendLine("  }");
                if (part.Lyrics != null)
                    lys.AppendLine($"  lyrics {part.Id}words sings {part.Id} {{ {string.Concat(part.Lyrics.Skip(from).Take(to - from).Select(b => b + " | "))}}}");
            }
            lys.AppendLine("}");
        }
        lys.AppendLine();
        lys.AppendLine(p.Form ?? "form { ~Piece }");
        lys.AppendLine();
        lys.AppendLine("score {");
        if (p.Grand) lys.AppendLine("  grandStaff {");
        for (int i = 0; i < p.Parts.Count; i++)
        {
            var part = p.Parts[i];
            // (a song's piano: its staves in a grand staff after the voice's)
            if (part.InGrand && (i == 0 || !p.Parts[i - 1].InGrand)) lys.AppendLine("  grandStaff {");
            string indent = p.Grand || part.InGrand ? "    " : "  ";
            string hide = part.RemoveEmpty != null ? $" as removeEmpty {part.RemoveEmpty}" : "";
            lys.AppendLine($"{indent}staff {part.Id} \"{part.Name}\"{hide}");
            if (part.Lyrics != null) lys.AppendLine($"{indent}lyrics {part.Id}words");
            if (part.InGrand && (i + 1 == p.Parts.Count || !p.Parts[i + 1].InGrand)) lys.AppendLine("  }");
        }
        if (p.Grand) lys.AppendLine("  }");
        lys.AppendLine("}");
        File.WriteAllText(Path.Combine(dir, p.Name + ".lys"), lys.ToString());

        // the OMR's score file: the parts, and what the page should show (for the check)
        var score = new StringBuilder();
        score.AppendLine($"title {p.Name}");
        foreach (var part in p.Parts) score.AppendLine($"part {part.Id} {part.Clef} \"{part.Name}\"");
        score.AppendLine($"bar 1 time {p.Time} key {KeyNames[p.Fifths + 7]}");
        if (p.ChangeAt >= 0) score.AppendLine($"bar {p.ChangeAt + 1} time {p.Time2} key {KeyNames[p.Fifths2 + 7]}");
        File.WriteAllText(Path.Combine(dir, p.Name + ".score"), score.ToString());

        // the truth, one file per staff and voice
        foreach (var part in p.Parts)
        {
            int nv = part.Bars.Max(b => b.Count);
            for (int v = 0; v < nv; v++)
            {
                var sb = new StringBuilder();
                int start = 0;
                for (int b = 0; b < part.Bars.Count; start += p.At(b).BarTicks, b++)
                {
                    if (v >= part.Bars[b].Count) continue;
                    double t = start;
                    foreach (var e in part.Bars[b][v])
                    {
                        string at = (t / 1920.0).ToString("F8", CultureInfo.InvariantCulture);
                        string len = (e.Sounding / 1920.0).ToString("F8", CultureInfo.InvariantCulture);
                        if (e.IsRest) sb.Append(at).Append("\trest\t").Append(TryDur(e.Ticks) ?? $"1*{e.Ticks}/1920").Append('\t').Append(len).Append('\n');
                        else
                            foreach (var q in e.Pitches)
                                sb.Append(at).Append("\tnote\t").Append(q.Midi).Append('\t').Append(DurText(e.Ticks)).Append('\t').Append(len).Append('\n');
                        t += e.Sounding;
                    }
                }
                File.WriteAllText(Path.Combine(dir, $"{p.Name}-{part.Id}-v{v + 1}.notes"), sb.ToString());
            }
        }
    }

    // every part's bar b is one full-measure rest
    static bool AllRest(GPiece p, int b) =>
        p.Parts.All(part => part.Bars[b].Count == 1 && part.Bars[b][0].Count == 1 && part.Bars[b][0][0].Measure);

    // n bars' rest as lysc takes it: R1*n, R2.*n, or of the largest value that divides another
    // meter's bar, R8*(5n) for 5/8 (a whole number after the *)
    static string MultiRest(int barTicks, int n)
    {
        if (TryDur(barTicks) is string one) return $"R{one}*{n}";
        int b = new[] { 1, 2, 4, 8, 16 }.First(b => barTicks % (1920 / b) == 0);
        return $"R{b}*{barTicks / (1920 / b) * n}";
    }

    static string TryDur(int ticks)
    {
        try { return DurText(ticks); } catch (InvalidOperationException) { return null; }
    }

    /// <summary>OmrGen --beams3: a run of plain notes of one value (eighths, sixteenths) beamed by
    /// hand three by three, what is left over two under a beam or one as the meter beams it.</summary>
    public static bool Beams3;

    internal static string Line(List<GEv> evs)
    {
        var parts = new List<string>();
        var beam = new string[evs.Count];
        if (Beams3)
            for (int i = 0; i < evs.Count;)
            {
                bool Plain(GEv e) => !e.Triplet && !e.IsRest && e.Grace == "" && e.Ticks is 240 or 120;
                int j = i;
                while (j < evs.Count && Plain(evs[j]) && evs[j].Ticks == evs[i].Ticks) j++;
                if (j - i >= 3)
                    for (int k = i; k + 1 < j; k += 3) { beam[k] = "["; beam[Math.Min(k + 2, j - 1)] = "]"; }
                i = Math.Max(j, i + 1);
            }
        for (int i = 0; i < evs.Count;)
        {
            if (beam[i] != null) { parts.Add(Ev(evs[i]) + beam[i]); i++; continue; }
            if (!evs[i].Triplet) { parts.Add(Ev(evs[i])); i++; continue; }
            var run = new List<string>();
            int sum = 0;
            // one tuplet group: N of the written value (or its equivalent); the next group of the
            // same kind starts its own
            int n = evs[i].TupletN, dd = evs[i].TupletD;
            int group = evs[i].Ticks * n;
            while (i < evs.Count && evs[i].TupletN == n && evs[i].TupletD == dd && sum < group) { run.Add(Ev(evs[i])); sum += evs[i].Ticks; i++; }
            parts.Add($"tuplet {n}/{dd} {{ " + string.Join(" ", run) + " }");
        }
        return string.Join(" ", parts);
    }

    internal static string Ev(GEv e) => e.Grace +
        (e.IsRest ? (e.Measure ? "R" : "r") : e.Pitches.Count == 1 ? e.Pitches[0].Lys() : "<" + string.Join(" ", e.Pitches.Select(q => q.Lys())) + ">") + DurText(e.Ticks) + e.Marks + (e.Tie ? "~" : "")
        + (e.SlurEnd ? ")" : "") + (e.SlurStart ? "(" : "");

    public static string DurText(int ticks)
    {
        foreach (int b in new[] { 1, 2, 4, 8, 16, 32 })
            for (int d = 0; d <= 2; d++)
            {
                int t = 1920 / b, add = t;
                for (int k = 0; k < d; k++) { add /= 2; t += add; }
                if (t == ticks) return b + new string('.', d);
            }
        throw new InvalidOperationException("no duration for " + ticks);
    }
}
