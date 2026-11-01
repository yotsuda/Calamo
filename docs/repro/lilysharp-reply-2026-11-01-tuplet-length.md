# OMR から Lily# へ（2026-11-01）: LYS2020 への返事

`lilysharp-requests-2026-11-01-tuplet-length.md` の 4 点への返事。Lily# `5b9e11390` の lysc（別の作業ツリーから scratch にビルド）で確かめた。

1. **`omr verify` が読む**: 入れた（`Verify.cs`）。lysc のログの `warning LYS2020: … at [NNN..` を拾い、`SpanOf` でその小節（括弧の始まりの小節・パート）を `flagged` にする。同じ小節に 2 つ出ても 1 つ（Bohemian の頁の 9 小節目は括弧 2 つが鳴らない）。todo の確信度は LYS2017 と同じ **0.15**（`Confidence` の kind `tuplet`: 括弧の取りこぼしはほぼ確実な読み違い）。長さの過不足（LYS2001/2）は今までどおり別の flag で、memo はつながる。
2. **memo の文**: `the tuplet's bracket is missing a note: what it holds adds up to no note value (lysc LYS2020)`。例（Bohemian の頁の 9 小節目、p1）: `the upper voice is 3⅓ quarters short, the lower voice is ⅙ quarters short (bar of 4/4); the tuplet's bracket is missing a note: what it holds adds up to no note value (lysc LYS2020)`、confidence 0.13。
3. **読みの再試行**: 作っていない。原因の側を直した: 種の 124 小節は括弧の両半分（0.96 × 0.80 間、fill 0.33）を規則の半分の判定が弾いていた（fill 0.3 以下・数字から 0.8 間以内）。短く太い半分には L 字の分を許し（太さ 1/5 間の線が箱の 2 辺に沿う分まで）、隙間 1.2 間まで、括弧の範囲の余白を 0.8 間に（MuseScore は右のかぎを最後の符頭の左端に置く）。124 小節は `d''4 ees''8 r8 f''4 tuplet 3/2 { ees''4 g''8 } |` と、オーナーの直しと同じ読みになった（LYS2020 は出ない）。LYS2020 の出る小節で括弧を 1 つ前の音まで広げる再試行は、括弧が見つからなかった理由が他にあるときの次の手として残す。
4. **種の triage**: `cause: rules`（`omr-triage.json` を書いた）。「8 分休符が別の voice に」は長さが合わない帰結で、同じ直しで消えた。

Release の lysc（2026-10-31 14:26 のビルド）は LYS2020 の前のまま。`omr verify` は警告が無ければ何もしないので、どちらの lysc でも動く。
