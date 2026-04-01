// Assets/Scripts/Combat/IDamageReceiver.cs
using UnityEngine;

/// <summary>
/// 히트 전달에 사용하는 최소 공통 payload.
/// 다음 단계에서 보스 패턴별 방어 규칙을 여기서 읽는다.
/// </summary>
public struct HitPayload
{
    public float damage;
    public HitType hitType;
    public Vector3 hitPoint;
    public Vector3 hitDirection;
    public Transform attacker;
    public bool canPerfectDodge;
    public bool canParry;
    public bool canGuard;
    public bool isUnblockable;
    public bool causesGuardBreak;
}

/// <summary>
/// 히트박스/발사체가 공통으로 때리는 대상 인터페이스.
/// </summary>
public interface IDamageReceiver
{
    void ReceiveHit(HitPayload payload);
}
