using UnityEngine;
using UnityEngine.Events;

[DisallowMultipleComponent]
public class UltimateTargetSimple : MonoBehaviour, IUltimateTarget
{
    [Header("Health")]
    public int maxHP = 10000;
    public int currentHP = 10000;
    public bool destroyOnDeath;

    [Header("Defense")]
    public int defense = 200;
    public bool ignoreDefense = true;
    public bool isBreak;
    [Min(1f)] public float breakBonusMultiplier = 1.5f;

    [Header("Reaction")]
    public Rigidbody rigidbodyForReaction;
    public float knockbackForce = 3f;
    public float hitStop = 0.03f;

    [Header("Feedback")]
    public GameObject hitVFXPrefab;
    public Transform vfxSpawnPoint;
    public AudioClip hitSFX;
    public AudioSource audioSource;

    [Header("Events")]
    public UnityEvent<int> OnUltimateDamageApplied;
    public UnityEvent OnDied;

    bool _isDead;
    float _hitStopRemain;

    void Reset()
    {
        currentHP = maxHP;
        if (!audioSource)
            audioSource = GetComponent<AudioSource>();
    }

    void Awake()
    {
        if (!audioSource)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 1f;
        }

        if (!vfxSpawnPoint)
            vfxSpawnPoint = transform;

        currentHP = Mathf.Clamp(currentHP, 0, maxHP);
        enabled = false;
    }

    void Update()
    {
        if (_hitStopRemain <= 0f)
        {
            enabled = false;
            return;
        }

        _hitStopRemain -= Time.unscaledDeltaTime;
    }

    public void ApplyUltimateDamage(int fixedDamage)
    {
        if (_isDead)
            return;

        int damage = fixedDamage;
        if (!ignoreDefense)
            damage = Mathf.Max(1, fixedDamage - Mathf.Max(0, defense));
        if (isBreak && breakBonusMultiplier > 1f)
            damage = Mathf.RoundToInt(damage * breakBonusMultiplier);

        OnUltimateDamageApplied?.Invoke(damage);
        ApplyLocalDamage(damage);
        PlayHitEffects();
        ApplySimpleReaction();

        if (hitStop > 0f)
        {
            _hitStopRemain = hitStop;
            enabled = true;
        }
    }

    void ApplyLocalDamage(int damage)
    {
        if (damage <= 0)
            return;

        currentHP -= damage;
        if (currentHP > 0 || _isDead)
            return;

        _isDead = true;
        currentHP = 0;
        OnDied?.Invoke();
        if (destroyOnDeath)
            Destroy(gameObject);
    }

    void PlayHitEffects()
    {
        if (hitVFXPrefab)
        {
            Vector3 pos = vfxSpawnPoint ? vfxSpawnPoint.position : transform.position;
            Instantiate(hitVFXPrefab, pos, Quaternion.identity);
        }

        if (hitSFX && audioSource)
            audioSource.PlayOneShot(hitSFX, AudioOptionsRuntime.ScaleSfx(1f));
    }

    void ApplySimpleReaction()
    {
        if (!rigidbodyForReaction || knockbackForce <= 0f)
            return;

        Vector3 dir = -transform.forward;
        dir.y = 0f;
        dir.Normalize();
        rigidbodyForReaction.AddForce(dir * knockbackForce, ForceMode.VelocityChange);
    }

    [ContextMenu("Debug Break On")]
    public void Debug_BreakOn() => isBreak = true;

    [ContextMenu("Debug Break Off")]
    public void Debug_BreakOff() => isBreak = false;

    [ContextMenu("Debug Full Heal")]
    public void Debug_FullHeal() => currentHP = maxHP;

    public void SetBreak(bool on) => isBreak = on;
}
