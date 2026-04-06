using System;
using UnityEngine;

public class TutorialProjectile : MonoBehaviour
{
    [Header("Projectile")]
    public float speed = 10f;
    public float damage = 10f;
    public float lifeTime = 5f;

    [Header("Defense")]
    public bool canBeParried = true;
    public bool canBePerfectDodged = true;
    public bool unblockable = false;

    [HideInInspector] public Transform owner;
    public event Action<TutorialProjectile, bool> Released;

    float _lifeRemaining;
    bool _hitPlayer;

    void OnEnable()
    {
        _lifeRemaining = lifeTime;
        _hitPlayer = false;
    }

    public void ConfigureRuntime(
        Transform runtimeOwner,
        float runtimeDamage,
        float runtimeSpeed,
        float runtimeLifeTime,
        bool allowParry,
        bool allowPerfectDodge,
        bool isUnblockable)
    {
        owner = runtimeOwner;
        damage = runtimeDamage;
        speed = runtimeSpeed;
        lifeTime = runtimeLifeTime;
        canBeParried = allowParry;
        canBePerfectDodged = allowPerfectDodge;
        unblockable = isUnblockable;
        _lifeRemaining = lifeTime;
        _hitPlayer = false;
    }

    void Update()
    {
        _lifeRemaining -= Time.deltaTime;
        if (_lifeRemaining <= 0f)
        {
            ReleaseSelf();
            return;
        }

        transform.Translate(Vector3.forward * speed * Time.deltaTime);
    }

    void OnTriggerEnter(Collider other)
    {
        if (owner != null && other.transform.IsChildOf(owner))
            return;

        if (other.CompareTag("Player"))
        {
            IDamageReceiver receiver = other.GetComponent<IDamageReceiver>();
            if (receiver != null)
            {
                _hitPlayer = true;
                HitPayload payload = new HitPayload
                {
                    damage = damage,
                    hitPoint = transform.position,
                    hitDirection = transform.forward,
                    attacker = owner,
                    hitType = HitType.Normal,
                    canParry = canBeParried,
                    canPerfectDodge = canBePerfectDodged,
                    canGuard = !unblockable,
                    unblockable = unblockable
                };

                receiver.ReceiveHit(payload);
            }

            ReleaseSelf();
        }
        else if (!other.CompareTag("Enemy") && !other.isTrigger)
        {
            ReleaseSelf();
        }
    }

    void OnDisable()
    {
        owner = null;
        Released = null;
    }

    void ReleaseSelf()
    {
        Released?.Invoke(this, _hitPlayer);
        Released = null;
        RuntimeObjectPool.Release(gameObject);
    }
}
