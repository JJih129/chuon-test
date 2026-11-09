"""Procedural textures for Tutorial / Lobby / BootScene polish.
Art direction: corporate-military (black/graphite corporate surfaces with thin red accents;
gunmetal / olive-grey military steel with amber hazard striping and stencil markings).
Outputs into Assets/Art/ChuonPolish/<Scene>/Textures. Run: python bake_scene_polish.py"""
import os
import numpy as np
from PIL import Image, ImageDraw, ImageFont, ImageFilter

ROOT = r"C:/Users/ikh33/source/chuon-test/Assets/Art/ChuonPolish"
FONT = r"C:/Windows/Fonts/bahnschrift.ttf"
RED = np.array([0.95, 0.07, 0.05])
AMBER = np.array([0.95, 0.62, 0.10])
WHITE = np.array([0.92, 0.92, 0.88])


def save(arr, scene, name, mode="RGB"):
    out = os.path.join(ROOT, scene, "Textures")
    os.makedirs(out, exist_ok=True)
    a = np.clip(arr, 0, 1)
    Image.fromarray((a * 255 + 0.5).astype(np.uint8), mode).save(os.path.join(out, name))


def vnoise(h, w, cy, cx, seed):
    g = np.random.default_rng(seed).random((cy + 1, cx + 1))
    g[-1, :] = g[0, :]; g[:, -1] = g[:, 0]
    y = np.linspace(0, cy, h, endpoint=False); x = np.linspace(0, cx, w, endpoint=False)
    iy = y.astype(int); ix = x.astype(int)
    fy = (y - iy); fx = (x - ix); fy = fy * fy * (3 - 2 * fy); fx = fx * fx * (3 - 2 * fx)
    a = g[iy][:, ix]; b = g[iy][:, ix + 1]; c = g[iy + 1][:, ix]; d = g[iy + 1][:, ix + 1]
    fx = fx[None, :]; fy = fy[:, None]
    return (a * (1 - fx) + b * fx) * (1 - fy) + (c * (1 - fx) + d * fx) * fy


def fbm(h, w, seed, base=4, oct=6):
    s = 0; t = 0
    for k in range(oct):
        a = 0.55 ** k
        s = s + vnoise(h, w, base * 2 ** k, base * 2 ** k * w // h, seed + k) * a; t += a
    return s / t


def line_mask(coord, pitch, width, soft, off=0.0):
    d = np.abs(((coord - off + pitch / 2) % pitch) - pitch / 2)
    return np.clip(1 - (d - width) / soft, 0, 1)


def normal_from_height(h, strength):
    gx = (np.roll(h, -1, 1) - np.roll(h, 1, 1)) * strength
    gy = (np.roll(h, -1, 0) - np.roll(h, 1, 0)) * strength
    l = np.sqrt(gx * gx + gy * gy + 1)
    return np.stack([(-gx / l) * 0.5 + 0.5, (gy / l) * 0.5 + 0.5, 1 / l * 0.5 + 0.5], -1)


def text_mask(h, w, items):
    """items: (text, x_px, y_px, size_px). Returns float mask (h,w)."""
    im = Image.new("L", (w, h), 0); d = ImageDraw.Draw(im)
    for txt, x, y, s in items:
        d.text((x, y), txt, fill=255, font=ImageFont.truetype(FONT, s))
    return np.array(im) / 255.0


def mask_rgba(metal, ao, smooth):
    return np.stack([metal, ao, np.zeros_like(ao), smooth], -1)


# ---------------------------------------------------------------- Tutorial: military test range
def tutorial():
    # floor: 1024px = 4 tile-UV units (~4.4 m). Concrete slabs with steel seams + worn white paint grid.
    n = 1024
    y, x = np.mgrid[0:n, 0:n] / n * 4.0
    nz = fbm(n, n, 3); grime = fbm(n, n, 9, base=3); fine = fbm(n, n, 17, base=32, oct=3)
    conc = 0.25 + 0.08 * nz + 0.03 * fine
    alb = np.stack([conc * 1.0, conc * 1.0, conc * 0.97], -1)
    alb *= (1 - 0.35 * np.clip(grime - 0.45, 0, 1) * 2)[..., None]
    slab = np.maximum(line_mask(x, 2.0, 0.006, 0.006), line_mask(y, 2.0, 0.006, 0.006))
    alb *= (1 - slab * 0.65)[..., None]
    wear = np.clip((fbm(n, n, 41, base=8) - 0.35) * 3, 0, 1)
    paint = np.maximum(line_mask(x, 4.0, 0.018, 0.004), line_mask(y, 4.0, 0.018, 0.004)) * wear
    alb = alb * (1 - paint[..., None]) + (WHITE * 0.62)[None, None] * paint[..., None]
    # small amber position markers at slab crossings
    cross = line_mask(x, 2.0, 0.0, 0.002) * line_mask(y, 2.0, 0.07, 0.003)
    cross = np.maximum(cross, line_mask(y, 2.0, 0.0, 0.002) * line_mask(x, 2.0, 0.07, 0.003))
    cross *= (1 - line_mask(x, 4.0, 0.08, 0.001) * line_mask(y, 4.0, 0.08, 0.001))
    cross = np.clip(cross * 6, 0, 1) * wear
    alb = alb * (1 - cross[..., None]) + (AMBER * 0.7)[None, None] * cross[..., None]
    h = 1 - slab + fine * 0.15 + paint * 0.05
    save(alb, "Tutorial", "T_Range_Floor_Albedo.png")
    save(normal_from_height(h, 2.0), "Tutorial", "T_Range_Floor_Normal.png")
    smooth = 0.18 + 0.22 * np.clip(grime, 0, 1) + paint * 0.15
    save(mask_rgba(np.zeros_like(h), 1 - slab * 0.5, smooth), "Tutorial", "T_Range_Floor_Mask.png", "RGBA")

    # wall: 1024 x 1024 covering 12.5 m (U) x 10 m (V). Olive-grey steel plates, hazard band, stencils.
    W, H = 1024, 1024
    yy, xx = np.mgrid[0:H, 0:W]
    u = xx / W * 12.5; v = (H - 1 - yy) / H * 10.0
    nz = fbm(H, W, 51); grime = fbm(H, W, 57, base=3); fine = fbm(H, W, 63, base=40, oct=3)
    base = np.array([0.20, 0.21, 0.18])
    plate_id = (np.floor(u / 2.5) * 7 + np.floor(v / 2.5) * 3) % 5
    alb = base[None, None] * (0.85 + 0.12 * (plate_id / 4.0)[..., None]) * (0.85 + 0.3 * nz[..., None]) * (0.95 + 0.1 * fine[..., None])
    seams = np.maximum(line_mask(u, 2.5, 0.012, 0.01), line_mask(v, 2.5, 0.012, 0.01))
    bx = np.abs(((u + 1.25) % 2.5) - 1.25); by = np.abs(((v + 1.25) % 2.5) - 1.25)
    # bolt rows just inside every plate edge
    bdist = np.minimum(np.abs(bx - 1.13), np.abs(by - 1.13))
    along = np.where(np.abs(bx - 1.13) < np.abs(by - 1.13), by, bx)
    bolts = (bdist < 0.03) * (np.abs(((along * 3.0) % 1.0) - 0.5) < 0.07) * 1.0
    # dirt streaks running down from seams
    streak = np.clip((vnoise(H, W, 2, 160, 71) - 0.55) * 3, 0, 1) * np.clip(1 - (((v % 2.5)) / 1.2), 0, 1)
    alb *= (1 - 0.35 * streak)[..., None]
    alb *= (1 - 0.3 * np.clip(grime - 0.5, 0, 1) * 2 * np.clip(1.5 - v / 3, 0, 1))[..., None]
    alb *= (1 - seams * 0.6)[..., None]
    alb += bolts[..., None] * 0.06
    # hazard band 0.25..1.05 m
    band = (v > 0.25) & (v < 1.05)
    stripe = (((u + v) / 0.5) % 2.0) < 1.0
    wear = np.clip((fbm(H, W, 81, base=10) - 0.3) * 3, 0, 1)
    haz = np.where(stripe, 1.0, 0.0)
    hz_col = np.where(haz[..., None] > 0.5, AMBER[None, None] * 0.75, np.array([0.04, 0.04, 0.04])[None, None])
    hm = (band * wear)[..., None]
    alb = alb * (1 - hm) + hz_col * hm
    # thin white rule above band, stencils
    rule = ((v > 1.15) & (v < 1.19)) * wear
    st = text_mask(H, W, [("TEST RANGE  B-02", 40, int(H - 3.0 / 10 * H), 64), ("KEEP CLEAR OF FIRING LANE", 44, int(H - 2.2 / 10 * H), 30),
                          ("02", int(W * 0.66), int(H - 4.4 / 10 * H), 190), ("LOAD 4.5t", int(W * 0.40), int(H - 8.6 / 10 * H), 26)])
    st = np.clip(st * (0.35 + 0.65 * wear), 0, 1)
    stc = np.where((xx > W * 0.6)[..., None], AMBER[None, None] * 0.8, WHITE[None, None] * 0.7)
    alb = alb * (1 - rule[..., None]) + WHITE[None, None] * 0.65 * rule[..., None]
    alb = alb * (1 - st[..., None]) + stc * st[..., None]
    h = 1 - seams + bolts * 0.6 + fine * 0.1
    save(alb, "Tutorial", "T_Range_Wall_Albedo.png")
    save(normal_from_height(h, 2.5), "Tutorial", "T_Range_Wall_Normal.png")
    smooth = 0.25 + 0.2 * nz - 0.15 * streak + 0.1 * band
    save(mask_rgba(np.full_like(h, 0.35), 1 - seams * 0.5, smooth), "Tutorial", "T_Range_Wall_Mask.png", "RGBA")

    # hazard floor tape (tiles along U, 1 m = 512 px; strip is 0.4 m tall)
    w, h = 512, 256
    yy, xx = np.mgrid[0:h, 0:w]
    u = xx / w; v = yy / h
    stripe = (((u * 2.5 + v * 1.0) % 1.0) < 0.5)
    wear = np.clip((fbm(h, w, 91, base=6) - 0.12) * 4, 0, 1)
    col = np.where(stripe[..., None], AMBER[None, None] * 0.8, np.array([0.03, 0.03, 0.03])[None, None])
    edge = (v < 0.06) | (v > 0.94)
    a = np.clip(wear * 0.95, 0, 1) * (1 - edge * 0.0)
    save(np.concatenate([col, a[..., None]], -1), "Tutorial", "T_Range_HazardTape.png", "RGBA")


# ---------------------------------------------------------------- Lobby: corporate black/graphite panels (1 tex = 4 m x 4 m)
def lobby():
    n = 1024
    y, x = np.mgrid[0:n, 0:n] / n * 4.0
    nz = fbm(n, n, 21); fine = fbm(n, n, 27, base=48, oct=3); smudge = fbm(n, n, 33, base=5)
    px = np.floor(x / 2.0).astype(int); py = np.floor(y / 1.0).astype(int)
    pt = np.random.default_rng(5).random((8, 8))[py % 8, px % 8]
    base = 0.085 + 0.045 * pt + 0.015 * nz
    alb = np.stack([base, base * 1.01, base * 1.05], -1)
    seam = np.maximum(line_mask(x, 2.0, 0.004, 0.004), line_mask(y, 1.0, 0.004, 0.004))
    alb *= (1 - seam * 0.7)[..., None]
    # brushed metal kick band texture variation inside panels
    brushed = vnoise(n, n, 400, 4, 44) * 0.02
    alb += brushed[..., None] * (pt > 0.7)[..., None]
    # emission: rare thin red line in one horizontal seam per tile + tiny white status dots
    lx = x % 4.0
    red = line_mask(y, 4.0, 0.0025, 0.003, off=1.0 + 0.012) * ((lx > 0.3) & (lx < 3.7))
    dots = np.clip(1 - (np.hypot((x % 2.0) - 1.85, (y % 1.0) - 0.88) - 0.006) / 0.004, 0, 1) * ((np.floor(x / 2) + np.floor(y)) % 3 == 0)
    emi = WHITE[None, None] * dots[..., None] * 0.6  # red accents come from geometry strips only
    h = 1 - seam + fine * 0.04
    smooth = 0.55 + 0.25 * pt - 0.2 * np.clip(smudge - 0.55, 0, 1) * 2 - seam * 0.3
    metal = 0.15 + 0.35 * (pt > 0.7)
    save(alb, "Lobby", "T_Corp_Panel_Albedo.png")
    save(emi, "Lobby", "T_Corp_Panel_Emission.png")
    save(normal_from_height(h, 2.0), "Lobby", "T_Corp_Panel_Normal.png")
    save(mask_rgba(np.full_like(h, 1.0) * metal, 1 - seam * 0.5, smooth), "Lobby", "T_Corp_Panel_Mask.png", "RGBA")


# ---------------------------------------------------------------- Boot UI (corporate red / white)
def boot():
    w = 512
    t = np.linspace(0, 1, w)[None, :, None]
    dark_red = np.array([0.45, 0.02, 0.02])
    g = dark_red[None, None] * (1 - t) + RED[None, None] * t
    g[:, -6:] = 1.0  # bright white leading edge
    hl = np.ones((16, 1, 1)); hl[:2] = 1.3; hl[-2:] = 0.7
    save(np.clip(g * hl, 0, 1), "BootScene", "T_Boot_BarGradient.png")
    n = 512
    yy, xx = np.mgrid[0:n, 0:n] / (n - 1) * 2 - 1
    r = np.sqrt(xx ** 2 + yy ** 2) / np.sqrt(2)
    a = np.clip((r - 0.35) / 0.65, 0, 1) ** 1.6 * 0.85
    save(np.stack([np.zeros_like(a)] * 3 + [a], -1), "BootScene", "T_Boot_Vignette.png", "RGBA")
    s = np.zeros((4, 4, 4)); s[2:, :, 3] = 0.18
    save(s, "BootScene", "T_Boot_Scanline.png", "RGBA")


def sky():
    # neutral overcast night panorama (equirect 2048x1024): graphite zenith, lighter haze at horizon,
    # faint warm/cold city glow + a few distant red aviation lights. Used by Tutorial / Lobby / MainScene.
    W, H = 2048, 1024
    yy, xx = np.mgrid[0:H, 0:W]
    el = 0.5 - yy / H  # +0.5 zenith .. -0.5 nadir
    cloud = fbm(H, W, 101, base=3)
    hz = np.exp(-np.abs(el) * 16)
    base = 0.035 + 0.05 * np.clip(el, 0, 1) * 0 + 0.10 * hz + 0.03 * cloud * np.clip(el * 4, 0, 1)
    col = np.stack([base * 1.0, base * 1.0, base * 1.04], -1)
    glow = np.clip(vnoise(H, W, 1, 24, 107) - 0.4, 0, 1) * hz * 0.18
    col += glow[..., None] * np.array([1.0, 0.85, 0.7])[None, None]
    col[el < -0.02] = col[el < -0.02] * 0.5
    rng = np.random.default_rng(3)
    for _ in range(18):
        x0 = rng.integers(0, W); y0 = int(H * (0.5 - rng.uniform(0.005, 0.05)))
        rr = (xx - x0) ** 2 + (yy - y0) ** 2
        col += np.exp(-rr / 3.0)[..., None] * RED[None, None] * 0.9
    save(col, "Shared", "T_Neutral_SkyPano.png")


if __name__ == "__main__":
    tutorial(); lobby(); boot(); sky()
    print("done")
