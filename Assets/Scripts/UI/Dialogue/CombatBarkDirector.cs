using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 전투 중 짧은 상황 대사(bark). 기존 이벤트를 읽기 전용으로 구독하고,
/// IntroDialoguePresenter 의 오버레이 자막으로 띄운다 (조작을 막지 않음).
/// - 대사 데이터: DialogueTable.barkGroups (Resources/Dialogue/DialogueTable_KO)
/// - 전역 쿨다운(기본 8초) + 그룹별 쿨다운, 직전 줄 반복 금지
/// - 연출(인트로/궁극기 시네마틱/엔딩/상층 대사/일시정지) 중에는 재생하지 않는다.
/// - 튜토리얼 씬은 EGO 가이드가 따로 있으므로 비활성.
/// 씬 배치가 필요 없도록 런타임에 1개 생성된다 (DontDestroyOnLoad).
/// </summary>
[DisallowMultipleComponent]
public sealed class CombatBarkDirector : MonoBehaviour
{
    // ---- bark 그룹 키 (DialogueTable.barkGroups 와 일치)
    public const string GroupParryFirst = "parry.first";
    public const string GroupParry = "parry";
    public const string GroupPerfectDodge = "perfect_dodge";
    public const string GroupBreak = "break";
    public const string GroupFinisher = "finisher";
    public const string GroupLowHp = "low_hp";
    public const string GroupHeal = "heal";
    public const string GroupUltimateReady = "ult_ready";
    public const string GroupPhase2 = "phase2.typed";
    public const string GroupPhase3 = "phase3.typed";
    public const string GroupDeath = "death";
    public const string GroupRetry = "retry";

    [SerializeField, Min(0f)] float globalCooldown = 8f;
    [SerializeField, Range(0.05f, 0.9f)] float lowHpThreshold = 0.3f;
    [SerializeField, Range(0.1f, 1f)] float lowHpRearmThreshold = 0.5f;
    [SerializeField, Min(0f)] float retryBarkDelay = 1.4f;
    [SerializeField, Min(0f)] float rebindInterval = 1f;

    static CombatBarkDirector s_instance;

    readonly Dictionary<string, float> _groupLastTime = new Dictionary<string, float>();
    readonly Dictionary<string, int> _groupLastIndex = new Dictionary<string, int>();
    readonly HashSet<string> _onceUsed = new HashSet<string>();
    float _lastBarkTime = -999f;

    PlayerHealth _playerHealth;
    PlayerGuardController _guard;
    PerfectDodgeController _perfectDodge;
    PlayerConsumables _consumables;
    PlayerUltimateController _ultimate;
    readonly List<BossController> _bosses = new List<BossController>();
    readonly List<BossBreakController> _breaks = new List<BossBreakController>();
    readonly List<bool> _breakFinisherAvailable = new List<bool>();
    readonly List<float> _breakEnteredAt = new List<float>();
    readonly List<UnityEngine.Events.UnityAction> _breakEnterHandlers = new List<UnityEngine.Events.UnityAction>();
    MainSceneArrivalController[] _arrivalControllers = System.Array.Empty<MainSceneArrivalController>();

    bool _sceneActive;
    bool _lowHpArmed = true;
    bool _ultimateWasReady;
    int _lastAmpouleCount = -1;
    float _nextRebindTime;
    Canvas _fallbackCanvas;
    Coroutine _followUpRoutine;
    // 연출 중 막힌 중요 bark (페이즈 전환/피니셔)는 연출이 끝난 뒤 잠시 안에 재생한다.
    string _pendingGroup;
    float _pendingExpireAt;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (s_instance != null)
            return;

        var go = new GameObject("CombatBarkDirector");
        DontDestroyOnLoad(go);
        s_instance = go.AddComponent<CombatBarkDirector>();
    }

    /// <summary>외부(인트로 등)에서 bark 를 요청. force=true 면 전역 쿨다운·겹침 검사를 건너뛴다(연출 검사는 유지).</summary>
    public static bool TryBark(string group, bool force = false)
    {
        return s_instance != null && s_instance.Play(group, force);
    }

    void OnEnable()
    {
        SceneManager.sceneLoaded += HandleSceneLoaded;
        StartCoroutine(CoBindNextFrame(true));
    }

    void OnDisable()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        Unbind();
    }

    void OnDestroy()
    {
        if (s_instance == this)
            s_instance = null;
    }

    void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (mode != LoadSceneMode.Single)
            return;

        Unbind();
        _onceUsed.Clear();
        _groupLastTime.Clear();
        _lastBarkTime = -999f;
        StartCoroutine(CoBindNextFrame(true));
    }

    IEnumerator CoBindNextFrame(bool sceneStart)
    {
        yield return null; // 런타임 생성 플레이어/HUD 를 기다린다
        Bind();

        if (sceneStart && _sceneActive && MainSceneArrivalController.LastLoadWasRetry)
        {
            yield return new WaitForSecondsRealtime(retryBarkDelay);
            Play(GroupRetry, true);
        }
    }

    void Update()
    {
        if (Time.unscaledTime >= _nextRebindTime)
        {
            _nextRebindTime = Time.unscaledTime + rebindInterval;
            if (_playerHealth == null || (_bosses.Count == 0 && FindObjectOfType<BossController>() != null))
                Bind();
        }

        if (!_sceneActive)
            return;

        PollFinisher();

        if (_pendingGroup != null)
        {
            if (Time.unscaledTime > _pendingExpireAt)
                _pendingGroup = null;
            else if (!IsPresentationBlocking())
            {
                string group = _pendingGroup;
                _pendingGroup = null;
                Play(group, true);
            }
        }
    }

    // ------------------------------------------------------------------ binding

    void Bind()
    {
        Unbind();

        // 튜토리얼은 EGO 가이드(TutorialRuntimeBootstrap/TutorialManager)가 담당
        if (FindObjectOfType<TutorialRuntimeBootstrap>() != null || FindObjectOfType<TutorialManager>() != null)
        {
            _sceneActive = false;
            return;
        }

        GameObject player = GameObject.FindGameObjectWithTag("Player");
        _playerHealth = player != null ? player.GetComponentInChildren<PlayerHealth>() : FindObjectOfType<PlayerHealth>();
        if (_playerHealth == null)
        {
            _sceneActive = false;
            return;
        }

        GameObject root = player != null ? player : _playerHealth.gameObject;
        _guard = root.GetComponentInChildren<PlayerGuardController>();
        _perfectDodge = root.GetComponentInChildren<PerfectDodgeController>();
        _consumables = root.GetComponentInChildren<PlayerConsumables>();
        _ultimate = root.GetComponentInChildren<PlayerUltimateController>();

        _playerHealth.OnHealthChanged += HandleHealthChanged;
        _playerHealth.OnDied += HandlePlayerDied;
        if (_guard != null && _guard.OnParrySuccess != null)
            _guard.OnParrySuccess.AddListener(HandleParry);
        if (_perfectDodge != null && _perfectDodge.OnPerfectDodge != null)
            _perfectDodge.OnPerfectDodge.AddListener(HandlePerfectDodge);
        if (_consumables != null)
        {
            _lastAmpouleCount = _consumables.CurrentAmpoule;
            _consumables.OnAmpouleChanged += HandleAmpouleChanged;
        }
        if (_ultimate != null)
        {
            _ultimateWasReady = _ultimate.IsGaugeReady;
            _ultimate.OnGaugeChanged += HandleUltimateGauge;
        }

        BossController[] bosses = FindObjectsOfType<BossController>();
        for (int i = 0; i < bosses.Length; i++)
        {
            _bosses.Add(bosses[i]);
            bosses[i].OnBossPhaseChanged += HandleBossPhaseChanged;
        }

        BossBreakController[] breaks = FindObjectsOfType<BossBreakController>();
        for (int i = 0; i < breaks.Length; i++)
        {
            BossBreakController controller = breaks[i];
            int index = _breaks.Count;
            _breaks.Add(controller);
            _breakFinisherAvailable.Add(controller.IsFinisherAvailable);
            _breakEnteredAt.Add(-999f);
            UnityEngine.Events.UnityAction handler = () => HandleBreakEnter(index);
            _breakEnterHandlers.Add(handler);
            controller.OnBreakEnter.AddListener(handler);
        }

        _arrivalControllers = FindObjectsOfType<MainSceneArrivalController>();
        _lowHpArmed = _playerHealth.MaxHP <= 0 || (float)_playerHealth.CurrentHP / _playerHealth.MaxHP > lowHpThreshold;
        _sceneActive = true;
    }

    void Unbind()
    {
        if (_playerHealth != null)
        {
            _playerHealth.OnHealthChanged -= HandleHealthChanged;
            _playerHealth.OnDied -= HandlePlayerDied;
        }
        if (_guard != null && _guard.OnParrySuccess != null)
            _guard.OnParrySuccess.RemoveListener(HandleParry);
        if (_perfectDodge != null && _perfectDodge.OnPerfectDodge != null)
            _perfectDodge.OnPerfectDodge.RemoveListener(HandlePerfectDodge);
        if (_consumables != null)
            _consumables.OnAmpouleChanged -= HandleAmpouleChanged;
        if (_ultimate != null)
            _ultimate.OnGaugeChanged -= HandleUltimateGauge;

        for (int i = 0; i < _bosses.Count; i++)
        {
            if (_bosses[i] != null)
                _bosses[i].OnBossPhaseChanged -= HandleBossPhaseChanged;
        }

        for (int i = 0; i < _breaks.Count; i++)
        {
            if (_breaks[i] != null && i < _breakEnterHandlers.Count)
                _breaks[i].OnBreakEnter.RemoveListener(_breakEnterHandlers[i]);
        }

        _playerHealth = null;
        _guard = null;
        _perfectDodge = null;
        _consumables = null;
        _ultimate = null;
        _bosses.Clear();
        _breaks.Clear();
        _breakFinisherAvailable.Clear();
        _breakEnteredAt.Clear();
        _breakEnterHandlers.Clear();
        _arrivalControllers = System.Array.Empty<MainSceneArrivalController>();
        _sceneActive = false;
    }

    // ------------------------------------------------------------------ event handlers

    void HandleParry()
    {
        if (!_onceUsed.Contains(GroupParryFirst) && Play(GroupParryFirst, false))
            return;
        Play(GroupParry, false);
    }

    void HandlePerfectDodge() => Play(GroupPerfectDodge, false);

    void HandleBreakEnter(int index)
    {
        if (index >= 0 && index < _breakEnteredAt.Count)
            _breakEnteredAt[index] = Time.time;
        Play(GroupBreak, false);
    }

    void HandleHealthChanged(int current, int max)
    {
        if (max <= 0 || current <= 0)
            return;

        float ratio = (float)current / max;
        if (ratio > lowHpRearmThreshold)
        {
            _lowHpArmed = true;
            return;
        }

        if (_lowHpArmed && ratio <= lowHpThreshold)
        {
            // 쿨다운에 막히면 다음 피격 때 다시 시도
            if (Play(GroupLowHp, false))
                _lowHpArmed = false;
        }
    }

    void HandlePlayerDied() => Play(GroupDeath, true);

    void HandleAmpouleChanged(int current, int max)
    {
        if (_lastAmpouleCount >= 0 && current < _lastAmpouleCount)
            Play(GroupHeal, false);
        _lastAmpouleCount = current;
    }

    void HandleUltimateGauge(float gauge, float normalized, bool ready)
    {
        if (ready && !_ultimateWasReady)
            Play(GroupUltimateReady, false);
        _ultimateWasReady = ready;
    }

    void HandleBossPhaseChanged(int phase, float hpNormalized)
    {
        if (phase == 2)
            Play(GroupPhase2, true);
        else if (phase >= 3)
            Play(GroupPhase3, true);
    }

    // 피니셔 시작: 브레이크 중 피니셔 가능 → 불가로 바뀌었고 창 시간이 남아 있었다면(=사용됨).
    void PollFinisher()
    {
        for (int i = 0; i < _breaks.Count; i++)
        {
            BossBreakController controller = _breaks[i];
            if (controller == null)
                continue;

            bool available = controller.IsFinisherAvailable;
            bool wasAvailable = _breakFinisherAvailable[i];
            _breakFinisherAvailable[i] = available;
            if (!wasAvailable || available || !controller.IsInBreak)
                continue;

            float window = controller.finisherWindowSeconds;
            if (window > 0f && Time.time - _breakEnteredAt[i] >= window - 0.05f)
                continue; // 시간 초과로 닫힌 것

            Play(GroupFinisher, true);
        }
    }

    // ------------------------------------------------------------------ playback

    bool Play(string groupKey, bool force)
    {
        if (!ExhibitionPrototypePresentationPolicy.CombatBarksEnabled)
            return false;
        if (IsPresentationBlocking())
        {
            if (force && _sceneActive && groupKey != GroupDeath && groupKey != GroupRetry)
            {
                _pendingGroup = groupKey;
                _pendingExpireAt = Time.unscaledTime + 6f;
            }
            return false;
        }

        if (!DialogueTable.TryPickBark(groupKey, _groupLastIndex.TryGetValue(groupKey, out int lastIndex) ? lastIndex : -1,
                out DialogueTable.BarkGroup group, out DialogueTable.Entry line, out int index))
            return false;

        if (group.once && _onceUsed.Contains(groupKey))
            return false;

        float now = Time.unscaledTime;
        bool priority = force || group.priority;
        if (_groupLastTime.TryGetValue(groupKey, out float groupLast) && now - groupLast < group.cooldown)
            return false;
        if (!priority)
        {
            if (now - _lastBarkTime < globalCooldown)
                return false;
            if (IntroDialoguePresenter.AnyVisible)
                return false;
        }
        if (group.chance < 1f && Random.value > group.chance)
            return false;

        IntroDialoguePresenter presenter = ResolvePresenter(out Canvas canvas);
        if (presenter == null)
            return false;

        float hold = line.duration > 0f ? line.duration : Mathf.Max(1.2f, DialogueTable.SuggestedDuration(line.text) * 0.8f);
        if (!presenter.ShowOverlayLine(canvas, string.IsNullOrEmpty(line.speaker) ? "EGO" : line.speaker, line.text, hold))
            return false;

        _lastBarkTime = now;
        _groupLastTime[groupKey] = now;
        _groupLastIndex[groupKey] = index;
        if (group.once)
            _onceUsed.Add(groupKey);

        if (!string.IsNullOrEmpty(group.followUpGroup))
        {
            if (_followUpRoutine != null)
                StopCoroutine(_followUpRoutine);
            _followUpRoutine = StartCoroutine(CoFollowUp(group.followUpGroup, hold + Mathf.Max(0f, group.followUpDelay)));
        }

        return true;
    }

    IEnumerator CoFollowUp(string groupKey, float delay)
    {
        yield return new WaitForSecondsRealtime(delay);
        _followUpRoutine = null;
        Play(groupKey, true);
    }

    bool IsPresentationBlocking()
    {
        if (!_sceneActive)
            return true;
        if (Time.timeScale <= 0.001f)
            return true; // 일시정지 메뉴
        if (TsukasaEndingPresenter.IsPlaying)
            return true;
        if (_ultimate != null && _ultimate.IsCinematic)
            return true;

        LobbyManager lobby = LobbyManager.Instance;
        if (lobby != null && lobby.IsDialogueVisible)
            return true;

        for (int i = 0; i < _arrivalControllers.Length; i++)
        {
            MainSceneArrivalController arrival = _arrivalControllers[i];
            if (arrival != null && arrival.isActiveAndEnabled && (arrival.IsIntroSequenceActive || arrival.IsIntroOverlayPlaying))
                return true;
        }

        return false;
    }

    IntroDialoguePresenter ResolvePresenter(out Canvas canvas)
    {
        canvas = null;
        PlayerHUD hud = FindObjectOfType<PlayerHUD>();
        if (hud != null)
            canvas = hud.GetComponentInParent<Canvas>();
        if (canvas != null)
            canvas = canvas.rootCanvas;

        if (canvas == null)
            canvas = EnsureFallbackCanvas();

        return IntroDialoguePresenter.ResolveFor(canvas);
    }

    Canvas EnsureFallbackCanvas()
    {
        if (_fallbackCanvas != null)
            return _fallbackCanvas;

        var go = new GameObject("CombatBarkCanvas", typeof(RectTransform), typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler));
        go.transform.SetParent(transform, false);
        _fallbackCanvas = go.GetComponent<Canvas>();
        _fallbackCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _fallbackCanvas.sortingOrder = 700;
        var scaler = go.GetComponent<UnityEngine.UI.CanvasScaler>();
        scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        return _fallbackCanvas;
    }
}
