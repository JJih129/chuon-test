// 추온 화면 갈라짐 / 유리 갈라짐 API (반복 11)
// 현재 프레임(포스트 처리 후)을 캡처해 Blender 보로노이 파편 메시에 매핑 → 균열선 → 파편 폭발 → 다음 프레임 노출.
// 실시간 기준, 기본 0.8 초. 시네마틱 건너뛰기 시 Skip() 으로 바로 흩어진다.
using UnityEngine;

public static class ScreenShatter
{
    public const float DefaultCrackDuration = 0.22f;
    public const float DefaultBurstDuration = 0.58f;

    public static bool IsPlaying => ActionColourDirector.ShatterPlaying;
    public static int PlayCount { get; private set; }

    /// <summary>impactViewport: 균열이 시작될 화면 위치 (0..1). onBurst: 균열이 끝나고 파편이 튀는 순간.</summary>
    public static void Play(Vector2 impactViewport, float crackDuration = DefaultCrackDuration, float burstDuration = DefaultBurstDuration, System.Action onBurst = null)
    {
        PlayCount++;
        ActionColourDirector.StartShatter(impactViewport, crackDuration, burstDuration, onBurst);
    }

    public static void Skip()
    {
        ActionColourDirector.SkipShatter();
    }
}

public static class ScreenShatterAssets
{
    const string MeshPath = "VFX/ScreenShatter_Shards";
    static Mesh s_mesh;
    static bool s_tried;

    public static Mesh Load()
    {
        if (s_mesh != null)
            return s_mesh;
        if (s_tried)
            return null;
        s_tried = true;
        s_mesh = Resources.Load<Mesh>(MeshPath);
        if (s_mesh == null)
        {
            GameObject prefab = Resources.Load<GameObject>(MeshPath);
            MeshFilter mf = prefab != null ? prefab.GetComponentInChildren<MeshFilter>() : null;
            s_mesh = mf != null ? mf.sharedMesh : null;
        }
        if (s_mesh != null)
            ActionColourState.ShatterMesh = s_mesh;
        return s_mesh;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        s_tried = false;
    }
}
