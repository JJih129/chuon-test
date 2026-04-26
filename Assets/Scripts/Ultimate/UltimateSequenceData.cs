using System;
using UnityEngine;

[CreateAssetMenu(fileName = "UltimateSequenceData", menuName = "ChuOn/Ultimate/Sequence Data")]
public sealed class UltimateSequenceData : ScriptableObject
{
    [Serializable]
    public sealed class ActivationSettings
    {
        [Header("발동 조건")]
        [Min(0.5f)] public float maxDistance = 10f;
        [Range(0f, 180f)] public float maxAngle = 85f;
        public bool requireTarget = true;
        public bool requireLockOnTarget = false;

        [Header("진입 정책")]
        public bool blockInputDuringSequence = true;
        public bool grantInvulnerability = true;
        public bool freezeGameplayTime = true;
        public bool alignPlayerOnStart = true;
        public bool keepTargetRootedDuringSequence = true;
        public bool abortOnTargetLost = true;
        public bool allowGracefulFinishIfTargetDies = true;
    }

    [Serializable]
    public sealed class TimingSettings
    {
        [Header("페이즈 시간")]
        [Min(0f)] public float preCastDuration = 0.1f;
        [Min(0.05f)] public float introPoseDuration = 0.72f;
        [Min(0.05f)] public float dashDuration = 0.18f;
        [Range(0f, 1f)] public float dashImpactNormalizedTime = 0.72f;
        [Min(0.01f)] public float defaultMultiSlashInterval = 0.12f;
        [Min(0f)] public float crackHoldDuration = 0.2f;
        [Min(0f)] public float finalExplosionHoldDuration = 0.32f;
        [Min(0.05f)] public float walkoutDuration = 0.95f;
        [Min(0f)] public float recoverDuration = 0.18f;
    }

    [Serializable]
    public sealed class MovementSettings
    {
        [Header("정렬 / 위치")]
        [Min(0.4f)] public float introDistance = 2.2f;
        public float introSideOffset = -0.15f;
        [Min(0.4f)] public float dashEndDistance = 1.15f;
        public float dashSideOffset = 0f;
        [Range(0.5f, 4f)] public float dashTravelBias = 2.1f;
        [Min(0.5f)] public float slashOrbitDistance = 1.45f;
        [Min(0.5f)] public float walkoutDistance = 2.8f;
        public float walkoutSideOffset = -0.85f;
        public bool faceAwayOnWalkout = true;
    }

    [Serializable]
    public sealed class DamageSettings
    {
        [Header("데미지")]
        [Min(0)] public int dashSlashDamage = 220;
        [Min(0)] public int multiSlashBaseDamage = 90;
        [Min(0)] public int finalExplosionDamage = 480;
        [Min(0)] public int executionBonusDamage = 180;
        [Range(0f, 1f)] public float executionHealthThresholdNormalized = 0.2f;
    }

    [Serializable]
    public sealed class TimeFxSettings
    {
        [Header("슬로우 / 히트스톱")]
        public bool useIntroSlow = true;
        [Range(0.01f, 1f)] public float introTimeScale = 0.12f;
        [Min(0f)] public float introSlowDuration = 0.04f;
        public bool useDashImpactSlow = true;
        [Range(0.01f, 1f)] public float dashImpactTimeScale = 0.08f;
        [Min(0f)] public float dashImpactSlowDuration = 0.03f;
        public bool useFinalExplosionSlow = true;
        [Range(0.01f, 1f)] public float finalExplosionTimeScale = 0.06f;
        [Min(0f)] public float finalExplosionSlowDuration = 0.05f;
    }

    [Serializable]
    public sealed class CameraShotSettings
    {
        [Header("카메라 배치")]
        public Vector3 cameraLocalOffset = new Vector3(0.85f, 2.65f, -4.35f);
        public Vector3 playerAnchorLocalOffset = new Vector3(0f, 0.85f, 0f);
        [Range(0f, 1f)] public float lookAtBlend = 0.7f;
        public float lookHeightOffset = 0.12f;
        [Range(0f, 1f)] public float targetBottomToCenterRatio = 0.8f;
        public float targetVerticalOffset = 0f;
        [Min(0.01f)] public float positionSmoothing = 16f;
        [Min(0.01f)] public float rotationSmoothing = 18f;
        [Min(10f)] public float fov = 45f;
        public bool snapOnEnter = true;
        [Header("공전 연출")]
        public bool orbitAroundTarget;
        public bool orbitClockwise = true;
        [Min(0f)] public float orbitDegreesPerSecond = 42f;
        [Range(0f, 1f)] public float orbitFocusBias = 0.82f;
        [Header("Shot Origin")]
        public bool anchorCameraOnTarget;
        [Header("고정 프레이밍")]
        public bool holdStaticFraming;
    }

    [Serializable]
    public sealed class VfxSettings
    {
        [Header("VFX 프리팹")]
        public GameObject introPoseVfxPrefab;
        public GameObject dashSlashVfxPrefab;
        public GameObject lightBeamVfxPrefab;
        public GameObject crackWorldVfxPrefab;
        public GameObject crackScreenVfxPrefab;
        public GameObject explosionVfxPrefab;
        public GameObject shardVfxPrefab;
        public GameObject walkoutVfxPrefab;

        [Header("표현 옵션")]
        public bool useScreenSpaceCrack = true;
        public bool useAfterImageStyleSlash = true;
        [Header("Slash Layering")]
        [Min(0)] public int supportSlashCount = 2;
        [Min(0)] public int lightBeamCount = 4;
        [Range(0f, 60f)] public float supportYawSpread = 14f;
        [Range(0f, 45f)] public float supportPitchSpread = 7f;
        [Range(0.1f, 3f)] public float supportLengthScale = 1.12f;
        [Range(0.1f, 3f)] public float supportThicknessScale = 0.72f;
        [Range(0f, 1f)] public float supportAlpha = 0.55f;
        [Range(0f, 90f)] public float beamYawSpread = 34f;
        [Range(0f, 45f)] public float beamPitchSpread = 12f;
        [Range(0.1f, 4f)] public float beamLengthScale = 1.65f;
        [Range(0.1f, 2f)] public float beamThicknessScale = 0.26f;
        [Range(0f, 1f)] public float beamAlpha = 0.34f;
        [Range(0.1f, 6f)] public float beamSpawnRadiusMin = 1.15f;
        [Range(0.2f, 8f)] public float beamSpawnRadiusMax = 2.35f;
        [Range(-1f, 4f)] public float beamSpawnHeightMin = 0.45f;
        [Range(-1f, 5f)] public float beamSpawnHeightMax = 1.85f;
        [Range(-1f, 3f)] public float beamFocusHeightOffset = 0.95f;
        [Min(0)] public int crackBeamCount = 6;
        [Min(0)] public int finalConvergenceBeamCount = 10;
        [Range(0f, 3f)] public float finalBurstIntensityMultiplier = 1.45f;
        [Header("화면 효과")]
        public bool useDashScreenPulse = true;
        public bool useMultiSlashScreenPulse = false;
        public bool useAccentSlashFlash = false;
        [Range(0f, 1f)] public float multiSlashPulseStrength = 0.18f;
        [Min(0.01f)] public float multiSlashPulseDuration = 0.06f;
        [Range(0f, 1f)] public float accentSlashFlashStrength = 0.32f;
        [Min(0.01f)] public float accentSlashFlashDuration = 0.06f;
        [Header("Screen Crack Overlay")]
        public bool useSlashScreenCrackOverlay = true;
        public bool useBurstScreenCrackOverlay = true;
        [Min(0)] public int slashCrackBranchCount = 2;
        [Min(0)] public int burstCrackLineCount = 8;
        [Min(0.05f)] public float screenCrackLifetime = 0.24f;
        [Min(0.01f)] public float screenCrackWidth = 0.08f;
        [Min(0.05f)] public float screenCrackLength = 0.9f;
        [Range(0f, 90f)] public float screenCrackBranchAngle = 24f;
        [Range(0.1f, 1f)] public float screenCrackBranchScale = 0.62f;
        [Range(0f, 1f)] public float screenCrackAlpha = 0.78f;
        [Min(0)] public int shardSpawnCount = 6;
        [Min(0.05f)] public float introPoseLifetime = 0.7f;
        [Min(0.05f)] public float dashSlashLifetime = 0.7f;
        [Min(0.05f)] public float crackLifetime = 0.9f;
        [Min(0.05f)] public float explosionLifetime = 1.15f;
        [Min(0.05f)] public float walkoutLifetime = 0.8f;
    }

    [Serializable]
    public sealed class ImpactSettings
    {
        [Header("카메라 임팩트")]
        [Min(0f)] public float dashShakeAmplitude = 0.11f;
        [Min(0f)] public float dashShakeDuration = 0.08f;
        [Min(0f)] public float slashShakeAmplitude = 0.035f;
        [Min(0f)] public float slashShakeDuration = 0.045f;
        [Min(0f)] public float crackShakeAmplitude = 0.06f;
        [Min(0f)] public float crackShakeDuration = 0.08f;
        [Min(0f)] public float finalShakeAmplitude = 0.18f;
        [Min(0f)] public float finalShakeDuration = 0.16f;
    }

    [Serializable]
    public sealed class CinematicAnimationSettings
    {
        [Header("인트로 / 워크아웃 클립")]
        public AnimationClip introPoseClip;
        public AnimationClip dashSlashClip;
        public AnimationClip walkoutClip;
        [Range(0f, 1f)] public float introPoseClipStartNormalized = 0.8f;
        [Range(0f, 1f)] public float introPoseClipEndNormalized = 0.995f;
        [Range(0f, 1f)] public float dashSlashClipStartNormalized = 0.08f;
        [Range(0f, 1f)] public float dashSlashClipEndNormalized = 0.68f;

        [Header("인트로 검 클로즈업")]
        public bool useSwordCloseupDuringIntro = true;
        public Vector3 introSwordCameraOffset = new Vector3(0.86f, 0.56f, 0.22f);
        public Vector3 introSwordLookLocalOffset = new Vector3(-0.02f, 0.08f, 0.02f);
        public CameraShotSettings introSwordCloseupCamera = new CameraShotSettings
        {
            cameraLocalOffset = new Vector3(0.42f, 0.58f, -1.08f),
            playerAnchorLocalOffset = new Vector3(0.08f, 0.02f, 0f),
            lookAtBlend = 0.12f,
            lookHeightOffset = 0.02f,
            targetBottomToCenterRatio = 0.78f,
            targetVerticalOffset = 0f,
            positionSmoothing = 22f,
            rotationSmoothing = 24f,
            fov = 38f,
            snapOnEnter = true,
            holdStaticFraming = true
        };
    }

    [Serializable]
    public sealed class SlashStepData
    {
        [Min(0f)] public float delay = 0.08f;
        [Range(0.1f, 4f)] public float damageMultiplier = 1f;
        public float angle = 0f;
        [Min(0.2f)] public float distance = 1.35f;
        public float sideOffset = 0f;
        public float heightOffset = 0f;
        public bool accentCrackPulse;
    }

    [Header("전체 옵션")]
    [SerializeField] private ActivationSettings activation = new ActivationSettings();
    [SerializeField] private TimingSettings timings = new TimingSettings();
    [SerializeField] private MovementSettings movement = new MovementSettings();
    [SerializeField] private DamageSettings damage = new DamageSettings();
    [SerializeField] private TimeFxSettings timeFx = new TimeFxSettings();
    [SerializeField] private ImpactSettings impact = new ImpactSettings();
    [SerializeField] private CinematicAnimationSettings cinematicAnimation = new CinematicAnimationSettings();
    [SerializeField] private VfxSettings vfx = new VfxSettings();

    [Header("3단 카메라 운용")]
    [SerializeField] private bool useThreeStageCameraSplit = true;
    [SerializeField] private CameraShotSettings introStageCamera = new CameraShotSettings
    {
        cameraLocalOffset = new Vector3(0.95f, 2.25f, -3.1f),
        playerAnchorLocalOffset = new Vector3(0f, 0.88f, 0f),
        lookAtBlend = 0.68f,
        lookHeightOffset = 0.16f,
        targetBottomToCenterRatio = 0.78f,
        targetVerticalOffset = 0f,
        positionSmoothing = 18f,
        rotationSmoothing = 20f,
        fov = 42f,
        snapOnEnter = true
    };
    [SerializeField] private CameraShotSettings assaultStageCamera = new CameraShotSettings
    {
        cameraLocalOffset = new Vector3(0.75f, 2.45f, -3.75f),
        playerAnchorLocalOffset = new Vector3(0f, 0.78f, 0f),
        lookAtBlend = 0.72f,
        lookHeightOffset = 0.1f,
        targetBottomToCenterRatio = 0.8f,
        targetVerticalOffset = 0f,
        positionSmoothing = 16f,
        rotationSmoothing = 18f,
        fov = 44f,
        snapOnEnter = true,
        holdStaticFraming = true
    };
    [SerializeField] private CameraShotSettings finishStageCamera = new CameraShotSettings
    {
        cameraLocalOffset = new Vector3(1.05f, 2.05f, -3.25f),
        playerAnchorLocalOffset = new Vector3(0f, 0.82f, 0f),
        lookAtBlend = 0.76f,
        lookHeightOffset = 0.2f,
        targetBottomToCenterRatio = 0.82f,
        targetVerticalOffset = 0.04f,
        positionSmoothing = 14f,
        rotationSmoothing = 16f,
        fov = 40f,
        snapOnEnter = true
    };

    [Header("레거시 개별 카메라 샷")]
    [SerializeField] private CameraShotSettings preCastCamera = new CameraShotSettings();
    [SerializeField] private CameraShotSettings introCamera = new CameraShotSettings();
    [SerializeField] private CameraShotSettings dashCamera = new CameraShotSettings();
    [SerializeField] private CameraShotSettings multiSlashCamera = new CameraShotSettings();
    [SerializeField] private CameraShotSettings crackCamera = new CameraShotSettings();
    [SerializeField] private CameraShotSettings finalExplosionCamera = new CameraShotSettings();
    [SerializeField] private CameraShotSettings walkoutCamera = new CameraShotSettings();

    [Header("애니메이터")]
    [SerializeField] private string introTrigger = "Ultimate_Start";
    [SerializeField] private string dashTrigger = "Ultimate_Dash";
    [SerializeField] private string multiSlashTrigger = "Ultimate_Slash";
    [SerializeField] private string finalExplosionTrigger = "Ultimate_Finish";
    [SerializeField] private string walkoutTrigger = "Ultimate_Walk";

    [Header("애니메이터 폴백 상태")]
    [SerializeField] private string introFallbackState = string.Empty;
    [SerializeField] private string dashFallbackState = "Base Layer.H3_D";
    [SerializeField] private string multiSlashFallbackState = "Base Layer.H4_C";
    [SerializeField] private string finalExplosionFallbackState = "Base Layer.H5_B";
    [SerializeField] private string walkoutFallbackState = "Base Layer.Locomotion";
    [SerializeField] [Min(0.01f)] private float animatorCrossFadeDuration = 0.08f;

    [Header("난무 단계")]
    [SerializeField] private SlashStepData[] slashSteps =
    {
        new SlashStepData { delay = 0.11f, damageMultiplier = 1f, angle = -35f, distance = 1.4f, sideOffset = -0.2f },
        new SlashStepData { delay = 0.12f, damageMultiplier = 1f, angle = 25f, distance = 1.3f, sideOffset = 0.24f },
        new SlashStepData { delay = 0.13f, damageMultiplier = 1.15f, angle = -120f, distance = 1.6f, sideOffset = -0.45f, accentCrackPulse = true },
        new SlashStepData { delay = 0.14f, damageMultiplier = 1.25f, angle = 115f, distance = 1.55f, sideOffset = 0.45f },
        new SlashStepData { delay = 0.15f, damageMultiplier = 1.35f, angle = 180f, distance = 1.2f, sideOffset = 0f, accentCrackPulse = true }
    };

    [Header("디버그")]
    [SerializeField] private bool debugLog;

    public ActivationSettings Activation => activation;
    public TimingSettings Timings => timings;
    public MovementSettings Movement => movement;
    public DamageSettings Damage => damage;
    public TimeFxSettings TimeFx => timeFx;
    public ImpactSettings Impact => impact;
    public CinematicAnimationSettings CinematicAnimation => cinematicAnimation;
    public VfxSettings Vfx => vfx;
    public bool UseThreeStageCameraSplit => useThreeStageCameraSplit;
    public CameraShotSettings IntroStageCamera => introStageCamera != null ? introStageCamera : introCamera;
    public CameraShotSettings AssaultStageCamera => assaultStageCamera != null ? assaultStageCamera : multiSlashCamera;
    public CameraShotSettings FinishStageCamera => finishStageCamera != null ? finishStageCamera : finalExplosionCamera;
    public CameraShotSettings PreCastCamera => preCastCamera;
    public CameraShotSettings IntroCamera => introCamera;
    public CameraShotSettings DashCamera => dashCamera;
    public CameraShotSettings MultiSlashCamera => multiSlashCamera;
    public CameraShotSettings CrackCamera => crackCamera;
    public CameraShotSettings FinalExplosionCamera => finalExplosionCamera;
    public CameraShotSettings WalkoutCamera => walkoutCamera;
    public string IntroTrigger => introTrigger;
    public string DashTrigger => dashTrigger;
    public string MultiSlashTrigger => multiSlashTrigger;
    public string FinalExplosionTrigger => finalExplosionTrigger;
    public string WalkoutTrigger => walkoutTrigger;
    public string IntroFallbackState => introFallbackState;
    public string DashFallbackState => dashFallbackState;
    public string MultiSlashFallbackState => multiSlashFallbackState;
    public string FinalExplosionFallbackState => finalExplosionFallbackState;
    public string WalkoutFallbackState => walkoutFallbackState;
    public float AnimatorCrossFadeDuration => animatorCrossFadeDuration;
    public bool DebugLog => debugLog;
    public int SlashCount => slashSteps != null && slashSteps.Length > 0 ? slashSteps.Length : 0;

    public SlashStepData GetSlashStep(int index)
    {
        if (slashSteps == null || slashSteps.Length == 0)
            return new SlashStepData();

        index = Mathf.Clamp(index, 0, slashSteps.Length - 1);
        return slashSteps[index] ?? new SlashStepData();
    }

    public UltimateCameraStage ResolveCameraStage(UltimateSequencePhase phase)
    {
        switch (phase)
        {
            case UltimateSequencePhase.PreCast:
            case UltimateSequencePhase.IntroPose:
                return UltimateCameraStage.Intro;

            case UltimateSequencePhase.DashSlash:
            case UltimateSequencePhase.MultiSlash:
            case UltimateSequencePhase.CrackBurst:
                return UltimateCameraStage.Assault;

            case UltimateSequencePhase.FinalExplosion:
            case UltimateSequencePhase.Walkout:
            case UltimateSequencePhase.Recover:
                return UltimateCameraStage.Finish;

            default:
                return UltimateCameraStage.None;
        }
    }

    public CameraShotSettings ResolveCameraShot(UltimateSequencePhase phase)
    {
        if (useThreeStageCameraSplit)
        {
            switch (phase)
            {
                case UltimateSequencePhase.PreCast:
                case UltimateSequencePhase.IntroPose:
                    return IntroStageCamera;

                case UltimateSequencePhase.DashSlash:
                case UltimateSequencePhase.MultiSlash:
                case UltimateSequencePhase.CrackBurst:
                    return AssaultStageCamera;

                case UltimateSequencePhase.FinalExplosion:
                case UltimateSequencePhase.Walkout:
                case UltimateSequencePhase.Recover:
                    return WalkoutCamera != null ? WalkoutCamera : FinishStageCamera;
            }
        }

        return ResolveLegacyCameraShot(phase);
    }

    public CameraShotSettings ResolveLegacyCameraShot(UltimateSequencePhase phase)
    {
        switch (phase)
        {
            case UltimateSequencePhase.PreCast:
                return preCastCamera;
            case UltimateSequencePhase.IntroPose:
                return introCamera;
            case UltimateSequencePhase.DashSlash:
                return dashCamera;
            case UltimateSequencePhase.MultiSlash:
                return multiSlashCamera;
            case UltimateSequencePhase.CrackBurst:
                return crackCamera;
            case UltimateSequencePhase.Walkout:
            case UltimateSequencePhase.Recover:
            case UltimateSequencePhase.FinalExplosion:
                return walkoutCamera;
            default:
                return introCamera;
        }
    }

    public float EstimateTotalDuration()
    {
        float slashDuration = 0f;
        if (slashSteps != null)
        {
            for (int i = 0; i < slashSteps.Length; i++)
                slashDuration += Mathf.Max(0.01f, GetSlashStep(i).delay);
        }

        if (slashDuration <= 0f)
            slashDuration = Mathf.Max(0.01f, timings.defaultMultiSlashInterval);

        return Mathf.Max(0f, timings.preCastDuration)
            + Mathf.Max(0f, timings.introPoseDuration)
            + Mathf.Max(0.01f, timings.dashDuration)
            + slashDuration
            + Mathf.Max(0f, timings.crackHoldDuration)
            + Mathf.Max(0f, timings.finalExplosionHoldDuration)
            + Mathf.Max(0f, timings.walkoutDuration)
            + Mathf.Max(0f, timings.recoverDuration);
    }

    public void ApplyReferenceCinematicStyle()
    {
        timings.preCastDuration = 0.08f;
        timings.introPoseDuration = 0.78f;
        timings.dashDuration = 0.34f;
        timings.dashImpactNormalizedTime = 0.42f;
        timings.defaultMultiSlashInterval = 0.17f;
        timings.crackHoldDuration = 0.18f;
        timings.finalExplosionHoldDuration = 0.36f;
        timings.walkoutDuration = 0.98f;
        timings.recoverDuration = 0.16f;

        movement.introDistance = 2.05f;
        movement.introSideOffset = -0.08f;
        movement.dashEndDistance = 1.25f;
        movement.dashSideOffset = 0f;
        movement.dashTravelBias = 1.65f;
        movement.slashOrbitDistance = 1.65f;
        movement.walkoutDistance = 2.35f;
        movement.walkoutSideOffset = -0.65f;

        ConfigureShot(
            introStageCamera,
            new Vector3(0.62f, 1.82f, -2.35f),
            new Vector3(0f, 0.9f, 0f),
            0.62f,
            0.1f,
            0.78f,
            0f,
            10f,
            12f,
            37f,
            true,
            false,
            true);

        ConfigureShot(
            assaultStageCamera,
            new Vector3(0.72f, 2.05f, -3.05f),
            new Vector3(0f, 0.84f, 0f),
            0.82f,
            0.08f,
            0.82f,
            0.03f,
            7.5f,
            9.5f,
            40f,
            false,
            false,
            true);
        assaultStageCamera.orbitAroundTarget = true;
        assaultStageCamera.orbitClockwise = true;
        assaultStageCamera.orbitDegreesPerSecond = 34f;
        assaultStageCamera.orbitFocusBias = 0.9f;

        ConfigureShot(
            finishStageCamera,
            new Vector3(1.2f, 2.2f, -4.1f),
            new Vector3(0f, 0.72f, 0f),
            0.9f,
            0.18f,
            0.82f,
            0.08f,
            9f,
            11f,
            43f,
            true,
            false,
            true);

        ConfigureShot(
            finalExplosionCamera,
            new Vector3(2.35f, 2.55f, 4.85f),
            new Vector3(0f, 0.42f, 0f),
            0.98f,
            0.32f,
            0.84f,
            0.16f,
            10f,
            12f,
            52f,
            true,
            false,
            true);
        finalExplosionCamera.anchorCameraOnTarget = true;

        ConfigureShot(
            walkoutCamera,
            new Vector3(0.95f, 1.85f, -3.25f),
            new Vector3(0f, 0.84f, 0f),
            0.76f,
            0.12f,
            0.82f,
            0.04f,
            9f,
            11f,
            40f,
            true,
            false,
            true);

        slashSteps = new[]
        {
            new SlashStepData { delay = 0.17f, damageMultiplier = 1f, angle = -16f, distance = 1.65f, sideOffset = -0.1f },
            new SlashStepData { delay = 0.16f, damageMultiplier = 1.08f, angle = 10f, distance = 1.58f, sideOffset = 0.08f },
            new SlashStepData { delay = 0.17f, damageMultiplier = 1.16f, angle = -8f, distance = 1.72f, sideOffset = -0.05f },
            new SlashStepData { delay = 0.21f, damageMultiplier = 1.32f, angle = 18f, distance = 1.55f, sideOffset = 0.12f, accentCrackPulse = true }
        };
    }

    static void ConfigureShot(
        CameraShotSettings shot,
        Vector3 cameraOffset,
        Vector3 playerAnchorOffset,
        float lookAtBlend,
        float lookHeightOffset,
        float targetBottomToCenterRatio,
        float targetVerticalOffset,
        float positionSmoothing,
        float rotationSmoothing,
        float fov,
        bool snapOnEnter,
        bool holdStaticFraming,
        bool clearOrbit)
    {
        if (shot == null)
            return;

        shot.cameraLocalOffset = cameraOffset;
        shot.playerAnchorLocalOffset = playerAnchorOffset;
        shot.lookAtBlend = Mathf.Clamp01(lookAtBlend);
        shot.lookHeightOffset = lookHeightOffset;
        shot.targetBottomToCenterRatio = Mathf.Clamp01(targetBottomToCenterRatio);
        shot.targetVerticalOffset = targetVerticalOffset;
        shot.positionSmoothing = Mathf.Max(0.01f, positionSmoothing);
        shot.rotationSmoothing = Mathf.Max(0.01f, rotationSmoothing);
        shot.fov = Mathf.Max(10f, fov);
        shot.snapOnEnter = snapOnEnter;
        shot.holdStaticFraming = holdStaticFraming;
        shot.anchorCameraOnTarget = false;
        if (clearOrbit)
            shot.orbitAroundTarget = false;
    }

    public static UltimateSequenceData CreateRuntimeDefaultInstance()
    {
        UltimateSequenceData runtime = CreateInstance<UltimateSequenceData>();
        runtime.hideFlags = HideFlags.DontSave;
        return runtime;
    }
}
