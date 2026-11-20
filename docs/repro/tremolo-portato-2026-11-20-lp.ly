\version "2.26.0"
% Kuecken Op.47-3 (Fr. Kistner 1711) bar 73, right hand: a chord repeated in eighths (one stroke on the stem), and
% above it an arc with six dots under it, one for each stroke -- the strokes played portato.
% LilyPond has no such sign. Bar 1: the stroke alone (\repeat-less ":8"), as Lily# writes <...>2.:8.
% Bar 2: the picture of the print, drawn by hand as a markup (an arc and six dots) -- the sign only, no sound.
% Bar 3: the meaning written out: six eighths under a slur, a dot on each.
dotsArc = \markup \center-align \overlay {
  \translate #'(-0.3 . 0.8) \path #0.13 #'((moveto 0 0) (curveto 1.5 1.1 5.3 1.1 6.8 0))
  \line { \musicglyph "dots.dot" \hspace #0.25 \musicglyph "dots.dot" \hspace #0.25 \musicglyph "dots.dot"
          \hspace #0.25 \musicglyph "dots.dot" \hspace #0.25 \musicglyph "dots.dot" \hspace #0.25 \musicglyph "dots.dot" }
}
\score {
  \new PianoStaff <<
    \new Staff {
      \clef treble \key b \major \time 6/8
      <fis b dis' fis'>2.:8 |
      <fis b dis' fis'>2.:8^\dotsArc |
      <fis b dis' fis'>8-.( q-. q-. q-. q-. q-.) |
    }
    \new Staff {
      \clef bass \key b \major \time 6/8
      <b, dis fis b>2.:8 | <b, dis fis b>2.:8 | <b, dis fis b>2.:8 |
    }
  >>
  \layout { }
}
