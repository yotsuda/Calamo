"""S60: where two detectors (same recipe, other seeds) answer a threshold differently.

    python splits.py <dumpA> <dumpB> [--near 0.1] [--out table.tsv]

Each dump is a folder the reader filled with OMR_DUMP_DETECTIONS (Detector.Dump): one .tsv per page,
named by a hash of its pixels, every peak down to 0.05. The two runs read the same pages, so the files
pair up by name. A peak of A pairs with B's peak of the same class within half a space (each used once,
surest first); a peak with no partner counts as 0 on the other side.

For each threshold the rules read (the class set it gates, its value), the table counts
  pairs  the pairs where either side is within --near of the threshold,
  split  the pairs one side over it and the other under it,
  a_only / b_only  which side was over.
A threshold with many splits is one where the reading turns on which seed taught the detector.
"""
import argparse, collections, os, sys

# (name, value, classes) -- the thresholds named in the code (Detector.cs, Symbols.cs, Ottavas.cs,
# Articulations.cs, Symbols.Classify.cs) and the scores written as numbers, by file:line
SIGNS = ("sharp", "flat", "natural", "dsharp", "dflat")
OTT = ("ott_8va", "ott_8vb", "ott_15ma", "ott_15mb")
def cls(*names): return lambda c: c in names
def pre(*p): return lambda c: c.startswith(p)
ANY = lambda c: True
THRESHOLDS = [
    ("Detector.Threshold / Symbols.TrustScore", 0.5, ANY),
    ("Symbols.SureScore", 0.9, ANY),
    ("Detector.WeakCross", 0.15, cls("head_x")),
    ("Detector.WeakPercent", 0.2, cls("percent_1", "percent_2")),
    ("Detector.WeakKey", 0.25, cls("sharp", "flat")),
    ("Detector.WeakWhole", 0.2, cls("head_whole")),
    ("Detector.WeakTab", 0.15, pre("tab_")),
    ("Detector.WeakDot", 0.2, cls("dot")),
    ("Detector.WeakGrace", 0.25, cls("head_black")),
    ("Detector.WeakHalf", 0.25, cls("head_half", "grace_hollow")),
    ("Detector.WeakHairpin", 0.08, pre("hairpin_")),
    ("Detector.WeakPedal", 0.1, cls("ped_r", "ped_up", "ped_notch")),
    ("Detector.WeakStar", 0.2, cls("ped_up")),
    ("Detector.WeakSlur", 0.2, cls("slur_l", "slur_r", "tie_l", "tie_r")),
    ("Detector.WeakSlash", 0.25, pre("slash_")),
    ("Detector.WeakOttava", 0.25, cls(*OTT)),
    ("Detector.WeakOttavaSeen", 0.15, cls(*OTT)),
    ("Detector.UnsureOttava", 0.8, cls(*OTT)),
    ("Detector.WeakSegno", 0.2, cls("segno")),
    ("Detector.WeakRest", 0.3, cls("rest_4", "rest_8", "rest_16")),
    ("Detector.WeakNatural", 0.3, cls("natural")),
    ("Detector.WeakClef", 0.2, pre("clef_")),
    ("Articulations.WeakTrill", 0.2, cls("orn_trill")),
    ("Ottavas.WeakBareEight", 0.3, cls("ott_8va", "ott_8vb")),
    ("Symbols.WeakSign", 0.2, cls(*SIGNS, "dot")),
    ("Symbols.WeakTupletNumber", 0.3, pre("tuplet_")),
    ("Arpeggios.cs:59 rest_4 0.8", 0.8, cls("rest_4")),
    ("Expressions.cs:504 tuplet 0.3", 0.3, pre("tuplet_")),
    ("Fixes.cs:238 rest 0.15", 0.15, pre("rest_")),
    ("Symbols.cs:200 half/black 0.4", 0.4, cls("head_half", "head_black")),
    ("Symbols.Stems.cs:740 grace 0.3", 0.3, cls("head_black", "grace_head")),
    ("Symbols.Tuplets.cs:95 tuplet 0.8", 0.8, pre("tuplet_")),
]

def load(path):
    space, peaks = None, []
    with open(path, encoding="utf-8") as f:
        for line in f:
            if line.startswith("#"):
                space = float(line.split("\t")[1]); continue
            c, x, y, sc = line.rstrip("\n").split("\t")
            peaks.append((c, float(x), float(y), float(sc)))
    return space, peaks

def pair(a, b, s):
    """Pairs (class, scoreA, scoreB) of one page."""
    out = []
    by = collections.defaultdict(list)
    for i, p in enumerate(b): by[p[0]].append(i)
    used = set()
    for c, x, y, sc in sorted(a, key=lambda p: -p[3]):
        best, bd = None, 0.5 * s
        for j in by.get(c, ()):
            if j in used: continue
            d = max(abs(b[j][1] - x), abs(b[j][2] - y))
            if d < bd: best, bd = j, d
        if best is None: out.append((c, sc, 0.0))
        else: used.add(best); out.append((c, sc, b[best][3]))
    out += [(b[j][0], 0.0, b[j][3]) for j in range(len(b)) if j not in used]
    return out

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("a"); ap.add_argument("b")
    ap.add_argument("--near", type=float, default=0.1)
    ap.add_argument("--out")
    ap.add_argument("--by-class", type=float, help="instead: the splits at this score, class by class")
    o = ap.parse_args()
    names = sorted(set(os.listdir(o.a)) & set(os.listdir(o.b)))
    if not names: sys.exit("no pages in common")
    pairs = []
    for n in names:
        s, pa = load(os.path.join(o.a, n)); _, pb = load(os.path.join(o.b, n))
        pairs += pair(pa, pb, s)
    if o.by_class is not None:
        t, per = o.by_class, collections.Counter()
        tot = collections.Counter()
        for c, x, y in pairs:
            if max(x, y) >= t: tot[c] += 1
            if (x >= t) != (y >= t): per[(c, "a" if x >= t else "b")] += 1
        print(f"class\tover_either\ta_only\tb_only  (at {t})")
        for c in sorted(tot, key=lambda c: -(per[(c, 'a')] + per[(c, 'b')])):
            if per[(c, 'a')] + per[(c, 'b')]: print(f"{c}\t{tot[c]}\t{per[(c, 'a')]}\t{per[(c, 'b')]}")
        return
    rows = []
    for name, t, gate in THRESHOLDS:
        near = split = a_only = b_only = 0
        for c, x, y in pairs:
            if not gate(c): continue
            if abs(x - t) > o.near and abs(y - t) > o.near: continue
            near += 1
            if (x >= t) != (y >= t):
                split += 1
                if x >= t: a_only += 1
                else: b_only += 1
        rows.append((name, t, near, split, a_only, b_only))
    rows.sort(key=lambda r: -r[3])
    head = "threshold\tvalue\tpairs\tsplit\ta_only\tb_only"
    lines = [head] + [f"{n}\t{t}\t{p}\t{s}\t{a}\t{b}" for n, t, p, s, a, b in rows]
    print(f"{len(names)} pages, {len(pairs)} pairs")
    print("\n".join(lines))
    if o.out:
        with open(o.out, "w", encoding="utf-8") as f: f.write("\n".join(lines) + "\n")

if __name__ == "__main__":
    main()
