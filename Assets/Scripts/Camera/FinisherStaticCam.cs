// FinisherStaticCam.cs
using UnityEngine;
using UnityEngine.Playables;
using Cinemachine;

[DisallowMultipleComponent]
public class FinisherStaticCam : MonoBehaviour
{
    // ===== 변수 헤더(한글 설명) =====
    [Header("플레이어 참조")]
    [Tooltip("궁극기 주체 플레이어 루트 Transform")]
    public Transform player;
    [Tooltip("플레이어 얼굴(머리) Transform. 없으면 player.position + headHeight 사용")]
    public Transform headTransform;
    [Tooltip("headTransform이 없을 때 사용할 머리 높이")]
    public float headHeight = 1.6f;

    [Header("카메라 배치(발동 시 고정)")]
    [Tooltip("플레이어 정면으로부터 떨어질 거리 (양수)")]
    public float forwardDistance = 3f;
    [Tooltip("카메라 높이 (바닥 기준; 낮게 설정하면 올려다보는 각도가 됨)")]
    public float cameraHeight = 0.25f;
    [Tooltip("카메라의 좌우 오프셋")]
    public float lateralOffset = 0f;

    [Header("Cinemachine / 우선도")]
    [Tooltip("제어할 Cinemachine Virtual Camera")]
    public CinemachineVirtualCamera vCam;
    [Tooltip("타임라인(선택) - 재생 제어는 플레이어 컨트롤러에서 함")]
    public PlayableDirector director;
    [Tooltip("타임라인 시작 시 vCam 우선도로 전환할 값")]
    public int overridePriority = 100;
    [Tooltip("발동 전 원래 Priority를 복구할 때 사용할 값")]
    public int restorePriority = 10;

    [Header("연동: 플레이어 이동 실행자")]
    [Tooltip("플레이어를 카메라 앞 Anchor로 이동시키는 스크립트")]
    public PlayerFinisherMover playerMover;

    // 내부
    Transform _anchor;
    int _oldPriority;

    void Awake()
    {
        if (vCam == null) vCam = GetComponent<CinemachineVirtualCamera>();
        _oldPriority = vCam != null ? vCam.Priority : 0;
    }

    // 발동 시 호출: activationPosition은 '플레이어가 궁극기를 사용한 위치' (world)
    public void LockAtActivation(Vector3 activationPosition)
    {
        if (player == null || vCam == null)
        {
            Debug.LogWarning("[FinisherStaticCam] player 또는 vCam이 할당되지 않았습니다.");
            return;
        }

        // 플레이어 발동 시점의 정면 기준으로 카메라 위치 계산
        Vector3 forward = player.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.001f) forward = Vector3.forward;
        forward.Normalize();

        Vector3 camPos = activationPosition + forward * forwardDistance + Vector3.up * cameraHeight + player.right * lateralOffset;

        // vCam 고정(Follow 사용하지 않음), LookAt은 headTransform 또는 대체 포인트로 설정
        vCam.Follow = null;
        _anchor = new GameObject("FinisherAnchor").transform;
        _anchor.position = camPos + vCam.transform.forward * -1.5f; // 카메라 앞, 플레이어가 이동할 위치
        _anchor.rotation = Quaternion.LookRotation((player.position + Vector3.up * headHeight) - _anchor.position, Vector3.up);

        // 카메라 위치 및 회전 설정
        vCam.transform.position = camPos;
        if (headTransform != null) vCam.LookAt = headTransform;
        else
        {
            // LookAt이 없을 경우 카메라가 플레이어 머리 방향을 바라보게 회전
            Vector3 lookTarget = player.position + Vector3.up * headHeight;
            vCam.transform.rotation = Quaternion.LookRotation((lookTarget - camPos).normalized, Vector3.up);
        }

        // 우선도 올리기(다른 vCam과 블렌드되게 하려면 Priority 변경)
        _oldPriority = vCam.Priority;
        vCam.Priority = overridePriority;
    }

    // 타임라인에서 시그널로 호출: 실제 이동 시작(플레이어 이동 담당자에게 위임)
    // duration은 이동 지속 시간(초)
    public void TriggerMoveToAnchor(float duration)
    {
        if (_anchor == null || playerMover == null)
        {
            Debug.LogWarning("[FinisherStaticCam] Anchor 또는 playerMover가 없습니다.");
            return;
        }
        playerMover.StartMoveToAnchor(_anchor, duration);
    }

    // 타임라인/연출이 끝났을 때 호출
    public void Release()
    {
        if (vCam != null) vCam.Priority = _oldPriority;
        if (_anchor != null) { Destroy(_anchor.gameObject); _anchor = null; }
    }

    // 외부에서 Anchor 참조 필요하면 호출
    public Transform GetAnchor() => _anchor;
}
