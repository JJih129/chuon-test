using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(BoxCollider))]
public sealed class BossSwordWaveProjectile : MonoBehaviour
{
    static Material s_sharedMaterial;

    Transform _owner;
    float _damage;
    float _speed;
    float _lifeTime;
    float _remainingLife;
    bool _canParry;
    bool _canPerfectDodge;
    bool _canGuard;
    bool _causesGuardBreak;
    bool _unblockable;
    bool _hasResolvedHit;

    BoxCollider _hitbox;
    Transform _visual;

    public void Configure(
        Transform owner,
        float damage,
        float speed,
        float lifeTime,
        float width,
        float height,
        float length,
        bool canParry,
        bool canPerfectDodge,
        bool canGuard,
        bool causesGuardBreak,
        bool unblockable)
    {
        _owner = owner;
        _damage = Mathf.Max(0f, damage);
        _speed = Mathf.Max(0.1f, speed);
        _lifeTime = Mathf.Max(0.1f, lifeTime);
        _remainingLife = _lifeTime;
        _canParry = canParry;
        _canPerfectDodge = canPerfectDodge;
        _canGuard = canGuard;
        _causesGuardBreak = causesGuardBreak;
        _unblockable = unblockable;
        _hasResolvedHit = false;

        EnsureRuntimeSetup(width, height, length);
    }

    void Awake()
    {
        _hitbox = GetComponent<BoxCollider>();
        _hitbox.isTrigger = true;
    }

    void Update()
    {
        _remainingLife -= Time.deltaTime;
        if (_remainingLife <= 0f)
        {
            Destroy(gameObject);
            return;
        }

        transform.position += transform.forward * (_speed * Time.deltaTime);
    }

    void OnTriggerEnter(Collider other)
    {
        if (_hasResolvedHit || other == null)
            return;

        if (_owner != null && other.transform.IsChildOf(_owner))
            return;

        IDamageReceiver receiver = other.GetComponentInParent<IDamageReceiver>();
        if (receiver != null)
        {
            _hasResolvedHit = true;
            receiver.ReceiveHit(new HitPayload
            {
                damage = _damage,
                hitType = HitType.Strong,
                hitPoint = other.ClosestPoint(transform.position),
                hitDirection = transform.forward,
                attacker = _owner,
                canParry = _canParry,
                canPerfectDodge = _canPerfectDodge,
                canGuard = _canGuard,
                causesGuardBreak = _causesGuardBreak,
                unblockable = _unblockable
            });
            Destroy(gameObject);
            return;
        }

        if (!other.isTrigger)
            Destroy(gameObject);
    }

    void EnsureRuntimeSetup(float width, float height, float length)
    {
        if (_hitbox == null)
            _hitbox = GetComponent<BoxCollider>();

        _hitbox.size = new Vector3(width, height, length);
        _hitbox.center = new Vector3(0f, 0f, length * 0.5f);

        if (_visual == null)
        {
            GameObject visualObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            visualObject.name = "Visual";
            visualObject.transform.SetParent(transform, false);
            Destroy(visualObject.GetComponent<Collider>());
            _visual = visualObject.transform;
        }

        _visual.localPosition = new Vector3(0f, 0f, length * 0.5f);
        _visual.localRotation = Quaternion.identity;
        _visual.localScale = new Vector3(width, height, length);

        Renderer renderer = _visual.GetComponent<Renderer>();
        if (renderer != null)
        {
            if (s_sharedMaterial == null)
            {
                Shader shader = Shader.Find("Unlit/Color");
                if (shader == null)
                    shader = Shader.Find("Sprites/Default");
                if (shader == null)
                    shader = Shader.Find("Standard");

                s_sharedMaterial = new Material(shader);
                if (s_sharedMaterial.HasProperty("_Color"))
                    s_sharedMaterial.color = new Color(1f, 0.25f, 0.2f, 0.9f);
            }

            renderer.sharedMaterial = s_sharedMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }
    }
}
