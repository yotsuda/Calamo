# Lily# 側の実装: OMR の取得と呼び出し（2026-10-05）

OMR（LilySharp-Omr）を Lily# の VS Code 拡張機能から使うために、**Lily# 側で作るもの**をまとめる。
`@todo`・候補のクイックフィックス・元の画像のビューなどは前の提案 [`lilysharp-proposal-2026-10-04.md`](lilysharp-proposal-2026-10-04.md) のまま。
この文書はその **B1（画像・PDF からの取り込み）を具体化**し、**E（ライセンスの注意）の「同梱するか」に答える**。
B1 にある設定 `lilysharp.omr.path` は使わない（下の 1）。

## 0. 現時点で Lily# 側に要る範囲

| 時期 | Lily# 側で作るもの | 待っているもの（OMR 側） |
|---|---|---|
| **今すぐ（作ってよい）** | **L1・L7**: 環境変数で OMR（`OmrProto.exe`）を指して「読んで、`.lys`・プレビュー・注釈画像を開く」を通す。画像（PNG・JPEG・TIFF・BMP）なら今の OMR で試せる | OMR 側の `omr read` の第 1 歩は動く（下の 3.2 の「実装の状況」）。PDF はまだ |
| **OMR を配る段になったら** | **L2〜L6・L9**（一覧・同意・取得・確かめ・配置・オフラインのインストール）、続いて **L8・L10・L11** | OS ごとの zip、公開の置き場所、一覧と署名の鍵 |
| **有料プラン（P2）を入れるとき** | ログイン（VS Code の GitHub・Microsoft のサインイン）、利用券の取得と更新（オンラインのときに裏で）、利用券を OMR に渡すこと（環境変数かファイルの場所）、有料の機能の案内 | 利用券を出すサーバー、OMR の中の利用券の確かめ（ROADMAP §6 のログイン） |

今の Lily# 側の優先は前の提案の残り（A1 の `@todo` と SVG の `data-todo` は実装済み。C1 の LSP、C2 の小節の番号、B2 以降の進み具合は Lily# 側で確かめる）。
この文書の項目は OMR 側の進みに合わせて始めればよい。

## 1. 決まったこと（ROADMAP §5・§6）

- **同梱しない。** 拡張機能が、メニューから初めて読むときに、利用者の同意を取って OMR を取ってくる。
  GPL の「集積」として同梱もできるが、次の 3 つを避ける: OS ごとの大きさ、OMR だけを直すときに Lily# ごと出し直す更新の周期、閉じた部品が入っているという見え方。
- **取ってくるのは「Lily# が対応する OMR のうちの最新版」**（下の 3 の `protocol`）。ただの最新版ではない。
- **ログインは要らない**（無料のベータ = P1）。ログインは有料プラン（P2）を入れるときに、有料の機能にだけ求める。今は作らない。
- **OMR の場所は設定画面に出さない。** 普段は拡張機能が置いた場所を使う。開発は環境変数、オフラインはコマンド（下の L1・L9）。
- **OMR は別プログラムのまま。** 子プロセスで呼び、OMR が書いたファイルを読むだけ。OMR のコードを拡張機能に入れない（JavaScript 版を同じプロセスで動かすこともしない）。
- OMR のリポジトリは private のまま。配布物と一覧は**公開の置き場所**に置く（バイナリだけの公開のリリース用リポジトリの GitHub Releases、または静的ホスティング。どちらかは未定）。

## 2. 全体の流れ

```
メニュー「Lily#: Import from Image/PDF…」
  └ L1 OMR はある？ ── ある ──────────────────────┐
        └ ない → L2 一覧を取る → 版を選ぶ          │
               → L3 同意 → 取る → L4 確かめる      │
               → L5 置く → L6 動くか確かめる ──────┤
                                                    ▼
                                 L7 子プロセスで読む（進み具合を通知）
                                 → .lys・プレビュー・注釈画像を開く
起動時（1 日 1 回まで）: L8 更新を確かめて通知
```

## 3. OMR 側が用意するもの（取り決め）

Lily# 側の実装の前提。**OMR 側はまだ実装していない**（今の OmrProto の CLI は開発用で、`.score` と pNN.png のフォルダが要る）。形は Lily# の都合で変えてよい。

### 3.1 一覧 `omr-manifest.json` と署名 `omr-manifest.json.sig`

```json
{
  "manifestVersion": 1,
  "releases": [
    {
      "version": "0.3.0",
      "protocol": 1,
      "minLilySharp": "1.4.0",
      "date": "2026-11-01",
      "notes": "…",
      "assets": {
        "win-x64":     { "url": "https://…/omr-0.3.0-win-x64.zip",     "sha256": "…", "size": 16800000 },
        "osx-arm64":   { "url": "https://…/omr-0.3.0-osx-arm64.zip",   "sha256": "…", "size": 0 },
        "linux-x64":   { "url": "https://…/omr-0.3.0-linux-x64.zip",   "sha256": "…", "size": 0 }
      }
    }
  ]
}
```
- `protocol` は CLI と出力の取り決めの版（整数）。引数・stdout・`.omr.json` の形を互換でなく変えたら上げる。
- `minLilySharp` は、その OMR が要る Lily# の最低の版（例: `@todo` を書く OMR は `@todo` を読める Lily# が要る）。
- `.sig` は一覧のバイト列への Ed25519 の分離署名（base64）。秘密鍵は OMR 側だけが持つ。
- 当面の `assets` は `win-x64` だけ（OmrProto は `net10.0-windows10.0.19041.0`。他の OS は System.Drawing を置き換えた後）。

### 3.2 CLI

**OMR 側の実装の状況（2026-10-05、第 1 歩）**: `OmrProto.exe read …` と `OmrProto.exe --version --json` が動く（exe の名前を `omr` にするのは発行のとき）。
`--out`・`--name`・`--lysc`・`--verify`・`--progress json`・終了コード・stdout の JSON の行・一時名からの名前の変更は下のとおり。
まだのもの: TAB・1 本線の譜表のある楽譜のパートの推測（5 本線の譜表だけ）。`.lys` への `@todo` は 2026-10-05 に入った。
注釈画像は照合（`--verify`）が警告した小節のあるページだけ。
**同じ名前の `.lys`（か `.omr.json`）が `--out` にあれば上書きしない**: `x-2.lys`・`x-2.omr.json`、`x-3…` と別名にする（直した `.lys` を、同じ画像を読み直して消さないため）。`done.lys` が実際の名前。

**PDF（2026-10-05 夜、版 0.2.0）**: Windows に入っている PDF の描画（`Windows.Data.Pdf`、ライブラリを足さない）で各ページを描いて読む。
- 生まれつきデジタルな PDF は 300 dpi。**スキャンの PDF**（どのページにも 1 メガピクセル以上の画像がある）は**埋め込まれた画像の解像度のまま**（150〜600 dpi に収める）描く。
  描き直すと読みが落ちる（Peters の 274 dpi を 300 dpi に描くと 88.9 → 85.1%、元の解像度なら 88.9%）。
- 描く速さは 300 dpi で 1 ページ約 0.25 秒、600 dpi で約 1 秒。`start` の行は全ページを描いてから出る（34 ページの 600 dpi のスキャンで約 35 秒、進み具合の行はない）。
- 壊れた PDF は終了コード 2（パスワードのかかった PDF も開けずに 2 になるはずだが、試していない）。
- **`.omr.json` の `pages[].image` は表示できる画像**（Lily# からの依頼 ⑴）: PNG・JPEG・BMP の入力はそのファイル、**PDF と TIFF のページは読み取りが使った PNG を `--out` に `<base>.pNN.png` として置き**、
  `image` はそれを指す。そのページには `source`（入力の PDF・TIFF）と `sourcePage`（そのファイルの何ページ目か、1 から）も付く。`todos[].box`・`measures[].box` はこの PNG の画素。
  ```
  "pages": [ { "page": 1, "image": "…/x.p01.png", "width": 2480, "height": 3508, "source": "…/x.pdf", "sourcePage": 1 } ]
  ```
- `omr --version --json` の `inputs` に `pdf` が入った（`{"version":"0.2.0","protocol":1,"inputs":["png","jpg","jpeg","tif","tiff","bmp","pdf"]}`）。取り決めは互換のまま（`protocol` は 1）。
- OmrProto の TFM が `net10.0-windows10.0.19041.0` になり、**exe の場所が `src\OmrProto\bin\Release\net10.0-windows10.0.19041.0\OmrProto.exe` に変わった**（`LILYSHARP_OMR_PATH` を直す）。

**`omr verify`（2026-10-05 深夜、版 0.3.0。Lily# の B5 = 保存で照合）**: `--version --json` に `"commands":["read","verify"]`。
- `omr verify <x.lys> [--lysc <path>] [--progress json]`: **ページは読み直さない**。`read` が `x.omr.json` の `verify`（Lily# は読まない項目。小節ごとにページが見せる記号と枠）に残した入力側と、今の `x.lys` を lysc で描き直したものを小節ごとに照合する。
  `x.lys` の各パートの小節は、`section` の中のパートの括弧を `|` で区切って取り直し、読んだ小節に順に当てる（数が違えば `warning` の行を出して順に比べる）。小節の長さは lysc の警告（LYS2001）から。
- `x.omr.json` を書き直す: `measures[].line`、`todos` のうち `source: "verify"`（新しく見つけたもの。`.lys` に `@todo` は書かない）。読み取りの小節の長さの todo（`source: "length"`）も照合のものに置き換わる。
  前の照合・長さの todo の `@todo` がまだ `.lys` にある小節は、そのキーを使い続ける。記号単位の todo（`source: "detector"`）は `.lys` にその `@todo` が残っている間だけ残し、`lines` を今の行に直す。
  注釈画像 `x.omr-pNN.png` も作り直す。**`.lys` には触らない**。1 ページの楽譜で 1〜2 秒（ほぼ lysc の描画）。
- **`.lys` に誤りがあれば**（lysc が LYS の error を出す。書きかけの保存）終了コード 2、`error` の行に lysc の最初の誤り。`x.omr.json` はそのまま。
- 前の版の OMR が書いた `x.omr.json`（`verify` の項目がない）は終了コード 2（「読み直してください」）。
- stdout・終了コードは `read` と同じ（`done` は `lys`・`json`・`annotated`・`todos`）。

**`todos[].confidence`（版 0.3.0）**: 記号単位の todo（`kind: "note"`）に、読んだとおりで正しい確からしさ（0〜1）。1 − 検出器が別の読みに付けた確信度（いくつかあれば一番低いもの）、規則の付点を検出器が見ていないときは 0.5。小節単位の todo は `null`。

```
omr --version --json
  → {"version":"0.3.0","protocol":1,"commands":["read","verify"],"inputs":["png","jpg","jpeg","tif","tiff","bmp","pdf"]}

omr read <画像|PDF>... --out <dir> [--name <base>] [--lysc <path>] [--verify] [--progress json]
omr verify <x.lys> [--lysc <path>] [--progress json]
```
- 入力は画像か PDF をそのまま（複数なら順にページ）。**`.score` は要らない**（パートの構成は OMR が推測する）。
- `--lysc`: 照合（`--verify`）で描き直すのに使う lysc。拡張機能が自分の lysc を渡す。無ければ OMR は自分の隣、次に PATH を探す。
- **出力**（`--out` に、`<base>` は既定で最初の入力の名前）: `<base>.lys`、`<base>.omr.json`（前の提案の D）、`<base>.omr-pNN.png`（注釈画像）、`<base>.pNN.png`（PDF・TIFF のページの画像）。
  一時名で書いて最後に名前を変えるので、途中のファイルは見えない。
- **stdout**（`--progress json`、UTF-8、1 行に 1 つの JSON）:
  ```
  {"event":"start","pages":9}
  {"event":"page","page":2,"of":9}
  {"event":"warning","message":"page 3: one system not found"}
  {"event":"done","lys":"…/x.lys","json":"…/x.omr.json","annotated":["…/x.omr-p01.png"],"todos":12}
  {"event":"error","code":"input","message":"…"}
  ```
- **stderr**: 人が読むログだけ（拡張機能は出力チャンネルにそのまま流す）。
- **終了コード**: 0 は成功、1 は一部のページが読めなかった（`.lys` は出る）、2 は入力の誤り（読めない画像、壊れた PDF）、3 は内部の誤り。
- 中止はプロセスを止めるだけでよい。

## 4. Lily# 側で作るもの

| # | 項目 | 中身 | 大きさ |
|---|---|---|---|
| L1 | **OMR の場所** | 次の順で探す: ① 環境変数 `LILYSHARP_OMR_PATH`（開発用。F5 なら `launch.json` の `env`。VS Code は起動したときの環境変数を見るので、変えたら再起動）、② `globalStorageUri/omr/` に置いた版（L5）、③ 無ければ L2 へ。**`package.json` の設定には出さない** | 小 |
| L2 | **一覧を取り、版を選ぶ** | 一覧の URL は拡張機能に埋める。拡張機能が対応する `protocol` の範囲（例 `[1, 1]`）を定数で持つ。`protocol` が範囲内、`minLilySharp` ≤ 自分の版、自分の OS 向けの `assets` がある、の 3 つを満たす版のうち最新を選ぶ。OS は `process.platform`・`process.arch` から（`win32`+`x64` → `win-x64`、`darwin`+`arm64` → `osx-arm64`、`linux`+`x64` → `linux-x64`）。無ければ「この OS にはまだ OMR がない」と出す | 小 |
| L3 | **同意と取得** | 初回はモーダルで「OMR（約 16 MB）を取ってきて入れますか？」を出す。同意を `globalState` に覚える。取得は進み具合の通知つきで、取り消せる。**VS Code のプロキシ設定が効く方法**で取る（社内のプロキシ） | 小〜中 |
| L4 | **確かめる** | ① `.sig` で一覧の署名を確かめる（Ed25519 の公開鍵は拡張機能に埋める。鍵を替えるときのために 2 本まで持てるように）。② zip の sha256 が一覧と合うかを確かめる。どちらかが合わなければ捨てて、誤りを出す（何度も繰り返さない） | 小 |
| L5 | **置く** | `globalStorageUri/omr/<version>/<rid>/` に展開する。一時のフォルダに展開してから名前を変える（途中で止まっても壊れた版が残らない）。mac・Linux は実行の権限（`chmod +x`）。新しい版が動いたら古い版を消す（動いている読み取りがあれば後で）。VS Code の窓が複数あるときの同時の取得を避ける（ロックのファイル） | 小〜中 |
| L6 | **動くか確かめる** | `omr --version --json` を呼び、`version` と `protocol` が一覧のとおりかを確かめる。だめなら、その版を消して誤りを出す | 小 |
| L7 | **読む（取り込みのコマンド）** | コマンド「Lily#: Import from Image/PDF…」、エクスプローラーの右クリック（`.png` `.jpg` `.jpeg` `.tif` `.tiff` `.pdf`）。前の提案の B1 にあるドラッグ＆ドロップ・クリップボードは後でよい。**引数は配列で渡す**（`child_process.spawn(path, args)`。シェルの文字列にしない。日本語や空白のパスがあるため）。`--lysc` には拡張機能の lysc、`--out` には入力の隣（書けなければ聞く）、`--progress json`。stdout の行を読んで通知の進み具合に、stderr は出力チャンネルに。取り消しはプロセスを止める。終わったら `.lys`・プレビュー・注釈画像を開く。終了コード 1 なら「一部のページが読めなかった」と添える | 中 |
| L8 | **更新** | 拡張機能の起動時に、1 日 1 回まで一覧を見る（L2 と同じ選び方）。新しい版があれば通知（「更新」「あとで」）。読み取りの途中では入れ替えない。Lily# を更新して `protocol` の範囲が変わったら、合う版を取り直す | 小 |
| L9 | **ファイルから入れる（オフライン）** | コマンド「Lily#: Install OMR from File…」。手で落とした zip と、一覧・署名を選ばせて、L4〜L6 と同じ確かめをして同じ場所に置く | 小 |
| L10 | **OMR を消す**（任意） | コマンド「Lily#: Remove OMR」。`globalStorageUri/omr/` を消し、同意も忘れる | 小 |
| L11 | **Remote**（WSL・SSH・Codespaces） | 拡張機能がリモート側で動くなら、リモートの OS 向けを取り、リモートで動かす（L2 の `process.platform` はリモートの値になる）。入力の画像もリモートにある前提。まずは「ローカルだけ」で始めてもよい | 小（確かめるだけ） |

### 誤りのときの出し方

| 場面 | 出すこと |
|---|---|
| ネットワークにつながらない | 「OMR を取れなかった」＋「ファイルから入れる…」（L9）への案内 |
| この OS 向けがない | 「この OS にはまだ OMR がない」（いまは Windows x64 だけ） |
| 署名・ハッシュが合わない | 「OMR の確かめに失敗した」。取ったファイルは捨てる。自動で繰り返さない |
| `protocol` が合う版がない | 「Lily# を更新してください」または「この Lily# に合う OMR がない」 |
| 終了コード 2 | 「この画像・PDF は読めない」＋ stderr の最後の数行 |
| 終了コード 3 | 「OMR の内部の誤り」＋出力チャンネルを開くボタン |

## 5. 作らないもの（今は）

- **ログイン・利用の制限・決済**（P2 になってから）。
- **利用の数を数える仕組み**: P1 では、同意した利用者の分だけ匿名で数える方針（`vscode.env.isTelemetryEnabled` に従う）。ただ、受け取るサーバーがまだないので後回し。
- **設定画面の OMR の場所**、OMR の同梱、OMR を同じプロセスで動かすこと。

## 6. 順番の提案

1. **L1・L7**（環境変数で OMR を指して、取り込みのコマンドを通す）。OMR 側の `omr read` が出来しだい、手元で「読んで開く」が試せる。
2. **L2〜L6・L9**（一覧・取得・確かめ・配置）。OMR 側の発行（zip、一覧、署名）と合わせる。
3. **L8・L10・L11**。
4. 前の提案の **B2 以降**（候補のクイックフィックス、元の画像のビュー、見比べ）。

## 7. 決まっていないこと

- 公開の置き場所（リリース用リポジトリの名前、または静的ホスティング）と一覧の URL。
- 署名の鍵の作り方と保管（OMR 側）。
- コード署名（Windows の Authenticode、mac の署名と notarization）をいつから入れるか。
- `minLilySharp` で使う Lily# の版の付け方（拡張機能の版か、lysc の版か）。
