"""Pre-release PV assembler for Chuon (TSUKASA art direction: graphite / white / TSUKASA red).

Usage:  python -X utf8 make_pv.py edl.json
EDL (JSON):
{
  "out": "Builds/PV/Chuon_PV",          # writes <out>_master.mp4 (CRF 16) and <out>_web.mp4
  "work": "Temp/pv_work",
  "fps": 60, "size": [1920, 1080], "xfade": 0.25,
  "music": "Assets/.../Boss_Loop_GEN.wav", "music_gain_db": -2, "music_fade_out": 2.5,
  "segments": [
    {"type": "card", "dur": 3.0, "logo": true, "lines": [["CHUON", 0.16, "white"], ["추온", 0.06, "red"]]},
    {"type": "clip", "file": "Recordings/pv_tutorial.mp4", "in": 1.5, "out": 6.0,
     "caption": "VR 침입 리허설", "sub": "INFILTRATION TUTORIAL", "speed": 1.0, "transition": "fade"}
  ]
}
Clips may be any resolution/fps; they are scaled/padded to the target, retimed to a constant fps.
Transitions: per segment "transition" = fade | wipeleft | slideleft | smoothleft | none (cut).
Requires ffmpeg (winget Gyan.FFmpeg) and Pillow.
"""
import json, os, subprocess, sys, shutil, glob
import numpy as np
from PIL import Image, ImageDraw, ImageFont, ImageFilter

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
FONT_LATIN = r"C:/Windows/Fonts/bahnschrift.ttf"
FONT_KO = r"C:/Windows/Fonts/malgunbd.ttf"
LOGO = os.path.join(ROOT, "Assets/Art/ChuonPolish/Level_Facility/Textures/T_LF_Holo_TsukasaLogo.png")
RED = (214, 32, 44)
WHITE = (236, 238, 240)
GRAPHITE = (14, 15, 17)


def ffmpeg_exe():
    exe = shutil.which("ffmpeg")
    if exe:
        return exe
    hits = glob.glob(os.path.expandvars(r"%LOCALAPPDATA%\Microsoft\WinGet\Packages\Gyan.FFmpeg*\*\bin\ffmpeg.exe"))
    if not hits:
        sys.exit("ffmpeg not found (winget install Gyan.FFmpeg)")
    return hits[0]


FF = ffmpeg_exe()


def run(args):
    print(">", " ".join(a if " " not in a else '"%s"' % a for a in args[:12]), "...")
    subprocess.run([FF, "-hide_banner", "-loglevel", "error", "-y"] + args, check=True)


def p(path):
    return path if os.path.isabs(path) else os.path.join(ROOT, path)


def font_for(text, size):
    ko = any("\uac00" <= c <= "\ud7a3" for c in text)
    return ImageFont.truetype(FONT_KO if ko else FONT_LATIN, size)


def render_card(seg, W, H, path):
    """Graphite card, thin red frame accents, optional TSUKASA mark, centred lines."""
    im = Image.new("RGB", (W, H), GRAPHITE)
    d = ImageDraw.Draw(im)
    # subtle vertical gradient + scanlines
    for y in range(H):
        v = int(6 * (1 - abs(y / H - 0.5) * 2))
        if y % 3 == 0:
            v += 2
        d.line([(0, y), (W, y)], fill=(GRAPHITE[0] + v, GRAPHITE[1] + v, GRAPHITE[2] + v + 1))
    m = int(H * 0.06)
    for (x0, y0, sx, sy) in ((m, m, 1, 1), (W - m, m, -1, 1), (m, H - m, 1, -1), (W - m, H - m, -1, -1)):
        d.line([(x0, y0), (x0 + sx * int(W * 0.05), y0)], fill=RED, width=3)
        d.line([(x0, y0), (x0, y0 + sy * int(H * 0.05))], fill=RED, width=3)
    lines = seg.get("lines", [])
    y = H * (0.30 if seg.get("logo") else 0.5)
    if seg.get("logo") and os.path.exists(LOGO):
        lg = Image.open(LOGO).convert("L")
        s = int(H * 0.30)
        lg = lg.resize((s, s), Image.LANCZOS)
        tint = Image.new("RGB", (s, s), RED)
        im.paste(tint, (W // 2 - s // 2, int(H * 0.12)), lg)
        y = H * 0.50
    heights = [int(H * l[1]) for l in lines]
    total = sum(heights) + int(H * 0.025) * max(0, len(lines) - 1)
    y = int(y - (0 if seg.get("logo") else total / 2))
    glow = Image.new("RGB", (W, H), (0, 0, 0))
    gd = ImageDraw.Draw(glow)
    for (text, frac, col), h in zip(lines, heights):
        f = font_for(text, h)
        bb = f.getbbox(text)
        x = (W - (bb[2] - bb[0])) // 2 - bb[0]
        c = RED if col == "red" else WHITE if col == "white" else tuple(col)
        d.text((x, y - bb[1]), text, font=f, fill=c)
        gd.text((x, y - bb[1]), text, font=f, fill=tuple(int(v * 0.5) for v in c))
        y += h + int(H * 0.025)
    glow = glow.filter(ImageFilter.GaussianBlur(H / 90))
    im = Image.fromarray(np.clip(np.asarray(im, dtype=np.int16) + np.asarray(glow, dtype=np.int16), 0, 255).astype(np.uint8))
    im.save(path)


def caption_png(seg, W, H, path):
    """Lower-third: red bar + Korean title + small latin sub, transparent PNG."""
    im = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    x, y = int(W * 0.055), int(H * 0.80)
    title, sub = seg.get("caption", ""), seg.get("sub", "")
    ft, fs = font_for(title, int(H * 0.042)), font_for(sub, int(H * 0.020))
    tw = ft.getbbox(title)[2] if title else 0
    sw = fs.getbbox(sub)[2] if sub else 0
    d.rectangle([x - 18, y - 6, x + max(tw, sw) + 26, y + int(H * 0.095)], fill=(10, 11, 13, 150))
    d.rectangle([x - 18, y - 6, x - 12, y + int(H * 0.095)], fill=RED + (255,))
    if title:
        d.text((x, y), title, font=ft, fill=WHITE + (255,))
    if sub:
        d.text((x + 2, y + int(H * 0.058)), sub, font=fs, fill=(200, 200, 205, 230))
    im.save(path)


def build_segment(i, seg, cfg, work):
    W, H = cfg["size"]
    fps = cfg["fps"]
    out = os.path.join(work, "seg_%02d.mp4" % i)
    enc = ["-c:v", "libx264", "-preset", "medium", "-crf", "12", "-pix_fmt", "yuv420p", "-r", str(fps), "-an"]
    if seg["type"] == "card":
        png = os.path.join(work, "card_%02d.png" % i)
        render_card(seg, W, H, png)
        dur = seg.get("dur", 3.0)
        # slow push-in for life
        vf = ("scale=%d:%d,zoompan=z='1+0.035*on/(%d)':x='iw/2-(iw/zoom/2)':y='ih/2-(ih/zoom/2)':d=%d:s=%dx%d:fps=%d"
              % (W * 2, H * 2, int(dur * fps), int(dur * fps), W, H, fps))
        run(["-loop", "1", "-i", png, "-t", str(dur), "-vf", vf] + enc + [out])
    else:
        src = p(seg["file"])
        speed = seg.get("speed", 1.0)
        vf = ("scale=%d:%d:force_original_aspect_ratio=decrease:flags=lanczos,pad=%d:%d:(ow-iw)/2:(oh-ih)/2:color=black,"
              "setsar=1,setpts=(PTS-STARTPTS)/%s,fps=%d" % (W, H, W, H, speed, fps))
        args = ["-ss", str(seg.get("in", 0)), "-to", str(seg.get("out", 9999)), "-i", src]
        if seg.get("caption") or seg.get("sub"):
            cap = os.path.join(work, "cap_%02d.png" % i)
            caption_png(seg, W, H, cap)
            args += ["-loop", "1", "-framerate", str(fps), "-i", cap, "-filter_complex",
                     "[0:v]%s[v];[1:v]format=rgba,fade=in:st=0.15:d=0.25:alpha=1[c];[v][c]overlay=0:0:shortest=1[o]" % vf,
                     "-map", "[o]"]
        else:
            args += ["-vf", vf]
        run(args + enc + [out])
    return out


def probe_dur(path):
    r = subprocess.run([FF.replace("ffmpeg.exe", "ffprobe.exe"), "-v", "error", "-show_entries", "format=duration",
                        "-of", "default=nw=1:nk=1", path], capture_output=True, text=True)
    return float(r.stdout.strip())


def main(edl_path):
    cfg = json.load(open(edl_path, encoding="utf-8"))
    cfg.setdefault("fps", 60)
    cfg.setdefault("size", [1920, 1080])
    work = p(cfg.get("work", "Temp/pv_work"))
    os.makedirs(work, exist_ok=True)
    segs = cfg["segments"]
    files = [build_segment(i, s, cfg, work) for i, s in enumerate(segs)]
    durs = [probe_dur(f) for f in files]
    X = cfg.get("xfade", 0.25)
    # xfade chain
    inputs = sum([["-i", f] for f in files], [])
    chain, last, t = [], "[0:v]", durs[0]
    for k in range(1, len(files)):
        tr = segs[k].get("transition", "fade")
        lab = "[x%d]" % k
        if tr == "none":
            chain.append("%s[%d:v]concat=n=2:v=1:a=0%s" % (last, k, lab))
            t += durs[k]
        else:
            chain.append("%s[%d:v]xfade=transition=%s:duration=%s:offset=%.3f%s" % (last, k, tr, X, t - X, lab))
            t += durs[k] - X
        last = lab
    total = t
    video = os.path.join(work, "video.mp4")
    if chain:
        run(inputs + ["-filter_complex", ";".join(chain), "-map", last, "-c:v", "libx264", "-preset", "medium",
                      "-crf", "12", "-pix_fmt", "yuv420p", "-r", str(cfg["fps"]), video])
    else:
        shutil.copy(files[0], video)
    out = p(cfg.get("out", "Builds/PV/Chuon_PV"))
    os.makedirs(os.path.dirname(out), exist_ok=True)
    fo = cfg.get("music_fade_out", 2.5)
    amix = []
    if cfg.get("music"):
        amix = ["-stream_loop", "-1", "-i", p(cfg["music"])]
        af = ("[1:a]atrim=0:%.3f,afade=t=in:d=0.6,afade=t=out:st=%.3f:d=%.3f,volume=%sdB,loudnorm=I=-14:TP=-1.5:LRA=11[a]"
              % (total, max(0, total - fo), fo, cfg.get("music_gain_db", 0)))
    vfade = "fade=t=in:d=0.4,fade=t=out:st=%.3f:d=0.8" % max(0, total - 0.8)
    for name, crf, extra in (("_master.mp4", "16", ["-preset", "slow", "-profile:v", "high", "-tune", "film"]),
                             ("_web.mp4", "20", ["-preset", "slow", "-maxrate", "16M", "-bufsize", "32M"])):
        args = ["-i", video] + amix
        if amix:
            args += ["-filter_complex", "[0:v]%s[v];%s" % (vfade, af), "-map", "[v]", "-map", "[a]",
                     "-c:a", "aac", "-b:a", "320k"]
        else:
            args += ["-vf", vfade]
        run(args + ["-c:v", "libx264", "-crf", crf, "-pix_fmt", "yuv420p", "-movflags", "+faststart"] + extra
            + ["-t", "%.3f" % total, out + name])
    print("PV done: %.1f s ->" % total, out + "_master.mp4")


if __name__ == "__main__":
    main(sys.argv[1])
