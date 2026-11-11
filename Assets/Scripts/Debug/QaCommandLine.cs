#if DEVELOPMENT_BUILD && !UNITY_EDITOR
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem.LowLevel;

/// <summary>
/// Development-player-only QA entry point (never compiled into the editor or release builds).
/// Lets an automated pass drive a dev build from the command line, e.g.
///   ChuOn.exe -logFile p.log -qaScene Level_Facility -qaSkipToBoss -qaBot 0.9 -qaDuration 60 -qaQuit
/// -qaScene X      load scene X right after the first scene
/// -qaSkipToBoss   checkpoint-skip the upper floor and the arrival intro (boss fight starts at once)
/// -qaBot S        attach QaCombatBot with defense skill S after -qaBotDelay seconds (default 5)
/// -qaDuration D   after D seconds log QaFrameSampler.Report() + bot summary ("[QA] ..." lines in Player.log)
/// -qaQuit         quit after the report
/// -qaFullRun      Level_Facility from the top (no skip): the bot walks the upper floor (teach android -> drones ->
///                 elevator guards + turret -> elevator) and then fights the boss. Logs "[QA-RUN] ..." phase times, a per-mob
///                 report when the lower floor is reached, and the usual frame/bot report after -qaDuration.
/// -qaDialogue     show the story dialogue lines (ExhibitionPrototypePresentationPolicy keeps them off by default)
/// -qaOcclusionShot  before the bot: put a wall (an existing URP/Lit scene material) between camera and player and
///                 capture qa_occl_off.png (fader disabled) / qa_occl_on.png (fader enabled) next to the player log
/// -qaFlowSmoke    end-to-end smoke from the first scene: TeamLogo -> Title -> Boot -> Tutorial -> Level_Facility -> Credits.
///                 Uses the game's own transitions (title start button, boot "press any key" automation hook, tutorial
///                 exit interactable, elevator handoff, boss clear exit) and force-completes tutorial steps / kills the
///                 boss by code. Logs "[QA-FLOW] ..." per scene, a FORCED line whenever a fallback LoadScene was needed,
///                 and the error/exception count at the end; quits afterwards (no -qaScene needed).
/// -qaPad          (iter 22) the bot plays through a virtual Input System gamepad (QaPadInput) instead of direct calls; with
///                 -qaFlowSmoke the title/settings/credits/boot/pause/credits-skip menus are also driven by pad taps, with a
///                 "[QA-PAD]" line per step and an on-screen glyph audit (keyboard tokens left on screen in pad scheme).
///                 In bot runs a death overlay is answered with pad South (restart).
/// -qaAudioMeter   (iter 22) QaAudioMeter: per-second output RMS/peak (Temp/qa/audio_meter.txt) + "[QA-AUDIO]" report at the end.
/// </summary>
public sealed class QaCommandLine : MonoBehaviour
{
    static bool s_consumed;

    string _scene;
    bool _skipToBoss;
    float _botSkill = -1f;
    float _botDelay = 5f;
    float _duration = 60f;
    bool _quit;
    bool _occlusionShot;
    bool _fullRun;
    bool _tutorialRun;
    bool _pad;
    bool _audioMeter;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (s_consumed || !Debug.isDebugBuild)
            return;

        string[] args = System.Environment.GetCommandLineArgs();
        if (System.Array.IndexOf(args, "-qaDialogue") >= 0)
            ExhibitionPrototypePresentationPolicy.ForceDialogueForQa = true;
        if (System.Array.IndexOf(args, "-qaFlowSmoke") >= 0)
        {
            s_consumed = true;
            QaCinematicWatch.Ensure(); // 반복 14C: 연출 시청 시간·프레임 ("[QA-CINE]")
            QaErrorShaderWatch.Ensure(); // 반복 15: 자홍(에러 셰이더) 렌더러 감시 ("[QA-SHADER]")
            var flow = new GameObject("__QaFlowSmoke");
            DontDestroyOnLoad(flow);
            QaCommandLine flowRunner = flow.AddComponent<QaCommandLine>();
            flowRunner.Parse(args);
            flowRunner.StartCoroutine(flowRunner.RunFlowSmoke());
            return;
        }

        if (System.Array.IndexOf(args, "-qaScene") < 0)
            return;

        s_consumed = true;
        QaCinematicWatch.Ensure();
        QaErrorShaderWatch.Ensure();
        var go = new GameObject("__QaCommandLine");
        DontDestroyOnLoad(go);
        QaCommandLine runner = go.AddComponent<QaCommandLine>();
        runner.Parse(args);
        runner.StartCoroutine(runner.Run());
    }

    void Parse(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            string next = i + 1 < args.Length ? args[i + 1] : null;
            switch (args[i])
            {
                case "-qaScene": _scene = next; break;
                case "-qaSkipToBoss": _skipToBoss = true; break;
                case "-qaBot": float.TryParse(next, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _botSkill); break;
                case "-qaBotDelay": float.TryParse(next, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _botDelay); break;
                case "-qaDuration": float.TryParse(next, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _duration); break;
                case "-qaQuit": _quit = true; break;
                case "-qaOcclusionShot": _occlusionShot = true; break;
                case "-qaFullRun": _fullRun = true; break;
                case "-qaTutorialRun": _tutorialRun = true; break;
                case "-qaPad": _pad = true; QaCombatBot.UsePad = true; break;
                case "-qaAudioMeter": _audioMeter = true; QaAudioMeter.Ensure(); break;
                case "-qaDifficulty": // easy|normal|hard (저장하지 않음)
                    if (next != null && System.Enum.TryParse(next, true, out BossDifficultyTier tier))
                        GameDifficulty.Set(tier, save: false);
                    break;
            }
        }
    }

    // ---------------- -qaFlowSmoke ----------------
    int _errors, _exceptions;
    readonly System.Text.StringBuilder _flowLog = new System.Text.StringBuilder();
    float _flowStart;

    void Flow(string message)
    {
        string line = $"[QA-FLOW] t={Time.realtimeSinceStartup - _flowStart:F1}s {message}";
        _flowLog.AppendLine(line);
        Debug.Log(line);
    }

    void HandleLog(string condition, string stackTrace, LogType type)
    {
        if (type == LogType.Exception) { _exceptions++; if (_exceptions <= 5) _flowLog.AppendLine("  EXC " + condition); }
        else if (type == LogType.Error || type == LogType.Assert) { _errors++; if (_errors <= 5) _flowLog.AppendLine("  ERR " + condition); }
    }

    static string ActiveScene => SceneManager.GetActiveScene().name;

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
            Flow($"FORCED LoadScene({scene}) (stuck in {ActiveScene})");
            GameTimeScale.ReleaseAll();
            SceneManager.LoadScene(scene, LoadSceneMode.Single);
            yield return WaitForScene(scene, 30f);
        }

        Flow($"scene reached: {ActiveScene}");
    }

    IEnumerator RunFlowSmoke()
    {
        _flowStart = Time.realtimeSinceStartup;
        Application.logMessageReceived += HandleLog;

        // 1) TeamLogo: auto splash
        Flow($"scene reached: {ActiveScene}");
        yield return EnsureScene("TitleScene", 25f);

        // 2) Title: the real start button handler
        yield return new WaitForSecondsRealtime(2f);
        var title = FindObjectOfType<TitleSceneUIController>();
        if (_pad)
            yield return PadTitle();
        if (ActiveScene == "TitleScene" && title != null) { Flow("title: OnClick_StartGame()" + (_pad ? " (PAD-FAIL fallback)" : "")); title.OnClick_StartGame(); }
        yield return EnsureScene("BootScene", 15f);

        // 3) Boot: wait for the "press any key" prompt, then the automation hook
        float bootEnd = Time.realtimeSinceStartup + 40f;
        var boot = FindObjectOfType<SystemBootLoader>();
        if (_pad)
        {
            // 부트 "아무 키나": 패드 South 탭만으로 넘어가는지 (30 s 안에 못 넘어가면 기존 자동화 훅)
            float padBootEnd = Time.realtimeSinceStartup + 30f;
            int bootTaps = 0;
            while (ActiveScene == "BootScene" && Time.realtimeSinceStartup < padBootEnd)
            {
                QaPadInput.Tap(GamepadButton.South); bootTaps++;
                yield return new WaitForSecondsRealtime(0.7f);
            }
            PadLog($"boot: {bootTaps} South taps -> {(ActiveScene != "BootScene" ? "left boot" : "PAD-FAIL still in boot")}");
        }
        while (ActiveScene == "BootScene" && Time.realtimeSinceStartup < bootEnd)
        {
            if (boot != null && boot.ForceProceedForAutomation())
                Flow("boot: ForceProceedForAutomation()");
            yield return new WaitForSecondsRealtime(0.5f);
        }
        yield return EnsureScene("Tutorial", 5f);

        // 4) Tutorial: force-complete steps until the exit step, then use the exit interactable
        yield return new WaitForSecondsRealtime(3f);
        float tutEnd = Time.realtimeSinceStartup + 90f;
        TutorialFlowController tutorial = FindObjectOfType<TutorialFlowController>();
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
                        Flow($"tutorial: {forced} steps force-completed, exit TryInteract -> {exit.TryInteract(player)}");
                        break;
                    }
                    yield return WaitForScene("Level_Facility", 15f);
                }
            }
            yield return new WaitForSecondsRealtime(0.6f);
        }
        yield return EnsureScene("Level_Facility", 5f);

        // 5) Level_Facility: upper floor with the bot (drones), elevator handoff, boss with the bot, clear exit
        yield return new WaitForSecondsRealtime(4f);
        QaFrameSampler.Clear();
        QaFrameSampler.Mark("facility_upper");
        QaCombatBot bot = QaCombatBot.Attach(0.9f);
        bot.captureScreenshots = false;
        // 반복 14C: 오프닝(30 s)·도착 대사·경보 리빌(9.5 s)을 건너뛰지 않고 본다 → 전투 단계가 시작될 때까지 기다린다 (최대 80 s).
        LobbyManager lobbyForCine = FindObjectOfType<LobbyManager>();
        float cineEnd = Time.realtimeSinceStartup + 80f;
        while (Time.realtimeSinceStartup < cineEnd && (CinematicDirector.IsPlaying
               || (lobbyForCine != null && lobbyForCine.CurrentPhase == LobbyManager.LobbyFlowPhase.Arrival)))
            yield return new WaitForSecondsRealtime(0.5f);
        Flow($"facility: upper-floor cinematics done ({QaCinematicWatch.Count} played so far)");
        yield return new WaitForSecondsRealtime(12f);
        // Let the bot finish the upper-floor drone fight like a player would (bounded), then take the elevator.
        LobbyManager lobbyForCombat = FindObjectOfType<LobbyManager>();
        float combatEnd = Time.realtimeSinceStartup + 45f;
        while (lobbyForCombat != null && lobbyForCombat.CurrentPhase == LobbyManager.LobbyFlowPhase.Combat && Time.realtimeSinceStartup < combatEnd)
            yield return new WaitForSecondsRealtime(0.5f);
        Flow($"facility: upper phase={(lobbyForCombat != null ? lobbyForCombat.CurrentPhase.ToString() : "?")} before elevator");
        bool lowerReached = false;
        System.Action<LobbyManager> onLower = _ => lowerReached = true;
        LobbyManager.LowerFloorReached += onLower;
        LobbyManager lobby = FindObjectOfType<LobbyManager>();
        if (lobby != null) { Flow("facility: OnInteractElevator()"); lobby.OnInteractElevator(); }
        float lowerEnd = Time.realtimeSinceStartup + 40f; // 반복 14C: 하강 연출 13 s 포함
        while (!lowerReached && Time.realtimeSinceStartup < lowerEnd)
            yield return null;
        LobbyManager.LowerFloorReached -= onLower;
        Flow($"facility: lower floor reached={lowerReached}");
        QaFrameSampler.Mark("facility_boss");
        // Arrival -> control: when does player input unblock, and when does the bot first attack?
        float lowerAt = Time.realtimeSinceStartup;
        IInputBlocker blocker = null;
        GameObject flowPlayer = GameObject.FindWithTag("Player");
        if (flowPlayer != null)
            foreach (MonoBehaviour mb in flowPlayer.GetComponentsInChildren<MonoBehaviour>(true))
                if (mb is IInputBlocker ib) { blocker = ib; break; }
        float controlAt = -1f, firstAttackAt = -1f;
        bool seenBlocked = false;
        int attacksAtLower = bot != null ? bot.AttackCount : 0;
        // 반복 19: Type-D 리빌(MainSceneIntroDirector) 이 실제로 재생됐는지 — 씬 참조가 빠지면 조용히 건너뛰므로 로그로 확인
        GameObject introGo = GameObject.Find("MainSceneIntroDirector");
        UnityEngine.Playables.PlayableDirector intro = introGo != null ? introGo.GetComponent<UnityEngine.Playables.PlayableDirector>() : null;
        double introMaxTime = -1d;
        while (Time.realtimeSinceStartup - lowerAt < 25f)
        {
            if (intro != null && intro.state == UnityEngine.Playables.PlayState.Playing && intro.time > introMaxTime)
                introMaxTime = intro.time;
            bool blocked = blocker != null && blocker.IsBlocked;
            seenBlocked |= blocked;
            if (controlAt < 0f && !blocked && (seenBlocked || Time.realtimeSinceStartup - lowerAt > 3f))
                controlAt = Time.realtimeSinceStartup - lowerAt;
            if (firstAttackAt < 0f && bot != null && bot.AttackCount > attacksAtLower)
                firstAttackAt = Time.realtimeSinceStartup - lowerAt;
            yield return null;
        }
        Flow($"facility: arrival control at +{controlAt:F1}s, bot first attack at +{firstAttackAt:F1}s after LowerFloorReached");
        if (_pad)
            yield return PadPauseCheck();
        Flow($"facility: type-d reveal director={(intro != null)} asset={(intro != null && intro.playableAsset != null ? intro.playableAsset.name : "none")} played={(introMaxTime >= 0d)} reached={introMaxTime:F2}s");
        BossHealth boss = FindObjectOfType<BossHealth>();
        Flow($"facility: bot {(bot != null ? bot.Summary : "gone")}");
        if (boss != null && !boss.IsDead) { Flow("facility: BossHealth.Kill()"); boss.Kill(); }
        // Ending: Tsukasa hologram must play right after Type-D falls (let it run unskipped, bounded).
        float killAt = Time.realtimeSinceStartup;
        int endingPlaysBefore = TsukasaEndingPresenter.PlayCount;
        // 반복 14C: 연출 경로(ending_tsukasa, 24 s) 또는 기존 오버레이
        bool CineEndingDone() => CinematicDirector.LastFinishedId == "ending_tsukasa" && CinematicDirector.LastFinishedAt >= killAt;
        while (TsukasaEndingPresenter.LastFinishedAt < killAt && !CineEndingDone() && Time.realtimeSinceStartup - killAt < 34f)
            yield return null;
        Flow($"facility: tsukasa ending overlayStarted={TsukasaEndingPresenter.PlayCount > endingPlaysBefore} cinematicDone={CineEndingDone()} finished={TsukasaEndingPresenter.LastFinishedAt >= killAt || CineEndingDone()} after {Time.realtimeSinceStartup - killAt:F1}s");
        float clearEnd = Time.realtimeSinceStartup + 40f;
        while (ActiveScene == "Level_Facility" && Time.realtimeSinceStartup < clearEnd)
        {
            MainSceneClearExitBridge bridge = FindObjectOfType<MainSceneClearExitBridge>();
            SceneLoadInteractable exit = bridge != null ? bridge.ActiveExitInteractable : null;
            if (exit != null && exit.TryInteract(GameObject.FindWithTag("Player")))
            {
                Flow("facility: clear exit TryInteract -> true");
                yield return WaitForScene("CreditsScene", 15f);
                break;
            }
            yield return new WaitForSecondsRealtime(1f);
        }
        yield return EnsureScene("CreditsScene", 5f);

        // 6) Credits
        yield return new WaitForSecondsRealtime(6f);
        if (_pad)
        {
            PadLog("credits: " + GlyphAudit("credits"));
            for (int i = 0; i < 3 && ActiveScene == "CreditsScene"; i++)
            {
                QaPadInput.Tap(GamepadButton.South);
                yield return new WaitForSecondsRealtime(4f);
            }
            PadLog($"credits skip by pad South -> {(ActiveScene != "CreditsScene" ? "left credits (" + ActiveScene + ")" : "PAD-FAIL still in credits")}");
        }
        Flow("credits ok; frame report:\n" + QaFrameSampler.Report());
        Flow("cinematics:\n" + QaCinematicWatch.Report());
        Flow(QaErrorShaderWatch.Report());
        if (_audioMeter) Flow(QaAudioMeter.ReportNow());
        Application.logMessageReceived -= HandleLog;
        Flow($"RESULT errors={_errors} exceptions={_exceptions}\n--- summary ---\n{_flowLog}");
        Application.Quit();
    }

    // Build check for the ShaderVariantCollection: CameraOcclusionFader switches URP/Lit to _SURFACE_TYPE_TRANSPARENT at
    // runtime; if that shader_feature variant were stripped the wall would stay opaque in qa_occl_on.png.
    IEnumerator OcclusionShot()
    {
        Camera cam = Camera.main;
        GameObject player = GameObject.FindWithTag("Player");
        CameraOcclusionFader fader = FindObjectOfType<CameraOcclusionFader>();
        Material wallMaterial = null;
        foreach (MeshRenderer r in FindObjectsOfType<MeshRenderer>())
        {
            Material m = r.sharedMaterial;
            if (m != null && m.shader != null && m.shader.name == "Universal Render Pipeline/Lit" && m.GetFloat("_Surface") < 0.5f && r.bounds.size.y > 2f)
            { wallMaterial = m; break; }
        }

        if (cam == null || player == null || fader == null || wallMaterial == null)
        {
            Debug.Log($"[QA] occlusion shot skipped cam={cam != null} player={player != null} fader={fader != null} mat={wallMaterial != null}");
            yield break;
        }

        string dir = System.IO.Path.GetDirectoryName(Application.consoleLogPath ?? Application.persistentDataPath + "/x");
        GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = "__QaOcclusionWall";
        wall.GetComponent<Renderer>().sharedMaterial = wallMaterial;
        Vector3 target = player.transform.position + Vector3.up * 1.1f;
        void Place()
        {
            Vector3 from = cam.transform.position;
            wall.transform.position = Vector3.Lerp(from, target, 0.55f);
            wall.transform.rotation = Quaternion.LookRotation(target - from);
            wall.transform.localScale = new Vector3(2.4f, 2.4f, 0.15f);
        }

        fader.enabled = false;
        for (float t = 0f; t < 0.6f; t += Time.unscaledDeltaTime) { Place(); yield return null; }
        ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, "qa_occl_off.png"));
        yield return null;
        fader.enabled = true;
        for (float t = 0f; t < 1.2f; t += Time.unscaledDeltaTime) { Place(); yield return null; }
        ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, "qa_occl_on.png"));
        yield return null;
        yield return null;
        Debug.Log($"[QA] occlusion shot: material={wallMaterial.name} shader={wallMaterial.shader.name} dir={dir}");
        Destroy(wall);
    }

    // -qaFullRun: upper-floor phase timeline + per-mob report at the elevator handoff.
    IEnumerator WatchFullRun(QaCombatBot bot)
    {
        float start = Time.realtimeSinceStartup;
        bool lower = false;
        System.Action<LobbyManager> onLower = _ => lower = true;
        LobbyManager.LowerFloorReached += onLower;
        LobbyManager lobby = FindObjectOfType<LobbyManager>();
        string phase = null;
        while (!lower && bot != null)
        {
            string now = lobby != null ? lobby.CurrentPhase.ToString() : "?";
            if (now != phase)
            {
                phase = now;
                Debug.Log($"[QA-RUN] t={Time.realtimeSinceStartup - start:F1}s lobby phase {phase}");
            }
            yield return new WaitForSecondsRealtime(0.25f);
        }
        LobbyManager.LowerFloorReached -= onLower;
        QaFrameSampler.Mark("facility_boss");
        yield return null;
        PlayerUltimateController ult = FindObjectOfType<PlayerUltimateController>();
        if (ult != null)
            Debug.Log($"[QA-RUN] arrival ultimate gauge {ult.Gauge:F1}/{ult.gaugeMax:F0} = {ult.Gauge / Mathf.Max(1f, ult.gaugeMax):P0} (cap 40%)");
        Debug.Log($"[QA-RUN] t={Time.realtimeSinceStartup - start:F1}s lower floor reached={lower}\n[QA-RUN] bot: {(bot != null ? bot.Summary : "gone")}\n[QA-RUN] mobs:\n{QaCombatBot.MobReport()}");
    }

    // -qaTutorialRun (with -qaScene Tutorial -qaBot S): infiltration tutorial start -> Level_Facility. Logs "[QA-TUT]" step times;
    // a step stuck for 90 s is force-completed and counted (target 0 forced).
    IEnumerator WatchTutorialRun(QaCombatBot bot)
    {
        float start = Time.realtimeSinceStartup;
        int forced = 0;
        string step = null;
        float stepAt = start;
        InfiltrationTutorialDirector d = null;
        QaFrameSampler.Mark("tutorial");
        while (ActiveScene == "Tutorial")
        {
            if (d == null) d = InfiltrationTutorialDirector.Active;
            string now = d != null ? d.CurrentStepId : null;
            if (now != step)
            {
                if (step != null)
                    Debug.Log($"[QA-TUT] t={Time.realtimeSinceStartup - start:F1}s step {step} took {Time.realtimeSinceStartup - stepAt:F1}s");
                step = now;
                stepAt = Time.realtimeSinceStartup;
            }
            if (d != null && step != null && Time.realtimeSinceStartup - stepAt > 90f)
            {
                forced++;
                Debug.Log($"[QA-TUT] FORCED step {step} after 90 s");
                d.QaForceCompleteStep();
                stepAt = Time.realtimeSinceStartup;
            }
            if (d != null)
                _tutSummary = $"steps={d.CurrentStepIndex + 1}/{d.StepCount} resyncs={d.ResyncCount} lessonRespawns={d.LessonRespawns} pdAssists={d.PerfectDodgeAssists} forced={forced} bot: {(bot != null ? bot.Summary : "gone")}\n  log: {string.Join(" | ", d.StepLog)}";
            yield return new WaitForSecondsRealtime(0.25f);
        }
        QaFrameSampler.Mark("after_tutorial");
        Debug.Log(QaCinematicWatch.Report());
        Debug.Log($"[QA-TUT] t={Time.realtimeSinceStartup - start:F1}s tutorial finished -> {ActiveScene} (last step {step})\n[QA-TUT] {_tutSummary}\n[QA] frame report (tutorial):\n{QaFrameSampler.Report()}");
    }
    string _tutSummary;

    IEnumerator Run()
    {
        Debug.Log($"[QA] command line: scene={_scene} skipToBoss={_skipToBoss} bot={_botSkill} duration={_duration}");
        yield return null;

        if (!string.IsNullOrEmpty(_scene))
        {
            if (_skipToBoss && !_fullRun)
            {
                LobbyManager.RequestSkipToLowerFloorOnNextLoad();
                MainSceneArrivalController.RequestSkipArrivalOnNextLoad();
            }

            GameTimeScale.ReleaseAll();
            // -qaScene 은 BootScene 을 건너뛰므로 Boot 의 글리프 워밍업을 여기서 대신 수행 (-qaNoFontWarmup 으로 끔 = 이전 동작 측정용)
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-qaNoFontWarmup") < 0)
            {
                yield return LegacyFontGlyphWarmup.WarmRoutine();
                Debug.Log($"[QA] legacy font warmup (boot-equivalent) {LegacyFontGlyphWarmup.LastWarmupMs:F0} ms, worst frame {LegacyFontGlyphWarmup.LastWarmupMaxFrameMs:F0} ms");
            }
            yield return SceneManager.LoadSceneAsync(_scene, LoadSceneMode.Single);
        }

        QaFrameSampler.Clear();
        QaFrameSampler.Mark("load+settle");
        yield return new WaitForSecondsRealtime(Mathf.Max(0f, _botDelay));
        if (_occlusionShot)
            yield return OcclusionShot();

        QaCombatBot bot = null;
        QaFrameSampler.Mark(_botSkill >= 0f ? "bot" : "idle");
        if (_botSkill >= 0f)
        {
            bot = QaCombatBot.Attach(_botSkill);
            bot.captureScreenshots = false;
            if (_fullRun)
            {
                bot.followLobbyRoute = true;
                StartCoroutine(WatchFullRun(bot));
            }
            if (_tutorialRun)
            {
                bot.followTutorialRoute = true;
                StartCoroutine(WatchTutorialRun(bot));
            }
        }
        if (_pad)
            StartCoroutine(PadGameOverWatch());
        CameraMetricsSampler.Begin(_scene + (SoulsCameraRig.Active != null ? " (souls rig)" : " (legacy cam)"));

        // 보스 처치 후 엔딩 연출(첫 프레임 히치 측정용)까지 본 뒤 조기 종료 — 고정 duration 대기 시간 절약
        float waitEnd = Time.realtimeSinceStartup + Mathf.Max(1f, _duration);
        float bossDeadSeenAt = -1f;
        while (Time.realtimeSinceStartup < waitEnd)
        {
            if (bot != null && !_tutorialRun && bot.BossDeadAt >= 0f && bossDeadSeenAt < 0f)
                bossDeadSeenAt = Time.realtimeSinceStartup;
            bool endingDone = CinematicDirector.LastFinishedId == "ending_tsukasa" && CinematicDirector.LastFinishedAt >= bossDeadSeenAt;
            if (bossDeadSeenAt > 0f && !CinematicDirector.IsPlaying
                && ((endingDone && Time.realtimeSinceStartup - CinematicDirector.LastFinishedAt > 3f) || Time.realtimeSinceStartup - bossDeadSeenAt > 60f))
            {
                Debug.Log($"[QA] early end: boss dead {Time.realtimeSinceStartup - bossDeadSeenAt:F0}s ago, ending done={endingDone}");
                break;
            }
            yield return null;
        }

        Debug.Log("[QA] frame report:\n" + QaFrameSampler.Report());
        if (CameraMetricsSampler.Instance != null)
            Debug.Log("[QA] camera report:\n" + CameraMetricsSampler.Instance.Report());
        if (bot != null)
            Debug.Log("[QA] bot: " + bot.Summary);
        Debug.Log("[QA] overhaul: " + QaCombatBot.OverhaulReport()); // 반복 16
        Debug.Log("[QA] cue audit:\n" + QaCombatBot.CueTimingReport()); // 반복 21: 텔레그래프 정직성 (타격당 큐/경고음/오프셋)
        Debug.Log(QaCinematicWatch.Report());
        Debug.Log(QaErrorShaderWatch.Report());
        if (_audioMeter) Debug.Log(QaAudioMeter.ReportNow());
        if (_pad) Debug.Log($"[QA-PAD] run end: padPresses={QaPadInput.ActionPresses} scheme={ChuonInput.Scheme} deathRestarts={_padRestarts} " + GlyphAudit("end"));

        if (_quit)
            Application.Quit();
    }

    // ---------------- -qaPad (iter 22) ----------------
    int _padRestarts;

    void PadLog(string message)
    {
        string line = $"[QA-PAD] {message} | {QaPadInput.Describe()}";
        _flowLog.AppendLine(line);
        Debug.Log(line);
    }

    static string SelectedName()
    {
        UnityEngine.EventSystems.EventSystem es = UnityEngine.EventSystems.EventSystem.current;
        return es != null && es.currentSelectedGameObject != null ? es.currentSelectedGameObject.name : "null";
    }

    static bool SelectedMatches(string[] keys)
    {
        string sel = SelectedName();
        foreach (string k in keys)
            if (sel.IndexOf(k, System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
        return false;
    }

    /// <summary>D-패드 아래로 내려가며 이름에 keys 중 하나가 들어간 버튼을 찾는다. 지나간 선택 이름을 trail 에 남긴다.</summary>
    IEnumerator PadSelect(string[] keys, System.Text.StringBuilder trail, int maxSteps = 9, GamepadButton step = GamepadButton.DpadDown)
    {
        for (int i = 0; i <= maxSteps; i++)
        {
            trail.Append(SelectedName()).Append(' ');
            if (SelectedMatches(keys))
                yield break;
            QaPadInput.Tap(step);
            yield return new WaitForSecondsRealtime(0.35f);
        }
    }

    IEnumerator PadTitle()
    {
        // 첫 패드 입력 → 스킴 전환 (글리프·선택 상태가 패드 기준으로 바뀌어야 한다)
        QaPadInput.Tap(GamepadButton.DpadDown);
        yield return new WaitForSecondsRealtime(0.5f);
        QaPadInput.Tap(GamepadButton.DpadUp);
        yield return new WaitForSecondsRealtime(0.5f);
        PadLog("title: first pad input; " + GlyphAudit("title"));

        var trail = new System.Text.StringBuilder();
        string[] optKeys = { "Option", "Setting" };
        yield return PadSelect(optKeys, trail);
        if (SelectedMatches(optKeys))
        {
            QaPadInput.Tap(GamepadButton.South);
            yield return new WaitForSecondsRealtime(1.2f);
            TitleSettingsOverlay so = FindObjectOfType<TitleSettingsOverlay>();
            bool open = so != null && so.IsVisible;
            PadLog($"title settings: South -> open={open} (trail {trail}); " + GlyphAudit("title-settings"));
            QaPadInput.Tap(GamepadButton.RightShoulder); // 탭 전환 한 번씩
            yield return new WaitForSecondsRealtime(0.5f);
            QaPadInput.Tap(GamepadButton.LeftShoulder);
            yield return new WaitForSecondsRealtime(0.5f);
            QaPadInput.Tap(GamepadButton.East);
            yield return new WaitForSecondsRealtime(1.0f);
            PadLog($"title settings: East -> open={(so != null && so.IsVisible)}");
        }
        else
            PadLog($"title settings: PAD-FAIL options button not reachable (trail {trail})");

        trail.Length = 0;
        string[] credKeys = { "Credit" };
        yield return PadSelect(credKeys, trail);
        if (SelectedMatches(credKeys))
        {
            QaPadInput.Tap(GamepadButton.South);
            yield return new WaitForSecondsRealtime(1.5f);
            PadLog("title credits: South (overlay)");
            QaPadInput.Tap(GamepadButton.East);
            yield return new WaitForSecondsRealtime(1.0f);
            if (SelectedName().IndexOf("Close", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                QaPadInput.Tap(GamepadButton.South);
                yield return new WaitForSecondsRealtime(1.0f);
            }
            PadLog("title credits: closed by East/South");
        }
        else
            PadLog($"title credits: PAD-FAIL credits button not reachable (trail {trail})");

        trail.Length = 0;
        string[] startKeys = { "Start", "New", "Play", "Begin" };
        yield return PadSelect(startKeys, trail, 9, GamepadButton.DpadUp); // 시작 버튼은 맨 위 (메뉴는 순환하지 않음)
        if (SelectedMatches(startKeys))
        {
            QaPadInput.Tap(GamepadButton.South);
            yield return WaitForScene("BootScene", 8f);
        }
        PadLog($"title start: South -> {(ActiveScene == "BootScene" ? "BootScene" : "PAD-FAIL (trail " + trail + ")")}");
    }

    IEnumerator PadPauseCheck()
    {
        PadLog("facility HUD: " + GlyphAudit("facility-hud"));
        QaPadInput.Tap(GamepadButton.Start);
        yield return new WaitForSecondsRealtime(0.8f);
        PauseManager pm = PauseManager.Instance;
        bool paused = pm != null && pm.IsPaused;
        PadLog($"pause: Start -> paused={paused}; " + GlyphAudit("pause"));
        if (!paused)
            yield break;
        var trail = new System.Text.StringBuilder();
        string[] optKeys = { "Setting", "Option" };
        yield return PadSelect(optKeys, trail);
        if (SelectedMatches(optKeys))
        {
            QaPadInput.Tap(GamepadButton.South);
            yield return new WaitForSecondsRealtime(1.0f);
            PadLog($"pause settings: South -> open={pm.IsSettingsOpen} (trail {trail}); " + GlyphAudit("pause-settings"));
            QaPadInput.Tap(GamepadButton.East);
            yield return new WaitForSecondsRealtime(0.8f);
            PadLog($"pause settings: East -> open={pm.IsSettingsOpen} paused={pm.IsPaused}");
        }
        else
            PadLog($"pause settings: PAD-FAIL not reachable (trail {trail})");
        QaPadInput.Tap(GamepadButton.East);
        yield return new WaitForSecondsRealtime(0.8f);
        if (pm.IsPaused)
        {
            QaPadInput.Tap(GamepadButton.Start);
            yield return new WaitForSecondsRealtime(0.8f);
            PadLog($"pause: East did not resume; Start -> paused={pm.IsPaused}");
        }
        else
            PadLog("pause: East -> resumed");
    }

    IEnumerator PadGameOverWatch()
    {
        while (true)
        {
            MainSceneDeathPresentationController death = FindObjectOfType<MainSceneDeathPresentationController>();
            string sel = SelectedName();
            if (death != null && sel.StartsWith("Button_"))
            {
                string scene = ActiveScene;
                yield return new WaitForSecondsRealtime(1.0f);
                string before = QaPadInput.Describe();
                string audit = GlyphAudit("game-over");
                int loadsBefore = _sceneLoads;
                QaPadInput.Tap(GamepadButton.South);
                float end = Time.realtimeSinceStartup + 12f;
                while (_sceneLoads == loadsBefore && Time.realtimeSinceStartup < end)
                    yield return null;
                _padRestarts++;
                Debug.Log($"[QA-PAD] game-over: sel={sel} South -> {(_sceneLoads > loadsBefore ? "reloaded " + ActiveScene : "PAD-FAIL no reload")} (from {scene}; before: {before}) {audit}");
                yield return new WaitForSecondsRealtime(5f);
            }
            yield return new WaitForSecondsRealtime(0.5f);
        }
    }

    int _sceneLoads;
    void OnEnable() { SceneManager.sceneLoaded += CountSceneLoad; }
    void OnDisable() { SceneManager.sceneLoaded -= CountSceneLoad; }
    void CountSceneLoad(Scene s, LoadSceneMode m) { _sceneLoads++; }

    /// <summary>패드 스킴에서 화면에 남은 키보드/마우스 표기를 찾는다 (보이는 TMP/UGUI 텍스트).</summary>
    static string GlyphAudit(string where)
    {
        var tokens = new System.Collections.Generic.List<string> { "WASD", "W/A/S/D", "마우스", "좌클릭", "우클릭", "LMB", "RMB", "[Esc]", "ESC", "[Space]", "스페이스", "Shift" };
        foreach (ChuonAction a in System.Enum.GetValues(typeof(ChuonAction)))
        {
            if (a == ChuonAction.Move) continue;
            string k = InputGlyphs.KeyboardLabel(a);
            if (string.IsNullOrEmpty(k) || k == "-") continue;
            tokens.Add("[" + k + "]");
            tokens.Add(k + " 키");
        }
        int scanned = 0, bad = 0;
        var sb = new System.Text.StringBuilder();
        void Check(Component c, string text, float alpha)
        {
            if (string.IsNullOrEmpty(text) || alpha < 0.05f) return;
            scanned++;
            foreach (string t in tokens)
            {
                if (text.IndexOf(t, System.StringComparison.Ordinal) < 0) continue;
                bad++;
                if (bad <= 8)
                {
                    string snip = text.Replace("\n", " ");
                    if (snip.Length > 70) snip = snip.Substring(0, 70);
                    sb.Append(" {").Append(c.name).Append(": '").Append(t).Append("' in \"").Append(snip).Append("\"}");
                }
                break;
            }
        }
        foreach (TMPro.TMP_Text t in FindObjectsOfType<TMPro.TMP_Text>())
        {
            if (!t.isActiveAndEnabled) continue;
            float alpha = t.alpha * (t is TMPro.TextMeshProUGUI && t.canvasRenderer != null ? t.canvasRenderer.GetInheritedAlpha() : 1f);
            Check(t, t.text, alpha);
        }
        foreach (UnityEngine.UI.Text t in FindObjectsOfType<UnityEngine.UI.Text>())
        {
            if (!t.isActiveAndEnabled) continue;
            Check(t, t.text, t.color.a * (t.canvasRenderer != null ? t.canvasRenderer.GetInheritedAlpha() : 1f));
        }
        return $"glyphs[{where}] scheme={ChuonInput.Scheme} scanned={scanned} kbTokens={bad}{sb}";
    }
}
#endif
