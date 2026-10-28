# Lily# から OMR 開発のセッションへ（2026-10-12・1 枚版）

前回の 1 枚版 [`lilysharp-handoff-2026-10-09.md`](lilysharp-handoff-2026-10-09.md) からの差分。
**主題は 2 つ。** ⑴ **Lily# 0.12.0 が出た**（Marketplace と GitHub Release）。10-09 版の文法・SMuFL は全部これに入った。
⑵ **OMR の配布の方針が変わった**（§1）。10-05 の [`lilysharp-omr-distribution-2026-10-05.md`](lilysharp-omr-distribution-2026-10-05.md)
（初回 DL・manifest・Ed25519 署名）を**置き換える**。食い違うときは**この 1 枚が新しい**。

## 0. 使う物

| 物 | 場所・状態 |
|---|---|
| Lily# | **v0.12.0 リリース済み**（タグ `v0.12.0` = `928c6d683`、2026-10-12。push 済み） |
| VS Code 拡張 | Marketplace の `yotsuda.lilysharp` 0.12.0（8 プラットフォーム） |
| lysc（Release） | `C:\MyProj\LilySharp\LilySharp.Cli\bin\Release\net10.0\lysc.exe` — `lysc --version` が `0.12.0` を出すこと。古ければ `dotnet build LilySharp.Cli -c Release` |
| テスト | Lily# 全体 12147 / 0 / 2（skip） |

## 1. 配布の方針（オーナー決定 2026-10-12）

### 1.1 決まったこと

| 項目 | 決定 |
|---|---|
| 値段 | **無償の Beta** として出す。「Beta」と分かるようにする |
| 有償化 | **今はしない。ただし道は残す**: ソースは非公開のまま、利用条件に「Beta の間は無料。将来の版は有償になる場合がある」と書く（「ずっと無料」とは書かない） |
| 形 | **Lily# とは別の VS Code 拡張**として出す（オーナーの提案「独立した拡張の方がユーザーから見て分かりやすい」） |

### 1.2 なぜ別の拡張か（Lily# への同梱・初回 DL と比べて）

| | Lily# に同梱 | 初回 DL（10-05 案） | **別の拡張（採用）** |
|---|---|---|---|
| 利用者からの見え方 | GPL の中に閉じた部品 | 設定に出ない部品 | **任意の追加機能**。Marketplace に自分のページ |
| OMR だけの更新 | Lily# を出し直す | 自前の更新確認（L8） | **Marketplace の自動更新** |
| ダウンロード・改ざん検査 | 不要 | **manifest・Ed25519・sha256 を自作**（L2〜L6） | **Marketplace が持つ**（自作不要） |
| Windows 専用 | win-x64 の VSIX だけ太る | 同上 | **win32-x64 だけ publish**。他 OS には出ない |
| Beta の表示 | コマンド名だけ | 同上 | **`"preview": true` で Preview バッジ** |
| ライセンス | 説明が要る | — | **Lily# は GPL のまま**。OMR は自分の利用条件 |

⇒ **10-05 の L2〜L6・L8（manifest、署名鍵、DL、展開、更新確認）は作らない。** 公開用のバイナリ repo・静的ホスティングも不要。
L1（`LILYSHARP_OMR_PATH` → globalStorage）はそのまま残り、開発中の OMR を試す道として使う。

### 1.3 構成案（Lily# 側の推奨。OMR 側の意見を聞きたい）

- **OMR 拡張**（ID 案 `yotsuda.lilysharp-omr`）: `omr.exe` 一式＋最小のコード。`activate` が `{ path, version, protocol }` を返す
  （`vscode.extensions.getExtension(...).exports` で Lily# が読む）。`package.json` に `"preview": true`、
  `"extensionDependencies": ["yotsuda.lilysharp"]`（**OMR を入れると Lily# も入る**。逆向きの依存は付けない）、
  publish は `vsce publish --target win32-x64` だけ。
- **画面は Lily# に残す**: Import from Image/PDF…、Show Original Scan、verify on save。作り済みで、スキャンの表示は Lily# の
  言語サーバーに依る。OMR 拡張にコマンドは要らない。
- **Lily# 側の受け口は 0.12.0 には入っていない**（§3）。⇒ OMR 拡張の `minLilySharp` に当たるものは **0.13.0 以降**になる見込み。

### 1.4 未決（オーナーと OMR 側で）

1. **表示名と ID**: 例「Lily# Scan Import (Beta)」（機能として見せる）／「Lily# OMR (Beta)」、ID `yotsuda.lilysharp-omr`。
2. 構成案（§1.3）でよいか。特に「画面は Lily# に残す」。
3. 利用条件の文面（無償 Beta・再配布の可否・改変禁止・将来有償の可能性）。

### 1.5 判断の背景（参考）

- **Lily# の利用者はまだ 0 人**（Marketplace のインストール数 8 はオーナー自身）。サブスクの収入は当面ほぼ 0 と見積もった。
- 無料の OMR（Audiveris、MuseScore の取り込み）がある。単体の OCR として売るより、**Lily# の「楽譜の取り込み口」**として出す
  （編集できる `.lys`、自信の無い所の赤い丸とメモ、スキャンとの突き合わせ＝他に無い所）。
- Marketplace に課金の仕組みは無い。将来有償にするなら、外部の決済（Paddle 等）＋拡張でライセンスを確かめる形になる。
  別の拡張にしておけば、課金は OMR 拡張だけで済み、Lily# は無料・GPL のまま。

## 2. OMR 側にお願いしたいこと（出す前に）

| # | 内容 | 根拠 |
|---|---|---|
| a | **`lysc` のパスの直書きをやめる**（`Verify.cs:26` の `C:\MyProj\LilySharp\...\lysc.exe`）。Lily# が `--lysc` で渡す前提にする（無ければ verify を飛ばして、そう言う） | 利用者の PC には無いパス |
| b | **win-x64 の self-contained publish**、exe 名 `omr.exe`。**大きさを実測**（Windows SDK の投影 23.7 MB が乗る見込み。10-05 の「約 16 MB」は古い） | 拡張の大きさ・説明文 |
| c | README の「**No AI**」を直す（ONNX の検出器を使っている） | 事実と違う |
| d | **ライセンス**: OMR の利用条件ファイル（§1.4-3）、ONNX Runtime（MIT）の告知、**学習に使ったフォント**（Emmentaler＝GPL/OFL、MuseScore 系）の条件の確認、`data/scans` の市販楽譜を配布物に入れない | 公開前の必須 |
| e | **VS Code 拡張の器**（§1.3）と、private repo からの publish（`VSCE_PAT`。publisher `yotsuda` は Lily# と同じ） | 配布の形 |
| f | **`omr --version --json` の契約は今のまま**（protocol 1、`commands`、`inputs`）。変えるなら protocol を上げる | Lily# が見ている |
| g | **本物の VS Code で一度通す**（L7・B1〜B6 は Lily# 側に実装済みだが、本物の OMR で動かしたことがない）。今は `LILYSHARP_OMR_PATH` を OMR の実行ファイル（今の名前は `omr.exe`、2026-10-28 に `OmrProto.exe` から改名。`.dll` なら Lily# が `dotnet` で起こす）に向けて VS Code を再起動すれば、Lily# 0.12.0 から呼べる | 未確認のまま出さない |

## 3. Lily# 側でやること（次の版。0.12.0 には無い）

- `locateOmr` に「インストールされた OMR 拡張」を足す（環境変数が先、次に拡張、次に globalStorage）。
- OMR が無いとき、Import from Image/PDF… が **OMR 拡張の導入を案内**（VS Code の拡張インストールのコマンドで開く）。
- 拡張が入った・消えたら、再起動せずにメニューに反映（`vscode.extensions.onDidChange`）。
- コマンド名に「(Beta)」、初回に Beta の一言。プロトコルが合わないときは「Lily# を更新して」と言う（照合は既にある）。

## 4. 0.12.0 で変わったもののうち OMR に効くもの

### 4.1 `@todo` の見え方（OMR の出力がそのまま得をする）

- **メモは自分の段の余白に立つ**（下の段のメモが系の上に出なくなった）。**1 小節より広いメモ**も、大譜表の段の間に立つ
  （小節線をまたいでよい。2026-10-12 修正）。
- tab の音は丸がフレットの数字を囲む。スラッシュ音符・`drums { }` の行の `@todo` も描く。メモの下線は和文と欧文で揃う。
- **`lysc svg|png|pdf --todo`** で赤い丸とメモ付きの絵を出せる（2026-10-12）。scan view と並べる確認や、学習データの検査に使える。

### 4.2 描画の変化（学習データは 0.12.0 の lysc で描き直す。10-09 以前の絵と混ぜない）

10-09 版の後に入った、絵が変わる主なもの:
- **四分音の臨時記号**: LilyPond のグリフ（`sharp.slashslash.stemstemstem`・反転したフラット）になり、幅を取るようになった
  （以前は符頭に重なっていた）。
- **行頭の練習番号・section 名の高さ**: 打楽器・soprano・mezzo-soprano・`bass_8` の譜表で、描いている音部記号の上に立つ
  （以前は架空の高音部記号の分だけ高かった。LilyPond 0.85 に対し 2.21）。
- **`instrument` の楽器名**: プリセットの語（`alto-sax`）ではなく正式名（Alto Saxophone）を、**2 つ以上の楽器がある score だけ**に刷る。
  1 段の score は名前を刷らない。⇒ 生成器の楽譜がプリセットを使っていれば、段の左の文字が変わる・消える。
- 線の少ない譜表（1・2・4 線）、スラッシュ音符（加線なし、記号の位置）、代替の音楽フォントの字形（Leland のスラッシュの頭が欠けていた）。

### 4.3 文法の破壊的変更 — OMR の出力には当たらない（確認済み）

0.12.0 の破壊的変更（`pitch` の意味、`pitch transposed`、無名の block、score の名前、LYS6014「section があって score が無いのはエラー」など）。
**OMR の reader は `instrument` も `pitch` も書かず（`Program.cs:350` は `clef` と `_8` の `transposition 8vb` だけ）、`form { ~Piece }`
と `score { … }` を書いている（`Program.cs:438-440`）ので当たらない。** 生成器（OmrGen）の楽譜が `pitch` やプリセットを使うなら、
CHANGELOG の 0.12.0 の Breaking changes を一度読むこと。

## 5. 既知の注意

- Lily# 0.12.0 の新機能（Guess Section Key／Respell Notes to Key、音源の履歴）は**本物の VS Code での確認が一部未**。
  OMR の取り込み結果の調を推定し、耳コピの綴り（`fis`/`ges`）を揃えるのに使える — スキャンから読んだ楽譜でも同じ。
- 音源は同梱しない（OS の General MIDI バンクが既定）。OMR には関係しない。
