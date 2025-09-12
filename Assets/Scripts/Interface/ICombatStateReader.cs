// 파일명: ICombatStateReader.cs
// 역할: 전투/이동의 현재 상태를 조회(발동 가능 조건 판단용)

public interface ICombatStateReader
{
    // ===== 변수 헤더(설명) =====
    // 공중 상태 여부(점프/낙하)
    bool IsInAir();
    // 경직/히트 상태 여부(피격 중 등)
    bool IsStaggered();
    // 공격 중 여부(콤보 실행 중 등)
    bool IsAttacking();
}