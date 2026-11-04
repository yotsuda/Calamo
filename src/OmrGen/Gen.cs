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
            case "threes":
                Threes(p, rng, bars);
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

    /// <summary>
    /// The "threes" suite: music that runs in triplets from end to end, an accompaniment's broken
    /// chords three to a beat, as an engraver prints the number over the first few or nowhere
    /// (Beethoven's Op.27-2, Schumann's Op.15-1; read with run-gen -NoNumbers, S55). On a grand
    /// staff, two pieces in three: the triplets under a melody in the right hand's second voice,
    /// a bass below (some bars the triplets alone), or in the left hand under the right hand's
    /// melody; else one staff, the triplets with a melody over them now and then. Eighths three
    /// to a quarter, or sixteenths six to it, or in 2/2 quarters three to a half (printed with a
    /// bracket, which -NoNumbers takes off too); in some pieces each group opens on a rest. The
    /// shapes that cross the reader's measure: in the right hand's lower voice, groups that reach
    /// down to the ledger lines near the left hand's staff (Op.27-2's bar 5), and a melody note
    /// that shares its head with the group's first; in the left hand, a bass a beat under the
    /// triplets on their staff (Op.15-1); two against three, the triplets in one hand
    /// against plain notes of the same value in the other. Not one of <see cref="Suites"/> (asked
    /// for by name).
    /// </summary>
    static void Threes(GPiece p, Random rng, int bars)
    {
        p.Fifths = rng.Next(-5, 6);
        p.Time = Pick(rng, ThreesTimes);
        // (--cross: a beam to join the two staves, so no 2/2's quarters; a group's first note is its hand's)
        // (--knee: 2/2's six eighths to a half under one beam are Op.27-2's own, below)
        if (CrossStaff && !KneeBeams) while (p.Time == "2/2") p.Time = Pick(rng, ThreesTimes);
        // (every draw of the piece's shape made whether used or not: the draws stay in step)
        double shape = rng.NextDouble(), unit = rng.NextDouble(), rests = rng.NextDouble(), turn = rng.NextDouble();
        double low = rng.NextDouble(), share = rng.NextDouble(), plainUp = rng.NextDouble();
        // 0: the right hand's lower voice, 1: the left hand, 2: one staff, 3: two against three
        int mode = CrossStaff ? 1 : FlipLine ? 0 : shape < 0.35 ? 0 : shape < 0.6 ? 1 : shape < 0.85 ? 2 : 3;
        // (--knee: eighths, as 2/2's six to a half under one beam are Op.27-2's own)
        int value = KneeBeams || FlipLine ? 240 : p.Time == "2/2" ? 480 : unit < 0.7 ? 240 : 120;
        bool restOpens = rests < 0.2 && !CrossStaff && !FlipLine;
        // the order of a group's three chord tones: up (Op.27-2), up and back, or the middle first
        int[] order = turn < 0.6 ? new[] { 0, 1, 2 } : turn < 0.8 || CrossStaff ? new[] { 0, 2, 1 } : new[] { 1, 0, 2 };
        // (--cross: how many of a group's notes climb to the right hand's staff, and whether a number
        // stands over the first bar's groups or none at all; the groups left whole now and then)
        int climb = CrossStaff && rng.NextDouble() < 0.3 ? 1 : 2;
        bool numbered = CrossStaff && rng.NextDouble() < 0.5;
        var keyAlt = KeyAlterations(p.Fifths);
        GPitch At(int dia) => new(dia, keyAlt[((dia % 7) + 7) % 7]);
        int beats = int.Parse(p.Time.Split('/')[0]), beat = 1920 / int.Parse(p.Time.Split('/')[1]);
        // the harmony, a chord (its root's step in the key) per half bar
        int[] degrees = { 0, 3, 4, 5, 1, 4 };
        var chords = new int[bars * 2];
        for (int i = 0; i < chords.Length; i++) chords[i] = i == 0 ? 0 : rng.NextDouble() < 0.5 ? chords[i - 1] : Pick(rng, degrees);
        int tonic = (4 * p.Fifths % 7 + 7) % 7;   // the key's tonic step (C 0, G 4, F 3, ...)
        // a chord's three tones from the lowest at or above dia, one a group of the triplets
        List<GPitch> Tones(int half, int from)
        {
            int root = (tonic + chords[half]) % 7, d = from;
            while (((d - root) % 7 + 7) % 7 % 2 == 1 || ((d - root) % 7 + 7) % 7 == 6) d++;
            var t = new List<int> { d };
            while (t.Count < 3) { d++; int r = ((d - root) % 7 + 7) % 7; if (r is 0 or 2 or 4) t.Add(d); }
            return order.Select(k => At(t[k])).ToList();
        }
        // a bar of the triplets from dia on: a group (or two of sixteenths) a beat
        List<GEv> Triplets(int b, int from)
        {
            var evs = new List<GEv>();
            for (int k = 0; k < beats; k++)
            {
                var tones = Tones(2 * b + (2 * k >= beats ? 1 : 0), from);
                // (a group sounds two of its value)
                for (int g = 0; g < beat / (2 * value); g++)
                    for (int i = 0; i < 3; i++)
                    {
                        var e = new GEv { Ticks = value, TupletN = 3, TupletD = 2 };
                        if (!(restOpens && i == 0)) e.Pitches.Add(tones[i]);
                        evs.Add(e);
                    }
            }
            return evs;
        }
        // a bar of plain notes of the triplets' value from dia on (two against three): the half
        // bar's chord broken up and down, beat / value a beat
        List<GEv> Plain(int b, int from)
        {
            var evs = new List<GEv>();
            for (int k = 0; k < beats; k++)
            {
                var tones = Tones(2 * b + (2 * k >= beats ? 1 : 0), from);
                for (int i = 0; i < beat / value; i++)
                {
                    var e = new GEv { Ticks = value };
                    e.Pitches.Add(tones[new[] { 0, 1, 2, 1 }[i % 4]]);
                    evs.Add(e);
                }
            }
            return evs;
        }
        // a melody note on a beat now and then takes the head of the triplets' group under it:
        // the group turned to open on its top tone, the melody's note that tone
        void Share(List<GEv> melody, List<GEv> tri)
        {
            int t = 0;
            foreach (var e in melody)
            {
                if (!e.IsRest && t % beat == 0 && rng.NextDouble() < 0.5)
                {
                    int g = t / beat * 3 * (beat / (2 * value));
                    var group = tri.GetRange(g, 3);
                    if (group.All(n => !n.IsRest))
                    {
                        var top = group.MaxBy(n => n.Pitches[0].Dia);
                        (group[0].Pitches, top.Pitches) = (top.Pitches, group[0].Pitches);
                        e.Pitches = new() { group[0].Pitches[0] };
                    }
                }
                t += e.Ticks;
            }
        }
        // a melody's bar: half notes, dotted halves, quarters, a quarter's rest, two eighths, a dotted
        // eighth and a sixteenth (Op.27-2's against the triplets' third), a chord tone or a step from the last
        int cur = 37;
        List<GEv> Melody(int b, int lo, int hi)
        {
            var evs = new List<GEv>();
            for (int k = 0; k < beats;)
            {
                double r = rng.NextDouble();
                int[] vals = r < 0.25 && k + 2 <= beats ? new[] { 2 * beat } : r < 0.35 && k + 3 <= beats ? new[] { 3 * beat }
                    : r < 0.6 ? new[] { beat } : r < 0.7 ? new[] { -beat }
                    // (--cross: no beams in the melody, its stems down would lay them over the climbing groups' beams,
                    // one voice on the staff as this suite writes it, which no engraver prints)
                    : CrossStaff ? new[] { beat }
                    : r < 0.85 ? new[] { beat / 2, beat / 2 } : new[] { beat * 3 / 4, beat / 4 };
                foreach (int v in vals)
                {
                    var e = new GEv { Ticks = Math.Abs(v) };
                    if (v > 0)
                    {
                        cur = Math.Clamp(cur + rng.Next(-2, 3), lo, hi);
                        e.Pitches.Add(At(cur));
                    }
                    evs.Add(e);
                }
                k += vals.Sum(Math.Abs) / beat;
            }
            return evs;
        }
        // a bass's bar: the half bar's root, an octave below it now and then, a note a half bar (or the bar)
        List<GEv> Bass(int b)
        {
            var evs = new List<GEv>();
            bool whole = chords[2 * b] == chords[2 * b + 1] && Representable(p.BarTicks);
            for (int h = 0; h < (whole ? 1 : 2); h++)
            {
                int root = (tonic + chords[2 * b + h]) % 7, d = 14 + root;
                var e = new GEv { Ticks = whole ? p.BarTicks : p.BarTicks / 2 };
                e.Pitches.Add(At(d));
                if (rng.NextDouble() < 0.5) e.Pitches.Add(At(d + 7));
                evs.Add(e);
            }
            return evs;
        }
        // a bass a beat under the triplets on their staff: the half bar's root, or a step from the last
        int low1 = 14;
        List<GEv> BassBeats(int b)
        {
            var evs = new List<GEv>();
            for (int k = 0; k < beats; k++)
            {
                int root = 14 + (tonic + chords[2 * b + (2 * k >= beats ? 1 : 0)]) % 7;
                low1 = k == 0 || rng.NextDouble() < 0.5 ? root : Math.Clamp(low1 + rng.Next(-1, 2), 12, 20);
                var e = new GEv { Ticks = beat };
                e.Pitches.Add(At(low1));
                evs.Add(e);
            }
            return evs;
        }
        if (mode == 2)
        {
            string clef = rng.NextDouble() < 0.7 ? "treble" : "bass";
            var part = new GPart { Id = "p1", Clef = clef, Name = "Staff" };
            int from = clef == "treble" ? 30 : 17;
            cur = from + 8;
            for (int b = 0; b < bars; b++)
            {
                var tri = Triplets(b, from);
                part.Bars.Add(rng.NextDouble() < 0.4 ? new() { Melody(b, from + 7, from + 12), tri } : new() { tri });
            }
            p.Parts.Add(part);
            return;
        }
        p.Grand = true;
        var rh = new GPart { Id = "rh", Clef = "treble", Name = "Right" };
        var lh = new GPart { Id = "lh", Clef = "bass", Name = "Left" };
        for (int b = 0; b < bars; b++)
        {
            if (mode == 0)
            {
                // (Op.27-2's first bars: the triplets alone; in some pieces half the bars' groups
                // from the G below middle C, on the ledger lines over the left hand's staff)
                var tri = Triplets(b, low < 0.4 && rng.NextDouble() < 0.5 ? 25 : 29);
                if (FlipLine && tri.Count >= 6 && rng.NextDouble() < 0.7)
                {
                    int n = tri.Count / 3, span = 2 * value;
                    GEv Rest() => new() { Ticks = span, Space = true };
                    List<GEv> Group(int g) => tri.GetRange(3 * g, 3);
                    if (rng.NextDouble() < 0.3)
                    {
                        // (Op.27-2's bar 12: the right hand starts the line, the left hand ends it over its own bass, every stem up)
                        int k = rng.Next(1, n);
                        var on = Triplets(b, 17);
                        var upper = new List<GEv>();
                        var lower = new List<GEv>();
                        for (int g = 0; g < n; g++)
                        {
                            upper.AddRange(g < k ? Group(g) : new() { Rest() });
                            lower.AddRange(g < k ? new() { Rest() } : on.GetRange(3 * g, 3));
                        }
                        rh.Bars.Add(new() { upper });
                        var bass = Bass(b);
                        foreach (var e in bass) e.Pitches = e.Pitches.Select(q => new GPitch(q.Dia - 7, q.Alter)).ToList();
                        lh.Bars.Add(new() { lower, bass });
                        continue;
                    }
                    // (Op.27-2's bars 37, 40 and 42: one line, its groups' stems up or down as the notes lie, which two voices
                    // on the staff write as here, each resting while the other has the line)
                    bool[] up;
                    do { up = Enumerable.Range(0, n).Select(_ => rng.NextDouble() < 0.5).ToArray(); } while (up.All(u => u) || up.All(u => !u));
                    var hi = new List<GEv>[n];
                    var lo = new List<GEv>[n];
                    // (the clef going down for a group on the lower voice and back after it: bars 37 and 42)
                    int clefAt = n >= 3 && rng.NextDouble() < 0.4 ? rng.Next(1, n - 1) : -1;
                    if (clefAt >= 0)
                    {
                        up[clefAt] = false;
                        if (up.All(u => !u)) up[0] = true;
                        var drop = Group(clefAt);
                        foreach (var e in drop) e.Pitches = e.Pitches.Select(q => new GPitch(q.Dia - 7, q.Alter)).ToList();
                        drop[0].Grace = "clef bass ";
                        tri[3 * (clefAt + 1)].Grace = "clef treble ";
                    }
                    for (int g = 0; g < n; g++)
                    {
                        hi[g] = up[g] ? Group(g) : new() { Rest() };
                        lo[g] = up[g] ? new() { Rest() } : Group(g);
                    }
                    // a note on the line's head stems the other way (bars 37, 40), or a melody over a group with its stems down (bar 42)
                    int at = rng.Next(n);
                    if (at != clefAt && at != clefAt + 1 || clefAt < 0)
                    {
                        if (up[at]) lo[at] = new() { new GEv { Ticks = span, Pitches = new() { tri[3 * at].Pitches[0] } } };
                        else if (span % 4 == 0)
                        {
                            cur = Math.Clamp(cur + rng.Next(-2, 3), 36, 41);
                            hi[at] = new() { new GEv { Ticks = span * 3 / 4, Pitches = new() { At(cur) } }, new GEv { Ticks = span / 4, Pitches = new() { At(cur) } } };
                        }
                    }
                    rh.Bars.Add(new() { hi.SelectMany(x => x).ToList(), lo.SelectMany(x => x).ToList() });
                    lh.Bars.Add(new() { Bass(b) });
                    continue;
                }
                if (b < 2 || rng.NextDouble() < 0.2) rh.Bars.Add(new() { tri });
                else
                {
                    var melody = Melody(b, 36, 41);
                    if (share < 0.4) Share(melody, tri);
                    rh.Bars.Add(new() { melody, tri });
                }
                lh.Bars.Add(new() { Bass(b) });
            }
            else if (mode == 3)
            {
                bool up = plainUp < 0.5;
                rh.Bars.Add(new() { up ? Plain(b, 31) : Triplets(b, 31) });
                lh.Bars.Add(new() { up ? Triplets(b, 17) : Plain(b, 17) });
            }
            else
            {
                // (--knee: the bar's rest, or a whole note high over the heads that climb: a stem down from a
                // melody over them would run through the group, which no engraver prints. Op.27-2 writes its melody
                // there stems up, a second voice on the staff, which @cross notes from another part do not make)
                // (--handover: Op.27-2's own, the melody forced stems up, over the climbing heads and clear of the group)
                if (HandOver)
                {
                    var melody = Melody(b, 36, 42);
                    foreach (var e in melody) if (!e.IsRest) e.Marks += "@stemUp";
                    rh.Bars.Add(new() { melody });
                }
                else if (KneeBeams)
                {
                    var bar = new GEv { Ticks = p.BarTicks };
                    if (Representable(p.BarTicks) && rng.NextDouble() < 0.3) bar.Pitches.Add(At(38 + rng.Next(5)));
                    rh.Bars.Add(new() { new() { bar } });
                }
                else rh.Bars.Add(new() { Melody(b, 33, 40) });
                // (Op.15-1's left hand in some pieces: the triplets over a bass a beat on the same staff)
                var left = Triplets(b, low < 0.5 || CrossStaff ? 21 : 17);
                if (CrossStaff)
                    for (int g = 0; g + 3 <= left.Count; g += 3)
                    {
                        bool whole = rng.NextDouble() < 0.15;
                        for (int i = 0; i < 3; i++)
                        {
                            left[g + i].Bare = !(numbered && b == 0);
                            if (whole || i < 3 - climb) continue;
                            left[g + i].Marks += "@cross";
                            // (--knee: heads over the beam, in the staff: their stems come down to it as the others'
                            // go up, Op.27-2's bars 23 and 30. F4 is as low as lysc and LilyPond draw it so here)
                            if (KneeBeams)
                            {
                                int dia = left[g + i].Pitches[0].Dia;
                                while (dia < 31) dia += 7;
                                left[g + i].Pitches[0] = At(dia);
                            }
                        }
                    }
                // (--handover: now and then the left hand's groups stop after a beat or more, and the right hand's
                // lower voice takes the triplets on under a melody that enters with it: Op.27-2's bars 23 and 30)
                if (HandOver && beats > 1 && rng.NextDouble() < 0.5)
                {
                    int k = rng.Next(1, beats);
                    left = left.Take(k * 3 * (beat / (2 * value))).ToList();
                    left.Add(new GEv { Ticks = (beats - k) * beat, Space = true });
                    var on = Triplets(b, 29).Skip(k * 3 * (beat / (2 * value))).ToList();
                    foreach (var e in on) e.Bare = true;
                    on.Insert(0, new GEv { Ticks = k * beat, Space = true });
                    var over = new List<GEv> { new() { Ticks = k * beat, Space = true } };
                    for (int j = k; j < beats; j++)
                    {
                        cur = Math.Clamp(cur + rng.Next(-2, 3), 36, 42);
                        var e = new GEv { Ticks = beat, Marks = "@stemUp" };
                        e.Pitches.Add(At(cur));
                        over.Add(e);
                    }
                    rh.Bars[^1] = new() { over, on };
                }
                // (--knee: one voice, a second forces the stems its way and no beam knees, in LilyPond as in lysc)
                lh.Bars.Add(low < 0.5 && !KneeBeams ? new() { left, BassBeats(b) } : new() { left });
            }
        }
        p.Parts.Add(rh);
        p.Parts.Add(lh);
    }

    static readonly string[] ThreesTimes = { "4/4", "4/4", "3/4", "2/4", "2/2" };

    // OmrGen --cross: threes' left hand climbing to the right hand's staff in mid group
    public static bool CrossStaff;
    // OmrGen --knee: the same with the climbing heads over the beam, a kneed beam between the staves
    public static bool KneeBeams;
    // OmrGen --handover: the same with a melody stems up, and bars the left hand starts and the right hand ends
    public static bool HandOver;
    // OmrGen --flip: the right hand's line of triplets with its stems turning in mid bar (Op.27-2's bars 37, 40 and 42)
    public static bool FlipLine;

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

    // OmrGen --leapslurs: two-note slurs over a wide leap (a fourth to an octave, up or down) from an
    // eighth or sixteenth, in every part and voice, from a stream of their own after the music like
    // --glissandi. Lily# draws such a slur as a short hook from the first head's tip down (or up) to
    // the far head, the shape old prints give most two-note slurs and the detector missed on the
    // scans (2026-10-30, HANDOFF). The second note is moved to the leap; within a bar, out of any slur
    // or tie, never a voice that sings words (its syllables follow the slurs)
    public static void AddLeapSlurs(GPiece p, Random rng)
    {
        foreach (var part in p.Parts)
        {
            if (part.Lyrics != null) continue;
            int mid = MiddleLine(part.Clef);
            for (int b = 0; b < part.Bars.Count; b++)
            {
                var keyAlt = KeyAlterations(p.At(b).Fifths);
                foreach (var line in part.Bars[b])
                {
                    bool inSlur = false;
                    for (int i = 0; i + 1 < line.Count; i++)
                    {
                        // (every value drawn whether used or not: the draws stay in step)
                        double r = rng.NextDouble();
                        int leap = rng.Next(3, 8) * (rng.Next(2) == 0 ? -1 : 1);
                        GEv e = line[i], n = line[i + 1];
                        if (e.SlurEnd) inSlur = false;
                        if (e.SlurStart) inSlur = true;
                        if (r >= 0.3 || inSlur || n.SlurStart || n.SlurEnd || e.IsRest || n.IsRest || e.Measure || n.Measure
                            || e.Pitches.Count != 1 || n.Pitches.Count != 1 || e.Tie || n.Tie || (i > 0 && line[i - 1].Tie)
                            || e.Ticks > 240 || n.Ticks > 480) continue;
                        int d = e.Pitches[0].Dia + leap;
                        if (Math.Abs(d - mid) > 9) d = e.Pitches[0].Dia - leap;
                        n.Pitches = new() { new GPitch(d, keyAlt[((d % 7) + 7) % 7]) };
                        e.SlurStart = n.SlurEnd = true;
                        i++;
                    }
                }
            }
        }
    }

    // OmrGen --cues: cue notes (another instrument's line printed small in a part's rest, the
    // detector's training data for small heads, rests and dots: the Eroica trumpet part, 2026-10-30
    // HANDOFF), from a stream of their own after the music like --leapslurs. A bar of a part on a
    // staff of its own becomes the cue's line over the part's full-measure rest, voice { cue { ... } }
    // { R }, as a Sibelius part prints it; a cue runs on over the next bars now and then. The cue's
    // line is the bar's own music a step or more away (its top note where it had a chord). Not a bar
    // a tie or a slur crosses into or out of, nor one with marks or grace notes (they would hang on
    // the cue). The truth's first voice is the cue's line, its second the rest, as the reader writes it
    // (--cue-whole: the share of the cue bars held on one note the bar long, a whole or a dotted half;
    // drawn only when asked for, so the pages without it stay as they were)
    public static double CueWhole = 0;

    public static void AddCues(GPiece p, Random rng)
    {
        foreach (var part in p.Parts)
        {
            if (p.Grand || part.InGrand || part.Lyrics != null) continue;
            bool slur = false, last = false;
            for (int b = 0; b < part.Bars.Count; b++)
            {
                // (every value drawn whether used or not: the draws stay in step)
                double r = rng.NextDouble();
                int shift = rng.Next(1, 5) * (rng.Next(2) == 0 ? -1 : 1);
                bool held = CueWhole > 0 && rng.NextDouble() < CueWhole;
                var voices = part.Bars[b];
                bool tiedIn = b > 0 && part.Bars[b - 1].Any(v => v.Count > 0 && v[^1].Tie);
                bool open = slur;
                foreach (var e in voices[0]) { if (e.SlurEnd) slur = false; if (e.SlurStart) slur = true; }
                // (and a bar whose rest is one written value: R1, R2., not 5/4's or 9/8's)
                last = r < (last ? 0.6 : 0.15) && voices.Count == 1 && !open && !slur && !tiedIn && Writer.TryDur(p.At(b).BarTicks) != null
                    && voices[0].All(e => !e.Measure && !e.Tie && !e.SlurStart && !e.SlurEnd && e.Marks == "" && e.Grace == "");
                if (!last) continue;
                var keyAlt = KeyAlterations(p.At(b).Fifths);
                int mid = MiddleLine(part.Clef);
                var cue = voices[0].Select(e =>
                {
                    var c = e.Copy();
                    c.Cue = true;
                    if (!e.IsRest)
                    {
                        var top = e.Pitches[^1];
                        // (from the staff's second line up to four ledger lines over it: lower, the line
                        // would run over the part's rest, which hangs under the staff, and Lily# draws them on each other)
                        int d = top.Dia + shift;
                        while (d < mid - 2) d += 7;
                        while (d > mid + 12) d -= 7;
                        c.Pitches = new() { new GPitch(d, keyAlt[((d % 7) + 7) % 7]) };
                    }
                    return c;
                }).ToList();
                // (held: the bar's first note, the bar long)
                if (held && cue.FirstOrDefault(e => !e.IsRest) is { } first)
                    cue = new() { new GEv { Ticks = p.At(b).BarTicks, Pitches = first.Pitches, Cue = true } };
                part.Bars[b] = new() { cue, new() { new GEv { Ticks = p.At(b).BarTicks, Measure = true } } };
            }
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
        int mixAt = 0;
        bool numbered = false, bare = false;
        for (int i = 0; i < rhythm.Count; i++)
        {
            var (ticks, tn, td) = rhythm[i];
            var e = new GEv { Ticks = ticks, TupletN = tn, TupletD = td };
            if (MixRng != null && tn == 3)
            {
                // (a group with no number only after a numbered one in the bar: kinkel19-1's bar 8)
                if (mixAt % 720 == 0) { bare = numbered && MixRng.NextDouble() < 0.5; numbered |= !bare; }
                e.Bare = bare;
                mixAt += ticks;
            }
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
    // OmrGen --mixthrees: the stream that picks triplets of unlike values, and which groups after a numbered one go bare
    public static Random MixRng;

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
            if (MixRng != null && den == 4 && !compound && MixRng.NextDouble() < 0.2)
            {
                int[][] mixed = { new[] { 240, 480 }, new[] { 480, 240 }, new[] { 360, 120, 240 }, new[] { 240, 360, 120 }, new[] { 120, 120, 240, 240 }, new[] { 240, 240, 240 } };
                foreach (var t in mixed[MixRng.Next(mixed.Length)]) outp.Add((t, 3, 2));
                b++;
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
