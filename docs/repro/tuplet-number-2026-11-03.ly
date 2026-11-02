% REQUESTS 19: written by lysc ly from tuplet-number-2026-11-03.lys, then \omit TupletNumber \omit TupletBracket
% put by hand before bar 3 (LilyPond 2.26.0's way of drawing bars 3-4 with no number and no bracket).
\version "2.26.0"

\header {
  title = "Tuplets without numbers"
}

rh = \fixed c {
  \time 4/4
  \key c \major
  \mark \markup \box "A" \tuplet 3/2 { g'8 c'' e'' } \tuplet 3/2 { g'8 c'' e'' } \tuplet 3/2 { g'8 c'' e'' } \tuplet 3/2 { g'8 c'' e'' } |
  \tuplet 3/2 { c''4 d'' e'' } \tuplet 3/2 { f''4 e'' d'' } |
  \omit TupletNumber \omit TupletBracket
  \tuplet 3/2 { a'8 c'' e'' } \tuplet 3/2 { a'8 c'' e'' } \tuplet 3/2 { a'8 c'' e'' } \tuplet 3/2 { a'8 c'' e'' } |
  \tuplet 3/2 { c''4 d'' e'' } \tuplet 3/2 { f''4 e'' d'' } |
}

\score {
    \new Staff { \clef "treble" \rh }
  \layout { indent = 15\mm \context { \Score printInitialRepeatBar = ##t } }
}
