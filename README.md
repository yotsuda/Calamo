# LilySharp-Omr

Experimental optical music recognition for Lily#: read a page image of a cleanly engraved
score and write `.lys` text. A small learnt symbol detector (ONNX, run locally through ONNX
Runtime) finds the symbols; image processing and the rules of notation check them and put the
music together. Nothing is sent anywhere: no cloud service, no large language model.
Kept apart from the Lily# repository on purpose.

Published as the VS Code extension **Lily# Scan Import**
([`yotsuda.lilysharp-omr`](https://marketplace.visualstudio.com/items?itemName=yotsuda.lilysharp-omr)),
which adds to the Lily# extension.

Direction and state: [docs/ROADMAP.md](docs/ROADMAP.md) (goals, workstreams, how a session runs)
and [docs/HANDOFF.md](docs/HANDOFF.md) (where things stand, the next step). `tools/check.ps1` is
the regression gate against `docs/baseline.json`.

## Scope

- Input: page images (PNG) and a small score file: the title and one `part` line per staff
  of a system (`data/<score>/<score>.score`).
- Reads: staves, barlines, printed bar numbers, clefs (treble, alto, tenor, bass, bass_8),
  key signatures (and key changes), time signatures (and changes), note heads (black, half,
  whole), stems, beams/flags, dots, chords, tuplets (3, 4), accidentals (flat, natural,
  sharp, double flat), rests (whole/half/quarter/eighth/16th).
- Output: `omr.lys`, per-system overlay images, and a comparison with a hand transcription.
- If the score file also states clefs and `bar N time .. key ..`, the run reports where the
  page reading disagrees; `--given` uses the stated values instead of reading them.

## Results

| Score | Engraver | Size | Clefs/keys/times | Notes and rests |
|---|---|---|---|---|
| Bohemian Rhapsody for strings (`data/bohemian`) | MuseScore 4 (Leland) | 19 pp., 5 staves, 137 bars, 3271 events | 1507/1507 | 99.5% exact (pitch 99.9%, duration 99.6%) |
| 9 to 5, bass line (`data/ninetofive`) | LilyPond 2.26 (Emmentaler) | 3 pp., 1 staff, 100 bars, 506 events | 300/300 | 100% exact |
| Bach BWV 610, organ (`data/bwv610`) | LilyPond 2.26 | 2 pp., 3 staves, two voices on a staff, 13 bars, 500 notes | 91/91 | recall 98.0%, precision 99.2% (`--poly`: heads as a multiset per staff and bar) |

About 1-2 s per page. Truth: a hand transcription (Bohemian), LilyPond's event listener
(the others; `event-listener-*.ly` in the data folders, MIDI pitches).

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

## How sure a todo is

Every place the reading doubts is a todo in `x.omr.json` with a `confidence` from 0 to 1: how
likely it is that the place needs **no** correcting (low: look here first). It is set so:

- **A note read with a doubt** (the detector half sees another sign or dot): 1 − the detector's
  score for the other reading; a dot the rules found and the detector does not see, 0.5.
- **A part-bar whose music does not fill its time** (one todo for each part that is off, on its
  first note, how much in `parts`; the first bar of pages read from a later bar, short alike on every
  staff, has one todo for the bar): the top voice full and the others only short, 0.9 down to 0.8 (most often a rest not
  read, the notes right); every voice that is off put right by one dot or one tuplet, 0.55 down to
  0.4; the top voice off, or every voice, 0.3 down to 0.1; nothing read, or off by a bar or more, 0.
  Within each, the more the bar is off, the lower. The first bar short alike on every staff (a
  pickup), 0.8. The piece's own first bar so short is no todo: it is written as a pickup, the first
  section's `partial` (also when the staves are short by different amounts and the next printed bar
  number bears the pickup out; the shorter staves are filled with spacer rests). Nor is the piece's
  last bar after such a pickup, when it is short of its time by exactly the pickup (the longest staff
  so long, none longer): it completes the pickup. A last bar short by anything else keeps its todo.
  A bar that holds a whole number of bars of its time (2 or more), every voice alike, and is about
  as many times as wide as its neighbours in the system, says a bar line may be missed there (0).
- **The redraw check** (`--verify`: the .lys drawn again does not match the page there): strong
  evidence, so 0.3 at most, times the share of the part-bar's symbols that do match (one wrong
  among twenty 0.28, half 0.15, one with nothing matching 0.08, ten with nothing 0.01); a TAB fret
  against its note 0.3; a bar's length alone 0.3 down to 0.1 as it is off by more. A difference
  that changes no note is less, within its band by the same share: a tie 0.6-0.7, a dot only the
  scan shows (a staccato as often as not) 0.8-0.85, a slur 0.85-0.9, a whole-bar rest the text has
  for a bar the scan shows nothing in 0.85-0.9. Where lysc finds an error in the .lys it still
  draws all of the music (a bar's or a span's error), the check compares all the same, and the
  part-bar of the error is a todo: two bars' worth with no bar line between (LYS2017, a bar line
  missed) 0.15; another (a slur across a repeat, a pedal left open) 0.5. After a parser's error it
  compares nothing. A flag on the system's stems and tuplet numbers alone (no part's symbols differ) is
  0.8 at the least, kept in `x.omr.json` only: it seldom points at a bar read wrong; in a bar a part's
  todo is written in, its words go with the part's.

- **A bar put right by the reader** (`"source": "fixed"`, confidence 1, kept in `x.omr.json` only):
  a staff's bar whose voices did not fill the time, where exactly one other reading of one event
  (a value a step longer or shorter, a dot more or fewer, a rest the detector half saw, a note or
  rest it sees nothing of taken out) both fills every voice exactly and has the page's support
  (the detector's score for what it adds against what it takes away: `evidence` in the memo). The
  memo says what was read and what it was read as. Nothing is put right on a page most of whose
  bars are off (the meter is in doubt there) or in a bar every voice of which is off alike (a
  pickup); `--no-fix` leaves every bar as read, `--fixes` prints every reading weighed.

A todo at or above `--todo-threshold` (0.8) is kept in `x.omr.json` only (`"inSource": false`),
not written into the .lys; so is a bar's length todo where the redraw check found the bar's
drawing as the page's. `--todo-threshold 1` writes every one. `done` counts the marks written
(`todos`) and the others (`quiet`); the annotated pages (`x.omr-pNN.png`) box the marks written,
thicker the less sure, 1 to 3 px (`--annotate-all`: every todo). The values are graded, not
stepped, so that a threshold shows or hides the todos one by one rather than a whole kind at once.

Each part-bar is in `measures` (`measure`, `part`, the .lys `line`, `page`), with two boxes in the
pixels of the page as loaded (`[x0, y0, x1, y1]`; on a page levelled to read, the box around the
corners mapped back): `box`, the bar from two spaces over its staff's top line to two spaces under
its bottom line, and `staff`, the bar on its staff alone, top line to bottom line.
Each todo's `bars` lists the part-bars its `box` is the union of (`{ measure, part }`: a note's
own, a length todo's parts that are off, a part's or every part of the bar for a system's), so
that Lily# boxes the same part-bars in the preview.

A length todo may carry `readings` (Lily#'s AI fix tells its model of them; absent where there
are none): the other readings of one event the reader weighed and did not take (the candidates of
"A bar put right by the reader", with `--no-fix` too), each `{ why, was, now, evidence, fills, x, y }`:
the event as read and as it might be (`-`: none), the page's evidence, whether every voice fills
the bar with it, and where the event is on the page as loaded. The ones that fill the bar first,
then the surer; eight at most. A bar the redraw check flagged (`source: "verify"`) carries none.

`pages[].image` (and `source`, the PDF or TIFF a page came from) is relative to the side file's
folder, with `/`, when the file is in that folder or under it, so that the folder moves whole;
elsewhere it is absolute. `omr verify` reads both.

`omr read … --progress json` (the feature `stages`) tells each stage as it begins, between `start`
and `done`: `{"event":"stage","stage":"layout","page":2,"of":6}` at each page's guess of the staves,
then `"read"` before the first page is read (each page read is a `page` event), `"verify"` before
the redraw check and `"write"` before the files go out. A stage not known can be passed over.

`omr verify x.lys --candidate tmp.lys --bars 12,13 --no-write --progress json` (the feature
`verify-bars` of `--version --json`) holds another text against the pages, answers a `bar` event for
each part-bar asked after (`measure`, `part` (null: the bar's stems and tuplet numbers), `differs`,
`confidence`, `memo`) before `done` (`bars`, `differing`), and writes nothing; `--candidate` implies
`--no-write`. A `differs` at a confidence of 0.8 and over is a difference that changes no note.

## Run

```
dotnet build src/Omr -c Release
omr --config data/bohemian/bohemian.score --pages <dir with p01.png ...> --from 14 --to 19 \
         --out out/run --truth data/bohemian/truth.md --overlay
```

`--debug` prints the staves; `--at x,y` traces the hole tests near a page point.

## License

The code is under the [Apache License 2.0](LICENSE) (see also [NOTICE](NOTICE)). The data under
`data/` keeps the licenses of its sources: public domain, CC0, CC BY 4.0, and LilyPond's GPL for
the `event-listener*.ly` files; [data/LICENSES.md](data/LICENSES.md) lists each. Contributions:
[CONTRIBUTING.md](CONTRIBUTING.md).
