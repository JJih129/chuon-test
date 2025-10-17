// HomingProjectile.cs
using System;
using UnityEngine;

[RequireComponent(typeof(Collider))]
public class HomingProjectile : MonoBehaviour
{
    [Header("유도/운동")]
    public Transform target;
    public float speed = 12f;
    public float turnSpeedDeg = 720f;
    public float lifeTime = 8f;
    public int damage = 15;

    [Header("발사자/그레이스")]
    public GameObject owner;
    public float graceDuration = 0.12f;

    Collider _col;
    Rigidbody _rb;
    float _spawnTime;

    void Awake()
    {
        _col = GetComponent<Collider>();
        _rb = GetComponent<Rigidbody>();
        if (_rb != null) _rb.useGravity = false;
    }

    public void Init(Transform t, float initialSpeed, GameObject from, int dmg = -1)
    {
        target = t;
        speed = initialSpeed;
        owner = from;
        if (dmg > 0) damage = dmg;
        _spawnTime = Time.time;

        if (owner != null && _col != null)
        {
            var attackerCols = owner.GetComponentsInChildren<Collider>(true);
            foreach (var ac in attackerCols) if (ac != null)
                try { Physics.IgnoreCollision(_col, ac, true); } catch { }
        }

        Debug.Log($"[HomingProjectile] Init target={(t? t.name : "null")} speed={initialSpeed} owner={(from? from.name : "null")} dmg={damage}", this);
        Destroy(gameObject, lifeTime);
    }

    void FixedUpdate()
    {
        if (target != null)
        {
            Vector3 toTarget = target.position - transform.position;
            Vector3 toFlat = new Vector3(toTarget.x, 0f, toTarget.z);
            if (toFlat.sqrMagnitude > 0.0001f)
            {
                Quaternion want = Quaternion.LookRotation(toFlat.normalized, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, want, turnSpeedDeg * Time.fixedDeltaTime);
            }
        }

        if (_rb != null) _rb.velocity = transform.forward * speed;
        else transform.position += transform.forward * speed * Time.deltaTime;
    }

    void OnTriggerEnter(Collider other)
    {
        if (other == null) return;
        if (owner != null && (other.gameObject == owner || other.transform.IsChildOf(owner.transform)))
        {
            if (Time.time - _spawnTime <= graceDuration) return;
            return;
        }

        var hitRec = other.GetComponentInParent<IHitReceiver>();
        if (hitRec != null)
        {
            var hd = new HitData { attacker = owner ?? gameObject, damage = damage, hitPoint = transform.position, hitDirection = transform.forward };
            hitRec.ReceiveHit(hd);
        }
        else
        {
            var ih = other.GetComponentInParent<IHealth>();
            if (ih != null)
            {
                try { ih.ApplyDamage(damage); } catch { }
            }
        }

        Destroy(gameObject);
    }
}
