namespace Omr;

/// <summary>
/// Arpeggios: a wavy line up a chord's left. Lily# stacks scripts.arpeggio, a space tall and some
/// eight tenths of a space wide, half a space left of the chord's heads or of its accidentals, from
/// half a space under its lowest head up past its highest. Read by rule, the detector having no class
/// for it: left of each chord of two heads and more, a band about a space wide whose ink, row by row
/// over the chord's heads, lies inside the band and runs short across it, and swings from side to
/// side and back once a space. Found with the symbols (Symbols.Find), so that the rests read in the
/// wave (a quarter rest's zigzag to the rules and the detector) go; written on the chord as @arpeggio.
/// </summary>
static class Arpeggios
{
    public static bool Verbose;
    // (OMR_ARP_DEBUG=x: the rows of the best band left of the chords about x)
    static readonly double? DebugX = double.TryParse(Environment.GetEnvironmentVariable("OMR_ARP_DEBUG"), out var v) ? v : null;

    /// <summary>A wave found: its chord, the ink's left and right, and how it measured. Sign: a clef the
    /// detector sees, measured as a wave -- no arpeggio, but its pieces are the clef's, not rests or signs.</summary>
    public sealed record Arp(List<Head> Chord, double L, double R, double Top, double Bottom, double Cover, int Turns, bool Fine = false, bool Sign = false);

    /// <summary>The arpeggios left of the page's chords, in the ink without the staff lines.</summary>
    public static List<Arp> Find(Img ink, List<Head> all, double s, List<Detection> dets = null)
    {
        var found = new List<Arp>();
        var heads = all.Where(h => !h.Cross && !h.Slash && !h.Grace).ToList();
        // the chords: heads on one stem, or stemless heads at one x (as the articulations' are)
        var chords = heads.GroupBy(h => (h.St, Key: h.HasStem ? Math.Round(h.StemX / (0.3 * s)) * 10 + (h.Up ? 1 : 0) : Math.Round(h.X / (0.35 * s)) * 10 + 5))
                          .Select(g => g.ToList()).Where(c => c.Count >= 2);
        foreach (var chord in chords)
        {
            double top = chord.Min(h => h.Y), bottom = chord.Max(h => h.Y), left = chord.Min(h => h.L);
            // (from just left of the heads, past the chord's accidentals, to the note before it at its heights)
            double stop = heads.Where(h => h.St == chord[0].St && h.R < left - 0.3 * s && h.Y > top - s && h.Y < bottom + s)
                               .Select(h => h.R).DefaultIfEmpty(double.MinValue).Max();
            // (half a space off the heads, or off the accidentals: a chord with none has its wave within a space)
            stop = Math.Max(stop + 0.4 * s, left - (chord.Any(h => h.Acc != null) ? 6 : 1.6) * s);
            Band? best = null, near = null;
            for (double xc = left - 0.7 * s; xc > stop; xc -= Math.Max(1, 0.1 * s))
            {
                var b = Measure(ink, chord[0].St, xc, top, bottom, s);
                if (b.Ok && (best == null || b.Cover > best.Value.Cover)) best = b;
                if (Verbose && (near == null || b.Cover * (b.Turns + 1) > near.Value.Cover * (near.Value.Turns + 1))) near = b;
            }
            // (none: the swing looked for again from a tenth of a space, as IsWave does -- a photo's
            // space of 13 pixels has the wave's centre swing two pixels, tabi.jpg's bar 7)
            bool fine = best == null;
            if (fine)
                for (double xc = left - 0.7 * s; xc > stop; xc -= Math.Max(1, 0.1 * s))
                {
                    var b = Measure(ink, chord[0].St, xc, top, bottom, s, swing: Math.Max(1.2, 0.1 * s));
                    if (b.Ok && (best == null || b.Cover > best.Value.Cover)) best = b;
                }
            // (a quarter rest's zigzag or a small clef change a few spaces left of a short chord with an
            // accidental measures as a wave over the chord's rows -- kinkel19-2's bar 20, kinkel10-6's bar 15:
            // not where the detector is sure of the rest or sees the clef. In the generated pieces' waves it
            // sees a quarter rest at 0.71 at most, and no clef. The ink followed on above and under the rows
            // measured told them too, but ran on along the beams the generated waves touch)
            if (best is { } bq && dets?.FirstOrDefault(t => t.Class == "rest_4" && t.Score >= 0.8 && t.X > bq.L - 0.3 * s && t.X < bq.R + 0.3 * s
                                                             && t.Y > top - 1.5 * s && t.Y < bottom + 1.5 * s
                                                         || t.Class.StartsWith("clef") && t.Score >= 0.5 && t.X > bq.L - s && t.X < bq.R + 0.5 * s
                                                             && t.Y > top - 3 * s && t.Y < bottom + 3 * s) is { } q)
            {
                if (Verbose) Console.WriteLine($"arpeggio? left of the chord at {left:F0},{top:F0}-{bottom:F0}: {bq.L:F0} to {bq.R:F0} is a {q.Class} ({q.Score:F2})");
                // (a clef's pieces in a scan read as rests and signs: kept as the wave's were, kinkel10-6-scan's bar 15)
                if (q.Class != "rest_4") found.Add(new Arp(chord, bq.L, bq.R, top, bottom, bq.Cover, bq.Turns, fine, Sign: true));
                best = null;
            }
            if (DebugX is { } dx0 && Math.Abs(left - dx0) < 0.5 * s)
                Console.WriteLine($"arpeggio debug: the chord at {left:F0},{top:F0}-{bottom:F0} ({chord.Count} heads), searched {left - 0.7 * s:F0} down to {stop:F0}");
            if (DebugX is { } dx && Math.Abs(left - dx) < 0.5 * s && near is { } nb)
            {
                Console.WriteLine($"arpeggio debug: the chord at {left:F0},{top:F0}-{bottom:F0}, the band {nb.L:F0} to {nb.R:F0}");
                Console.WriteLine(Measure(ink, chord[0].St, (nb.L + nb.R) / 2, top, bottom, s, Console.WriteLine));
            }
            if (best is { } w) found.Add(new Arp(chord, w.L, w.R, top, bottom, w.Cover, w.Turns, fine));
            else if (Verbose && near is { } n && n.Turns >= 1)
                Console.WriteLine($"arpeggio? left of the chord at {left:F0},{top:F0}-{bottom:F0}: at best {n.L:F0} to {n.R:F0}, cover {n.Cover:F2}, {n.Turns} turns, {n.Odd} uneven");
        }
        // A wave up both staves of a grand staff: one line from the right hand's note, a lone head on the
        // upper staff, down past the left hand's chord (lang36-3, bar 48; MusicXML marks the notes of both).
        // A note on another staff in the column of a wave found, the wave running on to it, is the wave's too.
        var marked = found.SelectMany(a => a.Chord).ToHashSet();
        foreach (var a in found.Where(a => !a.Sign).ToList())
            foreach (var h in heads.Where(h => h.St != a.Chord[0].St && !marked.Contains(h) && h.L - a.R > 0.1 * s && h.L - a.R < 1.6 * s
                                               && (h.Y < a.Top ? a.Top - h.Y : h.Y - a.Bottom) < 10 * s).ToList())
            {
                // (taken already with a head on its stem)
                if (marked.Contains(h)) continue;
                // (the rows from the head two spaces on towards the wave found: Measure looks from top down to half a space under bottom)
                bool above = h.Y < a.Top;
                double top = above ? h.Y : h.Y - 2.5 * s, bottom = above ? h.Y + 2 * s : h.Y - 0.5 * s;
                double xc = (a.L + a.R) / 2;
                bool Wave(double swing) => Enumerable.Range(-2, 5).Any(k => Measure(ink, h.St, xc + k * 0.1 * s, top, bottom, s, swing: swing).Ok);
                if (!Wave(0.2 * s) && !Wave(Math.Max(1.2, 0.1 * s))) continue;
                // (with the heads on its stem)
                var chord = heads.Where(o => o.St == h.St && !marked.Contains(o) && (o == h || h.HasStem && o.HasStem && Math.Abs(o.StemX - h.StemX) < 0.3 * s && o.Up == h.Up)).ToList();
                found.Add(new Arp(chord, a.L, a.R, chord.Min(o => o.Y), chord.Max(o => o.Y), a.Cover, a.Turns, a.Fine));
                marked.UnionWith(chord);
            }
        return found;
    }

    /// <summary>Whether a rest's middle lies in a wave found: the wave's, not a rest.</summary>
    public static bool InWave(List<Arp> arps, Rest r, double s)
        => arps.Any(a => a.Chord[0].St == r.St && r.X > a.L - 0.3 * s && r.X < a.R + 0.3 * s && r.Y > a.Top - 1.5 * s && r.Y < a.Bottom + 1.5 * s);

    /// <summary>The wave found a piece of ink lies in, if any: one of its strokes, cut at a staff line.</summary>
    public static Arp Holding(List<Arp> arps, Staff st, Comp c, double s)
        => arps.FirstOrDefault(a => a.Chord[0].St == st && c.MinX >= a.L - 0.15 * s && c.MaxX <= a.R + 0.15 * s && c.MaxY > a.Top - 2 * s && c.MinY < a.Bottom + 1.5 * s);

    /// <summary>The waves found, as @arpeggio on their chords' events.</summary>
    public static void Apply(PageResult res)
    {
        double s = res.S;
        foreach (var a in res.Sym.Arpeggios.Where(a => !a.Sign))
        {
            var evs = Articulations.Events(res, a.Chord[0], s);
            if (Verbose) res.Notes.Add($"arpeggio {a.L:F0} to {a.R:F0}, {a.Top:F0} to {a.Bottom:F0}: cover {a.Cover:F2}, {a.Turns} turns{(a.Fine ? " (a fine swing)" : "")} over {(a.Bottom - a.Top) / s:F1} spaces, part {a.Chord[0].St.Part}, "
                                       + (evs.Count == 0 ? "no event" : $"the chord at {evs[0].X:F0}"));
            foreach (var e in evs)
            {
                e.Articulations ??= new List<string>();
                if (!e.Articulations.Contains("arpeggio")) e.Articulations.Add("arpeggio");
            }
        }
    }

    /// <summary>
    /// Whether the ink about x on a staff is a wave rather than a line: the learnt detector sees a
    /// barline in an arpeggio's wave (tabi.jpg, a photo: the left hand's at 0.57, two bars made of
    /// one). Measured as a chord's wave is, over the rows the ink in the band runs through.
    /// </summary>
    public static bool IsWave(Img d, Staff st, double x, double s)
    {
        for (double xc = x - 0.4 * s; xc <= x + 0.4 * s; xc += Math.Max(1, 0.1 * s))
        {
            int x0 = (int)Math.Round(xc - 0.55 * s), x1 = (int)Math.Round(xc + 0.55 * s);
            double top = double.NaN, bottom = double.NaN;
            for (int y = (int)(st.Top - s); y <= (int)(st.Bottom + s); y++)
            {
                if (st.Lines.Any(q => y >= q.Top - 1 && y <= q.Bot + 1)) continue;
                bool ink = false;
                for (int cx = x0; cx <= x1 && !ink; cx++) ink = d[cx, y];
                if (!ink) continue;
                if (double.IsNaN(top)) top = y;
                bottom = y;
            }
            // (the rows Measure looks at run from top down to half a space under bottom)
            // (and a tenth of a space a swing: a photo's space of 13 pixels has the wave's centre swing
            // two, under the fifth of a space a chord's wave is looked for with)
            if (!double.IsNaN(top) && bottom - top >= 1.5 * s && Measure(d, st, xc, top, bottom - 0.5 * s, s, swing: Math.Max(1.2, 0.1 * s)).Ok) return true;
        }
        return false;
    }

    // what a band centred on xc shows over a chord's heads: the share of the rows whose ink lies inside it and runs short across it, how often that ink turns from
    // side to side, and the ink's left and right
    readonly record struct Band(double Cover, int Turns, int Odd, double L, double R, bool Ok);

    static Band Measure(Img d, Staff st, double xc, double top, double bottom, double s, Action<string> log = null, double swing = double.NaN)
    {
        int x0 = (int)Math.Round(xc - 0.55 * s), x1 = (int)Math.Round(xc + 0.55 * s);
        // (the rows Lily#'s pile covers: from half a space under the lowest head to the highest, widened
        // by half a space either way when that is under a space and a half, and whole glyphs a space tall
        // stacked up from the bottom over it; the ends a tenth in)
        double hi = top, lo = bottom + 0.5 * s;
        if (lo - hi < 1.4 * s) { hi -= 0.5 * s; lo += 0.5 * s; }
        // (the page's space measured to a pixel or two: a whole number of spaces is that many glyphs)
        int copies = (int)Math.Ceiling((lo - hi) / s - 0.1);
        int y0 = (int)Math.Round(lo - copies * s + 0.1 * s), y1 = (int)Math.Round(lo - 0.1 * s);
        int rows = 0, good = 0, split = 0, dir = 0, extY = 0;
        // (the rows the ink turned at, from one side to the other)
        var turns = new List<int>();
        var widths = new List<int>();
        // (a turn: back by a fifth of a space, the wave swinging some 0.36; a sign's ink in a scan jitters less)
        double ext = double.NaN, least = double.MaxValue, most = double.MinValue, h = double.IsNaN(swing) ? 0.2 * s : swing;
        double l = double.MaxValue, r = double.MinValue;
        for (int y = y0; y <= y1; y++)
        {
            // (a staff line's rows, taken out of the ink with the wave's strokes across them: not counted)
            if (st.Lines.Any(q => y >= q.Top - 1 && y <= q.Bot + 1)) continue;
            int a = -1, b = -1;
            for (int x = x0; x <= x1; x++)
                if (d[x, y]) { if (a < 0) a = x; b = x; }
            log?.Invoke($"  {y}: {new string(Enumerable.Range(x0 - 3, x1 - x0 + 7).Select(x => x == x0 - 1 || x == x1 + 1 ? '|' : d[x, y] ? '#' : '.').ToArray())} {a}-{b}");
            rows++;
            // (nothing, ink running in from outside the band, or too wide a row: not the wave's)
            if (a < 0 || (a == x0 && d[x0 - 1, y]) || (b == x1 && d[x1 + 1, y]) || b - a + 1 > 0.95 * s) continue;
            good++;
            widths.Add(b - a + 1);
            // (two runs a tenth of a space apart and more: a sharp's or a natural's two uprights)
            for (int x = a, gap = 0; x <= b; x++)
                if (d[x, y]) { if (gap >= Math.Max(2, 0.1 * s)) { split++; break; } gap = 0; } else gap++;
            l = Math.Min(l, a); r = Math.Max(r, b);
            double c = (a + b) / 2.0;
            least = Math.Min(least, c); most = Math.Max(most, c);
            if (double.IsNaN(ext)) { ext = c; extY = y; continue; }
            if (dir == 0) { if (Math.Abs(c - ext) > h) { dir = Math.Sign(c - ext); ext = c; extY = y; } }
            else if ((c - ext) * dir > 0) { ext = c; extY = y; }
            else if ((ext - c) * dir > h) { turns.Add(extY); log?.Invoke($"  turn at {extY}"); dir = -dir; ext = c; extY = y; }
        }
        double cover = rows == 0 ? 0 : good / (double)rows;
        // (the wave turns twice a glyph: once a glyph counts, and twice at least; and swings a quarter of a space and more)
        // (and evenly, half a space apart, but for one: a quarter rest's zigzag turns where it will)
        int odd = 0;
        for (int k = 1; k < turns.Count; k++)
            if (turns[k] - turns[k - 1] < 0.3 * s || turns[k] - turns[k - 1] > 0.75 * s) odd++;
        // (a beam may cross its top: a fifth of the rows may be another's)
        // (and thick across: the wave's slanting strokes a third of a space and more a row, a rest's stem
        // or a sign's upright a sixth)
        widths.Sort();
        bool thick = widths.Count > 0 && widths[widths.Count / 2] >= 0.22 * s;
        // (and one stroke a row: a sign's uprights split most of its rows in two)
        bool ok = cover >= 0.8 && turns.Count >= Math.Max(2, copies) && odd <= 1 && most - least >= 1.25 * h && thick && split <= 0.3 * good;
        return new Band(cover, turns.Count, odd, l, r, ok);
    }
}
