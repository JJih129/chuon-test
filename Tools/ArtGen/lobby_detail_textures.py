"""Level_Facility HQ lobby DETAIL kit textures -- numpy + PIL, deterministic.

  python Tools/ArtGen/lobby_detail_textures.py [out_dir]

Writes Assets/Art/ChuonPolish/Level_Facility/Detail/Textures/T_LFD_Atlas_{Albedo,Normal,Mask,Emission}.png (2048^2).
Cell rects = lobby_detail_layout.ATLAS (shared with lobby_detail_blender.py). Generic surfaces (ceramic, graphite, steel,
light strips, hazard, louvres, dark glass) are NOT here: the kit reuses the tutorial trim sheet T_INFD_Trim (M_INFD_Trim)
and the slogan atlas T_INF_Signs (M_INF_Signs). This atlas only carries lobby-specific content: wayfinding / floor signs,
lift indicators, holo directory, TSUKASA wall-screen content, emblem floor inlay, stone inlay field, corridor runner,
stylised foliage, wall-wash gradient and a hangar warning plate.
Mask = URP packed: R metallic, G occlusion, B 0, A smoothness (same convention as T_INFD_Trim_Mask).
Colour language (Docs/LevelDesign_TsukasaFacility.md): red = TSUKASA / danger, amber = interaction / passage (lift),
cyan = UI / hologram information only.
"""
import os, sys, math
import numpy as np
from PIL import Image, ImageDraw, ImageFont, ImageFilter

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
from lobby_detail_layout import ATLAS, ATLAS_SIZE  # noqa: E402

ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
OUT = os.path.abspath(sys.argv[1]) if len(sys.argv) > 1 else os.path.join(ROOT, "Assets", "Art", "ChuonPolish", "Level_Facility", "Detail", "Textures")
os.makedirs(OUT, exist_ok=True)
FONT_KO = r"C:\Windows\Fonts\malgunbd.ttf"
FONT_KO_L = r"C:\Windows\Fonts\malgun.ttf"
FONT_LAT = r"C:\Windows\Fonts\bahnschrift.ttf"
LOGO = os.path.join(ROOT, "Assets", "Art", "ChuonPolish", "Level_Facility", "Textures", "T_LF_Holo_TsukasaLogo.png")
N = ATLAS_SIZE
RED = (205, 22, 30)
AMBER = (240, 150, 30)
CYAN = (90, 210, 235)
WHITE = (236, 237, 240)


def font(path, size):
    try:
        return ImageFont.truetype(path, size)
    except OSError:
        return ImageFont.truetype(FONT_LAT, size)


def value_noise(w, h, cells, seed):
    r = np.random.default_rng(seed)
    lat = r.random((cells + 1, cells + 1)).astype(np.float32)
    ys = np.linspace(0, cells, h, endpoint=False); xs = np.linspace(0, cells, w, endpoint=False)
    y0 = np.floor(ys).astype(int); x0 = np.floor(xs).astype(int)
    fy = (ys - y0)[:, None]; fx = (xs - x0)[None, :]
    fy = fy * fy * (3 - 2 * fy); fx = fx * fx * (3 - 2 * fx)
    a = lat[y0][:, x0]; b = lat[y0][:, x0 + 1]; c = lat[y0 + 1][:, x0]; d = lat[y0 + 1][:, x0 + 1]
    return (a * (1 - fx) + b * fx) * (1 - fy) + (c * (1 - fx) + d * fx) * fy


def fbm(w, h, base, octaves, seed):
    acc = np.zeros((h, w), np.float32); amp = 1.0; tot = 0.0
    for o in range(octaves):
        acc += value_noise(w, h, base * (2 ** o), seed + o * 31) * amp; tot += amp; amp *= 0.5
    return acc / tot


def blur(a, r):
    im = Image.fromarray(np.clip(a * 255, 0, 255).astype(np.uint8))
    return np.asarray(im.filter(ImageFilter.GaussianBlur(r))).astype(np.float32) / 255.0


def normal_from_height(h, strength):
    dx = (np.roll(h, -1, 1) - np.roll(h, 1, 1)) * strength
    dy = (np.roll(h, -1, 0) - np.roll(h, 1, 0)) * strength
    nz = np.ones_like(h)
    l = np.sqrt(dx * dx + dy * dy + nz * nz)
    return np.stack([-dx / l * 0.5 + 0.5, dy / l * 0.5 + 0.5, nz / l * 0.5 + 0.5], -1)


class Atlas:
    def __init__(self):
        self.alb = np.zeros((N, N, 3), np.float32) + 0.05
        self.em = np.zeros((N, N, 3), np.float32)
        self.h = np.zeros((N, N), np.float32)
        self.met = np.zeros((N, N), np.float32)
        self.ao = np.ones((N, N), np.float32)
        self.sm = np.full((N, N), 0.5, np.float32)

    def put(self, key, alb=None, em=None, h=None, met=None, ao=None, sm=None):
        x, y, w, hh = ATLAS[key]
        s = (slice(y, y + hh), slice(x, x + w))
        for arr, v in ((self.alb, alb), (self.em, em), (self.h, h), (self.met, met), (self.ao, ao), (self.sm, sm)):
            if v is None:
                continue
            if isinstance(v, Image.Image):
                v = np.asarray(v.convert("RGB" if arr.ndim == 3 else "L")).astype(np.float32) / 255.0
            arr[s] = v

    def save(self):
        def w(a, name, mode):
            Image.fromarray(np.clip(a * 255 + 0.5, 0, 255).astype(np.uint8), mode).save(os.path.join(OUT, name)); print("wrote", name)
        w(self.alb, "T_LFD_Atlas_Albedo.png", "RGB")
        w(normal_from_height(self.h, 2.0), "T_LFD_Atlas_Normal.png", "RGB")
        w(np.stack([self.met, self.ao, np.zeros_like(self.ao), self.sm], -1), "T_LFD_Atlas_Mask.png", "RGBA")
        w(np.clip(self.em, 0, 1), "T_LFD_Atlas_Emission.png", "RGB")


def canvas(key, bg=(18, 19, 22)):
    _, _, w, h = ATLAS[key]
    return Image.new("RGB", (w, h), bg), Image.new("RGB", (w, h), (0, 0, 0))


def dim(c, k):
    return tuple(int(v * k) for v in c)


def text_both(da, de, xy, s, f, col, anchor="la", ka=0.72):
    da.text(xy, s, font=f, fill=dim(col, ka), anchor=anchor)
    de.text(xy, s, font=f, fill=col, anchor=anchor)


def fit(da, s, path, size, maxw, minsize=14):
    f = font(path, size)
    while da.textlength(s, font=f) > maxw and size > minsize:
        size -= 2; f = font(path, size)
    return f


def logo_mask(size):
    if not os.path.exists(LOGO):
        m = Image.new("L", (size, size), 0); ImageDraw.Draw(m).ellipse([size * 0.2, size * 0.2, size * 0.8, size * 0.8], outline=255, width=size // 14)
        return m
    return Image.open(LOGO).convert("L").resize((size, size), Image.LANCZOS)


def arrow(d, cx, cy, s, direction, fill):
    if direction == "r":
        pts = [(cx - s, cy - s * 0.18), (cx + s * 0.1, cy - s * 0.18), (cx + s * 0.1, cy - s * 0.6), (cx + s, cy), (cx + s * 0.1, cy + s * 0.6), (cx + s * 0.1, cy + s * 0.18), (cx - s, cy + s * 0.18)]
    elif direction == "l":
        pts = [(2 * cx - x, y) for x, y in arrow_pts(cx, cy, s)]
    elif direction == "d":
        pts = [(cx + (y - cy), cy + (x - cx)) for x, y in arrow_pts(cx, cy, s)]
    else:
        pts = [(cx - (y - cy), cy - (x - cx)) for x, y in arrow_pts(cx, cy, s)]
    d.polygon(pts, fill=fill)


def arrow_pts(cx, cy, s):
    return [(cx - s, cy - s * 0.18), (cx + s * 0.1, cy - s * 0.18), (cx + s * 0.1, cy - s * 0.6), (cx + s, cy), (cx + s * 0.1, cy + s * 0.6), (cx + s * 0.1, cy + s * 0.18), (cx - s, cy + s * 0.18)]


# ------------------------------------------------------------------ signage
def wayfinding(A, key, ko, en, accent, arrows):
    a, e = canvas(key, (226, 227, 229))
    da, de = ImageDraw.Draw(a), ImageDraw.Draw(e)
    w, h = a.size
    # ceramic plate, graphite pictogram block on the left, accent bar
    da.rectangle([0, 0, w - 1, h - 1], outline=(150, 152, 156), width=4)
    da.rectangle([18, 18, 230, h - 19], fill=(28, 29, 33))
    da.rectangle([244, 30, 252, h - 31], fill=dim(accent, 0.8)); de.rectangle([244, 30, 252, h - 31], fill=accent)
    for i, d in enumerate(arrows):
        arrow(da, 124, h // 2 if len(arrows) == 1 else 80 + i * 96, 62 if len(arrows) == 1 else 40, d, (240, 240, 242))
        arrow(de, 124, h // 2 if len(arrows) == 1 else 80 + i * 96, 62 if len(arrows) == 1 else 40, d, (200, 200, 204))
    fk = fit(da, ko, FONT_KO, 76, w - 310)
    fl = fit(da, en, FONT_LAT, 34, w - 310)
    da.text((282, 58), ko, font=fk, fill=(24, 25, 28))
    da.text((284, 168), en, font=fl, fill=(88, 90, 96))
    de.rectangle([282, 222, 282 + int(da.textlength(en, font=fl)), 226], fill=dim(accent, 0.9)); da.rectangle([282, 222, 282 + int(da.textlength(en, font=fl)), 226], fill=accent)
    A.put(key, alb=a, em=e, sm=0.62, h=0.0)


def floor_sign(A, key, big, ko, en, accent):
    a, e = canvas(key, (30, 31, 35))
    da, de = ImageDraw.Draw(a), ImageDraw.Draw(e)
    w, h = a.size
    da.rectangle([0, 0, w - 1, h - 1], outline=(70, 72, 78), width=6)
    fb = font(FONT_LAT, 250)
    text_both(da, de, (w // 2, 200), big, fb, WHITE, "mm", 0.9)
    da.rectangle([60, 340, w - 60, 348], fill=dim(accent, 0.8)); de.rectangle([60, 340, w - 60, 348], fill=accent)
    text_both(da, de, (w // 2, 400), ko, font(FONT_KO, 54), WHITE, "mm")
    text_both(da, de, (w // 2, 462), en, font(FONT_LAT, 30), (150, 152, 158), "mm")
    A.put(key, alb=a, em=e, sm=0.7)


def indicator(A, key, line1, line2, col, down=False):
    a, e = canvas(key, (8, 8, 10))
    da, de = ImageDraw.Draw(a), ImageDraw.Draw(e)
    w, h = a.size
    # dot-matrix look: draw text then mask with a dot grid
    t = Image.new("L", (w, h), 0); dt = ImageDraw.Draw(t)
    f1 = fit(dt, line1, FONT_LAT, 110, w - 60 - (90 if down else 0))
    tw = dt.textlength(line1, font=f1)
    cx = w // 2 - (45 if down else 0)
    dt.text((cx, 92), line1, font=f1, fill=255, anchor="mm")
    if down:  # descending-car triangle (glyph not in the font)
        x0 = cx + tw / 2 + 30
        dt.polygon([(x0, 60), (x0 + 70, 60), (x0 + 35, 124)], fill=255)
    dt.text((w // 2, 200), line2, font=font(FONT_KO, 46), fill=255, anchor="mm")
    m = np.asarray(t).astype(np.float32) / 255.0
    yy, xx = np.mgrid[0:h, 0:w]
    dots = (((xx % 6) < 4) & ((yy % 6) < 4)).astype(np.float32)
    lit = m * dots
    em = lit[..., None] * np.array(col, np.float32)[None, None] / 255.0 + dots[..., None] * 0.02
    al = np.full((h, w, 3), 0.03, np.float32) + lit[..., None] * np.array(col, np.float32) / 255.0 * 0.35
    A.put(key, alb=al, em=em, sm=0.92, met=0.0)


def directory(A):
    key = "directory"
    a, e = canvas(key, (6, 12, 16))
    da, de = ImageDraw.Draw(a), ImageDraw.Draw(e)
    w, h = a.size
    lg = logo_mask(120)
    e.paste(Image.new("RGB", lg.size, CYAN), ((w - 120) // 2, 22), lg)
    a.paste(Image.new("RGB", lg.size, dim(CYAN, 0.3)), ((w - 120) // 2, 22), lg)
    text_both(da, de, (w // 2, 176), "층별 안내 · DIRECTORY", font(FONT_KO, 17), (170, 225, 240), "mm", 0.3)
    rows = [("60F", "경영진", "EXECUTIVE"), ("10F", "프로젝트 시리즈", "PROJECT SERIES"), ("2-9F", "연구동", "R&D"),
            ("1F", "로비 · 안내", "LOBBY"), ("B4", "격납고", "HANGAR")]
    y = 226
    for fl_, ko, en in rows:
        col = RED if fl_ == "B4" else CYAN
        de.line([16, y - 8, w - 16, y - 8], fill=dim(CYAN, 0.35), width=1)
        text_both(da, de, (18, y), fl_, font(FONT_LAT, 26), col, "la", 0.3)
        text_both(da, de, (92, y), ko, font(FONT_KO, 19), (200, 236, 245), "la", 0.3)
        text_both(da, de, (92, y + 24), en, font(FONT_LAT, 14), dim(CYAN, 0.8), "la", 0.3)
        y += 54
    text_both(da, de, (w // 2, h - 22), "RESTRICTED · B4", font(FONT_LAT, 16), RED, "mm", 0.3)
    al = np.asarray(a).astype(np.float32) / 255.0
    yy = np.arange(h)[:, None, None]
    scan = ((yy % 4) < 2) * 0.12 + 0.88
    em = np.asarray(e).astype(np.float32) / 255.0 * scan
    A.put(key, alb=al, em=em, sm=0.93)


def strips(A):
    _, _, w, h = ATLAS["halo_cyan"]
    t = np.linspace(-1, 1, w)[None, :] * np.ones((h, 1))
    prof = np.exp(-(t / 0.45) ** 2)
    A.put("halo_cyan", alb=prof[..., None] * np.array([0.25, 0.7, 0.8]), em=prof[..., None] * np.array(CYAN) / 255.0, sm=0.9)
    _, _, w, h = ATLAS["screen_off"]
    g = np.linspace(0.06, 0.02, h)[:, None, None] * np.ones((1, w, 3))
    A.put("screen_off", alb=g * np.array([0.9, 1.0, 1.15]), sm=0.96, met=0.1)
    # volt: red emissive pin line (TSUKASA accent) on graphite
    _, _, w, h = ATLAS["volt"]
    prof = np.exp(-(np.linspace(-1, 1, w)[None, :] * np.ones((h, 1)) / 0.3) ** 2)
    A.put("volt", alb=0.06 + prof[..., None] * np.array([0.5, 0.03, 0.04]), em=prof[..., None] * np.array([1.0, 0.07, 0.06]), sm=0.7)
    # wall wash: ceramic albedo, emission strongest at the bottom (just above a cove), fading up
    _, _, w, h = ATLAS["wash"]
    v = np.linspace(0, 1, h)[:, None] * np.ones((1, w))  # row 0 = top
    em = (v ** 2.2)[..., None] * np.array([1.0, 0.97, 0.92]) * 0.75
    A.put("wash", alb=np.full((h, w, 3), 0.8) * np.array([0.99, 0.99, 0.965]), em=em, sm=0.6)


# ------------------------------------------------------------------ wall screens
def screen_ad(A):
    key = "screen_ad"
    a, e = canvas(key, (16, 17, 20))
    da, de = ImageDraw.Draw(a), ImageDraw.Draw(e)
    w, h = a.size
    # ceramic product stage + capsule silhouette (Project Series), soft white key light
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)
    glow = np.exp(-(((xx - w * 0.68) / (w * 0.32)) ** 2 + ((yy - h * 0.55) / (h * 0.6)) ** 2))
    base = 0.05 + glow * 0.32
    cap = (((xx - w * 0.68) / 120) ** 2 + ((yy - h * 0.52) / 220) ** 2) < 1.0
    rim = (((xx - w * 0.68) / 128) ** 2 + ((yy - h * 0.52) / 228) ** 2 < 1.0) & ~cap
    img = np.stack([base, base, base * 1.04], -1)
    img[cap] = np.array([0.82, 0.83, 0.85]) * (0.6 + 0.4 * (1 - (yy[cap] - h * 0.3) / (h * 0.5)).clip(0, 1))[:, None]
    img[rim] = np.array([0.9, 0.12, 0.12])
    seam = cap & (np.abs(yy - h * 0.52) < 2)
    img[seam] = np.array([0.95, 0.15, 0.15])
    plinth = (np.abs(xx - w * 0.68) < 200) & (yy > h * 0.86) & (yy < h * 0.9)
    img[plinth] = 0.75
    em = img * 0.95
    text = Image.new("RGB", (w, h), (0, 0, 0)); dt = ImageDraw.Draw(text)
    dt.text((60, 70), "PROJECT", font=font(FONT_LAT, 96), fill=(240, 240, 242))
    dt.text((60, 170), "SERIES", font=font(FONT_LAT, 96), fill=(240, 240, 242))
    dt.rectangle([62, 290, 330, 296], fill=RED)
    dt.text((62, 318), "인류의 다음 단계를 설계합니다", font=font(FONT_KO, 34), fill=(225, 226, 230))
    dt.text((62, 370), "ENGINEERING THE NEXT HUMAN", font=font(FONT_LAT, 26), fill=(150, 152, 158))
    lg = logo_mask(70)
    text.paste(Image.new("RGB", lg.size, RED), (62, 470), lg)
    dt.text((146, 492), "TSUKASA CORPORATION", font=font(FONT_LAT, 26), fill=(220, 220, 224))
    t = np.asarray(text).astype(np.float32) / 255.0
    tm = t.max(-1, keepdims=True)
    img = img * (1 - tm) + t
    em = em * (1 - tm) + t
    A.put(key, alb=img * 0.55, em=em, sm=0.94, met=0.0)


def screen_news(A):
    key = "screen_news"
    a, e = canvas(key, (10, 12, 15))
    da, de = ImageDraw.Draw(a), ImageDraw.Draw(e)
    w, h = a.size
    de.rectangle([0, 0, w, 70], fill=(150, 16, 22)); da.rectangle([0, 0, w, 70], fill=(110, 12, 16))
    lg = logo_mask(54)
    e.paste(Image.new("RGB", lg.size, WHITE), (16, 8), lg)
    text_both(da, de, (84, 35), "TSUKASA NETWORK  ·  기업 뉴스", font(FONT_KO, 32), WHITE, "lm")
    text_both(da, de, (w - 20, 35), "LIVE", font(FONT_LAT, 30), WHITE, "rm")
    heads = [("시민 안전 지수, 역대 최고치 경신", "CIVIC SAFETY INDEX AT RECORD HIGH"),
             ("드론 순찰망 확대 — 07 구역", "PATROL DRONE GRID EXPANDED · SECTOR 07"),
             ("레지스탕스 잔당 소탕 완료", "RESISTANCE CELL NEUTRALISED"),
             ("프로젝트 시리즈 2차 공개 예정", "PROJECT SERIES · PHASE II UNVEILING")]
    y = 100
    for i, (ko, en) in enumerate(heads):
        col = RED if i == 2 else WHITE
        de.rectangle([24, y + 6, 32, y + 52], fill=dim(col, 0.9)); da.rectangle([24, y + 6, 32, y + 52], fill=dim(col, 0.6))
        text_both(da, de, (50, y), ko, font(FONT_KO, 36), col, "la", 0.6)
        text_both(da, de, (52, y + 46), en, font(FONT_LAT, 20), (140, 144, 150), "la", 0.6)
        y += 92
    # stock chart panel
    x0, y0, x1, y1 = 700, 100, w - 24, 360
    de.rectangle([x0, y0, x1, y1], outline=(60, 64, 70), width=2)
    text_both(da, de, (x0 + 14, y0 + 10), "TSK  +3.42%", font(FONT_LAT, 30), (120, 230, 160), "la", 0.5)
    r = np.random.default_rng(7)
    pts = []; v = 0.0
    for i in range(40):
        v += r.normal(0.6, 1.4); pts.append((x0 + 14 + i * (x1 - x0 - 28) / 39, y1 - 30 - v * 3.4))
    de.line(pts, fill=(120, 230, 160), width=4); da.line(pts, fill=(60, 120, 80), width=4)
    # ticker
    de.rectangle([0, h - 64, w, h], fill=(26, 28, 32)); da.rectangle([0, h - 64, w, h], fill=(20, 21, 24))
    text_both(da, de, (16, h - 32), "감시는 보호입니다  ·  SURVEILLANCE IS PROTECTION  ·  신분증 미소지 시 출입 금지  ·  ORDER IS SAFETY",
              font(FONT_KO, 26), (215, 216, 220), "lm", 0.6)
    al = np.asarray(a).astype(np.float32) / 255.0
    em = np.asarray(e).astype(np.float32) / 255.0
    A.put(key, alb=al, em=em, sm=0.94)


# ------------------------------------------------------------------ floor / material cells
def emblem_inlay(A):
    key = "emblem_inlay"
    _, _, w, h = ATLAS[key]
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)
    rr = np.sqrt((xx - w / 2) ** 2 + (yy - h / 2) ** 2) / (w / 2)
    n = fbm(w, h, 6, 4, 41)
    stone = 0.11 + (n - 0.5) * 0.05
    alb = np.stack([stone * 0.97, stone, stone * 1.04], -1)
    ring1 = (np.abs(rr - 0.96) < 0.015); ring2 = (np.abs(rr - 0.86) < 0.008)
    lg = np.asarray(logo_mask(int(w * 0.62))).astype(np.float32) / 255.0
    L = np.zeros((h, w), np.float32); o = (w - lg.shape[1]) // 2; L[o:o + lg.shape[0], o:o + lg.shape[1]] = lg
    L = np.clip((L - 0.35) * 2.2, 0, 1)
    metal = np.clip(ring1 + L, 0, 1)
    alb = alb * (1 - metal[..., None]) + metal[..., None] * np.array([0.62, 0.62, 0.64])
    redl = ring2.astype(np.float32)
    alb = alb * (1 - redl[..., None]) + redl[..., None] * np.array([0.55, 0.04, 0.05])
    em = redl[..., None] * np.array([1.0, 0.06, 0.05]) + np.clip(blur(L, 1.0) - L, 0, 1)[..., None] * np.array([0.9, 0.1, 0.08]) * 0.8
    outside = rr > 1.0
    alb[outside] = 0.0; em[outside] = 0.0
    A.put(key, alb=alb, em=em, h=metal * 0.6 - redl * 0.3 + n * 0.05, met=metal * 0.9, sm=0.82 + metal * 0.08, ao=1 - ring2 * 0.3)


def stone_field(A):
    key = "stone_field"
    _, _, w, h = ATLAS[key]   # 3 x 3 m, 1 m tiles
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)
    t = w / 3.0
    gx = np.minimum(xx % t, t - xx % t); gy = np.minimum(yy % t, t - yy % t)
    grout = np.clip(1 - np.minimum(gx, gy) / 1.6, 0, 1)
    n = fbm(w, h, 5, 5, 61); vein = np.clip(1 - np.abs(fbm(w, h, 3, 4, 67) - 0.5) * 30, 0, 1) * 0.25
    tid = ((xx // t) * 7 + (yy // t) * 3) % 4
    base = 0.62 + tid * 0.012 + (n - 0.5) * 0.06 - vein * 0.08
    border = (np.minimum(np.minimum(xx, w - 1 - xx), np.minimum(yy, h - 1 - yy)) < 10)
    pin = (np.abs(np.minimum(np.minimum(xx, w - 1 - xx), np.minimum(yy, h - 1 - yy)) - 14) < 1.2)
    alb = np.stack([base * 0.99, base * 0.99, base * 0.975], -1) * (1 - grout[..., None] * 0.55)
    alb[border] = np.array([0.08, 0.082, 0.09])
    alb[pin] = np.array([0.55, 0.05, 0.06])
    em = pin[..., None] * np.array([0.8, 0.05, 0.04])
    A.put(key, alb=alb, em=em, h=-grout * 0.5 - border * 0.1, sm=np.where(border, 0.55, 0.78 - grout * 0.3), ao=1 - grout * 0.4)


def runner(A):
    key = "runner"
    _, _, w, h = ATLAS[key]   # 2.0 x 3.6 m, stretched: graphite carpet tile with red edge lines
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)
    n = fbm(w, h, 64, 2, 71)
    weave = (np.sin(xx * 1.4) * np.sin(yy * 1.4)) * 0.01
    base = 0.07 + (n - 0.5) * 0.03 + weave
    alb = np.stack([base, base, base * 1.06], -1)
    ex = np.minimum(xx, w - 1 - xx)
    edge = ex < 18
    line = np.abs(ex - 30) < 2.5
    alb[edge] = np.array([0.16, 0.165, 0.175])
    alb[line] = np.array([0.6, 0.05, 0.06])
    em = line[..., None] * np.array([0.9, 0.06, 0.05])
    A.put(key, alb=alb, em=em, h=n * 0.3 - edge * 0.2, sm=np.where(edge, 0.5, 0.18))


def foliage(A):
    key = "foliage"
    _, _, w, h = ATLAS[key]
    n1 = fbm(w, h, 12, 4, 81); n2 = fbm(w, h, 40, 3, 83)
    leaf = np.clip((n2 - 0.35) * 2.5, 0, 1)
    g = 0.5 * n1 + 0.5 * leaf
    # muted deep green -> slightly cool highlights (stylised, desaturated to sit in a graphite / ceramic lobby)
    c0 = np.array([0.035, 0.07, 0.05]); c1 = np.array([0.16, 0.26, 0.17]); c2 = np.array([0.32, 0.42, 0.30])
    alb = c0 + (c1 - c0) * g[..., None] + (c2 - c1) * np.clip(g - 0.6, 0, 1)[..., None] * 2
    A.put(key, alb=alb, h=g, sm=0.35 + leaf * 0.15, ao=0.55 + g * 0.45)


def warn_plate(A):
    key = "warn_plate"
    a, e = canvas(key, (210, 150, 25))
    da, de = ImageDraw.Draw(a), ImageDraw.Draw(e)
    w, h = a.size
    for i in range(-h, w + h, 40):
        da.polygon([(i, 0), (i + 20, 0), (i + 20 - h * 0.0, h)], fill=(210, 150, 25))
    da.rectangle([0, 0, w - 1, 70], fill=(20, 20, 22))
    for i in range(-80, w, 36):
        da.polygon([(i, 0), (i + 18, 0), (i + 18 + 70, 70), (i + 70, 70)], fill=(215, 150, 20))
    da.rectangle([10, 86, w - 11, h - 11], fill=(232, 232, 228))
    da.polygon([(w // 2, 110), (w // 2 + 80, 250), (w // 2 - 80, 250)], fill=(20, 20, 22))
    da.polygon([(w // 2, 132), (w // 2 + 60, 238), (w // 2 - 60, 238)], fill=(215, 150, 20))
    da.text((w // 2, 210), "!", font=font(FONT_LAT, 80), fill=(20, 20, 22), anchor="mm")
    da.text((w // 2, 300), "고압 주의", font=font(FONT_KO, 40), fill=(20, 20, 22), anchor="mm")
    da.text((w // 2, 346), "HANGAR B4", font=font(FONT_LAT, 30), fill=(150, 18, 24), anchor="mm")
    da.text((w // 2, 384), "HIGH VOLTAGE", font=font(FONT_LAT, 24), fill=(40, 40, 44), anchor="mm")
    al = np.asarray(a).astype(np.float32) / 255.0
    A.put(key, alb=al, sm=0.45, h=fbm(w, h, 32, 2, 91) * 0.15)


def main():
    A = Atlas()
    wayfinding(A, "sign_lift", "전용 엘리베이터 · B4 격납고", "EXECUTIVE LIFT  ·  B4 HANGAR", AMBER, ["d"])
    wayfinding(A, "sign_showroom", "프로젝트 시리즈 전시관", "PROJECT SERIES SHOWROOM", RED, ["l", "r"])
    wayfinding(A, "sign_reception", "안내 데스크", "RECEPTION  ·  VISITOR ID REQUIRED", WHITE, ["d"])
    wayfinding(A, "sign_skydeck", "스카이덱 전망 구역", "SKYDECK OBSERVATION  ·  LEVEL B4 BELOW", RED, ["d"])
    floor_sign(A, "floor_1F", "1F", "본사 로비", "HEADQUARTERS LOBBY", RED)
    floor_sign(A, "floor_B4", "B4", "격납고", "HANGAR  ·  RESTRICTED", AMBER)
    indicator(A, "ind_lock", "LOCKDOWN", "운행 중지", (255, 40, 30))
    indicator(A, "ind_b4", "B4", "격납고 직통", (255, 150, 30), down=True)
    directory(A)
    strips(A)
    screen_ad(A)
    screen_news(A)
    emblem_inlay(A)
    stone_field(A)
    runner(A)
    foliage(A)
    warn_plate(A)
    A.save()


if __name__ == "__main__":
    main()
