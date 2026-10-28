# schumann15-7 / rje : bars where this reader's reading disagrees with the truth

Each image: this edition's bar (top), the truth's bar as Mutopia prints it (bottom), each in a red frame, shown from its system's start on that staff (so the clef, key and time, and any clef change before the bar, are in the picture).
Only notes and rests count (pitch and length, accidentals that change a pitch, chord notes, how rests are written). Slurs, marks, dynamics and fingering do not.
Answer in the last columns: **same** (this edition = the truth: the OMR misread it) or what this edition has instead.

| bar | staff | image | edition = truth? | note |
|---|---|---|---|---|
| 2 | up | [b02-up.png](b02-up.png) | same | |
| 3 | up | [b03-up.png](b03-up.png) | diff | c8 f8 は下の声部だけ。上の声部の頭は s4 で、Mutopia の下の声部の r4 が無い → r4 を s4 に |
| 5 | down | [b05-down.png](b05-down.png) | diff | f と a を下の声部の和音 `<f a>4` に。上の声部の r4 が無い → s4 |
| 6 | up | [b06-up.png](b06-up.png) | same | |
| 7 | up | [b07-up.png](b07-up.png) | diff | 3 小節と同じく r4 が無い → s4 |
| 9 | up | [b09-up.png](b09-up.png) | same | 4 拍目の r8 は下の声部（Mutopia ではどちらか曖昧） |
| 9 | down | [b09-down.png](b09-down.png) | diff | 上の声部の c8 は下の声部の和音 `<c c'>8` に。r8 r4 が無い → s8 s4 |
| 10 | up | [b10-up.png](b10-up.png) | same | |
| 12 | up | [b12-up.png](b12-up.png) | diff | 最後の bes8 c8 が bes4 |
| 12 | down | [b12-down.png](b12-down.png) | diff | 上の声部の最後に c8（ees8 と同時）がある → 足した |
| 17 | down | [b17-down.png](b17-down.png) | diff | 4 拍目の下の音は ♮b（Mutopia の正解は bes のタイ）。c8 は和音の中で、r8 が無い → b8 に、r8 を s8 に |
| 18 | up | [b18-up.png](b18-up.png) | same | 違いは装飾音符の有無だけ（採点の対象外） |
| 19 | up | [b19-up.png](b19-up.png) | diff | 3 小節と同じ → s4 |
| 21 | down | [b21-down.png](b21-down.png) | diff | 5 小節と同じ → s4 |
| 22 | up | [b22-up.png](b22-up.png) | same | r4 に透かしが被る |
| 23 | up | [b23-up.png](b23-up.png) | same | 指番号が五線の中にある |
| 24 | down | [b24-down.png](b24-down.png) | diff | 2 拍目が `<c c'>2`（c' の符幹は上） → 足した |

2026-10-15、オーナーの答え。違う小節は `truth-rje/page.ly` で直した。`.notes` の差は狙いどおり（休符 8 つが無くなる、c が 2 つ加わる、bes8 c8 → bes4、bes → b）。
採点し直し（正解の音符と休符 406）: この OMR 97.5%、Audiveris 97.0%。
