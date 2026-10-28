# How the reader reads: voices, words, staff groups

Notes on parts of the reading that the [README](../README.md) only names. The side file and the command line contract are in [protocol.md](protocol.md).

## Several voices on a staff

`--poly` scores each staff-bar as a multiset of (pitch, duration), since with several voices on
a staff the order of events is not what is being measured.

Voices: a staff-bar whose events overfill the bar is split, stem-up events to voice 1 and
stem-down to voice 2, rests and stemless notes to whichever voice they complete; `.lys` gets
`voice { } { }`, with spacer rests placed in time by the x positions of complete lines on any
staff of the system. `--voices 'dir/voices-{part}-v*.notes'` (event listener, one file per
staff and voice) scores it: BWV 610 has the right number of voices in 39/39 staff-bars and
98.0% of the notes in the right voice and order (the same as the heads found at all).

## Title, composer, poet

`omr read` (and a run of the reader with `--header`; `--no-header` skips it) reads the words over
the first page's music with Windows' own OCR (`Windows.Media.Ocr`: nothing shipped, nothing sent;
the languages are those installed with Windows) and writes `title`, `subtitle`, `composer` and
`poet` into the .lys. Only the page above the first system is read, short of the six spaces just
over its first staff (a tempo, a rehearsal mark). The largest line centred on the music is the
title; the centred line under it (or over it: a cycle's name over a song's) the subtitle; the top
line to the right the composer, to the left the poet. A credit says what it is wherever it stands
("Words & Music by …", "Music by …", "作詞 …", "作曲 …"). A copyright line and a paragraph of notes
(three lines or more of one size from one left edge, as in a book of band scores) are passed over;
so are the corners of a page with no title (a running head). Latin text read by a Japanese engine:
l, I and 1 are put right inside words; with an English OCR language installed, a line of Latin
text is taken from it.

Over every system the same OCR reads the band up to sixteen spaces over its first staff (short of
three spaces under the system before, and of the title and subtitle over the first), and just over
the staff again in windows twenty spaces wide (the OCR passes over a short line among the notes,
"♩ = 116", which it reads cut out). The band's rows of letters, as the ink shows them (pieces half
a space to three and a half tall, four and more in a row three spaces wide), are each read again by
themselves, cut out on white with a rehearsal mark's box beside them painted over, at the page's
scale and up to 2.8 times it; of the readings, the clearest by the tempo vocabulary (words of the
vocabulary, a metronome figure, few stray characters) takes the place of the band's. A bold tempo
in the band of a page drawn at 560 dpi reads as "Me110 mOSSO" or not at all, and clean by itself.
A metronome mark's "=" read as "一" or "-" and a 0 among its figures read as "の" count as the
figures they are:

- a **tempo marking** (a metronome number, or words that begin one: Andante, Più mosso, Langsam,
  Tempo I …) at the bar its left end stands over: the first bar's as the piece's `tempo`, the others
  as `tempo` in the first part at their bar. The metronome mark's note is a glyph the OCR does not
  read: its value is the meter's beat (a dotted quarter in 3/8, 6/8, 9/8, 12/8, else the meter's unit).
- a **rehearsal mark**, words in a box, as a **section** of the .lys from the bar whose barline it
  is centred on: `section Theme { … }`, the music before the first mark `section Intro`, and
  `form { ~Intro Theme Agitato … }` (Lily# prints a section's name in a box unless `~` hides it).
  A name is the words with a letter first (`Mark12` for a number in a box; `A_2` for a name met
  again); where the words are no name of a section (a number, "Var. V") the section takes them as
  its declared label, `section Mark1 "1" { … }`, and Lily# prints the label in the box. A box with
  one piece of ink in it, a letter A–Z or a figure 1–9, is read by a nearest-neighbour classifier
  learnt from Lily#'s own boxes (the OCR reads no lone letter); a box another piece of ink touches
  (a sharp of the key signature under its corner) is found by its rules within the ink. With no
  mark the .lys has one section, `Piece`, as before.

Tempo words and common section names are spelt as the nearest word of a small vocabulary when the
OCR misses a letter or two ("Andant、00" → "Andantino", "Piü" → "Più", "Adagi 0" → "Adagio"). `--words`
prints every line read. Measured on generated pieces with words drawn at random (`tools/run-header.ps1`,
OmrGen `--headers`, 40 pieces): title 34/40, subtitle 14/18, composer 35/36, poet 17/17, tempo 34/36.

With no score file (`omr read`), the parts a brace joins on the page are written in a `grandStaff`
(a piano's two staves), a bracket's in a `staffGroup` (a quartet's four), a brace within a bracket
nested (`Layout.Joiner`; the score file's `grandStaff rh lh` and `staffGroup vn1 vn2 va vc` lines
say the same). The brace is the thin curve in the three and a half spaces left of the system's
opening line, on nearly every row from the upper staff's top to the lower's bottom, from a tenth of
a space wide at its tips and its middle point to four tenths in its lobes, bulging left by half a
space and more; the bracket is a bar at the line a third of a space or thicker whose hooks reach
out past the staves' outer lines.