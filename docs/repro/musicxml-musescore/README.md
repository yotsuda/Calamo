# Lily# の MusicXML を MuseScore 4 で読んだときの抜け（2026-09-30）

目的: 生成譜を別フォントで描いて検出器の学習データにする（ROADMAP §4b の B4 の前段）。流れは
`OmrGen の .lys` → `lysc xml` → MusicXML → `MuseScore4.exe -S <font>.mss -o page.png|svg`。
**正解は MusicXML**（ユーザーの決定）。そのため、学習に使う要素は MusicXML に正しく出ている必要がある。

使ったもの: Lily# の Release の lysc、MuseScore Studio 4.7.5。フォントはスタイルファイル（`leland.mss`、`bravura.mss`、`petaluma.mss`。
`musicalSymbolFont` / `musicalTextFont` / `defaultFontFace`）で切り替わる。1 曲の描画は約 0.6 秒。

- `band001.lys`: ボーカル（コード名、歌詞）、ギター + TAB、ベース + TAB、ドラム、`repeat percent`、スタッカート、`form { |: A :| }`
- `piano001.lys`: 生成器の piano スイートの 1 曲（`grandStaff`、2 声、和音）
- `*-lysc.png` は Lily# の描画、`*-musescore-*.png` は MusicXML を MuseScore で描いたもの

## MuseScore で描くと出ないもの・違うもの

| # | 要素 | MusicXML の中身 | MuseScore の描画 |
|---|---|---|---|
| 1 | TAB（`tab gt`、`tab ba`） | `<staff-details>`・`<technical><string>/<fret>` が無い | TAB 譜が出ない |
| 2 | 歌詞（`lyrics words`） | `<lyric>` が無い | 歌詞が出ない |
| 3 | `repeat percent` | `<measure-repeat type="start">` だけで `type="stop"` が無い（ギターだけ。ベースの 2 小節分は無い） | 音符がそのまま描かれ、`%` にならない |
| 4 | リハーサル記号（節の名前の □A） | `<rehearsal>` が無い | 出ない |
| 5 | パート名 | `<part-name>` が id（`vo`、`gt`）。`part vo "Vocal"` や `staff rh "Right"` の名前でない | 「vo」「rh」と出る |
| 6 | `grandStaff` | 2 つの別パート（`<staves>2` でない） | 括弧が無く、2 段が別の楽器になる |
| 7 | テンポ | Lily# は描かないのに、全パートに `<metronome>` 120 と `<sound tempo>` | 各段に「♩ = 120」 |
| 8 | C 記号（`time 4/4`） | `<time>` に `symbol="common"` が無い | 数字の 4/4（Lily# は C） |
| 9 | ドラム譜の調号 | 打楽器のパートにも `<key><fifths>1` | ドラム譜に ♯ が付き、バスドラム（F）に ♮ が付く |
| 10 | ハイハットのオープン・クローズ（`hho`、`hhc`） | `o`・`+` の記号が無い | 出ない |
| 11 | **連桁**（2026-09-30 追加） | `<beam>` が一つも無い（`MusicXmlExporter` に連桁を書く処理が無い） | MuseScore が自分の規則で連桁を付ける。4/4 などでは Lily# と同じになるが、7/4 などでは違う（`beams.lys`: Lily# は拍ごとに連桁か旗、MuseScore は 2 小節目の後半 9 音を 1 本でつなぐ。`beams.png` と `beams-musescore-leland-1.png`） |

| 12 | **同じ音部記号の書き直し**（2026-09-30 追加） | `section` の始まりごとに、変わっていない `<clef>` をもう一度書く（`band001.lys` を `lysc xml` すると P1・P3・P4 の m5 に同じ `<clef>`） | MuseScore は音部記号の変更とみなし、前の段の終わりに小さい予告の音部記号を描く（実際の楽譜には無い） |

OMR 側の回避（Lily# 側で直るまで）: `tools/train-model/ms_band_xml.py`（`run-ms.ps1 -Band`）が #3（`%` を同じ中身の小節から付け直す）、#9（打楽器パートの調号を消す）、
#12（同じ音部記号を消す）を MuseScore に渡す前に直す。

#11 の見つけ方: 学習セット 721 曲（`tools/run-ms.ps1` の `out/ms-train`）で、Lily# の描画（SVG の正解）と MuseScore の描画の記号の数を曲ごとに比べた。
符頭（種類ごと）・休符（種類ごと）・連符の数字は全曲で一致。旗の数が 112 曲で違い（times 65、meters 14 など、拍子が多様なスイートに偏る）、
その曲の XML に `<beam>` が無かった。ほかの違いは exporter の問題ではなかった: 付点 7 曲は `r1.`（6/4・12/8 の 1 小節全部の休符）を MuseScore が
付点のない小節休符として描くため（XML には `<dot/>` がある）。タイ・スラーの弧の数の ±1 は段の折り返しの位置の違い（XML の `<slur>`・`<tied>` の数は `.lys` と一致）。

正しく出たもの: 音高・音価、和音、2 声、スタッカート、コード名（`<harmony>`）、反復の小節線、ドラムの × の符頭（`<unpitched>` + `<notehead>x`）、
シンバルの ⊗、臨時記号、調号（打楽器以外）。piano001 の音符は Lily# の描画と一致した（目視、1 段目）。

## MuseScore の SVG

要素ごとに `class` が付く（basic001 で `Note` 110、`Stem` 109、`Beam` 51、`StaffLines` 25、`KeySig` 20、`BarLine` 16、`Rest` 10、`Accidental` 9、
`Hook` 8、`NoteDot` 7、`Clef` 5、`LedgerLine` 4、`TimeSig` 2、`Tuplet` 2 など）。記号単位の正解（検出器の枠）はここから取れる見込み。
符頭の種類（黒・白・全）は `class` では分からないので、グリフの輪郭（フォントごとに同じ `d` の形）で分ける必要がある。
SVG の座標は 1 mm = 47.2 単位（A4 = 9924 × 14028）。PNG は `-r 300` で 2481 × 3507。
