"""Bake procedural world-space sci-fi surfacing into the arena atlas UV layouts.
Outputs into Assets/Art/ChuonPolish/MainScene/Textures."""
import collections, os, sys
import numpy as np
from PIL import Image, ImageDraw, ImageFont

SCR = os.path.dirname(os.path.abspath(__file__))
OUT = os.environ.get("OUT", r"C:/Users/ikh33/source/chuon-test/Assets/Art/ChuonPolish/MainScene/Textures")
os.makedirs(OUT, exist_ok=True)
FONT = r"C:/Windows/Fonts/bahnschrift.ttf"

D = collections.defaultdict(list); cur = None
for l in open(os.path.join(SCR, 'arena_uv.txt')):
    if l.startswith('M '): cur = l.split()[1]; continue
    D[cur].append(list(map(float, l.split())))

# ---------------- noise helpers ----------------
def h2(ix, iy, seed=0.0):
    return np.modf(np.abs(np.sin(ix * 127.1 + iy * 311.7 + seed * 74.7) * 43758.5453))[0]

def vnoise(x, y, seed=0.0):
    ix = np.floor(x); iy = np.floor(y); fx = x - ix; fy = y - iy
    fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy)
    a = h2(ix, iy, seed); b = h2(ix + 1, iy, seed); c = h2(ix, iy + 1, seed); d = h2(ix + 1, iy + 1, seed)
    return (a * (1 - fx) + b * fx) * (1 - fy) + (c * (1 - fx) + d * fx) * fy

def fbm(x, y, oct=4, seed=0.0):
    s = 0; a = 0.5; t = 0
    for i in range(oct):
        s = s + a * vnoise(x, y, seed + i * 13.0); t += a; x = x * 2.03 + 1.7; y = y * 2.03 - 3.1; a *= 0.5
    return s / t

def sstep(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0, 1); return t * t * (3 - 2 * t)

def seamdist(c, pitch, off=0.0):
    g = np.mod(c - off, pitch); return np.minimum(g, pitch - g)

def text_sampler(text, height_m, center_s, center_t, size_px=256):
    """returns f(s,t)->coverage for text laid out with +s right, +t up."""
    font = ImageFont.truetype(FONT, size_px)
    bb = font.getbbox(text)
    w, h = bb[2] - bb[0] + 20, bb[3] - bb[1] + 20
    im = Image.new('L', (w, h), 0); ImageDraw.Draw(im).text((10 - bb[0], 10 - bb[1]), text, font=font, fill=255)
    arr = np.asarray(im, np.float32) / 255.0
    mpp = height_m / h; wm = w * mpp
    s0 = center_s - wm / 2; t1 = center_t + height_m / 2
    def f(s, t):
        cx = ((s - s0) / mpp).astype(np.int32); cy = ((t1 - t) / mpp).astype(np.int32)
        ok = (cx >= 0) & (cx < w) & (cy >= 0) & (cy < h)
        out = np.zeros(s.shape, np.float32)
        out[ok] = arr[cy[ok], cx[ok]]
        return out
    return f

# ---------------- rasterizer ----------------
def raster(tris, S, pad=10):
    tid = np.full((S, S), -1, np.int32)
    M = np.zeros((len(tris), 3, 3)); N = np.zeros((len(tris), 3)); valid = np.zeros(len(tris), bool)
    for i, t in enumerate(tris):
        uv = np.array([[t[0], t[2], t[4]], [t[1], t[3], t[5]], [1, 1, 1]])
        P = np.array([t[9:12], t[12:15], t[15:18]]).T
        if abs(np.linalg.det(uv)) < 1e-12: continue
        M[i] = P @ np.linalg.inv(uv); N[i] = t[6:9]; valid[i] = True
        # pixel bbox
        us = np.array([t[0], t[2], t[4]]) * S; vs = (1 - np.array([t[1], t[3], t[5]])) * S
        c0, c1 = int(max(0, np.floor(us.min()))), int(min(S - 1, np.ceil(us.max())))
        r0, r1 = int(max(0, np.floor(vs.min()))), int(min(S - 1, np.ceil(vs.max())))
        cc, rr = np.meshgrid(np.arange(c0, c1 + 1), np.arange(r0, r1 + 1))
        pu = (cc + 0.5); pv = (rr + 0.5)
        (x0, x1, x2), (y0, y1, y2) = us, vs
        den = (y1 - y2) * (x0 - x2) + (x2 - x1) * (y0 - y2)
        if abs(den) < 1e-9: continue
        l0 = ((y1 - y2) * (pu - x2) + (x2 - x1) * (pv - y2)) / den
        l1 = ((y2 - y0) * (pu - x2) + (x0 - x2) * (pv - y2)) / den
        l2 = 1 - l0 - l1
        inside = (l0 >= -1e-4) & (l1 >= -1e-4) & (l2 >= -1e-4)
        sub = tid[r0:r1 + 1, c0:c1 + 1]
        sub[inside & (sub < 0)] = i
    # dilate for mip padding
    for _ in range(pad):
        m = tid < 0
        if not m.any(): break
        for dr, dc in ((0, 1), (0, -1), (1, 0), (-1, 0)):
            sh = np.roll(np.roll(tid, dr, 0), dc, 1)
            fill = m & (tid < 0) & (sh >= 0)
            tid[fill] = sh[fill]
    filled = tid >= 0
    t = np.where(filled, tid, 0)
    rr, cc = np.mgrid[0:S, 0:S]
    u = (cc + 0.5) / S; v = 1 - (rr + 0.5) / S
    W = np.stack([M[t, k, 0] * u + M[t, k, 1] * v + M[t, k, 2] for k in range(3)], -1).astype(np.float32)
    Nn = N[t].astype(np.float32)
    mppu = (np.linalg.norm(M[:, :, 0], axis=1) / S)[t].astype(np.float32)
    mppv = (np.linalg.norm(M[:, :, 1], axis=1) / S)[t].astype(np.float32)
    return filled, W, Nn, mppu, mppv

def to_normal(Hh, mppu, mppv, filled, strength=1.0):
    dhdc = (np.roll(Hh, -1, 1) - np.roll(Hh, 1, 1)) / (2 * np.maximum(mppu, 1e-5))
    dhdr = (np.roll(Hh, -1, 0) - np.roll(Hh, 1, 0)) / (2 * np.maximum(mppv, 1e-5))
    dx = -dhdc * strength; dy = dhdr * strength  # +v is up (row decreases)
    dx = np.clip(dx, -3, 3); dy = np.clip(dy, -3, 3)
    n = np.stack([dx, dy, np.ones_like(dx)], -1); n /= np.linalg.norm(n, axis=-1, keepdims=True)
    n[~filled] = (0, 0, 1)
    return n

def save(arr, name, mode='RGB'):
    a = np.clip(arr * 255 + 0.5, 0, 255).astype(np.uint8)
    Image.fromarray(a, mode).save(os.path.join(OUT, name)); print('saved', name, a.shape)

def lerp(a, b, t):
    t = np.asarray(t)[..., None] if np.ndim(t) else t
    return a + (b - a) * t

# ---------------- palette (sRGB) ----------------
# corporate-military palette: red accents (corporate), amber hazards (military), cold white light; no cyan/magenta
RED = np.array([0.95, 0.08, 0.06]); WHT = np.array([0.86, 0.89, 0.93])
CYAN = np.array([0.15, 0.85, 1.0]); MAG = np.array([1.0, 0.18, 0.78]); VIO = np.array([0.55, 0.25, 1.0])  # legacy, unused
AMB = np.array([0.95, 0.62, 0.08]); COOLW = np.array([0.88, 0.91, 0.95])

def shade_floor(fx, fz, decals=True):
    pid = h2(np.floor(fx / 2), np.floor(fz / 2), 3.0); pid2 = h2(np.floor(fx / 2), np.floor(fz / 2), 9.0)
    base = np.array([0.150, 0.152, 0.158])[None, :] * (0.86 + 0.22 * pid)[:, None]
    grime = fbm(fx * 0.22, fz * 0.22, 5, 1.0)
    fine = vnoise(fx * 9.0, fz * 9.0, 4.0) * 0.5 + vnoise(fx * 31, fz * 31, 5.0) * 0.5
    a = base * (0.78 + 0.3 * (1 - grime))[:, None] * (0.94 + 0.08 * fine)[:, None]
    mt = np.full(fx.shape, 0.72); sm = 0.50 - 0.18 * grime + 0.06 * fine
    hh = np.zeros(fx.shape)
    d = np.minimum(seamdist(fx, 2.0), seamdist(fz, 2.0))
    groove = 1 - sstep(0.012, 0.03, d)
    hh -= 0.004 * groove; a *= (1 - 0.6 * groove)[:, None]; sm -= 0.2 * groove
    # inner bevel edge highlight on panel
    bev = sstep(0.03, 0.045, d) * (1 - sstep(0.045, 0.07, d))
    a += 0.03 * bev[:, None]; hh += 0.0015 * sstep(0.03, 0.07, d)
    # heavy trim bands every 6m (x) / 8m (z)
    tr = np.minimum(seamdist(fx, 6.0, 3.0), 99)
    trim = 1 - sstep(0.06, 0.075, tr)
    a = lerp(a, np.array([0.23, 0.232, 0.24]), trim * 0.9); hh += 0.003 * trim; sm = sm * (1 - trim) + 0.62 * trim
    bolts = trim * (1 - sstep(0.012, 0.02, np.hypot(seamdist(fx, 6.0, 3.0), seamdist(fz, 0.5))))
    hh += 0.004 * bolts; a *= (1 - 0.4 * bolts)[:, None]
    # vent panels
    vent = (pid2 < 0.13) & (np.abs(fx) < 11) & (seamdist(fx, 2.0) > 0.25) & (seamdist(fz, 2.0) > 0.25)
    slot = seamdist(fz, 0.14) < 0.035
    vv = vent & slot
    a[vv] *= 0.25; hh[vv] -= 0.01; sm[vv] = 0.2
    a[vent & ~slot] *= 1.12
    # wet puddles (rainy cyberpunk)
    wet = sstep(0.60, 0.68, fbm(fx * 0.12 + 7, fz * 0.12 - 2, 4, 21.0))
    a *= (1 - 0.25 * wet)[:, None]; sm = sm * (1 - wet) + 0.9 * wet; mt = mt * (1 - 0.4 * wet)
    e = np.zeros((fx.size, 3))
    # service gutter grating next to walls
    gut = np.abs(fx) > 14.25
    gs = gut & (seamdist(fz, 0.1) < 0.03)
    a[gut] *= 0.55; a[gs] *= 0.3; hh[gs] -= 0.012; sm[gut] = 0.3; mt[gut] = 0.8
    gedge = 1 - sstep(0.02, 0.04, np.abs(np.abs(fx) - 14.25))
    a = lerp(a, np.array([0.26, 0.262, 0.27]), gedge); hh += 0.003 * gedge
    if not decals:
        return a, e, mt, np.clip(sm, 0.05, 0.95), hh
    r = np.hypot(fx, fz); ang = np.degrees(np.arctan2(fz, fx)) % 360
    # arena ring
    paint = np.zeros(fx.shape)
    band = (r > 7.45) & (r < 7.85)
    a[band] = a[band] * 0.45
    tick = band & (np.mod(ang, 5.0) < 0.9)
    paint[tick] = 0.55
    ring = 1 - sstep(0.025, 0.045, np.abs(r - 8.0))
    e += ring[:, None] * RED * 0.9
    arcs = (1 - sstep(0.02, 0.035, np.abs(r - 8.35))) * (seamdist(ang, 90.0, 45.0) < 9)
    e += arcs[:, None] * WHT * 0.45
    inner = 1 - sstep(0.03, 0.05, np.abs(r - 2.6))
    paint = np.maximum(paint, inner * 0.45)
    # cross-hair segments
    ch = ((np.abs(fx) < 0.04) | (np.abs(fz) < 0.04)) & (r > 3.0) & (r < 7.2) & (np.mod(r, 0.8) < 0.45)
    paint[ch] = np.maximum(paint[ch], 0.35)
    # lane boundary lines (thin cold white, dashed toward ends)
    lane = (1 - sstep(0.03, 0.05, np.abs(np.abs(fx) - 11.5))) * (fz > -17.0) * (fz < 21.0)
    e += lane[:, None] * WHT * 0.30
    lane2 = (1 - sstep(0.02, 0.035, np.abs(np.abs(fx) - 11.75))) * (np.mod(fz, 1.6) < 0.9) * (fz > -17.0) * (fz < 21.0)
    paint = np.maximum(paint, lane2 * 0.25)
    # hazard chevrons along side edges and elevator exit
    haz_side = (np.abs(fx) > 13.6) & (np.abs(fx) < 14.15)
    haz_exit = (fz > -17.45) & (fz < -16.95) & (np.abs(fx) < 13.6)
    haz = haz_side | haz_exit
    stripe = np.mod((np.abs(fx) + fz) / 0.6, 1.0) < 0.5
    wear = sstep(0.35, 0.55, fbm(fx * 1.3, fz * 1.3, 4, 33.0))
    hz_col = np.where(stripe[:, None], AMB[None, :] * 0.8, np.array([0.035, 0.035, 0.04])[None, :])
    k = haz & (wear > 0.15)
    a[k] = lerp(a[k], hz_col[k], 0.9 * wear[k]); mt[k] = 0.1; sm[k] = 0.35
    # stencils
    st1 = text_sampler("SECTOR D-07  //  CONTAINMENT FLOOR", 0.55, 0.0, -15.6)(fx, fz)
    st2 = text_sampler("TYPE-D", 2.2, 0.0, 17.2)(fx, fz)
    st3 = text_sampler("AUTHORIZED PERSONNEL ONLY", 0.4, 0.0, 15.5)(fx, fz)
    paint = np.maximum(paint, np.maximum(st1, st3) * 0.3)
    pw = sstep(0.3, 0.5, fbm(fx * 2.1, fz * 2.1, 3, 44.0))
    pp = paint * pw
    a = lerp(a, np.array([0.66, 0.66, 0.66]), pp); mt = mt * (1 - pp); sm = sm * (1 - pp * 0.4)
    # TYPE-D in corporate red paint w/ faint glow
    t2 = st2 * pw
    a = lerp(a, RED * 0.5, t2 * 0.85); e += (t2 * 0.08)[:, None] * RED
    return a, e, mt, np.clip(sm, 0.05, 0.95), hh


def bake_room(S=4096):
    filled, W, Nn, mpu, mpv = raster(D['Object001'], S)
    x, y, z = W[..., 0], W[..., 1], W[..., 2]
    nx, ny, nz = Nn[..., 0], Nn[..., 1], Nn[..., 2]
    floor = ny > 0.7; ceil = ny < -0.7; wall = ~floor & ~ceil
    alb = np.zeros((S, S, 3), np.float32); emi = np.zeros((S, S, 3), np.float32)
    met = np.zeros((S, S), np.float32); smo = np.zeros((S, S), np.float32); H = np.zeros((S, S), np.float32)

    # ===== FLOOR (hidden under overlay; plain) =====
    m = floor
    a, e, mt, sm, hh = shade_floor(x[m], z[m], False)
    alb[m] = a; emi[m] = e * 0; met[m] = mt; smo[m] = sm; H[m] = hh

    # ===== WALLS =====
    m = wall
    wx, wy, wz = x[m], y[m], z[m]; wnx, wnz = nx[m], nz[m]
    s = np.where(np.abs(wnx) > np.abs(wnz), np.where(wnx > 0, wz, -wz), np.where(wnz < 0, wx, -wx))
    a = np.tile(np.array([0.160, 0.163, 0.170]), (s.size, 1))
    grime = fbm(s * 0.18, wy * 0.18, 5, 2.0)
    a *= (0.8 + 0.28 * (1 - grime))[:, None]
    # streaks (vertical rain grime)
    streak = vnoise(s * 3.0, wy * 0.15, 8.0)
    a *= (0.9 + 0.12 * streak)[:, None]
    mt = np.full(s.shape, 0.55); sm = 0.42 - 0.15 * grime; hh = np.zeros(s.shape)
    rows = np.array([0.0, 1.0, 3.0, 3.4, 6.2, 10.0, 14.0, 20.0])
    dy = np.min(np.abs(wy[:, None] - rows[None, :]), axis=1)
    ds = seamdist(s, 4.0, 0.0)
    d = np.minimum(dy, ds)
    groove = 1 - sstep(0.015, 0.035, d)
    hh -= 0.006 * groove; a *= (1 - 0.6 * groove)[:, None]
    hh += 0.004 * sstep(0.06, 0.12, d)
    pid = h2(np.floor(s / 4.0), np.searchsorted(rows, wy), 5.0)
    a *= (0.88 + 0.2 * pid)[:, None]
    kick = wy < 1.0
    a[kick] = a[kick] * 0.55; mt[kick] = 0.7
    upper = wy > 10.0
    rib = seamdist(s, 2.0) < 0.15
    a[upper] *= 0.62; hh[upper & rib] += 0.01; a[upper & rib] *= 1.35
    # emissive strips
    e = np.zeros((s.size, 3))
    strip = (wy > 3.12) & (wy < 3.18)
    e[strip] += RED * 0.95; a[strip] = 0.1
    vstrip = (seamdist(s, 16.0, 4.0) < 0.03) & (wy > 3.5) & (wy < 9.6)
    e[vstrip] += WHT * 0.7; a[vstrip] = 0.1
    hi = (wy > 10.95) & (wy < 11.03)
    a[hi] *= 0.6  # former violet band -> dark recess
    # small status lights
    led = (np.hypot(seamdist(s, 4.0, 2.0), wy - 2.55) < 0.06)
    e[led] += np.where((h2(np.floor(s / 4.0), 1.0, 2.0) > 0.7)[led][:, None], AMB[None, :], (RED * 0.8)[None, :])
    # kick-plate hazard band on end walls
    endw = np.abs(wnz) > 0.7
    hzb = endw & (wy > 0.2) & (wy < 0.7)
    stripe = np.mod((s + wy) / 0.5, 1.0) < 0.5
    a[hzb] = np.where(stripe[hzb][:, None], AMB * 0.75, np.array([0.03, 0.03, 0.035]))
    alb[m] = a; emi[m] = e; met[m] = mt; smo[m] = np.clip(sm, 0.05, 0.95); H[m] = hh

    # ===== CEILING =====
    m = ceil
    cx_, cz_ = x[m], z[m]
    a = np.tile(np.array([0.07, 0.071, 0.075]), (cx_.size, 1)) * (0.85 + 0.3 * fbm(cx_ * 0.3, cz_ * 0.3, 3, 6.0))[:, None]
    d = np.minimum(seamdist(cx_, 3.0), seamdist(cz_, 3.0))
    groove = 1 - sstep(0.02, 0.05, d)
    a *= (1 - 0.5 * groove)[:, None]; hh = -0.006 * groove
    e = np.zeros((cx_.size, 3))
    lp = (seamdist(cx_, 8.0, 0.0) < 2.0) & (seamdist(cz_, 6.0, 0.0) < 0.35)
    e[lp] += COOLW * 0.75; a[lp] = 0.6
    alb[m] = a; emi[m] = e; met[m] = 0.4; smo[m] = 0.35; H[m] = hh

    nrm = to_normal(H, mpu, mpv, filled, 1.0)
    save(alb, 'T_Arena_Room_Albedo.png')  # walls/ceiling atlas
    save(nrm * 0.5 + 0.5, 'T_Arena_Room_Normal.png')
    mask = np.stack([met, np.zeros_like(met), np.zeros_like(met), smo], -1)
    Image.fromarray(np.clip(mask * 255 + .5, 0, 255).astype(np.uint8), 'RGBA').resize((2048, 2048), Image.LANCZOS).save(os.path.join(OUT, 'T_Arena_Room_Mask.png'))
    Image.fromarray(np.clip(emi * 255 + .5, 0, 255).astype(np.uint8), 'RGB').save(os.path.join(OUT, 'T_Arena_Room_Emission.png'))
    print('room done')

FX0, FX1, FZ0, FZ1 = -16.33, 16.33, -25.0, 24.26

def bake_floor(SW=2048, SH=4096):
    cc, rr = np.meshgrid(np.arange(SW), np.arange(SH))
    u = (cc + 0.5) / SW; v = 1 - (rr + 0.5) / SH
    fx = (FX0 + u * (FX1 - FX0)).ravel(); fz = (FZ0 + v * (FZ1 - FZ0)).ravel()
    a, e, mt, sm, hh = shade_floor(fx, fz, True)
    sh2 = (SH, SW)
    H = hh.reshape(sh2).astype(np.float32)
    mpu = np.full(sh2, (FX1 - FX0) / SW, np.float32); mpv = np.full(sh2, (FZ1 - FZ0) / SH, np.float32)
    nrm = to_normal(H, mpu, mpv, np.ones(sh2, bool), 1.0)
    save(a.reshape(SH, SW, 3), 'T_Arena_Floor_Albedo.png')
    save(nrm * 0.5 + 0.5, 'T_Arena_Floor_Normal.png')
    save(e.reshape(SH, SW, 3), 'T_Arena_Floor_Emission.png')
    mask = np.stack([mt, mt * 0, mt * 0, sm], -1).reshape(SH, SW, 4)
    Image.fromarray(np.clip(mask * 255 + .5, 0, 255).astype(np.uint8), 'RGBA').resize((SW // 2, SH // 2), Image.LANCZOS).save(os.path.join(OUT, 'T_Arena_Floor_Mask.png'))
    print('floor done')

def bake_pillar(S=2048):
    src = np.asarray(Image.open(r"C:/Users/ikh33/source/chuon-test/Assets/MapAsset/11/Object.png").convert('RGB').resize((S, S)), np.float32) / 255.0
    filled, W, Nn, mpu, mpv = raster(D['pillar001'] + D['elevator001'], S, pad=6)
    x, y, z = W[..., 0], W[..., 1], W[..., 2]
    lum = src.mean(-1)
    sat = src.max(-1) - src.min(-1)
    tint = np.array([0.60, 0.60, 0.62])
    alb = src * tint
    yellow = sat > 0.25
    alb[yellow] = src[yellow] * np.array([0.9, 0.62, 0.15])  # railings -> amber hazard
    g = fbm(x * 0.3 + z * 0.3, y * 0.3, 4, 12.0)
    alb[filled] *= (0.8 + 0.3 * g[filled])[:, None]
    e = np.zeros_like(alb)
    ny = Nn[..., 1]
    side = filled & (np.abs(ny) < 0.5)
    b1 = side & (y > 8.6) & (y < 8.75)
    b2 = side & (y > 13.2) & (y < 13.3)
    b3 = side & (y > 0.9) & (y < 1.0) & (np.abs(x) > 14.2)
    elev = side & (z < -17.5)
    b4 = elev & (((y > 6.0) & (y < 6.14)) | ((y > 0.35) & (y < 0.45)))
    e[b1] = RED * 0.9; e[b2] = WHT * 0.5; e[b4] = RED * 0.9
    alb[b1 | b2 | b4] = 0.1
    met = np.where(yellow, 0.2, 0.6).astype(np.float32); smo = np.where(yellow, 0.4, 0.45 - 0.2 * lum).astype(np.float32)
    save(alb, 'T_Arena_Pillar_Albedo.png')
    Image.fromarray(np.clip(np.stack([met, met * 0, met * 0, smo], -1) * 255 + .5, 0, 255).astype(np.uint8), 'RGBA').save(os.path.join(OUT, 'T_Arena_Pillar_Mask.png'))
    save(e, 'T_Arena_Pillar_Emission.png')

if __name__ == '__main__':
    which = sys.argv[1:] or ['room', 'pillar', 'floor']
    if 'floor' in which: bake_floor(int(os.environ.get('SW', 2048)), int(os.environ.get('SH', 4096)))
    if 'room' in which: bake_room(int(os.environ.get('S', 4096)))
    if 'pillar' in which: bake_pillar()
