// 파일명: KioskAttract.cs
// 타이틀 방치 시 어트랙트 루프. PV 영상이 있으면 VideoPlayer 로 반복 재생, 없거나 실패하면
// 타이틀 위에 느린 카메라 흔들기 + 로고/기능 문구 순환(대체 연출). 아무 입력이면 KioskDirector 가 닫는다.
// 영상 탐색 순서: -kioskVideo 경로 → StreamingAssets/Kiosk/*.mp4 → StreamingAssets/*.mp4
//               → <실행 폴더>/PV, <실행 폴더>/../PV (Builds/PV) → (에디터) <프로젝트>/Builds/PV. 이름에 "web" 이 든 파일 우선.
using System;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

[DisallowMultipleComponent]
public sealed class KioskAttract : MonoBehaviour
{
    const int SortingOrder = 9400;
    const float PrepareTimeout = 5f;
    const float CaptionInterval = 5.5f;
    const float VideoVolume = 0.8f;

    static readonly string[] Captions =
    {
        "패링  —  금색 신호에 맞춰 막고, 반격하라",
        "퍼펙트 회피  —  빨간 신호는 피하라",
        "브레이크 → 처형  —  균형이 무너진 순간을 놓치지 마라",
        "궁극기 '위상단절검'  —  화면째로 베어라",
        "Type-D  —  츠카사 본사 격납고의 수문장",
    };

    Canvas _canvas;
    CanvasGroup _group;
    RawImage _videoImage;
    Image _videoBack;
    RectTransform _fallbackRoot;
    Text _caption;
    Text _prompt;
    Text _logo;

    VideoPlayer _video;
    AudioSource _audio;
    RenderTexture _rt;
    bool _videoFailed;
    bool _videoPlaying;
    float _prepareStartedAt;
    bool _audioPausedByUs;

    Camera _swayCamera;
    Quaternion _swayBase;
    float _shownAt;
    int _captionIndex = -1;

    public bool IsShowing { get; private set; }
    public bool PlayingVideo => _videoPlaying;
    public float ShownSeconds => IsShowing ? Time.unscaledTime - _shownAt : 0f;

    public static KioskAttract Create(Transform parent)
    {
        GameObject go = new GameObject("KioskAttract");
        go.transform.SetParent(parent, false);
        KioskAttract attract = go.AddComponent<KioskAttract>();
        attract.Build();
        return attract;
    }

    public void Show()
    {
        if (IsShowing)
            return;

        IsShowing = true;
        _shownAt = Time.unscaledTime;
        _captionIndex = -1;
        _canvas.enabled = true;
        _group.alpha = 0f;

        string path = _videoFailed ? null : FindVideo();
        if (!string.IsNullOrEmpty(path) && TryStartVideo(path))
        {
            SetFallbackVisible(true); // 영상 준비 동안은 로고 화면, 준비되면 HandlePrepared 가 숨긴다
        }
        else
        {
            SetFallbackVisible(true);
            BeginCameraSway();
        }
    }

    public void Hide()
    {
        if (!IsShowing)
            return;

        IsShowing = false;
        StopVideo();
        EndCameraSway();
        _canvas.enabled = false;
        _group.alpha = 0f;
    }

    void OnDisable()
    {
        Hide();
    }

    void OnDestroy()
    {
        ReleaseRenderTexture();
    }

    void Update()
    {
        if (!IsShowing)
            return;

        float t = Time.unscaledTime - _shownAt;
        _group.alpha = Mathf.Clamp01(t / 1.2f);

        // 영상 준비가 너무 오래 걸리면 대체 연출로
        if (_video != null && _video.enabled && !_videoPlaying && !_videoFailed && Time.unscaledTime - _prepareStartedAt > PrepareTimeout)
            FailVideo("prepare timeout");

        if (!_videoPlaying)
            UpdateFallback(t);

        if (_prompt != null)
        {
            Color c = _prompt.color;
            c.a = 0.55f + 0.45f * (0.5f + 0.5f * Mathf.Sin(t * 2.4f));
            _prompt.color = c;
        }
    }

    // ------------------------------------------------------------------
    // 영상
    // ------------------------------------------------------------------

    bool TryStartVideo(string path)
    {
        try
        {
            EnsureRenderTexture();
            if (_video == null)
            {
                _video = gameObject.AddComponent<VideoPlayer>();
                _video.playOnAwake = false;
                _video.errorReceived += HandleVideoError;
                _video.prepareCompleted += HandlePrepared;
                _audio = gameObject.AddComponent<AudioSource>();
                _audio.playOnAwake = false;
                _audio.ignoreListenerPause = true;
                _audio.spatialBlend = 0f;
            }

            _video.enabled = true;
            _video.source = VideoSource.Url;
            _video.url = path;
            _video.isLooping = true;
            _video.skipOnDrop = true;
            _video.timeUpdateMode = VideoTimeUpdateMode.UnscaledGameTime;
            _video.renderMode = VideoRenderMode.RenderTexture;
            _video.targetTexture = _rt;
            _video.aspectRatio = VideoAspectRatio.FitInside;
            _video.audioOutputMode = VideoAudioOutputMode.AudioSource;
            _video.controlledAudioTrackCount = 1;
            _video.EnableAudioTrack(0, true);
            _video.SetTargetAudioSource(0, _audio);
            _audio.volume = VideoVolume;

            _videoPlaying = false;
            _prepareStartedAt = Time.unscaledTime;
            _videoImage.texture = _rt;
            _video.Prepare();
            return true;
        }
        catch (Exception e)
        {
            FailVideo(e.Message);
            return false;
        }
    }

    void HandlePrepared(VideoPlayer source)
    {
        if (!IsShowing || source != _video || _videoFailed)
            return;

        try
        {
            _video.Play();
            _videoPlaying = true;
            _videoImage.gameObject.SetActive(true);
            _videoBack.gameObject.SetActive(true);
            SetFallbackVisible(false);
            EndCameraSway();
            // 타이틀 음악 등 다른 소리는 멈추고 영상 소리만 (영상 AudioSource 는 ignoreListenerPause)
            if (!AudioListener.pause)
            {
                AudioListener.pause = true;
                _audioPausedByUs = true;
            }
        }
        catch (Exception e)
        {
            FailVideo(e.Message);
        }
    }

    void HandleVideoError(VideoPlayer source, string message)
    {
        FailVideo(message);
    }

    void FailVideo(string reason)
    {
        if (_videoFailed)
            return;
        _videoFailed = true; // 이번 실행에서는 다시 시도하지 않는다
        Debug.LogWarning("[Kiosk] attract video unavailable, using title fallback: " + reason);
        StopVideo();
        if (IsShowing)
        {
            SetFallbackVisible(true);
            BeginCameraSway();
        }
    }

    void StopVideo()
    {
        _videoPlaying = false;
        try
        {
            if (_video != null)
            {
                _video.Stop();
                _video.enabled = false;
            }
            if (_audio != null)
                _audio.Stop();
        }
        catch (Exception)
        {
        }

        if (_audioPausedByUs)
        {
            _audioPausedByUs = false;
            AudioListener.pause = false;
        }

        if (_videoImage != null)
            _videoImage.gameObject.SetActive(false);
        if (_videoBack != null)
            _videoBack.gameObject.SetActive(false);
    }

    void EnsureRenderTexture()
    {
        if (_rt != null)
            return;
        _rt = new RenderTexture(1920, 1080, 0, RenderTextureFormat.ARGB32) { name = "KioskAttractRT" };
        _rt.Create();
    }

    void ReleaseRenderTexture()
    {
        if (_rt == null)
            return;
        _rt.Release();
        Destroy(_rt);
        _rt = null;
    }

    public static string FindVideo()
    {
        try
        {
            string explicitPath = KioskConfig.Settings.VideoPath;
            if (!string.IsNullOrEmpty(explicitPath) && File.Exists(explicitPath))
                return Path.GetFullPath(explicitPath);

            string streaming = Application.streamingAssetsPath;
            string dataDir = Application.dataPath;
            string exeDir = string.IsNullOrEmpty(dataDir) ? null : Path.GetDirectoryName(dataDir);
            string[] dirs =
            {
                Combine(streaming, "Kiosk"),
                streaming,
                Combine(exeDir, "PV"),
                Combine(exeDir, Path.Combine("..", "PV")),
#if UNITY_EDITOR
                Combine(exeDir, Path.Combine("Builds", "PV")),
#endif
            };

            for (int i = 0; i < dirs.Length; i++)
            {
                string found = PickVideo(dirs[i]);
                if (found != null)
                    return found;
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("[Kiosk] attract video search failed: " + e.Message);
        }

        return null;
    }

    static string Combine(string a, string b) => string.IsNullOrEmpty(a) ? null : Path.Combine(a, b);

    static string PickVideo(string dir)
    {
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
            return null;

        string[] files = Directory.GetFiles(dir, "*.mp4", SearchOption.TopDirectoryOnly);
        if (files.Length == 0)
            return null;

        Array.Sort(files, StringComparer.OrdinalIgnoreCase);
        for (int i = files.Length - 1; i >= 0; i--)   // 이름 역순 = 최신 버전(v2 > v1) 우선
        {
            if (Path.GetFileName(files[i]).IndexOf("web", StringComparison.OrdinalIgnoreCase) >= 0)
                return Path.GetFullPath(files[i]);
        }
        return Path.GetFullPath(files[files.Length - 1]);
    }

    // ------------------------------------------------------------------
    // 대체 연출 (영상 없음)
    // ------------------------------------------------------------------

    void SetFallbackVisible(bool visible)
    {
        if (_fallbackRoot != null)
            _fallbackRoot.gameObject.SetActive(visible);
    }

    void UpdateFallback(float t)
    {
        int index = (int)(t / CaptionInterval) % Captions.Length;
        if (index != _captionIndex)
        {
            _captionIndex = index;
            _caption.text = Captions[index];
        }

        float local = (t % CaptionInterval) / CaptionInterval;
        float fade = Mathf.Clamp01(local / 0.12f) * Mathf.Clamp01((1f - local) / 0.12f);
        Color c = _caption.color;
        c.a = fade;
        _caption.color = c;

        float breathe = 1f + 0.025f * Mathf.Sin(t * 0.6f);
        _logo.rectTransform.localScale = new Vector3(breathe, breathe, 1f);

        if (_swayCamera != null)
        {
            float yaw = 3.5f * Mathf.Sin(t * (Mathf.PI * 2f / 32f));
            float pitch = 1.2f * Mathf.Sin(t * (Mathf.PI * 2f / 47f));
            _swayCamera.transform.rotation = _swayBase * Quaternion.Euler(pitch, yaw, 0f);
        }
    }

    void BeginCameraSway()
    {
        if (_swayCamera != null)
            return;
        Camera cam = Camera.main;
        if (cam == null)
            return;
        _swayCamera = cam;
        _swayBase = cam.transform.rotation;
    }

    void EndCameraSway()
    {
        if (_swayCamera != null)
            _swayCamera.transform.rotation = _swayBase;
        _swayCamera = null;
    }

    // ------------------------------------------------------------------
    // 구성
    // ------------------------------------------------------------------

    void Build()
    {
        _canvas = KioskUi.CreateCanvas("KioskAttractCanvas", transform, SortingOrder, out _group);
        Transform root = _canvas.transform;

        _videoBack = KioskUi.CreateFullscreen("VideoBack", root, Color.black);
        _videoBack.gameObject.SetActive(false);
        GameObject videoGo = new GameObject("Video", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
        RectTransform videoRect = videoGo.GetComponent<RectTransform>();
        videoRect.SetParent(root, false);
        videoRect.anchorMin = Vector2.zero;
        videoRect.anchorMax = Vector2.one;
        videoRect.offsetMin = Vector2.zero;
        videoRect.offsetMax = Vector2.zero;
        _videoImage = videoGo.GetComponent<RawImage>();
        _videoImage.raycastTarget = false;
        AspectRatioFitter fitter = videoGo.AddComponent<AspectRatioFitter>();
        fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
        fitter.aspectRatio = 16f / 9f;
        videoGo.SetActive(false);

        _fallbackRoot = KioskUi.CreateRect("Fallback", root, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        KioskUi.CreateImage("ShadeTop", _fallbackRoot, new Color(0f, 0f, 0f, 0.45f), new Vector2(0f, 0.72f), Vector2.one, Vector2.zero, Vector2.zero);
        KioskUi.CreateImage("ShadeBottom", _fallbackRoot, new Color(0f, 0f, 0f, 0.62f), Vector2.zero, new Vector2(1f, 0.34f), Vector2.zero, Vector2.zero);
        _logo = KioskUi.CreateText("Logo", _fallbackRoot, "CHUON\n<size=40>추 온</size>", 132, FontStyle.Bold, TextAnchor.MiddleCenter, KioskUi.White,
            new Vector2(0f, 0.36f), new Vector2(1f, 0.70f), Vector2.zero, Vector2.zero);
        KioskUi.CreateImage("LogoRule", _fallbackRoot, KioskUi.Red, new Vector2(0.5f, 0.36f), new Vector2(0.5f, 0.36f), new Vector2(-90f, -2f), new Vector2(90f, 2f));
        _caption = KioskUi.CreateText("Caption", _fallbackRoot, string.Empty, 30, FontStyle.Bold, TextAnchor.MiddleCenter, KioskUi.White,
            new Vector2(0f, 0.20f), new Vector2(1f, 0.30f), Vector2.zero, Vector2.zero);
        _fallbackRoot.gameObject.SetActive(false);

        // 공통 (영상 위에도 표시)
        KioskUi.CreateText("Tag", root, "TSUKASA CORP.  //  PLAYX4 2026 DEMO", 18, FontStyle.Bold, TextAnchor.UpperLeft, KioskUi.Muted,
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(56f, -80f), new Vector2(0f, -40f));
        _prompt = KioskUi.CreateText("Prompt", root, "PRESS ANY BUTTON   ·   아무 버튼이나 누르세요", 26, FontStyle.Bold, TextAnchor.MiddleCenter, KioskUi.White,
            new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 48f), new Vector2(0f, 100f));
    }
}
