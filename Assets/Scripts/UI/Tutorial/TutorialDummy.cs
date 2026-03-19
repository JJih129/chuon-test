using UnityEngine;
using DG.Tweening; // 타격감 연출용

// AttackHitbox가 찾을 수 있도록 IDamageReceiver를 반드시 구현해야 합니다.
public class TutorialDummy : MonoBehaviour, IDamageReceiver
{
    [Header("설정")]
    [Tooltip("체력이 깎이는 연출을 위한 흔들림 강도")]
    public float shakeStrength = 0.5f;
    
    [Tooltip("피격 이펙트 (선택)")]
    public GameObject hitEffectPrefab;

    // 공격 쿨타임 (너무 빠르게 다단히트 되는 것 방지용)
    private float lastHitTime;
    private float hitCooldown = 0.1f;

    /// <summary>
    /// AttackHitbox.cs에서 호출하는 함수입니다.
    /// </summary>
    /// <param name="payload">공격 정보(데미지, 위치 등)</param>
    public void ReceiveHit(HitPayload payload)
    {
        // 1. 쿨타임 체크 (짧은 시간에 너무 많이 호출되는 것 방지)
        if (Time.time - lastHitTime < hitCooldown) return;
        lastHitTime = Time.time;
        TryGrantBasicAttackGauge(payload.attacker);

        // 2. 타격감 연출 (DOTween Shake)
        // 기존 트윈이 있으면 멈추고 새로 시작 (부자연스러운 떨림 방지)
        transform.DOKill(true); 
        transform.DOShakeScale(0.2f, shakeStrength, 10, 90, true);

        // 3. 피격 이펙트 생성
        if (hitEffectPrefab != null)
        {
            Instantiate(hitEffectPrefab, payload.hitPoint, Quaternion.LookRotation(payload.hitDirection));
        }

        Debug.Log($"[Dummy] Hit by {payload.attacker?.name}, Damage: {payload.damage}");

        // 4. 튜토리얼 매니저에게 "나 맞았어!" 보고
        if (TutorialManager.Instance != null)
        {
            TutorialManager.Instance.OnEnemyHit();
        }
    }

    void TryGrantBasicAttackGauge(Transform attacker)
    {
        if (attacker == null)
            return;

        var ultimate = attacker.GetComponent<PlayerUltimateController>();
        if (ultimate == null)
            ultimate = attacker.GetComponentInParent<PlayerUltimateController>();

        if (ultimate != null && ultimate.gaugePerH > 0f)
            ultimate.AddGauge(ultimate.gaugePerH);
    }
}
