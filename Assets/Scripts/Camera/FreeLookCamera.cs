using UnityEngine;
using Cinemachine;

/// ==============================
/// ▼ 변수 헤더(한글 설명)
/// player                 : 추적 대상 트랜스폼
/// playerLockOn           : 락온 상태 판별용
/// distance/height        : 비CM 경로에서의 카메라 거리/높이
/// mouseSensitivity       : 마우스 감도
/// minY/maxY              : 피치 각도 제한
/// autoAlignSpeed/delay   : 입력 없을 때 자동 정렬 속도/지연
/// alignBehindOnUnlock    : 락온 해제 시 뒤로 정렬 여부
/// lookAtChestOffset      : 바라볼 오프셋(Y)
/// zoomStep               : 휠 1틱당 FOV 변경량(도) 또는 거리 변경량
/// minFov/maxFov          : FOV 줌 한계(시네머신 경로)
/// minDist/maxDist        : 거리 줌 한계(비CM 경로)
/// invertScroll           : 휠 방향 반전
/// ==============================
public class FreeLookCamera : MonoBehaviour
{
    [Header("Refs")]
    public Transform player;
    public PlayerLockOn playerLockOn;

    [Header("Orbit (Non-CM fallback)")]
    public float distance = 4f;
    public float height = 1.5f;
    public float mouseSensitivity = 3f;
    public float minY = -30f;
    public float maxY = 60f;

    [Header("Auto-Align")]
    public float autoAlignSpeed = 2.5f;
    public float alignDelay = 1.0f;

    [Header("Unlock Transition")]
    public bool alignBehindOnUnlock = true;
    public float lookAtChestOffset = 1.0f;

    [Header("Zoom")]
    public float zoomStep = 5f;      // FOV 또는 거리 변화량
    public float minFov = 30f;
    public float maxFov = 70f;
    public float minDist = 2f;       // 비CM 경로용
    public float maxDist = 10f;
    public bool invertScroll = false;

    // ── 내부 상태 ──────────────────────────────────────────
    float noInputTimer = 0f;
    float yaw = 0f;
    float pitch = 10f;
    bool wasLockOn = false;
    float cachedYaw, cachedPitch;

    // 시네머신 참조(있으면 사용)
    CinemachineFreeLook cmFreeLook;
    CinemachineVirtualCamera cmVCam;           // 비FreeLook VCam 대응
    CinemachineTransposer cmTransposer;

    void Awake()
    {
        cmFreeLook = GetComponent<CinemachineFreeLook>();
        cmVCam = GetComponent<CinemachineVirtualCamera>();
        if (cmVCam) cmTransposer = cmVCam.GetCinemachineComponent<CinemachineTransposer>();
    }

    void Start()
    {
        if (!player) return;
        var ang = transform.eulerAngles;
        yaw = ang.y;
        pitch = Mathf.Clamp(ang.x, minY, maxY);

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void Update()
    {
        // 커서 토글
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            bool locked = Cursor.lockState == CursorLockMode.Locked;
            Cursor.lockState = locked ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = !locked;
        }

        // 휠 입력 통합
        float wheel = 0f;
#if ENABLE_INPUT_SYSTEM
        var m = UnityEngine.InputSystem.Mouse.current;
        if (m != null) wheel = m.scroll.ReadValue().y / 120f;
#endif
        if (Mathf.Approximately(wheel, 0f)) wheel = Input.mouseScrollDelta.y;
        if (Mathf.Abs(wheel) < 0.01f) return;
        if (invertScroll) wheel = -wheel;

        // 락온 중에도 프리룩 오브젝트가 활성일 수 있으므로,
        // "현재 이 카메라가 화면에 반영되는 경로"만 조절:
        bool isLockOn = playerLockOn != null && playerLockOn.IsLockOn;

        // 1) 시네머신 FreeLook이면 FOV로 줌
        if (!isLockOn && cmFreeLook)
        {
            var lens = cmFreeLook.m_Lens;
            lens.FieldOfView = Mathf.Clamp(lens.FieldOfView - wheel * zoomStep, minFov, maxFov);
            cmFreeLook.m_Lens = lens;
            return;
        }

        // 2) 시네머신 일반 VCam이면 Transposer Z로 줌
        if (!isLockOn && cmTransposer)
        {
            var off = cmTransposer.m_FollowOffset;
            off.z = Mathf.Clamp(off.z - wheel * (zoomStep * 0.2f), -maxDist, -minDist); // 휠↑=가까이
            cmTransposer.m_FollowOffset = off;
            return;
        }

        // 3) 시네머신이 없을 때(직접 궤도) 거리로 줌
        if (!isLockOn && !cmFreeLook && !cmTransposer)
        {
            distance = Mathf.Clamp(distance - wheel * (zoomStep * 0.2f), minDist, maxDist);
        }
    }

    void LateUpdate()
    {
        // 비CM 경로에서만 수동 오빗
        if (!player) return;

        bool isLockOn = playerLockOn != null && playerLockOn.IsLockOn;

        // 상태 전이
        if (isLockOn && !wasLockOn)
        {
            cachedYaw = yaw;
            cachedPitch = pitch;
            wasLockOn = true;
            return;
        }
        if (!isLockOn && wasLockOn)
        {
            if (alignBehindOnUnlock) AlignBehindPlayer();
            else { yaw = cachedYaw; pitch = Mathf.Clamp(cachedPitch, minY, maxY); }
            wasLockOn = false;
        }

        // 시네머신을 쓰면 여기서 직접 포지션을 건드릴 필요 없음
        if (cmFreeLook || cmTransposer) return;
        if (isLockOn) return;

        float mx = Input.GetAxis("Mouse X") * mouseSensitivity;
        float my = Input.GetAxis("Mouse Y") * mouseSensitivity;

        if (Mathf.Abs(mx) > .01f || Mathf.Abs(my) > .01f)
        {
            yaw += mx;
            pitch = Mathf.Clamp(pitch - my, minY, maxY);
            noInputTimer = 0f;
        }
        else
        {
            noInputTimer += Time.deltaTime;
            if (noInputTimer > alignDelay)
                yaw = Mathf.LerpAngle(yaw, player.eulerAngles.y, Time.deltaTime * autoAlignSpeed);
        }

        var rot = Quaternion.Euler(pitch, yaw, 0f);
        var offset = rot * new Vector3(0, 0, -distance) + new Vector3(0, height, 0);
        transform.position = player.position + offset;
        transform.LookAt(player.position + Vector3.up * lookAtChestOffset);
    }

    public void AlignCameraImmediately() => AlignBehindPlayer();

    void AlignBehindPlayer()
    {
        yaw = player.eulerAngles.y;
        pitch = Mathf.Clamp(12f, minY, maxY);
        var rot = Quaternion.Euler(pitch, yaw, 0f);
        var offset = rot * new Vector3(0, 0, -distance) + new Vector3(0, height, 0);
        transform.position = player.position + offset;
        transform.rotation = rot;
    }
}
