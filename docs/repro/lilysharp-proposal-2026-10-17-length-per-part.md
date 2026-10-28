# Lily# から OMR へ（2026-10-17・お願い 1 件: 長さの印をパートごとの `@todo` に）

`bars` はありがとう（`bf0e094`）。Lily# 側で新しい side file を読み、`bars` の集合と復元が一致するのを確かめた。
その上でオーナーから次の報告があった。

## 1. 何があったか

bwv610 の bar 7（長さの印 `b7`・p1 と p2 の両方が短い）で:

```
voice { g4@todo(b7 "bar 7: p1: the lower voice is a dotted quarter short (bar of 4/4); p2: the upper voice is a half note and an eighth short (bar of 4/4)") g4 aes4 g4 } { … } |  // bar 7: the lower voice is a dotted quarter short (bar of 4/4)
```

オーナー: 「この todo は p1 の音符についているけど、todo コメントは p2 の音符にも言及がある。`p1:` とか `p2:` のコメントヘッダは除去して、**それぞれの todo を正しい音符に直接つけてほしい**。todo が p1 にしかつかないのも使いにくい。」

もう一つ、「破線の大きさが揃わない」: scan では `b7` の箱が 2 パートを一つで囲み、`.lys` のプレビューはパートごとに別の枠。
これは Lily# 側で直した（`bars` が 2 つ以上なら scan もパート小節ごとに 1 枠ずつ描く・key は共有）。長さの印がパートごとに割れれば、どちらの画面でも「1 印 1 枠」に戻る。

## 2. お願い

`Sidecar.AddLengthTodo` の「1 小節 1 件（part −1・`Lengths` に各パート）」を、**拍の合わないパートごとに 1 件**にしてほしい:

| 今 | お願い |
|---|---|
| `Todo(bar, -1, "length", "bar 7: p1: …; p2: …", Lengths: [p1, p2])` 1 件 | `Todo(bar, p1, "length", "bar 7: the lower voice is a dotted quarter short (bar of 4/4)")` と `Todo(bar, p2, "length", "bar 7: the upper voice is …")` の 2 件 |
| key `b7` | `BarKey(bar, part)` = `b7-p1`・`b7-p2`（照合の `b4-lh` と同じ形） |
| memo に `p1:` / `p2:` の見出し | 見出しなし＝そのパートの `Memo.BarLength` だけ（1 パートのときの今の文と同じ） |
| `@todo` は最初の合わないパートの先頭の音符 | **それぞれのパートの先頭の音符**（`Annotate` の `PlaceOf` は `Part >= 0` ならそのパートなので、割るだけで付く） |
| confidence は `parts.Min(LengthConfidence)` | そのパートの `LengthConfidence(p.Voices, barTicks)` |
| quiet（照合で合った小節）| パートごと（照合はパート小節単位なので、合ったパートだけ quiet にできる） |
| side file の `parts` は 2 件・`bars` は 2 つ・box は和 | `parts` は 1 件（`delta`・`voices` は今のまま。Lily# は読み続ける）・`bars` は 1 つ・box はそのパート小節 |

残してよいもの:

- **弱起の印**（`first && … a pickup bar?`）は全パートが同じだけ短い「小節の」話なので、今のまま 1 件（part −1・`bars` は全パート）でよい。
- 符尾・連符の印（part −1・`Lengths` なし）も今のまま。`Annotate` がパートの印に合流させる動きもそのまま（割った長さの印にも合流する）。
- `Memo.BarLength` の文・`// bar N: …` の行コメントとの重複消し（`memo.Contains(...)`）は、パートごとの文になるぶんむしろ当たりやすくなる。

版は上げなくてよい（key と件数が変わるだけで、欄は同じ）。既に読んだ譜の side file は今の形のまま Lily# が読む。

## 3. Lily# 側

受け口は要らない。`b7-p1` のような key・1 パートの `bars`・1 件の `parts` は今の Lily# がそのまま読む（照合の印と同じ道）。
入った版を教えてもらえれば、bwv610 の p01 で `@todo` が p1 と p2 の両方の音符に付き、scan とプレビューの枠が 1 印 1 枠で同じ大きさになるのを確かめる。
