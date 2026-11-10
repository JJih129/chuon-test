# 반복 14 시네마틱 효과음·짧은 음악 큐 절차 생성 (numpy 만 사용, 결정적, 라이선스 문제 없음)
# 실행: python Tools/ArtGen/cine_audio.py [out_dir]
# 출력: Assets/Art/ChuonPolish/Cinematics/Audio/*.wav (44.1 kHz, 16-bit, 모노)
#   MUS_Cine_Flashback      14 s  어두운 단조 패드 + 느린 심박 (오프닝 회상)
#   MUS_Cine_DawnSwell       8 s  새벽 외경: 낮은 5도 드론이 열리며 상승 (본사 올려다보기)
#   SFX_Cine_RaidAlarm     3.5 s  멀리서 울리는 경보(벽 너머, 저역 통과) + 산발 총성
#   SFX_Cine_LowImpact     1.4 s  컷 전환용 저역 임팩트 (검정 컷, 페이즈 과열)
#   SFX_Cine_ElevatorRide 12.0 s  하강 험 + 층 통과 클랭크(2.4 s 간격) + 조명 끊김 버즈
#   SFX_Cine_HoloGlitch    1.6 s  홀로그램 맺힘: 디지털 글리치 + 상승 스웰
#   SFX_Cine_DroneLaunch   2.6 s  해치 서보 + 상승 휘인 + 바람 소리
#   SFX_Cine_Steam         1.3 s  증기 배출 히스 (Type-D 과열)
#   SFX_Cine_VrBoot        1.8 s  VR 부팅 처프 + 동기화 톤
#   SFX_Cine_VrRelease     1.6 s  VR 해제: 역재생형 스웰 + 저역 드롭
import os, sys, wave
import numpy as np

SR = 44100
ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
OUT = os.path.abspath(sys.argv[1]) if len(sys.argv) > 1 else os.path.join(ROOT, "Assets", "Art", "ChuonPolish", "Cinematics", "Audio")
os.makedirs(OUT, exist_ok=True)
rng = np.random.default_rng(1414)


def t_of(sec):
    return np.arange(int(sec * SR)) / SR


def write(name, x, peak=0.85):
    x = np.asarray(x, dtype=float)
    m = np.max(np.abs(x)) + 1e-9
    x = x / m * peak
    fade = min(len(x) // 8, int(0.01 * SR))
    x[:fade] *= np.linspace(0, 1, fade)
    x[-fade:] *= np.linspace(1, 0, fade)
    data = (np.clip(x, -1, 1) * 32767).astype(np.int16)
    with wave.open(os.path.join(OUT, name + ".wav"), "wb") as w:
        w.setnchannels(1); w.setsampwidth(2); w.setframerate(SR); w.writeframes(data.tobytes())
    print("wrote", name, "%.2fs" % (len(x) / SR))


def lowpass(x, hz):
    a = np.exp(-2 * np.pi * hz / SR)
    y = np.empty_like(x); s = 0.0
    for i in range(len(x)):
        s = (1 - a) * x[i] + a * s
        y[i] = s
    return y


def lowpass_fast(x, hz):
    # 1-pole via FFT magnitude (fast enough for long buffers)
    X = np.fft.rfft(x)
    f = np.fft.rfftfreq(len(x), 1 / SR)
    return np.fft.irfft(X / np.sqrt(1 + (f / hz) ** 2), len(x))


def highpass_fast(x, hz):
    X = np.fft.rfft(x)
    f = np.fft.rfftfreq(len(x), 1 / SR)
    return np.fft.irfft(X * (f / hz) / np.sqrt(1 + (f / hz) ** 2), len(x))


def reverb(x, sec=1.8, mix=0.35):
    n = int(sec * SR)
    ir = rng.standard_normal(n) * np.exp(-np.linspace(0, 7, n))
    ir = lowpass_fast(ir, 5000)
    wet = np.fft.irfft(np.fft.rfft(np.concatenate([x, np.zeros(n)])) * np.fft.rfft(np.concatenate([ir, np.zeros(len(x))])), len(x) + n)[:len(x)]
    wet /= np.max(np.abs(wet)) + 1e-9
    return x * (1 - mix) + wet * mix * np.max(np.abs(x))


def env(t, a, r, total):
    e = np.minimum(1, t / max(a, 1e-4))
    return e * np.clip((total - t) / max(r, 1e-4), 0, 1)


def midi(n):
    return 440.0 * 2 ** ((n - 69) / 12)


def pad(notes, sec, cutoff=900):
    t = t_of(sec); x = np.zeros_like(t)
    for n in notes:
        f = midi(n)
        for d in (-0.07, 0.0, 0.06):
            ff = f * 2 ** (d / 12)
            for k in range(1, 9):
                x += np.sin(2 * np.pi * ff * k * t + rng.uniform(0, 6.28)) / k / np.sqrt(1 + (ff * k / cutoff) ** 4)
    return x


# ------------------------------------------------------------------ cues
def flashback():
    sec = 14.0; t = t_of(sec)
    x = pad([45, 52, 57, 60], sec, 700) * env(t, 2.5, 3.0, sec)  # A minor add9-ish, dark
    beat = np.zeros_like(t)
    for b in np.arange(1.2, sec - 1.0, 1.35):
        for off, g in ((0.0, 1.0), (0.22, 0.6)):
            i0 = int((b + off) * SR); n = int(0.25 * SR)
            if i0 + n < len(t):
                tt = np.arange(n) / SR
                beat[i0:i0 + n] += g * np.sin(2 * np.pi * (52 - 20 * tt) * tt) * np.exp(-tt * 18)
    x = x * 0.55 + beat * 0.9
    write("MUS_Cine_Flashback", reverb(x, 2.4, 0.4))


def dawn():
    sec = 8.0; t = t_of(sec)
    x = pad([38, 45, 50, 57], sec, 400) * env(t, 1.5, 1.6, sec)
    sweep_cut = 400 + 2600 * (t / sec) ** 2
    hi = pad([62, 69, 74], sec, 3000) * (t / sec) ** 2.2
    x = lowpass_fast(x, 900) + 0.35 * hi
    write("MUS_Cine_DawnSwell", reverb(x * env(t, 0.6, 1.8, sec), 2.8, 0.45))


def raid_alarm():
    sec = 3.5; t = t_of(sec)
    lfo = 0.5 + 0.5 * np.sin(2 * np.pi * 0.9 * t)
    f = 520 + 260 * lfo
    ph = 2 * np.pi * np.cumsum(f) / SR
    klaxon = np.sign(np.sin(ph)) * 0.6 + np.sin(ph * 2) * 0.3
    klaxon = lowpass_fast(klaxon, 700)  # behind a wall
    shots = np.zeros_like(t)
    for s in rng.uniform(0.2, sec - 0.2, 9):
        i0 = int(s * SR); n = int(0.12 * SR)
        if i0 + n < len(t):
            shots[i0:i0 + n] += rng.standard_normal(n) * np.exp(-np.arange(n) / SR * 40)
    shots = lowpass_fast(shots, 1800)
    write("SFX_Cine_RaidAlarm", reverb(klaxon * 0.6 + shots * 1.2, 1.4, 0.45))


def low_impact():
    sec = 1.4; t = t_of(sec)
    sub = np.sin(2 * np.pi * (60 * np.exp(-t * 2.2) + 28) * t) * np.exp(-t * 2.4)
    noise = lowpass_fast(rng.standard_normal(len(t)), 400) * np.exp(-t * 9)
    write("SFX_Cine_LowImpact", sub + noise * 0.6)


def elevator_ride():
    sec = 12.0; t = t_of(sec)
    hum = sum(np.sin(2 * np.pi * f * t) / (k + 1) for k, f in enumerate((50, 100, 150, 220)))
    hum *= 0.5 + 0.1 * np.sin(2 * np.pi * 0.3 * t)
    wind = lowpass_fast(rng.standard_normal(len(t)), 300) * (0.4 + 0.6 * np.minimum(1, t / 2.5))
    x = hum * 0.35 + wind * 0.8
    for k, c in enumerate(np.arange(1.6, sec - 1.0, 2.4)):  # floor passes: clank + whoosh
        i0 = int(c * SR); n = int(0.5 * SR)
        tt = np.arange(n) / SR
        clank = (np.sin(2 * np.pi * 180 * tt) + 0.6 * np.sin(2 * np.pi * 431 * tt)) * np.exp(-tt * 14)
        whoosh = lowpass_fast(rng.standard_normal(n), 900) * np.sin(np.pi * tt / 0.5) * 0.7
        x[i0:i0 + n] += clank * 0.8 + whoosh
    buzz_t = (t > 5.6) & (t < 7.4)  # light flicker beat
    buzz = np.sign(np.sin(2 * np.pi * 120 * t)) * 0.15 * buzz_t * (rng.random(len(t)) > 0.3)
    x += lowpass_fast(buzz, 2500)
    x *= env(t, 0.4, 0.8, sec)
    write("SFX_Cine_ElevatorRide", x)


def holo_glitch():
    sec = 1.6; t = t_of(sec)
    swell = pad([69, 76, 81], sec, 4000) * (t / sec) ** 1.5
    gl = np.zeros_like(t)
    for s in np.arange(0.05, 1.1, 0.07):
        i0 = int(s * SR); n = int(rng.uniform(0.015, 0.05) * SR)
        f = rng.uniform(800, 3200)
        gl[i0:i0 + n] += np.sign(np.sin(2 * np.pi * f * np.arange(n) / SR)) * rng.uniform(0.2, 0.6)
    x = swell * 0.4 + highpass_fast(gl, 600) * 0.5
    write("SFX_Cine_HoloGlitch", reverb(x * env(t, 0.02, 0.4, sec), 1.2, 0.3))


def drone_launch():
    sec = 2.6; t = t_of(sec)
    f = 140 + 520 * (t / sec) ** 1.4
    whine = np.sin(2 * np.pi * np.cumsum(f) / SR) + 0.4 * np.sin(4 * np.pi * np.cumsum(f) / SR)
    servo = np.sin(2 * np.pi * 90 * t) * (t < 0.6) * 0.6
    wind = lowpass_fast(rng.standard_normal(len(t)), 1200) * (t / sec)
    write("SFX_Cine_DroneLaunch", (whine * 0.35 + servo + wind * 0.6) * env(t, 0.05, 0.5, sec))


def steam():
    sec = 1.3; t = t_of(sec)
    hiss = highpass_fast(rng.standard_normal(len(t)), 2500)
    write("SFX_Cine_Steam", hiss * env(t, 0.02, 0.9, sec) * (1 + 0.3 * np.sin(2 * np.pi * 13 * t)))


def vr_boot():
    sec = 1.8; t = t_of(sec)
    x = np.zeros_like(t)
    for k, s in enumerate((0.05, 0.22, 0.39, 0.56)):
        i0 = int(s * SR); n = int(0.09 * SR); tt = np.arange(n) / SR
        x[i0:i0 + n] += np.sin(2 * np.pi * (1200 + 300 * k) * tt) * np.exp(-tt * 30)
    tone = np.sin(2 * np.pi * 660 * t) * ((t > 0.8) & (t < 1.6)) * np.minimum(1, (t - 0.8).clip(0) / 0.05)
    write("SFX_Cine_VrBoot", x + tone * 0.45 * env(t, 0.01, 0.25, sec))


def vr_release():
    sec = 1.6; t = t_of(sec)
    sw = pad([64, 71], sec, 3500) * (t / sec) ** 2.5
    drop = np.sin(2 * np.pi * (220 * np.exp(-(t - 1.25).clip(0) * 6)) * t) * (t > 1.25) * np.exp(-(t - 1.25).clip(0) * 5)
    write("SFX_Cine_VrRelease", sw * 0.5 + drop)


if __name__ == "__main__":
    flashback(); dawn(); raid_alarm(); low_impact(); elevator_ride(); holo_glitch(); drone_launch(); steam(); vr_boot(); vr_release()
