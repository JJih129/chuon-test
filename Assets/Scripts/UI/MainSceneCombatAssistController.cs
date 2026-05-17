using UnityEngine;

[DisallowMultipleComponent]
public class MainSceneCombatAssistController : MonoBehaviour
{
    [SerializeField] private MainSceneArrivalController arrivalController;
    [SerializeField] private PlayerHUD playerHud;
    [SerializeField] private PlayerHealth playerHealth;
    [SerializeField] private PlayerConsumables playerConsumables;
    [SerializeField] private BossHealth bossHealth;
    [SerializeField] private BossBreakController bossBreakController;
    [SerializeField] private PlayerUltimateController playerUltimateController;

    [Header("Assist Rules")]
    [SerializeField, Range(0.1f, 0.8f)] private float lowHealthThresholdNormalized = 0.35f;
    [SerializeField, Min(2)] private int repeatedDamageThreshold = 2;
    [SerializeField, Min(0.25f)] private float repeatedDamageWindow = 5f;
    [SerializeField, Min(0.1f)] private float cueCooldown = 1.2f;
    [SerializeField] private bool disableAfterFirstBreak = true;
    [SerializeField, Range(0.05f, 0.40f)] private float finishPressureThresholdNormalized = 0.18f;

    [Header("Cue Text")]
    [SerializeField] private string repeatedDamageMessage = "\uc695\uc2ec\ub0b4\uc9c0 \ub9c8. \ud55c \ubc15\uc790 \ub2a6\ucdb0\uc11c \ubc29\uc5b4\ubd80\ud130";
    [SerializeField] private string lowHealthMessage = "\uccb4\ub825\uc774 \ub0ae\ub2e4. \uc570\ud50c\ub85c \ud638\ud761\ubd80\ud130 \ub418\ucc3e\uc544";
    [SerializeField] private string healUsedMessage = "\uc88b\uc544. \ud638\ud761 \ud68c\ubcf5 \uc644\ub8cc. \ub2e4\uc74c \ud328\ud134\uc744 \ub2e4\uc2dc \uc77d\uc5b4";
    [SerializeField] private string recoveryWindowMessage = "\ube0c\ub808\uc774\ud06c\uae4c\uc9c0 \uc0b4\uc544\ub0a8\uc558\ub2e4. \uc774\uc81c \ub9ac\ub4ec\uc744 \uc720\uc9c0\ud574";
    [SerializeField] private string finishPressureMessage = "\ub9c8\ubb34\ub9ac \uad6c\uac04\uc774\uc57c. \ubb34\ub9ac\ud558\uc9c0 \ub9d0\uace0 \ud328\ud134 \ub05d\uc5d0 \ub123\uc5b4";
    [SerializeField] private string finishUltimateReadyMessage = "\ub9c8\ubb34\ub9ac \ud0c0\uc774\ubc0d. \uad81\uadf9\uae30\ub97c \uc548\uc804\ud55c \ube48\ud2c8\uc5d0 \uacb9\uccd0";
    [SerializeField, Min(0.1f)] private float repeatedDamageHold = 0.84f;
    [SerializeField, Min(0.1f)] private float lowHealthHold = 0.92f;
    [SerializeField, Min(0.1f)] private float healUsedHold = 0.72f;
    [SerializeField, Min(0.1f)] private float recoveryWindowHold = 0.74f;
    [SerializeField, Min(0.1f)] private float finishPressureHold = 0.82f;

    bool _subscribed;
    bool _assistActive;
    bool _earlyAssistClosed;
    bool _repeatedDamageCueShown;
    bool _lowHealthCueShown;
    bool _healUsedCueShown;
    bool _recoveryWindowCueShown;
    bool _finishPressureCueShown;
    bool _finishUltimateReadyCueShown;
    int _damageChainCount;
    int _lastAmpouleCount = -1;
    float _lastDamageTime = float.NegativeInfinity;
    float _nextCueAllowedTime;

    public void ConfigureRuntime(
        MainSceneArrivalController runtimeArrivalController,
        PlayerHUD runtimeHud,
        PlayerHealth runtimePlayerHealth,
        PlayerConsumables runtimeConsumables,
        BossBreakController runtimeBossBreakController,
        PlayerUltimateController runtimePlayerUltimateController)
    {
        if (!ExhibitionPrototypePresentationPolicy.RuntimeCoachFeedbackEnabled)
        {
            ReleaseSubscriptions();
            return;
        }

        arrivalController = runtimeArrivalController;
        playerHud = runtimeHud;
        playerHealth = runtimePlayerHealth;
        playerConsumables = runtimeConsumables;
        bossHealth = runtimeBossBreakController != null ? runtimeBossBreakController.GetComponent<BossHealth>() : null;
        bossBreakController = runtimeBossBreakController;
        playerUltimateController = runtimePlayerUltimateController;

        ResolveReferences();
        ResetRuntimeState();
        RefreshSubscriptions();
    }

    void OnEnable()
    {
        if (!ExhibitionPrototypePresentationPolicy.RuntimeCoachFeedbackEnabled)
        {
            ReleaseSubscriptions();
            return;
        }

        ResolveReferences();
        ResetRuntimeState();
        RefreshSubscriptions();
    }

    void OnDisable()
    {
        ReleaseSubscriptions();
    }

    void OnDestroy()
    {
        ReleaseSubscriptions();
    }

    void ResolveReferences()
    {
        if (arrivalController == null)
            arrivalController = GetComponent<MainSceneArrivalController>();
        if (playerHud == null)
            playerHud = FindObjectOfType<PlayerHUD>(true);
        if (playerHealth == null)
            playerHealth = FindObjectOfType<PlayerHealth>(true);
        if (playerConsumables == null)
            playerConsumables = FindObjectOfType<PlayerConsumables>(true);
        if (bossHealth == null && bossBreakController != null)
            bossHealth = bossBreakController.GetComponent<BossHealth>();
        if (bossHealth == null)
            bossHealth = FindObjectOfType<BossHealth>(true);
        if (bossBreakController == null)
            bossBreakController = FindObjectOfType<BossBreakController>(true);
        if (playerUltimateController == null)
            playerUltimateController = FindObjectOfType<PlayerUltimateController>(true);
    }

    void ResetRuntimeState()
    {
        _assistActive = arrivalController != null && arrivalController.IsLobbyTransitionActive;
        _earlyAssistClosed = false;
        _repeatedDamageCueShown = false;
        _lowHealthCueShown = false;
        _healUsedCueShown = false;
        _recoveryWindowCueShown = false;
        _finishPressureCueShown = IsBossInFinishPressure();
        _finishUltimateReadyCueShown = false;
        _damageChainCount = 0;
        _lastDamageTime = float.NegativeInfinity;
        _nextCueAllowedTime = 0f;
        _lastAmpouleCount = playerConsumables != null ? playerConsumables.CurrentAmpoule : -1;
    }

    void RefreshSubscriptions()
    {
        if (!ExhibitionPrototypePresentationPolicy.RuntimeCoachFeedbackEnabled)
            return;

        ReleaseSubscriptions();
        if (!_assistActive)
            return;

        ResolveReferences();

        if (playerHealth != null)
        {
            playerHealth.OnDamaged += HandlePlayerDamaged;
            playerHealth.OnHPChanged += HandlePlayerHpChanged;
            _subscribed = true;
        }

        if (playerConsumables != null)
        {
            playerConsumables.OnAmpouleChanged += HandleAmpouleChanged;
            _subscribed = true;
        }

        if (bossHealth != null)
        {
            bossHealth.OnHPChanged += HandleBossHpChanged;
            _subscribed = true;
        }

        if (bossBreakController != null)
        {
            bossBreakController.OnBreakEnter.AddListener(HandleBreakEnter);
            _subscribed = true;
        }

        if (playerUltimateController != null)
        {
            playerUltimateController.OnGaugeChanged += HandleUltimateGaugeChanged;
            playerUltimateController.OnUltimateStarted += HandleUltimateStarted;
            _subscribed = true;
        }
    }

    void ReleaseSubscriptions()
    {
        if (!_subscribed)
            return;

        if (playerHealth != null)
        {
            playerHealth.OnDamaged -= HandlePlayerDamaged;
            playerHealth.OnHPChanged -= HandlePlayerHpChanged;
        }

        if (playerConsumables != null)
            playerConsumables.OnAmpouleChanged -= HandleAmpouleChanged;

        if (bossHealth != null)
            bossHealth.OnHPChanged -= HandleBossHpChanged;

        if (bossBreakController != null)
            bossBreakController.OnBreakEnter.RemoveListener(HandleBreakEnter);

        if (playerUltimateController != null)
        {
            playerUltimateController.OnGaugeChanged -= HandleUltimateGaugeChanged;
            playerUltimateController.OnUltimateStarted -= HandleUltimateStarted;
        }

        _subscribed = false;
    }

    void HandlePlayerDamaged(int damage)
    {
        if (!_assistActive || _earlyAssistClosed)
            return;

        float now = Time.unscaledTime;
        _damageChainCount = now - _lastDamageTime <= repeatedDamageWindow
            ? _damageChainCount + 1
            : 1;
        _lastDamageTime = now;

        if (!_repeatedDamageCueShown && _damageChainCount >= repeatedDamageThreshold)
        {
            if (TryShowCue(repeatedDamageMessage, AttackTelegraphType.Guard, repeatedDamageHold, false))
                _repeatedDamageCueShown = true;
        }
    }

    void HandlePlayerHpChanged(int current, int max)
    {
        if (!_assistActive || _earlyAssistClosed || _lowHealthCueShown || max <= 0)
            return;

        if ((float)current / max > lowHealthThresholdNormalized)
            return;

        if (playerConsumables != null && playerConsumables.CurrentAmpoule <= 0)
            return;

        if (TryShowCue(lowHealthMessage, AttackTelegraphType.Guard, lowHealthHold, false))
            _lowHealthCueShown = true;
    }

    void HandleAmpouleChanged(int current, int max)
    {
        if (!_assistActive || _earlyAssistClosed)
        {
            _lastAmpouleCount = current;
            return;
        }

        bool ampouleSpent = _lastAmpouleCount >= 0 && current < _lastAmpouleCount;
        _lastAmpouleCount = current;
        if (!ampouleSpent || _healUsedCueShown)
            return;

        if (TryShowCue(healUsedMessage, AttackTelegraphType.Parry, healUsedHold, false))
            _healUsedCueShown = true;
    }

    void HandleBossHpChanged(int current, int max)
    {
        if (!_assistActive || _finishPressureCueShown || max <= 0)
            return;

        if ((float)current / max > finishPressureThresholdNormalized)
            return;

        string message = playerUltimateController != null
            && playerUltimateController.IsGaugeReady
            && !playerUltimateController.IsCinematic
            ? finishUltimateReadyMessage
            : finishPressureMessage;

        if (TryShowCue(message, AttackTelegraphType.Danger, finishPressureHold, false))
        {
            _finishPressureCueShown = true;
            _finishUltimateReadyCueShown = message == finishUltimateReadyMessage;
        }
    }

    void HandleBreakEnter()
    {
        if (!_assistActive || _earlyAssistClosed)
            return;

        if (!_recoveryWindowCueShown)
        {
            if (TryShowCue(recoveryWindowMessage, AttackTelegraphType.Parry, recoveryWindowHold, false))
                _recoveryWindowCueShown = true;
        }

        if (disableAfterFirstBreak)
            _earlyAssistClosed = true;
    }

    void HandleUltimateStarted()
    {
        _earlyAssistClosed = true;
    }

    void HandleUltimateGaugeChanged(float gauge, float normalized, bool ready)
    {
        if (!_assistActive || _finishUltimateReadyCueShown || !ready || !IsBossInFinishPressure())
            return;

        if (TryShowCue(finishUltimateReadyMessage, AttackTelegraphType.Danger, finishPressureHold, false))
            _finishUltimateReadyCueShown = true;
    }

    bool TryShowCue(string message, AttackTelegraphType telegraphType, float holdTime, bool useDangerFeedback)
    {
        if (playerHud == null || string.IsNullOrWhiteSpace(message))
            return false;

        float now = Time.unscaledTime;
        if (now < _nextCueAllowedTime)
            return false;

        playerHud.ShowRuntimeTelegraphMessage(message, telegraphType, holdTime, useDangerFeedback);
        _nextCueAllowedTime = now + Mathf.Max(0.1f, cueCooldown);
        return true;
    }

    bool IsBossInFinishPressure()
    {
        return bossHealth != null
            && bossHealth.MaxHP > 0
            && (float)bossHealth.CurrentHP / bossHealth.MaxHP <= finishPressureThresholdNormalized;
    }
}
