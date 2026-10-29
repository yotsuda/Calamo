namespace Omr;

/// <summary>
/// Glissandi: a straight thin line from a note's head to the next note's. Lily# (GlissandoEngraver)
/// draws it from the head's right edge at its middle to the next head's left edge (or the left of
/// its accidentals) at its middle, each end pulled half a space in along the line, a tenth of a space
/// thick; a chord's heads each to the next chord's, in written order. As in LilyPond, nothing spaces
/// the notes for it: before a note with an accidental close by it may be a stub (under a space and a
/// half: not read). Read by rule: for each head, the line toward a head right of it on its staff
/// (or toward the left of the accidentals before it, where there are some), followed in the ink from
/// half a space past the head for as long as the ink lies straight on it and runs thin across it (a
/// beam is half a space thick, a flag or a slur bends away, a tie leaves the head over or under its
/// middle), and running on to short of where it is aimed. No steeper than six over five (a stem's or
/// a sign's upright), not through another note's head, and not to a head with an accidental before it
/// unless it stops there. Written on the first note as @glissando.
/// </summary>
static class Glissandi
{
    public static bool Verbose;
    // (OMR_GLISS_DEBUG=x: how the lines from the heads about x measured)
    static readonly double? DebugX = double.TryParse(Environment.GetEnvironmentVariable("OMR_GLISS_DEBUG"), out var v) ? v : null;

    /// <summary>A line found: from the head to the head, how long it ran in the ink, and the share of it thin across.</summary>
    public sealed record Line(Head From, Head To, double Length, double Thin)
    {
        /// <summary>A tie read from the line's end alone (Head.TieByEnd) was taken off its chord: none is left on it.</summary>
        public bool Untied;
    }

    /// <summary>The glissando lines between the page's heads, in the page as binarized (the signs looked for in the ink without the staff lines).</summary>
    public static List<Line> Find(Img d, Img ink, List<Head> all, double s, List<Rest> rests = null)
    {
        var found = new List<Line>();
        var heads = all.Where(h => !h.Cross && !h.Slash && !h.Grace).ToList();
        foreach (var a in heads)
        {
            Line best = null;
            foreach (var b in heads)
            {
                // (on the same staff, a space and more over or under: a slant, not a tie's level; and
                // room between the heads for the line and its two half-space gaps)
                if (b.St != a.St || b.L - a.R < 2 * s || b.L - a.R > 16 * s || Math.Abs(b.Y - a.Y) < 0.8 * s) continue;
                // (nor from a chord tied into b's: the line it follows is a tie that sags from one of its
                // heads past another's, tabi's photo, a long tie across a bar line. A tie read from its
                // end alone is no proof: a glissando's line ends as one does, chords001)
                if (heads.Any(t => t.Tie && !t.TieByEnd && t.St == a.St && Math.Abs(t.X - a.X) < 1.5 * s
                                   && heads.Any(u => u.St == b.St && u.Pos == t.Pos && Math.Abs(u.X - b.X) < 1.5 * s))) continue;
                // (the line ends at the head's left, or at the left of the accidentals of its column, any of its
                // chord's: a space to three back, an accidental's width and its gap and more, tried by halves)
                // (and none at the head's left when its column has signs there: Lily# stops before them)
                for (double back = 0; back <= 3; back += back == 0 ? 1 : 0.5)
                {
                    bool debug = DebugX is { } dx && Math.Abs(a.X - dx) < s;
                    if (back > 0 ? !SignAt(ink, b.L - back * s, b.L - 0.3 * s, b.Y, s) : SignBefore(ink, a, b, heads, s))
                    {
                        if (debug && back == 0) Console.WriteLine($"glissando debug: {a.R:F0},{a.Y:F0} to {b.L:F0},{b.Y:F0}: a sign before it");
                        continue;
                    }
                    // (what stands between a line's end and b is b's accidentals, not a rest: two sixteenth rests
                    // before a head, gen-trill's piano010, the rests' strokes lined up as a line to before them)
                    if (back > 0 && rests != null && rests.Any(r => r.St == b.St && r.R > b.L - back * s && r.L < b.L))
                    {
                        if (debug) Console.WriteLine($"glissando debug: {a.R:F0},{a.Y:F0} to {b.L - back * s:F0},{b.Y:F0}: a rest before it");
                        continue;
                    }
                    // (nor through another note's head on the way: a line from the chord before runs on through this one's)
                    if (Crosses(a, b, back * s, heads, s))
                    {
                        if (debug) Console.WriteLine($"glissando debug: {a.R:F0},{a.Y:F0} to {b.L - back * s:F0},{b.Y:F0}: through a head");
                        continue;
                    }
                    var (length, thin, reach) = Follow(d, a, b, back * s, s);
                    if (debug)
                        Console.WriteLine($"glissando debug: {a.R:F0},{a.Y:F0} to {b.L - back * s:F0},{b.Y:F0}: {length / s:F1} spaces, thin {thin:F2}, {(reach ? "reaches" : "short")}");
                    // (a space and a half and more of it, straight and thin, and on to the end aimed at)
                    if (length >= 1.5 * s && thin >= 0.7 && reach && (best == null || length > best.Length)) best = new Line(a, b, length, thin);
                }
            }
            if (best == null) continue;
            found.Add(best);
            // (the line's end the detector took for a tie's: no tie from a's chord into b's)
            var chord = heads.Where(t => t.St == a.St && Math.Abs(t.X - a.X) < 1.5 * s).ToList();
            var untie = chord.Where(t => t.TieByEnd && heads.Any(u => u.St == best.To.St && u.Pos == t.Pos && Math.Abs(u.X - best.To.X) < 1.5 * s)).ToList();
            foreach (var t in untie) t.Tie = t.TieByEnd = false;
            best.Untied = untie.Count > 0 && !chord.Any(t => t.Tie);
        }
        return found;
    }

    /// <summary>The lines found, as @glissando on their first notes' events.</summary>
    public static void Apply(PageResult res, List<Line> lines)
    {
        double s = res.S;
        foreach (var g in lines)
        {
            var evs = Articulations.Events(res, g.From, s);
            if (Verbose) res.Notes.Add($"glissando {g.From.R:F0},{g.From.Y:F0} to {g.To.L:F0},{g.To.Y:F0}: {g.Length / s:F1} spaces, thin {g.Thin:F2}, part {g.From.St.Part}, "
                                       + (evs.Count == 0 ? "no event" : $"the note at {evs[0].X:F0}"));
            foreach (var e in evs)
            {
                e.Articulations ??= new List<string>();
                if (!e.Articulations.Contains("glissando")) e.Articulations.Add("glissando");
                if (g.Untied) e.Tie = false;
            }
        }
    }

    // whether a sign's ink stands where a line ending at x would stop before it: half a space to a space right of x (and short of the
    // head, before), within a space and a half of the head's height (in the ink without the staff lines)
    static bool SignAt(Img ink, double x, double before, double y, double s)
    {
        for (int yy = (int)(y - 1.5 * s); yy <= y + 1.5 * s; yy++)
            for (int xx = (int)(x + 0.4 * s); xx <= Math.Min(x + 1.0 * s, before); xx++)
                if (ink[xx, yy]) return true;
        return false;
    }

    // whether ink stands left of head b, a third of a space to a space and a fifth off it and within a space and a half of its height, off the
    // line from a and out of the heads there (a chord's head set left of its stem): an accidental of b's column, which a line would stop before
    static bool SignBefore(Img ink, Head a, Head b, List<Head> heads, double s)
    {
        double x0 = a.R, y0 = a.Y, x1 = b.L, y1 = b.Y;
        double len = Math.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0));
        var near = heads.Where(h => h.St == b.St && h.R > b.L - 1.4 * s && h.L < b.L && Math.Abs(h.Y - b.Y) < 2.5 * s).ToList();
        for (int yy = (int)(b.Y - 1.5 * s); yy <= b.Y + 1.5 * s; yy++)
            for (int xx = (int)(b.L - 1.2 * s); xx <= b.L - 0.35 * s; xx++)
            {
                if (!ink[xx, yy]) continue;
                if (Math.Abs((xx - x0) * (y1 - y0) - (yy - y0) * (x1 - x0)) / len <= 0.3 * s) continue;
                if (near.Any(h => xx >= h.L - 2 && xx <= h.R + 2 && Math.Abs(yy - h.Y) <= 0.6 * s)) continue;
                // (a ledger line's end, or the chord's other glissando lines: thin up and down; a sign's strokes are
                // upright or thick)
                int up = 0, down = 0;
                while (up < s && ink[xx, yy - up - 1]) up++;
                while (down < s && ink[xx, yy + down + 1]) down++;
                if (up + down + 1 <= 0.3 * s) continue;
                if (DebugX is { } dx && Math.Abs(a.X - dx) < s) Console.WriteLine($"glissando debug: ink at {xx},{yy} before {b.L:F0},{b.Y:F0}; heads near {string.Join(" ", near.Select(h => $"{h.L:F0}-{h.R:F0},{h.Y:F0}"))}");
                return true;
            }
        return false;
    }

    // whether the line from a to back short of b's left passes within half a space of a head not of a's chord or b's
    static bool Crosses(Head a, Head b, double back, List<Head> heads, double s)
    {
        double x0 = a.R, y0 = a.Y, x1 = b.L - back, y1 = b.Y;
        double len = Math.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0));
        if (len < s) return false;
        double ux = (x1 - x0) / len, uy = (y1 - y0) / len;
        static bool Chord(Head h, Head g, double s) => h.HasStem && g.HasStem ? Math.Abs(h.StemX - g.StemX) < 0.3 * s : Math.Abs(h.X - g.X) < 0.35 * s;
        foreach (var h in heads)
        {
            if (h.St != a.St || h == a || h == b || Chord(h, a, s) || Chord(h, b, s)) continue;
            // (nor ending on one: Lily# ends a line short of b at its accidentals, never over a head -- a natural
            // and a flat's strokes lined up to the head after them, gen-trill's piano010)
            if (back > 0 && x1 > h.L - 0.1 * s && x1 < h.R && Math.Abs(h.Y - y1) < 1.1 * s) return true;
            double t = (h.X - x0) * ux + (h.Y - y0) * uy;
            if (t < 0.5 * s || t > len - 0.5 * s) continue;
            if (Math.Abs((h.X - x0) * uy - (h.Y - y0) * ux) < 0.5 * s) return true;
        }
        return false;
    }

    // the line from head a toward head b followed in the ink: how far it runs on (from where it starts, within a little past half a space
    // from the head's edge, to a gap over a fifth of a space), the share of it off the staff lines where the ink runs thin across it (a
    // third of a space and less), and whether it runs on to the end aimed at, back by `back` from b's left (its half-space gap and a little)
    static (double Length, double Thin, bool Reach) Follow(Img d, Head a, Head b, double back, double s)
    {
        double x0 = a.R, y0 = a.Y, x1 = b.L - back, y1 = b.Y;
        // (no steeper than six over five, and a space and a half across: a stem or a sign's upright stands up steeper)
        if (x1 - x0 < 1.5 * s || Math.Abs(y1 - y0) > 1.2 * (x1 - x0)) return (0, 0, false);
        double len = Math.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0));
        double ux = (x1 - x0) / len, uy = (y1 - y0) / len, nx = -uy, ny = ux;
        double near = s / 6, wide = s / 3, gapMax = 0.2 * s;
        double start = double.NaN, end = double.NaN, gap = 0;
        int measured = 0, thin = 0, on = 0, straight = 0;
        for (double t = 0.3 * s; t <= len - 0.3 * s; t += 1)
        {
            double px = x0 + ux * t, py = y0 + uy * t;
            // (the nearest ink across the line)
            double? hit = null;
            for (double k = 0; k <= near && hit == null; k += 1)
            {
                if (d[(int)Math.Round(px + nx * k), (int)Math.Round(py + ny * k)]) hit = k;
                else if (d[(int)Math.Round(px - nx * k), (int)Math.Round(py - ny * k)]) hit = -k;
            }
            if (hit is not double h)
            {
                if (!double.IsNaN(start) && (gap += 1) > gapMax) break;
                continue;
            }
            // (the line starts half a space past the head's edge: ink before that is the head's, a stem's or a dot's, and
            // the line runs on from it or not at all)
            if (double.IsNaN(start))
            {
                if (t > 1.1 * s) break;
                if (t < 0.45 * s) continue;
                start = t;
            }
            gap = 0;
            end = t;
            // (on the line itself, a tenth of a space off at most: a flag's or a sign's strokes bend away from it)
            on++;
            if (Math.Abs(h) <= Math.Max(2, 0.1 * s)) straight++;
            double cx = px + nx * h, cy = py + ny * h;
            // (a staff line's rows: the line crosses it, the run across is the staff line's)
            if (a.St.Lines.Any(q => cy >= q.Top - 2 && cy <= q.Bot + 2)) continue;
            measured++;
            int run = 1;
            for (int dir = -1; dir <= 1; dir += 2)
                for (int k = 1; k <= wide + 1; k++)
                {
                    if (!d[(int)Math.Round(cx + dir * nx * k), (int)Math.Round(cy + dir * ny * k)]) break;
                    run++;
                }
            if (run <= wide) thin++;
        }
        if (double.IsNaN(start) || straight < 0.85 * on) return (0, 0, false);
        return (end - start, measured == 0 ? 0 : thin / (double)measured, end >= len - 1.2 * s);
    }
}
