using System;
using System.Collections;
using System.Collections.Generic;
using Combat;
using UnityEngine;
using UnityEngine.Rendering;

#if UNITY_EDITOR
using UnityEditor;
#endif

[DisallowMultipleComponent]
public sealed class PlayerAttackVfxPresenter : MonoBehaviour
{
    [Serializable]
    public struct AttackVfxEntry
    {
        public AttackData attackData;
        public GameObject hitRangeVfxPrefab;
        public GameObject weaponTrailVfxPrefab;
        public Vector3 localPositionOffset;
        public Vector3 localEulerOffset;
        public Vector3 localScaleMultiplier;
        public bool parentRangeVfxToHitbox;
        public bool scaleRangeVfxToHitbox;
        public float fallbackLifetime;
    }

    [Header("참조")]
    [SerializeField] PlayerReferences playerReferences;
    [SerializeField] PlayerCombatController combatController;
    [SerializeField] Transform weaponSocketOverride;
    [SerializeField] Transform swordTrailAnchorOverride;

    [Header("기본 VFX")]
    [SerializeField] GameObject defaultHitRangeVfxPrefab;
    [SerializeField] GameObject defaultWeaponTrailVfxPrefab;
    [SerializeField] bool useProceduralTrailFallback = true;
    [SerializeField] bool useWeaponSocketForTrail = true;
    [SerializeField] bool preferSwordMeshTrailAnchor = true;
    [SerializeField] float defaultFallbackLifetime = 0.25f;
    [SerializeField] float minRespawnGap = 0.03f;

    [Header("프로시저럴 트레일")]
    [SerializeField] Color trailStartColor = new Color(1f, 0.95f, 0.9f, 0.95f);
    [SerializeField] Color trailEndColor = new Color(1f, 0.45f, 0.2f, 0f);
    [SerializeField] float trailTime = 0.12f;
    [SerializeField] float trailStartWidth = 0.12f;
    [SerializeField] float trailEndWidth = 0.02f;
    [SerializeField] float trailMinVertexDistance = 0.02f;
    [SerializeField] Vector3 proceduralTrailLocalOffset = Vector3.zero;
    [SerializeField] float swordTipForwardPadding = 0.03f;

    [Header("프로시저럴 범위 플래시")]
    [SerializeField] bool useProceduralRangeFlashFallback = false;
    [SerializeField] Color rangeFlashColor = new Color(1f, 0.6f, 0.18f, 0.2f);
    [SerializeField] Vector3 rangeFlashScalePadding = new Vector3(1.05f, 1.05f, 1.1f);
    [SerializeField] float rangeFlashLifetime = 0.12f;

    [Header("공격별 오버라이드")]
    [SerializeField] AttackVfxEntry[] attackVfxEntries = Array.Empty<AttackVfxEntry>();

    [Header("디버그")]
    [SerializeField] bool debugLog;

    AttackData _currentAttackData;
    AttackInput _currentInput;
    int _currentComboDepth;
    float _lastSpawnTime = float.NegativeInfinity;
    TrailRenderer _proceduralTrail;
    Material _proceduralTrailMaterial;
    Transform _activeTrailAnchor;
    Transform _resolvedSwordTrailAnchor;
    GameObject _proceduralRangeFlashObject;
    MeshRenderer _proceduralRangeFlashRenderer;
    Material _proceduralRangeFlashMaterial;
    float _proceduralRangeFlashHideAt = float.NegativeInfinity;
    Coroutine _rangeFlashHideRoutine;
    readonly Dictionary<AttackData, AttackVfxEntry> _entryByAttack = new Dictionary<AttackData, AttackVfxEntry>();
    bool _entryLookupDirty = true;
    Collider _cachedRangeSizeCollider;
    Vector3 _cachedRangeSize;

    public void Initialize(PlayerReferences references, PlayerCombatController controller)
    {
        if (references != null)
            playerReferences = references;

        if (controller != null)
            combatController = controller;

        AutoWire();
    }

    void Awake()
    {
        AutoWire();
#if UNITY_EDITOR
        EnsureDefaultMappings(force: false);
#endif
    }

    void OnDestroy()
    {
        if (_proceduralTrailMaterial != null)
            Destroy(_proceduralTrailMaterial);

        if (_proceduralRangeFlashMaterial != null)
            Destroy(_proceduralRangeFlashMaterial);

        if (_proceduralRangeFlashObject != null)
            Destroy(_proceduralRangeFlashObject);
    }

#if UNITY_EDITOR
    void Reset()
    {
        AutoWire();
        EnsureDefaultMappings(force: true);
        MarkEntryLookupDirty();
    }

    void OnValidate()
    {
        if (Application.isPlaying)
            return;

        AutoWire();
        EnsureDefaultMappings(force: false);
        MarkEntryLookupDirty();
    }
#endif

    public void NotifyAttackStarted(AttackData attackData, int comboDepth, AttackInput input)
    {
        _currentAttackData = attackData;
        _currentComboDepth = comboDepth;
        _currentInput = input;
    }

    public void NotifyAttackEnded()
    {
        _currentAttackData = null;
        _currentComboDepth = 0;
        DisableProceduralTrail();
    }

    public void StopAttackWindow()
    {
        DisableProceduralTrail();
    }

    public void PlayAttackWindow(AttackData attackData, AttackHitbox hitbox, AttackHitWindow? hitWindow, int comboDepth, AttackInput input)
    {
        if (attackData == null)
            return;

        AutoWire();

        _currentAttackData = attackData;
        _currentComboDepth = comboDepth;
        _currentInput = input;

        if (Time.time - _lastSpawnTime < Mathf.Max(0f, minRespawnGap))
            return;

        _lastSpawnTime = Time.time;

        AttackVfxEntry entry = ResolveEntry(attackData);
        Transform trailAnchor = ResolveTrailAnchor(hitbox);
        Transform rangeAnchor = ResolveRangeAnchor(hitbox, trailAnchor);

        if (entry.weaponTrailVfxPrefab != null || defaultWeaponTrailVfxPrefab != null)
            SpawnWeaponTrail(entry, trailAnchor);
        else if (useProceduralTrailFallback)
            EnableProceduralTrail(trailAnchor);

        GameObject rangePrefab = entry.hitRangeVfxPrefab != null ? entry.hitRangeVfxPrefab : defaultHitRangeVfxPrefab;
        if (rangePrefab == null)
        {
            if (useProceduralRangeFlashFallback && hitbox != null)
                ShowProceduralRangeFlash(entry, hitbox);
            return;
        }

        if (rangeAnchor == null)
            return;

        float lifetime = entry.fallbackLifetime > 0.01f ? entry.fallbackLifetime : defaultFallbackLifetime;
        Transform parent = entry.parentRangeVfxToHitbox && hitbox != null ? hitbox.transform : rangeAnchor;
        Quaternion rotation = rangeAnchor.rotation * Quaternion.Euler(entry.localEulerOffset);
        GameObject spawned = TransientVfxPool.Spawn(rangePrefab, rangeAnchor.position, rotation, parent, lifetime);
        if (spawned == null)
            return;

        if (parent != null)
        {
            spawned.transform.localPosition = entry.localPositionOffset;
            spawned.transform.localRotation = Quaternion.Euler(entry.localEulerOffset);
        }
        else
        {
            spawned.transform.position += rangeAnchor.TransformVector(entry.localPositionOffset);
        }

        Vector3 baseScale = ResolveBaseScale(entry, hitbox);
        spawned.transform.localScale = baseScale;

        if (debugLog)
            Debug.Log($"[PlayerAttackVfxPresenter] attack={attackData.name}, combo={comboDepth}, input={input}, prefab={rangePrefab.name}", this);
    }

    void AutoWire()
    {
        if (playerReferences == null)
            playerReferences = GetComponent<PlayerReferences>() ?? GetComponentInParent<PlayerReferences>(true);

        if (combatController == null)
            combatController = GetComponent<PlayerCombatController>() ?? GetComponentInParent<PlayerCombatController>(true);
    }

    AttackVfxEntry ResolveEntry(AttackData attackData)
    {
        RebuildEntryLookupIfNeeded();

        if (_entryByAttack.Count == 0)
            return BuildFallbackEntry();

        if (attackData != null && _entryByAttack.TryGetValue(attackData, out AttackVfxEntry entry))
            return entry;

        return BuildFallbackEntry();
    }

    AttackVfxEntry BuildFallbackEntry()
    {
        return CompleteEntry(new AttackVfxEntry
        {
            attackData = _currentAttackData,
            hitRangeVfxPrefab = defaultHitRangeVfxPrefab,
            weaponTrailVfxPrefab = defaultWeaponTrailVfxPrefab,
            localPositionOffset = Vector3.zero,
            localEulerOffset = Vector3.zero,
            localScaleMultiplier = Vector3.one,
            parentRangeVfxToHitbox = true,
            scaleRangeVfxToHitbox = true,
            fallbackLifetime = defaultFallbackLifetime
        });
    }

    static AttackVfxEntry CompleteEntry(AttackVfxEntry entry)
    {
        if (entry.localScaleMultiplier == Vector3.zero)
            entry.localScaleMultiplier = Vector3.one;
        return entry;
    }

    void SpawnWeaponTrail(AttackVfxEntry entry, Transform trailAnchor)
    {
        GameObject prefab = entry.weaponTrailVfxPrefab != null ? entry.weaponTrailVfxPrefab : defaultWeaponTrailVfxPrefab;
        if (prefab == null || trailAnchor == null)
            return;

        float lifetime = entry.fallbackLifetime > 0.01f ? entry.fallbackLifetime : defaultFallbackLifetime;
        GameObject spawned = TransientVfxPool.Spawn(prefab, trailAnchor.position, trailAnchor.rotation, trailAnchor, lifetime);
        if (spawned == null)
            return;

        spawned.transform.localPosition = entry.localPositionOffset;
        spawned.transform.localRotation = Quaternion.Euler(entry.localEulerOffset);
        spawned.transform.localScale = entry.localScaleMultiplier;
    }

    void EnableProceduralTrail(Transform trailAnchor)
    {
        if (trailAnchor == null)
            return;

        EnsureProceduralTrail(trailAnchor);
        if (_proceduralTrail == null)
            return;

        if (_activeTrailAnchor != trailAnchor)
        {
            _activeTrailAnchor = trailAnchor;
            _proceduralTrail.transform.SetParent(trailAnchor, false);
            _proceduralTrail.transform.localPosition = proceduralTrailLocalOffset;
            _proceduralTrail.transform.localRotation = Quaternion.identity;
            _proceduralTrail.transform.localScale = Vector3.one;
        }

        _proceduralTrail.Clear();
        _proceduralTrail.emitting = true;
        _proceduralTrail.gameObject.SetActive(true);
    }

    void DisableProceduralTrail()
    {
        if (_proceduralTrail == null)
            return;

        _proceduralTrail.emitting = false;
    }

    void EnsureProceduralTrail(Transform trailAnchor)
    {
        if (_proceduralTrail != null)
            return;

        if (_proceduralTrailMaterial == null)
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
                return;

            _proceduralTrailMaterial = new Material(shader);
            _proceduralTrailMaterial.name = "PlayerAttackTrailRuntime";
            _proceduralTrailMaterial.hideFlags = HideFlags.HideAndDontSave;
        }

        GameObject trailObject = new GameObject("RuntimeAttackTrail");
        trailObject.transform.SetParent(trailAnchor, false);
        trailObject.transform.localPosition = proceduralTrailLocalOffset;
        trailObject.transform.localRotation = Quaternion.identity;

        _proceduralTrail = trailObject.AddComponent<TrailRenderer>();
        _proceduralTrail.material = _proceduralTrailMaterial;
        _proceduralTrail.time = Mathf.Max(0.01f, trailTime);
        _proceduralTrail.startWidth = Mathf.Max(0.01f, trailStartWidth);
        _proceduralTrail.endWidth = Mathf.Max(0f, trailEndWidth);
        _proceduralTrail.minVertexDistance = Mathf.Max(0.001f, trailMinVertexDistance);
        _proceduralTrail.shadowCastingMode = ShadowCastingMode.Off;
        _proceduralTrail.receiveShadows = false;
        _proceduralTrail.alignment = LineAlignment.View;
        _proceduralTrail.textureMode = LineTextureMode.Stretch;
        _proceduralTrail.numCornerVertices = 2;
        _proceduralTrail.numCapVertices = 1;
        _proceduralTrail.emitting = false;
        _proceduralTrail.generateLightingData = false;

        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(trailStartColor, 0f),
                new GradientColorKey(Color.Lerp(trailStartColor, trailEndColor, 0.45f), 0.35f),
                new GradientColorKey(trailEndColor, 1f)
            },
            new[]
            {
                new GradientAlphaKey(trailStartColor.a, 0f),
                new GradientAlphaKey(Mathf.Lerp(trailStartColor.a, trailEndColor.a, 0.4f), 0.4f),
                new GradientAlphaKey(trailEndColor.a, 1f)
            });

        _proceduralTrail.colorGradient = gradient;
        _activeTrailAnchor = trailAnchor;
    }

    void ShowProceduralRangeFlash(AttackVfxEntry entry, AttackHitbox hitbox)
    {
        if (hitbox == null || hitbox.Collider == null)
            return;

        EnsureProceduralRangeFlash(hitbox.transform);
        if (_proceduralRangeFlashObject == null)
            return;

        Vector3 size = ResolveColliderLocalSize(hitbox.Collider);
        Vector3 multiplier = entry.localScaleMultiplier == Vector3.zero ? Vector3.one : entry.localScaleMultiplier;
        Vector3 scale = Vector3.Scale(Vector3.Scale(size, rangeFlashScalePadding), multiplier);

        _proceduralRangeFlashObject.transform.SetParent(hitbox.transform, false);
        _proceduralRangeFlashObject.transform.localPosition = entry.localPositionOffset;
        _proceduralRangeFlashObject.transform.localRotation = Quaternion.Euler(entry.localEulerOffset);
        _proceduralRangeFlashObject.transform.localScale = new Vector3(
            Mathf.Max(0.08f, scale.x),
            Mathf.Max(0.08f, scale.y),
            Mathf.Max(0.08f, scale.z));

        _proceduralRangeFlashObject.SetActive(true);
        _proceduralRangeFlashHideAt = Time.time + Mathf.Max(0.02f, rangeFlashLifetime);
        if (_rangeFlashHideRoutine != null)
            StopCoroutine(_rangeFlashHideRoutine);
        _rangeFlashHideRoutine = StartCoroutine(HideProceduralRangeFlashAfterDelay());
    }

    void EnsureProceduralRangeFlash(Transform anchor)
    {
        if (_proceduralRangeFlashObject != null)
            return;

        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null)
            return;

        _proceduralRangeFlashMaterial = new Material(shader);
        _proceduralRangeFlashMaterial.name = "PlayerAttackRangeFlashRuntime";
        _proceduralRangeFlashMaterial.hideFlags = HideFlags.HideAndDontSave;
        _proceduralRangeFlashMaterial.color = rangeFlashColor;

        _proceduralRangeFlashObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
        _proceduralRangeFlashObject.name = "RuntimeAttackRangeFlash";
        _proceduralRangeFlashObject.transform.SetParent(anchor, false);
        _proceduralRangeFlashObject.transform.localPosition = Vector3.zero;
        _proceduralRangeFlashObject.transform.localRotation = Quaternion.identity;
        _proceduralRangeFlashObject.SetActive(false);

        Collider flashCollider = _proceduralRangeFlashObject.GetComponent<Collider>();
        if (flashCollider != null)
            Destroy(flashCollider);

        _proceduralRangeFlashRenderer = _proceduralRangeFlashObject.GetComponent<MeshRenderer>();
        if (_proceduralRangeFlashRenderer != null)
        {
            _proceduralRangeFlashRenderer.sharedMaterial = _proceduralRangeFlashMaterial;
            _proceduralRangeFlashRenderer.shadowCastingMode = ShadowCastingMode.Off;
            _proceduralRangeFlashRenderer.receiveShadows = false;
        }
    }

    Transform ResolveTrailAnchor(AttackHitbox hitbox)
    {
        if (swordTrailAnchorOverride != null)
            return swordTrailAnchorOverride;

        if (preferSwordMeshTrailAnchor)
        {
            Transform swordAnchor = ResolveSwordTrailAnchor();
            if (swordAnchor != null)
                return swordAnchor;
        }

        if (!useWeaponSocketForTrail && hitbox != null)
            return hitbox.transform;

        if (weaponSocketOverride != null)
            return weaponSocketOverride;

        if (playerReferences != null && playerReferences.WeaponSocket != null)
            return playerReferences.WeaponSocket;

        return hitbox != null ? hitbox.transform : transform;
    }

    Transform ResolveSwordTrailAnchor()
    {
        if (_resolvedSwordTrailAnchor != null)
            return _resolvedSwordTrailAnchor;

        Transform swordTransform = FindSwordVisualTransform();
        if (swordTransform == null)
            return null;

        _resolvedSwordTrailAnchor = CreateSwordTipAnchor(swordTransform);
        return _resolvedSwordTrailAnchor;
    }

    Transform FindSwordVisualTransform()
    {
        Transform searchRoot = playerReferences != null && playerReferences.VisualRoot != null
            ? playerReferences.VisualRoot
            : transform;

        Transform byName = FindChildRecursive(searchRoot, "Object002");
        if (byName != null)
            return byName;

        string[] preferredNames =
        {
            "sword",
            "weapon_r",
            "sword_holder",
            "9CG_Sword(Clone)"
        };

        for (int i = 0; i < preferredNames.Length; i++)
        {
            Transform candidate = FindChildRecursive(searchRoot, preferredNames[i]);
            if (candidate != null)
                return candidate;
        }

        return null;
    }

    Transform CreateSwordTipAnchor(Transform swordTransform)
    {
        const string anchorName = "RuntimeSwordTrailAnchor";
        Transform existing = swordTransform.Find(anchorName);
        if (existing != null)
            return existing;

        Bounds localBounds;
        if (!TryGetLocalMeshBounds(swordTransform, out localBounds))
            return swordTransform;

        Vector3 size = localBounds.size;
        int axis = 0;
        if (size.y > size.x && size.y >= size.z)
            axis = 1;
        else if (size.z > size.x && size.z >= size.y)
            axis = 2;

        Vector3 localPosition = localBounds.center;
        switch (axis)
        {
            case 1:
                localPosition.y = localBounds.max.y + swordTipForwardPadding;
                break;
            case 2:
                localPosition.z = localBounds.max.z + swordTipForwardPadding;
                break;
            default:
                localPosition.x = localBounds.max.x + swordTipForwardPadding;
                break;
        }

        GameObject anchorObject = new GameObject(anchorName);
        Transform anchor = anchorObject.transform;
        anchor.SetParent(swordTransform, false);
        anchor.localPosition = localPosition;
        anchor.localRotation = Quaternion.identity;
        anchor.localScale = Vector3.one;
        return anchor;
    }

    static bool TryGetLocalMeshBounds(Transform target, out Bounds bounds)
    {
        MeshFilter meshFilter = target.GetComponent<MeshFilter>();
        if (meshFilter != null && meshFilter.sharedMesh != null)
        {
            bounds = meshFilter.sharedMesh.bounds;
            return true;
        }

        SkinnedMeshRenderer skinned = target.GetComponent<SkinnedMeshRenderer>();
        if (skinned != null && skinned.sharedMesh != null)
        {
            bounds = skinned.sharedMesh.bounds;
            return true;
        }

        bounds = default;
        return false;
    }

    static Transform FindChildRecursive(Transform root, string name)
    {
        if (root == null || string.IsNullOrEmpty(name))
            return null;

        if (string.Equals(root.name, name, StringComparison.OrdinalIgnoreCase))
            return root;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = FindChildRecursive(root.GetChild(i), name);
            if (found != null)
                return found;
        }

        return null;
    }

    Transform ResolveRangeAnchor(AttackHitbox hitbox, Transform trailAnchor)
    {
        if (hitbox != null)
            return hitbox.transform;

        return trailAnchor != null ? trailAnchor : transform;
    }

    Vector3 ResolveBaseScale(AttackVfxEntry entry, AttackHitbox hitbox)
    {
        Vector3 multiplier = entry.localScaleMultiplier == Vector3.zero ? Vector3.one : entry.localScaleMultiplier;
        if (hitbox == null || hitbox.Collider == null || !entry.scaleRangeVfxToHitbox)
            return multiplier;

        Vector3 size = ResolveColliderLocalSize(hitbox.Collider);
        size.x = Mathf.Max(0.1f, size.x);
        size.y = Mathf.Max(0.1f, size.y);
        size.z = Mathf.Max(0.1f, size.z);
        return Vector3.Scale(size, multiplier);
    }

    Vector3 ResolveColliderLocalSize(Collider collider)
    {
        if (collider == _cachedRangeSizeCollider)
            return _cachedRangeSize;

        Vector3 resolvedSize;
        switch (collider)
        {
            case BoxCollider box:
                resolvedSize = box.size;
                break;
            case SphereCollider sphere:
                float diameter = sphere.radius * 2f;
                resolvedSize = new Vector3(diameter, diameter, diameter);
                break;
            case CapsuleCollider capsule:
                float diameterCapsule = capsule.radius * 2f;
                switch (capsule.direction)
                {
                    case 0: resolvedSize = new Vector3(capsule.height, diameterCapsule, diameterCapsule); break;
                    case 2: resolvedSize = new Vector3(diameterCapsule, diameterCapsule, capsule.height); break;
                    default: resolvedSize = new Vector3(diameterCapsule, capsule.height, diameterCapsule); break;
                }
                break;
            case MeshCollider mesh when mesh.sharedMesh != null:
                resolvedSize = mesh.sharedMesh.bounds.size;
                break;
            default:
                resolvedSize = collider.bounds.size;
                break;
        }

        _cachedRangeSizeCollider = collider;
        _cachedRangeSize = resolvedSize;
        return resolvedSize;
    }

    IEnumerator HideProceduralRangeFlashAfterDelay()
    {
        while (_proceduralRangeFlashObject != null && Time.time < _proceduralRangeFlashHideAt)
            yield return null;

        if (_proceduralRangeFlashObject != null)
            _proceduralRangeFlashObject.SetActive(false);

        _rangeFlashHideRoutine = null;
    }

    void MarkEntryLookupDirty()
    {
        _entryLookupDirty = true;
    }

    void RebuildEntryLookupIfNeeded()
    {
        if (!_entryLookupDirty)
            return;

        _entryByAttack.Clear();

        if (attackVfxEntries != null)
        {
            for (int i = 0; i < attackVfxEntries.Length; i++)
            {
                AttackVfxEntry entry = CompleteEntry(attackVfxEntries[i]);
                if (entry.attackData != null && !_entryByAttack.ContainsKey(entry.attackData))
                    _entryByAttack.Add(entry.attackData, entry);
            }
        }

        _entryLookupDirty = false;
    }

#if UNITY_EDITOR
    void EnsureDefaultMappings(bool force)
    {
        if (!force && attackVfxEntries != null && attackVfxEntries.Length > 0)
            return;

        defaultHitRangeVfxPrefab = null;

        defaultWeaponTrailVfxPrefab = null;

        string[] attackAssetPaths =
        {
            @"Assets/Attack_data/AD_L1.asset",
            @"Assets/Attack_data/Combo_A/AD_L2.asset",
            @"Assets/Attack_data/Combo_A/AD_L3.asset",
            @"Assets/Attack_data/Combo_A/AD_L4.asset",
            @"Assets/Attack_data/AD_H1.asset",
            @"Assets/Attack_data/Combo_E/AD_H2.asset",
            @"Assets/Attack_data/Combo_E/AD_H3.asset",
            @"Assets/Attack_data/Combo_B/AD_H4_B.asset",
            @"Assets/Attack_data/Combo_B/AD_H5_B.asset"
        };

        GameObject trailPrefab = defaultWeaponTrailVfxPrefab;

        AttackVfxEntry[] defaults = new AttackVfxEntry[attackAssetPaths.Length];
        for (int i = 0; i < attackAssetPaths.Length; i++)
        {
            AttackData attackData = AssetDatabase.LoadAssetAtPath<AttackData>(attackAssetPaths[i]);
            Vector3 scale = Vector3.one;
            float lifetime = 0.2f;

            defaults[i] = new AttackVfxEntry
            {
                attackData = attackData,
                hitRangeVfxPrefab = null,
                weaponTrailVfxPrefab = trailPrefab,
                localPositionOffset = Vector3.zero,
                localEulerOffset = Vector3.zero,
                localScaleMultiplier = scale,
                parentRangeVfxToHitbox = false,
                scaleRangeVfxToHitbox = false,
                fallbackLifetime = lifetime
            };
        }

        attackVfxEntries = defaults;
        MarkEntryLookupDirty();
        EditorUtility.SetDirty(this);
    }
#endif
}
