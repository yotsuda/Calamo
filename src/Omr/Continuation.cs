namespace Omr;

/// <summary>
/// Where a line that runs past a system's end goes on (an octave line, a text spanner's dashes, a
/// trill's wave): the same part's staff in the next system of the page, and the last note it can be
/// followed to. Past the page's last system, on the next page: the reader leaves the line open
/// (<see cref="Open"/>, the page's <see cref="PageResult.Open"/>) and the next page's reader of its kind
/// looks for it over the part's staff in its first system (<see cref="PageResult.Before"/>).
/// </summary>
static class Continuation
{
    /// <summary>
    /// A line run out past a page's last system: its kind ("span", "trill", "ottava" and its octaves,
    /// "ottava-1"), its part, and what puts
    /// it on its notes, Close(page, head) with the next page and the head it ends at there, or
    /// Close(null, null) for none (as the line would be read had the page no next).
    /// </summary>
    public sealed class Open(string kind, int part, double x, Action<PageResult, Head> close)
    {
        public readonly string Kind = kind;
        public readonly int Part = part;
        /// <summary>Where it starts on its page (working image x): of two run out over one staff, the later goes on.</summary>
        public readonly double X = x;
        /// <summary>Its line runs out to the system's end, past its last note: with no continuation drawn on the
        /// next page, it ends on that page's first note (Lily# and LilyPond draw no piece for a line ending there).</summary>
        public bool RunsOut { get; init; }
        public bool Closed { get; private set; }
        public void Close(PageResult page, Head end)
        {
            if (Closed) return;
            Closed = true;
            close(page, end);
        }

        /// <summary>The line runs out past this page's last system too: the page leaves it open for the
        /// next, to end at fallback (this page's head) should that page not go on with it.</summary>
        public void On(PageResult page, Head fallback)
        {
            if (Closed) return;
            Closed = true;
            page.Open.Add(new Open(Kind, Part, double.MinValue, (at, end) => close(end != null ? at : page, end ?? fallback)));
        }
    }

    /// <summary>The lines of a kind (its name's start) the page before left open, one a part: the one
    /// starting last (an earlier one ran out to the system's last note, where the next starts: seed 11's
    /// piano010, bar 14), the others left to end there.</summary>
    public static IEnumerable<Open> Going(PageResult res, string kind)
        => res.Before.Where(o => o.Kind.StartsWith(kind) && !o.Closed).GroupBy(o => o.Part).Select(g => g.MaxBy(o => o.X)).ToList();

    /// <summary>Whether a system is the page's last with measures: a line run out past its end goes on on the next page.</summary>
    public static bool LastOnPage(PageResult res, SystemInfo sys)
        => !res.Systems.Any(q => q.Index > sys.Index && q.Measures > 0);

    /// <summary>The part's staff in the page's first system with measures, with that system; false for none.</summary>
    public static bool First(PageResult res, int part, out SystemInfo sys, out Staff on)
    {
        sys = res.Systems.Where(q => q.Measures > 0).MinBy(q => q.Index);
        on = sys?.Staves.FirstOrDefault(x => x.Part == part);
        if (on == null) { sys = null; return false; }
        return true;
    }

    /// <summary>The same part's staff in the system after sys (by = 1) or before it (by = -1), with that
    /// system; false when there is none, or it holds no measures.</summary>
    public static bool Next(PageResult res, SystemInfo sys, int part, out SystemInfo next, out Staff on, int by = 1)
    {
        on = null;
        next = res.Systems.FirstOrDefault(q => q.Index == sys.Index + by);
        if (next is not { Measures: > 0 }) { next = null; return false; }
        on = next.Staves.FirstOrDefault(x => x.Part == part);
        if (on == null) { next = null; return false; }
        return true;
    }

    /// <summary>The last head over a system's staff (no grace note's): a line that runs on past where
    /// the reading can follow it ends there.</summary>
    public static Head LastHead(PageResult res, Staff st, SystemInfo sys)
        => res.Sym.Heads.Where(h => h.St == st && !h.Grace && h.L < sys.Bars[^1]).MaxBy(h => h.L);
}
