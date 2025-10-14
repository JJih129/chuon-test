using System;
using UnityEngine;

public interface IHealth
{
    // 상태
    int CurrentHP { get; }
    int MaxHP { get; }
    bool IsDead { get; }
    bool IsStaggered { get; }

    // 인빈시블
    bool isInvincible { get; set; }
    void SetInvincible(float seconds);

    // 데미지 적용 오버로드(프로젝트 내 여러 호출을 수용)
    void ApplyDamage(float damage);
    void ApplyDamage(int damage);

    // 상세형 API (기존 코드에서 사용하던 시그니처)
    void TakeDamage(int amount, HitType hitType, Vector3 hitPoint);

    // 회복
    void Heal(int amount);

    // 이벤트: (cur, max) 형태로 통일 — 기존 코드 호환을 위해 이름 2개 유지
    event Action<int, int> OnHPChanged;
    event Action<int, int> OnHealthChanged;

    // 단발 데미지 이벤트들 (일부 스크립트가 구독)
    event Action<int> OnDamaged;
    event Action<int, HitType> OnDamagedWithType;

    // 사망
    event Action OnDied;
}
