// 입력 런타임 (DontDestroyOnLoad, ChuonInput.Bootstrap 이 생성)
//  1) 마지막 사용 장치 추적 → ChuonInput.Scheme (키보드·마우스 / Xbox / PlayStation)
//  2) 씬의 레거시 StandaloneInputModule → InputSystemUIInputModule(ChuonInput UI 맵) 교체: D-패드/스틱 메뉴 이동, A 확인, B 취소.
//     키보드 메뉴 키는 레거시와 같음 (방향키/WASD, Enter/Space, Esc). 이미 InputSystemUIInputModule 인 씬은 그대로.
//  3) 패드로 메뉴를 조작하는데 선택된 UI 가 없으면 가장 위 캔버스의 첫 버튼을 선택 (first-selected 보정)
//  4) 패드 사용 중 선택된 UI 위에 TSUKASA HUD 스타일 포커스 프레임 (시안 라인 + 앰버 코너). 키보드·마우스 사용 시 숨김 (기존 그대로).
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[DefaultExecutionOrder(-120)]
public sealed class ChuonInputRuntime : MonoBehaviour
{
    static ChuonInputRuntime s_instance;

    static readonly Color FocusLineColor = new Color(0.18f, 0.90f, 0.98f, 0.92f);
    static readonly Color FocusAccentColor = new Color(1f, 0.78f, 0.36f, 1f);
    const float FocusPadding = 6f;
    const float FocusLine = 2f;
    const float MouseSwitchPixels = 6f;

    RectTransform _focusRoot;
    Image[] _focusLines;
    Image _focusCornerA, _focusCornerB;
    Canvas _focusCanvas;
    float _nextUpgradeCheckAt;

    /// <summary>QA: 레거시 모듈을 교체한 횟수.</summary>
    public static int UpgradedModules { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        s_instance = null;
        UpgradedModules = 0;
    }

    public static ChuonInputRuntime Ensure()
    {
        if (s_instance != null)
            return s_instance;
        if (!Application.isPlaying)
            return null;
        var go = new GameObject("__ChuonInputRuntime");
        DontDestroyOnLoad(go);
        s_instance = go.AddComponent<ChuonInputRuntime>();
        return s_instance;
    }

    void OnEnable()
    {
        SceneManager.sceneLoaded += HandleSceneLoaded;
        _nextUpgradeCheckAt = 0f;
    }

    void OnDisable()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
    }

    void OnDestroy()
    {
        if (s_instance == this)
            s_instance = null;
    }

    void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        _nextUpgradeCheckAt = 0f;
    }

    void Update()
    {
        DetectScheme();

        float now = Time.unscaledTime;
        if (now >= _nextUpgradeCheckAt)
        {
            _nextUpgradeCheckAt = now + 1f;
            UpgradeLegacyInputModule(EventSystem.current);
        }
    }

    void LateUpdate()
    {
        // 반복 14A: 자동 선택은 LateUpdate 에서 (EventSystem 이 이번 프레임 입력을 처리한 뒤).
        // 선택이 없던 화면의 첫 패드 입력은 "포커스 표시"만 한다 — 같은 입력의 ↓/A 가 곧바로 한 칸 옮기거나
        // 시작 버튼을 눌러 게임을 시작하지 않는다 (타이틀 첫 ↓ 가 시작을 건너뛰던 문제).
        if (ChuonInput.IsGamepadScheme)
            EnsureGamepadSelection();
        UpdateFocusFrame();
    }

    // ------------------------------------------------------------------ device tracking

    void DetectScheme()
    {
        Gamepad pad = Gamepad.current;
        if (pad != null && GamepadActuated(pad))
        {
            ChuonInput.SetScheme(ChuonInput.SchemeForGamepad(pad));
            return;
        }

        Keyboard kb = Keyboard.current;
        if (kb != null && kb.anyKey.wasPressedThisFrame)
        {
            ChuonInput.SetScheme(ChuonInputScheme.KeyboardMouse);
            return;
        }

        Mouse mouse = Mouse.current;
        if (mouse != null && ChuonInput.IsGamepadScheme)
        {
            if (mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame || mouse.middleButton.wasPressedThisFrame
                || mouse.delta.ReadValue().sqrMagnitude > MouseSwitchPixels * MouseSwitchPixels
                || Mathf.Abs(mouse.scroll.ReadValue().y) > 0.01f)
                ChuonInput.SetScheme(ChuonInputScheme.KeyboardMouse);
        }
    }

    static bool GamepadActuated(Gamepad pad)
    {
        if (pad.buttonSouth.wasPressedThisFrame || pad.buttonEast.wasPressedThisFrame || pad.buttonWest.wasPressedThisFrame
            || pad.buttonNorth.wasPressedThisFrame || pad.leftShoulder.wasPressedThisFrame || pad.rightShoulder.wasPressedThisFrame
            || pad.leftTrigger.wasPressedThisFrame || pad.rightTrigger.wasPressedThisFrame || pad.startButton.wasPressedThisFrame
            || pad.selectButton.wasPressedThisFrame || pad.leftStickButton.wasPressedThisFrame || pad.rightStickButton.wasPressedThisFrame
            || pad.dpad.up.wasPressedThisFrame || pad.dpad.down.wasPressedThisFrame || pad.dpad.left.wasPressedThisFrame || pad.dpad.right.wasPressedThisFrame)
            return true;
        return pad.leftStick.ReadValue().sqrMagnitude > 0.25f || pad.rightStick.ReadValue().sqrMagnitude > 0.25f;
    }

    static bool AnyButtonHeld()
    {
        Mouse mouse = Mouse.current;
        if (mouse != null && (mouse.leftButton.isPressed || mouse.rightButton.isPressed || mouse.middleButton.isPressed))
            return true;
        Gamepad pad = Gamepad.current;
        if (pad != null && (pad.buttonSouth.isPressed || pad.buttonEast.isPressed || pad.buttonWest.isPressed || pad.buttonNorth.isPressed
            || pad.leftShoulder.isPressed || pad.rightShoulder.isPressed || pad.leftTrigger.isPressed || pad.rightTrigger.isPressed
            || pad.startButton.isPressed || pad.selectButton.isPressed))
            return true;
        Keyboard kb = Keyboard.current;
        return kb != null && (kb.enterKey.isPressed || kb.spaceKey.isPressed || kb.numpadEnterKey.isPressed);
    }

    /// <summary>눌려 있는 버튼이 모두 떨어진 뒤(최대 1초) 실행 (리바인드 시작용).</summary>
    public void RunWhenButtonsReleased(Action action)
    {
        StartCoroutine(CoRunWhenReleased(action));
    }

    IEnumerator CoRunWhenReleased(Action action)
    {
        float until = Time.unscaledTime + 1f;
        yield return null;
        while (AnyButtonHeld() && Time.unscaledTime < until)
            yield return null;
        action?.Invoke();
    }

    // ------------------------------------------------------------------ UI input module

    /// <summary>레거시 StandaloneInputModule 을 Input System UI 모듈로 교체 (패드 D-패드 지원). 이미 신형이면 아무것도 안 한다.</summary>
    public static void UpgradeLegacyInputModule(EventSystem eventSystem)
    {
        if (eventSystem == null)
            return;
        if (eventSystem.GetComponent<InputSystemUIInputModule>() != null)
            return;
        StandaloneInputModule legacy = eventSystem.GetComponent<StandaloneInputModule>();
        if (legacy == null)
            return;

        legacy.enabled = false;
        InputSystemUIInputModule module = eventSystem.gameObject.AddComponent<InputSystemUIInputModule>();
        ConfigureModule(module);
        // StandaloneInputModule 은 BaseInputModule 이라 EventSystem 이 둘 중 하나만 쓴다 → 레거시는 제거
        UnityEngine.Object.Destroy(legacy);
        UpgradedModules++;
    }

    public static void ConfigureModule(InputSystemUIInputModule module)
    {
        if (module == null)
            return;
        InputActionMap ui = ChuonInput.UiMap;
        if (ui == null)
            return;
        module.actionsAsset = ChuonInput.Asset;
        module.point = InputActionReference.Create(ui.FindAction("Point"));
        module.leftClick = InputActionReference.Create(ui.FindAction("Click"));
        module.rightClick = InputActionReference.Create(ui.FindAction("RightClick"));
        module.middleClick = InputActionReference.Create(ui.FindAction("MiddleClick"));
        module.scrollWheel = InputActionReference.Create(ui.FindAction("ScrollWheel"));
        module.move = InputActionReference.Create(ui.FindAction("Navigate"));
        module.submit = InputActionReference.Create(ui.FindAction("Submit"));
        module.cancel = InputActionReference.Create(ui.FindAction("Cancel"));
        module.moveRepeatDelay = 0.4f;
        module.moveRepeatRate = 0.12f;
    }

    // ------------------------------------------------------------------ first-selected fallback

    void EnsureGamepadSelection()
    {
        EventSystem es = EventSystem.current;
        if (es == null || ChuonInputRebinding.IsRebinding)
            return;
        // 게임플레이 중(커서 잠김)에는 UI 선택을 건드리지 않는다
        if (Cursor.lockState == CursorLockMode.Locked && !Cursor.visible)
            return;

        GameObject selected = es.currentSelectedGameObject;
        if (IsUsableSelection(selected))
            return;

        Selectable best = FindBestSelectable();
        if (best == null)
            return;
        es.SetSelectedGameObject(best.gameObject);
    }

    static bool IsUsableSelection(GameObject go)
    {
        if (go == null || !go.activeInHierarchy)
            return false;
        Selectable s = go.GetComponent<Selectable>();
        if (s == null)
            return true; // 선택 가능한 컴포넌트가 아니어도 스크립트가 일부러 고른 대상은 존중
        return s.IsInteractable() && IsVisible(s);
    }

    static readonly List<CanvasGroup> s_groups = new List<CanvasGroup>(8);

    static bool IsVisible(Selectable s)
    {
        if (s == null || !s.isActiveAndEnabled)
            return false;
        Canvas canvas = s.GetComponentInParent<Canvas>();
        if (canvas == null || !canvas.isActiveAndEnabled)
            return false;
        s.GetComponentsInParent(false, s_groups);
        for (int i = 0; i < s_groups.Count; i++)
        {
            CanvasGroup g = s_groups[i];
            if (g == null || !g.enabled)
                continue;
            // 반복 14A: blocksRaycasts 는 보지 않는다 (마우스 전용 차단 — 일시정지 메뉴는 레이캐스트를 막고 직접 클릭 처리)
            if (g.alpha <= 0.01f || !g.interactable)
            {
                s_groups.Clear();
                return false;
            }
            if (g.ignoreParentGroups)
                break;
        }
        s_groups.Clear();
        Graphic graphic = s.targetGraphic;
        return graphic == null || graphic.isActiveAndEnabled;
    }

    /// <summary>모달 캔버스(있으면) 안에서, 가장 위 캔버스의 화면 좌상단에 가까운 버튼.</summary>
    public static Selectable FindBestSelectable()
    {
        Selectable best = null;
        int bestOrder = int.MinValue;
        float bestScore = float.MaxValue;
        Selectable[] all = Selectable.allSelectablesArray;
        for (int i = 0; i < all.Length; i++)
        {
            Selectable s = all[i];
            if (s == null || !s.IsInteractable() || s.navigation.mode == Navigation.Mode.None || !IsVisible(s))
                continue;
            if (s is Scrollbar)
                continue;
            Canvas canvas = s.GetComponentInParent<Canvas>();
            Canvas root = canvas != null ? canvas.rootCanvas : null;
            if (root == null)
                continue;
            if (RuntimeUiInputUtilityHasModal() && !RuntimeUiInputUtility.IsModalCanvas(root))
                continue;

            RectTransform rt = s.transform as RectTransform;
            if (rt == null)
                continue;
            Vector2 screen = RectTransformUtility.WorldToScreenPoint(root.renderMode == RenderMode.ScreenSpaceOverlay ? null : root.worldCamera, rt.position);
            if (screen.x < 0f || screen.y < 0f || screen.x > Screen.width || screen.y > Screen.height)
                continue;

            int order = root.sortingOrder;
            float score = (Screen.height - screen.y) * 4f + screen.x; // 위쪽 우선, 그다음 왼쪽
            if (order > bestOrder || (order == bestOrder && score < bestScore))
            {
                best = s;
                bestOrder = order;
                bestScore = score;
            }
        }
        return best;
    }

    static bool RuntimeUiInputUtilityHasModal()
    {
        return RuntimeUiInputUtility.HasActiveModal;
    }

    /// <summary>메뉴를 열 때 호출: 패드 사용 중이면 지정 버튼(없으면 자동)을 선택한다. 마우스 사용 중에는 기존처럼 아무것도 선택하지 않는다.</summary>
    public static void SelectForGamepad(GameObject preferred)
    {
        EventSystem es = EventSystem.current;
        if (es == null || !ChuonInput.IsGamepadScheme)
            return;
        if (preferred != null && preferred.activeInHierarchy)
        {
            es.SetSelectedGameObject(preferred);
            return;
        }
        Selectable best = FindBestSelectable();
        if (best != null)
            es.SetSelectedGameObject(best.gameObject);
    }

    // ------------------------------------------------------------------ focus frame

    void EnsureFocusFrame()
    {
        if (_focusRoot != null)
            return;
        var canvasGo = new GameObject("FocusCanvas", typeof(RectTransform), typeof(Canvas));
        canvasGo.transform.SetParent(transform, false);
        _focusCanvas = canvasGo.GetComponent<Canvas>();
        _focusCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _focusCanvas.sortingOrder = 32000;
        _focusCanvas.overrideSorting = true;

        var root = new GameObject("Focus", typeof(RectTransform));
        _focusRoot = root.GetComponent<RectTransform>();
        _focusRoot.SetParent(canvasGo.transform, false);
        _focusRoot.anchorMin = _focusRoot.anchorMax = Vector2.zero;
        _focusRoot.pivot = Vector2.zero;

        _focusLines = new Image[4];
        for (int i = 0; i < 4; i++)
            _focusLines[i] = CreateLine("Edge" + i, FocusLineColor);
        _focusCornerA = CreateLine("CornerA", FocusAccentColor);
        _focusCornerB = CreateLine("CornerB", FocusAccentColor);
        root.SetActive(false);
    }

    Image CreateLine(string name, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(_focusRoot, false);
        rt.pivot = Vector2.zero;
        Image img = go.GetComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
        return img;
    }

    static readonly Vector3[] s_corners = new Vector3[4];

    void UpdateFocusFrame()
    {
        GameObject selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        bool show = ChuonInput.IsGamepadScheme && selected != null && selected.activeInHierarchy;
        RectTransform target = show ? selected.transform as RectTransform : null;
        Canvas canvas = target != null ? target.GetComponentInParent<Canvas>() : null;
        if (target == null || canvas == null || !canvas.isActiveAndEnabled)
            show = false;
        if (show)
        {
            Selectable s = selected.GetComponent<Selectable>();
            if (s != null && !IsVisible(s))
                show = false;
        }

        if (!show)
        {
            if (_focusRoot != null && _focusRoot.gameObject.activeSelf)
                _focusRoot.gameObject.SetActive(false);
            return;
        }

        EnsureFocusFrame();
        Canvas root = canvas.rootCanvas;
        Camera cam = root.renderMode == RenderMode.ScreenSpaceOverlay ? null : root.worldCamera;
        target.GetWorldCorners(s_corners);
        Vector2 min = new Vector2(float.MaxValue, float.MaxValue);
        Vector2 max = new Vector2(float.MinValue, float.MinValue);
        for (int i = 0; i < 4; i++)
        {
            Vector2 p = RectTransformUtility.WorldToScreenPoint(cam, s_corners[i]);
            min = Vector2.Min(min, p);
            max = Vector2.Max(max, p);
        }
        min -= Vector2.one * FocusPadding;
        max += Vector2.one * FocusPadding;
        Vector2 size = max - min;
        if (size.x < 4f || size.y < 4f)
        {
            _focusRoot.gameObject.SetActive(false);
            return;
        }

        if (!_focusRoot.gameObject.activeSelf)
            _focusRoot.gameObject.SetActive(true);
        float scale = _focusCanvas.scaleFactor > 0f ? _focusCanvas.scaleFactor : 1f;
        _focusRoot.anchoredPosition = min / scale;
        size /= scale;
        _focusRoot.sizeDelta = size;

        float pulse = 0.75f + 0.25f * Mathf.Sin(Time.unscaledTime * 5f);
        SetLine(_focusLines[0], 0f, 0f, size.x, FocusLine, FocusLineColor, pulse);              // 아래
        SetLine(_focusLines[1], 0f, size.y - FocusLine, size.x, FocusLine, FocusLineColor, pulse); // 위
        SetLine(_focusLines[2], 0f, 0f, FocusLine, size.y, FocusLineColor, pulse);              // 왼
        SetLine(_focusLines[3], size.x - FocusLine, 0f, FocusLine, size.y, FocusLineColor, pulse); // 오른
        float accent = Mathf.Min(28f, size.x * 0.3f);
        SetLine(_focusCornerA, -2f, size.y - 2f, accent, 4f, FocusAccentColor, 1f);               // 좌상단 앰버
        SetLine(_focusCornerB, -2f, size.y - accent + 2f, 4f, accent, FocusAccentColor, 1f);
    }

    static void SetLine(Image img, float x, float y, float w, float h, Color color, float alphaScale)
    {
        RectTransform rt = img.rectTransform;
        rt.anchorMin = rt.anchorMax = Vector2.zero;
        rt.anchoredPosition = new Vector2(x, y);
        rt.sizeDelta = new Vector2(w, h);
        color.a *= alphaScale;
        img.color = color;
    }
}
