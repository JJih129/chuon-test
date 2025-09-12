using UnityEngine;
using System.Reflection;

// ============================== ▼ 변수 헤더(튜닝 가이드) ▼ ==============================
// [참조]
// guard : 가드/패링 로직을 가진 컨트롤러 (PlayerGuardController)
// healthBehaviour : 네가 이미 쓰는 체력 컴포넌트(예: PlayerHealth, EnemyHealth 등)
//   - IHealth가 있으면 캐스팅해서 쓰고,
//   - IHealth가 없거나 시그니처가 다르면 리플렉션으로 ApplyDamage/Tak eDamage(int/float) 탐색.
//
// [피격 처리 옵션]
// zeroDamageOnParry : 패링 성공 시 데미지를 0으로 만들지 여부
// blockSparksOffset : 가드 스파크 VFX/히트 이펙트가 뜨는 위치 보정값
// ========================================================================================

[DisallowMultipleComponent]
public class PlayerDamageReceiver : MonoBehaviour
{
    [Header("▶ 참조")]
    [Tooltip("가드/패링 제어 스크립트")]
    public PlayerGuardController guard;

    [Tooltip("체력 컴포넌트(예: PlayerHealth/EnemyHealth). IHealth가 있다면 캐스팅됨")]
    public MonoBehaviour healthBehaviour; // 사용자가 쓰는 Health 스크립트 Drag&Drop

    [Header("▶ 피격 처리 옵션")]
    [Tooltip("패링 성공 시 데미지를 0으로 만들지 여부")]
    public bool zeroDamageOnParry = true;

    [Tooltip("블록/스파크 이펙트용 위치 보정값")]
    public Vector3 blockSparksOffset = new Vector3(0, 1.0f, 0);

    // --- 내부 캐시 ---
    object healthObj;                 // 실제 Health 인스턴스
    System.Type healthType;           // 타입 캐시
    // IHealth 캐스팅 성공 시 사용
    System.Type iHealthType;          // 인터페이스 타입 캐시
    PropertyInfo propCurrentHP;       // IHealth.CurrentHP
    PropertyInfo propMaxHP;           // IHealth.MaxHP
    MethodInfo methodApplyDamage;     // IHealth.ApplyDamage(float)

    // 리플렉션 폴백(메서드명/파라미터 타입 다양한 경우 커버)
    MethodInfo reflectApplyDamageFloat;   // ApplyDamage(float)/TakeDamage(float)
    MethodInfo reflectApplyDamageInt;     // ApplyDamage(int)/TakeDamage(int)

    void Awake()
    {
        if (guard == null) guard = GetComponent<PlayerGuardController>();

        healthObj = healthBehaviour != null ? (object)healthBehaviour : null;
        if (healthObj == null)
        {
            Debug.LogWarning("[PlayerDamageReceiver] healthBehaviour 미할당. 체력 반영 불가.");
            return;
        }

        healthType = healthObj.GetType();

        // 1) 기존 IHealth가 프로젝트에 있으면 ‘그 IHealth’로 시도 (네임스페이스 불문)
        //    - 인터페이스명만으로 탐색
        foreach (var itf in healthType.GetInterfaces())
        {
            if (itf.Name == "IHealth")
            {
                iHealthType = itf;
                break;
            }
        }

        if (iHealthType != null)
        {
            // IHealth 프로퍼티/메서드 시그니처 탐색 (타입이 float일 수도, int일 수도 있어 후속 처리에서 캐스팅)
            propCurrentHP = iHealthType.GetProperty("CurrentHP");
            propMaxHP = iHealthType.GetProperty("MaxHP");
            methodApplyDamage = iHealthType.GetMethod("ApplyDamage");
        }

        // 2) 폴백: 메서드명 다양한 케이스 탐색 (ApplyDamage/TakeDamage, float/int)
        // float 우선
        reflectApplyDamageFloat =
            healthType.GetMethod("ApplyDamage", new[] { typeof(float) }) ??
            healthType.GetMethod("TakeDamage", new[] { typeof(float) });
        reflectApplyDamageInt =
            healthType.GetMethod("ApplyDamage", new[] { typeof(int) }) ??
            healthType.GetMethod("TakeDamage", new[] { typeof(int) });
    }

    /// <summary>
    /// 적 히트박스의 단일 진입점
    /// attacker : 공격자 Transform (방향 계산용)
    /// baseDamage : 원 데미지
    /// attackIsParryable : 이 공격이 패링 가능한 패턴인지 여부
    /// hitPointWorld : 맞은 월드 위치(옵션)
    /// </summary>
    public void ReceiveHit(Transform attacker, float baseDamage, bool attackIsParryable, Vector3? hitPointWorld = null)
    {
        if (healthObj == null)
        {
            Debug.LogWarning("[PlayerDamageReceiver] Health 참조 없음.");
            return;
        }

        // 공격자→플레이어 방향
        Vector3 attackerToPlayer = (transform.position - attacker.position).normalized;

        // Guard 로직으로 최종 데미지 산출
        float finalDamage = baseDamage;
        if (guard != null)
            finalDamage = guard.ResolveIncomingAttack(attackerToPlayer, attackIsParryable, baseDamage);

        if (zeroDamageOnParry && finalDamage < Mathf.Epsilon)
            finalDamage = 0f;

        // 체력 반영
        ApplyDamageFlexible(finalDamage);

        // 디버그/이펙트
        if (hitPointWorld.HasValue)
            Debug.DrawRay(hitPointWorld.Value, Vector3.up * 0.2f, Color.yellow, 0.25f);
    }

    // 다양한 IHealth/Health 시그니처 대응
    void ApplyDamageFlexible(float dmg)
    {
        if (dmg <= 0f) return;

        // 1) IHealth가 있고 ApplyDamage 1개 파라미터라면 타입 검사 후 호출
        if (iHealthType != null && methodApplyDamage != null)
        {
            var p = methodApplyDamage.GetParameters();
            if (p.Length == 1)
            {
                if (p[0].ParameterType == typeof(float))
                {
                    methodApplyDamage.Invoke(healthObj, new object[] { dmg });
                    return;
                }
                if (p[0].ParameterType == typeof(int))
                {
                    methodApplyDamage.Invoke(healthObj, new object[] { Mathf.RoundToInt(dmg) });
                    return;
                }
            }
        }

        // 2) 폴백: 클래스 직접 탐색
        if (reflectApplyDamageFloat != null)
        {
            reflectApplyDamageFloat.Invoke(healthObj, new object[] { dmg });
            return;
        }
        if (reflectApplyDamageInt != null)
        {
            reflectApplyDamageInt.Invoke(healthObj, new object[] { Mathf.RoundToInt(dmg) });
            return;
        }

        Debug.LogWarning($"[PlayerDamageReceiver] Health에 호출 가능한 Apply/TakeDamage가 없습니다. 타입={healthType.Name}");
    }
}
