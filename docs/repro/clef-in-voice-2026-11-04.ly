\version "2.26.0"
\score { \new Staff { \clef treble \time 4/4
  << { \tuplet 3/2 { f'8 b8 d'8 } s2 \tuplet 3/2 { c8 f8 a8 } } \\ { s4 \tuplet 3/2 { \clef bass a8 b8 f8 } \tuplet 3/2 { a8 d8 f8 } c4 } >> |
  << { \tuplet 3/2 { c8 f8 a8 } s2 \tuplet 3/2 { c8 f8 a8 } } \\ { s4 \tuplet 3/2 { \clef treble g8 c'8 e'8 } \tuplet 3/2 { g8 c'8 e'8 } c4 } >> |
  << { \tuplet 3/2 { g8 c'8 e'8 } \clef bass s2 \tuplet 3/2 { c8 f8 a8 } } \\ { s4 \tuplet 3/2 { a8 b8 f8 } \tuplet 3/2 { a8 d8 f8 } c4 } >> |
  << { \tuplet 3/2 { c8 f8 a8 } \tuplet 3/2 { \clef treble g8 c'8 e'8 } s2 } \\ { s2 \tuplet 3/2 { g8 c'8 e'8 } \tuplet 3/2 { g8 c'8 e'8 } } >> |
} }