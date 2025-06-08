using UnityEngine;

public class FreeLookCamera : MonoBehaviour
{
    public Transform player;
    public PlayerLockOn playerLockOn;
    public float distance = 5f;
    public float height = 2f;
    public float mouseSensitivity = 3f;
    public float minY = -30f;
    public float maxY = 60f;

    float yaw = 0f;
    float pitch = 10f;

    void Start()
    {
        Vector3 angles = transform.eulerAngles;
        yaw = angles.y;
        pitch = angles.x;
    }

    void LateUpdate()
    {
        if (player == null || playerLockOn == null)
            return;

        // 락온 아닐 때는 자유 시점
        if (!playerLockOn.IsLockOn)
        {
            float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity;
            float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity;

            yaw += mouseX;
            pitch -= mouseY;
            pitch = Mathf.Clamp(pitch, minY, maxY);

            Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 offset = rotation * new Vector3(0, 0, -distance) + new Vector3(0, height, 0);

            transform.position = player.position + offset;
            transform.LookAt(player.position + Vector3.up * 1.0f);
        }
        // 락온 중에는 카메라 직접 이동 금지
    }
}