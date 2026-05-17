using System.Collections;
using System.Collections.Generic;
using System.Text;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class SystemBootLoader : MonoBehaviour
{
    [Header("Boot")]
    public string nextSceneName = "Tutorial";
    public float typingSpeed = 0.02f;
    [SerializeField] private bool useFastBootMode = true;
    [SerializeField, Min(0.1f)] private float fastBootMinDuration = 4.2f;
    [SerializeField, Min(0.1f)] private float fastBootMaxDuration = 4.8f;
    [SerializeField, Min(0.1f)] private float minimumBootSequenceDuration = 4.5f;
    [SerializeField, Min(0f)] private float autoProceedDelayWhenNoPrompt = 0.15f;
    [SerializeField, Min(0f)] private float sceneActivationFadeDuration = 0.2f;
    [SerializeField, Min(0)] private int visibleFramesBeforeAsyncLoad = 2;
    [SerializeField, Range(0.016f, 0.2f)] private float maxBootSequenceDelta = 0.05f;

    [Header("Log Display")]
    [Range(1, 30)] public int maxVisibleLines = 10;

    [Header("UI")]
    public TextMeshProUGUI logText;
    public TextMeshProUGUI progressText;
    public Slider progressBar;
    public TextMeshProUGUI pressAnyKeyText;
    public TextMeshProUGUI readyStatusText;

    [Header("Boot Logs")]
    [TextArea(2, 10)]
    public string[] bootLogs = new string[]
    {
        "[0x000000] FIRMWARE_DIAGNOSTICS :: Verifying critical signatures...",
        "[0x00000F] BOOT_VECTOR :: Redirecting control to secure block...",
        "[0x00001A] POWER_MATRIX :: Initializing fusion cells... [STABLE]",
        "[0x00002B] POWER_MATRIX :: Load-balancing fusion output channels...",
        "[0x00003C] SENSOR_ARRAY :: Calibrating optic sensors... [OK]",
        "[0x00004F] CORE_RAM_CHECK :: Mapping virtual memory banks... [OK]",
        "[0x000061] CORE_RAM_CHECK :: Zeroing unsafe regions... [OK]",
        "> Loading Kernel: E.G.O_OS_v4.9.223_rev14 (Build Date: 2077-11-20)",
        "[0x000071] KERNEL_SECURITY :: Enabling low-level safeguards...",
        "[0x000082] KERNEL_SECURITY :: Installing patchset SIG-2077-11...",
        "[0x00009C] IO_BUS :: Linking peripheral bridges... [OK]",
        "[0x0000B4] IO_BUS :: High-priority channel reserved for operator I/O.",
        "[0x0000D2] CLOCK_SYNC :: Aligning local time source with quantum beacon...",
        "[0x0000E8] CLOCK_SYNC :: Drift under 0.0001ms threshold. [LOCKED]",
        "[0x00012C] DRIVER_LOAD :: Synaptic_Input_Interface... [SUCCESS]",
        "[0x000151] DRIVER_LOAD :: Holo_UI_Overlay v3.1... [SUCCESS]",
        "[0x00016A] NET_STACK :: Establishing encrypted uplink... [OK]",
        "[0x00018F] NET_STACK :: Latency profiling... avg=3.7ms [STABLE]",
        "[0x0001B0] AUDIO_MATRIX :: Routing neural feedback channels...",
        "[0x0001C8] AUDIO_MATRIX :: Dynamic limiter online. [OK]",
        "[0x0001F0] STORAGE_ARRAY :: Mounting cognitive archives... [OK]",
        "[0x000207] STORAGE_ARRAY :: Deep integrity scan... [CLEAN]",
        "[0x00021F] COGNITO_LINK :: Scanning operator biometrics... [VERIFIED]",
        "[0x00023A] COGNITO_LINK :: Matching profile: CODE_NAME :: \"E.G.O\"",
        ">>> INITIATING NEURAL LINK SYNCHRONIZATION SEQUENCE <<<",
        "[0x000260] NEURAL_SYNC :: Phase 1/3 - Baseline alignment...",
        "[0x00028E] NEURAL_SYNC :: Phase 2/3 - Memory lattice hand-shake...",
        "[0x0002A9] NEURAL_SYNC :: Phase 3/3 - Emotional filter negotiation...",
        "ALERT: Emotional Inhibitor chip not detected. Switching to Manual Override Mode... [WARNING]",
        "[0x0002F0] FAILSAFE :: Updating risk profile... [ELEVATED]",
        "[0x00034A] RENDER_PIPE :: Allocating VRAM heaps... [OK]",
        "[0x00037C] RENDER_PIPE :: Compiling core shader bundles...",
        "[0x0003C2] RENDER_PIPE :: Shader warm-up pass complete. [READY]",
        "[0x0008A0] OPTIMIZATION :: Pre-caching pathfinding graphs... [COMPLETED]",
        "[0x0008F4] OPTIMIZATION :: Pre-caching shader pipelines... [COMPLETED]",
        "SYSTEM CHECK: All subsystems nominal.",
        ">>> ACCESS GRANTED. WELCOME BACK, OPERATOR.",
        "SYSTEM READY."
    };

    private string finalLogState;
    private readonly Queue<string> visibleLines = new Queue<string>();
    private AsyncOperation pendingSceneLoad;
    private bool awaitingPlayerInput;
    private bool forceContinueRequested;
    private float _bootSequenceStartedAt;

    public bool IsAwaitingPlayerInput => awaitingPlayerInput;

    void Awake()
    {
        ConfigureResponsiveUi();

        if (pressAnyKeyText != null)
        {
            if (pressAnyKeyText != progressText)
                pressAnyKeyText.gameObject.SetActive(false);
            Color c = pressAnyKeyText.color;
            c.a = 1f;
            pressAnyKeyText.color = c;
        }

        if (readyStatusText != null)
            readyStatusText.text = string.Empty;

        if (logText != null)
            logText.text = string.Empty;

        RefreshBootProgressUi(0, bootLogs != null ? bootLogs.Length : 0);
    }

    void ConfigureResponsiveUi()
    {
        var canvas = GetComponentInParent<Canvas>();
        if (canvas == null)
            return;

        var scaler = canvas.GetComponent<CanvasScaler>();
        if (scaler != null)
        {
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
        }

        ConfigureLogRect();
        ConfigureProgressBarRect();
        ConfigureProgressTextRect();
        ConfigureReadyStatusRect();
    }

    void ConfigureLogRect()
    {
        if (logText == null)
            return;

        var rect = logText.rectTransform;
        rect.anchorMin = new Vector2(0.06f, 0.58f);
        rect.anchorMax = new Vector2(0.58f, 0.94f);
        rect.pivot = new Vector2(0f, 1f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    void ConfigureProgressBarRect()
    {
        if (progressBar == null)
            return;

        var rect = progressBar.transform as RectTransform;
        if (rect == null)
            return;

        rect.anchorMin = new Vector2(0.08f, 0.10f);
        rect.anchorMax = new Vector2(0.92f, 0.18f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    void ConfigureProgressTextRect()
    {
        if (progressText == null)
            return;

        var rect = progressText.rectTransform;
        rect.anchorMin = new Vector2(0.08f, 0.20f);
        rect.anchorMax = new Vector2(0.92f, 0.28f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        progressText.alignment = TextAlignmentOptions.Center;
    }

    void ConfigureReadyStatusRect()
    {
        if (readyStatusText == null)
            return;

        var rect = readyStatusText.rectTransform;
        rect.anchorMin = new Vector2(0.08f, 0.30f);
        rect.anchorMax = new Vector2(0.92f, 0.36f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        readyStatusText.alignment = TextAlignmentOptions.Center;
    }

    void Start()
    {
        StartCoroutine(BootSequence());
    }

    IEnumerator BootSequence()
    {
        int totalLogs = bootLogs != null ? bootLogs.Length : 0;
        visibleLines.Clear();
        RefreshBootProgressUi(0, totalLogs);

        int warmupFrames = Mathf.Max(0, visibleFramesBeforeAsyncLoad);
        for (int i = 0; i < warmupFrames; i++)
            yield return null;

        _bootSequenceStartedAt = Time.unscaledTime;
        float bootVisibleElapsed = 0f;

        AsyncOperation op = SceneManager.LoadSceneAsync(nextSceneName);
        op.allowSceneActivation = false;
        pendingSceneLoad = op;
        forceContinueRequested = false;

        StringBuilder fullLogBuilder = new StringBuilder();
        float revealDuration = useFastBootMode
            ? Mathf.Clamp(Mathf.Max(typingSpeed, 0.001f) * Mathf.Max(1, totalLogs), fastBootMinDuration, fastBootMaxDuration)
            : Mathf.Max(typingSpeed, 0.001f) * Mathf.Max(1, totalLogs);

        float elapsed = 0f;
        int revealedCount = 0;

        while (revealedCount < totalLogs)
        {
            float stepDelta = GetBootSequenceDelta();
            elapsed += stepDelta;
            bootVisibleElapsed += stepDelta;
            float normalized = revealDuration <= 0.0001f ? 1f : Mathf.Clamp01(elapsed / revealDuration);
            int targetRevealCount = Mathf.Clamp(Mathf.CeilToInt(normalized * totalLogs), 1, totalLogs);

            while (revealedCount < targetRevealCount)
            {
                string line = bootLogs[revealedCount];
                fullLogBuilder.AppendLine(line);
                finalLogState = fullLogBuilder.ToString();

                visibleLines.Enqueue(line);
                while (visibleLines.Count > maxVisibleLines)
                    visibleLines.Dequeue();

                revealedCount++;
            }

            RefreshBootProgressUi(revealedCount, totalLogs);
            yield return null;
        }

        RefreshBootProgressUi(totalLogs, totalLogs);

        while (op.progress < 0.9f)
            yield return null;

        while (bootVisibleElapsed < minimumBootSequenceDuration)
        {
            bootVisibleElapsed += GetBootSequenceDelta();
            yield return null;
        }

        yield return StartCoroutine(WaitForPlayerInputAndProceed(op));
    }

    float GetBootSequenceDelta()
    {
        return Mathf.Min(Time.unscaledDeltaTime, Mathf.Max(0.001f, maxBootSequenceDelta));
    }

    void RefreshBootProgressUi(int revealedCount, int totalLogs)
    {
        float progress = totalLogs > 0 ? Mathf.Clamp01((float)revealedCount / totalLogs) : 1f;

        if (logText != null)
            logText.text = string.Join("\n", visibleLines.ToArray());

        if (progressBar != null)
            progressBar.value = progress;

        if (progressText != null)
            progressText.text = $"SYSTEM BOOT... {(progress * 100f):0}%";

        if (readyStatusText != null)
            readyStatusText.text = $"SYSTEM READY... {(progress * 100f):0}%";
    }

    private bool IsAnyKeyPressedThisFrame()
    {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame)
            return true;
#endif
        return Input.anyKeyDown;
    }

    IEnumerator WaitForPlayerInputAndProceed(AsyncOperation op)
    {
        if (pressAnyKeyText == null)
        {
            float delay = Mathf.Max(0f, autoProceedDelayWhenNoPrompt);
            while (delay > 0f)
            {
                delay -= Time.unscaledDeltaTime;
                yield return null;
            }

            awaitingPlayerInput = false;
            pendingSceneLoad = null;
            op.allowSceneActivation = true;
            yield break;
        }

        float flushTime = 0.15f;
        while (flushTime > 0f)
        {
            flushTime -= Time.unscaledDeltaTime;
            yield return null;
        }

        awaitingPlayerInput = true;
        if (pressAnyKeyText != progressText)
            pressAnyKeyText.gameObject.SetActive(true);
        pressAnyKeyText.text = "PRESS ANY KEY . . .";

        Tween blinkTween = pressAnyKeyText
            .DOFade(0.3f, 0.5f)
            .SetLoops(-1, LoopType.Yoyo)
            .SetUpdate(true);

        while (!IsAnyKeyPressedThisFrame() && !forceContinueRequested)
            yield return null;

        blinkTween.Kill();
        if (pressAnyKeyText != progressText)
            pressAnyKeyText.gameObject.SetActive(false);
        awaitingPlayerInput = false;
        forceContinueRequested = false;

        if (SceneFader.Instance != null && SceneFader.Instance.fadeCanvasGroup != null)
        {
            SceneFader.Instance.fadeCanvasGroup.gameObject.SetActive(true);
            SceneFader.Instance.fadeCanvasGroup.alpha = 0f;
            SceneFader.Instance.fadeCanvasGroup.blocksRaycasts = true;
            SceneFader.Instance.fadeCanvasGroup.interactable = true;
            SceneFader.Instance.fadeCanvasGroup
                .DOFade(1f, Mathf.Max(0.01f, sceneActivationFadeDuration))
                .OnComplete(() =>
                {
                    pendingSceneLoad = null;
                    op.allowSceneActivation = true;
                });
        }
        else
        {
            pendingSceneLoad = null;
            op.allowSceneActivation = true;
        }
    }

    public bool ForceProceedForAutomation()
    {
        if (!awaitingPlayerInput || pendingSceneLoad == null)
            return false;

        forceContinueRequested = true;
        return true;
    }
}
