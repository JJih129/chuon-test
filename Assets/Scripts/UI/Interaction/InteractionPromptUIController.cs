using TMPro;
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(CanvasGroup))]
public class InteractionPromptUIController : MonoBehaviour
{
    public Camera cam;
    public float yOffset = 0f;
    public TMP_Text text;
    public RectTransform background;
    public float hideDistance = 12f;
    public bool clampToScreen = true;
    public bool billboardYawOnly = false;

    Transform _target;
    CanvasGroup _cg;
    string _lastPrompt;

    void Awake()
    {
        if (!cam) cam = Camera.main;
        _cg = GetComponent<CanvasGroup>();
        Hide();
    }

    public void Show(Transform target, string prompt)
    {
        bool promptChanged = !string.Equals(_lastPrompt, prompt, System.StringComparison.Ordinal);
        _target = target;

        if (text == null)
            text = GetComponentInChildren<TMP_Text>(true);
        if (background == null)
            background = GetComponentInChildren<RectTransform>(true);

        if (text != null && promptChanged)
            text.text = prompt;

        if (!gameObject.activeSelf)
            gameObject.SetActive(true);
        if (_cg.alpha != 1f)
            _cg.alpha = 1f;

        if (promptChanged && background != null)
            LayoutRebuilder.ForceRebuildLayoutImmediate(background);

        _lastPrompt = prompt;
        UpdateTransformImmediate();
    }

    public void Hide()
    {
        if (!gameObject.activeSelf && _target == null) return;

        gameObject.SetActive(false);
        _target = null;
        _lastPrompt = null;
    }

    void UpdateTransformImmediate()
    {
        if (!_target || !cam) return;

        var pos = _target.position + Vector3.up * yOffset;
        transform.position = pos;

        Vector3 camPos = cam.transform.position;
        float hideDistanceSqr = hideDistance * hideDistance;
        if ((camPos - pos).sqrMagnitude > hideDistanceSqr)
        {
            Hide();
            return;
        }

        if (clampToScreen)
        {
            var vp = cam.WorldToViewportPoint(pos);
            bool off = vp.z < 0f || vp.x < 0f || vp.x > 1f || vp.y < 0f || vp.y > 1f;
            if (off)
            {
                Hide();
                return;
            }
        }

        Vector3 toCam = camPos - transform.position;
        if (billboardYawOnly) toCam.y = 0f;
        if (toCam.sqrMagnitude > 1e-6f)
            transform.rotation = Quaternion.LookRotation(-toCam.normalized, Vector3.up);
    }

    void LateUpdate()
    {
        if (!_target) return;
        UpdateTransformImmediate();
    }
}
