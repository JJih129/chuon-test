// 파일명: UltimateTargetSimple.cs
// 역할: IUltimateTarget의 기본 구현(보스에 바로 부착 가능)
// 특징: '고정 데미지' 입력을 받아 방어/브레이크 보정 → 체력 감소 → VFX/SFX/히트리액션까지 처리
//       외부에 기존 Health/Damage 시스템이 있으면, 아래 UnityEvent로 브릿지해 연동 가능.

using UnityEngine;
using UnityEngine.Events;

[DisallowMultipleComponent]
public class UltimateTargetSimple : MonoBehaviour, IUltimateTarget
{
    // ================================ 변수 헤더(한글 설명) ================================
    [Header("① 체력 설정 | 최대/현재 체력 및 사망 처리")]
    [Tooltip("최대 체력")]
    public int maxHP = 10000;
    [Tooltip("현재 체력(런타임 중 갱신)")]
    public int currentHP = 10000;
    [Tooltip("체력이 0 이하가 되면 파괴할지 여부")]
    public bool destroyOnDeath = false;

    [Header("② 방어/보정 | 고정데미지에 대한 방어 값/브레이크 보정")]
    [Tooltip("방어력(고정 데미지에서 감산). ignoreDefense가 false일 때만 적용")]
    public int defense = 200;
    [Tooltip("방어 무시(고정 데미지 그대로 적용)")]
    public bool ignoreDefense = true;
    [Tooltip("브레이크(무력화) 상태 여부 - 브레이크 중이면 배율 보너스 적용")]
    public bool isBreak = false;
    [Tooltip("브레이크 상태일 때 데미지 배율(예: 1.5 = 50% 추가피해)")]
    [Min(1f)] public float breakBonusMultiplier = 1.5f;

    [Header("③ 히트 리액션 | 넉백/경직/히트스톱 등(간단 옵션)")]
    [Tooltip("히트 시 약한 경직/리액션을 줄 리지드바디(없으면 무시)")]
    public Rigidbody rigidbodyForReaction;
    [Tooltip("히트 시 밀쳐낼 힘(전방 기준). 0이면 적용 안 함")]
    public float knockbackForce = 3f;
    [Tooltip("히트 시 약한 HitStop(초). 0이면 적용 안 함")]
    public float hitStop = 0.03f;

    [Header("④ 연출 | 피격 VFX/SFX 및 스폰 위치")]
    [Tooltip("히트 시 스폰할 VFX 프리팹(없으면 미사용)")]
    public GameObject hitVFXPrefab;
    [Tooltip("VFX 스폰 기준 트랜스폼(없으면 this.transform 사용)")]
    public Transform vfxSpawnPoint;
    [Tooltip("히트 SFX(없으면 미사용)")]
    public AudioClip hitSFX;
    [Tooltip("오디오 소스(없으면 자동 생성)")]
    public AudioSource audioSource;

    [Header("⑤ 이벤트 | 외부 시스템(기존 Health 등)과의 브릿지")]
    [Tooltip("궁극기 피격 시(적용 데미지 int) 호출 - 기존 Health로 라우팅할 때 사용")]
    public UnityEvent<int> OnUltimateDamageApplied;
    [Tooltip("사망 시 호출 - 보스 페이즈/연출 트리거 연결")]
    public UnityEvent OnDied;

    // ================================ 내부 상태/유틸 ================================
    bool _isDead;
    float _hitStopRemain;

    void Reset()
    {
        // 컴포넌트 추가 시 기본값 초기화
        currentHP = maxHP;
        if (!audioSource) audioSource = GetComponent<AudioSource>();
    }

    void Awake()
    {
        if (!audioSource)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 1f; // 3D
        }
        if (!vfxSpawnPoint) vfxSpawnPoint = transform;
        currentHP = Mathf.Clamp(currentHP, 0, maxHP);
    }

    void Update()
    {
        // 간단한 히트스톱 처리(프로덕션에선 글로벌 매니저/타임스케일 대체 권장)
        if (_hitStopRemain > 0f)
        {
            _hitStopRemain -= Time.unscaledDeltaTime;
            // 필요 시 Animator.speed, AI 업데이트, 파티클 등을 개별 일시정지하는 방식으로 확장 가능
        }
    }

    // ================================ 핵심 로직 ================================
    public void ApplyUltimateDamage(int fixedDamage)
    {
        if (_isDead) return;

        // 1) 최종 데미지 계산
        int dmg = fixedDamage;
        if (!ignoreDefense)
            dmg = Mathf.Max(1, fixedDamage - Mathf.Max(0, defense));
        if (isBreak && breakBonusMultiplier > 1f)
            dmg = Mathf.RoundToInt(dmg * breakBonusMultiplier);

        // 2) 외부 시스템 브릿지(있다면 먼저 알림)
        //    - 기존 Health 컴포넌트가 이 이벤트를 받아 내부 규칙대로 처리하도록 라우팅 가능
        OnUltimateDamageApplied?.Invoke(dmg);

        // 3) 내부 HP 처리(외부가 없는 경우 대비)
        ApplyLocalDamage(dmg);

        // 4) 연출: VFX/SFX/리액션
        PlayHitEffects();
        ApplySimpleReaction();

        // 5) 히트스톱(간단)
        if (hitStop > 0f) _hitStopRemain = hitStop;
    }

    // ================================ 내부 처리 함수 ================================
    void ApplyLocalDamage(int dmg)
    {
        if (dmg <= 0) return;
        currentHP -= dmg;
        if (currentHP <= 0 && !_isDead)
        {
            _isDead = true;
            currentHP = 0;
            OnDied?.Invoke();
            if (destroyOnDeath) Destroy(gameObject);
        }
    }

    void PlayHitEffects()
    {
        if (hitVFXPrefab)
        {
            var pos = vfxSpawnPoint ? vfxSpawnPoint.position : transform.position;
            Instantiate(hitVFXPrefab, pos, Quaternion.identity);
        }
        if (hitSFX && audioSource)
        {
            audioSource.PlayOneShot(hitSFX, AudioOptionsRuntime.ScaleSfx(1f));
        }
    }

    void ApplySimpleReaction()
    {
        if (rigidbodyForReaction && knockbackForce > 0f)
        {
            // 플레이어 → 타겟 방향으로 간단 밀쳐내기(수평 기준)
            // 실제 게임에선 히트 방향 벡터를 플레이어 위치/공격 방향에서 받아오는 게 정확함
            Vector3 dir = -transform.forward;
            dir.y = 0f;
            dir.Normalize();
            rigidbodyForReaction.AddForce(dir * knockbackForce, ForceMode.VelocityChange);
        }
    }

    // ================================ 퍼블릭 유틸(디버그/연동) ================================
    [ContextMenu("디버그: 브레이크 On")]
    public void Debug_BreakOn() => isBreak = true;

    [ContextMenu("디버그: 브레이크 Off")]
    public void Debug_BreakOff() => isBreak = false;

    [ContextMenu("디버그: HP 전부 회복")]
    public void Debug_FullHeal() => currentHP = maxHP;

    // 외부 시스템(브레이크 게이지, 페이즈 전환 등)에서 호출
    public void SetBreak(bool on) => isBreak = on;
}
