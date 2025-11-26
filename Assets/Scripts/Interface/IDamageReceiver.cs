// Assets/Scripts/Combat/IDamageReceiver.cs
using UnityEngine;

/// <summary>
/// 히트 정보 공통 구조체
/// - 액션 게임에서 '때리는 쪽'이 '맞는 쪽'에게 전달하는 최소 공통 데이터
/// </summary>
public struct HitPayload
{
    public float damage;          // 데미지 량
    public HitType hitType;       // 이미 프로젝트에 있는 HitType enum 사용
    public Vector3 hitPoint;      // 맞은 지점 (월드 좌표)
    public Vector3 hitDirection;  // 공격 방향(공격자 → 피격자)
    public Transform attacker;    // 공격자 루트 Transform
    public bool canPerfectDodge;  // 퍼펙트 회피 판정 허용 여부
}

/// <summary>
/// 맞는 쪽이 구현하는 공통 인터페이스
/// (플레이어, 보스, 일반 몬스터, 오브젝트 등)
/// </summary>
public interface IDamageReceiver
{
    /// <summary>
    /// 히트박스/투사체 등에서 호출하는 단일 엔트리 포인트
    /// </summary>
    void ReceiveHit(HitPayload payload);
}