#!/usr/bin/env python3
"""Whether a song's syllables land on the right notes (S54). tools/score-lyrics.ps1 counts spellings by bar
and cannot tell. Here the truth's .lys and the reader's omr/omr.lys both go through `lysc xml`, which writes
the syllables as drawn (a syllable run past the bar's notes is not there), and each syllable is looked for
under the same part, bar and time, with the same spelling.

Prints one line for the set: "on note" as recall and precision, and "undrawn", the syllables written in the
reader's lyrics tracks that lysc drew nowhere. With --detail, a piece's lines and the first misses."""
import argparse
import collections
import fractions
import os
import re
import shutil
import subprocess
import sys
import tempfile
import xml.etree.ElementTree as ET

LYSC = r'C:\MyProj\LilySharp\LilySharp.Cli\bin\Release\net10.0\lysc.exe'


def syllables(xml_path):
    """Counter of (part, bar, time in quarters, syllable) in a MusicXML file."""
    found = collections.Counter()
    root = ET.parse(xml_path).getroot()
    for pi, part in enumerate(root.findall('part')):
        divisions = 1
        for bi, measure in enumerate(part.findall('measure')):
            at = 0
            last = 0
            for el in measure:
                if el.tag == 'attributes':
                    d = el.find('divisions')
                    if d is not None:
                        divisions = int(d.text)
                elif el.tag == 'backup':
                    at -= int(el.find('duration').text)
                elif el.tag == 'forward':
                    at += int(el.find('duration').text)
                elif el.tag == 'note':
                    dur = el.find('duration')
                    length = int(dur.text) if dur is not None else 0
                    start = last if el.find('chord') is not None else at
                    for ly in el.findall('lyric'):
                        text = ''.join(t.text or '' for t in ly.findall('text'))
                        if text.strip():
                            found[(pi, bi, fractions.Fraction(start, divisions), text.strip())] += 1
                    if el.find('chord') is None:
                        last = at
                        at += length
    return found


def written(lys_path):
    """How many syllables the lyrics tracks of a .lys write."""
    n = 0
    with open(lys_path, encoding='utf-8-sig') as f:
        for line in f:
            m = re.match(r'^\s*lyrics\s+\S+\s*\{(.*)\}\s*$', line)
            if m:
                n += sum(1 for w in re.findall(r'"[^"]*"-?|\S+', m.group(1)) if w not in ('|', '~', '_'))
    return n


def to_xml(lys, folder, name):
    """The .lys's MusicXML, made in a folder of its own: lysc writes it beside its input, under its name."""
    copy = os.path.join(folder, name + '.lys')
    shutil.copyfile(lys, copy)
    # (a console of no input: lysc probes it at the start, the check does the same)
    r = subprocess.run([LYSC, 'xml', copy], stdin=subprocess.DEVNULL, capture_output=True, text=True, encoding='utf-8', errors='replace')
    out = os.path.join(folder, name + '.xml')
    return (out if os.path.exists(out) else None), r.stdout + r.stderr


ap = argparse.ArgumentParser()
ap.add_argument('out', help='a run-gen.ps1 folder')
ap.add_argument('--detail', action='store_true')
args = ap.parse_args()

pieces = truth_n = read_n = hit = in_tracks = drawn = failed = 0
for dirpath, dirnames, filenames in os.walk(args.out):
    name = os.path.basename(dirpath)
    src = os.path.join(dirpath, name + '.lys')
    omr = os.path.join(dirpath, 'omr', 'omr.lys')
    if not (os.path.exists(src) and os.path.exists(omr)) or written(src) == 0:
        continue
    with tempfile.TemporaryDirectory() as folder:
        tx, log1 = to_xml(src, folder, 'truth')
        rx, log2 = to_xml(omr, folder, 'read')
        if not (tx and rx):
            failed += 1
            print(f'{name}: lysc xml failed: {(log1 if not tx else log2).strip()[:200]}', file=sys.stderr)
            continue
        t, r = syllables(tx), syllables(rx)
    common = sum((t & r).values())
    pieces += 1
    truth_n += sum(t.values())
    read_n += sum(r.values())
    hit += common
    w = written(omr)
    in_tracks += w
    drawn += sum(r.values())
    if args.detail:
        print(f'{name}: truth {sum(t.values())} read {sum(r.values())} on note {common}, written {w}')
        for k in sorted((t - r).keys(), key=lambda k: (k[0], k[1], k[2]))[:8]:
            print(f'   miss part {k[0]} bar {k[1] + 1} at {float(k[2]):g}: {k[3]}')
        for k in sorted((r - t).keys(), key=lambda k: (k[0], k[1], k[2]))[:8]:
            print(f'   extra part {k[0]} bar {k[1] + 1} at {float(k[2]):g}: {k[3]}')


def pc(a, b):
    return f'{100.0 * a / b:.1f}' if b else 'n/a'


print(f'pieces {pieces} failed {failed} syllables {truth_n} read {read_n} on note {hit}  recall {pc(hit, truth_n)}  precision {pc(hit, read_n)}  undrawn {in_tracks - drawn} of {in_tracks}')
