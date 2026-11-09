using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(BoxCollider))]
public sealed class BossSwordWaveProjectile : MonoBehaviour
{
    // 연출용 초승달 검기 프리팹 (Assets/Resources/VFX/BossSwordWave.prefab). 없으면 기존 큐브로 폴백.
    const string VisualResourcePath = "VFX/BossSwordWave";
    // 시각적 폭만 살짝 넓힘 (히트박스는 그대로)
    const float VisualWidthScale = 1.2f;
    const float SpawnGrowDuration = 0.08f;
    const float SpawnStartWidthFactor = 0.55f;
    const float FadeOutDuration = 0.18f;

    static Material s_sharedMaterial;
    static GameObject s_visualPrefab;
    static bool s_visualPrefabResolved;
    static readonly Stack<BossSwordWaveProjectile> s_pool = new Stack<BossSwordWaveProjectile>();
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

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
    bool _inPool;

    BoxCollider _hitbox;
    Transform _visual;
    bool _usesAuthoredVisual;
    Vector3 _visualFullScale;
    Renderer[] _fadeRenderers;
    Color[] _fadeBaseColors;
    ParticleSystem[] _particles;
    MaterialPropertyBlock _propertyBlock;
    float _lastAppliedFade = -1f;

    /// <summary>
    /// 풀에서 재사용하거나 새로 생성. 반환 후 반드시 Configure 호출.
    /// </summary>
    public static BossSwordWaveProjectile Spawn(Vector3 position, Quaternion rotation, int layer)
    {
        BossSwordWaveProjectile projectile = null;
        // 씬 언로드로 파괴된 인스턴스는 건너뜀
        while (projectile == null && s_pool.Count > 0)
            projectile = s_pool.Pop();

        if (projectile == null)
        {
            GameObject projectileObject = new GameObject("BossSwordWaveProjectile");
            projectileObject.layer = layer;
            projectileObject.transform.SetPositionAndRotation(position, rotation);
            projectile = projectileObject.AddComponent<BossSwordWaveProjectile>();
            return projectile;
        }

        projectile._inPool = false;
        GameObject go = projectile.gameObject;
        go.layer = layer;
        go.transform.SetPositionAndRotation(position, rotation);
        go.SetActive(true);
        return projectile;
    }

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
        _inPool = false;

        EnsureRuntimeSetup(width, height, length);
    }

    void Awake()
    {
        _hitbox = GetComponent<BoxCollider>();
        _hitbox.isTrigger = true;

        // 트랜스폼으로 이동하는 트리거 → 키네마틱 리지드바디 (정적 콜라이더 트리 재구성 방지)
        Rigidbody body = GetComponent<Rigidbody>();
        if (body == null)
            body = gameObject.AddComponent<Rigidbody>();
        body.isKinematic = true;
        body.useGravity = false;
        body.interpolation = RigidbodyInterpolation.None;
        body.collisionDetectionMode = CollisionDetectionMode.Discrete;
    }

    // 기존(리지드바디 없음) 동작 유지: 정적 콜라이더와는 트리거 이벤트가 발생하지 않았으므로 무시
    static bool WouldHaveTriggeredWithoutRigidbody(Collider other)
    {
        return other.attachedRigidbody != null || other is CharacterController;
    }

    void Update()
    {
        _remainingLife -= Time.deltaTime;
        if (_remainingLife <= 0f)
        {
            ReturnToPool();
            return;
        }

        transform.position += transform.forward * (_speed * Time.deltaTime);
        UpdateVisualAnimation();
    }

    void OnTriggerEnter(Collider other)
    {
        if (_inPool || _hasResolvedHit || other == null)
            return;

        if (!WouldHaveTriggeredWithoutRigidbody(other))
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
            ReturnToPool();
            return;
        }

        if (!other.isTrigger)
            ReturnToPool();
    }

    void ReturnToPool()
    {
        if (_inPool)
            return;

        _inPool = true;
        _owner = null;
        gameObject.SetActive(false);
        s_pool.Push(this);
    }

    static GameObject ResolveVisualPrefab()
    {
        if (!s_visualPrefabResolved)
        {
            s_visualPrefabResolved = true;
            s_visualPrefab = Resources.Load<GameObject>(VisualResourcePath);
        }

        return s_visualPrefab;
    }

    void EnsureRuntimeSetup(float width, float height, float length)
    {
        if (_hitbox == null)
            _hitbox = GetComponent<BoxCollider>();

        _hitbox.size = new Vector3(width, height, length);
        _hitbox.center = new Vector3(0f, 0f, length * 0.5f);

        if (_visual == null)
            CreateVisual();

        if (_usesAuthoredVisual)
        {
            // 프리팹 메시는 X[-0.5,0.5] Y[-0.5,0.5] Z[0,1] 정규화 → 히트박스 크기로 스케일
            _visual.localPosition = Vector3.zero;
            _visual.localRotation = Quaternion.identity;
            _visualFullScale = new Vector3(width * VisualWidthScale, height, length);
            _lastAppliedFade = -1f;
            UpdateVisualAnimation();
            RestartParticles();
            return;
        }

        _visual.localPosition = new Vector3(0f, 0f, length * 0.5f);
        _visual.localRotation = Quaternion.identity;
        _visual.localScale = new Vector3(width, height, length);

        Renderer renderer = _visual.GetComponent<Renderer>();
        if (renderer != null)
        {
            if (s_sharedMaterial == null)
            {
                Color waveColor = new Color(1f, 0.25f, 0.2f, 0.9f);
                Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
                if (shader == null)
                    shader = Shader.Find("Unlit/Color");
                if (shader == null)
                    shader = Shader.Find("Sprites/Default");
                if (shader == null)
                    shader = Shader.Find("Standard");

                s_sharedMaterial = new Material(shader);
                if (s_sharedMaterial.HasProperty("_BaseColor"))
                    s_sharedMaterial.SetColor("_BaseColor", waveColor);
                if (s_sharedMaterial.HasProperty("_Color"))
                    s_sharedMaterial.SetColor("_Color", waveColor);
            }

            renderer.sharedMaterial = s_sharedMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }
    }

    void CreateVisual()
    {
        GameObject prefab = ResolveVisualPrefab();
        if (prefab != null)
        {
            GameObject visualObject = Instantiate(prefab, transform, false);
            visualObject.name = "Visual";
            // 연출 프리팹에 콜라이더가 섞여 있어도 판정에 영향 없도록 제거
            Collider[] stray = visualObject.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < stray.Length; i++)
                Destroy(stray[i]);

            _visual = visualObject.transform;
            _usesAuthoredVisual = true;
            _particles = visualObject.GetComponentsInChildren<ParticleSystem>(true);

            MeshRenderer[] meshRenderers = visualObject.GetComponentsInChildren<MeshRenderer>(true);
            _fadeRenderers = meshRenderers;
            _fadeBaseColors = new Color[meshRenderers.Length];
            for (int i = 0; i < meshRenderers.Length; i++)
            {
                Material material = meshRenderers[i].sharedMaterial;
                _fadeBaseColors[i] = material != null && material.HasProperty(BaseColorId)
                    ? material.GetColor(BaseColorId)
                    : Color.white;
            }

            _propertyBlock = new MaterialPropertyBlock();
            return;
        }

        GameObject primitive = GameObject.CreatePrimitive(PrimitiveType.Cube);
        primitive.name = "Visual";
        primitive.transform.SetParent(transform, false);
        Destroy(primitive.GetComponent<Collider>());
        _visual = primitive.transform;
        _usesAuthoredVisual = false;
    }

    void RestartParticles()
    {
        if (_particles == null)
            return;

        for (int i = 0; i < _particles.Length; i++)
        {
            ParticleSystem ps = _particles[i];
            if (ps == null)
                continue;
            ps.Clear(false);
            ps.Play(false);
        }
    }

    void UpdateVisualAnimation()
    {
        if (!_usesAuthoredVisual || _visual == null)
            return;

        // 발사 직후 좌우로 펼쳐지는 연출
        float age = _lifeTime - _remainingLife;
        float grow = Mathf.Clamp01(age / SpawnGrowDuration);
        grow = grow * grow * (3f - 2f * grow);
        float widthFactor = Mathf.Lerp(SpawnStartWidthFactor, 1f, grow);
        _visual.localScale = new Vector3(_visualFullScale.x * widthFactor, _visualFullScale.y, _visualFullScale.z);

        // 수명 끝에서 페이드아웃
        float fade = Mathf.Clamp01(_remainingLife / FadeOutDuration);
        if (Mathf.Abs(fade - _lastAppliedFade) < 0.01f || _fadeRenderers == null)
            return;

        _lastAppliedFade = fade;
        for (int i = 0; i < _fadeRenderers.Length; i++)
        {
            Renderer renderer = _fadeRenderers[i];
            if (renderer == null)
                continue;

            Color color = _fadeBaseColors[i];
            color.a *= fade;
            renderer.GetPropertyBlock(_propertyBlock);
            _propertyBlock.SetColor(BaseColorId, color);
            renderer.SetPropertyBlock(_propertyBlock);
        }
    }
}
