# 추온 임시 BGM / SFX 절차 생성 (반복 11) — 라이선스 문제 없는 자체 합성 (numpy 만 사용)
# 실행: python Tools/ArtGen/music_gen.py
# 출력: Assets/Art/ChuonPolish/Audio/Resources/Audio/Music/*_GEN.wav (44.1 kHz, 16-bit, 스테레오)
#       Assets/Art/ChuonPolish/Audio/Resources/Audio/Heartbeat_Low_GEN.wav
#
# 루프 이음새: 드라이 신호를 루프 길이 L + 꼬리로 렌더한 뒤 꼬리를 앞쪽에 접어 더하고(랩어라운드),
# 리버브는 길이 L 의 원형 합성곱(FFT)으로 걸어서 마지막 샘플 → 첫 샘플이 수학적으로 이어진다 (클릭 없음).
# LFO 는 모두 루프 길이 안에서 정수 주기.
# 실제 곡이 확정되면 같은 이름 규칙(*_GEN 제거)으로 교체하고 MusicDirector.TrackFor 경로만 바꾸면 된다.
import os, sys, wave
import numpy as np

SR = 44100
ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
OUT_MUSIC = os.path.join(ROOT, "Assets", "Art", "ChuonPolish", "Audio", "Resources", "Audio", "Music")
OUT_SFX = os.path.join(ROOT, "Assets", "Art", "ChuonPolish", "Audio", "Resources", "Audio")
os.makedirs(OUT_MUSIC, exist_ok=True)
rng = np.random.default_rng(20261110)

def midi(n):
    return 440.0 * 2.0 ** ((n - 69) / 12.0)

# ------------------------------------------------------------------ 기본 합성
def saw(freq, n, cutoff, phase=0.0, nharm=None, t0=0):
    """대역 제한 톱니파 (가산 합성). cutoff: 스칼라 또는 길이 n 배열(Hz)."""
    t = (np.arange(n) + t0) / SR
    if nharm is None:
        nharm = int(min(48, max(1, (SR * 0.45) // freq)))
    out = np.zeros(n)
    cut = np.asarray(cutoff, dtype=float)
    cmax = float(np.max(cut))
    for k in range(1, nharm + 1):
        fk = freq * k
        if fk > SR * 0.45 or fk > cmax * 3.5:
            break
        g = 1.0 / k / np.sqrt(1.0 + (fk / cut) ** 4)
        out += g * np.sin(2 * np.pi * fk * t + phase * k)
    return out

def env_adsr(n, a, d, s, r, sustain_len=None):
    a_n, d_n, r_n = int(a * SR), int(d * SR), int(r * SR)
    if sustain_len is None:
        sustain_len = max(0, n - a_n - d_n - r_n)
    e = np.concatenate([
        np.linspace(0, 1, max(1, a_n), endpoint=False),
        np.linspace(1, s, max(1, d_n), endpoint=False),
        np.full(max(0, sustain_len), s),
        np.linspace(s, 0, max(1, r_n)),
    ])
    if len(e) < n:
        e = np.concatenate([e, np.zeros(n - len(e))])
    return e[:n]

def exp_decay(n, tau):
    return np.exp(-np.arange(n) / (tau * SR))

def fft_filter(x, lo=None, hi=None):
    X = np.fft.rfft(x)
    f = np.fft.rfftfreq(len(x), 1.0 / SR)
    g = np.ones_like(f)
    if hi is not None:
        g *= 1.0 / np.sqrt(1.0 + (f / hi) ** 4)
    if lo is not None:
        g *= 1.0 / np.sqrt(1.0 + (lo / np.maximum(f, 1e-3)) ** 4)
    return np.fft.irfft(X * g, n=len(x))

def pan(mono, p):
    # p: -1 (좌) .. 1 (우), 등전력
    a = (p + 1) * np.pi / 4
    return np.stack([mono * np.cos(a), mono * np.sin(a)], axis=1)

def add_at(buf, sig, start):
    start = int(start)
    end = min(len(buf), start + len(sig))
    if end > start:
        buf[start:end] += sig[: end - start]

def fold_loop(buf, L):
    out = buf[:L].copy()
    tail = buf[L:]
    i = 0
    while i < len(tail):
        seg = tail[i:i + L]
        out[: len(seg)] += seg
        i += L
    return out

def reverb_ir(seconds, decay, bright=6000.0, seed=1):
    r = np.random.default_rng(seed)
    n = int(seconds * SR)
    t = np.arange(n) / SR
    irs = []
    for ch in range(2):
        noise = r.standard_normal(n) * np.exp(-t / decay)
        noise = fft_filter(noise, lo=120.0, hi=bright)
        noise[: int(0.012 * SR)] *= np.linspace(0, 1, int(0.012 * SR))
        irs.append(noise / np.sqrt(np.sum(noise ** 2)))
    return np.stack(irs, axis=1)

def circular_reverb(x, ir, wet):
    L = len(x)
    out = np.zeros_like(x)
    for ch in range(2):
        h = np.zeros(L)
        m = min(L, len(ir))
        h[:m] = ir[:m, ch]
        if len(ir) > L:  # IR 가 루프보다 길면 접어 넣는다
            extra = ir[L:, ch]
            for i in range(0, len(extra), L):
                seg = extra[i:i + L]
                h[: len(seg)] += seg
        y = np.fft.irfft(np.fft.rfft(x[:, ch]) * np.fft.rfft(h), n=L)
        out[:, ch] = y
    return x * (1 - wet) + out * wet * 2.2

def linear_reverb(x, ir, wet):
    n = len(x) + len(ir)
    out = np.zeros((n, 2))
    for ch in range(2):
        N = 1 << int(np.ceil(np.log2(n)))
        y = np.fft.irfft(np.fft.rfft(x[:, ch], N) * np.fft.rfft(ir[:, ch], N), N)[:n]
        out[:, ch] = y
    dry = np.zeros((n, 2)); dry[: len(x)] = x
    return dry * (1 - wet) + out * wet * 2.2

def soft_limit(x, ceiling_db=-3.0):
    c = 10 ** (ceiling_db / 20)
    return np.tanh(x / c) * c

def master(x, rms_db, ceiling_db=-3.0):
    rms = np.sqrt(np.mean(x ** 2)) + 1e-12
    x = x * (10 ** (rms_db / 20) / rms)
    peak = np.max(np.abs(x))
    c = 10 ** (ceiling_db / 20)
    if peak > c * 0.9:
        x = soft_limit(x / (c * 0.9) * c * 0.9 * 1.0, ceiling_db)
    return x

def write_wav(path, x):
    x = np.clip(x, -1, 1)
    data = (x * 32767).astype("<i2")
    with wave.open(path, "wb") as w:
        w.setnchannels(2)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(data.tobytes())
    rms = 20 * np.log10(np.sqrt(np.mean(x ** 2)) + 1e-12)
    peak = 20 * np.log10(np.max(np.abs(x)) + 1e-12)
    seam = np.max(np.abs(x[0] - x[-1]))
    print(f"{os.path.basename(path):34s} len={len(x)/SR:6.2f}s  peak={peak:6.2f} dBFS  rms={rms:6.2f} dBFS  seamJump={seam:.4f}")

# ------------------------------------------------------------------ 악기
def pad_chord(notes, n, cutoff_curve, detune_cents=(-9, 0, 8), spread=0.6, t0=0):
    out = np.zeros((n, 2))
    for i, note in enumerate(notes):
        for j, dc in enumerate(detune_cents):
            f = midi(note) * 2 ** (dc / 1200.0)
            v = saw(f, n, cutoff_curve, phase=rng.random() * 6.28, t0=t0)
            p = ((j - 1) * spread + (i - len(notes) / 2) * 0.12)
            out += pan(v, float(np.clip(p, -0.9, 0.9)))
    return out / (len(notes) * len(detune_cents))

def kick(length=0.45, f0=118.0, f1=44.0, click=0.35):
    n = int(length * SR)
    t = np.arange(n) / SR
    f = f1 + (f0 - f1) * np.exp(-t / 0.045)
    ph = 2 * np.pi * np.cumsum(f) / SR
    body = np.sin(ph) * np.exp(-t / 0.22)
    cl = rng.standard_normal(n) * np.exp(-t / 0.004) * click
    cl = fft_filter(cl, lo=1500, hi=9000)
    return body + cl

def tom(f0=96.0, length=0.6):
    n = int(length * SR)
    t = np.arange(n) / SR
    f = f0 * (1 + 0.35 * np.exp(-t / 0.03))
    body = np.sin(2 * np.pi * np.cumsum(f) / SR) * np.exp(-t / 0.3)
    skin = fft_filter(rng.standard_normal(n), lo=200, hi=1800) * np.exp(-t / 0.05) * 0.25
    return body + skin

def metal(f=420.0, length=0.5, ratio=1.414, index=3.0):
    n = int(length * SR)
    t = np.arange(n) / SR
    idx = index * np.exp(-t / 0.08)
    mod = np.sin(2 * np.pi * f * ratio * t) * idx
    return np.sin(2 * np.pi * f * t + mod) * np.exp(-t / 0.16)

def snare(length=0.3):
    n = int(length * SR)
    t = np.arange(n) / SR
    tone = np.sin(2 * np.pi * 186 * t) * np.exp(-t / 0.05) * 0.5
    nz = fft_filter(rng.standard_normal(n), lo=900, hi=9000) * np.exp(-t / 0.11)
    return tone + nz * 0.8

def hat(length=0.08, decay=0.025):
    n = int(length * SR)
    t = np.arange(n) / SR
    return fft_filter(rng.standard_normal(n), lo=6500, hi=15000) * np.exp(-t / decay)

def pluck_bass(freq, length, cutoff0=1400.0, cutoff1=160.0, drive=1.6):
    n = int(length * SR)
    t = np.arange(n) / SR
    cut = cutoff1 + (cutoff0 - cutoff1) * np.exp(-t / 0.09)
    v = saw(freq, n, cut, nharm=28) + 0.5 * np.sin(2 * np.pi * freq * 0.5 * t)
    v = np.tanh(v * drive) / np.tanh(drive)
    return v * env_adsr(n, 0.004, 0.12, 0.55, 0.05)

# ------------------------------------------------------------------ 곡
def lobby_pad():
    # 84 BPM, 8마디 (D 단조 계열: Dm9 - Bbmaj7 - Gm9 - A7sus) — 어두운 사이버펑크 패드 + 펄스
    bpm = 84.0
    beat = 60.0 / bpm
    bars = 8
    L = int(round(bars * 4 * beat * SR))
    tail = int(3.0 * SR)
    buf = np.zeros((L + tail, 2))
    chords = [[50, 57, 60, 64, 65], [46, 53, 57, 62, 65], [43, 50, 53, 57, 62], [45, 52, 55, 59, 64]]
    chord_len = 2 * 4 * beat
    t_all = np.arange(L) / SR
    # 컷오프 LFO: 루프 안에 정확히 2 주기
    for ci, ch in enumerate(chords):
        start = int(ci * chord_len * SR)
        n = int((chord_len + 1.6) * SR)
        tt = (np.arange(n) + start) / SR
        cut = 700 + 500 * (0.5 + 0.5 * np.sin(2 * np.pi * 2 * tt / (L / SR)))
        v = pad_chord(ch, n, cut, t0=start)
        e = env_adsr(n, 1.2, 0.5, 0.85, 1.6, sustain_len=int((chord_len - 1.7) * SR))
        add_at(buf, v * e[:, None] * 0.9, start)
    # 서브 베이스 드론 (코드 루트)
    for ci, ch in enumerate(chords):
        start = int(ci * chord_len * SR)
        n = int((chord_len + 0.4) * SR)
        f = midi(ch[0] - 12)
        tt = np.arange(n) / SR
        v = np.sin(2 * np.pi * f * tt) * env_adsr(n, 0.3, 0.2, 0.9, 0.4, sustain_len=int((chord_len - 0.5) * SR))
        add_at(buf, pan(v * 0.55, 0.0), start)
    # 16분 펄스 아르페지오 (필터 플럭) — 조용히
    step = beat / 4
    pattern = [0, 2, 4, 2, 3, 4, 1, 4]
    for i in range(bars * 16):
        ci = int((i * step) // chord_len) % 4
        note = chords[ci][pattern[i % len(pattern)]] + 12
        n = int(0.22 * SR)
        tt = np.arange(n) / SR
        cut = 300 + 2200 * np.exp(-tt / 0.05) * (0.6 + 0.4 * np.sin(2 * np.pi * i / 32.0))
        v = saw(midi(note), n, cut, nharm=24) * exp_decay(n, 0.07)
        accent = 1.0 if i % 4 == 0 else 0.65
        add_at(buf, pan(v * 0.22 * accent, 0.35 * np.sin(i * 0.7)), i * step * SR)
    # 공기 (밴드 노이즈)
    air = fft_filter(rng.standard_normal(L + tail), lo=2500, hi=7000) * 0.02
    buf += np.stack([air, np.roll(air, 3001)], axis=1)
    x = fold_loop(buf, L)
    x = circular_reverb(x, reverb_ir(3.2, 1.1, 5000, 3), 0.42)
    return master(x, -21.0)

def boss_loops():
    # 132 BPM, 16마디 — P1: 킥/톰/메탈 히트/베이스 오스티나토/드론, P2 레이어: 16분 하이햇·스네어·옥타브 베이스·스탭
    bpm = 132.0
    beat = 60.0 / bpm
    bars = 16
    L = int(round(bars * 4 * beat * SR))
    tail = int(2.5 * SR)
    p1 = np.zeros((L + tail, 2))
    p2 = np.zeros((L + tail, 2))
    root_seq = [38, 38, 41, 36]  # D, D, F, C (4마디씩)
    k = kick()
    for b in range(bars):
        bar0 = b * 4 * beat
        # 킥: 1, 2.75(싱코페이션), 3
        for off in (0.0, 1.75, 2.0) if b % 2 == 0 else (0.0, 2.0, 3.5):
            add_at(p1, pan(k * 0.95, 0.0), (bar0 + off * beat) * SR)
        # 톰 필 (4마디마다 끝)
        if b % 4 == 3:
            for j, off in enumerate((3.0, 3.25, 3.5, 3.75)):
                add_at(p1, pan(tom(110 - j * 12) * 0.55, -0.4 + j * 0.25), (bar0 + off * beat) * SR)
        # 메탈 히트 (2, 4 박 약하게) — 사이버 질감
        for off in (1.0, 3.0):
            m = metal(420 if b % 2 == 0 else 470, 0.35)
            add_at(p1, pan(m * 0.16, 0.5 if off == 1.0 else -0.5), (bar0 + off * beat) * SR)
    # 베이스 오스티나토 (8분)
    seq = [0, 0, 12, 0, 3, 0, 7, 5]
    for i in range(bars * 8):
        bar = i // 8
        root = root_seq[(bar // 4) % 4]
        note = root + seq[i % 8]
        v = pluck_bass(midi(note), beat / 2 * 0.95, cutoff0=1100, cutoff1=140)
        add_at(p1, pan(v * 0.42, 0.0), i * beat / 2 * SR)
    # 드론 패드 (루트 + 5도, 매우 어둡게)
    for seg in range(4):
        root = root_seq[seg]
        start = int(seg * 4 * 4 * beat * SR)
        n = int((4 * 4 * beat + 1.0) * SR)
        tt = (np.arange(n) + start) / SR
        cut = 380 + 200 * (0.5 + 0.5 * np.sin(2 * np.pi * 4 * tt / (L / SR)))
        v = pad_chord([root + 12, root + 19, root + 24], n, cut, detune_cents=(-12, 0, 11), spread=0.7, t0=start)
        e = env_adsr(n, 0.6, 0.3, 0.9, 1.0, sustain_len=int((4 * 4 * beat - 0.9) * SR))
        add_at(p1, v * e[:, None] * 0.5, start)

    # ---- P2 레이어 ----
    h = hat()
    for i in range(bars * 16):
        vel = 0.9 if i % 4 == 2 else (0.55 if i % 2 == 0 else 0.35)
        add_at(p2, pan(h * 0.22 * vel, 0.3 if i % 2 else -0.15), i * beat / 4 * SR)
    s = snare()
    for b in range(bars):
        bar0 = b * 4 * beat
        for off in (1.0, 3.0):
            add_at(p2, pan(s * 0.42, 0.05), (bar0 + off * beat) * SR)
        if b % 4 == 3:
            for j in range(4):
                add_at(p2, pan(s * (0.18 + 0.06 * j), -0.2 + 0.13 * j), (bar0 + (3.0 + j * 0.25) * beat) * SR)
    # 옥타브 위 베이스 (16분, 공격적)
    for i in range(bars * 16):
        if i % 4 == 3:
            continue
        bar = i // 16
        root = root_seq[(bar // 4) % 4] + 12
        note = root + (12 if i % 8 == 6 else 0)
        v = pluck_bass(midi(note), beat / 4 * 0.9, cutoff0=2600, cutoff1=300, drive=2.4)
        add_at(p2, pan(v * 0.2, 0.25 * np.sin(i * 0.9)), i * beat / 4 * SR)
    # 스탭 (2마디마다 오프비트 코드 히트)
    for b in range(0, bars, 2):
        root = root_seq[(b // 4) % 4]
        n = int(0.35 * SR)
        v = pad_chord([root + 24, root + 27, root + 31], n, 2400, detune_cents=(-7, 7), spread=0.8)
        v *= exp_decay(n, 0.08)[:, None]
        add_at(p2, v * 0.6, (b * 4 * beat + 2.5 * beat) * SR)

    ir = reverb_ir(1.6, 0.45, 7000, 5)
    x1 = circular_reverb(fold_loop(p1, L), ir, 0.22)
    x2 = circular_reverb(fold_loop(p2, L), ir, 0.18)
    # 같은 게인으로 마스터 (레이어를 더했을 때 상대 레벨 유지)
    g1 = 10 ** (-19.5 / 20) / (np.sqrt(np.mean(x1 ** 2)) + 1e-12)
    x1 *= g1
    x2 *= g1 * 1.5
    both_peak = np.max(np.abs(x1 + x2))
    c = 10 ** (-3.0 / 20)
    if both_peak > c:
        x1 *= c / both_peak
        x2 *= c / both_peak
    return x1, x2

def victory_sting():
    # 상승 스웰 → 히트 → 쉬머 꼬리 (D 장조 해결), 약 6초
    n = int(6.5 * SR)
    buf = np.zeros((n, 2))
    sw_n = int(1.2 * SR)
    tt = np.arange(sw_n) / SR
    cut = 300 + 3500 * (tt / tt[-1]) ** 2
    sw = pad_chord([50, 57, 62, 66, 69], sw_n, cut)
    sw *= (np.linspace(0, 1, sw_n) ** 2)[:, None]
    add_at(buf, sw * 0.8, 0)
    hit0 = int(1.2 * SR)
    add_at(buf, pan(kick(0.8, 90, 38, 0.5) * 1.0, 0), hit0)
    add_at(buf, pan(tom(70, 1.2) * 0.6, 0), hit0)
    hn = int(4.8 * SR)
    hv = pad_chord([38, 50, 57, 62, 66, 69, 74], hn, 2600 * np.exp(-np.arange(hn) / SR / 1.6) + 500)
    hv *= env_adsr(hn, 0.01, 0.6, 0.6, 3.6)[:, None]
    add_at(buf, hv * 1.1, hit0)
    # 쉬머 (높은 벨 FM)
    for j, note in enumerate([74, 78, 81, 86]):
        m = metal(midi(note), 2.4, ratio=3.5, index=1.2)
        add_at(buf, pan(m * 0.12, -0.6 + 0.4 * j), hit0 + int((0.08 + j * 0.11) * SR))
    x = linear_reverb(buf, reverb_ir(3.0, 0.9, 8000, 7), 0.35)
    x = x[: int(7.5 * SR)]
    fade = np.ones(len(x)); fade[-int(1.2 * SR):] = np.linspace(1, 0, int(1.2 * SR)) ** 2
    x *= fade[:, None]
    return master(x, -18.0)

def title_drone():
    # 32 초 드론: 낮은 D 5도 + 천천히 열리는 필터, 금속성 울림이 드문드문
    L = int(32.0 * SR)
    tail = int(4 * SR)
    buf = np.zeros((L + tail, 2))
    tt = np.arange(L + tail) / SR
    cut = 220 + 260 * (0.5 + 0.5 * np.sin(2 * np.pi * tt / 32.0))
    v = pad_chord([26, 33, 38, 45], L + tail, cut, detune_cents=(-14, 0, 13), spread=0.8)
    buf += v * 0.9
    for t0, f in ((3.0, 293.7), (11.0, 440.0), (19.0, 349.2), (27.0, 261.6)):
        m = metal(f, 3.5, ratio=2.76, index=0.9) * 0.08
        add_at(buf, pan(m, 0.5 * np.sin(t0)), t0 * SR)
    air = fft_filter(rng.standard_normal(L + tail), lo=1800, hi=5000) * 0.015
    buf += np.stack([air, np.roll(air, 4410)], axis=1)
    x = fold_loop(buf, L)
    x = circular_reverb(x, reverb_ir(4.0, 1.6, 4000, 9), 0.5)
    return master(x, -23.0)

def tutorial_ambient():
    # 튜토리얼: 거의 무음에 가까운 시설 공조음 + 드문 패드 (기획: 튜토 무음 → 아주 낮게)
    L = int(32.0 * SR)
    tail = int(4 * SR)
    buf = np.zeros((L + tail, 2))
    hum = fft_filter(rng.standard_normal(L + tail), lo=60, hi=400) * 0.12
    buf += np.stack([hum, np.roll(hum, 2205)], axis=1)
    tt = np.arange(L + tail) / SR
    buf += pan(np.sin(2 * np.pi * 55.0 * tt) * 0.05 * (0.6 + 0.4 * np.sin(2 * np.pi * tt / 16.0)), 0)
    for t0, ch in ((2.0, [62, 65, 69]), (18.0, [60, 64, 67])):
        n = int(9 * SR)
        v = pad_chord(ch, n, 600, detune_cents=(-6, 6))
        v *= env_adsr(n, 3.0, 1.0, 0.7, 4.0)[:, None]
        add_at(buf, v * 0.45, t0 * SR)
    x = fold_loop(buf, L)
    x = circular_reverb(x, reverb_ir(3.5, 1.3, 4500, 11), 0.45)
    return master(x, -27.0)

def credits_pad():
    # 76 BPM, 8마디: 따뜻한 F 장조 계열 + 느린 아르페지오
    bpm = 76.0
    beat = 60.0 / bpm
    bars = 8
    L = int(round(bars * 4 * beat * SR))
    tail = int(3.5 * SR)
    buf = np.zeros((L + tail, 2))
    chords = [[53, 60, 64, 69], [50, 57, 60, 65], [46, 53, 57, 62], [48, 55, 60, 64]]
    chord_len = 2 * 4 * beat
    for ci, ch in enumerate(chords):
        start = int(ci * chord_len * SR)
        n = int((chord_len + 1.8) * SR)
        tt = (np.arange(n) + start) / SR
        cut = 900 + 400 * (0.5 + 0.5 * np.sin(2 * np.pi * tt / (L / SR)))
        v = pad_chord(ch, n, cut, t0=start)
        e = env_adsr(n, 1.4, 0.6, 0.8, 1.8, sustain_len=int((chord_len - 1.9) * SR))
        add_at(buf, v * e[:, None] * 0.85, start)
    step = beat / 2
    pat = [0, 1, 2, 3, 2, 1]
    for i in range(bars * 8):
        ci = int((i * step) // chord_len) % 4
        note = chords[ci][pat[i % len(pat)]] + 12
        n = int(0.6 * SR)
        m = metal(midi(note), 0.6, ratio=2.0, index=0.7) * 0.1
        add_at(buf, pan(m, 0.4 * np.sin(i)), i * step * SR)
    x = fold_loop(buf, L)
    x = circular_reverb(x, reverb_ir(3.4, 1.2, 6000, 13), 0.45)
    return master(x, -21.0)

def heartbeat_low():
    # 0.8 초 루프 '쿵-쿵' (ActionColourDirector 의 비네트 박동과 같은 위상: 0.064 s, 0.24 s)
    L = int(0.8 * SR)
    buf = np.zeros((L + int(0.5 * SR), 2))
    for t0, amp, f in ((0.064, 1.0, 52.0), (0.24, 0.7, 47.0)):
        n = int(0.35 * SR)
        t = np.arange(n) / SR
        ff = f * (1 + 0.6 * np.exp(-t / 0.02))
        thump = np.sin(2 * np.pi * np.cumsum(ff) / SR) * np.exp(-t / 0.09)
        thump *= np.minimum(1.0, t / 0.006)
        thump = np.tanh(thump * 1.5)
        add_at(buf, pan(thump * amp, 0), (t0 - 0.006) * SR)
    x = fold_loop(buf, L)
    x = fft_filter(x[:, 0], hi=220.0)
    x = np.stack([x, x], axis=1)
    return master(x, -20.0, ceiling_db=-6.0)

def main():
    only = set(sys.argv[1:])
    def want(name):
        return not only or name in only
    if want("lobby"):
        write_wav(os.path.join(OUT_MUSIC, "Lobby_Pad_GEN.wav"), lobby_pad())
    if want("boss"):
        b1, b2 = boss_loops()
        write_wav(os.path.join(OUT_MUSIC, "Boss_Loop_GEN.wav"), b1)
        write_wav(os.path.join(OUT_MUSIC, "Boss_P2Layer_GEN.wav"), b2)
        both = np.clip(b1 + b2, -1, 1)
        print(f"{'(Boss P1+P2 summed)':34s} peak={20*np.log10(np.max(np.abs(both))):6.2f} dBFS  rms={20*np.log10(np.sqrt(np.mean(both**2))):6.2f} dBFS")
    if want("victory"):
        write_wav(os.path.join(OUT_MUSIC, "Victory_Sting_GEN.wav"), victory_sting())
    if want("title"):
        write_wav(os.path.join(OUT_MUSIC, "Title_Drone_GEN.wav"), title_drone())
    if want("tutorial"):
        write_wav(os.path.join(OUT_MUSIC, "Tutorial_Ambient_GEN.wav"), tutorial_ambient())
    if want("credits"):
        write_wav(os.path.join(OUT_MUSIC, "Credits_Pad_GEN.wav"), credits_pad())
    if want("heart"):
        write_wav(os.path.join(OUT_SFX, "Heartbeat_Low_GEN.wav"), heartbeat_low())

if __name__ == "__main__":
    main()
