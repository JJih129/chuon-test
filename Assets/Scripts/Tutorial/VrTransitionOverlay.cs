using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 가상현실 훈련(튜토리얼) 진입/종료용 디지털 부팅·디레졸브 오버레이.
/// 검은 가로 밴드가 화면을 덮었다가 엇갈리며 걷히고, 그리드와 "SIMULATION" 문구가 잠깐 깜빡인다.
/// DontDestroyOnLoad 라 씬 로드 직전에 시작하면 다음 씬 첫 화면에서 걷힌다. 실시간 기준, 입력을 막지 않는다.
/// </summary>
public sealed class VrTransitionOverlay : MonoBehaviour
{
    const int BandCount = 16;
    static Texture2D s_grid;

    readonly Image[] _bands = new Image[BandCount];
    readonly float[] _bandDelay = new float[BandCount];
    RawImage _grid;
    Text _title;
    Text _sub;
    CanvasGroup _group;
    float _hold;

    public static VrTransitionOverlay Play(string title, string subtitle, float hold)
    {
        var go = new GameObject("VrTransitionOverlay", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup));
        DontDestroyOnLoad(go);
        var overlay = go.AddComponent<VrTransitionOverlay>();
        overlay._hold = Mathf.Max(0f, hold);
        overlay.Build(title, subtitle);
        overlay.StartCoroutine(overlay.CoRun());
        return overlay;
    }

    void Build(string title, string subtitle)
    {
        var canvas = GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 900;
        var scaler = GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        _group = GetComponent<CanvasGroup>();
        _group.blocksRaycasts = false;
        _group.interactable = false;

        var rng = new System.Random(7);
        for (int i = 0; i < BandCount; i++)
        {
            var img = new GameObject("Band" + i, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)).GetComponent<Image>();
            img.transform.SetParent(transform, false);
            img.color = new Color(0.01f, 0.01f, 0.015f, 1f);
            img.raycastTarget = false;
            RectTransform rt = img.rectTransform;
            rt.anchorMin = new Vector2(0f, (float)i / BandCount);
            rt.anchorMax = new Vector2(1f, (float)(i + 1) / BandCount);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            rt.pivot = new Vector2(i % 2 == 0 ? 0f : 1f, 0.5f);
            _bands[i] = img;
            _bandDelay[i] = (float)rng.NextDouble() * 0.35f;
        }

        _grid = new GameObject("Grid", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage)).GetComponent<RawImage>();
        _grid.transform.SetParent(transform, false);
        _grid.texture = GetGridTexture();
        _grid.uvRect = new Rect(0f, 0f, 1920f / 64f, 1080f / 64f);
        _grid.color = new Color(1f, 0.25f, 0.22f, 0.35f);
        _grid.raycastTarget = false;
        RectTransform grt = _grid.rectTransform;
        grt.anchorMin = Vector2.zero;
        grt.anchorMax = Vector2.one;
        grt.offsetMin = grt.offsetMax = Vector2.zero;

        Font font = RuntimeBuiltInFontUtility.GetDefaultFont();
        _title = NewText("Title", font, 54, new Color(0.94f, 0.97f, 1f, 1f), new Vector2(0f, 26f));
        _title.fontStyle = FontStyle.Bold;
        _title.text = title;
        _sub = NewText("Sub", font, 22, new Color(1f, 0.2f, 0.18f, 1f), new Vector2(0f, -32f));
        _sub.text = subtitle;
    }

    Text NewText(string name, Font font, int size, Color color, Vector2 position)
    {
        var text = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text)).GetComponent<Text>();
        text.transform.SetParent(transform, false);
        text.font = font;
        text.fontSize = size;
        text.color = color;
        text.alignment = TextAnchor.MiddleCenter;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
        RectTransform rt = text.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(1600f, 80f);
        rt.anchoredPosition = position;
        return text;
    }

    IEnumerator CoRun()
    {
        // 부팅: 문구가 깜빡이며 유지
        float t = 0f;
        while (t < _hold)
        {
            t += Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            bool blink = Random.value < 0.12f;
            _title.enabled = !blink;
            _grid.color = new Color(1f, 0.25f, 0.22f, blink ? 0.6f : 0.35f);
            yield return null;
        }
        _title.enabled = true;

        // 디레졸브: 밴드가 엇갈려 좌우로 걷히고 그리드·문구가 꺼진다
        t = 0f;
        const float duration = 0.75f;
        while (t < duration + 0.35f)
        {
            t += Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            for (int i = 0; i < BandCount; i++)
            {
                float k = Mathf.Clamp01((t - _bandDelay[i]) / duration);
                float e = k * k * (3f - 2f * k);
                _bands[i].rectTransform.localScale = new Vector3(1f - e, 1f, 1f);
            }
            float fade = Mathf.Clamp01(t / (duration * 0.8f));
            _title.color = new Color(_title.color.r, _title.color.g, _title.color.b, 1f - fade);
            _sub.color = new Color(_sub.color.r, _sub.color.g, _sub.color.b, 1f - fade);
            _grid.color = new Color(1f, 0.25f, 0.22f, 0.35f * (1f - fade) * (Random.value < 0.2f ? 0.3f : 1f));
            yield return null;
        }

        Destroy(gameObject);
    }

    static Texture2D GetGridTexture()
    {
        if (s_grid != null)
            return s_grid;
        const int n = 64;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = "VrGrid", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
        var px = new Color32[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                bool line = x == 0 || y == 0;
                bool tick = (x == 32 && y < 3) || (y == 32 && x < 3);
                px[y * n + x] = new Color32(255, 255, 255, (byte)(line ? 200 : tick ? 120 : 0));
            }
        tex.SetPixels32(px);
        tex.Apply(false, true);
        s_grid = tex;
        return tex;
    }
}
