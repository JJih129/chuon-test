using UnityEngine;

[DisallowMultipleComponent]
public class TutorialDrone : MonoBehaviour
{
    [Header("Attack")]
    public GameObject projectilePrefab;
    public Transform firePoint;
    public float attackInterval = 3.5f;
    public float warningDuration = 1f;
    public float initialDelay = 1f;

    [Header("Target")]
    public Transform playerTarget;

    [Header("Visual")]
    public Renderer droneRenderer;
    public Color warningColor = Color.red;
    public float turnLerpSpeed = 5f;
    public float turnUpdateInterval = 1f / 30f;
    public float warningPulseFrequency = 4f;

    [Header("Performance")]
    [SerializeField] bool lightweightTickMode = true;
    [SerializeField, Min(0.016f)] float lightweightTickInterval = 0.05f;

    float _phaseEndTime;
    float _warningStartTime;
    float _nextTurnUpdateAt;
    float _nextLightweightTickAt;
    bool _warningActive;

    Color _normalColor = Color.white;
    MaterialPropertyBlock _colorBlock;
    bool _hasBaseColorProperty;
    bool _hasColorProperty;

    void Awake()
    {
        if (droneRenderer == null)
            droneRenderer = GetComponentInChildren<Renderer>();

        InitializeRendererState();
    }

    void OnEnable()
    {
        ResolvePlayerTarget();
        ResetAttackSchedule();
        _nextLightweightTickAt = Time.time;
        ApplyCurrentColor(_normalColor);
    }

    void OnDisable()
    {
        _warningActive = false;
        ApplyCurrentColor(_normalColor);
    }

    void Update()
    {
        if (!lightweightTickMode)
        {
            UpdateFacing();
            UpdateAttackSequence(Time.time);
            return;
        }

        if (Time.time < _nextLightweightTickAt)
            return;

        _nextLightweightTickAt = Time.time + Mathf.Max(0.016f, lightweightTickInterval);
        UpdateFacing();
        UpdateAttackSequence(Time.time);
    }

    void ResolvePlayerTarget()
    {
        if (playerTarget != null)
            return;

        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player != null)
            playerTarget = player.transform;
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
            _normalColor = sharedMaterial.GetColor("_BaseColor");
        else if (_hasColorProperty)
            _normalColor = sharedMaterial.GetColor("_Color");
    }

    void ResetAttackSchedule()
    {
        _warningActive = false;
        _warningStartTime = 0f;
        float cooldownBeforeWarning = Mathf.Max(0.1f, attackInterval - warningDuration - 0.5f);
        _phaseEndTime = Time.time + Mathf.Max(0f, initialDelay) + cooldownBeforeWarning;
    }

    void UpdateFacing()
    {
        if (playerTarget == null || Time.time < _nextTurnUpdateAt)
            return;

        _nextTurnUpdateAt = Time.time + Mathf.Max(0.01f, turnUpdateInterval);

        Vector3 dir = playerTarget.position - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude <= 0.0001f)
            return;

        Quaternion lookRot = Quaternion.LookRotation(dir.normalized, Vector3.up);
        float t = 1f - Mathf.Exp(-turnLerpSpeed * turnUpdateInterval);
        transform.rotation = Quaternion.Slerp(transform.rotation, lookRot, t);
    }

    void UpdateAttackSequence(float now)
    {
        if (!_warningActive)
        {
            if (now < _phaseEndTime)
                return;

            _warningActive = true;
            _warningStartTime = now;
            _phaseEndTime = now + Mathf.Max(0.1f, warningDuration);
            return;
        }

        float warningT = Mathf.PingPong((now - _warningStartTime) * warningPulseFrequency, 1f);
        ApplyCurrentColor(Color.Lerp(_normalColor, warningColor, warningT));

        if (now < _phaseEndTime)
            return;

        Fire();
        _warningActive = false;
        ApplyCurrentColor(_normalColor);
        _phaseEndTime = now + Mathf.Max(0.1f, attackInterval - warningDuration - 0.5f);
    }

    void Fire()
    {
        if (projectilePrefab == null || firePoint == null)
            return;

        GameObject bullet = RuntimeObjectPool.Acquire(projectilePrefab, firePoint.position, firePoint.rotation);
        if (bullet == null)
            return;

        TutorialProjectile projectile = bullet.GetComponent<TutorialProjectile>();
        if (projectile != null)
            projectile.owner = transform;
    }

    void ApplyCurrentColor(Color color)
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
}
