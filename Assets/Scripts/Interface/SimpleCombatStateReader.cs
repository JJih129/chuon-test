// 파일명: SimpleCombatStateReader.cs
// 역할: 임시 스위치로 상태를 마킹하는 가장 단순한 리더
// Animator/StateMachine/전투컨트롤러가 있다면 그 값으로 반환하도록 교체 권장

using UnityEngine;

public class SimpleCombatStateReader : MonoBehaviour, ICombatStateReader
{
    // ===== 변수 헤더(한글 설명) =====
    [Header("상태 플래그(테스트용)")]
    public bool inAir = false;
    public bool staggered = false;
    public bool attacking = false;

    public bool IsInAir() => inAir;
    public bool IsStaggered() => staggered;
    public bool IsAttacking() => attacking;
}