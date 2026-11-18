\version "2.26.0"
% beat repeat: \repeat percent with a body shorter than the bar (LilyPond prints a RepeatSlash per repetition)
\relative {
  \clef bass \key b \major \time 6/8
  fis16 <fis b>64 dis' <fis, b> dis' \repeat percent 4 { <fis, b>64 dis' <fis, b> dis' } \repeat tremolo 12 { <fis, b>64 dis' } |
}