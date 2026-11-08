\version "2.26.0"

voice = \fixed c {
  \time 4/4
  \key c \major
  \mark \markup \box "Piece" c''1 |
  c''1 |
}

rh = \fixed c {
  \time 4/4
  \key c \major
  \mark \markup \box "Piece" c''2 c''2 |
  c''2 c''2 |
}

lh = \fixed c {
  \time 4/4
  \key c \major
  \mark \markup \box "Piece" \change Staff = "voice_" g'4 \change Staff = "lh" \change Staff = "voice_" e'4 \change Staff = "lh" c4 e4 |
  c1 |
}

\score {
  <<
    \new Staff = "voice_" { \clef "treble" \voice }
    \new GrandStaff <<
      \new Staff = "rh" { \clef "treble" \rh }
      \new Staff = "lh" { \clef "bass" \lh }
    >>
  >>
  \layout { indent = 15\mm \context { \Score printInitialRepeatBar = ##t } }
}
