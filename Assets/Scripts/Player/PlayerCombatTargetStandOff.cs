using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
[RequireComponent(typeof(CharacterController))]
public sealed class PlayerCombatTargetStandOff : MonoBehaviour
{
    [SerializeField] CharacterController characterController;
    [SerializeField] LayerMask targetProbeMask = ~0;
    [SerializeField, Min(0f)] float standOffPadding = 0.14f;
    [SerializeField, Min(0f)] float topRejectHeight = 0.28f;
    [SerializeField, Min(0.1f)] float probeRadiusPadding = 0.8f;
    [SerializeField, Min(0.01f)] float maxPushPerFrame = 0.35f;

    readonly Collider[] _hits = new Collider[16];

    void Awake()
    {
        if (characterController == null)
            characterController = GetComponent<CharacterController>();
    }

    void LateUpdate()
    {
        if (characterController == null || !characterController.enabled)
            return;

        ResolveTargetOverlap();
    }

    void ResolveTargetOverlap()
    {
        Vector3 center = transform.TransformPoint(characterController.center);
        float halfHeight = Mathf.Max(characterController.height * 0.5f, characterController.radius);
        float feetY = center.y - halfHeight;
        float headY = center.y + halfHeight;
        float probeRadius = characterController.radius + probeRadiusPadding;

        int hitCount = Physics.OverlapSphereNonAlloc(
            center,
            probeRadius,
            _hits,
            targetProbeMask,
            QueryTriggerInteraction.Ignore);

        Vector3 totalPush = Vector3.zero;
        for (int i = 0; i < hitCount; i++)
        {
            Collider hit = _hits[i];
            if (!IsCombatTargetCollider(hit))
                continue;

            Bounds bounds = hit.bounds;
            bool verticalOverlap = feetY <= bounds.max.y + topRejectHeight && headY >= bounds.min.y - 0.05f;
            if (!verticalOverlap)
                continue;

            Vector3 push = ComputeHorizontalPush(center, bounds);
            totalPush += push;
        }

        totalPush.y = 0f;
        if (totalPush.sqrMagnitude <= 0.000001f)
            return;

        if (totalPush.magnitude > maxPushPerFrame)
            totalPush = totalPush.normalized * maxPushPerFrame;

        characterController.Move(totalPush);
    }

    Vector3 ComputeHorizontalPush(Vector3 playerCenter, Bounds targetBounds)
    {
        Vector3 closest = new Vector3(
            Mathf.Clamp(playerCenter.x, targetBounds.min.x, targetBounds.max.x),
            playerCenter.y,
            Mathf.Clamp(playerCenter.z, targetBounds.min.z, targetBounds.max.z));

        Vector3 away = playerCenter - closest;
        away.y = 0f;
        float distance = away.magnitude;
        if (distance <= 0.0001f)
        {
            away = playerCenter - targetBounds.center;
            away.y = 0f;
            distance = away.magnitude;
        }

        if (distance <= 0.0001f)
            away = transform.forward;
        else
            away /= distance;

        float requiredClearance = characterController.radius + standOffPadding;
        float penetration = requiredClearance - distance;
        if (penetration <= 0f)
            return Vector3.zero;

        return away * penetration;
    }

    bool IsCombatTargetCollider(Collider hit)
    {
        if (hit == null || hit.isTrigger)
            return false;

        Transform hitTransform = hit.transform;
        if (hitTransform == transform || hitTransform.IsChildOf(transform) || transform.IsChildOf(hitTransform))
            return false;

        if (_enemyLayer == int.MinValue)
            _enemyLayer = LayerMask.NameToLayer("Enemy");

        if (_enemyLayer >= 0 && hit.gameObject.layer == _enemyLayer)
            return true;

        // 콜라이더별 판정 결과 캐시 (GetComponentInParent 반복 호출 방지)
        // 런타임에 대상 컴포넌트가 추가되는 경우를 대비해 주기적으로 갱신
        float now = Time.unscaledTime;
        if (_targetColliderCache.Count >= MaxCachedColliders || now >= _cacheResetAt)
        {
            _targetColliderCache.Clear();
            _cacheResetAt = now + CacheResetIntervalSeconds;
        }

        int id = hit.GetInstanceID();
        if (_targetColliderCache.TryGetValue(id, out bool cached))
            return cached;

        bool isTarget = hit.GetComponentInParent<BossHealth>() != null
            || hit.GetComponentInParent<TrainingDummyController>() != null
            || hit.GetComponentInParent<TutorialEnemyVisualRig>() != null
            || hit.GetComponentInParent<UltimateTargetSimple>() != null
            || hit.GetComponentInParent<IHealth>() != null;
        _targetColliderCache[id] = isTarget;
        return isTarget;
    }

    const int MaxCachedColliders = 512;
    const float CacheResetIntervalSeconds = 2f;
    float _cacheResetAt;
    int _enemyLayer = int.MinValue;
    readonly Dictionary<int, bool> _targetColliderCache = new Dictionary<int, bool>(64);

    void OnEnable()
    {
        SceneManager.activeSceneChanged += HandleActiveSceneChanged;
    }

    void OnDisable()
    {
        SceneManager.activeSceneChanged -= HandleActiveSceneChanged;
        _targetColliderCache.Clear();
    }

    void HandleActiveSceneChanged(Scene previous, Scene next)
    {
        _targetColliderCache.Clear();
    }
}
