# Lily# から OMR へ（2026-10-23・お願い 3 件: AI で小節を直す機能の M3 に向けて）

## 0. 何ができたか（お知らせ・直すことはない）

Lily# の VS Code 拡張に **「Lily#: Fix OMR Marks with AI…」** が入った（`docs/omr-ai-fix-design.md`、commit `9fa1b2b07` = M1・`c41d98176` = M2。版は未リリース、Unreleased）。ROADMAP S33 の段 2・段 3 に当たる Lily# 側の分で、REQUESTS.md「将来の依頼」の「怪しい小節の自動修正の段 2」はこれで土台ができた。

- **呼ばれたときだけ**走る（パレット・`.lys` の右クリック・`@todo` のクイックフィックス「Fix this bar with AI…」・Show Original Scan の頭の「Fix N marks with AI…」）。背景では走らない。
- 対象は **`.lys` の `@todo` のある行**（= reader がパートの小節を 1 行に書く形に乗っている）。QuickPick は確信度の低い順、scan view の *Marks below* より下の印にチェック。
- モデルに渡すもの: `.lys` の宣言部（拍子・調・パート）、**その行**、同じパートの前後の小節、**同じ小節の他パートの行**（`measures[].line` で引く）、`todos[].memo`、`confidence`、`candidates`、Lily# コンパイラの診断（LYS2001/2002）、そして **`pages[].image` を `todos[].box`（無ければその行の `measures[].box`）で切り抜いた PNG**（余白 30%・長辺 1,568 px まで）。画像を受けない模型には文字だけで送り直す。
- 門: コンパイルで新しいエラーが増えない・その小節に LYS2001/2002 が無い。通ったものだけをパネルに並べ（元画像の切り抜き／今の譜／提案の譜／理由）、**Accept した行だけを 1 つの WorkspaceEdit で書く**（`@todo` は行ごと消える）。`x.omr.json` には書かない（今までどおり Lily# は読むだけ）。
- 出力チャネル「Lily# AI」に 1 小節 1 行（秒・提案の有無・門・画像の有無）と、実行ごとの集計（`N bars · P proposals · scan on|off|refused`）。採用率（画像あり／なし）はこれで数える。

side file で**今 Lily# が読んでいる欄**（M1・M2 で増えた分に ★）: `version`・`pages[].{page,image,width,height,annotated,source,sourcePage}`・`measures[].{measure,part,line,page,box,staff}`・`todos[].{key,page,box,measure,part,memo,confidence,inSource,parts[].part,bars[],candidates[].{label,text}}`・★`todos[].kind`・★`todos[].lines`。知らない欄は読み飛ばす（`source: "fixed"` の quiet な todo は `.lys` に無いので対象にならない＝そのままでよい）。

## 1. お願い ①: `omr verify` を小節に絞って、書かずに、候補の `.lys` で（M3 の「門 3」）

今の `omr verify x.lys` は全頁・全小節を照合して `x.omr.json` を書き直す（`VerifyCommand.Check`: lysc で全部描く → `verify.Compare` → todos を作り直して `File.Move`）。AI の提案を見せる**前**に「この 1 小節の食い違いが消えるか」を訊きたいので、次の形が欲しい:

```
omr verify <x.lys> --candidate <tmp.lys> --bars 12 --no-write --progress json
```

- `--candidate <path>`: 照合する本文は **この `.lys`**（Lily# が提案を差し込んだ全文を temp に書く。まだ保存していない編集中の本が対象なので `x.lys` 自身は使えない）。side file と頁画像は `x.lys` の側（`x.omr.json`）から。無ければ今までどおり `x.lys` の本文。
- `--bars 12[,13,…]`: **reader の小節番号**（`measures[].measure`・`todos[].measure` と同じ数え方）で絞る。描くのは lysc で全文のまま（1 回）でよいが、照合は指定の小節の頁・五線だけにして数秒に収めてほしい。省略は今までどおり全部。
- `--no-write`: `x.omr.json` と `x.omr-pNN.png` を**書かない**（提案の段階の照合は side file を動かしてはいけない。採用して保存したあとは Lily# の `verifyOnSave` が今までどおり全体を回す）。
- 結果は **小節ごとに 1 行**、`done` の前に:

```json
{"event":"bar","measure":12,"part":"p1","differs":false,"confidence":0.95,"memo":"bar 12: (nothing to say)"}
{"event":"bar","measure":12,"part":"p2","differs":true,"confidence":0.31,"memo":"bar 12: 1680/1920 ticks; a head not drawn"}
{"event":"done","lys":"…","bars":2,"differing":1}
```

`differs` が false なら門 ✓、true なら memo を模型への直しの言葉に使う。`part` は todo の `part`（段全体の印は null）。exit code は今の `verify` と同じ（0 照合した・2 入力・3 自分）。

Lily# 側の受け口: `omrCore.verifyArguments` に `--candidate`／`--bars`／`--no-write` を足し、`omrAiFix.fixOne` の門 1・2 のあとに門 3 を置く（`event: "bar"` を読む・無ければ「verify —」のまま）。**`--version --json` で有無が分かるようにしてほしい**: 例 `"features": ["verify-bars"]`（無ければ Lily# は門 3 を出さない）。`protocol` は上げなくてよい（任意の欄）。

## 2. お願い ②: `kind: "bar"` の todo に、1 事象の別の読みを（M3 の「候補」）

今、長さの todo の `candidates` は常に `[]`（`Sidecar.Write` と `VerifyCommand` の `["candidates"] = new JsonArray()`）。一方で段 1 の `Fixes.cs` は、拍子を満たさない小節ごとに **1 事象の別の読み** `Cand(Why, Was, Now, Evidence)` と「全声部がちょうど拍子になるか」(`fitting`) を持っていて、**1 つだけ残ったとき**に採って `source: "fixed"` にしている。残りの場合（候補が複数・0・採れない）が、まさに AI に判断してほしい小節で、その候補は模型に渡す価値が高い（memo の「1760/1920 ticks」より具体的）。

長さの todo（`source: "length"`・`"verify"`）に、任意の欄 **`readings`** を足してほしい:

```json
{ "key": "b22-p1", "kind": "bar", "source": "length", "measure": 22, "part": "p1", "lines": [43],
  "memo": "bar 22: 1760/1920 ticks", "confidence": 0.3, "candidates": [],
  "readings": [
    { "why": "a dot more",            "was": "f8~",  "now": "f8.~", "evidence": 0.63, "fills": true,  "x": 1210, "y": 1884 },
    { "why": "a rest half seen",      "was": "-",    "now": "r8",   "evidence": 0.41, "fills": true,  "x": 1380, "y": 1890 },
    { "why": "one duration longer",   "was": "g8",   "now": "g4",   "evidence": 0.22, "fills": false, "x": 1105, "y": 1880 }
  ] }
```

- `why`・`was`・`now`・`evidence` は `Cand` の `Why`・`Was`・`Now`・`Evidence` そのまま（`Was`/`Now` は事象の `Show()` の綴り＝`.lys` の事象 1 つ分。`-` は「無い」）。`fills` = `fitting` に入ったか。`x`・`y` はその事象の頁上の位置（`Cand.X`/`Y`）＝ Lily# が切り抜きの中で指すのに使う（省略可）。
- 並びは evidence の高い順、`fills` の true を先に。上限は 8 程度で十分。
- `--no-fix` のときも書いてほしい（段 1 で採ったものは `source: "fixed"` になって `readings` は要らない）。
- `candidates`（音符の todo の「印の付いた事象の別の綴り」）はそのまま。意味が違うので欄を分けた。
- **版は上げなくてよい**。Lily# は `readings` があれば模型への文に「reader が見た別の読み: f8~ → f8.~（a dot more・裏づけ 0.63・小節が埋まる）…」と入れ、無ければ今までどおり memo だけ。

## 3. お願い ③: `pages[].image` を side file のフォルダーからの相対パスに

`Sidecar.Write` は `pages[].image` を絶対パス（`ReadCommand.Sources` の `GetFullPath`）で書く（例: `c:\MyProj\LilySharp-Omr\data\scans\揺れる思い\img_029.jpg`）。`annotated`・`source` も同様。本のフォルダーをまるごと別の場所や別の PC に移すと、scan view も AI の切り抜きも頁を見失う（オーナーは複数の PC で作業している）。

- 頁の画像・annotated・source が **side file と同じフォルダーかその下**にあるときは、そのフォルダーからの相対パス（`img_029.jpg`・`sub/p01.png`、区切りは `/`）で書いてほしい。外にあるときは今までどおり絶対パス。
- Lily# の読み手は両方読む（`omrScanCore.parseSideFile` は `path.isAbsolute(p) ? p : path.join(dir, p)`、M1 以前から）。`omr verify` 自身が `root["pages"][].image` を読むので（`VerifyCommand.Check` の `images`）、そちらも相対を解決するようにしてほしい。
- **版は上げなくてよい**（今の Lily# でそのまま動く）。

## 4. 優先と受け口

| | 欲しい順 | Lily# 側でやること |
|---|---|---|
| ① `verify --candidate --bars --no-write` ＋ `event: "bar"` ＋ `features` | 1 | `omrCore.ts`・`omrAiFix.ts` に門 3（M3）。入った版を教えてもらえればオーナーのスキャンで確かめる |
| ② `readings` | 2 | `omrScanCore.markFactsOf` と `omrAiFixCore.taskPromptOf` に 1 段（M3） |
| ③ 相対パス | 3 | 何もしない（読める） |

どれも**依頼**で、やるか・どうやるかは OMR 側（とオーナー）の判断。①の `--bars` の数え方や `event` の形はこちらの案なので、都合のよい形に変えてもらってよい（変えたら教えてほしい）。返事は今までどおり `docs/repro/lilysharp-reply-2026-10-23-ai-fix.md` で。
