// [헤더] 상호작용 대상 인터페이스
public interface IInteractable
{
    string GetPromptText();   // [헤더] 화면에 띄울 문구 반환
    void OnHoverStart();      // [헤더] 커서/레이가 닿기 시작
    void OnHoverEnd();        // [헤더] 커서/레이가 벗어남
    bool TryInteract(object invoker = null); // [헤더] F 입력 등 실제 상호작용
}
