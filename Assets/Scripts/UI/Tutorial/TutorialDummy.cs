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
    private int lastAttackSequenceId;
    private int lastAttackerInstanceId;

    /// <summary>
    /// AttackHitbox.cs에서 호출하는 함수입니다.
    /// </summary>
    /// <param name="payload">공격 정보(데미지, 위치 등)</param>
    public void ReceiveHit(HitPayload payload)
    {
        // 1. 같은 공격 판정은 1회만 카운트한다.
        int attackerInstanceId = payload.attacker != null ? payload.attacker.root.GetInstanceID() : 0;
        bool hasAttackSequence = payload.attackSequenceId > 0;
        if (hasAttackSequence &&
            payload.attackSequenceId == lastAttackSequenceId &&
            attackerInstanceId == lastAttackerInstanceId)
        {
            return;
        }

        // 시퀀스 식별자가 없는 오래된 경로는 시간 쿨다운으로만 막는다.
        if (!hasAttackSequence && Time.time - lastHitTime < hitCooldown) return;

        lastHitTime = Time.time;
        if (hasAttackSequence)
        {
            lastAttackSequenceId = payload.attackSequenceId;
            lastAttackerInstanceId = attackerInstanceId;
        }

        CombatRewardUtility.TryGrantBasicAttackGauge(payload.attacker);

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

}
