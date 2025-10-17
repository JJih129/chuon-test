using UnityEngine;

/// <summary>
/// 히트 이벤트 데이터
/// 보스/적 스킬이 히트 판정 지점에서 이 클래스 인스턴스를 생성해 PlayerDamageReceiver.ReceiveHit(...)로 전달한다.
/// </summary>
public class HitData
{
    // ===== 기본 데미지 정보 =====
    /// <summary>공격 원래 데미지 (정수로 표현)</summary>
    public int damage;

    /// <summary>충돌 지점(월드 좌표)</summary>
    public Vector3 hitPoint;

    /// <summary>공격 진행 방향 (attacker -> target 방향)</summary>
    public Vector3 hitDirection;

    /// <summary>공격자 게임오브젝트 (null 가능)</summary>
    public GameObject attacker;

    /// <summary>공격 타입 (Normal, Projectile 등 확장 가능)</summary>
    public HitType hitType = HitType.Normal;

    // ===== 패링 관련 메타데이터 =====
    /// <summary>이 히트가 패리(Parry)로 판정될 수 있는지 여부.
    /// true면 플레이어 패리 윈도우 동안 패리 판정 적용 가능.</summary>
    public bool isParryable = false;

    /// <summary>패리 성공 시 적용할 데미지 배율.
    /// 0 => 완전 무효(데미지 없음). 0.5 => 데미지 절반 적용. 기본 0 (완전 무효) 권장.</summary>
    public float parryDamageMultiplier = 0f;

    /// <summary>패리 성공 시 공격자에게 적용할 추가 동작</summary>
    public ParryBehavior onParry = ParryBehavior.None;

    // ===== 편의 접근자 =====
    /// <summary>공격자 Transform (null-safe)</summary>
    public Transform attackerTransform => attacker != null ? attacker.transform : null;

    /// <summary>공격자 이름 (디버그용)</summary>
    public string attackerName => attacker != null ? attacker.name : "null";
}

/// <summary>
/// 패리 성공 시 공격자에게 적용할 동작 옵션
/// 필요하면 더 추가해서 사용하면 된다.
/// </summary>
public enum ParryBehavior
{
    None,       // 아무 행동 없음 (기본)
    Reflect,    // 탄환 반사 / 공격을 되돌려 보냄
    Stagger,    // 공격자 기절(스턴)
    Knockback   // 공격자 넉백
}
