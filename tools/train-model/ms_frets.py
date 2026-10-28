"""The shapes of a TAB's fret numbers in each face MuseScore draws them in (W-A: TAB digits in other fonts).

MuseScore draws a TAB's frets in the text style tab_fret_number, whose face a style file sets
(tabFretNumberFontFace, any face the system has; it takes effect only on a score MuseScore has read
as its own .mscx, not on MusicXML it imports). The SVG gives a fret as one path of class Note ("12" is
one path), so its shape is looked up as ms_boxes.py looks up a glyph's, with the fret's number for a
name. The table is made by drawing a TAB with the frets 0 to 24 in order in every face:

  python ms_frets.py [face ...]          -> data/ms-frets.tsv (the faces of run-ms.ps1 when none given)

A row's font is "fret:<face>" ("fret:default" is MuseScore's own face), "fret:<face>|bold" or
"fret:<face>|italic" for the face's styles (the size is the table's, ms_boxes.FRET_SIZE: a fret drawn
larger is scaled back to it); ms_boxes.py splits a fret of two digits by the widths of the single digits' rows.
A face MuseScore does not know draws MuseScore's own: it is left out (its shapes are the default's).
"""
import os, sys, subprocess, tempfile
import ms_boxes

MUSESCORE = r'C:\Program Files\MuseScore 4\bin\MuseScore4.exe'
OUT = os.path.join(ms_boxes.HERE, '..', '..', 'data', 'ms-frets.tsv')
# Windows' faces, and MuseScore's own text faces (MuseJazz Text and Petaluma Text are lettered by hand)
FACES = ['default', 'Arial', 'Times New Roman', 'Georgia', 'Palatino Linotype', 'Cambria', 'Sitka Text',
         'Courier New', 'Bahnschrift', 'Trebuchet MS', 'Candara', 'Franklin Gothic Medium',
         'Segoe Print', 'Segoe Script', 'Ink Free', 'MV Boli', 'Comic Sans MS',
         'MuseJazz Text', 'Petaluma Text', 'Edwin', 'Leland Text', 'Finale Broadway Text', 'Gabriola',
         'Constantia', 'Corbel', 'Consolas', 'Verdana']
STYLES = {'regular': 0, 'bold': 1, 'italic': 2}   # tabFretNumberFontStyle
FRETS = 25
STEPS = ['C', 'C', 'D', 'D', 'E', 'F', 'F', 'G', 'G', 'A', 'A', 'B']


def calibration_xml():
    """A guitar part as Lily# writes one (a staff and its TAB), the frets 0..24 on the top string in 5/4
    measures of quarters (a page of five-line staves too, so the TAB's wider lines tell it apart)."""
    tuning = ''.join('<staff-tuning line="%d"><tuning-step>%s</tuning-step><tuning-octave>%d</tuning-octave></staff-tuning>'
                     % (i + 1, s, o) for i, (s, o) in enumerate([('E', 2), ('A', 2), ('D', 3), ('G', 3), ('B', 3), ('E', 4)]))
    measures = []
    for m in range(FRETS // 5):
        staves = []
        for staff, voice in ((1, 1), (2, 5)):
            notes = []
            for k in range(5):
                fret = m * 5 + k
                midi = 64 + fret
                step, alter, octave = STEPS[midi % 12], 1 if midi % 12 in (1, 3, 6, 8, 10) else 0, midi // 12 - 1
                tech = '<notations><technical><string>1</string><fret>%d</fret></technical></notations>' % fret if staff == 2 else ''
                notes.append('<note><pitch><step>%s</step>%s<octave>%d</octave></pitch><duration>1</duration><voice>%d</voice>'
                             '<type>quarter</type><staff>%d</staff>%s</note>'
                             % (step, '<alter>1</alter>' if alter else '', octave, voice, staff, tech))
            staves.append(''.join(notes))
        attrs = ''
        if m == 0:
            attrs = ('<attributes><divisions>1</divisions><time><beats>5</beats><beat-type>4</beat-type></time><staves>2</staves>'
                     '<clef number="1"><sign>G</sign><line>2</line></clef><clef number="2"><sign>TAB</sign><line>5</line></clef>'
                     '<staff-details number="2"><staff-lines>6</staff-lines>%s</staff-details></attributes>' % tuning)
        measures.append('<measure number="%d">%s%s<backup><duration>5</duration></backup>%s</measure>'
                        % (m + 1, attrs, staves[0], staves[1]))
    return ('<?xml version="1.0" encoding="UTF-8"?><score-partwise version="4.0"><part-list><score-part id="P1">'
            '<part-name>Guitar</part-name></score-part></part-list><part id="P1">%s</part></score-partwise>' % ''.join(measures))


def run(*args):
    subprocess.run([MUSESCORE] + list(args), stdin=subprocess.DEVNULL, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, check=True)


def style(face, fstyle='regular'):
    extra = '' if face == 'default' else '<tabFretNumberFontFace>%s</tabFretNumberFontFace>' % face
    if STYLES[fstyle]:
        extra += '<tabFretNumberFontStyle>%d</tabFretNumberFontStyle>' % STYLES[fstyle]
    return '<?xml version="1.0" encoding="UTF-8"?>\n<museScore version="4.70">\n<Style>\n%s\n</Style>\n</museScore>\n' % extra


def frets(svg):
    """The TAB's Note paths in reading order: (shape, box)."""
    page = ms_boxes.Page(svg)
    found = [(g[2][1], g[2][0], g[1]) for g in page.glyphs() if g[0] == 'Fret']
    # rows of the page first (a system's top), then left to right
    found.sort(key=lambda f: (round(f[0] / (10 * page.space)), f[1]))
    return [f[2] for f in found]


def main(faces):
    tmp = tempfile.mkdtemp(prefix='ms-frets-')
    xml = os.path.join(tmp, 'frets.xml')
    open(xml, 'w', encoding='utf-8').write(calibration_xml())
    mscx = os.path.join(tmp, 'frets.mscx')
    run('-o', mscx, xml)
    rows = []
    default = None
    for face in faces:
        for fstyle in STYLES:
            if face == 'default' and fstyle != 'regular':
                continue
            font = ms_boxes.fret_font(face, fstyle)
            mss = os.path.join(tmp, 'face.mss')
            open(mss, 'w', encoding='utf-8').write(style(face, fstyle))
            svg = os.path.join(tmp, 'frets.svg')
            try:
                run('-S', mss, '-o', svg, mscx)
            except subprocess.CalledProcessError as e:
                # MuseScore 4.7.5 crashes writing the SVG of some faces (Segoe Print)
                print('%s: MuseScore failed (%d); left out' % (font, e.returncode & 0xffffffff))
                continue
            svg = os.path.join(tmp, 'frets-1.svg')
            shapes = frets(svg)
            os.remove(svg)
            if len(shapes) != FRETS:
                print('%s: %d frets found, not %d; left out' % (font, len(shapes), FRETS))
                continue
            if face == 'default':
                default = [s[0] for s in shapes]
            elif default and [s[0] for s in shapes] == default:
                print('%s: MuseScore\'s own face (the face unknown); left out' % font)
                continue
            keys, alike = {}, []
            for n, sh in enumerate(shapes):
                k = (sh[0], sh[1])
                for other, osh in keys.get(k, []):
                    if max(abs(a - b) for a, b in zip(sh[2:], osh[2:])) <= ms_boxes.TOLERANCE:
                        alike.append('%d and %d' % (other, n))
                keys.setdefault(k, []).append((n, sh))
            if alike:
                # (a 9 drawn as the 6 turned round: Impact) the boxes would name one for the other
                print('%s: frets %s look alike; left out' % (font, ', '.join(alike)))
                continue
            rows.extend((font, 'Fret') + tuple(sh) + ('fret.%d' % n,) for n, sh in enumerate(shapes))
            print('%s: %d frets' % (font, len(shapes)))
    with open(OUT, 'w', newline='', encoding='utf-8') as f:
        f.write('# made by ms_frets.py: the fret numbers 0..24 of a TAB, per face\n')
        f.write('font\tclass\thash\tsubpaths\tw\th\tcx\tcy\tname\n')
        for r in rows:
            f.write('%s\t%s\t%s\t%d\t%.3f\t%.3f\t%.3f\t%.3f\t%s\n' % r)
    print('%d rows -> %s' % (len(rows), os.path.abspath(OUT)))


if __name__ == '__main__':
    main(sys.argv[1:] or FACES)
