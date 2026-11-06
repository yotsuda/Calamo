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

    /// <summary>A part sings if its longest track has this share of the longest of any part (else its rows are other text under a staff).</summary>
    public const double MinSinging = 0.2;

    /// <summary>A syllable: which line of words under the staff, what it says, whether a hyphen goes on to the next.</summary>
    public readonly record struct Syllable(int Verse, string Text, bool Hyphen);

    sealed class Cut
    {
        public int L, R;
        public bool Hyphen;
        public string Text;
        public List<(int L, int R, int T, int B)> Ink = new();     // its pieces of ink, left to right
        public List<(int L, int R, int T, int B)> Foreign = new(); // the next or last syllable's, reaching into its box
        public List<(int L, int R, int T, int B)> Stems = new();   // the stems over its letters that touched them, cut off (painted white for the OCR)
        public int FirstH;                           // the height of its first letter (0: not known)
        public double Small;                         // the row's small letters' height
        public int Foot;                             // the row's foot line
        public Cut Before;                           // the syllable it was parted from (Parted), set against it with no hyphen drawn
        public int Tail;                             // the right end of a stop set a space after it (read with it, not placed by it; 0: none)
        public char Stop;                            // the comma, full stop, "!" or "?" its last ink has the shape of (0: none), for an OCR that drops it
        public bool StopAfterLine;                   // the stop set after the extender's line drawn from it: no speck of the paper's
        public bool StopAfterDashes;                 // the stop set after dashes from it: an extender's if a sentence starts after it, else a hyphen's last bit
        public bool StopBig;                         // the full stop set apart as big as a printed one, a third of a small letter wide and tall and more: no speck of the paper's
        public int Clear;                            // the row set close under the staff: over this line the staff's ink, painted white for the OCR (0: none)
        public List<double> Apostrophes = new();     // the middles of the ink over its letters with an apostrophe's shape, for an OCR that drops it
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
                        if (Regex.IsMatch(c.Text, @"^\d+[.)・]?$")) continue;
                        foreach (var e in Articulations.Events(res, head, res.S))
                        {
                            e.Words ??= new List<Syllable>();
                            if (e.Words.Any(w => w.Verse == verse)) continue;
                            e.Words.Add(new Syllable(verse, c.Text, c.Hyphen));
                        }
                    }
                    if (Verbose) res.Notes.Add($"lyrics s{st.System + 1} part {st.Part} verse {verse + 1}: " + string.Join(" ", cuts.Select(c => (c.Text ?? "?") + (c.Hyphen ? "-" : "") + $"@{c.L}" + (c.Stop != 0 ? $"[{c.Stop}]" : ""))));
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
        var (labels, comps) = Components.Label(w, h, i => dark[x0 + i % w, y0 + i / w], eight: true);
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
        // (a band of small letters' height: apostrophes over a row, "träumt’ … in’s", half a small letter tall and
        // just letter-sized, made a band of their own, a row of no words that took them from their syllables)
        var feet = bands.Where(b => b.Bodies.Count >= 2 && b.Bodies.Select(c => c.H).OrderBy(v => v).ElementAt(b.Bodies.Count / 2) >= 0.7 * small).Select(b => b.Bodies.Select(c => c.MaxY).OrderBy(y => y).ElementAt(b.Bodies.Count / 2)).ToList();
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
            // (a letter a stem coming down from the staff touches, "la" under a run of flagged notes, or a row set
            // so close under the staff that the region's top edge cuts its tall letters: one piece with the stem,
            // or touching the region's edge, so no letter. Its part under the row's top is labelled again, the
            // stem over it painted white for the OCR; a piece too big for a letter, a slur through the letters, not)
            var clipped = new Dictionary<Comp, (int L, int R, int T, int B)>();
            foreach (var c in comps.Skip(1).Where(c => c.MaxY >= foot - 0.4 * s && c.MaxY <= foot + 0.35 * s && !inRows.Contains(c)
                && (c.MinY == 0 || c.H > 2.5 * s && c.MinY < top - 0.2 * small)).ToList())
            {
                int sw = c.W, sh = c.MaxY - top + 1;
                var (_, subs) = Components.Label(sw, sh, i => labels[(top + i / sw) * w + c.MinX + i % sw] == c.Id, eight: true);
                foreach (var p in subs.Skip(1).Where(p => p.H >= 0.5 * s && p.H <= 2.5 * s && p.W >= 0.25 * small && p.W <= 3.5 * s))
                {
                    var piece = new Comp { Id = -1, N = p.N, MinX = c.MinX + p.MinX, MaxX = c.MinX + p.MaxX, MinY = top + p.MinY, MaxY = top + p.MaxY, Sx = p.Sx + (long)c.MinX * p.N, Sy = p.Sy + (long)top * p.N };
                    if (piece.MaxY < foot - 0.4 * s) continue;
                    row.Add(piece);
                    all.Add(piece);
                    inRows.Add(piece);
                    // (over the region's edge, the stem goes on up to the staff: the OCR's cut reaches half a space over the row)
                    if (c.MinY < top || c.MinY == 0) clipped[piece] = (x0 + c.MinX, x0 + c.MaxX + 1, y0 + c.MinY - (c.MinY == 0 ? (int)s : 0), y0 + top);
                    if (Verbose) res.Notes.Add($"lyrics letter cut from a stem at {x0 + piece.MinX},{y0 + piece.MinY}: {piece.W}x{piece.H}");
                }
            }
            // (with the ink too small for letters that stands in the row: an umlaut's dots, a full stop, a hyphen)
            var ink = row.Concat(all.Where(c => !inRows.Contains(c) && (c.MinY + c.MaxY) / 2.0 >= top - 0.4 * s && (c.MinY + c.MaxY) / 2.0 <= foot + 0.35 * s)).OrderBy(c => c.MinX).ToList();
            var cuts = new List<Cut>();
            var taken = new HashSet<Comp>();
            (Cut Of, int End) line = (null, 0);   // the extender's line drawn from a syllable, and where it ends
            (Cut Of, int Count, int End) dashes = (null, 0, 0);   // the hyphens' dashes after a syllable, and where the last ends
            // (an apostrophe over the letters, "schenk’", "an’s": wholly over the small letters, half a small letter
            // tall and more, and not round and full as an i's dot is (0.72 to 0.87 of its box inked on the development
            // set's scans, the apostrophes 0.53 to 0.60))
            var apostrophes = new List<double>();
            foreach (var c in ink)
                if (!inRows.Contains(c) && c.MaxY <= foot - 0.6 * small && c.MinY >= foot - 2.4 * small && c.H >= 0.5 * small && c.H <= 0.95 * small
                    && c.W >= 0.25 * small && c.W <= 0.7 * small && c.H >= c.W && c.N <= 0.68 * c.W * c.H
                    // (not a capital's arm broken off in a scan, "E u", "W o": over a letter risen as high as it; an
                    // apostrophe over a k's arm, "schenk’", rises a third of a small letter over its stem)
                    && !row.Any(o => o.MaxX >= c.MinX && o.MinX <= c.MaxX && o.MinY <= c.MinY + 0.15 * small))
                    apostrophes.Add(x0 + (c.MinX + c.MaxX + 1) / 2.0);
            foreach (var c in ink)
            {
                if (taken.Contains(c)) continue;
                // (an old print's extender drawn as dashes, "Meer _ _ _ _ .": two dashes and more from a syllable
                // with a stop after them, the syllable's, not a hyphen to the next)
                bool afterDashes = cuts.Count > 0 && dashes.Of == cuts[^1] && dashes.Count >= 2 && x0 + c.MinX >= dashes.End && x0 + c.MinX - dashes.End < 2 * s;
                var last = cuts.Count > 0 && (!cuts[^1].Hyphen || afterDashes) ? cuts[^1] : null;
                // (a stop, a "!" or a "?" past a syllable's letters is the syllable's: set after the extender's line
                // drawn from it, "Drohn ____ ?", "Thurm ____ , weit", or, in an old print, a space after it,
                // "schieht .", "du !", "Traum ?": then read with it, not placed by it)
                // (after the line by up to 1.7 spaces in one old print; nothing of the next word's within half a space after it)
                bool afterLine = last != null && (afterDashes || line.Of == last && x0 + c.MinX >= line.End && x0 + c.MinX - line.End < 2 * s)
                    && !ink.Any(o => o != c && o.H >= 0.5 * small && o.MinX > c.MaxX && o.MinX - c.MaxX < 0.5 * s);
                bool apart = !afterLine && last != null && x0 + c.MinX - last.R >= 0.5 * s && x0 + c.MinX - Math.Max(last.R, last.Tail) < 1.2 * s;
                if ((apart || afterLine) && Mark(c, all, foot, small, (x, y) => dark[x0 + x, y0 + y]) is var (mark, dot) && mark != '\0'
                    // (and ending the word: a word's first letter set after the last has no letter after it)
                    && !ink.Any(o => o != c && o != dot && o.H >= 0.5 * small && o.MinX > c.MinX && o.MinX - Math.Max(c.MaxX, dot.MaxX) < 0.5 * s))
                {
                    taken.Add(dot);
                    if (apart) last.Tail = Math.Max(last.Tail, x0 + Math.Max(c.MaxX, dot.MaxX) + 1);
                    last.Stop = mark;
                    last.StopAfterLine = afterLine && !afterDashes;
                    last.StopAfterDashes = afterDashes;

                    if (Verbose) res.Notes.Add($"lyrics mark '{mark}' at {x0 + c.MinX},{y0 + c.MinY}{(afterLine ? " after a line" : "")}");
                    continue;
                }
                // (a speck wholly under the foot, a little ink of the paper's between two rows: a comma hangs from the
                // foot, a letter's tail is one piece with it)
                if (c.MinY >= foot + 0.3 * small && c.H <= 0.8 * small) continue;
                bool thin = c.H <= 0.35 * s && c.W >= 1.5 * c.H;
                // (an extender's line stands on the foot, long: no syllable's, and no hyphen)
                if (thin && (c.W >= 2.5 * s || c.W >= 1.5 * s && c.MaxY >= foot - 0.1 * s))
                {
                    // (set off from its syllable by a space and more, "Haupt ______ , und": 1.3 to 2.9 spaces in one old print)
                    if (last != null && (x0 + c.MinX - Math.Max(last.R, last.Tail) < 4 * s || line.Of == last && x0 + c.MinX - line.End < 1.2 * s))
                        line = (last, x0 + c.MaxX + 1);
                    continue;
                }
                // (a hyphen, at the letters' middle or, in old prints, low, a short rule on the foot)
                // (a third of the way down and more, a third of a space wide and more, past the letters: not an
                // umlaut's dots or an i's over them, nor a bit of a serif broken off in a scan)
                if (thin && c.W >= 0.3 * s && c.MinY > top + 0.3 * (foot - top) && (cuts.Count == 0 || x0 + c.MinX >= cuts[^1].R - 1))
                {
                    if (cuts.Count > 0)
                    {
                        cuts[^1].Hyphen = true;   // (as far as it may be: set midway to a syllable far off)
                        dashes = (cuts[^1], dashes.Of == cuts[^1] ? dashes.Count + 1 : 1, x0 + c.MaxX + 1);
                    }
                    continue;
                }
                bool letter = inRows.Contains(c);
                // (a comma or a full stop: small, low, standing on the foot or hanging under it; a comma well under
                // it (or under it and tall, 0.13 to 0.23 of a small letter in old prints), a full stop round, not a piece
                // of a letter's leg broken off in a scan)
                // (or, an old print's short thick comma, "che ,", taller than a full stop and a little under the foot)
                char stop = c.W <= 0.75 * small && c.H <= 0.9 * small && c.MinY >= foot - 0.75 * small && c.MaxY >= foot - 0.2 * small
                    ? c.MaxY >= foot + 0.25 * small || c.MaxY >= foot + 0.12 * small && c.H >= 1.5 * c.W || c.MaxY >= foot + 0.12 * small && c.H >= 0.65 * small && c.H >= 1.2 * c.W ? ','
                    : c.H <= 0.6 * small && c.W <= 1.5 * c.H && c.H <= 1.5 * c.W ? '.' : '\0' : '\0';
                // (the dots of a dotted extender after a stop, "Mund, . . . .": the stop stays, the dots are no stops)
                // (after a full stop's shape the next may still be the stop: the first a speck of the paper's, "Fluss . !")
                if (stop != 0 && apart && last.Stop is ',' or '!' or '?') continue;
                if (Verbose && cuts.Count > 0 && c.W <= small && c.H <= 1.2 * small && c.MaxY >= foot - 0.3 * small)
                    res.Notes.Add($"lyrics speck at {x0 + c.MinX},{y0 + c.MinY} after {cuts[^1].L}-{cuts[^1].R}: gap {(x0 + c.MinX - cuts[^1].R) / s:F2} sp, w {c.W / small:F2} h {c.H / small:F2} top {(c.MinY - foot) / small:F2} bottom {(c.MaxY - foot) / small:F2} small, letter {letter}, stop '{stop}', apart {apart}, after a line {afterLine}");
                if (stop != 0 && (apart || afterLine))
                {
                    if (apart) last.Tail = Math.Max(last.Tail, x0 + c.MaxX + 1);
                    last.StopBig = apart && stop == '.' && c.W >= 0.35 * small && c.H >= 0.35 * small;
                    last.Stop = stop;
                    last.StopAfterLine = afterLine && !afterDashes;
                    last.StopAfterDashes = afterDashes;

                    continue;
                }
                if (cuts.Count > 0 && !cuts[^1].Hyphen && x0 + c.MinX - cuts[^1].R < 0.5 * s)
                {
                    // (the last piece of a syllable of two or more, past all its letters)
                    if (cuts[^1].Tail == 0) cuts[^1].Stop = stop != 0 && x0 + c.MinX >= cuts[^1].R - 1 ? stop : '\0';
                    cuts[^1].R = Math.Max(cuts[^1].R, x0 + c.MaxX + 1);
                }
                // (too small to be a letter, away from any: a speck, a dot of the paper's)
                else if (!letter) continue;
                else cuts.Add(new Cut { L = x0 + c.MinX, R = x0 + c.MaxX + 1, FirstH = c.H, Small = small, Foot = y0 + foot });
                cuts[^1].Ink.Add((x0 + c.MinX, x0 + c.MaxX + 1, y0 + c.MinY, y0 + c.MaxY + 1));
                if (clipped.TryGetValue(c, out var stem)) cuts[^1].Stems.Add(stem);
            }
            // (a row whose letters reach up to where the region starts: the OCR's cut, half a space over them,
            // would take in the staff's lowest line, the heads on it and the flags' tips)
            if (top < 0.5 * s) foreach (var c in cuts) c.Clear = y0;
            foreach (double a in apostrophes)
                if (cuts.LastOrDefault(c => c.L <= a) is { } of && a <= of.R + 0.1 * s) of.Apostrophes.Add(a);
            if (cuts.Count > 0 && Slant(res, cuts, y0 + top, y0 + foot) < Texts.MinSlant) rows.Add((y0 + top, y0 + foot, cuts));
        }
        rows.Sort((a, b) => a.Item2.CompareTo(b.Item2));
        return rows;
    }

    /// <summary>
    /// A "!" or a "?" (and its dot), or '\0': a stroke risen over the small letters as a capital is, ending
    /// over the foot, a dot under it standing on the foot; or, the ink run together in a scan, one piece
    /// down to the foot, narrowed to a neck over its dot. A "!" is a wedge; a "?" a hook, its curl crossed twice.
    /// </summary>
    static (char Mark, Comp Dot) Mark(Comp c, List<Comp> all, int foot, double small, Func<int, int, bool> ink)
    {
        if (c.MinY > foot - 1.2 * small || c.W > 1.1 * small) return ('\0', null);
        int Width(int y)
        {
            int l = -1, r = -1;
            for (int x = c.MinX; x <= c.MaxX; x++) if (ink(x, y)) { if (l < 0) l = x; r = x; }
            return l < 0 ? 0 : r - l + 1;
        }
        Comp dot;
        int stroke = c.MaxY;   // (the last row of the stroke, over its dot)
        if (c.MaxY <= foot - 0.15 * small && c.MaxY >= foot - 0.8 * small && c.W <= 0.9 * small)
        {
            dot = all.Where(d => d != c && d.H <= 0.6 * small && d.W <= 0.75 * small && d.MinY > c.MaxY - 1 && d.MaxY >= foot - 0.2 * small && d.MaxY <= foot + 0.3 * small
                    && d.Cx >= c.MinX - 0.15 * small && d.Cx <= c.MaxX + 0.15 * small)
                .MinBy(d => d.MinY - c.MaxY);
            if (dot == null) return ('\0', null);
        }
        else if (c.MaxY >= foot - 0.2 * small && c.MaxY <= foot + 0.3 * small && c.MinY <= foot - 1.4 * small)
        {
            // (the neck: a row 0.4 of the width or less, the dot under it no taller than a full stop, and wider)
            int neck = -1;
            for (int y = c.MaxY - (int)(0.15 * small); y >= c.MaxY - 0.6 * small; y--)
                if (Width(y) <= Math.Max(2, 0.4 * c.W)) { neck = y; break; }
            if (neck < 0) return ('\0', null);
            int dotW = Enumerable.Range(neck + 1, c.MaxY - neck).Max(Width);
            if (dotW < 1.3 * Math.Max(1, Width(neck)) || dotW > 0.75 * small) return ('\0', null);
            dot = c;
            stroke = neck - 1;
        }
        else return ('\0', null);
        // (a row of the hook's crossed twice: its curl)
        int twice = 0;
        for (int y = c.MinY; y <= stroke; y++)
        {
            int runs = 0;
            bool was = false;
            for (int x = c.MinX; x <= c.MaxX; x++) { bool on = ink(x, y); if (on && !was) runs++; was = on; }
            if (runs >= 2) twice++;
        }
        return (twice >= 0.15 * (stroke - c.MinY + 1) ? '?' : '!', dot);
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
            // (no syllable starts with a comma: "ten,um" set tight is "ten," and "um", not "ten" and ",um")
            // (or a little under the foot and tall, "ren ,", 0.13 under and 1.8 times as tall as wide in one old print)
            bool Comma((int L, int R, int T, int B) i) => i.T >= c.Foot - 0.75 * c.Small && i.B - i.T <= 0.9 * c.Small && i.R - i.L <= 0.75 * c.Small
                && (i.B - 1 >= c.Foot + 0.15 * c.Small || i.B - 1 >= c.Foot + 0.12 * c.Small && i.B - i.T >= 1.5 * (i.R - i.L));
            var bounds = Bounds(ink, s).Where(b => !Comma(ink[b.At])).ToList();
            // (and a syllable has a letter: not a comma and a speck of the paper's over it, "Ruh ˙," -- "Ruh" and "˙,")
            var letter = ink.Select(i => !Comma(i) && Math.Max(i.R - i.L, i.B - i.T) >= 0.3 * c.Small).ToList();
            var at = held.Count(h => !h) >= 2 ? Partition(within, held, bounds, letter, c.L, c.R, ink.Count, s) : null;
            // (and a comma ends its syllable: "sche,Fluss" set tight, parted after the F where the syllables
            // stand on their notes best, parts after the comma -- set so tight, neither stands on its note)
            // (a comma, not a speck of the paper's under a word: 0.64 to 0.9 of a small letter tall, the speck under
            // "heiss" 0.21)
            if (at != null)
                foreach (var m in bounds.Where(b => Comma(ink[b.At - 1]) && ink[b.At - 1].B - ink[b.At - 1].T >= 0.4 * c.Small).Select(b => b.At))
                {
                    int k = at.FindIndex(a => a >= m);
                    if (k >= 0 && at[k] > m && (k == 0 ? 0 : at[k - 1]) < m) at[k] = m;
                }
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
                parted.Add(new Cut { L = l, R = r, Ink = some, Foreign = foreign, Stems = c.Stems, Clear = c.Clear, Hyphen = to == ink.Count ? c.Hyphen : true, Tail = to == ink.Count ? c.Tail : 0, Stop = to == ink.Count ? c.Stop : Comma(some[^1]) ? ',' : '\0', StopAfterLine = to == ink.Count && c.StopAfterLine, StopAfterDashes = to == ink.Count && c.StopAfterDashes, FirstH = some[0].B - some[0].T, Small = c.Small, Foot = c.Foot, Before = from > 0 ? parted[^1] : null, Apostrophes = c.Apostrophes.Where(a => a >= l && a <= r).ToList() });
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
    /// No syllable of one piece ("T" and "rä") or without a <paramref name="letter"/>.
    /// </summary>
    static List<int> Partition(List<double> heads, List<bool> held, List<(int At, double Mid)> bounds, List<bool> letter, double left, double right, int pieces, double s)
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
                if (to - piece < 2 || !letter.Skip(piece).Take(to - piece).Any(q => q)) continue;
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
        // (the row's language from the whole row: one syllable alone, "Re" read "せ", cannot say)
        bool japanese = cuts.Count(c => c.Text != null && Regex.IsMatch(c.Text, $"[{Header.Cjk}]")) * 2 > cuts.Count;
        // (the Japanese engine passes over one short word alone: three in a row, as Texts does)
        // (and, those it still passes over, "al", at other sizes)
        foreach (double zoom in new[] { 1.0, 1.5, 0.7 })
            foreach (var c in cuts.Where(c => c.Text == null).ToList())
                foreach (var (cut, text) in Strip(page, res, [c], top, foot, 3, zoom, null, japanese)) cut.Text = text;
        // (a letter and a comma, "a,", the engines pass over at any size: after a word of the row, as a line, they read it)
        var anchor = cuts.Where(c => c.Text != null && Regex.IsMatch(c.Text, @"^\p{L}{2,}$")).MaxBy(c => c.R - c.L);
        if (anchor != null)
            foreach (var c in cuts.Where(c => c.Text == null).ToList())
                // (taken only if it has a word's form or a verse number's: a slur over the letters comes back "len--=t====")
                foreach (var (cut, text) in Strip(page, res, [c], top, foot, 1, 1, anchor, japanese))
                    if (Regex.IsMatch(text, @"^([\p{L}'’]+\p{P}{0,3}|\d+[.)・]?)$")) cut.Text = text;
        foreach (var c in cuts) c.Text = Cased(c, c.Text);
        // (a comma the ink shows that the OCR, reading a syllable alone, dropped; a full stop only after the
        // extender's line or set apart as big as a printed one: elsewhere a speck after a letter has its shape,
        // "mich." 42 times to 18 stops found on the development set's scans, and on a row's last syllable it
        // came to +6 there but -2 held out. The old prints' stops set apart, "ruht .", are 0.37 to 0.57 of a
        // small letter wide and tall on the development set's scans, the specks taken for them 0.25 and less)

        for (int i = 0; i < cuts.Count; i++)
        {
            var c = cuts[i];
            // (after dashes, "Meer _ _ _ _ . Die": an extender's if the next syllable starts a sentence, its
            // stop the syllable's and no hyphen; else the dashes a hyphen's, "hau _ _ _ sen", the stop a bit of one)
            if (c.StopAfterDashes)
            {
                var next = i + 1 < cuts.Count ? cuts[i + 1].Text : null;
                if (next != null && char.IsUpper(next[0])) { c.Hyphen = false; c.StopAfterLine = true; }
                else c.Stop = '\0';
            }
            if (c.Text == null || !char.IsLetter(c.Text[^1])) continue;
            if (c.Stop == ',' || c.Stop == '.' && (c.StopAfterLine || c.StopBig)) c.Text += c.Stop;
        }
        // (a "!" or a "?" set apart, which the OCR reads as nothing or as a letter of its own, "Traum l")
        foreach (var c in cuts.Where(c => c.Stop is '!' or '?' && c.Text != null))
        {
            if (Verbose) res.Notes.Add($"lyrics mark '{c.Stop}' read \"{c.Text}\"");
            if (char.IsLetter(c.Text[^1])) c.Text += c.Stop;
        }
        // (an apostrophe the ink shows that the OCR dropped, both engines, "schenk", "Freud", "ans": after the last
        // letter, or before it, "an’s", "Reih’n")
        foreach (var c in cuts.Where(c => c.Text != null && c.Apostrophes.Count > 0 && !Regex.IsMatch(c.Text, "['’‘`ʼ]")))
        {
            double a = c.Apostrophes[^1];
            int after = c.Ink.Count(i => (i.L + i.R) / 2.0 > a + 1 && i.B - i.T >= 0.5 * c.Small && i.B - 1 >= c.Foot - 0.3 * c.Small);
            var m = Regex.Match(c.Text, @"^(.*\p{L})(\p{L})(\p{P}*)$");
            // (a "!" or a "?" read: its stroke is no apostrophe)
            if (!m.Success || after > 1 || Regex.IsMatch(m.Groups[3].Value, "[!?]")) continue;
            if (Verbose) res.Notes.Add($"lyrics apostrophe at {a:F0} in \"{c.Text}\", {after} letters after it");
            c.Text = after == 0 ? m.Groups[1].Value + m.Groups[2].Value + "'" + m.Groups[3].Value : m.Groups[1].Value + "'" + m.Groups[2].Value + m.Groups[3].Value;
        }
        // (a capital I after a letter of a word is its small l: "AI", "Al")
        foreach (var c in cuts.Where(c => c.Text != null)) c.Text = Regex.Replace(c.Text, @"(?<=^\p{Lu}|\p{Ll})I(?=\p{Ll}|\p{P}|$)", "l");
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
        // (an engine without umlauts reads the ü's two dots and bowl as two strokes: "blii", "sLI", "sII")
        static string Strokes(string t) => Regex.Replace(t, "ii|II|LI|Li|lI|Il|li", "u");
        if (Plain(native) && Plain(other))
            return (string.Equals(Fold(native), Fold(other), StringComparison.Ordinal) || native.Contains('ü') && string.Equals(Fold(native), Strokes(other), StringComparison.Ordinal)) && native != Fold(native) ? native : other;
        return Plain(other) ? other : Plain(native) ? native : other ?? native;
    }

    static List<(Cut, string)> Strip(Bitmap page, PageResult res, List<Cut> cuts, int top, int foot, int times, double zoom = 1, Cut anchor = null, bool? rowJapanese = null)
    {
        double s = res.S;
        var dark = res.Dark;
        int pad = (int)(0.5 * s);
        int rowTop = top - pad, rowBottom = foot + (int)(1.0 * s);
        (RectangleF Box, Cut Cut) Source(Cut c)
        {
            var a = dark.ToSource(c.L - 0.1 * s, rowTop);
            var b = dark.ToSource(Math.Max(c.R, c.Tail) + 0.1 * s, rowBottom);
            return (RectangleF.FromLTRB((float)Math.Min(a.X, b.X), (float)Math.Min(a.Y, b.Y), (float)Math.Max(a.X, b.X), (float)Math.Max(a.Y, b.Y)), c);
        }
        var boxes = cuts.Select(Source).ToList();
        float height = boxes.Max(b => b.Box.Height);
        // (the letters about 48 pixels tall, as the OCR reads best; the strip within its widest)
        double k = zoom * 48.0 / Math.Max(1, height * 0.6);
        float gap = 2.2f * height;
        var spans = new List<(float L, float R, Cut Cut)>();
        var pastes = new List<(float L, RectangleF Box, Cut Cut)>();
        float at = gap;
        foreach (var (box, cut) in boxes)
            for (int t = 0; t < times; t++)
            {
                float from = at;
                if (anchor != null)
                {
                    // (the row's word just before it, a word's gap off: the same line to the OCR)
                    var before = Source(anchor).Box;
                    pastes.Add((at, before, anchor));
                    at += before.Width + 0.35f * height;
                }
                pastes.Add((at, box, cut));
                at += box.Width;
                spans.Add((from, at, cut));
                at += gap;
            }
        int W = (int)Math.Ceiling(at), H = (int)Math.Ceiling(height + 2 * pad);
        using var strip = new Bitmap(Math.Max(1, W), Math.Max(1, H), PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(strip))
        {
            g.Clear(Color.White);
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            foreach (var (l, box, cut) in pastes)
            {
                g.DrawImage(page, new RectangleF(l, pad, box.Width, box.Height), box, GraphicsUnit.Pixel);
                var clear = cut.Clear > 0 ? new[] { (L: (int)(cut.L - s), R: (int)(Math.Max(cut.R, cut.Tail) + s), T: (int)(rowTop - s), B: cut.Clear) } : [];
                foreach (var f in cut.Foreign.Concat(cut.Stems).Concat(clear))
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
                var words = string.Join(" ", its.Select(q => q.Text)).Split(' ', StringSplitOptions.RemoveEmptyEntries);
                // (read after a word: what follows it, and nothing if the two were read as one)
                if (anchor != null && words.Length < 2) continue;
                // (several places read as one line: the first word; a few letters within this place alone
                // are pieces of one syllable, "be n", a syllable having no space)
                bool pieces = words.Sum(w => w.Length) <= 6 && Regex.IsMatch(string.Concat(words), @"^\p{L}+\p{P}?$") && its.All(q => q.Box.Right - q.Box.Left <= r - l + 0.5 * gap);
                string text = Clean((anchor != null ? words[^1] : pieces ? string.Concat(words) : words.FirstOrDefault()) ?? "");
                if (text.Length > 0) return text;
            }
            return null;
        }
        var read = spans.GroupBy(q => q.Cut).Select(g => (Cut: g.Key, Native: Reading(lines, g), Other: Reading(theirs, g))).ToList();
        bool japanese = rowJapanese ?? read.Count(q => q.Native != null && Regex.IsMatch(q.Native, $"[{Header.Cjk}]")) * 2 > read.Count;
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
