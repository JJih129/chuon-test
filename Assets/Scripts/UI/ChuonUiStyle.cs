using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 반복 16 (플레이어 리뷰 E7·E8): UI 공용 글꼴·팔레트.
///
/// 글꼴: 레거시 Text 는 Arial(LegacyRuntime) 이라 한글이 OS 대체 글꼴로 그려졌고(전시 PC 마다 다름),
/// TMP 의 Noto Sans KR SDF 는 가변 글꼴 기본 인스턴스(wght 100 = Thin) 로 래스터되어 작은 크기에서 가늘었다.
/// → Resources/Fonts/ChuonUI-NotoSansKR-Medium.ttf (가변 원본의 wght 500 정적 인스턴스, 줄 높이를 Arial 수준 1.12 em 로 맞춤)
///   하나를 레거시 Text 와 런타임 TMP 글꼴 에셋의 공용 원본으로 쓴다.
/// - <see cref="Legacy"/>: 레거시 Text 용. 없으면 LegacyRuntime.
/// - <see cref="Tmp"/>: 같은 TTF 로 런타임에 만든 동적 SDF (대체 체인 = 기존 Noto SDF → Symbols2/맑은 고딕).
/// - 씬 로드 시 씬에 저장된 Arial/LegacyRuntime Text 와 기본 머티리얼을 쓰는 LiberationSans/Noto(Thin) TMP 를 교체한다.
/// - TMP 전역 대체 목록 맨 앞에 <see cref="Tmp"/> 를 넣어 LiberationSans 기본 글꼴 텍스트의 한글도 Medium 으로 그린다
///   (에디터에서는 플레이 종료 때 되돌려 TMP Settings 에셋에 런타임 객체가 남지 않게 한다).
/// </summary>
public static class ChuonUiFonts
{
    public const string LegacyResourcePath = "Fonts/ChuonUI-NotoSansKR-Medium";

    static Font s_legacy;
    static TMP_FontAsset s_tmp;
    static bool s_tmpTried;
    static Font s_builtin;

    /// <summary>레거시 UnityEngine.UI.Text 용 한글 글꼴 (Noto Sans KR Medium). 리소스가 없으면 LegacyRuntime.</summary>
    public static Font Legacy
    {
        get
        {
            if (s_legacy == null)
            {
                s_legacy = Resources.Load<Font>(LegacyResourcePath);
                if (s_legacy == null)
                    s_legacy = Builtin;
            }
            return s_legacy;
        }
    }

    static Font Builtin
    {
        get
        {
            if (s_builtin == null)
                s_builtin = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            return s_builtin;
        }
    }

    /// <summary>TMP 용 한글 글꼴 (Noto Sans KR Medium 동적 SDF). 만들 수 없으면 기존 Noto SDF → TMP 기본.</summary>
    public static TMP_FontAsset Tmp
    {
        get
        {
            if (s_tmp != null)
                return s_tmp;
            if (!s_tmpTried)
            {
                s_tmpTried = true;
                s_tmp = CreateRuntimeTmp();
            }
            return s_tmp != null ? s_tmp : ExistingNotoTmp();
        }
    }

    static TMP_FontAsset CreateRuntimeTmp()
    {
        Font src = Legacy;
        if (src == null || src == Builtin)
            return null;
        try
        {
            TMP_FontAsset fa = TMP_FontAsset.CreateFontAsset(src, 72, 7, UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, true);
            if (fa == null)
                return null;
            fa.name = "ChuonUI-NotoSansKR-Medium SDF (runtime)";
            fa.hideFlags = HideFlags.DontSave;
            if (fa.material != null)
                fa.material.hideFlags = HideFlags.DontSave;
            TMP_FontAsset noto = ExistingNotoTmp();
            fa.fallbackFontAssetTable = new List<TMP_FontAsset>();
            if (noto != null && noto != fa)
                fa.fallbackFontAssetTable.Add(noto);
            return fa;
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[ChuonUiFonts] runtime TMP font creation failed: " + e.Message);
            return null;
        }
    }

    static TMP_FontAsset s_existingNoto;

    /// <summary>프로젝트의 기존 Noto Sans KR SDF (TMP 전역 대체 목록에 들어 있음).</summary>
    static TMP_FontAsset ExistingNotoTmp()
    {
        if (s_existingNoto != null)
            return s_existingNoto;
        List<TMP_FontAsset> list = TMP_Settings.fallbackFontAssets;
        if (list != null)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] != null && list[i] != s_tmp && list[i].name.Contains("NotoSansKR"))
                    return s_existingNoto = list[i];
            }
        }
        return TMP_Settings.defaultFontAsset;
    }

    /// <summary>씬 기본 글꼴(교체 대상)인가: 없음 / Arial / LegacyRuntime.</summary>
    public static bool IsBuiltinLegacy(Font f)
    {
        return f == null || f == Builtin || f.name == "Arial" || f.name == "LegacyRuntime";
    }

    /// <summary>root 아래 레거시 Text·TMP 의 기본 글꼴을 한글 UI 글꼴로 바꾼다 (사용자 지정 글꼴·머티리얼은 그대로).</summary>
    public static void Apply(GameObject root)
    {
        if (root == null)
            return;
        Font legacy = Legacy;
        if (legacy != null && legacy != Builtin)
        {
            Text[] texts = root.GetComponentsInChildren<Text>(true);
            for (int i = 0; i < texts.Length; i++)
            {
                if (texts[i] != null && IsBuiltinLegacy(texts[i].font))
                    texts[i].font = legacy;
            }
        }

        TMP_FontAsset tmp = Tmp;
        if (tmp == null || tmp == ExistingNotoTmp())
            return;
        TMP_Text[] tmps = root.GetComponentsInChildren<TMP_Text>(true);
        for (int i = 0; i < tmps.Length; i++)
        {
            TMP_Text t = tmps[i];
            if (t == null || t.font == null || t.font == tmp)
                continue;
            string n = t.font.name;
            if (!n.StartsWith("LiberationSans") && !n.Contains("NotoSansKR"))
                continue;
            // 프리셋 머티리얼(외곽선 등)을 쓰는 텍스트는 건드리지 않는다
            if (t.fontSharedMaterial != null && t.font.material != null && t.fontSharedMaterial != t.font.material)
                continue;
            t.font = tmp;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Init()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        RegisterGlobalFallback();
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        GameObject[] roots = scene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
            Apply(roots[i]);
    }

    static bool s_fallbackRegistered;

    static void RegisterGlobalFallback()
    {
        if (s_fallbackRegistered)
            return;
        TMP_FontAsset tmp = Tmp;
        List<TMP_FontAsset> list = TMP_Settings.fallbackFontAssets;
        if (tmp == null || list == null || list.Contains(tmp) || tmp == ExistingNotoTmp())
            return;
        list.Insert(0, tmp);
        s_fallbackRegistered = true;
#if UNITY_EDITOR
        UnityEditor.EditorApplication.playModeStateChanged += OnPlayModeChanged;
#endif
    }

#if UNITY_EDITOR
    static void OnPlayModeChanged(UnityEditor.PlayModeStateChange change)
    {
        if (change != UnityEditor.PlayModeStateChange.ExitingPlayMode)
            return;
        UnityEditor.EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        List<TMP_FontAsset> list = TMP_Settings.fallbackFontAssets;
        if (list != null && s_tmp != null)
            list.Remove(s_tmp);
        s_fallbackRegistered = false;
        if (s_tmp != null)
        {
            Object.DestroyImmediate(s_tmp.material);
            Object.DestroyImmediate(s_tmp);
        }
        s_tmp = null;
        s_tmpTried = false;
        s_existingNoto = null;
    }
#endif
}

/// <summary>
/// 반복 16 (플레이어 리뷰 E7): 화자·HUD 팔레트. 아트 방향 = 그래파이트·세라믹 화이트 + 츠카사 레드, 앰버는 상호작용 전용, 시안은 VR 시스템 오버레이 전용.
/// 화자 구분은 색상(색맹 대비: 밝기 차)과 이름 글자로 한다.
/// - EGO: 스틸 화이트(차가운 저채도 회청, 시안 아님). 그래파이트(0.035) 위 명도 대비 ≈ 11:1.
/// - 추온: 세라믹 화이트. 대비 ≈ 17:1. EGO 와는 밝기·색온도로 구분 (청-황 축이라 적록 색약에서도 유지).
/// - 츠카사/적대(Type-D·시스템): 텍스트용 밝은 츠카사 레드. 대비 ≈ 6.9:1 (적록 색약에서도 EGO·추온보다 확연히 어두움).
/// </summary>
public static class ChuonUiPalette
{
    public static readonly Color Graphite = new Color(0.035f, 0.037f, 0.043f, 1f);
    public static readonly Color CeramicWhite = new Color(0.94f, 0.95f, 0.97f, 1f);
    /// <summary>선·바·강조 면 (텍스트에는 <see cref="TsukasaRedText"/>).</summary>
    public static readonly Color TsukasaRed = new Color(0.90f, 0.12f, 0.12f, 1f);
    /// <summary>그래파이트 위 본문/이름용 레드 (명도 대비 ≥ 4.5:1).</summary>
    public static readonly Color TsukasaRedText = new Color(1f, 0.40f, 0.36f, 1f);
    public static readonly Color EgoSteel = new Color(0.68f, 0.79f, 0.90f, 1f);
    public static readonly Color Amber = new Color(1f, 0.72f, 0.26f, 1f);
    /// <summary>보조 문구 (힌트·태그). 그래파이트 위 대비 ≈ 9:1.</summary>
    public static readonly Color MutedWhite = new Color(0.80f, 0.82f, 0.86f, 1f);

    public static bool IsEgo(string speaker) => speaker == "EGO" || speaker == "E.G.O" || speaker == "에고";
    public static bool IsChuon(string speaker) => speaker == "추온" || speaker == "ChuOn" || speaker == "Chuon";
    public static bool IsHostile(string speaker)
    {
        return !string.IsNullOrEmpty(speaker)
            && (speaker.StartsWith("Type-D") || speaker.StartsWith("TYPE-D") || speaker == "System" || speaker == "시스템"
                || speaker == "츠카사" || speaker == "TSUKASA" || speaker == "Tsukasa");
    }

    /// <summary>화자 이름 색. 모르는 화자는 fallback (기본 = EGO).</summary>
    public static Color Speaker(string speaker, Color fallback)
    {
        if (string.IsNullOrEmpty(speaker))
            return fallback;
        if (IsEgo(speaker))
            return EgoSteel;
        if (IsChuon(speaker))
            return CeramicWhite;
        if (IsHostile(speaker))
            return TsukasaRedText;
        return fallback;
    }

    public static Color Speaker(string speaker) => Speaker(speaker, EgoSteel);

    public static Color WithAlpha(Color c, float a)
    {
        c.a = a;
        return c;
    }
}

/// <summary>
/// 반복 16 (플레이어 리뷰 E2): 키 글리프 상자 + 설명 (예: [F] [Space] 건너뛰기). 레거시 Text, 1920×1080 기준 크기.
/// 상자 = 세라믹 화이트 판 위 그래파이트 글자 (대비 ≈ 17:1), 설명 = 흰색 90 %.
/// </summary>
public static class ChuonKeyHint
{
    public const int DefaultLabelSize = 24;

    /// <summary>parent 아래에 가로 배치 힌트를 만든다. 반환값은 루트(오른쪽 정렬 기준 pivot (1,0.5)).</summary>
    public static RectTransform Create(Transform parent, string name, string[] keys, string label, int labelSize = DefaultLabelSize)
    {
        var root = new GameObject(name, typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(ContentSizeFitter));
        var rt = (RectTransform)root.transform;
        rt.SetParent(parent, false);
        rt.pivot = new Vector2(1f, 0.5f);
        var layout = root.GetComponent<HorizontalLayoutGroup>();
        layout.spacing = Mathf.Round(labelSize * 0.4f);
        layout.childAlignment = TextAnchor.MiddleRight;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        var fitter = root.GetComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        SetKeys(rt, keys, label, labelSize);
        return rt;
    }

    /// <summary>키/설명 갱신 (장치 변경 시).</summary>
    public static void SetKeys(RectTransform root, string[] keys, string label, int labelSize = DefaultLabelSize)
    {
        if (root == null)
            return;
        for (int i = root.childCount - 1; i >= 0; i--)
        {
            GameObject child = root.GetChild(i).gameObject;
            child.SetActive(false);
            Object.Destroy(child);
        }
        Font font = ChuonUiFonts.Legacy;
        int keySize = Mathf.Max(16, Mathf.RoundToInt(labelSize * 0.86f));
        if (keys != null)
        {
            for (int i = 0; i < keys.Length; i++)
            {
                if (string.IsNullOrEmpty(keys[i]))
                    continue;
                var cap = new GameObject("Key_" + keys[i], typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(HorizontalLayoutGroup));
                cap.transform.SetParent(root, false);
                var img = cap.GetComponent<Image>();
                img.color = ChuonUiPalette.WithAlpha(ChuonUiPalette.CeramicWhite, 0.92f);
                img.raycastTarget = false;
                var capLayout = cap.GetComponent<HorizontalLayoutGroup>();
                int padX = Mathf.RoundToInt(keySize * 0.45f);
                int padY = Mathf.RoundToInt(keySize * 0.12f);
                capLayout.padding = new RectOffset(padX, padX, padY, padY);
                capLayout.childAlignment = TextAnchor.MiddleCenter;
                capLayout.childControlWidth = true;
                capLayout.childControlHeight = true;
                capLayout.childForceExpandWidth = false;
                capLayout.childForceExpandHeight = false;
                Text keyText = NewText(cap.transform, "Glyph", font, keySize, FontStyle.Bold, ChuonUiPalette.Graphite);
                keyText.text = keys[i];
                var le = cap.AddComponent<LayoutElement>();
                le.minWidth = keySize * 1.6f;
                le.minHeight = keySize * 1.45f;
            }
        }

        if (!string.IsNullOrEmpty(label))
        {
            Text l = NewText(root, "Label", font, labelSize, FontStyle.Normal, ChuonUiPalette.WithAlpha(ChuonUiPalette.CeramicWhite, 0.92f));
            l.text = label;
            var shadow = l.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.75f);
            shadow.effectDistance = new Vector2(1.5f, -1.5f);
        }
    }

    /// <summary>힌트 전체 알파 (CanvasGroup 없이 Graphic 들에 곱함).</summary>
    public static void SetAlpha(RectTransform root, float alpha)
    {
        if (root == null)
            return;
        CanvasGroup g = root.GetComponent<CanvasGroup>();
        if (g == null)
            g = root.gameObject.AddComponent<CanvasGroup>();
        g.alpha = alpha;
        g.blocksRaycasts = false;
        g.interactable = false;
    }

    /// <summary>건너뛰기 키 표기: 키보드 = 상호작용 키 + Space, 패드 = 상호작용 버튼.</summary>
    public static string[] SkipKeys()
    {
        string interact = InputGlyphs.Get(ChuonAction.Interact);
        return InputGlyphs.IsGamepad ? new[] { interact } : new[] { interact, "Space" };
    }

    static Text NewText(Transform parent, string name, Font font, int size, FontStyle style, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        go.transform.SetParent(parent, false);
        Text t = go.GetComponent<Text>();
        t.font = font;
        t.fontSize = size;
        t.fontStyle = style;
        t.color = color;
        t.alignment = TextAnchor.MiddleCenter;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        return t;
    }
}
