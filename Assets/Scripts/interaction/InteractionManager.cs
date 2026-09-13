using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// InteractionManager
/// - viewport 기준 Ray/SphereCast
/// - 프롬프트 프리팹을 런타임에 클론화하고 내부 레퍼런스 보정
/// - 시작 시 모든 InteractionOutlineRenderer를 비활성화(초기화)
/// </summary>
public class InteractionManager : MonoBehaviour
{
    [Header("레이 시작용 카메라 트랜스폼 (비우면 Camera.main 사용)")]
    public Transform cameraTransform;

    [Header("플레이어 루트 Transform (비우면 Player 태그로 자동 검색)")]
    public Transform playerRoot;

    [Header("상호작용 대상 레이어 마스크 (Interactable 계열만 체크)")]
    public LayerMask interactLayer;

    [Header("화면 기준점 (Viewport 0..1). X:좌->우, Y:밑->위")]
    public Vector2 viewportPoint = new Vector2(0.5f, 0.5f);

    [Header("탐지 최대 거리(미터)")]
    public float maxDistance = 5f;

    [Header("스피어캐스트 반지름(중앙 근접 폭)")]
    public float sphereRadius = 0.15f;

    [Header("프롬프트 프리팹(InteractionPromptUIController 포함)")]
    public InteractionPromptUIController promptPrefab;

    [Header("바운드 상단에서 더 올릴 오프셋(미터)")]
    public float promptYOffset = 0.08f;

    [Header("락온 리더(선택) - 연결 시 락온 중 상호작용 차단")]
    public UnityEngine.Object lockOnReader;

    [Header("입력 라우터(선택) - 없으면 F 키 폴백")]
    public UnityEngine.Object inputRouter;

    [Header("폴백 상호작용 키")]
    public KeyCode interactKey = KeyCode.F;

    [Header("조준 중 프롬프트 숨김 여부")]
    public bool hideWhenAiming = true;

    [Header("디버그 레이 시각화")]
    public bool debugDrawRay = false;

    [Header("호버 디바운스(초) - 짧은 깜박임 방지")]
    public float hoverGrace = 0.12f;

    IInteractable _current;
    InteractionPromptUIController _promptUI;
    Camera _cam;
    Transform _promptAnchor;

    float _lastHitTime = -10f;
    float _startTime;
    UnityEngine.Object _cachedInputRouter;
    MethodInfo _cachedInteractPressedMethod;
    MethodInfo _cachedIsAimingMethod;
    UnityEngine.Object _cachedLockOnReader;
    PropertyInfo _cachedIsLockedOnProperty;
    MethodInfo _cachedIsLockedOnMethod;

    public Transform PlayerRoot
    {
        get
        {
            if (playerRoot) return playerRoot;
            var go = GameObject.FindGameObjectWithTag("Player");
            if (go) playerRoot = go.transform;
            return playerRoot;
        }
    }

    void Awake()
    {
        _startTime = Time.time;
        _cam = Camera.main;
        if (!cameraTransform && _cam) cameraTransform = _cam.transform;
        CacheInputRouterMembers();
        CacheLockOnMembers();

        if (promptPrefab)
        {
            var prefabGO = promptPrefab.gameObject;
            var cloneGO = Instantiate(prefabGO);
            cloneGO.name = prefabGO.name + "(Clone)";

            // 활성화 후 내부 컴포넌트 확보
            cloneGO.SetActive(true);

            _promptUI = cloneGO.GetComponent<InteractionPromptUIController>();
            if (_promptUI == null)
            {
                Debug.LogError("[InteractionManager] Prompt prefab root missing InteractionPromptUIController.");
            }
            else
            {
                if (_promptUI.cam == null && _cam) _promptUI.cam = _cam;

                if (_promptUI.text == null)
                    _promptUI.text = _promptUI.GetComponentInChildren<TMP_Text>(true);
                if (_promptUI.background == null)
                    _promptUI.background = _promptUI.GetComponentInChildren<RectTransform>(true);

                if (_promptUI.text != null)
                {
                    _promptUI.text.gameObject.SetActive(true);
                    _promptUI.text.enabled = true;
                    var col = _promptUI.text.color;
                    _promptUI.text.color = new Color(col.r, col.g, col.b, 1f);
                    _promptUI.text.text = "";
                }

                var canvas = cloneGO.GetComponentInChildren<Canvas>(true);
                if (canvas != null)
                {
                    canvas.enabled = true;
                    if (canvas.worldCamera == null && _cam) canvas.worldCamera = _cam;
                }
                var cg = cloneGO.GetComponentInChildren<CanvasGroup>(true);
                if (cg != null) cg.alpha = 1f;

                if (_promptUI.background != null)
                    LayoutRebuilder.ForceRebuildLayoutImmediate(_promptUI.background);
            }

        }

        _promptAnchor = new GameObject("InteractionPromptAnchor").transform;
        _promptAnchor.gameObject.hideFlags = HideFlags.HideAndDontSave;

        // 씬 시작 시 모든 InteractionOutlineRenderer 강제 리셋(비활성)
        foreach (var o in FindObjectsOfType<InteractionOutlineRenderer>(true))
            o.SetActive(false);
    }

    void Update()
    {
        if (!cameraTransform) return;

        // 짧은 시작 지연으로 초기 깜박임 방지
        if (Time.time - _startTime < 0.12f) return;

        if (IsLockOn()) { ClearHover(); return; }
        if (hideWhenAiming && IsAiming()) { ClearHover(); return; }

        Ray centerRay;
        if (_cam != null)
            centerRay = _cam.ViewportPointToRay(new Vector3(viewportPoint.x, viewportPoint.y, 0f));
        else
            centerRay = new Ray(cameraTransform.position, cameraTransform.forward);

        if (debugDrawRay)
            Debug.DrawRay(centerRay.origin, centerRay.direction * maxDistance, Color.yellow);

        int mask = interactLayer.value;
        int playerLayer = LayerMask.NameToLayer("Player");
        if (playerLayer >= 0) mask &= ~(1 << playerLayer);

        bool didHit = Physics.SphereCast(centerRay, sphereRadius, out RaycastHit hit, maxDistance, mask, QueryTriggerInteraction.Collide);

        if (didHit) _lastHitTime = Time.time;

        if (!didHit)
        {
            ClearHover();
            return;
        }

        var hitTransform = hit.collider.GetComponentInParent<Transform>();
        if (hitTransform == null) { ClearHover(); return; }

        var interactable = hitTransform.GetComponentInParent<IInteractable>();
        if (interactable == null) { ClearHover(); return; }

        if (!ReferenceEquals(_current, interactable))
        {
            ClearHover(); // grace에 따라 내부에서 무시할 수 있음
            _current = interactable;
            _current.OnHoverStart();

            Vector3 anchorPos;
            var baseInt = interactable as BaseInteractable;
            if (baseInt != null && baseInt.anchor != null)
            {
                anchorPos = baseInt.anchor.position;
            }
            else
            {
                var b = hit.collider.bounds;
                anchorPos = new Vector3(b.center.x, b.max.y, b.center.z);
            }

            _promptAnchor.position = anchorPos + Vector3.up * promptYOffset;

            if (_promptUI) _promptUI.Show(_promptAnchor, _current.GetPromptText());
        }

        if (GetInteractPressed())
        {
            if (_current.TryInteract(this))
            {
                if (_promptUI) _promptUI.Show((interactable as Component).transform, _current.GetPromptText());
            }
        }
    }

    void ClearHover()
    {
        if (Time.time - _lastHitTime < hoverGrace) return;

        if (_current != null)
        {
            _current.OnHoverEnd();
            _current = null;
        }
        if (_promptUI) _promptUI.Hide();
    }

    bool GetInteractPressed()
    {
        if (inputRouter != null)
        {
            CacheInputRouterMembers();
            if (_cachedInteractPressedMethod != null)
            {
                var r = _cachedInteractPressedMethod.Invoke(inputRouter, null);
                if (r is bool b) return b;
            }
        }
        return Input.GetKeyDown(interactKey);
    }

    bool IsLockOn()
    {
        if (lockOnReader != null)
        {
            CacheLockOnMembers();
            if (_cachedIsLockedOnProperty != null)
            {
                var v = _cachedIsLockedOnProperty.GetValue(lockOnReader, null);
                if (v is bool b) return b;
            }
            if (_cachedIsLockedOnMethod != null)
            {
                var r = _cachedIsLockedOnMethod.Invoke(lockOnReader, null);
                if (r is bool b2) return b2;
            }
        }
        return false;
    }

    bool IsAiming()
    {
        if (inputRouter != null)
        {
            CacheInputRouterMembers();
            if (_cachedIsAimingMethod != null)
            {
                var r = _cachedIsAimingMethod.Invoke(inputRouter, null);
                if (r is bool b) return b;
            }
        }
        return false;
    }

    void CacheInputRouterMembers()
    {
        if (_cachedInputRouter == inputRouter) return;

        _cachedInputRouter = inputRouter;
        _cachedInteractPressedMethod = null;
        _cachedIsAimingMethod = null;

        if (inputRouter == null) return;

        var type = inputRouter.GetType();
        _cachedInteractPressedMethod = type.GetMethod("IsInteractPressed");
        _cachedIsAimingMethod = type.GetMethod("IsAiming");
    }

    void CacheLockOnMembers()
    {
        if (_cachedLockOnReader == lockOnReader) return;

        _cachedLockOnReader = lockOnReader;
        _cachedIsLockedOnProperty = null;
        _cachedIsLockedOnMethod = null;

        if (lockOnReader == null) return;

        var type = lockOnReader.GetType();
        _cachedIsLockedOnProperty = type.GetProperty("IsLockedOn");
        _cachedIsLockedOnMethod = type.GetMethod("IsLockedOn");
    }
}
