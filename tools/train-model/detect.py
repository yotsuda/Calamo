"""Run the detector on a page and compare what it finds with what the page holds, and draw the
detections on the page.

  python detect.py <model.pt> <page.png> <boxes.tsv> [<overlay.png>] [--space <px>]

Only the staff space is taken from boxes.tsv (the median of the reader's staves; --space overrides
it, for a page whose staves the reader did not find): the page is read in bands wherever the
staves are. The glyphs in boxes.tsv are the truth: matched by position on a generated page, by
count on a scan (whose truth boxes are not aligned to it; the "matched" line then means little).
"""
import sys
import numpy as np
import torch
from PIL import Image, ImageDraw
from dataset import CLASSES, SPACE, read_boxes, staff_space, page_of
from train import Detector, detect_page, match, report

args = [a for a in sys.argv[1:] if not a.startswith('--')]
opts = {sys.argv[i][2:]: sys.argv[i + 1] for i in range(1, len(sys.argv) - 1) if sys.argv[i].startswith('--')}
model, png, boxes = args[:3]
overlay = args[3] if len(args) > 3 else None
dev = 'cuda' if torch.cuda.is_available() else 'cpu'
net = Detector().to(dev); net.load_state_dict(torch.load(model, map_location=dev)); net.eval()

space = float(opts['space']) if 'space' in opts else None
img, targets, scale = page_of(png, boxes, space)
found = detect_page(net, img, dev)
truth = np.bincount([c for c, *_ in targets], minlength=len(CLASSES))
count = np.bincount([c for c, *_ in found], minlength=len(CLASSES))
if overlay:
    page = Image.open(png).convert('RGB')
    draw = ImageDraw.Draw(page)
    r = 0.4 * SPACE / scale
    for c, fx, fy, score in found:
        px, py = fx / scale, fy / scale
        draw.ellipse((px - r, py - r, px + r, py + r), outline=(255, 0, 0), width=2)
        draw.text((px + r, py - r), CLASSES[c][:6], fill=(0, 0, 255))
    page.save(overlay)
print('staff space %.1f px, page %dx%d at %d px a space' % (SPACE / scale, img.shape[1], img.shape[0], SPACE))
print('%-10s %6s %6s' % ('class', 'truth', 'found'))
for c in range(1, len(CLASSES)):
    if truth[c] or count[c]:
        print('%-10s %6d %6d' % (CLASSES[c], truth[c], count[c]))
print('%-10s %6d %6d' % ('all', truth[1:].sum(), count[1:].sum()))
print('matched by position (a generated page):')
report(*match(found, targets))
