// OptionsManagerAdvanced.cs
// 비디오/오디오/입력 옵션매니저 (경고 대응된 전체 파일)
// (파일 본문은 업로드된 버전과 동일하되 refreshRate 사용을 피하도록 작성)

#pragma warning disable CS0618

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Audio;

[DisallowMultipleComponent]
public class OptionsManagerAdvanced : MonoBehaviour
{
    const float GeneratedRowSpacing = 56f;

    bool _loggedMissingMixerWarning;

    [Header("▶ 비디오 설정")]
    public Dropdown resolutionDropdown;
    public Toggle fullscreenToggle;
    public Toggle vSyncToggle;
    public Slider brightnessSlider;
    public Toggle motionBlurToggle;
    public Dropdown qualityDropdown;

    Resolution[] _resolutions;

    [Header("▶ 오디오 설정")]
    public AudioMixer audioMixer;
    public Slider masterSlider;
    public Slider musicSlider;
    public Slider sfxSlider;
    public Slider voiceSlider;
    public string paramMaster = "MasterVol";
    public string paramMusic = "MusicVol";
    public string paramSfx = "SfxVol";
    public string paramVoice = "VoiceVol";

    [Header("▶ 키 바인드")]
    public List<Button> keybindButtons = new List<Button>();
    Dictionary<string, KeyCode> _keybinds = new Dictionary<string, KeyCode>();
    Button _currentRebindButton = null;
    bool _waitingForKey = false;

    [Header("▶ 접근성")]
    public Slider subtitleSizeSlider;
    public Toggle subtitleBackgroundToggle;
    public Slider cameraShakeSlider;

    public Action<float> OnBrightnessChanged;
    public Action<bool> OnMotionBlurToggled;
    public Action<float> OnSubtitleSizeChanged;
    public Action<bool> OnSubtitleBgToggled;
    public Action<float> OnCameraShakeStrengthChanged;

    const string K_RES = "opt_resolution_index";
    const string K_FULLSCREEN = "opt_fullscreen";
    const string K_VSYNC = "opt_vsync";
    const string K_BRIGHT = "opt_brightness";
    const string K_MBLUR = "opt_motionblur";
    const string K_MASTER = "opt_master";
    const string K_MUSIC = "opt_music";
    const string K_SFX = "opt_sfx";
    const string K_VOICE = "opt_voice";
    const string K_SUB_SIZE = "opt_sub_size";
    const string K_SUB_BG = "opt_sub_bg";
    const string K_CAM_SHAKE = "opt_cam_shake";

    public bool IsWaitingForRebind => _waitingForKey;
    public Button CurrentRebindButton => _currentRebindButton;

    public void CancelRebind()
    {
        if (!_waitingForKey)
            return;

        if (_currentRebindButton != null)
        {
            string keyName = GetPrefKeyForButton(_currentRebindButton);
            string saved = PlayerPrefs.GetString(keyName, string.Empty);
            var label = _currentRebindButton.GetComponentInChildren<Text>();
            if (label != null)
                label.text = string.IsNullOrEmpty(saved) ? keyName.Replace("Key_", string.Empty) : saved;
        }

        _waitingForKey = false;
        _currentRebindButton = null;
        enabled = false;
    }

    void Awake()
    {
        AudioOptionsRuntime.RefreshFromPrefs();
        VideoOptionsRuntime.RefreshFromPrefs();
        FramePacingRuntime.RefreshFromPrefs();

        OnBrightnessChanged -= VideoOptionsRuntime.SetBrightness;
        OnBrightnessChanged += VideoOptionsRuntime.SetBrightness;
        OnMotionBlurToggled -= VideoOptionsRuntime.SetMotionBlurEnabled;
        OnMotionBlurToggled += VideoOptionsRuntime.SetMotionBlurEnabled;

        if (OnCameraShakeStrengthChanged == null)
            OnCameraShakeStrengthChanged += CameraShake.SetGlobalStrength;

        EnsureRuntimeOptionRows();

        _resolutions = Screen.resolutions;
        if (resolutionDropdown != null) resolutionDropdown.options.Clear();
        int currentIndex = 0;
        for (int i = 0; i < _resolutions.Length; i++)
        {
            var r = _resolutions[i];

            int displayHz = 0;
            try
            {
                // refreshRateRatio 사용 시도
                displayHz = (r.refreshRateRatio.denominator != 0)
                    ? Mathf.RoundToInt((float)r.refreshRateRatio.numerator / r.refreshRateRatio.denominator)
                    : 0;
            }
            catch
            {
                // 안전 fallback
                displayHz = Screen.currentResolution.refreshRate;
            }

            string txt = r.width + " x " + r.height + " @ " + displayHz + "Hz";
            if (resolutionDropdown != null) resolutionDropdown.options.Add(new Dropdown.OptionData(txt));

            try
            {
                var cur = Screen.currentResolution;
                bool sameRes = r.width == cur.width && r.height == cur.height &&
                                r.refreshRateRatio.numerator == cur.refreshRateRatio.numerator &&
                                r.refreshRateRatio.denominator == cur.refreshRateRatio.denominator;
                if (sameRes) currentIndex = i;
            }
            catch
            {
                if (r.width == Screen.currentResolution.width && r.height == Screen.currentResolution.height) currentIndex = i;
            }
        }

        if (resolutionDropdown != null)
        {
            resolutionDropdown.onValueChanged.RemoveAllListeners();
            resolutionDropdown.onValueChanged.AddListener(OnResolutionChanged);
            resolutionDropdown.value = PlayerPrefs.GetInt(K_RES, currentIndex);
            resolutionDropdown.RefreshShownValue();
        }

        if (fullscreenToggle != null)
        {
            fullscreenToggle.onValueChanged.RemoveAllListeners();
            fullscreenToggle.onValueChanged.AddListener(OnFullscreenToggled);
            fullscreenToggle.isOn = PlayerPrefs.GetInt(K_FULLSCREEN, Screen.fullScreen ? 1 : 0) == 1;
        }

        if (vSyncToggle != null)
        {
            vSyncToggle.onValueChanged.RemoveAllListeners();
            vSyncToggle.onValueChanged.AddListener(OnVSyncToggled);
            vSyncToggle.isOn = PlayerPrefs.GetInt(K_VSYNC, 0) == 1;
        }

        if (brightnessSlider != null)
        {
            brightnessSlider.onValueChanged.RemoveAllListeners();
            brightnessSlider.onValueChanged.AddListener(v => { PlayerPrefs.SetFloat(K_BRIGHT, v); OnBrightnessChanged?.Invoke(v); });
            brightnessSlider.value = PlayerPrefs.GetFloat(K_BRIGHT, 1f);
        }

        if (motionBlurToggle != null)
        {
            motionBlurToggle.onValueChanged.RemoveAllListeners();
            motionBlurToggle.onValueChanged.AddListener(v => { PlayerPrefs.SetInt(K_MBLUR, v ? 1 : 0); OnMotionBlurToggled?.Invoke(v); });
            motionBlurToggle.isOn = PlayerPrefs.GetInt(K_MBLUR, 0) == 1;
        }

        if (masterSlider != null) { masterSlider.onValueChanged.RemoveAllListeners(); masterSlider.onValueChanged.AddListener(v => SetAudioParam(paramMaster, v)); masterSlider.value = PlayerPrefs.GetFloat(K_MASTER, 1f); }
        if (musicSlider != null)  { musicSlider.onValueChanged.RemoveAllListeners(); musicSlider.onValueChanged.AddListener(v => SetAudioParam(paramMusic, v)); musicSlider.value = PlayerPrefs.GetFloat(K_MUSIC, 1f); }
        if (sfxSlider != null)    { sfxSlider.onValueChanged.RemoveAllListeners(); sfxSlider.onValueChanged.AddListener(v => SetAudioParam(paramSfx, v)); sfxSlider.value = PlayerPrefs.GetFloat(K_SFX, 1f); }
        if (voiceSlider != null)  { voiceSlider.onValueChanged.RemoveAllListeners(); voiceSlider.onValueChanged.AddListener(v => SetAudioParam(paramVoice, v)); voiceSlider.value = PlayerPrefs.GetFloat(K_VOICE, 1f); }

        if (subtitleSizeSlider != null) { subtitleSizeSlider.onValueChanged.RemoveAllListeners(); subtitleSizeSlider.onValueChanged.AddListener(v => { PlayerPrefs.SetFloat(K_SUB_SIZE, v); OnSubtitleSizeChanged?.Invoke(v); }); subtitleSizeSlider.value = PlayerPrefs.GetFloat(K_SUB_SIZE, 1f); }
        if (subtitleBackgroundToggle != null) { subtitleBackgroundToggle.onValueChanged.RemoveAllListeners(); subtitleBackgroundToggle.onValueChanged.AddListener(v => { PlayerPrefs.SetInt(K_SUB_BG, v?1:0); OnSubtitleBgToggled?.Invoke(v); }); subtitleBackgroundToggle.isOn = PlayerPrefs.GetInt(K_SUB_BG, 1) == 1; }
        if (cameraShakeSlider != null) { cameraShakeSlider.onValueChanged.RemoveAllListeners(); cameraShakeSlider.onValueChanged.AddListener(v => { PlayerPrefs.SetFloat(K_CAM_SHAKE, v); OnCameraShakeStrengthChanged?.Invoke(v); }); cameraShakeSlider.value = PlayerPrefs.GetFloat(K_CAM_SHAKE, 1f); }

        foreach (var b in keybindButtons)
        {
            var keyName = GetPrefKeyForButton(b);
            string saved = PlayerPrefs.GetString(keyName, "");
            var label = b.GetComponentInChildren<Text>();
            if (!string.IsNullOrEmpty(saved) && label != null) label.text = saved;
            b.onClick.RemoveAllListeners();
            b.onClick.AddListener(() => StartRebind(b));
        }

        if (qualityDropdown != null)
        {
            qualityDropdown.onValueChanged.RemoveAllListeners();
            qualityDropdown.value = PlayerPrefs.GetInt("opt_quality_index", QualitySettings.GetQualityLevel());
            qualityDropdown.RefreshShownValue();
            qualityDropdown.onValueChanged.AddListener(i => { QualitySettings.SetQualityLevel(i, true); PlayerPrefs.SetInt("opt_quality_index", i); });
        }

        ApplyAudioInitial();
        ApplyRuntimeAvailabilityState();
    }

    void Start()
    {
        int idx = PlayerPrefs.GetInt(K_RES, resolutionDropdown != null ? resolutionDropdown.value : 0);
        bool fs = PlayerPrefs.GetInt(K_FULLSCREEN, Screen.fullScreen ? 1 : 0) == 1;
        ApplyResolutionIndex(idx, fs);
        bool vs = PlayerPrefs.GetInt(K_VSYNC, 0) == 1;
        QualitySettings.vSyncCount = vs ? 1 : 0;
        FramePacingRuntime.RefreshFromPrefs();
        enabled = _waitingForKey;
    }

    void OnResolutionChanged(int idx)
    {
        PlayerPrefs.SetInt(K_RES, idx);
        bool fs = (fullscreenToggle != null) ? fullscreenToggle.isOn : Screen.fullScreen;
        ApplyResolutionIndex(idx, fs);
    }

    void OnFullscreenToggled(bool v)
    {
        PlayerPrefs.SetInt(K_FULLSCREEN, v ? 1 : 0);
        ApplyResolutionIndex(resolutionDropdown != null ? resolutionDropdown.value : 0, v);
    }

    void OnVSyncToggled(bool v)
    {
        PlayerPrefs.SetInt(K_VSYNC, v ? 1 : 0);
        QualitySettings.vSyncCount = v ? 1 : 0;
        FramePacingRuntime.RefreshFromPrefs();
    }

    void ApplyResolutionIndex(int idx, bool fullscreen)
    {
        if (_resolutions == null || _resolutions.Length == 0) return;
        idx = Mathf.Clamp(idx, 0, _resolutions.Length - 1);
        var r = _resolutions[idx];

        FullScreenMode mode = fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;

        try
        {
            // Unity 최신 API 사용 시도
            Screen.SetResolution(r.width, r.height, mode, r.refreshRateRatio);
        }
        catch
        {
            // fallback: legacy overload
            int refresh = 0;
            try
            {
                if (r.refreshRateRatio.denominator != 0)
                    refresh = Mathf.RoundToInt((float)r.refreshRateRatio.numerator / r.refreshRateRatio.denominator);
            }
            catch { refresh = Screen.currentResolution.refreshRate; }

            Screen.SetResolution(r.width, r.height, fullscreen, refresh);
        }
    }

    void SetAudioParam(string exposedName, float linear01)
    {
        linear01 = Mathf.Clamp01(linear01);
        ApplyRuntimeAudioState(exposedName, linear01);

        if (audioMixer == null)
        {
            ApplyAudioFallback(exposedName, linear01);
            return;
        }

        float dB = (linear01 <= 0.0001f) ? -80f : 20f * Mathf.Log10(Mathf.Clamp(linear01, 0.0001f, 1f));
        audioMixer.SetFloat(exposedName, dB);
        SaveAudioPref(exposedName, linear01);
    }

    void ApplyAudioInitial()
    {
        SetAudioParam(paramMaster, PlayerPrefs.GetFloat(K_MASTER, 1f));
        SetAudioParam(paramMusic,  PlayerPrefs.GetFloat(K_MUSIC, 1f));
        SetAudioParam(paramSfx,    PlayerPrefs.GetFloat(K_SFX, 1f));
        SetAudioParam(paramVoice,  PlayerPrefs.GetFloat(K_VOICE, 1f));
    }

    void ApplyAudioFallback(string exposedName, float linear01)
    {
        SaveAudioPref(exposedName, linear01);

        if (exposedName == paramMaster)
        {
            return;
        }

        if (_loggedMissingMixerWarning)
            return;

        _loggedMissingMixerWarning = true;
        Debug.Log("[OptionsManagerAdvanced] AudioMixer is not assigned. Only master volume is applied at runtime via AudioListener.volume.");
    }

    void SaveAudioPref(string exposedName, float linear01)
    {
        if (exposedName == paramMaster) PlayerPrefs.SetFloat(K_MASTER, linear01);
        if (exposedName == paramMusic)  PlayerPrefs.SetFloat(K_MUSIC, linear01);
        if (exposedName == paramSfx)    PlayerPrefs.SetFloat(K_SFX, linear01);
        if (exposedName == paramVoice)  PlayerPrefs.SetFloat(K_VOICE, linear01);
    }

    void ApplyRuntimeAudioState(string exposedName, float linear01)
    {
        if (exposedName == paramMaster) AudioOptionsRuntime.SetMasterVolume(linear01);
        if (exposedName == paramMusic)  AudioOptionsRuntime.SetMusicVolume(linear01);
        if (exposedName == paramSfx)    AudioOptionsRuntime.SetSfxVolume(linear01);
        if (exposedName == paramVoice)  AudioOptionsRuntime.SetVoiceVolume(linear01);
    }

    void EnsureRuntimeOptionRows()
    {
        if (subtitleSizeSlider == null)
            subtitleSizeSlider = CreateGeneratedSliderRow(
                brightnessSlider != null ? brightnessSlider : masterSlider,
                "SubtitleSize_Row",
                "Subtitle Size",
                "Slider_SubtitleSize");

        if (subtitleBackgroundToggle == null)
            subtitleBackgroundToggle = CreateGeneratedToggleRow(
                motionBlurToggle != null ? motionBlurToggle : fullscreenToggle,
                "SubtitleBackground_Row",
                "Subtitle Background",
                "Toggle_SubtitleBackground");

        if (cameraShakeSlider == null)
            cameraShakeSlider = CreateGeneratedSliderRow(
                masterSlider != null ? masterSlider : sfxSlider,
                "CameraShake_Row",
                "Camera Shake",
                "Slider_CameraShake");
    }

    Slider CreateGeneratedSliderRow(Slider templateSlider, string rowName, string labelText, string sliderName)
    {
        if (templateSlider == null)
            return null;

        var templateRow = templateSlider.transform.parent as RectTransform;
        var parent = templateRow != null ? templateRow.parent as RectTransform : null;
        if (templateRow == null || parent == null)
            return null;

        var generatedRowObject = Instantiate(templateRow.gameObject, parent, false);
        generatedRowObject.name = rowName;

        var generatedRow = generatedRowObject.GetComponent<RectTransform>();
        if (generatedRow != null)
        {
            generatedRow.SetAsLastSibling();
            generatedRow.anchoredPosition = new Vector2(templateRow.anchoredPosition.x, CalculateGeneratedRowY(parent));
        }

        var label = generatedRowObject.GetComponentInChildren<Text>(true);
        if (label != null)
            label.text = labelText;

        var slider = generatedRowObject.GetComponentInChildren<Slider>(true);
        if (slider == null)
            return null;

        slider.gameObject.name = sliderName;
        slider.onValueChanged.RemoveAllListeners();
        return slider;
    }

    Toggle CreateGeneratedToggleRow(Toggle templateToggle, string rowName, string labelText, string toggleName)
    {
        if (templateToggle == null)
            return null;

        var templateRow = templateToggle.transform.parent as RectTransform;
        var parent = templateRow != null ? templateRow.parent as RectTransform : null;
        if (templateRow == null || parent == null)
            return null;

        var generatedRowObject = Instantiate(templateRow.gameObject, parent, false);
        generatedRowObject.name = rowName;

        var generatedRow = generatedRowObject.GetComponent<RectTransform>();
        if (generatedRow != null)
        {
            generatedRow.SetAsLastSibling();
            generatedRow.anchoredPosition = new Vector2(templateRow.anchoredPosition.x, CalculateGeneratedRowY(parent));
        }

        var label = generatedRowObject.GetComponentInChildren<Text>(true);
        if (label != null)
            label.text = labelText;

        var toggle = generatedRowObject.GetComponentInChildren<Toggle>(true);
        if (toggle == null)
            return null;

        toggle.gameObject.name = toggleName;
        toggle.onValueChanged.RemoveAllListeners();
        return toggle;
    }

    float CalculateGeneratedRowY(RectTransform parent)
    {
        var rowRects = new List<RectTransform>();
        foreach (Transform child in parent)
        {
            var rect = child as RectTransform;
            if (rect == null)
                continue;

            if (child.GetComponentInChildren<Slider>(true) == null)
                continue;

            rowRects.Add(rect);
        }

        if (rowRects.Count == 0)
            return -GeneratedRowSpacing;

        rowRects.Sort((a, b) => b.anchoredPosition.y.CompareTo(a.anchoredPosition.y));
        var lastRow = rowRects[rowRects.Count - 1];
        var spacing = GeneratedRowSpacing;

        if (rowRects.Count >= 2)
        {
            var previousRow = rowRects[rowRects.Count - 2];
            var measuredSpacing = Mathf.Abs(lastRow.anchoredPosition.y - previousRow.anchoredPosition.y);
            if (measuredSpacing > 1f)
                spacing = measuredSpacing;
        }

        return lastRow.anchoredPosition.y - spacing;
    }

    void ApplyRuntimeAvailabilityState()
    {
        var hasMixer = audioMixer != null;
        SetRowAvailability(masterSlider, true);
        SetRowAvailability(musicSlider, hasMixer);
        SetRowAvailability(sfxSlider, hasMixer);
        SetRowAvailability(voiceSlider, hasMixer);
        SetRowAvailability(cameraShakeSlider, true);
    }

    void SetRowAvailability(Selectable selectable, bool available)
    {
        if (selectable == null)
            return;

        selectable.interactable = available;

        var row = selectable.transform.parent as RectTransform;
        if (row == null)
            return;

        var canvasGroup = row.GetComponent<CanvasGroup>();
        if (canvasGroup == null)
            canvasGroup = row.gameObject.AddComponent<CanvasGroup>();

        canvasGroup.alpha = available ? 1f : 0.45f;
        canvasGroup.blocksRaycasts = true;
        canvasGroup.interactable = true;
    }

    void StartRebind(Button b)
    {
        if (_waitingForKey) return;
        _currentRebindButton = b;
        _waitingForKey = true;
        enabled = true;
        var label = b.GetComponentInChildren<Text>();
        if (label != null) label.text = "Press any key...";
    }

    void FinishRebind(Button b, KeyCode kc)
    {
        _waitingForKey = false;
        var label = b.GetComponentInChildren<Text>();
        if (label != null) label.text = kc.ToString();
        var keyName = GetPrefKeyForButton(b);
        PlayerPrefs.SetString(keyName, kc.ToString());
        PlayerPrefs.Save();
        _keybinds[keyName] = kc;
        _currentRebindButton = null;
        enabled = false;
    }

    void Update()
    {
        if (!_waitingForKey || _currentRebindButton == null)
        {
            enabled = false;
            return;
        }

        foreach (KeyCode kc in Enum.GetValues(typeof(KeyCode)))
        {
            if (Input.GetKeyDown(kc))
            {
                FinishRebind(_currentRebindButton, kc);
                break;
            }
        }
    }

    string GetPrefKeyForButton(Button b)
    {
        return "Key_" + b.gameObject.name.Replace("Button_", "");
    }

    public void SaveAll()
    {
        PlayerPrefs.Save();
        Debug.Log("[OptionsManagerAdvanced] SaveAll complete.");
    }

    public void LoadAll()
    {
        ApplyAudioInitial();
        OnBrightnessChanged?.Invoke(PlayerPrefs.GetFloat(K_BRIGHT, 1f));
        OnMotionBlurToggled?.Invoke(PlayerPrefs.GetInt(K_MBLUR, 0) == 1);
        OnSubtitleSizeChanged?.Invoke(PlayerPrefs.GetFloat(K_SUB_SIZE, 1f));
        OnSubtitleBgToggled?.Invoke(PlayerPrefs.GetInt(K_SUB_BG, 1) == 1);
        OnCameraShakeStrengthChanged?.Invoke(PlayerPrefs.GetFloat(K_CAM_SHAKE, 1f));
        FramePacingRuntime.RefreshFromPrefs();
        Debug.Log("[OptionsManagerAdvanced] LoadAll applied.");
    }
}

#pragma warning restore CS0618
