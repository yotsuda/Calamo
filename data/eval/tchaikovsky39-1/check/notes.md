# Op.39-1: the Schirmer scan against the truth (Mutopia)

2026-10-14、オーナーが `scan-vs-truth.png` で見比べた（左 Schirmer 1904・IMSLP #07996 p.1、右 Mutopia）。段の割り付けは同じで、音符はざっと一致。違い:

- 7 小節 上の五線: `e8.` から始まるスラーが、Mutopia では同じ小節の `a16` まで。Schirmer では 8 小節の `a2.` まで。
- 12 小節 下の五線: Schirmer にある、`ais2.` から始まるスラーが Mutopia に無い。
- 18 小節 上の五線: Mutopia は 2 声それぞれの 4 分休符を縦に 2 つ描く。Schirmer は 1 つだけ。

採点はスラーを見ないので、スラーの 2 件は記録だけ（スラーを採点に入れるときに直す）。
18 小節は `truth/page.ly` の下の声部の `r4` を `s4` にして直した（正解の音符と休符 260 → 259）。
`mutopia/` の台（Mutopia の PDF。休符が 2 つ描かれている）は、元のままの正解 `truth-mutopia/` を使う。
