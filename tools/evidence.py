#!/usr/bin/env python3
"""What the detector saw of each note read wrong (omr --evidence), over the development set.

Would a learnt second stage, judging how symbols relate, have anything to work with? Only where the
detector saw what the right reading needs and the reader still went wrong. Each wrong note is put
under the cause the scorer counted it as and one verdict:

  saw     every symbol the right reading needs is there at the reading's threshold (>= 0.5)
  weak    all there, some only under the threshold (down to the floor, 0.1)
  other   the detector plainly saw something else in its place (another stem's tip, the sign read).
          Under 'pitch': the head is seen where it was read and only its height's meaning is wrong
          (a clef in mid-bar, the staff's lines): the detector is not at fault there
  unseen  what it needs is not in the detector's output at any score
  ctx     nothing local is needed or could tell: a triplet with no number, a sign carried from
          earlier in the bar or the key, a clef or octave line with no sign seen
  staff   the note was read, on the system's other staff (missed here, extra there at its height)
  shared  no head of its own, and the truth has two notes of this height at this time: two voices on
          one printed head, nothing for a detector of heads to see
An extra note or rest (nothing in the truth): 'saw' = the detector backs it strongly, 'weak' = weakly,
'unseen' = the rules alone made it.

Positions go by the reader's own staves and bars, so a staff or a bar misplaced shows as unseen.
Scoring only: never used by the reading. Usage: py -3.12 -I tools/evidence.py [out\\audiveris] [rows.tsv]
"""
import csv
import glob
import math
import os
import sys
from collections import Counter, defaultdict

WHOLE = 13440
STRONG, FLOOR = 0.5, 0.1
HEADS = {"head_black", "head_half", "head_whole", "grace_head", "grace_hollow"}
RESTS = {"rest_long", "rest_4", "rest_8", "rest_16"}
SIGN = {1: "sharp", -1: "flat", 0: "natural", 2: "dsharp", -2: "dflat"}
# the diatonic number (c' = 28) on a staff's middle line
MID = {"treble": 34, "G": 34, "treble_8": 27, "treble_15": 20, "treble^8": 41, "bass": 22, "F": 22, "bass_8": 15,
       "alto": 28, "C": 28, "tenor": 26, "soprano": 32, "mezzosoprano": 30, "baritone": 24}


SANITY = defaultdict(lambda: [0, 0])


def table(path):
    with open(path, encoding="utf-8", newline="") as f:
        return list(csv.DictReader(f, delimiter="\t"))


def decode(ticks):
    """(basis, dots, tuplet number or 0) of a length in ticks."""
    for num, den, n in ((1, 1, 0), (3, 2, 3), (5, 4, 5), (6, 4, 6), (7, 4, 7)):
        w = ticks * num / den
        for basis in (1, 2, 4, 8, 16, 32, 64):
            t = WHOLE / basis
            for dots in range(3):
                if abs(t * (2 - 0.5 ** dots) - w) < 1e-6:
                    return basis, dots, n
    return None


def verdict_of(scores):
    """The verdict from the best score of each symbol needed."""
    if not scores:
        return "ctx"
    if min(scores) >= STRONG:
        return "saw"
    return "weak" if min(scores) >= FLOOR else "unseen"


class Bar:
    def __init__(self, r):
        self.page, self.system = int(r["page"]), int(r["system"])
        self.x0, self.x1, self.top, self.bottom = (float(r[k]) for k in ("x0", "x1", "top", "bottom"))
        self.s = (self.bottom - self.top) / 4 or float(r["s"])
        self.clef = r["clef"]
        self.part = r["part"]

    @property
    def mid(self):
        return (self.top + self.bottom) / 2


class Set:
    def __init__(self, d):
        self.dets = defaultdict(list)
        for r in table(os.path.join(d, "evidence-dets.tsv")):
            self.dets[int(r["page"])].append((r["class"], float(r["x"]), float(r["y"]), float(r["score"])))
        scored = {int(r["read"]): int(r["scored"]) for r in table(os.path.join(d, "evidence-map.tsv"))}
        self.bars = {}
        for r in table(os.path.join(d, "evidence-bars.tsv")):
            bar = scored.get(int(r["bar"]), 0)
            if bar > 0:
                self.bars[(bar, r["part"])] = Bar(r)
        self.notes = table(os.path.join(d, "evidence-notes.tsv"))

    def near(self, b, classes, x0, x1, y0, y1):
        return [t for t in self.dets[b.page] if t[0] in classes and x0 <= t[1] <= x1 and y0 <= t[2] <= y1]

    def best(self, b, classes, x0, x1, y0, y1):
        return max((t[3] for t in self.near(b, classes, x0, x1, y0, y1)), default=0.0)

    def heads_by_height(self, b, dmid):
        """The bar's heads on the staff and its ledger lines, one per spot (its best score), by diatonic number."""
        found = sorted(self.near(b, HEADS, b.x0 - 0.5 * b.s, b.x1 + 0.5 * b.s, b.top - 7 * b.s, b.bottom + 7 * b.s), key=lambda t: -t[3])
        kept, by = [], defaultdict(list)
        for t in found:
            if any(abs(t[1] - u[1]) < 0.5 * b.s and abs(t[2] - u[2]) < 0.4 * b.s for u in kept):
                continue
            kept.append(t)
            by[dmid + round(2 * (b.mid - t[2]) / b.s)].append(t[3])
        return by


def judge_bar(st, bar, part, rows, out, name, groups):
    b = st.bars.get((bar, part))
    errs = [r for r in rows if not r["row"].startswith("=")]
    # The scorer pairs notes in order, so a chord's note read at another length can come out as one
    # missed and one extra of the same pitch (kinkel10-1 ms, bar 1: eighths read as triplets, heads all
    # seen). They are that length misread: put together here, as the scorer's value or tuplet.
    for m in [r for r in errs if r["row"] == "missed"]:
        hit = next((x for x in errs if x["row"] == "extra" and x["oRest"] == m["tRest"]
                    and (m["tRest"] == "1" or (x["oD"], x["oAlter"]) == (m["tD"], m["tAlter"]))), None)
        if hit is None:
            continue
        errs.remove(m)
        errs.remove(hit)
        merged = dict(hit)
        for k in ("tRest", "tD", "tAlter", "tTicks", "tX"):
            merged[k] = m[k]
        tuplet = decode(int(m["tTicks"])) is None or decode(int(m["tTicks"]))[2] or decode(int(hit["oTicks"])) is None or decode(int(hit["oTicks"]))[2]
        merged["row"] = "tuplet" if tuplet and m["tRest"] == "0" else "value"
        merged["split"] = "1"
        errs.append(merged)
    if b is None or b.clef not in MID:
        for r in errs:
            out.append((name, bar, part, r["row"], "nobar", "reader has no such staff or clef here" if b is None else f"clef {b.clef}", r))
        return
    dmid = MID[b.clef]
    s = b.s
    y_of = lambda d: b.mid - (d - dmid) * s / 2
    heads = st.heads_by_height(b, dmid)
    read = Counter(int(r["oD"]) for r in rows if r["row"] == "=read" and r["oRest"] == "0")
    # (the measure's own check: do the notes read have a head where this script puts their height?)
    for d, n in read.items():
        SANITY[b.clef][0] += min(n, len(heads.get(d, [])))
        SANITY[b.clef][1] += n
    # the heads the read notes do not account for, by height: the strongest went to the read notes
    spare = {d: sorted(v, reverse=True)[read[d]:] for d, v in heads.items()}
    octave_sign = st.best(b, {"ott_8va", "ott_8vb", "ott_15ma", "ott_15mb"}, b.x0 - 40 * s, b.x1, b.top - 8 * s, b.bottom + 8 * s)
    others = [o for (k, p), o in st.bars.items() if k == bar and p != part and o.page == b.page and o.system == b.system]
    # the notes the other staves of the system read extra, by height: this staff's notes, read on that one
    moved = Counter()
    for o in others:
        for q in groups.get((bar, o.part), []):
            if q["row"] == "extra" and q["oRest"] == "0":
                moved[int(q["oD"])] += 1
    other_spare = []
    for o in others:
        if o.clef in MID:
            there = Counter(int(q["oD"]) for q in groups.get((bar, o.part), []) if q["row"] == "=read" and q["oRest"] == "0")
            other_spare.append({d: sorted(v, reverse=True)[there[d]:] for d, v in st.heads_by_height(o, MID[o.clef]).items()})

    def spare_head(d):
        """The best head no read note accounts for at this height (or an octave line's or the other staff's)."""
        ds = [d] + ([d + k for k in (-7, 7, -14, 14)] if octave_sign >= FLOOR else [])
        for k in ds:
            if spare.get(k):
                return spare[k].pop(0), ("" if k == d else "ottava")
        for sp in other_spare:
            if sp.get(d):
                return sp[d].pop(0), "other staff"
        for k in (d - 1, d + 1):
            if spare.get(k):
                return -spare[k].pop(0), "a step off"
        return 0.0, ""

    def rhythm_scores(r):
        """(scores, detail, contradicted) of the symbols that make the truth's length at the read note."""
        t = decode(int(r["tTicks"]))
        if t is None:
            return [], "length not decoded", False
        basis, dots, n = t
        ox, od = float(r["oX"]), int(r["oD"])
        hy = y_of(od)
        scores, why, against = [], [], False
        kind = "head_whole" if basis == 1 else "head_half" if basis == 2 else "head_black"
        hs = st.best(b, {kind} | ({"grace_head"} if basis >= 4 else {"grace_hollow"}), ox - 1.5 * s, ox + 1.5 * s, hy - 0.8 * s, hy + 0.8 * s)
        scores.append(hs); why.append(f"{kind} {hs:.2f}")
        if basis >= 2:
            want = {2: 0, 4: 0, 8: 1, 16: 2}.get(basis, 3)
            sx, lo, hi = float(r["oStemX"] or 0), float(r["oStemLo"] or 0), float(r["oStemHi"] or 0)
            if sx > 0:
                tips = {k: st.best(b, {f"stem_{k}"}, sx - 1.2 * s, sx + 1.2 * s, lo - 1.5 * s, hi + 1.5 * s) for k in range(4)}
                scores.append(tips[want]); why.append(f"stem_{want} {tips[want]:.2f}")
                top = max(tips, key=tips.get)
                if top != want and tips[top] >= STRONG and tips[top] > tips[want]:
                    # (a head with a stem each way, two voices': the tip it needs on the stem the reader did not
                    # give it is seen, lang36-3 ms bar 49; that is the wrong stem taken, not something else seen)
                    there = st.best(b, {f"stem_{want}"}, ox - 1.5 * s, ox + 1.5 * s, hy - 9 * s, hy + 9 * s)
                    if there >= STRONG:
                        scores[-1] = there; why.append(f"stem_{want} {there:.2f} on another stem")
                    else:
                        against = True; why.append(f"but stem_{top} {tips[top]:.2f}")
            else:
                why.append("no stem read")
                scores.append(0.0)
        ds = st.best(b, {"dot"}, ox + 0.3 * s, ox + 3 * s, hy - 1 * s, hy + 1 * s)
        if dots:
            scores.append(ds); why.append(f"dot {ds:.2f}")
        elif ds >= STRONG:
            against = True; why.append(f"but dot {ds:.2f}")
        if n:
            ns = st.best(b, {f"tuplet_{n}"}, ox - 6 * s, ox + 6 * s, b.top - 8 * s, b.bottom + 8 * s)
            why.append(f"tuplet_{n} {ns:.2f}")
            if ns < FLOOR:
                why.append("no number near")
        return scores, ", ".join(why), against

    for r in errs:
        row = r["row"]
        verdict, why = "ctx", ""
        if row == "missed":
            if r["tRest"] == "1":
                t = decode(int(r["tTicks"]))
                cls = {1: "rest_long", 2: "rest_long", 4: "rest_4", 8: "rest_8"}.get(t[0], "rest_16") if t else "rest_16"
                seen = sorted((u[3] for u in st.near(b, {cls}, b.x0, b.x1, b.top - 3 * s, b.bottom + 3 * s)), reverse=True)
                got = sum(1 for q in rows if q["row"] == "=read" and q["oRest"] == "1" and decode(int(q["oTicks"])) and
                          {1: "rest_long", 2: "rest_long", 4: "rest_4", 8: "rest_8"}.get(decode(int(q["oTicks"]))[0], "rest_16") == cls)
                sc = seen[got] if len(seen) > got else 0.0
                verdict, why = verdict_of([sc]), f"{cls} {sc:.2f}"
            elif moved[int(r["tD"])] > 0:
                moved[int(r["tD"])] -= 1
                verdict, why = "staff", "read as the other staff's"
            else:
                sc, how = spare_head(int(r["tD"]))
                same_time = sum(1 for q in rows if q["row"] == "=truth" and q["tRest"] == "0" and q["tD"] == r["tD"] and q["tX"] == r["tX"])
                if sc < 0:
                    verdict, why = "other", f"head a step off {-sc:.2f}"
                elif sc < FLOOR and same_time > 1:
                    verdict, why = "shared", "two voices on one printed head"
                else:
                    verdict, why = verdict_of([sc]), f"head {sc:.2f} {how}".strip()
        elif row == "extra":
            ox = float(r["oX"])
            lacking = Counter()
            for o in others:
                for q in groups.get((bar, o.part), []):
                    if q["row"] == "missed" and q["tRest"] == "0":
                        lacking[int(q["tD"])] += 1
            taken = sum(1 for q in errs[:errs.index(r)] if q["row"] == "extra" and q["oRest"] == "0" and q["oD"] == r["oD"])
            if r["oRest"] == "0" and lacking[int(r["oD"])] > taken:
                out.append((name, bar, part, row, "staff", "another staff's note, read on this one", r))
                continue
            if r["oRest"] == "1":
                sc = st.best(b, RESTS, ox - 1.5 * s, ox + 1.5 * s, b.top - 3 * s, b.bottom + 3 * s)
            else:
                yy = y_of(int(r["oD"]))
                sc = st.best(b, HEADS, ox - 1.5 * s, ox + 1.5 * s, yy - 0.6 * s, yy + 0.6 * s)
            verdict, why = verdict_of([sc]), f"backed {sc:.2f}"
        elif row in ("value", "tuplet"):
            if r["tRest"] == "1":
                t = decode(int(r["tTicks"]))
                cls = {1: "rest_long", 2: "rest_long", 4: "rest_4", 8: "rest_8"}.get(t[0], "rest_16") if t else "rest_16"
                ox = float(r["oX"])
                sc = st.best(b, {cls}, ox - 1.5 * s, ox + 1.5 * s, b.top - 3 * s, b.bottom + 3 * s)
                verdict, why = verdict_of([sc]), f"{cls} {sc:.2f}"
            else:
                scores, why, against = rhythm_scores(r)
                verdict = "other" if against else verdict_of(scores)
                t = decode(int(r["tTicks"]))
                if t and t[2] and "no number" in why and verdict == "saw":
                    verdict = "ctx"     # the notes are plain; only the bar's count can tell they are a triplet
        elif row in ("accidental", "key"):
            ox, od = float(r["oX"]), int(r["oD"])
            yy = y_of(od)
            want = st.best(b, {SIGN.get(int(r["tAlter"]), "natural")}, ox - 5 * s, ox - 0.2 * s, yy - 0.8 * s, yy + 0.8 * s)
            read_sign = st.best(b, {SIGN.get(int(r["oAlter"]), "natural")}, ox - 5 * s, ox - 0.2 * s, yy - 0.8 * s, yy + 0.8 * s)
            why = f"{SIGN.get(int(r['tAlter']))} {want:.2f}, read's {SIGN.get(int(r['oAlter']))} {read_sign:.2f}"
            verdict = "saw" if want >= STRONG else "other" if read_sign >= STRONG else "weak" if want >= FLOOR else "ctx"
        elif row == "pitch":
            sc, how = spare_head(int(r["tD"]))
            if sc < 0:
                verdict, why = "other", f"head a step off {-sc:.2f}"
            else:
                verdict, why = verdict_of([sc]), f"head {sc:.2f} {how}".strip()
                if verdict == "unseen":
                    ox = float(r["oX"]) if r["oX"] else 0
                    yy = y_of(int(r["oD"]))
                    q = st.best(b, HEADS, ox - 1.5 * s, ox + 1.5 * s, yy - 0.6 * s, yy + 0.6 * s)
                    if q >= STRONG:
                        verdict, why = "other", f"head where read {q:.2f}"
        elif row == "octave":
            verdict, why = ("saw" if octave_sign >= STRONG else "weak" if octave_sign >= FLOOR else "ctx"), f"octave sign {octave_sign:.2f}"
        elif row == "clef":
            clefs = st.near(b, {"clef_g", "clef_f", "clef_c"}, b.x0 - 60 * s, b.x1, b.top - 2 * s, b.bottom + 2 * s)
            read_cls = {"G": "clef_g", "t": "clef_g", "F": "clef_f", "b": "clef_f", "C": "clef_c", "a": "clef_c"}.get(b.clef[0], "")
            sc = max((u[3] for u in clefs if u[0] != read_cls), default=0.0)
            verdict, why = verdict_of([sc]) if sc >= FLOOR else "ctx", f"another clef {sc:.2f}"
        out.append((name, bar, part, row, verdict, why, r))


def main():
    root = sys.argv[1] if len(sys.argv) > 1 else os.path.join("out", "audiveris")
    out = []
    for d in sorted(glob.glob(os.path.join(root, "dev-*"))):
        own = os.path.join(d, "scored-own")
        if not os.path.exists(os.path.join(own, "evidence-notes.tsv")):
            continue
        name = os.path.basename(d)[4:]
        st = Set(own)
        groups = defaultdict(list)
        for r in st.notes:
            groups[(int(r["bar"]), r["part"])].append(r)
        for (bar, part), rows in sorted(groups.items()):
            if any(not r["row"].startswith("=") or r["row"] == "=truth" for r in rows):
                judge_bar(st, bar, part, rows, out, name, groups)
    print("read notes in wrong bars with a head at their computed height, by clef: " +
          ", ".join(f"{c} {v[0]}/{v[1]}" for c, v in sorted(SANITY.items())))
    verdicts = ["saw", "staff", "shared", "weak", "other", "unseen", "ctx", "nobar"]
    causes = ["missed", "extra", "value", "tuplet", "accidental", "key", "pitch", "octave", "clef"]
    for label, keep in (("all", lambda n: True), ("ms", lambda n: n.endswith("-ms")), ("scan", lambda n: n.endswith("-scan"))):
        rows = [o for o in out if keep(o[0])]
        print(f"\n{label}: {len(rows)} wrong notes in {len(set(o[0] for o in rows))} sets")
        print(f"{'':12}" + "".join(f"{v:>8}" for v in verdicts) + f"{'total':>8}")
        for c in causes:
            cnt = Counter(o[4] for o in rows if o[3] == c)
            print(f"{c:12}" + "".join(f"{cnt[v]:8}" for v in verdicts) + f"{sum(cnt.values()):8}")
        cnt = Counter(o[4] for o in rows)
        print(f"{'total':12}" + "".join(f"{cnt[v]:8}" for v in verdicts) + f"{sum(cnt.values()):8}")
        nonextra = [o for o in rows if o[3] != "extra"]
        if nonextra:
            n = Counter(o[4] for o in nonextra)
            print(f"without extra: saw {100 * n['saw'] / len(nonextra):.1f}%, saw+weak {100 * (n['saw'] + n['weak']) / len(nonextra):.1f}%, ctx {100 * n['ctx'] / len(nonextra):.1f}%, unseen {100 * n['unseen'] / len(nonextra):.1f}%, other {100 * n['other'] / len(nonextra):.1f}%")
    if len(sys.argv) > 2:
        with open(sys.argv[2], "w", encoding="utf-8", newline="") as f:
            w = csv.writer(f, delimiter="\t")
            w.writerow(["set", "bar", "part", "cause", "verdict", "why", "tRest", "tD", "tAlter", "tTicks", "oRest", "oD", "oAlter", "oTicks", "oX"])
            for name, bar, part, row, verdict, why, r in out:
                w.writerow([name, bar, part, row, verdict, why] + [r[k] for k in ("tRest", "tD", "tAlter", "tTicks", "oRest", "oD", "oAlter", "oTicks", "oX")])


if __name__ == "__main__":
    main()
