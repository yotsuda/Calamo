# Lily# へのフィードバック（2026-10-01、MuseScore の band の評価セットから）

LilySharp-Omr の作業（生成譜の band を MuseScore 4 で描き、生成元の `.notes` で採点する評価台）で見つけたもの。前回の #13〜#16（`lilysharp-feedback-2026-09-30.md`）の続きの番号。

- **lysc**: `C:\MyProj\LilySharp\LilySharp.Cli\bin\Release\net10.0\lysc.exe`（2026-10-01 00:47 のビルド、Lily# `d334e6d4`）。
- **MuseScore**: Studio 4.7.5。
- **再現用**: 生成譜 `out/gen-band/band/band009/band009.lys`（OMR のリポジトリ。`treble_8` のギター + TAB、`staff perc "Perc." as lines 1`）。`lysc xml` で書き出し、MuseScore で開く。

重さ: **高** = 他のソフトで楽譜の中身が変わる、**中** = 見た目が違う、**低** = 付随情報。

| # | 対象 | 重さ | 今の動き | あるべき動き |
|---|---|---|---|---|
| 17 | MusicXML: `clef treble_8` のパート（`tuning guitar`） | 高 | `<clef>` に `<clef-octave-change>-1`、`<attributes>` に `<transpose><octave-change>-1`、`<pitch>` は `.lys` の音名のまま（`b` → B4）。**MuseScore は五線を Lily# の描画より 1 オクターブ上に描き**（`b` を第 3 線でなく上第 2 線の上に）、**TAB も 1 オクターブ上のフレット**を出す（`<b d'>` を Lily# は G 弦 4・B 弦 3、MuseScore は B 弦 12・E 弦 10。XML の `<string>3</string><fret>4</fret>` は使われない） | Lily# の描画（第 3 線の `b`、鳴るのは 1 オクターブ下）と合う書き方は、**`<pitch>` を鳴る音（B3）にし、`<clef-octave-change>-1` を残し、`<transpose>` を書かない**（8vb のト音記号の第 3 線が B3）。MuseScore 4.7.5 でこう直すと、五線も TAB（G 弦 4・B 弦 3）も Lily# の描画と一致した（OMR 側の回避 `ms_band_xml.py` の `octave_down`）。Dorico・Finale での解釈は未確認 |
| 18 | MusicXML: `staff x as lines 1`（1 本線の打楽器譜） | 中 | `<staff-details><staff-lines>1</staff-lines>` が無く、**MuseScore は 5 本線で描く**。音符は `<unpitched><display-step>F</display-step><display-octave>4</display-octave>` | `<staff-details><staff-lines>1</staff-lines></staff-details>`。その上で音符の `display-step`/`display-octave` は線の上に来る高さ（MuseScore 4 では 1 本線の線は B4。F4 のままだと線の下に加線つきで描かれた） |

## 詳細

### #17 `treble_8` のオクターブ

- band009 の 1 小節目のギター `<b d'>8 …`: Lily# の描画は `b` が第 3 線、TAB は G 弦 4・B 弦 3（= B3・D4。鳴る音は書いた音の 1 オクターブ下）。`.notes` は 71（B4 = 書いた音）。
- 書き出しは `<pitch>B4</pitch>` + `<clef-octave-change>-1` + `<transpose><octave-change>-1`。MusicXML の `<pitch>` は書いた音で、`<clef-octave-change>-1` は「この記号の第 3 線は B3」という意味なので、
  B4 は第 3 線の 1 オクターブ上に描かれる（MuseScore はそう描いた）。オクターブを記号と `<transpose>` の両方で数えている。
- MuseScore は TAB のフレットを `<technical>` の弦・フレットでなく音高から決めるようで、TAB も 1 オクターブ上になった（B4 = B 弦 12）。
- OMR の評価への影響: `treble_8` のギターの 7 曲（band002〜004、007〜010）で、ギターの音が全部 1 オクターブ違い、MuseScore の band の評価が 52〜62% に落ちていた（回避後 Leland で 99.2%）。

### #18 1 本線の譜表

- band006〜010 の `Perc.`（`as lines 1`）が MuseScore では 5 本線になり、OMR の `.score`（`line`）と譜表の数が合わなくなっていた（段の中のパートの割り当てがずれる）。
- OMR 側の回避（`ms_band_xml.py`）: `.lys` の `as lines N` を読み、パート名で `<staff-details><staff-lines>N` を足し、1 本線なら `display` を B4 にする。
