# Lily# から OMR へ（2026-10-28）: span は開いた名前で閉じる

## 変わったこと（Lily# 側・オーナー決定 2026-10-28）

`@!` は**開いたのと同じ名前**で閉じる。食い違いは**エラー LYS4032**（クイックフィックスで直せる）。

| 開き | 閉じ（新しい規則） | 今まで通っていた閉じ |
|---|---|---|
| `@ottava` / `@ottava(bassa)` | `@!ottava` | （同じ） |
| `@quindicesima` / `@quindicesima(bassa)` | **`@!quindicesima`** | `@!ottava` ← **エラーになる** |
| `@8va` `@8vb` `@15ma` `@15mb` | `@!8va` …（同じ名前） | `@!ottava` など族の名前 ← エラー |
| `@rit` `@accel` `@rall` `@textSpan(…)` | 同じ名前 | 族の中なら何でも ← エラー |

描画と再生は変わらない（族で 1 つの終わり）。変わるのは書き方の規則だけ。Lily# の MusicXML 読み込みも同日に直した。

## お願い

OMR が書く `.lys` で、15ma・15mb の線の閉じを `@!quindicesima` にしてほしい。見つけた箇所は 2 つ:

1. `src/OmrProto/Program.cs` 1129 行: `(e.OttavaStart … "@quindicesima" : "@ottava") … (e.OttavaEnd ? "@!ottava" : "")` — 閉じは開いた線の大きさに合わせて `@!quindicesima` / `@!ottava`。
2. `src/OmrGen/Gen.cs` 506 行: `line[j].Marks += "@!ottava";` — 同じく、505 行で `@quindicesima` を書いたときは `@!quindicesima`。

文字の伸び（`Texts.cs` 188 行の `"!" + span.Split('(')[0]`、`Gen.cs` 363〜365 行）とペダル・松葉はもう名前どおりなので、そのままでよい。

## 互換について

オーナーの方針（「後方互換なし、両側を同時に新しくする」）のとおり、Lily# 側は古い書き方を受け付けない。直す前の OMR が 15ma を読んだ `.lys` は、新しい lysc で LYS4032 になる（エラー文とクイックフィックスが `@!quindicesima` を言う）。
