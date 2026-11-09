// 인터랙티브 리바인딩 (Input System RebindingOperation)
//  - 키보드: <Keyboard>/<Mouse> 버튼만, ESC = 취소 (ESC 는 일시정지 고정). 패드: <Gamepad> 버튼만 (스틱 방향 제외), START = 취소.
//  - 시작 전에 눌려 있던 버튼(리바인드 버튼을 누른 A / 좌클릭)이 떨어질 때까지 기다린다 → 그 버튼이 바로 잡히지 않음.
//  - 충돌: 같은 장치 맵 안에서 새 경로를 이미 쓰는 바인딩이 있으면 서로 교체(swap)하고 결과에 알린다.
//  - 완료/취소 후 ChuonInput.SaveOverrides() → PlayerPrefs JSON 저장 + 레거시 키 사용 여부 갱신.
using System;
using UnityEngine;
using UnityEngine.InputSystem;

public static class ChuonInputRebinding
{
    public struct Result
    {
        public ChuonAction action;
        public bool gamepad;
        public int bindingIndex;
        public bool completed;
        public string newPath;
        /// <summary>교체된 다른 동작 이름 (없으면 null).</summary>
        public string swappedWith;
    }

    public const float TimeoutSeconds = 6f;

    static InputActionRebindingExtensions.RebindingOperation s_op;
    static bool s_pending;
    static int s_finishedFrame = -10;

    /// <summary>리바인드 진행 중 (또는 끝난 직후 1프레임: 바인딩에 쓴 키/ESC 가 레거시 Input 으로 게임·메뉴에 새지 않게).</summary>
    public static bool IsRebinding => s_op != null || s_pending || Time.frameCount <= s_finishedFrame + 1;
    public static ChuonAction CurrentAction { get; private set; }
    public static bool CurrentIsGamepad { get; private set; }
    public static int CurrentBindingIndex { get; private set; } = -1;

    public static event Action<Result> Finished;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        s_op?.Dispose();
        s_op = null;
        s_pending = false;
        s_finishedFrame = -10;
        CurrentBindingIndex = -1;
        Finished = null;
    }

    public static bool CanRebind(ChuonAction a, bool gamepad)
    {
        if (!ChuonInput.IsRebindable(a))
            return false;
        // 패드 이동(왼스틱)은 고정 (스틱 축 리바인드는 지원하지 않음)
        return !(gamepad && a == ChuonAction.Move);
    }

    /// <summary>리바인드 시작. bindingIndex &lt; 0 이면 대표 바인딩. 이미 진행 중이면 false.</summary>
    public static bool Start(ChuonAction a, bool gamepad, int bindingIndex = -1, Action<Result> onDone = null, bool skipReleaseWait = false)
    {
        if (s_op != null || s_pending || !CanRebind(a, gamepad))
            return false;

        InputAction action = gamepad ? ChuonInput.GamepadAction(a) : ChuonInput.KeyboardAction(a);
        if (action == null)
            return false;
        int idx = bindingIndex >= 0 ? bindingIndex : ChuonInput.PrimaryBindingIndex(a, gamepad);
        if (idx < 0 || idx >= action.bindings.Count || action.bindings[idx].isComposite)
            return false;

        CurrentAction = a;
        CurrentIsGamepad = gamepad;
        CurrentBindingIndex = idx;
        s_pending = true;

        ChuonInputRuntime runtime = ChuonInputRuntime.Ensure();
        if (runtime != null && Application.isPlaying && !skipReleaseWait)
            runtime.RunWhenButtonsReleased(() => Begin(action, a, gamepad, idx, onDone));
        else
            Begin(action, a, gamepad, idx, onDone);
        return true;
    }

    public static void Cancel()
    {
        if (s_op != null)
            s_op.Cancel();
        else
            s_pending = false;
    }

    static void Begin(InputAction action, ChuonAction a, bool gamepad, int idx, Action<Result> onDone)
    {
        if (!s_pending)
            return; // 대기 중 취소됨

        string oldPath = action.bindings[idx].effectivePath;
        action.Disable();

        var op = action.PerformInteractiveRebinding(idx)
            .WithExpectedControlType("Button")
            .WithTimeout(TimeoutSeconds)
            .OnMatchWaitForAnother(0.08f);

        if (gamepad)
        {
            op.WithControlsHavingToMatchPath("<Gamepad>")
                .WithControlsExcluding("<Gamepad>/leftStick/*")
                .WithControlsExcluding("<Gamepad>/rightStick/*")
                .WithControlsExcluding("<Gamepad>/start")
                .WithCancelingThrough("<Gamepad>/start");
        }
        else
        {
            op.WithControlsHavingToMatchPath("<Keyboard>");
            if (a != ChuonAction.Move)
                op.WithControlsHavingToMatchPath("<Mouse>");
            op.WithControlsExcluding("<Keyboard>/anyKey")
                .WithControlsExcluding("<Mouse>/scroll/*")
                .WithControlsExcluding("<Mouse>/forwardButton")
                .WithControlsExcluding("<Mouse>/backButton")
                .WithCancelingThrough("<Keyboard>/escape");
        }

        op.OnComplete(o => Finish(action, a, gamepad, idx, oldPath, true, onDone))
          .OnCancel(o => Finish(action, a, gamepad, idx, oldPath, false, onDone));

        s_op = op;
        s_pending = false;
        op.Start();
    }

    static void Finish(InputAction action, ChuonAction a, bool gamepad, int idx, string oldPath, bool completed, Action<Result> onDone)
    {
        var result = new Result { action = a, gamepad = gamepad, bindingIndex = idx, completed = completed };
        string newPath = action.bindings[idx].effectivePath;
        result.newPath = newPath;

        if (completed && !string.IsNullOrEmpty(newPath) && !PathsEqual(newPath, oldPath))
            result.swappedWith = ResolveConflicts(action, idx, newPath, oldPath, gamepad);

        s_op?.Dispose();
        s_op = null;
        s_pending = false;
        s_finishedFrame = Time.frameCount;
        CurrentBindingIndex = -1;
        action.Enable();
        ChuonInput.SaveOverrides();

        try { onDone?.Invoke(result); }
        catch (Exception e) { Debug.LogException(e); }
        try { Finished?.Invoke(result); }
        catch (Exception e) { Debug.LogException(e); }
    }

    // 같은 맵에서 newPath 를 쓰던 바인딩을 oldPath 로 옮긴다 (교체). 반환: 교체된 동작 표시 이름.
    static string ResolveConflicts(InputAction changed, int changedIndex, string newPath, string oldPath, bool gamepad)
    {
        string swapped = null;
        InputActionMap map = changed.actionMap;
        if (map == null)
            return null;

        foreach (InputAction other in map.actions)
        {
            for (int b = 0; b < other.bindings.Count; b++)
            {
                if (other == changed && b == changedIndex)
                    continue;
                InputBinding binding = other.bindings[b];
                if (binding.isComposite || !PathsEqual(binding.effectivePath, newPath))
                    continue;
                // 고정 키(일시정지 / 메뉴 전용 콤보 표)는 메뉴 문맥 전용이라 게임플레이 키와 겹쳐도 교체하지 않는다
                if (Enum.TryParse(other.name, out ChuonAction fixedAction) && !ChuonInput.IsRebindable(fixedAction))
                    continue;

                other.ApplyBindingOverride(b, oldPath);
                if (swapped == null && Enum.TryParse(other.name, out ChuonAction otherAction))
                    swapped = ChuonInput.DisplayName(otherAction);
            }
        }

        return swapped;
    }

    static bool PathsEqual(string a, string b)
    {
        return string.Equals(a ?? string.Empty, b ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>한 동작의 한 장치 바인딩만 기본값으로.</summary>
    public static void ResetAction(ChuonAction a, bool gamepad)
    {
        InputAction action = gamepad ? ChuonInput.GamepadAction(a) : ChuonInput.KeyboardAction(a);
        if (action == null)
            return;
        action.RemoveAllBindingOverrides();
        ChuonInput.SaveOverrides();
    }

    public static void ResetAll()
    {
        Cancel();
        ChuonInput.ResetAllBindings();
    }
}
