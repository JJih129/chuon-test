// 처형 카메라 샷 라이브러리 (종류별 2~3 변형 + 움직임 줄이기용 고정 샷).
// 에셋: Assets/Settings/Camera/ExecutionCam/Resources/ExecutionCam/ExecutionCamLibrary.asset
// (없으면 BuildDefaults() 값으로 런타임 생성). 메뉴 Chuon/Camera/Execution Cam/Write Default Library 로 다시 쓴다.
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Chuon/Camera/Execution Cam Library", fileName = "ExecutionCamLibrary")]
public sealed class ExecutionCamLibrary : ScriptableObject
{
    public const string ResourcePath = "ExecutionCam/ExecutionCamLibrary";

    [Header("프레이밍 규칙")]
    [Tooltip("카메라 최저 높이 (발 기준 m). 다리·바닥 프레이밍 금지")]
    public float minCameraHeight = 0.6f;
    [Tooltip("시선점 최저 높이 (공격자 발 기준 m). 가슴~머리 높이 유지")]
    public float minLookHeight = 1.0f;
    [Tooltip("카메라 → 시선점 올려다보기 상한 (°)")]
    public float maxLookUp = 16f;
    [Tooltip("피벗 기준 고도 하한/상한 (°)")]
    public float minElevation = -12f;
    public float maxElevation = 40f;
    [Range(0f, 8f)] public float maxDutch = 8f;
    public float minFov = 22f;
    public float maxFov = 60f;
    [Tooltip("머리·가슴이 화면 가장자리에서 떨어져야 하는 비율")]
    [Range(0f, 0.3f)] public float frameMargin = 0.08f;
    [Tooltip("두 인물이 안 보이면 거리를 이 배율까지 늘린다")]
    public float maxFitScale = 1.7f;

    [Header("충돌")]
    public float collisionRadius = 0.25f;
    public LayerMask collisionMask = (1 << 0) | (1 << 13);
    [Tooltip("이보다 얇은 콜라이더(난간·기둥)는 무시")]
    public float thinPropSize = 0.35f;
    public float characterClearance = 0.45f;

    [Header("종류별 샷")]
    public List<ExecCamTypeEntry> types = new List<ExecCamTypeEntry>();

    static ExecutionCamLibrary s_cached;
    static bool s_loaded;

    public static ExecutionCamLibrary Load()
    {
        if (s_cached != null)
            return s_cached;
        if (!s_loaded)
        {
            s_loaded = true;
            s_cached = Resources.Load<ExecutionCamLibrary>(ResourcePath);
        }
        if (s_cached == null)
        {
            s_cached = CreateInstance<ExecutionCamLibrary>();
            s_cached.name = "ExecutionCamLibrary_Runtime";
            s_cached.hideFlags = HideFlags.DontSave;
            s_cached.BuildDefaults();
        }
        return s_cached;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        s_cached = null;
        s_loaded = false;
    }

    public ExecCamTypeEntry Get(ExecCamType type)
    {
        for (int i = 0; i < types.Count; i++)
            if (types[i] != null && types[i].type == type && types[i].variants != null && types[i].variants.Count > 0)
                return types[i];
        // 에셋에 빠진 종류는 기본값으로 (에셋은 건드리지 않는다)
        if (s_defaults == null)
        {
            s_defaults = CreateInstance<ExecutionCamLibrary>();
            s_defaults.hideFlags = HideFlags.HideAndDontSave;
            s_defaults.BuildDefaults();
        }
        if (s_defaults == this)
            return null;
        return s_defaults.Get(type);
    }

    static ExecutionCamLibrary s_defaults;

    // ================================================================== 기본 샷 리스트
    // 시간 표기는 단계 비율(0..1). 실제 길이: ExecutionDirector Timing 기준 타격 전/후 실시간.

    static ExecCamPose P(float az, float el, float d, float fov, float dutch = 0f, float pb = 0.5f, float lb = 0.55f)
        => new ExecCamPose(az, el, d, fov, dutch, pb, lb);

    static ExecCamShot Orbit(string name, ExecCamPhase ph, float s, float e, float pivotH, ExecCamPose a, ExecCamPose b,
        ExecCamEase ease, ExecCamEntry entry = ExecCamEntry.Cut, float entryTime = 0f, float jolt = 0f)
        => new ExecCamShot { name = name, phase = ph, start = s, end = e, mode = ExecCamMode.Orbit, pivotHeight = pivotH, from = a, to = b, ease = ease, entry = entry, entryTime = entryTime, jolt = jolt };

    static ExecCamShot Insert(string name, ExecCamPhase ph, float s, float e, ExecCamPose a, ExecCamPose b, ExecCamEase ease, ExecCamEntry entry = ExecCamEntry.Cut, float entryTime = 0f)
        => new ExecCamShot { name = name, phase = ph, start = s, end = e, mode = ExecCamMode.Orbit, pivot = ExecCamPivot.Impact, lookImpact = true, keepBoth = false, from = a, to = b, ease = ease, entry = entry, entryTime = entryTime };

    static ExecCamShot Push(string name, ExecCamPhase ph, float s, float e, float distMul, float fovDelta, float dutchDelta, float lookBias, ExecCamEase ease, bool lookImpact = true, float jolt = 0f)
        => new ExecCamShot { name = name, phase = ph, start = s, end = e, mode = ExecCamMode.PushInCurrent, lookImpact = lookImpact, to = new ExecCamPose(0f, 0f, distMul, fovDelta, dutchDelta, 0.5f, lookBias), ease = ease, jolt = jolt };

    static ExecCamShot Move(string name, ExecCamPhase ph, float s, float e, float pivotH, ExecCamPose to, ExecCamEase ease)
        => new ExecCamShot { name = name, phase = ph, start = s, end = e, mode = ExecCamMode.FromCurrent, pivotHeight = pivotH, to = to, ease = ease };

    static ExecCamShot Return(ExecCamPhase ph, float s, float e)
        => new ExecCamShot { name = "return", phase = ph, start = s, end = e, mode = ExecCamMode.ReturnToGameplay, ease = ExecCamEase.EaseInOut };

    static ExecCamVariant V(string name, params ExecCamShot[] shots)
        => new ExecCamVariant { name = name, shots = new List<ExecCamShot>(shots) };

    const ExecCamPhase Pre = ExecCamPhase.PreImpact;
    const ExecCamPhase Post = ExecCamPhase.PostImpact;

    static ExecCamVariant Calm(float pivotH, float dist, float fov)
    {
        ExecCamPose p = P(62f, 10f, dist, fov, 0f, 0.5f, 0.55f);
        return V("calm",
            Orbit("calm_hold", Pre, 0f, 1f, pivotH, p, p, ExecCamEase.Linear, ExecCamEntry.Blend, 0.3f),
            Orbit("calm_hold_post", Post, 0f, 0.62f, pivotH, p, p, ExecCamEase.Linear),
            Return(Post, 0.62f, 1f));
    }

    static ExecCamTypeEntry T(ExecCamType type, float rampScale, float rampHold, float rampOut, float jolt, float fovKick, float bw, ExecCamVariant calm, params ExecCamVariant[] variants)
        => new ExecCamTypeEntry { type = type, rampScale = rampScale, rampHold = rampHold, rampOut = rampOut, impactFreezeFrames = 2, impactJolt = jolt, impactFovKick = fovKick, impactBwPulse = bw, calm = calm, variants = new List<ExecCamVariant>(variants) };

    public void BuildDefaults()
    {
        types = new List<ExecCamTypeEntry>
        {
            // ---------------------------------------------------------- 보스 정면 (타격 전 ≈1.15 s / 후 ≈1.6 s)
            T(ExecCamType.BossFront, 0.35f, 0.32f, 0.22f, 0.07f, -3f, 0f, Calm(1.5f, 6.2f, 44f),
                V("F1_SnapOrbitLow",
                    Orbit("snap_zoom", Pre, 0f, 0.28f, 1.35f, P(62, 6, 6.0f, 45), P(68, 2, 4.6f, 36, -2, 0.5f, 0.55f), ExecCamEase.Snap),
                    Orbit("low_orbit", Pre, 0.28f, 1f, 1.35f, P(68, 2, 4.6f, 36, -2, 0.5f, 0.55f), P(118, -9, 3.9f, 33, -6, 0.55f, 0.6f), ExecCamEase.EaseInOut),
                    Push("push_blow", Post, 0f, 0.42f, 0.72f, -3f, 0f, 0.7f, ExecCamEase.Snap),
                    Push("breathe_out", Post, 0.42f, 0.72f, 1.25f, 4f, 3f, 0.6f, ExecCamEase.EaseInOut),
                    Return(Post, 0.72f, 1f)),
                V("F2_ShoulderSweep",
                    Orbit("shoulder_snap", Pre, 0f, 0.35f, 1.45f, P(20, 12, 3.4f, 44, 0, 0.3f, 0.6f), P(28, 9, 2.9f, 38, 0, 0.3f, 0.62f), ExecCamEase.Snap),
                    Orbit("sweep_orbit", Pre, 0.35f, 1f, 1.4f, P(28, 9, 3.0f, 38, 0, 0.4f, 0.6f), P(105, -7, 3.7f, 34, 5, 0.5f, 0.6f), ExecCamEase.EaseInOut, ExecCamEntry.Blend, 0.08f),
                    Push("push_blow", Post, 0f, 0.4f, 0.7f, -4f, 2f, 0.72f, ExecCamEase.Snap),
                    Push("breathe_out", Post, 0.4f, 0.72f, 1.2f, 3f, -3f, 0.6f, ExecCamEase.EaseInOut),
                    Return(Post, 0.72f, 1f)),
                V("F3_ProfileWhip",
                    Orbit("profile_track", Pre, 0f, 0.55f, 1.4f, P(90, 1, 5.8f, 40, 0, 0.25f, 0.45f), P(90, 0, 5.4f, 38, -3, 0.7f, 0.55f), ExecCamEase.Linear),
                    Orbit("whip_low_front", Pre, 0.55f, 1f, 1.35f, P(138, -8, 3.6f, 34, 6, 0.55f, 0.6f), P(144, -10, 3.3f, 32, 7, 0.55f, 0.62f), ExecCamEase.EaseOut, ExecCamEntry.Whip, 0.09f),
                    Push("push_blow", Post, 0f, 0.4f, 0.74f, -4f, 0f, 0.72f, ExecCamEase.Snap),
                    Push("breathe_out", Post, 0.4f, 0.72f, 1.22f, 4f, -4f, 0.6f, ExecCamEase.EaseInOut),
                    Return(Post, 0.72f, 1f))),

            // ---------------------------------------------------------- 보스 후면 (≈1.0 s / ≈1.55 s) — 등 뒤 찌르기
            T(ExecCamType.BossBack, 0.35f, 0.3f, 0.22f, 0.07f, -3f, 0f, Calm(1.5f, 6.0f, 44f),
                V("B1_ShoulderToLowSide",
                    Orbit("shoulder_snap", Pre, 0f, 0.4f, 1.5f, P(16, 13, 3.0f, 42, 0, 0.35f, 0.62f), P(22, 10, 2.5f, 36, 0, 0.35f, 0.64f), ExecCamEase.Snap),
                    Orbit("drop_to_side", Pre, 0.4f, 1f, 1.4f, P(22, 10, 2.6f, 36, 0, 0.4f, 0.62f), P(96, -6, 3.3f, 33, -5, 0.5f, 0.62f), ExecCamEase.EaseInOut, ExecCamEntry.Blend, 0.06f),
                    Push("push_blow", Post, 0f, 0.4f, 0.72f, -4f, 0f, 0.72f, ExecCamEase.Snap),
                    Push("breathe_out", Post, 0.4f, 0.72f, 1.2f, 4f, 3f, 0.6f, ExecCamEase.EaseInOut),
                    Return(Post, 0.72f, 1f)),
                V("B2_LowFrontQuarter",
                    Orbit("snap_quarter", Pre, 0f, 0.3f, 1.35f, P(128, -4, 4.6f, 42, 0, 0.5f, 0.5f), P(124, -7, 3.8f, 35, 3, 0.5f, 0.55f), ExecCamEase.Snap),
                    Orbit("low_orbit", Pre, 0.3f, 1f, 1.35f, P(124, -7, 3.8f, 35, 3, 0.5f, 0.55f), P(102, -10, 3.3f, 32, 6, 0.55f, 0.6f), ExecCamEase.EaseInOut),
                    Push("push_blow", Post, 0f, 0.4f, 0.7f, -4f, 1f, 0.72f, ExecCamEase.Snap),
                    Push("breathe_out", Post, 0.4f, 0.72f, 1.22f, 4f, -3f, 0.6f, ExecCamEase.EaseInOut),
                    Return(Post, 0.72f, 1f)),
                V("B3_HighStalkWhip",
                    Orbit("high_stalk", Pre, 0f, 0.6f, 1.4f, P(40, 26, 4.2f, 40, 0, 0.4f, 0.55f), P(48, 22, 3.4f, 36, 0, 0.45f, 0.6f), ExecCamEase.EaseOut),
                    Orbit("whip_side_low", Pre, 0.6f, 1f, 1.35f, P(110, -6, 3.0f, 32, -7, 0.5f, 0.62f), P(114, -8, 2.8f, 31, -7, 0.5f, 0.64f), ExecCamEase.EaseOut, ExecCamEntry.Whip, 0.08f),
                    Push("push_blow", Post, 0f, 0.4f, 0.74f, -3f, 0f, 0.72f, ExecCamEase.Snap),
                    Push("breathe_out", Post, 0.4f, 0.72f, 1.2f, 4f, 4f, 0.6f, ExecCamEase.EaseInOut),
                    Return(Post, 0.72f, 1f))),

            // ---------------------------------------------------------- 최종 코어 절단 (≈1.6 s / ≈2.2 s, 합 ≈3.8 s)
            T(ExecCamType.BossFinal, 0.3f, 0.45f, 0.3f, 0.1f, -4f, 0f, Calm(1.5f, 6.5f, 44f),
                V("N1_CoreInsertSever",
                    Orbit("low_wide", Pre, 0f, 0.32f, 1.3f, P(100, -7, 7.0f, 44, 0, 0.5f, 0.5f), P(96, -8, 6.0f, 40, -2, 0.5f, 0.52f), ExecCamEase.EaseOut),
                    Insert("core_insert", Pre, 0.32f, 0.7f, P(55, 4, 1.7f, 30, 0, 1f, 1f), P(52, 3, 1.35f, 24, 3, 1f, 1f), ExecCamEase.Snap),
                    Orbit("whip_side_thrust", Pre, 0.7f, 1f, 1.35f, P(88, -5, 3.6f, 34, -4, 0.5f, 0.62f), P(84, -6, 3.2f, 32, -6, 0.5f, 0.64f), ExecCamEase.EaseOut, ExecCamEntry.Whip, 0.08f),
                    Push("pierce_push", Post, 0f, 0.28f, 0.72f, -5f, 2f, 0.75f, ExecCamEase.Snap),
                    Move("crane_out", Post, 0.28f, 0.68f, 1.4f, P(70, 16, 6.2f, 40, 0, 0.5f, 0.55f), ExecCamEase.EaseInOut),
                    Return(Post, 0.68f, 1f)),
                V("N2_OrbitArc",
                    Orbit("arc_orbit", Pre, 0f, 0.5f, 1.35f, P(60, 4, 5.2f, 42, 0, 0.5f, 0.55f), P(110, -8, 4.4f, 36, -4, 0.5f, 0.58f), ExecCamEase.EaseInOut),
                    Insert("core_insert", Pre, 0.5f, 0.78f, P(50, 2, 1.6f, 30, 0, 1f, 1f), P(45, 3, 1.35f, 25, -3, 1f, 1f), ExecCamEase.Snap),
                    Orbit("whip_low_front", Pre, 0.78f, 1f, 1.35f, P(130, -9, 3.5f, 33, 6, 0.55f, 0.62f), P(134, -10, 3.2f, 31, 7, 0.55f, 0.64f), ExecCamEase.EaseOut, ExecCamEntry.Whip, 0.08f),
                    Push("pierce_push", Post, 0f, 0.28f, 0.7f, -5f, 1f, 0.75f, ExecCamEase.Snap),
                    Move("crane_out", Post, 0.28f, 0.68f, 1.4f, P(150, 14, 6.5f, 42, 0, 0.5f, 0.55f), ExecCamEase.EaseInOut),
                    Return(Post, 0.68f, 1f)),
                V("N3_ShoulderCore",
                    Orbit("shoulder_snap", Pre, 0f, 0.45f, 1.5f, P(18, 12, 3.4f, 42, 0, 0.35f, 0.62f), P(24, 9, 2.8f, 36, 0, 0.35f, 0.65f), ExecCamEase.Snap),
                    Insert("core_insert", Pre, 0.45f, 0.75f, P(60, 0, 1.7f, 30, 0, 1f, 1f), P(56, 1, 1.4f, 25, 2, 1f, 1f), ExecCamEase.Snap),
                    Orbit("whip_side_thrust", Pre, 0.75f, 1f, 1.35f, P(96, -6, 3.6f, 34, -5, 0.5f, 0.62f), P(92, -7, 3.3f, 32, -6, 0.5f, 0.64f), ExecCamEase.EaseOut, ExecCamEntry.Whip, 0.08f),
                    Push("pierce_push", Post, 0f, 0.28f, 0.72f, -5f, 2f, 0.75f, ExecCamEase.Snap),
                    Move("crane_out", Post, 0.28f, 0.68f, 1.4f, P(80, 18, 6.0f, 40, 0, 0.5f, 0.55f), ExecCamEase.EaseInOut),
                    Return(Post, 0.68f, 1f))),

            // ---------------------------------------------------------- 경비 안드로이드 (≈0.7 s / ≈0.6 s)
            T(ExecCamType.Android, 0.3f, 0.12f, 0.16f, 0.05f, -2.5f, 0f, Calm(1.35f, 4.2f, 44f),
                V("A1_SnapQuarter",
                    Orbit("snap_zoom", Pre, 0f, 0.35f, 1.25f, P(66, 5, 3.8f, 44), P(72, 2, 3.0f, 36, -2, 0.5f, 0.52f), ExecCamEase.Snap),
                    Orbit("low_orbit", Pre, 0.35f, 1f, 1.25f, P(72, 2, 3.0f, 36, -2, 0.5f, 0.52f), P(104, -7, 2.7f, 33, -5, 0.55f, 0.55f), ExecCamEase.EaseInOut),
                    Push("push_blow", Post, 0f, 0.45f, 0.74f, -4f, 1f, 0.6f, ExecCamEase.Snap),
                    Return(Post, 0.45f, 1f)),
                V("A2_LowProfile",
                    Orbit("low_profile", Pre, 0f, 1f, 1.25f, P(92, -8, 3.4f, 40, 0, 0.4f, 0.5f), P(118, -10, 2.8f, 34, 5, 0.55f, 0.55f), ExecCamEase.EaseOut),
                    Push("push_blow", Post, 0f, 0.45f, 0.75f, -4f, 0f, 0.6f, ExecCamEase.Snap),
                    Return(Post, 0.45f, 1f)),
                V("A3_ShoulderWhip",
                    Orbit("shoulder_snap", Pre, 0f, 0.55f, 1.4f, P(18, 10, 2.6f, 42, 0, 0.3f, 0.6f), P(24, 8, 2.2f, 37, 0, 0.3f, 0.62f), ExecCamEase.Snap),
                    Orbit("whip_side", Pre, 0.55f, 1f, 1.25f, P(96, -5, 2.9f, 34, -4, 0.5f, 0.55f), P(100, -6, 2.7f, 33, -5, 0.5f, 0.56f), ExecCamEase.EaseOut, ExecCamEntry.Whip, 0.07f),
                    Push("push_blow", Post, 0f, 0.45f, 0.74f, -4f, 0f, 0.6f, ExecCamEase.Snap),
                    Return(Post, 0.45f, 1f))),

            // ---------------------------------------------------------- 방패 집행관 (≈0.7 s / ≈0.6 s) — 방패가 가리지 않게 옆·위에서
            T(ExecCamType.Enforcer, 0.3f, 0.12f, 0.16f, 0.07f, -3f, 0f, Calm(1.4f, 4.4f, 44f),
                V("E1_SideDollyLow",
                    Orbit("snap_side", Pre, 0f, 0.3f, 1.3f, P(80, -3, 4.4f, 44, 0, 0.4f, 0.5f), P(84, -5, 3.6f, 38, -2, 0.45f, 0.52f), ExecCamEase.Snap),
                    Orbit("low_dolly", Pre, 0.3f, 1f, 1.3f, P(84, -5, 3.6f, 38, -2, 0.45f, 0.52f), P(100, -9, 3.0f, 33, -5, 0.55f, 0.55f), ExecCamEase.EaseInOut),
                    Push("push_blow", Post, 0f, 0.45f, 0.72f, -4f, 1f, 0.6f, ExecCamEase.Snap),
                    Return(Post, 0.45f, 1f)),
                V("E2_HighSlam",
                    Orbit("high_quarter", Pre, 0f, 0.6f, 1.4f, P(52, 24, 3.6f, 42, 0, 0.45f, 0.5f), P(58, 20, 3.0f, 37, 0, 0.5f, 0.52f), ExecCamEase.EaseOut),
                    Orbit("whip_low", Pre, 0.6f, 1f, 1.3f, P(112, -6, 2.9f, 33, 6, 0.55f, 0.55f), P(116, -8, 2.7f, 32, 7, 0.55f, 0.56f), ExecCamEase.EaseOut, ExecCamEntry.Whip, 0.07f),
                    Push("push_blow", Post, 0f, 0.45f, 0.74f, -4f, 0f, 0.6f, ExecCamEase.Snap),
                    Return(Post, 0.45f, 1f)),
                V("E3_QuarterArc",
                    Orbit("quarter_arc", Pre, 0f, 1f, 1.3f, P(130, -3, 4.0f, 42, 0, 0.5f, 0.5f), P(108, -8, 3.0f, 34, 4, 0.55f, 0.55f), ExecCamEase.EaseInOut),
                    Push("push_blow", Post, 0f, 0.45f, 0.74f, -4f, 0f, 0.6f, ExecCamEase.Snap),
                    Return(Post, 0.45f, 1f))),

            // ---------------------------------------------------------- 잠입 암살 (≈1.0 s / ≈0.7 s) — 조용하게, 흔들림 작게
            T(ExecCamType.Takedown, 0.3f, 0.14f, 0.22f, 0.03f, -2f, 0.7f, Calm(1.35f, 4.0f, 42f),
                V("T1_CreepShoulder",
                    Orbit("creep_push", Pre, 0f, 1f, 1.45f, P(14, 8, 2.5f, 42, 0, 0.35f, 0.62f), P(22, 6, 1.9f, 35, 0, 0.4f, 0.64f), ExecCamEase.EaseIn),
                    Move("swing_side", Post, 0f, 0.45f, 1.3f, P(95, 0, 2.2f, 31, -3, 0.5f, 0.6f), ExecCamEase.Snap),
                    Return(Post, 0.45f, 1f)),
                V("T2_LowSideHold",
                    Orbit("low_side", Pre, 0f, 1f, 1.3f, P(86, -5, 3.0f, 38, 0, 0.35f, 0.55f), P(90, -6, 2.6f, 34, -2, 0.5f, 0.58f), ExecCamEase.Linear),
                    Push("push_blow", Post, 0f, 0.45f, 0.75f, -4f, -2f, 0.6f, ExecCamEase.Snap),
                    Return(Post, 0.45f, 1f)),
                V("T3_HighBehind",
                    Orbit("high_behind", Pre, 0f, 0.6f, 1.3f, P(8, 24, 3.0f, 42, 0, 0.3f, 0.6f), P(12, 20, 2.6f, 38, 0, 0.35f, 0.62f), ExecCamEase.EaseOut),
                    Orbit("glide_side", Pre, 0.6f, 1f, 1.3f, P(70, 4, 2.4f, 34, 0, 0.5f, 0.6f), P(74, 3, 2.2f, 33, -3, 0.5f, 0.6f), ExecCamEase.EaseOut, ExecCamEntry.Blend, 0.12f),
                    Push("push_blow", Post, 0f, 0.45f, 0.78f, -3f, 0f, 0.6f, ExecCamEase.Snap),
                    Return(Post, 0.45f, 1f))),
        };
    }
}
