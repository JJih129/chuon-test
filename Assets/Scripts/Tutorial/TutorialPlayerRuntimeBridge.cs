using Combat;
using UnityEngine;
using UnityEngine.Events;

[DisallowMultipleComponent]
public class TutorialPlayerRuntimeBridge : MonoBehaviour
{
    [Header("Core References")]
    [SerializeField] private GameObject playerObject;
    [SerializeField] private Camera gameplayCamera;
    [SerializeField] private ExistingLockOnAdapter lockOnReader;

    [Header("Player Systems")]
    [SerializeField] private PlayerCombatController combatController;
    [SerializeField] private PlayerGuardController guardController;
    [SerializeField] private PlayerDodgeController dodgeController;
    [SerializeField] private PerfectDodgeController perfectDodgeController;
    [SerializeField] private PlayerHealth playerHealth;
    [SerializeField] private PlayerConsumables playerConsumables;
    [SerializeField] private PlayerUltimateController playerUltimateController;

    public event UnityAction GuardBlocked;
    public event UnityAction ParrySucceeded;
    public event UnityAction PerfectDodged;
    public event UnityAction DodgeStarted;
    public event UnityAction AmpouleUsed;
    public event UnityAction UltimateStarted;
    public event UnityAction UltimateEnded;
    public event UnityAction<int, int> HealthChanged;
    public event UnityAction<int, int> AmpouleChanged;

    public int GuardBlockCount { get; private set; }
    public int ParryCount { get; private set; }
    public int PerfectDodgeCount { get; private set; }
    public int PlayerDamageCount { get; private set; }
    public int AmpouleUseCount { get; private set; }
    public int CurrentHP => playerHealth != null ? playerHealth.CurrentHP : 0;
    public int MaxHP => playerHealth != null ? playerHealth.MaxHP : 0;
    public float NormalizedHealth => playerHealth != null && playerHealth.MaxHP > 0 ? Mathf.Clamp01((float)playerHealth.CurrentHP / playerHealth.MaxHP) : 0f;
    public int CurrentAmpouleCount => playerConsumables != null ? playerConsumables.CurrentAmpoule : 0;
    public int MaxAmpouleCount => playerConsumables != null ? playerConsumables.MaxAmpoule : 0;
    public float UltimateGaugeNormalized => playerUltimateController != null && playerUltimateController.gaugeMax > 0f
        ? Mathf.Clamp01(playerUltimateController.Gauge / playerUltimateController.gaugeMax)
        : 0f;
    public bool IsUltimateReady => UltimateGaugeNormalized >= 0.999f;
    public Transform PlayerTransform => playerObject != null ? playerObject.transform : null;
    public Camera GameplayCamera => gameplayCamera;
    public bool HasLockOnReader => lockOnReader != null;
    public bool IsDodgingNow => dodgeController != null && dodgeController.IsDodging;

    float _lastDodgeRealtime = float.NegativeInfinity;
    float _lastAttackObservedRealtime = float.NegativeInfinity;
    int _lastKnownAmpouleCount = -1;
    int _lastObservedComboDepth;
    TutorialAttackKind _lastObservedAttackKind = TutorialAttackKind.Unknown;
    [SerializeField, Min(0.05f)] private float recentAttackResolveWindow = 0.9f;

    public void ConfigureRuntime(GameObject player, Camera camera, ExistingLockOnAdapter runtimeLockOn)
    {
        playerObject = player;
        gameplayCamera = camera;
        lockOnReader = runtimeLockOn;
        AutoWire();
        BindEvents();
    }

    void Awake()
    {
        AutoWire();
        BindEvents();
    }

    void OnDestroy()
    {
        UnbindEvents();
    }

    public bool ResolveAttackInfo(Transform attacker, out TutorialAttackKind attackKind, out int comboDepth)
    {
        attackKind = TutorialAttackKind.Unknown;
        comboDepth = 0;

        PlayerCombatController runtimeCombat = combatController;
        if (attacker != null)
        {
            runtimeCombat = attacker.GetComponent<PlayerCombatController>();
            if (runtimeCombat == null)
                runtimeCombat = attacker.GetComponentInParent<PlayerCombatController>();
        }

        if (runtimeCombat == null)
            return TryResolveFromRecentAttack(out attackKind, out comboDepth);

        comboDepth = runtimeCombat.CurrentComboDepth;
        AttackInput? currentInput = runtimeCombat.CurrentAttackInput;
        if (currentInput.HasValue)
        {
            attackKind = currentInput.Value == AttackInput.Light
                ? TutorialAttackKind.Light
                : TutorialAttackKind.Heavy;
            CacheRecentAttack(attackKind, comboDepth);
            return true;
        }

        if (TryResolveFromRecentAttack(out TutorialAttackKind cachedKind, out int cachedDepth))
        {
            attackKind = cachedKind;
            comboDepth = Mathf.Max(comboDepth, cachedDepth);
            return true;
        }

        return comboDepth > 0;
    }

    public bool IsLockedOnTarget(Transform expectedTarget)
    {
        if (lockOnReader == null || !lockOnReader.IsLockedOn)
            return false;

        if (expectedTarget == null)
            return true;

        Transform currentTarget = lockOnReader.CurrentTarget;
        if (currentTarget == null)
            return false;

        Transform currentRoot = currentTarget.root != null ? currentTarget.root : currentTarget;
        Transform expectedRoot = expectedTarget.root != null ? expectedTarget.root : expectedTarget;

        return currentTarget == expectedTarget ||
               currentRoot == expectedTarget ||
               currentRoot == expectedRoot ||
               expectedTarget.IsChildOf(currentRoot) ||
               currentTarget.IsChildOf(expectedRoot);
    }

    public bool WasDodgingRecently(float realtimeWindow)
    {
        if (IsDodgingNow)
            return true;

        return Time.realtimeSinceStartup - _lastDodgeRealtime <= Mathf.Max(0.01f, realtimeWindow);
    }

    public bool TryOpenTutorialParryWindow(float seconds)
    {
        if (guardController == null)
            return false;

        guardController.ForceOpenTutorialParryWindow(Mathf.Max(0.05f, seconds));
        return guardController.IsParryWindowOpen;
    }

    public bool TryOpenTutorialPerfectDodgeWindow(float seconds)
    {
        if (perfectDodgeController == null)
            return false;

        dodgeController?.TryStartTutorialDodge();
        perfectDodgeController.PerfectDodgeWindow_Open(Mathf.Max(0.05f, seconds));
        return perfectDodgeController.IsWindowOpen;
    }

    public void FillUltimateGauge()
    {
        playerUltimateController?.FillGaugeForDebug();
    }

    public void EnsureAmpouleAvailable()
    {
        if (playerConsumables == null)
            return;

        if (playerConsumables.CurrentAmpoule > 0)
            return;

        playerConsumables.Refill();
        _lastKnownAmpouleCount = playerConsumables.CurrentAmpoule;
    }

    public void ReduceHealthForTutorial(float normalizedRemaining)
    {
        if (playerHealth == null || playerHealth.IsDead)
            return;

        int targetHp = Mathf.Clamp(
            Mathf.RoundToInt(playerHealth.MaxHP * Mathf.Clamp01(normalizedRemaining)),
            1,
            Mathf.Max(1, playerHealth.MaxHP - 1));

        if (playerHealth.CurrentHP <= targetHp)
            return;

        playerHealth.ApplyChipDamage(playerHealth.CurrentHP - targetHp);
    }

    void AutoWire()
    {
        if (playerObject == null)
        {
            GameObject playerByTag = GameObject.FindGameObjectWithTag("Player");
            if (playerByTag != null)
                playerObject = playerByTag;
        }

        if (gameplayCamera == null)
            gameplayCamera = Camera.main;

        if (playerObject == null)
            return;

        if (combatController == null)
            combatController = playerObject.GetComponent<PlayerCombatController>();
        if (guardController == null)
            guardController = playerObject.GetComponent<PlayerGuardController>();
        if (dodgeController == null)
            dodgeController = playerObject.GetComponent<PlayerDodgeController>();
        if (perfectDodgeController == null)
            perfectDodgeController = playerObject.GetComponent<PerfectDodgeController>();
        if (playerHealth == null)
            playerHealth = playerObject.GetComponent<PlayerHealth>();
        if (playerConsumables == null)
            playerConsumables = playerObject.GetComponent<PlayerConsumables>();
        if (playerUltimateController == null)
            playerUltimateController = playerObject.GetComponent<PlayerUltimateController>();
        if (lockOnReader == null)
            lockOnReader = playerObject.GetComponent<ExistingLockOnAdapter>();
    }

    void BindEvents()
    {
        UnbindEvents();

        if (guardController != null)
        {
            guardController.OnGuardBlock.AddListener(HandleGuardBlock);
            guardController.OnParrySuccess.AddListener(HandleParrySuccess);
        }

        if (dodgeController != null)
            dodgeController.OnDodgeStart.AddListener(HandleDodgeStarted);

        if (perfectDodgeController != null)
            perfectDodgeController.OnPerfectDodge.AddListener(HandlePerfectDodge);

        if (combatController != null)
            combatController.OnAttackStarted += HandleAttackStarted;

        if (playerConsumables != null)
        {
            _lastKnownAmpouleCount = playerConsumables.CurrentAmpoule;
            playerConsumables.OnAmpouleChanged += HandleAmpouleChanged;
        }

        if (playerUltimateController != null)
            playerUltimateController.OnUltimateStarted += HandleUltimateStarted;
        if (playerUltimateController != null)
            playerUltimateController.OnUltimateEnded += HandleUltimateEnded;

        if (playerHealth != null)
            playerHealth.OnDamaged += HandleDamaged;
        if (playerHealth != null)
            playerHealth.OnHealthChanged += HandleHealthChanged;
    }

    void UnbindEvents()
    {
        if (guardController != null)
        {
            guardController.OnGuardBlock.RemoveListener(HandleGuardBlock);
            guardController.OnParrySuccess.RemoveListener(HandleParrySuccess);
        }

        if (dodgeController != null)
            dodgeController.OnDodgeStart.RemoveListener(HandleDodgeStarted);

        if (perfectDodgeController != null)
            perfectDodgeController.OnPerfectDodge.RemoveListener(HandlePerfectDodge);

        if (combatController != null)
            combatController.OnAttackStarted -= HandleAttackStarted;

        if (playerConsumables != null)
            playerConsumables.OnAmpouleChanged -= HandleAmpouleChanged;

        if (playerUltimateController != null)
            playerUltimateController.OnUltimateStarted -= HandleUltimateStarted;
        if (playerUltimateController != null)
            playerUltimateController.OnUltimateEnded -= HandleUltimateEnded;

        if (playerHealth != null)
            playerHealth.OnDamaged -= HandleDamaged;
        if (playerHealth != null)
            playerHealth.OnHealthChanged -= HandleHealthChanged;
    }

    void HandleGuardBlock()
    {
        GuardBlockCount++;
        GuardBlocked?.Invoke();
    }

    void HandleParrySuccess()
    {
        ParryCount++;
        ParrySucceeded?.Invoke();
    }

    void HandlePerfectDodge()
    {
        PerfectDodgeCount++;
        PerfectDodged?.Invoke();
    }

    void HandleDodgeStarted()
    {
        _lastDodgeRealtime = Time.realtimeSinceStartup;
        DodgeStarted?.Invoke();
    }

    void HandleAttackStarted(AttackInput input, AttackData data, int comboDepth)
    {
        TutorialAttackKind attackKind = input == AttackInput.Light
            ? TutorialAttackKind.Light
            : TutorialAttackKind.Heavy;
        CacheRecentAttack(attackKind, comboDepth);
    }

    void HandleAmpouleChanged(int current, int max)
    {
        if (_lastKnownAmpouleCount >= 0 && current < _lastKnownAmpouleCount)
        {
            AmpouleUseCount += _lastKnownAmpouleCount - current;
            AmpouleUsed?.Invoke();
        }

        _lastKnownAmpouleCount = current;
        AmpouleChanged?.Invoke(current, max);
    }

    void HandleUltimateStarted()
    {
        UltimateStarted?.Invoke();
    }

    void HandleUltimateEnded()
    {
        UltimateEnded?.Invoke();
    }

    void HandleDamaged(int amount)
    {
        PlayerDamageCount++;
    }

    void HandleHealthChanged(int current, int max)
    {
        HealthChanged?.Invoke(current, max);
    }

    void CacheRecentAttack(TutorialAttackKind attackKind, int comboDepth)
    {
        _lastObservedAttackKind = attackKind;
        _lastObservedComboDepth = Mathf.Max(1, comboDepth);
        _lastAttackObservedRealtime = Time.realtimeSinceStartup;
    }

    bool TryResolveFromRecentAttack(out TutorialAttackKind attackKind, out int comboDepth)
    {
        attackKind = TutorialAttackKind.Unknown;
        comboDepth = 0;

        if (_lastObservedAttackKind == TutorialAttackKind.Unknown)
            return false;

        if (Time.realtimeSinceStartup - _lastAttackObservedRealtime > recentAttackResolveWindow)
            return false;

        attackKind = _lastObservedAttackKind;
        comboDepth = _lastObservedComboDepth;
        return true;
    }
}
