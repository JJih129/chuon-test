// 파일명: SimpleInputBlocker.cs
// 역할: 가장 단순한 형태의 입력 차단기(프로토타입용)
// 실제 Input System/커맨드 버퍼가 있다면 그쪽과 연동하도록 교체 권장

using UnityEngine;

public class SimpleInputBlocker : MonoBehaviour, IInputBlocker
{
    // ===== 변수 헤더(한글 설명) =====
    [Header("입력 차단 상태 | true면 모든 입력 무시")]
    [SerializeField] bool blocked;

    public void BlockAll(bool on) => blocked = on;
    public bool IsBlocked() => blocked;
}