// Assets/Scripts/Hit&health/health/EnemyHealth.cs
using UnityEngine;

[DisallowMultipleComponent]
public class EnemyHealth : MonoBehaviour, IHealth
{
    [Header("▶ 최대 체력(HP)")]
    [SerializeField] private int maxHP = 200;

    [Header("▶ 시작 체력(0 이하이면 최대치로 시작)")]
    [SerializeField] private int startHP = 0;

    [Header("▶ 무적 여부(데미지 무시)")]
    public bool isInvincible = false;

    [Header("▶ 사망 시 제거 지연(초) (0이면 제거 안 함)")]
    [SerializeField] private float destroyAfterDeathSeconds = 2f;

    [Header("▶ 경직(Stagger) 유지 시간(초): 경미/강 피격")]
    [SerializeField] private float staggerSecondsLight = 0.2f;
    [SerializeField] private float staggerSecondsHeavy = 0.45f;

    public int MaxHP => maxHP;
    public int CurrentHP => currentHP;
    public bool IsDead => isDead;

    // === UI/바인더 이벤트 (IHealth) ===
    public event System.Action<int, int> OnHPChanged;
    public event System.Action<int, int> OnHealthChanged;
    public event System.Action<int> OnDamaged;
    public event System.Action OnDied;

    private int currentHP;
    private bool isDead;

    public bool IsStaggered { get; private set; }
    private float staggerEndRealtime = 0f;

    void Awake()
    {
        currentHP = (startHP <= 0) ? maxHP : Mathf.Clamp(startHP, 0, maxHP);
        isDead = (currentHP <= 0);
        OnHPChanged?.Invoke(currentHP, maxHP);
        OnHealthChanged?.Invoke(currentHP, maxHP);
    }

    void Update()
    {
        if (IsStaggered && Time.realtimeSinceStartup >= staggerEndRealtime)
            IsStaggered = false;
    }

    public void TakeDamage(int amount, HitType type, Vector3 hitPoint)
    {
        if (amount <= 0) return;
        if (isInvincible || isDead) return;

        OnDamaged?.Invoke(amount);

        currentHP = Mathf.Max(0, currentHP - amount);
        OnHPChanged?.Invoke(currentHP, maxHP);
        OnHealthChanged?.Invoke(currentHP, maxHP);

        float stagDur = (type == HitType.Strong || type == HitType.ParryFail) ? staggerSecondsHeavy : staggerSecondsLight;
        if (stagDur > 0f)
        {
            IsStaggered = true;
            staggerEndRealtime = Time.realtimeSinceStartup + stagDur;
        }

        if (currentHP <= 0)
            Die();
    }

    public void Heal(int amount)
    {
        if (amount <= 0 || isDead) return;
        int prev = currentHP;
        currentHP = Mathf.Clamp(currentHP + amount, 0, maxHP);
        if (currentHP != prev)
        {
            OnHPChanged?.Invoke(currentHP, maxHP);
            OnHealthChanged?.Invoke(currentHP, maxHP);
        }
    }

    public void FullHeal()
    {
        if (isDead) return;
        currentHP = maxHP;
        OnHPChanged?.Invoke(currentHP, maxHP);
        OnHealthChanged?.Invoke(currentHP, maxHP);
    }

    public void ForceKill()
    {
        if (isDead) return;
        currentHP = 0;
        OnHPChanged?.Invoke(currentHP, maxHP);
        OnHealthChanged?.Invoke(currentHP, maxHP);
        Die();
    }

    private void Die()
    {
        if (isDead) return;
        isDead = true;
        IsStaggered = false;

        OnDied?.Invoke();

        if (destroyAfterDeathSeconds > 0f)
            Destroy(gameObject, destroyAfterDeathSeconds);
    }
}
