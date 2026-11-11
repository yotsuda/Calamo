\version "2.26.0"

\header {
  title = "Z Rest"
}

lh = \fixed c {
  \time 2/2
  \key c \major
  << { r2 <g, c e g>4-\staccato <g, c e>4-\staccato } \\ { <c, g,>2 ~ <c, g,>4 r4 } >> |
  << { r2 <g, c e g>4-\staccato <g, c e>4-\staccato } \\ { <c, c>2 ~ <c, c>4 r4 } >> |
  c4 r4 e4 r4 |
}

\score {
    \new Staff \with { instrumentName = "Piano" } { \clef "bass" \lh }
  \layout { indent = 15\mm \context { \Score printInitialRepeatBar = ##t } }
}
