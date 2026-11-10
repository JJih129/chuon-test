#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

/// <summary>
/// 반복 14A (개발 전용): 가상 게임패드로 "짧게 눌렀다 떼기"를 보낸다 (실제 패드 UI 흐름 검증용).
/// 상태를 계속 쥐지 않으므로 UI 모듈의 자동 반복(0.4 s 지연)이 걸리지 않는다. 스스로 실행되지 않는다:
/// Play 모드에서 MCP execute_code 로 QaPadInput.Tap(GamepadButton.South) 등을 호출.
/// </summary>
public sealed class QaPadInput : MonoBehaviour
{
    static QaPadInput s_runner;
    static Gamepad s_pad;

    public static int TapsSent { get; private set; }

    public static Gamepad Pad
    {
        get
        {
            if (s_pad == null || !s_pad.added)
            {
                // 도메인 리로드 뒤에도 장치는 남는다 → 기존 가상 패드 재사용 (여러 개면 Navigate 가 충돌)
                s_pad = null;
                foreach (InputDevice d in InputSystem.devices)
                    if (d is Gamepad g && d.name.StartsWith("QaVirtualPad")) { s_pad = g; break; }
                if (s_pad == null)
                    s_pad = InputSystem.AddDevice<Gamepad>("QaVirtualPad");
            }
            return s_pad;
        }
    }

    static QaPadInput Runner
    {
        get
        {
            if (s_runner == null)
            {
                var go = new GameObject("__QaPadInput");
                DontDestroyOnLoad(go);
                s_runner = go.AddComponent<QaPadInput>();
            }
            return s_runner;
        }
    }

    /// <summary>버튼을 holdFrames 프레임 누른 뒤 뗀다 (기본 2프레임 = 사람의 짧은 탭).</summary>
    public static void Tap(GamepadButton button, int holdFrames = 2)
    {
        Runner.StartCoroutine(Runner.CoTap(button, Mathf.Max(1, holdFrames)));
    }

    /// <summary>D-패드/스틱 한 칸 이동 (dir: up/down/left/right).</summary>
    public static void Nav(string dir, int holdFrames = 2)
    {
        GamepadButton b = dir switch
        {
            "up" => GamepadButton.DpadUp,
            "down" => GamepadButton.DpadDown,
            "left" => GamepadButton.DpadLeft,
            _ => GamepadButton.DpadRight
        };
        Tap(b, holdFrames);
    }

    /// <summary>seconds 동안 interval 마다 씬·선택 UI·스킴을 Temp/qa/padtrace.txt 에 기록 (실시간, 일시정지 무관).</summary>
    public static void Trace(float seconds, float interval = 0.25f)
    {
        Runner.StartCoroutine(Runner.CoTrace(seconds, Mathf.Max(0.02f, interval)));
    }

    /// <summary>
    /// 한 번에 실행하는 탭 시퀀스 (MCP 호출 지연 없이 사람 속도로). spec 예: "South,wait:1.5,Start,wait:1,East".
    /// 각 단계 뒤 상태를 Temp/qa/padtrace.txt 에 기록. 버튼 이름 = GamepadButton 열거형 (DpadUp/South/Start/LeftShoulder ...).
    /// </summary>
    public static void RunSequence(string spec, float gap = 0.35f)
    {
        Runner.StartCoroutine(Runner.CoSequence(spec, gap));
    }

    IEnumerator CoSequence(string spec, float gap)
    {
        System.IO.Directory.CreateDirectory(QaCombatBot.OutputDir);
        var sb = new System.Text.StringBuilder();
        float start = Time.realtimeSinceStartup;
        sb.AppendLine($"[0.00] start {Describe()}");
        foreach (string raw in spec.Split(','))
        {
            string step = raw.Trim();
            if (step.Length == 0)
                continue;
            if (step.StartsWith("wait:"))
            {
                float.TryParse(step.Substring(5), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float w);
                yield return new WaitForSecondsRealtime(w);
            }
            else if (System.Enum.TryParse(step, true, out GamepadButton b))
            {
                yield return CoTap(b, 2);
                yield return new WaitForSecondsRealtime(gap);
            }
            sb.AppendLine($"[{Time.realtimeSinceStartup - start:F2}] {step} -> {Describe()}");
        }
        sb.AppendLine("DONE");
        System.IO.File.WriteAllText(TracePath, sb.ToString());
    }

    public static string TracePath => System.IO.Path.Combine(QaCombatBot.OutputDir, "padtrace.txt");

    IEnumerator CoTrace(float seconds, float interval)
    {
        System.IO.Directory.CreateDirectory(QaCombatBot.OutputDir);
        var sb = new System.Text.StringBuilder();
        float start = Time.realtimeSinceStartup;
        string last = null;
        while (Time.realtimeSinceStartup - start < seconds)
        {
            string now = Describe();
            if (now != last)
            {
                sb.AppendLine($"[{Time.realtimeSinceStartup - start:F2}] {now}");
                last = now;
            }
            yield return new WaitForSecondsRealtime(interval);
        }
        System.IO.File.WriteAllText(TracePath, sb.ToString());
    }

    /// <summary>Describe() 끝에 붙일 추가 상태 (MCP 코드에서 람다로 지정).</summary>
    public static System.Func<string> ExtraProbe;

    public static string Describe()
    {
        UnityEngine.EventSystems.EventSystem es = UnityEngine.EventSystems.EventSystem.current;
        GameObject sel = es != null ? es.currentSelectedGameObject : null;
        string path = "null";
        if (sel != null)
        {
            path = sel.name;
            Transform t = sel.transform.parent;
            for (int d = 0; t != null && d < 2; d++, t = t.parent)
                path = t.name + "/" + path;
        }
        PauseManager pm = PauseManager.Instance;
        TitleSettingsOverlay settings = FindObjectOfType<TitleSettingsOverlay>();
        return $"scene={UnityEngine.SceneManagement.SceneManager.GetActiveScene().name} scheme={ChuonInput.Scheme} sel={path} paused={(pm != null && pm.IsPaused)} " +
               $"settings={(settings != null && settings.IsVisible)} rebinding={ChuonInputRebinding.IsRebinding} dodgePad={ChuonInput.GamepadAction(ChuonAction.Dodge)?.bindings[0].effectivePath} ts={Time.timeScale:F2}" + (ExtraProbe != null ? " " + ExtraProbe() : "");
    }

    public static void Remove()
    {
        if (s_pad != null && s_pad.added)
            InputSystem.RemoveDevice(s_pad);
        s_pad = null;
    }

    IEnumerator CoTap(GamepadButton button, int holdFrames)
    {
        Gamepad pad = Pad;
        InputSystem.QueueStateEvent(pad, new GamepadState().WithButton(button, true));
        for (int i = 0; i < holdFrames; i++)
            yield return null;
        InputSystem.QueueStateEvent(pad, new GamepadState());
        TapsSent++;
    }

    void OnDestroy()
    {
        if (s_runner == this)
            s_runner = null;
    }
}
#endif
