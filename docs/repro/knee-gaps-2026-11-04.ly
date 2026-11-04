\version "2.26.0"
\header { tagline = ##f }
up = \change Staff = "rh"
dn = \change Staff = "lh"
\score {
  \new PianoStaff <<
    \new Staff = "rh" { \clef treble \key e \major \time 4/4
      R1 | R1 | R1 | R1 |
    }
    \new Staff = "lh" { \clef bass \key e \major \time 4/4
      \omit TupletBracket \omit TupletNumber
      \tuplet 3/2 { e8 b \up gis' } \tuplet 3/2 { \dn e8 b \up gis' } \tuplet 3/2 { \dn e8 b \up gis' } \tuplet 3/2 { \dn e8 b \up gis' } \dn |
      \tuplet 3/2 { e,8 b, \up gis' } \tuplet 3/2 { \dn e,8 b, \up gis' } \tuplet 3/2 { \dn e,8 b, \up gis' } \tuplet 3/2 { \dn e,8 b, \up gis' } \dn |
      \tuplet 3/2 { e8 \up gis' b' } \tuplet 3/2 { \dn e8 \up gis' b' } \tuplet 3/2 { \dn e8 \up gis' b' } \tuplet 3/2 { \dn e8 \up gis' b' } \dn |
      \tuplet 3/2 { e8 b \up gis'' } \tuplet 3/2 { \dn e8 b \up gis'' } \tuplet 3/2 { \dn e8 b \up gis'' } \tuplet 3/2 { \dn e8 b \up gis'' } \dn |
    }
  >>
  \layout { indent = 0 }
}
