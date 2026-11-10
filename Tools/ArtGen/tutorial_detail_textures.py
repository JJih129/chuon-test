"""Infiltration tutorial DETAIL kit textures (iteration 16) -- numpy + PIL, deterministic.

  python Tools/ArtGen/tutorial_detail_textures.py [out_dir]

Writes Assets/Art/ChuonPolish/Tutorial/Detail/Textures/:
  T_INFD_Trim_{Albedo,Normal,Mask,Emission}.png  2048^2 shared trim sheet. Layout = TRIM / CELLS below and MUST match
                                                  tutorial_detail_blender.py (same tables). 256 px/m, strips wrap in U (8 m).
  T_INFD_Windows_{Albedo,Emission}.png           2048^2, 8x8 window interiors (WIN_ROWS; warm/cool office, blinds,
                                                  apartments, corridor, shops, dark). Not neon: dusk office light.
  T_INFD_Puddle_{Albedo,Normal,Mask}.png         1024^2, 2x2 puddle shapes, albedo alpha = clip mask.
Mask = URP packed ORM equivalent: R metallic, G occlusion, B detail mask (0), A smoothness
(assigned to both _MetallicGlossMap and _OcclusionMap, same convention as T_INF_*_Mask).
The impostor atlas (T_INFD_Impostor_*) is rendered by tutorial_detail_blender.py from the LOD0 buildings.
"""
import os, sys, math
import numpy as np
from PIL import Image, ImageDraw, ImageFont, ImageFilter

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
OUT = os.path.abspath(sys.argv[1]) if len(sys.argv) > 1 else os.path.join(ROOT, "Assets", "Art", "ChuonPolish", "Tutorial", "Detail", "Textures")
os.makedirs(OUT, exist_ok=True)
FONT_LAT = r"C:\Windows\Fonts\bahnschrift.ttf"
LOGO = os.path.join(ROOT, "Assets", "Art", "ChuonPolish", "Level_Facility", "Textures", "T_LF_Holo_TsukasaLogo.png")

N = 2048
# name, y0, y1 (pixel rows from the TOP of the image). 256 px = 1 m.
TRIM = [("ceramic", 0, 256), ("graph_rib", 256, 384), ("graphite", 384, 512), ("steel", 512, 640),
        ("concrete", 640, 768), ("louvre", 768, 832), ("red_light", 832, 896), ("white_light", 896, 960),
        ("amber_light", 960, 1024), ("rubber", 1024, 1088), ("hazard", 1088, 1152), ("dark_glass", 1152, 1216),
        ("cardboard", 1216, 1280), ("white_paint", 1280, 1344), ("red_paint", 1344, 1408), ("corrugated", 1408, 1536)]
CELLS = [("cell_vending", 0), ("cell_fan", 512), ("cell_emblem", 1024), ("cell_panel", 1536)]  # x0; rows 1536..2048
WIN_ROWS = ["dddddddd", "ddddSSCC", "wwwwwwww", "cccccccc", "BBBBbbbb", "aaaaaaaa", "TTTTqqqq", "ddddPPPP"]
RED = np.array([0.80, 0.086, 0.118])


# ------------------------------------------------------------------ noise (tileable)
def value_noise(n, cells, seed):
    r = np.random.default_rng(seed)
    lat = r.random((cells, cells)).astype(np.float32)
    x = np.arange(n, dtype=np.float32) / n * cells
    i0 = np.floor(x).astype(int) % cells
    i1 = (i0 + 1) % cells
    f = x - np.floor(x)
    f = f * f * (3 - 2 * f)
    a = lat[i0][:, i0]; b = lat[i0][:, i1]; c = lat[i1][:, i0]; d = lat[i1][:, i1]
    fx = f[None, :]; fy = f[:, None]
    return (a * (1 - fx) + b * fx) * (1 - fy) + (c * (1 - fx) + d * fx) * fy


def fbm(n, base, octaves, seed, gain=0.5):
    acc = np.zeros((n, n), np.float32); amp = 1.0; tot = 0.0
    for o in range(octaves):
        acc += value_noise(n, base * (2 ** o), seed + o * 17) * amp
        tot += amp; amp *= gain
    return acc / tot


def noise1d(n, cells, seed, octaves=4):
    r = np.random.default_rng(seed)
    acc = np.zeros(n, np.float32); amp = 1.0; tot = 0.0
    for o in range(octaves):
        c = cells * (2 ** o)
        lat = r.random(c)
        x = np.arange(n) / n * c
        i0 = np.floor(x).astype(int) % c; i1 = (i0 + 1) % c
        f = x - np.floor(x); f = f * f * (3 - 2 * f)
        acc += (lat[i0] * (1 - f) + lat[i1] * f) * amp
        tot += amp; amp *= 0.5
    return acc / tot


def normal_from_height(h, strength):
    dx = (np.roll(h, -1, 1) - np.roll(h, 1, 1)) * strength
    dy = (np.roll(h, -1, 0) - np.roll(h, 1, 0)) * strength
    nz = np.ones_like(h)
    l = np.sqrt(dx * dx + dy * dy + nz * nz)
    nx, ny = -dx / l, dy / l  # OpenGL style (+Y up), image row 0 = top
    return np.stack([nx * 0.5 + 0.5, ny * 0.5 + 0.5, nz / l * 0.5 + 0.5], -1)


def save(arr, name, mode):
    Image.fromarray(np.clip(arr * 255 + 0.5, 0, 255).astype(np.uint8), mode).save(os.path.join(OUT, name))
    print("wrote", name)


def blur(a, r):
    if r <= 0:
        return a
    im = Image.fromarray(np.clip(a * 255, 0, 255).astype(np.uint8))
    return np.asarray(im.filter(ImageFilter.GaussianBlur(r))).astype(np.float32) / 255.0


# ------------------------------------------------------------------ trim sheet
def trim_sheet():
    alb = np.zeros((N, N, 3), np.float32)
    H = np.zeros((N, N), np.float32)
    MET = np.zeros((N, N), np.float32)
    SM = np.full((N, N), 0.4, np.float32)
    AO = np.ones((N, N), np.float32)
    EM = np.zeros((N, N, 3), np.float32)
    big = fbm(N, 8, 5, 101)
    fine = fbm(N, 128, 2, 103)
    streak1d = noise1d(N, 64, 107)
    X = np.arange(N, dtype=np.float32)[None, :]

    def rows(name):
        for n, y0, y1 in TRIM:
            if n == name:
                return slice(y0, y1), y1 - y0
        raise KeyError(name)

    def ly(hh):
        return np.arange(hh, dtype=np.float32)[:, None]

    # ceramic 1 m panels (vertical seams every 1 m + edge seams, fasteners, vertical grime streaks)
    s, hh = rows("ceramic"); y = ly(hh)
    dx = np.minimum(X % 256, 256 - X % 256)
    dy = np.minimum(y, hh - 1 - y)
    seam = np.clip(1 - np.minimum(dx, dy) / 3.0, 0, 1)
    pid = (np.floor(X / 256) * 37) % 5
    grime = np.clip(streak1d[None, :] * (y / hh) ** 1.5 * 1.3 + big[s] * 0.25 - 0.2, 0, 1)
    fx = np.minimum(np.abs(X % 256 - 22), np.abs(X % 256 - 234)); fy = np.minimum(np.abs(y - 22), np.abs(y - (hh - 22)))
    fast = np.clip(1 - np.sqrt(fx ** 2 + fy ** 2) / 4.0, 0, 1)
    base = (0.80 + pid * 0.008 - grime * 0.10 + (fine[s] - 0.5) * 0.02) * (1 - seam * 0.45) * (1 - fast * 0.3)
    alb[s] = np.stack([base * 0.99, base * 0.99, base * 0.965], -1)
    H[s] = -seam - fast * 0.4 + fine[s] * 0.03
    SM[s] = 0.64 - grime * 0.25; AO[s] = 1 - seam * 0.5

    # graphite ribbed (ribs every 0.125 m)
    s, hh = rows("graph_rib"); y = ly(hh)
    rib = (0.5 + 0.5 * np.cos(X / 32.0 * 2 * math.pi)) ** 4 * np.ones_like(y)
    base = 0.085 + big[s] * 0.03 + rib * 0.02
    alb[s] = np.stack([base * 0.95, base, base * 1.08], -1)
    H[s] = rib * 0.7; MET[s] = 0.8; SM[s] = 0.42 + big[s] * 0.15; AO[s] = 0.85 + rib * 0.15

    # graphite smooth panel (seams every 2 m)
    s, hh = rows("graphite"); y = ly(hh)
    dx = np.minimum(X % 512, 512 - X % 512); dy = np.minimum(y, hh - 1 - y)
    seam = np.clip(1 - np.minimum(dx, dy) / 2.5, 0, 1)
    brushed = np.repeat(noise1d(hh, 32, 113)[:, None], N, 1) * 0.5 + big[s] * 0.5
    base = (0.09 + brushed * 0.03) * (1 - seam * 0.5)
    alb[s] = np.stack([base * 0.95, base, base * 1.07], -1)
    H[s] = -seam; MET[s] = 0.85; SM[s] = 0.5 + brushed * 0.15; AO[s] = 1 - seam * 0.4

    # brushed steel
    s, hh = rows("steel"); y = ly(hh)
    br = np.repeat(noise1d(hh, 48, 117)[:, None], N, 1)
    base = 0.40 + br * 0.08 + (big[s] - 0.5) * 0.06
    alb[s] = np.stack([base * 0.98, base, base * 1.03], -1)
    H[s] = br * 0.2; MET[s] = 1.0; SM[s] = 0.45 + br * 0.15

    # concrete with form-tie holes every 0.5 m
    s, hh = rows("concrete"); y = ly(hh)
    hole = np.clip(1 - np.sqrt((X % 128 - 64) ** 2 + (y - hh / 2) ** 2) / 5.0, 0, 1)
    base = (0.36 + (big[s] - 0.5) * 0.08 + (fine[s] - 0.5) * 0.05 - streak1d[None, :] * 0.04) * (1 - hole * 0.5)
    alb[s] = np.stack([base, base * 0.995, base * 0.98], -1)
    H[s] = fine[s] * 0.3 - hole; SM[s] = 0.2 + big[s] * 0.1; AO[s] = 1 - hole * 0.6

    # louvre: 4 slats per 0.25 m
    s, hh = rows("louvre"); y = ly(hh)
    t = (y % 16) / 16.0
    gap = (t > 0.82).astype(np.float32) * np.ones_like(X)
    base = (0.08 + (1 - t) * 0.05) * (1 - gap * 0.7) * np.ones_like(X)
    alb[s] = np.stack([base * 0.95, base, base * 1.06], -1)
    H[s] = (1 - t) * np.ones_like(X) - gap; MET[s] = 0.7; SM[s] = 0.45; AO[s] = 1 - gap * 0.6

    # light strips: graphite housing + diffuser, emissive
    for name, c in (("red_light", (1.0, 0.06, 0.05)), ("white_light", (0.95, 0.97, 1.0)), ("amber_light", (1.0, 0.5, 0.08))):
        s, hh = rows(name); y = ly(hh)
        c = np.array(c, np.float32)
        prof = np.exp(-((y - hh / 2) / (hh * 0.16)) ** 2) * np.ones_like(X)
        diff = ((y > hh * 0.22) & (y < hh * 0.78)).astype(np.float32) * np.ones_like(X)
        hous = 0.07 * np.ones_like(prof)
        alb[s] = hous[..., None] * (1 - diff[..., None]) + diff[..., None] * c * 0.55
        EM[s] = c * np.clip(prof * 1.1 + diff * 0.35, 0, 1)[..., None]
        H[s] = -diff * 0.3; SM[s] = 0.7; MET[s] = (1 - diff) * 0.6

    s, hh = rows("rubber")
    base = 0.03 + big[s] * 0.012
    alb[s] = np.stack([base, base, base * 1.05], -1); SM[s] = 0.28; H[s] = fine[s] * 0.2

    s, hh = rows("hazard"); y = ly(hh)
    stripe = (((X + y) // 32) % 2).astype(np.float32)
    wear = np.clip((big[s] - 0.62) * 4, 0, 1)
    yel = np.array([0.85, 0.58, 0.05], np.float32); blk = np.array([0.04, 0.04, 0.045], np.float32)
    a = stripe[..., None] * yel + (1 - stripe[..., None]) * blk
    alb[s] = a * (1 - wear[..., None] * 0.5) + wear[..., None] * 0.12; SM[s] = 0.42 - wear * 0.2; H[s] = -wear * 0.2

    s, hh = rows("dark_glass"); y = ly(hh)
    g = (1 - y / hh) * 0.015
    alb[s] = np.stack([0.018 + g, 0.026 + g, 0.036 + g * 1.4], -1) * np.ones((1, N, 1)); SM[s] = 0.95; MET[s] = 0.1

    s, hh = rows("cardboard"); y = ly(hh)
    tape = ((y > hh * 0.42) & (y < hh * 0.58)).astype(np.float32) * np.ones_like(X)
    flute = (0.5 + 0.5 * np.sin(X / 3.0)) * 0.02
    base = np.array([0.44, 0.32, 0.19], np.float32) * (0.9 + big[s][..., None] * 0.2) + flute[..., None]
    alb[s] = base * (1 - tape[..., None]) + tape[..., None] * np.array([0.62, 0.58, 0.5], np.float32)
    SM[s] = 0.18 + tape * 0.35; H[s] = -tape * 0.1

    s, hh = rows("white_paint"); y = ly(hh)
    scratch = np.clip((fbm(N, 256, 1, 131)[s] - 0.86) * 8, 0, 1)
    base = 0.78 + (big[s] - 0.5) * 0.04 - scratch * 0.25
    alb[s] = np.stack([base, base * 1.005, base * 1.012], -1); SM[s] = 0.55 - scratch * 0.2; H[s] = -scratch * 0.3

    s, hh = rows("red_paint")
    alb[s] = RED * 0.82 * (0.92 + big[s][..., None] * 0.16); SM[s] = 0.52; H[s] = fine[s] * 0.05

    s, hh = rows("corrugated"); y = ly(hh)
    t = (X % 32) / 32.0
    corr = np.clip(np.abs(t - 0.5) * 4 - 0.5, 0, 1) * np.ones_like(y)
    grime = np.clip(streak1d[None, :] * (y / hh) * 1.2 + big[s] * 0.2 - 0.25, 0, 1)
    base = 0.31 + corr * 0.04 - grime * 0.08
    alb[s] = np.stack([base * 0.97, base, base * 1.04], -1)
    H[s] = corr; MET[s] = 0.6; SM[s] = 0.35 - grime * 0.1; AO[s] = 0.85 + corr * 0.15

    # ---------------- cells (512 x 512 at rows 1536..2048)
    cy = 1536
    lat = ImageFont.truetype(FONT_LAT, 34)
    small = ImageFont.truetype(FONT_LAT, 16)

    # vending machine front: designed on a 256 x 512 canvas (0.9 x 1.8 m face), stretched to 512 x 512
    va = Image.new("RGB", (256, 512), (205, 207, 210)); ve = Image.new("RGB", (256, 512), (0, 0, 0))
    da, de = ImageDraw.Draw(va), ImageDraw.Draw(ve)
    da.rectangle([0, 0, 255, 60], fill=(150, 18, 26)); de.rectangle([0, 0, 255, 60], fill=(90, 8, 10))
    for d, f in ((da, (240, 240, 240)), (de, (200, 200, 205))):
        d.text((128, 30), "TSUKASA", font=lat, fill=f, anchor="mm")
    da.rectangle([14, 74, 241, 330], fill=(20, 22, 26)); de.rectangle([14, 74, 241, 330], fill=(120, 124, 130))
    r = np.random.default_rng(5)
    cans = [(230, 230, 232), (200, 30, 36), (170, 175, 182), (210, 130, 40), (90, 110, 130), (40, 44, 50)]
    for row in range(4):
        for col in range(5):
            cc = cans[int(r.integers(0, len(cans)))]
            x0 = 22 + col * 44; y0 = 86 + row * 60
            da.rounded_rectangle([x0, y0, x0 + 34, y0 + 46], 6, fill=cc)
            de.rounded_rectangle([x0, y0, x0 + 34, y0 + 46], 6, fill=tuple(int(v * 0.85) for v in cc))
            da.rectangle([x0 - 4, y0 + 50, x0 + 38, y0 + 53], fill=(60, 62, 66))
            de.ellipse([x0 + 13, y0 + 55, x0 + 20, y0 + 58], fill=(255, 150, 40))
    da.rectangle([14, 346, 241, 380], fill=(30, 32, 36))
    for i in range(6):
        de.rectangle([24 + i * 36, 356, 44 + i * 36, 368], fill=(255, 140, 30))
    da.rectangle([170, 392, 236, 450], fill=(28, 30, 34)); de.rectangle([180, 400, 226, 420], fill=(150, 190, 230))
    da.text((176, 432), "ID PAY", font=small, fill=(200, 200, 200)); de.text((176, 432), "ID PAY", font=small, fill=(120, 120, 120))
    da.rectangle([24, 462, 160, 500], fill=(12, 12, 14))
    da.rectangle([0, 0, 255, 511], outline=(150, 152, 156), width=3)
    va = va.resize((512, 512), Image.BICUBIC); ve = ve.resize((512, 512), Image.BICUBIC)
    alb[cy:, 0:512] = np.asarray(va).astype(np.float32) / 255.0
    EM[cy:, 0:512] = np.asarray(ve).astype(np.float32) / 255.0
    SM[cy:, 0:512] = 0.6; MET[cy:, 0:512] = 0.0
    H[cy:, 0:512] = 0.0

    # fan grille (AC condensers, roof units)
    yy, xx = np.mgrid[0:512, 0:512].astype(np.float32)
    rr = np.sqrt((xx - 256) ** 2 + (yy - 256) ** 2); ang = np.arctan2(yy - 256, xx - 256)
    inside = rr < 220
    rings = (np.abs(((rr % 18) - 9)) < 2.2) & inside
    spokes = (np.abs(np.sin(ang * 4)) < 0.06) & inside & (rr > 40)
    blades = (np.sin(ang * 5 + rr / 60.0) > 0.55) & (rr < 205) & (rr > 40)
    hub = rr < 40
    a = np.full((512, 512), 0.42, np.float32)
    a[inside] = 0.035; a[blades] = 0.12; a[rings | spokes] = 0.5; a[hub] = 0.3
    rim = (rr > 220) & (rr < 232)
    a[rim] = 0.22
    alb[cy:, 512:1024] = np.stack([a * 0.97, a, a * 1.04], -1)
    h = np.where(inside, -0.6, 0.0) + (rings | spokes) * 0.8 + hub * 0.6 - rim * 0.4
    H[cy:, 512:1024] = h; MET[cy:, 512:1024] = 0.6; SM[cy:, 512:1024] = 0.4; AO[cy:, 512:1024] = np.where(inside & ~(rings | spokes), 0.45, 1.0)

    # TSUKASA emblem relief (ceramic, raised) + red inlay light
    if os.path.exists(LOGO):
        lg = Image.open(LOGO).convert("L").resize((440, 440), Image.LANCZOS)
        L = np.zeros((512, 512), np.float32); L[36:476, 36:476] = np.asarray(lg).astype(np.float32) / 255.0
    else:
        L = np.zeros((512, 512), np.float32)
    L = np.clip((L - 0.35) * 2.2, 0, 1)
    hl = blur(L, 2.5)
    inlay = np.clip((blur(L, 0.8) - 0.85) * 8, 0, 1)
    dxe = np.minimum(xx, 511 - xx); dye = np.minimum(yy, 511 - yy)
    seam = np.clip(1 - np.minimum(dxe, dye) / 4.0, 0, 1)
    base = (0.80 - seam * 0.4) * (1 - (hl - L).clip(0, 1) * 0.35)
    alb[cy:, 1024:1536] = np.stack([base * 0.99, base * 0.99, base * 0.965], -1) * (1 - inlay[..., None]) + inlay[..., None] * np.array([0.6, 0.05, 0.05])
    EM[cy:, 1024:1536] = inlay[..., None] * np.array([1.0, 0.06, 0.05])
    H[cy:, 1024:1536] = hl * 2.5 - seam; SM[cy:, 1024:1536] = 0.62; AO[cy:, 1024:1536] = 1 - seam * 0.5

    # access / ID panel: screen + keypad + reader
    pa = Image.new("RGB", (512, 512), (24, 25, 28)); pe = Image.new("RGB", (512, 512), (0, 0, 0))
    da, de = ImageDraw.Draw(pa), ImageDraw.Draw(pe)
    da.rectangle([40, 36, 472, 200], fill=(14, 20, 28)); de.rectangle([40, 36, 472, 200], fill=(18, 30, 44))
    fsc = ImageFont.truetype(FONT_LAT, 30)
    for i, t in enumerate(("ID VERIFY", "TSK-SEC 07", "STATUS: STANDBY")):
        de.text((60, 52 + i * 44), t, font=fsc, fill=(190, 215, 235)); da.text((60, 52 + i * 44), t, font=fsc, fill=(80, 96, 110))
    de.rectangle([60, 186, 260, 190], fill=(200, 30, 30))
    for row in range(4):
        for col in range(3):
            x0 = 60 + col * 72; y0 = 232 + row * 62
            da.rounded_rectangle([x0, y0, x0 + 56, y0 + 48], 6, fill=(52, 54, 58))
            de.rounded_rectangle([x0 + 20, y0 + 18, x0 + 36, y0 + 30], 3, fill=(150, 80, 20))
    da.rounded_rectangle([300, 232, 470, 420], 10, fill=(40, 42, 46))
    da.rectangle([320, 300, 450, 312], fill=(8, 8, 9))
    de.ellipse([378, 250, 392, 264], fill=(255, 30, 25))
    for i in range(6):
        da.rectangle([300, 440 + i * 10, 470, 444 + i * 10], fill=(10, 10, 11))
    alb[cy:, 1536:2048] = np.asarray(pa).astype(np.float32) / 255.0
    EM[cy:, 1536:2048] = np.asarray(pe).astype(np.float32) / 255.0
    gl = np.asarray(pa.convert("L")).astype(np.float32) / 255.0
    H[cy:, 1536:2048] = blur(gl, 1.0) * 1.5; MET[cy:, 1536:2048] = 0.5; SM[cy:, 1536:2048] = 0.55

    save(alb, "T_INFD_Trim_Albedo.png", "RGB")
    save(normal_from_height(H, 2.4), "T_INFD_Trim_Normal.png", "RGB")
    save(np.stack([MET, AO, np.zeros_like(AO), SM], -1), "T_INFD_Trim_Mask.png", "RGBA")
    save(np.clip(EM, 0, 1), "T_INFD_Trim_Emission.png", "RGB")


# ------------------------------------------------------------------ window interiors
WARM = np.array([1.0, 0.80, 0.56], np.float32)
COOL = np.array([0.78, 0.87, 1.0], np.float32)


def window_cell(kind, r):
    n = 256
    y, x = np.mgrid[0:n, 0:n].astype(np.float32)
    t = y / n
    glass = np.stack([0.045 + t * 0.05, 0.055 + t * 0.03, 0.075 + t * 0.0], -1)
    glass += np.clip(1 - np.abs((x + y * 0.6) / n - 0.7 - r.random() * 0.3) * 6, 0, 1)[..., None] * 0.035
    em = np.zeros((n, n, 3), np.float32)
    lum = 0.0
    if kind in "wcBbP":
        c = WARM if kind in "wBP" else COOL
        c = c * (0.92 + r.random() * 0.12)
        room = (0.35 + 0.65 * np.clip(1 - t, 0, 1) ** 0.8)
        ceil = np.zeros((n, n), np.float32)
        for k, yy in enumerate((18, 40, 58)):
            w = 0.5 - k * 0.12
            ceil += ((np.abs(y - yy) < 4 - k) & (np.abs(x - n / 2) < n * w)).astype(np.float32) * (1.0 - k * 0.2)
        img = room * 0.55 + ceil * 0.9
        for _ in range(int(r.integers(2, 6))):  # desks / partitions silhouettes
            x0 = r.integers(0, n - 50); w = r.integers(30, 90); h = r.integers(30, 70)
            img = np.where((x >= x0) & (x < x0 + w) & (y > n - h), img * 0.25, img)
        for _ in range(int(r.integers(1, 4))):  # monitor glows
            x0 = r.integers(10, n - 30); y0 = r.integers(int(n * 0.55), int(n * 0.7))
            em_m = ((x >= x0) & (x < x0 + 18) & (y >= y0) & (y < y0 + 12)).astype(np.float32)
            em += em_m[..., None] * COOL * 0.6
        if kind == "P":  # shop: brighter, shelves of goods
            img = 0.55 + 0.45 * np.clip(1 - t, 0, 1)
            for k in range(4):
                yy = 90 + k * 38
                shelf = (np.abs(y - yy) < 2)
                goods = (y > yy - 26) & (y < yy - 2) & ((x // 14) % 2 == 0)
                img = np.where(shelf, 0.2, img)
                col = np.array([[0.9, 0.3, 0.3], [0.3, 0.5, 0.9], [0.9, 0.8, 0.4], [0.9, 0.9, 0.9]][k], np.float32)
                em += goods[..., None] * col * 0.25
        if kind in "Bb":
            down = r.random() * 0.8 + 0.2
            slats = ((y % 9) < 6) & (t < down)
            img = np.where(slats, img * 0.3, img)
        em += img[..., None] * c
        lum = 1.0
    elif kind in "aq":
        c = WARM * (0.75 if kind == "a" else 0.45)
        cx = r.random() * n; cyy = n * (0.45 + r.random() * 0.3)
        glow = np.exp(-(((x - cx) / (n * 0.45)) ** 2 + ((y - cyy) / (n * 0.5)) ** 2))
        img = 0.25 + glow * 0.75
        cl = r.random() * 0.35 + 0.1; cr = r.random() * 0.35 + 0.1
        curt = (x < n * cl) | (x > n * (1 - cr))
        folds = 0.6 + 0.4 * np.sin(x / 5.0) ** 2
        img = np.where(curt, img * folds * 0.55, img)
        em += img[..., None] * c
        lum = 1.0
    elif kind == "T":
        c = WARM * 0.35
        img = 0.3 + 0.2 * np.clip(1 - t, 0, 1)
        em += img[..., None] * c
        x0 = r.integers(30, n - 120); tv = ((x >= x0) & (x < x0 + 90) & (y > n * 0.45) & (y < n * 0.75)).astype(np.float32)
        em += blur(tv, 14)[..., None] * np.array([0.45, 0.55, 0.75], np.float32) * 0.9
        lum = 1.0
    elif kind == "C":
        c = COOL * 0.85
        img = np.clip(1 - np.abs(x - n / 2) / (n * 0.5), 0, 1) ** 2 * 0.6 + ((np.abs(y - 22) < 4) & (np.abs(x - n / 2) < 40)) * 1.0
        img = img + np.clip(1 - t, 0, 1) * 0.15
        em += img[..., None] * c
        lum = 1.0
    elif kind == "S":
        c = np.array([0.85, 0.78, 0.66], np.float32) * 0.28
        img = 0.6 + 0.4 * np.clip(1 - t, 0, 1)
        img = np.where((y % 40) < 4, 0.2, img)
        em += img[..., None] * c
        lum = 0.6
    frame = (np.minimum(np.minimum(x, n - 1 - x), np.minimum(y, n - 1 - y)) < 3)
    em[frame] = 0
    alb = glass * (1 - lum * 0.3) + np.clip(em, 0, 1) * 0.3
    alb[frame] = 0.05
    return np.clip(alb, 0, 1), np.clip(em, 0, 1)


def windows():
    r = np.random.default_rng(1601)
    A = np.zeros((N, N, 3), np.float32); E = np.zeros((N, N, 3), np.float32)
    for row, kinds in enumerate(WIN_ROWS):
        for col, k in enumerate(kinds):
            a, e = window_cell(k, r)
            A[row * 256:(row + 1) * 256, col * 256:(col + 1) * 256] = a
            E[row * 256:(row + 1) * 256, col * 256:(col + 1) * 256] = e
    save(A, "T_INFD_Windows_Albedo.png", "RGB")
    save(E, "T_INFD_Windows_Emission.png", "RGB")


# ------------------------------------------------------------------ puddles
def puddles():
    n = 1024; c = 512
    A = np.zeros((n, n, 4), np.float32); Hh = np.zeros((n, n), np.float32); S = np.zeros((n, n), np.float32)
    warp = fbm(n, 6, 4, 211); rip = fbm(n, 64, 2, 223)
    yy, xx = np.mgrid[0:c, 0:c].astype(np.float32)
    for i in range(4):
        ox, oy = (i % 2) * c, (i // 2) * c
        r = np.random.default_rng(300 + i)
        ax, ay = 0.8 + r.random() * 0.2, 0.45 + r.random() * 0.4
        d = np.sqrt(((xx - c / 2) / (c * 0.45 * ax)) ** 2 + ((yy - c / 2) / (c * 0.45 * ay)) ** 2)
        d = d + (warp[oy:oy + c, ox:ox + c] - 0.5) * 0.9
        m = np.clip((1.0 - d) * 12, 0, 1)
        edge = np.clip((1.0 - d) * 3, 0, 1)
        wet = np.clip((1.25 - d) * 3, 0, 1)
        a = np.full((c, c), 0.022, np.float32) * (1 + (1 - edge) * 0.4)
        A[oy:oy + c, ox:ox + c, :3] = a[..., None] * np.array([0.95, 1.0, 1.08])
        A[oy:oy + c, ox:ox + c, 3] = np.maximum(m, wet * 0.55)
        Hh[oy:oy + c, ox:ox + c] = rip[oy:oy + c, ox:ox + c] * 0.15 * edge
        S[oy:oy + c, ox:ox + c] = 0.55 + edge * 0.42
    save(A, "T_INFD_Puddle_Albedo.png", "RGBA")
    save(normal_from_height(Hh, 1.5), "T_INFD_Puddle_Normal.png", "RGB")
    save(np.stack([np.zeros_like(S), np.ones_like(S), np.zeros_like(S), S], -1), "T_INFD_Puddle_Mask.png", "RGBA")


if __name__ == "__main__":
    trim_sheet()
    windows()
    puddles()
