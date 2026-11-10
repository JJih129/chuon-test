// 파일명: KioskInputActivity.cs
// 키오스크용 "사람이 있다" 판정. Input System(키보드·마우스·모든 게임패드) + 레거시 Input.anyKey 보조.
// 스틱 미세 드리프트는 무시한다 (스틱 방향 버튼의 기본 눌림점 0.5 이상만 입력으로 본다).
using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

public static class KioskInputActivity
{
    const float MouseMoveThresholdSqr = 9f;       // 프레임당 3 px 이상
    const float MouseWakeThresholdSqr = 400f;     // 어트랙트 해제용: 20 px 이상 (책상 진동 무시)

    /// <summary>이번 프레임에 눌려 있거나 움직인 입력이 있는지.</summary>
    public static bool AnyActivity(out bool mouseMoved)
    {
        mouseMoved = false;
        bool any = false;

        try
        {
            Keyboard kb = Keyboard.current;
            if (kb != null && kb.anyKey.isPressed)
                any = true;

            Mouse mouse = Mouse.current;
            if (mouse != null)
            {
                if (mouse.delta.ReadValue().sqrMagnitude > MouseMoveThresholdSqr)
                {
                    mouseMoved = true;
                    any = true;
                }
                if (mouse.leftButton.isPressed || mouse.rightButton.isPressed || mouse.middleButton.isPressed)
                    any = true;
                if (mouse.scroll.ReadValue().sqrMagnitude > 0.01f)
                    any = true;
            }

            if (!any && AnyGamepadButton(pressedThisFrame: false))
                any = true;
        }
        catch (Exception)
        {
            // 장치 분리 순간 등: 입력 없음으로 처리
        }

        if (!any)
            any = LegacyAnyKey();

        return any;
    }

    /// <summary>이번 프레임에 새로 누른 입력(또는 큰 마우스 이동)이 있는지. 어트랙트/엔드 카드 해제용.</summary>
    public static bool AnyPressedThisFrame(bool includeMouseMove)
    {
        try
        {
            Keyboard kb = Keyboard.current;
            if (kb != null && kb.anyKey.wasPressedThisFrame)
                return true;

            Mouse mouse = Mouse.current;
            if (mouse != null)
            {
                if (mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame || mouse.middleButton.wasPressedThisFrame)
                    return true;
                if (includeMouseMove && mouse.delta.ReadValue().sqrMagnitude > MouseWakeThresholdSqr)
                    return true;
            }

            if (AnyGamepadButton(pressedThisFrame: true))
                return true;
        }
        catch (Exception)
        {
        }

        return false;
    }

    /// <summary>아무 키·버튼도 눌려 있지 않은지 (UI 입력 재개 전 확인).</summary>
    public static bool AllReleased()
    {
        try
        {
            Keyboard kb = Keyboard.current;
            if (kb != null && kb.anyKey.isPressed)
                return false;
            Mouse mouse = Mouse.current;
            if (mouse != null && (mouse.leftButton.isPressed || mouse.rightButton.isPressed || mouse.middleButton.isPressed))
                return false;
            if (AnyGamepadButton(pressedThisFrame: false))
                return false;
        }
        catch (Exception)
        {
        }

        return true;
    }

    static bool AnyGamepadButton(bool pressedThisFrame)
    {
        var pads = Gamepad.all;
        for (int p = 0; p < pads.Count; p++)
        {
            Gamepad pad = pads[p];
            if (pad == null || !pad.added)
                continue;

            var controls = pad.allControls;
            for (int i = 0; i < controls.Count; i++)
            {
                if (controls[i] is ButtonControl button && (pressedThisFrame ? button.wasPressedThisFrame : button.isPressed))
                    return true;
            }
        }

        return false;
    }

    static bool LegacyAnyKey()
    {
        try
        {
            return Input.anyKey;
        }
        catch (InvalidOperationException)
        {
            return false; // 레거시 입력 비활성 프로젝트 설정
        }
    }

    // ------------------------------------------------------------------
    // 스태프 콤보: 키보드 Ctrl+Shift+K / 패드 LB+RB+View(Select) 를 길게
    // (F1·F12 는 개발 빌드에서 씬 이동·디버그 HUD 단축키라 쓰지 않는다)
    // ------------------------------------------------------------------

    public static bool StaffComboHeld()
    {
        try
        {
            Keyboard kb = Keyboard.current;
            if (kb != null
                && (kb.leftCtrlKey.isPressed || kb.rightCtrlKey.isPressed)
                && (kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed)
                && kb.kKey.isPressed)
                return true;

            var pads = Gamepad.all;
            for (int p = 0; p < pads.Count; p++)
            {
                Gamepad pad = pads[p];
                if (pad != null && pad.leftShoulder.isPressed && pad.rightShoulder.isPressed && pad.selectButton.isPressed)
                    return true;
            }
        }
        catch (Exception)
        {
        }

        return false;
    }
}
