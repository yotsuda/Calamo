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
            bool tied = false;
            string why = "no stroke";
            // the stroke starts just past the head's edge (a short tie between close chords a little
            // later), within a space of the head's height: every thin stroke there is tried, the one
            // nearest the head's height first
            for (int x0 = xr + (int)(0.15 * s); x0 < Math.Min(xr + 0.8 * s, goal) && !tied; x0++)
                // (a stroke at the head's own height is a ledger line running on to the next head, not a tie)
                foreach (var start in Runs(x0).Where(r => r.B - r.T + 1 <= thinMax && Math.Abs((r.T + r.B) / 2.0 - h.Y) <= startNear
                                                          && Math.Abs((r.T + r.B) / 2.0 - h.Y) >= 0.3 * s)
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
                // (the bar line: the detector's, or, where the ties over it dim it for the detector,
                // tabi's 4th bar line at 0.41, a column dark through the staff clear of the two notes' stems)
                double mid = (st.Lines[0].Top + st.Lines[^1].Bot) / 2.0;
                bool barBetween = ps.Detections.Any(u => u.Class == "barline" && u.Score >= TrustScore && u.X > a.R && u.X < b.L && Math.Abs(u.Y - mid) < 3 * s)
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
        foreach (var u in rights)
        {
            var b = At(u, +1);
            if (b == null) continue;
            var a = ps.Heads.Where(o => o != b && o.St == b.St && o.Pos == b.Pos && b.X - o.X > 0.5 * s && b.X - o.X < 12 * s).OrderByDescending(o => o.X).FirstOrDefault();
            if (a == null || a.Tie) continue;
            if (DebugAt.HasValue && Math.Abs(u.X - DebugAt.Value.X) < 3 * s && Math.Abs(u.Y - DebugAt.Value.Y) < 3 * s)
                Console.WriteLine($"detected tie_r at {u.X:F0},{u.Y:F0} ({u.Score:F2}) at head {b.X:F0},{b.Y:F0} pos {b.Pos}: no tie_l seen, tied from the head before at {a.X:F0},{a.Y:F0}");
            a.Tie = a.TieByEnd = true;
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
        // (nor a weak start by a grace note: Lily#'s slur from an acciaccatura to its note, which
        // the start would give to the note before it, gen-grace's basic002)
        ends.RemoveAll(t => !Sure(t) && t.Class == "slur_l" && ps.Graces.Any(g => Math.Abs(g.X - t.X) < 1.5 * s && Math.Abs(g.Y - t.Y) < 2.5 * s));
        // a tie's start that tied nothing (DetectedTies: no head at its place after it, or a slur's
        // end there) is a slur's start the detector took for a tie: the short arc from a chord's top
        // note down to the next note, tabi's 8th bar. It starts a slur only to an end found for it
        var fromTie = ps.Detections.Where(t => t.Class == "tie_l" && t.Score >= TrustScore
            && !ps.Heads.Any(h => h.Tie && Math.Abs(h.Y - t.Y) <= 1.25 * s && t.X - h.X > -0.3 * s && t.X - h.X < 2.0 * s)).ToHashSet();
        ends.AddRange(fromTie);
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
            else if (DebugAt.HasValue && Math.Abs(t.X - DebugAt.Value.X) < 3 * s && Math.Abs(t.Y - DebugAt.Value.Y) < 3 * s)
                Console.WriteLine($"detected {t.Class} at {t.X:F0},{t.Y:F0} ({t.Score:F2}): by no note");
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
        bool Debug(Detection t) => DebugAt.HasValue && Math.Abs(t.X - DebugAt.Value.X) < 3 * s && Math.Abs(t.Y - DebugAt.Value.Y) < 3 * s;
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
            // itself at 0.27-0.42)
            // And a bracket with no number, MuseScore's after the first two: a straight thin line from
            // one end to the other through the middle of the span, where a slur bulges away from its
            // notes (the middle fifth of it, ink within 0.15 space of the line between the two ends
            // in seven of ten columns, and none 0.3-2 spaces beyond it in half of them)
            bool Bracket(Detection l, Detection r)
            {
                if (ps.Detections.Any(u => u.Class.StartsWith("tuplet_") && u.Score >= TrustScore
                    && u.X > l.X - s && u.X < r.X + s && Math.Abs(u.Y - (l.Y + r.Y) / 2) < 1.5 * s)) return true;
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
            foreach (var l in lefts.Where(Sure))
            {
                var a = at[l].Chord;
                double ax = X(l);
                // (a start the detector took for a tie's is a short arc, to a note or two on: it takes
                // no end bars away, which is another slur's, kinkel19-2's 42nd bar, the Zigeuner's 18th)
                var r = rights.Where(r => Sure(r) && !used.Contains(r) && at[r].Chord != a && X(r) > ax && at[r].Above == at[l].Above
                                          && (!fromTie.Contains(l) || X(r) - ax < 12 * s))
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
                var r = rights.Where(r => !Sure(r) && !used.Contains(r) && at[r].Chord != at[l].Chord && X(r) > ax && X(r) < next && at[r].Above == at[l].Above && !Bracket(l, r))
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
                if (runs) { foreach (var h in a) h.SlurStart = true; ranOn = true; }
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
            // last sure end there
            foreach (var r in rights.Where(r => Sure(r) && !used.Contains(r)))
            {
                double bx = X(r);
                double last = rights.Where(o => Sure(o) && o != r && at[o].Above == at[r].Above && X(o) < bx).Select(X).DefaultIfEmpty(double.MinValue).Max();
                var l = lefts.Where(l => !Sure(l) && !used.Contains(l) && at[l].Chord != at[r].Chord && X(l) < bx && X(l) > last && at[l].Above == at[r].Above
                                         && !lefts.Any(o => Sure(o) && at[o].Above == at[r].Above && X(o) > X(l) && X(o) < bx) && !Bracket(l, r))
                             .OrderBy(l => l.X).LastOrDefault();
                if (l != null) { used.Add(l); Pair(l, r); }
            }
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
                // inside the head's right edge, the real prints' ties 0.3-0.6 past it)
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

    /// <summary>A dark run longer than two spaces through the rows just above and below the head.</summary>
    static bool BeamAround(Img ink, Head h, double s)
    {
        foreach (double dy in new[] { -0.5 * s, 0.5 * s })
        {
            int y = (int)(h.Y + dy), x = (int)h.X;
            if (!ink[x, y]) continue;
            int a = x, b = x;
            while (ink[a - 1, y] && x - a < 3 * s) a--;
            while (ink[b + 1, y] && b - x < 3 * s) b++;
            if (b - a > 2.4 * s) return true;
        }
        return false;
    }
}
