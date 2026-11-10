// 반복 16: 40 % 이하 부위 파괴 (Docs/Boss_TypeD_Phases.md §1)
//  바이저: 머리가 내려온 순간(경직·넉백·다운·브레이크·과부하·배기 정체) 명중 8회(강공 2) → 파괴 → 실명 6 s
//          (공격 직전 조준 회전 없음, 측면·후면 피해 ×1.5·균형 ×1.5·충격 ×1.3).
//  팔 칼날(왼 전완, Blender): 패링 4회 → 파괴 → 사선 베기 콤보(패턴 e) 영구 삭제.
using System.Collections;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class BossBreakableParts : MonoBehaviour
{
    [SerializeField, Min(1)] int visorIntegrity = 8;
    [SerializeField, Min(1)] int bladeParries = 4;
    [SerializeField] float blindDuration = 6f;
    [SerializeField] float sideDamageMultiplier = 1.5f;
    [SerializeField] float sideBalanceMultiplier = 1.5f;
    [SerializeField] float sideImpactMultiplier = 1.3f;
    [SerializeField, Range(20f, 120f)] float sideAngle = 60f;
    [SerializeField] string bladePatternName = "e";

    TypeDOverhaul _overhaul;
    bool _unlocked;
    bool _exposed;
    int _visorHits;
    int _bladeHits;
    float _blindUntil;
    GameObject _blade;
    Light _visorMark;
    Light _bladeMark;

    public bool Unlocked => _unlocked;
    public bool VisorBroken { get; private set; }
    public bool BladeBroken { get; private set; }
    public bool IsBlind => Time.time < _blindUntil;
    public int VisorHits => _visorHits;
    public int BladeParries => _bladeHits;

    public void Bind(TypeDOverhaul overhaul) => _overhaul = overhaul;

    void Start()
    {
        AttachBlade();
    }

    Animator Humanoid()
    {
        foreach (Animator a in _overhaul.Boss.GetComponentsInChildren<Animator>(true))
            if (a != null && a.isHuman)
                return a;
        return null;
    }

    void AttachBlade()
    {
        TypeDOverhaulAssets assets = TypeDOverhaulAssets.Load();
        Animator h = Humanoid();
        if (assets == null || assets.armBladeModel == null || h == null)
            return;
        Transform forearm = h.GetBoneTransform(HumanBodyBones.LeftLowerArm);
        Transform hand = h.GetBoneTransform(HumanBodyBones.LeftHand);
        if (forearm == null || hand == null)
            return;
        _blade = Instantiate(assets.armBladeModel, forearm);
        _blade.name = "TypeD_ArmBlade";
        foreach (Collider c in _blade.GetComponentsInChildren<Collider>())
            Destroy(c);
        // 전완 바깥쪽을 따라 손목 → 팔꿈치 방향으로 (블레이드 원점 = 손목 쪽 마운트)
        Vector3 along = (hand.position - forearm.position);
        _blade.transform.position = Vector3.Lerp(forearm.position, hand.position, 0.55f);
        _blade.transform.rotation = Quaternion.LookRotation(along.normalized, -forearm.right);
        _blade.transform.localScale = Vector3.one;
    }

    public void Unlock()
    {
        if (_unlocked)
            return;
        _unlocked = true;
        // 약점 표시: 바이저·칼날에 작은 레드 점광원 (TSUKASA 레드, 펄스)
        if (_overhaul.Armor != null && _overhaul.Armor.VisorAnchor != null)
            _visorMark = MakeMark(_overhaul.Armor.VisorAnchor);
        if (_blade != null)
            _bladeMark = MakeMark(_blade.transform);
    }

    static Light MakeMark(Transform parent)
    {
        var go = new GameObject("OH_WeakpointMark");
        go.transform.SetParent(parent, false);
        Light l = go.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = new Color(1f, 0.15f, 0.1f);
        l.range = 1.4f;
        l.intensity = 2.2f;
        l.shadows = LightShadows.None;
        return l;
    }

    void Update()
    {
        float pulse = 1.6f + 1.2f * Mathf.Sin(Time.time * 5f);
        if (_visorMark != null) _visorMark.intensity = pulse;
        if (_bladeMark != null) _bladeMark.intensity = pulse;
        if (_blindUntil > 0f && Time.time >= _blindUntil)
        {
            _blindUntil = 0f;
            _overhaul.Boss.SetOverhaulAimDisabled(false);
        }
    }

    public void SetExposed(bool exposed) => _exposed = exposed;

    /// <summary>QA/스크린샷: 즉시 파괴 (잠금 해제 포함).</summary>
    public void QaBreakVisor() { Unlock(); if (!VisorBroken) BreakVisor(); }
    public void QaBreakBlade() { Unlock(); if (!BladeBroken) BreakBlade(); }

    bool HeadReachable()
    {
        BossController b = _overhaul.Boss;
        BossOverhaulReaction r = b.ActiveOverhaulReaction;
        return _exposed || _overhaul.IsOverloaded || (_overhaul.Break != null && _overhaul.Break.IsInBreak) ||
               r == BossOverhaulReaction.Stagger || r == BossOverhaulReaction.Knockback || r == BossOverhaulReaction.Down ||
               r == BossOverhaulReaction.WallSplat || r == BossOverhaulReaction.Overload;
    }

    public void NotifyHit(HitPayload payload, int hpLost)
    {
        if (!_unlocked || VisorBroken || hpLost <= 0 || !HeadReachable())
            return;
        _visorHits += payload.hitType == HitType.Heavy || payload.hitType == HitType.Strong ? 2 : 1;
        if (_visorHits >= visorIntegrity)
            BreakVisor();
    }

    public void NotifyParry()
    {
        if (!_unlocked || BladeBroken)
            return;
        _bladeHits++;
        if (_bladeHits >= bladeParries)
            BreakBlade();
    }

    bool IsSideOrBackHit(HitPayload payload)
    {
        if (payload.attacker == null)
            return false;
        Vector3 to = payload.attacker.position - _overhaul.Boss.transform.position;
        to.y = 0f;
        Vector3 f = _overhaul.Boss.transform.forward;
        f.y = 0f;
        return to.sqrMagnitude > 0.01f && Vector3.Angle(f, to) >= sideAngle;
    }

    public float ResolveDamageMultiplier(HitPayload payload) => IsBlind && IsSideOrBackHit(payload) ? sideDamageMultiplier : 1f;
    public float ResolveBalanceMultiplier(HitPayload payload) => IsBlind && IsSideOrBackHit(payload) ? sideBalanceMultiplier : 1f;
    public float ResolveImpactMultiplier(HitPayload payload) => IsBlind && IsSideOrBackHit(payload) ? sideImpactMultiplier : 1f;

    void BreakVisor()
    {
        VisorBroken = true;
        Vector3 p = _overhaul.Armor != null && _overhaul.Armor.VisorAnchor != null ? _overhaul.Armor.VisorAnchor.position : _overhaul.Boss.transform.position + Vector3.up * 2.2f;
        if (_overhaul.Armor != null)
            _overhaul.Armor.BreakVisor();
        if (_visorMark != null)
            Destroy(_visorMark.gameObject);
        SpawnShards(TypeDOverhaulAssets.Load()?.visorShardModel, p, 6, 3.2f);
        _blindUntil = Time.time + blindDuration;
        _overhaul.Boss.SetOverhaulAimDisabled(true);
        Feedback(p, "바이저 파괴 — 실명", "oh.visor");
    }

    void BreakBlade()
    {
        BladeBroken = true;
        Vector3 p = _blade != null ? _blade.transform.position : _overhaul.Boss.transform.position + Vector3.up * 1.2f;
        if (_bladeMark != null)
            Destroy(_bladeMark.gameObject);
        if (_blade != null)
        {
            _blade.transform.SetParent(null, true);
            _blade.layer = 2;
            BoxCollider box = _blade.AddComponent<BoxCollider>();
            MeshFilter mf = _blade.GetComponentInChildren<MeshFilter>();
            if (mf != null && mf.sharedMesh != null)
            {
                box.center = mf.sharedMesh.bounds.center;
                box.size = Vector3.Max(mf.sharedMesh.bounds.size, Vector3.one * 0.05f);
            }
            Rigidbody rb = _blade.AddComponent<Rigidbody>();
            rb.mass = 3f;
            rb.AddForce((Vector3.up * 2.5f + Random.insideUnitSphere * 2f), ForceMode.VelocityChange);
            rb.AddTorque(Random.insideUnitSphere * 8f, ForceMode.VelocityChange);
            Destroy(_blade, 8f);
            _blade = null;
        }
        SpawnShards(TypeDOverhaulAssets.Load()?.bladeShardModel, p, 5, 2.6f);
        _overhaul.Boss.RemovePatternForOverhaul(bladePatternName);
        Feedback(p, "팔 칼날 파괴", "oh.blade");
    }

    void Feedback(Vector3 p, string banner, string bark)
    {
        TypeDOverhaulAssets a = TypeDOverhaulAssets.Load();
        if (a != null)
        {
            TypeDOverhaulAssets.PlayAt(a.partBreak, p, 1f, 1f);
            if (a.sparkVfx != null) TransientVfxPool.Spawn(a.sparkVfx, p, Quaternion.identity, null, 1.4f);
        }
        ActionColourDirector.PulseAt(p, 0.7f, 0.35f);
        CameraShake cs = GameplaySceneCache.ResolveMainCameraShake();
        if (cs != null)
            cs.Shake(0.25f, 0.2f);
        _overhaul.Hud.ShowBanner(banner, 1.1f);
        CombatBarkDirector.TryBark(bark, true);
    }

    static void SpawnShards(GameObject prefab, Vector3 p, int count, float speed)
    {
        for (int i = 0; i < count; i++)
        {
            GameObject s;
            if (prefab != null)
                s = Instantiate(prefab, p, Random.rotation);
            else
            {
                s = GameObject.CreatePrimitive(PrimitiveType.Cube);
                s.transform.SetPositionAndRotation(p, Random.rotation);
                s.transform.localScale = Vector3.one * Random.Range(0.06f, 0.14f);
                Destroy(s.GetComponent<Collider>());
            }
            s.name = "OH_PartShard";
            s.layer = 2;
            foreach (Collider c in s.GetComponentsInChildren<Collider>())
                Destroy(c);
            BoxCollider box = s.AddComponent<BoxCollider>();
            box.size = Vector3.one * 0.12f / Mathf.Max(0.01f, s.transform.lossyScale.x);
            Rigidbody rb = s.AddComponent<Rigidbody>();
            rb.mass = 0.3f;
            rb.AddForce((Random.onUnitSphere + Vector3.up * 0.8f).normalized * speed * Random.Range(0.6f, 1.2f), ForceMode.VelocityChange);
            rb.AddTorque(Random.insideUnitSphere * 20f, ForceMode.VelocityChange);
            Destroy(s, 6f + Random.value * 2f);
        }
    }
}
