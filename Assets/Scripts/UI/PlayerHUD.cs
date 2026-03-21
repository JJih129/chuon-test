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

    [Header("Guard Strain")]
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
    [Tooltip("보스 공격 대응 힌트 텍스트")]
    public Text combatTelegraphText;
    [Tooltip("보스 공격 대응 힌트 배경")]
    public Image combatTelegraphPanelImage;
    [Tooltip("HUD 중앙 기준 Y 오프셋")]
    public float combatTelegraphYOffset = -110f;
    [Tooltip("텔레그래프 표시 유지 시간")]
    public float combatTelegraphHoldTime = 0.42f;
    public Color combatTelegraphPanelColor = new Color(0.03f, 0.07f, 0.10f, 0.88f);
    public Color combatTelegraphChainPanelColor = new Color(0.12f, 0.10f, 0.04f, 0.92f);
    public Color combatTelegraphParryColor = new Color(1.00f, 0.80f, 0.32f, 1f);
    public Color combatTelegraphGuardColor = new Color(0.35f, 0.92f, 1.00f, 1f);
    public Color combatTelegraphDodgeColor = new Color(1.00f, 0.40f, 0.40f, 1f);
    public Color combatTelegraphDangerColor = new Color(1.00f, 0.28f, 0.88f, 1f);
    public float combatTelegraphChainPunch = 0.10f;

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

    [Header("Parry Counter")]
    [Tooltip("패링 카운터 준비 텍스트")]
    public Text parryCounterText;
    [Tooltip("패링 카운터 배경")]
    public Image parryCounterPanelImage;
    [Tooltip("패링 카운터 남은 시간 표시")]
    public Image parryCounterFillImage;
    [Tooltip("카운터 배지의 중앙 HUD Y 오프셋")]
    public float parryCounterYOffset = -154f;
    [Tooltip("카운터 배지 유지 알파")]
    public float parryCounterActiveAlpha = 0.96f;
    [Tooltip("카운터 배지 숨김 알파")]
    public float parryCounterIdleAlpha = 0f;
    public Color parryCounterPanelColor = new Color(0.03f, 0.08f, 0.11f, 0.92f);
    public Color parryCounterFillColor = new Color(0.28f, 0.96f, 1.00f, 0.95f);
    public Color parryCounterTextColor = new Color(0.82f, 1.00f, 1.00f, 1.00f);

    [Header("Performance HUD")]
    [Tooltip("좌상단 FPS 표시")]
    public bool showPerformanceHud = true;
    [Tooltip("FPS 표시 위치")]
    public Vector2 performanceHudAnchoredPosition = new Vector2(24f, -24f);
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
    CanvasGroup _parryCounterCanvasGroup;
    RectTransform _parryCounterRoot;
    Tween _parryCounterTween;
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
    const string RuntimePerformanceHudRootName = "_RuntimePerformanceHUD";
    const string RuntimePerformanceHudTextName = "_Text";

    void Update()
    {
        float now = Time.unscaledTime;

        if (ShouldTickStatusHud(now) && now >= _nextStatusHudRefreshTime)
        {
            _nextStatusHudRefreshTime = now + Mathf.Max(1f / 60f, statusHudRefreshInterval);
            UpdateGuardStrainVisual();
            UpdateParryCounterVisual();
        }

        if (ShouldTickCombatTelegraph())
            UpdateCombatTelegraphVisual();

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
    }

    public void UpdateAmpoule(int count)
    {
        int maxDisplayCount = ampouleSlots.Length;
        int clamped = Mathf.Clamp(count, 0, maxDisplayCount);

        for (int i = 0; i < ampouleSlots.Length; i++)
        {
            if (ampouleSlots[i] != null)
                ampouleSlots[i].enabled = i < clamped;
        }
    }

    public void Bind(IHealth health, PlayerConsumables consumables = null, PlayerGuardController guard = null, BossController boss = null)
    {
        Unbind();

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
            UpdateAmpoule(0);
        }

        BindGuard(guard);
        BindBoss(boss);
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
        hpFillImage?.DOKill();
    }

    void BindGuard(PlayerGuardController guard)
    {
        _boundGuard = guard;
        EnsureGuardStrainVisuals();
        EnsureParryCounterVisuals();

        if (_boundGuard == null || guardStrainFillImage == null)
            return;

        _displayedGuardStrain = _boundGuard.GuardStrainNormalized;
        ApplyGuardStrainVisual(_displayedGuardStrain, true);

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

        if (_combatTelegraphCanvasGroup != null)
            _combatTelegraphCanvasGroup.alpha = 0f;
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
            punchStrength);

        if (telegraphType == AttackTelegraphType.Danger)
            PlayDangerTelegraphFeedback();
    }

    void HandlePunishWindowOpened(float duration, float damageMultiplier, string patternName)
    {
        EnsureCombatTelegraphVisuals();
        if (_combatTelegraphCanvasGroup == null || combatTelegraphText == null || combatTelegraphPanelImage == null)
            return;

        string message = damageMultiplier > 1.01f
            ? $"OPENING x{damageMultiplier:0.00}"
            : "OPENING";

        PlayCombatTelegraphMessage(
            message,
            new Color(0.70f, 1.00f, 0.86f, 1f),
            combatTelegraphPanelColor,
            Mathf.Max(0.26f, duration),
            0f);
    }

    void HandlePunishWindowClosed()
    {
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
    }

    void HandleBossPhaseChanged(int phase, float hpNormalized)
    {
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

        PlayCombatTelegraphMessage(message, textColor, panelColor, 0.72f, punchStrength);

        if (phase >= 3)
            PlayDangerTelegraphFeedback();
    }

    void PlayCombatTelegraphMessage(string message, Color textColor, Color panelColor, float holdTime, float punchStrength)
    {
        combatTelegraphText.text = message;
        combatTelegraphText.color = textColor;
        combatTelegraphPanelImage.color = panelColor;

        if (_combatTelegraphTween != null)
        {
            _combatTelegraphTween.Kill();
            _combatTelegraphTween = null;
        }

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
    }

    void UpdateCombatTelegraphVisual()
    {
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

    bool ShouldTickStatusHud(float now)
    {
        if (_boundGuard == null)
            return false;

        if (now < _guardBreakFlashUntilTime || now < _parrySuccessFlashUntilTime || now < _parryCounterFlashUntilTime)
            return true;

        if (_boundGuard.IsGuarding || _boundGuard.IsGuardBroken || _boundGuard.IsParryCounterReady)
            return true;

        if (_displayedGuardStrain > 0.001f)
            return true;

        if (_guardCanvasGroup != null && _guardCanvasGroup.alpha > guardStrainIdleAlpha + 0.001f)
            return true;

        if (_parryCounterCanvasGroup != null && _parryCounterCanvasGroup.alpha > parryCounterIdleAlpha + 0.001f)
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

    void EnsureGuardStrainVisuals()
    {
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
        root.sizeDelta = new Vector2(220f, 42f);
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
        text.fontSize = 20;
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
        root.sizeDelta = new Vector2(188f, 28f);
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
        text.fontSize = 14;
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
        if (root.sizeDelta != new Vector2(420f, 60f))
            root.sizeDelta = new Vector2(420f, 60f);
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
            text.fontSize = 36;
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
        EnsureDangerTelegraphFeedback();

        if (dangerTelegraphCameraShake != null)
            dangerTelegraphCameraShake.Shake(dangerTelegraphShakeAmplitude, dangerTelegraphShakeDuration);

        if (dangerTelegraphAudioSource == null)
            return;

        AudioClip clip = dangerTelegraphClip != null ? dangerTelegraphClip : _runtimeDangerTelegraphTone;
        if (clip == null)
            return;

        dangerTelegraphAudioSource.pitch = 1f;
        dangerTelegraphAudioSource.PlayOneShot(clip, AudioOptionsRuntime.ScaleSfx(dangerTelegraphVolume));
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
    void OnAmpouleChanged(int current, int max) => UpdateAmpoule(current);
    void OnDied() { }
}
