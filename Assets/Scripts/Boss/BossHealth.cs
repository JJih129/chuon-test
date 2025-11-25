// Assets/Scripts/Boss/BossHealth.cs
using System;
using UnityEngine;

/// <summary>
/// 보스 체력 컴포넌트.
/// IHealth 인터페이스(플레이어용과 동일 시그니처)를 구현해서
/// BulletProjectile, UI 등에서 통일된 방식으로 다루도록 한다.
/// </summary>
public class BossHealth : MonoBehaviour, IHealth
{
    [Header("▶ 체력 설정")]
    [Tooltip("보스 최대 체력")]
    [SerializeField] private int maxHP = 100;

    [Tooltip("시작 체력 (0 이하이면 MaxHP로 시작)")]
    [SerializeField] private int startHP = 0;

    [Header("▶ 무적/경직 옵션")]
    [Tooltip("시작 시 무적 여부")]
    [SerializeField] private bool invincibleOnStart = false;

    [Tooltip("피격 시 자동 무적 시간(초). 0이면 자동 무적 없음")]
    [SerializeField] private float hitInvincibleDuration = 0f;

    [Tooltip("시작 시 경직 상태 여부(패턴에 따라 외부에서 변경 가능)")]
    [SerializeField] private bool startStaggered = false;

    [Header("▶ 디버그/동작 옵션")]
    [Tooltip("사망 시 게임오브젝트 비활성화 여부")]
    [SerializeField] private bool disableOnDeath = false;

    [Tooltip("디버그 로그 출력 여부")]
    [SerializeField] private bool debugLog = false;

    // ─────────────────────────────────────────────────────────────
    // IHealth 인터페이스 구현 프로퍼티
    // ─────────────────────────────────────────────────────────────

    public int MaxHP => maxHP;

    public int CurrentHP { get; private set; }

    public bool IsDead { get; private set; }

    // 인터페이스는 get; 만 요구하지만 private set;을 추가해서 내부에서만 변경
    public bool IsStaggered { get; private set; }

    // 인빈시블 프로퍼티 (인터페이스 시그니처와 동일)
    public bool isInvincible { get; set; }

    // ─────────────────────────────────────────────────────────────
    // IHealth 인터페이스 이벤트 구현
    // ─────────────────────────────────────────────────────────────

    /// <summary> 체력 변경 이벤트 (현재, 최대) </summary>
    public event Action<int, int> OnHPChanged;

    /// <summary> 이름만 다른 동일 의미 이벤트(기존 코드 호환용) </summary>
    public event Action<int, int> OnHealthChanged;

    /// <summary> 단순 데미지 이벤트(양수 데미지량) </summary>
    public event Action<int> OnDamaged;

    /// <summary> 데미지 + 히트 타입 이벤트 </summary>
    public event Action<int, HitType> OnDamagedWithType;

    /// <summary> 사망 이벤트 </summary>
    public event Action OnDied;

    // ─────────────────────────────────────────────────────────────
    // 내부 상태
    // ─────────────────────────────────────────────────────────────

    private float _invincibleRemain;   // 남은 무적 시간(초)

    // ─────────────────────────────────────────────────────────────
    // 라이프 사이클
    // ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        // 시작 체력 결정
        CurrentHP = (startHP <= 0) ? MaxHP : Mathf.Clamp(startHP, 0, MaxHP);
        IsDead = CurrentHP <= 0;
        isInvincible = invincibleOnStart;
        IsStaggered = startStaggered;
        _invincibleRemain = 0f;

        // 초기 체력 브로드캐스트 (UI 초기화용)
        OnHPChanged?.Invoke(CurrentHP, MaxHP);
        OnHealthChanged?.Invoke(CurrentHP, MaxHP);

        if (debugLog)
        {
            Debug.Log(
                $"[BossHealth] Init HP={CurrentHP}/{MaxHP}, " +
                $"invincible={isInvincible}, staggered={IsStaggered}",
                this);
        }
    }

    private void Update()
    {
        // 무적 타이머 처리
        if (_invincibleRemain > 0f)
        {
            _invincibleRemain -= Time.deltaTime;
            if (_invincibleRemain <= 0f)
            {
                _invincibleRemain = 0f;
                isInvincible = false;
                if (debugLog) Debug.Log("[BossHealth] Invincible OFF (timer end)", this);
            }
        }
    }

    // ─────────────────────────────────────────────────────────────
    // IHealth API 구현
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 체력 회복 (양수만 허용)
    /// </summary>
    public void Heal(int amount)
    {
        if (IsDead || amount <= 0) return;

        int prev = CurrentHP;
        CurrentHP = Mathf.Clamp(CurrentHP + amount, 0, MaxHP);
        if (CurrentHP == prev) return;

        // HP 변경 브로드캐스트
        OnHPChanged?.Invoke(CurrentHP, MaxHP);
        OnHealthChanged?.Invoke(CurrentHP, MaxHP);

        if (debugLog)
        {
            Debug.Log($"[BossHealth] Heal +{amount} → {CurrentHP}/{MaxHP}", this);
        }
    }

    /// <summary>
    /// float 데미지 버전 (인터페이스 시그니처 맞추기용).
    /// 내부는 int 단위로 처리.
    /// </summary>
    public void ApplyDamage(float damage)
    {
        int dmgInt = Mathf.RoundToInt(damage);
        ApplyDamage(dmgInt);
    }

    /// <summary>
    /// 기본 int 데미지 적용 (HitType 모르면 Normal로 처리).
    /// BulletProjectile 등에서 사용.
    /// </summary>
    public void ApplyDamage(int damage)
    {
        TakeDamage(damage, HitType.Normal, transform.position);
    }

    /// <summary>
    /// 타입/히트 위치 포함 상세 데미지 적용 (IHealth 요구 시그니처).
    /// </summary>
    public void TakeDamage(int amount, HitType hitType, Vector3 hitPoint)
    {
        if (IsDead || amount <= 0) return;

        if (isInvincible)
        {
            if (debugLog)
                Debug.Log("[BossHealth] Damage ignored (Invincible)", this);
            return;
        }

        int prevHP = CurrentHP;
        CurrentHP = Mathf.Clamp(CurrentHP - amount, 0, MaxHP);

        // 이벤트 브로드캐스트
        OnDamaged?.Invoke(amount);
        OnHPChanged?.Invoke(CurrentHP, MaxHP);
        OnHealthChanged?.Invoke(CurrentHP, MaxHP);
        OnDamagedWithType?.Invoke(amount, hitType);

        if (debugLog)
        {
            Debug.Log(
                $"[BossHealth] Damage {amount} ({hitType}) → {CurrentHP}/{MaxHP}",
                this);
        }

        // 사망 처리
        if (CurrentHP <= 0 && !IsDead)
        {
            Die();
        }
        // 아직 살아 있으면 히트 후 무적 처리
        else if (hitInvincibleDuration > 0f)
        {
            SetInvincible(hitInvincibleDuration);
        }
    }

    /// <summary>
    /// 지정 시간 동안 무적 세팅 (0 이하면 즉시 해제).
    /// </summary>
    public void SetInvincible(float seconds)
    {
        if (seconds <= 0f)
        {
            isInvincible = false;
            _invincibleRemain = 0f;

            if (debugLog)
                Debug.Log("[BossHealth] Invincible OFF (SetInvincible <= 0)", this);
        }
        else
        {
            isInvincible = true;
            _invincibleRemain = seconds;

            if (debugLog)
                Debug.Log($"[BossHealth] Invincible ON for {seconds:0.00}s", this);
        }
    }

    // ─────────────────────────────────────────────────────────────
    // 보스 전용 유틸
    // ─────────────────────────────────────────────────────────────

    private void Die()
    {
        if (IsDead) return;

        IsDead = true;
        if (debugLog) Debug.Log("[BossHealth] DEAD", this);

        OnDied?.Invoke();

        if (disableOnDeath)
        {
            gameObject.SetActive(false);
        }
    }

    /// <summary>
    /// 외부에서 경직 상태 세팅(브레이크 게이지/패턴 등에서 호출).
    /// </summary>
    public void SetStagger(bool value)
    {
        IsStaggered = value;

        if (debugLog)
            Debug.Log($"[BossHealth] IsStaggered = {value}", this);
    }
}
