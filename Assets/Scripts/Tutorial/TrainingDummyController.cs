using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
public class TrainingDummyController : MonoBehaviour, IDamageReceiver
{
    [Header("Role")]
    [SerializeField] private TutorialDummyRole role = TutorialDummyRole.PassiveTarget;
    [SerializeField] private TrainingDummyStepProfile[] stepProfiles;

    [Header("References")]
    [SerializeField] private TutorialPlayerRuntimeBridge playerBridge;
    [SerializeField] private Transform playerTarget;
    [SerializeField] private Transform attackOrigin;
    [SerializeField] private Renderer primaryRenderer;
    [SerializeField] private GameObject parryMarkerObject;
    [SerializeField] private TutorialWorldMarker worldMarker;

    [Header("Projectile")]
    [SerializeField] private GameObject projectilePrefab;
    [SerializeField] private Transform projectileSpawnPoint;
    [SerializeField, Min(0)] private int projectilePrewarmCount = 2;
    [SerializeField, Min(0f)] private float playerAimHeight = 1.1f;

    [Header("Danger Indicator")]
    [SerializeField] private bool autoCreateDangerIndicator = true;
    [SerializeField] private LineRenderer dangerIndicatorLine;
    [SerializeField] private Transform dangerTargetMarker;
    [SerializeField] private MeshRenderer dangerTargetMarkerRenderer;
    [SerializeField, Min(0.01f)] private float dangerTargetMarkerHeight = 0.03f;
    [SerializeField, Min(0f)] private float dangerTargetMarkerOffsetY = 0.03f;

    [Header("Visual")]
    [SerializeField] private GameObject hitEffectPrefab;
    [SerializeField] private Animator visualAnimator;
    [SerializeField] private string idleAnimationState = "Idle";
    [SerializeField] private string moveAnimationState = "Run";
    [SerializeField] private string attackAnimationState = "Attack1";
    [SerializeField] private string hitAnimationState = "Hit1";
    [SerializeField] private string dieAnimationState = "Die";
    [SerializeField, Range(0f, 0.25f)] private float animationCrossFadeSeconds = 0.05f;
    [SerializeField] private string emissionProperty = "_EmissionColor";
    [SerializeField] private Color idleEmission = Color.black;
    [SerializeField] private Color hitEmission = new Color(1f, 0.85f, 0.2f, 1f);
    [SerializeField, Range(0f, 0.25f)] private float hitScalePunch = 0.08f;
    [SerializeField, Min(0.05f)] private float hitReactionDuration = 0.14f;

    [Header("Spawn Hologram")]
    [SerializeField] private bool useHologramSpawnDespawn = true;
    [SerializeField] private Material hologramTransitionMaterial;
    [SerializeField] private string hologramMaterialResourcePath = "Tutorial/TutorialHologramSpawn";
    [SerializeField, Min(0f)] private float hologramSpawnDuration = 0.55f;
    [SerializeField, Min(0f)] private float hologramDespawnDuration = 0.45f;
    [SerializeField] private Color hologramTransitionColor = new Color(0f, 0.65f, 1f, 0.34f);

    [Header("Defense Reaction")]
    [SerializeField] private Color guardedReactionColor = new Color(0.28f, 0.90f, 1f, 1f);
    [SerializeField] private Color parriedReactionColor = new Color(0.62f, 1f, 1f, 1f);
    [SerializeField] private Color dodgedReactionColor = new Color(1f, 0.62f, 0.20f, 1f);
    [SerializeField] private Color perfectDodgeReactionColor = new Color(1f, 0.36f, 0.18f, 1f);
    [SerializeField, Range(0f, 0.18f)] private float guardedScalePunch = 0.04f;
    [SerializeField, Range(0f, 0.18f)] private float dodgedScalePunch = 0.06f;
    [SerializeField, Range(0f, 0.32f)] private float parriedScalePunch = 0.14f;
    [SerializeField, Range(0f, 0.36f)] private float perfectDodgeScalePunch = 0.18f;
    [SerializeField, Min(0.05f)] private float guardedReactionDuration = 0.12f;
    [SerializeField, Min(0.05f)] private float dodgedReactionDuration = 0.16f;
    [SerializeField, Min(0.05f)] private float parriedReactionDuration = 0.22f;
    [SerializeField, Min(0.05f)] private float perfectDodgeReactionDuration = 0.28f;
    [SerializeField, Min(0f)] private float guardedRecoilDistance = 0.05f;
    [SerializeField, Min(0f)] private float dodgedRecoilDistance = 0.08f;
    [SerializeField, Min(0f)] private float parriedRecoilDistance = 0.22f;
    [SerializeField, Min(0f)] private float perfectDodgeRecoilDistance = 0.30f;
    [SerializeField, Min(0f)] private float guardedRecoveryDelay = 0.08f;
    [SerializeField, Min(0f)] private float dodgedRecoveryDelay = 0.10f;
    [SerializeField, Min(0f)] private float parriedRecoveryDelay = 0.28f;
    [SerializeField, Min(0f)] private float perfectDodgeRecoveryDelay = 0.36f;

    [Header("Melee Tracking")]
    [SerializeField] private bool trackPlayerDuringLoopAttack = true;
    [SerializeField, Min(0.5f)] private float desiredMeleeDistance = 2.3f;
    [SerializeField, Min(0f)] private float chaseMoveSpeed = 1.8f;
    [SerializeField, Min(0f)] private float chaseTurnSpeed = 540f;
    [SerializeField, Min(0f)] private float maxChaseBeforeAttackSeconds = 1.2f;
    [SerializeField] private AttackHitbox meleeAttackHitbox;
    [SerializeField, Min(0f)] private float meleeHitDelay = 0.22f;
    [SerializeField, Min(0.02f)] private float meleeHitWindow = 0.32f;
    [SerializeField] private bool useAnimationHitTiming = true;
    [SerializeField, Range(0f, 1f)] private float meleeHitNormalizedTime = 0.16f;
    [SerializeField, Min(0.1f)] private float maxAnimationHitWaitSeconds = 2.2f;
    [SerializeField] private Vector3 meleeHitboxCenter = new Vector3(0f, 1.05f, 0.95f);
    [SerializeField] private Vector3 meleeHitboxSize = new Vector3(1.15f, 1.35f, 1.15f);

    [Header("Attack Lunge")]
    [SerializeField] private bool useAttackLunge = true;
    [SerializeField, Range(0f, 1f)] private float attackLungeStartNormalizedTime = 0.04f;
    [SerializeField, Range(0f, 1f)] private float attackLungeEndNormalizedTime = 0.30f;
    [SerializeField, Min(0f)] private float attackLungeSpeed = 15f;
    [SerializeField, Min(0.2f)] private float attackLungeStopDistance = 1.25f;
    [SerializeField, Min(0f)] private float attackLungeMaxDistance = 7.5f;

    [Header("Parry Timing Assist")]
    [SerializeField] private bool enableParryTimingAssist = true;
    [SerializeField, Min(0f)] private float parryAssistLeadSeconds = 0.05f;
    [SerializeField, Range(0.05f, 1f)] private float parryAssistTimeScale = 0.35f;
    [SerializeField, Min(0.05f)] private float parryAssistDuration = 0.85f;
    [SerializeField] private string parryAssistSpeaker = "PARRY WINDOW";
    [SerializeField] private string parryAssistText = "<color=#66F6FF>Parry Now</color>  |  E";
    [SerializeField] private string dodgeAssistSpeaker = "DODGE WINDOW";
    [SerializeField] private string dodgeAssistText = "<color=#66F6FF>Shift</color>";

    [Header("Forced Parry Tutorial")]
    [SerializeField] private bool freezeForFirstParrySuccess = true;
    [SerializeField, Range(0f, 1f)] private float forcedParryFreezeNormalizedTime = 0.12f;
    [SerializeField, Range(0f, 1f)] private float forcedParryTimeScale = 0.02f;
    [SerializeField, Min(0.05f)] private float forcedParryWindowSeconds = 0.75f;
    [SerializeField] private string forcedParrySpeaker = "PARRY TRAINING";
    [SerializeField] private string forcedParryText = "<color=#66F6FF>E</color>  패링";

    [Header("Forced Perfect Dodge Tutorial")]
    [SerializeField] private bool freezeForFirstPerfectDodgeSuccess = true;
    [SerializeField, Range(0f, 1f)] private float forcedPerfectDodgeFreezeNormalizedTime = 0.12f;
    [SerializeField, Min(0.05f)] private float forcedPerfectDodgeWindowSeconds = 0.75f;

    [Header("Hit Tracking")]
    [SerializeField, Min(0f)] private float hitEventCooldown = 0.08f;
    [SerializeField] private bool debugLogs;

    [Header("Static Target")]
    [SerializeField] private bool lockWorldPose;

    float _lastHitTime = float.NegativeInfinity;
    int _lastAttackSequenceId;
    int _lastAttackerInstanceId;
    float _currentHealth;
    Coroutine _attackLoop;
    Coroutine _hitReaction;
    Coroutine _defenseReaction;
    string _currentVisualState;
    Vector3 _initialScale;
    MaterialPropertyBlock _propertyBlock;
    MaterialPropertyBlock _markerPropertyBlock;
    Coroutine _hologramTransitionRoutine;
    Renderer[] _hologramRenderers;
    Material[][] _hologramOriginalMaterials;
    Material _runtimeHologramMaterial;
    TrainingDummyStepProfile _activeProfile;
    bool _hasActiveProfile;
    int _adaptiveFailureCount;
    float _attackResumeRealtime;
    TutorialHintUIBridge _hintBridge;
    Coroutine _localParrySlowMotion;
    float _localSlowPreviousScale = 1f;
    float _localSlowPreviousFixedDeltaTime = 0.02f;
    bool _localSlowMotionActive;
    float _attackLungeMovedDistance;
    bool _forcedParryPromptActive;
    bool _forcedPerfectDodgePromptActive;
    bool _forcedParrySucceeded;
    bool _hasLockedWorldPose;
    Vector3 _lockedWorldPosition;
    Quaternion _lockedWorldRotation;
    float _forcedParryPreviousTimeScale = 1f;
    float _forcedParryPreviousFixedDeltaTime = 0.02f;
    float _forcedParryPreviousAnimatorSpeed = 1f;
    readonly Dictionary<TutorialProjectile, AttackSnapshot> _projectileSnapshots = new Dictionary<TutorialProjectile, AttackSnapshot>();

    struct AttackSnapshot
    {
        public int beforeGuard;
        public int beforeParry;
        public int beforePerfect;
        public int beforeDamage;
        public int beforeHp;
        public bool treatMissAsDodge;
    }

    static Material s_dangerIndicatorMaterial;
    static Material s_dangerTargetMarkerMaterial;
    static readonly int HologramColorId = Shader.PropertyToID("_Hologram_Color");
    static readonly int TextureTintColorId = Shader.PropertyToID("_Texture_Tint_Color");

    public event System.Action<TrainingDummyController, TutorialCombatHitInfo> PlayerHitByPlayer;
    public event System.Action<TrainingDummyController, TrainingDummyAttackResult> AttackResolved;

    void Awake()
    {
        if (attackOrigin == null)
            attackOrigin = transform;
        if (primaryRenderer == null)
            primaryRenderer = GetComponentInChildren<Renderer>(true);
        if (worldMarker == null)
            worldMarker = GetComponentInChildren<TutorialWorldMarker>(true);
        if (visualAnimator == null)
            visualAnimator = GetComponentInChildren<Animator>(true);
        if (projectileSpawnPoint == null)
            projectileSpawnPoint = attackOrigin != null ? attackOrigin : transform;

        _initialScale = transform.localScale;
        _propertyBlock = new MaterialPropertyBlock();
        _markerPropertyBlock = new MaterialPropertyBlock();
        EnsureDangerIndicator();
        EnsureDangerTargetMarker();
        EnsureMeleeAttackHitbox();
        SetEmission(idleEmission);
    }

    void OnDisable()
    {
        StopHologramTransition(true);
        StopVisualReactions();
        HideDangerIndicator();
        HideDangerTargetMarker();
        if (worldMarker != null)
            worldMarker.SetVisible(false);
        RestoreLocalSlowMotionIfNeeded();
        RestoreForcedParryFreezeIfNeeded();
        ClearPendingProjectiles();
    }

    void LateUpdate()
    {
        if (lockWorldPose && _hasLockedWorldPose)
            transform.SetPositionAndRotation(_lockedWorldPosition, _lockedWorldRotation);
    }

    public void ConfigureRuntime(
        TutorialPlayerRuntimeBridge bridge,
        TutorialDummyRole dummyRole,
        Transform runtimeAttackOrigin,
        Transform targetTransform,
        Renderer renderer,
        GameObject parryMarker)
    {
        playerBridge = bridge;
        role = dummyRole;
        attackOrigin = runtimeAttackOrigin != null ? runtimeAttackOrigin : transform;
        playerTarget = targetTransform;
        primaryRenderer = renderer != null ? renderer : primaryRenderer;
        parryMarkerObject = parryMarker;
        projectileSpawnPoint = attackOrigin;
        useHologramSpawnDespawn = true;
        if (primaryRenderer == null)
            primaryRenderer = GetComponentInChildren<Renderer>(true);
        if (visualAnimator == null)
            visualAnimator = GetComponentInChildren<Animator>(true);
        if (worldMarker == null)
            worldMarker = GetComponentInChildren<TutorialWorldMarker>(true);
        _initialScale = transform.localScale;
        EnsureDangerIndicator();
        EnsureDangerTargetMarker();
        EnsureMeleeAttackHitbox();
        SetEmission(idleEmission);
    }

    public void ConfigureStaticWorldPose(bool enabled)
    {
        lockWorldPose = enabled;
        if (!lockWorldPose)
        {
            _hasLockedWorldPose = false;
            return;
        }

        _lockedWorldPosition = transform.position;
        _lockedWorldRotation = transform.rotation;
        _hasLockedWorldPose = true;
        trackPlayerDuringLoopAttack = false;
        useAttackLunge = false;
        chaseMoveSpeed = 0f;
        chaseTurnSpeed = 0f;
        if (visualAnimator == null)
            visualAnimator = GetComponentInChildren<Animator>(true);
        if (visualAnimator != null)
        {
            visualAnimator.applyRootMotion = false;
            visualAnimator.speed = 0f;
            visualAnimator.enabled = false;
        }
    }

    public void ConfigureHitEffectRuntime(GameObject runtimeHitEffectPrefab)
    {
        if (runtimeHitEffectPrefab != null)
            hitEffectPrefab = runtimeHitEffectPrefab;
    }

    public void ConfigureWorldMarker(TutorialWorldMarker runtimeMarker)
    {
        worldMarker = runtimeMarker;
    }

    public void ConfigureProjectileRuntime(GameObject runtimeProjectilePrefab, Transform runtimeSpawnPoint)
    {
        projectilePrefab = runtimeProjectilePrefab;
        if (runtimeSpawnPoint != null)
            projectileSpawnPoint = runtimeSpawnPoint;

        if (projectilePrefab != null && projectilePrewarmCount > 0)
            RuntimeObjectPool.Prewarm(projectilePrefab, projectilePrewarmCount);
    }

    void EnsureMeleeAttackHitbox()
    {
        if (meleeAttackHitbox != null)
            return;

        Transform existing = transform.Find("TutorialMeleeAttackHitbox");
        GameObject hitboxObject = existing != null
            ? existing.gameObject
            : new GameObject("TutorialMeleeAttackHitbox");
        hitboxObject.transform.SetParent(transform, false);
        hitboxObject.transform.localPosition = Vector3.zero;
        hitboxObject.transform.localRotation = Quaternion.identity;
        hitboxObject.transform.localScale = Vector3.one;

        BoxCollider box = hitboxObject.GetComponent<BoxCollider>();
        if (box == null)
            box = hitboxObject.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.center = meleeHitboxCenter;
        box.size = meleeHitboxSize;
        box.enabled = false;

        meleeAttackHitbox = hitboxObject.GetComponent<AttackHitbox>();
        if (meleeAttackHitbox == null)
            meleeAttackHitbox = hitboxObject.AddComponent<AttackHitbox>();

        meleeAttackHitbox.attackerRoot = transform;
        meleeAttackHitbox.hitEachReceiverOncePerActivation = true;
        meleeAttackHitbox.useOneShotWindow = true;
        meleeAttackHitbox.oneShotWindow = meleeHitWindow;
        meleeAttackHitbox.useExpandedHitDetection = true;
        meleeAttackHitbox.expandedPadding = 0.08f;
        meleeAttackHitbox.useSweepHitDetection = true;
        if (playerTarget != null)
            meleeAttackHitbox.hitLayers = 1 << playerTarget.gameObject.layer;

        meleeAttackHitbox.DeactivateWindow();
    }

    public void SetRuntimeProfiles(params TrainingDummyStepProfile[] profiles)
    {
        stepProfiles = profiles;
    }

    public void ApplyStep(TutorialStepType stepType)
    {
        bool wasActiveProfile = _hasActiveProfile;
        bool wasActiveInHierarchy = gameObject.activeInHierarchy;
        _activeProfile = null;
        _hasActiveProfile = false;
        _adaptiveFailureCount = 0;
        _attackResumeRealtime = 0f;
        _lastAttackSequenceId = 0;
        _lastAttackerInstanceId = 0;
        _lastHitTime = float.NegativeInfinity;
        RestoreForcedParryFreezeIfNeeded();
        _forcedParrySucceeded = playerBridge != null && playerBridge.ParryCount > 0;
        _forcedParryPromptActive = false;
        _forcedPerfectDodgePromptActive = false;

        if (stepProfiles != null)
        {
            for (int i = 0; i < stepProfiles.Length; i++)
            {
                if (stepProfiles[i] == null || stepProfiles[i].stepType != stepType)
                    continue;

                _activeProfile = stepProfiles[i];
                _hasActiveProfile = _activeProfile.active;
                break;
            }
        }

        StopAttackLoop();
        StopVisualReactions();
        HideDangerIndicator();
        HideDangerTargetMarker();
        ClearPendingProjectiles();

        if (!_hasActiveProfile || _activeProfile == null)
        {
            if (wasActiveProfile && wasActiveInHierarchy)
                StartHologramDespawnOrDisable();
            else
            {
                StopHologramTransition(true);
                gameObject.SetActive(false);
            }
            return;
        }

        gameObject.SetActive(true);
        if (!wasActiveProfile || !wasActiveInHierarchy)
            StartHologramSpawn();
        else
            StopHologramTransition(true);

        PlayIdleAnimation();
        _currentHealth = _activeProfile.useHealth ? Mathf.Max(1f, _activeProfile.damage * 6f) : 99999f;
        SetEmission(_activeProfile.stateColor);
        if (parryMarkerObject != null)
            parryMarkerObject.SetActive(_activeProfile.canParry);
        if (worldMarker != null)
        {
            worldMarker.SetMarkerColor(_activeProfile.stateColor);
            worldMarker.SetVisible(true);
        }

        if (_activeProfile.loopAttack)
            _attackLoop = StartCoroutine(CoAttackLoop(stepType));
    }

    public void ReceiveHit(HitPayload payload)
    {
        if (!_hasActiveProfile)
            return;

        int attackerInstanceId = payload.attacker != null ? payload.attacker.root.GetInstanceID() : 0;
        bool hasAttackSequence = payload.attackSequenceId > 0;
        if (hasAttackSequence &&
            payload.attackSequenceId == _lastAttackSequenceId &&
            attackerInstanceId == _lastAttackerInstanceId)
        {
            return;
        }

        if (!hasAttackSequence && Time.time - _lastHitTime < hitEventCooldown)
            return;

        _lastHitTime = Time.time;
        if (hasAttackSequence)
        {
            _lastAttackSequenceId = payload.attackSequenceId;
            _lastAttackerInstanceId = attackerInstanceId;
        }

        CombatRewardUtility.TryGrantBasicAttackGauge(payload.attacker);
        PlayHitAnimation();
        PlayHitReaction();
        SpawnHitEffect(payload);

        if (_activeProfile != null && _activeProfile.useHealth && !_activeProfile.invulnerable)
        {
            _currentHealth = Mathf.Max(0f, _currentHealth - payload.damage);
            if (_currentHealth <= 0f)
                PlayDieAnimation();
        }

        TutorialCombatHitInfo hitInfo = default;
        hitInfo.payload = payload;

        if (playerBridge != null)
            playerBridge.ResolveAttackInfo(payload.attacker, out hitInfo.attackKind, out hitInfo.comboDepth);

        PlayerHitByPlayer?.Invoke(this, hitInfo);

        if (debugLogs)
            Debug.Log($"[TrainingDummyController] {name} hit. role={role} kind={hitInfo.attackKind} combo={hitInfo.comboDepth}", this);
    }

    void SpawnHitEffect(HitPayload payload)
    {
        if (hitEffectPrefab == null)
            return;

        Quaternion rotation = payload.hitDirection.sqrMagnitude > 0.0001f
            ? Quaternion.LookRotation(payload.hitDirection, Vector3.up)
            : Quaternion.identity;
        TransientVfxPool.Spawn(hitEffectPrefab, payload.hitPoint, rotation, null, 1.2f);
    }

    IEnumerator CoAttackLoop(TutorialStepType stepType)
    {
        yield return new WaitForSeconds(_activeProfile.initialDelay);

        while (_hasActiveProfile && _activeProfile != null && _activeProfile.stepType == stepType)
        {
            while (_hasActiveProfile && Time.realtimeSinceStartup < _attackResumeRealtime)
                yield return null;

            yield return CoApproachPlayerForMelee();
            SetEmission(_activeProfile.canParry ? hitEmission : _activeProfile.stateColor);
            ShowDangerIndicator(GetAttackTargetPoint(), _activeProfile);
            yield return CoTelegraphDelay(GetAdjustedTelegraphDuration());
            yield return CoExecuteAttackWindow(GetAttackTargetPoint());
            PlayIdleAnimation();
            SetEmission(_activeProfile.stateColor);
            HideDangerIndicator();
            HideDangerTargetMarker();
            yield return new WaitForSeconds(_activeProfile.attackInterval);
        }
    }

    void ExecuteAttack(Vector3 lockedTargetPoint)
    {
        if (playerTarget == null || playerBridge == null)
        {
            AttackResolved?.Invoke(this, TrainingDummyAttackResult.Missed);
            return;
        }

        AttackSnapshot snapshot = CaptureAttackSnapshot();

        if (_activeProfile.useProjectileAttack && TryFireProjectile(snapshot, lockedTargetPoint))
            return;

        StartCoroutine(CoApplyMeleeHitboxAttack(snapshot));
        // 실제 방어/패링/퍼펙트 회피 판정은 기존 PlayerDamageReceiver 쪽에 맡기고,
        // 더미는 공격 시점 전후의 플레이어 이벤트 변화만 읽어 결과를 분류한다.
        // 기존 전투 시스템 이벤트를 우선 신뢰하고, 아무 이벤트가 없을 때만 회피 추정으로 떨어진다.
    }

    IEnumerator CoExecuteAttackWindow(Vector3 lockedTargetPoint)
    {
        if (playerTarget == null || playerBridge == null)
        {
            AttackResolved?.Invoke(this, TrainingDummyAttackResult.Missed);
            yield break;
        }

        AttackSnapshot snapshot = CaptureAttackSnapshot();

        if (_activeProfile.useProjectileAttack && TryFireProjectile(snapshot, lockedTargetPoint))
            yield break;

        PlayAttackAnimation();
        FacePlayerForMelee();
        _attackLungeMovedDistance = 0f;
        yield return CoWaitForMeleeHitTimingWithAssist();

        yield return CoApplyMeleeHitboxAttack(snapshot);
    }

    IEnumerator CoWaitForMeleeHitTimingWithAssist()
    {
        if (!useAnimationHitTiming || visualAnimator == null || string.IsNullOrWhiteSpace(attackAnimationState))
        {
            yield return CoWaitForMeleeHitDelayWithAssist();
            yield break;
        }

        int attackStateHash = Animator.StringToHash(attackAnimationState);
        bool assistTriggered = false;
        bool sawAttackState = false;
        bool forcedFreezeHandled = false;
        float elapsed = 0f;
        float hitNormalized = Mathf.Clamp01(meleeHitNormalizedTime);
        float forcedFreezeNormalized = Mathf.Clamp01(Mathf.Min(forcedParryFreezeNormalizedTime, hitNormalized));
        float forcedPerfectDodgeNormalized = Mathf.Clamp01(Mathf.Min(forcedPerfectDodgeFreezeNormalizedTime, hitNormalized));
        yield return null;

        while (_hasActiveProfile && elapsed < maxAnimationHitWaitSeconds)
        {
            AnimatorStateInfo stateInfo = visualAnimator.GetCurrentAnimatorStateInfo(0);
            bool inAttackState = stateInfo.shortNameHash == attackStateHash;
            if (!inAttackState && visualAnimator.IsInTransition(0))
            {
                AnimatorStateInfo nextStateInfo = visualAnimator.GetNextAnimatorStateInfo(0);
                if (nextStateInfo.shortNameHash == attackStateHash)
                {
                    stateInfo = nextStateInfo;
                    inAttackState = true;
                }
            }

            if (inAttackState)
            {
                sawAttackState = true;
                float normalizedTime = Mathf.Clamp01(stateInfo.normalizedTime);
                float stateLength = Mathf.Max(0.01f, stateInfo.length);
                float assistNormalizedTime = Mathf.Clamp01(hitNormalized - (parryAssistLeadSeconds / stateLength));

                UpdateAttackLunge(normalizedTime);

                if (!forcedFreezeHandled && ShouldUseForcedParryFreeze() && normalizedTime >= forcedFreezeNormalized)
                {
                    forcedFreezeHandled = true;
                    yield return CoForcedParryPrompt();
                    yield break;
                }

                if (!forcedFreezeHandled && ShouldUseForcedPerfectDodgeFreeze() && normalizedTime >= forcedPerfectDodgeNormalized)
                {
                    forcedFreezeHandled = true;
                    yield return CoForcedPerfectDodgePrompt();
                    yield break;
                }

                if (!assistTriggered && ShouldShowDefenseTimingAssist() && normalizedTime >= assistNormalizedTime)
                {
                    TriggerDefenseTimingAssist();
                    assistTriggered = true;
                }

                if (normalizedTime >= hitNormalized)
                    yield break;
            }
            else if (sawAttackState)
            {
                break;
            }

            elapsed += Time.deltaTime;
            yield return null;
        }

        if (sawAttackState)
            yield break;

        yield return CoWaitForMeleeHitDelayWithAssist();
    }

    void UpdateAttackLunge(float normalizedTime)
    {
        if (lockWorldPose || !useAttackLunge || playerTarget == null || attackLungeSpeed <= 0f)
            return;

        float start = Mathf.Clamp01(attackLungeStartNormalizedTime);
        float end = Mathf.Clamp01(Mathf.Max(start, attackLungeEndNormalizedTime));
        if (normalizedTime < start || normalizedTime > end)
            return;

        if (attackLungeMaxDistance > 0f && _attackLungeMovedDistance >= attackLungeMaxDistance)
            return;

        Vector3 toPlayer = playerTarget.position - transform.position;
        toPlayer.y = 0f;
        float distance = toPlayer.magnitude;
        if (distance <= attackLungeStopDistance || distance <= 0.001f)
            return;

        Vector3 direction = toPlayer / distance;
        RotateToward(direction);

        float remainingDistance = Mathf.Max(0f, distance - attackLungeStopDistance);
        float maxByLungeBudget = attackLungeMaxDistance > 0f
            ? Mathf.Max(0f, attackLungeMaxDistance - _attackLungeMovedDistance)
            : remainingDistance;
        float step = Mathf.Min(remainingDistance, maxByLungeBudget, attackLungeSpeed * Time.deltaTime);
        transform.position += direction * step;
        _attackLungeMovedDistance += step;
    }

    bool ShouldUseForcedParryFreeze()
    {
        return freezeForFirstParrySuccess &&
               !_forcedParrySucceeded &&
               playerBridge != null &&
               playerBridge.ParryCount <= 0 &&
               _activeProfile != null &&
               _activeProfile.stepType == TutorialStepType.Parry &&
               _activeProfile.canParry;
    }

    bool ShouldUseForcedPerfectDodgeFreeze()
    {
        return freezeForFirstPerfectDodgeSuccess &&
               playerBridge != null &&
               playerBridge.PerfectDodgeCount <= 0 &&
               _activeProfile != null &&
               _activeProfile.stepType == TutorialStepType.PerfectDodge &&
               _activeProfile.canPerfectDodge;
    }

    IEnumerator CoForcedParryPrompt()
    {
        _forcedParryPromptActive = true;
        TriggerForcedParryFreeze();

        TutorialHintUIBridge hintBridge = ResolveHintBridge();
        if (hintBridge != null)
            hintBridge.ShowTimingCue(forcedParryText, forcedParrySpeaker, 999f);

        while (_hasActiveProfile && playerBridge != null && playerBridge.ParryCount <= 0)
        {
            if (WasParryPromptPressed())
            {
                playerBridge.TryOpenTutorialParryWindow(forcedParryWindowSeconds);
                break;
            }

            yield return null;
        }

        hintBridge?.HideTimingCue();
        RestoreForcedParryFreezeIfNeeded();
        _forcedParryPromptActive = false;
    }

    IEnumerator CoForcedPerfectDodgePrompt()
    {
        _forcedPerfectDodgePromptActive = true;
        TriggerForcedParryFreeze();

        TutorialHintUIBridge hintBridge = ResolveHintBridge();
        if (hintBridge != null)
            hintBridge.ShowTimingCue(dodgeAssistText, dodgeAssistSpeaker, 999f);

        while (_hasActiveProfile && playerBridge != null && playerBridge.PerfectDodgeCount <= 0)
        {
            if (WasDodgePromptPressed())
            {
                playerBridge.TryOpenTutorialPerfectDodgeWindow(forcedPerfectDodgeWindowSeconds);
                break;
            }

            yield return null;
        }

        hintBridge?.HideTimingCue();
        RestoreForcedParryFreezeIfNeeded();
        _forcedPerfectDodgePromptActive = false;
    }

    void TriggerForcedParryFreeze()
    {
        _forcedParryPreviousTimeScale = Time.timeScale;
        _forcedParryPreviousFixedDeltaTime = Time.fixedDeltaTime;
        Time.timeScale = Mathf.Clamp(forcedParryTimeScale, 0f, 1f);
        Time.fixedDeltaTime = 0.02f * Mathf.Max(Time.timeScale, 0.0001f);

        if (visualAnimator != null)
        {
            _forcedParryPreviousAnimatorSpeed = visualAnimator.speed;
            visualAnimator.speed = 0f;
        }
    }

    static bool WasParryPromptPressed()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard.eKey.wasPressedThisFrame)
            return true;

#if ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetKeyDown(KeyCode.E);
#else
        return false;
#endif
    }

    static bool WasDodgePromptPressed()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null &&
            (keyboard.leftShiftKey.wasPressedThisFrame || keyboard.rightShiftKey.wasPressedThisFrame))
            return true;

#if ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetKeyDown(KeyCode.LeftShift) || Input.GetKeyDown(KeyCode.RightShift);
#else
        return false;
#endif
    }

    void RestoreForcedParryFreezeIfNeeded()
    {
        if (!_forcedParryPromptActive && !_forcedPerfectDodgePromptActive)
            return;

        Time.timeScale = _forcedParryPreviousTimeScale;
        Time.fixedDeltaTime = _forcedParryPreviousFixedDeltaTime;

        if (visualAnimator != null)
            visualAnimator.speed = _forcedParryPreviousAnimatorSpeed;
    }

    IEnumerator CoWaitForMeleeHitDelayWithAssist()
    {
        float delay = Mathf.Max(0f, meleeHitDelay);
        if (!ShouldShowDefenseTimingAssist())
        {
            if (delay > 0f)
                yield return new WaitForSeconds(delay);
            yield break;
        }

        float lead = Mathf.Clamp(parryAssistLeadSeconds, 0f, delay);
        float beforeCue = delay - lead;
        if (beforeCue > 0f)
            yield return new WaitForSeconds(beforeCue);

        TriggerDefenseTimingAssist();

        if (lead > 0f)
            yield return new WaitForSeconds(lead);
    }

    bool ShouldShowDefenseTimingAssist()
    {
        return ShouldShowParryTimingAssist() || ShouldShowPerfectDodgeTimingAssist();
    }

    bool ShouldShowParryTimingAssist()
    {
        return enableParryTimingAssist &&
               _activeProfile != null &&
               _activeProfile.stepType == TutorialStepType.Parry &&
               _activeProfile.canParry;
    }

    bool ShouldShowPerfectDodgeTimingAssist()
    {
        return enableParryTimingAssist &&
               _activeProfile != null &&
               _activeProfile.stepType == TutorialStepType.PerfectDodge &&
               _activeProfile.canPerfectDodge;
    }

    void TriggerDefenseTimingAssist()
    {
        TutorialHintUIBridge hintBridge = ResolveHintBridge();
        if (hintBridge != null)
        {
            if (ShouldShowPerfectDodgeTimingAssist())
                hintBridge.ShowTimingCue(dodgeAssistText, dodgeAssistSpeaker, parryAssistDuration);
            else
                hintBridge.ShowTimingCue(parryAssistText, parryAssistSpeaker, parryAssistDuration);
        }

        TimeScaleController timeScaleController = TimeScaleController.Instance;
        if (timeScaleController != null)
        {
            timeScaleController.SetSlowMotion(parryAssistTimeScale, parryAssistDuration);
            return;
        }

        if (_localParrySlowMotion != null)
            StopCoroutine(_localParrySlowMotion);
        _localParrySlowMotion = StartCoroutine(CoLocalParrySlowMotion());
    }

    TutorialHintUIBridge ResolveHintBridge()
    {
        if (_hintBridge != null)
            return _hintBridge;

        _hintBridge = FindObjectOfType<TutorialHintUIBridge>(true);
        return _hintBridge;
    }

    IEnumerator CoLocalParrySlowMotion()
    {
        _localSlowPreviousScale = Time.timeScale;
        _localSlowPreviousFixedDeltaTime = Time.fixedDeltaTime;
        _localSlowMotionActive = true;
        Time.timeScale = Mathf.Clamp(parryAssistTimeScale, 0.05f, 1f);
        Time.fixedDeltaTime = 0.02f * Time.timeScale;

        yield return new WaitForSecondsRealtime(parryAssistDuration);

        RestoreLocalSlowMotionIfNeeded();
        _localParrySlowMotion = null;
    }

    void RestoreLocalSlowMotionIfNeeded()
    {
        if (!_localSlowMotionActive)
            return;

        if (Mathf.Abs(Time.timeScale - parryAssistTimeScale) <= 0.05f)
        {
            Time.timeScale = _localSlowPreviousScale;
            Time.fixedDeltaTime = _localSlowPreviousFixedDeltaTime;
        }

        _localSlowMotionActive = false;
    }

    IEnumerator CoApplyMeleeHitboxAttack(AttackSnapshot snapshot)
    {
        EnsureMeleeAttackHitbox();
        if (meleeAttackHitbox == null)
        {
            AttackResolved?.Invoke(this, TrainingDummyAttackResult.Missed);
            yield break;
        }

        FacePlayerForMelee();
        meleeAttackHitbox.Configure(
            _activeProfile != null ? _activeProfile.damage : 8f,
            _activeProfile != null && _activeProfile.canParry,
            _activeProfile == null || !_activeProfile.unblockable,
            _activeProfile != null && _activeProfile.unblockable,
            false,
            transform);
        meleeAttackHitbox.canPerfectDodge = _activeProfile != null && _activeProfile.canPerfectDodge;
        meleeAttackHitbox.attackSequenceId++;
        meleeAttackHitbox.ActivateWindow();

        yield return new WaitForSeconds(meleeHitWindow);

        meleeAttackHitbox.DeactivateWindow();
        TrainingDummyAttackResult result = ClassifyAttackResult(snapshot, false);
        if (result == TrainingDummyAttackResult.Parried)
            _forcedParrySucceeded = true;
        AttackResolved?.Invoke(this, result);
        UpdateAdaptiveAssist(result);
        PlayDefenseResultReaction(result);

        if (debugLogs)
            Debug.Log($"[TrainingDummyController] {name} melee attack resolved: {result}", this);
    }

    void PlayHitReaction()
    {
        if (_hitReaction != null)
            StopCoroutine(_hitReaction);

        _hitReaction = StartCoroutine(CoHitReaction());
    }

    IEnumerator CoApproachPlayerForMelee()
    {
        if (!ShouldUseMeleeTracking())
            yield break;

        float elapsed = 0f;
        while (_hasActiveProfile && elapsed < maxChaseBeforeAttackSeconds)
        {
            if (!MoveTowardPlayerForMelee())
                yield break;

            elapsed += Time.deltaTime;
            yield return null;
        }
    }

    IEnumerator CoTelegraphDelay(float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            FacePlayerForMelee();
            elapsed += Time.deltaTime;
            yield return null;
        }
    }

    bool MoveTowardPlayerForMelee()
    {
        if (!ShouldUseMeleeTracking())
            return false;

        Vector3 toPlayer = playerTarget.position - transform.position;
        toPlayer.y = 0f;
        float distance = toPlayer.magnitude;
        if (distance <= 0.001f)
            return false;

        Vector3 direction = toPlayer / distance;
        RotateToward(direction);

        float stopDistance = Mathf.Max(0.5f, desiredMeleeDistance);
        if (distance <= stopDistance)
            return false;

        PlayMoveAnimation();
        float step = Mathf.Min(distance - stopDistance, chaseMoveSpeed * Time.deltaTime);
        transform.position += direction * step;
        return true;
    }

    void FacePlayerForMelee()
    {
        if (!ShouldUseMeleeTracking())
            return;

        Vector3 toPlayer = playerTarget.position - transform.position;
        toPlayer.y = 0f;
        if (toPlayer.sqrMagnitude <= 0.0001f)
            return;

        RotateToward(toPlayer.normalized);
    }

    bool ShouldUseMeleeTracking()
    {
        return !lockWorldPose &&
               trackPlayerDuringLoopAttack &&
               playerTarget != null &&
               _activeProfile != null &&
               !_activeProfile.useProjectileAttack;
    }

    void RotateToward(Vector3 direction)
    {
        if (direction.sqrMagnitude <= 0.0001f)
            return;

        Quaternion targetRotation = Quaternion.LookRotation(direction, Vector3.up);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, chaseTurnSpeed * Time.deltaTime);
    }

    void PlayIdleAnimation()
    {
        PlayVisualState(idleAnimationState);
    }

    void PlayMoveAnimation()
    {
        PlayVisualState(moveAnimationState);
    }

    void PlayAttackAnimation()
    {
        PlayVisualState(attackAnimationState);
    }

    void PlayHitAnimation()
    {
        PlayVisualState(hitAnimationState);
    }

    void PlayDieAnimation()
    {
        PlayVisualState(dieAnimationState);
    }

    void PlayVisualState(string stateName)
    {
        if (visualAnimator == null || !visualAnimator.enabled || string.IsNullOrWhiteSpace(stateName))
            return;

        if (string.Equals(_currentVisualState, stateName, System.StringComparison.Ordinal))
            return;

        int stateHash = Animator.StringToHash(stateName);
        if (!visualAnimator.HasState(0, stateHash))
            return;

        _currentVisualState = stateName;
        if (animationCrossFadeSeconds > 0f)
            visualAnimator.CrossFadeInFixedTime(stateName, animationCrossFadeSeconds, 0, 0f);
        else
            visualAnimator.Play(stateName, 0, 0f);
    }

    IEnumerator CoHitReaction()
    {
        Vector3 targetScale = _initialScale * (1f + hitScalePunch);
        transform.localScale = targetScale;
        SetEmission(hitEmission);

        float elapsed = 0f;
        while (elapsed < hitReactionDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / hitReactionDuration);
            transform.localScale = Vector3.Lerp(targetScale, _initialScale, t);
            yield return null;
        }

        transform.localScale = _initialScale;
        SetEmission(_hasActiveProfile && _activeProfile != null ? _activeProfile.stateColor : idleEmission);
        _hitReaction = null;
    }

    void PlayDefenseResultReaction(TrainingDummyAttackResult result)
    {
        Color reactionColor;
        float scalePunch;
        float duration;
        float recoilDistance;
        float recoveryDelay;

        switch (result)
        {
            case TrainingDummyAttackResult.Guarded:
                reactionColor = guardedReactionColor;
                scalePunch = guardedScalePunch;
                duration = guardedReactionDuration;
                recoilDistance = guardedRecoilDistance;
                recoveryDelay = guardedRecoveryDelay;
                break;

            case TrainingDummyAttackResult.Parried:
                reactionColor = parriedReactionColor;
                scalePunch = parriedScalePunch;
                duration = parriedReactionDuration;
                recoilDistance = parriedRecoilDistance;
                recoveryDelay = parriedRecoveryDelay;
                break;

            case TrainingDummyAttackResult.Dodged:
                reactionColor = dodgedReactionColor;
                scalePunch = dodgedScalePunch;
                duration = dodgedReactionDuration;
                recoilDistance = dodgedRecoilDistance;
                recoveryDelay = dodgedRecoveryDelay;
                break;

            case TrainingDummyAttackResult.PerfectDodged:
                reactionColor = perfectDodgeReactionColor;
                scalePunch = perfectDodgeScalePunch;
                duration = perfectDodgeReactionDuration;
                recoilDistance = perfectDodgeRecoilDistance;
                recoveryDelay = perfectDodgeRecoveryDelay;
                break;

            default:
                return;
        }

        if (recoveryDelay > 0f)
            _attackResumeRealtime = Mathf.Max(_attackResumeRealtime, Time.realtimeSinceStartup + recoveryDelay);

        if (_defenseReaction != null)
            StopCoroutine(_defenseReaction);
        if (_hitReaction != null)
        {
            StopCoroutine(_hitReaction);
            _hitReaction = null;
        }

        _defenseReaction = StartCoroutine(CoDefenseResultReaction(reactionColor, scalePunch, duration, recoilDistance));
    }

    IEnumerator CoDefenseResultReaction(Color reactionColor, float scalePunch, float duration, float recoilDistance)
    {
        Vector3 startPosition = transform.position;
        Vector3 reactionPosition = startPosition + ResolveReactionDirection() * recoilDistance;
        Color baseColor = _hasActiveProfile && _activeProfile != null ? _activeProfile.stateColor : idleEmission;

        if (worldMarker != null)
            worldMarker.SetMarkerColor(reactionColor);

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / Mathf.Max(0.001f, duration));
            float pulse = Mathf.Sin(t * Mathf.PI);

            transform.localScale = Vector3.LerpUnclamped(_initialScale, _initialScale * (1f + scalePunch), pulse);
            transform.position = Vector3.Lerp(startPosition, reactionPosition, pulse);
            SetEmission(Color.Lerp(reactionColor, baseColor, EaseOutCubic(t)));
            yield return null;
        }

        transform.position = startPosition;
        transform.localScale = _initialScale;
        SetEmission(baseColor);
        if (worldMarker != null)
            worldMarker.SetMarkerColor(baseColor);
        _defenseReaction = null;
    }

    void StopAttackLoop()
    {
        if (_attackLoop == null)
            return;

        StopCoroutine(_attackLoop);
        _attackLoop = null;
    }

    void StopVisualReactions()
    {
        if (_hitReaction != null)
        {
            StopCoroutine(_hitReaction);
            _hitReaction = null;
        }

        if (_defenseReaction != null)
        {
            StopCoroutine(_defenseReaction);
            _defenseReaction = null;
        }

        transform.localScale = _initialScale;
        SetEmission(_hasActiveProfile && _activeProfile != null ? _activeProfile.stateColor : idleEmission);
        if (worldMarker != null && _hasActiveProfile && _activeProfile != null)
            worldMarker.SetMarkerColor(_activeProfile.stateColor);
    }

    void StartHologramSpawn()
    {
        if (!useHologramSpawnDespawn || !gameObject.activeInHierarchy)
        {
            StopHologramTransition(true);
            return;
        }

        Material sourceMaterial = ResolveHologramTransitionMaterial();
        if (sourceMaterial == null)
        {
            StopHologramTransition(true);
            return;
        }

        StopHologramTransition(true);
        if (!PrepareHologramMaterials(sourceMaterial))
            return;

        SetHologramAlpha(0f);
        _hologramTransitionRoutine = StartCoroutine(CoHologramTransition(hologramSpawnDuration, 0f, hologramTransitionColor.a, true));
    }

    void StartHologramDespawnOrDisable()
    {
        if (!gameObject.activeInHierarchy)
        {
            gameObject.SetActive(false);
            return;
        }

        if (!useHologramSpawnDespawn)
        {
            StopHologramTransition(true);
            gameObject.SetActive(false);
            return;
        }

        Material sourceMaterial = ResolveHologramTransitionMaterial();
        if (sourceMaterial == null)
        {
            StopHologramTransition(true);
            gameObject.SetActive(false);
            return;
        }

        StopHologramTransition(true);
        if (!PrepareHologramMaterials(sourceMaterial))
        {
            gameObject.SetActive(false);
            return;
        }

        SetHologramAlpha(hologramTransitionColor.a);
        _hologramTransitionRoutine = StartCoroutine(CoHologramTransition(hologramDespawnDuration, hologramTransitionColor.a, 0f, false));
    }

    IEnumerator CoHologramTransition(float duration, float startAlpha, float targetAlpha, bool restoreOnComplete)
    {
        if (duration <= 0f)
        {
            SetHologramAlpha(targetAlpha);
            _hologramTransitionRoutine = null;
            RestoreHologramMaterials();
            if (!restoreOnComplete)
                gameObject.SetActive(false);
            yield break;
        }

        float elapsed = 0f;
        while (elapsed < duration && _runtimeHologramMaterial != null)
        {
            float t = Mathf.Clamp01(elapsed / duration);
            float eased = t * t * (3f - 2f * t);
            SetHologramAlpha(Mathf.Lerp(startAlpha, targetAlpha, eased));
            elapsed += Time.deltaTime;
            yield return null;
        }

        SetHologramAlpha(targetAlpha);
        _hologramTransitionRoutine = null;
        RestoreHologramMaterials();
        if (!restoreOnComplete)
            gameObject.SetActive(false);
        else if (_hasActiveProfile && _activeProfile != null)
            SetEmission(_activeProfile.stateColor);
    }

    bool PrepareHologramMaterials(Material sourceMaterial)
    {
        Renderer[] childRenderers = GetComponentsInChildren<Renderer>(true);
        if (childRenderers == null || childRenderers.Length == 0)
            return false;

        List<Renderer> renderers = new List<Renderer>(childRenderers.Length);
        List<Material[]> originals = new List<Material[]>(childRenderers.Length);

        for (int i = 0; i < childRenderers.Length; i++)
        {
            Renderer renderer = childRenderers[i];
            if (IsHologramIgnoredRenderer(renderer))
                continue;

            Material[] materials = renderer.sharedMaterials;
            if (materials == null || materials.Length == 0)
                continue;

            renderers.Add(renderer);
            originals.Add(materials);
        }

        if (renderers.Count == 0)
            return false;

        _runtimeHologramMaterial = new Material(sourceMaterial)
        {
            name = name + "_RuntimeHologram"
        };

        _hologramRenderers = renderers.ToArray();
        _hologramOriginalMaterials = originals.ToArray();
        SetHologramAlpha(0f);

        for (int i = 0; i < _hologramRenderers.Length; i++)
        {
            Renderer renderer = _hologramRenderers[i];
            Material[] original = _hologramOriginalMaterials[i];
            if (renderer == null || original == null)
                continue;

            Material[] hologramMaterials = new Material[original.Length];
            for (int slot = 0; slot < hologramMaterials.Length; slot++)
                hologramMaterials[slot] = _runtimeHologramMaterial;

            renderer.sharedMaterials = hologramMaterials;
        }

        return true;
    }

    bool IsHologramIgnoredRenderer(Renderer renderer)
    {
        if (renderer == null || renderer is ParticleSystemRenderer || renderer is LineRenderer || renderer == dangerTargetMarkerRenderer)
            return true;

        if (renderer.GetComponentInParent<TutorialWorldMarker>(true) != null)
            return true;

        if (parryMarkerObject != null && renderer.transform != null && renderer.transform.IsChildOf(parryMarkerObject.transform))
            return true;

        string objectName = renderer.transform != null ? renderer.transform.name : string.Empty;
        return objectName.Contains("Marker") ||
               objectName.Contains("Danger") ||
               objectName.Contains("Hitbox") ||
               objectName.Contains("Guide") ||
               objectName.Contains("LockPivot");
    }

    Material ResolveHologramTransitionMaterial()
    {
        if (hologramTransitionMaterial != null)
            return hologramTransitionMaterial;

        if (!string.IsNullOrWhiteSpace(hologramMaterialResourcePath))
            hologramTransitionMaterial = Resources.Load<Material>(hologramMaterialResourcePath);

#if UNITY_EDITOR
        if (hologramTransitionMaterial == null)
            hologramTransitionMaterial = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/Holograms/Materials/Examples/Basic/Scanline_Hologram_Empty.mat");
#endif

        return hologramTransitionMaterial;
    }

    void SetHologramAlpha(float alpha)
    {
        if (_runtimeHologramMaterial == null)
            return;

        Color color = hologramTransitionColor;
        color.a = Mathf.Clamp01(alpha);

        if (_runtimeHologramMaterial.HasProperty(HologramColorId))
            _runtimeHologramMaterial.SetColor(HologramColorId, color);

        if (_runtimeHologramMaterial.HasProperty(TextureTintColorId))
        {
            Color tint = color;
            tint.a *= 0.75f;
            _runtimeHologramMaterial.SetColor(TextureTintColorId, tint);
        }
    }

    void StopHologramTransition(bool restoreMaterials)
    {
        if (_hologramTransitionRoutine != null)
        {
            StopCoroutine(_hologramTransitionRoutine);
            _hologramTransitionRoutine = null;
        }

        if (restoreMaterials)
            RestoreHologramMaterials();
    }

    void RestoreHologramMaterials()
    {
        if (_hologramRenderers != null && _hologramOriginalMaterials != null)
        {
            int count = Mathf.Min(_hologramRenderers.Length, _hologramOriginalMaterials.Length);
            for (int i = 0; i < count; i++)
            {
                Renderer renderer = _hologramRenderers[i];
                Material[] materials = _hologramOriginalMaterials[i];
                if (renderer != null && materials != null)
                    renderer.sharedMaterials = materials;
            }
        }

        _hologramRenderers = null;
        _hologramOriginalMaterials = null;

        if (_runtimeHologramMaterial != null)
        {
            if (Application.isPlaying)
                Destroy(_runtimeHologramMaterial);
            else
                DestroyImmediate(_runtimeHologramMaterial);
        }

        _runtimeHologramMaterial = null;
    }

    AttackSnapshot CaptureAttackSnapshot()
    {
        return new AttackSnapshot
        {
            beforeGuard = playerBridge != null ? playerBridge.GuardBlockCount : 0,
            beforeParry = playerBridge != null ? playerBridge.ParryCount : 0,
            beforePerfect = playerBridge != null ? playerBridge.PerfectDodgeCount : 0,
            beforeDamage = playerBridge != null ? playerBridge.PlayerDamageCount : 0,
            beforeHp = playerBridge != null ? playerBridge.CurrentHP : 0,
            treatMissAsDodge = _activeProfile != null && _activeProfile.countProjectileMissAsDodge
        };
    }

    bool TryFireProjectile(AttackSnapshot snapshot, Vector3 lockedTargetPoint)
    {
        if (projectilePrefab == null)
            return false;

        Transform spawn = projectileSpawnPoint != null ? projectileSpawnPoint : attackOrigin;
        if (spawn == null)
            spawn = transform;

        Vector3 source = spawn.position;
        Vector3 direction = lockedTargetPoint - source;
        if (direction.sqrMagnitude <= 0.0001f)
            direction = transform.forward;

        GameObject projectileObject = RuntimeObjectPool.Acquire(
            projectilePrefab,
            source,
            Quaternion.LookRotation(direction.normalized, Vector3.up));
        if (projectileObject == null)
            return false;

        TutorialProjectile projectile = projectileObject.GetComponent<TutorialProjectile>();
        if (projectile == null)
        {
            RuntimeObjectPool.Release(projectileObject);
            return false;
        }

        projectile.ConfigureRuntime(
            transform,
            _activeProfile.damage,
            GetAdjustedProjectileSpeed(),
            _activeProfile.projectileLifeTime,
            _activeProfile.canParry,
            _activeProfile.canPerfectDodge,
            _activeProfile.unblockable);
        projectile.Released += HandleProjectileReleased;
        _projectileSnapshots[projectile] = snapshot;
        return true;
    }

    void HandleProjectileReleased(TutorialProjectile projectile, bool hitPlayer)
    {
        if (projectile == null)
            return;

        projectile.Released -= HandleProjectileReleased;

        if (!_projectileSnapshots.TryGetValue(projectile, out AttackSnapshot snapshot))
            return;

        _projectileSnapshots.Remove(projectile);
        TrainingDummyAttackResult result = ClassifyAttackResult(snapshot, hitPlayer);
        AttackResolved?.Invoke(this, result);
        UpdateAdaptiveAssist(result);
        PlayDefenseResultReaction(result);

        if (debugLogs)
            Debug.Log($"[TrainingDummyController] {name} projectile resolved: {result}", this);
    }

    TrainingDummyAttackResult ClassifyAttackResult(AttackSnapshot snapshot, bool hitConfirmed)
    {
        if (playerBridge == null)
            return TrainingDummyAttackResult.Missed;

        if (playerBridge.PerfectDodgeCount > snapshot.beforePerfect)
            return TrainingDummyAttackResult.PerfectDodged;
        if (playerBridge.ParryCount > snapshot.beforeParry)
            return TrainingDummyAttackResult.Parried;
        if (playerBridge.GuardBlockCount > snapshot.beforeGuard)
            return TrainingDummyAttackResult.Guarded;
        if (hitConfirmed || playerBridge.PlayerDamageCount > snapshot.beforeDamage || playerBridge.CurrentHP < snapshot.beforeHp)
            return TrainingDummyAttackResult.Hit;
        if (snapshot.treatMissAsDodge || playerBridge.WasDodgingRecently(0.55f))
            return TrainingDummyAttackResult.Dodged;
        return TrainingDummyAttackResult.Missed;
    }

    Vector3 GetAttackTargetPoint()
    {
        if (playerTarget == null)
            return transform.position + transform.forward * 4f;

        return playerTarget.position + Vector3.up * playerAimHeight;
    }

    void EnsureDangerIndicator()
    {
        if (!autoCreateDangerIndicator || dangerIndicatorLine != null)
            return;

        GameObject lineObject = new GameObject("DangerIndicatorLine");
        lineObject.transform.SetParent(transform, false);
        dangerIndicatorLine = lineObject.AddComponent<LineRenderer>();
        dangerIndicatorLine.enabled = false;
        dangerIndicatorLine.positionCount = 2;
        dangerIndicatorLine.useWorldSpace = true;
        dangerIndicatorLine.alignment = LineAlignment.View;
        dangerIndicatorLine.textureMode = LineTextureMode.Stretch;
        dangerIndicatorLine.shadowCastingMode = ShadowCastingMode.Off;
        dangerIndicatorLine.receiveShadows = false;
        dangerIndicatorLine.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        dangerIndicatorLine.sharedMaterial = GetDangerIndicatorMaterial();
        dangerIndicatorLine.numCapVertices = 4;
        dangerIndicatorLine.numCornerVertices = 2;
    }

    void EnsureDangerTargetMarker()
    {
        if (dangerTargetMarker != null && dangerTargetMarkerRenderer != null)
            return;

        Transform existing = transform.Find("DangerTargetMarker");
        if (existing != null)
        {
            dangerTargetMarker = existing;
            dangerTargetMarkerRenderer = existing.GetComponent<MeshRenderer>();
            return;
        }

        GameObject markerObject = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        markerObject.name = "DangerTargetMarker";
        markerObject.transform.SetParent(transform, false);
        markerObject.layer = gameObject.layer;

        Collider markerCollider = markerObject.GetComponent<Collider>();
        if (markerCollider != null)
            Destroy(markerCollider);

        dangerTargetMarker = markerObject.transform;
        dangerTargetMarkerRenderer = markerObject.GetComponent<MeshRenderer>();
        if (dangerTargetMarkerRenderer != null)
        {
            dangerTargetMarkerRenderer.sharedMaterial = GetDangerTargetMarkerMaterial();
            dangerTargetMarkerRenderer.shadowCastingMode = ShadowCastingMode.Off;
            dangerTargetMarkerRenderer.receiveShadows = false;
            dangerTargetMarkerRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        }

        markerObject.SetActive(false);
    }

    void ShowDangerIndicator(Vector3 targetPoint, TrainingDummyStepProfile profile)
    {
        if (dangerIndicatorLine == null)
            return;

        Transform spawn = projectileSpawnPoint != null ? projectileSpawnPoint : attackOrigin;
        if (spawn == null)
            spawn = transform;

        float indicatorWidth = GetAdjustedIndicatorWidth();
        dangerIndicatorLine.startWidth = indicatorWidth;
        dangerIndicatorLine.endWidth = indicatorWidth;
        dangerIndicatorLine.startColor = profile.dangerIndicatorColor;
        dangerIndicatorLine.endColor = profile.dangerIndicatorColor;
        dangerIndicatorLine.SetPosition(0, spawn.position);
        dangerIndicatorLine.SetPosition(1, targetPoint);
        dangerIndicatorLine.enabled = true;
        ShowDangerTargetMarker(GetDangerTargetMarkerPoint(targetPoint), profile);
    }

    void HideDangerIndicator()
    {
        if (dangerIndicatorLine != null)
            dangerIndicatorLine.enabled = false;
    }

    void ShowDangerTargetMarker(Vector3 markerPoint, TrainingDummyStepProfile profile)
    {
        if (!profile.showTargetMarker || dangerTargetMarker == null || dangerTargetMarkerRenderer == null)
        {
            HideDangerTargetMarker();
            return;
        }

        dangerTargetMarker.position = markerPoint + Vector3.up * dangerTargetMarkerOffsetY;
        float markerSize = GetAdjustedTargetMarkerSize();
        dangerTargetMarker.localScale = new Vector3(markerSize, dangerTargetMarkerHeight, markerSize);
        ApplyMarkerColor(profile.targetMarkerColor);
        if (!dangerTargetMarker.gameObject.activeSelf)
            dangerTargetMarker.gameObject.SetActive(true);
    }

    void HideDangerTargetMarker()
    {
        if (dangerTargetMarker != null && dangerTargetMarker.gameObject.activeSelf)
            dangerTargetMarker.gameObject.SetActive(false);
    }

    void ClearPendingProjectiles()
    {
        if (_projectileSnapshots.Count == 0)
            return;

        foreach (KeyValuePair<TutorialProjectile, AttackSnapshot> pair in _projectileSnapshots)
        {
            if (pair.Key != null)
                pair.Key.Released -= HandleProjectileReleased;
        }

        _projectileSnapshots.Clear();
    }

    static Material GetDangerIndicatorMaterial()
    {
        if (s_dangerIndicatorMaterial != null)
            return s_dangerIndicatorMaterial;

        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null)
            shader = Shader.Find("Unlit/Color");

        s_dangerIndicatorMaterial = new Material(shader)
        {
            hideFlags = HideFlags.HideAndDontSave
        };
        return s_dangerIndicatorMaterial;
    }

    static Material GetDangerTargetMarkerMaterial()
    {
        if (s_dangerTargetMarkerMaterial != null)
            return s_dangerTargetMarkerMaterial;

        Shader shader = Shader.Find("Unlit/Color");
        if (shader == null)
            shader = Shader.Find("Sprites/Default");

        s_dangerTargetMarkerMaterial = new Material(shader)
        {
            hideFlags = HideFlags.HideAndDontSave
        };
        return s_dangerTargetMarkerMaterial;
    }

    void SetEmission(Color color)
    {
        if (primaryRenderer == null || string.IsNullOrWhiteSpace(emissionProperty))
            return;

        if (_propertyBlock == null)
            _propertyBlock = new MaterialPropertyBlock();

        primaryRenderer.GetPropertyBlock(_propertyBlock);
        if (UsesAspCharacterShader(primaryRenderer))
        {
            _propertyBlock.SetColor(emissionProperty, Color.black);
            primaryRenderer.SetPropertyBlock(_propertyBlock);
            return;
        }

        _propertyBlock.SetColor(emissionProperty, color);
        if (primaryRenderer.sharedMaterial != null && primaryRenderer.sharedMaterial.HasProperty("_Color"))
            _propertyBlock.SetColor("_Color", color);
        primaryRenderer.SetPropertyBlock(_propertyBlock);
    }

    static bool UsesAspCharacterShader(Renderer renderer)
    {
        if (renderer == null)
            return false;

        Material[] materials = renderer.sharedMaterials;
        if (materials == null)
            return false;

        for (int i = 0; i < materials.Length; i++)
        {
            Material material = materials[i];
            if (material != null && material.shader != null && material.shader.name == "ASP/Character")
                return true;
        }

        return false;
    }

    Vector3 GetDangerTargetMarkerPoint(Vector3 attackTargetPoint)
    {
        float markerY = playerTarget != null ? playerTarget.position.y : attackTargetPoint.y;
        return new Vector3(attackTargetPoint.x, markerY, attackTargetPoint.z);
    }

    void ApplyMarkerColor(Color color)
    {
        if (dangerTargetMarkerRenderer == null)
            return;

        _markerPropertyBlock.Clear();
        if (dangerTargetMarkerRenderer.sharedMaterial != null)
        {
            if (dangerTargetMarkerRenderer.sharedMaterial.HasProperty("_BaseColor"))
                _markerPropertyBlock.SetColor("_BaseColor", color);
            if (dangerTargetMarkerRenderer.sharedMaterial.HasProperty("_Color"))
                _markerPropertyBlock.SetColor("_Color", color);
        }

        dangerTargetMarkerRenderer.SetPropertyBlock(_markerPropertyBlock);
    }

    float GetAdjustedTelegraphDuration()
    {
        if (_activeProfile == null)
            return 0f;

        return _activeProfile.telegraphDuration +
               (_activeProfile.adaptiveTelegraphBonusPerStack * GetAdaptiveAssistStacks());
    }

    float GetAdjustedProjectileSpeed()
    {
        if (_activeProfile == null)
            return 0f;

        float reduction = _activeProfile.adaptiveProjectileSpeedReductionPerStack * GetAdaptiveAssistStacks();
        float multiplier = Mathf.Clamp(1f - reduction, 0.45f, 1f);
        return _activeProfile.projectileSpeed * multiplier;
    }

    float GetAdjustedTargetMarkerSize()
    {
        if (_activeProfile == null)
            return 0f;

        return _activeProfile.targetMarkerSize +
               (_activeProfile.adaptiveTargetMarkerSizeBonusPerStack * GetAdaptiveAssistStacks());
    }

    float GetAdjustedIndicatorWidth()
    {
        if (_activeProfile == null)
            return 0f;

        return _activeProfile.dangerIndicatorWidth +
               (_activeProfile.adaptiveIndicatorWidthBonusPerStack * GetAdaptiveAssistStacks());
    }

    int GetAdaptiveAssistStacks()
    {
        if (_activeProfile == null || !_activeProfile.enableAdaptiveAssist)
            return 0;

        int threshold = Mathf.Max(1, _activeProfile.assistStartAfterFailures);
        int stacks = _adaptiveFailureCount - threshold + 1;
        return Mathf.Clamp(stacks, 0, Mathf.Max(0, _activeProfile.maxAdaptiveFailureStacks));
    }

    void UpdateAdaptiveAssist(TrainingDummyAttackResult result)
    {
        if (_activeProfile == null || !_activeProfile.enableAdaptiveAssist)
            return;

        if (IsPreferredOutcome(result))
        {
            _adaptiveFailureCount = 0;
            return;
        }

        _adaptiveFailureCount = Mathf.Min(
            _adaptiveFailureCount + 1,
            Mathf.Max(1, _activeProfile.assistStartAfterFailures) + Mathf.Max(0, _activeProfile.maxAdaptiveFailureStacks));
    }

    bool IsPreferredOutcome(TrainingDummyAttackResult result)
    {
        if (_activeProfile == null)
            return false;

        switch (_activeProfile.stepType)
        {
            case TutorialStepType.Guard:
                return result == TrainingDummyAttackResult.Guarded;

            case TutorialStepType.Parry:
                return result == TrainingDummyAttackResult.Parried;

            case TutorialStepType.Dodge:
                return result == TrainingDummyAttackResult.Dodged ||
                       result == TrainingDummyAttackResult.PerfectDodged;

            case TutorialStepType.PerfectDodge:
                return result == TrainingDummyAttackResult.PerfectDodged;

            default:
                return result != TrainingDummyAttackResult.Hit;
        }
    }

    Vector3 ResolveReactionDirection()
    {
        Vector3 direction = transform.position - (playerTarget != null ? playerTarget.position : transform.position - transform.forward);
        direction.y = 0f;

        if (direction.sqrMagnitude <= 0.0001f)
            direction = attackOrigin != null ? -attackOrigin.forward : -transform.forward;

        direction.y = 0f;
        return direction.sqrMagnitude <= 0.0001f ? Vector3.back : direction.normalized;
    }

    static float EaseOutCubic(float t)
    {
        t = Mathf.Clamp01(t);
        t -= 1f;
        return t * t * t + 1f;
    }
}
