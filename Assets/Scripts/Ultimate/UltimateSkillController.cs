using UnityEngine;
using UnityEngine.Playables;

[DisallowMultipleComponent]
[RequireComponent(typeof(PlayableDirector))]
public sealed class UltimateSkillController : MonoBehaviour
{
    [Header("Compatibility")]
    [SerializeField] private PlayerUltimateController primaryController;

    [Header("Timeline")]
    [SerializeField] private PlayableDirector director;
    [SerializeField] private PlayableAsset ultimateTimelineAsset;
    [SerializeField] private bool useUnscaledDirectorTime = true;

    [Header("Input Blocking")]
    [SerializeField] private MonoBehaviour inputBlockerBehaviour;
    [SerializeField] private bool blockAllInputsDuringCutscene = true;

    [Header("Invulnerability")]
    [SerializeField] private MonoBehaviour invulnerabilityToggleBehaviour;
    [SerializeField] private bool setInvulnerableDuringCutscene = true;

    [Header("Time Scale")]
    [SerializeField] private bool freezeTimeScaleDuringCutscene = false;

    [Header("Debug")]
    [SerializeField] private bool enableDebugHotkey = false;
    [SerializeField] private KeyCode debugHotkey = KeyCode.R;
    [SerializeField] private bool debugLog = false;

    private IInputBlocker inputBlocker;
    private IInvulnerabilityToggle invulnerabilityToggle;
    private bool isCutscenePlaying;
    private float cachedPrevTimeScale = 1f;

    private bool HasPrimaryController => primaryController != null;
    public bool IsCutscenePlaying => HasPrimaryController ? primaryController.IsCinematic : isCutscenePlaying;

    private void Reset()
    {
        ResolveReferences();
        SyncPrimaryControllerBindings();
        ConfigureStandaloneDirector();
    }

    private void Awake()
    {
        ResolveReferences();

        inputBlocker = inputBlockerBehaviour as IInputBlocker;
        invulnerabilityToggle = invulnerabilityToggleBehaviour as IInvulnerabilityToggle;

        SyncPrimaryControllerBindings();
        ConfigureStandaloneDirector();
        RefreshDebugTickState();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (Application.isPlaying)
            return;

        ResolveReferences();
        SyncPrimaryControllerBindings();
        ConfigureStandaloneDirector();
    }
#endif

    private void OnEnable()
    {
        RefreshDebugTickState();
        if (!HasPrimaryController && director != null)
            director.stopped += HandleDirectorStopped;
    }

    private void OnDisable()
    {
        if (director != null)
            director.stopped -= HandleDirectorStopped;

        if (!HasPrimaryController && isCutscenePlaying)
            EndCutscene(force: true);
    }

    private void Update()
    {
        if (!enableDebugHotkey)
            return;

        if (Input.GetKeyDown(debugHotkey))
            TryPlayUltimate();
    }

    public bool TryPlayUltimate()
    {
        ResolveReferences();
        SyncPrimaryControllerBindings();

        if (HasPrimaryController)
            return primaryController.TryActivate();

        if (director == null || director.playableAsset == null)
        {
            if (debugLog)
                Debug.LogWarning("[Ultimate] Standalone fallback is missing a director or playable asset.", this);
            return false;
        }

        if (isCutscenePlaying)
            return false;

        BeginCutscene();
        return true;
    }

    private void BeginCutscene()
    {
        isCutscenePlaying = true;
        RefreshDebugTickState();

        if (blockAllInputsDuringCutscene && inputBlocker != null)
            inputBlocker.BlockAll(true);

        if (setInvulnerableDuringCutscene && invulnerabilityToggle != null)
            invulnerabilityToggle.SetInvulnerable(true);

        if (freezeTimeScaleDuringCutscene)
        {
            cachedPrevTimeScale = Time.timeScale;
            Time.timeScale = 0f;
        }

        director.time = 0d;
        director.Play();

        if (debugLog)
            Debug.Log("[Ultimate] Standalone fallback cutscene begin", this);
    }

    private void HandleDirectorStopped(PlayableDirector _)
    {
        if (!isCutscenePlaying)
            return;

        EndCutscene(force: false);
    }

    private void EndCutscene(bool force)
    {
        if (force && director != null && director.state == PlayState.Playing)
            director.Stop();

        if (freezeTimeScaleDuringCutscene)
            Time.timeScale = cachedPrevTimeScale;

        if (setInvulnerableDuringCutscene && invulnerabilityToggle != null)
            invulnerabilityToggle.SetInvulnerable(false);

        if (blockAllInputsDuringCutscene && inputBlocker != null)
            inputBlocker.BlockAll(false);

        isCutscenePlaying = false;
        RefreshDebugTickState();

        if (debugLog)
            Debug.Log("[Ultimate] Standalone fallback cutscene end", this);
    }

    private void ResolveReferences()
    {
        if (primaryController == null)
            primaryController = GetComponent<PlayerUltimateController>();

        if (director == null)
            director = GetComponent<PlayableDirector>();
    }

    private void SyncPrimaryControllerBindings()
    {
        if (!HasPrimaryController)
            return;

        if (primaryController.director == null && director != null)
            primaryController.director = director;
    }

    private void ConfigureStandaloneDirector()
    {
        if (director == null)
            return;

        if (ultimateTimelineAsset != null)
            director.playableAsset = ultimateTimelineAsset;

        director.timeUpdateMode = useUnscaledDirectorTime
            ? DirectorUpdateMode.UnscaledGameTime
            : DirectorUpdateMode.GameTime;
    }

    private void RefreshDebugTickState()
    {
        enabled = enableDebugHotkey || isCutscenePlaying;
    }
}
