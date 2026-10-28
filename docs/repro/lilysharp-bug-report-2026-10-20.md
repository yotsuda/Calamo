# Lily# 不具合レポート（OMR から、2026-10-20）

宛先: Lily# を保守している AI セッション。
差出: LilySharp-Omr（楽譜画像 → `.lys` の OMR）のセッション。OMR は Lily# が描いた生成譜を正解つきの学習・評価データに使うので、
「`.lys` に書いたとおりに描かれない」箇所に気づきやすい。下はそうして見つけたもので、**依頼ではなく報告**。直すかどうか、直す順はそちらで決めてほしい。

## 確かめた環境

- **lysc 0.13.0**（`LilySharp.Cli\bin\Release\net10.0\lysc.exe`、2026-10-17 12:37 のビルド）で、2026-10-20 に全件を描き直して絵で確かめた。
  ソースはその後 `edddc4978`（2026-10-18）まで進んでいるので、**HEAD で直っているものがあるかもしれない**。
- 各再現は `lysc png --scale 3 <file>.lys` で見られる。`.lys` は全部この文書に埋め込んだ（同じものが LilySharp-Omr の `docs/repro/` にもある）。
- 音高は Lily# の `octave absolute`（`c` = C4、`c'` = C5）で書いてある。

## 一覧

| # | 内容 | 確度 | 見つけた日 |
|---|---|---|---|
| 1 | 大譜表の**下の五線**のテキストの線（`@rit` など）が、上の五線の列から描かれる | 確実 | 2026-10-19 |
| 2 | 大譜表の**下の五線**のトリルの線（`@startTrillSpan`）が、上の五線の列から描かれる | 確実 | 2026-10-19 |
| 3 | **2 声目**の和音のアルペジオが、1 声目の同じ番号の事象の列に描かれる | 確実 | 2026-10-20 |
| 4 | **連符の中**の和音のアルペジオが描かれない（警告なし） | 確実 | 2026-10-20 |
| 5 | 段の終わり近くで始まり次の段へ続くトリルの線の、**最初の段の「tr」と波が描かれない** | 確実 | 2026-10-19 |
| 6 | 段をまたぐオッターヴァの続き「(8vb)」が別の段・別の五線に出る（大譜表） | 確実（最小化はできていない） | 2026-10-18 |
| 7 | 段をまたいだオッターヴァの続きが**連符の中で終わる**と、続きの線が描かれず、連符の頭の音が実音の高さに描かれる | 確実 | 2026-10-18 |
| 8 | 臨時記号を**声部ごと**に覚える（LilyPond の既定は五線ごと） | 仕様か不具合か要判断 | 2026-10-18 |
| 9 | 2 音以上の `acciaccatura` の連桁に斜線が無い | LilyPond と要照合 | 2026-10-18 |
| 10 | オッターヴァの下で、臨時記号を付け直すかを実音で決める | LilyPond と要照合 | 2026-10-18 |

1〜3 はおそらく同じ根: **声部・五線の中での事象の番号（`ItemIndex` / `AnchorItemIndex`）を、小節の列（上の五線または 1 声目の事象の並び）の番号として引いている**。
`TextSpannerEngraver`・`TrillSpannerEngraver`・`ArpeggioEngraver` が `LayoutUtilities.GetItemXOffset(measures, MeasureIndex, ItemIndex)` 系で x を引くところを見てほしい。
同じ形の引き方をしているほかの engraver（グリッサンドの `GlissandoEngraver` も `GetItemXOffset(measures, …ItemIndex…)` を使う。ただしグリッサンドは 1 声目・2 声目とも最小の譜では正しい列に描かれた）も合わせて確かめると早いかもしれない。

前に報告して**直っていたもの**: 大譜表で、もう一方の五線が何も始めない時刻の松葉が別の列から描かれる件（`hairpin-start-grandstaff-2026-10-14.lys`）。0.13.0 で、段の切れ目のある版・ない版とも音符から描かれる。

---

## 1. 大譜表の下の五線のテキストの線が上の五線の列から描かれる

**症状**: 左手の 2 つ目の音符 `e4@rit` の「rit.」が、右手の 2 つ目の 8 分（d''8）の列から描かれる（5 間隔早い）。破線の終わりも、左手の `g4@!rit` ではなく右手の 3 つ目の列（e''8）で決まる。
**期待**（LilyPond）: 「rit.」は左手の e（1 小節目の 3 拍目）から、破線は 2 小節目の左手の g の手前まで。
**見当**: `TextSpannerEngraver` が `StartItemIndex`/`EndItemIndex`（その五線の中の番号）を、上の五線の小節の列の番号として使っている。

`textspan-grandstaff-lower-2026-10-19.lys`
```lys
title "textspan grand staff"
octave absolute
time 4/4

part rh { clef treble }
part lh { clef bass }

section Piece {
  rh {
    c''8 d''8 e''8 f''8 g''8 a''8 b''8 c'''8 | c''8 d''8 e''8 f''8 g''8 a''8 b''8 c'''8 | c''1 |
  }
  lh {
    c2 e4@rit g4 | c2 e4 g4@!rit | c1 |
  }
}

score { grandStaff { staff rh staff lh } }
```

## 2. 大譜表の下の五線のトリルの線が上の五線の列から描かれる

**症状**: 左手の `d4@startTrillSpan`（2 拍目）の「tr」が、右手の 2 つ目の列（d''8）から描かれる（2.5 間隔早い）。波も左手の `f4@stopTrillSpan` ではなく右手の 4 つ目の列（f''8）の手前で終わる。2 小節目の「tr」は正しい（1 つ目の列は両方の五線で同じ）が、波は左手の b,4（4 拍目）ではなく右手の 3 つ目の列（e''8）の手前で終わる。
**期待**: 「tr」は左手の d から、波は左手の f の手前まで。2 小節目の波は左手の b, の手前まで。
**見当**: 1 と同じ形（`TrillSpannerEngraver`）。

`trillspan-grandstaff-lower-2026-10-19.lys`
```lys
title "trill span grand staff"
octave absolute
time 4/4

part rh { clef treble }
part lh { clef bass }

section Piece {
  rh {
    c''8 d''8 e''8 f''8 g''8 a''8 b''8 c'''8 | c''8 d''8 e''8 f''8 g''8 a''8 b''8 c'''8 | c''1 |
  }
  lh {
    c4 d4@startTrillSpan e4 f4@stopTrillSpan | g,2@startTrillSpan a,4 b,4@stopTrillSpan | c1 |
  }
}

score { grandStaff { staff rh staff lh } }
```

## 3. 2 声目の和音のアルペジオが別の列に描かれる

**症状**: 下の声部の 3 つ目の事象 `<d' f' a'>8@arpeggio`（3/8 の位置）の波が、その和音の前ではなく、2/8 の位置の f'8 の前（上の声部の 3 つ目の事象 r8 の列）に描かれる。
**期待**: 波は 3/8 の位置の和音 `<d' f' a'>` の左。
**見当**: `ArpeggioEngraver` が `GetItemXOffset(measures, MeasureIndex, ItemIndex)` で、声部の中の番号を五線の小節の事象の番号として引いている。

`arpeggio-second-voice-2026-10-20.lys`
```lys
title "arpeggio in a second voice"
octave absolute
time 4/4
key c major

part p1 { clef treble }

section Piece {
  p1 {
    voice { r8 g''8 r8 e''8 c''2 } { d'4 f'8 <d' f' a'>8@arpeggio <c' e'>2 } |
  }
}

form { ~Piece }

score {
  staff p1 "Staff"
}
```

## 4. 連符の中の和音のアルペジオが描かれない

**症状**: `@arpeggio` の付いた和音 4 つのうち、連符の外の 2 つ（最初の 4 分と最後の 2 分）だけに波が描かれる。連符の中の 2 つは描かれず、警告も出ない（SVG にも U+E06F が 2 つしかない）。
**期待**: 4 つとも描かれる。

`arpeggio-in-tuplet-2026-10-20.lys`
```lys
title "arpeggio in a tuplet"
octave absolute
time 4/4
key c major

part p1 { clef treble }

section Piece {
  p1 {
    <c' e' g'>4@arpeggio tuplet 3/2 { <c' e' g'>8@arpeggio <d' f'>8 <e' g'>8@arpeggio } <c' e' g'>2@arpeggio |
  }
}

form { ~Piece }

score {
  staff p1 "Staff"
}
```

## 5. 段の終わり近くで始まるトリルの線の最初の段が描かれない

**症状**: 4 小節目の `b,,16@startTrillSpan` は 1 段目の最後から 2 つ目の音符で、線は 5 小節目の `a,,16@stopTrillSpan` まで続く。1 段目の 4 小節目には「tr」も波も無い。2 段目の頭には続きの波（「tr」なし、これは正しい）があり、a,,16 の手前で終わる。
同じ曲（生成譜の全体）で、段の途中で始まって次の段へ続く線は、最初の段の「tr」と段末までの波が描かれる。つまり**始まりが段末に近いときだけ**落ちる。
**期待**: 4 小節目の b,,16 の上に「tr」、その波が 1 段目の終わりまで。続きは今のまま。

`trillspan-broken-start-2026-10-19.lys`
```lys
title "trill span broken start"
octave absolute
time 3/4
key g major

part p1 { clef bass }

section Piece {
  p1 {
    d,8. a,16 a,8 bes,8 e,16 r16 d,16 e,16 |
    d,16 e,16 b,8 c4 b,16 a,16 b,8 |
    tuplet 3/2 { c8 b,8 c8 } b,4. b,8 |
    a,8 b,16 c16 b,8. d,16 c,16 b,,16@startTrillSpan a,,8 |
    a,,8 g,,16 a,,16@stopTrillSpan b,,4. a,,8 |
    r2. |
    g,,2. |
    r16 fis,,16 g,,16 a,,16 b,,8 c,16 fis,,16 c,4 |
    b,,4 a,,8. b,,16 a,,8. g,,16 |
    f,,4 e,,8 c,8 b,,4 |
    a,,8 e,8 fis,2 |
    g,4 a,16 d,16 c,8 bes,,8. b,,16 |
    e,,4 fis,,2 |
    e,,2. |
    r2. |
    fis,,4. e,,8 fis,,8 g,,16 e,16 |
  }
}

form { ~Piece }

score {
  staff p1 "Staff"
}
```

## 6. 段をまたぐオッターヴァの続きが別の段・別の五線に出る（大譜表）

最小化できていない（同じ形の 6 小節の譜では正しく描かれる）ので、生成譜をそのまま添える。

**症状**（0.13.0、2026-10-20 に描き直して見たもの。2026-10-18 の 0.13.0 以前の報告と描かれ方が少し違う）:
- 左手の 4 小節目の `<e,, gis,,>16@ottava(bassa)`（段 2）から段末までの 8vb が、段 2 の左手の下に見当たらない。
- 段 3 では「8vb」と破線が**右手の上**に描かれる（6 小節目から段末まで）。左手の 6 小節目の `fis,,,1.@ottava(bassa)` の線と思われるが、左手の下には無い。
- 段 4 の頭では「(8vb)」が右手の上と左手の下の両方に出る。右手の上の「(8vb)」は破線とかぎつきで、右手自身の 8va（7 小節目の `cis'4.@ottava`）と重なる。
- 合わせて、大譜表の曲では**線の字が最初の音符から大きく離れて描かれる**ことがある（SVG で、4 小節目の「8vb」は x 61.87、それが掛かる和音 `<e,, gis,,>` の符頭は 76.83。2026-10-18 に生成譜 30 曲・83 本で数えたとき、左へ 5.7〜45.8 間隔ずれたものが 9 本、右へ 3.9〜18.7 間隔ずれたものが 4 本で、全部大譜表の曲。1 段の曲では起きない）。この 2 つ目の点は 2026-10-20 には SVG の座標を 1 か所見ただけ（同じ値）。
**期待**: 4 小節目の 8vb は段 2 の左手の下に、続き「(8vb)」は段 3 の頭の左手の下に。6 小節目の 8vb は左手の下。字はそれぞれ最初の音符の位置から。

`ottava-continuation-2026-10-18.lys`（生成譜 piano006。頭の長いコメントはここでは縮めた。音楽は同じ）
```lys
// An octave line broken at a system's end: its continuation "(8vb)" is drawn one system too far
// down, and over the upper staff instead of under the lower one. (OmrGen --ottava, piano006 of
// seed 7, as generated; lysc 0.13.0, 2026-10-18.)
title "piano006"
octave absolute
time 12/8
key b major

part rh { clef treble }
part lh { clef bass }

section Piece {
  rh {
    voice { fis'16 gis'16 ais'16 <gis' ais'>16 ais'8 r8. <g' b'>16 ais'8 r8 gis'8 fis'8 gis'8 ais'16 gis'16 fis'8 } { fisis4 e8 <a cis'>8. <e fis>16 r8 r16 disis16 ais16 a16 <gis b>8 ais8. ais16 gis8 } |
    voice { cis'4 cis'8 <dis' e'>8. <cis' e' gis'>16 <disis' fis' ais'>8 gis'8 e'8 fis'8 r8 cis'8 b8 } { <fis ais cis'>4 ais8 gis8. gis16 fis8 e4. cis8. r16 d8 } |
    voice { cis'8 r16 dis'16 cis'8 fis'4 b8 cis'4. cis'8 b16 e'16 ais'8 } { cis8. r16 r8 r8. dis16 gis8 ais16 ais16 gis16 fis16 cis8 a4 gis8 } |
    voice { <ais' cis''>1. } { ais4. r16 gis16 ais16 <gis b>16 ais8 r4 cis8 dis4 e8 } |
    <ais cis'>8 gis8 dis8 b8 fis'16 gis'16 fis'8 e'8 <ais cis' e'>8 ais8 r8 gis16 ais16 r8 |
    <ais cis'>16 e16 r16 fis16 e8 dis8 e16 fisis16 gis8 ais8 gis16 fis16 <gis b dis'>8 gis4. |
    <disis fis>16 fis16 e16 cis16 fis8 e4 dis8 cis'4.@ottava <dis' fis' ais'>4. |
    ais'4 gis'8 <dis'' fis''>4. <ais cis' e'>8.@!ottava b16 b8 e'8. dis'16 <cis' e'>8 |
    dis'8 ais8 r8 gis8. dis16 e8 dis16 cis16 fis16 <gis b dis'>16 ais8 <b dis' fis'>8. ais16 dis'8 |
    cis'16 bis16 r16 ais16 ais8 <a cis' e'>16 <dis fis ais>16 cis16 dis16 cis8 eis4. dis8 e16 b16 b8 |
    cis'4. r8 dis'16 <e' fis'>16 ais8 ais8 gis16 <ais b>16 ais8 <e gis b>4. |
    e8 dis16 <cis e gis>16 dis8 fis8 ais8 gis8 e'4. <e' gis' b'>16 fis'16 <gis' ais'>16 a'16 gis'8 |
    <e' fis'>16 dis'16 cis'16 r16 <dis' e'>8 dis'4 c'8 <fis' ais' cis''>16 <ais' cis''>16 gis'16 <b cis'>16 cis''8@ottava disis''16 gis'16 ais'16 bis'16 cis''8 |
    dis''16 e'16@!ottava ais16 dis'16 <e' gis'>8 f'4 fis'8 <gis' b'>8 r16 e'16 r8 ais4. |
    b8 cis'16 dis'16 cis'8 ais8 b16 ais16 b8 cis'4 r8 r4. |
    b4 fis'8 gis'8 r8 gis'8 gis'8 fis'16 gis'16 aisis'8 gis'4 ais'8 |
  }
  lh {
    r8 b,,16 ais,,16 <b,, cis,>8 <fis, ais,>4. e,8. <fis, gis,>16 cis8 gis,4. |
    ais,1. |
    dis,4. e,4. dis,8 e,16 gis,16 gis,8 r16 r16 <cis e>16 b,16 cis8 |
    r4. b,16 <a, cis e>16 b,16 <e,, gis,,>16@ottava(bassa) dis,,8 dis,,8. cis,,16 <dis,, fis,,>8 b,,4 cis,8 |
    cis,4 <fis,, ais,,>8 gis,,4. <dis,, fis,,>8 <ais,,, b,,,>8 b,,8@!ottava <b,, dis,>8 <b,, dis, fis,>16 <cis, dis,>16 e,,8 |
    fis,,,1.@ottava(bassa) |
    <fis,,, ais,,,>4. cis,,4 b,,,8 <gis,, b,,>4. ais,,4. |
    <gis,, ais,,>4. r8 b,,,8 gis,,,8 fisis,,,8. r16 e,,8@!ottava e,,8 r8 e,,8 |
    b,,4. <e,, gis,,>16 gis,,16 <gis,, b,,>16 fis,,16 <e,, gis,,>8 fis,,16 gis,,16 r16 cis,,16@ottava(bassa) dis,,8 <e,, gis,,>4. |
    b,,,8 r8 cis,8@!ottava <b,, dis,>8 gis,,16 ais,,16 r8 fisis,,4. eis,,4 cis,8 |
    <b,, dis, fis,>8 r8 ais,,8 b,,16 gis,16 ais,16 b,16 <ais, cis e>8 b,4 ais,8 bis,8 cis8 b,8 |
    ais,8 b,8 ais,8 <b, cis>16 <gis, b, dis>16 gis,16 ais,16 gis,8 ais,8 gis,8 fis,8 <eis, gis, b,>8 fis,8 <e, gis,>8 |
    dis,8 <e, fis,>8 r8 <dis, fis,>8 r16 e,16 <dis, e,>8 cis,8. b,,16 r8 ais,,8 <fis, ais, cis>16 <gis, b,>16 fis,8 |
    gis,16 <ais, cis e>16 <gis, b,>16 cis16 <gis, b, dis>8 <f, ais,>8 fis,16 cis16 b,8 <gis, b,>4 ais,8 b,8 r16 r16 e,8 |
    e,4. r8. fisis,16 gis,8 <gis, b,>4 gis,8 <fis, ais,>8 b,,8 <aisis,, b,,>8 |
    b,,8 cis,16 ais,,16 e,8 ais,,8 b,,8 <fis,, ais,, cis,>8 gis,,4. <e,, gis,, b,,>16 <fis,, ais,,>16 gis,,16 dis,16 <e, gis,>8 |
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

## 7. 段をまたいだオッターヴァの続きが連符の中で終わると、続きが描かれず、音が実音の高さに出る

**症状**: 2 小節目の `fis''4@quindicesima` から始まる 15ma が段末（`break`）を越え、3 小節目の連符の中（`c'8@!ottava` の前）で終わる。段 2 の頭の「(15ma)」は最初の音符の上だけで、破線もかぎも無い。連符の頭の `des'''8`（まだ線の下）は、2 オクターブ下げずに実音の高さ（五線の上 6 本の加線）に描かれ、`c'8`・`bes8` と連桁が大きく折れる。
**期待**: 「(15ma)」と破線が `c'''4` と `des'''8` の上に（2 オクターブ下で描かれ）、かぎは `c'8` の手前。

`ottava-tuplet-continuation-2026-10-18.lys`
```lys
title "ott-tuplet"
octave absolute
time 4/4
key aes major

part p1 { clef treble }

section Piece {
  p1 {
    c'4 d'4 ees'4 f'4 |
    fis''4@quindicesima ges''4. aes''8 tuplet 3/2 { g''8 c'''8 bes''8 } | break
    c'''4 tuplet 3/2 { des'''8 c'8@!ottava bes8 } r4 aes4 |
    c'1 |
  }
}

score { staff p1 }
```

## 8. 臨時記号を声部ごとに覚える（仕様か不具合か、判断してほしい）

**症状**: 1 小節目、上の声部の `fes'`（♭ が付く）の後の、下の声部の同じオクターブの `f'` に ♮ が付かない。2 小節目、上の声部の `g'`（♮ が付く）の後の、下の声部の `ges'`（調号どおり）に ♭ が付かない。紙の上では、1 小節目の f' は fes'、2 小節目の ges' は g と読める。
**LilyPond**: 既定の `Accidental_engraver` は Staff にあり、臨時記号は五線ごとに効く（♮ と ♭ を付ける）。声部ごとに効かせるのは `\accidentalStyle voice` のとき。
**お願い**: Lily# が声部ごとを意図しているなら仕様として教えてほしい（OMR は紙から区別できないので、五線ごとの決まりで読み続ける）。意図していないなら LilyPond の既定に合わせてほしい。

`accidentals-across-voices-2026-10-18.lys`
```lys
octave absolute
time 4/4
key des major
part p1 { clef treble }
score { staff p1 }
section Piece {
  p1 {
    voice { fes'4 ges'4 aes'4 bes'4 } { des'4 ees'4 f'4 ges'4 } |
    voice { g'4 aes'4 bes'4 c''4 } { des'4 ges'4 aes'4 bes'4 } |
  }
}
form { ~Piece }
```

## 9. 2 音以上の acciaccatura の連桁に斜線が無い（LilyPond と照合してほしい）

**症状**: 1 小節目の `acciaccatura { e16 f16 }` が、2 小節目の `grace { e16 f16 }` とスラーのほかは同じ絵になる（連桁に斜線が無い）。3 小節目の 1 音の `acciaccatura { f8 }` には旗に斜線がある。
**照合してほしいこと**: LilyPond の `\acciaccatura` の斜線（`Stem.stroke-style = "grace"`）は旗に描くもので、**連桁の組に斜線が描かれるかは、こちらでは確かめていない**。LilyPond も描かないなら、これは LilyPond どおり（OMR は生成器で 2 音の acciaccatura を書かないようにしてあるので、急ぎではない）。
（このファイルは前の版が古い `score { part … }` の書き方でエラーになっていたので、2026-10-20 に今の書き方で書き直した。絵は同じ。）

`acciaccatura-beamed-2026-10-18.lys`
```lys
octave absolute
time 4/4
key c major

part p1 { clef treble }

section Piece {
  p1 {
    acciaccatura { e16 f16 } g4 a4 b4 c'4 |
    grace { e16 f16 } g4 a4 b4 c'4 |
    acciaccatura { f8 } g4 a4 b4 c'4 |
  }
}

form { ~Piece }

score {
  staff p1 "Staff"
}
```

## 10. オッターヴァの下で、臨時記号を付け直すかを実音で決める（LilyPond と照合してほしい）

**症状**: 1 小節目、8va の下の `cisis''` は 1 オクターブ下（第 3 間）に × つきで描かれる（正しい）。線の後の `cis'8` は同じ第 3 間・同じ小節なのに ♯ が付かず、紙の上では cisis' に見える。2 小節目、8va の下の `ais'` は A4 の間に ♯ つきで描かれ、線の後の `a8` は同じ間なのに ♮ が付かず、ais に見える。
**照合してほしいこと**: LilyPond の既定の決まり（`same-octave`）は音高をオクターブ込みで覚え、`\ottava` は描く位置を動かすだけなので、**LilyPond も同じ絵になる可能性がある**（確かめていない）。LilyPond どおりなら仕様。ただ、紙を読む人（と OMR）は描かれた位置で読むので、もし Lily# 独自に「描いた位置で覚える」選択肢を持つ価値があるかは、そちらの判断。
（生成譜 basic008 の 11 小節目・basic002 の 14 小節目で見つけたものを、2026-10-20 に小さな譜にした。）

`ottava-accidental-reminder-2026-10-20.lys`
```lys
octave absolute
time 4/4
key a major

part p1 { clef treble }

section Piece {
  p1 {
    fis'8@ottava cisis''8 r16 b16@!ottava cis'8 d'2 |
    d'8@ottava ais'8 b'8 r16 b16@!ottava a8 d'4. |
    a'1 |
  }
}

form { ~Piece }

score {
  staff p1 "Staff"
}
```

---

## 参考（不具合ではないもの）

- **グリッサンドの段またぎ**: 段の切れ目をまたぐ線を、始まりの高さと終わりの高さの水平な 2 本で描く（`GlissandoEngraver` に LILYSHARP-OWN と明記されている Lily# 独自の描き方。LilyPond は 1 本の傾きでつなぐ）。意図どおりなら問題ない。OMR は今は段をまたぐグリッサンドを生成していない。
- **グリッサンドの長さ**: 近い臨時記号の手前で 0.1〜1 間隔の切れ端になることがある。LilyPond の既定も Glissando で音符の間を広げない（`springs-and-rods` を上書きしたときだけ）はずなので、LilyPond どおりと見ている。
