"""Iteration 16 Type-D boss overhaul SFX (Python 3 + numpy; deterministic).

Run:
  python Tools/ArtGen/typed_overhaul_audio.py [out_dir]
Writes <out_dir>/Audio/SFX_OH_*.wav (default Assets/Art/ChuonPolish/TypeD/Overhaul), 44.1 kHz mono 16-bit.
Corporate-industrial palette (TSUKASA): metal transients, servo whines, filtered noise, sub thumps. No melodic stingers
(the music system owns the score). Peak normalised to -1.4 dBFS; the mixer applies SFX volume.
"""
import os, sys, wave
import numpy as np

SR = 44100
HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
OUT = os.path.abspath(sys.argv[1]) if len(sys.argv) > 1 else os.path.join(ROOT, "Assets", "Art", "ChuonPolish", "TypeD", "Overhaul")
os.makedirs(os.path.join(OUT, "Audio"), exist_ok=True)
rng = np.random.default_rng(1604)


def t_axis(sec):
    return np.arange(int(SR * sec)) / SR


def write(name, x):
    x = np.asarray(x, dtype=np.float64)
    x = x - np.mean(x[: min(len(x), 64)]) * 0  # keep DC handling explicit (none)
    fade = min(len(x), int(SR * 0.006))
    x[-fade:] *= np.linspace(1, 0, fade)
    peak = max(1e-6, np.max(np.abs(x)))
    x = x / peak * 0.85
    path = os.path.join(OUT, "Audio", name)
    with wave.open(path, "wb") as w:
        w.setnchannels(1); w.setsampwidth(2); w.setframerate(SR)
        w.writeframes((np.clip(x, -1, 1) * 32767).astype("<i2").tobytes())
    print("wrote", path, "%.2fs" % (len(x) / SR))


def onepole_lp(x, a):
    y = np.empty_like(x); acc = 0.0
    for i in range(len(x)):
        acc += (x[i] - acc) * a
        y[i] = acc
    return y


def lp(x, cutoff):
    a = 1 - np.exp(-2 * np.pi * cutoff / SR)
    # vectorised-ish via scipy-free IIR: use lfilter-like recursion in chunks (fast enough for short clips)
    return onepole_lp(x, a)


def hp(x, cutoff):
    return x - lp(x, cutoff)


def env_exp(t, decay):
    return np.exp(-t * decay)


def noise(sec):
    return rng.uniform(-1, 1, int(SR * sec))


def sine_sweep(t, f0, f1):
    f = f0 + (f1 - f0) * (t / max(t[-1], 1e-6))
    return np.sin(2 * np.pi * np.cumsum(f) / SR)


def metal(t, freqs, decay):
    s = np.zeros_like(t)
    for i, f in enumerate(freqs):
        s += np.sin(2 * np.pi * f * t + i) * env_exp(t, decay * (1 + 0.35 * i)) / (1 + i * 0.4)
    return s


def thump(t, f0=70, f1=38, decay=9):
    return sine_sweep(t, f0, f1) * env_exp(t, decay)


def pad(x, sec):
    n = int(SR * sec)
    return np.concatenate([x, np.zeros(max(0, n - len(x)))])[:n] if len(x) < n else x


# wall splat: body slam into steel wall (sub thump + crunchy noise + metal ring)
t = t_axis(1.1)
x = 1.0 * thump(t, 90, 34, 6) + 0.55 * lp(noise(1.1), 2600) * env_exp(t, 14) + 0.25 * metal(t, [430, 690, 1130], 5)
write("SFX_OH_WallSplat.wav", x)

# heavy impact (knockback / down)
t = t_axis(0.8)
x = thump(t, 110, 42, 8) + 0.4 * lp(noise(0.8), 1800) * env_exp(t, 20) + 0.12 * metal(t, [880, 1320], 12)
write("SFX_OH_ImpactHeavy.wav", x)

# node hit: sword on armoured housing (bright clank, short)
t = t_axis(0.35)
x = metal(t, [1650, 2480, 3710, 5200], 22) + 0.35 * hp(noise(0.35), 3000) * env_exp(t, 60)
write("SFX_OH_NodeHit.wav", x)

# node break: electrical burst + glass + power-down whine
t = t_axis(1.4)
crackle = hp(noise(1.4), 2500) * (rng.uniform(0, 1, len(t)) > 0.86) * env_exp(t, 4)
whine = sine_sweep(t, 1400, 180) * env_exp(t, 2.2) * 0.35
x = 0.8 * thump(t, 120, 50, 10) + 0.6 * crackle + whine + 0.3 * metal(t, [2900, 4100, 5600], 9)
write("SFX_OH_NodeBreak.wav", x)

# shutter drop: rattling slats + heavy landing at 1.35 s
t = t_axis(2.0)
rattle = lp(noise(2.0), 900) * (0.5 + 0.5 * np.sin(2 * np.pi * 18 * t)) * np.clip(t / 0.2, 0, 1) * (t < 1.35)
land = np.zeros_like(t); i0 = int(1.35 * SR); tl = t[: len(t) - i0]
land[i0:] = thump(tl, 80, 30, 5) + 0.4 * metal(tl, [310, 520], 6)
x = 0.5 * rattle + land
write("SFX_OH_ShutterDrop.wav", x)

# shutter rise: motor whine + rattle
t = t_axis(1.9)
x = 0.4 * sine_sweep(t, 120, 260) * np.clip(t / 0.3, 0, 1) * np.clip((1.9 - t) / 0.4, 0, 1) + 0.35 * lp(noise(1.9), 700) * (0.5 + 0.5 * np.sin(2 * np.pi * 14 * t))
write("SFX_OH_ShutterRise.wav", x)

# lockdown alarm: two-tone corporate klaxon, 3 cycles (not musical)
t = t_axis(2.4)
tone = np.where((t % 0.8) < 0.4, 620, 465)
sq = np.sign(np.sin(2 * np.pi * np.cumsum(tone) / SR))
x = lp(sq, 2400) * (0.6 + 0.4 * np.clip(np.sin(np.pi * (t % 0.4) / 0.4), 0, 1)) * np.clip((2.4 - t) / 0.3, 0, 1)
write("SFX_OH_LockdownAlarm.wav", x)

# vent charge: pressure build hiss + rising tone (1.3 s)
t = t_axis(1.3)
x = 0.6 * hp(noise(1.3), 1500) * (t / 1.3) ** 1.5 + 0.35 * sine_sweep(t, 200, 900) * (t / 1.3)
write("SFX_OH_VentCharge.wav", x)

# vent blast: steam burst
t = t_axis(1.6)
x = thump(t, 70, 35, 6) + 0.9 * lp(noise(1.6), 5000) * env_exp(t, 2.6) * np.clip(t / 0.02, 0, 1)
write("SFX_OH_VentBlast.wav", x)

# desperation charge: 5 s overload build (sub rumble + rising whine + accelerating ticks)
t = t_axis(5.0)
rumble = lp(noise(5.0), 120) * (0.3 + 0.7 * t / 5.0)
whine = sine_sweep(t, 110, 1450) * (t / 5.0) ** 2 * 0.5
tick_rate = 2 + 14 * (t / 5.0) ** 2
ticks = (np.sin(2 * np.pi * np.cumsum(tick_rate) / SR) > 0.97).astype(float) * hp(noise(5.0), 4000)
x = 0.9 * rumble + whine + 0.35 * ticks
write("SFX_OH_DespCharge.wav", x)

# desperation blast
t = t_axis(2.4)
x = 1.2 * thump(t, 60, 24, 2.5) + 0.8 * lp(noise(2.4), 3500) * env_exp(t, 2.2) + 0.25 * metal(t, [210, 340, 560], 2.4)
write("SFX_OH_DespBlast.wav", x)

# strong parry: bright double clang + air
t = t_axis(1.0)
x = metal(t, [1240, 1870, 2790, 4180], 6) + 0.6 * metal(np.maximum(t - 0.035, 0), [1500, 2250, 3360], 7) * (t > 0.035) + 0.4 * thump(t, 140, 60, 14)
write("SFX_OH_StrongParry.wav", x)

# blink: reversed air + digital zip
t = t_axis(0.45)
zip_ = sine_sweep(t, 3200, 400) * env_exp(t, 9) * 0.5
air = hp(noise(0.45), 2000) * np.clip(t / 0.3, 0, 1) * np.clip((0.45 - t) / 0.08, 0, 1)
x = zip_ + 0.6 * air
write("SFX_OH_Blink.wav", x)

# execution slash: heavy blade through armour (swish + crunch)
t = t_axis(0.9)
swish = hp(noise(0.9), 1800) * np.exp(-((t - 0.06) / 0.05) ** 2)
x = 0.8 * swish + 0.7 * lp(noise(0.9), 2200) * env_exp(np.maximum(t - 0.08, 0), 16) * (t > 0.08) + 0.8 * thump(t, 100, 40, 7) + 0.2 * metal(t, [760, 1210], 10)
write("SFX_OH_ExecSlash.wav", x)

# final execution: core severed (sub drop + crystalline shatter + power-down)
t = t_axis(2.6)
x = 1.1 * thump(t, 80, 22, 1.8) + 0.5 * hp(noise(2.6), 4500) * env_exp(t, 5) + 0.35 * sine_sweep(t, 900, 60) * env_exp(t, 1.4) + 0.3 * metal(t, [3100, 4400, 6200], 4)
write("SFX_OH_ExecFinal.wav", x)

# part break (visor / blade)
t = t_axis(0.9)
x = 0.8 * hp(noise(0.9), 3500) * env_exp(t, 12) + metal(t, [2300, 3500, 5100], 10) * 0.6 + 0.5 * thump(t, 130, 70, 16)
write("SFX_OH_PartBreak.wav", x)

# armour purge: explosive bolts + plates clattering
t = t_axis(2.2)
x = np.zeros_like(t)
for k, at in enumerate([0.0, 0.07, 0.11, 0.19, 0.26, 0.34]):
    i0 = int(at * SR); tt = t[: len(t) - i0]
    x[i0:] += (0.9 - 0.08 * k) * (thump(tt, 160, 70, 18) + 0.5 * hp(rng.uniform(-1, 1, len(tt)), 2500) * env_exp(tt, 30))
for at in [0.7, 0.95, 1.1, 1.3, 1.55, 1.8]:
    i0 = int(at * SR); tt = t[: len(t) - i0]
    x[i0:] += 0.25 * metal(tt, [600 + 300 * rng.random(), 1100 + 500 * rng.random()], 14)
write("SFX_OH_ArmourPurge.wav", x)

# grab whoosh (windup servo + lunge air)
t = t_axis(1.2)
x = 0.4 * sine_sweep(t, 300, 120) * np.clip(t / 0.2, 0, 1) * env_exp(t, 1.5) + 0.7 * hp(noise(1.2), 900) * np.exp(-((t - 0.95) / 0.09) ** 2)
write("SFX_OH_GrabWhoosh.wav", x)

# cover rise: hydraulic thunk
t = t_axis(1.0)
x = 0.5 * lp(noise(1.0), 400) * np.clip(t / 0.1, 0, 1) * (t < 0.8) + 0.8 * thump(np.maximum(t - 0.8, 0), 90, 40, 9) * (t > 0.8)
write("SFX_OH_CoverRise.wav", x)
