# Moon relief for SpaceSky: NASA SVS CGI Moon Kit LOLA heights (ldem_16_uint.tif: 16 pixels per degree,
# 16-bit half-metres relative to 1727.4 km) -> a normal map in each point's own east / north / up frame (PNG, 8 bit).
# Usage: python moon_relief.py <ldem_16_uint.tif> <out.png> [exaggeration]
import sys
import numpy as np
from PIL import Image
Image.MAX_IMAGE_PIXELS = None
src, out = sys.argv[1], sys.argv[2]
k = float(sys.argv[3]) if len(sys.argv) > 3 else 1.5
h = np.asarray(Image.open(src)).astype(np.float64) * 0.5 - 10000.0     # metres, relative to 1737.4 km
H, W = h.shape
R = 1737400.0
lat = (0.5 - (np.arange(H) + 0.5) / H) * np.pi                          # row 0 = north
dx = R * np.maximum(np.cos(lat), 0.02) * (2 * np.pi / W)
dy = R * (np.pi / H)
gx = (np.roll(h, -1, axis=1) - np.roll(h, 1, axis=1)) / (2 * dx[:, None])
hn = np.vstack([h[:1], h, h[-1:]])
gy = (hn[:-2] - hn[2:]) / (2 * dy)                                       # d height / d north
n = np.dstack([-gx * k, -gy * k, np.ones_like(h)])
n /= np.linalg.norm(n, axis=2, keepdims=True)
img = np.clip((n * 0.5 + 0.5) * 255 + 0.5, 0, 255).astype(np.uint8)
Image.fromarray(img, 'RGB').save(out)
print(out, W, H, 'height range', h.min(), h.max(), 'max slope deg', np.degrees(np.arctan(np.hypot(gx, gy).max())))
