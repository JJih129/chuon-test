using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Audio;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
public class OptionsManagerAdvanced : MonoBehaviour
{
    // =========================
    // ▶ 비디오 섹션
    // =========================
    [Header("▶ 비디오 설정")]
    [Tooltip("해상도 선택 드롭다운 (자동으로 화면 해상도 목록으로 채움)")]
    public Dropdown resolutionDropdown;
    [Tooltip("전체화면 토글")]
    public Toggle fullscreenToggle;
    [Tooltip("VSync 토글 (체크 시 1로, 해제 시 0으로 셋)")]
    public Toggle vSyncToggle;
    [Tooltip("밝기 슬라이더 (0~1). 실제 적용은 프로젝트의 브라이트니스 처리 로직에 바인딩 필요)")]
    public Slider brightnessSlider;
    [Tooltip("모션 블러 On/Off 토글 (프로젝트 모션블러 활성화/비활성화 코드에 연결)")]
    public Toggle motionBlurToggle;

    // 내부: 사용 가능한 해상도 목록
    Resolution[] _resolutions;

    // =========================
    // ▶ 오디오 섹션
    // =========================
    [Header("▶ 오디오 설정")]
    [Tooltip("프로젝트 오디오믹서 (노출 파라미터로 볼륨을 제어함)")]
    public AudioMixer audioMixer;
    [Tooltip("마스터 볼륨 슬라이더 (0~1)")]
    public Slider masterSlider;
    [Tooltip("BGM 볼륨 슬라이더 (0~1)")]
    public Slider musicSlider;
    [Tooltip("SFX 볼륨 슬라이더 (0~1)")]
    public Slider sfxSlider;
    [Tooltip("Voice 볼륨 슬라이더 (0~1)")]
    public Slider voiceSlider;
    [Tooltip("AudioMixer에 노출된 파라미터 이름들")]
    public string paramMaster = "MasterVol";
    public string paramMusic = "MusicVol";
    public string paramSfx = "SfxVol";
    public string paramVoice = "VoiceVol";

    // =========================
    // ▶ 조작(키 바인딩) 섹션
    // =========================
    [Header("▶ 조작 / 키 바인딩")]
    [Tooltip("키 바인드 버튼 리스트. 버튼 이름 패턴: Button_Key_<ActionName>")]
    public List<Button> keybindButtons = new List<Button>();
    // 내부 저장: PlayerPrefs 키 -> KeyCode
    Dictionary<string, KeyCode> _keybinds = new Dictionary<string, KeyCode>();
    Button _currentRebindButton = null;
    bool _waitingForKey = false;

    // =========================
    // ▶ 접근성 섹션
    // =========================
    [Header("▶ 접근성")]
    [Tooltip("자막 크기 슬라이더 (0.5~2.0 등)")]
    public Slider subtitleSizeSlider;
    [Tooltip("자막 배경 토글 (on/off)")]
    public Toggle subtitleBackgroundToggle;
    [Tooltip("카메라 흔들림 강도 슬라이더 (0~1)")]
    public Slider cameraShakeSlider;

    // 이벤트 훅(외부 시스템에 연결해서 실제 효과 구현)
    public Action<float> OnBrightnessChanged;           // brightnessSlider 값 전파
    public Action<bool> OnMotionBlurToggled;           // motion blur toggle
    public Action<float> OnSubtitleSizeChanged;        // subtitle size
    public Action<bool> OnSubtitleBgToggled;           // subtitle bg on/off
    public Action<float> OnCameraShakeStrengthChanged; // camera shake strength

    // PlayerPrefs 키 접두사
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

    void Awake()
    {
        // 비디오: 해상도 채우기
        _resolutions = Screen.resolutions;
        resolutionDropdown?.options.Clear();
        int currentIndex = 0;
        for (int i = 0; i < _resolutions.Length; i++)
        {
            var r = _resolutions[i];
            string txt = r.width + " x " + r.height + " @ " + r.refreshRate + "Hz";
            resolutionDropdown?.options.Add(new Dropdown.OptionData(txt));
            if (r.width == Screen.currentResolution.width && r.height == Screen.currentResolution.height && r.refreshRate == Screen.currentResolution.refreshRate)
                currentIndex = i;
        }
        if (resolutionDropdown != null)
        {
            resolutionDropdown.onValueChanged.RemoveAllListeners();
            resolutionDropdown.onValueChanged.AddListener(OnResolutionChanged);
            resolutionDropdown.value = PlayerPrefs.GetInt(K_RES, currentIndex);
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
            vSyncToggle.isOn = PlayerPrefs.GetInt(K_VSYNC, QualitySettings.vSyncCount > 0 ? 1 : 0) == 1;
        }
        if (brightnessSlider != null)
        {
            brightnessSlider.onValueChanged.RemoveAllListeners();
            brightnessSlider.onValueChanged.AddListener(v => {
                PlayerPrefs.SetFloat(K_BRIGHT, v); 
                OnBrightnessChanged?.Invoke(v);
            });
            brightnessSlider.value = PlayerPrefs.GetFloat(K_BRIGHT, 1f);
        }
        if (motionBlurToggle != null)
        {
            motionBlurToggle.onValueChanged.RemoveAllListeners();
            motionBlurToggle.onValueChanged.AddListener(v => {
                PlayerPrefs.SetInt(K_MBLUR, v ? 1 : 0);
                OnMotionBlurToggled?.Invoke(v);
            });
            motionBlurToggle.isOn = PlayerPrefs.GetInt(K_MBLUR, 1) == 1;
        }

        // 오디오: 슬라이더 바인딩 및 로드
        if (masterSlider != null) { masterSlider.onValueChanged.RemoveAllListeners(); masterSlider.onValueChanged.AddListener(v => SetAudioParam(paramMaster, v)); masterSlider.value = PlayerPrefs.GetFloat(K_MASTER, 1f); }
        if (musicSlider != null)  { musicSlider.onValueChanged.RemoveAllListeners(); musicSlider.onValueChanged.AddListener(v => SetAudioParam(paramMusic, v)); musicSlider.value = PlayerPrefs.GetFloat(K_MUSIC, 1f); }
        if (sfxSlider != null)    { sfxSlider.onValueChanged.RemoveAllListeners(); sfxSlider.onValueChanged.AddListener(v => SetAudioParam(paramSfx, v)); sfxSlider.value = PlayerPrefs.GetFloat(K_SFX, 1f); }
        if (voiceSlider != null)  { voiceSlider.onValueChanged.RemoveAllListeners(); voiceSlider.onValueChanged.AddListener(v => SetAudioParam(paramVoice, v)); voiceSlider.value = PlayerPrefs.GetFloat(K_VOICE, 1f); }

        // 접근성
        if (subtitleSizeSlider != null) { subtitleSizeSlider.onValueChanged.RemoveAllListeners(); subtitleSizeSlider.onValueChanged.AddListener(v => { PlayerPrefs.SetFloat(K_SUB_SIZE, v); OnSubtitleSizeChanged?.Invoke(v); }); subtitleSizeSlider.value = PlayerPrefs.GetFloat(K_SUB_SIZE, 1f); }
        if (subtitleBackgroundToggle != null) { subtitleBackgroundToggle.onValueChanged.RemoveAllListeners(); subtitleBackgroundToggle.onValueChanged.AddListener(v => { PlayerPrefs.SetInt(K_SUB_BG, v?1:0); OnSubtitleBgToggled?.Invoke(v); }); subtitleBackgroundToggle.isOn = PlayerPrefs.GetInt(K_SUB_BG, 1) == 1; }
        if (cameraShakeSlider != null) { cameraShakeSlider.onValueChanged.RemoveAllListeners(); cameraShakeSlider.onValueChanged.AddListener(v => { PlayerPrefs.SetFloat(K_CAM_SHAKE, v); OnCameraShakeStrengthChanged?.Invoke(v); }); cameraShakeSlider.value = PlayerPrefs.GetFloat(K_CAM_SHAKE, 1f); }

        // 키 바인드 버튼 초기화
        foreach (var b in keybindButtons)
        {
            var keyName = GetPrefKeyForButton(b);
            string saved = PlayerPrefs.GetString(keyName, "");
            var label = b.GetComponentInChildren<Text>();
            if (!string.IsNullOrEmpty(saved) && label != null) label.text = saved;
            b.onClick.RemoveAllListeners();
            b.onClick.AddListener(() => StartRebind(b));
        }

        // 적용: 오디오 param 적용(초기)
        ApplyAudioInitial();
    }

    void Start()
    {
        // 해상도 적용 (PlayerPrefs에 저장된 값)
        int idx = PlayerPrefs.GetInt(K_RES, resolutionDropdown != null ? resolutionDropdown.value : 0);
        ApplyResolutionIndex(idx, PlayerPrefs.GetInt(K_FULLSCREEN, Screen.fullScreen?1:0)==1);
        // vSync
        bool vs = PlayerPrefs.GetInt(K_VSYNC, QualitySettings.vSyncCount>0?1:0)==1;
        QualitySettings.vSyncCount = vs ? 1 : 0;
    }

    // -------------------------
    // 비디오 콜백들
    // -------------------------
    void OnResolutionChanged(int idx)
    {
        PlayerPrefs.SetInt(K_RES, idx);
        ApplyResolutionIndex(idx, fullscreenToggle != null ? (fullscreenToggle.isOn ? 1 : 0) : (Screen.fullScreen?1:0));
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
    }

    void ApplyResolutionIndex(int idx, bool fullscreen)
    {
        if (_resolutions == null || _resolutions.Length == 0) return;
        idx = Mathf.Clamp(idx, 0, _resolutions.Length - 1);
        var r = _resolutions[idx];
        Screen.SetResolution(r.width, r.height, fullscreen, r.refreshRate);
    }

    // -------------------------
    // 오디오
    // -------------------------
    void SetAudioParam(string exposedName, float linear01)
    {
        if (audioMixer == null) return;
        // linear(0..1) -> dB (-80 .. 0)
        float dB = (linear01 <= 0.0001f) ? -80f : 20f * Mathf.Log10(Mathf.Clamp(linear01, 0.0001f, 1f));
        audioMixer.SetFloat(exposedName, dB);
        // 저장
        if (exposedName == paramMaster) PlayerPrefs.SetFloat(K_MASTER, linear01);
        if (exposedName == paramMusic)  PlayerPrefs.SetFloat(K_MUSIC, linear01);
        if (exposedName == paramSfx)    PlayerPrefs.SetFloat(K_SFX, linear01);
        if (exposedName == paramVoice)  PlayerPrefs.SetFloat(K_VOICE, linear01);
    }

    void ApplyAudioInitial()
    {
        if (audioMixer == null) return;
        SetAudioParam(paramMaster, PlayerPrefs.GetFloat(K_MASTER, 1f));
        SetAudioParam(paramMusic,  PlayerPrefs.GetFloat(K_MUSIC, 1f));
        SetAudioParam(paramSfx,    PlayerPrefs.GetFloat(K_SFX, 1f));
        SetAudioParam(paramVoice,  PlayerPrefs.GetFloat(K_VOICE, 1f));
    }

    // -------------------------
    // 키 바인드 (리바인드 루틴)
    // -------------------------
    void StartRebind(Button b)
    {
        if (_waitingForKey) return;
        _currentRebindButton = b;
        _waitingForKey = true;
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
    }

    void Update()
    {
        if (_waitingForKey && _currentRebindButton != null)
        {
            foreach (KeyCode kc in Enum.GetValues(typeof(KeyCode)))
            {
                if (Input.GetKeyDown(kc))
                {
                    FinishRebind(_currentRebindButton, kc);
                    break;
                }
            }
        }
    }

    string GetPrefKeyForButton(Button b)
    {
        return "Key_" + b.gameObject.name.Replace("Button_", "");
    }

    // -------------------------
    // 저장 / 불러오기 단순화 API
    // -------------------------
    public void SaveAll()
    {
        PlayerPrefs.Save();
        Debug.Log("[OptionsManagerAdvanced] SaveAll complete.");
    }

    public void LoadAll()
    {
        // 재적용 호출
        ApplyAudioInitial();
        // brightness / subtitle / cam shake 등 이벤트 호출
        OnBrightnessChanged?.Invoke(PlayerPrefs.GetFloat(K_BRIGHT, 1f));
        OnSubtitleSizeChanged?.Invoke(PlayerPrefs.GetFloat(K_SUB_SIZE, 1f));
        OnSubtitleBgToggled?.Invoke(PlayerPrefs.GetInt(K_SUB_BG, 1) == 1);
        OnCameraShakeStrengthChanged?.Invoke(PlayerPrefs.GetFloat(K_CAM_SHAKE, 1f));
        Debug.Log("[OptionsManagerAdvanced] LoadAll applied.");
    }
}
