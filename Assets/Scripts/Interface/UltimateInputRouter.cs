// 파일명: UltimateInputRouter.cs
// 역할: R키를 PlayerUltimateController.TryActivate()로 연결 (임시/프로토)
using UnityEngine;

[DisallowMultipleComponent]
public class UltimateInputRouter : MonoBehaviour
{
    [Header("입력 키 | 궁극기 발동 키")]
    public KeyCode ultimateKey = KeyCode.R;

    PlayerUltimateController _ult;

    void Awake() => _ult = GetComponent<PlayerUltimateController>();

    void Update()
    {
        if (_ult == null) return;
        // 입력이 전역 차단되었으면 무시(선택)
        var blk = GetComponent<IInputBlocker>();
        if (blk != null && blk.IsBlocked()) return;

        if (Input.GetKeyDown(ultimateKey))
            _ult.TryActivate();
    }
}