using UnityEngine;

public class BossUIBillboard : MonoBehaviour
{
    [SerializeField, Tooltip("빌보드 대상으로 사용할 카메라")]
    private Camera targetCamera;

    [SerializeField, Tooltip("수평(Y) 회전만 적용할지 여부")]
    private bool yOnly = true;

    void Awake()
    {
        if (targetCamera == null) targetCamera = Camera.main;
    }

    void LateUpdate()
    {
        if (targetCamera == null) return;
        // 카메라를 향해 앞면이 보이도록 설정 (정면 항상 카메라를 바라봄)
        Vector3 dir = transform.position - targetCamera.transform.position;
        if (yOnly) dir.y = 0f;
        if (dir.sqrMagnitude <= 0.0001f) return;
        transform.rotation = Quaternion.LookRotation(dir);
    }
}
