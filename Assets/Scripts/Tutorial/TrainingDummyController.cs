using System.Collections;
using System.Collections.Generic;
using UnityEngine;
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
    [SerializeField] private string emissionProperty = "_EmissionColor";
    [SerializeField] private Color idleEmission = Color.black;
    [SerializeField] private Color hitEmission = new Color(1f, 0.85f, 0.2f, 1f);
    [SerializeField, Range(0f, 0.25f)] private float hitScalePunch = 0.08f;
    [SerializeField, Min(0.05f)] private float hitReactionDuration = 0.14f;

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

    [Header("Hit Tracking")]
    [SerializeField, Min(0f)] private float hitEventCooldown = 0.08f;
    [SerializeField] private bool debugLogs;

    float _lastHitTime = float.NegativeInfinity;
    int _lastAttackSequenceId;
    int _lastAttackerInstanceId;
    float _currentHealth;
    Coroutine _attackLoop;
    Coroutine _hitReaction;
    Coroutine _defenseReaction;
    Vector3 _initialScale;
    MaterialPropertyBlock _propertyBlock;
    MaterialPropertyBlock _markerPropertyBlock;
    TrainingDummyStepProfile _activeProfile;
    bool _hasActiveProfile;
    int _adaptiveFailureCount;
    float _attackResumeRealtime;
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
        if (projectileSpawnPoint == null)
            projectileSpawnPoint = attackOrigin != null ? attackOrigin : transform;

        _initialScale = transform.localScale;
        _propertyBlock = new MaterialPropertyBlock();
        _markerPropertyBlock = new MaterialPropertyBlock();
        EnsureDangerIndicator();
        EnsureDangerTargetMarker();
        SetEmission(idleEmission);
    }

    void OnDisable()
    {
        StopVisualReactions();
        HideDangerIndicator();
        HideDangerTargetMarker();
        worldMarker?.SetVisible(false);
        ClearPendingProjectiles();
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
        if (worldMarker == null)
            worldMarker = GetComponentInChildren<TutorialWorldMarker>(true);
        _initialScale = transform.localScale;
        EnsureDangerIndicator();
        EnsureDangerTargetMarker();
        SetEmission(idleEmission);
    }

    public void ConfigureHitEffectRuntime(GameObject runtimeHitEffectPrefab)
    {
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

    public void SetRuntimeProfiles(params TrainingDummyStepProfile[] profiles)
    {
        stepProfiles = profiles;
    }

    public void ApplyStep(TutorialStepType stepType)
    {
        _activeProfile = null;
        _hasActiveProfile = false;
        _adaptiveFailureCount = 0;
        _attackResumeRealtime = 0f;
        _lastAttackSequenceId = 0;
        _lastAttackerInstanceId = 0;
        _lastHitTime = float.NegativeInfinity;

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

        gameObject.SetActive(_hasActiveProfile);
        StopAttackLoop();
        StopVisualReactions();
        HideDangerIndicator();
        HideDangerTargetMarker();
        ClearPendingProjectiles();

        if (!_hasActiveProfile || _activeProfile == null)
            return;

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
        PlayHitReaction();
        SpawnHitEffect(payload);

        if (_activeProfile != null && _activeProfile.useHealth && !_activeProfile.invulnerable)
            _currentHealth = Mathf.Max(0f, _currentHealth - payload.damage);

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

            Vector3 attackTargetPoint = GetAttackTargetPoint();
            SetEmission(_activeProfile.canParry ? hitEmission : _activeProfile.stateColor);
            ShowDangerIndicator(attackTargetPoint, _activeProfile);
            yield return new WaitForSeconds(GetAdjustedTelegraphDuration());
            ExecuteAttack(attackTargetPoint);
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

        Vector3 source = attackOrigin != null ? attackOrigin.position : transform.position;
        Vector3 targetPos = lockedTargetPoint;
        Vector2 source2D = new Vector2(source.x, source.z);
        Vector2 target2D = new Vector2(targetPos.x, targetPos.z);

        // 실제 방어/패링/퍼펙트 회피 판정은 기존 PlayerDamageReceiver 쪽에 맡기고,
        // 더미는 공격 시점 전후의 플레이어 이벤트 변화만 읽어 결과를 분류한다.
        if ((target2D - source2D).sqrMagnitude <= _activeProfile.hitRange * _activeProfile.hitRange)
        {
            IDamageReceiver receiver = playerTarget.GetComponent<IDamageReceiver>();
            if (receiver == null)
                receiver = playerTarget.GetComponentInParent<IDamageReceiver>();

            if (receiver != null)
            {
                Vector3 hitDirection = (targetPos - source).sqrMagnitude > 0.0001f
                    ? (targetPos - source).normalized
                    : transform.forward;

                HitPayload payload = new HitPayload
                {
                    attacker = transform,
                    damage = _activeProfile.damage,
                    hitDirection = hitDirection,
                    hitPoint = targetPos,
                    hitType = HitType.Normal,
                    canParry = _activeProfile.canParry,
                    canPerfectDodge = _activeProfile.canPerfectDodge,
                    canGuard = !_activeProfile.unblockable,
                    unblockable = _activeProfile.unblockable
                };

                receiver.ReceiveHit(payload);
            }
        }

        // 기존 전투 시스템 이벤트를 우선 신뢰하고, 아무 이벤트가 없을 때만 회피 추정으로 떨어진다.
        TrainingDummyAttackResult result = ClassifyAttackResult(snapshot, false);

        AttackResolved?.Invoke(this, result);
        UpdateAdaptiveAssist(result);
        PlayDefenseResultReaction(result);

        if (debugLogs)
            Debug.Log($"[TrainingDummyController] {name} attack resolved: {result}", this);
    }

    void PlayHitReaction()
    {
        if (_hitReaction != null)
            StopCoroutine(_hitReaction);

        _hitReaction = StartCoroutine(CoHitReaction());
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

        primaryRenderer.GetPropertyBlock(_propertyBlock);
        _propertyBlock.SetColor(emissionProperty, color);
        if (primaryRenderer.sharedMaterial != null && primaryRenderer.sharedMaterial.HasProperty("_Color"))
            _propertyBlock.SetColor("_Color", color);
        primaryRenderer.SetPropertyBlock(_propertyBlock);
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
