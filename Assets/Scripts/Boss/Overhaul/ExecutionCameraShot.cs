// 반복 16: 페이즈 전환 컷 카메라. 런타임 CinemachineCamera(우선순위 140)를 매 프레임 직접 배치한다.
// 끄면 SoulsCameraRig 가 기존 규칙대로 0.35 s 블렌드로 플레이어 등 뒤 포즈에 돌아온다 (Docs/CameraDesign_Soulslike.md §6).
// 처형 카메라는 ExecutionCamChoreographer 하나가 맡는다 (이전 처형 포즈 코드는 그쪽 샷 라이브러리로 통합됨, 중복 제거).
using Unity.Cinemachine;
using UnityEngine;

public sealed class ExecutionCameraShot : MonoBehaviour
{
    const int ActivePriority = 140;

    static ExecutionCameraShot s_instance;

    CinemachineCamera _vcam;
    object _owner;
    Transform _victim;
    Transform _focus;
    float _startedAt;
    float _duration;

    public static bool IsActive => s_instance != null && s_instance._owner != null;

    static ExecutionCameraShot EnsureInstance()
    {
        if (s_instance != null)
            return s_instance;
        var go = new GameObject("OH_ExecutionCamera");
        s_instance = go.AddComponent<ExecutionCameraShot>();
        s_instance._vcam = go.AddComponent<CinemachineCamera>();
        s_instance._vcam.Priority = 0;
        s_instance._vcam.enabled = false;
        return s_instance;
    }

    /// <summary>페이즈 전환 컷: 보스 근접 저각 → 천천히 뒤로 빠짐. (player 는 API 호환용)</summary>
    public static void BeginTransition(object owner, Transform boss, Transform focus, Transform player, float duration)
    {
        if (boss == null)
            return;
        ExecutionCameraShot s = EnsureInstance();
        s._owner = owner;
        s._victim = boss;
        s._focus = focus != null ? focus : boss;
        s._duration = Mathf.Max(0.2f, duration);
        s._startedAt = Time.unscaledTime;
        s.Activate();
    }

    public static void End(object owner)
    {
        if (s_instance == null || s_instance._owner != owner)
            return;
        s_instance._owner = null;
        s_instance._vcam.Priority = 0;
        s_instance._vcam.enabled = false;
    }

    void Activate()
    {
        Pose(0f, out Vector3 pos, out Quaternion rot, out float fov);
        transform.SetPositionAndRotation(pos, rot);
        _vcam.Lens.FieldOfView = fov;
        _vcam.enabled = true;
        _vcam.Priority = ActivePriority;
    }

    void LateUpdate()
    {
        if (_owner == null || _victim == null)
        {
            if (_owner != null)
                End(_owner);
            return;
        }
        float k = Mathf.Clamp01((Time.unscaledTime - _startedAt) / _duration);
        Pose(k, out Vector3 pos, out Quaternion rot, out float fov);
        transform.SetPositionAndRotation(pos, rot);
        _vcam.Lens.FieldOfView = fov;
    }

    void Pose(float k, out Vector3 pos, out Quaternion rot, out float fov)
    {
        Vector3 focus = _focus != null ? _focus.position : _victim.position + Vector3.up * 1.4f;
        Vector3 victimPos = _victim.position;
        float ease = Mathf.SmoothStep(0f, 1f, k);

        // 보스 정면 사선 저각: 1.6 m → 4.2 m 로 물러나며 장갑이 날아가는 것을 담는다
        Vector3 fwd = _victim.forward;
        fwd.y = 0f;
        fwd = fwd.sqrMagnitude > 0.001f ? fwd.normalized : Vector3.forward;
        Vector3 side = Vector3.Cross(Vector3.up, fwd);
        float dist = Mathf.Lerp(2.4f, 5.2f, ease);
        pos = victimPos + fwd * dist + side * Mathf.Lerp(1.1f, 2.0f, ease) + Vector3.up * Mathf.Lerp(0.7f, 1.4f, ease);
        Vector3 look = Vector3.Lerp(focus, victimPos + Vector3.up * 1.3f, ease);
        rot = Quaternion.LookRotation(look - pos, Vector3.up);
        fov = Mathf.Lerp(34f, 44f, ease);
    }

    void OnDestroy()
    {
        if (s_instance == this)
            s_instance = null;
    }
}
