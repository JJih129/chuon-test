// 파일명: KioskDirector.cs
// 전시(PlayX4 2026) 키오스크 모드 총괄. 자체 부트스트랩(DontDestroyOnLoad), 기본 꺼짐 (KioskConfig).
//  - 게임플레이 방치 90 s → "계속하시겠습니까? 10" 카운트다운(게임 정지) → 타이틀 (깨끗한 리셋)
//  - 타이틀 방치 60 s → 어트랙트 루프 (PV 영상 / 대체 연출), 아무 입력 → 타이틀
//  - 세션 상한 10분 (9분에 경고 토스트, 시네마틱·전투 중이면 최대 90 s 유예) → 데모 종료 카드 → 타이틀
//  - 크레딧까지 완주 → 타이틀에서 감사 카드
//  - 부스 안전장치 (KioskSafety), 스태프 패널 (Ctrl+Shift+K / 패드 LB+RB+View 3초), 통계 파일 (KioskStatsStore)
// 문서: Docs/Exhibition_Kiosk.md
using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

[DefaultExecutionOrder(-400)]
[DisallowMultipleComponent]
public sealed class KioskDirector : MonoBehaviour
{
    const float StaffComboSeconds = 3f;
    const float ResetTimeoutSeconds = 6f;
    const float AttractDismissGrace = 0.6f;

    static KioskDirector s_instance;

    /// <summary>키오스크가 타이틀로 리셋을 시작할 때 (사유). 다른 시스템이 세션 상태를 비울 때 사용.</summary>
    public static event Action<string> ResetStarted;
    /// <summary>세션 종료 (사유, 길이 초).</summary>
    public static event Action<string, float> SessionEnded;

    public static KioskDirector Instance => s_instance;
    public static bool SessionRunning => s_instance != null && s_instance._session.Running;
    public static float SessionElapsed => s_instance != null ? s_instance._session.Elapsed : 0f;

    KioskOverlays _overlays;
    KioskAttract _attract;
    KioskStaffPanel _staff;
    readonly KioskSafety _safety = new KioskSafety();

    KioskIdleTracker _gameIdle;
    KioskIdleTracker _titleIdle;
    readonly KioskSessionClock _session = new KioskSessionClock();

    string _scene = string.Empty;
    string _lastGameplayScene = string.Empty;
    bool _wasEnabled;
    bool _resetInFlight;
    float _resetStartedAt;
    bool _resetForced;
    string _pendingReason;
    bool _sawCredits;

    bool _endCardActive;
    float _endCardStartedAt;
    bool _endCardResetsToTitle;

    float _comboHeld;
    bool _comboLatched;

    bool _timeRequested;
    EventSystem _blockedEventSystem;
    bool _uiResumePending;
    float _uiResumeAt = -1f;

    float _deferCheckAt;
    bool _deferCached;
    bool _errorLogged;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        s_instance = null;
        ResetStarted = null;
        SessionEnded = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Bootstrap()
    {
        if (s_instance != null || Application.isBatchMode)
            return;

        GameObject go = new GameObject("__ChuonKiosk");
        DontDestroyOnLoad(go);
        s_instance = go.AddComponent<KioskDirector>();
    }

    void Awake()
    {
        if (s_instance != null && s_instance != this)
        {
            Destroy(gameObject);
            return;
        }
        s_instance = this;

        KioskSettings s = KioskConfig.Settings;
        _gameIdle = new KioskIdleTracker(s.GameplayIdleSeconds, s.CountdownSeconds);
        _titleIdle = new KioskIdleTracker(s.AttractIdleSeconds, 0f);
        _scene = SceneManager.GetActiveScene().name;
    }

    void OnEnable()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    void OnDisable()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        ReleaseTime();
        _safety.Disable();
    }

    void OnDestroy()
    {
        if (s_instance == this)
            s_instance = null;
    }

    void OnApplicationQuit()
    {
        // 세션 도중 종료(스태프 종료·전원)도 기록
        if (_session.Running)
            Guard(() => EndSession("app_quit"));
    }

    // ------------------------------------------------------------------
    // 공개 API (다른 시스템·일시정지 메뉴 연결용)
    // ------------------------------------------------------------------

    /// <summary>현재 세션을 끝내고 깨끗한 상태로 타이틀로 (키오스크 꺼져 있어도 동작).</summary>
    public static void RequestResetToTitle(string reason)
    {
        if (s_instance != null)
            s_instance.ResetToTitle(string.IsNullOrEmpty(reason) ? KioskResetReason.Staff : reason, keepEndCard: false);
    }

    // ------------------------------------------------------------------
    // 루프
    // ------------------------------------------------------------------

    void Update()
    {
        try
        {
            Tick();
        }
        catch (Exception e)
        {
            if (!_errorLogged)
            {
                _errorLogged = true;
                Debug.LogException(e, this);
            }
        }
    }

    void Tick()
    {
        float udt = Mathf.Min(Time.unscaledDeltaTime, 1f);
        bool activity = false;
        bool mouseMoved = false;
        if (_wasEnabled || KioskConfig.Enabled)
            activity = KioskInputActivity.AnyActivity(out mouseMoved); // 꺼져 있으면 스태프 콤보만 본다

        UpdateStaffCombo(udt);
        if (_staff != null && _staff.IsOpen)
        {
            HandleStaffCommand(_staff.ReadCommand());
            activity = true; // 스태프가 앞에 있다
        }

        bool enabled = KioskConfig.Enabled;
        if (enabled != _wasEnabled)
            ApplyEnabled(enabled);

        UpdateUiResume();

        if (_resetInFlight)
        {
            UpdateResetTimeout();
            if (_endCardActive)
                _overlays.UpdateEndCard(0f, false);
            return;
        }

        if (!enabled)
            return;

        _safety.Tick(udt, mouseMoved);

        if (_endCardActive)
        {
            UpdateEndCard();
            return;
        }

        if (_staff != null && _staff.IsOpen)
            return;

        if (KioskConfig.IsTitleScene(_scene))
            UpdateTitle(udt, activity);
        else if (KioskConfig.IsGameplayScene(_scene))
            UpdateGameplay(udt, activity);

        if (!KioskConfig.IsTitleScene(_scene))
            UpdateSession(udt);
    }

    void ApplyEnabled(bool on)
    {
        _wasEnabled = on;
        if (on)
        {
            EnsureUi();
            KioskSettings s = KioskConfig.Settings;
            _gameIdle.Configure(s.GameplayIdleSeconds, s.CountdownSeconds);
            _gameIdle.Reset();
            _titleIdle.Configure(s.AttractIdleSeconds, 0f);
            _titleIdle.Reset();
            _safety.Enable();
            if (KioskConfig.StartsSession(_scene) && !_session.Running)
                StartSession();
            Debug.Log($"[Kiosk] enabled (idle {s.GameplayIdleSeconds:0}s+{s.CountdownSeconds:0}s, attract {s.AttractIdleSeconds:0}s, cap {KioskConfig.EffectiveSessionCapSeconds:0}s)");
        }
        else
        {
            _safety.Disable();
            if (_overlays != null) _overlays.HideAll();
            if (_attract != null) _attract.Hide();
            _endCardActive = false;
            ReleaseTime();
            if (_session.Running)
                _session.Stop(); // 꺼진 동안의 플레이는 통계에 넣지 않는다
            ResumeUiNow();
            Debug.Log("[Kiosk] disabled");
        }
    }

    void EnsureUi()
    {
        if (_overlays == null)
            _overlays = KioskOverlays.Create(transform);
        if (_attract == null)
            _attract = KioskAttract.Create(transform);
        if (_staff == null)
        {
            _staff = KioskStaffPanel.Create(transform);
            _staff.StatusProvider = BuildStaffStatus;
        }
    }

    // ------------------------------------------------------------------
    // 게임플레이: 방치 → 카운트다운 → 타이틀
    // ------------------------------------------------------------------

    void UpdateGameplay(float udt, bool activity)
    {
        bool frozen = CinematicDirector.IsPlaying || LoadingTipOverlay.IsVisible || ChuonInputRebinding.IsRebinding;
        KioskIdlePhase phase = _gameIdle.Tick(udt, activity, frozen);

        switch (phase)
        {
            case KioskIdlePhase.Active:
                if (_overlays.CountdownVisible)
                {
                    _overlays.HideCountdown();
                    ReleaseTime();
                }
                break;

            case KioskIdlePhase.Countdown:
                _overlays.ShowCountdown(_gameIdle.CountdownDisplay, _gameIdle.CountdownFraction);
                RequestTime();
                break;

            case KioskIdlePhase.Expired:
                ResetToTitle(KioskResetReason.Idle, keepEndCard: false);
                break;
        }
    }

    // ------------------------------------------------------------------
    // 타이틀: 방치 → 어트랙트
    // ------------------------------------------------------------------

    void UpdateTitle(float udt, bool activity)
    {
        if (_attract.IsShowing)
        {
            if (_attract.ShownSeconds > AttractDismissGrace && KioskInputActivity.AnyPressedThisFrame(includeMouseMove: true))
            {
                _attract.Hide();
                _titleIdle.Reset();
                ResumeUiWhenReleased();
            }
            return;
        }

        if (_titleIdle.Tick(udt, activity, frozen: false) == KioskIdlePhase.Expired)
        {
            _titleIdle.Reset();
            BlockUi();
            _attract.Show();
            KioskStatsStore.RecordAttract();
        }
    }

    // ------------------------------------------------------------------
    // 세션 상한
    // ------------------------------------------------------------------

    void StartSession()
    {
        KioskSettings s = KioskConfig.Settings;
        _session.Configure(KioskConfig.EffectiveSessionCapSeconds, s.SessionWarningSeconds, s.SessionMaxDeferSeconds);
        _session.Start();
        _sawCredits = false;
        Debug.Log("[Kiosk] session start (" + _scene + ")");
    }

    void EndSession(string reason)
    {
        float seconds = _session.Stop();
        KioskStatsStore.RecordSession(seconds, reason, _lastGameplayScene);
        Debug.Log($"[Kiosk] session end: {reason} after {KioskStatsData.FormatDuration(seconds)}");
        try { SessionEnded?.Invoke(reason, seconds); }
        catch (Exception e) { Debug.LogException(e); }
    }

    void UpdateSession(float udt)
    {
        if (!_session.Running)
            return;

        bool defer = false;
        if (_session.CapSeconds > 0f && _session.Elapsed >= _session.CapSeconds)
        {
            float now = Time.unscaledTime;
            if (now >= _deferCheckAt)
            {
                _deferCheckAt = now + 1f;
                _deferCached = CinematicDirector.IsPlaying || SafeCombatActive();
            }
            defer = _deferCached;
        }

        switch (_session.Tick(udt, defer))
        {
            case KioskSessionEvent.Warning:
                _overlays.ShowToast("SESSION", "체험 시간이 1분 남았습니다.");
                break;
            case KioskSessionEvent.CapReached:
                BeginEndCard(resetToTitle: true);
                break;
        }
    }

    static bool SafeCombatActive()
    {
        try { return CinematicDirector.IsCombatActive(); }
        catch (Exception) { return false; }
    }

    // ------------------------------------------------------------------
    // 엔드 카드
    // ------------------------------------------------------------------

    void BeginEndCard(bool resetToTitle)
    {
        EnsureUi();
        _endCardActive = true;
        _endCardStartedAt = Time.unscaledTime;
        _endCardResetsToTitle = resetToTitle;
        _overlays.HideCountdown();
        _overlays.HideToast();
        _overlays.ShowEndCard(completed: !resetToTitle);
        if (resetToTitle)
            RequestTime();
        BlockUi();
    }

    void UpdateEndCard()
    {
        KioskSettings s = KioskConfig.Settings;
        float t = Time.unscaledTime - _endCardStartedAt;
        bool canSkip = t >= s.EndCardMinSeconds;
        _overlays.UpdateEndCard(s.EndCardSeconds - t, canSkip);
        if (_endCardResetsToTitle)
            RequestTime();

        if (t < s.EndCardSeconds && !(canSkip && KioskInputActivity.AnyPressedThisFrame(includeMouseMove: false)))
            return;

        if (_endCardResetsToTitle)
        {
            ResetToTitle(KioskResetReason.SessionCap, keepEndCard: true);
        }
        else
        {
            _endCardActive = false;
            _overlays.HideEndCard();
            _titleIdle.Reset();
            ResumeUiWhenReleased();
        }
    }

    // ------------------------------------------------------------------
    // 리셋
    // ------------------------------------------------------------------

    void ResetToTitle(string reason, bool keepEndCard)
    {
        if (_resetInFlight)
            return;

        _resetInFlight = true;
        _resetForced = false;
        _resetStartedAt = Time.unscaledTime;
        _pendingReason = reason;
        Debug.Log("[Kiosk] reset to title: " + reason);

        if (_overlays != null)
        {
            if (keepEndCard)
            {
                _overlays.HideCountdown();
                _overlays.HideToast();
            }
            else
            {
                _overlays.HideAll();
                _endCardActive = false;
            }
        }
        if (_attract != null)
            _attract.Hide();
        if (_staff != null && _staff.IsOpen)
            _staff.Close();

        ReleaseTime();
        Guard(() => RuntimeMenuSceneStateUtility.PrepareForMenuScene());
        Guard(() => { if (ChuonInputRebinding.IsRebinding) ChuonInputRebinding.Cancel(); });
        Guard(CinematicSettings.ResetRun);
        Guard(ClearCheckpointStatics);
        Guard(LoadingTipOverlay.HideImmediate);
        if (!_session.Running)
            Guard(() => KioskStatsStore.RecordReset(reason));

        try { ResetStarted?.Invoke(reason); }
        catch (Exception e) { Debug.LogException(e); }

        LoadTitle(useFader: true);
    }

    static void LoadTitle(bool useFader)
    {
        if (useFader)
        {
            try
            {
                SceneFader fader = SceneFader.Instance;
                if (fader != null && fader.fadeCanvasGroup != null)
                {
                    fader.FadeOutAndLoadScene(KioskConfig.TitleSceneName);
                    return;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Kiosk] fader load failed, loading directly: " + e.Message);
            }
        }

        SceneManager.LoadScene(KioskConfig.TitleSceneName, LoadSceneMode.Single);
    }

    void UpdateResetTimeout()
    {
        if (_resetForced || Time.unscaledTime - _resetStartedAt < ResetTimeoutSeconds)
            return;
        _resetForced = true; // 페이더가 멈춘 경우 등: 직접 로드 (1회)
        Debug.LogWarning("[Kiosk] reset timed out, loading title directly");
        Guard(() => LoadTitle(useFader: false));
    }

    /// <summary>체크포인트 재시작 플래그 정리 (다음 방문객이 중간부터 시작하지 않게). 해당 파일에 공개 API 가 생기면 교체.</summary>
    static void ClearCheckpointStatics()
    {
        LobbyManager.ClearCheckpointState();
        InfiltrationTutorialDirector.ClearCheckpointState();
    }

    static void SetStatic(Type type, string field, object value)
    {
        FieldInfo f = type.GetField(field, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        if (f != null && f.FieldType == value.GetType())
            f.SetValue(null, value);
    }

    // ------------------------------------------------------------------
    // 씬 전환
    // ------------------------------------------------------------------

    void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (mode != LoadSceneMode.Single)
            return;

        try
        {
            OnSingleSceneLoaded(scene.name);
        }
        catch (Exception e)
        {
            Debug.LogException(e, this);
        }
    }

    void OnSingleSceneLoaded(string sceneName)
    {
        _scene = sceneName ?? string.Empty;
        if (KioskConfig.IsGameplayScene(_scene))
            _lastGameplayScene = _scene;

        _blockedEventSystem = null;
        _uiResumePending = false;
        _uiResumeAt = -1f;
        _timeRequested = false; // GameTimeScale 은 씬 로드마다 다시 계산; 필요하면 아래에서 다시 요청
        if (GameTimeScale.HasRequest(this))
            GameTimeScale.Release(this);

        _gameIdle.Reset();
        _titleIdle.Reset();
        if (_overlays != null)
        {
            _overlays.HideCountdown();
            _overlays.HideToast();
        }
        if (_attract != null)
            _attract.Hide();

        bool isTitle = KioskConfig.IsTitleScene(_scene);
        if (isTitle)
        {
            string reason = _pendingReason;
            _resetInFlight = false;
            _pendingReason = null;
            bool endCardWasUp = _endCardActive;
            _endCardActive = false;
            if (_overlays != null)
                _overlays.HideEndCard();

            if (KioskConfig.Enabled)
            {
                Guard(ClearCheckpointStatics);
                _safety.OnSessionEnded();
            }

            if (_session.Running)
            {
                if (string.IsNullOrEmpty(reason))
                    reason = _sawCredits ? KioskResetReason.Completed : KioskResetReason.TitleMenu;
                EndSession(reason);
                if (reason == KioskResetReason.Completed && !endCardWasUp && KioskConfig.Enabled)
                    BeginEndCard(resetToTitle: false);
            }
            _sawCredits = false;
        }

        if (KioskConfig.Enabled != _wasEnabled)
            ApplyEnabled(KioskConfig.Enabled);
        if (!KioskConfig.Enabled)
            return;

        _safety.OnSceneLoaded();

        if (string.Equals(_scene, KioskConfig.CreditsSceneName, StringComparison.Ordinal))
            _sawCredits = true;
        else if (KioskConfig.StartsSession(_scene) && !_session.Running)
            StartSession();

        if (_endCardActive && _endCardResetsToTitle)
            RequestTime();
    }

    // ------------------------------------------------------------------
    // 스태프 패널
    // ------------------------------------------------------------------

    void UpdateStaffCombo(float udt)
    {
        if (!KioskInputActivity.StaffComboHeld())
        {
            _comboHeld = 0f;
            _comboLatched = false;
            return;
        }

        if (_comboLatched)
            return;

        _comboHeld += udt;
        if (_comboHeld < StaffComboSeconds)
            return;

        _comboLatched = true;
        EnsureUi();
        if (_staff.IsOpen)
            CloseStaff();
        else
            OpenStaff();
    }

    void OpenStaff()
    {
        _staff.Open();
        if (KioskConfig.IsGameplayScene(_scene))
            RequestTime();
        Cursor.visible = true;
    }

    void CloseStaff()
    {
        _staff.Close();
        bool keepPaused = (_overlays != null && _overlays.CountdownVisible) || (_endCardActive && _endCardResetsToTitle);
        if (!keepPaused)
            ReleaseTime();
        _gameIdle.Reset();
        _titleIdle.Reset();
    }

    void HandleStaffCommand(KioskStaffPanel.Command command)
    {
        switch (command)
        {
            case KioskStaffPanel.Command.ToggleKiosk:
                KioskConfig.SetEnabled(!KioskConfig.Enabled);
                _staff.Flash(KioskConfig.Enabled ? "키오스크 모드를 켰습니다 (다음 실행에도 유지)." : "키오스크 모드를 껐습니다.");
                break;

            case KioskStaffPanel.Command.ForceReset:
                CloseStaff();
                ResetToTitle(KioskResetReason.Staff, keepEndCard: false);
                break;

            case KioskStaffPanel.Command.ToggleSessionCap:
                KioskConfig.SetSessionCapEnabled(!KioskConfig.SessionCapEnabled);
                if (_session.Running)
                {
                    KioskSettings s = KioskConfig.Settings;
                    _session.Configure(KioskConfig.EffectiveSessionCapSeconds, s.SessionWarningSeconds, s.SessionMaxDeferSeconds);
                }
                _staff.Flash(KioskConfig.SessionCapEnabled ? "세션 상한을 켰습니다." : "세션 상한을 껐습니다.");
                break;

            case KioskStaffPanel.Command.ResetStats:
                KioskStatsStore.ResetStats();
                _staff.Flash("통계를 초기화했습니다 (세션 CSV 는 보존).");
                break;

            case KioskStaffPanel.Command.QuitGame:
                KioskSafety.AllowQuit = true;
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#else
                Application.Quit();
#endif
                break;

            case KioskStaffPanel.Command.Close:
                CloseStaff();
                break;
        }
    }

    string BuildStaffStatus()
    {
        string scene = string.IsNullOrEmpty(_scene) ? "-" : _scene;
        string session = _session.Running
            ? KioskStatsData.FormatDuration(_session.Elapsed) + (_session.CapSeconds > 0f ? " / " + KioskStatsData.FormatDuration(_session.CapSeconds) : string.Empty)
            : "없음";
        return "현재 장면          " + scene + "   ·   진행 중 세션 " + session;
    }

    // ------------------------------------------------------------------
    // 시간·UI 입력
    // ------------------------------------------------------------------

    void RequestTime()
    {
        if (!GameTimeScale.HasRequest(this))
            GameTimeScale.Request(this, 0f);
        _timeRequested = true;
    }

    void ReleaseTime()
    {
        if (!_timeRequested && !GameTimeScale.HasRequest(this))
            return;
        _timeRequested = false;
        GameTimeScale.Release(this);
    }

    /// <summary>오버레이 해제 입력이 타이틀 버튼을 누르지 않도록 UI 입력을 잠시 끈다.</summary>
    void BlockUi()
    {
        EventSystem es = EventSystem.current;
        if (es != null && es.enabled)
        {
            es.enabled = false;
            _blockedEventSystem = es;
        }
        _uiResumePending = false;
        _uiResumeAt = -1f;
    }

    void ResumeUiWhenReleased()
    {
        if (_blockedEventSystem == null)
            return;
        _uiResumePending = true;
        _uiResumeAt = -1f;
    }

    void ResumeUiNow()
    {
        if (_blockedEventSystem != null)
            _blockedEventSystem.enabled = true;
        _blockedEventSystem = null;
        _uiResumePending = false;
        _uiResumeAt = -1f;
    }

    void UpdateUiResume()
    {
        if (_blockedEventSystem == null || !_uiResumePending)
            return;

        if (_uiResumeAt < 0f)
        {
            if (KioskInputActivity.AllReleased())
                _uiResumeAt = Time.unscaledTime + 0.2f;
            return;
        }

        if (Time.unscaledTime >= _uiResumeAt)
            ResumeUiNow();
    }

    static void Guard(Action action)
    {
        try
        {
            action();
        }
        catch (Exception e)
        {
            Debug.LogWarning("[Kiosk] " + e.Message);
        }
    }
}
