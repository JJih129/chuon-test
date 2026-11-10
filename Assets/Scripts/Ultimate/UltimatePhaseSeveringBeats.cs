// 궁극기 '위상단절검' 비트 연출 (반복 17: 나이트브링어 피니시) — UltimateCinematicController 가 타임라인과 함께 구동
// 감정 레퍼런스만 차용: 어둠이 내려앉고 → 정령이 솟고 → 멈춘 세계 속 순간이동 연속 베기 → 정적 → 납도 '딸깍' → 한꺼번에 터짐.
// 에셋은 모두 자체 제작 (Ult_EgoSpirit 메시: Tools/ArtGen/ult_spirit_blender.py, ChuonUltimateEgoSpirit / ChuonUltimateCut 셰이더).
//
// 타임라인(TL_Ultimate_PlayerSword, 5.6 s)은 그대로 두고 인트로 포즈(0.45 s)에서 붙잡은 채 비트를 직접 연출한다.
// 해방 순간 워크아웃 직전(2.86 s)으로 넘겨 2.9 s 워크아웃·폭발·피해 신호를 그대로 쓴다 (피해/EGO 게이지 규칙 불변).
//   1 어둠      0.60 s : 노출↓·흑백 흡수·비네트로 세계가 흑연빛으로 가라앉고, 대상은 림 라이트, 소리가 빠진다. 세계 시간 정지.
//   2 정령      0.50 s : 추온 뒤로 세라믹 화이트 + 레드 가장자리의 EGO 칼날 정령이 솟는다.
//   3 연속 베기 2.20 s : 6 회 순간이동 베기 — 매 베기마다 허공에 곧은 적/백 절단선이 걸리고 카메라가 하드 컷.
//   4 정적      0.80 s : 와이드, 모두 멈춤, 무음, 추온의 등.
//   5 납도      0.40 s : 칼자루 클로즈업 — 칼이 들어가며 '딸깍'.
//   6 해방            : 걸린 절단선이 동시에 폭발 → ScreenCut 대각 베기 → 라이브 스냅 순간 타임라인 재개(피해) → 0.35 s 블렌드로 복귀.
// 건너뛰기: F / Space / Enter / 패드 South·Start (시작 0.5 s 후). 모든 타이밍은 실시간(unscaled), 시간 정지는 GameTimeScale.
// 접근성: 화면 번쩍임 슬라이더 → 폭발 세기, 카메라 흔들림 세기 → 베기 펀치, 흑백 끄기 → ActionColourDirector 가 처리.
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Playables;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

[DisallowMultipleComponent]
public sealed class UltimatePhaseSeveringBeats : MonoBehaviour
{
    public enum Beat { Idle, Darkness, Spirit, Dash, Stillness, Sheathe, Detonate, Cut, Release, Skipping }

    [Header("Timeline marks (s)")]
    [Tooltip("인트로 포즈 클립 안에서 붙잡는 시각 (어둠 비트 동안 여기까지 진행)")]
    [SerializeField] float holdTime = 0.45f;
    [Tooltip("해방 시 넘기는 시각 — 2.9 s 워크아웃/폭발/피해 신호 직전")]
    [SerializeField] float releaseJumpTime = 2.86f;
    [Tooltip("워크아웃 컷/신호 시각 — 여기를 지나면 카메라를 타임라인에 돌려준다")]
    [SerializeField] float walkoutTime = 2.9f;
    [Tooltip("이 시각 이후(폭발 컷) 타임라인을 tailSpeed 로 감는다")]
    [SerializeField] float tailFrom = 3.7f;
    [SerializeField] float tailSpeed = 1.6f;

    [Header("Beat lengths (realtime s)")]
    [SerializeField] float darknessDuration = 0.6f;
    [SerializeField] float spiritDuration = 0.5f;
    [SerializeField] float dashDuration = 2.2f;
    [SerializeField, Range(5, 7)] int slashCount = 6;
    [SerializeField] float stillnessDuration = 0.8f;
    [SerializeField] float sheatheDuration = 0.4f;
    [Tooltip("절단선 폭발 플래시 → 화면 베기 캡처까지 (폭발 프레임이 정지 화면이 된다)")]
    [SerializeField] float detonateLead = 0.09f;
    [SerializeField] float colourReturn = 0.4f;
    [SerializeField] float skipSpeed = 6f;
    [SerializeField] float skipGrace = 0.5f;

    [Header("Darkness")]
    [SerializeField] float darkExposure = -1.8f;
    [SerializeField] float darkContrast = 24f;
    [SerializeField, Range(0f, 1f)] float darkVignette = 0.45f;
    [Tooltip("어둠~납도 동안 월드 소리 배율 (베기/납도 효과음은 예외)")]
    [SerializeField, Range(0f, 1f)] float listenerDuck = 0.06f;
    [SerializeField] float rimKeyIntensity = 10f;
    [SerializeField] float rimRedIntensity = 6f;
    [SerializeField] Color rimKeyColor = new Color(1f, 0.95f, 0.9f, 1f);
    [SerializeField] Color rimRedColor = new Color(1f, 0.13f, 0.09f, 1f);

    [Header("Spirit / hanging cuts")]
    [SerializeField] float spiritHeight = 3.4f;
    [SerializeField] float cutThickness = 0.075f;
    [SerializeField] float cutIntensity = 2.6f;
    [SerializeField] float cutDrawTime = 0.07f;

    /// <summary>타임라인 길이 외에 비트가 추가하는 최대 실시간 (안전 워치독 여유).</summary>
    public const float ExtraDurationBudget = 6.5f;

    const int MaxCuts = 7;
    const string ResPrefix = "UltNightbringer/";
    // 블렌더 메시 정면(칼)이 유니티 +Z 를 보도록 보정 (ult_spirit_blender.py 의 FBX 축 설정 기준)
    const float SpiritYawOffset = 0f;

    public static Beat CurrentBeat { get; private set; } = Beat.Idle;
    public static int SkipCount { get; private set; }
    public static float LastStartRealtime { get; private set; } = -1f;
    /// <summary>QA: 마지막 궁극기 비트 전환 기록 (실시간 오프셋, 타임라인 시각).</summary>
    public static string QaTimeline { get; private set; } = "";

    // ------------------------------------------------------------------ 공유 리소스 (첫 사용/예열 때 1회 로드)
    static Mesh s_spiritMesh;
    static Mesh s_cutMesh;
    static Material s_spiritMat;
    static Material s_cutMat;
    static AudioClip s_slashClip;
    static AudioClip s_clickClip;
    static bool s_loaded;
    static readonly int IdIntensity = Shader.PropertyToID("_Intensity");
    static readonly int IdAlpha = Shader.PropertyToID("_Alpha");
    static readonly int IdReveal = Shader.PropertyToID("_Reveal");
    static readonly int IdPhase = Shader.PropertyToID("_Phase");
    static readonly int IdHeightMax = Shader.PropertyToID("_HeightMax");
    static readonly RaycastHit[] s_hits = new RaycastHit[16];
    static readonly List<Renderer> s_renderers = new List<Renderer>(32);
    static readonly float[] s_blinkAngles = { 155f, 35f, 250f, 110f, 320f, 60f };

    sealed class Cut
    {
        public GameObject Go;
        public Transform T;
        public MeshRenderer R;
        public Vector3 A;
        public Vector3 Dir;
        public float Length;
        public float Thick;
        public float Born;
        public int Seed;
    }

    PlayableDirector _director;
    PlayerUltimateController _owner;
    UltimateCinematicController _ctl;
    Transform _focus;
    Transform _target;
    readonly List<Transform> _keep = new List<Transform>(6);
    float _beatStart;
    float _startRealtime;
    float _speed = 1f;
    bool _timeStopRequested;
    bool _active;
    int _slashFired;
    bool _clickPlayed;
    bool _darkReshot;
    float _cutReleaseDeadline = 2f;

    // 장면 좌표 (비트 시작 시 갱신)
    Vector3 _T;
    Vector3 _Tc;
    Vector3 _F = Vector3.forward;
    Vector3 _R = Vector3.right;
    float _groundY;
    float _tRadius = 0.6f;
    float _tHeight = 1.8f;
    Vector3 _P0;
    Vector3 _finish;
    readonly Vector3[] _blink = new Vector3[MaxCuts];
    Vector3 _stillPos;
    Vector3 _stillLook;

    // 카메라 샷
    bool _camOwned;
    Vector3 _shotPos;
    Vector3 _shotLook;
    float _shotFov = 40f;
    float _shotDutch;
    float _shotPush;
    float _shotPushRate = 2f;
    float _shotShake;
    float _shotKick;
    float _shotStart;
    int _shotSeed;

    // 연출 오브젝트
    MaterialPropertyBlock _mpb;
    readonly Cut[] _cuts = new Cut[MaxCuts];
    int _cutCount;
    Cut _glint;
    float _detonateStart = -1f;
    GameObject _spiritGo;
    Transform _spiritT;
    MeshRenderer _spiritR;
    Vector3 _spiritBase;
    float _spiritStart;
    float _spiritFadeStart = -1f;
    Light _rimKey;
    Light _rimRed;
    Volume _volume;
    VolumeProfile _profile;
    ColorAdjustments _colorAdjust;
    Vignette _vignette;
    readonly AudioSource[] _sfx = new AudioSource[2];
    int _sfxNext;

    // 어둠/무음 페이드
    float _darkW;
    float _darkTarget;
    float _darkRate = 2f;
    float _duckW;
    float _duckTarget;
    float _duckRate = 3f;
    float _listenerBase = 1f;
    bool _duckApplied;

    /// <summary>워크아웃/카메라 궤도 코루틴이 곱할 속도 (정지 중 0, 건너뛰기 중 배속).</summary>
    public float PlaybackSpeed => _active ? _speed : 1f;
    /// <summary>슬래시 스톰 카메라 궤도 배율 (이 연출에서는 궤도를 쓰지 않는다).</summary>
    public float OrbitScale => _active ? _speed : 1f;
    public bool IsActive => _active;

    // ------------------------------------------------------------------ 수명

    public void Begin(PlayableDirector director, PlayerUltimateController owner, Transform focus, Transform target, IList<Transform> keepSubjects)
    {
        Begin(director, owner, focus, target, keepSubjects, null);
    }

    public void Begin(PlayableDirector director, PlayerUltimateController owner, Transform focus, Transform target, IList<Transform> keepSubjects, UltimateCinematicController controller)
    {
        LoadShared();
        _director = director;
        _owner = owner;
        _ctl = controller;
        _focus = focus;
        _target = target;
        _keep.Clear();
        if (keepSubjects != null)
        {
            for (int i = 0; i < keepSubjects.Count; i++)
            {
                if (keepSubjects[i] != null)
                    _keep.Add(keepSubjects[i]);
            }
        }
        _active = true;
        _timeStopRequested = false;
        _slashFired = 0;
        _clickPlayed = false;
        _darkReshot = false;
        _detonateStart = -1f;
        _spiritFadeStart = -1f;
        _startRealtime = Time.unscaledTime;
        LastStartRealtime = _startRealtime;
        enabled = true;

        EnsurePresentationObjects();
        HideCuts();
        CaptureContext();

        PhaseCoreHUD.Active?.PlayActivationFlare();
        Enter(Beat.Darkness);
        SetSpeed(holdTime / Mathf.Max(0.05f, darknessDuration));
        RequestTimeStop();

        // 1 어둠: 흑백 흡수(색 유지: 추온·대상) + 노출/비네트 + 림 라이트 + 무음
        ActionColourDirector.BeginAbsorb(this, _focus, darknessDuration, _keep);
        _darkTarget = 1f;
        _darkRate = 1f / Mathf.Max(0.05f, darknessDuration);
        if (!_duckApplied)
            _listenerBase = AudioListener.volume;
        _duckTarget = 1f;
        _duckRate = 1f / 0.35f;
        ShotDarkness();
    }

    public void End()
    {
        if (!_active)
            return;
        _active = false;
        QaTimeline += "End@" + (Time.unscaledTime - _startRealtime).ToString("F2");
        ReleaseTimeStop();
        ActionColourDirector.EndAbsorb(this, CurrentBeat == Beat.Skipping ? 0.2f : colourReturn);
        ReleasePresentation(true);
        CurrentBeat = Beat.Idle;
        _speed = 1f;
        _director = null;
        _owner = null;
        _ctl = null;
        // enabled 유지: 어둠/무음 페이드 아웃을 끝까지 틱한 뒤 스스로 꺼진다
    }

    void OnDisable()
    {
        if (_active)
            End();
        // 강제 복구 (비활성화/파괴 경로)
        _darkW = 0f;
        _darkTarget = 0f;
        ApplyDarkness();
        if (_duckApplied)
        {
            AudioListener.volume = _listenerBase;
            _duckApplied = false;
        }
        _duckW = 0f;
        _duckTarget = 0f;
    }

    void OnDestroy()
    {
        if (_profile != null)
            Destroy(_profile);
        for (int i = 0; i < _cuts.Length; i++)
        {
            if (_cuts[i] != null && _cuts[i].Go != null)
                Destroy(_cuts[i].Go);
        }
        if (_glint != null && _glint.Go != null)
            Destroy(_glint.Go);
        if (_spiritGo != null)
            Destroy(_spiritGo);
    }

    void Enter(Beat beat)
    {
        CurrentBeat = beat;
        _beatStart = Time.unscaledTime;
        if (beat == Beat.Darkness)
            QaTimeline = "";
        QaTimeline += beat + "@" + (_beatStart - _startRealtime).ToString("F2") + "(t=" + (_director != null ? _director.time : 0d).ToString("F2") + ") ";
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        QaFrameSampler.Event("ult." + beat);
#endif
    }

    void SetSpeed(float speed)
    {
        _speed = speed;
        if (_director == null || !_director.playableGraph.IsValid())
            return;
        int roots = _director.playableGraph.GetRootPlayableCount();
        for (int i = 0; i < roots; i++)
        {
            Playable root = _director.playableGraph.GetRootPlayable(i);
            if (root.IsValid())
                root.SetSpeed(speed);
        }
    }

    void JumpTimeline(float time)
    {
        if (_director != null && _director.time < time)
            _director.time = time;
    }

    void Update()
    {
        float now = Time.unscaledTime;
        TickFades();

        if (!_active || _director == null)
        {
            if (!_active && _darkW <= 0f && _duckW <= 0f && !_duckApplied)
                enabled = false;
            return;
        }

        double t = _director.time;
        double lead = Mathf.Min(0.05f, Time.unscaledDeltaTime) * _speed * 1.25f;

        if (CurrentBeat != Beat.Skipping && CurrentBeat != Beat.Release && now - _startRealtime > skipGrace && SkipPressed())
        {
            Skip();
            return;
        }

        // 그래프가 재생성되면 속도가 1 로 돌아가므로 매 프레임 유지
        if (_director.playableGraph.IsValid() && _director.playableGraph.GetRootPlayableCount() > 0)
        {
            Playable root = _director.playableGraph.GetRootPlayable(0);
            if (root.IsValid() && !Mathf.Approximately((float)root.GetSpeed(), _speed))
                SetSpeed(_speed);
        }

        float e = now - _beatStart;
        switch (CurrentBeat)
        {
            case Beat.Darkness:
                // 0.02 s 드로우 포즈 신호가 추온을 시작점으로 옮긴 뒤 한 번 다시 잡는다
                if (!_darkReshot && t > 0.05d)
                {
                    _darkReshot = true;
                    CaptureContext();
                    ShotDarkness();
                }
                if (_speed > 0f && t + lead >= holdTime)
                    SetSpeed(0f);
                if (e >= darknessDuration)
                {
                    SetSpeed(0f);
                    BeginSpirit();
                }
                break;

            case Beat.Spirit:
                if (e >= spiritDuration)
                    BeginDash();
                break;

            case Beat.Dash:
            {
                int n = Mathf.Clamp(slashCount, 5, 7);
                float interval = dashDuration / n;
                int due = Mathf.Min(n, Mathf.FloorToInt(e / interval) + 1);
                while (_slashFired < due)
                    FireSlash(_slashFired++, n, interval);
                if (e >= dashDuration)
                    BeginStillness();
                break;
            }

            case Beat.Stillness:
                if (e >= stillnessDuration)
                    BeginSheathe();
                break;

            case Beat.Sheathe:
                if (!_clickPlayed && e >= sheatheDuration * 0.78f)
                {
                    _clickPlayed = true;
                    PlaySfx(s_clickClip, 0.95f, 1f);
                }
                if (e >= sheatheDuration)
                    BeginDetonate();
                break;

            case Beat.Detonate:
                if (e >= detonateLead)
                    BeginCut();
                break;

            case Beat.Cut:
                // 베기 선·정적·분리 동안 계속 정지. 라이브로 스냅되는 콜백에서 재개 (워치독 포함).
                if (e > _cutReleaseDeadline)
                    HandleCutRelease();
                break;

            case Beat.Release:
            case Beat.Skipping:
                // 2.9 s 워크아웃 컷이 실제로 잡힐 때까지 마지막 구도를 유지 (넘긴 구간의 스톰 vcam 이 한 프레임도 보이지 않게)
                if (_camOwned && t >= walkoutTime)
                {
                    _ctl?.NbReleaseCamera();
                    _camOwned = false;
                }
                if (CurrentBeat == Beat.Release && t >= tailFrom && _speed < tailSpeed)
                    SetSpeed(tailSpeed);
                break;
        }

        TickSpirit(now);
        TickCuts(now);
        if (_camOwned)
            ApplyShot(false);
    }

    // ------------------------------------------------------------------ 비트

    void BeginSpirit()
    {
        Enter(Beat.Spirit);
        CaptureContext();
        if (_spiritGo != null && s_spiritMesh != null)
        {
            Vector3 p = PlayerPos();
            _spiritBase = new Vector3(p.x, p.y, p.z) - _F * 0.95f;
            _spiritT.rotation = Quaternion.LookRotation(_F, Vector3.up) * Quaternion.Euler(0f, SpiritYawOffset, 0f);
            float meshHeight = Mathf.Max(0.1f, s_spiritMesh.bounds.max.y);
            _spiritT.localScale = Vector3.one * (spiritHeight / meshHeight);
            _spiritT.position = _spiritBase + Vector3.down * 0.9f;
            _spiritStart = Time.unscaledTime;
            _spiritFadeStart = -1f;
            _spiritGo.SetActive(true);
        }
        ShotSpirit();
    }

    void BeginDash()
    {
        Enter(Beat.Dash);
        CaptureContext();
        _P0 = PlayerPos();
        PlanBlinks();
        _slashFired = 0;
        if (_spiritGo != null && _spiritGo.activeSelf)
            _spiritFadeStart = Time.unscaledTime;
    }

    void BeginStillness()
    {
        Enter(Beat.Stillness);
        ComputeStillShot();
        SetShot(_stillPos, _stillLook, 46f, 0f, 0.12f, 0.6f, 0f, 0f);
    }

    void BeginSheathe()
    {
        Enter(Beat.Sheathe);
        _clickPlayed = false;
        Transform sword = _ctl != null ? _ctl.NbSwordFocus : null;
        Vector3 hilt = sword != null ? sword.position : _finish + Vector3.up * 0.95f + _R * 0.22f;
        // 반복 18: 0.7 m · FOV 24 는 너무 붙어 로우폴리 각이 보였다 → ~1.8 m · FOV 32 (숙인 머리·손·칼집이 함께 들어오는 미디엄 샷, 스크린샷으로 조정)
        Vector3 look = hilt + Vector3.up * 0.12f;
        Vector3 pos = hilt + _R * 1.5f + _F * 1.0f + Vector3.up * 0.4f;
        SetShot(pos, look, 32f, 6f, 0.08f, 1.4f, 0f, 0f);

        // 칼이 들어가는 빛줄기: 칼집 방향에서 손잡이 쪽으로 줄어든다
        if (_glint != null)
        {
            Vector3 sheath = (-_F * 0.85f - Vector3.up * 0.3f + _R * 0.12f).normalized;
            _glint.A = hilt + sheath * 0.72f;
            _glint.Dir = -sheath;
            _glint.Length = 0.72f;
            _glint.Thick = 0.028f;
            _glint.Born = Time.unscaledTime;
            _glint.Go.SetActive(true);
        }
    }

    void BeginDetonate()
    {
        Enter(Beat.Detonate);
        if (_glint != null)
            _glint.Go.SetActive(false);
        _detonateStart = Time.unscaledTime;
        SetShot(_stillPos, _stillLook, 44f, 0f, 0f, 1f, 0.11f, 5f);
        PlaySfx(s_slashClip, 0.95f, 0.74f);
        PlaySfx(s_slashClip, 0.7f, 1.08f);
    }

    void BeginCut()
    {
        Enter(Beat.Cut);
        ScreenCutPattern cut = ScreenCut.NightbringerPattern(ResolveImpactViewport());
        _cutReleaseDeadline = cut.Total * 1.5f + 0.5f;   // calm 모드는 1.5 배 느림
        ScreenCut.Play(cut, HandleCutRelease);
    }

    void HandleCutRelease()
    {
        if (!_active || CurrentBeat != Beat.Cut)
            return;
        Enter(Beat.Release);
        // 라이브로 스냅되는 순간: 카메라를 타임라인에 돌려주고(워크아웃 컷) 시간·소리·색을 되돌린 뒤 피해 신호로 진행
        ReleasePresentation(false, true);
        ReleaseTimeStop();
        JumpTimeline(releaseJumpTime);
        SetSpeed(1f);
        ActionColourDirector.EndAbsorb(this, colourReturn);
    }

    void Skip()
    {
        SkipCount++;
        Enter(Beat.Skipping);
        ReleasePresentation(true, true);
        ReleaseTimeStop();
        ScreenCut.Skip();
        ActionColourDirector.EndAbsorb(this, 0.2f);
        JumpTimeline(releaseJumpTime);
        SetSpeed(skipSpeed);
    }

    /// <summary>QA/외부: 진행 중 연출 건너뛰기.</summary>
    public void RequestSkip()
    {
        if (_active && CurrentBeat != Beat.Skipping && CurrentBeat != Beat.Release)
            Skip();
    }

    void ReleasePresentation(bool fast, bool keepCamera = false)
    {
        if (!keepCamera)
        {
            if (_camOwned && _ctl != null)
                _ctl.NbReleaseCamera();
            _camOwned = false;
        }
        else
        {
            // 남은 구도는 정지 상태로 유지 (흔들림/밀기 없음)
            _shotShake = 0f;
            _shotKick = 0f;
            _shotPush = 0f;
        }
        _darkTarget = 0f;
        _darkRate = 1f / (fast ? 0.2f : 0.45f);
        _duckTarget = 0f;
        _duckRate = 1f / (fast ? 0.12f : 0.2f);
        if (_spiritGo != null)
            _spiritGo.SetActive(false);
        HideCuts();
        _detonateStart = -1f;
    }

    // ------------------------------------------------------------------ 연속 베기

    void PlanBlinks()
    {
        int n = Mathf.Clamp(slashCount, 5, 7);
        Vector3 center = new Vector3(_T.x, _groundY, _T.z);
        Vector3 probe = center + Vector3.up * 0.9f;
        float y = _P0.y;
        float ring = _tRadius + 1.5f;
        for (int i = 0; i < n - 1; i++)
        {
            Vector3 dir = Quaternion.AngleAxis(s_blinkAngles[i], Vector3.up) * -_F;
            float r = SafeDistance(probe, dir, ring, 0.45f);
            Vector3 p = center + dir * Mathf.Max(_tRadius + 0.5f, r);
            p.y = y;
            _blink[i] = p;
        }

        Vector3 finish = _ctl != null ? _ctl.NbFinishPoint : center + _F * 4.2f;
        Vector3 toFinish = finish - center;
        toFinish.y = 0f;
        float dist = toFinish.magnitude;
        Vector3 fdir = dist > 0.01f ? toFinish / dist : _F;
        float safe = SafeDistance(probe, fdir, Mathf.Max(dist, _tRadius + 1.2f), 0.45f);
        _finish = center + fdir * Mathf.Max(_tRadius + 0.6f, safe);
        _finish.y = y;
        _blink[n - 1] = _finish;
    }

    void FireSlash(int i, int n, float interval)
    {
        Vector3 from = i == 0 ? _P0 : _blink[i - 1];
        Vector3 to = _blink[i];
        Vector3 d = to - from;
        d.y = 0f;
        if (d.sqrMagnitude < 0.0001f)
            d = Quaternion.AngleAxis(90f * i, Vector3.up) * _F;
        d.Normalize();
        Vector3 side = Vector3.Cross(Vector3.up, d);

        // 허공에 걸리는 곧은 절단선: 대상 몸통을 비스듬히 지난다 (높이·기울기·옆 어긋남은 베기마다 다르게)
        float h = _groundY + _tHeight * Mathf.Lerp(0.3f, 0.8f, Hash(i, 0));
        float tilt = Mathf.Lerp(-0.45f, 0.45f, Hash(i, 1));
        Vector3 dir = (d + Vector3.up * tilt).normalized;
        Vector3 mid = new Vector3(_T.x, h, _T.z) + side * (Mathf.Lerp(-0.22f, 0.22f, Hash(i, 2)) * _tRadius);
        float len = 2f * _tRadius + 2.6f;
        SpawnCut(mid - dir * (len * 0.5f), dir, len, i);

        // 추온은 다음 자리에 '이미 벤 자세'로 나타난다 (대시 방향을 바라봄)
        if (_ctl != null)
        {
            Vector3 look = i == n - 1 ? to + _F * 2f : to + d * 2f;
            _ctl.NbPlacePlayer(to, look);
            if (i == n - 1)
                _ctl.NbMarkFinish(to);
        }

        PlaySfx(s_slashClip, 0.5f, 1.2f + 0.07f * (i % 3));
        ShotSlash(i, n, to, interval);
    }

    void SpawnCut(Vector3 a, Vector3 dir, float len, int seed)
    {
        if (_cutCount >= MaxCuts)
            return;
        Cut c = _cuts[_cutCount++];
        if (c == null)
            return;
        c.A = a;
        c.Dir = dir;
        c.Length = len;
        c.Thick = cutThickness * Mathf.Lerp(0.8f, 1.25f, Hash(seed, 3));
        c.Born = Time.unscaledTime;
        c.Seed = seed;
        c.Go.SetActive(true);
        WriteCut(c, 0.001f, c.Thick, 0f, 1f);
    }

    void HideCuts()
    {
        for (int i = 0; i < _cuts.Length; i++)
        {
            if (_cuts[i] != null && _cuts[i].Go != null)
                _cuts[i].Go.SetActive(false);
        }
        if (_glint != null && _glint.Go != null)
            _glint.Go.SetActive(false);
        _cutCount = 0;
    }

    void TickCuts(float now)
    {
        if (_mpb == null)
            return;
        float flash = AccessibilityOptionsRuntime.FlashIntensity;
        for (int i = 0; i < _cutCount; i++)
        {
            Cut c = _cuts[i];
            if (c == null || !c.Go.activeSelf)
                continue;
            float e = now - c.Born;
            float drawn = Mathf.Clamp01(e / Mathf.Max(0.01f, cutDrawTime));
            drawn = 1f - (1f - drawn) * (1f - drawn);
            float len = c.Length * drawn;
            float thick = c.Thick;
            // 갓 그어진 선은 밝게, 이후 미세한 떨림으로 '걸려 있음'
            float intensity = cutIntensity * (1f + 0.7f * Mathf.Exp(-e * 9f)) * (0.94f + 0.06f * Mathf.Sin(now * 37f + c.Seed * 1.9f));
            float alpha = 1f;
            Vector3 start = c.A;

            if (_detonateStart >= 0f)
            {
                // 동시 폭발: 선이 굵어지며 양 끝으로 늘어나고 하얗게 타오른 뒤 사라진다 (번쩍임 슬라이더 적용)
                float k = now - _detonateStart;
                float swell = 1f - Mathf.Exp(-k * 14f);
                thick *= 1f + 3.2f * swell;
                start -= c.Dir * (len * 0.075f * swell);
                len *= 1f + 0.15f * swell;
                intensity *= 1f + 3.5f * flash * Mathf.Exp(-k * 6f);
                alpha = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((k - 0.08f) / 0.26f));
                if (alpha <= 0f)
                {
                    c.Go.SetActive(false);
                    continue;
                }
            }
            WriteCut(c, len, thick, intensity, alpha, start);
        }

        if (_glint != null && _glint.Go.activeSelf)
        {
            float p = Mathf.Clamp01((now - _glint.Born) / Mathf.Max(0.05f, sheatheDuration * 0.78f));
            float ease = p * p * (3f - 2f * p);
            float len = Mathf.Lerp(_glint.Length, 0.02f, ease);
            Vector3 a = _glint.A + _glint.Dir * (_glint.Length - len);
            float clickFlash = _clickPlayed ? 1f + 2.5f * flash * Mathf.Exp(-(now - (_glint.Born + sheatheDuration * 0.78f)) * 18f) : 1f;
            WriteCut(_glint, len, _glint.Thick, cutIntensity * 1.2f * clickFlash, 1f, a);
        }
    }

    void WriteCut(Cut c, float len, float thick, float intensity, float alpha)
    {
        WriteCut(c, len, thick, intensity, alpha, c.A);
    }

    void WriteCut(Cut c, float len, float thick, float intensity, float alpha, Vector3 start)
    {
        c.T.SetPositionAndRotation(start, Quaternion.LookRotation(c.Dir, Mathf.Abs(c.Dir.y) > 0.95f ? Vector3.forward : Vector3.up));
        c.T.localScale = new Vector3(thick, thick, Mathf.Max(0.001f, len));
        _mpb.Clear();
        _mpb.SetFloat(IdIntensity, intensity);
        _mpb.SetFloat(IdAlpha, alpha);
        c.R.SetPropertyBlock(_mpb);
    }

    // ------------------------------------------------------------------ 정령

    void TickSpirit(float now)
    {
        if (_spiritGo == null || !_spiritGo.activeSelf || _mpb == null)
            return;
        float rise = Mathf.Clamp01((now - _spiritStart) / Mathf.Max(0.05f, spiritDuration));
        float ease = 1f - (1f - rise) * (1f - rise) * (1f - rise);
        _spiritT.position = _spiritBase + Vector3.up * Mathf.Lerp(-0.9f, 0f, ease);
        float alpha = 1f;
        if (_spiritFadeStart >= 0f)
            alpha = 1f - Mathf.Clamp01((now - _spiritFadeStart) / 0.35f);
        if (alpha <= 0f)
        {
            _spiritGo.SetActive(false);
            return;
        }
        _mpb.Clear();
        _mpb.SetFloat(IdReveal, Mathf.Lerp(0.05f, 1.05f, ease));
        _mpb.SetFloat(IdAlpha, alpha);
        _mpb.SetFloat(IdPhase, now - _startRealtime);
        _mpb.SetFloat(IdHeightMax, s_spiritMesh != null ? Mathf.Max(0.1f, s_spiritMesh.bounds.max.y) : 1.1f);
        _spiritR.SetPropertyBlock(_mpb);
    }

    // ------------------------------------------------------------------ 카메라

    void ShotDarkness()
    {
        // 추온 어깨 너머로 림 라이트를 받는 대상 — 천천히 밀고 들어간다
        Vector3 p = PlayerPos();
        Vector3 pos = p - _F * 2.1f + _R * 1.0f + Vector3.up * 1.4f;
        SetShot(pos, _Tc + Vector3.up * 0.1f, 40f, 0f, 0.45f, 2f, 0f, 0f);
    }

    void ShotSpirit()
    {
        // 낮은 앵글 옆에서 올려다본다: 추온 뒤로 솟는 정령
        Vector3 p = PlayerPos();
        Vector3 pos = p + _F * 1.5f + _R * 2.6f + Vector3.up * 0.45f;
        Vector3 look = p - _F * 0.5f + Vector3.up * (spiritHeight * 0.46f);
        SetShot(pos, look, 52f, -4f, 0.3f, 1.5f, 0f, 0f);
    }

    void ShotSlash(int i, int n, Vector3 chuon, float interval)
    {
        Vector3 tg = new Vector3(_T.x, _groundY, _T.z);
        Vector3 D = chuon - tg;
        D.y = 0f;
        D = D.sqrMagnitude > 0.0001f ? D.normalized : -_F;
        Vector3 S = Vector3.Cross(Vector3.up, D);
        Vector3 her = chuon + Vector3.up * 1.0f;
        Vector3 pos;
        Vector3 look;
        float fov;
        float dutch;

        if (i == n - 1)
        {
            // 마지막: 대상을 꿰뚫고 착지 — 옆 와이드
            Vector3 mid = Vector3.Lerp(tg, chuon, 0.5f);
            pos = mid + S * (_tRadius + 3.6f) + Vector3.up * 1.2f;
            look = mid + Vector3.up * 1.0f;
            fov = 46f;
            dutch = 0f;
        }
        else
        {
            switch (i % 6)
            {
                case 0: pos = chuon + D * 1.7f + S * 0.8f + Vector3.up * 1.3f; look = her - D * 0.2f; fov = 35f; dutch = 7f; break;
                case 1: pos = tg - D * (_tRadius + 2.3f) + S * 1.1f + Vector3.up * (0.6f + _tHeight * 0.15f); look = _Tc + D * 0.6f; fov = 38f; dutch = -9f; break;
                case 2: pos = tg + S * (_tRadius + 3.0f) + Vector3.up * 0.35f + D * 0.4f; look = Vector3.Lerp(_Tc, her, 0.45f) + Vector3.up * 0.25f; fov = 50f; dutch = 5f; break;
                case 3: pos = tg + Vector3.up * (_tHeight + 3.0f) + S * 0.9f - D * 0.8f; look = _Tc; fov = 55f; dutch = 0f; break;
                case 4: pos = chuon + D * 1.2f + S * 0.55f + Vector3.up * 1.5f; look = _Tc; fov = 40f; dutch = -6f; break;
                default: pos = _Tc + (S - D * 0.4f).normalized * (_tRadius + 1.7f) + Vector3.up * 0.2f; look = _Tc; fov = 32f; dutch = 14f; break;
            }
        }
        float rate = 3f / Mathf.Max(0.1f, interval);
        SetShot(pos, look, fov, dutch, 0.35f, rate, 0.06f, 6f);
    }

    void ComputeStillShot()
    {
        // 대상 너머(시작 쪽)에서 본 와이드: 전경에 절단선이 걸린 대상, 뒤로 등을 보인 추온
        Vector3 tg = new Vector3(_T.x, _groundY, _T.z);
        _stillLook = Vector3.Lerp(_Tc, _finish + Vector3.up * 1.0f, 0.55f);
        _stillPos = SafeCam(_stillLook, tg - _F * (_tRadius + 2.8f) + _R * 1.6f + Vector3.up * (0.7f + _tHeight * 0.35f));
    }

    void SetShot(Vector3 pos, Vector3 look, float fov, float dutch, float push, float pushRate, float shake, float fovKick)
    {
        _shotPos = SafeCam(look, pos);
        _shotLook = look;
        _shotFov = fov;
        _shotDutch = dutch;
        _shotPush = push;
        _shotPushRate = pushRate;
        _shotShake = shake * Mathf.Clamp01(CameraShake.GlobalStrength);
        _shotKick = fovKick;
        _shotStart = Time.unscaledTime;
        _shotSeed++;
        ApplyShot(true);
    }

    void ApplyShot(bool cut)
    {
        if (_ctl == null)
            return;
        float e = Time.unscaledTime - _shotStart;
        Vector3 fwd = _shotLook - _shotPos;
        float dist = fwd.magnitude;
        fwd = dist > 0.001f ? fwd / dist : Vector3.forward;
        float push = Mathf.Min(_shotPush * (1f - Mathf.Exp(-e * _shotPushRate)), dist * 0.5f);
        Vector3 pos = _shotPos + fwd * push;
        float k = Mathf.Clamp01(1f - e / 0.16f);
        if (k > 0f && _shotShake > 0f)
        {
            float s = _shotShake * k * k;
            pos += new Vector3(Mathf.Sin(e * 91f + _shotSeed), Mathf.Sin(e * 113f + _shotSeed * 1.7f), Mathf.Sin(e * 71f + _shotSeed * 2.3f)) * s;
        }
        float fov = _shotFov + _shotKick * k * k;
        _ctl.NbSetCamera(pos, _shotLook, fov, _shotDutch, cut);
        _camOwned = true;
    }

    // ------------------------------------------------------------------ 장면 좌표 / 충돌

    void CaptureContext()
    {
        _T = _ctl != null ? _ctl.NbTargetCenter : _target != null ? _target.position : transform.position + transform.forward * 2f;
        _F = _ctl != null ? _ctl.NbForward : Vector3.forward;
        _F.y = 0f;
        _F = _F.sqrMagnitude > 0.0001f ? _F.normalized : Vector3.forward;
        _R = Vector3.Cross(Vector3.up, _F);
        MeasureTarget();
        _Tc = new Vector3(_T.x, _groundY + _tHeight * 0.55f, _T.z);
        _P0 = PlayerPos();
    }

    void MeasureTarget()
    {
        _tRadius = 0.6f;
        _tHeight = 1.8f;
        Transform root = _ctl != null ? _ctl.NbTargetRoot : _target;
        _groundY = root != null ? root.position.y : _T.y - 1f;
        if (root == null)
            return;
        s_renderers.Clear();
        root.GetComponentsInChildren(false, s_renderers);
        bool any = false;
        Bounds b = default;
        for (int i = 0; i < s_renderers.Count; i++)
        {
            Renderer r = s_renderers[i];
            if (r == null || !r.enabled || !(r is SkinnedMeshRenderer || r is MeshRenderer))
                continue;
            if (!any)
            {
                b = r.bounds;
                any = true;
            }
            else
            {
                b.Encapsulate(r.bounds);
            }
        }
        s_renderers.Clear();
        if (!any)
            return;
        _tRadius = Mathf.Clamp(Mathf.Max(b.extents.x, b.extents.z) * 0.8f, 0.35f, 2.5f);
        _tHeight = Mathf.Clamp(b.max.y - _groundY, 1f, 6f);
    }

    Vector3 PlayerPos()
    {
        Transform p = _ctl != null ? _ctl.NbPlayerRoot : null;
        return p != null ? p.position : _T - _F * 2.4f;
    }

    float SafeDistance(Vector3 origin, Vector3 dir, float dist, float margin)
    {
        int n = Physics.RaycastNonAlloc(origin, dir, s_hits, dist + margin, ~0, QueryTriggerInteraction.Ignore);
        float best = dist;
        for (int i = 0; i < n; i++)
        {
            Collider c = s_hits[i].collider;
            if (c == null || IsSubject(c.transform))
                continue;
            best = Mathf.Min(best, s_hits[i].distance - margin);
        }
        return Mathf.Max(0.4f, best);
    }

    bool IsSubject(Transform t)
    {
        if (_ctl == null)
            return false;
        Transform p = _ctl.NbPlayerRoot;
        Transform tr = _ctl.NbTargetRoot;
        return p != null && t.IsChildOf(p) || tr != null && t.IsChildOf(tr);
    }

    Vector3 SafeCam(Vector3 look, Vector3 pos)
    {
        Vector3 d = pos - look;
        float len = d.magnitude;
        if (len < 0.01f)
            return pos;
        d /= len;
        return look + d * SafeDistance(look, d, len, 0.3f);
    }

    Vector2 ResolveImpactViewport()
    {
        Camera cam = Camera.main;
        if (cam == null)
            return new Vector2(0.5f, 0.5f);
        Vector3 vp = cam.WorldToViewportPoint(_Tc);
        if (vp.z <= 0f)
            return new Vector2(0.5f, 0.5f);
        return new Vector2(Mathf.Clamp(vp.x, 0.25f, 0.75f), Mathf.Clamp(vp.y, 0.3f, 0.7f));
    }

    static float Hash(int i, int k)
    {
        return Mathf.Repeat(Mathf.Sin(i * 12.9898f + k * 78.233f + 0.5f) * 43758.5453f, 1f);
    }

    // ------------------------------------------------------------------ 어둠 / 림 라이트 / 무음

    void TickFades()
    {
        float dt = Time.unscaledDeltaTime;
        _darkW = Mathf.MoveTowards(_darkW, _darkTarget, dt * _darkRate);
        ApplyDarkness();

        _duckW = Mathf.MoveTowards(_duckW, _duckTarget, dt * _duckRate);
        if (_duckW > 0f)
        {
            AudioListener.volume = _listenerBase * Mathf.Lerp(1f, listenerDuck, _duckW * _duckW * (3f - 2f * _duckW));
            _duckApplied = true;
        }
        else if (_duckApplied)
        {
            AudioListener.volume = _listenerBase;
            _duckApplied = false;
        }
    }

    void ApplyDarkness()
    {
        float w = _darkW * _darkW * (3f - 2f * _darkW);
        if (_volume != null)
        {
            _volume.weight = w;
            bool on = w > 0.0005f;
            if (_volume.enabled != on)
                _volume.enabled = on;
        }

        bool lit = w > 0.001f && _rimKey != null && _rimRed != null;
        if (_rimKey != null && _rimKey.enabled != lit)
            _rimKey.enabled = lit;
        if (_rimRed != null && _rimRed.enabled != lit)
            _rimRed.enabled = lit;
        if (!lit)
            return;

        // 카메라 반대편(대상 뒤)에서 흰 키 림, 아래 반대쪽에서 붉은 림 — 컷이 바뀌어도 따라간다
        Camera cam = Camera.main;
        Vector3 camPos = cam != null ? cam.transform.position : _Tc - _F * 4f;
        Vector3 away = _Tc - camPos;
        away.y = 0f;
        away = away.sqrMagnitude > 0.0001f ? away.normalized : _F;
        Vector3 side = Vector3.Cross(Vector3.up, away);
        float range = _tRadius * 2f + 3.5f;
        _rimKey.transform.position = _Tc + away * (_tRadius + 0.9f) + side * (_tRadius * 0.6f) + Vector3.up * (_tHeight * 0.3f);
        _rimRed.transform.position = _Tc + away * (_tRadius + 0.7f) - side * (_tRadius * 0.8f + 0.3f) - Vector3.up * (_tHeight * 0.2f);
        _rimKey.range = range;
        _rimRed.range = range;
        _rimKey.intensity = rimKeyIntensity * w;
        _rimRed.intensity = rimRedIntensity * w;
    }

    void PlaySfx(AudioClip clip, float volume, float pitch)
    {
        if (clip == null)
            return;
        AudioSource src = _sfx[_sfxNext];
        _sfxNext = (_sfxNext + 1) % _sfx.Length;
        if (src == null)
            return;
        src.pitch = pitch;
        // 월드 소리는 빠졌지만(리스너 볼륨) 베기/납도는 또렷하게: 리스너 볼륨을 무시하고 마스터 기준값만 곱한다
        float master = _duckApplied ? _listenerBase : AudioListener.volume;
        src.PlayOneShot(clip, AudioOptionsRuntime.ScaleSfx(volume) * master);
    }

    // ------------------------------------------------------------------ 오브젝트 구성 / 예열

    void EnsurePresentationObjects()
    {
        if (_mpb == null)
            _mpb = new MaterialPropertyBlock();

        if (_cuts[0] == null && s_cutMat != null && s_cutMesh != null)
        {
            for (int i = 0; i < _cuts.Length; i++)
                _cuts[i] = CreateCut("UltHangingCut_" + i);
            _glint = CreateCut("UltSheatheGlint");
        }

        if (_spiritGo == null && s_spiritMat != null && s_spiritMesh != null)
        {
            _spiritGo = new GameObject("UltEgoSpirit");
            _spiritT = _spiritGo.transform;
            _spiritGo.AddComponent<MeshFilter>().sharedMesh = s_spiritMesh;
            _spiritR = _spiritGo.AddComponent<MeshRenderer>();
            ConfigureRenderer(_spiritR, s_spiritMat);
            _spiritGo.SetActive(false);
        }

        if (_rimKey == null)
        {
            _rimKey = CreateLight("UltRimKey", rimKeyColor);
            _rimRed = CreateLight("UltRimRed", rimRedColor);
        }

        if (_volume == null)
        {
            var go = new GameObject("UltNightbringerGrade");
            go.transform.SetParent(transform, false);
            _volume = go.AddComponent<Volume>();
            _profile = ScriptableObject.CreateInstance<VolumeProfile>();
            _profile.name = "UltNightbringerGrade (runtime)";
            _profile.hideFlags = HideFlags.HideAndDontSave;
            _colorAdjust = _profile.Add<ColorAdjustments>(false);
            _colorAdjust.postExposure.Override(darkExposure);
            _colorAdjust.contrast.Override(darkContrast);
            _vignette = _profile.Add<Vignette>(false);
            _vignette.intensity.Override(darkVignette);
            _vignette.color.Override(Color.black);
            _vignette.smoothness.Override(0.45f);
            _volume.isGlobal = true;
            _volume.priority = 10050f;   // 비디오 옵션 볼륨(10000) 위
            _volume.sharedProfile = _profile;
            _volume.weight = 0f;
            _volume.enabled = false;
        }

        for (int i = 0; i < _sfx.Length; i++)
        {
            if (_sfx[i] != null)
                continue;
            AudioSource src = gameObject.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.loop = false;
            src.spatialBlend = 0f;
            src.ignoreListenerVolume = true;
            src.ignoreListenerPause = false;
            _sfx[i] = src;
        }
    }

    Cut CreateCut(string name)
    {
        var go = new GameObject(name);
        go.AddComponent<MeshFilter>().sharedMesh = s_cutMesh;
        MeshRenderer r = go.AddComponent<MeshRenderer>();
        ConfigureRenderer(r, s_cutMat);
        go.SetActive(false);
        return new Cut { Go = go, T = go.transform, R = r };
    }

    Light CreateLight(string name, Color color)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        Light l = go.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = color;
        l.shadows = LightShadows.None;
        l.intensity = 0f;
        l.range = 5f;
        l.renderMode = LightRenderMode.ForcePixel;
        l.enabled = false;
        return l;
    }

    static void ConfigureRenderer(MeshRenderer r, Material m)
    {
        r.sharedMaterial = m;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        r.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
    }

    static void LoadShared()
    {
        if (s_loaded)
            return;
        s_loaded = true;
        s_spiritMesh = Resources.Load<Mesh>(ResPrefix + "Ult_EgoSpirit");
        s_spiritMat = Resources.Load<Material>(ResPrefix + "M_Ult_EgoSpirit");
        s_cutMat = Resources.Load<Material>(ResPrefix + "M_Ult_HangingCut");
        s_slashClip = Resources.Load<AudioClip>("Audio/SFX_ScreenCut_Slash_GEN");
        s_clickClip = Resources.Load<AudioClip>("Audio/SFX_ScreenCut_Sheathe_GEN");
        s_cutMesh = BuildCutMesh();
    }

    /// <summary>절단선 메시: 두 장의 직교 쿼드, z 0..1 = 길이, u = 폭, v = 길이 (ChuOn/UltimateCut 규약).</summary>
    static Mesh BuildCutMesh()
    {
        var m = new Mesh { name = "Ult_HangingCut (runtime)", hideFlags = HideFlags.DontSave };
        m.vertices = new[]
        {
            new Vector3(-0.5f, 0f, 0f), new Vector3(0.5f, 0f, 0f), new Vector3(-0.5f, 0f, 1f), new Vector3(0.5f, 0f, 1f),
            new Vector3(0f, -0.5f, 0f), new Vector3(0f, 0.5f, 0f), new Vector3(0f, -0.5f, 1f), new Vector3(0f, 0.5f, 1f)
        };
        m.normals = new[]
        {
            Vector3.up, Vector3.up, Vector3.up, Vector3.up,
            Vector3.right, Vector3.right, Vector3.right, Vector3.right
        };
        m.uv = new[]
        {
            new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f),
            new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f)
        };
        var white = new Color[8];
        for (int i = 0; i < white.Length; i++)
            white[i] = Color.white;
        m.colors = white;
        m.triangles = new[] { 0, 2, 1, 1, 2, 3, 4, 6, 5, 5, 6, 7 };
        m.bounds = new Bounds(new Vector3(0f, 0f, 0.5f), Vector3.one);
        return m;
    }

    /// <summary>
    /// 첫 발동 히치 방지: 정령/절단선 재질을 메인 카메라 앞에서 알파 0 으로 2 프레임 렌더 (셰이더 변형·버퍼 업로드).
    /// PlayerUltimateController 의 기존 예열 경로(씬 시작·하층 도착 암전)에서 호출된다.
    /// </summary>
    public static IEnumerator CoPrewarmAssets()
    {
        yield return null;
        LoadShared();
        Camera cam = Camera.main;
        if (cam == null || (s_spiritMat == null && s_cutMat == null))
            yield break;

        var holder = new GameObject("__UltNightbringerWarm") { hideFlags = HideFlags.DontSave };
        Transform ct = cam.transform;
        holder.transform.SetPositionAndRotation(ct.position + ct.forward * 2.5f, ct.rotation);
        var mpb = new MaterialPropertyBlock();
        mpb.SetFloat(IdAlpha, 0f);
        mpb.SetFloat(IdIntensity, 0f);
        AddWarmRenderer(holder.transform, s_spiritMesh, s_spiritMat, mpb, new Vector3(-0.3f, -0.4f, 0f));
        AddWarmRenderer(holder.transform, s_cutMesh, s_cutMat, mpb, new Vector3(0.3f, 0f, 0f));
        yield return null;
        yield return null;
        Destroy(holder);
    }

    static void AddWarmRenderer(Transform parent, Mesh mesh, Material material, MaterialPropertyBlock mpb, Vector3 local)
    {
        if (mesh == null || material == null)
            return;
        var go = new GameObject("warm");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = local;
        go.transform.localScale = Vector3.one * 0.3f;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer r = go.AddComponent<MeshRenderer>();
        ConfigureRenderer(r, material);
        r.SetPropertyBlock(mpb);
    }

    // ------------------------------------------------------------------ 시간 / 입력

    void RequestTimeStop()
    {
        if (_timeStopRequested)
            return;
        _timeStopRequested = true;
        GameTimeScale.Request(this, 0f);
    }

    void ReleaseTimeStop()
    {
        if (!_timeStopRequested)
            return;
        _timeStopRequested = false;
        GameTimeScale.Release(this);
    }

    static bool SkipPressed()
    {
        Keyboard kb = Keyboard.current;
        Gamepad pad = Gamepad.current;
        return kb != null && (kb.fKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame)
               || pad != null && (pad.buttonSouth.wasPressedThisFrame || pad.startButton.wasPressedThisFrame);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        s_loaded = false;
        s_spiritMesh = null;
        s_cutMesh = null;
        s_spiritMat = null;
        s_cutMat = null;
        s_slashClip = null;
        s_clickClip = null;
        CurrentBeat = Beat.Idle;
        SkipCount = 0;
        LastStartRealtime = -1f;
        QaTimeline = "";
    }
}
