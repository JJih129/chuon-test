using UnityEngine;

// ===== 변수 헤더(한글 설명) =====
// 카메라 앞에 Quad를 두고, 화면 전체를 꽉 채우도록 자동 크기/위치 보정
// 주로 풀스크린 셰이더 효과용 Quad에 붙여 사용
[ExecuteAlways]
[RequireComponent(typeof(MeshRenderer))]
public class FitFullscreenQuad : MonoBehaviour
{
    [Header("카메라 앞 거리 (Z축, 미터 단위)")]
    public float distance = 1.5f;

    [Header("대상 카메라 (비우면 Main Camera 자동)")]
    public Camera targetCamera;

    void LateUpdate()
    {
        if (!targetCamera) targetCamera = Camera.main;
        if (!targetCamera) return;

        // 카메라 자식으로 붙이기
        if (transform.parent != targetCamera.transform)
        {
            transform.SetParent(targetCamera.transform, false);
        }

        // 카메라 앞 distance 지점으로 배치
        transform.localPosition = new Vector3(0, 0, distance);
        transform.localRotation = Quaternion.identity;

        // 카메라 FOV/종횡비에 맞춰 Quad 스케일 계산
        float height = 2f * distance * Mathf.Tan(targetCamera.fieldOfView * 0.5f * Mathf.Deg2Rad);
        float width = height * targetCamera.aspect;

        transform.localScale = new Vector3(width, height, 1f);
    }
}