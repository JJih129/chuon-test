using UnityEngine;
using UnityEngine.Playables;

[DisallowMultipleComponent]
public sealed class UltimateSkillController : MonoBehaviour
{
    const string DefaultSequenceResourcePath = "Ultimate/UltimateSequence_Default";
    const string IntroPoseClipResourcePath = "Ultimate/Clips/Sp_Idle_IntroPose";
    const string DashSlashClipResourcePath = "Ultimate/Clips/Sp_Skill3_Ultimate";

    [Header("기본 참조")]
    [SerializeField] private PlayerUltimateController primaryController;

    [Header("코드 주도 궁극기")]
    [SerializeField] private bool useCodeDrivenSequence = true;
    [SerializeField] private UltimateSequenceData sequenceData;
    [SerializeField] private UltimateSequencePlayer sequencePlayer;
    [SerializeField] private UltimateCinematicController cinematicController;
    [SerializeField] private UltimateTargetBinder targetBinder;
    [SerializeField] private UltimateCameraDirector cameraDirector;
    [SerializeField] private UltimateHitProcessor hitProcessor;
    [SerializeField] private UltimateVFXPresenter vfxPresenter;

    [Header("레거시 Timeline 폴백")]
    [SerializeField] private PlayableDirector director;
    [SerializeField] private PlayableAsset ultimateTimelineAsset;
    [SerializeField] private bool useUnscaledDirectorTime = true;
    [SerializeField] private bool allowLegacyDirectorFallback = true;

    [Header("입력 / 무적")]
    [SerializeField] private MonoBehaviour inputBlockerBehaviour;
    [SerializeField] private bool blockAllInputsDuringCutscene = true;
    [SerializeField] private MonoBehaviour invulnerabilityToggleBehaviour;
    [SerializeField] private bool setInvulnerableDuringCutscene = true;
    [SerializeField] private bool freezeTimeScaleDuringCutscene = false;

    [Header("디버그")]
    [SerializeField] private bool enableDebugHotkey = false;
    [SerializeField] private KeyCode debugHotkey = KeyCode.R;
    [SerializeField] private bool debugLog = false;

    IInputBlocker _inputBlocker;
    IInvulnerabilityToggle _invulnerabilityToggle;
    bool _isStandaloneCutscenePlaying;
    float _cachedPrevTimeScale = 1f;
    UltimateSequenceData _runtimeFallbackData;
    UltimateSequenceData _runtimeFallbackSource;

    bool HasPrimaryController => primaryController != null;
    public bool UseModernSequence => useCodeDrivenSequence;
    public bool IsCutscenePlaying => HasPrimaryController ? primaryController.IsCinematic : _isStandaloneCutscenePlaying;

    void Reset()
    {
        ResolveReferences(true);
        SyncPrimaryControllerBindings();
        ConfigureStandaloneDirector();
    }

    void Awake()
    {
        ResolveReferences(true);
        _inputBlocker = inputBlockerBehaviour as IInputBlocker;
        _invulnerabilityToggle = invulnerabilityToggleBehaviour as IInvulnerabilityToggle;
        SyncPrimaryControllerBindings();
        ConfigureStandaloneDirector();
        RefreshDebugTickState();
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        if (Application.isPlaying)
            return;

        ResolveReferences(false);
        SyncPrimaryControllerBindings();
        ConfigureStandaloneDirector();
    }
#endif

    void OnEnable()
    {
        RefreshDebugTickState();
        if (!HasPrimaryController && director != null)
            director.stopped += HandleDirectorStopped;
    }

    void OnDisable()
    {
        if (director != null)
            director.stopped -= HandleDirectorStopped;

        if (!HasPrimaryController && _isStandaloneCutscenePlaying)
            EndStandaloneCutscene(true);
    }

    void Update()
    {
        if (!enableDebugHotkey)
            return;

        if (Input.GetKeyDown(debugHotkey))
            TryPlayUltimate();
    }

    public bool TryPlayUltimate()
    {
        ResolveReferences(true);
        SyncPrimaryControllerBindings();

        if (HasPrimaryController)
            return primaryController.TryActivate();

        if (allowLegacyDirectorFallback)
            return TryPlayStandaloneFallback();

        return false;
    }

    public bool TryPlayModernUltimate(PlayerUltimateController owner)
    {
        if (!useCodeDrivenSequence)
            return false;

        ResolveReferences(true);
        SyncPrimaryControllerBindings();

        UltimateSequenceData resolvedData = sequenceData != null ? sequenceData : GetRuntimeFallbackData();
        if (cinematicController != null && cinematicController.Play(owner, resolvedData))
        {
            if (debugLog)
                Debug.Log("[Ultimate] Started timeline cinematic ultimate sequence.", this);
            return true;
        }

        if (sequencePlayer == null)
            return false;

        bool started = sequencePlayer.TryPlay(owner, resolvedData);
        if (debugLog && started)
            Debug.Log("[Ultimate] Started code-driven ultimate sequence.", this);
        return started;
    }

    bool TryPlayStandaloneFallback()
    {
        if (director == null || director.playableAsset == null)
        {
            if (debugLog)
                Debug.LogWarning("[Ultimate] Standalone fallback is missing a director or playable asset.", this);
            return false;
        }

        if (_isStandaloneCutscenePlaying)
            return false;

        BeginStandaloneCutscene();
        return true;
    }

    void BeginStandaloneCutscene()
    {
        _isStandaloneCutscenePlaying = true;
        RefreshDebugTickState();

        if (blockAllInputsDuringCutscene && _inputBlocker != null)
            _inputBlocker.BlockAll(true);

        if (setInvulnerableDuringCutscene && _invulnerabilityToggle != null)
            _invulnerabilityToggle.SetInvulnerable(true);

        if (freezeTimeScaleDuringCutscene)
        {
            _cachedPrevTimeScale = Time.timeScale;
            Time.timeScale = 0f;
        }

        director.time = 0d;
        director.Play();
    }

    void HandleDirectorStopped(PlayableDirector _)
    {
        if (!_isStandaloneCutscenePlaying)
            return;

        EndStandaloneCutscene(false);
    }

    void EndStandaloneCutscene(bool force)
    {
        if (force && director != null && director.state == PlayState.Playing)
            director.Stop();

        if (freezeTimeScaleDuringCutscene)
            Time.timeScale = _cachedPrevTimeScale;

        if (setInvulnerableDuringCutscene && _invulnerabilityToggle != null)
            _invulnerabilityToggle.SetInvulnerable(false);

        if (blockAllInputsDuringCutscene && _inputBlocker != null)
            _inputBlocker.BlockAll(false);

        _isStandaloneCutscenePlaying = false;
        RefreshDebugTickState();
    }

    void ResolveReferences(bool allowCreate)
    {
        if (primaryController == null)
            primaryController = GetComponent<PlayerUltimateController>();
        if (director == null)
            director = GetComponent<PlayableDirector>();
        if (sequencePlayer == null)
            sequencePlayer = GetComponent<UltimateSequencePlayer>() ?? (allowCreate ? gameObject.AddComponent<UltimateSequencePlayer>() : null);
        if (targetBinder == null)
            targetBinder = GetComponent<UltimateTargetBinder>() ?? (allowCreate ? gameObject.AddComponent<UltimateTargetBinder>() : null);
        if (cameraDirector == null)
            cameraDirector = GetComponent<UltimateCameraDirector>() ?? (allowCreate ? gameObject.AddComponent<UltimateCameraDirector>() : null);
        if (hitProcessor == null)
            hitProcessor = GetComponent<UltimateHitProcessor>() ?? (allowCreate ? gameObject.AddComponent<UltimateHitProcessor>() : null);
        if (vfxPresenter == null)
            vfxPresenter = GetComponent<UltimateVFXPresenter>() ?? (allowCreate ? gameObject.AddComponent<UltimateVFXPresenter>() : null);
    }

    void SyncPrimaryControllerBindings()
    {
        if (!HasPrimaryController)
            return;

        if (useCodeDrivenSequence && cinematicController != null)
        {
            if (primaryController.director == director)
                primaryController.director = null;
            return;
        }

        if (primaryController.director == null && director != null)
            primaryController.director = director;
    }

    void ConfigureStandaloneDirector()
    {
        if (director == null)
            return;

        if (ultimateTimelineAsset != null)
            director.playableAsset = ultimateTimelineAsset;

        director.timeUpdateMode = useUnscaledDirectorTime
            ? DirectorUpdateMode.UnscaledGameTime
            : DirectorUpdateMode.GameTime;
    }

    void RefreshDebugTickState()
    {
        enabled = enableDebugHotkey || _isStandaloneCutscenePlaying;
    }

    UltimateSequenceData GetRuntimeFallbackData()
    {
        if (sequenceData != null)
            return PrepareRuntimeSequenceData(sequenceData);

        UltimateSequenceData resourceData = Resources.Load<UltimateSequenceData>(DefaultSequenceResourcePath);
        if (resourceData != null)
            return PrepareRuntimeSequenceData(resourceData);

        if (_runtimeFallbackData == null)
        {
            _runtimeFallbackData = UltimateSequenceData.CreateRuntimeDefaultInstance();
            _runtimeFallbackSource = null;
        }

        PatchMissingCinematicClips(_runtimeFallbackData);
        return _runtimeFallbackData;
    }

    UltimateSequenceData PrepareRuntimeSequenceData(UltimateSequenceData source)
    {
        if (_runtimeFallbackData == null || _runtimeFallbackSource != source)
        {
            _runtimeFallbackData = Instantiate(source);
            _runtimeFallbackData.name = $"{source.name}_Runtime";
            _runtimeFallbackData.hideFlags = HideFlags.DontSave;
            _runtimeFallbackSource = source;
        }

        PatchMissingCinematicClips(_runtimeFallbackData);
        return _runtimeFallbackData;
    }

    void PatchMissingCinematicClips(UltimateSequenceData data)
    {
        if (data == null)
            return;

        AnimationClip introPoseClip = Resources.Load<AnimationClip>(IntroPoseClipResourcePath);
        if (introPoseClip != null)
        {
            data.CinematicAnimation.introPoseClip = introPoseClip;
        }
        else if (debugLog)
        {
            Debug.LogWarning(
                $"[Ultimate] Missing runtime intro pose clip resource at Resources/{IntroPoseClipResourcePath}.",
                this);
        }

        AnimationClip dashSlashClip = Resources.Load<AnimationClip>(DashSlashClipResourcePath);
        if (dashSlashClip != null)
        {
            data.CinematicAnimation.dashSlashClip = dashSlashClip;
        }
        else if (debugLog)
        {
            Debug.LogWarning(
                $"[Ultimate] Missing runtime dash slash clip resource at Resources/{DashSlashClipResourcePath}.",
                this);
        }
    }
}
