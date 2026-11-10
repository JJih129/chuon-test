// 추온 액션 컬러 디렉터 (반복 11)
// 흑백 '흡수' 연출 + 포스트 이벤트(패리 색수차/비네트, 브레이크 채도 딥, 저체력 붉은 비네트, 퍼펙트 회피 쿨 틴트)
// 와 화면 베기(ScreenCut, 반복 14S) 타이밍을 실시간(unscaled) 기준으로 굴려 ActionColourState 에 써 넣는다.
// 모든 세기는 접근성 '화면 번쩍임' 슬라이더(AccessibilityOptionsRuntime.FlashIntensity)로 제한된다.
//
// 사용:
//   ActionColourDirector.Pulse(1f, 0.3f);                         // 짧은 흑백 (추온/Type-D/칼·검기는 색 유지)
//   ActionColourDirector.BeginAbsorb(owner, swordTransform, 1.2f); // 궁극기: 외곽 → 칼날로 흡수, 유지
//   ActionColourDirector.EndAbsorb(owner, 0.45f);                   // 색 복귀 (코어에서 바깥으로)
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

[DefaultExecutionOrder(900)]
public sealed class ActionColourDirector : MonoBehaviour
{
    // ------------------------------------------------------------------ 튜닝 상수
    const float SweepMaxRadius = 2.1f;     // 화면 구석보다 바깥 (종횡비 보정 uv 단위)
    const float SweepMinRadius = -0.35f;   // 0 보다 작으면 화면 전체가 흑백
    const float SweepSoftness = 0.22f;
    const float LowHpStart = 0.3f;
    const float LowHpFull = 0.1f;

    static ActionColourDirector s_instance;

    // ------------------------------------------------------------------ 흑백 채널
    struct BwChannel
    {
        public bool Active;
        public bool Sustained;
        public object Owner;
        public float StartTime;
        public float Duration;
        public float SweepIn;
        public float FadeOut;
        public float FadeStart;      // 지속형 종료 시각 (-1 = 아직)
        public float Strength;
        public float RingScale;
        public Transform Focus;
        public Vector3 WorldPoint;
        public bool HasWorldPoint;
        public float FadeFromDesat;
        public float FadeFromRadius;
    }

    BwChannel _bw;

    // ------------------------------------------------------------------ 포스트 이벤트
    float _caStart = -1f, _caDuration, _caAmount;
    float _vigStart = -1f, _vigDuration, _vigAmount;
    float _dipStart = -1f, _dipDuration, _dipAmount;
    float _tintStart = -1f, _tintDuration, _tintAmount;
    float _lowHpTarget;
    float _lowHpCurrent;
    float _heartPhase;

    // ------------------------------------------------------------------ 화면 베기 (반복 14S)
    static readonly Color CutCoreColor = new Color(1f, 0.985f, 0.97f, 1f);
    static readonly Color CutCalmCoreColor = new Color(0.8f, 0.81f, 0.83f, 1f);
    static readonly Color CutRedColor = new Color(0.9f, 0.07f, 0.09f, 1f);   // 츠카사 레드
    static readonly Color CutGapColor = new Color(0.022f, 0.022f, 0.026f, 1f);   // 그래파이트 (선형)
    const float CutFlashFrames = 0.034f;   // 분리 순간 반전/흰 빛, 끝 스냅 — 60 fps 기준 2 프레임
    const float CutCalmSoft = 0.45f;

    bool _cutActive;
    float _cutStart;
    float _cutPace = 1f;          // calm 모드에선 느리게 (선·분리 모두)
    float _cutCalm;
    ScreenCutPattern _cut;
    bool _cutSplitStarted;
    bool _cutSkipped;
    float _cutSplitStartReal;
    float _cutSplitDurReal;
    int _cutSfxFired;             // 비트 i = 선 i 슬래시, 비트 7 = 납도 클릭
    System.Action _cutOnSplit;    // 라이브 화면으로 스냅되는 순간 (끝/건너뛰기/중단) 한 번
    AudioClip _cutSlashClip;
    AudioClip _cutClickClip;
    bool _cutAudioLoaded;

    // ------------------------------------------------------------------ 씬 참조
    PlayerHealth _playerHealth;
    BossController _boss;
    Transform _playerRoot;
    readonly HashSet<int> _markedRoots = new HashSet<int>();
    readonly List<Renderer> _rendererBuffer = new List<Renderer>(64);
    int _prewarmFrames;
    float _nextSceneProbeAt;
    AudioSource _heartbeat;
    AudioClip _heartbeatClip;
    bool _heartbeatLoaded;

    /// <summary>QA: 마지막 펄스 정보.</summary>
    public static string LastEvent { get; private set; } = "none";
    public static int PulseCount { get; private set; }
    public static bool IsAbsorbing => s_instance != null && s_instance._bw.Active && s_instance._bw.Sustained;
    public static float CurrentDesaturation => ActionColourState.Current.Desaturation;

    // ================================================================== 정적 API

    /// <summary>짧은 흑백 펄스. strength 0..1, duration 실시간 초. keepSubjects 는 색을 유지할 추가 루트.</summary>
    public static void Pulse(float strength, float duration, IList<Transform> keepSubjects = null)
    {
        Ensure()?.StartPulse(strength, duration, keepSubjects, null, false, 1f);
    }

    /// <summary>월드 지점을 중심으로 흡수되는 흑백 펄스.</summary>
    public static void PulseAt(Vector3 worldPoint, float strength, float duration, IList<Transform> keepSubjects = null)
    {
        Ensure()?.StartPulse(strength, duration, keepSubjects, worldPoint, false, 1f);
    }

    /// <summary>지속형 흑백 흡수 (궁극기). focus 쪽으로 sweepDuration 동안 세계가 빠져나간다. EndAbsorb 까지 유지.</summary>
    public static void BeginAbsorb(object owner, Transform focus, float sweepDuration, IList<Transform> keepSubjects = null)
    {
        ActionColourDirector d = Ensure();
        if (d == null)
            return;
        d.MarkKeepSubjects(keepSubjects);
        d.MarkDefaultSubjects();
        d._bw = new BwChannel
        {
            Active = true,
            Sustained = true,
            Owner = owner,
            StartTime = Time.unscaledTime,
            Duration = float.PositiveInfinity,
            SweepIn = Mathf.Max(0.05f, sweepDuration),
            FadeOut = 0.45f,
            FadeStart = -1f,
            Strength = 1f,
            RingScale = 1f,
            Focus = focus
        };
        LastEvent = "absorb";
        PulseCount++;
    }

    /// <summary>흡수 초점을 바꾼다 (컷이 바뀌어도 칼날을 따라가게).</summary>
    public static void SetAbsorbFocus(object owner, Transform focus)
    {
        if (s_instance != null && s_instance._bw.Active && s_instance._bw.Owner == owner)
            s_instance._bw.Focus = focus;
    }

    public static void EndAbsorb(object owner, float fadeDuration)
    {
        if (s_instance == null || !s_instance._bw.Active || !s_instance._bw.Sustained)
            return;
        if (owner != null && s_instance._bw.Owner != owner)
            return;
        s_instance.BeginBwFade(Mathf.Max(0.01f, fadeDuration));
    }

    public static void CancelAll()
    {
        if (s_instance == null)
            return;
        s_instance._bw = default;
        s_instance.StopCutInternal();
    }

    /// <summary>퍼펙트 패리: 짧은 흑백 + 색수차 + 비네트 펄스.</summary>
    public static void NotifyParry(Vector3 worldPoint)
    {
        ActionColourDirector d = Ensure();
        if (d == null)
            return;
        d.StartPulse(1f, 0.3f, null, worldPoint, false, 0.6f);
        float now = Time.unscaledTime;
        d._caStart = now; d._caDuration = 0.26f; d._caAmount = 0.014f;
        d._vigStart = now; d._vigDuration = 0.32f; d._vigAmount = 0.55f;
        LastEvent = "parry";
    }

    /// <summary>처형(피니셔) 임팩트: 0.6 초 흑백.</summary>
    public static void NotifyFinisher(Vector3 worldPoint)
    {
        ActionColourDirector d = Ensure();
        if (d == null)
            return;
        d.StartPulse(1f, 0.6f, null, worldPoint, false, 1f);
        d._caStart = Time.unscaledTime; d._caDuration = 0.35f; d._caAmount = 0.009f;
        LastEvent = "finisher";
    }

    /// <summary>브레이크 시작: 0.3 초 채도 딥 + 로우패스.</summary>
    public static void NotifyBreakStart()
    {
        ActionColourDirector d = Ensure();
        if (d == null)
            return;
        d._dipStart = Time.unscaledTime; d._dipDuration = 0.3f; d._dipAmount = 0.7f;
        ChuonAudioMix.LowpassDip(0.3f);
        LastEvent = "break";
        PulseCount++;
    }

    /// <summary>퍼펙트 회피: 기존 슬로모 동안 차가운 틴트.</summary>
    public static void NotifyPerfectDodge(float duration = 0.55f)
    {
        ActionColourDirector d = Ensure();
        if (d == null)
            return;
        d._tintStart = Time.unscaledTime; d._tintDuration = Mathf.Max(0.15f, duration); d._tintAmount = 0.75f;
        LastEvent = "perfectDodge";
        PulseCount++;
    }

    /// <summary>Type-D 페이즈 전환: 흑백 흡수 0.8 초.</summary>
    public static void NotifyBossPhaseChange(Vector3 worldPoint)
    {
        Ensure()?.StartPulse(0.85f, 0.8f, null, worldPoint, false, 0.8f);
        LastEvent = "bossPhase";
    }

    /// <summary>패스/RT/셰이더/파편 메시를 미리 돌린다 (보이지 않음). 도착 연출 중에 호출.</summary>
    public static void Prewarm()
    {
        ActionColourDirector d = Ensure();
        if (d == null)
            return;
        d.MarkDefaultSubjects();
        d.LoadCutAudio();
        d._prewarmFrames = 3;
        ActionColourState.SetPrewarm(true);
        ActionColourState.RequestCutCapture();
    }

    // ------------------------------------------------------------------ 화면 베기 (ScreenCut 이 호출)

    internal static void StartCut(in ScreenCutPattern pattern, System.Action onSplit)
    {
        ActionColourDirector d = Ensure();
        if (d == null || pattern.Count <= 0)
        {
            onSplit?.Invoke();
            return;
        }
        if (d._cutActive)
            d.StopCutInternal();   // 이전 베기의 분리 콜백은 그대로 한 번 발사

        float flash = AccessibilityOptionsRuntime.FlashIntensity;
        d._cut = pattern;
        d._cut.Count = Mathf.Min(pattern.Count, ActionColourState.CutMaxLines);
        d._cutCalm = 1f - Mathf.Clamp01(flash / 0.25f);
        d._cutPace = Mathf.Lerp(1f, 1.5f, d._cutCalm);
        d._cutActive = true;
        d._cutStart = Time.unscaledTime;
        d._cutSplitStarted = false;
        d._cutSkipped = false;
        d._cutSfxFired = 0;
        d._cutOnSplit = onSplit;
        d.LoadCutAudio();
        ActionColourState.RequestCutCapture();
        ActionColourState.SetCutActive(true);
        d.WriteCut(0f, 0f, flash);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (s_qaCutOffsets != null)
        {
            for (int k = 0; k < s_qaCutOffsets.Length; k++)
                s_qaCaptures.Add((d._cutStart + s_qaCutOffsets[k], s_qaCutPrefix + "_" + k.ToString("00") + ".png"));
            s_qaCutOffsets = null;
        }
#endif
        LastEvent = "cut";
        PulseCount++;
    }

    internal static void SkipCut()
    {
        ActionColourDirector d = s_instance;
        if (d == null || !d._cutActive)
            return;
        float now = Time.unscaledTime;
        d._cutSkipped = true;
        d._cutSfxFired = ~0;   // 건너뛰면 남은 효과음은 생략
        if (!d._cutSplitStarted)
        {
            // 선은 즉시 완성 → 바로 짧게 갈라진다
            d._cutStart = now - d._cut.SplitAt * d._cutPace;
            return;
        }
        // 분리 중: 현재 진행에서 남은 부분을 0.15 s 안에
        float s = Mathf.Clamp01((now - d._cutSplitStartReal) / d._cutSplitDurReal);
        float dur = 0.15f / Mathf.Max(0.05f, 1f - s);
        if (dur < d._cutSplitDurReal)
        {
            d._cutSplitDurReal = dur;
            d._cutSplitStartReal = now - s * dur;
        }
    }

    internal static bool CutPlaying => s_instance != null && s_instance._cutActive;

    // ================================================================== 수명

    static ActionColourDirector Ensure()
    {
        if (s_instance != null)
            return s_instance;
        if (!Application.isPlaying)
            return null;
        var go = new GameObject("__ActionColourDirector");
        DontDestroyOnLoad(go);
        s_instance = go.AddComponent<ActionColourDirector>();
        return s_instance;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        Ensure();
    }

    void OnEnable()
    {
        SceneManager.sceneLoaded += HandleSceneLoaded;
        LobbyManager.LowerFloorReached += HandleLowerFloorReached;
        ProbeScene(true);
    }

    void OnDisable()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        LobbyManager.LowerFloorReached -= HandleLowerFloorReached;
        Unsubscribe();
        ActionColourState.ClearColour();
        ActionColourState.SetCutActive(false);
        ActionColourState.SetPrewarm(false);
    }

    void OnDestroy()
    {
        if (s_instance == this)
            s_instance = null;
    }

    void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        _bw = default;
        StopCutInternal();
        _lowHpTarget = 0f;
        _lowHpCurrent = 0f;
        _markedRoots.Clear();
        ProbeScene(true);
    }

    void HandleLowerFloorReached(LobbyManager lobby)
    {
        Prewarm();
    }

    void ProbeScene(bool force)
    {
        if (!force && Time.unscaledTime < _nextSceneProbeAt)
            return;
        _nextSceneProbeAt = Time.unscaledTime + 2f;

        if (_playerHealth == null)
        {
            Unsubscribe();
            _playerHealth = FindObjectOfType<PlayerHealth>();
            if (_playerHealth != null)
            {
                _playerHealth.OnHPChanged += HandlePlayerHp;
                _playerRoot = _playerHealth.transform;
                HandlePlayerHp(_playerHealth.CurrentHP, _playerHealth.MaxHP);
                // 게임플레이 씬: 첫 프레임 직후 프리웜 (도착 연출이 가려 준다)
                _prewarmPending = true;
            }
        }

        if (_boss == null)
        {
            _boss = FindObjectOfType<BossController>();
            if (_boss != null)
                _boss.OnBossPhaseChanged += HandleBossPhase;
        }
    }

    bool _prewarmPending;

    void Unsubscribe()
    {
        if (_playerHealth != null)
            _playerHealth.OnHPChanged -= HandlePlayerHp;
        if (_boss != null)
            _boss.OnBossPhaseChanged -= HandleBossPhase;
        _playerHealth = null;
        _boss = null;
        _playerRoot = null;
    }

    void HandlePlayerHp(int current, int max)
    {
        float ratio = max > 0 ? (float)current / max : 1f;
        _lowHpTarget = current <= 0 ? 0f : Mathf.Clamp01(Mathf.InverseLerp(LowHpStart, LowHpFull, ratio));
        if (ratio > LowHpStart)
            _lowHpTarget = 0f;
    }

    void HandleBossPhase(int phase, float hpNormalized)
    {
        if (phase >= 2 && _boss != null)
            NotifyBossPhaseChange(_boss.transform.position + Vector3.up * 1.6f);
    }

    // ================================================================== 내부

    void StartPulse(float strength, float duration, IList<Transform> keepSubjects, Vector3? worldPoint, bool sustained, float ringScale)
    {
        MarkKeepSubjects(keepSubjects);
        MarkDefaultSubjects();

        // 지속형 흡수(궁극기) 중엔 짧은 펄스가 덮어쓰지 않는다
        if (_bw.Active && _bw.Sustained && _bw.FadeStart < 0f)
            return;

        float now = Time.unscaledTime;
        duration = Mathf.Max(0.08f, duration);
        _bw = new BwChannel
        {
            Active = true,
            Sustained = sustained,
            StartTime = now,
            Duration = duration,
            SweepIn = Mathf.Clamp(duration * 0.3f, 0.06f, 0.25f),
            FadeOut = Mathf.Clamp(duration * 0.4f, 0.1f, 0.3f),
            FadeStart = -1f,
            Strength = Mathf.Clamp01(strength),
            RingScale = ringScale,
            Focus = worldPoint.HasValue ? null : _playerRoot,
            WorldPoint = worldPoint ?? Vector3.zero,
            HasWorldPoint = worldPoint.HasValue
        };
        LastEvent = "pulse";
        PulseCount++;
    }

    void BeginBwFade(float duration)
    {
        float now = Time.unscaledTime;
        EvaluateBw(now, out float desat, out float radius, out _);
        _bw.FadeFromDesat = desat;
        _bw.FadeFromRadius = radius;
        _bw.FadeStart = now;
        _bw.FadeOut = duration;
    }

    void MarkDefaultSubjects()
    {
        if (_playerRoot == null || _boss == null)
            ProbeScene(false);
        if (_playerRoot != null)
            MarkRoot(_playerRoot.root);
        if (_boss != null)
            MarkRoot(_boss.transform);
    }

    void MarkKeepSubjects(IList<Transform> subjects)
    {
        if (subjects == null)
            return;
        for (int i = 0; i < subjects.Count; i++)
        {
            if (subjects[i] != null)
                MarkRoot(subjects[i]);
        }
    }

    /// <summary>루트 아래 렌더러에 KeepColour 비트를 켠다 (한 번만; 비트는 이 효과 외엔 아무 영향 없음).</summary>
    public static void MarkKeepColour(Transform root, bool force = false)
    {
        ActionColourDirector d = Ensure();
        if (d == null || root == null)
            return;
        if (force)
            d._markedRoots.Remove(root.GetInstanceID());
        d.MarkRoot(root);
    }

    void MarkRoot(Transform root)
    {
        if (root == null)
            return;
        int id = root.GetInstanceID();
        if (!_markedRoots.Add(id))
            return;
        _rendererBuffer.Clear();
        root.GetComponentsInChildren(true, _rendererBuffer);
        for (int i = 0; i < _rendererBuffer.Count; i++)
        {
            Renderer r = _rendererBuffer[i];
            if (r == null || r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer)
                continue;
            r.renderingLayerMask |= ActionColourState.KeepColourLayerMask;
        }
        _rendererBuffer.Clear();
    }

    void Update()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (s_qaNextFrame.Count > 0)
        {
            var list = s_qaNextFrame.ToArray();
            s_qaNextFrame.Clear();
            foreach (var a in list)
                a?.Invoke();
        }
#endif
        if (_prewarmPending && Time.unscaledTime > 0.5f)
        {
            _prewarmPending = false;
            Prewarm();
        }

        if (_prewarmFrames > 0)
        {
            _prewarmFrames--;
            if (_prewarmFrames == 0)
                ActionColourState.SetPrewarm(false);
        }

        if (_playerHealth == null || _boss == null)
            ProbeScene(false);
    }

    void LateUpdate()
    {
        float now = Time.unscaledTime;
        float flash = AccessibilityOptionsRuntime.FlashIntensity;
        ActionColourState.Snapshot s = ActionColourState.Default();
        Camera cam = Camera.main;

        // ---- 흑백 ----
        if (_bw.Active)
        {
            bool alive = EvaluateBw(now, out float desat, out float radius, out float ring);
            if (!alive)
            {
                _bw = default;
            }
            else if (AccessibilityOptionsRuntime.BwAbsorbEnabled)   // 접근성: 흑백 끄기면 채도/링 모두 생략 (타이밍은 그대로 진행)
            {
                float floor = _bw.Sustained ? 0.6f : 0.35f;
                s.Desaturation = desat * Mathf.Lerp(floor, 1f, flash);
                s.SweepRadius = radius;
                s.SweepSoftness = SweepSoftness;
                s.RingIntensity = ring * _bw.RingScale * flash;
                s.Center = ResolveCenter(cam);
            }
        }

        // ---- 색수차 / 비네트 펄스 ----
        float caEnv = Envelope(now, _caStart, _caDuration, 0.03f);
        s.ChromaticAberration = _caAmount * caEnv * flash;
        float vigEnv = Envelope(now, _vigStart, _vigDuration, 0.04f);
        float pulseVig = _vigAmount * vigEnv * flash;

        // ---- 저체력 붉은 비네트 + 심장 박동 ----
        _lowHpCurrent = Mathf.MoveTowards(_lowHpCurrent, _lowHpTarget, Time.unscaledDeltaTime * 0.8f);
        float lowVig = 0f;
        if (_lowHpCurrent > 0.001f && !GameTimeScale.IsPaused)
        {
            _heartPhase += Time.unscaledDeltaTime * Mathf.Lerp(1.05f, 1.45f, _lowHpCurrent);
            float beat = HeartbeatShape(_heartPhase);
            lowVig = _lowHpCurrent * (0.22f + 0.14f * beat) * Mathf.Lerp(0.5f, 1f, flash);
        }
        UpdateHeartbeatAudio(_lowHpCurrent);

        if (pulseVig + lowVig > 0.001f)
        {
            float total = pulseVig + lowVig;
            Color dark = new Color(0f, 0f, 0f, 0f);
            Color red = new Color(0.62f, 0.03f, 0.03f, 0.55f);
            s.VignetteColor = Color.Lerp(dark, red, lowVig / total);
            s.VignetteAmount = Mathf.Min(0.85f, total);
        }

        // ---- 채도 딥 ----
        s.SaturationDip = _dipAmount * Envelope(now, _dipStart, _dipDuration, 0.05f) * flash;

        // ---- 쿨 틴트 ----
        float tintEnv = Envelope(now, _tintStart, _tintDuration, 0.06f);
        s.TintAmount = _tintAmount * tintEnv * Mathf.Lerp(0.5f, 1f, flash);
        s.Tint = new Color(0.8f, 0.93f, 1.14f, 1f);

        ActionColourState.SetColour(s, true);

        // ---- 화면 베기 ----
        if (_cutActive)
            TickCut(now, flash);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        for (int i = s_qaCaptures.Count - 1; i >= 0; i--)
        {
            if (now >= s_qaCaptures[i].at)
            {
                ScreenCapture.CaptureScreenshot(s_qaCaptures[i].path);
                s_qaCaptures.RemoveAt(i);
            }
        }
#endif
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    static readonly List<(float at, string path)> s_qaCaptures = new List<(float, string)>();

    /// <summary>QA: delay 초(실시간) 뒤 프레임 끝에서 화면 캡처.</summary>
    public static void QaCaptureAfter(float delay, string path)
    {
        Ensure();
        s_qaCaptures.Add((Time.unscaledTime + Mathf.Max(0f, delay), path));
    }

    static readonly List<System.Action> s_qaNextFrame = new List<System.Action>();

    /// <summary>QA: 다음 프레임 Update 에서 실행 (MCP 호출 직후의 에디터 정지 시간을 피한다).</summary>
    public static void QaNextFrame(System.Action action)
    {
        Ensure();
        s_qaNextFrame.Add(action);
    }

    static float[] s_qaCutOffsets;
    static string s_qaCutPrefix;

    /// <summary>QA: 다음 화면 베기가 시작되면 시작 기준 offsets(실시간 s)마다 캡처 (prefix_00.png …).</summary>
    public static void QaArmCutCaptures(string prefix, float[] offsets)
    {
        Ensure();
        s_qaCutPrefix = prefix;
        s_qaCutOffsets = offsets;
    }

    /// <summary>QA(오프라인 렌더): 패턴을 실시간 t 초 시점으로 평가해 ActionColourState.Cut 에 쓴다.</summary>
    public static void QaEvaluateCut(in ScreenCutPattern pattern, float t, float flash, float aspect)
    {
        float calm = 1f - Mathf.Clamp01(flash / 0.25f);
        float pace = Mathf.Lerp(1f, 1.5f, calm);
        float te = t / pace;
        bool split = te >= pattern.SplitAt;
        float splitDur = Mathf.Max(0.05f, pattern.SplitDuration * pace);
        float s = split ? Mathf.Clamp01((t - pattern.SplitAt * pace) / splitDur) : 0f;
        ActionColourState.CutData c = ActionColourState.Cut;
        EvaluateCut(pattern, te, s, split, splitDur, calm, flash, aspect, c);
        c.Count = Mathf.Min(pattern.Count, ActionColourState.CutMaxLines);
    }

    /// <summary>QA: 저체력 비네트/심장 박동 강제 (ratio 1 = 해제).</summary>
    public static void QaSetHealthRatio(float ratio)
    {
        ActionColourDirector d = Ensure();
        if (d != null)
            d.HandlePlayerHp(Mathf.RoundToInt(ratio * 100f), 100);
    }
#endif

    bool EvaluateBw(float now, out float desat, out float radius, out float ring)
    {
        desat = 0f;
        radius = SweepMaxRadius;
        ring = 0f;
        float t = now - _bw.StartTime;

        if (_bw.FadeStart >= 0f || (!_bw.Sustained && t >= _bw.Duration - _bw.FadeOut))
        {
            // 색 복귀: 반경이 코어에서 바깥으로 넓어지며 세기도 줄어든다
            float fadeStart = _bw.FadeStart >= 0f ? _bw.FadeStart : _bw.StartTime + _bw.Duration - _bw.FadeOut;
            if (_bw.FadeStart < 0f)
            {
                _bw.FadeStart = fadeStart;
                _bw.FadeFromDesat = _bw.Strength;
                _bw.FadeFromRadius = SweepMinRadius;
            }
            float f = Mathf.Clamp01((now - fadeStart) / Mathf.Max(0.01f, _bw.FadeOut));
            if (f >= 1f)
                return false;
            float e = f * f * (3f - 2f * f);
            // 색 복귀: 반경이 코어(0)에서 바깥으로 커지며 안쪽부터 색이 돌아온다. 끝부분에서 세기도 0 으로.
            desat = _bw.FadeFromDesat * (1f - e * e);
            radius = Mathf.Lerp(Mathf.Max(-0.05f, _bw.FadeFromRadius), SweepMaxRadius, e);
            ring = 0f;
            return true;
        }

        float k = Mathf.Clamp01(t / Mathf.Max(0.01f, _bw.SweepIn));
        float ease = 1f - (1f - k) * (1f - k);
        desat = _bw.Strength * Mathf.Clamp01(k * 3f);
        radius = Mathf.Lerp(SweepMaxRadius, SweepMinRadius, ease);
        ring = k < 1f ? 0.4f * (1f - k * 0.5f) : 0f;
        return true;
    }

    Vector2 ResolveCenter(Camera cam)
    {
        if (cam == null)
            return new Vector2(0.5f, 0.5f);
        Vector3 world;
        if (_bw.Focus != null)
            world = _bw.Focus.position + (_bw.Focus == _playerRoot ? Vector3.up * 1.3f : Vector3.zero);
        else if (_bw.HasWorldPoint)
            world = _bw.WorldPoint;
        else
            return new Vector2(0.5f, 0.5f);
        Vector3 vp = cam.WorldToViewportPoint(world);
        if (vp.z <= 0f)
            return new Vector2(0.5f, 0.5f);
        return new Vector2(Mathf.Clamp(vp.x, -0.2f, 1.2f), Mathf.Clamp(vp.y, -0.2f, 1.2f));
    }

    static float Envelope(float now, float start, float duration, float attack)
    {
        if (start < 0f || duration <= 0f)
            return 0f;
        float t = now - start;
        if (t < 0f || t >= duration)
            return 0f;
        if (t < attack)
            return t / attack;
        float r = (t - attack) / Mathf.Max(0.001f, duration - attack);
        return 1f - r * r * (3f - 2f * r);
    }

    static float HeartbeatShape(float phase)
    {
        float p = phase - Mathf.Floor(phase);
        // 두 번 뛰는 박동 (쿵-쿵)
        float a = Mathf.Exp(-Mathf.Pow((p - 0.08f) / 0.05f, 2f));
        float b = 0.7f * Mathf.Exp(-Mathf.Pow((p - 0.3f) / 0.06f, 2f));
        return Mathf.Clamp01(a + b);
    }

    void UpdateHeartbeatAudio(float amount)
    {
        if (amount <= 0.001f || GameTimeScale.IsPaused)
        {
            if (_heartbeat != null && _heartbeat.isPlaying)
            {
                _heartbeat.volume = Mathf.MoveTowards(_heartbeat.volume, 0f, Time.unscaledDeltaTime * 1.5f);
                if (_heartbeat.volume <= 0.001f)
                    _heartbeat.Stop();
            }
            return;
        }

        if (!_heartbeatLoaded)
        {
            _heartbeatLoaded = true;
            _heartbeatClip = Resources.Load<AudioClip>("Audio/Heartbeat_Low_GEN");
        }
        if (_heartbeatClip == null)
            return;

        if (_heartbeat == null)
        {
            _heartbeat = gameObject.AddComponent<AudioSource>();
            _heartbeat.playOnAwake = false;
            _heartbeat.loop = true;
            _heartbeat.spatialBlend = 0f;
            _heartbeat.ignoreListenerPause = false;
            _heartbeat.clip = _heartbeatClip;
            _heartbeat.volume = 0f;
            ChuonAudioMix.Route(_heartbeat, ChuonAudioMix.Bus.Sfx);
        }

        float target = AudioOptionsRuntime.ScaleSfx(0.55f * amount);
        _heartbeat.volume = Mathf.MoveTowards(_heartbeat.volume, target, Time.unscaledDeltaTime * 0.8f);
        if (!_heartbeat.isPlaying)
            _heartbeat.Play();
    }

    // ------------------------------------------------------------------ 화면 베기

    void LoadCutAudio()
    {
        if (_cutAudioLoaded)
            return;
        _cutAudioLoaded = true;
        _cutSlashClip = Resources.Load<AudioClip>("Audio/SFX_ScreenCut_Slash_GEN");
        _cutClickClip = Resources.Load<AudioClip>("Audio/SFX_ScreenCut_Sheathe_GEN");
    }

    void TickCut(float now, float flash)
    {
        float te = (now - _cutStart) / _cutPace;   // 패턴 시간
        float splitAt = _cut.SplitAt;

        if (_cut.Sfx && _cutSfxFired != ~0)
        {
            for (int i = 0; i < _cut.Count; i++)
            {
                int bit = 1 << i;
                if ((_cutSfxFired & bit) == 0 && te >= _cut.Get(i).Start)
                {
                    _cutSfxFired |= bit;
                    OneShotAudio.Play2D(_cutSlashClip, 0.62f, 1f + 0.06f * i);
                }
            }
            // 납도 '딸깍' — 갈라지기 직전, 정적 속에서
            if ((_cutSfxFired & 0x80) == 0 && te >= splitAt - 0.05f)
            {
                _cutSfxFired |= 0x80;
                OneShotAudio.Play2D(_cutClickClip, 0.8f, 1f);
            }
        }

        if (!_cutSplitStarted && te >= splitAt)
        {
            _cutSplitStarted = true;
            _cutSplitStartReal = now;
            _cutSplitDurReal = _cutSkipped ? 0.16f : Mathf.Max(0.05f, _cut.SplitDuration * _cutPace);
        }

        float s = _cutSplitStarted ? Mathf.Clamp01((now - _cutSplitStartReal) / _cutSplitDurReal) : 0f;
        if (s >= 1f)
        {
            StopCutInternal();
            return;
        }
        WriteCut(te, s, flash);
    }

    void WriteCut(float te, float s, float flash)
    {
        float aspect = Screen.height > 0 ? (float)Screen.width / Screen.height : 16f / 9f;
        EvaluateCut(_cut, te, s, _cutSplitStarted, _cutSplitDurReal, _cutCalm, flash, aspect, ActionColourState.Cut);
    }

    /// <summary>패턴 시간 te·분리 진행 s 에서 셰이더 상수를 계산한다 (할당 없음).</summary>
    static void EvaluateCut(in ScreenCutPattern cut, float te, float s, bool split, float splitDur, float calm, float flash, float aspect, ActionColourState.CutData c)
    {
        float move = 1f - calm;

        // 분리 이동: 빠르게 벌어진 뒤 천천히 흘러간다
        float e = 1f - (1f - s) * (1f - s) * (1f - s);
        float drift = (e + 0.3f * s) * move;

        c.Count = cut.Count;
        c.Calm = calm;
        for (int i = 0; i < ActionColourState.CutMaxLines; i++)
        {
            if (i >= cut.Count)
            {
                c.Lines[i] = new Vector4(0f, 0f, 1f, 0f);
                c.Spans[i] = Vector4.zero;
                c.Moves[i] = Vector4.zero;
                continue;
            }
            ScreenCutLine l = cut.Get(i);
            Vector2 p = new Vector2((l.Through.x * 2f - 1f) * aspect, l.Through.y * 2f - 1f);
            float rad = l.AngleDeg * Mathf.Deg2Rad;
            Vector2 d = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
            ClipToScreen(p, d, aspect, out float tA, out float tB);

            float k = Mathf.Clamp01((te - l.Start) / Mathf.Max(0.01f, l.Duration));
            float prog = tB > tA ? 1f - (1f - k) * (1f - k) * (1f - k) : 0f;   // 날카로운 ease-out
            float doneAt = l.Start + l.Duration;
            float lineFlash = k <= 0f ? 0f : te < doneAt ? 1f : Mathf.Exp(-(te - doneAt) / 0.07f);

            c.Lines[i] = new Vector4(p.x, p.y, d.x, d.y);
            c.Spans[i] = new Vector4(tA, tB, prog, lineFlash * flash);
            c.Moves[i] = new Vector4(l.Slide * drift, l.Push * drift, l.RotationDeg * Mathf.Deg2Rad * drift, 0f);
        }

        // 정지 프레임: 선을 긋는 동안 살짝 어둡게 → 정적(hold) 동안 더 어둡고 채도가 빠져 선만 빛난다
        float dimIn = Mathf.Clamp01(te / 0.06f);
        float hold = Mathf.Clamp01((te - cut.LinesEnd) / Mathf.Max(0.01f, cut.Hold));
        hold = hold * hold * (3f - 2f * hold);
        float dim = (cut.Dim * dimIn + 0.14f * hold) * Mathf.Lerp(0.5f, 1f, flash);
        float desat = 0.55f * hold;

        // 분리: 조각은 끝까지 불투명 (라이브 화면과 겹친 잔상 없음) → 끝나는 프레임에 라이브로 하드 컷 스냅.
        // 분리 순간 2 프레임 반전 + 틈 흰 빛 (번쩍임 슬라이더 비례, 0 이면 없음).
        // calm(번쩍임 0 근처)만 베인 선에서 번지는 크로스페이드 (조각이 움직이지 않으므로 잔상 없음).
        float splitT = split ? s * splitDur : 0f;
        float overall = 1f;
        if (calm > 0.5f)
        {
            float fade = Mathf.Clamp01((s - 0.75f) / 0.25f);
            overall = 1f - fade * fade * (3f - 2f * fade);
        }
        float invert = split && splitT < CutFlashFrames ? 0.85f * flash : 0f;
        float gapWhite = split ? cut.GapFlash * flash * (splitT < CutFlashFrames ? 1f : Mathf.Exp(-(splitT - CutFlashFrames) / 0.035f)) : 0f;
        // 화면 공간 베기 선은 반전 프레임 동안 사라진다 (조각 절단면 발광이 이어받음). calm 은 천천히.
        float lineFade = calm > 0.5f ? Mathf.Clamp01(s / 0.4f) : Mathf.Clamp01(splitT / (CutFlashFrames * 1.5f));
        float rest = 1f - s;

        c.State = new Vector4(split ? Mathf.Max(s, 1e-4f) : 0f, overall, gapWhite, dim);
        float front = split ? Mathf.Lerp(-CutCalmSoft, 1.7f, Mathf.SmoothStep(0f, 1f, s)) : -CutCalmSoft;
        c.Fx = new Vector4(split ? 1f - lineFade * lineFade * (3f - 2f * lineFade) : 1f, cut.SpeedLines * flash, front, cut.Streak * flash);
        c.Fx2 = new Vector4(split ? Mathf.Pow(rest, 1.5f) * Mathf.Lerp(0.35f, 1f, flash) * move : 0f,
            invert, CutCalmSoft, desat);
        c.Core = Color.Lerp(CutCoreColor, CutCalmCoreColor, calm);
        Color edge = CutRedColor;
        edge.a = Mathf.Lerp(0.9f, 0.35f, calm);
        c.Edge = edge;
        c.Gap = CutGapColor;
    }

    /// <summary>무한 직선 p + t·d 를 화면 사각형(±aspect, ±1)에 자른 파라미터 구간.</summary>
    static void ClipToScreen(Vector2 p, Vector2 d, float aspect, out float tA, out float tB)
    {
        float tMin = -1e4f, tMax = 1e4f;
        ClipAxis(p.x, d.x, aspect, ref tMin, ref tMax);
        ClipAxis(p.y, d.y, 1f, ref tMin, ref tMax);
        if (tMin > tMax)
        {
            tA = tB = 0f;
            return;
        }
        tA = tMin;
        tB = tMax;
    }

    static void ClipAxis(float p, float d, float half, ref float tMin, ref float tMax)
    {
        if (Mathf.Abs(d) < 1e-5f)
        {
            if (Mathf.Abs(p) > half)
                tMin = 1e4f;   // 화면 밖 평행선
            return;
        }
        float t1 = (-half - p) / d;
        float t2 = (half - p) / d;
        tMin = Mathf.Max(tMin, Mathf.Min(t1, t2));
        tMax = Mathf.Min(tMax, Mathf.Max(t1, t2));
    }

    void StopCutInternal()
    {
        if (_cutActive && _cutOnSplit != null)
        {
            System.Action cb = _cutOnSplit;
            _cutOnSplit = null;
            cb.Invoke();
        }
        _cutActive = false;
        _cutSplitStarted = false;
        ActionColourState.SetCutActive(false);
    }
}
