using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 씬에 배치하는 이름 붙은 연출 기준점 (예: "CineAnchor_LobbyFrontDesk").
/// 연출 리그(CinematicSequence)의 CineAttach 가 이 이름으로 위치·방향을 맞춘다. 앵커 목록은 Docs/Cinematics_Shotlist.md.
/// 컴포넌트가 없으면 같은 이름의 GameObject 를 찾는다 (GameObject.Find, 재생 시작 때 1회).
/// </summary>
[DisallowMultipleComponent]
public sealed class CinematicAnchor : MonoBehaviour
{
    [SerializeField] string anchorName;

    static readonly Dictionary<string, CinematicAnchor> s_anchors = new Dictionary<string, CinematicAnchor>(System.StringComparer.Ordinal);

    public string AnchorName => string.IsNullOrEmpty(anchorName) ? name : anchorName;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => s_anchors.Clear();

    public void Configure(string anchor) => anchorName = anchor;

    void OnEnable()
    {
        s_anchors[AnchorName] = this;
    }

    void OnDisable()
    {
        if (s_anchors.TryGetValue(AnchorName, out CinematicAnchor current) && current == this)
            s_anchors.Remove(AnchorName);
    }

    public static Transform Resolve(string anchor)
    {
        if (string.IsNullOrEmpty(anchor))
            return null;
        if (s_anchors.TryGetValue(anchor, out CinematicAnchor found) && found != null)
            return found.transform;
        GameObject go = GameObject.Find(anchor);
        return go != null ? go.transform : null;
    }

#if UNITY_EDITOR
    void OnDrawGizmos()
    {
        Gizmos.color = new Color(1f, 0.15f, 0.12f, 0.9f);
        Vector3 p = transform.position;
        Gizmos.DrawWireSphere(p, 0.25f);
        Gizmos.DrawLine(p, p + transform.forward * 1.2f);
        Gizmos.DrawLine(p + transform.forward * 1.2f, p + transform.forward * 0.9f + transform.right * 0.2f);
        Gizmos.DrawLine(p + transform.forward * 1.2f, p + transform.forward * 0.9f - transform.right * 0.2f);
    }
#endif
}
