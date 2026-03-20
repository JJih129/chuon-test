using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;
using System.Collections;
using System.Text;
using DG.Tweening;
using UnityEngine.InputSystem;
using System.Collections.Generic;

/// <summary>
/// 부팅 로그를 터미널처럼 위에서 아래로 흘려보내고,
/// 모든 로그가 끝난 뒤에만 PRESS ANY KEY를 보여주며,
/// PRESS ANY KEY 위치 근처에 SYSTEM READY... N% 진행률을 함께 표시한다.
/// </summary>
public class SystemBootLoader : MonoBehaviour
{
    [Header("■ 기본 설정")]
    [Tooltip("부팅 후 이동할 다음 씬 이름")]
    public string nextSceneName = "Tutorial";

    [Tooltip("로그 한 줄과 한 줄 사이의 간격(초). 작을수록 더 빨리 내려옴")]
    public float typingSpeed = 0.02f;

    [Header("■ 로그 연출 설정")]
    [Tooltip("화면에 동시에 보이는 최대 로그 줄 수")]
    [Range(1, 30)]
    public int maxVisibleLines = 10;              // 터미널 창에 항상 보이는 줄 수(기본 10줄)

    [Header("■ UI 연결")]
    [Tooltip("로그 출력용 텍스트 (멀티라인)")]
    public TextMeshProUGUI logText;               // 로그 출력용 텍스트

    [Tooltip("진행률 % 텍스트 (상단 등)")]
    public TextMeshProUGUI progressText;          // 진행률 % 텍스트

    [Tooltip("부팅 진행 바")]
    public Slider progressBar;                    // 진행률 바

    [Tooltip("아무 키나 누르라는 메시지용 텍스트")]
    public TextMeshProUGUI pressAnyKeyText;       // PRESS ANY KEY 텍스트

    [Header("■ READY 상태 UI")]
    [Tooltip("READY 퍼센트를 보여줄 텍스트 (PRESS ANY KEY 근처에 배치)")]
    public TextMeshProUGUI readyStatusText;       // SYSTEM READY... N% 표시용 텍스트

    [Header("■ 부팅 로그 내용 (길게 넣어도 됨)")]
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

    // 내부 상태
    private string finalLogState;                        // 전체 로그 텍스트(필요 시 재사용)
    private readonly Queue<string> visibleLines = new Queue<string>(); // 화면에 보이는 로그 버퍼
    private AsyncOperation pendingSceneLoad;
    private bool awaitingPlayerInput;
    private bool forceContinueRequested;

    public bool IsAwaitingPlayerInput => awaitingPlayerInput;

    void Awake()
    {
        // PRESS ANY KEY는 시작 시 무조건 숨김
        if (pressAnyKeyText != null)
        {
            pressAnyKeyText.gameObject.SetActive(false);

            // 알파 초기화(혹시 이전 트윈 영향 제거)
            Color c = pressAnyKeyText.color;
            c.a = 1f;
            pressAnyKeyText.color = c;
        }

        // READY 퍼센트 텍스트 초기화
        if (readyStatusText != null)
        {
            readyStatusText.text = string.Empty;
        }

        // 로그 텍스트 초기화
        if (logText != null)
            logText.text = string.Empty;
    }

    void Start()
    {
        StartCoroutine(BootSequence());
    }

    IEnumerator BootSequence()
    {
        // 1. 비동기 씬 로딩 시작 (자동 전환은 막아둠)
        AsyncOperation op = SceneManager.LoadSceneAsync(nextSceneName);
        op.allowSceneActivation = false;
        pendingSceneLoad = op;
        forceContinueRequested = false;

        StringBuilder fullLogBuilder = new StringBuilder();
        int totalLogs = bootLogs.Length;

        visibleLines.Clear();

        // 2. 로그를 한 줄씩 위에서 아래로 "내려오듯" 출력
        for (int i = 0; i < totalLogs; i++)
        {
            string line = bootLogs[i];

            // 전체 로그에 누적 (필요하다면 나중에 전체 로그 표시용)
            fullLogBuilder.AppendLine(line);
            finalLogState = fullLogBuilder.ToString();

            // 화면에 보이는 큐에 추가
            visibleLines.Enqueue(line);
            // maxVisibleLines보다 많아지면 가장 오래된 줄부터 제거 → 터미널에서 위로 밀리는 느낌
            while (visibleLines.Count > maxVisibleLines)
            {
                visibleLines.Dequeue();
            }

            if (logText != null)
            {
                // 큐에 들어있는 것만 다시 이어 붙여서 보여줌
                logText.text = string.Join("\n", visibleLines.ToArray());
            }

            // 로그 진행도 (0~1)
            float progress = (float)(i + 1) / totalLogs;

            // 상단 진행률 UI 갱신
            if (progressBar != null)
                progressBar.value = progress;

            if (progressText != null)
                progressText.text = $"SYSTEM BOOT... {(progress * 100f):0}%";

            // PRESS ANY KEY 자리 근처 READY 퍼센트 표시
            if (readyStatusText != null)
            {
                // 로그 진행도에 맞춰 SYSTEM READY... N% 갱신
                readyStatusText.text = $"SYSTEM READY... {(progress * 100f):0}%";
            }

            // 줄 간 간격(랜덤 약간 섞어서 기계 로그 느낌)
            yield return new WaitForSeconds(Random.Range(typingSpeed * 0.5f, typingSpeed * 1.5f));
        }

        // 3. 완료 상태 표시 (100%)
        if (progressText != null)
            progressText.text = "SYSTEM BOOT... 100%";

        if (progressBar != null)
            progressBar.value = 1.0f;

        if (readyStatusText != null)
            readyStatusText.text = "SYSTEM READY... 100%";

        // 4. 실제 로딩이 끝날 때까지 대기 (씬은 아직 전환하지 않음)
        while (op.progress < 0.9f)
        {
            yield return null;
        }

        // 5. 모든 로그가 끝난 후에만 PRESS ANY KEY 등장 + 입력 대기
        yield return StartCoroutine(WaitForPlayerInputAndProceed(op));
    }

    /// <summary>
    /// 새 입력 시스템/구 입력 시스템 둘 다에서 "아무 키" 입력 감지.
    /// </summary>
    private bool IsAnyKeyPressedThisFrame()
    {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame)
            return true;
#endif
        return Input.anyKeyDown;
    }

    // 아무 키 입력 대기 로직
    IEnumerator WaitForPlayerInputAndProceed(AsyncOperation op)
    {
        // 인스펙터에서 pressAnyKeyText 연결 안 되어 있으면
        // 자동으로 넘어갈 수밖에 없음 → 반드시 할당해 둘 것.
        if (pressAnyKeyText == null)
        {
            yield return new WaitForSeconds(1.0f);
            awaitingPlayerInput = false;
            pendingSceneLoad = null;
            op.allowSceneActivation = true;
            yield break;
        }

        // 혹시 이전 키 입력이 남아있을 수 있으니 짧게 버퍼 플러시
        float flushTime = 0.15f;
        while (flushTime > 0f)
        {
            flushTime -= Time.unscaledDeltaTime;
            yield return null;
        }

        // 여기서부터 PRESS ANY KEY 표시 (로그 + 로딩 완료 후)
        awaitingPlayerInput = true;
        pressAnyKeyText.gameObject.SetActive(true);
        pressAnyKeyText.text = "PRESS ANY KEY . . .";

        // 점멸(알파 1 ↔ 0.3) 반복
        Tween blinkTween = pressAnyKeyText
            .DOFade(0.3f, 0.5f)
            .SetLoops(-1, LoopType.Yoyo)
            .SetUpdate(true); // timeScale 영향 안 받게

        // READY 퍼센트는 이미 100%로 고정된 상태에서,
        // 플레이어 실제 입력을 기다림.
        while (!IsAnyKeyPressedThisFrame() && !forceContinueRequested)
        {
            yield return null;
        }

        // 점멸 종료 및 숨김
        blinkTween.Kill();
        pressAnyKeyText.gameObject.SetActive(false);
        awaitingPlayerInput = false;
        forceContinueRequested = false;

        // 6. 페이드 아웃 후 씬 활성화
        if (SceneFader.Instance != null && SceneFader.Instance.fadeCanvasGroup != null)
        {
            SceneFader.Instance.fadeCanvasGroup.blocksRaycasts = true;
            SceneFader.Instance.fadeCanvasGroup
                .DOFade(1f, 1.0f)
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
