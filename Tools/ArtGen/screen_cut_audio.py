# 반복 14S 화면 베기 효과음 절차 생성 (numpy 만 사용, 결정적, 라이선스 문제 없음)
# 실행: python Tools/ArtGen/screen_cut_audio.py [out_dir]
# 출력: Assets/Art/ChuonPolish/Audio/Resources/Audio/*.wav (44.1 kHz, 16-bit, 모노)
#   SFX_ScreenCut_Slash_GEN    0.42 s  정지 화면을 가르는 얇고 날카로운 '싱' (고역 노이즈 스윕 + 비조화 금속 링)
#   SFX_ScreenCut_Sheathe_GEN  0.45 s  납도 '딸깍' (코등이 접촉 저역 틱 + 밝은 금속 팅 + 짧은 잔향)
import os, sys, wave
import numpy as np

SR = 44100
ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
OUT = os.path.abspath(sys.argv[1]) if len(sys.argv) > 1 else os.path.join(ROOT, "Assets", "Art", "ChuonPolish", "Audio", "Resources", "Audio")
os.makedirs(OUT, exist_ok=True)
rng = np.random.default_rng(1419)


def t_of(sec):
    return np.arange(int(sec * SR)) / SR


def write(name, x, peak=0.85):
    x = np.asarray(x, dtype=float)
    x = x / (np.max(np.abs(x)) + 1e-9) * peak
    fade = int(0.004 * SR)
    x[-fade:] *= np.linspace(1, 0, fade)
    data = (np.clip(x, -1, 1) * 32767).astype(np.int16)
    with wave.open(os.path.join(OUT, name + ".wav"), "wb") as w:
        w.setnchannels(1); w.setsampwidth(2); w.setframerate(SR); w.writeframes(data.tobytes())
    print("wrote", name, "%.2fs" % (len(x) / SR))


def band(x, lo, hi):
    X = np.fft.rfft(x)
    f = np.fft.rfftfreq(len(x), 1 / SR)
    g = 1 / np.sqrt(1 + (lo / np.maximum(f, 1)) ** 4) / np.sqrt(1 + (f / hi) ** 4)
    return np.fft.irfft(X * g, len(x))


def small_room(x, sec=0.25, mix=0.18):
    n = int(sec * SR)
    ir = rng.standard_normal(n) * np.exp(-np.linspace(0, 9, n))
    ir = band(ir, 600, 9000)
    wet = np.fft.irfft(np.fft.rfft(np.concatenate([x, np.zeros(n)])) * np.fft.rfft(np.concatenate([ir, np.zeros(len(x))])), len(x) + n)[:len(x)]
    wet /= np.max(np.abs(wet)) + 1e-9
    return x * (1 - mix) + wet * mix * np.max(np.abs(x))


def slash():
    t = t_of(0.42)
    n = len(t)
    # 공기를 가르는 고역 스윕: 노이즈를 시간에 따라 높아지는 대역으로 (짧은 블록 처리)
    noise = rng.standard_normal(n)
    sweep = np.zeros(n)
    blk = 512
    for i in range(0, n, blk):
        k = i / n
        lo = 2500 + 5000 * k
        seg = noise[max(0, i - blk):i + 2 * blk]
        y = band(seg, lo, lo * 2.2)
        off = i - max(0, i - blk)
        sweep[i:i + blk] = y[off:off + min(blk, n - i)]
    env = np.minimum(t / 0.004, 1) * np.exp(-t / 0.05)
    air = sweep * env
    # 비조화 금속 링 (칼날의 '싱')
    ring = np.zeros(n)
    for f, a, d in ((3150, 1.0, 0.16), (4870, 0.7, 0.12), (7420, 0.45, 0.08), (9900, 0.25, 0.05)):
        ring += a * np.sin(2 * np.pi * f * t * (1 + 0.002 * np.exp(-t / 0.03))) * np.exp(-t / d)
    ring *= np.minimum(t / 0.002, 1)
    return small_room(air * 0.9 + ring * 0.35, 0.2, 0.15)


def sheathe():
    t = t_of(0.45)
    n = len(t)
    x = np.zeros(n)
    # 1) 코등이가 닿는 낮은 틱
    tick = band(rng.standard_normal(n), 700, 2600) * np.exp(-t / 0.006)
    x += tick * 0.9
    # 2) 22 ms 뒤 밝은 금속 '팅' (딸깍의 '깍')
    d = int(0.022 * SR)
    tt = t[: n - d]
    tink = np.zeros(n - d)
    for f, a, dec in ((3480, 1.0, 0.07), (5920, 0.6, 0.045), (8310, 0.35, 0.03)):
        tink += a * np.sin(2 * np.pi * f * tt) * np.exp(-tt / dec)
    tink += band(rng.standard_normal(n - d), 3000, 11000) * np.exp(-tt / 0.004) * 0.8
    x[d:] += tink * 0.75
    return small_room(x, 0.3, 0.22)


write("SFX_ScreenCut_Slash_GEN", slash(), 0.8)
write("SFX_ScreenCut_Sheathe_GEN", sheathe(), 0.85)
