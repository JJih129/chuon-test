// DroneController.cs
// 설명: 부모/자식 구조 자동 탐지. 수평 거리 유지(stopDistance). 안전한 발사(spawn 검사).
using System;
using UnityEngine;

[DisallowMultipleComponent]
public class DroneController : MonoBehaviour, IHitReceiver
{
    [Header("▶ 타겟 (비워두면 Player 태그 자동 검색)")]
    [Tooltip("추적할 대상 Transform")]
    public Transform target;

    [Header("▶ 이동 파라미터")]
    [Tooltip("이동 속도 (m/s)")]
    public float moveSpeed = 1.5f;
    [Tooltip("유지할 수평 거리 (XZ 평면 기준)")]
    public float stopDistance = 12f;
    [Tooltip("회전 속도 (deg/s)")]
    public float turnSpeedDeg = 360f;

    [Header("▶ 사격 파라미터")]
    [Tooltip("발사체 프리팹 (HomingProjectile 또는 BulletProjectile)")]
    public GameObject projectilePrefab;
    [Tooltip("발사 기준 Transform. 비우면 자동 탐색(이름 FireOrigin 등)")]
    public Transform fireOrigin;
    [Tooltip("발사체를 전방으로 띄울 거리 (m)")]
    public float projectileSpawnForwardOffset = 0.6f;
    [Tooltip("사격 허용 수평 거리")]
    public float fireDistance = 12f;
    [Tooltip("발사 쿨다운(초)")]
    public float fireCooldown = 1.2f;
    [Tooltip("발사체 속도")]
    public float projectileSpeed = 12f;
    [Tooltip("발사체 데미지")]
    public int projectileDamage = 15;

    [Header("▶ 체력")]
    [Tooltip("최대 체력")]
    public int maxHP = 100;
    [Tooltip("사망 VFX (선택)")]
    public GameObject deathVFX;

    [Header("▶ 안전/디버그")]
    [Tooltip("spawnPos 검사 반경")]
    public float spawnSafetyRadius = 0.25f;
    [Tooltip("spawn 보정 단위 (타겟쪽으로 전진)")]
    public float spawnAdvanceStep = 0.25f;
    [Tooltip("spawn 보정 최대 시도")]
    public int spawnAdvanceAttempts = 6;

    // 내부
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

        // 회전 (타겟 방향)
        if (flat.sqrMagnitude > 0.0001f)
        {
            Quaternion want = Quaternion.LookRotation(flat.normalized, Vector3.up);
            Quaternion cur = _rb != null ? _rb.rotation : transform.rotation;
            Quaternion next = Quaternion.RotateTowards(cur, want, turnSpeedDeg * Time.fixedDeltaTime);
            if (_rb != null) _rb.MoveRotation(next); else transform.rotation = next;
        }

        // 이동: Stop distance 유지 (수평)
        if (horizDist > stopDistance + 0.05f)
        {
            Vector3 moveDir = flat.normalized;
            Vector3 nextPos = currentPos + moveDir * moveSpeed * Time.fixedDeltaTime;
            if (_rb != null) _rb.MovePosition(nextPos); else transform.position = nextPos;
        }

        // 사격 (수평 기준)
        if (horizDist <= fireDistance && Time.time >= _nextFireTime && projectilePrefab != null)
        {
            if (TryFireSafeAndInstantiate()) _nextFireTime = Time.time + fireCooldown;
            else Debug.Log("[Drone] Fire skipped - spawn unsafe", this);
        }
    }

    bool TryFireSafeAndInstantiate()
    {
        Transform origin = _fireOriginCached != null ? _fireOriginCached : transform;
        // spawn 방향은 타겟쪽으로 잡음(모델 계층 문제 보정)
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

    // IHitReceiver
    public void ReceiveHit(HitData hit)
    {
        if (hit == null) return;
        ApplyDamage(hit.damage, hit);
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
