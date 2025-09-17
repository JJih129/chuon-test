using UnityEngine;

public class BossUIBillboard : MonoBehaviour
{
    // [목표 카메라] 이 카메라 방향으로 UI가 정면 유지.
    [SerializeField, Tooltip("빌보드 대상으로 사용할 카메라")]
    private Camera targetCamera;

    // [축 고정] Y축만 회전할지 여부. true면 롤/피치는 고정하고 요만 적용.
    [SerializeField, Tooltip("수평(Y) 회전만 적용할지 여부")]
    private bool yOnly = true;

    private void LateUpdate()
    {
        if (targetCamera == null)
        {
            if (Camera.main == null) return;
            targetCamera = Camera.main;
        }

        Vector3 dir = targetCamera.transform.position - transform.position;
        if (yOnly) dir.y = 0f;

        if (dir.sqrMagnitude > 0.0001f)
            transform.rotation = Quaternion.LookRotation(-dir.normalized, Vector3.up);
    }
}
