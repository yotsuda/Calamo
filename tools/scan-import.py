"""Bring a scanned page into the form the OMR reads: <piece>/scan/pNN.png, grey, upright.

  tools/train-model/.venv/Scripts/python tools/scan-import.py <scan.pdf|scan.jpg|scan.png> [<more> ...]

A PDF gives its embedded images (one per page) at their native resolution. A page scanned
sideways is turned so that its staff lines run level: whichever of the two orientations gives
the sharper row profile (the staff lines each falling into one row) is the upright one.
"""
import sys, os
import numpy as np
from PIL import Image


def sharpness(dark):
    rows = dark.sum(axis=1).astype(np.float64)
    return float((rows * rows).sum()) / max(1.0, float(dark.sum()))


def left_heavy(dark):
    """Ink in the leftmost tenth of the music's width against the rightmost: the clefs and key
    signatures stand at the left of every staff, the last barline alone at the right."""
    cols = dark.sum(axis=0)
    xs = np.where(cols > 0)[0]
    if len(xs) == 0:
        return 0.0
    x0, x1 = xs.min(), xs.max()
    tenth = max(1, (x1 - x0) // 10)
    return float(cols[x0:x0 + tenth].sum()) - float(cols[x1 - tenth:x1 + 1].sum())


def upright(im):
    """Of the four turns, the two with level staff lines (sharpest row profile), and of those the
    one with the clefs on the left."""
    turns = [None, Image.Transpose.ROTATE_90, Image.Transpose.ROTATE_180, Image.Transpose.ROTATE_270]
    cands = []
    for t in turns:
        img = im if t is None else im.transpose(t)
        dark = np.array(img.convert('L')) < 128
        cands.append((sharpness(dark), left_heavy(dark), img))
    level = sorted(cands, key=lambda c: -c[0])[:2]
    return max(level, key=lambda c: c[1])[2]


def pages(path):
    if path.lower().endswith('.pdf'):
        import pymupdf
        doc = pymupdf.open(path)
        for page in doc:
            for info in page.get_images(full=True):
                pix = pymupdf.Pixmap(doc, info[0])
                if pix.n > 3:
                    pix = pymupdf.Pixmap(pymupdf.csRGB, pix)
                mode = 'RGB' if pix.n == 3 else 'L'
                yield Image.frombytes(mode, (pix.width, pix.height), pix.samples)
    else:
        yield Image.open(path)


for path in sys.argv[1:]:
    out_dir = os.path.join(os.path.dirname(path), 'scan')
    os.makedirs(out_dir, exist_ok=True)
    for n, im in enumerate(pages(path), 1):
        im = upright(im).convert('L')
        out = os.path.join(out_dir, 'p%02d.png' % n)
        im.save(out)
        print(out, im.size)
