// 개발용 입력 자가 테스트 (메뉴/MCP 실행 전용 — 자동 실행 없음)
// Tools/Chuon/QA/Input Self-Test (Virtual Gamepad)   또는 MCP execute_code: return ChuonInputSelfTest.Run();
//  가상 Gamepad 를 추가해 상태 이벤트를 넣고 InputSystem.Update() 로 처리시켜 ChuonInput 동작/글리프/리바인드(충돌 교체·취소)를 검사한다.
//  - 에디트 모드: 글리프 / 오버라이드 저장·복원 / 인터랙티브 리바인드 + 충돌 교체 + 취소.
//    (InputAction 은 에디트 모드의 Editor 업데이트에서 갱신되지 않으므로 1~4 는 건너뛴다)
//  - 플레이 모드(타이틀 화면 권장 — 가상 패드 입력이 게임에도 들어간다): 전체 (버튼→동작, 트리거, 가드 유지/놓음, 스틱 데드존 포함).
//  사용자 키 설정(PlayerPrefs opt_input_overrides_v1)은 백업 후 복원, 가상 장치는 항상 제거한다.
using System;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

public static class ChuonInputSelfTest
{
    [MenuItem("Tools/Chuon/QA/Input Self-Test (Virtual Gamepad)")]
    static void RunMenu()
    {
        Debug.Log(Run());
    }

    static StringBuilder s_log;
    static int s_fail;

    public static string Run()
    {
        bool playing = EditorApplication.isPlaying;
        s_log = new StringBuilder("[ChuonInputSelfTest] mode=" + (playing ? "play" : "edit") + "\n");
        s_fail = 0;
        bool hadPrefs = PlayerPrefs.HasKey(ChuonInput.OverridesPrefKey);
        string savedPrefs = PlayerPrefs.GetString(ChuonInput.OverridesPrefKey, string.Empty);
        Gamepad pad = null;
        try
        {
            ChuonInput.EnsureInitialized();
            ChuonInput.ResetAllBindings();
            pad = InputSystem.AddDevice<Gamepad>("ChuonSelfTestPad");
            Step();

            if (!playing)
            {
                s_log.AppendLine("  skip 1~4 (에디트 모드: 액션은 플레이어 루프에서만 갱신) — 플레이 모드에서 다시 실행");
            }
            else
            {
                // 1) 버튼 → 동작 (패드 기본 배치)
                Press(pad, GamepadButton.East);
                Check("B → Dodge pressed", ChuonInput.Pressed(ChuonAction.Dodge));
                Check("B ≠ Heal", !ChuonInput.Pressed(ChuonAction.Heal));
                Release(pad);
                Press(pad, GamepadButton.RightShoulder);
                Check("RB → AttackLight", ChuonInput.Pressed(ChuonAction.AttackLight));
                Release(pad);
                Press(pad, GamepadButton.West);
                Check("X → Heal", ChuonInput.Pressed(ChuonAction.Heal));
                Release(pad);
                Press(pad, GamepadButton.South);
                Check("A → Interact", ChuonInput.Pressed(ChuonAction.Interact));
                Release(pad);
                Press(pad, GamepadButton.RightStick);
                Check("R3 → LockOn", ChuonInput.Pressed(ChuonAction.LockOn));
                Release(pad);
                Press(pad, GamepadButton.Start);
                Check("START → Pause", ChuonInput.Pressed(ChuonAction.Pause));
                Release(pad);

                // 2) 트리거 (아날로그 → 버튼, 누름 기준 0.5)
                QueueState(pad, new GamepadState { rightTrigger = 0.9f });
                Check("RT → AttackHeavy", ChuonInput.Pressed(ChuonAction.AttackHeavy));
                QueueState(pad, new GamepadState { leftTrigger = 0.9f });
                Check("LT → Ultimate", ChuonInput.Pressed(ChuonAction.Ultimate));
                Release(pad);

                // 3) 가드 유지/놓음
                Press(pad, GamepadButton.LeftShoulder);
                Check("LB → Guard pressed", ChuonInput.Pressed(ChuonAction.Guard));
                Step();
                Check("LB → Guard held", ChuonInput.Held(ChuonAction.Guard));
                Release(pad);
                Check("LB up → Guard released", ChuonInput.Released(ChuonAction.Guard));

                // 4) 이동 스틱 + 데드존 (0.2)
                QueueState(pad, new GamepadState { leftStick = new Vector2(0.8f, 0f) });
                Vector2 m = ChuonInput.PadMove();
                Check("L stick 0.8 → move x>0.6 (" + m.x.ToString("F2") + ")", m.x > 0.6f);
                QueueState(pad, new GamepadState { leftStick = new Vector2(0.12f, 0f) });
                Check("L stick 0.12 → deadzone 0", ChuonInput.PadMove().sqrMagnitude < 0.0001f);
                Release(pad);
            }

            // 5) 글리프
            Check("glyph Dodge Xbox = B", InputGlyphs.Get(ChuonAction.Dodge, ChuonInputScheme.Xbox) == "B");
            Check("glyph Dodge PS = CIRCLE", InputGlyphs.Get(ChuonAction.Dodge, ChuonInputScheme.PlayStation) == "CIRCLE");
            Check("glyph Dodge KBM = L-SHIFT", InputGlyphs.Get(ChuonAction.Dodge, ChuonInputScheme.KeyboardMouse) == "L-SHIFT");
            Check("glyph Attack KBM = 좌클릭", InputGlyphs.Get(ChuonAction.AttackLight, ChuonInputScheme.KeyboardMouse) == "좌클릭");
            Check("glyph Guard PS = L1", InputGlyphs.Get(ChuonAction.Guard, ChuonInputScheme.PlayStation) == "L1");
            Check("glyph LockOn KBM = TAB / 휠클릭", InputGlyphs.Get(ChuonAction.LockOn, ChuonInputScheme.KeyboardMouse) == "TAB / 휠클릭");
            Check("glyph Move KBM = W/A/S/D", InputGlyphs.Get(ChuonAction.Move, ChuonInputScheme.KeyboardMouse) == "W/A/S/D");
            Check("glyph tag fallback = [좌클릭]", InputGlyphs.SpriteAsset != null || ChuonInput.Scheme != ChuonInputScheme.KeyboardMouse
                || InputGlyphs.GetTag(ChuonAction.AttackLight) == "[좌클릭]");
            Check("scheme for plain Gamepad = Generic", ChuonInput.SchemeForGamepad(pad) == ChuonInputScheme.GenericGamepad);

            // 6) 키보드 오버라이드 → 레거시 키 끄기 플래그 + JSON 저장/복원
            Check("KB Dodge not overridden by default", !ChuonInput.KeyboardOverridden(ChuonAction.Dodge));
            ChuonInput.KeyboardAction(ChuonAction.Dodge).ApplyBindingOverride(0, "<Keyboard>/space");
            ChuonInput.SaveOverrides();
            Check("KB Dodge overridden after rebind", ChuonInput.KeyboardOverridden(ChuonAction.Dodge));
            string json = PlayerPrefs.GetString(ChuonInput.OverridesPrefKey, string.Empty);
            Check("overrides JSON saved", json.Contains("Dodge") && json.Contains("<Keyboard>/space"));
            Check("glyph Dodge KBM = SPACE", InputGlyphs.Get(ChuonAction.Dodge, ChuonInputScheme.KeyboardMouse) == "SPACE");
            ChuonInput.KeyboardAction(ChuonAction.Dodge).RemoveAllBindingOverrides();
            ChuonInput.ReloadOverridesFromPrefs();
            Check("reload from prefs restores override", ChuonInput.KeyboardOverridden(ChuonAction.Dodge)
                && ChuonInput.KeyboardAction(ChuonAction.Dodge).bindings[0].effectivePath == "<Keyboard>/space");
            ChuonInput.ResetAllBindings();
            Check("reset → not overridden + prefs cleared", !ChuonInput.KeyboardOverridden(ChuonAction.Dodge) && !PlayerPrefs.HasKey(ChuonInput.OverridesPrefKey));

            // 7) 인터랙티브 리바인드 (가상 패드) + 충돌 교체 + 취소
            //    (리바인드 직후 1프레임은 입력 억제 → 이 뒤로는 Pressed 검사를 하지 않는다)
            ChuonInputRebinding.Result r1 = default;
            bool done1 = false;
            Check("rebind start (pad Dodge)", ChuonInputRebinding.Start(ChuonAction.Dodge, true, -1, res => { r1 = res; done1 = true; }, true));
            Press(pad, GamepadButton.North);
            System.Threading.Thread.Sleep(150);
            Step();
            Release(pad);
            Check("rebind completed → Y (" + r1.newPath + ")", done1 && r1.completed && r1.newPath == "<Gamepad>/buttonNorth");
            Check("menu-only Y (MenuAlt) not swapped", string.IsNullOrEmpty(r1.swappedWith)
                && ChuonInput.GamepadAction(ChuonAction.MenuAlt).bindings[0].effectivePath == "<Gamepad>/buttonNorth");

            ChuonInputRebinding.Result r2 = default;
            bool done2 = false;
            Check("rebind start (pad Dodge → X)", ChuonInputRebinding.Start(ChuonAction.Dodge, true, -1, res => { r2 = res; done2 = true; }, true));
            Press(pad, GamepadButton.West);
            System.Threading.Thread.Sleep(150);
            Step();
            Release(pad);
            Check("conflict swap: Dodge=X, Heal=Y (" + r2.swappedWith + ")", done2 && r2.completed
                && ChuonInput.GamepadAction(ChuonAction.Dodge).bindings[0].effectivePath == "<Gamepad>/buttonWest"
                && ChuonInput.GamepadAction(ChuonAction.Heal).bindings[0].effectivePath == "<Gamepad>/buttonNorth"
                && r2.swappedWith == ChuonInput.DisplayName(ChuonAction.Heal));
            Check("pad overrides persisted", PlayerPrefs.GetString(ChuonInput.OverridesPrefKey, string.Empty).Contains("buttonWest"));

            ChuonInputRebinding.Result r3 = default;
            bool done3 = false;
            Check("rebind start (cancel test)", ChuonInputRebinding.Start(ChuonAction.Guard, true, -1, res => { r3 = res; done3 = true; }, true));
            Press(pad, GamepadButton.Start);
            Step();
            Release(pad);
            Check("START cancels rebind, Guard unchanged", done3 && !r3.completed
                && ChuonInput.GamepadAction(ChuonAction.Guard).bindings[0].effectivePath == "<Gamepad>/leftShoulder");
        }
        catch (Exception e)
        {
            s_fail++;
            s_log.AppendLine("EXCEPTION " + e);
        }
        finally
        {
            try { ChuonInputRebinding.Cancel(); } catch { }
            if (pad != null)
                InputSystem.RemoveDevice(pad);
            ChuonInput.ResetAllBindings();
            if (hadPrefs)
                PlayerPrefs.SetString(ChuonInput.OverridesPrefKey, savedPrefs);
            PlayerPrefs.Save();
            ChuonInput.ReloadOverridesFromPrefs(); // 사용자 키 설정 복원
        }

        s_log.AppendLine(s_fail == 0 ? "RESULT: PASS" : "RESULT: FAIL x" + s_fail);
        return s_log.ToString();
    }

    static void Check(string label, bool ok)
    {
        if (!ok)
            s_fail++;
        s_log.AppendLine((ok ? "  ok   " : "  FAIL ") + label);
    }

    static void Press(Gamepad pad, GamepadButton button)
    {
        QueueState(pad, new GamepadState().WithButton(button));
    }

    static void Release(Gamepad pad)
    {
        QueueState(pad, new GamepadState());
    }

    static void QueueState(Gamepad pad, GamepadState state)
    {
        InputSystem.QueueStateEvent(pad, state);
        Step();
    }

    static void Step()
    {
        InputSystem.Update();
    }
}
