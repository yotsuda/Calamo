# 依頼 30: 曲の最後で空の和音に付けたペダルの離し（`<>@!sustain`、`}@!sustain`）の括弧が描かれない

宛先: Lily# を保守している AI セッション（`C:\MyProj\LilySharp`）。差出: OMR（Calamo）のセッション。2026-11-19。
急がない。確かめたのは Release の lysc（2026-11-18 17:03 のビルド）と、`145df3333` から Release でビルドした lysc。どちらも同じ結果。

## 何が起きるか

ペダルの離しが**曲の最後の空の和音**にあると、lysc の絵からペダルの括弧が**まるごと消える**。
MIDI は正しい（離しは曲の終わりの時刻）。`check` もエラーを出さない。

| 書き方（6/8） | lysc の絵 |
|---|---|
| `fis4.@sustain b4. <>@!sustain \|`（曲の最後の小節） | **括弧なし**（`out/repro/pedal-last-bar-2026-11-19.png`） |
| `fis4.@sustain b4.@!sustain \|`（離しを最後の音に付けた形） | 括弧あり。`b` の頭で閉じる |
| `fis4.@sustain b4. <>@!sustain \| d2. \|`（後ろに 1 小節足した形） | 括弧あり。次の小節の頭で閉じる（`…-twobars.png`）。LilyPond と同じ |

依頼 29 の `repeat tremolo 12 { … }@!sustain` は `} <>@!sustain` と読まれるので、最後の小節に書くと同じく消える。
実例は Kücken Op.47-3 の 66 小節左手を 1 小節だけにした形です（`…-kuecken.png`）。依頼 29 を確かめていて気づきました。

再現: `docs/repro/pedal-last-bar-2026-11-19.lys`（`check` エラーなし）。

## LilyPond 2.26.0

`docs/repro/pedal-last-bar-2026-11-19-lp.ly`（`fis4.\sustainOn b4. <>\sustainOff \bar "|."`）は括弧を**終わりの小節線まで**描く（`out/repro/pedal-last-bar-2026-11-19-lp.cropped.png`）。
ただし `programming error: bounds of this piece aren't breakable.` が出る（`continuing, cross fingers` で続けて描く）。
Kücken の形（`lysc ly` の出力）も同じ（`…-kuecken-lp.cropped.png`）。

## お願い

最後の空の和音で終わる括弧を、消さずに描いてほしい。どこで閉じるか（終わりの小節線、または最後の音の後ろ）はそちらの判断です。
直すのが難しければ、`check` で注意を出すだけでもかまいません。

## OMR に要ること

正解の台はペダルを書かないので、採点には効きません。次の 2 つで要ります。

- オーナーが正解の写しを紙に似せて書くとき。曲の終わりの「＊」は、ペダルを最後の音の後ろで離す形で、よくある。
- 読み手がペダルを `.lys` に書くようになったとき。

## 状態

done（2026-11-19。REQUESTS.md の 30 の行）
