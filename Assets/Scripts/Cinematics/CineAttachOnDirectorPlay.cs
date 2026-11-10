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

    public void Configure(PlayableDirector target) => director = target;

    void Awake()
    {
        if (director == null)
            director = GetComponentInParent<PlayableDirector>();
        _attachments = GetComponentsInChildren<CineAttach>(true);
    }

    void OnEnable()
    {
        if (director != null)
            director.played += HandlePlayed;
    }

    void OnDisable()
    {
        if (director != null)
            director.played -= HandlePlayed;
    }

    // 반복 15: follow 샷 루트(보스 머리 등)를 재생 중 매 프레임 갱신. 예전엔 played 순간 1회뿐이라
    // 리빌 걸음으로 Type-D 가 5 m 앞으로 나온 뒤 바이저 인서트(5.55 s)가 빈 자리(바닥)를 비췄다.
    void Update()
    {
        if (director == null || _attachments == null || director.state != PlayState.Playing)
            return;
        for (int i = 0; i < _attachments.Length; i++)
        {
            CineAttach a = _attachments[i];
            if (a != null && a.Follow)
                a.Tick();
        }
    }

    void HandlePlayed(PlayableDirector d)
    {
        PlayerReferences refs = GameplaySceneCache.ResolvePlayerReferences();
        Transform player = refs != null ? refs.PlayerRoot : null;
        BossController boss = GameplaySceneCache.ResolveBossController();
        Transform bossRoot = boss != null ? boss.transform : null;
        for (int i = 0; i < _attachments.Length; i++)
            if (_attachments[i] != null)
                _attachments[i].Begin(player, bossRoot);
    }
}
