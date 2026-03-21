using UnityEngine;

/// <summary>
/// 보스가 데미지를 받을 때 발생하는 비주얼/애니메이션/사운드 반응 전담 컴포넌트.
/// - BossHealth.OnDamagedWithType 를 구독해서,
///   * 히트 이펙트 스폰
///   * Hit / Stagger 애니메이션 트리거 호출
///   * 히트 사운드 재생
/// 를 담당한다.
/// </summary>
[DisallowMultipleComponent]
public class BossHitReaction : MonoBehaviour
{
    bool _hasHitTrigger;
    bool _hasStaggerTrigger;
    float _lastVfxRealtime = float.NegativeInfinity;
    float _lastAudioRealtime = float.NegativeInfinity;
    float _lastHitFeelRealtime = float.NegativeInfinity;
    float _lastAnimationRealtime = float.NegativeInfinity;

    [Header("참조 (한글 설명)")]
    [Tooltip("같은 오브젝트 또는 부모에 있는 BossHealth를 지정. 비워두면 자동 검색.")]
    public BossHealth bossHealth;
    
    [Tooltip("보스 애니메이터. Hit / Stagger 트리거를 보낼 대상.")]
    public Animator bossAnimator;

    [Header("히트 이펙트 (한글 설명)")]
    [Tooltip("일반 공격 피격 시 사용할 VFX 프리팹 (선택).")]
    public GameObject normalHitVfx;

    [Tooltip("강공격/경직 공격 피격 시 사용할 VFX 프리팹 (선택).")]
    public GameObject heavyHitVfx;

    [Tooltip("이펙트 생성 위치 기준 트랜스폼 (없으면 보스 중심 위치 사용).")]
    public Transform vfxPivot;

    [Header("애니메이션 파라미터 이름 (한글 설명)")]
    [Tooltip("일반 피격 시 사용할 트리거 이름 (예: \"Hit\"). 비워두면 애니는 호출하지 않음.")]
    public string hitTriggerName = "Hit";

    [Tooltip("경직/강공격 피격 시 사용할 트리거 이름 (예: \"Stagger\").")]
    public string staggerTriggerName = "Stagger";

    [Header("경직 조건 (한글 설명)")]
    [Tooltip("이 HitType에 해당할 경우 경직 애니메이션과 헤비 이펙트/사운드를 사용한다.")]
    public HitType staggerHitType = HitType.Heavy;  // 프로젝트의 HitType 정의에 맞게 값만 맞춰서 사용

    [Header("히트 사운드 설정 (한글 설명)")]
    [Tooltip("히트 사운드를 재생할 AudioSource (없으면 런타임에 이 오브젝트에 자동 추가).")]
    public AudioSource audioSource;   // [조절값]

    [Tooltip("일반 피격 시 재생할 사운드 클립들 (여러 개 넣으면 랜덤으로 하나 재생).")]
    public AudioClip[] normalHitClips;    // [조절값]

    [Tooltip("경직/강공격 피격 시 재생할 사운드 클립들 (여러 개 넣으면 랜덤으로 하나 재생).")]
    public AudioClip[] heavyHitClips;     // [조절값]

    [Tooltip("히트 사운드 기본 볼륨 (0~1)")]
    [Range(0f, 1f)]
    public float hitVolume = 1f;         // [조절값]

    [Tooltip("같은 사운드가 반복될 때의 단조로움을 줄이기 위한 랜덤 피치 범위 (x = 최소, y = 최대). 둘 다 1이면 피치 고정.")]
    public Vector2 randomPitchRange = new Vector2(0.95f, 1.05f); // [조절값]

    [Header("Hit Feel Policy")]
    [SerializeField] bool useCombatFeelPolicy = true;
    [SerializeField] CameraShake hitCameraShake;

    [Header("Performance")]
    [SerializeField, Min(0f)] float minVfxIntervalRealtime = 0.05f;
    [SerializeField, Min(0f)] float minAudioIntervalRealtime = 0.06f;
    [SerializeField, Min(0f)] float minHitFeelIntervalRealtime = 0.07f;
    [SerializeField, Min(0f)] float minAnimationIntervalRealtime = 0.03f;

    void Awake()
    {
        if (!bossHealth)
            bossHealth = GetComponent<BossHealth>();

        if (!bossAnimator && bossHealth)
            bossAnimator = bossHealth.GetComponentInChildren<Animator>();

        if (!bossHealth)
        {
            Debug.LogError("[BossHitReaction] BossHealth를 찾지 못했습니다.", this);
            enabled = false;
            return;
        }

        // 히트 이벤트 구독
        // IHealth.OnDamagedWithType: Action<int, HitType> 형태라고 가정
        CacheAnimatorTriggers();
        bossHealth.OnDamagedWithType += HandleDamagedWithType;

        // 히트 사운드용 AudioSource 준비 (없으면 자동 생성)
        EnsureAudioSource();

        if (hitCameraShake == null && Camera.main != null)
            hitCameraShake = Camera.main.GetComponent<CameraShake>();
    }

    void OnDestroy()
    {
        if (bossHealth != null)
            bossHealth.OnDamagedWithType -= HandleDamagedWithType;
    }

    /// <summary>
    /// BossHealth에서 데미지를 받은 직후 호출되는 콜백.
    /// 여기서 이펙트/애니/사운드를 모두 처리한다.
    /// </summary>
    void HandleDamagedWithType(int damage, HitType hitType)
    {
        // 1) VFX 스폰
        SpawnHitVfx(hitType);

        // 2) 애니메이션 트리거
        PlayHitAnimation(hitType);

        // 3) 사운드 재생
        PlayHitSound(hitType);

        ApplyHitFeel(hitType);
    }

    // ==================== VFX ====================

    void SpawnHitVfx(HitType hitType)
    {
        float now = Time.unscaledTime;
        if (now - _lastVfxRealtime < minVfxIntervalRealtime)
            return;

        GameObject prefab = null;

        // 경직(Heavy) 공격인지에 따라 다른 이펙트 선택
        if (hitType == staggerHitType && heavyHitVfx != null)
            prefab = heavyHitVfx;
        else if (normalHitVfx != null)
            prefab = normalHitVfx;

        if (!prefab) return;

        Vector3 spawnPos = vfxPivot ? vfxPivot.position : transform.position;
        _lastVfxRealtime = now;
        TransientVfxPool.Spawn(prefab, spawnPos, Quaternion.identity);
    }

    // ==================== 애니메이션 ====================

    void PlayHitAnimation(HitType hitType)
    {
        if (!bossAnimator) return;
        float now = Time.unscaledTime;
        if (now - _lastAnimationRealtime < minAnimationIntervalRealtime)
            return;

        // 경직 공격이면 Stagger 우선
        if (hitType == staggerHitType && _hasStaggerTrigger)
        {
            _lastAnimationRealtime = now;
            bossAnimator.ResetTrigger(staggerTriggerName);
            bossAnimator.SetTrigger(staggerTriggerName);
            return;
        }

        // 그 외에는 일반 Hit
        if (_hasHitTrigger)
        {
            _lastAnimationRealtime = now;
            bossAnimator.ResetTrigger(hitTriggerName);
            bossAnimator.SetTrigger(hitTriggerName);
        }
    }

    // ==================== 사운드 ====================

    void CacheAnimatorTriggers()
    {
        _hasHitTrigger = false;
        _hasStaggerTrigger = false;

        if (bossAnimator == null || bossAnimator.runtimeAnimatorController == null)
            return;

        foreach (AnimatorControllerParameter parameter in bossAnimator.parameters)
        {
            if (!_hasHitTrigger &&
                parameter.type == AnimatorControllerParameterType.Trigger &&
                parameter.name == hitTriggerName)
            {
                _hasHitTrigger = true;
            }

            if (!_hasStaggerTrigger &&
                parameter.type == AnimatorControllerParameterType.Trigger &&
                parameter.name == staggerTriggerName)
            {
                _hasStaggerTrigger = true;
            }

            if (_hasHitTrigger && _hasStaggerTrigger)
                break;
        }
    }

    void EnsureAudioSource()
    {
        if (audioSource != null) return;

        // 먼저 자기 오브젝트에서 AudioSource 찾아보고
        audioSource = GetComponent<AudioSource>();
        if (audioSource != null) return;

        // 없으면 새로 추가 (3D 히트 사운드 용도)
        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 1.0f;       // 3D 사운드
        audioSource.rolloffMode = AudioRolloffMode.Linear;
        audioSource.maxDistance = 30f;         // 필요 시 조절
    }

    void ApplyHitFeel(HitType hitType)
    {
        if (!useCombatFeelPolicy || hitCameraShake == null)
            return;
        float now = Time.unscaledTime;
        if (now - _lastHitFeelRealtime < minHitFeelIntervalRealtime)
            return;

        CombatFeelPreset preset = CombatFeelPolicy.GetBossHitPreset(hitType);
        if (preset.CameraShakeAmplitude > 0f && preset.CameraShakeDuration > 0f)
        {
            _lastHitFeelRealtime = now;
            hitCameraShake.Shake(preset.CameraShakeAmplitude, preset.CameraShakeDuration);
        }
    }

    void PlayHitSound(HitType hitType)
    {
        if (audioSource == null) return;
        float now = Time.unscaledTime;
        if (now - _lastAudioRealtime < minAudioIntervalRealtime)
            return;

        AudioClip clip = GetClipForHitType(hitType);
        if (clip == null) return;

        CombatFeelPreset preset = useCombatFeelPolicy
            ? CombatFeelPolicy.GetBossHitPreset(hitType)
            : default;

        // 랜덤 피치 적용
        float pitch = 1f;
        if (useCombatFeelPolicy)
        {
            float minPitch = Mathf.Min(preset.AudioPitchMin, preset.AudioPitchMax);
            float maxPitch = Mathf.Max(preset.AudioPitchMin, preset.AudioPitchMax);
            pitch = Mathf.Approximately(minPitch, maxPitch)
                ? minPitch
                : Random.Range(minPitch, maxPitch);
        }
        else if (randomPitchRange.y > 0f && randomPitchRange.y >= randomPitchRange.x)
        {
            pitch = Random.Range(randomPitchRange.x, randomPitchRange.y);
        }

        audioSource.pitch = pitch;

        // PlayOneShot 사용: 같은 프레임에 여러 히트가 들어와도 겹쳐서 재생 가능
        float volume = useCombatFeelPolicy
            ? Mathf.Clamp01(hitVolume * preset.AudioVolumeMultiplier)
            : hitVolume;
        _lastAudioRealtime = now;
        audioSource.PlayOneShot(clip, AudioOptionsRuntime.ScaleSfx(volume));
    }

    AudioClip GetClipForHitType(HitType hitType)
    {
        // 경직 타입이면 헤비 클립 우선
        if (hitType == staggerHitType)
        {
            AudioClip heavy = GetRandomClip(heavyHitClips);
            if (heavy != null)
                return heavy;
        }

        // 나머지는 일반 히트
        return GetRandomClip(normalHitClips);
    }

    AudioClip GetRandomClip(AudioClip[] clips)
    {
        if (clips == null || clips.Length == 0)
            return null;

        int idx = Random.Range(0, clips.Length);
        return clips[idx];
    }
}
