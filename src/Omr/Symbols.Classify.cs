namespace Omr;

static partial class Symbols
{
    static void Classify(PageSymbols ps, Img page, Img ink, List<SystemInfo> systems, double s)
    {
        // right to left: the sign next to a note claims it before one farther out (a natural
        // before a sharp cancels a double sharp; it does not belong to the note by itself)
        foreach (var o in ps.Others.OrderByDescending(o => o.C.MaxX).ToList())
        {
            if (o.Class != "?") continue;   // clef, key or time signature
            var c = o.C;
            double w = c.W / s, h = c.H / s;
            if (GlyphKnn.Enabled)
            {
                if (ClassifyLearnt(ps, o, s, out var learnt)) continue;
                // (a whole and a half rest are one shape to the classifier: the rules place them)
                if (KnnOnly && learnt is not ("rests.0" or "rests.1")) continue;
            }
            // the rules check where a sign stands, so what they name is a sample of this score's own
            // font for the classifier (calibration: a first reading gathers, a second one knows)
            void Learn(string label) { if (Collect) GlyphKnn.AddLocal(label, GlyphKnn.Features(ps, c, s)); }
            if (w >= 0.2 && w <= 0.65 && h >= 0.2 && h <= 0.65 && c.Fill >= 0.5 && Math.Abs(w - h) <= 0.25) { o.Class = "dot"; Learn("dots.dot"); continue; }
            // accidental: tall, just left of a head that sits within its height. A flat carries
            // the head low (in its bowl); a sharp or natural carries it in the middle.
            if (h >= 2.0 && h <= 3.4 && w >= 0.4 && w <= 1.8)
            {
                Head best = null;
                double bd = double.MaxValue;
                foreach (var hd in ps.Heads)
                {
                    if (!hd.On(o.St) || hd.Acc != null) continue;
                    double gap = hd.L - c.MaxX;
                    if (gap < -0.15 * s || gap > 1.0 * s) continue;
                    if (hd.Y < c.MinY + 0.2 * c.H || hd.Y > c.MaxY - 0.1 * c.H) continue;
                    if (gap < bd) { bd = gap; best = hd; }
                }
                // a 16th rest is as wide as a double flat but taller and thinner in ink
                if (best != null && w >= 1.3 && (h > 2.8 || c.Fill < 0.3)) best = null;
                // a piece the detector is sure is a rest and sees no sign in: MuseJazz's sixteenth
                // rest just left of a head passes for a double flat (DetectedRests names it); so does
                // Petaluma's between a beam's heads, seen at 0.90 (no sign seen there is enough)
                if (best != null && ps.Detections != null
                    && ps.Detections.Any(t => t.Class.StartsWith("rest_") && t.Score >= TrustScore && Inside(t))
                    && !ps.Detections.Any(t => t.Class is "sharp" or "flat" or "natural" or "dsharp" or "dflat" && t.Score >= TrustScore && Inside(t)))
                    best = null;
                bool Inside(Detection t) => c.MinX <= t.X && t.X <= c.MaxX && c.MinY <= t.Y && t.Y <= c.MaxY;
                // a sign by a chord reaches several heads: the detector's kind tells where its note
                // stands (a flat's in its bowl, a quarter of its height up from the bottom; others'
                // at the middle) and names it (by the nearest head the rules read Leland's flat by a
                // chord's middle head as a sharp)
                var sign = best == null || ps.Detections == null ? null
                    : ps.Detections.Where(t => t.Class is "sharp" or "flat" or "natural" or "dsharp" or "dflat" && t.Score >= TrustScore && Inside(t)).OrderByDescending(t => t.Score).FirstOrDefault();
                if (sign != null)
                {
                    // (a sharp's or natural's own centre: its ink box may run on into a stroke beside it,
                    // Finale Broadway's sharp by a second's two heads halfway between them)
                    double mark = sign.Class is "flat" or "dflat" ? c.MaxY - 0.256 * c.H : sign.Y;
                    // (up to a head's width further: a second's other head is set beside the first)
                    var near = ps.Heads.Where(hd => hd.On(o.St) && hd.Acc == null && hd.L - c.MaxX >= -0.15 * s && hd.L - c.MaxX <= bd + 1.5 * s
                                                    && Math.Abs(hd.Y - mark) < 0.5 * s)
                                       .OrderBy(hd => Math.Abs(hd.Y - mark)).FirstOrDefault();
                    if (near != null)
                    {
                        o.Class = sign.Class;
                        near.Acc = sign.Class switch { "sharp" => 1, "flat" => -1, "natural" => 0, "dsharp" => 2, _ => -2 };
                        o.For = near;
                        continue;
                    }
                }
                if (best != null)
                {
                    double at = (best.Y - c.MinY) / c.H;
                    (o.Class, best.Acc) = w >= 1.3 ? ("dflat", -2) : w < 0.76 ? ("natural", 0) : at >= 0.6 ? ("flat", -1) : ("sharp", 1);
                    o.For = best;
                    Learn(o.Class switch { "dflat" => "accidentals.flatflat", "natural" => "accidentals.natural", "flat" => "accidentals.flat", _ => "accidentals.sharp" });
                }
            }
            if (o.Class != "?") continue;
            // rests sit in the staff, or beyond it when two voices share the staff
            double pos = o.St.Pos(c.MinY + c.H / 2.0);
            if (pos < -12 || pos > 12) continue;
            RestKind? kind = null;
            // (a block half a space thick: a staff line's piece the line removal left on a bowed page,
            // a row of pixels, is none -- 揺れる想い's whole rests in Syn.2's full bars)
            if (c.Fill >= 0.8 && h <= 0.8 && h >= 0.25 && w >= 0.8 && w <= 1.8)
            {
                // a whole rest hangs from a line, a half rest sits on one (lines run on as ledgers)
                bool hangs = OnLine(o.St, c.MinY, s), sits = OnLine(o.St, c.MaxY, s);
                bool outside = Math.Abs(pos) > 4;
                // beyond the staff (a voice's rest moved aside) only a solid block on a line's place
                // counts, and not far out: a mark's box or a thick stroke is no rest
                if (outside && (Math.Abs(pos) > 8 || c.Fill < 0.9 || hangs == sits)) continue;
                // (a short thick tie's middle between a chord's heads is such a block: where the
                // detector sees the tie's two ends either side of it and no rest -- Petaluma's ties001,
                // <b, d fis>16~ <b, d fis>8, the lower tie read as a whole rest; the ends lie up to a
                // space off the arc's middle, ties004's over 16ths' heads)
                bool unseen = ps.Detections != null
                    && !ps.Detections.Any(t => t.Class == "rest_long" && t.Score >= TrustScore && Math.Abs(t.X - c.Cx) < s && Math.Abs(t.Y - c.Cy) < s);
                if (unseen
                    && ps.Detections.Any(t => t.Class is "tie_l" or "slur_l" && t.Score > Detector.Threshold && t.X < c.Cx && c.Cx - t.X < 2.5 * s && Math.Abs(t.Y - c.Cy) < 1.2 * s)
                    && ps.Detections.Any(t => t.Class is "tie_r" or "slur_r" && t.Score > Detector.Threshold && t.X > c.Cx && t.X - c.Cx < 2.5 * s && Math.Abs(t.Y - c.Cy) < 1.2 * s))
                    continue;
                // (a thin block the detector sees no whole or half rest on is a staff line's scrap, left where
                // a bowed page's line drifted out of the band the removal takes: kinkel7-1's second page, four
                // whole rests in a bar. A rest's block is half a space thick; the scraps a third and under)
                if (unseen && h < 0.4) continue;
                kind = hangs && !sits ? RestKind.Whole : sits && !hangs ? RestKind.Half : pos > 1 ? RestKind.Whole : RestKind.Half;
            }
            else if (w >= 0.9 && w <= 1.35 && h >= 1.6 && h <= 2.2 && c.Fill < 0.4) kind = RestKind.Eighth;
            else if (w >= 0.7 && w <= 1.1 && h >= 2.7 && h <= 3.2) kind = RestKind.Quarter;
            else if (w >= 1.2 && w <= 1.65 && h >= 2.6 && h <= 3.2 && c.Fill < 0.33) kind = RestKind.Sixteenth;
            if (kind == null) continue;
            // (a voice's rest moved aside stands within a ledger line's place or so of the staff;
            // an f of a dynamic a space and a half off the staff (pos 8), a lyric's letter under a
            // vocal staff (an n or a y at pos -7 to -11) pass for eighth rests: the detector's
            // DropUnseenRests, the rules alone here)
            // (the whole and half rests' blocks have their own test above)
            if (kind is not (RestKind.Whole or RestKind.Half) && Math.Abs(pos) > 7) continue;
            if (Math.Abs(pos) > 5 && InTextRow(ps, o, s)) continue;
            if (kind is RestKind.Eighth or RestKind.Sixteenth && HeadAtFoot(ps, c, s)) continue;
            o.Class = "rest";
            ps.Rests.Add(new Rest { St = o.St, X = c.Cx, Y = c.MinY + c.H / 2.0, L = c.MinX, R = c.MaxX, Kind = kind.Value });
            Learn(kind switch { RestKind.Whole => "rests.0", RestKind.Half => "rests.1", RestKind.Quarter => "rests.2", RestKind.Eighth => "rests.3", _ => "rests.4" });
        }
        // (after the rules, so that a clean page reads as it did: the detector fills in what the
        // rules could not name, a scan's blurred signs)
        if (ps.Detections != null) { DropUnseenRests(ps, s); DetectedAccidentals(ps, s); WeakNaturals(ps, s); DetectedRests(ps, s); }
        TwinFlats(ps, s);
        // the slur Lily# draws from an acciaccatura or appoggiatura down (or up) to its note is no
        // rest (chords005's bar 3: an eighth rest read on it, and the grace note before a "rest" is
        // nobody's): a rest between a grace note and the next head on its staff, where the detector
        // sees no rest, goes
        if (ps.Graces.Count > 0)
            ps.Rests.RemoveAll(r => ps.Graces.Any(g => g.St == r.St && r.X > g.X && r.X < ps.Heads.Where(h => h.St == g.St && h.X > g.X).Select(h => h.X).DefaultIfEmpty(g.X + 8 * s).Min() + 0.3 * s && Math.Abs(r.Y - g.Y) < 5 * s)
                && !(ps.Detections?.Any(t => t.Class.StartsWith("rest_") && t.Score >= TrustScore && Math.Abs(t.X - r.X) < 0.7 * s && Math.Abs(t.Y - r.Y) < s) == true));
        // an arpeggio's wavy line left of a chord: no rest read in it (a quarter rest's zigzag to the
        // rules and the detector), whatever the detector says
        ps.Arpeggios = Arpeggios.Find(ink, ps.Heads, s, ps.Detections);
        if (ps.Arpeggios.Count > 0)
        {
            ps.Rests.RemoveAll(r => Arpeggios.InWave(ps.Arpeggios, r, s));
            // (nor a hollow head the rules alone found: a loop of the wave, its line taken for the stem --
            // kinkel7-1's scan, bar 5's left hand read as <e g>2 e2 beside the waves)
            ps.Heads.RemoveAll(h => h.Hollow && h.Score == 0 && !ps.Arpeggios.Any(a => a.Chord.Contains(h))
                && ps.Arpeggios.Any(a => !a.Sign && a.Chord[0].St == h.St && h.L < a.R && h.R > a.L && h.Y > a.Top - 1.5 * s && h.Y < a.Bottom + 1.5 * s));
            // (nor a sign or a dot: the wave's pieces between the staff lines, taken for flats and
            // naturals by the rules and the classifier, gave their signs to the chord's heads)
            foreach (var o in ps.Others.Where(o => o.Class is "?" or "rest" or "dot" or "sharp" or "flat" or "natural" or "dsharp" or "dflat").ToList())
            {
                if (Arpeggios.Holding(ps.Arpeggios, o.St, o.C, s) is not { } a) continue;
                if (o.For != null && o.For.Acc == (o.Class switch { "sharp" => 1, "flat" => -1, "natural" => 0, "dsharp" => 2, "dflat" => -2, _ => 9 })) o.For.Acc = null;
                o.For = null;
                o.Class = a.Sign ? "clef" : "arpeggio";
            }
        }
        // dots: right of a head or rest, within a space vertically
        var dots = ps.Others.Where(o => o.Class == "dot").Select(o => new DotAt { O = o, St = o.St, X = o.C.Cx, Y = o.C.Cy }).ToList();
        if (ps.Detections != null) dots = DetectedDots(ps, s, dots);
        RepeatDots(ps, page, systems, dots, s);
        var dotsAt = new Dictionary<Head, List<double>>();
        var given = new List<DotAt>();
        void Give(DotAt d, object best, string how = "beside it")
        {
            if (DebugAt.HasValue && Math.Abs(d.X - DebugAt.Value.X) < 4 * s && Math.Abs(d.Y - DebugAt.Value.Y) < 3 * s)
                Console.WriteLine($"dot at {d.X:F0},{d.Y:F0}{(d.O != null ? $" ({d.O.C.W / s:F2}x{d.O.C.H / s:F2})" : "")}: to the {(best is Head h ? $"head at {h.X:F0},{h.Y:F0}" : best is Rest r ? $"rest at {r.X:F0},{r.Y:F0}" : "?")}, {how}");
            if (best is Head bh) { bh.Dots++; bh.UnseenDot |= d.Unseen; (dotsAt.TryGetValue(bh, out var l) ? l : dotsAt[bh] = new()).Add(d.X); }
            else if (best is Rest br) br.Dots++;
            else return;
            if (d.O != null) d.O.Class = "dot*";
            given.Add(d);
        }
        var left = new List<DotAt>();
        foreach (var d in dots.OrderBy(d => d.X))
        {
            object best = null;
            double bd = double.MaxValue;
            // a dot sits in the head's own space (the space above, for a head on a line); with two
            // voices the other voice's heads are near too, so the vertical match decides first
            foreach (var h in ps.Heads)
            {
                if (!h.On(d.St)) continue;
                double dx = d.X - h.R, dy = Math.Abs(d.Y - h.Y);
                // (a slash's dot is set at its upper right end, touching it, within the slash's own
                // extent -- leadsheet-collide's /2. on its rhythm staff)
                // (an old print tucks a ledger head's dot into the space under it, under the head's right
                // end -- lang36-3's scan, the bass's ees,,4.: right of the head's middle, unlike a staccato's)
                bool tucked = !h.Slash && dx >= -0.35 * s && dx <= 0.3 * s && d.X - h.X >= 0.35 * s && dy >= 0.5 * s && dy <= 0.9 * s
                              && Math.Abs(h.St.Pos(h.Y)) > 5 && Math.Abs(h.St.Pos(d.Y)) > Math.Abs(h.St.Pos(h.Y))
                              // (an i's dot under a low note has its letter under it: "Spiel", "dimin.")
                              && !ps.Others.Any(q => q != d.O && q.St == d.St && q.C.H >= 0.5 * s && q.C.MinX <= d.X + 0.3 * s
                                                     && q.C.MaxX >= d.X - 0.3 * s && q.C.MinY > d.Y && q.C.MinY - d.Y < 0.8 * s);
                if (!tucked && (dx < (h.Slash ? -0.5 * s : 0.05 * s) || dx > 1.8 * s || dy > 0.65 * s)) continue;
                double score = Math.Max(dx, 0) + 2 * dy;
                if (score < bd) { bd = score; best = h; }
            }
            foreach (var r in ps.Rests)
            {
                if (r.St != d.St) continue;
                // (an old print sets a rest's dot further out: kinkel19-2's half rests' at 1.3 spaces)
                double dx = d.X - r.R;
                if (dx < 0.05 * s || dx > 1.5 * s || Math.Abs(d.Y - r.Y) > 1.5 * s) continue;
                // (a rest's dot stands in a space: a piece of ink on a line is a speck of the toner --
                // kinkel10-2's scan, half a space wide on the middle line right of a 16th rest, a dozen
                // 16th rests dotted)
                if (d.O != null && d.St.Lines.Min(l => Math.Abs(l.Y - d.Y)) < 0.2 * s) continue;
                if (dx < bd) { bd = dx; best = r; }
            }
            // (a unison, one head on two stems, has one dot for both voices: two of one value are both
            // dotted; of two values the longer is -- an old print shares the head of bes'8. a'16 with
            // the lower voice's 16ths, kinkel19-1's scan, bar 9: the 16th was dotted, both voices
            // went wrong, and the bar split at the system's end was not put together)
            if (best is Head bu && ps.Heads.FirstOrDefault(o => o != bu && o.St == bu.St && o.Pos == bu.Pos && o.HasStem && bu.HasStem
                                                              && o.Up != bu.Up && Math.Abs(o.X - bu.X) < 0.3 * s) is { } twin)
            {
                static int Rank(Head h) => h.Hollow ? -1 : h.Beams;
                if (Rank(twin) == Rank(bu)) Give(d, twin, "the unison's other voice");
                else if (Rank(twin) < Rank(bu)) best = twin;
            }
            if (best != null) Give(d, best); else left.Add(d);
        }
        // the dots of one moment stand in one column, right of its rightmost head: a voice's head
        // left of the other voice's second (whose upper note sits right of the stem) has its dot
        // there, farther out than a dot's usual reach (piano006). A dot left over in the column of
        // a dot given on its staff takes the head at its height left of it, up to three spaces out.
        foreach (var d in left)
        {
            if (!given.Any(g => g.St == d.St && Math.Abs(g.X - d.X) < 0.3 * s)) continue;
            Head best = null;
            double bd = double.MaxValue;
            foreach (var h in ps.Heads)
            {
                if (!h.On(d.St)) continue;
                double dx = d.X - h.R, dy = Math.Abs(d.Y - h.Y);
                if (dx < 0.05 * s || dx > 3.0 * s || dy > 0.65 * s) continue;
                if (dotsAt.TryGetValue(h, out var xs) && xs.Any(x => Math.Abs(x - d.X) < 0.3 * s)) continue;
                double score = dx + 2 * dy;
                if (score < bd) { bd = score; best = h; }
            }
            if (best != null) Give(d, best, "in a column of dots");
        }
        // An old print sets a dot further from its head still (kinkel19-2's Trautwein print: two to
        // two and a half spaces, the half notes of 3/4 bars read undotted and the bars filled
        // otherwise). A dot left over takes the head at its height left of it, up to three and a
        // quarter spaces out, when nothing of the staff's stands between the two (another head, a
        // rest, a sign, a barline): a dot stands right after its note.
        // (a dot of a dot's size, 0.35 space and more: a speck of the toner is none -- gen-grace-scan's
        // piano006, a speck two spaces off a chord read as its dot by the rules)
        var barXs = systems.SelectMany(x => x.Bars).ToList();
        foreach (var d in left.Where(d => !given.Contains(d) && (d.O == null || (d.O.C.W >= 0.35 * s && d.O.C.H >= 0.35 * s))))
        {
            Head best = null;
            double bd = double.MaxValue;
            foreach (var h in ps.Heads)
            {
                if (!h.On(d.St)) continue;
                double dx = d.X - h.R, dy = Math.Abs(d.Y - h.Y);
                if (dx < 1.8 * s || dx > 3.25 * s || dy > 0.65 * s) continue;
                if (ps.Heads.Any(o => o != h && o.On(d.St) && o.L > h.R + 0.2 * s && o.L < d.X - 0.2 * s)) continue;
                if (ps.Rests.Any(r => r.St == d.St && r.R > h.R + 0.2 * s && r.L < d.X - 0.2 * s)) continue;
                if (ps.Others.Any(o => o.St == d.St && o.Class is "sharp" or "flat" or "natural" or "dsharp" or "dflat" && o.C.MinX > h.R && o.C.MinX < d.X)) continue;
                if (barXs.Any(b => b > h.R && b < d.X)) continue;
                double score = dx + 2 * dy;
                if (score < bd) { bd = score; best = h; }
            }
            if (best != null) Give(d, best, "far off, as an old print sets it");
        }
        // a chord's dots belong to the chord: each head has as many as the chord has columns of them
        // (a dot goes to the nearest head, so one head may take two and its neighbour none)
        // (a chord: heads on one stem, or stemless at one x. Chained by nearness, not rounded into
        // buckets: two heads on one stem fell either side of a bucket's edge, Bravura's ties006)
        // (and on one stem only within its length: two stems one under the other at one x,
        // lied010's upper head and a chord on ledger lines below the staff, are two chords)
        bool Spans(Head a, Head b) => b.Y >= Math.Min(a.Y, a.Tip) - 0.5 * s && b.Y <= Math.Max(a.Y, a.Tip) + 0.5 * s;
        var chords = new List<List<Head>>();
        foreach (var g in ps.Heads.GroupBy(h => (h.St, h.HasStem, h.Up)))
        {
            double reach = g.Key.HasStem ? 0.3 * s : 0.8 * s;
            double At(Head h) => h.HasStem ? h.StemX : h.X;
            var open = new List<List<Head>>();
            foreach (var h in g.OrderBy(At))
            {
                var mate = open.LastOrDefault(c => At(h) - At(c[^1]) < reach && (!h.HasStem || c.Any(o => Spans(o, h) || Spans(h, o))));
                if (mate != null) mate.Add(h);
                else open.Add(new List<Head> { h });
            }
            chords.AddRange(open);
        }
        foreach (var chord in chords)
        {
            var xs = chord.SelectMany(h => dotsAt.GetValueOrDefault(h) ?? new List<double>()).OrderBy(x => x).ToList();
            int cols = 0;
            double last = double.MinValue;
            foreach (var x in xs) if (x - last > 0.3 * s) { cols++; last = x; }
            foreach (var h in chord) h.Dots = cols;
        }
        if (ps.Detections != null) Doubts(ps, s);
    }

    // a sign or dot the detector saw this surely, under TrustScore, is a reading to offer
    public static double WeakSign = 0.2;

    static readonly Dictionary<string, (int Alt, string Glyph)> Signs = new()
    {
        ["sharp"] = (1, "♯"), ["flat"] = (-1, "♭"), ["natural"] = (0, "♮"), ["dsharp"] = (2, "𝄪"), ["dflat"] = (-2, "𝄫"),
    };
    /// <summary>
    /// A repeat barline's dots (:| or |:) are no note's: two dots of one staff in its middle two
    /// spaces, one over the other, with a barline beside them within two spaces and a fifth that is
    /// a repeat's, thick (a run a third of a space wide on the dots' rows, out to half a space past
    /// the barline) or double. The pair leaves the dots (bwv610's last sixteenth before the :| at
    /// the end of its bar 6 read dotted, a 32nd too long). The repeat itself is not read yet.
    /// </summary>
    static void RepeatDots(PageSymbols ps, Img page, List<SystemInfo> systems, List<DotAt> dots, double s)
    {
        if (systems == null) return;
        foreach (var a in dots.ToList())
        {
            bool debug = DebugAt.HasValue && Math.Abs(a.X - DebugAt.Value.X) < 2 * s && Math.Abs(a.Y - DebugAt.Value.Y) < 3 * s;
            if (debug) Console.WriteLine($"repeat? dot at {a.X:F0},{a.Y:F0} pos {a.St.Pos(a.Y):F1}");
            if (!dots.Contains(a) || Math.Abs(a.St.Pos(a.Y) - 1) > 0.5) continue;
            var b = dots.FirstOrDefault(o => o != a && o.St == a.St && Math.Abs(o.X - a.X) < 0.25 * s && Math.Abs(o.St.Pos(o.Y) + 1) < 0.5);
            if (debug) Console.WriteLine($"repeat? dot at {a.X:F0},{a.Y:F0}: the one under it {(b == null ? "none" : $"at {b.X:F0},{b.Y:F0}")}");
            if (b == null) continue;
            var sys = systems.FirstOrDefault(y => y.Staves.Contains(a.St) || y.All.Contains(a.St));
            if (sys == null) continue;
            double x = (a.X + b.X) / 2;
            // (the barline's x: a thin line's, or at a system's end the staff's end, the thick line's
            // right edge, a space and three quarters out from the dots)
            double bar = sys.Bars.Where(v => Math.Abs(v - x) < 2.2 * s).OrderBy(v => Math.Abs(v - x)).FirstOrDefault(double.NaN);
            if (debug) Console.WriteLine($"repeat? dots at {x:F0}: barline {(double.IsNaN(bar) ? "none within 2.2 spaces" : $"at {bar:F0}")} (bars {string.Join(" ", sys.Bars.Select(v => v.ToString("F0")))})");
            if (double.IsNaN(bar)) continue;
            // the dark runs on the dots' rows from the dots towards the barline, half a space past it
            int dir = bar > x ? 1 : -1;
            int x0 = (int)Math.Round(x + dir * 0.35 * s), x1 = (int)Math.Round(bar + dir * 0.6 * s);
            bool repeat = false;
            foreach (double y in new[] { a.Y, b.Y })
            {
                var runs = new List<int>();
                int run = 0;
                for (int xx = x0; xx != x1 + dir; xx += dir)
                {
                    if (page[xx, (int)Math.Round(y)]) run++;
                    else { if (run > 0) runs.Add(run); run = 0; }
                }
                if (run > 0) runs.Add(run);
                if (debug) Console.WriteLine($"repeat? dots at {x:F0}: runs on row {y:F0} from {x0} to {x1}: {string.Join(" ", runs)} (a space is {s:F1})");
                if (runs.Any(r => r >= 0.3 * s) || runs.Count >= 2) { repeat = true; break; }
            }
            if (!repeat) continue;
            dots.Remove(a); dots.Remove(b);
            foreach (var o in new[] { a, b }) if (o.O != null) o.O.Class = "repeat_dot";
            ps.Note = (ps.Note == null ? "" : ps.Note + "; ") + $"a repeat barline's dots at {x:F0},{a.St.Mid:F0} (the repeat is not read)";
        }
    }

    static string Glyph(int? acc) => acc switch { 1 => "♯", -1 => "♭", 0 => "♮", 2 => "𝄪", -2 => "𝄫", _ => "no sign" };

    /// <summary>
    /// The rules' accidentals checked against the detector's signs: one it sees nothing of is
    /// dropped, one it is sure is another sign is that sign. Where the reading of a head's
    /// accidental or dot stays in doubt, the other reading (Head.Alts), for the user to choose
    /// between (the .lys's @todo, the side file's candidates):
    /// - a sign the detector half saw (WeakSign to TrustScore) before a head given none;
    /// - a sign given that the detector less than surely sees as another sign;
    /// - a dot it half saw beside a head given none, and a rule's dot it saw nothing on.
    /// Where a sign stands for its head as in DetectedAccidentals.
    /// </summary>
    static void Doubts(PageSymbols ps, double s)
    {
        // (the naturals under the threshold too, kept apart: a rules' natural they stand by is kept, kinkel19-2's scan)
        var signs = ps.Detections.Concat(ps.WeakNaturals).Where(t => Signs.ContainsKey(t.Class) && t.Score >= WeakSign).ToList();
        // (a flat's box centre is about 0.6 space above its bowl, where its note is)
        double At(Detection t) => Signs[t.Class].Alt < 0 ? t.Y + 0.6 * s : t.Y;
        bool Before(Detection t, Head h) => t.X < h.L && h.L - t.X < 3.2 * s && Math.Abs(At(t) - h.Y) < 0.3 * s;
        void Add(Head h, HeadAlt a) => (h.Alts ??= new()).Add(a);
        foreach (var h in ps.Heads)
        {
            if (h.Slash || h.Cross) continue;
            var mine = signs.Where(t => Before(t, h)).OrderBy(t => h.L - t.X).FirstOrDefault();
            if (h.Acc == null)
            {
                // (a sign that the rules named is a key's or another note's)
                if (mine == null || mine.Score >= TrustScore) continue;
                var owner = PieceAt(ps, mine.X, mine.Y);
                if (owner != null && owner.Class is not ("?" or "dot")) continue;
                if (ps.Heads.Any(o => o != h && o.St == h.St && o.Acc == Signs[mine.Class].Alt && Math.Abs(o.Y - h.Y) < 0.3 * s && o.L > mine.X && o.L < h.L)) continue;
                Add(h, new HeadAlt($"{Signs[mine.Class].Glyph} seen faintly ({mine.Score:F2})", true, Signs[mine.Class].Alt, 0, mine.Score));
            }
            else if (mine == null)
            {
                // A sign the rules gave that the detector sees nothing of is none: a wide flat taken
                // for a double flat (MuseJazz, Petaluma), a scrap by the head taken for a sign (the
                // MuseScore fonts, clean and degraded: 50 of 50 such signs were none). No todo: sure.
                // (any sign a little off its height: a chord's column of signs, a sign the detector placed loosely)
                if (signs.Any(t => t.X < h.L && h.L - t.X < 3.6 * s && Math.Abs(At(t) - h.Y) < 0.8 * s)) continue;
                h.Acc = null;
            }
            // A sign the rules name otherwise than the detector does: the detector's, when it is
            // sure (11 of 11 on the MuseScore fonts: Finale Broadway's double flat read as a natural);
            // otherwise the rules', the detector's on offer
            else if (Signs[mine.Class].Alt != h.Acc && mine.Score >= SureScore) h.Acc = Signs[mine.Class].Alt;
            else if (Signs[mine.Class].Alt != h.Acc)
                Add(h, new HeadAlt($"{Glyph(h.Acc)} read, the detector sees {Signs[mine.Class].Glyph} ({mine.Score:F2})", true, Signs[mine.Class].Alt, 0, mine.Score));
        }
        var dots = ps.Detections.Where(t => t.Class == "dot" && t.Score >= WeakSign && t.Score < TrustScore).ToList();
        foreach (var t in dots)
        {
            // the head left of it, as a dot is given (Classify), when that head has no dot
            Head best = null;
            double bd = double.MaxValue;
            foreach (var h in ps.Heads)
            {
                double dx = t.X - h.R, dy = Math.Abs(t.Y - h.Y);
                if (dx < 0.05 * s || dx > 1.8 * s || dy > 0.65 * s) continue;
                if (dx + 2 * dy < bd) { bd = dx + 2 * dy; best = h; }
            }
            if (best == null || best.Dots > 0 || best.Slash) continue;
            // (a piece the rules named, a sign's or a rest's, is no dot)
            var owner = PieceAt(ps, t.X, t.Y);
            if (owner != null && owner.Class is not ("?" or "dot")) continue;
            Add(best, new HeadAlt($"a dot seen faintly ({t.Score:F2})", false, null, 1, t.Score));
        }
        foreach (var h in ps.Heads.Where(h => h.UnseenDot && h.Dots > 0))
            Add(h, new HeadAlt("a dot the detector does not see", false, null, -1, 0));
    }
}
