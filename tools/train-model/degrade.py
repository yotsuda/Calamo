"""Strong degradation of a training window, drawn afresh every time a window is cut (the comparison with
other OMRs, 2026-10-26, 4.1 and 4.7: homr's strength on old scans comes from pages dirtied anew at every
pass, augraphy-style). Geometry is untouched, so the targets stay where they are.

Three kinds of page, as the old prints of the development set show them (data/dev/lieder/*/scan):
  - a print scanned to black and white, mostly an old one: ink that bled or wore thin, thin strokes (staff
    lines, stems, ledger lines) broken where the ink faded, the page cut at one grey level, dust and blots;
  - a grey scan: a show-through of the other side, a shadow or uneven light, paper grain, streaks, JPEG;
  - a small page blown up (about 96 dpi).
Only numpy and PIL: nothing to install, nothing that ships.

  degrade(g, rng, other)  g: float32 HxW, 1 = paper, 0 = ink; other: another window (for show-through)
"""
import io
import numpy as np
from PIL import Image, ImageFilter


def field(h, w, rng, period):
    """A smooth random field in [0, 1] whose blobs are about 'period' pixels across."""
    gh, gw = max(2, int(h / period) + 2), max(2, int(w / period) + 2)
    small = Image.fromarray((rng.random((gh, gw)) * 255).astype(np.uint8))
    return np.asarray(small.resize((w, h), Image.Resampling.BICUBIC), dtype=np.float32) / 255.0


def blur(g, sigma):
    im = Image.fromarray(np.clip(g * 255, 0, 255).astype(np.uint8))
    return np.asarray(im.filter(ImageFilter.GaussianBlur(sigma)), dtype=np.float32) / 255.0


def specks(g, rng, density, dark):
    """Dust (dark) or pinholes (light): dots one to three pixels across."""
    h, w = g.shape
    n = rng.poisson(density * h * w)
    for _ in range(n):
        y, x, r = rng.integers(h), rng.integers(w), rng.integers(0, 2)
        g[max(0, y - r):y + r + 1, max(0, x - r):x + r + 1] = rng.uniform(0, 0.3) if dark else 1.0
    return g


def resize(g, w, h, how):
    im = Image.fromarray(np.clip(g * 255, 0, 255).astype(np.uint8))
    return np.asarray(im.resize((w, h), how), dtype=np.float32) / 255.0


def old_print(g, rng):
    """A print scanned to black and white: cut at one grey level at the scanner's resolution (1 to 2.5
    times the detector's), then scaled to the detector's, so the cut's edge is smooth again (a page cut at
    the detector's own scale has jagged strokes no scan has: the held-out set's black-and-white pages read
    worse for it). Mostly an old print, bold and worn; a third of the time a clean modern one."""
    h, w = g.shape
    s = rng.uniform(1.0, 2.5)
    H, W = int(round(h * s)), int(round(w * s))
    g = resize(g, W, H, Image.Resampling.BICUBIC)
    crisp = rng.random() < 0.35
    ink = 1.0 - g
    # worn ink: blotches where the ink faded, a space or a few across; after the blur and the cut a thin
    # stroke breaks there and a head only greys
    if not crisp and rng.random() < 0.7:
        f = field(H, W, rng, rng.uniform(8, 40) * s)
        q = rng.uniform(0.05, 0.25)
        ink *= np.clip((f - q) / 0.15, 0.0, 1.0) * 0.45 + 0.55
    g = 1.0 - ink
    # bled or thinned ink, then the cut at one grey level: a cut above the middle grey thickens every
    # stroke (the old prints are mostly bold), below it thins them (and a faint one breaks)
    if crisp:
        g = blur(g, rng.uniform(0.3, 0.6) * s)
        t = rng.uniform(0.42, 0.6)
    else:
        g = blur(g, rng.uniform(0.5, 1.1) * s)
        t = rng.uniform(0.46, 0.75)
    # (the light of the scan is never even: the cut moves a little over the page)
    t = t + (field(H, W, rng, 200 * s) - 0.5) * rng.uniform(0, 0.08)
    g = (g >= t).astype(np.float32)
    g = specks(g, rng, rng.uniform(0, 4e-4) / s, dark=True)
    g = specks(g, rng, rng.uniform(0, 2e-4) / s, dark=False)
    # a blot now and then: a few pixels of ink run together
    for _ in range(0 if crisp else rng.poisson(1.0)):
        y, x = rng.integers(H), rng.integers(W)
        r = rng.uniform(1.5, 4) * s
        yy, xx = np.ogrid[:H, :W]
        g[(yy - y) ** 2 + ((xx - x) * rng.uniform(0.5, 1.5)) ** 2 < r * r] = 0
    return resize(g, w, h, Image.Resampling.BOX)


def low_resolution(g, rng):
    """A small page (an IMSLP image of 96 dpi or so) the reader blows up: everything finer than a pixel of
    the small page is lost, and what is left is smeared over the pixels of the big one."""
    h, w = g.shape
    s = rng.uniform(0.35, 0.7)
    small = resize(g, max(1, int(round(w * s))), max(1, int(round(h * s))), Image.Resampling.BOX)
    small = np.clip(small + rng.normal(0, rng.uniform(0, 0.04), small.shape).astype(np.float32), 0, 1)
    if rng.random() < 0.4:
        # (and a JPEG of the small page)
        im = Image.fromarray(np.clip(small * 255, 0, 255).astype(np.uint8))
        buf = io.BytesIO(); im.save(buf, 'JPEG', quality=int(rng.integers(30, 80)))
        small = np.asarray(Image.open(buf), dtype=np.float32) / 255.0
    return resize(small, w, h, Image.Resampling.BICUBIC if rng.random() < 0.5 else Image.Resampling.BILINEAR)


def grey_scan(g, rng, other):
    """A grey scan of a worn page."""
    h, w = g.shape
    # ink that is not black and not even
    g = 1.0 - (1.0 - g) * (rng.uniform(0.55, 1.0) + rng.uniform(-0.2, 0.0) * field(h, w, rng, rng.uniform(10, 60)))
    if rng.random() < 0.5:
        g = blur(g, rng.uniform(0.4, 1.2))
    # show-through of the other side: the mirror of another window, soft and faint
    if other is not None and rng.random() < 0.4:
        back = blur(other[:, ::-1], rng.uniform(1.0, 2.5))
        g = g * (1.0 - (1.0 - back) * rng.uniform(0.08, 0.3))
    # paper that is not white, a shadow or uneven light (a gradient over the window, or a soft field)
    paper = rng.uniform(0.7, 1.0)
    if rng.random() < 0.5:
        ax = rng.uniform(-1, 1); ay = rng.uniform(-1, 1)
        yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)
        ramp = (ax * xx / w + ay * yy / h)
        ramp = (ramp - ramp.min()) / max(1e-6, ramp.max() - ramp.min())
        light = 1.0 - rng.uniform(0, 0.35) * ramp
    else:
        light = 1.0 - rng.uniform(0, 0.3) * field(h, w, rng, rng.uniform(60, 300))
    g = g * paper * light
    # streaks of a roller or a dirty glass, across or down
    for _ in range(rng.poisson(0.4)):
        if rng.random() < 0.6:
            y = rng.integers(h); th = rng.integers(1, 4)
            g[y:y + th, :] *= rng.uniform(0.6, 0.9)
        else:
            x = rng.integers(w); th = rng.integers(1, 4)
            g[:, x:x + th] *= rng.uniform(0.6, 0.9)
    # paper grain
    g = g + rng.normal(0, rng.uniform(0.01, 0.06), g.shape).astype(np.float32)
    g = specks(np.clip(g, 0, 1), rng, rng.uniform(0, 2e-4), dark=True)
    # JPEG
    if rng.random() < 0.4:
        im = Image.fromarray(np.clip(g * 255, 0, 255).astype(np.uint8))
        buf = io.BytesIO(); im.save(buf, 'JPEG', quality=int(rng.integers(15, 70)))
        g = np.asarray(Image.open(buf), dtype=np.float32) / 255.0
    return g


def degrade(g, rng, other=None):
    g = g.astype(np.float32)
    r = rng.random()
    g = old_print(g, rng) if r < 0.45 else grey_scan(g, rng, other) if r < 0.8 else low_resolution(g, rng)
    return np.clip(g, 0, 1)
