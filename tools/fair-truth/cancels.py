import sys, xml.etree.ElementTree as ET
# cancels.py <truth.xml>...: the accidentals lysc prints to cancel one met earlier in the same bar and staff (a natural
# after a sharp, a sharp after a flat...), from the MusicXML lysc writes for a truth (lysc xml). Where the print has no
# sign there, the truth dropped an accidental that carries on to the end of the bar (fis ... f where the print means
# fis ... fis), the commonest truth error against homr (Kuecken, Wuerst, Puget 2026-11-18/19). Each line: the piece, the
# bar (lysc's measure number), the part and staff, the earlier pitch and the cancelling one. For checking the truth only.
STEPS = 'CDEFGAB'
def name(step, octave, alter):
    n = step.lower() + {-2: 'eses', -1: 'es', 0: '', 1: 'is', 2: 'isis'}[alter]
    o = octave - 3
    return n + ("'" * o if o > 0 else ',' * -o)
for path in sys.argv[1:]:
    root = ET.parse(path).getroot()
    names = {p.get('id'): (p.findtext('part-name') or p.get('id')) for p in root.iter('score-part')}
    for part in root.iter('part'):
        pname = names.get(part.get('id'), part.get('id'))
        for m in part.iter('measure'):
            t = 0; last = 0; notes = []
            for el in m:
                if el.tag == 'backup': t -= int(el.findtext('duration') or 0)
                elif el.tag == 'forward': t += int(el.findtext('duration') or 0)
                elif el.tag == 'note':
                    d = int(el.findtext('duration') or 0)
                    on = last if el.find('chord') is not None else t
                    if el.find('chord') is None: last = t; t += d
                    p = el.find('pitch')
                    if p is None or el.find('grace') is not None: continue
                    notes.append((on, el.findtext('staff') or '1', p.findtext('step'), int(p.findtext('octave')),
                                  int(float(p.findtext('alter') or 0)), el.findtext('accidental')))
            for on, st, step, octv, alt, acc in notes:
                before = [n for n in notes if n[0] < on and n[1] == st and n[2] == step and n[3] == octv and n[4] != alt]
                if before:
                    b = max(before)
                    print(f"{path.split(chr(92))[-1].split('/')[-1]:24} bar {m.get('number'):>4} {pname:8} staff {st}  "
                          f"{name(step, octv, b[4]):8} -> {name(step, octv, alt):8}")
