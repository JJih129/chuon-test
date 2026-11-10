// 반복 16: 봉쇄 프로토콜 벽 전원 노드 (Blender 모델, TSUKASA 레드 발광). 명중 4회(강공 2) 로 파괴.
// IDamageReceiver + IHealth 라 플레이어 무기 판정·락온 대상이 된다 (보스 피격 콜라이더와 같은 레이어).
using System;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class BossPowerNode : MonoBehaviour, IDamageReceiver, IHealth
{
    BossLockdownGimmick _owner;
    int _integrity;
    int _max;
    bool _dead;
    Light _glow;
    Renderer[] _renderers;
    MaterialPropertyBlock _block;
    float _flashUntil;
    float _lastHitAt = float.NegativeInfinity;

    public int MaxHP => _max;
    public int CurrentHP => _integrity;
    public bool IsDead => _dead;
    public bool IsStaggered => false;
    public bool isInvincible { get; set; }
    public event Action<int, int> OnHPChanged;
    public event Action<int, int> OnHealthChanged { add { } remove { } }
    public event Action<int> OnDamaged { add { } remove { } }
    public event Action<int, HitType> OnDamagedWithType { add { } remove { } }
    public event Action OnDied;
    public static int QaDestroyed { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => QaDestroyed = 0;

    static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    public static BossPowerNode Create(Vector3 position, Quaternion rotation, int integrity, int layer, TypeDOverhaulAssets assets, BossLockdownGimmick owner)
    {
        GameObject root = new GameObject("OH_PowerNode");
        root.transform.SetPositionAndRotation(position, rotation);
        root.layer = layer;
        if (assets != null && assets.powerNodeModel != null)
        {
            GameObject model = Instantiate(assets.powerNodeModel, root.transform);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            foreach (Collider c in model.GetComponentsInChildren<Collider>())
                Destroy(c);
        }
        else
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(cube.GetComponent<Collider>());
            cube.transform.SetParent(root.transform, false);
            cube.transform.localScale = new Vector3(0.8f, 1.1f, 0.45f);
        }
        BoxCollider box = root.AddComponent<BoxCollider>();
        box.center = new Vector3(0f, 0f, 0.05f);
        box.size = new Vector3(1.0f, 1.3f, 0.7f);

        var lightGo = new GameObject("Glow");
        lightGo.transform.SetParent(root.transform, false);
        lightGo.transform.localPosition = new Vector3(0f, 0f, 0.55f);
        Light l = lightGo.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = new Color(1f, 0.16f, 0.1f);
        l.range = 4.5f;
        l.intensity = 3.2f;
        l.shadows = LightShadows.None;

        BossPowerNode node = root.AddComponent<BossPowerNode>();
        node._owner = owner;
        node._max = Mathf.Max(1, integrity);
        node._integrity = node._max;
        node._glow = l;
        node._renderers = root.GetComponentsInChildren<Renderer>();
        node._block = new MaterialPropertyBlock();
        return node;
    }

    public void ReceiveHit(HitPayload payload)
    {
        if (_dead)
            return;
        if (payload.attacker == null || payload.attacker.GetComponentInParent<PlayerReferences>() == null)
            return;
        // 같은 휘두르기의 중복 판정 방지
        if (Time.time - _lastHitAt < 0.12f)
            return;
        _lastHitAt = Time.time;
        int amount = payload.hitType == HitType.Heavy || payload.hitType == HitType.Strong || payload.hitType == HitType.Force ? 2 : 1;
        _integrity = Mathf.Max(0, _integrity - amount);
        OnHPChanged?.Invoke(_integrity, _max);
        _flashUntil = Time.time + 0.12f;
        TypeDOverhaulAssets a = TypeDOverhaulAssets.Load();
        if (a != null)
        {
            TypeDOverhaulAssets.PlayAt(a.nodeHit, payload.hitPoint, 0.85f, UnityEngine.Random.Range(0.94f, 1.06f));
            if (a.sparkVfx != null) TransientVfxPool.Spawn(a.sparkVfx, payload.hitPoint, transform.rotation, null, 0.8f);
        }
        CombatRewardUtility.TryGrantBasicAttackGauge(payload.attacker);
        if (_integrity <= 0)
            Break();
    }

    void Break()
    {
        _dead = true;
        QaDestroyed++;
        Collider c = GetComponent<Collider>();
        if (c != null)
            c.enabled = false;
        TypeDOverhaulAssets a = TypeDOverhaulAssets.Load();
        if (a != null)
        {
            TypeDOverhaulAssets.PlayAt(a.nodeBreak, transform.position, 1f, 1f);
            if (a.sparkVfx != null) TransientVfxPool.Spawn(a.sparkVfx, transform.position + transform.forward * 0.3f, transform.rotation, null, 1.6f);
            if (a.powerNodeBrokenModel != null)
            {
                for (int i = transform.childCount - 1; i >= 0; i--)
                {
                    Transform ch = transform.GetChild(i);
                    if (ch.GetComponent<Light>() == null)
                        Destroy(ch.gameObject);
                }
                GameObject broken = Instantiate(a.powerNodeBrokenModel, transform);
                broken.transform.localPosition = Vector3.zero;
                broken.transform.localRotation = Quaternion.identity;
                foreach (Collider bc in broken.GetComponentsInChildren<Collider>())
                    Destroy(bc);
                _renderers = GetComponentsInChildren<Renderer>();
            }
        }
        ActionColourDirector.PulseAt(transform.position, 0.5f, 0.25f);
        CameraShake cs = GameplaySceneCache.ResolveMainCameraShake();
        if (cs != null)
            cs.Shake(0.18f, 0.18f);
        OnDied?.Invoke();
        if (_owner != null)
            _owner.NotifyNodeDestroyed(this);
    }

    void Update()
    {
        if (_glow == null)
            return;
        float target = _dead ? 0f : 3.2f * (0.8f + 0.2f * Mathf.Sin(Time.time * 4.2f)) * (Time.time < _flashUntil ? 2.2f : 1f);
        _glow.intensity = Mathf.MoveTowards(_glow.intensity, target, Time.deltaTime * (_dead ? 3f : 40f));
        if (_renderers == null || _block == null)
            return;
        float flash = Time.time < _flashUntil ? 1f : 0f;
        if (flash <= 0f && !_flashApplied)
            return;
        _flashApplied = flash > 0f;
        for (int i = 0; i < _renderers.Length; i++)
        {
            Renderer r = _renderers[i];
            if (r == null)
                continue;
            if (flash > 0f)
            {
                _block.Clear();
                _block.SetColor(EmissionId, new Color(4f, 3.4f, 3.2f));
                r.SetPropertyBlock(_block);
            }
            else
                r.SetPropertyBlock(null);
        }
    }

    bool _flashApplied;

    // IHealth (락온 판정용 최소 구현)
    public void Heal(int amount) { }
    public void ApplyDamage(float amount) { }
    public void ApplyDamage(int amount) { }
    public void TakeDamage(int damage, HitType hitType, Vector3 hitPoint) { }
    public void Kill() { if (!_dead) { _integrity = 0; Break(); } }
    public void SetInvincible(float duration) { }
}
