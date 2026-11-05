#!/usr/bin/env python3
"""The real scores' lyric errors by kind (S54), for a run of run-audiveris.ps1 with --read-lyrics: truth
and reading aligned verse by verse, a syllable read wrong, merged with its neighbour, or split; runs of
syllables missed or extra by length (a row or a verse lost is another fault than one syllable).

    lyric-errors.py <repo> <out\\audiveris> [--prefix dev-] [--kind ms|scan] [--show N] [--only name]

Run it with stdin from NUL in a PowerShell.MCP console. A verse the truth numbers otherwise than its rows
stand under the staff shows as both missed and extra (kinkel10-4's "Vers 4").
"""
import argparse, os, re, sys, unicodedata
import xml.etree.ElementTree as ET
from collections import Counter, defaultdict


def truth_verses(path):
    verses = defaultdict(list)
    for part in ET.parse(path).getroot().findall('part'):
        for note in part.iter('note'):
            for ly in note.findall('lyric'):
                t = ''.join(x.text or '' for x in ly.findall('text')).strip()
                t = re.sub(r'^\d+[.)]\s*', '', t)
                if t:
                    verses[ly.get('number', '1')].append(t)
    return verses


def read_tracks(path):
    tracks = defaultdict(list)
    with open(path, encoding='utf-8') as f:
        for line in f:
            m = re.match(r'\s*lyrics\s+(\S+?)words(\d+)\s+sings\s+\S+\s*\{(.*)\}\s*$', line)
            if not m:
                continue
            for tok in re.findall(r'"[^"]*"-?|[^\s|]+', m.group(3)):
                if tok in ('~', '_', '__'):
                    continue
                tracks[int(m.group(2))].append(tok.rstrip('-').strip('"'))
    return tracks


def fold(s):
    s = unicodedata.normalize('NFD', s)
    s = ''.join(c for c in s if unicodedata.category(c) != 'Mn')
    return re.sub(r'[^\w]', '', s).lower()


def dist(a, b):
    prev = list(range(len(b) + 1))
    for i, x in enumerate(a):
        cur = [i + 1]
        for j, y in enumerate(b):
            cur.append(min(prev[j + 1] + 1, cur[j] + 1, prev[j] + (x != y)))
        prev = cur
    return prev[-1]


def near(a, b):
    """0 same, 1 wholly different, on the folded letters"""
    a, b = fold(a), fold(b)
    if not a and not b:
        return 0.0
    return dist(a, b) / max(len(a), len(b), 1)


def align(T, R):
    """the cheapest reading of R as T: ops (kind, truths, reads)"""
    INF = 1e9
    n, m = len(T), len(R)
    cost = [[INF] * (m + 1) for _ in range(n + 1)]
    back = [[None] * (m + 1) for _ in range(n + 1)]
    cost[0][0] = 0
    for i in range(n + 1):
        for j in range(m + 1):
            c = cost[i][j]
            if c >= INF:
                continue
            def go(ni, nj, add, op):
                if ni <= n and nj <= m and c + add < cost[ni][nj]:
                    cost[ni][nj] = c + add
                    back[ni][nj] = (i, j, op)
            if i < n:
                go(i + 1, j, 1.0, 'miss')
            if j < m:
                go(i, j + 1, 1.0, 'extra')
            if i < n and j < m:
                go(i + 1, j + 1, 0.0 if T[i] == R[j] else min(0.05 + near(T[i], R[j]) * 1.5, 2.0), 'sub')
            for k in (2, 3):
                # a merge or split only where the joined letters read clearly better than any one part
                if i + k <= n and j < m:
                    d = near(''.join(T[i:i + k]), R[j])
                    if d <= 0.3 and d + 0.2 < min(near(x, R[j]) for x in T[i:i + k]):
                        go(i + k, j + 1, 0.2 + d, 'merge')
                if i < n and j + k <= m:
                    d = near(T[i], ''.join(R[j:j + k]))
                    if d <= 0.3 and d + 0.2 < min(near(T[i], x) for x in R[j:j + k]):
                        go(i + 1, j + k, 0.2 + d, 'split')
    ops = []
    i, j = n, m
    while i or j:
        pi, pj, op = back[i][j]
        ops.append((op, T[pi:i], R[pj:j], pi))
        i, j = pi, pj
    return ops[::-1]


def strip_marks(s):
    s = unicodedata.normalize('NFD', s)
    return ''.join(c for c in s if unicodedata.category(c) != 'Mn')


def kind_of(t, r):
    if '?' in r and not fold(r):
        return 'unread ?'
    if t == r:
        return 'ok'
    if fold(t) == fold(r):
        if re.sub(r'[^\w]', '', t) == re.sub(r'[^\w]', '', r):
            return 'punctuation only'
        if re.sub(r'[^\w]', '', t).lower() == re.sub(r'[^\w]', '', r).lower():
            return 'case only'
        return 'umlaut/accent only'
    d = near(t, r)
    if d <= 0.34:
        return 'letters: close'
    if d <= 0.67:
        return 'letters: half'
    return 'letters: far'


ap = argparse.ArgumentParser()
ap.add_argument('repo')
ap.add_argument('out')
ap.add_argument('--prefix', default='dev-')
ap.add_argument('--kind', default=None)
ap.add_argument('--show', type=int, default=12)
ap.add_argument('--only', default=None)
ap.add_argument('--dump', action='store_true', help='print every piece side by side')
args = ap.parse_args()
sys.stdout.reconfigure(encoding='utf-8')
where = os.path.join(args.repo, 'data', 'dev' if args.prefix == 'dev-' else 'eval', 'lieder')

for kind in ('ms', 'scan'):
    if args.kind and kind != args.kind:
        continue
    cnt = Counter()
    ex = defaultdict(list)
    per = defaultdict(Counter)
    total = 0
    for name in sorted(os.listdir(args.out)):
        m = re.match(re.escape(args.prefix) + r'(.+)-(ms|scan)$', name)
        if not m or m.group(2) != kind or (args.only and args.only not in name):
            continue
        xml = os.path.join(where, m.group(1), 'ms', 'truth.musicxml')
        lys = next((p for p in (os.path.join(args.out, name, s, 'omr.lys') for s in ('own', 'scored-own')) if os.path.exists(p)), None)
        if not lys or not os.path.exists(xml):
            continue
        truth = truth_verses(xml)
        read = read_tracks(lys)
        keys = sorted(truth, key=lambda k: int(k) if k.isdigit() else 0)
        for vi, k in enumerate(keys):
            T, R = truth[k], read.get(vi + 1, [])
            total += len(T)
            ops = align(T, R)
            # runs of misses and extras: a whole row or bar lost is another fault than one syllable
            idx = 0
            while idx < len(ops):
                op, ts, rs, pos = ops[idx]
                if op in ('miss', 'extra'):
                    e = idx
                    while e < len(ops) and ops[e][0] == op:
                        e += 1
                    n = e - idx
                    label = f'{op} {"1" if n == 1 else "2-3" if n <= 3 else "4+"}'
                    words = [ops[x][1][0] if op == 'miss' else ops[x][2][0] for x in range(idx, e)]
                    cnt[label] += n
                    per[name][label] += n
                    ex[label].append(f'{name[4:]} v{k}@{pos}: {" ".join(words[:10])}')
                    idx = e
                    continue
                if op == 'sub':
                    c = kind_of(ts[0], rs[0])
                    if c != 'ok':
                        cnt[c] += 1
                        per[name][c] += 1
                        ex[c].append(f'{ts[0]} -> {rs[0]}')
                else:
                    c = op + (' close' if near(''.join(ts), ''.join(rs)) <= 0.34 else ' far')
                    cnt[c] += len(ts)
                    per[name][c] += len(ts)
                    ex[c].append(f'{"|".join(ts)} -> {"|".join(rs)}')
                idx += 1
    print(f'== {kind}: {total} syllables')
    for c, v in cnt.most_common():
        print(f'{v:5} {100 * v / max(1, total):5.1f}%  {c:22} ' + ' ; '.join(ex[c][:args.show]))
    print()
    for name, p in sorted(per.items(), key=lambda x: -sum(x[1].values()))[:8]:
        print(f'  {name:26} ' + ', '.join(f'{c} {v}' for c, v in p.most_common(6)))
    print()
