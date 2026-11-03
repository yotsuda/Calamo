\version "2.26.0"

\header {
  title = "rest-cross"
}

rh = \fixed c {
  \time 2/4
  \key c \major
  e''2 |
  e''2 |
}

lh = \fixed c {
  \time 2/4
  \key c \major
  << { \tuplet 3/2 { d8 \change Staff = "rh" g8 \change Staff = "lh" \change Staff = "rh" b8 \change Staff = "lh" } \tuplet 3/2 { d8 \change Staff = "rh" g8 \change Staff = "lh" \change Staff = "rh" b8 \change Staff = "lh" } } \\ { g,8 r8 g,8 r8 } >> |
  << { \tuplet 3/2 { d8 g8 b8 } \tuplet 3/2 { d8 g8 b8 } } \\ { g,8 r8 g,8 r8 } >> |
}

\score {
    \new GrandStaff <<
      \new Staff = "rh" \with { instrumentName = "Right" } { \clef "treble" \rh }
      \new Staff = "lh" \with { instrumentName = "Left" } { \clef "bass" \lh }
    >>
  \layout { indent = 15\mm \context { \Score printInitialRepeatBar = ##t } }
}
