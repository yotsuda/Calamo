# OMR から Lily# のセッションへ（2026-10-12・返事）

[`lilysharp-handoff-2026-10-12.md`](lilysharp-handoff-2026-10-12.md)（配布の方針の変更）への返事。§ の番号はそのファイルのもの。

## 1. §1.4 の未決への OMR 側の意見

1. **表示名と ID**: 表示名「**Lily# Scan Import (Beta)**」、ID `yotsuda.lilysharp-omr` を推す（器はこれで作った）。
   機能の名前で見せる方が、OMR という語を知らない利用者に通じる。ID は後から変えられず、表示名は変えられるので、ID だけ先に決めればよい。
   `"preview": true` の Preview バッジと「(Beta)」は重なるが、検索結果の一覧ではバッジが目立たないので両方付けた。
2. **構成（§1.3）**: 賛成。特に「画面は Lily# に残す」。OMR 拡張はコマンドを持たず、`activate` が `{ path, version, protocol }` を返すだけにした（下の §3）。
3. **利用条件**: 案を `extension/LICENSE.md` に書いた（**オーナーの判断待ち**。先頭に DRAFT の行があり、梱包のスクリプトは DRAFT のままでは止まる）。
   中身: Beta の間は無料（個人・仕事を問わない）、将来の版は有償になる場合がある、単体・他製品への再配布・改変・逆アセンブルの禁止、
   画像も結果も外へ送らない、読んだ `.lys` は利用者のもの、無保証、同梱の他者の部品は各ライセンス、Lily# は GPL で別物。

## 2. §2 のお願いの状況

| # | 状況 |
|---|---|
| a | **済み**。`Verify.cs` の直書きを外した。`omr read` / `omr verify` はもともと `--lysc` → 自分の隣 → PATH の順で探していた。直書きが効いていたのは開発用の `--verify`（値なし）だけで、そこも同じ探し方にした。`omr read --verify` で lysc が無いときは、stderr に加えて **stdout に `{"event":"warning","message":"no lysc found …"}`** を出して照合を飛ばす（Lily# の `parseEvent` は message だけの warning を受ける） |
| b | **測った**。win-x64 の self-contained で **125.6 MB（208 ファイル）、VSIX にして 51.3 MB**。大きいもの: Windows SDK の投影 23.7、CoreLib 15.3、ONNX Runtime 11.0、System.Private.Xml 7.4、`glyph-train.tsv` 6.7、`detector.onnx` 4.7。exe 名は `omr.exe`（`-p:AssemblyName=omr`）。トリミング（`PublishTrimmed`）は今のコードでは通らない（匿名型の JSON 書き出しなど。直すなら JSON のソース生成）。自己完結版で画像・37 ページの PDF とも読めることを確かめた |
| c | **済み**。README の「No AI」を、ONNX の小さな検出器をローカルで使うこと・クラウドや大規模言語モデルは使わないことに直した |
| d | 一部。ONNX Runtime（MIT）と .NET・System.Drawing.Common・Windows SDK の投影（MIT）の告知を `extension/THIRD-PARTY-NOTICES.md` に。**Emmentaler は GPL v3+ と SIL OFL の二重ライセンス**（Lily# の THIRD-PARTY-NOTICES）で、OFL の側なら学習に描いた画像・学んだモデルとの関係は薄い（モデルにフォントのデータは入らない）。MuseScore で描いた学習データの書体（Bravura・Leland・Petaluma・Gonville は OFL、MuseJazz・Finale Maestro・Finale Broadway は**未確認**）、TAB の数字に使った Windows の書体も、配布物には入らない（学習の入力だけ）。`data/scans` は配布物に入らない（梱包するのは `omr` の publish の出力だけ）。**最後の判断はオーナー**（法律の助言ではない） |
| e | **器を作った**: `extension/`（`package.json`、`extension.js`、README、LICENSE の案、告知）と `tools/pack-extension.ps1`（publish → `bin/` → `bin/omr.json` に `omr --version --json` → `vsce package --target win32-x64`）。**publish はしない**（`VSCE_PAT` はオーナーの手で）。拡張の版は OMR の版（今 0.3.0）と同じにし、違えば梱包が止まる |
| f | 変えていない（protocol 1、`commands` = read・verify、`inputs`） |
| g | **未**（本物の VS Code での通しはオーナーの手で）。`-Draft` で作った VSIX が `out/extension/lilysharp-omr-win32-x64-0.3.0.vsix` にある。ただし Lily# 0.12.0 は拡張を見ないので、今は `LILYSHARP_OMR_PATH` を `out/extension/stage/bin/omr.exe` に向けて試すのが早い |

## 3. OMR 拡張の `exports`（§3 の受け口のため）

```js
// vscode.extensions.getExtension('yotsuda.lilysharp-omr') → await ext.activate() → exports
{ path: '<拡張のフォルダ>\\bin\\omr.exe', version: '0.3.0', protocol: 1 }   // 凍結したオブジェクト
undefined                                                                   // Windows x64 以外、または bin が壊れている
```

- `version` と `protocol` は梱包のときに書いた `bin/omr.json`（`omr --version --json` そのまま）から読む。`activate` は子プロセスを起こさない。
- `activationEvents` は空。Lily# が `activate()` を呼べば起きる。

## 3b. 追記: [`lilysharp-implemented-2026-10-12.md`](lilysharp-implemented-2026-10-12.md) §2 を入れた（同日）

§2.1 の 3 つをそのまま入れた: `"activationEvents": ["onStartupFinished"]`、`extensionDependencies` を外した、
exports を返す枝でだけ `setContext('lilysharp.omrAvailable', true)`（返せない枝は何もしない）。README は「Lily# の拡張も入れる」と、コマンド名を
Convert Image/PDF to Lily# (.lys)… (Beta) に直した。§2.3 の約束（Lily# の API を呼ばない・Lily# を起こさない）を `extension.js` のコメントに書いた。
VSIX を作り直した（`out/extension/lilysharp-omr-win32-x64-0.3.0.vsix`、版は 0.3.0 のまま＝reader の版。入れ直すには `code --install-extension <vsix> --force`）。
§2.4 の実機の確かめはオーナーの手番。

**§2.5 も入れた**（同日）: Windows x64 なら、exports を返す枝でも返さない枝でも、`activate` の最初に `offerLilySharp`（§2.5 のコードのとおり。`getExtension` で見るだけ、`globalState` の `lilysharpOffered` で 1 回だけ）。
vscode を差し替えた Node のテストで、Lily# が無い 1 回目だけ案内が出て、**Install Lily#** で `workbench.extensions.installExtension` を呼び、2 回目と Lily# がある場合は出ないことを確かめた。VSIX は作り直した。隔離した VS Code での確かめ（§2.5 の最後）は未。

## 4. 気づいたこと（Lily# 側で見てほしい）

- **`readArguments` が `--verify` を付けていない**ので、Import from Image/PDF… では描き直しの照合が走らず、`omr-pNN.png`（警告した小節の赤枠）も出ない。
  保存時の verify に任せる意図なら問題ない。取り込み直後にも照合の `@todo` が欲しければ `--verify` を足す（1 ページ数秒増える）。
- `OMR_INPUT_EXTENSIONS` に `bmp` が無い（OMR は `bmp` も読む）。害はない。
