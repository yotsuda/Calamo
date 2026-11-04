using System.Drawing;
using System.Drawing.Imaging;
using System.Text.RegularExpressions;

namespace Omr;

/// <summary>
/// A song's words under a staff (S54): rows of upright letters, a syllable each run of letters, on
/// the note it stands under; the .lys writes them as lyrics tracks, one a verse.
///
/// Where the syllables part is the ink's to say and what they spell the OCR's: read as a line, the OCR
/// runs "spran- gen" into "sprangen" and drops the hyphen. So the letters are cut by their gaps, a
/// hyphen (a short thin dash at the letters' middle) kept aside, and each syllable is pasted on a strip
/// of its own far from the next, which the OCR returns as a line each (Header.Recognize parts a line
/// where words stand more than an em apart).
///
/// A row is the words' when most of its syllables stand on notes of the staff above and it stands
/// upright (a text script leans: Texts): a bar number or a tempo marks no notes.
/// </summary>
static class Lyrics
{
    public static bool Verbose;

    /// <summary>A syllable: which line of words under the staff, what it says, whether a hyphen goes on to the next.</summary>
    public readonly record struct Syllable(int Verse, string Text, bool Hyphen);

    sealed class Cut
    {
        public int L, R;
        public bool Hyphen;
        public string Text;
        public List<(int L, int R)> Ink = new();     // its pieces of ink, left to right
        public int FirstH;                           // the height of its first letter (0: not known)
        public double Small;                         // the row's small letters' height
        public double Cx => (L + R) / 2.0;
    }

    public static void Apply(string path, PageResult res)
    {
        if (res.Dark == null || res.Systems.Count == 0) return;
        Bitmap page = null;
        try
        {
            var systems = res.Systems.Where(q => q.Measures > 0 && q.Staves.Count > 0).ToList();
            var staves = systems.SelectMany(q => q.Staves).OrderBy(q => q.Top).ToList();
            foreach (var st in staves)
            {
                var rows = Rows(res, st, staves);
                int verse = 0;
                foreach (var (top, foot, whole) in rows)
                {
                    var cuts = Parted(res, st, whole);
                    var on = Assign(res, st, cuts);
                    // (the staff's words, or a bar number's, a tempo's, a piano's text: most stand on notes)
                    if (on.Count < 1 || on.Sum(v => v.Cut.R - v.Cut.L) < 0.6 * cuts.Sum(c => c.R - c.L))
                    {
                        if (Verbose) res.Notes.Add($"lyrics s{st.System + 1} part {st.Part} row at {foot}: {cuts.Count} cuts, {on.Count} on notes: not words");
                        continue;
                    }
                    page ??= new Bitmap(path);
                    Read(page, res, cuts, top, foot);
                    // (bits of letters the OCR reads nothing in, between two verses: no verse of its own, or
                    // the verses under it would count one on)
                    if (cuts.Count(c => c.Text != null) * 2 <= cuts.Count)
                    {
                        if (Verbose) res.Notes.Add($"lyrics s{st.System + 1} part {st.Part} row at {foot}: {cuts.Count} cuts, read {cuts.Count(c => c.Text != null)}: not words");
                        continue;
                    }
                    foreach (var (c, head) in on)
                    {
                        if (c.Text == null) continue;
                        // (a verse's number before the first note)
                        if (Regex.IsMatch(c.Text, @"^\d+[.)]?$")) continue;
                        foreach (var e in Articulations.Events(res, head, res.S))
                        {
                            e.Words ??= new List<Syllable>();
                            if (e.Words.Any(w => w.Verse == verse)) continue;
                            e.Words.Add(new Syllable(verse, c.Text, c.Hyphen));
                        }
                    }
                    if (Verbose) res.Notes.Add($"lyrics s{st.System + 1} part {st.Part} verse {verse + 1}: " + string.Join(" ", cuts.Select(c => (c.Text ?? "?") + (c.Hyphen ? "-" : ""))));
                    verse++;
                }
            }
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            Console.WriteLine($"lyrics: no OCR ({(string.IsNullOrWhiteSpace(e.Message) ? $"0x{e.HResult:X8}" : e.Message.Trim())})");
        }
        finally { page?.Dispose(); }
    }

    /// <summary>
    /// The rows of letters between a staff and the next below (the working image): each its top, the
    /// line its letters stand on, and its syllables, cut where the letters stand half a space apart.
    /// </summary>
    static List<(int Top, int Foot, List<Cut> Cuts)> Rows(PageResult res, Staff st, List<Staff> staves)
    {
        var rows = new List<(int, int, List<Cut>)>();
        double s = res.S;
        var dark = res.Dark;
        double next = staves.Where(o => o.Top > st.Bottom).Select(o => (double)o.Top).DefaultIfEmpty(dark.H).Min();
        int x0 = Math.Max(0, st.Left), x1 = Math.Min(dark.W - 1, st.Right + (int)s), y0 = (int)(st.Bottom + 0.5 * s), y1 = (int)Math.Min(Math.Min(dark.H - 1, next - 0.5 * s), st.Bottom + 20 * s);
        int w = x1 - x0 + 1, h = y1 - y0 + 1;
        if (w < 10 * s || h < 2 * s) return rows;
        var (_, comps) = Components.Label(w, h, i => dark[x0 + i % w, y0 + i / w], eight: true);
        // (the heads of the notes low under the staff and high over the next one, and their stems)
        var heads = res.Sym.Heads.Where(q => q.X >= x0 && q.X <= x1 && q.Y >= y0 && q.Y <= y1).Select(q => (q.X, q.Y)).ToList();
        bool HasHead(Comp c) => heads.Any(p => p.X >= x0 + c.MinX - 1 && p.X <= x0 + c.MaxX + 1 && p.Y >= y0 + c.MinY - 1 && p.Y <= y0 + c.MaxY + 1);
        var all = comps.Skip(1).Where(c => c.MinY > 0 && c.MaxY < h - 1 && !HasHead(c)).ToList();
        var letters = all.Where(c => c.H >= 0.5 * s && c.H <= 2.5 * s && c.W <= 3.5 * s).ToList();
        if (letters.Count < 2) return rows;
        // the rows: the small letters' bands, which overlap within a row however the paper bows it (a foot
        // line shared within a fifth of a space split one old print's verse in two) and not between rows
        double small = letters.Select(c => (double)c.H).OrderBy(v => v).ElementAt(letters.Count / 4);
        var bands = new List<(int Top, int Bottom, List<Comp> Bodies)>();
        foreach (var c in letters.Where(c => c.H <= 1.25 * small).OrderBy(c => c.MinY))
        {
            if (bands.Count > 0 && c.MinY <= bands[^1].Bottom - 0.25 * small)
            {
                var b = bands[^1];
                b.Bodies.Add(c);
                bands[^1] = (b.Top, Math.Max(b.Bottom, c.MaxY), b.Bodies);
            }
            else bands.Add((c.MinY, c.MaxY, new List<Comp> { c }));
        }
        var feet = bands.Where(b => b.Bodies.Count >= 2).Select(b => b.Bodies.Select(c => c.MaxY).OrderBy(y => y).ElementAt(b.Bodies.Count / 2)).ToList();
        if (feet.Count == 0) return rows;
        // (each letter its row: the foot nearest its own, a p's or a y's tail hanging under it)
        var byRow = letters.Where(c => feet.Any(f => c.MaxY >= f - 0.4 * s && c.MaxY <= f + 1.2 * s && c.MinY > f - 2.6 * s))
            .GroupBy(c => feet.Where(f => c.MaxY >= f - 0.4 * s && c.MaxY <= f + 1.2 * s && c.MinY > f - 2.6 * s).MinBy(f => Math.Abs(c.MaxY - f)));
        var groups = byRow.ToList();
        var inRows = groups.SelectMany(g => g).ToHashSet();
        foreach (var group in groups)
        {
            int foot = group.Key;
            var row = group.ToList();
            int top = row.Min(c => c.MinY);
            // (with the ink too small for letters that stands in the row: an umlaut's dots, a full stop, a hyphen)
            var ink = row.Concat(all.Where(c => !inRows.Contains(c) && (c.MinY + c.MaxY) / 2.0 >= top - 0.4 * s && (c.MinY + c.MaxY) / 2.0 <= foot + 0.35 * s)).OrderBy(c => c.MinX).ToList();
            var cuts = new List<Cut>();
            foreach (var c in ink)
            {
                bool thin = c.H <= 0.35 * s && c.W >= 1.5 * c.H;
                // (an extender's line stands on the foot, long: no syllable's, and no hyphen)
                if (thin && (c.W >= 2.5 * s || c.W >= 1.5 * s && c.MaxY >= foot - 0.1 * s)) continue;
                // (a hyphen, at the letters' middle or, in old prints, low, a short rule on the foot)
                // (a third of the way down and more, a third of a space wide and more, past the letters: not an
                // umlaut's dots or an i's over them, nor a bit of a serif broken off in a scan)
                if (thin && c.W >= 0.3 * s && c.MinY > top + 0.3 * (foot - top) && (cuts.Count == 0 || x0 + c.MinX >= cuts[^1].R - 1))
                {
                    if (cuts.Count > 0) cuts[^1].Hyphen = true;   // (as far as it may be: set midway to a syllable far off)
                    continue;
                }
                bool letter = inRows.Contains(c);
                if (cuts.Count > 0 && !cuts[^1].Hyphen && x0 + c.MinX - cuts[^1].R < 0.5 * s) cuts[^1].R = Math.Max(cuts[^1].R, x0 + c.MaxX + 1);
                // (too small to be a letter, away from any: a speck, a dot of the paper's)
                else if (!letter) continue;
                else cuts.Add(new Cut { L = x0 + c.MinX, R = x0 + c.MaxX + 1, FirstH = c.H, Small = small });
                cuts[^1].Ink.Add((x0 + c.MinX, x0 + c.MaxX + 1));
            }
            if (cuts.Count > 0 && Slant(res, cuts, y0 + top, y0 + foot) < Texts.MinSlant) rows.Add((y0 + top, y0 + foot, cuts));
        }
        rows.Sort((a, b) => a.Item2.CompareTo(b.Item2));
        return rows;
    }

    /// <summary>
    /// A cut over the heads of two notes or more may be as many syllables: where the notes stand close
    /// the engraver sets a word's syllables touching, no hyphen drawn, the gap between them no wider than
    /// between letters ("spran" and "gen", 9 pixels at a space of 56). Each syllable is centred on its
    /// note, so the first one's right end is as far past its head as its left end is before it, and so on
    /// to the last, which must come out at the cut's right end: then the cut is parted at the gaps
    /// nearest those ends. Set so close, they are one word's: a hyphen goes on.
    /// </summary>
    static List<Cut> Parted(PageResult res, Staff st, List<Cut> cuts)
    {
        double s = res.S;
        var xs = res.Sym.Heads.Where(h => h.St == st && !h.Grace).Select(h => h.X).OrderBy(x => x).ToList();
        var parted = new List<Cut>();
        foreach (var c in cuts)
        {
            var within = new List<double>();
            foreach (double x in xs.Where(x => x > c.L && x < c.R))
                if (within.Count == 0 || x - within[^1] > s) within.Add(x);
            var ink = c.Ink.OrderBy(i => i.L).ToList();
            // (the gaps between pieces of ink, not over one another: an umlaut's dots stand over their letter)
            var gaps = new List<(int At, double Mid)>();
            int reach = ink.Count > 0 ? ink[0].R : 0;
            for (int i = 1; i < ink.Count; i++)
            {
                if (ink[i].L > reach) gaps.Add((i, (ink[i].L + reach) / 2.0));
                reach = Math.Max(reach, ink[i].R);
            }
            var at = new List<int>();
            double end = c.L;
            bool fits = within.Count >= 2;
            for (int i = 0; fits && i < within.Count; i++)
            {
                end = 2 * within[i] - end;
                if (i == within.Count - 1) { fits = Math.Abs(end - c.R) <= 0.8 * s; break; }
                double wanted = end;
                var near = gaps.Where(g => at.Count == 0 || g.At > at[^1]).OrderBy(g => Math.Abs(g.Mid - wanted)).Cast<(int At, double Mid)?>().FirstOrDefault();
                if (near == null || Math.Abs(near.Value.Mid - wanted) > 0.5 * s) { fits = false; break; }
                at.Add(near.Value.At);
                end = near.Value.Mid;
            }
            // (no syllable of one piece: "T" and "rä" fit two heads under one syllable, by chance)
            int was = 0;
            foreach (int to in at.Append(ink.Count)) { if (to - was < 2) fits = false; was = to; }
            if (!fits) { parted.Add(c); continue; }
            int from = 0;
            foreach (int to in at.Append(ink.Count))
            {
                var some = ink.GetRange(from, to - from);
                parted.Add(new Cut { L = some[0].L, R = some.Max(i => i.R), Ink = some, Hyphen = to == ink.Count ? c.Hyphen : true, FirstH = from == 0 ? c.FirstH : 0, Small = c.Small });
                from = to;
            }
        }
        return parted;
    }

    // (the row's lean, from its longest cut: a text script's italic is not the words')
    static double Slant(PageResult res, List<Cut> cuts, int top, int foot)
    {
        var c = cuts.MaxBy(k => k.R - k.L);
        return Texts.Slant(res.Dark, RectangleF.FromLTRB(c.L, top, c.R, foot + 1));
    }

    /// <summary>
    /// Each cut's note: the staff's head nearest its middle, within a space and a half or half the
    /// cut's width and a space (a long syllable set at its note's left, as a melisma's). Two cuts on
    /// one note: the nearer.
    /// </summary>
    static List<(Cut Cut, Head Head)> Assign(PageResult res, Staff st, List<Cut> cuts)
    {
        double s = res.S;
        var heads = res.Sym.Heads.Where(h => h.St == st && !h.Grace).ToList();
        var best = new Dictionary<double, (Cut Cut, Head Head)>();
        foreach (var c in cuts)
        {
            var h = heads.OrderBy(q => Math.Abs(q.X - c.Cx)).FirstOrDefault();
            if (h == null || Math.Abs(h.X - c.Cx) > Math.Max(1.5 * s, (c.R - c.L) / 2.0 + s)) continue;
            double key = Math.Round(h.X / s);
            if (!best.TryGetValue(key, out var had) || Math.Abs(h.X - c.Cx) < Math.Abs(had.Head.X - had.Cut.Cx)) best[key] = (c, h);
        }
        return best.Values.OrderBy(v => v.Cut.L).ToList();
    }

    /// <summary>What each cut says: pasted on a strip two letters' heights apart, each in its own place, read as lines; a cut left unread, again alone, three times over.</summary>
    static void Read(Bitmap page, PageResult res, List<Cut> cuts, int top, int foot)
    {
        foreach (var (cut, text) in Strip(page, res, cuts, top, foot, 1)) cut.Text = text;
        // (the Japanese engine passes over one short word alone: three in a row, as Texts does)
        foreach (var c in cuts.Where(c => c.Text == null).ToList())
            foreach (var (cut, text) in Strip(page, res, [c], top, foot, 3)) cut.Text = text;
        foreach (var c in cuts) c.Text = Cased(c, c.Text);
    }

    /// <summary>
    /// Of the engines' readings of one syllable. A Latin row (most of the Japanese engine's readings
    /// hold no Japanese): the first without Japanese in it and with no oddity (a symbol against a letter),
    /// the other language's first; where the two differ only by marks (schö, scho), the one with them.
    /// A row in Japanese: the Japanese engine's.
    /// </summary>
    static string Choose(string native, string other, bool japanese)
    {
        if (japanese) return native ?? other;
        static bool Plain(string t) => t != null && !Regex.IsMatch(t, $"[{Header.Cjk}]") && Header.Oddities(t) == 0;
        static string Fold(string t) => new string(t.Normalize(System.Text.NormalizationForm.FormD).Where(ch => char.GetUnicodeCategory(ch) != System.Globalization.UnicodeCategory.NonSpacingMark).ToArray());
        if (Plain(native) && Plain(other))
            return string.Equals(Fold(native), Fold(other), StringComparison.Ordinal) && native != Fold(native) ? native : other;
        return Plain(other) ? other : Plain(native) ? native : other ?? native;
    }

    static List<(Cut, string)> Strip(Bitmap page, PageResult res, List<Cut> cuts, int top, int foot, int times)
    {
        double s = res.S;
        var dark = res.Dark;
        int pad = (int)(0.5 * s);
        int rowTop = top - pad, rowBottom = foot + (int)(1.0 * s);
        (RectangleF Box, Cut Cut) Source(Cut c)
        {
            var a = dark.ToSource(c.L - 0.1 * s, rowTop);
            var b = dark.ToSource(c.R + 0.1 * s, rowBottom);
            return (RectangleF.FromLTRB((float)Math.Min(a.X, b.X), (float)Math.Min(a.Y, b.Y), (float)Math.Max(a.X, b.X), (float)Math.Max(a.Y, b.Y)), c);
        }
        var boxes = cuts.Select(Source).ToList();
        float height = boxes.Max(b => b.Box.Height);
        // (the letters about 48 pixels tall, as the OCR reads best; the strip within its widest)
        double k = 48.0 / Math.Max(1, height * 0.6);
        float gap = 2.2f * height;
        var spans = new List<(float L, float R, Cut Cut)>();
        float at = gap;
        foreach (var (box, cut) in boxes)
            for (int t = 0; t < times; t++)
            {
                spans.Add((at, at + box.Width, cut));
                at += box.Width + gap;
            }
        int W = (int)Math.Ceiling(at), H = (int)Math.Ceiling(height + 2 * pad);
        using var strip = new Bitmap(Math.Max(1, W), Math.Max(1, H), PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(strip))
        {
            g.Clear(Color.White);
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            foreach (var (l, r, cut) in spans)
            {
                var box = boxes.First(b => b.Cut == cut).Box;
                g.DrawImage(page, new RectangleF(l, pad, box.Width, box.Height), box, GraphicsUnit.Pixel);
            }
        }
        var others = new List<List<(string Text, RectangleF Box)>>();
        var lines = Header.Recognize(strip, new Rectangle(0, 0, strip.Width, strip.Height), k, false, others);
        var theirs = others.FirstOrDefault() ?? new();
        // (a reading of the cut: the first of its places that was read)
        string Reading(List<(string Text, RectangleF Box)> from, IEnumerable<(float L, float R, Cut Cut)> places)
        {
            foreach (var (l, r, _) in places)
            {
                var its = from.Where(q => (q.Box.Left + q.Box.Right) / 2 >= l - 0.5 * gap && (q.Box.Left + q.Box.Right) / 2 <= r + 0.5 * gap).OrderBy(q => q.Box.Left).ToList();
                if (its.Count == 0) continue;
                // (several places read as one line: the first word)
                string text = Clean(its[0].Text.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "");
                if (text.Length > 0) return text;
            }
            return null;
        }
        var read = spans.GroupBy(q => q.Cut).Select(g => (Cut: g.Key, Native: Reading(lines, g), Other: Reading(theirs, g))).ToList();
        bool japanese = read.Count(q => q.Native != null && Regex.IsMatch(q.Native, $"[{Header.Cjk}]")) * 2 > read.Count;
        return read.Select(q => (q.Cut, Choose(q.Native, q.Other, japanese))).Where(q => q.Item2 != null).ToList();
    }

    // (a hyphen, dash or long mark the OCR kept at the end: the .lys writes the hyphen itself)
    static string Clean(string text) => text.Replace("•,", ";").Replace("・,", ";").Trim('-', '—', '–', 'ー', '_', '~', '|', '•');

    /// <summary>A first letter whose capital is its small letter larger (W, w) as tall as the ink says: the OCR, a syllable alone, cannot tell.</summary>
    static string Cased(Cut c, string text)
    {
        if (string.IsNullOrEmpty(text) || c.FirstH == 0 || c.Small == 0 || !"cosuvwxzCOSUVWXZ".Contains(text[0])) return text;
        char first = c.FirstH >= 1.25 * c.Small ? char.ToUpperInvariant(text[0]) : char.ToLowerInvariant(text[0]);
        return first + text[1..];
    }

    /// <summary>A syllable as a lyrics track writes it: bare, or quoted when it holds what the track's syntax reads; then its hyphen.</summary>
    public static string Lys(Syllable w)
    {
        bool bare = Regex.IsMatch(w.Text, @"^[\p{L}\p{M}'’.,;:!?]+$");
        return (bare ? w.Text : Header.Quote(w.Text)) + (w.Hyphen ? "-" : "");
    }
}
