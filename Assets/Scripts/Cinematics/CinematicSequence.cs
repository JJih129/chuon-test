using System;
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Playables;
using UnityEngine.Timeline;

/// <summary>
/// 연출 리그 루트 (프리팹 CINE_*.prefab). PlayableDirector + 타임라인 + 샷 카메라·소품·퍼펫을 한 덩어리로 둔다.
/// CinematicDirector.Play(id) 로만 재생한다 (playOnAwake 금지). 리그 안 바인딩(퍼펫 애니메이터, 샷 카메라)은 프리팹에 직렬화되고,
/// 씬 대상(시네머신 브레인, 플레이어/보스 애니메이터 등)은 재생 시작 때 bindings 표대로 1회 연결한다.
/// 샷 문법·앵커 목록: Docs/Cinematics_Shotlist.md.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(PlayableDirector))]
public sealed class CinematicSequence : MonoBehaviour
{
    public enum BindingRole : byte
    {
        MainCameraBrain = 0,
        Player = 1,
        Boss = 2,
        Anchor = 3,
        Overlay = 4,
    }

    [Serializable]
    public sealed class Binding
    {
        public string trackName;
        public BindingRole role;
        [Tooltip("role = Anchor 일 때 CinematicAnchor 이름")]
        public string anchorName;
    }

    [Header("Identity")]
    [SerializeField] string sequenceId = "cine";
    [Tooltip("문서용: 이 리그가 놓이는 씬")]
    [SerializeField] string hostScene;

    [Header("Skip")]
    [SerializeField] bool skippable = true;
    [SerializeField, Min(0.2f)] float skipHoldSeconds = 0.7f;
    [SerializeField] CinematicSettings.SeenScope seenScope = CinematicSettings.SeenScope.Run;

    [Header("Gameplay")]
    [SerializeField] bool blockPlayerInput = true;
    [SerializeField] bool makePlayerInvulnerable = true;
    [SerializeField] bool pauseBoss = true;
    [Tooltip("락온 카메라 관리자에게서 카메라를 가져온다 (타임라인이 카메라 소유).")]
    [SerializeField] bool takeCameraFromLockOn = true;
    [Tooltip("실제 플레이어 렌더러를 끈다 (리그 안 퍼펫이 대신 연기할 때).")]
    [SerializeField] bool hideRealPlayer;
    [Tooltip("이 이름의 HUD 오브젝트를 재생 중 숨긴다 (CanvasGroup alpha 0).")]
    [SerializeField] string[] hudObjectNames = { "Player_HUD", "PlayerHUD", "Boss_HUD", "BossHUD" };

    [Header("Presentation")]
    [SerializeField] bool letterbox = true;
    [SerializeField, Min(0f)] float letterboxRamp = 0.35f;
    [Tooltip("게임플레이 복귀 시 남은 페이드(검정)를 이 시간 동안 걷는다 (카메라 리그 0.35 s 블렌드와 같이).")]
    [SerializeField, Min(0f)] float returnFadeSeconds = 0.35f;
    [Tooltip("재생 중에만 켜는 무대(세트·조명·퍼펫). 비우면 리그 루트 자식 'Stage'.")]
    [SerializeField] GameObject stageRoot;
    [Tooltip("안전 장치: 이 시간(실시간)이 지나면 강제로 끝낸다. 0 = 타임라인 길이 + 2 s")]
    [SerializeField, Min(0f)] float maxRealtimeSeconds;

    [Header("Scene bindings (resolved on play)")]
    [SerializeField] List<Binding> bindings = new List<Binding>();

    [Header("Events")]
    public UnityEvent onStarted = new UnityEvent();
    public UnityEvent onFinished = new UnityEvent();
    [Tooltip("건너뛰기/재도전 생략 시 (onFinished 보다 먼저). 끝 상태(문 열림 등)를 맞추는 데 쓴다.")]
    public UnityEvent onSkipped = new UnityEvent();

    PlayableDirector _director;
    CineAttach[] _attachments;
    Renderer[] _hiddenRenderers;
    bool[] _hiddenRendererStates;

    public string Id => sequenceId;
    public string HostScene => hostScene;
    public bool Skippable => skippable;
    public float SkipHoldSeconds => skipHoldSeconds;
    public CinematicSettings.SeenScope SeenScope => seenScope;
    public bool BlockPlayerInput => blockPlayerInput;
    public bool MakePlayerInvulnerable => makePlayerInvulnerable;
    public bool PauseBoss => pauseBoss;
    public bool TakeCameraFromLockOn => takeCameraFromLockOn;
    public bool UseLetterbox => letterbox;
    public float LetterboxRamp => letterboxRamp;
    public float ReturnFadeSeconds => returnFadeSeconds;
    public string[] HudObjectNames => hudObjectNames;
    public PlayableDirector Director => _director != null ? _director : (_director = GetComponent<PlayableDirector>());
    public double Duration => Director.playableAsset != null ? Director.playableAsset.duration : 0d;
    public float MaxRealtimeSeconds => maxRealtimeSeconds > 0f ? maxRealtimeSeconds : (float)Duration + 2f;

    /// <summary>에디터 빌더/설치기용 설정.</summary>
    public void Configure(string id, string scene, bool canSkip, CinematicSettings.SeenScope scope, bool blockInput, bool pauseBossAi, bool hidePlayer, bool useLetterbox)
    {
        sequenceId = id;
        hostScene = scene;
        skippable = canSkip;
        seenScope = scope;
        blockPlayerInput = blockInput;
        makePlayerInvulnerable = blockInput;
        pauseBoss = pauseBossAi;
        hideRealPlayer = hidePlayer;
        letterbox = useLetterbox;
    }

    public void SetStageRoot(GameObject stage) => stageRoot = stage;

    public void SetBindings(IEnumerable<Binding> list)
    {
        bindings.Clear();
        if (list != null)
            bindings.AddRange(list);
    }

    void Awake()
    {
        PlayableDirector d = Director;
        d.playOnAwake = false;
        d.timeUpdateMode = DirectorUpdateMode.UnscaledGameTime;
        d.extrapolationMode = DirectorWrapMode.None;
        _attachments = GetComponentsInChildren<CineAttach>(true);
        if (stageRoot == null)
        {
            Transform stage = transform.Find("Stage");
            if (stage != null)
                stageRoot = stage.gameObject;
        }
        if (stageRoot != null)
            stageRoot.SetActive(false);
    }

    void OnEnable() => CinematicDirector.Register(this);
    void OnDisable() => CinematicDirector.Unregister(this);

    // ------------------------------------------------------------------ lifecycle (CinematicDirector 가 호출)

    internal void Begin(Transform player, Transform boss, PlayerReferences playerRefs)
    {
        if (stageRoot != null)
            stageRoot.SetActive(true);

        if (_attachments == null)
            _attachments = GetComponentsInChildren<CineAttach>(true);
        for (int i = 0; i < _attachments.Length; i++)
        {
            if (_attachments[i] != null && !_attachments[i].Begin(player, boss))
                GameLog.Warn($"[Cinematic] {sequenceId}: attach target missing on '{_attachments[i].name}' ({_attachments[i].AttachTarget} {_attachments[i].AnchorName})", this);
        }

        BindSceneTracks(player, boss);

        if (hideRealPlayer && playerRefs != null)
            HidePlayer(playerRefs);
    }

    internal void Tick()
    {
        if (_attachments == null)
            return;
        for (int i = 0; i < _attachments.Length; i++)
        {
            CineAttach a = _attachments[i];
            if (a != null && a.Follow)
                a.Tick();
        }
    }

    internal void End()
    {
        RestorePlayer();
        if (_attachments != null)
            for (int i = 0; i < _attachments.Length; i++)
                if (_attachments[i] != null)
                    _attachments[i].End();
        if (stageRoot != null)
            stageRoot.SetActive(false);
    }

    void BindSceneTracks(Transform player, Transform boss)
    {
        PlayableDirector d = Director;
        if (!(d.playableAsset is TimelineAsset timeline))
            return;

        CinemachineBrain brain = ResolveBrain();
        foreach (TrackAsset track in timeline.GetOutputTracks())
        {
            if (track == null)
                continue;

            Binding rule = FindRule(track.name);
            if (rule == null)
            {
                // 규칙이 없는 시네머신 트랙은 항상 메인 브레인에 붙인다.
                if (track is CinemachineTrack && brain != null && d.GetGenericBinding(track) == null)
                    d.SetGenericBinding(track, brain);
                continue;
            }

            UnityEngine.Object value = null;
            switch (rule.role)
            {
                case BindingRole.MainCameraBrain: value = brain; break;
                case BindingRole.Player: value = Adapt(track, player); break;
                case BindingRole.Boss: value = Adapt(track, boss); break;
                case BindingRole.Anchor: value = Adapt(track, CinematicAnchor.Resolve(rule.anchorName)); break;
                case BindingRole.Overlay: value = null; break;
            }

            if (value != null)
                d.SetGenericBinding(track, value);
            else if (rule.role != BindingRole.Overlay)
                GameLog.Warn($"[Cinematic] {sequenceId}: binding for track '{track.name}' ({rule.role}) not found", this);
        }
    }

    Binding FindRule(string trackName)
    {
        for (int i = 0; i < bindings.Count; i++)
            if (bindings[i] != null && bindings[i].trackName == trackName)
                return bindings[i];
        return null;
    }

    static UnityEngine.Object Adapt(TrackAsset track, Transform target)
    {
        if (target == null)
            return null;
        Type bindingType = null;
        foreach (PlayableBinding output in track.outputs)
        {
            bindingType = output.outputTargetType;
            break;
        }

        if (bindingType == null || bindingType == typeof(GameObject))
            return target.gameObject;
        if (bindingType == typeof(Transform))
            return target;
        if (typeof(Component).IsAssignableFrom(bindingType))
        {
            Component c = target.GetComponentInChildren(bindingType, true);
            return c;
        }
        return target.gameObject;
    }

    static CinemachineBrain ResolveBrain()
    {
        Camera main = GameplaySceneCache.ResolveMainCamera();
        CinemachineBrain brain = main != null ? main.GetComponent<CinemachineBrain>() : null;
        return brain != null ? brain : FindAnyObjectByType<CinemachineBrain>();
    }

    void HidePlayer(PlayerReferences refs)
    {
        Transform visual = refs.VisualRoot != null ? refs.VisualRoot : refs.PlayerRoot;
        _hiddenRenderers = visual.GetComponentsInChildren<Renderer>(false);
        _hiddenRendererStates = new bool[_hiddenRenderers.Length];
        for (int i = 0; i < _hiddenRenderers.Length; i++)
        {
            Renderer r = _hiddenRenderers[i];
            if (r == null || r.transform.IsChildOf(transform))
                continue;
            _hiddenRendererStates[i] = r.forceRenderingOff;
            r.forceRenderingOff = true;
        }
    }

    void RestorePlayer()
    {
        if (_hiddenRenderers == null)
            return;
        for (int i = 0; i < _hiddenRenderers.Length; i++)
            if (_hiddenRenderers[i] != null)
                _hiddenRenderers[i].forceRenderingOff = _hiddenRendererStates[i];
        _hiddenRenderers = null;
        _hiddenRendererStates = null;
    }
}
