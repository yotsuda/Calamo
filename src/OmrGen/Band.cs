using System.Globalization;
using System.Text;

namespace OmrGen;

/// <summary>
/// The "band" suite (B3): a page of a commercial band score, as the scans in data/scans show one.
/// A vocal staff with chord names above and lyrics below, a guitar and a bass each with its TAB,
/// sometimes a keyboard's grand staff, a drum kit (cross heads, two voices, fills) and sometimes a
/// one-line percussion staff; rhythm slashes, percent repeats of one bar and of two, a rehearsal
/// mark at each section. Not one of <see cref="Gen.Suites"/> (asked for by name): it is written by
/// its own writer. The truth (.notes) holds the pitched staves' notes as written; slash bars and
/// the drums have none (a percent repeat's bars have the bars they repeat).
/// </summary>
static class Band
{
    static readonly string[] Times = { "4/4", "4/4", "4/4", "4/4", "4/4", "4/4", "3/4", "2/4", "6/8", "12/8" };
    static readonly string[] Syllables =
    {
        "I", "you", "love", "night", "time", "a-", "gain", "dream", "on", "the", "way", "so", "far", "hold", "me",
        "to-", "night", "sun-", "shine", "heart", "fire", "dance", "with", "baby", "yeah", "oh", "run-", "ning", "free",
    };
    const int Bar = 1920;   // ticks in a whole

    sealed class BPart
    {
        public string Id, Clef, Name, Decl;   // Decl: what the part declaration holds besides the clef
        public bool Tab, OneLine, Percussion;
        public string[] Bars;                 // each bar's music as .lys
        public string[] Lower;                // the drums: each bar's lower voice (Bars holds the upper)
        public List<List<GEv>>[] Truth;       // each bar's voices (null: no truth, as for slashes and drums)
        public int[] Copy;                    // 1: the bar repeats the one before (%), 2: the one two before (%%), 0: written out
    }

    public static void Write(string name, Random rng, int bars, string dir)
    {
        var p = new GPiece { Name = name, Suite = "band", Fifths = rng.Next(-4, 5), Time = Gen.Pick(rng, Times) };
        var keyAlt = Gen.KeyAlterations(p.Fifths);
        // sections of four or eight bars, each a rehearsal mark: the letters from one the piece's
        // number picks (so that a set of pieces draws every letter, for the classifier that reads them);
        // every third piece numbers its marks instead, 1 to 9 from one its number picks (a numbered
        // mark is a section named S<n> with the declared label "<n>": Lily# prints the label in the box)
        var sections = new List<(string Name, string Label, int Start, int Len)>();
        int num = int.TryParse(new string(name.Where(char.IsDigit).ToArray()), out int n0) && n0 > 0 ? n0 : 1;
        bool numbered = num % 3 == 0;
        int first = numbered ? (num / 3 - 1) % 9 : (num - 1) % 26;
        for (int b = 0; b < bars;)
        {
            int len = Math.Min(bars - b, rng.NextDouble() < 0.5 ? 4 : 8);
            string mark = numbered ? ((first + sections.Count) % 9 + 1).ToString() : ((char)('A' + (first + sections.Count) % 26)).ToString();
            sections.Add((numbered ? "S" + mark : mark, numbered ? $" \"{mark}\"" : "", b, len));
            b += len;
        }
        // the harmony: one or two chords a bar, their roots the scale's degrees
        var harmony = new List<List<int>>();
        for (int b = 0; b < bars; b++)
            harmony.Add(Enumerable.Range(0, p.Time is "4/4" or "12/8" && rng.NextDouble() < 0.3 ? 2 : 1).Select(_ => Gen.Pick(rng, new[] { 0, 0, 3, 4, 4, 5, 1, 2 })).ToList());

        var parts = new List<BPart>();
        bool vocal = rng.NextDouble() < 0.85, chordNames = vocal && rng.NextDouble() < 0.7, lyrics = vocal && rng.NextDouble() < 0.65;
        if (vocal) parts.Add(Vocal(p, rng, bars, keyAlt, sections));
        if (rng.NextDouble() < 0.9) parts.Add(Guitar(p, rng, bars, keyAlt, harmony));
        if (rng.NextDouble() < 0.35)
        {
            parts.Add(Keyboard(p, rng, bars, keyAlt, "kbr", "treble", "Keyboard", 30, 39, 0.5));
            parts.Add(Keyboard(p, rng, bars, keyAlt, "kbl", "bass", null, 17, 26, 0.3));
        }
        if (rng.NextDouble() < 0.9) parts.Add(BassPart(p, rng, bars, keyAlt, harmony));
        bool drums = rng.NextDouble() < 0.9;
        if (drums) parts.Add(Drums(p, rng, bars, sections));
        if (drums && rng.NextDouble() < 0.3) parts.Add(Percussion(p, rng, bars));
        if (parts.Count == 0) parts.Add(Guitar(p, rng, bars, keyAlt, harmony));
        foreach (var part in parts)
        {
            part.Copy = new int[bars];
            var (one, two) = part.Id switch { "vox" => (0.0, 0.0), "kit" => (0.45, 0.15), "kbr" or "kbl" => (0.15, 0.05), _ => (0.3, 0.08) };
            foreach (var s in sections) Percents(part, rng, s.Start, s.Len, one, two);
        }
        // the keyboard's two staves repeat together (a percent sign spans the grand staff)
        var kbr = parts.FirstOrDefault(q => q.Id == "kbr");
        var kbl = parts.FirstOrDefault(q => q.Id == "kbl");
        if (kbr != null && kbl != null)
            for (int b = 0; b < bars; b++)
            {
                kbl.Copy[b] = kbr.Copy[b];
                if (kbr.Copy[b] != 0) Repeat(kbl, b - kbr.Copy[b], b);
            }

        // ---- the .lys
        var lys = new StringBuilder();
        lys.AppendLine("// Generated by OmrGen (band).");
        lys.AppendLine($"title \"{name}\"");
        Writer.Fonts(lys);
        lys.AppendLine("octave absolute");
        lys.AppendLine($"time {p.Time}");
        lys.AppendLine($"key {KeyName(p.Fifths)} major");
        lys.AppendLine();
        foreach (var part in parts) lys.AppendLine($"part {part.Id} {{ clef {part.Clef}{part.Decl} }}");
        lys.AppendLine();
        var vox = parts.FirstOrDefault(q => q.Id == "vox");
        foreach (var (sname, label, start, len) in sections)
        {
            lys.AppendLine($"section {sname}{label} {{");
            if (chordNames)
                lys.AppendLine("  chords harm { " + string.Join(" ", Enumerable.Range(start, len).Select(b => string.Join(" ", harmony[b].Select(d => ChordName(rng, p.Fifths, keyAlt, d))) + " |")) + " }");
            foreach (var part in parts)
            {
                // (the drums' two voices each carry the repeats: a repeat around "voice { } { }" draws
                // its first bar empty, docs/repro/percent-around-voice.lys)
                if (part.Lower == null) lys.AppendLine($"  {part.Id} {{\n{Stream(part, part.Bars, start, len, "    ")}  }}");
                else lys.AppendLine($"  {part.Id} {{\n    voice {{\n{Stream(part, part.Bars, start, len, "      ")}    }} {{\n{Stream(part, part.Lower, start, len, "      ")}    }}\n  }}");
            }
            if (lyrics)
            {
                // a syllable a note; a bar with none is left empty
                var words = new StringBuilder();
                for (int b = start; b < start + len; b++)
                {
                    foreach (var e in vox.Truth[b][0].Where(e => !e.IsRest)) words.Append(Gen.Pick(rng, Syllables)).Append(' ');
                    words.Append("| ");
                }
                lys.AppendLine("  lyrics words { " + words + "}");
            }
            lys.AppendLine("}");
        }
        lys.AppendLine();
        lys.AppendLine("form { " + string.Join(" ", sections.Select(s => s.Name)) + " }");
        lys.AppendLine();
        lys.AppendLine("score {");
        if (chordNames) lys.AppendLine("  chords harm");
        foreach (var part in parts)
        {
            string staff = $"staff {part.Id}" + (part.Name != null ? $" \"{part.Name}\"" : "") + (part.OneLine ? " as lines 1" : "");
            if (part.Id == "kbr") lys.AppendLine("  grandStaff {");
            lys.AppendLine((part.Id is "kbr" or "kbl" ? "    " : "  ") + staff);
            if (part.Id == "kbl") lys.AppendLine("  }");
            if (part.Id == "vox" && lyrics) lys.AppendLine("  lyrics words");
            if (part.Tab) lys.AppendLine($"  tab {part.Id}");
        }
        lys.AppendLine("}");
        File.WriteAllText(Path.Combine(dir, name + ".lys"), lys.ToString());

        // ---- the OMR's score file: the five-line staves as parts, the TABs and one-line staves between
        var score = new StringBuilder();
        score.AppendLine($"title {name}");
        foreach (var part in parts)
        {
            if (part.OneLine) { score.AppendLine("line"); continue; }
            score.AppendLine($"part {part.Id} {part.Clef} \"{part.Name ?? part.Id}\"");
            if (part.Tab) score.AppendLine("tab");
        }
        score.AppendLine($"bar 1 time {p.Time} key {KeyName(p.Fifths)}");
        File.WriteAllText(Path.Combine(dir, name + ".score"), score.ToString());

        // ---- the truth, one file per staff and voice (bars without truth left out)
        foreach (var part in parts.Where(q => q.Truth.Any(t => t != null)))
        {
            int nv = part.Truth.Where(t => t != null).Max(t => t.Count);
            for (int v = 0; v < nv; v++)
            {
                var sb = new StringBuilder();
                for (int b = 0; b < bars; b++)
                {
                    if (part.Truth[b] == null || v >= part.Truth[b].Count) continue;
                    double t = b * p.BarTicks;
                    foreach (var e in part.Truth[b][v])
                    {
                        string at = (t / 1920.0).ToString("F8", CultureInfo.InvariantCulture);
                        string len = (e.Sounding / 1920.0).ToString("F8", CultureInfo.InvariantCulture);
                        if (e.IsRest) sb.Append(at).Append("\trest\t").Append(Writer.DurText(e.Ticks)).Append('\t').Append(len).Append('\n');
                        else
                            foreach (var q in e.Pitches)
                                // (as written: a treble_8 staff's truth too, as the reader reads it,
                                // and as Bohemian's bass_8 is scored)
                                sb.Append(at).Append("\tnote\t").Append(q.Midi).Append('\t').Append(Writer.DurText(e.Ticks)).Append('\t').Append(len).Append('\n');
                        t += e.Sounding;
                    }
                }
                File.WriteAllText(Path.Combine(dir, $"{name}-{part.Id}-v{v + 1}.notes"), sb.ToString());
            }
        }
    }

    /// <summary>A section's bars of one voice, a line each, the repeated ones as percent marks ("c4 d e f | % | % |", "a2 b | c d | %2 |").</summary>
    static string Stream(BPart part, string[] bars, int start, int len, string indent)
    {
        var sb = new StringBuilder();
        for (int b = start; b < start + len;)
        {
            int k = 0;
            while (b + 1 + k < start + len && part.Copy[b + 1 + k] == 1) k++;
            if (k > 0) { sb.Append($"{indent}{bars[b]} |{string.Concat(Enumerable.Repeat(" % |", k))}\n"); b += 1 + k; }
            else if (b + 3 < start + len && part.Copy[b + 2] == 2) { sb.Append($"{indent}{bars[b]} | {bars[b + 1]} | %2 |\n"); b += 4; }
            else { sb.Append($"{indent}{bars[b]} |\n"); b++; }
        }
        return sb.ToString();
    }

    /// <summary>Percent repeats in one section: a bar followed by one to three %, or two bars by a %%.</summary>
    static void Percents(BPart part, Random rng, int start, int len, double one, double two)
    {
        for (int b = start; b < start + len;)
        {
            int left = start + len - b;
            if (left >= 4 && rng.NextDouble() < two)
            {
                for (int i = 2; i < 4; i++) { Repeat(part, b + i - 2, b + i); part.Copy[b + i] = 2; }
                b += 4;
                continue;
            }
            int k = left >= 2 && rng.NextDouble() < one ? rng.Next(1, Math.Min(3, left - 1) + 1) : 0;
            for (int i = 1; i <= k; i++) { Repeat(part, b, b + i); part.Copy[b + i] = 1; }
            b += 1 + k;
        }
    }

    static void Repeat(BPart part, int from, int to)
    {
        part.Bars[to] = part.Bars[from];
        part.Truth[to] = part.Truth[from];
        if (part.Lower != null) part.Lower[to] = part.Lower[from];
    }

    static BPart New(string id, string clef, string name, int bars, string decl = "") =>
        new() { Id = id, Clef = clef, Name = name, Decl = decl, Bars = new string[bars], Truth = new List<List<GEv>>[bars] };

    /// <summary>One bar of written notes (a single voice), and its .lys.</summary>
    static void Notes(BPart part, int b, List<GEv> evs)
    {
        part.Truth[b] = new List<List<GEv>> { evs };
        part.Bars[b] = Writer.Line(evs);
    }

    static List<GEv> BarRest(GPiece p) => new() { new GEv { Ticks = p.BarTicks } };

    static BPart Vocal(GPiece p, Random rng, int bars, int[] keyAlt, List<(string Name, string Label, int Start, int Len)> sections)
    {
        var part = New("vox", "treble", "Vocal", bars);
        int cur = 34;
        // the voice often waits out the first section (an intro)
        bool intro = sections.Count > 1 && rng.NextDouble() < 0.4;
        for (int b = 0; b < bars; b++)
            Notes(part, b, intro && b < sections[0].Len || rng.NextDouble() < 0.08 ? BarRest(p)
                : Gen.Line(p, rng, keyAlt, ref cur, 30, 40, chords: 0, triplets: 0, leaps: 0.25, restP: 0.18));
        return part;
    }

    static BPart Guitar(GPiece p, Random rng, int bars, int[] keyAlt, List<List<int>> harmony)
    {
        bool octave = rng.NextDouble() < 0.5;
        var part = New("gtr", octave ? "treble_8" : "treble", "Guitar", bars, " tuning guitar");
        part.Tab = rng.NextDouble() < 0.75;
        int cur = 33;
        string mode = "";
        for (int b = 0; b < bars; b++)
        {
            // the way of playing holds for two bars at a time: slashes, a riff, or chords
            if (b % 2 == 0) { double r = rng.NextDouble(); mode = r < 0.35 ? "slash" : r < 0.7 ? "riff" : "chords"; }
            if (mode == "slash")
            {
                var durs = Gen.Rhythm(p, rng, 0).Select(t => Writer.DurText(t.Ticks)).ToList();
                part.Bars[b] = "/" + string.Join(" ", durs);
                continue;
            }
            Notes(part, b, Gen.Line(p, rng, keyAlt, ref cur, 29, 38, chords: mode == "chords" ? 0.9 : 0.2, triplets: 0, leaps: 0.3, restP: 0.1));
            // (under treble_8 the .lys writes the notes as they sound, an octave below where they are printed: the
            // clef's 8 draws them up. The truth stays as written)
            if (octave) part.Bars[b] = Writer.Line(part.Truth[b][0].Select(e => e.OctaveDown()).ToList());
        }
        return part;
    }

    static BPart Keyboard(GPiece p, Random rng, int bars, int[] keyAlt, string id, string clef, string name, int lo, int hi, double chords)
    {
        var part = New(id, clef, name, bars);
        int cur = (lo + hi) / 2;
        for (int b = 0; b < bars; b++)
            Notes(part, b, Gen.Line(p, rng, keyAlt, ref cur, lo, hi, chords, triplets: 0, leaps: 0.25, restP: 0.1));
        return part;
    }

    static BPart BassPart(GPiece p, Random rng, int bars, int[] keyAlt, List<List<int>> harmony)
    {
        var part = New("bsg", "bass", "Bass", bars, " tuning bass");
        part.Tab = rng.NextDouble() < 0.75;
        int tonic = TonicStep(p.Fifths), cur = 21;
        bool pump = rng.NextDouble() < 0.5;
        for (int b = 0; b < bars; b++)
        {
            if (!pump || rng.NextDouble() < 0.25)
            {
                Notes(part, b, Gen.Line(p, rng, keyAlt, ref cur, 16, 26, chords: 0, triplets: 0, leaps: 0.35, restP: 0.1));
                continue;
            }
            // eighths on the chord's root, now and then its octave or fifth
            var evs = new List<GEv>();
            int n = p.BarTicks / 240, chords = harmony[b].Count;
            for (int i = 0; i < n; i++)
            {
                int root = 14 + (tonic + harmony[b][i * chords / n]) % 7;
                if (root < 16) root += 7;
                double r = rng.NextDouble();
                int d = r < 0.15 ? root + 7 : r < 0.25 ? root + 4 : root;
                var e = new GEv { Ticks = 240 };
                if (rng.NextDouble() > 0.06) e.Pitches.Add(new GPitch(d, keyAlt[((d % 7) + 7) % 7]));
                evs.Add(e);
            }
            Notes(part, b, evs);
        }
        return part;
    }

    static BPart Drums(GPiece p, Random rng, int bars, List<(string Name, string Label, int Start, int Len)> sections)
    {
        var part = New("kit", "percussion", "Drums", bars);
        part.Percussion = true;
        part.Lower = new string[bars];
        bool compound = p.Time.EndsWith("/8");
        int eighths = p.BarTicks / 240, beat = compound ? 720 : 480;
        // the groove holds through the piece: its cymbal, and whether the hi-hat plays eighths or quarters
        string cym = rng.NextDouble() < 0.75 ? "hh" : "cymr";
        bool quarters = !compound && rng.NextDouble() < 0.2;
        for (int b = 0; b < bars; b++)
        {
            bool first = sections.Any(s => s.Start == b), last = sections.Any(s => s.Start + s.Len - 1 == b);
            if (last && b > 0 && rng.NextDouble() < 0.5)
            {
                // a fill: snare and toms, eighths and sixteenths, under a rest in the upper voice
                var fill = new List<string>();
                string[] drums = { "sn", "sn", "tomh", "tomml", "toml", "tomfh", "tomfl" };
                int t = 0, k = 0;
                while (t < p.BarTicks)
                {
                    int d = p.BarTicks - t >= 240 && rng.NextDouble() < 0.4 ? 240 : 120;
                    fill.Add(drums[Math.Min(drums.Length - 1, k++ * drums.Length * 120 / p.BarTicks)] + Writer.DurText(d));
                    t += d;
                }
                part.Bars[b] = string.Join(" ", fill);
                part.Lower[b] = "s" + Writer.DurText(p.BarTicks);
                continue;
            }
            var up = new List<string>();
            if (quarters)
                for (int i = 0; i < p.BarTicks / 480; i++) up.Add((i == 0 && first ? "cymc" : cym) + "4");
            else
                for (int i = 0; i < eighths; i++)
                    up.Add((i == 0 && first ? "cymc" : cym == "hh" && rng.NextDouble() < 0.08 ? "hho" : cym) + "8");
            var down = new List<string>();
            for (int i = 0, n = p.BarTicks / beat; i < n; i++)
            {
                bool back = i % 2 == 1;
                string[] pats = compound
                    ? back ? new[] { "sn4.", "sn4 bd8", "sn8 bd bd" } : new[] { "bd4.", "bd4 bd8", "bd8 r bd" }
                    : back ? new[] { "sn4", "sn4", "sn8 bd", "sn8 sn" } : new[] { "bd4", "bd4", "bd8 bd", "r8 bd" };
                down.Add(Gen.Pick(rng, pats));
            }
            part.Bars[b] = string.Join(" ", up);
            part.Lower[b] = string.Join(" ", down);
        }
        return part;
    }

    static BPart Percussion(GPiece p, Random rng, int bars)
    {
        var part = New("perc", "percussion", "Perc.", bars);
        part.OneLine = part.Percussion = true;
        string drum = rng.NextDouble() < 0.7 ? "bd" : "ss";
        for (int b = 0; b < bars; b++)
            part.Bars[b] = string.Join(" ", Gen.Rhythm(p, rng, 0).Select(t => (rng.NextDouble() < 0.25 ? "r" : drum) + Writer.DurText(t.Ticks)));
        return part;
    }

    static int TonicStep(int fifths) => ((fifths * 4) % 7 + 7) % 7;

    static string KeyName(int fifths) => new[] { "ces", "ges", "des", "aes", "ees", "bes", "f", "c", "g", "d", "a", "e", "b", "fis", "cis" }[fifths + 7];

    /// <summary>A chord name on the scale's degree (0 = the tonic), spelled in the key.</summary>
    static string ChordName(Random rng, int fifths, int[] keyAlt, int degree)
    {
        string Note(int step) => "CDEFGAB"[step] + (keyAlt[step] > 0 ? "#" : keyAlt[step] < 0 ? "b" : "");
        int root = (TonicStep(fifths) + degree) % 7;
        string[] kinds = degree switch
        {
            0 or 3 => new[] { "", "", "maj7", "add9", "6" },
            4 => new[] { "", "7", "7", "sus4", "7sus4" },
            6 => new[] { "m7-5" },
            _ => new[] { "m", "m7", "m7", "m9" },
        };
        string name = Note(root) + Gen.Pick(rng, kinds);
        if (rng.NextDouble() < 0.12) name += "/" + Note((root + 2) % 7);   // over its third
        return name;
    }
}
