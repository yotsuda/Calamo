"""Mend a band piece before MuseScore draws it (B4, W-A: band training data in other fonts).

The MusicXML Lily# writes: percent repeats (one and two bars) come with their start and stop, and
still the repeated measures' notes (for playback). MuseScore puts the sign in the first voice and
would draw the other voices' notes under it (the drums' two voices), so a repeated measure loses its
notes: the file is no notes' truth. (The export's two slips, a treble_8 part's octave counted twice and
a one-line staff's lines left out, docs/repro/lilysharp-feedback-2026-10-01.md, Lily# has put right:
an export of an older lysc is drawn wrong.)

The .mscx MuseScore makes of it (run-ms.ps1 -Band draws from that, where a style file can set the
frets' face): a TAB's staff type. MuseScore draws a TAB's time signature, which no band score here
does; and its rhythm (stems, beams, rests) under the frets, apart from them. A band score draws the
rhythm from the frets down (Fantasy) or none (Lily#, Time After Time's TAB): each piece takes one at
random (the piece's path the seed), stems through the TAB or none; and its lines through the frets or
broken for them, at a line distance at random.

  python ms_band_xml.py [--mute] <piece.xml> [...]   rewritten in place (--mute: training data's muted notes)
  python ms_band_xml.py --mscx <piece.mscx> [...]    rewritten in place
"""
import re, sys, zlib, random
import xml.etree.ElementTree as ET

MUSIC = ('note', 'backup', 'forward')
RHYTHM = 0.6                        # the share of pieces whose TAB shows the rhythm
LINES_THROUGH = 0.4                 # the share of pieces whose TAB's lines run through the frets
LINE_DISTANCE = (1.35, 1.8)         # a TAB's line distance in staff spaces (ms_boxes.py takes over 1.2 for a TAB)
MUTED_PIECES = 0.5                  # --mute: the share of guitar and bass parts with muted notes
MUTED_NOTES = 0.12                  # and of their notes or chords muted


def repeated(measures):
    """Whether each measure is under a percent sign (from a repeat's start to its stop)."""
    on, out = False, []
    for m in measures:
        for r in m.iter('measure-repeat'):
            on = r.get('type') == 'start'
        out.append(on)
    return out


STEPS = 'CDEFGAB'
CLEF_NOTE = {'G': 4 * 7 + 4, 'F': 3 * 7 + 3, 'C': 4 * 7}   # the note a clef's sign marks, as octave * 7 + step


def middle_line(part):
    """The step and octave of the part's middle staff line, by its first clef (None if it has none
    with a pitch: percussion, TAB)."""
    clef = part.find('measure/attributes/clef')
    if clef is None or clef.findtext('sign') not in CLEF_NOTE:
        return None
    n = CLEF_NOTE[clef.findtext('sign')] + 2 * (3 - int(clef.findtext('line') or 3))
    n += 7 * int(clef.findtext('clef-octave-change') or 0)
    return STEPS[n % 7], str(n // 7)


def slashes_on_middle_line(part):
    """Lily# writes a rhythm slash as unpitched B4 whatever the clef; MuseScore draws it there (over a
    treble_8 staff a ledger line above) and takes it for a B natural (a natural before it in a flat
    key), where a band score draws it on the middle line with no sign: the slash is made the middle
    line's note as the key alters it."""
    mid = middle_line(part)
    if mid is None:
        return
    fifths = int(part.findtext('measure/attributes/key/fifths') or 0)
    order = 'FCGDAEB' if fifths >= 0 else 'BEADGCF'
    alter = (1 if fifths > 0 else -1) if mid[0] in order[:abs(fifths)] else 0
    for note in part.iter('note'):
        u = note.find('unpitched')
        if u is not None and note.findtext('notehead') == 'slash':
            i = list(note).index(u)
            note.remove(u)
            p = ET.Element('pitch')
            ET.SubElement(p, 'step').text = mid[0]
            if alter:
                ET.SubElement(p, 'alter').text = str(alter)
            ET.SubElement(p, 'octave').text = mid[1]
            note.insert(i, p)


def mute(part, rng):
    """A guitar's or bass's muted notes (training data only, --mute): now and then a note or chord of the
    five-line staff drawn with cross heads, as Fantasy's guitar strikes one before its chords. The TAB's
    notes (the second staff) keep their frets."""
    if rng.random() >= MUTED_PIECES:
        return
    on = False
    for note in part.iter('note'):
        if note.find('rest') is not None or note.findtext('staff', '1') != '1' or note.findtext('notehead') == 'slash':
            continue
        if note.find('chord') is None:
            on = rng.random() < MUTED_NOTES
        if on:
            nh = ET.Element('notehead')
            nh.text = 'x'
            # the schema's order: before staff, beam, notations, lyric
            after = [i for i, e in enumerate(note) if e.tag in ('staff', 'beam', 'notations', 'lyric')]
            note.insert(after[0] if after else len(note), nh)


def mend(path, muted=False):
    tree = ET.parse(path)
    rng = random.Random(zlib.crc32(path.encode()))
    root = tree.getroot()
    names = {sp.get('id'): sp.findtext('part-name') for sp in root.iter('score-part')}
    for part in root.iter('part'):
        measures = part.findall('measure')
        for m, r in zip(measures, repeated(measures)):
            if r:
                for e in [e for e in m if e.tag in MUSIC]:
                    m.remove(e)
        slashes_on_middle_line(part)
        if muted and names.get(part.get('id')) in ('Guitar', 'Bass'):
            mute(part, rng)
    tree.write(path, encoding='UTF-8', xml_declaration=True)


def mend_mscx(path):
    text = open(path, encoding='utf-8').read()
    rng = random.Random(zlib.crc32(path.encode()))
    rhythm = rng.random() < RHYTHM
    sets = {'timesig': '0'}
    sets.update({'stemsThrough': '1', 'stemsDown': '1', 'showRests': '1'} if rhythm else {'stemless': '1', 'showRests': '0'})
    # the lines drawn through the frets (Time After Time's) or broken for them (MuseScore's, Fantasy's),
    # and the lines' distance (1.5 spaces MuseScore's; Time After Time's 1.3, its frets larger than that)
    sets['linesThrough'] = '1' if rng.random() < LINES_THROUGH else '0'
    sets['lineDistance'] = '%.2f' % rng.uniform(*LINE_DISTANCE)

    def fix(m):
        body = m.group(0)
        for k, v in sets.items():
            if '<%s>' % k in body:
                body = re.sub(r'<%s>[^<]*</%s>' % (k, k), '<%s>%s</%s>' % (k, v, k), body)
            else:
                body = body.replace('</StaffType>', '<%s>%s</%s>\n          </StaffType>' % (k, v, k))
        return body

    text = re.sub(r'<StaffType group="tablature">.*?</StaffType>', fix, text, flags=re.S)
    open(path, 'w', encoding='utf-8').write(text)


if __name__ == '__main__':
    if sys.argv[1] == '--mscx':
        for p in sys.argv[2:]:
            mend_mscx(p)
    else:
        muted = sys.argv[1] == '--mute'
        for p in sys.argv[2 if muted else 1:]:
            mend(p, muted)
