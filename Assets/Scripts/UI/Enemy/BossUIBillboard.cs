using UnityEngine;

public class BossUIBillboard : MonoBehaviour
{
    const float RotationEpsilonDegrees = 0.01f;
    [SerializeField, Tooltip("빌보드 대상으로 사용할 카메라")]
    private Camera targetCamera;

    [SerializeField, Tooltip("수평(Y) 회전만 적용할지 여부")]
    private bool yOnly = true;

    Transform _tr;
    Transform _cameraTransform;

    void Awake()
    {
        _tr = transform;
        ResolveCamera();
    }

    void LateUpdate()
    {
        if (_cameraTransform == null)
        {
            ResolveCamera();
            if (_cameraTransform == null) return;
        }
        // 카메라를 향해 앞면이 보이도록 설정 (정면 항상 카메라를 바라봄)
        Vector3 dir = _tr.position - _cameraTransform.position;
        if (yOnly) dir.y = 0f;
        if (dir.sqrMagnitude <= 0.0001f) return;
        Quaternion desiredRotation = Quaternion.LookRotation(dir);
        if (Quaternion.Angle(_tr.rotation, desiredRotation) > RotationEpsilonDegrees)
            _tr.rotation = desiredRotation;
    }

    void ResolveCamera()
    {
        if (targetCamera == null)
            targetCamera = Camera.main;

        _cameraTransform = targetCamera != null ? targetCamera.transform : null;
    }
}
