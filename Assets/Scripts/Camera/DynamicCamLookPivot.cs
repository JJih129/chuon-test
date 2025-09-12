using UnityEngine;

// ===== 변수 헤더(한글 설명) =====
// 플레이어(타겟) 앞쪽에 "카메라가 바라볼 피벗"을 자동으로 위치시켜주는 보조 스크립트입니다.
// - forwardDistance: 타겟 정면 앞으로 얼마나 둘지(미터)
// - heightOffset: 타겟 기준 얼마나 높게 둘지(미터)
// - lateralOffset: 좌/우로 얼마나 치울지(미터, +는 오른쪽, -는 왼쪽)
// - followLerp: 따라붙는 속도(0=즉시 텔레포트, 1=매우 느림; 보통 0.1~0.3)
// - alignYawToTarget: 피벗의 Yaw(수평 회전)를 타겟 정면으로 맞출지 여부
// - yawLerp: Yaw 회전 보간 속도(0=즉시, 1=매우 느림)
[ExecuteAlways]
public class DynamicCamLookPivot : MonoBehaviour
{
    [Header("① 타겟(보통 Player)")]
    public Transform target;

    [Header("② 오프셋(미터) | 전방/높이/좌우")]
    public float forwardDistance = 2.0f;
    public float heightOffset = 1.5f;
    public float lateralOffset = 0.0f;

    [Header("③ 따라붙는 속도(0~1) | 0=즉시, 0.1~0.3 권장")]
    [Range(0f, 1f)] public float followLerp = 0.15f;

    [Header("④ 회전 옵션 | 타겟 정면으로 Yaw 정렬")]
    public bool alignYawToTarget = true;
    [Range(0f, 1f)] public float yawLerp = 0.15f;

    void LateUpdate()
    {
        if (!target) return;

        // 목표 위치 계산
        Vector3 desired = target.position
                        + target.forward * forwardDistance
                        + target.right * lateralOffset
                        + Vector3.up * heightOffset;

        // 위치 보간
        if (followLerp <= 0f || !Application.isPlaying)
            transform.position = desired;
        else
            transform.position = Vector3.Lerp(transform.position, desired, 1f - Mathf.Pow(1f - followLerp, Time.deltaTime * 60f));

        // 회전 보간(정면을 바라보게)
        if (alignYawToTarget)
        {
            Quaternion desiredRot = Quaternion.LookRotation(target.forward, Vector3.up);
            if (yawLerp <= 0f || !Application.isPlaying)
                transform.rotation = desiredRot;
            else
                transform.rotation = Quaternion.Slerp(transform.rotation, desiredRot, 1f - Mathf.Pow(1f - yawLerp, Time.deltaTime * 60f));
        }
    }
}
