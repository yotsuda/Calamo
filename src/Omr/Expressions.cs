namespace Omr;

/// <summary>
/// Dynamics, hairpins and the pedal (M3, S14), from what the learnt detector sees: a dynamic's letters
/// (dyn_p ...), a hairpin's tip and open end, Ped. and its release *, a pedal bracket's hooks and its
/// notch. Each is put on the event it stands at, in every list of the part's events (a bar's events
/// and its voices' are copies of one another), as the .lys writes them: @mf, @cresc ... @!cresc,
/// @sustain ... @!sustain.
///
/// Whose a mark is: the staff it hangs under (Lily# sets dynamics, hairpins and the pedal below the
/// staff, and between a grand staff's staves the upper staff's stand halfway), unless it stands
/// much nearer the staff under it (a voice's marks over its staff, .up); over a system's first staff,
/// that staff's. Where: a dynamic is centred on its note, a hairpin runs from its note's left (after
/// a dynamic on the same note) to the right edge of the note it ends on, or up to the next dynamic;
/// the pedal's marks start at the left of their note's head.
/// </summary>
sealed class Expressions
{
    // the dynamics the letters may spell (others are taken for letters misread)
    static readonly HashSet<string> Words = new() { "pppp", "ppp", "pp", "p", "mp", "mf", "f", "ff", "fff", "ffff", "sf", "sfz", "sffz", "sfp", "fp", "fz", "rf", "rfz" };
    /// <summary>Whether letters spell a dynamic (Texts: the detector's letters in a word of expression spell none).</summary>
    public static bool IsDynamic(string letters) => Words.Contains(letters);

    // the longest hairpin traced from one end, in spaces (a system's width: one of chords003's runs 60 and more)
    const double MaxHairpin = 150;
    // between two systems, a hairpin this much nearer the last staff above (the share of its distance to the
    // next system's first staff) is the staff above's (Owner)
    public static double NearRatio = 0.7;

    // the pedal of each part, down at the end of the pages read so far
    readonly HashSet<int> down = new();
    // the parts whose last hairpin ran on past their system's end (its second piece starts the next system)
    readonly HashSet<int> goesOn = new();
    // how each part draws its pedal: Ped. and *, brackets, or both (mixed)
    readonly Dictionary<int, (bool Text, bool Bracket)> styles = new();

    public static bool Verbose;

    /// <summary>The part's pedal style for its declaration (part lh { clef bass pedal text }), or null.</summary>
    public string PedalStyle(int part) => styles.TryGetValue(part, out var st) ? st.Text && st.Bracket ? "mixed" : st.Text ? "text" : "bracket" : null;

    public void Apply(PageResult res)
    {
        if (res.Sym.Detections == null) return;
        double s = res.S;
        var det = res.Sym.Detections.Where(t => t.Score > Detector.Threshold).ToList();

        // the staff a mark at (x, y) belongs to, the system it is in, and whether it stands over the staff
        // Between two staves, the one the mark fits (a note where it should stand), if only one does; the
        // one whose note a dynamic is centred on (off(staff): how far x is off the staff's nearest note, not a rest), if
        // only one's is; else the one above, unless the mark stands much nearer the one below. A dynamic between a
        // system's two staves (letters from l to r): much nearer its ink, as Lily# sets it a little off its
        // own staff's notes, beams and stems (one under the upper staff's low beam stands far nearer the
        // lower staff's lines than the upper's); elsewhere, much nearer its lines
        (SystemInfo Sys, Staff St, bool Over)? Owner(double x, double y, bool pedal, Func<SystemInfo, Staff, bool> fits = null,
                                                     double l = double.NaN, double r = double.NaN, Func<SystemInfo, Staff, double> off = null, double near = 0)
        {
            // (the staves of every system that spans x, the next system's too: under a system's last
            // staff its marks hang lower than the next system's first staff's stand over it)
            var staves = res.Systems.Where(q => q.Staves.Count > 0 && q.Measures > 0 && x > q.Bars[0] - 2 * s && x < q.Bars[^1] + 2 * s)
                .SelectMany(q => q.Staves.Select(st => (Sys: q, St: st))).ToList();
            var above = staves.Where(p => p.St.Bottom < y).OrderByDescending(p => p.St.Bottom).FirstOrDefault();
            var below = staves.Where(p => p.St.Top > y).OrderBy(p => p.St.Top).FirstOrDefault();
            if (above.St == null) return below.St == null || below.St.Top - y > 8 * s ? null : (below.Sys, below.St, true);
            if (y - above.St.Bottom > 10 * s && (below.St == null || below.St.Top - y > 8 * s)) return null;
            bool canBelow = !pedal && below.St != null && below.St.Top - y < 8 * s;
            // (a hairpin between two systems, far nearer the system above's last staff (near: the share of its
            // distance to the next one's first), is that staff's, whatever the next system's notes under it:
            // piano004's crescendo 1.9 spaces under the left hand, 6 over the next system's right hand, which
            // has a note where it starts. Not a dynamic: the next system's, set over its notes, may stand nearer)
            if (canBelow && above.Sys != below.Sys && y - above.St.Bottom < near * (below.St.Top - y)) return (above.Sys, above.St, false);
            if (canBelow && fits != null && fits(above.Sys, above.St) != fits(below.Sys, below.St))
                return fits(below.Sys, below.St) ? (below.Sys, below.St, true) : (above.Sys, above.St, false);
            // (a dynamic is centred on its note's head: on one staff's note, within a quarter of a space, and
            // off the other's by 0.4 of a space more)
            if (canBelow && off != null)
            {
                double a = off(above.Sys, above.St), b = off(below.Sys, below.St);
                if (Verbose) res.Notes.Add($"owner {x:F0},{y:F0}: off the notes above {a / s:F2} below {b / s:F2}");
                if (b < 0.25 * s && a > b + 0.4 * s) return (below.Sys, below.St, true);
                if (a < 0.25 * s && b > a + 0.4 * s) return (above.Sys, above.St, false);
            }
            // (not between two systems: the next one's notes and stems may reach into the marks hanging
            // under the last staff. Both touching: the lines decide)
            if (canBelow && !double.IsNaN(l) && above.Sys == below.Sys)
            {
                double up = Clear(l, r, y, -1, above.St.Bottom), down = Clear(l, r, y, 1, below.St.Top);
                if (Verbose) res.Notes.Add($"owner {l:F0}-{r:F0} at {y:F0}: clear up {up / s:F2} down {down / s:F2} (staves {(y - above.St.Bottom) / s:F1} {(below.St.Top - y) / s:F1})");
                if (Math.Max(up, down) >= 0.15 * s) return down < 0.5 * up ? (below.Sys, below.St, true) : (above.Sys, above.St, false);
            }
            if (canBelow && below.St.Top - y < 0.5 * (y - above.St.Bottom)) return (below.Sys, below.St, true);
            return (above.Sys, above.St, false);
        }
        // the white between letters from l to r on the line y and the nearest ink up (dir -1) or down (1), as
        // far as the staff's line at edge: from the letters' edge (the first two rows without ink across
        // them: the fonts' letters differ in size), every column's to its first ink
        double Clear(double l, double r, double y, int dir, double edge)
        {
            var ink = res.Dark;
            int x0 = (int)Math.Max(0, l), x1 = (int)Math.Min(ink.W - 1, r);
            bool Dark(int x, double d) { int yy = (int)Math.Round(y + dir * d); return yy < 0 || yy >= ink.H || ink[x, yy]; }
            bool Row(double d) { for (int x = x0; x <= x1; x++) if (Dark(x, d)) return true; return false; }
            double own = 0, far = Math.Abs(edge - y);
            while (own < 1.6 * s && (Row(own) || Row(own + 1))) own++;
            // (ink wider than a stem: the other staff's stem tip, thickened by a scan, may reach under the
            // letters, and a scan's specks are no ink. piano006's f over a stem of the left hand)
            bool Wide(int x, double d)
            {
                int n = 0, w = (int)(0.4 * s);
                for (int xx = Math.Max(0, x - w); xx <= Math.Min(ink.W - 1, x + w); xx++) if (Dark(xx, d)) n++;
                return n >= 0.4 * s;
            }
            double least = far;
            for (int x = x0; x <= x1; x++)
            {
                double d = own;
                while (d < far && d - own < least && !(Dark(x, d) && Wide(x, d))) d++;
                least = Math.Min(least, Math.Max(0, d - own));
            }
            return least;
        }
        // the event of the staff's part nearest x (within the given reach), in each of its lists: its bar's and its voices'
        List<Ev> At(SystemInfo sys, Staff st, double x, double reach, Func<Ev, bool> ok = null)
        {
            int k = sys.Bars.FindLastIndex(b => b <= x);
            k = Math.Clamp(k, 0, sys.Measures - 1);
            foreach (int bar in new[] { sys.BarOf(k), sys.BarOf(k) - 1, sys.BarOf(k) + sys.Span(k) }.Where(b => b >= sys.FirstBar && b < sys.FirstBar + sys.BarCount))
            {
                var lists = new List<List<Ev>>();
                if (res.Bars.TryGetValue((bar, st.Part), out var evs)) lists.Add(evs);
                if (res.Voices.TryGetValue((bar, st.Part), out var vs)) lists.AddRange(vs);
                // the nearest event of any list, then its copy in the bar's list and in one of its voices
                // (a mark on two voices would be written twice). A copy is the same stem's (a chord's
                // copy may stand at another of its heads, a second aside), or a rest at its x
                var near = lists.SelectMany(l => l).Where(e => !e.FullBar && !e.Slash && (ok == null || ok(e)) && Math.Abs(e.X - x) < reach)
                    .OrderBy(e => Math.Abs(e.X - x)).FirstOrDefault();
                if (near == null) continue;
                bool Same(Ev e) => e == near || e.IsRest == near.IsRest && !e.FullBar && !e.Slash
                    && (e.Stem != 0 && near.Stem != 0 ? e.Stem == near.Stem && Math.Abs(e.StemX - near.StemX) < 0.3 * s : Math.Abs(e.X - near.X) < 0.3 * s);
                var found = new List<Ev>();
                if (evs != null && evs.FirstOrDefault(Same) is { } one) found.Add(one);
                if (vs != null && vs.Select(v => v.FirstOrDefault(Same)).Where(e => e != null).OrderBy(e => Math.Abs(e.X - x)).FirstOrDefault() is { } inVoice)
                    found.Add(inVoice);
                return found;
            }
            return new List<Ev>();
        }

        // dynamics: the letters side by side on one line, read as a word
        var letters = det.Where(t => t.Class.StartsWith("dyn_")).OrderBy(t => t.X).ToList();
        var dynamics = new List<(double X, double Y, double L, double R, string Word)>();
        var taken = new bool[letters.Count];
        for (int i = 0; i < letters.Count; i++)
        {
            if (taken[i]) continue;
            var word = new List<Detection> { letters[i] };
            taken[i] = true;
            for (int j = i + 1; j < letters.Count; j++)
            {
                if (taken[j] || Math.Abs(letters[j].Y - word[^1].Y) > 0.7 * s) continue;
                if (letters[j].X - word[^1].X > 1.8 * s) break;
                word.Add(letters[j]); taken[j] = true;
            }
            foreach (var part in Split(word))
            {
                string w = string.Concat(part.Select(t => t.Class[4]));
                if (!Words.Contains(w)) { if (Verbose) res.Notes.Add($"dynamic \"{w}\" at {part[0].X:F0},{part[0].Y:F0}: no dynamic"); continue; }
                dynamics.Add(((part[0].X + part[^1].X) / 2, part.Average(t => t.Y), part[0].X - 0.7 * s, part[^1].X + 0.7 * s, w));
            }
        }
        // the dynamics of two notes side by side run on as one row of letters (rfz f, f rfz, ff f): split
        // where both sides spell a dynamic, at the widest gap if the whole spells none, or if it does, where
        // the gap is half as wide again as every other (the letters' own gaps run 0.7-1.6 spaces by the pair)
        static List<List<Detection>> Split(List<Detection> word)
        {
            static bool Known(IEnumerable<Detection> p) => Words.Contains(string.Concat(p.Select(t => t.Class[4])));
            var gaps = Enumerable.Range(1, word.Count - 1).Select(k => (K: k, Gap: word[k].X - word[k - 1].X)).ToList();
            bool whole = Known(word);
            foreach (var (k, gap) in gaps.OrderByDescending(g => g.Gap))
            {
                if (!Known(word.Take(k)) || !Known(word.Skip(k))) continue;
                if (whole && (gaps.Count < 2 || gaps.Any(g => g.K != k && gap < 1.5 * g.Gap))) break;
                return new List<List<Detection>> { word.Take(k).ToList(), word.Skip(k).ToList() };
            }
            return new List<List<Detection>> { word };
        }

        // whether ink runs from a hairpin's tip to both edges of its open end (Lily#'s hairpin opens to
        // two thirds of a space either side): in seven of ten places along each line (a scan's toner breaks them), within a fifth of a space
        bool Lines(Detection tip, Detection open)
        {
            var ink = res.Dark;
            foreach (int side in new[] { -1, 1 })
            {
                int n = 0, hit = 0;
                for (double f = 0.1; f <= 0.9; f += 0.05)
                {
                    // (the open end's edge: up to a space away, as hairpins' openings vary; the nearest ink across)
                    double x = tip.X + f * (open.X - tip.X);
                    n++;
                    bool any = false;
                    for (double h = 0.35; h <= 1.0 && !any; h += 0.05)
                    {
                        int yy = (int)Math.Round(tip.Y + f * (open.Y + side * h * s - tip.Y)), xx = (int)Math.Round(x);
                        if (xx >= 0 && xx < ink.W && yy >= 1 && yy < ink.H - 1 && (ink[xx, yy] || ink[xx, yy - 1] || ink[xx, yy + 1])) any = true;
                    }
                    if (any) hit++;
                }
                if (hit < 0.7 * n) return false;
            }
            return true;
        }

        // hairpins: a tip and an open end on one line, the nearest pair first
        var tips = det.Where(t => t.Class == "hairpin_tip").ToList();
        var opens = det.Where(t => t.Class == "hairpin_open").ToList();
        var pairs = (from a in tips from b in opens
                     where Math.Abs(a.Y - b.Y) < 0.8 * s && Math.Abs(a.X - b.X) > 1.5 * s && Math.Abs(a.X - b.X) < 60 * s
                     orderby Math.Abs(a.X - b.X) select (Tip: a, Open: b)).ToList();
        var usedTips = new HashSet<Detection>(); var usedOpens = new HashSet<Detection>();
        var hairpins = new List<(string Kind, double L, double R, double Y)>();
        var startAt = new Dictionary<double, List<Ev>>();   // a hairpin's start (its left end) to the note of the dynamic just before it
        foreach (var (tip, open) in pairs)
        {
            if (usedTips.Contains(tip) || usedOpens.Contains(open)) continue;
            // (no other end of a hairpin between the two: two hairpins in a row)
            double l = Math.Min(tip.X, open.X), r = Math.Max(tip.X, open.X);
            if (tips.Concat(opens).Any(t => t != tip && t != open && Math.Abs(t.Y - tip.Y) < 0.8 * s && t.X > l + 0.5 * s && t.X < r - 0.5 * s)) continue;
            // (and its two lines there: from the tip to either side of the open end. The open end of one
            // hairpin beside the tip of the next is no hairpin: nothing runs between them)
            if (!Lines(tip, open)) continue;
            usedTips.Add(tip); usedOpens.Add(open);
            hairpins.Add((tip.X < open.X ? "cresc" : "decresc", l, r, (tip.Y + open.Y) / 2));
        }
        // hairpins the detector saw only faintly, or saw one end of: from an end, the two lines found in
        // the ink (Trace). A scan cut dark to black and white: piano006's every hairpin seen at 0-0.3
        var staves = res.Systems.SelectMany(q => q.Staves).ToList();
        foreach (var e in res.Sym.WeakHairpins.Where(t => t.Score > Detector.WeakHairpin)
                     .Concat(tips.Where(t => !usedTips.Contains(t))).Concat(opens.Where(o => !usedOpens.Contains(o))).OrderByDescending(t => t.Score))
        {
            // (not on a staff, and no hairpin there already: its other end, or one the pairs found)
            if (staves.Any(st => e.X > st.Left && e.X < st.Right && e.Y > st.Top - 0.5 * s && e.Y < st.Bottom + 0.5 * s)) continue;
            if (hairpins.Any(h => Math.Abs(h.Y - e.Y) < 0.8 * s && e.X > h.L - 1 * s && e.X < h.R + 1 * s)) continue;
            bool isTip = e.Class == "hairpin_tip";
            var found = isTip ? new[] { -1, 1 }.Select(dir => Trace(e.X, e.Y, dir)).FirstOrDefault(h => h != null) : FromOpen(e.X, e.Y);
            if (found is not { } f) continue;
            if (isTip) usedTips.Add(e); else usedOpens.Add(e);
            hairpins.Add(f);
            if (Verbose) res.Notes.Add($"hairpin {f.Kind} {f.L:F0}-{f.R:F0} at {f.Y:F0}: traced from its {(isTip ? "tip" : "open end")} at {e.X:F0},{e.Y:F0} ({e.Score:F2})");
        }

        // ink at (x, y), or a pixel above or below it
        bool Ink(double x, double y)
        {
            var ink = res.Dark;
            int xx = (int)Math.Round(x), yy = (int)Math.Round(y);
            return xx >= 0 && xx < ink.W && yy >= 1 && yy < ink.H - 1 && (ink[xx, yy] || ink[xx, yy - 1] || ink[xx, yy + 1]);
        }
        // how far a straight line runs from (x0, y0) in dir, rising by k a pixel to the side (-1 up, 1 down):
        // its last ink, and the share of the places on the way to it with ink (a scan's toner breaks
        // it: a gap of up to two thirds of a space)
        (double Reach, double Share) Ray(double x0, double y0, int dir, int side, double k)
        {
            double step = 0.25 * s, last = -1;
            int n = 0, hit = 0, miss = 0;
            for (double t = 0; t <= MaxHairpin * s; t += step)
            {
                n++;
                if (Ink(x0 + dir * t, y0 + side * k * t)) { hit++; last = t; miss = 0; }
                else if (++miss * step > 0.66 * s) break;
            }
            return last < 0 ? (0, 0) : (last, hit / (double)Math.Max(1, (int)(last / step) + 1));
        }
        // the hairpin whose tip is at (x, y), opening in dir: its two lines from the tip, up and down, each
        // straight and running three spaces or more, ending together, open a third of a space to more than
        // a space either side; and between them no ink (no beam, no staff line)
        (string Kind, double L, double R, double Y)? Trace(double x, double y, int dir)
        {
            (string, double, double, double)? best = null;
            double bestReach = 0;
            for (double dy = -0.2; dy <= 0.21; dy += 0.1)
            {
                double y0 = y + dy * s;
                var sides = new List<(double Reach, double K)>();
                foreach (int side in new[] { -1, 1 })
                {
                    (double Reach, double K) top = (0, 0);
                    for (double k = 0.002; k < 0.4; k *= 1.05)
                    {
                        var (reach, share) = Ray(x, y0, dir, side, k);
                        double open = k * reach;
                        if (reach >= 3 * s && share >= 0.85 && open >= 0.35 * s && open <= 1.2 * s && reach > top.Reach) top = (reach, k);
                    }
                    if (top.Reach == 0) break;
                    sides.Add(top);
                }
                if (sides.Count < 2) continue;
                double r = Math.Min(sides[0].Reach, sides[1].Reach);
                if (Math.Abs(sides[0].Reach - sides[1].Reach) > Math.Max(1 * s, 0.1 * r)) continue;
                if (Math.Abs(sides[0].K * sides[0].Reach - sides[1].K * sides[1].Reach) > 0.3 * s) continue;
                // (the middle, from half way to the open end: white)
                int n = 0, inked = 0;
                for (double f = 0.5; f <= 0.95; f += 0.05) { n++; if (Ink(x + dir * f * r, y0)) inked++; }
                if (inked > 0.2 * n) continue;
                if (r > bestReach) { bestReach = r; best = (dir > 0 ? "cresc" : "decresc", Math.Min(x, x + dir * r), Math.Max(x, x + dir * r), y0); }
            }
            return best;
        }
        // the hairpin whose open end is at (x, y): its two edges, the nearest ink a third of a space to more
        // than a space above and below; the tip where two straight lines from them meet, in seven of eight
        // places; then the whole of it traced from that tip, its open end back here
        (string Kind, double L, double R, double Y)? FromOpen(double x0, double y)
        {
            // (the detector's point may stand a little off the end: a third of a space either way)
            foreach (double x in new[] { x0, x0 - 0.3 * s, x0 + 0.3 * s })
                if (FromOpenAt(x, y) is { } f) return f;
            return null;
        }
        (string Kind, double L, double R, double Y)? FromOpenAt(double x, double y)
        {
            double? Edge(int side)
            {
                // (the line's middle: a dark scan thickens it)
                for (double h = 0.3 * s; h <= 1.3 * s; h += 1)
                {
                    if (!Ink(x, y + side * h)) continue;
                    double a = h;
                    while (h <= 1.6 * s && Ink(x, y + side * h)) h += 1;
                    return y + side * (a + h - 1) / 2;
                }
                return null;
            }
            if (Edge(-1) is not { } yu || Edge(1) is not { } yl) return null;
            double yc = (yu + yl) / 2, h = (yl - yu) / 2;
            foreach (int dir in new[] { -1, 1 })
            {
                // (the farthest tip of the best: a tip a little short of the true one has its lines in the
                // thickened ink as well)
                var shares = new List<(double T, double Share)>();
                for (double t = 3 * s; t <= MaxHairpin * s; t += 0.25 * s)
                {
                    int n = 0, hit = 0;
                    for (double f = 0; f <= 1; f += 0.25 * s / t)
                    {
                        n += 2;
                        if (Ink(x + dir * f * t, yu + f * h)) hit++;
                        if (Ink(x + dir * f * t, yl - f * h)) hit++;
                    }
                    shares.Add((t, hit / (double)n));
                }
                double bestShare = shares.Max(q => q.Share);
                if (bestShare < 0.85) continue;
                // (traced from the farthest that holds: from past the true tip, the lines are not met)
                double last = double.MaxValue;
                foreach (var (t, _) in shares.Where(q => q.Share >= bestShare - 0.03).Reverse())
                {
                    if (t > last - 0.5 * s) continue;
                    last = t;
                    if (Trace(x + dir * t, yc, -dir) is { } hp && Math.Abs((dir > 0 ? hp.L : hp.R) - x) < 1.5 * s) return hp;
                    if (shares.Last(q => q.Share >= bestShare - 0.03).T - t > 4 * s) break;
                }
            }
            return null;
        }
        // a decrescendo broken at a system's end: its open end, its lines running on to the right and
        // closing in, with no tip on this system (the tip, on the next, ends its second piece). One the
        // detector saw only faintly: its lines and the white between them all the way to the system's end
        // (piano006's on a page cut dark to black and white, seen at 0.14)
        var weakOpens = res.Sym.WeakHairpins.Where(t => t.Class == "hairpin_open" && t.Score > Detector.WeakHairpin && !usedOpens.Contains(t)
                && !staves.Any(st => t.X > st.Left && t.X < st.Right && t.Y > st.Top - 0.5 * s && t.Y < st.Bottom + 0.5 * s)).ToHashSet();
        foreach (var open in opens.Where(o => !usedOpens.Contains(o)).Concat(weakOpens.OrderByDescending(t => t.Score)))
        {
            if (Owner(open.X, open.Y, false) is not { } o) continue;
            double end = o.Sys.Bars[^1];
            bool faint = weakOpens.Contains(open);
            // (a faint one's other end stands near the system's end, and no other hairpin's there already)
            if (faint && (hairpins.Any(h => Math.Abs(h.Y - open.Y) < 0.8 * s && open.X > h.L - 1 * s && open.X < h.R + 1 * s)
                         || !Converging(open, (end - open.X) / s - 0.5, 0.1, 0.85))) continue;
            if (!faint && !Converging(open)) continue;
            if (Verbose) res.Notes.Add($"hairpin decresc {open.X:F0}-{end:F0} at {open.Y:F0}: an open end alone ({open.Score:F2}), broken at the system's end");
            hairpins.Add(("decresc", open.X, end, open.Y));
        }
        // the dynamics on their notes: centred on it, or, where a hairpin starts at the note, set to the
        // left of the note so that the hairpin starts after it (the note is the hairpin's)
        var dynOwners = new List<(double L, double R, double Y, (SystemInfo Sys, Staff St, bool Over) Owner)>();
        foreach (var d in dynamics)
        {
            var hp = hairpins.Where(h => Math.Abs(h.Y - d.Y) < 1.2 * s && h.L > d.R - 0.5 * s && h.L < d.R + 2 * s).OrderBy(h => h.L).ToList();
            if (Owner(d.X, d.Y, false, (q, st) => At(q, st, d.X, 0.8 * s).Count > 0 || hp.Count > 0 && At(q, st, hp[0].L + 0.6 * s, 1.2 * s).Count > 0,
                      d.L + 0.2 * s, d.R - 0.2 * s, (q, st) => At(q, st, d.X, 1.5 * s, e => !e.IsRest).Select(e => Math.Abs(e.X - d.X)).DefaultIfEmpty(1e9).Min()) is not { } owner) continue;
            var (sys, st, over) = owner;
            dynOwners.Add((d.L, d.R, d.Y, owner));
            // (a note under its middle is its note, the hairpin after it starting there too; with none
            // under it, the dynamic was set left of its note, and the hairpin starts at that note's left)
            var evs = At(sys, st, d.X, 0.8 * s);
            if (evs.Count == 0) evs = hp.Count > 0 ? At(sys, st, hp[0].L + 0.6 * s, 2.5 * s, e => e.X > hp[0].L - 1 * s) : At(sys, st, d.X, 2.5 * s);
            if (hp.Count > 0 && evs.Count > 0) startAt[hp[0].L] = evs;
            foreach (var e in evs) e.Dynamic = d.Word + (over ? ".up" : "");
            if (Verbose) res.Notes.Add($"dynamic {d.Word} at {d.X:F0},{d.Y:F0}: part {st.Part}" + (hp.Count > 0 ? ", the hairpin's note" : "")
                                       + (evs.Count == 0 ? ", no note" : $", the note at {string.Join(" ", evs.Select(e => $"{e.X:F0},{e.Y:F0}"))}"));
        }
        // (system by system, left to right: a hairpin broken at a system's end goes on on the next)
        foreach (var (kind, l, r, y) in hairpins.Select(h => (h, o: HairpinOwner(h.L, h.R, h.Y))).Where(p => p.o != null)
                     .OrderBy(p => p.o.Value.Sys.Index).ThenBy(p => p.h.L).Select(p => p.h).ToList())
            Hairpin(kind, l, r, y);

        // a hairpin from l to r: from the note at its left end (a dynamic's just before it, on the same
        // note) to the last note before its right end, or on to the next dynamic, or on past the system's end
        // (a hairpin's staff: a dynamic's in a row with it, just before its start or just after its end, at
        // its height; else the one with a note where it starts. piano001's decrescendo between the staves,
        // ending at the right hand's pp)
        (SystemInfo Sys, Staff St, bool Over)? HairpinOwner(double l, double r, double y)
        {
            foreach (var d in dynOwners)
                if (Math.Abs(d.Y - y) < 1.2 * s && (l > d.R - 0.5 * s && l < d.R + 2 * s || d.L > r - 1 * s && d.L < r + 2.5 * s))
                    return d.Owner;
            return Owner((l + r) / 2, y, false, (q, st) => At(q, st, l + 0.6 * s, 1.2 * s).Count > 0, near: NearRatio);
        }
        void Hairpin(string kind, double l, double r, double y)
        {
            if (HairpinOwner(l, r, y) is not { } owner) return;
            var (sys, st, _) = owner;
            // (the second piece of one broken at the system before's end starts before this system's first note: not a new one)
            bool after = goesOn.Remove(st.Part);
            var first = At(sys, st, sys.Bars[0], 1e9);
            if (after && first.Count > 0 && l < first.Min(e => e.X) - 0.3 * s)
            {
                if (Verbose) res.Notes.Add($"hairpin {kind} {l:F0}-{r:F0} at {y:F0}: goes on from the system before");
                return;
            }
            // (it starts at its note's left, or after a dynamic on its note: that dynamic's note)
            var start = startAt.TryGetValue(l, out var dn) ? dn : At(sys, st, l + 0.6 * s, 2.5 * s, e => e.X > l - 1 * s);
            if (start.Count == 0) start = At(sys, st, l, 4 * s);
            if (start.Count == 0) return;
            foreach (var e in start) e.HairpinStart = kind;
            // (a dynamic just after its end ends it, where its note is; one reaching the system's end goes on)
            bool broken = r > sys.Bars[^1] - 1.5 * s;
            if (broken) goesOn.Add(st.Part);
            bool runsOn = broken || dynamics.Any(d => Math.Abs(d.Y - y) < 1.2 * s && d.L > r - 1 * s && d.L < r + 2.5 * s);
            var end = runsOn ? new List<Ev>() : At(sys, st, r - 0.5 * s, 6 * s, e => e.X < r && e.X > start[0].X + 0.5 * s);
            foreach (var e in end) if (e.HairpinStart == null) e.HairpinEnd = kind;
            if (Verbose) res.Notes.Add($"hairpin {kind} {l:F0}-{r:F0} at {y:F0}: part {st.Part}{(end.Count == 0 ? ", runs on" : "")}");
        }
        // whether two lines run on to the right of an open end, closing in: ink a third of a space or
        // more either side of its middle, in seven of ten places over the next three spaces, and the middle
        // white (a dynamic's letters just after the open end of a hairpin's second piece are no lines).
        // Or over len spaces, the lines at lo or more, in the given share of places
        bool Converging(Detection open, double len = 3.5, double lo = 0.2, double share = 0.7)
        {
            var ink = res.Dark;
            if (len < 3.5) return false;
            int m = 0, inked = 0;
            for (double dx = 0.5; dx <= 0.85 * len; dx += 0.25) { m++; if (Ink(open.X + dx * s, open.Y)) inked++; }
            if (inked > 0.2 * m) return false;
            foreach (int side in new[] { -1, 1 })
            {
                int n = 0, hit = 0;
                for (double dx = 0.5; dx <= len; dx += 0.25)
                {
                    n++;
                    int xx = (int)Math.Round(open.X + dx * s);
                    for (double h = lo; h <= 0.8; h += 0.05)
                    {
                        int yy = (int)Math.Round(open.Y + side * h * s);
                        if (xx >= 0 && xx < ink.W && yy >= 1 && yy < ink.H - 1 && (ink[xx, yy] || ink[xx, yy - 1] || ink[xx, yy + 1])) { hit++; break; }
                    }
                }
                if (hit < share * n) return false;
            }
            return true;
        }

        // the pedal: pressed (Ped., a bracket's start), released (*, a bracket's end), changed (a notch;
        // a release and a Ped. at one note), in order along each part
        // (an end hook, a notch or a * the detector saw only faintly: a hook touching the dynamic or the stem
        // above it, where the bracket's line runs into it from the left; a notch where it runs on from it to
        // the right; a * on a page printed dark. A release taken only where the pedal is down and no surer
        // release follows before the next press, a notch only where the pedal is down, below)
        bool Bracket(Detection t, int dir, double from, double lo, double hi)
        {
            for (double dy = lo; dy <= hi + 0.01; dy += 0.05)
            {
                int n = 0, hit = 0;
                for (double d = from * s; d <= (from + 2) * s; d++, n++)
                    if (Ink(t.X + dir * d, t.Y + dy * s)) hit++;
                if (hit >= 0.95 * n) return true;
            }
            return false;
        }
        bool WeakOk(Detection t) => t.Class switch
        {
            "ped_r" => Bracket(t, -1, 0.2, -0.3, 0.3),
            "ped_notch" => Bracket(t, 1, 0.8, 0, 1.2),   // (the line runs on from the notch's foot, under its peak)
            _ => t.Score > Detector.WeakStar,
        };
        if (Verbose)
            foreach (var t in res.Sym.WeakPedals.Where(t => t.Score > Detector.WeakPedal))
                res.Notes.Add($"pedal {t.Class} {t.Score:F2} at {t.X:F0},{t.Y:F0}: weak, " + (WeakOk(t) ? "kept" : t.Class == "ped_up" ? "too faint" : "no bracket's line"));
        var weak = res.Sym.WeakPedals.Where(t => t.Score > Detector.WeakPedal && WeakOk(t)).ToHashSet();
        // (a tuplet's bracket has hooks too, with its number in the middle of its line: no pedal's.
        // The real scan of piano002, its left hand's triplet bracket seen as a pedal bracket's at 0.51 and 0.43)
        var numbers = res.Sym.Detections.Where(t => t.Class.StartsWith("tuplet_") && t.Score > 0.3).ToList();
        bool TupletHook(Detection t) => t.Class is "ped_l" or "ped_r" && numbers.Any(n =>
        {
            double d = t.Class == "ped_l" ? n.X - t.X : t.X - n.X;
            return Math.Abs(n.Y - t.Y) < 1.0 * s && d > 0.5 * s && d < 8 * s;
        });
        if (Verbose)
            foreach (var t in det.Concat(weak).Where(TupletHook))
                res.Notes.Add($"pedal {t.Class} {t.Score:F2} at {t.X:F0},{t.Y:F0}: a tuplet's bracket (its number on the line)");
        var pedal = det.Where(t => t.Class is "ped" or "ped_up" or "ped_l" or "ped_r" or "ped_notch").Concat(weak).Where(t => !TupletHook(t)).OrderBy(t => t.X).ToList();
        var marks = new List<(Staff St, List<Ev> Evs, string Kind, bool Weak)>();
        foreach (var t in pedal)
        {
            if (Owner(t.X, t.Y, true) is not { } owner) continue;
            var (sys, st, _) = owner;
            // (Ped. is centred a little left of its note's head, the others at the head's left)
            double x = t.X + (t.Class == "ped" ? 0.8 : 0.6) * s;
            var evs = At(sys, st, x, 2.5 * s);
            if (Verbose) res.Notes.Add($"pedal {t.Class} {t.Score:F2} at {t.X:F0},{t.Y:F0}: part {st.Part}, " + (evs.Count == 0 ? "no note" : $"the note at {evs[0].X:F0}"));
            if (evs.Count == 0) continue;
            marks.Add((st, evs, t.Class, weak.Contains(t)));
            if (weak.Contains(t)) continue;
            var style = styles.GetValueOrDefault(st.Part);
            styles[st.Part] = t.Class is "ped" or "ped_up" ? (true, style.Bracket) : (style.Text, true);
        }
        foreach (var g in marks.GroupBy(m => m.St.Part))
        {
            int part = g.Key;
            // (the marks at one note together: a release and a press there are a change)
            var ats = g.GroupBy(m => m.Evs[0]).OrderBy(a => a.First().St.System).ThenBy(a => a.Key.X).ToList();
            static bool Press(IEnumerable<string> kinds) => kinds.Any(k => k is "ped" or "ped_l" or "ped_notch");
            for (int i = 0; i < ats.Count; i++)
            {
                var at = ats[i];
                var kinds = at.Select(m => m.Kind).ToHashSet();
                var evs = at.SelectMany(m => m.Evs).Distinct().ToList();
                bool press = Press(kinds);
                bool release = kinds.Contains("ped_up") || kinds.Contains("ped_r");
                // (a faint release only where no sure one follows before the next press)
                if (release && !press && at.All(m => m.Weak)
                    && ats.Skip(i + 1).FirstOrDefault(a => Press(a.Select(m => m.Kind)) || a.Any(m => !m.Weak)) is { } next && !Press(next.Select(m => m.Kind)))
                {
                    if (Verbose) res.Notes.Add($"pedal release at {at.Key.X:F0}: faint, a surer one follows at {next.Key.X:F0}");
                    continue;
                }
                if (press && at.All(m => m.Weak) && !down.Contains(part)) continue;
                if (press)
                {
                    // (pressed again while down: a change, which is the press alone)
                    foreach (var e in evs) e.PedalDown = true;
                    down.Add(part);
                }
                else if (release && down.Contains(part))
                {
                    foreach (var e in evs) e.PedalUp = true;
                    down.Remove(part);
                }
            }
        }
    }

    /// <summary>A pedal still down at the end of the music is released at the part's last note (Lily# draws none it never sees released).</summary>
    public void Finish(SortedDictionary<int, List<Ev>[]> bars, Dictionary<(int Bar, int Part), List<List<Ev>>> voices)
    {
        foreach (int part in down)
        {
            var last = bars.Reverse().Select(kv => kv.Value[part]).FirstOrDefault(l => l != null && l.Any(e => !e.IsRest && !e.FullBar));
            if (last == null) continue;
            int bar = bars.First(kv => kv.Value[part] == last).Key;
            var e = last.Last(e => !e.IsRest && !e.FullBar);
            e.PedalUp = !e.PedalDown;
            foreach (var v in voices.GetValueOrDefault((bar, part)) ?? new())
                foreach (var c in v.Where(c => Math.Abs(c.X - e.X) < 1 && !c.PedalDown)) c.PedalUp = true;
        }
        down.Clear();
    }
}
