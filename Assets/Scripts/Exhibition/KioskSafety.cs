// 파일명: KioskSafety.cs
// 공개 부스용 안전장치 (키오스크 모드에서만):
//  - 게임 종료 차단: Application.wantsToQuit (Alt+F4·창 닫기·모든 "게임 종료" 버튼). 스태프 패널의 종료만 허용.
//  - "게임 종료" 버튼 숨김 (타이틀·일시정지·사망·크레딧) + 패드 내비게이션 우회 연결.
//  - 해상도·전체화면·품질·VSync: 옵션 컨트롤 잠금 + 키오스크 시작 시점 값으로 되돌림, 전체화면 강제(-kioskAllowWindowed 제외).
//  - 볼륨 하한(마스터 35%, 채널 15%), 방문객 키 리바인딩은 세션 종료 시 원복.
//  - 커서 자동 숨김, 개발용 오버레이(전투 디버그 HUD·성능 진단·씬 단축키·F10 테스트 모드) 끄기.
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed class KioskSafety
{
    const float ScanInterval = 0.5f;
    const float DisplayCheckInterval = 1f;
    const float DisplaySettleSeconds = 2f;

    static readonly string[] QuitLabels =
    {
        "게임 종료", "게임종료", "종료", "QUIT", "QUIT GAME", "EXIT", "EXIT GAME", "바탕화면으로", "데스크톱으로",
    };

    /// <summary>스태프 패널 "게임 종료"가 한 번 허용한다.</summary>
    public static bool AllowQuit;

    bool _active;
    float _nextScanAt;
    float _nextDisplayCheckAt;
    float _displaySettleUntil;
    bool _lastPaused;

    // 디스플레이 스냅샷
    bool _displaySnapshot;
    int _snapW, _snapH, _snapHz;
    FullScreenMode _snapMode;
    int _snapQuality;
    int _snapVSync;

    // 키 설정 스냅샷
    string _bindingSnapshot;
    bool _bindingSnapshotTaken;

    // 커서
    bool _cursorHiddenByUs;
    float _mouseIdle;

    // 꺼 둔 개발용 오브젝트 (키오스크 끌 때 복원)
    readonly List<GameObject> _disabledDevObjects = new List<GameObject>();
    readonly List<Behaviour> _disabledDevBehaviours = new List<Behaviour>();
    readonly List<Button> _buttonBuffer = new List<Button>();
    readonly List<Button> _hiddenQuitButtons = new List<Button>();

    public bool Active => _active;

    public void Enable()
    {
        if (_active)
            return;
        _active = true;
        AllowQuit = false;
        Application.wantsToQuit -= HandleWantsToQuit;
        Application.wantsToQuit += HandleWantsToQuit;
        ClampVolumes();
        DisableDevOverlays();
        TakeBindingSnapshot();
        _displaySnapshot = false; // 다음 Tick 에서 현재 값으로 스냅샷
        _nextScanAt = 0f;
        _nextDisplayCheckAt = Time.unscaledTime + DisplaySettleSeconds;
    }

    public void Disable()
    {
        if (!_active)
            return;
        _active = false;
        Application.wantsToQuit -= HandleWantsToQuit;
        RestoreDevOverlays();
        if (_cursorHiddenByUs)
        {
            Cursor.visible = true;
            _cursorHiddenByUs = false;
        }
    }

    public void OnSceneLoaded()
    {
        if (!_active)
            return;
        _nextScanAt = 0f;
        DisableDevOverlays();
    }

    /// <summary>세션이 끝날 때 (타이틀 복귀): 방문객이 바꾼 키 설정을 원복.</summary>
    public void OnSessionEnded()
    {
        if (!_active)
            return;
        RestoreBindings();
        ClampVolumes();
    }

    public void Tick(float unscaledDt, bool mouseMoved)
    {
        if (!_active)
            return;

        float now = Time.unscaledTime;

        bool paused = PauseManager.Instance != null && PauseManager.Instance.IsPaused;
        if (paused != _lastPaused)
        {
            _lastPaused = paused;
            _nextScanAt = 0f; // 일시정지 메뉴가 열리면 즉시 종료 버튼 숨김
        }

        if (now >= _nextScanAt)
        {
            _nextScanAt = now + ScanInterval;
            Guard(HideQuitButtons);
            Guard(LockOptionControls);
        }

        if (now >= _nextDisplayCheckAt)
        {
            _nextDisplayCheckAt = now + DisplayCheckInterval;
            Guard(GuardDisplay);
        }

        UpdateCursor(unscaledDt, mouseMoved);
    }

    static void Guard(Action action)
    {
        try
        {
            action();
        }
        catch (Exception e)
        {
            Debug.LogWarning("[Kiosk] safety step failed: " + e.Message);
        }
    }

    // ------------------------------------------------------------------
    // 종료 차단
    // ------------------------------------------------------------------

    static bool HandleWantsToQuit()
    {
        if (AllowQuit || !KioskConfig.Enabled)
            return true;
        Debug.Log("[Kiosk] quit blocked (staff panel: Ctrl+Shift+K 3 s → 0)");
        return false;
    }

    void HideQuitButtons()
    {
        Button[] buttons = UnityEngine.Object.FindObjectsByType<Button>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        _buttonBuffer.Clear();
        for (int i = 0; i < buttons.Length; i++)
        {
            Button b = buttons[i];
            if (b != null && IsQuitButton(b))
                _buttonBuffer.Add(b);
        }

        // 이미 숨긴 버튼: 메뉴가 다시 열리며 내비게이션이 재구성됐을 수 있으니 매번 우회 연결
        for (int i = _hiddenQuitButtons.Count - 1; i >= 0; i--)
        {
            Button hidden = _hiddenQuitButtons[i];
            if (hidden == null)
                _hiddenQuitButtons.RemoveAt(i);
            else if (!hidden.gameObject.activeSelf)
                RedirectNavigation(buttons, hidden);
        }

        if (_buttonBuffer.Count == 0)
            return;

        bool onTitle = KioskConfig.IsTitleScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
        for (int i = 0; i < _buttonBuffer.Count; i++)
        {
            Button quit = _buttonBuffer[i];

            // 다른 선택지가 없는 화면(예: 클리어 후 크레딧 오버레이의 유일한 버튼)은 숨기지 않고 "타이틀로" 버튼으로 바꾼다
            if (!onTitle && !HasSiblingChoice(quit))
            {
                ConvertToTitleButton(quit);
                continue;
            }

            if (!_hiddenQuitButtons.Contains(quit))
                _hiddenQuitButtons.Add(quit);
            RedirectNavigation(buttons, quit);
            EventSystem es = EventSystem.current;
            if (es != null && es.currentSelectedGameObject == quit.gameObject)
            {
                Selectable fallback = quit.navigation.selectOnUp;
                if (fallback == null || !fallback.isActiveAndEnabled)
                    fallback = quit.navigation.selectOnDown;
                es.SetSelectedGameObject(fallback != null && fallback.isActiveAndEnabled ? fallback.gameObject : null);
            }
            quit.gameObject.SetActive(false);
        }
        _buttonBuffer.Clear();
    }

    static bool HasSiblingChoice(Button quit)
    {
        Transform parent = quit.transform.parent;
        if (parent == null)
            return false;
        Button[] siblings = parent.GetComponentsInChildren<Button>(false);
        for (int i = 0; i < siblings.Length; i++)
        {
            Button b = siblings[i];
            if (b != null && b != quit && b.isActiveAndEnabled && b.interactable && !IsQuitButton(b))
                return true;
        }
        return false;
    }

    /// <summary>종료 버튼을 "타이틀로"(세션 종료 → 타이틀)로 바꾼다. 원래 버튼이 다시 설정돼도 다음 스캔에서 다시 바꾼다.</summary>
    static void ConvertToTitleButton(Button quit)
    {
        quit.onClick.RemoveAllListeners();
        for (int p = 0; p < quit.onClick.GetPersistentEventCount(); p++)
            quit.onClick.SetPersistentListenerState(p, UnityEngine.Events.UnityEventCallState.Off);
        string reason = HierarchyContains(quit.transform, "credit") ? KioskResetReason.Completed : KioskResetReason.TitleMenu;
        quit.onClick.AddListener(() => KioskDirector.RequestResetToTitle(reason));

        Text text = quit.GetComponentInChildren<Text>(true);
        if (text != null && text.text != TitleLabel)
            text.text = TitleLabel;
        TMP_Text tmp = quit.GetComponentInChildren<TMP_Text>(true);
        if (tmp != null && tmp.text != TitleLabel)
            tmp.text = TitleLabel;
    }

    const string TitleLabel = "타이틀로";

    static bool HierarchyContains(Transform t, string token)
    {
        for (int depth = 0; t != null && depth < 8; depth++, t = t.parent)
        {
            if (t.name.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }
        return false;
    }

    static bool IsQuitButton(Button button)
    {
        string name = button.name;
        if (name.IndexOf("quit", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("exit", StringComparison.OrdinalIgnoreCase) >= 0)
            return true;

        string label = null;
        Text text = button.GetComponentInChildren<Text>(true);
        if (text != null)
            label = text.text;
        if (string.IsNullOrEmpty(label))
        {
            TMP_Text tmp = button.GetComponentInChildren<TMP_Text>(true);
            if (tmp != null)
                label = tmp.text;
        }
        if (string.IsNullOrEmpty(label))
            return false;

        label = StripRichText(label).Trim();
        for (int i = 0; i < QuitLabels.Length; i++)
        {
            if (string.Equals(label, QuitLabels[i], StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    static string StripRichText(string s)
    {
        if (s.IndexOf('<') < 0)
            return s;
        var sb = new System.Text.StringBuilder(s.Length);
        bool inTag = false;
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c == '<') { inTag = true; continue; }
            if (c == '>') { inTag = false; continue; }
            if (!inTag) sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>숨길 버튼을 가리키는 명시적 내비게이션을 그 버튼의 다음 대상으로 우회 (패드 순환 유지).</summary>
    static void RedirectNavigation(Button[] all, Button hidden)
    {
        Navigation hiddenNav = hidden.navigation;
        for (int i = 0; i < all.Length; i++)
        {
            Button other = all[i];
            if (other == null || other == hidden)
                continue;
            Navigation nav = other.navigation;
            if (nav.mode != Navigation.Mode.Explicit)
                continue;

            bool changed = false;
            if (nav.selectOnDown == hidden) { nav.selectOnDown = Skip(hiddenNav.selectOnDown, hidden); changed = true; }
            if (nav.selectOnUp == hidden) { nav.selectOnUp = Skip(hiddenNav.selectOnUp, hidden); changed = true; }
            if (nav.selectOnLeft == hidden) { nav.selectOnLeft = Skip(hiddenNav.selectOnLeft, hidden); changed = true; }
            if (nav.selectOnRight == hidden) { nav.selectOnRight = Skip(hiddenNav.selectOnRight, hidden); changed = true; }
            if (changed)
                other.navigation = nav;
        }
    }

    static Selectable Skip(Selectable next, Button hidden) => next == hidden ? null : next;

    // ------------------------------------------------------------------
    // 옵션 잠금
    // ------------------------------------------------------------------

    void LockOptionControls()
    {
        OptionsManagerAdvanced[] managers = UnityEngine.Object.FindObjectsByType<OptionsManagerAdvanced>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        KioskSettings s = KioskConfig.Settings;
        for (int i = 0; i < managers.Length; i++)
        {
            OptionsManagerAdvanced m = managers[i];
            if (m == null)
                continue;
            Lock(m.resolutionDropdown);
            Lock(m.fullscreenToggle);
            Lock(m.vSyncToggle);
            Lock(m.qualityDropdown);
            if (m.keybindButtons != null)
            {
                for (int k = 0; k < m.keybindButtons.Count; k++)
                    Lock(m.keybindButtons[k]);
            }
            ClampSlider(m.masterSlider, s.MinMasterVolume);
            ClampSlider(m.musicSlider, s.MinChannelVolume);
            ClampSlider(m.sfxSlider, s.MinChannelVolume);
            ClampSlider(m.voiceSlider, s.MinChannelVolume);
        }
    }

    static void Lock(Selectable selectable)
    {
        if (selectable != null && selectable.interactable)
            selectable.interactable = false;
    }

    static void ClampSlider(Slider slider, float min)
    {
        if (slider == null)
            return;
        if (slider.maxValue > 1.0001f)
            return; // 0–1 선형이 아닌 슬라이더는 건드리지 않는다
        if (slider.minValue < min)
            slider.minValue = min; // 현재 값이 낮으면 Slider 가 올려 주고 onValueChanged 로 믹서에 반영
    }

    void ClampVolumes()
    {
        KioskSettings s = KioskConfig.Settings;
        if (AudioOptionsRuntime.MasterVolume < s.MinMasterVolume)
        {
            AudioOptionsRuntime.SetMasterVolume(s.MinMasterVolume);
            PlayerPrefs.SetFloat("opt_master", s.MinMasterVolume);
        }
        if (AudioOptionsRuntime.MusicVolume < s.MinChannelVolume)
        {
            AudioOptionsRuntime.SetMusicVolume(s.MinChannelVolume);
            PlayerPrefs.SetFloat("opt_music", s.MinChannelVolume);
        }
        if (AudioOptionsRuntime.SfxVolume < s.MinChannelVolume)
        {
            AudioOptionsRuntime.SetSfxVolume(s.MinChannelVolume);
            PlayerPrefs.SetFloat("opt_sfx", s.MinChannelVolume);
        }
        if (AudioOptionsRuntime.VoiceVolume < s.MinChannelVolume)
        {
            AudioOptionsRuntime.SetVoiceVolume(s.MinChannelVolume);
            PlayerPrefs.SetFloat("opt_voice", s.MinChannelVolume);
        }
    }

    // ------------------------------------------------------------------
    // 디스플레이 (플레이어 빌드에서만)
    // ------------------------------------------------------------------

    void GuardDisplay()
    {
        if (Application.isEditor)
            return;

        if (!_displaySnapshot)
        {
            _displaySnapshot = true;
            _snapW = Screen.width;
            _snapH = Screen.height;
            _snapHz = DisplayModeRuntime.RefreshHz(Screen.currentResolution);
            _snapMode = Screen.fullScreenMode;
            if (!KioskConfig.Settings.AllowWindowed && _snapMode == FullScreenMode.Windowed)
            {
                _snapMode = FullScreenMode.FullScreenWindow;
                Resolution native = Screen.currentResolution;
                _snapW = native.width;
                _snapH = native.height;
            }
            _snapQuality = QualitySettings.GetQualityLevel();
            _snapVSync = QualitySettings.vSyncCount;
        }

        if (Time.unscaledTime < _displaySettleUntil)
            return;

        if (QualitySettings.GetQualityLevel() != _snapQuality)
        {
            QualitySettings.SetQualityLevel(_snapQuality, true);
            PlayerPrefs.SetInt("opt_quality_index", _snapQuality);
        }
        if (QualitySettings.vSyncCount != _snapVSync)
            QualitySettings.vSyncCount = _snapVSync;

        bool modeChanged = Screen.fullScreenMode != _snapMode;
        bool sizeChanged = Screen.width != _snapW || Screen.height != _snapH;
        if (!modeChanged && !sizeChanged)
            return;

        Debug.Log($"[Kiosk] display restored to {_snapW}x{_snapH} {_snapMode} (was {Screen.width}x{Screen.height} {Screen.fullScreenMode})");
        Screen.SetResolution(_snapW, _snapH, _snapMode);
        DisplayModeRuntime.Save(_snapW, _snapH, _snapHz, _snapMode != FullScreenMode.Windowed);
        _displaySettleUntil = Time.unscaledTime + DisplaySettleSeconds;
    }

    // ------------------------------------------------------------------
    // 키 설정
    // ------------------------------------------------------------------

    void TakeBindingSnapshot()
    {
        _bindingSnapshot = PlayerPrefs.GetString(ChuonInput.OverridesPrefKey, string.Empty);
        _bindingSnapshotTaken = true;
    }

    void RestoreBindings()
    {
        if (!_bindingSnapshotTaken || ChuonInputRebinding.IsRebinding)
            return;
        string current = PlayerPrefs.GetString(ChuonInput.OverridesPrefKey, string.Empty);
        if (string.Equals(current, _bindingSnapshot, StringComparison.Ordinal))
            return;

        Debug.Log("[Kiosk] visitor key bindings reverted to booth setup");
        if (string.IsNullOrEmpty(_bindingSnapshot))
        {
            ChuonInput.ResetAllBindings();
        }
        else
        {
            PlayerPrefs.SetString(ChuonInput.OverridesPrefKey, _bindingSnapshot);
            ChuonInput.ReloadOverridesFromPrefs();
        }
    }

    // ------------------------------------------------------------------
    // 커서
    // ------------------------------------------------------------------

    void UpdateCursor(float dt, bool mouseMoved)
    {
        if (mouseMoved)
            _mouseIdle = 0f;
        else
            _mouseIdle += dt;

        if (_cursorHiddenByUs)
        {
            if (Cursor.visible)
            {
                _cursorHiddenByUs = false; // 게임이 직접 다시 켰다 (메뉴 열림 등)
                _mouseIdle = 0f;
            }
            else if (mouseMoved)
            {
                Cursor.visible = true;
                _cursorHiddenByUs = false;
            }
            return;
        }

        float hideAfter = ChuonInput.IsGamepadScheme ? 0.5f : KioskConfig.Settings.CursorHideSeconds;
        if (Cursor.visible && Cursor.lockState != CursorLockMode.Locked && _mouseIdle >= hideAfter)
        {
            Cursor.visible = false;
            _cursorHiddenByUs = true;
        }
    }

    // ------------------------------------------------------------------
    // 개발용 오버레이
    // ------------------------------------------------------------------

    void DisableDevOverlays()
    {
        DisableAll<CombatDebugHudRuntime>();
        DisableAll<PerformanceDiagnosticsRuntime>();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        DisableAll<DevelopmentSceneHotkeys>();
        if (DevTestMode.Current != DevTestMode.Mode.Off)
            DevTestMode.SetMode(DevTestMode.Mode.Off);
        DevTestMode[] modes = UnityEngine.Object.FindObjectsByType<DevTestMode>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < modes.Length; i++)
        {
            if (modes[i] != null && modes[i].enabled)
            {
                modes[i].enabled = false; // F10 토글 끔 (오브젝트는 유지: 피해 배율 정적 상태만 사용)
                _disabledDevBehaviours.Add(modes[i]);
            }
        }
#endif
    }

    void DisableAll<T>() where T : Component
    {
        T[] found = UnityEngine.Object.FindObjectsByType<T>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < found.Length; i++)
        {
            if (found[i] == null)
                continue;
            GameObject go = found[i].gameObject;
            if (!go.activeSelf)
                continue;
            go.SetActive(false);
            _disabledDevObjects.Add(go);
        }
    }

    void RestoreDevOverlays()
    {
        for (int i = 0; i < _disabledDevObjects.Count; i++)
        {
            if (_disabledDevObjects[i] != null)
                _disabledDevObjects[i].SetActive(true);
        }
        _disabledDevObjects.Clear();

        for (int i = 0; i < _disabledDevBehaviours.Count; i++)
        {
            if (_disabledDevBehaviours[i] != null)
                _disabledDevBehaviours[i].enabled = true;
        }
        _disabledDevBehaviours.Clear();
    }
}
