# 印刷してスキャンする生成譜（正解つきの本物の劣化）

目的: 合成の劣化（`OmrDegrade`）が本物の印刷・スキャンにどれだけ似ているかを、正解つきで比べる。
S9（ROADMAP §4）の材料。

## 中身

各フォルダに、生成譜の `.lys`（Lily# の元）、`.pdf`（印刷用）、`.svg`（記号単位の正解）、`.notes`（音符単位の正解）、`.score`。
6 曲: basic003、chords006、ledger007、piano002、ties004、voices2006。

## お願い（ユーザー）

1. 各 `.pdf` を **等倍（拡大縮小なし、「実際のサイズ」）** で A4 に印刷する。
2. **300 dpi、グレースケール（または白黒写真）**、傾き補正や自動トリミングは **切って** スキャンする。
3. 画像を `data/print/<曲名>/scan-p01.png`（複数ページなら `scan-p02.png` …）として置く。PNG か TIFF。JPEG は避ける。
4. できれば 1 曲だけ、別の設定（200 dpi、白黒 2 値）も撮って `scan200-p01.png`、`scanbw-p01.png` として置く。

## 届いたもの（2026-09-28）

- `basic003_scanned.pdf`、`chords006_scanned.pdf`: 白黒 2 値スキャン（五線が途切れる、読みにくい）。
- `ledger007/piano002/ties004/voices2006_scanned.jpg`: カラースキャン。
- `tools/train-model/.venv/Scripts/python tools/scan-import.py <file>` で `<曲>/scan/p01.png`（グレー、正立）にする。
  `tools/run-real.ps1` がこれを読み、check.ps1 が `real.scan-<曲>` で見張る。

## 使い方（OMR 側）

- 傾き（0.3〜0.45°）は `Img.Load` が補正する。SVG の正解を当てるには、さらに拡大縮小とずれの位置合わせが要る（未実装）。
- 合成の劣化のパラメータ（にじみ、紙の濃淡、粒状ノイズ、2 値化）を、スキャンの線の太さ・背景の分布に合わせる。
