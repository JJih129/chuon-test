// 파일명: KioskOverlays.cs
// 키오스크 화면 요소: 방치 카운트다운("계속하시겠습니까? 10"), 세션 경고 토스트, 데모 종료 카드(QR 슬롯).
// KioskDirector 가 생성·구동한다. 모든 갱신은 비스케일 시간. 입력을 막지 않는다(레이캐스트 없음).
using System.IO;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class KioskOverlays : MonoBehaviour
{
    const int SortingOrder = 9500;   // 페이더(500)·팁(520)·일시정지 메뉴 위
    const float ToastSeconds = 6f;
    const float FadeSpeed = 6f;
    /// <summary>QR 이미지 (있으면 데모 종료 카드에 표시): StreamingAssets/Kiosk/qr.png</summary>
    public const string QrRelativePath = "Kiosk/qr.png";

    Canvas _canvas;
    CanvasGroup _group;

    // 카운트다운
    RectTransform _countdownRoot;
    Text _countdownNumber;
    Image _countdownFill;
    int _shownNumber = -1;

    // 토스트
    RectTransform _toastRoot;
    CanvasGroup _toastGroup;
    Text _toastTag;
    Text _toastBody;
    float _toastUntil;

    // 엔드 카드
    RectTransform _endRoot;
    Text _endSubtitle;
    Text _endFooter;
    RawImage _qrImage;
    Text _qrPlaceholder;
    Texture2D _qrTexture;
    bool _qrTried;
    int _endShownSeconds = -1;

    public bool CountdownVisible => _countdownRoot != null && _countdownRoot.gameObject.activeSelf;
    public bool EndCardVisible => _endRoot != null && _endRoot.gameObject.activeSelf;

    public static KioskOverlays Create(Transform parent)
    {
        GameObject go = new GameObject("KioskOverlays");
        go.transform.SetParent(parent, false);
        KioskOverlays overlays = go.AddComponent<KioskOverlays>();
        overlays.Build();
        return overlays;
    }

    void OnDestroy()
    {
        if (_qrTexture != null)
            Destroy(_qrTexture);
    }

    // ------------------------------------------------------------------
    // 카운트다운
    // ------------------------------------------------------------------

    public void ShowCountdown(int number, float fraction)
    {
        if (!_countdownRoot.gameObject.activeSelf)
        {
            _countdownRoot.gameObject.SetActive(true);
            _shownNumber = -1;
        }

        if (number != _shownNumber)
        {
            _shownNumber = number;
            _countdownNumber.text = number.ToString();
        }

        RectTransform fill = _countdownFill.rectTransform;
        fill.anchorMax = new Vector2(Mathf.Clamp01(fraction), 1f);
        // 마지막 3초는 숫자 박동
        float pulse = number <= 3 ? 1f + 0.06f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * Mathf.PI * 2f)) : 1f;
        _countdownNumber.rectTransform.localScale = new Vector3(pulse, pulse, 1f);
    }

    public void HideCountdown()
    {
        if (_countdownRoot != null && _countdownRoot.gameObject.activeSelf)
            _countdownRoot.gameObject.SetActive(false);
    }

    // ------------------------------------------------------------------
    // 토스트
    // ------------------------------------------------------------------

    public void ShowToast(string tag, string body)
    {
        _toastTag.text = "// " + tag;
        _toastBody.text = body;
        _toastRoot.gameObject.SetActive(true);
        _toastUntil = Time.unscaledTime + ToastSeconds;
    }

    public void HideToast()
    {
        _toastUntil = 0f;
        if (_toastRoot != null)
        {
            _toastGroup.alpha = 0f;
            _toastRoot.gameObject.SetActive(false);
        }
    }

    // ------------------------------------------------------------------
    // 엔드 카드
    // ------------------------------------------------------------------

    public void ShowEndCard(bool completed)
    {
        EnsureQr();
        _endSubtitle.text = completed
            ? "끝까지 플레이해주셔서 감사합니다"
            : "플레이해주셔서 감사합니다";
        _endShownSeconds = -1;
        _endRoot.gameObject.SetActive(true);
    }

    public void UpdateEndCard(float remainingSeconds, bool canSkip)
    {
        int seconds = Mathf.Max(0, Mathf.CeilToInt(remainingSeconds));
        int key = seconds * 2 + (canSkip ? 1 : 0);
        if (key == _endShownSeconds)
            return;
        _endShownSeconds = key;
        _endFooter.text = canSkip
            ? "아무 버튼이나 누르면 타이틀로 돌아갑니다  ·  " + seconds
            : "잠시 후 타이틀로 돌아갑니다  ·  " + seconds;
    }

    public void HideEndCard()
    {
        if (_endRoot != null && _endRoot.gameObject.activeSelf)
            _endRoot.gameObject.SetActive(false);
    }

    public void HideAll()
    {
        HideCountdown();
        HideToast();
        HideEndCard();
    }

    void Update()
    {
        bool toastActive = _toastRoot.gameObject.activeSelf;
        if (toastActive)
        {
            bool showing = Time.unscaledTime < _toastUntil;
            float target = showing ? 1f : 0f;
            _toastGroup.alpha = Mathf.MoveTowards(_toastGroup.alpha, target, Time.unscaledDeltaTime * FadeSpeed * 0.5f);
            if (!showing && _toastGroup.alpha <= 0.001f)
                _toastRoot.gameObject.SetActive(false);
        }

        bool anyVisible = _countdownRoot.gameObject.activeSelf || _endRoot.gameObject.activeSelf || _toastRoot.gameObject.activeSelf;
        if (anyVisible && !_canvas.enabled)
            _canvas.enabled = true;
        float groupTarget = anyVisible ? 1f : 0f;
        _group.alpha = Mathf.MoveTowards(_group.alpha, groupTarget, Time.unscaledDeltaTime * FadeSpeed);
        if (!anyVisible && _group.alpha <= 0.001f && _canvas.enabled)
            _canvas.enabled = false;
    }

    void EnsureQr()
    {
        if (_qrTried)
            return;
        _qrTried = true;

        try
        {
            string path = Path.Combine(Application.streamingAssetsPath, QrRelativePath);
            if (!File.Exists(path))
                return;
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (tex.LoadImage(File.ReadAllBytes(path)))
            {
                tex.filterMode = FilterMode.Point; // QR 은 선명하게
                _qrTexture = tex;
                _qrImage.texture = tex;
                _qrImage.gameObject.SetActive(true);
                _qrPlaceholder.gameObject.SetActive(false);
            }
            else
            {
                Destroy(tex);
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[Kiosk] QR image load failed: " + e.Message);
        }
    }

    // ------------------------------------------------------------------
    // 구성 (1920×1080 기준)
    // ------------------------------------------------------------------

    void Build()
    {
        _canvas = KioskUi.CreateCanvas("KioskOverlayCanvas", transform, SortingOrder, out _group);
        Transform root = _canvas.transform;

        BuildCountdown(root);
        BuildToast(root);
        BuildEndCard(root);
    }

    void BuildCountdown(Transform root)
    {
        _countdownRoot = KioskUi.CreateRect("Countdown", root, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        KioskUi.CreateFullscreen("Dim", _countdownRoot, KioskUi.Dim);

        RectTransform card = KioskUi.CreateCard("Card", _countdownRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(820f, 340f));
        KioskUi.DecorateCard(card, "TSUKASA CORP.  //  SESSION MONITOR");

        KioskUi.CreateText("Question", card, "계속하시겠습니까?", 46, FontStyle.Bold, TextAnchor.MiddleLeft, KioskUi.White,
            new Vector2(0f, 1f), new Vector2(0.72f, 1f), new Vector2(28f, -150f), new Vector2(0f, -60f));
        KioskUi.CreateText("Hint", card, "아무 버튼이나 누르면 계속합니다.\n입력이 없으면 타이틀 화면으로 돌아갑니다.", 20, FontStyle.Normal, TextAnchor.UpperLeft, KioskUi.Muted,
            new Vector2(0f, 0f), new Vector2(0.72f, 1f), new Vector2(28f, 60f), new Vector2(0f, -164f));

        _countdownNumber = KioskUi.CreateText("Number", card, "10", 150, FontStyle.Bold, TextAnchor.MiddleCenter, KioskUi.Red,
            new Vector2(0.70f, 0f), new Vector2(1f, 1f), new Vector2(0f, 40f), new Vector2(-24f, -52f));

        RectTransform bar = KioskUi.CreateRect("Bar", card, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(28f, 24f), new Vector2(-28f, 30f));
        KioskUi.CreateImage("Track", bar, new Color(KioskUi.Line.r, KioskUi.Line.g, KioskUi.Line.b, 0.18f), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        _countdownFill = KioskUi.CreateImage("Fill", bar, KioskUi.Red, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        _countdownRoot.gameObject.SetActive(false);
    }

    void BuildToast(Transform root)
    {
        _toastRoot = KioskUi.CreateCard("Toast", root, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-64f, -64f), new Vector2(560f, 118f));
        _toastGroup = _toastRoot.gameObject.AddComponent<CanvasGroup>();
        _toastGroup.alpha = 0f;
        _toastGroup.blocksRaycasts = false;
        _toastGroup.interactable = false;
        KioskUi.DecorateCard(_toastRoot, "TSUKASA CORP.  //  DEMO SESSION", new Color(0.035f, 0.04f, 0.045f, 0.86f));
        _toastTag = KioskUi.CreateText("Tag", _toastRoot, string.Empty, 16, FontStyle.Bold, TextAnchor.UpperLeft, KioskUi.Red,
            new Vector2(0f, 1f), new Vector2(0.4f, 1f), new Vector2(28f, -84f), new Vector2(0f, -56f));
        _toastBody = KioskUi.CreateText("Body", _toastRoot, string.Empty, 20, FontStyle.Normal, TextAnchor.UpperLeft, KioskUi.White,
            new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(28f, 10f), new Vector2(-24f, -84f));
        _toastBody.text = string.Empty;
        // 태그와 본문을 한 줄로: 본문은 태그 오른쪽
        RectTransform bodyRect = _toastBody.rectTransform;
        bodyRect.anchorMin = new Vector2(0f, 0f);
        bodyRect.anchorMax = new Vector2(1f, 1f);
        bodyRect.offsetMin = new Vector2(150f, 12f);
        bodyRect.offsetMax = new Vector2(-24f, -54f);
        _toastRoot.gameObject.SetActive(false);
    }

    void BuildEndCard(Transform root)
    {
        _endRoot = KioskUi.CreateRect("EndCard", root, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        KioskUi.CreateFullscreen("Black", _endRoot, new Color(0.01f, 0.012f, 0.015f, 0.96f));

        RectTransform card = KioskUi.CreateCard("Card", _endRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1240f, 520f));
        KioskUi.DecorateCard(card, "TSUKASA CORP.  //  END OF TRANSMISSION");

        KioskUi.CreateText("Event", card, "PLAYX4 2026 DEMO", 72, FontStyle.Bold, TextAnchor.MiddleLeft, KioskUi.White,
            new Vector2(0f, 1f), new Vector2(0.74f, 1f), new Vector2(40f, -190f), new Vector2(0f, -80f));
        KioskUi.CreateImage("Rule", card, KioskUi.Red, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(40f, -208f), new Vector2(220f, -204f));
        _endSubtitle = KioskUi.CreateText("Thanks", card, "플레이해주셔서 감사합니다", 40, FontStyle.Bold, TextAnchor.MiddleLeft, KioskUi.White,
            new Vector2(0f, 1f), new Vector2(0.74f, 1f), new Vector2(40f, -290f), new Vector2(0f, -220f));
        KioskUi.CreateText("Game", card, "추온 (CHUON)  —  출시 소식과 위시리스트는 QR 코드로 확인하세요.", 20, FontStyle.Normal, TextAnchor.UpperLeft, KioskUi.Muted,
            new Vector2(0f, 0f), new Vector2(0.74f, 1f), new Vector2(40f, 90f), new Vector2(0f, -306f));
        _endFooter = KioskUi.CreateText("Footer", card, string.Empty, 18, FontStyle.Bold, TextAnchor.MiddleLeft, KioskUi.Muted,
            new Vector2(0f, 0f), new Vector2(0.74f, 0f), new Vector2(40f, 26f), new Vector2(0f, 64f));

        // QR 슬롯 (StreamingAssets/Kiosk/qr.png 가 있으면 그 이미지, 없으면 자리 표시)
        RectTransform qr = KioskUi.CreateCard("QrSlot", card, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-48f, -12f), new Vector2(280f, 280f));
        KioskUi.CreateImage("QrFrame", qr, KioskUi.Line, Vector2.zero, Vector2.one, new Vector2(-6f, -6f), new Vector2(6f, 6f));
        KioskUi.CreateImage("QrBack", qr, new Color(0.96f, 0.96f, 0.97f, 1f), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        GameObject qrGo = new GameObject("QrImage", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
        RectTransform qrRect = qrGo.GetComponent<RectTransform>();
        qrRect.SetParent(qr, false);
        qrRect.anchorMin = Vector2.zero;
        qrRect.anchorMax = Vector2.one;
        qrRect.offsetMin = new Vector2(12f, 12f);
        qrRect.offsetMax = new Vector2(-12f, -12f);
        _qrImage = qrGo.GetComponent<RawImage>();
        _qrImage.raycastTarget = false;
        qrGo.SetActive(false);
        _qrPlaceholder = KioskUi.CreateText("QrPlaceholder", qr, "QR\n<size=16>StreamingAssets/Kiosk/qr.png</size>", 64, FontStyle.Bold, TextAnchor.MiddleCenter,
            new Color(0.25f, 0.26f, 0.28f, 1f), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        KioskUi.CreateText("QrCaption", qr, "SCAN", 14, FontStyle.Bold, TextAnchor.UpperCenter, KioskUi.Muted,
            new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, -34f), new Vector2(0f, -12f));

        _endRoot.gameObject.SetActive(false);
    }
}
