using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Omr;

/// <summary>A binary page image: true = ink.</summary>
sealed class Img
{
    public readonly int W, H;
    public readonly bool[] D;

    public Img(int w, int h) { W = w; H = h; D = new bool[w * h]; }

    public bool this[int x, int y]
    {
        get => (uint)x < (uint)W && (uint)y < (uint)H && D[y * W + x];
        set { if ((uint)x < (uint)W && (uint)y < (uint)H) D[y * W + x] = value; }
    }

    public Img Clone()
    {
        var c = new Img(W, H);
        Array.Copy(D, c.D, D.Length);
        return c;
    }

    /// <summary>
    /// Binarizes the page. A scan is grainy and its paper is not white: the grain goes with a
    /// 3x3 median, and the threshold is where the two tones (paper, ink) part (Otsu). A clean
    /// engraving is untouched by either.
    /// </summary>
    // how the page is cleaned before binarizing: 0 none, 1 lone pixels dropped, 2 a 3x3 median
    public static int Denoise = 1;
    public static bool OtsuThreshold = true;

    public static Img Load(string path)
    {
        using var bmp = new Bitmap(path);
        var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
        var data = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
        var raw = new byte[data.Stride * bmp.Height];
        Marshal.Copy(data.Scan0, raw, 0, raw.Length);
        bmp.UnlockBits(data);
        int w = bmp.Width, h = bmp.Height;
        var gray = new byte[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int o = y * data.Stride + 3 * x;
                gray[y * w + x] = (byte)((raw[o] + raw[o + 1] + raw[o + 2]) / 3);
            }
        // a page scanned or saved small (IMSLP's 96 dpi pages: a space of 6 or 7 pixels, a staff
        // line a grey smear under one) is read enlarged, in grey before it is binarized, to the
        // space of a 300 dpi scan: every measure of the reader is tuned there, and at 7 pixels a
        // space it found no staff at all (gen-lo13: none of 2118 notes; at 10 pixels 10-52 percent)
        double zoom = 1;
        double est = NoZoom ? 0 : GraySpacing(gray, w, h);
        if (est > 0 && est < ZoomBelow)
        {
            zoom = ZoomTo / est;
            // (the thin lines made solid only where they are under a pixel: at 14 pixels a space
            // a line is one and a half, and the ridges only fattened it -- gen-lo25 piano 91 -> 81 percent)
            (gray, w, h) = Enlarge(est < RidgesBelow ? Ridges(gray, w, h) : gray, w, h, zoom);
        }
        LastZoom = zoom;
        var smooth = Denoise == 2 ? Median3(gray, w, h) : gray;
        int threshold = OtsuThreshold ? Otsu(smooth) : 128;
        var img = new Img(w, h);
        for (int i = 0; i < smooth.Length; i++) img.D[i] = smooth[i] < threshold;
        // (only a grainy page: a clean engraving has no lone pixels, and the cleaning would only
        // touch its finest strokes)
        LastGrainy = Denoise == 1 && Grainy(img);
        // (a black-and-white scan's staff lines, grey once blurred, can come out as lone dots that
        // the cleaning takes away: the page before it is kept for finding the staves)
        var unclean = LastGrainy ? img.Clone() : null;
        if (LastGrainy) Despeckle(img);
        // a scan lies a little askew on the glass: turn it so that the staff lines run level
        double angle = Skew(img);
        if (Math.Abs(angle) >= 0.03) { img = Rotate(img, -angle); if (unclean != null) unclean = Rotate(unclean, -angle); }
        // and a page that bows, its tilt changing down the page, each row by its own (a scanned
        // page only, a grainy one: a clean engraving is level by its making, and the strips of
        // one, matched on its words and beams, bowed it where it was not -- faisst4-5 drawn by
        // MuseScore, 15 rows at the left of a band, 92.7 -> 77.5 percent)
        // (only a page whose quarters tilt unalike, by the long level runs of their lines, as
        // before: a clean engraving's agree, and its strips matched on words and beams bowed it
        // where it was not -- faisst4-5 drawn by MuseScore, 92.7 -> 77.5 percent; a black-and-white
        // scan is neither grainy once scaled nor grey of paper -- kinkel10-2 -- but bows all the same)
        LastBow = null;
        var bow = Bow(img, zoom != 1 ? ZoomTo : est);
        if (bow != null) { img = Unbow(img, bow); if (unclean != null) unclean = Unbow(unclean, bow); }
        img.Raw = unclean;
        img.Turned = Math.Abs(angle) >= 0.03 ? -angle : 0;
        img.Bowed = bow;
        img.Zoom = zoom;
        if (SaveBinarized != null) img.SavePng(SaveBinarized);
        // the grey page turned level, in the same frame as everything the reader reports: what
        // the learnt detector reads (--save-gray writes it out)
        img.Gray = Math.Abs(angle) >= 0.03 ? RotateGray(gray, w, h, -angle) : gray;
        if (bow != null) img.Gray = UnbowGray(img.Gray, w, h, bow);
        if (SaveGray != null) SaveGrayPng(SaveGray, w, h, img.Gray);
        return img;
    }

    /// <summary>
    /// The columns of row <paramref name="y"/> that are a scan's black edge: the run of ink from
    /// the page's left side and the run to its right side (a book's gutter, the scanner's lid
    /// beyond the page's end, whole black rows at the top and bottom), which no staff line reaches.
    /// Returns the first column past the left run and the first column of the right run.
    /// </summary>
    public (int From, int To) EdgeFree(int y)
    {
        int o = y * W, a = 0, b = W;
        while (a < W && D[o + a]) a++;
        while (b > a && D[o + b - 1]) b--;
        return (a, b);
    }

    public byte[] Gray;                     // the page in grey (0 = ink), turned level like D
    public Img Raw;                         // a grainy page as binarized, before its lone pixels were cleaned away
    public double Turned;                   // the degrees the page was turned by (0: not turned)
    public Warp Bowed;                      // how the page was levelled cell by cell (null: not bowed)
    public double Zoom = 1;                 // how much the page was enlarged by before reading (1: not)

    /// <summary>Where a point of the levelled page lies on the page as loaded (undoes Unbow, then Rotate, then the enlarging).</summary>
    public (double X, double Y) ToSource(double x, double y)
    {
        double cx = W / 2.0, cy = H / 2.0;
        if (Bowed != null) y += Bowed.At(x, y);
        if (Turned != 0)
        {
            double r = Turned * Math.PI / 180, cos = Math.Cos(r), sin = Math.Sin(r), dx = x - cx, dy = y - cy;
            (x, y) = (cx + dx * cos + dy * sin, cy - dx * sin + dy * cos);
        }
        return (x / Zoom, y / Zoom);
    }

    public static bool NoZoom;              // --no-zoom: read a small page as it is (to tell what the enlarging does)
    // (with the detector the gain is all under 12: gen-lo18's 10 pixels 90 -> 100 percent, gen-lo25's 14
    // already 100 without; and the held-out lied-boulanger-scan, 16 pixels, fell 535 -> 405 notes enlarged)
    public static double ZoomBelow = 12;    // a page whose space measures under this many pixels is enlarged,
    public static double ZoomTo = 28;       // to this space (OmrDegrade's scan of the generated set, gen-scan)
    public static double LastZoom = 1;
    // (12, as every enlarged page: 11 left out pages measured 11.0 whose lines are as thin -- data/dev's
    // kinkel19-5 page 2 kept two lines of five; Kinkel Op.19's five songs 755 -> 915 notes)
    public static double RidgesBelow = 12;  // and one under this many is enlarged with its thin lines made solid (Ridges)

    /// <summary>
    /// A small page's thin lines made solid before it is enlarged: a staff line or a stem under a
    /// pixel thick is a pale grey row or column, shared between two, that no threshold keeps
    /// without fattening every head (gen-lo13: three lines of five left at the halfway grey). A
    /// pixel darker than its two neighbours across (above and below, or left and right) whose three
    /// together hold as much ink as one pixel at halfway grey is a line's middle, and is set to the
    /// page's ink; a head, a beam -- wider than three pixels -- is no such ridge, and stays. On a
    /// line the ridge is told from the grey averaged along it, and a line shared evenly between two
    /// rows is set a shade under the threshold in both.
    /// </summary>
    static byte[] Ridges(byte[] g, int w, int h)
    {
        var hist = new long[256];
        foreach (var v in g) hist[v]++;
        int paper = Array.IndexOf(hist, hist.Max());
        // (the ink's grey: the darkest percent of the dark pixels)
        long dark = 0;
        for (int i = 0; i < paper - 64 && i < 256; i++) dark += hist[i];
        int ink = 0;
        for (long seen = 0; ink < paper && seen + hist[ink] <= dark / 100; ink++) seen += hist[ink];
        double need = 0.5 * (paper - ink);
        // a line's middle is told from the grey averaged along it (a row across, a column down): a
        // line shared between two rows is about as dark in both, which of them a shade darker
        // changing along it with the grain. Pixel by pixel it came out beaded, each bead read for a
        // dot (gen-lo13, 356 dots wrong); both rows made ink, it came out twice as thick as the
        // others, and what its removal left was read for whole rests and dots (gen-lo13 piano, 40)
        const int R = 5;
        var gh = Smooth(g, w, h, 1, R);
        var gv = Smooth(g, w, h, w, R);
        int tol = (int)(0.2 * (paper - ink));
        // (two rows within this of each other are even: on an evenly shared line the darker of the
        // two changed with the grain, the line stepping between them, each step read for a tie --
        // gen-lo13, 6 -- and the lower of the two taken moved it half a pixel down, the dots and
        // half rests beside it read on its wrong side. Both are set a shade under the threshold
        // instead: enlarged, the band between them is a row thick, where the line lies)
        double even = 0.1 * (paper - ink);
        byte mid = (byte)Math.Clamp(128 - 0.05 * (paper - ink), ink, 127);
        var o = (byte[])g.Clone();
        Parallel.For(2, h - 2, y =>
        {
            for (int x = 2; x < w - 2; x++)
            {
                int i = y * w + x;
                if (g[i] >= paper) continue;
                // (paler on one side by as much: along a line, a shade's wobble is no ridge across
                // it). Only where the averaged grey shows a line: a dot, an accidental's or a rest's
                // short stroke is gone in the average, and keeps the pixel's own ridge (gen-lo13, 23
                // dots and rests lost)
                byte v = 255;
                foreach (var (a, step) in new[] { (gh, w), (gv, 1) })
                {
                    if (!Line(a, i, step)) { if (Local(i, step)) v = (byte)ink; }
                    // (and the pixel itself holding ink: the average runs on past a stem's end, a
                    // stroke's, and spurred them)
                    else if (paper - g[i] >= tol && Ridge(a, i, step))
                        v = Math.Min(v, Math.Min(Math.Abs(a[i] - a[i - step]), Math.Abs(a[i] - a[i + step])) < even ? mid : (byte)ink);
                }
                if (v < 255) o[i] = Math.Min(g[i], v);
            }
        });
        return o;

        bool Line(double[] a, int i, int step) => Ridge(a, i - step, step) || Ridge(a, i, step) || Ridge(a, i + step, step);
        bool Ridge(double[] a, int i, int step) => Dark(a[i - step], a[i], a[i + step], even, even);
        // (the pixel's own: as dark as both sides within the paleness, both rows of a line taken)
        bool Local(int i, int step) => Dark(g[i - step], g[i], g[i + step], tol, tol);
        bool Dark(double up, double v, double down, double above, double below) =>
            v <= up + above && v <= down + below && Math.Max(up, down) - v >= tol
            && Math.Max(0, paper - up) + Math.Max(0, paper - v) + Math.Max(0, paper - down) >= need;
    }

    /// <summary>The grey page averaged over r pixels each way along one direction (step 1: a row, step w: a column).</summary>
    static double[] Smooth(byte[] g, int w, int h, int step, int r)
    {
        var o = new double[g.Length];
        Parallel.For(0, h, y =>
        {
            for (int x = 0; x < w; x++)
            {
                int along = step == 1 ? x : y, n = step == 1 ? w : h;
                int a = Math.Max(0, along - r), b = Math.Min(n - 1, along + r);
                double sum = 0;
                for (int k = a; k <= b; k++) sum += step == 1 ? g[y * w + k] : g[k * w + x];
                o[y * w + x] = sum / (b - a + 1);
            }
        });
        return o;
    }

    /// <summary>
    /// The page's staff spacing from its grey rows (0 where none shows): where the rows' darkness
    /// agrees with itself shifted, as <see cref="Layout"/>'s guess does on the binarized page -- but
    /// from 3 pixels up, and in grey, as a line under a pixel thick is a faint grey row.
    /// </summary>
    static double GraySpacing(byte[] g, int w, int h)
    {
        var row = new double[h];
        for (int y = 0; y < h; y++)
        {
            double sum = 0;
            for (int x = 0, o = y * w; x < w; x++) sum += 255 - g[o + x];
            row[y] = sum;
        }
        // an old print's paper shading down the page can make the double of the space agree better
        // than the space itself (data/dev's kinkel19-5 page 2: 21.5 for 11): the rows less their
        // running mean, 17 rows, find the space; that one is taken only where the two part by a fifth,
        // as it moves every other page's by a few hundredths, and the enlarged page's reading with it
        // (gen-lo7 basic 99.7 -> 98.9 percent)
        double plain = RowSpacing(row);
        const int R = 8;
        var flat = new double[h];
        for (int y = 0; y < h; y++)
        {
            double s = 0;
            for (int k = -R; k <= R; k++) s += row[Math.Clamp(y + k, 0, h - 1)];
            flat[y] = row[y] - s / (2 * R + 1);
        }
        double level = RowSpacing(flat);
        return plain > 0 && level > 0 && Math.Abs(plain - level) > 0.2 * level ? level : plain;
    }

    static double RowSpacing(double[] row)
    {
        int h = row.Length;
        double mean = row.Average();
        const int Max = 100;
        var corr = new double[Max + 1];
        for (int p = 1; p <= Max; p++)
            for (int y = 0; y + p < h; y++) corr[p] += (row[y] - mean) * (row[y + p] - mean);
        var rise = new double[Max];
        for (int p = 3; p < Max - 20; p++)
        {
            int k = Math.Max(1, (int)Math.Round(p / 5.0));
            rise[p] = corr[p] - 0.5 * (corr[p - k] + corr[p + k]);
        }
        double top = rise.Max();
        for (int p = 3; p < Max - 20 && top > 0; p++)
            if (rise[p] >= 0.5 * top && rise[p] >= rise[p - 1] && rise[p] >= rise[p + 1])
            {
                double a = corr[p - 1], b = corr[p], c = corr[p + 1], den = a - 2 * b + c;
                return p + (den < 0 ? Math.Clamp(0.5 * (a - c) / den, -0.5, 0.5) : 0);
            }
        return 0;
    }

    /// <summary>The grey page enlarged by the factor (bilinear: a grey edge stays a grey edge).</summary>
    static (byte[] G, int W, int H) Enlarge(byte[] g, int w, int h, double f)
    {
        int nw = (int)Math.Round(w * f), nh = (int)Math.Round(h * f);
        var o = new byte[nw * nh];
        Parallel.For(0, nh, y =>
        {
            double sy = Math.Clamp((y + 0.5) / f - 0.5, 0, h - 1);
            int y0 = (int)sy, y1 = Math.Min(y0 + 1, h - 1);
            double fy = sy - y0;
            for (int x = 0; x < nw; x++)
            {
                double sx = Math.Clamp((x + 0.5) / f - 0.5, 0, w - 1);
                int x0 = (int)sx, x1 = Math.Min(x0 + 1, w - 1);
                double fx = sx - x0;
                double top = g[y0 * w + x0] + fx * (g[y0 * w + x1] - g[y0 * w + x0]);
                double bot = g[y1 * w + x0] + fx * (g[y1 * w + x1] - g[y1 * w + x0]);
                o[y * nw + x] = (byte)Math.Round(top + fy * (bot - top));
            }
        });
        return (o, nw, nh);
    }
    public static string SaveGray;

    static byte[] RotateGray(byte[] g, int w, int h, double degrees)
    {
        double r = degrees * Math.PI / 180, cos = Math.Cos(r), sin = Math.Sin(r);
        double cx = w / 2.0, cy = h / 2.0;
        var o = new byte[w * h];
        Array.Fill(o, (byte)255);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                double dx = x - cx, dy = y - cy;
                int sx = (int)Math.Round(cx + dx * cos + dy * sin), sy = (int)Math.Round(cy - dx * sin + dy * cos);
                if (sx >= 0 && sx < w && sy >= 0 && sy < h) o[y * w + x] = g[sy * w + sx];
            }
        return o;
    }

    internal static void SaveGrayPng(string path, int w, int h, byte[] g)
    {
        using var bmp = new Bitmap(w, h, PixelFormat.Format24bppRgb);
        var rect = new Rectangle(0, 0, w, h);
        var data = bmp.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
        var raw = new byte[data.Stride * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int o = y * data.Stride + 3 * x;
                raw[o] = raw[o + 1] = raw[o + 2] = g[y * w + x];
            }
        Marshal.Copy(raw, 0, data.Scan0, raw.Length);
        bmp.UnlockBits(data);
        bmp.Save(path, ImageFormat.Png);
    }

    public static double LastSkew;
    public static bool LastGrainy;         // the page read last was grainy (a scan): its pinholes get filled, too
    public static string SaveBinarized;     // --save-bin <png>: the page as read (binarized, turned level)
    public static string SaveInk;           // --save-ink <png>: the page's ink with the staff lines taken out (Layout.MakeInk), "-pN" before the extension

    public void SavePng(string path)
    {
        using var bmp = new Bitmap(W, H, PixelFormat.Format24bppRgb);
        var rect = new Rectangle(0, 0, W, H);
        var data = bmp.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
        var raw = new byte[data.Stride * H];
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                byte v = D[y * W + x] ? (byte)0 : (byte)255;
                int o = y * data.Stride + 3 * x;
                raw[o] = raw[o + 1] = raw[o + 2] = v;
            }
        Marshal.Copy(raw, 0, data.Scan0, raw.Length);
        bmp.UnlockBits(data);
        bmp.Save(path, ImageFormat.Png);
    }

    /// <summary>
    /// The page's tilt in degrees: the angle at which the rows of the turned page are sharpest,
    /// the staff lines each falling into one row (the row counts' sum of squares is greatest).
    /// </summary>
    static double Skew(Img img)
    {
        int w = img.W, h = img.H;
        var xs = new List<int>(); var ys = new List<int>();
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (img.D[y * w + x]) { xs.Add(x); ys.Add(y); }
        double cx = w / 2.0, cy = h / 2.0;
        double best = -1, bestA = 0;
        var hist = new long[h + 2];
        for (double a = -2.0; a <= 2.0 + 1e-9; a += 0.05)
        {
            double r = a * Math.PI / 180, cos = Math.Cos(r), sin = Math.Sin(r);
            Array.Clear(hist);
            for (int i = 0; i < xs.Count; i++)
            {
                int yy = (int)Math.Round(cy + (ys[i] - cy) * cos - (xs[i] - cx) * sin);
                if (yy >= 0 && yy < h) hist[yy]++;
            }
            double score = 0;
            foreach (long c in hist) score += (double)c * c;
            if (score > best) { best = score; bestA = a; }
        }
        LastSkew = bestA;
        return bestA;
    }

    /// <summary>
    /// How a bowed page was levelled (<see cref="Bow"/>): the rows each point was moved up the
    /// page by. In each band of rows the shift runs along the columns, straight between the
    /// strips' middles, zero at the page's middle strip; between the bands' middles it is straight.
    /// </summary>
    public sealed class Warp
    {
        public double[] Mids;       // the bands' middle rows
        public double[][] Shift;    // per band, the shift at each column (rows, + down the page)

        public double At(double x, double y)
        {
            int k = 0;
            while (k + 1 < Mids.Length - 1 && y > Mids[k + 1]) k++;
            double f = Mids.Length > 1 ? Math.Clamp((y - Mids[k]) / (Mids[k + 1] - Mids[k]), 0, 1) : 0;
            int xi = Math.Clamp((int)Math.Round(x), 0, Shift[k].Length - 1);
            return Mids.Length > 1 ? Shift[k][xi] + f * (Shift[k + 1][xi] - Shift[k][xi]) : Shift[k][xi];
        }
    }

    /// <summary>
    /// A book's page bows on the glass: its tilt changes down the page (揺れる想い p4, a staff line
    /// falling 8 rows over the page at the top, rising 4 at the bottom), and no one turn levels it.
    /// And towards the binding the lines bend: a tilt for a whole row levels the page's open side
    /// and leaves the lines by the binding falling, or the other way about (kinkel19-2's scan, the
    /// lines of a staff drifting over 6 rows either side after the rows were levelled, no row of
    /// it a peak for the comb). So the page is cut into four bands of rows and each band into
    /// strips of columns, and each strip's ink per row is matched against the band's middle
    /// strip's, shifted up and down: the shift that matches best is how far the lines have
    /// dropped there (the staff lines, five peaks a space apart in every strip, decide the match;
    /// the long level runs tried first ran out where the notes stand close). A strip with little
    /// ink, or no clear match, takes its nearest neighbour's shift. Null where no strip is shifted
    /// by two rows or more: a page turned level by Skew is left as it is.
    /// </summary>
    /// <param name="space">the staff space as estimated from the grey page (0: unknown)</param>
    static Warp Bow(Img img, double space)
    {
        // (eight bands: a quarter of a page holds three staves, and where the page curves down
        // towards its foot the last of them is shifted other than the first -- kinkel19-2's last
        // staff, 4 rows low at the page's right with the band's shift, its end lost)
        int w = img.W, h = img.H, bands = 8;
        int sw = Math.Max(100, w / 16), n = w / sw, maxS = Math.Max(8, w / 80);
        // (the rows of a scan's black edges, ink across nine tenths of the page, are no line's and level anyway)
        var edge = new bool[h];
        for (int y = 0; y < h; y++)
        {
            int c = 0, o = y * w;
            for (int x = 0; x < w; x++) if (img.D[o + x]) c++;
            edge[y] = c > 0.9 * w;
        }
        var mids = new List<double>(); var shifts = new List<double[]>(); var strips = new List<double[]>();
        bool bowed = false;
        for (int b = 0; b < bands; b++)
        {
            int y0 = b * h / bands, y1 = (b + 1) * h / bands, bh = y1 - y0;
            var prof = new double[n][];
            for (int k = 0; k < n; k++) prof[k] = new double[bh];
            for (int y = y0; y < y1; y++)
            {
                if (edge[y]) continue;
                // (nor the ink of the black edges where they are ragged: the gutter's and the lid's
                // rows are level, and matched at no shift)
                var (from, to) = img.EdgeFree(y);
                int o = y * w;
                for (int x = from; x < Math.Min(to, n * sw); x++) if (img.D[o + x]) prof[x / sw][y - y0]++;
            }
            int r = (w / 2) / sw;
            // (a band whose middle strip holds little ink -- a page's foot, its black edge left
            // out -- has nothing to match the others against: a strip of one staff holds a
            // hundredth of its area in ink and more, a page number or a footer a tenth of that)
            if (prof[r].Sum() < 0.01 * sw * bh) continue;
            // (and holds staff lines: its rows match themselves sharply, the lines a few rows thick
            // losing each other five rows off. A band of a title's words, or of a part's name,
            // still matches itself well five rows off, and matched the words of other strips at
            // any shift -- voices2009's title band, two strips at -20 rows)
            // (and matches itself again a space off, line on line: the rows of a page's foot --
            // a scan's noise, a footer -- match other strips' consistently but repeat at no
            // lag; slurs005 printed and scanned, its last band shifted 6 to 30 rows)
            double auto0 = 0, auto5 = 0, period = 0;
            for (int y = 0; y + 5 < bh; y++) { auto0 += prof[r][y] * prof[r][y]; auto5 += prof[r][y] * prof[r][y + 5]; }
            for (int lag = 8; lag <= Math.Min(120, bh / 2); lag++)
            {
                double sum = 0;
                for (int y = 0; y + lag < bh; y++) sum += prof[r][y] * prof[r][y + lag];
                period = Math.Max(period, sum);
            }
            if (Trace) Console.WriteLine($"  bow band {b} (rows {y0}-{y1}): the middle strip five rows off matches itself {auto5 / Math.Max(1, auto0):F2}, a lag off at best {period / Math.Max(1, auto0):F2}");
            if (auto5 > 0.7 * auto0 || period < 0.45 * auto0) continue;
            // (and the strips matched hold lines: four runs of rows and more inked across seven
            // tenths of the strip's width, each run no thicker than a third of a space -- a staff
            // line's rows, even broken or blurred; the rows of words, of a page's number or of a
            // scan's speckle hold less, and a beam's rows, as wide, run half a space thick: at two
            // fifths the rows of a clean page's verses passed, and matched other strips' words
            // rows off; the level beams of dl-ms-08's thirty-seconds passed at seven tenths)
            int thick = space > 0 ? Math.Max(3, (int)(0.3 * space)) : 4;
            bool Lined(double[] p)
            {
                int runs = 0, run = 0;
                for (int y = 0; y <= bh; y++)
                {
                    if (y < bh && p[y] >= 0.7 * sw) { run++; continue; }
                    if (run > 0 && run <= thick) runs++;
                    run = 0;
                }
                return runs >= 4;
            }
            if (!Lined(prof[r])) continue;
            // how far down strip b's ink lies from strip a's: the shift that matches their rows best
            double? Shift(double[] pa, double[] pb)
            {
                var corr = new double[2 * maxS + 1];
                for (int d = -maxS; d <= maxS; d++)
                {
                    double sum = 0;
                    for (int y = Math.Max(0, -d); y < bh && y + d < bh; y++) sum += pa[y] * pb[y + d];
                    corr[d + maxS] = sum;
                }
                int bi = 0;
                for (int i = 1; i < corr.Length; i++) if (corr[i] > corr[bi]) bi = i;
                // (of the shifts that match within a twentieth of the best, the smallest: a clean
                // page's notes and tuplet numbers matched three rows off as well as its lines did
                // at none -- tuplets005 -- and a page that bows shifts its lines well clear of none)
                for (int i = 0; i < corr.Length; i++) if (corr[i] >= 0.95 * corr[bi] && Math.Abs(i - maxS) < Math.Abs(bi - maxS)) bi = i;
                // (a clear match stands well over the run of shifts; and not at the end of the range)
                if (bi == 0 || bi == corr.Length - 1 || corr[bi] < 1.5 * corr.Average()) return null;
                // (to a fraction of a row: the peak's parabola)
                double a = corr[bi - 1], c = corr[bi + 1], den = a - 2 * corr[bi] + c;
                return bi - maxS + (den < 0 ? 0.5 * (a - c) / den : 0);
            }
            var s = new double[n];
            Array.Fill(s, double.NaN);
            s[r] = 0;
            for (int k = 0; k < n; k++)
            {
                if (k == r) continue;
                // (a strip with little ink -- a margin, a title page's blank top -- tells nothing)
                if (prof[k].Sum() < 0.01 * sw * bh || !Lined(prof[k])) continue;
                s[k] = Shift(prof[r], prof[k]) ?? double.NaN;
            }
            // (a page bows smoothly: outward from the middle strip, a strip shifted from the last
            // one taken by more than a strip's twenty-fifth -- a tilt of two degrees and more --
            // or half as much again past a strip dropped, matched something else, the words of a
            // title or a part's name, a scan's ragged edge, and is dropped: kinkel19-2's top left,
            // 13 rows where the staves' strips beside it had 4 and -7, and 23 past two dropped
            // strips; a clean page's foot, 19 past one dropped)
            if (Trace) Console.WriteLine($"  bow band {b} (rows {y0}-{y1}): strips matched {string.Join(" ", s.Select(v => double.IsNaN(v) ? "-" : v.ToString("F1")))}");
            // (and each strip's shift from the last one taken is borne out by matching the two
            // strips themselves: a strip of a title's words or a part's name matches the middle
            // strip's staff lines somewhere, but not its own neighbour's the same way -- a clean
            // page's indented first system, its title over the margin, 25 and 7 rows)
            double maxStep = sw / 25.0;
            foreach (int dir in new[] { -1, 1 })
            {
                int last = r;
                for (int k = r + dir; k >= 0 && k < n; k += dir)
                {
                    if (double.IsNaN(s[k])) continue;
                    int apart = Math.Abs(k - last);
                    double? adj = Shift(prof[last], prof[k]);
                    if (Trace) Console.WriteLine($"    strip {k} against {last}: {s[k]:F1} - {s[last]:F1} = {s[k] - s[last]:F1}, the two strips matched at {(adj.HasValue ? adj.Value.ToString("F1") : "nothing")}");
                    if (Math.Abs(s[k] - s[last]) > maxStep * Math.Min(1.5, apart) || adj == null || Math.Abs(s[k] - s[last] - adj.Value) > 1.5 + (apart - 1)) s[k] = double.NaN;
                    else last = k;
                }
            }
            var known = Enumerable.Range(0, n).Where(k => !double.IsNaN(s[k])).ToList();
            if (known.Count < 2) continue;
            // (a bow shows in strips side by side, two and more shifted by two rows or more: one
            // strip alone shifted is a match on something else)
            for (int i = 0; i + 1 < known.Count; i++)
                if (known[i + 1] == known[i] + 1 && Math.Abs(s[known[i]]) >= 2 && Math.Abs(s[known[i + 1]]) >= 2) bowed = true;
            for (int k = 0; k < n; k++)
                if (double.IsNaN(s[k])) s[k] = s[known.OrderBy(j => Math.Abs(j - k)).First()];
            // the band's shift along the columns, straight between the strips' middles
            var shift = new double[w];
            for (int x = 0; x < w; x++)
            {
                double p = (x - sw / 2.0) / sw;
                int k = Math.Clamp((int)Math.Floor(p), 0, n - 2);
                double f = Math.Clamp(p - k, 0, 1);
                shift[x] = s[k] + f * (s[k + 1] - s[k]);
            }
            // (the band's shift holds where its ink is -- its staves -- not at its middle row: the
            // last band of a page is a staff near its top and the margin below, and between the
            // bands' middles the staff got a shift between its own and the band above's, its lines
            // 4 rows low at the page's right -- kinkel19-2's last staff, ending 700 columns early)
            double ink = 0, moment = 0;
            for (int y = 0; y < bh; y++) { double v = prof[r][y]; ink += v; moment += v * y; }
            mids.Add(ink > 0 ? y0 + moment / ink : (y0 + y1) / 2.0); shifts.Add(shift); strips.Add(s);
        }
        if (mids.Count < 2 || !bowed) return null;
        LastBow = strips.ToArray();
        return new Warp { Mids = mids.ToArray(), Shift = shifts.ToArray() };
    }

    /// <summary>
    /// The tilt of each quarter of the page, as the tangent, from the ink of its long level runs
    /// (the staff lines' stretches between the notes): the quarters with ink enough and a tilt
    /// within the range. Whether a page bows at all is read off these (<see cref="Load"/>).
    /// </summary>
    static List<double> BandTilts(Img img)
    {
        int w = img.W, h = img.H, bands = 4;
        var tans = new List<double>();
        double cx = w / 2.0;
        for (int b = 0; b < bands; b++)
        {
            int y0 = b * h / bands, y1 = (b + 1) * h / bands;
            // (only the ink of long level runs: a title's letters, set large, tilted a clean page's top quarter by 0.16 degrees)
            var xs = new List<int>(); var ys = new List<int>();
            int minRun = Math.Max(20, w / 40);
            for (int y = y0; y < y1; y++)
                for (int x = 0; x < w;)
                {
                    if (!img.D[y * w + x]) { x++; continue; }
                    int e = x;
                    while (e < w && img.D[y * w + e]) e++;
                    if (e - x >= minRun) for (int k = x; k < e; k += 4) { xs.Add(k); ys.Add(y); }
                    x = e;
                }
            // (a quarter with little of them -- a title page's blank top -- tells no tilt)
            if (xs.Count < 0.0005 * w * (y1 - y0)) continue;
            var hist = new long[y1 - y0 + 2 * w / 50 + 2];
            int off = w / 50 + 1;
            double best = -1, bestT = 0, bestA = 0;
            long bestRow = 0;
            for (double a = -0.6; a <= 0.6 + 1e-9; a += 0.02)
            {
                double t = Math.Tan(a * Math.PI / 180);
                Array.Clear(hist);
                for (int i = 0; i < xs.Count; i++)
                {
                    int yy = (int)Math.Round(ys[i] - y0 - (xs[i] - cx) * t) + off;
                    if (yy >= 0 && yy < hist.Length) hist[yy]++;
                }
                double score = 0;
                foreach (long c in hist) score += (double)c * c;
                if (score > best) { best = score; bestT = t; bestA = a; bestRow = hist.Max(); }
            }
            // (and levelled, some row is a staff line across a fifth of the page; nor is a tilt at
            // the end of the range one: a steep thick beam pulled a clean page's top quarter to 0.6 degrees)
            if (4 * bestRow < w / 5 || Math.Abs(bestA) > 0.55) continue;
            tans.Add(bestT);
        }
        return tans;
    }

    public static double[][] LastBow;   // the strips' shifts in rows, a row per band
    public static bool Trace;           // --trace: how the bow was measured

    /// <summary>Each point taken from where the warp says it lies (nearest pixel).</summary>
    static Img Unbow(Img img, Warp warp)
    {
        int w = img.W, h = img.H;
        var o = new Img(w, h);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int sy = (int)Math.Round(y + warp.At(x, y));
                if (sy >= 0 && sy < h) o.D[y * w + x] = img.D[sy * w + x];
            }
        return o;
    }

    static byte[] UnbowGray(byte[] g, int w, int h, Warp warp)
    {
        var o = new byte[w * h];
        Array.Fill(o, (byte)255);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int sy = (int)Math.Round(y + warp.At(x, y));
                if (sy >= 0 && sy < h) o[y * w + x] = g[sy * w + x];
            }
        return o;
    }

    /// <summary>The page turned by the angle about its centre (nearest pixel).</summary>
    static Img Rotate(Img img, double degrees)
    {
        int w = img.W, h = img.H;
        double r = degrees * Math.PI / 180, cos = Math.Cos(r), sin = Math.Sin(r);
        double cx = w / 2.0, cy = h / 2.0;
        var o = new Img(w, h);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                // where this pixel came from
                double dx = x - cx, dy = y - cy;
                int sx = (int)Math.Round(cx + dx * cos + dy * sin), sy = (int)Math.Round(cy - dx * sin + dy * cos);
                if (sx >= 0 && sx < w && sy >= 0 && sy < h) o.D[y * w + x] = img.D[sy * w + sx];
            }
        return o;
    }

    /// <summary>Whether lone dark pixels make up more than a fifth of a percent of the ink.</summary>
    static bool Grainy(Img img)
    {
        int w = img.W, h = img.H;
        long dark = 0, lone = 0;
        for (int y = 1; y < h - 1; y++)
            for (int x = 1; x < w - 1; x++)
            {
                if (!img.D[y * w + x]) continue;
                dark++;
                int n = 0;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                        if ((dx != 0 || dy != 0) && img.D[(y + dy) * w + x + dx]) n++;
                if (n <= 1) lone++;
            }
        return dark > 0 && lone > 0.002 * dark;
    }

    /// <summary>
    /// Grain: a dark pixel with no dark neighbour but one, a white pixel walled in by dark ones.
    /// A line one pixel wide keeps its two neighbours along it, so it stays.
    /// </summary>
    static void Despeckle(Img img)
    {
        int w = img.W, h = img.H;
        var src = (bool[])img.D.Clone();
        for (int y = 1; y < h - 1; y++)
            for (int x = 1; x < w - 1; x++)
            {
                int n = 0;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                        if ((dx != 0 || dy != 0) && src[(y + dy) * w + x + dx]) n++;
                if (src[y * w + x]) { if (n <= 1) img.D[y * w + x] = false; }
                else if (n >= 7) img.D[y * w + x] = true;
            }
    }

    static byte[] Median3(byte[] g, int w, int h)
    {
        var o = new byte[w * h];
        var win = new byte[9];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int n = 0;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int xx = Math.Clamp(x + dx, 0, w - 1), yy = Math.Clamp(y + dy, 0, h - 1);
                        win[n++] = g[yy * w + xx];
                    }
                Array.Sort(win);
                o[y * w + x] = win[4];
            }
        return o;
    }

    /// <summary>
    /// The grey level halfway between the page's two tones: the paper's peak of the histogram and
    /// the ink's, the strongest peak at least a quarter of the range away from it. A clean
    /// engraving (white, black) gives 128; a scan's grey paper and brownish ink move it.
    /// (Otsu's threshold leans on the antialiased edge pixels of a clean render and thickens its strokes.)
    /// </summary>
    static int Otsu(byte[] g)
    {
        var hist = new long[256];
        foreach (var v in g) hist[v]++;
        // smooth the histogram a little: a peak is a hill, not one level
        var sm = new double[256];
        for (int i = 0; i < 256; i++)
            for (int k = -3; k <= 3; k++) sm[i] += hist[Math.Clamp(i + k, 0, 255)];
        int paper = 0;
        for (int i = 1; i < 256; i++) if (sm[i] > sm[paper]) paper = i;
        int ink = -1;
        for (int i = 0; i < 256; i++) if (Math.Abs(i - paper) >= 64 && (ink < 0 || sm[i] > sm[ink])) ink = i;
        if (ink < 0) return 128;
        return (paper + ink + 1) / 2;       // (white and black give 128, as the fixed threshold did)
    }
}

/// <summary>A connected component: bounding box, pixel count and centroid.</summary>
sealed class Comp
{
    public int Id, N;
    public int MinX = int.MaxValue, MaxX = -1, MinY = int.MaxValue, MaxY = -1;
    public long Sx, Sy;
    public int W => MaxX - MinX + 1;
    public int H => MaxY - MinY + 1;
    public double Cx => Sx / (double)N;
    public double Cy => Sy / (double)N;
    public double Fill => N / (double)(W * H);
}

static class Components
{
    /// <summary>Labels the pixels where <paramref name="pred"/> holds. Label 0 = none.</summary>
    public static (int[] Labels, List<Comp> Comps) Label(int w, int h, Func<int, bool> pred, bool eight)
    {
        var labels = new int[w * h];
        var comps = new List<Comp> { null };
        var stack = new Stack<int>();
        for (int i = 0; i < labels.Length; i++)
        {
            if (labels[i] != 0 || !pred(i)) continue;
            var c = new Comp { Id = comps.Count };
            comps.Add(c);
            labels[i] = c.Id;
            stack.Push(i);
            while (stack.Count > 0)
            {
                int p = stack.Pop();
                int px = p % w, py = p / w;
                c.N++; c.Sx += px; c.Sy += py;
                if (px < c.MinX) c.MinX = px;
                if (px > c.MaxX) c.MaxX = px;
                if (py < c.MinY) c.MinY = py;
                if (py > c.MaxY) c.MaxY = py;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        if (!eight && dx != 0 && dy != 0) continue;
                        int qx = px + dx, qy = py + dy;
                        if ((uint)qx >= (uint)w || (uint)qy >= (uint)h) continue;
                        int q = qy * w + qx;
                        if (labels[q] != 0 || !pred(q)) continue;
                        labels[q] = c.Id;
                        stack.Push(q);
                    }
            }
        }
        return (labels, comps);
    }

    /// <summary>White regions fully enclosed by ink (4-connected).</summary>
    public static List<Comp> Holes(Img ink)
    {
        int w = ink.W, h = ink.H;
        var outside = new bool[w * h];
        var stack = new Stack<int>();
        for (int x = 0; x < w; x++) { stack.Push(x); stack.Push((h - 1) * w + x); }
        for (int y = 0; y < h; y++) { stack.Push(y * w); stack.Push(y * w + w - 1); }
        while (stack.Count > 0)
        {
            int p = stack.Pop();
            if (outside[p] || ink.D[p]) continue;
            outside[p] = true;
            int px = p % w, py = p / w;
            if (px > 0) stack.Push(p - 1);
            if (px < w - 1) stack.Push(p + 1);
            if (py > 0) stack.Push(p - w);
            if (py < h - 1) stack.Push(p + w);
        }
        var (_, comps) = Label(w, h, i => !ink.D[i] && !outside[i], eight: false);
        comps.RemoveAt(0);
        return comps;
    }
}
