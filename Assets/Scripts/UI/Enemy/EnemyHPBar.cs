using System;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class EnemyHPBar : MonoBehaviour
{
    const float PositionEpsilonSqr = 0.000001f;

    public Transform target;
    public Vector3 offset = new Vector3(0f, 1.8f, 0f);
    public Slider slider;
    public Image fillImage;
    public Vector3 billboardUp = Vector3.up;

    Camera _cam;
    Transform _camTransform;
    RectTransform _rt;
    Transform _tr;
    bool _isUI;
    RectTransform _parentRT;
    IHealth _boundHealth;

    void Awake()
    {
        _tr = transform;
        _cam = Camera.main;
        _camTransform = _cam != null ? _cam.transform : null;
        _rt = _tr as RectTransform;
        _isUI = _rt != null;
        _parentRT = _rt != null ? _rt.parent as RectTransform : null;

        if (slider == null)
            slider = GetComponentInChildren<Slider>(true);

        if (fillImage == null)
        {
            var named = transform.Find("Fill_Image");
            if (named != null)
                fillImage = named.GetComponent<Image>();

            if (fillImage == null)
            {
                var images = GetComponentsInChildren<Image>(true);
                foreach (var image in images)
                {
                    if (slider != null && image.transform.IsChildOf(slider.transform))
                        continue;

                    fillImage = image;
                    break;
                }
            }
        }

        if (slider == null && fillImage == null)
            Debug.LogWarning($"[EnemyHPBar] Slider and Fill Image are both missing. ({name})", this);
    }

    void OnEnable()
    {
        EnsureCamera();
    }

    void Update()
    {
        if (target == null)
        {
            if (gameObject.activeSelf)
                gameObject.SetActive(false);
            return;
        }

        if (_isUI)
        {
            EnsureCamera();
            if (_cam == null) return;

            Vector3 screenPos = _cam.WorldToScreenPoint(target.position + offset);

            if (screenPos.z <= 0f)
            {
                if (gameObject.activeSelf)
                    gameObject.SetActive(false);
                return;
            }

            if (!gameObject.activeSelf)
                gameObject.SetActive(true);

            if (_parentRT != null)
            {
                if (RectTransformUtility.ScreenPointToLocalPointInRectangle(_parentRT, screenPos, _cam, out Vector2 anchored))
                {
                    if ((_rt.anchoredPosition - anchored).sqrMagnitude > PositionEpsilonSqr)
                        _rt.anchoredPosition = anchored;
                }
                else
                {
                    if ((_tr.position - screenPos).sqrMagnitude > PositionEpsilonSqr)
                        _tr.position = screenPos;
                }
            }
            else
            {
                if ((_tr.position - screenPos).sqrMagnitude > PositionEpsilonSqr)
                    _tr.position = screenPos;
            }

            return;
        }

        Vector3 worldPos = target.position + offset;
        if ((_tr.position - worldPos).sqrMagnitude > PositionEpsilonSqr)
            _tr.position = worldPos;

        EnsureCamera();
        if (_camTransform != null)
        {
            Vector3 lookDir = _tr.position - _camTransform.position;
            if (lookDir.sqrMagnitude > 0.000001f)
            {
                Quaternion desiredRotation = Quaternion.LookRotation(lookDir.normalized, billboardUp);
                if (Quaternion.Angle(_tr.rotation, desiredRotation) > 0.01f)
                    _tr.rotation = desiredRotation;
            }
        }
    }

    public void OnHPChangedCallback(int current, int max)
    {
        float value = max <= 0 ? 0f : Mathf.Clamp01((float)current / max);

        if (slider != null && !Mathf.Approximately(slider.value, value))
            slider.value = value;

        if (fillImage != null && !Mathf.Approximately(fillImage.fillAmount, value))
            fillImage.fillAmount = value;
    }

    public void Show()
    {
        if (!gameObject.activeSelf)
            gameObject.SetActive(true);
    }

    public void Hide()
    {
        if (gameObject.activeSelf)
            gameObject.SetActive(false);
    }

    public void ResetForPool()
    {
        Unbind();
        target = null;

        if (slider != null && !Mathf.Approximately(slider.value, 0f))
            slider.value = 0f;

        if (fillImage != null && !Mathf.Approximately(fillImage.fillAmount, 0f))
            fillImage.fillAmount = 0f;

        Hide();
    }

    public void Bind(IHealth health)
    {
        Unbind();

        if (health == null)
        {
            target = null;
            return;
        }

        _boundHealth = health;

        if (health is Component component)
            target = component.transform;

        try
        {
            OnHPChangedCallback(_boundHealth.CurrentHP, _boundHealth.MaxHP);
        }
        catch
        {
        }

        try
        {
            _boundHealth.OnHPChanged -= OnHPChangedCallback;
            _boundHealth.OnHealthChanged -= OnHPChangedCallback;
            _boundHealth.OnDied -= HandleBoundHealthDied;

            _boundHealth.OnHPChanged += OnHPChangedCallback;
            _boundHealth.OnHealthChanged += OnHPChangedCallback;
            _boundHealth.OnDied += HandleBoundHealthDied;
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[EnemyHPBar] Failed to subscribe to IHealth events: " + ex.Message, this);
        }

        Show();
    }

    void HandleBoundHealthDied()
    {
        ResetForPool();
    }

    void Unbind()
    {
        if (_boundHealth == null) return;

        try
        {
            _boundHealth.OnHPChanged -= OnHPChangedCallback;
            _boundHealth.OnHealthChanged -= OnHPChangedCallback;
            _boundHealth.OnDied -= HandleBoundHealthDied;
        }
        catch
        {
        }

        _boundHealth = null;
    }

    void EnsureCamera()
    {
        if (_cam == null)
            _cam = Camera.main;

        _camTransform = _cam != null ? _cam.transform : null;
    }

    public void Bind(UnityEngine.Object source)
    {
        if (source == null)
        {
            ResetForPool();
            return;
        }

        if (source is IHealth health)
        {
            Bind(health);
            return;
        }

        if (source is Transform transformSource)
        {
            Bind(transformSource);
            return;
        }

        if (source is Component component)
        {
            Bind(component.transform);
            return;
        }

        Debug.LogWarning("[EnemyHPBar] Unsupported Bind(UnityEngine.Object) source.", this);
    }

    public void Bind(object obj)
    {
        if (obj == null)
        {
            ResetForPool();
            return;
        }

        if (obj is IHealth health)
        {
            Bind(health);
            return;
        }

        if (obj is Transform transformSource)
        {
            Bind(transformSource);
            return;
        }

        if (obj is Component component)
        {
            Bind(component.transform);
            return;
        }

        Debug.LogWarning("[EnemyHPBar] Unsupported Bind(object) source.", this);
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
        if (mb == null)
        {
            ResetForPool();
            return;
        }

        target = mb.transform;
        Show();
    }

    void OnDestroy()
    {
        Unbind();
    }
}
