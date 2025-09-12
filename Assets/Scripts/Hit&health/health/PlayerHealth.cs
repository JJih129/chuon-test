// Assets/Scripts/Hit&health/health/PlayerHealth.cs
using UnityEngine;

[DisallowMultipleComponent]
public class PlayerHealth : MonoBehaviour, IHealth
{
    [Header("▶ 최대 체력(HP)")]
    [SerializeField] private int maxHP = 100;

    [Header("▶ 시작 시 현재 체력(0 이하이면 최대치로 시작)")]
    [SerializeField] private int startHP = 0;

    [Header("▶ 무적 여부(피해 무시)")]
    public bool isInvincible = false;

    [Header("▶ 경미/강 피격 시 경직(Stagger) 유지 시간(초)")]
    [SerializeField] private float staggerSecondsLight = 0.25f;
    [SerializeField] private float staggerSecondsHeavy = 0.55f;

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

    private PlayerCombatController combat;

    void Awake()
    {
        combat = GetComponent<PlayerCombatController>();
        currentHP = (startHP <= 0) ? maxHP : Mathf.Clamp(startHP, 0, maxHP);
        isDead = (currentHP <= 0);
        // 초기값 브로드캐스트
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

        // 피해 이벤트(선 발행해도/후 발행해도 무방 → 여기선 선)
        OnDamaged?.Invoke(amount);

        // HP 감소
        currentHP = Mathf.Max(0, currentHP - amount);
        OnHPChanged?.Invoke(currentHP, maxHP);
        OnHealthChanged?.Invoke(currentHP, maxHP);

        // 경직 플래그
        float stagDur = (type == HitType.Strong || type == HitType.ParryFail) ? staggerSecondsHeavy : staggerSecondsLight;
        if (stagDur > 0f)
        {
            IsStaggered = true;
            staggerEndRealtime = Time.realtimeSinceStartup + stagDur;
        }

        // 사망 처리
        if (currentHP <= 0)
        {
            isDead = true;
            IsStaggered = false;
            combat?.ApplyDeath();
            OnDied?.Invoke();
            return;
        }

        // 피격 연출
        combat?.ApplyHit(type == HitType.Strong || type == HitType.ParryFail);
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
        isDead = true;
        IsStaggered = false;
        combat?.ApplyDeath();
        OnDied?.Invoke();
    }
}
