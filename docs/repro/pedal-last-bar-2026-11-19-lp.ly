\version "2.26.0"
\score {
  \new Staff \with { pedalSustainStyle = #'bracket } \fixed c { \clef bass \time 6/8 fis4.\sustainOn b4. <>\sustainOff \bar "|." }
  \layout { }
}