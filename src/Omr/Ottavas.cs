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
    /// printed; thin, and on the row the first dash set: the dashes are collinear) before a gap of two
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
        // the ink in the column within the band: 0 none, 1 a dash's (thin, on the row; or a stem
        // through the band), 2 something else's; with the rows it covers
        (int Kind, int Top, int Bottom) Column(int xx)
        {
            int top = -1, bottom = -1;
            for (int yy = y0; yy <= y1; yy++) if (ink[xx, yy]) { if (top < 0) top = yy; bottom = yy; }
            if (top < 0) return (0, top, bottom);
            double thick = bottom - top + 1, centre = (top + bottom) / 2.0;
            if (thick <= 0.25 * s && (double.IsNaN(row) || Math.Abs(centre - row) <= 0.15 * s)) return (1, top, bottom);
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
                    if (xx - runStart >= 0.35 * s || !double.IsNaN(row)) { dashSeen = dashed = true; if (double.IsNaN(row) && rowN > 0) row = rowSum / rowN; }
                    last = xx - 1; runStart = -1;
                }
                continue;
            }
            if (kind == 1)
            {
                if (runStart < 0) { runStart = xx; rowSum = 0; rowN = 0; }
                if (xx - runStart > 1.2 * s) break;
                if (bottom - top + 1 <= 0.25 * s) { rowSum += (top + bottom) / 2.0; rowN++; }
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
            // (the run leading into it is a dash too, when long enough: "- " then the hook, with no gap
            // between them to end the run, sets the row here: basic001's "(15ma)" over one note)
            if (runStart >= 0 && xx - runStart >= 0.35 * s && double.IsNaN(row) && rowN > 0) { row = rowSum / rowN; dashSeen = dashed = true; }
            if (runStart >= 0 && !double.IsNaN(row) && w <= 0.3 * s)
            {
                int r = (int)Math.Round(row);
                double up = Reach(xx, r, -1), down = Reach(xx, r, 1);
                if (ink[xx, r] && Math.Max(up, down) >= 0.4 * s && Math.Min(up, down) <= 0.2 * s && !DashAhead(xx + w)) { hooked = true; return xx + w - 1; }
            }
            // (or the hook's upright alone, a dash's gap after the last dash: Lily# leaves the hook's foot out
            // where the dashes' pattern has a gap, seed 11's ledger007 bar 9. Short, as a hook is, not a stem
            // ending at the row. Its end within the dash's thickness of the row, a pixel off it: seed 11's
            // piano007, an 8va's hook from 3478 under dashes at 3475-3479, the line taken as going on)
            if (runStart < 0 && dashSeen && !double.IsNaN(row) && w <= 0.3 * s
                && Enumerable.Range((int)Math.Round(row - 0.1 * s), (int)Math.Round(0.2 * s) + 1).Where(rr => ink[xx, rr]).DefaultIfEmpty(-1).First() is var hr && hr >= 0)
            {
                double up = Reach(xx, hr, -1), down = Reach(xx, hr, 1);
                if (Math.Max(up, down) >= 0.4 * s && Math.Max(up, down) <= 1.2 * s && Math.Min(up, down) <= 0.2 * s && !DashAhead(xx + w)) { hooked = true; return xx + w - 1; }
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
        // the nearest ink to the line on one side (dir 1 below it, -1 above), in spaces: over the columns
        // past the letters and short of the hook, from past the dashes to fourteen spaces off; 99 for none
        double InkDistance(Detection t, double endX, int dir)
        {
            int x0 = (int)(t.X + HalfWidth(t) * s + 0.3 * s), x1 = (int)Math.Min(ink.W - 1, endX - 1.0 * s);
            double best = 99;
            for (int xx = x0; xx <= x1; xx++)
                for (double d = 0.35 * s; d < best * s && d < 14 * s; d++)
                {
                    int yy = (int)Math.Round(t.Y + dir * d);
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
        // not the whole window (a stem); the dashes are flat
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
            int last = x0;
            for (int x = x0; x <= x1 && x - last <= 0.5 * s; x++)
                if (Letter(x)) last = x;
            // (and just before it: a bracket, or the word's own letters)
            int left = (int)(t.X - HalfWidth(t) * s) - 1, first = left;
            for (int x = left; x >= Math.Max(0, left - (int)(4 * s)) && first - x <= 0.5 * s; x--)
                if (Letter(x)) first = x;
            if (Verbose) Console.WriteLine($"  octave sign {t.Class} {t.Score:F2} at {t.X:F0},{t.Y:F0}: letters on {(last - x0) / s:F2} spaces past it, {(left - first) / s:F2} before it");
            return last - x0 >= 2 * s || t.Score < Detector.Threshold && (last - x0 >= 1.0 * s || last - x0 >= 0.6 * s && first == left);
        }
        var weak = Pooled(res.Sym.WeakOttavas, s);
        if (Verbose) foreach (var t in weak) Console.WriteLine($"  weak octave sign {t.Class} {t.Score:F2} at {t.X:F0},{t.Y:F0}");
        var signs = det.Where(IsSign).Concat(weak.Where(t => t.Score > Detector.WeakOttava && DashesOn(t)))
            .OrderByDescending(t => t.Score).ToList();
        signs = signs.Where((t, i) => !signs.Take(i).Any(o => Math.Abs(o.X - t.X) < 1.5 * s && Math.Abs(o.Y - t.Y) < 1.0 * s))
            .Where(t => !InWords(t))
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
            double dashes = end == null ? Ottavas.DashesEnd(ink, s, t.X + half, t.Y, out hooked, out _) : 0;
            // (the hook's direction: the detector's hook, or the one the dashes run into in the ink; a
            // dash's bare end tells nothing, whatever ink stands near it)
            int hook = end != null ? HookDir(end.X, end.Y) : hooked ? HookDir(dashes + 0.1 * s, t.Y) : 0;
            if ((up ? over : under).St == null)
            {
                if (hook == 0 || (hook > 0) == up) { if (Verbose) res.Notes.Add($"octave line sign {t.Class[4..]} at {t.X:F0},{t.Y:F0}: no staff on its side, and no hook pointing the other way"); continue; }
                up = !up; sure = false;
                if (Verbose) res.Notes.Add($"octave line sign {t.Class[4..]} at {t.X:F0},{t.Y:F0}: no staff on its side; taken {(up ? "over" : "under")} by its hook");
            }
            else if (t.Score < Symbols.SureScore && (up ? under : over).St != null)
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
                    foreach (var e in m.Ends) e.OttavaEnd = Math.Abs(m.Ottava);
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
            double dashes = end != null ? end.X : DashesEnd(t.X + half, t.Y);
            // (the scan stops at the next sign at the height, and a line with no dash before it is its letters
            // only: seed 11's piano006, a "(8vb)" over one rest at a system's start, its hook in its letters,
            // ran through the next 8vb's letters, narrow pieces of them taken for its tail, to that one's hook)
            if (end == null && signs.Where(o => o != t && Math.Abs(o.Y - t.Y) < 1.0 * s && o.X > t.X + half)
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
    // sounds), the first marked @ottava and the event after the last @!ottava (Lily#'s end is the first
    // note no longer under the line; the last note when none follows), in the bars as read and in their
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
                if (e.Graces != null) e.Graces = e.Graces.Select(Up).ToList();
                foreach (var a in e.Alts ?? new()) if (raisedAlts.Add(a)) { a.From = Up(a.From); a.To = Up(a.To); }
            }
            if (under.Count == 0) return;
            if (!m.Continued) under[0].OttavaStart = m.Ottava;
            if (m.NoEnd && m.Ends == null) return;
            int last = evs.IndexOf(under[^1]);
            // (the rests after the last note, short of the line's end, are under it too: the end is written
            // after them, where the page's hook stands. piano006's bar 8, fisis,,,8. r16 | e,,8)
            while (last + 1 < evs.Count && evs[last + 1].IsRest && res.Dark.ToSource(evs[last + 1].X, evs[last + 1].Y).X <= x1) last++;
            var end = last + 1 < evs.Count ? evs[last + 1] : evs[last];
            // (a line left open for the next page: its end there, or here should that page not go on with it)
            if (m.NoEnd) m.Ends.Add(end);
            else end.OttavaEnd = Math.Abs(m.Ottava);
        }
        // (the part's events in order over the bars: the line may run across barlines; its system's bars
        // only, and the bar after, where its end may fall, while that is the system's: another system's
        // notes stand at the same x)
        bool Mine((int Bar, int Part) k) => k.Part == m.Part && k.Bar >= m.Bar && k.Bar <= Math.Min(m.EndBar + 1, m.LastBar);
        Raise(res.Bars.Where(kv => Mine(kv.Key)).OrderBy(kv => kv.Key.Bar).SelectMany(kv => kv.Value).ToList());
        Raise(res.Voices.Where(kv => Mine(kv.Key)).OrderBy(kv => kv.Key.Bar).SelectMany(kv => kv.Value.SelectMany(v => v).OrderBy(e => e.X)).ToList());
    }
}
