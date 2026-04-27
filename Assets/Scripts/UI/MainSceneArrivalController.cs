using System.Collections;
using System.Text;
using Unity.Cinemachine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

[DisallowMultipleComponent]
public class MainSceneArrivalController : MonoBehaviour
{
    enum IntroEvent
    {
        None,
        ElevatorOpened,
        Alarm,
        BossIdentify,
        CombatStart
    }

    [System.Serializable]
    sealed class IntroStep
    {
        public string speaker = "EGO";
        [TextArea(1, 3)] public string text = "";
        [Min(0.1f)] public float hold = 1.8f;
        [Min(0f)] public float delayAfter = 0.2f;
        public AttackTelegraphType telegraphType = AttackTelegraphType.Auto;
        public bool dangerFeedback;
        public IntroEvent introEvent;

        public IntroStep(string speaker, string text, float hold, AttackTelegraphType telegraphType, IntroEvent introEvent = IntroEvent.None, bool dangerFeedback = false, float delayAfter = 0.2f)
        {
            this.speaker = speaker;
            this.text = text;
            this.hold = hold;
            this.telegraphType = telegraphType;
            this.introEvent = introEvent;
            this.dangerFeedback = dangerFeedback;
            this.delayAfter = delayAfter;
        }
    }

    [SerializeField] private PlayerHUD playerHud;
    [SerializeField] private BossController bossController;
    [SerializeField] private PlayerHealth playerHealth;
    [SerializeField] private BossBreakController bossBreakController;
    [SerializeField] private PlayerUltimateController playerUltimateController;

    [Header("Arrival Sequence")]
    [SerializeField] private string arrivalMessage = "\uc2e4\uc804 \uad6c\uac04 \uc9c4\uc785";
    [SerializeField] private string threatMessage = "\uace0\uc704\ud5d8 \ubc18\uc751 \uac10\uc9c0";
    [SerializeField] private string egoMessage = "EGO SYNC";
    [SerializeField, Min(0.1f)] private float arrivalDelay = 0.45f;
    [SerializeField, Min(0.1f)] private float arrivalHold = 0.72f;
    [SerializeField, Min(0.1f)] private float threatHold = 0.82f;
    [SerializeField, Min(0.05f)] private float betweenDelay = 0.26f;
    [SerializeField] private AttackTelegraphType arrivalTelegraphType = AttackTelegraphType.Guard;
    [SerializeField] private AttackTelegraphType threatTelegraphType = AttackTelegraphType.Danger;

    [Header("Boss Intro Timeline")]
    [SerializeField] bool useBossIntroTimeline = true;
    [SerializeField] IntroStep[] bossIntroSteps =
    {
        new IntroStep("EGO", "도착했어, 추온. 격납고 층이야.", 1.8f, AttackTelegraphType.Guard, IntroEvent.ElevatorOpened),
        new IntroStep("추온", "일부러 여기로 보낸 건가.", 1.6f, AttackTelegraphType.Auto),
        new IntroStep("EGO", "이상해. 보안망이 너무 조용해.", 1.8f, AttackTelegraphType.Auto),
        new IntroStep("EGO", "정정할게. 이제 시끄러워졌네.", 1.6f, AttackTelegraphType.Danger, IntroEvent.Alarm, true),
        new IntroStep("EGO", "전방 고에너지 반응. 인간형... 아니, 실험체야.", 2.2f, AttackTelegraphType.Danger, IntroEvent.BossIdentify, true),
        new IntroStep("추온", "나랑 같은 부류라는 건가.", 1.7f, AttackTelegraphType.Auto),
        new IntroStep("EGO", "Type-D. 네 이전 실험 데이터를 기반으로 만든 방어 개체일 가능성이 높아.", 2.4f, AttackTelegraphType.Danger),
        new IntroStep("추온", "그럼 베면 되겠네.", 1.5f, AttackTelegraphType.Parry),
        new IntroStep("EGO", "추온, 먼저 읽고 들어가. 힘으로 밀면 네가 먼저 부서져.", 2.2f, AttackTelegraphType.Guard),
        new IntroStep("추온", "걱정 마. 끝까지 벨 거야.", 1.7f, AttackTelegraphType.Parry, IntroEvent.CombatStart)
    };
    [SerializeField] AudioSource introAudioSource;
    [SerializeField] AudioClip elevatorOpenSfx;
    [SerializeField] AudioClip alarmSfx;
    [SerializeField] AudioClip bossIdentifySfx;
    [SerializeField, Min(0f)] float alarmSirenDuration = 1.85f;
    [SerializeField, Min(0f)] float alarmSirenPulseSpeed = 7.5f;
    [SerializeField] Color alarmSirenColor = new Color(1f, 0.05f, 0.02f, 1f);
    [SerializeField, Range(0f, 1f)] float alarmSirenMaxOverlayAlpha = 0.22f;
    [SerializeField, Min(0f)] float alarmSirenLightIntensity = 3.2f;
    [SerializeField, Min(0f)] float alarmSirenLightRange = 8f;
    [SerializeField] ParticleSystem bossRevealDustPrefab;
    [SerializeField] Transform bossRevealVfxAnchor;
    [SerializeField] bool useFallbackBossRevealDust = true;
    [SerializeField] bool useBossRevealCloseUp = true;
    [SerializeField] CinemachineCamera bossRevealCloseUpCamera;
    [SerializeField] int bossRevealCloseUpPriority = 120;
    [SerializeField] int bossRevealCloseUpInactivePriority = -100;
    [SerializeField, Min(0.1f)] float bossRevealCloseUpHold = 2.35f;
    [SerializeField, Min(0f)] float bossRevealCloseUpBlendIn = 0.42f;
    [SerializeField, Min(0f)] float bossRevealCloseUpBlendOut = 0.38f;
    [SerializeField, Min(0.5f)] float bossRevealCloseUpDistance = 3.4f;
    [SerializeField] float bossRevealCloseUpSideOffset = 1.45f;
    [SerializeField] float bossRevealCloseUpHeight = 1.35f;
    [SerializeField] float bossRevealCloseUpLookHeight = 1.25f;

    [Header("Boss Intro Dialogue UI")]
    [SerializeField] bool useBossIntroDialogueSubtitle = true;
    [SerializeField, Min(0f)] float introTextTypingSpeed = 0.018f;
    [SerializeField] Vector2 introDialogueAnchor = new Vector2(0.5f, 0.18f);
    [SerializeField] Vector2 introDialogueSize = new Vector2(920f, 112f);
    [SerializeField] Color introDialogueSpeakerColor = new Color(0.0f, 0.85f, 1f, 1f);
    [SerializeField] Color introDialogueTextColor = new Color(1f, 1f, 1f, 1f);

    [Header("Elevator Arrival")]
    [SerializeField] bool playElevatorArrivalOnSceneStart = true;
    [SerializeField] Transform elevatorArrivalAnchor;
    [SerializeField] GameObject closedElevatorDoorVisual;
    [SerializeField] GameObject openElevatorDoorVisual;
    [SerializeField] Transform[] elevatorGrillePanels;
    [SerializeField] Transform[] elevatorDoorPanels;
    [SerializeField] bool useFallbackDoorPanelMotion = true;
    [SerializeField] bool useExplicitElevatorArrivalPosition = true;
    [SerializeField] Vector3 elevatorArrivalWorldPosition = new Vector3(0f, 0f, -22f);
    [SerializeField] Vector3 elevatorArrivalWorldOffset = Vector3.zero;
    [SerializeField] bool useExplicitElevatorArrivalRotation = true;
    [SerializeField] bool faceBossOnElevatorArrival = true;
    [SerializeField] float elevatorArrivalYaw = 0f;
    [SerializeField] bool snapCameraBehindPlayerOnElevatorArrival = true;
    [SerializeField] float elevatorArrivalCameraPitch = 8f;
    [SerializeField] float elevatorArrivalCameraYawOffset = 180f;
    [SerializeField, Min(0.5f)] float elevatorArrivalCameraDistance = 4.2f;
    [SerializeField] float elevatorArrivalCameraHeight = 1.25f;
    [SerializeField, Min(0f)] float elevatorDoorClosedHold = 0.9f;
    [SerializeField, Min(0f)] float elevatorDoorOpenHold = 0.75f;
    [SerializeField, Min(0f)] float elevatorGrilleOpenDuration = 0.85f;
    [SerializeField, Min(0f)] float elevatorDoorOpenDelayAfterGrille = 0.12f;
    [SerializeField, Min(0f)] float elevatorDoorOpenDuration = 1.1f;
    [SerializeField, Min(0f)] float elevatorDoorOpenLift = 4.2f;
    [SerializeField] bool walkPlayerOutAfterDoorOpen = true;
    [SerializeField, Min(0f)] float elevatorWalkOutDelay = 0.15f;
    [SerializeField, Min(0f)] float elevatorWalkOutDistance = 6.2f;
    [SerializeField, Min(0.1f)] float elevatorWalkOutDuration = 3.0f;
    [SerializeField, Range(0f, 1f)] float elevatorWalkOutAnimSpeed = 0.45f;
    [SerializeField] string elevatorWalkOutSpeedParameter = "speed";
    [SerializeField] AnimationClip elevatorWalkStartClip;
    [SerializeField] AnimationClip elevatorWalkLoopClip;
    [SerializeField, Min(0.05f)] float elevatorWalkStartMaxDuration = 0.65f;
    [SerializeField, Min(0.1f)] float elevatorWalkAnimationSpeed = 1f;
    [SerializeField] string elevatorArrivedMessage = "\uc2b9\uac15\uae30 \uc815\ucc29";
    [SerializeField] string elevatorDoorOpenMessage = "\uc804\ud22c \uad6c\uc5ed \uc811\uc18d";

    [Header("Start Facing")]
    [SerializeField] bool applyPlayerStartFacingYaw = true;
    [SerializeField] bool faceCameraForwardOnStart = true;
    [SerializeField] float playerStartYawOffset = 180f;

    [Header("Encounter Coach")]
    [SerializeField] private string firstDefenseCoachMessage = "\uccab \uad50\ud658\uc740 \uc0b4\uc544\ub0a8\ub294 \ucabd\uc774 \uc6b0\uc120\uc774\uc57c";
    [SerializeField] private string firstPunishCoachMessage = "\ube48\ud2c8\uc774 \uc5f4\ub838\ub2e4. \uc9e7\uac8c \ub123\uace0 \ube60\uc838";
    [SerializeField] private string firstDamageCoachMessage = "\ubb34\ub9ac\ud558\uc9c0 \ub9c8. \ub2e4\uc74c \ud328\ud134\uc744 \uba3c\uc800 \uc77d\uc5b4";
    [SerializeField] private string phaseTwoCoachMessage = "\ud398\uc774\uc988 2. \uacf5\uc138\uac00 \ube68\ub77c\uc9c4\ub2e4. \uc751\uc218 \ud6c4 \uc9e7\uac8c";
    [SerializeField] private string phaseThreeCoachMessage = "\ud398\uc774\uc988 3. \ubc84\uc11c\ud06c \uad6c\uac04\uc774\uc57c. \ud328\ud134\uc744 \ub05d\uae4c\uc9c0 \ubd10";
    [SerializeField] private string breakCoachMessage = "\ube0c\ub808\uc774\ud06c\ub2e4. \uc9e7\uac8c \ubab0\uc544\uce58\uace0 \uad81\uadf9\uae30\ub97c \uacb9\uccd0";
    [SerializeField] private string ultimateReadyMessage = "\uad81\uadf9\uae30 \uc900\ube44 \uc644\ub8cc. \ube0c\ub808\uc774\ud06c \ud0c0\uc774\ubc0d\uc5d0 \ub9de\ucdb0";
    [SerializeField, Min(0.05f)] private float coachDelayAfterTelegraph = 0.14f;
    [SerializeField, Min(0.1f)] private float coachHold = 0.72f;
    [SerializeField, Min(0.05f)] private float phaseCoachDelay = 0.48f;
    [SerializeField, Min(0.1f)] private float breakCoachHold = 0.84f;
    [SerializeField, Min(0.1f)] private float ultimateReadyHold = 0.78f;
    Coroutine _arrivalRoutine;
    Coroutine _bossRevealCloseUpRoutine;
    Coroutine _alarmSirenRoutine;
    bool _fromLobbyTransition;
    bool _subscribed;
    bool _firstDefenseCoachShown;
    bool _firstPunishCoachShown;
    bool _firstDamageCoachShown;
    bool _phaseTwoCoachShown;
    bool _phaseThreeCoachShown;
    bool _breakCoachShown;
    bool _ultimateReadyCoachShown;
    bool _startFacingApplied;
    IInputBlocker _inputBlocker;
    bool _arrivalInputLocked;
    bool _bossPausedForIntro;
    CanvasGroup _introDialogueGroup;
    Text _introSpeakerText;
    Text _introContentText;
    CanvasGroup _alarmSirenGroup;
    Image _alarmSirenOverlay;
    Text _alarmSirenText;
    Light _alarmSirenLight;
    Vector3[] _elevatorGrillePanelClosedPositions;
    Vector3[] _elevatorDoorPanelClosedPositions;
    readonly StringBuilder _introTextBuilder = new StringBuilder(96);
    int _bossRevealCloseUpOldPriority;
    bool _bossRevealCloseUpPriorityRaised;

    public bool IsLobbyTransitionActive => _fromLobbyTransition;

    public void ConfigureRuntime(
        PlayerHUD runtimeHud,
        BossController runtimeBoss,
        PlayerHealth runtimePlayerHealth,
        BossBreakController runtimeBossBreakController,
        PlayerUltimateController runtimePlayerUltimateController)
    {
        playerHud = runtimeHud;
        bossController = runtimeBoss;
        playerHealth = runtimePlayerHealth;
        bossBreakController = runtimeBossBreakController;
        playerUltimateController = runtimePlayerUltimateController;
        RefreshSubscriptions();
    }

    void OnEnable()
    {
        bool enteredFromLobby = TutorialSceneTransitionState.ConsumeLobbyToMain();
        _fromLobbyTransition = enteredFromLobby || playElevatorArrivalOnSceneStart;

        _firstDefenseCoachShown = false;
        _firstPunishCoachShown = false;
        _firstDamageCoachShown = false;
        _phaseTwoCoachShown = false;
        _phaseThreeCoachShown = false;
        _breakCoachShown = false;
        _ultimateReadyCoachShown = false;

        if (_arrivalRoutine != null)
            StopCoroutine(_arrivalRoutine);

        RefreshSubscriptions();
        if (playElevatorArrivalOnSceneStart || enteredFromLobby)
            _arrivalRoutine = StartCoroutine(CoPlayArrivalSequence());
        else
            StartCoroutine(CoApplyPlayerStartFacingYawDelayed());
    }

    void OnDisable()
    {
        ReleaseSubscriptions();
        StopBossRevealCloseUp();
        StopAlarmSirenCue();
        SetBossIntroPaused(false);
        SetArrivalInputLocked(false);
        HideIntroDialogue();

        if (_arrivalRoutine == null)
            return;

        StopCoroutine(_arrivalRoutine);
        _arrivalRoutine = null;
    }

    void OnDestroy()
    {
        ReleaseSubscriptions();
        StopBossRevealCloseUp();
        StopAlarmSirenCue();
        SetBossIntroPaused(false);
        HideIntroDialogue();
    }

    IEnumerator CoPlayArrivalSequence()
    {
        if (playerHud == null)
            playerHud = FindObjectOfType<PlayerHUD>(true);

        if (bossController == null)
            bossController = FindObjectOfType<BossController>(true);

        if (playerHealth == null)
            playerHealth = FindObjectOfType<PlayerHealth>(true);

        if (bossBreakController == null && bossController != null)
            bossBreakController = bossController.GetComponent<BossBreakController>();
        if (bossBreakController == null)
            bossBreakController = FindObjectOfType<BossBreakController>(true);

        if (playerUltimateController == null)
            playerUltimateController = FindObjectOfType<PlayerUltimateController>(true);

        RefreshSubscriptions();
        SetBossIntroPaused(true);

        if (playerHud == null)
        {
            SetBossIntroPaused(false);
            yield break;
        }

        if (playElevatorArrivalOnSceneStart)
            yield return StartCoroutine(CoPlayElevatorArrivalIntro());

        if (useBossIntroTimeline && bossIntroSteps != null && bossIntroSteps.Length > 0)
        {
            yield return StartCoroutine(CoPlayBossIntroTimeline());
            SetBossIntroPaused(false);
            SetArrivalInputLocked(false);
            _arrivalRoutine = null;
            yield break;
        }

        if (arrivalDelay > 0f)
            yield return new WaitForSeconds(arrivalDelay);

        playerHud.ShowRuntimeTelegraphMessage(arrivalMessage, arrivalTelegraphType, arrivalHold, false);

        if (betweenDelay > 0f)
            yield return new WaitForSeconds(betweenDelay);

        playerHud.ShowRuntimeTelegraphMessage(egoMessage, AttackTelegraphType.Parry, 0.56f, false);

        if (bossController != null)
        {
            if (betweenDelay > 0f)
                yield return new WaitForSeconds(betweenDelay);

            playerHud.ShowRuntimeTelegraphMessage(threatMessage, threatTelegraphType, threatHold, true);
        }

        SetBossIntroPaused(false);
        SetArrivalInputLocked(false);
        _arrivalRoutine = null;
    }

    IEnumerator CoPlayBossIntroTimeline()
    {
        for (int i = 0; i < bossIntroSteps.Length; i++)
        {
            IntroStep step = bossIntroSteps[i];
            if (step == null)
                continue;

            PlayIntroEvent(step.introEvent);

            string message = FormatIntroLine(step);
            if (!string.IsNullOrWhiteSpace(message))
            {
                if (useBossIntroDialogueSubtitle)
                    yield return StartCoroutine(PlayIntroDialogue(step));
                else
                    playerHud.ShowRuntimeTelegraphMessage(message, step.telegraphType, step.hold, step.dangerFeedback, 20);
            }

            if (!useBossIntroDialogueSubtitle)
            {
                float wait = Mathf.Max(0.1f, step.hold) + Mathf.Max(0f, step.delayAfter);
                yield return new WaitForSeconds(wait);
            }
        }

        HideIntroDialogue();
    }

    static string FormatIntroLine(IntroStep step)
    {
        if (step == null || string.IsNullOrWhiteSpace(step.text))
            return string.Empty;

        if (string.IsNullOrWhiteSpace(step.speaker))
            return step.text;

        return $"{step.speaker}: {step.text}";
    }

    IEnumerator PlayIntroDialogue(IntroStep step)
    {
        EnsureIntroDialogueUi();
        if (_introDialogueGroup == null)
        {
            string fallbackMessage = FormatIntroLine(step);
            if (!string.IsNullOrWhiteSpace(fallbackMessage))
                playerHud.ShowRuntimeTelegraphMessage(fallbackMessage, step.telegraphType, step.hold, step.dangerFeedback, 20);

            yield return new WaitForSeconds(Mathf.Max(0.1f, step.hold) + Mathf.Max(0f, step.delayAfter));
            yield break;
        }

        _introDialogueGroup.alpha = 1f;
        if (_introSpeakerText != null)
            _introSpeakerText.text = step.speaker;

        if (_introContentText != null)
        {
            _introContentText.text = string.Empty;
            _introTextBuilder.Clear();

            string content = step.text ?? string.Empty;
            for (int i = 0; i < content.Length; i++)
            {
                _introTextBuilder.Append(content[i]);
                _introContentText.text = _introTextBuilder.ToString();

                if (introTextTypingSpeed > 0f)
                    yield return new WaitForSeconds(introTextTypingSpeed);
            }
        }

        yield return new WaitForSeconds(Mathf.Max(0.1f, step.hold) + Mathf.Max(0f, step.delayAfter));
    }

    void EnsureIntroDialogueUi()
    {
        if (_introDialogueGroup != null)
            return;

        Canvas canvas = playerHud != null ? playerHud.GetComponentInParent<Canvas>() : null;
        if (canvas == null)
            canvas = FindObjectOfType<Canvas>(true);
        if (canvas == null)
            return;

        GameObject rootObject = new GameObject("BossIntroDialogueRuntime", typeof(RectTransform), typeof(CanvasGroup));
        RectTransform root = rootObject.GetComponent<RectTransform>();
        root.SetParent(canvas.transform, false);
        root.anchorMin = introDialogueAnchor;
        root.anchorMax = introDialogueAnchor;
        root.pivot = new Vector2(0.5f, 0.5f);
        root.sizeDelta = introDialogueSize;
        root.anchoredPosition = Vector2.zero;

        _introDialogueGroup = rootObject.GetComponent<CanvasGroup>();
        _introDialogueGroup.alpha = 0f;
        _introDialogueGroup.interactable = false;
        _introDialogueGroup.blocksRaycasts = false;

        _introSpeakerText = CreateIntroText(root, "Speaker", 23, FontStyle.Bold, TextAnchor.MiddleCenter, introDialogueSpeakerColor, new Vector2(0f, 0.58f), Vector2.one, Vector2.zero, Vector2.zero);
        _introContentText = CreateIntroText(root, "Content", 27, FontStyle.Bold, TextAnchor.MiddleCenter, introDialogueTextColor, Vector2.zero, new Vector2(1f, 0.72f), Vector2.zero, Vector2.zero);
    }

    static Text CreateIntroText(RectTransform parent, string name, int fontSize, FontStyle style, TextAnchor anchor, Color color, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
    {
        GameObject textObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        RectTransform rect = textObject.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;

        Text text = textObject.GetComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = fontSize;
        text.fontStyle = style;
        text.alignment = anchor;
        text.color = color;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
        return text;
    }

    void HideIntroDialogue()
    {
        if (_introDialogueGroup != null)
            _introDialogueGroup.alpha = 0f;
    }

    void PlayIntroEvent(IntroEvent introEvent)
    {
        switch (introEvent)
        {
            case IntroEvent.ElevatorOpened:
                PlayIntroSfx(elevatorOpenSfx);
                break;
            case IntroEvent.Alarm:
                PlayIntroSfx(alarmSfx);
                PlayAlarmSirenCue();
                break;
            case IntroEvent.BossIdentify:
                PlayIntroSfx(bossIdentifySfx);
                SpawnBossRevealVfx();
                PlayBossRevealCloseUp();
                break;
            case IntroEvent.CombatStart:
                break;
        }
    }

    void PlayIntroSfx(AudioClip clip)
    {
        if (clip == null)
            return;

        AudioSource source = ResolveIntroAudioSource();
        if (source != null)
            source.PlayOneShot(clip);
    }

    void PlayAlarmSirenCue()
    {
        StopAlarmSirenCue();
        if (alarmSirenDuration <= 0f)
            return;

        _alarmSirenRoutine = StartCoroutine(CoAlarmSirenCue());
    }

    void StopAlarmSirenCue()
    {
        if (_alarmSirenRoutine != null)
        {
            StopCoroutine(_alarmSirenRoutine);
            _alarmSirenRoutine = null;
        }

        if (_alarmSirenGroup != null)
            _alarmSirenGroup.alpha = 0f;
        if (_alarmSirenLight != null)
            _alarmSirenLight.enabled = false;
    }

    IEnumerator CoAlarmSirenCue()
    {
        EnsureAlarmSirenRuntime();
        if (_alarmSirenGroup == null)
            yield break;

        float elapsed = 0f;
        if (_alarmSirenLight != null)
            _alarmSirenLight.enabled = true;

        while (elapsed < alarmSirenDuration)
        {
            float pulse = 0.5f + Mathf.Sin(elapsed * alarmSirenPulseSpeed) * 0.5f;
            float sharpPulse = pulse * pulse;
            _alarmSirenGroup.alpha = Mathf.Lerp(0.08f, 1f, sharpPulse);

            if (_alarmSirenOverlay != null)
            {
                Color overlayColor = alarmSirenColor;
                overlayColor.a = alarmSirenMaxOverlayAlpha * sharpPulse;
                _alarmSirenOverlay.color = overlayColor;
            }

            if (_alarmSirenText != null)
            {
                Color textColor = alarmSirenColor;
                textColor.a = Mathf.Lerp(0.35f, 1f, sharpPulse);
                _alarmSirenText.color = textColor;
            }

            if (_alarmSirenLight != null)
            {
                Camera mainCamera = Camera.main;
                if (mainCamera != null)
                    _alarmSirenLight.transform.position = mainCamera.transform.position + mainCamera.transform.forward * 1.8f + Vector3.up * 0.35f;

                _alarmSirenLight.color = alarmSirenColor;
                _alarmSirenLight.intensity = alarmSirenLightIntensity * sharpPulse;
                _alarmSirenLight.range = alarmSirenLightRange;
            }

            elapsed += Time.deltaTime;
            yield return null;
        }

        StopAlarmSirenCue();
    }

    void EnsureAlarmSirenRuntime()
    {
        if (_alarmSirenGroup != null)
            return;

        Canvas canvas = playerHud != null ? playerHud.GetComponentInParent<Canvas>() : null;
        if (canvas == null)
            canvas = FindObjectOfType<Canvas>(true);
        if (canvas == null)
            return;

        GameObject rootObject = new GameObject("BossIntroAlarmSirenRuntime", typeof(RectTransform), typeof(CanvasGroup));
        RectTransform root = rootObject.GetComponent<RectTransform>();
        root.SetParent(canvas.transform, false);
        root.anchorMin = Vector2.zero;
        root.anchorMax = Vector2.one;
        root.offsetMin = Vector2.zero;
        root.offsetMax = Vector2.zero;
        root.SetAsFirstSibling();

        _alarmSirenGroup = rootObject.GetComponent<CanvasGroup>();
        _alarmSirenGroup.alpha = 0f;
        _alarmSirenGroup.interactable = false;
        _alarmSirenGroup.blocksRaycasts = false;

        GameObject overlayObject = new GameObject("RedFlash", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        RectTransform overlayRect = overlayObject.GetComponent<RectTransform>();
        overlayRect.SetParent(root, false);
        overlayRect.anchorMin = Vector2.zero;
        overlayRect.anchorMax = Vector2.one;
        overlayRect.offsetMin = Vector2.zero;
        overlayRect.offsetMax = Vector2.zero;
        _alarmSirenOverlay = overlayObject.GetComponent<Image>();
        _alarmSirenOverlay.raycastTarget = false;

        _alarmSirenText = CreateIntroText(root, "AlarmText", 34, FontStyle.Bold, TextAnchor.MiddleCenter, alarmSirenColor, new Vector2(0.25f, 0.48f), new Vector2(0.75f, 0.58f), Vector2.zero, Vector2.zero);
        _alarmSirenText.text = "WARNING";

        GameObject lightObject = new GameObject("RuntimeBossIntroSirenLight", typeof(Light));
        lightObject.transform.SetParent(transform, false);
        _alarmSirenLight = lightObject.GetComponent<Light>();
        _alarmSirenLight.type = LightType.Point;
        _alarmSirenLight.color = alarmSirenColor;
        _alarmSirenLight.range = alarmSirenLightRange;
        _alarmSirenLight.intensity = 0f;
        _alarmSirenLight.enabled = false;
    }

    AudioSource ResolveIntroAudioSource()
    {
        if (introAudioSource != null)
            return introAudioSource;

        Camera mainCamera = Camera.main;
        if (mainCamera != null)
            introAudioSource = mainCamera.GetComponent<AudioSource>();

        if (introAudioSource == null && gameObject.scene.isLoaded)
            introAudioSource = gameObject.AddComponent<AudioSource>();

        return introAudioSource;
    }

    void SpawnBossRevealVfx()
    {
        if (bossRevealDustPrefab == null && !useFallbackBossRevealDust)
            return;

        Transform anchor = bossRevealVfxAnchor != null ? bossRevealVfxAnchor : ResolveBossRoot();
        Vector3 position = anchor != null ? anchor.position : Vector3.zero;
        Quaternion rotation = anchor != null ? anchor.rotation : Quaternion.identity;
        ParticleSystem instance = bossRevealDustPrefab != null
            ? Instantiate(bossRevealDustPrefab, position, rotation)
            : CreateFallbackBossRevealDust(position, rotation);

        if (instance == null)
            return;

        Destroy(instance.gameObject, Mathf.Max(1f, instance.main.duration + instance.main.startLifetime.constantMax));
    }

    void PlayBossRevealCloseUp()
    {
        if (!useBossRevealCloseUp)
            return;

        StopBossRevealCloseUp();
        _bossRevealCloseUpRoutine = StartCoroutine(CoBossRevealCloseUp());
    }

    void StopBossRevealCloseUp()
    {
        if (_bossRevealCloseUpRoutine == null)
        {
            RestoreBossRevealCloseUpCameraPriority();
            return;
        }

        StopCoroutine(_bossRevealCloseUpRoutine);
        _bossRevealCloseUpRoutine = null;
        RestoreBossRevealCloseUpCameraPriority();
    }

    IEnumerator CoBossRevealCloseUp()
    {
        Camera mainCamera = Camera.main;
        Transform bossRoot = ResolveBossRoot();
        Transform playerRoot = ResolvePlayerFacingRoot();
        if (mainCamera == null || bossRoot == null)
            yield break;

        FreeLookCamera freeLookCamera = FindObjectOfType<FreeLookCamera>(true);
        bool restoreFreeLook = freeLookCamera != null && freeLookCamera.enabled;
        if (restoreFreeLook)
            freeLookCamera.enabled = false;

        CinemachineCamera revealCamera = EnsureBossRevealCloseUpCamera();
        bool useRevealVirtualCamera = revealCamera != null && mainCamera.GetComponent<CinemachineBrain>() != null;
        Transform cameraTransform = useRevealVirtualCamera ? revealCamera.transform : mainCamera.transform;
        Vector3 startPosition = mainCamera.transform.position;
        Quaternion startRotation = mainCamera.transform.rotation;

        Vector3 bossPosition = bossRoot.position;
        Vector3 fromBossToPlayer = playerRoot != null ? playerRoot.position - bossPosition : -bossRoot.forward;
        fromBossToPlayer.y = 0f;
        if (fromBossToPlayer.sqrMagnitude <= 0.0001f)
            fromBossToPlayer = -bossRoot.forward;
        fromBossToPlayer.Normalize();

        Vector3 side = Vector3.Cross(Vector3.up, fromBossToPlayer).normalized;
        if (side.sqrMagnitude <= 0.0001f)
            side = bossRoot.right;

        Vector3 lookAt = bossPosition + Vector3.up * Mathf.Max(0f, bossRevealCloseUpLookHeight);
        Vector3 targetPosition =
            bossPosition +
            fromBossToPlayer * Mathf.Max(0.5f, bossRevealCloseUpDistance) +
            side * bossRevealCloseUpSideOffset +
            Vector3.up * bossRevealCloseUpHeight;
        Quaternion targetRotation = Quaternion.LookRotation(lookAt - targetPosition, Vector3.up);

        if (useRevealVirtualCamera)
        {
            CinemachineCompat.TryCopyLensFromUnityCamera(revealCamera, mainCamera);
            revealCamera.Follow = null;
            revealCamera.LookAt = null;
            revealCamera.transform.SetPositionAndRotation(startPosition, startRotation);
            RaiseBossRevealCloseUpCameraPriority(revealCamera);
        }

        yield return StartCoroutine(CoBlendCameraPose(cameraTransform, startPosition, startRotation, targetPosition, targetRotation, bossRevealCloseUpBlendIn));
        if (bossRevealCloseUpHold > 0f)
            yield return new WaitForSeconds(bossRevealCloseUpHold);

        Transform restorePlayerRoot = ResolvePlayerFacingRoot();
        if (useRevealVirtualCamera)
        {
            RestoreBossRevealCloseUpCameraPriority();
            if (bossRevealCloseUpBlendOut > 0f)
                yield return new WaitForSeconds(bossRevealCloseUpBlendOut);
        }
        else
        {
            Vector3 restorePosition;
            Quaternion restoreRotation;
            ResolvePlayerFollowCameraPose(restorePlayerRoot, out restorePosition, out restoreRotation);
            yield return StartCoroutine(CoBlendCameraPose(cameraTransform, cameraTransform.position, cameraTransform.rotation, restorePosition, restoreRotation, bossRevealCloseUpBlendOut));
        }

        if (restoreFreeLook && freeLookCamera != null)
        {
            freeLookCamera.enabled = true;
            SnapCameraBehindPlayer(restorePlayerRoot, false);
        }

        _bossRevealCloseUpRoutine = null;
    }

    CinemachineCamera EnsureBossRevealCloseUpCamera()
    {
        if (bossRevealCloseUpCamera != null)
            return bossRevealCloseUpCamera;

        GameObject cameraObject = new GameObject("RuntimeBossRevealCloseUpCamera");
        cameraObject.transform.SetParent(transform, false);
        bossRevealCloseUpCamera = cameraObject.AddComponent<CinemachineCamera>();
        bossRevealCloseUpCamera.Priority = bossRevealCloseUpInactivePriority;
        return bossRevealCloseUpCamera;
    }

    void RaiseBossRevealCloseUpCameraPriority(CinemachineCamera revealCamera)
    {
        if (revealCamera == null)
            return;

        if (!_bossRevealCloseUpPriorityRaised)
            _bossRevealCloseUpOldPriority = revealCamera.Priority;

        revealCamera.Priority = bossRevealCloseUpPriority;
        _bossRevealCloseUpPriorityRaised = true;
    }

    void RestoreBossRevealCloseUpCameraPriority()
    {
        if (!_bossRevealCloseUpPriorityRaised)
            return;

        if (bossRevealCloseUpCamera != null)
            bossRevealCloseUpCamera.Priority = _bossRevealCloseUpOldPriority;

        _bossRevealCloseUpPriorityRaised = false;
    }

    IEnumerator CoBlendCameraPose(Transform cameraTransform, Vector3 startPosition, Quaternion startRotation, Vector3 targetPosition, Quaternion targetRotation, float duration)
    {
        if (cameraTransform == null)
            yield break;

        if (duration <= 0f)
        {
            cameraTransform.SetPositionAndRotation(targetPosition, targetRotation);
            yield break;
        }

        float elapsed = 0f;
        while (elapsed < duration)
        {
            float t = Mathf.Clamp01(elapsed / duration);
            float eased = t * t * (3f - 2f * t);
            cameraTransform.SetPositionAndRotation(
                Vector3.LerpUnclamped(startPosition, targetPosition, eased),
                Quaternion.SlerpUnclamped(startRotation, targetRotation, eased));
            elapsed += Time.deltaTime;
            yield return null;
        }

        cameraTransform.SetPositionAndRotation(targetPosition, targetRotation);
    }

    void ResolvePlayerFollowCameraPose(Transform playerRoot, out Vector3 position, out Quaternion rotation)
    {
        Camera mainCamera = Camera.main;
        if (playerRoot == null)
        {
            position = mainCamera != null ? mainCamera.transform.position : Vector3.zero;
            rotation = mainCamera != null ? mainCamera.transform.rotation : Quaternion.identity;
            return;
        }

        Quaternion yawRotation = Quaternion.Euler(0f, playerRoot.eulerAngles.y + elevatorArrivalCameraYawOffset, 0f);
        Vector3 focusPoint = playerRoot.position + Vector3.up;
        position = playerRoot.position + yawRotation * new Vector3(0f, 0f, -elevatorArrivalCameraDistance) + Vector3.up * elevatorArrivalCameraHeight;
        rotation = Quaternion.LookRotation(focusPoint - position, Vector3.up);
    }

    static ParticleSystem CreateFallbackBossRevealDust(Vector3 position, Quaternion rotation)
    {
        GameObject dustObject = new GameObject("RuntimeBossRevealDust");
        dustObject.transform.SetPositionAndRotation(position, rotation);
        dustObject.SetActive(false);

        ParticleSystem dust = dustObject.AddComponent<ParticleSystem>();
        ParticleSystem.MainModule main = dust.main;
        main.duration = 1.2f;
        main.loop = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.55f, 1.15f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(1.2f, 3.2f);
        main.startSize = new ParticleSystem.MinMaxCurve(1.1f, 2.4f);
        main.startColor = new Color(0.55f, 0.55f, 0.52f, 0.42f);
        main.maxParticles = 80;
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        ParticleSystem.EmissionModule emission = dust.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[]
        {
            new ParticleSystem.Burst(0f, 42),
            new ParticleSystem.Burst(0.18f, 24)
        });

        ParticleSystem.ShapeModule shape = dust.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 22f;
        shape.radius = 1.4f;
        shape.rotation = new Vector3(-90f, 0f, 0f);

        ParticleSystem.ColorOverLifetimeModule color = dust.colorOverLifetime;
        color.enabled = true;
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(0.62f, 0.62f, 0.58f), 0f),
                new GradientColorKey(new Color(0.25f, 0.25f, 0.25f), 1f)
            },
            new[]
            {
                new GradientAlphaKey(0.52f, 0f),
                new GradientAlphaKey(0.0f, 1f)
            });
        color.color = gradient;

        ParticleSystem.SizeOverLifetimeModule size = dust.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.7f, 1f, 1.6f));

        dustObject.SetActive(true);
        dust.Play();
        return dust;
    }

    IEnumerator CoPlayElevatorArrivalIntro()
    {
        SetArrivalInputLocked(true);
        CacheElevatorArrivalReferences();
        PlacePlayerAtElevatorArrival();

        SetElevatorDoorOpenState(false, immediate: true);

        if (!string.IsNullOrWhiteSpace(elevatorArrivedMessage))
            playerHud.ShowRuntimeTelegraphMessage(elevatorArrivedMessage, AttackTelegraphType.Guard, Mathf.Max(0.4f, elevatorDoorClosedHold), false);

        if (elevatorDoorClosedHold > 0f)
            yield return new WaitForSeconds(elevatorDoorClosedHold);

        yield return StartCoroutine(CoSetElevatorDoorOpen(true));

        if (!string.IsNullOrWhiteSpace(elevatorDoorOpenMessage))
            playerHud.ShowRuntimeTelegraphMessage(elevatorDoorOpenMessage, AttackTelegraphType.Parry, Mathf.Max(0.4f, elevatorDoorOpenHold), false);

        if (elevatorDoorOpenHold > 0f)
            yield return new WaitForSeconds(elevatorDoorOpenHold);

        if (walkPlayerOutAfterDoorOpen)
            yield return StartCoroutine(CoWalkPlayerOutOfElevator());
    }

    IEnumerator CoSetElevatorDoorOpen(bool open)
    {
        SetElevatorDoorOpenState(open, immediate: elevatorDoorOpenDuration <= 0f || !useFallbackDoorPanelMotion);
        if (!useFallbackDoorPanelMotion)
            yield break;

        if (open)
        {
            if (elevatorGrillePanels != null && elevatorGrillePanels.Length > 0)
                yield return StartCoroutine(CoAnimateElevatorPanels(elevatorGrillePanels, _elevatorGrillePanelClosedPositions, elevatorGrilleOpenDuration, true));

            if (elevatorDoorOpenDelayAfterGrille > 0f)
                yield return new WaitForSeconds(elevatorDoorOpenDelayAfterGrille);

            if (elevatorDoorPanels != null && elevatorDoorPanels.Length > 0)
                yield return StartCoroutine(CoAnimateElevatorPanels(elevatorDoorPanels, _elevatorDoorPanelClosedPositions, elevatorDoorOpenDuration, true));
        }
        else
        {
            if (elevatorDoorPanels != null && elevatorDoorPanels.Length > 0)
                yield return StartCoroutine(CoAnimateElevatorPanels(elevatorDoorPanels, _elevatorDoorPanelClosedPositions, elevatorDoorOpenDuration, false));

            if (elevatorGrillePanels != null && elevatorGrillePanels.Length > 0)
                yield return StartCoroutine(CoAnimateElevatorPanels(elevatorGrillePanels, _elevatorGrillePanelClosedPositions, elevatorGrilleOpenDuration, false));
        }
    }

    IEnumerator CoAnimateElevatorPanels(Transform[] panels, Vector3[] closedPositions, float openDuration, bool open)
    {
        if (openDuration <= 0f || panels == null || panels.Length == 0)
        {
            ApplyElevatorPanelOpenAmount(panels, closedPositions, open ? 1f : 0f);
            yield break;
        }

        float duration = Mathf.Max(0.01f, openDuration);
        float elapsed = 0f;
        while (elapsed < duration)
        {
            float t = Mathf.Clamp01(elapsed / duration);
            float eased = t * t * (3f - 2f * t);
            ApplyElevatorPanelOpenAmount(panels, closedPositions, open ? eased : 1f - eased);
            elapsed += Time.deltaTime;
            yield return null;
        }

        ApplyElevatorPanelOpenAmount(panels, closedPositions, open ? 1f : 0f);
    }

    void SetElevatorDoorOpenState(bool open, bool immediate)
    {
        if (closedElevatorDoorVisual != null)
            closedElevatorDoorVisual.SetActive(!open);
        if (openElevatorDoorVisual != null)
            openElevatorDoorVisual.SetActive(open);

        if (!useFallbackDoorPanelMotion)
            return;

        EnsureElevatorDoorPanelCache();
        if (immediate)
        {
            ApplyElevatorPanelOpenAmount(elevatorGrillePanels, _elevatorGrillePanelClosedPositions, open ? 1f : 0f);
            ApplyElevatorPanelOpenAmount(elevatorDoorPanels, _elevatorDoorPanelClosedPositions, open ? 1f : 0f);
        }
    }

    void ApplyElevatorPanelOpenAmount(Transform[] panels, Vector3[] closedPositions, float open01)
    {
        if (closedPositions == null || panels == null)
            return;

        float lift = Mathf.Max(0f, elevatorDoorOpenLift) * Mathf.Clamp01(open01);
        int count = Mathf.Min(panels.Length, closedPositions.Length);
        for (int i = 0; i < count; i++)
        {
            Transform panel = panels[i];
            if (panel == null)
                continue;

            panel.position = closedPositions[i] + Vector3.up * lift;
        }
    }

    IEnumerator CoWalkPlayerOutOfElevator()
    {
        Transform playerRoot = ResolvePlayerFacingRoot();
        if (playerRoot == null || elevatorWalkOutDistance <= 0f)
            yield break;

        if (elevatorWalkOutDelay > 0f)
            yield return new WaitForSeconds(elevatorWalkOutDelay);

        PlayerMoveController moveController = playerRoot.GetComponent<PlayerMoveController>();
        if (moveController == null)
            moveController = playerRoot.GetComponentInChildren<PlayerMoveController>(true);

        Animator animator = playerRoot.GetComponentInChildren<Animator>(true);
        bool useClipPlayback = CanUseElevatorWalkClipPlayback(animator);
        bool canDriveSpeed = !useClipPlayback && HasFloatParameter(animator, elevatorWalkOutSpeedParameter);
        ElevatorWalkClipPlayback clipPlayback = default;
        bool originalApplyRootMotion = animator != null && animator.applyRootMotion;
        Vector3 forward = ResolveElevatorWalkOutDirection(playerRoot);
        Quaternion rotation = Quaternion.LookRotation(forward, Vector3.up);
        Vector3 start = playerRoot.position;
        Vector3 end = start + forward * elevatorWalkOutDistance;
        float duration = Mathf.Max(0.1f, elevatorWalkOutDuration);
        float elapsed = 0f;

        moveController?.SetExternalControl(true);
        try
        {
            if (useClipPlayback)
            {
                animator.applyRootMotion = false;
                clipPlayback = BeginElevatorWalkClipPlayback(animator);
            }

            while (elapsed < duration)
            {
                float t = Mathf.Clamp01(elapsed / duration);
                float eased = t * t * (3f - 2f * t);
                SetPlayerWorldPose(playerRoot, Vector3.LerpUnclamped(start, end, eased), rotation);
                if (canDriveSpeed)
                    animator.SetFloat(elevatorWalkOutSpeedParameter, elevatorWalkOutAnimSpeed);
                if (clipPlayback.IsValid)
                    UpdateElevatorWalkClipPlayback(ref clipPlayback, elapsed);

                elapsed += Time.deltaTime;
                yield return null;
            }

            SetPlayerWorldPose(playerRoot, end, rotation);
        }
        finally
        {
            EndElevatorWalkClipPlayback(ref clipPlayback);
            if (animator != null)
                animator.applyRootMotion = originalApplyRootMotion;
            if (canDriveSpeed)
                animator.SetFloat(elevatorWalkOutSpeedParameter, 0f);
            moveController?.SetExternalControl(false);
        }

        SnapCameraBehindPlayer(playerRoot, false);
    }

    bool CanUseElevatorWalkClipPlayback(Animator animator)
    {
        AnimationClip walkStart = ResolveElevatorWalkStartClip();
        AnimationClip walkLoop = ResolveElevatorWalkLoopClip();
        return animator != null
            && animator.isActiveAndEnabled
            && animator.runtimeAnimatorController != null
            && (walkStart != null || walkLoop != null);
    }

    ElevatorWalkClipPlayback BeginElevatorWalkClipPlayback(Animator animator)
    {
        ElevatorWalkClipPlayback playback = default;
        if (animator == null)
            return playback;

        AnimationClip walkStart = ResolveElevatorWalkStartClip();
        AnimationClip walkLoop = ResolveElevatorWalkLoopClip();
        playback.Graph = PlayableGraph.Create("ElevatorWalkOutClipPlayback");
        playback.Graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
        AnimationPlayableOutput output = AnimationPlayableOutput.Create(playback.Graph, "ElevatorWalkOut", animator);
        playback.Mixer = AnimationMixerPlayable.Create(playback.Graph, 2);
        output.SetSourcePlayable(playback.Mixer);

        if (walkStart != null)
        {
            playback.Start = AnimationClipPlayable.Create(playback.Graph, walkStart);
            playback.Start.SetApplyFootIK(true);
            playback.Start.SetSpeed(elevatorWalkAnimationSpeed);
            playback.Graph.Connect(playback.Start, 0, playback.Mixer, 0);
            playback.HasStart = true;
            playback.StartClip = walkStart;
            playback.StartDuration = Mathf.Min(elevatorWalkStartMaxDuration, Mathf.Max(0.05f, walkStart.length / Mathf.Max(0.01f, elevatorWalkAnimationSpeed)));
        }

        if (walkLoop != null)
        {
            playback.Loop = AnimationClipPlayable.Create(playback.Graph, walkLoop);
            playback.Loop.SetApplyFootIK(true);
            playback.Loop.SetSpeed(elevatorWalkAnimationSpeed);
            playback.Graph.Connect(playback.Loop, 0, playback.Mixer, 1);
            playback.HasLoop = true;
            playback.LoopClip = walkLoop;
        }

        playback.IsValid = playback.Graph.IsValid();
        if (playback.IsValid)
            playback.Graph.Play();

        return playback;
    }

    void UpdateElevatorWalkClipPlayback(ref ElevatorWalkClipPlayback playback, float elapsed)
    {
        if (!playback.IsValid || !playback.Mixer.IsValid())
            return;

        bool playStart = playback.HasStart && (!playback.HasLoop || elapsed < playback.StartDuration);
        playback.Mixer.SetInputWeight(0, playStart ? 1f : 0f);
        playback.Mixer.SetInputWeight(1, playStart ? 0f : 1f);

        if (playback.HasStart && playback.Start.IsValid())
            playback.Start.SetTime(Mathf.Min(elapsed * elevatorWalkAnimationSpeed, playback.StartClip.length));

        if (playback.HasLoop && playback.Loop.IsValid())
        {
            float loopLength = Mathf.Max(0.05f, playback.LoopClip.length);
            float loopTime = Mathf.Repeat(Mathf.Max(0f, elapsed - playback.StartDuration) * elevatorWalkAnimationSpeed, loopLength);
            playback.Loop.SetTime(loopTime);
        }
    }

    AnimationClip ResolveElevatorWalkStartClip()
    {
        if (elevatorWalkStartClip != null)
            return elevatorWalkStartClip;

#if UNITY_EDITOR
        elevatorWalkStartClip = LoadEditorAnimationClip(
            "Assets/GhostSamurai_Animset/Animation/katana/Common/Inplace/GhostSamurai_Common_Walk_Start_Inplace.FBX",
            "GhostSamurai_Common_Walk_Start_Inplace");
#endif
        return elevatorWalkStartClip;
    }

    AnimationClip ResolveElevatorWalkLoopClip()
    {
        if (elevatorWalkLoopClip != null)
            return elevatorWalkLoopClip;

#if UNITY_EDITOR
        elevatorWalkLoopClip = LoadEditorAnimationClip(
            "Assets/GhostSamurai_Animset/Animation/katana/Common/Root/GhostSamurai_Common_Walk_Loop_Root.FBX",
            "GhostSamurai_Common_Walk_Loop_Root");
#endif
        return elevatorWalkLoopClip;
    }

#if UNITY_EDITOR
    static AnimationClip LoadEditorAnimationClip(string path, string clipName)
    {
        Object[] assets = AssetDatabase.LoadAllAssetsAtPath(path);
        for (int i = 0; i < assets.Length; i++)
        {
            AnimationClip clip = assets[i] as AnimationClip;
            if (clip != null && clip.name == clipName)
                return clip;
        }

        for (int i = 0; i < assets.Length; i++)
        {
            AnimationClip clip = assets[i] as AnimationClip;
            if (clip != null)
                return clip;
        }

        return null;
    }
#endif

    static void EndElevatorWalkClipPlayback(ref ElevatorWalkClipPlayback playback)
    {
        if (playback.Graph.IsValid())
            playback.Graph.Destroy();

        playback = default;
    }

    struct ElevatorWalkClipPlayback
    {
        public PlayableGraph Graph;
        public AnimationMixerPlayable Mixer;
        public AnimationClipPlayable Start;
        public AnimationClipPlayable Loop;
        public AnimationClip StartClip;
        public AnimationClip LoopClip;
        public bool HasStart;
        public bool HasLoop;
        public bool IsValid;
        public float StartDuration;
    }

    void CacheElevatorArrivalReferences()
    {
        if (elevatorArrivalAnchor == null)
        {
            GameObject elevator = GameObject.Find("Elevator");
            if (elevator != null)
                elevatorArrivalAnchor = elevator.transform;
        }

        if (closedElevatorDoorVisual == null)
            closedElevatorDoorVisual = GameObject.Find("closedoor (1)");

        if (openElevatorDoorVisual == null)
            openElevatorDoorVisual = GameObject.Find("opendoor (1)");

        EnsureElevatorDoorPanelCache();
    }

    void EnsureElevatorDoorPanelCache()
    {
        if (!useFallbackDoorPanelMotion)
            return;

        if ((elevatorGrillePanels == null || elevatorGrillePanels.Length == 0) && elevatorArrivalAnchor != null)
            elevatorGrillePanels = FindFallbackElevatorDoorPanels(elevatorArrivalAnchor, true);

        if ((elevatorDoorPanels == null || elevatorDoorPanels.Length == 0) && elevatorArrivalAnchor != null)
            elevatorDoorPanels = FindFallbackElevatorDoorPanels(elevatorArrivalAnchor, false);

        _elevatorGrillePanelClosedPositions = EnsurePanelClosedPositionCache(elevatorGrillePanels, _elevatorGrillePanelClosedPositions);
        _elevatorDoorPanelClosedPositions = EnsurePanelClosedPositionCache(elevatorDoorPanels, _elevatorDoorPanelClosedPositions);
    }

    static Vector3[] EnsurePanelClosedPositionCache(Transform[] panels, Vector3[] cachedPositions)
    {
        if (panels == null || panels.Length == 0)
            return cachedPositions;

        if (cachedPositions != null && cachedPositions.Length == panels.Length)
            return cachedPositions;

        cachedPositions = new Vector3[panels.Length];
        for (int i = 0; i < panels.Length; i++)
            cachedPositions[i] = panels[i] != null ? panels[i].position : Vector3.zero;

        return cachedPositions;
    }

    static Transform[] FindFallbackElevatorDoorPanels(Transform elevatorRoot, bool grille)
    {
        if (elevatorRoot == null)
            return System.Array.Empty<Transform>();

        Renderer[] renderers = elevatorRoot.GetComponentsInChildren<Renderer>(true);
        System.Collections.Generic.List<Transform> panels = new System.Collections.Generic.List<Transform>(24);
        System.Collections.Generic.HashSet<Transform> unique = new System.Collections.Generic.HashSet<Transform>();
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null || !IsFallbackElevatorDoorRenderer(renderer, grille))
                continue;

            AddUniqueDoorPanelTransform(panels, unique, renderer.transform);

            SkinnedMeshRenderer skinned = renderer as SkinnedMeshRenderer;
            if (skinned == null)
                continue;

            AddUniqueDoorPanelTransform(panels, unique, skinned.rootBone);
            Transform[] bones = skinned.bones;
            if (bones == null)
                continue;

            for (int boneIndex = 0; boneIndex < bones.Length; boneIndex++)
                AddUniqueDoorPanelTransform(panels, unique, bones[boneIndex]);
        }

        panels.Sort((a, b) => a.position.y.CompareTo(b.position.y));
        return panels.ToArray();
    }

    static bool IsFallbackElevatorDoorRenderer(Renderer renderer, bool grille)
    {
        if (renderer == null)
            return false;

        Transform child = renderer.transform;
        string objectName = child.name;
        bool knownDoorName =
            objectName.StartsWith("elevator002", System.StringComparison.OrdinalIgnoreCase) ||
            objectName.StartsWith("elevator003", System.StringComparison.OrdinalIgnoreCase) ||
            objectName.StartsWith("elevator004", System.StringComparison.OrdinalIgnoreCase) ||
            objectName.StartsWith("elevator005", System.StringComparison.OrdinalIgnoreCase) ||
            objectName.StartsWith("Dummy00", System.StringComparison.OrdinalIgnoreCase);

        if (!knownDoorName)
            return false;

        Bounds bounds = renderer.bounds;
        Vector3 size = bounds.size;
        if (size.x < 1.2f || size.y > 5.2f)
            return false;

        Vector3 localPosition = child.localPosition;
        bool frontDoorLayer = localPosition.z > 0.1f && localPosition.z < 0.8f;
        bool importedDoorLayer = localPosition.z < -10f && localPosition.y < -10f;
        if (!frontDoorLayer && !importedDoorLayer)
            return false;

        if (grille)
            return objectName.StartsWith("Dummy00", System.StringComparison.OrdinalIgnoreCase) ||
                   objectName.StartsWith("elevator002", System.StringComparison.OrdinalIgnoreCase) ||
                   size.z > 0.8f;

        return objectName.StartsWith("elevator003", System.StringComparison.OrdinalIgnoreCase) ||
               objectName.StartsWith("elevator004", System.StringComparison.OrdinalIgnoreCase) ||
               objectName.StartsWith("elevator005", System.StringComparison.OrdinalIgnoreCase);
    }

    static void AddUniqueDoorPanelTransform(System.Collections.Generic.List<Transform> panels, System.Collections.Generic.HashSet<Transform> unique, Transform target)
    {
        if (target == null || !unique.Add(target))
            return;

        panels.Add(target);
    }

    void PlacePlayerAtElevatorArrival()
    {
        Transform playerRoot = ResolvePlayerFacingRoot();
        if (playerRoot == null)
            return;

        Vector3 basePosition = ResolveElevatorArrivalBasePosition(playerRoot.position) + elevatorArrivalWorldOffset;

        Quaternion rotation = ResolveElevatorArrivalRotation(playerRoot);
        SetPlayerWorldPose(playerRoot, basePosition, rotation);
        SnapCameraBehindPlayer(playerRoot, true);
        _startFacingApplied = true;
    }

    IEnumerator CoSnapCameraBehindPlayerDelayed()
    {
        yield return null;
        SnapCameraBehindPlayer(ResolvePlayerFacingRoot(), false);

        yield return new WaitForEndOfFrame();
        SnapCameraBehindPlayer(ResolvePlayerFacingRoot(), false);
    }

    void SnapCameraBehindPlayer(Transform playerRoot, bool scheduleSecondSnap)
    {
        if (!snapCameraBehindPlayerOnElevatorArrival || playerRoot == null)
            return;

        FreeLookCamera freeLookCamera = FindObjectOfType<FreeLookCamera>(true);
        if (freeLookCamera != null)
        {
            freeLookCamera.player = playerRoot;
            freeLookCamera.distance = elevatorArrivalCameraDistance;
            freeLookCamera.height = elevatorArrivalCameraHeight;
            freeLookCamera.SnapBehindPlayer(elevatorArrivalCameraPitch, elevatorArrivalCameraYawOffset);
            if (scheduleSecondSnap)
                StartCoroutine(CoSnapCameraBehindPlayerDelayed());
            return;
        }

        Camera mainCamera = Camera.main;
        if (mainCamera == null)
            return;

        Quaternion yawRotation = Quaternion.Euler(0f, playerRoot.eulerAngles.y + elevatorArrivalCameraYawOffset, 0f);
        Vector3 focusPoint = playerRoot.position + Vector3.up;
        Vector3 cameraPosition = playerRoot.position + yawRotation * new Vector3(0f, 0f, -elevatorArrivalCameraDistance) + Vector3.up * elevatorArrivalCameraHeight;
        mainCamera.transform.SetPositionAndRotation(
            cameraPosition,
            Quaternion.LookRotation(focusPoint - cameraPosition, Vector3.up));

        if (scheduleSecondSnap)
            StartCoroutine(CoSnapCameraBehindPlayerDelayed());
    }

    Quaternion ResolveElevatorArrivalRotation(Transform playerRoot)
    {
        if (faceBossOnElevatorArrival)
        {
            Transform bossRoot = ResolveBossRoot();
            if (bossRoot != null && playerRoot != null)
            {
                Vector3 toBoss = bossRoot.position - playerRoot.position;
                toBoss.y = 0f;
                if (toBoss.sqrMagnitude > 0.0001f)
                    return Quaternion.LookRotation(toBoss.normalized, Vector3.up);
            }
        }

        return useExplicitElevatorArrivalRotation
            ? Quaternion.Euler(0f, elevatorArrivalYaw, 0f)
            : ResolveStartFacingRotation(playerRoot != null ? playerRoot.rotation : Quaternion.identity);
    }

    Transform ResolveBossRoot()
    {
        if (bossController != null)
            return bossController.transform;

        BossController runtimeBoss = FindObjectOfType<BossController>(true);
        return runtimeBoss != null ? runtimeBoss.transform : null;
    }

    Vector3 ResolveElevatorWalkOutDirection(Transform playerRoot)
    {
        Vector3 forward = playerRoot != null ? playerRoot.forward : Vector3.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude > 0.0001f)
            return forward.normalized;

        Transform bossRoot = ResolveBossRoot();
        if (bossRoot != null && playerRoot != null)
        {
            forward = bossRoot.position - playerRoot.position;
            forward.y = 0f;
            if (forward.sqrMagnitude > 0.0001f)
                return forward.normalized;
        }

        return Vector3.forward;
    }

    Vector3 ResolveElevatorArrivalBasePosition(Vector3 fallbackPosition)
    {
        if (useExplicitElevatorArrivalPosition)
            return elevatorArrivalWorldPosition;

        if (TryGetRendererBoundsCenter(closedElevatorDoorVisual, out Vector3 closedDoorCenter))
            return closedDoorCenter;

        if (TryGetRendererBoundsCenter(openElevatorDoorVisual, out Vector3 openDoorCenter))
            return openDoorCenter;

        if (closedElevatorDoorVisual != null)
            return closedElevatorDoorVisual.transform.position;

        if (openElevatorDoorVisual != null)
            return openElevatorDoorVisual.transform.position;

        if (elevatorArrivalAnchor != null)
            return elevatorArrivalAnchor.position;

        return fallbackPosition;
    }

    static bool TryGetRendererBoundsCenter(GameObject target, out Vector3 center)
    {
        center = Vector3.zero;
        if (target == null)
            return false;

        Renderer[] renderers = target.GetComponentsInChildren<Renderer>(true);
        if (renderers == null || renderers.Length == 0)
            return false;

        Bounds bounds = default;
        bool hasBounds = false;
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null)
                continue;

            if (!hasBounds)
            {
                bounds = renderer.bounds;
                hasBounds = true;
                continue;
            }

            bounds.Encapsulate(renderer.bounds);
        }

        if (!hasBounds)
            return false;

        center = bounds.center;
        return true;
    }

    static void SetPlayerWorldPose(Transform playerRoot, Vector3 position, Quaternion rotation)
    {
        CharacterController characterController = playerRoot.GetComponent<CharacterController>();
        bool restoreCharacterController = characterController != null && characterController.enabled;
        if (restoreCharacterController)
            characterController.enabled = false;

        playerRoot.SetPositionAndRotation(position, rotation);

        if (restoreCharacterController)
            characterController.enabled = true;
    }

    void ApplyPlayerStartFacingYawOnce()
    {
        if (_startFacingApplied || !applyPlayerStartFacingYaw || Mathf.Abs(playerStartYawOffset) <= 0.001f)
            return;

        Transform facingRoot = ResolvePlayerFacingRoot();
        if (facingRoot == null)
            return;

        facingRoot.rotation = ResolveStartFacingRotation(facingRoot.rotation);
        _startFacingApplied = true;
    }

    IEnumerator CoApplyPlayerStartFacingYawDelayed()
    {
        ApplyPlayerStartFacingYawOnce();

        if (_startFacingApplied)
            yield break;

        yield return null;
        ApplyPlayerStartFacingYawOnce();
    }

    Transform ResolvePlayerFacingRoot()
    {
        if (playerHealth != null)
            return playerHealth.transform;

        PlayerHealth runtimeHealth = FindObjectOfType<PlayerHealth>(true);
        if (runtimeHealth != null)
            return runtimeHealth.transform;

        GameObject player = GameObject.FindWithTag("Player");
        if (player == null)
        {
            PlayerReferences runtimeReferences = FindObjectOfType<PlayerReferences>(true);
            if (runtimeReferences != null)
                return runtimeReferences.PlayerRoot != null ? runtimeReferences.PlayerRoot : runtimeReferences.transform;

            return null;
        }

        PlayerReferences references = player.GetComponent<PlayerReferences>();
        if (references != null && references.PlayerRoot != null)
            return references.PlayerRoot;

        return player.transform;
    }

    void SetArrivalInputLocked(bool locked)
    {
        if (_arrivalInputLocked == locked)
            return;

        IInputBlocker inputBlocker = ResolveInputBlocker();
        if (inputBlocker == null)
            return;

        inputBlocker.BlockAll(locked);
        _arrivalInputLocked = locked;
    }

    void SetBossIntroPaused(bool paused)
    {
        if (!paused && !_bossPausedForIntro)
            return;

        if (bossController == null)
            bossController = FindObjectOfType<BossController>(true);
        if (bossController == null)
            return;

        bossController.SetExternalIntroPaused(paused);
        _bossPausedForIntro = paused;
    }

    static bool HasFloatParameter(Animator animator, string parameterName)
    {
        if (animator == null || string.IsNullOrEmpty(parameterName))
            return false;

        int hash = Animator.StringToHash(parameterName);
        AnimatorControllerParameter[] parameters = animator.parameters;
        for (int i = 0; i < parameters.Length; i++)
        {
            AnimatorControllerParameter parameter = parameters[i];
            if (parameter.type == AnimatorControllerParameterType.Float && parameter.nameHash == hash)
                return true;
        }

        return false;
    }

    IInputBlocker ResolveInputBlocker()
    {
        if (_inputBlocker != null)
        {
            UnityEngine.Object cachedObject = _inputBlocker as UnityEngine.Object;
            if (cachedObject != null)
                return _inputBlocker;

            _inputBlocker = null;
        }

        Transform playerRoot = ResolvePlayerFacingRoot();
        if (playerRoot == null)
            return null;

        _inputBlocker = playerRoot.GetComponent<IInputBlocker>();
        if (_inputBlocker == null)
            _inputBlocker = playerRoot.gameObject.AddComponent<SimpleInputBlocker>();

        return _inputBlocker;
    }

    Quaternion ResolveStartFacingRotation(Quaternion fallbackRotation)
    {
        if (faceCameraForwardOnStart)
        {
            Camera mainCamera = Camera.main;
            if (mainCamera != null)
            {
                Vector3 forward = mainCamera.transform.forward;
                forward.y = 0f;
                if (forward.sqrMagnitude > 0.0001f)
                    return Quaternion.LookRotation(forward.normalized, Vector3.up);
            }
        }

        return Quaternion.AngleAxis(playerStartYawOffset, Vector3.up) * fallbackRotation;
    }

    void RefreshSubscriptions()
    {
        ReleaseSubscriptions();

        if (!_fromLobbyTransition)
            return;

        if (bossController == null)
            bossController = FindObjectOfType<BossController>(true);

        if (playerHealth == null)
            playerHealth = FindObjectOfType<PlayerHealth>(true);

        if (bossBreakController == null && bossController != null)
            bossBreakController = bossController.GetComponent<BossBreakController>();
        if (bossBreakController == null)
            bossBreakController = FindObjectOfType<BossBreakController>(true);

        if (playerUltimateController == null)
            playerUltimateController = FindObjectOfType<PlayerUltimateController>(true);

        if (bossController != null)
        {
            bossController.OnAttackTelegraph += HandleBossAttackTelegraph;
            bossController.OnPunishWindowOpened += HandlePunishWindowOpened;
            bossController.OnBossPhaseChanged += HandleBossPhaseChanged;
            _subscribed = true;
        }

        if (bossBreakController != null)
        {
            bossBreakController.OnBreakEnter.AddListener(HandleBossBreakEnter);
            _subscribed = true;
        }

        if (playerHealth != null)
        {
            playerHealth.OnDamaged += HandlePlayerDamaged;
            _subscribed = true;
        }

        if (playerUltimateController != null)
        {
            playerUltimateController.OnGaugeChanged += HandleUltimateGaugeChanged;
            playerUltimateController.OnUltimateStarted += HandleUltimateStarted;
            HandleUltimateGaugeChanged(
                playerUltimateController.Gauge,
                playerUltimateController.gaugeMax > 0f ? Mathf.Clamp01(playerUltimateController.Gauge / playerUltimateController.gaugeMax) : 0f,
                playerUltimateController.IsGaugeReady);
            _subscribed = true;
        }
    }

    void ReleaseSubscriptions()
    {
        if (!_subscribed)
            return;

        if (bossController != null)
        {
            bossController.OnAttackTelegraph -= HandleBossAttackTelegraph;
            bossController.OnPunishWindowOpened -= HandlePunishWindowOpened;
            bossController.OnBossPhaseChanged -= HandleBossPhaseChanged;
        }

        if (bossBreakController != null)
            bossBreakController.OnBreakEnter.RemoveListener(HandleBossBreakEnter);

        if (playerHealth != null)
            playerHealth.OnDamaged -= HandlePlayerDamaged;

        if (playerUltimateController != null)
        {
            playerUltimateController.OnGaugeChanged -= HandleUltimateGaugeChanged;
            playerUltimateController.OnUltimateStarted -= HandleUltimateStarted;
        }

        _subscribed = false;
    }

    void HandleBossAttackTelegraph(AttackTelegraphType telegraphType, float leadTime, string label)
    {
        if (!_fromLobbyTransition || _firstDefenseCoachShown || playerHud == null)
            return;

        _firstDefenseCoachShown = true;
        StartCoroutine(CoShowCoachMessage(
            ResolveDefenseCoachMessage(telegraphType),
            telegraphType,
            Mathf.Max(coachHold, leadTime),
            telegraphType == AttackTelegraphType.Danger,
            coachDelayAfterTelegraph));
    }

    void HandlePunishWindowOpened(float duration, float damageMultiplier, string patternName)
    {
        if (!_fromLobbyTransition || _firstPunishCoachShown || playerHud == null)
            return;

        _firstPunishCoachShown = true;
        StartCoroutine(CoShowCoachMessage(
            firstPunishCoachMessage,
            AttackTelegraphType.Parry,
            Mathf.Max(0.42f, duration),
            false,
            0.08f));
    }

    void HandlePlayerDamaged(int damage)
    {
        if (!_fromLobbyTransition || _firstDamageCoachShown || playerHud == null)
            return;

        _firstDamageCoachShown = true;
        StartCoroutine(CoShowCoachMessage(
            firstDamageCoachMessage,
            AttackTelegraphType.Guard,
            coachHold,
            false,
            0.08f));
    }

    void HandleBossPhaseChanged(int phase, float hpNormalized)
    {
        if (!_fromLobbyTransition || playerHud == null)
            return;

        if (phase == 2 && !_phaseTwoCoachShown)
        {
            _phaseTwoCoachShown = true;
            StartCoroutine(CoShowCoachMessage(
                phaseTwoCoachMessage,
                AttackTelegraphType.Guard,
                coachHold,
                false,
                phaseCoachDelay));
            return;
        }

        if (phase >= 3 && !_phaseThreeCoachShown)
        {
            _phaseThreeCoachShown = true;
            StartCoroutine(CoShowCoachMessage(
                phaseThreeCoachMessage,
                AttackTelegraphType.Danger,
                Mathf.Max(coachHold, 0.82f),
                true,
                phaseCoachDelay));
        }
    }

    void HandleBossBreakEnter()
    {
        if (!_fromLobbyTransition || _breakCoachShown || playerHud == null)
            return;

        _breakCoachShown = true;
        StartCoroutine(CoShowCoachMessage(
            breakCoachMessage,
            AttackTelegraphType.Parry,
            breakCoachHold,
            false,
            0.06f));
    }

    void HandleUltimateStarted()
    {
        _ultimateReadyCoachShown = true;
    }

    void HandleUltimateGaugeChanged(float gauge, float normalized, bool ready)
    {
        if (!_fromLobbyTransition || _ultimateReadyCoachShown || !ready || playerHud == null)
            return;

        _ultimateReadyCoachShown = true;
        playerHud.ShowRuntimeTelegraphMessage(
            ultimateReadyMessage,
            AttackTelegraphType.Danger,
            ultimateReadyHold,
            false);
    }

    IEnumerator CoShowCoachMessage(string message, AttackTelegraphType telegraphType, float hold, bool useDangerFeedback, float delay)
    {
        if (delay > 0f)
            yield return new WaitForSeconds(delay);

        if (playerHud != null)
            playerHud.ShowRuntimeTelegraphMessage(message, telegraphType, hold, useDangerFeedback);
    }

    string ResolveDefenseCoachMessage(AttackTelegraphType telegraphType)
    {
        switch (telegraphType)
        {
            case AttackTelegraphType.Parry:
                return "\ubc1b\uc544\ub0bc \uc218 \uc788\ub2e4. \ud0c0\uc774\ubc0d\uc744 \ub05d\uae4c\uc9c0 \ubd10";
            case AttackTelegraphType.Guard:
                return "\uc815\uba74\uc5d0\uc11c \ubc1b\uc544. \ub9ac\ub4ec\uc744 \ub04a\uc9c0 \ub9c8";
            case AttackTelegraphType.Danger:
            case AttackTelegraphType.Dodge:
            case AttackTelegraphType.Auto:
            default:
                return firstDefenseCoachMessage;
        }
    }
}
