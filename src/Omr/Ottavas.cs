using System.Drawing;

namespace Omr;

/// <summary>
/// Octave lines (8va, 8vb, 15ma, 15mb: M3, S18) from what the learnt detector sees: the sign by its
/// letters (ott_8va ... ott_15mb, at the centre of the letters; "(8va)" where a line goes on from a
/// system's start is the same) and the hook where its dashes end (ott_end). The notes under a line
/// over a staff sound an octave (two) above what is written, under a staff below. Each line is handed
/// on as a <see cref="Tempo.Mark"/> with Ottava set (1, 2 up; -1, -2 down), as the OCR's 8va is
/// (Tempo.Read), and <see cref="Apply"/> moves the notes.
///
/// Whose a sign is: the staff under an 8va or 15ma, the staff over an 8vb or 15mb (the letters say
/// which; an 8va over a staff and an 8vb under the staff above it stand in the same gap, where the
/// position alone cannot tell them apart: piano006, 1.2 spaces against 1.3). With no staff on the
/// letters' side within reach, the one on the other side, if its hook says so (it points at the
/// staff: down from a line over it). Where a line ends: at the nearest hook to the right at the
/// sign's height, before the next sign there; with none, where its dashes end in the ink, or,
/// reaching the system's end, in the next system, where "(8va)" at the first note takes it up (the
/// first piece is then not closed, the second not started again). A line left open is an error to
/// Lily# (LYS4018), so one with no hook and no piece going on is closed at its system's end all the same.
/// </summary>
static class Ottavas
{
    public static bool Verbose;
    // (a bare 8's digit is taken from this up, under the detector's threshold: BareEights)
    public static double WeakBareEight = 0.3;

    public static bool IsSign(Detection t) => t.Class.StartsWith("ott_") && t.Class != "ott_end";
    // half the width of a sign's letters, in spaces: 8va and 8vb are 3.7 spaces wide, 15ma and 15mb 5.9 (Lily#'s bold italic at 2.2)
    public static double HalfWidth(Detection t) => t.Class.StartsWith("ott_15") ? 2.95 : 1.85;
    static bool SaysUp(Detection t) => t.Class.EndsWith("a");
    static int Octaves(Detection t) => t.Class.StartsWith("ott_15") ? 2 : 1;

    sealed class Line
    {
        public Tempo.Mark Mark; public Detection Sign; public SystemInfo Sys; public Staff St; public bool Up, Closed, Sure; public double Left;
        public bool Hooked;   // (unsure letters whose side its own hook told)
        public (SystemInfo Sys, Staff St) Over, Under;
    }

    /// <summary>
    /// How far the dashes run right of x at the height y, in the ink: along the band of 0.4 of a space
    /// about y, the end of the last run of ink that is a dash's (0.6 of a space long, up to 1.2 as
    /// printed; thin, and on the row the dashes before it set: the dashes are collinear) before a gap of two
    /// and a half spaces (a dash's gap is 1.4), a longer run (a beam, a ledger line: not the dashes')
    /// or ink that is no dash (a head's top in the band, off the dashes' row: ledger002's bes' after a
    /// hook the detector missed ran the line on to the head's edge). A stem through the band, ink on
    /// both sides of the row, is passed over (the other staff's, hanging into the gap of a grand staff:
    /// piano005). The hook, a narrow stroke joined to the last dash and running one way from the row,
    /// is the end: its right edge. With no hook seen, the line ends there: a line too short for dashes
    /// ("(8vb)" over one note, going on from a system's start) ends at its sign, one reaching the
    /// system's end goes on
    /// </summary>
    public static double DashesEnd(Img ink, double s, double x, double y) => DashesEnd(ink, s, x, y, out _, out _);

    /// <param name="hooked">whether the end found is the hook's (else a dash's end, or x itself)</param>
    /// <param name="dashed">whether any dash was found (else the end is only what the letters ran into)</param>
    /// <param name="stop">where the scan stops: another sign's letters at the height (the next line's)</param>
    public static double DashesEnd(Img ink, double s, double x, double y, out bool hooked, out bool dashed, double stop = double.MaxValue)
    {
        hooked = false; dashed = false;
        int y0 = (int)Math.Max(0, y - 0.4 * s), y1 = (int)Math.Min(ink.H - 1, y + 0.4 * s);
        double row = double.NaN;    // the dashes' row, from the first dash's ink
        double Thin() => double.IsNaN(row) ? 0.25 * s : 0.3 * s;   // a dash's thickness at most (see Column)
        // the ink in the column within the band: 0 none, 1 a dash's (thin, on the row; or a stem
        // through the band), 2 something else's; with the rows it covers
        (int Kind, int Top, int Bottom) Column(int xx)
        {
            int top = -1, bottom = -1;
            // (once the row is set, the run of ink nearest it: other ink in the band apart from it is not the
            // dash's column -- a slur under the dashes reaching into the band, Holmes's No.2 scan, bar 53)
            double near = double.MaxValue;
            for (int yy = y0; yy <= y1; yy++)
            {
                if (!ink[xx, yy]) continue;
                int e = yy; while (e < y1 && ink[xx, e + 1]) e++;
                if (double.IsNaN(row)) { if (top < 0) top = yy; bottom = e; }
                else if (Math.Max(0, Math.Max(yy - row, row - e)) is var d && d < near) { near = d; top = yy; bottom = e; }
                yy = e;
            }
            if (top < 0) return (0, top, bottom);
            double thick = bottom - top + 1, centre = (top + bottom) / 2.0;
            // (once the first dash set the row, a scan's dashes may run thicker: Holmes's No.2's, six pixels at 23 to
            // the space, 0.26. The first is thin: a letter's stroke four pixels thick at 15 to the space, the "d" of a
            // "ritard.", taken for one, an unsure 15ma in its letters, schumann25-1's MuseScore page, bar 25)
            if (thick <= Thin() && (double.IsNaN(row) || Math.Abs(centre - row) <= 0.15 * s)) return (1, top, bottom);
            if (!double.IsNaN(row) && Reach(xx, (int)Math.Round(row), -1) >= 0.4 * s && Reach(xx, (int)Math.Round(row), 1) >= 0.4 * s) return (1, top, bottom);
            return (2, top, bottom);
        }
        // how far the ink runs on from the row at the column, up (dir -1) or down (1): a space and a half at most
        double Reach(int xx, int r, int dir) { int n = 0; for (int yy = r + dir; n < 1.5 * s && ink[xx, yy]; yy += dir) n++; return n; }
        // whether a dash follows within a dash's gap and more, past narrow strokes only: no hook then,
        // whatever touches the dash and runs one way from the row (a sharp's upright through the dashes
        // of an 8vb, seed 11's piano001 bar 8, Lily# drawing the line on beyond it)
        bool DashAhead(int from)
        {
            int start = -1;
            for (int xx = from; xx < ink.W && xx - from < 2.5 * s; xx++)
            {
                int kind = Column(xx).Kind;
                if (kind == 1) { if (start < 0) start = xx; if (xx - start > 1.2 * s) return false; continue; }
                if (start >= 0 && xx - start >= 0.35 * s) return true;
                start = -1;
                if (kind == 2) { int w = 0; while (xx + w < ink.W && Column(xx + w).Kind == 2) w++; if (w > 0.3 * s) return false; xx += w - 1; }
            }
            return false;
        }
        double last = x, runStart = -1, rowSum = 0; int rowN = 0;
        // (past ink across the dashes' row, the gap is counted from its end: the other staff's beam through
        // the dashes, Lily# drawing them on beyond it, piano005's "(8va)" under the right hand's sixteenths.
        // The end moves only with a dash or the hook found after it)
        double across = x;
        bool dashSeen = false;
        for (int xx = (int)x; xx < ink.W && xx < stop && xx - Math.Max(last, across) < 2.5 * s; xx++)
        {
            var (kind, top, bottom) = Column(xx);
            if (kind == 0)
            {
                // (a run shorter than a third of a space before any dash is no dash: a letter's thin
                // edge, which must not set the row)
                if (runStart >= 0)
                {
                    // (the row follows the dashes, each one long enough setting it again: a scan's line slants, Holmes's
                    // No.2 bar 45's 8va falling 10 pixels over 30 spaces, off the first dash's row by more than a dash's
                    // play two thirds of the way)
                    if (xx - runStart >= 0.35 * s || !double.IsNaN(row)) { dashSeen = dashed = true; if (rowN > 0 && (double.IsNaN(row) || xx - runStart >= 0.35 * s)) row = rowSum / rowN; }
                    last = xx - 1; runStart = -1;
                }
                continue;
            }
            if (kind == 1)
            {
                if (runStart < 0) { runStart = xx; rowSum = 0; rowN = 0; }
                if (xx - runStart > 1.2 * s) break;
                if (bottom - top + 1 <= Thin()) { rowSum += (top + bottom) / 2.0; rowN++; }
                continue;
            }
            // ink that is no dash: the hook, joined to the dash before it, narrow, its stroke running one
            // way only from the dashes' row; before any dash, the letters' tail where x fell short of
            // their end, or, with no dashes, whatever the line ran to before (passed over, as a run was);
            // after the dashes, something else's: passed over without moving the end, which only a dash
            // does (the line ends where the dashes end, whether the other ink is a head under a hook the
            // detector missed, ledger002, or the next system's ledger-line note reaching up into the band
            // of a line that runs on, ledger008)
            int w = 0; while (xx + w < ink.W && Column(xx + w).Kind == 2) w++;
            bool seenBefore = dashSeen;
            // (the run leading into it is a dash too, when long enough: "- " then the hook, with no gap
            // between them to end the run, sets the row here: basic001's "(15ma)" over one note)
            if (runStart >= 0 && xx - runStart >= 0.35 * s && double.IsNaN(row) && rowN > 0) { row = rowSum / rowN; dashSeen = dashed = true; }
            // (half a space wide at most: a staccato dot under the last dash touching the hook's foot, Holmes's No.2 scan,
            // bar 54, the two nine pixels wide, 0.38)
            if (runStart >= 0 && !double.IsNaN(row) && w <= 0.5 * s)
            {
                // (the row a pixel off the hook's end will do, after dashes seen: the row, following the dashes, at 3318.5
                // and rounded to 3318, the hook's stroke from 3319 -- gen-ott-scan's piano002, a 15ma's hook missed, hung under
                // the staff above. Not with the row just set by this run: a letter's stroke, the "d" of a "ritard." after an
                // unsure 15ma, its upright taken for the hook, schumann25-1's MuseScore page)
                int r = (int)Math.Round(row);
                if (!ink[xx, r] && seenBefore) r = Enumerable.Range(1, Math.Max(1, (int)(0.1 * s))).SelectMany(k => new[] { r + k, r - k }).Where(rr => ink[xx, rr]).DefaultIfEmpty(r).First();
                double up = Reach(xx, r, -1), down = Reach(xx, r, 1);
                if (ink[xx, r] && Math.Max(up, down) >= 0.4 * s && Math.Min(up, down) <= 0.2 * s && !DashAhead(xx + w)) { hooked = true; return xx + w - 1; }
            }
            // (no hook: the run leading into it, a dash's length, is a dash all the same -- a staccato dot just
            // under the dashes touching the band at a dash's end, Holmes's No.2 scan, bar 31, lost the dash before it)
            // (after a dash seen only: before one, a letter's stroke running into the rest of its word is no dash -- the
            // "rd" of a "ritard." an unsure 15ma was seen in, schumann25-1's MuseScore page, bar 25)
            if (seenBefore && runStart >= 0 && !double.IsNaN(row) && xx - runStart >= 0.35 * s) { last = xx - 1; }
            // (or the hook's upright alone, a dash's gap after the last dash: Lily# leaves the hook's foot out
            // where the dashes' pattern has a gap, seed 11's ledger007 bar 9. Short, as a hook is, not a stem
            // ending at the row. Its end within the dash's thickness of the row, a pixel off it: seed 11's
            // piano007, an 8va's hook from 3478 under dashes at 3475-3479, the line taken as going on)
            // Any column of the upright will do: a scan's grain breaks its edge column into pieces (gen-ott-scan's
            // piano002 bar 14, an upright three pixels wide, its first column cut four rows down: the 15ma, its letters
            // read as a 15mb, hung under the staff above for want of the hook)
            if (runStart < 0 && dashSeen && !double.IsNaN(row) && w <= 0.3 * s)
            {
                bool upright = false;
                for (int k = 0; k < w && !upright; k++)
                {
                    int cx = xx + k, hr = Enumerable.Range((int)Math.Round(row - 0.1 * s), (int)Math.Round(0.2 * s) + 1).Where(rr => ink[cx, rr]).DefaultIfEmpty(-1).First();
                    if (hr < 0) continue;
                    double up = Reach(cx, hr, -1), down = Reach(cx, hr, 1);
                    upright = Math.Max(up, down) >= 0.4 * s && Math.Max(up, down) <= 1.2 * s && Math.Min(up, down) <= 0.2 * s;
                }
                if (upright && !DashAhead(xx + w)) { hooked = true; return xx + w - 1; }
            }
            // (before any dash, only the letters' own tail moves the end: narrow ink reaching little
            // past the band, the closing paren of a "(15ma)" or a letter past the sign's usual width. A
            // stem through the band is passed over, as a dash's row would pass it; anything else -- the
            // next note's stem ending at its beam at the sign's height, the beam itself -- is not the
            // line's, and the scan runs on from the tail only as far as a dash's gap: a "(15ma)" over
            // one note with the tuplet's stem and beam right after it, basic001, ran on along the beam)
            double over = 0, under = 0;
            for (int k = 0; k < w; k++) { over = Math.Max(over, Reach(xx + k, y0, -1)); under = Math.Max(under, Reach(xx + k, y1, 1)); }
            bool through = w <= 0.3 * s && over >= 0.4 * s && under >= 0.4 * s;
            bool tail = w <= 1.0 * s && over < 0.8 * s && under < 0.8 * s;
            if (dashSeen && !double.IsNaN(row) && w <= 6 * s)
            {
                int r = (int)Math.Round(row), on = 0;
                for (int k = 0; k < w; k++) if (ink[xx + k, r]) on++;
                if (on >= 0.8 * w) across = xx + w - 1;
            }
            xx += w - 1; runStart = -1;
            if (!dashSeen && (tail || through)) last = xx;
        }
        return last;
    }

    /// <summary>
    /// An old print's line: a wavy line in place of the dashes, "8va~~~~" ended by "loco." (kinkel7-1's scan,
    /// bar 21: the wave 0.3 to 0.55 of a space thick a column, its middle turning up and down by 0.3 of a space
    /// every third of a space). From the letters' right edge (a space and a half on at most), a thin band of ink
    /// (0.1 to 0.65 of a space a column) within a space of the sign's height, followed column by column (its
    /// middle moving a fifth of a space at most; gaps of a quarter of a space, and ink across it -- a stem
    /// touching it -- passed over) for MINLENGTH spaces and more, unbroken, that waves: its middle turns (by 0.08 of a space)
    /// once every space and a half at least; under three spaces, once every three quarters of a space, as Lily#'s
    /// wave does (a line over two notes, its wave 2.3 spaces long, was taken by its last turn for a hook up, an
    /// 8va hung as an 8vb under the staff above: gen-ottw's piano001). A slur, a beam or the dashes run on
    /// without turning. The end of
    /// its last column, or NaN for none; top and bottom: the rows the band covers
    /// </summary>
    public static double WaveEnd(Img ink, double s, double x, double y, double minLength, out double top, out double bottom)
    {
        top = bottom = double.NaN;
        int lo = (int)Math.Max(0, y - s), hi = (int)Math.Min(ink.H - 1, y + s);
        // the run of ink in the column nearest c, within 0.6 of a space of it: its rows
        (int Top, int Bottom) Run(int xx, double c)
        {
            int a = (int)Math.Max(lo, c - 0.6 * s), b = (int)Math.Min(hi, c + 0.6 * s);
            (int, int) best = (-1, -1); double near = double.MaxValue;
            for (int yy = a; yy <= b; yy++)
            {
                if (!ink[xx, yy]) continue;
                int t = yy; while (t > 0 && ink[xx, t - 1]) t--;
                int e = yy; while (e < ink.H - 1 && ink[xx, e + 1]) e++;
                if (Math.Abs((t + e) / 2.0 - c) < near) { near = Math.Abs((t + e) / 2.0 - c); best = (t, e); }
                yy = e;
            }
            return best;
        }
        for (int x0 = (int)Math.Max(0, x - 0.3 * s); x0 < Math.Min(ink.W, x + 1.5 * s); x0++)
        {
            var (t0, b0) = Run(x0, y);
            if (t0 < 0 || b0 - t0 + 1 < 0.1 * s || b0 - t0 + 1 > 0.65 * s) continue;
            var mids = new List<double>();
            double c = (t0 + b0) / 2.0, up = t0, down = b0;
            // (ink over the band's middle, thicker than the wave, is passed over without a gap and without moving
            // the end: a beam the wave touches along nine spaces, bar 21's)
            int last = x0, seen = x0;
            for (int xx = x0; xx < ink.W && xx - seen <= 0.25 * s; xx++)
            {
                var (t, b) = Run(xx, c);
                if (t >= 0 && t <= c + 0.3 * s && b >= c - 0.3 * s) seen = xx;
                if (t < 0 || b - t + 1 < 0.1 * s || b - t + 1 > 0.65 * s || Math.Abs((t + b) / 2.0 - c) > 0.2 * s) continue;
                c = (t + b) / 2.0; mids.Add(c); last = xx;
                up = Math.Min(up, t); down = Math.Max(down, b);
            }
            double length = last - x0;
            if (length < minLength * s) continue;
            // (the turns, each a move back of 0.08 of a space from the furthest the middle went the other way)
            int turns = 0, dir = 0; double ext = mids[0], least = mids[0], most = mids[0], h = 0.08 * s;
            foreach (var m in mids)
            {
                if (dir == 0)
                {
                    least = Math.Min(least, m); most = Math.Max(most, m);
                    if (m - least >= h) { dir = 1; ext = m; } else if (most - m >= h) { dir = -1; ext = m; }
                }
                else if (dir * (m - ext) > 0) ext = m;
                else if (dir * (ext - m) >= h) { turns++; dir = -dir; ext = m; }
            }
            if (turns < (length < 3 * s ? length / (0.75 * s) : length / (1.5 * s))) continue;
            // (and is one stroke: its band in three columns of four and more -- Lily#'s waves 0.99, kinkel7-1's
            // scan's 0.97. A word's letters wave too, but apart: the "moto" of gen-lied-all lied009's "con moto",
            // under an 8va at 0.30 in its "on", 0.38)
            if (mids.Count < 0.75 * (last - x0 + 1)) continue;
            top = up; bottom = down;
            return last;
        }
        return double.NaN;
    }

    /// <summary>
    /// Which way the hook at (x, y) points: the rows with ink in the column a fifth of a space either side
    /// of x, from a quarter of a space off y to a space off, below and above (the hook is 0.8 of a space).
    /// 1 down (a line over its staff), -1 up (under), 0 unclear (a stem through it, or none)
    /// </summary>
    public static int HookDir(Img ink, double s, double x, double y)
    {
        int Rows(int dir)
        {
            int n = 0, x0 = (int)Math.Max(0, x - 0.2 * s), x1 = (int)Math.Min(ink.W - 1, x + 0.2 * s);
            for (double d = 0.25 * s; d <= 1.0 * s; d++)
            {
                int yy = (int)Math.Round(y + dir * d);
                if (yy < 0 || yy >= ink.H) continue;
                for (int xx = x0; xx <= x1; xx++) if (ink[xx, yy]) { n++; break; }
            }
            return n;
        }
        int down = Rows(1), up = Rows(-1);
        if (Math.Max(down, up) < 0.4 * s || Math.Min(down, up) > 0.5 * Math.Max(down, up)) return 0;
        return down > up ? 1 : -1;
    }

    /// <summary>
    /// A sign the detector was unsure of (under its threshold, over Detector.WeakOttava) is one where its
    /// dashes run on from it in the ink for two spaces or more, or a sure hook (ends) ends them at its
    /// height: piano-collide's 8va at 0.49. (Symbols.Find drops the heads it would read in such a
    /// sign's letters too: basic001's a of a 15ma at 0.50 read as a head two ledger lines under the staff above)
    /// </summary>
    public static bool DashesOn(Img ink, double s, Detection t, IEnumerable<Detection> ends)
    {
        double right = t.X + HalfWidth(t) * s;
        // (a dash must be seen: before one, the letters' tail and whatever else they run into move the end
        // too. The hook found in the ink is enough by itself: a "(15ma)" going on over one note has one dash
        // and the hook, within two spaces of letters wider than the sign's usual, basic001)
        double end = DashesEnd(ink, s, right, t.Y, out bool hooked, out bool dashed);
        return hooked || dashed && end > right + 2 * s
            || ends.Any(e => Math.Abs(e.Y - t.Y) < 1.5 * s && e.X > right && e.X < t.X + 60 * s);
    }

    /// <summary>An unsure sign's line runs on from it: its dashes (<see cref="DashesOn"/>) or an old print's wave (<see cref="WaveEnd"/>)</summary>
    public static bool LineOn(Img ink, double s, Detection t, IEnumerable<Detection> ends)
        => DashesOn(ink, s, t, ends) || !double.IsNaN(WaveEnd(ink, s, t, out _, out _));

    /// <summary>
    /// The wave on from a sign's letters (<see cref="WaveEnd(Img, double, double, double, double, out double, out double)"/>):
    /// two spaces long at least after a sign the detector sees at its threshold, three after an unsure one (the
    /// "ce." after the "va" of kinkel10-6's scan's "vivace", an 8va at 0.31, waves for 2.7 spaces)
    /// </summary>
    public static double WaveEnd(Img ink, double s, Detection t, out double top, out double bottom)
        => WaveEnd(ink, s, t.X + HalfWidth(t) * s, t.Y, t.Score >= Detector.Threshold ? 2 : 3, out top, out bottom);

    /// <summary>
    /// The detector's unsure signs, those at one place (within a space and a half across, 0.6 of a space
    /// up and down) taken as one: the letters it split between the two of one octave count (a "15mb" seen
    /// as a 15mb at 0.25 and a 15ma at 0.22, piano001's bar 6, under WeakOttava each) are that count's
    /// sign, the surer of the two, at the sum of the two's best (under the threshold still: the hook, or
    /// the ink, says the side, as for a sign unsure of its letters)
    /// </summary>
    public static List<Detection> Pooled(List<Detection> weak, double s)
    {
        var left = weak.OrderByDescending(t => t.Score).ToList();
        var pooled = new List<Detection>();
        while (left.Count > 0)
        {
            var t = left[0];
            var near = left.Where(o => Math.Abs(o.X - t.X) < 1.5 * s && Math.Abs(o.Y - t.Y) < 0.6 * s).ToList();
            left.RemoveAll(near.Contains);
            var best = near.GroupBy(Octaves).Select(g => (Sign: g.First(), Sum: g.GroupBy(o => o.Class).Sum(c => c.Max(o => o.Score))))
                .OrderByDescending(p => p.Sum).First();
            pooled.Add(best.Sum > best.Sign.Score
                ? new Detection { Class = best.Sign.Class, X = best.Sign.X, Y = best.Sign.Y, Score = Math.Min(best.Sum, Detector.Threshold) }
                : best.Sign);
        }
        return pooled;
    }

    /// <summary>
    /// The box of the piece of ink just over a loop (its rim's top, in the ink's components), within two
    /// spaces of it; null where there is none or the piece runs past that window (no digit's).
    /// </summary>
    static Comp PieceAbove(PageSymbols ps, Comp hole, double s)
    {
        if (ps.Labels == null) return null;
        int w = ps.LabelW, h = ps.Labels.Length / w, x = (int)Math.Round(hole.Cx), id = 0;
        for (int y = hole.MinY - 1; y >= Math.Max(0, hole.MinY - 3) && id == 0; y--) id = ps.Labels[y * w + x];
        if (id == 0) return null;
        int r = (int)(2 * s);
        int x0 = Math.Max(0, x - r), x1 = Math.Min(w - 1, x + r), y0 = Math.Max(0, hole.MinY - r), y1 = Math.Min(h - 1, hole.MaxY + r);
        var box = new Comp();
        for (int yy = y0; yy <= y1; yy++)
            for (int xx = x0; xx <= x1; xx++)
                if (ps.Labels[yy * w + xx] == id)
                {
                    if (xx == x0 || xx == x1 || yy == y0 || yy == y1) return null;
                    box.MinX = Math.Min(box.MinX, xx); box.MaxX = Math.Max(box.MaxX, xx);
                    box.MinY = Math.Min(box.MinY, yy); box.MaxY = Math.Max(box.MaxY, yy);
                    box.N++; box.Sx += xx; box.Sy += yy;
                }
        return box;
    }

    /// <summary>
    /// An octave line printed with a bare "8" for its letters (kinkel7-1's MuseScore page, bar 21): the
    /// detector has no class for it and sees a digit. A digit 8 off every staff with four spaces of dashes
    /// running on from it and a hook ending them is the line, the hook telling its side. The dashes stand
    /// at the digit's top, off its centre, so the row is searched. As a sign of the detector's: its middle
    /// placed so that its letters' left edge is the digit's, its height the dashes', sure by its hook.
    /// The digit may be seen under the threshold, from WeakBareEight up, its dashes and hook found all
    /// the same: MuseScore's italic 8 over Holmes's No.2 (bar 29), seen as a TAB 8 at 0.45. Or not at
    /// all, its two loops in the ink saying it (bars 30 and 53); and with no hook, the dashes running to
    /// the system's end, the line goes on, its side the digit's to its staff (or stopping a few spaces short of it, as an old print does).
    /// </summary>
    static IEnumerable<Detection> BareEights(PageResult res, List<Detection> det)
    {
        double s = res.S;
        var ink = res.Dark;
        var digits = det.Where(t => t.Class is "tab_8" or "digit_8").Concat(res.Sym.WeakTabs.Where(t => t.Class == "tab_8" && t.Score > WeakBareEight)).ToList();
        // (and an 8 the detector saw nothing of, its two loops one over the other in the ink: MuseScore's italic 8
        // at a system's start, Holmes's No.2 bars 30 and 53. Each loop a fifth to seven tenths of a space, the upper
        // one 0.35 to a space over the lower, the italic's slant leaning it at most 0.4 to the right)
        var loops = res.Sym.InkHoles.Where(h => h.W >= 0.15 * s && h.W <= 0.7 * s && h.H >= 0.15 * s && h.H <= 0.7 * s).ToList();
        bool Ink(int x0, int x1, int y0, int y1)
        {
            for (int yy = Math.Max(0, y0); yy <= Math.Min(ink.H - 1, y1); yy++)
                for (int xx = Math.Max(0, x0); xx <= Math.Min(ink.W - 1, x1); xx++)
                    if (ink[xx, yy]) return true;
            return false;
        }
        // the piece of ink about a loop (lo) taken as an 8, the upper loop hi with it or none (open): a digit's size,
        // shutting in the loops it should and no other (a pedal's star, loops in a ring, is none, nor two notes' loops)
        void Eight(Comp lo, Comp hi)
        {
            var piece = PieceAbove(res.Sym, hi ?? lo, s);
            bool In(Comp h) => piece != null && h.MinX > piece.MinX && h.MaxX < piece.MaxX && h.MinY > piece.MinY && h.MaxY < piece.MaxY;
            if (piece == null || !In(lo) || piece.H < 1.0 * s || piece.H > 1.9 * s || piece.W < 0.6 * s || piece.W > 1.4 * s
                || res.Sym.InkHoles.Count(h => h.N >= 4 && In(h)) != (hi == null ? 1 : 2)) return;
            int mid = (piece.MinY + piece.MaxY) / 2;
            if (hi == null)
            {
                // (an open upper loop: the one loop in the lower half, half the piece's width and more, and the upper
                // half as wide as a bowl -- no b's or d's stem over its loop, no 6's hook)
                // (its middle a tenth of the piece's height under the piece's: a pedal's star shuts in its loop at its middle)
                if (lo.Cy < mid + 0.1 * piece.H || lo.W < 0.35 * piece.W) return;
                int l = int.MaxValue, r = -1;
                for (int yy = piece.MinY; yy < piece.MinY + (mid - piece.MinY) * 2 / 3; yy++)
                    for (int xx = piece.MinX; xx <= piece.MaxX; xx++)
                        if (ink[xx, yy]) { l = Math.Min(l, xx); r = Math.Max(r, xx); }
                if (r - l + 1 < 0.6 * (piece.MaxX - piece.MinX + 1)) return;
                // (and the bowl's two sides there, ink in the left and the right third of the piece in seven rows of ten
                // over the middle: an a's hook has its ball on one side only, its stem on the other -- a song's "a" and
                // the hyphens after it in gen-band's band007)
                int both = 0, rows = 0, third = (piece.MaxX - piece.MinX + 1) / 3;
                for (int yy = piece.MinY + (int)(0.15 * piece.H); yy < mid - (int)(0.05 * piece.H); yy++, rows++)
                {
                    bool left = false, right = false;
                    for (int k = 0; k < third; k++) { left |= ink[piece.MinX + k, yy]; right |= ink[piece.MaxX - k, yy]; }
                    if (left && right) both++;
                }
                if (rows == 0 || both < 0.7 * rows) return;
                // (and no stem: in neither outer third three columns whose ink runs unbroken for three quarters of the piece's
                // height -- an a's stem on its right, nine columns at 0.9, its ball wide enough to give it two sides over its
                // bowl, gen-band's band007; an 8's waist breaks all but its edge, one column at 0.84 on Holmes's No.2 scan)
                foreach (int side in new[] { 1, -1 })
                {
                    int stem = 0;
                    for (int k = 0; k < third; k++)
                    {
                        int cx = side > 0 ? piece.MinX + k : piece.MaxX - k, run = 0, longest = 0;
                        for (int yy = piece.MinY; yy <= piece.MaxY; yy++) { run = ink[cx, yy] ? run + 1 : 0; longest = Math.Max(longest, run); }
                        if (longest >= 0.75 * piece.H) stem++;
                    }
                    if (stem >= 3) return;
                }
            }
            // (alone, no word's letter: nothing within eight tenths of a space before it, nor after it below
            // the dashes' row, its top -- the italic g of an "allargando" with its spanner's dashes, gen-span-scan's basic005)
            // (an open 8's dashes may stand at its middle, its lower loop alone in the lower part: there, only that part's
            // height is looked at -- Holmes's No.2 scan, bar 29, the first dash at the 8's middle half a space off it)
            int under = hi == null ? piece.MaxY - (int)(0.3 * piece.H) : mid;
            if (Ink(piece.MinX - (int)(0.8 * s), piece.MinX - 2, piece.MinY, piece.MaxY) || Ink(piece.MaxX + 2, piece.MaxX + (int)(0.8 * s), under, piece.MaxY)) return;
            double x = hi == null ? (piece.MinX + piece.MaxX) / 2.0 : (lo.Cx + hi.Cx) / 2, y = hi == null ? (piece.MinY + piece.MaxY) / 2.0 : (lo.Cy + hi.Cy) / 2;
            if (Verbose) Console.WriteLine($"  digit 8 in the ink at {x:F0},{y:F0}{(hi == null ? ", its upper loop open" : "")}: piece {piece.MinX}-{piece.MaxX} x {piece.MinY}-{piece.MaxY}, loop {lo.MinX}-{lo.MaxX} x {lo.MinY}-{lo.MaxY}");
            if (!digits.Any(t => Math.Abs(t.X - x) < 0.8 * s && Math.Abs(t.Y - y) < 0.8 * s))
                digits.Add(new Detection { Class = "digit_8", X = x, Y = y, Score = 0 });
        }
        foreach (var lo in loops)
        {
            foreach (var hi in loops)
                if (hi != lo && lo.Cy - hi.Cy is var gap && gap >= 0.35 * s && gap <= 1.0 * s && hi.Cx - lo.Cx > -0.15 * s && hi.Cx - lo.Cx < 0.4 * s)
                    Eight(lo, hi);
            // (or its upper loop open, a scan's grain breaking the bowl's top: Holmes's No.2 scan, bar 29)
            Eight(lo, null);
        }
        foreach (var t in digits)
        {
            // (a TAB's too: its fret numbers' 8s, the string's line between them broken into what looks like
            // dashes, gen-band's band001)
            if (res.Systems.Any(q => q.All.Any(st => t.Y > st.Top - 0.5 * s && t.Y < st.Bottom + 0.5 * s && t.X > q.Bars[0] - 2 * s && t.X < q.Bars[^1] + 2 * s))) continue;
            // (the system it stands in, across its width: a line with no hook that runs to its end goes on to the next)
            var sys = res.Systems.Where(q => t.X > q.Bars[0] - 2 * s && t.X < q.Bars[^1]).OrderBy(q => Math.Min(Math.Abs(q.Staves[0].Top - t.Y), Math.Abs(q.Staves[^1].Bottom - t.Y))).FirstOrDefault();
            foreach (double dy in new[] { 0.0, -0.4, 0.4, -0.8, 0.8 })
            {
                double y = t.Y + dy * s, from = t.X + 0.6 * s;
                double end = DashesEnd(ink, s, from, y, out bool hooked, out bool dashed);
                if (Verbose) Console.WriteLine($"  digit 8 at {t.X:F0},{t.Y:F0} ({t.Score:F2}): row {y:F0}, to {end:F0}, hooked {hooked}, dashed {dashed}");
                if (!dashed || end - from < 4 * s) continue;
                if (!hooked)
                {
                    // (the dashes on to the system's last barline: the line goes on, its side the digit's to the
                    // staff nearest it, as MuseScore sets an 8va over its staff and an 8vb under. An old print stops
                    // them short of it, three spaces and a half and more: Holmes's No.2 scan, bars 32 and 48, 3.6 and
                    // 3.7. Further short, no line: a word's letters and its hyphens after an unsure 8, kinkel19-1's scan)
                    if (sys == null || sys.Bars[^1] - end > 4.5 * s) continue;
                    var near = sys.Staves.OrderBy(st => Math.Min(Math.Abs(st.Top - t.Y), Math.Abs(st.Bottom - t.Y))).First();
                    bool over = t.Y < near.Top;
                    // (between two staves, the one whose notes under the line stand out of it towards the line: the
                    // notes an 8va is written for stand high over their staff, an 8vb's low under it. The nearer staff
                    // may be the other: a voice's, its words between it and the line, Holmes's No.2 scan, bar 45)
                    var below = sys.Staves.Where(st => st.Top > t.Y).OrderBy(st => st.Top).FirstOrDefault();
                    var above = sys.Staves.Where(st => st.Bottom < t.Y).OrderByDescending(st => st.Bottom).FirstOrDefault();
                    if (below != null && above != null)
                    {
                        int high = res.Sym.Heads.Count(h => h.On(below) && h.X > t.X && h.X < end && h.Y < below.Top - 1.5 * s);
                        int low = res.Sym.Heads.Count(h => h.On(above) && h.X > t.X && h.X < end && h.Y > above.Bottom + 1.5 * s);
                        if (high != low) over = high > low;
                        if (Verbose) Console.WriteLine($"  bare 8 at {t.X:F0},{t.Y:F0}: {high} notes high over the staff below, {low} low under the staff above");
                    }
                    if (Verbose) Console.WriteLine($"  bare 8 at {t.X:F0},{t.Y:F0} ({t.Score:F2}): dashes at {y:F0} to {end:F0} (the system's end {sys.Bars[^1]:F0}), {(over ? "over" : "under")} its staff");
                    yield return new Detection { Class = over ? "ott_8va" : "ott_8vb", X = t.X - 0.6 * s + 1.85 * s, Y = y, Score = Math.Max(t.Score, Symbols.SureOttava) };
                    break;
                }
                // (the dashes' own row, in the last dash; the hook from it, shorter than HookDir asks: 0.6 of a space here)
                int cx = (int)(end - 0.4 * s), top = -1, bottom = -1;
                for (int yy = (int)(y - 0.4 * s); yy <= (int)(y + 0.4 * s); yy++) if (ink[cx, yy]) { if (top < 0) top = yy; bottom = yy; }
                if (top < 0) continue;
                int Rows(int dir, int from)
                {
                    int n = 0;
                    for (int d = 1; d <= s; d++)
                    {
                        int yy = from + dir * d;
                        if (yy >= 0 && yy < ink.H && (ink[(int)end, yy] || ink[(int)end - 1, yy])) n++;
                    }
                    return n;
                }
                int down = Rows(1, bottom), up = Rows(-1, top);
                if (Math.Max(down, up) < 0.4 * s || Math.Min(down, up) > 0.1 * s) continue;
                if (Verbose) Console.WriteLine($"  bare 8 at {t.X:F0},{t.Y:F0} ({t.Score:F2}): dashes at {top} to {end:F0}, hook {(down > up ? "down" : "up")}");
                yield return new Detection { Class = down > up ? "ott_8va" : "ott_8vb", X = t.X - 0.6 * s + 1.85 * s, Y = (top + bottom) / 2.0, Score = Math.Max(t.Score, Symbols.SureOttava) };
                break;
            }
        }
    }

    public static List<Tempo.Mark> Find(PageResult res)
    {
        var marks = new List<Tempo.Mark>();
        if (res.Sym.Detections == null) return marks;
        double s = res.S;
        var ink = res.Dark;
        var det = res.Sym.Detections.Where(t => t.Score > Detector.Threshold).ToList();
        var ends = det.Where(t => t.Class == "ott_end").ToList();
        double DashesEnd(double x, double y) => Ottavas.DashesEnd(ink, s, x, y);
        int HookDir(double x, double y) => Ottavas.HookDir(ink, s, x, y);
        // an old print's wavy line on from a sign (WaveEnd): its end (NaN for none) and the rows it covers
        var waves = new Dictionary<Detection, (double End, double Top, double Bottom)>();
        (double End, double Top, double Bottom) Wave(Detection t)
        {
            if (!waves.TryGetValue(t, out var w))
                waves[t] = w = (WaveEnd(ink, s, t, out double top, out double bottom), top, bottom);
            return w;
        }
        bool Wavy(Detection t) => !double.IsNaN(Wave(t).End);
        // the nearest ink to the line on one side (dir 1 below it, -1 above), in spaces: over the columns
        // past the letters and short of the hook, from past the dashes (a wave's band) to fourteen spaces off; 99 for none
        double InkDistance(Detection t, double endX, int dir)
        {
            // (an old print's letters run wider, an accent over the "va" reaching 2.6 spaces past the sign's middle: kinkel7-1's)
            int x0 = (int)(t.X + HalfWidth(t) * s + (Wavy(t) ? 1.5 : 0.3) * s), x1 = (int)Math.Min(ink.W - 1, endX - 1.0 * s);
            double from = Wavy(t) ? (dir > 0 ? Wave(t).Bottom : Wave(t).Top) : t.Y;
            double best = 99;
            for (int xx = x0; xx <= x1; xx++)
                for (double d = 0.35 * s; d < best * s && d < 14 * s; d++)
                {
                    int yy = (int)Math.Round(from + dir * d);
                    if (yy < 0 || yy >= ink.H) break;
                    if (ink[xx, yy]) { best = d / s; break; }
                }
            return best;
        }
        // the signs: the detector's sure ones, and its unsure ones (over WeakOttava) whose dashes run on
        // from them in the ink for two spaces or more, or a sure hook ends at their height (piano-collide's
        // 8va at 0.49); one sign seen as two, 8va and 8vb at one place, is the surer reading (basic002's at 0.75 and 0.53)
        bool DashesOn(Detection t) => Ottavas.DashesOn(ink, s, t, ends);
        // a sign the detector saw in a word, the "mo" of a "molto rit." or the "po" of a "poco rit." (a text
        // spanner's words, its dashes after them: S25): letters go on from its right edge for two spaces and
        // more, gaps of half a space at most between them (the "va" of an 8va the detector boxed as its 8
        // ends within one and a half), or for six tenths past an unsure one (the closing bracket of a sure
        // "(8vb)" at a system's start reaches 0.8; "poco" seen at 0.25 runs on 0.7) with nothing just before
        // it (the opening bracket of an unsure "(15mb)"), or for one past an unsure one ("molto" seen at 0.27
        // in its middle runs on 1.2). A letter's column: ink a third of a space tall and more at its height,
        // not the whole window (a stem); the dashes are flat. Past an unsure one, a word may follow a word's
        // space on (a space at most), two spaces long and more, wider than a note: the "on" of a "con moto" under
        // the staff seen as an 8va at 0.30, its box over the whole "con", the "moto" 0.8 on (gen-lied-all lied009)
        bool InWords(Detection t)
        {
            int y0 = Math.Max(0, (int)(t.Y - 0.6 * s)), y1 = Math.Min(ink.H - 1, (int)(t.Y + 0.6 * s));
            bool Letter(int x)
            {
                if (x < 0 || x >= ink.W) return false;
                int n = 0;
                for (int y = y0; y <= y1; y++) if (ink[x, y]) n++;
                return n >= 0.35 * s && !(ink[x, y0] && ink[x, y1]);
            }
            int x0 = (int)(t.X + HalfWidth(t) * s) + 1, x1 = Math.Min(ink.W - 1, (int)(t.X + HalfWidth(t) * s + 4 * s));
            int last = x0, start = -1;
            for (int x = x0; x <= x1 && x - last <= (start < 0 && t.Score < Detector.Threshold ? 1.0 : 0.5) * s; x++)
                if (Letter(x)) { if (start < 0) start = x; last = x; }
            if (start - x0 > 0.5 * s)
            {
                if (Verbose) Console.WriteLine($"  octave sign {t.Class} {t.Score:F2} at {t.X:F0},{t.Y:F0}: a word {(last - start) / s:F2} spaces long {(start - x0) / s:F2} past it");
                return last - start >= 2 * s;
            }
            // (and just before it: a bracket, or the word's own letters)
            int left = (int)(t.X - HalfWidth(t) * s) - 1, first = left;
            for (int x = left; x >= Math.Max(0, left - (int)(4 * s)) && first - x <= 0.5 * s; x--)
                if (Letter(x)) first = x;
            if (Verbose) Console.WriteLine($"  octave sign {t.Class} {t.Score:F2} at {t.X:F0},{t.Y:F0}: letters on {(last - x0) / s:F2} spaces past it, {(left - first) / s:F2} before it");
            return last - x0 >= 2 * s || t.Score < Detector.Threshold && (last - x0 >= 1.0 * s || last - x0 >= 0.6 * s && first == left);
        }
        var weak = Pooled(res.Sym.WeakOttavas, s);
        if (Verbose) foreach (var t in weak) Console.WriteLine($"  weak octave sign {t.Class} {t.Score:F2} at {t.X:F0},{t.Y:F0}");
        // (or a wave runs on from them, which is no word's letters: kinkel7-1's scan, an "8va~~~" read as an 8vb at 0.36)
        var signs = det.Where(IsSign).Concat(weak.Where(t => t.Score > Detector.WeakOttava && (DashesOn(t) || Wavy(t))))
            .Concat(BareEights(res, det))
            .OrderByDescending(t => t.Score).ToList();
        signs = signs.Where((t, i) => !signs.Take(i).Any(o => Math.Abs(o.X - t.X) < 1.5 * s && Math.Abs(o.Y - t.Y) < 1.0 * s))
            .Where(t => Wavy(t) || !InWords(t))
            .OrderBy(t => t.Y).ThenBy(t => t.X).ToList();
        if (signs.Count == 0) return marks;
        var found = new List<Line>();
        // the staff a sign stands over and the one it hangs under, within fourteen spaces, in the systems its x falls in
        ((SystemInfo Sys, Staff St) Over, (SystemInfo Sys, Staff St) Under) Beside(Detection t)
        {
            var staves = res.Systems.Where(q => q.Staves.Count > 0 && q.Measures > 0 && t.X > q.Bars[0] - 2 * s && t.X < q.Bars[^1] + 2 * s)
                .SelectMany(q => q.Staves.Select(st => (Sys: q, St: st))).ToList();
            return (staves.Where(p => p.St.Top > t.Y && p.St.Top - t.Y < 14 * s).OrderBy(p => p.St.Top).FirstOrDefault(),
                    staves.Where(p => p.St.Bottom < t.Y && t.Y - p.St.Bottom < 14 * s).OrderByDescending(p => p.St.Bottom).FirstOrDefault());
        }
        foreach (var t in signs)
        {
            double half = HalfWidth(t) * s, left = t.X - half;
            // the hook that ends it: the nearest to the right at its height, with no other sign between
            var end = ends.Where(e => Math.Abs(e.Y - t.Y) < 1.5 * s && e.X > t.X + half)
                .Where(e => !signs.Any(o => o != t && Math.Abs(o.Y - t.Y) < 1.5 * s && o.X > t.X && o.X < e.X))
                .OrderBy(e => e.X).FirstOrDefault();
            // (a hook off the sign's own height may be another line's, when the dashes end in the ink well
            // before it: piano003's "(8vb)" at a system's start, its hook unseen by the detector, and the next
            // 8vb's, lower by 1.8 spaces, at the system's end; seed 11)
            if (end != null && Math.Abs(end.Y - t.Y) >= 0.4 * s
                && Ottavas.DashesEnd(ink, s, t.X + half, t.Y, out bool inkHook, out bool inkDashed) is var inkEnd
                && (inkHook && inkEnd < end.X - s || inkDashed && inkEnd < end.X - 3 * s))
                end = null;
            var (over, under) = Beside(t);
            if (over.St == null && under.St == null) { if (Verbose) res.Notes.Add($"octave line sign at {t.X:F0},{t.Y:F0}: no staff near"); continue; }
            // the side the letters say; with no staff there, the other side when the hook (the detector's,
            // or the one in the ink where the dashes end) points that way
            bool up = SaysUp(t), sure = true, hooked = false, hookSide = false;
            double dashes = end != null ? 0 : Wavy(t) ? Wave(t).End : Ottavas.DashesEnd(ink, s, t.X + half, t.Y, out hooked, out _);
            // (the hook's direction: the detector's hook, or the one the dashes run into in the ink; a
            // dash's bare end tells nothing, whatever ink stands near it)
            int hook = end != null ? HookDir(end.X, end.Y) : hooked ? HookDir(dashes + 0.1 * s, t.Y) : 0;
            if ((up ? over : under).St == null)
            {
                if (hook == 0 || (hook > 0) == up) { if (Verbose) res.Notes.Add($"octave line sign {t.Class[4..]} at {t.X:F0},{t.Y:F0}: no staff on its side, and no hook pointing the other way"); continue; }
                up = !up; sure = false;
                if (Verbose) res.Notes.Add($"octave line sign {t.Class[4..]} at {t.X:F0},{t.Y:F0}: no staff on its side; taken {(up ? "over" : "under")} by its hook");
            }
            else if (t.Score < Symbols.SureOttava && (up ? under : over).St != null)
            {
                // letters read unsurely, a staff on either side (an 8va read as an 8vb at 0.46-0.55: basic008's
                // over its staff, hung under the staff above; piano002's over the left hand, hung under the
                // right): the line is drawn close over (under) its staff's ink, so the other side's is the
                // staff when the letters' side's ink is three times as far (piano006's 8vb and 8va in one
                // gap, at 1.2 and 1.3, stay as read). A sign read unsurely (under Detector.UnsureOttava) has
                // its hook, which points at the line's staff, as the first word, when the hook is its own
                // (the detector's at its dashes' height, or the one its dashes run into: in a grand staff's
                // gap the other line's hook can stand at the same height, and the ink can stand nearer the
                // other side, piano002's 8va over the left hand at 0.8 from the right hand's flags)
                double endX = end != null ? end.X : dashes, own = InkDistance(t, endX, up ? 1 : -1), other = InkDistance(t, endX, up ? -1 : 1);
                bool inkSaysOther = other < 1.5 && own > 3 * other;
                bool ownHook = end == null || Math.Abs(end.Y - t.Y) < 0.4 * s;
                if (Verbose) res.Notes.Add($"octave line sign {t.Class[4..]} at {t.X:F0},{t.Y:F0} ({t.Score:F2}): hook {hook}{(ownHook ? "" : " (another's)")}, ink {own:F1} off its own side, {other:F1} off the other, dashes to {dashes:F0}{(end != null ? $", hook seen at {end.X:F0}" : "")}");
                if (t.Score < Detector.UnsureOttava && hook != 0 && ownHook)
                {
                    hookSide = true;
                    if ((hook > 0) != up) { up = !up; sure = false; if (Verbose) res.Notes.Add($"octave line sign {t.Class[4..]} at {t.X:F0},{t.Y:F0} ({t.Score:F2}): its hook points the other way; taken {(up ? "over" : "under")}"); }
                }
                else if (inkSaysOther) { up = !up; sure = false; if (Verbose) res.Notes.Add($"octave line sign {t.Class[4..]} at {t.X:F0},{t.Y:F0} ({t.Score:F2}): drawn {other:F1} spaces off the other side's ink, {own:F1} off its own; taken {(up ? "over" : "under")}"); }
                // (letters read unsurely with no hook to tell, nor the ink: the piece going on from it, or the
                // one it goes on from, may say the side. ledger010's 15mb over the system's last note, read as
                // a 15ma at 0.65, its "(15mb)" at the next system's start)
                else if (t.Score < Detector.UnsureOttava && hook == 0) sure = false;
            }
            var line = new Line { Sign = t, Over = over, Under = under, Left = left, Up = up, Sure = sure, Hooked = hookSide };
            Place(line, end);
            found.Add(line);
        }
        // a line that reaches its system's end with no sign of the detector's taking it up at the next
        // system's start: the detector's unsure sign there (over WeakOttava, with no dashes running on
        // from it: a "(15ma)" over one note, read as a 15mb at 0.48, the next note's stem and beam right
        // after it, basic001's bar 10, whose c'''4 read two octaves low), on the same part's staff on the
        // line's side and before that staff's first note, takes the line up all the same
        foreach (var f in found.Where(f => !f.Closed).ToList())
        {
            var mine = f.Up ? f.Over : f.Under;
            if (mine.St == null) continue;
            if (!Continuation.Next(res, mine.Sys, mine.St.Part, out var next, out var st)) continue;
            double first = res.Sym.Heads.Where(h => h.On(st) && h.X > next.Bars[0]).Select(h => h.X).DefaultIfEmpty(next.Bars[^1]).Min();
            if (found.Any(g => g.Sys == next && g.St == st && g.Left < first + 0.5 * s)) continue;
            var w = weak.Where(t => t.Score > Detector.WeakOttava && Octaves(t) == Octaves(f.Sign)
                    && t.X > next.Bars[0] - 2 * s && t.X - HalfWidth(t) * s < first + 0.5 * s
                    && !signs.Any(o => Math.Abs(o.X - t.X) < 1.5 * s && Math.Abs(o.Y - t.Y) < 1.0 * s)
                    && (f.Up ? Beside(t).Over : Beside(t).Under).St == st)
                .OrderByDescending(t => t.Score).FirstOrDefault();
            if (w == null) continue;
            var (over, under) = Beside(w);
            var line = new Line { Sign = w, Over = over, Under = under, Left = w.X - HalfWidth(w) * s, Up = f.Up, Sure = false };
            Place(line, null);
            found.Add(line); signs.Add(w);
            if (Verbose) res.Notes.Add($"octave line sign {w.Class[4..]} at {w.X:F0},{w.Y:F0} ({w.Score:F2}): unsure, before the first note of part {st.Part} in the next system; takes up the line from {f.Sign.X:F0},{f.Sign.Y:F0}, to x {line.Mark.EndX:F0} (page)");
        }
        // and the other way: a sign at a system's start, before its staff's first note, that no line reaching
        // the system before's end goes on into: the detector's unsure sign there (over WeakOttava, with no
        // dashes: a 15ma over the system's last note, its letters past the barline, piano002's bar 2, seen
        // as a 15ma at 0.46 and a 15mb at 0.25), of its octave count, on the same part's staff on its side
        // and over that staff's last note, starts the line. The sign at the start may be unsure of its
        // letters when its own hook told its side (seed 11's basic004: the "(15ma)" read as a 15mb at 0.66,
        // its hook up, and the 15ma over the system before's last note seen at 0.50)
        foreach (var g in found.Where(g => g.Sure || g.Hooked).ToList())
        {
            if (!Continuation.Next(res, g.Sys, g.St.Part, out var prev, out var st, by: -1)) continue;
            if (res.Sym.Heads.Any(h => h.On(g.St) && h.X > g.Sys.Bars[0] && h.X < g.Left - 0.5 * s)) continue;
            if (found.Any(f => !f.Closed && f.Sys == prev && f.St == st)) continue;
            var heads = res.Sym.Heads.Where(h => h.On(st)).Select(h => h.X).OrderByDescending(x => x).Take(2).ToList();
            if (heads.Count < 2) continue;
            // (over the last note: its middle past halfway from the last note but one; its letters may start
            // further left, at the last note's accidental -- lysc 371e8f3e8 printing the natural of basic004's
            // a' under the 15ma after the aes' before it)
            var w = weak.Where(t => t.Score > Detector.WeakOttava && Octaves(t) == Octaves(g.Sign)
                    && t.X > (heads[0] + heads[1]) / 2 && t.X < prev.Bars[^1] + 2 * s
                    && !signs.Any(o => Math.Abs(o.X - t.X) < 1.5 * s && Math.Abs(o.Y - t.Y) < 1.0 * s)
                    && (g.Up ? Beside(t).Over : Beside(t).Under) is var b && b.St == st && b.Sys == prev)
                .OrderByDescending(t => t.Score).FirstOrDefault();
            if (w == null) continue;
            var (over, under) = Beside(w);
            var line = new Line { Sign = w, Over = over, Under = under, Left = w.X - HalfWidth(w) * s, Up = g.Up, Sure = false };
            Place(line, null);
            if (line.Closed) continue;
            found.Add(line); signs.Add(w);
            if (Verbose) res.Notes.Add($"octave line sign {w.Class[4..]} at {w.X:F0},{w.Y:F0} ({w.Score:F2}): unsure, over the last note of part {st.Part} in the system before the line at {g.Sign.X:F0},{g.Sign.Y:F0}; starts it");
        }
        // a line that reaches its system's end goes on where the next system's sign stands at its first note:
        // the same part's, on the same side. A sign told by nothing surer is that line's; a line told by
        // nothing surer (no hook: it ran on) takes the side of the piece that goes on from it, which has one
        foreach (var f in found.Where(f => !f.Closed))
        {
            Line next = null;
            foreach (bool u in f.Sure ? new[] { f.Up } : new[] { f.Up, !f.Up })
            {
                var mine = u ? f.Over : f.Under;
                if (mine.St == null) continue;
                foreach (var g in found.Where(g => g.Sys.Index == mine.Sys.Index + 1 && Octaves(g.Sign) == Octaves(f.Sign) && (g.Up == u || !g.Sure)))
                {
                    var cand = u ? g.Over : g.Under;
                    if (cand.St == null || cand.Sys != g.Sys || cand.St.Part != mine.St.Part) continue;
                    if (res.Sym.Heads.Any(h => h.On(cand.St) && h.X > g.Sys.Bars[0] && h.X < g.Left - 0.5 * s)) continue;
                    if (g.Up != u) { g.Up = u; Place(g, null); if (Verbose) res.Notes.Add($"octave line at {g.Sign.X:F0},{g.Sign.Y:F0}: goes on from the system before, {(u ? "over" : "under")} part {mine.St.Part}"); }
                    if (f.Up != u) { f.Up = u; Place(f, null); if (Verbose) res.Notes.Add($"octave line at {f.Sign.X:F0},{f.Sign.Y:F0}: {(u ? "over" : "under")} part {mine.St.Part}, as the piece going on from it is"); }
                    next = g; break;
                }
                if (next != null) break;
            }
            if (next != null) next.Mark.Continued = true;
            // (one run out past the page's last system: left open for the next page, ended here should that
            // page not go on with it)
            else if (Continuation.LastOnPage(res, f.Sys))
            {
                var m = f.Mark;
                m.Ends = new List<Ev>();
                res.Open.Add(new Continuation.Open($"ottava{m.Ottava}", f.St.Part, f.Left, (at, end) =>
                {
                    if (at != null) return;
                    m.NoEnd = false;
                    foreach (var e in m.Ends) e.OttavaEnd = m.Ottava;
                }));
            }
            else f.Mark.NoEnd = false;
        }
        // the lines the page before ran out past its last system: a sign at the part's staff's start in this
        // page's first system, before its first note, of the same octaves on the same side (or told by nothing
        // surer) is the piece going on from it, as at a next system's start
        foreach (var o in Continuation.Going(res, "ottava"))
        {
            int ottava = int.Parse(o.Kind["ottava".Length..]);
            bool u = ottava > 0;
            if (!Continuation.First(res, o.Part, out var sys, out var on)) continue;
            var g = found.Where(g => g.Sys == sys && (u ? g.Over : g.Under) is var c && c.St == on && c.Sys == sys
                                     && !g.Mark.Continued && Octaves(g.Sign) == Math.Abs(ottava) && (g.Up == u || !g.Sure)
                                     && !res.Sym.Heads.Any(h => h.On(on) && h.X > sys.Bars[0] && h.X < g.Left - 0.5 * s))
                         .MinBy(g => g.Left);
            if (g == null) continue;
            if (g.Up != u) { g.Up = u; Place(g, null); }
            g.Mark.Continued = true;
            o.Close(res, null);
            if (Verbose) res.Notes.Add($"octave line at {g.Sign.X:F0},{g.Sign.Y:F0}: goes on from the page before, {(u ? "over" : "under")} part {o.Part}");
        }
        foreach (var f in found)
        {
            marks.Add(f.Mark);
            if (Verbose) res.Notes.Add($"octave line {f.Mark.Ottava} at {f.Sign.X:F0},{f.Sign.Y:F0} ({f.Sign.Score:F2}): s{f.Sys.Index + 1} part {f.St.Part}, bars {f.Mark.Bar}-{f.Mark.EndBar}, to x {f.Mark.EndX:F0} (page){(f.Mark.NoEnd ? ", going on" : "")}{(f.Mark.Continued ? ", gone on from the system before" : "")}");
        }
        return marks;

        // the line's mark: its staff by its side, its end (the hook; else the dashes' end in the ink,
        // or the system's end when they reach it) and its bars
        void Place(Line line, Detection end)
        {
            var t = line.Sign;
            double half = HalfWidth(t) * s;
            var (sys, st) = line.Up ? line.Over : line.Under;
            int BarAt(double x) { int k = 0; while (k + 1 < sys.Measures && x >= sys.Bars[k + 1] - 0.5 * s) k++; return sys.BarOf(k); }
            double dashes = end != null ? end.X : Wavy(t) ? Wave(t).End : DashesEnd(t.X + half, t.Y);
            // (the scan stops at the next sign at the height, and a line with no dash before it is its letters
            // only: seed 11's piano006, a "(8vb)" over one rest at a system's start, its hook in its letters,
            // ran through the next 8vb's letters, narrow pieces of them taken for its tail, to that one's hook)
            if (end == null && !Wavy(t) && signs.Where(o => o != t && Math.Abs(o.Y - t.Y) < 1.0 * s && o.X > t.X + half)
                    .Select(o => o.X - HalfWidth(o) * s).Where(l => l < dashes).DefaultIfEmpty(double.NaN).Min() is var next && !double.IsNaN(next))
            {
                dashes = Ottavas.DashesEnd(ink, s, t.X + half, t.Y, out _, out bool dashed, next);
                if (!dashed) dashes = t.X + half - 0.3 * s;
            }
            bool toEnd = end == null && dashes > sys.Bars[^1] - 3 * s;
            double endX = end != null ? end.X : toEnd ? sys.Bars[^1] : Math.Max(dashes, t.X + half - 0.3 * s);
            var bl = res.Dark.ToSource(line.Left, t.Y);
            int octaves = Octaves(t) * (line.Up ? 1 : -1);
            var box = new RectangleF((float)bl.X, (float)(bl.Y - 0.75 * s), (float)(2 * half), (float)(1.5 * s));
            // (the notes under the line start at the sign's left: Lily# sets the letters' left a little left of the first note's head)
            line.Mark = new Tempo.Mark(BarAt(line.Left), null, null, null, box)
            {
                Ottava = octaves, Part = st.Part, EndX = res.Dark.ToSource(endX, t.Y).X, EndBar = BarAt(endX),
                StartX = res.Dark.ToSource(line.Left + 0.3 * s, t.Y).X, NoEnd = toEnd, LastBar = sys.FirstBar + sys.BarCount - 1,
            };
            line.Sys = sys; line.St = st; line.Closed = !toEnd;
        }
    }

    // An octave line over a staff (or under it: Ottava < 0): the notes under it, from its sign to its
    // dashes' end, sound so many octaves up (down). Their pitches are raised (the .lys writes what
    // sounds), the first marked @8va and the last @!8va (Lily#'s end is the last event under the line,
    // since 2026-10-28), in the bars as read and in their
    // voices alike. A line that goes on into the next system (NoEnd) is not closed: the piece that goes
    // on from there (Continued) is not started again, and closes it
    public static void Apply(PageResult res, Tempo.Mark m)
    {
        // (the hook stands at the right edge of the last note under the line; the next note's head may be no more than a space on)
        double x0 = double.IsNaN(m.StartX) ? m.Box.Left - 0.5 * m.Box.Height : m.StartX, x1 = m.EndX + 0.2 * m.Box.Height;
        // (a chord by its stem where that is right of its x, and a head on the stem of one under it: a second's
        // lower head stands left of the stem, and of the letters' start, piano010's <g'' aes''>8@ottava; the
        // voices hold a chord as one event, the bars its heads as events of their own: those by their stem
        // too, or a second after the line is under it in the bars and not in the voices, piano003's (seed 11)
        // <eis, fis,>16@!ottava, its lower head left of the stem and of the hook). A head by its centre is
        // under the line up to the hook itself: Lily# may draw the hook on to over the next note's head, short
        // of its centre (seed 11's piano004, an 8va over ges,8 its hook 0.15 of a space before f,,16's centre)
        bool Under(Ev e, bool chord) => !e.IsRest && !e.Slash && (chord && e.StemX > e.X) is var byStem
            && res.Dark.ToSource(byStem ? e.StemX : e.X, e.Y).X is var x && x >= x0 && x <= (byStem ? x1 : m.EndX);
        bool OnStem(Ev e, Ev u) => e.Stem != 0 && u.Stem != 0 && Math.Abs(e.StemX - u.StemX) < 0.3 * res.S && Math.Abs(e.Y - u.Y) < 4 * res.S;
        int by = 7 * m.Ottava;
        Pitch Up(Pitch p) => new(p.Diatonic + by, p.Alter);
        var raisedAlts = new HashSet<EvAlt>();     // (an event's other readings are shared by its copies: raised once)
        var raised = new HashSet<Ev>();            // (a bar of one voice holds the same events in both lists)
        void Raise(List<Ev> evs)
        {
            var sure = evs.Where(e => Under(e, e.Chord != null || evs.Any(u => u != e && !u.IsRest && !u.Slash && OnStem(e, u)))).ToList();
            var under = evs.Where(e => sure.Contains(e) || !e.IsRest && !e.Slash && sure.Any(u => OnStem(e, u))).ToList();
            if (Ottavas.Verbose) res.Notes.Add($"octave line {m.Ottava} bars {m.Bar}-{m.EndBar}: x {x0:F0}-{x1:F0} (page): {under.Count} of {evs.Count} events under it: {string.Join(" ", under.Select(e => $"{e.Show()}@{res.Dark.ToSource(e.X, e.Y).X:F0}"))}");
            foreach (var e in under.Where(raised.Add))
            {
                e.P = Up(e.P);
                if (e.Chord != null) e.Chord = e.Chord.Select(Up).ToList();
                if (e.Tied != null) e.Tied = e.Tied.Select(Up).ToList();
                if (e.Graces != null) e.Graces = e.Graces.Select(Up).ToList();
                foreach (var a in e.Alts ?? new()) if (raisedAlts.Add(a)) { a.From = Up(a.From); a.To = Up(a.To); }
            }
            if (under.Count == 0) return;
            if (!m.Continued) under[0].OttavaStart = m.Ottava;
            if (m.NoEnd && m.Ends == null) return;
            int last = evs.IndexOf(under[^1]);
            // (the rests after the last note, short of the line's end, are under it too: the end is written
            // on the last of them, where the page's hook stands. piano006's bar 8, fisis,,,8. r16 | e,,8.
            // Not a whole-bar rest: its marks are not written)
            while (last + 1 < evs.Count && evs[last + 1].IsRest && !evs[last + 1].FullBar && res.Dark.ToSource(evs[last + 1].X, evs[last + 1].Y).X <= x1) last++;
            var end = evs[last];
            // (a line left open for the next page: its end there, or here should that page not go on with it)
            if (m.NoEnd) m.Ends.Add(end);
            else end.OttavaEnd = m.Ottava;
        }
        // (the part's events in order over the bars: the line may run across barlines; its system's bars
        // only, and the bar after, where its end may fall, while that is the system's: another system's
        // notes stand at the same x)
        bool Mine((int Bar, int Part) k) => k.Part == m.Part && k.Bar >= m.Bar && k.Bar <= Math.Min(m.EndBar + 1, m.LastBar);
        Raise(res.Bars.Where(kv => Mine(kv.Key)).OrderBy(kv => kv.Key.Bar).SelectMany(kv => kv.Value).ToList());
        Raise(res.Voices.Where(kv => Mine(kv.Key)).OrderBy(kv => kv.Key.Bar).SelectMany(kv => kv.Value.SelectMany(v => v).OrderBy(e => e.X)).ToList());
    }
}
