using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class IntroDialoguePresenter : MonoBehaviour
{
    [SerializeField] Vector2 anchor = new Vector2(0.5f, 0.18f);
    [SerializeField] Vector2 size = new Vector2(920f, 112f);
    [SerializeField] Color speakerColor = new Color(0f, 0.85f, 1f, 1f);
    [SerializeField] Color textColor = Color.white;
    [SerializeField, Min(0f)] float typingSpeed = 0.018f;

    readonly StringBuilder _builder = new StringBuilder(96);
    CanvasGroup _group;
    Text _speakerText;
    Text _contentText;

    public IEnumerator CoShow(Canvas canvas, string speaker, string text, float hold, float delayAfter)
    {
        EnsureUi(canvas);
        if (_group == null)
            yield break;

        _group.alpha = 1f;
        if (_speakerText != null)
            _speakerText.text = speaker ?? string.Empty;

        if (_contentText != null)
        {
            _contentText.text = string.Empty;
            _builder.Clear();

            string content = text ?? string.Empty;
            for (int i = 0; i < content.Length; i++)
            {
                _builder.Append(content[i]);
                _contentText.text = _builder.ToString();

                if (typingSpeed > 0f)
                    yield return new WaitForSeconds(typingSpeed);
            }
        }

        yield return new WaitForSeconds(Mathf.Max(0.1f, hold) + Mathf.Max(0f, delayAfter));
    }

    public void Hide()
    {
        if (_group != null)
            _group.alpha = 0f;
    }

    void EnsureUi(Canvas canvas)
    {
        if (_group != null || canvas == null)
            return;

        GameObject rootObject = new GameObject("IntroDialogueRuntime", typeof(RectTransform), typeof(CanvasGroup));
        RectTransform root = rootObject.GetComponent<RectTransform>();
        root.SetParent(canvas.transform, false);
        root.anchorMin = anchor;
        root.anchorMax = anchor;
        root.pivot = new Vector2(0.5f, 0.5f);
        root.sizeDelta = size;
        root.anchoredPosition = Vector2.zero;

        _group = rootObject.GetComponent<CanvasGroup>();
        _group.alpha = 0f;
        _group.interactable = false;
        _group.blocksRaycasts = false;

        _speakerText = CreateText(root, "Speaker", 23, FontStyle.Bold, TextAnchor.MiddleCenter, speakerColor, new Vector2(0f, 0.58f), Vector2.one);
        _contentText = CreateText(root, "Content", 27, FontStyle.Bold, TextAnchor.MiddleCenter, textColor, Vector2.zero, new Vector2(1f, 0.72f));
    }

    static Text CreateText(RectTransform parent, string name, int fontSize, FontStyle style, TextAnchor alignment, Color color, Vector2 anchorMin, Vector2 anchorMax)
    {
        GameObject textObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        RectTransform rect = textObject.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        Text text = textObject.GetComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = fontSize;
        text.fontStyle = style;
        text.alignment = alignment;
        text.color = color;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
        return text;
    }
}
