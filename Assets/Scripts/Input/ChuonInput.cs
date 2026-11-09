// 추온 입력 서비스 (반복 13: 게임패드 + 리바인딩)
//  - 키보드/마우스: 기존 레거시 Input(KeyCode) 경로를 그대로 쓴다. 사용자가 그 동작의 키보드 바인딩을 바꾼 경우에만
//    레거시 키를 끄고 Input System(리바인드된 경로)으로 읽는다 → 리바인드하지 않은 사용자의 KB+M 동작은 1:1 동일.
//  - 게임패드: 코드로 만든 InputActionAsset 의 'Gamepad' 맵 (소울라이크 표준 배치, 리바인드 가능).
//  - 리바인드 저장: PlayerPrefs(opt_input_overrides_v1) 에 JSON {map, action, index, path} 목록. (에셋을 코드로 만들어 바인딩 GUID 가
//    매 실행 바뀌므로 Input System 기본 SaveBindingOverridesAsJson 대신 인덱스 기반으로 저장한다.)
//  - 마지막 입력 장치(키보드·마우스 / Xbox / PlayStation)를 추적해 InputGlyphs 가 프롬프트 표기를 바꾼다.
// 도메인 리로드 꺼짐(Enter Play Mode Options) → 정적 상태는 SubsystemRegistration 에서 초기화.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public enum ChuonAction : byte
{
    Move = 0,
    AttackLight = 1,
    AttackHeavy = 2,
    Guard = 3,
    Dodge = 4,
    LockOn = 5,
    Heal = 6,
    Ultimate = 7,
    Interact = 8,
    Pause = 9,
    MenuAlt = 10
}

public enum ChuonInputScheme : byte
{
    KeyboardMouse = 0,
    Xbox = 1,
    PlayStation = 2,
    GenericGamepad = 3
}

public static class ChuonInput
{
    public const string OverridesPrefKey = "opt_input_overrides_v1";
    public const string KeyboardMapName = "KeyboardMouse";
    public const string GamepadMapName = "Gamepad";
    public const string UiMapName = "UI";
    public const int ActionCount = 11;

    /// <summary>Move 컴포지트 파트 바인딩 인덱스 (0 = 컴포지트 헤더).</summary>
    public const int MoveUpIndex = 1, MoveDownIndex = 2, MoveLeftIndex = 3, MoveRightIndex = 4;

    struct Def
    {
        public string name;
        public string displayName;
        public string[] keyboard;
        public string[] gamepad;
        public bool rebindable;
    }

    // 키보드 기본값 = 플레이어 프리팹의 레거시 KeyCode 와 동일 (PlayerRoot.prefab: dodgeKey LeftShift / toggleKey Tab + Mouse2 / interactKey F,
    // 가드 E, 회복 Q, 궁극기 R, 공격 좌/우클릭). 패드 = 소울라이크 표준 (RB 약 / RT 강 / LB 가드 / B 회피 / R3 락온 / X 회복 / LT 궁극기 / A 상호작용).
    static readonly Def[] s_defs =
    {
        new Def { name = "Move", displayName = "이동", keyboard = null, gamepad = new[] { "<Gamepad>/leftStick" }, rebindable = true },
        new Def { name = "AttackLight", displayName = "기본공격", keyboard = new[] { "<Mouse>/leftButton" }, gamepad = new[] { "<Gamepad>/rightShoulder" }, rebindable = true },
        new Def { name = "AttackHeavy", displayName = "강공격", keyboard = new[] { "<Mouse>/rightButton" }, gamepad = new[] { "<Gamepad>/rightTrigger" }, rebindable = true },
        new Def { name = "Guard", displayName = "가드 / 패링", keyboard = new[] { "<Keyboard>/e" }, gamepad = new[] { "<Gamepad>/leftShoulder" }, rebindable = true },
        new Def { name = "Dodge", displayName = "회피", keyboard = new[] { "<Keyboard>/leftShift" }, gamepad = new[] { "<Gamepad>/buttonEast" }, rebindable = true },
        new Def { name = "LockOn", displayName = "락온", keyboard = new[] { "<Keyboard>/tab", "<Mouse>/middleButton" }, gamepad = new[] { "<Gamepad>/rightStickPress" }, rebindable = true },
        new Def { name = "Heal", displayName = "회복 앰플", keyboard = new[] { "<Keyboard>/q" }, gamepad = new[] { "<Gamepad>/buttonWest" }, rebindable = true },
        new Def { name = "Ultimate", displayName = "궁극기", keyboard = new[] { "<Keyboard>/r" }, gamepad = new[] { "<Gamepad>/leftTrigger" }, rebindable = true },
        new Def { name = "Interact", displayName = "상호작용", keyboard = new[] { "<Keyboard>/f" }, gamepad = new[] { "<Gamepad>/buttonSouth" }, rebindable = true },
        new Def { name = "Pause", displayName = "일시정지", keyboard = new[] { "<Keyboard>/escape" }, gamepad = new[] { "<Gamepad>/start" }, rebindable = false },
        new Def { name = "MenuAlt", displayName = "콤보 표", keyboard = new[] { "<Keyboard>/c" }, gamepad = new[] { "<Gamepad>/buttonNorth" }, rebindable = false },
    };

    static InputActionAsset s_asset;
    static InputActionMap s_kbMap, s_padMap, s_uiMap;
    static readonly InputAction[] s_kb = new InputAction[ActionCount];
    static readonly InputAction[] s_pad = new InputAction[ActionCount];
    static readonly bool[] s_kbOverridden = new bool[ActionCount];
    static ChuonInputScheme s_scheme = ChuonInputScheme.KeyboardMouse;

    public static event Action SchemeChanged;
    public static event Action BindingsChanged;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        if (s_asset != null)
        {
            s_asset.Disable();
            if (Application.isPlaying)
                UnityEngine.Object.Destroy(s_asset);
            else
                UnityEngine.Object.DestroyImmediate(s_asset);
        }
        s_asset = null;
        s_kbMap = s_padMap = s_uiMap = null;
        Array.Clear(s_kb, 0, s_kb.Length);
        Array.Clear(s_pad, 0, s_pad.Length);
        Array.Clear(s_kbOverridden, 0, s_kbOverridden.Length);
        s_scheme = ChuonInputScheme.KeyboardMouse;
        SchemeChanged = null;
        BindingsChanged = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Bootstrap()
    {
        EnsureInitialized();
        ChuonInputRuntime.Ensure();
    }

    public static InputActionAsset Asset { get { EnsureInitialized(); return s_asset; } }
    public static InputActionMap UiMap { get { EnsureInitialized(); return s_uiMap; } }
    public static ChuonInputScheme Scheme => s_scheme;
    public static bool IsGamepadScheme => s_scheme != ChuonInputScheme.KeyboardMouse;

    public static InputAction KeyboardAction(ChuonAction a) { EnsureInitialized(); return s_kb[(int)a]; }
    public static InputAction GamepadAction(ChuonAction a) { EnsureInitialized(); return s_pad[(int)a]; }
    public static string DisplayName(ChuonAction a) => s_defs[(int)a].displayName;
    public static bool IsRebindable(ChuonAction a) => s_defs[(int)a].rebindable;

    public static void EnsureInitialized()
    {
        if (s_asset != null)
            return;

        s_asset = ScriptableObject.CreateInstance<InputActionAsset>();
        s_asset.name = "ChuonInput (runtime)";
        s_asset.hideFlags = HideFlags.DontSave;
        s_kbMap = s_asset.AddActionMap(KeyboardMapName);
        s_padMap = s_asset.AddActionMap(GamepadMapName);

        for (int i = 0; i < s_defs.Length; i++)
        {
            Def d = s_defs[i];
            bool isMove = i == (int)ChuonAction.Move;
            InputActionType type = isMove ? InputActionType.Value : InputActionType.Button;
            string layout = isMove ? "Vector2" : "Button";

            InputAction kb = s_kbMap.AddAction(d.name, type, null, null, null, null, layout);
            if (isMove)
            {
                // mode=1 (Digital): 축별 -1/0/1, 대각선 정규화 없음 = 레거시 GetAxisRaw 와 같은 값
                kb.AddCompositeBinding("2DVector(mode=1)")
                    .With("Up", "<Keyboard>/w")
                    .With("Down", "<Keyboard>/s")
                    .With("Left", "<Keyboard>/a")
                    .With("Right", "<Keyboard>/d");
            }
            else if (d.keyboard != null)
            {
                for (int k = 0; k < d.keyboard.Length; k++)
                    kb.AddBinding(d.keyboard[k]);
            }
            s_kb[i] = kb;

            InputAction pad = s_padMap.AddAction(d.name, type, null, null, isMove ? "stickDeadzone(min=0.2,max=0.925)" : null, null, layout);
            for (int k = 0; k < d.gamepad.Length; k++)
                pad.AddBinding(d.gamepad[k]);
            s_pad[i] = pad;
        }

        BuildUiMap();
        LoadOverrides();
        s_asset.Enable();
    }

    // UI 네비게이션 (InputSystemUIInputModule 용). 레거시 StandaloneInputModule 과 같은 키(방향키/WASD, Enter/Space, Esc) + 패드(D-패드/왼스틱, A, B).
    static void BuildUiMap()
    {
        s_uiMap = s_asset.AddActionMap(UiMapName);
        InputAction point = s_uiMap.AddAction("Point", InputActionType.PassThrough, "<Mouse>/position", null, null, null, "Vector2");
        point.AddBinding("<Pen>/position");
        point.AddBinding("<Touchscreen>/touch*/position");
        InputAction click = s_uiMap.AddAction("Click", InputActionType.PassThrough, "<Mouse>/leftButton", null, null, null, "Button");
        click.AddBinding("<Touchscreen>/touch*/press");
        s_uiMap.AddAction("RightClick", InputActionType.PassThrough, "<Mouse>/rightButton", null, null, null, "Button");
        s_uiMap.AddAction("MiddleClick", InputActionType.PassThrough, "<Mouse>/middleButton", null, null, null, "Button");
        s_uiMap.AddAction("ScrollWheel", InputActionType.PassThrough, "<Mouse>/scroll", null, null, null, "Vector2");

        InputAction nav = s_uiMap.AddAction("Navigate", InputActionType.PassThrough, null, null, null, null, "Vector2");
        nav.AddBinding("<Gamepad>/leftStick");
        nav.AddBinding("<Gamepad>/dpad");
        nav.AddCompositeBinding("2DVector")
            .With("Up", "<Keyboard>/upArrow").With("Down", "<Keyboard>/downArrow")
            .With("Left", "<Keyboard>/leftArrow").With("Right", "<Keyboard>/rightArrow");
        nav.AddCompositeBinding("2DVector")
            .With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s")
            .With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");

        InputAction submit = s_uiMap.AddAction("Submit", InputActionType.Button, "<Keyboard>/enter");
        submit.AddBinding("<Keyboard>/numpadEnter");
        submit.AddBinding("<Keyboard>/space");
        submit.AddBinding("<Gamepad>/buttonSouth");
        InputAction cancel = s_uiMap.AddAction("Cancel", InputActionType.Button, "<Keyboard>/escape");
        cancel.AddBinding("<Gamepad>/buttonEast");
    }

    // ------------------------------------------------------------------ queries

    /// <summary>이 동작의 키보드 바인딩을 사용자가 바꿨는가 (true 면 레거시 KeyCode 는 무시).</summary>
    public static bool KeyboardOverridden(ChuonAction a)
    {
        EnsureInitialized();
        return s_kbOverridden[(int)a];
    }

    /// <summary>패드(및 리바인드된 키보드) 이번 프레임 눌림. 레거시 키는 포함하지 않는다.</summary>
    public static bool Pressed(ChuonAction a)
    {
        EnsureInitialized();
        int i = (int)a;
        if (ChuonInputRebinding.IsRebinding)
            return false;
        return s_pad[i].WasPressedThisFrame() || (s_kbOverridden[i] && s_kb[i].WasPressedThisFrame());
    }

    public static bool Released(ChuonAction a)
    {
        EnsureInitialized();
        int i = (int)a;
        return s_pad[i].WasReleasedThisFrame() || (s_kbOverridden[i] && s_kb[i].WasReleasedThisFrame());
    }

    public static bool Held(ChuonAction a)
    {
        EnsureInitialized();
        int i = (int)a;
        if (ChuonInputRebinding.IsRebinding)
            return false;
        return s_pad[i].IsPressed() || (s_kbOverridden[i] && s_kb[i].IsPressed());
    }

    /// <summary>레거시 KeyCode(리바인드 안 했을 때) 또는 패드/리바인드 키. 호출부의 기존 Input.GetKeyDown(key) 를 그대로 대체.</summary>
    public static bool PressedOr(ChuonAction a, KeyCode legacy, KeyCode legacyAlt = KeyCode.None)
    {
        if (Pressed(a))
            return true;
        return !KeyboardOverridden(a) && (LegacyDown(legacy) || LegacyDown(legacyAlt));
    }

    public static bool HeldOr(ChuonAction a, KeyCode legacy, KeyCode legacyAlt = KeyCode.None)
    {
        if (Held(a))
            return true;
        return !KeyboardOverridden(a) && (LegacyHeld(legacy) || LegacyHeld(legacyAlt));
    }

    /// <summary>
    /// 놓음: 레거시 키 놓음 / 패드 놓음. 다른 장치가 여전히 누르고 있으면 놓음으로 치지 않는다 (가드 유지 중 장치 전환 대비).
    /// </summary>
    public static bool ReleasedOr(ChuonAction a, KeyCode legacy, KeyCode legacyAlt = KeyCode.None)
    {
        bool legacyActive = !KeyboardOverridden(a);
        bool released = Released(a) || (legacyActive && (LegacyUp(legacy) || LegacyUp(legacyAlt)));
        if (!released)
            return false;
        return !HeldOr(a, legacy, legacyAlt);
    }

    /// <summary>
    /// 이동 입력: 레거시 축(리바인드 안 했을 때, 기존 그대로) 또는 리바인드된 WASD 컴포지트, 그리고 패드 왼스틱 중 큰 쪽.
    /// 패드를 안 쓰면 반환값은 기존 Input.GetAxisRaw(axisX, axisY) 와 동일.
    /// </summary>
    public static Vector2 ReadMove(string axisX = "Horizontal", string axisY = "Vertical")
    {
        EnsureInitialized();
        Vector2 v = Vector2.zero;
        if (!s_kbOverridden[(int)ChuonAction.Move])
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            try
            {
                v.x = Input.GetAxisRaw(axisX);
                v.y = Input.GetAxisRaw(axisY);
            }
            catch { }
#endif
        }
        else if (!ChuonInputRebinding.IsRebinding)
        {
            v = s_kb[(int)ChuonAction.Move].ReadValue<Vector2>();
        }

        Vector2 pad = PadMove();
        return pad.sqrMagnitude > v.sqrMagnitude ? pad : v;
    }

    /// <summary>패드 왼스틱 (데드존 적용, 크기 0..1).</summary>
    public static Vector2 PadMove()
    {
        EnsureInitialized();
        if (ChuonInputRebinding.IsRebinding)
            return Vector2.zero;
        return Vector2.ClampMagnitude(s_pad[(int)ChuonAction.Move].ReadValue<Vector2>(), 1f);
    }

    /// <summary>메뉴 '뒤로' (패드 B / Circle). 리바인드 중에는 false.</summary>
    public static bool PadBackPressed()
    {
        Gamepad pad = Gamepad.current;
        return pad != null && !ChuonInputRebinding.IsRebinding && pad.buttonEast.wasPressedThisFrame;
    }

    /// <summary>메뉴 탭 전환 (LB = -1 / RB = +1).</summary>
    public static int PadTabDelta()
    {
        Gamepad pad = Gamepad.current;
        if (pad == null || ChuonInputRebinding.IsRebinding)
            return 0;
        if (pad.leftShoulder.wasPressedThisFrame) return -1;
        if (pad.rightShoulder.wasPressedThisFrame) return 1;
        return 0;
    }

    static bool LegacyDown(KeyCode k)
    {
#if ENABLE_LEGACY_INPUT_MANAGER
        return k != KeyCode.None && !ChuonInputRebinding.IsRebinding && Input.GetKeyDown(k);
#else
        return false;
#endif
    }

    static bool LegacyHeld(KeyCode k)
    {
#if ENABLE_LEGACY_INPUT_MANAGER
        return k != KeyCode.None && !ChuonInputRebinding.IsRebinding && Input.GetKey(k);
#else
        return false;
#endif
    }

    static bool LegacyUp(KeyCode k)
    {
#if ENABLE_LEGACY_INPUT_MANAGER
        return k != KeyCode.None && Input.GetKeyUp(k);
#else
        return false;
#endif
    }

    // ------------------------------------------------------------------ scheme

    internal static void SetScheme(ChuonInputScheme scheme)
    {
        if (s_scheme == scheme)
            return;
        s_scheme = scheme;
        try { SchemeChanged?.Invoke(); }
        catch (Exception e) { Debug.LogException(e); }
    }

    public static ChuonInputScheme SchemeForGamepad(Gamepad pad)
    {
        if (pad == null)
            return ChuonInputScheme.GenericGamepad;
        if (pad is UnityEngine.InputSystem.DualShock.DualShockGamepad)
            return ChuonInputScheme.PlayStation;
        if (pad is UnityEngine.InputSystem.XInput.XInputController)
            return ChuonInputScheme.Xbox;
        string product = pad.description.product ?? string.Empty;
        if (product.IndexOf("DualSense", StringComparison.OrdinalIgnoreCase) >= 0 || product.IndexOf("DUALSHOCK", StringComparison.OrdinalIgnoreCase) >= 0
            || product.IndexOf("Wireless Controller", StringComparison.OrdinalIgnoreCase) >= 0)
            return ChuonInputScheme.PlayStation;
        if (product.IndexOf("Xbox", StringComparison.OrdinalIgnoreCase) >= 0)
            return ChuonInputScheme.Xbox;
        return ChuonInputScheme.GenericGamepad;
    }

    // ------------------------------------------------------------------ overrides persistence

    [Serializable]
    sealed class OverrideEntry
    {
        public string map;
        public string action;
        public int index;
        public string path;
    }

    [Serializable]
    sealed class OverrideList
    {
        public int version = 1;
        public List<OverrideEntry> entries = new List<OverrideEntry>();
    }

    public static void SaveOverrides()
    {
        EnsureInitialized();
        var list = new OverrideList();
        CollectOverrides(s_kbMap, list);
        CollectOverrides(s_padMap, list);
        if (list.entries.Count == 0)
            PlayerPrefs.DeleteKey(OverridesPrefKey);
        else
            PlayerPrefs.SetString(OverridesPrefKey, JsonUtility.ToJson(list));
        PlayerPrefs.Save();
        RefreshOverrideFlags();
    }

    static void CollectOverrides(InputActionMap map, OverrideList list)
    {
        foreach (InputAction action in map.actions)
        {
            for (int b = 0; b < action.bindings.Count; b++)
            {
                InputBinding binding = action.bindings[b];
                if (binding.isComposite || string.IsNullOrEmpty(binding.overridePath) || binding.overridePath == binding.path)
                    continue;
                list.entries.Add(new OverrideEntry { map = map.name, action = action.name, index = b, path = binding.overridePath });
            }
        }
    }

    static void LoadOverrides()
    {
        string json = PlayerPrefs.GetString(OverridesPrefKey, string.Empty);
        if (!string.IsNullOrEmpty(json))
        {
            try
            {
                OverrideList list = JsonUtility.FromJson<OverrideList>(json);
                if (list != null && list.entries != null)
                {
                    for (int i = 0; i < list.entries.Count; i++)
                    {
                        OverrideEntry e = list.entries[i];
                        InputActionMap map = e.map == KeyboardMapName ? s_kbMap : (e.map == GamepadMapName ? s_padMap : null);
                        InputAction action = map?.FindAction(e.action);
                        if (action == null || e.index < 0 || e.index >= action.bindings.Count || action.bindings[e.index].isComposite)
                            continue;
                        action.ApplyBindingOverride(e.index, e.path);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[ChuonInput] 저장된 키 설정을 읽지 못해 기본값을 사용합니다: " + ex.Message);
            }
        }
        RefreshOverrideFlags();
    }

    /// <summary>저장된 사용자 키 설정을 다시 읽는다 (QA 자가 테스트 정리용).</summary>
    public static void ReloadOverridesFromPrefs()
    {
        EnsureInitialized();
        s_kbMap.RemoveAllBindingOverrides();
        s_padMap.RemoveAllBindingOverrides();
        LoadOverrides();
    }

    public static void ResetAllBindings()
    {
        EnsureInitialized();
        s_kbMap.RemoveAllBindingOverrides();
        s_padMap.RemoveAllBindingOverrides();
        PlayerPrefs.DeleteKey(OverridesPrefKey);
        PlayerPrefs.Save();
        RefreshOverrideFlags();
    }

    /// <summary>바인딩 변경 후 호출 (리바인드 완료/리셋). 키보드 오버라이드 플래그 갱신 + 이벤트.</summary>
    internal static void RefreshOverrideFlags()
    {
        for (int i = 0; i < ActionCount; i++)
        {
            bool overridden = false;
            InputAction kb = s_kb[i];
            if (kb != null)
            {
                for (int b = 0; b < kb.bindings.Count; b++)
                {
                    InputBinding binding = kb.bindings[b];
                    if (!binding.isComposite && !string.IsNullOrEmpty(binding.overridePath) && binding.overridePath != binding.path)
                    {
                        overridden = true;
                        break;
                    }
                }
            }
            s_kbOverridden[i] = overridden;
        }

        try { BindingsChanged?.Invoke(); }
        catch (Exception e) { Debug.LogException(e); }
    }

    /// <summary>표시용 바인딩 인덱스 (락온 키보드 0 = TAB). Move 는 호출부가 파트 인덱스를 직접 지정.</summary>
    public static int PrimaryBindingIndex(ChuonAction a, bool gamepad)
    {
        if (a == ChuonAction.Move)
            return gamepad ? 0 : MoveUpIndex;
        return 0;
    }
}
