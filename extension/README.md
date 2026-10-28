# Lily# Scan Import (Beta)

Optical music recognition (OMR) for [Lily#](https://yotsuda.github.io/LilySharp/): reads a page
image or a PDF of printed sheet music into editable Lily# text (`.lys`). The places it is unsure
of are marked in the text with `@todo`; Lily# shows them in
red in the preview, with a note on what to check, and opens the original page beside the score.

![A scanned page of Tchaikovsky's March of the Wooden Soldiers, and the same bars as the reader wrote them, drawn by Lily# with two places marked in red](https://yotsuda.github.io/LilySharp/scan-import.png)

*Above: a 1904 edition from a scan (G. Schirmer; public domain, from IMSLP). Below: the first
bars as the reader wrote them, drawn by Lily#. Two places are marked for a look: bars where the
notes it read do not fill the time.*

**Beta, open source** ([Apache-2.0](https://github.com/yotsuda/LilySharp-Omr/blob/main/LICENSE)).
Source, issues and reports of misread pages: <https://github.com/yotsuda/LilySharp-Omr>.

## Use

1. Install the [Lily#](https://marketplace.visualstudio.com/items?itemName=yotsuda.lilysharp) extension
   (0.13.0 or later) too: the commands that read a scan are Lily#'s, and this extension shows
   nothing without it.
2. Right-click an image (PNG, JPEG, BMP, TIFF) or a PDF in the Explorer, or run
   **Convert Image/PDF to Lily# (.lys)… (Beta)** from the Command Palette, and pick the pages of
   one piece in order.
3. The `.lys` is written beside the image, with `x.omr.json` (where each bar is on the page) and
   `x.omr-pNN.png` (the pages with the marked places boxed) beside it.
4. Correct what it misread. **Show Original Scan** opens the page beside the score; saving checks
   the corrected text against the page again and keeps the marks that still differ.

This extension adds no commands of its own: it carries the reader that Lily#'s commands run.
What Lily# is, and how its editor and preview work: <https://yotsuda.github.io/LilySharp/>
(the scan commands are under [Commands](https://yotsuda.github.io/LilySharp/commands.html#import-image)).

## What it reads

Printed (engraved) music: staves, clefs (and their changes within a bar), key and time
signatures, a pickup bar, notes and chords, rests, dots,
accidentals, beams, tuplets, ties and slurs, grace notes, several voices on a staff, piano
systems and groups of staves (the braces and brackets that join them), multi-bar rests, percent
repeats, two-note tremolos, octave lines, guitar and bass TAB, drums; dynamics,
hairpins, pedal marks, articulations and ornaments, trill lines ("tr" and a wave), arpeggios, glissandi; common words of expression in italic
("dolce", "rit.", "a tempo", "sempre p") and "rit." or "accel." with a dashed line; the tempo marking, the title, subtitle, composer and poet
over the first system, and the rehearsal marks in boxes over the staff, which become the sections
of the `.lys`.

Best results come from clean digital pages (PDF or image exports of LilyPond, MuseScore, Dorico,
Finale) and good black-and-white scans at about 300 dpi. Older prints scanned small (the 96 dpi
pages of many IMSLP files, nineteenth-century engraving, a page bowed at the binding) are read
too, with more marks to check.

## Not yet (Beta)

- Lyrics and chord names are passed over (not misread as notes, but not written).
- The form is read (repeat barlines, the endings with their numbers, segno and coda, "To Coda",
  "Fine", "D.C. al Fine", "D.S. al Coda" and the like) and written as a `form` line; "D.S." or
  "D.C." standing alone, and a "Fine" among other words, are not.
- Words of expression are written only when they are among the common ones the reader knows
  ("dolce", "espress.", "rit.", "pizz.", "sempre legato" and the like) and printed in italic;
  others ("tenderly", "sul G") are passed over. A "rit." with a dashed line too short to show a
  dash is written as plain text.
- A rehearsal mark of a single letter in a box (A, B, …) becomes a nameless section (`Mark`);
  section names printed without a box are not read.
- Lead sheets: the melody, its header and marks are read over the chord diagrams and verses, and
  the one-line rhythm staff under the melody is written as slashes (`staff gtr as lines 1`); the
  chord names and diagrams are passed over. Parts laid out in columns are read poorly.
- Handwritten music is not read. Colour or low-contrast scans read worse than black and white.
- Words (title, tempo, rehearsal marks) are read with the OCR built into Windows, in the
  languages installed for it (Settings › Language), so a Latin title on a Japanese-only PC may
  come out with a wrong letter or two.

Check what it reads: a mark says where it was unsure, but a bar with no mark can still be wrong.

## Privacy

Everything runs on your computer: a small learnt symbol detector (ONNX), the rules of notation
and Windows' own text recognition read the page. No image or result is sent anywhere.

## Requirements

Windows 10 version 2004 (build 19041) or later, x64. The Lily# extension 0.13.0 or later
(installed separately; this one starts with VS Code, lightly, only to tell Lily# the reader is
there, and never starts Lily# itself).
