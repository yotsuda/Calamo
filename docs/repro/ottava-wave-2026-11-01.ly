\version "2.26.0"

\header {
  title = "Ottava Wave (style trill)"
}

rh = \fixed c {
  \time 9/8
  \key f \major
  a'8 c''8 f''8 a''8 g''8 f''8 e''8 d''8 c''8 |
  \once \override Staff.OttavaBracket.style = #'trill \once \override Staff.OttavaBracket.edge-height = #'(0 . 0) \ottava #1 f'''4. f'''8 e'''8 d'''8 cis'''8 d'''8 bes''8 \ottava #0 |
  a''8^\markup \italic "loco." a''8 a''8 g''8 g''8 g''8 f''4. |
}

\score {
    \new Staff \with { instrumentName = "Piano" } { \clef "treble" \set Staff.ottavationMarkups = #ottavation-simple-ordinals \rh }
  \layout { indent = 15\mm \context { \Score printInitialRepeatBar = ##t } }
}
