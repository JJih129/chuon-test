// 입력 프롬프트 표기 서비스 (마지막 사용 장치 기준: 키보드·마우스 / Xbox / PlayStation)
// 사용법 (프롬프트 텍스트 생성부):
//   InputGlyphs.Get(ChuonAction.Dodge)      → "L-SHIFT" | "B" | "CIRCLE"  (현재 장치, 리바인드 반영)
//   InputGlyphs.GetTag(ChuonAction.Dodge)   → TMP 스프라이트 에셋이 등록돼 있으면 "<sprite name=\"xbox_b\">", 아니면 "[B]"
//   InputGlyphs.Changed += Refresh;         → 장치 전환 / 리바인드 시 다시 그리기 (OnDisable 에서 해제)
// 스프라이트 이름 규칙: kbm_<key> / xbox_<control> / ps_<control> (예: kbm_leftshift, xbox_buttoneast, ps_buttoneast).
using System;
using UnityEngine;
using UnityEngine.InputSystem;

public static class InputGlyphs
{
    /// <summary>선택: 버튼 아이콘 TMP 스프라이트 에셋. 비어 있으면 GetTag 는 [텍스트] 를 돌려준다.</summary>
    public static TMPro.TMP_SpriteAsset SpriteAsset;

    static bool s_hooked;
    static readonly string[] s_cache = new string[ChuonInput.ActionCount * 4];

    /// <summary>장치 전환 또는 바인딩 변경.</summary>
    public static event Action Changed
    {
        add { Hook(); s_changed += value; }
        remove { s_changed -= value; }
    }

    static event Action s_changed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        s_changed = null;
        s_hooked = false;
        SpriteAsset = null;
        System.Array.Clear(s_cache, 0, s_cache.Length);
    }

    static void Hook()
    {
        if (s_hooked)
            return;
        s_hooked = true;
        ChuonInput.SchemeChanged += Raise;
        ChuonInput.BindingsChanged += Raise;
    }

    static void Raise()
    {
        System.Array.Clear(s_cache, 0, s_cache.Length);
        try { s_changed?.Invoke(); }
        catch (Exception e) { Debug.LogException(e); }
    }

    public static ChuonInputScheme Scheme => ChuonInput.Scheme;
    public static bool IsGamepad => ChuonInput.IsGamepadScheme;

    /// <summary>현재 장치 기준 표기 텍스트.</summary>
    public static string Get(ChuonAction a) => Get(a, ChuonInput.Scheme);

    public static string Get(ChuonAction a, ChuonInputScheme scheme)
    {
        Hook(); // 장치/바인딩 변경 시 캐시 무효화 (HUD 가 매 프레임 불러도 할당 없음)
        int key = (int)a * 4 + (int)scheme;
        string cached = s_cache[key];
        if (cached != null)
            return cached;
        cached = scheme == ChuonInputScheme.KeyboardMouse ? KeyboardLabel(a) : GamepadLabel(a, scheme);
        s_cache[key] = cached;
        return cached;
    }

    /// <summary>TMP 리치텍스트 태그 (스프라이트가 있으면 아이콘, 없으면 [텍스트]).</summary>
    public static string GetTag(ChuonAction a)
    {
        ChuonInputScheme scheme = ChuonInput.Scheme;
        if (SpriteAsset != null)
        {
            string sprite = SpriteName(a, scheme);
            if (!string.IsNullOrEmpty(sprite) && SpriteAsset.GetSpriteIndexFromName(sprite) >= 0)
                return "<sprite name=\"" + sprite + "\">";
        }
        return "[" + Get(a, scheme) + "]";
    }

    /// <summary>
    /// 기존 하드코딩 프롬프트의 키 표기를 현재 장치/리바인드 표기로 바꾼다. 키보드·마우스 + 기본 키면 원문 그대로 반환 (KB+M 표시 불변).
    /// 바꾸는 형태: "[F]", 맨 앞 "F:" / "F " , "F 키" (예: ReplaceKeyToken("F: 상호작용", ChuonAction.Interact, "F") → 패드 "A: 상호작용").
    /// </summary>
    public static string ReplaceKeyToken(string text, ChuonAction a, string legacyToken)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(legacyToken))
            return text;
        if (!ChuonInput.IsGamepadScheme && !ChuonInput.KeyboardOverridden(a))
            return text;

        string label = Get(a);
        string keyWord = ChuonInput.IsGamepadScheme ? " 버튼" : " 키";
        string result = text.Replace("[" + legacyToken + "]", "[" + label + "]")
                            .Replace(legacyToken + " 키", label + keyWord);
        if (result.StartsWith(legacyToken + ":", StringComparison.Ordinal) || result.StartsWith(legacyToken + " ", StringComparison.Ordinal))
            result = label + result.Substring(legacyToken.Length);
        return result;
    }

    /// <summary>카메라 조작 표기 (리바인드 대상 아님).</summary>
    public static string LookLabel()
    {
        switch (ChuonInput.Scheme)
        {
            case ChuonInputScheme.KeyboardMouse: return "마우스";
            case ChuonInputScheme.PlayStation: return "R 스틱";
            default: return "R 스틱";
        }
    }

    public static string KeyboardLabel(ChuonAction a)
    {
        InputAction kb = ChuonInput.KeyboardAction(a);
        if (kb == null)
            return "-";
        if (a == ChuonAction.Move)
        {
            return PathLabel(kb.bindings[ChuonInput.MoveUpIndex].effectivePath, ChuonInputScheme.KeyboardMouse) + "/"
                 + PathLabel(kb.bindings[ChuonInput.MoveLeftIndex].effectivePath, ChuonInputScheme.KeyboardMouse) + "/"
                 + PathLabel(kb.bindings[ChuonInput.MoveDownIndex].effectivePath, ChuonInputScheme.KeyboardMouse) + "/"
                 + PathLabel(kb.bindings[ChuonInput.MoveRightIndex].effectivePath, ChuonInputScheme.KeyboardMouse);
        }

        string label = BindingLabel(kb, 0, ChuonInputScheme.KeyboardMouse);
        if (a == ChuonAction.LockOn && kb.bindings.Count > 1)
            label += " / " + BindingLabel(kb, 1, ChuonInputScheme.KeyboardMouse);
        return label;
    }

    public static string GamepadLabel(ChuonAction a, ChuonInputScheme scheme)
    {
        if (scheme == ChuonInputScheme.KeyboardMouse)
            scheme = ChuonInputScheme.Xbox;
        InputAction pad = ChuonInput.GamepadAction(a);
        return pad != null ? BindingLabel(pad, 0, scheme) : "-";
    }

    /// <summary>특정 바인딩 하나의 표기 (옵션 화면 리바인드 행).</summary>
    public static string BindingLabel(InputAction action, int bindingIndex, ChuonInputScheme scheme)
    {
        if (action == null || bindingIndex < 0 || bindingIndex >= action.bindings.Count)
            return "-";
        return PathLabel(action.bindings[bindingIndex].effectivePath, scheme);
    }

    public static string PathLabel(string path, ChuonInputScheme scheme)
    {
        if (string.IsNullOrEmpty(path))
            return "-";
        string control = ControlName(path);
        bool isGamepad = path.StartsWith("<Gamepad>", StringComparison.OrdinalIgnoreCase)
            || path.IndexOf("Gamepad", StringComparison.OrdinalIgnoreCase) >= 0
            || path.IndexOf("XInput", StringComparison.OrdinalIgnoreCase) >= 0
            || path.IndexOf("DualShock", StringComparison.OrdinalIgnoreCase) >= 0
            || path.IndexOf("DualSense", StringComparison.OrdinalIgnoreCase) >= 0;
        if (isGamepad)
            return PadControlLabel(control, scheme == ChuonInputScheme.PlayStation);

        if (path.IndexOf("Mouse", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            switch (control)
            {
                case "leftbutton": return "좌클릭";
                case "rightbutton": return "우클릭";
                case "middlebutton": return "휠클릭";
                case "forwardbutton": return "마우스 앞";
                case "backbutton": return "마우스 뒤";
            }
        }

        switch (control)
        {
            case "leftshift": return "L-SHIFT";
            case "rightshift": return "R-SHIFT";
            case "leftctrl": return "L-CTRL";
            case "rightctrl": return "R-CTRL";
            case "leftalt": return "L-ALT";
            case "rightalt": return "R-ALT";
            case "space": return "SPACE";
            case "tab": return "TAB";
            case "escape": return "ESC";
            case "enter": return "ENTER";
            case "backspace": return "BACKSPACE";
            case "capslock": return "CAPS";
            case "uparrow": return "UP";
            case "downarrow": return "DOWN";
            case "leftarrow": return "LEFT";
            case "rightarrow": return "RIGHT";
        }

        string readable = InputControlPath.ToHumanReadableString(path, InputControlPath.HumanReadableStringOptions.OmitDevice);
        return string.IsNullOrEmpty(readable) ? control.ToUpperInvariant() : readable.ToUpperInvariant();
    }

    static string PadControlLabel(string control, bool playStation)
    {
        switch (control)
        {
            case "buttonsouth": return playStation ? "CROSS" : "A";
            case "buttoneast": return playStation ? "CIRCLE" : "B";
            case "buttonwest": return playStation ? "SQUARE" : "X";
            case "buttonnorth": return playStation ? "TRIANGLE" : "Y";
            case "leftshoulder": return playStation ? "L1" : "LB";
            case "rightshoulder": return playStation ? "R1" : "RB";
            case "lefttrigger": return playStation ? "L2" : "LT";
            case "righttrigger": return playStation ? "R2" : "RT";
            case "leftstickpress": return playStation ? "L3" : "LS";
            case "rightstickpress": return playStation ? "R3" : "RS";
            case "start": return playStation ? "OPTIONS" : "MENU";
            case "select": return playStation ? "SHARE" : "VIEW";
            case "leftstick": return "L 스틱";
            case "rightstick": return "R 스틱";
            case "up": return "D-PAD UP";
            case "down": return "D-PAD DOWN";
            case "left": return "D-PAD LEFT";
            case "right": return "D-PAD RIGHT";
            case "dpad": return "D-PAD";
        }
        return control.ToUpperInvariant();
    }

    static string ControlName(string path)
    {
        int slash = path.LastIndexOf('/');
        string c = slash >= 0 ? path.Substring(slash + 1) : path;
        return c.ToLowerInvariant();
    }

    static string SpriteName(ChuonAction a, ChuonInputScheme scheme)
    {
        InputAction action = scheme == ChuonInputScheme.KeyboardMouse ? ChuonInput.KeyboardAction(a) : ChuonInput.GamepadAction(a);
        if (action == null || action.bindings.Count == 0)
            return null;
        int idx = a == ChuonAction.Move && scheme == ChuonInputScheme.KeyboardMouse ? ChuonInput.MoveUpIndex : 0;
        string control = ControlName(action.bindings[idx].effectivePath ?? string.Empty);
        string prefix = scheme == ChuonInputScheme.KeyboardMouse ? "kbm_" : (scheme == ChuonInputScheme.PlayStation ? "ps_" : "xbox_");
        return prefix + control;
    }
}
