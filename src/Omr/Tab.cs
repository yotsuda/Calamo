namespace Omr;

/// <summary>A fret number read on a TAB: where it stands, its string (0 = the top line, the highest), and its pitch as tuned.</summary>
sealed class TabNote
{
    public double X, Y;
    public int String, Fret;
    public int Midi;            // the open string's pitch plus the fret (the TAB's own octave)
    public double Score;        // the detector's confidence in its least sure digit
    // the other frets the detector half saw there (one digit read as another it scored at least
    // Tab.AltFloor), surest first
    public List<(int Fret, double Score)> Alts = new();
    // a digit under the detector's threshold: read by no head, only a rival for one (Tab.Apply)
    public bool Weak;
}

/// <summary>
/// TAB under a part's staff (B5): the staff and the TAB write the same notes, so each checks the
/// other (principle 2). The learnt detector's TAB digits (tab_0..tab_9) are joined into fret
/// numbers string by string and turned into pitches by the tuning; the staff's reading is then
/// held against them note by note. The octave between the two is not assumed (Lily#'s TAB under a
/// treble staff is at the staff's pitch, under treble_8 an octave below, and a band score's guitar
/// and bass staves stand an octave above their TABs): each part votes it, from the fret numbers
/// whose nearby heads agree with them in pitch class.
/// </summary>
static class Tab
{
    public static bool Verbose;     // --tab: each column's reading and fret numbers
    // fret numbers met in measures whose octave was voted, and those a head of their pitch bore out
    // (a scan has no truth: how well the TAB's digits are read, as the staves see it)
    public static int Seen, BorneOut, Reread;
    // A digit's other reading counts down to this score: the detector's hand-drawn 7 with a stem
    // through it is a 1 at 0.6 and a 7 at 0.2-0.35 (Fantasy's guitar), and the staff's head says which.
    // Down to AltFloor a sure digit's reading is a rival only: Fantasy's G-string 7 read as 1 at 0.64,
    // a 7 at 0.08, may still be the D its head says, which the B string's 7 read as 3 would take
    public static double AltScore = 0.15, AltFloor = 0.05;
    // open strings, highest first (the TAB's top line)
    static readonly int[] Guitar = { 64, 59, 55, 50, 45, 40 };     // E4 B3 G3 D3 A2 E2
    static readonly int[] Bass4 = { 43, 38, 33, 28 };               // G2 D2 A1 E1
    static readonly int[] Bass5 = { 43, 38, 33, 28, 23 };           // + B0
    static readonly int[] Bass6 = { 48, 43, 38, 33, 28, 23 };       // C3 .. B0

    /// <summary>The system's fret numbers, by the part whose staff stands above each TAB.</summary>
    public static Dictionary<int, List<TabNote>> Read(SystemInfo sys, List<Detection> det, List<Detection> weak, double s, Func<int, string> clefOf)
    {
        var byPart = new Dictionary<int, List<TabNote>>();
        if (det == null) return byPart;
        for (int i = 1; i < sys.All.Count; i++)
        {
            var tab = sys.All[i];
            if (tab.Kind != "tab" || sys.All[i - 1].Kind != "") continue;
            int p = sys.Staves.IndexOf(sys.All[i - 1]);
            if (p < 0) continue;
            int n = tab.Lines.Length;
            double gap = tab.S;
            bool bass = clefOf(p) is "bass" or "bass_8";
            int[] tuning = !bass ? (n == 6 ? Guitar : null) : n switch { 4 => Bass4, 5 => Bass5, 6 => Bass6, _ => null };
            if (tuning == null) continue;
            // each digit on its string (a digit stands on its line, within a third of the lines' gap)
            var digits = new List<(double X, double Y, int String, int Value, double Score, float[] Digits)>();
            foreach (var d in det)
            {
                if (!d.Class.StartsWith("tab_")) continue;
                int k = Enumerable.Range(0, n).OrderBy(j => Math.Abs(tab.Lines[j].Y - d.Y)).First();
                if (Math.Abs(tab.Lines[k].Y - d.Y) > 0.35 * gap) continue;
                digits.Add((d.X, d.Y, k, d.Class[4] - '0', d.Score, d.Digits));
            }
            // neighbours on a string make one number ("12": digits closer than two spaces; frets
            // in a row stand farther apart, a note's width and more)
            var notes = new List<TabNote>();
            foreach (var str in digits.GroupBy(t => t.String))
            {
                var row = str.OrderBy(t => t.X).ToList();
                for (int a = 0; a < row.Count;)
                {
                    int b = a + 1;
                    while (b < row.Count && b - a < 2 && row[b].X - row[b - 1].X < 2.0 * s) b++;
                    int fret = 0;
                    for (int c = a; c < b; c++) fret = fret * 10 + row[c].Value;
                    if (fret <= 24)
                    {
                        var note = new TabNote { X = (row[a].X + row[b - 1].X) / 2, Y = row[a].Y, String = str.Key, Fret = fret, Midi = tuning[str.Key] + fret,
                                                 Score = row.Skip(a).Take(b - a).Min(t => t.Score) };
                        // one digit read otherwise
                        for (int c = a; c < b; c++)
                        {
                            if (row[c].Digits == null) continue;
                            for (int v = 0; v < 10; v++)
                            {
                                if (v == row[c].Value || row[c].Digits[v] < AltFloor) continue;
                                int alt = 0;
                                for (int e = a; e < b; e++) alt = alt * 10 + (e == c ? v : row[e].Value);
                                double sc = Math.Min(row[c].Digits[v], Enumerable.Range(a, b - a).Where(e => e != c).Select(e => row[e].Score).DefaultIfEmpty(1).Min());
                                if (alt <= 24 && alt != fret) note.Alts.Add((alt, sc));
                            }
                        }
                        note.Alts.Sort((u, v) => v.Score.CompareTo(u.Score));
                        notes.Add(note);
                    }
                    a = b;
                }
            }
            // the digits under the threshold, one fret each, where no digit over it stands (there they
            // are its other readings) and the surest of those at one place
            var rivals = new List<TabNote>();
            foreach (var d in weak.OrderByDescending(d => d.Score))
            {
                int k = Enumerable.Range(0, n).OrderBy(j => Math.Abs(tab.Lines[j].Y - d.Y)).First();
                if (Math.Abs(tab.Lines[k].Y - d.Y) > 0.35 * gap) continue;
                if (digits.Any(t => t.String == k && Math.Abs(t.X - d.X) < 0.5 * s) || rivals.Any(t => t.String == k && Math.Abs(t.X - d.X) < 0.5 * s)) continue;
                var note = new TabNote { X = d.X, Y = d.Y, String = k, Fret = d.Class[4] - '0', Midi = tuning[k] + d.Class[4] - '0', Score = d.Score, Weak = true };
                // (a digit itself in doubt rivals with its surer readings only: Fantasy p07's G-string
                // 8 at 0.49, a 2 at 0.09, does not stand against the D string's 7 read as 1)
                for (int v = 0; v < 10; v++)
                    if (v != note.Fret && d.Digits[v] >= AltScore) note.Alts.Add((v, d.Digits[v]));
                rivals.Add(note);
            }
            notes.AddRange(rivals);
            if (notes.Count > 0) byPart[p] = notes;
        }
        return byPart;
    }

    /// <summary>
    /// Holds a part's measure against the fret numbers under it. With the part's octave known,
    /// each fret number first finds a head of its pitch near it, or of its other reading when no
    /// other fret might be read as that head (a digit misread); one that finds none turns the
    /// nearest head within two semitones that no fret number bore out into its pitch (the key
    /// missed on the head's step; anything else only when the fret is sure), or else, when sure
    /// and every head over it is borne out, joins their chord (a head missed). Returns how many
    /// pitches were changed and added; a fret left quarrelling with a head goes to quarrels.
    /// </summary>
    public static (int Changed, int Added) Apply(List<Ev> evs, List<Ev> single, List<TabNote> tab, double x0, double x1, double s, int fifths,
                                                 Dictionary<int, int> votes, List<string> quarrels = null, int? systemFifths = null)
    {
        var inBar = tab.Where(t => !t.Weak && t.X > x0 && t.X < x1).OrderBy(t => t.X).ToList();
        // every head read, with its event (a chord's heads share the event's x; a rhythm slash has no pitch)
        var heads = evs.Where(e => !e.IsRest && !e.Slash).SelectMany(e => Pitches(e).Select(p => new Slot { E = e, P = p })).ToList();
        if (heads.Count == 0 || inBar.Count == 0) return (0, 0);
        // (a chord's frets stand zigzag left and right of its column, and a second's heads side by
        // side: a fret number looks for its head by pitch first, within two spaces and a half)
        List<Slot> Near(TabNote t, double within) => heads.Where(h => Math.Abs(h.E.X - t.X) < within * s).OrderBy(h => Math.Abs(h.E.X - t.X)).ToList();
        // the octave: a fret number whose nearby heads agree with it in pitch class, in one octave only, votes
        foreach (var t in inBar)
        {
            var diffs = Near(t, 1.5).Select(h => h.P.Midi() - t.Midi).Where(d => d % 12 == 0).Distinct().ToList();
            if (diffs.Count == 1) votes[diffs[0]] = votes.GetValueOrDefault(diffs[0]) + 1;
        }
        int total = votes.Values.Sum();
        var best = votes.OrderByDescending(v => v.Value).FirstOrDefault();
        if (Verbose) Console.WriteLine($"    tab x {x0:F0}-{x1:F0}: {inBar.Count} fret numbers, {heads.Count} heads; octave votes {string.Join(" ", votes.Select(v => $"{v.Key}:{v.Value}"))}");
        if (total < 3 || 2 * best.Value <= total) return (0, 0);
        int octave = best.Key;
        // the heads the fret numbers bear out
        var left = new List<TabNote>();
        foreach (var t in inBar)
        {
            var h = Near(t, 2.5).FirstOrDefault(h => !h.Taken && h.P.Midi() == t.Midi + octave);
            if (h != null) h.Taken = true;
            else left.Add(t);
        }
        // (a two-digit fret's thin 1 goes unseen, or its two 1s make one: Fantasy p09's small
        // 10, 11 and 12 read as 0, 1 and 2. A lone digit a head bears out ten frets up is that,
        // when no fret's own reading took the head: Fantasy p03's G-string 7 read as 1 took the
        // F sharp of the B string's 7 beside it)
        foreach (var t in left.Where(t => t.Fret < 10).ToList())
        {
            var h = Near(t, 2.5).FirstOrDefault(h => !h.Taken && h.P.Midi() == t.Midi + 10 + octave);
            if (h == null) continue;
            h.Taken = true;
            left.Remove(t);
        }
        Seen += inBar.Count;
        BorneOut += inBar.Count - left.Count;
        // a fret no head bore out that a head left over would bear out read as the detector's other
        // reading is that reading (the staff's step is seldom wrong, the digit's shape often is): it
        // is neither a change nor a quarrel. After every first reading has had its head, so that a
        // second reading takes no head another fret's first reading claims; and only a head no other
        // fret might be read as, the digits under the threshold too (Time After Time's 7 on the E
        // string, a 1 at 0.20, would take the F of a 6 on the B string the detector saw as a 0 at 0.42)
        var rivals = tab.Where(t => t.Weak && t.X > x0 && t.X < x1).ToList();
        IEnumerable<int> Readings(TabNote t) => (t.Weak ? t.Alts.Prepend((t.Fret, t.Score)) : t.Alts).Select(r => t.Midi - t.Fret + r.Fret + octave);
        bool Rivalled(TabNote t, Slot h) => left.Concat(rivals).Any(c => c != t && Math.Abs(h.E.X - c.X) < 2.5 * s && Readings(c).Contains(h.P.Midi()));
        foreach (var t in left.ToList())
            foreach (var (fret, sc) in t.Alts.Where(a => a.Score >= AltScore))
            {
                int midi = t.Midi - t.Fret + fret;
                var h = Near(t, 2.5).FirstOrDefault(h => !h.Taken && h.P.Midi() == midi + octave);
                if (h == null) continue;
                if (Rivalled(t, h))
                {
                    if (Verbose) Console.WriteLine($"    tab fret {t.Fret} on string {t.String} at x {t.X:F0} might be {fret} ({sc:F2}), but {h.P.Lys()} at x {h.E.X:F0} might be another fret's");
                    break;
                }
                if (Verbose) Console.WriteLine($"    tab fret {t.Fret} on string {t.String} at x {t.X:F0} read {fret} ({sc:F2}): {h.P.Lys()} at x {h.E.X:F0}");
                h.Taken = true;
                t.Fret = fret;
                t.Midi = midi;
                t.Score = sc;
                left.Remove(t);
                Reread++;
                break;
            }
        if (Verbose)
            foreach (var t in left)
                Console.WriteLine($"    tab fret {t.Fret} on string {t.String} at x {t.X:F0} ({Pitch.FromMidi(t.Midi + octave).Lys()}): heads near {string.Join(" ", Near(t, 1.5).Select(h => h.P.Lys() + (h.Taken ? "*" : "")))}");
        int[] keyAlt = Keys.Alterations(fifths);
        // (the key the other staves of the system read: a staff whose own key went unread is put
        // right to it too -- Fantasy p05's bass, read with no key, F under 4s on the D string in G major)
        int[] otherAlt = systemFifths is int sf && sf != fifths ? Keys.Alterations(sf) : null;
        int changed = 0, added = 0;
        var settled = new HashSet<TabNote>();
        foreach (var t in left)
        {
            int want = t.Midi + octave;
            // a head no fret number bore out, within two semitones: an accidental missed (the step
            // read spells the fret's pitch), or a head read a step off. The detector confuses a
            // hand-drawn 9 with a 7 (Fantasy's guitar, at 0.57 and 0.59) and a 3 with a 2 (Time
            // After Time's, up to 0.88), and the staff's step is seldom wrong. What a fret
            // trusted (TrustScore) may do is put the key back on a head's step (the key or an
            // accidental misapplied: Fantasy's bass read E double flat under a 7 on the A string,
            // F under 4s on the D string in G major); anything else it does only when sure (SureScore)
            var h = Near(t, 1.0).Where(h => !h.Taken && Math.Abs(h.P.Midi() - want) <= 2).OrderBy(h => Math.Abs(h.P.Midi() - want)).FirstOrDefault();
            var p = h != null ? Spell(want, h.P.Diatonic, otherAlt != null && fifths == 0 ? otherAlt : keyAlt) : default;
            bool keyed = h != null && p.Diatonic == h.P.Diatonic
                         && (p.Alter == keyAlt[Pitch.Mod7(p.Diatonic)] || otherAlt != null && p.Alter == otherAlt[Pitch.Mod7(p.Diatonic)]);
            if (h != null && (keyed || t.Score >= Symbols.SureScore))
            {
                if (Verbose) Console.WriteLine($"    tab: {h.P.Lys()} at x {h.E.X:F0} y {h.E.Y:F0} -> {p.Lys()} (fret {t.Fret} on string {t.String}, {t.Score:F2})");
                Replace(h.E, single, h.P, p, s);
                h.P = p;
                h.Taken = true;
                changed++;
                settled.Add(t);
                continue;
            }
            if (h != null) continue;
            // a head missed: it joins the chord of the column (the event with the nearest pitch),
            // when the fret is sure and the fret numbers bear out every head there. A head they do
            // not is a quarrel, not a miss (Fantasy p03 bar 8: the staff's B2s over the TAB's Ds, a
            // third apart all through the bar), and a head of the pitch near is the fret's own (a
            // digit read twice). (Time After Time's frets stand off their column: the next note's
            // fret bears out a head, and this one's would join it)
            var col = Near(t, 1.0);
            if (t.Score < Symbols.SureScore || col.Count == 0 || col.Any(c => !c.Taken) || Near(t, 2.5).Any(c => c.P.Midi() == want)) continue;
            var near = col.OrderBy(c => Math.Abs(c.P.Midi() - want)).First();
            var q = Spell(want, near.P.Diatonic, keyAlt);
            if (Verbose) Console.WriteLine($"    tab: {q.Lys()} added to {near.P.Lys()} at x {near.E.X:F0} y {near.E.Y:F0} (fret {t.Fret} on string {t.String}, {t.Score:F2})");
            Add(near.E, single, q, s);
            heads.Add(new Slot { E = near.E, P = q, Taken = true });
            added++;
            settled.Add(t);
        }
        // what is left is for the reader of the output to look at: a fret number over a head it
        // does not bear out, neither put right (a digit misread, a head misread, the page's own
        // slip) -- the redraw check's "? verify" (a fret with no head near, or a head with no fret,
        // says less: the TAB's slashes and repeats, a digit unseen)
        foreach (var t in left.Where(t => !settled.Contains(t)))
        {
            var odd = Near(t, 1.0).Where(h => !h.Taken).ToList();
            if (odd.Count == 0) continue;
            quarrels?.Add(Memo.Tab(t.Fret, Pitch.FromMidi(t.Midi + octave), odd.Select(h => h.P)));
        }
        return (changed, added);
    }

    sealed class Slot
    {
        public Ev E;
        public Pitch P;
        public bool Taken;      // a fret number bears it out
    }

    static List<Pitch> Pitches(Ev e) => e.Chord != null ? new List<Pitch>(e.Chord) : new List<Pitch> { e.P };

    /// <summary>
    /// A spelling of the pitch near the diatonic step read: the step the key alters to it first
    /// (no accidental written), then a sharp in a sharp key and a flat in a flat one, then the
    /// step read (the accidental missed), then the plainest.
    /// </summary>
    static Pitch Spell(int midi, int near, int[] keyAlt)
    {
        int sign = Math.Sign(keyAlt.Sum());
        // (a step read too far off to name it: the pitch's own)
        if (Math.Abs(new Pitch(near, 0).Midi() - midi) > 3) near = Pitch.FromMidi(midi).Diatonic;
        var cands = new List<Pitch>();
        for (int d = near - 2; d <= near + 2; d++)
        {
            int alt = midi - new Pitch(d, 0).Midi();
            if (Math.Abs(alt) <= 1) cands.Add(new Pitch(d, alt));
        }
        return cands.OrderByDescending(p => p.Alter == keyAlt[Pitch.Mod7(p.Diatonic)])
                    .ThenByDescending(p => p.Alter == 0 || Math.Sign(p.Alter) == sign)
                    .ThenByDescending(p => p.Diatonic == near)
                    .ThenBy(p => Math.Abs(p.Alter))
                    .ThenBy(p => Math.Abs(p.Diatonic - near)).First();
    }

    static void Replace(Ev e, List<Ev> single, Pitch was, Pitch now, double s)
    {
        if (e.Chord != null)
        {
            e.Chord[e.Chord.IndexOf(was)] = now;
            e.Chord.Sort((a, b) => a.Diatonic.CompareTo(b.Diatonic));
            e.P = e.Chord[^1];
        }
        else e.P = now;
        var one = single.FirstOrDefault(o => !o.IsRest && o.P == was && Same(o, e, s));
        if (one != null) one.P = now;
    }

    static void Add(Ev e, List<Ev> single, Pitch p, double s)
    {
        e.Chord ??= new List<Pitch> { e.P };
        e.Chord.Add(p);
        e.Chord.Sort((a, b) => a.Diatonic.CompareTo(b.Diatonic));
        e.P = e.Chord[^1];
        var one = single.FirstOrDefault(o => !o.IsRest && Same(o, e, s));
        if (one == null) return;
        var c = one.Copy();
        c.P = p;
        c.Tie = false;
        single.Insert(single.IndexOf(one) + 1, c);
    }

    /// <summary>Whether a single head's event is of the chord's column (its stem, or its x when stemless).</summary>
    static bool Same(Ev o, Ev e, double s) => o.Stem == e.Stem && (e.Stem != 0 ? Math.Abs(o.StemX - e.StemX) < 0.3 * s : Math.Abs(o.X - e.X) < 0.35 * s);
}
