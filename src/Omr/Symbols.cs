namespace Omr;

sealed class Head
{
    public Staff St;
    public double X, Y;
    public bool Hollow;
    public bool Cross;           // a drum's cross head (the detector's head_x)
    public bool Slash;           // a rhythm slash (slash_black, slash_hollow): a duration, no pitch
    public bool WholeShape;     // passed the whole-note test (may stand without a stem)
    public int Pos;
    public double L, R;          // horizontal extent of the head
    public double OuterW;        // hollow: ring width
    public bool HasStem, Up;
    public double StemX, Tip;
    public int Beams;            // beams or flags at the stem tip
    public int Dots;
    public int? Acc;             // accidental written before the head
    public bool Tie;             // a tie runs from this head to the next one at its pitch
    public bool SlurStart, SlurEnd;   // a slur starts at this head (its chord) / ends at it
    public double Score;         // the learnt detector's confidence (0: the rules found it)
    public int Cluster;          // ink component id
    public (int Num, int Den) Scale = (1, 1);   // tuplet
    public int Tuplet;           // which tuplet number's span it is under (0: none): the .lys keeps the groups as written
    public List<HeadAlt> Alts;   // other readings the page half supports (Symbols.Doubts): the .lys's @todo and its candidates
    public bool UnseenDot;       // a dot of its the rules found and the detector saw nothing on
    public Staff Was;            // the nearest staff, when its beam or ledgers gave it the next one (BeamStaves)
    public bool Far;             // further from its staff than Nearest reaches: on many ledger lines, or nothing
    public bool Grace;           // a grace note's head, small (the detector's grace_head, S21): no time in the bar
    public bool Slashed;         // a grace note's: a slash through its stem or beam (an acciaccatura)
    public int Tremolo;          // a two-note tremolo's note: the strokes between it and its mate (Symbols.Tremolos); each plays half its written value
    public bool TremoloFirst;    // the first note of the two
    public bool TremoloStem;     // one of the two has a stem: both are halves (a hollow head's stem missed, read as a whole's)
    /// <summary>Whether its sign or dot may be one found on this staff: its own, or the nearest it was moved from (signs and dots go by the nearest staff).</summary>
    public bool On(Staff st) => St == st || Was == st;
}

/// <summary>
/// Another reading of a head: another accidental (SetAcc; Acc null = none written) or a dot more
/// or less (DotsBy), with what the page shows for it (What) and how sure the detector was.
/// </summary>
sealed record HeadAlt(string What, bool SetAcc, int? Acc, int DotsBy, double Score);

enum RestKind { Whole, Half, Quarter, Eighth, Sixteenth }

sealed class Rest
{
    public Staff St;
    public double X, Y, L, R;
    public RestKind Kind;
    public int Dots;
    public (int Num, int Den) Scale = (1, 1);
    public int Tuplet;
}

/// <summary>A beat repeat: one to four slashes a beam's thickness on the staff, no head among them (Symbols.BeatRepeats).</summary>
sealed record BeatRepeat(Staff St, double L, double R, int Strokes)
{
    public (double L, double R)? Played;   // the group it plays again, where the reader wrote it out (Reader.StaffEvents): what the redraw check takes it for
}

/// <summary>An ink component that is not part of a note: a candidate rest, accidental, dot, ...</summary>
sealed class Other
{
    public Comp C;
    public Staff St;
    public string Class = "?";
    public Head For;     // an accidental's: the head it was given to (Classify), taken back when the piece is an arpeggio's
}

sealed class PageSymbols
{
    public List<Head> Heads = new();
    public List<Head> Graces = new();    // the grace notes' heads, small: they take no time, written before the note after them
    public List<Rest> Rests = new();
    public List<Other> Others = new();
    public List<Sig> Sigs = new();
    public List<Other> Tuplets = new();  // the tuplet numbers taken, with their staff (a detected one may be no piece of Others)
    public Dictionary<(int Bar, int Part), (Staff St, List<Ev> Evs)> Unprinted = new();   // the triplets read from a bar's length, no number printed: their events (Reader.UnprintedTriplets)
    public List<Detection> Detections;   // what the learnt detector found (--detector), or null
    public List<Tempo.Frame> Frames;     // the rehearsal marks' boxes over the systems (found with the detector; else Tempo finds them)
    public List<Detection> WeakSigns = new();   // its sharps and flats under Detector.Threshold: a key's, only where the system's other staves read that key
    public List<Detection> WeakDots = new();    // its dots under Detector.Threshold: only in the column of a surer dot (Symbols.ColumnDots)
    public List<Detection> WeakHairpins = new();   // its hairpins' ends under Detector.Threshold: only where the ink holds the two lines (Expressions)
    public List<Detection> WeakPedals = new();     // its pedal brackets' end hooks and notches and its *s under Detector.Threshold: only where the pedal is down (Expressions)
    public List<Detection> WeakOttavas = new();    // its octave lines' signs under Detector.Threshold: only where the dashes run on from them in the ink (Ottavas)
    public List<Detection> WeakSegnos = new();     // its segnos under Detector.Threshold: only where a jump back to the sign was read in words and no sure segno seen (Form)
    public bool Scan;                              // the page is a scan, grainy or bowed (Img.Raw, Img.Bowed): Fixes weighs the detector's weak dots only there
    public List<Detection> WeakRests = new();      // its rests under Detector.Threshold: only as a bar's one reading that fills every voice (Fixes)
    public List<Detection> WeakTrills = new();     // its trills' "tr"s under Detector.Threshold: only where a trill line's wave follows (Articulations)
    public List<Detection> WeakClefs = new();      // its clefs under Detector.Threshold: only inside a staff, on a clef's shape in the ink (Signatures.MidClefs)
    public List<Detection> WeakNaturals = new();   // its naturals under Detector.Threshold: only on a scan, by a head with no sign, on ink no rule named (Symbols.WeakNaturals)
    public List<Arpeggios.Arp> Arpeggios = new();   // the arpeggios' wavy lines left of chords: @arpeggio on them, and no rests in them
    public List<(double L, double T, double R, double B)> RepeatSlashes = new();   // the slashes of chords repeated by their stems (ChordRepeats): no symbols
    public List<BeatRepeat> BeatRepeats = new();   // the slashes that play the beam group before them again (Symbols.BeatRepeats)
    public string Note;          // with the detector: how many of its heads the rules kept, and what dropped the rest
    public int[] Labels;         // the ink's components, by pixel
    public int LabelW;
    // every place a time signature could stand, as cut out ("t": the whole sign, "d": a digit of it)
    public List<(string Kind, Comp Box, double[] F)> TimeSamples = new();
}

static partial class Symbols
{
    public static (double X, double Y)? DebugAt;
    static readonly Dictionary<Other, List<Other>> pieceOf = new();
    public static bool TupletVerbose;
    public static bool KnnOnly;      // no hand-written rules for the shapes the classifier is unsure of
    public static bool Collect;      // the rules' findings become the score's own classifier samples
    // Two scores stand between the detector and the rules (each retrain shifts the model's scores a
    // little, and a rule that leaned on one number broke with every new model):
    // - SureScore (--sure): a detection this sure overrules a reading the rules made otherwise (a
    //   head the rules hold for a digit or a clef's ink, a clef of another kind, a stray number);
    // - TrustScore (--trust): a detection this sure fills in where the rules read nothing (a head
    //   with no stem or ledger line found, a clef or rest the rules did not find, a sign the rules
    //   did not name), and where it only extends the rules' reading (a key of more signs, the
    //   rules' among them). Above 1 = the rules decide, as before.
    public static double SureScore = 0.9;
    public static double TrustScore = 0.5;

    static bool IsWeakSign(Detection t) => (t.Class == "sharp" || t.Class == "flat") && t.Score <= Detector.Threshold;

    /// <summary>
    /// The piece of ink at a point (a detection's centre), on <paramref name="st"/> if given: of
    /// the pieces whose boxes hold the point, the one whose ink lies there, else the smallest. The
    /// first piece in the list whose box held the point was taken before: a long slur, a hairpin or
    /// a scan's border boxes in the signs under it, and the sign's own piece went unlooked at
    /// (kinkel19-2's scan: its frame, one piece named for the clef it ran past, owned every natural
    /// and flat the detector saw, and they were passed over as the clef's).
    /// </summary>
    public static Other PieceAt(PageSymbols ps, double x, double y, Staff st = null)
    {
        Other best = null;
        long area = long.MaxValue;
        int px = (int)Math.Round(x), py = (int)Math.Round(y);
        int at = ps.Labels != null && px >= 0 && py >= 0 && px < ps.LabelW && py * ps.LabelW + px < ps.Labels.Length ? ps.Labels[py * ps.LabelW + px] : 0;
        foreach (var o in ps.Others)
        {
            if (st != null && o.St != st) continue;
            var c = o.C;
            if (x < c.MinX || x > c.MaxX || y < c.MinY || y > c.MaxY) continue;
            if (at != 0 && c.Id == at) return o;
            long a = (long)c.W * c.H;
            if (a < area) { area = a; best = o; }
        }
        return best;
    }

    /// <param name="det">what the learnt detector found on the page (Reader runs it first, for the barlines), or null</param>
    /// <param name="weakDots">the detector's dots under its threshold (Detector.WeakDot), or null</param>
    /// <param name="weakHeads">the detector's black heads under its threshold (Detector.WeakGrace): only a grace note's, or null</param>
    /// <param name="weakSlashes">the detector's rhythm slashes under its threshold (Detector.WeakSlash): only on a one-line rhythm staff, alone, leaning as a slash, or null</param>
    /// <param name="weakOttavas">the detector's octave line signs under its threshold (Detector.WeakOttava): their letters hold no heads where their dashes run on, or null</param>
    /// <param name="weakHalves">the detector's half heads under its threshold (Detector.WeakHalf): only rivals to a black head seen about as surely, or half notes whose stem's tip it sees, or null</param>
    public static PageSymbols Find(Img d, Img ink, List<Staff> staves, List<SystemInfo> systems, double s, List<Detection> det, Action<PageSymbols> clefsRead = null, List<Detection> weakDots = null, List<Detection> weakHeads = null, List<Detection> weakSlashes = null, List<Detection> weakOttavas = null, List<Detection> weakClefs = null, List<Detection> weakHalves = null, List<Detection> weakNaturals = null)
    {
        var ps = new PageSymbols();
        ps.Scan = d.Raw != null || d.Bowed != null;   // (as Reader has it: the accidentals' reading weighs it too)
        if (weakDots != null) ps.WeakDots = weakDots;
        if (weakClefs != null) ps.WeakClefs = weakClefs;
        if (weakNaturals != null) ps.WeakNaturals = weakNaturals;
        var (labels, comps) = Components.Label(ink.W, ink.H, i => ink.D[i], eight: true);
        ps.Labels = labels; ps.LabelW = ink.W;
        var inkHoles = Components.Holes(ink);
        string hollowNote = "";
        bool rulesRead = true;
        if (det != null)
        {
            // the learnt detector's heads (S10); the rules below check them as they check their own
            ps.Detections = det.Where(t => !IsWeakSign(t)).ToList();
            ps.WeakSigns = det.Where(IsWeakSign).ToList();
            DetectedHeads(d, ink, ps.Detections, staves, s, ps.Heads, weakHalves);
            // a head the detector is torn over, black and half each under its threshold (0.47 and 0.47), is
            // the half where its middle is white: an old print's half note, a thin rim round a wide hole
            // (kinkel19-5's scan, the bass's c,2 after a natural)
            if (weakHalves?.Count > 0 && weakHeads?.Count > 0)
                DetectedHeads(d, ink, weakHalves.Where(u => u.Score >= 0.4 && InkCore(ink, u.X, u.Y, 0.15 * s) < 0.3
                    && weakHeads.Any(b => b.Score >= 0.4 && Math.Abs(b.X - u.X) < 0.3 * s && Math.Abs(b.Y - u.Y) < 0.3 * s)
                    && !ps.Heads.Any(h => Math.Abs(h.X - u.X) < 0.5 * s && Math.Abs(h.Y - u.Y) < 0.5 * s)).ToList(), staves, s, ps.Heads);
            // a half head the detector is unsure of, its middle white, where it sees the stem's tip of a
            // note without flags a stem's length off at the head's side, nothing else on the spot and no
            // other head on the way to the tip, is the half note (kinkel10-2's scan, the voice's c'2 under
            // a slur's end: 0.49, the tip 0.81)
            if (weakHalves?.Count > 0)
            {
                var tips0 = ps.Detections.Where(t => t.Class == "stem_0" && t.Score >= TrustScore).ToList();
                bool Stemmed(Detection u) => tips0.Any(t =>
                {
                    double dy = t.Y - u.Y, dx = t.X - u.X;
                    // (down: the stem at the head's left; up: at its right)
                    if (Math.Abs(dy) < 2.5 * s || Math.Abs(dy) > 4.5 * s || Math.Abs(Math.Abs(dx) - 0.6 * s) > 0.4 * s || (dy > 0) != (dx < 0)) return false;
                    return !ps.Heads.Any(h => Math.Abs(h.X - u.X) < 1.0 * s && (h.Y - u.Y) * Math.Sign(dy) > 0.3 * s && (h.Y - u.Y) * Math.Sign(dy) < Math.Abs(dy) + 0.5 * s);
                });
                DetectedHeads(d, ink, weakHalves.Where(u => u.Score >= 0.4 && InkCore(ink, u.X, u.Y, 0.15 * s) < 0.3 && Stemmed(u)
                    && !ps.Heads.Any(h => Math.Abs(h.X - u.X) < 1.0 * s && Math.Abs(h.Y - u.Y) < 0.6 * s)
                    && !ps.Detections.Any(t => !t.Class.StartsWith("stem_") && !t.Class.StartsWith("slur") && !t.Class.StartsWith("tie")
                                               && t.Score >= 0.3 && Math.Abs(t.X - u.X) < 0.6 * s && Math.Abs(t.Y - u.Y) < 0.6 * s)).ToList(), staves, s, ps.Heads);
            }
            // a slash the detector is unsure of (a fermata close over leadsheet-collide's last whole
            // slash: 0.40) on a one-line rhythm staff, where slashes are what stands on the line:
            // alone (no head within a space and a half), and the ink there one piece of a slash's
            // size whose top leans right of its bottom (a stem hangs from its lower left corner)
            if (weakSlashes?.Count > 0)
                foreach (var t in weakSlashes)
                {
                    if (Nearest(staves, t.Y, s) is not { OneLine: true } st || Math.Abs(t.Y - st.Mid) > 0.6 * s) continue;
                    if (ps.Heads.Any(h => h.St == st && Math.Abs(h.X - t.X) < 1.5 * s)) continue;
                    if (!SlashInk(labels, comps, ink.W, t.X, t.Y, s)) continue;
                    if (DebugAt.HasValue && Math.Abs(t.X - DebugAt.Value.X) < 2 * s && Math.Abs(t.Y - DebugAt.Value.Y) < 3 * s)
                        Console.WriteLine($"weak {t.Class} at {t.X:F0},{t.Y:F0} ({t.Score:F2}) on a one-line staff, alone, the ink a slash's: taken");
                    ps.Detections.Add(t);
                    DetectedHeads(d, ink, new List<Detection> { t }, staves, s, ps.Heads);
                }
            // Another font's hollow heads the detector does not know (Leland's half and whole notes
            // in Bohemian, lost whole with each retrain): the rules' hollow heads where the detector
            // sees nothing at all. The rules below check them as they check their own.
            hollowNote = RulesHollowHeads(d, ink, inkHoles, labels, comps, staves, s, ps, out rulesRead);
        }
        else
        {
            FindFilledHeads(ink, staves, s, ps.Heads);
            FindHollowHeads(ink, inkHoles, labels, comps, staves, s, ps.Heads);
            // A head in a space has its thin rim on the staff lines, and removing the lines opens it.
            // Look again with the lines in place.
            FindHollowHeads(ink, SpacedHoles(d, staves, s), labels, comps, staves, s, ps.Heads);
        }
        // a grace note's head is drawn small: it takes no time in the bar, and the .lys writes it before
        // the note. The detector's grace heads (grace_head, S21), under its threshold only where the ink
        // is a small solid head with a stem the detector sees by it; and, where the rules read the ink,
        // the rules' heads that are small (a scan's heads, broken by the toner, are small and narrow
        // here and there -- the detector's on the six scans, which the rules read a third of)
        // A grace note stands left of its note, never in a column with a head: a "grace head" the detector
        // sees in a head's column, a chord's span from it, is the chord's (an old print's heads at 96 dpi,
        // kinkel19-5's <f a c'>8: the lowest, at the stem's root, seen as grace heads 0.56-0.83)
        foreach (var h in ps.Heads.Where(h => h.Grace && h.Score > Detector.Threshold).ToList())
            if (ps.Heads.Any(o => !o.Grace && o.St == h.St && Math.Abs(o.X - h.X) < 0.4 * s && Math.Abs(o.Y - h.Y) > 0.6 * s && Math.Abs(o.Y - h.Y) < 4 * s))
            {
                if (DebugAt.HasValue && Math.Abs(h.X - DebugAt.Value.X) < 2 * s && Math.Abs(h.Y - DebugAt.Value.Y) < 3 * s)
                    Console.WriteLine($"grace head at {h.X:F0},{h.Y:F0} ({h.Score:F2}) in a head's column: the chord's");
                h.Grace = false;
                // (a head's usual width where its ink was broken, as DetectedHeads gives a black head)
                if (h.OuterW < 0.9 * s) { h.L = Math.Round(h.X - 0.65 * s); h.R = Math.Round(h.X + 0.65 * s); h.OuterW = h.R - h.L + 1; }
            }
        // nor is a grace note on the notes' beam: its stem short, never into their beam. A "grace head"
        // whose stem tip the detector sees 3.5 spaces off or more, at the beam another tip of the same
        // kind ends at, a head's below it, is the beam's note (kinkel19-1's scan, the right hand's g, hung
        // under two ledger lines at the start of "r16 g,16 b,16 d16", seen as a grace head 0.88)
        if (ps.Detections != null)
            foreach (var h in ps.Heads.Where(h => h.Grace && h.Score > Detector.Threshold).ToList())
            {
                // (on its own stem's side, an up-stem's at the head's right and a down-stem's at its left, or over the
                // head: not a note's stem past its edge -- gen-grace's basic002, the eighth's tip a space left of the
                // grace head. Over it: kinkel19-1's c16, its own tip unseen and the note's before it there)
                var tip = ps.Detections.FirstOrDefault(t => t.Class.StartsWith("stem_") && Math.Abs(Math.Abs(t.X - h.X) - 0.6 * s) < 0.5 * s
                    && (t.Y < h.Y ? t.X >= h.L - 0.1 * s : t.X <= h.R + 0.1 * s) && Math.Abs(t.Y - h.Y) >= 3.5 * s && Math.Abs(t.Y - h.Y) < 8 * s
                    && Nearest(staves, t.Y, s) == h.St);   // (and on its staff: not the tip of the staff above's note, gen-grace-scan's piano007)
                if (tip == null) continue;
                int side = Math.Sign(tip.Y - h.Y);
                // (nor where a short stem's tip stands the other way in its column: the grace note's own,
                // gen-grace-scan's piano007, a grace head under the staff its stem down, the tip above another note's)
                if (ps.Detections.Any(t => t.Class.StartsWith("stem_") && Math.Abs(Math.Abs(t.X - h.X) - 0.6 * s) < 0.5 * s
                        && Math.Sign(t.Y - h.Y) == -side && Math.Abs(t.Y - h.Y) >= 1.5 * s && Math.Abs(t.Y - h.Y) < 3.5 * s && Nearest(staves, t.Y, s) == h.St))
                    continue;
                // (the two tips one piece of ink, the beam: a grace notes' own beam is no note's -- gen-grace's
                // basic002, two beamed grace sixteenths by an eighth)
                int piece = LabelNear(labels, ink.W, ink.H, tip.X, tip.Y, s);
                if (piece == 0 || !ps.Detections.Any(t => t != tip && t.Class == tip.Class && Math.Abs(t.X - tip.X) > 0.8 * s && Math.Abs(t.X - tip.X) < 4 * s && Math.Abs(t.Y - tip.Y) < 1.5 * s
                        && LabelNear(labels, ink.W, ink.H, t.X, t.Y, s) == piece
                        && ps.Heads.Any(o => !o.Grace && o.St == h.St && Math.Abs(o.X - t.X) < 0.9 * s && Math.Sign(t.Y - o.Y) == side && Math.Abs(t.Y - o.Y) >= 2 * s)))
                    continue;
                if (DebugAt.HasValue && Math.Abs(h.X - DebugAt.Value.X) < 2 * s && Math.Abs(h.Y - DebugAt.Value.Y) < 3 * s)
                    Console.WriteLine($"grace head at {h.X:F0},{h.Y:F0} ({h.Score:F2}) on the notes' beam ({tip.Class} at {tip.X:F0},{tip.Y:F0}): a note");
                h.Grace = false;
                if (h.OuterW < 0.9 * s) { h.L = Math.Round(h.X - 0.65 * s); h.R = Math.Round(h.X + 0.65 * s); h.OuterW = h.R - h.L + 1; }
            }
        var graces =ps.Heads.Where(h => h.Grace && (h.Score > Detector.Threshold
            || GraceHead(ink, h, s, narrowOnly: true) && ps.Detections?.Any(t => t.Class.StartsWith("stem_") && Math.Abs(t.X - h.X) < 1.2 * s && Math.Abs(t.Y - h.Y) < 4 * s) == true)).ToList();
        ps.Heads.RemoveAll(h => h.Grace);
        if (rulesRead) graces.AddRange(ps.Heads.Where(h => GraceHead(ink, h, s) && !graces.Any(g => Math.Abs(g.X - h.X) < 0.8 * s && Math.Abs(g.Y - h.Y) < 0.8 * s)).ToList());
        ps.Heads.RemoveAll(graces.Contains);
        // and the detector's weak black heads that are a grace note's: small and solid (GraceHead), clear
        // of every head kept, a stem the detector sees by it (a beamed group's flags or a lone one's)
        if (rulesRead && weakHeads?.Count > 0 && ps.Detections != null)
        {
            var weak = new List<Head>();
            DetectedHeads(d, ink, weakHeads, staves, s, weak);
            foreach (var g in weak)
                if (GraceHead(ink, g, s, narrowOnly: true) && !ps.Heads.Concat(graces).Any(h => h.St == g.St && Math.Abs(h.X - g.X) < 0.8 * s && Math.Abs(h.Y - g.Y) < 0.8 * s)
                    && ps.Detections.Any(t => t.Class.StartsWith("stem_") && Math.Abs(t.X - g.X) < 1.2 * s && Math.Abs(t.Y - g.Y) < 4 * s))
                    graces.Add(g);
        }
        // (a tempo marking's note is no grace note either: the detector sees its head, small beside the
        // staff's, as a grace head -- tabi.jpg's "♩. = 60" over the first bar read as appoggiatura { a''8 }.
        // Nor is a grace head well off the staff with no ledger lines to it: a grace note's are drawn as a note's)
        graces.RemoveAll(g => MetronomeMark(d, g, s) || Math.Abs(g.Pos) >= 7 && !HasLedgers(d, g, s));
        ps.Graces = graces;
        ps.Heads.RemoveAll(h => MetronomeMark(d, h, s));
        foreach (var h in ps.Heads.Where(h => !h.Cross)) FindStem(ink, h, s);
        var roots = weakHeads != null && ps.Detections != null ? RootHeads(d, ink, staves, ps, weakHeads, graces, s) : new List<Head>();
        // a "head" whose stem ends at a grace note's head is a piece of the grace notes' beam (lied007's
        // two sixteenths): a stem runs on past its chord's last head, never into one
        ps.Heads.RemoveAll(h => h.HasStem && graces.Any(g => g.St == h.St && Math.Abs(g.Y - h.Tip) < 0.5 * s && Math.Abs(g.R - h.StemX) < 0.4 * s));
        foreach (var h in ps.Heads.Where(h => h.Cross)) CrossStem(ink, ps.Heads, h, s);
        ShareStems(ink, ps.Heads, s);
        // (a stem the detector gives a head is shared with the head's chord mates in turn: on a scan
        // the lower head of a second, its stem found for the upper head only from the detector,
        // was left stemless -- chords006's b in bar 8, piano002's c in bar 6)
        if (ps.Detections != null) { DetectedStems(ps, s); ShareStems(ink, ps.Heads, s); GraceStems(ink, ps, s); }
        // A stem runs on well past its chord's last head. Whole notes stacked in thirds line their
        // rims up into a column that ends at the far head (Petaluma's and Gonville's small whole
        // notes, band009's <b d' fis'>1 read as halves): a hollow head's stem ending within a space
        // past the furthest hollow head of its column, with no stem tip the detector sees there, is none.
        foreach (var h in ps.Heads.Where(h => h.HasStem && h.Hollow && !h.Cross).ToList())
        {
            // (the column's heads along the stem, from the head to just past the tip: another voice's
            // half note beyond the tip, in the same column, is not on this stem)
            List<Head> Column(double tip) => ps.Heads.Where(o => o.Hollow && o.St == h.St && Math.Abs(o.X - h.X) < 0.4 * s
                                             && (h.Up ? o.Y <= h.Y && o.Y > tip - 0.5 * s : o.Y >= h.Y && o.Y < tip + 0.5 * s)).ToList();
            var column = Column(h.Tip);
            if (column.Count < 2) continue;
            // a chord's head whose stem was followed only to the next head up takes the stem of a head
            // of the column that runs further, along the same line (E.G.2's dotted half chord, its
            // lowest head's stem found to the head above it, read as a whole note)
            var longer = column.Where(o => o != h && o.HasStem && o.Up == h.Up && Math.Abs(o.StemX - h.StemX) < 0.3 * s
                                           && (h.Up ? o.Tip < h.Tip : o.Tip > h.Tip)).OrderBy(o => h.Up ? o.Tip : -o.Tip).FirstOrDefault();
            double tipAt = longer?.Tip ?? h.Tip;
            if (longer != null) column = Column(tipAt);
            double far = h.Up ? column.Min(o => o.Y) : column.Max(o => o.Y);
            double past = h.Up ? far - tipAt : tipAt - far;
            if (past >= 1.0 * s) { h.Tip = tipAt; continue; }
            if (ps.Detections != null && ps.Detections.Any(t => t.Class.StartsWith("stem_") && t.Score >= Detector.Threshold
                                                               && Math.Abs(t.X - h.StemX) < 0.5 * s && Math.Abs(t.Y - h.Tip) < 1.5 * s)) continue;
            if (DebugAt.HasValue && Math.Abs(h.X - DebugAt.Value.X) < 2 * s && Math.Abs(h.Y - DebugAt.Value.Y) < 3 * s)
                Console.WriteLine($"head at {h.X:F0},{h.Y:F0}: stem to {h.Tip:F0} ends {past / s:F2} spaces past its column's last head: none");
            h.HasStem = false;
        }
        // A second's outer head stands on the far side of its chord's stem: from its edge the stem
        // reads the other way, ending at the chord's far head (E.G.2's <bes, f bes c' f'>, the c'
        // given a down stem to the bes, and read as a voice of its own). A head whose stem shares a
        // chord's column the other way, lying among that chord's heads with its tip at the chord's
        // far end, is the chord's. So is one a step past the chord's head nearest its tip: the second
        // at that end (a down stem's lower head left of it, <f, gis, d>8, read as an up stem to the top
        // head and a voice of its own: docs/repro/seconds-in-chords-2026-10-24.lys).
        foreach (var h in ps.Heads.Where(h => h.HasStem && !h.Cross))
        {
            var mates = ps.Heads.Where(o => o != h && o.HasStem && !o.Cross && o.St == h.St && o.Up != h.Up && o.Hollow == h.Hollow
                                            && Math.Abs(o.StemX - h.StemX) < 0.3 * s).ToList();
            if (mates.Count < 2) continue;
            double lo = mates.Min(o => o.Y), hi = mates.Max(o => o.Y);
            double end = mates[0].Up ? hi : lo;     // the chord's head furthest from its tip
            var near = mates[0].Up ? mates.MinBy(o => o.Y) : mates.MaxBy(o => o.Y);
            bool among = h.Y > lo && h.Y < hi;
            bool past = (mates[0].Up ? h.Y < lo : h.Y > hi) && Math.Abs(h.Pos - near.Pos) == 1;
            if (!among && !past || Math.Abs(h.Tip - end) > 1.0 * s) continue;
            h.Up = mates[0].Up; h.Tip = mates[0].Tip;
        }
        // A second of two, flagged: the outer head's stem is its neighbour's, running past it at its
        // edge, but from the outer head the flag's curve may read as a stem of its own (MuseJazz's
        // chords004, the flag joining the head). A head a step from one whose stem goes the same way
        // through the head and on past it is that stem's.
        foreach (var h in ps.Heads.Where(h => h.HasStem && !h.Cross && !h.Slash))
        {
            var o = ps.Heads.FirstOrDefault(o => o != h && o.HasStem && !o.Cross && o.St == h.St && o.Up == h.Up && o.Hollow == h.Hollow
                                                && Math.Abs(o.Pos - h.Pos) == 1 && Math.Abs(o.StemX - h.StemX) >= 0.3 * s
                                                && o.StemX > h.L - 0.3 * s && o.StemX < h.R + 0.3 * s
                                                && (h.Up ? o.Tip < h.Tip && o.Y > h.Y : o.Tip > h.Tip && o.Y < h.Y));
            if (o == null) continue;
            if (DebugAt.HasValue && Math.Abs(h.X - DebugAt.Value.X) < 2 * s && Math.Abs(h.Y - DebugAt.Value.Y) < 3 * s)
                Console.WriteLine($"head at {h.X:F0},{h.Y:F0}: its stem x {h.StemX:F0} is the neighbour's at {o.StemX:F0} (a second)");
            h.StemX = o.StemX; h.Tip = o.Tip;
        }
        // In a second on one stem the higher head is always right of the stem. A head across one
        // stroke from a neighbour a step higher than it on the left is not that neighbour's chord:
        // the stroke is two stems in one column, the neighbour's one way and another chord's the
        // other way (MuseScore's slurs005: an upper voice's 16th up from its right edge, the lower
        // voice's chord down from the left edge of its top head, the head a step under the 16th).
        // The head is the chord's below (or above) it on its own side.
        foreach (var h in ps.Heads.Where(h => h.HasStem && !h.Cross && !h.Slash))
        {
            bool right = h.X > h.StemX;
            var o = ps.Heads.FirstOrDefault(o => o != h && o.HasStem && !o.Cross && o.St == h.St && o.Up == h.Up
                                                && Math.Abs(o.StemX - h.StemX) < 0.3 * s && Math.Abs(o.Pos - h.Pos) == 1
                                                && (o.X > o.StemX) != right && (right ? o.Pos > h.Pos : o.Pos < h.Pos));
            if (o == null) continue;
            var mates = ps.Heads.Where(m => m != h && m.HasStem && !m.Cross && m.St == h.St && m.Up != h.Up
                                            && Math.Abs(m.StemX - h.StemX) < 0.3 * s && Math.Abs(m.X - h.X) < 0.3 * s
                                            && (m.Up ? m.Y < h.Y : m.Y > h.Y)).ToList();
            if (mates.Count == 0) continue;
            if (DebugAt.HasValue && Math.Abs(h.X - DebugAt.Value.X) < 2 * s && Math.Abs(h.Y - DebugAt.Value.Y) < 3 * s)
                Console.WriteLine($"head at {h.X:F0},{h.Y:F0}: a step under its neighbour across the stem at {o.X:F0},{o.Y:F0}: the other way's chord's");
            h.Up = mates[0].Up; h.Tip = mates[0].Tip;
        }
        // Two voices on one pitch share one head: a stem up at its right edge and another down at
        // its left (lied's piano, the upper voice's note on the lower voice's chord). Each voice
        // has its note: the head again, on the other stem.
        var unisons = new List<Head>();
        foreach (var h in ps.Heads.Where(h => h.HasStem && !h.Cross && !h.Slash && !h.WholeShape))
        {
            // (only a head with its stem on its own side: the outer head of a second sits on the far
            // side of its chord's stem, which runs on past it to the chord's other heads)
            if (h.Up ? h.StemX < h.X : h.StemX > h.X) continue;
            if (!OtherStem(ink, h, s, out int x, out int tip) && !ChordStemAtEdge(ink, h, ps.Heads, s, out x, out tip)) continue;
            if (Math.Abs(x - h.StemX) < 0.6 * s) continue;
            // (and runs on past the heads of its own chord: the edges of hollow heads stacked on one
            // stem make a column, a thick-ringed font's (Finale Broadway's chords) as long as a stem)
            var chord = ps.Heads.Where(o => o.St == h.St && o.HasStem && o.Up == h.Up && Math.Abs(o.StemX - h.StemX) < 0.3 * s).Append(h);
            double far = h.Up ? chord.Max(o => o.Y) : chord.Min(o => o.Y);
            if (Math.Abs(tip - far) < 2.2 * s) continue;
            if (ps.Detections != null && ps.Detections.Any(t => t.Class == "barline" && t.Score > Detector.Threshold && Math.Abs(t.X - x) < 0.4 * s && Math.Abs(t.Y - h.Y) < 6 * s)) continue;
            if (DebugAt.HasValue && Math.Abs(h.X - DebugAt.Value.X) < 2 * s && Math.Abs(h.Y - DebugAt.Value.Y) < 3 * s)
                Console.WriteLine($"head at {h.X:F0},{h.Y:F0}: a stem on either side, {(h.Up ? "down" : "up")} x {x} tip {tip}: two voices' unison");
            unisons.Add(new Head
            {
                St = h.St, X = h.X, Y = h.Y, Hollow = h.Hollow, Pos = h.Pos, L = h.L, R = h.R, OuterW = h.OuterW, Score = h.Score, Cluster = h.Cluster,
                HasStem = true, Up = !h.Up, StemX = x, Tip = tip,
            });
        }
        ps.Heads.AddRange(unisons);
        // a cross the detector is unsure of is a head when it has a stem (a cross's stem is the head's:
        // principle 2); else nothing, not even a detection. A weak whole note likewise, when alone
        if (ps.Detections != null)
        {
            var weak = ps.Heads.Where(h => h.Cross && h.Score <= Detector.Threshold && !h.HasStem).ToList();
            // nor when its stem is a barline the detector sees (a slur crossing the barline, lied003)
            weak.AddRange(ps.Heads.Where(h => h.Cross && h.Score <= Detector.Threshold && h.HasStem
                && ps.Detections.Any(t => t.Class == "barline" && t.Score > Detector.Threshold && Math.Abs(t.X - h.StemX) < 0.4 * s && Math.Abs(t.Y - h.Y) < 6 * s)));
            // nor by a grace note: a piece of it (the white under it on ledger lines, its slash across
            // the flag: lied004)
            weak.AddRange(ps.Heads.Where(h => h.Cross && h.Score <= Detector.Threshold
                && graces.Any(g => g.St == h.St && Math.Abs(g.X - h.X) < 1.5 * s && Math.Abs(g.Y - h.Y) < 3 * s)));
            weak.AddRange(ps.Heads.Where(h => h.Cross && h.Score <= Detector.Threshold && StrokeThrough(ink, h, s)));
            // nor where the detector sees a tie's or a slur's end: one tie's end meeting the next one's
            // start over a head (Petaluma's ties007, des16~ des16~ des16, a cross at 0.25 with the
            // head's stem read as its own)
            weak.AddRange(ps.Heads.Where(h => h.Cross && h.Score <= Detector.Threshold
                && ps.Detections.Any(t => t.Class is "tie_l" or "tie_r" or "slur_l" or "slur_r" && t.Score > Detector.Threshold && Math.Abs(t.X - h.X) < 0.5 * s && Math.Abs(t.Y - h.Y) < 0.5 * s)));
            // nor on an accidental the detector is surer of: a double sharp is a cross (Leland's and
            // Finale Broadway's piano007, <aisis cis'>16's sign at 0.87, a cross at 0.19)
            weak.AddRange(ps.Heads.Where(h => h.Cross && h.Score <= Detector.Threshold
                && ps.Detections.Any(t => t.Class is "sharp" or "flat" or "natural" or "dsharp" or "dflat" && t.Score > Detector.Threshold && t.Score > h.Score
                                          && Math.Abs(t.X - h.X) < 0.5 * s && Math.Abs(t.Y - h.Y) < 0.5 * s)));
            // nor at the very tip of another head's stem: that is the stem's flag (Time After Time's
            // hand-drawn eighth's hook at 0.17 with detector19); a cross on a head's stem lies between
            // the head and the tip (a hi-hat under its beam stands most of a space off the tip)
            weak.AddRange(ps.Heads.Where(h => h.Cross && h.Score <= Detector.Threshold
                && ps.Heads.Any(o => o != h && !o.Cross && o.HasStem && o.St == h.St && Math.Abs(o.StemX - h.X) < 0.6 * s && Math.Abs(h.Y - o.Tip) < 0.5 * s)));
            // nor over a head that is no cross, the detector surer of it: the ink between a chord's
            // heads stacked in thirds (揺れる想い's Syn.2 <bes, d f> read with a c at 0.24); over a third
            // of it (an arpeggio's wave and a whole note right of it, gen-arp's piano004, a cross at 0.16
            // 0.63 spaces left of the whole note's middle)
            weak.AddRange(ps.Heads.Where(h => h.Cross && h.Score <= Detector.Threshold
                && ps.Heads.Any(o => o != h && !o.Cross && o.Score > h.Score && o.St == h.St && Math.Abs(o.X - h.X) < 0.8 * s && Math.Abs(o.Y - h.Y) < 0.6 * s)));
            // nor a step or two from a head the detector is sure of, in its column: a scan's smear of toner on
            // the head's ledger (kinkel19-1's scan, a cross at 0.16 over the right hand's g, under two ledgers,
            // read as its chord). A drum's cross and head on one stem stand two spaces apart and more
            weak.AddRange(ps.Heads.Where(h => h.Cross && h.Score <= Detector.Threshold
                && ps.Heads.Any(o => o != h && !o.Cross && o.Score > Detector.Threshold && o.St == h.St && Math.Abs(o.X - h.X) < 0.4 * s && Math.Abs(o.Y - h.Y) < 1.2 * s)));
            // a whole note the detector is unsure of, only standing alone as a whole note does: no
            // stem, no other head on its staff within four spaces (but a chord's whole notes over it), no
            // rest the detector trusts by it
            weak.AddRange(ps.Heads.Where(h => h.WholeShape && h.Score <= Detector.Threshold
                && (h.HasStem || ps.Heads.Any(o => o != h && o.St == h.St && Math.Abs(o.X - h.X) < 4 * s && !(o.WholeShape && Math.Abs(o.X - h.X) < 0.8 * s))
                    || ps.Detections.Any(t => t.Class.StartsWith("rest_") && t.Score > Detector.Threshold && Math.Abs(t.X - h.X) < 2 * s && Math.Abs(t.Y - h.Y) < 4 * s))));
            // stemless slashes packed side by side are the strokes of a repeat sign of two or four
            // bars (Time After Time's "//" and "////", dotted), not the rhythm: a slash of the rhythm
            // stands a beat apart, or on a stem of its own. Where the detector sees a percent sign
            // too, even unsure of it, the slash is that sign's (its strokes seen as one slash)
            weak.AddRange(ps.Heads.Where(h => h.Slash && !h.HasStem
                && (ps.Heads.Any(o => o != h && o.Slash && !o.HasStem && o.St == h.St && Math.Abs(o.X - h.X) < 3 * s && Math.Abs(o.Y - h.Y) < s)
                    || ps.Detections.Any(t => t.Class.StartsWith("percent_") && t.Score > Detector.WeakPercent && Math.Abs(t.X - h.X) < 2 * s && Math.Abs(t.Y - h.Y) < s))));
            ps.Heads.RemoveAll(weak.Contains);
            ps.Detections = ps.Detections.Where(t => t.Class is not ("head_x" or "head_whole") || t.Score > Detector.Threshold
                                                     || ps.Heads.Any(h => h.X == t.X && h.Y == t.Y)).ToList();
        }
        if (DebugAt.HasValue)
            foreach (var h in ps.Heads.Where(h => Math.Abs(h.X - DebugAt.Value.X) < 2 * s && Math.Abs(h.Y - DebugAt.Value.Y) < 3 * s))
                Console.WriteLine($"head at {h.X:F0},{h.Y:F0} pos {h.Pos} L {h.L:F0} R {h.R:F0}: stem {(h.HasStem ? (h.Up ? "up" : "down") + $" x {h.StemX:F0} tip {h.Tip:F0}" : "none")}");
        // The rules below throw out what is no head after all. They were written against the rules'
        // own candidates (every solid blob, every hole); a head the detector is sure of is a head,
        // and the rules only check its parts (a black head with no stem found is a quarter note).
        // With the detector, what each rule dropped is counted (the page's note).
        int detected = ps.Heads.Count;
        var drops = new List<string>();
        var droppedAt = new List<string>();
        // (a rule that fails to confirm a part -- no stem, no ledger line found -- drops a detected
        // head only under the trust score; a rule that reads the ink as something else, only under
        // the sure score)
        void Drop(string why, Predicate<Head> gone, double? keep = null)
        {
            int before = ps.Heads.Count;
            ps.Heads.RemoveAll(h =>
            {
                if (h.Score >= (keep ?? SureScore) || !gone(h)) return false;
                if (h.Score > 0 && droppedAt.Count < 12) droppedAt.Add($"{h.X:F0},{h.Y:F0}");
                return true;
            });
            if (ps.Heads.Count < before) drops.Add($"{why} {before - ps.Heads.Count}");
        }
        // a black head needs a stem; a hollow one is a half (stem) or a whole (wide, no stem)
        Drop("no stem", h => !h.HasStem && (!h.Hollow || (!h.WholeShape && h.OuterW < 1.3 * s)), TrustScore);
        // the heads on one stem are all black or all hollow: a hollow "head" on a black chord's stem
        // is the hole a flag's curl closes against the stem
        // (two voices a second apart put an up stem and a down stem in one column: only one stem's
        // own heads count, those with its direction)
        // (a black head the detector is less sure of than the hollow one: an old print's small half
        // notes, their rims thick, fill in at a low resolution -- kinkel19-2's <f aes f'>2., the
        // middle head black at 0.54 under two halves at 0.70. The hollow heads stand, and the black
        // one goes hollow with them, below)
        bool OnStemWith(Head h, Head o) => o.HasStem && o.St == h.St && o.Up == h.Up && Math.Abs(o.StemX - h.StemX) < 0.3 * s && Math.Abs(o.Y - h.Y) < 4 * s;
        Drop("hollow on a black stem", h => h.Hollow && h.HasStem && ps.Heads.Any(o => !o.Hollow && o.Score >= h.Score && OnStemWith(h, o)));
        foreach (var h in ps.Heads.Where(h => !h.Hollow && h.HasStem && h.Score > 0 && h.Score < SureScore).ToList())
            if (ps.Heads.Any(o => o.Hollow && o.Score > h.Score && OnStemWith(h, o))) h.Hollow = true;
        // a whole note has nothing long above or below it: the white between two beams of a group
        // (a beam runs on past two spaces; a whole note's ring does not)
        Drop("between beams", h => !h.HasStem && h.Hollow && BeamAround(ink, h, s));
        // a time signature's digits stand in a column, each with a loop: a stemless "whole note"
        // with another loop straight above or below it is a digit. A digit stands on the staff: a
        // whole note the detector trusts on ledger lines above it, a chord name's a and d straight
        // above, is no digit (a title's g, loops over loops, is: the rules' heads drop as before);
        // and a loop that is another whole note the detector trusts, as this one, is a chord's (b d' f'')
        bool OffStaff(Head h, double y) => h.Score >= TrustScore && Math.Abs(h.St.Pos(y)) > 5;
        Drop("digit", h => !h.HasStem && !OffStaff(h, h.Y) && inkHoles.Any(o =>
            o.W > 0.3 * s && Math.Abs(o.Cx - h.X) < 0.5 * s && Math.Abs(o.Cy - h.Y) > 1.2 * s && Math.Abs(o.Cy - h.Y) < 3.0 * s
            && !OffStaff(h, o.Cy)
            && !(h.Score >= TrustScore && ps.Heads.Any(q => q != h && q.Hollow && !q.HasStem && q.Score >= TrustScore
                                                            && Math.Abs(q.X - o.Cx) < 0.4 * s && Math.Abs(q.Y - o.Cy) < 0.4 * s))));
        // a whole note stands clear of other notes: a white patch shut in between an accidental
        // and the head it belongs to, or between two voices' heads, is no whole note
        Drop("whole beside a note", h => !h.HasStem && ps.Heads.Any(o => o != h && o.HasStem && o.St == h.St
            && Math.Min(o.R, h.R) - Math.Max(o.L, h.L) > 0.3 * s && Math.Abs(o.Y - h.Y) < 1.0 * s));
        // a head beyond the staff stands on ledger lines; the notes of a metronome mark do not
        // (the rules' hollow heads added beside the detector's: their ledger lines run out past them)
        Drop("no ledger", h => !roots.Contains(h) && !HasLedgers(d, h, s, wider: ps.Detections != null && h.Score == 0), TrustScore);
        // (a head that far out, however sure, only on its seven ledger lines and more: a metronome
        // mark's note over the staff is a head too. Not the wider test: a head on a ledger line
        // has its edges read along that line, out past it)
        Drop("far, no ledger", h => h.Far && !HasLedgers(d, h, s), 2);
        // (nor a black head with no stem either, three ledger lines' way out and more: a verse's number
        // under a voice's staff, "1." read as a dotted quarter at 0.52 -- kinkel10-6's scan, its key's
        // window then shut by that "first note" left of the flats)
        Drop("no stem, no ledger", h => !h.Hollow && !h.HasStem && Math.Abs(h.Pos) >= 10 && !HasLedgers(d, h, s));
        // nor a head on a thick barline: a third of a space of columns about its middle dark unbroken from
        // the staff's top line to its bottom line (kinkel7-6's scan, the old print's :|| at a system's end,
        // twelve "heads" seen up its thick line, a pale streak down it; a chord's heads leave a gap between
        // them, and the surest stand)
        Drop("on a barline", h =>
        {
            int y0 = (int)(h.St.Top + 0.1 * s), y1 = (int)(h.St.Bottom - 0.1 * s);
            // (within the staff: a scan's dark margin past its end is left as it was read -- kinkel19-1's,
            // where it bears on the system's open end)
            if (y0 < 0 || y1 >= d.H || h.X > h.St.Right) return false;
            int run = 0, at = -1;
            for (int x = Math.Max(0, (int)Math.Round(h.X - 0.5 * s)); x <= Math.Min(d.W - 1, (int)Math.Round(h.X + 0.5 * s)) && at < 0; x++)
            {
                bool dark = true;
                for (int y = y0; y <= y1 && dark; y++) dark = d[x, y] || ink[x, y];
                run = dark ? run + 1 : 0;
                if (run >= 0.3 * s) at = x;
            }
            if (at < 0) return false;
            // (and a line's straight edge on one side all the way down, the staff lines' rows aside: a chord
            // stacked in thirds the staff's height is as dark down its middle, its heads' round edges in and
            // out -- gen-lied-all's lied002; the old print's thin line runs into the thick one here and there)
            // (the edges' spread over the middle four fifths of the rows: the staff lines blur into the rows
            // beside them)
            var ls = new List<int>(); var rs = new List<int>();
            for (int y = y0; y <= y1; y++)
            {
                int r = at, l = at;
                while (r + 1 < d.W && r + 1 - at <= 2 * s && (d[r + 1, y] || ink[r + 1, y])) r++;
                while (l - 1 >= 0 && at - (l - 1) <= 2 * s && (d[l - 1, y] || ink[l - 1, y])) l--;
                if (r - l + 1 >= 2 * s) continue;
                ls.Add(l); rs.Add(r);
            }
            if (ls.Count < 2 * s) return false;
            static int Spread(List<int> v) { v.Sort(); return v[(int)(0.9 * (v.Count - 1))] - v[(int)(0.1 * (v.Count - 1))]; }
            return Spread(rs) <= 0.2 * s || Spread(ls) <= 0.2 * s;
        });
        // a chord's ledger lines run out to its outermost head: a "head" past a surer mate on its stem,
        // with no ledger lines of its own, is something hanging under the chord (cresc.'s c, lied003).
        // Where the rules read the ink: on a scan the toner breaks a chord's outer ledger lines
        Drop("past its chord's ledgers", h => rulesRead && h.HasStem && Math.Abs(h.Pos) > 5 && !HasLedgers(d, h, s, wider: ps.Detections != null && h.Score == 0)
            && ps.Heads.Any(o => o != h && o.HasStem && o.St == h.St && Math.Abs(o.StemX - h.StemX) < 0.3 * s && o.Score > h.Score && Math.Abs(o.Pos) < Math.Abs(h.Pos)));
        // nor is any head left of where the staff begins (the loop of a "g" in an instrument name),
        // nor one that is part of the clef's own ink (a tall piece standing at the staff's start):
        // an F clef's blob, blurred, passes for a black head, and with it the clef is lost and the
        // key and time after it
        Drop("clef or left of the staff", h =>
        {
            if (h.X < h.St.Left) return true;
            // (the clef's piece is a few spaces wide; a staff whose lines were not taken out is one
            // piece from end to end, and its heads are no clef)
            // (a small page enlarged sets the clef further in, the blur paling the staff's ends: 0.8 space)
            bool ClefPiece(Comp c) => c.H >= 2.5 * s && c.W <= 4 * s && c.MinX > h.St.Left - 0.5 * s && c.MinX < h.St.Left + 1.2 * s;
            int id = LabelNear(labels, ink.W, ink.H, h.X, h.Y, s);
            if (id > 0 && id < comps.Count && ClefPiece(comps[id])) return true;
            // (taking the staff lines out can cut an F clef's blob from its tail: the blob is no tall
            // piece, but the "head" read on it reaches over the tail)
            return h.X < h.St.Left + 3 * s
                   && comps.Skip(1).Any(c => ClefPiece(c) && Math.Min(c.MaxX, h.R) - Math.Max(c.MinX, h.L) > 0.3 * s
                                             && c.MinY < h.Y + s && c.MaxY > h.Y - s);
        });
        // nor is a dynamic's letter or the pedal's mark where the detector sees one (S14: the pedal's *
        // is a round blob under the staff, a p's bowl a black head)
        if (ps.Detections != null)
        {
            // (nor an octave line's sign, S18: the 8 of an 8va is two bowls of ink a head's size; a sign
            // the detector is unsure of counts where its dashes run on from it, as Ottavas takes it)
            var ends = ps.Detections.Where(t => t.Class == "ott_end" && t.Score > Detector.Threshold).ToList();
            var marks = ps.Detections.Where(t => t.Score > Detector.Threshold && (t.Class.StartsWith("dyn_") || t.Class is "ped" or "ped_up" || Ottavas.IsSign(t)))
                .Concat((weakOttavas == null ? null : Ottavas.Pooled(weakOttavas, s))?.Where(t => t.Score > Detector.WeakOttava && Ottavas.DashesOn(d, s, t, ends)) ?? Enumerable.Empty<Detection>()).ToList();
            Drop("a dynamic or the pedal", h => marks.Any(t => Math.Abs(t.X - h.X) < (Ottavas.IsSign(t) ? Ottavas.HalfWidth(t) + 0.2 : t.Class == "ped" ? 1.6 : 0.8) * s && Math.Abs(t.Y - h.Y) < (Ottavas.IsSign(t) ? 0.9 : 0.8) * s));
            // nor an articulation (S15: a marcato's or a fermata's ink, a staccato's dot) or an ornament
            // (S23: a turn's or a mordent's bowls of ink over the staff)
            var arts = ps.Detections.Where(t => t.Score > Detector.Threshold && (t.Class.StartsWith("art_") || t.Class == "fermata" || Articulations.IsOrnament(t))).ToList();
            Drop("an articulation", h => arts.Any(t => Math.Abs(t.X - h.X) < (t.Class == "fermata" || Articulations.IsOrnament(t) ? 1.2 : 0.6) * s && Math.Abs(t.Y - h.Y) < 0.5 * s));
            // nor a letter of a rehearsal mark's words in their box over the staff (the detector's ×
            // head at the x of the sonatina's "Exposition", 0.50: a note high over the staff, and the
            // box, with a head in it, passed over as a mark). The boxes are found once here, for Tempo too
            ps.Frames = Tempo.FramesOf(d, s, systems);
            Drop("in a rehearsal mark's box", h => ps.Frames.Any(f => !f.Grid && f.Dark.Contains((int)h.X, (int)h.Y)));
        }
        if (ps.Detections != null)
            ps.Note = $"detector heads {detected}, kept {ps.Heads.Count}" + (drops.Count > 0 ? " (dropped: " + string.Join(", ", drops) + "; at " + string.Join(" ", droppedAt) + ")" : "")
                    + (hollowNote != "" ? "; " + hollowNote : "");
        ps.Heads.Sort((a, b) => a.X.CompareTo(b.X));
        Dedup(ps.Heads, s);
        // beams belong to the stem: count them from the chord's head nearest the tip (from a
        // lower head the other heads of the chord would pass for beams) and give all its heads the count
        // (twice: a stem reaches its beams by a neighbour's tip at them, which may itself have reached them first)
        for (int pass = 0; pass < 2; pass++)
            foreach (var h in ps.Heads.Where(h => h.HasStem && !h.Hollow)) ReachBeams(ink, ps.Heads, h, s);
        if (ps.Detections != null) StemsUnderBeams(ink, ps.Heads, s);
        foreach (var h in ps.Heads.Where(h => !h.Hollow && h.Score > 0 && !h.Cross && !h.Slash && !h.WholeShape).ToList())
            ShortStem(ink, ps.Heads, h, s);
        foreach (var h in ps.Heads)
        {
            if (!h.HasStem || h.Hollow) continue;
            var chord = ps.Heads.Where(o => o.St == h.St && o.HasStem && !o.Hollow && o.Up == h.Up && Math.Abs(o.StemX - h.StemX) < 0.3 * s).ToList();
            var nearest = chord.OrderBy(o => Math.Abs(o.Tip - o.Y)).First();
            h.Beams = CountBeams(ink, labels, nearest, s);
            // (and from the head whose tip reaches furthest, where that is another: a head whose stem a scan
            // broke at a staff line has a tip short of the beams, and counted from there the chord's own heads
            // pass for two beams -- kinkel7-6's scan, the left hand's chords read as sixteenths. Not where it
            // counts two beams more: counted from a far tip another voice's beams may pass for this stem's,
            // lang36-3's, one read three)
            double far = h.Up ? chord.Min(o => o.Tip) : chord.Max(o => o.Tip);
            var furthest = chord.Where(o => Math.Abs(o.Tip - far) < 0.5 * s).OrderBy(o => Math.Abs(o.Tip - o.Y)).First();
            if (furthest != nearest)
            {
                int n = CountBeams(ink, labels, furthest, s);
                if (n <= h.Beams + 1) h.Beams = n;
            }
        }
        if (ps.Detections != null) DetectedBeams(ps, s, ink);
        if (ps.Detections != null) ChordRepeats(ink, ps, staves, s);
        Tremolos(ink, ps, s);

        var clusters = new HashSet<int>();
        foreach (var h in ps.Heads)
        {
            // a head's beam group is the piece its stem is part of: the toner hollows a black head,
            // and the middle of the ring holds a speck of its own (tuplets004 on fill: a triplet
            // spanning one head of its three)
            // (the piece at the head is the head's all the same, and no loose symbol: a scan's broken
            // head left as a piece of its own reads as a rest)
            int own = LabelNear(labels, ink.W, ink.H, h.Hollow ? h.L + 0.05 * s : h.X, h.Y, s);
            int id = h.HasStem ? LabelNear(labels, ink.W, ink.H, h.StemX, (h.Y + h.Tip) / 2, s) : 0;
            if (id == 0) id = own;
            h.Cluster = id;
            if (id != 0) clusters.Add(id);
            if (own != 0) clusters.Add(own);
        }
        BeamStaves(d, ps, staves, s);
        // (a head beyond its staff on a page levelled by strips: its position from the ledger lines as
        // printed, LedgerPos. Not elsewhere: a degraded page's blurred ledgers moved chords on ledger
        // lines a position, gen-scan's chords 99.9 -> 98.8). A head inside it: from its lines beside it, LinePos
        if (d.Bowed != null) foreach (var h in ps.Heads) h.Pos = Math.Abs(h.Pos) >= 6 ? LedgerPos(d, h, s) : LinePos(d, h, s);
        // (elsewhere a detected head one or two steps past the first ledger with no ledgers running to it: it may
        // sit on that ledger, printed low -- LedgerPos's own test, and only that: where it finds a ledger, a
        // degraded page's head filled over its own second ledger hangs under the first by it, gen-scan-fill's
        // chords 99.6 -> 98.7)
        // (and not one on 8 with a chord-mate inside it in its column: the mate's ink over the first ledger hides it)
        else foreach (var h in ps.Heads.Where(h => h.Score > 0 && Math.Abs(h.Pos) is 7 or 8 && !HasLedgers(d, h, s)
                                                   && !(Math.Abs(h.Pos) == 8 && ps.Heads.Any(o => o != h && o.St == h.St && Math.Abs(o.X - h.X) < 0.6 * s && o.Pos * h.Pos > 0 && Math.Abs(o.Pos) is 6 or 7))).ToList())
            h.Pos = LedgerPos(d, h, s, onFirstOnly: true);
        var tabs = systems?.SelectMany(x => x.All).Where(x => x.Kind == "tab").ToList() ?? new List<Staff>();
        // (a piece holding a head is no loose symbol -- unless the detector sees a clef inside the staff on
        // it, past the staff's start: a G clef's loop read as a head, the piece then kept for Signatures.MidClefs)
        var midClefs = det == null ? new List<Detection>() : det.Concat(ps.WeakClefs)
            .Where(t => t.Class.StartsWith("clef_") && t.Score >= Detector.WeakClef && staves.Any(st => t.X > st.Left + 6 * s && Math.Abs(t.Y - st.Mid) < 2 * s)).ToList();
        bool OnMidClef(Comp c) => midClefs.Any(t => t.X > c.MinX && t.X < c.MaxX && t.Y > c.MinY && t.Y < c.MaxY);
        var beatIds = BeatRepeats(labels, comps, clusters, ps, staves, s);
        if (Signatures.Verbose)
            foreach (var t in midClefs)
            {
                int id = LabelNear(labels, ink.W, ink.H, t.X, t.Y, s);
                Console.WriteLine($"    clef {t.Class} inside at {t.X:F0},{t.Y:F0} ({t.Score:F2}): the piece there {(id > 0 && id < comps.Count ? $"x {comps[id].MinX}-{comps[id].MaxX} y {comps[id].MinY}-{comps[id].MaxY}{(clusters.Contains(id) ? ", a head's" : "")}" : "none")}");
            }
        for (int i = 1; i < comps.Count; i++)
        {
            var c = comps[i];
            if (clusters.Contains(i) && !(c.W < 60 * s && c.H < 60 * s && OnMidClef(c))) continue;
            if (c.W < 0.15 * s && c.H < 0.15 * s) continue; // specks
            // (nor a scan's black border, one piece round the page: kinkel19-2's, 137 spaces wide and
            // 173 tall at a fill of 0.02, boxed in every symbol of the page and, named for the clef it
            // ran past, made every natural and flat the detector saw "a clef's piece". No symbol, no
            // brace, no slur is 20 spaces both ways)
            // (but a clef the detector sees inside a staff may be one piece with the arpeggio and chords beside it, 34 by 23 spaces: kinkel7-1)
            if (c.W > 20 * s && c.H > 20 * s && !(c.W < 60 * s && c.H < 60 * s && OnMidClef(c))) continue;
            var st = Nearest(staves, c.Cy, s);
            if (st == null) continue;
            if (c.MaxX < st.Left || c.MinX > st.Right) continue;
            // (a TAB's fret numbers are no staff's: a bass TAB's 3 read as a triplet on the drums below it)
            if (tabs.Any(t => c.Cy > t.Top - 0.3 * s && c.Cy < t.Bottom + 0.3 * s && c.Cx > t.Left && c.Cx < t.Right)) continue;
            if (ps.RepeatSlashes.Any(b => c.Cx > b.L && c.Cx < b.R && c.Cy > b.T && c.Cy < b.B)) continue;
            if (beatIds.Contains(i)) continue;
            ps.Others.Add(new Other { C = c, St = st });
        }
        Signatures.Detect(ps, ink, inkHoles, systems, s, clefsRead == null ? null : () => clefsRead(ps));
        FindTies(ps, d, ink, s);
        if (ps.Detections != null) DetectedTies(ps, s);
        Classify(ps, d, ink, systems, s);
        // a unison's sign is both voices' (it was given to one of the two heads)
        foreach (var h in ps.Heads.Where(h => h.Acc != null))
            foreach (var o in ps.Heads.Where(o => o != h && o.Acc == null && o.St == h.St && o.Pos == h.Pos && Math.Abs(o.X - h.X) < 0.3 * s && o.HasStem && h.HasStem && o.Up != h.Up))
                o.Acc = h.Acc;
        // (after the dots are given: a slur leaving a dotted note starts past its dot)
        if (ps.Detections != null) DetectedSlurs(ps, s);
        FindTuplets(ps, d, inkHoles, s);
        return ps;
    }

    /// <summary>Width of a hole's white run through row y (the longest run inside its box).</summary>
    static int RowWidth(Img d, Comp c, int y)
    {
        int best = 0, run = 0;
        for (int x = c.MinX; x <= c.MaxX; x++)
        {
            if (!d[x, y]) { run++; best = Math.Max(best, run); }
            else run = 0;
        }
        return best;
    }

    /// <summary>y lies on a staff line or where a ledger line would be (an even position).</summary>
    static bool OnLine(Staff st, double y, double s)
    {
        double p = st.Pos(y);
        int k = (int)Math.Round(p / 2) * 2;
        return Math.Abs(y - (st.Mid - k * st.S / 2)) <= 0.15 * s;
    }

    /// <param name="wider">the ledger lines must also run out past the head's edges, as engravers draw
    /// them (Lily# half a space each side, SMuFL 0.4): a letter's strokes do not (a chord name's B
    /// above a vocal staff, its bowls taken for hollow heads and its bars for ledger lines)</param>
    static bool HasLedgers(Img d, Head h, double s, bool wider = false)
    {
        int p = h.Pos;
        if (Math.Abs(p) < 6) return true;
        int sign = Math.Sign(p);
        bool Inked(int x0, int x1, int yy)
        {
            int c = 0, n = 0;
            for (int x = x0; x <= x1; x++, n++) if (d[x, yy]) c++;
            return c >= 0.8 * n;
        }
        for (int k = 6; k <= Math.Abs(p); k += 2)
        {
            double y = h.St.Mid - sign * k * h.St.S / 2;
            bool found = false;
            // (the window: a fifth of a space, three rows at least -- a page bowed towards its binding and
            // levelled by strips has the ledgers between its staves a quarter of a space off their places)
            int win = d.Bowed != null ? Math.Max(3, (int)(0.2 * s)) : 3;
            // (on the head's own row, the head's own width: a head in a space rounded onto the line
            // beside it -- the bowed page's gap stretched -- has no line through it, only its ink)
            bool own = k == Math.Abs(p);
            int x0 = own ? (int)(h.R > h.L ? h.L : h.X - 0.55 * s) : (int)(h.X - 0.7 * s), x1 = own ? (int)(h.R > h.L ? h.R : h.X + 0.55 * s) : (int)(h.X + 0.7 * s);
            for (int yy = (int)y - win; yy <= (int)y + win && !found; yy++)
                found = Inked(x0, x1, yy)
                        && (!wider || Inked((int)(h.L - 0.3 * s), (int)(h.L - 0.1 * s), yy) && Inked((int)(h.R + 0.1 * s), (int)(h.R + 0.3 * s), yy))
                        // (a ledger line is thin: a beam of the other staff's notes through the gap, half a
                        // space thick, is no ledger line -- kinkel7-1's left-hand chords climbing into the
                        // right hand's beams, their top heads given to the right hand. Not on the head's own
                        // row or at its edge, half a space off, where the head's ink is, or a chord-mate's)
                        && (Math.Abs(p) - k < 2 || Thin(yy));
            if (!found) return false;
        }
        return true;
        // whether the ink through the row is a line's: beyond the head's width on either side (a ledger
        // line reaches a third of a space past the head; a chord-mate on the row, or touching the line,
        // and the stem stand within), the run of dark rows there is no thicker than a third of a space.
        // A beam is thick on both sides; a side with no ink says nothing
        bool Thin(int yy) => LedgerLike(d, h.X, yy, s, h.L, h.R);
    }

    /// <summary>
    /// Whether the ink through the row yy about the head at x is a ledger line's and not a beam's: beyond
    /// the head's width on either side (a ledger line reaches a third of a space past the head), the run
    /// of dark rows there is a line's, a third of a space and under, on one side at least. A beam is
    /// thick on both sides, a third to three quarters of a space; a run taller than that is a head's (a
    /// second's other head stands beside the row) or a stem's, and says nothing; a side with no ink says
    /// nothing either.
    /// </summary>
    /// <param name="l">the head's extent, when known (a whole note is 1.7 spaces wide: beyond its own width, not a usual head's), else 0</param>
    /// <param name="overhang">the line must be seen past the head on one side: a row with no ink on either side is the head's own</param>
    static bool LedgerLike(Img d, double x, int yy, double s, double l = 0, double r = 0, bool overhang = false)
    {
        double? left = r > l ? LineThickness(d, (int)(l - 0.35 * s), (int)(l - 0.1 * s), yy) : LineThickness(d, (int)(x - 0.9 * s), (int)(x - 0.65 * s), yy);
        double? right = r > l ? LineThickness(d, (int)(r + 0.1 * s), (int)(r + 0.35 * s), yy) : LineThickness(d, (int)(x + 0.65 * s), (int)(x + 0.9 * s), yy);
        bool Thin(double? t) => t != null && t <= 0.35 * s;
        bool Beam(double? t) => t > 0.35 * s && t <= 0.75 * s;
        if (Thin(left) || Thin(right)) return true;
        if (Beam(left) && (Beam(right) || right == null) || Beam(right) && (Beam(left) || left == null)) return false;
        return !overhang;
    }

    /// <summary>The ink's thickness through the row over the columns x0..x1: the median of the run of dark rows there (null: no ink).</summary>
    static double? LineThickness(Img d, int x0, int x1, int yy)
    {
        var runs = new List<int>();
        for (int x = Math.Max(0, x0); x <= x1 && x < d.W; x++)
        {
            if (!d[x, yy]) continue;
            int a = yy, b = yy;
            while (a - 1 >= 0 && d[x, a - 1]) a--;
            while (b + 1 < d.H && d[x, b + 1]) b++;
            runs.Add(b - a + 1);
        }
        if (runs.Count == 0) return null;
        runs.Sort();
        return runs[runs.Count / 2];
    }

    /// <summary>
    /// The position of a head beyond the staff counted from its ledger lines as printed. On a page bowed
    /// towards its binding and levelled by strips (Img.Warp) the ledgers between the staves stand a
    /// quarter of a space off the staff's grid, and a head's position counted from the staff's lines
    /// comes out one off (kinkel7-1's left-hand chords in the gap over the staff, <g, d f> read
    /// <g, e g>). From the staff outward, each ledger line's row is looked for about its place, a third
    /// of a space either way from the last one found (a thin line across the head's width, as
    /// HasLedgers takes it); the head's position is then counted from the nearest ledger found, in the
    /// staff's half-spaces. With no ledger found, the position stands. On a clean page the ledgers
    /// stand on the grid and nothing changes.
    /// </summary>
    /// <param name="onFirstOnly">only the head on the first ledger, none found at its place: where a ledger is
    /// found the position stands</param>
    static int LedgerPos(Img d, Head h, double s, bool onFirstOnly = false)
    {
        int p = h.Pos;
        if (Math.Abs(p) < 6 || h.St == null || h.St.OneLine) return p;
        int sign = Math.Sign(p);
        double half = h.St.S / 2;
        double lastY = h.St.Mid - sign * 4 * half;
        int lastK = 4, x0 = (int)(h.X - 0.7 * s), x1 = (int)(h.X + 0.7 * s);
        for (int k = 6; k <= Math.Abs(p) + 2; k += 2)
        {
            double expect = lastY - sign * (k - lastK) * half;
            int bestRow = -1;
            double bestOff = double.MaxValue;
            for (int yy = (int)(expect - 0.33 * s); yy <= (int)(expect + 0.33 * s); yy++)
            {
                if (yy < 1 || yy >= d.H - 1) continue;
                int c = 0, n = 0;
                for (int x = Math.Max(0, x0); x <= x1 && x < d.W; x++, n++) if (d[x, yy]) c++;
                if (c < 0.8 * n) continue;
                // (a line's thickness beyond the head's width, on the side clear of the stem: a beam, or the
                // head's own row, or a line run into a chord-mate's head, is none; nor is a row with nothing
                // past the head -- kinkel10-4's scan, a head hung under its ledger read one lower by its own middle)
                if (!LedgerLike(d, h.X, yy, s, h.L, h.R, overhang: true)) continue;
                // (the line's middle row, measured where it is thin)
                double xl = h.R > h.L ? h.L - 0.2 * s : h.X - 0.8 * s, xr = h.R > h.L ? h.R + 0.2 * s : h.X + 0.8 * s;
                double? left = LineThickness(d, (int)xl, (int)xl, yy);
                int xm = left <= 0.35 * s ? (int)xl : (int)xr, a = yy, b = yy;
                while (a - 1 >= 0 && d[xm, a - 1]) a--;
                while (b + 1 < d.H && d[xm, b + 1]) b++;
                // (not where the column runs on far past a line's thickness: the head's own stem beside it,
                // kinkel19-1's scan, an up-stem's whole length taken for the line, its middle four steps up)
                if (b - a > 0.5 * s) { a = b = yy; }
                double off = Math.Abs((a + b) / 2.0 - expect);
                if (off < bestOff) { bestOff = off; bestRow = (a + b) / 2; }
            }
            if (bestRow < 0) break;
            lastY = bestRow; lastK = k;
        }
        if (lastK == 4)
        {
            // (no ledger found about its place, and a head a step past it by the grid with a line through its
            // own middle: the head sits on that first ledger, drawn low -- lang36-3's scan, the bass's ees,,8
            // hung under the other voice's beams, its top run into them, read d,,8)
            // Two steps past it too, when no line stands between the staff and the head (a head on the second
            // ledger has the first between: lang36-3's scan, the bass's ees,,8 in bar 19 and its ledger printed
            // 0.6 space low, the head 7.5 steps under the middle line, read c,,8)
            if ((Math.Abs(p) == 7 || Math.Abs(p) == 8 && NoLineBetween())
                && Enumerable.Range(-2, 5).Any(dy => LedgerLike(d, h.X, (int)h.Y + dy, s, h.L, h.R, overhang: true) && OwnRowThin((int)h.Y + dy)))
            {
                if (DebugAt.HasValue && Math.Abs(h.X - DebugAt.Value.X) < 2 * s && Math.Abs(h.Y - DebugAt.Value.Y) < 3 * s)
                    Console.WriteLine($"head at {h.X:F0},{h.Y:F0}: pos {p} by the staff's grid, {sign * 6} on its own ledger (none found at its place)");
                return sign * 6;
            }
            return p;
        }
        // (no row between the staff's outer line and the head's edge is a line's across the head's width: inked
        // there, a third of a space thick and under -- a beam runs thicker. Not by LedgerLike: the line's ends
        // past the head are a stem's and nothing, kinkel19-5's bes'' on its second ledger. The row with those
        // beside it: a scan's specks break a line into rows none of which runs the width, the printed scan of
        // ledger007's b,,16 on its second ledger, the first broken to 77 percent)
        bool NoLineBetween()
        {
            int from = (int)(h.St.Mid - sign * (4 * half + 0.3 * s)), to = (int)(h.Y + sign * 0.45 * s);
            for (int yy = Math.Min(from, to); yy <= Math.Max(from, to); yy++)
            {
                if (yy < 2 || yy >= d.H - 2) continue;
                int c = 0, n = 0;
                for (int x = Math.Max(0, x0); x <= x1 && x < d.W; x++, n++) if (d[x, yy - 1] || d[x, yy] || d[x, yy + 1]) c++;
                if (c >= 0.8 * n && LineThickness(d, x0, x1, yy) <= 0.35 * s) return false;
            }
            return true;
        }
        // (the line seen past the head on both sides, thin)
        bool OwnRowThin(int yy) => h.R > h.L
            && LineThickness(d, (int)(h.L - 0.35 * s), (int)(h.L - 0.1 * s), yy) is double l && l <= 0.35 * s
            && LineThickness(d, (int)(h.R + 0.1 * s), (int)(h.R + 0.35 * s), yy) is double r && r <= 0.35 * s;
        if (onFirstOnly) return p;
        int at = (int)Math.Round(sign * lastK + (lastY - h.Y) / half);
        // (a head on the next ledger out would have shown that ledger through it, and it was looked for there
        // and not found: the head hangs just past the last one -- a scan's head drawn low, its middle more than
        // half a step past)
        if (Math.Abs(at) == lastK + 2 && lastK <= Math.Abs(p)) at = sign * (lastK + 1);
        if (at != p && DebugAt.HasValue && Math.Abs(h.X - DebugAt.Value.X) < 2 * s && Math.Abs(h.Y - DebugAt.Value.Y) < 3 * s)
            Console.WriteLine($"head at {h.X:F0},{h.Y:F0}: pos {p} by the staff's grid, {at} by its ledger lines (the ledger {lastK} at y {lastY:F0})");
        return at;
    }

    /// <summary>
    /// The position of a head inside the staff counted from the staff's lines where they run beside it.
    /// On a page bowed towards its binding and levelled by strips (Img.Warp) the lines still drift a
    /// row or several along the staff, and the staff's lines, one level row each, stand off them here and
    /// there: lang36-3's scan, the left hand's lines 5 rows under the staff's rows at its right end, and a
    /// bes,, on the second line, drawn a little low, read aes,,. Each line's row is looked for in the
    /// columns within two spaces of the head, clear of it, a third of a space either way of the staff's
    /// row (a line's run, a third of a space thick and under: a beam's, a head's or a stem's run taller);
    /// the median of how far they stand off is the staff's drift here. With few columns seen the position
    /// stands.
    /// </summary>
    static int LinePos(Img d, Head h, double s)
    {
        int p = h.Pos;
        if (h.St == null || h.St.OneLine || h.St.Lines.Length != 5) return p;
        var offs = new List<double>();
        int hl = (int)((h.R > h.L ? h.L : h.X - 0.6 * s) - 0.1 * s), hr = (int)((h.R > h.L ? h.R : h.X + 0.6 * s) + 0.1 * s);
        for (int x = (int)(h.X - 2 * s); x <= (int)(h.X + 2 * s); x++)
        {
            if (x >= hl && x <= hr || x < h.St.Left || x > h.St.Right || x < 0 || x >= d.W) continue;
            foreach (var line in h.St.Lines)
            {
                double best = double.MaxValue;
                for (int yy = (int)(line.Y - 0.33 * s); yy <= (int)(line.Y + 0.33 * s); yy++)
                {
                    if (yy < 1 || yy >= d.H - 1 || !d[x, yy] || d[x, yy - 1]) continue;
                    int b = yy;
                    while (b + 1 < d.H && d[x, b + 1]) b++;
                    if (b - yy + 1 > 0.35 * s) continue;
                    double off = (yy + b) / 2.0 - line.Y;
                    if (Math.Abs(off) < Math.Abs(best)) best = off;
                }
                if (best != double.MaxValue) offs.Add(best);
            }
        }
        if (offs.Count < 10) return p;
        offs.Sort();
        double drift = offs[offs.Count / 2];
        int at = (int)Math.Round(h.St.Pos(h.Y - drift));
        if (at != p && DebugAt.HasValue && Math.Abs(h.X - DebugAt.Value.X) < 2 * s && Math.Abs(h.Y - DebugAt.Value.Y) < 3 * s)
            Console.WriteLine($"head at {h.X:F0},{h.Y:F0}: pos {p} by the staff's grid, {at} by its lines beside it (they stand {drift:F1} rows off, {offs.Count} seen)");
        return Math.Abs(at) < 6 ? at : p;
    }

    /// <summary>
    /// The staff of a head at (x, y) between two staves, by its ledger lines: a head beyond its nearest
    /// staff's lines (st) with no ledger line running to that staff, and ledger lines all the way to the
    /// next staff on its other side (within twelve spaces), is that staff's. Two ledgers under one staff
    /// can be two or six over the next, as the systems stand: a lied's left hand climbing two ledgers over
    /// its staff, 3.8 spaces under the right hand's (lied010); basic001's des''' on six ledgers over its
    /// staff, under a 15ma, which the system above read as an aes,8 on two. The truth (SymEval) places
    /// Lily#'s heads the same way, so that the two agree on the staff. Returns st when nothing says otherwise.
    /// </summary>
    public static Staff ByLedgers(Img d, List<Staff> staves, Staff st, double x, double y, double s)
    {
        var probe = new Head { St = st, X = x, Y = y, Pos = (int)Math.Round(st.Pos(y)) };
        bool debug = DebugAt.HasValue && Math.Abs(x - DebugAt.Value.X) < 2 * s && Math.Abs(y - DebugAt.Value.Y) < 3 * s;
        if (Math.Abs(probe.Pos) < 6 || HasLedgers(d, probe, s)) { if (debug) Console.WriteLine($"by ledgers? head at {x:F0},{y:F0}: pos {probe.Pos} on the staff at {st.Top:F0}{(Math.Abs(probe.Pos) < 6 ? ", inside it" : ", its ledgers run to it")}"); return st; }
        bool above = y < st.Mid;
        var other = staves.Where(o => o != st && o.Kind == "" && (above ? o.Mid < y && y - o.Bottom < 12 * s : o.Mid > y && o.Top - y < 12 * s))
            .OrderBy(o => Math.Abs(o.Mid - y)).FirstOrDefault();
        if (other == null) { if (debug) Console.WriteLine($"by ledgers? head at {x:F0},{y:F0}: pos {probe.Pos} on the staff at {st.Top:F0}, no ledgers to it, no staff on the other side"); return st; }
        var there = new Head { St = other, X = x, Y = y, Pos = (int)Math.Round(other.Pos(y)) };
        bool theirs = Math.Abs(there.Pos) >= 6 && HasLedgers(d, there, s);
        if (debug) Console.WriteLine($"by ledgers? head at {x:F0},{y:F0}: pos {probe.Pos} on the staff at {st.Top:F0}, no ledgers to it; pos {there.Pos} on the staff at {other.Top:F0}, {(theirs ? "its ledgers run to it" : "no ledgers to it either")}");
        return theirs ? other : st;
    }

    public static Staff Nearest(List<Staff> staves, double y, double s, double reach = 6.5)
    {
        Staff best = null;
        double bd = double.MaxValue;
        foreach (var st in staves)
        {
            double dd = Math.Abs(st.Mid - y);
            if (dd < bd) { bd = dd; best = st; }
        }
        if (best == null || y < best.Top - reach * s || y > best.Bottom + reach * s) return null;
        return best;
    }

    static int LabelNear(int[] labels, int w, int h, double x, double y, double s)
    {
        int cx = (int)Math.Round(x), cy = (int)Math.Round(y), r = (int)(0.3 * s);
        for (int rr = 0; rr <= r; rr++)
            for (int dy = -rr; dy <= rr; dy++)
                for (int dx = -rr; dx <= rr; dx++)
                {
                    int px = cx + dx, py = cy + dy;
                    if ((uint)px >= (uint)w || (uint)py >= (uint)h) continue;
                    int l = labels[py * w + px];
                    if (l != 0) return l;
                }
        return 0;
    }

    /// <summary>
    /// A head between two staves goes to the staff its beam group is on. Heads are given the
    /// nearest staff, but a grand staff's left hand climbs on ledger lines towards the right hand:
    /// three ledgers over the bass staff is two and a half spaces under the treble staff (a ballade's
    /// D-flat arpeggio: ges' read on the treble staff as bes,). The beam says whose it is: a head
    /// outside its staff, on the side of the other, alone on its stem, whose beam group's other heads
    /// (two or more) all stand on that other staff, with
    /// stems the same way and tips along one beam, and whose ledger lines run to that staff, not to
    /// its own, is that staff's. So is a head beamed to none whose stem points to the next staff and
    /// whose ledgers run to it. A head inside its staff's lines stays, and so does a head with its
    /// ledgers on its own side: a beam across the staves, a lower voice under its staff.
    /// </summary>
    static void BeamStaves(Img d, PageSymbols ps, List<Staff> staves, double s)
    {
        foreach (var h in ps.Heads.Where(h => h.HasStem && h.St.Kind == "" && (h.Y < h.St.Top - 0.25 * s || h.Y > h.St.Bottom + 0.25 * s)).ToList())
        {
            var mates = h.Cluster == 0 ? new List<Head>() : ps.Heads.Where(o => o != h && o.Cluster == h.Cluster && Math.Abs(o.StemX - h.StemX) >= 0.3 * s).ToList();
            Staff other;
            if (mates.Count >= 2)
            {
                // (a beam's: stems the same way, their tips along it -- not a stem that touches another voice's
                // ink, BWV 610's alto under the treble staff whose stem meets the tenor's beam)
                if (mates.Any(o => !o.HasStem || o.Up != h.Up || Math.Abs(o.Tip - h.Tip) > 2.5 * s)) continue;
                other = mates[0].St;
                if (mates.Any(o => o.St != other)) continue;
            }
            // a head beamed to none: the staff its stem points to, next to its own (the ballade's
            // bar 12, a dotted quarter four ledgers over the bass staff, 1.7 spaces under the treble)
            else if (mates.Count == 0)
                other = staves.Where(o => o.Kind == "" && (h.Up ? o.Mid < h.St.Mid : o.Mid > h.St.Mid) && Math.Abs(o.Mid - h.St.Mid) < 16 * s)
                              .OrderBy(o => Math.Abs(o.Mid - h.St.Mid)).FirstOrDefault();
            else continue;
            if (other == null || other == h.St || other.Kind != "") continue;
            // (on the other's side of its staff, short of the other's lines: in the gap between them)
            bool below = other.Mid > h.St.Mid;
            if (below ? !(h.Y > h.St.Bottom && h.Y < other.Top) : !(h.Y < h.St.Top && h.Y > other.Bottom)) continue;
            // (the ledger lines say which staff it hangs from: they run from a head to its own staff. A
            // beam across the staves joins a head to the other staff's beam with its ledgers on its own
            // side, lied010's right hand to the left hand's beam; the ballade's climb has them on the bass side)
            var there = new Head { St = other, X = h.X, Y = h.Y, L = h.L, R = h.R, Pos = (int)Math.Round(other.Pos(h.Y)) };
            if (HasLedgers(d, h, s) || !HasLedgers(d, there, s)) continue;
            // (a head on a stem with others keeps its staff: a chord with a head inside its staff, or one
            // whose heads stand on both staves, lied010's right-hand note on the left hand's chord, as written)
            // (on its stem: one head between the other and that one's tip -- not a stem in the same column that
            // ends short of it, the ballade's bar 15, the right hand's <dis' a'> over the left hand's gis)
            static bool Along(double y, Head o, double m) => y >= Math.Min(o.Y, o.Tip) - m && y <= Math.Max(o.Y, o.Tip) + m;
            if (ps.Heads.Any(o => o != h && o.HasStem && o.Up == h.Up && Math.Abs(o.StemX - h.StemX) < 0.3 * s && (Along(h.Y, o, 0.5 * s) || Along(o.Y, h, 0.5 * s)))) continue;
            if (DebugAt.HasValue && Math.Abs(h.X - DebugAt.Value.X) < 2 * s && Math.Abs(h.Y - DebugAt.Value.Y) < 3 * s)
                Console.WriteLine($"head at {h.X:F0},{h.Y:F0}: to the staff its beam group is on ({mates.Count} heads)");
            h.Was = h.St;
            h.St = other;
            h.Pos = (int)Math.Round(other.Pos(h.Y));
        }
    }

    /// <summary>
    /// Whether the ink about (x, y) is a rhythm slash's: one piece whose box holds the point, a
    /// slash's size (1.5 to 3.5 spaces wide, 1.5 to 5.5 tall: a stem may hang from it), the ink
    /// of its top rows standing right of its bottom rows' by half a space or more (a slash leans
    /// up to the right; its stem hangs from the lower left corner). Of several such pieces, the largest.
    /// </summary>
    static bool SlashInk(int[] labels, List<Comp> comps, int w, double x, double y, double s)
    {
        Comp best = null;
        foreach (var c in comps)
        {
            if (c == null || x < c.MinX - 0.2 * s || x > c.MaxX + 0.2 * s || y < c.MinY - 0.2 * s || y > c.MaxY + 0.2 * s) continue;
            if (c.W < 1.5 * s || c.W > 3.5 * s || c.H < 1.5 * s || c.H > 5.5 * s) continue;
            if (best == null || c.N > best.N) best = c;
        }
        if (best == null) return false;
        int rows = Math.Max(1, (int)(0.3 * s));
        double Mean(int y0, int y1)
        {
            double sx = 0; int n = 0;
            for (int yy = y0; yy <= y1; yy++)
                for (int xx = best.MinX; xx <= best.MaxX; xx++)
                    if (labels[yy * w + xx] == best.Id) { sx += xx; n++; }
            return n == 0 ? double.NaN : sx / n;
        }
        double top = Mean(best.MinY, best.MinY + rows), bot = Mean(best.MaxY - rows, best.MaxY);
        return !double.IsNaN(top) && !double.IsNaN(bot) && top - bot >= 0.5 * s;
    }
}
