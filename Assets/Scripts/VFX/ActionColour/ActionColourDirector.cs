// 추온 액션 컬러 디렉터 (반복 11)
// 흑백 '흡수' 연출 + 포스트 이벤트(패리 색수차/비네트, 브레이크 채도 딥, 저체력 붉은 비네트, 퍼펙트 회피 쿨 틴트)
// 와 화면 갈라짐(ScreenShatter) 타이밍을 실시간(unscaled) 기준으로 굴려 ActionColourState 에 써 넣는다.
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

    // ------------------------------------------------------------------ 화면 갈라짐
    bool _shatterActive;
    float _shatterStart;
    float _shatterCrackDuration;
    float _shatterBurstDuration;
    Vector2 _shatterImpact;
    bool _shatterBurstNotified;
    System.Action _shatterOnBurst;

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
        s_instance.StopShatterInternal();
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
        ScreenShatterAssets.Load();
        d._prewarmFrames = 3;
        ActionColourState.SetPrewarm(true);
        ActionColourState.RequestShatterCapture();
    }

    // ------------------------------------------------------------------ 화면 갈라짐 (ScreenShatter 가 호출)

    internal static void StartShatter(Vector2 impactViewport, float crackDuration, float burstDuration, System.Action onBurst)
    {
        ActionColourDirector d = Ensure();
        if (d == null)
            return;
        if (ScreenShatterAssets.Load() == null)
        {
            onBurst?.Invoke();
            return;
        }
        d._shatterActive = true;
        d._shatterStart = Time.unscaledTime;
        d._shatterCrackDuration = Mathf.Max(0.01f, crackDuration);
        d._shatterBurstDuration = Mathf.Max(0.05f, burstDuration);
        d._shatterImpact = impactViewport;
        d._shatterBurstNotified = false;
        d._shatterOnBurst = onBurst;
        ActionColourState.RequestShatterCapture();
        d.WriteShatter(0f, 0f);
        LastEvent = "shatter";
        PulseCount++;
    }

    internal static void SkipShatter()
    {
        if (s_instance == null || !s_instance._shatterActive)
            return;
        // 남은 시간을 아주 짧게 (튕겨 나가는 첫 프레임 몇 장만)
        float now = Time.unscaledTime;
        float elapsed = now - s_instance._shatterStart;
        float total = s_instance._shatterCrackDuration + s_instance._shatterBurstDuration;
        if (elapsed < total)
        {
            s_instance._shatterCrackDuration = Mathf.Min(s_instance._shatterCrackDuration, elapsed);
            s_instance._shatterBurstDuration = Mathf.Max(0.12f, Mathf.Min(0.18f, total - elapsed));
            s_instance._shatterStart = now - s_instance._shatterCrackDuration;
        }
    }

    internal static bool ShatterPlaying => s_instance != null && s_instance._shatterActive;

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
        ActionColourState.SetShatter(default, false);
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
        StopShatterInternal();
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
            else
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

        // ---- 화면 갈라짐 ----
        if (_shatterActive)
            TickShatter(now, flash);

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

    // ------------------------------------------------------------------ 화면 갈라짐

    void TickShatter(float now, float flash)
    {
        float t = now - _shatterStart;
        float crack = Mathf.Clamp01(t / _shatterCrackDuration);
        float burst = Mathf.Clamp01((t - _shatterCrackDuration) / _shatterBurstDuration);

        if (!_shatterBurstNotified && t >= _shatterCrackDuration)
        {
            _shatterBurstNotified = true;
            System.Action cb = _shatterOnBurst;
            _shatterOnBurst = null;
            cb?.Invoke();
        }

        if (burst >= 1f)
        {
            StopShatterInternal();
            return;
        }

        WriteShatter(crack, burst, flash);
    }

    void WriteShatter(float crack, float burst, float flash = 1f)
    {
        var snap = new ActionColourState.ShatterSnapshot
        {
            Impact = _shatterImpact,
            Crack = crack * crack * (3f - 2f * crack),
            Burst = burst,
            Alpha = 1f,
            ChromaticAberration = 0.006f * flash,
            CrackColor = new Color(0.86f, 0.93f, 1f, Mathf.Lerp(0.5f, 0.9f, flash))
        };
        ActionColourState.SetShatter(snap, true);
    }

    void StopShatterInternal()
    {
        if (_shatterActive && !_shatterBurstNotified)
        {
            System.Action cb = _shatterOnBurst;
            _shatterOnBurst = null;
            _shatterBurstNotified = true;
            cb?.Invoke();
        }
        _shatterActive = false;
        ActionColourState.SetShatter(default, false);
    }
}
