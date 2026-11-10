using UnityEngine;
using UnityEngine.Playables;

/// <summary>
/// CinematicDirector 가 아닌 다른 컨트롤러가 재생하는 PlayableDirector(예: MainSceneArrivalController 의 Type-D 리빌 v2)에서
/// 자식 CineAttach 샷 루트를 재생 시작 순간 플레이어/보스 위치에 맞춘다. director.played 이벤트만 구독한다 (Update 없음).
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
