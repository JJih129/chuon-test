using System;
using UnityEngine;
using UnityEngine.Rendering;

public enum DroneCombatRole
{
    Standard = 0,
    Suppressor = 1,
    Flanker = 2,
    Skirmisher = 3
}

[DisallowMultipleComponent]
public class DroneController : MonoBehaviour, IDamageReceiver
{
    public event Action<DroneController> AttackTelegraphed;
    public event Action<DroneController> Fired;
    public event Action<DroneController> Died;

    [Header("Target")]
    public Transform target;

    [Header("Movement")]
    public float moveSpeed = 3.5f;
    public float stopDistance = 8f;
    public float turnSpeedDeg = 360f;

    [Header("Collision")]
    [SerializeField] bool useCollisionAwareMovement = true;
    [SerializeField] LayerMask movementCollisionMask = ~0;
    [SerializeField, Min(0f)] float movementSkin = 0.04f;

    [Header("Combat Movement")]
    [SerializeField, Min(0.1f)] float combatDistanceTolerance = 1.4f;
    [SerializeField, Range(0.1f, 1f)] float strafeSpeedMultiplier = 0.55f;
    [SerializeField, Range(0.1f, 1f)] float retreatSpeedMultiplier = 0.7f;
    [SerializeField, Min(0.1f)] float minStrafeSwitchInterval = 0.75f;
    [SerializeField, Min(0.1f)] float maxStrafeSwitchInterval = 1.35f;
    [SerializeField, Min(0f)] float postShotStrafeDuration = 0.4f;
    [SerializeField, Range(2f, 45f)] float fireFacingAngleThreshold = 12f;
    [SerializeField, Range(2f, 60f)] float telegraphFacingAngleThreshold = 22f;

    [Header("Combat Role")]
    [SerializeField] DroneCombatRole combatRole = DroneCombatRole.Standard;
    [SerializeField] bool applyPerInstanceVariance = true;
    [SerializeField, Range(0f, 0.25f)] float fireCooldownVariance = 0.08f;
    [SerializeField, Range(0f, 1.5f)] float combatDistanceVariance = 0.5f;

    [Header("Attack")]
    public GameObject projectilePrefab;
    public Transform fireOrigin;
    public float fireDistance = 15f;
    public float fireCooldown = 2f;
    public float projectileSpeed = 15f;
    public int projectileDamage = 10;
    [SerializeField, Min(0f)] float projectileSpawnForwardOffset = 0.65f;

    [Header("Health / VFX")]
    public int maxHP = 20;
    public GameObject hitVFX;
    public GameObject deathVFX;
    public Renderer droneRenderer;
    public Color hitFlashColor = Color.red;
    public float hitFlashDuration = 0.2f;

    public DroneCombatRole CombatRole => combatRole;
    public bool IsDead => _isDead;

    [Header("Death Cleanup")]
    [SerializeField, Min(0.05f)] float deathDespawnDelay = 0.45f;
    [SerializeField, Min(0.05f)] float deathScaleDownDuration = 0.28f;
    [SerializeField, Min(0f)] float deathRiseDistance = 0.2f;
    [SerializeField] bool disableCollidersImmediatelyOnDeath = true;

    [Header("Hit Reaction")]
    [SerializeField, Min(0f)] float lightHitStun = 0.08f;
    [SerializeField, Min(0f)] float heavyHitStun = 0.16f;
    [SerializeField, Min(0f)] float hitPushDistance = 0.22f;
    [SerializeField, Min(0f)] float hitFireDelay = 0.18f;
    [SerializeField] bool interruptTelegraphOnHit = true;
    [SerializeField] bool cancelPostShotStrafeOnHit = true;

    [Header("Debug")]
    [SerializeField] bool debugLogs = false;

    [Header("Performance")]
    [SerializeField] bool optimizeChildRenderers = true;
    [SerializeField] bool disableRendererShadows = true;
    [SerializeField] bool disableMotionVectors = true;
    [SerializeField] bool lightweightSimulationMode = false;
    [SerializeField, Min(0.016f)] float lightweightTickInterval = 0.05f;

    int _currentHP;
    float _nextFireTime;
    float _hitFlashUntil;
    Rigidbody _rigidbody;
    Color _originalColor = Color.white;
    MaterialPropertyBlock _colorBlock;
    bool _hasBaseColorProperty;
    bool _hasColorProperty;
    bool _flashApplied;
    float _initialFireDelay;
    float _attackTelegraphLeadTime;
    float _nextLightweightTickAt;
    Renderer[] _cachedRenderers;
    bool _attackTelegraphIssuedForCurrentShot;
    int _strafeSign = 1;
    float _nextStrafeSwitchAt;
    float _postShotStrafeUntil;
    float _hitRecoverUntil;
    Vector3 _hitPushDirection;
    float _hitPushSpeed;
    float _hitPushDistanceRemaining;
    bool _baseCombatTuningCached;
    float _baseMoveSpeed;
    float _baseStopDistance;
    float _baseCombatDistanceTolerance;
    float _baseStrafeSpeedMultiplier;
    float _baseRetreatSpeedMultiplier;
    float _baseMinStrafeSwitchInterval;
    float _baseMaxStrafeSwitchInterval;
    float _basePostShotStrafeDuration;
    float _baseFireFacingAngleThreshold;
    float _baseTelegraphFacingAngleThreshold;
    float _baseFireDistance;
    float _baseFireCooldown;
    float _baseProjectileSpeed;
    float _baseLightHitStun;
    float _baseHeavyHitStun;
    float _baseHitPushDistance;
    bool _isDead;
    Coroutine _deathRoutine;
    Collider[] _cachedColliders;
    Collider _movementCollider;
    readonly RaycastHit[] _movementCastHits = new RaycastHit[8];
    Vector3 _initialLocalScale;

    void Awake()
    {
        _rigidbody = GetComponent<Rigidbody>();
        _currentHP = maxHP;
        CacheBaseCombatTuning();
        CacheColliders();
        ResolveMovementCollider();
        _initialLocalScale = transform.localScale;

        CacheChildRenderers();
        if (droneRenderer == null)
            droneRenderer = ResolvePrimaryRenderer();

        InitializeRendererState();
        ApplyRendererPerformanceOverrides();

        if (target == null)
        {
            GameObject player = GameObject.FindWithTag("Player");
            if (player != null)
                target = player.transform;
        }
    }

    void OnEnable()
    {
        ApplyCombatRoleTuning();
        _currentHP = maxHP;
        _isDead = false;
        _nextFireTime = _initialFireDelay > 0f ? Time.time + _initialFireDelay : 0f;
        _hitFlashUntil = 0f;
        _flashApplied = false;
        _nextLightweightTickAt = Time.time;
        _attackTelegraphIssuedForCurrentShot = _attackTelegraphLeadTime <= 0f;
        _hitRecoverUntil = 0f;
        _hitPushDirection = Vector3.zero;
        _hitPushSpeed = 0f;
        _hitPushDistanceRemaining = 0f;
        if (_deathRoutine != null)
        {
            StopCoroutine(_deathRoutine);
            _deathRoutine = null;
        }
        transform.localScale = _initialLocalScale;
        ResetCombatMovementState();
        CacheChildRenderers();
        CacheColliders();
        ResolveMovementCollider();
        RestoreDeathState();
        ApplyRendererPerformanceOverrides();
        ApplySimulationMode();
        ApplyRendererColor(_originalColor);
    }

    public void SetCombatRole(DroneCombatRole role, bool reapplyImmediately = true)
    {
        combatRole = role;
        if (reapplyImmediately)
            ApplyCombatRoleTuning();
    }

    public void SetInitialFireDelay(float delay)
    {
        _initialFireDelay = Mathf.Max(0f, delay);
        _nextFireTime = _initialFireDelay > 0f ? Time.time + _initialFireDelay : 0f;
        _attackTelegraphIssuedForCurrentShot = _attackTelegraphLeadTime <= 0f;
    }

    public void SetAttackTelegraph(float leadTime)
    {
        _attackTelegraphLeadTime = Mathf.Max(0f, leadTime);
        _attackTelegraphIssuedForCurrentShot = _attackTelegraphLeadTime <= 0f;
    }

    public void SetLightweightSimulation(bool enabled, float tickInterval = 0.05f)
    {
        lightweightSimulationMode = enabled;
        lightweightTickInterval = Mathf.Max(0.016f, tickInterval);
        _nextLightweightTickAt = Time.time;
        ApplySimulationMode();
    }

    public void SetProjectileSpawnForwardOffset(float offset)
    {
        projectileSpawnForwardOffset = Mathf.Max(0f, offset);
    }

    void ApplySimulationMode()
    {
        if (_rigidbody == null)
            return;

        _rigidbody.isKinematic = lightweightSimulationMode;
        _rigidbody.interpolation = RigidbodyInterpolation.None;
        _rigidbody.collisionDetectionMode = CollisionDetectionMode.Discrete;
    }

    void CacheBaseCombatTuning()
    {
        if (_baseCombatTuningCached)
            return;

        _baseCombatTuningCached = true;
        _baseMoveSpeed = moveSpeed;
        _baseStopDistance = stopDistance;
        _baseCombatDistanceTolerance = combatDistanceTolerance;
        _baseStrafeSpeedMultiplier = strafeSpeedMultiplier;
        _baseRetreatSpeedMultiplier = retreatSpeedMultiplier;
        _baseMinStrafeSwitchInterval = minStrafeSwitchInterval;
        _baseMaxStrafeSwitchInterval = maxStrafeSwitchInterval;
        _basePostShotStrafeDuration = postShotStrafeDuration;
        _baseFireFacingAngleThreshold = fireFacingAngleThreshold;
        _baseTelegraphFacingAngleThreshold = telegraphFacingAngleThreshold;
        _baseFireDistance = fireDistance;
        _baseFireCooldown = fireCooldown;
        _baseProjectileSpeed = projectileSpeed;
        _baseLightHitStun = lightHitStun;
        _baseHeavyHitStun = heavyHitStun;
        _baseHitPushDistance = hitPushDistance;
    }

    void ApplyCombatRoleTuning()
    {
        CacheBaseCombatTuning();

        moveSpeed = _baseMoveSpeed;
        stopDistance = _baseStopDistance;
        combatDistanceTolerance = _baseCombatDistanceTolerance;
        strafeSpeedMultiplier = _baseStrafeSpeedMultiplier;
        retreatSpeedMultiplier = _baseRetreatSpeedMultiplier;
        minStrafeSwitchInterval = _baseMinStrafeSwitchInterval;
        maxStrafeSwitchInterval = _baseMaxStrafeSwitchInterval;
        postShotStrafeDuration = _basePostShotStrafeDuration;
        fireFacingAngleThreshold = _baseFireFacingAngleThreshold;
        telegraphFacingAngleThreshold = _baseTelegraphFacingAngleThreshold;
        fireDistance = _baseFireDistance;
        fireCooldown = _baseFireCooldown;
        projectileSpeed = _baseProjectileSpeed;
        lightHitStun = _baseLightHitStun;
        heavyHitStun = _baseHeavyHitStun;
        hitPushDistance = _baseHitPushDistance;

        switch (combatRole)
        {
            case DroneCombatRole.Suppressor:
                moveSpeed *= 0.92f;
                stopDistance += 1.5f;
                combatDistanceTolerance *= 0.85f;
                strafeSpeedMultiplier = Mathf.Clamp01(strafeSpeedMultiplier * 0.85f);
                retreatSpeedMultiplier = Mathf.Clamp(retreatSpeedMultiplier * 0.9f, 0.1f, 1.2f);
                postShotStrafeDuration *= 0.75f;
                fireDistance += 2.4f;
                fireCooldown *= 0.86f;
                projectileSpeed *= 1.08f;
                fireFacingAngleThreshold += 2f;
                telegraphFacingAngleThreshold += 4f;
                break;

            case DroneCombatRole.Flanker:
                moveSpeed *= 1.14f;
                stopDistance = Mathf.Max(4f, stopDistance - 1.35f);
                combatDistanceTolerance *= 1.15f;
                strafeSpeedMultiplier = Mathf.Clamp01(strafeSpeedMultiplier * 1.35f);
                retreatSpeedMultiplier = Mathf.Clamp(retreatSpeedMultiplier * 0.95f, 0.1f, 1.2f);
                minStrafeSwitchInterval *= 0.72f;
                maxStrafeSwitchInterval *= 0.72f;
                postShotStrafeDuration += 0.25f;
                fireDistance = Mathf.Max(stopDistance + 2f, fireDistance - 0.75f);
                fireCooldown *= 1.04f;
                break;

            case DroneCombatRole.Skirmisher:
                moveSpeed *= 1.2f;
                stopDistance = Mathf.Max(4.5f, stopDistance - 0.55f);
                combatDistanceTolerance *= 1.2f;
                strafeSpeedMultiplier = Mathf.Clamp01(strafeSpeedMultiplier * 1.12f);
                retreatSpeedMultiplier = Mathf.Clamp(retreatSpeedMultiplier * 1.18f, 0.1f, 1.3f);
                minStrafeSwitchInterval *= 0.62f;
                maxStrafeSwitchInterval *= 0.62f;
                postShotStrafeDuration += 0.18f;
                fireCooldown *= 0.94f;
                projectileSpeed *= 1.05f;
                lightHitStun *= 0.85f;
                heavyHitStun *= 0.9f;
                hitPushDistance *= 0.9f;
                break;
        }

        if (applyPerInstanceVariance)
            ApplyCombatRoleVariance();

        fireDistance = Mathf.Max(stopDistance + 2f, fireDistance);
        minStrafeSwitchInterval = Mathf.Max(0.1f, minStrafeSwitchInterval);
        maxStrafeSwitchInterval = Mathf.Max(minStrafeSwitchInterval, maxStrafeSwitchInterval);
    }

    void ApplyCombatRoleVariance()
    {
        float cdVariance = Mathf.Clamp(fireCooldownVariance, 0f, 0.25f);
        float distVariance = Mathf.Max(0f, combatDistanceVariance);

        if (cdVariance > 0.001f)
            fireCooldown *= UnityEngine.Random.Range(1f - cdVariance, 1f + cdVariance);

        if (distVariance > 0.001f)
        {
            stopDistance = Mathf.Max(3.5f, stopDistance + UnityEngine.Random.Range(-distVariance, distVariance));
            fireDistance = Mathf.Max(stopDistance + 2f, fireDistance + UnityEngine.Random.Range(-distVariance, distVariance));
        }

        moveSpeed *= UnityEngine.Random.Range(0.96f, 1.06f);
    }

    void Update()
    {
        UpdateHitFlash();

        if (!lightweightSimulationMode)
            return;

        if (Time.time < _nextLightweightTickAt)
            return;

        _nextLightweightTickAt = Time.time + Mathf.Max(0.016f, lightweightTickInterval);
        TickMovementAndFire(Mathf.Max(lightweightTickInterval, Time.deltaTime));
    }

    void FixedUpdate()
    {
        if (lightweightSimulationMode)
            return;

        TickMovementAndFire(Time.fixedDeltaTime);
    }

    void TickMovementAndFire(float deltaTime)
    {
        if (target == null)
            return;
        if (_isDead)
            return;

        Vector3 toTarget = target.position - transform.position;
        Vector3 flatDirection = new Vector3(toTarget.x, 0f, toTarget.z);
        float distanceSqr = flatDirection.sqrMagnitude;
        if (distanceSqr > 0.0001f)
        {
            float distance = Mathf.Sqrt(distanceSqr);
            Vector3 flatNormalized = flatDirection / distance;
            Quaternion lookRotation = Quaternion.LookRotation(flatNormalized, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, lookRotation, turnSpeedDeg * deltaTime);

            if (TickHitReaction(deltaTime))
                return;

            Vector3 moveDirection = ResolveCombatMoveDirection(flatNormalized, distance);
            if (moveDirection.sqrMagnitude > 0.0001f)
            {
                float speedMultiplier = ResolveCombatMoveSpeedMultiplier(distance);
                Vector3 moveStep = moveDirection * (moveSpeed * speedMultiplier * deltaTime);
                MoveWithCollision(moveStep);
            }
        }

        if (TickHitReaction(deltaTime))
            return;

        if (distanceSqr <= fireDistance * fireDistance)
        {
            float facingAngle = distanceSqr > 0.0001f
                ? Vector3.Angle(transform.forward, flatDirection / Mathf.Sqrt(distanceSqr))
                : 0f;

            if (_attackTelegraphLeadTime > 0f && !_attackTelegraphIssuedForCurrentShot)
            {
                float telegraphStartTime = _nextFireTime - _attackTelegraphLeadTime;
                if (Time.time >= telegraphStartTime && facingAngle <= telegraphFacingAngleThreshold)
                {
                    _attackTelegraphIssuedForCurrentShot = true;
                    AttackTelegraphed?.Invoke(this);

                    if (Time.time > telegraphStartTime + 0.01f)
                        _nextFireTime = Time.time + _attackTelegraphLeadTime;
                }
            }

            if (Time.time < _nextFireTime)
                return;

            if (facingAngle > fireFacingAngleThreshold)
                return;

            Fire();
            _nextFireTime = Time.time + fireCooldown;
            _attackTelegraphIssuedForCurrentShot = _attackTelegraphLeadTime <= 0f;
            _postShotStrafeUntil = Time.time + Mathf.Max(0f, postShotStrafeDuration);
            FlipStrafeDirection();
        }
    }

    Vector3 ResolveCombatMoveDirection(Vector3 toTargetNormalized, float distanceToTarget)
    {
        float tolerance = Mathf.Max(0.1f, combatDistanceTolerance);
        float desiredMin = Mathf.Max(0.5f, stopDistance - tolerance);
        float desiredMax = stopDistance + tolerance;

        if (distanceToTarget > desiredMax)
            return toTargetNormalized;

        if (distanceToTarget < desiredMin)
            return -toTargetNormalized;

        UpdateStrafeDirection();

        Vector3 strafe = Vector3.Cross(Vector3.up, toTargetNormalized) * _strafeSign;
        float radialBias = 0f;
        if (distanceToTarget > stopDistance + tolerance * 0.35f)
            radialBias = 0.2f;
        else if (distanceToTarget < stopDistance - tolerance * 0.35f)
            radialBias = -0.2f;

        Vector3 desired = strafe + (toTargetNormalized * radialBias);
        desired.y = 0f;
        return desired.sqrMagnitude > 0.0001f ? desired.normalized : Vector3.zero;
    }

    float ResolveCombatMoveSpeedMultiplier(float distanceToTarget)
    {
        float tolerance = Mathf.Max(0.1f, combatDistanceTolerance);
        float desiredMin = Mathf.Max(0.5f, stopDistance - tolerance);
        float desiredMax = stopDistance + tolerance;

        if (distanceToTarget > desiredMax)
            return 1f;

        if (distanceToTarget < desiredMin)
            return Mathf.Clamp(retreatSpeedMultiplier, 0.1f, 1f);

        float strafeMultiplier = Mathf.Clamp(strafeSpeedMultiplier, 0.1f, 1f);
        if (Time.time < _postShotStrafeUntil)
            return Mathf.Min(1f, strafeMultiplier + 0.12f);

        return strafeMultiplier;
    }

    void UpdateStrafeDirection()
    {
        if (Time.time < _nextStrafeSwitchAt)
            return;

        FlipStrafeDirection(UnityEngine.Random.value < 0.5f ? -1 : 1);
    }

    void ResetCombatMovementState()
    {
        _strafeSign = UnityEngine.Random.value < 0.5f ? -1 : 1;
        _postShotStrafeUntil = 0f;
        _nextStrafeSwitchAt = Time.time + RandomStrafeSwitchInterval();
    }

    void FlipStrafeDirection()
    {
        FlipStrafeDirection(-_strafeSign);
    }

    void FlipStrafeDirection(int nextSign)
    {
        _strafeSign = nextSign >= 0 ? 1 : -1;
        _nextStrafeSwitchAt = Time.time + RandomStrafeSwitchInterval();
    }

    float RandomStrafeSwitchInterval()
    {
        float minInterval = Mathf.Max(0.1f, minStrafeSwitchInterval);
        float maxInterval = Mathf.Max(minInterval, maxStrafeSwitchInterval);
        return UnityEngine.Random.Range(minInterval, maxInterval);
    }

    bool TickHitReaction(float deltaTime)
    {
        bool inRecover = Time.time < _hitRecoverUntil;

        if (_hitPushDistanceRemaining > 0.0001f && _hitPushDirection.sqrMagnitude > 0.0001f)
        {
            float pushStep = Mathf.Min(_hitPushDistanceRemaining, _hitPushSpeed * deltaTime);
            Vector3 pushDelta = _hitPushDirection * pushStep;
            MoveWithCollision(pushDelta);

            _hitPushDistanceRemaining -= pushStep;
        }

        return inRecover;
    }

    void InitializeRendererState()
    {
        if (droneRenderer == null)
            return;

        _colorBlock ??= new MaterialPropertyBlock();
        Material sharedMaterial = droneRenderer.sharedMaterial;
        if (sharedMaterial == null)
            return;

        _hasBaseColorProperty = sharedMaterial.HasProperty("_BaseColor");
        _hasColorProperty = sharedMaterial.HasProperty("_Color");

        if (_hasBaseColorProperty)
            _originalColor = sharedMaterial.GetColor("_BaseColor");
        else if (_hasColorProperty)
            _originalColor = sharedMaterial.GetColor("_Color");
    }

    void CacheChildRenderers()
    {
        if (_cachedRenderers == null || _cachedRenderers.Length == 0)
            _cachedRenderers = GetComponentsInChildren<Renderer>(true);
    }

    void CacheColliders()
    {
        if (_cachedColliders == null || _cachedColliders.Length == 0)
            _cachedColliders = GetComponentsInChildren<Collider>(true);
    }

    void ResolveMovementCollider()
    {
        _movementCollider = null;
        if (_cachedColliders == null || _cachedColliders.Length == 0)
            return;

        for (int i = 0; i < _cachedColliders.Length; i++)
        {
            Collider col = _cachedColliders[i];
            if (col == null || col.isTrigger)
                continue;

            _movementCollider = col;
            return;
        }
    }

    Renderer ResolvePrimaryRenderer()
    {
        if (_cachedRenderers == null || _cachedRenderers.Length == 0)
            return GetComponentInChildren<Renderer>(true);

        for (int i = 0; i < _cachedRenderers.Length; i++)
        {
            Renderer renderer = _cachedRenderers[i];
            if (renderer == null)
                continue;

            if (renderer is SkinnedMeshRenderer)
                return renderer;

            MeshFilter meshFilter = renderer.GetComponent<MeshFilter>();
            if (meshFilter != null && meshFilter.sharedMesh != null)
                return renderer;
        }

        return _cachedRenderers[0];
    }

    void ApplyRendererPerformanceOverrides()
    {
        if (!optimizeChildRenderers || _cachedRenderers == null)
            return;

        for (int i = 0; i < _cachedRenderers.Length; i++)
        {
            Renderer renderer = _cachedRenderers[i];
            if (renderer == null)
                continue;

            if (disableRendererShadows)
            {
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }

            if (disableMotionVectors)
                renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        }
    }

    void UpdateHitFlash()
    {
        if (_hitFlashUntil > Time.time)
        {
            if (!_flashApplied)
            {
                ApplyRendererColor(hitFlashColor);
                _flashApplied = true;
            }

            return;
        }

        if (!_flashApplied)
            return;

        _flashApplied = false;
        ApplyRendererColor(_originalColor);
    }

    void ApplyRendererColor(Color color)
    {
        if (droneRenderer == null || (!_hasBaseColorProperty && !_hasColorProperty))
            return;

        MaterialPropertyBlock colorBlock = GetOrCreateColorBlock();
        droneRenderer.GetPropertyBlock(colorBlock);
        if (_hasBaseColorProperty)
            colorBlock.SetColor("_BaseColor", color);
        if (_hasColorProperty)
            colorBlock.SetColor("_Color", color);
        droneRenderer.SetPropertyBlock(colorBlock);
    }

    MaterialPropertyBlock GetOrCreateColorBlock()
    {
        _colorBlock ??= new MaterialPropertyBlock();
        return _colorBlock;
    }

    void Fire()
    {
        if (projectilePrefab == null || fireOrigin == null)
            return;

        Vector3 spawnPosition = fireOrigin.position + fireOrigin.forward * projectileSpawnForwardOffset;
        GameObject bullet = RuntimeObjectPool.Acquire(projectilePrefab, spawnPosition, fireOrigin.rotation);
        if (bullet == null)
            return;

        BulletProjectile bulletProjectile = bullet.GetComponent<BulletProjectile>();
        if (bulletProjectile != null)
            bulletProjectile.Init(gameObject, projectileSpeed, projectileDamage);

        TutorialProjectile tutorialProjectile = bullet.GetComponent<TutorialProjectile>();
        if (tutorialProjectile != null)
            tutorialProjectile.owner = transform;

        Fired?.Invoke(this);
    }

    public void ReceiveHit(HitPayload payload)
    {
        if (_isDead)
            return;

        int beforeHp = _currentHP;
        TakeDamage(Mathf.RoundToInt(payload.damage), payload.hitPoint, payload.hitDirection, payload.hitType);

        if (_currentHP < beforeHp)
            CombatRewardUtility.TryGrantBasicAttackGauge(payload.attacker);
    }

    public void TakeDamage(int amount, Vector3 hitPoint, Vector3 hitDir)
    {
        TakeDamage(amount, hitPoint, hitDir, HitType.Normal);
    }

    void TakeDamage(int amount, Vector3 hitPoint, Vector3 hitDir, HitType hitType)
    {
        if (_isDead || _currentHP <= 0 || amount <= 0)
            return;

        _currentHP -= amount;
        if (debugLogs)
            Debug.Log($"[Drone] Hit {amount} => {_currentHP}/{maxHP}", this);

        if (hitVFX != null)
        {
            Quaternion rotation = hitDir.sqrMagnitude > 0.0001f
                ? Quaternion.LookRotation(-hitDir.normalized, Vector3.up)
                : Quaternion.identity;
            TransientVfxPool.Spawn(hitVFX, hitPoint, rotation, null, 1f);
        }

        _hitFlashUntil = Time.time + Mathf.Max(0.05f, hitFlashDuration);
        _flashApplied = false;
        ApplyHitReaction(hitDir, hitType);

        if (_currentHP <= 0)
            Die();
    }

    void ApplyHitReaction(Vector3 hitDir, HitType hitType)
    {
        float stunDuration = ResolveHitStun(hitType);
        if (stunDuration > 0f)
            _hitRecoverUntil = Mathf.Max(_hitRecoverUntil, Time.time + stunDuration);

        Vector3 planarDirection = hitDir;
        planarDirection.y = 0f;
        if (planarDirection.sqrMagnitude > 0.0001f)
        {
            _hitPushDirection = planarDirection.normalized;
            float pushDistance = ResolveHitPushDistance(hitType);
            _hitPushDistanceRemaining = Mathf.Max(_hitPushDistanceRemaining, pushDistance);
            _hitPushSpeed = pushDistance / Mathf.Max(0.04f, stunDuration);
        }

        float delayedFireTime = Time.time + Mathf.Max(0f, hitFireDelay);
        if (interruptTelegraphOnHit)
        {
            _attackTelegraphIssuedForCurrentShot = _attackTelegraphLeadTime <= 0f;
            delayedFireTime = Mathf.Max(delayedFireTime, Time.time + _attackTelegraphLeadTime);
        }

        _nextFireTime = Mathf.Max(_nextFireTime, delayedFireTime);

        if (cancelPostShotStrafeOnHit)
            _postShotStrafeUntil = 0f;

        FlipStrafeDirection();
    }

    float ResolveHitStun(HitType hitType)
    {
        switch (hitType)
        {
            case HitType.Heavy:
            case HitType.Strong:
            case HitType.Force:
            case HitType.Parried:
                return Mathf.Max(0f, heavyHitStun);

            default:
                return Mathf.Max(0f, lightHitStun);
        }
    }

    float ResolveHitPushDistance(HitType hitType)
    {
        float baseDistance = Mathf.Max(0f, hitPushDistance);
        switch (hitType)
        {
            case HitType.Heavy:
            case HitType.Force:
                return baseDistance * 1.25f;

            case HitType.Strong:
            case HitType.Parried:
                return baseDistance * 1.1f;

            default:
                return baseDistance;
        }
    }

    void Die()
    {
        if (_isDead)
            return;

        _isDead = true;
        if (debugLogs)
            Debug.Log("[Drone] Destroyed", this);

        if (deathVFX != null)
            TransientVfxPool.Spawn(deathVFX, transform.position, Quaternion.identity, null, 2f);

        Died?.Invoke(this);
        BeginDeathState();

        if (_deathRoutine != null)
            StopCoroutine(_deathRoutine);
        _deathRoutine = StartCoroutine(CoDeathCleanup());
    }

    void BeginDeathState()
    {
        _hitRecoverUntil = 0f;
        _hitPushDistanceRemaining = 0f;
        _hitPushSpeed = 0f;
        _nextFireTime = float.PositiveInfinity;
        _attackTelegraphIssuedForCurrentShot = true;
        _postShotStrafeUntil = 0f;

        if (_rigidbody != null)
        {
            _rigidbody.velocity = Vector3.zero;
            _rigidbody.angularVelocity = Vector3.zero;
            _rigidbody.isKinematic = true;
        }

        if (!disableCollidersImmediatelyOnDeath)
            return;

        CacheColliders();
        for (int i = 0; i < _cachedColliders.Length; i++)
        {
            Collider col = _cachedColliders[i];
            if (col != null)
                col.enabled = false;
        }
    }

    void RestoreDeathState()
    {
        CacheColliders();
        for (int i = 0; i < _cachedColliders.Length; i++)
        {
            Collider col = _cachedColliders[i];
            if (col != null)
                col.enabled = true;
        }

        if (_rigidbody != null)
            _rigidbody.isKinematic = lightweightSimulationMode;

        ResolveMovementCollider();
    }

    void MoveWithCollision(Vector3 desiredDelta)
    {
        if (desiredDelta.sqrMagnitude <= 0.0000001f)
            return;

        Vector3 resolvedDelta = useCollisionAwareMovement
            ? ResolveCollisionAwareDelta(desiredDelta)
            : desiredDelta;
        if (resolvedDelta.sqrMagnitude <= 0.0000001f)
            return;

        if (_rigidbody != null && !lightweightSimulationMode)
            _rigidbody.MovePosition(_rigidbody.position + resolvedDelta);
        else
            transform.position += resolvedDelta;
    }

    Vector3 ResolveCollisionAwareDelta(Vector3 desiredDelta)
    {
        if (_movementCollider == null || !_movementCollider.enabled)
            return desiredDelta;

        float distance = desiredDelta.magnitude;
        if (distance <= 0.0001f)
            return desiredDelta;

        Vector3 direction = desiredDelta / distance;
        int hitCount = CastMovementShape(direction, distance + movementSkin);
        if (hitCount <= 0)
            return desiredDelta;

        float nearestDistance = float.PositiveInfinity;
        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = _movementCastHits[i];
            Collider hitCollider = hit.collider;
            if (hitCollider == null)
                continue;
            if (hitCollider.transform.root == transform.root)
                continue;
            if (((1 << hitCollider.gameObject.layer) & movementCollisionMask) == 0)
                continue;

            if (hit.distance < nearestDistance)
                nearestDistance = hit.distance;
        }

        if (!float.IsFinite(nearestDistance))
            return desiredDelta;

        float allowedDistance = Mathf.Max(0f, nearestDistance - movementSkin);
        if (allowedDistance <= 0.0001f)
            return Vector3.zero;

        return direction * Mathf.Min(distance, allowedDistance);
    }

    int CastMovementShape(Vector3 direction, float distance)
    {
        const QueryTriggerInteraction QueryMode = QueryTriggerInteraction.Ignore;

        if (_movementCollider is SphereCollider sphereCollider)
        {
            Vector3 center = sphereCollider.transform.TransformPoint(sphereCollider.center);
            Vector3 absScale = AbsVector(sphereCollider.transform.lossyScale);
            float radius = sphereCollider.radius * Mathf.Max(absScale.x, Mathf.Max(absScale.y, absScale.z));
            return Physics.SphereCastNonAlloc(
                center,
                Mathf.Max(0.01f, radius),
                direction,
                _movementCastHits,
                distance,
                movementCollisionMask,
                QueryMode);
        }

        if (_movementCollider is CapsuleCollider capsuleCollider)
        {
            GetCapsuleWorldPoints(capsuleCollider, out Vector3 point0, out Vector3 point1, out float radius);
            return Physics.CapsuleCastNonAlloc(
                point0,
                point1,
                Mathf.Max(0.01f, radius),
                direction,
                _movementCastHits,
                distance,
                movementCollisionMask,
                QueryMode);
        }

        if (_movementCollider is BoxCollider boxCollider)
        {
            Vector3 center = boxCollider.transform.TransformPoint(boxCollider.center);
            Vector3 halfExtents = Vector3.Scale(boxCollider.size * 0.5f, AbsVector(boxCollider.transform.lossyScale));
            return Physics.BoxCastNonAlloc(
                center,
                MaxVector(halfExtents, Vector3.one * 0.01f),
                direction,
                _movementCastHits,
                boxCollider.transform.rotation,
                distance,
                movementCollisionMask,
                QueryMode);
        }

        Bounds bounds = _movementCollider.bounds;
        float fallbackRadius = Mathf.Max(0.05f, Mathf.Min(bounds.extents.x, bounds.extents.z));
        return Physics.SphereCastNonAlloc(
            bounds.center,
            fallbackRadius,
            direction,
            _movementCastHits,
            distance,
            movementCollisionMask,
            QueryMode);
    }

    void GetCapsuleWorldPoints(CapsuleCollider capsuleCollider, out Vector3 point0, out Vector3 point1, out float radius)
    {
        Transform capsuleTransform = capsuleCollider.transform;
        Vector3 absScale = AbsVector(capsuleTransform.lossyScale);
        int axis = Mathf.Clamp(capsuleCollider.direction, 0, 2);
        float axisScale = axis == 0 ? absScale.x : axis == 1 ? absScale.y : absScale.z;
        float radiusScale = axis == 0
            ? Mathf.Max(absScale.y, absScale.z)
            : axis == 1
                ? Mathf.Max(absScale.x, absScale.z)
                : Mathf.Max(absScale.x, absScale.y);

        radius = capsuleCollider.radius * radiusScale;
        float height = Mathf.Max(capsuleCollider.height * axisScale, radius * 2f);
        float halfSegment = Mathf.Max(0f, (height * 0.5f) - radius);
        Vector3 center = capsuleTransform.TransformPoint(capsuleCollider.center);
        Vector3 axisDirection = capsuleTransform.TransformDirection(axis == 0 ? Vector3.right : axis == 1 ? Vector3.up : Vector3.forward).normalized;
        point0 = center + axisDirection * halfSegment;
        point1 = center - axisDirection * halfSegment;
    }

    static Vector3 AbsVector(Vector3 value)
    {
        return new Vector3(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));
    }

    static Vector3 MaxVector(Vector3 a, Vector3 b)
    {
        return new Vector3(
            Mathf.Max(a.x, b.x),
            Mathf.Max(a.y, b.y),
            Mathf.Max(a.z, b.z));
    }

    System.Collections.IEnumerator CoDeathCleanup()
    {
        float duration = Mathf.Max(0.05f, deathDespawnDelay);
        float scaleDuration = Mathf.Clamp(deathScaleDownDuration, 0.05f, duration);
        Vector3 startScale = _initialLocalScale;
        Vector3 endScale = Vector3.zero;
        Vector3 startPosition = transform.position;
        Vector3 endPosition = startPosition + Vector3.up * Mathf.Max(0f, deathRiseDistance);
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float positionT = Mathf.Clamp01(elapsed / duration);
            float scaleT = Mathf.Clamp01(elapsed / scaleDuration);
            transform.position = Vector3.Lerp(startPosition, endPosition, positionT);
            transform.localScale = Vector3.LerpUnclamped(startScale, endScale, scaleT);
            yield return null;
        }

        transform.localScale = _initialLocalScale;
        _deathRoutine = null;

        RuntimePooledObject pooled = GetComponent<RuntimePooledObject>();
        if (pooled != null && pooled.prefabKey != 0)
            RuntimeObjectPool.Release(gameObject);
        else
            Destroy(gameObject);
    }

}
