using DG.Tweening;
using UnityEngine;

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
    public float fireCooldown = 2.0f;
    public float projectileSpeed = 15f;
    public int projectileDamage = 10;

    [Header("Health / Effects")]
    public int maxHP = 30;
    public GameObject hitVFX;
    public GameObject deathVFX;
    public Renderer droneRenderer;
    [SerializeField] bool debugLog = false;

    int currentHP;
    float nextFireTime;
    float _stopDistanceSq;
    float _fireDistanceSq;
    Rigidbody rb;
    Color originalColor;
    Material droneMaterial;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        currentHP = maxHP;
        CacheDistanceThresholds();

        if (droneRenderer == null)
            droneRenderer = GetComponentInChildren<Renderer>();

        if (droneRenderer != null)
        {
            droneMaterial = droneRenderer.material;
            originalColor = droneMaterial.color;
        }

        if (target == null)
            ResolveTarget();
    }

    void OnEnable()
    {
        currentHP = maxHP;
    }

    void OnValidate()
    {
        CacheDistanceThresholds();
    }

    void FixedUpdate()
    {
        if (target == null)
        {
            ResolveTarget();
            if (target == null) return;
        }

        Vector3 toTarget = target.position - transform.position;
        Vector3 flatDirection = new Vector3(toTarget.x, 0f, toTarget.z);

        if (flatDirection.sqrMagnitude > 0.000001f)
        {
            Quaternion lookRotation = Quaternion.LookRotation(flatDirection.normalized);
            transform.rotation = Quaternion.Slerp(transform.rotation, lookRotation, Time.fixedDeltaTime * 5f);
        }

        float distanceSq = toTarget.sqrMagnitude;
        Vector3 moveDirection = toTarget.normalized;

        if (distanceSq > _stopDistanceSq)
        {
            Vector3 movePos = transform.position + moveDirection * moveSpeed * Time.fixedDeltaTime;
            if (rb) rb.MovePosition(movePos);
            else transform.position = movePos;
        }

        if (distanceSq <= _fireDistanceSq && Time.time >= nextFireTime)
        {
            Fire();
            nextFireTime = Time.time + fireCooldown;
        }
    }

    void Fire()
    {
        if (!projectilePrefab || !fireOrigin) return;

        GameObject bullet = Instantiate(projectilePrefab, fireOrigin.position, fireOrigin.rotation);

        var bulletProjectile = bullet.GetComponent<BulletProjectile>();
        if (bulletProjectile) bulletProjectile.Init(gameObject, projectileSpeed, projectileDamage);

        var tutorialProjectile = bullet.GetComponent<TutorialProjectile>();
        if (tutorialProjectile) tutorialProjectile.owner = transform;
    }

    public void ReceiveHit(HitPayload payload)
    {
        TakeDamage((int)payload.damage, payload.hitPoint, payload.hitDirection);
    }

    public void TakeDamage(int amount, Vector3 hitPoint, Vector3 hitDir)
    {
        if (currentHP <= 0) return;

        currentHP -= amount;
        if (debugLog)
            Debug.Log($"[Drone] Hit. HP: {currentHP}/{maxHP}", this);

        if (hitVFX != null)
        {
            Quaternion rotation = Quaternion.LookRotation(-hitDir);
            GameObject vfx = Instantiate(hitVFX, hitPoint, rotation);
            Destroy(vfx, 1.0f);
        }

        if (droneMaterial != null)
        {
            droneMaterial.DOKill();
            droneMaterial.color = Color.red;
            droneMaterial.DOColor(originalColor, 0.2f);
        }

        transform.DOPunchPosition(hitDir.normalized * 0.5f, 0.2f);

        if (currentHP <= 0)
            Die();
    }

    void Die()
    {
        if (debugLog)
            Debug.Log("[Drone] Destroyed", this);

        if (deathVFX != null)
        {
            GameObject vfx = Instantiate(deathVFX, transform.position, Quaternion.identity);
            Destroy(vfx, 2.0f);
        }

        Destroy(gameObject);
    }

    void ResolveTarget()
    {
        var player = GameObject.FindWithTag("Player");
        if (player) target = player.transform;
    }

    void CacheDistanceThresholds()
    {
        _stopDistanceSq = stopDistance * stopDistance;
        _fireDistanceSq = fireDistance * fireDistance;
    }
}
