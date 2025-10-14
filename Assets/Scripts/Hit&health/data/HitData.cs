using UnityEngine;

public class HitData
{
    public int damage;
    public Vector3 hitPoint;
    public Vector3 hitDirection;
    public GameObject attacker;
    public bool isParryable = false;
    public HitType hitType = HitType.Normal;

    // 편의 접근자
    public Transform attackerTransform => attacker != null ? attacker.transform : null;
    public string attackerName => attacker != null ? attacker.name : "null";
}
