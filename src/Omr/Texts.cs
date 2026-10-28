using System.Drawing;
using System.Drawing.Imaging;
using System.Text.RegularExpressions;

namespace Omr;

/// <summary>
/// Words of expression over or under a staff (M3): "dolce", "rit.", "a tempo", "poco rit.", read with
/// Windows' OCR as the tempo is (Tempo) and put on the note they start at, as the .lys writes them:
/// @text("dolce"), @text("rit.").up.
///
/// Where: Lily# sets a text script in italic at the paper's text size (2.2 spaces), its left end at
/// its note's head, under the staff (a voice's .up over it), clear of the notes, stems and beams. The
/// bands a third of a space to eight spaces over and under each staff are searched for rows of
/// letters (pieces of ink a letter's height, no head and nothing the detector marked in them but a
/// dynamic's letter within a word); a row that leans as italic does (<see cref="Slant"/>: a song's
/// words and a printed bar number stand upright) is cut out, sheared back upright and read by itself,
/// larger, and set three times in a row (the Japanese engine passes over one short word alone); it is
/// a text when a reading is one of the words of expression the vocabulary knows (<see cref="Phrases"/>,
/// <see cref="Match"/>). Whose: the staff with a head at the row's left end, else within a system the
/// one above, between two systems the nearer. A dynamic after "sempre" is the text's ("sempre p"), and
/// a dynamic the detector saw in the words ("mp" in "sempre") is taken off the note it was put on.
/// </summary>
static class Texts
{
    public static bool Verbose;
    /// <summary>Dev: a folder every cut read is saved in (--texts-dump).</summary>
    public static string Dump;
    static int dumped;
    /// <summary>Dev: the cuts read and the time spent cleaning them (Stopwatch ticks).</summary>
    public static long CleanTicks; public static int Cuts;

    // the words of expression a text may say, as the .lys spells them: Words, and each of Bases after
    // sempre, poco, molto, più, meno and ben ("sempre legato", "poco cresc.") -- Phrases, below them (the
    // static fields are set in the order written)
    static readonly string[] Bases =
    {
        // (no dynamic: "poco p" is "POCO r", the start of a "poco rit." half read; "sempre p" is among Words)
        "legato", "staccato", "marcato", "espress.", "espressivo", "dolce", "cantabile", "tenuto", "cresc.", "dim.", "rit.", "rall.", "accel.",
    };
    static readonly string[] Words =
    {
        "dolce", "dolciss.", "dolcissimo", "espress.", "espressivo", "cantabile", "legato", "leggiero", "marcato", "tranquillo",
        "grazioso", "scherzando", "agitato", "sostenuto", "sost.", "ten.", "rubato", "simile", "sempre", "semplice", "sotto voce", "mezza voce",
        "rit.", "ritard.", "ritardando", "riten.", "ritenuto", "rall.", "rallentando", "accel.", "accelerando", "allargando", "string.", "stringendo",
        "poco rit.", "molto rit.", "poco a poco", "a tempo", "con moto", "con fuoco", "con brio", "meno mosso", "più mosso",
        "cresc.", "crescendo", "poco cresc.", "dim.", "diminuendo", "poco dim.", "decresc.", "decrescendo", "morendo", "smorzando", "calando", "perdendosi",
        "sempre p", "sempre pp", "sempre f", "sub. p", "subito p",
        "leise", "zart", "innig", "ruhig", "breit", "mit Ausdruck", "ausdrucksvoll", "zurückhaltend", "etwas langsamer",
        "pizz.", "arco", "l.h.", "r.h.", "m.s.", "m.d.",
    };
    static readonly string[] Phrases = Words.Concat(new[] { "sempre", "poco", "molto", "più", "meno", "ben" }.SelectMany(m => Bases.Select(b => m + " " + b)))
        .Concat(["poco a poco cresc.", "poco a poco dim.", "poco a poco accel.", "poco a poco rit."]).Distinct().ToArray();

    /// <summary>The texts the OCR reads on a page, put on their notes; the page's tempo and other marks' boxes are no text's.</summary>
    public static void Apply(string path, PageResult res, IReadOnlyList<Tempo.Mark> marks)
    {
        if (res.Systems.Count == 0) return;
        double s = res.S;
        var dark = res.Dark;
        // the text spanners the page before ran out past its last system: their dashes over the part's
        // staff in this page's first system, as over a next system's (no words before them)
        foreach (var o in Continuation.Going(res, "span"))
        {
            if (!Continuation.First(res, o.Part, out var sys, out var on) || Continued(res, sys, on) is not { } cont) continue;
            var end = SpanEnd(res, on, new DashLine(cont.Count, cont[0], cont[^1], sys), out bool off);
            if (Verbose) res.Notes.Add($"span line from the page before: {cont.Count} dashes to {cont[^1].Right:F0}, " + (end == null ? "no end" : $"ending at the note at {end.X:F0},{end.Y:F0}") + (off ? ", on past the page's end" : ""));
            if (off) o.On(res, end);
            else if (end != null) o.Close(res, end);
        }
        var taken = (marks ?? []).Select(m => m.Box).ToList();
        var rows = new List<(RectangleF Dark, Staff St)>();
        try
        {
            // (the page as loaded only when there is an italic row to read: loading it costs a third of a second)
            Bitmap page = null;
            using var _ = new Disposer(() => page?.Dispose());
            foreach (var sys in res.Systems.Where(x => x.Measures > 0 && x.Staves.Count > 0))
            {
                foreach (var st in sys.Staves)
                    foreach (bool over in new[] { true, false })
                    {
                        int y0 = (int)(over ? st.Top - 8 * s : st.Bottom + 0.3 * s), y1 = (int)(over ? st.Top - 0.3 * s : st.Bottom + 8 * s);
                        // (to twelve spaces past the staff's end: a text on the system's last note runs out over the margin)
                        foreach (var row in Rows(res, st.Left - (int)(2 * s), y0, st.Right + (int)(12 * s), y1))
                            if (!rows.Any(r => Overlap(r.Dark, row) > 0.5)) rows.Add((row, st));
                    }
            }
            var dynamics = DynamicRuns(res);
            foreach (var (row, near) in rows)
            {
                var a = dark.ToSource(row.Left, row.Top);
                var b = dark.ToSource(row.Right, row.Bottom);
                var box = RectangleF.FromLTRB((float)Math.Min(a.X, b.X), (float)Math.Min(a.Y, b.Y), (float)Math.Max(a.X, b.X), (float)Math.Max(a.Y, b.Y));
                if (taken.Any(t => Overlap(t, box) > 0.3))
                {
                    if (Verbose) Console.WriteLine($"  text row s{near.System + 1} at {box.X:F0},{box.Y:F0} {box.Width:F0}x{box.Height:F0}: a mark's (" + string.Join("; ", (marks ?? []).Where(m => Overlap(m.Box, box) > 0.3).Select(m => m.ToString())) + ")");
                    continue;
                }
                // (italic only: Lily# sets a text script in italic, as engravers do; a song's words and a
                // printed bar number stand upright, and are not read)
                double slant = Slant(dark, row);
                if (slant < MinSlant)
                {
                    if (Verbose) Console.WriteLine($"  text row s{near.System + 1} at {box.X:F0},{box.Y:F0} {box.Width:F0}x{box.Height:F0}: upright ({slant:F2})");
                    continue;
                }
                page ??= new Bitmap(path);
                string phrase = Read(page, box, out string seen);
                if (Verbose) Console.WriteLine($"  text row s{near.System + 1} at {box.X:F0},{box.Y:F0} {box.Width:F0}x{box.Height:F0}: slant {slant:F2} {seen} -> {phrase ?? "-"}");
                if (phrase == null) continue;
                // ("sempre p": the dynamic after the words is the text's)
                var dyn = phrase is "sempre" or "sub." or "subito"
                    ? dynamics.FirstOrDefault(d => d.Word != null && d.Letters[0].X - row.Right > -0.3 * s && d.Letters[0].X - row.Right < 2 * s && Math.Abs(d.Letters[0].Y - (row.Top + row.Bottom) / 2) < s)
                    : default;
                if (dyn.Word != null) phrase += " " + dyn.Word;
                // whose: of the staves above and below it (of the page's systems over its x), the one with a
                // head at the row's left end (Lily# sets a text's left end at its note's head); with both
                // or neither, within a system the one above (a staff's texts stand under it, a voice's .up
                // over it: between a song's piano staves, the right hand's), between two systems the one
                // whose edge it stands nearer (a voice's .up over the next system's first staff)
                double cy = (row.Top + row.Bottom) / 2;
                var staves = res.Systems.Where(q => q.Measures > 0 && row.Left > q.Bars[0] - 3 * s && row.Left < q.Bars[^1]).SelectMany(q => q.Staves).ToList();
                var aboveSt = staves.Where(x => x.Bottom < cy).OrderByDescending(x => x.Bottom).FirstOrDefault();
                var belowSt = staves.Where(x => x.Top > cy).OrderBy(x => x.Top).FirstOrDefault();
                Head At(Staff x) => x == null ? null : res.Sym.Heads.Where(h => h.St == x && !h.Grace).OrderBy(h => Math.Abs(h.L - row.Left)).FirstOrDefault();
                bool Fits(Staff x) => At(x) is { } h && Math.Abs(h.L - row.Left) <= 0.8 * s;
                var st = aboveSt == null ? belowSt : belowSt == null ? aboveSt
                    : Fits(belowSt) != Fits(aboveSt) ? (Fits(belowSt) ? belowSt : aboveSt)
                    : aboveSt.System == belowSt.System ? aboveSt
                    // (between two systems with a head at its left end over both: the nearer head, by a fifth
                    // of a space and more -- an "accel." raised over the next system's beams stands nearer the
                    // system above)
                    : Fits(aboveSt) && Math.Abs(Math.Abs(At(aboveSt).L - row.Left) - Math.Abs(At(belowSt).L - row.Left)) > 0.2 * s
                        ? (Math.Abs(At(aboveSt).L - row.Left) < Math.Abs(At(belowSt).L - row.Left) ? aboveSt : belowSt)
                    : row.Top - aboveSt.Bottom <= belowSt.Top - row.Bottom ? aboveSt : belowSt;
                // a text spanner's words, a dashed line after them: the staff under it (Lily# sets a span
                // over its staff, between a grand staff's two over the lower), when the line ends on a note
                var dashes = Dashes(res, row);
                Head last = null;
                bool off = false;
                if (dashes != null && (belowSt != null && belowSt.Top - row.Bottom < 8 * s ? belowSt : st) is { } over)
                {
                    last = SpanEnd(res, over, dashes.Value, out off);
                    // (one run out past the page with no end here goes on over its staff only when that is
                    // the words' own: else it stays the text it was)
                    if (last == null && over != st) off = false;
                    if (last != null || off) st = over;
                }
                if (st == null) continue;
                // (and no dynamic of a note's, that one nor the detector's letters within the words, the p of
                // "sempre" at 0.9: Expressions put each on the staff's note nearest it)
                var inside = RectangleF.Inflate(row, (float)(0.2 * s), (float)(0.2 * s));
                // (one within the row only when the words spell it: the "mp" of "sempre", not the "sf" before "dim.")
                foreach (var run in dynamics.Where(d => d.Word != null && (d.Letters == dyn.Letters || (d.Letters.All(t => inside.Contains((float)t.X, (float)t.Y)) && Key(phrase).Contains(d.Word)))))
                {
                    var had = res.Bars.Where(kv => kv.Key.Part == st.Part).SelectMany(kv => kv.Value)
                        .Concat(res.Voices.Where(kv => kv.Key.Part == st.Part).SelectMany(kv => kv.Value.SelectMany(x => x)))
                        .Where(e => e.Dynamic != null && e.Dynamic.Split('.')[0] == run.Word && Math.Abs(e.X - run.Letters[0].X) < 3 * s).ToList();
                    if (had.Count == 0) continue;
                    double x = had.MinBy(e => Math.Abs(e.X - run.Letters[0].X)).X;
                    foreach (var e in had.Where(e => Math.Abs(e.X - x) < 0.5)) e.Dynamic = null;
                    if (Verbose) res.Notes.Add($"text \"{phrase}\": the dynamic {run.Word} at {run.Letters[0].X:F0},{run.Letters[0].Y:F0} is the text's, off the note at {x:F0}");
                }
                bool up = row.Bottom < st.Top;
                // its note: the staff's head whose left edge stands nearest the row's left end, within two spaces;
                // or the first to its right within five (the quartet's "arco", set under its chord's "ff", starts
                // 4.6 spaces before the chord)
                var head = At(st);
                if (head != null && Math.Abs(head.L - row.Left) > 2 * s)
                    head = res.Sym.Heads.Where(h => h.St == st && !h.Grace && h.L - row.Left > -2 * s && h.L - row.Left <= 5 * s).OrderBy(h => h.L).FirstOrDefault() ?? head;
                if (head == null || head.L - row.Left > 5 * s || row.Left - head.L > 2 * s)
                {
                    if (Verbose) res.Notes.Add($"text \"{phrase}\" at {row.X:F0},{row.Y:F0}: no note (over {belowSt?.Part}/s{belowSt?.System + 1}, under {aboveSt?.Part}/s{aboveSt?.System + 1}{(dashes is { } dz ? $", dashes {dz.Count} from {dz.First.X:F0},{dz.First.Y:F0} {dz.First.Width:F0}x{dz.First.Height:F0}, the row to {row.Right:F0}, {row.Top:F0}-{row.Bottom:F0}" : "")})" + (head == null ? "" : $" (part {st.Part}'s nearest head's left {(head.L - row.Left) / s:F1} spaces off, at {head.X:F0},{head.Y:F0})"));
                    continue;
                }
                var evs = Articulations.Events(res, head, s);
                if (Verbose) res.Notes.Add($"text \"{phrase}\" at {row.X:F0},{row.Y:F0}: part {st.Part}, " + (evs.Count == 0 ? "no event" : $"the note at {evs[0].X:F0}")
                                          + (dashes is { } dd ? $", dashes {dd.Count} from {dd.First.X:F0},{dd.First.Y:F0} ({dd.First.Width:F0}x{dd.First.Height:F0}; the row to {row.Right:F0}, {row.Top:F0}-{row.Bottom:F0}) to {dd.Last.Right:F0}" + (last == null ? ", no end" : $", ending at the note at {last.X:F0},{last.Y:F0}") : "")
                                          + (off ? ", on past the page's end" : ""));
                // (a span's words and its end, the note on whose events and which page's)
                string text = $"text({Header.Quote(phrase)})" + (up ? ".up" : "");
                // (Lily#'s own spellings for its three: @rit is @textSpan("rit."))
                string span = phrase switch { "rit." => "rit", "accel." => "accel", "rall." => "rall", _ => $"textSpan({Header.Quote(phrase)})" };
                void Put(PageResult at, Head end)
                {
                    Mark(evs, end == null ? text : span);
                    if (end != null) Mark(Articulations.Events(at, end, at.S), "!" + span.Split('(')[0]);
                }
                // (a line run out past the page's last system: on the next page, or as it is without one)
                if (off)
                {
                    var here = res;
                    var fallback = last;
                    res.Open.Add(new Continuation.Open("span", st.Part, head.X, (at, end) => { if (end != null) Put(at, end); else Put(here, fallback); }));
                }
                else Put(res, last);
            }
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            Console.WriteLine($"texts: no OCR ({(string.IsNullOrWhiteSpace(e.Message) ? $"0x{e.HResult:X8}" : e.Message.Trim())})");
        }
    }

    sealed class Disposer(Action done) : IDisposable { public void Dispose() => done(); }

    // a mark on a note's events, once; a span's end before anything else on it (the next span may
    // start on the note this one ends on, and Lily# reads the marks on a note left to right)
    static void Mark(List<Ev> evs, string name)
    {
        foreach (var e in evs)
        {
            e.Articulations ??= new List<string>();
            if (e.Articulations.Contains(name)) continue;
            if (name.StartsWith('!')) e.Articulations.Insert(0, name); else e.Articulations.Add(name);
        }
    }

    /// <summary>A text spanner's dashed line: how many dashes, the first and the last (the working image's), its system.</summary>
    internal readonly record struct DashLine(int Count, RectangleF First, RectangleF Last, SystemInfo Sys);

    /// <summary>
    /// The dashed line after a row of words, or null: Lily# runs a text spanner's line from the words'
    /// advance to its last note, dashes 0.6 spaces long every 3 (dash-period 3, dash-fraction 0.2), a
    /// tenth of a space thick, level with the words' baseline. The first dash within a space and a
    /// half of the row's end, the next each 3 spaces on (a quarter of a space either way) and level
    /// with it; the last may be cut short where the line ends.
    /// </summary>
    static DashLine? Dashes(PageResult res, RectangleF row)
    {
        double s = res.S;
        // (the system under the words: Lily# sets a span over its staff; between a grand staff's two, inside its system)
        var sys = res.Systems.Where(q => q.Measures > 0 && q.Staves.Count > 0 && row.Left > q.Bars[0] - 3 * s && row.Left < q.Bars[^1]
                                         && row.Bottom < q.Staves[^1].Bottom && q.Staves[0].Top - row.Bottom < 12 * s)
                             .MinBy(q => Math.Max(0, q.Staves[0].Top - row.Bottom));
        if (sys == null) return null;
        var chain = Chain(res, (int)(row.Right - 0.3 * s), (int)(row.Top - 0.3 * s), (int)(sys.Bars[^1] + 1.5 * s), (int)(row.Bottom + 0.3 * s),
                          left => left - row.Right <= 1.5 * s && left - row.Right >= -0.1 * s,
                          // (level with the words' baseline: in the lower half of the row, a descender's below it)
                          cy => cy >= row.Top + 0.4 * row.Height && cy <= row.Bottom + 0.2 * s);
        // (words at the system's end: their line goes on in the next system only)
        if (chain == null) return row.Right > sys.Bars[^1] - 3.3 * s ? new DashLine(0, row, row, sys) : null;
        return new DashLine(chain.Count, chain[0], chain[^1], sys);
    }

    // a chain of dashes in the box (the working image), its first one where `starts` says of its left end (and `level` of its height), each next
    // 3 spaces on; null for none
    static List<RectangleF> Chain(PageResult res, int x0, int y0, int x1, int y1, Func<int, bool> starts, Func<double, bool> level = null)
    {
        double s = res.S;
        var dark = res.Dark;
        x0 = Math.Max(0, x0); y0 = Math.Max(0, y0); x1 = Math.Min(dark.W - 1, x1); y1 = Math.Min(dark.H - 1, y1);
        int w = x1 - x0 + 1, h = y1 - y0 + 1;
        if (w < 2 || h < 2) return null;
        // (the ink of flat strokes only: a pixel in a row's run of a quarter of a space to nine tenths, or
        // in a column's run of a third of a space at most. A stem the line crosses is cut away above and
        // below it, a head and a beam are too long)
        var ink = new bool[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++) ink[y * w + x] = dark[x0 + x, y0 + y];
        var keep = new bool[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w;)
            {
                if (!ink[y * w + x]) { x++; continue; }
                int e = x;
                while (e < w && ink[y * w + e]) e++;
                if (e - x >= 0.25 * s && e - x <= 0.9 * s) for (int t = x; t < e; t++) keep[y * w + t] = true;
                x = e;
            }
        for (int x = 0; x < w; x++)
            for (int y = 0; y < h;)
            {
                if (!ink[y * w + x]) { y++; continue; }
                int e = y;
                while (e < h && ink[e * w + x]) e++;
                if (e - y <= 0.3 * s) for (int t = y; t < e; t++) keep[t * w + x] |= true;
                y = e;
            }
        var (_, comps) = Components.Label(w, h, i => keep[i], eight: true);
        // (a dash: flat, a third of a space high at most, not touching the box's top or bottom; a cut
        // last one down to a tenth of a space long)
        var flat = comps.Skip(1).Where(c => c.H <= 0.3 * s && c.W >= 0.1 * s && c.W <= 0.8 * s && c.W >= 2 * c.H && c.MinY > 0 && c.MaxY < h - 1)
                        .OrderBy(c => c.MinX).ToList();
        // (the longest chain from any first: a flat piece of a clef's curl before a continued line's first
        // dash is a chain of one, gen-span's piano003)
        List<Comp> best = null;
        foreach (var first in flat.Where(c => c.W >= 0.35 * s && starts(x0 + c.MinX) && (level == null || level(y0 + c.Cy))))
        {
            var chain = new List<Comp> { first };
            while (true)
            {
                var prev = chain[^1];
                var next = flat.Where(c => Math.Abs(c.MinX - prev.MinX - 3 * s) <= 0.3 * s && Math.Abs(c.Cy - prev.Cy) <= 0.25 * s).OrderBy(c => Math.Abs(c.Cy - prev.Cy)).FirstOrDefault();
                // (a cut dash only at the end)
                if (next == null || chain.Count > 1 && chain[^1].W < 0.35 * s) break;
                chain.Add(next);
            }
            if (best == null || chain.Count > best.Count) best = chain;
        }
        return best?.Select(c => new RectangleF(x0 + c.MinX, y0 + c.MinY, c.W, c.H)).ToList();
    }

    /// <summary>
    /// The note a text spanner ends on (its @!rit): Lily# ends the line a quarter of a space before
    /// that note's column, so the note's head stands from three quarters of a space past the last
    /// dash's start (a whole last dash) to three and a quarter (the gap before the next), or just past
    /// a cut one. A line run out to the system's end goes on in the next system over the same part's
    /// staff, from its first measure, without the words: its end is read there. One run out past the
    /// page's last system (off) may go on on the next page: what it returns is its end should none.
    /// </summary>
    static Head SpanEnd(PageResult res, Staff st, DashLine line, out bool off)
    {
        double s = res.S;
        var last = line.Last;
        var sys = line.Sys;
        off = false;
        if (line.Count == 0 && sys.Bars[^1] - last.Right >= 3.3 * s) return null;
        if (sys.Bars[^1] - last.Right < 3.3 * s && Continuation.Next(res, sys, st.Part, out var next, out var on))
        {
            var cont = Continued(res, next, on);
            // (words at the end with no dash after them: none goes on)
            if (cont == null) return line.Count == 0 ? null : Continuation.LastHead(res, st, sys);
            st = on;
            sys = next;
            last = cont[^1];
            if (next.Bars[^1] - last.Right < 3.3 * s)
            {
                off = Continuation.LastOnPage(res, sys);
                return Continuation.LastHead(res, st, sys);
            }
        }
        else if (sys.Bars[^1] - last.Right < 3.3 * s) off = Continuation.LastOnPage(res, sys);
        var heads = res.Sym.Heads.Where(h => h.St == st && !h.Grace).ToList();
        bool cut = last.Width < 0.5 * s;
        var end = cut
            ? heads.Where(h => h.L > last.Left).MinBy(h => Math.Abs(h.L - (last.Right + 0.25 * s)))
            : heads.Where(h => h.L >= last.Right + 0.1 * s && h.L <= last.Left + 3.4 * s).MinBy(h => h.L);
        return end ?? heads.Where(h => h.L > last.Right).MinBy(h => h.L);
    }

    // a text spanner's continuation over a system's staff, its dashes; null for none. Lily# starts it at
    // the first measure, so its first dash stands before the staff's first head, or half a space past it at most
    static List<RectangleF> Continued(PageResult res, SystemInfo next, Staff on)
    {
        double s = res.S;
        double lead = res.Sym.Heads.Where(h => h.St == on && !h.Grace).Select(h => h.L + 0.5 * s).DefaultIfEmpty(next.Bars.Count > 1 ? next.Bars[1] : next.Bars[0] + 15 * s).Min();
        var cont = Chain(res, (int)(next.Bars[0] - s), (int)(on.Top - 8 * s), (int)(next.Bars[^1] + 1.5 * s), (int)(on.Top - 0.2 * s),
                         left => left < lead);
        if (Verbose) res.Notes.Add($"span line on into system {next.Index + 1} (staff top {on.Top:F0}, its first head to {lead:F0}): " + (cont == null ? "no dashes" : $"{cont.Count} dashes, {cont[0].X:F0},{cont[0].Y:F0} to {cont[^1].Right:F0}"));
        return cont;
    }

    /// <summary>The slant a row of letters is read as italic from (its strokes' lean, x per y).</summary>
    public static double MinSlant = 0.1;

    /// <summary>
    /// How far a row's letters lean (x per y, rightwards up): the shear, of -0.1 to 0.4 in steps of a
    /// twentieth, that stands its strokes upright, the columns of the sheared ink most heaped (the sum
    /// of their squared counts). Lily#'s italic leans about 0.2; upright print 0.
    /// </summary>
    static double Slant(Img dark, RectangleF row)
    {
        int x0 = (int)row.Left, x1 = (int)row.Right, y0 = (int)row.Top, y1 = (int)row.Bottom;
        int h = y1 - y0 + 1, w = x1 - x0 + 1;
        if (w < 2 || h < 2) return 0;
        var ink = new List<(int X, int Y)>();
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
                if (dark[x, y]) ink.Add((x - x0, y1 - y));
        double best = 0, bestScore = double.MinValue;
        int pad = (int)Math.Ceiling(0.5 * h);
        var counts = new int[w + 2 * pad];
        for (double t = -0.1; t <= 0.401; t += 0.05)
        {
            Array.Clear(counts);
            foreach (var (x, up) in ink)
            {
                int c = (int)Math.Round(x - t * up) + pad;
                if (c >= 0 && c < counts.Length) counts[c]++;
            }
            double score = 0;
            foreach (int c in counts) score += (double)c * c;
            if (score > bestScore) { bestScore = score; best = t; }
        }
        return best;
    }

    static double Overlap(RectangleF a, RectangleF b)
    {
        var o = RectangleF.Intersect(a, b);
        return o.Width * o.Height / Math.Max(1e-6, Math.Min(a.Width * a.Height, b.Width * b.Height));
    }

    /// <summary>
    /// The rows of letters in a band (the working image, x0,y0 to x1,y1): pieces of ink two fifths of a
    /// space to two and a half tall and up to five wide, not touching the band's edges (a stem, a slur
    /// running out of it), with no head and nothing the detector marked in them (a dynamic's letters,
    /// an accidental, a rest); in rows of two and more that overlap by half their height and stand within
    /// a space and a half of each other, a space and a fifth wide and more.
    /// </summary>
    static List<RectangleF> Rows(PageResult res, int x0, int y0, int x1, int y1)
    {
        var rows = new List<RectangleF>();
        double s = res.S;
        var dark = res.Dark;
        x0 = Math.Max(0, x0); y0 = Math.Max(0, y0); x1 = Math.Min(dark.W - 1, x1); y1 = Math.Min(dark.H - 1, y1);
        int w = x1 - x0 + 1, h = y1 - y0 + 1;
        if (w < 2 * s || h < 2 * s) return rows;
        var (_, comps) = Components.Label(w, h, i => dark[x0 + i % w, y0 + i / w], eight: true);
        var marked = res.Sym.Heads.Select(q => (q.X, q.Y))
            .Concat((res.Sym.Detections ?? []).Where(t => t.Score > Detector.Threshold && !t.Class.StartsWith("dyn_")).Select(t => (t.X, t.Y))).ToList();
        var runs = DynamicRuns(res).Where(r => r.Word != null).Select(r => r.Letters.Select(t => (t.X, t.Y)).ToList()).ToList();
        var dynamic = runs.SelectMany(r => r).ToList();
        bool Holds(Comp c, List<(double X, double Y)> at) => at.Any(p => p.X >= x0 + c.MinX - 1 && p.X <= x0 + c.MaxX + 1 && p.Y >= y0 + c.MinY - 1 && p.Y <= y0 + c.MaxY + 1);
        var letters = comps.Skip(1).Where(c => c.H >= 0.4 * s && c.H <= 2.5 * s && c.W <= 5 * s && c.MinX > 0 && c.MinY > 0 && c.MaxX < w - 1 && c.MaxY < h - 1 && !Holds(c, marked)).ToList();
        // (a dynamic's letters are no row's, unless other letters touch the run within a third of a space:
        // the "mp" of "sempre" and the p of "poco", which the detector sees as dynamics, are the word's.
        // So is the "sf" just before "dim.", 0.16 spaces off it: the reading takes in a letter or two more
        // at the ends (Match), and the dynamic stays the note's, since "dim." spells no "sf" (Apply))
        bool Side(Comp c, int dir) => letters.Any(o => o != c && !Holds(o, dynamic) && Math.Min(o.MaxY, c.MaxY) > Math.Max(o.MinY, c.MinY)
                                                       && (dir > 0 ? o.MinX - c.MaxX : c.MinX - o.MaxX) is var gap && gap < 0.35 * s && gap > -0.5 * c.W);
        var inWords = new HashSet<Comp>();
        foreach (var run in runs)
        {
            var its = letters.Where(c => Holds(c, run)).OrderBy(c => c.MinX).ToList();
            if (its.Count > 0 && (Side(its[0], -1) || Side(its[^1], 1))) inWords.UnionWith(its);
        }
        var pieces = letters.Where(c => !Holds(c, dynamic) || inWords.Contains(c)).OrderBy(c => c.MinX).ToList();
        var groups = new List<List<Comp>>();
        foreach (var c in pieces)
        {
            List<Comp> best = null;
            double bestOverlap = 0;
            foreach (var g in groups)
            {
                // (two spaces: "poco" and "rit." stand 1.8 apart where the row loses a letter joined to a barline)
                if (c.MinX - g.Max(o => o.MaxX) > 2.0 * s) continue;
                int gTop = g.Min(o => o.MinY), gBot = g.Max(o => o.MaxY);
                double overlap = (Math.Min(c.MaxY, gBot) - Math.Max(c.MinY, gTop) + 1) / (double)Math.Min(c.H, gBot - gTop + 1);
                if (overlap >= 0.5 && overlap > bestOverlap) { best = g; bestOverlap = overlap; }
            }
            if (best == null) groups.Add(best = new List<Comp>());
            best.Add(c);
        }
        foreach (var g in groups)
        {
            int left = g.Min(o => o.MinX), right = g.Max(o => o.MaxX), top = g.Min(o => o.MinY), bottom = g.Max(o => o.MaxY);
            var heights = g.Select(o => o.H).OrderBy(v => v).ToList();
            // (twenty spaces at most: "etwas langsamer" is sixteen; a song's words run on the length of a system.
            // Not a dynamic's letters alone, "mp")
            if (g.Count < 2 || right - left + 1 < 1.2 * s || right - left + 1 > 20 * s || heights[heights.Count / 2] < 0.6 * s || g.All(c => Holds(c, dynamic))) continue;
            rows.Add(RectangleF.FromLTRB(x0 + left, y0 + top, x0 + right + 1, y0 + bottom + 1));
        }
        return rows;
    }

    /// <summary>
    /// The detector's dynamic letters in runs level with one another, a space and a half apart at most,
    /// with the dynamic each run spells (Expressions' words: sf, mf, pp), or null. A letter it sees in
    /// a word of expression, the r of "rit." (dyn_r at 0.61), spells none, and stays the row's.
    /// </summary>
    static List<(string Word, List<Detection> Letters)> DynamicRuns(PageResult res)
    {
        double s = res.S;
        var letters = (res.Sym.Detections ?? []).Where(t => t.Score > Detector.Threshold && t.Class.StartsWith("dyn_")).OrderBy(t => t.X).ToList();
        var runs = new List<List<Detection>>();
        foreach (var t in letters)
        {
            // (within a space of height: a p's centre stands lower than an m's, its stem hanging under the line;
            // and two spaces apart: an mp's letters' centres stand a space and a half apart)
            var run = runs.FirstOrDefault(r => t.X - r[^1].X < 2.0 * s && Math.Abs(t.Y - r[^1].Y) < 1.0 * s);
            if (run == null) runs.Add(run = new List<Detection>());
            run.Add(t);
        }
        return runs.Select(r => (string.Concat(r.Select(t => t.Class[4..])), r)).Select(r => (Expressions.IsDynamic(r.Item1) ? r.Item1 : null, r.r)).ToList();
    }

    /// <summary>
    /// A row read as a word of expression: cut out with half an em and then nine tenths round it,
    /// sheared back upright and as it is, read by itself at the page's scale and twice it, alone and
    /// three times in a row (the Japanese engine passes over one short word by itself: Form's
    /// figures); the first reading the vocabulary knows (<see cref="Match"/>), or null.
    /// </summary>
    static string Read(Bitmap page, RectangleF box, out string seen)
    {
        var tried = new List<string>();
        double k0 = Header.Scale(page.Width);
        // (half an em of the page either side, then nine tenths: a letter the row left out, one joined to
        // a barline, is in the wider cut; the narrower reads "zart" where the wider reads "Zctrt")
        foreach (double margin in new[] { 0.5, 0.9 })
        foreach (double k in new[] { 1.0, 2.0 })
            foreach (int n in new[] { 1, 3 })
                foreach (double shear in Shears)
                {
                    var read = Cut(page, box, k * k0, n, shear, margin);
                    tried.Add($"{margin:F1}/{shear:F2}/{k:F0}/{n}x\"{string.Join("|", read)}\"");
                    foreach (string t in read)
                        if (Match(t) is string p) { seen = string.Join(" ", tried); return p; }
                }
        seen = string.Join(" ", tried);
        return null;
    }

    // (the italic's slant taken out: the cut sheared back by so much of its height, before as it is.
    // The Japanese engine reads Lily#'s italic "dim." as "市m." and sheared back as "dim.")
    public static double[] Shears = { 0.22, 0.0 };

    // the cut, with half an em round it, set n times in a row on white (sheared back by `shear`) and
    // read at scale k: each tile's words
    static List<string> Cut(Bitmap page, RectangleF box, double k, int n, double shear, double margin)
    {
        int em = (int)Math.Ceiling(box.Height), pad = (int)Math.Ceiling(0.25 * box.Height);
        var inner = Rectangle.FromLTRB((int)Math.Floor(box.Left - margin * em), (int)Math.Floor(box.Top - 0.35 * em), (int)Math.Ceiling(box.Right + margin * em), (int)Math.Ceiling(box.Bottom + 0.35 * em));
        inner.Intersect(new Rectangle(0, 0, page.Width, page.Height));
        if (inner.Width < 4 || inner.Height < 4) return [];
        int extra = (int)Math.Ceiling(shear * inner.Height);
        int tw = inner.Width + extra + 2 * pad, th = inner.Height + 2 * pad;
        var t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        using var clip = Clean(page, inner);
        CleanTicks += System.Diagnostics.Stopwatch.GetTimestamp() - t0;
        Cuts++;
        using var bmp = new Bitmap(n * tw, th, PixelFormat.Format24bppRgb);
        using (var gr = Graphics.FromImage(bmp))
        {
            gr.Clear(Color.White);
            gr.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            for (int t = 0; t < n; t++)
            {
                // (x' = x + shear * y: the bottom moved right as far as the slant leans the top)
                gr.Transform = new System.Drawing.Drawing2D.Matrix(1, 0, (float)shear, 1, t * tw + pad, pad);
                gr.DrawImage(clip, new Rectangle(0, 0, inner.Width, inner.Height), new Rectangle(0, 0, inner.Width, inner.Height), GraphicsUnit.Pixel);
            }
            gr.ResetTransform();
        }
        if (Dump != null) bmp.Save(Path.Combine(Dump, $"cut{dumped++:D3}-{n}-{shear:F2}.png"));
        var lines = Header.Recognize(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height), k, false);
        if (n == 1) return lines.Count == 0 ? [] : [string.Join(" ", lines.OrderBy(l => l.Box.Left).Select(l => l.Text))];
        if (lines.Count == 1)
        {
            // (the three read as one line: its words in three groups)
            var words = lines[0].Text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length % 3 == 0 && words.Length > 0)
                return Enumerable.Range(0, 3).Select(t => string.Join(" ", words.Skip(t * words.Length / 3).Take(words.Length / 3))).ToList();
        }
        // (each tile's lines joined: the engine breaks a word now and then, "dol" and "e" for "dolce")
        return lines.GroupBy(l => (int)((l.Box.Left + l.Box.Right) / 2 / tw)).OrderBy(g => g.Key)
                    .Select(g => string.Join(" ", g.OrderBy(l => l.Box.Left).Select(l => l.Text))).ToList();
    }

    // the cut as a bitmap of its own, a line through it from top to bottom, a barline or a stem the
    // words run over, painted white: the t of a "poco rit." over a barline is read with it as "rL"
    static Bitmap Clean(Bitmap page, Rectangle inner)
    {
        // (drawn, not Bitmap.Clone: a clone of a cut of the whole page costs a tenth of a second)
        var clip = new Bitmap(inner.Width, inner.Height, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(clip)) g.DrawImage(page, new Rectangle(0, 0, inner.Width, inner.Height), inner, GraphicsUnit.Pixel);
        var data = clip.LockBits(new Rectangle(0, 0, clip.Width, clip.Height), ImageLockMode.ReadWrite, PixelFormat.Format24bppRgb);
        try
        {
            int stride = data.Stride, w = clip.Width, h = clip.Height;
            var px = new byte[stride * h];
            System.Runtime.InteropServices.Marshal.Copy(data.Scan0, px, 0, px.Length);
            bool any = false;
            for (int x = 0; x < w; x++)
            {
                int dark = 0;
                for (int y = 0; y < h; y++) { int i = y * stride + 3 * x; if (px[i] + px[i + 1] + px[i + 2] < 3 * 128) dark++; }
                if (dark < 0.95 * h) continue;
                for (int y = 0; y < h; y++) { int i = y * stride + 3 * x; px[i] = px[i + 1] = px[i + 2] = 255; }
                any = true;
            }
            if (any) System.Runtime.InteropServices.Marshal.Copy(px, 0, data.Scan0, px.Length);
        }
        finally { clip.UnlockBits(data); }
        return clip;
    }

    // (letters only, lower case, without accents; the OCR's figures and bars for letters; a run of the
    // Japanese engine's own script as one '?', a letter or two it made a character of)
    static string Key(string text)
    {
        const string Accented = "àáâäãåèéêëìíîïòóôöõùúûüñç10|!", Plain = "aaaaaaeeeeiiiiooooouuuuncloli";
        var sb = new System.Text.StringBuilder();
        foreach (char ch in text.ToLowerInvariant())
        {
            int i = Accented.IndexOf(ch);
            char c = i >= 0 ? Plain[i] : ch;
            if (c is >= 'a' and <= 'z') sb.Append(c);
            else if (c == 'ß') sb.Append("ss");
            else if (Regex.IsMatch(c.ToString(), $"[{Header.Cjk}]") && (sb.Length == 0 || sb[^1] != '?')) sb.Append('?');
        }
        return sb.ToString();
    }

    /// <summary>
    /// The word of expression a reading is, as the vocabulary spells it: its letters (<see cref="Key"/>)
    /// the same as one's, or one's with up to two letters more at its ends ("bTrit." for "rit."), or
    /// within a letter's change of one of five letters and more (two of nine and more); a character of
    /// the Japanese engine's own script stands for up to two letters, with no other letter amiss
    /// ("sempre 盟" for "sempre p": the letters read three and more, and seven tenths of the word's).
    /// Read as it is and with the engine's ways with italic undone ("Zctrt" for "zart"). Null for none.
    /// </summary>
    internal static string Match(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        // (a character of the engine's script at either end is what the cut took in beside the words: "リ sempre ー")
        string key = Key(text).Trim('?');
        // (and as read with the Japanese engine's ways with italic letters undone: an a as "ct", "Zctrt" for
        // "zart"; a d as "cl"; an m as "rn")
        string undone = key.Replace("ct", "a").Replace("cl", "d").Replace("rn", "m");
        return Nearest(key) ?? (undone != key ? Nearest(undone) : null);
    }

    static string Nearest(string k)
    {
        int real = k.Count(c => c != '?');
        if (real < 3) return null;
        string best = null;
        int bestD = int.MaxValue;
        bool wild = k.Contains('?');
        foreach (var p in Phrases)
        {
            string pk = Key(p);
            if (real < (wild ? 0.7 : 0.6) * pk.Length) continue;
            // (with a character of the engine's script for letters, no other letter amiss: "1蝨el", a song's
            // "mei-", is no "accel.")
            int limit = wild ? (pk.Length >= 9 ? 1 : 0) : pk.Length >= 9 ? 2 : pk.Length >= 5 ? 1 : 0;
            int d = pk == k ? 0 : Distance(pk, k);
            // (the word with up to two letters more at its ends, what the cut took in beside it: "bTrit.")
            if (d > limit && !wild && k.Length > pk.Length && k.Length <= pk.Length + 2 && k.IndexOf(pk, StringComparison.Ordinal) is int at && at >= 0) d = limit;
            // (of two as near, the one nearer the reading's length, a character of the engine's script
            // counted a letter: "sempre 盟" is "sempre p", neither "sempre" nor "sempre pp")
            if (d <= limit && (d < bestD || (d == bestD && Math.Abs(pk.Length - k.Length) < Math.Abs(Key(best).Length - k.Length)))) { best = p; bestD = d; }
        }
        return best;
    }

    // (the edit distance from a word to a reading, a '?' in the reading standing for up to two letters of the word)
    static int Distance(string a, string b)
    {
        var d = new int[a.Length + 1, b.Length + 1];
        for (int i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (int j = 1; j <= b.Length; j++)
            for (int i = 0; i <= a.Length; i++)
            {
                if (b[j - 1] == '?') { d[i, j] = d[i, j - 1]; for (int t = 1; t <= 2 && i - t >= 0; t++) d[i, j] = Math.Min(d[i, j], d[i - t, j - 1]); continue; }
                // (an i read as an l costs nothing: the engine's way with an italic i, "PLZZ." for "pizz.")
                d[i, j] = i == 0 ? d[0, j - 1] + 1
                    : Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + (a[i - 1] == b[j - 1] || (a[i - 1] == 'i' && b[j - 1] == 'l') ? 0 : 1));
            }
        return d[a.Length, b.Length];
    }
}
