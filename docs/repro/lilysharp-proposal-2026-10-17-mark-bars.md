# Lily# から OMR へ（2026-10-17・お願い 1 件: todo が覆うパート小節を side file に）

## 1. 何があったか

オーナーの報告（2026-10-17）: 「`.lys` のプレビューの赤い破線は必ず 3 パート全部を囲む。scan 画像では 1 パート、あるいは 2 パートだけ囲む。なるべくピンポイントに、両方で同じ部分を囲んでほしい」。

原因は Lily# 側にあった。プレビューは todo の**印刷上の小節番号**だけを受け取り、段の小節（全パートを貫く箱）を囲んでいた。scan 側の箱は reader が `Sidecar.BoxOf` で決めたもの＝音符の印はその符頭、長さの印は**拍が合わないパート**の小節箱の和、符尾・連符の印は段全体、他はそのパートの小節箱。

Lily# はこれを直した（commit `bc612cd3a`）。**印が覆う範囲＝パート小節の集合**を一つの関数 `coverageOf(todo, side)` で決め、プレビューはその各パート小節が書かれた行（`measures[].line`、編集に追従する anchor 付き）に最初の音符がある五線の小節を囲む。集合は side file の既存の欄から復元している:

| todo | 復元に使う欄 | 覆う集合 |
|---|---|---|
| 長さの印（`length`） | `parts[].part` | 拍が合わないパートの、その小節 |
| パートの印 | `part` | そのパートの、その小節 |
| 符尾・連符の印 | `part: null`・`parts` なし | その小節の全パート |

0.3.0 の side file のままで両側が揃う。**今のままでも困ってはいない。**

## 2. お願い（任意・次の版で）

復元は「`BoxOf` がどう箱を作るか」を Lily# が知っている前提で、side file に書かれていない取り決え（10-14 の `staff` のときと同じ形の弱さ）。`BoxOf` が合わせるパートを変えたり、新しい種類の印（例: 小節線をまたぐタイ＝2 小節）を足したりすると、Lily# の枠が黙ってずれる。

そこで `todos[]` の各項目に、任意の欄として**覆うパート小節の列挙**を足してほしい:

```json
{ "key": "b4", "kind": "bar", "measure": 4, "part": null, "lines": [32, 33],
  "parts": [{ "part": "rh", "delta": "-1/4" }, { "part": "lh", "delta": "1/8" }],
  "bars": [{ "measure": 4, "part": "rh" }, { "measure": 4, "part": "lh" }],
  "page": 1, "box": [1292, 900, 2223, 1203], … }
```

- `bars` = **`BoxOf` が箱の和を取った (bar, part) の組そのもの**。`Sidecar.Write` の `lines` を出している式（`spans.Where(kv => kv.Key.Bar == t.Bar && (t.Lengths != null ? t.Lengths.Any(l => l.Part == kv.Key.Part) : t.Part < 0 || kv.Key.Part == t.Part))`）が列挙している key を、行番号にせずそのまま `{ measure, part }` で書けばよい。段全体の印は全パートを並べる（`part` を省いた `{ "measure": 4 }` でも Lily# は「その小節の全パート」と読む）。
- 音符の印（`kind: note`）も `[{ measure, part }]` 1 つ。
- `parts`・`part`・`lines`・`box` は今のまま（`parts` は memo とクイックフィックスが使う）。
- **版は上げなくてよい**。Lily# は `bars` があればそれを優先し、無ければ上の表で復元する（もう入っている・`parseSideFile` は知らない欄を読み飛ばすので古い Lily# も困らない）。
- README の `measures` の段の後に 1 文（`bars`: the part-bars the todo's box is the union of）を足してもらえると助かる。

## 3. Lily# 側の受け口

`ScanTodo.bars?: { measure, part? }[]` と `coverageOf` の `bars` 優先はもう入っている（`editors/vscode/src/omrScanCore.ts`・網 `test/omrScanCore.test.ts`「knows the part-bars a mark covers」）。入った版を教えてもらえれば、オーナーのスキャン 1 枚で `bars` を書いた side file を読ませて確かめる。
