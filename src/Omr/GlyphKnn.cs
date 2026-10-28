using System.Globalization;
using System.Text;

namespace Omr;

/// <summary>
/// Shape classes of the loose symbols (accidentals, dots, rests, ...) by nearest neighbours among
/// samples labelled from Lily#'s own drawing of generated scores: the thresholds are learnt, not written.
/// </summary>
static class GlyphKnn
{
    public const int G = 8;                      // shape grid, G x G over the symbol's box
    static List<(string Label, double[] F)> train;
    // samples the score being read taught (its own font), beside the generated ones
    static readonly List<(string Label, double[] F)> local = new();
    public static bool Enabled => train != null && train.Count > 0;
    public static int LocalCount => local.Count;
    public static void AddLocal(string label, double[] f) { local.Add((label, f)); families.Clear(); }
    public static void ClearLocal() { local.Clear(); families.Clear(); }
    // each family's samples (train, then local), sorted by width: a symbol is compared only with those
    // within a space of its width (a scan's specks are thousands of symbols; each went through every sample)
    static readonly Dictionary<string, (double[] W, (string Label, double[] F)[] S)> families = new();

    /// <summary>Size in spaces, fill, and the ink share of each cell of a grid over the box.</summary>
    public static double[] Features(PageSymbols ps, Comp c, double s)
    {
        var f = new double[3 + G * G];
        f[0] = c.W / s; f[1] = c.H / s; f[2] = c.Fill;
        var n = new int[G * G];
        var ink = new int[G * G];
        for (int y = c.MinY; y <= c.MaxY; y++)
            for (int x = c.MinX; x <= c.MaxX; x++)
            {
                int cell = (y - c.MinY) * G / c.H * G + (x - c.MinX) * G / c.W;
                n[cell]++;
                if (ps.Labels[y * ps.LabelW + x] == c.Id) ink[cell]++;
            }
        for (int i = 0; i < G * G; i++) f[3 + i] = n[i] == 0 ? 0 : ink[i] / (double)n[i];
        return f;
    }

    /// <summary>
    /// The same features over all the ink in a rectangle (for a sign that comes in pieces: a cut
    /// C split by its stroke, digits that touch), plus any extra numbers the caller adds.
    /// </summary>
    public static double[] BoxFeatures(Img ink, Comp box, double s, params double[] extra)
    {
        var f = new double[3 + G * G + extra.Length];
        f[0] = box.W / s; f[1] = box.H / s;
        var n = new int[G * G];
        var on = new int[G * G];
        int all = 0;
        for (int y = box.MinY; y <= box.MaxY; y++)
            for (int x = box.MinX; x <= box.MaxX; x++)
            {
                int cell = (y - box.MinY) * G / box.H * G + (x - box.MinX) * G / box.W;
                n[cell]++;
                if (ink[x, y]) { on[cell]++; all++; }
            }
        f[2] = all / (double)(box.W * box.H);
        for (int i = 0; i < G * G; i++) f[3 + i] = n[i] == 0 ? 0 : on[i] / (double)n[i];
        for (int i = 0; i < extra.Length; i++) f[3 + G * G + i] = extra[i];
        return f;
    }

    /// <summary>The distance, or anything at least <paramref name="bound"/> once it is past it.</summary>
    static double Dist(double[] a, double[] b, double bound = double.MaxValue)
    {
        double d = 0;
        // size counts for more than any one cell: a dot and a whole rest are both solid squares
        for (int i = 0; i < 2; i++) d += 9 * (a[i] - b[i]) * (a[i] - b[i]);
        d += 4 * (a[2] - b[2]) * (a[2] - b[2]);
        if (d >= bound) return d;
        int grid = 3 + G * G;
        for (int i = 3; i < grid; i++)
        {
            d += (a[i] - b[i]) * (a[i] - b[i]);
            if ((i & 7) == 0 && d >= bound) return d;
        }
        // extras (where the sign stands on the staff) weigh like size
        for (int i = grid; i < a.Length; i++) d += 9 * (a[i] - b[i]) * (a[i] - b[i]);
        return d;
    }

    public static void Load(string path)
    {
        train = new();
        families.Clear();
        foreach (var line in File.ReadLines(path))
        {
            if (line.StartsWith('#') || line.Length == 0) continue;
            var p = line.Split('\t');
            train.Add((p[0], p.Skip(1).Select(v => double.Parse(v, CultureInfo.InvariantCulture)).ToArray()));
        }
    }

    /// <summary>
    /// The label most of the k nearest samples carry, and how near the nearest of them is.
    /// Samples are in families: loose pieces (plain glyph names), "t:" whole time signatures,
    /// "d:" time-signature digits; <paramref name="family"/> picks one ("" = loose pieces).
    /// </summary>
    public static (string Label, double Dist) Classify(double[] f, int k = 5, string family = "")
    {
        var best = new List<(double D, string L)>();
        if (!families.TryGetValue(family, out var fam))
        {
            var all = train.Concat(local).Where(x => family == "" ? !x.Label.Contains(':') : x.Label.StartsWith(family))
                           .Select((x, i) => (x, i)).OrderBy(p => p.x.F[0]).ThenBy(p => p.i).Select(p => p.x).ToArray();
            families[family] = fam = (all.Select(x => x.F[0]).ToArray(), all);
        }
        // (the first sample at least f[0] - 1 wide)
        int lo = 0, hi = fam.W.Length;
        while (lo < hi) { int mid = (lo + hi) / 2; if (fam.W[mid] < f[0] - 1.0) lo = mid + 1; else hi = mid; }
        for (int j = lo; j < fam.S.Length && fam.W[j] <= f[0] + 1.0; j++)
        {
            var (label, t) = fam.S[j];
            if (t.Length != f.Length) continue;
            // cheap reject on size before the full distance
            if (Math.Abs(t[0] - f[0]) > 1.0 || Math.Abs(t[1] - f[1]) > 1.0) continue;
            double d = Dist(f, t, best.Count < k ? double.MaxValue : best[^1].D);
            if (best.Count < k) { best.Add((d, label)); best.Sort((a, b) => a.D.CompareTo(b.D)); }
            else if (d < best[^1].D) { best[^1] = (d, label); best.Sort((a, b) => a.D.CompareTo(b.D)); }
        }
        if (best.Count == 0) return ("none", double.MaxValue);
        // votes weighted by nearness
        var vote = best.GroupBy(b => b.L).Select(g => (L: g.Key, W: g.Sum(b => 1 / (0.05 + b.D)))).OrderByDescending(v => v.W).First();
        return (vote.L, best[0].D);
    }

    public static string Row(string label, double[] f) =>
        label + "\t" + string.Join("\t", f.Select(v => v.ToString("0.###", CultureInfo.InvariantCulture)));
}
