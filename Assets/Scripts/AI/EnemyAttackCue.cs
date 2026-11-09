using UnityEngine;

// 잡몹 공격 큐 (정직한 큐: Issue(type, lead) 는 '지금부터 lead 초 뒤에 판정'일 때만 호출된다 → EnemyBrain 이 보장)
//  - 섬광: BossPatternHandTelegraph (머리 뼈/바이저 또는 'PatternPivot' 위 HDR 섬광, 보스와 같은 머티리얼)
//  - 바닥: BossGroundTelegraph 사각 데칼 (채움이 판정 순간에 끝에 닿는다) — 공격 범위 = footprint 박스
//  - 소리: Resources/Audio/TelegraphCue_* (보스와 같은 타입별 효과음)
[DisallowMultipleComponent]
public sealed class EnemyAttackCue : MonoBehaviour
{
    [SerializeField] BossPatternHandTelegraph flash;
    [SerializeField] BossGroundTelegraph ground;
    [Tooltip("공격 범위 발자국 (비활성 트리거 박스). 비우면 런타임 생성.")]
    [SerializeField] BoxCollider footprint;
    [SerializeField, Range(0.2f, 2f)] float flashScale = 0.75f;
    [SerializeField] bool showGroundDecal = true;
    [SerializeField, Range(0f, 1f)] float cueVolume = 0.8f;
    [SerializeField] AudioClip parryableClip;
    [SerializeField] AudioClip dodgeOnlyClip;
    [SerializeField] AudioClip heavyClip;
    [SerializeField] AudioClip unblockableClip;

    /// <summary>QA: 마지막 큐 시각(Time.time)/공지 시간/타입, 누적 횟수.</summary>
    public float LastIssueTime { get; private set; } = -1f;
    public float LastAnnouncedLead { get; private set; }
    public EnemyCueType LastType { get; private set; }
    public int IssueCount { get; private set; }

    Vector3 _pinPosition;
    Quaternion _pinRotation = Quaternion.identity;
    float _pinUntil;

    void Awake()
    {
        if (flash == null) flash = GetComponent<BossPatternHandTelegraph>();
        if (ground == null) ground = GetComponent<BossGroundTelegraph>();
        if (parryableClip == null) parryableClip = Resources.Load<AudioClip>("Audio/TelegraphCue_Parryable");
        if (dodgeOnlyClip == null) dodgeOnlyClip = Resources.Load<AudioClip>("Audio/TelegraphCue_DodgeOnly");
        if (heavyClip == null) heavyClip = Resources.Load<AudioClip>("Audio/TelegraphCue_Heavy");
        if (unblockableClip == null) unblockableClip = Resources.Load<AudioClip>("Audio/TelegraphCue_Unblockable");
        EnsureFootprint(transform);
    }

    void EnsureFootprint(Transform parent)
    {
        if (footprint != null)
            return;
        var go = new GameObject("CueFootprint");
        go.layer = gameObject.layer;
        go.transform.SetParent(parent, false);
        footprint = go.AddComponent<BoxCollider>();
        footprint.isTrigger = true;
        footprint.enabled = false;
    }

    /// <summary>
    /// 큐 발행. lead = 실제 판정까지 남은 시간(초). range/width = 공격 범위(소유자 전방 기준, 미터).
    /// owner = 데칼 방향 기준 (기본: 이 오브젝트). 데칼은 owner 앞쪽 [0, range] 를 덮는다.
    /// </summary>
    public void Issue(EnemyCueType type, float lead, float range, float width, Transform owner = null)
    {
        Transform frame = owner != null ? owner : transform;
        IssueAt(type, lead, frame.position, frame.eulerAngles.y, range, width);
    }

    /// <summary>바닥 원점/방위를 직접 지정 (벽 터렛: 원점 = 바닥 투영점).</summary>
    public void IssueAt(EnemyCueType type, float lead, Vector3 groundOrigin, float yawDegrees, float range, float width)
    {
        if (type == EnemyCueType.None || lead <= 0.01f)
            return;

        LastIssueTime = Time.time;
        LastAnnouncedLead = lead;
        LastType = type;
        IssueCount++;

        if (flash != null)
            flash.Play(lead, EnemyCuePalette.GetColor(type), flashScale);

        if (showGroundDecal && ground != null && range > 0.1f)
        {
            // 데칼은 큐 순간의 위치/방향에 바닥 고정 (돌진해도 미끄러지지 않음) — 판정 사각형과 같은 모양
            _pinPosition = groundOrigin;
            _pinRotation = Quaternion.Euler(0f, yawDegrees, 0f);
            _pinUntil = Time.time + lead + 0.15f;
            footprint.transform.SetPositionAndRotation(_pinPosition, _pinRotation);
            footprint.center = new Vector3(0f, 0.05f, range * 0.5f);
            footprint.size = new Vector3(Mathf.Max(0.3f, width), 0.1f, range);
            ground.ShowCue(footprint, EnemyCuePalette.ToGroundType(type), lead, footprint.transform);
        }

        AudioClip clip = ResolveClip(type);
        if (clip != null)
            OneShotAudio.PlayAt(clip, transform.position + Vector3.up * 1.2f, cueVolume, 1.12f, 0.75f);
    }

    void LateUpdate()
    {
        if (footprint == null || Time.time > _pinUntil)
            return;
        footprint.transform.SetPositionAndRotation(_pinPosition, _pinRotation);
    }

    public void Cancel()
    {
        _pinUntil = 0f;
        if (ground != null)
            ground.HideCue();
        if (flash != null)
            flash.Hide();
    }

    AudioClip ResolveClip(EnemyCueType type)
    {
        switch (type)
        {
            case EnemyCueType.Parryable: return parryableClip;
            case EnemyCueType.DodgeOnly: return dodgeOnlyClip;
            case EnemyCueType.Heavy: return heavyClip;
            case EnemyCueType.Unblockable: return unblockableClip;
            default: return null;
        }
    }
}
