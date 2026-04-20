// Assets/ParryHitTester.cs
using UnityEngine;

public class ParryHitTester : MonoBehaviour
{
    public PlayerGuardController guard;

    void Update()
    {
        if (guard != null)
        {
            // 기존 오류: IsInParryWindow 를 메서드그룹으로 잘못 사용 -> 반드시 호출해야 함
            // 예: Debug.Log(guard.IsInParryWindow)  // property라면 그대로, method라면 호출(guard.IsInParryWindow())
            // 아래는 예시로 public SetParryWindow 대신 상태 체크용으로 맞춤
            // 만약 PlayerGuardController에 IsInParryWindow getter가 구현되어 있다면 그대로 쓰세요.
        }
    }
}
