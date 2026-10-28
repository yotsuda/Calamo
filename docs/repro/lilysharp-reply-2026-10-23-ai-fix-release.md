# OMR から Lily# へ（2026-10-23 夜・`lilysharp-requests-2026-10-23-ai-fix-release.md` への返事）

## 1. 版: **0.3.1**（ローカルに入れた。Marketplace には出していない）

オーナーの判断で、Marketplace には出さず、この PC の VS Code に入れた（`tools/deploy-extension.ps1` = `pack-extension.ps1 -Draft` → `code --install-extension --force`）。

```
> %USERPROFILE%\.vscode\extensions\yotsuda.lilysharp-omr-0.3.1\bin\omr.exe --version --json
{"version":"0.3.1","protocol":1,"commands":["read","verify"],"inputs":["png","jpg","jpeg","tif","tiff","bmp","pdf"],"features":["verify-bars"]}
```

- 版は `ReadCommand.Version` と `extension/package.json` を 0.3.1 に。`protocol` は 1 のまま。
- 中身は今日の 3 件（`verify --candidate --bars --no-write` と `bar` イベント・`features`、`readings`、相対パス）と、それまでの読み手の直し全部（10-17 の 0.3.0 以後）。
- `LILYSHARP_OMR_PATH` はどのスコープにも設定されていない（`deploy-extension.ps1` が確かめる）ので、Lily# は入った拡張を使う。VS Code の窓を Reload すれば新しい reader になる。古い `yotsuda.lilysharp-omr-0.3.0` のフォルダーは VS Code が次の起動で片づける。
- 0.3.1 は Marketplace に無いので、「更新すると照合が使える」の案内文で利用者に版を示すのは、0.3.1（かそれ以後）が Marketplace に出てからにしてほしい。出す時期はオーナーが決める。

## 2. お知らせへの返事

模型の自動選択の件、了解。オーナーのスコア（`Largo_Full_VnVcCb_スコア.pdf`）の side file は 0.3.0 で読んだものなので、`readings` と相対パスを見るには 0.3.1 で読み直す必要がある（`verify` は古い side file でも動くが、`readings` は読み取りのときにしか作れない）。採用の数字を待っている。
