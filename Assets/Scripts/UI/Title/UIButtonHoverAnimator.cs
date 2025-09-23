// Assets/Scripts/UIButtonHoverAnimator.cs
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

[DisallowMultipleComponent]
public class UIButtonHoverAnimator : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [Header("▶ 그룹 (RectTransform 또는 일반 GameObject 가능)")]
    [Tooltip("버튼과 배경을 자식으로 둔 부모. UI일 경우 RectTransform, 일반 오브젝트도 가능.")]
    public Transform groupTransform;

    [Header("▶ 글로우")]
    public GameObject glowObject;

    [Header("▶ 애니메이션 튜닝")]
    public Vector2 hoverOffset = new Vector2(20f, 0f);
    public float duration = 0.18f;
    public AnimationCurve easeCurve = default;
    public bool disableGlowOnExit = true;

    // 내부
    RectTransform _rect;
    Vector2 _originalAnchoredPos;
    Vector3 _originalLocalPos;
    Image _glowImage;
    Coroutine _moveCoroutine;
    Coroutine _glowCoroutine;

    void Reset() { if (easeCurve == null || easeCurve.length == 0) easeCurve = AnimationCurve.EaseInOut(0, 0, 1, 1); }

    void Awake()
    {
        if (groupTransform == null) groupTransform = GetComponent<Transform>();
        _rect = groupTransform as RectTransform;
        if (_rect != null)
            _originalAnchoredPos = _rect.anchoredPosition;
        else
            _originalLocalPos = groupTransform.localPosition;

        if (glowObject != null)
        {
            _glowImage = glowObject.GetComponent<Image>();
            if (_glowImage != null) _glowImage.raycastTarget = false;
            glowObject.SetActive(false);
        }
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        Vector2 targetAnchored = _originalAnchoredPos + hoverOffset;
        Vector3 targetLocal = _originalLocalPos + new Vector3(hoverOffset.x, hoverOffset.y, 0f);

        if (_moveCoroutine != null) StopCoroutine(_moveCoroutine);
        _moveCoroutine = StartCoroutine(AnimateMove((_rect != null) ? (Vector3)targetAnchored : targetLocal, duration));

        if (glowObject != null)
        {
            glowObject.SetActive(true);
            if (_glowImage != null)
            {
                if (_glowCoroutine != null) StopCoroutine(_glowCoroutine);
                _glowCoroutine = StartCoroutine(AnimateGlowAlpha(0f, 1f, duration));
            }
        }
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        Vector2 targetAnchored = _originalAnchoredPos;
        Vector3 targetLocal = _originalLocalPos;

        if (_moveCoroutine != null) StopCoroutine(_moveCoroutine);
        _moveCoroutine = StartCoroutine(AnimateMove((_rect != null) ? (Vector3)targetAnchored : targetLocal, duration));

        if (glowObject != null && _glowImage != null)
        {
            if (_glowCoroutine != null) StopCoroutine(_glowCoroutine);
            _glowCoroutine = StartCoroutine(AnimateGlowAlpha(_glowImage.color.a, 0f, duration, () =>
            {
                if (disableGlowOnExit && glowObject != null) glowObject.SetActive(false);
            }));
        }
        else if (glowObject != null && disableGlowOnExit)
        {
            glowObject.SetActive(false);
        }
    }

    IEnumerator AnimateMove(Vector3 target, float time)
    {
        float t = 0f;
        if (_rect != null)
        {
            Vector2 start = _rect.anchoredPosition;
            Vector2 end = (Vector2)target;
            while (t < time)
            {
                t += Time.unscaledDeltaTime;
                float p = Mathf.Clamp01(t / time);
                float e = easeCurve.Evaluate(p);
                _rect.anchoredPosition = Vector2.LerpUnclamped(start, end, e);
                yield return null;
            }
            _rect.anchoredPosition = end;
        }
        else
        {
            Vector3 start = groupTransform.localPosition;
            Vector3 end = target;
            while (t < time)
            {
                t += Time.unscaledDeltaTime;
                float p = Mathf.Clamp01(t / time);
                float e = easeCurve.Evaluate(p);
                groupTransform.localPosition = Vector3.LerpUnclamped(start, end, e);
                yield return null;
            }
            groupTransform.localPosition = end;
        }
    }

    IEnumerator AnimateGlowAlpha(float from, float to, float time, System.Action onComplete = null)
    {
        if (_glowImage == null) { onComplete?.Invoke(); yield break; }
        float t = 0f;
        Color col = _glowImage.color;
        while (t < time)
        {
            t += Time.unscaledDeltaTime;
            float p = Mathf.Clamp01(t / time);
            float e = easeCurve.Evaluate(p);
            col.a = Mathf.LerpUnclamped(from, to, e);
            _glowImage.color = col;
            yield return null;
        }
        col.a = to;
        _glowImage.color = col;
        onComplete?.Invoke();
    }

    void OnValidate()
    {
        if (glowObject != null)
        {
            var img = glowObject.GetComponent<Image>();
            if (img != null) img.raycastTarget = false;
        }
    }
}
