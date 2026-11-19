\version "2.26.0"
% Two-note tremolo: the default (beams kept off the stems) and the old engraving's (beams joined to both stems)
\relative {
  \clef bass \key b \major \time 6/8
  \repeat tremolo 12 { <fis b>64 dis' } \repeat tremolo 12 { <fis, b>64 dis' } |
  \override Beam.gap = #0
  \repeat tremolo 12 { <fis, b>64 dis' } \repeat tremolo 12 { <fis, b>64 dis' } |
  \revert Beam.gap
  \override Beam.gap-count = #0
  \repeat tremolo 12 { <fis, b>64 dis' } \repeat tremolo 12 { <fis, b>64 dis' } |
}