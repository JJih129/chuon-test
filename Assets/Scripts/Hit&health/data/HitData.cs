using UnityEngine;

// 공격/피격 정보(데미지, 방향 등)를 한 번에 넘겨주는 데이터 클래스
public class HitData
{
    public int damage;
    public Vector3 hitPoint;
    public Vector3 hitDirection;
    public GameObject attacker;

    // (추후 넉백, 크리티컬, 이펙트 등 확장 가능)
}
