// 파일명: IInputBlocker.cs
// 역할: 플레이어 입력을 전역으로 차단/해제하는 공용 인터페이스

public interface IInputBlocker
{
    // ===== 변수 헤더(설명) =====
    // true: 모든 입력 차단, false: 입력 허용
    void BlockAll(bool on);
    // 현재 입력 차단 여부 확인
    bool IsBlocked();
}