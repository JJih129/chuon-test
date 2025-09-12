using UnityEngine;

// 모든 피격 가능한 오브젝트에 붙일 인터페이스(공통 규약)
public interface IHitReceiver
{
    void ReceiveHit(HitData hit); // 공격 정보 받을 때 호출됨
}
