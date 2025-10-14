// PlayerDamageReceiver.cs
// 설명: 데미지 처리 + 체력 호출 + 애니 재생(AnimationLockManager 사용 우선).
using System;
using System.Reflection;
using UnityEngine;

[DisallowMultipleComponent]
public class PlayerDamageReceiver : MonoBehaviour
{
    [Header("연결 (Inspector에 지정 가능)")]
    [Tooltip("가드/패링 컨트롤러 (PlayerGuardController 등). 비워두면 자동 탐색")]
    public MonoBehaviour guard;
    [Tooltip("체력 처리 컴포넌트 (IHealth 또는 ApplyDamage 등). 비워두면 자동 탐색")]
    public MonoBehaviour healthBehaviour;

    [Header("애니메이션 (Hit 레이어에 Hit_Light / Hit_Heavy 상태)")]
    [Tooltip("Hit 레이어 이름")]
    public string hitLayerName = "Hit";
    [Tooltip("경미한 히트 상태명")]
    public string lightStateName = "Hit_Light";
    [Tooltip("강한 히트 상태명")]
    public string heavyStateName = "Hit_Heavy";
    [Tooltip("Heavy 판정 임계값")]
    public float heavyThreshold = 10f;
    [Tooltip("애니메이터(비워두면 자동탐색)")]
    public Animator animator;

    [Header("애니 잠금 매니저")]
    [Tooltip("AnimationLockManager를 직접 연결 (비워두면 자동탐색)")]
    public AnimationLockManager animationLockManager;

    [Header("동작")]
    [Tooltip("기본적으로 들어오는 히트가 패리 가능한가")]
    public bool defaultHitIsParryable = false;
    [Tooltip("패리로 인해 데미지 0이면 Hit 애니 재생 생략")]
    public bool skipHitAnimOnParryZeroDamage = true;

    // 리플렉션 캐시
    MethodInfo _miApplyFloat;
    MethodInfo _miApplyInt;
    MethodInfo _miTakeDamage;

    void Awake()
    {
        // 자동 탐색
        if (guard == null) guard = FindComponentByName("PlayerGuardController") ?? GetComponent<MonoBehaviour>();
        if (healthBehaviour == null) AutoFindHealthBehaviour();
        if (animator == null) animator = GetComponentInChildren<Animator>(true) ?? GetComponentInParent<Animator>(true) ?? GetComponent<Animator>();
        if (animationLockManager == null) animationLockManager = GetComponentInChildren<AnimationLockManager>(true) ?? GetComponentInParent<AnimationLockManager>(true) ?? GetComponent<AnimationLockManager>();

        CacheHealthMethods();

        if (healthBehaviour == null) Debug.LogWarning("[PDR] healthBehaviour 미할당. 데미지 적용 불가.", this);
        if (animator == null) Debug.LogWarning("[PDR] animator 미할당. Hit 애니 재생 불가.", this);
        if (animationLockManager == null) Debug.Log("[PDR] AnimationLockManager 미할당. PlayStateAndLock 사용 불가. 폴백으로 직접 재생합니다.", this);
    }

    void AutoFindHealthBehaviour()
    {
        MonoBehaviour found = null;
        foreach (var mb in GetComponentsInChildren<MonoBehaviour>(true)) if (mb is IHealth) { found = mb; break; }
        if (found == null) foreach (var mb in GetComponentsInParent<MonoBehaviour>(true)) if (mb is IHealth) { found = mb; break; }
        if (found == null)
        {
            foreach (var mb in GetComponentsInChildren<MonoBehaviour>(true)) if (HasHealthLikeAPI(mb.GetType())) { found = mb; break; }
            if (found == null) foreach (var mb in GetComponentsInParent<MonoBehaviour>(true)) if (HasHealthLikeAPI(mb.GetType())) { found = mb; break; }
        }
        if (found != null) { healthBehaviour = found; Debug.Log("[PDR] healthBehaviour 자동할당: " + found.GetType().Name, this); }
    }

    MonoBehaviour FindComponentByName(string typeName)
    {
        if (string.IsNullOrEmpty(typeName)) return null;
        foreach (var mb in GetComponentsInChildren<MonoBehaviour>(true)) { if (mb != null && string.Equals(mb.GetType().Name, typeName, StringComparison.OrdinalIgnoreCase)) return mb; }
        foreach (var mb in GetComponentsInParent<MonoBehaviour>(true)) { if (mb != null && string.Equals(mb.GetType().Name, typeName, StringComparison.OrdinalIgnoreCase)) return mb; }
        return null;
    }

    bool HasHealthLikeAPI(Type t)
    {
        if (t == null) return false;
        if (typeof(IHealth).IsAssignableFrom(t)) return true;
        if (t.GetMethod("ApplyDamage", new[] { typeof(float) }) != null) return true;
        if (t.GetMethod("ApplyDamage", new[] { typeof(int) }) != null) return true;
        if (t.GetMethod("TakeDamage", new[] { typeof(int), typeof(HitType), typeof(Vector3) }) != null) return true;
        return false;
    }

    void CacheHealthMethods()
    {
        if (healthBehaviour == null) return;
        var t = healthBehaviour.GetType();
        _miApplyFloat = t.GetMethod("ApplyDamage", new[] { typeof(float) });
        _miApplyInt = t.GetMethod("ApplyDamage", new[] { typeof(int) });
        _miTakeDamage = t.GetMethod("TakeDamage", new[] { typeof(int), typeof(HitType), typeof(Vector3) });
        Debug.Log($"[PDR] Cached health methods: ApplyFloat={_miApplyFloat!=null} ApplyInt={_miApplyInt!=null} TakeDamage={_miTakeDamage!=null}", this);
    }

    // 외부에서 히트 전달 (HitData 구조에 맞춰 사용)
    public void ReceiveHit(HitData hit)
    {
        if (hit == null) { Debug.LogWarning("[PDR] ReceiveHit null"); return; }

        GameObject attacker = hit.attacker;
        Debug.Log($"[PDR] ReceiveHit called. baseDamage={hit.damage} attacker={(attacker? attacker.name : "null")}", this);

        Transform attackerTransform = attacker != null ? attacker.transform : null;
        bool attackIsParryable = hit.isParryable || defaultHitIsParryable;
        Vector3 dir = (attackerTransform != null) ? (transform.position - attackerTransform.position).normalized : Vector3.forward;
        float finalDamage = hit.damage;

        // guard 처리 (ResolveIncomingAttack 가능)
        if (guard != null)
        {
            var mi = guard.GetType().GetMethod("ResolveIncomingAttack", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (mi != null)
            {
                try
                {
                    var result = mi.Invoke(guard, new object[] { dir, attackIsParryable, (float)hit.damage });
                    if (result is float f) finalDamage = f;
                    else if (result is double d) finalDamage = (float)d;
                    else if (result is int i) finalDamage = i;
                    Debug.Log($"[PDR] guard.ResolveIncomingAttack returned {finalDamage}", this);
                }
                catch (Exception ex) { Debug.LogWarning("[PDR] guard.ResolveIncomingAttack invocation failed: " + ex); }
            }
        }

        if (skipHitAnimOnParryZeroDamage && finalDamage <= Mathf.Epsilon)
        {
            Debug.Log("[PDR] finalDamage <= 0 : parried/blocked. no damage applied.", this);
            return;
        }

        // 체력 적용
        bool applied = false;
        if (healthBehaviour != null)
        {
            if (healthBehaviour is IHealth ihealth)
            {
                if (Mathf.Approximately(finalDamage, Mathf.Round(finalDamage))) ihealth.ApplyDamage((int)Mathf.Round(finalDamage));
                else ihealth.ApplyDamage(finalDamage);
                applied = true;
            }
            else if (_miApplyFloat != null) { _miApplyFloat.Invoke(healthBehaviour, new object[] { finalDamage }); applied = true; }
            else if (_miApplyInt != null) { _miApplyInt.Invoke(healthBehaviour, new object[] { Mathf.RoundToInt(finalDamage) }); applied = true; }
            else if (_miTakeDamage != null) { _miTakeDamage.Invoke(healthBehaviour, new object[] { Mathf.RoundToInt(finalDamage), hit.hitType, hit.hitPoint }); applied = true; }
        }

        if (!applied) Debug.LogWarning("[PDR] Health 처리 불가.", this);
        else Debug.Log("[PDR] Applied damage: " + finalDamage, this);

        // 애니 재생: AnimationLockManager 우선. 없으면 직접 재생(기존 방식).
        string chosenState = (finalDamage >= heavyThreshold) ? heavyStateName : lightStateName;
        if (animationLockManager != null)
        {
            animationLockManager.PlayStateAndLock(hitLayerName, chosenState);
        }
        else
        {
            PlayHitDirect(chosenState);
        }
    }

    void PlayHitDirect(string chosenState)
    {
        if (animator == null)
        {
            Debug.Log("[PDR] animator 없다. Hit 재생 스킵", this);
            return;
        }

        int layerIndex = animator.GetLayerIndex(hitLayerName);
        if (layerIndex < 0) layerIndex = 0;

        // 강제 가중치 (임시)
        float orig = animator.GetLayerWeight(layerIndex);
        animator.SetLayerWeight(layerIndex, 1f);

        int stateHash = Animator.StringToHash(chosenState);
        animator.Play(stateHash, layerIndex, 0f);
        animator.Update(0f);

        // 복구: 코루틴으로 다음 프레임에서 원복
        StartCoroutine(RestoreLayerWeightNextFrame(layerIndex, orig));
        Debug.Log($"[PDR] DirectPlay Hit 애니 재생: {chosenState} on layer {layerIndex}", this);
    }

    System.Collections.IEnumerator RestoreLayerWeightNextFrame(int layerIndex, float orig)
    {
        yield return null;
        animator.SetLayerWeight(layerIndex, orig);
    }
}
