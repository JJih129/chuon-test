// 추온 액션 컬러 — 렌더러 피처가 매 프레임 읽는 정적 상태 (반복 11)
// ActionColourDirector (흑백·포스트 이벤트·화면 베기) 가 갱신하고 ChuonActionColourFeature 가 소비한다.
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

    public const int CutMaxLines = 3;

    /// <summary>
    /// 화면 베기 (반복 14S) — ChuonScreenCut 셰이더 상수 그대로 (종횡비 공간: x ±aspect, y ±1).
    /// 배열은 미리 할당돼 매 프레임 덮어쓴다 (할당 없음).
    /// </summary>
    public sealed class CutData
    {
        public readonly Vector4[] Lines = new Vector4[CutMaxLines];  // xy 점, zw 방향
        public readonly Vector4[] Spans = new Vector4[CutMaxLines];  // x 진입 t, y 이탈 t, z 그리기 진행, w 번쩍임
        public readonly Vector4[] Moves = new Vector4[CutMaxLines];  // x 미끄러짐, y 벌어짐, z 회전 rad
        public int Count;
        public float Calm;
        public Vector4 State;  // x 분리 진행, y 조각 알파, z 틈 번쩍임, w 어둡게
        public Vector4 Fx;     // x 선 알파, y 스피드 라인, z calm 전선, w 줄무늬
        public Vector4 Fx2;    // x 절단면 발광, y 흰 틴트, z calm 경계 폭
        public Color Core = Color.white;
        public Color Edge = Color.red;
        public Color Gap = Color.white;

        public void Clear()
        {
            Count = 0;
            Calm = 0f;
            State = Vector4.zero;
            Fx = Vector4.zero;
            Fx2 = Vector4.zero;
            for (int i = 0; i < CutMaxLines; i++)
            {
                Lines[i] = new Vector4(0f, 0f, 1f, 0f);
                Spans[i] = Vector4.zero;
                Moves[i] = Vector4.zero;
            }
        }
    }

    static Snapshot s_current = Default();
    static readonly CutData s_cut = new CutData();
    static bool s_colourActive;
    static bool s_useKeepMask;
    static bool s_cutActive;
    static bool s_captureRequested;
    static bool s_prewarm;

    public static Snapshot Current => s_current;
    public static CutData Cut => s_cut;
    public static bool ColourActive => s_colourActive || s_prewarm;
    public static bool UseKeepMask => s_useKeepMask || s_prewarm;
    public static bool CutActive => s_cutActive || s_prewarm;

    /// <summary>캡처 버퍼가 새로 할당됐다 (해상도 변경 등) — 진행 중 베기는 다음 프레임을 다시 캡처한다.</summary>
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

    /// <summary>베기 패스 켜기/끄기. 끌 때 상수도 비운다 (값은 Cut 에 직접 쓴다).</summary>
    public static void SetCutActive(bool active)
    {
        s_cutActive = active;
        if (!active)
            s_cut.Clear();
    }

    public static void RequestCutCapture() => s_captureRequested = true;

    internal static bool ConsumeCutCaptureRequest()
    {
        if (!s_captureRequested)
            return false;
        s_captureRequested = false;
        return true;
    }

    internal static void NotifyCutCaptureLost()
    {
        CaptureGeneration++;
        if (s_cutActive)
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
            if (!s_cutActive)
                s_cut.Clear();   // 선 0 개 + 조각 알파 0 → 패스는 돌지만 보이지 않는다
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        s_current = Default();
        s_cut.Clear();
        s_colourActive = false;
        s_useKeepMask = false;
        s_cutActive = false;
        s_captureRequested = false;
        s_prewarm = false;
    }
}
