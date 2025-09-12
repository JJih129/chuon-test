// Assets/Scripts/Hit&health/IHealth.cs
public interface IHealth
{
    // ▶ 최대/현재 체력
    int MaxHP { get; }
    int CurrentHP { get; }

    // ▶ 사망 여부
    bool IsDead { get; }

    // ▶ 이벤트 (UI/바인더 호환)
    //  - OnHPChanged(cur, max) : 대부분의 HP UI가 구독
    //  - OnHealthChanged(cur, max) : 과거 호환용(남겨둠)
    //  - OnDamaged(amount) : 피해 연출/플래시 등
    //  - OnDied() : 사망 UI/처리
    event System.Action<int, int> OnHPChanged;
    event System.Action<int, int> OnHealthChanged;
    event System.Action<int> OnDamaged;
    event System.Action OnDied;

    // ▶ 회복
    void Heal(int amount);
}