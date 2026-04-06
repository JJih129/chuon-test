using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
public class LobbyPresentationController : MonoBehaviour
{
    sealed class MarkerVisual
    {
        public Transform target;
        public Transform root;
        public Transform fillRoot;
        public Transform ringRoot;
        public Transform beaconRoot;
        public Renderer fillRenderer;
        public Renderer ringRenderer;
        public Renderer beaconRenderer;
    }

    [SerializeField] private Transform playerRoot;
    [SerializeField] private Transform[] threatTargets;
    [SerializeField] private Transform approachTarget;
    [SerializeField] private Transform boardTarget;
    [SerializeField] private BaseInteractable elevatorInteractable;

    [Header("Threat Markers")]
    [SerializeField] private Vector3 threatMarkerOffset = new Vector3(0f, 0.03f, 0f);
    [SerializeField, Min(0.1f)] private float threatPulseSpeed = 3.4f;
    [SerializeField, Min(0f)] private float threatBaseScale = 1.55f;
    [SerializeField, Min(0f)] private float threatScalePulse = 0.14f;
    [SerializeField, Min(0f)] private float threatBeaconHeight = 1.9f;
    [SerializeField, Range(0f, 1f)] private float threatFillAlpha = 0.16f;
    [SerializeField, Range(0f, 1f)] private float threatRingAlpha = 0.55f;
    [SerializeField, Range(0f, 1f)] private float threatBeaconAlpha = 0.42f;
    [SerializeField] private Color threatColor = new Color(1f, 0.28f, 0.18f, 0.96f);

    [Header("Objective Marker")]
    [SerializeField] private Vector3 objectiveMarkerOffset = new Vector3(0f, 0.03f, 0f);
    [SerializeField, Min(0.1f)] private float objectivePulseSpeed = 2.5f;
    [SerializeField, Min(0f)] private float objectiveBaseScale = 1.8f;
    [SerializeField, Min(0f)] private float objectiveScalePulse = 0.16f;
    [SerializeField, Min(0f)] private float objectiveBeaconHeight = 2.4f;
    [SerializeField, Range(0f, 1f)] private float objectiveFillAlpha = 0.14f;
    [SerializeField, Range(0f, 1f)] private float objectiveRingAlpha = 0.42f;
    [SerializeField, Range(0f, 1f)] private float objectiveBeaconAlpha = 0.34f;
    [SerializeField] private Color approachColor = new Color(0.22f, 0.86f, 1f, 0.96f);
    [SerializeField] private Color boardColor = new Color(1f, 0.76f, 0.28f, 0.96f);

    [Header("Elevator Panel Pulse")]
    [SerializeField, Min(0.05f)] private float panelPulseOnDuration = 0.24f;
    [SerializeField, Min(0.05f)] private float panelPulseOffDuration = 0.12f;
    [SerializeField, Min(1)] private int panelPulseCount = 3;

    [Header("Elevator Panel Guide")]
    [SerializeField, Min(0.02f)] private float panelGuideWidth = 0.12f;
    [SerializeField, Min(0f)] private float panelGuidePlayerYOffset = 0.04f;
    [SerializeField, Min(0f)] private float panelGuideTargetYOffset = 0.02f;
    [SerializeField, Min(0.2f)] private float panelGuideMarkerScale = 0.9f;
    [SerializeField, Min(0.1f)] private float panelGuidePulseSpeed = 3.2f;
    [SerializeField, Range(0f, 1f)] private float panelGuideLineAlpha = 0.78f;
    [SerializeField, Range(0f, 1f)] private float panelGuideMarkerAlpha = 0.34f;
    [SerializeField] private Color panelGuideColor = new Color(1f, 0.80f, 0.30f, 0.96f);

    [Header("Shot Telegraph")]
    [SerializeField, Min(0.05f)] private float shotTelegraphDuration = 0.6f;
    [SerializeField, Min(0.02f)] private float shotTelegraphWidth = 0.18f;
    [SerializeField, Min(0f)] private float shotTelegraphStartYOffset = 0.02f;
    [SerializeField, Min(0f)] private float shotTelegraphEndYOffset = 0.04f;
    [SerializeField, Min(0.2f)] private float shotTelegraphEndScale = 1.1f;
    [SerializeField, Range(0f, 1f)] private float shotTelegraphLineAlpha = 0.92f;
    [SerializeField, Range(0f, 1f)] private float shotTelegraphTargetAlpha = 0.46f;
    [SerializeField] private Color shotTelegraphColor = new Color(1f, 0.42f, 0.18f, 0.98f);

    readonly List<MarkerVisual> _threatMarkers = new List<MarkerVisual>(8);
    MaterialPropertyBlock _propertyBlock;

    MarkerVisual _objectiveMarker;
    Transform _objectiveTarget;
    Color _objectiveColor;
    float _threatMarkersVisibleUntil;
    bool _threatMarkersActive;
    Coroutine _panelPulseRoutine;
    Coroutine _shotTelegraphRoutine;
    LineRenderer _shotTelegraphLine;
    Transform _shotTelegraphTargetMarker;
    Renderer _shotTelegraphTargetRenderer;
    LineRenderer _panelGuideLine;
    Transform _panelGuideMarker;
    Renderer _panelGuideMarkerRenderer;
    bool _panelGuideActive;

    static Material s_markerMaterial;

    public void ConfigureRuntime(
        Transform runtimePlayerRoot,
        Transform[] runtimeThreatTargets,
        Transform runtimeApproachTarget,
        Transform runtimeBoardTarget,
        BaseInteractable runtimeElevatorInteractable)
    {
        playerRoot = runtimePlayerRoot;
        threatTargets = runtimeThreatTargets;
        approachTarget = runtimeApproachTarget;
        boardTarget = runtimeBoardTarget;
        elevatorInteractable = runtimeElevatorInteractable;

        EnsureBuilt();
        HideThreatMarkers();
        HideObjectiveMarker();
        HideElevatorPanelGuide();
    }

    void Awake()
    {
        EnsureBuilt();
        enabled = false;
    }

    void OnDisable()
    {
        HideThreatMarkers();
        HideObjectiveMarker();
        HideElevatorPanelGuide();
        StopPanelPulseRoutine();
        StopShotTelegraphRoutine();
        SetPanelHighlight(false);
    }

    void OnDestroy()
    {
        StopPanelPulseRoutine();
        StopShotTelegraphRoutine();
        SetPanelHighlight(false);
        HideElevatorPanelGuide();
    }

    void LateUpdate()
    {
        // Only animate while a marker is visible to avoid idle per-frame cost in the lobby.
        bool keepUpdating = false;

        if (_threatMarkersActive)
        {
            keepUpdating = true;
            UpdateThreatMarkers();

            if (Time.realtimeSinceStartup >= _threatMarkersVisibleUntil)
                HideThreatMarkers();
        }

        if (_objectiveTarget != null)
        {
            keepUpdating = true;
            UpdateObjectiveMarker();
        }

        if (_panelGuideActive)
        {
            keepUpdating = true;
            UpdateElevatorPanelGuide();
        }

        if (!keepUpdating)
            enabled = false;
    }

    public void ShowThreatMarkers(float duration)
    {
        EnsureBuilt();

        bool hasVisibleMarker = false;
        for (int i = 0; i < _threatMarkers.Count; i++)
        {
            MarkerVisual marker = _threatMarkers[i];
            bool visible = marker != null && marker.target != null;
            SetMarkerVisible(marker, visible);
            hasVisibleMarker |= visible;
        }

        _threatMarkersActive = hasVisibleMarker;
        _threatMarkersVisibleUntil = Time.realtimeSinceStartup + Mathf.Max(0.1f, duration);
        enabled = _threatMarkersActive || _objectiveTarget != null;

        if (_threatMarkersActive)
            UpdateThreatMarkers();
    }

    public void HideThreatMarkers()
    {
        _threatMarkersActive = false;
        _threatMarkersVisibleUntil = 0f;

        for (int i = 0; i < _threatMarkers.Count; i++)
            SetMarkerVisible(_threatMarkers[i], false);

        if (_objectiveTarget == null)
            enabled = false;
    }

    public void ShowApproachMarker()
    {
        SetObjectiveMarker(approachTarget, approachColor);
    }

    public void ShowBoardMarker()
    {
        SetObjectiveMarker(boardTarget, boardColor);
    }

    public void HideObjectiveMarker()
    {
        _objectiveTarget = null;
        SetMarkerVisible(_objectiveMarker, false);

        if (!_threatMarkersActive)
            enabled = false;
    }

    public void PulseElevatorInteractable()
    {
        if (elevatorInteractable == null || elevatorInteractable.highlight == null)
            return;

        StopPanelPulseRoutine();
        _panelPulseRoutine = StartCoroutine(CoPulseElevatorInteractable());
    }

    public void ShowElevatorPanelGuide()
    {
        if (elevatorInteractable == null)
            return;

        EnsureBuilt();
        _panelGuideActive = true;
        if (_panelGuideLine != null)
        {
            _panelGuideLine.enabled = true;
            _panelGuideLine.gameObject.SetActive(true);
        }

        if (_panelGuideMarker != null)
            _panelGuideMarker.gameObject.SetActive(true);

        UpdateElevatorPanelGuide();
        enabled = true;
    }

    public void HideElevatorPanelGuide()
    {
        _panelGuideActive = false;

        if (_panelGuideLine != null)
        {
            _panelGuideLine.enabled = false;
            _panelGuideLine.gameObject.SetActive(false);
        }

        if (_panelGuideMarker != null)
            _panelGuideMarker.gameObject.SetActive(false);

        if (!_threatMarkersActive && _objectiveTarget == null)
            enabled = false;
    }

    public void ShowShotTelegraph(Transform origin, Vector3 targetPoint, float duration = -1f)
    {
        if (origin == null)
            return;

        EnsureBuilt();
        StopShotTelegraphRoutine();

        Vector3 start = origin.position + (Vector3.up * shotTelegraphStartYOffset);
        Vector3 end = targetPoint + (Vector3.up * shotTelegraphEndYOffset);

        if (_shotTelegraphLine != null)
        {
            Color lineColor = new Color(shotTelegraphColor.r, shotTelegraphColor.g, shotTelegraphColor.b, shotTelegraphLineAlpha);
            _shotTelegraphLine.startWidth = shotTelegraphWidth;
            _shotTelegraphLine.endWidth = shotTelegraphWidth;
            _shotTelegraphLine.startColor = lineColor;
            _shotTelegraphLine.endColor = lineColor;
            _shotTelegraphLine.SetPosition(0, start);
            _shotTelegraphLine.SetPosition(1, end);
            _shotTelegraphLine.enabled = true;
            _shotTelegraphLine.gameObject.SetActive(true);
        }

        if (_shotTelegraphTargetMarker != null)
        {
            _shotTelegraphTargetMarker.position = end;
            _shotTelegraphTargetMarker.localRotation = Quaternion.identity;
            _shotTelegraphTargetMarker.localScale = new Vector3(shotTelegraphEndScale, 0.008f, shotTelegraphEndScale);
            _shotTelegraphTargetMarker.gameObject.SetActive(true);
        }

        if (_shotTelegraphTargetRenderer != null)
            ApplyColor(_shotTelegraphTargetRenderer, shotTelegraphColor, shotTelegraphTargetAlpha);

        _shotTelegraphRoutine = StartCoroutine(CoShowShotTelegraph(Mathf.Max(0.1f, duration > 0f ? duration : shotTelegraphDuration)));
    }

    void EnsureBuilt()
    {
        RebuildThreatMarkersIfNeeded();

        if (_objectiveMarker == null)
            _objectiveMarker = CreateMarker("ObjectiveMarker");

        SetMarkerVisible(_objectiveMarker, false);
        EnsureShotTelegraphBuilt();
        EnsurePanelGuideBuilt();
    }

    void RebuildThreatMarkersIfNeeded()
    {
        int requiredCount = threatTargets != null ? threatTargets.Length : 0;

        while (_threatMarkers.Count < requiredCount)
        {
            MarkerVisual marker = CreateMarker($"ThreatMarker_{_threatMarkers.Count}");
            _threatMarkers.Add(marker);
        }

        for (int i = 0; i < _threatMarkers.Count; i++)
        {
            MarkerVisual marker = _threatMarkers[i];
            if (marker == null)
                continue;

            marker.target = i < requiredCount ? threatTargets[i] : null;
            SetMarkerVisible(marker, false);
        }
    }

    MarkerVisual CreateMarker(string rootName)
    {
        MarkerVisual marker = new MarkerVisual();
        GameObject rootObject = new GameObject(rootName);
        rootObject.transform.SetParent(transform, false);
        marker.root = rootObject.transform;

        marker.fillRoot = CreatePrimitiveChild(marker.root, "Fill", PrimitiveType.Cylinder, out marker.fillRenderer);
        marker.ringRoot = CreatePrimitiveChild(marker.root, "Ring", PrimitiveType.Cylinder, out marker.ringRenderer);
        marker.beaconRoot = CreatePrimitiveChild(marker.root, "Beacon", PrimitiveType.Cube, out marker.beaconRenderer);

        return marker;
    }

    Transform CreatePrimitiveChild(Transform parent, string name, PrimitiveType primitiveType, out Renderer renderer)
    {
        GameObject primitive = GameObject.CreatePrimitive(primitiveType);
        primitive.name = name;
        primitive.transform.SetParent(parent, false);

        Collider primitiveCollider = primitive.GetComponent<Collider>();
        if (primitiveCollider != null)
            Destroy(primitiveCollider);

        renderer = primitive.GetComponent<Renderer>();
        if (renderer != null)
        {
            renderer.sharedMaterial = GetMarkerMaterial();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        }

        primitive.SetActive(false);
        return primitive.transform;
    }

    void EnsureShotTelegraphBuilt()
    {
        if (_shotTelegraphLine == null)
        {
            GameObject lineObject = new GameObject("ShotTelegraphLine");
            lineObject.transform.SetParent(transform, false);
            _shotTelegraphLine = lineObject.AddComponent<LineRenderer>();
            _shotTelegraphLine.enabled = false;
            _shotTelegraphLine.positionCount = 2;
            _shotTelegraphLine.useWorldSpace = true;
            _shotTelegraphLine.alignment = LineAlignment.View;
            _shotTelegraphLine.textureMode = LineTextureMode.Stretch;
            _shotTelegraphLine.numCapVertices = 4;
            _shotTelegraphLine.numCornerVertices = 2;
            _shotTelegraphLine.shadowCastingMode = ShadowCastingMode.Off;
            _shotTelegraphLine.receiveShadows = false;
            _shotTelegraphLine.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            _shotTelegraphLine.sharedMaterial = GetMarkerMaterial();
            lineObject.SetActive(false);
        }

        if (_shotTelegraphTargetMarker == null)
            _shotTelegraphTargetMarker = CreatePrimitiveChild(transform, "ShotTelegraphTarget", PrimitiveType.Cylinder, out _shotTelegraphTargetRenderer);

        if (_shotTelegraphTargetMarker != null)
            _shotTelegraphTargetMarker.gameObject.SetActive(false);
    }

    void EnsurePanelGuideBuilt()
    {
        if (_panelGuideLine == null)
        {
            GameObject lineObject = new GameObject("ElevatorPanelGuideLine");
            lineObject.transform.SetParent(transform, false);
            _panelGuideLine = lineObject.AddComponent<LineRenderer>();
            _panelGuideLine.enabled = false;
            _panelGuideLine.positionCount = 2;
            _panelGuideLine.useWorldSpace = true;
            _panelGuideLine.alignment = LineAlignment.View;
            _panelGuideLine.textureMode = LineTextureMode.Stretch;
            _panelGuideLine.numCapVertices = 4;
            _panelGuideLine.numCornerVertices = 2;
            _panelGuideLine.shadowCastingMode = ShadowCastingMode.Off;
            _panelGuideLine.receiveShadows = false;
            _panelGuideLine.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            _panelGuideLine.sharedMaterial = GetMarkerMaterial();
            lineObject.SetActive(false);
        }

        if (_panelGuideMarker == null)
            _panelGuideMarker = CreatePrimitiveChild(transform, "ElevatorPanelGuideMarker", PrimitiveType.Cylinder, out _panelGuideMarkerRenderer);

        if (_panelGuideMarker != null)
            _panelGuideMarker.gameObject.SetActive(false);
    }

    void UpdateThreatMarkers()
    {
        // Spawn warnings are short-lived and reuse prebuilt primitives instead of spawning FX.
        float pulse = 0.5f + (Mathf.Sin(Time.unscaledTime * threatPulseSpeed) * 0.5f);

        for (int i = 0; i < _threatMarkers.Count; i++)
        {
            MarkerVisual marker = _threatMarkers[i];
            if (marker == null || marker.target == null)
                continue;

            float emphasis = GetMarkerEmphasis(marker.target.position, 5.5f);
            UpdateMarkerVisual(
                marker,
                marker.target.position + threatMarkerOffset,
                threatColor,
                threatBaseScale,
                threatScalePulse,
                threatBeaconHeight,
                threatFillAlpha,
                threatRingAlpha,
                threatBeaconAlpha,
                pulse,
                emphasis);
        }
    }

    void UpdateObjectiveMarker()
    {
        if (_objectiveMarker == null || _objectiveTarget == null)
            return;

        float pulse = 0.5f + (Mathf.Sin(Time.unscaledTime * objectivePulseSpeed) * 0.5f);
        float emphasis = GetMarkerEmphasis(_objectiveTarget.position, 7.2f);

        UpdateMarkerVisual(
            _objectiveMarker,
            _objectiveTarget.position + objectiveMarkerOffset,
            _objectiveColor,
            objectiveBaseScale,
            objectiveScalePulse,
            objectiveBeaconHeight,
            objectiveFillAlpha,
            objectiveRingAlpha,
            objectiveBeaconAlpha,
            pulse,
            emphasis);
    }

    void UpdateElevatorPanelGuide()
    {
        if (!_panelGuideActive || elevatorInteractable == null || playerRoot == null)
            return;

        Transform targetTransform = elevatorInteractable.anchor != null ? elevatorInteractable.anchor : elevatorInteractable.transform;
        if (targetTransform == null)
            return;

        Vector3 start = playerRoot.position + (Vector3.up * panelGuidePlayerYOffset);
        Vector3 end = targetTransform.position + (Vector3.up * panelGuideTargetYOffset);
        float pulse = 0.5f + (Mathf.Sin(Time.unscaledTime * panelGuidePulseSpeed) * 0.5f);
        float emphasis = GetMarkerEmphasis(targetTransform.position, 4.4f);

        if (_panelGuideLine != null)
        {
            Color lineColor = panelGuideColor;
            lineColor.a = Mathf.Clamp01(panelGuideLineAlpha + (pulse * 0.08f) + (emphasis * 0.12f));
            _panelGuideLine.startWidth = panelGuideWidth;
            _panelGuideLine.endWidth = panelGuideWidth;
            _panelGuideLine.startColor = lineColor;
            _panelGuideLine.endColor = lineColor;
            _panelGuideLine.SetPosition(0, start);
            _panelGuideLine.SetPosition(1, end);
        }

        if (_panelGuideMarker != null)
        {
            float scale = panelGuideMarkerScale * (1f + (pulse * 0.08f) + (emphasis * 0.10f));
            _panelGuideMarker.position = end;
            _panelGuideMarker.localRotation = Quaternion.identity;
            _panelGuideMarker.localScale = new Vector3(scale, 0.01f, scale);
        }

        if (_panelGuideMarkerRenderer != null)
            ApplyColor(_panelGuideMarkerRenderer, panelGuideColor, panelGuideMarkerAlpha + (pulse * 0.08f) + (emphasis * 0.10f));
    }

    void UpdateMarkerVisual(
        MarkerVisual marker,
        Vector3 worldPosition,
        Color color,
        float baseScale,
        float scalePulse,
        float beaconHeight,
        float fillAlpha,
        float ringAlpha,
        float beaconAlpha,
        float pulse,
        float emphasis)
    {
        if (marker == null || marker.root == null)
            return;

        marker.root.position = worldPosition;

        float scale = baseScale * (1f + (scalePulse * pulse) + (emphasis * 0.12f));
        float ringScale = scale * (1.08f + (pulse * 0.08f));
        float beamHeight = beaconHeight * (1f + (emphasis * 0.08f));
        float beamWidth = Mathf.Max(0.08f, scale * 0.12f);

        if (marker.fillRoot != null)
        {
            marker.fillRoot.localPosition = Vector3.zero;
            marker.fillRoot.localRotation = Quaternion.identity;
            marker.fillRoot.localScale = new Vector3(scale, 0.0065f, scale);
        }

        if (marker.ringRoot != null)
        {
            marker.ringRoot.localPosition = Vector3.zero;
            marker.ringRoot.localRotation = Quaternion.identity;
            marker.ringRoot.localScale = new Vector3(ringScale, 0.0105f, ringScale);
        }

        if (marker.beaconRoot != null)
        {
            marker.beaconRoot.localPosition = new Vector3(0f, (beamHeight * 0.5f) + 0.04f, 0f);
            marker.beaconRoot.localRotation = Quaternion.identity;
            marker.beaconRoot.localScale = new Vector3(beamWidth, beamHeight, beamWidth);
        }

        ApplyColor(marker.fillRenderer, color, fillAlpha + (emphasis * 0.08f));
        ApplyColor(marker.ringRenderer, color, ringAlpha + (emphasis * 0.10f));
        ApplyColor(marker.beaconRenderer, color, beaconAlpha + (pulse * 0.06f) + (emphasis * 0.08f));
    }

    float GetMarkerEmphasis(Vector3 targetPosition, float boostDistance)
    {
        if (playerRoot == null || boostDistance <= 0.01f)
            return 0f;

        Vector3 playerPosition = playerRoot.position;
        playerPosition.y = 0f;
        targetPosition.y = 0f;

        float distance = Vector3.Distance(playerPosition, targetPosition);
        if (distance >= boostDistance)
            return 0f;

        return 1f - Mathf.Clamp01(distance / boostDistance);
    }

    void SetObjectiveMarker(Transform target, Color color)
    {
        EnsureBuilt();

        // A single reusable beacon tracks the current navigation objective.
        _objectiveTarget = target;
        _objectiveColor = color;
        bool visible = _objectiveTarget != null;
        SetMarkerVisible(_objectiveMarker, visible);
        enabled = visible || _threatMarkersActive;

        if (visible)
            UpdateObjectiveMarker();
    }

    void SetMarkerVisible(MarkerVisual marker, bool visible)
    {
        if (marker == null || marker.root == null)
            return;

        marker.root.gameObject.SetActive(visible);
    }

    void ApplyColor(Renderer renderer, Color color, float alpha)
    {
        if (renderer == null)
            return;

        color.a = Mathf.Clamp01(alpha);
        MaterialPropertyBlock propertyBlock = GetOrCreatePropertyBlock();
        renderer.GetPropertyBlock(propertyBlock);
        propertyBlock.SetColor("_BaseColor", color);
        propertyBlock.SetColor("_Color", color);
        renderer.SetPropertyBlock(propertyBlock);
    }

    MaterialPropertyBlock GetOrCreatePropertyBlock()
    {
        if (_propertyBlock == null)
            _propertyBlock = new MaterialPropertyBlock();

        return _propertyBlock;
    }

    IEnumerator CoPulseElevatorInteractable()
    {
        int pulseCount = Mathf.Max(1, panelPulseCount);
        for (int i = 0; i < pulseCount; i++)
        {
            SetPanelHighlight(true);
            yield return new WaitForSecondsRealtime(panelPulseOnDuration);
            SetPanelHighlight(false);

            if (i < pulseCount - 1)
                yield return new WaitForSecondsRealtime(panelPulseOffDuration);
        }

        _panelPulseRoutine = null;
    }

    void StopPanelPulseRoutine()
    {
        if (_panelPulseRoutine == null)
            return;

        StopCoroutine(_panelPulseRoutine);
        _panelPulseRoutine = null;
    }

    void StopShotTelegraphRoutine()
    {
        if (_shotTelegraphRoutine != null)
        {
            StopCoroutine(_shotTelegraphRoutine);
            _shotTelegraphRoutine = null;
        }

        if (_shotTelegraphLine != null)
        {
            _shotTelegraphLine.enabled = false;
            _shotTelegraphLine.gameObject.SetActive(false);
        }

        if (_shotTelegraphTargetMarker != null)
            _shotTelegraphTargetMarker.gameObject.SetActive(false);
    }

    IEnumerator CoShowShotTelegraph(float duration)
    {
        yield return new WaitForSecondsRealtime(duration);
        if (_shotTelegraphLine != null)
        {
            _shotTelegraphLine.enabled = false;
            _shotTelegraphLine.gameObject.SetActive(false);
        }

        if (_shotTelegraphTargetMarker != null)
            _shotTelegraphTargetMarker.gameObject.SetActive(false);

        _shotTelegraphRoutine = null;
    }

    void SetPanelHighlight(bool active)
    {
        if (elevatorInteractable == null || elevatorInteractable.highlight == null)
            return;

        elevatorInteractable.highlight.SetActive(active);
    }

    static Material GetMarkerMaterial()
    {
        if (s_markerMaterial != null)
            return s_markerMaterial;

        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
            shader = Shader.Find("Unlit/Color");
        if (shader == null)
            shader = Shader.Find("Sprites/Default");

        s_markerMaterial = new Material(shader)
        {
            name = "LobbyPresentationMarkerMaterial"
        };

        return s_markerMaterial;
    }
}
