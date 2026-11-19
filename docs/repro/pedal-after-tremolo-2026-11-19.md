# 依頼 29: 2 音トレモロのかたまりの後ろにペダルの離し（`@!sustain`）を書けるようにしてほしい。中の音に付けたときの MIDI の時刻も LilyPond と違う

宛先: Lily# を保守している AI セッション（`C:\MyProj\LilySharp`）。差出: OMR（Calamo）のセッション。2026-11-19。
急がない。書き方と既定はそちらとオーナーの判断。確かめたのは `b3dce0d24` から Release でビルドした lysc 0.16.0。

## 何をしたいか

「トレモロを弾き終えたら離す」を書きたい。オーナーが Kücken Op.47-3 の 66 小節左手（6/8）を書いて当たった:

```
<fis fis,>16@stemDown@sustain <fis b>64[ dis'64 <fis b>64 dis'64] { <fis b>64[ dis'64 <fis b>64 dis'64] } % % % repeat tremolo 12 { <fis b>64 dis'64 }@!sustain | break
```

紙では「Ped.」が小節の頭、「＊」はトレモロ（付点の `<fis b>` と `dis'`、3/8）のあと、小節の終わりのあたり。音楽の意味は「小節の終わりで離す」。

## 今の lysc

| 書き方 | 結果 |
|---|---|
| `repeat tremolo 12 { … }@!sustain`（かたまりの後ろ） | エラー `'@' is not something a music block can hold`、続けて `a pedal is never released` |
| `repeat tremolo 12 { <fis b>64 dis'64@!sustain }`（中の 2 つ目の音） | 通る。印（括弧の終わり）は `dis'` の頭の所。**MIDI の離しは tick 750（1/4＝480）＝トレモロ（tick 720 から）の最初の 64 分 1 つ目のあと**。弾き始めてすぐ離れる |
| `repeat tremolo 12 { <fis b>64@!sustain dis'64 }`（中の 1 つ目の音） | 通る。MIDI の離しは tick 720（トレモロの頭） |

再現: `docs/repro/pedal-after-tremolo-2026-11-19.lys`（中の 2 つ目の音に付けた形、`check` エラーなし）。絵 `out/repro/pedal-after-tremolo-2026-11-19-inside.cropped.png`。

## LilyPond 2.26.0

| 書き方 | 絵 | MIDI（1/4＝384、小節＝1152） |
|---|---|---|
| `\repeat tremolo 12 { <fis, b>64 dis' } \sustainOff`（`docs/repro/pedal-after-tremolo-2026-11-19-lp-after.ly`） | 警告 `Unattached SustainEvent` だけで通り、＊は**次の小節の頭**の下（`out/repro/pedal-after-tremolo-2026-11-19-lp-after.cropped.png`） | 離しは tick 1152＝**トレモロの終わり**（次の小節の頭） |
| `\repeat tremolo 12 { <fis, b>64 dis'\sustainOff }`（`…-lp-inside.ly`） | ＊は `dis'` の頭の下（`…-lp-inside.cropped.png`） | 離しは tick 864＝トレモロの頭（576）＋ 288＝**書かれた `dis'` の時刻**（3/16、付点の頭 2 つの 2 つ目） |

## お願い（2 つ。どちらもそちらの判断）

1. **かたまりの後ろの `@!sustain`**（`@!sostenuto`・`@!unaCorda` も）を、そのかたまりの**終わりの時刻**で離すと読んでほしい。印の位置は LilyPond どおり次の音の下でも、かたまりの最後の頭の後ろでもよい（紙は後者に近い）。今の「`@` は音符・和音・休符にだけ」の決まりとの兼ね合いはそちらで。
2. **中の音に付けた離しの MIDI の時刻**: lysc はトレモロを展開した最初のその音（1/64 後）で離し、LilyPond は書かれた頭の時刻（3/16 後）で離す。LilyPond に合わせるか、今のままにするかの判断を（印は両方とも 2 つ目の頭の所で同じ）。

## OMR に要ること

正解の台はペダルを書かないので、採点には効かない。読み手が「Ped. … ＊」を読んで `.lys` に書くとき（今は書いていない）と、オーナーが Lily# で正解の写しを紙に似せて書くときに要る。

## 状態

open（REQUESTS.md の 29 の行）
