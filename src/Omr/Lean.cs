namespace Omr;

/// <summary>
/// A page photographed from a little off square is a trapezoid: once Skew has turned its staff lines
/// level, the lines that should stand upright still lean, and lean more the further across the page
/// they stand (the Scoring Notes photo of the Nutcracker score). FindBars gathers the barlines of a
/// system by their column, and a system holds one column for each barline, so on a tall system one
/// barline made two (its third page's 5 bars read as 9). What leans is read off the long upright lines
/// themselves: each row's columns slide by (y - Cy) * (A + B * (x - Cx)), nothing at the page's middle row.
/// </summary>
sealed class Lean
{
    public double A, B, Cx, Cy;     // the lean at the page's middle column (columns per row), its change per column; the middle
    public int Lines;               // how many upright lines it was measured on
    public double Most;             // the most it moves a line's end, in columns
    public double Parted;           // the most a line's two ends stand apart, in spaces

    /// <summary>The columns the point of the upright page lies to the right of, on the page as it leaned.</summary>
    public double At(double x, double y) => (y - Cy) * (A + B * (x - Cx));

    public Img Upright(Img img)
    {
        int w = img.W, h = img.H;
        var o = new Img(w, h);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int sx = (int)Math.Round(x + At(x, y));
                if (sx >= 0 && sx < w) o.D[y * w + x] = img.D[y * w + sx];
            }
        return o;
    }

    public byte[] Upright(byte[] g, int w, int h)
    {
        var o = new byte[w * h];
        Array.Fill(o, (byte)255);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int sx = (int)Math.Round(x + At(x, y));
                if (sx >= 0 && sx < w) o[y * w + x] = g[y * w + sx];
            }
        return o;
    }

    public override string ToString() => $"upright lines lean {A * 1000:F2} columns per 1000 rows at the page's middle, {B * 1e6:F3} more per 1000 columns; {Lines} lines, the most moved {Most:F1} columns, a line's ends {Parted:F2} spaces apart";

    /// <summary>
    /// The lean of a page, from its long straight upright lines (barlines through two staves and more,
    /// a system's opening line): null when it has too few of them, or when they lean too little over
    /// their own length to matter -- a clean engraving's stand as they are, and it is left untouched.
    /// </summary>
    /// <param name="space">the staff space as estimated from the grey page (0: unknown, the page is left as it is)</param>
    public static Lean Measure(Img img, double space)
    {
        if (space <= 0) return null;
        int w = img.W, h = img.H;
        var lines = UprightLines(img, space);
        if (lines.Count < MinLines) return null;
        double cx = w / 2.0, cy = h / 2.0;
        // (a fit weighted by each line's length, and the lines that disagree left out: a stem chain
        // or a part's name that happens to pass as a line)
        var keep = lines;
        double a = 0, b = 0;
        for (int round = 0; round < 3; round++)
        {
            (a, b) = Fit(keep, cx);
            double fa = a, fb = b;
            var next = keep.Where(l => Math.Abs(l.Tilt - (fa + fb * (l.X - cx))) <= Outlier).ToList();
            if (next.Count < MinLines) return null;
            keep = next;
        }
        (a, b) = Fit(keep, cx);
        if (Img.Trace) Console.WriteLine($"  lean: {keep.Count} of {lines.Count} lines kept: {string.Join(" ", keep.OrderBy(l => l.X).Select(l => $"x{l.X:F0}:{l.Tilt * 1000:F1}"))}");
        // (lines in one column tell how much they lean there and not across the page: a band score's
        // systems, their barlines a staff tall, have only their opening lines, and leaning the whole
        // page by those moved its right side the wrong way -- gen band with a keystone, heads off 123 -> 458)
        if (keep.Max(l => l.X) - keep.Min(l => l.X) < w / 4.0) return null;
        var lean = new Lean { A = a, B = b, Cx = cx, Cy = cy, Lines = keep.Count };
        lean.Most = keep.Max(l => Math.Max(Math.Abs(lean.At(l.X, l.Top)), Math.Abs(lean.At(l.X, l.Bottom))));
        lean.Parted = keep.Max(l => Math.Abs(a + b * (l.X - cx)) * l.Length) / space;
        if (Img.Trace) Console.WriteLine($"  lean: {lean}");
        // (only where it does harm: a line whose ends part by more than FindBars' gathering holds, half a space,
        // is a system tall enough to split a barline in two. Old prints' songs lean about as much, on systems
        // of three staves that read well as they are, and standing them up only roughened them,
        // read with a few lines' fit carried to the page's corners -- kinkel7-1's scan 915 -> 887)
        return lean.Parted >= MinParted ? lean : null;
    }

    const int MinLines = 3;
    const double Outlier = 0.004;           // columns per row off the fit (a quarter of a degree)
    const double MinParted = 0.5;           // spaces between a line's two ends for the page to be touched at all

    public readonly record struct Line(double X, double Tilt, double Length, double Top, double Bottom);

    /// <summary>
    /// The page's long upright lines: thin runs of ink followed down the page, column to column, past
    /// the staff lines that cross them, ten spaces and more (longer than any stem, as long as a
    /// barline through two staves with a gap), straight within a sixth of a space (a brace is not).
    /// </summary>
    public static List<Line> UprightLines(Img img, double space)
    {
        int w = img.W, h = img.H;
        int thin = Math.Max(3, (int)Math.Ceiling(0.45 * space));     // a line's width at most, in columns
        int minLen = (int)(10 * space), gap = Math.Max(2, (int)(0.2 * space)), wide = Math.Max(3, (int)(0.5 * space));
        int margin = w / 50;
        var seen = new bool[w * h];
        var found = new List<Line>();
        var ys = new List<int>(); var xs = new List<double>();
        for (int y0 = 0; y0 < h; y0++)
            for (int x0 = margin; x0 < w - margin; x0++)
            {
                if (!img.D[y0 * w + x0] || seen[y0 * w + x0]) continue;
                var (a0, z0) = RunAt(img, x0, y0);
                if (z0 - a0 + 1 > thin) { for (int x = a0; x <= z0; x++) seen[y0 * w + x] = true; x0 = z0; continue; }
                ys.Clear(); xs.Clear();
                double at = (a0 + z0) / 2.0;
                int miss = 0, over = 0, y = y0;
                for (; y < h; y++)
                {
                    // (the ink at the line's column, or a column to either side: it drifts as it leans)
                    int cx = (int)Math.Round(at), hit = -1;
                    foreach (int dx in new[] { 0, -1, 1 })
                        if (cx + dx >= 0 && cx + dx < w && img.D[y * w + cx + dx]) { hit = cx + dx; break; }
                    if (hit < 0) { if (++miss > gap) break; continue; }
                    miss = 0;
                    var (a, z) = RunAt(img, hit, y);
                    if (z - a + 1 > thin) { if (++over > wide) break; continue; }   // a staff line or a beam across it
                    over = 0;
                    for (int x = a; x <= z; x++) seen[y * w + x] = true;
                    at = (a + z) / 2.0;
                    ys.Add(y); xs.Add(at);
                }
                if (ys.Count < minLen || ys[^1] - ys[0] + 1 < minLen) continue;
                // the straight line through the centres
                int n = ys.Count;
                double my = ys.Average(), mx = xs.Average(), syy = 0, sxy = 0;
                for (int i = 0; i < n; i++) { syy += (ys[i] - my) * (ys[i] - my); sxy += (ys[i] - my) * (xs[i] - mx); }
                double t = sxy / syy, err = 0;
                for (int i = 0; i < n; i++) { double e = xs[i] - (mx + t * (ys[i] - my)); err += e * e; }
                if (Math.Sqrt(err / n) > space / 6 || Math.Abs(t) > 0.05) continue;
                found.Add(new Line(mx, t, ys[^1] - ys[0] + 1, ys[0], ys[^1]));
            }
        return found;
    }

    static (int, int) RunAt(Img img, int x, int y)
    {
        int w = img.W, o = y * w, a = x, z = x;
        while (a > 0 && img.D[o + a - 1]) a--;
        while (z < w - 1 && img.D[o + z + 1]) z++;
        return (a, z);
    }

    /// <summary>The lean at the middle column and its change per column, the lines weighted by their length.</summary>
    static (double A, double B) Fit(List<Line> lines, double cx)
    {
        double sw = 0, sx = 0, st = 0, sxx = 0, sxt = 0;
        foreach (var l in lines)
        {
            double x = l.X - cx;
            sw += l.Length; sx += l.Length * x; st += l.Length * l.Tilt; sxx += l.Length * x * x; sxt += l.Length * x * l.Tilt;
        }
        double den = sw * sxx - sx * sx;
        if (Math.Abs(den) < 1e-6) return (st / sw, 0);
        double b = (sw * sxt - sx * st) / den;
        return ((st - b * sx) / sw, b);
    }
}
