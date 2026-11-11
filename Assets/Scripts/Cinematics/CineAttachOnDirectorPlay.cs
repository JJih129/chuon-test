using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Playables;

/// <summary>
/// CinematicDirector 가 아닌 다른 컨트롤러가 재생하는 PlayableDirector(예: MainSceneArrivalController 의 Type-D 리빌 v2)에서
/// 자식 CineAttach 샷 루트를 재생 시작 순간 플레이어/보스 위치에 맞춘다. director.played 이벤트로 맞추고, follow 루트는 재생 중 Update 에서 따라간다.
/// </summary>
[DisallowMultipleComponent]
public sealed class CineAttachOnDirectorPlay : MonoBehaviour
{
    [SerializeField] PlayableDirector director;

    CineAttach[] _attachments;
    // 반복 20: 샷 카메라는 프리팹에서 꺼 둔다(에디트 모드 RotationComposer 가 씬에 회전 오버라이드를 만들던 문제). 재생 시작에 켜고, 멈춘 뒤 잠시 후 끈다.
    CinemachineCamera[] _shotCams;
    float _disableCamsAt = -1f;
    const float DisableCamsDelay = 1.5f;

    public void Configure(PlayableDirector target) => director = target;

    void Awake()
    {
        if (director == null)
            director = GetComponentInParent<PlayableDirector>();
        _attachments = GetComponentsInChildren<CineAttach>(true);
        _shotCams = GetComponentsInChildren<CinemachineCamera>(true);
    }

    void OnEnable()
    {
        if (director != null)
        {
            director.played += HandlePlayed;
            director.stopped += HandleStopped;
        }
    }

    void OnDisable()
    {
        if (director != null)
        {
            director.played -= HandlePlayed;
            director.stopped -= HandleStopped;
        }
    }

    // 반복 15: follow 샷 루트(보스 머리 등)를 재생 중 매 프레임 갱신. 예전엔 played 순간 1회뿐이라
    // 리빌 걸음으로 Type-D 가 5 m 앞으로 나온 뒤 바이저 인서트(5.55 s)가 빈 자리(바닥)를 비췄다.
    void Update()
    {
        if (_disableCamsAt >= 0f && Time.unscaledTime >= _disableCamsAt)
        {
            _disableCamsAt = -1f;
            if (director == null || director.state != PlayState.Playing)
                SetShotCamsEnabled(false);
        }
        if (director == null || _attachments == null || director.state != PlayState.Playing)
            return;
        for (int i = 0; i < _attachments.Length; i++)
        {
            CineAttach a = _attachments[i];
            if (a != null && a.Follow)
                a.Tick();
        }
    }

    void SetShotCamsEnabled(bool on)
    {
        if (_shotCams == null)
            return;
        for (int i = 0; i < _shotCams.Length; i++)
            if (_shotCams[i] != null && _shotCams[i].enabled != on)
                _shotCams[i].enabled = on;
    }

    // 블렌드 아웃이 끝난 뒤 끈다 (멈춘 즉시 끄면 복귀 블렌드의 출발 카메라가 사라진다)
    void HandleStopped(PlayableDirector d) => _disableCamsAt = Time.unscaledTime + DisableCamsDelay;

    void HandlePlayed(PlayableDirector d)
    {
        _disableCamsAt = -1f;
        SetShotCamsEnabled(true);
        PlayerReferences refs = GameplaySceneCache.ResolvePlayerReferences();
        Transform player = refs != null ? refs.PlayerRoot : null;
        BossController boss = GameplaySceneCache.ResolveBossController();
        Transform bossRoot = boss != null ? boss.transform : null;
        for (int i = 0; i < _attachments.Length; i++)
            if (_attachments[i] != null)
                _attachments[i].Begin(player, bossRoot);
    }
}
