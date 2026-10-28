# The side file and the command line contract

What `omr read` and `omr verify` write and print, for a program that calls them (Lily#'s extension does). The calling contract between the two projects is [repro/lilysharp-handoff-2026-10-12.md](repro/lilysharp-handoff-2026-10-12.md) (in Japanese); `omr --version --json` names the `protocol` and the `features`.

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

## Where each bar is

Each part-bar is in `measures` (`measure`, `part`, the .lys `line`, `page`), with two boxes in the
pixels of the page as loaded (`[x0, y0, x1, y1]`; on a page levelled to read, the box around the
corners mapped back): `box`, the bar from two spaces over its staff's top line to two spaces under
its bottom line, and `staff`, the bar on its staff alone, top line to bottom line.
Each todo's `bars` lists the part-bars its `box` is the union of (`{ measure, part }`: a note's
own, a length todo's parts that are off, a part's or every part of the bar for a system's), so
that Lily# boxes the same part-bars in the preview.

## Other readings

A length todo may carry `readings` (Lily#'s AI fix tells its model of them; absent where there
are none): the other readings of one event the reader weighed and did not take (the candidates of
"A bar put right by the reader", with `--no-fix` too), each `{ why, was, now, evidence, fills, x, y }`:
the event as read and as it might be (`-`: none), the page's evidence, whether every voice fills
the bar with it, and where the event is on the page as loaded. The ones that fill the bar first,
then the surer; eight at most. A bar the redraw check flagged (`source: "verify"`) carries none.

## Page paths

`pages[].image` (and `source`, the PDF or TIFF a page came from) is relative to the side file's
folder, with `/`, when the file is in that folder or under it, so that the folder moves whole;
elsewhere it is absolute. `omr verify` reads both.

## Progress

`omr read … --progress json` (the feature `stages`) tells each stage as it begins, between `start`
and `done`: `{"event":"stage","stage":"layout","page":2,"of":6}` at each page's guess of the staves,
then `"read"` before the first page is read (each page read is a `page` event), `"verify"` before
the redraw check and `"write"` before the files go out. A stage not known can be passed over.

## Checking a candidate text

`omr verify x.lys --candidate tmp.lys --bars 12,13 --no-write --progress json` (the feature
`verify-bars` of `--version --json`) holds another text against the pages, answers a `bar` event for
each part-bar asked after (`measure`, `part` (null: the bar's stems and tuplet numbers), `differs`,
`confidence`, `memo`) before `done` (`bars`, `differing`), and writes nothing; `--candidate` implies
`--no-write`. A `differs` at a confidence of 0.8 and over is a difference that changes no note.