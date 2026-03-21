using UnityEngine;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
public class DroneController : MonoBehaviour, IDamageReceiver
{
    [Header("Target")]
    public Transform target;

    [Header("Movement")]
    public float moveSpeed = 3.5f;
    public float stopDistance = 8f;
    public float turnSpeedDeg = 360f;

    [Header("Attack")]
    public GameObject projectilePrefab;
    public Transform fireOrigin;
    public float fireDistance = 15f;
    public float fireCooldown = 2f;
    public float projectileSpeed = 15f;
    public int projectileDamage = 10;

    [Header("Health / VFX")]
    public int maxHP = 30;
    public GameObject hitVFX;
    public GameObject deathVFX;
    public Renderer droneRenderer;
    public Color hitFlashColor = Color.red;
    public float hitFlashDuration = 0.2f;

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
    float _nextLightweightTickAt;
    Renderer[] _cachedRenderers;

    void Awake()
    {
        _rigidbody = GetComponent<Rigidbody>();
        _currentHP = maxHP;

        if (droneRenderer == null)
            droneRenderer = GetComponentInChildren<Renderer>();

        InitializeRendererState();
        CacheChildRenderers();
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
        _currentHP = maxHP;
        _nextFireTime = _initialFireDelay > 0f ? Time.time + _initialFireDelay : 0f;
        _hitFlashUntil = 0f;
        _flashApplied = false;
        _nextLightweightTickAt = Time.time;
        CacheChildRenderers();
        ApplyRendererPerformanceOverrides();
        ApplySimulationMode();
        ApplyRendererColor(_originalColor);
    }

    public void SetInitialFireDelay(float delay)
    {
        _initialFireDelay = Mathf.Max(0f, delay);
        _nextFireTime = _initialFireDelay > 0f ? Time.time + _initialFireDelay : 0f;
    }

    public void SetLightweightSimulation(bool enabled, float tickInterval = 0.05f)
    {
        lightweightSimulationMode = enabled;
        lightweightTickInterval = Mathf.Max(0.016f, tickInterval);
        _nextLightweightTickAt = Time.time;
        ApplySimulationMode();
    }

    void ApplySimulationMode()
    {
        if (_rigidbody == null)
            return;

        _rigidbody.isKinematic = lightweightSimulationMode;
        _rigidbody.interpolation = RigidbodyInterpolation.None;
        _rigidbody.collisionDetectionMode = CollisionDetectionMode.Discrete;
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

        Vector3 toTarget = target.position - transform.position;
        Vector3 flatDirection = new Vector3(toTarget.x, 0f, toTarget.z);
        float distanceSqr = flatDirection.sqrMagnitude;
        if (distanceSqr > 0.0001f)
        {
            Vector3 flatNormalized = flatDirection / Mathf.Sqrt(distanceSqr);
            Quaternion lookRotation = Quaternion.LookRotation(flatNormalized, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, lookRotation, turnSpeedDeg * deltaTime);

            if (distanceSqr > stopDistance * stopDistance)
            {
                Vector3 moveStep = flatNormalized * moveSpeed * deltaTime;
                if (_rigidbody != null && !lightweightSimulationMode)
                    _rigidbody.MovePosition(transform.position + moveStep);
                else
                    transform.position += moveStep;
            }
        }

        if (distanceSqr <= fireDistance * fireDistance && Time.time >= _nextFireTime)
        {
            Fire();
            _nextFireTime = Time.time + fireCooldown;
        }
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

        droneRenderer.GetPropertyBlock(_colorBlock);
        if (_hasBaseColorProperty)
            _colorBlock.SetColor("_BaseColor", color);
        if (_hasColorProperty)
            _colorBlock.SetColor("_Color", color);
        droneRenderer.SetPropertyBlock(_colorBlock);
    }

    void Fire()
    {
        if (projectilePrefab == null || fireOrigin == null)
            return;

        GameObject bullet = RuntimeObjectPool.Acquire(projectilePrefab, fireOrigin.position, fireOrigin.rotation);
        if (bullet == null)
            return;

        BulletProjectile bulletProjectile = bullet.GetComponent<BulletProjectile>();
        if (bulletProjectile != null)
            bulletProjectile.Init(gameObject, projectileSpeed, projectileDamage);

        TutorialProjectile tutorialProjectile = bullet.GetComponent<TutorialProjectile>();
        if (tutorialProjectile != null)
            tutorialProjectile.owner = transform;
    }

    public void ReceiveHit(HitPayload payload)
    {
        int beforeHp = _currentHP;
        TakeDamage(Mathf.RoundToInt(payload.damage), payload.hitPoint, payload.hitDirection);

        if (_currentHP < beforeHp)
            TryGrantBasicAttackGauge(payload.attacker);
    }

    public void TakeDamage(int amount, Vector3 hitPoint, Vector3 hitDir)
    {
        if (_currentHP <= 0 || amount <= 0)
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

        if (_currentHP <= 0)
            Die();
    }

    void Die()
    {
        if (debugLogs)
            Debug.Log("[Drone] Destroyed", this);

        if (deathVFX != null)
            TransientVfxPool.Spawn(deathVFX, transform.position, Quaternion.identity, null, 2f);

        Destroy(gameObject);
    }

    void TryGrantBasicAttackGauge(Transform attacker)
    {
        if (attacker == null)
            return;

        PlayerUltimateController ultimate = attacker.GetComponent<PlayerUltimateController>();
        if (ultimate == null)
            ultimate = attacker.GetComponentInParent<PlayerUltimateController>();

        if (ultimate != null && ultimate.gaugePerH > 0f)
            ultimate.AddGauge(ultimate.gaugePerH);
    }
}
