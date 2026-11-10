# -*- coding: utf-8 -*-
"""CHUON 앱 아이콘 생성기 (TSUKASA 아트 디렉션: 흑연 바탕 + 세라믹 화이트 칼날 + TSUKASA 레드 위상단절 베기).

usage: python -X utf8 Tools/ArtGen/chuon_icon.py <out_dir> [preview_dir]
  out_dir     : Assets/Art/ChuonPolish/Branding (PNG 1024..16 + ChuonIcon.ico)
  preview_dir : (선택) 미리보기 시트 저장 위치 (스크래치)
"""
import math
import os
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

GRAPHITE_HI = (46, 48, 53)
GRAPHITE_LO = (14, 15, 18)
CERAMIC = (236, 234, 228)
CERAMIC_SHADE = (176, 178, 182)
RED = (224, 48, 58)          # TitleSettingsOverlay.TsukasaRedColor (0.878, 0.188, 0.227)
RED_HOT = (255, 120, 110)

SIZES = [1024, 512, 256, 128, 64, 48, 32, 24, 16]


def rounded_mask(s, margin, radius):
    m = Image.new("L", (s, s), 0)
    ImageDraw.Draw(m).rounded_rectangle([margin, margin, s - 1 - margin, s - 1 - margin], radius=radius, fill=255)
    return m


def background(s):
    y, x = np.mgrid[0:s, 0:s].astype(np.float32) / s
    # 위쪽 왼편에서 비스듬히 들어오는 부드러운 빛
    d = np.sqrt((x - 0.38) ** 2 + (y - 0.30) ** 2)
    t = np.clip(d / 0.85, 0, 1) ** 1.2
    hi = np.array(GRAPHITE_HI, np.float32)
    lo = np.array(GRAPHITE_LO, np.float32)
    rgb = hi[None, None, :] * (1 - t[..., None]) + lo[None, None, :] * t[..., None]
    # 세라믹 패널 이음선 (가는 가로선, 매우 옅게)
    panel = ((y * 7.0) % 1.0 < 0.004).astype(np.float32) * 6.0
    rgb += panel[..., None]
    # 미세 노이즈 (밴딩 방지)
    rng = np.random.default_rng(7)
    rgb += rng.normal(0, 1.2, rgb.shape).astype(np.float32)
    return Image.fromarray(np.clip(rgb, 0, 255).astype(np.uint8), "RGB").convert("RGBA")


def rot(p, c, a):
    x, y = p[0] - c[0], p[1] - c[1]
    ca, sa = math.cos(a), math.sin(a)
    return (c[0] + x * ca - y * sa, c[1] + x * sa + y * ca)


def draw_blade(img, s, detail):
    """왼쪽 아래 → 오른쪽 위로 뻗은 카타나. 날(오른쪽 아래)이 볼록, 칼끝이 등 쪽으로 휜다(소리)."""
    c = (s * 0.5, s * 0.5)
    ang = math.radians(-52)
    L = s * (0.84 if detail >= 2 else 0.92)
    w = s * (0.036 if detail >= 2 else 0.080)
    sori = s * (0.040 if detail >= 2 else 0.03)
    layer = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    g = ImageDraw.Draw(layer)
    base_u, tip_u = -0.17, 0.50

    def bend(u):
        t = max(0.0, (u - base_u) / (tip_u - base_u))
        return -sori * t * t

    def P(u, v, curved=True):
        return rot((c[0] + u * L, c[1] + v + (bend(u) if curved else 0.0)), c, ang)

    N = 40
    kis = 0.43                          # 키사키(칼끝부) 시작
    spine, edge = [], []
    for i in range(N + 1):
        u = base_u + (kis - base_u) * i / N
        spine.append(P(u, -w * 0.50))
        edge.append(P(u, w * 0.50))
    # 키사키: 날은 둥글게 칼끝으로, 등은 곧게
    for i in range(1, 13):
        t = i / 12
        u = kis + (tip_u - kis) * t
        edge.append(P(u, w * (0.50 - 0.50 * (1 - math.cos(t * math.pi / 2)) ** 0.8 - 0.55 * t * t)))
        spine.append(P(u, -w * 0.50 + w * 0.10 * t))
    tip = P(tip_u + 0.004, -w * 0.42)
    g.polygon(spine + [tip] + edge[::-1], fill=CERAMIC + (255,))
    if detail >= 2:
        # 시노기(등 쪽 면) 음영
        shin = [P(base_u + (kis - base_u) * i / N, -w * 0.50) for i in range(N + 1)]
        shin2 = [P(base_u + (kis - base_u) * i / N, -w * 0.08) for i in range(N + 1)]
        g.polygon(shin + [P(tip_u - 0.01, -w * 0.38)] + shin2[::-1], fill=CERAMIC_SHADE + (255,))
        # 하몬(물결 담금선)
        ham = []
        for i in range(N * 2 + 1):
            u = base_u + 0.01 + (kis - base_u - 0.01) * i / (N * 2)
            ham.append(P(u, w * (0.18 + 0.07 * math.sin(i * 0.9))))
        g.line(ham, fill=(255, 255, 255, 200), width=max(1, int(s * 0.003)))
    # 츠바: 작은 타원 원판
    tr = w * (1.55 if detail >= 2 else 1.25)
    tsuba_c = P(base_u - 0.008, 0)
    pts = []
    for i in range(36):
        a = 2 * math.pi * i / 36
        pts.append(rot((tsuba_c[0] + s * 0.010 * math.cos(a), tsuba_c[1] + tr * math.sin(a)), tsuba_c, ang))
    g.polygon(pts, fill=(12, 12, 14, 255))
    if detail >= 2:
        g.line(pts + [pts[0]], fill=CERAMIC_SHADE + (255,), width=max(1, int(s * 0.004)))
    # 손잡이: 검은 감기 + 어두운 붉은 마름모, 끝에 세라믹 카시라
    hw = w * 0.60
    h0, h1 = base_u - 0.018, -0.46
    g.polygon([P(h0, -hw, False), P(h0, hw, False), P(h1, hw * 0.9, False), P(h1, -hw * 0.9, False)],
              fill=(20, 20, 23, 255))
    if detail >= 2:
        n = 7
        for i in range(n):
            u0 = h0 - (h0 - h1) * (i + 0.12) / n
            u1 = h0 - (h0 - h1) * (i + 0.88) / n
            um = (u0 + u1) * 0.5
            g.polygon([P(u0, 0, False), P(um, -hw * 0.6, False), P(u1, 0, False), P(um, hw * 0.6, False)],
                      fill=(92, 24, 30, 255))
    g.polygon([P(h1, -hw * 0.95, False), P(h1, hw * 0.95, False), P(h1 - 0.016, hw * 0.75, False),
               P(h1 - 0.016, -hw * 0.75, False)], fill=CERAMIC_SHADE + (255,))
    shadow = layer.split()[3].filter(ImageFilter.GaussianBlur(s * 0.012))
    sh = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    sh.putalpha(shadow.point(lambda a: int(a * 0.6)))
    img.alpha_composite(sh, (int(s * 0.006), int(s * 0.012)))
    img.alpha_composite(layer)


def draw_ring(img, s):
    """위상 링: 얇은 세라믹 원 + 눈금 (TSUKASA HUD 언어)."""
    layer = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    g = ImageDraw.Draw(layer)
    r = s * 0.335
    c = s * 0.5
    g.ellipse([c - r, c - r, c + r, c + r], outline=CERAMIC + (110,), width=max(1, int(s * 0.008)))
    r2 = s * 0.365
    for i in range(48):
        a = 2 * math.pi * i / 48
        long_tick = i % 6 == 0
        r_in = r2 - (s * 0.020 if long_tick else s * 0.009)
        g.line([(c + r_in * math.cos(a), c + r_in * math.sin(a)), (c + r2 * math.cos(a), c + r2 * math.sin(a))],
               fill=CERAMIC + (120 if long_tick else 60,), width=max(1, int(s * (0.006 if long_tick else 0.003))))
    img.alpha_composite(layer)


def sever(img, s, detail):
    """위상단절: 직선 하나로 그림을 둘로 가르고 양쪽을 선을 따라 어긋나게 민 뒤 틈에 붉은 베기를 넣는다."""
    ang = math.radians(-14)            # 베기 방향 (왼쪽 아래 → 오른쪽 위로 완만)
    c = np.array([s * 0.5, s * 0.53])
    d = np.array([math.cos(ang), math.sin(ang)])
    nrm = np.array([-d[1], d[0]])
    shift = s * (0.055 if detail >= 2 else 0.06)
    gap = s * (0.016 if detail >= 2 else 0.03)

    y, x = np.mgrid[0:s, 0:s].astype(np.float32)
    side = (x - c[0]) * nrm[0] + (y - c[1]) * nrm[1]
    upper = Image.fromarray(((side < 0) * 255).astype(np.uint8), "L")

    # 각 반쪽을 베기 방향으로 반대로 밀고, 법선 방향으로 살짝 벌린다
    def shifted(sign):
        off = d * shift * sign + nrm * gap * 0.5 * (-sign)
        return img.transform(img.size, Image.AFFINE, (1, 0, -off[0], 0, 1, -off[1]), resample=Image.BICUBIC)

    up = shifted(+1)
    lo = shifted(-1)
    um = upper.transform(img.size, Image.AFFINE, (1, 0, -(d * shift)[0] + (nrm * gap * 0.5)[0], 0, 1,
                                                  -(d * shift)[1] + (nrm * gap * 0.5)[1]))
    out = Image.new("RGBA", img.size, (0, 0, 0, 0))
    lm = Image.fromarray(255 - np.array(upper), "L").transform(img.size, Image.AFFINE, (
        1, 0, (d * shift)[0] - (nrm * gap * 0.5)[0], 0, 1, (d * shift)[1] - (nrm * gap * 0.5)[1]))
    out.paste(lo, (0, 0), lm)
    out.paste(up, (0, 0), um)
    # 틈 바닥 (가장 어두운 흑연)
    base = Image.new("RGBA", img.size, GRAPHITE_LO + (255,))
    base.alpha_composite(out)
    out = base

    # 붉은 베기 획: 가운데 굵고 양 끝으로 날카롭게 가늘어짐
    half_len = s * 0.62
    width = s * (0.020 if detail >= 2 else 0.045)
    pts_top, pts_bot = [], []
    N = 64
    for i in range(N + 1):
        t = -1 + 2 * i / N
        prof = (1 - abs(t) ** 1.6) ** 0.9                # 붓 끝처럼 모이는 두께
        prof *= 0.9 + 0.2 * (t + 1) * 0.5                 # 오른쪽(베기 진행 끝)이 조금 더 굵다
        p = c + d * (t * half_len)
        pts_top.append(tuple(p - nrm * width * 0.5 * prof))
        pts_bot.append(tuple(p + nrm * width * 0.5 * prof))
    slash = Image.new("L", img.size, 0)
    ImageDraw.Draw(slash).polygon(pts_top + pts_bot[::-1], fill=255)

    glow_r = s * (0.030 if detail >= 2 else 0.04)
    glow = slash.filter(ImageFilter.GaussianBlur(glow_r))
    gl = Image.new("RGBA", img.size, RED + (0,))
    gl.putalpha(glow.point(lambda a: min(255, int(a * 1.6))))
    out.alpha_composite(gl)
    red = Image.new("RGBA", img.size, RED + (0,))
    red.putalpha(slash)
    out.alpha_composite(red)
    if detail >= 2:
        # 뜨거운 심선
        core = Image.new("L", img.size, 0)
        cw = width * 0.22
        ct, cb = [], []
        for i in range(N + 1):
            t = -0.92 + 1.84 * i / N
            prof = (1 - abs(t / 0.92) ** 1.4)
            p = c + d * (t * half_len)
            ct.append(tuple(p - nrm * cw * prof))
            cb.append(tuple(p + nrm * cw * prof))
        ImageDraw.Draw(core).polygon(ct + cb[::-1], fill=255)
        hot = Image.new("RGBA", img.size, RED_HOT + (0,))
        hot.putalpha(core.filter(ImageFilter.GaussianBlur(s * 0.002)))
        out.alpha_composite(hot)
        # 불티 몇 점 (베기 끝 쪽)
        g = ImageDraw.Draw(out)
        rng = np.random.default_rng(3)
        for _ in range(9):
            t = rng.uniform(0.35, 0.9)
            p = c + d * (t * half_len) + nrm * rng.normal(0, width * 1.4)
            r = s * rng.uniform(0.002, 0.005)
            g.ellipse([p[0] - r, p[1] - r, p[0] + r, p[1] + r], fill=RED_HOT + (int(rng.uniform(120, 230)),))
    return out


def render(size):
    ss = 4 if size <= 256 else 2
    s = size * ss
    detail = 2 if size >= 48 else 1
    img = background(s)
    if detail >= 2:
        draw_ring(img, s)
    draw_blade(img, s, detail)
    img = sever(img, s, detail)
    # 안쪽 테두리 광택 (세라믹 베벨)
    margin = int(s * (0.035 if size >= 48 else 0.02))
    radius = int(s * 0.20)
    if detail >= 2:
        g = ImageDraw.Draw(img)
        g.rounded_rectangle([margin + s * 0.006, margin + s * 0.006, s - 1 - margin - s * 0.006, s - 1 - margin - s * 0.006],
                            radius=radius, outline=(255, 255, 255, 28), width=max(1, int(s * 0.004)))
    mask = rounded_mask(s, margin, radius)
    img.putalpha(Image.fromarray(np.minimum(np.array(img.split()[3]), np.array(mask)), "L"))
    return img.resize((size, size), Image.LANCZOS)


def main():
    out_dir = sys.argv[1]
    prev_dir = sys.argv[2] if len(sys.argv) > 2 else None
    os.makedirs(out_dir, exist_ok=True)
    imgs = {}
    for sz in SIZES:
        im = render(sz)
        imgs[sz] = im
        im.save(os.path.join(out_dir, "ChuonIcon_%d.png" % sz), optimize=True)
    # Windows .ico (바로가기·README 용, Unity 아이콘은 PNG 사용)
    ico_sizes = [256, 128, 64, 48, 32, 24, 16]
    imgs[256].save(os.path.join(out_dir, "ChuonIcon.ico"), format="ICO", sizes=[(z, z) for z in ico_sizes],
                   append_images=[imgs[z] for z in ico_sizes[1:]])
    if prev_dir:
        os.makedirs(prev_dir, exist_ok=True)
        W = 1024 + 40 + 512 + 40 + 256 + 40 + 256 + 40
        for bg_name, bg in (("dark", (30, 30, 30, 255)), ("light", (238, 238, 238, 255))):
            sheet = Image.new("RGBA", (W, 1104), bg)
            sheet.alpha_composite(imgs[1024], (40, 40))
            sheet.alpha_composite(imgs[512], (1104, 40))
            x = 1104
            y = 600
            for z in (256, 128, 64, 48, 32, 24, 16):
                sheet.alpha_composite(imgs[z], (x, y))
                x += z + 24
            # 실제 픽셀 크기 확대 비교 (32·16 를 8배 최근접)
            sheet.alpha_composite(imgs[32].resize((256, 256), Image.NEAREST), (1104, 860 - 0))
            sheet.alpha_composite(imgs[16].resize((128, 128), Image.NEAREST), (1104 + 280, 860))
            sheet.convert("RGB").save(os.path.join(prev_dir, "chuon_icon_sheet_%s.png" % bg_name))
    print("ok", out_dir)


if __name__ == "__main__":
    main()
