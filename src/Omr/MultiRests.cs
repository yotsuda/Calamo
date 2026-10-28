namespace Omr;

/// <summary>
/// Multi-bar rests: one measure that stands for several bars of rest, on every staff of the system.
/// Lily# (as LilyPond) prints the count over the staff in a time signature's digits, and on the staff
/// the count in church rests -- a longa rest (two spaces tall, the second line to the fourth) for each
/// four bars, a breve rest (a space tall, the middle line to the fourth) for two, a whole rest for
/// one -- or, from ten bars on, a thick bar along the middle line between two uprights. The count is
/// read twice, from the digits and from the rests (principle 2: a structure, not a threshold). Under
/// ten the rests tell it, and the digits must be there: a breve rest alone is a 4/2 measure's rest.
/// From ten the digits alone tell it.
/// </summary>
static class MultiRests
{
    public static bool Verbose;     // --mmrests: what each measure with no heads shows

    public static void Find(PageResult res, Img ink, double s)
    {
        foreach (var sys in res.Systems)
            for (int m = 0; m < sys.Measures; m++)
            {
                double x0 = sys.Bars[m], x1 = sys.Bars[m + 1];
                if (x1 - x0 < 4 * s) continue;
                var counts = sys.Staves.Select(st => Count(res, ink, st, x0, x1, s, out _)).ToList();
                var seen = counts.Where(n => n > 1).Distinct().ToList();
                // (the staves agree: one count, read on more than half of them)
                if (seen.Count != 1 || 2 * counts.Count(n => n == seen[0]) <= counts.Count) continue;
                sys.Rests[x0] = seen[0];
                foreach (var (st, k) in sys.Staves.Select((st, k) => (st, k)))
                {
                    Count(res, ink, st, x0, x1, s, out string how);
                    res.Notes.Add($"page {res.Page} system {sys.Index + 1}: a multi-bar rest of {seen[0]} at x {x0:F0}-{x1:F0}, part {st.Part}: {how}");
                }
            }
    }

    /// <summary>The bars the measure rests on the staff, or 0 when it shows no multi-bar rest.</summary>
    static int Count(PageResult res, Img ink, Staff st, double x0, double x1, double s, out string how)
    {
        how = "";
        var sym = res.Sym;
        // (past the signatures at the measure's start: a key's X is about its first sign's left, each sign about a space wide;
        // a time signature's digits each about 1.2 spaces, 12/8's wider than 3/4's)
        double from = sym.Sigs.Where(g => g.St == st && g.X > x0 - 0.5 * s && g.X < x1 && !g.Mid)
            .Select(g => g.X + (g.Fifths is int f ? 1.1 * Math.Abs(f) * s + 1.0 * s : g.Clef != null ? 3.0 * s
                : (0.8 + 1.2 * (g.Time?.Split('/').Max(t => t.Length) ?? 1)) * s))
            .Append(x0).Max() + 0.3 * s;
        int xa = (int)from, xb = (int)(x1 - 0.3 * s);
        if (xb - xa < 2 * s) return 0;
        double mid = st.Mid, line4 = st.Mid - s;
        // the rests on the staff: nothing else may stand there
        int church = 0;
        Comp bar = null;
        var rests = new List<Comp>();
        foreach (var c in Signatures.SubComps(ink, xa, (int)(st.Top - 0.3 * s), xb, (int)(st.Bottom + 0.3 * s)))
        {
            if (c.N < 0.05 * s * s) continue;
            double w = c.W / s, h = c.H / s;
            if (Verbose) Console.WriteLine($"    rests? s{st.System + 1} part {st.Part} x {x0:F0}: piece at {c.MinX},{c.MinY} {w:F2}x{h:F2} fill {c.Fill:F2}, middle {(c.Cy - mid) / s:F2} spaces under the middle line, top {(c.MinY - line4) / s:F2} under the fourth");
            if (w >= 3 && h <= 2.6 && Math.Abs(c.Cy - mid) < 0.6 * s && bar == null) { bar = c; continue; }
            if (c.Fill < 0.8) return 0;
            if (w >= 0.8 && w <= 1.8 && h >= 0.3 && h <= 0.75 && Math.Abs(c.MinY - line4) < 0.3 * s) church += 1;
            else if (w >= 0.3 && w <= 0.9 && h >= 0.75 && h <= 1.3 && Math.Abs(c.Cy - (mid - 0.5 * s)) < 0.35 * s) church += 2;
            else if (w >= 0.3 && w <= 0.9 && h >= 1.7 && h <= 2.4 && Math.Abs(c.Cy - mid) < 0.4 * s) church += 4;
            else return 0;
            rests.Add(c);
        }
        if (bar != null && rests.Count > 0 || bar == null && church < 2) return 0;
        // no notes on the staff, but for a "head" on a rest (the detector sees a longa rest as a slash)
        bool OnRest(Head h) => rests.Append(bar).Any(c => c != null && h.X > c.MinX - 0.3 * s && h.X < c.MaxX + 0.3 * s && h.Y > c.MinY - 0.3 * s && h.Y < c.MaxY + 0.3 * s);
        // (nor among the signatures at the start: on a scan the rules take a bass clef's ball for a head)
        var note = sym.Heads.FirstOrDefault(h => h.St == st && h.X > from && h.X < x1 && !OnRest(h));
        if (note != null)
        {
            if (Verbose) Console.WriteLine($"    rests? s{st.System + 1} part {st.Part} x {x0:F0}: a head at {note.X:F0},{note.Y:F0}");
            return 0;
        }
        // the count over the staff, its digits a time signature's (two spaces tall, just clear of the top line),
        // cut from the page as binarized: the ink has lost their strokes along the ledger lines' rows
        var page = res.Dark;
        double cx = bar != null ? (bar.MinX + bar.MaxX) / 2.0 : rests.Average(c => (c.MinX + c.MaxX) / 2.0);
        double half = bar != null ? bar.W / 2.0 + s : 3 * s;
        // (no higher than a digit's height over the line: a title or a tempo above is no part of it)
        int top = (int)(st.Top - 2.8 * s);
        var digits = Signatures.SubComps(page, (int)(cx - half), top, (int)(cx + half), (int)(st.Top - 0.1 * s))
            .SelectMany(c => Split(page, c))
            .Where(c => c.H >= 1.3 * s && c.H <= 2.6 * s && c.W >= 0.3 * s && c.W <= 2.0 * s && c.MaxY > st.Top - 1.5 * s)
            .OrderBy(c => c.MinX).ToList();
        // (side by side, a digit's gap apart)
        for (int i = 1; i < digits.Count; i++)
            if (digits[i].MinX - digits[i - 1].MaxX > 0.8 * s) { digits.RemoveRange(i, digits.Count - i); break; }
        if (digits.Count == 0)
        {
            if (Verbose) Console.WriteLine($"    rests? s{st.System + 1} part {st.Part} x {x0:F0}: no digits over {cx:F0} ("
                + string.Join(", ", Signatures.SubComps(page, (int)(cx - half), top, (int)(cx + half), (int)(st.Top - 0.1 * s)).Select(c => $"{c.MinX},{c.MinY} {c.W / s:F2}x{c.H / s:F2}")) + ")");
            return 0;
        }
        int? number = 0;
        foreach (var c in digits)
        {
            var f = GlyphKnn.BoxFeatures(page, c, s);
            sym.TimeSamples.Add(("d", c, f));
            // (the classifier's 0s are from the counts over multi-bar rests: no time signature has one)
            var (label, dist) = GlyphKnn.Enabled ? GlyphKnn.Classify(f, family: "d:") : ("d:none", double.MaxValue);
            number = dist <= 3.0 && int.TryParse(label[2..], out int v) ? number * 10 + v : null;
            if (Verbose) Console.WriteLine($"    rests? s{st.System + 1} part {st.Part} x {x0:F0}: digit at {c.MinX},{c.MinY} {c.W / s:F2}x{c.H / s:F2}: {label} {dist:F2}");
        }
        string rule = bar != null ? "a bar" : $"church rests of {church}";
        how = $"{rule}, digits {(number is int nv ? nv.ToString() : "unread")}";
        if (bar != null) return number is int n && n >= 10 ? n : 0;
        if (number is int d && d != church) how += $": the rests' {church} taken";
        return church;
    }

    /// <summary>Two digits that touch ("12"), cut at their waist: the thinnest column of the middle (as Signatures cuts a time signature's).</summary>
    static IEnumerable<Comp> Split(Img page, Comp d)
    {
        if (d.W <= 1.15 * d.H) { yield return d; yield break; }
        int cut = -1, least = int.MaxValue;
        for (int cx = d.MinX + (int)(0.3 * d.W); cx <= d.MinX + (int)(0.7 * d.W); cx++)
        {
            int n = 0;
            for (int y = d.MinY; y <= d.MaxY; y++) if (page[cx, y]) n++;
            if (n < least) { least = n; cut = cx; }
        }
        foreach (var part in new[] { (d.MinX, cut - 1), (cut + 1, d.MaxX) })
        {
            // (the half's whole ink, not its largest piece only)
            var all = Signatures.SubComps(page, part.Item1, d.MinY, part.Item2, d.MaxY);
            if (all.Count == 0) continue;
            yield return new Comp { N = all.Sum(o => o.N), MinX = all.Min(o => o.MinX), MaxX = all.Max(o => o.MaxX), MinY = all.Min(o => o.MinY), MaxY = all.Max(o => o.MaxY),
                                    Sx = all.Sum(o => o.Sx), Sy = all.Sum(o => o.Sy) };
        }
    }
}
