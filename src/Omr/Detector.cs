using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Omr;

/// <summary>A symbol the learnt detector found: its class, its centre in page pixels, and how sure it is.</summary>
sealed class Detection
{
    public string Class;
    public double X, Y, Score;
    // a TAB digit's score as each digit 0-9 at its peak (the best over the peak's 3x3): the readings
    // the detector half saw, for the staff to choose among (Tab.Apply)
    public float[] Digits;
}

/// <summary>
/// The learnt symbol detector (S10): the net trained by tools/train-model/train.py, run through
/// ONNX Runtime. It reads the grey page (turned level) at a fixed staff space, in bands 14 spaces
/// tall and 7 apart, and marks the centre of every symbol with a heat map per class. Only the
/// staff space is taken from the reader: where the staves are is not, since on a scan the reader's
/// staves are not to be trusted. (Mirrors detect_page in train.py; keep the two alike.)
/// </summary>
static class Detector
{
    public const int Space = 16, Height = 14 * Space, Step = Height / 2, Stride = 4;
    public static readonly string[] Classes =
    {
        "none", "head_black", "head_half", "head_whole", "rest_long", "rest_4", "rest_8", "rest_16",
        "sharp", "flat", "natural", "dsharp", "dflat", "dot", "clef_g", "clef_f", "clef_c",
        "digit_1", "digit_2", "digit_3", "digit_4", "digit_5", "digit_6", "digit_7", "digit_8", "digit_9",   // a time signature's digit, by value
        "timesig_c", "flag",
        "stem_0", "stem_1", "stem_2", "stem_3",     // a stem's tip with that many beams or flags
        "barline",                                  // one per staff it crosses, at the staff's middle
        "tuplet_3",                                 // a triplet's cue-sized number
        "tie_l", "tie_r",                           // a tie's left end (at the head it starts from) and right end
        "slur_l", "slur_r",                         // a slur's ends, over or under its first and last heads
        // band scores (B3): a drum's cross head, slash heads black and hollow, a percent repeat of one
        // bar and of two, a TAB fret number's digits by value
        "head_x", "slash_black", "slash_hollow", "percent_1", "percent_2",
        "tab_0", "tab_1", "tab_2", "tab_3", "tab_4", "tab_5", "tab_6", "tab_7", "tab_8", "tab_9",
        // dynamics, hairpins and the pedal (S14): a dynamic's letters, a hairpin's tip and open end,
        // Ped. and its release, a pedal bracket's start and end hooks and its notch
        "dyn_p", "dyn_m", "dyn_f", "dyn_r", "dyn_s", "dyn_z", "hairpin_tip", "hairpin_open",
        "ped", "ped_up", "ped_l", "ped_r", "ped_notch",
        // articulations (S15): staccato, accent, tenuto, marcato and staccatissimo (above or below
        // the note: one class each) and the fermata
        "art_staccato", "art_accent", "art_tenuto", "art_marcato", "art_staccatissimo", "fermata",
        // the other tuplet numbers (a quintuplet's, sextuplet's and septuplet's: OmrGen --tuplets)
        "tuplet_5", "tuplet_6", "tuplet_7",
        // octave lines (S18): the sign's letters, by what they say (an 8va over a staff and an 8vb
        // under the staff above it stand in the same gap: the letters tell them apart, the position
        // alone cannot), and the hook where its dashes end
        "ott_8va", "ott_8vb", "ott_15ma", "ott_15mb", "ott_end",
        // grace notes (S21): the small head (its stem's tip is a stem_N, its accidental a sign like any
        // other) and an acciaccatura's slash through the stem or beam
        "grace_head", "grace_slash",
        // the form (S22): a repeat barline's dots (one at the staff's middle: right of the thick line
        // at a |:, left of it at a :|), a volta bracket's start (its left hook's corner, the number
        // beside it) and end (its right hook's corner), and the segno and coda signs
        "rep_open", "rep_close", "volta_l", "volta_r", "segno", "coda",
        // ornaments (S23): the trill's tr, the mordent, the prall, the turn and the reverse turn, all
        // set above the staff
        "orn_trill", "orn_mordent", "orn_prall", "orn_turn", "orn_reverseturn",
        // the rarer scripts (OmrGen --scripts2): the portato (a tenuto's line and a staccato's dot, one
        // class above or below), the stopped note's + and the pralltriller
        "art_portato", "art_stopped", "orn_prallprall",
    };
    public static double Threshold = 0.5;
    // A cross head is found down to this score, and kept under Threshold only when a stem is found
    // for it (Symbols.Find): a band scan's thin, large crosses (Fantasy's hi-hat, Time After Time's
    // shaker) are another font's, seen at 0.2-0.5, while their stems and beams are plain.
    public static double WeakCross = 0.15;
    // A percent sign likewise, kept under Threshold only at the staff's middle, in the middle of a
    // measure with no heads (one bar) or on its barline (two bars) (Reader.Repeat): Fantasy's drums'
    // sign at 0.35, its bass's two-bar sign at 0.41; one drum sign at 0.24 with detector18)
    public static double WeakPercent = 0.2;
    // A key's sharp or flat likewise, kept apart (PageSymbols.WeakSigns) and taken only on a staff whose
    // key went unread while the system's other staves read the key it would make (Signatures.AgreeWithinSystems):
    // Fantasy p05's bass, its sharp at 0.42 under five staves of one sharp
    public static double WeakKey = 0.25;
    // A whole note likewise, kept under Threshold only standing alone, as a whole note does: no stem, no
    // other head near it on its staff (Symbols.Find). A 0 with a TAB's line through it looks like a
    // whole note on a ledger line, and a detector that reads the one sees the other less surely
    // (Time After Time's keyboard, a low C at 0.29)
    public static double WeakWhole = 0.2;
    // A TAB digit likewise, kept apart and only a rival to another fret's second reading (Tab.Apply):
    // Time After Time's 6 seen as a 0 at 0.42, the head of its pitch not another digit's
    public static double WeakTab = 0.15;
    // A dot likewise, kept apart (PageSymbols.WeakDots) and taken only in the column of a surer dot
    // (Symbols.ColumnDots): Lily# lines up the dots of a chord and of the other voice, and the
    // detector sees one pushed under the other voice's second less surely (piano006's, at 0.31)
    public static double WeakDot = 0.2;
    public static bool IsWeakDot(Detection t) => t.Class == "dot" && t.Score <= Threshold;
    // A black head likewise, kept apart (Symbols.Find's weakHeads) and taken only as a grace note's: small,
    // solid, a stem by it (the ballade's grace notes, seen at 0.33-0.44: the detector learnt no grace notes)
    public static double WeakGrace = 0.25;
    public static bool IsWeakHead(Detection t) => t.Class == "head_black" && t.Score <= Threshold;
    // A grace note's head (grace_head, S21) likewise, kept apart and taken only where the ink is a small
    // solid head with a stem by it (Symbols.Find)
    public static bool IsWeakGrace(Detection t) => t.Class == "grace_head" && t.Score <= Threshold;
    // A half head likewise, kept apart and only a rival to a black head seen about as surely, where the
    // head's middle is white (Symbols.DetectedHeads): an old print's half note, a thin rim round a wide
    // hole, seen as black 0.53 and half 0.50 (kinkel19-5's scan, the bass's c,2)
    public static double WeakHalf = 0.25;
    public static bool IsWeakHalf(Detection t) => t.Class == "head_half" && t.Score <= Threshold;
    public static bool IsWeakTab(Detection t) => t.Digits != null && t.Score <= Threshold;
    // A hairpin's end likewise, kept apart (PageSymbols.WeakHairpins) and taken only where its two lines
    // are found in the ink (Expressions.Trace): a scan cut dark to black and white thickens and speckles
    // the lines, and the detector saw piano006's hairpins there at 0-0.3 while they read plainly
    public static double WeakHairpin = 0.08;
    public static bool IsWeakHairpin(Detection t) => t.Class.StartsWith("hairpin_") && t.Score <= Threshold;
    // A pedal bracket's end hook or notch and a * likewise, kept apart (PageSymbols.WeakPedals) and taken
    // only where the pedal is down (Expressions): a release where no surer one follows before the next
    // press, a hook or notch where the bracket's line runs into it in the ink, a * over WeakStar. The
    // detector saw piano002's hooks touching a ppp above them, or a natural's stem, at 0.43 and 0.21 (0.13
    // with --vary), a notch at a system's start at 0.47, and the * of piano006's page cut dark to black
    // and white at 0.29-0.48
    public static double WeakPedal = 0.1, WeakStar = 0.2;
    public static bool IsWeakPedal(Detection t) => t.Class is "ped_r" or "ped_up" or "ped_notch" && t.Score <= Threshold;
    // A rhythm slash likewise, kept apart (Symbols.Find's weakSlashes) and taken only on a one-line
    // rhythm staff, standing alone, where the ink leans as a slash does: leadsheet-collide's last
    // whole-note slash under a fermata, seen at 0.40 (S17)
    public static double WeakSlash = 0.25;
    public static bool IsWeakSlash(Detection t) => t.Class.StartsWith("slash_") && t.Score <= Threshold;
    // An octave line's sign likewise, kept apart (PageSymbols.WeakOttavas) and taken only where its dashes
    // run on from it in the ink, or a hook ends them (Ottavas): the Lab's piano-collide's 8va, seen at 0.49
    public static double WeakOttava = 0.25;
    // (its peaks are kept from this up: two of one octave count at one place, under WeakOttava each, are
    // one sign, Ottavas.Pooled)
    public static double WeakOttavaSeen = 0.15;
    public static bool IsWeakOttava(Detection t) => Ottavas.IsSign(t) && t.Score <= Threshold;
    // A sign read under this is unsure of its letters (an 8va read as an 8vb at 0.46-0.55): its hook, or
    // the ink about it, says which side its staff is (Ottavas). Up to 0.8: piano002's 15mb read as a 15ma
    // at 0.75, and its 8va over the left hand at 0.71 (since lysc 1aca41cd2), whose hooks point at their staves
    public static double UnsureOttava = 0.8;
    // A segno likewise, kept apart (PageSymbols.WeakSegnos) and taken only where the words of a jump
    // back to it ("D.S.", "D.S. al Coda", "D.S. al Fine") were read and no segno was seen sure (Form):
    // the Lab's roadsong's segno, seen by one training of the detector and under the threshold by the next
    public static double WeakSegno = 0.2;
    // A rest likewise, kept apart (PageSymbols.WeakRests) and taken only as a bar's one reading that fills
    // every voice (Fixes' "a rest half seen"): kinkel19-5's old print's eighth rests over the left hand, at 0.45
    public static double WeakRest = 0.3;
    public static bool IsWeakRest(Detection t) => t.Class is "rest_4" or "rest_8" or "rest_16" && t.Score <= Threshold;
    // A natural likewise, kept apart (PageSymbols.WeakNaturals) and taken only on a scan, just left of a
    // head at its middle that has no sign, on a piece of ink no rule named (Symbols.WeakNaturals): an old
    // print's tall naturals by the bass's chords, seen at 0.34-0.40 (kinkel19-2's scan, bars 30 and 31)
    public static double WeakNatural = 0.3;
    public static bool IsWeakNatural(Detection t) => t.Class == "natural" && t.Score <= Threshold;
    public static bool IsWeakSegno(Detection t) => t.Class == "segno" && t.Score <= Threshold;
    // A trill's "tr" likewise, kept apart (PageSymbols.WeakTrills) and taken only where a trill line's
    // wave follows it (Articulations): ledger005's line over two notes, its one element after the "tr"
    public static bool IsWeakTrill(Detection t) => t.Class == "orn_trill" && t.Score <= Threshold;
    // A clef likewise, kept apart (PageSymbols.WeakClefs) and taken only inside a staff, past its start,
    // on a clef's own shape in the ink (Signatures.MidClefs): an old print's left hand changing clef
    // within the bar, its F clefs back from the treble seen at 0.2-0.5 (Kinkel Op.7-1, Brandt 1838)
    public static double WeakClef = 0.2;
    public static bool IsWeakClef(Detection t) => t.Class.StartsWith("clef_") && t.Score <= Threshold;
    static InferenceSession session;

    public static bool Enabled => session != null;

    /// <param name="threads">ONNX Runtime's threads (0: one per core). Many readers at once want 1 each:
    /// each spinning on every core, they took ten times as long.</param>
    public static void Load(string path, int threads = 0)
    {
        var o = new SessionOptions { GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL };
        if (threads > 0) { o.IntraOpNumThreads = threads; o.InterOpNumThreads = 1; }
        session = new InferenceSession(path, o);
    }

    /// <summary>Every symbol on the page (grey, 0 = ink, w x h), with the staff space s in page pixels.</summary>
    public static List<Detection> Detect(byte[] gray, int w, int h, double s)
    {
        var found = new List<Detection>();
        if (session == null || s <= 0) return found;
        double scale = Space / s;
        int rw = Math.Max(1, (int)Math.Round(w * scale)), rh = Math.Max(1, (int)Math.Round(h * scale));
        var page = Resample(gray, w, h, rw, rh);            // ink = 1
        int w4 = (rw + Stride - 1) / Stride * Stride;
        var tops = new List<int>();
        for (int top = 0; top < Math.Max(1, rh - Height + Step); top += Step) tops.Add(top);
        var input = new DenseTensor<float>(new[] { 1, 1, Height, w4 });
        for (int k = 0; k < tops.Count; k++)
        {
            int top = tops[k];
            var buf = input.Buffer.Span;
            buf.Clear();
            for (int y = 0; y < Height && top + y < rh; y++)
                page.AsSpan((top + y) * rw, rw).CopyTo(buf.Slice(y * w4, rw));
            using var results = session.Run(new[] { NamedOnnxValue.CreateFromTensor("strip", input) });
            var heat = results.First().AsTensor<float>();
            // a band answers for its middle rows; the first for its top rows too, the last for its bottom ones.
            // Which band a peak belongs to goes by its grid row, not by its refined centre: a peak on the
            // rows where two bands meet had its centre put a little above the line by one band and a
            // little below by the other, and both kept it (a key's flat counted twice: meters004)
            int lo = k == 0 ? 0 : Step / 2, hi = k == tops.Count - 1 ? Height : Step / 2 + Step;
            foreach (var (c, fx, fy, score, gy) in Peaks(heat))
                if (gy >= lo && gy < hi)
                    found.Add(new Detection { Class = Classes[c], X = fx / scale, Y = (fy + top) / scale, Score = score,
                                              Digits = IsTab(c) ? DigitScores(heat, (int)Math.Round(fx / Stride), gy / Stride) : null });
        }
        return found;
    }

    static readonly int Tab0 = Array.IndexOf(Classes, "tab_0");
    // (tab_0 ... tab_9 only: the classes after them, S14's, are no digits)
    static bool IsTab(int c) => c >= Tab0 && c < Tab0 + 10;

    /// <summary>Each TAB digit's score around a grid point (the best over its 3x3).</summary>
    static float[] DigitScores(Tensor<float> heat, int gx, int gy)
    {
        int ph = heat.Dimensions[2], pw = heat.Dimensions[3];
        var d = new float[10];
        for (int k = 0; k < 10; k++)
        {
            float best = float.NegativeInfinity;
            for (int y = Math.Max(0, gy - 1); y <= Math.Min(ph - 1, gy + 1); y++)
                for (int x = Math.Max(0, gx - 1); x <= Math.Min(pw - 1, gx + 1); x++)
                    best = Math.Max(best, heat[0, Tab0 + k, y, x]);
            d[k] = (float)(1.0 / (1.0 + Math.Exp(-best)));
        }
        return d;
    }

    /// <summary>
    /// The peaks of the heat maps, in band pixels: local maxima over 3x3 above the threshold, each
    /// placed at the centre of mass of its 3x3 neighbourhood (the grid, a quarter of a space, is
    /// too coarse to tell a line from a space).
    /// </summary>
    static IEnumerable<(int C, double X, double Y, double Score, int GridY)> Peaks(Tensor<float> heat)
    {
        int kk = heat.Dimensions[1], ph = heat.Dimensions[2], pw = heat.Dimensions[3];
        int crossClass = Array.IndexOf(Classes, "head_x"), percentClass = Array.IndexOf(Classes, "percent_1"), percent2Class = Array.IndexOf(Classes, "percent_2");
        int sharpClass = Array.IndexOf(Classes, "sharp"), flatClass = Array.IndexOf(Classes, "flat"), naturalClass = Array.IndexOf(Classes, "natural"), wholeClass = Array.IndexOf(Classes, "head_whole");
        int dotClass = Array.IndexOf(Classes, "dot"), blackClass = Array.IndexOf(Classes, "head_black"), graceClass = Array.IndexOf(Classes, "grace_head"), halfClass = Array.IndexOf(Classes, "head_half");
        int tipClass = Array.IndexOf(Classes, "hairpin_tip"), openClass = Array.IndexOf(Classes, "hairpin_open");
        int pedEndClass = Array.IndexOf(Classes, "ped_r"), pedUpClass = Array.IndexOf(Classes, "ped_up"), notchClass = Array.IndexOf(Classes, "ped_notch");
        int slashClass = Array.IndexOf(Classes, "slash_black"), slashHollowClass = Array.IndexOf(Classes, "slash_hollow");
        int ottFirst = Array.IndexOf(Classes, "ott_8va");   // (the four signs' classes follow one another)
        int segnoClass = Array.IndexOf(Classes, "segno"), trillClass = Array.IndexOf(Classes, "orn_trill");
        int rest4 = Array.IndexOf(Classes, "rest_4"), rest8 = Array.IndexOf(Classes, "rest_8"), rest16 = Array.IndexOf(Classes, "rest_16");
        int clefG = Array.IndexOf(Classes, "clef_g"), clefF = Array.IndexOf(Classes, "clef_f"), clefC = Array.IndexOf(Classes, "clef_c");
        // (the logits straight from the tensor's buffer, the sigmoid only where a peak is: the
        // sigmoid keeps the order, so the threshold and the 3x3 maximum are tested on the logits)
        float[] all = heat is DenseTensor<float> dense ? dense.Buffer.ToArray() : heat.ToArray();
        static float Sig(float z) => (float)(1.0 / (1.0 + Math.Exp(-z)));
        for (int c = 1; c < kk; c++)
        {
            int o = c * ph * pw;
            double th = c == crossClass ? Math.Min(Threshold, WeakCross) : c == percentClass || c == percent2Class ? Math.Min(Threshold, WeakPercent)
                        : c == sharpClass || c == flatClass ? Math.Min(Threshold, WeakKey)
                        : c == naturalClass ? Math.Min(Threshold, WeakNatural)
                        : c == wholeClass ? Math.Min(Threshold, WeakWhole)
                        : c == dotClass ? Math.Min(Threshold, WeakDot)
                        : c == blackClass || c == graceClass ? Math.Min(Threshold, WeakGrace)
                        : c == halfClass ? Math.Min(Threshold, WeakHalf)
                        : c == tipClass || c == openClass ? Math.Min(Threshold, WeakHairpin)
                        : c == pedEndClass || c == pedUpClass || c == notchClass ? Math.Min(Threshold, WeakPedal)
                        : c == slashClass || c == slashHollowClass ? Math.Min(Threshold, WeakSlash)
                        : c >= ottFirst && c < ottFirst + 4 ? Math.Min(Threshold, WeakOttavaSeen)
                        : c == segnoClass ? Math.Min(Threshold, WeakSegno)
                        : c == rest4 || c == rest8 || c == rest16 ? Math.Min(Threshold, WeakRest)
                        : c == trillClass ? Math.Min(Threshold, Articulations.WeakTrill)
                        : c == clefG || c == clefF || c == clefC ? Math.Min(Threshold, WeakClef)
                        : IsTab(c) ? Math.Min(Threshold, WeakTab) : Threshold;
            // (a little under the threshold's logit, then the threshold itself on the sigmoid, as before)
            double zth = Math.Log(th / (1 - th)) - 1e-3;
            for (int y = 0; y < ph; y++)
                for (int x = 0; x < pw; x++)
                {
                    float z = all[o + y * pw + x];
                    if (z <= zth) continue;
                    float v = Sig(z);
                    if (v <= th) continue;
                    bool max = true;
                    double tot = 0, sx = 0, sy = 0;
                    for (int dy = -1; dy <= 1 && max; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int yy = y + dy, xx = x + dx;
                            if (yy < 0 || yy >= ph || xx < 0 || xx >= pw) continue;
                            float q = Sig(all[o + yy * pw + xx]);
                            if (q > v) { max = false; break; }
                            tot += q; sx += q * xx; sy += q * yy;
                        }
                    if (!max) continue;
                    // (grid point i stands for pixel i * Stride: the training peak sits at round(centre / Stride))
                    yield return (c, sx / tot * Stride, sy / tot * Stride, v, y * Stride);
                }
        }
    }

    /// <summary>
    /// The page at the detector's staff space, ink = 1: area averaging (a box filter with
    /// fractional edges, as PIL's BOX resampling in dataset.py), rows first, then columns.
    /// </summary>
    static float[] Resample(byte[] gray, int w, int h, int rw, int rh)
    {
        var rows = new float[rw * h];
        double sx = (double)w / rw, sy = (double)h / rh;
        for (int x = 0; x < rw; x++)
        {
            double a = x * sx, b = Math.Min(w, (x + 1) * sx);
            int ia = (int)a, ib = Math.Min(w - 1, (int)Math.Ceiling(b) - 1);
            for (int y = 0; y < h; y++)
            {
                double sum = 0, wt = 0;
                for (int xx = ia; xx <= ib; xx++)
                {
                    double o = Math.Min(b, xx + 1) - Math.Max(a, xx);
                    if (o <= 0) continue;
                    sum += o * gray[y * w + xx]; wt += o;
                }
                rows[y * rw + x] = wt > 0 ? (float)(sum / wt) : 255f;
            }
        }
        var outp = new float[rw * rh];
        for (int y = 0; y < rh; y++)
        {
            double a = y * sy, b = Math.Min(h, (y + 1) * sy);
            int ia = (int)a, ib = Math.Min(h - 1, (int)Math.Ceiling(b) - 1);
            for (int x = 0; x < rw; x++)
            {
                double sum = 0, wt = 0;
                for (int yy = ia; yy <= ib; yy++)
                {
                    double o = Math.Min(b, yy + 1) - Math.Max(a, yy);
                    if (o <= 0) continue;
                    sum += o * rows[yy * rw + x]; wt += o;
                }
                outp[y * rw + x] = (float)(1.0 - (wt > 0 ? sum / wt : 255.0) / 255.0);
            }
        }
        return outp;
    }
}
