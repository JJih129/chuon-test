using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 월드스페이스 상호작용 프롬프트 컨트롤러 (로그 포함)
/// - Show 호출 시 로그 및 레이아웃 즉시 갱신
/// </summary>
[RequireComponent(typeof(CanvasGroup))]
public class InteractionPromptUIController : MonoBehaviour
{
    // [헤더] 빌보드 대상 카메라. 비우면 Camera.main 사용
    public Camera cam;

    // [헤더] 텍스트가 따라다닐 기준 추가 오프셋(사용시)
    public float yOffset = 0f;

    // [헤더] 표시할 TMP 텍스트(프리팹에서 드래그)
    public TMP_Text text;

    // [헤더] 배경 RectTransform (자동 크기 계산 대상)
    public RectTransform background;

    // [헤더] 이 거리보다 멀면 자동 숨김
    public float hideDistance = 12f;

    // [헤더] 화면 밖이면 자동 숨김 여부
    public bool clampToScreen = true;

    // [헤더] 빌보드 회전: 수평(Y)만 회전할지 여부
    public bool billboardYawOnly = false;
    [SerializeField] bool debugLogs = false;
    [SerializeField, Range(0.01f, 0.2f)] float refreshInterval = 1f / 20f;

    Transform _target;
    CanvasGroup _cg;
    float _nextRefreshAt;
    string _lastPrompt;

    void Awake()
    {
        if (!cam) cam = Camera.main;
        _cg = GetComponent<CanvasGroup>();
        Hide();
    }

    // 프롬프트 표시. 로그 + 자동 레퍼런스 보정 + 레이아웃 강제 갱신
    public void Show(Transform target, string prompt)
    {
        _target = target;

        // 자동 레퍼런스 보정
        if (text == null)
            text = GetComponentInChildren<TMP_Text>(true);
        if (background == null)
            background = GetComponentInChildren<RectTransform>(true);

        if (debugLogs)
            Debug.Log($"[Prompt] Show called. prompt='{prompt}' textRef={(text != null)} backgroundRef={(background != null)}");

        // 텍스트 세팅
        bool promptChanged = !string.Equals(_lastPrompt, prompt);
        if (text != null && promptChanged)
            text.text = prompt;
        _lastPrompt = prompt;

        // 활성화 먼저(레이아웃 계산은 활성화 상태에서만 정확)
        gameObject.SetActive(true);
        _cg.alpha = 1f;

        // 강제 레이아웃 갱신 (ContentSizeFitter / LayoutGroup 사용 시 필요)
        if (background != null && promptChanged)
            LayoutRebuilder.ForceRebuildLayoutImmediate(background);

        // 즉시 위치/회전 업데이트
        UpdateTransformImmediate();
    }

    public void Hide()
    {
        gameObject.SetActive(false);
        _target = null;
        _lastPrompt = null;
    }

    void UpdateTransformImmediate()
    {
        if (!_target || !cam) return;

        var pos = _target.position + Vector3.up * yOffset;
        transform.position = pos;

        Vector3 toCamera = cam.transform.position - pos;
        if (toCamera.sqrMagnitude > hideDistance * hideDistance) { Hide(); return; }

        if (clampToScreen)
        {
            var vp = cam.WorldToViewportPoint(pos);
            bool off = vp.z < 0 || vp.x < 0 || vp.x > 1 || vp.y < 0 || vp.y > 1;
            if (off) { Hide(); return; }
        }

        Vector3 toCam = cam.transform.position - transform.position;
        if (billboardYawOnly) toCam.y = 0f;
        if (toCam.sqrMagnitude > 1e-6f)
            transform.rotation = Quaternion.LookRotation(-toCam.normalized, Vector3.up);
    }

    void LateUpdate()
    {
        if (!_target) return;
        if (Time.unscaledTime < _nextRefreshAt) return;
        _nextRefreshAt = Time.unscaledTime + Mathf.Max(1f / 20f, refreshInterval);
        UpdateTransformImmediate();
    }
}
