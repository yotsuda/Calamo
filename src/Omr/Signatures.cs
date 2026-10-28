namespace Omr;

/// <summary>A clef, key signature or time signature read from the page.</summary>
sealed class Sig
{
    public Staff St;
    public double X;
    public string Clef;      // treble, treble_8, alto, tenor, bass, bass_8
    public int? Fifths;      // key signature: + sharps, - flats
    public string Time;      // "12/8"
    public bool Guessed;     // no sign printed: the time the measures fill (Reader.CheckTimes), not a glyph read
    public bool Mid;         // a clef inside the staff, past its start (a change within or between measures, MidClefs): not the staff's opening clef
}

static class Signatures
{
    public static bool Verbose;
    static readonly Dictionary<Other, List<Other>> joinedFrom = new();
    static readonly Dictionary<Other, Other> splitFrom = new();     // a half of a piece cut at its waist

    /// <summary>
    /// Reads the clef and key at each system start, the time signature after them, and key or
    /// time changes after barlines. The glyphs found are marked so they are not read as rests
    /// or accidentals later.
    /// </summary>
    /// <param name="clefsRead">called once every staff's clef is read, before anything goes by part
    /// (a system that hides some parts' staves has its staves given parts by their clefs)</param>
    public static void Detect(PageSymbols ps, Img ink, List<Comp> holes, List<SystemInfo> systems, double s, Action clefsRead = null)
    {
        foreach (var sys in systems)
        {
            var opening = new Dictionary<Staff, Sig>();     // each staff's time after its clef and key
            var changes = new List<(Sig Sig, int Bar, List<(Other O, string Was)> Named)>();   // the keys read after barlines, with their pieces' names before
            foreach (var st in sys.Staves)
            {
                var mine = ps.Others.Where(o => o.St == st).ToList();
                double after = st.Left;
                // (a clef's 8 close over the next staff may be counted that staff's piece)
                var near = ps.Others.Where(o => o.St != null && o.St.System == st.System).ToList();
                var clef = ReadClef(mine, near, ink, st, s);
                // (on a scan the clef comes apart and the rules name it wrong or not at all; the learnt detector knows its shape)
                if (ps.Detections != null) clef = DetectedClef(ps, mine, near, st, clef, s);
                if (clef != null) { ps.Sigs.Add(clef.Value.Sig); after = clef.Value.EndX; }
                // a key signature's signs stand in their fixed order and places (a cut-time C is not a sharp)
                var key = ReadKey(ps, mine, ink, st, after, s, clef?.Sig.Clef, strict: clef != null);
                // (on a scan the signs' shapes fail the rules, or the first sign is swallowed into
                // the clef's pieces and the rules count one short; the learnt detector still sees
                // them all. Looked for from the clef's start, and taken when it counts more. Where the
                // rules read no key at all, a sign the detector is less than sure of still counts: a
                // retrained model scored chords008's sharps 0.81 and 0.88 for the last one's 0.91 and
                // 0.94, and the key went, with every F and C of the piece)
                if (ps.Detections != null)
                {
                    var dk = DetectedKey(ps, st, clef?.Sig.X ?? after, s, out var signs, Symbols.TrustScore, clef: clef?.Sig.Clef);
                    // (more signs than the rules counted: taken when the rules' signs are among them --
                    // the detector then extends the rules' reading rather than contradicting it (basic010's
                    // one sharp of five on a scan) -- or when it is sure of every sign)
                    bool more = dk != null && key != null && Math.Abs(dk.Value.Sig.Fifths.Value) > Math.Abs(key.Value.Sig.Fifths.Value);
                    bool extends = more && Math.Sign(dk.Value.Sig.Fifths.Value) == Math.Sign(key.Value.Sig.Fifths.Value)
                                        && dk.Value.Sig.X < key.Value.Sig.X + 0.6 * s && dk.Value.EndX > key.Value.EndX - 0.6 * s;
                    if (dk != null && (key == null || (more && (extends || signs.All(t => t.Score >= Symbols.SureScore)))))
                    {
                        if (Verbose && key != null) Console.WriteLine($"    key from the detector over the rules' {key.Value.Sig.Fifths}, staff {st.System + 1}/{st.Part}{(extends ? " (the rules' signs among its own)" : "")}");
                        key = dk;
                        // the signs' pieces of ink are named only now: counted but not taken (the
                        // rules' key stands), a sign may be the first note's accidental
                        NameSigns(ps, st, signs);
                    }
                }
                if (key != null) { ps.Sigs.Add(key.Value.Sig); after = key.Value.EndX; }
                // every system restates its key after the clef: none there means no sharps or flats
                else if (clef != null) ps.Sigs.Add(new Sig { St = st, X = clef.Value.Sig.X + 1, Fifths = 0 });
                var time = Time(ps, mine, ink, holes, st, after, s);
                if (time != null) { ps.Sigs.Add(time); opening[st] = time; }
                // (the clefs inside the staff, named before the bars' keys and times are read so that
                // their pieces are not taken for signs or digits)
                MidClefs(ps, mine, st, after, s);
                for (int b = 1; b < sys.Bars.Count - 1; b++)
                {
                    double start = sys.Bars[b];
                    // (a repeat's or a double barline's second line, thick or thin, stands within a
                    // space past the barline found: a solid stroke from the staff's top line to its
                    // bottom line and nothing else (a natural on the middle line reaches nearly as far,
                    // but is no solid stroke). The bar's key and time begin after it --
                    // leadsheet-collide's ":|" before the "♭ C" of its Tag)
                    var second = mine.Where(o => o.Class == "?" && o.C.MinX > start && o.C.MinX < start + 1.5 * s && o.C.W < 0.8 * s
                                                 && o.C.MinY < st.Top + 0.2 * s && o.C.MaxY > st.Bottom - 0.2 * s && o.C.H < 5 * s && o.C.Fill >= 0.6)
                                     .OrderBy(o => o.C.MinX).FirstOrDefault();
                    if (second != null)
                    {
                        if (Verbose) Console.WriteLine($"    barline at x {start:F0}, staff {st.System + 1}/{st.Part}: its second line at x {second.C.MinX}-{second.C.MaxX}");
                        second.Class = "barline";
                        start = second.C.MaxX + 1;
                    }
                    var named = ps.Detections == null ? null : ps.Others.Select(o => o.Class).ToList();
                    var before = mine.Where(o => o.C.MinX > start - s && o.C.MinX < start + 13 * s).Select(o => (o, o.Class)).ToList();
                    var k = ReadKey(ps, mine, ink, st, start, s, clef?.Sig.Clef, strict: true);
                    // (a key change the detector sees no sign of is something else: another font's
                    // dotted quarter rest after a barline stood where a flat of the key would)
                    if (k != null && ps.Detections != null
                        && !ps.Detections.Any(t => (t.Class == "sharp" || t.Class == "flat" || t.Class == "natural") && t.Score >= Detector.Threshold
                                                   && t.X > k.Value.Sig.X - 0.5 * s && t.X < k.Value.EndX + 0.5 * s && t.Y > st.Top - 2 * s && t.Y < st.Bottom + 2 * s))
                    {
                        if (Verbose) Console.WriteLine($"    key {k.Value.Sig.Fifths} after the barline at x {sys.Bars[b]:F0}, staff {st.System + 1}/{st.Part}: the detector sees no sign there, dropped");
                        k = null;
                        for (int i = 0; i < named.Count; i++) ps.Others[i].Class = named[i];   // (its pieces of ink unnamed again)
                    }
                    if (k != null) { ps.Sigs.Add(k.Value.Sig); start = k.Value.EndX; changes.Add((k.Value.Sig, b, before)); }
                    var t = Time(ps, mine, ink, holes, st, start, s, afterBar: k == null);
                    if (t != null) ps.Sigs.Add(t);
                }
            }
            // a change of key is printed on every staff of the system: a single sign read at a bar
            // on fewer than half of its pitched staves is an accidental leading the bar (kinkel19-2's
            // flat on a chord's C after a barline, the chord's white heads unseen, read as F major
            // on the piano's right hand alone, its A flats natural for four bars). Two signs and
            // more -- a natural cancelling, the new key's sign -- are a change, though the rules
            // read it on one staff alone (faisst4-5's, lost with it: 92.7 -> 82.7 percent)
            // (the signs are counted, not their pieces: a flat cut in two by a staff line is two
            // pieces named key-flat, and kinkel19-2's lone flat before a C after a barline, so
            // counted, stayed a change of key to one flat on the right hand alone -- its A, E and D
            // natural for four bars)
            int pitched = sys.Staves.Count(x => !x.OneLine && x.Kind == "");
            foreach (var grp in changes.GroupBy(c => c.Bar).Where(g => pitched >= 2 && 2 * g.Select(c => c.Sig.St).Distinct().Count() < pitched
                                                                         && g.All(c => Math.Abs(c.Sig.Fifths ?? 0) <= 1 && SignCount(c.Named.Select(o => o.O), s) <= 1)))
                foreach (var c in grp)
                {
                    if (Verbose) Console.WriteLine($"    key {c.Sig.Fifths} after the barline of measure {c.Bar}, staff {c.Sig.St.System + 1}/{c.Sig.St.Part}: on {grp.Select(x => x.Sig.St).Distinct().Count()} of the system's {pitched} staves, dropped");
                    ps.Sigs.Remove(c.Sig);
                    foreach (var (o, was) in c.Named) o.Class = was;
                }
            // A system's opening time signatures stand in one column, set past the widest key: a
            // staff with no key (a drum staff among keyed ones) has its sign several spaces after
            // the clef, past where it was looked for. It is looked for again where the others stand.
            if (opening.Count > 0 && opening.Count < sys.Staves.Count)
            {
                double x = opening.Values.Min(g => g.X);
                foreach (var st in sys.Staves.Where(st => !opening.ContainsKey(st)))
                {
                    var mine = ps.Others.Where(o => o.St == st).ToList();
                    var time = Time(ps, mine, ink, holes, st, x - 0.5 * s, s);
                    if (time == null) continue;
                    if (Verbose) Console.WriteLine($"    time {time.Time} at x {x:F0}, staff {st.System + 1}/{st.Part}, where the other staves' stand");
                    ps.Sigs.Add(time);
                }
            }
        }
        clefsRead?.Invoke();
        if (ps.Detections != null) { AgreeClefsAcrossSystems(ps, systems, s); AgreeWithinSystems(ps, systems, s); AgreeAcrossSystems(ps, systems, s); }
    }

    /// <summary>
    /// A system's staves share their key (concert-pitch parts; a drum staff has none). A staff whose
    /// key went unread (no signs found: none, 0) where most of the system's other pitched staves read
    /// one, takes it when the detector sees that key's signs there, even under its threshold
    /// (Detector.WeakKey): Fantasy p05's bass, its one sharp at 0.42 under five staves of one sharp,
    /// read as F natural throughout. A staff with no signs at all to the detector keeps its key (a
    /// horn in F among staves in two flats).
    /// </summary>
    static void AgreeWithinSystems(PageSymbols ps, List<SystemInfo> systems, double s)
    {
        foreach (var sys in systems)
        {
            var keys = ps.Sigs.Where(g => g.Fifths != null && sys.Staves.Contains(g.St)).GroupBy(g => g.St).Select(grp => grp.OrderBy(g => g.X).First()).ToList();
            var clefs = ps.Sigs.Where(g => g.Clef != null && !g.Mid && sys.Staves.Contains(g.St)).GroupBy(g => g.St).ToDictionary(grp => grp.Key, grp => grp.OrderBy(g => g.X).First());
            var pitched = keys.Where(g => !clefs.TryGetValue(g.St, out var c) || c.Clef != "percussion").ToList();
            foreach (var g in pitched.Where(g => g.Fifths == 0))
            {
                var others = pitched.Where(o => o != g).ToList();
                var common = others.GroupBy(o => o.Fifths.Value).OrderByDescending(grp => grp.Count()).FirstOrDefault();
                if (common == null || common.Key == 0 || 2 * common.Count() <= others.Count) continue;
                // (one other staff is a majority of one: kinkel7-6's scan, its last system the piano's two
                // staves, the right hand's sharp lost -- taken only as the part's other systems read it, below)
                bool lone = common.Count() < 2;
                var dk = DetectedKey(ps, g.St, clefs.TryGetValue(g.St, out var clef) ? clef.X : g.St.Left, s, out var signs, Detector.WeakKey, weak: true, clef: clef?.Clef);
                // (or, seeing none or fewer of the key's signs, where the part's staves in the page's other
                // systems read the key the other staves read: no horn in F, which would read its own key in
                // every system, but a print's faint signs -- kinkel10-6's scan, the voice's E flat all but
                // gone in the second system, its B flat under the rules' reach)
                bool fewer = dk == null || (Math.Sign(dk.Value.Sig.Fifths.Value) == Math.Sign(common.Key) && Math.Abs(dk.Value.Sig.Fifths.Value) < Math.Abs(common.Key));
                var elsewhere = ps.Sigs.Where(o => o.Fifths != null && o.St.Part == g.St.Part && o.St.System != g.St.System).GroupBy(o => o.St).Select(grp => grp.OrderBy(o => o.X).First()).ToList();
                bool partAgrees = fewer && elsewhere.Count >= 2 && 2 * elsewhere.Count(o => o.Fifths == common.Key) > elsewhere.Count;
                if (lone && !(elsewhere.Count >= 3 && elsewhere.All(o => o.Fifths == common.Key))) continue;
                if ((dk == null || dk.Value.Sig.Fifths != common.Key) && !partAgrees)
                {
                    if (Verbose) Console.WriteLine($"    no key on staff {g.St.System + 1}/{g.St.Part} among the system's {common.Key}; the detector sees {(dk == null ? "no key" : dk.Value.Sig.Fifths.ToString())} there: kept");
                    continue;
                }
                if (dk == null || dk.Value.Sig.Fifths != common.Key)
                {
                    if (Verbose) Console.WriteLine($"    no key on staff {g.St.System + 1}/{g.St.Part} among the system's {common.Key}, the detector seeing {(dk == null ? "no key" : dk.Value.Sig.Fifths.ToString())}, but the part's other systems read it: taken");
                    g.Fifths = common.Key;
                    if (dk != null) NameSigns(ps, g.St, signs);
                    continue;
                }
                if (Verbose) Console.WriteLine($"    no key on staff {g.St.System + 1}/{g.St.Part} among the system's {common.Key}, which the detector sees there ({string.Join(" ", signs.Select(t => t.Score.ToString("F2")))}): taken");
                g.Fifths = common.Key;
                NameSigns(ps, g.St, signs);
            }
        }
    }

    /// <summary>
    /// A page's systems agree: every system restates each part's clef and key, the same from one
    /// system to the next unless the music changes between them. A staff whose key (or clef) the
    /// rules read unlike most other staves of its part is suspect, and where the learnt detector
    /// sees on it, though less than sure, the signs (the clef) that would make it agree, the
    /// rules' reading gives way. (basic003: the third system's second flat scored under 0.9 and
    /// the key read as one flat among four systems of two; ties004: the fifth system's C clef
    /// read as tenor among alto.) The pieces of ink of the signs taken are named, as DetectedKey's are.
    /// </summary>
    static void AgreeAcrossSystems(PageSymbols ps, List<SystemInfo> systems, double s)
    {
        // each staff's opening key and clef
        var keys = ps.Sigs.Where(g => g.Fifths != null).GroupBy(g => g.St).Select(grp => grp.OrderBy(g => g.X).First()).ToList();
        var clefs = ps.Sigs.Where(g => g.Clef != null && !g.Mid).GroupBy(g => g.St).Select(grp => grp.OrderBy(g => g.X).First()).ToList();
        foreach (var part in keys.GroupBy(g => g.St.Part))
        {
            var mine = part.ToList();
            if (mine.Count < 3) continue;
            var common = mine.GroupBy(g => g.Fifths.Value).OrderByDescending(grp => grp.Count()).First();
            if (common.Count() <= mine.Count / 2) continue;
            foreach (var g in mine.Where(g => g.Fifths != common.Key))
            {
                var clef = clefs.FirstOrDefault(c => c.St == g.St);
                var dk = DetectedKey(ps, g.St, clef?.X ?? g.St.Left, s, out var signs, Detector.Threshold, clef: clef?.Clef);
                // (or sees fewer signs of the key's kind, the rest lost -- kinkel19-2's five flats packed
                // in two columns, two seen on a staff whose clef was lost too -- and no natural, as a
                // change to a smaller key would print: the other systems' key)
                // (and only where the system's other staves read the common key: after a change of
                // key every staff of the system shows the new, smaller key -- gen-lied-all's changes)
                var mates = keys.Where(o => o.St.System == g.St.System && o.St != g.St).ToList();
                bool fewer = dk != null && g.Fifths != 0 && Math.Sign(dk.Value.Sig.Fifths.Value) == Math.Sign(common.Key) && Math.Abs(dk.Value.Sig.Fifths.Value) < Math.Abs(common.Key)
                             && mates.Count > 0 && 2 * mates.Count(o => o.Fifths == common.Key) > mates.Count
                             && !ps.Detections.Any(t => t.Class == "natural" && t.Score >= Detector.Threshold && t.X > (clef?.X ?? g.St.Left) && t.X < dk.Value.EndX + 2 * s
                                                        && t.Y > g.St.Top - 2 * s && t.Y < g.St.Bottom + 2 * s);
                // (or one sign more, its last at the height of the first head a space or so to its right:
                // that note's accidental, which stands where the larger key's next sign would --
                // kinkel10-4's right hand, its C sharp before a c'' read as the key's second sharp)
                Head own = null;
                if (dk != null && common.Key != 0 && dk.Value.Sig.Fifths == common.Key + Math.Sign(common.Key))
                {
                    var last = signs[^1];
                    double pos = g.St.Pos(last.Y) - (last.Class == "flat" ? 1 : 0);
                    own = ps.Heads.Where(h => h.St == g.St && h.L > last.X && h.L < last.X + 1.5 * s).MinBy(h => h.L) is { } h0 && Math.Abs(h0.Pos - pos) <= 0.6 ? h0 : null;
                }
                if (dk == null || (dk.Value.Sig.Fifths != common.Key && !fewer && own == null))
                {
                    if (Verbose) Console.WriteLine($"    key {g.Fifths} of staff {g.St.System + 1}/{g.St.Part} unlike the other systems' {common.Key}; the detector sees {(dk == null ? "no key" : dk.Value.Sig.Fifths.ToString())} there: kept");
                    continue;
                }
                if (Verbose) Console.WriteLine($"    key {g.Fifths} of staff {g.St.System + 1}/{g.St.Part} unlike the other systems' {common.Key}, which the detector sees there{(fewer ? $" in part ({dk.Value.Sig.Fifths}, no natural)" : own != null ? $" (its last sign the note's at x {own.L:F0})" : "")}: taken");
                g.Fifths = common.Key;
                if (own != null)
                {
                    // (the note's sign named again as nothing, for the note to take)
                    if (Symbols.PieceAt(ps, signs[^1].X, signs[^1].Y, g.St) is { } o && o.Class.StartsWith("key-")) o.Class = "?";
                    signs.RemoveAt(signs.Count - 1);
                }
                NameSigns(ps, g.St, signs);
            }
        }
    }

    /// <summary>
    /// The clefs' half of <see cref="AgreeAcrossSystems"/>, run before the keys are looked at again:
    /// a key's signs are looked for in its clef's places, and a staff whose clef the rules read wrong
    /// has its key's sign out of them (kinkel10-4's left hand, its F clef read as tenor, its sharp on
    /// the F line out of the tenor's places, the key lost with the clef put right)
    /// </summary>
    static void AgreeClefsAcrossSystems(PageSymbols ps, List<SystemInfo> systems, double s)
    {
        var clefs = ps.Sigs.Where(g => g.Clef != null && !g.Mid).GroupBy(g => g.St).Select(grp => grp.OrderBy(g => g.X).First()).ToList();
        foreach (var part in clefs.GroupBy(g => g.St.Part))
        {
            var mine = part.ToList();
            // (the part's staves count, not its clefs read: kinkel7-5's left hand on three systems, the
            // second's clef read by neither the rules nor the detector, the third's a doubtful tenor)
            if (mine.Count < 3 && systems.SelectMany(x => x.Staves).Count(x => x.Part == part.Key && !x.OneLine && x.Kind == "") < 3) continue;
            // (a C clef the rules read where the detector sees no clef has no vote: on a scan an F clef's
            // dots go and its body passes for a C clef's half -- kinkel7-6's left hand, bass, bass, alto
            // and tenor, no kind a majority, two systems a sixth off -- while the detector knows a C clef)
            bool Doubtful(Sig g) => (g.Clef == "alto" || g.Clef == "tenor")
                                    && !ps.Detections.Any(t => t.Class.StartsWith("clef_") && t.Score >= Detector.Threshold
                                                               && t.X > g.St.Left - 0.5 * s && t.X < g.St.Left + 12 * s && t.Y > g.St.Top - 3 * s && t.Y < g.St.Bottom + 3 * s);
            var voters = mine.Where(g => !Doubtful(g)).ToList();
            // (one voter is enough when the detector sees its clef too and every other system's clef is
            // such a doubtful C clef: kinkel7-6's left hand under a detector learnt on dirtied windows, its
            // old F clefs seen at 0.25 and 0.33 on three systems, read alto, alto and tenor, the first
            // system's bass alone confirmed)
            bool Seen(Sig g) => ps.Detections.Any(t => t.Score >= Detector.Threshold && t.X > g.St.Left - 0.5 * s && t.X < g.St.Left + 12 * s
                                                       && t.Y > g.St.Top - 3 * s && t.Y < g.St.Bottom + 3 * s
                                                       && (t.Class, g.Clef.Replace("_8", "")) is ("clef_g", "treble") or ("clef_f", "bass"));
            if (voters.Count < 2 && !(voters.Count == 1 && Seen(voters[0]))) continue;
            // (a clef and its octave clef are one kind here: kinkel10-4's left hand read bass, bass_8 (the
            // lyrics' line under it taken for an 8), bass and tenor, no kind a majority, the tenor kept)
            var common = voters.GroupBy(g => g.Clef.Replace("_8", "")).OrderByDescending(grp => grp.Count()).First();
            if (common.Count() <= voters.Count / 2) continue;
            string usual = common.GroupBy(g => g.Clef).OrderByDescending(grp => grp.Count()).First().Key;
            foreach (var g in mine.Where(g => g.Clef != usual))
            {
                var st = g.St;
                var t = ps.Detections.Where(t => (t.Class == "clef_g" || t.Class == "clef_f" || t.Class == "clef_c") && t.Score >= Detector.Threshold
                                                 && t.X > st.Left - 0.5 * s && t.X < st.Left + 12 * s && t.Y > st.Top - 3 * s && t.Y < st.Bottom + 3 * s)
                                     .OrderBy(t => t.X).FirstOrDefault();
                // (alto and tenor are one glyph, told apart by where it stands: the rules' reading
                // of its place on a scan is the weaker evidence, so the other systems' kind stands
                // even with no detection to confirm it)
                bool cClefs = (g.Clef == "alto" || g.Clef == "tenor") && (usual == "alto" || usual == "tenor");
                // (an F clef read as a C clef by the rules, its dots lost -- one taken for a note's dot, the other
                // run into the body: kinkel7-1's old print -- where the detector sees no clef at all, in a part
                // the other systems give the F clef: a C clef it would have seen)
                // (so too a G clef broken into a C clef's pieces: kinkel10-4's right hand among systems of treble)
                bool lostDots = t == null && (g.Clef == "alto" || g.Clef == "tenor") && (common.Key == "bass" || common.Key == "treble");
                // (an 8 the other systems' clef of the kind lacks, or has, is the rules' reading of
                // what stands under or over it: kinkel10-4's voice, its lyrics' last line under the clef)
                bool eight = g.Clef.Replace("_8", "") == common.Key;
                string kind = t == null ? null : t.Class switch { "clef_g" => "treble", "clef_f" => "bass", _ => st.Pos(t.Y) > 1 ? "tenor" : "alto" };
                if (kind != common.Key && !cClefs && !lostDots && !eight) continue;
                if (Verbose) Console.WriteLine($"    clef {g.Clef} of staff {st.System + 1}/{st.Part} unlike the other systems' {usual}, {(t == null ? lostDots ? "no clef to the detector" : "the same glyph" : $"which the detector sees there ({t.Score:F2})")}: taken");
                g.Clef = usual;
            }
            // (a staff of the part with no clef read at all, where the detector sees no clef of any
            // kind either, has the part's clef as the other systems print it: kinkel19-2's first
            // system, its F clef's body and dots one piece with the system's opening line, read as
            // treble by default, the left hand a sixth off for six bars)
            foreach (var st in systems.SelectMany(x => x.Staves).Where(x => x.Part == part.Key && !x.OneLine && x.Kind == "" && !clefs.Any(c => c.St == x)))
            {
                if (ps.Detections.Any(t => t.Class.StartsWith("clef_") && t.Score >= Detector.Threshold
                                           && t.X > st.Left - 0.5 * s && t.X < st.Left + 12 * s && t.Y > st.Top - 3 * s && t.Y < st.Bottom + 3 * s)) continue;
                if (Verbose) Console.WriteLine($"    no clef on staff {st.System + 1}/{st.Part}, and none to the detector: the other systems' {usual} taken");
                ps.Sigs.Add(new Sig { St = st, X = st.Left, Clef = usual });
            }
        }
    }

    /// <summary>How many signs the pieces named a key's (key-sharp, key-flat, key-natural) make: pieces whose columns overlap are one sign (a flat's stem and bowl cut apart by a staff line, a sign's halves).</summary>
    static int SignCount(IEnumerable<Other> pieces, double s)
    {
        int n = 0, end = int.MinValue;
        foreach (var o in pieces.Where(o => o.Class.StartsWith("key-")).OrderBy(o => o.C.MinX))
        {
            if (o.C.MinX > end + 0.1 * s) n++;
            end = Math.Max(end, o.C.MaxX);
        }
        return n;
    }

    /// <summary>Names the signs' pieces of ink (key-sharp, key-flat) so that nothing reads them as a note's accidental.</summary>
    static void NameSigns(PageSymbols ps, Staff st, List<Detection> signs)
    {
        foreach (var t in signs)
            if (Symbols.PieceAt(ps, t.X, t.Y, st) is { Class: "?" } o) o.Class = "key-" + t.Class;
    }

    /// <summary>
    /// The key signature as the learnt detector sees it: the sharps (or the flats) it is sure of
    /// between the clef and the first head of the staff, standing within the staff, all of one
    /// kind. The signs are returned for the caller to name their pieces of ink (so that nothing
    /// reads them as a note's accidental) once it takes this key.
    /// </summary>
    /// <param name="minScore">how sure the detector must be of a sign (the sure score unless given)</param>
    /// <param name="weak">the detector's signs under its threshold count too</param>
    /// <param name="clef">the staff's clef, if read: the signs must then stand in the key's places</param>
    static (Sig Sig, double EndX)? DetectedKey(PageSymbols ps, Staff st, double startX, double s, out List<Detection> signs, double? minScore = null, bool weak = false, string clef = null)
    {
        double firstHead = ps.Heads.Where(h => h.St == st).Select(h => h.L).DefaultIfEmpty(double.MaxValue).Min();
        signs = (weak ? ps.Detections.Concat(ps.WeakSigns) : ps.Detections).Where(t => (t.Class == "sharp" || t.Class == "flat") && t.Score >= (minScore ?? Symbols.SureScore)
                                             // (from the clef's start, a clef and seven signs span twelve spaces or so;
                                             // and a key never abuts the first note, while that note's own accidental
                                             // stands within a space of it: Fugue 1's first flat on a staff in C major)
                                             && t.X > startX && t.X < Math.Min(firstHead - 1.0 * s, startX + 13 * s)
                                             && Math.Abs(st.Pos(t.Y)) <= 7)
                                 .OrderBy(t => t.X).ToList();
        // (one sign seen twice, at one x and less than a step apart, is one: detector35b saw kinkel7-1's
        // small flat at pos 0.2 and 0.8, 0.55 and 0.59 -- two signs out of the key's places. An old print's
        // two columns stack their signs two steps apart at the least)
        var found = signs;
        signs = found.Where(t => !found.Any(o => o != t && o.Class == t.Class && Math.Abs(o.X - t.X) < 0.5 * s && Math.Abs(st.Pos(o.Y) - st.Pos(t.Y)) < 1
                                                 && (o.Score > t.Score || o.Score == t.Score && found.IndexOf(o) < found.IndexOf(t)))).ToList();
        // (the signs stand a space or so apart: a sign past a wider gap is the first note's, which may
        // stand a space or more before its head when the key ends further off: Petaluma's band008)
        // A font of wide signs (Lily#'s Petaluma: 1.6 spaces, stepped 1.7 apart) sets them edge to edge
        // instead: a sign whose ink all but touches the last one's goes on with the key.
        Comp InkOf(Detection t) => Symbols.PieceAt(ps, t.X, t.Y, st)?.C;
        for (int i = 1; i < signs.Count; i++)
        {
            if (signs[i].X - signs[i - 1].X <= 1.6 * s) continue;
            Comp a = InkOf(signs[i - 1]), b = InkOf(signs[i]);
            if (a != null && b != null && b.MinX - a.MaxX <= 0.35 * s) continue;
            signs.RemoveRange(i, signs.Count - i); break;
        }
        if (Verbose && signs.Count > 0) Console.WriteLine($"    key signs from the detector, staff {st.System + 1}/{st.Part}: {string.Join(" ", signs.Select(t => $"{t.Class}@{t.X:F0},pos{st.Pos(t.Y):F1}({t.Score:F2})"))}"
            + (weak ? "" : $"; weak: {string.Join(" ", ps.WeakSigns.Where(t => t.X > startX && t.X < startX + 13 * s && Math.Abs(st.Pos(t.Y)) <= 7).Select(t => $"{t.Class}@{t.X:F0},pos{st.Pos(t.Y):F1}({t.Score:F2})"))}"));
        if (signs.Count == 0 || signs.Count > 7 || signs.Select(t => t.Class).Distinct().Count() > 1) return null;
        // with the clef known, the signs stand in the key's order and places, as the rules' strict
        // key (within a step: a sign's centre is the detector's; a flat's is a step above its line,
        // its bowl's). A first note's flat on a ledger line under a bass staff in C major is no key
        // (the places, not the order: an old print packs its five flats in two columns, B D A G E
        // from the left -- kinkel19-2's Trautwein print, 1838 -- so each sign stands in one of the
        // key's first places, and each place has its sign)
        if (clef is "treble" or "treble_8" or "alto" or "tenor" or "bass" or "bass_8")
        {
            int middle = Clefs.MiddleLine(clef);
            int[] order = signs[0].Class == "sharp" ? new[] { 3, 0, 4, 1, 5, 2, 6 } : new[] { 6, 2, 5, 1, 4, 0, 3 };
            int Dia(Detection t) => middle + (int)Math.Round(st.Pos(t.Y) - (t.Class == "flat" ? 1 : 0));
            // the places of the key's first k signs left after these signs take theirs, or null where one fits none
            List<int> Left(IEnumerable<Detection> ts, int k)
            {
                var places = order.Take(k).ToList();
                foreach (var t in ts)
                {
                    int dia = Dia(t);
                    int at = places.FindIndex(p => new[] { dia - 1, dia, dia + 1 }.Any(d => Pitch.Mod7(d) == p));
                    if (at < 0) return null;
                    places.RemoveAt(at);
                }
                return places;
            }
            // (the last sign out of the key's places, at the height of the first head a sign's width
            // or two to its right, is that note's accidental: kinkel10-4's voice, its key's sharp and
            // the first note's, the A sharp's, a space and a bit apart -- the key read as none)
            if (signs.Count >= 2 && Left(signs, signs.Count) == null && Left(signs.Take(signs.Count - 1), signs.Count - 1) != null)
            {
                var last = signs[^1];
                double pos = st.Pos(last.Y) - (last.Class == "flat" ? 1 : 0);
                var head = ps.Heads.Where(h => h.St == st && h.L > last.X && h.L < last.X + 2.5 * s).MinBy(h => h.L);
                if (head != null && Math.Abs(head.Pos - pos) <= 0.6)
                {
                    if (Verbose) Console.WriteLine($"    key from the detector, staff {st.System + 1}/{st.Part}: its last sign at x {last.X:F0} pos {pos:F1} is the first note's, at x {head.L:F0} pos {head.Pos}");
                    signs.RemoveAt(signs.Count - 1);
                }
            }
            if (Left(signs, signs.Count) == null)
            {
                // The signs fit the first places of a larger key, some of its signs unseen: packed in
                // two columns, kinkel19-2's five flats (B D A G E) are seen E, A and D surely, B faintly
                // and G not at all. The key is the fewest places that hold them, when the detector sees
                // a sign, however faintly, at each place left over (four flats here: the fifth stays
                // unseen, and the page's other staves read four as well)
                var sure = signs;
                var faint = ps.Detections.Concat(ps.WeakSigns).Where(t => t.Class == sure[0].Class && !sure.Contains(t)
                                                                           && t.X > startX && t.X < Math.Min(firstHead - 1.0 * s, startX + 13 * s) && Math.Abs(st.Pos(t.Y)) <= 7).ToList();
                var taken = new List<Detection>();
                int k = Enumerable.Range(sure.Count + 1, 7 - sure.Count).Where(n => Left(sure, n) != null).DefaultIfEmpty(-1).First();
                foreach (var p in k < 0 ? new List<int>() : Left(signs, k))
                {
                    var f = faint.Where(t => !taken.Contains(t) && new[] { Dia(t) - 1, Dia(t), Dia(t) + 1 }.Any(d => Pitch.Mod7(d) == p)).OrderByDescending(t => t.Score).FirstOrDefault();
                    if (f == null) break;
                    taken.Add(f);
                }
                if (k < 0 || taken.Count < k - signs.Count)
                {
                    if (Verbose) Console.WriteLine($"    key from the detector, staff {st.System + 1}/{st.Part}: its signs at {string.Join(" ", signs.Select(t => $"x {t.X:F0} pos {st.Pos(t.Y):F1}"))} are out of the key's places in the {clef} clef{(k < 0 ? "" : $" (the first {k} places hold them, but not every place left has a sign)")}");
                    return null;
                }
                if (Verbose) Console.WriteLine($"    key from the detector, staff {st.System + 1}/{st.Part}: its {signs.Count} signs fit the first {k} places, the rest seen faintly: {string.Join(" ", taken.Select(t => $"{t.Class}@{t.X:F0},pos{st.Pos(t.Y):F1}({t.Score:F2})"))}");
                signs = signs.Concat(taken).OrderBy(t => t.X).ToList();
            }
        }
        int fifths = signs[0].Class == "sharp" ? signs.Count : -signs.Count;
        double halfW = 0.5 * s;
        if (Verbose) Console.WriteLine($"    key from the detector, staff {st.System + 1}/{st.Part}: {fifths} ({signs.Count} {signs[0].Class} from x {signs[0].X:F0})");
        return (new Sig { St = st, X = signs[0].X - halfW, Fifths = fifths }, signs[^1].X + halfW);
    }

    static bool SmallRound(Comp c, double s) =>
        c.W >= 0.2 * s && c.W <= 0.65 * s && c.H >= 0.2 * s && c.H <= 0.65 * s && c.Fill >= 0.5;

    /// <summary>
    /// The clef at the staff's start as the learnt detector sees it (clef_g, clef_f, clef_c),
    /// against what the rules read. The rules' clef stands when the two agree in kind (they know
    /// the clef's extent and its "8"); the detector's replaces it when they differ and the
    /// detector is sure (voices2006's F clef read as alto, and every pitch of the system with it),
    /// and fills in where the rules read none if the detector trusts it. A C clef is alto or
    /// tenor by where its centre stands.
    /// </summary>
    static (Sig Sig, double EndX)? DetectedClef(PageSymbols ps, List<Other> mine, List<Other> near, Staff st, (Sig Sig, double EndX)? rules, double s)
    {
        // (the leftmost clef near the staff's start; the start may be found too far left, at
        // an instrument name at the staff's height, so the window is wide)
        var clefs = ps.Detections.Where(t => (t.Class == "clef_g" || t.Class == "clef_f" || t.Class == "clef_c") && t.Score >= Symbols.TrustScore
                                             && t.X > st.Left - 0.5 * s && t.X < st.Left + 12 * s && t.Y > st.Top - 3 * s && t.Y < st.Bottom + 3 * s)
                                 .OrderBy(t => t.X).ToList();
        if (Verbose) Console.WriteLine($"    clefs the detector sees, staff {st.System + 1}/{st.Part} (left {st.Left}): {string.Join(" ", ps.Detections.Where(d => d.Class.StartsWith("clef_") && d.X > st.Left - 0.5 * s && d.X < st.Left + 12 * s && d.Y > st.Top - 3 * s && d.Y < st.Bottom + 3 * s).Select(d => $"{d.Class}@{d.X:F0},{d.Y:F0}({d.Score:F2})"))}");
        var t = clefs.FirstOrDefault();
        if (t == null) return rules;
        string KindOf(Detection d) => d.Class switch { "clef_g" => "treble", "clef_f" => "bass", _ => st.Pos(d.Y) > 1 ? "tenor" : "alto" };
        string kind = KindOf(t);
        if (Verbose && t.Class == "clef_c") Console.WriteLine($"    C clef from the detector, staff {st.System + 1}/{st.Part}: centre at pos {st.Pos(t.Y):F2} -> {kind}, rules {rules?.Sig.Clef ?? "none"}");
        if (rules != null && rules.Value.Sig.Clef.Replace("_8", "") == kind) return rules;
        // (the two disagree: the detector's kind wins when it is sure of it, or when it sees nothing
        // of the rules' kind there even at the trust score -- on a scan the rules name a broken
        // F clef alto (piano008), and the detector, at 0.89, has no C clef there at all; a rules'
        // kind the detector also sees is a real doubt, left to the rules)
        if (rules != null && t.Score < Symbols.SureScore && clefs.Any(d => KindOf(d).Replace("tenor", "alto") == rules.Value.Sig.Clef.Replace("_8", "").Replace("tenor", "alto")))
        {
            if (Verbose) Console.WriteLine($"    clef from the detector, staff {st.System + 1}/{st.Part}: {kind} ({t.Score:F2}) unlike the rules' {rules.Value.Sig.Clef}, which it also sees: the rules' stands");
            return rules;
        }
        if (Verbose) Console.WriteLine($"    clef from the detector, staff {st.System + 1}/{st.Part}: {kind} ({t.Score:F2}) for the rules' {rules?.Sig.Clef ?? "none"}");
        // the clef's pieces of ink, so that nothing reads them as rests or accidentals
        double x0 = t.X - 2 * s, x1 = t.X + 1.6 * s;
        // (its "8", from the body's ink: MuseJazz's G clef the rules miss, its 8 a piece apart; not
        // an F clef's reach below the staff, as MuseJazz's plain one, which the rules call alto, goes there)
        var body = mine.Where(o => o.Class == "?" && o.C.MaxX > x0 && o.C.MinX < x1 && o.C.H >= 2.9 * s && Math.Abs(st.Pos(o.C.Cy)) <= 4).ToList();
        if (body.Count > 0)
        {
            int bx0 = body.Min(o => o.C.MinX), bx1 = body.Max(o => o.C.MaxX), by1 = body.Max(o => o.C.MaxY);
            if (Eight(near, st, kind, bx0, bx1, by1, body.MaxBy(o => o.C.H).C, st.Pos(by1), s, joinedF: false)) kind += "_8";
        }
        // (a piece far wider than a clef, a slur or a scan's border running past it, is not its)
        foreach (var o in mine.Where(o => o.Class == "?" && o.C.MaxX > x0 && o.C.MinX < x1 && o.C.H > 0.5 * s && o.C.W <= 10 * s)) o.Class = "clef";
        if (rules != null) return (new Sig { St = st, X = rules.Value.Sig.X, Clef = kind }, rules.Value.EndX);
        return (new Sig { St = st, X = Math.Max(st.Left, x0), Clef = kind }, x1);
    }

    /// <summary>
    /// The clefs inside the staff, past its opening clef, key and time (<paramref name="after"/>):
    /// a change of clef within a measure or at a barline, as an old print's left hand changes between
    /// F and G and back within the bar (Kinkel Op.7-1, Brandt 1838). From the learnt detector: a clef
    /// it is sure of standing on a tall piece of ink of its own (a G clef's reaching above and below the
    /// staff, an F clef's over it), or one it half saw (<see cref="PageSymbols.WeakClefs"/>, 0.2 and up:
    /// the F clefs back from the treble, 0.2-0.5) on a clef's own shape: an F clef's body with its two
    /// dots stacked at its right about the F line, a G clef's body from two and a half spaces over the
    /// staff to two and a half under it, no wider than a clef. Marked <see cref="Sig.Mid"/>: the events
    /// after one read by it (Reader.StaffEvents), the staff's opening clef unchanged. The clef's pieces
    /// are named, and any head read on them dropped.
    /// </summary>
    static void MidClefs(PageSymbols ps, List<Other> mine, Staff st, double after, double s)
    {
        if (ps.Detections == null) return;
        bool IsClef(Detection t) => t.Class is "clef_g" or "clef_f" or "clef_c";
        var seen = ps.Detections.Where(t => IsClef(t) && t.Score >= Detector.Threshold).Select(t => (t, Sure: true))
            .Concat(ps.WeakClefs.Where(t => IsClef(t) && t.Score >= Detector.WeakClef).Select(t => (t, Sure: false)))
            // (past the staff's opening signatures, and past where DetectedClef looks for the opening clef: a
            // staff found to start too far left, at an instrument's name, has its opening clef well in)
            .Where(p => p.t.X > after + 3 * s && p.t.X > st.Left + 11 * s && p.t.X < st.Right - 0.5 * s && Math.Abs(p.t.Y - st.Mid) < 2 * s)
            .OrderBy(p => p.t.X).ToList();
        double lastX = double.NegativeInfinity;
        // a dot in the ink right of x at a staff position, whatever piece holds it: a fifth of a space of
        // columns and more in a row, each with ink there in a run no taller than a dot and about the place
        // (a wave's or a stem's column runs on)
        bool InkDot(int x, double pos)
        {
            if (ps.Labels == null) return false;
            int w = ps.LabelW, h = ps.Labels.Length / w, cy = (int)Math.Round(st.Mid - pos * s / 2), n = 0, best = 0;
            bool Ink(int xx, int yy) => yy >= 0 && yy < h && ps.Labels[yy * w + xx] != 0;
            for (int xx = Math.Max(0, x + 1); xx <= Math.Min(w - 1, x + (int)(1.0 * s)); xx++)
            {
                bool dot = false;
                for (int y = cy - (int)(0.15 * s); y <= cy + (int)(0.15 * s) && !dot; y++)
                {
                    if (!Ink(xx, y)) continue;
                    int a = y, b = y;
                    while (Ink(xx, a - 1) && cy - a < s) a--;
                    while (Ink(xx, b + 1) && b - cy < s) b++;
                    dot = b - a + 1 >= 0.2 * s && b - a + 1 <= 0.55 * s && Math.Abs((a + b) / 2.0 - cy) < 0.25 * s;
                }
                n = dot ? n + 1 : 0;
                best = Math.Max(best, n);
            }
            return best >= 0.2 * s;
        }
        // an upright line through the whole staff, in a column within a space right of x
        bool BarlineAt(int x)
        {
            if (ps.Labels == null) return false;
            int w = ps.LabelW, h = ps.Labels.Length / w, y0 = (int)(st.Top + 0.3 * s), y1 = (int)(st.Bottom - 0.3 * s);
            if (y0 < 0 || y1 >= h) return false;
            for (int xx = x + 1; xx <= Math.Min(w - 1, x + (int)s); xx++)
            {
                int y = y0;
                while (y <= y1 && ps.Labels[y * w + xx] != 0) y++;
                if (y > y1) return true;
            }
            return false;
        }
        // the columns from..to where a dot at a staff position starts: as InkDot, its run a fifth of a space of columns and more
        List<int> DotStarts(int from, int to, double pos, bool loose = false)
        {
            var starts = new List<int>();
            if (ps.Labels == null) return starts;
            int w = ps.LabelW, h = ps.Labels.Length / w, cy = (int)Math.Round(st.Mid - pos * s / 2), n = 0;
            bool Ink(int xx, int yy) => yy >= 0 && yy < h && ps.Labels[yy * w + xx] != 0;
            for (int xx = Math.Max(0, from); xx <= Math.Min(w - 1, to); xx++)
            {
                bool dot = false;
                for (int y = cy - (int)((loose ? 0.4 : 0.15) * s); y <= cy + (int)((loose ? 0.4 : 0.15) * s) && !dot; y++)
                {
                    if (!Ink(xx, y)) continue;
                    int a = y, b = y;
                    while (Ink(xx, a - 1) && cy - a < s) a--;
                    while (Ink(xx, b + 1) && b - cy < s) b++;
                    dot = b - a + 1 >= 0.2 * s && b - a + 1 <= (loose ? 0.8 : 0.55) * s && Math.Abs((a + b) / 2.0 - cy) < (loose ? 0.4 : 0.25) * s;
                }
                n = dot ? n + 1 : 0;
                if (n == (int)Math.Ceiling(0.2 * s)) starts.Add(xx - n + 1);
            }
            return starts;
        }

        // a half-seen clef's shape, from its extent (minX..maxX, minY..maxY): null when it is a clef's, else why not
        // (cut out of the ink about it, a G clef's thin top and tail may stay behind with the lines: lang36-3's
        // right hand back to the treble before the barline, pos 4.4..-4.4 -- a little less reach, and a clef's width)
        string Shape(string kind, int minX, int maxX, int minY, int maxY, bool cut = false)
        {
            double top = st.Pos(minY), bottom = st.Pos(maxY), w = (maxX - minX + 1) / s, h = (maxY - minY + 1) / s;
            double reach = cut ? 4.2 : 4.5;
            if (kind == "treble") return top > reach && bottom < -reach && w >= (cut ? 1.6 : 1.2) ? null : $"no G clef's shape (pos {top:F1}..{bottom:F1}, {w:F1} wide)";
            if (kind != "bass") return "a C clef half seen";
            // (its two dots: small round pieces right of the body, one in each space about the F line)
            var dots = mine.Where(o => (o.Class == "?" || o.Class == "dot") && SmallRound(o.C, s) && o.C.MinX > maxX - 0.3 * s && o.C.MinX < maxX + 1.2 * s).ToList();
            bool up = dots.Any(o => st.Pos(o.C.Cy) is > 2.3 and < 3.8), lo = dots.Any(o => st.Pos(o.C.Cy) is > 0.3 and < 1.8);
            // (or both seen in the ink, run into a piece beside them: kinkel7-1's arpeggio, its wave touching
            // the dots of the F clef printed small before the chord)
            if (!up && !lo && InkDot(maxX, 3) && InkDot(maxX, 1)) up = lo = true;
            // (or the two a space apart a little lower, each run into a line's remains: kinkel7-1's small F
            // clef before the arpeggio sits low, its dots at 2.1 and 0.3 -- detector35b saw it at 0.27)
            if (!up && !lo)
                foreach (var p in new[] { 3.0, 2.5, 2.0 })
                    if (DotStarts(maxX + 1, maxX + (int)(1.2 * s), p, loose: true).Count > 0 && DotStarts(maxX + 1, maxX + (int)(1.2 * s), p - 2, loose: true).Count > 0) { up = lo = true; break; }
            // (or, cut out of the ink, sunk into the barline it stands against: a line through the staff within
            // a space right of the body -- lang36-3's left hand back to the bass before its last system's barline)
            if (!up && !lo && cut && BarlineAt(maxX)) up = lo = true;
            // (one dot will do: the other sinks into the body or a staff line's remains on a scan -- kinkel7-1's at 0.39.
            // The body stands from the top line, or near it, down past the middle line: an old print's full
            // clef to the bottom line, a change of clef printed small, MuseScore's at about three quarters, to a
            // position under the middle)
            return top > 2.3 && top < 5.2 && bottom < -0.5 && bottom > -5 && w >= 1.1 && w <= 3.5 && h >= 2.0 && (up || lo) ? null
                : $"no F clef's shape (pos {top:F1}..{bottom:F1}, {w:F1} wide, {h:F1} tall, dots {(up ? "upper" : "-")} {(lo ? "lower" : "-")})";
        }
        // the ink about a detection, whatever piece holds it: from the ink nearest its centre, joined within a
        // clef's columns about it (an F clef's dots apart), the columns of a longer upright run than a clef's
        // left out (a barline, a stem), and none at or right of right (the dots found, run into the heads after); its extent, or null
        // (wide: the two columns either side of such a run left out too -- a scan's barline, its edge ragged,
        // joins a clef against it to the notes far below)
        (int, int, int, int)? Local(Detection t, string kind, int right = int.MaxValue, bool wide = false)
        {
            if (ps.Labels == null) return null;
            int w = ps.LabelW, h = ps.Labels.Length / w;
            int wx0 = Math.Max(0, (int)(t.X - 1.3 * s)), wx1 = Math.Min(Math.Min(w - 1, right - 1), (int)(t.X + 1.3 * s));
            int wy0 = Math.Max(0, (int)(st.Top - 3.5 * s)), wy1 = Math.Min(h - 1, (int)(st.Bottom + 3.5 * s));
            if (wx1 <= wx0 || wy1 <= wy0) return null;
            int ww = wx1 - wx0 + 1, wh = wy1 - wy0 + 1;
            double longest = (kind == "treble" ? 8.5 : 4.6) * s;
            var off = new bool[ww];
            for (int x = 0; x < ww; x++)
                for (int y = wy0, run = 0; y <= wy1 && !off[x]; y++)
                {
                    run = ps.Labels[y * w + wx0 + x] != 0 ? run + 1 : 0;
                    off[x] = run > longest;
                }
            if (wide)
            {
                var line = (bool[])off.Clone();
                for (int x = 0; x < ww; x++)
                    if (line[x])
                        for (int d = -2; d <= 2; d++)
                            if (x + d >= 0 && x + d < ww) off[x + d] = true;
            }
            // (and what is left of a staff line: ink on a line no taller than the line -- it joins the clef to
            // the window's sides, and the clef's own thin strokes along a line with it)
            bool Raw(int x, int y) => y >= 0 && y < h && ps.Labels[y * w + x] != 0;
            bool OnLine(int x, int y)
            {
                if (!st.Lines.Any(l => Math.Abs(l.Y - y) <= 0.25 * s)) return false;
                int a = y, b = y;
                while (Raw(x, a - 1) && y - a < s) a--;
                while (Raw(x, b + 1) && b - y < s) b++;
                return b - a + 1 <= 0.3 * s;
            }
            bool Ink(int x, int y) => !off[x - wx0] && ps.Labels[y * w + x] != 0 && !OnLine(x, y);
            // the pieces of that ink in the window
            var id = new int[ww * wh];
            var boxes = new List<(int X0, int X1, int Y0, int Y1)>();
            var todo = new Stack<(int, int)>();
            for (int y = wy0; y <= wy1; y++)
                for (int x = wx0; x <= wx1; x++)
                {
                    if (id[(y - wy0) * ww + x - wx0] != 0 || !Ink(x, y)) continue;
                    boxes.Add((x, x, y, y));
                    int n = boxes.Count;
                    id[(y - wy0) * ww + x - wx0] = n;
                    todo.Push((x, y));
                    while (todo.Count > 0)
                    {
                        var (px, py) = todo.Pop();
                        var b = boxes[n - 1];
                        boxes[n - 1] = (Math.Min(b.X0, px), Math.Max(b.X1, px), Math.Min(b.Y0, py), Math.Max(b.Y1, py));
                        for (int dy = -1; dy <= 1; dy++)
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                int nx = px + dx, ny = py + dy;
                                if (nx < wx0 || nx > wx1 || ny < wy0 || ny > wy1 || id[(ny - wy0) * ww + nx - wx0] != 0 || !Ink(nx, ny)) continue;
                                id[(ny - wy0) * ww + nx - wx0] = n;
                                todo.Push((nx, ny));
                            }
                    }
                }
            // the piece nearest the centre, and the pieces a line's width or so from it, in turn (the clef's
            // strokes the lines cut apart, an F clef's ball; a dot right of it is not of its body)
            int seed = 0;
            double near = 0.6 * s;
            for (int y = Math.Max(wy0, (int)(t.Y - near)); y <= Math.Min(wy1, (int)(t.Y + near)); y++)
                for (int x = Math.Max(wx0, (int)(t.X - near)); x <= Math.Min(wx1, (int)(t.X + near)); x++)
                    if (id[(y - wy0) * ww + x - wx0] is var k && k != 0 && Math.Sqrt((x - t.X) * (x - t.X) + (y - t.Y) * (y - t.Y)) is var d && d < near) { near = d; seed = k; }
            if (seed == 0) return null;
            var (x0, x1, y0, y1) = boxes[seed - 1];
            var taken = new bool[boxes.Count];
            taken[seed - 1] = true;
            double gap = 0.25 * s;
            for (bool more = true; more;)
            {
                more = false;
                for (int i = 0; i < boxes.Count; i++)
                {
                    var b = boxes[i];
                    if (taken[i] || b.X1 - b.X0 < 0.75 * s && b.Y1 - b.Y0 < 0.75 * s && b.X0 > x1 - 0.2 * s) continue;
                    if (b.X0 > x1 + gap || b.X1 < x0 - gap || b.Y0 > y1 + gap || b.Y1 < y0 - gap) continue;
                    taken[i] = more = true;
                    (x0, x1, y0, y1) = (Math.Min(x0, b.X0), Math.Max(x1, b.X1), Math.Min(y0, b.Y0), Math.Max(y1, b.Y1));
                }
            }
            return (x0, x1, y0, y1);
        }
        foreach (var (t, sure) in seen)
        {
            if (t.X - lastX < 2 * s) continue;   // (one clef, seen twice)
            string kind = t.Class switch { "clef_g" => "treble", "clef_f" => "bass", _ => st.Pos(t.Y) > 1 ? "tenor" : "alto" };
            // (a half-seen clef where the detector is sure of something else -- a rest, a sign, a head --
            // is that other thing: a dotted quarter rest has an F clef's height and a dot in its lower space)
            var other = sure ? null : ps.Detections.Where(d => !IsClef(d) && d.Score >= Detector.Threshold && Math.Abs(d.X - t.X) < 0.7 * s && Math.Abs(d.Y - t.Y) < 2.5 * s).OrderByDescending(d => d.Score).FirstOrDefault();
            if (other != null)
            {
                if (Verbose) Console.WriteLine($"    clef {kind} inside staff {st.System + 1}/{st.Part} at x {t.X:F0} ({t.Score:F2}, weak): the detector is sure of a {other.Class} there ({other.Score:F2}), no clef");
                continue;
            }
            // the body: a tall piece under the detection, no wider than a clef
            // (a sure one's may have gathered the notes beside it, or the staff's lines where a bowed
            // page kept them in the ink -- kinkel7-1's second page, a piece 50 spaces wide: any width,
            // any height; a half-seen one's must be a clef's own piece)
            var body = mine.Where(o => (o.Class == "?" || sure && o.Class != "clef") && o.C.MinX < t.X + 0.6 * s && o.C.MaxX > t.X - 0.6 * s && (sure || o.C.W <= 4 * s && o.C.H <= 12 * s)
                                       && o.C.H >= (kind == "treble" ? 5.5 : 2.9) * s).OrderByDescending(o => o.C.H).FirstOrDefault();
            // the clef's extent: its body's, or its pieces' together
            int bx0 = 0, bx1 = 0, by0 = 0, by1 = 0;
            List<Other> pieces = null;
            string why = null;
            if (body != null)
            {
                (bx0, bx1, by0, by1) = (body.C.MinX, body.C.MaxX, body.C.MinY, body.C.MaxY);
                if (!sure) why = Shape(kind, bx0, bx1, by0, by1);
            }
            if (!sure && (body == null || why != null))
            {
                // (a change of clef is printed smaller than the staff's opening clef -- MuseScore's at about three
                // quarters -- and the staff lines taken out cut its body into pieces none of which is a clef's height:
                // lang36-3's F clefs back from the treble, under each a piece a space wide and two tall. The pieces
                // about the detection, centred within a space of it and no wider than a clef, are taken together)
                pieces = mine.Where(o => (o.Class == "?" || o.Class == "dot") && !SmallRound(o.C, s) && Math.Abs(o.C.Cx - t.X) < 1.0 * s
                                         && o.C.MinY < st.Bottom + 2.5 * s && o.C.MaxY > st.Top - 2.5 * s && o.C.W <= 3.5 * s && o.C.H <= 8 * s).ToList();
                string together = null;
                if (pieces.Count >= 2 && pieces.Max(o => o.C.H) >= 1.2 * s)
                {
                    int ux0 = pieces.Min(o => o.C.MinX), ux1 = pieces.Max(o => o.C.MaxX), uy0 = pieces.Min(o => o.C.MinY), uy1 = pieces.Max(o => o.C.MaxY);
                    together = Shape(kind, ux0, ux1, uy0, uy1);
                    if (together == null) { (bx0, bx1, by0, by1) = (ux0, ux1, uy0, uy1); why = null; }
                }
                else together = pieces.Count < 2 ? "one piece" : "no piece over a space tall";
                if (together != null && Local(t, kind) is var (lx0, lx1, ly0, ly1))
                {
                    // (or its ink cut out about the detection: a scan's clef printed small before the barline
                    // runs, by what is left of the staff lines, into the barline and the chords beside it --
                    // lang36-3's left hand, changing between F and G clef in each of its first bars)
                    string local = Shape(kind, lx0, lx1, ly0, ly1, cut: true);
                    if (local == null) { (bx0, bx1, by0, by1) = (lx0, lx1, ly0, ly1); why = null; together = null; pieces = new List<Other>(); }
                    else together += $"; its ink about it x {lx0}-{lx1} y {ly0}-{ly1}: {local}";
                }
                if (together != null)
                {
                    pieces = null;
                    why = (why ?? "no body") + $"; its pieces together: {together}"
                        + (Verbose ? $" (pieces there: {string.Join(" ", ps.Others.Where(o => o.C.MinX < t.X + 1.0 * s && o.C.MaxX > t.X - 1.0 * s && o.C.H > 0.3 * s).Select(o => $"{o.Class} of {o.St?.System + 1}/{o.St?.Part} x {o.C.MinX}-{o.C.MaxX} y {o.C.MinY}-{o.C.MaxY} {o.C.W / s:F1}x{o.C.H / s:F1}"))}; heads there: {string.Join(" ", ps.Heads.Where(h => Math.Abs(h.X - t.X) < 1.5 * s && Math.Abs(h.Y - t.Y) < 4 * s).Select(h => $"{h.X:F0},{h.Y:F0}"))})" : "");
                }
            }
            if (Verbose) Console.WriteLine($"    clef {kind} inside staff {st.System + 1}/{st.Part} at x {t.X:F0} ({t.Score:F2}{(sure ? "" : ", weak")}): {(why ?? $"taken, the body x {bx0}-{bx1} h {(by1 - by0 + 1) / s:F2}{(pieces != null ? $" ({pieces.Count} pieces together)" : "")}")}");
            if (why != null) continue;
            lastX = t.X;
            // (the clef's own columns: about the detection, within the body -- the body may have gathered the notes beside it)
            double x0 = Math.Max(bx0, t.X - 1.6 * s) - 0.2 * s, x1 = Math.Min(bx1, t.X + 1.6 * s) + (kind == "bass" ? 1.2 * s : 0.3 * s);
            // (its pieces of ink, so that nothing reads them as rests, signs or dots; and no head stands on its body)
            foreach (var o in mine.Where(o => (o.Class == "?" || o.Class == "dot") && o.C.MaxX > x0 && o.C.MinX < x1 && o.C.MinY < st.Bottom + 2 * s && o.C.MaxY > st.Top - 2 * s && o.C.W <= 10 * s)) o.Class = "clef";
            ps.Heads.RemoveAll(h => h.St == st && Math.Abs(h.X - t.X) < 1.2 * s && h.Y > Math.Max(by0, st.Top - 3.5 * s) && h.Y < Math.Min(by1, st.Bottom + 3.5 * s));
            ps.Sigs.Add(new Sig { St = st, X = x0, Clef = kind, Mid = true });
        }
        // an F clef the detector did not see, after a G clef it did inside the staff: the change back, printed
        // small (lang36-3's left hand, its G clef for a chord's top and the F clef a 16th after, on every page; the
        // detector sees the one and not the other, and the notes after it were read in the G clef). Its two dots
        // stacked about the F line, a space apart, with an F clef's body cut out of the ink just left of them
        // (after the staff's opening G clef too, an old print's F clef full size against a barline, its dots
        // past it: kinkel19-6's right hand from its tenth bar. No G clef inside the staff vouches for a change
        // there, so the clef must show its own shape: a dot a piece of its own, small and round, nothing the
        // detector is sure of on its body or its dots, and no barline it sees in its body)
        string OwnShape(int dx, int bx0, int bx1, int by0, int by1)
        {
            var dots = mine.Where(o => (o.Class == "?" || o.Class == "dot") && SmallRound(o.C, s) && o.C.MinX > dx - 0.4 * s && o.C.MinX < dx + 0.6 * s).ToList();
            // (one of them will do: the other may run into the chord after it -- kinkel19-6's lower dot, into a head)
            if (!dots.Any(o => st.Pos(o.C.Cy) is > 2.3 and < 3.8 or > 0.3 and < 1.8)) return "neither dot a piece of its own";
            var sure = ps.Detections.FirstOrDefault(d => !IsClef(d) && d.Class != "barline" && d.Score >= Detector.Threshold
                                                         && d.X > bx0 - 0.2 * s && d.X < dx + 0.8 * s && d.Y > by0 - 0.2 * s && d.Y < by1 + 0.2 * s);
            if (sure != null) return $"the detector is sure of a {sure.Class} there ({sure.Score:F2})";
            // (the barline it stands against is right of its body, not in it: kinkel7-1's |:, its thick line
            // taken for the body and the repeat's dots for the clef's)
            var bar = ps.Detections.FirstOrDefault(d => d.Class == "barline" && d.Score >= Detector.Threshold && d.X >= bx0 && d.X <= bx1 && d.Y > by0 && d.Y < by1);
            return bar == null ? null : $"a barline in its body ({bar.Score:F2})";
        }
        foreach (var g in ps.Sigs.Where(g => g.St == st && g.Clef == "treble").OrderBy(g => g.X).ToList())
        {
            double end = ps.Sigs.Where(o => o.St == st && o.Mid && o.X > g.X + s).Select(o => o.X).DefaultIfEmpty(st.Right).Min();
            if (ps.Sigs.Any(o => o.St == st && o.Mid && o.Clef == "bass" && o.X > g.X && o.X <= end)) continue;
            // (the dots in the ink: on a scan they run into the heads and lines beside them. One of them may touch a
            // line or a head, a dot's height up to 0.8 of a space for it; and a small clef's stand closer about the
            // F line: lang36-3's at 3.3 and 1.8)
            int from = (int)(g.Mid ? g.X + 2 * s : Math.Max(g.X + 2 * s, after + 2 * s));
            var ups = DotStarts(from, (int)end, 3);
            var los = DotStarts(from, (int)end, 1);
            var found = ups.SelectMany(u => DotStarts(from, (int)end, 1.25, loose: true).Where(l => Math.Abs(l - u) <= 0.4 * s).Select(l => Math.Min(u, l)))
                .Concat(los.SelectMany(l => DotStarts(from, (int)end, 2.75, loose: true).Where(u => Math.Abs(l - u) <= 0.4 * s).Select(u => Math.Min(u, l))))
                .Distinct().OrderBy(x => x);
            foreach (int dx in found)
            {
                var t = new Detection { Class = "clef_f", X = dx - 1.0 * s, Y = st.Mid - 2.5 * s / 2, Score = 0 };
                if (Local(t, "bass", dx, wide: !g.Mid) is not var (bx0, bx1, by0, by1)) { if (Verbose) Console.WriteLine($"    clef bass inside staff {st.System + 1}/{st.Part} after the treble at x {g.X:F0}, from its dots at x {dx}: no ink for a body"); continue; }
                string why = bx1 < dx - 0.8 * s ? "the body not just left of the dots" : Shape("bass", bx0, bx1, by0, by1, cut: true);
                if (why == null && !g.Mid) why = OwnShape(dx, bx0, bx1, by0, by1);
                if (Verbose) Console.WriteLine($"    clef bass inside staff {st.System + 1}/{st.Part} after the treble at x {g.X:F0}, from its dots at x {dx}: {why ?? $"taken, the body x {bx0}-{bx1} h {(by1 - by0 + 1) / s:F2}"}");
                if (why != null) continue;
                double x0 = bx0 - 0.2 * s, x1 = dx + 1.0 * s;
                foreach (var o in mine.Where(o => (o.Class == "?" || o.Class == "dot") && o.C.MaxX > x0 && o.C.MinX < x1 && o.C.MinY < st.Bottom + 2 * s && o.C.MaxY > st.Top - 2 * s && o.C.W <= 10 * s)) o.Class = "clef";
                ps.Heads.RemoveAll(h => h.St == st && h.X > x0 && h.X < x1 && h.Y > by0 - 0.3 * s && h.Y < by1 + 0.3 * s);
                ps.Sigs.Add(new Sig { St = st, X = x0, Clef = "bass", Mid = true });
                break;
            }
        }
    }

    /// <summary>
    /// Whether a treble or bass clef, its body's ink at minX..maxX down to maxY (top and bottom as
    /// staff positions), carries a small "8" under it: a piece of its own just below, or joined to
    /// the body. An F clef with the 8 attached reaches below the bottom line (a plain one's tail
    /// stops short of it); a G clef with it reaches further below the staff than above it (a plain
    /// one's tail goes at most 1.6 positions further down than its head goes up, in MuseScore's
    /// eight fonts; with Bravura's, Leland's, Gonville's or the Finale fonts' 8 joined, 2.3 and more).
    /// The G clef's reach is its tallest piece's, whole from above the staff: a broken clef on a
    /// scan gathers bits of the brace and the notes below it (Leland's band005 degraded).
    /// </summary>
    static bool Eight(List<Other> near, Staff st, string clef, int minX, int maxX, int maxY, Comp main, double bottom, double s, bool joinedF = true)
    {
        if (clef != "treble" && clef != "bass") return false;
        if (near.Any(o => o.Class == "?" && (o.St == st || o.C.MaxY < o.St.Top - 0.2 * s) && o.C.MinY > maxY - 0.3 * s && o.C.MinY < maxY + 2.5 * s
                          && o.C.Cx > minX && o.C.Cx < maxX && o.C.H < 1.4 * s && o.C.H > 0.5 * s
                          && o.C.W > 0.4 * s)) return true;   // (not a bit of the system's start line broken on a scan)
        // (an old print's F clef tail reaches the bottom line, -4.0, with no 8: Kinkel Op.7-1)
        if (clef == "bass") return joinedF && bottom < -4.6;
        double mTop = st.Pos(main.MinY), mBottom = st.Pos(main.MaxY);
        // (a piece reaching six spaces under the middle line is more than a clef with its 8: the
        // opening line of an old print's lone voice staff, run down to the lyrics -- Kinkel Op.7-1)
        return mTop > 5.5 && -mBottom - mTop > 2.0 && mBottom > -12;
    }

    static (Sig Sig, double EndX)? ReadClef(List<Other> mine, List<Other> near, Img ink, Staff st, double s)
    {
        // (an F clef's dots stand further in on an old print, the body set in from the staff's start
        // and wide: 3.5 spaces in on Kinkel Op.7-1, read alto without them)
        var zone = mine.Where(o => o.Class == "?" && o.C.MinX >= st.Left - 0.5 * s && o.C.MinX < st.Left + 3.2 * s
                                   && o.C.H < 9 * s && o.C.MaxY >= st.Top - 0.2 * s && o.C.MinY <= st.Bottom + 0.2 * s).ToList();
        // (the system's opening line, where the staff starts at it and the lines are lost about it: a
        // thin upright piece no further in than half a space, running out of the staff -- kinkel7-6's
        // scan, its last system's F clef set against the line, the line's piece five spaces tall the
        // tallest and read as a G clef's body)
        zone.RemoveAll(o => o.C.W <= 0.35 * s && o.C.MaxX <= st.Left + 0.5 * s
                            && (o.C.MinY < st.Top - 0.5 * s || o.C.MaxY > st.Bottom + 0.5 * s));
        // the clef's body is the tallest piece; with it go the pieces that start before it ends
        // (a C clef's bars) and, for an F clef, its two dots just after it
        var main = zone.OrderByDescending(o => o.C.H).FirstOrDefault();
        // (an F clef's: beside a body of its height, in the spaces about the F line, up to 4.5 spaces in;
        // not a G clef's, whose end would then reach the key's first sign and lose it. The lower dot of
        // kinkel7-1's scan stands at 0.3, its staff bowed)
        if (main != null && main.C.H < 5 * s)
            zone.AddRange(mine.Where(o => o.Class == "?" && !zone.Contains(o) && SmallRound(o.C, s) && o.C.MinX >= st.Left + 3.2 * s && o.C.MinX < st.Left + 4.5 * s
                                          && o.C.MinX > main.C.MaxX - 0.3 * s && o.C.MinX < main.C.MaxX + 1.0 * s && st.Pos(o.C.Cy) is > 0.0 and < 3.8));
        if (Verbose)
            foreach (var o in mine.Where(o => o.C.MinX < st.Left + 4 * s))
                Console.WriteLine($"    clef? staff {st.System + 1}/{st.Part} left {st.Left}: {o.Class} x {o.C.MinX}-{o.C.MaxX} y {o.C.MinY}-{o.C.MaxY} h {o.C.H / s:F2}");
        // a percussion clef: two upright bars side by side, a thick and a thin one, from the
        // second line to the fourth (nothing else at a staff's start is two spaces tall and thin);
        // MuseJazz's are two thick ones over three spaces tall, as a C clef's two bars, but with
        // no curves after them
        var bars = zone.Where(o => o.C.H >= 1.6 * s && o.C.H <= 4.2 * s && o.C.W >= 0.1 * s && o.C.W <= 0.7 * s && Math.Abs(st.Pos(o.C.Cy)) <= 1)
                       .OrderBy(o => o.C.MinX).ToList();
        if (bars.Count == 2 && bars[1].C.MinX - bars[0].C.MaxX <= 0.8 * s && Math.Abs(bars[0].C.Cy - bars[1].C.Cy) <= 0.3 * s
            && (main.C.H < 2.9 * s || !zone.Any(o => !bars.Contains(o) && o.C.H > 0.5 * s && o.C.MinX < bars[1].C.MaxX + 1.0 * s)))
        {
            foreach (var o in bars) o.Class = "clef";
            return (new Sig { St = st, X = bars[0].C.MinX, Clef = "percussion" }, bars[1].C.MaxX);
        }
        // an F clef whose body is lost -- an old print sets it against the system's opening line,
        // and the two come as one tall piece, the key's first flats then the tallest piece at the
        // staff's start (kinkel19-2's Trautwein print, 1838) -- is told by its two dots alone: two
        // small round pieces stacked about the F line at the staff's start, a space apart, or the
        // two run together into one piece a space and a half tall, with ink of the body's height
        // left of them and no tall piece of their own left of them. Nothing else stands there so
        {
            // (the dots of an old print are large and blurred: up to 0.85 of a space)
            static bool Dotty(Comp c, double s) => c.W >= 0.2 * s && c.W <= 0.85 * s && c.H >= 0.2 * s && c.H <= 0.85 * s && c.Fill >= 0.5;
            var pieces = mine.Where(o => (o.Class == "?" || o.Class == "dot") && o.C.MinX >= st.Left - 0.5 * s && o.C.MinX < st.Left + 3.5 * s).ToList();
            var pairs = new List<(int MinX, int MaxX, double Cx, double Cy, Other Up, Other Lo)>();
            foreach (var up in pieces.Where(o => Dotty(o.C, s) && st.Pos(o.C.Cy) is > 2.3 and < 3.8))
                foreach (var lo in pieces.Where(o => Dotty(o.C, s) && st.Pos(o.C.Cy) is > 0.3 and < 1.8 && Math.Abs(o.C.Cx - up.C.Cx) <= 0.4 * s))
                    pairs.Add((Math.Min(up.C.MinX, lo.C.MinX), Math.Max(up.C.MaxX, lo.C.MaxX), (up.C.Cx + lo.C.Cx) / 2, (up.C.Cy + lo.C.Cy) / 2, up, lo));
            foreach (var o in pieces.Where(o => o.C.W >= 0.25 * s && o.C.W <= 0.85 * s && o.C.H >= 1.0 * s && o.C.H <= 1.7 * s
                                               && st.Pos(o.C.MinY) is > 2.4 and < 4.0 && st.Pos(o.C.MaxY) is > 0.0 and < 1.6))
                pairs.Add((o.C.MinX, o.C.MaxX, o.C.Cx, o.C.Cy, o, null));
            foreach (var p in pairs.OrderBy(p => p.MinX))
            {
                // (a body of its own left of the dots is read below, as any clef; and a piece
                // within a tall piece's box is a bit of a clef's body come apart on a scan, no dot
                // -- scan-piano002's F clef, the top of its curl read for the dots run together)
                if (zone.Any(o => o.C.H >= 2.9 * s && o.C.MaxX <= p.MinX + 0.3 * s)) break;
                if (zone.Any(o => o.C.H >= 2.5 * s && Overlap(o.C) >= 0.7)) continue;
                double Overlap(Comp c)
                {
                    int ox = Math.Min(c.MaxX, p.MaxX) - Math.Max(c.MinX, p.MinX) + 1, oy = Math.Min(c.MaxY, p.Up.C.MaxY) - Math.Max(c.MinY, p.Up.C.MinY) + 1;
                    return ox <= 0 || oy <= 0 ? 0 : (double)ox * oy / ((p.MaxX - p.MinX + 1) * (p.Up.C.MaxY - p.Up.C.MinY + 1));
                }
                int x0 = (int)Math.Max(0, p.MinX - 2.2 * s), x1 = p.MinX - (int)(0.2 * s);
                int rows = 0;
                for (int y = (int)(st.Top - 0.5 * s); y <= (int)(st.Bottom + 1.5 * s); y++)
                {
                    bool any = false;
                    for (int x = x0; x <= x1 && !any; x++) any = ink[x, y];
                    if (any) rows++;
                }
                if (rows < 3.0 * s) continue;
                // (a tall piece over or just right of the dots is the clef's body after all, its
                // curl come apart at the top -- MuseScore's F clef on dl-ms-12, the curl's head
                // taken for the dots run together and the clef then placed before the staff's
                // start, unread, the left hand treble: the clef runs to that body's end)
                var body = zone.Where(o => o.C.H >= 2.5 * s && o.C.MinX <= p.MaxX + 1.0 * s && o.C.MaxX >= p.MinX).ToList();
                int endX = Math.Max(p.MaxX, body.Count > 0 ? body.Max(o => o.C.MaxX) : 0);
                if (Verbose) Console.WriteLine($"    clef bass staff {st.System + 1}/{st.Part}: from its two dots at x {p.MinX}-{p.MaxX}{(p.Lo == null ? " (run together)" : "")}, the body {(body.Count > 0 ? $"a piece to x {endX}" : "lost in the system's line")}");
                p.Up.Class = "clef";
                if (p.Lo != null) p.Lo.Class = "clef";
                foreach (var o in zone.Where(o => o.C.MaxX <= p.MinX + 0.3 * s).Concat(body)) o.Class = "clef";
                return (new Sig { St = st, X = Math.Max(st.Left, x0), Clef = "bass" }, endX);
            }
        }
        if (main == null || main.C.H < 2.9 * s) return null;
        // (a C clef can come as two tall pieces a hair apart: its thick bar, then thin bar and curves)
        int end = main.C.MaxX;
        // (the second piece starts with a thin bar the staff's full height; a time signature
        // set close after the clef does not)
        bool BarAtLeft(Comp c)
        {
            for (int x = c.MinX; x <= c.MinX + (int)(0.3 * s); x++)
            {
                int run = 0, best = 0;
                for (int y = c.MinY; y <= c.MaxY; y++) { if (ink[x, y]) { run++; best = Math.Max(best, run); } else run = 0; }
                if (best >= 0.9 * c.H) return true;
            }
            return false;
        }
        foreach (var o in zone.Where(o => o.C.H >= 3 * s).OrderBy(o => o.C.MinX))
            if (o != main && o.C.MinX <= end + 0.25 * s && BarAtLeft(o.C)) end = Math.Max(end, o.C.MaxX);
        zone = zone.Where(o => o.C.MinX <= end + 0.1 * s
                               || (SmallRound(o.C, s) && o.C.MinX < end + 1.0 * s)).ToList();
        int minX = zone.Min(o => o.C.MinX), maxX = zone.Max(o => o.C.MaxX);
        int minY = zone.Min(o => o.C.MinY), maxY = zone.Max(o => o.C.MaxY);
        double top = st.Pos(minY), bottom = st.Pos(maxY);
        int dots = zone.Count(o => SmallRound(o.C, s));
        string clef;
        if (top > 5.5 && bottom < -5.5) clef = "treble";
        else if (dots >= 2) clef = "bass";
        else clef = (top + bottom) / 2 > 1 ? "tenor" : "alto";
        if (Eight(near, st, clef, minX, maxX, maxY, main.C, bottom, s)) clef += "_8";
        if (Verbose)
            Console.WriteLine($"    clef {clef} staff {st.System + 1}/{st.Part}: the zone x {minX}-{maxX} pos {top:F1}..{bottom:F1}, {dots} dots, the body x {main.C.MinX}-{main.C.MaxX} h {main.C.H / s:F2}");
        if (Verbose)
            foreach (var o in mine.Where(o => o.C.MinX < maxX + 0.5 * s && o.C.MaxX > minX - 0.5 * s))
                Console.WriteLine($"    clef {clef} staff {st.System + 1}/{st.Part}: piece {o.Class} x {o.C.MinX}-{o.C.MaxX} y {o.C.MinY}-{o.C.MaxY} pos {st.Pos(o.C.MinY):F1}..{st.Pos(o.C.MaxY):F1}");
        foreach (var o in zone) o.Class = "clef";
        return (new Sig { St = st, X = minX, Clef = clef }, maxX);
    }

    static (Sig Sig, double EndX)? ReadKey(PageSymbols ps, List<Other> mine, Img ink, Staff st, double startX, double s,
                                           string clef = null, bool strict = false)
    {
        // two signs packed side by side touch at a low resolution and come as one piece: cut it at
        // its waist, each half's box taken from its own ink (a double flat has no waist)
        var halves = new List<Other>();
        // (only where a key signature stands: on the staff, not a bracket or a beam group beyond it)
        foreach (var o in mine.Where(o => o.Class == "?" && o.C.MinX > startX && o.C.MinX < startX + 12 * s
                                          && o.C.W >= 1.6 * s && o.C.W <= 2.4 * s && o.C.H >= 2.1 * s && o.C.H <= 5.5 * s
                                          && Math.Abs(st.Pos(o.C.Cy)) <= 6))
        {
            var c = o.C;
            int cut = -1, least = int.MaxValue;
            for (int x = c.MinX + (int)(0.35 * c.W); x <= c.MinX + (int)(0.65 * c.W); x++)
            {
                int n = 0;
                for (int y = c.MinY; y <= c.MaxY; y++) if (ink[x, y]) n++;
                if (n < least) { least = n; cut = x; }
            }
            // (at the waist between two sharps one's arms may reach the other's stem: less than a
            // sign's height of ink there; a double flat's second stem fills the column)
            // (and enlarged from a small page, the strokes run thicker: a space of ink there)
            if (least > 1.0 * s) continue;
            var l = InkBox(ink, c.MinX, c.MinY, cut - 1, c.MaxY);
            var r = InkBox(ink, cut + 1, c.MinY, c.MaxX, c.MaxY);
            if (l == null || r == null) continue;
            // the other sign's arm tips reach over the waist, and a packed neighbour's into the box's
            // edge: a half's height comes from its ink clear of both (a sharp's), unless that leaves
            // less than a sign (a flat's stem stands at its edge)
            int m = (int)(0.25 * s);
            var lc = InkBox(ink, c.MinX + m, c.MinY, cut - 1 - m, c.MaxY);
            var rc = InkBox(ink, cut + 1 + m, c.MinY, c.MaxX - m, c.MaxY);
            if (lc != null && lc.H >= 2.1 * s) { l.MinY = lc.MinY; l.MaxY = lc.MaxY; }
            if (rc != null && rc.H >= 2.1 * s) { r.MinY = rc.MinY; r.MaxY = rc.MaxY; }
            if (Verbose) Console.WriteLine($"    key split x {c.MinX}-{c.MaxX} at {cut} ({least / s:F2}): {l.MinX}-{l.MaxX} h {l.H / s:F2}, {r.MinX}-{r.MaxX} h {r.H / s:F2}");
            foreach (var half in new[] { l, r })
            {
                var piece = new Other { C = half, St = st };
                splitFrom[piece] = o;
                halves.Add(piece);
            }
        }
        // a staff line can cut a flat in two (stem and bowl): join pieces that share their x range
        var pieces = mine.Concat(halves).Where(o => o.Class == "?" && o.C.MinX > startX && o.C.MinX < startX + 12 * s && o.C.H < 3.5 * s)
                         .OrderBy(o => o.C.MinX).ToList();
        var cand = new List<Other>();
        var used = new HashSet<Other>();
        foreach (var o in pieces)
        {
            if (used.Contains(o)) continue;
            var box = new Comp { MinX = o.C.MinX, MaxX = o.C.MaxX, MinY = o.C.MinY, MaxY = o.C.MaxY, N = o.C.N };
            var members = new List<Other> { o };
            foreach (var q in pieces)
            {
                if (q == o || used.Contains(q)) continue;
                int ov = Math.Min(box.MaxX, q.C.MaxX) - Math.Max(box.MinX, q.C.MinX);
                int gapY = Math.Max(q.C.MinY - box.MaxY, box.MinY - q.C.MaxY);
                // pieces stacked (a flat cut across) or side by side touching (stem and bowl)
                bool stacked = ov > 0.5 * Math.Min(box.W, q.C.W) && gapY < 0.3 * s;
                bool touching = ov > -0.1 * s && gapY < 0 && Math.Min(box.MaxY, q.C.MaxY) - Math.Max(box.MinY, q.C.MinY) > 0.5 * s;
                // ... but never two whole signs: packed key signatures touch too
                int mergedW = Math.Max(box.MaxX, q.C.MaxX) - Math.Min(box.MinX, q.C.MinX) + 1;
                if ((stacked || touching) && mergedW <= 1.05 * s)
                {
                    box.MinX = Math.Min(box.MinX, q.C.MinX); box.MaxX = Math.Max(box.MaxX, q.C.MaxX);
                    box.MinY = Math.Min(box.MinY, q.C.MinY); box.MaxY = Math.Max(box.MaxY, q.C.MaxY);
                    box.N += q.C.N;
                    members.Add(q);
                }
            }
            // (a common-time C, about two spaces tall, is shorter than any sharp, flat or natural)
            // (a common-time C sits centred on the middle line and is barely two spaces tall)
            bool cLike = box.H < 2.4 * s && Math.Abs(st.Pos(box.MinY) + st.Pos(box.MaxY)) < 0.6;
            if (!cLike && box.H >= 2.1 * s && box.H <= 3.4 * s && box.W >= 0.4 * s && box.W <= 1.45 * s)
            {
                foreach (var q in members) used.Add(q);
                var joined = new Other { C = box, St = st };
                joinedFrom[joined] = members;
                cand.Add(joined);
            }
        }
        var group = new List<Other>();
        if (Verbose)
            foreach (var o in mine.Where(o => o.C.MinX > startX && o.C.MinX < startX + 4 * s).OrderBy(o => o.C.MinX))
                Console.WriteLine($"    key? staff {st.System + 1}/{st.Part} from {startX:F0}: {o.Class} x {o.C.MinX}-{o.C.MaxX} w {o.C.W / s:F2} h {o.C.H / s:F2} pos {st.Pos(o.C.Cy):F1}");
        foreach (var o in cand)
        {
            if (group.Count == 0 && o.C.MinX > startX + 1.6 * s) break;
            if (group.Count > 0 && o.C.MinX > group[^1].C.MaxX + 0.9 * s) break;
            group.Add(o);
        }
        if (group.Count == 0) return null;
        // an accidental hugging a head belongs to that note
        double end = group[^1].C.MaxX;
        if (ps.Heads.Any(h => h.St == st && h.L - end > -0.2 * s && h.L - end < 0.6 * s)) return null;
        int flats = 0, sharps = 0;
        foreach (var o in group)
        {
            var c = o.C;
            if (c.W < 0.76 * s) { o.Class = "key-natural"; continue; }
            // count inside the box's middle, clear of a packed neighbour's edge
            int upper = 0, lower = 0, midY = (c.MinY + c.MaxY) / 2, inset = (int)(0.12 * c.W);
            for (int y = c.MinY; y <= c.MaxY; y++)
                for (int x = c.MinX + inset; x <= c.MaxX - inset; x++)
                    if (ink[x, y]) { if (y < midY) upper++; else lower++; }
            if (lower > 1.5 * upper) { flats++; o.Class = "key-flat"; }
            else { sharps++; o.Class = "key-sharp"; }
        }
        // after a barline, accept only a true key signature: its sharps or flats in the fixed
        // order and places (F C G D ... / B E A D ...), not accidentals that happen to lead a bar
        if (strict)
        {
            if (clef == null) return null;
            var signs = group.Where(o => o.Class != "key-natural").ToList();
            if (signs.Count == 0 && group.Count < 2) return null;
            // the learnt classifier knows the shapes: a sign it is sure is something else (a
            // quarter rest after a barline stands just where an F sharp would) is no key signature
            if (GlyphKnn.Enabled)
                foreach (var o in signs)
                {
                    var members = joinedFrom[o];
                    if (members.Count != 1) continue;
                    // (a half cut from a piece is no component of its own: its features come from its box)
                    var f = splitFrom.ContainsKey(members[0]) ? GlyphKnn.BoxFeatures(ink, members[0].C, s) : GlyphKnn.Features(ps, members[0].C, s);
                    var (label, dist) = GlyphKnn.Classify(f);
                    if (dist <= 3.0 && !label.StartsWith("accidentals.")) return null;
                }
            int[] order = sharps > 0 ? new[] { 3, 0, 4, 1, 5, 2, 6 } : new[] { 6, 2, 5, 1, 4, 0, 3 };
            if (sharps > 0 && flats > 0) return null;
            for (int i = 0; i < signs.Count; i++)
            {
                var c = signs[i].C;
                double y = signs[i].Class == "key-flat" ? c.MaxY - 0.5 * s : (c.MinY + c.MaxY) / 2.0;
                int dia = Clefs.MiddleLine(clef) + (int)Math.Round(st.Pos(y));
                if (i >= order.Length || Pitch.Mod7(dia) != order[i]) return null;
            }
        }
        foreach (var o in group)
            foreach (var q in joinedFrom[o])
            {
                q.Class = o.Class;
                if (splitFrom.TryGetValue(q, out var whole)) whole.Class = o.Class;
            }
        // (a key has seven signs at most: more is a degraded page's specks run together)
        if (Math.Max(sharps, flats) > 7) return null;
        int fifths = sharps > 0 ? sharps : -flats;
        if (Verbose) Console.WriteLine($"    key {fifths} by the rules{(strict ? " after a barline" : "")}, staff {st.System + 1}/{st.Part}: {group.Count} signs at x {group[0].C.MinX}-{end:F0}");
        return (new Sig { St = st, X = group[0].C.MinX, Fifths = fifths }, end);
    }

    /// <summary>
    /// The time signature at startX, if any: read by the learnt classifier when it knows the shape,
    /// else by the hand-written rules. A sign taken claims its ink: a digit's loop read as a
    /// whole note, or a cut C read as a rest, goes.
    /// </summary>
    static Sig Time(PageSymbols ps, List<Other> mine, Img ink, List<Comp> holes, Staff st, double startX, double s, bool afterBar = false)
    {
        var rules = TimeByRules(ps, mine, ink, holes, st, startX, s, afterBar);
        // (no piece is in fifths or ninths: the rules read a jazz font's rest or flat as 4/5, 1/9)
        if (rules?.Time is { } tm && tm.Contains('/') && int.TryParse(tm[(tm.IndexOf('/') + 1)..], out int den) && (den & (den - 1)) != 0) rules = null;
        return ps.Detections == null ? rules : DetectedTime(ps, mine, st, startX, s, rules);
    }

    static Sig TimeByRules(PageSymbols ps, List<Other> mine, Img ink, List<Comp> holes, Staff st, double startX, double s, bool afterBar)
    {
        var (sig, decided, box) = ReadTimeLearnt(ps, ink, st, startX, s);
        // (after a barline a bar's first note and its accidental stand where a sign would: only a
        // sign the classifier knows is taken there, not the rules' guess at a shape unlike its samples)
        if (!decided) return afterBar && GlyphKnn.Enabled ? null : ReadTime(ps, mine, ink, holes, st, startX, s);
        if (sig == null) return null;
        foreach (var o in mine.Where(o => o.C.Cx >= box.MinX && o.C.Cx <= box.MaxX && o.C.Cy >= box.MinY && o.C.Cy <= box.MaxY)) o.Class = "timesig";
        ps.Heads.RemoveAll(h => h.St == st && h.X >= box.MinX && h.X <= box.MaxX && h.Y >= box.MinY && h.Y <= box.MaxY);
        return sig;
    }

    /// <summary>
    /// The time signature at startX as the learnt detector sees it (digit_1..digit_9, each with its
    /// value, and the C sign), against what the rules read. The digits of the upper number stand
    /// on the middle line and those of the lower on the bottom line, the two numbers one above the
    /// other; a number is the digits in a row, left to right. The rules read a digit by its shape
    /// (loops, width) and on a scan the toner closes a 9 into an 8 or leaves nothing they accept;
    /// the detector's reading replaces theirs whenever it has both numbers and they make a
    /// signature some piece has (a lower number a power of two, an upper one up to 16). A C sign
    /// seen where the rules read nothing is common time, or cut time when a stroke runs up through
    /// it (the detector's class does not tell the two apart).
    /// </summary>
    static Sig DetectedTime(PageSymbols ps, List<Other> mine, Staff st, double startX, double s, Sig rules)
    {
        // the digits after startX within the staff: a key's seven signs are read before this, so
        // the sign stands close; "12" reaches five spaces. (Some fonts draw the digits large: Petaluma's
        // stand out of the staff, the lower one's middle on the bottom line)
        var digits = ps.Detections.Where(t => t.Class.StartsWith("digit_") && t.Score >= Detector.Threshold
                                              && t.X > startX - 0.3 * s && t.X < startX + 5 * s
                                              && Math.Abs(st.Pos(t.Y)) > 0.5 && Math.Abs(st.Pos(t.Y)) < 5)
                                .OrderBy(t => t.X).ToList();
        // the first number of each half: the digits in a row from the leftmost (a digit is a space
        // and a half wide; Petaluma's nearly two, its 12's centres 1.85 spaces apart)
        List<Detection> Number(IEnumerable<Detection> half)
        {
            var row = new List<Detection>();
            foreach (var t in half)
            {
                if (row.Count > 0 && t.X - row[^1].X > 2.2 * s) break;
                // (a tall digit can peak twice, one over the other: Petaluma's 1, three spaces tall)
                if (row.Count > 0 && t.Class == row[^1].Class && t.X - row[^1].X < 0.5 * s)
                {
                    if (t.Score > row[^1].Score) row[^1] = t;
                    continue;
                }
                row.Add(t);
            }
            return row;
        }
        var up = Number(digits.Where(t => st.Pos(t.Y) > 0));
        var low = Number(digits.Where(t => st.Pos(t.Y) < 0));
        // Petaluma's 1 has a flag at its head, unlike every other font's, and the detector misses
        // it: a "12" read as 2, the 1 left for the rules to take as a quarter rest before the sign
        // (and a sign after a bar's first note is none). A lone upper digit that would make a number
        // with a 1 before it, and an unclaimed stroke there, narrow, as tall as a digit, in the
        // upper half of the staff, is that 1
        Other one = null;
        if (up.Count == 1 && up[0].Class[6] - '0' <= 6)
            one = mine.Where(o => o.Class == "?" && o.C.MaxX < up[0].X - 0.3 * s && o.C.MaxX > up[0].X - 1.6 * s && o.C.MinX > startX - 0.3 * s
                                  && o.C.W < 1.3 * s && o.C.H > 2.3 * s && o.C.H < 4.5 * s && st.Pos(o.C.MaxY) > -0.7 && st.Pos(o.C.MinY) > 3)
                      .OrderByDescending(o => o.C.MaxX).FirstOrDefault();
        Sig sig = null;
        double least = 1;
        if (up.Count > 0 && low.Count > 0 && up.Count <= 2 && low.Count <= 2
            && Math.Abs(up.Average(t => t.X) - low.Average(t => t.X)) < 1.0 * s)
        {
            int num = (one != null ? 1 : 0) * 10 + up.Aggregate(0, (n, t) => n * 10 + (t.Class[6] - '0')), den = low.Aggregate(0, (n, t) => n * 10 + (t.Class[6] - '0'));
            if (one != null)
            {
                if (Verbose) Console.WriteLine($"    time: the stroke at x {one.C.MinX}-{one.C.MaxX} before the detector's {up[0].Class} is a 1, staff {st.System + 1}/{st.Part}");
                one.Class = "timesig";
            }
            least = up.Concat(low).Min(t => t.Score);
            if (Valid(num, den))
                sig = new Sig { St = st, X = Math.Min(Math.Min(up[0].X, low[0].X) - 0.7 * s, one?.C.MinX ?? double.MaxValue), Time = $"{num}/{den}" };
        }
        else if (up.Count == 0 && low.Count == 0)
        {
            // (any score: where the rules read nothing, common time is the reader's guess anyway)
            var c = ps.Detections.FirstOrDefault(t => t.Class == "timesig_c" && t.Score >= Detector.Threshold
                                                      && t.X > startX - 0.3 * s && t.X < startX + 4 * s && Math.Abs(st.Pos(t.Y)) < 1.0);
            // (cut time is the C with a stroke up through its middle and out above it: a piece of ink
            // across the C's middle column reaching well over the C's top, a space above its centre.
            // Petaluma's ¢ is 5.5 spaces tall, beyond the rules' C)
            bool cut = c != null && mine.Any(o => o.C.MinX <= c.X + 0.4 * s && o.C.MaxX >= c.X - 0.4 * s
                                                  && o.C.MinY < c.Y - 1.35 * s && o.C.MaxY > c.Y - 0.5 * s);
            if (c != null) { sig = new Sig { St = st, X = c.X - 0.7 * s, Time = cut ? "2/2" : "4/4" }; least = c.Score; }
        }
        else if (up.Count == 0 && low.Count <= 2 && rules == null)
        {
            // the lower number seen and the upper one not (Petaluma's and MuseJazz's pen-drawn 7,
            // whose curl the detector knows in no font; Finale Broadway's 5), with a piece of ink over it reaching from above the middle
            // line: a sign whose upper number the measures after it tell (Reader.CheckTimes)
            double lx = low.Average(t => t.X);
            // (or one the rules took for a sign's and read no digit in: Finale Broadway's 5)
            var over = mine.FirstOrDefault(o => o.Class is "?" or "timesig" && Math.Abs(o.C.Cx - lx) < 1.0 * s && o.C.W < 3 * s && o.C.H > 2 * s
                                               && st.Pos(o.C.MinY) > 3 && st.Pos(o.C.MinY) < 7);
            int den = low.Aggregate(0, (n, t) => n * 10 + (t.Class[6] - '0'));
            if (over != null && Valid(1, den))
            {
                sig = new Sig { St = st, X = Math.Min(low[0].X - 0.7 * s, over.C.MinX), Time = $"?/{den}" };
                least = low.Min(t => t.Score);
            }
        }
        // a rules' sign of digits where the detector sees no digit nor C but is sure of a sign or a
        // rest is that: Petaluma's natural before a guitar's slashes, read by the rules as 1/1
        if (sig == null && rules != null && up.Count == 0 && low.Count == 0
            && !ps.Detections.Any(t => t.Class == "timesig_c" && t.Score >= Detector.Threshold && Math.Abs(t.X - rules.X) < 3 * s && Math.Abs(st.Pos(t.Y)) < 5)
            && ps.Detections.FirstOrDefault(t => (t.Class is "sharp" or "flat" or "natural" or "dsharp" or "dflat" || t.Class.StartsWith("rest_"))
                                                 && t.Score >= Symbols.SureScore && t.X > rules.X - 0.5 * s && t.X < rules.X + 2.5 * s
                                                 && Math.Abs(st.Pos(t.Y)) < 5) is { } other)
        {
            if (Verbose) Console.WriteLine($"    time {rules.Time} of the rules, staff {st.System + 1}/{st.Part}, dropped: the detector sees {other.Class} ({other.Score:F2}) there and no digit");
            return null;
        }
        if (sig == null) return rules;
        if (rules != null && rules.Time == sig.Time) return rules;
        // the C sign's kind is the rules' to tell (a cut C's stroke); it stands when they read a C
        if (sig.Time is "4/4" or "2/2" && up.Count == 0 && rules != null && (rules.Time == "4/4" || rules.Time == "2/2")) return rules;
        // (a digit the detector names is the right one: over the clean, scanned and fill sets it
        // named no digit wrongly, only missed some; the rules' shape reading on a scan is what fails)
        if (Verbose) Console.WriteLine($"    time {sig.Time} from the detector ({least:F2}), staff {st.System + 1}/{st.Part}, for the rules' {rules?.Time ?? "none"}");
        // the sign's pieces of ink (a digit's loop may have been taken for a head)
        double x0 = sig.X, x1 = up.Concat(low).Select(t => t.X).DefaultIfEmpty(sig.X + 0.7 * s).Max() + 0.8 * s;
        foreach (var o in mine.Where(o => o.Class == "?" && o.C.Cx >= x0 && o.C.Cx <= x1 && o.C.MaxY >= st.Top - 0.5 * s && o.C.MinY <= st.Bottom + 0.5 * s)) o.Class = "timesig";
        ps.Heads.RemoveAll(h => h.St == st && h.X >= x0 && h.X <= x1 && h.Y >= st.Top - 0.5 * s && h.Y <= st.Bottom + 0.5 * s);
        return sig;

        static bool Valid(int num, int den) => num >= 1 && num <= 16 && (den == 1 || den == 2 || den == 4 || den == 8 || den == 16);
    }

    /// <summary>
    /// The first sign after startX: the ink across the staff's height from its first column to a
    /// clear gap, whatever pieces it came in (a cut C comes apart at its stroke; a digit's loop may
    /// have been taken for a note head).
    /// </summary>
    static Comp SignBox(Img ink, Staff st, double startX, double s)
    {
        int y0 = (int)(st.Top - 0.6 * s), y1 = (int)(st.Bottom + 0.6 * s);
        bool Col(int x)
        {
            int c = 0;
            for (int y = y0; y <= y1; y++) if (ink[x, y] && ++c >= 2) return true;
            return false;
        }
        int x = (int)startX + 1, limit = (int)(startX + 2.0 * s);
        while (x < limit && !Col(x)) x++;
        if (x >= limit) return null;
        // (after a barline, startX is the barline's: its own ink, a thin line the staff's height, is
        // stepped over -- else a change of time in a system read as the line, "t:none")
        bool Line(int cx)
        {
            int c = 0;
            for (int y = (int)st.Top; y <= (int)st.Bottom; y++) if (ink[cx, y]) c++;
            return c >= 0.9 * (st.Bottom - st.Top);
        }
        if (Line(x))
        {
            int w = 0;
            while (x < limit && Line(x)) { x++; w++; }
            if (w > 0.4 * s) { x -= w; }   // (no barline: something as wide)
            else
            {
                limit = (int)(x + 2.0 * s);
                while (x < limit && !Col(x)) x++;
                if (x >= limit) return null;
            }
        }
        int x0 = x, last = x, gap = 0;
        for (; x < x0 + 4 * s; x++)
        {
            if (Col(x)) { last = x; gap = 0; }
            else if (++gap > 0.35 * s) break;
        }
        return InkBox(ink, x0, y0, last, y1);
    }

    /// <summary>
    /// Reads the sign at startX with the learnt classifier: C, cut C, a pair of numbers, or no time
    /// signature at all. Not decided when the classifier is off or the shape is unlike its samples.
    /// Every cut-out is kept in ps.TimeSamples, for training.
    /// </summary>
    static (Sig Sig, bool Decided, Comp Box) ReadTimeLearnt(PageSymbols ps, Img ink, Staff st, double startX, double s)
    {
        var box = SignBox(ink, st, startX, s);
        if (box == null) return (null, GlyphKnn.Enabled, null);
        var f = GlyphKnn.BoxFeatures(ink, box, s, (box.MinY - st.Top) / s, (box.MaxY - st.Bottom) / s);
        ps.TimeSamples.Add(("t", box, f));
        // the digits of each half, cut apart at blank columns (or at the thinnest, when they touch)
        int mid = (int)Math.Round(st.Mid);
        var halves = new List<List<Comp>>();
        foreach (var (y0, y1) in new[] { ((int)(st.Top - 0.3 * s), mid - 2), (mid + 2, (int)(st.Bottom + 0.3 * s)) })
        {
            var digits = new List<Comp>();
            int x = box.MinX;
            // (a digit's thin stroke goes with the staff line it crosses at a low resolution and
            // leaves a blank column or two inside the digit: a gap that narrow is no digit boundary)
            // (digits that touch, or that a gap this small keeps apart, are cut at their waist below)
            int gapMax = (int)(0.25 * s);
            while (x <= box.MaxX)
            {
                while (x <= box.MaxX && InkBox(ink, x, y0, x, y1) == null) x++;
                int a = x, last = x;
                while (x <= box.MaxX && x - last <= gapMax)
                {
                    if (InkBox(ink, x, y0, x, y1) != null) last = x;
                    x++;
                }
                x = last + 1;
                if (last - a + 1 < 0.15 * s) continue;
                var d = InkBox(ink, a, y0, last, y1);
                if (d == null) continue;
                if (d.W > 1.15 * d.H)
                {
                    int cut = -1, least = int.MaxValue;
                    for (int cx = d.MinX + (int)(0.3 * d.W); cx <= d.MinX + (int)(0.7 * d.W); cx++)
                    {
                        int n = 0;
                        for (int y = d.MinY; y <= d.MaxY; y++) if (ink[cx, y]) n++;
                        if (n < least) { least = n; cut = cx; }
                    }
                    var l = InkBox(ink, d.MinX, d.MinY, cut - 1, d.MaxY);
                    var r = InkBox(ink, cut + 1, d.MinY, d.MaxX, d.MaxY);
                    if (l != null && r != null) { digits.Add(l); digits.Add(r); continue; }
                }
                digits.Add(d);
            }
            halves.Add(digits);
        }
        var digitFeatures = halves.Select(h => h.Select(d => (d, F: GlyphKnn.BoxFeatures(ink, d, s))).ToList()).ToList();
        foreach (var h in digitFeatures) foreach (var (d, df) in h) ps.TimeSamples.Add(("d", d, df));
        if (!GlyphKnn.Enabled) return (null, false, box);

        var (label, dist) = GlyphKnn.Classify(f, family: "t:");
        if (Verbose) Console.WriteLine($"    time sign at {box.MinX},{box.MinY} {box.W / s:F2}x{box.H / s:F2}: {label} {dist:F2}");
        // (a sign of numbers unlike any the classifier knows -- an old print's figures, kinkel19-2's
        // 3/4 at 4.8-6.2 -- is not read digit by digit either: tried, the digits then read the first
        // notes of a bar as a time signature on the clean generated pages, one in most suites)
        if (dist > 3.0) return (null, false, box);
        switch (label)
        {
            case "t:none": return (null, true, box);
            case "t:timesig.C44": return (new Sig { St = st, X = box.MinX, Time = "4/4" }, true, box);
            // (cut time's stroke stands out of the C, above and below: 2.8 spaces tall in Emmentaler,
            // 2.9-5.5 in the SMuFL fonts, where the C is 2.0-2.4. A C read as cut over thick staff
            // lines, their remnants on it, is no taller than a C)
            case "t:timesig.C22": return (new Sig { St = st, X = box.MinX, Time = box.H < 2.45 * s ? "4/4" : "2/2" }, true, box);
        }
        // numbers: every digit must be one the classifier knows
        var nums = new List<int>();
        foreach (var h in digitFeatures)
        {
            if (h.Count == 0) return (null, false, box);
            int n = 0;
            foreach (var (d, df) in h)
            {
                var (dl, dd) = GlyphKnn.Classify(df, family: "d:");
                if (Verbose) Console.WriteLine($"      digit at {d.MinX},{d.MinY} {d.W / s:F2}x{d.H / s:F2}: {dl} {dd:F2}");
                if (dd > 3.0 || dl == "d:none" || !int.TryParse(dl[2..], out int v)) return (null, false, box);
                n = n * 10 + v;
            }
            nums.Add(n);
        }
        if (nums[0] == 0 || nums[1] == 0) return (null, false, box);
        return (new Sig { St = st, X = box.MinX, Time = $"{nums[0]}/{nums[1]}" }, true, box);
    }

    static Sig ReadTime(PageSymbols ps, List<Other> mine, Img ink, List<Comp> holes, Staff st, double startX, double s)
    {
        // a sign hugging a head is that note's accidental, never a time signature
        bool Hugs(Comp c) => ps.Heads.Any(h => h.St == st && h.L - c.MaxX > -0.2 * s && h.L - c.MaxX < 0.6 * s
                                              && h.Y > c.MinY - 0.5 * s && h.Y < c.MaxY + 0.5 * s);
        // the numbers fill the upper and lower halves of the staff; the two can touch through the
        // middle line and come as one piece, so each piece is cut at the middle line
        var pieces = mine.Where(o => o.Class == "?" && o.C.MinX > startX && o.C.MinX < startX + 2.5 * s
                                     && o.C.H >= 1.5 * s && o.C.H <= 4.6 * s && o.C.W >= 0.5 * s && o.C.W <= 3.6 * s
                                     && o.C.MinY >= st.Top - 0.6 * s && o.C.MaxY <= st.Bottom + 0.6 * s).ToList();
        // common time "C" (4/4) sits on the middle line, a space tall either way; a cut time
        // sign (2/2) is the same with a stroke through it, taller than the staff's middle half
        // (the C's thick back runs down most of its height on the left; an eighth rest's does not)
        bool CShape(Comp c)
        {
            if (Math.Abs(st.Pos(c.MinY) + st.Pos(c.MaxY)) > 0.6) return false;       // centred on the middle line
            int x = c.MinX + (int)(0.15 * c.W), run = 0, best = 0;
            for (int y = c.MinY; y <= c.MaxY; y++) { if (ink[x, y]) { run++; best = Math.Max(best, run); } else run = 0; }
            if (best < 0.5 * c.H) return false;
            // ... and its top arm reaches right (a flat's top is its stem alone)
            // (look a third down: a cut-time stroke stands above the C itself)
            for (int y = c.MinY; y <= c.MinY + (int)(0.35 * c.H); y++)
                for (int xx = c.MaxX - (int)(0.4 * c.W); xx <= c.MaxX + (int)(0.6 * s); xx++)
                    if (ink[xx, y]) return true;
            return false;
        }
        var cSign = mine.FirstOrDefault(o => o.Class == "?" && o.C.MinX > startX && o.C.MinX < startX + 2.5 * s
                                             && o.C.W >= 0.6 * s && o.C.W <= 2.0 * s
                                             && o.C.H >= 1.6 * s && o.C.H <= 3.8 * s
                                             && !holes.Any(h => h.MinX >= o.C.MinX && h.MaxX <= o.C.MaxX && h.MinY >= o.C.MinY && h.MaxY <= o.C.MaxY && h.W > 0.15 * s)
                                             && !Hugs(o.C) && CShape(o.C));
        if (cSign != null)
        {
            cSign.Class = "timesig";
            // the C's serif can come loose where a staff line is removed: take the pieces right beside it
            foreach (var o in mine.Where(o => o.Class == "?" && o.C.MinX > cSign.C.MinX && o.C.MinX < cSign.C.MaxX + 0.4 * s && o.C.H < 1.2 * s))
                o.Class = "timesig";
            // cut time: a stroke runs up through the C and out above it (it may have come loose
            // from the C where a staff line was taken out, so look at the ink above the C)
            var k0 = cSign.C;
            int mx = (k0.MinX + k0.MaxX) / 2, reach = (int)(0.2 * s);
            bool strokeAbove = false;
            for (int x = mx - reach; x <= mx + reach && !strokeAbove; x++) strokeAbove = ink[x, k0.MinY - (int)(0.3 * s)];
            return new Sig { St = st, X = cSign.C.MinX, Time = cSign.C.H > 2.6 * s || strokeAbove ? "2/2" : "4/4" };
        }
        int mid = (int)Math.Round(st.Mid);
        if (Verbose && st.Part == 0)
            foreach (var o in mine.Where(o => o.C.MinX > startX && o.C.MinX < startX + 2.5 * s))
                Console.WriteLine($"    time? staff {st.System + 1}/{st.Part} from {startX:F0}: {o.Class} x {o.C.MinX}-{o.C.MaxX} w {o.C.W / s:F2} h {o.C.H / s:F2} y {st.Pos(o.C.MinY):F1}..{st.Pos(o.C.MaxY):F1}");
        var up = new List<Comp>();
        var low = new List<Comp>();
        foreach (var o in pieces)
        {
            // each half, then each digit in it (split at blank columns: "12" is two digits)
            foreach (var (y0, y1, list) in new[] { (o.C.MinY, mid - 2, up), (mid + 2, o.C.MaxY, low) })
            {
                var half = InkBox(ink, o.C.MinX, y0, o.C.MaxX, y1);
                if (half == null || half.H < 1.5 * s) continue;
                // the pieces inside the half; a stray tail of the other number is too short to count
                foreach (var dgt in SubComps(ink, half.MinX, half.MinY, half.MaxX, half.MaxY).OrderBy(c => c.MinX))
                {
                    {
                        if (dgt.H < 1.5 * s || dgt.W < 0.3 * s) continue;
                        // touching digits ("12" set tight): too wide for one, cut at the thinnest column
                        if (dgt.W > 1.15 * dgt.H)
                        {
                            int cut = -1, least = int.MaxValue;
                            for (int cx = dgt.MinX + (int)(0.3 * dgt.W); cx <= dgt.MinX + (int)(0.7 * dgt.W); cx++)
                            {
                                int n = 0;
                                for (int y = dgt.MinY; y <= dgt.MaxY; y++) if (ink[cx, y]) n++;
                                if (n < least) { least = n; cut = cx; }
                            }
                            var l = InkBox(ink, dgt.MinX, dgt.MinY, cut - 1, dgt.MaxY);
                            var r = InkBox(ink, cut + 1, dgt.MinY, dgt.MaxX, dgt.MaxY);
                            if (l != null && r != null) { list.Add(l); list.Add(r); continue; }
                        }
                        list.Add(dgt);
                    }
                }
            }
        }
        if (up.Count == 0 || low.Count == 0) return null;
        up.Sort((p, q) => p.MinX.CompareTo(q.MinX));
        low.Sort((p, q) => p.MinX.CompareTo(q.MinX));
        // the two numbers stand one above the other
        double upC = (up.Min(c => c.MinX) + up.Max(c => c.MaxX)) / 2.0, lowC = (low.Min(c => c.MinX) + low.Max(c => c.MaxX)) / 2.0;
        if (Math.Abs(upC - lowC) > 1.0 * s) return null;
        int? Number(List<Comp> ds)
        {
            int n = 0;
            foreach (var c in ds)
            {
                int v = Digits.TimeDigit(ink, c, holes, out string why);
                if (Verbose) Console.WriteLine($"    digit at {c.MinX},{c.MinY} {c.W / s:F2}x{c.H / s:F2}: {v} ({why})");
                n = n * 10 + v;
            }
            return n;
        }
        var num = Number(up);
        var den = Number(low);
        if (num == null || den == null || num == 0 || den == 0) return null;
        foreach (var o in pieces) o.Class = "timesig";
        return new Sig { St = st, X = up[0].MinX, Time = $"{num}/{den}" };
    }

    /// <summary>Connected pieces of the ink inside a rectangle, in page coordinates.</summary>
    public static List<Comp> SubComps(Img ink, int x0, int y0, int x1, int y1)
    {
        int w = x1 - x0 + 1, h = y1 - y0 + 1;
        var (_, comps) = Components.Label(w, h, i => ink[x0 + i % w, y0 + i / w], eight: true);
        var list = new List<Comp>();
        foreach (var c in comps.Skip(1))
        {
            c.MinX += x0; c.MaxX += x0; c.MinY += y0; c.MaxY += y0;
            c.Sx += (long)x0 * c.N; c.Sy += (long)y0 * c.N;
            list.Add(c);
        }
        return list;
    }

    /// <summary>The tight box of the ink inside a rectangle, or null when it is empty.</summary>
    static Comp InkBox(Img ink, int x0, int y0, int x1, int y1)
    {
        var c = new Comp();
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
                if (ink[x, y])
                {
                    c.N++;
                    if (x < c.MinX) c.MinX = x;
                    if (x > c.MaxX) c.MaxX = x;
                    if (y < c.MinY) c.MinY = y;
                    if (y > c.MaxY) c.MaxY = y;
                }
        return c.N == 0 ? null : c;
    }
}
