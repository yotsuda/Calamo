import sys
from PIL import Image
# zoom.py <image> <out> <x0> <y0> <x1> <y1> <zoom>: one region of a page, enlarged
im = Image.open(sys.argv[1]); x0, y0, x1, y1 = (int(v) for v in sys.argv[3:7]); z = float(sys.argv[7])
c = im.crop((x0, y0, x1, y1)); c = c.resize((int(c.width * z), int(c.height * z)), Image.LANCZOS); c.save(sys.argv[2]); print(sys.argv[2], c.size)
