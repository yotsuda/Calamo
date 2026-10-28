namespace Omr;

static partial class Symbols
{
    /// <summary>
    /// A chord repeated by its stem alone: under the chord's beam, a stem with no head whose foot
    /// ends in a thin slash plays the chord before it again (a band score's synthesizer, 揺れる想い's
    /// Syn.2: one chord, then seven such stems a bar). The detector sees each stem's tip (stem_N)
    /// and is unsure of the slash (a weak slash or cross, kept where it found a stem). A tip with no
    /// head's stem under it, its stem ending in such a slash, gets a copy of the nearest chord left
    /// of it on its staff, beamed level with it, the heads on the tip's stem; the weak readings of
    /// the slash go.
    /// </summary>
    static void ChordRepeats(Img ink, PageSymbols ps, List<Staff> staves, double s)
    {
        bool Loose(Head h) => (h.Cross || h.Slash) && h.Score < SureScore;
        var added = new List<Head>();
        var gone = new HashSet<Head>();
        // (the stem hangs from the chord's beam: a tip with no beam is a rhythm slash's own stem, a half's)
        foreach (var t in ps.Detections.Where(t => t.Class.StartsWith("stem_") && t.Class != "stem_0" && t.Score >= TrustScore).OrderBy(t => t.X))
        {
            var heads = ps.Heads.Concat(added).Where(h => !Loose(h) && h.HasStem).ToList();
            if (heads.Any(h => Math.Abs(h.StemX - t.X) < 0.5 * s && Math.Abs(h.Tip - t.Y) < 1.5 * s)) continue;
            // (the chord may be a half or a quarter, its own tip off the beam's height: E.G.2's dotted
            // half tied into two such eighths, its stem reaching four spaces over their beam)
            var near = heads.Where(h => h.StemX < t.X - 0.8 * s && t.X - h.StemX < 40 * s
                                        && Math.Abs(h.Tip - t.Y) < (h.Beams > 0 ? 2.5 : 4.5) * s).OrderByDescending(h => h.StemX).FirstOrDefault();
            if (near == null) continue;
            var chord = heads.Where(h => h.St == near.St && h.Up == near.Up && h.Hollow == near.Hollow && Math.Abs(h.StemX - near.StemX) < 0.3 * s).ToList();
            // the stem, from the tip towards where its head would be (stepping over a removed staff line)
            int dir = near.Up ? 1 : -1, bestX = 0, foot = 0, len = 0;
            for (int x = (int)Math.Round(t.X) - 2; x <= (int)Math.Round(t.X) + 2; x++)
            {
                int y = (int)Math.Round(t.Y), last = -1;
                for (; Math.Abs(y - t.Y) < 6 * s; y += dir)
                    if (ink[x, y]) last = y;
                    else if (last >= 0 && Math.Abs(y - last) > 0.25 * s) break;
                if (last >= 0 && Math.Abs(last - t.Y) > len) { len = (int)Math.Abs(last - t.Y); bestX = x; foot = last; }
            }
            if (len < 1.5 * s || Nearest(staves, foot, s) != near.St) continue;
            bool dbg = DebugAt.HasValue && Math.Abs(t.X - DebugAt.Value.X) < s && Math.Abs(t.Y - DebugAt.Value.Y) < 2 * s;
            if (!SlashAt(ink, bestX, foot, dir, s, out var end, dbg)) continue;
            // (a head the detector is sure of at the foot is the stem's own, missed above)
            if (ps.Heads.Any(h => !Loose(h) && h.St == near.St && Math.Abs(h.X - bestX) < 1.2 * s && Math.Abs(h.Y - foot) < 1.2 * s)) continue;
            int beams = t.Class[5] - '0';
            // (removing the staff lines cuts the slash from its stem: its pieces read as a triplet's 3)
            ps.RepeatSlashes.Add((Math.Min(bestX, end.X) - 0.3 * s, Math.Min(foot, end.Y) - 0.3 * s, Math.Max(bestX, end.X) + 0.3 * s, Math.Max(foot, end.Y) + 0.3 * s));
            foreach (var h in chord)
            {
                double dx = bestX - h.StemX;
                added.Add(new Head
                {
                    St = h.St, X = h.X + dx, Y = h.Y, Pos = h.Pos, L = h.L + dx, R = h.R + dx, OuterW = h.OuterW,
                    HasStem = true, Up = h.Up, StemX = bestX, Tip = t.Y, Beams = beams, Score = t.Score,
                });
            }
            // the slash's weak readings: on this stem, or about the slash below its foot
            foreach (var h in ps.Heads.Where(h => Loose(h) && h.St == near.St
                         && ((h.HasStem && Math.Abs(h.StemX - bestX) < 0.6 * s)
                             || (Math.Abs(h.X - bestX) < 2 * s && (h.Y - foot) * dir > -0.6 * s && (h.Y - foot) * dir < 2.4 * s))))
                gone.Add(h);
        }
        if (added.Count == 0) return;
        ps.Heads.RemoveAll(gone.Contains);
        ps.Heads.AddRange(added);
        ps.Heads.Sort((a, b) => a.X.CompareTo(b.X));
        var stems = added.GroupBy(h => h.StemX).Select(g => $"{g.Key:F0},{g.First().Tip:F0}").ToList();
        ps.Note = (ps.Note == null ? "" : ps.Note + "; ") + $"chord repeats {stems.Count} (stems at {string.Join(" ", stems.Take(12))})";
    }

    /// <summary>
    /// Two-note tremolos: two hollow notes next to each other on a staff (halves, or wholes with no
    /// stems), with nothing between them but one to four thick strokes, beams that run from the one
    /// to the other (MuseScore floats them between the stems, older prints join the stems with
    /// them). The two alternate for the time of one of them: each plays half its written value
    /// (kinkel10-4's dotted halves in 6/8, two to a bar). Strokes: a column of the gap's middle
    /// holds that many runs of ink a beam's thickness across (a tie or a slur is thinner), in four
    /// columns of five and more.
    /// </summary>
    public static bool NoTremolos;   // --no-tremolos (to tell what this does)

    static void Tremolos(Img ink, PageSymbols ps, double s)
    {
        if (NoTremolos) return;
        var heads = ps.Heads.Where(h => !h.Grace && !h.Slash && !h.Cross).ToList();
        bool Mates(Head h, Head g) => h.HasStem && g.HasStem ? Math.Abs(h.StemX - g.StemX) < 0.3 * s : Math.Abs(h.X - g.X) < 0.35 * s;
        int found = 0;
        foreach (var a in heads)
        {
            // (or two quarters, black, their stems with no beam: the strokes float between them, MuseScore's
            // dotted quarters before kinkel10-4's fermata; strokes that reach the stems are an eighths' beam)
            if (a.Tremolo > 0 || !(a.Hollow || a.HasStem && a.Beams == 0)) continue;
            // (its mate: the next head right of it on its staff, out of its own chord)
            var b = heads.Where(h => h.St == a.St && h.X > a.X + 0.5 * s && !Mates(h, a)).OrderBy(h => h.X).FirstOrDefault();
            if (b == null || b.Hollow != a.Hollow || b.L - a.R > 12 * s) continue;
            // (a half's stem may go unseen, the head read as a whole: its mate's stem tells. Two quarters have both)
            if (!a.Hollow && !(b.HasStem && b.Beams == 0)) continue;
            var ca = heads.Where(h => h.St == a.St && Mates(h, a)).ToList();
            var cb = heads.Where(h => h.St == a.St && Mates(h, b)).ToList();
            if (ca.Any(h => h.Hollow != a.Hollow) || cb.Any(h => h.Hollow != a.Hollow)) continue;
            // the gap: from a's right (or its stem, up on its right) to b's left (or its stem, down on its left)
            double xl = Math.Max(ca.Max(h => h.R), a.HasStem ? a.StemX : 0) + 0.25 * s;
            double xr = Math.Min(cb.Min(h => h.L), b.HasStem ? b.StemX : double.MaxValue) - 0.25 * s;
            if (xr - xl < 0.8 * s) continue;
            bool stems = a.HasStem || b.HasStem;
            var ys = ca.Concat(cb).Select(h => h.Y).Concat(new[] { a, b }.Where(h => h.HasStem).Select(h => h.Tip)).ToList();
            double yt = ys.Min() - (stems ? 0.5 : 3) * s, yb = ys.Max() + (stems ? 0.5 : 3) * s;
            double w = xr - xl;
            int cols = 0, struck = 0;
            var counts = new int[6];
            for (int x = (int)(xl + 0.2 * w); x <= xr - 0.2 * w; x++)
            {
                cols++;
                int runs = 0, run = 0;
                bool odd = false;
                for (int y = (int)yt; y <= yb + 1; y++)
                {
                    if (y <= yb && ink[x, y]) { run++; continue; }
                    if (run > 0)
                    {
                        if (run >= 0.33 * s && run <= 1.1 * s) runs++;
                        else if (run > 1.1 * s) odd = true;
                    }
                    run = 0;
                }
                if (odd || runs == 0 || runs > 4) continue;
                struck++;
                counts[runs]++;
            }
            bool dbg = DebugAt.HasValue && Math.Abs(a.X - DebugAt.Value.X) < 2 * s && Math.Abs(a.Y - DebugAt.Value.Y) < 4 * s;
            if (dbg) Console.WriteLine($"tremolo from {a.X:F0},{a.Y:F0} to {b.X:F0},{b.Y:F0}: gap {xl:F0}-{xr:F0}, {struck} of {cols} columns struck, runs {string.Join(" ", counts)}");
            if (cols < 3 || struck < 0.8 * cols) continue;
            int strokes = Array.IndexOf(counts, counts.Max());
            foreach (var h in ca) { h.Tremolo = strokes; h.TremoloFirst = true; h.TremoloStem = stems; }
            foreach (var h in cb) { h.Tremolo = strokes; h.TremoloStem = stems; }
            found++;
        }
        if (found > 0) ps.Note = (ps.Note == null ? "" : ps.Note + "; ") + $"two-note tremolos {found}";
    }

    /// <summary>
    /// Beat repeats: on the staff, where no head stands, two to four short slashes side by side,
    /// each a beam's thickness and leaning up to the right (steeper than a beam): the beam group
    /// before them is played again, one mark one group (kinkel10-4's old print, 32nds a quaver at a
    /// time, "///" twice after each group). The strokes are the group's beams. Taking the staff
    /// lines out leaves the strokes of a mark, and the marks side by side, joined by the line's
    /// remains or a stroke cut in two: the pieces near each other on a staff are taken together,
    /// their ink cast along the slant that stacks it best, and each tall heap in that is a stroke.
    /// Their pieces are no symbols. Reader.StaffEvents writes the group out again where the bar has
    /// room for it (Lily# has no beat repeat: its migration too writes the notes out).
    /// </summary>
    public static bool NoBeatRepeats;   // --no-beat-repeats (to tell what this does)

    static HashSet<int> BeatRepeats(int[] labels, List<Comp> comps, HashSet<int> clusters, PageSymbols ps, List<Staff> staves, double s)
    {
        var ids = new HashSet<int>();
        if (NoBeatRepeats) return ids;
        int w = ps.LabelW;
        // the pieces on a staff that are no notes', a stroke's height at most, near ones together
        // (a piece only a stemless slash or cross the detector is unsure of stands on is no note's: kinkel19-6's
        // old print, its thin strokes seen as rhythm slashes at 0.61 and 0.74 -- as among the strokes below)
        bool Unsure(Head h) => (h.Cross || h.Slash) && h.Score < SureScore && !h.HasStem;
        var notes = ps.Heads.Where(h => !Unsure(h)).Select(h => h.Cluster).ToHashSet();
        var unsure = ps.Heads.Where(Unsure).Select(h => h.Cluster).Where(id => !notes.Contains(id)).ToHashSet();
        var pieces = new List<(Comp C, Staff St)>();
        for (int i = 1; i < comps.Count; i++)
        {
            var c = comps[i];
            if (clusters.Contains(i) && !unsure.Contains(i) || c.W > 12 * s || c.H > 3.2 * s || c.W < 0.15 * s && c.H < 0.15 * s) continue;
            var st = Nearest(staves, c.Cy, s);
            if (st == null || st.Kind != "" || c.MinY < st.Top - 0.6 * s || c.MaxY > st.Bottom + 0.6 * s) continue;
            pieces.Add((c, st));
        }
        pieces.Sort((a, b) => a.C.MinX.CompareTo(b.C.MinX));
        var blobs = new List<List<(Comp C, Staff St)>>();
        foreach (var p in pieces)
        {
            var into = blobs.LastOrDefault(b => b[0].St == p.St && p.C.MinX <= b.Max(q => q.C.MaxX) + 0.8 * s);
            if (into != null) into.Add(p); else blobs.Add(new() { p });
        }
        int found = 0;
        foreach (var blob in blobs)
        {
            var st = blob[0].St;
            int y0 = blob.Min(p => p.C.MinY), y1 = blob.Max(p => p.C.MaxY), x0 = blob.Min(p => p.C.MinX), x1 = blob.Max(p => p.C.MaxX);
            if (y1 - y0 < 1.2 * s || x1 - x0 < 0.7 * s) continue;
            bool dbg = DebugAt.HasValue && DebugAt.Value.X > x0 - 2 * s && DebugAt.Value.X < x1 + 2 * s && Math.Abs(st.Mid - DebugAt.Value.Y) < 4 * s;
            var px = new List<(int X, int Y, int Id)>();
            foreach (var (c, _) in blob)
                for (int y = c.MinY; y <= c.MaxY; y++)
                    for (int x = c.MinX; x <= c.MaxX; x++)
                        if (labels[y * w + x] == c.Id) px.Add((x, y, c.Id));
            // the slant (dx per dy, up to the right: below 0) that stacks the ink in the fewest columns
            double bestK = 0, bestScore = -1;
            int span = (int)(1.8 * (y1 - y0)) + 2;
            var hist = new int[x1 - x0 + span + 1];
            for (double k = -1.8; k <= -0.249; k += 0.05)
            {
                Array.Clear(hist);
                foreach (var (x, y, _) in px) hist[(int)Math.Round(x - x0 - k * (y - y0))]++;
                double score = hist.Sum(v => (double)v * v);
                if (score > bestScore) { bestScore = score; bestK = k; }
            }
            Array.Clear(hist);
            int U(int x, int y) => (int)Math.Round(x - x0 - bestK * (y - y0));
            foreach (var (x, y, _) in px) hist[U(x, y)]++;
            // the strokes: runs of columns each holding a space of ink and more, a stroke's width
            var strokes = new List<(int A, int B, int T, int Bot)>();
            for (int u = 0; u < hist.Length;)
            {
                if (hist[u] < s) { u++; continue; }
                int a = u;
                while (u < hist.Length && hist[u] >= s) u++;
                int b = u - 1;
                // (its ends: the longest run of rows holding its ink, over gaps of a removed line -- a
                // staff line's remains run across every slant's column, above and below the stroke)
                var rowsOn = px.Where(q => U(q.X, q.Y) >= a && U(q.X, q.Y) <= b).Select(q => q.Y).Distinct().Order().ToList();
                int t = rowsOn[0], bot = rowsOn[0], from = rowsOn[0];
                for (int i = 1; i <= rowsOn.Count; i++)
                    if (i == rowsOn.Count || rowsOn[i] - rowsOn[i - 1] > 0.3 * s)
                    {
                        if (rowsOn[i - 1] - from > bot - t) { t = from; bot = rowsOn[i - 1]; }
                        if (i < rowsOn.Count) from = rowsOn[i];
                    }
                var on = px.Where(q => q.Y >= t && q.Y <= bot && U(q.X, q.Y) >= a && U(q.X, q.Y) <= b).ToList();
                // (filled: a stroke is solid along its slant; and as tall as the marks are printed, about the middle line.
                // kinkel19-6's old print leans its thin strokes less, 0.4 and 0.45)
                bool ok = b - a + 1 >= 0.15 * s && b - a + 1 <= 1.2 * s && bot - t >= 1.4 * s && bot - t <= 2.6 * s
                          && on.Count >= 0.6 * (b - a + 1) * (bot - t + 1) && bestK >= -1.6 && bestK <= -0.3;
                if (dbg) Console.WriteLine($"beat repeat? blob x {x0}-{x1} y {y0}-{y1}, slant {bestK:F2}: heap u {a}-{b} ({(b - a + 1) / s:F2} spaces wide) y {t}-{bot} ({(bot - t) / s:F2} tall){(ok ? ", a stroke" : "")}");
                if (ok) strokes.Add((a, b, t, bot));
            }
            // side by side, a space or so apart, their ends level with each other (a quarter rest's two
            // slanting strokes stand one above the other): one mark
            for (int k = 0; k < strokes.Count;)
            {
                int j = k + 1;
                while (j < strokes.Count && (strokes[j].A + strokes[j].B - strokes[j - 1].A - strokes[j - 1].B) / 2.0 <= 1.4 * s
                       && Math.Abs(strokes[j].T - strokes[k].T) <= 0.35 * s && Math.Abs(strokes[j].Bot - strokes[k].Bot) <= 0.35 * s) j++;
                var mark = strokes.Skip(k).Take(j - k).ToList();
                k = j;
                int n = mark.Count;
                if (n < 2 || n > 4) { if (dbg) Console.WriteLine($"beat repeat? {n} strokes side by side: no mark"); continue; }
                var on = px.Where(q => { int u = U(q.X, q.Y); return mark.Any(m => u >= m.A && u <= m.B); }).ToList();
                double l = on.Min(q => q.X), r = on.Max(q => q.X), t = mark.Min(m => m.T), b = mark.Max(m => m.Bot);
                if (Math.Abs((t + b) / 2 - st.Mid) > 0.75 * s) { if (dbg) Console.WriteLine($"beat repeat? x {l}-{r}: not about the middle line"); continue; }
                // (no head or stem among them: a beam's slanting piece between its stems is the notes')
                var among = ps.Heads.FirstOrDefault(h => !h.Grace && !((h.Cross || h.Slash) && h.Score < SureScore) && (h.X > l && h.X < r && h.Y > t - 0.5 * s && h.Y < b + 0.5 * s || h.HasStem && h.StemX > l + 0.2 * s && h.StemX < r - 0.2 * s && Math.Min(h.Y, h.Tip) < b && Math.Max(h.Y, h.Tip) > t));
                if (among != null)
                { if (dbg) Console.WriteLine($"beat repeat? x {l}-{r}: the head at {among.X:F0},{among.Y:F0} among them ({among.Score:F2}{(among.HasStem ? $", stem at {among.StemX:F0} tip {among.Tip:F0}" : "")})"); continue; }
                // (nor a rest the detector sees: an eighth rest's stem leans so, and two may stand close)
                if (ps.Detections != null && ps.Detections.Any(d => d.Class.StartsWith("rest_") && d.Score >= TrustScore && d.X > l && d.X < r && d.Y > t && d.Y < b))
                { if (dbg) Console.WriteLine($"beat repeat? x {l}-{r}: a rest the detector sees"); continue; }
                if (dbg) Console.WriteLine($"beat repeat at x {l}-{r} y {t}-{b}: {n} strokes");
                ps.BeatRepeats.Add(new BeatRepeat(st, l, r, n));
                // (a cross or a slash the detector is unsure of on them is their ink)
                ps.Heads.RemoveAll(h => (h.Cross || h.Slash) && h.Score < SureScore && h.X > l && h.X < r && h.Y > t - 0.5 * s && h.Y < b + 0.5 * s);
                // (the pieces mostly in its strokes are no symbols)
                foreach (var g in on.GroupBy(q => q.Id))
                    if (g.Count() >= 0.5 * px.Count(q => q.Id == g.Key)) ids.Add(g.Key);
                found++;
            }
        }
        if (found > 0) ps.Note = (ps.Note == null ? "" : ps.Note + "; ") + $"beat repeats {found} (at {string.Join(" ", ps.BeatRepeats.Select(b => $"{(b.L + b.R) / 2:F0},{b.St.Mid:F0}"))})";
        return ids;
    }

    /// <summary>
    /// A thin straight stroke leaves the stem's foot (x, y) slanting on away from the tip (dir: +1
    /// down), a space or two long, no wider than a line: the slash of a repeated chord. (A head
    /// missed at the foot is ink across, not a stroke; a cross head has an arm the other way too.)
    /// </summary>
    static bool SlashAt(Img ink, int x, int y, int dir, double s, out (double X, double Y) end, bool dbg = false)
    {
        end = (x, y);
        if (dbg) Console.WriteLine($"  repeat stem foot {x},{y} dir {dir}");
        bool Ink(double px, double py) { int ix = (int)Math.Round(px), iy = (int)Math.Round(py); return ink[ix, iy] || ink[ix - 1, iy] || ink[ix + 1, iy] || ink[ix, iy - 1] || ink[ix, iy + 1]; }
        // how much of the segment from the foot to (ex, ey) is ink
        double Along(double ex, double ey)
        {
            int n = (int)Math.Sqrt((ex - x) * (ex - x) + (ey - y) * (ey - y)), hit = 0;
            for (int k = 1; k <= n; k++) if (Ink(x + (ex - x) * k / n, y + (ey - y) * k / n)) hit++;
            return hit / (double)Math.Max(1, n);
        }
        // (the slash runs two spaces or more, 揺れる想い's about 1.5 across and 1.9 down: a cross
        // head's arm is a space and a half, its other arm crossing it in the middle)
        foreach (int side in new[] { -1, 1 })
            for (double w = 0.8 * s; w <= 2.0 * s; w += 0.1 * s)
                for (double h = 0.6 * s; h <= 2.4 * s; h += 0.1 * s)
                {
                    double ex = x + side * w, ey = y + dir * h, l = Math.Sqrt(w * w + h * h);
                    if (l < 1.9 * s || Along(ex, ey) < 0.9) continue;
                    // thin: white either side of the stroke, across it, a quarter, half and three quarters along
                    double nx = -(ey - y) / l, ny = (ex - x) / l;
                    bool thin = new[] { 0.25, 0.5, 0.75 }.All(f =>
                    {
                        double mx = x + (ex - x) * f, my = y + (ey - y) * f;
                        return !Ink(mx + nx * 0.4 * s, my + ny * 0.4 * s) && !Ink(mx - nx * 0.4 * s, my - ny * 0.4 * s);
                    });
                    if (!thin) continue;
                    // one arm only: no stroke mirrored about the stem
                    if (dbg) Console.WriteLine($"    stroke to {ex:F0},{ey:F0}: mirrored {Along(x - side * w * 0.6, y + dir * h * 0.6):F2}");
                    if (Along(x - side * w * 0.6, y + dir * h * 0.6) > 0.6) return false;
                    // (the stroke found is the shortest that passes: the slash runs on along it while there is ink)
                    double ux = (ex - x) / l, uy = (ey - y) / l;
                    while (Ink(ex + ux, ey + uy) && Math.Abs(ex - x) < 3 * s) { ex += ux; ey += uy; }
                    end = (ex, ey);
                    return true;
                }
        return false;
    }

    /// <summary>
    /// The rests the learnt detector sees where the rules found none (a scan's rest breaks into
    /// pieces that fit no rule's proportions; the toner hollows a half rest's block). The detector
    /// has one class for a whole and a half rest: which side of a line the block's centre stands
    /// tells them apart. The rest's piece of ink is named, and the dots after it are given to it
    /// as to any rest.
    /// </summary>
    static void DetectedRests(PageSymbols ps, double s)
    {
        var staves = ps.Heads.Select(h => h.St).Concat(ps.Rests.Select(r => r.St)).Concat(ps.Others.Select(o => o.St)).Distinct().ToList();
        foreach (var t in ps.Detections.Where(t => t.Score >= TrustScore))
        {
            var st = Nearest(staves, t.Y, s);
            if (st == null || Math.Abs(st.Pos(t.Y)) > 8) continue;
            RestKind kind;
            switch (t.Class)
            {
                case "rest_4": kind = RestKind.Quarter; break;
                case "rest_8": kind = RestKind.Eighth; break;
                case "rest_16": kind = RestKind.Sixteenth; break;
                case "rest_long": kind = RestKind.Whole; break;
                default: continue;
            }
            // (two voices' rests may stand one above the other: Petaluma's dotted eighth rests)
            var ruled = ps.Rests.FirstOrDefault(r => r.St == st && Math.Abs(r.X - t.X) < 1.0 * s && Math.Abs(r.Y - t.Y) < 1.5 * s);
            if (ruled != null)
            {
                // a rules' rest of another kind the detector sees, where it does not see the rules'
                // kind (as DetectedClef), is the detector's: Finale Broadway's quarter rest, a small
                // N, passes the rules' sixteenth rest test (seen at 0.87-0.98 as a quarter)
                if (t.Class != "rest_long" && ruled.Kind is RestKind.Quarter or RestKind.Eighth or RestKind.Sixteenth
                    && ruled.Kind != kind
                    && !ps.Detections.Any(u => u.Class == RestClass(ruled.Kind) && u.Score >= TrustScore && Math.Abs(u.X - ruled.X) < s && Math.Abs(u.Y - ruled.Y) < 1.5 * s))
                {
                    if (DebugAt.HasValue && Math.Abs(t.X - DebugAt.Value.X) < 2 * s && Math.Abs(t.Y - DebugAt.Value.Y) < 3 * s)
                        Console.WriteLine($"detected {t.Class} at {t.X:F0},{t.Y:F0} ({t.Score:F2}) over the rules' {ruled.Kind} rest");
                    ruled.Kind = kind;
                }
                else if (DebugAt.HasValue && Math.Abs(t.X - DebugAt.Value.X) < 2 * s && Math.Abs(t.Y - DebugAt.Value.Y) < 3 * s)
                    Console.WriteLine($"detected {t.Class} at {t.X:F0},{t.Y:F0} ({t.Score:F2}) at the rules' {ruled.Kind} rest at {ruled.X:F0},{ruled.Y:F0}");
                continue;
            }
            var owner = PieceAt(ps, t.X, t.Y, st);
            // (a dot the rules named inside the rest's box is a scrap of the block the toner hollowed:
            // the block is the rest's piece -- gen-scan-fill's basic006, its half rest)
            if (owner is { Class: "dot" })
                owner = ps.Others.Where(o => o.St == st && o.Class == "?" && o.C.W >= 0.8 * s && o.C.MinX <= t.X && t.X <= o.C.MaxX && o.C.MinY <= t.Y && t.Y <= o.C.MaxY)
                                 .OrderBy(o => (long)o.C.W * o.C.H).FirstOrDefault() ?? owner;
            if (owner != null && owner.Class is not ("?" or "dot"))     // named already: a clef's piece, a key's sign
            {
                if (DebugAt.HasValue && Math.Abs(t.X - DebugAt.Value.X) < 2 * s && Math.Abs(t.Y - DebugAt.Value.Y) < 3 * s)
                    Console.WriteLine($"detected {t.Class} at {t.X:F0},{t.Y:F0} ({t.Score:F2}) in a piece named {owner.Class}");
                continue;
            }
            if (t.Class == "rest_long")
            {
                // a block hangs from a line (whole) or sits on one (half). Beyond the staff the piece
                // holds its ledger, wider than the block, at the edge it hangs from or sits on:
                // Fugue 1 sets a moved rest's ledger off the staff's line places (1.5 spaces above
                // it), so no line position tells. In the staff (its lines are not in the ink) the
                // piece's edges tell, as for the rules' rests; else the detector's centre, a quarter
                // space below or above the line (as Verify does)
                bool hangs = false, sits = false;
                if (owner != null && owner.C.W > 2.0 * s)
                {
                    int top = RowInk(ps, owner.C, owner.C.MinY + 1), bottom = RowInk(ps, owner.C, owner.C.MaxY - 1);
                    if (Math.Abs(top - bottom) > 0.3 * s) (hangs, sits) = (top > bottom, bottom > top);
                }
                if (owner != null && hangs == sits) { hangs = OnLine(st, owner.C.MinY, s); sits = OnLine(st, owner.C.MaxY, s); }
                if (hangs == sits) { double p = st.Pos(t.Y); hangs = p - 2 * Math.Round(p / 2) < 0; }
                kind = hangs ? RestKind.Whole : RestKind.Half;
            }
            if (DebugAt.HasValue && Math.Abs(t.X - DebugAt.Value.X) < 2 * s && Math.Abs(t.Y - DebugAt.Value.Y) < 3 * s)
                Console.WriteLine($"detected {t.Class} at {t.X:F0},{t.Y:F0} ({t.Score:F2}), pos {st.Pos(t.Y):F2}{(owner == null ? ", no piece" : $", piece pos {st.Pos(owner.C.MinY):F2}..{st.Pos(owner.C.MaxY):F2} w {owner.C.W / s:F2}")}: added as {kind}");
            if (owner != null) owner.Class = "rest";
            // (a piece far wider than a rest is the rest's block run on into a staff line's remains:
            // its ends are the detector's, else the dot after it stands "inside" the rest -- kinkel19-2's
            // dotted half rests, their pieces 3 to 10 spaces wide)
            bool wide = owner != null && owner.C.W > 2.2 * s;
            double l = owner == null || wide ? t.X - 0.6 * s : owner.C.MinX, r = owner == null || wide ? t.X + 0.6 * s : owner.C.MaxX;
            ps.Rests.Add(new Rest { St = st, X = t.X, Y = t.Y, L = l, R = r, Kind = kind });
        }
    }

    static string RestClass(RestKind k) => k switch
    {
        RestKind.Quarter => "rest_4",
        RestKind.Eighth => "rest_8",
        RestKind.Sixteenth => "rest_16",
        _ => "rest_long",
    };

    /// <summary>The piece's pixels in one row.</summary>
    static int RowInk(PageSymbols ps, Comp c, int y)
    {
        int n = 0;
        for (int x = c.MinX; x <= c.MaxX; x++) if (ps.Labels[y * ps.LabelW + x] == c.Id) n++;
        return n;
    }

    /// <summary>
    /// The learnt classifier names the shape; where it stands decides the rest: an accidental
    /// needs a head just right of it at a height it covers, a rest a place in or near the staff.
    /// A whole and a half rest are one shape: which line they touch tells them apart.
    /// Returns false to leave the piece to the hand-written rules (a block rest, a shape unlike any sample).
    /// </summary>
    static bool ClassifyLearnt(PageSymbols ps, Other o, double s, out string label)
    {
        var c = o.C;
        double dist;
        (label, dist) = GlyphKnn.Classify(GlyphKnn.Features(ps, c, s));
        if (DebugAt.HasValue && Math.Abs(c.Cx - DebugAt.Value.X) < 2 * s && Math.Abs(c.Cy - DebugAt.Value.Y) < 3 * s)
            Console.WriteLine($"knn at {c.Cx:F0},{c.Cy:F0} ({c.W / s:F2} x {c.H / s:F2}): {label} {dist:F2}");
        if (dist > 3.0) return false;
        switch (label)
        {
            case "dots.dot":
                o.Class = "dot";
                return true;
            case "accidentals.sharp" or "accidentals.flat" or "accidentals.natural" or "accidentals.doublesharp" or "accidentals.flatflat":
            {
                // the glyph says where its note stands: a sharp, natural or double sharp at its
                // middle, a flat in its bowl (a quarter of its height up from the bottom). In a
                // chord the signs stand in columns, the farther ones up to a few spaces off.
                double at = label is "accidentals.flat" or "accidentals.flatflat" ? c.MaxY - 0.256 * c.H : (c.MinY + c.MaxY) / 2.0;
                Head best = null;
                double bd = double.MaxValue;
                foreach (var hd in ps.Heads)
                {
                    if (!hd.On(o.St) || hd.Acc != null) continue;
                    double gap = hd.L - c.MaxX, dy = Math.Abs(hd.Y - at);
                    if (gap < -0.15 * s || gap > 2.6 * s || dy > 0.3 * s) continue;
                    // the nearest in x of the heads at the sign's height
                    if (gap < bd) { bd = gap; best = hd; }
                }
                if (best == null) { o.Class = "?"; return true; }
                (o.Class, best.Acc) = label switch
                {
                    "accidentals.sharp" => ("sharp", 1), "accidentals.flat" => ("flat", -1), "accidentals.natural" => ("natural", 0),
                    "accidentals.doublesharp" => ("dsharp", 2), _ => ("dflat", -2),
                };
                o.For = best;
                return true;
            }
            case "rests.2" or "rests.3" or "rests.4":
            {
                double pos = o.St.Pos(c.MinY + c.H / 2.0);
                if (pos < -12 || pos > 12) { o.Class = "?"; return true; }
                var kind = label == "rests.2" ? RestKind.Quarter : label == "rests.3" ? RestKind.Eighth : RestKind.Sixteenth;
                // (a grace note, its small head at the foot, is no rest)
                if (kind != RestKind.Quarter && HeadAtFoot(ps, c, s)) { o.Class = "?"; return true; }
                o.Class = "rest";
                ps.Rests.Add(new Rest { St = o.St, X = c.Cx, Y = c.MinY + c.H / 2.0, L = c.MinX, R = c.MaxX, Kind = kind });
                return true;
            }
            case "rests.0" or "rests.1":
                return false;
            default:
                // not a symbol read here (a scrap, a slur, text): the rules may still know the shape
                // from another font
                return false;
        }
    }

    /// <summary>
    /// The accidentals the learnt detector is sure of, given to the heads the rules left without
    /// one (on a scan the classifier and the rules both fail on the sign's shape, while the
    /// detector finds every one). Where the sign stands decides, as in ClassifyLearnt: the note
    /// is at a sharp's middle, in a flat's bowl; the nearest head to the right at that height.
    /// Right to left, so the sign next to a note claims it first. Runs after the rules: put
    /// before them it took a few signs from the wrong head on clean pages (head.acc 1-5 a suite).
    /// </summary>
    static void DetectedAccidentals(PageSymbols ps, double s)
    {
        foreach (var t in ps.Detections.Where(t => t.Score >= TrustScore).OrderByDescending(t => t.X))
        {
            int alt;
            double halfW;
            switch (t.Class)
            {
                case "sharp": alt = 1; halfW = 0.5 * s; break;
                case "flat": alt = -1; halfW = 0.45 * s; break;
                case "natural": alt = 0; halfW = 0.33 * s; break;
                case "dsharp": alt = 2; halfW = 0.5 * s; break;
                case "dflat": alt = -2; halfW = 0.85 * s; break;
                default: continue;
            }
            // (a sign the rules already named, a key signature's, is not a note's)
            var owner = PieceAt(ps, t.X, t.Y);
            // (but a rules' rest the detector is sure is a sign, and sees no rest in: Petaluma's flat,
            // its bowl cut short by the staff line under it, is a quarter rest's size and stands too
            // high for its head -- ties005, <aes bes>8 after a double flat)
            // (a flat's box centre is about 0.6 space above its bowl, the box 2.4 spaces tall)
            double at = alt < 0 ? t.Y + 0.6 * s : t.Y, right = t.X + halfW;
            // (a grace note's flat is drawn small, about 0.7 of a note's: its bowl nearer its box's
            // centre, so a grace head is measured from there -- basic003's des,8 grace, its flat's
            // bowl a third of a space above the 0.6 estimate, given to the c,16 a step below)
            double atGrace = alt < 0 ? t.Y + 0.42 * s : t.Y;
            double AtOf(Head hd) => ps.Graces.Contains(hd) ? atGrace : at;
            var st = Nearest(ps.Heads.Select(h => h.St).Distinct().ToList(), t.Y, s);
            bool restSeen = owner != null && ps.Detections.Any(u => u.Class.StartsWith("rest_") && u.Score >= TrustScore && owner.C.MinX <= u.X && u.X <= owner.C.MaxX && owner.C.MinY <= u.Y && u.Y <= owner.C.MaxY);
            // (or a grace note's small sign: a rest to the rules, or a sign named by the classifier
            // that no note claimed (the grace head is no note of theirs), with the grace head nearer
            // right of it than any note -- chords005's and chords010's naturals, seen at 0.8, one of
            // them a flat to the classifier)
            bool graceSign = owner != null && !restSeen && owner.Class is "rest" or "sharp" or "flat" or "natural" or "dsharp" or "dflat"
                             && ps.Graces.Any(g => g.On(st) && g.L - right >= -0.15 * s && g.L - right <= 1.5 * s && Math.Abs(g.Y - at) < 0.5 * s
                                                   && !ps.Heads.Any(h => h.On(st) && h.L - right >= -0.15 * s && h.L < g.L && Math.Abs(h.Y - at) < 0.5 * s));
            bool overRest = owner != null && owner.Class == "rest" && !restSeen && (t.Score >= SureScore || graceSign);
            if (owner != null && owner.Class != "?" && !overRest && !graceSign)
            {
                if (DebugAt.HasValue && Math.Abs(t.Y - DebugAt.Value.Y) < 3 * s)
                    Console.WriteLine($"detected {t.Class} at {t.X:F0},{t.Y:F0} ({t.Score:F2}): owner {owner.Class}, passed over");
                continue;
            }
            Head best = null;
            double bd = double.MaxValue;
            // (a grace note's sign, small, is the sign it is: the grace note's head is the nearer one right of it, S21)
            foreach (var hd in ps.Heads.Concat(ps.Graces))
            {
                if (!hd.On(st) || hd.Acc != null) continue;
                double gap = hd.L - right, dy = Math.Abs(hd.Y - AtOf(hd));
                if (gap < -0.15 * s || gap > 2.6 * s || dy > 0.3 * s) continue;
                if (gap < bd) { bd = gap; best = hd; }
            }
            // (none at its height: an old print's flat is short, its bowl nearer its box's centre as a grace
            // note's is -- kinkel19-1's scan, the bass's <des,, des,>2, the upper flat's bowl 0.46 space above
            // the estimate, the head left d,)
            if (best == null && alt < 0 && ps.Scan)
                foreach (var hd in ps.Heads)
                {
                    if (!hd.On(st) || hd.Acc != null) continue;
                    double gap = hd.L - right, dy = Math.Abs(hd.Y - atGrace);
                    if (gap < -0.15 * s || gap > 2.6 * s || dy > 0.3 * s) continue;
                    if (gap < bd) { bd = gap; best = hd; }
                }
            // (a head a step off the sign's height and a head at it, the latter moved aside by a
            // second: the one at its height, up to a head's width further -- MuseJazz's tall sharp,
            // its centre between the upper voice's b and the lower voice's ais beside it, piano004)
            // (but not past a grace head at the sign's height: the sign before a grace note is its,
            // not the note's after it -- chords008's ees16 on the scan, its flat given to the head after)
            if (best != null)
                best = ps.Heads.Concat(ps.Graces).Where(hd => hd.On(st) && hd.Acc == null && hd.L - right >= -0.15 * s && hd.L - right <= bd + 1.5 * s && Math.Abs(hd.Y - AtOf(hd)) <= 0.3 * s
                                                              && !ps.Graces.Any(g => g != hd && g.On(st) && g.L >= right - 0.15 * s && g.L < hd.L && Math.Abs(g.Y - at) <= 0.5 * s))
                               .OrderBy(hd => Math.Abs(hd.Y - AtOf(hd))).FirstOrDefault() ?? best;
            if (DebugAt.HasValue && Math.Abs(t.Y - DebugAt.Value.Y) < 3 * s)
                Console.WriteLine($"detected {t.Class} at {t.X:F0},{t.Y:F0} ({t.Score:F2}): owner {(owner == null ? "none" : owner.Class)}, head {(best == null ? "none" : $"{best.X:F0},{best.Y:F0}")}");
            if (best == null) continue;
            best.Acc = alt;
            if (owner?.Class == "rest") ps.Rests.RemoveAll(r => r.St == owner.St && r.L == owner.C.MinX && r.R == owner.C.MaxX);
            // the sign's own piece of ink is named, so that no rule takes it for a rest
            if (owner != null) owner.Class = t.Class;
        }
    }

    /// <summary>
    /// The naturals the detector sees under its threshold (Detector.WeakNatural), on a scan only: an old
    /// print's tall natural by the bass's chord, seen at 0.34-0.40 (kinkel19-2's scan, bars 30 and 31, the
    /// left hand's a, and e, read in the key's flats). Taken where a head with no sign stands just right
    /// of it at its middle (nearer than DetectedAccidentals looks: a space at most), on a piece of ink no
    /// rule named, and where the detector sees nothing else surely at the spot.
    /// </summary>
    static void WeakNaturals(PageSymbols ps, double s)
    {
        if (!ps.Scan) return;
        foreach (var t in ps.WeakNaturals.OrderByDescending(t => t.X))
        {
            var owner = PieceAt(ps, t.X, t.Y);
            var other = ps.Detections.FirstOrDefault(u => u.Score >= Detector.Threshold && !u.Class.StartsWith("stem") && !u.Class.StartsWith("beam") && u.Class != "barline"
                                                          && Math.Abs(u.X - t.X) < 0.5 * s && Math.Abs(u.Y - t.Y) < 0.8 * s);
            var st = Nearest(ps.Heads.Select(h => h.St).Distinct().ToList(), t.Y, s);
            double right = t.X + 0.33 * s;
            var best = ps.Heads.Where(hd => hd.On(st) && hd.Acc == null && hd.L - right >= -0.15 * s && hd.L - right <= 1.0 * s && Math.Abs(hd.Y - t.Y) <= 0.3 * s)
                               .OrderBy(hd => hd.L).FirstOrDefault();
            if (DebugAt.HasValue && Math.Abs(t.Y - DebugAt.Value.Y) < 3 * s)
                Console.WriteLine($"weak natural at {t.X:F0},{t.Y:F0} ({t.Score:F2}): owner {(owner == null ? "none" : owner.Class + (owner.For == null ? "" : $" for {owner.For.X:F0},{owner.For.Y:F0}"))}, other {other?.Class ?? "none"}, head {(best == null ? "none" : $"{best.X:F0},{best.Y:F0}")}");
            if (best == null || other != null) continue;
            // (a natural the rules named by a chord and gave to the head nearest its piece's edge, a third
            // off its middle: the head at its middle has it -- bar 30's <f, a, c>, the f, given it)
            if (owner?.Class == "natural" && owner.For != null && owner.For != best && owner.For.Acc == 0 && Math.Abs(owner.For.Y - t.Y) >= 0.6 * s)
            {
                owner.For.Acc = null;
                owner.For = best;
                best.Acc = 0;
                if (DebugAt.HasValue && Math.Abs(t.Y - DebugAt.Value.Y) < 3 * s) Console.WriteLine($"  moved to {best.X:F0},{best.Y:F0}");
                continue;
            }
            if (owner != null && owner.Class != "?") continue;
            best.Acc = 0;
            if (owner != null) owner.Class = "natural";
        }
    }

    /// <summary>
    /// A double flat drawn as two flats side by side (Finale Maestro's, Finale Broadway's: two
    /// pieces of ink, where Emmentaler's is one): the flat next to the head is given to it, and
    /// its twin, of its size at its height just left of it, is left with no head of its own.
    /// The twin makes it a double flat.
    /// </summary>
    static void TwinFlats(PageSymbols ps, double s)
    {
        foreach (var h in ps.Heads.Where(h => h.Acc == -1))
        {
            // the flat given to it: right before it, its bowl at the head's height
            var flat = ps.Others.Where(o => o.Class == "flat" && o.St == h.St && o.C.MaxX < h.L + 0.15 * s && h.L - o.C.MaxX < 2.6 * s
                                            && Math.Abs(o.C.MaxY - 0.256 * o.C.H - h.Y) < 0.4 * s)
                                .OrderByDescending(o => o.C.MaxX).FirstOrDefault();
            if (flat == null) continue;
            var f = flat.C;
            var twin = ps.Others.FirstOrDefault(o => o != flat && o.St == h.St && o.Class is ("?" or "flat") && o.C.MaxX <= f.MinX + 0.1 * s && f.MinX - o.C.MaxX < 0.4 * s
                                                     && Math.Abs(o.C.MinY - f.MinY) < 0.25 * s && Math.Abs(o.C.MaxY - f.MaxY) < 0.25 * s
                                                     && Math.Abs(o.C.W - f.W) < 0.3 * f.W);
            // (a flat of its own: another head's at its height, further left)
            if (twin == null || (twin.Class == "flat" && ps.Heads.Any(o => o != h && o.Acc != null && o.St == h.St && o.L > twin.C.MaxX - 0.15 * s && o.L < f.MinX && Math.Abs(o.Y - h.Y) < 0.4 * s))) continue;
            h.Acc = -2;
            flat.Class = twin.Class = "dflat";
        }
    }

    /// <summary>A dot to give to a head or rest: the piece of ink the rules named, or a detected dot.</summary>
    sealed class DotAt
    {
        public Other O;         // null for a detected dot with no free piece of ink of its own
        public Staff St;
        public double X, Y;
        public bool Unseen;     // a rule's dot kept with the detector seeing nothing on it
    }

    /// <summary>
    /// The detector's weak dots (Detector.WeakDot) that stand in the column of a dot it sees
    /// surely, a space to three above or below: Lily# lines up the dots of a chord and of the
    /// other voice, and one pushed out by the other voice's second is seen less surely
    /// (piano006's lower voice's gis8., at 0.31 under the chord's two at 0.77 and 0.89).
    /// </summary>
    public static List<Detection> ColumnDots(PageSymbols ps, double s) =>
        ps.WeakDots.Where(w => ps.Detections != null && ps.Detections.Any(t => t.Class == "dot" && t.Score >= TrustScore
            && Math.Abs(t.X - w.X) < 0.3 * s && Math.Abs(t.Y - w.Y) > 0.7 * s && Math.Abs(t.Y - w.Y) < 3.2 * s)).ToList();

    /// <summary>
    /// The dots the learnt detector sees, in place of the rules' guess from a small round piece
    /// alone: the short tie between a chord's inner heads, a stub of a tie or a rest broken on a
    /// scan pass the rules' test and gave whole chords a dot (the ties suite), while the detector
    /// sees no dot there and a tie's ends. A rule's dot is kept when the detector sees a dot on it
    /// (the rules' piece holds the better centre), or nothing at all on a round one (below); a
    /// detected dot the rules did not find is added
    /// (a dot merged with a line, or thinned by the toner), with its piece of ink named.
    /// </summary>
    static List<DotAt> DetectedDots(PageSymbols ps, double s, List<DotAt> rules)
    {
        // (a dot inside a double sharp the detector is surer of is one of its four lobes: Gonville's,
        // in pieces, the lower left seen as a dot at 0.64 and given to the note before -- piano007)
        var crosses = ps.Detections.Where(t => t.Class == "dsharp" && t.Score >= TrustScore).ToList();
        bool InCross(double x, double y, double score) => crosses.Any(t => t.Score > score && Math.Abs(t.X - x) < 0.6 * s && Math.Abs(t.Y - y) < 0.6 * s);
        // (nor is a dot one where the detector is surer of a staccato or a staccatissimo: S15; or a portato,
        // whose dot stands 0.4 space from its centre)
        var staccatos = ps.Detections.Where(t => t.Class is "art_staccato" or "art_staccatissimo" or "art_portato" && t.Score > Detector.Threshold).ToList();
        bool Staccato(double x, double y, double score) => staccatos.Any(t => t.Score > score && Math.Abs(t.X - x) < 0.4 * s && Math.Abs(t.Y - y) < 0.5 * s);
        var seen = ps.Detections.Where(t => t.Class == "dot" && t.Score >= TrustScore).Concat(ColumnDots(ps, s)).Where(t => !InCross(t.X, t.Y, t.Score) && !Staccato(t.X, t.Y, t.Score)).ToList();
        rules = rules.Where(d => !InCross(d.X, d.Y, seen.Where(t => Math.Abs(t.X - d.X) < 0.4 * s && Math.Abs(t.Y - d.Y) < 0.4 * s).Select(t => t.Score).DefaultIfEmpty(0).Max())).ToList();
        bool Near(double x, double y, Detection t) => Math.Abs(t.X - x) < 0.4 * s && Math.Abs(t.Y - y) < 0.4 * s;
        // A rule's dot the detector does not see is kept when the detector sees nothing at all on
        // it and the piece is a clean round dot: another font's dot the detector does not know
        // (Bohemian's Leland dots, 0.4 space), or one it missed (voices2008's on fill, a ring the
        // toner hollowed; piano006's, pushed out by the other voice's second, it sees weakly: ColumnDots). The false ones have a tie's end or a head on them, or are flat,
        // thin or small (a tie's stub, a scrap of a head the toner thinned: 0.2-0.3 space).
        bool Round(Comp c) => c.W >= 0.35 * s && c.H >= 0.35 * s && Math.Abs(c.W - c.H) <= 0.12 * s && c.Fill >= 0.5;
        bool Unseen(DotAt d) => !ps.Detections.Any(t => Math.Abs(t.X - d.X) < 0.7 * s && Math.Abs(t.Y - d.Y) < 0.7 * s);
        // (or a clean round one the detector sees weakly as a dot, at 0.45 and more, and nothing else there more
        // surely: lang36-3's scan, the dot tucked under the right end of a head on a ledger, ees,,4., at 0.497
        // beside the stem's tip. Not a speck: kinkel7-1's scan, its specks beside the heads seen as dots, five
        // dots in a bar)
        bool Weakly(DotAt d) => ps.WeakDots.Where(w => Near(d.X, d.Y, w)).Select(w => w.Score).DefaultIfEmpty(0).Max() is double w && w >= 0.45
                                && !ps.Detections.Any(t => t.Class != "dot" && Near(d.X, d.Y, t) && t.Score > w);
        var dots = rules.Where(d => seen.Any(t => Near(d.X, d.Y, t)) || Round(d.O.C) && (Unseen(d) || Weakly(d))).ToList();
        foreach (var d in dots) d.Unseen = !seen.Any(t => Near(d.X, d.Y, t));
        var staves = ps.Heads.Select(h => h.St).Concat(ps.Rests.Select(r => r.St)).Distinct().ToList();
        foreach (var t in seen)
        {
            if (dots.Any(d => Near(d.X, d.Y, t))) continue;
            var owner = PieceAt(ps, t.X, t.Y);
            if (owner != null && owner.Class is not ("?" or "dot")) continue;    // named already: a rest's, a clef's piece
            if (owner != null && (owner.C.W > 0.8 * s || owner.C.H > 0.8 * s)) owner = null;   // a larger piece the dot touches (a tie, a line's stub)
            var st = owner?.St ?? Nearest(staves, t.Y, s);
            if (st == null) continue;
            // (with no piece of its own, a dot centred on a line or a ledger line's place is the line's
            // end or a speck on it: a dot stands in a space -- kinkel19-1's scan, bar 8, the end of the
            // ledger line under c,2 seen as a dot at 0.53, its half note dotted)
            if (owner == null && OnLine(st, t.Y, s))
            {
                if (DebugAt.HasValue && Math.Abs(t.X - DebugAt.Value.X) < 2 * s && Math.Abs(t.Y - DebugAt.Value.Y) < 3 * s)
                    Console.WriteLine($"detected dot at {t.X:F0},{t.Y:F0} ({t.Score:F2}): on a line, dropped");
                continue;
            }
            if (DebugAt.HasValue && Math.Abs(t.X - DebugAt.Value.X) < 2 * s && Math.Abs(t.Y - DebugAt.Value.Y) < 3 * s)
                Console.WriteLine($"detected dot at {t.X:F0},{t.Y:F0} ({t.Score:F2}): added");
            if (owner != null) owner.Class = "dot";
            dots.Add(new DotAt { O = owner, St = st, X = t.X, Y = t.Y });
        }
        if (DebugAt.HasValue)
            foreach (var d in rules.Except(dots).Where(d => Math.Abs(d.X - DebugAt.Value.X) < 2 * s && Math.Abs(d.Y - DebugAt.Value.Y) < 3 * s))
                Console.WriteLine($"rules' dot at {d.X:F0},{d.Y:F0}: no detected dot there, dropped");
        return dots;
    }

    /// <summary>
    /// With the learnt detector: a rules' rest well off the staff (past its second ledger line's
    /// place) that the detector sees no rest on is none. A lyric's letter below a vocal staff (a y,
    /// 1.3 by 1.9 spaces, pos -9) passes the rules' eighth rest test; a voice's rest moved off the
    /// staff the detector sees. Inside the staff the rules stand as before.
    /// </summary>
    static void DropUnseenRests(PageSymbols ps, double s)
    {
        var seen = ps.Detections.Where(t => t.Class.StartsWith("rest_") && t.Score >= TrustScore).ToList();
        foreach (var r in ps.Rests.ToList())
        {
            // (on a one-line staff, imagined five lines wide, the words under the voice's staff above
            // stand "inside": only a rest about the line itself stands without the detector)
            if (Math.Abs(r.St.Pos(r.Y)) <= (r.St.OneLine ? 3 : 7)) continue;
            if (seen.Any(t => Math.Abs(t.X - r.X) < s && Math.Abs(t.Y - r.Y) < 1.5 * s)) continue;
            ps.Rests.Remove(r);
            var o = ps.Others.FirstOrDefault(o => o.Class == "rest" && o.St == r.St && o.C.MinX == r.L && o.C.MaxX == r.R);
            if (o != null) o.Class = "?";
        }
    }

    /// <summary>
    /// A piece off the staff that stands in a row of text: two or more other unnamed pieces of a
    /// letter's size beside it (within four spaces), across its middle height, on its side of the
    /// staff. A lyric's letters under a vocal staff (an h, an n, a y) pass for eighth rests, a
    /// space or two below the staff where the song's notes are low; a voice's rest moved aside
    /// has heads and stems about it, not a row of letters.
    /// </summary>
    static bool InTextRow(PageSymbols ps, Other o, double s)
    {
        var c = o.C;
        double mid = c.MinY + c.H / 2.0;
        int n = 0;
        foreach (var q in ps.Others)
        {
            if (q == o || q.St != o.St || q.Class != "?") continue;
            var d = q.C;
            if (d.H < 0.5 * s || d.H > 2.6 * s || d.W > 2.5 * s) continue;
            if (mid < d.MinY || mid > d.MaxY) continue;
            if (d.MinX > c.MaxX + 4 * s || d.MaxX < c.MinX - 4 * s) continue;
            if (++n >= 2) return true;
        }
        return false;
    }

    /// <summary>
    /// A piece that ends at the bottom in a head, not in a stroke: a grace note (a small head on
    /// a stem with its flag, an acciaccatura's slash through it) is one piece of ink of an eighth
    /// or sixteenth rest's size, but its bottom fifth holds a run of ink half a space wide, where a
    /// rest's slanted stroke ends in a third of that.
    /// </summary>
    static bool HeadAtFoot(PageSymbols ps, Comp c, double s)
    {
        int widest = 0;
        for (int y = c.MaxY - c.H / 5; y <= c.MaxY; y++)
        {
            int run = 0;
            for (int x = c.MinX; x <= c.MaxX; x++)
                if (ps.Labels[y * ps.LabelW + x] == c.Id) { if (++run > widest) widest = run; }
                else run = 0;
        }
        return widest >= 0.5 * s;
    }
}
