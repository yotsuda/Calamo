using System.Drawing;
using System.Drawing.Imaging;

namespace Omr;

/// <summary>Draws what was recognised over the page and saves one crop per system half.</summary>
static class Overlay
{
    public static void Save(string pagePath, PageResult res, ScoreConfig cfg, string outDir)
    {
        using var src = new Bitmap(pagePath);
        // (a small page read enlarged is drawn enlarged, as the reader saw it)
        using var bmp = new Bitmap(res.Dark.W, res.Dark.H, PixelFormat.Format24bppRgb);
        double s = res.S;
        using (var g = Graphics.FromImage(bmp))
        {
            g.DrawImage(src, 0, 0, res.Dark.W, res.Dark.H);
            using var font = new Font("Arial", (float)(0.55 * s), GraphicsUnit.Pixel);
            using var small = new Font("Arial", (float)(0.45 * s), GraphicsUnit.Pixel);
            var magenta = new Pen(Color.Magenta, 3);
            foreach (var sys in res.Systems)
            {
                double top = sys.All[0].Top - 2 * s, bot = sys.All[^1].Bottom + 2 * s;
                // each staff's lines as found, by kind: a part's green, a TAB's blue, a one-line staff's purple
                foreach (var st in sys.All)
                {
                    using var lp = new Pen(st.Kind == "" && !st.OneLine ? Color.FromArgb(120, Color.LimeGreen) : st.Kind == "tab" ? Color.FromArgb(120, Color.DodgerBlue) : Color.FromArgb(120, Color.Purple), 5);
                    // (a one-line staff read as a part: its printed line only, not the four imagined about it)
                    foreach (var ln in st.OneLine ? new[] { st.Lines[2] } : st.Lines) g.DrawLine(lp, st.Left, (float)ln.Y, st.Right, (float)ln.Y);
                }
                for (int i = 0; i < sys.Bars.Count; i++)
                    g.DrawLine(magenta, (float)sys.Bars[i], (float)top, (float)sys.Bars[i], (float)bot);
                for (int m = 0; m < sys.Measures; m++)
                    g.DrawString(sys.BarOf(m).ToString(), font, Brushes.Magenta, (float)sys.Bars[m] + 4, (float)top);
            }
            foreach (var h in res.Sym.Heads)
            {
                var col = h.Hollow ? Color.Blue : Color.Red;
                using var pen = new Pen(col, 3);
                g.DrawEllipse(pen, (float)(h.X - 0.6 * s), (float)(h.Y - 0.5 * s), (float)(1.2 * s), (float)s);
                if (h.HasStem)
                {
                    using var sp = new Pen(Color.LimeGreen, 3);
                    g.DrawLine(sp, (float)h.StemX, (float)h.Y, (float)h.StemX, (float)h.Tip);
                }
                string label = $"{h.Pos}{(h.Beams > 0 ? "/" + h.Beams : "")}{(h.Dots > 0 ? new string('.', h.Dots) : "")}{(h.Acc.HasValue ? AccText(h.Acc.Value) : "")}";
                var ty = h.HasStem && !h.Up ? h.Y - 1.4 * s : h.Y + 0.6 * s;
                g.DrawString(label, small, new SolidBrush(col), (float)(h.X - 0.5 * s), (float)ty);
            }
            foreach (var r in res.Sym.Rests)
            {
                using var pen = new Pen(Color.Orange, 3);
                g.DrawRectangle(pen, (float)r.L, (float)(r.Y - s), (float)(r.R - r.L), (float)(2 * s));
                g.DrawString(r.Kind + (r.Dots > 0 ? "." : ""), small, Brushes.DarkOrange, (float)r.L, (float)(r.Y + s));
            }
            foreach (var o in res.Sym.Others)
            {
                var c = o.C;
                var col = o.Class switch { "?" => Color.Gray, "dot*" => Color.Goldenrod, _ => Color.Teal };
                using var pen = new Pen(col, 2);
                g.DrawRectangle(pen, c.MinX, c.MinY, c.W, c.H);
                if (o.Class != "dot*") g.DrawString(o.Class, small, new SolidBrush(col), c.MinX, c.MaxY + 2);
            }
        }
        Directory.CreateDirectory(outDir);
        foreach (var sys in res.Systems)
        {
            int top = Math.Max(0, (int)(sys.All[0].Top - 7 * s)), bot = Math.Min(bmp.Height - 1, (int)(sys.All[^1].Bottom + 5 * s));
            foreach (var half in new[] { 0, 1 })
            {
                int x0 = half == 0 ? 0 : (int)(bmp.Width * 0.47), x1 = half == 0 ? (int)(bmp.Width * 0.56) : bmp.Width;
                using var crop = bmp.Clone(new Rectangle(x0, top, x1 - x0, bot - top), bmp.PixelFormat);
                crop.Save(Path.Combine(outDir, $"p{res.Page:D2}s{sys.Index + 1}{(half == 0 ? 'L' : 'R')}.png"));
            }
        }
    }

    public static string AccText(int a) => a switch { -2 => "bb", -1 => "b", 0 => "n", 1 => "#", 2 => "x", _ => "?" };
}
