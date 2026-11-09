// 추온 액션 컬러 — 렌더러 피처가 매 프레임 읽는 정적 상태 (반복 11)
// ActionColourDirector / ScreenShatterDirector 가 갱신하고 ChuonActionColourFeature 가 소비한다.
using UnityEngine;

public static class ActionColourState
{
    /// <summary>KeepColour 렌더링 레이어 비트 (0~7 은 라이트 레이어용으로 비워 둔다).</summary>
    public const int KeepColourLayerBit = 9;
    public const uint KeepColourLayerMask = 1u << KeepColourLayerBit;

    public struct Snapshot
    {
        public float Desaturation;
        public float SweepRadius;
        public float SweepSoftness;
        public float RingIntensity;
        public Vector2 Center;
        public float ChromaticAberration;
        public float VignetteAmount;
        public Color VignetteColor;
        public float TintAmount;
        public Color Tint;
        public float SaturationDip;
        public float KeepLumaStart;
        public float KeepLumaEnd;
        public float BwContrast;
        public float BwBrightness;
        public Color RingColor;

        public bool IsVisible =>
            Desaturation > 0.002f || ChromaticAberration > 0.00002f || VignetteAmount > 0.002f ||
            TintAmount > 0.002f || SaturationDip > 0.002f;
    }

    public struct ShatterSnapshot
    {
        public Vector2 Impact;
        public float Crack;
        public float Burst;
        public float Alpha;
        public float ChromaticAberration;
        public Color CrackColor;
    }

    static Snapshot s_current = Default();
    static ShatterSnapshot s_shatter;
    static bool s_colourActive;
    static bool s_useKeepMask;
    static bool s_shatterActive;
    static bool s_captureRequested;
    static bool s_prewarm;

    public static Snapshot Current => s_current;
    public static ShatterSnapshot Shatter => s_shatter;
    public static bool ColourActive => s_colourActive || s_prewarm;
    public static bool UseKeepMask => s_useKeepMask || s_prewarm;
    public static bool ShatterActive => s_shatterActive || s_prewarm;
    public static Mesh ShatterMesh { get; set; }

    /// <summary>캡처 버퍼가 새로 할당됐다 (해상도 변경 등) — 진행 중 파편은 다음 프레임을 다시 캡처한다.</summary>
    public static int CaptureGeneration { get; private set; }

    public static Snapshot Default()
    {
        return new Snapshot
        {
            SweepRadius = 3f,
            SweepSoftness = 0.25f,
            Center = new Vector2(0.5f, 0.5f),
            VignetteColor = new Color(0f, 0f, 0f, 0f),
            Tint = Color.white,
            KeepLumaStart = 1.35f,
            KeepLumaEnd = 2.6f,
            BwContrast = 0.35f,
            BwBrightness = 0.95f,
            RingColor = new Color(0.9f, 0.95f, 1f, 1f)
        };
    }

    public static void SetColour(in Snapshot snapshot, bool useKeepMask)
    {
        s_current = snapshot;
        s_colourActive = snapshot.IsVisible;
        s_useKeepMask = useKeepMask && snapshot.Desaturation > 0.002f;
    }

    public static void ClearColour()
    {
        s_current = Default();
        s_colourActive = false;
        s_useKeepMask = false;
    }

    public static void SetShatter(in ShatterSnapshot snapshot, bool active)
    {
        s_shatter = snapshot;
        s_shatterActive = active;
    }

    public static void RequestShatterCapture() => s_captureRequested = true;

    internal static bool ConsumeShatterCaptureRequest()
    {
        if (!s_captureRequested)
            return false;
        s_captureRequested = false;
        return true;
    }

    internal static void NotifyShatterCaptureLost()
    {
        CaptureGeneration++;
        if (s_shatterActive)
            s_captureRequested = true;
    }

    /// <summary>프리웜: 패스·RT·셰이더를 미리 한 번 돌려서 첫 사용 히치를 없앤다 (보이지 않는 값).</summary>
    public static void SetPrewarm(bool on)
    {
        s_prewarm = on;
        if (on)
        {
            Snapshot s = Default();
            s.Desaturation = 0f;
            s_current = s;
            s_shatter = new ShatterSnapshot { Impact = new Vector2(0.5f, 0.5f), Alpha = 0f, CrackColor = Color.white };
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        s_current = Default();
        s_shatter = default;
        s_colourActive = false;
        s_useKeepMask = false;
        s_shatterActive = false;
        s_captureRequested = false;
        s_prewarm = false;
    }
}
