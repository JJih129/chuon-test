using UnityEngine;

/// <summary>
/// 소울라이크 게임플레이 카메라 튜닝값 (Docs/CameraDesign_Soulslike.md).
/// SoulsCameraRig 이 이 프로필을 들고 있는 씬(Level_Facility, Tutorial)에서만 새 카메라/락온 규칙이 켜진다.
/// 피치 부호는 Unity 기준: + = 카메라가 위에서 내려다봄, - = 아래에서 올려다봄.
/// </summary>
[CreateAssetMenu(menuName = "ChuOn/Camera/Souls Camera Profile", fileName = "SoulsCameraProfile")]
public sealed class SoulsCameraProfile : ScriptableObject
{
    [Header("자유 시점")]
    [Tooltip("캐릭터 발 기준 피벗 높이 (m).")]
    public float pivotHeight = 1.55f;
    [Tooltip("기본 카메라 거리 (m).")]
    public float distance = 3.8f;
    [Tooltip("휠 줌 하한 (m). 충돌 당김은 이 값보다 더 가까워질 수 있다.")]
    public float minDistance = 2.6f;
    [Tooltip("휠 줌 상한 (m).")]
    public float maxDistance = 5.0f;
    [Tooltip("마우스 휠 줌 허용.")]
    public bool allowWheelZoom = false;
    [Tooltip("자유 시점 우측 숄더 오프셋 (m).")]
    public float freeShoulderOffset = 0.25f;
    [Tooltip("옵션 FOV 저장값이 없을 때 쓰는 수직 FOV.")]
    public float fieldOfView = 50f;
    [Tooltip("최소 피치 (아래에서 올려다봄).")]
    public float minPitch = -35f;
    [Tooltip("최대 피치 (위에서 내려다봄).")]
    public float maxPitch = 55f;
    [Tooltip("리셋/복귀 시 기본 피치.")]
    public float defaultPitch = 10f;
    [Tooltip("피벗을 화면 중앙보다 아래(하단 1/3 쪽)에 두기 위한 시선 위쪽 기울임 (deg).")]
    public float freeFramingTilt = 7f;

    [Header("입력")]
    [Tooltip("마우스 원시 입력 감도 (deg / pixel), 옵션 배율이 곱해진다.")]
    public float mouseDegreesPerPixel = 0.06f;
    [Tooltip("패드 최대 요 속도 (deg/s).")]
    public float padYawSpeed = 300f;
    [Tooltip("패드 최대 피치 속도 (deg/s).")]
    public float padPitchSpeed = 170f;
    [Tooltip("패드 스틱 응답 곡선 지수 (1 = 선형).")]
    public float padResponseExponent = 2f;
    [Tooltip("스틱을 끝까지 밀었을 때 가속이 다 차는 시간 (s).")]
    public float padAccelRampTime = 0.25f;
    [Tooltip("가속 램프 시작 배율.")]
    [Range(0.1f, 1f)] public float padAccelStartMultiplier = 0.55f;
    [Tooltip("패드 스틱 데드존.")]
    [Range(0f, 0.5f)] public float padDeadZone = 0.15f;

    [Header("추종 감쇠")]
    [Tooltip("피벗 수평 추종 SmoothDamp 시간 (s).")]
    public float followDampingHorizontal = 0.08f;
    [Tooltip("피벗 수직 추종 SmoothDamp 시간 (s). 회피/피격 출렁임 억제.")]
    public float followDampingVertical = 0.22f;

    [Header("자동 재정렬 / 리셋")]
    [Tooltip("자동 재정렬(옵션 켜짐 시): 이동 중 카메라 입력이 이 시간 없으면 수평만 정렬.")]
    public float autoRecenterDelay = 2.0f;
    [Tooltip("자동 재정렬 최대 속도 (deg/s).")]
    public float autoRecenterSpeed = 70f;
    [Tooltip("락온 키 탭(타깃 없음) 리셋 시간 (s).")]
    public float recenterDuration = 0.25f;

    [Header("락온 타깃 선택")]
    public float lockSearchRange = 20f;
    [Tooltip("카메라 정면 기준 시야 원뿔 전체 각도 (deg).")]
    public float lockSearchCone = 70f;
    [Tooltip("보스 우선순위: 화면 중앙 거리(뷰포트 단위)에서 빼는 값.")]
    public float bossPriorityBonus = 0.25f;
    [Tooltip("가림 판정 레이어 (환경).")]
    public LayerMask lineOfSightMask = (1 << 0) | (1 << 13);
    [Tooltip("락온 중 락온 키 탭 = 해제 (소울류). 끄면 기존처럼 탭 = 다음 타깃.")]
    public bool tapUnlocks = true;

    [Header("락온 전환 / 해제")]
    public float switchCooldown = 0.25f;
    [Tooltip("마우스 전환: 이 시간 창 안에 누적 이동량이 임계값을 넘으면 전환.")]
    public float mouseSwitchWindow = 0.15f;
    [Tooltip("마우스 전환 임계 이동량 (1080p 기준 px).")]
    public float mouseSwitchThreshold = 90f;
    [Tooltip("오른쪽 스틱 튕김 임계값.")]
    public float stickFlickThreshold = 0.8f;
    [Tooltip("스틱이 이 값 아래로 돌아와야 다음 튕김을 받는다.")]
    public float stickResetThreshold = 0.35f;
    public float lockBreakDistance = 25f;
    [Tooltip("가림이 이 시간 이상 이어지면 해제 (s).")]
    public float lockLineOfSightGrace = 1.5f;

    [Header("락온 프레이밍")]
    [Tooltip("요 추종 SmoothDamp 시간 (s).")]
    public float lockYawDamping = 0.18f;
    [Tooltip("요 최대 각속도 (deg/s).")]
    public float lockMaxYawSpeed = 240f;
    [Tooltip("시선점 = Lerp(플레이어 가슴, 타깃 가슴, 이 값).")]
    [Range(0f, 1f)] public float lockLookTargetBias = 0.65f;
    [Tooltip("락온 중 오빗 피치 (deg).")]
    public float lockPitch = 10f;
    [Tooltip("락온 피치 추종 시간 (s).")]
    public float lockPitchDamping = 0.25f;
    [Tooltip("락온 기본 거리 (m). 0 이면 자유 시점 거리 사용.")]
    public float lockDistance = 3.8f;
    [Tooltip("큰 타깃 판정 키 (m). BossController 는 항상 큰 타깃.")]
    public float bigTargetHeight = 2.5f;
    public float bigTargetExtraDistance = 0.8f;
    public float bigTargetExtraPivot = 0.3f;
    [Tooltip("큰 타깃과 이 거리 미만이면 시선을 위로 든다.")]
    public float bigTargetCloseDistance = 3f;
    public float bigTargetClosePitchUp = 8f;
    [Tooltip("근접 데드존 거리 (m): 이 안에서는 요 추종 속도를 줄인다.")]
    public float closeDeadZone = 1.5f;
    [Range(0.1f, 1f)] public float closeDeadZoneFollow = 0.5f;
    [Tooltip("숄더 오프셋 (가까울 때).")]
    public float lockShoulderClose = 0.6f;
    [Tooltip("숄더 오프셋 (멀 때).")]
    public float lockShoulderFar = 0.45f;
    public float lockShoulderCloseDistance = 2f;
    public float lockShoulderFarDistance = 8f;
    [Tooltip("타깃 가슴 높이 = 렌더러 바운드 바닥 + 높이 * 이 비율.")]
    [Range(0.3f, 0.95f)] public float chestHeightRatio = 0.72f;
    [Tooltip("플레이어 가슴 높이 (m).")]
    public float playerChestHeight = 1.3f;
    [Tooltip("자유 ↔ 락온 프레이밍 블렌드 시간 (s).")]
    public float lockTransitionTime = 0.3f;

    [Header("충돌 / 가림")]
    public float collisionRadius = 0.25f;
    [Tooltip("벽/바닥에 당겨질 때 시간 (s).")]
    public float collisionPullInTime = 0.04f;
    [Tooltip("당김이 풀릴 때 시간 (s).")]
    public float collisionReleaseTime = 0.35f;
    public LayerMask collisionMask = (1 << 0) | (1 << 13);
    [Tooltip("콜라이더 바운드의 두 번째로 큰 변이 이 값 미만이면 얇은 소품/기둥 → 충돌 무시 (페이더가 투명 처리).")]
    public float thinPropMaxSize = 1.0f;
    [Tooltip("피벗에서 카메라까지 최소 거리 (m).")]
    public float minCollisionDistance = 0.35f;
    [Tooltip("카메라-플레이어(가슴) 거리가 이 값 이하면 플레이어 페이드 시작.")]
    public float playerFadeStartDistance = 0.8f;
    [Tooltip("이 거리에서 페이드 최대.")]
    public float playerFadeEndDistance = 0.4f;
    [Range(0f, 1f)] public float playerFadeMinAlpha = 0.2f;
    [Tooltip("벽에 당겨진 거리(피벗→카메라)가 이 값 미만이면 어깨 너머 시점으로 올리기 시작 (m).")]
    public float pulledInStartDistance = 1.7f;
    [Tooltip("이 거리에서 어깨 너머 보정 최대 (m).")]
    public float pulledInFullDistance = 0.7f;
    [Tooltip("당겨졌을 때 위로 올리는 양 (m).")]
    public float pulledInLift = 0.32f;
    [Tooltip("당겨졌을 때 오른쪽 어깨로 빼는 양 (m).")]
    public float pulledInSide = 0.4f;
    [Tooltip("당겨졌을 때 시선을 아래로 숙이는 각 (deg).")]
    public float pulledInPitch = 6f;
    [Tooltip("카메라가 캐릭터(보스/적) 몸통 캡슐에 이 여유 이내로 들어가면 그 캐릭터 렌더링을 숨긴다 (m).")]
    public float characterHideMargin = 0.15f;

    [Header("좁은 공간 (반복 16)")]
    [Tooltip("좌우 여유 합(카메라 오른쪽/왼쪽 탐침)이 이 값 미만이면 좁은 공간으로 보기 시작 (m).")]
    public float narrowWidthStart = 6.0f;
    [Tooltip("좌우 여유 합이 이 값 이하면 좁음 최대 (m).")]
    public float narrowWidthFull = 3.2f;
    [Tooltip("뒤쪽 충돌 당김 비율(당겨진 거리/원래 거리)이 이 값 미만이면 좁음으로 본다.")]
    [Range(0.2f, 1f)] public float narrowPullRatioStart = 0.8f;
    [Range(0.1f, 1f)] public float narrowPullRatioFull = 0.4f;
    [Tooltip("좁은 공간 자유 시점 거리 (m). 벽에 눌린 카메라 대신 처음부터 가깝게.")]
    public float narrowDistance = 3.0f;
    [Tooltip("좁은 공간 피벗 추가 높이 (m). 벽보다 바닥·캐릭터가 더 보이게.")]
    public float narrowPivotLift = 0.2f;
    [Tooltip("좁은 공간 시선 숙임 추가 (deg).")]
    public float narrowPitchAdd = 4f;
    [Tooltip("오른쪽 어깨 쪽이 막히고 왼쪽이 트였으면 숄더 오프셋을 왼쪽으로 옮긴다.")]
    public bool narrowSideShift = true;
    [Tooltip("벽을 정면으로 보고(시선 앞 벽 < narrowFacingWall) 뒤가 눌린 채 이동 중이면, 카메라 입력이 없을 때 이동 방향(트인 쪽)으로 천천히 정렬.")]
    public bool narrowAutoAlign = true;
    public float narrowFacingWall = 3.2f;
    public float narrowAlignDelay = 0.7f;
    [Tooltip("정렬 최대 각속도 (deg/s). 락온 상한 240°/s 보다 훨씬 낮게.")]
    public float narrowAlignSpeed = 55f;
    [Tooltip("정렬 시작 최소 이동 속도 (m/s).")]
    public float narrowAlignMinSpeed = 1.2f;

    [Header("락온 공중 타깃 (반복 16)")]
    [Tooltip("타깃 몸 바닥이 플레이어 발보다 이 값 이상 높아도 시선은 이만큼만 따라 올린다 (Type-D 도약 시 천장을 비추던 문제, m).")]
    public float lockAirborneLookRise = 0.9f;
    [Tooltip("락온 시선이 위로 올라가는 최대 각 (deg, 수평 기준).")]
    public float lockMaxLookUp = 14f;
    [Tooltip("플레이어 몸 중앙이 화면 아래 가장자리 안쪽 이 비율(반 FOV 기준) 안에 남도록 피치를 제한.")]
    [Range(0.5f, 0.95f)] public float lockPlayerFrameLimit = 0.78f;

    [Header("전투 피드백")]
    [Tooltip("흔들림 회전 성분 배율 (deg / m). 최소로 유지.")]
    public float shakeRotationScale = 1.2f;
    [Tooltip("흔들림 수직 성분 배율 (수평 대비).")]
    [Range(0f, 1f)] public float shakeVerticalScale = 0.5f;
    public float shakeFrequency = 24f;
    [Tooltip("연속 흔들림 누적 상한 (m).")]
    public float maxStackedShake = 0.12f;
    public float parryFovPunch = -3f;
    public float parryFovPunchDuration = 0.12f;
    public float perfectDodgeFov = 4f;
    public float perfectDodgeFovRelease = 0.2f;

    [Header("연출 ↔ 게임플레이")]
    public float cinematicBlendTime = 0.35f;

    [Header("락온 표식")]
    public Color markerColor = new Color(1f, 1f, 1f, 0.95f);
    public Color markerAccentColor = new Color(1f, 0.16f, 0.18f, 1f);
    public float markerSize = 24f;
    public float markerSnapTime = 0.1f;

    [Header("화면 밖 위협 표시")]
    public bool enableOffscreenThreats = true;
    public Color threatParryableColor = new Color(1f, 0.78f, 0.22f, 1f);
    public Color threatDodgeColor = new Color(1f, 0.1f, 0.08f, 1f);
    public Color threatUnblockableColor = new Color(1f, 0.12f, 0.85f, 1f);
    public Color threatHeavyColor = new Color(1f, 0.6f, 0.04f, 1f);
    [Tooltip("화면 가장자리 여백 (px, 1080p 기준).")]
    public float threatEdgeMargin = 70f;
    public float threatMinDisplay = 0.6f;
    public float threatMaxDisplay = 1.4f;
    [Tooltip("이 거리보다 먼 공격자는 표시하지 않는다 (m).")]
    public float threatMaxDistance = 30f;
}
