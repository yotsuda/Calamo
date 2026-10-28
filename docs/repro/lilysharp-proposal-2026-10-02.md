# Lily# への機能提案（2026-10-02、OMR の正解つき学習データのために）

LilySharp-Omr は、生成した `.lys` を lysc で PNG と SVG に描き、SVG のグリフと `data-pos` を記号単位の正解にして検出器を学習している（ROADMAP §2 原則 1）。
この仕組みの弱点は、**Lily# の描画が毎回同じ組み方になる**こと。同じ音符の並びは、いつも同じ間隔・同じ五線の大きさ・同じ符幹の長さ・同じ線の太さで描かれる。
実際の楽譜（MuseScore、Finale、Dorico、市販の浄書）はそこが揺れる。OMR はその揺れを、今は次の回り道で補っている。

- 解像度だけ変える（`lysc png --scale 3 / 5.6`）。
- MuseScore 4 で同じ曲を 8 フォントで描く（`run-ms.ps1`）。MusicXML 経由なので、書き出しの不具合（#1〜#18）に何度も止められた。
- MuseScore の SVG を自前で描き直し、グリフごとに大きさ・縦横比・傾き・線の太さを乱数で変える（`ms_restyle.py`）。これで初めて実スキャンが伸びた（detector16）。

Lily# が組み方の揺れを自分で出せれば、正解は SVG からそのまま取れ、回り道が要らなくなる。

## 今あるもの（調べた範囲。Lily# `docs/SYNTAX_REFERENCE.md`、`CLI_REFERENCE.md`、`GRAMMAR_AUDIT.md`）

| 分野 | ある | ない |
|---|---|---|
| 横の間隔 | `paper { spacingIncrement 1.2 }`（1 つだけ）、`raggedRight`、`indent`、`break`/`pageBreak` | 最短の音価の間隔、音価による間隔の比、符頭どうしの最小距離、小節の最小幅、1 段の小節数・1 ページの段数 |
| 縦の配置 | 用紙・余白、13 種の spacing ブロック（`staffStaffSpacing` など） | 五線の大きさ（`StaffHeight = 4` で固定。「五線の間隔が単位そのもの」という設計判断） |
| グリフ | `override` は `NoteHead`/`Stem` の `transparent`/`color` の 4 つ | 符幹の長さ・太さ、連桁の太さ・傾き、五線・加線・小節線の太さ、符頭の大きさ |
| 音楽フォント | Emmentaler のみ | SMuFL（Bravura、Leland など） |
| 乱数 | なし（出力はバイト単位で再現できる） | — |
| 正解の書き出し | SVG の `<text class="music" data-pos=…>`（グリフはコードポイント）、曲線の `data-pos` | 記号ごとの外接枠、記号の種類の名前、五線・声部・小節の番号 |

## 設計の方針（提案）

1. **楽譜の言語（`.lys`）は変えない。** どれも「描き方」の設定なので、CLI のオプションか描画用の設定ファイルで渡す。五線の大きさを言語に入れない今の設計判断とも両立する。
2. **乱数は Lily# に入れなくてよい。** 値を外から渡せれば、揺らす値は OMR 側が乱数の種から決める。Lily# は今どおり決定的（同じ入力と同じ設定なら同じ出力）のまま。
3. **SVG の座標は揺らした結果をそのまま表す。** 正解は今の仕組み（グリフと `data-pos`）で取れる。

## 提案（推奨順）

| # | 提案 | OMR での効き目 | 大きさの見立て |
|---|---|---|---|
| P1 | **CLI で `paper` の値を上書きする**: `lysc png --set spacingIncrement=1.6 --set raggedRight=true --set staffStaffSpacing.basicDistance=9 song.lys` | 今ある値だけで、同じ曲を曲ごと・ページごとに違う組み方で描ける。`.lys` を書き換えずに済む（`--batch` の行ごとに違う `--set` を渡せるとなお良い） | 小（既存の `PaperPlanReader` の値を外から差し込む） |
| P2 | **横の間隔のつまみを増やす**（P1 の `--set` で渡せる名前で） | 符頭の間隔・小節の幅は、OMR が「どこまでが 1 つの和音か」「どの符頭がどの符幹か」を決める手がかり。今は 1 種類の間隔しか学んでいない | 中 |
| P3 | **五線の大きさを描画の設定として**: `--staff-space 1.5mm`（言語には入れない） | 用紙に対する五線の大きさ（＝1 ページの情報の密度、文字・余白との比）が揺れる。今の `--scale` は全体を拡大するだけで、比は変わらない | 中（`StaffHeight` を設定から読む。言語の判断は変えない） |
| P4 | **グリフの形の値を描画の設定で**（下の表） | `ms_restyle.py` が MuseScore の SVG に後から掛けている揺らぎを、Lily# の描画で正しく出せる。後からの変形と違い、連桁と符幹のつながり、加線の長さなどが崩れない | 中〜大（項目ごとに足せる。`override` の仕組みはすでに汎用） |
| P5 | **記号の外接枠の書き出し**: `lysc boxes song.lys` → ページごとの JSON | OMR の `SvgPage` は SVG から記号を推定している（例: 斜体の小さい文字なら連符の数字、TAB の上の小節線の位置の約束）。種類と枠が出れば推定が要らなくなり、Lily# の描画が変わっても正解が壊れない | 中 |
| P6 | **SMuFL フォント**（Bravura、Leland、Petaluma など） | M2（別フォントへの一般化）の本命。今は MuseScore で回り道している。ROADMAP §5 で「将来ユーザーが入れる」とした項目 | 大 |

### P2 の項目（名前は例。LilyPond の同名の値に合わせる案）

| 名前 | 意味 | 揺らす幅の見立て |
|---|---|---|
| `shortestDurationSpace` | 段の中の最短の音価に与える間隔（五線の間隔の倍数） | 1.5〜3.0 |
| `spacingIncrement`（今ある） | 音価が倍になるごとに足す間隔 | 0.8〜1.8 |
| `minimumNoteDistance` | 符頭どうしの最小の距離（混んだ小節で効く） | 0.8〜1.5 |
| `measureMinimumWidth` | 小節の最小幅 | 0〜8 |
| `measuresPerSystem` / `systemsPerPage` | 1 段の小節数・1 ページの段数を固定する（強制改行を自分で書かずに） | 2〜8 / 2〜10 |

### P4 の項目（どれも「五線の間隔の倍数」で。LilyPond の既定値に近いもの）

| 名前 | 既定（目安） | 揺らす幅の見立て | なぜ要るか |
|---|---|---|---|
| `Stem.length` | 3.5 | 3.0〜4.0 | 符幹の先・連桁の位置（検出器の `stem_N` クラス） |
| `Stem.thickness` | 0.13 | 0.08〜0.2 | 細い符幹を規則が取りこぼす（実スキャン） |
| `Beam.thickness`、`Beam.maxSlope` | 0.48、— | 0.4〜0.55、0〜0.5 | 連桁の数の読み（`DetectedBeams`） |
| `StaffSymbol.thickness`、`LedgerLine.thickness`、`BarLine.thickness` | 0.1、0.16、0.16 | 0.06〜0.2 | 五線の除去と、太い五線の上の符頭 |
| `LedgerLine.length` | 1.6 | 1.3〜2.0 | 加線の数え |
| `NoteHead.scale` | 1.0 | 0.9〜1.15 | 符頭の大きさ（フォントごとに違う） |
| `Dots.distance`、`Accidental.padding` | — | ±30% | 付点・臨時記号の割り当て（S6 の誤りの多くはここ） |

### P5 の書き出しの形（案）

```json
{ "page": 1, "staffSpace": 7.0, "symbols": [
  { "kind": "notehead", "glyph": "noteheadBlack", "box": [x0, y0, x1, y1], "pos": 1234,
    "staff": 3, "voice": 1, "measure": 12, "pitch": "fis'", "duration": "8" },
  { "kind": "stem", "box": [...], "pos": 1234, "beams": 2 },
  { "kind": "tie", "from": [x, y], "to": [x, y], "pos": 1250 },
  { "kind": "tabFret", "glyph": "7", "string": 3, "box": [...], "pos": 1300 } ] }
```

`kind` は SMuFL の名前でも、Lily# の内部の種類でもよい。要るのは **種類・外接枠・`data-pos`・五線の番号** の 4 つ。音高と音価があれば音符単位の正解も同じファイルから取れる（今は生成器が別に `.notes` を書いている）。

## OMR 側での使い方（P1〜P4 が入ったら）

- `run-gen.ps1` が曲ごとに乱数の種から値を引き、`lysc png --set …` と `lysc svg --set …` に同じ値を渡す（PNG と SVG の座標が一致すること）。
- 学習セットを「組み方の揺れあり」で作り直し、Lily# の生成譜の評価（seed 7）は揺れなしと揺れありの両方で採点する。
- 効き目の判定は、別フォントの評価（MuseScore の 8 フォント）と実スキャンが良くなるかで決める（生成譜だけでは決めない）。

## 優先の提案

**P1 と P2 を先に。** 小さく、効き目が大きい（横の間隔は今いちばん揺れていない要素）。次に P5（正解の推定をなくすので、以後の Lily# の変更に強くなる）。
P3・P4 は項目ごとに少しずつ足せる。P6 は大きいので別に判断する。

ライセンスの注意（ROADMAP §6）: これらは Lily# の機能で、OMR は lysc を学習データの生成のときに子プロセスとして呼ぶだけなので、OMR と Lily# の分離は変わらない。
