# OMR から Lily# のセッションへ（2026-10-12・お願い 1 つ: スキャンのタブの名前）

**オーナーの報告**: Show Original Scan で開いたタブの名前が `Scan: score.lys` になる。開いているのは元の画像・PDF なので、**元のファイルの名前（拡張子つき）**を出してほしい。

## 今

`editors/vscode/src/omrScan.ts:119`: `createWebviewPanel('lilysharpScan', `Scan: ${path.basename(lys.fsPath)}`, …)` — `.lys` の名前を決め打ち。

## OMR が `.omr.json` に書いているもの（protocol 1 のまま。変えない）

`pages[]` の 1 ページごとに:

| 入力 | `image` | `source` | `sourcePage` |
|---|---|---|---|
| 画像 1 枚（png・jpg・bmp…） | **元のファイルの絶対パス** | なし | なし |
| PDF・複数ページの TIFF | 読んだページの PNG（`score.p03.png`） | **元の PDF・TIFF の絶対パス** | そのページ（1 から） |

⇒ 元のファイルは `source ?? image`。今の `omrScanCore.ts` の `ScanPage` は `source` を読んでいない（`image` だけ）。

## お願い

1. `ScanPage` に `source?: string` と `sourcePage?: number` を足して読む（`image` と同じく `.omr.json` の場所から解決）。
2. タブの名前を**元のファイルの名前**にする: ページの `source ?? image` の basename を出てきた順に重ねずに並べ、
   - 1 つ: `Scan: score.pdf`／`Scan: page1.jpg`
   - 2 つ: `Scan: page1.jpg, page2.jpg`
   - 3 つ以上: `Scan: page1.jpg +2`
   - ページが無い・読めないときは今までどおり `.lys` の名前。
3. （任意）タブのツールチップ（webview は `panel.title` だけなので、ビューの中の見出しかページの上）に `score.pdf p.3` のように元のファイルとページを出すと、PDF の何ページ目かが分かる。

テストは `omrScanCore` の側に、名前を作る関数（`scanTitle(pages, lysPath)`）を切り出して置けば `npm test` で見られる。
