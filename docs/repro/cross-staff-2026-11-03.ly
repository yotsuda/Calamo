% lysc ly output, with the \change Staff lines added by hand (lysc drops @cross: "articulation @cross not mapped").
\version "2.26.0"

\header {
  title = "cross-staff"
}

rh = \fixed c {
  \time 4/4
  \key c \major
  r2 e''2 |
  r2 g''2 |
}

lh = \fixed c {
  \time 4/4
  \key c \major
  \tuplet 3/2 { c8 e8 \change Staff = "up" g8 \change Staff = "down" } \tuplet 3/2 { c8 e8 \change Staff = "up" g8 \change Staff = "down" } \tuplet 3/2 { c8 \change Staff = "up" g8 c'8 \change Staff = "down" } \tuplet 3/2 { c8 \change Staff = "up" g8 c'8 \change Staff = "down" } |
  \tuplet 3/2 { g,8 d8 g8 } \tuplet 3/2 { g,8 d8 g8 } c8 \change Staff = "up" g8 c'4 \change Staff = "down" |
}

\score {
    \new GrandStaff <<
      \new Staff = "up" \with { instrumentName = "Right" } { \clef "treble" \rh }
      \new Staff = "down" \with { instrumentName = "Left" } { \clef "bass" \lh }
    >>
  \layout { indent = 15\mm \context { \Score printInitialRepeatBar = ##t } }
}
