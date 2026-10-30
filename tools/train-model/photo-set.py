"""An evaluation set as a phone would see it: every page of a .npz through degrade.photo (12 px a space or
so, soft, grey, unevenly lit, sharpened, JPEG; 2026-10-31), the targets where they were (photo moves nothing).
The detector's own measure of the photo kind, which no rendered set has (the Eroica photo is one part).

  python photo-set.py <in.npz> <out.npz> [seed]
"""
import sys
import numpy as np
from degrade import photo

src, dst = sys.argv[1], sys.argv[2]
rng = np.random.default_rng(int(sys.argv[3]) if len(sys.argv) > 3 else 7)
d = np.load(src, allow_pickle=True)
images = [np.clip(photo(im.astype(np.float32) / 255.0, rng) * 255, 0, 255).astype(np.uint8) for im in d['images']]
out = {k: d[k] for k in d.files}
out['images'] = np.array(images, dtype=object)
np.savez_compressed(dst, **out)
print(f"{len(images)} pages -> {dst}")
