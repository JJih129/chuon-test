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
    [Tooltip("0 보다 크면 끝 화면 덮개(페이드·레터박스)를 이 시간(실시간) 유지한 뒤 걷는다 — 씬 전환으로 끝나는 연출(튜토리얼 종료)이 " +
             "복귀 페이드 동안 게임플레이 화면을 비추지 않게. 씬이 바뀌면 오버레이와 함께 사라진다. 반복 16.")]
    [SerializeField, Min(0f)] float holdEndCoverSeconds;
    [Tooltip("재생 중에만 켜는 무대(세트·조명·퍼펫). 비우면 리그 루트 자식 'Stage'.")]
    [SerializeField] GameObject stageRoot;
    [Tooltip("안전 장치: 이 시간(실시간)이 지나면 강제로 끝낸다. 0 = 타임라인 길이 + 2 s")]
    [SerializeField, Min(0f)] float maxRealtimeSeconds;

    [Header("Prewarm (iter 15)")]
    [Tooltip("씬 로드 프레임(Start)에 무대를 한 번 켜고 타임라인 그래프 생성 + 전용 카메라 1회 렌더(셰이더·텍스처·안개 변형) 후 다시 끈다. " +
             "모두 같은 프레임 안에서 끝나므로 화면에는 보이지 않는다. 첫 프레임 히치(경보 78 ms·엘리베이터 59 ms) 대책.")]
    [SerializeField] bool prewarmOnLoad = true;

    [Header("Scene bindings (resolved on play)")]
    [SerializeField] List<Binding> bindings = new List<Binding>();

    [Header("Events")]
    public UnityEvent onStarted = new UnityEvent();
    public UnityEvent onFinished = new UnityEvent();
    [Tooltip("건너뛰기/재도전 생략 시 (onFinished 보다 먼저). 끝 상태(문 열림 등)를 맞추는 데 쓴다.")]
    public UnityEvent onSkipped = new UnityEvent();

    PlayableDirector _director;
    CineAttach[] _attachments;
    bool _prewarmed;
    static Camera s_warmCamera;
    static RenderTexture s_warmTarget;
    static readonly List<GameObject> s_warmActivated = new List<GameObject>(64);

    /// <summary>마지막 예열에 걸린 시간 (ms, 개발 로그용).</summary>
    public float LastPrewarmMs { get; private set; } = -1f;
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
    public float HoldEndCoverSeconds => holdEndCoverSeconds;
    public void SetHoldEndCover(float seconds) => holdEndCoverSeconds = Mathf.Max(0f, seconds);
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

    // 씬 시작 직후 바로 재생되는 연출은 로드 프레임에 즉시 예열, 나머지는 0.6 s 뒤 프레임당 1개씩 (첫 연출의 첫 프레임에 몰리지 않게)
    static readonly string[] s_immediatePrewarmIds = { "tut_vr_boot", "opening" };
    static readonly List<CinematicSequence> s_prewarmQueue = new List<CinematicSequence>(8);
    static float s_prewarmQueueAt;
    static int s_prewarmQueueFrame;

    void Start()
    {
        if (!prewarmOnLoad || CinematicSettings.QaSkipAll)
            return;
        if (Array.IndexOf(s_immediatePrewarmIds, sequenceId) >= 0)
        {
            PrewarmNow();
            return;
        }
        if (s_prewarmQueue.Count == 0)
            s_prewarmQueueAt = Time.realtimeSinceStartup + 0.6f;
        s_prewarmQueue.Add(this);
    }

    void LateUpdate()
    {
        // 큐의 첫 원소만 처리 (프레임당 1개). 재생 중인 연출과 겹치면 다음 프레임으로 미루지 않고 건너뛴다.
        if (s_prewarmQueue.Count == 0 || s_prewarmQueue[0] != this || Time.realtimeSinceStartup < s_prewarmQueueAt || s_prewarmQueueFrame == Time.frameCount)
            return;
        s_prewarmQueueFrame = Time.frameCount;
        s_prewarmQueue.RemoveAt(0);
        PrewarmNow();
    }

    /// <summary>
    /// 무대 예열 (동기, 한 프레임 안). 무대·비활성 소품을 잠깐 켜서 Awake/OnEnable·TMP 메시를 끝내고, 타임라인 그래프를 미리 만들고,
    /// 숨은 카메라로 무대 전체를 한 번 렌더해 셰이더 변형(무대 전용 안개·조명 포함)과 텍스처 업로드를 끝낸 뒤 원래 상태로 되돌린다.
    /// </summary>
    public void PrewarmNow()
    {
        if (_prewarmed || CinematicDirector.IsPlaying && CinematicDirector.CurrentId == sequenceId)
            return;
        _prewarmed = true;
        float t0 = Time.realtimeSinceStartup;
        GameObject root = stageRoot != null ? stageRoot : gameObject;
        bool wasActive = root.activeSelf;
        s_warmActivated.Clear();
        try
        {
            if (!wasActive)
                root.SetActive(true);
            // 활성화 트랙으로 나중에 켜지는 소품(드론·컷 전용 조명 등)도 한 번 켠다
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (!t.gameObject.activeSelf)
                {
                    t.gameObject.SetActive(true);
                    s_warmActivated.Add(t.gameObject);
                }
            }

            PlayableDirector d = Director;
            if (d != null && d.playableAsset != null && !d.playableGraph.IsValid())
                d.RebuildGraph();

            WarmRender(root);
            WarmShotCameras(root);
        }
        catch (Exception e)
        {
            Debug.LogException(e, this);
        }
        finally
        {
            for (int i = s_warmActivated.Count - 1; i >= 0; i--)
                if (s_warmActivated[i] != null)
                    s_warmActivated[i].SetActive(false);
            s_warmActivated.Clear();
            if (!wasActive && root != null)
                root.SetActive(false);
        }
        LastPrewarmMs = (Time.realtimeSinceStartup - t0) * 1000f;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        Debug.Log($"[Cinematic] '{sequenceId}' prewarmed in {LastPrewarmMs:F0} ms");
#endif
    }

    // 샷 카메라 자리에서도 한 번씩 렌더 (무대 밖 씬 배경 — 예: 튜토리얼 도시 크레인 샷의 첫 화면). 앵커 정렬 후 렌더, 끝나면 해제.
    void WarmShotCameras(GameObject root)
    {
        CinemachineCamera[] cams = root.GetComponentsInChildren<CinemachineCamera>(false);
        if (cams.Length == 0 || s_warmCamera == null && !EnsureWarmCamera())
            return;
        PlayerReferences refs = GameplaySceneCache.ResolvePlayerReferences();
        BossController boss = GameplaySceneCache.ResolveBossController();
        if (_attachments == null)
            _attachments = GetComponentsInChildren<CineAttach>(true);
        for (int i = 0; i < _attachments.Length; i++)
            if (_attachments[i] != null)
                _attachments[i].Begin(refs != null ? refs.PlayerRoot : null, boss != null ? boss.transform : null);
        try
        {
            for (int i = 0; i < cams.Length; i++)
            {
                Transform t = cams[i].transform;
                s_warmCamera.transform.SetPositionAndRotation(t.position, t.rotation);
                s_warmCamera.fieldOfView = Mathf.Clamp(cams[i].Lens.FieldOfView, 10f, 100f);
                s_warmCamera.nearClipPlane = 0.05f;
                s_warmCamera.farClipPlane = Mathf.Max(100f, cams[i].Lens.FarClipPlane);
                s_warmCamera.Render();
            }
        }
        finally
        {
            for (int i = 0; i < _attachments.Length; i++)
                if (_attachments[i] != null)
                    _attachments[i].End();
            s_warmCamera.fieldOfView = 75f;
        }
    }

    static bool EnsureWarmCamera()
    {
        if (s_warmCamera != null)
            return true;
        var go = new GameObject("__CineWarmCamera") { hideFlags = HideFlags.HideAndDontSave };
        s_warmCamera = go.AddComponent<Camera>();
        s_warmCamera.enabled = false;
        s_warmCamera.fieldOfView = 75f;
        s_warmCamera.clearFlags = CameraClearFlags.Skybox;
        s_warmTarget = new RenderTexture(256, 144, 24) { name = "__CineWarmTarget", hideFlags = HideFlags.HideAndDontSave };
        s_warmCamera.targetTexture = s_warmTarget;
        return true;
    }

    static void WarmRender(GameObject root)
    {
        bool any = false;
        Bounds b = default;
        foreach (Renderer r in root.GetComponentsInChildren<Renderer>(false))
        {
            if (!r.enabled)
                continue;
            if (!any) { b = r.bounds; any = true; }
            else b.Encapsulate(r.bounds);
        }
        if (!any)
            return;

        EnsureWarmCamera();

        float radius = Mathf.Max(1f, b.extents.magnitude);
        float dist = radius / Mathf.Tan(s_warmCamera.fieldOfView * 0.5f * Mathf.Deg2Rad);
        Vector3 dir = new Vector3(0.35f, -0.3f, 1f).normalized;
        s_warmCamera.transform.SetPositionAndRotation(b.center - dir * dist, Quaternion.LookRotation(dir, Vector3.up));
        s_warmCamera.nearClipPlane = Mathf.Max(0.05f, dist - radius * 1.1f);
        s_warmCamera.farClipPlane = dist + radius * 1.1f;
        s_warmCamera.Render();
    }

    void OnEnable() => CinematicDirector.Register(this);

    void OnDestroy() => s_prewarmQueue.Remove(this);
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
