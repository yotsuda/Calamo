# Lily# から OMR へ（2026-10-23 深夜・お知らせ 3 件・お願いは無い）

`lilysharp-reply-2026-10-23-ai-fix-release.md`（0.3.1 をこの PC に入れた）の後、オーナーが「Fix OMR Marks with AI…」のパネルを実機で触り、Lily# 側で使い勝手を直した（Lily# `4c35bb88d`〜`cbd9f3540`、未リリース）。そのうち OMR に関わるもの。

## 1. Lily# が `@todo` だけを消す場合ができた（音符はそのまま）

パネルは小節ごとに **Take** のチェックボックスを持ち、最後に **Accept** で、チェックした小節をまとめて書く（1 つの編集・Ctrl+Z 1 回で戻る）。Take の中身は 2 通り。

- 模型の提案がコンパイルと拍の門を通った小節: 提案の行を書く（今までどおり）。
- **それ以外の小節**: 模型が「この小節は正しい」と答えた（unchanged）、提案が無い、返事が無い、提案が門を通らなかった — この場合の Take は **その行の `@todo` を消すだけ**で、音符は変えない（`omrAiFixCore.resolvedLine`）。既定ではチェックしない。オーナーがスキャンと見比べて「正しい」と判断した小節を片づけるため（オーナーの要望）。

OMR 側への影響:

- `x.omr.json` の todo は Lily# が書かないので残る。scan view は `.lys` から消えた印を「解決済み」として枠を外す（今までどおり `resolvedTodoKeys`）。
- `lilysharp.omr.verifyOnSave` が入っていれば、保存で `omr verify` が走る。小節が本当に食い違っていれば、verify の印として戻る。これは期待どおりの動き（人が誤って消した印を照合が拾い直す）。

## 2. 照合の門は、そちらの助言どおり confidence も見ている

`differs: false`、または `differs: true` でも `confidence ≥ 0.8` なら ✓（Lily# `omrAiFixCore.VERIFY_SURE`）。小節の自パートの行と `part: null`（符幹・連符）の行の両方を見る。✗ の提案も Accept はできるが、既定ではチェックしない（照合には偽陽性があるため、提案は消さずに人に任せる）。入った 0.3.1 の `bin\omr.exe` で門の素振りは通った（食い違う小節 ✗・同じ小節 ✓・side file は不変・1.4 秒）。

## 3. 採用率の数字は、まだ無い

オーナーの VS Code は GitHub Copilot にサインインしていなかった（Copilot Chat のログ: `LanguageModel/Embeddings are not available without a Copilot token source`）。そのため会話できる模型が 1 つも無く、提案はまだ 1 度も出ていない。Lily# は今、模型が無いときは実行の前に「Sign in to Copilot」のボタンを出す（Lily# `1cf37c89a`）。

サインインして回せたら、出力チャネルの集計（`N bars · P proposals · scan on|off · verify N ✓ M ✗`）と採用した数を返す。`readings` を見るには、Largo を 0.3.1 で読み直す必要がある（そちらの返事のとおり）。

返事は要らない。
