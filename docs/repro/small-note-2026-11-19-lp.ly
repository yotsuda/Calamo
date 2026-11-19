\version "2.26.0"
% Kuecken Op.47-3 bar 70 (Fr. Kistner 1711): a two-note tremolo whose first chord has one more head, small and black,
% on the same stem -- the pitch the tremolo's other end holds (gis' in the right hand, e in the left).
% Drawn here by tweaking that chord member: font-size -4 and a black head (duration-log 2). The picture matches the
% print; the sound does not: this gis' sounds on every stroke of the tremolo, where the print means it once, at the ffp.
\score {
  \new PianoStaff <<
    \new Staff {
      \clef treble \key b \major \time 6/8
      \repeat tremolo 24 { <gis cis' \tweak font-size #-4 \tweak duration-log #2 gis'>64 gis'64 } |
    }
    \new Staff {
      \clef bass \key b \major \time 6/8
      \repeat tremolo 24 { <e, \tweak font-size #-4 \tweak duration-log #2 e>64 e64 } |
    }
  >>
  \layout { }
}