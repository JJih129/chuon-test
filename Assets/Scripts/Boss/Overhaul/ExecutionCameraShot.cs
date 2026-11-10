// 반복 16: 처형·페이즈 전환 컷 카메라. 런타임 CinemachineCamera(우선순위 140)를 매 프레임 직접 배치한다.
// 끄면 SoulsCameraRig 가 기존 규칙대로 0.35 s 블렌드로 플레이어 등 뒤 포즈에 돌아온다 (Docs/CameraDesign_Soulslike.md §6).
using Unity.Cinemachine;
using UnityEngine;

public sealed class ExecutionCameraShot : MonoBehaviour
{
    const int ActivePriority = 140;

    static ExecutionCameraShot s_instance;

    CinemachineCamera _vcam;
    object _owner;
    ExecutionKind _kind;
    Transform _player;
    Transform _victim;
    Transform _focus;
    float _startedAt;
    float _duration;
    bool _transition;
    Vector3 _side;

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

    public static void Begin(object owner, ExecutionKind kind, Transform player, Transform victim, Transform focus, float duration)
    {
        ExecutionCameraShot s = EnsureInstance();
        s._owner = owner;
        s._kind = kind;
        s._transition = false;
        s._player = player;
        s._victim = victim;
        s._focus = focus != null ? focus : victim;
        s._duration = Mathf.Max(0.2f, duration);
        s._startedAt = Time.unscaledTime;
        Vector3 axis = victim != null && player != null ? victim.position - player.position : Vector3.forward;
        axis.y = 0f;
        s._side = Vector3.Cross(Vector3.up, axis.sqrMagnitude > 0.001f ? axis.normalized : Vector3.forward);
        s.Activate();
    }

    /// <summary>페이즈 전환 컷: 보스 근접 저각 → 천천히 뒤로 빠짐.</summary>
    public static void BeginTransition(object owner, Transform boss, Transform focus, Transform player, float duration)
    {
        ExecutionCameraShot s = EnsureInstance();
        s._owner = owner;
        s._kind = ExecutionKind.None;
        s._transition = true;
        s._player = player;
        s._victim = boss;
        s._focus = focus != null ? focus : boss;
        s._duration = Mathf.Max(0.2f, duration);
        s._startedAt = Time.unscaledTime;
        Vector3 axis = boss != null && player != null ? boss.position - player.position : Vector3.forward;
        axis.y = 0f;
        s._side = Vector3.Cross(Vector3.up, axis.sqrMagnitude > 0.001f ? axis.normalized : Vector3.forward);
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
        Vector3 playerChest = _player != null ? _player.position + Vector3.up * 1.25f : focus;
        Vector3 victimPos = _victim.position;
        float ease = Mathf.SmoothStep(0f, 1f, k);

        if (_transition)
        {
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
            return;
        }

        Vector3 mid = Vector3.Lerp(playerChest, focus, 0.5f);
        switch (_kind)
        {
            case ExecutionKind.BossBack:
            {
                // 플레이어 어깨 너머 → 등 뒤 찌르기
                Vector3 axis = (victimPos - (_player != null ? _player.position : victimPos - Vector3.forward));
                axis.y = 0f;
                axis = axis.sqrMagnitude > 0.001f ? axis.normalized : Vector3.forward;
                Vector3 side = Vector3.Cross(Vector3.up, axis);
                pos = playerChest - axis * Mathf.Lerp(2.3f, 1.8f, ease) + side * 0.75f + Vector3.up * 0.45f;
                rot = Quaternion.LookRotation(focus - pos, Vector3.up);
                fov = Mathf.Lerp(40f, 34f, ease);
                break;
            }
            case ExecutionKind.BossFinal:
            {
                // 코어 접사: 측면에서 코어 쪽으로 밀고 들어간다
                Vector3 fwd = _victim.forward;
                fwd.y = 0f;
                fwd = fwd.sqrMagnitude > 0.001f ? fwd.normalized : Vector3.forward;
                pos = focus + fwd * Mathf.Lerp(2.4f, 1.35f, ease) + _side * Mathf.Lerp(1.6f, 0.75f, ease) + Vector3.up * Mathf.Lerp(-0.1f, 0.15f, ease);
                rot = Quaternion.LookRotation(Vector3.Lerp(mid, focus, ease) - pos, Vector3.up);
                fov = Mathf.Lerp(36f, 24f, ease);
                break;
            }
            default:
            {
                // 정면: 3/4 측면 저각
                pos = mid + _side * Mathf.Lerp(4.6f, 3.9f, ease) + Vector3.up * Mathf.Lerp(0.1f, 0.2f, ease) - (focus - playerChest).normalized * 0.9f;
                rot = Quaternion.LookRotation(mid + Vector3.up * 0.15f - pos, Vector3.up);
                fov = Mathf.Lerp(46f, 38f, ease);
                break;
            }
        }
    }

    void OnDestroy()
    {
        if (s_instance == this)
            s_instance = null;
    }
}
