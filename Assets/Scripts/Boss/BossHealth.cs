using UnityEngine;

// BossHealth.cs
// IHealth를 올바르게 구현한 보스 체력 컴포넌트

public class BossHealth : MonoBehaviour, IHealth
{
    [Header("▶ 체력 설정")]
    [Tooltip("보스 최대 체력")]
    [SerializeField] int maxHP = 100;              // 인스펙터 노출용 내부 필드
    [Tooltip("시작 시 현재 체력 (0이면 MaxHP로 세팅됨)")]
    [SerializeField] int startHP = 0;

    [Header("▶ 디버그/옵션")]
    [Tooltip("죽었을 때 오브젝트 비활성화할지 여부")]
    public bool disableOnDeath = false;

    // 인터페이스 프로퍼티 구현 (MaxHP는 읽기 전용 프로퍼티)
    public int MaxHP => maxHP;
    public int CurrentHP { get; private set; }
    public bool IsDead { get; private set; }

    // IHealth 이벤트들
    public event System.Action<int, int> OnHPChanged;
    public event System.Action<int, int> OnHealthChanged;
    public event System.Action<int> OnDamaged;
    public event System.Action OnDied;

    void Awake()
    {
        CurrentHP = (startHP <= 0) ? MaxHP : Mathf.Clamp(startHP, 0, MaxHP);
        IsDead = CurrentHP <= 0;
    }

    // 회복
    public void Heal(int amount)
    {
        if (IsDead || amount <= 0) return;
        CurrentHP = Mathf.Clamp(CurrentHP + amount, 0, MaxHP);
        OnDamaged?.Invoke(0); // 필요 없다면 제거
        OnHPChanged?.Invoke(CurrentHP, MaxHP);
        OnHealthChanged?.Invoke(CurrentHP, MaxHP);
    }

    // 데미지 적용
    public void ApplyDamage(int amount)
    {
        if (IsDead || amount <= 0) return;
        CurrentHP = Mathf.Clamp(CurrentHP - amount, 0, MaxHP);
        OnDamaged?.Invoke(amount);
        OnHPChanged?.Invoke(CurrentHP, MaxHP);
        OnHealthChanged?.Invoke(CurrentHP, MaxHP);

        if (CurrentHP <= 0 && !IsDead)
        {
            IsDead = true;
            OnDied?.Invoke();
            if (disableOnDeath) gameObject.SetActive(false);
        }
    }
}
