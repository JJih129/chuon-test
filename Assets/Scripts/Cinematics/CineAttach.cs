using UnityEngine;

/// <summary>
/// 연출 리그 안의 하위 루트를 재생 시작 시 씬 대상(앵커·플레이어·보스)에 맞춘다.
/// 샷 카메라·소품·퍼펫은 이 하위 루트의 로컬 공간에 저작되므로, 대상 위치가 바뀌어도 구도가 유지된다.
/// follow 를 켜면 재생 중 매 프레임 따라간다 (LateUpdate 이전, 할당 없음).
/// </summary>
[DisallowMultipleComponent]
public sealed class CineAttach : MonoBehaviour
{
    public enum Target : byte
    {
        None = 0,
        /// <summary>CinematicAnchor 이름 (없으면 같은 이름의 GameObject).</summary>
        Anchor = 1,
        /// <summary>플레이어 루트 (발 위치, 몸 방향 요).</summary>
        Player = 2,
        /// <summary>보스(Type-D) 루트.</summary>
        Boss = 3,
        /// <summary>재생 시작 순간의 메인 카메라 (첫 컷을 게임플레이 화면에서 이어 받을 때).</summary>
        MainCamera = 4,
        /// <summary>플레이어 휴머노이드 머리 뼈 (클로즈업 시선 대상, 모델 키와 무관).</summary>
        PlayerHead = 5,
        /// <summary>보스 휴머노이드 머리 뼈 (Type-D 바이저 인서트).</summary>
        BossHead = 6,
    }

    [SerializeField] Target target = Target.Anchor;
    [SerializeField] string anchorName;
    [Tooltip("대상의 요(Y 회전)를 따른다. 끄면 리그에 저작된 월드 방향 유지.")]
    [SerializeField] bool matchYaw = true;
    [Tooltip("요만 쓰고 피치/롤은 버린다 (카메라 대상일 때도 수평 유지).")]
    [SerializeField] bool yawOnly = true;
    [SerializeField] Vector3 localOffset;
    [SerializeField] float yawOffset;
    [Tooltip("재생 중 계속 따라간다 (움직이는 대상).")]
    [SerializeField] bool follow;
    [Tooltip("대상을 못 찾으면 이 하위 루트를 끈다 (엉뚱한 곳을 비추지 않게).")]
    [SerializeField] bool disableIfMissing;
    [Tooltip("None 이 아니면 요를 이 대상 쪽으로 돌린다 (예: 플레이어 기준 샷이 보스/데스크를 향하게). matchYaw 보다 우선.")]
    [SerializeField] Target faceTarget = Target.None;
    [SerializeField] string faceAnchorName;
    [Tooltip("0 보다 크면 시작 시 뒤쪽(-Z)으로 이 거리만큼 벽이 없는 요를 고른다 (엔딩 홀로그램처럼 대상 뒤에 무대를 세우는 리그). 반복 14C.")]
    [SerializeField] float clearBehind;

    Transform _resolved;
    Transform _face;

    public Target AttachTarget => target;
    public string AnchorName => anchorName;
    public bool Follow => follow;

    public void SetClearBehind(float metres) => clearBehind = Mathf.Max(0f, metres);

    public void ConfigureFacing(Target face, string anchor = null)
    {
        faceTarget = face;
        faceAnchorName = anchor;
    }

    public void Configure(Target attachTarget, string anchor, bool matchTargetYaw = true, bool followTarget = false, Vector3 offset = default, float yaw = 0f)
    {
        target = attachTarget;
        anchorName = anchor;
        matchYaw = matchTargetYaw;
        follow = followTarget;
        localOffset = offset;
        yawOffset = yaw;
    }

    /// <summary>재생 시작 시 1회. 찾았으면 true.</summary>
    public bool Begin(Transform player, Transform boss)
    {
        _resolved = ResolveTarget(target, anchorName, player, boss);
        _face = faceTarget != Target.None ? ResolveTarget(faceTarget, faceAnchorName, player, boss) : null;
        if (_resolved == null && target == Target.Boss && player != null && !follow)
        {
            // 반복 14C: 보스가 이미 사라졌으면(파괴) 플레이어 앞 6 m 에 플레이어를 보게 세운다 — 무대가 씬 원점에 생기지 않게.
            Vector3 fwd = player.forward; fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.0001f) fwd = Vector3.forward;
            fwd.Normalize();
            transform.SetPositionAndRotation(player.position + fwd * 6f, Quaternion.LookRotation(-fwd, Vector3.up) * Quaternion.Euler(0f, yawOffset, 0f));
            if (clearBehind > 0f)
                ResolveClearBehind(player, boss);
            return true;
        }
        if (_resolved == null)
        {
            if (target != Target.None && disableIfMissing)
                gameObject.SetActive(false);
            return target == Target.None;
        }

        if (!gameObject.activeSelf && disableIfMissing)
            gameObject.SetActive(true);
        Snap();
        if (clearBehind > 0f)
            ResolveClearBehind(player, boss);
        return true;
    }

    // 대상 뒤(-Z)가 벽에 막혀 있으면 ±35° 씩 돌려 가며 처음으로 트인 방향을 쓴다 (모두 막히면 가장 먼 방향). 시작 시 1회, 할당은 RaycastAll 배열 뿐.
    static readonly float[] s_clearYaws = { 0f, 35f, -35f, 70f, -70f, 105f, -105f, 140f, -140f, 180f };

    void ResolveClearBehind(Transform player, Transform boss)
    {
        Quaternion baseRot = transform.rotation;
        Vector3 origin = transform.position + Vector3.up * 1.2f;
        float bestDist = -1f;
        Quaternion best = baseRot;
        for (int i = 0; i < s_clearYaws.Length; i++)
        {
            Quaternion r = Quaternion.Euler(0f, s_clearYaws[i], 0f) * baseRot;
            Vector3 back = r * Vector3.back;
            float d = clearBehind;
            RaycastHit[] hits = Physics.SphereCastAll(origin, 0.45f, back, clearBehind, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int h = 0; h < hits.Length; h++)
            {
                Transform ht = hits[h].transform;
                if (hits[h].distance <= 0f || (player != null && ht.IsChildOf(player)) || (boss != null && ht.IsChildOf(boss)))
                    continue;
                d = Mathf.Min(d, hits[h].distance);
            }
            if (d > bestDist + 0.01f)
            {
                bestDist = d;
                best = r;
            }
            if (d >= clearBehind - 0.01f)
                break;
        }
        transform.rotation = best;
    }

    public void Tick()
    {
        if (follow && _resolved != null)
            Snap();
    }

    public void End()
    {
        _resolved = null;
        _face = null;
    }

    static Transform ResolveTarget(Target which, string anchor, Transform player, Transform boss)
    {
        switch (which)
        {
            case Target.Anchor: return CinematicAnchor.Resolve(anchor);
            case Target.Player: return player;
            case Target.Boss: return boss;
            case Target.MainCamera:
                Camera cam = GameplaySceneCache.ResolveMainCamera();
                return cam != null ? cam.transform : null;
            case Target.PlayerHead: return Head(player);
            case Target.BossHead: return Head(boss);
            default: return null;
        }
    }

    static Transform Head(Transform root)
    {
        if (root == null)
            return null;
        Animator[] animators = root.GetComponentsInChildren<Animator>(false);
        for (int i = 0; i < animators.Length; i++)
        {
            Animator a = animators[i];
            if (a != null && a.isHuman && a.avatar != null)
            {
                Transform head = a.GetBoneTransform(HumanBodyBones.Head);
                if (head != null)
                    return head;
            }
        }
        return root;
    }

    void Snap()
    {
        Quaternion rot;
        Vector3 toFace = _face != null ? _face.position - _resolved.position : Vector3.zero;
        toFace.y = 0f;
        if (toFace.sqrMagnitude > 0.01f)
        {
            rot = Quaternion.LookRotation(toFace.normalized, Vector3.up);
        }
        else if (!matchYaw)
        {
            rot = transform.rotation;
        }
        else if (yawOnly)
        {
            Vector3 fwd = _resolved.forward;
            fwd.y = 0f;
            rot = fwd.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(fwd.normalized, Vector3.up) : Quaternion.identity;
        }
        else
        {
            rot = _resolved.rotation;
        }

        rot *= Quaternion.Euler(0f, yawOffset, 0f);
        transform.SetPositionAndRotation(_resolved.position + rot * localOffset, rot);
    }
}
