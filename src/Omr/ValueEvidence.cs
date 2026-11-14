using System.Globalization;
using System.Text;

namespace Omr;

/// <summary>
/// What the page showed for an event's value, every reading's score (S61, step 1 of docs/assembly-inference.md):
/// the candidates an inference over the bar's constraints will weigh -- the rules' count of beams beside the
/// detector's, each stem_N's score at the stem's tip (its faint ones too, Detector.Faint), the head's kinds, a
/// dot, a rest's classes. Nothing in the reading takes it yet: with it or without it, the reading is the same.
/// </summary>
sealed class ValueEvidence
{
    public double Head;                      // the detector's score of its surest head (0: the rules found them all; a rest's: 0)
    public double Black, Half;               // the best head_black and head_half at its heads
    public int RuleBeams = -1;               // the rules' count of beams or flags at its stem (-1: not counted: no stem, hollow)
    public double[] Stem = new double[4];    // the detector's best stem_N at its stem's tip, N = 0..3, its faint ones too (0: none seen)
    public double[] Rest;                    // a rest's: the best rest_4, rest_8, rest_16 at it, its weak ones too (null: a note's)
    public double Dot;                       // the best dot right of it, its weak ones too

    /// <summary>Each head's and rest's evidence, from the page's detections (none without the detector).</summary>
    public static void Attach(PageSymbols ps, double s)
    {
        if (ps.Detections == null) return;
        var dets = ps.Detections.Concat(ps.Faint).Concat(ps.WeakDots).Concat(ps.WeakRests).ToList();
        double Best(string cls, double x, double y, double dx, double dy) =>
            dets.Where(t => t.Class == cls && Math.Abs(t.X - x) < dx && Math.Abs(t.Y - y) < dy).Select(t => t.Score).DefaultIfEmpty(0).Max();
        // (the windows Fixes weighs its readings in: a stem's tip, a head, a rest, a dot right of it)
        double DotRight(double r, double y) => Best("dot", r + 1.3 * s, y - 0.25 * s, 1.3 * s, 0.9 * s);
        foreach (var h in ps.Heads)
        {
            var v = new ValueEvidence { Head = h.Score, RuleBeams = h.RuleBeams,
                                        Black = Best("head_black", h.X, h.Y, 0.6 * s, 0.6 * s), Half = Best("head_half", h.X, h.Y, 0.6 * s, 0.6 * s), Dot = DotRight(h.R, h.Y) };
            if (h.HasStem)
                for (int n = 0; n < 4; n++) v.Stem[n] = Best($"stem_{n}", h.StemX, h.Tip, 0.6 * s, 1.5 * s);
            h.Seen = v;
        }
        foreach (var r in ps.Rests)
            r.Seen = new ValueEvidence { Rest = new[] { Best("rest_4", r.X, r.Y, 1.0 * s, 1.5 * s), Best("rest_8", r.X, r.Y, 1.0 * s, 1.5 * s), Best("rest_16", r.X, r.Y, 1.0 * s, 1.5 * s) },
                                         Dot = DotRight(r.R, r.Y) };
    }

    /// <summary>A chord's: the best of its heads' (the rules' count, the larger).</summary>
    public static ValueEvidence Join(ValueEvidence a, ValueEvidence b)
    {
        if (a == null || b == null) return a ?? b;
        return new ValueEvidence
        {
            Head = Math.Max(a.Head, b.Head), Black = Math.Max(a.Black, b.Black), Half = Math.Max(a.Half, b.Half),
            RuleBeams = Math.Max(a.RuleBeams, b.RuleBeams), Stem = a.Stem.Zip(b.Stem, Math.Max).ToArray(),
            Rest = a.Rest == null || b.Rest == null ? a.Rest ?? b.Rest : a.Rest.Zip(b.Rest, Math.Max).ToArray(), Dot = Math.Max(a.Dot, b.Dot),
        };
    }

    public const string Header = "head\tblack\thalf\trule_beams\tstem_0\tstem_1\tstem_2\tstem_3\trest_4\trest_8\trest_16\tdot";

    public string Tsv()
    {
        static string F(double x) => x.ToString("F2", CultureInfo.InvariantCulture);
        var r = Rest ?? new double[3];
        return string.Join("\t", new[] { F(Head), F(Black), F(Half), RuleBeams.ToString(CultureInfo.InvariantCulture) }
            .Concat(Stem.Select(F)).Concat(r.Select(F)).Append(F(Dot)));
    }

    /// <summary>
    /// --candidates: every event of the page's bars with its evidence, and every tuplet number the detector saw
    /// (sure, weak or faint) with whether the reading took one there -- candidates.tsv, for measuring what an
    /// inference would have to choose from. Off, the reading is the same to the byte.
    /// </summary>
    public static void Write(StringBuilder events, StringBuilder tuplets, PageResult res, Func<int, string> partId)
    {
        static string F(double x) => x.ToString("F1", CultureInfo.InvariantCulture);
        foreach (var ((bar, part), evs) in res.Bars.OrderBy(kv => kv.Key.Bar).ThenBy(kv => kv.Key.Part))
            foreach (var e in evs)
                events.Append($"{res.Page}\t{bar}\t{partId(part)}\t{F(e.X)}\t{F(e.Y)}\t{e.Show()}\t{(e.Tuplet != 0 ? $"{e.Scale.Num}/{e.Scale.Den}{(e.Tuplet < 0 ? " bare" : "")}" : "")}\t{e.Seen?.Tsv() ?? ""}\n");
        var ps = res.Sym;
        if (ps.Detections == null) return;
        double s = res.S;
        foreach (var t in ps.Detections.Concat(ps.WeakTuplets).Concat(ps.Faint).Where(t => t.Class.StartsWith("tuplet_")).OrderBy(t => t.Y).ThenBy(t => t.X))
        {
            bool taken = ps.Tuplets.Any(o => o.C != null && t.X > o.C.MinX - 0.5 * s && t.X < o.C.MaxX + 0.5 * s && t.Y > o.C.MinY - 0.5 * s && t.Y < o.C.MaxY + 0.5 * s);
            string how = t.Score > Detector.Threshold ? "sure" : ps.Faint.Contains(t) ? "faint" : "weak";
            tuplets.Append($"{res.Page}\t{t.Class}\t{F(t.X)}\t{F(t.Y)}\t{t.Score.ToString("F3", CultureInfo.InvariantCulture)}\t{how}\t{(taken ? 1 : 0)}\n");
        }
    }
}
