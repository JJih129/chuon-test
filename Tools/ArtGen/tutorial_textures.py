"""Infiltration tutorial (iteration 13) texture set -- numpy + PIL, deterministic.

Writes Assets/Art/ChuonPolish/Tutorial/Textures/T_INF_*.png:
  Asphalt / Road / Sidewalk / Ceramic / Graphite / Facade A,B / Container / Shutter  (Albedo, Normal, Mask[, Emission])
  Signs atlas (4x2 slogans + emblem, emissive), Sky panorama (dusk), Skyline card (alpha), VR grid (alpha).
URP mask map convention: R metallic, G occlusion, B detail mask, A smoothness.
Usage: python Tools/ArtGen/tutorial_textures.py [out_dir]
Style: corporate-military graphite / off-white ceramic / TSUKASA red; dusk city, not neon.
"""
import os, sys, math
import numpy as np
from PIL import Image, ImageDraw, ImageFont, ImageFilter

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
OUT = os.path.abspath(sys.argv[1]) if len(sys.argv) > 1 else os.path.join(ROOT, "Assets", "Art", "ChuonPolish", "Tutorial", "Textures")
os.makedirs(OUT, exist_ok=True)
RNG = np.random.default_rng(1307)
FONT_KO = r"C:\Windows\Fonts\malgunbd.ttf"
FONT_LAT = r"C:\Windows\Fonts\bahnschrift.ttf"
LOGO = os.path.join(ROOT, "Assets", "Art", "ChuonPolish", "Level_Facility", "Textures", "T_LF_Holo_TsukasaLogo.png")
RED = (205, 22, 30)


# ------------------------------------------------------------------ noise helpers (tileable)
def value_noise(n, cells, seed):
    """Tileable smooth value noise in [0,1], n x n, `cells` lattice cells per side."""
    r = np.random.default_rng(seed)
    lat = r.random((cells, cells))
    x = np.arange(n) / n * cells
    i0 = np.floor(x).astype(int) % cells
    i1 = (i0 + 1) % cells
    f = x - np.floor(x)
    f = f * f * (3 - 2 * f)
    a = lat[i0][:, i0]; b = lat[i0][:, i1]; c = lat[i1][:, i0]; d = lat[i1][:, i1]
    fx = f[None, :]; fy = f[:, None]
    return (a * (1 - fx) + b * fx) * (1 - fy) + (c * (1 - fx) + d * fx) * fy


def fbm(n, base, octaves, seed, gain=0.5):
    acc = np.zeros((n, n)); amp = 1.0; tot = 0.0
    for o in range(octaves):
        acc += value_noise(n, base * (2 ** o), seed + o * 17) * amp
        tot += amp; amp *= gain
    return acc / tot


def normal_from_height(h, strength):
    dx = (np.roll(h, -1, 1) - np.roll(h, 1, 1)) * strength
    dy = (np.roll(h, -1, 0) - np.roll(h, 1, 0)) * strength
    nz = np.ones_like(h)
    l = np.sqrt(dx * dx + dy * dy + nz * nz)
    # Unity: +Y up in tangent space (OpenGL style). Image row 0 = top -> invert dy.
    nx, ny = -dx / l, dy / l
    return np.stack([(nx * 0.5 + 0.5), (ny * 0.5 + 0.5), (nz / l * 0.5 + 0.5)], -1)


def save_rgb(arr, name):
    Image.fromarray(np.clip(arr * 255 + 0.5, 0, 255).astype(np.uint8), "RGB").save(os.path.join(OUT, name))
    print("wrote", name)


def save_rgba(arr, name):
    Image.fromarray(np.clip(arr * 255 + 0.5, 0, 255).astype(np.uint8), "RGBA").save(os.path.join(OUT, name))
    print("wrote", name)


def mask(metal, ao, detail, smooth):
    return np.stack([metal, ao, detail, smooth], -1)


def lerp(a, b, t):
    return a + (b - a) * t


def col(c):
    return np.array(c, dtype=np.float64) / 255.0


# ------------------------------------------------------------------ surfaces
def asphalt(n=1024):
    grain = RNG.random((n, n))
    grain = np.array(Image.fromarray((grain * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(0.6))) / 255.0
    big = fbm(n, 4, 5, 11)
    stain = np.clip((fbm(n, 3, 4, 23) - 0.52) * 4.0, 0, 1)
    puddle = np.clip((fbm(n, 2, 4, 37) - 0.60) * 7.0, 0, 1)
    base = lerp(0.075, 0.13, big) + (grain - 0.5) * 0.05 - stain * 0.03
    alb = np.stack([base * 0.97, base * 0.99, base * 1.04], -1)
    alb = alb * (1 - puddle[..., None] * 0.35)
    h = grain * 0.6 + big * 0.4 - puddle * 0.5
    nrm = normal_from_height(h, 2.2)
    nrm = lerp(nrm, np.array([0.5, 0.5, 1.0]), puddle[..., None])
    sm = np.clip(0.18 + big * 0.12 + puddle * 0.72, 0, 0.94)
    return alb, nrm, mask(np.zeros_like(sm), lerp(0.85, 1.0, big), np.zeros_like(sm), sm), puddle


def write_asphalt():
    alb, nrm, msk, _ = asphalt()
    save_rgb(alb, "T_INF_Asphalt_Albedo.png"); save_rgb(nrm, "T_INF_Asphalt_Normal.png"); save_rgba(msk, "T_INF_Asphalt_Mask.png")


def write_road():
    """Road strip: u = across 8 m (0..1), v = along 16 m (tiles). Lane edge lines + dashed centre."""
    n = 1024
    alb, nrm, msk, puddle = asphalt(n)
    u = (np.arange(n) + 0.5) / n
    v = (np.arange(n) + 0.5) / n
    U, V = np.meshgrid(u, v)
    w = 8.0
    xm = U * w
    paint = np.zeros((n, n))
    paint += ((xm > 0.35) & (xm < 0.50)).astype(float)
    paint += ((xm > w - 0.50) & (xm < w - 0.35)).astype(float)
    dash = ((V * 16.0) % 8.0) < 4.0
    paint += (((xm > w / 2 - 0.08) & (xm < w / 2 + 0.08)) & dash).astype(float)
    wear = np.clip(fbm(n, 16, 3, 51) * 1.4 - 0.15, 0, 1)
    paint = np.clip(paint, 0, 1) * wear
    pc = col((196, 196, 188))
    alb = lerp(alb, pc[None, None, :] * np.ones_like(alb), paint[..., None] * 0.9)
    msk[..., 3] = lerp(msk[..., 3], 0.35, paint)
    save_rgb(alb, "T_INF_Road_Albedo.png"); save_rgb(nrm, "T_INF_Road_Normal.png"); save_rgba(msk, "T_INF_Road_Mask.png")


def write_sidewalk():
    """Concrete pavers, 0.6 x 1.2 m running bond; texture = 2.4 x 2.4 m."""
    n = 1024
    px = n / 2.4
    y, x = np.mgrid[0:n, 0:n].astype(float)
    row = np.floor(y / (0.6 * px))
    xo = x + (row % 2) * 0.6 * px
    gx = np.minimum((xo % (1.2 * px)), 1.2 * px - (xo % (1.2 * px)))
    gy = np.minimum((y % (0.6 * px)), 0.6 * px - (y % (0.6 * px)))
    joint = np.clip(1.0 - np.minimum(gx, gy) / 3.0, 0, 1)
    tile_id = (row * 7 + np.floor(xo / (1.2 * px)) * 13) % 11
    tint = 0.92 + (tile_id / 11.0) * 0.1
    grain = fbm(n, 32, 3, 61)
    big = fbm(n, 3, 4, 67)
    base = (0.36 + grain * 0.07 - big * 0.05) * tint
    base = base * (1 - joint * 0.55)
    alb = np.stack([base, base * 0.995, base * 0.985], -1)
    h = grain * 0.4 - joint * 1.0
    save_rgb(alb, "T_INF_Sidewalk_Albedo.png")
    save_rgb(normal_from_height(h, 3.0), "T_INF_Sidewalk_Normal.png")
    sm = np.clip(0.22 + (fbm(n, 2, 3, 71) - 0.55) * 1.4, 0.18, 0.62)
    save_rgba(mask(np.zeros_like(sm), 1 - joint * 0.5, np.zeros_like(sm), sm), "T_INF_Sidewalk_Mask.png")


def panel_field(n, pw, ph, seam, seed):
    """Returns (seam mask 0..1, panel id) for a panel grid of pw x ph pixels."""
    y, x = np.mgrid[0:n, 0:n].astype(float)
    gx = np.minimum(x % pw, pw - x % pw)
    gy = np.minimum(y % ph, ph - y % ph)
    s = np.clip(1.0 - np.minimum(gx, gy) / seam, 0, 1)
    pid = (np.floor(x / pw) * 31 + np.floor(y / ph) * 17 + seed) % 7
    return s, pid


def write_ceramic():
    """Off-white ceramic cladding: 1.0 x 2.0 m panels (texture = 2 x 4 m -> 1024 x 2048 scaled to 1024^2)."""
    n = 1024
    s, pid = panel_field(n, n / 2, n / 2, 3.5, 3)
    grime = fbm(n, 4, 5, 81)
    streak = np.clip(fbm(n, 64, 2, 83)[:, :] * 0.0 + np.repeat(fbm(n, 48, 2, 85)[:1, :], n, 0), 0, 1)
    base = 0.74 + (pid / 7.0) * 0.03 - grime * 0.07 - streak * 0.04
    base = base * (1 - s * 0.6)
    alb = np.stack([base * 0.985, base * 0.99, base], -1)
    h = -s * 1.0 + fbm(n, 64, 2, 87) * 0.05
    save_rgb(alb, "T_INF_Ceramic_Albedo.png")
    save_rgb(normal_from_height(h, 2.4), "T_INF_Ceramic_Normal.png")
    sm = 0.62 - grime * 0.18
    save_rgba(mask(np.zeros_like(sm), 1 - s * 0.6, np.zeros_like(sm), sm), "T_INF_Ceramic_Mask.png")


def write_graphite():
    """Graphite metal: vertical ribs every 0.25 m + panel seams (texture = 2 x 2 m)."""
    n = 1024
    y, x = np.mgrid[0:n, 0:n].astype(float)
    rib = 0.5 + 0.5 * np.cos(x / (n / 8.0) * 2 * math.pi)
    rib = rib ** 6
    s, pid = panel_field(n, n / 2, n / 2, 2.5, 9)
    brushed = np.repeat(fbm(n, 128, 2, 91)[:1, :], n, 0) * 0.6 + fbm(n, 8, 3, 93) * 0.4
    base = 0.085 + brushed * 0.03 + (pid / 7.0) * 0.01
    base = base * (1 - s * 0.5)
    alb = np.stack([base * 0.96, base, base * 1.06], -1)
    h = rib * 0.35 - s
    save_rgb(alb, "T_INF_Graphite_Albedo.png")
    save_rgb(normal_from_height(h, 2.6), "T_INF_Graphite_Normal.png")
    sm = 0.48 + brushed * 0.18
    save_rgba(mask(np.full_like(sm, 0.75), 1 - s * 0.5, np.zeros_like(sm), sm), "T_INF_Graphite_Mask.png")


def write_facade(tag, bays, floors, frame_c, spandrel_c, glass_c, lit_ratio, seed):
    """Curtain-wall facade tile: `bays` x `floors` windows in one 1024^2 tile (u wraps bays, v wraps floors).
    Albedo, Normal, Mask, Emission (lit windows: warm/cool office light, some blinds)."""
    n = 1024
    r = np.random.default_rng(seed)
    y, x = np.mgrid[0:n, 0:n].astype(float)
    bw = n / bays; fh = n / floors
    bx = x % bw; fy = y % fh
    bi = np.floor(x / bw).astype(int); fi = np.floor(y / fh).astype(int)
    mull = (bx < bw * 0.06) | (bx > bw * 0.94)
    spand = fy > fh * 0.72
    glass = ~mull & ~spand
    lit = r.random((floors, bays)) < lit_ratio
    warm = r.random((floors, bays)) < 0.55
    blind = r.random((floors, bays))
    L = lit[fi, bi]; W = warm[fi, bi]; B = blind[fi, bi]
    alb = np.zeros((n, n, 3))
    alb[mull] = col(frame_c); alb[spand] = col(spandrel_c)
    g = col(glass_c)
    refl = 0.8 + 0.4 * (y % fh) / fh
    alb[glass] = g * refl[glass][:, None]
    em = np.zeros((n, n, 3))
    wc = np.where(W[..., None], col((255, 214, 160)), col((200, 225, 255)))
    # interior: ceiling bright band + falloff + blinds stripes on some
    inner = np.clip(1.0 - (fy / (fh * 0.72)), 0, 1) ** 0.6
    stripes = np.where(B > 0.7, 0.55 + 0.45 * (np.sin(fy / fh * 70) > 0), 1.0)
    lum = (0.35 + 0.65 * inner) * stripes
    em = np.where((glass & L)[..., None], wc * lum[..., None], 0.0)
    alb = np.where((glass & L)[..., None], lerp(alb, wc * 0.35, 0.5), alb)
    h = np.where(mull, 1.0, np.where(spand, 0.6, 0.0)) + fbm(n, 16, 2, seed + 3) * 0.04
    msk = mask(np.where(mull, 0.8, 0.0), np.where(glass, 0.9, 1.0), np.zeros((n, n)), np.where(glass, 0.92, np.where(mull, 0.55, 0.35)))
    save_rgb(alb, f"T_INF_Facade{tag}_Albedo.png")
    save_rgb(normal_from_height(h, 3.0), f"T_INF_Facade{tag}_Normal.png")
    save_rgba(msk, f"T_INF_Facade{tag}_Mask.png")
    save_rgb(em, f"T_INF_Facade{tag}_Emission.png")


def write_corrugated(name, n, period_px, base_c, horizontal, stencil=None, stripe=None):
    y, x = np.mgrid[0:n, 0:n].astype(float)
    t = (y if horizontal else x) / period_px * 2 * math.pi
    h = np.abs(np.sin(t)) ** 0.7
    grime = fbm(n, 6, 4, hash(name) % 1000)
    base = col(base_c)[None, None, :] * (0.86 + grime[..., None] * 0.2) * (0.92 + h[..., None] * 0.1)
    img = Image.fromarray(np.clip(base * 255, 0, 255).astype(np.uint8), "RGB")
    d = ImageDraw.Draw(img)
    if stripe:
        d.rectangle([0, int(n * stripe[0]), n, int(n * stripe[1])], fill=RED)
    if stencil:
        f = ImageFont.truetype(FONT_LAT, int(n * 0.07))
        d.text((int(n * 0.06), int(n * 0.12)), stencil, font=f, fill=(225, 225, 220))
    alb = np.array(img).astype(float) / 255.0
    save_rgb(alb, f"T_INF_{name}_Albedo.png")
    save_rgb(normal_from_height(h, 1.6), f"T_INF_{name}_Normal.png")
    sm = 0.32 + grime * 0.2
    save_rgba(mask(np.full_like(sm, 0.55), 0.85 + h * 0.15, np.zeros_like(sm), sm), f"T_INF_{name}_Mask.png")


# ------------------------------------------------------------------ signage atlas (emissive)
SIGNS = [
    ("질서가 곧 안전입니다", "ORDER IS SAFETY  ·  TSUKASA CORPORATION", "emblem_red"),
    ("감시는 보호입니다", "SURVEILLANCE IS PROTECTION", "white"),
    ("신분증 미소지 시 출입 금지", "NO ID  ·  NO ENTRY  —  SECURITY CHECKPOINT 07", "red"),
    ("TSUKASA LOGISTICS", "DOCK 03  ·  PROJECT SERIES INBOUND", "white"),
    ("내일을 설계합니다", "TSUKASA — WE DESIGN TOMORROW", "emblem_white"),
    ("관계자 외 출입 금지", "AUTHORIZED PERSONNEL ONLY  ·  SIDE ENTRANCE B", "red"),
    ("보안 스캔 구역", "SECURITY SCAN ZONE  ·  KEEP MOVING", "amber"),
    ("본사 로비", "HEADQUARTERS LOBBY  ·  RECEPTION", "white"),
]


def write_signs():
    W, H = 2048, 1024
    cw, ch = W // 4, H // 2
    alb = Image.new("RGB", (W, H), (10, 11, 13))
    em = Image.new("RGB", (W, H), (0, 0, 0))
    logo = Image.open(LOGO).convert("L") if os.path.exists(LOGO) else None
    fk = ImageFont.truetype(FONT_KO, 56)
    fl = ImageFont.truetype(FONT_LAT, 22)
    for i, (ko, en, style) in enumerate(SIGNS):
        cx, cy = (i % 4) * cw, (i // 4) * ch
        da = ImageDraw.Draw(alb); de = ImageDraw.Draw(em)
        accent = {"red": RED, "amber": (240, 150, 30)}.get(style, (235, 236, 238))
        if style.startswith("emblem") and logo is not None:
            lg = logo.resize((210, 210)).crop((0, 6, 210, 128))
            tint = RED if style == "emblem_red" else (240, 240, 240)
            layer = Image.new("RGB", lg.size, tint)
            em.paste(layer, (cx + (cw - 210) // 2, cy + 30), lg)
            alb.paste(Image.new("RGB", lg.size, tuple(int(c * 0.6) for c in tint)), (cx + (cw - 210) // 2, cy + 30), lg)
            ty = cy + 215
        else:
            de.rectangle([cx + 30, cy + 70, cx + cw - 30, cy + 78], fill=accent)
            da.rectangle([cx + 30, cy + 70, cx + cw - 30, cy + 78], fill=tuple(int(c * 0.7) for c in accent))
            ty = cy + 130
        tw = da.textlength(ko, font=fk)
        size = 56
        f = fk
        while tw > cw - 60 and size > 30:
            size -= 4; f = ImageFont.truetype(FONT_KO, size); tw = da.textlength(ko, font=f)
        txt_c = accent if style in ("red", "amber") else (238, 238, 240)
        for d, c in ((da, tuple(int(v * 0.75) for v in txt_c)), (de, txt_c)):
            d.text((cx + (cw - tw) / 2, ty), ko, font=f, fill=c)
        ew = da.textlength(en, font=fl)
        while ew > cw - 50:
            fl2 = ImageFont.truetype(FONT_LAT, 18); ew = da.textlength(en, font=fl2); break
        else:
            fl2 = fl
        for d, c in ((da, (150, 152, 156)), (de, (120, 122, 126))):
            d.text((cx + (cw - ew) / 2, ty + size + 26), en, font=fl2, fill=c)
        da.rectangle([cx + 2, cy + 2, cx + cw - 3, cy + ch - 3], outline=(40, 42, 46), width=3)
    alb.save(os.path.join(OUT, "T_INF_Signs_Albedo.png")); em.save(os.path.join(OUT, "T_INF_Signs_Emission.png"))
    print("wrote T_INF_Signs_*")


# ------------------------------------------------------------------ sky + skyline + grid
def write_sky():
    W, H = 2048, 1024
    y = (np.arange(H) + 0.5) / H          # 0 top .. 1 bottom
    elev = (0.5 - y) * 180.0               # degrees
    x = (np.arange(W) + 0.5) / W
    az = x * 360.0
    E, A = np.meshgrid(elev, az, indexing="ij")
    zen = col((10, 16, 34)); mid = col((34, 52, 82)); hor = col((168, 108, 92)); glow = col((236, 150, 96))
    t = np.clip(E / 60.0, 0, 1)
    sky = lerp(mid[None, None, :], zen[None, None, :], (t ** 0.8)[..., None])
    low = np.clip(1 - E / 14.0, 0, 1)
    sky = lerp(sky, hor[None, None, :], (low ** 1.6)[..., None])
    # sun glow low in the west-south-west (az ~ 250) -> warm band
    dA = np.minimum(np.abs(A - 250), 360 - np.abs(A - 250))
    sun = np.exp(-(dA / 38.0) ** 2) * np.exp(-np.clip(E, 0, 90) / 7.0)
    sky = sky + glow[None, None, :] * sun[..., None] * 0.55
    # soft clouds (tileable in azimuth)
    n = 1024
    cl = fbm(n, 6, 5, 97)
    cl = np.array(Image.fromarray((cl * 255).astype(np.uint8)).resize((W, H))) / 255.0
    band = np.exp(-((E - 16) / 12.0) ** 2)
    cmask = np.clip((cl - 0.5) * 3.0, 0, 1) * band
    ccol = lerp(col((40, 46, 64)), col((150, 96, 90)), np.clip(1 - E / 30, 0, 1)[..., None])
    sky = lerp(sky, ccol, (cmask * 0.55)[..., None])
    ground = col((8, 9, 12))
    below = lerp(hor * 0.3, ground, np.clip(-E / 6, 0, 1)[..., None])
    sky = np.where((E < 0)[..., None], below, sky)
    save_rgb(np.clip(sky, 0, 1), "T_INF_Sky_Pano.png")


def write_skyline():
    """Distant skyline silhouette card, 4096 x 1024, alpha. Lit window specks, red aviation lights."""
    W, H = 4096, 1024
    r = np.random.default_rng(211)
    a = np.zeros((H, W)); c = np.zeros((H, W, 3))
    x = 0
    while x < W:
        w = int(r.integers(40, 170)); h = int(r.integers(160, 900))
        layer = r.random()
        shade = 0.035 + layer * 0.04
        a[H - h:, x:x + w] = 1.0
        c[H - h:, x:x + w] = (shade * 0.9, shade * 0.95, shade * 1.15)
        # windows
        wins = r.random((h // 9, w // 6)) < (0.08 + 0.12 * r.random())
        ys, xs = np.nonzero(wins)
        for yy, xx in zip(ys, xs):
            py = H - h + yy * 9 + 3; px = x + xx * 6 + 2
            if py < H - 4:
                warm = r.random() < 0.6
                c[py:py + 3, px:px + 2] = (0.95, 0.78, 0.55) if warm else (0.7, 0.82, 0.95)
        if h > 650 and r.random() < 0.7:
            c[H - h:H - h + 5, x + w // 2 - 2:x + w // 2 + 3] = (1.0, 0.1, 0.08)
        x += w + int(r.integers(-20, 30))
    save_rgba(np.concatenate([c, a[..., None]], -1), "T_INF_Skyline.png")


def write_grid():
    n = 512
    y, x = np.mgrid[0:n, 0:n].astype(float)
    g = n / 8
    line = np.clip(1.0 - np.minimum(np.minimum(x % g, g - x % g), np.minimum(y % g, g - y % g)) / 2.0, 0, 1)
    fine = np.clip(1.0 - np.minimum(np.minimum(x % (g / 4), g / 4 - x % (g / 4)), np.minimum(y % (g / 4), g / 4 - y % (g / 4))) / 1.0, 0, 1) * 0.25
    a = np.clip(line + fine, 0, 1)
    rgb = np.stack([a * 0.55 + 0.45, a * 0.85 + 0.15, np.ones_like(a)], -1)
    save_rgba(np.concatenate([rgb, a[..., None]], -1), "T_INF_VRGrid.png")


def write_beam():
    """Scanner beam / laser gradient (horizontal stripe, alpha)."""
    w, h = 256, 64
    y = (np.arange(h) + 0.5) / h
    prof = np.exp(-((y - 0.5) / 0.08) ** 2) + 0.35 * np.exp(-((y - 0.5) / 0.25) ** 2)
    a = np.clip(np.repeat(prof[:, None], w, 1), 0, 1)
    rgb = np.ones((h, w, 3))
    save_rgba(np.concatenate([rgb, a[..., None]], -1), "T_INF_Beam.png")


if __name__ == "__main__":
    write_asphalt(); write_road(); write_sidewalk(); write_ceramic(); write_graphite()
    write_facade("A", 4, 4, (34, 36, 40), (58, 60, 64), (30, 42, 58), 0.42, 401)
    write_facade("B", 6, 5, (196, 198, 200), (176, 178, 182), (26, 34, 46), 0.30, 409)
    write_corrugated("Container", 1024, 18, (58, 60, 64), False, stencil="TSUKASA LOGISTICS  //  TSK-0713", stripe=(0.82, 0.86))
    write_corrugated("Shutter", 1024, 14, (120, 122, 126), True)
    write_signs(); write_sky(); write_skyline(); write_grid(); write_beam()
