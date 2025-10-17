using System;
using System.Reflection;
using UnityEngine;

public class PlayerDamageReceiver : MonoBehaviour
{
    [Header("Optional references (will try GetComponent if null)")]
    [SerializeField] private MonoBehaviour healthBehaviour; // IHealth 구현체
    [SerializeField] private PlayerGuardController guardController; // 가드 컨트롤러 (Inspector에 할당 권장)

    // 런타임 캐시
    private IHealth health;

    void Awake()
    {
        if (healthBehaviour != null) health = healthBehaviour as IHealth;
        if (health == null) health = GetComponent<IHealth>();

        if (guardController == null) guardController = GetComponent<PlayerGuardController>();

        Debug.Log("[PDR] Cached health methods: " + (health != null));
    }

    // 외부에서 호출되는 피격 수신 함수
    // HitData의 필드명이 프로젝트마다 다를 수 있으니 필요하면 이름을 맞춰서 사용하세요.
    public void ReceiveHit(HitData hit)
    {
        if (hit == null)
        {
            Debug.LogWarning("[PDR] ReceiveHit: hit == null");
            return;
        }

        // HitData 속성명은 프로젝트 기준으로 맞춰주세요.
        float baseDamage = hit.damage;
        Vector3 attackDir = hit.hitDirection;
        bool isParryable = hit.isParryable;
        GameObject attacker = hit.attacker;

        Debug.Log($"[PDR] ReceiveHit called. baseDamage={baseDamage} attacker={(attacker? attacker.name : "null")}");

        // 1) 가드가 있으면 가드에 판정 위임 (최종 데미지 반환 기대)
        float finalDamage = baseDamage;
        if (guardController != null)
        {
            try
            {
                // ResolveIncomingAttack(Vector3 attackDir, bool isParryable, float baseDamage, GameObject attacker)
                finalDamage = guardController.ResolveIncomingAttack(attackDir, isParryable, baseDamage, attacker);
                Debug.Log($"[PDR] guard.ResolveIncomingAttack returned {finalDamage}");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[PDR] guard.ResolveIncomingAttack 호출 실패: " + ex.Message);
                finalDamage = baseDamage;
            }
        }

        // 2) IHealth에 데미지 적용 시도
        if (health != null)
        {
            // 우선 float 버전 시도, 없으면 int 버전 시도
            try
            {
                var miFloat = health.GetType().GetMethod("ApplyDamage", new Type[] { typeof(float) });
                if (miFloat != null)
                {
                    miFloat.Invoke(health, new object[] { finalDamage });
                    Debug.Log($"[PDR] Called IHealth.ApplyDamage(float) with {finalDamage}");
                    return;
                }

                var miInt = health.GetType().GetMethod("ApplyDamage", new Type[] { typeof(int) });
                if (miInt != null)
                {
                    miInt.Invoke(health, new object[] { Mathf.RoundToInt(finalDamage) });
                    Debug.Log($"[PDR] Called IHealth.ApplyDamage(int) with {Mathf.RoundToInt(finalDamage)}");
                    return;
                }

                // 혹시 TakeDamage 같은 이름이면 시도
                var miTake = health.GetType().GetMethod("TakeDamage", new Type[] { typeof(float) });
                if (miTake != null)
                {
                    miTake.Invoke(health, new object[] { finalDamage });
                    Debug.Log($"[PDR] Called IHealth.TakeDamage(float) with {finalDamage}");
                    return;
                }

                Debug.LogWarning("[PDR] IHealth에 ApplyDamage/TakeDamage 메서드가 없음");
            }
            catch (Exception ex)
            {
                Debug.LogError("[PDR] IHealth 적용 중 예외: " + ex);
            }
        }
        else
        {
            Debug.LogWarning("[PDR] No IHealth to apply damage to");
        }
    }
}
