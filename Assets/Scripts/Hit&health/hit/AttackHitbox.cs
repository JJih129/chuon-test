using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
[RequireComponent(typeof(Collider))]
public class AttackHitbox : MonoBehaviour
{
    [Header("Damage")]
    [Min(0f)] public float baseDamage = 10f;
    public HitType hitType = HitType.Normal;
    public bool canParry = true;
    public bool canPerfectDodge = true;
    public bool unblockable = false;

    [Header("Attacker")]
    public Transform attackerRoot;

    [Header("Filters")]
    public LayerMask hitLayers = ~0;
    public bool ignoreTriggerColliders = true;

    [Header("One-Shot Window")]
    public bool useOneShotWindow = true;
    [Min(0f)] public float oneShotWindow = 0.2f;
    public bool hitEachReceiverOncePerActivation = true;
    [Min(0)] public int maxUniqueTargetsPerActivation = 0;

    [Header("Expanded Detection")]
    public bool useExpandedHitDetection = false;
    [Min(0f)] public float expandedPadding = 0.45f;
    [Min(4)] public int expandedHitBufferSize = 24;
    [Range(0.1f, 1f)] public float meshExpandedPaddingScale = 0.6f;
    [Min(0f)] public float expandedScanInterval = 0.06f;

    [Header("Runtime Preview")]
    public bool showRuntimeHitboxPreview = false;
    public bool previewUseExpandedShape = true;
    public Color previewColor = new Color(1f, 0.35f, 0.1f, 0.95f);
    [Min(0.002f)] public float previewLineWidth = 0.04f;
    [Range(8, 48)] public int previewCircleSegments = 18;
    [Min(0f)] public float previewPersistSeconds = 0.2f;
    [Range(0.02f, 0.5f)] public float previewShellAlpha = 0.16f;

    [Header("Debug")]
    public bool enableLogs = false;

    Collider _col;
    readonly HashSet<IDamageReceiver> _alreadyHit = new HashSet<IDamageReceiver>();
    Coroutine _oneShotRoutine;
    Collider[] _expandedHitResults;
    readonly List<LineRenderer> _previewLines = new List<LineRenderer>();
    GameObject _previewRoot;
    Material _previewMaterial;
    Material _previewShellMaterial;
    GameObject _previewShellObject;
    MeshRenderer _previewShellRenderer;
    PrimitiveType _previewShellType = PrimitiveType.Cube;
    float _previewHideAtRealtime = float.NegativeInfinity;
    float _nextExpandedScanAt;
    bool _sleepAfterPreviewHide;

    public Collider Collider => _col;

    void Reset()
    {
        _col = GetComponent<Collider>();
        if (_col != null)
        {
            _col.isTrigger = true;
            _col.enabled = false;

            if (_col is MeshCollider meshCol && !meshCol.convex)
                meshCol.convex = true;
        }

        hitLayers = ~0;
        ignoreTriggerColliders = true;
    }

    void Awake()
    {
        _col = GetComponent<Collider>();
        if (_col == null)
        {
            Debug.LogError("[AttackHitbox] Collider is missing.", this);
            enabled = false;
            return;
        }

        if (!_col.isTrigger)
            _col.isTrigger = true;

        if (_col is MeshCollider meshCol)
        {
            if (!meshCol.convex)
                Debug.LogWarning("[AttackHitbox] MeshCollider should usually be Convex for moving trigger hitboxes.", this);

            if (meshCol.sharedMesh == null)
                Debug.LogWarning("[AttackHitbox] MeshCollider.sharedMesh is missing.", this);
        }

        if (attackerRoot == null)
            attackerRoot = transform.root;

        _expandedHitResults = new Collider[Mathf.Max(4, expandedHitBufferSize)];
        RefreshExecutionState();
    }

    void OnEnable()
    {
        _alreadyHit.Clear();
        _sleepAfterPreviewHide = false;
    }

    void OnDisable()
    {
        _alreadyHit.Clear();
        _sleepAfterPreviewHide = false;

        if (_previewRoot != null && _previewRoot.activeSelf)
            _previewRoot.SetActive(false);

        if (_oneShotRoutine != null)
        {
            StopCoroutine(_oneShotRoutine);
            _oneShotRoutine = null;
        }
    }

    void OnDestroy()
    {
        if (_previewMaterial != null)
            Destroy(_previewMaterial);

        if (_previewShellMaterial != null)
            Destroy(_previewShellMaterial);

        if (_previewRoot != null)
            Destroy(_previewRoot);
    }

    void FixedUpdate()
    {
        if (!useExpandedHitDetection || _col == null || !_col.enabled)
            return;

        if (maxUniqueTargetsPerActivation > 0 && _alreadyHit.Count >= maxUniqueTargetsPerActivation)
            return;

        if (expandedScanInterval > 0f && Time.time < _nextExpandedScanAt)
            return;

        _nextExpandedScanAt = Time.time + expandedScanInterval;
        EnsureExpandedHitBuffer();
        ScanExpandedTargets();
    }

    void LateUpdate()
    {
        if (!ShouldShowRuntimePreview())
        {
            if (_sleepAfterPreviewHide && (_col == null || !_col.enabled))
            {
                _sleepAfterPreviewHide = false;
                RefreshExecutionState();
            }
            return;
        }

        bool colliderActive = _col != null && _col.enabled;
        bool previewActive = _previewRoot != null && _previewRoot.activeSelf;

        if (colliderActive)
        {
            UpdatePreviewVisual();
            return;
        }

        if (previewActive && Time.realtimeSinceStartup >= _previewHideAtRealtime)
        {
            _previewRoot.SetActive(false);
            previewActive = false;
        }

        if (!previewActive && !colliderActive && _sleepAfterPreviewHide)
        {
            _sleepAfterPreviewHide = false;
            RefreshExecutionState();
        }
    }

    public void ActivateWindow()
    {
        if (_col == null)
            return;

        if (!enabled)
            enabled = true;

        _sleepAfterPreviewHide = false;
        _col.enabled = true;
        _alreadyHit.Clear();
        SetPreviewVisible(true);

        if (!hitEachReceiverOncePerActivation && useOneShotWindow && oneShotWindow > 0f)
        {
            if (_oneShotRoutine != null)
                StopCoroutine(_oneShotRoutine);

            _oneShotRoutine = StartCoroutine(CoClearOneShotAfter(oneShotWindow));
        }

        if (enableLogs)
            Debug.Log("[AttackHitbox] Window ON", this);
    }

    public void DeactivateWindow()
    {
        if (_col == null)
            return;

        _col.enabled = false;
        _alreadyHit.Clear();
        SetPreviewVisible(false);

        if (_oneShotRoutine != null)
        {
            StopCoroutine(_oneShotRoutine);
            _oneShotRoutine = null;
        }

        if (enableLogs)
            Debug.Log("[AttackHitbox] Window OFF", this);

        if (_previewRoot != null && _previewRoot.activeSelf)
            _sleepAfterPreviewHide = true;
        else
            RefreshExecutionState();
    }

    IEnumerator CoClearOneShotAfter(float seconds)
    {
        yield return new WaitForSeconds(seconds);
        _alreadyHit.Clear();
        _oneShotRoutine = null;
    }

    public void Configure(
        float damage,
        HitType type,
        bool allowParry,
        bool allowPerfectDodge,
        bool isUnblockable = false,
        Transform attackerOverride = null)
    {
        baseDamage = damage;
        hitType = type;
        canParry = allowParry;
        canPerfectDodge = allowPerfectDodge;
        unblockable = isUnblockable;

        if (attackerOverride != null)
            attackerRoot = attackerOverride;
    }

    public void Configure(
        float damage,
        bool allowParry,
        bool allowPerfectDodge,
        bool isUnblockable = false,
        Transform attackerOverride = null)
    {
        baseDamage = damage;
        canParry = allowParry;
        canPerfectDodge = allowPerfectDodge;
        unblockable = isUnblockable;

        if (attackerOverride != null)
            attackerRoot = attackerOverride;
    }

    void OnTriggerEnter(Collider other)
    {
        TryApplyHit(other);
    }

    void TryApplyHit(Collider other)
    {
        if (other == null || other == _col)
            return;

        if (maxUniqueTargetsPerActivation > 0 && _alreadyHit.Count >= maxUniqueTargetsPerActivation)
            return;

        if (attackerRoot != null && other.transform.root == attackerRoot.root)
            return;

        if (ignoreTriggerColliders && other.isTrigger)
            return;

        if (((1 << other.gameObject.layer) & hitLayers) == 0)
            return;

        IDamageReceiver receiver = other.GetComponentInParent<IDamageReceiver>();
        if (receiver == null)
            return;

        bool dedupeByReceiver = hitEachReceiverOncePerActivation || useOneShotWindow;
        if (dedupeByReceiver && _alreadyHit.Contains(receiver))
            return;

        if (dedupeByReceiver)
            _alreadyHit.Add(receiver);

        Vector3 attackerPos = attackerRoot != null ? attackerRoot.position : transform.position;
        Vector3 hitPoint = other.ClosestPoint(attackerPos);

        Vector3 dir = hitPoint - attackerPos;
        if (dir.sqrMagnitude > 0.0001f)
            dir.Normalize();
        else
            dir = attackerRoot != null ? attackerRoot.forward : transform.forward;

        HitPayload payload = new HitPayload
        {
            damage = baseDamage,
            hitType = hitType,
            hitPoint = hitPoint,
            hitDirection = dir,
            attacker = attackerRoot,
            canParry = canParry,
            canPerfectDodge = canPerfectDodge,
            unblockable = unblockable
        };

        if (enableLogs)
            Debug.Log($"[AttackHitbox] Hit {receiver} | dmg={payload.damage} type={payload.hitType}", this);

        receiver.ReceiveHit(payload);
    }

    void ScanExpandedTargets()
    {
        int hitCount = OverlapExpandedHitbox();
        for (int i = 0; i < hitCount; i++)
        {
            Collider other = _expandedHitResults[i];
            _expandedHitResults[i] = null;
            TryApplyHit(other);
        }
    }

    int OverlapExpandedHitbox()
    {
        QueryTriggerInteraction queryTriggerInteraction = ignoreTriggerColliders
            ? QueryTriggerInteraction.Ignore
            : QueryTriggerInteraction.Collide;
        float effectivePadding = GetEffectiveExpandedPadding();

        if (_col is BoxCollider boxCollider)
            return OverlapExpandedBox(boxCollider, queryTriggerInteraction, effectivePadding);

        if (_col is SphereCollider sphereCollider)
            return OverlapExpandedSphere(sphereCollider, queryTriggerInteraction, effectivePadding);

        if (_col is CapsuleCollider capsuleCollider)
            return OverlapExpandedCapsule(capsuleCollider, queryTriggerInteraction, effectivePadding);

        if (_col is MeshCollider meshCollider)
            return OverlapExpandedMesh(meshCollider, queryTriggerInteraction, effectivePadding);

        Bounds bounds = _col.bounds;
        float radius = Mathf.Max(bounds.extents.x, Mathf.Max(bounds.extents.y, bounds.extents.z)) + effectivePadding;
        return Physics.OverlapSphereNonAlloc(bounds.center, radius, _expandedHitResults, hitLayers, queryTriggerInteraction);
    }

    float GetEffectiveExpandedPadding()
    {
        float padding = Mathf.Max(0f, expandedPadding);
        if (_col is MeshCollider)
            padding *= Mathf.Clamp(meshExpandedPaddingScale, 0.1f, 1f);

        return padding;
    }

    bool ShouldShowRuntimePreview()
    {
        return showRuntimeHitboxPreview && (Application.isEditor || Debug.isDebugBuild);
    }

    void RefreshExecutionState()
    {
        bool shouldRun = (_col != null && _col.enabled)
            || (_previewRoot != null && _previewRoot.activeSelf);

        if (enabled != shouldRun)
            enabled = shouldRun;
    }

    void SetPreviewVisible(bool visible)
    {
        if (!visible || !ShouldShowRuntimePreview())
        {
            if (_previewRoot != null)
            {
                if (previewPersistSeconds > 0f && ShouldShowRuntimePreview())
                    _previewHideAtRealtime = Time.realtimeSinceStartup + previewPersistSeconds;
                else
                    _previewRoot.SetActive(false);
            }
            return;
        }

        EnsurePreviewRoot();
        UpdatePreviewMaterial();
        UpdatePreviewVisual();
        _previewHideAtRealtime = float.PositiveInfinity;

        if (_previewRoot != null)
            _previewRoot.SetActive(true);
    }

    void EnsurePreviewRoot()
    {
        if (_previewRoot != null)
            return;

        _previewRoot = new GameObject($"{name}_HitboxPreview");
        _previewRoot.hideFlags = HideFlags.HideAndDontSave;
        _previewRoot.transform.SetParent(transform, false);
        _previewRoot.transform.localPosition = Vector3.zero;
        _previewRoot.transform.localRotation = Quaternion.identity;
        _previewRoot.transform.localScale = Vector3.one;
    }

    void UpdatePreviewMaterial()
    {
        if (_previewMaterial == null)
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
                shader = Shader.Find("Unlit/Color");

            if (shader != null)
            {
                _previewMaterial = new Material(shader)
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
            }
        }

        if (_previewShellMaterial == null)
        {
            Shader shellShader = Shader.Find("Sprites/Default");
            if (shellShader == null)
                shellShader = Shader.Find("Unlit/Color");

            if (shellShader != null)
            {
                _previewShellMaterial = new Material(shellShader)
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
            }
        }

        for (int i = 0; i < _previewLines.Count; i++)
            ApplyPreviewLineStyle(_previewLines[i]);

        if (_previewShellRenderer != null)
            ApplyPreviewShellStyle(_previewShellRenderer);
    }

    void UpdatePreviewVisual()
    {
        if (_col == null || _previewRoot == null)
            return;

        float padding = previewUseExpandedShape && useExpandedHitDetection ? GetEffectiveExpandedPadding() : 0f;

        if (_col is BoxCollider boxCollider)
        {
            UpdateBoxPreview(boxCollider, padding);
            return;
        }

        if (_col is SphereCollider sphereCollider)
        {
            UpdateSpherePreview(sphereCollider, padding);
            return;
        }

        if (_col is CapsuleCollider capsuleCollider)
        {
            UpdateCapsulePreview(capsuleCollider, padding);
            return;
        }

        if (_col is MeshCollider meshCollider)
        {
            UpdateMeshPreview(meshCollider, padding);
            return;
        }

        UpdateBoundsPreview(_col.bounds, padding);
    }

    void UpdateBoxPreview(BoxCollider boxCollider, float padding)
    {
        Vector3 absScale = AbsVector(transform.lossyScale);
        Vector3 center = transform.TransformPoint(boxCollider.center);
        Vector3 halfExtents = Vector3.Scale(boxCollider.size * 0.5f, absScale) + (Vector3.one * padding);
        DrawOrientedBox(center, transform.rotation, halfExtents);
        UpdateBoxShell(center, transform.rotation, halfExtents);
    }

    void UpdateBoundsPreview(Bounds bounds, float padding)
    {
        DrawOrientedBox(bounds.center, Quaternion.identity, bounds.extents + (Vector3.one * padding));
        UpdateBoxShell(bounds.center, Quaternion.identity, bounds.extents + (Vector3.one * padding));
    }

    void DrawOrientedBox(Vector3 center, Quaternion rotation, Vector3 halfExtents)
    {
        Vector3 right = rotation * Vector3.right * halfExtents.x;
        Vector3 up = rotation * Vector3.up * halfExtents.y;
        Vector3 forward = rotation * Vector3.forward * halfExtents.z;

        Vector3 topFrontRight = center + right + up + forward;
        Vector3 bottomFrontRight = center + right - up + forward;
        Vector3 bottomFrontLeft = center - right - up + forward;
        Vector3 topFrontLeft = center - right + up + forward;
        Vector3 topBackRight = center + right + up - forward;
        Vector3 bottomBackRight = center + right - up - forward;
        Vector3 bottomBackLeft = center - right - up - forward;
        Vector3 topBackLeft = center - right + up - forward;

        SetPreviewQuadLoop(0, topFrontRight, bottomFrontRight, bottomFrontLeft, topFrontLeft);
        SetPreviewQuadLoop(1, topBackRight, bottomBackRight, bottomBackLeft, topBackLeft);
        SetPreviewSegment(2, topFrontRight, topBackRight);
        SetPreviewSegment(3, bottomFrontRight, bottomBackRight);
        SetPreviewSegment(4, bottomFrontLeft, bottomBackLeft);
        SetPreviewSegment(5, topFrontLeft, topBackLeft);
        HideUnusedPreviewLines(6);
    }

    void UpdateSpherePreview(SphereCollider sphereCollider, float padding)
    {
        Vector3 center = transform.TransformPoint(sphereCollider.center);
        float radius = sphereCollider.radius * MaxComponent(AbsVector(transform.lossyScale)) + padding;
        DrawSpherePreview(center, radius);
        UpdateSphereShell(center, radius);
    }

    void DrawSpherePreview(Vector3 center, float radius)
    {
        Vector3 right = transform.right.normalized;
        Vector3 up = transform.up.normalized;
        Vector3 forward = transform.forward.normalized;

        SetPreviewCircle(0, center, right, up, radius);
        SetPreviewCircle(1, center, right, forward, radius);
        SetPreviewCircle(2, center, up, forward, radius);
        HideUnusedPreviewLines(3);
    }

    void UpdateCapsulePreview(CapsuleCollider capsuleCollider, float padding)
    {
        Vector3 absScale = AbsVector(transform.lossyScale);
        int axis = Mathf.Clamp(capsuleCollider.direction, 0, 2);
        float axisScale = GetAxis(absScale, axis);
        float radiusScale = axis == 0
            ? Mathf.Max(absScale.y, absScale.z)
            : axis == 1
                ? Mathf.Max(absScale.x, absScale.z)
                : Mathf.Max(absScale.x, absScale.y);

        float radius = capsuleCollider.radius * radiusScale + padding;
        float height = Mathf.Max(capsuleCollider.height * axisScale, radius * 2f);
        float halfSegment = Mathf.Max(0f, (height * 0.5f) - radius);
        Vector3 center = transform.TransformPoint(capsuleCollider.center);
        Vector3 axisDirection = transform.TransformDirection(GetAxisVector(axis)).normalized;

        if (halfSegment <= 0.001f)
        {
            DrawSpherePreview(center, radius);
            UpdateSphereShell(center, radius);
            return;
        }

        Vector3 point0 = center + axisDirection * halfSegment;
        Vector3 point1 = center - axisDirection * halfSegment;
        DrawCapsulePreview(point0, point1, radius);
        UpdateCapsuleShell(point0, point1, radius);
    }

    void UpdateMeshPreview(MeshCollider meshCollider, float padding)
    {
        if (meshCollider.sharedMesh == null)
        {
            Bounds bounds = meshCollider.bounds;
            float fallbackRadius = Mathf.Max(bounds.extents.x, Mathf.Max(bounds.extents.y, bounds.extents.z)) + padding;
            DrawSpherePreview(bounds.center, fallbackRadius);
            UpdateSphereShell(bounds.center, fallbackRadius);
            return;
        }

        Bounds localBounds = meshCollider.sharedMesh.bounds;
        Vector3 absScale = AbsVector(transform.lossyScale);
        Vector3 scaledExtents = Vector3.Scale(localBounds.extents, absScale);
        int dominantAxis = DominantAxis(scaledExtents);
        float dominantExtent = GetAxis(scaledExtents, dominantAxis);
        float crossExtent = dominantAxis == 0
            ? Mathf.Max(scaledExtents.y, scaledExtents.z)
            : dominantAxis == 1
                ? Mathf.Max(scaledExtents.x, scaledExtents.z)
                : Mathf.Max(scaledExtents.x, scaledExtents.y);

        Vector3 center = transform.TransformPoint(localBounds.center);
        float radius = Mathf.Max(0.04f, crossExtent + padding);
        float halfSegment = Mathf.Max(0f, dominantExtent - radius);
        if (halfSegment <= 0.001f)
        {
            DrawSpherePreview(center, radius);
            UpdateSphereShell(center, radius);
            return;
        }

        Vector3 axisDirection = transform.TransformDirection(GetAxisVector(dominantAxis)).normalized;
        Vector3 point0 = center + axisDirection * halfSegment;
        Vector3 point1 = center - axisDirection * halfSegment;
        DrawCapsulePreview(point0, point1, radius);
        UpdateCapsuleShell(point0, point1, radius);
    }

    void DrawCapsulePreview(Vector3 point0, Vector3 point1, float radius)
    {
        Vector3 axisDirection = (point1 - point0).normalized;
        Vector3 tangent = GetPerpendicularAxis(axisDirection);
        Vector3 bitangent = Vector3.Cross(axisDirection, tangent).normalized;
        Vector3 center = (point0 + point1) * 0.5f;

        SetPreviewCircle(0, point0, tangent, bitangent, radius);
        SetPreviewCircle(1, point1, tangent, bitangent, radius);
        SetPreviewCircle(2, center, tangent, bitangent, radius);
        SetPreviewSegment(3, point0 + tangent * radius, point1 + tangent * radius);
        SetPreviewSegment(4, point0 - tangent * radius, point1 - tangent * radius);
        SetPreviewSegment(5, point0 + bitangent * radius, point1 + bitangent * radius);
        SetPreviewSegment(6, point0 - bitangent * radius, point1 - bitangent * radius);
        HideUnusedPreviewLines(7);
    }

    Vector3 GetPerpendicularAxis(Vector3 axisDirection)
    {
        Vector3 fallbackAxis = Mathf.Abs(Vector3.Dot(axisDirection, Vector3.up)) > 0.85f
            ? Vector3.right
            : Vector3.up;

        Vector3 perpendicular = Vector3.Cross(axisDirection, fallbackAxis);
        if (perpendicular.sqrMagnitude <= 0.0001f)
            perpendicular = Vector3.Cross(axisDirection, Vector3.forward);

        return perpendicular.normalized;
    }

    void SetPreviewQuadLoop(int index, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
    {
        LineRenderer line = GetPreviewLine(index, 4, true);
        line.SetPosition(0, a);
        line.SetPosition(1, b);
        line.SetPosition(2, c);
        line.SetPosition(3, d);
    }

    void SetPreviewSegment(int index, Vector3 start, Vector3 end)
    {
        LineRenderer line = GetPreviewLine(index, 2, false);
        line.SetPosition(0, start);
        line.SetPosition(1, end);
    }

    void SetPreviewCircle(int index, Vector3 center, Vector3 axisA, Vector3 axisB, float radius)
    {
        int segmentCount = Mathf.Clamp(previewCircleSegments, 8, 48);
        LineRenderer line = GetPreviewLine(index, segmentCount, true);
        Vector3 basisA = axisA.normalized;
        Vector3 basisB = axisB.normalized;

        for (int i = 0; i < segmentCount; i++)
        {
            float angle = (i / (float)segmentCount) * Mathf.PI * 2f;
            Vector3 offset = (basisA * Mathf.Cos(angle) + basisB * Mathf.Sin(angle)) * radius;
            line.SetPosition(i, center + offset);
        }
    }

    LineRenderer GetPreviewLine(int index, int positionCount, bool loop)
    {
        EnsurePreviewRoot();
        UpdatePreviewMaterial();

        while (_previewLines.Count <= index)
            _previewLines.Add(CreatePreviewLine(_previewLines.Count));

        LineRenderer line = _previewLines[index];
        if (line == null)
            line = CreatePreviewLine(index);

        line.gameObject.SetActive(true);
        line.positionCount = positionCount;
        line.loop = loop;
        ApplyPreviewLineStyle(line);
        return line;
    }

    LineRenderer CreatePreviewLine(int index)
    {
        GameObject lineObject = new GameObject($"PreviewLine_{index}");
        lineObject.hideFlags = HideFlags.HideAndDontSave;
        lineObject.transform.SetParent(_previewRoot.transform, false);

        LineRenderer line = lineObject.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.textureMode = LineTextureMode.Stretch;
        line.alignment = LineAlignment.View;
        line.numCapVertices = 0;
        line.numCornerVertices = 2;
        line.shadowCastingMode = ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.lightProbeUsage = LightProbeUsage.Off;
        line.reflectionProbeUsage = ReflectionProbeUsage.Off;
        ApplyPreviewLineStyle(line);

        if (_previewLines.Count > index)
            _previewLines[index] = line;

        return line;
    }

    void ApplyPreviewLineStyle(LineRenderer line)
    {
        if (line == null)
            return;

        if (_previewMaterial != null)
            line.sharedMaterial = _previewMaterial;

        line.widthMultiplier = previewLineWidth;
        line.startColor = previewColor;
        line.endColor = previewColor;
    }

    void UpdateBoxShell(Vector3 center, Quaternion rotation, Vector3 halfExtents)
    {
        MeshRenderer renderer = EnsurePreviewShell(PrimitiveType.Cube);
        if (renderer == null)
            return;

        Transform shellTransform = renderer.transform;
        shellTransform.SetPositionAndRotation(center, rotation);
        shellTransform.localScale = halfExtents * 2f;
    }

    void UpdateSphereShell(Vector3 center, float radius)
    {
        MeshRenderer renderer = EnsurePreviewShell(PrimitiveType.Sphere);
        if (renderer == null)
            return;

        Transform shellTransform = renderer.transform;
        shellTransform.SetPositionAndRotation(center, Quaternion.identity);
        shellTransform.localScale = Vector3.one * (radius * 2f);
    }

    void UpdateCapsuleShell(Vector3 point0, Vector3 point1, float radius)
    {
        MeshRenderer renderer = EnsurePreviewShell(PrimitiveType.Capsule);
        if (renderer == null)
            return;

        Vector3 axis = point1 - point0;
        float cylinderHeight = axis.magnitude;
        float totalHeight = cylinderHeight + (radius * 2f);
        Quaternion rotation = axis.sqrMagnitude > 0.0001f
            ? Quaternion.FromToRotation(Vector3.up, axis.normalized)
            : Quaternion.identity;

        Transform shellTransform = renderer.transform;
        shellTransform.SetPositionAndRotation((point0 + point1) * 0.5f, rotation);
        shellTransform.localScale = new Vector3(radius * 2f, totalHeight * 0.5f, radius * 2f);
    }

    MeshRenderer EnsurePreviewShell(PrimitiveType primitiveType)
    {
        EnsurePreviewRoot();
        UpdatePreviewMaterial();

        if (_previewShellRenderer != null &&
            _previewShellObject != null &&
            _previewShellType == primitiveType)
        {
            _previewShellObject.SetActive(true);
            ApplyPreviewShellStyle(_previewShellRenderer);
            return _previewShellRenderer;
        }

        if (_previewShellObject != null)
            Destroy(_previewShellObject);

        _previewShellObject = GameObject.CreatePrimitive(primitiveType);
        _previewShellObject.name = $"PreviewShell_{primitiveType}";
        _previewShellObject.hideFlags = HideFlags.HideAndDontSave;
        _previewShellObject.transform.SetParent(_previewRoot.transform, false);

        Collider shellCollider = _previewShellObject.GetComponent<Collider>();
        if (shellCollider != null)
            Destroy(shellCollider);

        _previewShellRenderer = _previewShellObject.GetComponent<MeshRenderer>();
        _previewShellType = primitiveType;
        ApplyPreviewShellStyle(_previewShellRenderer);
        return _previewShellRenderer;
    }

    void ApplyPreviewShellStyle(MeshRenderer renderer)
    {
        if (renderer == null || _previewShellMaterial == null)
            return;

        Color shellColor = previewColor;
        shellColor.a = previewShellAlpha;
        _previewShellMaterial.color = shellColor;
        renderer.sharedMaterial = _previewShellMaterial;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
    }

    void HideUnusedPreviewLines(int usedCount)
    {
        for (int i = usedCount; i < _previewLines.Count; i++)
        {
            if (_previewLines[i] != null)
                _previewLines[i].gameObject.SetActive(false);
        }
    }

    int OverlapExpandedBox(BoxCollider boxCollider, QueryTriggerInteraction queryTriggerInteraction, float padding)
    {
        Vector3 absScale = AbsVector(transform.lossyScale);
        Vector3 center = transform.TransformPoint(boxCollider.center);
        Vector3 halfExtents = Vector3.Scale(boxCollider.size * 0.5f, absScale) + (Vector3.one * padding);
        return Physics.OverlapBoxNonAlloc(center, halfExtents, _expandedHitResults, transform.rotation, hitLayers, queryTriggerInteraction);
    }

    int OverlapExpandedSphere(SphereCollider sphereCollider, QueryTriggerInteraction queryTriggerInteraction, float padding)
    {
        Vector3 center = transform.TransformPoint(sphereCollider.center);
        float radius = sphereCollider.radius * MaxComponent(AbsVector(transform.lossyScale)) + padding;
        return Physics.OverlapSphereNonAlloc(center, radius, _expandedHitResults, hitLayers, queryTriggerInteraction);
    }

    int OverlapExpandedCapsule(CapsuleCollider capsuleCollider, QueryTriggerInteraction queryTriggerInteraction, float padding)
    {
        Vector3 absScale = AbsVector(transform.lossyScale);
        int axis = Mathf.Clamp(capsuleCollider.direction, 0, 2);
        float axisScale = GetAxis(absScale, axis);
        float radiusScale = axis == 0
            ? Mathf.Max(absScale.y, absScale.z)
            : axis == 1
                ? Mathf.Max(absScale.x, absScale.z)
                : Mathf.Max(absScale.x, absScale.y);

        float radius = capsuleCollider.radius * radiusScale + padding;
        float height = Mathf.Max(capsuleCollider.height * axisScale, radius * 2f);
        float halfSegment = Mathf.Max(0f, (height * 0.5f) - radius);
        Vector3 center = transform.TransformPoint(capsuleCollider.center);
        Vector3 axisDirection = transform.TransformDirection(GetAxisVector(axis)).normalized;
        Vector3 point0 = center + axisDirection * halfSegment;
        Vector3 point1 = center - axisDirection * halfSegment;

        return Physics.OverlapCapsuleNonAlloc(point0, point1, radius, _expandedHitResults, hitLayers, queryTriggerInteraction);
    }

    int OverlapExpandedMesh(MeshCollider meshCollider, QueryTriggerInteraction queryTriggerInteraction, float padding)
    {
        if (meshCollider.sharedMesh == null)
        {
            Bounds fallbackBounds = meshCollider.bounds;
            float fallbackRadius = Mathf.Max(fallbackBounds.extents.x, Mathf.Max(fallbackBounds.extents.y, fallbackBounds.extents.z)) + padding;
            return Physics.OverlapSphereNonAlloc(fallbackBounds.center, fallbackRadius, _expandedHitResults, hitLayers, queryTriggerInteraction);
        }

        Bounds localBounds = meshCollider.sharedMesh.bounds;
        Vector3 absScale = AbsVector(transform.lossyScale);
        Vector3 scaledExtents = Vector3.Scale(localBounds.extents, absScale);
        int dominantAxis = DominantAxis(scaledExtents);
        float dominantExtent = GetAxis(scaledExtents, dominantAxis);
        float crossExtent = dominantAxis == 0
            ? Mathf.Max(scaledExtents.y, scaledExtents.z)
            : dominantAxis == 1
                ? Mathf.Max(scaledExtents.x, scaledExtents.z)
                : Mathf.Max(scaledExtents.x, scaledExtents.y);

        Vector3 center = transform.TransformPoint(localBounds.center);
        float radius = Mathf.Max(0.04f, crossExtent + padding);
        float halfSegment = Mathf.Max(0f, dominantExtent - radius);
        if (halfSegment <= 0.001f)
            return Physics.OverlapSphereNonAlloc(center, radius, _expandedHitResults, hitLayers, queryTriggerInteraction);

        Vector3 axisDirection = transform.TransformDirection(GetAxisVector(dominantAxis)).normalized;
        Vector3 point0 = center + axisDirection * halfSegment;
        Vector3 point1 = center - axisDirection * halfSegment;
        return Physics.OverlapCapsuleNonAlloc(point0, point1, radius, _expandedHitResults, hitLayers, queryTriggerInteraction);
    }

    void EnsureExpandedHitBuffer()
    {
        int requiredSize = Mathf.Max(4, expandedHitBufferSize);
        if (_expandedHitResults == null || _expandedHitResults.Length != requiredSize)
            _expandedHitResults = new Collider[requiredSize];
    }

    static Vector3 AbsVector(Vector3 value)
    {
        return new Vector3(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));
    }

    static float MaxComponent(Vector3 value)
    {
        return Mathf.Max(value.x, Mathf.Max(value.y, value.z));
    }

    static int DominantAxis(Vector3 value)
    {
        if (value.x >= value.y && value.x >= value.z)
            return 0;

        return value.y >= value.z ? 1 : 2;
    }

    static float GetAxis(Vector3 value, int axis)
    {
        switch (axis)
        {
            case 0:
                return value.x;
            case 1:
                return value.y;
            default:
                return value.z;
        }
    }

    static Vector3 GetAxisVector(int axis)
    {
        switch (axis)
        {
            case 0:
                return Vector3.right;
            case 1:
                return Vector3.up;
            default:
                return Vector3.forward;
        }
    }
}
