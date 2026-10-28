namespace OmrGen;

/// <summary>
/// Makes one test piece of a suite. The music is plausible rather than random: a melody walks
/// the key's scale (mostly steps, some leaps, a few chromatic notes), rhythms are patterns that
/// fit the meter's beats, rests are occasional.
/// </summary>
static class Gen
{
    public static readonly string[] Suites = { "basic", "ledger", "tuplets", "chords", "voices2", "piano", "meters", "ties", "slurs" };

    static readonly string[] AllTimes = { "4/4", "3/4", "2/4", "6/8", "3/8", "12/8", "2/2", "5/4", "9/8" };
    // every digit a time signature uses, in both numbers (Lily# draws 4/4 and 2/2 as C signs)
    static readonly string[] MeterTimes = { "2/4", "3/4", "4/4", "5/4", "6/4", "7/4", "9/4", "3/8", "5/8", "6/8", "7/8", "9/8", "12/8", "2/2", "3/2", "4/2" };
    static readonly string[] SimpleTimes = { "4/4", "3/4", "2/4", "2/2" };
    // the "times" suite: training data for the detector's digit classes (S11), every digit 1-9 in
    // its turn, in both numbers, the rare 1 and 7 oftener; only the denominators the rhythms know
    // (the C signs of 4/4 and 2/2 are left to the other suites)
    static readonly string[] TimesTimes =
    {
        "1/4", "1/2", "12/8", "11/8", "12/4", "11/4", "13/8", "14/8", "15/8", "16/8",
        "7/4", "7/8", "7/2", "7/4", "7/8", "5/4", "5/8", "5/2", "9/8", "9/4", "9/2", "9/8",
        "6/8", "6/4", "6/2", "3/4", "3/8", "3/2", "2/4", "2/8", "4/8", "4/2", "8/8", "8/4",
    };
    static readonly string[] Clefs = { "treble", "treble", "bass", "bass", "alto", "tenor" };

    internal static int MiddleLine(string clef) => clef switch
    {
        "treble" => 34, "alto" => 28, "tenor" => 26, "bass" => 22, _ => 34,
    };

    public static GPiece Make(string suite, string name, Random rng, int bars)
    {
        var p = new GPiece { Name = name, Suite = suite, Fifths = rng.Next(-6, 7) };
        p.Time = suite == "tuplets" ? Pick(rng, SimpleTimes) : suite == "meters" ? Pick(rng, MeterTimes)
               : suite == "times" ? Pick(rng, TimesTimes) : Pick(rng, AllTimes);
        switch (suite)
        {
            case "piano":
                p.Grand = true;
                p.Parts.Add(Melodic(p, rng, bars, "rh", "treble", "Right", width: 6, voices: 0.5, chords: 0.15));
                p.Parts.Add(Melodic(p, rng, bars, "lh", "bass", "Left", width: 6, voices: 0, chords: 0.25));
                break;
            case "lied":
                Lied(p, rng, bars);
                break;
            default:
                string clef = Pick(rng, Clefs);
                p.Parts.Add(Melodic(p, rng, bars, "p1", clef, "Staff",
                    width: suite == "ledger" ? 11 : 6,
                    voices: suite == "voices2" ? 0.85 : suite == "slurs" ? 0.3 : 0,
                    chords: suite == "chords" ? 0.6 : suite == "ties" ? 0.2 : suite == "slurs" ? 0.15 : 0,
                    triplets: suite == "tuplets" ? 0.35 : suite == "slurs" ? 0.05 : 0.02,
                    leaps: suite == "ledger" ? 0.5 : 0.25,
                    ties: suite == "ties" ? 0.3 : suite == "slurs" ? 0.1 : 0,
                    slurs: suite == "slurs" ? 0.35 : 0));
                break;
        }
        return p;
    }

    // a full-measure rest's value must be one written value
    static readonly string[] LiedTimes = { "4/4", "4/4", "3/4", "2/4", "6/8", "2/2", "12/8", "3/8" };

    /// <summary>
    /// The "lied" suite: a song, a voice over a piano's grand staff. The voice rests for whole
    /// stretches (an introduction, interludes, a postlude), and the score hides its staff in the
    /// systems where it only rests (Lily#'s removeEmpty: "true" keeps it in the first system, "all"
    /// hides it there too), so a system shows two staves or three. Not one of <see cref="Suites"/>
    /// (asked for by name). Half again as many bars as asked, so that a stretch fills a system.
    /// </summary>
    static void Lied(GPiece p, Random rng, int bars)
    {
        bars = bars * 3 / 2;
        p.Fifths = rng.Next(-5, 6);
        p.Time = Pick(rng, LiedTimes);
        if (Changes)
        {
            // a change of key (a step or three round the circle of fifths), of meter, or of both,
            // somewhere in the middle third
            p.ChangeAt = bars / 3 + rng.Next(bars / 3);
            double c = rng.NextDouble();
            p.Fifths2 = c < 0.75 ? Math.Clamp(p.Fifths + (rng.Next(2) == 0 ? -1 : 1) * rng.Next(1, 4), -6, 6) : p.Fifths;
            if (p.Fifths2 == p.Fifths) p.Fifths2 = p.Fifths > 0 ? p.Fifths - 2 : p.Fifths + 2;
            p.Time2 = c > 0.5 ? LiedTimes.Where(t => t != p.Time).ElementAt(rng.Next(LiedTimes.Count(t => t != p.Time))) : p.Time;
            if (c >= 0.75) p.Fifths2 = p.Fifths;
        }
        var vo = Melodic(p, rng, bars, "vo", "treble", "Voice", width: 5, voices: 0, chords: 0, triplets: 0.02, leaps: 0.2, ties: 0.05, slurs: 0.15);
        vo.RemoveEmpty = rng.NextDouble() < 0.5 ? "all" : "true";
        // the voice's rests: an introduction of up to six bars, one or two interludes of three to
        // eight, now and then a postlude
        var silent = new bool[bars];
        void Rest(int from, int len) { for (int b = Math.Max(0, from); b < Math.Min(bars, from + len); b++) silent[b] = true; }
        Rest(0, rng.Next(0, 7));
        for (int k = rng.Next(1, 3), at = bars / 3; k > 0; k--, at += bars / 3) Rest(at + rng.Next(-2, 3), rng.Next(3, 9));
        if (rng.NextDouble() < 0.4) Rest(bars - rng.Next(2, 5), 4);
        for (int b = 0; b < bars; b++)
            if (silent[b]) vo.Bars[b] = new() { new() { new GEv { Ticks = p.At(b).BarTicks, Measure = true } } };
        // (a tie or a slur that ran into a silent bar ends where the singing does)
        // (and one that came out of a silent bar ends nowhere)
        var line = vo.Bars.SelectMany(b => b.SelectMany(v => v)).ToList();
        for (int i = 0; i < line.Count; i++)
        {
            if (line[i].Tie && (i + 1 == line.Count || line[i + 1].IsRest)) line[i].Tie = false;
            if (!line[i].SlurStart) continue;
            int j = i + 1;
            while (j < line.Count && !line[j].SlurEnd && !line[j].IsRest) j++;
            if (j == line.Count || line[j].IsRest) line[i].SlurStart = false;
        }
        bool open = false;
        foreach (var e in line)
        {
            if (e.SlurEnd) { e.SlurEnd = open; open = false; }
            if (e.SlurStart) open = true;
        }
        p.Parts.Add(vo);
        // (--piano: a fuller accompaniment, more of it in two voices, octaves and four-note chords)
        var rh = Melodic(p, rng, bars, "rh", "treble", "Piano", width: 6, voices: Piano ? 0.6 : 0.3, chords: Piano ? 0.5 : 0.3, full: Piano);
        var lh = Melodic(p, rng, bars, "lh", "bass", "", width: 6, voices: Piano ? 0.3 : 0, chords: Piano ? 0.45 : 0.25, full: Piano);
        rh.InGrand = lh.InGrand = true;
        p.Parts.Add(rh);
        p.Parts.Add(lh);
        // (drawn after the music, so that the words leave the notes as they were)
        if (Lyrics && rng.NextDouble() < 0.8) vo.Lyrics = Words(vo, rng);
        if (Dynamics)
        {
            // (the voice's above its staff, as songs print them, clear of the words)
            Expression(vo, rng, ".up");
            Expression(rh, rng, "");
            if (rng.NextDouble() < 0.3) Expression(lh, rng, "");
        }
        if (Texts)
        {
            // a tempo marking over the first system: a word, a word and a metronome mark, or a mark alone
            string word = Pick(rng, TempoWords), unit = p.Time.EndsWith("/8") ? "4." : p.Time == "2/2" ? "2" : "4";
            double t = rng.NextDouble();
            p.Tempo = t < 0.5 ? $"\"{word}\"" : t < 0.85 ? $"\"{word}\" {unit} = {rng.Next(10, 31) * 4}" : $"{unit} = {rng.Next(10, 31) * 4}";
            // and the words of expression, the voice's above it, the piano's below its notes
            TextMarks(vo, rng, ".up");
            TextMarks(rh, rng, "");
        }
        if (Graces) { GraceNotes(vo, rng); GraceNotes(rh, rng); }
    }

    // OmrGen --texts: a tempo marking and words of expression in lied
    public static bool Texts;
    // OmrGen --piano: lied's piano fuller
    public static bool Piano;
    // OmrGen --changes: lied changes key or meter (or both) once
    public static bool Changes;
    // OmrGen --grace: grace notes before some of lied's notes (the voice's and the right hand's)
    public static bool Graces;

    /// <summary>
    /// Grace notes before a note now and then: a slashed one (acciaccatura), an unslashed one
    /// (appoggiatura), or two sixteenths leading in; a step or two above or below the note.
    /// </summary>
    static void GraceNotes(GPart part, Random rng)
    {
        foreach (var e in part.Bars.SelectMany(b => b[0]))
        {
            if (e.IsRest || e.Triplet || rng.NextDouble() >= 0.04) continue;
            var to = e.Pitches[^1];
            GPitch Near(int d) => new(to.Dia + d, 0);
            double r = rng.NextDouble();
            e.Grace = r < 0.4 ? $"acciaccatura {{ {Near(rng.Next(2) == 0 ? 1 : -1).Lys()}8 }} "
                    : r < 0.7 ? $"appoggiatura {{ {Near(1).Lys()}8 }} "
                    : $"grace {{ {Near(-2).Lys()}16 {Near(-1).Lys()}16 }} ";
        }
    }
    static readonly string[] TempoWords = { "Langsam", "Mäßig", "Ziemlich langsam", "Sehr innig", "Lebhaft", "Andante", "Moderato", "Allegretto", "Nicht schnell", "Innig, nicht zu schnell" };
    static readonly string[] ExpressionWords = { "dolce", "espress.", "rit.", "a tempo", "leise", "zart", "poco rit.", "cresc.", "dim.", "ritard.", "sempre p", "con moto" };

    static void TextMarks(GPart part, Random rng, string side)
    {
        foreach (var e in part.Bars.SelectMany(b => b[0]).Where(e => !e.IsRest))
            if (rng.NextDouble() < 0.025) e.Marks += $"@text(\"{Pick(rng, ExpressionWords)}\"){side}";
    }

    // OmrGen --dynamics: dynamics and hairpins in lied
    public static bool Dynamics;
    static readonly string[] Levels = { "pp", "p", "p", "mp", "mf", "mf", "f", "f", "ff", "sf", "fp" };

    /// <summary>
    /// Dynamics and hairpins on a part's notes: now and then a level, and now and then a hairpin
    /// from a note to a level two to six notes on (Lily# runs one to the next dynamic).
    /// </summary>
    static void Expression(GPart part, Random rng, string side)
    {
        var notes = part.Bars.SelectMany(b => b[0]).Where(e => !e.IsRest).ToList();
        for (int i = 0; i < notes.Count; i++)
        {
            double r = rng.NextDouble();
            if (r < 0.06) notes[i].Marks += "@" + Pick(rng, Levels) + side;
            else if (r < 0.1 && i + 2 < notes.Count)
            {
                int j = Math.Min(notes.Count - 1, i + rng.Next(2, 7));
                notes[i].Marks += rng.NextDouble() < 0.5 ? "@cresc" : "@decresc";
                notes[j].Marks += "@" + Pick(rng, Levels) + side;
                i = j;
            }
        }
    }

    // OmrGen --marks: dynamics, hairpins and the pedal on any suite's music (S14: the detector's
    // training data for them), drawn from a stream of their own after the music: the same seed
    // writes the same notes with or without them (no sfp: Lily# draws none, LYS1008)
    static readonly string[] AllLevels = { "ppp", "pp", "p", "p", "mp", "mf", "mf", "f", "f", "ff", "fff", "sf", "sfz", "fp", "fz", "rfz" };

    public static void AddMarks(GPiece p, Random rng)
    {
        foreach (var part in p.Parts)
        {
            // (lied --dynamics has its own)
            if (Dynamics && p.Suite == "lied") continue;
            // the voice's above its staff, as songs print them; an instrument's below, now and then above
            string side = part.Id == "vo" ? ".up" : rng.NextDouble() < 0.15 ? ".up" : "";
            var notes = part.Bars.SelectMany(b => b[0]).Where(e => !e.IsRest).ToList();
            for (int i = 0; i < notes.Count; i++)
            {
                double r = rng.NextDouble();
                if (r < 0.08) notes[i].Marks += "@" + Pick(rng, AllLevels) + side;
                else if (r < 0.14 && i + 2 < notes.Count)
                {
                    // a hairpin to a level two to eight notes on, or ended on a note with no level (@!cresc)
                    int j = Math.Min(notes.Count - 1, i + rng.Next(2, 9));
                    string kind = rng.NextDouble() < 0.5 ? "cresc" : "decresc";
                    notes[i].Marks += (rng.NextDouble() < 0.3 ? "@" + Pick(rng, AllLevels) + side : "") + "@" + kind;
                    notes[j].Marks += rng.NextDouble() < 0.6 ? "@" + Pick(rng, AllLevels) + side : "@!" + kind;
                    i = j;
                }
            }
        }
        // the pedal under a piano's left hand (the grand staff's lower staff), in half the pieces
        var lh = p.Parts.FirstOrDefault(x => x.Id == "lh");
        if (lh == null || rng.NextDouble() < 0.5) return;
        double style = rng.NextDouble();
        lh.Pedal = style < 0.5 ? "text" : style < 0.8 ? "bracket" : "mixed";
        var held = lh.Bars.SelectMany((bar, b) => bar[0].Where(e => !e.IsRest).Select(e => (Ev: e, Bar: b))).ToList();
        for (int i = 0; i < held.Count; i++)
        {
            if (rng.NextDouble() < 0.5) continue;
            // held to the end of this bar or the next, changed now and then at a bar's first note
            int endBar = held[i].Bar + rng.Next(0, 2);
            int j = held.FindLastIndex(h => h.Bar <= endBar);
            if (j <= i) continue;
            held[i].Ev.Marks += "@sustain";
            for (int k = i + 1; k < j; k++)
                if (held[k].Bar != held[k - 1].Bar && rng.NextDouble() < 0.6) held[k].Ev.Marks += "@sustain";
            held[j].Ev.Marks += "@!sustain";
            i = j;
        }
    }

    // OmrGen --artic: articulations on every suite's notes (S15: the detector's training data for
    // them), from a stream of their own after the music like --marks. No portato or stopped:
    // rare (--scripts2 has them)
    static readonly (string Name, int Weight)[] Articulations =
        { ("staccato", 36), ("accent", 24), ("tenuto", 14), ("marcato", 8), ("staccatissimo", 8), ("accent@staccato", 5), ("tenuto@accent", 3), ("fermata", 2) };

    public static void AddArticulations(GPiece p, Random rng)
    {
        int total = Articulations.Sum(a => a.Weight);
        string Draw()
        {
            int r = rng.Next(total);
            foreach (var (name, w) in Articulations) { if (r < w) return name; r -= w; }
            return "staccato";
        }
        foreach (var part in p.Parts)
            foreach (var voice in Enumerable.Range(0, part.Bars.Max(b => b.Count)))
            {
                var notes = part.Bars.Where(b => voice < b.Count).SelectMany(b => b[voice]).Where(e => !e.IsRest).ToList();
                for (int i = 0; i < notes.Count; i++)
                {
                    if (rng.NextDouble() >= 0.14) continue;
                    // a run of one to four notes with the same mark (staccato eighths, accented beats),
                    // now and then forced above or below the note
                    string name = Draw(), side = rng.NextDouble() < 0.1 ? (rng.NextDouble() < 0.5 ? ".up" : ".down") : "";
                    int run = name == "fermata" ? 1 : rng.Next(1, 5);
                    for (int k = i; k < Math.Min(notes.Count, i + run); k++)
                        notes[k].Marks += string.Concat(name.Split('@').Select(n => "@" + n + side));
                    i += run;
                }
                // a fermata over the last note, in a piece in four
                if (voice == 0 && notes.Count > 0 && rng.NextDouble() < 0.25 && !notes[^1].Marks.Contains("@fermata"))
                    notes[^1].Marks += "@fermata";
            }
    }

    // OmrGen --ornaments: ornaments on every suite's notes (S23: the detector's training data for
    // them), from a stream of their own after the music like --artic: the trill's tr, the mordent,
    // the prall, the turn and the reverse turn, which Lily# sets above the staff whatever the stem.
    // One on a note in ten, never on a rest, on a note with a mark already, or on a note tied into.
    // No pralltriller: rare (--scripts2 has it)
    static readonly (string Name, int Weight)[] Ornaments =
        { ("trill", 30), ("mordent", 20), ("prall", 20), ("turn", 15), ("reverseTurn", 15) };

    public static void AddOrnaments(GPiece p, Random rng)
    {
        int total = Ornaments.Sum(o => o.Weight);
        foreach (var part in p.Parts)
            foreach (var voice in Enumerable.Range(0, part.Bars.Max(b => b.Count)))
            {
                var line = part.Bars.Where(b => voice < b.Count).SelectMany(b => b[voice]).ToList();
                for (int i = 0; i < line.Count; i++)
                {
                    // (every value drawn whether used or not: the draws stay in step)
                    double r = rng.NextDouble();
                    int k = rng.Next(total);
                    var e = line[i];
                    if (r >= 0.1 || e.IsRest || e.Marks.Contains('@') || (i > 0 && line[i - 1].Tie)) continue;
                    string name = Ornaments[0].Name;
                    foreach (var (n, w) in Ornaments) { if (k < w) { name = n; break; } k -= w; }
                    e.Marks += "@" + name;
                }
            }
    }

    // OmrGen --scripts2: the rarer scripts --artic and --ornaments leave out, the portato, the stopped
    // note's + and the pralltriller, from a stream of their own after the music like --artic. One on a
    // note in twelve, never on a rest, on a note with a mark already, or on a note tied into; a portato
    // now and then on a run of up to four notes (as under a slur), now and then forced above or below
    static readonly (string Name, int Weight)[] Scripts2 = { ("portato", 40), ("stopped", 25), ("pralltriller", 35) };

    public static void AddScripts2(GPiece p, Random rng)
    {
        int total = Scripts2.Sum(o => o.Weight);
        foreach (var part in p.Parts)
            foreach (var voice in Enumerable.Range(0, part.Bars.Max(b => b.Count)))
            {
                var line = part.Bars.Where(b => voice < b.Count).SelectMany(b => b[voice]).ToList();
                for (int i = 0; i < line.Count; i++)
                {
                    // (every value drawn whether used or not: the draws stay in step)
                    double r = rng.NextDouble(), s = rng.NextDouble();
                    int k = rng.Next(total), run = rng.Next(1, 5);
                    var e = line[i];
                    if (r >= 1 / 12.0 || e.IsRest || e.Marks.Contains('@') || (i > 0 && line[i - 1].Tie)) continue;
                    string name = Scripts2[0].Name;
                    foreach (var (n, w) in Scripts2) { if (k < w) { name = n; break; } k -= w; }
                    string side = name != "pralltriller" && s < 0.1 ? (s < 0.05 ? ".up" : ".down") : "";
                    if (name != "portato") run = 1;
                    for (int j = i; j < Math.Min(line.Count, i + run); j++)
                    {
                        if (j > i && (line[j].IsRest || line[j].Marks.Contains('@'))) break;
                        line[j].Marks += "@" + name + side;
                        i = j;
                    }
                }
            }
    }

    // OmrGen --spans: text spanners (rit., accel., rall. and words of the vocabulary's, a dashed line
    // after them) over stretches of a part's first voice, from a stream of their own after the music
    // like --artic. Lily# sets the words in italic over the staff at the first note's head and runs the
    // dashes to the last note's (the note the @!rit stands on), across barlines and systems. A span of
    // two to twelve notes, one open at a time, now and then. On every part's staff (until Lily#'s
    // 1aca41cd2, 2026-10-20, a lower staff's span was drawn from the upper staff's columns and the
    // first part's only were written: docs/repro/textspan-grandstaff-lower-2026-10-19.lys)
    static readonly (string Start, string End, int Weight)[] Spans =
    {
        ("@rit", "@!rit", 35), ("@accel", "@!accel", 20), ("@rall", "@!rall", 20),
        ("@textSpan(\"poco rit.\")", "@!textSpan", 10), ("@textSpan(\"molto rit.\")", "@!textSpan", 5),
        ("@textSpan(\"poco accel.\")", "@!textSpan", 5), ("@textSpan(\"allargando\")", "@!textSpan", 5),
    };

    public static void AddSpans(GPiece p, Random rng)
    {
        int total = Spans.Sum(s => s.Weight);
        foreach (var part in p.Parts)
        {
            var notes = part.Bars.SelectMany(b => b[0]).Where(e => !e.IsRest).ToList();
            for (int i = 0; i < notes.Count; i++)
            {
                // (every value drawn whether used or not: the draws stay in step)
                double r = rng.NextDouble();
                int k = rng.Next(total), len = rng.Next(2, 13);
                if (r >= 0.05 || i + len >= notes.Count) continue;
                var (start, end, _) = Spans[^1];
                foreach (var sp in Spans) { if (k < sp.Weight) { (start, end, _) = sp; break; } k -= sp.Weight; }
                notes[i].Marks += start;
                notes[i + len].Marks += end;
                // (the next may start on the note this one ends on: Lily# pairs the stop before the start)
                i += len - 1;
            }
        }
    }

    // OmrGen --trills: trill lines (a "tr" and a wavy line after it) over stretches of a part's first
    // voice, from a stream of their own after the music like --spans. Lily# sets the "tr" over the
    // staff at the first note and runs the wave to just before the note after the @!trillSpan's,
    // across barlines and systems. A line of one to six notes, one open at a time, now and then. On
    // every part's staff (until Lily#'s 1aca41cd2, 2026-10-20, a lower staff's line was drawn from the
    // upper staff's columns and the first part's only were written:
    // docs/repro/trillspan-grandstaff-lower-2026-10-19.lys)
    public static void AddTrills(GPiece p, Random rng)
    {
        foreach (var part in p.Parts)
        {
            var notes = part.Bars.SelectMany(b => b[0]).Where(e => !e.IsRest).ToList();
            for (int i = 0; i < notes.Count; i++)
            {
                // (every value drawn whether used or not: the draws stay in step)
                double r = rng.NextDouble();
                int len = rng.Next(1, 7);
                if (r >= 0.06 || i + len >= notes.Count) continue;
                notes[i].Marks += "@trillSpan";
                // (closed on the last note under the wave: Lily#'s @! end since 2026-10-28. The next may
                // start on the note after, the wave run to it)
                notes[i + len - 1].Marks += "@!trillSpan";
                i += len - 1;
            }
        }
    }

    // OmrGen --arpeggios: arpeggios (a wavy line up the chord's left) on chords of every part and
    // voice, from a stream of their own after the music like --trills. Lily# stacks the wiggle half a
    // space left of the chord's heads or its accidentals, from half a space under its lowest head to
    // past its highest. A chord in four, never a chord tied into (Lily# draws it again). (Until Lily#'s
    // 1aca41cd2, 2026-10-20, none was drawn in a tuplet and a second voice's was drawn at the first
    // voice's event of its index, and neither was written: docs/repro/arpeggio-in-tuplet-2026-10-20.lys,
    // arpeggio-second-voice-2026-10-20.lys)
    public static void AddArpeggios(GPiece p, Random rng)
    {
        foreach (var part in p.Parts)
            foreach (var voice in Enumerable.Range(0, part.Bars.Max(b => b.Count)))
            {
                var line = part.Bars.Where(b => voice < b.Count).SelectMany(b => b[voice]).ToList();
                for (int i = 0; i < line.Count; i++)
                {
                    // (every value drawn whether used or not: the draws stay in step)
                    double r = rng.NextDouble();
                    if (r >= 0.25 || line[i].Pitches.Count < 2 || (i > 0 && line[i - 1].Tie)) continue;
                    line[i].Marks += "@arpeggio";
                }
            }
    }

    // OmrGen --glissandi: glissandi (a straight line from a note's head to the next note's) between
    // notes of a part's first voice, from a stream of their own after the music like --arpeggios.
    // Lily# draws the line from the head's right to the next head's left (or its accidentals'), each
    // end pulled half a space in along it, and none at all where that leaves nothing: so only from a
    // quarter or longer (room between the heads), to a note two steps and more away (a slant, no
    // tie's level; a chord's heads paired with the next one's in written order, a line a pair), the
    // two notes out of tuplets, and never to a note with grace notes before it. Within a bar: a line
    // over a system's end Lily# draws as two level pieces, at the first note's height and at the
    // next one's (its own way, LilyPond slopes them as one line)
    public static void AddGlissandi(GPiece p, Random rng)
    {
        foreach (var part in p.Parts)
            foreach (var line in part.Bars.Select(b => b[0]))
            for (int i = 0; i + 1 < line.Count; i++)
            {
                // (every value drawn whether used or not: the draws stay in step)
                double r = rng.NextDouble();
                GEv e = line[i], n = line[i + 1];
                if (r >= 0.35 || e.IsRest || n.IsRest || e.Measure || n.Measure || e.Tie || e.Triplet || n.Triplet || n.Grace != "" || e.Ticks < 480
                    || e.Pitches.Zip(n.Pitches).Any(q => Math.Abs(q.First.Dia - q.Second.Dia) < 2)) continue;
                e.Marks += "@glissando";
            }
    }

    // OmrGen --ottava: octave lines (8va, 8vb, 15ma, 15mb) over stretches of a part's notes (S18: the
    // detector's training data for them), from a stream of their own after the music like --marks.
    // The notes under a line are written as they sound, an octave or two up or down, and Lily# draws
    // them where the music had them: the same seed draws the same page with the line added, and the
    // truth's pitches move with it. A treble (or alto) staff's lines run mostly up, a bass staff's
    // mostly down; one in five is 15ma or 15mb. A line starts at a note and ends before a note (Lily#'s
    // @!8va goes on the last event under the line; a line left open is an error, LYS4018),
    // over bars of one voice (Lily# would move the other voice's notes too), cutting no tie, over no
    // grace note (its pitches are written beside the note's and would not move)
    // (--ottava-15: the share of the lines that are 15ma or 15mb. A fifth by default; the detector's
    // training sets raise it, since 15ma and 15mb were few in them and read at 33-75%)
    public static double Ottava15 = 0.2;

    public static void AddOttavas(GPiece p, Random rng)
    {
        foreach (var part in p.Parts)
        {
            if (part.Id == "vo") continue;       // (a song's voice has none)
            bool high = part.Clef is "treble" or "alto";
            var runs = new List<List<GEv>>();
            List<GEv> run = null;
            foreach (var bar in part.Bars)
            {
                if (bar.Count == 1 && !bar[0][0].Measure) { (run ??= new()).AddRange(bar[0]); continue; }
                if (run != null) { runs.Add(run); run = null; }
            }
            if (run != null) runs.Add(run);
            foreach (var line in runs)
                for (int i = 0; i + 2 < line.Count;)
                {
                    if (line[i].IsRest || (i > 0 && line[i - 1].Tie) || rng.NextDouble() >= 0.03) { i++; continue; }
                    // (every value drawn whether the line is placed or not: the draws stay in step)
                    int len = rng.Next(2, 13);
                    bool up = rng.NextDouble() < (high ? 0.85 : 0.15);
                    int octaves = rng.NextDouble() < Ottava15 ? 2 : 1;
                    // the note the line ends before: the first after its stretch (over the rests there)
                    int j = i + len;
                    while (j < line.Count && line[j].IsRest) j++;
                    if (j >= line.Count) break;
                    if (line[j - 1].Tie || line.Skip(i).Take(j - i + 1).Any(e => e.Grace != "")) { i = j + 1; continue; }
                    int by = 7 * octaves * (up ? 1 : -1);
                    for (int k = i; k < j; k++) line[k].Pitches = line[k].Pitches.Select(q => new GPitch(q.Dia + by, q.Alter)).ToList();
                    // (named by the letters printed, closed by the name it was opened with, on the last event
                    // under it, a rest maybe: Lily#'s since 2026-10-28)
                    string name = (octaves == 2 ? "15m" : "8v") + (up ? "a" : "b");
                    line[i].Marks += "@" + name;
                    line[j - 1].Marks += "@!" + name;
                    i = j + 1;
                }
        }
    }

    // OmrGen --graces: grace notes before notes of every suite (S21: the detector's training data
    // for them), from a stream of their own after the music like --marks: an acciaccatura (one
    // slashed eighth, or two slashed sixteenths), an appoggiatura (one eighth or sixteenth, no
    // slash), or two or three sixteenths leading in (grace { }), a step or two from the note, now
    // and then with an accidental of its own (a leading note raised, an upper neighbour lowered).
    // Not before a rest, in a tuplet, after a tie (the tie would run over it), nor where lied's
    // --grace put one. The truth's .notes has no grace note (Grace is text written before the
    // note's): tools/score-marks.ps1 compares the .lys texts bar by bar.
    // (No acciaccatura of two notes: Lily# draws its beam with no slash through it, so the page
    // shows a plain grace group -- docs/repro/acciaccatura-beamed-2026-10-18.lys. The weights' sum
    // is kept, so the draws of the other kinds stay as they were)
    static readonly (string Kind, int Notes, int Value, int Weight)[] GraceKinds =
        { ("acciaccatura", 1, 8, 40), ("appoggiatura", 1, 8, 20), ("appoggiatura", 1, 16, 8), ("grace", 2, 16, 25), ("grace", 3, 16, 7) };

    public static void AddGraces(GPiece p, Random rng)
    {
        int total = GraceKinds.Sum(k => k.Weight);
        foreach (var part in p.Parts)
            foreach (var voice in Enumerable.Range(0, part.Bars.Max(b => b.Count)))
            {
                var line = part.Bars.Where(b => voice < b.Count).SelectMany(b => b[voice]).ToList();
                for (int i = 0; i < line.Count; i++)
                {
                    var e = line[i];
                    // (every value drawn whether used or not: the draws stay in step)
                    double r = rng.NextDouble();
                    int k = rng.Next(total), dir = rng.Next(2) == 0 ? 1 : -1;
                    bool far = rng.NextDouble() < 0.2, chromatic = rng.NextDouble() < 0.25;
                    if (r >= 0.06 || e.IsRest || e.Triplet || e.Grace != "" || (i > 0 && line[i - 1].Tie)) continue;
                    var kind = GraceKinds[0];
                    foreach (var g in GraceKinds) { if (k < g.Weight) { kind = g; break; } k -= g.Weight; }
                    var to = e.Pitches[^1];
                    var notes = new List<string>();
                    for (int n = kind.Notes; n >= 1; n--)
                    {
                        // one note a step (or two) off; two or three stepping in from that side
                        int d = dir * (kind.Notes == 1 && far ? 2 : n);
                        int alter = chromatic && n == 1 ? -dir : 0;
                        notes.Add(new GPitch(to.Dia + d, alter).Lys() + kind.Value);
                    }
                    e.Grace = $"{kind.Kind} {{ {string.Join(" ", notes)} }} ";
                }
            }
    }

    // OmrGen --repeats: the piece's form (S22: the detector's training data for the repeat barlines,
    // the volta endings' brackets and the segno and coda signs): the bars cut into sections, and a
    // form that plays a stretch twice (|: ~B :|), with a first and a second ending ([1. ~C] :| [2. ~D]),
    // or that jumps (segno ... to coda ... D.S. al Coda ... coda). From a stream of its own after the
    // music, so the same seed writes the same notes; a tie or a slur across one of the cuts is undone
    // (Lily# carries no slur over a repeat sign, LYS4023). The truth's .notes is the bars as written,
    // in their written order: the page shows each once.
    public static void AddRepeats(GPiece p, Random rng)
    {
        int bars = p.Parts[0].Bars.Count;
        if (bars < 6) return;
        var secs = new List<(string Name, int From, int To)>();
        var form = new List<string>();
        void Add(string name, int from, int to, string before = "", string after = "")
        {
            if (to <= from) return;
            secs.Add((name, from, to));
            form.Add($"{before}~{name}{after}");
        }
        // (every value drawn whether used or not: the draws stay in step)
        double r = rng.NextDouble();
        int a = rng.Next(0, bars / 3 + 1);                   // the bars before the repeat (none: the piece opens with it)
        int body = rng.Next(2, Math.Min(9, bars - a - 1));   // the bars played twice
        int e1 = rng.Next(1, 3), e2 = rng.Next(1, 3);        // the endings' bars
        int jump = rng.Next(2, 5);                           // the bars between "to coda" and "D.S. al Coda"
        // (the forms the words carry, a third of the pieces: drawn last, so the pieces without them
        // are as they were before the words were read -- S22's second step)
        double w = rng.NextDouble();
        // (the endings' numbers, half of the pieces with endings: [1-2. ~C] :| [3. ~D], printed
        // "1. 2." and "3.", and [1-3. ~C] :| [4. ~D], printed "1.–3." and "4." -- S22's third step.
        // Drawn last, after the words, so the other pieces are as they were)
        double passes = rng.NextDouble();
        if (w < 0.35)
        {
            // the words (read by the OCR, not the detector): "Fine" with "D.C. al Fine"; a segno with
            // "Fine" and "D.S. al Fine"; "To Coda" with "D.C. al Coda" and the coda sign
            if (w < 0.15)
            {
                // ~A fine ~B dc al fine -- to the end, back to the start, out at the Fine
                int f = Math.Clamp(a + body, 2, bars - 2);
                Add("A", 0, f, "", " fine");
                Add("B", f, bars, "", " dc al fine");
            }
            else if (w < 0.25)
            {
                // ~A segno ~B fine ~C ds al fine -- back to the sign, out at the Fine
                a = Math.Max(1, a);
                int f = Math.Clamp(a + body, a + 2, bars - 2);
                Add("A", 0, a);
                Add("B", a, f, "segno ", " fine");
                Add("C", f, bars, "", " ds al fine");
            }
            else
            {
                // ~A to coda ~B dc al coda coda ~C -- back to the start, out at "To Coda" to the coda
                int t = Math.Max(2, a), c = Math.Clamp(t + Math.Max(2, body), t + 2, bars - 1);
                Add("A", 0, t, "", " to coda");
                Add("B", t, c, "", " dc al coda");
                Add("C", c, bars, "coda ");
            }
        }
        else if (r < 0.4)
        {
            // |: ~B :| -- a stretch played twice, with no endings
            Add("A", 0, a);
            Add("B", a, a + body, "|: ", " :|");
            Add("C", a + body, bars);
        }
        else if (r < 0.8)
        {
            // |: ~B [1. ~C] :| [2. ~D] ~E -- with a first and a second ending (or the first played
            // twice or three times: [1-2. ~C] :| [3. ~D], [1-3. ~C] :| [4. ~D])
            int c = a + body, d = Math.Min(c + e1, bars - 1), e = Math.Min(d + e2, bars);
            var (first, second) = passes < 0.35 ? ("1-2", "3") : passes < 0.5 ? ("1-3", "4") : ("1", "2");
            Add("A", 0, a);
            Add("B", a, c, "|: ");
            Add("C", c, d, $"[{first}. ", "] :|");
            Add("D", d, e, $"[{second}. ", "]");
            Add("E", e, bars);
        }
        else
        {
            // ~A segno ~B to coda ~C ds al coda coda ~D -- a jump back to the sign, and out to the coda:
            // the sign at B's start, "To Coda" at B's end, "D.S. al Coda" at C's end, the coda sign at D's start
            a = Math.Max(1, a);
            int b = a + Math.Max(2, Math.Min(body, bars - a - 3)), c = Math.Min(b + jump, bars - 1);
            Add("A", 0, a);
            Add("B", a, b, "segno ", " to coda");
            Add("C", b, c, "", " ds al coda");
            Add("D", c, bars, "coda ");
        }
        p.Sections = secs;
        p.Form = "form { " + string.Join(" ", form) + " }";
        // a tie or a slur that would cross a cut ends before it
        var cuts = secs.Select(x => x.From).Where(f => f > 0).ToHashSet();
        foreach (var part in p.Parts)
            foreach (int v in Enumerable.Range(0, part.Bars.Max(b => b.Count)))
            {
                var line = new List<(int Bar, GEv Ev)>();
                for (int b = 0; b < part.Bars.Count; b++)
                    if (v < part.Bars[b].Count) foreach (var e in part.Bars[b][v]) line.Add((b, e));
                for (int i = 0; i < line.Count; i++)
                {
                    var (b, e) = line[i];
                    if (e.Tie && i + 1 < line.Count && line[i + 1].Bar != b && cuts.Contains(line[i + 1].Bar)) e.Tie = false;
                    if (!e.SlurStart) continue;
                    int j = i + 1;
                    while (j < line.Count && !line[j].Ev.SlurEnd) j++;
                    if (j < line.Count && Enumerable.Range(b + 1, line[j].Bar - b).Any(cuts.Contains)) { e.SlurStart = false; line[j].Ev.SlurEnd = false; }
                }
            }
    }

    // OmrGen --mmrests: stretches of bars where every part rests (an orchestral part's, a song's
    // introduction), written as one multi-bar rest (R1*N) that Lily# draws as church rests up to
    // ten bars and a thick bar past ten, the count over the staff. The bars are put in between the
    // music's, from a stream of their own, so the same seed writes the same notes around them
    static readonly int[] RestLengths = { 2, 2, 3, 3, 4, 4, 5, 6, 7, 8, 9, 10, 11, 12, 15, 20 };

    public static void AddMultiRests(GPiece p, Random rng)
    {
        int bars = p.Parts[0].Bars.Count;
        // one stretch or two, at barlines inside the piece (now and then the first: an introduction).
        int k = rng.NextDouble() < 0.4 ? 2 : 1;
        var at = new SortedSet<int>();
        while (at.Count < k) at.Add(rng.NextDouble() < 0.15 ? 0 : rng.Next(1, bars));
        foreach (int pos in at.Reverse())
        {
            int len = Pick(rng, RestLengths);
            // (rests put in at a change keep the meter before it)
            if (p.ChangeAt >= 0 && pos <= p.ChangeAt) p.ChangeAt += len;
            var pb = p.At(pos);
            foreach (var part in p.Parts)
            {
                for (int i = 0; i < len; i++)
                    part.Bars.Insert(pos, new() { new() { new GEv { Ticks = pb.BarTicks, Measure = true } } });
                if (part.Lyrics != null) part.Lyrics.InsertRange(Math.Min(pos, part.Lyrics.Count), Enumerable.Repeat("", len));
            }
        }
        // (a tie or a slur that ran into the rests ends where the music does, as in a song's voice)
        foreach (var part in p.Parts)
        {
            var line = part.Bars.Where(b => b.Count == 1).SelectMany(b => b[0]).ToList();
            for (int i = 0; i < line.Count; i++)
            {
                if (line[i].Tie && (i + 1 == line.Count || line[i + 1].IsRest)) line[i].Tie = false;
                if (!line[i].SlurStart) continue;
                int j = i + 1;
                while (j < line.Count && !line[j].SlurEnd && !line[j].IsRest) j++;
                if (j == line.Count || line[j].IsRest) line[i].SlurStart = false;
            }
            bool open = false;
            foreach (var e in line)
            {
                if (e.SlurEnd) { e.SlurEnd = open; open = false; }
                if (e.SlurStart) open = true;
            }
        }
    }

    // OmrGen --lyrics: words under a song's voice (off by default while Lily# draws a hidden
    // staff's rests on the staff below, far worse with a lyrics row under it, which it also sets
    // under the wrong staff: docs/repro/lilysharp-feedback-2026-10-04.md #19)
    public static bool Lyrics;

    // a song's words, split into syllables ("-" ends a syllable a word goes on from)
    static readonly string[] LiedWords =
    {
        "Im", "wun- der- schö- nen", "Mo- nat", "Mai", "als", "al- le", "Knos- pen", "spran- gen", "da", "ist",
        "in", "mei- nem", "Her- zen", "die", "Lie- be", "auf- ge- gan- gen", "aus", "Trä- nen", "sprie- ßen",
        "viel", "blü- hen- de", "Blu- men", "her- vor", "und", "mei- ne", "Seuf- zer", "wer- den", "ein", "Nach- ti- gal- len- chor",
        "Wenn", "ich", "in", "dei- ne", "Au- gen", "seh'", "so", "schwin- det", "all", "mein", "Leid", "und", "Weh;",
        "doch", "wenn", "du", "sprichst:", "ich", "lie- be", "dich!", "so", "muß", "ich", "wei- nen", "bit- ter- lich.",
    };

    /// <summary>
    /// The words under a song's voice: a syllable a note, none for the notes a slur or a tie holds
    /// the syllable over (Lily#'s melisma, LilyPond's), a bar with no notes left empty.
    /// </summary>
    static List<string> Words(GPart vo, Random rng)
    {
        var syllables = new Queue<string>();
        var bars = new List<string>();
        bool held = false;   // the note before was tied into this one, or a slur goes on over it
        bool inSlur = false;
        foreach (var bar in vo.Bars)
        {
            var sb = new List<string>();
            foreach (var e in bar[0])
            {
                if (e.IsRest) { held = false; continue; }
                if (!held)
                {
                    if (syllables.Count == 0) foreach (var s in Pick(rng, LiedWords).Split(' ')) syllables.Enqueue(s);
                    sb.Add(syllables.Dequeue());
                }
                if (e.SlurEnd) inSlur = false;
                if (e.SlurStart) inSlur = true;
                held = e.Tie || inSlur;
            }
            bars.Add(string.Join(" ", sb));
        }
        return bars;
    }

    internal static T Pick<T>(Random rng, T[] xs) => xs[rng.Next(xs.Length)];

    internal static GPart Melodic(GPiece p, Random rng, int bars, string id, string clef, string label,
        int width, double voices, double chords, double triplets = 0.02, double leaps = 0.25, double ties = 0, double slurs = 0, bool full = false)
    {
        var part = new GPart { Id = id, Clef = clef, Name = label };
        int mid = MiddleLine(clef);
        int cur = mid, upper = mid + 3, lower = mid - 3;
        bool twoVoices = voices > 0 && rng.NextDouble() < 0.5;
        for (int b = 0; b < bars; b++)
        {
            // a stretch of two voices lasts a few bars
            if (voices > 0 && b % 4 == 0) twoVoices = rng.NextDouble() < voices;
            var bar = new List<List<GEv>>();
            // (the bar's own key and meter: a song may change them)
            var pb = p.At(b);
            var keyAlt = KeyAlterations(pb.Fifths);
            if (twoVoices)
            {
                bar.Add(Line(pb, rng, keyAlt, ref upper, mid, mid + width, chords * 0.5, triplets, leaps, restP: 0.1, full));
                bar.Add(Line(pb, rng, keyAlt, ref lower, mid - width, mid - 1, chords * 0.5, triplets, leaps, restP: 0.1, full));
            }
            else bar.Add(Line(pb, rng, keyAlt, ref cur, mid - width, mid + width, chords, triplets, leaps, restP: 0.12, full));
            part.Bars.Add(bar);
        }
        // ties: a note (or chord) held into the next one, within the bar or across the barline;
        // the next note takes the tied one's pitches
        if (ties > 0)
        {
            var line = part.Bars.Where(b => b.Count == 1).SelectMany(b => b[0]).ToList();
            for (int i = 0; i + 1 < line.Count; i++)
            {
                GEv e = line[i], f = line[i + 1];
                if (e.IsRest || f.IsRest || e.Triplet || f.Triplet || e.Tie) continue;
                if (rng.NextDouble() >= ties) continue;
                f.Pitches = new List<GPitch>(e.Pitches);
                e.Tie = true;
            }
        }
        // slurs: over two to six notes in a row (no rest under one), across barlines where the
        // bars have one voice, within the bar in each voice of a two-voice bar; one ends before
        // the next starts
        if (slurs > 0)
        {
            var lines = new List<List<GEv>>();
            List<GEv> run = null;
            foreach (var bar in part.Bars)
            {
                if (bar.Count == 1) { (run ??= new()).AddRange(bar[0]); continue; }
                if (run != null) { lines.Add(run); run = null; }
                lines.AddRange(bar);
            }
            if (run != null) lines.Add(run);
            foreach (var line in lines)
                for (int i = 0; i + 1 < line.Count;)
                {
                    if (line[i].IsRest || rng.NextDouble() >= slurs) { i++; continue; }
                    int len = rng.Next(2, 7), n = 0;
                    while (n < len && i + n < line.Count && !line[i + n].IsRest) n++;
                    if (n < 2) { i++; continue; }
                    line[i].SlurStart = true;
                    line[i + n - 1].SlurEnd = true;
                    i += n;
                }
        }
        return part;
    }

    internal static int[] KeyAlterations(int fifths)
    {
        int[] sharpOrder = { 3, 0, 4, 1, 5, 2, 6 }, flatOrder = { 6, 2, 5, 1, 4, 0, 3 };
        var a = new int[7];
        for (int i = 0; i < Math.Abs(fifths); i++) a[fifths > 0 ? sharpOrder[i] : flatOrder[i]] = fifths > 0 ? 1 : -1;
        return a;
    }

    /// <summary>One voice's bar: a rhythm that fills it, and a walk through the scale.</summary>
    internal static List<GEv> Line(GPiece p, Random rng, int[] keyAlt, ref int cur, int lo, int hi,
        double chords, double triplets, double leaps, double restP, bool full = false)
    {
        var evs = new List<GEv>();
        var rhythm = Rhythm(p, rng, triplets);
        var figure = Figures(rhythm);
        for (int i = 0; i < rhythm.Count; i++)
        {
            var (ticks, tn, td) = rhythm[i];
            var e = new GEv { Ticks = ticks, TupletN = tn, TupletD = td };
            // (the rest drawn whether used or not: the draws stay in step)
            bool rest = tn == 0 && rng.NextDouble() < restP;
            if (figure[i] != 0 ? figure[i] < 0 : rest) { evs.Add(e); continue; }
            // the walk: mostly steps, sometimes a leap, turning back at the range's edges
            double r = rng.NextDouble();
            int move = r < 0.1 ? 0 : r < 0.1 + (1 - leaps) * 0.9 ? 1 : rng.Next(2, 6);
            int dir = rng.Next(2) == 0 ? -1 : 1;
            int next = cur + dir * move;
            if (next < lo || next > hi) next = cur - dir * move;
            cur = Math.Clamp(next, lo, hi);
            int alt = keyAlt[((cur % 7) + 7) % 7];
            if (rng.NextDouble() < 0.07) alt = Math.Clamp(alt + (rng.Next(2) == 0 ? -1 : 1), -2, 2);
            e.Pitches.Add(new GPitch(cur, alt));
            if (rng.NextDouble() < chords)
            {
                // a third, a third and fifth, or a second above (the tight case for heads)
                double c = rng.NextDouble();
                int[] adds = full
                    // (a piano's: octaves, a third or sixth inside one, four notes)
                    ? (c < 0.25 ? new[] { 7 } : c < 0.4 ? new[] { 2, 7 } : c < 0.55 ? new[] { 4, 7 } : c < 0.7 ? new[] { 2, 4, 7 } : c < 0.85 ? new[] { 2, 4 } : new[] { 2, 5 })
                    : c < 0.45 ? new[] { 2 } : c < 0.8 ? new[] { 2, 4 } : new[] { 1 };
                foreach (int add in adds)
                {
                    int d = cur + add;
                    if (d > hi + 2) continue;
                    e.Pitches.Add(new GPitch(d, keyAlt[((d % 7) + 7) % 7]));
                }
            }
            evs.Add(e);
        }
        return evs;
    }

    // OmrGen --figures: the stream that picks which runs open on a rest (null: none)
    public static Random FigureRng;

    /// <summary>OmrGen --figures: an accompaniment's figure, a rest and three notes of one value:
    /// a run of four plain sixteenths (or eighths) from its own beat (a quarter's, a half's), now
    /// and then opened by a rest, the three after it notes (three of one value under one beam after
    /// a rest, the rest twice as long if misread: a triplet's bar, the test of
    /// Reader.UnprintedTriplets). Per value: -1 a rest, +1 a note, 0 as drawn.</summary>
    static int[] Figures(List<(int Ticks, int N, int D)> rhythm)
    {
        var f = new int[rhythm.Count];
        if (FigureRng == null) return f;
        for (int i = 0, at = 0; i < rhythm.Count; at += rhythm[i].Ticks, i++)
        {
            int t = rhythm[i].Ticks;
            if (rhythm[i].N != 0 || t is not (120 or 240) || at % (4 * t) != 0 || i + 4 > rhythm.Count) continue;
            if (Enumerable.Range(i, 4).Any(k => rhythm[k].Ticks != t || rhythm[k].N != 0)) continue;
            if (FigureRng.NextDouble() >= 0.6) continue;
            f[i] = -1;
            f[i + 1] = f[i + 2] = f[i + 3] = 1;
        }
        return f;
    }

    // OmrGen --tuplets: the stream that picks a tuplet group's kind (null: triplets only)
    public static Random TupletRng;

    /// <summary>Values that fill one bar, beat by beat, in patterns natural to the meter. A tuplet's
    /// values carry the group's ratio N:D (0:0 for none).</summary>
    internal static List<(int Ticks, int N, int D)> Rhythm(GPiece p, Random rng, double triplets)
    {
        var num = int.Parse(p.Time.Split('/')[0]);
        var den = int.Parse(p.Time.Split('/')[1]);
        bool compound = den == 8 && num % 3 == 0;
        int beat = compound ? 720 : 1920 / den;
        int beats = p.BarTicks / beat;
        var outp = new List<(int, int, int)>();
        // now and then one note fills the bar (when a single value can)
        if (rng.NextDouble() < 0.07 && Representable(p.BarTicks)) { outp.Add((p.BarTicks, 0, 0)); return outp; }
        int[][] simple = den == 2
            ? new[] { new[] { 960 }, new[] { 480, 480 }, new[] { 720, 240 }, new[] { 480, 240, 240 }, new[] { 240, 240, 240, 240 } }
            : den == 8
            ? new[] { new[] { 240 }, new[] { 120, 120 } }
            : new[] { new[] { 480 }, new[] { 240, 240 }, new[] { 240, 120, 120 }, new[] { 120, 120, 240 }, new[] { 120, 120, 120, 120 }, new[] { 360, 120 }, new[] { 480 } };
        int[][] comp = { new[] { 720 }, new[] { 240, 240, 240 }, new[] { 480, 240 }, new[] { 360, 120, 240 }, new[] { 240, 120, 120, 240 }, new[] { 120, 120, 120, 120, 240 } };
        int b = 0;
        while (b < beats)
        {
            if (!compound && den == 4 && b + 1 < beats && rng.NextDouble() < 0.2)
            {
                var two = rng.NextDouble() < 0.6 ? new[] { 960 } : new[] { 720, 240 };
                foreach (var t in two) outp.Add((t, 0, 0));
                b += 2;
                continue;
            }
            if (!compound && den == 4 && rng.NextDouble() < triplets)
            {
                // three eighths in a quarter; with --tuplets, half the groups are five, six or seven
                // sixteenths in it instead (the kind from a stream of its own: the music stays the same)
                var (n, dd, t) = (3, 2, 240);
                if (TupletRng != null)
                {
                    double k = TupletRng.NextDouble();
                    if (k >= 0.5) (n, dd, t) = k < 0.7 ? (5, 4, 120) : k < 0.85 ? (6, 4, 120) : (7, 4, 120);
                }
                for (int k = 0; k < n; k++) outp.Add((t, n, dd));
                b++;
                continue;
            }
            var pat = compound ? comp[rng.Next(comp.Length)] : simple[rng.Next(simple.Length)];
            foreach (var t in pat) outp.Add((t, 0, 0));
            b++;
        }
        return outp;
    }

    static bool Representable(int ticks)
    {
        foreach (int b in new[] { 1, 2, 4, 8, 16 })
            for (int d = 0; d <= 1; d++)
                if ((d == 0 ? 1920 / b : 1920 / b * 3 / 2) == ticks) return true;
        return false;
    }
}
