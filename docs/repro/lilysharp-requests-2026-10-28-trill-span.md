# Lily# から OMR へ（2026-10-28 夜）: トリルの線は `@trillSpan … @!trillSpan`

## 変わったこと（Lily# 側・オーナー決定 2026-10-28）

トリルの波線（trill span）も、ほかの span と同じく**1 つの名前で開いて閉じる**書き方になった。

| 今まで | これから |
|---|---|
| `c4@startTrillSpan d e@stopTrillSpan` | `c4@trillSpan d e@!trillSpan` |
| `@startTrillSpan.down` | `@trillSpan.down` |

古い 2 語は受け付けない（後方互換なし）。何も描かず、警告 LYS1008 が新しい書き方を言う: 「A trill span is written '@trillSpan' … '@!trillSpan' …」。描画・再生・LilyPond への書き出し（`\startTrillSpan` / `\stopTrillSpan`）は変わらない。同じ音符で前の線を閉じて次を開く `b'8@!trillSpan@trillSpan` も今までどおり（閉じを先に読む）。

## お願い

OMR が書く `.lys` を新しい書き方にしてほしい。見つけた箇所:

1. `src/Omr/Articulations.cs` 211・268 行: `"startTrillSpan"` → `"trillSpan"`。266 行: `"stopTrillSpan"` → `"!trillSpan"`（`Program.cs` 1126 行が `"@" + a` で書くので、`!` を名前に含めれば `@!trillSpan` になる。`Contains` の判定も同じ文字列に）。
2. `src/Omr/Program.cs` 613 行: `Named("trill line", "startTrillSpan", "stopTrillSpan")` → `Named("trill line", "trillSpan", "!trillSpan")`（rit などと同じ形）。617 行の注釈も。
3. `src/OmrGen/Gen.cs` 408・409 行: `"@startTrillSpan"` → `"@trillSpan"`、`"@stopTrillSpan"` → `"@!trillSpan"`。

既にある生成譜の台の正解 `.lys` は、ottava のときと同じく描き直すときに作り直せばよい。
