using UnityEngine;

[DisallowMultipleComponent]
public class TutorialMovementConditionChecker : TutorialConditionChecker
{
    [SerializeField] private Transform playerTransform;
    [SerializeField] private TutorialZoneTrigger completionZone;
    [SerializeField] private CollapseFloorTrigger collapseFloorTrigger;
    [SerializeField, Min(0f)] private float requiredDistance = 6f;
    [SerializeField] private bool useDistance = true;
    [SerializeField] private bool useCompletionZone = true;

    Vector3 _startPosition;

    public void ConfigureRuntime(Transform player, TutorialZoneTrigger zone, CollapseFloorTrigger collapseFloor, float distance)
    {
        playerTransform = player;
        completionZone = zone;
        collapseFloorTrigger = collapseFloor;
        requiredDistance = distance;
        useCompletionZone = completionZone != null;
        useDistance = requiredDistance > 0.01f;
    }

    protected override bool ShouldEnableTick() => useDistance;

    protected override void OnBeginChecking(TutorialStepDefinition step)
    {
        if (playerTransform == null)
        {
            Debug.LogWarning("[TutorialMovementConditionChecker] Player transform missing. Step skipped.", this);
            Complete();
            return;
        }

        if (!useDistance && (!useCompletionZone || completionZone == null))
        {
            Debug.LogWarning("[TutorialMovementConditionChecker] No movement target configured. Step skipped.", this);
            Complete();
            return;
        }

        _startPosition = playerTransform.position;

        if (useCompletionZone && completionZone != null)
        {
            completionZone.ResetTriggerState();
            completionZone.TriggerEntered += HandleZoneEntered;
        }

        collapseFloorTrigger?.TriggerCollapse();
    }

    protected override void OnEndChecking()
    {
        if (completionZone != null)
            completionZone.TriggerEntered -= HandleZoneEntered;
    }

    void Update()
    {
        if (!IsRunning || !useDistance || playerTransform == null)
            return;

        Vector3 from = _startPosition;
        Vector3 to = playerTransform.position;
        from.y = 0f;
        to.y = 0f;

        if ((to - from).sqrMagnitude >= requiredDistance * requiredDistance)
            Complete();
    }

    void HandleZoneEntered(TutorialZoneTrigger zone, Collider other)
    {
        Complete();
    }
}

[DisallowMultipleComponent]
public class TutorialLookAtConditionChecker : TutorialConditionChecker
{
    [SerializeField] private Camera targetCamera;
    [SerializeField] private Transform focusTarget;
    [SerializeField, Min(0.05f)] private float requiredVisibleDuration = 0.2f;
    [SerializeField, Range(0.1f, 0.99f)] private float centerDotThreshold = 0.7f;
    [SerializeField, Range(0f, 0.45f)] private float viewportMargin = 0.1f;

    float _visibleTimer;

    public void ConfigureRuntime(Camera cameraRef, Transform target)
    {
        targetCamera = cameraRef;
        focusTarget = target;
    }

    protected override bool ShouldEnableTick() => true;

    protected override void OnBeginChecking(TutorialStepDefinition step)
    {
        if (targetCamera == null)
            targetCamera = Camera.main;

        if (targetCamera == null || focusTarget == null)
        {
            Debug.LogWarning("[TutorialLookAtConditionChecker] Camera or target missing. Step skipped.", this);
            Complete();
        }
    }

    void Update()
    {
        if (!IsRunning || targetCamera == null || focusTarget == null)
            return;

        Vector3 toTarget = focusTarget.position - targetCamera.transform.position;
        float dot = Vector3.Dot(targetCamera.transform.forward.normalized, toTarget.normalized);
        Vector3 viewport = targetCamera.WorldToViewportPoint(focusTarget.position);
        bool isVisible = viewport.z > 0f &&
                         viewport.x >= viewportMargin &&
                         viewport.x <= 1f - viewportMargin &&
                         viewport.y >= viewportMargin &&
                         viewport.y <= 1f - viewportMargin &&
                         dot >= centerDotThreshold;

        _visibleTimer = isVisible ? _visibleTimer + Time.unscaledDeltaTime : 0f;
        if (_visibleTimer >= requiredVisibleDuration)
            Complete();
    }
}

[DisallowMultipleComponent]
public class TutorialLockOnConditionChecker : TutorialConditionChecker
{
    [SerializeField] private TutorialPlayerRuntimeBridge playerBridge;
    [SerializeField] private Transform expectedTarget;
    [SerializeField, Min(0.05f)] private float stableDuration = 0.15f;

    float _lockedTimer;

    public void ConfigureRuntime(TutorialPlayerRuntimeBridge bridge, Transform target)
    {
        playerBridge = bridge;
        expectedTarget = target;
    }

    protected override bool ShouldEnableTick() => true;

    protected override void OnBeginChecking(TutorialStepDefinition step)
    {
        if (playerBridge == null || !playerBridge.HasLockOnReader)
        {
            Debug.LogWarning("[TutorialLockOnConditionChecker] Lock-on reader missing. Step skipped.", this);
            Complete();
        }
    }

    void Update()
    {
        if (!IsRunning || playerBridge == null)
            return;

        _lockedTimer = playerBridge.IsLockedOnTarget(expectedTarget)
            ? _lockedTimer + Time.unscaledDeltaTime
            : 0f;

        if (_lockedTimer >= stableDuration)
            Complete();
    }
}

[DisallowMultipleComponent]
public class TutorialAttackConditionChecker : TutorialConditionChecker
{
    [SerializeField] private TrainingDummyController[] observedDummies;
    [SerializeField] private int requiredLightHits = 1;
    [SerializeField] private int requiredHeavyHits = 1;

    int _lightHits;
    int _heavyHits;
    int _lastAttackSequenceId;
    int _lastAttackerInstanceId;
    float _lastHitRealtime = float.NegativeInfinity;

    public void ConfigureRuntime(TrainingDummyController[] dummies, int lightHits, int heavyHits)
    {
        observedDummies = dummies;
        requiredLightHits = lightHits;
        requiredHeavyHits = heavyHits;
    }

    protected override void OnBeginChecking(TutorialStepDefinition step)
    {
        _lightHits = 0;
        _heavyHits = 0;
        _lastAttackSequenceId = 0;
        _lastAttackerInstanceId = 0;
        _lastHitRealtime = float.NegativeInfinity;

        if (observedDummies == null || observedDummies.Length == 0)
        {
            Complete();
            return;
        }

        for (int i = 0; i < observedDummies.Length; i++)
        {
            if (observedDummies[i] != null)
                observedDummies[i].PlayerHitByPlayer += HandleDummyHit;
        }

        ReportProgress(BuildProgressText());
    }

    protected override void OnEndChecking()
    {
        if (observedDummies == null)
            return;

        for (int i = 0; i < observedDummies.Length; i++)
        {
            if (observedDummies[i] != null)
                observedDummies[i].PlayerHitByPlayer -= HandleDummyHit;
        }
    }

    void HandleDummyHit(TrainingDummyController dummy, TutorialCombatHitInfo info)
    {
        if (!IsRunning)
            return;

        if (IsDuplicateHit(info))
            return;

        if (info.attackKind == TutorialAttackKind.Light)
            _lightHits = Mathf.Min(requiredLightHits, _lightHits + 1);
        else if (info.attackKind == TutorialAttackKind.Heavy)
            _heavyHits = Mathf.Min(requiredHeavyHits, _heavyHits + 1);

        string progress = BuildProgressText();
        ReportProgress(progress);

        if (_lightHits >= requiredLightHits && _heavyHits >= requiredHeavyHits)
            Complete(progress);
    }

    string BuildProgressText()
    {
        return $"\uc57d\uacf5 {_lightHits}/{requiredLightHits}  \uac15\uacf5 {_heavyHits}/{requiredHeavyHits}";
    }

    bool IsDuplicateHit(TutorialCombatHitInfo info)
    {
        int attackSequenceId = info.payload.attackSequenceId;
        int attackerInstanceId = info.payload.attacker != null ? info.payload.attacker.root.GetInstanceID() : 0;
        if (attackSequenceId > 0)
        {
            if (attackSequenceId == _lastAttackSequenceId && attackerInstanceId == _lastAttackerInstanceId)
                return true;

            _lastAttackSequenceId = attackSequenceId;
            _lastAttackerInstanceId = attackerInstanceId;
            _lastHitRealtime = Time.realtimeSinceStartup;
            return false;
        }

        float now = Time.realtimeSinceStartup;
        if (now - _lastHitRealtime <= 0.08f)
            return true;

        _lastHitRealtime = now;
        return false;
    }
}

[DisallowMultipleComponent]
public class TutorialComboConditionChecker : TutorialConditionChecker
{
    [SerializeField] private TrainingDummyController[] observedDummies;
    [SerializeField] private int requiredComboDepth = 3;
    [SerializeField] private int fallbackHitStreak = 3;
    [SerializeField, Min(0.1f)] private float fallbackWindow = 1.1f;

    int _hitStreak;
    float _lastHitTime = float.NegativeInfinity;
    int _lastAttackSequenceId;
    int _lastAttackerInstanceId;

    public void ConfigureRuntime(TrainingDummyController[] dummies, int comboDepth, int streakCount, float streakWindow)
    {
        observedDummies = dummies;
        requiredComboDepth = comboDepth;
        fallbackHitStreak = streakCount;
        fallbackWindow = streakWindow;
    }

    protected override void OnBeginChecking(TutorialStepDefinition step)
    {
        _hitStreak = 0;
        _lastHitTime = float.NegativeInfinity;
        _lastAttackSequenceId = 0;
        _lastAttackerInstanceId = 0;

        if (observedDummies == null || observedDummies.Length == 0)
        {
            Complete();
            return;
        }

        for (int i = 0; i < observedDummies.Length; i++)
        {
            if (observedDummies[i] != null)
                observedDummies[i].PlayerHitByPlayer += HandleDummyHit;
        }

        ReportProgress(BuildProgressText(0));
    }

    protected override void OnEndChecking()
    {
        if (observedDummies == null)
            return;

        for (int i = 0; i < observedDummies.Length; i++)
        {
            if (observedDummies[i] != null)
                observedDummies[i].PlayerHitByPlayer -= HandleDummyHit;
        }
    }

    void HandleDummyHit(TrainingDummyController dummy, TutorialCombatHitInfo info)
    {
        if (!IsRunning)
            return;

        if (IsDuplicateHit(info))
            return;

        if (Time.time - _lastHitTime <= fallbackWindow)
            _hitStreak++;
        else
            _hitStreak = 1;

        _lastHitTime = Time.time;

        int observedDepth = Mathf.Max(info.comboDepth, _hitStreak);
        string progress = BuildProgressText(observedDepth);
        ReportProgress(progress);

        if (info.comboDepth >= requiredComboDepth || _hitStreak >= fallbackHitStreak)
            Complete(progress);
    }

    string BuildProgressText(int currentDepth)
    {
        return $"\uc5f0\uc18d \ud0c0\uaca9 {currentDepth}/{requiredComboDepth}";
    }

    bool IsDuplicateHit(TutorialCombatHitInfo info)
    {
        int attackSequenceId = info.payload.attackSequenceId;
        int attackerInstanceId = info.payload.attacker != null ? info.payload.attacker.root.GetInstanceID() : 0;
        if (attackSequenceId <= 0)
            return false;

        if (attackSequenceId == _lastAttackSequenceId && attackerInstanceId == _lastAttackerInstanceId)
            return true;

        _lastAttackSequenceId = attackSequenceId;
        _lastAttackerInstanceId = attackerInstanceId;
        return false;
    }
}

[DisallowMultipleComponent]
public class TutorialGuardConditionChecker : TutorialConditionChecker
{
    [SerializeField] private TutorialPlayerRuntimeBridge playerBridge;
    [SerializeField] private int requiredGuardCount = 1;

    int _guardCount;

    public void ConfigureRuntime(TutorialPlayerRuntimeBridge bridge, int targetCount)
    {
        playerBridge = bridge;
        requiredGuardCount = targetCount;
    }

    protected override void OnBeginChecking(TutorialStepDefinition step)
    {
        _guardCount = 0;
        if (playerBridge == null)
        {
            Complete();
            return;
        }

        playerBridge.GuardBlocked += HandleGuardBlocked;
        ReportProgress(BuildProgressText());
    }

    protected override void OnEndChecking()
    {
        if (playerBridge != null)
            playerBridge.GuardBlocked -= HandleGuardBlocked;
    }

    void HandleGuardBlocked()
    {
        _guardCount = Mathf.Min(requiredGuardCount, _guardCount + 1);
        string progress = BuildProgressText();
        ReportProgress(progress);

        if (_guardCount >= requiredGuardCount)
            Complete(progress);
    }

    string BuildProgressText()
    {
        return $"\ubc29\uc5b4 {_guardCount}/{requiredGuardCount}";
    }
}

[DisallowMultipleComponent]
public class TutorialParryConditionChecker : TutorialConditionChecker
{
    [SerializeField] private TutorialPlayerRuntimeBridge playerBridge;
    [SerializeField] private int requiredParryCount = 1;

    int _parryCount;

    public void ConfigureRuntime(TutorialPlayerRuntimeBridge bridge, int targetCount)
    {
        playerBridge = bridge;
        requiredParryCount = targetCount;
    }

    protected override void OnBeginChecking(TutorialStepDefinition step)
    {
        _parryCount = 0;
        if (playerBridge == null)
        {
            Complete();
            return;
        }

        playerBridge.ParrySucceeded += HandleParrySucceeded;
        ReportProgress(BuildProgressText());
    }

    protected override void OnEndChecking()
    {
        if (playerBridge != null)
            playerBridge.ParrySucceeded -= HandleParrySucceeded;
    }

    void HandleParrySucceeded()
    {
        _parryCount = Mathf.Min(requiredParryCount, _parryCount + 1);
        string progress = BuildProgressText();
        ReportProgress(progress);

        if (_parryCount >= requiredParryCount)
            Complete(progress);
    }

    string BuildProgressText()
    {
        return $"\ud328\ub9c1 {_parryCount}/{requiredParryCount}";
    }
}

[DisallowMultipleComponent]
public class TutorialDodgeConditionChecker : TutorialConditionChecker
{
    [SerializeField] private TrainingDummyController[] observedDummies;
    [SerializeField] private int requiredDodges = 1;
    [SerializeField] private bool acceptPerfectDodgeAsSuccess;

    int _dodgeCount;

    public void ConfigureRuntime(TrainingDummyController[] dummies, int targetCount, bool acceptPerfect)
    {
        observedDummies = dummies;
        requiredDodges = targetCount;
        acceptPerfectDodgeAsSuccess = acceptPerfect;
    }

    protected override void OnBeginChecking(TutorialStepDefinition step)
    {
        _dodgeCount = 0;
        if (observedDummies == null || observedDummies.Length == 0)
        {
            Complete();
            return;
        }

        for (int i = 0; i < observedDummies.Length; i++)
        {
            if (observedDummies[i] != null)
                observedDummies[i].AttackResolved += HandleAttackResolved;
        }

        ReportProgress(BuildProgressText());
    }

    protected override void OnEndChecking()
    {
        if (observedDummies == null)
            return;

        for (int i = 0; i < observedDummies.Length; i++)
        {
            if (observedDummies[i] != null)
                observedDummies[i].AttackResolved -= HandleAttackResolved;
        }
    }

    void HandleAttackResolved(TrainingDummyController dummy, TrainingDummyAttackResult result)
    {
        bool counted = result == TrainingDummyAttackResult.Dodged ||
                       (acceptPerfectDodgeAsSuccess && result == TrainingDummyAttackResult.PerfectDodged);
        if (!counted)
            return;

        _dodgeCount = Mathf.Min(requiredDodges, _dodgeCount + 1);
        string progress = BuildProgressText();
        ReportProgress(progress);

        if (_dodgeCount >= requiredDodges)
            Complete(progress);
    }

    string BuildProgressText()
    {
        return $"\ud68c\ud53c {_dodgeCount}/{requiredDodges}";
    }
}

[DisallowMultipleComponent]
public class TutorialPerfectDodgeConditionChecker : TutorialConditionChecker
{
    [SerializeField] private TutorialPlayerRuntimeBridge playerBridge;
    [SerializeField] private int requiredPerfectDodges = 1;

    int _perfectCount;

    public void ConfigureRuntime(TutorialPlayerRuntimeBridge bridge, int targetCount)
    {
        playerBridge = bridge;
        requiredPerfectDodges = targetCount;
    }

    protected override void OnBeginChecking(TutorialStepDefinition step)
    {
        _perfectCount = 0;
        if (playerBridge == null)
        {
            Complete();
            return;
        }

        playerBridge.PerfectDodged += HandlePerfectDodged;
        ReportProgress(BuildProgressText());
    }

    protected override void OnEndChecking()
    {
        if (playerBridge != null)
            playerBridge.PerfectDodged -= HandlePerfectDodged;
    }

    void HandlePerfectDodged()
    {
        _perfectCount = Mathf.Min(requiredPerfectDodges, _perfectCount + 1);
        string progress = BuildProgressText();
        ReportProgress(progress);

        if (_perfectCount >= requiredPerfectDodges)
            Complete(progress);
    }

    string BuildProgressText()
    {
        return $"\ud37c\ud399\ud2b8 \ud68c\ud53c {_perfectCount}/{requiredPerfectDodges}";
    }
}

[DisallowMultipleComponent]
public class TutorialHealConditionChecker : TutorialConditionChecker
{
    [SerializeField] private TutorialPlayerRuntimeBridge playerBridge;
    [SerializeField, Range(0.05f, 0.95f)] private float normalizedHealthTarget = 0.42f;

    public void ConfigureRuntime(TutorialPlayerRuntimeBridge bridge, float targetHealthRatio)
    {
        playerBridge = bridge;
        normalizedHealthTarget = targetHealthRatio;
    }

    protected override void OnBeginChecking(TutorialStepDefinition step)
    {
        if (playerBridge == null)
        {
            Complete();
            return;
        }

        playerBridge.EnsureAmpouleAvailable();
        playerBridge.ReduceHealthForTutorial(normalizedHealthTarget);
        playerBridge.AmpouleUsed += HandleAmpouleUsed;
    }

    protected override void OnEndChecking()
    {
        if (playerBridge != null)
            playerBridge.AmpouleUsed -= HandleAmpouleUsed;
    }

    void HandleAmpouleUsed()
    {
        Complete("\ud68c\ubcf5 \uc644\ub8cc");
    }
}

[DisallowMultipleComponent]
public class TutorialUltimateConditionChecker : TutorialConditionChecker
{
    [SerializeField] private TutorialPlayerRuntimeBridge playerBridge;
    [SerializeField] private UltimateTargetSimple ultimateTarget;
    bool _ultimateStarted;
    bool _ultimateDamageApplied;

    public void ConfigureRuntime(TutorialPlayerRuntimeBridge bridge, UltimateTargetSimple target = null)
    {
        playerBridge = bridge;
        ultimateTarget = target;
    }

    protected override void OnBeginChecking(TutorialStepDefinition step)
    {
        if (playerBridge == null)
        {
            Complete();
            return;
        }

        _ultimateStarted = false;
        _ultimateDamageApplied = false;
        playerBridge.FillUltimateGauge();
        playerBridge.UltimateStarted += HandleUltimateStarted;
        playerBridge.UltimateEnded += HandleUltimateEnded;
        if (ultimateTarget != null && ultimateTarget.OnUltimateDamageApplied != null)
            ultimateTarget.OnUltimateDamageApplied.AddListener(HandleUltimateDamageApplied);
    }

    protected override void OnEndChecking()
    {
        if (playerBridge != null)
        {
            playerBridge.UltimateStarted -= HandleUltimateStarted;
            playerBridge.UltimateEnded -= HandleUltimateEnded;
        }

        if (ultimateTarget != null && ultimateTarget.OnUltimateDamageApplied != null)
            ultimateTarget.OnUltimateDamageApplied.RemoveListener(HandleUltimateDamageApplied);
    }

    void HandleUltimateStarted()
    {
        _ultimateStarted = true;
        ReportProgress("\uad81\uadf9\uae30 \uc5f0\ucd9c \uc9c4\ud589 \uc911");
    }

    void HandleUltimateEnded()
    {
        _ultimateStarted = true;
        Complete("\uad81\uadf9\uae30 \uc644\ub8cc");
    }

    void HandleUltimateDamageApplied(int _)
    {
        _ultimateDamageApplied = true;
        if (_ultimateStarted)
            ReportProgress("\uad81\uadf9\uae30 \ud0c0\uaca9 \ud655\uc778");
    }
}

[DisallowMultipleComponent]
public class TutorialZoneConditionChecker : TutorialConditionChecker
{
    [SerializeField] private TutorialZoneTrigger zoneTrigger;

    public void ConfigureRuntime(TutorialZoneTrigger zone)
    {
        zoneTrigger = zone;
    }

    protected override void OnBeginChecking(TutorialStepDefinition step)
    {
        if (zoneTrigger == null)
        {
            Complete();
            return;
        }

        zoneTrigger.ResetTriggerState();
        zoneTrigger.TriggerEntered += HandleZoneEntered;
    }

    protected override void OnEndChecking()
    {
        if (zoneTrigger != null)
            zoneTrigger.TriggerEntered -= HandleZoneEntered;
    }

    void HandleZoneEntered(TutorialZoneTrigger zone, Collider other)
    {
        Complete();
    }
}
