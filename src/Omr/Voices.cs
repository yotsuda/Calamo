namespace Omr;

/// <summary>
/// Splits a staff's bar into voices: lines of events that follow one another in time. The page gives
/// each event its x (staves are aligned, so x tells time within the bar), its stem, and its heads'
/// places on the staff. A stem against the way its heads would have it alone (up for a head on or over
/// the middle line, down for one under it) is a voice's mark: up, the first voice; down, the second,
/// as engravers force them where two voices share a staff. A stem its heads would have anyway says
/// nothing: the note may be alone at its time, in either voice. Events in one column sound together,
/// so they are different voices'; the notes of one beam are one voice's. Of the ways to lay the events
/// into lines that fit the bar, the one with the fewest lines is taken; then the fewest holes inside a
/// line; then the fewest beamed figures broken between lines; then the first voice filling the bar
/// (engraving programs keep it full, and the lower voices may stop); then the fewest leaps inside a
/// line; then the fewest stems against their voice's way; then rests on their voice's side. The lines
/// come out in Lily#'s order (the first voice's stems up, the second's down, the third's up ...), and
/// an event whose stem goes the other way is marked (Ev.StemForced), so that the .lys draws the page:
/// the chords a melody goes on with after the lower voice stopped, kinkel7-1's bar 13.
/// </summary>
static class Voices
{
    /// <param name="s">the staff space, when known: a rest stacked with a note is then another voice however short the line;
    /// events within 0.6 of it stand in one column (8 px with none)</param>
    public static List<List<Ev>> Separate(List<Ev> evs, int barTicks, double staffMid, double s = 0)
    {
        foreach (var e in evs) e.StemForced = 0;
        // one line that fills the bar is one voice; more than a bar's worth is several
        // (a stemless whole note over a stem-down line is two voices, too)
        int total = evs.Sum(e => e.Ticks);
        if (total <= barTicks && !Stacked(evs, s)) return new List<List<Ev>> { evs };
        // (events that sound together stand at one x, a rest's glyph a fifth of a space or so off a head's: a column.
        // Not wider: a dotted eighth's sixteenth stands half a space from a triplet's second eighth, gen-threes' threes008)
        double col = s > 0 ? 0.35 * s : 6;
        var units = Units(evs, staffMid, s);
        // (no staff geometry: a trial reading asking whether the stems' lines overfill the bar, Reader.UnprintedTriplets
        // and the like -- the stems alone answer, as before)
        if (s <= 0) return Marked(ByStems(units, barTicks, staffMid, s));
        var search = new Search(units, barTicks, col);
        // (--debug-at x,y in the bar, on the staff: the units and the laying chosen, and every laying tried that did not fit)
        bool debug = Symbols.DebugAt.HasValue && evs.Count > 0 && (Symbols.DebugPage == null || Symbols.DebugPage == Symbols.DebugPageNow)
                     && (s <= 0 || Math.Abs(Symbols.DebugAt.Value.Y - staffMid) < 6 * s)
                     && Symbols.DebugAt.Value.X >= evs.Min(e => e.X) - 4 * Math.Max(s, 8) && Symbols.DebugAt.Value.X <= evs.Max(e => e.X) + 4 * Math.Max(s, 8);
        search.Trace = debug;
        var lines = search.Run() ?? ByStems(units, barTicks, staffMid, s);
        if (debug)
        {
            Console.WriteLine($"voices of the bar at x {evs.Min(e => e.X):F0}-{evs.Max(e => e.X):F0} ({barTicks} ticks, {units.Count} units, space {s:F1}, {search.Nodes} tries{(search.Best == null ? ", none fit: by the stems" : "")}):");
            foreach (var u in units)
                Console.WriteLine($"  x {u.X:F0} {u.Ev.Show()}: stem {u.Stem}{(u.Forced ? " forced" : "")}, pos {u.Pos:F1}{(u.Figure != null ? $", beamed ({u.Figure.Notes.Count})" : "")} -> voice {lines.FindIndex(l => l.Contains(u)) + 1} at {u.T}");
            if (search.Best != null) Console.WriteLine($"  cost: beam holes {search.Best[0]}, lines {search.Best[1]}, short {search.Best[2]}, holes {search.Best[3]}, broken figures {search.Best[4]}, first voice short {search.Best[5]}, leaps {search.Best[6]}, stems against {search.Best[7]}, rests against {search.Best[8]}");
        }
        return Marked(lines);
    }

    /// <summary>The voices' events, a stem against its voice's way marked so (the page is drawn again).</summary>
    static List<List<Ev>> Marked(List<List<Unit>> lines)
    {
        var result = new List<List<Ev>>();
        for (int k = 0; k < lines.Count; k++)
        {
            var v = lines[k].Select(u => u.Ev).ToList();
            if (lines.Count > 1) foreach (var e in v) if (e.Stem != 0 && e.Stem != Default(k)) e.StemForced = e.Stem;
            result.Add(v);
        }
        return result;
    }

    /// <summary>The way Lily# turns a voice's stems: the first voice's up, the second's down, and so on by turns.</summary>
    static int Default(int slot) => slot % 2 == 0 ? 1 : -1;

    /// <summary>One event as the search lays it: a note (a chord), a rest.</summary>
    sealed class Unit
    {
        public Ev Ev;
        public double X => Ev.X;
        public int Ticks => Ev.Ticks;
        public int Stem => Ev.Stem;
        public bool Rest => Ev.IsRest;
        public bool FullBar => Ev.FullBar;
        public bool Forced;         // the stem against its heads' own way (its figure's heads): a voice's mark
        public double Pos;          // its heads' mean place in steps from the middle line (a rest's height there)
        public double Pitch;        // its notes' mean pitch in diatonic steps (the same across a change of clef): its register
        public Figure Figure;       // the beamed figure it is a note of, or null
        public Line Line; public int T;   // the search's: its line and its time in the bar
    }

    /// <summary>The notes on one beam, their stems one way: one voice's, laid together.</summary>
    sealed class Figure
    {
        public List<Unit> Notes = new();
        public int Value => Notes[0].Ticks;             // its first note's length (a figure's notes are mostly alike): its continuity
        public double Pitch => Notes.Average(n => n.Pitch);
        public bool Forced => Notes[0].Forced;
        public int T => Notes[0].T;
        public int End => Notes[^1].T + Notes[^1].Ticks;
        public Line Line => Notes[0].Line;
    }

    static List<Unit> Units(List<Ev> evs, double staffMid, double s)
    {
        var units = new List<Unit>();
        var figures = new Dictionary<(int Beam, int Stem), Figure>();
        foreach (var e in evs.OrderBy(e => e.X))
        {
            var u = new Unit { Ev = e };
            if (e.IsRest) u.Pos = s > 0 ? (staffMid - e.Y) / (s / 2) : 0;
            else { u.Pos = (e.PosLo + e.PosHi) / 2.0; u.Pitch = e.Pitches.Average(p => p.Diatonic); }
            if (!e.IsRest && e.Beam != 0 && e.Stem != 0)
            {
                if (!figures.TryGetValue((e.Beam, e.Stem), out var f)) figures[(e.Beam, e.Stem)] = f = new Figure();
                f.Notes.Add(u); u.Figure = f;
            }
            units.Add(u);
        }
        // the way a stem would go by itself (a figure's, as one): away from the head farthest from the middle
        // line; as far each way, away from the more heads; still even, down (a head on the middle line, too)
        foreach (var group in units.Where(u => !u.Rest && u.Stem != 0).GroupBy(u => u.Figure ?? (object)u))
        {
            var heads = group.Select(u => u.Ev).ToList();
            int hi = heads.Max(e => e.PosHi), lo = heads.Min(e => e.PosLo);
            int above = heads.Count(e => e.PosLo + e.PosHi > 0), below = heads.Count(e => e.PosLo + e.PosHi < 0);
            int natural = hi > -lo ? -1 : -lo > hi ? 1 : above > below ? -1 : below > above ? 1 : -1;
            foreach (var u in group) u.Forced = u.Stem != natural;
        }
        return units;
    }

    sealed class Line
    {
        public List<Unit> Units = new();
        public int End, Gaps;                   // where its last unit ends; the time left empty between its units
        public int BeamHoles;                   // its notes of a beam that waited for another line's note in their column (a rest of theirs unread, or the laying is wrong)
        public int Forced, ForcedDir, StemSum;  // its stems' way: a forced unit's, else the sum of its stems
        public int Dir => Forced > 0 ? ForcedDir : Math.Sign(StemSum);
        public double Pos => Units.Count == 0 ? 0 : Units.Average(u => u.Pos);
        public bool Full(int bar) => Units.Count > 0 && Units[0].T == 0 && Gaps == 0 && End == bar;
    }

    /// <summary>
    /// Lays the units into lines, column by column from the left. A column's units go into distinct lines
    /// (a note of a figure into its figure's), and the column's time is the latest of where those lines stand
    /// and where the columns before it began. A unit that would run past the bar's end is not placed so. The
    /// best laying by the costs is kept (Evaluate); past a budget of tries, the best so far.
    /// </summary>
    sealed class Search
    {
        readonly List<Unit> units;
        readonly int bar;
        readonly double col, near;
        readonly List<List<Unit>> columns = new();
        readonly List<Line> lines = new();
        int nodes, tLeft;
        const int Budget = 20000, MaxLines = 4;
        long[] bestCost;
        int[] bestSlot, bestT;
        public int Nodes => nodes;
        public long[] Best => bestCost;
        public bool Trace;

        public Search(List<Unit> units, int bar, double col)
        {
            this.units = units; this.bar = bar; this.col = col; near = col * 1.5 / 0.6;
            // a full-bar rest stands mid-bar on the page and sounds from its start: a line of its own
            foreach (var u in units.Where(u => u.FullBar)) { var l = new Line(); Place(l, u, 0); lines.Add(l); }
            foreach (var u in units.Where(u => !u.FullBar))
            {
                if (columns.Count > 0 && u.X - columns[^1][0].X <= col) columns[^1].Add(u);
                else columns.Add(new List<Unit> { u });
            }
            // (the most bound first: a note that goes on a beam, its line settled; then a forced stem, then a note, then a
            // rest, which goes wherever -- so that the column's freer units see the lines the bound ones took and open
            // their own: kinkel19-5's bar 30, the third voice's quarter beside the lower voice's second eighth; kinkel7-1's
            // bar 13, the song's eighth over the second voice's rest)
            for (int i = 0; i < columns.Count; i++)
                columns[i] = columns[i].OrderBy(u => u.Figure != null && u.Figure.Notes[0] != u ? 0 : u.Forced ? 1 : !u.Rest ? 2 : 3).ToList();
        }

        public List<List<Unit>> Run()
        {
            Dfs(0);
            if (bestSlot == null) return null;
            var slots = new List<List<Unit>>();
            for (int i = 0; i < units.Count; i++)
            {
                while (slots.Count <= bestSlot[i]) slots.Add(new List<Unit>());
                units[i].T = bestT[i];
                slots[bestSlot[i]].Add(units[i]);
            }
            foreach (var v in slots) v.Sort((a, b) => a.T != b.T ? a.T.CompareTo(b.T) : a.X.CompareTo(b.X));
            return slots;
        }

        void Place(Line l, Unit u, int t)
        {
            if (l.Units.Count > 0) l.Gaps += t - l.End;
            // (a figure's notes follow one another without a hole, rests of their line between them at most: a note of
            // the beam that waits for another line's note in its column tells of a wrong laying -- kinkel19-5's bar 25,
            // the third voice's quarter under the beam's second eighth -- or of a rest under the beam unread, lang36-3's bar 10)
            if (u.Figure != null && u.Figure.Notes[0] != u && l.End != t) l.BeamHoles++;
            l.Units.Add(u); l.End = t + u.Ticks;
            if (u.Forced) { l.Forced++; l.ForcedDir = u.Stem; } else l.StemSum += u.Stem;
            u.Line = l; u.T = t;
        }

        void Unplace(Line l, Unit u, (int End, int Gaps, int Forced, int StemSum, int BeamHoles) was)
        {
            l.Units.RemoveAt(l.Units.Count - 1);
            (l.End, l.Gaps, l.Forced, l.StemSum, l.BeamHoles) = was;
            u.Line = null;
        }

        void Dfs(int ci)
        {
            if (++nodes > Budget) return;
            // (no better than the best: more holes in beams, or as many and more lines, or as many lines and more holes)
            if (bestCost != null)
            {
                long beamHoles = lines.Sum(l => l.BeamHoles);
                if (beamHoles > bestCost[0] || beamHoles == bestCost[0] && (lines.Count > bestCost[1] || lines.Count == bestCost[1] && lines.Sum(l => l.Gaps) > bestCost[3])) return;
            }
            if (ci == columns.Count) { Evaluate(); return; }
            // (a column is no earlier than any column before it)
            int wasLeft = tLeft;
            if (ci > 0) tLeft = Math.Max(tLeft, columns[ci - 1][0].T);
            Assign(ci, 0, new List<Line>());
            tLeft = wasLeft;
        }

        // the column's units one by one into distinct lines (a forced stem to a line of its way, a figure's note to
        // its figure's), then the column's time and the next column
        void Assign(int ci, int k, List<Line> chosen)
        {
            var column = columns[ci];
            if (k == column.Count)
            {
                int t = tLeft;
                foreach (var l in chosen) t = Math.Max(t, l.End);
                for (int i = 0; i < column.Count; i++)
                    if (t + column[i].Ticks > bar) { if (Trace) Console.WriteLine($"    x {column[i].X:F0} {column[i].Ev.Show()} in line {lines.IndexOf(chosen[i]) + 1} at {t}: past the bar"); return; }
                var was = chosen.Select(l => (l.End, l.Gaps, l.Forced, l.StemSum, l.BeamHoles)).ToList();
                for (int i = 0; i < column.Count; i++) Place(chosen[i], column[i], t);
                Dfs(ci + 1);
                for (int i = column.Count - 1; i >= 0; i--) Unplace(chosen[i], column[i], was[i]);
                return;
            }
            var unit = column[k];
            if (unit.Figure?.Line is { } own)
            {
                if (chosen.Contains(own)) return;
                chosen.Add(own); Assign(ci, k + 1, chosen); chosen.RemoveAt(chosen.Count - 1);
                return;
            }
            // the lines its stem's way first, then the others, then a new line. A line whose last note stands within a
            // head's width and a half of this note cannot go on with it (no two notes of a line stand so close; a rest
            // and a note may): the other voice's note in its column, a second or a unison apart -- kinkel7-1's bar 20,
            // kinkel19-5's bar 25
            // (and a line the unit would run past the bar's end in cannot)
            var open = lines.Where(l => !chosen.Contains(l) && (!unit.Forced || l.Forced == 0 || l.ForcedDir == unit.Stem)
                                        && (l.Units.Count == 0 || l.Units[^1].Rest || unit.Rest || l.Units[^1].X < unit.X - near)
                                        && Math.Max(l.End, tLeft) + unit.Ticks <= bar)
                            .OrderBy(l => l.Dir == unit.Stem ? 0 : 1).ToList();
            foreach (var l in open)
            {
                chosen.Add(l); Assign(ci, k + 1, chosen); chosen.RemoveAt(chosen.Count - 1);
                if (nodes > Budget) return;
            }
            // a new line: the first; or for a unit no line can go on with (the lines its column's other units took, the
            // lines full, the lines whose last note stands too close: another voice for sure); or for a forced stem while
            // no line is proved its way by a forced stem of its own (a voice that way is there for sure: the lower voice's
            // eighths after the rest the upper voice opens with, kinkel19-5's bar 31; the melody that enters over the
            // sixteenths whose stems said nothing, kinkel19-1's bar 9). Not for a unit a line could hold: a bar too long
            // as read (a value misread, a triplet with no number) stays too long for its voices to fit (ByStems), which
            // the reader's other steps go by -- gen voices2003's bar 11, the lower voice's triplet read as eighths
            if (lines.Count < MaxLines && (lines.Count == 0 || open.Count == 0 || unit.Forced && !lines.Any(l => l.Forced > 0 && l.ForcedDir == unit.Stem)))
            {
                var l = new Line();
                lines.Add(l); chosen.Add(l);
                Assign(ci, k + 1, chosen);
                chosen.RemoveAt(chosen.Count - 1); lines.Remove(l);
            }
        }

        // Lily#'s order: the voices with stems up and those with stems down by turns, each kind the one that
        // fills the bar first, then from the top down. A line with no stems of its own (whole notes, rests) takes
        // the other voice's slot when one other line has stems -- the whole note over the moving lower voice is
        // the first voice, whose ornament is looked for over it, the moving voice's under it (gen-orn piano006's
        // bar 4, piano007's bar 6); the half rest over the lower voice's two quarters too (gen slurs007's bar 6) --
        // and among more lines goes by where it lies: over them, an upper voice; under them, a lower. A lone such
        // line, or one level with the others, last
        List<Line> Order()
        {
            int DirOf(Line l)
            {
                if (l.Dir != 0 || lines.Count < 2) return l.Dir;
                var others = lines.Where(o => o != l).ToList();
                if (others.Count == 1 && others[0].Dir != 0) return -others[0].Dir;
                double pos = others.Average(o => o.Pos);
                return l.Pos > pos ? 1 : l.Pos < pos ? -1 : 0;
            }
            List<Line> Of(int dir) => lines.Where(l => DirOf(l) == dir).OrderBy(l => l.Full(bar) ? 0 : 1).ThenByDescending(l => l.Pos).ToList();
            var ups = Of(1);
            var downs = Of(-1);
            var none = Of(0);
            var order = new List<Line>();
            for (int i = 0; i < Math.Max(ups.Count, downs.Count); i++)
            {
                if (i < ups.Count) order.Add(ups[i]);
                if (i < downs.Count) order.Add(downs[i]);
            }
            order.AddRange(none);
            return order;
        }

        void Evaluate()
        {
            var order = Order();
            var cost = new long[9];
            cost[0] = lines.Sum(l => l.BeamHoles);
            cost[1] = lines.Count;
            // (two voices that both fill the bar before one that stops: the reader's other steps ask whether every voice fills)
            cost[2] = lines.Count(l => !l.Full(bar));
            cost[3] = lines.Sum(l => l.Gaps);
            // a beamed figure whose stems say nothing, met end to start by one of the same value in its register in another
            // line: a line broken between voices (kinkel19-1's bar 9, sixteenths through the bar, the first half's stems up
            // and the second's down under the melody that enters there). Not where the second figure goes on a voice that
            // forced stems prove, its own stems saying nothing: the figure a melody goes on with where the lower voice's
            // figures stop belongs to the melody, the first voice, full (kinkel7-1's bars 14 and 17)
            // (and not where the figure goes on in its own line as well: the melody's figure that starts where the lower
            // voice's chords go on in chords, kinkel7-1's bar 18)
            var figures = units.Where(u => u.Figure != null && u.Figure.Notes.Count > 1).Select(u => u.Figure).Distinct().ToList();
            foreach (var f in figures.Where(f => !f.Forced))
            {
                var next = figures.Where(g => g.Value == f.Value && Math.Abs(g.Pitch - f.Pitch) <= 2 && f.End == g.T).ToList();
                if (next.Count > 0 && !next.Any(g => g.Line == f.Line) && next.Any(g => g.Forced || g.Line.Forced == 0)) cost[4]++;
            }
            cost[5] = order[0].Full(bar) ? 0 : 1;
            // a voice keeps to its register: its leaps past a fourth, from each note to the one before it in its line, in all
            // (a bass's half note and the quarter on its pitch, over the eighths a sixth up: kinkel19-2's bar 21. Steps cost
            // nothing: a melody laid in two lines has fewer of them, not a better voice. Nor a figure's own leaps)
            foreach (var l in lines)
                foreach (var (a, b) in l.Units.Where(u => !u.Rest).Zip(l.Units.Where(u => !u.Rest).Skip(1)))
                    if (a.Figure == null || a.Figure != b.Figure) cost[6] += (long)(10 * Math.Max(0, Math.Abs(b.Pitch - a.Pitch) - 3));
            for (int k = 0; k < order.Count; k++)
                foreach (var u in order[k].Units)
                {
                    if (u.Stem != 0 && u.Stem != Default(k)) cost[7]++;
                    if (u.Rest && !u.FullBar && (u.Pos > 0.5 && Default(k) < 0 || u.Pos < -0.5 && Default(k) > 0)) cost[8]++;
                }
            if (bestCost != null)
                for (int i = 0; i < cost.Length; i++)
                {
                    if (cost[i] < bestCost[i]) break;
                    if (cost[i] > bestCost[i]) return;
                    if (i == cost.Length - 1) return;
                }
            bestCost = cost;
            bestSlot = new int[units.Count]; bestT = new int[units.Count];
            for (int i = 0; i < units.Count; i++) { bestSlot[i] = order.IndexOf(units[i].Line); bestT[i] = units[i].T; }
        }
    }

    /// <summary>
    /// No laying fits the bar (a value misread, a triplet with no number: the bar is too long as it stands, and the
    /// reader's other steps go by which voice is too long) or none was found within the budget: the stems alone, as
    /// before -- stem-up events the first voice, stem-down the second; rests and stemless notes where they complete a
    /// voice's time, one above the middle line leaning to the first.
    /// </summary>
    static List<List<Unit>> ByStems(List<Unit> units, int barTicks, double staffMid, double s)
    {
        var a = units.Where(u => u.Stem > 0).ToList();
        var b = units.Where(u => u.Stem < 0).ToList();
        var free = units.Where(u => u.Stem == 0).ToList();
        int sa = a.Sum(u => u.Ticks), sb = b.Sum(u => u.Ticks);
        // try every placement of the free events (few in practice; greedy past a dozen)
        int n = free.Count;
        int bestMask = 0;
        double bestCost = double.MaxValue;
        if (n <= 12)
        {
            for (int mask = 0; mask < (1 << n); mask++)
            {
                int ta = sa, tb = sb;
                double lean = 0;
                for (int i = 0; i < n; i++)
                {
                    bool toA = (mask >> i & 1) == 1;
                    if (toA) ta += free[i].Ticks; else tb += free[i].Ticks;
                    // a rest above the middle line belongs up, one below belongs down
                    bool high = free[i].Ev.Y < staffMid;
                    if (toA != high) lean += 0.1;
                    // (a rest in a note's column is not that note's voice: Op.27-2's bar 23, the melody's quarter rest over
                    // the first note of the triplets. As the time would have it either way, this decides)
                    if (Clash(free[i], toA ? a : b, s)) lean += 1;
                }
                double cost = Math.Abs(ta - barTicks) + Math.Abs(tb - barTicks) + lean * Dur.Whole / 64;
                if (cost < bestCost) { bestCost = cost; bestMask = mask; }
            }
        }
        else
            for (int i = 0; i < n; i++) if (free[i].Ev.Y < staffMid) bestMask |= 1 << i;
        for (int i = 0; i < n; i++) (((bestMask >> i) & 1) == 1 ? a : b).Add(free[i]);
        a.Sort((p, q) => p.X.CompareTo(q.X));
        b.Sort((p, q) => p.X.CompareTo(q.X));
        // a third voice: one way's stems too long for the bar, its unbeamed notes a bar's worth by themselves
        // and its beamed groups another (lang36-3's left hand: sixteenths beamed down over the bass's
        // ees,,4. ees,,8, stems down too)
        var voices = new List<List<Unit>>();
        foreach (var v in new[] { a, b })
        {
            // (a note of a group's piece of ink longer than another of the group at its x is no member: the
            // bass's eighth whose top touches the sixteenths' beam, lang36-3's scan, taken into their group;
            // nor is a note alone in its piece, a flag's: bar 19's)
            bool Lone(Unit e) => e.Ev.Beam == 0 || !v.Any(o => o != e && o.Ev.Beam == e.Ev.Beam && !o.Rest)
                                 || v.Any(o => o != e && o.Ev.Beam == e.Ev.Beam && !o.Rest && Math.Abs(o.X - e.X) < (s > 0 ? 0.5 * s : 8) && o.Ticks < e.Ticks);
            var lone = v.Where(e => !e.Rest && Lone(e)).ToList();
            var beamed = v.Where(e => !e.Rest && !Lone(e)).ToList();
            // (or a beamed group short of the bar, the other hand ending or starting its line: Op.27-2's bar 12, the
            // left hand's last triplet over its chords, every stem up)
            if (v.Sum(e => e.Ticks) > barTicks && lone.Count > 0 && beamed.Count > 0 && beamed.Count == v.Count - lone.Count
                && lone.Sum(e => e.Ticks) == barTicks && beamed.Sum(e => e.Ticks) <= barTicks)
                voices.AddRange(new[] { beamed, lone }.OrderBy(w => w.Average(e => e.Ev.Y)));
            else voices.Add(v);
        }
        return voices.Where(v => v.Count > 0).ToList();
    }

    static bool Clash(Unit r, List<Unit> voice, double s) =>
        s > 0 && r.Rest && !r.FullBar && voice.Any(n => !n.Rest && Math.Abs(n.X - r.X) <= 0.6 * s);

    /// <summary>
    /// A line a hand starts and the other hand ends: a voice short of the bar, and voices on another staff
    /// short by just its length, all of them after its last note (Op.27-2's bars 23, 30, 32 and 33, the
    /// triplets climbing from the left hand's staff and running on in the right hand's). The first
    /// tells the others' time; the time is whole, so no length is missing. A voice of rests alone beside
    /// them on a staff is part of that picture (the bar's rest the engraver stopped printing).
    /// </summary>
    /// <returns>the voices that open such a line, and every voice of such a bar not to be measured against the bar alone</returns>
    public static (List<List<Ev>> Leaders, HashSet<List<Ev>> Whole, Dictionary<Ev, int> Times) HandedOver(List<List<List<Ev>>> staves, int barTicks)
    {
        var leaders = new List<List<Ev>>();
        var whole = new HashSet<List<Ev>>(ReferenceEqualityComparer.Instance);
        bool Short(List<Ev> v) => v.Count > 0 && v.Any(e => !e.IsRest) && v.Sum(e => e.Ticks) < barTicks;
        for (int p = 0; p < staves.Count; p++)
            foreach (var a in staves[p].Where(Short))
            {
                double end = a.Max(e => e.X);
                int left = barTicks - a.Sum(e => e.Ticks);
                // (a neighbouring staff: a hand's)
                var rest = staves.Where((_, q) => Math.Abs(q - p) == 1).SelectMany(vs => vs)
                    .Where(v => Short(v) && v.Sum(e => e.Ticks) == left && v.Where(e => !e.IsRest).Min(e => e.X) > end).ToList();
                if (rest.Count == 0) continue;
                leaders.Add(a);
                whole.Add(a);
                whole.UnionWith(rest);
            }
        foreach (var vs in staves.Where(vs => vs.Any(whole.Contains)))
            foreach (var v in vs.Where(v => v.Count > 0 && v.All(e => e.IsRest))) whole.Add(v);
        var times = new Dictionary<Ev, int>(ReferenceEqualityComparer.Instance);
        foreach (var vs in staves)
            if (TurningLine(vs, barTicks) is { } line)
            {
                foreach (var (e, t) in line) times[e] = t;
                whole.UnionWith(vs);
            }
        return (leaders, whole, times);
    }

    /// <summary>
    /// One line split by its stems: the voices of a staff, one at least short of the bar, whose beamed groups of one
    /// value, from more than one voice and none under another, add up to the bar (Op.27-2's bars 37, 40 and 42, the
    /// triplets' stems turning with the notes, or a clef, beside a note or a melody of their own). Stems made them
    /// voices; the bar's length says each voice rests while the other has the line. The line's notes have their time
    /// from it; the voices' other notes from a line note in their column, or after the voice's last. A voice's first
    /// note with neither takes the nearest line note's: a head beside the one it shares a column with (bar 40's whole).
    /// </summary>
    /// <returns>every event's time in the bar, or null: no such line</returns>
    static Dictionary<Ev, int> TurningLine(List<List<Ev>> voices, int barTicks)
    {
        var real = voices.Where(v => v.Count > 0).ToList();
        if (real.Count < 2 || !real.Any(v => v.Sum(e => e.Ticks) < barTicks) || real.Any(v => v.Sum(e => e.Ticks) > barTicks)) return null;
        var groups = real.SelectMany(v => v.Where(e => !e.IsRest && e.Beam != 0).GroupBy(e => e.Beam).Select(g => (Voice: v, Evs: g.ToList()))).ToList();
        foreach (var kind in groups.Where(g => g.Evs.All(e => e.Ticks == g.Evs[0].Ticks && e.Scale == g.Evs[0].Scale)).GroupBy(g => (g.Evs[0].Ticks, g.Evs[0].Scale)))
        {
            var run = kind.Where(g => g.Evs.All(e => e.Ticks == kind.Key.Ticks && e.Scale == kind.Key.Scale)).OrderBy(g => g.Evs[0].X).ToList();
            if (run.Sum(g => g.Evs.Count) * kind.Key.Ticks != barTicks || run.Select(g => g.Voice).Distinct().Count() < 2) continue;
            // (none under another: two voices' triplets at once are two lines, whatever they add up to)
            if (run.Zip(run.Skip(1)).Any(p => p.First.Evs[^1].X >= p.Second.Evs[0].X)) continue;
            var line = run.SelectMany(g => g.Evs).OrderBy(e => e.X).ToList();
            double near = line.Zip(line.Skip(1)).Min(p => p.Second.X - p.First.X) / 2;
            var times = new Dictionary<Ev, int>(ReferenceEqualityComparer.Instance);
            for (int i = 0; i < line.Count; i++) times[line[i]] = i * kind.Key.Ticks;
            bool fits = true;
            foreach (var v in real)
            {
                int end = 0;
                bool started = false;
                foreach (var e in v)
                {
                    if (!times.TryGetValue(e, out int t))
                    {
                        var column = line.FirstOrDefault(n => Math.Abs(n.X - e.X) <= near);
                        // (a voice's note its last one's length still covers has that length's time: a melody's sixteenth after a
                        // dotted eighth, over a triplet's third note and an inch from it)
                        if (column != null && times[column] >= end) t = times[column];
                        else if (started) t = end;
                        else t = times[line.MinBy(n => Math.Abs(n.X - e.X))];
                        times[e] = t;
                    }
                    if (t < end) { fits = false; break; }
                    end = t + e.Ticks;
                    started = true;
                }
                if (!fits || end > barTicks) { fits = false; break; }
            }
            if (fits) return times;
        }
        return null;
    }

    // A rest at a note's x, off its head on the side away from its stem: the two sound at once, so they
    // are two voices, though the line they would make is no longer than the bar (a song's pickup, the
    // voice's eighth over the second voice's eighth rest: read as "r8 d8", a quarter -- kinkel19-6)
    // (and two notes in one column, the upper with its stem up and the lower with its stem down: a melody's quarter
    // over the first of three eighths, in a bar the two lines fill together by chance -- 2/4's "b''4 tuplet { d' g' b' }")
    static bool Stacked(List<Ev> evs, double s) =>
        s > 0 && evs.Any(r => r.IsRest && !r.FullBar && r.Stem == 0
                              && evs.Any(n => !n.IsRest && n.Stem != 0 && Math.Abs(n.X - r.X) <= 0.6 * s && (r.Y - n.Y) * n.Stem >= 1.5 * s)
                              || !r.IsRest && r.Stem > 0
                                 && evs.Any(n => !n.IsRest && n.Stem < 0 && Math.Abs(n.X - r.X) <= 0.6 * s && n.Y > r.Y));
}
