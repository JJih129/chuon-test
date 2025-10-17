// UltimateInputRouter.cs
// 간단한 입력 라우터: InputBlocker 확인, Guard 입력을 PlayerGuardController로 라우팅
// guardKey : 가드 입력 키를 변경 가능하게 함
using System;
using UnityEngine;

[DisallowMultipleComponent]
public class UltimateInputRouter : MonoBehaviour
{
    [Tooltip("Assign a component that implements IInputBlocker (or null).")]
    public MonoBehaviour inputBlockerBehaviour;

    [Tooltip("Assign the PlayerGuardController (used to call StartGuard/EndGuard)")]
    public PlayerGuardController guardController;

    [Tooltip("가드 입력 키 설정")]
    public KeyCode guardKey = KeyCode.E;

    IInputBlocker inputBlocker;
    bool lastGuardPressed = false;

    void Awake()
    {
        if (inputBlockerBehaviour != null && inputBlockerBehaviour is IInputBlocker)
        {
            inputBlocker = (IInputBlocker)inputBlockerBehaviour;
        }
        else
        {
            if (inputBlockerBehaviour != null)
                Debug.LogWarning($"UltimateInputRouter: inputBlockerBehaviour isn't an IInputBlocker.");
            inputBlocker = null;
        }

        if (guardController == null)
        {
            guardController = GetComponent<PlayerGuardController>() ?? FindObjectOfType<PlayerGuardController>();
            if (guardController == null) Debug.LogWarning("UltimateInputRouter: guardController not assigned/found.");
        }
    }

    void Update()
    {
        if (inputBlocker != null && inputBlocker.IsBlocked) return;

        bool pressed = Input.GetKey(guardKey);

        // 상태 변화 기반으로 Start/End 호출 (debounced)
        if (pressed && !lastGuardPressed)
        {
            if (guardController != null) guardController.StartGuard();
        }
        else if (!pressed && lastGuardPressed)
        {
            if (guardController != null) guardController.EndGuard();
        }

        lastGuardPressed = pressed;
    }
}
