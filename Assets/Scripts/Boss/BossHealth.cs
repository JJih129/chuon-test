using System;
using UnityEngine;

public class BossHealth : MonoBehaviour, IHealth
{
    public int maxHP = 300;
    public int currentHP = 300;
    public GameObject hitEffectPrefab;

    public bool isInvincible { get; set; } = false;
    public bool IsStaggered { get; private set; } = false;

    public event Action<int, int> OnHPChanged;
    public event Action<int, int> OnHealthChanged;
    public event Action<int> OnDamaged;
    public event Action<int, HitType> OnDamagedWithType;
    public event Action OnDied;

    public int CurrentHP => currentHP;
    public int MaxHP => maxHP;
    public bool IsDead => currentHP <= 0;

    public void ApplyDamage(float damage) => ApplyDamage(Mathf.RoundToInt(damage));
    public void ApplyDamage(int damage) => TakeDamage(damage, HitType.Heavy, transform.position);

    public void TakeDamage(int amount, HitType hitType, Vector3 hitPoint)
    {
        if (isInvincible) return;

        currentHP -= amount;
        if (currentHP < 0) currentHP = 0;

        if (hitEffectPrefab != null) Instantiate(hitEffectPrefab, hitPoint, Quaternion.identity);

        OnHPChanged?.Invoke(currentHP, maxHP);
        OnHealthChanged?.Invoke(currentHP, maxHP);
        OnDamaged?.Invoke(amount);
        OnDamagedWithType?.Invoke(amount, hitType);

        if (currentHP <= 0) OnDied?.Invoke();
    }

    public void Heal(int amount)
    {
        currentHP = Mathf.Clamp(currentHP + amount, 0, maxHP);
        OnHPChanged?.Invoke(currentHP, maxHP);
        OnHealthChanged?.Invoke(currentHP, maxHP);
    }

    public void SetInvincible(float seconds) { isInvincible = true; /* 간단 처리 */ }
}
