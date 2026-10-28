using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace Omr;

/// <summary>
/// Reads the bar number printed at the start of a system. Each digit is matched against
/// the digits 0-9 drawn with a serif system font: no training, just shape comparison.
/// </summary>
static class Digits
{
    const int GW = 16, GH = 24;
    public static bool Verbose;
    static readonly bool[][] Templates = BuildTemplates("Times New Roman", FontStyle.Regular).Select(t => t.Grid).ToArray();

    /// <summary>Time-signature digits are heavy: several bold serif faces, each digit with its hole count.</summary>
    static readonly List<(int Digit, bool[] Grid, int Holes)> Bold =
        new[] { "Times New Roman", "Georgia", "Cambria", "Century Schoolbook" }
            .SelectMany(f => BuildTemplates(f, FontStyle.Bold)).ToList();

    static List<(int Digit, bool[] Grid, int Holes)> BuildTemplates(string face, FontStyle style)
    {
        var t = new List<(int, bool[], int)>();
        for (int dgt = 0; dgt < 10; dgt++)
        {
            using var bmp = new Bitmap(120, 160, PixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.White);
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                using var font = new Font(face, 100, style, GraphicsUnit.Pixel);
                g.DrawString(dgt.ToString(), font, Brushes.Black, 5, 5);
            }
            var img = ToImg(bmp);
            var (_, comps) = Components.Label(img.W, img.H, i => img.D[i], eight: true);
            int minX = int.MaxValue, minY = int.MaxValue, maxX = 0, maxY = 0;
            foreach (var c in comps.Skip(1))
            {
                minX = Math.Min(minX, c.MinX); minY = Math.Min(minY, c.MinY);
                maxX = Math.Max(maxX, c.MaxX); maxY = Math.Max(maxY, c.MaxY);
            }
            int holes = Components.Holes(img).Count(h => h.W > 6);
            t.Add((dgt, Normalize(img, minX, minY, maxX, maxY), holes));
        }
        return t;
    }

    /// <summary>
    /// A time-signature digit by its shape features: loops (count and height), width, the
    /// width of its top and bottom rows, and the crossbar of a 4. Engraving fonts draw these
    /// digits differently from text fonts, but these traits hold across them.
    /// </summary>
    public static int TimeDigit(Img ink, Comp c, List<Comp> holes, out string why)
    {
        double w = c.W, h = c.H;
        // (an engraving font's loop can be a narrow slit: a 9's in Emmentaler)
        var loops = holes.Where(o => o.MinX >= c.MinX && o.MaxX <= c.MaxX && o.MinY >= c.MinY && o.MaxY <= c.MaxY && o.W > 0.05 * h && o.H > 0.1 * h).ToList();
        int Run(int y)
        {
            int best = 0, run = 0;
            for (int x = c.MinX; x <= c.MaxX; x++) { if (ink[x, y]) { run++; best = Math.Max(best, run); } else run = 0; }
            return best;
        }
        double Band(double f0, double f1)
        {
            int m = 0;
            for (int y = c.MinY + (int)(f0 * h); y <= c.MinY + (int)(f1 * h); y++) m = Math.Max(m, Run(y));
            return m / w;
        }
        // bands kept off the very edges, where a staff line's removal can nick the digit
        double top = Band(0.03, 0.15), bottom = Band(0.8, 0.97);
        bool bar = Band(0.55, 0.82) >= 0.85;
        double aspect = w / h;
        why = $"loops {loops.Count} aspect {aspect:F2} top {top:F2} bottom {bottom:F2} bar {bar}";
        if (loops.Count >= 2) return 8;
        if (loops.Count == 1)
        {
            double hy = (loops[0].Cy - c.MinY) / h;
            why += $" loopY {hy:F2}";
            if (hy < 0.45) return 9;
            if (hy > 0.55) return 6;
            return 0;
        }
        if (aspect < 0.55) return 1;
        // a 4 has an upright stroke through its whole height (and often a wide foot, like a 2)
        int vert = 0;
        for (int x = c.MinX; x <= c.MaxX; x++)
        {
            int run = 0;
            for (int y = c.MinY; y <= c.MaxY; y++) { if (ink[x, y]) { run++; vert = Math.Max(vert, run); } else run = 0; }
        }
        why += $" vert {vert / h:F2}";
        // an upright through the whole height: a 4 if a crossbar leaves it, else a 1 (with foot and flag)
        // strokes crossed going down the middle: a 1 is its stem only; a 3, 2 or 5 is crossed thrice
        int cx = c.MinX + (int)(0.45 * w), crossings = 0;
        bool inInk = false;
        for (int y = c.MinY; y <= c.MaxY; y++)
        {
            bool on = ink[cx, y];
            if (on && !inInk) crossings++;
            inInk = on;
        }
        why += $" crossings {crossings}";
        if (vert >= 0.6 * h && bar) return 4;
        if (vert >= 0.85 * h && crossings <= 1) return 1;
        // a 9 whose loop is too thin a slit to survive: its bowl has ink on both sides up top,
        // where a 3 is open on the left
        double Side(bool left, double f0, double f1)
        {
            int rows = 0, hit = 0;
            int x0 = left ? c.MinX : c.MaxX - (int)(0.25 * w), x1 = left ? c.MinX + (int)(0.25 * w) : c.MaxX;
            for (int y = c.MinY + (int)(f0 * h); y <= c.MinY + (int)(f1 * h); y++)
            {
                rows++;
                for (int x = x0; x <= x1; x++) if (ink[x, y]) { hit++; break; }
            }
            return rows == 0 ? 0 : hit / (double)rows;
        }
        double lu = Side(true, 0.3, 0.5), ru = Side(false, 0.3, 0.5);
        why += $" leftUp {lu:F2} rightUp {ru:F2}";
        if (lu >= 0.6 && ru >= 0.6 && bottom < 0.6) return 9;   // (a 2's curve is round up top too, but it stands on a wide foot)
        // likewise a 6: open on the right up top, a bowl (both sides) below
        double ll = Side(true, 0.6, 0.8), rl = Side(false, 0.6, 0.8);
        why += $" leftLow {ll:F2} rightLow {rl:F2}";
        if (lu >= 0.6 && ru < 0.55 && ll >= 0.6 && rl >= 0.6 && top < 0.7) return 6;   // (a 5's flat top is wide)
        // a wide top: a 7 is its top bar and one diagonal; a 5's middle is crossed three times
        if (top >= 0.7) return crossings <= 2 ? 7 : 5;
        if (bottom >= 0.7) return 2;
        if (top >= 0.7) return 5;
        return 3;
    }

    /// <summary>Best bold template among those with the same number of holes.</summary>
    public static int ClassifyBold(Img img, Comp c, int holes, out double score)
    {
        var g = Normalize(img, c.MinX, c.MinY, c.MaxX, c.MaxY);
        int best = -1;
        score = -1;
        foreach (var (dgt, grid, th) in Bold)
        {
            if (th != holes) continue;
            double iou = Iou(g, grid);
            if (iou > score) { score = iou; best = dgt; }
        }
        return best;
    }

    static double Iou(bool[] a, bool[] b)
    {
        int inter = 0, union = 0;
        for (int i = 0; i < a.Length; i++)
        {
            if (a[i] && b[i]) inter++;
            if (a[i] || b[i]) union++;
        }
        return union == 0 ? 0 : inter / (double)union;
    }

    static Img ToImg(Bitmap bmp)
    {
        var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
        var raw = new byte[data.Stride * bmp.Height];
        Marshal.Copy(data.Scan0, raw, 0, raw.Length);
        bmp.UnlockBits(data);
        var img = new Img(bmp.Width, bmp.Height);
        for (int y = 0; y < img.H; y++)
            for (int x = 0; x < img.W; x++)
                img.D[y * img.W + x] = raw[y * data.Stride + 3 * x] < 128;
        return img;
    }

    /// <summary>The box resampled to GW x GH, keeping the digit's aspect (a narrow "1" stays narrow).</summary>
    static bool[] Normalize(Img img, int x0, int y0, int x1, int y1)
    {
        var g = new bool[GW * GH];
        double h = y1 - y0 + 1, scale = h / GH;
        double w = x1 - x0 + 1, off = (GW - w / scale) / 2;
        for (int gy = 0; gy < GH; gy++)
            for (int gx = 0; gx < GW; gx++)
            {
                double sx = x0 + (gx - off + 0.5) * scale, sy = y0 + (gy + 0.5) * scale;
                g[gy * GW + gx] = sx >= x0 && sx <= x1 && img[(int)sx, (int)sy];
            }
        return g;
    }

    /// <summary>Text digits of several serif faces (bar numbers are set in the score's text font).</summary>
    static readonly List<(int Digit, bool[] Grid, int Holes)> Regular =
        new[] { "Times New Roman", "Century Schoolbook", "Georgia", "Cambria" }
            .SelectMany(f => BuildTemplates(f, FontStyle.Regular)).ToList();

    /// <summary>Loops of a glyph: white regions enclosed within its own box.</summary>
    public static int HoleCount(Img img, Comp c)
    {
        int w = c.W + 2, h = c.H + 2;
        var sub = new Img(w, h);
        for (int y = 0; y < c.H; y++)
            for (int x = 0; x < c.W; x++)
                sub.D[(y + 1) * w + x + 1] = img[c.MinX + x, c.MinY + y];
        return Components.Holes(sub).Count(o => o.W > 0.12 * c.H && o.H > 0.08 * c.H);
    }

    /// <summary>
    /// Digits learnt from this score: a printed bar number that agrees with the counted bar
    /// gives its digits' shapes, in whatever font the score uses. Later numbers are matched
    /// against these first.
    /// </summary>
    static readonly List<(int Digit, bool[] Grid, int Holes)> Learnt = new();

    public static void Learn(Img img, List<Comp> digits, int number)
    {
        var s = number.ToString();
        if (s.Length != digits.Count) return;
        for (int i = 0; i < digits.Count; i++)
        {
            int dgt = s[i] - '0';
            if (Learnt.Count(t => t.Digit == dgt) >= 3) continue;
            Learnt.Add((dgt, Normalize(img, digits[i].MinX, digits[i].MinY, digits[i].MaxX, digits[i].MaxY), HoleCount(img, digits[i])));
        }
    }

    public static int Classify(Img img, Comp c, out double score)
    {
        var g = Normalize(img, c.MinX, c.MinY, c.MaxX, c.MaxY);
        int holes = HoleCount(img, c);
        int best = -1;
        score = -1;
        foreach (var (dgt, grid, th) in Learnt)
        {
            if (th != holes) continue;
            double iou = Iou(g, grid);
            if (iou > score) { score = iou; best = dgt; }
        }
        if (score >= 0.7) return best;
        foreach (var (dgt, grid, th) in Regular)
        {
            if (th != holes) continue;
            double iou = Iou(g, grid);
            if (iou > score) { score = iou; best = dgt; }
        }
        return best;
    }

    /// <summary>The number above the first staff's start, or null.</summary>
    public static int? BarNumber(Img d, SystemInfo sys, double s)
    {
        var st = sys.Staves[0];
        int x0 = (int)(st.Left - 1.5 * s), x1 = (int)(st.Left + 4 * s);
        int y0 = (int)(st.Top - 5 * s), y1 = (int)(st.Top - 0.8 * s);
        var (_, comps) = Components.Label(d.W, d.H, i =>
        {
            int x = i % d.W, y = i / d.W;
            return x >= x0 && x <= x1 && y >= y0 && y <= y1 && d.D[i];
        }, eight: true);
        if (Verbose)
            foreach (var c in comps.Skip(1).Where(c => c.H > 0.3 * s))
                Console.WriteLine($"    barno? system {sys.Index + 1}: x {c.MinX}-{c.MaxX} y {c.MinY}-{c.MaxY} {c.W / s:F2}x{c.H / s:F2} -> {Classify(d, c, out double sc)} ({sc:F2}, holes {HoleCount(d, c)})");
        // a thin stroke can break at binarisation: pieces sharing their x range make one digit
        var pieces = comps.Skip(1).Where(c => c.H > 0.15 * s).OrderBy(c => c.MinX).ToList();
        var merged = new List<Comp>();
        foreach (var c in pieces)
        {
            var m = merged.FirstOrDefault(o => Math.Min(o.MaxX, c.MaxX) - Math.Max(o.MinX, c.MinX) > 0.5 * Math.Min(o.W, c.W));
            if (m == null) { merged.Add(new Comp { MinX = c.MinX, MaxX = c.MaxX, MinY = c.MinY, MaxY = c.MaxY, N = c.N }); continue; }
            m.MinX = Math.Min(m.MinX, c.MinX); m.MaxX = Math.Max(m.MaxX, c.MaxX);
            m.MinY = Math.Min(m.MinY, c.MinY); m.MaxY = Math.Max(m.MaxY, c.MaxY); m.N += c.N;
        }
        var digits = merged.Where(c => c.H >= 0.8 * s && c.H <= 1.5 * s && c.W >= 0.2 * s && c.W <= 1.0 * s).OrderBy(c => c.MinX).ToList();
        if (digits.Count == 0) return null;
        // one line of digits: same baseline
        int bottom = digits.Max(c => c.MaxY);
        digits = digits.Where(c => Math.Abs(c.MaxY - bottom) < 0.25 * s).ToList();
        int n = 0;
        LastScore = 1;
        foreach (var c in digits)
        {
            int v = Classify(d, c, out double sc);
            if (sc < 0.3) return null;
            n = n * 10 + v;
            LastScore = Math.Min(LastScore, sc);
        }
        LastDigits = digits;
        return n;
    }

    /// <summary>The digit boxes of the last number read (to learn from when it proves right).</summary>
    public static List<Comp> LastDigits = new();
    /// <summary>How well the least sure digit of the last number matched its shape (IoU, 0..1).</summary>
    public static double LastScore;
}
