using UnityEngine;
using Cinemachine;

public class FreeLookCamera : MonoBehaviour
{
    [Header("Refs")]
    public Transform player;
    public PlayerLockOn playerLockOn;

    [Header("Orbit")]
    public float distance = 4f;
    public float height = 1.5f;
    public float mouseSensitivity = 3f;
    public float minY = -30f;
    public float maxY = 60f;

    [Header("Auto-Align")]
    public float autoAlignSpeed = 2.5f;
    public float alignDelay = 1.0f;

    [Header("Zoom")]
    public float zoomStep = 5f;
    public float minFov = 30f;
    public float maxFov = 70f;
    
    // 내부 변수
    float noInputTimer = 0f;
    float yaw = 0f;
    float pitch = 10f;
    
    CinemachineFreeLook cmFreeLook;

    void Awake()
    {
        cmFreeLook = GetComponent<CinemachineFreeLook>();
    }

    void Start()
    {
        if (!player) return;
        var ang = transform.eulerAngles;
        yaw = ang.y;
        pitch = Mathf.Clamp(ang.x, minY, maxY);
        
        // 시작 시 커서 잠금
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void Update()
    {
        // ★ [핵심] 일시정지 중이면 아무것도 하지 마라 (커서 건드리지 마라)
        if (Time.timeScale == 0f) return;

        // ... 기존 줌 로직 ...
        // (간략화됨)
    }

    void LateUpdate()
    {
        // ★ [핵심] 일시정지 중이면 카메라 회전 멈춤
        if (Time.timeScale == 0f) return;

        if (!player) return;

        // 마우스 입력 받기
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

        // 카메라 위치/회전 적용
        var rot = Quaternion.Euler(pitch, yaw, 0f);
        var offset = rot * new Vector3(0, 0, -distance) + new Vector3(0, height, 0);
        transform.position = player.position + offset;
        transform.LookAt(player.position + Vector3.up * 1.0f);
    }
}