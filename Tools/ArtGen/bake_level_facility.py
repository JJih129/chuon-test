"""Holo / label textures for Level_Facility (TSUKASA HQ lobby + B4 hangar) and the Tutorial VR range.
Canon: Docs/Story_Reference.md (TSUKASA CORPORATION HQ lobby, PROJECT SERIES showroom, SUBJECT 07 = Chuon, DISPOSED).
Same panel style as the MainScene holo signs: white glyphs on black, tinted by an additive
unlit material (red = TSUKASA corporate/danger, white = info, amber = interactable).
Outputs into Assets/Art/ChuonPolish/Level_Facility/Textures. Run: python bake_level_facility.py"""
import os
import numpy as np
from PIL import Image, ImageDraw, ImageFont, ImageFilter

OUT = r"C:/Users/ikh33/source/chuon-test/Assets/Art/ChuonPolish/Level_Facility/Textures/"
LATIN = r"C:/Windows/Fonts/bahnschrift.ttf"
HANGUL = r"C:/Windows/Fonts/malgunbd.ttf"


def font_for(txt, size):
    has_hangul = any('\uac00' <= ch <= '\ud7a3' for ch in txt)
    return ImageFont.truetype(HANGUL if has_hangul else LATIN, size)


def panel(W, H, lines, path, frame=True):
    im = Image.new('RGBA', (W, H), (0, 0, 0, 0)); d = ImageDraw.Draw(im)
    w = max(3, H // 90)
    if frame:
        c = int(H * 0.12)
        pts = [(c, w), (W - w, w), (W - w, H - c), (W - c, H - w), (w, H - w), (w, c)]
        d.polygon(pts, fill=(255, 255, 255, 10)); d.line(pts + [pts[0]], fill=(255, 255, 255, 235), width=w)
        for x0 in (int(W * 0.04), int(W * 0.9)):
            d.rectangle([x0, int(H * 0.08), x0 + int(W * 0.06), int(H * 0.08) + w * 2], fill=(255, 255, 255, 255))
    a = np.array(im); yy = np.arange(H)[:, None]; scan = ((yy // 3) % 2 == 0)
    a[..., 3] = np.where(scan & (a[..., 3] < 40), np.maximum(a[..., 3], 22), a[..., 3]).astype(np.uint8)
    im = Image.fromarray(a); d = ImageDraw.Draw(im)
    heights = []
    for txt, frac, al in lines:
        f = font_for(txt, int(H * frac)); bb = f.getbbox(txt); heights.append(bb[3] - bb[1])
    total = sum(heights) + int(H * 0.07) * (len(lines) - 1)
    y = (H - total) // 2
    for (txt, frac, al), hgt in zip(lines, heights):
        f = font_for(txt, int(H * frac)); bb = f.getbbox(txt)
        d.text(((W - (bb[2] - bb[0])) // 2 - bb[0], y - bb[1]), txt, font=f, fill=(255, 255, 255, al))
        y += hgt + int(H * 0.07)
    glow = im.filter(ImageFilter.GaussianBlur(H / 80)); out = Image.alpha_composite(glow, im)
    bg = Image.new("RGBA", out.size, (0, 0, 0, 255))
    os.makedirs(OUT, exist_ok=True)
    Image.alpha_composite(bg, out).convert("RGB").save(OUT + path); print(path)


def emblem(W, H, path):
    """TSUKASA corporate mark: open ring + vertical blade + two slashes, wordmark below (white glyphs, tinted by material)."""
    im = Image.new('RGBA', (W, H), (0, 0, 0, 0)); d = ImageDraw.Draw(im)
    cx, cy, r = W // 2, int(H * 0.36), int(H * 0.26); lw = max(6, H // 40)
    d.arc([cx - r, cy - r, cx + r, cy + r], 120, 420, fill=(255, 255, 255, 255), width=lw)
    d.rectangle([cx - lw // 2, cy - int(r * 1.15), cx + lw // 2, cy + int(r * 0.85)], fill=(255, 255, 255, 255))
    for s in (-1, 1):
        d.line([(cx + s * int(r * 0.25), cy + int(r * 0.15)), (cx + s * int(r * 0.7), cy - int(r * 0.4))], fill=(255, 255, 255, 235), width=lw)
    f = font_for("TSUKASA", int(H * 0.17)); bb = f.getbbox("TSUKASA")
    d.text(((W - (bb[2] - bb[0])) // 2 - bb[0], int(H * 0.68) - bb[1]), "TSUKASA", font=f, fill=(255, 255, 255, 255))
    f2 = font_for("CORPORATION", int(H * 0.06)); bb = f2.getbbox("CORPORATION")
    d.text(((W - (bb[2] - bb[0])) // 2 - bb[0], int(H * 0.88) - bb[1]), "CORPORATION", font=f2, fill=(255, 255, 255, 210))
    glow = im.filter(ImageFilter.GaussianBlur(H / 70)); out = Image.alpha_composite(glow, im)
    bg = Image.new("RGBA", out.size, (0, 0, 0, 255)); os.makedirs(OUT, exist_ok=True)
    Image.alpha_composite(bg, out).convert("RGB").save(OUT + path); print(path)


# --- HQ lobby (upper floor) ---
panel(2048, 640, [("TSUKASA", 0.36, 255),
                  ("TSUKASA CORPORATION  //  HEADQUARTERS  //  MAIN LOBBY", 0.07, 225),
                  ("츠카사는 당신의 내일을 설계합니다", 0.085, 200)],
      'T_LF_Holo_Notice.png')
panel(1024, 512, [("ATRIUM SKYDECK", 0.14, 255),
                  ("HANGAR B4 BELOW  //  TYPE-D", 0.075, 220),
                  ("유리에 접근 금지", 0.085, 180)],
      'T_LF_Holo_Observation.png')
panel(1024, 512, [("EXECUTIVE LIFT 00", 0.14, 255),
                  ("LOBBY  >>  HANGAR B4 ONLY", 0.09, 220),
                  ("AUTHORIZED TRANSFER ONLY", 0.065, 170)],
      'T_LF_Holo_Elevator.png')
panel(1024, 512, [("PROJECT SERIES", 0.13, 255),
                  ("TYPE-A  /  TYPE-B  /  TYPE-C  :  TERMINATED", 0.07, 220),
                  ("SUBJECT 07 : DISPOSED     TYPE-D : ACTIVE", 0.07, 200)],
      'T_LF_Holo_Intake.png')
panel(1024, 512, [("LOCKDOWN", 0.2, 255),
                  ("OUT OF SERVICE", 0.1, 230),
                  ("엘리베이터 운행 중지", 0.085, 200)],
      'T_LF_Holo_Lockdown.png')
panel(1024, 384, [("RECEPTION", 0.2, 255),
                  ("VISITOR ID REQUIRED  //  안내", 0.09, 210)],
      'T_LF_Holo_Reception.png')
emblem(1024, 1024, 'T_LF_Holo_TsukasaLogo.png')
# showroom plaques (file names kept: TYPEE = Chuon's pod)
for t, s2, s3, fn in (("TYPE-A", "PROJECT SERIES  //  PROTOTYPE", "TERMINATED", "TYPEA"),
                      ("TYPE-B", "PROJECT SERIES  //  PROTOTYPE", "TERMINATED", "TYPEB"),
                      ("TYPE-C", "PROJECT SERIES  //  PROTOTYPE", "TERMINATED", "TYPEC"),
                      ("SUBJECT 07", "실험체 07호", "STATUS : DISPOSED", "TYPEE")):
    panel(512, 256, [(t, 0.24, 255), (s2, 0.1, 215), (s3, 0.1, 190)], 'T_LF_Label_' + fn + '.png')

# --- Tutorial = VR training ---
OUT_TUTORIAL = r"C:/Users/ikh33/source/chuon-test/Assets/Art/ChuonPolish/Tutorial/Textures/"
OUT = OUT_TUTORIAL
panel(1024, 512, [("SIMULATION", 0.2, 255),
                  ("TSUKASA COMBAT VR  //  TRAINING PROGRAM", 0.075, 220),
                  ("가상현실 훈련 중", 0.085, 190)],
      'T_TU_Holo_Simulation.png')
