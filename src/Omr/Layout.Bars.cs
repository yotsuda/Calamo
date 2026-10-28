namespace Omr;

static partial class Layout
{
    /// <summary>A barline covers every staff of the system from its top line to its bottom line at one x.</summary>
    public static void FindBars(Img d, SystemInfo sys, double s, List<Detection> det = null)
    {
        int left = sys.Staves.Min(t => t.Left), right = sys.Staves.Max(t => t.Right);
        var cand = new bool[right + 3];
        // (three staves or more: no stem stands at one x on all of them, and a scanned barline may
        // be tested leniently; on one or two staves a stem ending at an outer line would pass)
        int width = sys.Staves.Count >= 3 ? 2 : 1;
        for (int x = left; x <= right; x++)
        {
            bool all = true;
            // (the line is followed down the system, staff by staff: on a page turned a quarter of a
            // degree a barline drifts five pixels over a band score's ten staves -- Time After Time)
            int at = x;
            for (int k = 0; k < sys.Staves.Count; k++)
            {
                var st = sys.Staves[k];
                bool any = false;
                // (a row's ink in the column or the one right of it: a thin scanned line wanders
                // between two columns, Time After Time's vocal barlines 0.96 in either)
                for (int dx = -2; dx <= 2 && !any; dx++)
                    if (ColFrac(d, at + dx, st, width) >= 0.97 && !Overruns(d, at + dx, sys, k, s)) { any = true; at += dx; }
                if (!any) { all = false; break; }
            }
            cand[x] = all;
        }
        var centers = new List<double>();
        for (int x = left; x <= right;)
        {
            if (!cand[x]) { x++; continue; }
            int a = x;
            while (x <= right && cand[x]) x++;
            double c = (a + x - 1) / 2.0;
            // a barline stands clear: in the staff's spaces, nothing touches it on either side
            // (a C clef's thin bar has the clef's curves right beside it)
            if (!Clear(d, sys.Staves[0], a - 2, x + 1, s)) { if (Signatures.Verbose) Console.WriteLine($"    barline? x {a}-{x - 1} (system {sys.Index + 1}): not clear"); continue; }
            if (centers.Count > 0 && c - centers[^1] < 1.0 * s) centers[^1] = c; // double barline
            else centers.Add(c);
        }
        // The barlines the learnt detector found (one per staff it crosses) that the rules did not:
        // on a scan a thin barline fades and breaks, and the column test above fails on it, merging
        // two measures. A detected line counts when it is seen on every staff of the system, or the
        // detector is sure of it on one; Reader still checks each barline against the stems and the
        // bar lengths, as it checks the rules' own.
        if (det != null)
        {
            var seen = det.Where(t => t.Class == "barline" && sys.Staves.Any(st => Math.Abs(t.Y - st.Mid) < 2.5 * s)).OrderBy(t => t.X).ToList();
            // ... and the rules' barlines that are stems: a stem spanning the staff, a beam or a
            // chord's head at its end, passes the column test. Reader tells such a line by the bar
            // lengths, but on a scan the measures do not fill their time anyway. Where the detector
            // sees no barline and does see a stem's tip in the column, the line is the stem.
            // (a stem that passes the column test covers the staff from its top line to its bottom
            // line, so its tip stands at or beyond the staff's edge -- and the column test asks
            // this of every staff of the system, so the stem must be seen on every staff too: in
            // Fugue 1 a real barline the detector missed had a beamed group starting right beside
            // it on one staff, its beam on the top line)
            var tips = det.Where(t => t.Class.StartsWith("stem_")).Select(t => (T: t, St: sys.Staves.OrderBy(st => Math.Abs(t.Y - st.Mid)).First()))
                          .Where(p => Math.Abs(p.T.Y - p.St.Mid) < 4.5 * s && (p.T.Y <= p.St.Top + 0.6 * s || p.T.Y >= p.St.Bottom - 0.6 * s)).ToList();
            bool StemOnEveryStaff(double c) => sys.Staves.All(st => tips.Any(p => p.St == st && Math.Abs(p.T.X - c) < 0.5 * s));
            if (Signatures.Verbose)
                foreach (var c in centers.Where(c => !seen.Any(t => Math.Abs(t.X - c) < 1.0 * s)))
                    Console.WriteLine($"    barline at x {c:F0} (system {sys.Index + 1}): the detector sees none{(StemOnEveryStaff(c) ? ", a stem tip at the staff's edge in its column on every staff: dropped" : ", kept")}");
            centers.RemoveAll(c => !seen.Any(t => Math.Abs(t.X - c) < 1.0 * s) && StemOnEveryStaff(c));
            // ... and a C clef's thin bar: the clef's curves beside it keep it from standing clear,
            // but the toner hollows them out (ledger010 on fill: a bar 0.1 space left of the clef's
            // centre, an empty first measure, every bar after it one off). Where the detector sees
            // no barline and does see a clef on the line, the line is the clef's.
            var clefs = det.Where(t => t.Class.StartsWith("clef_") && t.Score >= Symbols.TrustScore
                                       && sys.Staves.Any(st => Math.Abs(t.Y - st.Mid) < 2.5 * s)).ToList();
            bool OnClef(double c) => !seen.Any(t => Math.Abs(t.X - c) < 1.0 * s) && clefs.Any(t => Math.Abs(t.X - c) < 0.8 * s);
            if (Signatures.Verbose)
                foreach (var c in centers.Where(OnClef))
                    Console.WriteLine($"    barline at x {c:F0} (system {sys.Index + 1}): on a detected clef, dropped");
            centers.RemoveAll(OnClef);
            for (int i = 0; i < seen.Count;)
            {
                int j = i;
                while (j + 1 < seen.Count && seen[j + 1].X - seen[i].X < 0.5 * s) j++;
                var group = seen.GetRange(i, j - i + 1);
                i = j + 1;
                double x = group.Average(t => t.X);
                var onStaves = group.Select(t => sys.Staves.OrderBy(st => Math.Abs(t.Y - st.Mid)).First()).Distinct().ToList();
                if (onStaves.Count < sys.Staves.Count && group.Max(t => t.Score) < Symbols.SureScore)
                {
                    // seen on some staves only, and not surely: it fills in what the rules read nothing
                    // of, so TrustScore is enough when nothing says the line is a stem -- no stem's tip
                    // stands at the edge of any staff it is not seen on, in its column (a retrain's swing:
                    // piano002's scan had a barline at 0.86 and 0.87 on its two staves, then 0.70 on one)
                    // (unless the line is seen on two staves or more and its column is dark from the top
                    // line to the bottom line on the staff with the tip: kinkel10-2's scan had a barline
                    // at 0.77 on the voice's and the right hand's staves, the left hand's beamed group
                    // starting right beside it, its stem's tip five pixels off at the bottom line --
                    // a stem from a head inside the staff covers only part of it)
                    bool Dark(Staff st) => Enumerable.Range((int)(x - 0.3 * s), (int)(0.6 * s) + 1).Any(cx => ColFrac(d, cx, st, 2) >= 0.9);
                    bool Spanned(Staff st) => onStaves.Count >= 2 && Dark(st);
                    bool stemInstead = sys.Staves.Where(st => !onStaves.Contains(st)).Any(st => tips.Any(p => p.St == st && Math.Abs(p.T.X - x) < 0.5 * s) && !Spanned(st));
                    if (group.Max(t => t.Score) < Symbols.TrustScore || stemInstead) continue;
                    // (nor is a wavy line: an arpeggio's wave, up a chord's left, is no barline)
                    if (onStaves.Any(st => Arpeggios.IsWave(d, st, x, s)))
                    {
                        if (Signatures.Verbose) Console.WriteLine($"    barline at x {x:F0} (system {sys.Index + 1}): seen on {onStaves.Count} of {sys.Staves.Count} staves, a wave: dropped");
                        continue;
                    }
                    // (a system of three staves or more has its barlines through all of them: one
                    // seen unsurely on a single staff is something else -- Fantasy p3, 0.54 and 0.58
                    // on the guitar's staff alone of seven, two measures too many -- unless its column
                    // is dark from the top line to the bottom line on every other staff: detector35b
                    // saw kinkel10-4's scan's barline at 0.83 on the left hand's staff alone, a sharp
                    // set right after it on the voice's and the right hand's)
                    if (sys.Staves.Count >= 3 && onStaves.Count < 2 && !sys.Staves.Where(st => !onStaves.Contains(st)).All(Dark)) continue;
                }
                if (centers.Any(c => Math.Abs(c - x) < 1.0 * s)) continue;
                centers.Add(x);
            }
            centers.Sort();
        }
        sys.Bars.Clear();
        sys.Bars.Add(left);
        foreach (var c in centers)
        {
            // the system's opening line, and anything in the clef's room after it (a C clef's
            // bars): no measure is narrower than the clef that starts every system
            if (c - left < 3.0 * s) continue;
            sys.Bars.Add(c);
        }
        if (sys.Bars.Count > 1 && right - sys.Bars[^1] < 0.8 * s) sys.Bars.RemoveAt(sys.Bars.Count - 1); // closing barline = staff end
        // a courtesy time or key signature after the last barline is not a measure: that
        // barline ends the system's last measure
        if (!(sys.Bars.Count > 1 && right - sys.Bars[^1] < 3.5 * s)) sys.Bars.Add(right);
    }

    /// <summary>
    /// A barline stops at its staff's outer lines, or runs on to the next staff of the system.
    /// A stem that happens to span the staff runs on past a line (to its head or beam) and stops.
    /// </summary>
    static bool Overruns(Img d, int x, SystemInfo sys, int k, double s)
    {
        var st = sys.Staves[k];
        // three staves or more: no stem stands at one x on all of them, and a barline between two
        // staves may be broken by what stands on it (a band score's "4" over a slash repeat)
        if (sys.Staves.Count >= 3) return false;
        // (ink in nine rows in ten, within a pixel of the column: the line drifts on a turned page,
        // and a scanned one breaks -- Time After Time's grand staff; a stem gets nowhere near the next staff)
        bool Solid(double y0, double y1)
        {
            int n = 0, dark = 0;
            for (int y = (int)y0; y <= (int)y1; y++) { n++; if (d[x, y] || d[x - 1, y] || d[x + 1, y]) dark++; }
            return dark >= 0.9 * n;
        }
        bool Dark(double y) => d[x, (int)y];
        bool up = Dark(st.Top - 0.3 * s) && Dark(st.Top - 0.6 * s);
        bool down = Dark(st.Bottom + 0.3 * s) && Dark(st.Bottom + 0.6 * s);
        // (the staves next to it: a TAB or a one-line staff too, which a barline runs on into)
        int ai = sys.All.IndexOf(st);
        if (up && !(ai > 0 && Solid(sys.All[ai - 1].Bottom, st.Top))) return true;
        if (down && !(ai + 1 < sys.All.Count && Solid(st.Bottom, sys.All[ai + 1].Top))) return true;
        // a stem that spans the staff ends in a head or a beam near an outer line: a blob joined to
        // the column, wide and thick. A tie or slur across a barline is wide but thin.
        int Run(int yy)
        {
            if (!d[x, yy]) return 0;
            int a = x, b = x;
            while (d[a - 1, yy] && x - a < 2 * s) a--;
            while (d[b + 1, yy] && b - x < 2 * s) b++;
            return b - a + 1;
        }
        // thick: most rows within 0.35 space either way are wide too (a beam is half a space thick,
        // a head a space; an arc over the barline is a quarter)
        // (only a lone staff needs this: across several staves no stem stands at one x in all of
        // them, while a beam passing over a real barline would look just like this)
        if (sys.Staves.Count > 1) return false;
        int t = (int)(0.35 * s);
        foreach (double edge in new[] { st.Top, st.Bottom })
            for (int yy = (int)(edge - 0.8 * s); yy <= (int)(edge + 0.8 * s); yy += 2)
            {
                if (Run(yy) < 0.8 * s) continue;
                int wide = 0;
                for (int dy = -t; dy <= t; dy++) if (Run(yy + dy) >= 0.5 * s) wide++;
                if (wide >= 0.6 * (2 * t + 1)) return true;
            }
        return false;
    }

    /// <summary>Columns a..b hold a line with white beside it, in at least three of the four spaces.</summary>
    static bool Clear(Img d, Staff st, int a, int b, double s)
    {
        // (a C clef's curves touch its thin bar; notes in a dense bar come close to a barline, not onto it)
        int reach = (int)(0.12 * s), clear = 0;
        bool outer = true;
        for (int k = 0; k < 4; k++)
        {
            int y = (int)Math.Round((st.Lines[k].Y + st.Lines[k + 1].Y) / 2);
            // step off the line itself, then look for ink within reach on each side
            int l = a; while (l > a - 6 && d[l, y]) l--;
            int r = b; while (r < b + 6 && d[r, y]) r++;
            bool inkL = false, inkR = false;
            for (int x = l - reach; x < l; x++) inkL |= d[x, y];
            for (int x = r + 1; x <= r + reach; x++) inkR |= d[x, y];
            if (!inkL && !inkR) clear++;
            else if (k == 0 || k == 3) outer = false;
        }
        // (or the outer two: a repeat's dots stand beside it in the middle two, Time After Time p03)
        return clear >= 3 || outer;
    }

    static double ColFrac(Img d, int x, Staff st, int width = 1)
    {
        int y0 = (int)Math.Round(st.Top), y1 = (int)Math.Round(st.Bottom), c = 0;
        // (a one-line staff's barline runs about a space above and below its line: half a space each way asked)
        if (st.OneLine) { y0 = (int)Math.Round(st.Mid - 0.5 * st.S); y1 = (int)Math.Round(st.Mid + 0.5 * st.S); }
        // (and a scanned barline may stop a row short of the outer lines' middles)
        else if (width > 1) { y0++; y1--; }
        for (int y = y0; y <= y1; y++) if (d[x, y] || width > 1 && d[x + 1, y]) c++;
        return c / (double)(y1 - y0 + 1);
    }
}
