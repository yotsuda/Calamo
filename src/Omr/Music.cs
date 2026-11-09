using System.Text;
using System.Text.RegularExpressions;

namespace Omr;

/// <summary>Durations in ticks: a whole note = 13440 (1920 x 7: a septuplet's sixteenth, 480/7 of
/// 1920, is a whole number of ticks too, so a bar of them adds up to the bar). Nothing outside
/// counts ticks: the truth's times are fractions of a whole, the side file's lengths too.</summary>
static class Dur
{
    public const int Whole = 1920 * 7;

    public static int Of(int basis, int dots)
    {
        int t = Whole / basis, add = t;
        for (int i = 0; i < dots; i++) { add /= 2; t += add; }
        return t;
    }

    /// <summary>Lily# duration text for ticks ("4.", "8"), or null.</summary>
    public static string Text(int ticks)
    {
        foreach (int b in new[] { 1, 2, 4, 8, 16, 32, 64 })
            for (int dots = 0; dots <= 2; dots++)
                if (Of(b, dots) == ticks) return b + new string('.', dots);
        return null;
    }

    /// <summary>A partial bar's length as Lily# takes it: one note value ("4."), or a count of one
    /// ("8*5", five eighths) when no single value spells it; null when none does.</summary>
    public static string PartialText(int ticks)
    {
        if (Text(ticks) is string one) return one;
        foreach (int b in new[] { 2, 4, 8, 16, 32, 64 })
            if (ticks > 0 && ticks % Of(b, 0) == 0) return $"{b}*{ticks / Of(b, 0)}";
        return null;
    }

    public static int OfTime(string time)
    {
        var p = time.Split('/');
        return Whole * int.Parse(p[0]) / int.Parse(p[1]);
    }
}

/// <summary>A pitch as a diatonic number (C4 = 28) and an alteration in semitones.</summary>
readonly record struct Pitch(int Diatonic, int Alter)
{
    static readonly string Letters = "cdefgab";

    /// <summary>The pitch as an absolute-octave .lys writes it: LilyPond's octaves, c' middle C.</summary>
    public string Lys()
    {
        var sb = new StringBuilder();
        sb.Append(Letters[Mod7(Diatonic)]);
        if (Alter > 0) for (int i = 0; i < Alter; i++) sb.Append("is");
        if (Alter < 0) for (int i = 0; i < -Alter; i++) sb.Append("es");
        int oct = FloorDiv7(Diatonic) - 3;
        sb.Append(oct > 0 ? new string('\'', oct) : new string(',', -oct));
        return sb.ToString();
    }

    public override string ToString() => Lys();

    static readonly int[] StepSemis = { 0, 2, 4, 5, 7, 9, 11 };

    public int Midi() => (FloorDiv7(Diatonic) + 1) * 12 + StepSemis[Mod7(Diatonic)] + Alter;

    /// <summary>A spelling for a MIDI number (black keys as flats).</summary>
    public static Pitch FromMidi(int midi)
    {
        int oct = midi / 12 - 1, pc = midi % 12;
        int[] step = { 0, 1, 1, 2, 2, 3, 4, 4, 5, 5, 6, 6 };
        int[] alter = { 0, -1, 0, -1, 0, 0, -1, 0, -1, 0, -1, 0 };
        return new Pitch(oct * 7 + step[pc], alter[pc]);
    }

    public static int Mod7(int d) => ((d % 7) + 7) % 7;
    public static int FloorDiv7(int d) => (int)Math.Floor(d / 7.0);

    static readonly Regex Rx = new(@"^([a-g])((?:is|es)*)([',]*)$");

    /// <summary>A pitch as <see cref="Lys"/> writes it (LilyPond's octaves: c' is middle C).</summary>
    public static Pitch? Parse(string text)
    {
        var m = Rx.Match(text);
        if (!m.Success) return null;
        int step = Letters.IndexOf(m.Groups[1].Value[0]);
        string acc = m.Groups[2].Value;
        int alter = 0;
        for (int i = 0; i + 1 < acc.Length; i += 2) alter += acc.Substring(i, 2) == "is" ? 1 : -1;
        int oct = 3;
        foreach (char ch in m.Groups[3].Value) oct += ch == '\'' ? 1 : -1;
        return new Pitch(oct * 7 + step, alter);
    }
}

/// <summary>One rhythmic event of a part in a bar: a note, a rest, or a full-bar rest.</summary>
sealed class Ev
{
    public bool IsRest, FullBar;
    public bool Blank;                          // OMR: the rest of a measure left blank (no rest written)
    public int MultiBars;                       // OMR: a full-bar rest that is a multi-bar rest's: its first bar holds the count (R1*n), the bars after it -1
    public bool Slash;                          // OMR: a rhythm slash (a duration, no pitch: the truth's .notes has none)
    public Pitch P;
    public int Ticks;
    public double X;          // page x (OMR only)
    public (int Num, int Den) Scale = (1, 1);   // tuplet: ticks = written * Num / Den
    public int Tuplet;                          // OMR: the tuplet number it stands under (0: none; below 0: a triplet read from the bar's length, no number printed)
    public int Beam;                            // OMR: the ink piece of its beamed group (0: no beam)
    public List<Pitch> Chord;                   // two or more pitches, low to high; P = the top
    public bool MidiOnly;                       // truth knows the sounding pitch, not its spelling
    public List<Pitch> Tied;                    // the notes a tie starts at, low to high (a chord's: some of them, or all), or null: none
    public bool Tie => Tied != null;            // held into the next event (a tie starts at one of its notes at least)
    public int BeamRun;                         // OMR: the run of stems beams really join within its ink piece (Head's; Reader.KneeBeam)
    public bool SlurStart, SlurEnd;             // OMR: a slur starts / ends at it
    public bool SlurStartAcross, SlurEndAcross; // OMR: a sure slur start / end at it that nothing on its staff pairs with (Head's): a line handed to the neighbouring staff (Reader.HandedGroups)
    public bool PhraseStart, PhraseEnd;         // OMR: that slur is written as a phrasing slur: it runs over notes with syllables of their own, which a slur would hold
    public int Stem;                            // OMR: +1 stem up, -1 stem down, 0 none (rest, whole note)
    public int StemForced;                      // OMR: +1 @stemUp, -1 @stemDown: its voice is written out of order, for the words on it
    public double Y;                            // OMR: page y (a rest's height hints at its voice)
    public int PosLo, PosHi;                    // OMR: its lowest and highest head's place in steps from the staff's middle line (up positive): the way a stem of its own would go
    public double StemX;
    public double StemLo, StemHi;               // OMR: the page rows its heads and stem run over (a chord's, all of them)
    public List<EvAlt> Alts;                   // OMR: other readings of its heads (a todo's candidates); shared by its copies
    public List<int> Bare;                      // OMR: the steps of its heads with no sign, none held in the bar before (a tie into it sets their alteration)
    public List<Pitch> Graces;                  // OMR: the grace notes before it, left to right (no time; the .lys writes them before it)
    public string GraceKind;                    // OMR: how they are written (acciaccatura, appoggiatura, grace), or null: by their count
    public int GraceValue;                      // OMR: their written value (8, 16), or 0: by their count
    public int OttavaStart;                     // OMR: an octave line starts at it: 1, 2 octaves up (8va, 15ma), -1, -2 down (8vb, 15mb); 0 none (its pitches are as sounded)
    public int OttavaEnd;                       // OMR: the last event under an octave line: the line it closes, as OttavaStart (1 @!8va, -2 @!15mb: Lily# closes a span by its opening name), 0 none
    public string Dynamic;                      // OMR: its dynamic as the .lys writes it after the '@' (mf, p.up), or null
    public string HairpinStart, HairpinEnd;     // OMR: a hairpin ("cresc", "decresc") starts at it / ends on it (no dynamic there to end it)
    public bool PedalDown, PedalUp;             // OMR: the pedal pressed at it (again, while down: a change) / released at it
    public List<string> Articulations;          // OMR: its articulations as the .lys names them (staccato, accent, fermata), or null
    public string ClefBefore;                   // OMR: a clef printed just before it inside the measure (treble, bass, ...): the .lys writes "clef x" before it
    public bool OctaveClef;                     // OMR: read under an octave clef (treble_8, bass_8): its pitches are as written, and it sounds, and the .lys writes it, an octave lower
    public int Tremolo;                         // OMR: a two-note tremolo's note (Ticks: half its written value): the strokes between the two
    public bool TremoloFirst;                   // OMR: the first of the two
    public List<Lyrics.Syllable> Words;         // OMR: the syllables sung on it, a verse each (the copies share them), or null
    public bool Cue;                            // OMR: a cue's note or rest, small over the part's own full-bar rest (the .lys writes its voice in cue { })
    public bool Held;                           // OMR: another voice's held note written on a beamed chord's stem (Head.Held): its value from the bar's time (Reader.HeldNotes)
    public int DrawnOn = -1;                    // OMR: the part whose staff it is drawn on, its beam group this part's (Reader.CrossBeams; -1: its own): the .lys writes @cross
    public bool CrossStaff => DrawnOn >= 0;

    public Ev Copy()
    {
        var e = (Ev)MemberwiseClone();
        if (Chord != null) e.Chord = new List<Pitch>(Chord);
        if (Tied != null) e.Tied = new List<Pitch>(Tied);
        return e;
    }

    /// <summary>Its notes: the chord's, or the one.</summary>
    public List<Pitch> Pitches => Chord ?? new List<Pitch> { P };

    /// <summary>A tie starts at the note p (one of its notes; the chord's tie, at every note of it).</summary>
    public void TieAt(Pitch p)
    {
        Tied ??= new List<Pitch>();
        if (!Tied.Contains(p)) { Tied.Add(p); Tied.Sort((a, b) => a.Diatonic.CompareTo(b.Diatonic)); }
    }

    /// <summary>Its note was respelled or moved (was -> now): a tie at it moves with it.</summary>
    public void Respell(Pitch was, Pitch now)
    {
        if (Tied == null) return;
        int k = Tied.IndexOf(was);
        if (k >= 0) Tied[k] = now;
    }

    public string Show() => FullBar ? "R"
        : (IsRest ? "r" : Slash ? "/" : Chord != null ? "<" + string.Join(" ", Chord.Select(p => p.Lys())) + ">" : P.Lys()) + (Dur.Text(Ticks) ?? $"[{Ticks}]");
}

/// <summary>
/// OMR: another reading of an event, from one of its heads (Head.Alts): its pitch From read as To,
/// or a dot more or less (DotsBy). Where it is: page pixels (L, T, R, B: the head, with its sign
/// or dot), and on the page as loaded (Page, Box, set when the page is done).
/// </summary>
sealed class EvAlt
{
    public string What;
    public Pitch From, To;
    public int DotsBy;
    public double Score;
    public double L, T, R, B;
    public int Page;
    public int[] Box;
}

static class Keys
{
    static readonly int[] SharpOrder = { 3, 0, 4, 1, 5, 2, 6 };   // F C G D A E B
    static readonly int[] FlatOrder = { 6, 2, 5, 1, 4, 0, 3 };    // B E A D G C F

    /// <summary>The key signature's alteration of each step (c..b).</summary>
    public static int[] Alterations(int fifths)
    {
        var a = new int[7];
        for (int i = 0; i < Math.Abs(fifths); i++)
            a[fifths > 0 ? SharpOrder[i] : FlatOrder[i]] = fifths > 0 ? 1 : -1;
        return a;
    }

    public static string MajorName(int fifths)
    {
        string[] names = { "ces", "ges", "des", "aes", "ees", "bes", "f", "c", "g", "d", "a", "e", "b", "fis", "cis" };
        return names[fifths + 7] + " major";
    }

    public static int ParseMajor(string name)
    {
        for (int f = -7; f <= 7; f++) if (MajorName(f) == name + " major" || MajorName(f) == name) return f;
        throw new FormatException("key " + name);
    }
}

static class Clefs
{
    /// <summary>Diatonic number of the middle line (written pitch).</summary>
    public static int MiddleLine(string clef) => clef switch
    {
        "treble" or "treble_8" or "percussion" => 4 * 7 + 6,   // B4 (written; a drum's place as on a treble staff)
        "alto" => 4 * 7 + 0,     // C4
        "tenor" => 3 * 7 + 5,    // A3
        "bass" or "bass_8" => 3 * 7 + 1, // D3
        _ => throw new FormatException("clef " + clef),
    };
}
