"""TSUKASA mob trim sheet (Blender 5.x headless, numpy only -- no scene needed).

Run:
  blender -b --factory-startup --python mob_trim_atlas.py -- [out_dir]
Writes <out_dir>/Textures/:
  T_Mob_Trim_Albedo.png  sRGB   graphite metal / off-white ceramic / TSUKASA red / rubber / glow / visor bands
  T_Mob_Trim_Normal.png  linear OpenGL (+Y) tangent space: panel seams, rivets, rubber ribs, brushed grain
  T_Mob_Trim_OSM.png     linear ASP StylizePBR mask (same as Type-D): R occlusion (seams), G smoothness, B metallic
Bands (v, bottom->top) and u tiling are defined in mob_common.BANDS; every band tiles horizontally (periodic noise,
seams at integer fractions of u). Deterministic (fixed seed).
"""
import os, sys
import numpy as np
import bpy

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from mob_common import BANDS, parse_args  # noqa: E402

N = 1024
OUT_DIR, _ = parse_args()
TEX_DIR = os.path.join(OUT_DIR, "Textures")
os.makedirs(TEX_DIR, exist_ok=True)
rng = np.random.default_rng(1207)


def periodic_noise(scale_px, seed_shift=0, aniso=(1.0, 1.0)):
    """Tileable gaussian-filtered noise in [-1, 1] (filtered in the Fourier domain -> periodic)."""
    w = rng.standard_normal((N, N))
    f = np.fft.fft2(w)
    ky = np.fft.fftfreq(N)[:, None] * aniso[1]
    kx = np.fft.fftfreq(N)[None, :] * aniso[0]
    g = np.exp(-((kx ** 2 + ky ** 2) * (scale_px ** 2) * 2 * np.pi ** 2))
    r = np.real(np.fft.ifft2(f * g))
    r -= r.mean()
    return r / (np.abs(r).max() + 1e-6)


u = (np.arange(N) + 0.5)[None, :] / N * np.ones((N, 1))
v = (np.arange(N) + 0.5)[:, None] / N * np.ones((1, N))
albedo = np.zeros((N, N, 3), np.float32)
height = np.zeros((N, N), np.float32)
occl = np.ones((N, N), np.float32)
smooth = np.zeros((N, N), np.float32)
metal = np.zeros((N, N), np.float32)

fine = periodic_noise(1.2)
mid = periodic_noise(6.0)
large = periodic_noise(40.0)
brushed = periodic_noise(0.8, aniso=(0.04, 1.0))  # streaks along u


def band_mask(name):
    b0, b1 = BANDS[name]
    return (v >= b0) & (v < b1), b0, b1


def groove(dist_px, width_px):
    """0..1 groove profile (1 at centre) for seam lines."""
    return np.clip(1.0 - np.abs(dist_px) / width_px, 0.0, 1.0) ** 1.5


def seams_u(count, width_px):
    t = u * count
    d = (t - np.round(t)) / count * N
    return groove(d, width_px)


def seam_v(vc, width_px):
    return groove((v - vc) * N, width_px)


def rivets(cols, row_v, radius_px):
    t = u * cols
    du = (t - np.floor(t) - 0.5) / cols * N
    dv = (v - row_v) * N
    r = np.sqrt(du ** 2 + dv ** 2)
    return np.clip(1.0 - r / radius_px, 0, 1) ** 0.6


# ---- graphite metal (Arasaka graphite, brushed, panel seams + rivet rows)
m, b0, b1 = band_mask("Metal")
seam = np.maximum(seams_u(4, 2.2), seam_v((b0 + b1) * 0.5, 2.0))
riv = np.maximum(rivets(16, b0 + (b1 - b0) * 0.12, 3.2), rivets(16, b1 - (b1 - b0) * 0.12, 3.2))
base = np.array([0.190, 0.198, 0.212]) * (1.0 + 0.10 * brushed[..., None] + 0.08 * large[..., None])
albedo[m] = (base * (1.0 - 0.55 * seam[..., None]) + 0.08 * riv[..., None])[m]
height[m] = (-1.0 * seam + 0.6 * riv + 0.04 * brushed)[m]
occl[m] = (1.0 - 0.6 * seam - 0.05 * (mid * 0.5 + 0.5))[m]
smooth[m] = (0.50 + 0.07 * brushed - 0.22 * seam)[m]
metal[m] = 0.82

# ---- ceramic shell (off-white, faint speckle, wide panels)
m, b0, b1 = band_mask("Ceramic")
seam = np.maximum(seams_u(3, 2.0), seam_v(b0 + (b1 - b0) * 0.35, 1.8))
speck = (fine > 0.86).astype(np.float32) * 0.5
base = np.array([0.64, 0.645, 0.63]) * (1.0 + 0.025 * mid[..., None] + 0.02 * large[..., None]) - 0.08 * speck[..., None]
albedo[m] = (base * (1.0 - 0.5 * seam[..., None]))[m]
height[m] = (-1.0 * seam + 0.02 * mid)[m]
occl[m] = (1.0 - 0.55 * seam)[m]
smooth[m] = (0.74 + 0.04 * mid - 0.3 * seam)[m]
metal[m] = 0.0

# ---- TSUKASA red paint (with chipped edges showing graphite)
m, b0, b1 = band_mask("Accent")
chips = np.clip((large * 0.6 + fine * 0.5) - 0.55, 0, 1) * 4
chips = np.clip(chips, 0, 1)
edge = np.maximum(groove((v - b0) * N, 6.0), groove((v - b1) * N, 6.0))
chip = np.clip(chips + edge * 0.6 * (fine * 0.5 + 0.5), 0, 1)
red = np.array([0.56, 0.07, 0.08]) * (1.0 + 0.04 * mid[..., None])
gm = np.array([0.20, 0.205, 0.215])
albedo[m] = (red * (1 - chip[..., None]) + gm * chip[..., None])[m]
height[m] = (-0.25 * chip)[m]
occl[m] = (1.0 - 0.15 * chip)[m]
smooth[m] = (0.55 * (1 - chip) + 0.45 * chip)[m]
metal[m] = (0.1 * (1 - chip) + 0.82 * chip)[m]

# ---- rubber / gaskets (ribbed)
m, b0, b1 = band_mask("Rubber")
ribs = 0.5 + 0.5 * np.sin(u * N / 6.0 * 2 * np.pi)
albedo[m] = (np.array([0.075, 0.075, 0.08]) * (1.0 + 0.15 * mid[..., None]))[m]
height[m] = (0.5 * ribs)[m]
occl[m] = (0.85 + 0.15 * ribs)[m]
smooth[m] = (0.22 + 0.05 * mid)[m]
metal[m] = 0.0

# ---- glow (emission handled by the Glow material) and visor glass
m, _, _ = band_mask("Glow")
albedo[m] = np.array([1.0, 0.3, 0.3])
smooth[m] = 0.8
m, _, _ = band_mask("Visor")
albedo[m] = np.array([0.06, 0.065, 0.075])
smooth[m] = 0.93
metal[m] = 0.35

# ---- normal from height (periodic gradient)
strength = 2.6
dx = (np.roll(height, -1, axis=1) - np.roll(height, 1, axis=1)) * 0.5
dy = (np.roll(height, -1, axis=0) - np.roll(height, 1, axis=0)) * 0.5
nx, ny, nz = -dx * strength, -dy * strength, np.ones_like(height)
ln = np.sqrt(nx ** 2 + ny ** 2 + nz ** 2)
normal = np.stack([nx / ln, ny / ln, nz / ln], -1) * 0.5 + 0.5


def save_png(name, rgb, cs):
    im = bpy.data.images.new(name, N, N, alpha=False, float_buffer=False)
    im.colorspace_settings.name = cs
    px = np.ones((N, N, 4), np.float32)
    px[..., :3] = np.clip(rgb, 0, 1)
    im.pixels.foreach_set(px.ravel())
    im.filepath_raw = os.path.join(TEX_DIR, name)
    im.file_format = 'PNG'
    im.save()
    print("wrote", im.filepath_raw)


save_png("T_Mob_Trim_Albedo.png", albedo, 'sRGB')
save_png("T_Mob_Trim_Normal.png", normal, 'Non-Color')
save_png("T_Mob_Trim_OSM.png", np.stack([np.clip(occl, 0, 1), np.clip(smooth, 0, 1), np.clip(metal, 0, 1)], -1), 'Non-Color')
