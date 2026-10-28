# Changelog

Notable changes to Lily# Scan Import are recorded here, newest first. Release notes are
taken from this file: the topmost section is the version being tagged, and the release
workflow (`.github/workflows/release.yml`) attaches that section to the GitHub Release.

## Unreleased

- **Open source.** The reader and this extension are now under the Apache License 2.0, and the
  source is public: <https://github.com/yotsuda/LilySharp-Omr>. Reports of misread pages are
  welcome there as issues. The extension's own terms of use are gone.
- The `.lys` the reader writes begins `// Read by Lily# Scan Import <version> …`, and
  `x.omr.json` names the reader the same way.
- Needs Lily# 0.14.0 or later (it carries `lysc`, which checks a reading against the scan).

## 0.4.0

- Old prints and scans read much better: the nineteenth-century songs on IMSLP, scanned at about
  96 dpi, are enlarged before they are read; a page bowed at the binding is flattened; a scan's dark
  border is left out; key signatures packed in two columns, clefs whose dots are lost or touch the
  key's first sharp, stems broken short of their beams, beams run together by the toner, dots hung
  from a staff line and thin-rimmed half notes are read; a filled eighth-note flag is no longer
  taken for two. The systems of a page agree on each part's clef and key, so one misread clef no
  longer moves a whole system's notes. On the real prints we measure on but never tune on (fifteen
  songs from OpenScore Lieder, both their MuseScore drawing and the IMSLP scan they were typed from,
  Schumann's Dichterliebe, and short piano pieces in several engravings), 92.5 in 100 notes are
  read right (85 when the fifteen songs were added).
- Clef changes inside a bar are read, the small ones MuseScore prints before a barline too, and
  written with `clef` in the bar where they stand.
- A bar the reader is unsure of is put right where it can be: when a bar's notes do not fill its
  time and exactly one other reading of one note or rest (its value, its dot, a rest seen faintly)
  fills every voice and the detector supports it, that reading is taken. The side file keeps a
  quiet mark with `source: "fixed"`. On our test pieces three in four of the bars it changes come
  out right, and 2 in 187 further off than before.
- A pickup is written as `partial 4` (or the like) at the start of the first section, the staves
  that rest filled with spacers; a last bar that completes the pickup is no longer marked as short.
- Triplets printed without their 3 are read where the bar's time asks for them, including those
  that begin with a rest. A three-note beam that is no triplet is left as it is.
- Portato (`@portato`), the stopped sign `+` (`@stopped`) and the pralltriller (`@pralltriller`)
  are read.
- Beat repeats (two to four short slashes after a beamed group, `///`) are written out as the
  group's notes again, as Lily# has no beat repeat of its own.
- Chords with a second, the lower note on the far side of the stem, stay one chord. A rest standing
  under or over a note is another voice's. A bar split by a system break with no barline at its end
  is joined with its other half.
- A verse's extender lines, five of them under each other, are no longer taken for a staff; a page
  with no staff on it no longer stops the reading; a fingering "2" is no longer read as a duplet.
- Two-note tremolos are read: two half notes (or two quarters with no beam on their stems) with
  beams floating between them, or joining their stems, alternate for the time of one of them, and
  come out as `repeat tremolo 6 { c''16 fis'16 }`. Before, each was read at its full written value
  and the bar came out twice too long. A song by Kinkel engraved in MuseScore went from 95.8 to 99.0
  notes in 100 read right.
- What Lily#'s "Fix OMR Marks with AI…" asks of the reader (first in 0.3.1, which was installed
  locally only, never on the Marketplace). `omr verify --candidate <file> --bars 12,13 --no-write`
  checks a proposed text against the pages bar by bar without writing anything (`--version --json`
  lists `features: ["verify-bars"]`);
  a bar whose notes do not fill the time lists the other readings the reader weighed (`readings`);
  the page images are named relative to the `.omr.json` when they are beside it, so a folder can be
  moved whole.
- Octave lines: a "15ma" printed over a system's last note, with no dashes, is read when the next
  system's "(15ma)" carries it on, even when the letters there are read unsurely.
- Octave lines end where they are drawn more often: a sharp across the dashes no longer ends the
  line, an end hook drawn apart from the last dash is found, a "(8vb)" over a single rest at a
  system's start no longer runs on into the next line, and a chord just after a line keeps its
  octave. A triplet's 3 printed on an octave line's dashes no longer takes the dashes for its
  bracket. On generated piano music never used for tuning, 99.4 in 100 notes are read right (98.9
  before).
- Grace notes: a grace note's small flat is given to the grace note, not to the main note a step
  below it, and two beamed grace notes are no longer parted by the other staff's grace group under
  them. On generated music with grace notes, 99 in 100 are read with their kind, pitches and values
  all right (98.8 degraded as a scan).
- Octave lines (8va, 8vb, 15ma, 15mb) are read more reliably: a sign whose letters are unclear takes
  its side from the hook at its end, a "15mb" seen half as a 15ma is still read, a line's dashes
  crossed by the other hand's beam are followed past it, a chord with a second under the line's start
  is moved with it, and a sign over a system's last note is joined to its continuation. On generated
  music with octave lines every note is read at the right octave (99.8 in 100 degraded as a scan).
- Trill lines and spans of words ("rit.", "accel." …) on a grand staff's lower staff are read as
  the lower staff's: a left-hand line between the two staves is no longer given to the right hand,
  a wave crossed by the barline between the staves or by the other hand's beam is still read as a
  trill line, and a span's continuation in the next system is no longer cut short by the clef. On
  generated piano music with lines in either hand, 99 in 100 trill lines are read with both ends in
  the right bar and none is wrong (97 in 100 degraded as a scan); 95 in 100 spans. A natural and a
  flat, or two sixteenth rests, lined up before a note are no longer read as a glissando.
- Glissandi are read: the straight line from a note's head to the next note's comes out on the
  first note as `@glissando`, a chord's lines on the chord. On generated music 92 in 100 are read
  and none is wrong (98 in 100 right degraded as a scan); the rest are lines drawn so short, before
  an accidental close by, that they show as little more than a dot.
- Arpeggios are read: the wavy line up a chord's left comes out on the chord as `@arpeggio`. On
  generated music 99 or more in 100 are read and none is wrong, clean or degraded as a scan. The
  wave is no longer read as a quarter rest, nor its pieces as flats and naturals on the chord.
- Trill lines are read: a "tr" with a wavy line after it comes out as
  `@startTrillSpan … @stopTrillSpan`, the wave followed to the note it ends before, into the next
  system when the line runs on. A zigzag of the wave is no longer read as a prall. A "tr" the
  detector misses is found from the wave after it. On generated music 97 in 100 are read with both
  ends in the right bar and none is wrong (96 in 100 degraded as a scan); the rest are mostly
  lines Lily# itself draws without their "tr", or a "tr" the detector does not see.
- "rit.", "accel.", "rall." and other words of expression with a dashed line after them are read
  as spans: the dashes are followed to the note the line ends on, into the next system when the
  line runs on, and written as `@rit … @!rit` (`@accel`, `@rall`; other words as
  `@textSpan("poco rit.") … @!textSpan`). On generated music 96 in 100 are read with both ends
  in the right bar, and none is wrong (94 in 100 degraded as a scan). A span so short that no
  dash is drawn is still written as plain text. The "mo" of "molto" and the "po" of "poco" are
  no longer taken for an octave sign (which raised the notes under the dashes an octave).
- Words of expression are read: an italic "dolce", "rit.", "a tempo", "poco rit.", "sempre p",
  "pizz." over or under a staff comes out on its note as `@text("dolce")` (`.up` over the
  staff). Windows' OCR reads each italic row of letters by itself, sheared upright; a reading
  counts when it is one of the common words of expression, so a song's words and printed bar
  numbers (upright) are passed over and a word not in the list is not written. On generated
  songs 98 to 100 in 100 are read and none is wrong (91 in 100 degraded as a scan). A dynamic
  letter the detector saw inside the words (the "mp" of "sempre") is no longer written as a
  dynamic of the note. A bare "a tempo" is no longer read as a tempo marking "Tempo".
- An ending's numbers are read: the figures by a volta bracket's left hook ("1.", "1. 2.",
  "1.–3.") come out in the `form` line as `[1.`, `[1-2.`, `[1-3.`, `[3.`, where before the
  endings were numbered in order. Windows' OCR passes over one short word by itself, so the
  figures are set three times in a row and read as one line. The Lab's lead sheet comes out as
  written, `|: Verse [1-2. ~A] :| [3. ~B]`. A page's row of chord diagrams is no longer taken for
  a staff when the detector sees their strings as barlines (no "line" of it runs eight spaces).
- The form is read: repeat barlines (`|:` and `:|`), first and second endings, the segno and
  coda signs, "To Coda", and the words of a jump or an end ("Fine", "D.C. al Fine", "D.S. al
  Fine", "D.C. al Coda", "D.S. al Coda", "D.S.", "D.C.") come out as a `form` line, the music
  cut into sections at them. The detector learnt the repeat dots, the endings' hooks and the
  two signs (detector31); the words are read by Windows' OCR under or over the staff, their
  right end at the barline of the bar they follow; a second ending whose start the detector
  missed is opened after the first's `:|` all the same. On generated pages the repeats, the
  endings and the jumps all read exactly (100 in 100), and 100, 100 and 96 in 100 on the same
  pages degraded as a scan (the segno the toner thins). The Lab's roadsong (segno, To Coda,
  D.S. al Coda) and sarabande (Fine, D.C. al Fine) come out as written. Not yet: "D.S." and
  "D.C." standing alone, "Fine" among other words.
- Grace notes read better without retraining: a lone grace note's flags are counted in the ink
  where the detector is unsure (the curls joining the stem: one for an eighth, two for a
  sixteenth, a neighbouring beam over the stem passed over), a slashed lone grace note is an
  eighth as acciaccaturas are written, a slash the detector misses is found as the slanting
  stroke left of the stem (a down-stem's slash, drawn mirrored), a grace note's small accidental
  is its own even where the rules took it for a rest or gave it to no note, and the other voice's
  rest between the grace notes and their note no longer orphans them. On generated pages 99 in
  100 groups read exactly (94 before), 99 degraded as a scan (92 before).
- Grace notes are read: an acciaccatura (the slashed small note), an appoggiatura (an unslashed
  eighth or sixteenth) and a group of two or three small sixteenths come out as `acciaccatura { }`,
  `appoggiatura { }` and `grace { }` before their note, with their own accidentals and values. The
  detector learnt the small heads and the slash (detector30); on generated pages with grace notes,
  94 in 100 groups read exactly (92 on the same pages degraded as a scan), 98 by kind and count. A
  grace note's slur is no longer read as a rest or a slur, and its head no longer as a note.
- The guess of a score's staves on a degraded scan uses the learnt detector too, as the reading
  does: a staff whose lines the toner broke is recovered about the barlines the detector sees, the
  opening line of a system that the toner broke still joins its staves (a column dark on a quarter
  of its rows with no break of two spaces, where the page's whole lines stand), a percussion line
  broken to pieces is found from the row of noteheads sitting at one height, and a brace is told
  from a speckled page's noise by how its left edge wanders. On MuseScore's band charts in eight
  fonts, degraded as a scan, the staves come out right on 227 of 270 pages (213 before); clean pages
  as before (269 of 269). The pages still wrong have lost their staff lines to the toner altogether.
- The guess of a score's staves (which are TABs, which one-line percussion staves) is right on
  every clean page of MuseScore's band charts in eight fonts (269 of 269; 231 before) and on
  more of the scanned ones: a TAB whose lines the digits break up, found by none of the earlier
  tests, is now found from its rows (four to six of them a TAB's spacing apart, an outer line
  lost under the beams along it recovered); the opening line of a system is found when a TAB
  starts left of its staff (at its "TAB" letters), where the staves above and below it were
  split into two systems; a row of beams stacked over a staff, or of hi-hat x heads over a drum
  staff, is no longer taken for a one-line staff (its ink reaches the staff, and a line is
  thinner than a beam); a percussion line with noteheads hanging under it is no longer dropped
  as if it were a staff's line.
- The brace and bracket at a system's start are read on MuseScore's pages too: the opening line
  is told from a brace's middle, which runs down the short gap between a guitar's staff and its
  TAB (the brace was taken for the line there, and never seen), a hand-drawn brace (MuseJazz) a
  quarter to nine tenths of a space wide counts, and so does a scanned brace, whose thinnest part
  blurs to a third of a space. A bracket is a bar standing left of the line, thicker than it;
  a scan's thickened line is no bracket any more. On MuseScore's band charts in eight fonts the
  braces come out right on every clean page (231 of 269 pages match the score's layout, the rest
  by the staves' kinds) and on all but one scanned page.
- An octave line that runs to the end of a system is taken up by the "(8va)" or "(15ma)" at the
  start of the next one even when the detector is unsure of that sign and no dashes follow it
  (a continuation over a single note); its line then ends at the sign, not along the next note's
  beam. The `@!ottava` of a line that ends after a rest is written after the rest, where the hook
  stands. A note between two staves is given the staff its ledger lines run to, not the nearest:
  a left hand climbing two ledger lines over its staff, close under the right hand's, read on the
  right hand's staff before.
- The Marketplace description and keywords say "optical music recognition (OMR)", so that a
  search for the technique's name finds the extension; the name stays Lily# Scan Import.
- Rehearsal marks numbered in boxes (1, 2, 3 …) are read like the lettered ones, by the
  classifier that learnt Lily#'s boxes (the OCR reads no lone figure), and written as sections
  with a declared label, `section Mark1 "1" { … }`, so that the redrawn score prints the number;
  a mark whose words are no section name ("Var. V") takes them as its label too. A box another
  piece of ink touches (a sharp of the key signature under its corner at a system's start) is
  found by its rules within the ink; before, it was passed over.
- Without the detector, a quintuplet, sextuplet or septuplet of equal notes is read by the count
  of its stems (five, six or seven under one number, no rest among them); before, the rules read
  the number as a 3.
- Quintuplets, sextuplets and septuplets (5:4, 6:4, 7:4) are read by their number; before,
  every tuplet other than a duplet or quadruplet was read as a triplet.
- Lead sheets: the row of chord diagrams over each system is no longer taken for a staff (it
  came out as an empty bar in another clef), the one-line rhythm staff under the melody is
  found and set aside, and the title, tempo and rehearsal marks over the diagrams are read.
  A time signature after a repeat barline is read.
- The one-line rhythm staff of a lead sheet is read as a part of its own: its slashes with their
  durations, dots and articulations (`/4 /4 /8 /8 /4`, `/2.@fermata`), written as a part with
  `staff gtr as lines 1`; its key and time follow the melody's.
- Octave lines are read from the page's symbols, not the OCR: 8va and 15ma over a staff and
  8vb and 15mb under one, to the hook at their end or on into the next system (`@ottava`,
  `@ottava(bassa)`, `@quindicesima`). Before, only an 8va over the staff was read, and only
  when the OCR made out its letters.
- Octave lines, second step: 15ma and 15mb are detected as well as 8va and 8vb are (the
  detector learnt four times as many); a sign whose letters are read unsurely takes the side
  its hook points to, or the side whose ink it is drawn close against (an 8va over a staff was
  hung under the staff above, moving its notes the wrong way); a line's end is where its dashes
  end, not the first ink after them (a note's head under a missed hook stretched the line over
  it, and the next system's ledger-line notes reaching up cut it short); and the letters of a
  faint sign no longer read as a note head.
- The braces and brackets that join a system's staves are read: the parts a brace joins are
  written in a `grandStaff { … }` (a piano's two staves), a bracket's in a `staffGroup { … }`
  (a quartet's four), a brace within a bracket nested. Before, every part stood alone in the
  score block.
- Tempo markings standing high over the staff (over a rehearsal mark's box, over a bar of notes
  on ledger lines, up to sixteen spaces) are read; before, the band stopped at nine and cut
  them off. The words are read again by themselves, cut out on white at a larger scale, when
  the reading of the whole band is unclear, and the clearest reading (by the tempo vocabulary)
  is taken: a bold "Meno mosso (♩ = 96)" the OCR read as "Me110 mOSSO" or lost. A metronome
  mark's "=" read as "一" or "-" and a 0 read as "の" are taken as the figures they are.
- A ledger line between a high note and the staff is no longer taken for a tenuto on the
  note below it.

## 0.3.0

First release on the Marketplace (Beta, free; Windows x64; needs Lily# 0.13.0 or later).

- Reads a page image (PNG, JPEG, BMP, TIFF) or a PDF of printed music into Lily# text, with
  the places it is unsure of marked `@todo` in the text and in a side file (`x.omr.json`) that
  Lily# uses to draw the marks and to open the original page beside the score.
- Reads staves, clefs, key and time signatures, notes and chords, rests, dots, accidentals,
  beams, tuplets, ties and slurs, grace notes, several voices on a staff, piano systems,
  multi-bar rests, percent repeats, octave lines, guitar and bass TAB, drums, dynamics,
  hairpins, pedal marks and articulations, the header (title, subtitle, composer, poet, tempo)
  and boxed rehearsal marks as sections.
- Not yet: lyrics and chord names, repeat barlines and volta brackets, single-letter boxed
  marks (become a nameless section), handwriting. See the readme's "Not yet (Beta)".
