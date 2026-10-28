namespace Omr;

static partial class Symbols
{
    /// <summary>
    /// A cross head's stem. Some engravings (Fantasy's drums) run the stem through the cross's
    /// middle, not at its edge: a head's stem through the middle, the cross lying along it between
    /// the head and the tip (the hi-hat over the snare of one beamed stem), is the cross's; else the
    /// cross's own stem, looked for at its edge or its middle.
    /// </summary>
    static void CrossStem(Img ink, List<Head> heads, Head h, double s)
    {
        foreach (var o in heads)
        {
            if (o == h || o.Cross || !o.HasStem || o.St != h.St || Math.Abs(o.StemX - h.X) > 0.35 * s) continue;
            if (Math.Abs(o.Y - h.Y) < 0.3 * s || Math.Abs(h.Y - o.Tip) < 1.2 * s) continue;
            if ((h.Y - o.Tip) * (o.Y - h.Y) <= 0) continue;
            h.HasStem = true; h.Up = o.Up; h.StemX = o.StemX; h.Tip = o.Tip;
            return;
        }
        FindStem(ink, h, s);
    }

    static void FindStem(Img ink, Head h, double s)
    {
        int cy = (int)Math.Round(h.Y), off = (int)(0.15 * s);
        int bestLen = 0, bestX = 0, bestTip = 0; bool bestUp = false;
        bool dbg = DebugAt.HasValue && Math.Abs(h.X - DebugAt.Value.X) < 2 * s && Math.Abs(h.Y - DebugAt.Value.Y) < 3 * s;
        // On a scan the toner thins where the stem meets the head, and the stem breaks where it
        // crosses a staff line. For a head the detector placed (it is a head; the question is only
        // where its stem is) the column's ink may begin up to half a space from the centre, and a
        // break of up to a fifth of a space is stepped over when a thin stroke goes on beyond it
        // (not a beam: the stem's tip stays this side of the beams). The rules' own candidates get
        // the strict search: many of them are no heads, and a stroke near by must not make them one.
        // The strict search first (the stem's ink unbroken from the head: on a clean engraving the
        // column it picks is the stem's solid middle, which the beam count relies on); the loose
        // one only when it found nothing.
        // (a slash's stem leaves its corner, about a space above or below its centre)
        Search(h.Slash ? (int)(1.4 * s) : 0, 0);
        if (bestLen < 2.2 * s && h.Score > 0)
        {
            bestLen = 0;
            Search((int)((h.Slash ? 1.4 : 0.55) * s), Math.Max(1, (int)(0.2 * s)));
        }
        // (a scan's head the detector boxed narrower than its ink: the stem a third of a space past the
        // box's edge, broken a quarter of a space over its ledger -- kinkel10-4's b, left stemless and read a quarter)
        if (bestLen < 2.2 * s && h.Score > 0 && !h.Slash && !h.Cross)
        {
            bestLen = 0;
            // (and broken up to half a space short of its beam, the tip the beam's edge: kinkel10-4's scan, the left
            // hand's d, white a third of a space under the beam and once more over the staff, read a quarter)
            Search((int)(0.55 * s), Math.Max(1, (int)(0.3 * s)), 0.3, (int)(0.5 * s));
        }
        if (bestLen >= 2.2 * s)
        {
            h.HasStem = true; h.Up = bestUp; h.StemX = bestX; h.Tip = bestTip;
        }

        void Search(int start, int gap, double past = 0.12, int beamGap = 0)
        {
            int Follow(int x, int y, int dir)
            {
                while (true)
                {
                    if (ink[x, y + dir]) { y += dir; continue; }
                    if (gap == 0) return y;
                    int g = 2;
                    while (g <= Math.Max(gap, beamGap) + 1 && !ink[x, y + dir * g]) g++;
                    if (g <= gap + 1 && Thin(ink, x, y + dir * g, s)) { y += dir * g; continue; }
                    // (a beam past the break: the stem ends at its edge)
                    if (beamGap > 0 && g <= beamGap + 1 && Wide(ink, x, y + dir * g, dir, s)) return y + dir * g;
                    return y;
                }
            }
            // up: at the right edge, going up (a cross's stem may run through its middle)
            for (int x = (int)(h.Cross ? h.X - 0.2 * s : h.R - 0.35 * s); x <= (int)(h.R + past * s); x++)
            {
                int y = cy - off;
                while (!ink[x, y] && cy - y < start) y--;
                if (!ink[x, y]) { if (dbg) Console.WriteLine($"  stem? up x {x}: no ink"); continue; }
                y = Follow(x, y, -1);
                int len = cy - y;
                if (dbg) Console.WriteLine($"  stem? up x {x}: len {len / s:F2} thin {ThinSomewhere(ink, x, cy, -1, len, s)}");
                if (len > bestLen && ThinSomewhere(ink, x, cy, -1, len, s)) { bestLen = len; bestX = x; bestTip = y; bestUp = true; }
            }
            // down: at the left edge, going down
            for (int x = (int)(h.L - past * s); x <= (int)(h.Cross ? h.X + 0.2 * s : h.L + 0.35 * s); x++)
            {
                int y = cy + off;
                while (!ink[x, y] && y - cy < start) y++;
                if (!ink[x, y]) { if (dbg) Console.WriteLine($"  stem? down x {x}: no ink"); continue; }
                y = Follow(x, y, 1);
                int len = y - cy;
                if (dbg) Console.WriteLine($"  stem? down x {x}: len {len / s:F2} thin {ThinSomewhere(ink, x, cy, 1, len, s)}");
                if (len > bestLen && ThinSomewhere(ink, x, cy, 1, len, s)) { bestLen = len; bestX = x; bestTip = y; bestUp = false; }
            }
        }
    }

    /// <summary>
    /// The stem the other way from a head's own: down at its left edge for an up stem, up at its
    /// right edge for a down one; unbroken from the head, 2.2 spaces long or more, thin somewhere.
    /// </summary>
    static bool OtherStem(Img ink, Head h, double s, out int stemX, out int tip)
    {
        int cy = (int)Math.Round(h.Y), off = (int)(0.15 * s), dir = h.Up ? 1 : -1, best = 0;
        stemX = 0; tip = 0;
        int x0 = (int)(h.Up ? h.L - 0.12 * s : h.R - 0.35 * s), x1 = (int)(h.Up ? h.L + 0.35 * s : h.R + 0.12 * s);
        for (int x = x0; x <= x1; x++)
        {
            int y = cy + dir * off;
            if (!ink[x, y]) continue;
            while (ink[x, y + dir]) y += dir;
            int len = Math.Abs(y - cy);
            if (len > best && ThinSomewhere(ink, x, cy, dir, len, s)) { best = len; stemX = x; tip = y; }
        }
        return best >= 2.2 * s;
    }

    /// <summary>
    /// The other way's stem at the head's far edge, already read as a chord's: a chord stacked in
    /// thirds under (or over) the head hides the stem behind its heads wherever <see cref="OtherStem"/>
    /// looks for it thin (piano006: the upper voice's cis' on the lower voice's &lt;fis ais cis'&gt;).
    /// A head of another chord whose stem stands at that edge, the ink unbroken from this head to it.
    /// </summary>
    static bool ChordStemAtEdge(Img ink, Head h, List<Head> heads, double s, out int stemX, out int tip)
    {
        stemX = 0; tip = 0;
        double x0 = h.Up ? h.L - 0.12 * s : h.R - 0.35 * s, x1 = h.Up ? h.L + 0.35 * s : h.R + 0.12 * s;
        foreach (var m in heads.Where(m => m != h && m.St == h.St && m.HasStem && !m.Cross && m.Up != h.Up
                                          && m.StemX >= x0 && m.StemX <= x1 && (h.Up ? m.Y > h.Y : m.Y < h.Y))
                               .OrderBy(m => h.Up ? -m.Tip : m.Tip))
        {
            // (from a little inside the head: the stem joins it below (above) its middle row)
            double from = h.Up ? h.Y + 0.3 * s : h.Y - 0.3 * s;
            int x = (int)Math.Round(m.StemX), y0 = (int)Math.Round(Math.Min(from, m.Y)), y1 = (int)Math.Round(Math.Max(from, m.Y));
            bool unbroken = true;
            for (int y = y0; y <= y1 && unbroken; y++) unbroken = ink[x - 1, y] || ink[x, y] || ink[x + 1, y];
            if (!unbroken) continue;
            stemX = x; tip = (int)Math.Round(m.Tip);
            return true;
        }
        return false;
    }

    /// <summary>
    /// A vertical stroke through the head's middle, running on past it both up and down: a
    /// stem with something drawn across it (a grace note's slash over its flag), no cross head, whose
    /// stem stands at its edge and runs one way.
    /// </summary>
    static bool StrokeThrough(Img ink, Head h, double s)
    {
        int cy = (int)Math.Round(h.Y), reach = (int)Math.Ceiling(0.7 * s);
        for (int x = (int)(h.X - 0.35 * s); x <= (int)(h.X + 0.35 * s); x++)
        {
            int miss = 0;
            for (int y = cy - reach; y <= cy + reach && miss <= 2; y++) miss = ink[x, y] ? 0 : miss + 1;
            if (miss <= 2) return true;
        }
        return false;
    }

    /// <summary>
    /// The heads of a chord share one stem. From a head in the middle of a tall chord (or the far
    /// end of it) the stem is never thin where it is looked for: take the stem of a head it hangs
    /// on, when the stem's column runs unbroken from that head to this one at this one's edge.
    /// </summary>
    static void ShareStems(Img ink, List<Head> heads, double s)
    {
        var stemmed = heads.Where(h => h.HasStem).ToList();
        foreach (var h in heads.Where(h => !h.HasStem))
            // (a stem whose column runs unbroken to the head first, then for a detected head one broken)
            foreach (bool broken in h.Score > 0 && !h.Hollow && !h.WholeShape ? new[] { false, true } : new[] { false })
            foreach (var o in stemmed)
            {
                if (h.HasStem) break;
                // (a second's heads are half a space apart; a ledger line through one shifts its centre)
                if (o.St != h.St || Math.Abs(o.Y - h.Y) > 4 * s || Math.Abs(o.Y - h.Y) < 0.3 * s) continue;
                bool edge = o.Up ? o.StemX >= h.R - 0.35 * s && o.StemX <= h.R + 0.12 * s
                                 : o.StemX >= h.L - 0.12 * s && o.StemX <= h.L + 0.35 * s;
                // in a second the upper head of an up stem sits right of it, the lower of a down
                // stem left of it, beside a head of the chord a step away on the other side
                // (a quarter space off its edge: a scan's head, its rim eaten, is narrower than drawn --
                // kinkel10-2's scan, the right hand's <a b>16 on a down stem 0.18 space off the a)
                bool flipped = (o.Up ? o.StemX >= h.L - 0.25 * s && o.StemX <= h.L + 0.35 * s
                                     : o.StemX >= h.R - 0.35 * s && o.StemX <= h.R + 0.25 * s)
                    && heads.Any(n => n != h && n.St == h.St && Math.Abs(n.Pos - h.Pos) == 1
                                      && Math.Abs(n.X - h.X) > 0.8 * s && Math.Abs(n.X - h.X) < 1.6 * s);
                // (or a detected head by its centre, half a space to a space off the stem, its neighbour on the
                // stem's other side: the detector's box of an old print's head is narrower than its ink --
                // kinkel10-4's scan, the right hand's <a c' d'>16 on a down stem, the c' 0.4 space off by its box)
                double off = o.Up ? h.X - o.StemX : o.StemX - h.X;
                flipped |= h.Score > 0 && off > 0.5 * s && off < 1.1 * s
                    && heads.Any(n => n != h && n.St == h.St && Math.Abs(n.Pos - h.Pos) == 1
                                      && Math.Abs(n.X - h.X) > 0.8 * s && Math.Abs(n.X - h.X) < 1.6 * s
                                      && (o.Up ? n.R <= o.StemX + 0.25 * s : n.L >= o.StemX - 0.25 * s));
                // (two detected heads in one column are a chord's on one stem: the detector's centres
                // align to a tenth of a space where a scan eats a head's edge -- chords008's top head,
                // its left edge half a space short of the stem, fell to the digit rule)
                bool centred = h.Score > 0 && o.Score > 0 && Math.Abs(o.X - h.X) < 0.3 * s;
                if (!edge && !flipped && !centred) continue;
                // the head must lie along the stem: between its tip and its other end, which is
                // found by following the stem's column from o away from the tip (it joins the
                // last head of the chord off its middle: allow a little past the end)
                // (broken: across a break of up to half a space, a scan's stem white beside its head --
                // kinkel10-4's scan, the right hand's <a c' d'>8, the d' left stemless. Not before an unbroken
                // one: slurs010's a under the upper voice's cis', its own the lower voice's chord's)
                int x = (int)o.StemX, dir = o.Up ? 1 : -1, maxGap = broken ? (int)(0.5 * s) : 0;
                bool Ink(int y) => ink[x, y] || ink[x - 1, y] || ink[x + 1, y];
                int end = (int)o.Y;
                for (int y = end + dir, gap = 0; gap <= maxGap && Math.Abs(end - o.Y) < 4 * s; y += dir)
                    if (Ink(y)) { end = y; gap = 0; } else gap++;
                double lo = Math.Min(o.Tip, end) - 0.4 * s, hi = Math.Max(o.Tip, end) + 0.4 * s;
                if (h.Y < lo || h.Y > hi) continue;
                // ... and no head stands at the tip, where a flag curls or a beam ends: a stem runs
                // on well past its last head
                // (save the second's outer head a step from the stem's own head: the stem's tip on a scan
                // may be found short of its beam, kinkel10-2's <a b>16 at 1.7 spaces)
                bool second = flipped && Math.Abs(o.Pos - h.Pos) == 1 && Math.Abs(o.X - h.X) > 0.8 * s;
                if (Math.Abs(h.Y - o.Tip) < 1.8 * s && !second) continue;
                h.HasStem = true; h.Up = o.Up; h.StemX = o.StemX;
                // the tip is the stem's far end, beyond whichever head is further from it
                h.Tip = o.Tip;
            }
    }

    /// <summary>
    /// A stem is thin somewhere along it: just past its head, or further on (in a chord the next
    /// head up the stem is right there, and a beam may sit at the tip).
    /// </summary>
    static bool ThinSomewhere(Img ink, int x, int cy, int dir, int len, double s)
    {
        foreach (double at in new[] { 0.9, 1.5, 2.1 })
            if (at * s < len - 0.3 * s && Thin(ink, x, (int)(cy + dir * at * s), s)) return true;
        return false;
    }

    /// <summary>
    /// From row y on in direction dir, a beam's ink: within 0.6 of a space, a third of a space of rows
    /// wider than a space about column x (a staff line is thinner; a sloping beam's edge comes in late).
    /// </summary>
    static bool Wide(Img ink, int x, int y, int dir, double s)
    {
        int r = (int)(0.6 * s), wide = 0;
        for (int dy = 0; dy < (int)(0.6 * s); dy++)
        {
            int c = 0;
            for (int dx = -r; dx <= r; dx++) if (ink[x + dx, y + dir * dy]) c++;
            if (c >= 1.8 * r) wide++;
        }
        return wide >= 0.3 * s;
    }

    /// <summary>At row y the ink around column x is no wider than a stem.</summary>
    static bool Thin(Img ink, int x, int y, double s)
    {
        int c = 0, r = (int)(0.5 * s);
        for (int dx = -r; dx <= r; dx++) if (ink[x + dx, y]) c++;
        return c <= 0.35 * s;
    }

    /// <summary>
    /// A stem a scan broke short of its beams: past the tip, ink in the stem's column (or beside it)
    /// running on, across breaks of up to half a space, into a beam -- rows of ink wider
    /// than a stem, a quarter of a space thick or more, that runs on sideways to another stem of the
    /// staff (an ornament over the tip is as thick: gen-orn's trills and mordents). The tip moves to
    /// the far end of the last such beam, so the beams are counted (kinkel10-4's scan: the stem white
    /// between the two beams of its group, counted one; a faded stem's last space under the beam,
    /// counted none). Without a beam the stem stands as found.
    /// </summary>
    static void ReachBeams(Img ink, List<Head> heads, Head h, double s)
    {
        int x = (int)Math.Round(h.StemX), dir = h.Up ? -1 : 1, tip = (int)h.Tip;    // towards the tip
        // (a removed staff line or ledger, and the white between two beams, run to 0.45 of a space there)
        int maxGap = Math.Max(2, (int)(0.5 * s)), reach = (int)(2 * s);
        bool Ink(int yy) => yy > 0 && yy < ink.H - 1 && (ink[x, yy] || ink[x - 1, yy] || ink[x + 1, yy]);
        int end = tip, wide = 0, gap = 0, edge = 0, from = 0, last = tip;
        bool beam = false;
        // (once among beams -- the tip may stand in one -- the ink past a break is the next beam, else it is
        // another's: piano003's eighth, its stem ending in its own beam, ran on to the other voice's stem
        // and beam half a space below)
        bool inBeams = !Thin(ink, x, tip, s) && !Thin(ink, x, tip - dir, s);
        // (a run of wide rows, from..to, is a beam of this stem's)
        void Close(int to)
        {
            if (wide >= Math.Max(1, (int)(0.25 * s)) && Sideways((from + to) / 2, from, to)) { beam = true; end = to; }
            wide = 0;
        }
        // (the column and its neighbours: the stem's found column may end where a ragged edge leaves it)
        // (and on past the reach while the walk has been mostly a stem's thin ink: a long stem broken
        // far from its beams -- kinkel10-4's scan, the low G's stem broken 3.6 spaces under them)
        int thin = 0;
        for (int y = tip + dir; Math.Abs(y - tip) <= reach || (Math.Abs(y - tip) <= 6 * s && thin >= 0.7 * Math.Abs(y - tip)); y += dir)
        {
            if (Ink(y))
            {
                if (Thin(ink, x, y, s)) thin++;
                // (the next beam's ragged edge may run thin for a few rows)
                if (gap > 0) edge = inBeams ? (int)(0.15 * s) + 1 : 0;
                if (edge > 0 && Thin(ink, x, y, s) && --edge == 0) break;
                if (!Thin(ink, x, y, s)) { inBeams = true; edge = 0; }
                gap = 0;
                if (Thin(ink, x, y, s)) { if (wide > 0) Close(y - dir); }
                else if (wide++ == 0) from = y;
                last = y;
                continue;
            }
            if (wide > 0) Close(y - dir);
            if (++gap > maxGap) break;
        }
        if (wide > 0) Close(last);
        if (DebugAt.HasValue && Math.Abs(h.X - DebugAt.Value.X) < s && Math.Abs(h.Y - DebugAt.Value.Y) < s)
            Console.WriteLine($"stem of head {h.X:F0},{h.Y:F0} past its tip {tip} at x {x}: " + string.Concat(Enumerable.Range(1, reach).Select(k => !Ink(tip + dir * k) ? '.' : Thin(ink, x, tip + dir * k, s) ? '|' : '='))
                              + (beam ? $" -> reaches the beams, to {end}" : ""));
        if (!beam) return;
        h.Tip = end;

        // (the row's ink from the stem's column out to either side, across breaks of a fifth of a space,
        // reaches another stem's column of the staff, a stem that ends in the beam's rows y0..y1)
        // (the beam may slope: the walk follows its middle, and another stem's tip is looked for in the
        // beam's rows moved by the slope there -- lang36-3's scan, a steep sixteenths' beam, the second
        // beam under a stem left at the first)
        bool Sideways(int yy, int y0, int y1)
        {
            var at = new Dictionary<int, int>();
            bool moved = false;
            bool Ends(Head o)
            {
                int c = (int)Math.Round(o.StemX);
                int? yo = at.TryGetValue(c, out var y) || at.TryGetValue(c - 1, out y) || at.TryGetValue(c + 1, out y) ? y : null;
                // (a walk the slope moved reaches a stem only at its column: past the walk's end the slope
                // says nothing -- gen-orn-scan's piano006, a turn over the beam followed round its curl)
                if (yo == null && moved) return false;
                int off = (yo ?? yy) - yy;
                return o.Tip >= Math.Min(y0, y1) + off - 0.25 * s && o.Tip <= Math.Max(y0, y1) + off + 0.25 * s;
            }
            // (a beam's stems stand a space or more apart: another stem the same way close by, ending in the
            // beam, is the beam's there -- lang36-3's eighth by the first stem of a beamed group)
            if (heads.Any(o => o != h && o.HasStem && o.St == h.St && o.Up == h.Up && Math.Abs(o.StemX - x) > 0.25 * s
                               && Math.Abs(o.StemX - x) < 0.8 * s && Ends(o))) return false;
            int jump = Math.Max(1, (int)(0.2 * s));
            foreach (int side in new[] { -1, 1 })
            {
                int far = x, miss = 0, yc = yy, band = Math.Abs(y1 - y0) + 1;
                moved = false;
                for (int xx = x + side; xx > 0 && xx < ink.W - 1 && miss <= jump; xx += side)
                {
                    int hit = -1;
                    foreach (int d in new[] { 0, -1, 1 })
                        if (yc + d > 0 && yc + d < ink.H - 1 && ink[xx, yc + d]) { hit = yc + d; break; }
                    if (hit < 0) { miss++; at[xx] = yc; continue; }
                    far = xx; miss = 0;
                    // (the middle of the run there, a beam's thickness or so: a stem or a head joined to it
                    // runs longer and leaves the walk where it was; a pixel a column at most, and half a
                    // pixel a column in all -- a beam's slope, not an ornament's curl over the beam, gen-orn's
                    // piano004 turns read as a second beam)
                    int a = hit, b = hit;
                    while (a > 1 && ink[xx, a - 1] && b - a < 2 * band) a--;
                    while (b < ink.H - 2 && ink[xx, b + 1] && b - a < 2 * band) b++;
                    int lim = (int)(0.5 * Math.Abs(xx - x)) + 1;
                    if (b - a + 1 <= band + 0.3 * s) yc = Math.Clamp(Math.Clamp((a + b) / 2, yc - 1, yc + 1), yy - lim, yy + lim);
                    moved |= yc != yy;
                    at[xx] = yc;
                }
                if (DebugAt.HasValue && Math.Abs(h.X - DebugAt.Value.X) < s && Math.Abs(h.Y - DebugAt.Value.Y) < s)
                    Console.WriteLine($"  beam row {yy} side {side}: ink to {far}; stems there: " + string.Join(" ", heads.Where(o => o != h && o.HasStem && o.St == h.St && Math.Sign(o.StemX - x) == side && Math.Abs(o.StemX - x) < 4 * s).Select(o => $"{o.StemX:F0}{(o.Up ? "up" : "down")} tip {o.Tip:F0}")));
                // (a stem of the same way, its tip in the beam: another voice's beam, piano009's under a
                // quarter's stem, joins stems the other way; another staff's, lang36-3's left hand's beam
                // half a space under the right hand's, holds no tip of this staff's stems)
                if (heads.Any(o => o != h && o.HasStem && o.St == h.St && o.Up == h.Up && Math.Abs(o.StemX - x) >= 0.8 * s
                                   && Math.Sign(o.StemX - x) == side && Math.Abs(o.StemX - x) <= Math.Abs(far - x) + 0.3 * s
                                   && Ends(o))) return true;
            }
            return false;
        }
    }

    /// <summary>Beams (or flags) crossing a column just beside the stem, near its tip.</summary>
    static int CountBeams(Img ink, int[] labels, Head h, double s)
    {
        int dir = h.Up ? 1 : -1;                  // from the tip towards the head
        int tip = (int)h.Tip;
        int limit = (int)(h.Up ? h.Y - 0.9 * s : h.Y + 0.9 * s);
        int stemLabel = labels[(int)((h.Tip + h.Y) / 2) * ink.W + (int)h.StemX];
        // (a stem broken short of its beams, ReachBeams: the beams are the tip's piece, the ink at the
        // tip in the stem's column or beside it)
        int tipLabel = 0;
        for (int k = 0; k <= 3 && tipLabel == 0; k++)
            foreach (int dx in new[] { 0, -1, 1 })
                if (tipLabel == 0) tipLabel = labels[(tip + dir * k) * ink.W + (int)h.StemX + dx];
        int best = 0;
        foreach (int side in new[] { -1, 1 })
        {
            // (the column further out within a beamlet's length: meters002's, about three quarters of a space)
            int count = Count((int)Math.Round(h.StemX + side * 0.5 * s), (int)Math.Round(h.StemX + side * 0.7 * s));
            best = Math.Max(best, count);
        }
        return best;

        int Count(int x, int xFar)
        {
            int count = 0, run = 0, gap = 0, first = 0, gap12 = -1, before = 0;
            int y = tip - dir * (int)(0.15 * s);
            // (a beam runs on towards the next stem: a run past the first, much thinner than a beam, that
            // the column further out does not see about its row, is a scan's blot on the stem --
            // kinkel10-4's g16 read as g32)
            bool Blot(int yy, int r) => count > 0 && first >= 0.4 * s && r < 0.7 * first
                && !Enumerable.Range(yy - (int)(0.3 * s), (int)(0.6 * s) + 1).Any(k => ink[xFar, k]);
            // a beam or flag is part of the stem's ink; an accidental or a slur nearby is not
            bool Joined(int yy) => labels[yy * ink.W + x] is var l && l != 0 && (l == stemLabel || l == tipLabel);
            for (; dir > 0 ? y <= limit : y >= limit; y += dir)
            {
                if (ink[x, y])
                {
                    if (run == 0) before = gap;
                    run++; gap = 0;
                }
                else
                {
                    // (past the beams, a run far thicker than the first is no beam: MuseJazz's natural
                    // before the next head, its stem touching the beams, band003's d'16 as d'32)
                    if (count > 0 && run > 1.6 * first) break;
                    // (the beams stand evenly spaced: past two, a thin run much farther off than the second
                    // from the first is none -- lang36-3's scan, a staff line's remains under a sixteenth's two
                    // beams read as a third. A tremolo's slashes, measured aslant, space unevenly: as thick)
                    if (run > 0 && count == 2 && gap12 >= 0 && before > 1.5 * gap12 + 0.15 * s && run < 0.7 * first) break;
                    if (run >= 0.28 * s && Joined(y - dir * (run / 2)) && !Blot(y - dir * (run / 2), run))
                    {
                        // (the first run may begin before the scan does, in a sloping beam: measured to its start)
                        if (count++ == 0) { first = run; for (int yy = y - dir * (run + 1); ink[x, yy] && first <= 2 * s; yy -= dir) first++; }
                        else if (count == 2) gap12 = before;
                    }
                    run = 0;
                    // the beams lie close together: past a space of white after them the ink is
                    // another's (the next head of the group at the column, its stem the same piece)
                    if (count > 0 && ++gap > 1.0 * s) break;
                }
            }
            // (a run cut off at the limit is measured to its end for the thickness test)
            int whole = run;
            if (run > 0 && count > 0) for (int yy = y; ink[x, yy] && whole <= 2 * first; yy += dir) whole++;
            bool farThin = run > 0 && count == 2 && gap12 >= 0 && before > 1.5 * gap12 + 0.15 * s && whole < 0.7 * first;
            if (run >= 0.28 * s && !farThin && !(count > 0 && whole > 1.6 * first) && Joined(y - dir * (run / 2)) && !Blot(y - dir * (run / 2), whole)) count++;
            if (DebugAt.HasValue && Math.Abs(h.X - DebugAt.Value.X) < s && Math.Abs(h.Y - DebugAt.Value.Y) < s)
                Console.WriteLine($"beams at x {x}: tip {tip} limit {limit} stemLabel {stemLabel} count {count}; column: " +
                    string.Concat(Enumerable.Range(0, Math.Abs(limit - tip) + 10).Select(k => ink[x, tip - dir * 5 + dir * k] ? (labels[(tip - dir * 5 + dir * k) * ink.W + x] == stemLabel ? '#' : 'o') : '.')));
            return count;
        }
    }

    /// <summary>
    /// A stem for a detected head the ink gave none (on a scan the stem breaks where it meets the
    /// head and where it crosses a staff line, and a black head left without one is read as a
    /// quarter note): the detector's stem tip standing in the head's stem column (at its right
    /// edge going up, at its left going down), two to eight spaces away, is its stem's. The
    /// nearest such tip gives the direction, the column and the tip; DetectedBeams then gives the count.
    /// </summary>
    static void DetectedStems(PageSymbols ps, double s)
    {
        var tips = ps.Detections.Where(t => t.Class.StartsWith("stem_")).ToList();
        if (tips.Count == 0) return;
        foreach (var h in ps.Heads)
        {
            if (h.HasStem || h.Score <= 0 || h.WholeShape) continue;
            Detection best = null;
            bool bestUp = false;
            double bd = double.MaxValue;
            foreach (var t in tips)
            {
                bool up = t.Y < h.Y;
                if (Math.Abs(t.X - (up ? h.R : h.L)) > 0.4 * s && !(h.Cross && Math.Abs(t.X - h.X) < 0.3 * s)) continue;
                double along = Math.Abs(t.Y - h.Y);
                // (a short one too, a space and a half, when the detector is fairly sure of it: two voices
                // pressed together, the lower voice's sixteenth's stem over its beams under the head --
                // lang36-3's scan, the head given the upper voice's stem six spaces up instead)
                if (along < (t.Score >= 0.45 ? 1.5 : 2.0) * s || along > 8 * s) continue;
                if (along < bd) { bd = along; best = t; bestUp = up; }
            }
            if (DebugAt.HasValue && Math.Abs(h.X - DebugAt.Value.X) < s && Math.Abs(h.Y - DebugAt.Value.Y) < s)
                Console.WriteLine($"stem from the detector for head {h.X:F0},{h.Y:F0}: {(best == null ? "none" : $"{best.Class} at {best.X:F0},{best.Y:F0} ({best.Score:F2})")}");
            if (best == null) continue;
            h.HasStem = true; h.Up = bestUp; h.StemX = best.X; h.Tip = best.Y;
        }
    }

    /// <summary>
    /// A detected black head left with no stem between two stems of one beam: an old print's stem faded
    /// to dashes, broken half a space at a time and its tip unseen by the detector (kinkel10-4's scan, the
    /// left hand's g,, among its group of sixteenths, read a quarter). The stems either side the same way, a
    /// beam's ink running from tip to tip, and the column at the head's edge inked over half its rows from
    /// the head to the beam, no other head along it: the stem runs to the beam between the two tips. So too a
    /// stem found broken off half a space or more short of that beam (the same left hand's upper notes of its
    /// zigzag, their long stems found to the first break, read quarters), the column inked from its tip on.
    /// </summary>
    static void StemsUnderBeams(Img ink, List<Head> heads, double s)
    {
        foreach (var h in heads.Where(h => h.Score > 0 && !h.Hollow && !h.Cross && !h.Slash && !h.WholeShape).ToList())
            foreach (bool up in h.HasStem ? new[] { h.Up } : new[] { true, false })
            {
                // (not a stem beside which another voice's stem runs on past its tip, half a space off: lang36-3,
                // the bass's ees,,8 hung from the upper voice's chord, that one's beam further down. Its chord's
                // own stem, in its column, runs on to the beam it reaches)
                if (h.HasStem && heads.Any(o => o != h && o.St == h.St && o.HasStem && o.Up == h.Up && Math.Abs(o.StemX - h.StemX) >= 0.3 * s && Math.Abs(o.StemX - h.StemX) < 0.8 * s
                                                && (h.Up ? o.Tip < h.Tip - 0.5 * s : o.Tip > h.Tip + 0.5 * s))) continue;
                double x = h.HasStem ? h.StemX : up ? h.R - 0.1 * s : h.L + 0.1 * s;
                var side = heads.Where(o => o != h && o.St == h.St && o.HasStem && o.Up == up && !o.Hollow && Math.Abs(o.StemX - x) > 0.5 * s && Math.Abs(o.StemX - x) < 5 * s).ToList();
                var left = side.Where(o => o.StemX < x).OrderBy(o => x - o.StemX).FirstOrDefault();
                var right = side.Where(o => o.StemX > x).OrderBy(o => o.StemX - x).FirstOrDefault();
                // (one stem of the beam only, the group's first or last note, the beam level from its tip: the
                // other side's nearest stem may be another group's)
                double tip = 0;
                foreach (var (l, r) in new[] { (left, right), (left, null), (null, right) })
                {
                    if (l == null && r == null) continue;
                    double TipAt(double at) => l == null ? r.Tip : r == null ? l.Tip : l.Tip + (r.Tip - l.Tip) * (at - l.StemX) / (r.StemX - l.StemX);
                    double t = TipAt(x), len = up ? h.Y - t : t - h.Y;
                    if (len < 2.2 * s || len > 8 * s || h.HasStem && (up ? h.Tip - t : t - h.Tip) < 0.5 * s) continue;
                    // the beam: ink at every column from tip to tip, within a third of a space of the line between them
                    bool beam = true;
                    for (int bx = (int)(l?.StemX ?? x); bx <= (int)(r?.StemX ?? x) && beam; bx++)
                    {
                        int ty = (int)Math.Round(TipAt(bx)), dy = (int)(0.35 * s);
                        beam = Enumerable.Range(-dy, 2 * dy + 1).Any(k => ink[bx, ty + k]);
                    }
                    if (beam) { tip = t; break; }
                }
                if (tip == 0) continue;
                // (no other head along the column: another voice's note over the head, its stem in the column)
                double start = h.HasStem ? h.Tip : h.Y, y0 = Math.Min(start, tip), y1 = Math.Max(start, tip);
                if (heads.Any(o => o != h && o.St == h.St && Math.Abs(o.X - h.X) < 0.6 * s && o.Y > y0 + 0.5 * s && o.Y < y1 - 0.3 * s)) continue;
                int from = (int)Math.Round(h.HasStem ? h.Tip : up ? h.Y - 0.6 * s : h.Y + 0.6 * s), to = (int)Math.Round(tip), dir = up ? -1 : 1;
                int bestX = 0; double bestShare = 0, wide = h.HasStem ? 0.15 : 0.3;
                for (int cx = (int)(x - wide * s); cx <= (int)(x + wide * s); cx++)
                {
                    int rows = 0, inked = 0;
                    for (int y = from; y != to; y += dir) { rows++; if (ink[cx, y] || ink[cx - 1, y] || ink[cx + 1, y]) inked++; }
                    double share = rows > 0 ? (double)inked / rows : 0;
                    if (share > bestShare) { bestShare = share; bestX = cx; }
                }
                if (DebugAt.HasValue && Math.Abs(h.X - DebugAt.Value.X) < s && Math.Abs(h.Y - DebugAt.Value.Y) < s)
                    Console.WriteLine($"stem under a beam for head {h.X:F0},{h.Y:F0}: {(up ? "up" : "down")} at x {bestX}, tip {tip:F0}{(h.HasStem ? $" (from {h.Tip:F0})" : "")}, inked {bestShare:F2}");
                if (bestShare < 0.5) continue;
                if (!h.HasStem) h.StemX = bestX;
                h.HasStem = true; h.Up = up; h.Tip = tip;
                break;
            }
    }

    /// <summary>
    /// A detected black head left with no stem, its stem a space or so long into beams: two voices pressed
    /// together, the lower voice's sixteenth on the second line and its beams right under the staff (lang36-3's
    /// scan, the stem's ink 1.2 spaces from the head's middle to the white between its two beams, the rules
    /// asking for 2.2 and the detector seeing no tip). The ink unbroken from the head's edge (down at its
    /// left, up at its right) a space and more is taken for its stem when it runs on into a beam that another
    /// stem the same way ends in (<see cref="ReachBeams"/>); else the head stands stemless. So too a head
    /// whose stem runs past another head of its column with no ink between the two: the detector's tip of
    /// the other head's stem (lang36-3's bes,, on the second line given the upper voice's bes,'s tip over
    /// the staff, six spaces up). Without the short stem it keeps that stem (the stem's ink fails on a scan
    /// where a chord's is broken: refusing the tip outright lost kinkel10-2's scan 11 notes).
    /// </summary>
    static void ShortStem(Img ink, List<Head> heads, Head h, double s)
    {
        if (h.HasStem && !heads.Any(o => o != h && o.St == h.St && Math.Abs(o.X - h.X) < 0.5 * s && Math.Abs(o.Y - h.Y) > 0.8 * s
                                         && (h.Up ? o.Y < h.Y && o.Y > h.Tip : o.Y > h.Y && o.Y < h.Tip) && !StemInked(ink, h.StemX, h.Y, o.Y, s)))
            return;
        var (had, wasUp, wasX, wasTip) = (h.HasStem, h.Up, h.StemX, h.Tip);
        int cy = (int)Math.Round(h.Y), off = (int)(0.15 * s), bestLen = 0, bestX = 0, bestTip = 0;
        bool bestUp = false;
        // (across a staff line taken out of the ink, a fifth of a space and a row, when a stem's thin stroke
        // goes on past it: lang36-3's scan, bar 7's bes,,, its stem broken at the staff's bottom line)
        int gap = Math.Max(1, (int)(0.2 * s)) + 1;
        foreach (bool up in new[] { true, false })
        {
            int dir = up ? -1 : 1;
            int x0 = (int)(up ? h.R - 0.35 * s : h.L - 0.12 * s), x1 = (int)(up ? h.R + 0.12 * s : h.L + 0.35 * s);
            for (int x = Math.Max(1, x0); x <= x1 && x < ink.W - 1; x++)
            {
                int y = cy + dir * off;
                if (y <= 0 || y >= ink.H - 1 || !ink[x, y]) continue;
                while (true)
                {
                    while (y + dir > 0 && y + dir < ink.H - 1 && ink[x, y + dir]) y += dir;
                    int g = 2;
                    while (g <= gap && y + dir * g > 0 && y + dir * g < ink.H - 1 && !ink[x, y + dir * g]) g++;
                    if (g > gap || y + dir * g <= 0 || y + dir * g >= ink.H - 1 || !Thin(ink, x, y + dir * g, s)) break;
                    y += dir * g;
                }
                int len = Math.Abs(y - cy);
                if (len > bestLen) { bestLen = len; bestX = x; bestTip = y; bestUp = up; }
            }
        }
        bool debug = DebugAt.HasValue && Math.Abs(h.X - DebugAt.Value.X) < s && Math.Abs(h.Y - DebugAt.Value.Y) < s;
        if (bestLen < 1.0 * s || bestLen >= 2.2 * s) { if (debug) Console.WriteLine($"short stem for head {h.X:F0},{h.Y:F0}: none ({bestLen / s:F2} spaces)"); return; }
        h.HasStem = true; h.Up = bestUp; h.StemX = bestX; h.Tip = bestTip;
        ReachBeams(ink, heads, h, s);
        bool beamed = Math.Abs(h.Tip - bestTip) > 0.1 * s;
        if (debug) Console.WriteLine($"short stem for head {h.X:F0},{h.Y:F0}: {(bestUp ? "up" : "down")} at x {bestX}, {bestLen / s:F2} spaces, {(beamed ? $"into beams to {h.Tip:F0}" : "no beam")}{(had ? $" (its stem past another head: {(wasUp ? "up" : "down")} to {wasTip:F0})" : "")}");
        if (!beamed) (h.HasStem, h.Up, h.StemX, h.Tip) = (had, wasUp, wasX, wasTip);
    }

    /// <summary>
    /// A chord's head at its stem's root the detector sees only weakly (under its threshold, as a black
    /// head or a grace note's): an old print's chords at 96 dpi, kinkel19-5's <f a c'>8 with the f at
    /// 0.39-0.44. Taken where a stemmed head's stem runs on from the head away from its tip, in the ink,
    /// down (up) to it, its column a chord's span from that head, clear of every head kept.
    /// </summary>
    static List<Head> RootHeads(Img d, Img ink, List<Staff> staves, PageSymbols ps, List<Detection> weakHeads, List<Head> graces, double s)
    {
        var roots = new List<Head>();
        var seen = weakHeads.Concat(ps.Detections.Where(Detector.IsWeakGrace)).Where(t => t.Score >= 0.3).ToList();
        if (seen.Count == 0) return roots;
        var found = new List<Head>();
        DetectedHeads(d, ink, seen, staves, s, found);
        foreach (var c in found.OrderByDescending(c => c.Score))
        {
            if (ps.Heads.Concat(graces).Any(h => h.St == c.St && Math.Abs(h.X - c.X) < 0.8 * s && Math.Abs(h.Y - c.Y) < 0.6 * s)) continue;
            var on = ps.Heads.FirstOrDefault(h => h.HasStem && !h.Hollow && !h.Grace && h.St == c.St && Math.Abs(h.X - c.X) < 0.4 * s
                && (h.Up ? c.Y - h.Y : h.Y - c.Y) is var dy && dy > 0.6 * s && dy < 3.6 * s);
            if (on == null) continue;
            // the stem's column inked all the way from the head to the root's middle (a scan's toner
            // breaks a row or two)
            int x = (int)Math.Round(on.StemX), ya = (int)Math.Round(Math.Min(on.Y, c.Y)), yb = (int)Math.Round(Math.Max(on.Y, c.Y)), inked = 0;
            for (int y = ya; y <= yb; y++) if (ink[x - 1, y] || ink[x, y] || ink[x + 1, y]) inked++;
            bool stem = inked >= 0.9 * (yb - ya + 1);
            if (DebugAt.HasValue && Math.Abs(c.X - DebugAt.Value.X) < 2 * s && Math.Abs(c.Y - DebugAt.Value.Y) < 3 * s)
                Console.WriteLine($"weak head at {c.X:F0},{c.Y:F0} ({c.Score:F2}) at the root of the stem of {on.X:F0},{on.Y:F0}: inked {inked}/{yb - ya + 1}{(stem ? ", taken" : "")}");
            if (!stem) continue;
            // (its edges its mate's on the stem: the toner narrows it, and its stem then stands past its edge,
            // a thick run beside its ledger line)
            c.Grace = false;
            (c.L, c.R, c.OuterW) = (on.L, on.R, on.OuterW);
            // (past the staff, counted from its ledger lines as printed: an old print's second ledger stands a
            // quarter of a space low, and kinkel19-5's g, hanging under it came out on -10. Its stem in the ink
            // vouches for it: the "no ledger" test passes it by. Off a page levelled by strips only on its own first
            // ledger, as Find takes such heads: a degraded page's blurred ledgers move them)
            c.Pos = LedgerPos(d, c, s, onFirstOnly: d.Bowed == null);
            if (DebugAt.HasValue && Math.Abs(c.X - DebugAt.Value.X) < 2 * s && Math.Abs(c.Y - DebugAt.Value.Y) < 3 * s)
                Console.WriteLine($"weak head at {c.X:F0},{c.Y:F0}: pos {c.Pos}");
            FindStem(ink, c, s);
            ps.Heads.Add(c);
            roots.Add(c);
        }
        return roots;
    }

    /// <summary>
    /// A grace note's stem, its flags or beams, and its slash, from the detector (S21): the stem's
    /// tip (stem_N) in the column of the head's edge, a space and a half to four and a half away (a
    /// grace note's stem is shorter than a note's), its N the flags or beams at it (one an eighth,
    /// two a sixteenth); an acciaccatura's slash (grace_slash) within a space and a quarter of the
    /// stem's column, from a space short of the head to a space past the tip (through the beam of two).
    /// </summary>
    static void GraceStems(Img ink, PageSymbols ps, double s)
    {
        var tips = ps.Detections.Where(t => t.Class.StartsWith("stem_") && t.Score >= TrustScore).ToList();
        var slashes = ps.Detections.Where(t => t.Class == "grace_slash" && t.Score >= TrustScore).ToList();
        // the detector's tips first, for every grace note: a group's member with none takes a neighbour's
        var marked = new Dictionary<Head, Detection>();
        foreach (var g in ps.Graces)
        {
            Detection best = null;
            bool bestUp = false;
            double bd = double.MaxValue;
            foreach (var t in tips)
            {
                bool up = t.Y < g.Y;
                if (Math.Abs(t.X - (up ? g.R : g.L)) > 0.5 * s) continue;
                double along = Math.Abs(t.Y - g.Y);
                if (along < 1.5 * s || along > 4.5 * s) continue;
                // (a stem in ink from the head to the tip: the other staff's grace group under it, its
                // up stem's tip a space and a half below, is not this one's down stem -- piano006's
                // grace { f16 e16 }, the e taking the left hand's tip and the two parted)
                if (!StemInked(ink, t.X, g.Y, t.Y, s)) continue;
                if (along < bd) { bd = along; best = t; bestUp = up; }
            }
            if (best != null) { g.HasStem = true; g.Up = bestUp; g.StemX = best.X; g.Tip = best.Y; g.Beams = best.Class[5] - '0'; marked[g] = best; }
        }
        // (the rules' stems first, for all: whether two are of one group may need both stems' tips)
        foreach (var g in ps.Graces.Where(g => !marked.ContainsKey(g))) FindStem(ink, g, s);
        foreach (var g in ps.Graces)
        {
            marked.TryGetValue(g, out var best);
            // (no tip the detector marks: the rules' stem, unbroken from the head's edge -- piano002's
            // eis16, its flag's end touching the beam over it; basic003's bis16 under two rests. In a
            // group, the way and the count of a neighbour's stem the detector marked: the rules took
            // the upper voice's stem for the first of piano006's grace { f16 e16 }, and the two were
            // parted between the voices)
            if (best == null)
            {
                // (a neighbour at its height: the other voice's group stands an octave off, piano007)
                var mate = ps.Graces.FirstOrDefault(o => o != g && Fellow(ink, g, o, s) && Math.Abs(o.Y - g.Y) < 2 * s && marked.ContainsKey(o) && o.Beams > 0);
                if (mate != null) { g.HasStem = true; g.Up = mate.Up; g.StemX = g.Up ? g.R : g.L; g.Tip = mate.Tip; g.Beams = mate.Beams; }
            }
            // a lone grace note (no other within two spaces and a half on its staff: a group's are beamed,
            // a space or so apart) wears a flag, not a beam
            bool lone = !ps.Graces.Any(o => o != g && Fellow(ink, g, o, s));
            double x = g.HasStem ? g.StemX : g.X;
            double ya = g.HasStem ? Math.Min(g.Y, g.Tip) - s : g.Y - 4 * s, yb = g.HasStem ? Math.Max(g.Y, g.Tip) + s : g.Y + 4 * s;
            var slash = slashes.FirstOrDefault(t => Math.Abs(t.X - x) < 1.25 * s && t.Y > ya && t.Y < yb);
            // the slash crosses the stem, rising to the right: a stroke joined to the stem's left side,
            // clear of the head and of the tip, a third of a space long and more (the ink the detector's
            // slash was checked by, left of the stem under the detection's height, was the head itself
            // under a slash seen low, piano009's ces'8; a sixteenth's second flag, right of the stem, was
            // seen as a slash, piano002's eis16). The detector misses a slash now and then (a down stem's,
            // piano009's a8): a stroke half a space long and more is one without it. A grace note with no
            // stem read has no slash read either
            // (a slash the detector sees stands unless the ink shows no stroke at all left of the stem and
            // the detector is unsure: a scan's slash, its toner thin, fails the stroke's measure, 20 of
            // them on gen-grace-scan. A slur crossing the stem low passes for one, piano009's ces'8)
            var stroke = lone && g.HasStem ? LeftStroke(ink, g, s) : (Rows: 0, Reach: 0, Slant: 0, Any: 0);
            g.Slashed = !lone ? slash != null
                      : g.HasStem && (slash != null ? stroke.Any > 0 || slash.Score >= SureScore
                                                    : stroke.Rows >= 0.1 * s && stroke.Reach >= 0.3 * s && stroke.Slant >= 0.12 * s);
            // the flags of a lone grace note, counted in the ink where the detector is unsure of its tip
            // or saw none: the runs of ink joined to the stem's right side between the tip and the head
            // (an eighth's flag joins it once and swings away; a sixteenth's two curls join it twice,
            // a notch between. The runs in a column beside the stem are no count: an eighth's flag,
            // swinging out and back, crosses it twice). A slashed lone grace note is an eighth, as
            // acciaccaturas are written: its slash through the flag looks like a second curl
            int runs = lone && g.HasStem && !g.Slashed && (best == null || best.Score < SureScore) ? FlagRuns(ink, g, s) : 0;
            if (lone && g.HasStem && g.Slashed) g.Beams = 1;
            else if (runs is 1 or 2) g.Beams = runs;
            if (DebugAt.HasValue && Math.Abs(g.X - DebugAt.Value.X) < 2 * s && Math.Abs(g.Y - DebugAt.Value.Y) < 3 * s)
                Console.WriteLine($"grace head at {g.X:F0},{g.Y:F0} ({g.Score:F2}): stem {(best != null ? $"{best.Class} at {best.X:F0},{best.Y:F0} ({best.Score:F2})" : g.HasStem ? $"by the rules at {g.StemX:F0}, tip {g.Tip:F0}" : "none")}, "
                    + $"{(g.Slashed ? "slashed" : "no slash")} (a stroke left reaching {stroke.Reach / s:F2} on {stroke.Rows / s:F2} rows, slanting {stroke.Slant / s:F2}, ink on {stroke.Any} rows{(slash == null ? ", none seen" : $", seen at {slash.X:F0},{slash.Y:F0} ({slash.Score:F2})")}), "
                    + $"{(lone ? "lone" : "in a group")}, flag runs {runs}, {g.Beams} flags, sign {(g.Acc.HasValue ? g.Acc.Value.ToString() : "none")}");
            if (DebugAt.HasValue && g.HasStem && Math.Abs(g.X - DebugAt.Value.X) < 2 * s && Math.Abs(g.Y - DebugAt.Value.Y) < 3 * s)
            {
                int xc = StemColumn(ink, g, s), step = Math.Max(1, (int)(s / 28));
                Console.WriteLine($"  the ink about the stem's column {xc}, from the tip to the head, a row every {step} px (| the column):");
                for (int y = (int)Math.Round(g.Tip); g.Up ? y <= g.Y : y >= g.Y; y += g.Up ? step : -step)
                    Console.WriteLine("  " + string.Concat(Enumerable.Range(-(int)(1.3 * s / step), (int)(2.6 * s / step) + 1).Select(k => { int xx = xc + k * step; return xx <= 0 || xx >= ink.W || y <= 0 || y >= ink.H ? ' ' : k == 0 ? (ink[xx, y] ? '|' : ':') : ink[xx, y] ? '#' : '.'; })));
            }
        }
    }

    /// <summary>
    /// Whether two grace notes are of one group: on one staff within two spaces and a half, or within
    /// four when a beam's ink joins their stems' tips (a sign between them spreads a group: piano008's
    /// grace { f16 gis16 } in gen-grace, 2.6 spaces apart, each read as a lone note's flags). The beam
    /// is looked for along the line from tip to tip, a fifth of a space in from the tips' end, ink
    /// within a tenth of a space of nine samples in ten.
    /// </summary>
    static bool Fellow(Img ink, Head a, Head b, double s)
    {
        if (a.St != b.St) return false;
        double dx = Math.Abs(a.X - b.X);
        if (dx < 2.5 * s) return true;
        if (dx >= 4 * s || !a.HasStem || !b.HasStem || a.Up != b.Up) return false;
        double inward = a.Up ? 0.2 * s : -0.2 * s;
        int n = 20, hit = 0, r = Math.Max(1, (int)Math.Round(0.1 * s));
        for (int k = 0; k <= n; k++)
        {
            double x = a.StemX + (b.StemX - a.StemX) * k / n, y = a.Tip + (b.Tip - a.Tip) * k / n + inward;
            int xi = (int)Math.Round(x), yi = (int)Math.Round(y);
            if (xi <= 0 || xi >= ink.W) continue;
            for (int yy = Math.Max(0, yi - r); yy <= Math.Min(ink.H - 1, yi + r); yy++)
                if (ink[xi, yy]) { hit++; break; }
        }
        return hit >= 0.9 * (n + 1);
    }

    /// <summary>
    /// Whether a stem's ink runs between a head and a tip the detector marked: of the rows between
    /// them, clear of the head (half a space) and of the tip (a quarter), three quarters and more
    /// have ink within a sixth of a space of the tip's column.
    /// </summary>
    static bool StemInked(Img ink, double x, double yHead, double yTip, double s)
    {
        int y0 = (int)Math.Round(Math.Min(yHead, yTip) + (yTip < yHead ? 0.25 : 0.5) * s);
        int y1 = (int)Math.Round(Math.Max(yHead, yTip) - (yTip < yHead ? 0.5 : 0.25) * s);
        int x0 = Math.Max(0, (int)Math.Round(x - s / 6)), x1 = Math.Min(ink.W - 1, (int)Math.Round(x + s / 6));
        if (y1 <= y0) return true;
        int rows = 0, inked = 0;
        for (int y = Math.Max(0, y0); y <= Math.Min(ink.H - 1, y1); y++)
        {
            rows++;
            for (int xx = x0; xx <= x1; xx++) if (ink[xx, y]) { inked++; break; }
        }
        return rows == 0 || inked >= 0.75 * rows;
    }

    /// <summary>
    /// A slanting stroke left of a lone grace note's stem, an acciaccatura's slash: over the rows
    /// from the tip to the head, clear of the tip (a quarter space) and of the head (six tenths of a
    /// space from its centre), the piece of ink nearest the stem's column on its left (within half a
    /// space of it, breaks of a twentieth of a space stepped over), its near edge D0 and its far
    /// edge D1 from the column. A row reaching a space and more is a staff line's, a ledger line's or
    /// a beam's and is no stroke's, nor are the rows within a sixth of a space of one (a beam's
    /// sloping end). The stroke is the longest chain of rows (breaks of a twentieth of a space
    /// stepped over) whose piece reaches a quarter space and is a twelfth wide and more: its Rows,
    /// its Reach (the furthest D1) and its Slant (the span of D0: the slash's arm, drawn from the
    /// stem's crossing away from the tip, moves left as it goes; a natural's upright stroke beside
    /// the stem, piano009, and a ledger line do not).
    /// </summary>
    static (int Rows, int Reach, int Slant, int Any) LeftStroke(Img ink, Head g, double s)
    {
        int x0 = StemColumn(ink, g, s);
        int ya = (int)Math.Round(Math.Min(g.Y, g.Tip)), yb = (int)Math.Round(Math.Max(g.Y, g.Tip));
        int n = yb - ya + 1, gapMax = Math.Max(2, (int)(0.07 * s)), any = 0;
        var d0 = new int[n]; var d1 = new int[n]; var full = new bool[n];
        for (int i = 0; i < n; i++)
        {
            int y = ya + i;
            d0[i] = -1;
            if (y <= 0 || y >= ink.H) continue;
            // the stem's own ink first (up to a seventh of a space; the column found is its right
            // edge on a scan): a piece running on from it begins at the stem
            int x = x0 - 1, white = 0, start = -1, end = -1;
            while (x > 0 && x0 - 1 - x < 0.15 * s && ink[x, y]) x--;
            if (x0 - 1 - x >= 0.15 * s && ink[x, y]) start = x0 - 1;
            // (else the nearest piece: its start past up to half a space of white; either way its
            // end past a break of up to a fourteenth; the row is full when the ink runs on to a
            // space from the column)
            for (; x > 0 && x0 - x <= 1.25 * s; x--)
            {
                if (ink[x, y]) { if (start < 0) start = x; end = x; white = 0; }
                else if (start < 0) { if (++white > 0.5 * s) break; }
                else if (++white > gapMax) break;
            }
            if (start < 0) continue;
            if (end < 0) end = start;
            d0[i] = x0 - 1 - start; d1[i] = x0 - end;
            full[i] = d1[i] >= 1.0 * s;
            if (!full[i] && d1[i] >= 0.15 * s && Math.Abs(y - g.Y) >= 0.6 * s && Math.Abs(y - g.Tip) >= 0.25 * s) any++;
        }
        int bestRows = 0, bestReach = 0, bestSlant = 0;
        int rows = 0, reach = 0, lo = int.MaxValue, hi = 0, gap = 0;
        // (the longest chain that slants, else the longest: a natural's upright stroke beside the
        // stem runs over more rows than the slash)
        void Close()
        {
            bool slants = hi - lo >= 0.08 * s, had = bestSlant >= 0.08 * s;
            if (rows > 0 && (slants && !had || slants == had && rows > bestRows)) { bestRows = rows; bestReach = reach; bestSlant = hi - lo; }
            rows = 0; reach = 0; lo = int.MaxValue; hi = 0; gap = 0;
        }
        for (int i = 0; i < n; i++)
        {
            int y = ya + i;
            bool ok = d0[i] >= 0 && !full[i] && d0[i] <= 0.5 * s && d1[i] >= 0.25 * s && d1[i] - d0[i] >= 0.08 * s
                      && Math.Abs(y - g.Y) >= 0.6 * s && Math.Abs(y - g.Tip) >= 0.25 * s
                      && !Enumerable.Range(Math.Max(0, i - (int)(0.16 * s)), 2 * (int)(0.16 * s) + 1).Any(j => j < n && full[j]);
            if (ok) { rows++; reach = Math.Max(reach, d1[i]); lo = Math.Min(lo, d0[i]); hi = Math.Max(hi, d0[i]); gap = 0; }
            else if (rows > 0 && ++gap > gapMax) Close();
        }
        Close();
        return (bestRows, bestReach, bestSlant, any);
    }

    /// <summary>
    /// The column of a grace note's stem's ink: the one within a fifth of a space of the stem's x
    /// (the detector's tip may stand a few pixels off it) with the most ink between the head and the tip.
    /// </summary>
    static int StemColumn(Img ink, Head g, double s)
    {
        int x0 = (int)Math.Round(g.StemX), best = x0, most = -1;
        int ya = (int)Math.Round(Math.Min(g.Y, g.Tip) + 0.3 * s), yb = (int)Math.Round(Math.Max(g.Y, g.Tip) - 0.3 * s);
        for (int x = x0 - (int)(0.2 * s); x <= x0 + (int)(0.2 * s); x++)
        {
            if (x <= 0 || x >= ink.W) continue;
            int n = 0;
            for (int y = Math.Max(1, ya); y <= Math.Min(ink.H - 1, yb); y++) if (ink[x, y]) n++;
            if (n > most) { most = n; best = x; }
        }
        return best;
    }

    /// <summary>
    /// The flags of a lone grace note, counted by the curls joining its stem: from the tip towards the
    /// head (stopping six tenths of a space short of the head's centre), the ink nearest the stem on
    /// its right, past the stem's own thickness (a seventh of a space): a flag's curl joins the stem
    /// at its root (ink joined to the stem a tenth of a space past the stem's own thickness) and
    /// swings away from it. An eighth's flag joins once; a sixteenth's second curl joins again under
    /// the first, past a notch. The count is the joined runs (each over a tenth of a space of rows),
    /// the next within two fifths of a space of the last; none at all is no count (0). Rows where the ink runs on a space
    /// and more to the right, or a quarter space to the left, are a staff line's, a ledger line's or
    /// a neighbouring group's beam's over the stem: they stand between no two runs of one flag (a
    /// beam's sloping end under it joins the stem like a root: basic004's bis8, its root under the
    /// beam, the end read as the one run; basic002's f8, its root between two beams).
    /// </summary>
    static int FlagRuns(Img ink, Head g, double s)
    {
        int x0 = StemColumn(ink, g, s), dir = g.Up ? 1 : -1;
        int y = (int)Math.Round(g.Tip), end = (int)Math.Round(g.Y - dir * 0.6 * s);
        var joinedAt = new List<int>(); var leftAt = new List<int>();
        for (; dir > 0 ? y <= end : y >= end; y += dir)
        {
            if (y <= 0 || y >= ink.H) break;
            int x = x0 + 1;
            while (x < ink.W - 1 && x - x0 < 1.3 * s && ink[x, y]) x++;
            joinedAt.Add(x - x0 - 1);
            int xl = x0 - 1;
            while (xl > 0 && x0 - xl < 0.5 * s && ink[xl, y]) xl--;
            leftAt.Add(x0 - 1 - xl);
        }
        // (the stem's own thickness: the middle of the rows where little else joins it. A curl joins
        // when the ink runs a tenth of a space past it: a scan's blurred stem is thicker, and a
        // sixteenth's notch, a fifth of a space of ink on gen-grace-scan's basic005, passed for joined)
        var thin = joinedAt.Where(j => j > 0 && j < 0.3 * s).OrderBy(j => j).ToList();
        double stemW = thin.Count > 0 ? thin[thin.Count / 2] : 0.1 * s;
        var rows = new System.Text.StringBuilder();
        for (int i = 0; i < joinedAt.Count; i++)
            rows.Append(joinedAt[i] >= 1.0 * s || leftAt[i] >= 0.25 * s ? '=' : joinedAt[i] >= stemW + 0.1 * s ? '#' : '.');
        // (a break of a twentieth of a space in a run is a scan's ragged edge, not a notch)
        var text = System.Text.RegularExpressions.Regex.Replace(rows.ToString(), $"#[.]{{1,{Math.Max(1, (int)(0.05 * s))}}}(?=#)", m => new string('#', m.Length)) + '.';
        int runs = 0, run = 0, gap = 0;
        bool lined = false;     // a line's or beam's row since the last run
        foreach (char c in text)
        {
            if (c == '#') { run++; continue; }
            if (run >= 0.1 * s)
            {
                // (the next curl within two fifths of a space: a scan's toner eats the thin root of the second)
                if (runs == 0 || gap <= 0.4 * s && !lined) runs++;
                gap = 0; lined = false;
            }
            run = 0;
            gap++;
            if (c == '=') lined = true;
        }
        if (DebugAt.HasValue && Math.Abs(g.X - DebugAt.Value.X) < 2 * s && Math.Abs(g.Y - DebugAt.Value.Y) < 3 * s)
            Console.WriteLine($"  flag rows from the tip at column {x0} (# ink joined to the stem, = a line's or a beam's row): {rows} -> {runs} runs");
        return runs;
    }

    /// <summary>
    /// The beams from the detector's stem-tip classes (stem_N: a tip with N beams or flags at it).
    /// On a scan the beams' toner thins inside and CountBeams, counting the ink runs in a column
    /// beside the stem, finds fewer than there are (the fill set: a third of the beamed heads wrong,
    /// nearly all too few). The tip the detector marks in the stem's column, on the stem's side of
    /// the head and nearest to the tip the rules found (a scan's stem breaks at a staff line, so the
    /// found tip may fall short), gives its count to every head on the stem.
    /// </summary>
    static void DetectedBeams(PageSymbols ps, double s, Img ink = null)
    {
        var tips = ps.Detections.Where(t => t.Class.StartsWith("stem_")).ToList();
        if (tips.Count == 0) return;
        var ruled = new Dictionary<Head, int>();    // the rules' count of each head the detector gave one
        var weakTwo = new List<Head>();             // a two the detector half saw where the rules count one
        // (a stem whose piece of ink holds no other stem of the staff, and whose ink right of it, from
        // the tip on, is one run of 0.55 to 0.9 of a space: two flags run together are a space and more)
        bool LoneFlag(Head h)
        {
            int height = ps.Labels.Length / ps.LabelW;
            int piece = LabelNear(ps.Labels, ps.LabelW, height, h.StemX, (h.Y + h.Tip) / 2, s);
            // (any other stem: two sixteenths half a space apart, a short beam between them -- kinkel10-2's)
            if (piece == 0 || ps.Heads.Any(o => o.HasStem && o.St == h.St && Math.Abs(o.StemX - h.StemX) >= 0.3 * s && Math.Abs(o.StemX - h.StemX) < 8 * s
                                                && LabelNear(ps.Labels, ps.LabelW, height, o.StemX, (o.Y + o.Tip) / 2, s) == piece)) { Dbg("shared piece " + piece); return false; }
            int dir = h.Up ? 1 : -1, x = (int)Math.Round(h.StemX + 0.5 * s), y = (int)h.Tip, run = 0;
            if (x < 0 || x >= ink.W) return false;
            // (a flag runs right of its stem a space or so, a beam on to the next stem, or left: a scan
            // cuts a beamed stem's piece from its beam -- kinkel10-4's left hand, its sixteenths read as eighths)
            int sx = (int)Math.Round(h.StemX), off = Math.Max(2, (int)(0.15 * s) + 1);
            for (int k = -(int)(0.6 * s); k < 0.6 * s; k++)
            {
                int yy = (int)h.Tip + dir * k;
                if (yy <= 0 || yy >= ink.H - 1) continue;
                int r = 0, l = 0;
                for (int xx = sx + off; xx < ink.W && (ink[xx, yy] || ink[xx, yy - 1] || ink[xx, yy + 1]); xx++) r++;
                for (int xx = sx - off; xx > 0 && (ink[xx, yy] || ink[xx, yy - 1] || ink[xx, yy + 1]); xx--) l++;
                if (r > 1.4 * s || l > 0.6 * s) { Dbg($"sideways r {r} l {l} at {yy}"); return false; }
            }
            void Dbg(string m) { if (DebugAt.HasValue && Math.Abs(h.X - DebugAt.Value.X) < s && Math.Abs(h.Y - DebugAt.Value.Y) < s) Console.WriteLine("LoneFlag: " + m); }
            // (the wedge leaves the stem a little under its tip)
            for (int k = 0; k < 0.8 * s && !(y >= 0 && y < ink.H && ink[x, y]); k++) y += dir;
            while (y >= 0 && y < ink.H && ink[x, y] && run <= 3 * s) { run++; y += dir; }
            Dbg($"run {run} to {y}");
            // (a wedge, over half a space: a sixteenth's flag is under it, its second one faint and missed)
            if (run < 0.55 * s || run >= 0.9 * s) return false;
            // (and nothing more past it within half a space: a second flag)
            for (int k = 0; k < 0.5 * s; k++, y += dir) if (y >= 0 && y < ink.H && ink[x, y]) return false;
            return true;
        }
        foreach (var h in ps.Heads)
        {
            if (!h.HasStem || h.Hollow) continue;
            Detection best = null;
            double bd = double.MaxValue;
            // (the heads of a chord share the stem's column and tip, so they all pick the same tip)
            foreach (var t in tips)
            {
                if (Math.Abs(t.X - h.StemX) > 0.5 * s) continue;
                double along = h.Up ? h.Y - t.Y : t.Y - h.Y;    // from the head towards the tip
                if (along < 1.5 * s) continue;
                double d = Math.Abs(t.Y - h.Tip);
                if (d < 4 * s && d < bd) { bd = d; best = t; }
            }
            if (DebugAt.HasValue && Math.Abs(h.X - DebugAt.Value.X) < s && Math.Abs(h.Y - DebugAt.Value.Y) < s)
                Console.WriteLine($"stem tip for head {h.X:F0},{h.Y:F0}: {(best == null ? "none" : $"{best.Class} at {best.X:F0},{best.Y:F0} ({best.Score:F2})")}, rules counted {h.Beams}");
            if (best == null) continue;
            int n = best.Class[5] - '0';
            // (the generator writes next to no 32nd notes, so the detector has seen one three-beam
            // tip and calls them two-beam: where the rules count three at a tip it calls two, three
            // it is. Fugue 1's 32nds; to be retired when the generator writes 32nds)
            if (n == 2 && h.Beams == 3) n = 3;
            // (a tip marked well short of the stem the rules followed into its beams, ReachBeams, with
            // fewer beams than they count, is the mark of the stem's break: kinkel10-4's scan, stem_1 at
            // the white between two beams)
            ruled[h] = h.Beams;
            if (n < h.Beams && (h.Up ? best.Y - h.Tip : h.Tip - best.Y) > 0.75 * s) continue;
            // (an old print's eighth flag, a filled wedge, the detector less than sure calls two: where
            // the rules count one on a stem no other stem shares (a flag, not a beam), its ink beside the
            // stem one run shorter than two flags run together, the one stands -- kinkel10-4's voice,
            // its eighths read as sixteenths, the flag's run 0.64 of a space. Up stems only: kinkel10-2's
            // down-stemmed sixteenths in pairs half a space apart passed for flags, their short beam a wedge)
            if (n == 2 && h.Beams == 1 && h.Up && best.Score < SureScore && ink != null && ps.Labels != null && LoneFlag(h))
            {
                if (DebugAt.HasValue && Math.Abs(h.X - DebugAt.Value.X) < s && Math.Abs(h.Y - DebugAt.Value.Y) < s)
                    Console.WriteLine($"stem tip for head {h.X:F0},{h.Y:F0}: one flag's ink, the rules' 1 stands");
                continue;
            }
            if (n == 2 && h.Beams == 1 && best.Score < 0.6) weakTwo.Add(h);
            h.Beams = n;
        }
        // A stem the detector marks no tip of, on a beam whose other stems it does, as many beams at
        // each, more than the rules count here, has their count where the rules count one of those as
        // they count this one: they misread the beam's ink (kinkel19-1's scan, the two beams one run of
        // toner along the whole group, counted one nearly everywhere; the c16 among stem_2s read as an eighth)
        if (ps.Labels == null || ink == null) return;
        int height = ps.Labels.Length / ps.LabelW;
        // (the beams' ink beside the stem from its tip, as thick as two beams run together: a single beam,
        // an eighth's among sixteenths, is half a space, a space where it lies on a staff line -- kinkel10-1's,
        // 10-4's, 7-1's scans, b8. a16 and g,8 g,16 g,16, the eighth given its mates' two. Two run together
        // and the white between are 1.25 spaces; kinkel19-1's measure 1.5 to 1.65)
        bool Merged(Head h)
        {
            int dir = h.Up ? 1 : -1, best = 0;
            foreach (int side in new[] { -1, 1 })
            {
                int x = (int)Math.Round(h.StemX + side * 0.5 * s), y = (int)h.Tip, run = 0;
                if (x < 0 || x >= ink.W) continue;
                for (int k = 0; k < 0.3 * s && !(y >= 0 && y < ink.H && ink[x, y]); k++) y += dir;
                while (y >= 0 && y < ink.H && ink[x, y] && run <= 3 * s) { run++; y += dir; }
                best = Math.Max(best, run);
            }
            return best >= 1.3 * s;
        }
        int Piece(Head h) => LabelNear(ps.Labels, ps.LabelW, height, h.StemX, (h.Y + h.Tip) / 2, s);
        // And the other way: a two the detector half saw (under 0.6) at a stem the rules give one, on a beam
        // whose other stems, two or more, both give one, its ink beside the stem no two beams' thick, is
        // one (kinkel7-6's scan, the left hand's chords at a group's end read as sixteenths, 0.53)
        foreach (var h in weakTwo)
        {
            int piece = Piece(h);
            if (piece == 0) continue;
            var mates = ps.Heads.Where(o => o.HasStem && !o.Hollow && o.St == h.St && Math.Abs(o.StemX - h.StemX) >= 0.8 * s && Math.Abs(o.StemX - h.StemX) < 8 * s && Piece(o) == piece).ToList();
            if (DebugAt.HasValue && Math.Abs(h.X - DebugAt.Value.X) < s && Math.Abs(h.Y - DebugAt.Value.Y) < s)
                Console.WriteLine($"beams for head {h.X:F0},{h.Y:F0}: a weak 2; its piece's other stems {string.Join(" ", mates.Select(o => $"{o.StemX:F0}{(o.Up ? "u" : "d")}:{o.Beams}/{(ruled.TryGetValue(o, out var r) ? r : -1)}"))}, merged {Merged(h)}");
            mates = mates.Where(o => o.Up == h.Up && ruled.ContainsKey(o)).ToList();
            if (mates.Count < 2 || mates.Any(o => o.Beams != 1 || ruled[o] != 1) || Merged(h)) continue;
            if (DebugAt.HasValue && Math.Abs(h.X - DebugAt.Value.X) < s && Math.Abs(h.Y - DebugAt.Value.Y) < s)
                Console.WriteLine($"beams for head {h.X:F0},{h.Y:F0}: 2 -> 1, as its beam's {mates.Count} other stems");
            h.Beams = 1;
        }
        foreach (var h in ps.Heads.Where(h => h.HasStem && !h.Hollow && h.Beams > 0 && !ruled.ContainsKey(h)).ToList())
        {
            int piece = Piece(h);
            if (piece == 0) continue;
            var mates = ruled.Keys.Where(o => o.St == h.St && o.Up == h.Up && Math.Abs(o.StemX - h.StemX) >= 0.8 * s && Math.Abs(o.StemX - h.StemX) < 8 * s && Piece(o) == piece).ToList();
            if (mates.Count < 1 || mates.Any(o => o.Beams != mates[0].Beams) || mates[0].Beams <= h.Beams || !mates.Any(o => ruled[o] == h.Beams) || !Merged(h)) continue;
            if (DebugAt.HasValue && Math.Abs(h.X - DebugAt.Value.X) < s && Math.Abs(h.Y - DebugAt.Value.Y) < s)
                Console.WriteLine($"beams for head {h.X:F0},{h.Y:F0}: {h.Beams} -> {mates[0].Beams}, as its beam's {mates.Count} other stems the detector marks");
            h.Beams = mates[0].Beams;
        }
    }
}
