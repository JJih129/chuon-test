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

    [Serializable]
    sealed class AttackVfxProfileEntry
    {
        [Tooltip("Attack key called from Animation Events. Example: Attack_01")]
        public string key;

        [Tooltip("Profile used for this attack key.")]
        public AttackVfxProfile profile;
    }

    sealed class PooledVfxInstance
    {
        public readonly GameObject GameObject;
        public readonly Transform Transform;
        public readonly ParticleSystem[] Particles;
        public readonly TrailRenderer[] Trails;
        public int LeaseId;

        public PooledVfxInstance(GameObject gameObject)
        {
            GameObject = gameObject;
            Transform = gameObject != null ? gameObject.transform : null;
            Particles = gameObject != null
                ? gameObject.GetComponentsInChildren<ParticleSystem>(true)
                : Array.Empty<ParticleSystem>();
            Trails = gameObject != null
                ? gameObject.GetComponentsInChildren<TrailRenderer>(true)
                : Array.Empty<TrailRenderer>();
        }
    }

    struct BladePoseSample
    {
        public bool HasAnchor;
        public bool HasEnd;
        public bool HasEdge;
        public Vector3 AnchorPosition;
        public Vector3 EndPosition;
        public Vector3 EdgePosition;
    }

    [Header("참조")]
    [SerializeField] PlayerReferences playerReferences;
    [SerializeField] PlayerCombatController combatController;
    [SerializeField] PlayerLockOn lockOnTargetProvider;

    [Header("Animation Event VFX")]
    [Tooltip("Slash VFX spawn point. Place this around the chest/waist area in front of the player.")]
    [SerializeField] Transform slashSpawnPoint;

    [Tooltip("Direction source for slash rotation. Usually the player root or current attack direction root.")]
    [SerializeField] Transform directionRoot;

    [Tooltip("TrailRenderers attached to the weapon. Animation Events toggle these directly.")]
    [SerializeField] TrailRenderer[] weaponTrails = Array.Empty<TrailRenderer>();

    [Header("Attack VFX Profiles")]
    [Tooltip("Animation Event attack key to VFX profile mappings.")]
    [SerializeField] AttackVfxProfileEntry[] attackProfileEntries = Array.Empty<AttackVfxProfileEntry>();

    [Header("Profile VFX Pooling")]
    [Tooltip("Number of slash/impact VFX instances to prewarm per prefab.")]
    [SerializeField, Min(1)] int prewarmCount = 3;

    [Header("Unified Player Slash VFX")]
    [Tooltip("If enabled, every player attack slash/range VFX uses this prefab while keeping attack-specific offsets below.")]
    [SerializeField] bool useUnifiedSlashVfx = true;
    [SerializeField] GameObject unifiedSlashVfxPrefab;
    [Tooltip("Use the current lock-on target as the horizontal VFX direction when available.")]
    [SerializeField] bool useLockOnDirectionForSlashVfx = true;
    [Tooltip("Use Object002 blade pose/motion to roll the slash plane so it follows the actual sword arc.")]
    [SerializeField] bool useObject002BladePoseForSlashVfx = true;
    [SerializeField, Range(0f, 1f)] float object002BladeRollWeight = 1f;
    [Tooltip("Global correction for Slash_C only. Use this when the imported prefab axis does not match the weapon plane.")]
    [SerializeField] Vector3 unifiedSlashEulerOffset = Vector3.zero;
    [SerializeField] Vector3 unifiedSlashScaleMultiplier = Vector3.one;
    [Tooltip("Debug only: draw the resolved slash direction in Scene View.")]
    [SerializeField] bool debugDrawSlashDirection;

    [SerializeField] Transform weaponSocketOverride;
    [SerializeField] Transform swordTrailAnchorOverride;
    [SerializeField] Transform slashSpawnAnchorOverride;
    [SerializeField] string slashSpawnAnchorName = "SwordSlashAnchor";
    [SerializeField] Transform swordEndAnchorOverride;
    [SerializeField] string swordEndAnchorName = "SwordEnd";
    [SerializeField] Transform swordEdgeAnchorOverride;
    [SerializeField] string swordEdgeAnchorName = "SwordEdge";
    [SerializeField] bool preferSlashSpawnAnchorForRangeVfx = true;
    [SerializeField] bool orientSlashVfxToAttackDirection = true;
    [SerializeField] bool useSwordTipMotionForSlashRoll = true;
    [SerializeField] bool useSwordEndForSlashRoll = true;
    [SerializeField] bool useSwordEdgeForSlashRollSign = true;
    [SerializeField] bool useSampledBladePoseForProfileSlash = true;
    [SerializeField, Range(0f, 1f)] float swordTipMotionDirectionWeight = 0.78f;
    [SerializeField] bool invertSwordTipMotionRoll = false;
    [SerializeField, Range(2, 10)] int swordTipMotionSampleFrames = 5;
    [SerializeField, Min(0.001f)] float swordTipMotionMinDistance = 0.035f;
    [SerializeField, Range(0f, 1.5f)] float slashSpawnBladeCenterBias = 0.58f;
    [SerializeField] float slashSpawnBladeAxisOffset = 0f;
    [SerializeField] float slashSpawnHorizontalOutwardOffset = 0f;
    [SerializeField] float slashSpawnVerticalOffset = 0.22f;
    [SerializeField] float slashSpawnForwardOffset = 0.38f;
    [SerializeField] bool enforceSlashSpawnInFrontOfPlayer = true;
    [SerializeField, Min(0f)] float slashSpawnMinPlayerForwardDistance = 0.62f;
    [SerializeField] float slashSpawnPlaneRightOffset = 0.18f;
    [SerializeField] float slashSpawnPlaneUpOffset = 0.22f;

    [Header("기본 VFX")]
    [SerializeField] GameObject defaultHitRangeVfxPrefab;
    [SerializeField] GameObject defaultWeaponTrailVfxPrefab;
    [SerializeField] GameObject defaultHitImpactVfxPrefab;
    [SerializeField, Min(0.05f)] float defaultHitImpactLifetime = 0.75f;
    [SerializeField] bool useProceduralTrailFallback = true;
    [SerializeField] bool useDrakkarTrailFallback = true;
    [SerializeField] bool useWeaponSocketForTrail = true;
    [SerializeField] bool preferSwordMeshTrailAnchor = true;
    [SerializeField] float defaultFallbackLifetime = 0.25f;
    [SerializeField] float minRespawnGap = 0.03f;
    [SerializeField] Material drakkarTrailMaterial;
    [SerializeField] Texture2D drakkarLightTrailTexture;
    [SerializeField] Texture2D drakkarHeavyTrailTexture;
    [SerializeField] Texture2D drakkarWideTrailTexture;

    [Header("Auto Sword Trail Mesh")]
    [Tooltip("Use the sword-motion sampled mesh trail instead of a fixed slash anchor for weapon trail VFX.")]
    [SerializeField] bool useSwordMotionMeshTrail = true;

    [Tooltip("Optional Free Slash VFX prefab used only as a material source. It is not spawned every attack.")]
    [SerializeField] GameObject swordMotionTrailVfxPrefab;

    [Tooltip("Pre-placed static mesh trail component. This must be assigned in the scene or player prefab.")]
    [SerializeField] SwordTrailMeshRenderer swordMotionTrail;

    [Tooltip("Pre-placed static TrailBase/TrailTip provider. This must be assigned in the scene or player prefab.")]
    [SerializeField] SwordTrailPoints swordMotionTrailPoints;

    [Tooltip("Player trail tint. Red is the default player attack color.")]
    [SerializeField] Color swordMotionTrailTint = new Color(1f, 0.08f, 0.03f, 1f);

    [Tooltip("Minimum sword point movement before adding a new mesh trail sample.")]
    [SerializeField, Min(0.001f)] float swordMotionTrailMinSampleDistance = 0.02f;

    [Tooltip("How long the sampled player sword trail remains visible.")]
    [SerializeField, Min(0.01f)] float swordMotionTrailLifeTime = 0.18f;

    [Tooltip("Maximum sample count kept by the player sword trail ring buffer.")]
    [SerializeField, Range(2, 128)] int swordMotionTrailMaxSamples = 36;

    [Header("Swing Plane (iteration 10)")]
    [Tooltip("Orient the spawned slash arc to the real swing plane (blade axis x tip velocity during the hit window) instead of per-attack fixed euler offsets.")]
    [SerializeField] bool orientSlashToSwingPlane = true;
    [Tooltip("Slash spawn waits until the blade tip has travelled this far inside the hit window (m), so the swing direction is measured, not guessed.")]
    [SerializeField, Min(0.01f)] float slashSwingMinTipTravel = 0.14f;
    [Tooltip("Upper bound for that wait (s).")]
    [SerializeField, Range(0f, 0.2f)] float slashSwingMaxDelay = 0.07f;
    [Tooltip("Arc radius = blade length x this (arc centred on the hilt).")]
    [SerializeField, Range(0.5f, 2f)] float slashArcRadiusScale = 1.08f;
    [Tooltip("Sign of the prefab sweep (Slash_C sweeps local -X to +X after the unified Y180 offset). Flip only if a new prefab sweeps the other way.")]
    [SerializeField] bool flipSlashSweepSense;

    [Header("프로시저럴 트레일")]
    [SerializeField] Color trailStartColor = new Color(1f, 0.32f, 0.28f, 0.95f);
    [SerializeField] Color trailEndColor = new Color(0.85f, 0.08f, 0.08f, 0f);
    [SerializeField] float trailStartWidth = 0.14f;
    [SerializeField] float trailEndWidth = 0.02f;
    [SerializeField] float swordTipForwardPadding = 0.03f;
    [SerializeField] float swordBaseBackwardPadding = 0.01f;
    [SerializeField] bool autoSizeTrailFromSword = true;
    [SerializeField] float trailWidthMultiplier = 1.35f;
    [SerializeField] float minimumAutoTrailWidth = 0.32f;
    [SerializeField] float autoTrailEndWidthRatio = 0.24f;

    [Header("프로시저럴 범위 플래시")]
    [SerializeField] bool useProceduralRangeFlashFallback = false;
    [SerializeField] Color rangeFlashColor = new Color(1f, 0.6f, 0.18f, 0.2f);
    [SerializeField] Vector3 rangeFlashScalePadding = new Vector3(1.05f, 1.05f, 1.1f);
    [SerializeField] float rangeFlashLifetime = 0.12f;

    [Header("공격별 오버라이드")]
    [SerializeField] AttackVfxEntry[] attackVfxEntries = Array.Empty<AttackVfxEntry>();

    [Header("Heavy Attack VFX")]
    [SerializeField] Color heavyAttackVfxTint = new Color(1f, 0.12f, 0.04f, 1f);
    [SerializeField] Color lightAttackVfxTint = new Color(0.92f, 0.16f, 0.08f, 1f);

    [Header("디버그")]
    [SerializeField] bool debugLog;

    AttackData _currentAttackData;
    AttackInput _currentInput;
    int _currentComboDepth;
    float _lastSpawnTime = float.NegativeInfinity;
    DrakkarSwordTrailDriver _proceduralTrailDriver;
    Material _proceduralTrailMaterial;
    Material _proceduralLightTrailMaterial;
    Material _proceduralHeavyTrailMaterial;
    Material _proceduralWideTrailMaterial;
    Material _swordMotionTrailMaterial;
    bool _warnedMissingSwordMotionTrailSetup;
    Transform _activeTrailBaseAnchor;
    Transform _activeTrailTipAnchor;
    Transform _resolvedSlashSpawnAnchor;
    Transform _resolvedSwordEndAnchor;
    Transform _resolvedSwordEdgeAnchor;
    Transform _resolvedSwordBaseAnchor;
    Transform _resolvedSwordTrailAnchor;
    GameObject _proceduralRangeFlashObject;
    MeshRenderer _proceduralRangeFlashRenderer;
    Material _proceduralRangeFlashMaterial;
    float _proceduralRangeFlashHideAt = float.NegativeInfinity;
    Coroutine _rangeFlashHideRoutine;
    readonly Dictionary<AttackData, AttackVfxEntry> _entryByAttack = new Dictionary<AttackData, AttackVfxEntry>();
    readonly Dictionary<string, AttackVfxProfile> _attackProfileByKey = new Dictionary<string, AttackVfxProfile>();
    readonly Dictionary<GameObject, Queue<PooledVfxInstance>> _poolByPrefab = new Dictionary<GameObject, Queue<PooledVfxInstance>>();
    readonly HashSet<GameObject> _prewarmedProfilePrefabs = new HashSet<GameObject>();
    bool _entryLookupDirty = true;
    Collider _cachedRangeSizeCollider;
    Vector3 _cachedRangeSize;
    readonly Vector3[] _swordTipMotionSamples = new Vector3[12];
    int _swordTipMotionWriteIndex;
    int _swordTipMotionSampleCount;
    readonly BladePoseSample[] _bladePoseSamples = new BladePoseSample[12];
    int _bladePoseWriteIndex;
    int _bladePoseSampleCount;

    // 스윙 평면 측정용: 실제 칼날(TrailBase/TrailTip) 위치 기록 (LateUpdate, 애니메이션 이후)
    const int SwingHistoryLength = 8;
    readonly Vector3[] _swingBase = new Vector3[SwingHistoryLength];
    readonly Vector3[] _swingTip = new Vector3[SwingHistoryLength];
    int _swingWrite;
    int _swingCount;

    struct PendingSlash
    {
        public bool Active;
        public GameObject Prefab;
        public AttackVfxEntry Entry;
        public float Lifetime;
        public bool Heavy;
        public float StartTime;
        public Vector3 StartTip;
        public bool HasStartTip;
    }

    PendingSlash _pendingSlash;

    /// <summary>QA: last spawned slash (swing normal, attack forward, tip velocity) for orientation checks.</summary>
    public Vector3 QaLastSwingNormal { get; private set; }
    public Vector3 QaLastSwingVelocity { get; private set; }
    public Vector3 QaLastSlashForward { get; private set; }
    public Vector3 QaLastSlashRight { get; private set; }
    public int QaSlashSpawnCount { get; private set; }
    public float QaLastStrikeWeight { get; private set; }

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
        BuildProfileLookup();
        PrewarmPools();
        StopWeaponTrail();
        ClearWeaponTrail();
#if UNITY_EDITOR
        EnsureDefaultMappings(force: false);
#endif
    }

    void OnDestroy()
    {
        if (_proceduralTrailMaterial != null)
            Destroy(_proceduralTrailMaterial);
        if (_proceduralLightTrailMaterial != null)
            Destroy(_proceduralLightTrailMaterial);
        if (_proceduralHeavyTrailMaterial != null)
            Destroy(_proceduralHeavyTrailMaterial);
        if (_proceduralWideTrailMaterial != null)
            Destroy(_proceduralWideTrailMaterial);

        if (_proceduralRangeFlashMaterial != null)
            Destroy(_proceduralRangeFlashMaterial);

        if (_proceduralRangeFlashObject != null)
            Destroy(_proceduralRangeFlashObject);

    }

    void LateUpdate()
    {
        if (!Application.isPlaying)
            return;

        RecordSwingSample();
        if (_pendingSlash.Active)
            TrySpawnPendingSlash(force: false);

        if (!TryCaptureBladePose(out BladePoseSample pose))
            return;

        _bladePoseSamples[_bladePoseWriteIndex] = pose;
        _bladePoseWriteIndex = (_bladePoseWriteIndex + 1) % _bladePoseSamples.Length;
        _bladePoseSampleCount = Mathf.Min(_bladePoseSampleCount + 1, _bladePoseSamples.Length);

        if (!pose.HasEnd)
            return;

        _swordTipMotionSamples[_swordTipMotionWriteIndex] = pose.EndPosition;
        _swordTipMotionWriteIndex = (_swordTipMotionWriteIndex + 1) % _swordTipMotionSamples.Length;
        _swordTipMotionSampleCount = Mathf.Min(_swordTipMotionSampleCount + 1, _swordTipMotionSamples.Length);
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

    void OnDrawGizmosSelected()
    {
        if (!debugDrawSlashDirection)
            return;

        Transform origin = slashSpawnPoint != null ? slashSpawnPoint : transform;
        Vector3 forward = ResolveBaseAttackForward(null);
        Gizmos.color = Color.red;
        Gizmos.DrawLine(origin.position, origin.position + forward * 1.4f);
    }

    public void NotifyAttackStarted(AttackData attackData, int comboDepth, AttackInput input)
    {
        _currentAttackData = attackData;
        _currentComboDepth = comboDepth;
        _currentInput = input;

        AutoWire();
        if (!useSwordMotionMeshTrail && (useDrakkarTrailFallback || ShouldUseProceduralTrailFallback(attackData)))
            EnableProceduralTrail();
    }

    public void NotifyAttackEnded()
    {
        _currentAttackData = null;
        _currentComboDepth = 0;
        _pendingSlash.Active = false; // 공격이 끊겼으면 아직 안 나간 검기는 버린다
        StopSwordMotionTrail();
        DisableProceduralTrail();
    }

    public void StopAttackWindow()
    {
        // 히트 윈도우가 측정 대기보다 짧았으면 지금까지의 칼날 움직임으로 바로 낸다
        if (_pendingSlash.Active)
            TrySpawnPendingSlash(force: true);
        StopSwordMotionTrail();
        DisableProceduralTrail();
    }

    public void PlayAttackVfx(string attackKey)
    {
        if (!TryGetAttackVfxProfile(attackKey, out AttackVfxProfile profile))
            return;

        if (profile.UseWeaponTrail)
            StartWeaponTrail();

        PlaySlashInternal(profile);
        RequestFeedbackHooks(profile);
    }

    public void PlaySlash(string attackKey)
    {
        if (!TryGetAttackVfxProfile(attackKey, out AttackVfxProfile profile))
            return;

        PlaySlashInternal(profile);
    }

    public void StartWeaponTrail()
    {
        if (weaponTrails != null)
        {
            for (int i = 0; i < weaponTrails.Length; i++)
            {
                TrailRenderer trail = weaponTrails[i];
                if (trail == null)
                    continue;

                trail.Clear();
                trail.emitting = true;
            }
        }

        if (swordMotionTrail != null)
            swordMotionTrail.StartTrail();
    }

    public void StopWeaponTrail()
    {
        if (weaponTrails != null)
        {
            for (int i = 0; i < weaponTrails.Length; i++)
            {
                TrailRenderer trail = weaponTrails[i];
                if (trail != null)
                    trail.emitting = false;
            }
        }

        if (swordMotionTrail != null)
            swordMotionTrail.StopTrail();
    }

    public void ClearWeaponTrail()
    {
        if (weaponTrails != null)
        {
            for (int i = 0; i < weaponTrails.Length; i++)
            {
                TrailRenderer trail = weaponTrails[i];
                if (trail != null)
                    trail.Clear();
            }
        }

        if (swordMotionTrail != null)
            swordMotionTrail.ClearTrail();
    }

    public void PlayHitImpact(string attackKey, Vector3 hitPosition, Vector3 hitNormal)
    {
        AttackVfxProfile profile = null;
        if (!string.IsNullOrWhiteSpace(attackKey))
        {
            if (_attackProfileByKey.Count == 0)
                BuildProfileLookup();
            _attackProfileByKey.TryGetValue(attackKey, out profile);
        }

        PlayHitImpactInternal(profile, hitPosition, hitNormal);
    }

    public void PlayHitImpact(AttackData attackData, Vector3 hitPosition, Vector3 hitNormal)
    {
        PlayHitImpactInternal(null, hitPosition, hitNormal);
    }

    void PlayHitImpactInternal(AttackVfxProfile profile, Vector3 hitPosition, Vector3 hitNormal)
    {
        GameObject impactPrefab = profile != null && profile.HitImpactPrefab != null
            ? profile.HitImpactPrefab
            : defaultHitImpactVfxPrefab;
        if (impactPrefab == null)
            return;

        Quaternion rotation = hitNormal.sqrMagnitude > 0.0001f
            ? Quaternion.LookRotation(hitNormal.normalized, Vector3.up)
            : Quaternion.identity;

        float lifetime = profile != null
            ? profile.HitImpactLifetime
            : defaultHitImpactLifetime;
        TransientVfxPool.Spawn(impactPrefab, hitPosition, rotation, null, lifetime);
    }

    void PlaySlashInternal(AttackVfxProfile profile)
    {
        if (profile == null)
            return;

        AttackSlashProfile slashProfile = profile.SlashProfile;
        if (slashProfile == null)
        {
            Debug.LogWarning($"[PlayerAttackVfxPresenter] Attack VFX profile '{profile.name}' has no slash profile.", this);
            return;
        }

        GameObject slashPrefab = ResolveSlashVfxPrefab(slashProfile);
        if (slashPrefab == null)
        {
            Debug.LogWarning($"[PlayerAttackVfxPresenter] Slash profile '{slashProfile.name}' has no slash prefab.", this);
            return;
        }

        Transform spawnPoint = ResolveAnimationEventSlashSpawnPoint();
        if (spawnPoint == null)
        {
            Debug.LogWarning("[PlayerAttackVfxPresenter] Slash Spawn Point is missing. Assign slashSpawnPoint or SwordSlashAnchor.", this);
            return;
        }

        PooledVfxInstance instance = GetFromPool(slashPrefab);
        if (instance == null || instance.Transform == null)
            return;

        ResolveProfileSlashPose(spawnPoint, slashProfile, out Vector3 worldPosition, out Quaternion worldRotation);
        worldRotation = ApplyUnifiedSlashRotation(worldRotation);

        instance.LeaseId++;
        instance.Transform.SetParent(null, false);
        instance.Transform.SetPositionAndRotation(worldPosition, worldRotation);
        instance.Transform.localScale = ApplyUnifiedSlashScale(slashProfile.LocalScale);

        if (slashProfile.FollowOwner)
            instance.Transform.SetParent(spawnPoint, true);

        instance.GameObject.SetActive(true);
        ApplySpawnedVfxTint(instance.GameObject, IsHeavyAttackProfile(profile));
        PlayParticles(instance);
        StartCoroutine(ReturnAfterLifetime(slashPrefab, instance, slashProfile.Lifetime));
    }

    bool TryGetAttackVfxProfile(string attackKey, out AttackVfxProfile profile)
    {
        profile = null;
        if (string.IsNullOrWhiteSpace(attackKey))
            return false;

        if (_attackProfileByKey.Count == 0)
            BuildProfileLookup();

        if (_attackProfileByKey.TryGetValue(attackKey, out profile) && profile != null)
            return true;

        Debug.LogWarning($"[PlayerAttackVfxPresenter] Attack VFX key was not found: {attackKey}", this);
        return false;
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
        Transform rangeAnchor = ResolveRangeAnchor(hitbox, trailAnchor, entry);

        bool swordMotionTrailStarted = BeginSwordMotionTrail();

        if (!swordMotionTrailStarted && (entry.weaponTrailVfxPrefab != null || defaultWeaponTrailVfxPrefab != null))
            SpawnWeaponTrail(entry, trailAnchor);
        if (!swordMotionTrailStarted && (useDrakkarTrailFallback || ShouldUseProceduralTrailFallback()))
            EnableProceduralTrail();

        GameObject rangePrefab = ResolveRangeVfxPrefab(entry);
        if (rangePrefab == null)
        {
            if (useProceduralRangeFlashFallback && hitbox != null)
                ShowProceduralRangeFlash(entry, hitbox);
            return;
        }

        if (rangeAnchor == null)
            return;

        float lifetime = entry.fallbackLifetime > 0.01f ? entry.fallbackLifetime : defaultFallbackLifetime;

        // 스윙 평면 정렬: 히트 윈도우 안에서 칼끝이 실제로 움직인 방향을 잰 뒤(LateUpdate) 낸다
        if (orientSlashToSwingPlane && !entry.parentRangeVfxToHitbox && HasSwingPoints())
        {
            if (_pendingSlash.Active)
                TrySpawnPendingSlash(force: true);
            _pendingSlash = new PendingSlash
            {
                Active = true,
                Prefab = rangePrefab,
                Entry = entry,
                Lifetime = lifetime,
                Heavy = IsHeavyAttackVfx(attackData, input),
                StartTime = Time.time,
                HasStartTip = TryGetCurrentSwingPoints(out _, out Vector3 startTip),
                StartTip = startTip
            };
            return;
        }

        bool useDetachedSlashAnchor = IsConfiguredSlashSpawnAnchor(rangeAnchor) && !entry.parentRangeVfxToHitbox;
        Transform parent = entry.parentRangeVfxToHitbox && hitbox != null ? hitbox.transform : useDetachedSlashAnchor ? null : rangeAnchor;
        Quaternion anchorRotation = ResolveRangeVfxRotation(rangeAnchor, hitbox);
        Quaternion rotation = ApplyUnifiedSlashRotation(anchorRotation * Quaternion.Euler(entry.localEulerOffset));
        Vector3 spawnPosition = ResolveRangeVfxPosition(rangeAnchor);
        if (IsConfiguredSlashSpawnAnchor(rangeAnchor))
            spawnPosition += anchorRotation * new Vector3(slashSpawnPlaneRightOffset, slashSpawnPlaneUpOffset, 0f);

        if (parent == null)
            spawnPosition += anchorRotation * entry.localPositionOffset;

        GameObject spawned = TransientVfxPool.Spawn(rangePrefab, spawnPosition, rotation, parent, lifetime);
        if (spawned == null)
            return;

        ConfigureOneShotParticleVfx(spawned, IsHeavyAttackVfx(attackData, input));

        if (parent != null)
        {
            spawned.transform.localPosition = entry.localPositionOffset;
            spawned.transform.localRotation = Quaternion.Euler(entry.localEulerOffset);
        }
        else
        {
            spawned.transform.SetPositionAndRotation(spawnPosition, rotation);
        }

        Vector3 baseScale = ApplyUnifiedSlashScale(ResolveBaseScale(entry, hitbox));
        spawned.transform.localScale = baseScale;

        if (debugLog)
            Debug.Log($"[PlayerAttackVfxPresenter] attack={attackData.name}, combo={comboDepth}, input={input}, prefab={rangePrefab.name}, anchor={rangeAnchor.name}", this);
    }

    void ConfigureOneShotParticleVfx(GameObject spawned, bool heavyAttack)
    {
        if (spawned == null)
            return;

        ApplySpawnedVfxTint(spawned, heavyAttack);

        // TransientVfxPool 이 캐시한 배열 재사용 + 풀에서 이미 재시작했으므로 이중 Stop/Play 생략
        bool restartedByPool = TransientVfxPool.TryGetCachedParticleSystems(spawned, out ParticleSystem[] particles);
        if (!restartedByPool)
            particles = spawned.GetComponentsInChildren<ParticleSystem>(true);

        for (int i = 0; i < particles.Length; i++)
        {
            ParticleSystem particle = particles[i];
            if (particle == null)
                continue;

            if (!restartedByPool)
                particle.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);

            ParticleSystem.MainModule main = particle.main;
            if (main.loop)
                main.loop = false;

            if (!restartedByPool)
                particle.Play(false);
        }
    }

    void ApplySpawnedVfxTint(GameObject spawned, bool heavyAttack)
    {
        RuntimeVfxTintCache cache = spawned != null ? spawned.GetComponent<RuntimeVfxTintCache>() : null;
        if (cache == null && spawned != null)
            cache = spawned.AddComponent<RuntimeVfxTintCache>();

        if (cache != null)
            cache.Apply(true, heavyAttack ? heavyAttackVfxTint : lightAttackVfxTint);
    }

    bool IsHeavyAttackProfile(AttackVfxProfile profile)
    {
        if (_currentInput == AttackInput.Heavy)
            return true;

        string key = profile != null ? profile.AttackKey : string.Empty;
        return !string.IsNullOrWhiteSpace(key)
            && key.IndexOf("H", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    static bool IsHeavyAttackVfx(AttackData attackData, AttackInput input)
    {
        if (input == AttackInput.Heavy)
            return true;

        if (attackData == null)
            return false;

        // 에셋 이름 문자열 할당을 피하기 위해 AttackData 별 판정 결과 캐시
        int id = attackData.GetInstanceID();
        if (s_heavyAttackNameCache.TryGetValue(id, out bool cached))
            return cached;

        string name = attackData.name;
        bool isHeavy = !string.IsNullOrWhiteSpace(name)
            && (name.IndexOf("AD_H", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("_H", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Heavy", StringComparison.OrdinalIgnoreCase) >= 0);
        s_heavyAttackNameCache[id] = isHeavy;
        return isHeavy;
    }

    static readonly Dictionary<int, bool> s_heavyAttackNameCache = new Dictionary<int, bool>(32);

    GameObject ResolveSlashVfxPrefab(AttackSlashProfile slashProfile)
    {
        if (useUnifiedSlashVfx && unifiedSlashVfxPrefab != null)
            return unifiedSlashVfxPrefab;

        return slashProfile != null ? slashProfile.SlashPrefab : null;
    }

    GameObject ResolveRangeVfxPrefab(AttackVfxEntry entry)
    {
        if (useUnifiedSlashVfx && unifiedSlashVfxPrefab != null)
            return unifiedSlashVfxPrefab;

        return entry.hitRangeVfxPrefab != null ? entry.hitRangeVfxPrefab : defaultHitRangeVfxPrefab;
    }

    Quaternion ApplyUnifiedSlashRotation(Quaternion rotation)
    {
        if (!useUnifiedSlashVfx || unifiedSlashVfxPrefab == null)
            return rotation;

        return rotation * Quaternion.Euler(unifiedSlashEulerOffset);
    }

    Vector3 ApplyUnifiedSlashScale(Vector3 scale)
    {
        if (!useUnifiedSlashVfx || unifiedSlashVfxPrefab == null)
            return scale;

        Vector3 multiplier = unifiedSlashScaleMultiplier;
        if (multiplier == Vector3.zero)
            multiplier = Vector3.one;

        return Vector3.Scale(scale, multiplier);
    }

    void AutoWire()
    {
        if (playerReferences == null)
            playerReferences = GetComponent<PlayerReferences>().OrNull() ?? GetComponentInParent<PlayerReferences>(true);

        if (combatController == null)
            combatController = GetComponent<PlayerCombatController>().OrNull() ?? GetComponentInParent<PlayerCombatController>(true);

        if (lockOnTargetProvider == null)
            lockOnTargetProvider = GetComponent<PlayerLockOn>().OrNull() ?? GetComponentInParent<PlayerLockOn>(true);

        if (slashSpawnPoint == null)
            slashSpawnPoint = ResolveSlashSpawnAnchor();

        if (directionRoot == null)
            directionRoot = playerReferences != null && playerReferences.PlayerRoot != null ? playerReferences.PlayerRoot : transform;

        if ((weaponTrails == null || weaponTrails.Length == 0) && Application.isPlaying)
            weaponTrails = GetComponentsInChildren<TrailRenderer>(true);

        if (defaultWeaponTrailVfxPrefab == null)
            useProceduralTrailFallback = true;

    }

    bool ShouldUseProceduralTrailFallback(AttackData attackData = null)
    {
        if (!useProceduralTrailFallback)
            return false;

        GameObject attackTrailPrefab = null;
        if (attackData != null)
            attackTrailPrefab = ResolveEntry(attackData).weaponTrailVfxPrefab;

        return attackTrailPrefab == null && defaultWeaponTrailVfxPrefab == null;
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

    void EnableProceduralTrail()
    {
        if (!ResolveTrailAnchors(out Transform baseAnchor, out Transform tipAnchor))
            return;

        if (!useDrakkarTrailFallback)
            return;

        EnsureProceduralTrail(baseAnchor);
        if (_proceduralTrailDriver == null)
            return;

        _activeTrailBaseAnchor = baseAnchor;
        _activeTrailTipAnchor = tipAnchor;

        Material trailMaterial = ResolveDrakkarTrailMaterial();
        if (trailMaterial == null)
            return;

        _proceduralTrailDriver.Configure(
            baseAnchor,
            tipAnchor,
            trailMaterial,
            gameObject.layer);
        _proceduralTrailDriver.Begin();
    }

    void DisableProceduralTrail()
    {
        if (_proceduralTrailDriver == null)
            return;

        _proceduralTrailDriver.End();
    }

    bool BeginSwordMotionTrail()
    {
        if (!useSwordMotionMeshTrail)
            return false;

        if (swordMotionTrail == null || swordMotionTrailPoints == null || !swordMotionTrailPoints.HasValidPoints)
            TryResolveSwordMotionTrailAtRuntime();

        if (swordMotionTrail == null || swordMotionTrailPoints == null || !swordMotionTrailPoints.HasValidPoints)
        {
            WarnMissingSwordMotionTrailSetup();
            return false;
        }

        Material material = ResolveSwordMotionTrailMaterial();
        if (material == null)
        {
            WarnMissingSwordMotionTrailSetup();
            return false;
        }

        swordMotionTrail.Configure(
            swordMotionTrailPoints,
            material,
            swordMotionTrailTint,
            swordMotionTrailMinSampleDistance,
            swordMotionTrailLifeTime,
            swordMotionTrailMaxSamples,
            false,
            false);
        swordMotionTrail.gameObject.layer = gameObject.layer;
        float weight = ResolveStrikeWeight(_currentAttackData, _currentComboDepth, _currentInput);
        QaLastStrikeWeight = weight;
        swordMotionTrail.SetStrikeWeight(weight);
        swordMotionTrail.StartTrail();
        return true;
    }

    // 공격 무게 0(약 오프너) ~ 1(강 피니셔): 리본 폭·밝기·잔상 길이에 쓴다
    static float ResolveStrikeWeight(AttackData attackData, int comboDepth, AttackInput input)
    {
        bool heavy = IsHeavyAttackVfx(attackData, input);
        float w = heavy ? 0.72f : 0.28f;
        if (comboDepth >= (heavy ? 3 : 4))
            w += 0.22f; // 피니셔
        else if (comboDepth >= 2)
            w += 0.08f;
        if (attackData != null && attackData.attackDamageMultiplier > 1.15f)
            w += 0.06f;
        return Mathf.Clamp01(w);
    }

    // ───────── 스윙 평면 (반복 10) ─────────
    bool HasSwingPoints()
    {
        if (swordMotionTrailPoints == null || !swordMotionTrailPoints.HasValidPoints)
            TryResolveSwordMotionTrailAtRuntime();
        return swordMotionTrailPoints != null && swordMotionTrailPoints.HasValidPoints;
    }

    bool TryGetCurrentSwingPoints(out Vector3 basePosition, out Vector3 tipPosition)
    {
        basePosition = tipPosition = Vector3.zero;
        if (swordMotionTrailPoints == null || !swordMotionTrailPoints.HasValidPoints)
            return false;
        basePosition = swordMotionTrailPoints.TrailBase.position;
        tipPosition = swordMotionTrailPoints.TrailTip.position;
        return true;
    }

    void RecordSwingSample()
    {
        if (!TryGetCurrentSwingPoints(out Vector3 b, out Vector3 t))
            return;
        _swingBase[_swingWrite] = b;
        _swingTip[_swingWrite] = t;
        _swingWrite = (_swingWrite + 1) % SwingHistoryLength;
        _swingCount = Mathf.Min(_swingCount + 1, SwingHistoryLength);
    }

    void TrySpawnPendingSlash(bool force)
    {
        if (!_pendingSlash.Active)
            return;

        if (!TryGetCurrentSwingPoints(out Vector3 basePos, out Vector3 tipPos))
        {
            _pendingSlash.Active = false;
            return;
        }

        float travelled = _pendingSlash.HasStartTip ? Vector3.Distance(tipPos, _pendingSlash.StartTip) : 0f;
        bool timedOut = Time.time - _pendingSlash.StartTime >= slashSwingMaxDelay;
        if (!force && travelled < slashSwingMinTipTravel && !timedOut)
            return;

        PendingSlash pending = _pendingSlash;
        _pendingSlash.Active = false;

        Vector3 blade = tipPos - basePos;
        float bladeLength = blade.magnitude;
        if (bladeLength < 0.05f)
            return;

        // 칼끝 속도: 최근 2프레임 (없으면 윈도우 시작 이후 이동)
        Vector3 velocity = Vector3.zero;
        if (_swingCount >= 3)
        {
            int newest = (_swingWrite - 1 + SwingHistoryLength) % SwingHistoryLength;
            int older = (_swingWrite - 3 + SwingHistoryLength) % SwingHistoryLength;
            velocity = _swingTip[newest] - _swingTip[older];
        }
        if (velocity.sqrMagnitude < 0.0004f && pending.HasStartTip)
            velocity = tipPos - pending.StartTip;

        Vector3 attackForward = ResolveBaseAttackForward(null);
        Vector3 normal = Vector3.Cross(blade, velocity);
        if (normal.sqrMagnitude < 1e-6f)
            normal = Vector3.Cross(blade, Vector3.Cross(Vector3.up, attackForward)); // 거의 정지: 수평 가정
        if (normal.sqrMagnitude < 1e-6f)
            normal = Vector3.up;
        normal.Normalize();
        if (flipSlashSweepSense)
            normal = -normal;

        // 호의 중앙 = 공격 방향을 스윙 평면에 투영 (없으면 현재 칼날 방향)
        Vector3 forward = Vector3.ProjectOnPlane(attackForward, normal);
        if (forward.sqrMagnitude < 0.09f)
            forward = Vector3.ProjectOnPlane(blade, normal);
        if (forward.sqrMagnitude < 1e-6f)
            forward = attackForward;
        forward.Normalize();

        Quaternion planeRotation = Quaternion.LookRotation(forward, normal);
        Quaternion rotation = ApplyUnifiedSlashRotation(planeRotation);

        QaLastSwingNormal = normal;
        QaLastSwingVelocity = velocity;
        QaLastSlashForward = forward;
        QaLastSlashRight = planeRotation * Vector3.right;
        QaSlashSpawnCount++;

        // 호 중심 = 손잡이, 반지름 = 칼날 길이 → 검기가 칼날이 지나간 자리에 겹친다
        Vector3 multiplier = pending.Entry.localScaleMultiplier == Vector3.zero ? Vector3.one : pending.Entry.localScaleMultiplier;
        float radius = bladeLength * slashArcRadiusScale * (pending.Heavy ? 1.08f : 1f);
        Vector3 scale = ApplyUnifiedSlashScale(multiplier * radius);

        GameObject spawned = TransientVfxPool.Spawn(pending.Prefab, basePos, rotation, null, pending.Lifetime);
        if (spawned == null)
            return;

        ConfigureOneShotParticleVfx(spawned, pending.Heavy);
        spawned.transform.SetPositionAndRotation(basePos, rotation);
        spawned.transform.localScale = scale;

        if (debugLog)
            Debug.Log($"[PlayerAttackVfxPresenter] swing slash n={normal} v={velocity} fwd={forward} r={radius:F2}", this);
    }

    bool _triedRuntimeSwordTrailResolve;

    // 플레이어 비주얼(검 모델)이 런타임에 생성되는 씬에서는 프리팹에 SwordTrailPoints 참조가 비어 있다.
    // 에디터 설치 도구(SwordTrailStaticInstaller)와 같은 규칙으로 런타임에 한 번 찾아/만들어 연결한다.
    void TryResolveSwordMotionTrailAtRuntime()
    {
        if (_triedRuntimeSwordTrailResolve)
            return;
        _triedRuntimeSwordTrailResolve = true;

        Transform owner = playerReferences != null ? playerReferences.transform : transform;
        if (swordMotionTrail == null)
            swordMotionTrail = owner.GetComponentInChildren<SwordTrailMeshRenderer>(true);

        if (swordMotionTrailPoints != null && swordMotionTrailPoints.HasValidPoints)
            return;

        Transform visualRoot = playerReferences != null && playerReferences.VisualRoot != null ? playerReferences.VisualRoot : owner;
        SwordTrailPoints existing = visualRoot.GetComponentInChildren<SwordTrailPoints>(true);
        if (existing != null && existing.HasValidPoints)
        {
            swordMotionTrailPoints = existing;
            EnsureTrailPointsOrientedFromGrip(existing);
            return;
        }

        Transform sword = FindChildByNameRecursive(visualRoot, "Object002") ?? FindChildByNameRecursive(visualRoot, "Object009");
        if (sword == null || !TryGetLocalSwordMeshBounds(sword, out Bounds bounds))
            return;

        Transform basePoint = sword.Find("Player_TrailBase");
        if (basePoint == null)
        {
            basePoint = new GameObject("Player_TrailBase").transform;
            basePoint.SetParent(sword, false);
        }

        Transform tipPoint = sword.Find("Player_TrailTip");
        if (tipPoint == null)
        {
            tipPoint = new GameObject("Player_TrailTip").transform;
            tipPoint.SetParent(sword, false);
        }

        // 손잡이(그립) 기준으로 칼끝을 정한다: 메시 bounds 의 max 가 항상 칼끝은 아니다 (반대면 손잡이 페이드가 뒤집힘)
        Transform grip = GetSwordGripReference();
        if (PlayerWeaponVisualUtility.TryGetBladeWorldPose(sword, grip, out Vector3 worldBase, out Vector3 worldTip, out _))
        {
            basePoint.position = worldBase;
            tipPoint.position = worldTip;
        }
        else
        {
            basePoint.localPosition = ResolveSwordBladePoint(bounds, false);
            tipPoint.localPosition = ResolveSwordBladePoint(bounds, true);
        }

        SwordTrailPoints points = existing != null ? existing : sword.gameObject.AddComponent<SwordTrailPoints>();
        points.SetPoints(basePoint, tipPoint);
        swordMotionTrailPoints = points;
    }

    void EnsureTrailPointsOrientedFromGrip(SwordTrailPoints points)
    {
        Transform grip = GetSwordGripReference();
        if (points == null || !points.HasValidPoints || grip == null)
            return;
        float baseDist = (points.TrailBase.position - grip.position).sqrMagnitude;
        float tipDist = (points.TrailTip.position - grip.position).sqrMagnitude;
        if (baseDist > tipDist)
            points.SetPoints(points.TrailTip, points.TrailBase);
    }

    static Transform FindChildByNameRecursive(Transform root, string childName)
    {
        if (root == null)
            return null;
        if (string.Equals(root.name, childName, System.StringComparison.OrdinalIgnoreCase))
            return root;
        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = FindChildByNameRecursive(root.GetChild(i), childName);
            if (found != null)
                return found;
        }
        return null;
    }

    static bool TryGetLocalSwordMeshBounds(Transform target, out Bounds bounds)
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

    static Vector3 ResolveSwordBladePoint(Bounds bounds, bool tip)
    {
        Vector3 size = bounds.size;
        int axis = 0;
        if (size.y > size.x && size.y >= size.z)
            axis = 1;
        else if (size.z > size.x && size.z >= size.y)
            axis = 2;

        Vector3 point = bounds.center;
        if (axis == 1)
            point.y = tip ? bounds.max.y : bounds.min.y;
        else if (axis == 2)
            point.z = tip ? bounds.max.z : bounds.min.z;
        else
            point.x = tip ? bounds.max.x : bounds.min.x;
        return point;
    }

    void WarnMissingSwordMotionTrailSetup()
    {
        if (_warnedMissingSwordMotionTrailSetup)
            return;

        _warnedMissingSwordMotionTrailSetup = true;
        Debug.LogWarning("[PlayerAttackVfxPresenter] Static sword motion trail is enabled, but the scene/prefab is missing SwordTrailMeshRenderer, SwordTrailPoints, TrailBase/TrailTip, or a usable material source. Run Tools/ChuOn/VFX/Install Static Sword Trails, then verify the assigned static references.", this);
    }

    void StopSwordMotionTrail()
    {
        if (swordMotionTrail != null)
            swordMotionTrail.StopTrail();
    }

    Material ResolveSwordMotionTrailMaterial()
    {
        if (_swordMotionTrailMaterial != null)
            return _swordMotionTrailMaterial;

        _swordMotionTrailMaterial = SwordTrailVfxMaterialSource.ResolveMaterial(
            swordMotionTrailVfxPrefab,
            SwordTrailVfxMaterialSource.Palette.Player);
        return _swordMotionTrailMaterial;
    }

    void EnsureProceduralTrail(Transform baseAnchor)
    {
        if (_proceduralTrailDriver != null || baseAnchor == null)
            return;

        GameObject trailObject = new GameObject("RuntimeAttackTrail");
        trailObject.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        trailObject.transform.localScale = Vector3.one;

        _proceduralTrailDriver = trailObject.AddComponent<DrakkarSwordTrailDriver>();
        _activeTrailBaseAnchor = baseAnchor;
    }

    bool ResolveTrailAnchors(out Transform baseAnchor, out Transform tipAnchor)
    {
        tipAnchor = ResolveTrailAnchor(null);
        baseAnchor = ResolveSwordBaseAnchor();

        if (tipAnchor == null)
        {
            baseAnchor = null;
            return false;
        }

        if (baseAnchor == null)
            baseAnchor = tipAnchor;

        return true;
    }

    void ResolveDynamicTrailWidthScales(Transform trailAnchor, out float startWidthScale, out float endWidthScale)
    {
        float baseScale = Mathf.Max(1f, trailStartWidth / Mathf.Max(0.01f, minimumAutoTrailWidth));
        float endScaleFromBase = Mathf.Clamp(baseScale * Mathf.Clamp01(autoTrailEndWidthRatio), 0.02f, baseScale);
        Transform swordTransform = trailAnchor != null && trailAnchor.parent != null ? trailAnchor.parent : trailAnchor;
        if (swordTransform != null && TryGetLocalMeshBounds(swordTransform, out Bounds swordBounds))
        {
            Vector3 scaledSize = Vector3.Scale(swordBounds.size, Abs(swordTransform.lossyScale));
            float swordWorldLength = Mathf.Max(scaledSize.x, Mathf.Max(scaledSize.y, scaledSize.z));
            if (autoSizeTrailFromSword && swordWorldLength > 0.001f)
            {
                float autoWidth = Mathf.Max(minimumAutoTrailWidth, swordWorldLength * trailWidthMultiplier);
                startWidthScale = Mathf.Max(baseScale, autoWidth / Mathf.Max(0.01f, swordWorldLength));
                float worldEndWidth = Mathf.Max(trailEndWidth, autoWidth * Mathf.Clamp01(autoTrailEndWidthRatio));
                endWidthScale = Mathf.Clamp(worldEndWidth / Mathf.Max(0.01f, swordWorldLength), 0.02f, startWidthScale);
                return;
            }
        }

        startWidthScale = baseScale;
        endWidthScale = endScaleFromBase;
    }

    static Vector3 Abs(Vector3 value)
    {
        return new Vector3(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));
    }

    Material ResolveDrakkarTrailMaterial()
    {
        if (drakkarTrailMaterial != null)
            return drakkarTrailMaterial;

#if UNITY_EDITOR
        if (_currentComboDepth >= 4)
        {
            Material wideMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Effects/Trail/MAT_PlayerTrail_Wide.mat");
            if (wideMat != null)
                return wideMat;
        }

        if (_currentInput == AttackInput.Heavy)
        {
            Material heavyMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Effects/Trail/MAT_PlayerTrail_Heavy.mat");
            if (heavyMat != null)
                return heavyMat;
        }

        Material lightMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Effects/Trail/MAT_PlayerTrail_Light.mat");
        if (lightMat != null)
            return lightMat;
#endif

        EnsureDefaultDrakkarTextures();

        bool useWide = _currentComboDepth >= 4;
        if (useWide && drakkarWideTrailTexture != null)
        {
            _proceduralWideTrailMaterial = GetOrCreateDrakkarTrailMaterial(
                _proceduralWideTrailMaterial,
                drakkarWideTrailTexture,
                "PlayerAttackTrailWideRuntime");
            return _proceduralWideTrailMaterial;
        }

        if (_currentInput == AttackInput.Heavy && drakkarHeavyTrailTexture != null)
        {
            _proceduralHeavyTrailMaterial = GetOrCreateDrakkarTrailMaterial(
                _proceduralHeavyTrailMaterial,
                drakkarHeavyTrailTexture,
                "PlayerAttackTrailHeavyRuntime");
            return _proceduralHeavyTrailMaterial;
        }

        if (drakkarLightTrailTexture != null)
        {
            _proceduralLightTrailMaterial = GetOrCreateDrakkarTrailMaterial(
                _proceduralLightTrailMaterial,
                drakkarLightTrailTexture,
                "PlayerAttackTrailLightRuntime");
            return _proceduralLightTrailMaterial;
        }

#if UNITY_EDITOR
        _proceduralTrailMaterial = AssetDatabase.LoadAssetAtPath<Material>(
            "Assets/Drakkar/GameUtils/VISUALS/Trails/DrakkarTrails Examples/Assets/Trail Red 3 Alpha.mat");
#endif

        if (_proceduralTrailMaterial == null)
        {
            Shader shader = Shader.Find("Trail Shader Alpha") ?? Shader.Find("Sprites/Default");
            if (shader == null)
                return null;

            _proceduralTrailMaterial = new Material(shader);
            _proceduralTrailMaterial.name = "PlayerAttackTrailRuntime";
            _proceduralTrailMaterial.hideFlags = HideFlags.HideAndDontSave;

            ConfigureDrakkarTrailMaterial(_proceduralTrailMaterial, Texture2D.whiteTexture);
        }

        return _proceduralTrailMaterial;
    }

    Material GetOrCreateDrakkarTrailMaterial(Material current, Texture2D texture, string materialName)
    {
        if (current != null)
            return current;

        Shader shader = Shader.Find("Trail Shader Alpha") ?? Shader.Find("Sprites/Default");
        if (shader == null || texture == null)
            return null;

        current = new Material(shader);
        current.name = materialName;
        current.hideFlags = HideFlags.HideAndDontSave;
        ConfigureDrakkarTrailMaterial(current, texture);
        return current;
    }

    void ConfigureDrakkarTrailMaterial(Material material, Texture texture)
    {
        if (material == null)
            return;

        if (material.HasProperty("_Color1"))
            material.SetColor("_Color1", ForceVisibleTrailAlpha(trailStartColor));
        if (material.HasProperty("_Color2"))
            material.SetColor("_Color2", ForceVisibleTrailAlpha(trailEndColor));
        if (material.HasProperty("_Texture"))
            material.SetTexture("_Texture", texture);
        if (material.HasProperty("_Power"))
            material.SetFloat("_Power", 8f);
    }

    static Color ForceVisibleTrailAlpha(Color color)
    {
        color.a = 1f;
        return color;
    }

    void EnsureDefaultDrakkarTextures()
    {
#if UNITY_EDITOR
        if (drakkarLightTrailTexture == null)
            drakkarLightTrailTexture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Effects/Trail/trail_player_light.png");
        if (drakkarHeavyTrailTexture == null)
            drakkarHeavyTrailTexture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Effects/Trail/trail_player_heavy.png");
        if (drakkarWideTrailTexture == null)
            drakkarWideTrailTexture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Effects/Trail/trail_ultimate_wide.png");
#endif
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

    Transform ResolveSlashSpawnAnchor()
    {
        if (!preferSlashSpawnAnchorForRangeVfx)
            return null;

        if (slashSpawnAnchorOverride != null)
            return slashSpawnAnchorOverride;

        if (_resolvedSlashSpawnAnchor != null)
            return _resolvedSlashSpawnAnchor;

        Transform searchRoot = playerReferences != null && playerReferences.VisualRoot != null
            ? playerReferences.VisualRoot
            : transform;

        if (!string.IsNullOrWhiteSpace(slashSpawnAnchorName))
            _resolvedSlashSpawnAnchor = FindChildRecursive(searchRoot, slashSpawnAnchorName);

        if (_resolvedSlashSpawnAnchor == null && Application.isPlaying)
        {
            Transform swordTransform = FindSwordVisualTransform();
            _resolvedSlashSpawnAnchor = PlayerWeaponVisualUtility.GetOrCreateBladeAnchor(
                swordTransform,
                string.IsNullOrWhiteSpace(slashSpawnAnchorName) ? "SwordSlashAnchor" : slashSpawnAnchorName,
                PlayerWeaponBladeAnchorKind.Center,
                GetSwordGripReference());
        }

        return _resolvedSlashSpawnAnchor;
    }

    Transform ResolveSwordEndAnchor()
    {
        if (swordEndAnchorOverride != null)
            return swordEndAnchorOverride;

        if (_resolvedSwordEndAnchor != null)
            return _resolvedSwordEndAnchor;

        Transform searchRoot = playerReferences != null && playerReferences.VisualRoot != null
            ? playerReferences.VisualRoot
            : transform;

        if (!string.IsNullOrWhiteSpace(swordEndAnchorName))
            _resolvedSwordEndAnchor = FindChildRecursive(searchRoot, swordEndAnchorName);

        if (_resolvedSwordEndAnchor == null && Application.isPlaying)
        {
            Transform swordTransform = FindSwordVisualTransform();
            _resolvedSwordEndAnchor = PlayerWeaponVisualUtility.GetOrCreateBladeAnchor(
                swordTransform,
                string.IsNullOrWhiteSpace(swordEndAnchorName) ? "SwordEnd" : swordEndAnchorName,
                PlayerWeaponBladeAnchorKind.Tip,
                GetSwordGripReference(),
                swordTipForwardPadding);
        }

        return _resolvedSwordEndAnchor;
    }

    Transform ResolveSwordEdgeAnchor()
    {
        if (swordEdgeAnchorOverride != null)
            return swordEdgeAnchorOverride;

        if (_resolvedSwordEdgeAnchor != null)
            return _resolvedSwordEdgeAnchor;

        Transform searchRoot = playerReferences != null && playerReferences.VisualRoot != null
            ? playerReferences.VisualRoot
            : transform;

        if (!string.IsNullOrWhiteSpace(swordEdgeAnchorName))
            _resolvedSwordEdgeAnchor = FindChildRecursive(searchRoot, swordEdgeAnchorName);

        if (_resolvedSwordEdgeAnchor == null && Application.isPlaying)
        {
            Transform swordTransform = FindSwordVisualTransform();
            _resolvedSwordEdgeAnchor = PlayerWeaponVisualUtility.GetOrCreateBladeAnchor(
                swordTransform,
                string.IsNullOrWhiteSpace(swordEdgeAnchorName) ? "SwordEdge" : swordEdgeAnchorName,
                PlayerWeaponBladeAnchorKind.Edge,
                GetSwordGripReference());
        }

        return _resolvedSwordEdgeAnchor;
    }

    bool IsConfiguredSlashSpawnAnchor(Transform candidate)
    {
        if (candidate == null)
            return false;

        Transform configuredAnchor = ResolveSlashSpawnAnchor();
        return configuredAnchor != null && candidate == configuredAnchor;
    }

    Vector3 ResolveRangeVfxPosition(Transform rangeAnchor)
    {
        if (IsConfiguredSlashSpawnAnchor(rangeAnchor))
        {
            Transform swordEnd = ResolveSwordEndAnchor();
            if (swordEnd != null)
            {
                Vector3 bladeVector = swordEnd.position - rangeAnchor.position;
                Vector3 position = Vector3.LerpUnclamped(rangeAnchor.position, swordEnd.position, Mathf.Clamp(slashSpawnBladeCenterBias, 0f, 1.5f));
                if (bladeVector.sqrMagnitude > 0.0001f)
                    position += bladeVector.normalized * slashSpawnBladeAxisOffset;

                Vector3 attackForward = ResolveBaseAttackForward(null);
                position += ResolveBodyOutwardDirection(position) * slashSpawnHorizontalOutwardOffset;
                position += Vector3.up * slashSpawnVerticalOffset;
                position += attackForward * slashSpawnForwardOffset;
                position = ResolvePlayerForwardPresentationPosition(position, attackForward);
                return position;
            }
        }

        return rangeAnchor != null ? rangeAnchor.position : transform.position;
    }

    Vector3 ResolvePlayerForwardPresentationPosition(Vector3 position, Vector3 attackForward)
    {
        if (!enforceSlashSpawnInFrontOfPlayer || slashSpawnMinPlayerForwardDistance <= 0f || attackForward.sqrMagnitude <= 0.0001f)
            return position;

        Transform playerRoot = playerReferences != null ? playerReferences.PlayerRoot : transform;
        if (playerRoot == null)
            return position;

        Vector3 flatForward = Vector3.ProjectOnPlane(attackForward, Vector3.up);
        if (flatForward.sqrMagnitude <= 0.0001f)
            return position;

        flatForward.Normalize();
        Vector3 fromPlayer = Vector3.ProjectOnPlane(position - playerRoot.position, Vector3.up);
        float currentForwardDistance = Vector3.Dot(fromPlayer, flatForward);
        float missingForwardDistance = slashSpawnMinPlayerForwardDistance - currentForwardDistance;
        if (missingForwardDistance <= 0f)
            return position;

        return position + flatForward * missingForwardDistance;
    }

    Vector3 ResolveBodyOutwardDirection(Vector3 fromPosition)
    {
        Transform playerRoot = playerReferences != null ? playerReferences.PlayerRoot : transform;
        Vector3 outward = playerRoot != null ? fromPosition - playerRoot.position : Vector3.zero;
        outward = Vector3.ProjectOnPlane(outward, Vector3.up);
        if (outward.sqrMagnitude > 0.0001f)
            return outward.normalized;

        if (TryResolveEdgeAxis(ResolveSlashSpawnAnchor(), out Vector3 edgeAxis))
        {
            outward = Vector3.ProjectOnPlane(edgeAxis, Vector3.up);
            if (outward.sqrMagnitude > 0.0001f)
                return outward.normalized;
        }

        return Vector3.zero;
    }

    void ResolveProfileSlashPose(Transform spawnPoint, AttackSlashProfile slashProfile, out Vector3 worldPosition, out Quaternion worldRotation)
    {
        if (useSampledBladePoseForProfileSlash && TryGetLatestBladePose(out BladePoseSample latestPose))
        {
            Vector3 forward = ResolveBaseAttackForward(null);
            if (forward.sqrMagnitude <= 0.0001f)
                forward = directionRoot != null ? directionRoot.forward : transform.forward;

            forward = Vector3.ProjectOnPlane(forward, Vector3.up);
            if (forward.sqrMagnitude <= 0.0001f)
                forward = Vector3.forward;
            else
                forward.Normalize();

            Vector3 up = ResolveSampledSlashUp(latestPose, forward);
            Quaternion sampledRotation = Quaternion.LookRotation(forward, up);
            worldRotation = sampledRotation * Quaternion.Euler(slashProfile.LocalEulerOffset);

            Vector3 basePosition = latestPose.HasAnchor ? latestPose.AnchorPosition : spawnPoint.position;
            worldPosition = basePosition + sampledRotation * slashProfile.LocalPositionOffset;
            return;
        }

        worldPosition = spawnPoint.TransformPoint(slashProfile.LocalPositionOffset);
        Quaternion baseRotation = directionRoot != null ? directionRoot.rotation : transform.rotation;
        worldRotation = baseRotation * Quaternion.Euler(slashProfile.LocalEulerOffset);
    }

    Vector3 ResolveSampledSlashUp(BladePoseSample latestPose, Vector3 forward)
    {
        if (!useObject002BladePoseForSlashVfx)
            return Vector3.up;

        Vector3 up = Vector3.zero;
        Vector3 bladeUp = Vector3.zero;

        if (latestPose.HasAnchor && latestPose.HasEnd)
        {
            Vector3 bladeAxis = latestPose.EndPosition - latestPose.AnchorPosition;
            Vector3 projectedBladeAxis = Vector3.ProjectOnPlane(bladeAxis, forward);
            if (projectedBladeAxis.sqrMagnitude > 0.0001f)
                bladeUp = projectedBladeAxis.normalized;
        }

        if (TryResolveSampledTipMotion(out Vector3 motionForward))
        {
            Vector3 motionUp = Vector3.ProjectOnPlane(motionForward, forward);
            if (motionUp.sqrMagnitude > 0.0001f)
            {
                motionUp.Normalize();
                up = bladeUp.sqrMagnitude > 0.0001f
                    ? Vector3.Slerp(bladeUp, motionUp, Mathf.Clamp01(swordTipMotionDirectionWeight)).normalized
                    : motionUp;

                if (invertSwordTipMotionRoll)
                    up = -up;
            }
        }

        if (up.sqrMagnitude <= 0.0001f)
            up = bladeUp;

        if (useSwordEdgeForSlashRollSign && latestPose.HasAnchor && latestPose.HasEdge)
        {
            Vector3 edgeAxis = Vector3.ProjectOnPlane(latestPose.EdgePosition - latestPose.AnchorPosition, forward);
            if (edgeAxis.sqrMagnitude > 0.0001f)
            {
                edgeAxis.Normalize();
                if (up.sqrMagnitude <= 0.0001f)
                {
                    up = Vector3.Cross(forward, edgeAxis);
                    if (up.sqrMagnitude > 0.0001f)
                        up.Normalize();
                }
                else
                {
                    Vector3 right = Vector3.Cross(up, forward);
                    if (right.sqrMagnitude > 0.0001f && Vector3.Dot(right.normalized, edgeAxis) < 0f)
                        up = -up;
                }
            }
        }

        if (up.sqrMagnitude <= 0.0001f)
            return Vector3.up;

        return Vector3.Slerp(Vector3.up, up.normalized, Mathf.Clamp01(object002BladeRollWeight)).normalized;
    }

    bool TryCaptureBladePose(out BladePoseSample pose)
    {
        pose = default;

        Transform anchor = ResolveAnimationEventSlashSpawnPoint();
        Transform swordEnd = ResolveSwordEndAnchor();
        Transform swordEdge = ResolveSwordEdgeAnchor();

        pose.HasAnchor = anchor != null;
        pose.HasEnd = swordEnd != null;
        pose.HasEdge = swordEdge != null;

        if (pose.HasAnchor)
            pose.AnchorPosition = anchor.position;
        if (pose.HasEnd)
            pose.EndPosition = swordEnd.position;
        if (pose.HasEdge)
            pose.EdgePosition = swordEdge.position;

        return pose.HasAnchor || pose.HasEnd || pose.HasEdge;
    }

    bool TryGetLatestBladePose(out BladePoseSample pose)
    {
        pose = default;
        if (_bladePoseSampleCount <= 0)
            return TryCaptureBladePose(out pose);

        int index = (_bladePoseWriteIndex - 1 + _bladePoseSamples.Length) % _bladePoseSamples.Length;
        pose = _bladePoseSamples[index];
        return pose.HasAnchor || pose.HasEnd || pose.HasEdge;
    }

    bool TryResolveSampledTipMotion(out Vector3 forward)
    {
        forward = Vector3.zero;
        if (_bladePoseSampleCount < 2)
            return TryResolveSwordTipMotionForward(out forward);

        int newestIndex = (_bladePoseWriteIndex - 1 + _bladePoseSamples.Length) % _bladePoseSamples.Length;
        int framesBack = Mathf.Clamp(swordTipMotionSampleFrames, 1, _bladePoseSampleCount - 1);
        int oldestIndex = (newestIndex - framesBack + _bladePoseSamples.Length) % _bladePoseSamples.Length;

        BladePoseSample newest = _bladePoseSamples[newestIndex];
        BladePoseSample oldest = _bladePoseSamples[oldestIndex];
        if (!newest.HasEnd || !oldest.HasEnd)
            return TryResolveSwordTipMotionForward(out forward);

        Vector3 motion = newest.EndPosition - oldest.EndPosition;
        if (motion.sqrMagnitude < swordTipMotionMinDistance * swordTipMotionMinDistance)
            return TryResolveSwordTipMotionForward(out forward);

        forward = motion.normalized;
        return true;
    }

    Quaternion ResolveRangeVfxRotation(Transform rangeAnchor, AttackHitbox hitbox)
    {
        if (orientSlashVfxToAttackDirection && IsConfiguredSlashSpawnAnchor(rangeAnchor))
        {
            Vector3 forward = ResolveAttackForward(hitbox);
            if (forward.sqrMagnitude > 0.0001f)
                return Quaternion.LookRotation(forward, ResolveSlashUp(rangeAnchor, forward));
        }

        return rangeAnchor != null ? rangeAnchor.rotation : transform.rotation;
    }

    Vector3 ResolveSlashUp(Transform slashAnchor, Vector3 forward)
    {
        if (!useObject002BladePoseForSlashVfx)
            return Vector3.up;

        Vector3 up = Vector3.zero;
        Vector3 bladeUp = Vector3.zero;

        if (useSwordEndForSlashRoll && TryResolveBladeAxis(slashAnchor, out Vector3 bladeAxis))
        {
            Vector3 rolledUp = Vector3.ProjectOnPlane(bladeAxis, forward);
            if (rolledUp.sqrMagnitude > 0.0001f)
                bladeUp = rolledUp.normalized;
        }

        if (useSwordTipMotionForSlashRoll && TryResolveSwordTipMotionForward(out Vector3 motionForward))
        {
            Vector3 motionUp = Vector3.ProjectOnPlane(motionForward, forward);
            if (motionUp.sqrMagnitude > 0.0001f)
            {
                motionUp.Normalize();
                up = bladeUp.sqrMagnitude > 0.0001f
                    ? Vector3.Slerp(bladeUp, motionUp, Mathf.Clamp01(swordTipMotionDirectionWeight)).normalized
                    : motionUp;

                if (invertSwordTipMotionRoll)
                    up = -up;
            }
        }

        if (up.sqrMagnitude <= 0.0001f)
            up = bladeUp;

        if (useSwordEdgeForSlashRollSign && TryResolveEdgeAxis(slashAnchor, out Vector3 edgeAxis))
        {
            Vector3 rolledEdge = Vector3.ProjectOnPlane(edgeAxis, forward);
            if (rolledEdge.sqrMagnitude > 0.0001f)
            {
                rolledEdge.Normalize();

                if (up.sqrMagnitude <= 0.0001f)
                {
                    up = Vector3.Cross(forward, rolledEdge);
                    if (up.sqrMagnitude > 0.0001f)
                        up.Normalize();
                }
                else
                {
                    Vector3 right = Vector3.Cross(up, forward);
                    if (right.sqrMagnitude > 0.0001f && Vector3.Dot(right.normalized, rolledEdge) < 0f)
                        up = -up;
                }
            }
        }

        if (up.sqrMagnitude <= 0.0001f)
            return Vector3.up;

        return Vector3.Slerp(Vector3.up, up.normalized, Mathf.Clamp01(object002BladeRollWeight)).normalized;
    }

    bool TryResolveBladeAxis(Transform slashAnchor, out Vector3 bladeAxis)
    {
        bladeAxis = Vector3.zero;
        Transform swordEnd = ResolveSwordEndAnchor();
        if (slashAnchor == null || swordEnd == null)
            return false;

        bladeAxis = swordEnd.position - slashAnchor.position;
        if (bladeAxis.sqrMagnitude <= 0.0001f)
            return false;

        bladeAxis.Normalize();
        return true;
    }

    bool TryResolveEdgeAxis(Transform slashAnchor, out Vector3 edgeAxis)
    {
        edgeAxis = Vector3.zero;
        Transform swordEdge = ResolveSwordEdgeAnchor();
        if (slashAnchor == null || swordEdge == null)
            return false;

        edgeAxis = swordEdge.position - slashAnchor.position;
        if (edgeAxis.sqrMagnitude <= 0.0001f)
            return false;

        edgeAxis.Normalize();
        return true;
    }

    Vector3 ResolveAttackForward(AttackHitbox hitbox)
    {
        return ResolveBaseAttackForward(hitbox);
    }

    Vector3 ResolveBaseAttackForward(AttackHitbox hitbox)
    {
        if (useLockOnDirectionForSlashVfx && TryResolveLockOnAttackForward(out Vector3 lockOnForward))
            return lockOnForward;

        if (TryResolveHorizontalForward(hitbox != null ? hitbox.transform : null, out Vector3 hitboxForward))
            return hitboxForward;

        Transform playerRoot = playerReferences != null ? playerReferences.PlayerRoot : null;
        if (TryResolveHorizontalForward(playerRoot, out Vector3 playerForward))
            return playerForward;

        if (TryResolveHorizontalForward(transform, out Vector3 fallbackForward))
            return fallbackForward;

        return Vector3.forward;
    }

    bool TryResolveLockOnAttackForward(out Vector3 forward)
    {
        forward = Vector3.zero;
        if (lockOnTargetProvider == null || !lockOnTargetProvider.HasTarget)
            return false;

        Transform target = lockOnTargetProvider.CurrentTarget;
        Transform playerRoot = playerReferences != null && playerReferences.PlayerRoot != null
            ? playerReferences.PlayerRoot
            : transform;
        if (target == null || playerRoot == null)
            return false;

        forward = Vector3.ProjectOnPlane(target.position - playerRoot.position, Vector3.up);
        if (forward.sqrMagnitude <= 0.0001f)
            return false;

        forward.Normalize();
        return true;
    }

    bool TryResolveSwordTipMotionForward(out Vector3 forward)
    {
        forward = Vector3.zero;
        if (_swordTipMotionSampleCount < 2)
            return false;

        int newestIndex = (_swordTipMotionWriteIndex - 1 + _swordTipMotionSamples.Length) % _swordTipMotionSamples.Length;
        int framesBack = Mathf.Clamp(swordTipMotionSampleFrames, 1, _swordTipMotionSampleCount - 1);
        int oldestIndex = (newestIndex - framesBack + _swordTipMotionSamples.Length) % _swordTipMotionSamples.Length;

        Vector3 motion = _swordTipMotionSamples[newestIndex] - _swordTipMotionSamples[oldestIndex];
        if (motion.sqrMagnitude < swordTipMotionMinDistance * swordTipMotionMinDistance)
            return false;

        forward = motion.normalized;
        return true;
    }

    static bool TryResolveHorizontalForward(Transform source, out Vector3 forward)
    {
        forward = Vector3.zero;
        if (source == null)
            return false;

        forward = Vector3.ProjectOnPlane(source.forward, Vector3.up);
        if (forward.sqrMagnitude <= 0.0001f)
            return false;

        forward.Normalize();
        return true;
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

        if (weaponSocketOverride != null)
            return weaponSocketOverride;

        if (playerReferences != null && playerReferences.WeaponSocket != null)
            return playerReferences.WeaponSocket;

        if (!useWeaponSocketForTrail && hitbox != null)
            return hitbox.transform;

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

    Transform ResolveSwordBaseAnchor()
    {
        if (_resolvedSwordBaseAnchor != null)
            return _resolvedSwordBaseAnchor;

        Transform swordTransform = FindSwordVisualTransform();
        if (swordTransform == null)
            return null;

        _resolvedSwordBaseAnchor = CreateSwordBaseAnchor(swordTransform);
        return _resolvedSwordBaseAnchor;
    }

    Transform FindSwordVisualTransform()
    {
        Transform searchRoot = playerReferences != null && playerReferences.VisualRoot != null
            ? playerReferences.VisualRoot
            : transform;

        Transform object002 = PlayerWeaponVisualUtility.FindSwordVisualTransform(searchRoot);
        if (object002 != null && string.Equals(object002.name, "Object002", StringComparison.OrdinalIgnoreCase))
            return object002;

        Transform weaponSocket = weaponSocketOverride != null
            ? weaponSocketOverride
            : playerReferences != null ? playerReferences.WeaponSocket : null;

        Transform bestByHeuristic = FindBestSwordLikeTransform(searchRoot, weaponSocket);
        if (bestByHeuristic != null)
            return bestByHeuristic;

        string[] preferredNames =
        {
            "sword",
            "katana",
            "blade",
            "weapon_r",
            "weapon",
            "sword_holder",
            "9CG_Sword(Clone)"
        };

        for (int i = 0; i < preferredNames.Length; i++)
        {
            Transform candidate = FindChildRecursive(searchRoot, preferredNames[i]);
            if (candidate != null)
                return candidate;
        }

        Transform object009 = FindChildRecursive(searchRoot, "Object009");
        if (LooksLikeSwordMesh(object009))
            return object009;

        return null;
    }

    Transform GetSwordGripReference()
    {
        if (weaponSocketOverride != null)
            return weaponSocketOverride;

        if (playerReferences != null && playerReferences.WeaponSocket != null)
            return playerReferences.WeaponSocket;

        Transform swordTransform = FindSwordVisualTransform();
        if (swordTransform != null && swordTransform.parent != null)
            return swordTransform.parent;

        return playerReferences != null && playerReferences.PlayerRoot != null ? playerReferences.PlayerRoot : transform;
    }

    Transform FindBestSwordLikeTransform(Transform searchRoot, Transform weaponSocket)
    {
        if (searchRoot == null)
            return null;

        Renderer[] renderers = searchRoot.GetComponentsInChildren<Renderer>(true);
        Transform best = null;
        float bestScore = float.NegativeInfinity;

        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null)
                continue;

            Transform candidate = renderer.transform;
            if (!TryGetLocalMeshBounds(candidate, out Bounds localBounds))
                continue;

            string lowerName = candidate.name.ToLowerInvariant();
            if (lowerName.Contains("body") || lowerName.Contains("clothes") || lowerName.Contains("face") || lowerName.Contains("hair"))
                continue;

            Vector3 size = localBounds.size;
            float maxAxis = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
            float minAxis = Mathf.Min(size.x, Mathf.Min(size.y, size.z));
            if (maxAxis <= 0.05f || minAxis <= 0.0001f)
                continue;

            float slenderness = maxAxis / minAxis;
            if (slenderness < 3f)
                continue;

            float score = slenderness * 10f - size.sqrMagnitude;
            if (weaponSocket != null)
                score -= Vector3.Distance(candidate.position, weaponSocket.position) * 4f;

            if (lowerName.Contains("sword") || lowerName.Contains("katana") || lowerName.Contains("blade") || lowerName.Contains("weapon"))
                score += 50f;

            if (score > bestScore)
            {
                bestScore = score;
                best = candidate;
            }
        }

        return best;
    }

    bool LooksLikeSwordMesh(Transform candidate)
    {
        if (candidate == null || !TryGetLocalMeshBounds(candidate, out Bounds localBounds))
            return false;

        Vector3 size = localBounds.size;
        float maxAxis = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
        float minAxis = Mathf.Min(size.x, Mathf.Min(size.y, size.z));
        return maxAxis > 0.05f && minAxis > 0.0001f && (maxAxis / minAxis) >= 3f;
    }

    Transform CreateSwordTipAnchor(Transform swordTransform)
    {
        const string anchorName = "RuntimeSwordTrailAnchor";
        Transform existing = swordTransform.Find(anchorName);
        if (existing != null)
            return existing;

        Transform anchor = PlayerWeaponVisualUtility.GetOrCreateBladeAnchor(
            swordTransform,
            anchorName,
            PlayerWeaponBladeAnchorKind.Tip,
            GetSwordGripReference(),
            swordTipForwardPadding);
        return anchor != null ? anchor : swordTransform;
    }

    Transform CreateSwordBaseAnchor(Transform swordTransform)
    {
        const string anchorName = "RuntimeSwordBaseAnchor";
        Transform existing = swordTransform.Find(anchorName);
        if (existing != null)
            return existing;

        Transform anchor = PlayerWeaponVisualUtility.GetOrCreateBladeAnchor(
            swordTransform,
            anchorName,
            PlayerWeaponBladeAnchorKind.Base,
            GetSwordGripReference(),
            swordBaseBackwardPadding);
        return anchor != null ? anchor : swordTransform;
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

    Transform ResolveRangeAnchor(AttackHitbox hitbox, Transform trailAnchor, AttackVfxEntry entry)
    {
        Transform slashSpawnAnchor = ResolveSlashSpawnAnchor();
        if (slashSpawnAnchor != null && !entry.parentRangeVfxToHitbox && !entry.scaleRangeVfxToHitbox)
            return slashSpawnAnchor;

        if (!entry.parentRangeVfxToHitbox && !entry.scaleRangeVfxToHitbox && trailAnchor != null)
            return trailAnchor;

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

    void BuildProfileLookup()
    {
        _attackProfileByKey.Clear();

        if (attackProfileEntries == null)
            return;

        for (int i = 0; i < attackProfileEntries.Length; i++)
        {
            AttackVfxProfileEntry entry = attackProfileEntries[i];
            if (entry == null)
                continue;

            if (entry.profile == null)
            {
                Debug.LogWarning($"[PlayerAttackVfxPresenter] Attack VFX profile is missing at index {i}.", this);
                continue;
            }

            string key = !string.IsNullOrWhiteSpace(entry.key) ? entry.key : entry.profile.AttackKey;
            if (string.IsNullOrWhiteSpace(key))
            {
                Debug.LogWarning($"[PlayerAttackVfxPresenter] Attack VFX key is empty for profile '{entry.profile.name}'.", this);
                continue;
            }

            if (_attackProfileByKey.ContainsKey(key))
                Debug.LogWarning($"[PlayerAttackVfxPresenter] Duplicate attack VFX key detected: {key}", this);

            _attackProfileByKey[key] = entry.profile;
        }
    }

    void PrewarmPools()
    {
        _prewarmedProfilePrefabs.Clear();

        if (useUnifiedSlashVfx && unifiedSlashVfxPrefab != null)
            PrewarmPool(unifiedSlashVfxPrefab);

        if (_attackProfileByKey.Count == 0)
            BuildProfileLookup();

        foreach (AttackVfxProfile profile in _attackProfileByKey.Values)
        {
            if (profile == null)
                continue;

            AttackSlashProfile slashProfile = profile.SlashProfile;
            if (slashProfile != null && slashProfile.SlashPrefab != null)
                PrewarmPool(slashProfile.SlashPrefab);

            if (profile.HitImpactPrefab != null)
                PrewarmPool(profile.HitImpactPrefab);
        }
    }

    void PrewarmPool(GameObject prefab)
    {
        if (prefab == null || _prewarmedProfilePrefabs.Contains(prefab))
            return;

        _prewarmedProfilePrefabs.Add(prefab);

        if (!_poolByPrefab.TryGetValue(prefab, out Queue<PooledVfxInstance> pool))
        {
            pool = new Queue<PooledVfxInstance>();
            _poolByPrefab.Add(prefab, pool);
        }

        int count = Mathf.Max(1, prewarmCount);
        while (pool.Count < count)
            pool.Enqueue(CreatePooledVfxInstance(prefab));
    }

    Transform ResolveAnimationEventSlashSpawnPoint()
    {
        if (slashSpawnPoint != null)
            return slashSpawnPoint;

        slashSpawnPoint = ResolveSlashSpawnAnchor();
        return slashSpawnPoint;
    }

    PooledVfxInstance GetFromPool(GameObject prefab)
    {
        if (prefab == null)
            return null;

        if (!_poolByPrefab.TryGetValue(prefab, out Queue<PooledVfxInstance> pool))
        {
            pool = new Queue<PooledVfxInstance>();
            _poolByPrefab.Add(prefab, pool);
        }

        while (pool.Count > 0)
        {
            PooledVfxInstance instance = pool.Dequeue();
            if (instance != null && instance.GameObject != null)
                return instance;
        }

        return CreatePooledVfxInstance(prefab);
    }

    PooledVfxInstance CreatePooledVfxInstance(GameObject prefab)
    {
        if (prefab == null)
            return null;

        GameObject instance = Instantiate(prefab);
        instance.name = $"{prefab.name}_Pooled";
        instance.transform.SetParent(null, false);
        instance.SetActive(false);
        return new PooledVfxInstance(instance);
    }

    IEnumerator ReturnAfterLifetime(GameObject prefab, PooledVfxInstance instance, float lifetime)
    {
        if (prefab == null || instance == null)
            yield break;

        int leaseId = instance.LeaseId;
        // WaitForSeconds 할당 대신 타이머 사용 (동일한 스케일 시간 기준)
        float returnAt = Time.time + Mathf.Max(0.05f, lifetime);
        while (Time.time < returnAt)
            yield return null;

        if (instance.GameObject == null || instance.LeaseId != leaseId)
            yield break;

        StopParticles(instance);
        instance.Transform.SetParent(null, false);
        instance.GameObject.SetActive(false);

        if (!_poolByPrefab.TryGetValue(prefab, out Queue<PooledVfxInstance> pool))
        {
            pool = new Queue<PooledVfxInstance>();
            _poolByPrefab.Add(prefab, pool);
        }

        pool.Enqueue(instance);
    }

    static void PlayParticles(PooledVfxInstance instance)
    {
        if (instance == null)
            return;

        if (instance.Particles != null)
        {
            for (int i = 0; i < instance.Particles.Length; i++)
            {
                ParticleSystem particle = instance.Particles[i];
                if (particle == null)
                    continue;

                // 배열에 모든 자식 시스템이 포함되어 있으므로 withChildren=false (중복 순회 방지)
                particle.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                particle.Play(false);
            }
        }

        if (instance.Trails != null)
        {
            for (int i = 0; i < instance.Trails.Length; i++)
            {
                TrailRenderer trail = instance.Trails[i];
                if (trail == null)
                    continue;

                trail.Clear();
                trail.emitting = true;
            }
        }
    }

    static void StopParticles(PooledVfxInstance instance)
    {
        if (instance == null)
            return;

        if (instance.Particles != null)
        {
            for (int i = 0; i < instance.Particles.Length; i++)
            {
                ParticleSystem particle = instance.Particles[i];
                if (particle != null)
                    particle.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
        }

        if (instance.Trails != null)
        {
            for (int i = 0; i < instance.Trails.Length; i++)
            {
                TrailRenderer trail = instance.Trails[i];
                if (trail == null)
                    continue;

                trail.emitting = false;
                trail.Clear();
            }
        }
    }

    void RequestFeedbackHooks(AttackVfxProfile profile)
    {
        if (profile == null)
            return;

        if (!profile.RequestHitStop && !profile.RequestCameraShake)
            return;

        // TODO: Connect this to the project's Hit Stop / Camera Shake systems when the hit pipeline is finalized.
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
    [ContextMenu("Refresh Default Slash VFX Mappings")]
    public void RefreshDefaultSlashVfxMappings()
    {
        EnsureDefaultMappings(force: true);
        MarkEntryLookupDirty();
    }

    void EnsureDefaultMappings(bool force)
    {
        GameObject slashCPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Slash_C.prefab");
        if (unifiedSlashVfxPrefab == null)
            unifiedSlashVfxPrefab = slashCPrefab;

        if (!force && attackVfxEntries != null && attackVfxEntries.Length > 0)
            return;

        GameObject lightSlashPrefab = slashCPrefab != null
            ? slashCPrefab
            : AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Free Slash VFX/Prefabs/Slash VFX.prefab");
        GameObject heavySlashPrefab = slashCPrefab != null
            ? slashCPrefab
            : AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Free Slash VFX/Prefabs/Slash Eletric VFX.prefab");
        defaultHitRangeVfxPrefab = lightSlashPrefab;
        defaultWeaponTrailVfxPrefab = null;

        string[] attackAssetPaths =
        {
            @"Assets/Attack_data/AD_L1.asset",
            @"Assets/Attack_data/Combo_A/AD_L2.asset",
            @"Assets/Attack_data/Combo_A/AD_L3.asset",
            @"Assets/Attack_data/Combo_A/AD_L4.asset",
            @"Assets/Attack_data/Combo_D/AD_L2_D.asset",
            @"Assets/Attack_data/Combo_C/AD_L3_C.asset",
            @"Assets/Attack_data/AD_H1.asset",
            @"Assets/Attack_data/Combo_E/AD_H2.asset",
            @"Assets/Attack_data/Combo_E/AD_H3.asset",
            @"Assets/Attack_data/Combo_C/AD_H2_C.asset",
            @"Assets/Attack_data/Combo_D/AD_H3_D.asset",
            @"Assets/Attack_data/Combo_B/AD_H4_B.asset",
            @"Assets/Attack_data/Combo_C/AD_H4_C.asset",
            @"Assets/Attack_data/Combo_B/AD_H5_B.asset"
        };

        GameObject trailPrefab = null;
        Vector3[] eulerOffsets =
        {
            new Vector3(0f, 0f, -24f),
            new Vector3(0f, 0f, 28f),
            new Vector3(0f, 0f, 92f),
            new Vector3(0f, 0f, -8f),
            new Vector3(0f, 0f, -38f),
            new Vector3(0f, 0f, 58f),
            new Vector3(0f, 0f, -12f),
            new Vector3(0f, 0f, 36f),
            new Vector3(0f, 0f, 84f),
            new Vector3(0f, 0f, -52f),
            new Vector3(0f, 0f, 18f),
            new Vector3(0f, 0f, 112f),
            new Vector3(0f, 0f, -72f),
            new Vector3(0f, 0f, 6f)
        };
        Vector3[] positionOffsets =
        {
            new Vector3(0f, 0.02f, 0f),
            new Vector3(0f, 0.02f, 0.01f),
            new Vector3(0f, 0.03f, 0f),
            new Vector3(0f, 0.025f, 0.02f),
            new Vector3(0f, 0.02f, 0.01f),
            new Vector3(0f, 0.025f, 0.01f),
            new Vector3(0f, 0.03f, 0.02f),
            new Vector3(0f, 0.035f, 0.02f),
            new Vector3(0f, 0.04f, 0.02f),
            new Vector3(0f, 0.03f, 0.02f),
            new Vector3(0f, 0.04f, 0.025f),
            new Vector3(0f, 0.045f, 0.025f),
            new Vector3(0f, 0.04f, 0.025f),
            new Vector3(0f, 0.05f, 0.03f)
        };
        Vector3[] scaleMultipliers =
        {
            new Vector3(0.9f, 0.9f, 0.9f),
            new Vector3(0.95f, 0.95f, 0.95f),
            new Vector3(1f, 1f, 1f),
            new Vector3(1.12f, 1.12f, 1.12f),
            new Vector3(0.95f, 0.95f, 0.95f),
            new Vector3(1.02f, 1.02f, 1.02f),
            new Vector3(1.1f, 1.1f, 1.1f),
            new Vector3(1.18f, 1.18f, 1.18f),
            new Vector3(1.26f, 1.26f, 1.26f),
            new Vector3(1.16f, 1.16f, 1.16f),
            new Vector3(1.24f, 1.24f, 1.24f),
            new Vector3(1.34f, 1.34f, 1.34f),
            new Vector3(1.3f, 1.3f, 1.3f),
            new Vector3(1.42f, 1.42f, 1.42f)
        };

        AttackVfxEntry[] defaults = new AttackVfxEntry[attackAssetPaths.Length];
        for (int i = 0; i < attackAssetPaths.Length; i++)
        {
            AttackData attackData = AssetDatabase.LoadAssetAtPath<AttackData>(attackAssetPaths[i]);
            Vector3 scale = i < scaleMultipliers.Length ? scaleMultipliers[i] : Vector3.one;
            Vector3 positionOffset = i < positionOffsets.Length ? positionOffsets[i] : Vector3.zero;
            Vector3 eulerOffset = i < eulerOffsets.Length ? eulerOffsets[i] : Vector3.zero;
            float lifetime = i >= 6 ? 0.38f : 0.28f;

            defaults[i] = new AttackVfxEntry
            {
                attackData = attackData,
                hitRangeVfxPrefab = i >= 6 && heavySlashPrefab != null ? heavySlashPrefab : lightSlashPrefab,
                weaponTrailVfxPrefab = trailPrefab,
                localPositionOffset = positionOffset,
                localEulerOffset = eulerOffset,
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
