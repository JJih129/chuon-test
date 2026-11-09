using System.Collections;
using Unity.Cinemachine;
using UnityEngine;

// Level_Facility 관측 브리지: 플레이어가 유리 바닥에 처음 올라서면 짧게 유리 너머 사선 시점으로 전환해
// 아래층 격리실에 붙잡힌 TYPE-D를 보여 준다 (랜드마크/최종 목표 예고). 한 번만 재생.
[RequireComponent(typeof(Collider))]
public sealed class LevelFacilityObservationBeat : MonoBehaviour
{
    [SerializeField] Transform lookTarget;
    [SerializeField] Vector3 lookTargetOffset = new Vector3(0f, 1.2f, 0f);
    [SerializeField] Vector3 cameraWorldPosition = new Vector3(1.4f, 42.6f, 11.2f);
    [SerializeField] Vector3 cameraPushIn = new Vector3(0f, -0.9f, 0f);
    [SerializeField, Range(10f, 70f)] float fieldOfView = 28f;
    [SerializeField, Range(8f, 70f)] float endFieldOfView = 16f;
    [SerializeField, Min(0.5f)] float hold = 3.0f;
    [Header("Crane to oblique (유리 너머 → 사선 구도)")]
    [Tooltip("켜면 유리 위 시점에서 잠깐 보여 준 뒤 격리 샤프트를 따라 사선 시점으로 내려가 보스를 크게 잡는다.")]
    [SerializeField] bool craneToOblique = true;
    [SerializeField] Vector3 obliqueCameraWorldPosition = new Vector3(4.5f, 16f, 5f);
    [SerializeField, Range(10f, 70f)] float obliqueFieldOfView = 34f;
    [SerializeField, Range(8f, 70f)] float obliqueEndFieldOfView = 28f;
    [SerializeField, Min(0f)] float establishDuration = 0.7f;
    [SerializeField, Min(0.1f)] float craneDuration = 1.3f;
    [SerializeField] int activePriority = 140;
    [SerializeField] string cueTitle = "아트리움 스카이덱";
    [SerializeField] string cueDescription = "유리 아래는 격납고. 실험체 TYPE-D가 대기 중이다.";

    bool _played;

    /// <summary>관측 연출(카메라 점유) 진행 중. 상층 잡몹이 이 동안 플레이어를 인지하지 않게 하는 데 쓴다.</summary>
    public static bool IsPlaying { get; private set; }

    void OnDisable()
    {
        IsPlaying = false;
    }

    void Reset()
    {
        Collider trigger = GetComponent<Collider>();
        if (trigger != null)
            trigger.isTrigger = true;
    }

    void OnTriggerEnter(Collider other)
    {
        if (_played || other == null)
            return;

        if (!other.CompareTag("Player") && (other.attachedRigidbody == null || !other.attachedRigidbody.CompareTag("Player")))
        {
            PlayerHealth health = other.GetComponentInParent<PlayerHealth>();
            if (health == null)
                return;
        }

        LobbyManager lobby = LobbyManager.Instance;
        if (lobby != null && lobby.HasHandedOffToLowerFloor)
            return;

        _played = true;
        IsPlaying = true;
        StartCoroutine(CoPlayBeat());
    }

    IEnumerator CoPlayBeat()
    {
        Camera mainCamera = Camera.main;
        if (mainCamera == null || mainCamera.GetComponent<CinemachineBrain>() == null || lookTarget == null)
            yield break;

        GameObject cameraObject = new GameObject("ObservationBeatCamera");
        cameraObject.transform.SetParent(transform, false);
        CinemachineCamera beatCamera = cameraObject.AddComponent<CinemachineCamera>();
        LensSettings lens = LensSettings.FromCamera(mainCamera);
        lens.FieldOfView = fieldOfView;
        lens.FarClipPlane = Mathf.Max(lens.FarClipPlane, 200f);
        beatCamera.Lens = lens;
        Aim(cameraObject.transform, cameraWorldPosition);
        beatCamera.Priority = activePriority;

        LobbyManager lobby = LobbyManager.Instance;
        if (lobby != null)
            lobby.ShowTransientQuestCue(cueTitle, cueDescription, (craneToOblique ? Mathf.Max(hold, establishDuration + craneDuration + 0.8f) : hold) + 0.6f, true);

        float elapsed = 0f;
        float total = craneToOblique ? Mathf.Max(hold, establishDuration + craneDuration + 0.8f) : hold;
        while (elapsed < total)
        {
            elapsed += Time.deltaTime;
            Vector3 position;
            float fov;
            if (!craneToOblique)
            {
                float t = Mathf.SmoothStep(0f, 1f, elapsed / hold);
                position = cameraWorldPosition + cameraPushIn * t;
                fov = Mathf.Lerp(fieldOfView, endFieldOfView, t);
            }
            else if (elapsed <= establishDuration)
            {
                position = cameraWorldPosition;
                fov = fieldOfView;
            }
            else
            {
                float crane = Mathf.SmoothStep(0f, 1f, (elapsed - establishDuration) / craneDuration);
                float settle = Mathf.Clamp01((elapsed - establishDuration - craneDuration) / Mathf.Max(0.1f, total - establishDuration - craneDuration));
                position = Vector3.Lerp(cameraWorldPosition, obliqueCameraWorldPosition, crane);
                fov = Mathf.Lerp(Mathf.Lerp(fieldOfView, obliqueFieldOfView, crane), obliqueEndFieldOfView, settle);
            }

            Aim(cameraObject.transform, position);
            LensSettings zoomLens = beatCamera.Lens;
            zoomLens.FieldOfView = fov;
            beatCamera.Lens = zoomLens;
            yield return null;
        }

        beatCamera.Priority = -100;
        yield return new WaitForSeconds(0.8f); // 게임플레이 카메라로 블렌드 복귀
        IsPlaying = false;
        yield return new WaitForSeconds(1.2f);
        Destroy(cameraObject);
    }

    void Aim(Transform cameraTransform, Vector3 position)
    {
        Vector3 target = lookTarget.position + lookTargetOffset;
        Vector3 direction = target - position;
        if (direction.sqrMagnitude < 0.0001f)
            return;

        // 사선(유리 너머) 구도는 월드 up을 쓰고, 거의 수직일 때만 +Z를 up으로 써서 롤이 튀지 않게 한다.
        Vector3 forward = direction.normalized;
        Vector3 up = Mathf.Abs(forward.y) > 0.97f ? Vector3.forward : Vector3.up;
        cameraTransform.SetPositionAndRotation(position, Quaternion.LookRotation(forward, up));
    }
}
