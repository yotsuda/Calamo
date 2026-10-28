# Lily# へのフィードバック（2026-09-30 夜、MusicXML 書き出しの修正の確認から）

LilySharp-Omr の作業で見つけたもの。前の 12 件（`musicxml-exporter-bugs.md`）は**全部直った**ことを確かめた。これはその確認の途中で見つけた残り。

- **確認に使った lysc**: `C:\MyProj\LilySharp\LilySharp.Cli\bin\Release\net10.0\lysc.exe`（2026-09-30 23:21 のビルド。コミット `3db4de10` に、作業ツリーの未コミットの
  `wedge`/`octave-shift` の `number` の変更を足したもの）。
- **MuseScore**: Studio 4.7.5、`leland.mss`（同じフォルダ）。
- **再現用ファイル**: `docs/repro/musicxml-musescore/slash-tab.lys`（ギター + TAB、1 小節目は和音、2 小節目はリズムのスラッシュ）と、Lily# の描画 `slash-tab.png`。

## 修正の確認（2026-10-01）

Lily# `d334e6d4`（handoff `548f1a9d`）の lysc（10-01 00:47 のビルド、作業ツリーはきれい）で、slash-tab.lys・再現用の 3 曲・生成譜 band001・002・005 を書き出し直し、MuseScore でも描いた。

| # | 結果 |
|---|---|
| 13 | **直った**。TAB の譜表のスラッシュは `<forward>`（`staff` 2、`voice` 5）になり、MuseScore の TAB のその小節は空（偽の「7」が消えた）。band002・005 で TAB の譜表のスラッシュは 0（前は 41・52 音）。`<beam>` の減り（477 → 423、568 → 509）はちょうどそのスラッシュの連桁（54・59） |
| 14 | **直った**。slash-tab.lys の LYS5002 は 0（`xml`・`png` とも）。band の曲で残るのは本物の 2 件（band002 の 92 行目、ベースの `ees,,` = OMR の生成器の問題） |
| 15 | Lily# の判断で**意図した描き方**（ジグザグ。対応なし） |
| 16 | **直った**。2 段のパート（TAB つき、大譜表）とドラムの 2 声の音符は全部 `<voice>` つき。`<voice>` が無いのは 1 段 1 声のパート（ボーカル、TAB の無いベース）だけ（仕様上問題ない） |

前の 12 件は崩れていない（beams・piano001・band001 の要素の数は前回と同じ。band002・005 の違いは上の #13 の分だけ）。

重さ: **高** = 他のソフトで楽譜の中身が変わる、**中** = 見た目・診断が違う、**低** = 付随情報。

| # | 対象 | 重さ | 今の動き | あるべき動き |
|---|---|---|---|---|
| 13 | MusicXML: TAB の譜表のスラッシュ | 高 | スラッシュの音符を TAB の譜表（`<staff>2`、`<voice>5`）にも `<unpitched>` B4 + `<notehead>slash` で書く。**MuseScore は TAB にフレット「7」を音符ごとに出す**（存在しない音）。Lily# の描画では TAB のその小節は空 | TAB の譜表にはスラッシュを書かない（Lily# の描画と同じく空。時間を埋めるなら `<forward>`） |
| 14 | 診断 LYS5002 | 中 | スラッシュの音符ごとに「note is below the tab's lowest string and was omitted from the tab … likely an octave too low」。`xml`・`png`・`svg` のどれでも出る。slash-tab.lys で 5 件、生成譜 1 曲で数十件 | スラッシュは音高が無いので警告しない（本物の低すぎる音の警告は今のままでよい。例: ベースの `ees,,`） |
| 15 | 描画: TAB の和音のフレット | 中（要確認） | slash-tab.png の 1 小節目: `<e g b>` のフレット 9・8・7 が縦にそろわず、**真ん中の 8 だけが左にずれる**（7 と 9 は同じ列） | 和音のフレットは 1 列に縦に並べる（LilyPond・MuseScore とも同じ列。数字が線の間に収まらないなら、数字の下の線を白く抜く）。意図した回避なら不要 |
| 16 | MusicXML: 五線の譜表の `<voice>` | 低 | TAB つきのパートで、五線（`<staff>1`）の音符に `<voice>` が無く、TAB（`<staff>2`）の音符は `<voice>5` | 両方に書く（`<voice>1` と `<voice>5`）。省略は仕様上は可だが、TAB 側だけ書くのは読む側の既定の解釈に頼る |

## 詳細

### #13 TAB の譜表のスラッシュ
- slash-tab.lys を `lysc xml` → MuseScore で描くと、2 小節目の TAB に「7」が 5 つ並ぶ（B4 を 1 弦の 7 フレットと解釈）。Lily# の描画 `slash-tab.png` の TAB は空。
- バンドスコアのギターはスラッシュが多い（生成譜の band スイートでは 2 小節ごと）ので、他のソフトで開くと TAB が偽の数字だらけになる。
- 五線側（`<staff>1`）のスラッシュの書き方（`<unpitched>` + `<notehead>slash`）は MuseScore で正しく描かれた。

### #14 LYS5002
- 警告の位置はスラッシュの音符（`/4 4 8 8 4` の各音）。スラッシュを TAB から省くこと自体は描画と合っている。直すのは診断の条件だけ（音高の無い音符を除く）。

## MuseScore 側の表示（Lily# の不具合ではない。参考）
- リハーサル記号: XML では一番上のパートにだけ書かれている（正しい）が、MuseScore 4 は各楽器の上に描く（MuseScore の「システムの記号を表示する譜表」の既定）。
- TAB のリズム: MuseScore は TAB の下に符幹・旗を描き、TAB にも拍子記号を描く。Lily# の TAB は数字だけ。MusicXML で抑えられるか（TAB の音符の `<stem>none</stem>`、
  `<time number="2" print-object="no">` など）は**確かめていない**。Lily# の見た目に揃えたいときの候補。
- `repeat percent` が曲の終わりまで続くとき `type="stop"` が無いのは、後ろに小節が無いので正しい（MuseScore も正しく描いた）。
