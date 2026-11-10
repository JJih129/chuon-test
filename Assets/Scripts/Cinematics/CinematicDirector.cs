using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Playables;

/// <summary>
/// 시네마틱 재생기 (반복 14). 씬의 CinematicSequence 를 id 로 재생한다.
///   CinematicDirector.Play("lobby_alarm", onDone);           // 콜백
///   yield return CinematicDirector.PlayRoutine("elevator_descent"); // 코루틴에서 끝까지 대기 (리그가 없으면 즉시 통과)
/// 규칙 (Docs/Cinematics_Shotlist.md):
/// - 비스케일 시간(PlayableDirector UnscaledGameTime). Time.timeScale 은 건드리지 않는다. 일시정지(GameTimeScale 0) 중엔 타임라인도 멈춘다.
/// - 재생 중: 플레이어 입력 차단(IInputBlocker), 무적, 보스 AI 정지(SetExternalIntroPaused, 이미 다른 연출이 멈춰 뒀으면 건드리지 않음),
///   HUD 숨김, 레터박스(옵션), 자막(대사 옵션). 잡몹은 IsPlaying 을 보고 인지/공격 토큰을 보류한다 (통합 단계).
/// - 건너뛰기: 상호작용 키(또는 Space/Enter/Start) 길게 누르기. 첫 입력에 프롬프트가 뜬다. 끝 상태로 점프하고 onSkipped → onFinished.
/// - 재도전 생략: 같은 회차에서 이미 본 연출은 재생 없이 끝 상태로 (CinematicSettings.SkipSeenOnRetry).
/// - 복귀: 타임라인 정지 → 연출 카메라가 꺼지면 SoulsCameraRig 가 0.35 s 블렌드로 플레이어 등 뒤 포즈로 돌아간다 (락온 복원 포함).
/// - 전투 중에는 새 연출을 시작하지 않는다 (CinematicTrigger 가드). 궁극기/처형은 기존 컨트롤러 소관.
/// </summary>
[DisallowMultipleComponent]
public sealed class CinematicDirector : MonoBehaviour
{
    static readonly Dictionary<string, CinematicSequence> s_registry = new Dictionary<string, CinematicSequence>(StringComparer.Ordinal);
    static CinematicDirector s_runner;

    public static bool IsPlaying { get; private set; }
    public static string CurrentId { get; private set; }
    /// <summary>QA/통합: 마지막으로 끝난 연출 id 와 시각(realtime).</summary>
    public static string LastFinishedId { get; private set; }
    public static float LastFinishedAt { get; private set; } = -1f;
    public static bool LastWasSkipped { get; private set; }

    public static event Action<string> Started;
    public static event Action<string, bool> Finished; // id, skipped

    CinematicSequence _current;
    Action _onDone;
    Coroutine _routine;

    // 게임플레이 잠금 상태 (해제할 때 정확히 되돌리기 위해 기록)
    IInputBlocker _blocker;
    bool _blockedInput;
    IInvulnerabilityToggle _invuln;
    bool _setInvuln;
    BossController _pausedBoss;
    PlayerLockOn _lockOn;
    bool _tookCamera;
    readonly List<CanvasGroup> _hudGroups = new List<CanvasGroup>(4);
    readonly List<float> _hudAlphas = new List<float>(4);
    readonly List<Canvas> _hudCanvases = new List<Canvas>(4); // 반복 15: 알파를 매 프레임 덮어쓰는 HUD(Boss_HUD) 대비 — 캔버스 자체를 끈다
    bool _skipRequested;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        s_registry.Clear();
        s_runner = null;
        IsPlaying = false;
        CurrentId = null;
        LastFinishedId = null;
        LastFinishedAt = -1f;
        LastWasSkipped = false;
        Started = null;
        Finished = null;
    }

    // ------------------------------------------------------------------ registry

    internal static void Register(CinematicSequence sequence)
    {
        if (sequence == null || string.IsNullOrEmpty(sequence.Id))
            return;
        if (s_registry.TryGetValue(sequence.Id, out CinematicSequence existing) && existing != null && existing != sequence)
            GameLog.Warn($"[Cinematic] duplicate sequence id '{sequence.Id}' ({existing.name} / {sequence.name}); using the newest", sequence);
        s_registry[sequence.Id] = sequence;
    }

    internal static void Unregister(CinematicSequence sequence)
    {
        if (sequence == null || string.IsNullOrEmpty(sequence.Id))
            return;
        if (s_registry.TryGetValue(sequence.Id, out CinematicSequence existing) && existing == sequence)
            s_registry.Remove(sequence.Id);
    }

    public static bool Has(string id) => !string.IsNullOrEmpty(id) && s_registry.TryGetValue(id, out CinematicSequence s) && s != null;

    public static bool TryGet(string id, out CinematicSequence sequence)
    {
        sequence = null;
        return !string.IsNullOrEmpty(id) && s_registry.TryGetValue(id, out sequence) && sequence != null;
    }

    // ------------------------------------------------------------------ API

    /// <summary>재생 시작. 리그가 없거나 다른 연출이 재생 중이면 false (onDone 은 리그가 없을 때 즉시 호출).</summary>
    public static bool Play(string id, Action onDone = null)
    {
        if (!TryGet(id, out CinematicSequence sequence))
        {
            GameLog.Info($"[Cinematic] '{id}' not installed in this scene; skipped");
            onDone?.Invoke();
            return false;
        }

        if (IsPlaying)
        {
            GameLog.Warn($"[Cinematic] '{id}' requested while '{CurrentId}' is playing; ignored");
            return false;
        }

        EnsureRunner().Begin(sequence, onDone);
        return true;
    }

    /// <summary>코루틴용: 끝날 때까지 기다린다. 리그가 없으면 바로 끝난다.</summary>
    public static IEnumerator PlayRoutine(string id)
    {
        bool done = false;
        if (!Play(id, () => done = true) && !done)
            yield break;
        while (!done)
            yield return null;
    }

    /// <summary>현재 연출을 끝 상태로 건너뛴다 (메뉴/QA).</summary>
    public static void SkipCurrent()
    {
        if (s_runner != null && IsPlaying)
            s_runner._skipRequested = true;
    }

    /// <summary>
    /// 지금 전투 중인가: 보스가 교전 상태이거나, 살아 있는 적이 플레이어를 기억하고 있다.
    /// 트리거가 발동 직전에만 부른다 (씬 검색 포함, 매 프레임 호출 금지).
    /// </summary>
    public static bool IsCombatActive()
    {
        BossController boss = GameplaySceneCache.ResolveBossController();
        if (boss != null && boss.isActiveAndEnabled
            && boss.currentState != BossState.IntroIdle && boss.currentState != BossState.Dead
            && !boss.IsExternalIntroPaused && !boss.IsHoldingForArenaEntry)
            return true;

        EnemyPerception[] perceptions = FindObjectsByType<EnemyPerception>(FindObjectsSortMode.None);
        for (int i = 0; i < perceptions.Length; i++)
        {
            EnemyPerception p = perceptions[i];
            if (p == null || !p.isActiveAndEnabled || !p.RemembersTarget)
                continue;
            EnemyHealth health = p.GetComponentInParent<EnemyHealth>();
            if (health == null || !health.IsDead)
                return true;
        }
        return false;
    }

    static CinematicDirector EnsureRunner()
    {
        if (s_runner != null)
            return s_runner;
        var go = new GameObject("CinematicDirector");
        s_runner = go.AddComponent<CinematicDirector>();
        return s_runner;
    }

    void OnDestroy()
    {
        if (s_runner == this)
        {
            s_runner = null;
            if (IsPlaying)
            {
                // 씬 언로드 중: 잠금만 풀고 상태 초기화 (타임스케일은 건드린 적 없음)
                EndGameplayLock();
                IsPlaying = false;
                CurrentId = null;
            }
        }
    }

    // ------------------------------------------------------------------ playback

    void Begin(CinematicSequence sequence, Action onDone)
    {
        _current = sequence;
        _onDone = onDone;
        _skipRequested = false;
        IsPlaying = true;
        CurrentId = sequence.Id;
        _routine = StartCoroutine(CoPlay(sequence));
    }

    IEnumerator CoPlay(CinematicSequence seq)
    {
        string id = seq.Id;
        try { Started?.Invoke(id); } catch (Exception e) { Debug.LogException(e); }
        try { seq.onStarted?.Invoke(); } catch (Exception e) { Debug.LogException(e, seq); }

        PlayableDirector director = seq.Director;
        bool autoSkip = CinematicSettings.ShouldAutoSkip(id, seq.SeenScope) || director == null || director.playableAsset == null;
        if (autoSkip)
        {
            GameLog.Info($"[Cinematic] '{id}' auto-skipped (seen={CinematicSettings.WasSeen(id)} qa={CinematicSettings.QaSkipAll})", seq);
            if (director != null && director.playableAsset != null)
            {
                PlayerReferences autoRefs = GameplaySceneCache.ResolvePlayerReferences();
                Transform autoBoss = ResolveBossRoot();
                seq.Begin(autoRefs != null ? autoRefs.PlayerRoot : null, autoBoss, null);
                JumpToEnd(director);
                director.Stop();
                seq.End();
            }
            Finish(seq, true);
            yield break;
        }

        CinematicOverlay overlay = CinematicOverlay.Ensure();
        PlayerReferences refs = GameplaySceneCache.ResolvePlayerReferences();
        Transform player = refs != null ? refs.PlayerRoot : null;
        Transform boss = ResolveBossRoot();

        BeginGameplayLock(seq, refs);
        seq.Begin(player, boss, refs);

        director.timeUpdateMode = DirectorUpdateMode.UnscaledGameTime;
        director.extrapolationMode = DirectorWrapMode.None;
        director.time = 0d;
        director.Play();
        GameLog.Info($"[Cinematic] '{id}' play ({seq.Duration:F1}s)", seq);

        double duration = seq.Duration;
        float started = Time.unscaledTime;
        float guard = seq.MaxRealtimeSeconds;
        float hold = 0f;
        float promptUntil = -1f;
        float paused = 0f;
        bool skipped = false;
        bool pausedByMenu = false;
        // 반복 14C: 연출을 발동한 상호작용 키(F 등)를 계속 누르고 있으면 건너뛰기로 새지 않게, 한 번 뗀 뒤의 새 입력만 센다.
        bool needRelease = SkipHeld();
        float tailFade = 0f; // 반복 18: 마지막으로 본 (끝 평가 전) 페이드 알파

        while (true)
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);

            // 일시정지 메뉴(GameTimeScale 0): 타임라인도 멈춘다.
            if (GameTimeScale.IsPaused)
            {
                if (!pausedByMenu && director.state == PlayState.Playing)
                    director.Pause();
                pausedByMenu = true;
                paused += dt;
                overlay.SetSkipPrompt(0f, 0f);
                yield return null;
                continue;
            }
            if (pausedByMenu)
            {
                pausedByMenu = false;
                director.Resume();
            }

            float elapsed = Time.unscaledTime - started - paused;
            seq.Tick();

            // 레터박스 자동 램프 (옵션). 끝 0.35 s 앞에서부터 걷는다.
            if (seq.UseLetterbox && CinematicSettings.LetterboxEnabled)
            {
                float ramp = Mathf.Max(0.01f, seq.LetterboxRamp);
                float inT = Mathf.Clamp01(elapsed / ramp);
                float outT = Mathf.Clamp01((float)(duration - director.time) / ramp);
                overlay.SetLetterbox(Smooth(Mathf.Min(inT, outT)));
            }
            else
            {
                overlay.SetLetterbox(0f);
            }

            // 길게 눌러 건너뛰기
            if (needRelease && !SkipHeld())
                needRelease = false;
            if (seq.Skippable && elapsed > 0.3f && !needRelease)
            {
                if (AnySkipPressedThisFrame())
                    promptUntil = Time.unscaledTime + 2.5f;
                if (SkipHeld())
                {
                    hold += dt;
                    promptUntil = Time.unscaledTime + 2.5f;
                }
                else
                {
                    hold = Mathf.Max(0f, hold - dt * 2f);
                }

                float progress = hold / seq.SkipHoldSeconds;
                float alpha = Time.unscaledTime < promptUntil ? 1f : Mathf.Clamp01(1f - (Time.unscaledTime - promptUntil) / 0.3f);
                overlay.SetSkipPrompt(alpha, progress);
                if (progress >= 1f)
                    _skipRequested = true;
            }

            if (_skipRequested)
            {
                skipped = true;
                break;
            }

            if (director.time >= duration - 0.0001d || (elapsed > 0.1f && director.state != PlayState.Playing))
                break;
            // 반복 18: 끝 페이드 클립이 타임라인 끝에 딱 맞으면, 끝을 지나는 평가 프레임에서 클립 가중치가 0 이 돼 페이드가 0 으로 풀린
            // 화면(복귀 전 엉뚱한 구도)이 한 프레임 이상 보였다 → 거의 덮인 상태로 끝 1.5 프레임 안에 들어오면 여기서 끊고 덮개를 잡는다.
            tailFade = overlay.FadeAlpha;
            if (tailFade >= 0.8f && duration - director.time <= Math.Max(0.05d, Time.unscaledDeltaTime * 1.5d))
                break;
            if (elapsed > guard)
            {
                Debug.LogWarning($"[Cinematic] '{id}' exceeded {guard:F1}s realtime guard; forcing end", seq);
                break;
            }

            yield return null;
        }

        overlay.SetSkipPrompt(0f, 0f);
        if (skipped)
        {
            overlay.HideLine();
            overlay.SetCard(0f);
            JumpToEnd(director);
            try { seq.onSkipped?.Invoke(); } catch (Exception e) { Debug.LogException(e, seq); }
        }

        director.Stop();
        seq.End();
        EndGameplayLock(false); // 반복 18: HUD 는 복귀 페이드가 다 걷힌 뒤에 켠다 (어두운 복귀 화면 위에 HUD 만 먼저 뜨던 문제)

        // 복귀: 남은 검정/레터박스를 카메라 리그 블렌드(0.35 s)와 같이 걷는다.
        float fadeFrom = overlay.FadeAlpha;
        Color fadeColor = overlay.FadeColor;
        float boxFrom = overlay.Letterbox;
        float ret = Mathf.Max(0.01f, seq.ReturnFadeSeconds);
        float t = 0f;
        // 반복 18: 덮개(거의 검정)로 끝나는 연출 — 리그 복귀를 컷으로 요청해 덮개가 걷히기 전에 이미 복귀 포즈에 있게 한다.
        //   자연 종료면 끝 평가 프레임이 페이드를 0 으로 풀었을 수 있으므로 직전 프레임 값(tailFade)도 본다.
        bool coverEnd = fadeFrom >= 0.8f || (!skipped && tailFade >= 0.8f);
        if (coverEnd && fadeFrom < 1f)
        {
            fadeFrom = 1f;
            overlay.SetFade(fadeColor, 1f);
        }
        float stoppedAt = Time.unscaledTime;
        SoulsCameraRig rig = coverEnd ? SoulsCameraRig.Active : null;
        if (rig != null && seq.HoldEndCoverSeconds <= 0f)
            rig.RequestCutOnNextReturn();
        Finish(seq, skipped); // 게임플레이 쪽 후속 처리는 복귀 페이드와 동시에 시작해도 된다
        if (seq.HoldEndCoverSeconds > 0f && (fadeFrom > 0f || boxFrom > 0f))
        {
            // 반복 16: 씬 전환으로 끝나는 연출 — 덮개를 유지(호출자는 바로 이어서 씬을 로드). 로드가 안 되면 안전하게 걷는다.
            RestoreHud();
            StartCoroutine(CoReleaseHeldCover(overlay, seq.HoldEndCoverSeconds, ret));
            yield break;
        }
        if (coverEnd)
        {
            // 반복 17 (E3) / 18: 검정으로 끝나는 연출(레벨 밖 무대 — 오프닝·엘리베이터)은 복귀 중간 프레임이 걷히는 덮개 사이로 보였다
            // → 완전 검정으로 올려 두고, 리그가 복귀 포즈에 안착(컷 + 렌더 2프레임)할 때까지 유지한 뒤 걷는다.
            fadeFrom = 1f;
            float held = 0f;
            float holdMax = ret + 0.5f;
            int settledFrames = 0;
            while (held < holdMax && !IsPlaying)
            {
                if (rig != null ? (rig.IsSettledAfterReturn(stoppedAt) && ++settledFrames >= 2) : held >= ret + 0.05f)
                    break;
                held += Mathf.Min(Time.unscaledDeltaTime, 0.1f);
                overlay.SetFade(fadeColor, fadeFrom);
                yield return null;
            }
        }
        while (t < ret && (fadeFrom > 0f || boxFrom > 0f))
        {
            t += Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            float k = 1f - Smooth(Mathf.Clamp01(t / ret));
            overlay.SetFade(fadeColor, fadeFrom * k);
            overlay.SetLetterbox(boxFrom * k);
            yield return null;
        }
        if (!IsPlaying)
        {
            overlay.SetFade(Color.black, 0f);
            overlay.SetLetterbox(0f);
            RestoreHud(); // 다음 연출이 이미 시작됐다면 그쪽 BeginGameplayLock 이 복원·재숨김을 맡는다
        }
    }

    void Finish(CinematicSequence seq, bool skipped)
    {
        string id = seq.Id;
        CinematicSettings.MarkSeen(id, seq.SeenScope);
        _current = null;
        _routine = null;
        IsPlaying = false;
        CurrentId = null;
        LastFinishedId = id;
        LastFinishedAt = Time.realtimeSinceStartup;
        LastWasSkipped = skipped;
        GameLog.Info($"[Cinematic] '{id}' finished (skipped={skipped})", seq);

        Action done = _onDone;
        _onDone = null;
        try { seq.onFinished?.Invoke(); } catch (Exception e) { Debug.LogException(e, seq); }
        try { Finished?.Invoke(id, skipped); } catch (Exception e) { Debug.LogException(e); }
        try { done?.Invoke(); } catch (Exception e) { Debug.LogException(e); }
    }

    static void JumpToEnd(PlayableDirector director)
    {
        if (director.playableAsset == null)
            return;
        if (!director.playableGraph.IsValid())
            director.RebuildGraph();
        director.time = director.playableAsset.duration;
        director.Evaluate();
    }

    static Transform ResolveBossRoot()
    {
        BossController boss = GameplaySceneCache.ResolveBossController();
        return boss != null ? boss.transform : null;
    }

    static float Smooth(float x) => x * x * (3f - 2f * x);

    IEnumerator CoReleaseHeldCover(CinematicOverlay overlay, float hold, float ret)
    {
        float waited = 0f;
        while (waited < hold)
        {
            if (IsPlaying || overlay == null)
                yield break;
            waited += Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            yield return null;
        }
        if (IsPlaying || overlay == null)
            yield break;
        float fadeFrom = overlay.FadeAlpha;
        Color fadeColor = overlay.FadeColor;
        float boxFrom = overlay.Letterbox;
        float t = 0f;
        while (t < ret)
        {
            if (IsPlaying || overlay == null)
                yield break;
            t += Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            float k = 1f - Smooth(Mathf.Clamp01(t / ret));
            overlay.SetFade(fadeColor, fadeFrom * k);
            overlay.SetLetterbox(boxFrom * k);
            yield return null;
        }
        if (!IsPlaying && overlay != null)
        {
            overlay.SetFade(Color.black, 0f);
            overlay.SetLetterbox(0f);
            overlay.SetDissolve(Color.black, 0f);
        }
    }

    // ------------------------------------------------------------------ input

    static bool SkipHeld()
    {
        if (ChuonInput.Held(ChuonAction.Interact))
            return true;
        Keyboard kb = Keyboard.current;
        if (kb != null && (kb.spaceKey.isPressed || kb.enterKey.isPressed || kb.numpadEnterKey.isPressed))
            return true;
        // 반복 14C: 패드 Start 는 일시정지 메뉴 키라 건너뛰기에서 뺀다 (누르면 메뉴가 열리고 연출도 멈춤 — 둘이 겹치지 않게).
        return false;
    }

    static bool AnySkipPressedThisFrame()
    {
        Keyboard kb = Keyboard.current;
        if (kb != null && kb.anyKey.wasPressedThisFrame)
            return true;
        Mouse mouse = Mouse.current;
        if (mouse != null && (mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame))
            return true;
        Gamepad pad = Gamepad.current;
        return pad != null && (pad.buttonSouth.wasPressedThisFrame || pad.buttonEast.wasPressedThisFrame
            || pad.buttonNorth.wasPressedThisFrame || pad.buttonWest.wasPressedThisFrame);
    }

    // ------------------------------------------------------------------ gameplay lock

    void BeginGameplayLock(CinematicSequence seq, PlayerReferences refs)
    {
        Transform playerRoot = refs != null ? refs.PlayerRoot : null;

        if (seq.BlockPlayerInput && playerRoot != null)
        {
            _blocker = playerRoot.GetComponent<IInputBlocker>();
            if (_blocker == null)
                _blocker = playerRoot.gameObject.AddComponent<SimpleInputBlocker>();
            _blocker.BlockAll(true);
            _blockedInput = true;
        }

        if (seq.MakePlayerInvulnerable && playerRoot != null)
        {
            _invuln = playerRoot.GetComponentInChildren<IInvulnerabilityToggle>(true);
            if (_invuln != null && !_invuln.IsInvulnerable())
            {
                _invuln.SetInvulnerable(true);
                _setInvuln = true;
            }
        }

        if (seq.TakeCameraFromLockOn)
        {
            _lockOn = GameplaySceneCache.ResolvePlayerLockOn();
            if (_lockOn != null && !_lockOn.IsTimelineOwningCamera)
            {
                _lockOn.GiveCameraControlToTimeline(true);
                _tookCamera = true;
            }
        }

        if (seq.PauseBoss)
        {
            BossController boss = GameplaySceneCache.ResolveBossController();
            if (boss != null && boss.isActiveAndEnabled && !boss.IsExternalIntroPaused && boss.currentState != BossState.Dead)
            {
                boss.SetExternalIntroPaused(true);
                _pausedBoss = boss;
            }
        }

        RestoreHud(); // 반복 18: 앞 연출의 복귀 페이드 중(HUD 보류 중)에 시작하면 원래 알파부터 되돌려 놓는다
        string[] hud = seq.HudObjectNames;
        if (hud != null)
        {
            for (int i = 0; i < hud.Length; i++)
            {
                if (string.IsNullOrEmpty(hud[i]))
                    continue;
                GameObject go = GameObject.Find(hud[i]);
                if (go == null)
                    continue;
                CanvasGroup group = go.GetComponent<CanvasGroup>();
                if (group == null)
                    group = go.AddComponent<CanvasGroup>();
                _hudGroups.Add(group);
                _hudAlphas.Add(group.alpha);
                group.alpha = 0f;
                Canvas canvas = go.GetComponent<Canvas>();
                if (canvas != null && canvas.enabled)
                {
                    canvas.enabled = false;
                    _hudCanvases.Add(canvas);
                }
            }
        }
    }

    void EndGameplayLock(bool restoreHud = true)
    {
        if (_blockedInput && _blocker != null && !(_blocker is UnityEngine.Object o && o == null))
            _blocker.BlockAll(false);
        _blockedInput = false;
        _blocker = null;

        if (_setInvuln && _invuln != null && !(_invuln is UnityEngine.Object io && io == null))
            _invuln.SetInvulnerable(false);
        _setInvuln = false;
        _invuln = null;

        if (_tookCamera && _lockOn != null)
            _lockOn.GiveCameraControlToTimeline(false);
        _tookCamera = false;
        _lockOn = null;

        if (_pausedBoss != null)
            _pausedBoss.SetExternalIntroPaused(false);
        _pausedBoss = null;

        if (restoreHud)
            RestoreHud();
    }

    void RestoreHud()
    {
        for (int i = 0; i < _hudGroups.Count; i++)
            if (_hudGroups[i] != null)
                _hudGroups[i].alpha = _hudAlphas[i];
        _hudGroups.Clear();
        _hudAlphas.Clear();
        for (int i = 0; i < _hudCanvases.Count; i++)
            if (_hudCanvases[i] != null)
                _hudCanvases[i].enabled = true;
        _hudCanvases.Clear();
    }
}
