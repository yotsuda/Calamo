namespace Omr;

/// <summary>
/// Splits a staff's bar into voices. The convention of two voices on a staff: the upper voice
/// has its stems up, the lower one down. A bar whose events add up to the bar as one line is
/// one voice (stems may point both ways in a single melody). Otherwise stem-up events make
/// voice 1 and stem-down events voice 2; rests and stemless notes go where they complete a
/// voice's time, one above the middle line leaning to voice 1.
/// </summary>
static class Voices
{
    /// <param name="s">the staff space, when known: a rest stacked with a note is then another voice however short the line</param>
    public static List<List<Ev>> Separate(List<Ev> evs, int barTicks, double staffMid, double s = 0)
    {
        // one line that fills the bar is one voice; more than a bar's worth is several
        // (a stemless whole note over a stem-down line is two voices, too)
        int total = evs.Sum(e => e.Ticks);
        if (total <= barTicks && !Stacked(evs, s)) return new List<List<Ev>> { evs };

        var a = evs.Where(e => e.Stem > 0).ToList();
        var b = evs.Where(e => e.Stem < 0).ToList();
        var free = evs.Where(e => e.Stem == 0).ToList();
        int sa = a.Sum(e => e.Ticks), sb = b.Sum(e => e.Ticks);

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
                    bool high = free[i].Y < staffMid;
                    if (toA != high) lean += 0.1;
                }
                double cost = Math.Abs(ta - barTicks) + Math.Abs(tb - barTicks) + lean * Dur.Whole / 64;
                if (cost < bestCost) { bestCost = cost; bestMask = mask; }
            }
        }
        else
            for (int i = 0; i < n; i++) if (free[i].Y < staffMid) bestMask |= 1 << i;

        for (int i = 0; i < n; i++) (((bestMask >> i) & 1) == 1 ? a : b).Add(free[i]);
        a.Sort((p, q) => p.X.CompareTo(q.X));
        b.Sort((p, q) => p.X.CompareTo(q.X));
        // a third voice: one way's stems too long for the bar, its unbeamed notes a bar's worth by themselves
        // and its beamed groups another (lang36-3's left hand: sixteenths beamed down over the bass's
        // ees,,4. ees,,8, stems down too)
        var voices = new List<List<Ev>>();
        foreach (var v in new[] { a, b })
        {
            // (a note of a group's piece of ink longer than another of the group at its x is no member: the
            // bass's eighth whose top touches the sixteenths' beam, lang36-3's scan, taken into their group;
            // nor is a note alone in its piece, a flag's: bar 19's)
            bool Lone(Ev e) => e.Beam == 0 || !v.Any(o => o != e && o.Beam == e.Beam && !o.IsRest)
                               || v.Any(o => o != e && o.Beam == e.Beam && !o.IsRest && Math.Abs(o.X - e.X) < (s > 0 ? 0.5 * s : 8) && o.Ticks < e.Ticks);
            var lone = v.Where(e => !e.IsRest && Lone(e)).ToList();
            var beamed = v.Where(e => !e.IsRest && !Lone(e)).ToList();
            if (v.Sum(e => e.Ticks) > barTicks && lone.Count > 0 && beamed.Count == v.Count - lone.Count
                && lone.Sum(e => e.Ticks) == barTicks && beamed.Sum(e => e.Ticks) == barTicks)
                voices.AddRange(new[] { beamed, lone }.OrderBy(w => w.Average(e => e.Y)));
            else voices.Add(v);
        }
        return voices.Where(v => v.Count > 0).ToList();
    }

    // A rest at a note's x, off its head on the side away from its stem: the two sound at once, so they
    // are two voices, though the line they would make is no longer than the bar (a song's pickup, the
    // voice's eighth over the second voice's eighth rest: read as "r8 d8", a quarter -- kinkel19-6)
    static bool Stacked(List<Ev> evs, double s) =>
        s > 0 && evs.Any(r => r.IsRest && !r.FullBar && r.Stem == 0
                              && evs.Any(n => !n.IsRest && n.Stem != 0 && Math.Abs(n.X - r.X) <= 0.6 * s && (r.Y - n.Y) * n.Stem >= 1.5 * s));
}
