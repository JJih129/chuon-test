// EnemyHPBar.cs
// 역할: 적/오브젝트 위 체력바. Slider 또는 Image(Filled) 지원. IHealth 이벤트 바인딩 자동구독.
// 변수 헤더는 한글 설명.

using System;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class EnemyHPBar : MonoBehaviour
{
    [Header("▶ 타겟 바인딩")]
    [Tooltip("체력바가 따라다닐 대상 트랜스폼 (Bind로 자동 설정 가능)")]
    public Transform target;

    [Header("▶ 위치/오프셋")]
    [Tooltip("타겟 기준 월드 오프셋 (예: 머리 위)")]
    public Vector3 offset = new Vector3(0f, 1.8f, 0f);

    [Header("▶ 슬라이더 사용(옵션)")]
    [Tooltip("Slider가 있으면 Slider로 채움 표시")]
    public Slider slider;

    [Header("▶ 이미지 채움 사용(옵션)")]
    [Tooltip("Fill용 Image (Image.Type = Filled 로 설정)")]
    public Image fillImage;

    [Header("▶ 동작 튜닝")]
    [Tooltip("월드 빌보드 Up 벡터")]
    public Vector3 billboardUp = Vector3.up;

    // 내부 캐시
    Camera _cam;
    Transform _camTransform;
    RectTransform _rt;
    bool _isUI;
    RectTransform _parentRT;
    float _nextRefreshAt;
    bool _isCurrentlyVisible = true;
    const float NearRefreshInterval = 1f / 6f;
    const float FarRefreshInterval = 1f / 2f;
    const float FarDistanceThreshold = 12f;

    // 바인딩된 IHealth (이벤트 해제용)
    IHealth _boundHealth;

    void Awake()
    {
        _cam = Camera.main;
        _camTransform = _cam != null ? _cam.transform : null;
        _rt = transform as RectTransform;
        _isUI = (_rt != null);
        _parentRT = _rt != null ? _rt.parent as RectTransform : null;

        // Slider 자동 탐색
        if (slider == null)
            slider = GetComponentInChildren<Slider>(true);

        // Fill Image 자동 탐색 (우선 이름으로, 없으면 첫 자식 Image)
        if (fillImage == null)
        {
            var named = transform.Find("Fill_Image");
            if (named != null) fillImage = named.GetComponent<Image>();
            if (fillImage == null)
            {
                // 자식에서 Image 찾되 Slider가 있으면 Slider의 이미지와 혼동되지 않도록 우선순위 조정
                var imgs = GetComponentsInChildren<Image>(true);
                foreach (var img in imgs)
                {
                    if (slider != null && img.transform.IsChildOf(slider.transform)) continue;
                    fillImage = img;
                    break;
                }
            }
        }

        if (slider == null && fillImage == null)
            Debug.LogWarning($"[EnemyHPBar] Slider와 Fill Image 둘 다 비어있음. ({name})", this);
    }

    void OnEnable()
    {
        _isCurrentlyVisible = gameObject.activeSelf;
        if (_cam == null)
        {
            _cam = Camera.main;
            _camTransform = _cam != null ? _cam.transform : null;
        }
    }

    void Update()
    {
        if (Time.unscaledTime < _nextRefreshAt)
            return;

        if (target == null)
        {
            SetVisible(false);
            return;
        }

        float sqrDistance = _camTransform != null
            ? (target.position - _camTransform.position).sqrMagnitude
            : 0f;
        float refreshInterval = sqrDistance > FarDistanceThreshold * FarDistanceThreshold
            ? FarRefreshInterval
            : NearRefreshInterval;
        _nextRefreshAt = Time.unscaledTime + refreshInterval;

        if (_isUI)
        {
            if (_cam == null)
            {
                _cam = Camera.main;
                _camTransform = _cam != null ? _cam.transform : null;
            }
            if (_cam == null) return;

            Vector3 screenPos = _cam.WorldToScreenPoint(target.position + offset);

            // 뒤편이면 숨김
            if (screenPos.z <= 0f)
            {
                SetVisible(false);
                return;
            }
            else
            {
                SetVisible(true);
            }

            if (_parentRT != null)
            {
                Vector2 anchored;
                bool ok = RectTransformUtility.ScreenPointToLocalPointInRectangle(_parentRT, screenPos, _cam, out anchored);
                if (ok) _rt.anchoredPosition = anchored;
                else _rt.position = screenPos;
            }
            else
            {
                transform.position = screenPos;
            }

            return;
        }

        // World-space 모드: 따라다니고 카메라를 바라봄
        transform.position = target.position + offset;
        if (_cam == null)
        {
            _cam = Camera.main;
            _camTransform = _cam != null ? _cam.transform : null;
        }
        if (_cam != null)
        {
            Vector3 lookDir = transform.position - _cam.transform.position;
            if (lookDir.sqrMagnitude > 0.000001f)
                transform.rotation = Quaternion.LookRotation(lookDir.normalized, billboardUp);
        }
    }

    void SetVisible(bool visible)
    {
        if (_isCurrentlyVisible == visible)
            return;

        _isCurrentlyVisible = visible;
        gameObject.SetActive(visible);
    }

    // ---------------- 외부 API ----------------
    // current/max 값으로 채움 갱신. Slider 우선, Image 채움 병행 지원.
    public void OnHPChangedCallback(int current, int max)
    {
        float value = (max <= 0) ? 0f : Mathf.Clamp01((float)current / (float)max);

        if (slider != null)
            slider.value = value;

        if (fillImage != null)
            fillImage.fillAmount = value;
    }

    public void Show() => SetVisible(true);
    public void Hide() => SetVisible(false);

    // 풀 반환/초기화
    public void ResetForPool()
    {
        Unbind();
        target = null;
        if (slider != null) slider.value = 0f;
        if (fillImage != null) fillImage.fillAmount = 0f;
        SetVisible(false);
    }

    // ---------------- IHealth 바인딩 ----------------
    // IHealth 구현체를 전달하면 이벤트 자동 구독하고 target 자동 설정(가능하면)
    public void Bind(IHealth health)
    {
        Unbind();

        if (health == null)
        {
            target = null;
            return;
        }

        _boundHealth = health;

        // IHealth가 Component라면 transform 바인딩
        if (health is Component comp)
            target = comp.transform;

        // 즉시 초기값 적용 안전 호출
        try { OnHPChangedCallback(_boundHealth.CurrentHP, _boundHealth.MaxHP); } catch { }

        // 이벤트 구독 (있으면)
        try
        {
            // 안전을 위해 기존 구독 제거 후 추가
            _boundHealth.OnHPChanged -= OnHPChangedCallback;
            _boundHealth.OnHealthChanged -= OnHPChangedCallback;
            _boundHealth.OnDied -= HandleBoundHealthDied;

            _boundHealth.OnHPChanged += OnHPChangedCallback;
            _boundHealth.OnHealthChanged += OnHPChangedCallback;
            _boundHealth.OnDied += HandleBoundHealthDied;
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[EnemyHPBar] IHealth 이벤트 구독 중 예외: " + ex.Message, this);
        }

        Show();
    }

    void HandleBoundHealthDied() => ResetForPool();

    void Unbind()
    {
        if (_boundHealth == null) return;
        try
        {
            _boundHealth.OnHPChanged -= OnHPChangedCallback;
            _boundHealth.OnHealthChanged -= OnHPChangedCallback;
            _boundHealth.OnDied -= HandleBoundHealthDied;
        }
        catch { }
        _boundHealth = null;
    }

    // ---------------- 유연한 바인딩 오버로드 ----------------
    public void Bind(UnityEngine.Object source)
    {
        if (source == null) { ResetForPool(); return; }
        if (source is IHealth ih) { Bind(ih); return; }
        if (source is Transform t) { Bind(t); return; }
        if (source is Component c) { Bind(c.transform); return; }
        Debug.LogWarning("[EnemyHPBar] Bind(UnityEngine.Object): 지원하지 않는 타입", this);
    }

    public void Bind(object obj)
    {
        if (obj == null) { ResetForPool(); return; }
        if (obj is IHealth ih) { Bind(ih); return; }
        if (obj is Transform t) { Bind(t); return; }
        if (obj is Component c) { Bind(c.transform); return; }
        Debug.LogWarning("[EnemyHPBar] Bind(object): 지원하지 않는 타입", this);
    }

    public void Bind(Transform t)
    {
        Unbind();
        target = t;
        Show();
    }

    public void Bind(MonoBehaviour mb)
    {
        Unbind();
        if (mb == null) { ResetForPool(); return; }
        target = mb.transform;
        Show();
    }

    void OnDestroy() => Unbind();
}
