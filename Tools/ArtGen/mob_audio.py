"""TSUKASA mob SFX (plain Python 3, stdlib only; deterministic).

Run:
  python mob_audio.py [out_dir]
Writes <out_dir>/Audio/ (default Assets/Art/ChuonPolish/Mobs/Audio):
  SFX_Mob_Alert.wav        'spotted' corporate security chirp: two rising sine blips + soft square edge (0.26 s)
  SFX_Mob_TurretShot.wav   short suppressed burst round: click transient + filtered noise tail (0.12 s)
  SFX_Mob_PowerDown.wav    servo wind-down for deaths/turret stun (0.6 s)
44.1 kHz mono 16-bit.
"""
import math, os, random, struct, sys, wave

SR = 44100
here = os.path.dirname(os.path.abspath(__file__))
root = os.path.abspath(os.path.join(here, "..", ".."))
out = os.path.abspath(sys.argv[1]) if len(sys.argv) > 1 else os.path.join(root, "Assets", "Art", "ChuonPolish", "Mobs")
os.makedirs(os.path.join(out, "Audio"), exist_ok=True)
rnd = random.Random(4412)


def write(name, samples):
    peak = max(1e-6, max(abs(s) for s in samples))
    gain = 0.85 / peak
    path = os.path.join(out, "Audio", name)
    with wave.open(path, "wb") as w:
        w.setnchannels(1); w.setsampwidth(2); w.setframerate(SR)
        w.writeframes(b"".join(struct.pack("<h", int(max(-1, min(1, s * gain)) * 32767)) for s in samples))
    print("wrote", path)


def env(t, a, d, total):
    if t < a:
        return t / a
    return max(0.0, 1.0 - (t - a) / max(1e-6, total - a)) ** d


# alert chirp: blip 1 (1180 Hz -> 1320) then blip 2 (1560 -> 1760)
alert = []
for i in range(int(SR * 0.26)):
    t = i / SR
    s = 0.0
    for t0, f0, f1 in ((0.0, 1180, 1320), (0.11, 1560, 1760)):
        tt = t - t0
        if 0 <= tt < 0.1:
            f = f0 + (f1 - f0) * tt / 0.1
            ph = 2 * math.pi * (f0 * tt + 0.5 * (f1 - f0) * tt * tt / 0.1)
            e = env(tt, 0.004, 1.6, 0.1)
            s += e * (0.8 * math.sin(ph) + 0.12 * (1 if math.sin(ph) > 0 else -1))
    alert.append(s)
write("SFX_Mob_Alert.wav", alert)

# turret round
shot = []
lp = 0.0
for i in range(int(SR * 0.12)):
    t = i / SR
    n = rnd.uniform(-1, 1)
    lp += (n - lp) * 0.25
    click = math.exp(-t * 900) * math.sin(2 * math.pi * 2400 * t)
    body = math.exp(-t * 38) * lp
    thump = math.exp(-t * 60) * math.sin(2 * math.pi * 140 * t)
    shot.append(0.7 * click + 0.9 * body + 0.5 * thump)
write("SFX_Mob_TurretShot.wav", shot)

# power down: falling servo whine + crackle
pd = []
ph = 0.0
for i in range(int(SR * 0.6)):
    t = i / SR
    f = 900 * math.exp(-t * 3.2) + 60
    ph += 2 * math.pi * f / SR
    crack = rnd.uniform(-1, 1) if rnd.random() < 0.02 * math.exp(-t * 4) else 0.0
    pd.append(env(t, 0.01, 1.3, 0.6) * (0.6 * math.sin(ph) + 0.25 * math.sin(2 * ph)) + 0.5 * crack)
write("SFX_Mob_PowerDown.wav", pd)
