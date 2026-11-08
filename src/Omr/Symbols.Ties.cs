namespace Omr;

static partial class Symbols
{
    /// <summary>
    /// A tie is a thin arc from the edge of one head to the edge of the next at the same staff
    /// position (across a barline, too). At a system's end it runs on to the margin and its note
    /// is tied into the next system; at a system's start the hanging half belongs to no note here.
    /// </summary>
    static void FindTies(PageSymbols ps, Img d, Img ink, double s)
    {
        // 1. arcs among the loose ink: thin, wide, sparse pieces, and their ends' heights
        var arcs = new List<(int X0, int X1, double Yl, double Yr, Staff St, List<Other> Parts)>();
        foreach (var o in ps.Others)
        {
            if (o.Class != "?") continue;
            var c = o.C;
            // (between two chords a beat apart a tie is short; a hanging half-tie at a system's end shorter;
            // a staff line cuts one that lies along it into two short ends)
            if (c.W < 0.5 * s || c.W > 12 * s || c.H < 0.1 * s || c.H > 1.3 * s || c.W < 1.2 * c.H || c.Fill > 0.5) continue;
            double End(int x)
            {
                double sum = 0; int n = 0;
                for (int y = c.MinY; y <= c.MaxY; y++) if (ps.Labels[y * ps.LabelW + x] == c.Id) { sum += y; n++; }
                return n == 0 ? c.Cy : sum / n;
            }
            arcs.Add((c.MinX, c.MaxX, End(c.MinX), End(c.MaxX), o.St, new List<Other> { o }));
        }
        // a staff line took the middle of an arc lying along it: the two ends left meet at the line's
        // height, a few spaces apart
        for (bool merged = true; merged;)
        {
            merged = false;
            for (int i = 0; i < arcs.Count && !merged; i++)
                for (int j = 0; j < arcs.Count && !merged; j++)
                {
                    var (p, q) = (arcs[i], arcs[j]);
                    if (i == j || p.St != q.St || q.X0 <= p.X1 || q.X0 - p.X1 > 4 * s) continue;
                    if (Math.Abs(p.Yr - q.Yl) > 0.25 * s || !OnLine(p.St, p.Yr, s)) continue;
                    // (two ties in a row along one line meet at a head; a cut one has nothing between its ends)
                    if (ps.Heads.Any(h => h.St == p.St && h.X > p.X1 && h.X < q.X0 && Math.Abs(h.Y - p.Yr) < 1.2 * s)) continue;
                    arcs[i] = (p.X0, q.X1, p.Yl, q.Yr, p.St, p.Parts.Concat(q.Parts).ToList());
                    arcs.RemoveAt(j);
                    merged = true;
                }
        }
        // 2. each arc ties the head at its left end to the one at its right end (at the same position),
        // or to the next system when it runs on to the margin
        foreach (var arc in arcs)
        {
            var st = arc.St;
            if (DebugAt.HasValue && Math.Abs((arc.X0 + arc.X1) / 2.0 - DebugAt.Value.X) < 4 * s && Math.Abs(arc.Yl - DebugAt.Value.Y) < 3 * s)
                Console.WriteLine($"tie? arc {arc.X0}-{arc.X1} ends y {arc.Yl:F0} {arc.Yr:F0} ({arc.Parts.Count} pieces)");
            Head Near(double x, double y, int side) => ps.Heads
                .Where(h => h.St == st && Math.Abs(h.Y - y) < 1.3 * s && (side < 0 ? x - h.X > -0.5 * s && x - h.X < 1.3 * s : h.X - x > -0.5 * s && h.X - x < 1.3 * s))
                .OrderBy(h => Math.Abs(h.Y - y) + 0.3 * Math.Abs(h.X - x)).FirstOrDefault();
            var a = Near(arc.X0, arc.Yl, -1);
            var b = Near(arc.X1, arc.Yr, +1);
            bool toMargin = arc.X1 > st.Right - 1.0 * s, fromMargin = arc.X0 < st.Left + 1.0 * s;
            if (a != null && (b != null && b.Pos == a.Pos || toMargin)) { a.Tie = true; foreach (var o in arc.Parts) o.Class = "tie"; }
            else if (fromMargin && b != null) foreach (var o in arc.Parts) o.Class = "tie";
        }
        // 3. a tie that touches its head (a hollow head's rim, a short tie between close chords) is
        // no loose piece. From the head's right edge one thin stroke runs, column by column, to the
        // next head at its position (or, at a system's end, out towards the margin): followed on the
        // page as printed (the ink with the lines taken out loses a tie's thin ends at a ledger's
        // height), leaving out the staff's own line rows. A barline or a stem it crosses is passed.
        foreach (var h in ps.Heads.Where(h => !h.Tie))
        {
            var st = h.St;
            int xr = (int)h.R, y0 = (int)(h.Y - 1.6 * s), y1 = (int)(h.Y + 1.6 * s);
            var lineRows = new HashSet<int>();
            foreach (var ln in st.Lines) for (int yy = ln.Top - 1; yy <= ln.Bot + 1; yy++) lineRows.Add(yy);
            bool Dark(int x, int y) => d[x, y] && !lineRows.Contains(y);
            // the strokes a column holds within the window: runs of dark rows
            List<(int T, int B)> Runs(int x)
            {
                var r = new List<(int, int)>();
                for (int y = y0; y <= y1; y++)
                {
                    if (!Dark(x, y)) continue;
                    int t = y;
                    while (y + 1 <= y1 && Dark(x, y + 1)) y++;
                    r.Add((t, y));
                }
                return r;
            }
            var b = ps.Heads.Where(o => o != h && o.St == st && o.Pos == h.Pos && o.L - xr > 0.5 * s && o.L - xr < 10 * s)
                            .OrderBy(o => o.X).FirstOrDefault();
            bool last = !ps.Heads.Any(o => o != h && o.St == st && o.X > h.X + 0.3 * s);
            // (a hanging half-tie at the margin starts at the head; a flag hangs from the stem's tip)
            double startNear = b != null ? 1.6 * s : 0.9 * s;
            int goal = b != null ? (int)b.L : last && st.Right - xr < 3 * s ? (int)st.Right : -1;
            if (goal < 0) continue;
            // a note with an accidental is never tied to: a sign right before the next head (a tall
            // stroke on the page, shorter than a barline) ends whatever runs there
            if (b != null)
            {
                bool sign = false;
                for (int x = (int)(b.L - 0.9 * s); x <= (int)(b.L - 0.15 * s) && !sign; x++)
                {
                    // (over a tall window, so that a barline shows its full height and is not taken for a sign)
                    int run = 0, best = 0;
                    for (int yy = Math.Max(1, (int)(b.Y - 3.2 * s)); yy <= Math.Min(d.H - 2, (int)(b.Y + 3.2 * s)); yy++)
                    {
                        if (d[x, yy]) { run++; best = Math.Max(best, run); }
                        else run = 0;
                    }
                    sign = best >= 1.5 * s && best <= 3.5 * s;
                }
                if (sign) continue;
            }
            int thinMax = (int)(0.3 * s);
            // the stroke's height on the page with the staff's line rows in: a beam along a staff line is a thin
            // edge either side of it with the line's rows left out (gen-threes' threes009, a beam of six
            // sixteenths on the top line, its lower edge followed from the top note to the same note three on)
            int Whole(int x, (int T, int B) r)
            {
                int t = r.T, b = r.B;
                while (t > 0 && d[x, t - 1]) t--;
                while (b < d.H - 1 && d[x, b + 1]) b++;
                return b - t + 1;
            }
            bool tied = false;
            string why = "no stroke";
            // the stroke starts just past the head's edge (a short tie between close chords a little
            // later), within a space of the head's height: every thin stroke there is tried, the one
            // nearest the head's height first
            for (int x0 = xr + (int)(0.15 * s); x0 < Math.Min(xr + 0.8 * s, goal) && !tied; x0++)
                // (a stroke at the head's own height is a ledger line running on to the next head, not a tie)
                foreach (var start in Runs(x0).Where(r => r.B - r.T + 1 <= thinMax && Math.Abs((r.T + r.B) / 2.0 - h.Y) <= startNear
                                                          && Math.Abs((r.T + r.B) / 2.0 - h.Y) >= 0.3 * s && Whole(x0, r) <= 0.45 * s)
                                              .OrderBy(r => Math.Abs((r.T + r.B) / 2.0 - h.Y)))
                {
                    var cur = start;
                    int reach = x0, thick = 0;
                    bool clipped = false;
                    var widths = new List<int> { start.B - start.T + 1 };
                    for (int x = x0 + 1; x < goal; x++)
                    {
                        // the run that carries on from the last one (rows overlapping, a hair of play)
                        var next = Runs(x).Where(r => r.B >= cur.T - 2 && r.T <= cur.B + 2).OrderBy(r => r.B - r.T).Cast<(int T, int B)?>().FirstOrDefault();
                        if (next == null) break;
                        int w = next.Value.B - next.Value.T + 1;
                        if (w > thinMax) { if (++thick > 0.35 * s) break; continue; }    // a barline or a stem: pass
                        cur = next.Value; reach = x; widths.Add(w);
                        // a stroke cut by the window's edge is something beyond it (a beam) seen in part
                        if (cur.T <= y0 || cur.B >= y1) { clipped = true; break; }
                    }
                    // even along its length: a beam's edge is a wedge that grows, a tie keeps its thickness
                    int n = widths.Count;
                    double wa = widths.Take(Math.Max(1, n / 4)).Average(), wb = widths.Skip(n - Math.Max(1, n / 4)).Average();
                    bool even = Math.Abs(wa - wb) <= 0.1 * s;
                    tied = !clipped && reach - x0 >= 0.4 * s && goal - reach <= 0.45 * s && even;
                    why = $"from {x0} rows {start.T}-{start.B} reach {reach} of {goal}, widths {wa:F1}..{wb:F1}{(clipped ? ", clipped" : "")}";
                    if (tied) break;
                }
            if (DebugAt.HasValue && Math.Abs(h.X - DebugAt.Value.X) < 3 * s && Math.Abs(h.Y - DebugAt.Value.Y) < 3 * s)
            {
                Console.WriteLine($"trace tie? head at {h.X:F0},{h.Y:F0} pos {h.Pos}: next {(b != null ? b.X.ToString("F0") : "margin")}: {why} -> {tied}");
                Console.WriteLine("    columns from R (rows rel. to head): " + string.Join(" | ", Enumerable.Range(0, (goal - xr) / 4 + 1)
                    .Select(k => string.Join(",", Runs(xr + 4 * k).Select(r => $"{r.T - (int)h.Y}..{r.B - (int)h.Y}")))));
            }
            if (tied) h.Tie = true;
        }
    }

    /// <summary>
    /// The ties the learnt detector saw, given to the heads the rules left untied. The detector
    /// marks a tie's two ends (tie_l just right of the head it starts from, tie_r just left of the
    /// head it ends on): a head with a tie_l at it is tied when the next head at its position on
    /// the staff has a tie_r at it, or, when it is the staff's last head with none at its position
    /// after it, the tie runs on into the next system. The two ends on the two heads are the
    /// structure the rules' stroke-following asked of the ink (one stroke from head to head), read
    /// from the learnt shape instead: on a scan the stroke breaks, the ends still look like ends.
    /// </summary>
    static void DetectedTies(PageSymbols ps, Img d, double s)
    {
        var lefts = ps.Detections.Where(t => t.Class == "tie_l" && t.Score >= TrustScore).ToList();
        var rights = ps.Detections.Where(t => t.Class == "tie_r" && t.Score >= TrustScore).ToList();
        // The head each end stands at. Lily# starts a tie 0.2-1.8 spaces right of its head's centre
        // (past a dot), within a space and a quarter of its height, and ends it as far left of the
        // next. A chord's ties stack, one to a head, and an inner one lies between two heads, nearer
        // to the wrong one as often as not (ties001's middle tie of three: a third of a space under
        // the top head, two thirds over its own). So each end goes to the nearest head first, and
        // then the ends that fell on one chord are dealt out again to its heads, one each, in order
        // of height, the deal that keeps them nearest their heads overall.
        var chordOf = new Dictionary<Head, List<Head>>();
        foreach (var h in ps.Heads)
        {
            if (chordOf.ContainsKey(h)) continue;
            // (the chord: the heads on the stem, or at one x when there is none)
            var chord = ps.Heads.Where(o => o.St == h.St && (h.HasStem ? o.HasStem && o.Up == h.Up && Math.Abs(o.StemX - h.StemX) < 0.3 * s : !o.HasStem && Math.Abs(o.X - h.X) < 0.5 * s))
                                .OrderBy(o => o.Y).ToList();
            foreach (var o in chord) chordOf[o] = chord;
        }
        Dictionary<Detection, Head> Assign(List<Detection> ends, int side)
        {
            var map = new Dictionary<Detection, Head>();
            foreach (var t in ends)
            {
                var h = ps.Heads.Where(h => Math.Abs(h.Y - t.Y) <= 1.25 * s
                                        && (side < 0 ? t.X - h.X > -0.3 * s && t.X - h.X < 2.0 * s : h.X - t.X > -0.3 * s && h.X - t.X < 2.0 * s))
                                .OrderBy(h => Math.Abs(h.Y - t.Y) + 0.3 * Math.Abs(h.X - t.X)).FirstOrDefault();
                if (h != null) map[t] = h;
            }
            foreach (var g in map.GroupBy(kv => chordOf[kv.Value]).ToList())
            {
                var chord = g.Key;
                var onIt = g.Select(kv => kv.Key).OrderBy(t => t.Y).ToList();
                if (onIt.Count < 2) continue;
                var deal = Deal(onIt.Select(t => t.Y).ToList(), chord.Select(h => h.Y).ToList());
                if (deal != null) for (int i = 0; i < onIt.Count; i++) map[onIt[i]] = chord[deal[i]];
            }
            return map;
        }
        ps.TieEndsAsSlur.Clear();
        var atLeft = Assign(lefts, -1);
        var atRight = Assign(rights, +1);
        Head At(Detection t, int side) => (side < 0 ? atLeft : atRight).TryGetValue(t, out var h) ? h : null;
        // a tie the rules read whose start the detector sees nothing of is none (the stroke the
        // rules followed from the head was a beam's edge or a ledger line's: the reading is not
        // held up by the detector's eye, as a clef the detector sees nothing of the kind of)
        var starts = new HashSet<Head>(atLeft.Values);
        foreach (var a in ps.Heads.Where(h => h.Tie && !starts.Contains(h)))
        {
            if (DebugAt.HasValue && Math.Abs(a.X - DebugAt.Value.X) < 3 * s && Math.Abs(a.Y - DebugAt.Value.Y) < 3 * s)
                Console.WriteLine($"tie read by the rules at head {a.X:F0},{a.Y:F0} pos {a.Pos}: no tie_l detected at it, dropped");
            a.Tie = false;
        }
        foreach (var t in lefts)
        {
            var a = At(t, -1);
            bool debug = DebugAt.HasValue && Math.Abs(t.X - DebugAt.Value.X) < 3 * s && Math.Abs(t.Y - DebugAt.Value.Y) < 3 * s;
            if (a == null) { if (debug) Console.WriteLine($"detected tie_l at {t.X:F0},{t.Y:F0} ({t.Score:F2}): no head before it"); continue; }
            if (a.Tie) continue;
            var st = a.St;
            // (a bar spread by its words holds a tie over 12 spaces: the Zigeuner's b'4.~ b'8, 12.2)
            var b = ps.Heads.Where(o => o != a && o.St == st && o.Pos == a.Pos && o.X - a.X > 0.5 * s && o.X - a.X < 20 * s).OrderBy(o => o.X).FirstOrDefault();
            bool tied;
            string why;
            if (b != null)
            {
                var r = rights.Where(u => u.X > a.X && At(u, +1) == b).OrderByDescending(u => u.Score).FirstOrDefault();
                tied = r != null;
                why = r != null ? $"tie_r at {r.X:F0},{r.Y:F0} ({r.Score:F2}) at the next head at pos {a.Pos}" : $"no tie_r at the next head at pos {a.Pos} ({b.X:F0},{b.Y:F0})";
                // (a long tie across a bar line to the next chord, on a photo: the detector sees its
                // start and nothing of its end, which comes in just past the bar line, and the rules'
                // stroke breaks in the grey. The next note at the same place, past a bar line, nothing
                // between on the staff and no slur ending there, is the tie's end by the notation's
                // own rule: a tie joins a note to the next one at its pitch. Within a bar the detector
                // sees a tie's two ends; a lone start there is a glissando's line, chords001)
                // (a second's head beside its chord's other heads is no note between)
                // (the bar line: the layout's (Layout.FindBars: the rules' column test, or the detector's seen
                // on every staff of the system -- tabi's 2nd bar line, which the ties over it dim to 0.50 for
                // detector42 on the piano's staff, 0.64 for detector39), the detector's on this staff, or, where
                // the ties over it dim it for the detector, tabi's 4th bar line at 0.41, a column dark through
                // the staff clear of the two notes' stems)
                double mid = (st.Lines[0].Top + st.Lines[^1].Bot) / 2.0;
                bool barBetween = ps.Systems?.Any(sys => sys.Staves.Contains(st) && sys.Bars.Any(x => x > a.R && x < b.L)) == true
                    || ps.Detections.Any(u => u.Class == "barline" && u.Score >= TrustScore && u.X > a.R && u.X < b.L && Math.Abs(u.Y - mid) < 3 * s)
                    || Enumerable.Range((int)(a.R + 0.6 * s), Math.Max(0, (int)(b.L - 0.6 * s) - (int)(a.R + 0.6 * s))).Any(x =>
                       {
                           int top = st.Lines[0].Top, bot = st.Lines[^1].Bot, dark = 0;
                           for (int y = top; y <= bot; y++) if (d[x, y]) dark++;
                           return dark >= 0.9 * (bot - top + 1);
                       });
                bool nextNote = !ps.Heads.Any(o => o.St == st && o.X > a.X + 1.5 * s && o.X < b.X - 1.5 * s);
                if (!tied && barBetween && nextNote
                    && !ps.Detections.Any(u => u.Class == "slur_r" && u.Score >= TrustScore && Math.Abs(u.X - b.X) < 1.5 * s && Math.Abs(u.Y - b.Y) < 4 * s))
                {
                    tied = true;
                    why += ", but it is the next note and no slur ends there";
                }
                // (an arc from a note to the next one at its pitch, nothing between, is a tie whatever the
                // detector called its end: a slur_r where a tie ends, on the start's side of the heads. An
                // old print's tie sags, its end a space and a half off the head, the Zigeuner's b'4.~ b'8;
                // Brandt's song: a'4.~ a'8)
                else if (!tied && nextNote && !barBetween)
                {
                    var u = ps.Detections.Where(u => u.Class == "slur_r" && u.Score >= TrustScore && u.X > a.R && u.X < b.X + 0.3 * s
                                                     && b.X - u.X < 2.0 * s && Math.Abs(u.Y - b.Y) <= 2.0 * s && Math.Sign(u.Y - b.Y) == Math.Sign(t.Y - a.Y))
                                      .OrderByDescending(u => u.Score).FirstOrDefault();
                    if (u != null)
                    {
                        tied = true;
                        ps.TieEndsAsSlur.Add(u);
                        why += $", but a slur_r at it ({u.Score:F2}) where a tie ends: the next note at its pitch";
                    }
                }
            }
            else
            {
                // (the last head of the staff with nothing at its position after it: the tie runs
                // on to the margin and into the next system)
                bool last = !ps.Heads.Any(o => o != a && o.St == st && o.X > a.X + 0.3 * s);
                tied = last && st.Right - a.R < 4 * s;
                why = tied ? "the staff's last head: tied into the next system" : "no next head at its position";
            }
            if (debug) Console.WriteLine($"detected tie_l at {t.X:F0},{t.Y:F0} ({t.Score:F2}) at head {a.X:F0},{a.Y:F0} pos {a.Pos}: {why} -> {tied}");
            if (tied) a.Tie = true;
        }
        // a tie's end at a head whose start the detector missed (a short tie under a chord, a space
        // and a half long, ties007): the head before it at its position is the one it left
        // (and one farther back, to a tie's full reach, when an arc's start stands at that head, seen
        // under the threshold or as a slur's: an old print's tie from a dotted note over the other
        // voice's chords to its eighth, the start seen as tie_l 0.26-0.32 or slur_l 0.37-0.79 past the
        // dot, the end as tie_r 0.65-0.87, twelve to seventeen spaces on; kinkel7-1's scan, bars 18,
        // 20 and 22. The start's arc leaves the head past its dot, up to two spaces over it -- and
        // the head's chord is the nearest to it, as Assign gives an end its head: not the other
        // voice's start beside it)
        bool StartNear(Detection t, Head o) => t.X - o.X > -0.3 * s && t.X - o.X < 2.5 * s && Math.Abs(t.Y - o.Y) <= 2 * s;
        Detection StartAt(Head h) => ps.Detections.Where(t => t.Class is "tie_l" or "slur_l" && t.Score >= Detector.WeakSlur && StartNear(t, h)
                                                               && chordOf[ps.Heads.Where(o => StartNear(t, o)).MinBy(o => Math.Abs(o.Y - t.Y) + 0.3 * Math.Abs(o.X - t.X))] == chordOf[h])
                                                  .MaxBy(t => t.Score);
        foreach (var u in rights)
        {
            var b = At(u, +1);
            if (b == null) continue;
            var a = ps.Heads.Where(o => o != b && o.St == b.St && o.Pos == b.Pos && b.X - o.X > 0.5 * s && b.X - o.X < 20 * s).OrderByDescending(o => o.X).FirstOrDefault();
            if (a == null || a.Tie) continue;
            bool debug = DebugAt.HasValue && Math.Abs(u.X - DebugAt.Value.X) < 3 * s && Math.Abs(u.Y - DebugAt.Value.Y) < 3 * s;
            var start = b.X - a.X < 12 * s ? null : StartAt(a);
            if (b.X - a.X >= 12 * s && start == null)
            {
                if (debug) Console.WriteLine($"detected tie_r at {u.X:F0},{u.Y:F0} ({u.Score:F2}) at head {b.X:F0},{b.Y:F0} pos {b.Pos}: no tie_l seen, the head before at {a.X:F0},{a.Y:F0} {(b.X - a.X) / s:F1} spaces back with no start seen at it: not tied");
                continue;
            }
            if (debug)
                Console.WriteLine($"detected tie_r at {u.X:F0},{u.Y:F0} ({u.Score:F2}) at head {b.X:F0},{b.Y:F0} pos {b.Pos}: no tie_l seen, tied from the head before at {a.X:F0},{a.Y:F0}"
                                  + (start == null ? "" : $" ({(b.X - a.X) / s:F1} spaces back, a {start.Class} {start.Score:F2} at it)"));
            // (a slur's start the detector saw there is the tie's: no slur's)
            if (start?.Class == "slur_l") ps.TieEndsAsSlur.Add(start);
            a.Tie = a.TieByEnd = true;
        }
        // a chord tied at a note to the next chord, which repeats others of its notes: those are tied too where
        // the detector sees an end of their own arc (within 0.6 of a space of the head's height), however
        // unsure (from Detector.WeakSlur) -- tabi's <cis fis>2.~ over the bar line on the owner's photo, the
        // fis's tie_l at 0.81, the cis's arc at tie_l 0.11 and tie_r 0.43. A tie on one note only was the
        // chord's in the .lys until it could be written a note at a time (Lily#'s 8a3780d5e)
        Head Next(Head h) => ps.Heads.Where(o => o != h && o.St == h.St && o.Pos == h.Pos && o.X - h.X > 0.5 * s && o.X - h.X < 20 * s).OrderBy(o => o.X).FirstOrDefault();
        foreach (var chord in chordOf.Values.Distinct().Where(c => c.Count > 1 && c.Any(h => h.Tie) && !c.All(h => h.Tie)).ToList())
        {
            var to = chord.Where(h => h.Tie).Select(Next).FirstOrDefault(n => n != null);
            if (to == null) continue;
            foreach (var h in chord.Where(h => !h.Tie))
            {
                var n = Next(h);
                if (n == null || chordOf[n] != chordOf[to]) continue;
                // (a slur's end seen there is the arc's as well, as a slur_l is a tie's start in StartAt: the
                // cis's start in bar 3 seen as slur_l 0.31. An end comes in up to 0.6 of a space past the middle of
                // a second's head set left of the stem: the fis's tie_r at 0.47, 5 pixels right of it)
                var end = ps.Detections.Where(t => t.Score >= Detector.WeakSlur
                        && (t.Class is "tie_l" or "slur_l" && t.X - h.X > -0.3 * s && t.X - h.X < 2.0 * s && Math.Abs(t.Y - h.Y) <= 0.6 * s
                            || t.Class is "tie_r" or "slur_r" && n.X - t.X > -0.6 * s && n.X - t.X < 2.0 * s && Math.Abs(t.Y - n.Y) <= 0.6 * s))
                    .MaxBy(t => t.Score);
                if (DebugAt.HasValue && Math.Abs(h.X - DebugAt.Value.X) < 3 * s && Math.Abs(h.Y - DebugAt.Value.Y) < 3 * s)
                    Console.WriteLine($"chord-mate of a tied head at {h.X:F0},{h.Y:F0} pos {h.Pos}, repeated at {n.X:F0},{n.Y:F0}: {(end == null ? "no end of its own arc seen" : $"{end.Class} {end.Score:F2} at {end.X:F0},{end.Y:F0}")} -> {end != null}");
                if (end != null) h.Tie = true;
            }
        }
    }

    /// <summary>
    /// The slurs the learnt detector saw (the rules read none). Lily# ends a slur over or under the
    /// first and last notes it spans: within a space of the note's x (past its dot or its flag when
    /// it leaves one), a space beyond its outer head, or some 0.7 space beyond its stem's tip when
    /// the stem points the slur's way, two or three spaces off when a higher note between lifts it
    /// (measured over the slurs suite's 1,600 slurs). Each detected end goes to the chord it stands
    /// by, the one it is nearest to across and beyond; then on each staff a start takes the first
    /// end after it on the same side of its notes (a second voice's slurs curve the other way).
    /// A slur broken at a system's end leaves an end by no note at the staff's very end and a start
    /// by none after the next system's clef: the staff's last open start runs on when the edge has
    /// such an end, and the next system's first end closes it (the second piece's start is a shape
    /// the detector mostly misses).
    /// </summary>
    static void DetectedSlurs(PageSymbols ps, Img d, double s)
    {
        // (a slur_r DetectedTies took for a tie's end ends no slur; a weak end, 0.2 up, only pairs with
        // a sure one)
        var ends = ps.Detections.Where(t => (t.Class == "slur_l" || t.Class == "slur_r") && t.Score >= Detector.WeakSlur && !ps.TieEndsAsSlur.Contains(t)).ToList();
        bool Sure(Detection t) => t.Score >= TrustScore;
        // a tie's start that tied nothing (DetectedTies: no head at its place after it, or a slur's
        // end there) is a slur's start the detector took for a tie: the short arc from a chord's top
        // note down to the next note, tabi's 8th bar. It starts a slur only to an end found for it
        // (and a weak one, 0.2 up, only a sure end with no start of its own takes, as a weak slur_l:
        // the arc over a second voice's b'8( g'8) that leaves beside the first voice's dotted note
        // over it, tie_l 0.31-0.32 and nothing else, kinkel7-6's 21st bar in print and scan)
        // (one with no head before it, as a tie's start has, is nothing: the accidentals before a
        // chord seen as tie_l 0.25, kinkel10-5's scan, paired with a sure end over the next chord)
        bool HeadBefore(Detection t, bool tied) => ps.Heads.Any(h => (h.Tie || !tied) && Math.Abs(h.Y - t.Y) <= 1.25 * s && t.X - h.X > -0.3 * s && t.X - h.X < 2.0 * s);
        var fromTie = ps.Detections.Where(t => t.Class == "tie_l" && t.Score >= Detector.WeakSlur && HeadBefore(t, false) && !HeadBefore(t, true)).ToHashSet();
        ends.AddRange(fromTie);
        // (nor a weak start by a grace note, slur_l or tie_l: Lily#'s slur from an acciaccatura to
        // its note, which the start would give to the note before it, gen-grace's basic002)
        ends.RemoveAll(t => !Sure(t) && t.Class is "slur_l" or "tie_l" && ps.Graces.Any(g => Math.Abs(g.X - t.X) < 1.5 * s && Math.Abs(g.Y - t.Y) < 2.5 * s));
        // (nor a weak end at a hairpin's end the detector is sure of: the two lines' ends at a hairpin's open
        // end, a space above and below it (1.04 spaces off its centre), seen as slur_l 0.35 and 0.25 under
        // kinkel19-2's right hand, bar 26, the open end itself at 0.71; the upper one, under the chord,
        // started a slur to the next bar's end. Within 1.2 spaces of the hairpin's end only: a slur's true
        // end 1.3 spaces under a hairpin's, slur_r 0.30 ending the arc over kinkel19-2's left hand, bar 50)
        var pinEnds = ps.Detections.Where(t => t.Class is "hairpin_tip" or "hairpin_open" && t.Score >= TrustScore).ToList();
        ends.RemoveAll(t => !Sure(t) && pinEnds.FirstOrDefault(h => Math.Abs(h.X - t.X) < 0.7 * s && Math.Abs(h.Y - t.Y) < 1.2 * s) is Detection pin
                            && (!Debug(t) || Log($"detected {t.Class} at {t.X:F0},{t.Y:F0} ({t.Score:F2}): a line's end of the {pin.Class} at {pin.X:F0},{pin.Y:F0} ({pin.Score:F2}), dropped")));
        static bool Log(string line) { Console.WriteLine(line); return true; }
        if (ends.Count == 0) return;
        // chords: the heads on one stem, or stemless at one x
        var chords = new List<List<Head>>();
        var seen = new HashSet<Head>();
        foreach (var h in ps.Heads.OrderBy(h => h.X))
        {
            if (seen.Contains(h)) continue;
            var chord = ps.Heads.Where(o => o.St == h.St && (h.HasStem ? o.HasStem && o.Up == h.Up && Math.Abs(o.StemX - h.StemX) < 0.3 * s : !o.HasStem && Math.Abs(o.X - h.X) < 0.5 * s)).ToList();
            foreach (var o in chord) seen.Add(o);
            chords.Add(chord);
        }
        // how far the end stands from the chord: across from its heads' x, and beyond the span of
        // its heads and stem up or down
        (double Cost, bool Above) Fit(List<Head> c, Detection t)
        {
            // (across: from the chord's heads, a second's head on the stem's far side and the dots
            // included: a slur leaving a dotted chord or a second starts past its right edge)
            // (and an up stem's flag: a slur under nothing starts past it)
            double xl = c.Min(h => h.L), xr = c.Max(h => h.R + (h.Dots > 0 ? 0.8 * s : 0));
            if (c[0].HasStem && c[0].Up && c[0].Beams > 0) xr = Math.Max(xr, c[0].StemX + 0.8 * s);
            double top = c.Min(h => h.Y), bot = c.Max(h => h.Y);
            if (c[0].HasStem) { top = Math.Min(top, c[0].Tip); bot = Math.Max(bot, c[0].Tip); }
            double dx = t.X < xl ? xl - t.X : t.X > xr ? t.X - xr : 0, gap = t.Y < top ? top - t.Y : t.Y > bot ? t.Y - bot : 0;
            // (a slur clears the notes it spans: over a high chord between, its end stands two or
            // three spaces off its own note, 3.5 at most in the slurs suite; across it is within one)
            if (dx > 1.0 * s || gap > 4.5 * s) return (double.MaxValue, false);
            return (dx + 0.5 * gap, t.Y < (c.Min(h => h.Y) + c.Max(h => h.Y)) / 2);
        }
        var at = new Dictionary<Detection, (List<Head> Chord, bool Above)>();
        foreach (var t in ends)
        {
            // (a broken slur's first piece ends at the staff's very end, past the last note's reach)
            var near = Nearest(ps.Heads.Select(h => h.St).Distinct().ToList(), t.Y, s);
            if (t.Class == "slur_r" && near != null && near.Right - t.X < 0.6 * s) continue;
            // (not moved to the other voice's chord at the same x when level with this one's outer head:
            // tried for kinkel7-6's 21st bar as MuseScore draws it, where the arc over the second voice's
            // b'8( g'8) begins beside the first voice's dis''4., and dropped -- the detector sees that
            // start under 0.2 there, and the move took a glissando's line seen as slur_l 0.55 from the
            // first voice to the second, gen-gliss's voices2001, for one slur more on the development set)
            var best = chords.Select(c => (c, f: Fit(c, t))).Where(p => p.f.Cost < double.MaxValue).OrderBy(p => p.f.Cost).FirstOrDefault();
            // (a start among a tied chord's heads, not beyond them, is one of its ties' ends: a slur
            // clears its outer note. Tabi's 4th bar, four ties out of a chord, one seen as a slur)
            if (best.c != null && t.Class == "slur_l" && best.c.Any(h => h.Tie) && t.Y > best.c.Min(h => h.Y) && t.Y < best.c.Max(h => h.Y))
            {
                if (DebugAt.HasValue && Math.Abs(t.X - DebugAt.Value.X) < 3 * s && Math.Abs(t.Y - DebugAt.Value.Y) < 3 * s)
                    Console.WriteLine($"detected slur_l at {t.X:F0},{t.Y:F0} ({t.Score:F2}): among a tied chord's heads, a tie's end");
                continue;
            }
            if (best.c != null) at[t] = (best.c, best.f.Above);
            if (DebugAt.HasValue && Math.Abs(t.X - DebugAt.Value.X) < 3 * s && Math.Abs(t.Y - DebugAt.Value.Y) < 3 * s)
                Console.WriteLine($"detected {t.Class} at {t.X:F0},{t.Y:F0} ({t.Score:F2}): " + (best.c == null ? "by no note"
                    : $"{(best.f.Above ? "over" : "under")} the chord at {best.c.Average(h => h.X):F0} ({string.Join(" ", best.c.Select(h => h.Pos))})"));
        }
        // an arc over three notes and more that each sing a syllable of their own is no slur: a sung
        // slur holds one syllable over its notes. An old print's tuplet arc, its 3 at the top, over a
        // voice's triplet of eighths (Chaminade's song, Enoch 1898: au pa-ys, dé-li-ci-eux)
        var words = new Dictionary<Staff, List<(double L, double R)>>();
        int? Sung(Staff st, List<Head> a, List<Head> b)
        {
            if (!words.TryGetValue(st, out var syl)) words[st] = syl = Syllables(ps, d, st, s);
            if (syl.Count == 0) return null;
            double ax = a.Average(h => h.X), bx = b.Average(h => h.X);
            var on = chords.Where(c => c[0].St == st).Select(c => c.Average(h => h.X)).Order().ToList();
            var span = on.Where(x => x > ax - 0.3 * s && x < bx + 0.3 * s).ToList();
            // (a tuplet's three notes and more: a slur from one syllable's note to the next one's is
            // printed in songs, "-gen Ziel!" in Lang's Op.36-3)
            if (span.Count < 3) return null;
            // (each syllable is its nearest note's, within a space and a half of it)
            var owner = syl.Select(w => on.MinBy(x => Math.Abs(x - (w.L + w.R) / 2)) is var x && Math.Abs(x - (w.L + w.R) / 2) < 1.5 * s ? x : double.NaN).ToList();
            return span.All(x => owner.Contains(x)) ? span.Count : null;
        }
        Staff StaffOf(Detection t) => at.TryGetValue(t, out var a) ? a.Chord[0].St : Nearest(ps.Heads.Select(h => h.St).Distinct().ToList(), t.Y, s);
        // (--at 0,0: every slur's working on the page)
        bool Debug(Detection t) => DebugAt.HasValue && (DebugAt.Value == (0, 0) || Math.Abs(t.X - DebugAt.Value.X) < 3 * s && Math.Abs(t.Y - DebugAt.Value.Y) < 3 * s);
        var carried = new Dictionary<int, bool>();   // part -> a slur ran on from its staff in the system before
        foreach (var g in ends.GroupBy(StaffOf).Where(g => g.Key != null).OrderBy(g => g.Key.System).ThenBy(g => g.Key.Part))
        {
            var st = g.Key;
            // (one start and one end a chord on a side: the detector seen twice on one end, 0.51 and
            // 0.77 a space apart, gave the second start the next slur's end, and every slur after it
            // the end of the one after, kinkel19-4's left hand)
            List<Detection> OnePerChord(IEnumerable<Detection> ts) => ts.GroupBy(t => (at[t].Chord, at[t].Above))
                .Select(c => c.MaxBy(t => (fromTie.Contains(t) ? 0 : 1, t.Score))).OrderBy(t => t.X).ToList();
            var lefts = OnePerChord(g.Where(t => (t.Class == "slur_l" || fromTie.Contains(t)) && at.ContainsKey(t)));
            var rights = OnePerChord(g.Where(t => t.Class == "slur_r" && at.ContainsKey(t)));
            // the ends by no note: a broken slur's, at the staff's two edges
            bool hangsRight = g.Any(t => t.Class == "slur_r" && Sure(t) && !at.ContainsKey(t) && st.Right - t.X < 3 * s);
            bool hangsLeft = g.Any(t => t.Class == "slur_l" && Sure(t) && !at.ContainsKey(t) && t.X - st.Left < 12 * s);
            var used = new HashSet<Detection>();
            var open = new List<Detection>();
            double X(Detection t) => at[t].Chord.Average(h => h.X);
            // (a weak end and a sure one with a tuplet's number between them at their height, or by
            // the weak end, are a bracket's two ends: MuseScore's triplet brackets under kinkel19-6's
            // right hand, the right hook seen as a slur's end at 0.62-0.71, the left end or the number
            // itself at 0.27-0.42. The number is taken from WeakTupletNumber: an old print's 3 under
            // its arc, kinkel19-6's scan, bar 2, seen at 0.42)
            // And a bracket with no number, MuseScore's after the first two: a straight thin line from
            // one end to the other through the middle of the span, where a slur bulges away from its
            // notes (the middle fifth of it, ink within 0.15 space of the line between the two ends
            // in seven of ten columns, and none 0.3-2 spaces beyond it in half of them)
            // (Two sure ends with the number in the middle of the span at their height, over as many
            // chords as it says, are not told from a slur: an old print's tuplet arc round its 3
            // (lang36-3's scan, bar 32, slur_l 0.93 and slur_r 0.82, the 3 at 0.49) is the shape of
            // Lily#'s slur over a triplet whose 3 stands on the slur's side (gen-mmrests-scan's
            // slurs004) and of MuseScore's over a triplet sung on one syllable (kinkel19-1's ms, bar
            // 8, "Her - -"): tried 2026-11-01 and dropped)
            bool Bracket(Detection l, Detection r)
            {
                double midY = (l.Y + r.Y) / 2;
                if (ps.Detections.Concat(ps.WeakTuplets).Any(u => u.Class.StartsWith("tuplet_") && u.Score >= WeakTupletNumber
                                                                  && u.X > l.X - s && u.X < r.X + s && Math.Abs(u.Y - midY) < 1.5 * s)) return true;
                int x0 = (int)(l.X + 0.4 * (r.X - l.X)), x1 = (int)(l.X + 0.6 * (r.X - l.X));
                int sign = at[l].Above ? -1 : 1, on = 0, beyond = 0, n = 0;
                for (int x = Math.Max(0, x0); x <= Math.Min(d.W - 1, x1); x++)
                {
                    double y = l.Y + (r.Y - l.Y) * (x - l.X) / (r.X - l.X);
                    bool Ink(double a, double b)
                    {
                        for (int yy = (int)Math.Min(a, b); yy <= (int)Math.Max(a, b); yy++)
                            if (yy >= 0 && yy < d.H && d[x, yy]) return true;
                        return false;
                    }
                    n++;
                    if (Ink(y - 0.15 * s, y + 0.15 * s)) on++;
                    if (Ink(y + sign * 0.3 * s, y + sign * 2 * s)) beyond++;
                }
                return n > 0 && on >= 0.7 * n && beyond < 0.5 * n;
            }
            // a start and an end on the two notes a tie joins (the same notes, the tie read, nothing between)
            // are the tie's own arc seen again by the detector as a slur's two ends -- an old print's tie
            // under a sixteenth, tie_l 0.86 and slur_l 0.51 at it, kinkel7-6's scan -- unless two arcs run
            // between the notes, the tie and a slur over it (the slurs suite's c4~( c8), as written; and a
            // slur from a tied note on past its partner, c4~( c8 d4), ends beyond it): the ink in the middle
            // column between the chords, on the arc's side, holds two thin strokes then, one for the tie alone
            bool TiePartner(Detection l, Detection r)
            {
                var (a, b) = (at[l].Chord, at[r].Chord);
                if (!a.All(h => h.Tie) || b.Count != a.Count || !a.Select(h => h.Pos).Order().SequenceEqual(b.Select(h => h.Pos).Order())) return false;
                bool OtherVoice(Head o) => a[0].HasStem && o.HasStem && o.Up != a[0].Up && (a[0].Up ? o.Y > a.Max(h => h.Y) : o.Y < a.Min(h => h.Y));
                if (ps.Heads.Any(o => o.St == st && !a.Contains(o) && !b.Contains(o) && !OtherVoice(o) && o.X > a.Max(h => h.X) + 0.5 * s && o.X < b.Min(h => h.X) - 0.5 * s)) return false;
                var lineRows = new HashSet<int>();
                foreach (var ln in st.Lines) for (int yy = ln.Top - 1; yy <= ln.Bot + 1; yy++) lineRows.Add(yy);
                int mid = (int)((a.Max(h => h.R) + b.Min(h => h.L)) / 2), sign = at[l].Above ? -1 : 1;
                double edge = at[l].Above ? Math.Min(a.Min(h => h.Y), b.Min(h => h.Y)) - 0.5 * s : Math.Max(a.Max(h => h.Y), b.Max(h => h.Y)) + 0.5 * s;
                int arcs = 0;
                for (int x = mid - 2; x <= mid + 2; x++)
                {
                    if (x < 0 || x >= d.W) continue;
                    int n = 0;
                    for (int k = 0; k <= 4 * s; k++)
                    {
                        int y = (int)edge + sign * k;
                        if (y < 0 || y >= d.H || !d[x, y] || lineRows.Contains(y)) continue;
                        int t = k;
                        while (k + 1 <= 4 * s && (int)edge + sign * (k + 1) is var y2 && y2 >= 0 && y2 < d.H && d[x, y2] && !lineRows.Contains(y2)) k++;
                        if (k - t + 1 <= 0.4 * s) n++;
                    }
                    arcs = Math.Max(arcs, n);
                }
                if (Debug(l) || Debug(r)) Console.WriteLine($"slur from {a.Average(h => h.X):F0} to {b.Average(h => h.X):F0}, the notes a tie joins: {arcs} thin stroke(s) between them at x {mid} -> {(arcs >= 2 ? "a slur over the tie" : "the tie's own arc")}");
                return arcs < 2;
            }
            // a fermata's arc over one note, its two edges seen as a slur's start and end (0.69 and 0.57,
            // three spaces apart, over the last note of kinkel10-4's scan's 34th bar)
            // (or one the detector does not see, an old print's, its 10th bar's: the arc's two edges two to five
            // spaces apart at one height, and its dot, a loose piece of ink a third of a space round, in the
            // middle under the arc, on the notes' side)
            bool Fermata(Detection l, Detection r)
            {
                bool seen = ps.Detections.Any(u => u.Class == "fermata" && u.Score >= TrustScore
                    && Math.Abs(u.X - l.X) < 2.2 * s && Math.Abs(u.X - r.X) < 2.2 * s && Math.Abs(u.Y - l.Y) < 1.2 * s && Math.Abs(u.Y - r.Y) < 1.2 * s);
                if (seen && (Debug(l) || Debug(r))) Console.WriteLine($"fermata detected between the ends {l.X:F0},{l.Y:F0} and {r.X:F0},{r.Y:F0}: no slur");
                if (seen) return true;
                if (r.X - l.X < 2 * s || r.X - l.X > 5 * s || Math.Abs(l.Y - r.Y) > 0.6 * s) return false;
                double mx = (l.X + r.X) / 2, my = (l.Y + r.Y) / 2;
                int sign = at[l].Above ? 1 : -1;
                // (round: a dash of ink half a space wide and a quarter high under a two-note slur is none, kinkel19-2's
                // scan; centred under the arc's middle within a third of a space, as a fermata's is: a round piece
                // half a space off the middle between two slurred notes is none, the slurs suite's slurs006; and
                // clear of the notes, a space off every head of the two chords: a dotted note's dot is none)
                var heads = at[l].Chord.Concat(at[r].Chord).ToList();
                bool Dot(Other o) => o.C.W >= 0.25 * s && o.C.W <= 0.7 * s && o.C.H >= 0.25 * s && o.C.H <= 0.7 * s && Math.Min(o.C.W, o.C.H) >= 0.65 * Math.Max(o.C.W, o.C.H)
                    && Math.Abs(o.C.Cx - mx) < 0.3 * s && (o.C.Cy - my) * sign > -0.8 * s && (o.C.Cy - my) * sign < 1.2 * s
                    && heads.All(h => (h.Y - o.C.Cy) * sign >= 0.9 * s);
                if (Debug(l) || Debug(r))
                    Console.WriteLine($"fermata? ends {l.X:F0},{l.Y:F0} {r.X:F0},{r.Y:F0}: ink pieces near {mx:F0},{my:F0}: "
                        + string.Join(" ", ps.Others.Where(o => Math.Abs(o.C.Cx - mx) < 2 * s && Math.Abs(o.C.Cy - my) < 2 * s).Select(o => $"{o.Class}@{o.C.Cx:F0},{o.C.Cy:F0} {o.C.W}x{o.C.H}{(Dot(o) ? " dot" : "")}")));
                return ps.Others.Any(Dot);
            }
            // A weak end far from its sure one joins it only along the arc's own ink: from the sure end one
            // thin stroke runs, column by column, to the weak end, passing a stem or a beam it crosses and a
            // break in an old print's ink (the staves' line rows left out, as FindTies does). Not joined: a
            // tie's start seen as slur_l 0.20 with a slur's end seventy spaces on, the tie's own end unseen
            // (kinkel7-1's scan, 8th bar). A near one (FarSlur spaces and less, most slurs) is taken without:
            // the stroke of an old print's short slur is lost too often (a staff line's rows take a shallow
            // arc, its ink breaks, its tip merges with a stem) for the few far pairs it would spare
            bool Joined(Detection sure, Detection weak) => JoinedTo(sure, weak.X, weak.Y, $"{weak.X:F0},{weak.Y:F0} ({weak.Score:F2})", Debug(sure) || Debug(weak));
            // (or to a point: the end of a tie's arc by the head it joins, no end detected there)
            // (slack: how far short of the point the stroke may end, a space and a half; two for a tie's end no
            // detector saw, where an old print's arc thins to nothing: kinkel7-1's scan, bar 19, the arc fading
            // out two spaces before the dot of the d'''4. it ties)
            // (bowsUp: the stroke must bow off the straight line between its two ends by 0.4 space, upward or
            // downward, as a tie's arc does away from its notes -- asked when one end only was seen: a staff
            // line's row the scan's turn left out of the rows excluded, run along from a weak start, was taken
            // for a tie's arc to the next head at the pitch six spaces on, kinkel19-6's scan, bar 16)
            bool JoinedTo(Detection sure, double gx, double gy, string to, bool debug, double slack = 1.5, bool? bowsUp = null)
            {
                int dir = Math.Sign(gx - sure.X);
                if (dir == 0) return false;
                // (every staff's line rows on the page: a slur under one staff runs close over the next one's top line)
                var lineRows = new HashSet<int>();
                foreach (var o in ps.Heads.Select(h => h.St).Distinct())
                    foreach (var ln in o.Lines) for (int yy = ln.Top - 1; yy <= ln.Bot + 1; yy++) lineRows.Add(yy);
                bool InLines(double y) => lineRows.Contains((int)Math.Floor(y)) || lineRows.Contains((int)Math.Ceiling(y));
                int thinMax = (int)(0.4 * s);
                int y0 = (int)Math.Max(0, Math.Min(sure.Y, gy) - 6 * s), y1 = (int)Math.Min(d.H - 1, Math.Max(sure.Y, gy) + 6 * s);
                List<(int T, int B)> Runs(int x)
                {
                    var r = new List<(int, int)>();
                    for (int y = y0; y <= y1; y++)
                    {
                        if (!d[x, y] || lineRows.Contains(y)) continue;
                        int t = y;
                        while (y + 1 <= y1 && d[x, y + 1] && !lineRows.Contains(y + 1)) y++;
                        r.Add((t, y));
                    }
                    return r;
                }
                double Mid((int T, int B) r) => (r.T + r.B) / 2.0;
                int goal = (int)gx;
                string Columns(int from) => string.Join(" | ", Enumerable.Range(0, 12).Select(k => from + dir * 3 * k).Where(xx => xx >= 0 && xx < d.W)
                    .Select(xx => string.Join(",", Runs(xx).Select(r => $"{r.T - (int)sure.Y}..{r.B - (int)sure.Y}"))));
                // the stroke at the sure end: a thin run within a space of its height over the first columns
                // from it towards the other end, nearest first; the first that leads to the other end wins (the
                // end's own column may hold none of the arc yet, only the head under it, the tip coming a column
                // or two on; or a line's one-row remnant lies nearer the end's height than the tip does)
                var starts = new List<(int X, (int T, int B) Run)>();
                for (int k = 0; k <= s; k++)
                {
                    int xx = (int)sure.X + dir * k;
                    if (xx < 0 || xx >= d.W) break;
                    foreach (var r in Runs(xx).Where(r => r.B - r.T + 1 <= thinMax && Math.Abs(Mid(r) - sure.Y) <= s)) starts.Add((xx, r));
                }
                if (starts.Count == 0)
                {
                    if (debug) Console.WriteLine($"arc from {sure.X:F0},{sure.Y:F0} ({sure.Score:F2}) towards {to}: no thin stroke at the sure end -> not joined\n    columns from the sure end (rows rel. to it): {Columns((int)sure.X)}");
                    return false;
                }
                (int Reach, double Y, double Bow) far = (int.MinValue, 0, 0);
                foreach (var (sx, run) in starts.OrderBy(p => Math.Abs(Mid(p.Run) - sure.Y)).Take(6))
                {
                    var end = Trace(sx, run);
                    // (the stroke is not asked to bow away from the notes unless bowsUp says: a slur between notes
                    // of different heights climbs from one end to the other, its highest point the end itself)
                    if (Math.Abs(end.Reach - goal) <= slack * s && Math.Abs(end.Y - gy) <= 1.5 * s)
                    {
                        bool bows = bowsUp == null || (bowsUp.Value ? end.Bow : -end.Bow) >= 0.4 * s;
                        if (debug) Console.WriteLine($"arc from {sure.X:F0},{sure.Y:F0} ({sure.Score:F2}) towards {to}: from {sx},{Mid(run):F0} reach {end.Reach} of {goal} at y {end.Y:F0}, bowing {end.Bow / s:F2} space{(bowsUp == null ? "" : bowsUp.Value ? " (up asked)" : " (down asked)")} -> {(bows ? "joined" : "straight, not joined")}");
                        if (bows) return true;
                    }
                    if (far.Reach == int.MinValue || Math.Abs(end.Reach - goal) < Math.Abs(far.Reach - goal)) far = end;
                }
                if (debug)
                {
                    Console.WriteLine($"arc from {sure.X:F0},{sure.Y:F0} ({sure.Score:F2}) towards {to}: {starts.Count} strokes to start from, the farthest reach {far.Reach} of {goal} at y {far.Y:F0} -> not joined");
                    Console.WriteLine($"    columns about that reach (rows rel. to the sure end): {Columns(far.Reach - dir * 6)}");
                }
                return false;

                // (Bow: how far the stroke bows off the straight line between its first and last points, at most;
                // upward positive)
                (int Reach, double Y, double Bow) Trace(int x, (int T, int B) cur)
                {
                    int reach = x, gap = 0, thick = 0, hidden = 0;
                    // (the arc's slope where it was last seen, over the five columns before: where a break or a
                    // crossing hides it, it is looked for where that slope leads -- a thin arc drawn at a slope
                    // steps a row or two a column)
                    var path = new List<(int X, double Y)> { (x, Mid(cur)) };
                    double Slope() => path.Count < 2 ? 0 : (path[^1].Y - path[Math.Max(0, path.Count - 6)].Y) / (path[^1].X - path[Math.Max(0, path.Count - 6)].X);
                    for (x += dir; dir > 0 ? x < goal : x > goal; x += dir)
                    {
                        if (x < 0 || x >= d.W) break;
                        // the run that carries on from the last one: overlapping where the arc should be, with play
                        // for the columns skipped over a break or a crossing; the nearest, and of two as near the
                        // one as thick as the arc (a line's one-row remnant beside it does not draw it off)
                        double at0 = Mid(cur) + Slope() * (x - reach);
                        double half = (cur.B - cur.T) / 2.0 + 3 + (gap + thick + hidden) / 4.0;
                        var next = Runs(x).Where(r => r.B >= at0 - half && r.T <= at0 + half)
                                          .OrderBy(r => Math.Abs(Mid(r) - at0) + Math.Abs((r.B - r.T) - (cur.B - cur.T))).Cast<(int T, int B)?>().FirstOrDefault();
                        if (next == null)
                        {
                            // (a shallow arc crosses a staff line's rows, left out, over many columns: hidden there, not broken)
                            if (InLines(at0)) { if (++hidden > 6 * s) break; continue; }
                            if (++gap > 1.5 * s) break;
                            continue;
                        }
                        // (a crossing: a stem's few columns, a beam's edge the arc runs along for a space or two)
                        if (next.Value.B - next.Value.T + 1 > thinMax) { if (++thick > 2.5 * s) break; continue; }
                        cur = next.Value; gap = thick = hidden = 0; reach = x;
                        path.Add((x, Mid(cur)));
                    }
                    double bow = 0;
                    if (path.Count > 2 && path[^1].X != path[0].X)
                        foreach (var p in path)
                        {
                            double onLine = path[0].Y + (path[^1].Y - path[0].Y) * (p.X - path[0].X) / (double)(path[^1].X - path[0].X);
                            if (Math.Abs(onLine - p.Y) > Math.Abs(bow)) bow = onLine - p.Y;
                        }
                    return (reach, Mid(cur), bow);
                }
            }
            // the next chord on the staff a tie from this one would join: the same notes, none with a sign,
            // nothing between but the other voice's heads, within a tie's reach (DetectedTies' 20 spaces)
            List<Head> TieNext(List<Head> a)
            {
                double ax = a.Max(h => h.X);
                bool OtherVoice(Head o) => a[0].HasStem && o.HasStem && o.Up != a[0].Up && (a[0].Up ? o.Y > a.Max(h => h.Y) : o.Y < a.Min(h => h.Y));
                var b = chords.Where(c => c[0].St == st && c.Min(h => h.X) > ax + 0.5 * s && c.Min(h => h.X) - ax < 20 * s && !c.Any(OtherVoice)).MinBy(c => c.Min(h => h.X));
                if (b == null || b.Count != a.Count || !a.Select(h => h.Pos).Order().SequenceEqual(b.Select(h => h.Pos).Order()) || b.Any(h => h.Acc != null)) return null;
                return b;
            }
            // (where a tie's arc ends by the chord it joins: just before its heads, two thirds of a space off them on the arc's side;
            // and where it leaves a head: just past it, or past its dot)
            (double X, double Y) TieEnd(List<Head> b, bool above) => (b.Min(h => h.L) - 0.2 * s, (above ? b.Min(h => h.Y) - 0.7 * s : b.Max(h => h.Y) + 0.7 * s));
            (double X, double Y) TieStart(Head h, bool above) => (h.R + (h.Dots > 0 ? 0.8 * s : 0) + 0.2 * s, above ? h.Y - 0.7 * s : h.Y + 0.7 * s);
            foreach (var l in lefts.Where(Sure))
            {
                var a = at[l].Chord;
                double ax = X(l);
                // (a start the detector took for a tie's is a short arc, to a note or two on: it takes
                // no end bars away, which is another slur's, kinkel19-2's 42nd bar, the Zigeuner's 18th)
                var r = rights.Where(r => Sure(r) && !used.Contains(r) && at[r].Chord != a && X(r) > ax && at[r].Above == at[l].Above
                                          && (!fromTie.Contains(l) || X(r) - ax < 12 * s) && !TiePartner(l, r) && !Fermata(l, r))
                              .OrderBy(r => r.X).FirstOrDefault();
                if (r != null) Pair(l, r);
                else if (!fromTie.Contains(l)) open.Add(l);
            }
            // a sure start with no sure end takes a weak one: the detector sees an old print's slur ends
            // at 0.2-0.5 as often as above (the development set's scans, 122 slurs placed: both ends
            // sure 34, one sure and the other weak 48 more). The first after it on its side, before the
            // next sure start there; not the staff's last start when a slur runs on from it to the edge
            foreach (var l in open.ToList())
            {
                if (hangsRight && l == open[^1]) continue;
                double ax = X(l);
                double next = lefts.Where(o => Sure(o) && o != l && at[o].Above == at[l].Above && X(o) > ax).Select(X).DefaultIfEmpty(double.MaxValue).Min();
                var r = rights.Where(r => !Sure(r) && !used.Contains(r) && at[r].Chord != at[l].Chord && X(r) > ax && X(r) < next && at[r].Above == at[l].Above
                                          && !TiePartner(l, r) && !Bracket(l, r) && !Fermata(l, r) && (r.X - l.X <= FarSlur * s || Joined(l, r)))
                              .OrderBy(r => r.X).FirstOrDefault();
                if (r == null) continue;
                Pair(l, r);
                open.Remove(l);
            }
            // (only the last start left open can run on to the edge)
            bool ranOn = false;
            foreach (var l in open)
            {
                var a = at[l].Chord;
                bool runs = hangsRight && l == open[^1];
                if (runs) { foreach (var h in a) h.SlurStart = true; ranOn = true; used.Add(l); }
                // (a sure start with no end at all, leaving the head from its side, as a tie does, towards the next
                // chord at the same notes with nothing between: the tie's, its end unseen, when the arc's ink runs
                // from the start to that chord -- an old print's tie from a dotted quarter to its eighth, slur_l
                // 0.75 past the dot and nothing at the eighth, kinkel7-1's scan, bar 16)
                else if (l.X > a.Max(h => h.R) && TieNext(a) is { } b && TieEnd(b, at[l].Above) is var e && JoinedTo(l, e.X, e.Y, $"the tie's end by {b[0].X:F0},{b[0].Y:F0}", Debug(l)))
                {
                    foreach (var h in a) h.Tie = true;
                    used.Add(l);
                    if (Debug(l)) Console.WriteLine($"slur_l at {l.X:F0},{l.Y:F0} ({l.Score:F2}) by the note at {a[0].X:F0}: no end after it, the arc runs to the next chord, the same notes at {b[0].X:F0}: a tie");
                    continue;
                }
                if (Debug(l)) Console.WriteLine($"slur_l at {l.X:F0},{l.Y:F0} ({l.Score:F2}) by the note at {a[0].X:F0}: no end after it, {(runs ? "runs on into the next system" : "dropped")}");
            }
            // a slur from the system before: its second piece's start, after the clef, the detector
            // mostly misses (a rare shape), but the part's staff in that system ran one on to its edge.
            // Only the staff's first end can close it, one no start before it on its side has taken
            if (hangsLeft || carried.GetValueOrDefault(st.Part))
            {
                var r = rights.FirstOrDefault(Sure);
                if (r != null && !used.Contains(r) && !lefts.Any(l => Sure(l) && l.X < r.X && at[l].Above == at[r].Above))
                {
                    used.Add(r);
                    foreach (var h in at[r].Chord) h.SlurEnd = true;
                    if (Debug(r)) Console.WriteLine($"slur_r at {r.X:F0},{r.Y:F0} ({r.Score:F2}): ends a slur from the system before");
                }
            }
            carried[st.Part] = ranOn;
            // a sure end no start took takes a weak start: the last before it on its side, after the
            // last sure end there (one seen as a tie's start, a short arc, from no farther than a sure one)
            foreach (var r in rights.Where(r => Sure(r) && !used.Contains(r)))
            {
                double bx = X(r);
                double last = rights.Where(o => Sure(o) && o != r && at[o].Above == at[r].Above && X(o) < bx).Select(X).DefaultIfEmpty(double.MinValue).Max();
                var l = lefts.Where(l => !Sure(l) && !used.Contains(l) && at[l].Chord != at[r].Chord && X(l) < bx && X(l) > last && at[l].Above == at[r].Above
                                         && (!fromTie.Contains(l) || bx - X(l) < 12 * s)
                                         && !lefts.Any(o => Sure(o) && at[o].Above == at[r].Above && X(o) > X(l) && X(o) < bx) && !TiePartner(l, r) && !Bracket(l, r) && !Fermata(l, r) && (r.X - l.X <= FarSlur * s || Joined(r, l)))
                             .OrderBy(l => l.X).LastOrDefault();
                if (l != null) { used.Add(l); Pair(l, r); }
            }
            // a weak start and a weak end on the two notes a tie joins, the arc's ink running from the one to
            // the other, are the tie's: an old print's tie from a dotted quarter to its eighth, slur_l 0.30 and
            // slur_r 0.35 under the two, kinkel7-1's scan, bar 19. Two weak ends make no slur; a tie between
            // the same notes, nothing between, asks only that an arc run between them
            foreach (var l in lefts.Where(l => !Sure(l) && !used.Contains(l)))
            {
                var a = at[l].Chord;
                if (l.X <= a.Max(h => h.R) || TieNext(a) is not { } b) continue;
                var r = rights.Where(r => !Sure(r) && !used.Contains(r) && at[r].Chord == b && at[r].Above == at[l].Above).MaxBy(r => r.Score);
                // (not a fermata's two edges over the second of two like chords, kinkel7-1's scan's last bar, nor a bracket's)
                if (r == null || Fermata(l, r) || Bracket(l, r) || !Joined(l, r)) continue;
                used.Add(l); used.Add(r);
                foreach (var h in a) h.Tie = true;
                if (Debug(l) || Debug(r)) Console.WriteLine($"weak {l.Class} at {l.X:F0},{l.Y:F0} ({l.Score:F2}) and slur_r at {r.X:F0},{r.Y:F0} ({r.Score:F2}) on the next chord, the same notes at {b[0].X:F0}, the arc's ink between them: a tie");
            }
            // a start with no end seen, or an end with no start, is the tie of its chord's outer head on the
            // arc's side when the arc's ink runs on to the next head at that pitch, or back from the one
            // before: an old print's held note on one stem with the inner voice's eighths it sounds over,
            // dotted, tied to its own eighth in the chord three eighths on (kinkel7-1's scan, bar 17: c'''4.~
            // over <c'' f''>8 twice, slur_l 0.38 past the dot and nothing seen at the end; bar 19: d'''4.~,
            // nothing seen at the start and slur_r 0.46 before the end). A tie's, not a slur's, when the
            // heads between all lie a space and a half and more on the far side of the arc, none at its own
            // pitch: a slur from a note over lower notes back to it is the same shape otherwise. Not a
            // start the detector took for a tie's: DetectedTies judged those by the next head at the pitch
            foreach (var t in lefts.Concat(rights).Where(t => !used.Contains(t) && !fromTie.Contains(t)))
            {
                bool start = t.Class != "slur_r";
                var (c, above) = at[t];
                var h = above ? c.MinBy(o => o.Y) : c.MaxBy(o => o.Y);
                // (leaving the head from its side, as a tie does, or coming in before the head it joins)
                if (start ? t.X <= h.R : t.X >= h.L) continue;
                int dir = start ? 1 : -1;
                var n = ps.Heads.Where(o => o.St == st && o.Pos == h.Pos && (o.X - h.X) * dir > 0.5 * s && (o.X - h.X) * dir < 20 * s).MinBy(o => (o.X - h.X) * dir);
                if (n == null) continue;
                var (from, to) = start ? (h, n) : (n, h);
                if (from.Tie || to.Acc != null) continue;
                // (over notes: the next chord at the pitch with nothing between is the rules' case above, with
                // both ends or a sure one; a weak start alone, four spaces from a chord like its own, was a
                // fermata's left edge over the left hand's last two chords, kinkel7-1's scan, bar 23)
                var between = ps.Heads.Where(o => o.St == st && o.X > from.X + 0.5 * s && o.X < to.X - 0.5 * s).ToList();
                if (between.Count == 0 || between.Any(o => above ? o.Y < h.Y + 1.5 * s : o.Y > h.Y - 1.5 * s)) continue;
                var e = start ? TieEnd(new List<Head> { n }, above) : TieStart(n, above);
                if (!JoinedTo(t, e.X, e.Y, $"the tie's {(start ? "end" : "start")} by {n.X:F0},{n.Y:F0}", Debug(t), 2.0, above)) continue;
                used.Add(t);
                from.Tie = true;
                if (Debug(t)) Console.WriteLine($"{t.Class} at {t.X:F0},{t.Y:F0} ({t.Score:F2}) by the head at {h.X:F0},{h.Y:F0} pos {h.Pos}, no {(start ? "end" : "start")} seen: the arc's ink runs {(start ? "on to" : "back from")} the {(start ? "next" : "last")} head at its pitch, {(to.X - from.X) / s:F1} spaces, over nothing near it: a tie");
            }
            // (a sure end left over, no start on its staff pairing with it, or a sure start with no end: the slur's other
            // end is on the neighbouring staff, where the detector mostly misses it -- MuseScore's arc from the right hand's
            // line down to its last two notes drawn on the left hand's staff, Schumann's Op.25-15 -- or unseen on this one.
            // Noted on the heads for the reader to weigh (Reader.HandedGroups); nothing is written for it)
            foreach (var t in lefts.Concat(rights).Where(t => Sure(t) && !used.Contains(t) && !fromTie.Contains(t)))
                foreach (var h in at[t].Chord) { if (t.Class == "slur_r") h.SlurEndAcross = true; else h.SlurStartAcross = true; }
            void Pair(Detection l, Detection r)
            {
                var (a, above) = at[l];
                double ax = a.Average(h => h.X);
                // (an arc from a chord to the next one on the staff, the same notes and nothing between, is
                // a tie whatever the detector called its two ends: a slur joins different notes. Brandt's
                // song as MuseScore draws it, a''4.( a''8) four times for a''4.~ a''8. Not when the second
                // has a sign: the same place, another note, Brandt's other song's a'8( ais'8). A second
                // voice's heads between, stemmed the other way on the far side, are no note between.
                // And only an arc that leaves the head from its side, as a tie does: Lily#'s slur from a
                // stemmed note starts over it, the slurs suite's c4.( c8), 11 of 13 there 0.05-0.8 space
                // inside the head's right edge, the real prints' ties 0.3-0.6 past it. Not by the arc's
                // height over the heads: an engraver's arc from a whole note to the next at the same pitch,
                // bowed high over the bar line between them, 1.5 spaces over its heads where it leaves them
                // (kinkel19-6's scan, bar 13, "Meer" sung on through the two, a slur in the truth), is the
                // shape of MuseScore's ties over a bar, 1.5-1.7 spaces over the heads a space before the
                // second (kinkel7-1's ms, bar 20, ties in the truth): tried both by the detector's ends and
                // by the ink, 2026-11-01, and dropped)
                bool OtherVoice(Head o) => a[0].HasStem && o.HasStem && o.Up != a[0].Up && (a[0].Up ? o.Y > a.Max(h => h.Y) : o.Y < a.Min(h => h.Y));
                if (!fromTie.Contains(l) && at[r].Chord is var b && b.Count == a.Count && l.X > a.Max(h => h.R)
                    && a.Select(h => h.Pos).Order().SequenceEqual(b.Select(h => h.Pos).Order()) && b.All(h => h.Acc == null)
                    && !ps.Heads.Any(o => o.St == st && !a.Contains(o) && !b.Contains(o) && !OtherVoice(o) && o.X > a.Max(h => h.X) + 0.5 * s && o.X < b.Min(h => h.X) - 0.5 * s))
                {
                    used.Add(r);
                    foreach (var h in a) h.Tie = true;
                    if (Debug(l) || Debug(r)) Console.WriteLine($"slur from {ax:F0},{a[0].Y:F0} ({l.Score:F2}) to the next chord, the same notes at {b[0].X:F0}: a tie");
                }
                else if (Sung(st, a, at[r].Chord) is int n)
                {
                    used.Add(r);
                    if (Debug(l) || Debug(r)) Console.WriteLine($"slur from {ax:F0},{a[0].Y:F0} ({l.Score:F2}) to {at[r].Chord[0].X:F0}: each of its {n} notes has its own syllable, a tuplet's arc");
                }
                else
                {
                    used.Add(r);
                    foreach (var h in a) h.SlurStart = true;
                    foreach (var h in at[r].Chord) h.SlurEnd = true;
                    if (Debug(l) || Debug(r)) Console.WriteLine($"slur from {ax:F0},{a[0].Y:F0} ({l.Score:F2}) to {at[r].Chord[0].X:F0},{at[r].Chord[0].Y:F0} ({r.Score:F2}), {(above ? "over" : "under")}");
                }
            }
        }
    }

    /// <summary>
    /// The syllables of a song's words under a staff (their x spans), or none: pieces of ink a letter's
    /// height (half a space to two and a half) and up to three spaces wide, a space to eight under the
    /// staff (short of the next staff below), with no head and nothing the detector marked in them; the
    /// row is the letters level with the line most of them stand on, and a syllable the letters within
    /// half a space of each other (a hyphen or an extender is too thin for a letter). None for a staff
    /// with fewer than six syllables in its row, or with a second row of six letters and more (verses).
    /// </summary>
    static List<(double L, double R)> Syllables(PageSymbols ps, Img d, Staff st, double s)
    {
        var none = new List<(double L, double R)>();
        double below = ps.Heads.Select(h => h.St).Distinct().Where(o => o != st && o.System == st.System && o.Top > st.Bottom).Select(o => o.Top - s).DefaultIfEmpty(st.Bottom + 8 * s).Min();
        int x0 = Math.Max(0, st.Left), x1 = Math.Min(d.W - 1, st.Right), y0 = (int)(st.Bottom + s), y1 = (int)Math.Min(d.H - 1, Math.Min(below, st.Bottom + 8 * s));
        int w = x1 - x0 + 1, h = y1 - y0 + 1;
        if (w < 10 * s || h < 2 * s) return none;
        var (_, comps) = Components.Label(w, h, i => d[x0 + i % w, y0 + i / w], eight: true);
        var marked = ps.Heads.Select(q => (q.X, q.Y))
            .Concat((ps.Detections ?? []).Where(t => t.Score > Detector.Threshold).Select(t => (t.X, t.Y))).ToList();
        bool Holds(Comp c) => marked.Any(p => p.X >= x0 + c.MinX - 1 && p.X <= x0 + c.MaxX + 1 && p.Y >= y0 + c.MinY - 1 && p.Y <= y0 + c.MaxY + 1);
        var letters = comps.Skip(1).Where(c => c.H >= 0.5 * s && c.H <= 2.5 * s && c.W <= 3 * s && c.MinY > 0 && c.MaxY < h - 1 && !Holds(c)).ToList();
        if (letters.Count < 12) return none;
        // (the line they stand on: the bottom most letters share, within a fifth of a space; a p's or a
        // y's tail hangs below it, an accent or an apostrophe stands over it)
        int foot = letters.Select(c => c.MaxY).MaxBy(y => letters.Count(o => Math.Abs(o.MaxY - y) <= 0.2 * s));
        var row = letters.Where(c => c.MaxY >= foot - 0.2 * s && c.MaxY <= foot + 1.2 * s && c.MinY < foot - 0.3 * s && c.MinY > foot - 2.5 * s).OrderBy(c => c.MinX).ToList();
        // (one verse only: under several, a slur may be one verse's, a dashed one -- Kinkel's Op.19-4)
        var rest = letters.Except(row).ToList();
        if (rest.Count >= 6 && rest.Max(c => rest.Count(o => Math.Abs(o.MaxY - c.MaxY) <= 0.2 * s)) >= 6) return none;
        var syl = new List<(double L, double R)>();
        foreach (var c in row)
            if (syl.Count > 0 && x0 + c.MinX - syl[^1].R < 0.5 * s) syl[^1] = (syl[^1].L, Math.Max(syl[^1].R, x0 + c.MaxX + 1));
            else syl.Add((x0 + c.MinX, x0 + c.MaxX + 1));
        return syl.Count >= 6 ? syl : none;
    }

    /// <summary>
    /// The ends of a chord's ties dealt to its heads, both lists in order of height (y): each head
    /// takes one end at most and the order is kept, in the deal with the least height between end
    /// and head in all. Null when there are more ends than heads. (SymEval deals the truth's ties
    /// the same way.)
    /// </summary>
    public static int[] Deal(IReadOnlyList<double> ends, IReadOnlyList<double> heads)
    {
        int n = ends.Count, m = heads.Count;
        if (n > m) return null;
        var cost = new double[n + 1, m + 1];
        var take = new bool[n + 1, m + 1];
        for (int i = n; i >= 0; i--)
            for (int j = m; j >= 0; j--)
            {
                if (i == n) { cost[i, j] = 0; continue; }
                if (m - j < n - i) { cost[i, j] = double.MaxValue; continue; }
                double skip = cost[i, j + 1];
                double with = cost[i + 1, j + 1] == double.MaxValue ? double.MaxValue : Math.Abs(ends[i] - heads[j]) + cost[i + 1, j + 1];
                take[i, j] = with <= skip;
                cost[i, j] = Math.Min(skip, with);
            }
        var deal = new int[n];
        for (int i = 0, j = 0; i < n; j++)
            if (take[i, j]) { deal[i] = j; i++; }
        return deal;
    }

    /// <summary>A dark run longer than two spaces and of a beam's thickness through the rows just above and below
    /// the head. (A ledger line is one of that length but thin: the lower of a whole note's two ledger lines on a
    /// scan, Op.27-2's low G sharp and B, dropped in six bars.)</summary>
    static bool BeamAround(Img ink, Head h, double s)
    {
        foreach (double dy in new[] { -0.5 * s, 0.5 * s })
        {
            int y = (int)(h.Y + dy), x = (int)h.X;
            if (!ink[x, y]) continue;
            int a = x, b = x;
            // (a ledger line stands outside the staff only: there a thin run is one)
            bool ledger = Math.Abs(h.Pos) >= 5;
            while (ink[a - 1, y] && x - a < 3 * s) a--;
            while (ink[b + 1, y] && b - x < 3 * s) b++;
            if (b - a <= 2.4 * s) continue;
            // (beside the head, where the ink is the run's alone)
            int cx = b - x >= x - a ? (b + x) / 2 + (int)(0.5 * s) : (a + x) / 2 - (int)(0.5 * s);
            cx = Math.Clamp(cx, a, b);
            int up = y, down = y;
            while (up > 0 && ink[cx, up - 1] && y - up < s) up--;
            while (down < ink.H - 1 && ink[cx, down + 1] && down - y < s) down++;
            if (DebugAt.HasValue && Math.Abs(h.X - DebugAt.Value.X) < 2 * s && Math.Abs(h.Y - DebugAt.Value.Y) < 3 * s)
                Console.WriteLine($"beam around {h.X:F0},{h.Y:F0}: dy {dy / s:F2} run {(b - a) / s:F2} thick {(down - up + 1) / s:F2} s {s:F1}");
            if (!ledger || down - up + 1 >= 0.45 * s) return true;
        }
        return false;
    }
}
