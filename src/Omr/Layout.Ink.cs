namespace Omr;

static partial class Layout
{
    /// <summary>
    /// Fills the white holes smaller than a fifth of a space each way that lie in solid ink (a
    /// scan's pinholes in a head). A sharp's window or a clef's loop is as small, but the ink
    /// around it is a thin stroke, and past the stroke is white: those stay.
    /// </summary>
    public static void FillPinholes(Img d, double s)
    {
        int lim = (int)(0.2 * s), rim = (int)(0.15 * s);
        foreach (var hole in Components.Holes(d))
        {
            // (a pinhole is roundish; the thin crescent of white between two heads that touch is not)
            if (hole.W > lim || hole.H > lim || Math.Min(hole.W, hole.H) < 0.5 * Math.Max(hole.W, hole.H)) continue;
            // (nine tenths of the rim dark: the toner leaves several pinholes near one another)
            int rimAll = 0, rimDark = 0;
            for (int y = hole.MinY - rim; y <= hole.MaxY + rim; y++)
                for (int x = hole.MinX - rim; x <= hole.MaxX + rim; x++)
                    if (x < hole.MinX || x > hole.MaxX || y < hole.MinY || y > hole.MaxY) { rimAll++; if (d[x, y]) rimDark++; }
            if (rimDark < 0.9 * rimAll) continue;
            for (int y = hole.MinY; y <= hole.MaxY; y++)
                for (int x = hole.MinX; x <= hole.MaxX; x++) d.D[y * d.W + x] = true;
        }
    }

    /// <summary>Ink with the staff lines and ledger lines removed, except where a symbol crosses them.</summary>
    public static Img MakeInk(Img d, List<Staff> staves, double s, List<Staff> extras = null)
    {
        var ink = d.Clone();
        foreach (var st in extras == null ? staves : staves.Concat(extras))
            foreach (var ln in st.Lines)
                RemoveH(d, ink, ln.Top - 1, ln.Bot + 1, st.Left - 2, st.Right + 2);
        int thin = (int)Math.Round(s * 0.25);
        foreach (var st in staves)
            for (int k = 1; k <= 6; k++)
                foreach (double yc in new[] { st.Top - k * st.S, st.Bottom + k * st.S })
                    for (int y = (int)(yc - 0.2 * s); y <= (int)(yc + 0.2 * s); y++)
                        for (int x = st.Left; x <= st.Right;)
                        {
                            if (!d[x, y]) { x++; continue; }
                            int a = x;
                            while (x <= st.Right && d[x, y]) x++;
                            int b = x - 1, len = b - a + 1;
                            if (len < 1.0 * s || len > 3.6 * s) continue;
                            int x1 = a + 2, x2 = b - 2;
                            if (d[x1, y - thin] || d[x1, y + thin] || d[x2, y - thin] || d[x2, y + thin]) continue;
                            int bt = y, bb = y;
                            while (d[x1, bt - 1]) bt--;
                            while (d[x1, bb + 1]) bb++;
                            if (bb - bt > 0.2 * s) continue;
                            RemoveH(d, ink, bt - 1, bb + 1, a, b);
                        }
        return ink;
    }

    /// <summary>
    /// Takes the line out of the ink, column by column: the run of dark rows through the band yt..yb
    /// (the line's rows and one each side), where that run is no taller than the band and a row each
    /// side -- a symbol crossing the line there runs further. The run, not the band: on a page bowed
    /// towards its binding and levelled by strips (Img.Warp) the line drifts a row or two out of the
    /// band here and there, and the band alone left it in dashes that tied the beams, chords and clefs
    /// of a staff into one piece fifty spaces wide (kinkel7-1's second page).
    /// </summary>
    static void RemoveH(Img d, Img ink, int yt, int yb, int x0, int x1)
    {
        int line = yb - yt - 1;    // the line's own rows (the band is a row wider each side)
        for (int x = x0; x <= x1; x++)
        {
            // the line as it was: the band's rows go when nothing stands two rows beyond the band
            if (!d[x, yt - 2] && !d[x, yb + 2])
            {
                for (int y = yt; y <= yb; y++) ink[x, y] = false;
                continue;
            }
            // and the line drifted a row or two out of the band: the run of dark rows through the band,
            // where it is no taller than the line and a row (a stroke of a symbol on the line runs taller:
            // at scale 3 a run of the band and a row more took a key's signs and a time's digits apart).
            // Only on a page levelled by strips, where lines drift: elsewhere this run is a hollow head's
            // rim or a slur crossing the line, and went with it (a half note lost in every piano suite)
            if (d.Bowed == null) continue;
            int a = -1;
            for (int y = Math.Max(0, yt); y <= yb && y < d.H && a < 0; y++) if (d[x, y]) a = y;
            if (a < 0) continue;
            int b = a;
            while (b + 1 < d.H && d[x, b + 1]) b++;
            while (a - 1 >= 0 && d[x, a - 1]) a--;
            // (a row: two rows took gen-lo10's basic from 97.7 to 96.4 by the rules alone for kinkel7-1's 84.4 -> 85.1)
            if (b - a + 1 > line + 1) continue;
            for (int y = a; y <= b; y++) ink[x, y] = false;
        }
    }
}
