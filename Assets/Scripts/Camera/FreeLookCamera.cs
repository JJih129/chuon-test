using UnityEngine;

public class FreeLookCamera : MonoBehaviour
{
    [Header("Refs")]
    public Transform player;
    public PlayerLockOn playerLockOn;

    [Header("Orbit")]
    public float distance = 1f;
    public float height = 1f;
    public float mouseSensitivity = 3f;
    public float minY = -30f;
    public float maxY = 60f;

    [Header("Auto-Align")]
    public float autoAlignSpeed = 2.5f;
    public float alignDelay = 1.0f;

    [Header("Unlock Transition")]
    public bool alignBehindOnUnlock = true;
    public float lookAtChestOffset = 1.0f;

    float noInputTimer = 0f;
    float yaw = 0f;
    float pitch = 10f;
    bool wasLockOn = false;
    float cachedYaw, cachedPitch;

    void Start()
    {
        if (player == null) return;
        var ang = transform.eulerAngles;
        yaw = ang.y;
        pitch = Mathf.Clamp(ang.x, minY, maxY);

        // ★ 게임 시작 시 마우스 중앙 고정 & 숨기기
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void Update()
    {
        // ★ ESC로 잠금 해제/재적용 토글
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (Cursor.lockState == CursorLockMode.Locked)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            else
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }
    }

    void LateUpdate()
    {
        if (player == null || playerLockOn == null) return;
        bool isLockOn = playerLockOn.IsLockOn;

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
            else
            {
                yaw = cachedYaw;
                pitch = Mathf.Clamp(cachedPitch, minY, maxY);
            }
            wasLockOn = false;
        }

        // 자유 시점만 직접 제어
        if (!isLockOn)
        {
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
