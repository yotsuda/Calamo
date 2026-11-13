namespace Omr;

static partial class Symbols
{
    /// <summary>
    /// A tuplet number sits outside the staff, taller than a bar number. Its bracket's two halves,
    /// if any, give the span; otherwise it spans the beam group nearest to it.
    /// </summary>
    /// <summary>
    /// Whether a tuplet number is a 2 (the detector knows one class for every number): its foot a
    /// straight stroke, its lowest two rows inked across two thirds of its width or more from its left
    /// corner, where a 3's foot is a curve, short at the bottom. A row a staff or ledger line runs
    /// through (ink past both sides of the number) is passed over.
    /// </summary>
    static bool IsTwo(Img d, List<Comp> own, double s)
    {
        int x0 = own.Min(p => p.MinX), x1 = own.Max(p => p.MaxX), y0 = own.Min(p => p.MinY), y1 = own.Max(p => p.MaxY);
        int w = x1 - x0 + 1, h = y1 - y0 + 1, pad = Math.Max(2, (int)(0.3 * s));
        if (w < 3 || h < 5) return false;
        bool Line(int y) => d[x0 - pad, y] && d[x1 + pad, y];
        // (the lowest two rows of ink: a 2's foot fills them; a 3's lowest curve, blurred, only its widest rows above)
        var rows = Enumerable.Range(y0, h).Reverse().Where(y => !Line(y) && Enumerable.Range(x0, w).Any(x => d[x, y])).Take(2).ToList();
        if (rows.Count == 0) return false;
        double across = rows.Average(y => Enumerable.Range(x0, w).Count(x => d[x, y]) / (double)w);
        bool corner = rows.Any(y => Enumerable.Range(x0, Math.Max(1, w / 4)).Any(x => d[x, y]));
        // (the ballade's 2: 0.81; a serif 3 blurred by the toner, its curl in the corner, 0.46 at most: tuplets004 on fill)
        if (TupletVerbose) Console.WriteLine($"      2? foot {across:F2} corner {corner}");
        return across >= 0.65 && corner;
    }

    static void FindTuplets(PageSymbols ps, Img d, List<Comp> holes, double s)
    {
        // a number on a staff line comes apart where the line is removed: join stacked pieces
        var free = ps.Others.Where(o => o.Class == "?" || o.Class == "dot").OrderBy(o => o.C.MinX).ToList();
        // (each staff's pieces, in the same order: a scan's specks are thousands of pieces)
        var onStaff = free.Where(o => o.St != null).GroupBy(o => o.St).ToDictionary(g => g.Key, g => g.ToList());
        var noStaff = free.Where(o => o.St == null).ToList();
        var numbers = new List<Other>();
        var taken = new HashSet<Other>();
        int tupletId = 0;
        foreach (var o in free)
        {
            if (taken.Contains(o)) continue;
            var box = new Comp { MinX = o.C.MinX, MaxX = o.C.MaxX, MinY = o.C.MinY, MaxY = o.C.MaxY, N = o.C.N, Sx = o.C.Sx, Sy = o.C.Sy };
            var members = new List<Other> { o };
            foreach (var q in o.St == null ? noStaff : onStaff[o.St])
            {
                // (in order of their left edges: one starting past the box overlaps none, nor do those after it)
                if (q.C.MinX >= box.MaxX) break;
                if (q == o || taken.Contains(q)) continue;
                int ov = Math.Min(box.MaxX, q.C.MaxX) - Math.Max(box.MinX, q.C.MinX);
                int gapY = Math.Max(q.C.MinY - box.MaxY, box.MinY - q.C.MaxY);
                if (ov > 0.5 * Math.Min(box.W, q.C.W) && gapY < 0.3 * s && Math.Max(box.MaxY, q.C.MaxY) - Math.Min(box.MinY, q.C.MinY) < 1.6 * s)
                {
                    box.MinX = Math.Min(box.MinX, q.C.MinX); box.MaxX = Math.Max(box.MaxX, q.C.MaxX);
                    box.MinY = Math.Min(box.MinY, q.C.MinY); box.MaxY = Math.Max(box.MaxY, q.C.MaxY);
                    box.N += q.C.N; box.Sx += q.C.Sx; box.Sy += q.C.Sy;
                    members.Add(q);
                }
            }
            var joined = new Other { C = box, St = o.St, Class = "?" };
            if (members.Count > 1) { foreach (var q in members) taken.Add(q); pieceOf[joined] = members; }
            numbers.Add(joined);
        }
        // With the learnt detector (--detector), its triplet numbers decide which pieces are
        // numbers: a piece with one of them on it is a number whatever its shape (on a scan the
        // digit breaks and fits no rule's proportions); a piece with none is not (a stray mark or a
        // bracket's hook read as a number put false triplets on every scan); and a number the
        // detector sees where the rules have no piece becomes one of its own.
        var sure = new HashSet<Other>();
        var detValue = new Dictionary<Other, int>();     // the number the detector read on a sure piece
        var threeByNotes = new HashSet<Other>();         // a piece the detector passed by, a 3 by the unbeamed three under it
        if (ps.Detections != null)
        {
            if (TupletVerbose)
                foreach (var t in ps.Detections.Where(t => t.Class.StartsWith("tuplet_")))
                    Console.WriteLine($"    {t.Class} detected at {t.X:F0},{t.Y:F0} score {t.Score:F2}");
            // (the class has few training samples and scores a true number as low as 0.7: any
            // detection confirms a piece the rules hold for a number; only a sure one makes a number
            // of its own; and only a page the detector was sure of a number on may lose the rules'
            // unconfirmed ones -- in another font (Fugue 1, Bohemian) it may know none of them)
            // The detector's classes are the numbers 3, 5, 6 and 7 (a 2 it sees as a 3: IsTwo tells
            // them apart); the surest detection on a piece says which it is
            var seen = ps.Detections.Where(t => t.Class.StartsWith("tuplet_")).ToList();
            bool On(Other o, Detection t) => Math.Abs(o.C.Cx - t.X) < 0.7 * s && Math.Abs(o.C.Cy - t.Y) < 0.7 * s;
            static int ValueOf(Detection t) => int.Parse(t.Class[7..]);
            foreach (var o in numbers)
                if (seen.Where(t => On(o, t)).OrderByDescending(t => t.Score).FirstOrDefault() is Detection on) { sure.Add(o); detValue[o] = ValueOf(on); }
            var staves = ps.Heads.Select(h => h.St).Distinct().ToList();
            bool competent = seen.Any(t => t.Score >= SureTuplet);
            // (a less sure one too where its number is one piece with a slur that touches it, the
            // piece a wide arc of little ink: slurs006, the slur under a triplet through its 3)
            bool OnArc(Detection t) => ps.Others.Any(o => o.Class == "?" && o.C.W > 2.5 * s && o.C.Fill < 0.3
                && t.X > o.C.MinX && t.X < o.C.MaxX && t.Y > o.C.MinY && t.Y < o.C.MaxY);
            foreach (var t in seen.Where(t => t.Score >= SureTuplet || t.Score >= 0.8 && OnArc(t)))
            {
                if (numbers.Any(o => On(o, t))) continue;
                var st = Nearest(staves, t.Y, s);
                if (st == null) continue;
                if (TupletVerbose) Console.WriteLine($"    tuplet number detected with no piece at {t.X:F0},{t.Y:F0} ({t.Score:F2}): a number of its own");
                var c = new Comp { MinX = (int)(t.X - 0.45 * s), MaxX = (int)(t.X + 0.45 * s), MinY = (int)(t.Y - 0.7 * s), MaxY = (int)(t.Y + 0.7 * s), N = 1, Sx = (long)t.X, Sy = (long)t.Y };
                var o = new Other { C = c, St = st, Class = "?" };
                numbers.Add(o); sure.Add(o); detValue[o] = ValueOf(t);
            }
            if (competent)
            {
                // (but a bracket's two halves either side of it say what it is: Bravura's italic 3
                // the detector passes by, meters009)
                bool Bracketed(Other o) { var (l, r) = BracketHalves(ps, d, o, s); return l != null && r != null; }
                if (TupletVerbose)
                    foreach (var o in numbers.Where(o => !sure.Contains(o) && o.C.H > 1.0 * s && o.C.H < 1.8 * s && o.C.W > 0.5 * s && o.C.W < 1.3 * s))
                        Console.WriteLine($"    tuplet? piece at {o.C.Cx:F0},{o.C.Cy:F0} {o.C.W / s:F2}x{o.C.H / s:F2}: no detection on it, {(Bracketed(o) ? "kept: bracketed" : "dropped")}");
                // (nor does an old print's 3 over the middle of three unbeamed notes under a slur, no bracket, standing
                // on the middle note's stem tip: the detector, taught Lily#'s bracketed unbeamed triplets only, passes
                // it by -- Holmes's No.2 scan, the voice's quarters in bars 39, 43, 51 and 69. The notes say what
                // it is: an unbeamed stem up under it, a neighbour on either side as far off (Unbeamed3), a slur
                // starting by the first)
                foreach (var o in numbers.Where(o => !sure.Contains(o) && OverUnbeamed3(o)))
                {
                    if (TupletVerbose) Console.WriteLine($"    tuplet? piece at {o.C.Cx:F0},{o.C.Cy:F0}: no detection on it, kept: a 3 on the stem tip of an unbeamed three under a slur");
                    sure.Add(o); threeByNotes.Add(o);
                }
                bool OverUnbeamed3(Other o)
                {
                    var c = o.C;
                    if (o.St == null || c.H < 0.9 * s || c.H > 1.6 * s || c.W < 0.5 * s || c.W > 1.2 * s || c.Cy > o.St.Top) return false;
                    // (its shape no matter: three notes say 3, the toner's blot filling its curls to an 8's loops -- bar 69)
                    // (the stem may run into the number's foot, its tip read at the number's top)
                    var mid = ps.Heads.Where(hd => hd.St == o.St && hd.HasStem && hd.Up && hd.Beams == 0 && Math.Abs(hd.StemX - c.Cx) < 0.5 * s
                                                   && hd.Tip > c.MinY - 0.3 * s && hd.Tip < c.MaxY + 1.0 * s)
                                      .OrderBy(hd => Math.Abs(hd.StemX - c.Cx)).FirstOrDefault();
                    if (TupletVerbose && c.H > 1.0 * s) Console.WriteLine($"    unbeamed 3? piece at {c.Cx:F0},{c.Cy:F0}: stem {(mid == null ? "none" : $"{mid.StemX:F0}, tip {mid.Tip:F0}")}");
                    if (mid == null) return false;
                    var (l3, _) = Unbeamed3(ps, o.St, mid, c.Cx, s);
                    if (l3 <= 0) return false;
                    if (TupletVerbose) Console.WriteLine($"    unbeamed 3? slur starts {string.Join(" ", ps.Detections.Where(t => t.Class == "slur_l" && Math.Abs(t.X - mid.X) < 8 * s && Math.Abs(t.Y - c.Cy) < 4 * s).Select(t => $"{t.X:F0},{t.Y:F0}"))}");
                    return ps.Detections.Any(t => t.Class == "slur_l" && t.X > l3 - 1.5 * s && t.X < mid.X - 1.0 * s && t.Y < c.MaxY + 0.5 * s && t.Y > c.MinY - 3 * s);
                }
                numbers.RemoveAll(o => !sure.Contains(o) && !Bracketed(o));
            }
            else sure.Clear();     // (nothing sure on the page: the rules read as they did)
        }
        foreach (var o in numbers)
        {
            var c = o.C;
            double w = c.W / s, h = c.H / s, pos = o.St.Pos(c.Cy);
            bool det = sure.Contains(o);
            if (TupletVerbose && h > 1.0 && h < 1.8 && w > 0.5 && w < 1.3)
                Console.WriteLine($"    tuplet? s{o.St.System + 1}/{o.St.Part} x {c.MinX} y {c.MinY}: {w:F2}x{h:F2} pos {pos:F1} fill {c.Fill:F2}{(det ? " (detected)" : "")}");
            if (!det && (h < 1.2 || h > 1.55 || w < 0.6 || w > 1.05 || Math.Abs(pos) < 2.5 || c.Fill > 0.45)) continue;
            // a digit of a longer number (a metronome mark's "122") is not a tuplet number
            // (its neighbours share its top and baseline; a bracket's hook does not)
            if (!det && ps.Others.Any(q => q.St == o.St && q.C != c && Math.Abs(q.C.MinY - c.MinY) < 0.15 * s && Math.Abs(q.C.MaxY - c.MaxY) < 0.15 * s
                                   && (Math.Abs(q.C.MinX - c.MaxX) < 0.3 * s || Math.Abs(c.MinX - q.C.MaxX) < 0.3 * s)))
            { if (TupletVerbose) Console.WriteLine("      -> part of a longer number"); continue; }
            // nor a volta ending's number ("1.", "2."): it stands just under the bracket's rule, a long
            // thin line (six spaces or more, with its hooks under three spaces tall) hooked down at its
            // left end right beside the number (the rules read the bold 1 as a 2, and the bar under it
            // as a duplet: gen-rep's chords002)
            // (the rule straight along the top of its piece, ink in its top rows across four fifths of its width: a
            // slur's arc over the number is a piece as wide, its top only by the arc's crown -- Holmes's No.2 scan,
            // bar 51's voice, the arc broken by the 3 into a piece starting just left of it)
            bool RuleAlongTop(Comp q)
            {
                int on = 0, y1 = Math.Min(q.MaxY, q.MinY + (int)Math.Ceiling(0.3 * s));
                for (int x = q.MinX; x <= q.MaxX; x++)
                    for (int y = q.MinY; y <= y1; y++)
                        if (d[x, y]) { on++; break; }
                return on >= 0.8 * (q.MaxX - q.MinX + 1);
            }
            if (ps.Others.Any(q => q.C != c && q.C.W >= 6 * s && q.C.H <= 3 * s && q.C.Fill < 0.3
                                   && c.MinX - q.C.MinX > 0 && c.MinX - q.C.MinX < 3 * s && c.MinY - q.C.MinY > 0 && c.MinY - q.C.MinY < 1.5 * s && RuleAlongTop(q.C)))
            { if (TupletVerbose) Console.WriteLine("      -> under a bracket's rule, by its left hook: an ending's number"); o.Class = "volta_num"; continue; }
            // nor a letter's neighbour in a word: a chord name's raised digit (Cm7, Bb6) beside its
            // letters, which reach into its height (an m is 1.35 spaces wide). A bracket's line by the
            // number is thin, and its hook with the line an L of little ink in its box
            // A letter on the number's baseline is one too, lower-case or set apart: a serif face's
            // Fsus4, its s 0.6 spaces off the F; maj7, its a 0.8 spaces off the 7 past the j
            if (!det && ps.Others.Any(q =>
                {
                    // (any staff's: a lyric's tall f between two staves is the upper one's, its
                    // short neighbours the lower one's; the rows overlap below)
                    if (q.C == c || q.C.W > 2.0 * s || q.C.Fill < 0.2) return false;
                    if (q.C.MinX >= c.MinX && q.C.MaxX <= c.MaxX) return false;    // (a piece of the number itself, cut by a line)
                    // (a letter's shape: a bracket's half, its line and hook, ends on the baseline too)
                    bool line = Math.Abs(q.C.MaxY - c.MaxY) < 0.12 * s && q.C.W <= 1.5 * q.C.H;
                    if (!(q.C.H >= 0.6 * c.H || (line && q.C.H >= 0.4 * c.H))) return false;
                    if (Math.Min(q.C.MaxY, c.MaxY) - Math.Max(q.C.MinY, c.MinY) < 0.1 * Math.Min(q.C.H, c.H)) return false;   // (the raised 7 overlaps the m's top fifth)
                    double gap = Math.Min(Math.Abs(q.C.MinX - c.MaxX), Math.Abs(c.MinX - q.C.MaxX));
                    return gap < (line ? 1.0 : 0.35) * s;
                }))
            { if (TupletVerbose) Console.WriteLine("      -> a letter beside it: a word's"); continue; }
            // nor a piece of an accidental the detector trusts, a staff line cutting it in two (kinkel10-2's scan, bar 5's
            // left hand, a natural's lower half read as a 3 over the b,16 beamed across the barline; lang36-3's scan,
            // bar 48's left hand, a sharp's lower half read as a 3 over the bar's sixteenths)
            if (!det && ps.Detections != null && ps.Detections.Any(t => t.Class is "sharp" or "flat" or "natural" or "dsharp" or "dflat" && t.Score >= TrustScore
                                                                        && Math.Abs(t.X - c.Cx) < 0.5 * s && Math.Abs(t.Y - c.Cy) < 1.2 * s))
            { if (TupletVerbose) Console.WriteLine("      -> a piece of a detected accidental"); continue; }
            // nor a beam's end by its stem: a number stands past the stem's tip, beyond the beam, never
            // alongside the stem (Gonville's sixteenth stub over the eighth beam, cut free by the staff
            // line, band010's dotted rhythm read as a triplet)
            if (!det && ps.Heads.Any(hd => hd.St == o.St && hd.HasStem && hd.StemX > c.MinX - 0.3 * s && hd.StemX < c.MaxX + 0.3 * s
                                           && Math.Min(c.MaxY, Math.Max(hd.Y, hd.Tip)) - Math.Max(c.MinY, Math.Min(hd.Y, hd.Tip)) > 0.5 * c.H))
            { if (TupletVerbose) Console.WriteLine("      -> alongside a stem: a beam's end"); continue; }
            // nor a flag: it hangs from a stem's tip to the right of the stem, cut free of it by a staff
            // line (揺れる想い's vocal eighths: the piece a pixel past the stem, its top at the tip).
            // A number stands a space or more beyond the tip
            if (!det && ps.Heads.Any(hd => hd.St == o.St && hd.HasStem
                                           && (hd.Up ? c.MinY - hd.Tip : hd.Tip - c.MaxY) is var past && past > -0.4 * s && past < 1.0 * s
                                           && c.MinX - hd.StemX > -0.3 * s && c.MinX - hd.StemX < 0.6 * s))
            { if (TupletVerbose) Console.WriteLine("      -> hanging from a stem's tip: a flag"); continue; }
            // a 4 has a closed loop within its own ink (not a gap between the pieces of a cut 3);
            // a number the detector read as a 5, 6 or 7 is that (the rules' shapes tell a 2 and a 4
            // from a 3, and nothing else; its 3 is a 2 when the foot says so). The detector's word on
            // the value holds on a page it was sure of no number on, too: quartet-collide's 5 and 7
            // beside accidentals, seen at 0.74-0.82
            var own = pieceOf.TryGetValue(o, out var ps0) ? ps0.Select(q => q.C).ToList() : new List<Comp> { c };
            int value = threeByNotes.Contains(o) ? 3
                : detValue.TryGetValue(o, out int dv) && dv != 3 ? dv
                : holes.Any(hl => hl.W > 0.15 * s && hl.H > 0.2 * s && own.Any(p => hl.MinX >= p.MinX && hl.MaxX <= p.MaxX && hl.MinY >= p.MinY && hl.MaxY <= p.MaxY)) && !det ? 4
                : IsTwo(d, own, s) ? 2 : 3;
            o.Class = "tuplet" + value;
            var (left, right) = BracketHalves(ps, d, o, s);
            double x0, x1;
            bool? up = null;    // a number by a beam group: its voice's stems (another voice under it keeps its time)
            bool? bracketAbove = null;
            // (one half, long and hooked, the other run into a mark: the number stands at the bracket's middle.
            // kinkel10-1's bar 6, the left hook on the accent under it, the number too far over the heads for a group)
            var lone = left ?? right;
            bool oneHalf = (left == null) != (right == null) && lone.C.W >= 2 * s && lone.C.H >= 0.5 * s;
            if (left != null && right != null || oneHalf)
            {
                // (a hook stands at a note's edge -- the first's left or its stem, the last's right, or, as
                // MuseScore sets it, the last head's left edge, Bohemian Rhapsody's bar 124 with its g''8's
                // centre 0.3 space past the hook: a head whose centre is within four fifths of a space of a
                // hook, a head's width, is under the bracket)
                double reach = oneHalf ? (left != null ? c.Cx - left.C.MinX : right.C.MaxX - c.Cx) : 0;
                x0 = (oneHalf ? c.Cx - reach : left.C.MinX) - 0.8 * s; x1 = (oneHalf ? c.Cx + reach : right.C.MaxX) + 0.8 * s;
                // a bracket over nothing near it (a metronome mark's tuplet) spans no notes here
                double lo = x0, hi = x1;
                // (a bracket may sit on the heads' side or the stems', or just off the staff)
                bool near = Math.Abs(pos) <= 11 || ps.Heads.Any(hd => hd.St == o.St && hd.X > lo && hd.X < hi
                    && Math.Min(Math.Abs(hd.Y - c.Cy), hd.HasStem ? Math.Abs(hd.Tip - c.Cy) : double.MaxValue) < 2.5 * s);
                if (!near) { o.Class = "?"; continue; }
                if (left != null) left.Class = "bracket";
                if (right != null) right.Class = "bracket";
                bracketAbove = c.Cy < (o.St.Top + o.St.Bottom) / 2;
            }
            else
            {
                // the beam group whose stem tips are nearest the number (a cue's small notes' too: their
                // number is full-sized over their beam, gen-cue's tuplets)
                var stemmed = ps.Heads.Concat(ps.Graces.Where(g => g.HasStem && g.Beams > 0)).ToList();
                Head near = null;
                double bd = double.MaxValue;
                foreach (var hd in stemmed)
                {
                    // (the staff above's too: a group beamed below its staff sets its number under the
                    // beam, between the staves, nearer the staff below -- a grand staff's right hand;
                    // and the staff below's, a group beamed above its staff sets it nearer the staff
                    // above -- lied006's right hand under the voice; or the next system's, a number between
                    // two systems a little nearer the upper -- slurs004. A number the detector saw, in a system however far
                    // apart its staves: its own distance to the beam decides, below -- gen-flip's threes004, sixteen spaces.
                    // Not a piece the rules took for one: a fingering's 2 under a voice's far staff, kinkel19-6's bar 20)
                    if (!hd.HasStem || hd.St != o.St && !(hd.St.System == o.St.System && hd.St.Bottom < o.St.Top && (det || o.St.Top - hd.St.Bottom < 8 * s) && !hd.Up)
                                                     && !(hd.St.Top > o.St.Bottom && hd.St.Top - o.St.Bottom < 12 * s && hd.Up)) continue;
                    // the number stands by the beam (stem tips) or on the heads' side (where a
                    // bracket usually goes with it: by the tips first)
                    double dy = Math.Min(Math.Abs(hd.Tip - c.Cy), Math.Abs(hd.Y - c.Cy) + (hd.St != o.St ? double.MaxValue / 4 : 0.5 * s));
                    // a beam inside the staff pushes its number out past the staff: measure to the edge
                    double edge = c.Cy > hd.St.Bottom ? hd.St.Bottom : c.Cy < hd.St.Top ? hd.St.Top : double.NaN;
                    bool inside(double y) => y >= hd.St.Top - 0.1 * s && y <= hd.St.Bottom + 0.1 * s;
                    if (!double.IsNaN(edge) && (inside(hd.Tip) || inside(hd.Y))) dy = Math.Min(dy, Math.Abs(edge - c.Cy));
                    if (dy > 3.0 * s || Math.Abs(hd.StemX - c.Cx) > 3 * s) continue;
                    double dd = Math.Abs(hd.StemX - c.Cx) + dy;
                    if (dd < bd) { bd = dd; near = hd; }
                }
                if (near == null) { if (TupletVerbose) Console.WriteLine("      -> no beam group near"); continue; }
                if (near.St != o.St) { if (TupletVerbose) Console.WriteLine("      -> by another staff's beam: its number"); o.St = near.St; }
                if (TupletVerbose) Console.WriteLine($"      -> near head at {near.X:F0},{near.Y:F0} cluster {near.Cluster}");
                var group = ps.Heads.Where(hd => hd.St == o.St && hd.Cluster == near.Cluster && hd.Cluster != 0).ToList();
                if (group.Select(hd => Math.Round(hd.StemX / s)).Distinct().Count() < 2)
                {
                    // one stem is no group of three: the toner hollowed the beam too and its rim broke
                    // between the stems. The beam is still a line through the stem tips: follow the
                    // beamed stems of the same direction on either side while their tips keep to it,
                    // then keep what stands as far on either side of the number (it is centred on its group)
                    var stems = stemmed.Where(hd => hd.St == o.St && hd.HasStem && hd.Up == near.Up && hd.Beams > 0)
                                        .GroupBy(hd => Math.Round(hd.StemX / (0.3 * s))).Select(g => g.OrderBy(hd => Math.Abs(hd.Tip - hd.Y)).Last())
                                        .OrderBy(hd => hd.StemX).ToList();
                    int k = stems.FindIndex(hd => Math.Abs(hd.StemX - near.StemX) < 0.3 * s);
                    if (k >= 0)
                    {
                        int a = k, b = k;
                        // A cue's small notes stand as far apart as the full-sized notes they copy, a group's
                        // and its neighbours' alike, their tips at one height: a small beam's ink between their
                        // stems joins them, and the group it joins is the number's (the number stands half a
                        // space off its middle, too far for the half-way measure below)
                        // (gen-cue's tuplets002, the triplets' notes read one by one, or with a neighbour's)
                        bool cueBeam = ps.Graces.Contains(near);
                        bool Joins(Head p, Head q) => cueBeam ? ps.Graces.Contains(p) && ps.Graces.Contains(q) && q.StemX - p.StemX < 8 * s && CueBeam(d, p, q, s)
                                                              : q.StemX - p.StemX < 4 * s && Math.Abs(q.Tip - p.Tip) < 1.5 * s;
                        if (TupletVerbose && cueBeam) Console.WriteLine("      cue stems " + string.Join(" ", stems.Where(hd => Math.Abs(hd.StemX - near.StemX) < 8 * s).Select(hd => $"{hd.X:F0},{hd.Y:F0}:{hd.StemX:F0}/{hd.Tip:F0}{(ps.Graces.Contains(hd) ? "g" : "")}")));
                        while (a > 0 && Joins(stems[a - 1], stems[a])) a--;
                        while (b < stems.Count - 1 && Joins(stems[b], stems[b + 1])) b++;
                        double half = cueBeam ? double.MaxValue : Math.Min(c.Cx - stems[a].StemX, stems[b].StemX - c.Cx) + 0.5 * s;
                        var xs = stems.GetRange(a, b - a + 1).Where(hd => Math.Abs(hd.StemX - c.Cx) <= half).Select(hd => hd.StemX).ToList();
                        if (xs.Count >= 2)
                        {
                            double lo = xs.Min() - 0.3 * s, hi = xs.Max() + 0.3 * s;
                            group = stemmed.Where(hd => hd.St == o.St && hd.HasStem && hd.StemX > lo && hd.StemX < hi).ToList();
                            if (TupletVerbose) Console.WriteLine($"      -> one stem in its piece: {xs.Count} stems along the beam line");
                        }
                    }
                }
                // (a small note's group has no cluster: alone where the beam line found no fellows)
                if (group.Count == 0) group.Add(near);
                x0 = group.Min(hd => hd.L) - 0.1 * s; x1 = group.Max(hd => hd.R) + 0.1 * s;
                up = near.Up;
            }
            // An old print sets a triplet of unbeamed quarters or halves under a slur with the 3 on its arc and
            // no bracket, the 3 over the middle note (Holmes's No.2 scan, the voice's e' dis' e' in half its
            // bars). Read by the beam it is that one note; the slur's pieces taken for a bracket's halves
            // span one or two of the three. Under fewer than three columns, the group is the note under the
            // 3 and its neighbour on either side in its bar, the two standing about as far from it
            if (value == 3)
            {
                int cols = ps.Heads.Where(hd => hd.St == o.St && hd.X > x0 && hd.X < x1).Select(hd => Math.Round(hd.X / (0.6 * s)))
                                   .Concat(ps.Rests.Where(r => r.St == o.St && r.X > x0 && r.X < x1).Select(r => Math.Round(r.X / (0.6 * s)))).Distinct().Count();
                var under3 = ps.Heads.Where(hd => hd.St == o.St && hd.HasStem && hd.Beams == 0 && Math.Abs(hd.X - c.Cx) < s)
                                  .OrderBy(hd => Math.Abs(hd.X - c.Cx)).FirstOrDefault();
                if (cols < 3 && under3 != null && Unbeamed3(ps, o.St, under3, c.Cx, s) is var (l3, r3) && l3 > 0)
                {
                    x0 = l3; x1 = r3;
                    if (up != null) up = under3.Up;
                    if (TupletVerbose) Console.WriteLine($"      -> {cols} columns under it, the 3 over an unbeamed note: with its neighbours, x {x0:F0}-{x1:F0}");
                }
            }
            // a tuplet holds two notes or rests at least: a "number" over one note's column is a
            // lyric's kana, a chord name's letter or a flag (揺れる想い, a tuplet 3/2 { a8 } in half the vocal's bars)
            if (!det)
            {
                int Columns(double lo, double hi) => ps.Heads.Concat(ps.Graces).Where(hd => hd.St == o.St && hd.X > lo && hd.X < hi).Select(hd => Math.Round((hd.HasStem ? hd.StemX : hd.X) / (0.6 * s))).Distinct().Count()
                                                     + ps.Rests.Count(r => r.St == o.St && r.X > lo && r.X < hi);
                if (Columns(x0, x1) < 2)
                {
                    // (a group of two not one piece, its bracket unfound: Bohemian's eighth and quarter
                    // under a 3. Its notes stand close by the number, a head or a tip within 3 spaces of it)
                    var by = ps.Heads.Where(hd => hd.St == o.St && Math.Abs(hd.X - c.Cx) < 1.8 * s
                                                  && Math.Min(Math.Abs(hd.Y - c.Cy), hd.HasStem ? Math.Abs(hd.Tip - c.Cy) : double.MaxValue) < 3 * s).ToList();
                    double lo = Math.Min(x0, by.Select(hd => hd.L - 0.1 * s).DefaultIfEmpty(x0).Min()), hi = Math.Max(x1, by.Select(hd => hd.R + 0.1 * s).DefaultIfEmpty(x1).Max());
                    if (Columns(lo, hi) < 2) { if (TupletVerbose) Console.WriteLine($"      -> over x {x0:F0}-{x1:F0}, one note: none"); o.Class = "?"; continue; }
                    x0 = lo; x1 = hi;
                }
            }
            // The rules' shapes tell a 2 and a 4 from a 3 and nothing else: a 5, 6 or 7 they read as a
            // 3 (a 6's loop as a 4's). The group under the number says what it is: five, six or seven
            // notes of one value, no rest among them, are so many in the time of four (the structure
            // before another threshold on the shape; the detector's reading, where there is one, stands).
            // Six sixteenths under a printed 3, a triplet of eighths each halved, read as a sextuplet:
            // the same time, the digit alone differs
            if (!det && value is 2 or 3 or 4)
            {
                var under = ps.Heads.Where(hd => hd.St == o.St && hd.X > x0 && hd.X < x1 && hd.HasStem && (up == null || hd.Up == up)).ToList();
                int n = under.Select(hd => Math.Round(hd.StemX / (0.6 * s))).Distinct().Count();
                bool alike = under.Count > 0 && under.All(hd => hd.Beams == under[0].Beams);
                // (a rest between the first stem and the last: one past them is the group's neighbour
                // in a span a stray head widened, tuplets004's quarter rest after its septuplet)
                double sx0 = under.Count > 0 ? under.Min(hd => hd.StemX) : x0, sx1 = under.Count > 0 ? under.Max(hd => hd.StemX) : x1;
                bool rest = ps.Rests.Any(r => r.St == o.St && r.X > sx0 && r.X < sx1);
                if (TupletVerbose && n >= 5) Console.WriteLine($"      -> {n} stems under it, beams {(alike ? under[0].Beams.ToString() : "unalike")}{(rest ? ", a rest among them" : "")}");
                if (alike && !rest && n is 5 or 6 or 7) { value = n; o.Class = "tuplet" + value; }
                // (a 2 over three beamed notes of one value is no duplet, which holds two: a fingering's
                // 2 between the staves, over the left hand's beamed threes -- kinkel19-6's fourth page)
                if (value == 2 && alike && !rest && n >= 3) { if (TupletVerbose) Console.WriteLine($"      -> a 2 over {n} beamed notes alike: no duplet"); o.Class = "?"; continue; }
                // (nor, with no bracket, over eight stems and more of any values, no tuplet's count: a chord's hollow head on a
                // scan read as a 2, a slur joining it to the notes before -- lang36-3's last bars, nine stems under it. Under
                // eight, a number the rules read as a 2 may be a real 5, 6 or 7 over notes of unlike beams)
                if (value == 2 && n >= 8 && !(left != null && right != null)) { if (TupletVerbose) Console.WriteLine($"      -> a 2 over {n} stems, no bracket: no duplet"); o.Class = "?"; continue; }
                // (likewise a 3 over four beamed notes of one value, which no triplet holds: a verse's number
                // over the staff, "3. Und" over kinkel10-1's four eighths)
                if (value == 3 && alike && !rest && n == 4 && !(left != null && right != null)) { if (TupletVerbose) Console.WriteLine("      -> a 3 over 4 beamed notes alike: no triplet"); o.Class = "?"; continue; }
            }
            if (TupletVerbose) Console.WriteLine($"      -> tuplet {value} over x {x0:F0}-{x1:F0}, bracket {(left != null && right != null)}");
            if (pieceOf.TryGetValue(o, out var parts)) foreach (var q in parts) q.Class = o.Class;
            // (three in the time of two, four or two in the time of three: a 9/8's duplet, its two eighths a
            // dotted quarter; five, six or seven in the time of four)
            var (num, den) = value switch { 4 => (3, 4), 2 => (3, 2), 5 => (4, 5), 6 => (4, 6), 7 => (4, 7), _ => (2, 3) };
            tupletId++;
            ps.Tuplets.Add(o);
            // (a grace note's scale is no matter -- it takes no time -- but a cue's small notes play theirs)
            foreach (var hd in ps.Heads.Concat(ps.Graces))
                // (not a whole note under a group of stems, another voice's at the group's first column: Op.27-2's bars 32 and 33)
                if (hd.St == o.St && hd.X > x0 && hd.X < x1 && (up == null || !hd.HasStem && !hd.WholeShape || hd.Up == up && hd.HasStem)) { hd.Scale = (num, den); hd.Tuplet = tupletId; }
            // (and a rest of its voice: where the other voice's stems stand in the span too, the rests
            // on that voice's side of the staff are its -- the upper voice's 16th rest over a lower
            // voice's triplet, voices2003)
            bool twoVoices = up != null && ps.Heads.Any(hd => hd.St == o.St && hd.HasStem && hd.Up != up && hd.X > x0 && hd.X < x1);
            double mid = (o.St.Top + o.St.Bottom) / 2;
            // (nor, under a bracket, a rest out past the staff's middle on the side away from it, a space and a
            // half off: another voice's, whose notes there may be none -- Holmes's No.2 bar 45, the right hand's
            // triplet bracketed over the staff, the lower voice's half rest under the staff)
            bool Far(Rest r) => bracketAbove == true ? r.Y > mid + 1.5 * s : bracketAbove == false && r.Y < mid - 1.5 * s;
            foreach (var r in ps.Rests)
                // (not the whole rest under a cue: the part's own bar of rest, its notes' span about it)
                if (r.St == o.St && r.X > x0 && r.X < x1 && r.Kind != RestKind.Whole && !(twoVoices && (up == true ? r.Y > mid + 0.5 * s : r.Y < mid - 0.5 * s)) && !Far(r)) { r.Scale = (num, den); r.Tuplet = tupletId; }
        }
    }

    /// <summary>
    /// The span of an unbeamed triplet whose 3 stands over its middle note: the nearest column of notes
    /// or rests on either side of the note in its bar (the note's voice's stems, unbeamed), when the 3 is
    /// over the note's middle and the two stand about as far from it, within eight spaces. (0, 0): none.
    /// </summary>
    static (double L, double R) Unbeamed3(PageSymbols ps, Staff st, Head near, double cx, double s)
    {
        var sys = ps.Systems?.FirstOrDefault(sy => sy.Staves.Contains(st) || sy.All.Contains(st));
        double b0 = sys?.Bars.Where(b => b < near.X).DefaultIfEmpty(double.MinValue).Max() ?? double.MinValue;
        double b1 = sys?.Bars.Where(b => b > near.X).DefaultIfEmpty(double.MaxValue).Min() ?? double.MaxValue;
        var cols = ps.Heads.Where(hd => hd.St == st && hd != near && (hd.HasStem ? hd.Up == near.Up && hd.Beams == 0 : !hd.WholeShape))
                           .Select(hd => (hd.X, hd.L, hd.R))
                           .Concat(ps.Rests.Where(r => r.St == st && r.Kind != RestKind.Whole).Select(r => (r.X, r.L, r.R)))
                           .Where(p => p.X > b0 && p.X < b1).ToList();
        var left = cols.Where(p => p.X < near.X - 0.6 * s).OrderBy(p => p.X).LastOrDefault();
        var right = cols.Where(p => p.X > near.X + 0.6 * s).OrderBy(p => p.X).FirstOrDefault();
        if (left == default || right == default) return (0, 0);
        double gl = near.X - left.X, gr = right.X - near.X;
        if (Math.Max(gl, gr) > 8 * s || Math.Max(gl, gr) > 1.6 * Math.Min(gl, gr) || Math.Abs(cx - near.X) > 0.3 * Math.Min(gl, gr)) return (0, 0);
        // (and its bar holds more than the three: three alike that are the whole bar fill a 3/4's as they
        // stand, the "3" over them some other mark's -- a fingering's. Any note or rest of the staff off
        // their columns counts, whatever its stem: Holmes's voice runs on after its triplet with its stems
        // down, and a rest of any kind -- his right hand rests a half before its triplet, read as a whole's)
        if (!ps.Heads.Where(hd => hd.St == st).Select(hd => hd.X).Concat(ps.Rests.Where(r => r.St == st).Select(r => r.X))
                 .Any(x => x > b0 && x < b1 && (x < left.X - 1.5 * s || x > right.X + 1.5 * s)))   // (a chord's head pushed aside by a second is its column's)
        {
            if (TupletVerbose) Console.WriteLine($"      -> the 3 over an unbeamed note at {near.X:F0},{near.Y:F0}: its neighbours are the whole bar, no triplet");
            return (0, 0);
        }
        // (a chord's heads, a column's: the whole column)
        double lo = cols.Where(p => Math.Abs(p.X - left.X) < 0.6 * s).Min(p => p.L), hi = cols.Where(p => Math.Abs(p.X - right.X) < 0.6 * s).Max(p => p.R);
        return (lo - 0.1 * s, hi + 0.1 * s);
    }

    /// <summary>
    /// Whether a beam joins two small notes' stems: in nine columns in ten between them, a run of ink
    /// as thick as a small beam (a fifth of a space, more than a staff line's) between the two tips'
    /// heights, half a space beyond either towards the heads and a space and a half away from them.
    /// Their tips are often off, the rules' and the detector's on one beam, a fifth of a space apart, or
    /// short of a steep one (tuplets004's b'8 a'8 g'8, and a'8 d''8 g''8 with the g'' tipped half a
    /// space over its head): the line from tip to tip misses the beam.
    /// </summary>
    static bool CueBeam(Img d, Head p, Head q, double s)
    {
        double lo = Math.Min(p.Tip, q.Tip), hi = Math.Max(p.Tip, q.Tip);
        int y0 = (int)(lo - (p.Up ? 1.5 : 0.5) * s), y1 = (int)(hi + (p.Up ? 0.5 : 1.5) * s);
        int need = Math.Max(2, (int)Math.Round(0.18 * s)), n = 20, hit = 0;
        for (int k = 1; k < n; k++)
        {
            int x = (int)Math.Round(p.StemX + (q.StemX - p.StemX) * k / n), run = 0;
            if (x <= 0 || x >= d.W) continue;
            for (int y = Math.Max(0, y0); y <= Math.Min(d.H - 1, y1); y++)
            {
                run = d[x, y] ? run + 1 : 0;
                if (run >= need) { hit++; break; }
            }
        }
        return hit >= 0.9 * (n - 1);
    }

    /// <summary>
    /// Whether the page shows a bracket's line from a piece's end on the number's side (its first ink from
    /// the side away from the staff) to within a space of the number: a thin run in every column, each
    /// within two pixels of the last (the line may slope).
    /// </summary>
    static bool LineTo(Img d, Comp bc, Comp c, bool above, double s)
    {
        bool isLeft = bc.MaxX < c.MinX;
        int x = isLeft ? bc.MaxX : bc.MinX, end = isLeft ? (int)(c.MinX - 1.0 * s) : (int)(c.MaxX + 1.0 * s), dir = isLeft ? 1 : -1;
        int y = -1;
        for (int yy = above ? bc.MinY : bc.MaxY; above ? yy <= bc.MaxY : yy >= bc.MinY; yy += above ? 1 : -1)
            if (d[x, yy]) { y = yy; break; }
        if (y < 0) return false;
        for (int xx = x + dir; isLeft ? xx <= end : xx >= end; xx += dir)
        {
            if (xx <= 0 || xx >= d.W - 1) return false;
            int at = -1;
            for (int dy = 0; dy <= 2 && at < 0; dy++)
                if (y + dy < d.H - 1 && d[xx, y + dy]) at = y + dy;
                else if (y - dy > 0 && d[xx, y - dy]) at = y - dy;
            if (at < 0) return false;
            int t = at, e = at;
            while (t > 1 && d[xx, t - 1]) t--;
            while (e < d.H - 2 && d[xx, e + 1]) e++;
            if (e - t > 0.3 * s) return false;   // (a stem, a head or a letter across it: no bracket's line)
            y = at;
        }
        return true;
    }

    /// <summary>The two halves of a tuplet bracket either side of a number (null where there is none).</summary>
    static (Other Left, Other Right) BracketHalves(PageSymbols ps, Img d, Other o, double s)
    {
        var c = o.C;
        Other left = null, right = null;
        if (TupletVerbose)
            foreach (var b in ps.Others.Where(b => b != o && Math.Abs(b.C.Cx - c.Cx) < 9 * s && Math.Abs(b.C.Cy - c.Cy) < 2 * s))
                Console.WriteLine($"      bracket? piece {b.Class} at {b.C.Cx:F0},{b.C.Cy:F0} {b.C.W / s:F2}x{b.C.H / s:F2} fill {b.C.Fill:F2}{(b.St != o.St ? " (another staff's)" : "")}");
        foreach (var b in ps.Others)
        {
            if (b == o || b.Class != "?" || b.St != o.St) continue;
            var bc = b.C;
            // a bracket half: a hook, or just its horizontal line when the corner came apart. Thin: ink in
            // under 0.3 of its box, or, a short stout hook, no more than a stroke a fifth of a space thick
            // along two sides of the box would fill (MuseScore's hooks either side of Bohemian Rhapsody's
            // 3, bar 124, the owner's seed of 2026-11-01: 0.96 by 0.8 spaces at fill 0.33, which left the
            // triplet one note; ninetofive's long halves, 3.1 by 0.9 spaces at 0.25, pass by the flat 0.3)
            bool thin = bc.H <= 0.2 * s || bc.Fill <= 0.3 || bc.Fill <= 0.2 * s * (bc.W + bc.H) / (bc.W * bc.H);
            if (bc.H > 1.3 * s || bc.W < 0.4 * s || !thin || Math.Abs(bc.Cy - c.Cy) > 1.0 * s) continue;
            // (nor a dash of an octave line the number stands on, another dash on its row further out: a
            // bracket's half ends at its hook. Seed 11's piano008, a triplet's 3 on an 8vb's dashes, the
            // triplet cut to the one note between the two dashes)
            bool onLeft = bc.MaxX < c.MinX;
            if (bc.H <= 0.25 * s && ps.Others.Any(q => q != b && q != o && q.C.H <= 0.25 * s && q.C.W >= 0.4 * s && q.C.W <= 1.2 * s
                    && Math.Abs(q.C.Cy - bc.Cy) < 0.15 * s && (onLeft ? bc.MinX - q.C.MaxX : q.C.MinX - bc.MaxX) is var g && g > 0 && g < 1.5 * s))
                continue;
            // (within a space and a fifth of the number: the same hooks stand 0.9 space off their 3)
            // (or further, up to four spaces, where the page shows its line on to the number: a line on a ledger
            // line's row was taken out with them over a part of its length, Holmes's No.2 bar 37, the right half 2.4 spaces off)
            bool Reaches(double gap) => gap < 1.2 * s || gap < 4 * s && LineTo(d, bc, c, c.Cy < o.St.Top, s);
            if (bc.MaxX < c.MinX && Reaches(c.MinX - bc.MaxX) && (left == null || bc.MaxX > left.C.MaxX)) left = b;
            if (bc.MinX > c.MaxX && Reaches(bc.MinX - c.MaxX) && (right == null || bc.MinX < right.C.MinX)) right = b;
        }
        // A bracket's line at the height of a ledger line is taken out with the ledger lines
        // (Bravura's meters002, three spaces over the staff): its hook is left alone, a thin upright
        // stroke a few spaces off. The page still shows the line from the hook's end towards the number.
        // (The hook may keep a stub of the line, its corner: Petaluma's meters002, 0.67 spaces wide
        // where the line lies on the next ledger line's row and the rest of it went)
        if (left == null || right == null)
            foreach (var b in ps.Others)
            {
                if (b == o || b.Class != "?" || b.St != o.St) continue;
                var bc = b.C;
                if (bc.W > 1.0 * s || (bc.W > 0.25 * s && bc.Fill > 0.4) || bc.H < 0.3 * s || bc.H > 1.3 * s || Math.Abs(bc.Cy - c.Cy) > 1.0 * s) continue;
                bool isLeft = bc.MaxX < c.MinX;
                double gap = isLeft ? c.MinX - bc.MaxX : bc.MinX - c.MaxX;
                if (gap < 0.8 * s || (isLeft ? left : right) != null) continue;
                // (further off, up to ten spaces, where the page shows its line all the way to the number: a sloping
                // line crossing the ledger lines' rows, Holmes's No.2 bar 37, its left hook 7.6 spaces off)
                if (gap > 4 * s && !(gap < 10 * s && LineTo(d, bc, c, c.Cy < o.St.Top, s))) continue;
                // (the line leaves the hook's end on the number's side of it: over the staff the hook
                // hangs down from it, under the staff it stands up)
                int y0 = c.Cy < o.St.Top ? bc.MinY : bc.MaxY;
                bool line = true;
                for (int i = 1; i <= 5 && line; i++)
                {
                    int x = isLeft ? (int)(bc.MaxX + 1 + i * 0.12 * gap) : (int)(bc.MinX - 1 - i * 0.12 * gap);
                    bool hit = false;
                    for (int y = (int)(y0 - 0.3 * s); y <= (int)(y0 + 0.3 * s) && !hit; y++)
                        if (d[x, y])
                        {
                            int t = y, e = y;
                            while (d[x, e + 1]) e++;
                            hit = e - t <= 0.25 * s;
                            y = e;
                        }
                    line = hit;
                }
                if (!line) continue;
                if (isLeft) left = b; else right = b;
            }
        // (one half found, the other's hook no piece of its own -- joined to another piece or taken with
        // a note's: the page shows the line from the number to a hook. Holmes's No.2 bars 45, 46, 48, the
        // right hooks of triplets over an octave line's chords, the left half a hook alone. The bracket so
        // made spans three columns of notes or rests at least: two under it is a wrong piece taken for
        // the other half, the span cut short)
        if ((left == null) != (right == null) && TracedHook(d, c, left == null, c.Cy < o.St.Top, s) is { } hook)
        {
            double lo = Math.Min(hook.MinX, (left ?? right).C.MinX) - 0.8 * s, hi = Math.Max(hook.MaxX, (left ?? right).C.MaxX) + 0.8 * s;
            int cols = ps.Heads.Where(hd => hd.St == o.St && hd.X > lo && hd.X < hi).Select(hd => Math.Round((hd.HasStem ? hd.StemX : hd.X) / (0.6 * s))).Distinct().Count()
                       + ps.Rests.Count(r => r.St == o.St && r.X > lo && r.X < hi);
            if (TupletVerbose) Console.WriteLine($"      bracket: the {(left == null ? "left" : "right")} hook traced on the page at x {hook.MinX}, y {hook.MinY}-{hook.MaxY}, {cols} columns under it");
            // (and the bracket so made stands as a bracket does: its number at the middle, the hook about as far
            // off it as the found half's far end, and the two halves' lines at one height, give or take a space
            // for the slope -- not a fingering's 3 by some line on the page)
            var found = (left ?? right).C;
            double near = Math.Abs(hook.MinX - c.Cx), far = left != null ? c.Cx - found.MinX : found.MaxX - c.Cx;
            int rowHook = c.Cy < o.St.Top ? hook.MinY : hook.MaxY, rowFound = c.Cy < o.St.Top ? found.MinY : found.MaxY;
            bool even = near > 0.6 * far && near < 1.6 * far && Math.Abs(rowHook - rowFound) <= 1.0 * s;
            if (TupletVerbose) Console.WriteLine($"        hook {near:F0} off the number, the found half {far:F0}, rows {rowHook}/{rowFound}{(even ? "" : ": no bracket")}");
            if (cols >= 3 && even)
            {
                if (left == null) left = new Other { C = hook, St = o.St }; else right = new Other { C = hook, St = o.St };
            }
        }
        return (left, right);
    }

    /// <summary>
    /// A bracket's half traced on the page from beside the number: a thin line (each column within
    /// two pixels of the last) running at least a space and at most twelve to a hook, an upright stroke
    /// from the line's row towards the staff (down over it, up under it) of half a space to two, past
    /// which the line goes no further. The hook's stroke, as a piece, or null.
    /// </summary>
    static Comp TracedHook(Img d, Comp c, bool isLeft, bool above, double s)
    {
        int dir = isLeft ? -1 : 1, x0 = isLeft ? c.MinX - 1 : c.MaxX + 1;
        int x = -1, y = -1;
        // the line's first ink beside the number, within the number's height: a thin run
        for (int xx = x0; Math.Abs(xx - x0) <= 1.2 * s && y < 0; xx += dir)
        {
            if (xx <= 0 || xx >= d.W - 1) return null;
            for (int yy = Math.Max(1, c.MinY); yy <= Math.Min(d.H - 2, c.MaxY) && y < 0; yy++)
            {
                if (!d[xx, yy]) continue;
                int e = yy;
                while (e < d.H - 2 && d[xx, e + 1]) e++;
                if (e - yy <= 0.25 * s) { x = xx; y = yy; }
                yy = e;
            }
        }
        if (y < 0) return null;
        int gap = (int)Math.Max(2, 0.3 * s);
        for (int xx = x + dir; Math.Abs(xx - x) <= 12 * s; xx += dir)
        {
            if (xx <= gap || xx >= d.W - 1 - gap) return null;
            int at = -1;
            for (int dy = 0; dy <= 2 && at < 0; dy++)
                if (y + dy < d.H - 1 && d[xx, y + dy]) at = y + dy;
                else if (y - dy > 0 && d[xx, y - dy]) at = y - dy;
            if (at < 0) return null;
            int t = at, e = at;
            while (t > 1 && d[xx, t - 1]) t--;
            while (e < d.H - 2 && d[xx, e + 1]) e++;
            if (e - t > 0.3 * s)
            {
                // a hook: from the line's row towards the staff, not a stem crossing it, and the line ends
                int along = above ? e - at : at - t, back = above ? at - t : e - at;
                if (Math.Abs(xx - x) < 1.0 * s || along < 0.5 * s || along > 2 * s || back > 0.2 * s) return null;
                for (int yy = at - 3; yy <= at + 3; yy++)
                    if (yy > 0 && yy < d.H && d[xx + gap * dir, yy]) return null;
                return new Comp { MinX = xx, MaxX = xx, MinY = t, MaxY = e, N = e - t + 1, Sx = (long)xx * (e - t + 1), Sy = (long)(t + e) * (e - t + 1) / 2 };
            }
            y = at;
        }
        return null;
    }
}
