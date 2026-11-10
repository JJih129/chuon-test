"""Pre-release PV assembler for Chuon (TSUKASA art direction: graphite / white / TSUKASA red).

Usage:  python -X utf8 make_pv.py edl.json [--sheet Temp/pv/sheet.png] [--only-sheet]
EDL (JSON):
{
  "out": "Builds/PV/Chuon_PV",          # writes <out>_master.mp4 (CRF 16) and <out>_web.mp4 (CRF 20)
  "work": "Temp/pv_work",
  "fps": 60, "size": [1920, 1080],
  "xfade": 0.25,                        # default transition length (s)
  "bpm": 132,                           # optional: segment "beats" -> seconds (60/bpm per beat)
  "music": "Assets/.../Boss_Loop_GEN.wav", "music_gain_db": -2, "music_fade_out": 2.5,   # legacy: one looped bed
  "music": [ {"file": "...wav", "at": 0.0, "in": 0.0, "dur": 16.0, "gain_db": -2,
              "fade_in": 0.5, "fade_out": 1.0, "loop": true} ],                           # or a cue list
  "sfx":   [ {"file": "...wav", "at": 12.3, "gain_db": -3} ],
  "game_audio_db": -12,                 # default gain of recorded game audio (null = muted)
  "loudness": -14,                      # integrated LUFS target (two-pass loudnorm), true peak -1.5
  "segments": [
    {"type": "card", "beats": 8, "logo": true, "lines": [["CHUON", 0.16, "white"], ["추온", 0.06, "red"]]},
    {"type": "clip", "file": "Recordings/PV/take.mp4", "in": 12.5, "beats": 4,      # or "out" / "len"
     "speed": 1.0, "interp": "mci", "transition": "fade", "tdur": 0.2, "flash": 0.18,
     "caption": "패링 · 흑백 흡수", "sub": "PARRY", "overlay": {"lines": [["TYPE-D", 0.1, "white"]]},
     "audio_db": -10, "zoom": 1.08, "crop_y": 0.45}
  ]
}
Timeline model: every segment has a timeline length L (beats*60/bpm, or len, or (out-in)/speed, or dur for cards).
Cut k lands exactly at sum(L[0..k-1]) — the incoming shot is fully on screen on the beat; a transition of length
X starts X earlier (the incoming clip gets X of pre-roll from before its "in" point).
Transitions: none (hard cut) | any ffmpeg xfade name (fade, fadewhite, fadeblack, wipeleft, slideleft, smoothleft,
hblur, zoomin, squeezeh, ...). "flash": white flash (s) on the cut. "speed" < 1 = slow motion; "interp": mci
(motion-compensated, smooth) | blend | none. Slowed game audio is pitched down (asetrate) to match.
"zoom" (>=1) punches in around (crop_x, crop_y) — e.g. to keep framing off the lower half of the frame.
Clips may be any resolution/fps; they are scaled/padded to the target and retimed to a constant fps.
Requires ffmpeg (winget Gyan.FFmpeg), numpy and Pillow.
"""
import json, os, subprocess, sys, shutil, glob, re
import numpy as np
from PIL import Image, ImageDraw, ImageFont, ImageFilter

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
FONT_LATIN = r"C:/Windows/Fonts/bahnschrift.ttf"
FONT_KO = r"C:/Windows/Fonts/malgunbd.ttf"
LOGO = os.path.join(ROOT, "Assets/Art/ChuonPolish/Level_Facility/Textures/T_LF_Holo_TsukasaLogo.png")
RED = (214, 32, 44)
WHITE = (236, 238, 240)
GRAPHITE = (14, 15, 17)
SR = 48000


def ffmpeg_exe():
    exe = shutil.which("ffmpeg")
    if exe:
        return exe
    hits = glob.glob(os.path.expandvars(r"%LOCALAPPDATA%\Microsoft\WinGet\Packages\Gyan.FFmpeg*\*\bin\ffmpeg.exe"))
    if not hits:
        sys.exit("ffmpeg not found (winget install Gyan.FFmpeg)")
    return hits[0]


FF = ffmpeg_exe()
FFPROBE = os.path.join(os.path.dirname(FF), "ffprobe.exe" if FF.lower().endswith(".exe") else "ffprobe")


def run(args, capture=False):
    print(">", " ".join(a if " " not in a else '"%s"' % a for a in args[:14]), "...", flush=True)
    r = subprocess.run([FF, "-hide_banner", "-loglevel", "error", "-y"] + args, check=True,
                       capture_output=capture, text=capture, encoding="utf-8" if capture else None)
    return r


def p(path):
    return path if os.path.isabs(path) else os.path.join(ROOT, path)


def probe(path, entries="format=duration"):
    r = subprocess.run([FFPROBE, "-v", "error", "-show_entries", entries, "-of", "default=nw=1:nk=1", path],
                       capture_output=True, text=True)
    return r.stdout.strip()


def probe_dur(path):
    return float(probe(path).splitlines()[0])


def has_audio(path):
    r = subprocess.run([FFPROBE, "-v", "error", "-select_streams", "a", "-show_entries", "stream=index",
                        "-of", "csv=p=0", path], capture_output=True, text=True)
    return bool(r.stdout.strip())


def font_for(text, size):
    ko = any("\uac00" <= c <= "\ud7a3" for c in text)
    return ImageFont.truetype(FONT_KO if ko else FONT_LATIN, size)


def col(c):
    return RED if c == "red" else WHITE if c == "white" else (150, 152, 158) if c == "grey" else tuple(c)


def draw_lines(im, lines, W, H, y_center, glow_strength=0.5, tracking=0):
    """Centred text lines [[text, height_frac, colour]] around y_center (frac of H). Returns RGBA layer."""
    layer = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    heights = [int(H * l[1]) for l in lines]
    gap = int(H * 0.025)
    total = sum(heights) + gap * max(0, len(lines) - 1)
    y = int(H * y_center - total / 2)
    for (text, frac, c), h in zip(lines, heights):
        f = font_for(text, h)
        if tracking:
            widths = [f.getbbox(ch)[2] for ch in text]
            tw = sum(widths) + tracking * h * (len(text) - 1)
            x = (W - tw) / 2
            bb = f.getbbox(text)
            for ch, w in zip(text, widths):
                d.text((x, y - bb[1]), ch, font=f, fill=col(c) + (255,))
                x += w + tracking * h
        else:
            bb = f.getbbox(text)
            x = (W - (bb[2] - bb[0])) // 2 - bb[0]
            d.text((x, y - bb[1]), text, font=f, fill=col(c) + (255,))
        y += h + gap
    if glow_strength > 0:
        a = np.asarray(layer).astype(np.float32)
        g = Image.fromarray(a.astype(np.uint8)).filter(ImageFilter.GaussianBlur(H / 90))
        ga = np.asarray(g).astype(np.float32)
        ga[..., 3] *= glow_strength
        out = Image.alpha_composite(Image.fromarray(ga.astype(np.uint8)), layer)
        return out
    return layer


def render_card(seg, W, H, path):
    """Graphite card, thin red frame accents, optional TSUKASA mark, centred lines."""
    im = Image.new("RGB", (W, H), GRAPHITE)
    d = ImageDraw.Draw(im)
    for y in range(H):  # subtle vertical gradient + scanlines
        v = int(6 * (1 - abs(y / H - 0.5) * 2))
        if y % 3 == 0:
            v += 2
        d.line([(0, y), (W, y)], fill=(GRAPHITE[0] + v, GRAPHITE[1] + v, GRAPHITE[2] + v + 1))
    if seg.get("frame", True):
        m = int(H * 0.06)
        for (x0, y0, sx, sy) in ((m, m, 1, 1), (W - m, m, -1, 1), (m, H - m, 1, -1), (W - m, H - m, -1, -1)):
            d.line([(x0, y0), (x0 + sx * int(W * 0.05), y0)], fill=RED, width=3)
            d.line([(x0, y0), (x0, y0 + sy * int(H * 0.05))], fill=RED, width=3)
    lines = seg.get("lines", [])
    yc = 0.5
    if seg.get("logo") and os.path.exists(LOGO):
        lg = Image.open(LOGO).convert("L")
        s = int(H * seg.get("logo_size", 0.30))
        lg = lg.resize((s, s), Image.LANCZOS).filter(ImageFilter.MaxFilter(3))  # thin holo strokes -> readable mark
        tint = Image.new("RGB", (s, s), RED)
        top = int(H * 0.5 - s / 2) if not lines else int(H * 0.14)
        im.paste(tint, (W // 2 - s // 2, top), lg)
        yc = 0.66
    if seg.get("rule"):
        ry = int(H * seg["rule"])
        d.line([(W // 2 - int(W * 0.06), ry), (W // 2 + int(W * 0.06), ry)], fill=RED, width=2)
    if lines:
        layer = draw_lines(im, lines, W, H, seg.get("y", yc), tracking=seg.get("tracking", 0))
        im = Image.alpha_composite(im.convert("RGBA"), layer).convert("RGB")
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
    d.rectangle([x - 18, y - 6, x + max(tw, sw) + 26, y + int(H * 0.095)], fill=(10, 11, 13, 170))
    d.rectangle([x - 18, y - 6, x - 12, y + int(H * 0.095)], fill=RED + (255,))
    if title:
        d.text((x, y), title, font=ft, fill=WHITE + (255,))
    if sub:
        d.text((x + 2, y + int(H * 0.058)), sub, font=fs, fill=(200, 200, 205, 230))
    im.save(path)


def overlay_png(seg, W, H, path):
    ov = seg["overlay"]
    layer = draw_lines(None, ov.get("lines", []), W, H, ov.get("y", 0.5), glow_strength=0.8, tracking=ov.get("tracking", 0))
    if ov.get("shade", 0) > 0:  # soft dark band behind the text
        band = Image.new("RGBA", (W, H), (0, 0, 0, 0))
        bd = ImageDraw.Draw(band)
        cy = int(H * ov.get("y", 0.5))
        bh = int(H * 0.16)
        bd.rectangle([0, cy - bh, W, cy + bh], fill=(0, 0, 0, int(255 * ov["shade"])))
        band = band.filter(ImageFilter.GaussianBlur(H / 30))
        layer = Image.alpha_composite(band, layer)
    layer.save(path)


def seg_len(seg, cfg):
    if "beats" in seg:
        return seg["beats"] * 60.0 / cfg["bpm"]
    if "len" in seg:
        return float(seg["len"])
    if seg["type"] == "card":
        return float(seg.get("dur", 3.0))
    return (seg.get("out", 0) - seg.get("in", 0)) / seg.get("speed", 1.0)


def trans_of(segs, k, cfg):
    if k == 0:
        return "none", 0.0
    tr = segs[k].get("transition", cfg.get("transition", "fade"))
    if tr == "none":
        return "none", 0.0
    return tr, float(segs[k].get("tdur", cfg.get("xfade", 0.25)))


def build_segment(i, seg, cfg, work, L, Xin, Xout):
    """Renders seg_i.mp4 (video, exactly L+Xin s) and seg_i.wav (game audio, same length)."""
    W, H = cfg["size"]
    fps = cfg["fps"]
    R = L + Xin
    nframes = int(round(R * fps))
    out = os.path.join(work, "seg_%02d.mp4" % i)
    wav = os.path.join(work, "seg_%02d.wav" % i)
    enc = ["-c:v", "libx264", "-preset", "medium", "-crf", "12", "-pix_fmt", "yuv420p", "-r", str(fps),
           "-frames:v", str(nframes), "-an"]
    post = []
    if seg.get("flash"):
        fd = float(seg["flash"])
        post.append("eq=eval=frame:brightness='0.85*gte(t,%.4f)*max(0,1-(t-%.4f)/%.4f)':contrast='1-0.4*gte(t,%.4f)*max(0,1-(t-%.4f)/%.4f)'"
                    % (Xin, Xin, fd, Xin, Xin, fd))
    if seg.get("grade"):
        post.append(seg["grade"])
    gain = seg.get("audio_db", cfg.get("game_audio_db", -12))
    if seg["type"] == "card":
        png = os.path.join(work, "card_%02d.png" % i)
        render_card(seg, W, H, png)
        z = seg.get("push", 0.035)
        vf = ("scale=%d:%d,zoompan=z='1+%s*on/(%d)':x='iw/2-(iw/zoom/2)':y='ih/2-(ih/zoom/2)':d=%d:s=%dx%d:fps=%d"
              % (W * 2, H * 2, z, nframes, nframes, W, H, fps))
        if seg.get("fade_in_text"):
            vf += ",fade=t=in:st=%.3f:d=%.3f" % (Xin, float(seg["fade_in_text"]))
        if post:
            vf += "," + ",".join(post)
        run(["-loop", "1", "-framerate", str(fps), "-i", png, "-vf", vf] + enc + [out])
        audio_src = None
    else:
        src = p(seg["file"])
        speed = float(seg.get("speed", 1.0))
        t_in = float(seg.get("in", 0.0)) - Xin * speed
        if t_in < 0:
            print("  WARN seg %d: pre-roll clipped (in=%.2f < %.2f)" % (i, seg.get("in", 0), Xin * speed))
            t_in = 0.0
        span = R * speed + 0.25
        chain = "setpts=(PTS-STARTPTS)/%s" % speed
        interp = seg.get("interp", "mci" if speed < 0.95 else "none")
        if speed < 0.95 and interp == "mci":
            chain += ",minterpolate=fps=%d:mi_mode=mci:mc_mode=aobmc:me_mode=bidir:vsbmc=1" % fps
        elif speed < 0.95 and interp == "blend":
            chain += ",framerate=fps=%d" % fps
        chain += ",fps=%d" % fps
        z = float(seg.get("zoom", 1.0))
        if z > 1.001:
            cx, cy = seg.get("crop_x", 0.5), seg.get("crop_y", 0.5)
            chain += (",crop=iw/%s:ih/%s:(iw-iw/%s)*%s:(ih-ih/%s)*%s" % (z, z, z, cx, z, cy))
        chain += (",scale=%d:%d:force_original_aspect_ratio=decrease:flags=lanczos,pad=%d:%d:(ow-iw)/2:(oh-ih)/2:color=black,setsar=1"
                  % (W, H, W, H))
        if post:
            chain += "," + ",".join(post)
        args = ["-ss", "%.4f" % t_in, "-t", "%.4f" % span, "-i", src]
        layers = []
        if seg.get("caption") or seg.get("sub"):
            cap = os.path.join(work, "cap_%02d.png" % i)
            caption_png(seg, W, H, cap)
            layers.append((cap, Xin + 0.12, max(0.0, R - 0.25)))
        if seg.get("overlay"):
            ovp = os.path.join(work, "ovl_%02d.png" % i)
            overlay_png(seg, W, H, ovp)
            ov = seg["overlay"]
            layers.append((ovp, Xin + ov.get("at", 0.1), min(R - 0.2, Xin + ov.get("until", L - 0.05) - 0.25)))
        if layers:
            fc = ["[0:v]%s[v0]" % chain]
            last = "[v0]"
            for k, (png, st, end) in enumerate(layers):
                args += ["-loop", "1", "-framerate", str(fps), "-t", "%.3f" % (R + 0.5), "-i", png]
                fc.append("[%d:v]format=rgba,fade=in:st=%.3f:d=0.22:alpha=1,fade=out:st=%.3f:d=0.25:alpha=1[c%d]"
                          % (k + 1, st, max(st + 0.3, end), k))
                fc.append("%s[c%d]overlay=0:0:shortest=0:eof_action=pass[v%d]" % (last, k, k + 1))
                last = "[v%d]" % (k + 1)
            args += ["-filter_complex", ";".join(fc), "-map", last]
        else:
            args += ["-vf", chain]
        run(args + enc + [out])
        audio_src = (src, t_in, speed) if (gain is not None and has_audio(src)) else None
    # game audio, same length as the rendered video, with crossfade ramps matching the transitions
    if audio_src is not None:
        src, t_in, speed = audio_src
        af = "asetpts=PTS-STARTPTS"
        if abs(speed - 1.0) > 1e-3:
            af += ",asetrate=%d,aresample=%d" % (int(SR * speed), SR)
        af += ",apad,atrim=0:%.4f,afade=t=in:d=%.3f,afade=t=out:st=%.4f:d=%.3f,volume=%sdB" % (
            R, max(0.01, Xin), max(0.0, R - max(0.01, Xout)), max(0.01, Xout), gain)
        run(["-ss", "%.4f" % t_in, "-t", "%.4f" % (R * speed + 0.25), "-i", src, "-vn", "-af", af,
             "-ar", str(SR), "-ac", "2", "-c:a", "pcm_s16le", wav])
    else:
        run(["-f", "lavfi", "-t", "%.4f" % R, "-i", "anullsrc=r=%d:cl=stereo" % SR, "-c:a", "pcm_s16le", wav])
    return out, wav


def music_cues(cfg, total):
    m = cfg.get("music")
    if not m:
        return []
    if isinstance(m, str):
        fo = cfg.get("music_fade_out", 2.5)
        return [{"file": m, "at": 0.0, "in": 0.0, "dur": total, "gain_db": cfg.get("music_gain_db", 0),
                 "fade_in": 0.6, "fade_out": fo, "loop": True}]
    return m


def mix_audio(cfg, work, wavs, starts, total):
    """game bus (segment wavs at their timeline starts) + music cues + sfx -> two-pass loudnorm wav."""
    inputs, labels, fc = [], [], []
    n = 0
    for w, st in zip(wavs, starts):
        inputs += ["-i", w]
        fc.append("[%d:a]adelay=delays=%d:all=1[g%d]" % (n, int(round(st * 1000)), n))
        labels.append("[g%d]" % n)
        n += 1
    for c in music_cues(cfg, total) + [dict(s, loop=False) for s in cfg.get("sfx", [])]:
        f = p(c["file"])
        if c.get("loop"):
            inputs += ["-stream_loop", "-1"]
        inputs += ["-i", f]
        cin = float(c.get("in", 0.0))
        dur = float(c.get("dur", probe_dur(f) - cin))
        chain = "[%d:a]aresample=%d,aformat=channel_layouts=stereo,atrim=%.4f:%.4f,asetpts=PTS-STARTPTS" % (n, SR, cin, cin + dur)
        if c.get("fade_in", 0) > 0:
            chain += ",afade=t=in:d=%.3f" % c["fade_in"]
        if c.get("fade_out", 0) > 0:
            chain += ",afade=t=out:st=%.4f:d=%.3f" % (max(0.0, dur - c["fade_out"]), c["fade_out"])
        chain += ",volume=%sdB,adelay=delays=%d:all=1[m%d]" % (c.get("gain_db", 0), int(round(float(c.get("at", 0)) * 1000)), n)
        fc.append(chain)
        labels.append("[m%d]" % n)
        n += 1
    fc.append("%samix=inputs=%d:normalize=0:dropout_transition=0,atrim=0:%.4f,afade=t=out:st=%.4f:d=%.3f[mix]"
              % ("".join(labels), n, total, max(0.0, total - 1.0), 1.0))
    raw = os.path.join(work, "mix_raw.wav")
    run(inputs + ["-filter_complex", ";".join(fc), "-map", "[mix]", "-ar", str(SR), "-c:a", "pcm_s24le", raw])
    target = cfg.get("loudness", -14)
    r = subprocess.run([FF, "-hide_banner", "-i", raw, "-af", "loudnorm=I=%s:TP=-1.5:LRA=11:print_format=json" % target,
                        "-f", "null", "-"], capture_output=True, text=True, encoding="utf-8")
    js = json.loads(re.findall(r"\{[^{}]*\"input_i\"[^{}]*\}", r.stderr)[-1])
    final = os.path.join(work, "mix.wav")
    run(["-i", raw, "-af", ("loudnorm=I=%s:TP=-1.5:LRA=11:measured_I=%s:measured_TP=%s:measured_LRA=%s:measured_thresh=%s:"
                            "offset=%s:linear=true" % (target, js["input_i"], js["input_tp"], js["input_lra"],
                                                       js["input_thresh"], js["target_offset"])),
         "-ar", str(SR), "-c:a", "pcm_s24le", final])
    print("  loudness in=%s LUFS -> target %s" % (js["input_i"], target))
    return final


def contact_sheet(video, out_png, every=1.0, cols=8, tw=320):
    tmp = os.path.join(os.path.dirname(out_png), "_sheet_frames")
    shutil.rmtree(tmp, ignore_errors=True)
    os.makedirs(tmp)
    run(["-i", video, "-vf", "fps=1/%s,scale=%d:-2" % (every, tw), os.path.join(tmp, "f_%04d.png")])
    frames = sorted(glob.glob(os.path.join(tmp, "f_*.png")))
    if not frames:
        return
    im0 = Image.open(frames[0])
    th = im0.size[1]
    rows = (len(frames) + cols - 1) // cols
    sheet = Image.new("RGB", (cols * tw, rows * (th + 18)), (20, 20, 22))
    d = ImageDraw.Draw(sheet)
    f = ImageFont.truetype(FONT_LATIN, 14)
    for k, fr in enumerate(frames):
        x, y = (k % cols) * tw, (k // cols) * (th + 18)
        sheet.paste(Image.open(fr), (x, y + 18))
        t = k * every + every / 2
        d.text((x + 4, y + 1), "%d:%05.2f" % (t // 60, t % 60), font=f, fill=(230, 230, 230))
    sheet.save(out_png)
    shutil.rmtree(tmp, ignore_errors=True)
    print("contact sheet ->", out_png)


def main(edl_path, sheet=None, only_sheet=False):
    cfg = json.load(open(edl_path, encoding="utf-8"))
    cfg.setdefault("fps", 60)
    cfg.setdefault("size", [1920, 1080])
    cfg.setdefault("bpm", 120)
    out = p(cfg.get("out", "Builds/PV/Chuon_PV"))
    if only_sheet:
        contact_sheet(out + "_master.mp4", p(sheet), cfg.get("sheet_every", 1.0))
        return
    work = p(cfg.get("work", "Temp/pv_work"))
    os.makedirs(work, exist_ok=True)
    segs = cfg["segments"]
    Ls = [seg_len(s, cfg) for s in segs]
    trs = [trans_of(segs, k, cfg) for k in range(len(segs))]
    T = [sum(Ls[:k]) for k in range(len(segs))]
    total = sum(Ls)
    files, wavs, starts = [], [], []
    for i, s in enumerate(segs):
        Xin = trs[i][1]
        Xout = trs[i + 1][1] if i + 1 < len(segs) else 0.0
        # cache: skip re-rendering a segment whose definition/timing did not change
        key = json.dumps([s, Ls[i], Xin, Xout, cfg["size"], cfg["fps"], cfg.get("game_audio_db", -12)], sort_keys=True, ensure_ascii=False)
        kf = os.path.join(work, "seg_%02d.key" % i)
        v, a = os.path.join(work, "seg_%02d.mp4" % i), os.path.join(work, "seg_%02d.wav" % i)
        if not (os.path.exists(kf) and os.path.exists(v) and os.path.exists(a) and open(kf, encoding="utf-8").read() == key):
            v, a = build_segment(i, s, cfg, work, Ls[i], Xin, Xout)
            open(kf, "w", encoding="utf-8").write(key)
        files.append(v)
        wavs.append(a)
        starts.append(T[i] - Xin)
    # timeline report
    print("\n#  start    len   trans        content")
    for i, s in enumerate(segs):
        what = s.get("caption") or (s.get("lines") or [[""]])[0][0] or s.get("file", "")
        print("%2d %6.2f %6.2f  %-11s  %s %s" % (i, T[i], Ls[i], trs[i][0], s["type"], what))
    print("total %.2f s\n" % total)
    inputs = sum([["-i", f] for f in files], [])
    # same timebase everywhere (concat outputs 1/1000000, mp4 inputs 1/15360 — xfade needs them equal)
    chain = ["[%d:v]settb=AVTB,setpts=PTS-STARTPTS[s%d]" % (k, k) for k in range(len(files))]
    last = "[s0]"
    for k in range(1, len(files)):
        tr, X = trs[k]
        lab = "[x%d]" % k
        if tr == "none":
            chain.append("%s[s%d]concat=n=2:v=1:a=0,settb=AVTB%s" % (last, k, lab))
        else:
            chain.append("%s[s%d]xfade=transition=%s:duration=%.4f:offset=%.4f%s" % (last, k, tr, X, T[k] - X, lab))
        last = lab
    video = os.path.join(work, "video.mp4")
    if len(files) > 1:
        run(inputs + ["-filter_complex", ";".join(chain), "-map", last, "-c:v", "libx264", "-preset", "medium",
                      "-crf", "12", "-pix_fmt", "yuv420p", "-r", str(cfg["fps"]), video])
    else:
        shutil.copy(files[0], video)
    mix = mix_audio(cfg, work, wavs, starts, total)
    os.makedirs(os.path.dirname(out), exist_ok=True)
    vfade = "fade=t=in:d=%s,fade=t=out:st=%.3f:d=0.8" % (cfg.get("fade_in", 0.4), max(0, total - 0.8))
    for name, crf, extra in (("_master.mp4", "16", ["-preset", "slow", "-profile:v", "high", "-tune", "film"]),
                             ("_web.mp4", "20", ["-preset", "slow", "-maxrate", "16M", "-bufsize", "32M"])):
        run(["-i", video, "-i", mix, "-map", "0:v", "-map", "1:a", "-vf", vfade, "-c:a", "aac", "-b:a", "320k",
             "-c:v", "libx264", "-crf", crf, "-pix_fmt", "yuv420p", "-movflags", "+faststart"] + extra
            + ["-t", "%.3f" % total, out + name])
    print("PV done: %.2f s ->" % total, out + "_master.mp4")
    if sheet:
        contact_sheet(out + "_master.mp4", p(sheet), cfg.get("sheet_every", 1.0))


if __name__ == "__main__":
    a = sys.argv[1:]
    sh = a[a.index("--sheet") + 1] if "--sheet" in a else None
    main(a[0], sh, "--only-sheet" in a)
