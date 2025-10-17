// DroneController.cs
// 부모/자식 구조 자동 탐지. 수평 거리 유지(stopDistance). 안전한 발사(spawn 검사).
using System;
using UnityEngine;

[DisallowMultipleComponent]
public class DroneController : MonoBehaviour, IHitReceiver
{
    [Header("▶ 타겟 (비워두면 Player 태그 자동 검색)")]
    public Transform target;

    [Header("▶ 이동 파라미터")]
    public float moveSpeed = 1.5f;
    public float stopDistance = 12f;
    public float turnSpeedDeg = 360f;

    [Header("▶ 사격 파라미터")]
    public GameObject projectilePrefab;
    public Transform fireOrigin;
    public float projectileSpawnForwardOffset = 0.6f;
    public float fireDistance = 12f;
    public float fireCooldown = 1.2f;
    public float projectileSpeed = 12f;
    public int projectileDamage = 15;

    [Header("▶ 체력")]
    public int maxHP = 100;
    public GameObject deathVFX;

    [Header("▶ 안전/디버그")]
    public float spawnSafetyRadius = 0.25f;
    public float spawnAdvanceStep = 0.25f;
    public int spawnAdvanceAttempts = 6;

    Rigidbody _rb;
    Collider _col;
    Transform _root;
    Transform _fireOriginCached;
    int _hp;
    float _nextFireTime;

    void Awake()
    {
        _root = transform.root ? transform.root : transform;
        _rb = GetComponent<Rigidbody>() ?? GetComponentInParent<Rigidbody>() ?? GetComponentInChildren<Rigidbody>();
        _col = GetComponent<Collider>() ?? GetComponentInParent<Collider>() ?? GetComponentInChildren<Collider>();

        if (_rb != null)
        {
            _rb.useGravity = false;
            try { _rb.freezeRotation = true; } catch { }
        }

        if (fireOrigin != null) _fireOriginCached = fireOrigin;
        else
        {
            _fireOriginCached = FindTransformByNames(_root, new string[] { "FireOrigin", "fireOrigin", "Muzzle", "muzzle", "firingPoint" })
                                ?? FindTransformByPredicate(_root, t => t.name.ToLower().Contains("fire") || t.name.ToLower().Contains("muzzle"))
                                ?? transform;
        }

        if (target == null)
        {
            var p = GameObject.FindWithTag("Player");
            if (p) target = p.transform;
        }

        _hp = Mathf.Max(1, maxHP);
        Debug.Log($"[Drone] Awake root={_root.name} rb={(_rb? _rb.name : "null")} col={(_col? _col.name : "null")} fireOrigin={_fireOriginCached?.name}", this);
    }

    Transform FindTransformByNames(Transform root, string[] names)
    {
        if (root == null) return null;
        foreach (var n in names) if (string.Equals(root.name, n, StringComparison.OrdinalIgnoreCase)) return root;
        foreach (Transform c in root)
        {
            var r = FindTransformByNames(c, names);
            if (r != null) return r;
        }
        return null;
    }

    Transform FindTransformByPredicate(Transform root, Func<Transform, bool> pred)
    {
        if (root == null) return null;
        if (pred(root)) return root;
        foreach (Transform c in root)
        {
            var r = FindTransformByPredicate(c, pred);
            if (r != null) return r;
        }
        return null;
    }

    void FixedUpdate()
    {
        if (target == null) return;

        Vector3 currentPos = _rb != null ? _rb.position : transform.position;
        Vector3 toTarget = target.position - currentPos;
        Vector3 flat = new Vector3(toTarget.x, 0f, toTarget.z);
        float horizDist = flat.magnitude;

        if (flat.sqrMagnitude > 0.0001f)
        {
            Quaternion want = Quaternion.LookRotation(flat.normalized, Vector3.up);
            Quaternion cur = _rb != null ? _rb.rotation : transform.rotation;
            Quaternion next = Quaternion.RotateTowards(cur, want, turnSpeedDeg * Time.fixedDeltaTime);
            if (_rb != null) _rb.MoveRotation(next); else transform.rotation = next;
        }

        if (horizDist > stopDistance + 0.05f)
        {
            Vector3 moveDir = flat.normalized;
            Vector3 nextPos = currentPos + moveDir * moveSpeed * Time.fixedDeltaTime;
            if (_rb != null) _rb.MovePosition(nextPos); else transform.position = nextPos;
        }
        else if (horizDist < stopDistance - 0.05f)
        {
            Vector3 moveDir = -flat.normalized;
            Vector3 nextPos = currentPos + moveDir * moveSpeed * Time.fixedDeltaTime;
            if (_rb != null) _rb.MovePosition(nextPos); else transform.position = nextPos;
        }

        if (horizDist <= fireDistance && Time.time >= _nextFireTime && projectilePrefab != null)
        {
            if (TryFireSafeAndInstantiate()) _nextFireTime = Time.time + fireCooldown;
            else Debug.Log("[Drone] Fire skipped - spawn unsafe", this);
        }
    }

    bool TryFireSafeAndInstantiate()
    {
        Transform origin = _fireOriginCached != null ? _fireOriginCached : transform;
        Vector3 spawnForward = (target != null) ? (new Vector3(target.position.x - origin.position.x, 0f, target.position.z - origin.position.z)).normalized : origin.forward.normalized;
        if (spawnForward.sqrMagnitude < 0.0001f) spawnForward = origin.forward.normalized;

        Vector3 spawnPos = origin.position + spawnForward * projectileSpawnForwardOffset;

        for (int i = 0; i < spawnAdvanceAttempts; i++)
        {
            Collider[] hits = Physics.OverlapSphere(spawnPos, spawnSafetyRadius);
            bool overlapPlayer = false;
            if (hits != null && hits.Length > 0)
            {
                foreach (var c in hits)
                {
                    if (c == null) continue;
                    if (target != null && (c.transform.IsChildOf(target) || c.gameObject == target.gameObject)) { overlapPlayer = true; break; }
                    if (c.gameObject.CompareTag("Player")) { overlapPlayer = true; break; }
                }
            }

            if (!overlapPlayer)
            {
                InstantiateProjectile(spawnPos, spawnForward);
                return true;
            }

            spawnPos += spawnForward * spawnAdvanceStep;
        }

        return false;
    }

    void InstantiateProjectile(Vector3 spawnPos, Vector3 forward)
    {
        Quaternion rot = target != null
            ? Quaternion.LookRotation((new Vector3(target.position.x, spawnPos.y, target.position.z) - spawnPos).normalized, Vector3.up)
            : Quaternion.LookRotation(forward, Vector3.up);

        var go = Instantiate(projectilePrefab, spawnPos, rot);

        var homing = go.GetComponent<HomingProjectile>();
        if (homing != null) { homing.Init(target, projectileSpeed, this.gameObject, projectileDamage); return; }

        var bullet = go.GetComponent<BulletProjectile>();
        if (bullet != null) { bullet.Init(this.gameObject, projectileSpeed, projectileDamage); return; }

        go.transform.forward = forward;
    }

    public void ReceiveHit(HitData hit)
    {
        if (hit == null) return;
        ApplyDamage((int)hit.damage, hit);
    }

    public void ApplyDamage(int damage, HitData hitInfo = null)
    {
        if (damage <= 0) return;
        _hp -= damage;
        _hp = Mathf.Max(0, _hp);
        if (_hp <= 0) Die();
    }

    void Die()
    {
        if (deathVFX) Instantiate(deathVFX, transform.position, Quaternion.identity);
        Destroy(gameObject);
    }

    void OnDrawGizmosSelected()
    {
        if (_fireOriginCached != null)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(_fireOriginCached.position + _fireOriginCached.forward * projectileSpawnForwardOffset, spawnSafetyRadius);
        }
    }
}
