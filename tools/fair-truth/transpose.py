import re, sys
# transpose.py <in.lys> <out.lys> <steps> <semitones>: the notes of a .lys (absolute octaves) moved up by an
# interval -- a truth of the fair set for an edition printed in another key. The music lines only (not the
# words of a lyrics track, a string, an annotation); the `key` line is moved too.
src, dst, steps, semis = sys.argv[1], sys.argv[2], int(sys.argv[3]), int(sys.argv[4])
STEP = 'cdefgab'; SEMI = [0, 2, 4, 5, 7, 9, 11]
ALT = {'': 0, 'es': -1, 'is': 1, 'eses': -2, 'isis': 2}; NAME = {v: k for k, v in ALT.items()}
note = re.compile(r"(?<![A-Za-z_@.\"])([a-g])(eses|isis|es|is)?([,']*)(?=[\d~()>\s@]|$)")
keep = re.compile(r'"[^"]*"|@!?\w+(\([^)]*\))?(\.\w+)*|//.*$')
def move(m):
    d = STEP.index(m.group(1)); o = m.group(3).count("'") - m.group(3).count(',')
    dia = o * 7 + d + steps; semi = o * 12 + SEMI[d] + ALT[m.group(2) or ''] + semis
    o2, d2 = divmod(dia, 7)
    return STEP[d2] + NAME[semi - (o2 * 12 + SEMI[d2])] + ("'" * o2 if o2 > 0 else ',' * -o2)
out = []; music = False
for line in open(src, encoding='utf-8').read().split('\n'):
    s = line.strip()
    if s.startswith('key '): line = re.sub(r'key (\S+)', lambda m: 'key ' + note.sub(move, m.group(1) + ' ').strip(), line)
    if s.startswith('section '): music = True
    elif s.startswith('form '): music = False
    elif music and not s.startswith('lyrics '):
        parts = []; at = 0
        for k in keep.finditer(line):
            parts.append(note.sub(move, line[at:k.start()])); parts.append(k.group(0)); at = k.end()
        parts.append(note.sub(move, line[at:]))
        line = ''.join(parts)
    out.append(line)
open(dst, 'w', encoding='utf-8', newline='\n').write('\n'.join(out))
