# Lily# から OMR へ（2026-10-28 夜・2 通目）: オッターヴァとトリルの `@!` は「線がかかる最後の音符」に

## 変わったこと（Lily# 側・オーナー決定 2026-10-28）

`@!ottava`（`@!quindicesima` `@!8va` なども）と `@!trillSpan` は、**線がかかる最後の音符**に書く。その音符も線の中に入る。

| | 今まで | これから |
|---|---|---|
| 8va が c d e f にかかる | `c4@ottava d e f g@!ottava`（g は戻った音） | `c4@ottava d e f@!ottava g` |
| トリルが c d e にかかり、f の手前で止まる | `c4@trillSpan d e f@!trillSpan` | `c4@trillSpan d e@!trillSpan f` |
| トリルを続けてつなぐ（2 本目は e から） | `c4@trillSpan d e@!trillSpan@trillSpan f g@!trillSpan` | `c4@trillSpan d e@trillSpan f@!trillSpan g`（新しい始まりが前を閉じるので、`@!` は要らない） |

文字の伸び（`@rit` など）・松葉・フレーズスラーは、もとからこの書き方だった。ペダルは変わらない（`@!sustain` は踏み上げる音符）。描画と LilyPond への書き出しは、LilyPond と同じ結果になるように合わせた（18 種すべて測って一致）。古い書き方は、エラーにならずに 1 音ずれる（オッターヴァは次の音符まで移調され、トリルは 1 音長くなる）。

## お願い

OMR が書く `.lys` の、オッターヴァとトリルの終わりを 1 音前に。

1. **オッターヴァ**: `Omr/Program.cs` 1129 行あたり（`e.OttavaEnd` で `@!quindicesima` / `@!ottava` を書くところ）と `OmrGen/Gen.cs` 505〜506 行。今は「線の外の最初の音符」に付けているはず（MusicXML の stop の読み方と同じ）。これを「線がかかる最後の音符」に。段末で切れて次の頁へ続く線、形の切れ目で閉じる線も同じ。
2. **トリル**: `Omr/Articulations.cs` 262〜268 行の `PutLine`。今は波の終わる音符（`end`）の `Events` に `!trillSpan` を入れている。これを、その 1 つ前の音符（波がかかる最後の音符）に。続けてつなぐトリル（266 行の注釈「the next line may start on the note this one ends on」）は、新しい始まりだけを書けばよい（前のトリルの終わりは要らない）。
3. **`OmrGen/Gen.cs` 408・409 行**: `notes[i + len].Marks += "@!trillSpan"` を `notes[i + len - 1]` に（407 行の範囲の判定も 1 つずれる）。410 行の注釈（「Lily# pairs the stop before the start」）の書き方は使わなくなる。

Lily# の MusicXML 読み込みも同じ日に直した（オッターヴァの stop は、その前の音符に付く）。

既にある生成譜の台の正解 `.lys` は、ottava のときと同じく描き直すときに作り直せばよい。
