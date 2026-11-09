using System;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class PlayerHUD : MonoBehaviour
{
    [Header("HP Fill | Type=Filled")]
    [Tooltip("체력바 Image")]
    public Image hpFillImage;

    [Header("HP Tween")]
    [Tooltip("HP 채움 애니메이션 시간")]
    public float dotweenDuration = 0.3f;

    [Header("Ampoule Slots")]
    [Tooltip("앰플 슬롯 이미지들")]
    public Image[] ampouleSlots = new Image[5];
    [Tooltip("사용한(빈) 앰플 슬롯의 알파. 최대치를 넘는 슬롯은 숨긴다.")]
    [SerializeField, Range(0f, 1f)] float spentAmpouleSlotAlpha = 0.22f;

    [Header("Ultimate Gauge Text")]
    [SerializeField] Text ultimateGaugePercentText;
    [SerializeField] bool showUltimateGaugePercent = true;
    [SerializeField] int ultimateGaugePercentFontSize = 18;
    [SerializeField] Color ultimateGaugePercentTextColor = new Color(0.86f, 1f, 1f, 0.98f);

    [Header("Phase Core (Ultimate)")]
    [Tooltip("기존 % 링 대신 5단 페이즈 코어(PhaseCoreHUD)를 표시")]
    [SerializeField] bool usePhaseCore = true;
    [Tooltip("페이즈 코어 사용 시 중앙 % 텍스트 크기 (작게)")]
    [SerializeField] int phaseCorePercentFontSize = 13;
    [Tooltip("페이즈 코어 사용 시 % 텍스트 세로 오프셋 (PHASE 라벨과 겹치지 않게)")]
    [SerializeField] float phaseCorePercentOffsetY = -5f;

    [Header("Frame")]
    [Tooltip("플레이어 HUD 큰 시안 프레임(UltimateGaugeRoot/BG) 알파. 장면 가독성을 위해 살짝 낮춤 (1 = 원본)")]
    [SerializeField, Range(0f, 1f)] float frameAlpha = 0.68f;

    [Header("Guard Strain")]
    [Tooltip("구형 가드 안정도 바 표시 여부. 밸런스(체간) 게이지는 PlayerBalanceHudBar가 단독 표시하므로 기본 꺼짐")]
    [SerializeField] bool showLegacyGuardStrainBar = false;
    [Tooltip("직렬화된 가드 안정도 Fill 이미지가 있으면 우선 사용")]
    public Image guardStrainFillImage;
    [Tooltip("직렬화된 가드 안정도 Track 이미지가 있으면 우선 사용")]
    public Image guardStrainTrackImage;
    [Tooltip("가드 안정도 바 높이")]
    public float guardStrainBarHeight = 8f;
    [Tooltip("HP 바 기준 세로 오프셋")]
    public float guardStrainYOffset = -14f;
    [Tooltip("가드 안정도 보간 속도")]
    public float guardStrainLerpSpeed = 3.5f;
    [Tooltip("가드 안정도 바 기본 알파")]
    public float guardStrainIdleAlpha = 0f;
    [Tooltip("가드 중/위험 시 바 알파")]
    public float guardStrainActiveAlpha = 0.95f;
    [Tooltip("가드 브레이크 플래시 시간")]
    public float guardBreakFlashDuration = 0.28f;
    public Color guardStrainTrackColor = new Color(0.06f, 0.10f, 0.12f, 0.92f);
    public Color guardStrainLowColor = new Color(0.20f, 0.82f, 0.92f, 0.96f);
    public Color guardStrainHighColor = new Color(1.00f, 0.34f, 0.18f, 1.00f);

    [Header("Combat Telegraph")]
    [SerializeField] private bool showCombatTelegraphText = false;
    [Tooltip("보스 공격 대응 힌트 텍스트")]
    public Text combatTelegraphText;
    [Tooltip("보스 공격 대응 힌트 배경")]
    public Image combatTelegraphPanelImage;
    [Tooltip("HUD 중앙 기준 Y 오프셋")]
    public float combatTelegraphYOffset = -132f;
    [Tooltip("텔레그래프 표시 유지 시간")]
    public float combatTelegraphHoldTime = 0.42f;
    public Color combatTelegraphPanelColor = new Color(0.03f, 0.07f, 0.10f, 0.88f);
    public Color combatTelegraphChainPanelColor = new Color(0.12f, 0.10f, 0.04f, 0.92f);
    public Color combatTelegraphParryColor = new Color(1.00f, 0.80f, 0.32f, 1f);
    public Color combatTelegraphGuardColor = new Color(0.35f, 0.92f, 1.00f, 1f);
    public Color combatTelegraphDodgeColor = new Color(1.00f, 0.40f, 0.40f, 1f);
    public Color combatTelegraphDangerColor = new Color(1.00f, 0.28f, 0.88f, 1f);
    public float combatTelegraphChainPunch = 0.10f;
    [Tooltip("같은 텔레그래프가 연속 요청되면 HUD를 처음부터 다시 띄우지 않고 유지 시간을 연장")]
    public bool mergeDuplicateCombatTelegraphs = true;
    [Tooltip("중복 텔레그래프 병합 허용 시간")]
    public float combatTelegraphDuplicateMergeWindow = 0.55f;

    [Header("Danger Telegraph Feedback")]
    [Tooltip("위험 공격 경고에 사용할 카메라 셰이크")]
    public CameraShake dangerTelegraphCameraShake;
    [Tooltip("위험 공격 경고 카메라 셰이크 강도")]
    public float dangerTelegraphShakeAmplitude = 0.18f;
    [Tooltip("위험 공격 경고 카메라 셰이크 시간")]
    public float dangerTelegraphShakeDuration = 0.22f;
    [Tooltip("위험 공격 경고 HUD 펀치 강도")]
    public float dangerTelegraphHudPunch = 0.18f;
    [Tooltip("위험 공격 경고 사운드용 오디오 소스")]
    public AudioSource dangerTelegraphAudioSource;
    [Tooltip("지정하면 이 클립을 우선 재생")]
    public AudioClip dangerTelegraphClip;
    [Tooltip("클립이 없어도 런타임 합성 경고음을 재생")]
    public bool generateDangerTelegraphTone = true;
    [Range(0f, 1f)] public float dangerTelegraphVolume = 0.85f;
    public float dangerTelegraphToneFrequency = 920f;
    public float dangerTelegraphToneDuration = 0.12f;
    [Tooltip("위험 텔레그래프 피드백 최소 간격")]
    public float dangerTelegraphFeedbackCooldown = 0.18f;

    [Header("Parry Counter")]
    [Tooltip("패링 카운터 준비 텍스트")]
    public Text parryCounterText;
    [Tooltip("패링 카운터 배경")]
    public Image parryCounterPanelImage;
    [Tooltip("패링 카운터 남은 시간 표시")]
    public Image parryCounterFillImage;
    [Tooltip("카운터 배지의 중앙 HUD Y 오프셋")]
    public float parryCounterYOffset = -182f;
    [Tooltip("카운터 배지 유지 알파")]
    public float parryCounterActiveAlpha = 0.96f;
    [Tooltip("카운터 배지 숨김 알파")]
    public float parryCounterIdleAlpha = 0f;
    public Color parryCounterPanelColor = new Color(0.03f, 0.08f, 0.11f, 0.92f);
    public Color parryCounterFillColor = new Color(0.28f, 0.96f, 1.00f, 0.95f);
    public Color parryCounterTextColor = new Color(0.82f, 1.00f, 1.00f, 1.00f);

    [Header("Punish Window")]
    [Tooltip("보스 빈틈 타이머 텍스트")]
    public Text punishWindowText;
    [Tooltip("보스 빈틈 배경")]
    public Image punishWindowPanelImage;
    [Tooltip("보스 빈틈 남은 시간 표시")]
    public Image punishWindowFillImage;
    [Tooltip("빈틈 배지의 중앙 HUD Y 오프셋")]
    public float punishWindowYOffset = -228f;
    [Tooltip("빈틈 배지 유지 알파")]
    public float punishWindowActiveAlpha = 0.96f;
    [Tooltip("빈틈 배지 숨김 알파")]
    public float punishWindowIdleAlpha = 0f;
    public Color punishWindowPanelColor = new Color(0.03f, 0.10f, 0.09f, 0.92f);
    public Color punishWindowFillColor = new Color(0.38f, 1.00f, 0.78f, 0.94f);
    public Color punishWindowTextColor = new Color(0.88f, 1.00f, 0.95f, 1.00f);

    [Header("Performance HUD")]
    [Tooltip("좌상단 FPS 표시")]
    public bool showPerformanceHud = true;
    [Tooltip("좌상단 성능 HUD 토글 키")]
    public KeyCode performanceHudToggleKey = KeyCode.F11;
    [Tooltip("FPS 표시 위치")]
    public Vector2 performanceHudAnchoredPosition = new Vector2(24f, -18f);
    [Tooltip("FPS 갱신 간격")]
    public float performanceHudRefreshInterval = 0.85f;
    [Tooltip("프레임 타임 평활화 강도")]
    [Range(0.01f, 1f)] public float performanceHudSmoothing = 0.10f;
    public Color performanceHudTextColor = new Color(0.80f, 0.98f, 1.00f, 0.96f);
    public Color performanceHudWarningColor = new Color(1.00f, 0.76f, 0.28f, 0.98f);
    public Color performanceHudCriticalColor = new Color(1.00f, 0.38f, 0.30f, 1.00f);

    [Header("HUD Refresh")]
    [Tooltip("상시 갱신되는 HUD 요소의 갱신 간격")]
    public float statusHudRefreshInterval = 1f / 24f;

    IHealth _boundHealth;
    PlayerConsumables _boundConsumables;
    PlayerGuardController _boundGuard;
    BossController _boundBoss;
    PlayerUltimateController _boundUltimate;

    CanvasGroup _guardCanvasGroup;
    RectTransform _guardRoot;
    Tween _guardBreakTween;
    float _displayedGuardStrain;
    CanvasGroup _combatTelegraphCanvasGroup;
    RectTransform _combatTelegraphRoot;
    Tween _combatTelegraphTween;
    float _combatTelegraphShowStartTime;
    float _combatTelegraphFadeOutStartTime;
    float _combatTelegraphHideTime;
    float _combatTelegraphPunchUntilTime;
    float _combatTelegraphPunchStrength;
    bool _combatTelegraphVisible;
    int _combatTelegraphPriority;
    string _combatTelegraphCurrentMessage;
    Color _combatTelegraphCurrentTextColor;
    Color _combatTelegraphCurrentPanelColor;
    float _nextDangerTelegraphFeedbackAllowedTime;
    CanvasGroup _parryCounterCanvasGroup;
    RectTransform _parryCounterRoot;
    Tween _parryCounterTween;
    CanvasGroup _punishWindowCanvasGroup;
    RectTransform _punishWindowRoot;
    AudioClip _runtimeDangerTelegraphTone;
    Text _performanceHudText;
    RectTransform _performanceHudRoot;
    float _smoothedDeltaTime = 1f / 60f;
    float _nextPerformanceHudRefreshTime;
    float _performanceFrameTimeAccum;
    int _performanceFrameCount;
    float _displayedPerformanceFps = 60f;
    float _displayedPerformanceMs = 16.7f;
    string _lastPerformanceHudLabel;
    float _nextStatusHudRefreshTime;
    float _guardBreakFlashUntilTime;
    float _parrySuccessFlashUntilTime;
    float _parryCounterFlashUntilTime;
    float _punishWindowDuration;
    float _punishWindowDamageMultiplier = 1f;
    int _lastUltimateGaugePercent = -1;
    static string[] s_ultimateGaugePercentLabels;

    const string RuntimeUltimateGaugePercentTextName = "_PercentText";
    const string RuntimeGuardRootName = "_RuntimeGuardStrainBar";
    const string RuntimeGuardTrackName = "_Track";
    const string RuntimeGuardFillName = "_Fill";
    const string RuntimeTelegraphRootName = "_RuntimeCombatTelegraph";
    const string RuntimeTelegraphPanelName = "_Panel";
    const string RuntimeTelegraphTextName = "_Text";
    const string RuntimeParryCounterRootName = "_RuntimeParryCounter";
    const string RuntimeParryCounterPanelName = "_Panel";
    const string RuntimeParryCounterFillName = "_Fill";
    const string RuntimeParryCounterTextName = "_Text";
    const string RuntimePunishWindowRootName = "_RuntimePunishWindow";
    const string RuntimePunishWindowPanelName = "_Panel";
    const string RuntimePunishWindowFillName = "_Fill";
    const string RuntimePunishWindowTextName = "_Text";
    const string RuntimePerformanceHudRootName = "_RuntimePerformanceHUD";
    const string RuntimePerformanceHudTextName = "_Text";
    const string RuntimeAmpouleFocusTargetName = "_RuntimeAmpouleFocusTarget";
    static readonly Vector3[] s_ampouleFocusCorners = new Vector3[4];

    void Update()
    {
        if (Debug.isDebugBuild && Input.GetKeyDown(performanceHudToggleKey))
        {
            showPerformanceHud = !showPerformanceHud;
            if (!showPerformanceHud && _performanceHudRoot != null)
                _performanceHudRoot.gameObject.SetActive(false);
        }

        NormalizeRuntimeHudLayout();
        float now = Time.unscaledTime;

        if (ShouldTickStatusHud(now) && now >= _nextStatusHudRefreshTime)
        {
            _nextStatusHudRefreshTime = now + Mathf.Max(1f / 60f, statusHudRefreshInterval);
            UpdateGuardStrainVisual();
            UpdateParryCounterVisual();
            UpdatePunishWindowVisual();
        }

        if (ShouldTickCombatTelegraph())
            UpdateCombatTelegraphVisual();
        else if (!showCombatTelegraphText)
            HideCombatTelegraphText();

        UpdatePerformanceHud();
    }

    public void UpdateHP(int current, int max)
    {
        if (!hpFillImage)
            return;

        float targetFillAmount = max > 0
            ? Mathf.Clamp01((float)current / max)
            : 0f;

        hpFillImage.DOKill();
        hpFillImage.DOFillAmount(targetFillAmount, dotweenDuration)
            .SetEase(Ease.OutSine)
            .SetUpdate(true);

        if (_hudThemeApplied)
        {
            hpFillImage.color = GameplayHudStyler.ResolveHpColor(targetFillAmount);
            UpdateHpDamageChunk(targetFillAmount);
        }
    }

    public void UpdateAmpoule(int count)
    {
        UpdateAmpoule(count, _boundConsumables != null ? _boundConsumables.MaxAmpoule : ampouleSlots.Length);
    }

    // 슬롯 수는 실제 최대 앰플 수(PlayerConsumables.MaxAmpoule)만큼만 표시한다. (프리팹 슬롯은 5개, 기본 최대치는 4)
    // 남은 앰플은 정상 표시, 사용한 앰플은 흐리게 남겨 "몇 개 중 몇 개"가 한눈에 보이게 한다.
    public void UpdateAmpoule(int count, int maxCount)
    {
        int visibleSlots = Mathf.Clamp(maxCount, 0, ampouleSlots.Length);
        int clamped = Mathf.Clamp(count, 0, visibleSlots);

        for (int i = 0; i < ampouleSlots.Length; i++)
        {
            Image slot = ampouleSlots[i];
            if (slot == null)
                continue;

            bool visible = i < visibleSlots;
            if (slot.gameObject.activeSelf != visible)
                slot.gameObject.SetActive(visible);
            slot.enabled = visible;
            slot.canvasRenderer.SetAlpha(i < clamped ? 1f : spentAmpouleSlotAlpha);
        }

        ApplyAmpouleFrameVariant(visibleSlots);
    }

    // 프레임 아트(chuon_hud_player_white)에는 앰플 슬롯 외곽선 5개가 구워져 있다.
    // 최대치가 더 적으면 같은 프레임의 N슬롯 변형(UI/HUD/chuon_hud_player_<N>slot_white)으로 바꿔 빈 외곽선이 보이지 않게 한다.
    const string AmpouleFrameWhiteName = "chuon_hud_player_white";
    const string AmpouleFrameVariantFormat = "UI/HUD/chuon_hud_player_{0}slot_white";
    int _appliedAmpouleFrameSlots = -1;

    void ApplyAmpouleFrameVariant(int slotCount)
    {
        if (slotCount == _appliedAmpouleFrameSlots)
            return;

        Transform frame = transform.Find("UltimateGaugeRoot/BG");
        Image frameImage = frame != null ? frame.GetComponent<Image>() : null;
        if (frameImage == null || frameImage.sprite == null)
            return;

        // 흰 프레임(테마 색 적용 완료) 또는 이미 바꾼 변형일 때만 교체 (원본 시안 스프라이트는 건드리지 않음)
        string current = frameImage.sprite.name;
        bool isWhiteFrame = current == AmpouleFrameWhiteName || (current.StartsWith("chuon_hud_player_") && current.EndsWith("slot_white"));
        if (!isWhiteFrame)
            return;

        Sprite target = slotCount > 0 && slotCount < ampouleSlots.Length
            ? Resources.Load<Sprite>(string.Format(AmpouleFrameVariantFormat, slotCount))
            : null;
        if (target == null)
            target = Resources.Load<Sprite>("UI/HUD/" + AmpouleFrameWhiteName);
        if (target != null && frameImage.sprite != target)
            frameImage.sprite = target;

        _appliedAmpouleFrameSlots = slotCount;
    }

    public void Bind(IHealth health, PlayerConsumables consumables = null, PlayerGuardController guard = null, BossController boss = null, PlayerUltimateController ultimate = null)
    {
        Unbind();
        ApplyFrameAlpha();

        _boundHealth = health;
        if (_boundHealth != null)
        {
            _boundHealth.OnHPChanged += OnHPChanged;
            _boundHealth.OnDied += OnDied;

            if (hpFillImage != null)
            {
                hpFillImage.fillAmount = _boundHealth.MaxHP > 0
                    ? Mathf.Clamp01((float)_boundHealth.CurrentHP / _boundHealth.MaxHP)
                    : 0f;
                if (_hpDamageChunkImage != null)
                {
                    _hpDamageChunkImage.DOKill();
                    _hpDamageChunkImage.fillAmount = hpFillImage.fillAmount;
                }
                if (_hudThemeApplied)
                    hpFillImage.color = GameplayHudStyler.ResolveHpColor(hpFillImage.fillAmount);
            }
        }

        _boundConsumables = consumables;
        if (_boundConsumables != null)
        {
            _boundConsumables.OnAmpouleChanged += OnAmpouleChanged;
            UpdateAmpoule(_boundConsumables.CurrentAmpoule);
        }
        else
        {
            UpdateAmpoule(0, 0);
        }

        BindGuard(guard);
        BindBoss(boss);
        BindUltimate(ultimate != null ? ultimate : GameplaySceneCache.ResolvePlayerUltimateController());
    }

    public void Unbind()
    {
        if (_boundHealth != null)
        {
            _boundHealth.OnHPChanged -= OnHPChanged;
            _boundHealth.OnDied -= OnDied;
            _boundHealth = null;
        }

        if (_boundConsumables != null)
        {
            _boundConsumables.OnAmpouleChanged -= OnAmpouleChanged;
            _boundConsumables = null;
        }

        UnbindGuard();
        UnbindBoss();
        UnbindUltimate();
        hpFillImage?.DOKill();
        if (_hpDamageChunkImage != null)
            _hpDamageChunkImage.DOKill();
    }

    void OnDestroy()
    {
        if (hpFillImage != null)
            hpFillImage.DOKill();
        if (_hpDamageChunkImage != null)
            _hpDamageChunkImage.DOKill();
    }

    public void ShowRuntimeTelegraphMessage(string message, AttackTelegraphType telegraphType = AttackTelegraphType.Auto, float holdTime = 0.72f, bool useDangerFeedback = false, int priority = 0)
    {
        if (!showCombatTelegraphText)
        {
            HideCombatTelegraphText();
            if (useDangerFeedback || telegraphType == AttackTelegraphType.Danger)
                PlayDangerTelegraphFeedback();
            return;
        }

        EnsureCombatTelegraphVisuals();
        if (_combatTelegraphCanvasGroup == null || combatTelegraphText == null || combatTelegraphPanelImage == null)
            return;

        Color textColor = ResolveCombatTelegraphColor(telegraphType);
        Color panelColor = telegraphType == AttackTelegraphType.Danger
            ? Color.Lerp(combatTelegraphPanelColor, combatTelegraphDangerColor, 0.16f)
            : combatTelegraphPanelColor;
        float punchStrength = telegraphType == AttackTelegraphType.Danger
            ? dangerTelegraphHudPunch
            : 0.08f;

        PlayCombatTelegraphMessage(
            string.IsNullOrWhiteSpace(message) ? "NOTICE" : message,
            textColor,
            panelColor,
            Mathf.Max(0.18f, holdTime),
            punchStrength,
            priority);

        if (useDangerFeedback || telegraphType == AttackTelegraphType.Danger)
            PlayDangerTelegraphFeedback();
    }

    public RectTransform GetAmpouleFocusTarget()
    {
        Canvas canvas = null;
        for (int i = 0; i < ampouleSlots.Length; i++)
        {
            if (ampouleSlots[i] != null)
            {
                canvas = ampouleSlots[i].canvas;
                break;
            }
        }

        RectTransform canvasRect = canvas != null ? canvas.transform as RectTransform : null;
        if (canvasRect != null && TryBuildAmpouleFocusProxy(canvas, canvasRect, out RectTransform proxy))
            return proxy;

        for (int i = 0; i < ampouleSlots.Length; i++)
        {
            Image slot = ampouleSlots[i];
            if (slot == null)
                continue;

            return slot.rectTransform;
        }

        return hpFillImage != null ? hpFillImage.rectTransform : null;
    }

    public RectTransform GetUltimateGaugeFocusTarget()
    {
        EnsureUltimateGaugePercentVisual();

        RectTransform parent = ResolveUltimateGaugeTextParent();
        if (parent != null)
            return parent;

        return ultimateGaugePercentText != null ? ultimateGaugePercentText.rectTransform : null;
    }

    bool TryBuildAmpouleFocusProxy(Canvas canvas, RectTransform canvasRect, out RectTransform proxy)
    {
        proxy = canvasRect.Find(RuntimeAmpouleFocusTargetName) as RectTransform;
        if (proxy == null)
        {
            GameObject proxyObject = new GameObject(RuntimeAmpouleFocusTargetName, typeof(RectTransform));
            proxy = proxyObject.GetComponent<RectTransform>();
            proxy.SetParent(canvasRect, false);
        }

        Camera eventCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        Vector2 min = new Vector2(float.MaxValue, float.MaxValue);
        Vector2 max = new Vector2(float.MinValue, float.MinValue);
        bool hasSlot = false;

        for (int i = 0; i < ampouleSlots.Length; i++)
        {
            Image slot = ampouleSlots[i];
            if (slot == null || slot.rectTransform == null || !slot.gameObject.activeSelf)
                continue;

            slot.rectTransform.GetWorldCorners(s_ampouleFocusCorners);
            for (int corner = 0; corner < s_ampouleFocusCorners.Length; corner++)
            {
                Vector2 screen = RectTransformUtility.WorldToScreenPoint(eventCamera, s_ampouleFocusCorners[corner]);
                if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen, eventCamera, out Vector2 local))
                    continue;

                min = Vector2.Min(min, local);
                max = Vector2.Max(max, local);
                hasSlot = true;
            }
        }

        if (!hasSlot || max.x <= min.x || max.y <= min.y)
            return false;

        proxy.anchorMin = proxy.anchorMax = proxy.pivot = new Vector2(0.5f, 0.5f);
        proxy.anchoredPosition = (min + max) * 0.5f;
        proxy.sizeDelta = max - min;
        proxy.localScale = Vector3.one;
        return true;
    }

    void BindGuard(PlayerGuardController guard)
    {
        _boundGuard = guard;
        EnsureGuardStrainVisuals();
        EnsureParryCounterVisuals();

        if (_boundGuard == null)
            return;

        if (guardStrainFillImage != null)
        {
            _displayedGuardStrain = _boundGuard.GuardStrainNormalized;
            ApplyGuardStrainVisual(_displayedGuardStrain, true);
        }

        if (_boundGuard.OnGuardBreak != null)
            _boundGuard.OnGuardBreak.AddListener(HandleGuardBreak);

        if (_boundGuard.OnParrySuccess != null)
            _boundGuard.OnParrySuccess.AddListener(HandleParrySuccess);

        if (_parryCounterCanvasGroup != null)
            _parryCounterCanvasGroup.alpha = _boundGuard.IsParryCounterReady ? parryCounterActiveAlpha : parryCounterIdleAlpha;
    }

    void UnbindGuard()
    {
        if (_boundGuard != null)
        {
            if (_boundGuard.OnGuardBreak != null)
                _boundGuard.OnGuardBreak.RemoveListener(HandleGuardBreak);

            if (_boundGuard.OnParrySuccess != null)
                _boundGuard.OnParrySuccess.RemoveListener(HandleParrySuccess);
        }

        _boundGuard = null;

        if (_guardBreakTween != null)
        {
            _guardBreakTween.Kill();
            _guardBreakTween = null;
        }

        if (_parryCounterTween != null)
        {
            _parryCounterTween.Kill();
            _parryCounterTween = null;
        }

        if (_parryCounterCanvasGroup != null)
            _parryCounterCanvasGroup.alpha = parryCounterIdleAlpha;
    }

    void BindBoss(BossController boss)
    {
        _boundBoss = boss;
        EnsureCombatTelegraphVisuals();
        EnsurePunishWindowVisuals();
        EnsureDangerTelegraphFeedback();

        if (_boundBoss == null)
            return;

        _boundBoss.OnAttackTelegraph += HandleAttackTelegraph;
        _boundBoss.OnPunishWindowOpened += HandlePunishWindowOpened;
        _boundBoss.OnPunishWindowClosed += HandlePunishWindowClosed;
        _boundBoss.OnBossPhaseChanged += HandleBossPhaseChanged;
    }

    void UnbindBoss()
    {
        if (_boundBoss != null)
        {
            _boundBoss.OnAttackTelegraph -= HandleAttackTelegraph;
            _boundBoss.OnPunishWindowOpened -= HandlePunishWindowOpened;
            _boundBoss.OnPunishWindowClosed -= HandlePunishWindowClosed;
            _boundBoss.OnBossPhaseChanged -= HandleBossPhaseChanged;
        }

        _boundBoss = null;

        if (_combatTelegraphTween != null)
        {
            _combatTelegraphTween.Kill();
            _combatTelegraphTween = null;
        }

        _combatTelegraphVisible = false;
        _combatTelegraphShowStartTime = 0f;
        _combatTelegraphFadeOutStartTime = 0f;
        _combatTelegraphHideTime = 0f;
        _combatTelegraphPunchUntilTime = 0f;
        _combatTelegraphPunchStrength = 0f;
        _combatTelegraphPriority = 0;
        _combatTelegraphCurrentMessage = null;
        _nextDangerTelegraphFeedbackAllowedTime = 0f;
        _punishWindowDuration = 0f;
        _punishWindowDamageMultiplier = 1f;

        if (_combatTelegraphCanvasGroup != null)
            _combatTelegraphCanvasGroup.alpha = 0f;

        if (_punishWindowCanvasGroup != null)
            _punishWindowCanvasGroup.alpha = punishWindowIdleAlpha;

        if (_punishWindowRoot != null)
            _punishWindowRoot.localScale = Vector3.one;
    }

    void BindUltimate(PlayerUltimateController ultimate)
    {
        EnsureUltimateGaugePercentVisual();
        _boundUltimate = ultimate;
        EnsurePhaseCore();
        if (_phaseCore != null)
            _phaseCore.Bind(ultimate);

        if (_boundUltimate == null)
        {
            SetUltimateGaugePercent(0f);
            return;
        }

        _boundUltimate.OnGaugeChanged += HandleUltimateGaugeChanged;
        HandleUltimateGaugeChanged(
            _boundUltimate.Gauge,
            _boundUltimate.gaugeMax > 0f ? Mathf.Clamp01(_boundUltimate.Gauge / _boundUltimate.gaugeMax) : 0f,
            _boundUltimate.IsGaugeReady);
    }

    void UnbindUltimate()
    {
        if (_boundUltimate != null)
        {
            _boundUltimate.OnGaugeChanged -= HandleUltimateGaugeChanged;
            _boundUltimate = null;
        }

        if (_phaseCore != null)
            _phaseCore.Unbind();

        _lastUltimateGaugePercent = -1;
    }

    // ---- 페이즈 코어: 기존 링(UltimateGaugeRoot/Fill) 자리에 생성하고 링 채움은 숨김 ----
    PhaseCoreHUD _phaseCore;

    public PhaseCoreHUD PhaseCore => _phaseCore;

    void EnsurePhaseCore()
    {
        if (!usePhaseCore)
            return;

        if (_phaseCore != null)
        {
            _phaseCore.SetPercentText(showUltimateGaugePercent ? ultimateGaugePercentText : null);
            return;
        }

        RectTransform ringRect = transform.Find("UltimateGaugeRoot/Fill") as RectTransform;
        if (ringRect == null)
            ringRect = ResolveUltimateGaugeTextParent();
        if (ringRect == null)
            return;

        _phaseCore = PhaseCoreHUD.CreateUnder(ringRect, showUltimateGaugePercent ? ultimateGaugePercentText : null);

        Image ringImage = ringRect.GetComponent<Image>();
        if (ringImage != null)
            ringImage.enabled = false;
    }

    void HandleUltimateGaugeChanged(float gauge, float normalized, bool ready)
    {
        SetUltimateGaugePercent(normalized);
        ApplyUltimateGaugeColor(normalized, ready);
    }

    void SetUltimateGaugePercent(float normalized)
    {
        EnsureUltimateGaugePercentVisual();
        if (ultimateGaugePercentText == null)
            return;

        int percent = Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(normalized) * 100f), 0, 100);
        if (percent == _lastUltimateGaugePercent)
            return;

        _lastUltimateGaugePercent = percent;
        ultimateGaugePercentText.text = GetUltimateGaugePercentLabel(percent);
    }

    void UpdateGuardStrainVisual()
    {
        if (_boundGuard == null || guardStrainFillImage == null || _guardCanvasGroup == null)
            return;

        float target = _boundGuard.GuardStrainNormalized;
        _displayedGuardStrain = Mathf.MoveTowards(
            _displayedGuardStrain,
            target,
            Mathf.Max(0.01f, guardStrainLerpSpeed) * Time.unscaledDeltaTime);

        ApplyGuardStrainVisual(_displayedGuardStrain, false);
    }

    void ApplyGuardStrainVisual(float normalized, bool immediate)
    {
        if (guardStrainFillImage == null || _guardCanvasGroup == null)
            return;

        float now = Time.unscaledTime;
        Color baseColor = Color.Lerp(guardStrainLowColor, guardStrainHighColor, normalized);
        float scale = 1f;

        if (now < _guardBreakFlashUntilTime)
        {
            float duration = Mathf.Max(0.06f, guardBreakFlashDuration);
            float t = 1f - ((_guardBreakFlashUntilTime - now) / duration);
            float wave = Mathf.Sin(Mathf.Clamp01(t) * Mathf.PI * 5f) * (1f - Mathf.Clamp01(t));
            scale += wave * 0.08f;
            baseColor = Color.Lerp(baseColor, Color.white, 0.55f);
        }
        else if (now < _parrySuccessFlashUntilTime)
        {
            float remaining = _parrySuccessFlashUntilTime - now;
            float t = 1f - (remaining / 0.22f);
            baseColor = Color.Lerp(new Color(0.58f, 1f, 1f, 1f), baseColor, Mathf.Clamp01(t));
        }

        guardStrainFillImage.fillAmount = normalized;
        guardStrainFillImage.color = baseColor;

        bool shouldShow = normalized > 0.001f
            || (_boundGuard != null && (_boundGuard.IsGuarding || _boundGuard.IsGuardBroken));

        float targetAlpha = shouldShow ? guardStrainActiveAlpha : guardStrainIdleAlpha;
        float alphaStep = immediate ? 1f : Time.unscaledDeltaTime * 8f;
        _guardCanvasGroup.alpha = Mathf.MoveTowards(_guardCanvasGroup.alpha, targetAlpha, alphaStep);

        if (_guardRoot != null)
            _guardRoot.localScale = new Vector3(scale, scale, 1f);
    }

    void UpdateParryCounterVisual()
    {
        if (_boundGuard == null || _parryCounterCanvasGroup == null || parryCounterFillImage == null || parryCounterText == null)
            return;

        bool active = _boundGuard.IsParryCounterReady;
        float duration = Mathf.Max(0.05f, _boundGuard.parryCounterWindow);
        float remaining = _boundGuard.ParryCounterRemaining;
        float normalized = active ? Mathf.Clamp01(remaining / duration) : 0f;
        float targetAlpha = active ? parryCounterActiveAlpha : parryCounterIdleAlpha;

        _parryCounterCanvasGroup.alpha = Mathf.MoveTowards(
            _parryCounterCanvasGroup.alpha,
            targetAlpha,
            Time.unscaledDeltaTime * 10f);

        parryCounterFillImage.fillAmount = normalized;

        if (parryCounterPanelImage != null)
            parryCounterPanelImage.color = parryCounterPanelColor;

        if (active)
        {
            float pulse = 0.65f + (Mathf.Sin(Time.unscaledTime * 10f) * 0.35f);
            parryCounterFillImage.color = Color.Lerp(parryCounterFillColor * 0.72f, Color.white, pulse * 0.28f);
            parryCounterText.color = Color.Lerp(parryCounterTextColor, Color.white, pulse * 0.22f);
            parryCounterText.text = "COUNTER READY";
        }
        else
        {
            parryCounterFillImage.color = parryCounterFillColor;
            parryCounterText.color = parryCounterTextColor;
            parryCounterText.text = "COUNTER READY";
        }

        if (_parryCounterRoot != null)
        {
            float scale = 1f;
            float remainingFlash = _parryCounterFlashUntilTime - Time.unscaledTime;
            if (remainingFlash > 0f)
            {
                float t = 1f - (remainingFlash / 0.20f);
                scale = Mathf.LerpUnclamped(1.05f, 1f, EaseOutBack(Mathf.Clamp01(t)));
            }

            _parryCounterRoot.localScale = new Vector3(scale, scale, 1f);
        }
    }

    void UpdatePunishWindowVisual()
    {
        if (_boundBoss == null || _punishWindowCanvasGroup == null || punishWindowFillImage == null || punishWindowText == null)
            return;

        bool active = _boundBoss.IsPunishWindowActive;
        float remaining = active ? _boundBoss.PunishWindowRemaining : 0f;
        float duration = Mathf.Max(0.05f, _punishWindowDuration > 0.01f ? _punishWindowDuration : remaining);
        float normalized = active ? Mathf.Clamp01(remaining / duration) : 0f;
        float targetAlpha = active ? punishWindowActiveAlpha : punishWindowIdleAlpha;

        _punishWindowCanvasGroup.alpha = Mathf.MoveTowards(
            _punishWindowCanvasGroup.alpha,
            targetAlpha,
            Time.unscaledDeltaTime * 12f);

        punishWindowFillImage.fillAmount = normalized;

        float pulse = active ? 0.65f + Mathf.Sin(Time.unscaledTime * 12f) * 0.35f : 0f;
        punishWindowFillImage.color = active
            ? Color.Lerp(punishWindowFillColor * 0.74f, Color.white, pulse * 0.24f)
            : punishWindowFillColor;
        punishWindowText.color = active
            ? Color.Lerp(punishWindowTextColor, Color.white, pulse * 0.12f)
            : punishWindowTextColor;
        punishWindowText.text = _punishWindowDamageMultiplier > 1.01f
            ? $"PUNISH x{_punishWindowDamageMultiplier:0.00}"
            : "PUNISH";

        if (punishWindowPanelImage != null)
            punishWindowPanelImage.color = punishWindowPanelColor;

        if (_punishWindowRoot != null)
        {
            float scale = 1f;
            if (active)
                scale += (1f - normalized) * 0.035f;

            _punishWindowRoot.localScale = new Vector3(scale, scale, 1f);
        }
    }

    void HandleGuardBreak()
    {
        if (_guardRoot == null || guardStrainFillImage == null)
            return;

        if (_guardBreakTween != null)
            _guardBreakTween.Kill();

        _guardCanvasGroup.alpha = 1f;
        _guardRoot.localScale = Vector3.one;
        _guardBreakFlashUntilTime = Time.unscaledTime + Mathf.Max(0.06f, guardBreakFlashDuration);
        _parrySuccessFlashUntilTime = 0f;
        _guardBreakTween = null;
    }

    void HandleParrySuccess()
    {
        if (guardStrainFillImage != null)
        {
            _parrySuccessFlashUntilTime = Time.unscaledTime + 0.22f;
            _guardBreakFlashUntilTime = 0f;
            _guardBreakTween = null;
        }

        if (_boundGuard != null && _boundGuard.IsParryCounterReady)
            FlashParryCounterVisual();
    }

    void HandleAttackTelegraph(AttackTelegraphType telegraphType, float leadTime, string label)
    {
        if (!showCombatTelegraphText)
        {
            HideCombatTelegraphText();
            if (telegraphType == AttackTelegraphType.Danger)
                PlayDangerTelegraphFeedback();
            return;
        }

        EnsureCombatTelegraphVisuals();
        if (_combatTelegraphCanvasGroup == null || combatTelegraphText == null || combatTelegraphPanelImage == null)
            return;

        bool isChain = !string.IsNullOrWhiteSpace(label) &&
            label.StartsWith("CHAIN", StringComparison.OrdinalIgnoreCase);
        Color textColor = ResolveCombatTelegraphColor(telegraphType);
        if (isChain)
            textColor = Color.Lerp(textColor, Color.white, 0.18f);

        float holdTime = Mathf.Max(combatTelegraphHoldTime, leadTime + (isChain ? 0.18f : 0.12f));
        float punchStrength = telegraphType == AttackTelegraphType.Danger
            ? dangerTelegraphHudPunch
            : (isChain ? combatTelegraphChainPunch : 0f);

        PlayCombatTelegraphMessage(
            string.IsNullOrWhiteSpace(label) ? "DODGE" : label,
            textColor,
            isChain ? combatTelegraphChainPanelColor : combatTelegraphPanelColor,
            holdTime,
            punchStrength,
            0);

        if (telegraphType == AttackTelegraphType.Danger)
            PlayDangerTelegraphFeedback();
    }

    void HandlePunishWindowOpened(float duration, float damageMultiplier, string patternName)
    {
        if (!showCombatTelegraphText)
        {
            HideCombatTelegraphText();
            return;
        }

        EnsureCombatTelegraphVisuals();
        EnsurePunishWindowVisuals();
        if (_combatTelegraphCanvasGroup == null || combatTelegraphText == null || combatTelegraphPanelImage == null)
            return;

        _punishWindowDuration = Mathf.Max(0.05f, duration);
        _punishWindowDamageMultiplier = Mathf.Max(1f, damageMultiplier);

        string message = _punishWindowDamageMultiplier > 1.01f
            ? $"PUNISH x{_punishWindowDamageMultiplier:0.00}"
            : "PUNISH";

        PlayCombatTelegraphMessage(
            message,
            new Color(0.70f, 1.00f, 0.86f, 1f),
            Color.Lerp(combatTelegraphPanelColor, punishWindowFillColor, 0.16f),
            Mathf.Max(0.26f, duration * 0.46f),
            0f,
            0);
    }

    void HandlePunishWindowClosed()
    {
        if (!showCombatTelegraphText)
        {
            HideCombatTelegraphText();
            return;
        }

        if (_combatTelegraphCanvasGroup == null || combatTelegraphText == null)
            return;

        if (!combatTelegraphText.text.StartsWith("OPENING"))
            return;

        if (_combatTelegraphTween != null)
        {
            _combatTelegraphTween.Kill();
            _combatTelegraphTween = null;
        }

        float now = Time.unscaledTime;
        _combatTelegraphFadeOutStartTime = now;
        _combatTelegraphHideTime = now + 0.10f;
        _combatTelegraphVisible = true;
        _punishWindowDuration = 0f;
        _punishWindowDamageMultiplier = 1f;
    }

    void HandleBossPhaseChanged(int phase, float hpNormalized)
    {
        if (!showCombatTelegraphText)
        {
            HideCombatTelegraphText();
            if (phase >= 3)
                PlayDangerTelegraphFeedback();
            return;
        }

        EnsureCombatTelegraphVisuals();
        if (_combatTelegraphCanvasGroup == null || combatTelegraphText == null || combatTelegraphPanelImage == null)
            return;

        string message = phase >= 3 ? "BERSERK" : $"PHASE {Mathf.Max(1, phase)}";
        Color textColor = phase >= 3
            ? combatTelegraphDangerColor
            : Color.Lerp(combatTelegraphDodgeColor, combatTelegraphGuardColor, 0.42f);
        Color panelColor = phase >= 3
            ? Color.Lerp(combatTelegraphPanelColor, combatTelegraphDangerColor, 0.16f)
            : Color.Lerp(combatTelegraphPanelColor, combatTelegraphChainPanelColor, 0.58f);
        float punchStrength = phase >= 3
            ? dangerTelegraphHudPunch
            : Mathf.Max(combatTelegraphChainPunch, 0.08f);

        PlayCombatTelegraphMessage(message, textColor, panelColor, 0.72f, punchStrength, 0);

        if (phase >= 3)
            PlayDangerTelegraphFeedback();
    }

    void PlayCombatTelegraphMessage(string message, Color textColor, Color panelColor, float holdTime, float punchStrength, int priority = 0)
    {
        string normalizedMessage = string.IsNullOrWhiteSpace(message) ? "NOTICE" : message;
        if (_combatTelegraphVisible && priority < _combatTelegraphPriority)
            return;

        if (TryRefreshCombatTelegraph(normalizedMessage, textColor, panelColor, holdTime, punchStrength, priority))
            return;

        combatTelegraphText.text = normalizedMessage;
        combatTelegraphText.color = textColor;
        combatTelegraphPanelImage.color = panelColor;

        if (_combatTelegraphTween != null)
        {
            _combatTelegraphTween.Kill();
            _combatTelegraphTween = null;
        }

        _combatTelegraphRoot.gameObject.SetActive(true);
        _combatTelegraphRoot.DOKill();
        _combatTelegraphCanvasGroup.alpha = 0f;
        _combatTelegraphRoot.localScale = new Vector3(0.96f, 0.96f, 1f);

        float now = Time.unscaledTime;
        _combatTelegraphShowStartTime = now;
        _combatTelegraphFadeOutStartTime = now + Mathf.Max(0.06f, holdTime);
        _combatTelegraphHideTime = _combatTelegraphFadeOutStartTime + 0.18f;
        _combatTelegraphPunchUntilTime = punchStrength > 0.001f ? now + 0.26f : now + 0.10f;
        _combatTelegraphPunchStrength = Mathf.Max(0f, punchStrength);
        _combatTelegraphVisible = true;
        _combatTelegraphPriority = priority;
        _combatTelegraphCurrentMessage = normalizedMessage;
        _combatTelegraphCurrentTextColor = textColor;
        _combatTelegraphCurrentPanelColor = panelColor;
    }

    bool TryRefreshCombatTelegraph(string message, Color textColor, Color panelColor, float holdTime, float punchStrength, int priority)
    {
        if (!mergeDuplicateCombatTelegraphs || !_combatTelegraphVisible)
            return false;

        if (priority != _combatTelegraphPriority)
            return false;

        if (!string.Equals(_combatTelegraphCurrentMessage, message, StringComparison.Ordinal))
            return false;

        if (!Approximately(_combatTelegraphCurrentTextColor, textColor) || !Approximately(_combatTelegraphCurrentPanelColor, panelColor))
            return false;

        float now = Time.unscaledTime;
        if (now - _combatTelegraphShowStartTime > Mathf.Max(0.05f, combatTelegraphDuplicateMergeWindow))
            return false;

        _combatTelegraphFadeOutStartTime = Mathf.Max(_combatTelegraphFadeOutStartTime, now + Mathf.Max(0.06f, holdTime));
        _combatTelegraphHideTime = _combatTelegraphFadeOutStartTime + 0.18f;

        if (punchStrength > _combatTelegraphPunchStrength + 0.001f)
        {
            _combatTelegraphPunchStrength = punchStrength;
            _combatTelegraphPunchUntilTime = now + (punchStrength > 0.001f ? 0.22f : 0.10f);
        }

        return true;
    }

    void UpdateCombatTelegraphVisual()
    {
        if (!showCombatTelegraphText)
        {
            HideCombatTelegraphText();
            return;
        }

        if (_combatTelegraphCanvasGroup == null || _combatTelegraphRoot == null)
            return;

        if (!_combatTelegraphVisible)
        {
            if (_combatTelegraphCanvasGroup.alpha > 0.001f)
                _combatTelegraphCanvasGroup.alpha = 0f;

            if (_combatTelegraphRoot.localScale != Vector3.one)
                _combatTelegraphRoot.localScale = Vector3.one;

            return;
        }

        float now = Time.unscaledTime;
        if (now >= _combatTelegraphHideTime)
        {
            _combatTelegraphVisible = false;
            _combatTelegraphPriority = 0;
            _combatTelegraphCanvasGroup.alpha = 0f;
            _combatTelegraphRoot.localScale = Vector3.one;
            return;
        }

        float fadeInDuration = 0.08f;
        float fadeOutDuration = Mathf.Max(0.10f, _combatTelegraphHideTime - _combatTelegraphFadeOutStartTime);
        float alpha = now < _combatTelegraphFadeOutStartTime
            ? Mathf.Clamp01((now - _combatTelegraphShowStartTime) / fadeInDuration)
            : 1f - Mathf.Clamp01((now - _combatTelegraphFadeOutStartTime) / fadeOutDuration);
        _combatTelegraphCanvasGroup.alpha = alpha;

        float introT = Mathf.Clamp01((now - _combatTelegraphShowStartTime) / 0.10f);
        float scale = Mathf.LerpUnclamped(0.96f, 1f, EaseOutBack(introT));
        if (now < _combatTelegraphPunchUntilTime && _combatTelegraphPunchStrength > 0.001f)
        {
            float punchT = Mathf.Clamp01((now - _combatTelegraphShowStartTime) / Mathf.Max(0.06f, _combatTelegraphPunchUntilTime - _combatTelegraphShowStartTime));
            float punchWave = Mathf.Sin(punchT * Mathf.PI * 5f) * (1f - punchT);
            scale += punchWave * (_combatTelegraphPunchStrength * 0.24f);
        }

        _combatTelegraphRoot.localScale = new Vector3(scale, scale, 1f);
    }

    void HideCombatTelegraphText()
    {
        _combatTelegraphVisible = false;
        _combatTelegraphPriority = 0;
        _combatTelegraphCurrentMessage = null;
        _combatTelegraphShowStartTime = 0f;
        _combatTelegraphFadeOutStartTime = 0f;
        _combatTelegraphHideTime = 0f;
        _combatTelegraphPunchUntilTime = 0f;
        _combatTelegraphPunchStrength = 0f;

        if (_combatTelegraphTween != null)
        {
            _combatTelegraphTween.Kill();
            _combatTelegraphTween = null;
        }

        if (_combatTelegraphRoot != null)
        {
            _combatTelegraphRoot.DOKill();
            _combatTelegraphRoot.localScale = Vector3.one;
            _combatTelegraphRoot.gameObject.SetActive(false);
        }

        if (_combatTelegraphCanvasGroup != null)
            _combatTelegraphCanvasGroup.alpha = 0f;
    }

    void NormalizeRuntimeHudLayout()
    {
        combatTelegraphYOffset = -132f;
        parryCounterYOffset = -182f;
        punishWindowYOffset = -228f;
        performanceHudAnchoredPosition = new Vector2(24f, -18f);
    }

    bool ShouldTickStatusHud(float now)
    {
        bool hasGuard = _boundGuard != null;
        bool hasPunish = _boundBoss != null && _boundBoss.IsPunishWindowActive;

        if (now < _guardBreakFlashUntilTime || now < _parrySuccessFlashUntilTime || now < _parryCounterFlashUntilTime)
            return true;

        if (hasGuard && (_boundGuard.IsGuarding || _boundGuard.IsGuardBroken || _boundGuard.IsParryCounterReady))
            return true;

        if (hasPunish)
            return true;

        if (_displayedGuardStrain > 0.001f)
            return true;

        if (_guardCanvasGroup != null && _guardCanvasGroup.alpha > guardStrainIdleAlpha + 0.001f)
            return true;

        if (_parryCounterCanvasGroup != null && _parryCounterCanvasGroup.alpha > parryCounterIdleAlpha + 0.001f)
            return true;

        if (_punishWindowCanvasGroup != null && _punishWindowCanvasGroup.alpha > punishWindowIdleAlpha + 0.001f)
            return true;

        return false;
    }

    bool ShouldTickCombatTelegraph()
    {
        if (_combatTelegraphVisible)
            return true;

        if (_combatTelegraphCanvasGroup != null && _combatTelegraphCanvasGroup.alpha > 0.001f)
            return true;

        if (_combatTelegraphRoot != null && _combatTelegraphRoot.localScale != Vector3.one)
            return true;

        return false;
    }

    static float EaseOutBack(float t)
    {
        t = Mathf.Clamp01(t);
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;
        float x = t - 1f;
        return 1f + c3 * x * x * x + c1 * x * x;
    }

    void EnsureUltimateGaugePercentVisual()
    {
        if (!showUltimateGaugePercent)
        {
            if (ultimateGaugePercentText != null)
                ultimateGaugePercentText.gameObject.SetActive(false);
            return;
        }

        if (ultimateGaugePercentText == null)
        {
            RectTransform parent = ResolveUltimateGaugeTextParent();
            if (parent == null)
                return;

            RectTransform textRect = parent.Find(RuntimeUltimateGaugePercentTextName) as RectTransform;
            if (textRect == null)
            {
                GameObject textGo = new GameObject(RuntimeUltimateGaugePercentTextName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
                textRect = textGo.GetComponent<RectTransform>();
                textRect.SetParent(parent, false);
            }

            ultimateGaugePercentText = textRect.GetComponent<Text>();
            if (ultimateGaugePercentText == null)
                ultimateGaugePercentText = textRect.gameObject.AddComponent<Text>();
        }

        RectTransform rect = ultimateGaugePercentText.rectTransform;
        StretchToParent(rect);
        rect.SetAsLastSibling();

        ultimateGaugePercentText.alignment = TextAnchor.MiddleCenter;
        ultimateGaugePercentText.font = ResolveRuntimeFont();
        ultimateGaugePercentText.fontSize = Mathf.Max(10, ultimateGaugePercentFontSize);
        ultimateGaugePercentText.fontStyle = FontStyle.Bold;
        ultimateGaugePercentText.horizontalOverflow = HorizontalWrapMode.Overflow;
        ultimateGaugePercentText.verticalOverflow = VerticalWrapMode.Overflow;
        ultimateGaugePercentText.color = ultimateGaugePercentTextColor;
        ultimateGaugePercentText.raycastTarget = false;
        ultimateGaugePercentText.gameObject.SetActive(true);

        if (usePhaseCore)
        {
            // 페이즈 코어 중앙의 작은 % 표시
            ultimateGaugePercentText.fontSize = Mathf.Max(8, phaseCorePercentFontSize);
            rect.offsetMin = new Vector2(0f, phaseCorePercentOffsetY);
            rect.offsetMax = new Vector2(0f, phaseCorePercentOffsetY);
        }
    }

    RectTransform ResolveUltimateGaugeTextParent()
    {
        UI_UltimateGauge[] gauges = FindObjectsOfType<UI_UltimateGauge>(true);
        Canvas hudCanvas = hpFillImage != null ? hpFillImage.canvas : null;
        RectTransform fallback = null;

        for (int i = 0; i < gauges.Length; i++)
        {
            Image fill = gauges[i] != null ? gauges[i].fill : null;
            RectTransform fillRect = fill != null ? fill.rectTransform : null;
            if (fillRect == null)
                continue;

            if (fallback == null)
                fallback = fillRect;

            if (hudCanvas == null || fill.canvas == hudCanvas)
                return fillRect;
        }

        return fallback;
    }

    void EnsureGuardStrainVisuals()
    {
        if (!showLegacyGuardStrainBar)
        {
            DisableLegacyGuardStrainVisuals();
            return;
        }

        if (hpFillImage == null)
            return;

        if (guardStrainFillImage == null || guardStrainTrackImage == null)
            CreateRuntimeGuardStrainBar();

        if (_guardRoot == null && guardStrainTrackImage != null)
            _guardRoot = guardStrainTrackImage.rectTransform.parent as RectTransform;

        if (_guardRoot != null)
        {
            _guardCanvasGroup = _guardRoot.GetComponent<CanvasGroup>();
            if (_guardCanvasGroup == null)
                _guardCanvasGroup = _guardRoot.gameObject.AddComponent<CanvasGroup>();
        }
    }

    void EnsureCombatTelegraphVisuals()
    {
        if (hpFillImage == null)
            return;

        RectTransform parent = hpFillImage.canvas != null
            ? hpFillImage.canvas.transform as RectTransform
            : hpFillImage.transform.root as RectTransform;

        if (parent == null)
            return;

        RectTransform root = parent.Find(RuntimeTelegraphRootName) as RectTransform;
        if (root == null)
        {
            GameObject rootGo = new GameObject(RuntimeTelegraphRootName, typeof(RectTransform), typeof(CanvasRenderer), typeof(CanvasGroup));
            root = rootGo.GetComponent<RectTransform>();
            root.SetParent(parent, false);
        }

        _combatTelegraphRoot = root;
        root.anchorMin = new Vector2(0.5f, 0.5f);
        root.anchorMax = new Vector2(0.5f, 0.5f);
        root.pivot = new Vector2(0.5f, 0.5f);
        root.anchoredPosition = new Vector2(0f, combatTelegraphYOffset);
        root.sizeDelta = new Vector2(376f, 60f);
        root.localScale = Vector3.one;

        Image panel = FindOrCreateChildImage(root, RuntimeTelegraphPanelName);
        StretchToParent(panel.rectTransform);
        panel.sprite = ResolveRuntimeSprite();
        panel.type = Image.Type.Sliced;
        panel.color = combatTelegraphPanelColor;
        panel.raycastTarget = false;
        combatTelegraphPanelImage = panel;

        RectTransform textRect = root.Find(RuntimeTelegraphTextName) as RectTransform;
        if (textRect == null)
        {
            GameObject textGo = new GameObject(RuntimeTelegraphTextName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            textRect = textGo.GetComponent<RectTransform>();
            textRect.SetParent(root, false);
        }

        StretchToParent(textRect);
        Text text = textRect.GetComponent<Text>();
        if (text == null)
            text = textRect.gameObject.AddComponent<Text>();

        text.alignment = TextAnchor.MiddleCenter;
        text.font = ResolveRuntimeFont();
        text.fontSize = 24;
        text.fontStyle = FontStyle.Bold;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
        text.text = string.Empty;
        combatTelegraphText = text;

        _combatTelegraphCanvasGroup = root.GetComponent<CanvasGroup>();
        if (_combatTelegraphCanvasGroup == null)
            _combatTelegraphCanvasGroup = root.gameObject.AddComponent<CanvasGroup>();

        if (_combatTelegraphTween == null)
            _combatTelegraphCanvasGroup.alpha = 0f;
    }

    void EnsureParryCounterVisuals()
    {
        if (hpFillImage == null)
            return;

        RectTransform parent = hpFillImage.canvas != null
            ? hpFillImage.canvas.transform as RectTransform
            : hpFillImage.transform.root as RectTransform;

        if (parent == null)
            return;

        RectTransform root = parent.Find(RuntimeParryCounterRootName) as RectTransform;
        if (root == null)
        {
            GameObject rootGo = new GameObject(RuntimeParryCounterRootName, typeof(RectTransform), typeof(CanvasRenderer), typeof(CanvasGroup));
            root = rootGo.GetComponent<RectTransform>();
            root.SetParent(parent, false);
        }

        _parryCounterRoot = root;
        root.anchorMin = new Vector2(0.5f, 0.5f);
        root.anchorMax = new Vector2(0.5f, 0.5f);
        root.pivot = new Vector2(0.5f, 0.5f);
        root.anchoredPosition = new Vector2(0f, parryCounterYOffset);
        root.sizeDelta = new Vector2(276f, 38f);
        root.localScale = Vector3.one;

        Image panel = FindOrCreateChildImage(root, RuntimeParryCounterPanelName);
        StretchToParent(panel.rectTransform);
        panel.sprite = ResolveRuntimeSprite();
        panel.type = Image.Type.Sliced;
        panel.color = parryCounterPanelColor;
        panel.raycastTarget = false;
        parryCounterPanelImage = panel;

        Image fill = FindOrCreateChildImage(root, RuntimeParryCounterFillName);
        StretchToParent(fill.rectTransform);
        fill.sprite = ResolveRuntimeSprite();
        fill.type = Image.Type.Filled;
        fill.fillMethod = Image.FillMethod.Horizontal;
        fill.fillOrigin = (int)Image.OriginHorizontal.Left;
        fill.fillClockwise = true;
        fill.color = parryCounterFillColor;
        fill.raycastTarget = false;
        parryCounterFillImage = fill;

        RectTransform textRect = root.Find(RuntimeParryCounterTextName) as RectTransform;
        if (textRect == null)
        {
            GameObject textGo = new GameObject(RuntimeParryCounterTextName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            textRect = textGo.GetComponent<RectTransform>();
            textRect.SetParent(root, false);
        }

        StretchToParent(textRect);
        Text text = textRect.GetComponent<Text>();
        if (text == null)
            text = textRect.gameObject.AddComponent<Text>();

        text.alignment = TextAnchor.MiddleCenter;
        text.font = ResolveRuntimeFont();
        text.fontSize = 18;
        text.fontStyle = FontStyle.Bold;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
        text.text = "COUNTER READY";
        text.color = parryCounterTextColor;
        parryCounterText = text;

        _parryCounterCanvasGroup = root.GetComponent<CanvasGroup>();
        if (_parryCounterCanvasGroup == null)
            _parryCounterCanvasGroup = root.gameObject.AddComponent<CanvasGroup>();

        if (_parryCounterTween == null)
            _parryCounterCanvasGroup.alpha = parryCounterIdleAlpha;
    }

    void EnsurePunishWindowVisuals()
    {
        if (hpFillImage == null)
            return;

        RectTransform parent = hpFillImage.canvas != null
            ? hpFillImage.canvas.transform as RectTransform
            : hpFillImage.transform.root as RectTransform;

        if (parent == null)
            return;

        RectTransform root = parent.Find(RuntimePunishWindowRootName) as RectTransform;
        if (root == null)
        {
            GameObject rootGo = new GameObject(RuntimePunishWindowRootName, typeof(RectTransform), typeof(CanvasRenderer), typeof(CanvasGroup));
            root = rootGo.GetComponent<RectTransform>();
            root.SetParent(parent, false);
        }

        _punishWindowRoot = root;
        root.anchorMin = new Vector2(0.5f, 0.5f);
        root.anchorMax = new Vector2(0.5f, 0.5f);
        root.pivot = new Vector2(0.5f, 0.5f);
        root.anchoredPosition = new Vector2(0f, punishWindowYOffset);
        root.sizeDelta = new Vector2(284f, 36f);
        root.localScale = Vector3.one;

        Image panel = FindOrCreateChildImage(root, RuntimePunishWindowPanelName);
        StretchToParent(panel.rectTransform);
        panel.sprite = ResolveRuntimeSprite();
        panel.type = Image.Type.Sliced;
        panel.color = punishWindowPanelColor;
        panel.raycastTarget = false;
        punishWindowPanelImage = panel;

        Image fill = FindOrCreateChildImage(root, RuntimePunishWindowFillName);
        StretchToParent(fill.rectTransform);
        fill.sprite = ResolveRuntimeSprite();
        fill.type = Image.Type.Filled;
        fill.fillMethod = Image.FillMethod.Horizontal;
        fill.fillOrigin = (int)Image.OriginHorizontal.Left;
        fill.fillClockwise = true;
        fill.color = punishWindowFillColor;
        fill.raycastTarget = false;
        punishWindowFillImage = fill;

        RectTransform textRect = root.Find(RuntimePunishWindowTextName) as RectTransform;
        if (textRect == null)
        {
            GameObject textGo = new GameObject(RuntimePunishWindowTextName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            textRect = textGo.GetComponent<RectTransform>();
            textRect.SetParent(root, false);
        }

        StretchToParent(textRect);
        Text text = textRect.GetComponent<Text>();
        if (text == null)
            text = textRect.gameObject.AddComponent<Text>();

        text.alignment = TextAnchor.MiddleCenter;
        text.font = ResolveRuntimeFont();
        text.fontSize = 18;
        text.fontStyle = FontStyle.Bold;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
        text.text = "PUNISH";
        text.color = punishWindowTextColor;
        punishWindowText = text;

        _punishWindowCanvasGroup = root.GetComponent<CanvasGroup>();
        if (_punishWindowCanvasGroup == null)
            _punishWindowCanvasGroup = root.gameObject.AddComponent<CanvasGroup>();

        _punishWindowCanvasGroup.alpha = punishWindowIdleAlpha;
    }

    void UpdatePerformanceHud()
    {
        if (!showPerformanceHud)
        {
            if (_performanceHudRoot != null)
                _performanceHudRoot.gameObject.SetActive(false);
            return;
        }

        EnsurePerformanceHudVisuals();
        if (_performanceHudText == null)
            return;

        float frameDelta = Mathf.Max(0.0001f, Time.unscaledDeltaTime);
        float smoothing = 1f - Mathf.Clamp01(performanceHudSmoothing);
        _smoothedDeltaTime = Mathf.Lerp(frameDelta, _smoothedDeltaTime, smoothing);
        _performanceFrameTimeAccum += frameDelta;
        _performanceFrameCount++;

        if (Time.unscaledTime < _nextPerformanceHudRefreshTime)
            return;

        _nextPerformanceHudRefreshTime = Time.unscaledTime + Mathf.Max(0.05f, performanceHudRefreshInterval);

        float averagedDelta = _performanceFrameCount > 0
            ? _performanceFrameTimeAccum / _performanceFrameCount
            : _smoothedDeltaTime;
        _performanceFrameTimeAccum = 0f;
        _performanceFrameCount = 0;

        _displayedPerformanceMs = Mathf.Lerp(_displayedPerformanceMs, averagedDelta * 1000f, 0.22f);
        _displayedPerformanceFps = Mathf.Lerp(_displayedPerformanceFps, 1f / Mathf.Max(0.0001f, averagedDelta), 0.22f);

        string nextLabel = $"FPS {Mathf.RoundToInt(_displayedPerformanceFps)}  |  {_displayedPerformanceMs:0.0} ms";
        if (!string.Equals(_lastPerformanceHudLabel, nextLabel))
        {
            _lastPerformanceHudLabel = nextLabel;
            _performanceHudText.text = nextLabel;
        }

        if (_performanceHudText.color != performanceHudTextColor)
            _performanceHudText.color = performanceHudTextColor;
    }

    void EnsurePerformanceHudVisuals()
    {
        if (!showPerformanceHud || hpFillImage == null)
            return;

        if (_performanceHudRoot != null && _performanceHudText != null)
        {
            if (!_performanceHudRoot.gameObject.activeSelf)
                _performanceHudRoot.gameObject.SetActive(true);
            return;
        }

        RectTransform parent = hpFillImage.canvas != null
            ? hpFillImage.canvas.transform as RectTransform
            : hpFillImage.transform.root as RectTransform;

        if (parent == null)
            return;

        RectTransform root = parent.Find(RuntimePerformanceHudRootName) as RectTransform;
        if (root == null)
        {
            GameObject rootGo = new GameObject(RuntimePerformanceHudRootName, typeof(RectTransform), typeof(CanvasRenderer));
            root = rootGo.GetComponent<RectTransform>();
            root.SetParent(parent, false);
        }

        _performanceHudRoot = root;
        if (root.anchorMin != new Vector2(0f, 1f))
            root.anchorMin = new Vector2(0f, 1f);
        if (root.anchorMax != new Vector2(0f, 1f))
            root.anchorMax = new Vector2(0f, 1f);
        if (root.pivot != new Vector2(0f, 1f))
            root.pivot = new Vector2(0f, 1f);
        if (root.anchoredPosition != performanceHudAnchoredPosition)
            root.anchoredPosition = performanceHudAnchoredPosition;
        if (root.sizeDelta != new Vector2(360f, 48f))
            root.sizeDelta = new Vector2(360f, 48f);
        if (root.localScale != Vector3.one)
            root.localScale = Vector3.one;
        root.gameObject.SetActive(true);

        RectTransform textRect = root.Find(RuntimePerformanceHudTextName) as RectTransform;
        bool createdText = false;
        if (textRect == null)
        {
            GameObject textGo = new GameObject(RuntimePerformanceHudTextName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            textRect = textGo.GetComponent<RectTransform>();
            textRect.SetParent(root, false);
            createdText = true;
        }

        if (textRect.anchorMin != Vector2.zero || textRect.anchorMax != Vector2.one || textRect.offsetMin != Vector2.zero || textRect.offsetMax != Vector2.zero)
            StretchToParent(textRect);
        Text text = textRect.GetComponent<Text>();
        if (text == null)
        {
            text = textRect.gameObject.AddComponent<Text>();
            createdText = true;
        }

        if (createdText)
        {
            text.alignment = TextAnchor.UpperLeft;
            text.font = ResolveRuntimeFont();
            text.fontSize = 26;
            text.fontStyle = FontStyle.Bold;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
        }

        if (createdText || string.IsNullOrWhiteSpace(text.text))
        {
            text.text = "FPS --  |  --.- ms";
            _lastPerformanceHudLabel = text.text;
        }

        if (createdText)
            text.color = performanceHudTextColor;

        _performanceHudText = text;
    }

    void EnsureDangerTelegraphFeedback()
    {
        if (dangerTelegraphCameraShake == null && Camera.main != null)
            dangerTelegraphCameraShake = Camera.main.GetComponent<CameraShake>();

        if (dangerTelegraphAudioSource == null && (dangerTelegraphClip != null || generateDangerTelegraphTone))
        {
            dangerTelegraphAudioSource = GetComponent<AudioSource>();
            if (dangerTelegraphAudioSource == null)
                dangerTelegraphAudioSource = gameObject.AddComponent<AudioSource>();

            dangerTelegraphAudioSource.playOnAwake = false;
            dangerTelegraphAudioSource.loop = false;
            dangerTelegraphAudioSource.spatialBlend = 0f;
        }

        if (_runtimeDangerTelegraphTone == null && generateDangerTelegraphTone)
        {
            _runtimeDangerTelegraphTone = CreateToneClip(
                "_DangerTelegraphTone",
                Mathf.Max(120f, dangerTelegraphToneFrequency),
                Mathf.Max(0.04f, dangerTelegraphToneDuration));
        }
    }

    void FlashParryCounterVisual()
    {
        EnsureParryCounterVisuals();
        if (_parryCounterRoot == null || _parryCounterCanvasGroup == null)
            return;

        if (_parryCounterTween != null)
            _parryCounterTween.Kill();

        _parryCounterCanvasGroup.alpha = 1f;
        _parryCounterRoot.localScale = new Vector3(0.96f, 0.96f, 1f);
        _parryCounterFlashUntilTime = Time.unscaledTime + 0.20f;
        _parryCounterTween = null;
    }

    void PlayDangerTelegraphFeedback()
    {
        float now = Time.unscaledTime;
        if (now < _nextDangerTelegraphFeedbackAllowedTime)
            return;

        _nextDangerTelegraphFeedbackAllowedTime = now + Mathf.Max(0.05f, dangerTelegraphFeedbackCooldown);
        EnsureDangerTelegraphFeedback();

        if (dangerTelegraphCameraShake != null)
            dangerTelegraphCameraShake.Shake(dangerTelegraphShakeAmplitude, dangerTelegraphShakeDuration);

        // 반복 10: 보스 텔레그래프가 타입별 큐 효과음을 내면 HUD 합성음은 생략 (같은 순간 이중 재생 방지)
        if (dangerTelegraphAudioSource == null || BossTelegraphVfxPresenter.CueSfxActive)
            return;

        AudioClip clip = dangerTelegraphClip != null ? dangerTelegraphClip : _runtimeDangerTelegraphTone;
        if (clip == null)
            return;

        dangerTelegraphAudioSource.pitch = 1f;
        dangerTelegraphAudioSource.PlayOneShot(clip, AudioOptionsRuntime.ScaleSfx(dangerTelegraphVolume));
    }

    void ApplyFrameAlpha()
    {
        ApplyHudTheme();

        Transform frame = transform.Find("UltimateGaugeRoot/BG");
        Image frameImage = frame != null ? frame.GetComponent<Image>() : null;
        if (frameImage == null)
            return;

        // 테마(흰 프레임 스프라이트)가 적용되면 테마 알파 사용, 아니면 기존 알파 보정
        if (GameplayHudStyler.ApplyFrame(frameImage))
            return;

        Color c = frameImage.color;
        c.a = frameAlpha;
        frameImage.color = c;
    }

    // ---- 아라사카 톤 HUD 테마 (GameplayUiTheme 한 곳에서 색 결정) ----
    const string RuntimeHpDamageChunkName = "_RuntimeHpDamageChunk";
    [Header("HP Damage Chunk")]
    [Tooltip("HP 감소 시 레드 청크가 남아 있는 시간")]
    public float hpDamageChunkHold = 0.35f;
    [Tooltip("레드 청크가 줄어드는 시간")]
    public float hpDamageChunkDrain = 0.45f;

    bool _hudThemeApplied;
    Image _hpDamageChunkImage;
    Image _ultimateFillImage;

    void ApplyHudTheme()
    {
        if (_hudThemeApplied)
            return;

        _hudThemeApplied = true;
        GameplayUiTheme theme = GameplayHudStyler.Theme;

        if (hpFillImage != null)
        {
            EnsureHpDamageChunk();
            GameplayHudStyler.ApplyWhite(hpFillImage, GameplayHudStyler.ResolveHpColor(hpFillImage.fillAmount));
        }

        if (ampouleSlots != null)
        {
            for (int i = 0; i < ampouleSlots.Length; i++)
                GameplayHudStyler.ApplyWhite(ampouleSlots[i], theme.hudAmpouleColor);
        }

        Transform ultFill = transform.Find("UltimateGaugeRoot/Fill");
        _ultimateFillImage = ultFill != null ? ultFill.GetComponent<Image>() : null;
        if (GameplayHudStyler.ApplyWhite(_ultimateFillImage, GameplayHudStyler.ResolveUltimateColor(0f, false)))
        {
            // 흰 프레임(BG)의 어두운 내부가 링 채움을 덮어 탁해지지 않도록 채움을 프레임 위로
            Transform ultBg = transform.Find("UltimateGaugeRoot/BG");
            if (ultBg != null && ultBg.parent == ultFill.parent && ultFill.GetSiblingIndex() < ultBg.GetSiblingIndex())
                ultFill.SetSiblingIndex(ultBg.GetSiblingIndex());
        }

        ultimateGaugePercentTextColor = theme.hudTextColor;
        if (ultimateGaugePercentText != null)
            ultimateGaugePercentText.color = ultimateGaugePercentTextColor;

        // 런타임 배지(패링 카운터/빈틈)·성능 HUD도 같은 팔레트로 (생성 전에 색 확정)
        parryCounterPanelColor = theme.hudPanelColor;
        parryCounterFillColor = theme.hudAccentColor;
        parryCounterTextColor = theme.hudTextColor;
        punishWindowPanelColor = theme.hudPanelColor;
        punishWindowFillColor = theme.hudHpFillColor;
        punishWindowTextColor = theme.hudTextColor;
        guardStrainLowColor = theme.hudFrameColor;
        performanceHudTextColor = new Color(theme.hudTextColor.r, theme.hudTextColor.g, theme.hudTextColor.b, 0.8f);
    }

    void EnsureHpDamageChunk()
    {
        if (_hpDamageChunkImage != null || hpFillImage == null)
            return;

        RectTransform source = hpFillImage.rectTransform;
        Transform parent = source.parent;
        if (parent == null)
            return;

        Transform existing = parent.Find(RuntimeHpDamageChunkName);
        Image chunk = existing != null ? existing.GetComponent<Image>() : null;
        if (chunk == null)
        {
            GameObject go = new GameObject(RuntimeHpDamageChunkName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.layer = source.gameObject.layer;
            chunk = go.GetComponent<Image>();
            chunk.rectTransform.SetParent(parent, false);
        }

        RectTransform rect = chunk.rectTransform;
        rect.anchorMin = source.anchorMin;
        rect.anchorMax = source.anchorMax;
        rect.pivot = source.pivot;
        rect.sizeDelta = source.sizeDelta;
        rect.anchoredPosition = source.anchoredPosition;
        rect.localScale = source.localScale;
        rect.localRotation = source.localRotation;
        rect.SetSiblingIndex(source.GetSiblingIndex());

        Sprite white = GameplayHudStyler.ResolveWhiteSprite(hpFillImage.sprite);
        chunk.sprite = white != null ? white : hpFillImage.sprite;
        chunk.type = Image.Type.Filled;
        chunk.fillMethod = hpFillImage.fillMethod;
        chunk.fillOrigin = hpFillImage.fillOrigin;
        chunk.fillClockwise = hpFillImage.fillClockwise;
        chunk.preserveAspect = hpFillImage.preserveAspect;
        chunk.fillAmount = hpFillImage.fillAmount;
        chunk.color = GameplayHudStyler.Theme.hudHpDamageChunkColor;
        chunk.raycastTarget = false;
        _hpDamageChunkImage = chunk;
    }

    void UpdateHpDamageChunk(float targetFillAmount)
    {
        if (_hpDamageChunkImage == null)
            return;

        _hpDamageChunkImage.DOKill();
        if (targetFillAmount >= _hpDamageChunkImage.fillAmount)
        {
            _hpDamageChunkImage.fillAmount = targetFillAmount;
            return;
        }

        _hpDamageChunkImage.DOFillAmount(targetFillAmount, hpDamageChunkDrain)
            .SetDelay(hpDamageChunkHold)
            .SetEase(Ease.InQuad)
            .SetUpdate(true);
    }

    void ApplyUltimateGaugeColor(float normalized, bool ready)
    {
        if (_ultimateFillImage != null)
            _ultimateFillImage.color = GameplayHudStyler.ResolveUltimateColor(normalized, ready);
    }

    // 밸런스 게이지(PlayerBalanceHudBar)와 같은 값을 중복 표시하지 않도록 구형 가드 안정도 바를 숨김
    void DisableLegacyGuardStrainVisuals()
    {
        RectTransform root = _guardRoot;
        if (root == null && guardStrainTrackImage != null)
            root = guardStrainTrackImage.rectTransform.parent as RectTransform;
        if (root == null && hpFillImage != null && hpFillImage.rectTransform.parent != null)
            root = hpFillImage.rectTransform.parent.Find(RuntimeGuardRootName) as RectTransform;

        if (root != null && root.name == RuntimeGuardRootName)
            root.gameObject.SetActive(false);
        else
        {
            if (guardStrainTrackImage != null)
                guardStrainTrackImage.gameObject.SetActive(false);
            if (guardStrainFillImage != null)
                guardStrainFillImage.gameObject.SetActive(false);
        }

        guardStrainFillImage = null;
        guardStrainTrackImage = null;
        _guardRoot = null;
        _guardCanvasGroup = null;
        _displayedGuardStrain = 0f;
    }

    void CreateRuntimeGuardStrainBar()
    {
        RectTransform hpRect = hpFillImage.rectTransform;
        RectTransform parent = hpRect.parent as RectTransform;
        if (parent == null)
            return;

        RectTransform root = parent.Find(RuntimeGuardRootName) as RectTransform;
        if (root == null)
        {
            GameObject rootGo = new GameObject(RuntimeGuardRootName, typeof(RectTransform), typeof(CanvasRenderer), typeof(CanvasGroup));
            root = rootGo.GetComponent<RectTransform>();
            root.SetParent(parent, false);
        }

        _guardRoot = root;
        root.anchorMin = hpRect.anchorMin;
        root.anchorMax = hpRect.anchorMax;
        root.pivot = hpRect.pivot;
        root.sizeDelta = new Vector2(ResolveRuntimeWidth(hpRect), guardStrainBarHeight);
        root.anchoredPosition = hpRect.anchoredPosition + new Vector2(0f, guardStrainYOffset);
        root.localScale = Vector3.one;

        Image track = FindOrCreateChildImage(root, RuntimeGuardTrackName);
        Image fill = FindOrCreateChildImage(root, RuntimeGuardFillName);
        Sprite sprite = ResolveRuntimeSprite();

        track.sprite = sprite;
        track.type = Image.Type.Sliced;
        track.color = guardStrainTrackColor;
        track.raycastTarget = false;
        StretchToParent(track.rectTransform);

        fill.sprite = sprite;
        fill.type = Image.Type.Filled;
        fill.fillMethod = Image.FillMethod.Horizontal;
        fill.fillOrigin = (int)Image.OriginHorizontal.Left;
        fill.fillClockwise = true;
        fill.color = guardStrainLowColor;
        fill.raycastTarget = false;
        StretchToParent(fill.rectTransform);

        guardStrainTrackImage = track;
        guardStrainFillImage = fill;
        _guardCanvasGroup = root.GetComponent<CanvasGroup>();
        _guardCanvasGroup.alpha = guardStrainIdleAlpha;
    }

    static Image FindOrCreateChildImage(RectTransform parent, string name)
    {
        RectTransform rect = parent.Find(name) as RectTransform;
        if (rect == null)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
        }

        Image image = rect.GetComponent<Image>();
        if (image == null)
            image = rect.gameObject.AddComponent<Image>();

        return image;
    }

    static void StretchToParent(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.localScale = Vector3.one;
    }

    static float ResolveRuntimeWidth(RectTransform hpRect)
    {
        float width = hpRect.rect.width;
        if (width <= 0.01f)
            width = Mathf.Abs(hpRect.sizeDelta.x);
        if (width <= 0.01f)
            width = 220f;
        return width;
    }

    static AudioClip CreateToneClip(string clipName, float frequency, float duration)
    {
        const int sampleRate = 44100;
        int sampleCount = Mathf.Max(1, Mathf.CeilToInt(sampleRate * duration));
        float[] data = new float[sampleCount];

        for (int i = 0; i < sampleCount; i++)
        {
            float t = i / (float)sampleRate;
            float envelope = 1f - Mathf.Clamp01(i / (float)(sampleCount - 1));
            data[i] = Mathf.Sin(2f * Mathf.PI * frequency * t) * 0.18f * envelope;
        }

        AudioClip clip = AudioClip.Create(clipName, sampleCount, 1, sampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }

    Sprite ResolveRuntimeSprite()
    {
        if (hpFillImage != null && hpFillImage.sprite != null)
            return hpFillImage.sprite;

        return Resources.GetBuiltinResource<Sprite>("UI/Skin/UISprite.psd");
    }

    static Font ResolveRuntimeFont()
    {
        return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    }

    static string GetUltimateGaugePercentLabel(int percent)
    {
        percent = Mathf.Clamp(percent, 0, 100);
        if (s_ultimateGaugePercentLabels == null)
        {
            s_ultimateGaugePercentLabels = new string[101];
            for (int i = 0; i < s_ultimateGaugePercentLabels.Length; i++)
                s_ultimateGaugePercentLabels[i] = i + "%";
        }

        return s_ultimateGaugePercentLabels[percent];
    }

    static bool Approximately(Color a, Color b)
    {
        return Mathf.Abs(a.r - b.r) <= 0.001f
            && Mathf.Abs(a.g - b.g) <= 0.001f
            && Mathf.Abs(a.b - b.b) <= 0.001f
            && Mathf.Abs(a.a - b.a) <= 0.001f;
    }

    Color ResolveCombatTelegraphColor(AttackTelegraphType telegraphType)
    {
        switch (telegraphType)
        {
            case AttackTelegraphType.Parry:
                return combatTelegraphParryColor;
            case AttackTelegraphType.Guard:
                return combatTelegraphGuardColor;
            case AttackTelegraphType.Danger:
                return combatTelegraphDangerColor;
            case AttackTelegraphType.Dodge:
            case AttackTelegraphType.Auto:
            default:
                return combatTelegraphDodgeColor;
        }
    }

    void OnHPChanged(int current, int max) => UpdateHP(current, max);
    void OnAmpouleChanged(int current, int max) => UpdateAmpoule(current, max);
    void OnDied() { }
}
