# Lily# から OMR へ（2026-11-01）: 音価にならない tuplet の警告 LYS2020

## きっかけ

種 `C:\MyProj\omr-seeds\20261101-110443-bohemian-rhapsody-bar124-p1`（p1 の 124 小節）。スキャンの括弧 `3` は 4 分＋8 分を覆っているのに、読みは `ees''4 tuplet 3/2 { g''8 }` で、8 分だけを括弧に入れた。Lily# は小節が 13/12 だと言う（LYS2002）だけで、括弧そのものは咎めていなかった。オーナーの問い「妥当な警告を出せば OMR の助けになるか」への答えとして、Lily# 側に入れた（commit `5b9e11390`・次の Release lysc から。それまでは Lily# の worktree のビルド）。

## Lily# が新しく言うこと

- **LYS2020（警告）**: tuplet の中の音が、比率の個数分の 1 つの音価に足し合わさらないとき。tuplet の鳴る長さ（中身の書かれた長さ × 底/比）の分母が 2 の冪でなければ鳴る。`tuplet 3/2 { g8 }` は 1/12、`tuplet 3/2 { d8 e }` は 1/6 で鳴り、`tuplet 3/2 { c4 d8 }`・`{ c8 d e }`・`tuplet 5/4 { c16 c c c c }` は鳴らない。入れ子は内側と外側を各自で見る。1 つの tuplet に 1 度。
- 位置は **括弧の `tuplet` から `}` まで**（小節ではない）。LYS2002 は今までどおり小節に出る。
- 文言（英語のまま）:

  ```
  warning LYS2020: this tuplet sounds 1/12 of a whole note, a length no note value writes: the notes inside a 3/2 bracket add up to 3 of one value played in the time of 2 (three eighths, or a quarter and an eighth) — a note the bracket covers may have been left outside it. at [NNN..MMM)
  ```

  3/2・5/4・6/4・7/4・2/3 には括弧の中身の例が付く（上の「three eighths, or a quarter and an eighth」）。

## お願い

形はそちらで決めてほしい。Lily# 側から見た一案:

1. **`omr verify` が読む**: `Verify.cs` の `warning LYS200[12]:` の正規表現は LYS2020 を拾わない。LYS2020 の `[NNN..` は括弧の位置なので、`SpanOf` でその小節を引き、`byLength` と同じく小節を `flagged` にしてほしい。小節の長さの過不足（LYS2001/2002）は括弧の取りこぼしの帰結で、原因は括弧にある。
2. **memo の文**: `@todo` の memo に「the tuplet's bracket is missing a note (lysc LYS2020)」のような一言。オーナーがこの小節を開いたとき、どこを見ればよいかが分かる（今の memo は「the lower voice is ⅙ quarters short」で、原因は言えていなかった）。
3. **読みの再試行の材料**: 括弧の数字 `3` の左右で、括弧の線の長さに対して中の音が 1 つしかない読みは、括弧を 1 つ前の音まで広げると音価になる（4 分＋8 分 → 3/2 で 1/4）。LYS2020 が出た小節だけで試せば、ほかの小節は動かない。
4. **種 bar124-p1 の triage**: `cause` は `rules`（括弧の範囲の組み立て）と見ている。同じ seed の `note` にある「8 分休符が別の voice に入っていた」は、Lily# の過不足検査（LYS2001）が既に memo に書いていた。

## Lily# 側で変えないこと

- LYS2001/2002 の文言と位置は変えていない。`omr verify` の今の読みはそのまま動く。
- LYS2020 は警告で、描画と再生は今までどおり（1/12 のまま描く・LYS2002 が小節の後ろを切る）。
