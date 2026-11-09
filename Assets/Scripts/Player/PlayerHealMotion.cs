// 파일명: PlayerHealMotion.cs
// 회복 앰플 '캡슐 깨기' 동작 (전용 애니메이션 클립이 없어 상체 절차적 포즈로 구현).
// 검을 들지 않은 손을 가슴 앞으로 가져와 캡슐을 쥐고(발광) 회복 시점에 깨뜨린다.
// Animator 평가 이후 LateUpdate 에서 2본 IK 로 팔만 덮어쓰므로 하체/이동 애니메이션은 그대로다.
using UnityEngine;

[DisallowMultipleComponent]
public sealed class PlayerHealMotion : MonoBehaviour
{
    [SerializeField] float raiseFraction = 0.32f;   // 손을 올리는 구간 (동작 대비)
    [SerializeField] float releaseFraction = 0.22f; // 마지막 내리는 구간
    [SerializeField] Vector3 chestOffset = new Vector3(0.0f, 0.02f, 0.30f); // 가슴 기준 (우, 상, 앞)
    [SerializeField] Color capsuleColor = new Color(0.55f, 1f, 0.92f, 1f);
    [SerializeField] Color crushColor = new Color(1f, 1f, 1f, 1f);

    PlayerConsumables _consumables;
    PlayerReferences _refs;
    Animator _animator;
    Transform _upper, _lower, _hand, _chest;
    bool _resolved;
    bool _active;
    bool _crushed;
    float _crushAt;
    Transform _capsule;
    Renderer _capsuleRenderer;
    MaterialPropertyBlock _mpb;
    static Material s_capsuleMaterial;

    void Awake()
    {
        _consumables = GetComponent<PlayerConsumables>();
        _refs = GetComponent<PlayerReferences>();
    }

    void OnEnable()
    {
        if (_consumables == null) _consumables = GetComponent<PlayerConsumables>();
        if (_consumables == null) return;
        _consumables.OnHealStarted += HandleStarted;
        _consumables.OnHealApplied += HandleApplied;
        _consumables.OnHealCancelled += HandleCancelled;
    }

    void OnDisable()
    {
        if (_consumables != null)
        {
            _consumables.OnHealStarted -= HandleStarted;
            _consumables.OnHealApplied -= HandleApplied;
            _consumables.OnHealCancelled -= HandleCancelled;
        }
        SetCapsuleVisible(false);
        _active = false;
    }

    void HandleStarted()
    {
        _active = true;
        _crushed = false;
        ResolveRig();
        EnsureCapsule();
        SetCapsuleVisible(_capsule != null);
    }

    void HandleApplied(int amount)
    {
        _crushed = true;
        _crushAt = Time.time;
    }

    void HandleCancelled()
    {
        _active = false;
        SetCapsuleVisible(false);
    }

    void ResolveRig()
    {
        Animator animator = _refs != null ? _refs.MainAnimator : null;
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (_resolved && animator == _animator)
            return;

        _animator = animator;
        _resolved = true;
        _upper = _lower = _hand = _chest = null;
        if (_animator == null || !_animator.isHuman)
            return;

        // 검(렌더러)을 쥔 손의 반대 손을 쓴다.
        Transform rightHand = _animator.GetBoneTransform(HumanBodyBones.RightHand);
        Transform leftHand = _animator.GetBoneTransform(HumanBodyBones.LeftHand);
        int rightItems = rightHand != null ? rightHand.GetComponentsInChildren<Renderer>(true).Length : 0;
        int leftItems = leftHand != null ? leftHand.GetComponentsInChildren<Renderer>(true).Length : 0;
        bool useLeft = rightItems >= leftItems;

        _upper = _animator.GetBoneTransform(useLeft ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm);
        _lower = _animator.GetBoneTransform(useLeft ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
        _hand = useLeft ? leftHand : rightHand;
        _chest = _animator.GetBoneTransform(HumanBodyBones.UpperChest);
        if (_chest == null) _chest = _animator.GetBoneTransform(HumanBodyBones.Chest);
        if (_chest == null) _chest = _animator.GetBoneTransform(HumanBodyBones.Spine);
        if (_capsule != null && _hand != null && _capsule.parent != _hand)
        {
            _capsule.SetParent(_hand, false);
            _capsule.localPosition = Vector3.zero;
        }
    }

    void EnsureCapsule()
    {
        if (_capsule != null || _hand == null)
            return;

        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        go.name = "_HealAmpouleCapsule";
        Collider c = go.GetComponent<Collider>();
        if (c != null) Destroy(c);
        _capsule = go.transform;
        _capsule.SetParent(_hand, false);
        _capsule.localPosition = Vector3.zero;
        _capsule.localRotation = Quaternion.Euler(0f, 0f, 90f);
        _capsule.localScale = new Vector3(0.045f, 0.06f, 0.045f);
        _capsuleRenderer = go.GetComponent<Renderer>();
        _capsuleRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _capsuleRenderer.receiveShadows = false;
        if (s_capsuleMaterial == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader != null)
                s_capsuleMaterial = new Material(shader) { name = "_HealAmpouleCapsule (runtime)" };
        }
        if (s_capsuleMaterial != null)
            _capsuleRenderer.sharedMaterial = s_capsuleMaterial;
        _mpb = new MaterialPropertyBlock();
        go.SetActive(false);
    }

    void SetCapsuleVisible(bool visible)
    {
        if (_capsule != null)
            _capsule.gameObject.SetActive(visible);
    }

    void LateUpdate()
    {
        if (!_active || _consumables == null)
            return;

        if (!_consumables.IsHealing)
        {
            _active = false;
            SetCapsuleVisible(false);
            return;
        }

        float p = _consumables.HealMotionProgress;
        float w;
        if (p < raiseFraction) w = Mathf.SmoothStep(0f, 1f, p / Mathf.Max(0.01f, raiseFraction));
        else if (p > 1f - releaseFraction) w = Mathf.SmoothStep(1f, 0f, (p - (1f - releaseFraction)) / Mathf.Max(0.01f, releaseFraction));
        else w = 1f;

        if (_upper != null && _lower != null && _hand != null && _chest != null && w > 0.001f)
        {
            Transform root = transform;
            Vector3 target = _chest.position + root.right * chestOffset.x + Vector3.up * chestOffset.y + root.forward * chestOffset.z;
            // 쥐어짜는 순간 손을 살짝 앞으로 밀었다 당긴다
            if (_crushed)
            {
                float k = Mathf.Clamp01((Time.time - _crushAt) / 0.12f);
                target += root.forward * (0.05f * Mathf.Sin(k * Mathf.PI));
            }
            Vector3 hint = _lower.position - root.forward * 0.3f + Vector3.down * 0.2f;
            SolveTwoBone(_upper, _lower, _hand, target, hint, w);
        }

        if (_capsule != null && _capsuleRenderer != null)
        {
            Color color = capsuleColor;
            float scale = 1f;
            if (_crushed)
            {
                float k = Mathf.Clamp01((Time.time - _crushAt) / 0.14f);
                scale = Mathf.Lerp(1.6f, 0.01f, k);
                color = Color.Lerp(crushColor, capsuleColor, k);
                if (k >= 1f) SetCapsuleVisible(false);
            }
            else
            {
                float pulse = 0.75f + 0.25f * Mathf.Sin(Time.time * 28f);
                color *= pulse;
                color.a = 1f;
            }
            _capsule.localScale = new Vector3(0.045f, 0.06f, 0.045f) * scale;
            _capsuleRenderer.GetPropertyBlock(_mpb);
            _mpb.SetColor("_BaseColor", color);
            _mpb.SetColor("_Color", color);
            _capsuleRenderer.SetPropertyBlock(_mpb);
        }
    }

    // 2본 IK (a=상완, b=하완, c=손). 결과를 w 로 원래 포즈와 섞는다.
    static void SolveTwoBone(Transform a, Transform b, Transform c, Vector3 target, Vector3 hint, float w)
    {
        Quaternion aLocal0 = a.localRotation;
        Quaternion bLocal0 = b.localRotation;

        Vector3 aPos = a.position, bPos = b.position, cPos = c.position;
        float lab = (bPos - aPos).magnitude;
        float lcb = (cPos - bPos).magnitude;
        if (lab < 1e-4f || lcb < 1e-4f)
            return;
        float lat = Mathf.Clamp((target - aPos).magnitude, 0.01f, lab + lcb - 0.002f);

        float acAb0 = Mathf.Acos(Mathf.Clamp(Vector3.Dot((cPos - aPos).normalized, (bPos - aPos).normalized), -1f, 1f));
        float baBc0 = Mathf.Acos(Mathf.Clamp(Vector3.Dot((aPos - bPos).normalized, (cPos - bPos).normalized), -1f, 1f));
        float acAb1 = Mathf.Acos(Mathf.Clamp((lcb * lcb - lab * lab - lat * lat) / (-2f * lab * lat), -1f, 1f));
        float baBc1 = Mathf.Acos(Mathf.Clamp((lat * lat - lab * lab - lcb * lcb) / (-2f * lab * lcb), -1f, 1f));

        Vector3 axis0 = Vector3.Cross(cPos - aPos, hint - aPos);
        if (axis0.sqrMagnitude < 1e-8f) axis0 = Vector3.Cross(cPos - aPos, bPos - aPos);
        if (axis0.sqrMagnitude < 1e-8f) return;
        axis0.Normalize();

        Quaternion r0 = Quaternion.AngleAxis((acAb1 - acAb0) * Mathf.Rad2Deg, Quaternion.Inverse(a.rotation) * axis0);
        Quaternion r1 = Quaternion.AngleAxis((baBc1 - baBc0) * Mathf.Rad2Deg, Quaternion.Inverse(b.rotation) * axis0);
        a.localRotation = aLocal0 * r0;
        b.localRotation = bLocal0 * r1;

        // 손이 타깃 방향을 향하도록 상완을 돌린다
        cPos = c.position;
        aPos = a.position;
        Vector3 from = (cPos - aPos).normalized;
        Vector3 to = (target - aPos).normalized;
        float ang = Mathf.Acos(Mathf.Clamp(Vector3.Dot(from, to), -1f, 1f)) * Mathf.Rad2Deg;
        Vector3 axis1 = Vector3.Cross(from, to);
        if (axis1.sqrMagnitude > 1e-8f && ang > 0.01f)
            a.localRotation = a.localRotation * Quaternion.AngleAxis(ang, Quaternion.Inverse(a.rotation) * axis1.normalized);

        if (w < 0.999f)
        {
            a.localRotation = Quaternion.Slerp(aLocal0, a.localRotation, w);
            b.localRotation = Quaternion.Slerp(bLocal0, b.localRotation, w);
        }
    }
}
