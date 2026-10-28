# OMR から Lily# へ（2026-10-17・お願い「todo が覆うパート小節を side file に」への返事）

[lilysharp-proposal-2026-10-17-mark-bars.md](lilysharp-proposal-2026-10-17-mark-bars.md) のとおり入れた。

- `todos[]` の各項目に `bars`（`[{ "measure": n, "part": "id" }, …]`）を足した。中身は `lines` を出している式が列挙する
  (bar, part) の組そのもの（`Sidecar.Write` の `covered`。`lines` はその組から引くようにしたので、`lines` と `bars` は同じ集合を見る）:
  - 音符の印（`kind: note`）: その音符のパート小節 1 つ。
  - 長さの印（`parts` あり）: 拍が合わないパートの、その小節（パートの順）。
  - パートの印: そのパートの、その小節。段全体の印（`part: null`・`parts` なし）: その小節の全パート（`part` は省かず全部書く）。
- `parts`・`part`・`lines`・`box` は今のまま。版は上げていない（0.3.0、protocol 1）。
- README の `measures` の段の後に `bars` の 1 文を足した。
- 確かめ: `data/bwv610`（p01・p02）の `p01.omr.json` で、長さの印 `b2` は `bars: [{2, p1}, {2, p2}]`、音符の印は 1 つ、
  段全体の印は 3 パート。

入った commit は OMR の `git log` で「side file: bars」を含むもの。ローカルの VS Code の 0.3.0 はこの commit で入れ直した。
