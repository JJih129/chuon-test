using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
[DefaultExecutionOrder(50)]
public class CameraOcclusionFader : MonoBehaviour
{
    const int HitBufferSize = 64;
    const float ReferenceResolveInterval = 0.5f;

    [Header("참조")]
    [SerializeField] private Camera targetCamera;
    [SerializeField] private Transform targetAnchor;
    [SerializeField] private PlayerReferences playerReferences;
    [SerializeField] private PlayerLockOn playerLockOn;

    [Header("가림 탐지")]
    [SerializeField] private LayerMask occluderMask = ~0;
    [SerializeField, Min(0.01f)] private float sphereRadius = 0.32f;
    [SerializeField, Min(0f)] private float extraDistancePadding = 0.15f;
    [SerializeField] private bool ignoreCharacterTargets = true;
    [SerializeField] private bool ignoreCurrentLockTarget = true;

    [Header("투명화")]
    [SerializeField, Range(0.05f, 1f)] private float occludedAlpha = 0.18f;
    [SerializeField, Min(0.1f)] private float fadeOutSpeed = 12f;
    [SerializeField, Min(0.1f)] private float fadeInSpeed = 8f;
    [SerializeField] private bool useShadowsOnlyFallback = true;
    [SerializeField, Min(1f / 120f)] private float occlusionRefreshInterval = 1f / 30f;
    [SerializeField, Min(0f)] private float cameraMovementRefreshThreshold = 0.04f;
    [SerializeField, Min(0f)] private float targetMovementRefreshThreshold = 0.03f;

    readonly RaycastHit[] _hitBuffer = new RaycastHit[HitBufferSize];
    readonly HashSet<Renderer> _currentOccluders = new HashSet<Renderer>();
    readonly Dictionary<Renderer, OccluderState> _activeStates = new Dictionary<Renderer, OccluderState>(16);
    readonly List<Renderer> _cleanupBuffer = new List<Renderer>(16);
    readonly Dictionary<int, Renderer> _colliderRendererCache = new Dictionary<int, Renderer>(64);
    readonly Dictionary<int, bool> _characterRootCache = new Dictionary<int, bool>(32);
    readonly Dictionary<Renderer, OccluderState> _stateCache = new Dictionary<Renderer, OccluderState>(16);
    const int StateCachePruneThreshold = 32;

    Transform _playerRoot;
    float _nextReferenceResolveAt;
    float _nextOcclusionRefreshAt;
    Vector3 _lastOcclusionOrigin;
    Vector3 _lastOcclusionTarget;
    bool _hasOcclusionSample;

    sealed class OccluderState
    {
        public Renderer Renderer;
        public MaterialFadeState[] Materials;
        // 원본 sharedMaterials / 투명화용 복제본 (렌더러별 1회 생성 후 재사용)
        public Material[] OriginalSharedMaterials;
        public Material[] FadeSharedMaterials;
        public Material[] OwnedClones;
        public ShadowCastingMode OriginalShadowCastingMode;
        public float CurrentAlpha = 1f;
        public float TargetAlpha = 1f;
        public bool UsesShadowFallback;
        public bool FadeMaterialsApplied;

        public void Activate()
        {
            CurrentAlpha = 1f;
            TargetAlpha = 1f;
            UsesShadowFallback = false;

            if (Renderer == null)
                return;

            OriginalShadowCastingMode = Renderer.shadowCastingMode;
            if (Materials != null && FadeSharedMaterials != null && !FadeMaterialsApplied)
            {
                Renderer.sharedMaterials = FadeSharedMaterials;
                FadeMaterialsApplied = true;
            }
        }

        public void DestroyClones()
        {
            if (OwnedClones == null)
                return;

            for (int i = 0; i < OwnedClones.Length; i++)
            {
                if (OwnedClones[i] != null)
                    UnityEngine.Object.Destroy(OwnedClones[i]);
            }

            OwnedClones = null;
            FadeSharedMaterials = null;
            Materials = null;
        }

        public bool Update(float deltaTime, float fadeOutSpeed, float fadeInSpeed, bool useShadowsOnlyFallback)
        {
            float speed = TargetAlpha < CurrentAlpha ? fadeOutSpeed : fadeInSpeed;
            CurrentAlpha = Mathf.MoveTowards(CurrentAlpha, TargetAlpha, speed * deltaTime);

            bool canFade = Materials != null && Materials.Length > 0;
            if (canFade)
            {
                for (int i = 0; i < Materials.Length; i++)
                    Materials[i].Apply(CurrentAlpha);
            }
            else if (useShadowsOnlyFallback)
            {
                UsesShadowFallback = CurrentAlpha < 0.98f;
                Renderer.shadowCastingMode = UsesShadowFallback ? ShadowCastingMode.ShadowsOnly : OriginalShadowCastingMode;
            }

            bool restored = TargetAlpha >= 0.999f && CurrentAlpha >= 0.999f;
            if (restored)
            {
                Restore();
                return true;
            }

            return false;
        }

        public void Restore()
        {
            // 원본 sharedMaterials 로 되돌림 (복제본은 캐시에 유지, 키워드 재전환 없음)
            if (FadeMaterialsApplied && Renderer != null && OriginalSharedMaterials != null)
                Renderer.sharedMaterials = OriginalSharedMaterials;
            FadeMaterialsApplied = false;

            if (Renderer != null)
                Renderer.shadowCastingMode = OriginalShadowCastingMode;

            UsesShadowFallback = false;
            CurrentAlpha = 1f;
            TargetAlpha = 1f;
        }
    }

    sealed class MaterialFadeState
    {
        readonly Material _material;
        readonly string _colorProperty;
        readonly Color _originalColor;
        readonly int _originalRenderQueue;
        readonly bool _hadSurface;
        readonly float _originalSurface;
        readonly bool _hadSurfaceType;
        readonly float _originalSurfaceType;
        readonly bool _hadMode;
        readonly float _originalMode;
        readonly bool _hadSrcBlend;
        readonly float _originalSrcBlend;
        readonly bool _hadDstBlend;
        readonly float _originalDstBlend;
        readonly bool _hadZWrite;
        readonly float _originalZWrite;
        readonly bool _hadBlend;
        readonly float _originalBlend;
        readonly bool _isStandardShader;

        public MaterialFadeState(Material material, string colorProperty)
        {
            _material = material;
            _colorProperty = colorProperty;
            _originalColor = material.GetColor(colorProperty);
            _originalRenderQueue = material.renderQueue;

            _hadSurface = material.HasProperty("_Surface");
            _originalSurface = _hadSurface ? material.GetFloat("_Surface") : 0f;
            _hadSurfaceType = material.HasProperty("_SurfaceType");
            _originalSurfaceType = _hadSurfaceType ? material.GetFloat("_SurfaceType") : 0f;
            _hadMode = material.HasProperty("_Mode");
            _originalMode = _hadMode ? material.GetFloat("_Mode") : 0f;
            _hadSrcBlend = material.HasProperty("_SrcBlend");
            _originalSrcBlend = _hadSrcBlend ? material.GetFloat("_SrcBlend") : 0f;
            _hadDstBlend = material.HasProperty("_DstBlend");
            _originalDstBlend = _hadDstBlend ? material.GetFloat("_DstBlend") : 0f;
            _hadZWrite = material.HasProperty("_ZWrite");
            _originalZWrite = _hadZWrite ? material.GetFloat("_ZWrite") : 0f;
            _hadBlend = material.HasProperty("_Blend");
            _originalBlend = _hadBlend ? material.GetFloat("_Blend") : 0f;

            string shaderName = material.shader != null ? material.shader.name : string.Empty;
            _isStandardShader = shaderName.Contains("Standard");

            ConfigureTransparent();
        }

        public void Apply(float alpha)
        {
            if (_material == null)
                return;

            Color color = _material.GetColor(_colorProperty);
            color.a = alpha;
            _material.SetColor(_colorProperty, color);
        }

        public void Restore()
        {
            if (_material == null)
                return;

            _material.SetColor(_colorProperty, _originalColor);

            if (_hadSurface)
                _material.SetFloat("_Surface", _originalSurface);
            if (_hadSurfaceType)
                _material.SetFloat("_SurfaceType", _originalSurfaceType);
            if (_hadMode)
                _material.SetFloat("_Mode", _originalMode);
            if (_hadBlend)
                _material.SetFloat("_Blend", _originalBlend);
            if (_hadSrcBlend)
                _material.SetFloat("_SrcBlend", _originalSrcBlend);
            if (_hadDstBlend)
                _material.SetFloat("_DstBlend", _originalDstBlend);
            if (_hadZWrite)
                _material.SetFloat("_ZWrite", _originalZWrite);

            _material.renderQueue = _originalRenderQueue;

            if (_isStandardShader)
            {
                ApplyStandardKeywords((int)_originalMode);
            }
            else
            {
                if (_hadSurface && Mathf.Approximately(_originalSurface, 0f))
                    _material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
                if (_hadSurfaceType && Mathf.Approximately(_originalSurfaceType, 0f))
                    _material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            }
        }

        void ConfigureTransparent()
        {
            if (_material == null)
                return;

            if (_hadSurface)
            {
                _material.SetFloat("_Surface", 1f);
                _material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            }

            if (_hadSurfaceType)
            {
                _material.SetFloat("_SurfaceType", 1f);
                _material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            }

            if (_hadBlend)
                _material.SetFloat("_Blend", 0f);

            if (_hadSrcBlend)
                _material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            if (_hadDstBlend)
                _material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            if (_hadZWrite)
                _material.SetFloat("_ZWrite", 0f);

            if (_hadMode)
            {
                _material.SetFloat("_Mode", 2f);
                ApplyStandardKeywords(2);
            }

            _material.renderQueue = (int)RenderQueue.Transparent;
        }

        void ApplyStandardKeywords(int mode)
        {
            _material.DisableKeyword("_ALPHATEST_ON");
            _material.DisableKeyword("_ALPHABLEND_ON");
            _material.DisableKeyword("_ALPHAPREMULTIPLY_ON");

            switch (mode)
            {
                case 1:
                    _material.EnableKeyword("_ALPHATEST_ON");
                    break;
                case 2:
                    _material.EnableKeyword("_ALPHABLEND_ON");
                    break;
                case 3:
                    _material.EnableKeyword("_ALPHAPREMULTIPLY_ON");
                    break;
            }
        }
    }

    void Reset()
    {
        TryAutoResolveReferences(true);
    }

    void Awake()
    {
        TryAutoResolveReferences(true);
    }

    void OnEnable()
    {
        TryAutoResolveReferences(true);
        _hasOcclusionSample = false;
    }

    void OnDisable()
    {
        if (_playerHidden)
            SetPlayerHidden(false);
        RestoreAllOccluders();
        _hasOcclusionSample = false;
    }

    void LateUpdate()
    {
        if (NeedsReferenceResolve())
            TryAutoResolveReferences();

        if (targetCamera == null || targetAnchor == null)
            return;

        Vector3 origin = targetCamera.transform.position;
        Vector3 target = targetAnchor.position;
        bool shouldRefresh = ShouldRefreshOccluders(origin, target);
        if (shouldRefresh)
            RefreshOccluders(origin, target);

        if (!shouldRefresh && _activeStates.Count == 0)
            return;

        UpdateActiveStates(Time.unscaledDeltaTime);
    }

    bool NeedsReferenceResolve()
    {
        return targetCamera == null
            || targetAnchor == null
            || playerReferences == null
            || (playerReferences == null && playerLockOn == null);
    }

    void TryAutoResolveReferences(bool force = false)
    {
        if (!force && !NeedsReferenceResolve())
            return;

        if (!force && Time.unscaledTime < _nextReferenceResolveAt)
            return;

        AutoResolveReferences();
        _nextReferenceResolveAt = Time.unscaledTime + ReferenceResolveInterval;
    }

    void AutoResolveReferences()
    {
        if (targetCamera == null)
            targetCamera = GetComponent<Camera>().OrNull() ?? GameplaySceneCache.ResolveMainCamera();

        if (playerReferences == null)
            playerReferences = GameplaySceneCache.ResolvePlayerReferences();

        if (playerLockOn == null)
            playerLockOn = GameplaySceneCache.ResolvePlayerLockOn();

        if (playerReferences != null)
        {
            _playerRoot = playerReferences.PlayerRoot;

            if (targetAnchor == null)
                targetAnchor = playerReferences.LockPivot != null ? playerReferences.LockPivot : playerReferences.PlayerRoot;
        }
        else if (_playerRoot == null)
        {
            _playerRoot = playerLockOn != null ? playerLockOn.transform.root : null;
        }
    }

    bool ShouldRefreshOccluders(Vector3 origin, Vector3 target)
    {
        if (!_hasOcclusionSample)
            return true;

        float cameraThreshold = Mathf.Max(0.08f, cameraMovementRefreshThreshold);
        if ((origin - _lastOcclusionOrigin).sqrMagnitude >= cameraThreshold * cameraThreshold)
            return true;

        float targetThreshold = Mathf.Max(0.06f, targetMovementRefreshThreshold);
        if ((target - _lastOcclusionTarget).sqrMagnitude >= targetThreshold * targetThreshold)
            return true;

        return Time.unscaledTime >= _nextOcclusionRefreshAt;
    }

    void RefreshOccluders(Vector3 origin, Vector3 target)
    {
        _hasOcclusionSample = true;
        _lastOcclusionOrigin = origin;
        _lastOcclusionTarget = target;
        _nextOcclusionRefreshAt = Time.unscaledTime + Mathf.Max(1f / 20f, occlusionRefreshInterval);
        _currentOccluders.Clear();

        Vector3 delta = target - origin;
        float distance = delta.magnitude;
        if (distance <= 0.0001f)
        {
            foreach (KeyValuePair<Renderer, OccluderState> pair in _activeStates)
                pair.Value.TargetAlpha = 1f;
            return;
        }

        Vector3 direction = delta / distance;
        int hitCount = Physics.SphereCastNonAlloc(
            origin,
            sphereRadius,
            direction,
            _hitBuffer,
            distance + extraDistancePadding,
            occluderMask,
            QueryTriggerInteraction.Ignore);

        for (int i = 0; i < hitCount; i++)
        {
            Renderer renderer = ResolveOccluderRenderer(_hitBuffer[i].collider);
            if (renderer != null)
                _currentOccluders.Add(renderer);
        }

        foreach (Renderer renderer in _currentOccluders)
        {
            if (!_activeStates.TryGetValue(renderer, out OccluderState state))
            {
                state = CreateState(renderer);
                if (state == null)
                    continue;

                _activeStates.Add(renderer, state);
            }

            state.TargetAlpha = occludedAlpha;
        }

        foreach (KeyValuePair<Renderer, OccluderState> pair in _activeStates)
        {
            OccluderState state = pair.Value;
            if (!_currentOccluders.Contains(pair.Key))
                state.TargetAlpha = 1f;
        }
    }

    // ---- 카메라 근접 플레이어 숨김 (SoulsCameraRig: 카메라가 플레이어 가슴 0.8 m 안으로 붙을 때)
    // 플레이어 툰 셰이더는 _BaseColor 투명 전환 시 단색(시안)으로 깨지므로 머티리얼 페이드 대신 '그림자만' 렌더로 숨긴다.
    // 히스테리시스: alpha < 0.5 이면 숨김, > 0.7 이면 복원.
    float _playerProximityAlpha = 1f;
    bool _playerHidden;
    Renderer[] _playerRenderers;
    ShadowCastingMode[] _playerShadowModes;

    /// <summary>1 = 보통, 작을수록 카메라가 플레이어에 붙은 상태. 0.5 미만이면 플레이어를 그림자만 남기고 숨긴다.</summary>
    public void SetPlayerProximityFade(float alpha)
    {
        _playerProximityAlpha = Mathf.Clamp01(alpha);
        bool hide = _playerHidden ? _playerProximityAlpha < 0.7f : _playerProximityAlpha < 0.5f;
        if (hide != _playerHidden)
            SetPlayerHidden(hide);
    }

    void SetPlayerHidden(bool hide)
    {
        if (hide && _playerRoot == null)
            TryAutoResolveReferences(true);
        if (hide && _playerRoot == null)
            return;

        if (hide && (_playerRenderers == null || _playerRenderers.Length == 0 || _playerRenderers[0] == null))
        {
            List<Renderer> list = new List<Renderer>(8);
            foreach (Renderer r in _playerRoot.GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer)
                    continue;
                list.Add(r);
            }
            _playerRenderers = list.ToArray();
            _playerShadowModes = new ShadowCastingMode[_playerRenderers.Length];
        }

        if (_playerRenderers == null)
            return;

        for (int i = 0; i < _playerRenderers.Length; i++)
        {
            Renderer r = _playerRenderers[i];
            if (r == null)
                continue;
            if (hide)
            {
                _playerShadowModes[i] = r.shadowCastingMode;
                r.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
            }
            else
            {
                r.shadowCastingMode = _playerShadowModes[i];
            }
        }
        _playerHidden = hide;
    }

    void UpdateActiveStates(float deltaTime)
    {
        _cleanupBuffer.Clear();
        foreach (KeyValuePair<Renderer, OccluderState> pair in _activeStates)
        {
            OccluderState state = pair.Value;

            if (state.Update(deltaTime, fadeOutSpeed, fadeInSpeed, useShadowsOnlyFallback))
                _cleanupBuffer.Add(pair.Key);
        }

        for (int i = 0; i < _cleanupBuffer.Count; i++)
            _activeStates.Remove(_cleanupBuffer[i]);
    }

    Renderer ResolveOccluderRenderer(Collider hitCollider)
    {
        if (hitCollider == null)
            return null;

        Transform root = hitCollider.transform.root;
        if (root == null)
            return null;

        if (_playerRoot != null && root == _playerRoot)
            return null;

        if (ignoreCurrentLockTarget && playerLockOn != null && playerLockOn.CurrentTarget != null && root == playerLockOn.CurrentTarget.root)
            return null;

        if (ignoreCharacterTargets && IsCharacterRoot(root))
            return null;

        int colliderId = hitCollider.GetInstanceID();
        if (!_colliderRendererCache.TryGetValue(colliderId, out Renderer renderer) || renderer == null)
        {
            renderer = hitCollider.GetComponent<Renderer>();
            if (renderer == null)
                renderer = hitCollider.GetComponentInParent<Renderer>();

            _colliderRendererCache[colliderId] = renderer;
        }

        if (renderer == null || !renderer.enabled)
            return null;

        if (renderer is ParticleSystemRenderer || renderer is TrailRenderer || renderer is LineRenderer)
            return null;

        return renderer;
    }

    bool IsCharacterRoot(Transform root)
    {
        if (root == null)
            return false;

        int rootId = root.GetInstanceID();
        if (_characterRootCache.TryGetValue(rootId, out bool cached))
            return cached;

        bool isCharacterRoot = root.GetComponent<IDamageReceiver>() != null || root.GetComponent<IHealth>() != null;
        _characterRootCache[rootId] = isCharacterRoot;
        return isCharacterRoot;
    }

    OccluderState CreateState(Renderer renderer)
    {
        if (renderer == null)
            return null;

        // 이전에 만든 복제본이 있으면 재사용
        if (_stateCache.TryGetValue(renderer, out OccluderState cachedState) && cachedState != null)
        {
            cachedState.Activate();
            return cachedState;
        }

        PruneStateCache();

        // renderer.materials 대신 직접 복제해 소유권을 추적 (복원 시 원본 sharedMaterials 로 교체)
        Material[] originals = renderer.sharedMaterials;
        Material[] fadeMaterials = new Material[originals.Length];
        List<MaterialFadeState> fadeStates = new List<MaterialFadeState>(originals.Length);
        List<Material> ownedClones = new List<Material>(originals.Length);
        for (int i = 0; i < originals.Length; i++)
        {
            Material original = originals[i];
            fadeMaterials[i] = original;
            if (original == null)
                continue;

            string colorProperty = ResolveColorProperty(original);
            if (string.IsNullOrEmpty(colorProperty))
                continue;

            Material clone = new Material(original);
            fadeMaterials[i] = clone;
            ownedClones.Add(clone);
            fadeStates.Add(new MaterialFadeState(clone, colorProperty));
        }

        OccluderState state = new OccluderState
        {
            Renderer = renderer,
            Materials = fadeStates.Count > 0 ? fadeStates.ToArray() : null,
            OriginalSharedMaterials = originals,
            FadeSharedMaterials = fadeStates.Count > 0 ? fadeMaterials : null,
            OwnedClones = ownedClones.Count > 0 ? ownedClones.ToArray() : null,
            OriginalShadowCastingMode = renderer.shadowCastingMode
        };

        _stateCache[renderer] = state;
        state.Activate();
        return state;
    }

    void PruneStateCache()
    {
        if (_stateCache.Count < StateCachePruneThreshold)
            return;

        _cleanupBuffer.Clear();
        foreach (KeyValuePair<Renderer, OccluderState> pair in _stateCache)
        {
            if (pair.Key == null)
                _cleanupBuffer.Add(pair.Key);
        }

        for (int i = 0; i < _cleanupBuffer.Count; i++)
        {
            if (_stateCache.TryGetValue(_cleanupBuffer[i], out OccluderState state))
                state?.DestroyClones();
            _stateCache.Remove(_cleanupBuffer[i]);
        }

        _cleanupBuffer.Clear();
    }

    void DestroyCachedClones()
    {
        foreach (KeyValuePair<Renderer, OccluderState> pair in _stateCache)
            pair.Value?.DestroyClones();

        _stateCache.Clear();
    }

    void OnDestroy()
    {
        RestoreAllOccluders();
        DestroyCachedClones();
    }

    string ResolveColorProperty(Material material)
    {
        if (material == null)
            return null;

        if (material.HasProperty("_BaseColor"))
            return "_BaseColor";

        if (material.HasProperty("_Color"))
            return "_Color";

        return null;
    }

    void RestoreAllOccluders()
    {
        foreach (KeyValuePair<Renderer, OccluderState> pair in _activeStates)
            pair.Value.Restore();

        _activeStates.Clear();
        _cleanupBuffer.Clear();
        _currentOccluders.Clear();
        _colliderRendererCache.Clear();
        _characterRootCache.Clear();
        _hasOcclusionSample = false;
    }
}
