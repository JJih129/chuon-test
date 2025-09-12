// 파일명: IUltimateTarget.cs
// 역할: 궁극기 전용 데미지 입력 통합 인터페이스(플레이어 → 타겟)

public interface IUltimateTarget
{
    // ===== 변수 헤더(설명) =====
    // 궁극기 전용 '고정 데미지'를 입력받아
    // 내부의 방어/브레이크(무력화) 보정 등을 적용해 처리한다.
    void ApplyUltimateDamage(int fixedDamage);
}