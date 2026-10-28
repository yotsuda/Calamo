# Lily# の MusicXML 書き出し（`lysc xml`）の不具合と抜け

LilySharp-Omr の作業（B4a: 生成譜を MuseScore の別フォントで描いて検出器の学習データにする）で見つけたもの。
経路は `.lys` → `lysc xml` → MusicXML → MuseScore Studio 4.7.5。

- **確認日**: 2026-09-30。lysc は `C:\MyProj\LilySharp\LilySharp.Cli\bin\Release\net10.0\lysc.exe`（2026-09-29 16:48 のビルド）。
  下の 12 件は、この日にこのビルドで書き出し直して **全部まだ残っている**ことを確かめた。
- **再現用ファイル**: `docs/repro/musicxml-musescore/`（`band001.lys`、`beams.lys`、`piano001.lys`、書き出した `.xml`、Lily# と MuseScore の描画の PNG）。
  経緯と MuseScore 側の観察は同じフォルダの `README.md`。
- **正しく出ているもの**: 音高・音価、和音、2 声、臨時記号、調号（打楽器以外）、スタッカート、コード名（`<harmony>`）、反復の小節線、
  ドラムの × の符頭（`<unpitched>` + `<notehead>x`）、シンバルの ⊗（`circle-x`）、`<slur>`・`<tied>` の数。
  評価用の 90 曲で、MusicXML から作った音符の正解は `.notes` の正解と全曲で同じ数字になった（音高・音価・声部は正しい）。

## 修正の確認（2026-09-30 夜）

Lily# の 8 コミット（`ae588dba` 連桁 … `88756938`、作業ツリーの未コミットの `wedge`/`octave-shift` の `number` を含めて Release をビルド、lysc 23:21）で、
`band001.lys`・`beams.lys`・`piano001.lys` と生成譜 `out/gen-band` の band001・002・005 を書き出し直し、MuseScore 4.7.5（Leland）でも描いて確かめた。**12 件とも直った。**

| # | 結果 |
|---|---|
| 1 TAB | `<staves>2`、`<sign>TAB`、`<fret>`（band001 で 64）。MuseScore が TAB を描く |
| 2 歌詞 | `<lyric>`（トップレベルの `lyrics` も、生成譜の `section` の中の `lyrics` も）。ハイフンも出る |
| 3 `%` | `start`/`stop` がそろい、`%%` は中身 `2`（band002: P4 m6 start 2 → m8 stop）。**`stop` が無いのは曲の終わりまで続く区間だけ**（後ろに小節が無い） |
| 4 リハーサル記号 | `<rehearsal>`（一番上のパートだけ = 正しい）。MuseScore は各楽器の上に描く（MuseScore の既定の表示） |
| 5 パート名 | `Vocal`、`Guitar`、`Keyboard`… |
| 6 大譜表 | 1 パート `<staves>2`（band005 のキーボード）。下段にも名前がある `piano001` は `brace` の `part-group` の 2 パート（どちらも MusicXML として正しい） |
| 7 テンポ | `<metronome>` なし |
| 8 C 記号 | `symbol="common"` |
| 9 打楽器の調号 | 打楽器パートに `<key>` なし |
| 10 ハイハット | `open`/`stopped`。MuseScore が ○・＋ を描く |
| 11 連桁 | `<beam>`（beams で 37、piano001 で 225）。MuseScore の `beams.xml` の描画が Lily# の `beams.png` と同じ組み方（7/4 の 2 小節目の後半が拍ごと） |
| 12 音部記号 | 最初の小節だけ（band005: 7 = 各譜表 1 つ） |

**残り**は **`lilysharp-feedback-2026-09-30.md`**（#13〜#16: TAB の譜表に書かれるスラッシュを MuseScore が偽のフレット「7」で描く、スラッシュへの LYS5002 の偽の警告、
TAB の和音のフレットのずれ、`<voice>` の省略）。

OMR 側で分かったこと（Lily# の不具合ではない）: 生成器（`OmrGen/Band.cs`）のベースが、書いた音で E2 より低い音（`ees,,`、`fes,,`）を書くことがあり、TAB に出ない（LYS5002 の本物の警告。band002 の 92 行目）。

## 一覧（修正前）

重さ: **高** = 楽譜の中身が変わる・失われる、**中** = 見た目が Lily# の描画と違う、**低** = 名前・付随情報。

| # | 要素 | 重さ | 今の出力 | あるべき出力（MusicXML 4.0） |
|---|---|---|---|---|
| 1 | TAB（`tab gt`、`tab ba`） | 高 | TAB の譜表も弦・フレットも無い | TAB の譜表（`<staff-details>` に `<staff-lines>`・`<staff-tuning>`、`<clef><sign>TAB</sign>`）と、音符ごとの `<technical><string>`/`<fret>` |
| 2 | 歌詞（`lyrics words`） | 高 | `<lyric>` が無い | 音符ごとに `<lyric number="1"><syllabic>`/`<text>`（ハイフン・延長線は `<syllabic>`・`<extend/>`） |
| 3 | `repeat percent`（`%`・`%%`） | 高 | `<measure-repeat type="start">` だけで `type="stop"` が無い。2 小節の `%%` は何も出ない（band001 ではギターだけ出て、ベースの 2 小節分は無い） | 繰り返す区間の最初の小節に `<measure-style><measure-repeat type="start" slashes="1">1</measure-repeat>`（`%%` は中身 `2`）、区間の後の小節に `type="stop"` |
| 4 | リハーサル記号（`section` の名前、□A） | 中 | `<rehearsal>` が無い | 節の最初の小節に `<direction><direction-type><rehearsal enclosure="square">A</rehearsal>` |
| 5 | パート名 | 低 | `<part-name>` がパートの id（`vo`、`gt`、`rh`） | `part vo "Vocal"` の表示名（`staff rh "Right"` も）。無いときだけ id |
| 6 | `grandStaff` | 中 | 2 つの別パートになる | 1 パートで `<staves>2`、音符に `<staff>1`/`2`（または `<part-group>` の `brace`） |
| 7 | テンポ | 中 | Lily# が描かないのに、全パートの最初に `<metronome>` 120 と `<sound tempo="120">` | `.lys` にテンポ表示があるときだけ書く。再生用の既定のテンポは `<sound>` だけにし、`<metronome>` は出さない |
| 8 | C 記号（`time 4/4`、`2/2`） | 中 | `<time>` に `symbol` が無い（数字の 4/4 と描かれる） | Lily# が C で描くなら `<time symbol="common">`（2/2 は `symbol="cut"`） |
| 9 | 打楽器パートの調号 | 中 | `clef percussion` のパートにも `<key><fifths>` が出る | 打楽器のパートには `<key>` を出さない（出すなら `<fifths>0`）。MuseScore はドラム譜に ♯ を描き、バスドラムに ♮ を付けた |
| 10 | ハイハットのオープン・クローズ（`hho`、`hhc`） | 中 | `o`・`+` が無い | `<technical><open/>`（`hho`）・`<stopped/>`（`hhc`）。または `<articulations>` |
| 11 | **連桁** | 高 | `<beam>` が一つも無い（`MusicXmlExporter` に連桁を書く処理が無い） | 連桁でつながる音符に `<beam number="1">begin`/`continue`/`end`（2 本目以降は `number="2"`…、`forward hook`・`backward hook`） |
| 12 | **同じ音部記号の書き直し** | 中 | `section` の始まりごとに、変わっていない `<clef>` をもう一度書く | 音部記号が変わるときだけ `<clef>` を書く |

## 詳細

### #3 `repeat percent`
- `band001.lys` を書き出すと `<measure-repeat` が 2 つで、`type="stop"` は 0。
- `repeat percent N` の N は **書いた小節を含めた回数**（Lily# の意味）。MusicXML では、書いた小節は普通に出し、その後の N−1 小節を
  `start`…`stop` で囲むのが MuseScore の読み方（音符も書いておけば再生・他のソフトでも安全）。
- 関連（描画側の不具合）: `repeat percent` で `voice { } { }` を囲むと、Lily# の描画で最初の小節が空になる（`docs/repro/percent-around-voice.lys` と `.png`）。

### #11 連桁
- 見つけ方: 学習セット 721 曲で、Lily# の描画（SVG）と MuseScore の描画の記号の数を曲ごとに比べた。旗の数が 112 曲で違い（拍子の多様な
  `times` 65 曲、`meters` 14 曲などに偏る）、その曲の XML に `<beam>` が無かった。
- 4/4 などでは MuseScore が自分の規則で付けた連桁がたまたま Lily# と同じになるので気づきにくい。`beams.lys`（7/4）では、Lily# は拍ごとに
  連桁か旗、MuseScore は 2 小節目の後半 9 音を 1 本でつなぐ（`beams.png` と `beams-musescore-leland-1.png`）。

### #12 同じ音部記号の書き直し
- OmrGen の band スイートの曲（`section A { … } section B { … }`）で、section B の最初の小節（m9）に P1（ト音）・P3（ヘ音）の同じ `<clef>` が出る。
  `docs/repro/musicxml-musescore/band001.lys` は section が 1 つなので出ない。
- MuseScore はこれを音部記号の変更とみなし、前の段の終わりに予告の小さい音部記号を描く（実際の楽譜には無い）。

## OMR 側の回避（Lily# で直るまで）

- `tools/train-model/ms_band_xml.py`（`tools/run-ms.ps1 -Band`）が MuseScore に渡す前に XML を直す:
  #3（`measure-repeat` を消し、前の 1 小節・2 小節と音符が同じ小節から `%`・`%%` を付け直し、繰り返した小節の音符を消す）、
  #9（打楽器パートの `<key>` を消す）、#12（変わっていない `<clef>` を消す）。
- `tools/run-ms.ps1` が #7 の `<metronome>` の `<direction>` を消す。
- #1・#2・#4 は直せないので、TAB・歌詞・リハーサル記号は MuseScore の学習データに入れていない（TAB・`%` は Lily# の描画の学習データで覚える）。
- #11 は、学習の正解を MuseScore が描いた SVG から取るので学習データの誤りにはならない（ただし連桁の付き方は Lily# と違う）。

## 直す順の提案

1. **#11 連桁**と **#3 `%`**: 楽譜の見た目が他のソフトで大きく変わる。どちらも `.lys` 側に情報がある（連桁は組版で決めたもの）。
2. **#1 TAB**・**#2 歌詞**: バンドスコアの正解（B1）を `lysc xml` で作る計画なので、これが無いと TAB と歌詞を採点できない。
3. #12・#9・#7・#8: 小さい修正で、MuseScore での見た目が Lily# に揃う。
4. #4・#5・#6・#10。
