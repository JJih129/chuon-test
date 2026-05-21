using UnityEngine;

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

        int enemyLayer = LayerMask.NameToLayer("Enemy");
        if (enemyLayer >= 0 && hit.gameObject.layer == enemyLayer)
            return true;

        return hit.GetComponentInParent<BossHealth>() != null
            || hit.GetComponentInParent<TrainingDummyController>() != null
            || hit.GetComponentInParent<TutorialEnemyVisualRig>() != null
            || hit.GetComponentInParent<UltimateTargetSimple>() != null
            || hit.GetComponentInParent<IHealth>() != null;
    }
}
