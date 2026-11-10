using UnityEngine;

/// <summary>
/// 튜토리얼/로비 목표 표식. 반복 16 (플레이어 리뷰 #1): 불투명 Unlit 원판 + 기둥 + 뚜껑(거대한 노란 원판으로 읽힘)을
/// ObjectiveBeaconVisual(얇은 앰버 홀로그램 링 + 가는 빛기둥 + 쉐브론, 거리·각도 페이드, 연출 중 숨김)로 교체했다.
/// 공개 API(ConfigureRuntime / SetMarkerColor / SetVisible)와 직렬화 필드는 그대로 — 호출부 변경 없음.
/// 예전 레이아웃 값은 크기 힌트로만 쓴다: 링 반경 = ringScale.x / 2 (0.35~0.7 m), 기둥 높이 = 뚜껑 높이 + 1 m (1.8~2.6 m).
/// </summary>
[DisallowMultipleComponent]
public class TutorialWorldMarker : MonoBehaviour
{
    [Header("Target")]
    [SerializeField] private Transform attachTarget;
    [SerializeField] private Vector3 localOffset = new Vector3(0f, 0.04f, 0f);

    [Header("Layout")]
    [SerializeField] private Vector3 ringScale = new Vector3(0.85f, 0.025f, 0.85f);
    [SerializeField] private Vector3 beamLocalPosition = new Vector3(0f, 0.55f, 0f);
    [SerializeField] private Vector3 beamScale = new Vector3(0.08f, 0.28f, 0.08f);
    [SerializeField] private Vector3 capLocalPosition = new Vector3(0f, 0.98f, 0f);
    [SerializeField] private Vector3 capScale = new Vector3(0.24f, 0.10f, 0.24f);

    [Header("Style")]
    [SerializeField] private Color markerColor = new Color(0.18f, 0.85f, 1f, 0.9f);

    ObjectiveBeaconVisual _visual;

    void Awake()
    {
        EnsureState();
        Reattach();
        ApplyLayout();
        ApplyColor(markerColor);
    }

    public void ConfigureRuntime(
        Transform target,
        Color color,
        Vector3 runtimeRingScale,
        Vector3 runtimeBeamLocalPosition,
        Vector3 runtimeBeamScale,
        Vector3 runtimeCapLocalPosition,
        Vector3 runtimeCapScale)
    {
        attachTarget = target;
        markerColor = color;
        ringScale = runtimeRingScale;
        beamLocalPosition = runtimeBeamLocalPosition;
        beamScale = runtimeBeamScale;
        capLocalPosition = runtimeCapLocalPosition;
        capScale = runtimeCapScale;

        EnsureState();
        Reattach();
        ApplyLayout();
        ApplyColor(markerColor);
    }

    public void SetMarkerColor(Color color)
    {
        EnsureState();
        markerColor = color;
        ApplyColor(markerColor);
    }

    public void SetVisible(bool visible)
    {
        if (gameObject.activeSelf == visible)
            return;

        gameObject.SetActive(visible);
    }

    void EnsureState()
    {
        if (_visual == null)
        {
            _visual = GetComponent<ObjectiveBeaconVisual>();
            if (_visual == null)
                _visual = gameObject.AddComponent<ObjectiveBeaconVisual>();
        }

        // 씬/프리팹에 저장된 예전 원판·기둥·뚜껑 자식이 있으면 그리지 않는다
        HideLegacyPart("Ring");
        HideLegacyPart("Beam");
        HideLegacyPart("Cap");
    }

    void HideLegacyPart(string partName)
    {
        Transform legacy = transform.Find(partName);
        if (legacy != null && legacy.TryGetComponent(out Renderer r) && r.enabled)
            r.enabled = false;
    }

    void Reattach()
    {
        if (attachTarget == null)
            return;

        if (transform.parent == attachTarget)
            return;

        transform.SetParent(attachTarget, false);
    }

    void ApplyLayout()
    {
        transform.localPosition = localOffset;
        transform.localRotation = Quaternion.identity;
        transform.localScale = Vector3.one;

        float radius = Mathf.Clamp(ringScale.x * 0.5f, 0.35f, 0.7f);
        float height = Mathf.Clamp(capLocalPosition.y + 1f, 1.8f, 2.6f);
        if (_visual != null)
            _visual.Configure(markerColor, radius, height);
    }

    void ApplyColor(Color color)
    {
        if (_visual != null)
            _visual.SetColor(color);
    }
}
