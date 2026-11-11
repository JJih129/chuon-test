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
    [Tooltip("화면 번쩍임(궁극기 플래시/경보 점멸) 강도. 비어 있으면 카메라 흔들림 행을 복제해 만든다.")]
    public Slider flashIntensitySlider;
    [Tooltip("궁극기/패링 흑백 흡수(채도 제거) 사용. 비어 있으면 자막 배경 행을 복제해 만든다.")]
    public Toggle bwAbsorbToggle;
    [Tooltip("연출 레터박스(2.35:1) 표시 (CinematicSettings). 비어 있으면 자막 배경 행을 복제해 만든다.")]
    public Toggle cinematicLetterboxToggle;
    [Tooltip("재도전 시 이미 본 연출 생략 (CinematicSettings). 비어 있으면 자막 배경 행을 복제해 만든다.")]
    public Toggle cinematicSkipSeenToggle;
    [Tooltip("자막(대사) 표시 (ExhibitionPrototypePresentationPolicy.DialogueEnabled). 비어 있으면 런타임 생성.")]
    public Toggle subtitlesEnabledToggle;
    [Tooltip("전투 중 짧은 대사 (CombatBarkDirector). 비어 있으면 런타임 생성.")]
    public Toggle combatBarksToggle;
    [Tooltip("처형 카메라 연출 (ExecutionCamSettings.Enabled, opt_exec_cam). 비어 있으면 런타임 생성.")]
    public Toggle executionCamToggle;
    [Tooltip("처형 연출 모션 줄이기 (ExecutionCamSettings.ReduceMotion, opt_reduce_motion). 비어 있으면 런타임 생성.")]
    public Toggle reduceMotionToggle;

    [Header("▶ 카메라 (비어 있으면 런타임 생성, CameraOptionsRuntime 에 저장/즉시 적용)")]
    public Slider lookSensitivityMouseSlider;
    public Slider lookSensitivityPadSlider;
    public Toggle invertYToggle;
    public Toggle autoRecenterToggle;
    public Slider cameraFovSlider;

    public Action<float> OnBrightnessChanged;
    public Action<bool> OnMotionBlurToggled;
    public Action<float> OnSubtitleSizeChanged;
    public Action<bool> OnSubtitleBgToggled;

    // 자막 프리젠터용 전역 이벤트 (옵션 매니저 인스턴스가 다른 씬에 있어도 구독 가능)
    public static event Action<float> SubtitleSizeChangedGlobal;
    public static event Action<bool> SubtitleBackgroundChangedGlobal;
    public static float CurrentSubtitleSize => PlayerPrefs.GetFloat(K_SUB_SIZE, 1f);
    public static bool CurrentSubtitleBackground => PlayerPrefs.GetInt(K_SUB_BG, 1) == 1;
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

        if (ChuonInputRebinding.IsRebinding)
            ChuonInputRebinding.Cancel();

        _waitingForKey = false;
        if (_currentRebindButton != null)
            RefreshKeybindLabel(_currentRebindButton);
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
            OnCameraShakeStrengthChanged += AccessibilityOptionsRuntime.SetCameraShakeStrength;

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
            resolutionDropdown.RefreshShownValue();
            // 반복 13: 저장값은 인덱스가 아니라 w×h@Hz 로 찾는다 (모니터/드라이버마다 Screen.resolutions 순서·개수가 달라 인덱스가 엉뚱한 해상도를 가리키던 문제)
            //  + 값 설정은 알림 없이 → Awake 마다(씬 로드마다) SetResolution 이 다시 호출되지 않는다.
            resolutionDropdown.SetValueWithoutNotify(ResolveSavedResolutionIndex(currentIndex));
            resolutionDropdown.RefreshShownValue();
            resolutionDropdown.onValueChanged.AddListener(OnResolutionChanged);
        }

        if (fullscreenToggle != null)
        {
            fullscreenToggle.onValueChanged.RemoveAllListeners();
            fullscreenToggle.SetIsOnWithoutNotify(PlayerPrefs.GetInt(K_FULLSCREEN, Screen.fullScreen ? 1 : 0) == 1);
            fullscreenToggle.onValueChanged.AddListener(OnFullscreenToggled);
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

        if (subtitleSizeSlider != null) { subtitleSizeSlider.onValueChanged.RemoveAllListeners(); subtitleSizeSlider.onValueChanged.AddListener(v => { PlayerPrefs.SetFloat(K_SUB_SIZE, v); OnSubtitleSizeChanged?.Invoke(v); SubtitleSizeChangedGlobal?.Invoke(v); }); subtitleSizeSlider.value = PlayerPrefs.GetFloat(K_SUB_SIZE, 1f); }
        if (subtitleBackgroundToggle != null) { subtitleBackgroundToggle.onValueChanged.RemoveAllListeners(); subtitleBackgroundToggle.onValueChanged.AddListener(v => { PlayerPrefs.SetInt(K_SUB_BG, v?1:0); OnSubtitleBgToggled?.Invoke(v); SubtitleBackgroundChangedGlobal?.Invoke(v); }); subtitleBackgroundToggle.isOn = PlayerPrefs.GetInt(K_SUB_BG, 1) == 1; }
        if (flashIntensitySlider != null) { flashIntensitySlider.onValueChanged.RemoveAllListeners(); flashIntensitySlider.onValueChanged.AddListener(AccessibilityOptionsRuntime.SetFlashIntensity); flashIntensitySlider.value = AccessibilityOptionsRuntime.FlashIntensity; }
        if (bwAbsorbToggle != null) { bwAbsorbToggle.onValueChanged.RemoveAllListeners(); bwAbsorbToggle.onValueChanged.AddListener(AccessibilityOptionsRuntime.SetBwAbsorbEnabled); bwAbsorbToggle.isOn = AccessibilityOptionsRuntime.BwAbsorbEnabled; }
        if (cinematicLetterboxToggle != null) { cinematicLetterboxToggle.onValueChanged.RemoveAllListeners(); cinematicLetterboxToggle.onValueChanged.AddListener(CinematicSettings.SetLetterboxEnabled); cinematicLetterboxToggle.isOn = CinematicSettings.LetterboxEnabled; }
        if (cinematicSkipSeenToggle != null) { cinematicSkipSeenToggle.onValueChanged.RemoveAllListeners(); cinematicSkipSeenToggle.onValueChanged.AddListener(CinematicSettings.SetSkipSeenOnRetry); cinematicSkipSeenToggle.isOn = CinematicSettings.SkipSeenOnRetry; }
        if (cameraShakeSlider != null) { cameraShakeSlider.onValueChanged.RemoveAllListeners(); cameraShakeSlider.onValueChanged.AddListener(v => { PlayerPrefs.SetFloat(K_CAM_SHAKE, v); OnCameraShakeStrengthChanged?.Invoke(v); }); cameraShakeSlider.value = PlayerPrefs.GetFloat(K_CAM_SHAKE, 1f); }
        BindPrefToggle(subtitlesEnabledToggle, ExhibitionPrototypePresentationPolicy.DialogueEnabled, ExhibitionPrototypePresentationPolicy.SetDialogueEnabled);
        BindPrefToggle(combatBarksToggle, PlayerPrefs.GetInt(ExhibitionPrototypePresentationPolicy.CombatBarkPrefKey, 1) != 0, ExhibitionPrototypePresentationPolicy.SetCombatBarksEnabled);
        BindPrefToggle(executionCamToggle, ExecutionCamSettings.Enabled, ExecutionCamSettings.SetEnabled);
        BindPrefToggle(reduceMotionToggle, ExecutionCamSettings.ReduceMotion, ExecutionCamSettings.SetReduceMotion);
        BindCameraOptionControls();

        foreach (var b in keybindButtons)
        {
            if (b == null)
                continue;
            RefreshKeybindLabel(b);
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
        // 저장된 해상도/전체화면은 DisplayModeRuntime 이 부팅 시 1회 복원한다. 여기서는 UI 표시만 맞춘다 (씬마다 SetResolution 반복 금지).
        bool vs = PlayerPrefs.GetInt(K_VSYNC, 0) == 1;
        QualitySettings.vSyncCount = vs ? 1 : 0;
        FramePacingRuntime.RefreshFromPrefs();
        enabled = _waitingForKey;
    }

    void OnResolutionChanged(int idx)
    {
        PlayerPrefs.SetInt(K_RES, idx);
        SaveResolutionPrefs(idx);
        bool fs = (fullscreenToggle != null) ? fullscreenToggle.isOn : Screen.fullScreen;
        ApplyResolutionIndex(idx, fs);
        PlayerPrefs.Save();
    }

    void OnFullscreenToggled(bool v)
    {
        PlayerPrefs.SetInt(K_FULLSCREEN, v ? 1 : 0);
        int idx = resolutionDropdown != null ? resolutionDropdown.value : ResolveSavedResolutionIndex(0);
        SaveResolutionPrefs(idx);
        ApplyResolutionIndex(idx, v);
        PlayerPrefs.Save();
    }

    int ResolveSavedResolutionIndex(int fallback)
    {
        if (_resolutions == null || _resolutions.Length == 0)
            return 0;
        if (DisplayModeRuntime.TryGetSaved(out int w, out int h, out int hz, out _))
        {
            int best = -1;
            for (int i = 0; i < _resolutions.Length; i++)
            {
                Resolution r = _resolutions[i];
                if (r.width != w || r.height != h)
                    continue;
                if (best < 0 || Mathf.Abs(DisplayModeRuntime.RefreshHz(r) - hz) < Mathf.Abs(DisplayModeRuntime.RefreshHz(_resolutions[best]) - hz))
                    best = i;
            }
            if (best >= 0)
                return best;
        }
        // 구버전 저장값(인덱스)은 현재 해상도와 크기가 같을 때만 신뢰
        int legacy = PlayerPrefs.GetInt(K_RES, -1);
        if (legacy >= 0 && legacy < _resolutions.Length && _resolutions[legacy].width == Screen.width && _resolutions[legacy].height == Screen.height)
            return legacy;
        return Mathf.Clamp(fallback, 0, _resolutions.Length - 1);
    }

    void SaveResolutionPrefs(int idx)
    {
        if (_resolutions == null || _resolutions.Length == 0)
            return;
        Resolution r = _resolutions[Mathf.Clamp(idx, 0, _resolutions.Length - 1)];
        bool fs = fullscreenToggle != null ? fullscreenToggle.isOn : Screen.fullScreen;
        DisplayModeRuntime.Save(r.width, r.height, DisplayModeRuntime.RefreshHz(r), fs);
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

        if (flashIntensitySlider == null)
            flashIntensitySlider = CreateGeneratedSliderRow(
                cameraShakeSlider != null ? cameraShakeSlider : masterSlider,
                "FlashIntensity_Row",
                "Screen Flash",
                "Slider_FlashIntensity");

        Slider sliderTemplate = cameraShakeSlider != null ? cameraShakeSlider : masterSlider;
        Toggle toggleTemplate = subtitleBackgroundToggle != null ? subtitleBackgroundToggle : fullscreenToggle;
        if (bwAbsorbToggle == null)
            bwAbsorbToggle = CreateGeneratedToggleRow(toggleTemplate, "BwAbsorb_Row", "B&W Absorb Effect", "Toggle_BwAbsorb") ?? CreateHiddenToggle("Toggle_BwAbsorb");
        if (cinematicLetterboxToggle == null)
            cinematicLetterboxToggle = CreateGeneratedToggleRow(toggleTemplate, "CineLetterbox_Row", "Cinematic Letterbox", "Toggle_CineLetterbox") ?? CreateHiddenToggle("Toggle_CineLetterbox");
        if (cinematicSkipSeenToggle == null)
            cinematicSkipSeenToggle = CreateGeneratedToggleRow(toggleTemplate, "CineSkipSeen_Row", "Skip Seen Cinematics On Retry", "Toggle_CineSkipSeen") ?? CreateHiddenToggle("Toggle_CineSkipSeen");
        if (lookSensitivityMouseSlider == null)
            lookSensitivityMouseSlider = CreateGeneratedSliderRow(sliderTemplate, "LookSensMouse_Row", "Mouse Sensitivity", "Slider_LookSensMouse") ?? CreateHiddenSlider("Slider_LookSensMouse");
        if (lookSensitivityPadSlider == null)
            lookSensitivityPadSlider = CreateGeneratedSliderRow(sliderTemplate, "LookSensPad_Row", "Pad Sensitivity", "Slider_LookSensPad") ?? CreateHiddenSlider("Slider_LookSensPad");
        if (cameraFovSlider == null)
            cameraFovSlider = CreateGeneratedSliderRow(sliderTemplate, "CameraFov_Row", "Field of View", "Slider_CameraFov") ?? CreateHiddenSlider("Slider_CameraFov");
        if (invertYToggle == null)
            invertYToggle = CreateGeneratedToggleRow(toggleTemplate, "InvertY_Row", "Invert Y", "Toggle_InvertY") ?? CreateHiddenToggle("Toggle_InvertY");
        if (autoRecenterToggle == null)
            autoRecenterToggle = CreateGeneratedToggleRow(toggleTemplate, "AutoRecenter_Row", "Auto Camera Recenter", "Toggle_AutoRecenter") ?? CreateHiddenToggle("Toggle_AutoRecenter");
        // 반복 21: 저장/적용은 되지만 메뉴 항목이 없던 옵션 (자막 표시, 전투 대사, 처형 카메라, 모션 줄이기)
        if (subtitlesEnabledToggle == null)
            subtitlesEnabledToggle = CreateGeneratedToggleRow(toggleTemplate, "SubtitlesEnabled_Row", "자막 표시", "Toggle_SubtitlesEnabled") ?? CreateHiddenToggle("Toggle_SubtitlesEnabled");
        if (combatBarksToggle == null)
            combatBarksToggle = CreateGeneratedToggleRow(toggleTemplate, "CombatBarks_Row", "전투 대사", "Toggle_CombatBarks") ?? CreateHiddenToggle("Toggle_CombatBarks");
        if (executionCamToggle == null)
            executionCamToggle = CreateGeneratedToggleRow(toggleTemplate, "ExecutionCam_Row", "처형 카메라 연출", "Toggle_ExecutionCam") ?? CreateHiddenToggle("Toggle_ExecutionCam");
        if (reduceMotionToggle == null)
            reduceMotionToggle = CreateGeneratedToggleRow(toggleTemplate, "ReduceMotion_Row", "처형 연출 모션 줄이기", "Toggle_ReduceMotion") ?? CreateHiddenToggle("Toggle_ReduceMotion");
    }

    // 저장값을 알림 없이 표시한 뒤 setter(저장 + 즉시 적용)를 연결한다.
    static void BindPrefToggle(Toggle toggle, bool value, Action<bool> setter)
    {
        if (toggle == null)
            return;
        toggle.onValueChanged.RemoveAllListeners();
        toggle.SetIsOnWithoutNotify(value);
        toggle.onValueChanged.AddListener(v => setter(v));
    }

    // 카메라 옵션 (CameraOptionsRuntime: PlayerPrefs 저장 + 즉시 적용)
    void BindCameraOptionControls()
    {
        CameraOptionsRuntime.RefreshFromPrefs();
        BindRangeSlider(lookSensitivityMouseSlider, CameraOptionsRuntime.MinSensitivity, CameraOptionsRuntime.MaxSensitivity, false, CameraOptionsRuntime.MouseSensitivity, CameraOptionsRuntime.SetMouseSensitivity);
        BindRangeSlider(lookSensitivityPadSlider, CameraOptionsRuntime.MinSensitivity, CameraOptionsRuntime.MaxSensitivity, false, CameraOptionsRuntime.PadSensitivity, CameraOptionsRuntime.SetPadSensitivity);
        BindRangeSlider(cameraFovSlider, CameraOptionsRuntime.MinFov, CameraOptionsRuntime.MaxFov, true, CameraOptionsRuntime.FieldOfView, CameraOptionsRuntime.SetFieldOfView);
        if (invertYToggle != null) { invertYToggle.onValueChanged.RemoveAllListeners(); invertYToggle.isOn = CameraOptionsRuntime.InvertY; invertYToggle.onValueChanged.AddListener(CameraOptionsRuntime.SetInvertY); }
        if (autoRecenterToggle != null) { autoRecenterToggle.onValueChanged.RemoveAllListeners(); autoRecenterToggle.isOn = CameraOptionsRuntime.AutoRecenter; autoRecenterToggle.onValueChanged.AddListener(CameraOptionsRuntime.SetAutoRecenter); }
    }

    static void BindRangeSlider(Slider slider, float min, float max, bool wholeNumbers, float value, Action<float> setter)
    {
        if (slider == null)
            return;
        slider.onValueChanged.RemoveAllListeners();
        slider.minValue = min;
        slider.maxValue = max;
        slider.wholeNumbers = wholeNumbers;
        slider.SetValueWithoutNotify(value);
        slider.onValueChanged.AddListener(v => setter(v));
    }

    Slider CreateHiddenSlider(string objectName)
    {
        var go = new GameObject(objectName, typeof(RectTransform), typeof(Slider));
        go.transform.SetParent(transform, false);
        go.SetActive(false);
        return go.GetComponent<Slider>();
    }

    Toggle CreateHiddenToggle(string objectName)
    {
        var go = new GameObject(objectName, typeof(RectTransform), typeof(Toggle));
        go.transform.SetParent(transform, false);
        go.SetActive(false);
        return go.GetComponent<Toggle>();
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
        // 반복 13: 음악/효과음/보이스 볼륨은 믹서 노출 파라미터가 아니라 AudioOptionsRuntime.Scale*(소스별) + TimelineAudioRouter(타임라인)로
        // 항상 적용된다 → 믹서 미할당이어도 행을 비활성(반투명)으로 두지 않는다.
        SetRowAvailability(masterSlider, true);
        SetRowAvailability(musicSlider, true);
        SetRowAvailability(sfxSlider, true);
        SetRowAvailability(voiceSlider, true);
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

    // 반복 13: 씬/프리팹의 키 버튼(Button_Key_*)을 실제 입력(ChuonInput, Input System 인터랙티브 리바인딩)에 연결.
    //  Attack → 기본공격, Jump → 회피(점프 없음), MoveForward/MoveBack/MoveLeft/MoveRight → 이동 파트, 그 외 이름은 ChuonAction 이름과 일치하면 연결.
    static bool TryMapKeybindButton(Button b, out ChuonAction action, out int bindingIndex)
    {
        action = ChuonAction.AttackLight;
        bindingIndex = 0;
        if (b == null)
            return false;
        string id = b.gameObject.name.Replace("Button_", string.Empty).Replace("Key_", string.Empty);
        switch (id)
        {
            case "Attack": action = ChuonAction.AttackLight; return true;
            case "Jump": action = ChuonAction.Dodge; return true;
            case "MoveForward": action = ChuonAction.Move; bindingIndex = ChuonInput.MoveUpIndex; return true;
            case "MoveBack": action = ChuonAction.Move; bindingIndex = ChuonInput.MoveDownIndex; return true;
            case "MoveLeft": action = ChuonAction.Move; bindingIndex = ChuonInput.MoveLeftIndex; return true;
            case "MoveRight": action = ChuonAction.Move; bindingIndex = ChuonInput.MoveRightIndex; return true;
        }
        return Enum.TryParse(id, out action) && ChuonInput.IsRebindable(action);
    }

    void RefreshKeybindLabel(Button b)
    {
        var label = b != null ? b.GetComponentInChildren<Text>() : null;
        if (label == null)
            return;
        if (TryMapKeybindButton(b, out ChuonAction action, out int index))
            label.text = InputGlyphs.BindingLabel(ChuonInput.KeyboardAction(action), index, ChuonInputScheme.KeyboardMouse);
    }

    void StartRebind(Button b)
    {
        if (_waitingForKey || ChuonInputRebinding.IsRebinding) return;
        if (!TryMapKeybindButton(b, out ChuonAction action, out int index))
            return;

        _currentRebindButton = b;
        _waitingForKey = true;
        var label = b.GetComponentInChildren<Text>();
        if (label != null) label.text = "Press any key...";
        bool started = ChuonInputRebinding.Start(action, false, index, result => FinishRebind(b));
        if (!started)
            FinishRebind(b);
    }

    void FinishRebind(Button b)
    {
        _waitingForKey = false;
        RefreshKeybindLabel(b);
        foreach (var other in keybindButtons)
            RefreshKeybindLabel(other); // 충돌 교체로 다른 키가 바뀌었을 수 있다
        _currentRebindButton = null;
        enabled = false;
    }

    void Update()
    {
        // 리바인드 입력은 ChuonInputRebinding(Input System) 이 처리한다. 대기 상태가 아니면 꺼 둔다.
        if (!_waitingForKey || _currentRebindButton == null || !ChuonInputRebinding.IsRebinding)
        {
            if (_waitingForKey && !ChuonInputRebinding.IsRebinding && _currentRebindButton != null)
                FinishRebind(_currentRebindButton);
            enabled = false;
        }
    }

    string GetPrefKeyForButton(Button b)
    {
        return "Key_" + b.gameObject.name.Replace("Button_", "");
    }

    // 설정 화면을 닫을 때 디스크에 즉시 기록 (비정상 종료 시 유실 방지)
    public static void FlushPrefs()
    {
        PlayerPrefs.Save();
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
        SubtitleSizeChangedGlobal?.Invoke(CurrentSubtitleSize);
        SubtitleBackgroundChangedGlobal?.Invoke(CurrentSubtitleBackground);
        OnCameraShakeStrengthChanged?.Invoke(PlayerPrefs.GetFloat(K_CAM_SHAKE, 1f));
        FramePacingRuntime.RefreshFromPrefs();
        Debug.Log("[OptionsManagerAdvanced] LoadAll applied.");
    }
}

#pragma warning restore CS0618
