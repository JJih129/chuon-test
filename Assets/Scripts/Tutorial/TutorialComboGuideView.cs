using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class TutorialComboGuideView : MonoBehaviour
{
    [Header("Layout")]
    [SerializeField] private Vector2 anchoredPosition = new Vector2(32f, -160f);
    [SerializeField] private Vector2 size = new Vector2(680f, 500f);

    [Header("Style")]
    [SerializeField] private Color backgroundColor = new Color(0.04f, 0.07f, 0.11f, 0.92f);
    [SerializeField] private Color accentColor = new Color(0.18f, 0.85f, 1f, 1f);
    [SerializeField] private Color titleColor = new Color(0.82f, 0.95f, 1f, 1f);
    [SerializeField] private Color bodyColor = new Color(0.94f, 0.97f, 1f, 1f);
    [SerializeField] private Color shortcutColor = new Color(1f, 0.84f, 0.42f, 1f);
    [SerializeField] private Color labelColor = new Color(0.55f, 0.86f, 1f, 1f);
    [SerializeField] private Color statusBackgroundColor = new Color(0.10f, 0.16f, 0.24f, 0.96f);
    [SerializeField] private Color statusTextColor = new Color(0.86f, 0.96f, 1f, 1f);
    [SerializeField] private Color completedStatusBackgroundColor = new Color(0.09f, 0.24f, 0.20f, 0.98f);
    [SerializeField] private Color completedStatusTextColor = new Color(0.88f, 1f, 0.94f, 1f);

    RectTransform _rootRect;
    TextMeshProUGUI _legacyText;
    TextMeshProUGUI _titleText;
    TextMeshProUGUI _bodyText;
    TextMeshProUGUI _statusText;
    Image _backgroundImage;
    Image _accentImage;
    Image _statusBackgroundImage;
    TMP_FontAsset _fontAsset;

    public void ConfigureRuntime()
    {
        EnsureBuilt();
        gameObject.SetActive(false);
    }

    public void Show(string title, string body)
    {
        EnsureBuilt();

        if (_titleText != null)
            _titleText.text = string.IsNullOrWhiteSpace(title) ? "\uacf5\uaca9\u0020\ubc30\uce58\ud45c" : title;

        if (_bodyText != null)
            _bodyText.text = ColorizeShortcuts(string.IsNullOrWhiteSpace(body) ? string.Empty : body);

        SetStatus(string.Empty);
        gameObject.SetActive(true);
    }

    public void SetStatus(string status)
    {
        EnsureBuilt();
        if (_statusText == null || _statusBackgroundImage == null)
            return;

        bool visible = !string.IsNullOrWhiteSpace(status);
        bool completed = string.Equals(status, "\uc644\ub8cc", System.StringComparison.Ordinal);
        _statusText.text = visible ? status : string.Empty;
        _statusText.color = completed ? completedStatusTextColor : statusTextColor;
        _statusBackgroundImage.color = completed ? completedStatusBackgroundColor : statusBackgroundColor;
        _statusText.gameObject.SetActive(visible);
        _statusBackgroundImage.gameObject.SetActive(visible);
    }

    public void Hide()
    {
        gameObject.SetActive(false);
    }

    void EnsureBuilt()
    {
        if (_rootRect == null)
            _rootRect = GetComponent<RectTransform>();
        if (_legacyText == null)
            _legacyText = GetComponent<TextMeshProUGUI>();

        if (_legacyText != null)
        {
            _fontAsset = _legacyText.font;
            _legacyText.enabled = false;
            _legacyText.raycastTarget = false;
        }

        ApplyRootLayout();
        EnsureBackground();
        EnsureAccent();
        EnsureTitle();
        EnsureBody();
        EnsureStatus();
    }

    void ApplyRootLayout()
    {
        if (_rootRect == null)
            return;

        _rootRect.anchorMin = new Vector2(0f, 1f);
        _rootRect.anchorMax = new Vector2(0f, 1f);
        _rootRect.pivot = new Vector2(0f, 1f);
        _rootRect.anchoredPosition = anchoredPosition;
        _rootRect.sizeDelta = size;
        _rootRect.localScale = Vector3.one;
    }

    void EnsureBackground()
    {
        if (_backgroundImage == null)
        {
            Transform existing = transform.Find("GuideBackground");
            if (existing != null)
                _backgroundImage = existing.GetComponent<Image>();
        }

        if (_backgroundImage == null)
        {
            GameObject backgroundObject = new GameObject("GuideBackground", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            backgroundObject.transform.SetParent(transform, false);
            backgroundObject.transform.SetAsFirstSibling();
            _backgroundImage = backgroundObject.GetComponent<Image>();
        }

        RectTransform rect = _backgroundImage.rectTransform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        _backgroundImage.color = backgroundColor;
        _backgroundImage.raycastTarget = false;
    }

    void EnsureAccent()
    {
        if (_accentImage == null)
        {
            Transform existing = transform.Find("GuideAccent");
            if (existing != null)
                _accentImage = existing.GetComponent<Image>();
        }

        if (_accentImage == null)
        {
            GameObject accentObject = new GameObject("GuideAccent", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            accentObject.transform.SetParent(transform, false);
            _accentImage = accentObject.GetComponent<Image>();
        }

        RectTransform rect = _accentImage.rectTransform;
        rect.anchorMin = new Vector2(0f, 0f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(8f, 0f);
        _accentImage.color = accentColor;
        _accentImage.raycastTarget = false;
    }

    void EnsureTitle()
    {
        if (_titleText == null)
        {
            Transform existing = transform.Find("GuideTitle");
            if (existing != null)
                _titleText = existing.GetComponent<TextMeshProUGUI>();
        }

        if (_titleText == null)
        {
            GameObject titleObject = new GameObject("GuideTitle", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            titleObject.transform.SetParent(transform, false);
            _titleText = titleObject.GetComponent<TextMeshProUGUI>();
        }

        RectTransform rect = _titleText.rectTransform;
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.offsetMin = new Vector2(36f, -72f);
        rect.offsetMax = new Vector2(-28f, -20f);

        _titleText.font = _fontAsset != null ? _fontAsset : _titleText.font;
        _titleText.fontSize = 36f;
        _titleText.fontStyle = FontStyles.Bold;
        _titleText.color = titleColor;
        _titleText.alignment = TextAlignmentOptions.TopLeft;
        _titleText.enableWordWrapping = false;
        _titleText.raycastTarget = false;
    }

    void EnsureBody()
    {
        if (_bodyText == null)
        {
            Transform existing = transform.Find("GuideBody");
            if (existing != null)
                _bodyText = existing.GetComponent<TextMeshProUGUI>();
        }

        if (_bodyText == null)
        {
            GameObject bodyObject = new GameObject("GuideBody", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            bodyObject.transform.SetParent(transform, false);
            _bodyText = bodyObject.GetComponent<TextMeshProUGUI>();
        }

        RectTransform rect = _bodyText.rectTransform;
        rect.anchorMin = new Vector2(0f, 0f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.offsetMin = new Vector2(32f, 74f);
        rect.offsetMax = new Vector2(-28f, -84f);

        _bodyText.font = _fontAsset != null ? _fontAsset : _bodyText.font;
        _bodyText.fontSize = 28f;
        _bodyText.color = bodyColor;
        _bodyText.alignment = TextAlignmentOptions.TopLeft;
        _bodyText.enableWordWrapping = true;
        _bodyText.richText = true;
        _bodyText.raycastTarget = false;
    }

    void EnsureStatus()
    {
        if (_statusBackgroundImage == null)
        {
            Transform existing = transform.Find("GuideStatusBackground");
            if (existing != null)
                _statusBackgroundImage = existing.GetComponent<Image>();
        }

        if (_statusBackgroundImage == null)
        {
            GameObject statusBackgroundObject = new GameObject("GuideStatusBackground", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            statusBackgroundObject.transform.SetParent(transform, false);
            _statusBackgroundImage = statusBackgroundObject.GetComponent<Image>();
        }

        RectTransform backgroundRect = _statusBackgroundImage.rectTransform;
        backgroundRect.anchorMin = new Vector2(0f, 0f);
        backgroundRect.anchorMax = new Vector2(1f, 0f);
        backgroundRect.pivot = new Vector2(0.5f, 0f);
        backgroundRect.offsetMin = new Vector2(24f, 18f);
        backgroundRect.offsetMax = new Vector2(-24f, 58f);
        _statusBackgroundImage.color = statusBackgroundColor;
        _statusBackgroundImage.raycastTarget = false;

        if (_statusText == null)
        {
            Transform existing = transform.Find("GuideStatus");
            if (existing != null)
                _statusText = existing.GetComponent<TextMeshProUGUI>();
        }

        if (_statusText == null)
        {
            GameObject statusObject = new GameObject("GuideStatus", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            statusObject.transform.SetParent(transform, false);
            _statusText = statusObject.GetComponent<TextMeshProUGUI>();
        }

        RectTransform statusRect = _statusText.rectTransform;
        statusRect.anchorMin = new Vector2(0f, 0f);
        statusRect.anchorMax = new Vector2(1f, 0f);
        statusRect.pivot = new Vector2(0.5f, 0f);
        statusRect.offsetMin = new Vector2(34f, 24f);
        statusRect.offsetMax = new Vector2(-24f, 52f);

        _statusText.font = _fontAsset != null ? _fontAsset : _statusText.font;
        _statusText.fontSize = 24f;
        _statusText.fontStyle = FontStyles.Bold;
        _statusText.color = statusTextColor;
        _statusText.alignment = TextAlignmentOptions.MidlineLeft;
        _statusText.enableWordWrapping = false;
        _statusText.raycastTarget = false;
        _statusText.gameObject.SetActive(false);
        _statusBackgroundImage.gameObject.SetActive(false);
    }

    string ColorizeShortcuts(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        string shortcutHex = ColorUtility.ToHtmlStringRGB(shortcutColor);
        string labelHex = ColorUtility.ToHtmlStringRGB(labelColor);
        string accentHex = ColorUtility.ToHtmlStringRGB(accentColor);
        string[] lines = value.Replace("\r", string.Empty).Split('\n');
        StringBuilder builder = new StringBuilder(value.Length + 64);

        for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            string line = lines[lineIndex];
            if (string.IsNullOrWhiteSpace(line))
            {
                if (lineIndex < lines.Length - 1)
                    builder.Append('\n');
                continue;
            }

            string[] tokens = line.Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
            for (int tokenIndex = 0; tokenIndex < tokens.Length; tokenIndex++)
            {
                if (tokenIndex > 0)
                    builder.Append(' ');

                builder.Append(FormatToken(tokens[tokenIndex], shortcutHex, labelHex, accentHex));
            }

            if (lineIndex < lines.Length - 1)
                builder.Append('\n');
        }

        return builder.ToString();
    }

    static string FormatToken(string token, string shortcutHex, string labelHex, string accentHex)
    {
        switch (token)
        {
            case "\uc57d\uacf5":
                return $"<color=#{labelHex}><b>{token}</b></color>";

            case "\uac15\uacf5":
                return $"<color=#{labelHex}><b>{token}</b></color>";

            case "LMB":
            case "RMB":
                return $"<color=#{shortcutHex}><b>{token}</b></color>";

            case ">":
                return $"<color=#{accentHex}><b>></b></color>";

            default:
                return token;
        }
    }
}
