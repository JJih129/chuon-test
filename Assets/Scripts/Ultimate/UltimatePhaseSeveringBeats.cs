// 궁극기 '위상단절검' 비트 연출 (반복 11) — UltimateCinematicController 가 타임라인과 함께 구동
// 기획(전투 시스템 명세서 7단계 콘티): 페이즈 코어 충전 → 흑백 흡수(세계가 칼날로 빨려 듦) → 위상 단절 대시 베기
// → 시간 정지 → 마지막 일격 → 화면 갈라짐 → 색 복귀 + 피해(최대 HP 25%) + 보스 경직.
//
// 타임라인(TL_Ultimate_PlayerSword, 5.6 s) 은 그대로 두고, 루트 플레이어블 속도로 비트를 늘리거나 멈춘다.
//   0.00–0.60  인트로 포즈   : 속도 0.55 (코어 충전, HUD 코어 플레어 후 HUD 페이드)
//   0.60       클로즈업      : 흑백 흡수 시작 (외곽 → 칼날, 1.25 s)
//   0.95       흡수 정지     : 흡수가 끝날 때까지 정지
//   1.07–2.90  슬래시 스톰   : 대시 베기 (흑백 세계 속 컬러 검기)
//   2.84       시간 정지     : 0.9 s (월드 시간 0, 카메라 궤도만 느리게)
//   → 화면 갈라짐 시작 (프레임 캡처 + 균열 0.22 s) → 파편 폭발과 동시에 재생 재개
//   2.90       워크아웃 + 최종 폭발 + 피해 확정 (파편 사이로 컬러 복귀)
// 건너뛰기: F / Space / Enter / 패드 South (시작 0.5 s 후) → 정지 해제 + 6배속 (신호는 모두 정상 발사).
// 모든 타이밍은 실시간(unscaled).
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Playables;

[DisallowMultipleComponent]
public sealed class UltimatePhaseSeveringBeats : MonoBehaviour
{
    public enum Beat { Idle, Charge, Absorb, AbsorbHold, Slashes, TimeStop, Shatter, Release, Skipping }

    [Header("Timeline marks (s)")]
    [SerializeField] float closeUpTime = 0.60f;
    [SerializeField] float absorbHoldTime = 0.95f;
    [SerializeField] float timeStopTime = 2.84f;

    [Header("Beat lengths (realtime s)")]
    [SerializeField] float chargeSpeed = 0.55f;
    [SerializeField] float absorbSweep = 1.25f;
    [SerializeField] float absorbHoldExtra = 0.12f;
    [SerializeField] float timeStopDuration = 0.9f;
    [SerializeField] float shatterCrack = 0.22f;
    [SerializeField] float shatterBurst = 0.6f;
    [SerializeField] float colourReturn = 0.4f;
    [SerializeField] float skipSpeed = 6f;
    [SerializeField] float skipGrace = 0.5f;

    /// <summary>타임라인 길이 외에 비트가 추가하는 최대 실시간 (안전 워치독 여유).</summary>
    public const float ExtraDurationBudget = 4.5f;

    public static Beat CurrentBeat { get; private set; } = Beat.Idle;
    public static int SkipCount { get; private set; }
    public static float LastStartRealtime { get; private set; } = -1f;

    PlayableDirector _director;
    PlayerUltimateController _owner;
    Transform _focus;
    Transform _target;
    readonly List<Transform> _keep = new List<Transform>(6);
    float _beatStart;
    float _startRealtime;
    float _speed = 1f;
    bool _absorbStarted;
    bool _timeStopRequested;
    bool _active;

    /// <summary>워크아웃/카메라 궤도 코루틴이 곱할 속도 (정지 중 0, 건너뛰기 중 배속).</summary>
    public float PlaybackSpeed => _active ? _speed : 1f;
    /// <summary>슬래시 스톰 카메라 궤도 배율 (시간 정지 중엔 아주 느리게 돈다).</summary>
    public float OrbitScale => !_active ? 1f : CurrentBeat == Beat.TimeStop || CurrentBeat == Beat.Shatter ? 0.3f : _speed;
    public bool IsActive => _active;

    public void Begin(PlayableDirector director, PlayerUltimateController owner, Transform focus, Transform target, IList<Transform> keepSubjects)
    {
        _director = director;
        _owner = owner;
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
        _absorbStarted = false;
        _timeStopRequested = false;
        _startRealtime = Time.unscaledTime;
        LastStartRealtime = _startRealtime;
        enabled = true;

        PhaseCoreHUD.Active?.PlayActivationFlare();
        Enter(Beat.Charge);
        SetSpeed(chargeSpeed);
    }

    public void End()
    {
        if (!_active)
            return;
        _active = false;
        QaTimeline += "End@" + (Time.unscaledTime - _startRealtime).ToString("F2");
        ReleaseTimeStop();
        ActionColourDirector.EndAbsorb(this, CurrentBeat == Beat.Skipping ? 0.2f : colourReturn);
        CurrentBeat = Beat.Idle;
        _speed = 1f;
        _director = null;
        _owner = null;
        enabled = false;
    }

    void OnDisable()
    {
        if (_active)
            End();
    }

    void Enter(Beat beat)
    {
        CurrentBeat = beat;
        _beatStart = Time.unscaledTime;
        if (beat == Beat.Charge)
            QaTimeline = "";
        QaTimeline += beat + "@" + (_beatStart - _startRealtime).ToString("F2") + "(t=" + (_director != null ? _director.time : 0d).ToString("F2") + ") ";
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        QaFrameSampler.Event("ult." + beat);
#endif
    }

    /// <summary>QA: 마지막 궁극기 비트 전환 기록 (실시간 오프셋, 타임라인 시각).</summary>
    public static string QaTimeline { get; private set; } = "";

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

    void Update()
    {
        if (!_active || _director == null)
            return;

        float now = Time.unscaledTime;
        double t = _director.time;
        // 다음 프레임에 넘어갈 시간까지 미리 본다 (프레임이 길어도 2.90 워크아웃 신호를 넘기지 않게)
        double lead = Mathf.Min(0.05f, Time.unscaledDeltaTime) * _speed * 1.25f;

        if (CurrentBeat != Beat.Skipping && now - _startRealtime > skipGrace && SkipPressed())
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

        switch (CurrentBeat)
        {
            case Beat.Charge:
                if (t >= closeUpTime)
                {
                    StartAbsorb();
                    Enter(Beat.Absorb);
                    SetSpeed(1f);
                }
                break;

            case Beat.Absorb:
                if (t + lead >= absorbHoldTime)
                {
                    Enter(Beat.AbsorbHold);
                    SetSpeed(0f);
                }
                break;

            case Beat.AbsorbHold:
            {
                // 흡수 스윕이 끝날 때까지 클로즈업을 붙잡는다
                float sweepEndsAt = _absorbStartTime + absorbSweep + absorbHoldExtra;
                if (now >= sweepEndsAt)
                {
                    Enter(Beat.Slashes);
                    SetSpeed(1f);
                }
                break;
            }

            case Beat.Slashes:
                if (t + lead >= timeStopTime)
                {
                    Enter(Beat.TimeStop);
                    SetSpeed(0f);
                    RequestTimeStop();
                    ChuonAudioMix.LowpassDip(timeStopDuration + shatterCrack);
                }
                break;

            case Beat.TimeStop:
                if (now - _beatStart >= timeStopDuration)
                {
                    Enter(Beat.Shatter);
                    ScreenShatter.Play(ResolveImpactViewport(), shatterCrack, shatterBurst, HandleShatterBurst);
                }
                break;

            case Beat.Shatter:
                // 균열이 퍼지는 동안 계속 정지. 폭발 콜백에서 재개.
                if (now - _beatStart > shatterCrack + 0.5f)
                    HandleShatterBurst();
                break;
        }
    }

    float _absorbStartTime;

    void StartAbsorb()
    {
        if (_absorbStarted)
            return;
        _absorbStarted = true;
        _absorbStartTime = Time.unscaledTime;
        ActionColourDirector.BeginAbsorb(this, _focus, absorbSweep, _keep);
    }

    void HandleShatterBurst()
    {
        if (!_active || CurrentBeat != Beat.Shatter)
            return;
        Enter(Beat.Release);
        ReleaseTimeStop();
        SetSpeed(1f);
        // 파편 아래 라이브 화면은 컬러로 — 코어에서 바깥으로 색이 돌아온다
        ActionColourDirector.EndAbsorb(this, colourReturn);
    }

    void Skip()
    {
        SkipCount++;
        if (!_absorbStarted)
        {
            _absorbStarted = true;
            _absorbStartTime = Time.unscaledTime;
        }
        Enter(Beat.Skipping);
        ReleaseTimeStop();
        ScreenShatter.Skip();
        ActionColourDirector.EndAbsorb(this, 0.2f);
        SetSpeed(skipSpeed);
    }

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

    Vector2 ResolveImpactViewport()
    {
        Camera cam = Camera.main;
        Transform anchor = _target != null ? _target : _focus;
        if (cam == null || anchor == null)
            return new Vector2(0.5f, 0.5f);
        Vector3 vp = cam.WorldToViewportPoint(anchor.position);
        if (vp.z <= 0f)
            return new Vector2(0.5f, 0.5f);
        return new Vector2(Mathf.Clamp(vp.x, 0.25f, 0.75f), Mathf.Clamp(vp.y, 0.3f, 0.7f));
    }

    static bool SkipPressed()
    {
        Keyboard kb = Keyboard.current;
        Gamepad pad = Gamepad.current;
        return kb != null && (kb.fKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame)
               || pad != null && (pad.buttonSouth.wasPressedThisFrame || pad.startButton.wasPressedThisFrame);
    }

    /// <summary>QA/외부: 진행 중 연출 건너뛰기.</summary>
    public void RequestSkip()
    {
        if (_active && CurrentBeat != Beat.Skipping)
            Skip();
    }
}
