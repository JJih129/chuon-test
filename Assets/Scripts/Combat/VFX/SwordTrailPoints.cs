using UnityEngine;

/// <summary>
/// Usage:
/// 1. Create empty child objects named TrailBase and TrailTip under the sword.
/// 2. Place TrailBase near the grip and TrailTip near the blade tip.
/// 3. Assign both Transforms here, then reference this component from SwordTrailMeshRenderer.
/// </summary>
[DisallowMultipleComponent]
public sealed class SwordTrailPoints : MonoBehaviour
{
    [Tooltip("Sword grip-side point used as one edge of the generated mesh trail.")]
    [SerializeField] Transform trailBase;

    [Tooltip("Sword tip point used as the other edge of the generated mesh trail.")]
    [SerializeField] Transform trailTip;

    public Transform TrailBase => trailBase;
    public Transform TrailTip => trailTip;
    public bool HasValidPoints => trailBase != null && trailTip != null;

    public void SetPoints(Transform basePoint, Transform tipPoint)
    {
        trailBase = basePoint;
        trailTip = tipPoint;
    }

    void Reset()
    {
        trailBase = transform.Find("TrailBase");
        trailTip = transform.Find("TrailTip");
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        // 런타임 AddComponent(PlayerAttackVfxPresenter 의 자동 연결) 직후에는 아직 비어 있으므로 플레이 중엔 경고하지 않음
        if (!Application.isPlaying && (trailBase == null || trailTip == null))
            Debug.LogWarning("[SwordTrailPoints] TrailBase and TrailTip must be assigned.", this);
    }

    void OnDrawGizmosSelected()
    {
        if (trailBase == null || trailTip == null)
            return;

        Gizmos.color = new Color(0.1f, 0.9f, 1f, 0.9f);
        Gizmos.DrawLine(trailBase.position, trailTip.position);
        Gizmos.DrawSphere(trailBase.position, 0.025f);
        Gizmos.DrawWireSphere(trailTip.position, 0.04f);
    }
#endif
}
