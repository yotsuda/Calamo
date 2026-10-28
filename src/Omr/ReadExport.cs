using System.Globalization;
using System.Text;
using Omr;

/// <summary>
/// --export-read &lt;dir&gt;: the reader's own reading of a page, as training data for the learnt detector
/// (S50: a real print's shapes, where no SVG truth is): the grey page in the reader's frame (read-pNN.png,
/// what the detector reads) and read-pNN.tsv with the staff space, the staves, each measure's x span and
/// bar, the heads, rests, their signs, dots and stems, the rules' named pieces of ink, and the clefs, keys
/// and times. Which bars to learn from is decided outside (tools/train-model/pseudo.py, by the bars scored
/// right against the truth): nothing here is trusted on its own.
/// </summary>
static class ReadExport
{
    static string F(double v) => v.ToString("F1", CultureInfo.InvariantCulture);

    public static void Write(string dir, int page, PageResult res, ScoreConfig cfg)
    {
        Directory.CreateDirectory(dir);
        var d = res.Dark;
        if (d?.Gray == null) return;
        Img.SaveGrayPng(Path.Combine(dir, $"read-p{page:D2}.png"), d.W, d.H, d.Gray);
        double s = res.S;
        string Part(Staff st) => st == null ? "-" : st.Part >= 0 && st.Part < cfg.Parts.Count ? cfg.Parts[st.Part].Id : "-";
        var tsv = new StringBuilder("kind\tclass\tx\ty\tsys\tpart\ta\tb\tc\td\n");
        void Row(string kind, string cls, double x, double y, Staff st, params object[] more)
        {
            tsv.Append(kind).Append('\t').Append(cls).Append('\t').Append(F(x)).Append('\t').Append(F(y)).Append('\t')
               .Append(st == null ? "-" : (st.System + 1).ToString()).Append('\t').Append(Part(st));
            for (int i = 0; i < 4; i++)
                tsv.Append('\t').Append(i < more.Length ? more[i] is double v ? F(v) : more[i]?.ToString() ?? "-" : "-");
            tsv.Append('\n');
        }
        tsv.Append("space\t-\t").Append(F(s)).Append("\t-\t-\t-\t-\t-\t-\t-\n");
        foreach (var sys in res.Systems)
        {
            foreach (var st in sys.All)
                Row("staff", st.Kind == "" && !st.OneLine ? "5" : st.OneLine ? "L" : st.Kind, st.Left, st.Top, st, st.Right, st.Bottom, st.S);
            // a measure's span: its bar number (a multi-bar rest's first) and its two ends
            for (int m = 0; m < sys.Measures; m++)
                Row("measure", sys.Span(m) > 1 ? "multi" : "1", sys.Bars[m], sys.Bars[m + 1], sys.Staves.FirstOrDefault(), sys.BarOf(m));
        }
        foreach (var h in res.Sym.Heads)
        {
            string cls = h.Cross ? "head_x" : h.Slash ? (h.Hollow ? "slash_hollow" : "slash_black") : h.Hollow ? (h.HasStem ? "head_half" : "head_whole") : "head_black";
            Row("head", cls, h.X, h.Y, h.St, h.L, h.R, h.Pos, h.Score);
            if (h.Acc.HasValue) Row("acc", h.Acc.Value switch { 1 => "sharp", -1 => "flat", 0 => "natural", 2 => "dsharp", _ => "dflat" }, h.X, h.Y, h.St, h.L);
            if (h.Dots > 0) Row("dots", h.Dots.ToString(), h.X, h.Y, h.St, h.R, h.Pos);
            if (h.HasStem) Row("stem", $"stem_{Math.Min(3, h.Beams)}", h.StemX, h.Tip, h.St, h.Up ? 1 : 0);
            if (h.Tremolo > 0) Row("tremolo", "-", h.X, h.Y, h.St);
        }
        foreach (var g in res.Sym.Graces) Row("grace", "grace_head", g.X, g.Y, g.St);
        foreach (var r in res.Sym.Rests)
        {
            Row("rest", r.Kind switch { RestKind.Whole or RestKind.Half => "rest_long", RestKind.Quarter => "rest_4", RestKind.Eighth => "rest_8", _ => "rest_16" }, r.X, r.Y, r.St, r.L, r.R);
            if (r.Dots > 0) Row("rdots", r.Dots.ToString(), r.X, r.Y, r.St, r.R);
        }
        foreach (var o in res.Sym.Others)
            Row("other", o.Class, o.C.MinX + o.C.W / 2.0, o.C.MinY + o.C.H / 2.0, o.St, o.C.MinX, o.C.MinY, o.C.MaxX, o.C.MaxY);
        foreach (var t in res.Sym.Tuplets)
            Row("tuplet", t.Class, t.C.MinX + t.C.W / 2.0, t.C.MinY + t.C.H / 2.0, t.St);
        foreach (var b in res.Sym.BeatRepeats) Row("beatrepeat", "-", b.L, b.St.Mid, b.St, b.R);
        foreach (var g in res.Sym.Sigs)
            Row("sig", g.Clef ?? "-", g.X, g.St?.Mid ?? 0, g.St, g.Fifths?.ToString() ?? "-", g.Time ?? "-", g.Mid ? 1 : 0, g.Guessed ? 1 : 0);
        File.WriteAllText(Path.Combine(dir, $"read-p{page:D2}.tsv"), tsv.ToString());
    }
}
