\version "2.26.0"

rh = \fixed c {
  \time 3/2
  \key aes \major
  \mark \markup \box "Piece" << { f'8 aes'8 des''8. f''16 f''8 f'8 ees''8 f'8 des''8 f'8 c''8 f'8 } \\ { s2 s4 ees''4 des''4 c''4 } \\ { \shiftOff s2 \once \override Stem.direction = #DOWN aes'8 \once \override Stem.direction = #DOWN aes8 \once \override Stem.direction = #DOWN g'8 \once \override Stem.direction = #DOWN aes8 \once \override Stem.direction = #DOWN f'8 \once \override Stem.direction = #DOWN aes8 \once \override Stem.direction = #DOWN ees'8 \once \override Stem.direction = #DOWN aes8 } >> |
}

\score {
    \new Staff { \clef "treble" \rh }
  \layout { indent = 15\mm \context { \Score printInitialRepeatBar = ##t } }
}
