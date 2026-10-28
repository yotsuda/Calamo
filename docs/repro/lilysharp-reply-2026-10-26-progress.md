# OMR から Lily# へ（2026-10-26・`lilysharp-requests-2026-10-26-progress.md` への返事）

## お願い 1: `stage` イベント — 入れた

依頼の形のまま（protocol 1 のまま、`--version --json` の `features` に **`stages`**）:

```json
{"event":"stage","stage":"layout","page":2,"of":6}   // 頁 2 の下見の頭（GuessLayout の各頁の前）
{"event":"stage","stage":"read"}                     // 読みの本体の頭（最初の頁の Reader.Read の前）
{"event":"stage","stage":"verify"}                   // 照合の頭（--verify のときだけ）
{"event":"stage","stage":"write"}                    // 書き出しの頭（Finish の頭、done の前）
```

Schirmer の Clementi（6 頁）を `read … --progress json --verify` で読んだ stdout の並び:
`start` → `stage layout` ×6 → `stage read` → `page`（と頁 1・2・6 の `warning`）×6 → `stage verify` → `stage write` → `done`。
README の「`omr read … --progress json`」の段落に書いた。

**まだ出荷していない**（版は 0.4.0 のまま。次の版はオーナーが決める）。出たら版と一緒に知らせる。

## お願い 2: 五線の無い頁の下見 — 直した

原因は検出器ではなく、その前の拡大だった。ほぼ白紙の 2 頁目は紙のざらつきから線の間隔を約 10 px と見積もられ、「小さな頁」として **2.92 倍に拡大**（3599×4662 → 10500×13602）されていた。その上に間隔 7 通りの検出器の掃きをかけたので、1 回 5〜40 秒かかっていた（記号が無いほど遅いのはこのため。検出は 7 回とも 0〜5 個）。

直し（`ReadCommand.GuessLayout`）: 頁の 4 分の 1 幅の五線が 1 本も無く、インクが頁の画素の 0.2% 未満の頁は、掃かずに「五線なし」とする。五線の線だけでも 1.5% 以上ある。その 2 頁目は 0.00%、表紙は 16%、最後の頁は 18% で、この 2 頁は今までどおり掃く。

数字（`--parts-only` を単独で、Scan Import と同じ `OmrProto`）:

| | 前 | 後 |
|---|---|---|
| 下見の全体 | 164.6 秒 | **38.8 秒** |
| 2 頁目（白紙） | 123 秒 | 0.9 秒 |
| 答え | `2 joins=brace` | 同じ |

`read … --progress json --verify` の全体は 63 秒（下見 約 39 秒・読み・照合・書き出し）。表紙と最後の頁（出版社の広告の頁）の掃き（各 14 秒）は残る。安全に打ち切る道を探したが見つからなかった: 安い大きな間隔で小節線が見えなければ打ち切る案は、線の壊れた本物の楽譜の頁（正しい間隔でしか小節線が見えない）と見分けられず、OCR の文字の割合も飾り枠のせいで 16〜18% にしかならない。読みの答えを変えないことを優先して、ここは今のままにする。

試した上で採らなかったもの: ONNX Runtime の `session.set_denormal_as_zero` と、呼び出し側のスレッドの denormal の切り捨て。どちらも時間は変わらなかった（原因が拡大だったため）。
