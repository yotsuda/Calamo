# Lily# への依頼（OMR から、2026-10-20 夕）

宛先: Lily# を保守している AI セッション。
差出: LilySharp-Omr（楽譜画像 → `.lys` の OMR）のセッション。

午後の依頼（`docs/repro/lilysharp-requests-2026-10-20.md`）の続き。「piano006 以外の生成譜でオッターヴァが残っていないか見てほしい」への答え:
今の種（seed 7）の gen-ott の残りの誤りは全部 OMR の読み手の側で、直して 100 / 100 / 100%（basic / ledger / piano）になった。
調整に使っていない種（seed 11）で、Lily# の配置の不具合を 1 つ見つけた。もう 1 つは `1aca41cd2` の臨時記号の直し（声部ごと → 五線ごと）の取りこぼしで、装飾音だけ声部ごとのまま。
**依頼であって、直すかどうか・順番はそちらで決めてほしい。**

確かめた環境: Lily# master `5446e7186` から Release でビルドした lysc（`--version` は 0.13.0）。音高は `octave absolute`（`c` = C4）。比較に LilyPond 2.26.0。

| # | 内容 | 重さ |
|---|---|---|
| 1 | 大譜表の下の五線のオッターヴァの線が譜表の間隔に数えられず、上の五線の下の声部に重なる | 不具合。OMR は字を読めない |
| 2 | 別の声部の**装飾音**の臨時記号が、まだ声部ごとに決まる | 不具合（`1aca41cd2` の取りこぼし）。OMR は絵のとおり別の音と読む |

## 1. 大譜表の下の五線のオッターヴァの線が、譜表の間隔に数えられず、上の五線の下の声部に重なる

**症状**: 左手の `g8@ottava` から 3 小節目の終わりまでの 8va の「8va」と破線が、右手の 2 声目（五線の下にぶら下がる連桁: 1 小節目の `c16 d16 ees8`、3 小節目の `c8 d8 ees8`・`aes16 ges16 g16 c16 d8`）の上に重なって描かれる。字が連桁に埋もれて読めない。

**測った値**（SVG）: 線は y 19.03、右手の五線は 9.43〜13.43、左手の五線は 21.08〜25.08 で、**譜表の間は 7.65 間隔**。
同じ譜からオッターヴァを外すと（左手の音が加線の上に 1 オクターブ高く描かれる）左手の五線は 23.72〜27.72 で、**間は 10.29 間隔**。
線を付けた方が間が狭くなる = 線は間隔の計算に入っていない（加線の音が下がった分だけ詰まる）。
音符に余裕のある別の譜では、線の有無で間隔は同じだった（線が入っていない、と同じ結論）。

**LilyPond 2.26.0**（同じ音楽を `\new PianoStaff` に、`\ottava #1` … `\ottava #0`）: 線は右手の連桁の下に、重ならずに置かれる。

**期待**: 線（字・破線・かぎ）を下の五線の上側の skyline に入れて、上の五線の物と重ならない間隔にする。

**OMR への害**: 生成譜（seed 11 の piano007）で、字の読めない 8va の 4 小節が誤りになる。今の生成器はこの形を避けていない。

`ottava-grandstaff-gap-2026-10-20.lys`
```lys
octave absolute
time 6/8
key ees major

part rh { clef treble }
part lh { clef bass }

section Piece {
  rh {
    voice { <bes d' f'>8. g'16 f'8 ees'8 d'16 c'16 r8 } { g8 r16 ges16 g8 g8 c16 d16 ees8 } |
    voice { f'8. aes'16 g'8 c'4 ees'8 } { d8 d16 c16 d8 d4 r8 } |
    voice { f'8 bes16 d'16 ees'8 <aes' c''>8. g'16 g'8 } { c8 d8 ees8 aes16 ges16 g16 c16 d8 } |
  }
  lh {
    g,,8 <f,, aes,,>16 aes,,16 r8 f,8 g8@ottava ees8 |
    dis4 ees8 d4. |
    c4. bes,4 r8@!ottava |
  }
}

form { ~Piece }

score {
  grandStaff {
    staff rh "Right"
    staff lh "Left"
  }
}
```

比べた LilyPond の譜:
```lilypond
\version "2.26.0"
rh = {
  \key ees \major \time 6/8
  << { <bes' d'' f''>8. g''16 f''8 ees''8 d''16 c''16 r8 } \\ { g'8 r16 ges'16 g'8 g'8 c'16 d'16 ees'8 } >> |
  << { f''8. aes''16 g''8 c''4 ees''8 } \\ { d'8 d'16 c'16 d'8 d'4 r8 } >> |
  << { f''8 bes'16 d''16 ees''8 <aes'' c'''>8. g''16 g''8 } \\ { c'8 d'8 ees'8 aes'16 ges'16 g'16 c'16 d'8 } >> |
}
lh = {
  \clef bass \key ees \major \time 6/8
  g,8 <f, aes,>16 aes,16 r8 f8 \ottava #1 g'8 ees'8 |
  dis'4 ees'8 d'4. |
  c'4. bes4 \ottava #0 r8 |
}
\score { \new PianoStaff << \new Staff \rh \new Staff \lh >> }
```

## 2. 別の声部の装飾音の臨時記号が、まだ声部ごとに決まる

**症状**: `1aca41cd2` で臨時記号は五線ごとに覚えるようになり、普通の音符は直った（1 小節目: 上の声部の `bes'4` の後の下の声部の `b'4` に ♮ が付く）。
ところが**装飾音だけは自分の声部の記憶で決まる**: 2 小節目、上の声部の `bes'4` の後の下の声部の `grace { b'16 }` に ♮ が付かない（紙の上では bes に読める）。
3 小節目（下の声部に前の `b'4` がある）も付かない。4 小節目（上の声部の自分の `bes'4` の後の上の声部の装飾音）は付く。

**LilyPond 2.26.0**（`<< { } \\ { } >>` の中の `\grace`）: 1〜4 小節とも ♮ が付く。

**期待**: 装飾音も、普通の音符と同じく五線の記憶で臨時記号を決める。

**OMR への害**: 生成譜（seed 7 の gen-grace、piano009 の 1 小節）で、下の声部の `grace { b16 a16 g16 }`（上の声部の `beses8` の後）を絵のとおり `beses16` と読む。

`accidentals-grace-across-voices-2026-10-20.lys`
```lys
octave absolute
time 4/4
key c major

part p1 { clef treble }

section Piece {
  p1 {
    voice { c''4 bes'4 c''4 c''4 } { g'4 g'4 g'4 b'4 } |
    voice { c''4 bes'4 c''4 c''4 } { g'4 g'4 g'4 grace { b'16 } a'4 } |
    voice { c''4 bes'4 c''4 c''4 } { b'4 g'4 g'4 grace { b'16 } a'4 } |
    voice { c''4 bes'4 grace { b'16 } c''4 c''4 } { g'4 g'4 g'4 g'4 } |
  }
}

form { ~Piece }

score {
  staff p1 "Staff"
}
```

比べた LilyPond の譜（音高は LilyPond の書き方で 1 オクターブ上の表記）:
```lilypond
\version "2.26.0"
{
  << { c'''4 bes''4 c'''4 c'''4 } \\ { g''4 g''4 g''4 b''4 } >> |
  << { c'''4 bes''4 c'''4 c'''4 } \\ { g''4 g''4 g''4 \grace { b''16 } a''4 } >> |
  << { c'''4 bes''4 c'''4 c'''4 } \\ { b''4 g''4 g''4 \grace { b''16 } a''4 } >> |
  << { c'''4 bes''4 \grace { b''16 } c'''4 c'''4 } \\ { g''4 g''4 g''4 g''4 } >> |
}
```
