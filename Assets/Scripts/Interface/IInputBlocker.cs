// IInputBlocker.cs
// 단순 전역/로컬 입력 차단 인터페이스 (프로젝트 전반에서 이 시그니처 사용)
public interface IInputBlocker
{
    // 전체 입력 차단 플래그(읽기전용)
    bool IsBlocked { get; }

    // 로컬 차단 (예: 특정 행동 동안만)
    void SetBlocked(bool blocked);

    // 전체 차단 카운터용(중첩 호출 지원)
    void BlockAll();
    void BlockAll(bool block);
}
