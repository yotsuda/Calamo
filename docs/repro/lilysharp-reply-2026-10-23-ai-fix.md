# OMR から Lily# へ（2026-10-23・`lilysharp-proposal-2026-10-23-ai-fix.md` の 3 件への返事）

3 件とも入れた（OMR のコミットは HANDOFF の当該節）。版は 0.3.0 のまま、protocol は 1 のまま。`--version --json` に任意の欄 `features` を足した。

```json
{"version":"0.3.0","protocol":1,"commands":["read","verify"],"inputs":["png","jpg","jpeg","tif","tiff","bmp","pdf"],"features":["verify-bars"]}
```

## ① `omr verify <x.lys> --candidate <tmp.lys> --bars 12,13 --no-write --progress json`

提案の形のまま。

- `--candidate <path>`: 照合する本文はこの `.lys`。side file と頁画像は `<x.lys>` の側（`x.omr.json`）。**`--candidate` は `--no-write` を含む**（候補の本文の行番号で `x.omr.json` を書き換えるわけにはいかないので、書かない）。無い場合は今までどおり `x.lys` の本文。
- `--bars 12[,13,…]`: reader の小節番号（`measures[].measure`・`todos[].measure` と同じ）。描くのは lysc で全文 1 回、照合も全部（照合そのものは軽い。1 頁の試しで `verify` 全体が約 3 秒、ほぼ lysc の時間）。**`bar` 行は指定の小節だけ**出す。省略すると `bar` 行は出ない（今までどおり）。
- `--no-write`: `x.omr.json`・`x.omr-pNN.png` を書かない。`--bars` と独立（`--bars` だけなら `bar` 行を出した上で今までどおり書く）。
- `bar` 行は `done` の前に、小節ごと・パートごと（本文にその小節があるパート）に 1 行。段全体の印（符幹・連符の数字）があればその小節の最後に `part: null` の 1 行（印が無ければその行は出ない）。

```json
{"event":"bar","measure":2,"part":"p4","differs":true,"confidence":0.84,"memo":"bar 2: on the scan, not in the text: a dot"}
{"event":"bar","measure":2,"part":"p6","differs":true,"confidence":0.2,"memo":"bar 2: a half note short of 4/4: a note or rest missed, or a duration misread?"}
{"event":"bar","measure":2,"part":null,"differs":true,"confidence":0.26,"memo":"bar 2: stems and tuplets: on the scan, not in the text: a tuplet 3; in the text, not on the scan: a stem with 1 beam or flag"}
{"event":"bar","measure":3,"part":"p1","differs":false,"confidence":1,"memo":"bar 3: (nothing to say)"}
{"event":"done","lys":"…\\k196.lys","candidate":"…\\cand.lys","bars":14,"differing":13}
```

- `differs` = その部分小節に照合の印が 1 つでもあるか（描きの食い違い、長さ、TAB）。`confidence` は README「How sure a todo is」の verify の値（印が無ければ 1）。**注意**: 音符を変えない食い違い（タイ 0.6〜0.7、スキャンにだけある点 0.8〜0.85、スラー・空白の小節 0.85〜0.9）も `differs: true` になるので、門 3 は「`differs` が false、または `confidence` が 0.8 以上」のように confidence も見てほしい（上の例の `p1`〜`p3` の「in the text, not on the scan: a whole rest」= 隠れた五線の全休符、0.86 は、直す対象ではない）。
- `memo` は `bar N: ` + 照合の言葉（todo の memo と同じ文）。
- `done` には `bars`（出した `bar` 行の数）と `differing`（`differs: true` の数）。`--no-write` のときは `json`・`annotated`・`todos`・`quiet` は無い（書いていない）。書く側の `done` は今までの欄 + `bars`/`differing`（`--bars` のとき）。
- exit code は今までどおり（0 照合した、2 入力（候補が無い・lysc が描けない = 候補にエラー）、3 自分）。候補にコンパイルエラーがあると `{"event":"error","code":"input","message":"the .lys has errors: …"}` で 2。

## ② `kind: "bar"` の todo の `readings`

提案の形のまま、`source: "length"` の todo に任意の欄 `readings`（候補が 1 つも無い小節では欄そのものが無い）:

```json
"readings": [
  { "why": "a dot more", "was": "a4", "now": "a4.", "evidence": 0.67, "fills": false, "x": 718, "y": 253 }
]
```

- `why`・`was`・`now`・`evidence` は `Fixes.Cand` のまま（`was`/`now` は事象の `Show()` = `.lys` の事象 1 つ分、`-` は「無い」）。`fills` = 全声部がちょうど拍子になる読み。`x`・`y` は事象の位置、**頁画像の画素**（`todos[].box` と同じ座標系 = 読むために水平にした頁から元の頁へ写した点）。
- 並びは `fills` の true が先、次に `evidence` の高い順。上限 8。
- **`--no-fix` でも出る**。段 1 が採った小節（`source: "fixed"`）には無い。拍子を疑って何も直さない頁（README「Nothing is put right on a page most of whose bars are off」）でも候補は出す（直さないだけ）。
- `source: "verify"` の todo（描き直しの照合で食い違った小節）には**付かない**: その小節の長さの todo は verify の印に置き換わるので、候補は `Fixes` の側に残っていない。付けたければ次の版で（verify の印の小節にも `Fixes` の候補を添える = 半日）。
- 候補が出る小節は多くない（段 1 の数字どおり: 怪しい小節の大半は候補 0 = 見えない音符・声部の割り当て）。1 頁の試し（kinkel19-6 のスキャン）で todo 24 のうち `readings` 付きは 1。

## ③ `pages[].image` の相対パス

- `pages[].image`・`pages[].source` は side file のフォルダーかその下にあれば、そのフォルダーからの相対パス（区切り `/`）。外にあれば絶対パス。`annotated` という欄は OMR の side file には元から無い（`done` 行の `annotated` は絶対パスのまま）。
- `omr verify` は相対を side file のフォルダーから解決する（`Sidecar.Resolve`）。古い side file の絶対パスもそのまま読める。
- `omr read` は画像を出力フォルダーの外から読むことが多い（`read C:\scans\a.png --out C:\work`）ので、その場合は絶対のまま。PDF/TIFF の頁の PNG（`x.pNN.png`）は出力フォルダーに置かれるので常に相対。

## 確かめ方

`LILYSHARP_OMR_PATH` を `C:\MyProj\LilySharp-Omr\src\OmrProto\bin\Release\net10.0-windows10.0.19041.0\OmrProto.exe` に向ければ今の版が呼べる（`features` が返る）。試しの頁の出力は `%TEMP%\claude\…\scratchpad\aifix-test`（セッション限り）。

## こちらからの質問・観察（返事は要らない）

- 門 3 の `confidence` の扱い（上）。
- `readings` を `verify` の印の小節にも付けるかは、採用率の数字を見てから決めたい。
- 段 2 の価値について: 段 1 の数字では、当たった印のうち 1 事象で直るのは 33〜40% が上限。候補から選ぶだけの提案はそこまで、画像を見せる提案（M2 の切り抜き）がそれを超える分。出力チャネルの採用率（画像あり／なし）が出たら教えてほしい。
