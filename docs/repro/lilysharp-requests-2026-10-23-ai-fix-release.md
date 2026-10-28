# Lily# から OMR へ（2026-10-23 夜・お願い 1 件とお知らせ: AI 修正の実機の試しから）

`lilysharp-reply-2026-10-23-ai-fix.md`（3 件とも入れてもらった）の後、オーナーが VS Code で「Fix OMR Marks with AI…」を初めて実機で回した。その結果から。

## 1. お願い: 今日の 3 件を Scan Import 拡張の新しい版で出してほしい（版の番号を上げて）

オーナーの機械で動いているのは Marketplace から入った拡張 `yotsuda.lilysharp-omr-0.3.0` の `bin\omr.exe`（2026-10-17 12:40 の build）。

```
> omr --version --json
{"version":"0.3.0","protocol":1,"commands":["read","verify"],"inputs":["png","jpg","jpeg","tif","tiff","bmp","pdf"]}
```

`features` が無いので、Lily# は照合の門（門 3）を出さない（出力チャネルに `gate verify: the reader at …\yotsuda.lilysharp-omr-0.3.0\bin\omr.exe (0.3.0) has no 'verify-bars' — not run`）。この reader で読んだ本（オーナーの `Largo_Full_VnVcCb_スコア.pdf` → `…スコア-6.lys`、5 頁・todo 6）の side file も今日の前の形だった:

- `pages[].image` は絶対パス（`c:\Users\ytsud\OneDrive\Documents\Largo_Full_VnVcCb_スコア-6.p01.png`）。PDF の頁の PNG は出力フォルダーに在るので、今日の版なら相対になるはず。
- `source: "length"` の todo 6 つ（`b27-p2`・`b45-p1`・`b45-p2`・`b46-p2`・`b49-p2`・`b54-p1`）に `readings` が無い。

**お願い**: 今日の 3 件（`verify --candidate --bars --no-write` と `bar` イベント・`features`、`readings`、相対パス）を入れた拡張を Marketplace に出してほしい。そのとき **`version` を上げてほしい**（例 `0.3.1`）。今は 10-17 の reader も今日の reader も `0.3.0` と名乗るので、Lily# は版の数で見分けられない。

- Lily# は機能の有無を `features` で見ているので、版を上げなくても門 3 は正しく出し分けられる。版が要るのは、**古い reader に「更新すると照合が使える」と案内するため**。Lily# の `omrCore.OMR_MIN_VERSION`（いま `0.3.0`）は読み込み全体の床なので、これは上げない。代わりに AI 修正だけが「`verify-bars` が無い reader には更新を一度だけ勧める」形にする予定で、その文に新しい版の番号を書きたい。
- `protocol` は 1 のまま、で同意。

出したら、版の番号を返事で教えてほしい。Lily# 側は、その版を入れた VS Code で照合の門（MANUAL-CHECKS §5 B11）を確かめる。

## 2. お知らせ（OMR 側に直すものは無い）: 最初の実機の試しは、模型の自動選択の不具合で空振りだった

3 回とも、どの小節も 0.1 秒で「no answer」だった。原因は Lily# 側にあった。

- 模型の自動選択が `copilotcli`（Copilot CLI のエージェントが VS Code に登録する口。family が空で、入力の上限が最大）を選んでいた。この口は普通の問いに空を返す。
- Lily# `a3a7b0eb3` で直した。自動選択は family のある会話用の模型だけを候補にし、GitHub Copilot の模型を先にする。空の返事は「`<模型> returned nothing`」と名指しし、最初の小節が空ならそこで止める。

このため、そちらの返事の最後の問い（採用率、画像あり／なし）に答える数字は**まだ無い**。今回の 3 小節（`b49-p2`・`b45-p1`・`b54-p1`）は、どれも「上の声部が 2 分音符以上短い」の多声の小節だった。段 1 の数字どおり 1 事象では直らない型で、画像を見せた提案が効くかを見るのにちょうどよい。オーナーが模型を直して回し直したら、出力チャネルの集計（`N bars · P proposals · scan on|off · verify N ✓ M ✗`）と採用の数を返す。

## 3. 返事

`docs/repro/lilysharp-reply-2026-10-23-ai-fix-release.md` で。版の番号だけなら REQUESTS.md の行に一言でもよい。
