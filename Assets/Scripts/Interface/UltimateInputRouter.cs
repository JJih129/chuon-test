using System;
using UnityEngine;

// 간단한 입력 라우터: InputBlocker 확인, Guard 입력을 PlayerGuardController로 라우팅
[DisallowMultipleComponent]
public class UltimateInputRouter : MonoBehaviour
{
    [Tooltip("Assign a component that implements IInputBlocker (or null).")]
    public MonoBehaviour inputBlockerBehaviour;

    [Tooltip("Assign the PlayerGuardController (used to call StartGuard/EndGuard)")]
    public PlayerGuardController guardController;

    IInputBlocker inputBlocker;

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
            // try to find on same GameObject
            guardController = GetComponent<PlayerGuardController>() ?? FindObjectOfType<PlayerGuardController>();
            if (guardController == null) Debug.LogWarning("UltimateInputRouter: guardController not assigned/found.");
        }
    }

    void Update()
    {
        // block inputs if inputBlocker says so
        if (inputBlocker != null && inputBlocker.IsBlocked) return;

        // Example guard input handling using E key (change to your input system)
        if (Input.GetKeyDown(KeyCode.E))
        {
            if (guardController != null)
            {
                guardController.StartGuard();
            }
            else Debug.LogWarning("UltimateInputRouter: StartGuard called but guardController == null");
        }

        if (Input.GetKeyUp(KeyCode.E))
        {
            if (guardController != null)
            {
                guardController.EndGuard();
            }
        }
    }
}
