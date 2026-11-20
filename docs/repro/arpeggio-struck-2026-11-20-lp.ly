\version "2.26.0"
% Kuecken Op.47-3 bar 70 (Fr. Kistner 1711), right hand: the wavy line stands beside the two white heads (gis, cis')
% only, not beside the small black gis' on the same stem (Lily#'s @struck, request 31).
% Bar 1: a plain \arpeggio on that chord -- LilyPond draws it beside every head, the small one too.
% Bar 2: the same, its extent set by hand (Arpeggio.positions, in staff spaces from the middle line) to the two white
% heads -- the picture of the print.
\score {
  \new PianoStaff <<
    \new Staff {
      \clef treble \key b \major \time 6/8
      \repeat tremolo 24 { <gis cis' \tweak font-size #-4 \tweak duration-log #2 \tweak Dots.stencil ##f gis'>64\arpeggio gis'64 } |
      \once \override Arpeggio.positions = #'(-5.5 . -3)
      \repeat tremolo 24 { <gis cis' \tweak font-size #-4 \tweak duration-log #2 \tweak Dots.stencil ##f gis'>64\arpeggio gis'64 } |
    }
    \new Staff {
      \clef bass \key b \major \time 6/8
      \repeat tremolo 24 { <e, \tweak font-size #-4 \tweak duration-log #2 \tweak Dots.stencil ##f e>64 e64 } |
      \repeat tremolo 24 { <e, \tweak font-size #-4 \tweak duration-log #2 \tweak Dots.stencil ##f e>64 e64 } |
    }
  >>
  \layout { }
}
