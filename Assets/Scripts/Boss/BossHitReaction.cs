// Assets/Scripts/Boss/BossHitReaction.cs
using UnityEngine;

/// <summary>
/// 보스가 데미지를 받을 때 발생하는 비주얼/애니메이션 반응 전담 컴포넌트.
/// - BossHealth.OnDamagedWithType 를 구독해서,
///   * 히트 이펙트 스폰
///   * Hit / Stagger 애니메이션 트리거 호출
/// 를 담당한다.
/// </summary>
[DisallowMultipleComponent]
public class BossHitReaction : MonoBehaviour
{
    [Header("참조")]
    [Tooltip("같은 오브젝트 또는 부모에 있는 BossHealth를 지정. 비워두면 자동 검색.")]
    public BossHealth bossHealth;
    
    [Tooltip("보스 애니메이터. Hit / Stagger 트리거를 보낼 대상.")]
    public Animator bossAnimator;

    [Header("히트 이펙트")]
    [Tooltip("일반 공격 피격 시 사용할 VFX 프리팹 (선택).")]
    public GameObject normalHitVfx;

    [Tooltip("강공격/경직 공격 피격 시 사용할 VFX 프리팹 (선택).")]
    public GameObject heavyHitVfx;

    [Tooltip("이펙트 생성 위치 기준 트랜스폼 (없으면 피격 지점 사용).")]
    public Transform vfxPivot;

    [Header("애니메이션 파라미터 이름")]
    [Tooltip("일반 피격 시 사용할 트리거 이름 (예: \"Hit\"). 비워두면 애니는 호출하지 않음.")]
    public string hitTriggerName = "Hit";

    [Tooltip("경직/강공격 피격 시 사용할 트리거 이름 (예: \"Stagger\").")]
    public string staggerTriggerName = "Stagger";

    [Header("경직 조건")]
    [Tooltip("이 HitType에 해당할 경우 경직 애니메이션을 사용한다.")]
    public HitType staggerHitType = HitType.Heavy;  // 프로젝트의 HitType 정의에 맞게 값만 맞춰서 사용

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

        // BossHealth에서 제공하는 이벤트 구독 (이미 정의된 시그니처에 맞춰 사용)
        // IHealth.OnDamagedWithType: Action<int, HitType> 형태라고 가정
        bossHealth.OnDamagedWithType += HandleDamagedWithType;
    }

    void OnDestroy()
    {
        if (bossHealth != null)
            bossHealth.OnDamagedWithType -= HandleDamagedWithType;
    }

    /// <summary>
    /// BossHealth에서 데미지를 받은 직후 호출되는 콜백.
    /// 여기서 이펙트/애니를 모두 처리한다.
    /// </summary>
    void HandleDamagedWithType(int damage, HitType hitType)
    {
        // 1) VFX 스폰
        SpawnHitVfx(hitType);

        // 2) 애니메이션 트리거
        PlayHitAnimation(hitType);
    }

    void SpawnHitVfx(HitType hitType)
    {
        GameObject prefab = null;

        // 경직(Heavy) 공격인지에 따라 다른 이펙트 선택
        if (hitType == staggerHitType && heavyHitVfx != null)
            prefab = heavyHitVfx;
        else if (normalHitVfx != null)
            prefab = normalHitVfx;

        if (!prefab) return;

        Vector3 spawnPos = vfxPivot ? vfxPivot.position : transform.position;
        Instantiate(prefab, spawnPos, Quaternion.identity);
    }

    void PlayHitAnimation(HitType hitType)
    {
        if (!bossAnimator) return;

        // 경직 공격이면 Stagger 우선
        if (hitType == staggerHitType && !string.IsNullOrEmpty(staggerTriggerName))
        {
            bossAnimator.ResetTrigger(staggerTriggerName);
            bossAnimator.SetTrigger(staggerTriggerName);
            return;
        }

        // 그 외에는 일반 Hit
        if (!string.IsNullOrEmpty(hitTriggerName))
        {
            bossAnimator.ResetTrigger(hitTriggerName);
            bossAnimator.SetTrigger(hitTriggerName);
        }
    }
}
