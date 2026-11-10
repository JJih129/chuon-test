"""PV trailer score generator for Chuon — self-synthesised (numpy only), licence-safe.

Reuses the instruments of Tools/ArtGen/music_gen.py (same palette as the in-game *_GEN music: D minor,
132 BPM boss ostinato, title drone) and arranges them into a trailer form that matches the PV edit bar-for-bar.

Usage:  python -X utf8 Tools/PV/pv_music.py score.json out.wav
score.json:
{ "bpm": 132, "sections": [ {"kind": "drone", "bars": 8}, {"kind": "riser", "bars": 1}, {"kind": "hit", "bars": 2},
                            {"kind": "build", "bars": 8}, {"kind": "full", "bars": 8}, {"kind": "full2", "bars": 8},
                            {"kind": "gap", "bars": 1}, {"kind": "hero", "bars": 4}, {"kind": "story", "bars": 6},
                            {"kind": "end", "bars": 4} ],
  "hits": [ {"bar": 8} ]   # optional extra impacts at bar positions (float bars allowed)
}
Every section starts with an impact unless "impact": false. Prints the section start times (s).
"""
import json, os, sys, wave
import numpy as np

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "ArtGen"))
import music_gen as mg  # noqa: E402  (main() is guarded; importing only defines instruments)

SR = mg.SR
rng = np.random.default_rng(7)
ROOTS = [38, 38, 41, 36]  # D D F C


def impact(strength=1.0):
    n = int(2.6 * SR)
    t = np.arange(n) / SR
    sub = np.sin(2 * np.pi * np.cumsum(46 + 60 * np.exp(-t / 0.06)) / SR) * np.exp(-t / 0.9)
    k = np.zeros(n); kk = mg.kick(0.6, 140, 40, 0.6); k[:len(kk)] = kk
    tm = np.zeros(n); tt = mg.tom(72, 1.2); tm[:len(tt)] = tt
    me = np.zeros(n); m = mg.metal(196, 1.6, 2.76, 2.0); me[:len(m)] = m
    nz = mg.fft_filter(rng.standard_normal(n), lo=200, hi=5000) * np.exp(-t / 0.35) * 0.25
    mono = sub * 0.9 + k * 0.8 + tm * 0.5 + me * 0.12 + nz
    st = mg.pan(mono, 0.0)
    st[:, 1] = np.roll(st[:, 1], 90)
    return st * strength


def riser(seconds):
    n = int(seconds * SR)
    t = np.arange(n) / SR
    x = rng.standard_normal(n)
    out = np.zeros(n)
    # sweeping band-pass noise via block-wise filtering
    blocks = 24
    for b in range(blocks):
        s, e = b * n // blocks, (b + 1) * n // blocks
        f = 400 * (12 ** (b / blocks))
        out[s:e] = mg.fft_filter(x[s:e], lo=f * 0.5, hi=f * 2.0)
    env = (t / seconds) ** 2.2
    tone = np.sin(2 * np.pi * np.cumsum(110 * 2 ** (2 * t / seconds)) / SR) * 0.25
    mono = (out * 0.7 + tone) * env
    return mg.pan(mono, 0.0) * 0.9


def reverse_swell(seconds, root):
    n = int(seconds * SR)
    v = mg.pad_chord([root + 12, root + 19, root + 24, root + 27], n, 1800, detune_cents=(-10, 0, 10), spread=0.8)
    env = np.linspace(0, 1, n) ** 3
    return v * env[:, None] * 1.4


def drone(buf, s0, n):
    tt = np.arange(n) / SR
    cut = 220 + 260 * (0.5 - 0.5 * np.cos(2 * np.pi * tt / max(1.0, n / SR)))
    v = mg.pad_chord([26, 33, 38, 45], n, cut, detune_cents=(-14, 0, 13), spread=0.8)
    v *= mg.env_adsr(n, 1.5, 0.5, 0.9, 1.5)[:, None]
    mg.add_at(buf, v * 0.9, s0)


def section(buf, kind, s0, bars, beat, root_i0):
    bar = 4 * beat
    n = int(bars * bar * SR)
    if kind == "drone":
        drone(buf, s0, n + int(1.5 * SR))
        hb = mg.kick(0.5, 70, 38, 0.05)
        for b in range(bars):  # heartbeat: lub-dub every bar
            for off, g in ((0.0, 0.55), (0.32, 0.35)):
                mg.add_at(buf, mg.pan(hb * g, 0.0), s0 + (b * bar + off) * SR)
            if b % 2 == 1:
                m = mg.metal(293.7 if b % 4 == 1 else 440.0, 2.5, 2.76, 0.9) * 0.09
                mg.add_at(buf, mg.pan(m, 0.5 if b % 4 == 1 else -0.5), s0 + (b * bar + 2 * beat) * SR)
    elif kind in ("build", "full", "full2"):
        k = mg.kick()
        drone(buf, s0, n + int(0.8 * SR))
        seq = [0, 0, 12, 0, 3, 0, 7, 5]
        for b in range(bars):
            b0 = s0 + b * bar * SR
            root = ROOTS[(root_i0 + b // 2) % 4]
            offs = (0.0, 2.0) if kind == "build" and b < bars // 2 else ((0.0, 1.75, 2.0) if b % 2 == 0 else (0.0, 2.0, 3.5))
            if kind != "build":
                offs = (0.0, 1.0, 1.75, 2.0, 3.0) if kind == "full2" else offs
            for off in offs:
                mg.add_at(buf, mg.pan(k * 0.95, 0.0), b0 + off * beat * SR)
            for i in range(8):
                if kind == "build" and b < bars // 2 and i % 2:
                    continue
                v = mg.pluck_bass(mg.midi(root + seq[i]), beat / 2 * 0.95, cutoff0=900 + (b / bars) * 900, cutoff1=140)
                mg.add_at(buf, mg.pan(v * 0.42, 0.0), b0 + i * beat / 2 * SR)
            for off in (1.0, 3.0):
                m = mg.metal(420 if b % 2 == 0 else 470, 0.35)
                mg.add_at(buf, mg.pan(m * 0.14, 0.5 if off == 1.0 else -0.5), b0 + off * beat * SR)
            if kind in ("full", "full2"):
                h = mg.hat()
                for i in range(16 if kind == "full2" else 8):
                    step = beat / (4 if kind == "full2" else 2)
                    vel = 0.9 if i % 4 == 2 else 0.5
                    mg.add_at(buf, mg.pan(h * 0.2 * vel, 0.3 if i % 2 else -0.15), b0 + i * step * SR)
                s = mg.snare()
                for off in (1.0, 3.0):
                    mg.add_at(buf, mg.pan(s * 0.42, 0.05), b0 + off * beat * SR)
                if b % 2 == 1:
                    sn = int(0.35 * SR)
                    v = mg.pad_chord([root + 24, root + 27, root + 31], sn, 2400, detune_cents=(-7, 7), spread=0.8)
                    v *= mg.exp_decay(sn, 0.08)[:, None]
                    mg.add_at(buf, v * 0.55, b0 + 2.5 * beat * SR)
            if kind == "full2":
                for i in range(16):
                    if i % 4 == 3:
                        continue
                    v = mg.pluck_bass(mg.midi(root + 12 + (12 if i % 8 == 6 else 0)), beat / 4 * 0.9, 2600, 300, 2.4)
                    mg.add_at(buf, mg.pan(v * 0.17, 0.25 * np.sin(i * 0.9)), b0 + i * beat / 4 * SR)
            if b == bars - 1:  # tom fill into the next section
                for j, off in enumerate((3.0, 3.25, 3.5, 3.75)):
                    mg.add_at(buf, mg.pan(mg.tom(110 - j * 12) * 0.55, -0.4 + j * 0.25), b0 + off * beat * SR)
    elif kind == "riser":
        mg.add_at(buf, riser(bars * bar), s0)
        mg.add_at(buf, reverse_swell(bars * bar, 38), s0)
    elif kind == "gap":  # near-silence + reverse swell into the hero downbeat
        mg.add_at(buf, reverse_swell(bars * bar, 38) * 0.8, s0)
        mg.add_at(buf, riser(bars * bar) * 0.6, s0)
    elif kind == "hit":
        drone(buf, s0, n)
    elif kind == "hero":  # half-time: sub booms on 1 every bar, big minor chord swell, heartbeat hats
        v = mg.pad_chord([38, 45, 50, 53, 57], n + int(2 * SR), np.linspace(600, 2600, n + int(2 * SR)),
                         detune_cents=(-12, 0, 12), spread=0.9)
        v *= mg.env_adsr(len(v), 0.2, 0.5, 0.8, 2.0)[:, None]
        mg.add_at(buf, v * 0.7, s0)
        s = mg.snare(0.6)
        for b in range(bars):
            mg.add_at(buf, impact(0.55), s0 + b * bar * SR)
            mg.add_at(buf, mg.pan(s * 0.5, 0.0), s0 + (b * bar + 2 * beat) * SR)
    elif kind == "story":
        chords = [[50, 53, 57, 60, 64], [46, 50, 53, 57], [43, 46, 50, 53, 57], [45, 49, 52, 55]]
        per = max(1, bars // 4) if bars >= 4 else 1
        for c in range(0, bars, per):
            ch = chords[(c // per) % 4]
            cn = int((per * bar + 1.5) * SR)
            v = mg.pad_chord(ch, cn, 900, detune_cents=(-8, 0, 8), spread=0.7)
            v *= mg.env_adsr(cn, 0.8, 0.4, 0.85, 1.5)[:, None]
            mg.add_at(buf, v * 0.55, s0 + c * bar * SR)
        for b in range(bars):  # slow pulse
            mg.add_at(buf, mg.pan(mg.kick(0.5, 70, 38, 0.05) * 0.35, 0.0), s0 + b * bar * SR)
            mg.add_at(buf, mg.pan(mg.metal(587.3, 2.0, 2.76, 0.7) * 0.05, 0.4), s0 + (b * bar + 3 * beat) * SR)
    elif kind == "end":
        v = mg.pad_chord([38, 45, 50, 57, 62], n + int(3 * SR), 1400, detune_cents=(-10, 0, 10), spread=0.9)
        v *= mg.env_adsr(len(v), 0.05, 1.0, 0.5, 3.5)[:, None]
        mg.add_at(buf, v * 0.6, s0)


def main(score_path, out_path):
    sc = json.load(open(score_path, encoding="utf-8"))
    bpm = float(sc.get("bpm", 132))
    beat = 60.0 / bpm
    bar = 4 * beat
    total_bars = sum(s["bars"] for s in sc["sections"])
    n = int((total_bars * bar + 5.0) * SR)
    buf = np.zeros((n, 2))
    pos = 0
    root_i = 0
    print("section starts (s):")
    for s in sc["sections"]:
        s0 = pos * bar * SR
        print("  bar %5.1f  t=%6.2f  %-6s %d bars" % (pos, pos * bar, s["kind"], s["bars"]))
        if s.get("impact", s["kind"] not in ("drone", "riser", "gap")):
            mg.add_at(buf, impact(1.0 if s["kind"] in ("hit", "hero", "end", "full", "full2") else 0.75), s0)
        section(buf, s["kind"], s0, s["bars"], beat, root_i)
        root_i += s["bars"] // 2
        pos += s["bars"]
    for h in sc.get("hits", []):
        mg.add_at(buf, impact(h.get("gain", 0.8)), h["bar"] * bar * SR)
    ir = mg.reverb_ir(2.2, 0.6, 6500, 11)
    x = mg.linear_reverb(buf, ir, 0.2)[: n]
    x = mg.master(x, -16.0, ceiling_db=-1.5)
    x = x[: int((total_bars * bar + 4.0) * SR)]
    x[-int(3.5 * SR):] *= np.linspace(1, 0, int(3.5 * SR))[:, None] ** 1.5
    data = (np.clip(x, -1, 1) * 32767).astype("<i2")
    os.makedirs(os.path.dirname(os.path.abspath(out_path)), exist_ok=True)
    with wave.open(out_path, "wb") as w:
        w.setnchannels(2); w.setsampwidth(2); w.setframerate(SR); w.writeframes(data.tobytes())
    print("score %.2f s (%d bars @ %g BPM) -> %s" % (len(x) / SR, total_bars, bpm, out_path))


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
