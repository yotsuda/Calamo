using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.Runtime.InteropServices;

// OmrDegrade --in <png> --out <png> [--scale f] [--blur r] [--ink k] [--paper a] [--noise s] [--specks p] [--gamma g] [--binarize t | --binarize-auto o] [--fill f] [--fill-edge px] [--spread px] [--swell px [--swell-core px]] [--erase boxes.tsv [--erase-names prefix]] [--paste "src.png|box|box;..."] [--keystone k] [--shear a] [--bend b] [--seed n]
//
// Makes a clean engraving look printed and scanned: a lower resolution (scale), ink that bleeds
// (blur) and is not quite black (ink), paper that is not quite white and not evenly lit (paper),
// sensor grain (noise), dust and pinholes (specks), a scanner's tone curve (gamma), and toner
// that does not cover a solid area evenly (fill: the inside of a head or a beam goes grey and
// mottled while its edge and every thin stroke stay dark, as a laser print does). The geometry
// is untouched (no rotation), so the SVG truth still fits by the image's width -- unless asked:
// --keystone, --shear and --bend draw a page photographed off square, which the reader must
// stand up and level itself (Omr's Lean and Bow) for the truth to fit.
static class Program
{
    static int Main(string[] args)
    {
        var opt = new Dictionary<string, string>();
        for (int i = 0; i < args.Length; i++)
            if (args[i].StartsWith("--")) opt[args[i][2..]] = i + 1 < args.Length && !args[i + 1].StartsWith("--") ? args[++i] : "true";
        double P(string k, double d) => opt.TryGetValue(k, out var v) ? double.Parse(v, CultureInfo.InvariantCulture) : d;
        string inPath = opt["in"], outPath = opt.GetValueOrDefault("out", inPath);
        double scale = P("scale", 1.0), blur = P("blur", 0.8), ink = P("ink", 0.12), paper = P("paper", 0.10);
        double noise = P("noise", 8), specks = P("specks", 0.0003), gamma = P("gamma", 1.0);
        // --binarize t: a scanner set to black-and-white cuts the page at one grey level; the
        // blurred, thinnest strokes (staff lines) fall apart where the paper's shade lifts them
        double binarize = P("binarize", -1);
        // --binarize-auto o: as a scanner does it by default, at the page's own grey level between
        // ink and paper (Otsu's), moved by o (below 0 lighter: thin strokes break first)
        bool binarizeAuto = opt.ContainsKey("binarize-auto");
        double binarizeShift = P("binarize-auto", 0);
        // --fill f: how much of the ink is lost inside a solid area (0 none, 1 all of it at the
        // worst spots); --fill-edge: how far in, in pixels of the input, the rim stays dark
        double fill = P("fill", 0), fillEdge = P("fill-edge", 4);
        var rng = new Random((int)P("seed", 1));

        var (w, h, g) = Load(inPath);
        // --erase boxes.tsv: take the glyphs named --erase-names (a prefix, tuplet. by default) off the
        // page, as an engraver that leaves them unprinted would (a triplet's 3 after the first few)
        if (opt.TryGetValue("erase", out var boxes)) Erase(w, h, g, boxes, opt.GetValueOrDefault("erase-names", "tuplet."));
        // --paste "src.png|sl,st,sr,sb|dl,dt,dr,db[;...]": a glyph drawn on another page put in place of one on this
        // page (a time signature's upper figure printed other than the measures hold: tools/make-retime.ps1)
        if (opt.TryGetValue("paste", out var pastes)) Paste(w, h, g, pastes);
        // --spread px: ink that spreads on soft paper or a worn plate, every stroke grown by px pixels of the
        // input (a disc), so that close glyphs fuse: the two heads of a second either side of a stem in an old print
        double spread = P("spread", 0);
        if (spread > 0) g = Morph(w, h, g, spread, darken: true);
        // --swell px [--swell-core px]: only the solid glyphs grow (heads, beams; what is left of the ink after
        // strokes thinner than twice the core are opened away), the staff lines and stems keep their weight --
        // an old print's heads, large and fused in a second while its lines stay fine
        double swell = P("swell", 0), swellCore = P("swell-core", 6);
        if (swell > 0)
        {
            var solid = Morph(w, h, Morph(w, h, g, swellCore, darken: false), swellCore + swell, darken: true);
            for (int i = 0; i < g.Length; i++) g[i] = Math.Min(g[i], solid[i]);
        }
        if (fill > 0) g = Unfill(w, h, g, fill, fillEdge, rng);
        // --keystone k: the corners' columns moved by k of the page's width, inward at the top and outward
        // at the bottom (the page narrows away from the camera); --shear a: every upright line leaning a
        // of the page's width over its height; --bend b: the rows at the page's sides moved b of its height
        double keystone = P("keystone", 0), shear = P("shear", 0), bend = P("bend", 0);
        if (keystone != 0 || shear != 0 || bend != 0) g = Warp(w, h, g, keystone, shear, bend);
        if (Math.Abs(scale - 1.0) > 1e-6) (w, h, g) = Resample(w, h, g, scale);
        if (blur > 0) g = Blur(w, h, g, blur);
        // ink lifted off black, paper pulled off white, unevenly
        var shade = LowFrequency(w, h, rng, cells: 6);
        for (int i = 0; i < g.Length; i++)
        {
            double v = ink + (1 - ink) * g[i];                       // 0 -> ink
            v *= 1 - paper * (0.5 + 0.5 * shade[i]);                // paper darkens by up to 'paper', more where the shade is
            g[i] = v;
        }
        // grain, dust, pinholes
        for (int i = 0; i < g.Length; i++)
        {
            double n = Gauss(rng) * noise / 255.0;
            double v = g[i] + n;
            double r = rng.NextDouble();
            if (r < specks) v = 0.1 + 0.3 * rng.NextDouble();        // a dust speck
            else if (r < 2 * specks) v = 1.0;                        // a pinhole in the ink
            g[i] = Math.Clamp(v, 0, 1);
        }
        if (Math.Abs(gamma - 1.0) > 1e-6) for (int i = 0; i < g.Length; i++) g[i] = Math.Pow(g[i], gamma);
        if (binarizeAuto) binarize = Otsu(g) + binarizeShift;
        if (binarize >= 0) for (int i = 0; i < g.Length; i++) g[i] = g[i] < binarize ? 0 : 1;
        Save(outPath, w, h, g);
        return 0;
    }

    /// <summary>
    /// The page's darkest (darken) or lightest value within about r pixels of every pixel: the ink grown or
    /// shrunk by r, the antialiased rim kept. An octagon (r steps of 3x3, cross and square in turn), so that
    /// the large radii --swell needs stay quick on a full-size page.
    /// </summary>
    static double[] Morph(int w, int h, double[] g, double r, bool darken)
    {
        var cross = new (int dx, int dy)[] { (0, 0), (1, 0), (-1, 0), (0, 1), (0, -1) };
        var square = new (int dx, int dy)[] { (0, 0), (1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1) };
        int steps = Math.Max(1, (int)Math.Round(r));
        for (int k = 0; k < steps; k++)
        {
            var offs = k % 2 == 0 ? cross : square;
            var src = g;
            var o = new double[g.Length];
            Parallel.For(0, h, y =>
            {
                for (int x = 0; x < w; x++)
                {
                    double m = src[y * w + x];
                    foreach (var (dx, dy) in offs)
                    {
                        int xx = x + dx, yy = y + dy;
                        if ((uint)xx < (uint)w && (uint)yy < (uint)h && (darken ? src[yy * w + xx] < m : src[yy * w + xx] > m)) m = src[yy * w + xx];
                    }
                    o[y * w + x] = m;
                }
            });
            g = o;
        }
        return g;
    }

    /// <summary>
    /// Toner that thins out inside a solid area: the ink's darkness, from half the edge width in,
    /// is cut by up to 'fill' times a mottle (a random field with a period of a few pixels), so a
    /// head's or a beam's inside goes grey and patchy while its rim, a stem and a staff line keep
    /// their black. (What a laser print looks like under a scanner: see data/print.)
    /// </summary>
    static double[] Unfill(int w, int h, double[] g, double fill, double edge, Random rng)
    {
        // the distance of every ink pixel from the paper (a 3-4 chamfer, two passes)
        const int inf = 1 << 20;
        var d = new int[w * h];
        for (int i = 0; i < d.Length; i++) d[i] = g[i] < 0.5 ? inf : 0;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                if (d[i] == 0) continue;
                int v = d[i];
                if (x > 0) v = Math.Min(v, d[i - 1] + 3);
                if (y > 0) { v = Math.Min(v, d[i - w] + 3); if (x > 0) v = Math.Min(v, d[i - w - 1] + 4); if (x < w - 1) v = Math.Min(v, d[i - w + 1] + 4); }
                d[i] = v;
            }
        for (int y = h - 1; y >= 0; y--)
            for (int x = w - 1; x >= 0; x--)
            {
                int i = y * w + x;
                if (d[i] == 0) continue;
                int v = d[i];
                if (x < w - 1) v = Math.Min(v, d[i + 1] + 3);
                if (y < h - 1) { v = Math.Min(v, d[i + w] + 3); if (x < w - 1) v = Math.Min(v, d[i + w + 1] + 4); if (x > 0) v = Math.Min(v, d[i + w - 1] + 4); }
                d[i] = v;
            }
        var mottle = LowFrequency(w, h, rng, cells: Math.Max(2, w / 12));
        var o = (double[])g.Clone();
        for (int i = 0; i < o.Length; i++)
        {
            if (d[i] == 0) continue;
            double depth = Math.Clamp((d[i] / 3.0 - 0.5 * edge) / edge, 0, 1);   // 0 at the rim, 1 an edge and a half in
            double dark = 1 - g[i];
            dark *= 1 - depth * fill * (0.4 + 1.2 * mottle[i]);
            o[i] = 1 - Math.Clamp(dark, 0, 1);
        }
        return o;
    }

    /// <summary>
    /// Whites out the glyphs of an omr --export-boxes table whose names start with the prefix:
    /// every piece of ink (8-connected, darker than mid-grey) that reaches into the middle of the
    /// glyph's box and lies wholly within twice the box around it. A beam or a staff line by the
    /// glyph runs out of that and stays (the box is a space around the glyph's centre for a tuplet's number).
    /// </summary>
    static void Erase(int w, int h, double[] g, string tsv, string prefix)
    {
        int erased = 0;
        foreach (var line in File.ReadLines(tsv).Skip(1))
        {
            var f = line.Split('\t');
            if (f.Length < 6 || !f[1].StartsWith(prefix)) continue;
            double l = double.Parse(f[2], CultureInfo.InvariantCulture), t = double.Parse(f[3], CultureInfo.InvariantCulture);
            double r = double.Parse(f[4], CultureInfo.InvariantCulture), b = double.Parse(f[5], CultureInfo.InvariantCulture);
            // (a line, a tuplet bracket's rule or hook: its box with its stroke, whited out and a pixel round it)
            if (f[0] == "line")
            {
                for (int y = Math.Max(0, (int)Math.Floor(t) - 1); y <= Math.Min(h - 1, (int)Math.Ceiling(b) + 1); y++)
                    for (int x = Math.Max(0, (int)Math.Floor(l) - 1); x <= Math.Min(w - 1, (int)Math.Ceiling(r) + 1); x++)
                        if (g[y * w + x] < 1) { g[y * w + x] = 1; erased++; }
                continue;
            }
            double cx = (l + r) / 2, cy = (t + b) / 2, hw = (r - l) / 2, hh = (b - t) / 2;
            int ox0 = (int)Math.Floor(cx - 2 * hw), ox1 = (int)Math.Ceiling(cx + 2 * hw), oy0 = (int)Math.Floor(cy - 2 * hh), oy1 = (int)Math.Ceiling(cy + 2 * hh);
            var seen = new HashSet<int>();
            for (int y = (int)(cy - 0.6 * hh); y <= (int)(cy + 0.6 * hh); y++)
                for (int x = (int)(cx - 0.6 * hw); x <= (int)(cx + 0.6 * hw); x++)
                {
                    if (x < 0 || y < 0 || x >= w || y >= h || g[y * w + x] >= 0.5 || seen.Contains(y * w + x)) continue;
                    // the piece of ink, as long as it keeps within the outer box
                    var piece = new List<int>();
                    var stack = new Stack<int>();
                    stack.Push(y * w + x); seen.Add(y * w + x);
                    bool inside = true;
                    while (stack.Count > 0)
                    {
                        int i = stack.Pop(), px = i % w, py = i / w;
                        piece.Add(i);
                        if (px <= ox0 || px >= ox1 || py <= oy0 || py >= oy1) { inside = false; continue; }
                        for (int dy = -1; dy <= 1; dy++)
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                int qx = px + dx, qy = py + dy, q = qy * w + qx;
                                if (qx < 0 || qy < 0 || qx >= w || qy >= h || g[q] >= 0.5 || !seen.Add(q)) continue;
                                stack.Push(q);
                            }
                    }
                    if (!inside)
                    {
                        // a staff or ledger line through the glyph joins it to the page: within the
                        // glyph's box, the rows a thin line runs out of on either side stay, and the
                        // ink joined to the middle by the other rows goes
                        int wx0 = (int)(cx - 1.5 * hw), wx1 = (int)(cx + 1.5 * hw), wy0 = (int)(cy - 1.6 * hh), wy1 = (int)(cy + 1.6 * hh);
                        bool Dark(int px, int py) => px >= 0 && py >= 0 && px < w && py < h && g[py * w + px] < 0.5;
                        bool Thin(int px, int py)
                        {
                            if (!Dark(px, py)) return false;
                            int a = py, z = py;
                            while (Dark(px, a - 1) && py - a < hh) a--;
                            while (Dark(px, z + 1) && z - py < hh) z++;
                            return z - a + 1 <= 0.5 * hh;
                        }
                        var lineRow = new HashSet<int>();
                        for (int py = wy0; py <= wy1; py++)
                            if (Thin(wx0 - 1, py) || Thin(wx1 + 1, py)) lineRow.Add(py);
                        // (the line cuts the glyph in pieces: each piece within the box goes, one
                        // reaching the box's edge -- a beam's corner -- stays)
                        var win = new List<int>();
                        var seen2 = new HashSet<int>();
                        for (int sy = wy0; sy <= wy1; sy++)
                            for (int sx = wx0; sx <= wx1; sx++)
                            {
                                if (!Dark(sx, sy) || lineRow.Contains(sy) || !seen2.Add(sy * w + sx)) continue;
                                var part = new List<int>();
                                var st2 = new Stack<int>();
                                st2.Push(sy * w + sx);
                                bool edge = false;
                                while (st2.Count > 0)
                                {
                                    int i = st2.Pop(), px = i % w, py = i / w;
                                    part.Add(i);
                                    edge |= px == wx0 || px == wx1 || py == wy0 || py == wy1;
                                    for (int dy = -1; dy <= 1; dy++)
                                        for (int dx = -1; dx <= 1; dx++)
                                        {
                                            int qx = px + dx, qy = py + dy;
                                            if (qx < wx0 || qx > wx1 || qy < wy0 || qy > wy1 || lineRow.Contains(qy) || !Dark(qx, qy) || !seen2.Add(qy * w + qx)) continue;
                                            st2.Push(qy * w + qx);
                                        }
                                }
                                if (!edge) win.AddRange(part);
                            }
                        foreach (int i in win)
                        {
                            int px = i % w, py = i / w;
                            for (int dy = -1; dy <= 1; dy++)
                                for (int dx = -1; dx <= 1; dx++)
                                    if (px + dx >= 0 && py + dy >= 0 && px + dx < w && py + dy < h && !lineRow.Contains(py + dy) && g[(py + dy) * w + px + dx] >= 0.5) g[(py + dy) * w + px + dx] = 1;
                        }
                        foreach (int i in win) g[i] = 1;
                        if (win.Count > 0) erased++;
                        continue;
                    }
                    // (and the grey rim the anti-aliasing leaves around it)
                    foreach (int i in piece)
                    {
                        int px = i % w, py = i / w;
                        for (int dy = -1; dy <= 1; dy++)
                            for (int dx = -1; dx <= 1; dx++)
                                if (px + dx >= 0 && py + dy >= 0 && px + dx < w && py + dy < h && g[(py + dy) * w + px + dx] >= 0.5) g[(py + dy) * w + px + dx] = 1;
                        g[i] = 1;
                    }
                    erased++;
                }
        }
        Console.WriteLine($"erased {erased} pieces of {prefix}*");
    }

    /// <summary>A page off square: each pixel taken (bilinearly) from where the page's lean and bend put it, paper beyond the edges.</summary>
    static double[] Warp(int w, int h, double[] g, double keystone, double shear, double bend)
    {
        var o = new double[w * h];
        double cx = w / 2.0, cy = h / 2.0;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                double u = (x - cx) / cx, v = (y - cy) / cy;
                // (a corner moves keystone * w: u = v = 1 there; the rows bend away from the page's middle
                // column, which stays, as Omr's Bow levels them to it: the truth still fits once it has)
                double sx = x - keystone * w * u * v - shear * w * v / 2;
                double sy = y - bend * h * u * u;
                int x0 = (int)Math.Floor(sx), y0 = (int)Math.Floor(sy);
                if (x0 < 0 || y0 < 0 || x0 + 1 >= w || y0 + 1 >= h) { o[y * w + x] = 1; continue; }
                double fx = sx - x0, fy = sy - y0;
                double a = g[y0 * w + x0] * (1 - fx) + g[y0 * w + x0 + 1] * fx;
                double b = g[(y0 + 1) * w + x0] * (1 - fx) + g[(y0 + 1) * w + x0 + 1] * fx;
                o[y * w + x] = a * (1 - fy) + b * fy;
            }
        return o;
    }

    /// <summary>Otsu's threshold: the grey level that best splits the page into two classes.</summary>
    static double Otsu(double[] g)
    {
        var hist = new double[256];
        foreach (var v in g) hist[(int)Math.Round(Math.Clamp(v, 0, 1) * 255)]++;
        double total = g.Length, sumAll = 0;
        for (int t = 0; t < 256; t++) sumAll += t * hist[t];
        double wB = 0, sumB = 0, best = -1; int bestT = 128;
        for (int t = 0; t < 256; t++)
        {
            wB += hist[t]; if (wB == 0) continue;
            double wF = total - wB; if (wF == 0) break;
            sumB += t * hist[t];
            double mB = sumB / wB, mF = (sumAll - sumB) / wF, between = wB * wF * (mB - mF) * (mB - mF);
            if (between > best) { best = between; bestT = t; }
        }
        return (bestT + 0.5) / 255;
    }

    static double Gauss(Random rng)
    {
        double u1 = 1 - rng.NextDouble(), u2 = rng.NextDouble();
        return Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
    }

    /// <summary>A smooth random field in [0,1]: a coarse grid of values, bilinearly interpolated.</summary>
    static double[] LowFrequency(int w, int h, Random rng, int cells)
    {
        int gw = cells + 1, gh = Math.Max(2, (int)Math.Round(cells * (double)h / w) + 1);
        var grid = new double[gw * gh];
        for (int i = 0; i < grid.Length; i++) grid[i] = rng.NextDouble();
        var f = new double[w * h];
        for (int y = 0; y < h; y++)
        {
            double gy = (double)y / h * (gh - 1);
            int y0 = (int)gy, y1 = Math.Min(gh - 1, y0 + 1);
            double ty = gy - y0;
            for (int x = 0; x < w; x++)
            {
                double gx = (double)x / w * (gw - 1);
                int x0 = (int)gx, x1 = Math.Min(gw - 1, x0 + 1);
                double tx = gx - x0;
                double a = grid[y0 * gw + x0] * (1 - tx) + grid[y0 * gw + x1] * tx;
                double b = grid[y1 * gw + x0] * (1 - tx) + grid[y1 * gw + x1] * tx;
                f[y * w + x] = a * (1 - ty) + b * ty;
            }
        }
        return f;
    }

    static double[] Blur(int w, int h, double[] g, double sigma)
    {
        int r = (int)Math.Ceiling(3 * sigma);
        var k = new double[2 * r + 1];
        double sum = 0;
        for (int i = -r; i <= r; i++) { k[i + r] = Math.Exp(-i * i / (2 * sigma * sigma)); sum += k[i + r]; }
        for (int i = 0; i < k.Length; i++) k[i] /= sum;
        var t = new double[w * h];
        var o = new double[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                double v = 0;
                for (int i = -r; i <= r; i++) v += k[i + r] * g[y * w + Math.Clamp(x + i, 0, w - 1)];
                t[y * w + x] = v;
            }
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                double v = 0;
                for (int i = -r; i <= r; i++) v += k[i + r] * t[Math.Clamp(y + i, 0, h - 1) * w + x];
                o[y * w + x] = v;
            }
        return o;
    }

    /// <summary>Area-averaging downscale (a scanner integrates over its pixel), bilinear upscale.</summary>
    static (int, int, double[]) Resample(int w, int h, double[] g, double scale)
    {
        int nw = Math.Max(1, (int)Math.Round(w * scale)), nh = Math.Max(1, (int)Math.Round(h * scale));
        var o = new double[nw * nh];
        for (int y = 0; y < nh; y++)
        {
            double sy0 = y / scale, sy1 = (y + 1) / scale;
            for (int x = 0; x < nw; x++)
            {
                double sx0 = x / scale, sx1 = (x + 1) / scale;
                double sum = 0, area = 0;
                for (int yy = (int)sy0; yy < Math.Min(h, Math.Ceiling(sy1)); yy++)
                {
                    double fy = Math.Min(sy1, yy + 1) - Math.Max(sy0, yy);
                    if (fy <= 0) continue;
                    for (int xx = (int)sx0; xx < Math.Min(w, Math.Ceiling(sx1)); xx++)
                    {
                        double fx = Math.Min(sx1, xx + 1) - Math.Max(sx0, xx);
                        if (fx <= 0) continue;
                        sum += g[yy * w + xx] * fx * fy; area += fx * fy;
                    }
                }
                o[y * nw + x] = area > 0 ? sum / area : 1;
            }
        }
        return (nw, nh, o);
    }

    /// <summary>
    /// Puts the glyphs of other pages in place of glyphs on this one. Each paste names a page and a
    /// box on it, and a box on this page; the source box is moved to the destination's centre column
    /// and top row (a time signature's figure stands on the staff's top or middle line, so its staff
    /// lines fall on this page's), and the band two pixels round the larger of the two is taken over
    /// from the source page, staff lines and all.
    /// </summary>
    static void Paste(int w, int h, double[] g, string pastes)
    {
        var pages = new Dictionary<string, (int W, int H, double[] G)>();
        foreach (var one in pastes.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var f = one.Split('|');
            static double[] Box(string s) => s.Split(',').Select(v => double.Parse(v, CultureInfo.InvariantCulture)).ToArray();
            if (!pages.TryGetValue(f[0], out var src)) pages[f[0]] = src = Load(f[0]);
            double[] sb = Box(f[1]), db = Box(f[2]);
            int dx = (int)Math.Round((db[0] + db[2]) / 2 - (sb[0] + sb[2]) / 2), dy = (int)Math.Round(db[1] - sb[1]);
            double half = Math.Max(sb[2] - sb[0], db[2] - db[0]) / 2 + 2, mid = (db[0] + db[2]) / 2;
            int l = (int)Math.Floor(mid - half), r = (int)Math.Ceiling(mid + half);
            int t = (int)Math.Floor(Math.Min(db[1], sb[1] + dy)) - 2, b = (int)Math.Ceiling(Math.Max(db[3], sb[3] + dy)) + 2;
            for (int y = Math.Max(0, t); y <= Math.Min(h - 1, b); y++)
                for (int x = Math.Max(0, l); x <= Math.Min(w - 1, r); x++)
                {
                    int sx = x - dx, sy = y - dy;
                    g[y * w + x] = sx >= 0 && sx < src.W && sy >= 0 && sy < src.H ? src.G[sy * src.W + sx] : 1;
                }
        }
    }

    static (int, int, double[]) Load(string path)
    {
        using var bmp = new Bitmap(path);
        var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
        var data = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
        var raw = new byte[data.Stride * bmp.Height];
        Marshal.Copy(data.Scan0, raw, 0, raw.Length);
        bmp.UnlockBits(data);
        var g = new double[bmp.Width * bmp.Height];
        for (int y = 0; y < bmp.Height; y++)
            for (int x = 0; x < bmp.Width; x++)
            {
                int o = y * data.Stride + x * 3;
                g[y * bmp.Width + x] = (raw[o] + raw[o + 1] + raw[o + 2]) / (3.0 * 255);
            }
        return (bmp.Width, bmp.Height, g);
    }

    static void Save(string path, int w, int h, double[] g)
    {
        using var bmp = new Bitmap(w, h, PixelFormat.Format24bppRgb);
        var rect = new Rectangle(0, 0, w, h);
        var data = bmp.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
        var raw = new byte[data.Stride * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                byte v = (byte)Math.Round(Math.Clamp(g[y * w + x], 0, 1) * 255);
                int o = y * data.Stride + x * 3;
                raw[o] = raw[o + 1] = raw[o + 2] = v;
            }
        Marshal.Copy(raw, 0, data.Scan0, raw.Length);
        bmp.UnlockBits(data);
        bmp.Save(path, ImageFormat.Png);
    }
}
