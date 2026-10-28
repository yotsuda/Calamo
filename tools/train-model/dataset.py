"""Training data for the symbol detector (S10): pages at a fixed staff space, and the symbol centres on them.

Every generated piece the OMR read with --export-boxes has <dir>/pNN.png and
<dir>/omr/boxes-pNN.tsv (Lily#'s glyphs and ties from the SVG, and the staves the reader found).
The page is resampled so that one staff space is SPACE pixels (the only thing taken from the
reader: the staff space, as the median over its staves); its targets are the classes and centres
of every glyph on the page. Where the staves are is not used: the detector must find the symbols
wherever they stand, since on a scan the reader's staves are not to be trusted (S10, second stage).

  python dataset.py <out.npz> <set dir> [<set dir> ...]     e.g. out/train out/train-s3 out/train-scan
"""
import os, sys, glob, csv
import numpy as np
from PIL import Image

SPACE = 16                       # pixels per staff space on a resampled page
HEIGHT = 14 * SPACE              # the band the detector sees at a time (a strip 14 spaces tall)

# glyph name -> class (the shapes the detector tells apart; 0 is background)
CLASSES = ['none', 'head_black', 'head_half', 'head_whole', 'rest_long', 'rest_4', 'rest_8', 'rest_16',
           'sharp', 'flat', 'natural', 'dsharp', 'dflat', 'dot', 'clef_g', 'clef_f', 'clef_c',
           # a time signature's digit, by its value (S11: the rules' shape reading of a digit fails on a
           # scan, and a digit found without its value is no help; "12" is a 1 and a 2)
           'digit_1', 'digit_2', 'digit_3', 'digit_4', 'digit_5', 'digit_6', 'digit_7', 'digit_8', 'digit_9',
           'timesig_c', 'flag',
           # a stem's tip with 0, 1, 2 or 3 beams or flags at it (S11: on a scan the beams' toner thins
           # and the rules count too few; the tip's surroundings tell the count)
           'stem_0', 'stem_1', 'stem_2', 'stem_3',
           # a barline, one target per staff it crosses (at the staff's middle): on a scan a thin
           # barline fades and the rules miss it, merging two measures
           'barline',
           # a triplet's number (the generator writes no other tuplet): cue-sized, outside the staff
           'tuplet_3',
           # a tie's two ends (S12: the redraw check needs to see ties, and no rule reads them well;
           # the left end stands at the head the tie starts from, the right at the head it ends on)
           'tie_l', 'tie_r',
           # a slur's two ends (M3): over or under the first and last heads it spans, or hanging at
           # a system's edge where it breaks
           'slur_l', 'slur_r',
           # band scores (B3): a drum's cross head (and the circled cross), a slash head black and
           # hollow (rhythm slashes; the stem tells a half from a whole), a percent repeat of one bar
           # and of two, and a TAB fret number's digits by value ("12" is a 1 and a 2)
           'head_x', 'slash_black', 'slash_hollow', 'percent_1', 'percent_2',
           'tab_0', 'tab_1', 'tab_2', 'tab_3', 'tab_4', 'tab_5', 'tab_6', 'tab_7', 'tab_8', 'tab_9',
           # dynamics, hairpins and the pedal (M3, S14): a dynamic's letters one by one (mf is an m and
           # an f), a hairpin's tip and the middle of its open end (which side the tip is on tells a
           # crescendo from a decrescendo), the pedal's Ped. and its release *, a pedal bracket's start
           # and end hooks and the notch where it changes. Added at the end: the sets made before keep
           # their numbers, and their pages, with none of these marks, teach where there are none
           'dyn_p', 'dyn_m', 'dyn_f', 'dyn_r', 'dyn_s', 'dyn_z', 'hairpin_tip', 'hairpin_open',
           'ped', 'ped_up', 'ped_l', 'ped_r', 'ped_notch',
           # articulations (M3, S15): staccato, accent, tenuto, marcato, staccatissimo (the one above
           # the note and the one below are one class: where it stands tells which) and the fermata.
           # No set made before has any (the band's flageolet is no class)
           'art_staccato', 'art_accent', 'art_tenuto', 'art_marcato', 'art_staccatissimo', 'fermata',
           # the other tuplet numbers (a quintuplet's, sextuplet's and septuplet's: OmrGen --tuplets).
           # No set made before has any (the generator wrote triplets only)
           'tuplet_5', 'tuplet_6', 'tuplet_7',
           # octave lines (M3, S18): the sign's letters by what they say ("(8va)" where a line goes on from
           # a system's start is the same as 8va; an 8va over a staff and an 8vb under the staff above it
           # stand in the same gap, and only the letters tell them apart) and the hook where its dashes
           # end. No set made before has any
           'ott_8va', 'ott_8vb', 'ott_15ma', 'ott_15mb', 'ott_end',
           # grace notes (M3, S21): the small head (its stem's tip is a stem_N like any other, its
           # accidental a sharp, flat or natural like any other) and an acciaccatura's slash through
           # the stem or the beam. The lied sets made before have a few, unlabelled
           'grace_head', 'grace_slash',
           # the form (S22): a repeat barline's two dots (one target at the staff's middle; right of
           # the thick line at a |:, left of it at a :|), a volta bracket's start (the corner of its
           # left hook, the number beside it) and end (its right hook's corner; a bracket going on
           # from a system's start has no start, one going on past its end no end), and the segno
           # and coda signs. No set made before has any
           'rep_open', 'rep_close', 'volta_l', 'volta_r', 'segno', 'coda',
           # ornaments (M3, S23): the trill's tr, the mordent, the prall, the turn and the reverse
           # turn, all set above the staff. No set made before has any
           'orn_trill', 'orn_mordent', 'orn_prall', 'orn_turn', 'orn_reverseturn',
           # the rarer scripts (OmrGen --scripts2): the portato (a tenuto's line and a staccato's dot;
           # the one above and the one below one class), the stopped note's + and the pralltriller.
           # No set made before has any
           'art_portato', 'art_stopped', 'orn_prallprall']
# the classes a page the reader read teaches (pseudo.py), by the kind of region: a bar read right (0)
# teaches its notes, rests, signs, dots, stems' tips, barlines and clefs; a system's start before its
# first note (1) its clef, key and time. A region of kind 2 teaches nothing (a mark the reader did not
# place, or a symbol of a bar read wrong): the loss looks nowhere else on such a page
GROUPS = {0: [CLASSES.index(c) for c in ['head_black', 'head_half', 'head_whole', 'rest_long', 'rest_4', 'rest_8', 'rest_16',
                                         'sharp', 'flat', 'natural', 'dsharp', 'dflat', 'dot', 'clef_g', 'clef_f', 'clef_c',
                                         'stem_0', 'stem_1', 'stem_2', 'stem_3', 'barline']],
          1: [CLASSES.index(c) for c in ['head_black', 'head_half', 'head_whole', 'rest_long', 'rest_4', 'rest_8', 'rest_16',
                                         'sharp', 'flat', 'natural', 'dot', 'clef_g', 'clef_f', 'clef_c', 'timesig_c',
                                         'digit_1', 'digit_2', 'digit_3', 'digit_4', 'digit_5', 'digit_6', 'digit_7', 'digit_8', 'digit_9']]}
CLASS_OF = {
    'noteheads.s2': 'head_black', 'noteheads.s1': 'head_half', 'noteheads.s0': 'head_whole',
    'rests.0': 'rest_long', 'rests.1': 'rest_long', 'rests.2': 'rest_4', 'rests.3': 'rest_8', 'rests.4': 'rest_16',
    'accidentals.sharp': 'sharp', 'accidentals.flat': 'flat', 'accidentals.natural': 'natural',
    'accidentals.doublesharp': 'dsharp', 'accidentals.flatflat': 'dflat', 'dots.dot': 'dot',
    'clefs.G': 'clef_g', 'clefs.F': 'clef_f', 'clefs.C': 'clef_c',
    'timesig.C44': 'timesig_c', 'timesig.C22': 'timesig_c',
    'noteheads.s2cross': 'head_x', 'noteheads.s1cross': 'head_x', 'noteheads.s0cross': 'head_x',
    'noteheads.s2xcircle': 'head_x',
    'noteheads.s2slash': 'slash_black', 'noteheads.s1slash': 'slash_hollow', 'noteheads.s0slash': 'slash_hollow',
    'percent.1': 'percent_1', 'percent.2': 'percent_2',
    'hairpin.tip': 'hairpin_tip', 'hairpin.open': 'hairpin_open',
    'pedal.Ped': 'ped', 'pedal.*': 'ped_up', 'ped.l': 'ped_l', 'ped.r': 'ped_r', 'ped.notch': 'ped_notch',
    'scripts.staccato': 'art_staccato', 'scripts.sforzato': 'art_accent', 'scripts.tenuto': 'art_tenuto',
    'scripts.umarcato': 'art_marcato', 'scripts.dmarcato': 'art_marcato',
    'scripts.ustaccatissimo': 'art_staccatissimo', 'scripts.dstaccatissimo': 'art_staccatissimo',
    'scripts.ufermata': 'fermata', 'scripts.dfermata': 'fermata',
    'ott.8va': 'ott_8va', 'ott.8vb': 'ott_8vb', 'ott.15ma': 'ott_15ma', 'ott.15mb': 'ott_15mb', 'ott.end': 'ott_end',
    'grace.head': 'grace_head', 'grace.slash': 'grace_slash',
    'rep.open': 'rep_open', 'rep.close': 'rep_close', 'volta.l': 'volta_l', 'volta.r': 'volta_r',
    'scripts.segno': 'segno', 'scripts.coda': 'coda', 'scripts.varcoda': 'coda',
    'scripts.trill': 'orn_trill', 'scripts.mordent': 'orn_mordent', 'scripts.prall': 'orn_prall',
    'scripts.turn': 'orn_turn', 'scripts.reverseturn': 'orn_reverseturn',
    'scripts.uportato': 'art_portato', 'scripts.dportato': 'art_portato', 'scripts.stopped': 'art_stopped',
    'scripts.prallprall': 'orn_prallprall',
}


def class_of(name):
    if name in CLASS_OF:
        return CLASSES.index(CLASS_OF[name])
    if len(name) == 1 and name in '123456789':
        return CLASSES.index('digit_' + name)
    if name.startswith('flags.'):
        return CLASSES.index('flag')
    if name.startswith('stem.'):
        return CLASSES.index('stem_' + name[5:])
    if name == 'barline':
        return CLASSES.index('barline')
    if name.startswith('tuplet.') and 'tuplet_' + name[7:] in CLASSES:
        return CLASSES.index('tuplet_' + name[7:])
    if name.startswith('tab.'):
        return CLASSES.index('tab_' + name[4:])
    # (niente's n is too rare to learn: not a class)
    if name.startswith('dyn.') and 'dyn_' + name[4:] in CLASSES:
        return CLASSES.index('dyn_' + name[4:])
    return 0


def read_boxes(path):
    glyphs, staves = [], []
    with open(path, newline='') as f:
        for row in csv.DictReader(f, delimiter='\t'):
            if row['kind'] == 'glyph':
                c = class_of(row['name'])
                if c:
                    glyphs.append((c, float(row['l']), float(row['t']), float(row['r']), float(row['b'])))
            elif row['kind'] in ('tie', 'slur'):
                # the row holds the curve's two ends (x1 y1 x2 y2), left one first: a target at each
                x1, y1, x2, y2 = float(row['l']), float(row['t']), float(row['r']), float(row['b'])
                glyphs.append((CLASSES.index(row['kind'] + '_l'), x1, y1, x1, y1))
                glyphs.append((CLASSES.index(row['kind'] + '_r'), x2, y2, x2, y2))
            elif row['kind'] == 'staff':
                staves.append((float(row['l']), float(row['t']), float(row['r']), float(row['b']), float(row['src'])))
    return glyphs, staves


def staff_space(staves):
    """The page's staff space: the median of the reader's staves (a staff it got wrong is an outlier)."""
    s = sorted(st[4] for st in staves if st[4] >= 4)
    return s[len(s) // 2] if s else None


def page_of(png, boxes, space=None):
    """(image uint8 HxW at SPACE per staff space, targets [(class, cx, cy)], scale) for the page."""
    page = Image.open(png).convert('L')
    glyphs, staves = read_boxes(boxes)
    s = space or staff_space(staves)
    if s is None:
        return None
    scale = SPACE / s
    w, h = max(1, int(round(page.width * scale))), max(1, int(round(page.height * scale)))
    img = page.resize((w, h), Image.Resampling.BOX)
    targets = [(c, (l + r) / 2 * scale, (t + b) / 2 * scale) for (c, l, t, r, b) in glyphs]
    return np.array(img, dtype=np.uint8), targets, scale


def build(dirs):
    images, targets, names = [], [], []
    for d in dirs:
        for boxes in sorted(glob.glob(os.path.join(d, '**', 'omr', 'boxes-p*.tsv'), recursive=True)):
            page = os.path.basename(boxes)[6:-4]            # p01
            piece = os.path.dirname(os.path.dirname(boxes))
            png = os.path.join(piece, page + '.png')
            if not os.path.exists(png):
                continue
            got = page_of(png, boxes)
            if got is None:
                continue
            img, tg, _ = got
            images.append(img); targets.append(np.array(tg, dtype=np.float32).reshape(-1, 3))
            names.append('%s/%s' % (os.path.basename(piece), page))
    return images, targets, names


if __name__ == '__main__':
    out, dirs = sys.argv[1], sys.argv[2:]
    images, targets, names = build(dirs)
    # variable sizes: stored as object arrays, filled one by one (numpy would try to stack them)
    def ragged(items):
        a = np.empty(len(items), dtype=object)
        for i, it in enumerate(items):
            a[i] = it
        return a
    np.savez_compressed(out, images=ragged(images), targets=ragged(targets), names=np.array(names))
    n = sum(len(t) for t in targets)
    counts = np.bincount(np.concatenate([t[:, 0].astype(int) for t in targets if len(t)]), minlength=len(CLASSES))
    print('%d pages, %d symbols -> %s' % (len(images), n, out))
    for c, k in zip(CLASSES, counts):
        if k:
            print('  %-10s %6d' % (c, k))
