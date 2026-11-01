\version "2.26.0"
% REQUESTS 16, LilyPond 2.26.0: bar 1 a tie on one member of a chord (chord_body post-event): only the d''' is tied.
% Bar 2: the chord's tie to a single note: only the d''' is tied. Bar 3: ties on two members.
\score {
  \new Staff \absolute {
    \clef treble \time 3/4 \key c \major
    <c'' f'' d'''~>8. <c'' f'' d'''>16 r2 |
    <c'' f'' d'''>8.~ d'''16 r2 |
    <c'' f''~ d'''~>8. <c'' f'' d'''>16 r2 |
  }
}
