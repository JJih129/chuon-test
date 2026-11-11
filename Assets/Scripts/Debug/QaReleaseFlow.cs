#if !UNITY_EDITOR && !DEVELOPMENT_BUILD
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 반복 22: 릴리스(비개발) 플레이어 전용 최소 자동 흐름 — 출시 게이트 "TeamLogo → Credits 오류/예외 0" 을 릴리스 빌드에서도 확인.
/// 명령줄에 <c>-qaReleaseFlow</c> 가 있을 때만 동작한다 (플래그가 없으면 아무 오브젝트도 만들지 않음).
/// 개발 빌드의 -qaFlowSmoke 와 같은 경로(게임 자체 전환 훅)를 쓰되, 개발 전용 QA 도구(봇·프레임 샘플러)는 쓰지 않는다:
/// 타이틀 시작 버튼 → 부트 자동 진행 → 튜토리얼 단계 강제 완료 + 출구 → 상층 연출 시청 → 엘리베이터 → 보스 처치(코드) → 엔딩 → 크레딧.
/// 로그: "[QA-REL] ..." 줄과 마지막 "RESULT errors=N exceptions=M", 끝나면 종료.
/// </summary>
public sealed class QaReleaseFlow : MonoBehaviour
{
    const string Flag = "-qaReleaseFlow";
    static bool s_started;

    int _errors, _exceptions;
    float _start;
    readonly System.Text.StringBuilder _log = new System.Text.StringBuilder();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (s_started || System.Array.IndexOf(System.Environment.GetCommandLineArgs(), Flag) < 0)
            return;
        s_started = true;
        var go = new GameObject("__QaReleaseFlow");
        DontDestroyOnLoad(go);
        go.AddComponent<QaReleaseFlow>().StartCoroutine(go.GetComponent<QaReleaseFlow>().Run());
    }

    static string ActiveScene => SceneManager.GetActiveScene().name;

    void Note(string message)
    {
        string line = $"[QA-REL] t={Time.realtimeSinceStartup - _start:F1}s {message}";
        _log.AppendLine(line);
        Debug.Log(line);
    }

    void HandleLog(string condition, string stackTrace, LogType type)
    {
        if (type == LogType.Exception) { _exceptions++; if (_exceptions <= 8) _log.AppendLine("  EXC " + condition); }
        else if (type == LogType.Error || type == LogType.Assert) { _errors++; if (_errors <= 8) _log.AppendLine("  ERR " + condition); }
    }

    IEnumerator WaitForScene(string scene, float timeout)
    {
        float end = Time.realtimeSinceStartup + timeout;
        while (ActiveScene != scene && Time.realtimeSinceStartup < end)
            yield return null;
    }

    IEnumerator EnsureScene(string scene, float timeout)
    {
        yield return WaitForScene(scene, timeout);
        if (ActiveScene != scene)
        {
            Note($"FORCED LoadScene({scene}) (stuck in {ActiveScene})");
            GameTimeScale.ReleaseAll();
            SceneManager.LoadScene(scene, LoadSceneMode.Single);
            yield return WaitForScene(scene, 30f);
        }
        Note($"scene reached: {ActiveScene}");
    }

    IEnumerator Run()
    {
        _start = Time.realtimeSinceStartup;
        Application.logMessageReceived += HandleLog;
        Note($"release flow start (debugBuild={Debug.isDebugBuild}) scene={ActiveScene}");

        yield return EnsureScene("TitleScene", 25f);
        yield return new WaitForSecondsRealtime(2f);
        var title = FindObjectOfType<TitleSceneUIController>();
        if (title != null) { Note("title: OnClick_StartGame()"); title.OnClick_StartGame(); }
        yield return EnsureScene("BootScene", 15f);

        float bootEnd = Time.realtimeSinceStartup + 40f;
        var boot = FindObjectOfType<SystemBootLoader>();
        while (ActiveScene == "BootScene" && Time.realtimeSinceStartup < bootEnd)
        {
            if (boot != null && boot.ForceProceedForAutomation())
                Note("boot: ForceProceedForAutomation()");
            yield return new WaitForSecondsRealtime(0.5f);
        }
        yield return EnsureScene("Tutorial", 5f);

        yield return new WaitForSecondsRealtime(3f);
        float tutEnd = Time.realtimeSinceStartup + 90f;
        TutorialFlowController tutorial = null;
        int forced = 0;
        while (ActiveScene == "Tutorial" && Time.realtimeSinceStartup < tutEnd)
        {
            if (tutorial == null) tutorial = FindObjectOfType<TutorialFlowController>();
            TutorialStepDefinition step = tutorial != null ? tutorial.CurrentStep : null;
            if (step != null)
            {
                bool exitStep = step.stepType == TutorialStepType.Exit;
                tutorial.ForceCompleteCurrentStep();
                forced++;
                if (exitStep)
                {
                    yield return new WaitForSecondsRealtime(1f);
                    GameObject player = GameObject.FindWithTag("Player");
                    foreach (SceneLoadInteractable exit in FindObjectsOfType<SceneLoadInteractable>())
                    {
                        if (exit.sceneName != "Level_Facility") continue;
                        Note($"tutorial: {forced} steps force-completed, exit TryInteract -> {exit.TryInteract(player)}");
                        break;
                    }
                    yield return WaitForScene("Level_Facility", 15f);
                }
            }
            yield return new WaitForSecondsRealtime(0.6f);
        }
        yield return EnsureScene("Level_Facility", 5f);

        // 상층: 오프닝·도착 연출을 끝까지 본 뒤 바로 엘리베이터 (봇이 없으므로 상층 전투는 건너뜀)
        yield return new WaitForSecondsRealtime(4f);
        LobbyManager lobby = FindObjectOfType<LobbyManager>();
        float cineEnd = Time.realtimeSinceStartup + 80f;
        while (Time.realtimeSinceStartup < cineEnd && (CinematicDirector.IsPlaying
               || (lobby != null && lobby.CurrentPhase == LobbyManager.LobbyFlowPhase.Arrival)))
            yield return new WaitForSecondsRealtime(0.5f);
        Note("facility: upper-floor cinematics done");
        bool lowerReached = false;
        System.Action<LobbyManager> onLower = _ => lowerReached = true;
        LobbyManager.LowerFloorReached += onLower;
        lobby = FindObjectOfType<LobbyManager>();
        if (lobby != null) { Note("facility: OnInteractElevator()"); lobby.OnInteractElevator(); }
        float lowerEnd = Time.realtimeSinceStartup + 40f;
        while (!lowerReached && Time.realtimeSinceStartup < lowerEnd)
            yield return null;
        LobbyManager.LowerFloorReached -= onLower;
        Note($"facility: lower floor reached={lowerReached}");

        // 보스 리빌 연출을 보고 (최대 20 s) 처치
        float revealEnd = Time.realtimeSinceStartup + 20f;
        yield return new WaitForSecondsRealtime(3f);
        while (CinematicDirector.IsPlaying && Time.realtimeSinceStartup < revealEnd)
            yield return null;
        yield return new WaitForSecondsRealtime(3f);
        BossHealth boss = FindObjectOfType<BossHealth>();
        if (boss != null && !boss.IsDead) { Note("facility: BossHealth.Kill()"); boss.Kill(); }
        float killAt = Time.realtimeSinceStartup;
        bool CineEndingDone() => CinematicDirector.LastFinishedId == "ending_tsukasa" && CinematicDirector.LastFinishedAt >= killAt;
        while (TsukasaEndingPresenter.LastFinishedAt < killAt && !CineEndingDone() && Time.realtimeSinceStartup - killAt < 34f)
            yield return null;
        Note($"facility: ending finished={TsukasaEndingPresenter.LastFinishedAt >= killAt || CineEndingDone()} after {Time.realtimeSinceStartup - killAt:F1}s");
        float clearEnd = Time.realtimeSinceStartup + 40f;
        while (ActiveScene == "Level_Facility" && Time.realtimeSinceStartup < clearEnd)
        {
            MainSceneClearExitBridge bridge = FindObjectOfType<MainSceneClearExitBridge>();
            SceneLoadInteractable exit = bridge != null ? bridge.ActiveExitInteractable : null;
            if (exit != null && exit.TryInteract(GameObject.FindWithTag("Player")))
            {
                Note("facility: clear exit TryInteract -> true");
                yield return WaitForScene("CreditsScene", 15f);
                break;
            }
            yield return new WaitForSecondsRealtime(1f);
        }
        yield return EnsureScene("CreditsScene", 5f);

        yield return new WaitForSecondsRealtime(6f);
        Application.logMessageReceived -= HandleLog;
        Note($"RESULT errors={_errors} exceptions={_exceptions}\n--- summary ---\n{_log}");
        Application.Quit();
    }
}
#endif
