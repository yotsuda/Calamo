# Lily# から OMR へ（2026-10-24・お知らせ: lysc は Lily# の VSIX に同梱する＝案 A に決定）

`lilysharp-notice-2026-10-24-lysc.md` の続き。オーナーの判断は **A**（Lily# の VSIX の `server` に lysc を同梱）。

## Lily# 側でしたこと（Lily# 第975）

- `publish-marketplace.ps1`・`release.yml`・`deploy-extension.ps1` が、言語サーバーと**同じ** `server` フォルダーへ lysc を self-contained で出す。
  - 実測（win-x64）: 言語サーバーの 298 ファイルと lysc の 249 ファイルのうち 244 がバイト一致・食い違い 0・lysc だけのものは 5 つ（`lysc.exe` ほか、0.4 MB）。
  - 同じフォルダーから `lysc --version`・`lysc svg`・言語サーバーの起動がどれも通った。
- 拡張は前から `server\lysc(.exe)` があればそれを `--lysc` で渡す（`bundledLysc`）。今回は配るものを足しただけで、TypeScript は変えていない。
- 確かめ: Scan Import 0.3.1 の `omr.exe verify largo.lys --bars 15 --no-write --progress json` は、`--lysc` なしでは `no lysc found`（exit 2）、同梱の場所を `--lysc` で渡すと `bar 15: in the text, not on the scan: a ♯` を返した（exit 0）。

## OMR 側に頼むこと

**ない。** Lily# から呼ぶ 3 つ（取り込みの照合・保存時の照合・AI 修正の門 3）は、どれも `--lysc` に同梱の lysc を渡す。

参考までに: VS Code の外で `omr` を直接使う人には、これまでどおり PATH か `--lysc` が要る。Scan Import の README にその一言があれば親切だが、急がない。

効くのは Lily# の次の版から。それまで出ている版（0.14.0 以前）では、利用者の PATH に lysc が無いかぎり照合は走らない。

返事は要らない。
