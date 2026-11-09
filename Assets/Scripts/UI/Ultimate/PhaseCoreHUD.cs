// 파일명: PhaseCoreHUD.cs
// 궁극기 게이지를 '페이즈 코어'로 표시: 코어 둘레 5개 호(20/40/60/80/100%), 회전하는 내부 링,
// 단계마다 회전 속도·밝기 증가, 100% 에서 레드/화이트 플라즈마 펄스, 보스전 1회 사용 후 '소진(SPENT)' 균열 표시.
// 모든 스프라이트는 런타임 절차 생성 (에셋 파일 없음). PlayerHUD 가 기존 링(UltimateGaugeRoot/Fill) 자리에 생성한다.
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class PhaseCoreHUD : MonoBehaviour
{
    public const int StageCount = 5;
    public const string RootObjectName = "_PhaseCoreHUD";

    /// <summary>현재 활성 코어 (QA/디버그용).</summary>
    public static PhaseCoreHUD Active { get; private set; }

    [Header("Motion")]
    [Tooltip("0단계 회전 속도 (deg/s)")]
    [SerializeField] float baseSpinSpeed = 14f;
    [Tooltip("단계당 추가 회전 속도 (deg/s)")]
    [SerializeField] float spinSpeedPerStage = 16f;
    [Tooltip("100% 플라즈마 상태 회전 속도 (deg/s)")]
    [SerializeField] float readySpinSpeed = 240f;
    [SerializeField] float readyPulseFrequency = 2.6f;
    [SerializeField] float displayLerpSpeed = 2.4f;

    // ---- QA 노출 ----
    /// <summary>완전히 켜진 단계 수 (0..5). 소진 상태에서는 0.</summary>
    public int LitStages { get; private set; }
    public float Normalized { get; private set; }
    public bool IsReady { get; private set; }
    public bool IsSpent { get; private set; }
    public PlayerUltimateController Source => _source;

    const float SegmentGapDegrees = 9f;
    const float SegmentSpanDegrees = 360f / StageCount;
    const float FlashDuration = 0.45f;

    PlayerUltimateController _source;
    Text _percentText;

    RectTransform _rect;
    RectTransform _spinOuter;
    RectTransform _spinInner;
    RectTransform _plasmaRect;
    RectTransform _glowRect;
    Image _glow;
    Image _hairline;
    readonly Image[] _segmentTracks = new Image[StageCount];
    readonly Image[] _segmentFills = new Image[StageCount];
    readonly float[] _segmentFlash = new float[StageCount];
    Image _dashOuter;
    Image _dashInner;
    Image _plasma;
    Image _coreDisc;
    Image _crack;
    Text _phaseLabel;
    Text _stateLabel;

    float _targetNormalized;
    float _displayNormalized = -1f;
    float _spinAngle;
    float _plasmaAngle;
    float _readyTime;
    bool _built;
    bool _visualDirty = true;
    int _lastLabelKey = -1;

    static string[] s_phaseLabels;

    // ------------------------------------------------------------------
    // 생성 / 바인딩
    // ------------------------------------------------------------------

    /// <summary>기존 링 이미지 rect 아래에 코어를 만든다 (이미 있으면 재사용).</summary>
    public static PhaseCoreHUD CreateUnder(RectTransform ringRect, Text percentText)
    {
        if (ringRect == null)
            return null;

        Transform existing = ringRect.Find(RootObjectName);
        PhaseCoreHUD core = existing != null ? existing.GetComponent<PhaseCoreHUD>() : null;
        if (core == null)
        {
            GameObject go = new GameObject(RootObjectName, typeof(RectTransform));
            go.layer = ringRect.gameObject.layer;
            go.transform.SetParent(ringRect, false);
            core = go.AddComponent<PhaseCoreHUD>();
        }

        core._percentText = percentText;
        core.FitTo(ringRect);
        core.EnsureBuilt();
        // 퍼센트 텍스트(링의 자식)보다 뒤에 그려지도록 첫 자식으로
        core.transform.SetAsFirstSibling();
        core._visualDirty = true;
        return core;
    }

    public void SetPercentText(Text percentText)
    {
        _percentText = percentText;
        _visualDirty = true;
    }

    public void Bind(PlayerUltimateController source)
    {
        if (_source == source && source != null)
        {
            SyncFromSource();
            return;
        }

        Unbind();
        _source = source;
        if (_source != null)
            _source.OnGaugeChanged += HandleGaugeChanged;

        SyncFromSource();
        _displayNormalized = _targetNormalized;
        _visualDirty = true;
    }

    public void Unbind()
    {
        if (_source != null)
            _source.OnGaugeChanged -= HandleGaugeChanged;
        _source = null;
    }

    void SyncFromSource()
    {
        if (_source == null)
        {
            ApplyGauge(0f, false);
            return;
        }

        float max = _source.gaugeMax;
        ApplyGauge(max > 0f ? Mathf.Clamp01(_source.Gauge / max) : 0f, _source.IsGaugeReady);
    }

    void HandleGaugeChanged(float gauge, float normalized, bool ready)
    {
        ApplyGauge(normalized, ready);
    }

    void ApplyGauge(float normalized, bool ready)
    {
        normalized = Mathf.Clamp01(normalized);
        bool becameReady = ready && !IsReady;
        int previousLit = LitStages;

        _targetNormalized = ready ? 1f : normalized;
        Normalized = normalized;
        IsReady = ready;
        LitStages = IsSpent ? 0 : (ready ? StageCount : Mathf.Clamp(Mathf.FloorToInt(normalized * StageCount + 0.0001f), 0, StageCount));

        // 새로 켜진 단계 플래시
        if (LitStages > previousLit)
        {
            for (int i = previousLit; i < LitStages && i < StageCount; i++)
                _segmentFlash[i] = FlashDuration;
        }

        if (becameReady)
            _readyTime = 0f;

        _visualDirty = true;
    }

    void FitTo(RectTransform ringRect)
    {
        _rect = transform as RectTransform;
        if (_rect == null)
            return;

        // 링 rect 는 부모(UltimateGaugeRoot 1.6 x 1.5) 비균등 스케일 아래에 있다 → 코어는 월드 기준 정원이 되도록 보정
        Vector3 lossy = ringRect.lossyScale;
        float sx = Mathf.Abs(lossy.x) > 0.0001f ? Mathf.Abs(lossy.x) : 1f;
        float sy = Mathf.Abs(lossy.y) > 0.0001f ? Mathf.Abs(lossy.y) : 1f;
        Vector2 size = ringRect.rect.size;
        if (size.x <= 1f || size.y <= 1f)
            size = ringRect.sizeDelta;

        float diameter = Mathf.Max(16f, Mathf.Min(size.x * sx / sy, size.y));

        _rect.anchorMin = new Vector2(0.5f, 0.5f);
        _rect.anchorMax = new Vector2(0.5f, 0.5f);
        _rect.pivot = new Vector2(0.5f, 0.5f);
        _rect.anchoredPosition = Vector2.zero;
        _rect.sizeDelta = new Vector2(diameter, diameter);
        _rect.localRotation = Quaternion.identity;
        _rect.localScale = new Vector3(sy / sx, 1f, 1f);
    }

    // ------------------------------------------------------------------
    // Unity
    // ------------------------------------------------------------------

    void OnEnable()
    {
        Active = this;
        _visualDirty = true;
    }

    void OnDisable()
    {
        if (Active == this)
            Active = null;
    }

    void OnDestroy()
    {
        Unbind();
        if (Active == this)
            Active = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        Active = null;
    }

    void Update()
    {
        if (!_built)
            return;

        float dt = Time.unscaledDeltaTime;

        // 보스전 1회 규칙: 이벤트가 없으므로 폴링
        bool spent = _source != null && _source.IsSpentThisFight;
        if (spent != IsSpent)
        {
            IsSpent = spent;
            if (!spent)
                SyncFromSource();
            else
                LitStages = 0;
            _visualDirty = true;
        }

        if (!Mathf.Approximately(_displayNormalized, _targetNormalized))
        {
            _displayNormalized = _displayNormalized < 0f
                ? _targetNormalized
                : Mathf.MoveTowards(_displayNormalized, _targetNormalized, displayLerpSpeed * dt);
            _visualDirty = true;
        }

        bool flashing = false;
        for (int i = 0; i < StageCount; i++)
        {
            if (_segmentFlash[i] > 0f)
            {
                _segmentFlash[i] = Mathf.Max(0f, _segmentFlash[i] - dt);
                flashing = true;
            }
        }

        bool plasma = IsReady && !IsSpent;
        if (plasma)
            _readyTime += dt;

        // 회전
        float spinSpeed = IsSpent ? 0f : (plasma ? readySpinSpeed : baseSpinSpeed + spinSpeedPerStage * LitStages);
        if (spinSpeed > 0f)
        {
            _spinAngle = Mathf.Repeat(_spinAngle + spinSpeed * dt, 360f);
            _spinOuter.localRotation = Quaternion.Euler(0f, 0f, -_spinAngle);
            _spinInner.localRotation = Quaternion.Euler(0f, 0f, _spinAngle * 0.6f);
        }

        if (plasma)
        {
            _plasmaAngle = Mathf.Repeat(_plasmaAngle - readySpinSpeed * 0.55f * dt, 360f);
            _plasmaRect.localRotation = Quaternion.Euler(0f, 0f, _plasmaAngle);
        }

        if (_visualDirty || flashing || plasma)
            RefreshVisuals(plasma);
    }

    // ------------------------------------------------------------------
    // 표시
    // ------------------------------------------------------------------

    void RefreshVisuals(bool plasma)
    {
        _visualDirty = false;

        GameplayUiTheme theme = GameplayHudStyler.Theme;
        Color white = theme.hudUltimateChargingColor;
        Color red = theme.hudAccentColor;
        float stage01 = LitStages / (float)StageCount;

        float pulse = plasma ? 0.5f + 0.5f * Mathf.Sin(_readyTime * readyPulseFrequency * Mathf.PI * 2f) : 0f;
        float display = Mathf.Clamp01(_displayNormalized < 0f ? _targetNormalized : _displayNormalized);

        // 5개 호
        for (int i = 0; i < StageCount; i++)
        {
            float segmentFill = IsSpent ? 0f : Mathf.Clamp01(display * StageCount - i);
            Image fill = _segmentFills[i];
            fill.fillAmount = segmentFill * (SegmentSpanDegrees - SegmentGapDegrees) / 360f;

            Color litColor;
            if (plasma)
                litColor = Color.Lerp(red, white, pulse * 0.65f);
            else if (segmentFill >= 0.999f)
                litColor = Color.Lerp(white, red, i >= StageCount - 2 ? 0.35f + 0.15f * (i - (StageCount - 2)) : 0f);
            else
                litColor = new Color(white.r, white.g, white.b, 0.55f); // 충전 중인 부분 호

            float flash = _segmentFlash[i] / FlashDuration;
            if (flash > 0f)
                litColor = Color.Lerp(litColor, Color.white, flash);
            fill.color = litColor;

            _segmentTracks[i].color = IsSpent
                ? new Color(0.20f, 0.20f, 0.22f, 0.55f)
                : new Color(white.r, white.g, white.b, 0.14f + (plasma ? 0.10f * pulse : 0f));
        }

        // 회전 링 (단계마다 밝아짐)
        float ringAlpha = IsSpent ? 0.12f : 0.22f + 0.13f * LitStages;
        Color ringColor = plasma ? Color.Lerp(white, red, 0.35f + 0.4f * pulse) : white;
        _dashOuter.color = new Color(ringColor.r, ringColor.g, ringColor.b, Mathf.Clamp01(ringAlpha));
        _dashInner.color = new Color(ringColor.r, ringColor.g, ringColor.b, Mathf.Clamp01(ringAlpha * 0.7f));
        _hairline.color = IsSpent ? new Color(red.r, red.g, red.b, 0.35f) : new Color(white.r, white.g, white.b, 0.20f + 0.10f * stage01);

        // 코어 원판
        Color coreColor;
        if (IsSpent)
            coreColor = new Color(0.09f, 0.09f, 0.10f, 0.92f);
        else if (plasma)
            coreColor = Color.Lerp(red, Color.white, 0.25f + 0.55f * pulse);
        else
            coreColor = new Color(white.r, white.g, white.b, 0.16f + 0.15f * LitStages);
        _coreDisc.color = coreColor;
        float coreScale = plasma ? 1f + 0.12f * pulse : 0.86f + 0.03f * LitStages;
        _coreDisc.rectTransform.localScale = new Vector3(coreScale, coreScale, 1f);

        // 글로우 / 플라즈마
        if (plasma)
        {
            _glow.enabled = true;
            _glow.color = new Color(red.r, red.g, red.b, 0.35f + 0.45f * pulse);
            float glowScale = 1f + 0.16f * pulse;
            _glowRect.localScale = new Vector3(glowScale, glowScale, 1f);
            _plasma.enabled = true;
            _plasma.color = Color.Lerp(new Color(red.r, red.g, red.b, 0.85f), new Color(1f, 1f, 1f, 0.95f), pulse * 0.5f);
        }
        else
        {
            bool dimGlow = !IsSpent && LitStages >= 3;
            _glow.enabled = dimGlow;
            if (dimGlow)
            {
                _glow.color = new Color(red.r, red.g, red.b, 0.08f * (LitStages - 2));
                _glowRect.localScale = Vector3.one;
            }
            _plasma.enabled = false;
        }

        // 소진 균열
        _crack.enabled = IsSpent;
        if (IsSpent)
            _crack.color = new Color(red.r, red.g, red.b, 0.85f);

        RefreshLabels(plasma);
    }

    void RefreshLabels(bool plasma)
    {
        int key = IsSpent ? 100 : (plasma ? 50 : LitStages);
        bool centerOverride = IsSpent || plasma;

        if (_percentText != null)
            _percentText.enabled = !centerOverride;

        if (key == _lastLabelKey)
            return;

        _lastLabelKey = key;
        GameplayUiTheme theme = GameplayHudStyler.Theme;

        if (IsSpent)
        {
            _phaseLabel.text = "SPENT";
            _phaseLabel.color = theme.hudAccentColor;
            _stateLabel.text = "소진";
            _stateLabel.color = new Color(0.78f, 0.78f, 0.80f, 0.95f);
            _stateLabel.enabled = true;
        }
        else if (plasma)
        {
            _phaseLabel.text = "READY";
            _phaseLabel.color = theme.hudTextColor;
            _stateLabel.text = "R";
            _stateLabel.color = theme.hudTextColor;
            _stateLabel.enabled = true;
        }
        else
        {
            _phaseLabel.text = GetPhaseLabel(LitStages);
            Color c = theme.hudTextColor;
            _phaseLabel.color = new Color(c.r, c.g, c.b, 0.55f + 0.08f * LitStages);
            _stateLabel.enabled = false;
        }
    }

    static string GetPhaseLabel(int lit)
    {
        if (s_phaseLabels == null)
        {
            s_phaseLabels = new string[StageCount + 1];
            for (int i = 0; i <= StageCount; i++)
                s_phaseLabels[i] = i + " / " + StageCount;
        }

        return s_phaseLabels[Mathf.Clamp(lit, 0, StageCount)];
    }

    // ------------------------------------------------------------------
    // 구성
    // ------------------------------------------------------------------

    void EnsureBuilt()
    {
        if (_built)
            return;

        _rect = transform as RectTransform;
        float d = _rect != null ? _rect.sizeDelta.x : 80f;

        _glow = CreateImage("Glow", transform, ProceduralSprites.Glow, d * 1.55f);
        _glowRect = _glow.rectTransform;
        _glow.enabled = false;

        _hairline = CreateImage("Hairline", transform, ProceduralSprites.Hairline, d * 1.04f);

        for (int i = 0; i < StageCount; i++)
        {
            float startAngle = i * SegmentSpanDegrees + SegmentGapDegrees * 0.5f;
            float span = (SegmentSpanDegrees - SegmentGapDegrees) / 360f;

            Image track = CreateImage("SegmentTrack" + i, transform, ProceduralSprites.Ring, d);
            ConfigureRadial(track, span, startAngle);
            _segmentTracks[i] = track;

            Image fill = CreateImage("SegmentFill" + i, transform, ProceduralSprites.Ring, d);
            ConfigureRadial(fill, 0f, startAngle);
            _segmentFills[i] = fill;
        }

        _plasma = CreateImage("PlasmaRing", transform, ProceduralSprites.Plasma, d * 0.86f);
        _plasmaRect = _plasma.rectTransform;
        _plasma.enabled = false;

        _dashOuter = CreateImage("CoreSpinOuter", transform, ProceduralSprites.Dash, d * 0.70f);
        _spinOuter = _dashOuter.rectTransform;
        _dashInner = CreateImage("CoreSpinInner", transform, ProceduralSprites.DashSparse, d * 0.50f);
        _spinInner = _dashInner.rectTransform;

        _coreDisc = CreateImage("CoreDisc", transform, ProceduralSprites.Disc, d * 0.30f);

        _crack = CreateImage("Crack", transform, ProceduralSprites.Crack, d * 0.92f);
        _crack.enabled = false;

        Font font = RuntimeBuiltInFontUtility.GetDefaultFont();
        _phaseLabel = CreateLabel("PhaseLabel", font, Mathf.Max(8, Mathf.RoundToInt(d * 0.12f)), new Vector2(0f, d * 0.2f), new Vector2(d, d * 0.16f));
        _stateLabel = CreateLabel("StateLabel", font, Mathf.Max(9, Mathf.RoundToInt(d * 0.17f)), new Vector2(0f, -d * 0.02f), new Vector2(d, d * 0.26f));
        _stateLabel.enabled = false;

        _built = true;
        _visualDirty = true;
    }

    static void ConfigureRadial(Image image, float fillAmount, float startAngleClockwise)
    {
        image.type = Image.Type.Filled;
        image.fillMethod = Image.FillMethod.Radial360;
        image.fillOrigin = (int)Image.Origin360.Top;
        image.fillClockwise = true;
        image.fillAmount = fillAmount;
        image.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -startAngleClockwise);
    }

    static Image CreateImage(string name, Transform parent, Sprite sprite, float size)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.layer = parent.gameObject.layer;
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(size, size);

        Image image = go.GetComponent<Image>();
        image.sprite = sprite;
        image.raycastTarget = false;
        image.preserveAspect = false;
        return image;
    }

    Text CreateLabel(string name, Font font, int fontSize, Vector2 position, Vector2 size)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        go.layer = gameObject.layer;
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.SetParent(transform, false);
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;

        Text text = go.GetComponent<Text>();
        text.font = font;
        text.fontSize = fontSize;
        text.fontStyle = FontStyle.Bold;
        text.alignment = TextAnchor.MiddleCenter;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.supportRichText = false;
        text.raycastTarget = false;
        text.text = string.Empty;
        UnityEngine.UI.Outline outline = go.AddComponent<UnityEngine.UI.Outline>(); // 링 위에서도 읽히도록
        outline.effectColor = new Color(0f, 0f, 0f, 0.85f);
        outline.effectDistance = new Vector2(1f, -1f);
        return text;
    }

    // ------------------------------------------------------------------
    // 절차 생성 스프라이트 (한 번만 생성, 씬 간 공유)
    // ------------------------------------------------------------------

    static class ProceduralSprites
    {
        const int Size = 128;

        static Sprite s_ring;
        static Sprite s_hairline;
        static Sprite s_dash;
        static Sprite s_dashSparse;
        static Sprite s_disc;
        static Sprite s_glow;
        static Sprite s_plasma;
        static Sprite s_crack;

        // 반지름은 텍스처 반폭(64px) 기준 정규화
        public static Sprite Ring => s_ring != null ? s_ring : (s_ring = Build("PhaseCore_Ring", RingAlpha));
        public static Sprite Hairline => s_hairline != null ? s_hairline : (s_hairline = Build("PhaseCore_Hairline", HairlineAlpha));
        public static Sprite Dash => s_dash != null ? s_dash : (s_dash = Build("PhaseCore_Dash", DashAlpha));
        public static Sprite DashSparse => s_dashSparse != null ? s_dashSparse : (s_dashSparse = Build("PhaseCore_DashSparse", DashSparseAlpha));
        public static Sprite Disc => s_disc != null ? s_disc : (s_disc = Build("PhaseCore_Disc", DiscAlpha));
        public static Sprite Glow => s_glow != null ? s_glow : (s_glow = Build("PhaseCore_Glow", GlowAlpha));
        public static Sprite Plasma => s_plasma != null ? s_plasma : (s_plasma = Build("PhaseCore_Plasma", PlasmaAlpha));
        public static Sprite Crack => s_crack != null ? s_crack : (s_crack = BuildCrack());

        delegate float AlphaFunc(float radiusPx, float angle01, float px, float py);

        static Sprite Build(string name, AlphaFunc alpha)
        {
            Texture2D texture = NewTexture(name);
            Color32[] pixels = new Color32[Size * Size];
            float half = Size * 0.5f;
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    float px = x + 0.5f - half;
                    float py = y + 0.5f - half;
                    float r = Mathf.Sqrt(px * px + py * py);
                    // 위쪽(12시)에서 시계 방향 0..1
                    float angle01 = Mathf.Repeat(Mathf.Atan2(px, py) / (Mathf.PI * 2f), 1f);
                    byte a = (byte)Mathf.RoundToInt(Mathf.Clamp01(alpha(r, angle01, px, py)) * 255f);
                    pixels[y * Size + x] = new Color32(255, 255, 255, a);
                }
            }

            return Finish(texture, pixels, name);
        }

        static Texture2D NewTexture(string name)
        {
            Texture2D texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            texture.name = name;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            texture.hideFlags = HideFlags.HideAndDontSave;
            return texture;
        }

        static Sprite Finish(Texture2D texture, Color32[] pixels, string name)
        {
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, Size, Size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            sprite.name = name;
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }

        static float Band(float r, float inner, float outer)
        {
            return Mathf.Clamp01(r - inner + 0.5f) * Mathf.Clamp01(outer - r + 0.5f);
        }

        static float RingAlpha(float r, float a, float x, float y) => Band(r, 51f, 63f);
        static float HairlineAlpha(float r, float a, float x, float y) => Band(r, 61.5f, 63f);

        static float DashPattern(float r, float angle01, int count, float duty)
        {
            float arc = Mathf.Max(1f, r) * Mathf.PI * 2f / count;
            float pos = Mathf.Repeat(angle01 * count, 1f) * arc;
            float on = duty * arc;
            if (pos > on)
                return 0f;
            return Mathf.Clamp01(Mathf.Min(pos, on - pos) + 0.5f);
        }

        static float DashAlpha(float r, float a, float x, float y)
        {
            float ring = Band(r, 52f, 60f) * DashPattern(r, a, 16, 0.62f);
            float hair = Band(r, 45f, 46.5f) * 0.7f;
            return Mathf.Max(ring, hair);
        }

        static float DashSparseAlpha(float r, float a, float x, float y)
        {
            return Band(r, 50f, 60f) * DashPattern(r, a, 3, 0.22f);
        }

        static float DiscAlpha(float r, float a, float x, float y)
        {
            float edge = Mathf.Clamp01(62f - r + 0.5f);
            float core = Mathf.Lerp(1f, 0.55f, Mathf.Clamp01(r / 62f));
            return edge * core;
        }

        static float GlowAlpha(float r, float a, float x, float y)
        {
            float t = Mathf.Clamp01(1f - r / 63f);
            return t * t * t;
        }

        static float PlasmaAlpha(float r, float a, float x, float y)
        {
            float n = r / 63f;
            float band = Mathf.Exp(-Mathf.Pow((n - 0.84f) / 0.09f, 2f));
            float ang = a * Mathf.PI * 2f;
            float noise = Mathf.PerlinNoise(Mathf.Cos(ang) * 2.4f + 7.3f, Mathf.Sin(ang) * 2.4f + 3.1f + n * 1.5f);
            float streak = Mathf.SmoothStep(0.30f, 0.72f, noise);
            return band * streak * Mathf.Clamp01(63.5f - r);
        }

        static Sprite BuildCrack()
        {
            const string name = "PhaseCore_Crack";
            Texture2D texture = NewTexture(name);
            float[] alpha = new float[Size * Size];
            System.Random random = new System.Random(7);
            float half = Size * 0.5f;

            // 중심 근처에서 바깥으로 뻗는 지그재그 균열 5줄 + 짧은 가지
            for (int line = 0; line < 5; line++)
            {
                float baseAngle = line * (Mathf.PI * 2f / 5f) + 0.35f + (float)random.NextDouble() * 0.4f;
                Vector2 point = new Vector2(Mathf.Sin(baseAngle), Mathf.Cos(baseAngle)) * 6f;
                int steps = 6;
                for (int s = 0; s < steps; s++)
                {
                    float jitter = ((float)random.NextDouble() - 0.5f) * 0.9f;
                    float stepAngle = baseAngle + jitter;
                    Vector2 next = point + new Vector2(Mathf.Sin(stepAngle), Mathf.Cos(stepAngle)) * (8f + (float)random.NextDouble() * 4f);
                    if (next.magnitude > 58f)
                        next = next.normalized * 58f;
                    float width = Mathf.Lerp(2.2f, 0.9f, s / (float)steps);
                    StampSegment(alpha, point, next, width, half);

                    if (s == 2)
                    {
                        float branchAngle = stepAngle + (random.NextDouble() > 0.5 ? 0.9f : -0.9f);
                        Vector2 branchEnd = next + new Vector2(Mathf.Sin(branchAngle), Mathf.Cos(branchAngle)) * 12f;
                        StampSegment(alpha, next, branchEnd, 0.9f, half);
                    }

                    point = next;
                    if (point.magnitude >= 58f)
                        break;
                }
            }

            Color32[] pixels = new Color32[Size * Size];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(alpha[i]) * 255f));

            return Finish(texture, pixels, name);
        }

        static void StampSegment(float[] alpha, Vector2 a, Vector2 b, float width, float half)
        {
            int minX = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.x, b.x) + half - width - 2f));
            int maxX = Mathf.Min(Size - 1, Mathf.CeilToInt(Mathf.Max(a.x, b.x) + half + width + 2f));
            int minY = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.y, b.y) + half - width - 2f));
            int maxY = Mathf.Min(Size - 1, Mathf.CeilToInt(Mathf.Max(a.y, b.y) + half + width + 2f));
            Vector2 ab = b - a;
            float lengthSq = Mathf.Max(0.0001f, ab.sqrMagnitude);

            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    Vector2 p = new Vector2(x + 0.5f - half, y + 0.5f - half);
                    float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / lengthSq);
                    float distance = Vector2.Distance(p, a + ab * t);
                    float value = Mathf.Clamp01(width * 0.5f - distance + 0.5f);
                    int index = y * Size + x;
                    if (value > alpha[index])
                        alpha[index] = value;
                }
            }
        }
    }
}
