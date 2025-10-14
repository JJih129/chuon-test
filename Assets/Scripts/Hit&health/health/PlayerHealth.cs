using System;
using UnityEngine;

[DisallowMultipleComponent]
public class PlayerHealth : MonoBehaviour, IHealth
{
    [Header("Player HP")]
    public int maxHP = 100;
    public int currentHP = 100;

    [Header("Invincible")]
    [SerializeField] bool _isInvincible = false;
    public bool isInvincible
    {
        get => _isInvincible;
        set
        {
            _isInvincible = value;
            if (!_isInvincible) invincibleTimer = 0f;
        }
    }
    float invincibleTimer = 0f;

    public float invincibleDuration = 1.0f;

    [Header("Effects")]
    public GameObject hitEffectPrefab;
    public CameraShake cameraShake;

    // EVENTS
    public event Action<int, int> OnHPChanged;
    public event Action<int, int> OnHealthChanged;
    public event Action<int> OnDamaged;
    public event Action<int, HitType> OnDamagedWithType;
    public event Action OnDied;

    // PROPS
    public int CurrentHP => currentHP;
    public int MaxHP => maxHP;
    public bool IsDead => currentHP <= 0;
    public bool IsStaggered { get; private set; } = false;

    void Awake()
    {
        currentHP = Mathf.Clamp(currentHP, 0, maxHP);
    }

    void Update()
    {
        if (isInvincible)
        {
            invincibleTimer -= Time.deltaTime;
            if (invincibleTimer <= 0f) isInvincible = false;
        }
    }

    // overloads
    public void ApplyDamage(float damage) => ApplyDamage(Mathf.RoundToInt(damage));
    public void ApplyDamage(int damage) => TakeDamage(damage, HitType.Normal, transform.position);

    public void TakeDamage(int amount, HitType hitType, Vector3 hitPoint)
    {
        if (isInvincible) return;

        currentHP -= amount;
        if (currentHP < 0) currentHP = 0;

        // effect
        if (hitEffectPrefab != null) Instantiate(hitEffectPrefab, hitPoint, Quaternion.identity);
        if (cameraShake != null) cameraShake.Shake(0.3f, 0.2f);

        // animator triggers (존재하는 파라미터만 호출)
        var anim = GetComponent<Animator>();
        if (anim != null)
        {
            if (anim.HasTrigger("Hit")) anim.SetTrigger("Hit");
            else if (anim.HasTrigger("Hurt")) anim.SetTrigger("Hurt");
        }

        // events
        OnHPChanged?.Invoke(currentHP, maxHP);
        OnHealthChanged?.Invoke(currentHP, maxHP);
        OnDamaged?.Invoke(amount);
        OnDamagedWithType?.Invoke(amount, hitType);

        if (currentHP <= 0) Die();
    }

    void Die() => OnDied?.Invoke();

    public void Heal(int amount)
    {
        currentHP = Mathf.Clamp(currentHP + amount, 0, maxHP);
        OnHPChanged?.Invoke(currentHP, maxHP);
        OnHealthChanged?.Invoke(currentHP, maxHP);
    }

    public void SetInvincible(float seconds)
    {
        isInvincible = true;
        invincibleTimer = seconds;
    }
}
