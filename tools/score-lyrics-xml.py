#!/usr/bin/env python3
"""How well the reader reads a real song's words (S54): a MusicXML truth's <lyric>s against the lyrics
tracks of the reader's omr.lys, for a run of run-audiveris.ps1 (-Dev or -Eval).

    score-lyrics-xml.py <out\\audiveris> [--prefix dev-] [--detail]

Dichterliebe (dl-ms, dl-scan) counts with --prefix lied-. The syllables are compared in the order sung, verse by verse (the truth's lyric number against the n-th
track, summed over the parts): a longest common subsequence, so that a bar read wrong loses its bar and
no more. Exact as spelt ("found", the apostrophes ' and ’ one), and with case and marks and punctuation folded away ("loose": what a
German or French word comes to with an English and a Japanese OCR). The stanza number some truths have
inside the first syllable ("1. Wenn") is left out. Standard library only.
"""
import argparse
import os
import re
import sys
import unicodedata
import xml.etree.ElementTree as ET
from collections import defaultdict


def truth_verses(path):
    """verse number -> the syllables in score order, a part after another, each as it stands in .lys words"""
    verses = defaultdict(list)
    root = ET.parse(path).getroot()
    for part in root.findall('part'):
        for note in part.iter('note'):
            for ly in note.findall('lyric'):
                text = ''.join(t.text or '' for t in ly.findall('text')).strip()
                text = apostrophe(re.sub(r'^\d+[.)]\s*', '', text))
                if not text:
                    continue
                verses[ly.get('number', '1')].append(text)
    return verses


def read_tracks(path):
    """the lyrics tracks, in order within the part, summed over the parts: n -> syllables"""
    tracks = defaultdict(list)
    seen = defaultdict(int)
    with open(path, encoding='utf-8') as f:
        for line in f:
            m = re.match(r'\s*lyrics\s+(\S+?)words(\d+)\s*\{(.*)\}\s*$', line)
            if not m:
                continue
            for tok in re.findall(r'"[^"]*"-?|[^\s|]+', m.group(3)):
                if tok in ('~', '_', '__'):
                    continue
                tracks[int(m.group(2))].append(apostrophe(tok.rstrip('-').strip('"')))
    return tracks


def apostrophe(s):
    """one apostrophe: the truths mix the typewriter's and the printer's (hab' and glaubt’ in one song),
    and the paper does not tell them apart"""
    return re.sub('[’‘ʼ`]', "'", s)


def fold(s):
    s = unicodedata.normalize('NFD', s)
    s = ''.join(c for c in s if unicodedata.category(c) != 'Mn')
    return re.sub(r'[^\w]', '', s).lower()


def lcs(a, b):
    prev = [0] * (len(b) + 1)
    for x in a:
        cur = [0]
        for j, y in enumerate(b):
            cur.append(prev[j] + 1 if x == y else max(prev[j + 1], cur[j]))
        prev = cur
    return prev[-1]


ap = argparse.ArgumentParser()
ap.add_argument('out')
ap.add_argument('--data', default=None, help='the repository root (default: two up from this script)')
ap.add_argument('--prefix', default='dev-', help='dev- for data/dev/lieder, lied- for data/eval/lieder')
ap.add_argument('--detail', action='store_true')
args = ap.parse_args()
root = args.data or os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
where = {'dev-': os.path.join(root, 'data', 'dev', 'lieder'), 'lied-': os.path.join(root, 'data', 'eval', 'lieder')}[args.prefix]
tot = defaultdict(int)
for name in sorted(os.listdir(args.out)):
    m = re.match(re.escape(args.prefix) + r'(.+)-(ms|scan)$', name)
    # (Dichterliebe, read as one book of sixteen songs: its truths in order, with the held-out set)
    # (and each song's MuseScore drawing, dl-ms-01 ...)
    book = args.prefix == 'lied-' and re.match(r'dl-(ms-\d+|scan)$', name)
    if book:
        m = re.match(r'(dl)-(ms|scan)', name)
    if not m:
        continue
    xml = os.path.join(where, m.group(1), 'ms', 'truth.musicxml')
    xmls = [xml]
    if book:
        ms = os.path.join(root, 'data', 'eval', 'dichterliebe', 'ms')
        song = re.search(r'ms-(\d+)$', name)
        xmls = [os.path.join(ms, d, 'truth.musicxml') for d in sorted(os.listdir(ms)) if not song or d == song.group(1)]
    lys = None
    for sub in ('own', 'scored-own'):
        p = os.path.join(args.out, name, sub, 'omr.lys')
        if os.path.exists(p):
            lys = p
            break
    if lys is None or not all(os.path.exists(x) for x in xmls):
        continue
    truth = defaultdict(list)
    for x in xmls:
        for k, v in truth_verses(x).items():
            truth[k].extend(v)
    read = read_tracks(lys)
    keys = sorted(truth, key=lambda k: (not k.isdigit(), int(k) if k.isdigit() else 0))
    n = sum(len(v) for v in truth.values())
    r = sum(len(v) for v in read.values())
    found = loose = 0
    for i, k in enumerate(keys):
        got = read.get(i + 1, [])
        found += lcs(truth[k], got)
        loose += lcs([fold(x) for x in truth[k]], [fold(x) for x in got])
    tot[m.group(2) + 'n'] += n; tot[m.group(2) + 'r'] += r; tot[m.group(2) + 'f'] += found; tot[m.group(2) + 'l'] += loose
    if args.detail:
        print(f'{name:32} verses {len(truth)}/{len(read)}  syllables {n:5} read {r:5}  found {found:5} ({100 * found / max(1, n):5.1f}%)  loose {loose:5} ({100 * loose / max(1, n):5.1f}%)')
for kind in ('ms', 'scan'):
    n, r, f, l = (tot[kind + x] for x in 'nrfl')
    if n:
        print(f'lyrics {kind}: syllables {n} read {r}  found {f} recall {100 * f / n:.1f}% precision {100 * f / max(1, r):.1f}%  loose {l} recall {100 * l / n:.1f}% precision {100 * l / max(1, r):.1f}%')
