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
        public List<(int L, int R, int T, int B)> Ink = new();     // its pieces of ink, left to right
        public List<(int L, int R, int T, int B)> Foreign = new(); // the next or last syllable's, reaching into its box
        public int FirstH;                           // the height of its first letter (0: not known)
        public double Small;                         // the row's small letters' height
        public Cut Before;                           // the syllable it was parted from (Parted), set against it with no hyphen drawn
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
                cuts[^1].Ink.Add((x0 + c.MinX, x0 + c.MaxX + 1, y0 + c.MinY, y0 + c.MaxY + 1));
            }
            if (cuts.Count > 0 && Slant(res, cuts, y0 + top, y0 + foot) < Texts.MinSlant) rows.Add((y0 + top, y0 + foot, cuts));
        }
        rows.Sort((a, b) => a.Item2.CompareTo(b.Item2));
        return rows;
    }

    /// <summary>
    /// A cut over the heads of two notes or more may be as many syllables: where the notes stand close
    /// the engraver sets a word's syllables touching, no hyphen drawn, the gap between them no wider than
    /// between letters ("spran" and "gen", 9 pixels at a space of 56). A syllable stands on its note in
    /// one of two ways: centred on it, or, on a melisma, left-aligned, its left end 0.6 spaces before the
    /// head ("Knos", "nat"), the notes it holds then taking no syllable. Pushed apart in a tight bar a
    /// centred one may lie off its head ("Seuf", 0.6 spaces). So the partition is searched, not walked
    /// from the left: the one where every syllable stands on a note in one of those ways, nearest them.
    /// Set so close, they are one word's: a hyphen goes on.
    /// </summary>
    static List<Cut> Parted(PageResult res, Staff st, List<Cut> cuts)
    {
        double s = res.S;
        // (the staff's heads, and whether a slur or a tie the reader saw holds each: it takes no syllable)
        var xs = new List<(double X, bool Held)>();
        bool slur = false, tie = false;
        foreach (var h in res.Sym.Heads.Where(h => h.St == st && !h.Grace).OrderBy(h => h.X))
        {
            xs.Add((h.X, slur || tie));
            if (h.SlurEnd) slur = false;
            if (h.SlurStart) slur = true;
            tie = h.Tie;
        }
        var parted = new List<Cut>();
        foreach (var c in cuts)
        {
            var within = new List<double>();
            var held = new List<bool>();
            foreach (var (x, h) in xs.Where(q => q.X > c.L && q.X < c.R))
                if (within.Count == 0 || x - within[^1] > s) { within.Add(x); held.Add(h); }
            var ink = c.Ink.OrderBy(i => i.L).ToList();
            var at = held.Count(h => !h) >= 2 ? Partition(within, held, Bounds(ink, s), c.L, c.R, ink.Count, s) : null;
            if (Verbose && at == null && within.Count >= 2)
                res.Notes.Add($"lyrics unparted s{st.System + 1} part {st.Part} cut {c.L}-{c.R} ({(c.R - c.L) / s:F1} sp): heads {string.Join(" ", within.Select(x => $"{(x - c.L) / s:F2}"))}; bounds {string.Join(" ", Bounds(ink, s).Select(g => $"{(g.Mid - c.L) / s:F2}"))}");
            if (at == null) { parted.Add(c); continue; }
            int from = 0;
            foreach (int to in at.Append(ink.Count))
            {
                var some = ink.GetRange(from, to - from);
                int l = some[0].L, r = some.Max(i => i.R), margin = (int)(0.1 * s) + 1;
                // (the letters of the syllables beside it that its box takes in: the OCR reads an f with the next g's edge as "f}")
                var foreign = ink.Where((i, k) => (k < from || k >= to) && i.R > l - margin && i.L < r + margin).ToList();
                parted.Add(new Cut { L = l, R = r, Ink = some, Foreign = foreign, Hyphen = to == ink.Count ? c.Hyphen : true, FirstH = some[0].B - some[0].T, Small = c.Small, Before = from > 0 ? parted[^1] : null });
                from = to;
            }
        }
        return parted;
    }

    /// <summary>
    /// Where a cut's ink may part: between pieces that stand apart, or that overlap by a little (an f's
    /// hook over the next letter, "auf|ge"), not within a letter (an umlaut's dots stand over theirs).
    /// </summary>
    static List<(int At, double Mid)> Bounds(List<(int L, int R, int T, int B)> ink, double s)
    {
        var bounds = new List<(int At, double Mid)>();
        if (ink.Count == 0) return bounds;
        int reach = ink[0].R;
        for (int i = 1; i < ink.Count; i++)
        {
            if (ink[i].R > reach && ink[i].L > reach - 0.5 * s) bounds.Add((i, (ink[i].L + reach) / 2.0));
            reach = Math.Max(reach, ink[i].R);
        }
        return bounds;
    }

    /// <summary>
    /// The pieces of ink each syllable after the first starts at, for the best way the cut's syllables
    /// stand on the heads within it, two syllables or more; null if none does. No syllable stands on a
    /// held head. A centred syllable's next is on the next head not held; a left-aligned one holds a
    /// head after it (held, or, a slur not read, the next), and the last may hold heads to the cut's end.
    /// No syllable of one piece ("T" and "rä").
    /// </summary>
    static List<int> Partition(List<double> heads, List<bool> held, List<(int At, double Mid)> bounds, double left, double right, int pieces, double s)
    {
        const double Off = 0.7, LeftSet = 0.6, LeftOff = 0.3;
        List<int> best = null;
        double bestCost = double.MaxValue;
        var path = new List<int>();
        int Free(int from) { while (from < heads.Count && held[from]) from++; return from; }
        void Go(int head, int piece, double l, double cost)
        {
            int after = Free(head + 1);
            for (int b = -1; b < bounds.Count; b++)
            {
                bool last = b < 0;
                if (!last && bounds[b].At <= piece) continue;
                int to = last ? pieces : bounds[b].At;
                if (to - piece < 2) continue;
                double r = last ? right : bounds[b].Mid;
                double h = heads[head];
                double centred = Math.Abs((l + r) / 2 - h) / s;
                double leftSet = Math.Abs(l - (h - LeftSet * s)) / s;
                // (centred: every head to the cut's end has a syllable; left-aligned: wide enough to be told from a centred one)
                bool isCentred = centred <= Off && (last ? after >= heads.Count : after < heads.Count);
                // (held heads after it, or the next taken as held: the note after that takes the next syllable)
                int leftNext = after > head + 1 ? after : Free(after + 1);
                bool isLeft = leftSet <= LeftOff && r - l >= 2 * s && (last || leftNext < heads.Count);
                if (last)
                {
                    if (path.Count == 0) continue;   // (one syllable: nothing to part)
                    double end = cost + Math.Min(isCentred ? centred : double.MaxValue, isLeft ? leftSet : double.MaxValue);
                    if ((isCentred || isLeft) && end < bestCost) { bestCost = end; best = new List<int>(path); }
                    continue;
                }
                path.Add(to);
                if (isCentred) Go(after, to, r, cost + centred);
                if (isLeft) for (int next = leftNext; next < heads.Count; next = Free(next + 1)) Go(next, to, r, cost + leftSet);
                path.RemoveAt(path.Count - 1);
            }
        }
        Go(Free(0), 0, left, 0);
        return best;
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
            // (a melisma's syllable, left-aligned on the note a slur or a tie starts at: its middle may stand nearer the note it holds)
            var melisma = c.R - c.L >= 2 * s ? heads.Where(q => (q.SlurStart || q.Tie) && Math.Abs(c.L - (q.X - 0.6 * s)) <= 0.3 * s).OrderBy(q => Math.Abs(c.L - (q.X - 0.6 * s))).FirstOrDefault() : null;
            var h = melisma ?? heads.OrderBy(q => Math.Abs(q.X - c.Cx)).FirstOrDefault();
            if (h == null || melisma == null && Math.Abs(h.X - c.Cx) > Math.Max(1.5 * s, (c.R - c.L) / 2.0 + s)) continue;
            double key = Math.Round(h.X / s);
            double Off((Cut Cut, Head Head) v) => v.Head.SlurStart || v.Head.Tie ? Math.Min(Math.Abs(v.Head.X - v.Cut.Cx), Math.Abs(v.Cut.L - (v.Head.X - 0.6 * s))) : Math.Abs(v.Head.X - v.Cut.Cx);
            if (!best.TryGetValue(key, out var had) || Off((c, h)) < Off(had)) best[key] = (c, h);
        }
        return best.Values.OrderBy(v => v.Cut.L).ToList();
    }

    /// <summary>What each cut says: pasted on a strip two letters' heights apart, each in its own place, read as lines; a cut left unread, again alone, three times over.</summary>
    static void Read(Bitmap page, PageResult res, List<Cut> cuts, int top, int foot)
    {
        foreach (var (cut, text) in Strip(page, res, cuts, top, foot, 1)) cut.Text = text;
        // (the Japanese engine passes over one short word alone: three in a row, as Texts does)
        // (and, those it still passes over, "al", at other sizes)
        foreach (double zoom in new[] { 1.0, 1.5, 0.7 })
            foreach (var c in cuts.Where(c => c.Text == null).ToList())
                foreach (var (cut, text) in Strip(page, res, [c], top, foot, 3, zoom)) cut.Text = text;
        foreach (var c in cuts) c.Text = Cased(c, c.Text);
        // (two words set close, parted as syllables: a capital starts a word, a stop ends one)
        foreach (var c in cuts.Where(c => c.Before?.Text != null && c.Text != null))
            if (char.IsUpper(c.Text[0]) || char.IsPunctuation(c.Before.Text[^1])) c.Before.Hyphen = false;
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

    static List<(Cut, string)> Strip(Bitmap page, PageResult res, List<Cut> cuts, int top, int foot, int times, double zoom = 1)
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
        double k = zoom * 48.0 / Math.Max(1, height * 0.6);
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
                foreach (var f in cut.Foreign)
                {
                    var a = dark.ToSource(f.L, f.T);
                    var b = dark.ToSource(f.R, f.B);
                    float fl = Math.Max(box.Left, (float)Math.Min(a.X, b.X)), fr = Math.Min(box.Right, (float)Math.Max(a.X, b.X));
                    float ft = Math.Max(box.Top, (float)Math.Min(a.Y, b.Y)), fb = Math.Min(box.Bottom, (float)Math.Max(a.Y, b.Y));
                    if (fr > fl && fb > ft) g.FillRectangle(Brushes.White, l + fl - box.Left, pad + ft - box.Top, fr - fl, fb - ft);
                }
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
