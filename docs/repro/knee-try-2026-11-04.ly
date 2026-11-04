\version "2.26.0"
\header { tagline = ##f }
up = \change Staff = "rh"
dn = \change Staff = "lh"
\score {
  \new PianoStaff <<
    \new Staff = "rh" { \clef treble \key e \major \time 2/2
      \omit Staff.TupletBracket \omit Staff.TupletNumber
      s1 | s1 | s1 | s1 |
    }
    \new Staff = "lh" { \clef bass \key e \major \time 2/2
      \tuplet 3/2 4 { b,8 e \up gis  \dn b, e \up gis  \dn b, e \up gis  \dn b, e \up gis } \dn |
      \tuplet 3/2 4 { b,8 \up gis' b'  \dn b, \up gis' b'  \dn b, \up gis' b'  \dn b, \up gis' b' } \dn |
      \tuplet 3/2 4 { \up gis'8 \dn e b,  \up gis' \dn e b,  \up gis' \dn e b,  \up gis' \dn e b, } |
      \tuplet 3/2 4 { e,8 b, \up e'  \dn e, b, \up e'  \dn e, b, \up e'  \dn e, b, \up e' } \dn |
    }
  >>
  \layout { indent = 0 }
}
