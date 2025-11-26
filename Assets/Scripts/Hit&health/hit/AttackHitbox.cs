// Assets/Scripts/Combat/AttackHitbox.cs
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 근접 공격, 보스 패턴, 플레이어 무기 등에서 공통으로 쓰는 히트박스.
/// - Trigger Collider 기반
/// - IDamageReceiver 에 HitPayload 전달
/// - 애니메이션 이벤트로 On/Off 하는 방식 추천 (ActivateWindow / DeactivateWindow)
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Collider))]
public class AttackHitbox : MonoBehaviour
{
    // ─────────────────────────────────────
    // ① 기본 데미지 / 히트 타입 설정
    // ─────────────────────────────────────
    [Header("① 기본 데미지 설정")]
    [Tooltip("이 히트박스가 기본으로 주는 데미지 (패턴/애니에서 덮어쓸 수 있음)")]
    [Min(0f)]
    public float baseDamage = 10f;

    [Tooltip("이 히트박스의 기본 히트 타입 (프로젝트 공용 HitType 사용)")]
    public HitType hitType = HitType.Normal;

    [Tooltip("해당 공격이 퍼펙트 회피 판정 대상으로 들어가는지 여부")]
    public bool canPerfectDodge = true;

    // ─────────────────────────────────────
    // ② 공격자 / 레이어 필터
    // ─────────────────────────────────────
    [Header("② 공격자 설정")]
    [Tooltip("공격자(플레이어/보스) 루트. 비워두면 transform.root 사용")]
    public Transform attackerRoot;

    [Header("③ 히트 필터링")]
    [Tooltip("맞출 레이어 마스크")]
    public LayerMask hitLayers = ~0;

    [Tooltip("상대 콜라이더가 Trigger 이면 무시할지 여부")]
    public bool ignoreTriggerColliders = true;

    // ─────────────────────────────────────
    // ④ 중복 히트 방지(원샷 창)
    // ─────────────────────────────────────
    [Header("④ 중복 히트 방지 (One-Shot Window)")]
    [Tooltip("창이 열려 있는 동안, 같은 리시버를 한 번만 때리도록 관리할지 여부")]
    public bool useOneShotWindow = true;

    [Tooltip("OneShot 창 지속 시간(초). 0이면 한 번 활성화된 동안(콜라이더 On ~ Off)만 유지")]
    [Min(0f)] public float oneShotWindow = 0.2f;

    // ─────────────────────────────────────
    // ⑤ 디버그
    // ─────────────────────────────────────
    [Header("⑤ 디버그")]
    public bool enableLogs = false;

    // ─────────────────────────────────────
    // 내부 필드
    // ─────────────────────────────────────
    Collider _col;
    readonly HashSet<IDamageReceiver> _alreadyHit = new HashSet<IDamageReceiver>();
    Coroutine _oneShotRoutine;

    /// <summary>
    /// 현재 이 히트박스가 쓰는 Collider (외부에서 접근 필요할 때용)
    /// </summary>
    public Collider Collider => _col;

    void Reset()
    {
        // 에디터에서 컴포넌트를 붙였을 때 기본 세팅
        _col = GetComponent<Collider>();
        if (_col != null)
        {
            _col.isTrigger = true;
            _col.enabled = false;    // 기본은 꺼둔 상태에서, 애니 이벤트로 켜는 걸 권장
        }

        hitLayers = ~0;
        ignoreTriggerColliders = true;
    }

    void Awake()
    {
        _col = GetComponent<Collider>();
        if (_col == null)
        {
            Debug.LogError("[AttackHitbox] Collider가 없습니다. 컴포넌트를 비활성화합니다.", this);
            enabled = false;
            return;
        }

        if (!_col.isTrigger)
        {
            Debug.LogWarning("[AttackHitbox] Collider.isTrigger 를 자동으로 켭니다.", this);
            _col.isTrigger = true;
        }

        // 공격자 루트 기본값은 transform.root
        if (attackerRoot == null)
            attackerRoot = transform.root;
    }

    void OnEnable()
    {
        // 컴포넌트 자체를 끄고 켤 수도 있으니 방어적으로 초기화
        _alreadyHit.Clear();
    }

    void OnDisable()
    {
        _alreadyHit.Clear();
        if (_oneShotRoutine != null)
        {
            StopCoroutine(_oneShotRoutine);
            _oneShotRoutine = null;
        }
    }

    // ─────────────────────────────────────
    // ⑥ 애니메이션 이벤트용 On/Off 메서드
    //     → 공격 클립에서 직접 호출해서 창을 여닫는 방식
    // ─────────────────────────────────────

    /// <summary>
    /// 히트 창 활성화 (애니메이션 이벤트에서 호출 권장)
    /// </summary>
    public void ActivateWindow()
    {
        if (_col == null) return;

        _col.enabled = true;
        _alreadyHit.Clear();

        if (useOneShotWindow && oneShotWindow > 0f)
        {
            if (_oneShotRoutine != null)
                StopCoroutine(_oneShotRoutine);
            _oneShotRoutine = StartCoroutine(Co_ClearOneShotAfter(oneShotWindow));
        }

        if (enableLogs)
            Debug.Log("[AttackHitbox] Window ON", this);
    }

    /// <summary>
    /// 히트 창 비활성화 (애니메이션 이벤트에서 호출 권장)
    /// </summary>
    public void DeactivateWindow()
    {
        if (_col == null) return;

        _col.enabled = false;
        _alreadyHit.Clear();

        if (_oneShotRoutine != null)
        {
            StopCoroutine(_oneShotRoutine);
            _oneShotRoutine = null;
        }

        if (enableLogs)
            Debug.Log("[AttackHitbox] Window OFF", this);
    }

    IEnumerator Co_ClearOneShotAfter(float t)
    {
        // 게임 시간 기준(슬로우 모션과 함께 느려짐). 필요하면 WaitForSecondsRealtime 로 변경.
        yield return new WaitForSeconds(t);
        _alreadyHit.Clear();
        _oneShotRoutine = null;
    }

    // ─────────────────────────────────────
    // ⑦ 외부에서 데미지/타입 덮어쓰기용 API
    //     (보스 패턴 / 무기 강화 등에서 사용)
    // ─────────────────────────────────────

    /// <summary>
    /// 코드에서 이 히트박스의 데미지/타입/퍼펙트 회피 여부를 덮어쓸 때 사용.
    /// 예: 보스 패턴 진입 시 BossController에서 세팅.
    /// </summary>
    public void Configure(float damage, HitType type, bool allowPerfectDodge, Transform attackerOverride = null)
    {
        baseDamage = damage;
        hitType = type;
        canPerfectDodge = allowPerfectDodge;

        if (attackerOverride != null)
            attackerRoot = attackerOverride;
    }

    // ─────────────────────────────────────
    // ⑧ 실제 충돌 처리
    // ─────────────────────────────────────

    void OnTriggerEnter(Collider other)
    {
        // 1) 트리거 필터
        if (ignoreTriggerColliders && other.isTrigger)
            return;

        // 2) 레이어 필터
        if (((1 << other.gameObject.layer) & hitLayers) == 0)
            return;

        // 3) 상대에서 IDamageReceiver 찾기 (부모까지 탐색)
        var receiver = other.GetComponentInParent<IDamageReceiver>();
        if (receiver == null)
            return;

        // 4) OneShot 창에서 이미 맞은 대상이면 스킵
        if (useOneShotWindow && _alreadyHit.Contains(receiver))
            return;

        if (useOneShotWindow)
            _alreadyHit.Add(receiver);

        // 5) 히트 정보 계산
        Vector3 attackerPos = attackerRoot ? attackerRoot.position : transform.position;
        Vector3 hitPoint = other.ClosestPoint(attackerPos);

        Vector3 dir = hitPoint - attackerPos;
        if (dir.sqrMagnitude > 0.0001f)
            dir.Normalize();
        else
            dir = attackerRoot ? attackerRoot.forward : transform.forward;

        // 6) 페이로드 구성
        HitPayload payload = new HitPayload
        {
            damage          = baseDamage,
            hitType         = hitType,
            hitPoint        = hitPoint,
            hitDirection    = dir,
            attacker        = attackerRoot,
            canPerfectDodge = canPerfectDodge
        };

        if (enableLogs)
        {
            Debug.Log($"[AttackHitbox] Hit {receiver} | dmg={payload.damage} type={payload.hitType}", this);
        }

        // 7) 실제 데미지 전달
        receiver.ReceiveHit(payload);
    }
}
