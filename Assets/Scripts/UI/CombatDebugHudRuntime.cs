using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class CombatDebugHudRuntime : MonoBehaviour
{
    const float HudRefreshInterval = 0.1f;
    const float ReferenceResolveInterval = 0.5f;
    const int CanvasSortOrder = 5100;
    const int MaxCommandRows = 6;

    static readonly Color AccentColor = new Color(1f, 0.86f, 0.42f, 0.98f);
    static readonly Color BodyColor = new Color(0.94f, 0.98f, 1f, 0.98f);
    static readonly Color ShadowColor = new Color(0f, 0f, 0f, 0.55f);

    static CombatDebugHudRuntime _instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Bootstrap()
    {
        if (!Application.isEditor && !Debug.isDebugBuild)
            return;

        EnsureInstance();
    }

    static void EnsureInstance()
    {
        if (_instance != null)
            return;

        _instance = FindObjectOfType<CombatDebugHudRuntime>(true);
        if (_instance != null)
        {
            _instance.Initialize();
            return;
        }

        GameObject go = new GameObject("CombatDebugHudRuntime");
        DontDestroyOnLoad(go);
        _instance = go.AddComponent<CombatDebugHudRuntime>();
        _instance.Initialize();
    }

    [SerializeField] KeyCode toggleKey = KeyCode.F12;
    [SerializeField] bool visibleByDefault = false;

    Canvas _canvas;
    CanvasScaler _canvasScaler;
    RectTransform _root;
    TextMeshProUGUI _titleText;
    TextMeshProUGUI _bodyText;
    TMP_FontAsset _fontAsset;

    float _nextRefreshAt;
    float _nextResolveAt;
    bool _visible;
    bool _initialized;
    Rect _lastSafeArea;
    Vector2Int _lastScreenSize;

    PlayerReferences _playerReferences;
    ICombatStateReader _combatStateReader;
    PlayerCombatController _combatController;
    PlayerGuardController _guardController;
    PlayerDodgeController _dodgeController;
    PlayerInputCommandBuffer _inputCommandBuffer;
    UltimateSkillController _ultimateSkillController;
    UltimateSequencePlayer _ultimateSequencePlayer;
    UltimateTargetBinder _ultimateTargetBinder;

    readonly StringBuilder _builder = new StringBuilder(512);

    void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }

        _instance = this;
        DontDestroyOnLoad(gameObject);
        Initialize();
    }

    void OnEnable()
    {
        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    void OnDisable()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
    }

    void Initialize()
    {
        if (_initialized)
            return;

        _visible = visibleByDefault;
        EnsureHud();
        ResolvePlayerReferences(force: true);
        ApplyVisibility();
        RefreshHud(force: true);
        _initialized = true;
    }

    void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        ResolvePlayerReferences(force: true);
        EnsureHud();
        UpdateSafeAreaLayout(force: true);
        RefreshHud(force: true);
    }

    void Update()
    {
        if (Input.GetKeyDown(toggleKey))
        {
            _visible = !_visible;
            ApplyVisibility();
        }

        if (!_visible)
            return;

        if (Time.unscaledTime >= _nextResolveAt)
            ResolvePlayerReferences(force: false);

        if (Time.unscaledTime < _nextRefreshAt)
            return;

        _nextRefreshAt = Time.unscaledTime + HudRefreshInterval;
        UpdateSafeAreaLayout(force: false);
        RefreshHud(force: false);
    }

    void EnsureHud()
    {
        if (_canvas == null)
        {
            GameObject canvasObject = new GameObject("CombatDebugHudCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            canvasObject.transform.SetParent(transform, false);

            _canvas = canvasObject.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = CanvasSortOrder;
            _canvas.pixelPerfect = false;

            _canvasScaler = canvasObject.GetComponent<CanvasScaler>();
            _canvasScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            _canvasScaler.referenceResolution = new Vector2(1920f, 1080f);
            _canvasScaler.matchWidthOrHeight = 0.5f;
        }

        if (_root == null)
        {
            GameObject rootObject = new GameObject("CombatDebugHudRoot", typeof(RectTransform), typeof(Image));
            _root = rootObject.GetComponent<RectTransform>();
            _root.SetParent(_canvas.transform, false);
            _root.anchorMin = new Vector2(1f, 1f);
            _root.anchorMax = new Vector2(1f, 1f);
            _root.pivot = new Vector2(1f, 1f);
            _root.sizeDelta = new Vector2(420f, 236f);

            Image background = rootObject.GetComponent<Image>();
            background.color = new Color(0.05f, 0.08f, 0.12f, 0.74f);
            background.raycastTarget = false;
        }

        if (_fontAsset == null)
            _fontAsset = ResolveFontAsset();

        if (_titleText == null)
            _titleText = CreateText("CombatDebugHudTitle", new Vector2(-14f, -12f), new Vector2(392f, 24f), 18f, FontStyles.Bold, AccentColor);
        if (_bodyText == null)
            _bodyText = CreateText("CombatDebugHudBody", new Vector2(-14f, -42f), new Vector2(392f, 176f), 16f, FontStyles.Normal, BodyColor);
    }

    TextMeshProUGUI CreateText(string objectName, Vector2 anchoredPosition, Vector2 size, float fontSize, FontStyles fontStyle, Color color)
    {
        GameObject textObject = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI), typeof(Shadow));
        RectTransform rectTransform = textObject.GetComponent<RectTransform>();
        rectTransform.SetParent(_root, false);
        rectTransform.anchorMin = new Vector2(1f, 1f);
        rectTransform.anchorMax = new Vector2(1f, 1f);
        rectTransform.pivot = new Vector2(1f, 1f);
        rectTransform.anchoredPosition = anchoredPosition;
        rectTransform.sizeDelta = size;

        TextMeshProUGUI text = textObject.GetComponent<TextMeshProUGUI>();
        text.raycastTarget = false;
        text.richText = false;
        text.enableWordWrapping = false;
        text.overflowMode = TextOverflowModes.Overflow;
        text.fontSize = fontSize;
        text.fontStyle = fontStyle;
        text.color = color;
        text.alignment = TextAlignmentOptions.TopRight;
        if (_fontAsset != null)
            text.font = _fontAsset;

        Shadow shadow = textObject.GetComponent<Shadow>();
        shadow.effectColor = ShadowColor;
        shadow.effectDistance = new Vector2(1.2f, -1.2f);
        shadow.useGraphicAlpha = true;

        return text;
    }

    TMP_FontAsset ResolveFontAsset()
    {
        if (TMP_Settings.defaultFontAsset != null)
            return TMP_Settings.defaultFontAsset;

        TextMeshProUGUI existing = FindObjectOfType<TextMeshProUGUI>(true);
        return existing != null ? existing.font : null;
    }

    void ResolvePlayerReferences(bool force)
    {
        if (!force && _playerReferences != null && _playerReferences.gameObject.activeInHierarchy)
            return;

        _nextResolveAt = Time.unscaledTime + ReferenceResolveInterval;
        _playerReferences = GameplaySceneCache.ResolvePlayerReferences();
        if (_playerReferences == null)
        {
            ClearCachedSources();
            return;
        }

        _combatStateReader = CombatStateReaderResolver.ResolveOrAttach(_playerReferences);
        _combatController = _playerReferences.GetComponent<PlayerCombatController>();
        _guardController = _playerReferences.GetComponent<PlayerGuardController>();
        _dodgeController = _playerReferences.GetComponent<PlayerDodgeController>();
        _inputCommandBuffer = _playerReferences.GetComponent<PlayerInputCommandBuffer>();
        _ultimateSkillController = _playerReferences.GetComponent<UltimateSkillController>();
        _ultimateSequencePlayer = _playerReferences.GetComponent<UltimateSequencePlayer>();
        _ultimateTargetBinder = _playerReferences.GetComponent<UltimateTargetBinder>();
    }

    void ClearCachedSources()
    {
        _combatStateReader = null;
        _combatController = null;
        _guardController = null;
        _dodgeController = null;
        _inputCommandBuffer = null;
        _ultimateSkillController = null;
        _ultimateSequencePlayer = null;
        _ultimateTargetBinder = null;
    }

    void ApplyVisibility()
    {
        if (_canvas != null)
            _canvas.enabled = _visible;
    }

    void UpdateSafeAreaLayout(bool force)
    {
        if (_root == null)
            return;

        Rect safeArea = Screen.safeArea;
        Vector2Int screenSize = new Vector2Int(Screen.width, Screen.height);
        if (!force && safeArea == _lastSafeArea && screenSize == _lastScreenSize)
            return;

        _lastSafeArea = safeArea;
        _lastScreenSize = screenSize;

        Vector2 anchorMax = new Vector2(
            Mathf.Clamp01(safeArea.xMax / Mathf.Max(1f, Screen.width)),
            Mathf.Clamp01(safeArea.yMax / Mathf.Max(1f, Screen.height)));

        _root.anchorMin = anchorMax;
        _root.anchorMax = anchorMax;
        _root.anchoredPosition = new Vector2(-24f, -24f);
    }

    void RefreshHud(bool force)
    {
        EnsureHud();

        if (_titleText != null)
            _titleText.text = "Combat Debug";

        if (_bodyText == null)
            return;

        _builder.Clear();

        if (_playerReferences == null)
        {
            _builder.Append("player: missing");
            _bodyText.text = _builder.ToString();
            return;
        }

        _builder.Append("air ").Append(FormatBool(_combatStateReader != null && _combatStateReader.IsInAir()));
        _builder.Append("  stagger ").Append(FormatBool(_combatStateReader != null && _combatStateReader.IsStaggered()));
        _builder.Append("  hit ").Append(FormatBool(_combatStateReader != null && _combatStateReader.IsInHitState()));
        _builder.Append('\n');

        _builder.Append("attack ").Append(FormatBool(_combatStateReader != null && _combatStateReader.IsAttacking()));
        _builder.Append("  guard ").Append(FormatBool(_combatStateReader != null && _combatStateReader.IsGuarding()));
        _builder.Append("  gmove ").Append(FormatBool(_combatStateReader != null && _combatStateReader.IsGuardMovementActive()));
        _builder.Append("  dodge ").Append(FormatBool(_combatStateReader != null && _combatStateReader.IsDodging()));
        _builder.Append('\n');

        if (_combatController != null)
        {
            _builder.Append("combo ").Append(_combatController.CurrentComboDepth);
            _builder.Append("  atkInput ").Append(_combatController.CurrentAttackInput?.ToString() ?? "-");
            _builder.Append('\n');

            if (_combatController.TryGetHitDebugState(out float hitLayerWeight, out bool waitingForEntry, out bool hitActive, out float hitNormalizedTime))
            {
                _builder.Append("hitL ").Append(hitLayerWeight.ToString("0.00"));
                _builder.Append("  wait ").Append(FormatBool(waitingForEntry));
                _builder.Append("  active ").Append(FormatBool(hitActive));
                _builder.Append("  t ").Append(hitNormalizedTime.ToString("0.00"));
                _builder.Append('\n');
            }
        }

        if (_guardController != null)
        {
            _builder.Append("parry ").Append(FormatBool(_guardController.IsParryWindowOpen));
            _builder.Append("  start ").Append(_guardController.GetParryStartupRemaining().ToString("0.00"));
            _builder.Append("  open ").Append(_guardController.GetParryWindowRemaining().ToString("0.00"));
            _builder.Append("  recover ").Append(_guardController.ParryRecoveryRemaining.ToString("0.00"));
            _builder.Append('\n');

            _builder.Append("gbreak ").Append(_guardController.GuardBreakRemaining.ToString("0.00"));
            _builder.Append("  counter ").Append(_guardController.ParryCounterRemaining.ToString("0.00"));
            _builder.Append('\n');
        }

        if (_dodgeController != null)
        {
            _builder.Append("dodgeRemain ").Append(_dodgeController.RemainingDodgeTime.ToString("0.00"));
            _builder.Append(" / ").Append(_dodgeController.DodgeDuration.ToString("0.00"));
            _builder.Append("  dbuf ").Append(_dodgeController.BufferedDodgeRemaining.ToString("0.00"));
            _builder.Append("  active ").Append(FormatBool(_dodgeController.HasBufferedDodge));
            _builder.Append('\n');
        }

        if (_combatController != null && _combatController.TryGetAttackDebugWindow(out float attackNormalizedTime, out float cancelStart, out float cancelEnd))
        {
            _builder.Append("atkT ").Append(attackNormalizedTime.ToString("0.00"));
            _builder.Append("  cancel ").Append(cancelStart.ToString("0.00"));
            _builder.Append('-').Append(cancelEnd.ToString("0.00"));
            _builder.Append('\n');
        }

        if (_ultimateSequencePlayer != null || _ultimateSkillController != null)
        {
            _builder.Append("ult ");
            _builder.Append(FormatBool(_ultimateSkillController != null && _ultimateSkillController.IsCutscenePlaying));
            _builder.Append("  phase ").Append(_ultimateSequencePlayer != null ? _ultimateSequencePlayer.CurrentPhase.ToString() : "-");
            _builder.Append("  clip ").Append(_ultimateSequencePlayer != null ? _ultimateSequencePlayer.ActivePhaseClipName : "-");
            _builder.Append("  vis ").Append(_ultimateTargetBinder != null && _ultimateTargetBinder.PlayerPresentationAnimator != null ? _ultimateTargetBinder.PlayerPresentationAnimator.name : "-");
            _builder.Append('\n');
        }

        _builder.Append("recent ");
        AppendRecentCommands();
        _bodyText.text = _builder.ToString();
    }

    void AppendRecentCommands()
    {
        if (_inputCommandBuffer == null || _inputCommandBuffer.Count <= 0)
        {
            _builder.Append("-");
            return;
        }

        int printed = 0;
        float now = Time.realtimeSinceStartup;
        for (int i = 0; i < _inputCommandBuffer.Count && printed < MaxCommandRows; i++)
        {
            if (!_inputCommandBuffer.TryGetRecent(i, out PlayerInputCommandBuffer.CommandEntry entry))
                break;

            if (printed > 0)
                _builder.Append(" | ");

            float age = Mathf.Max(0f, now - entry.Realtime);
            _builder.Append(entry.Type);
            if (entry.Buffered)
                _builder.Append('*');
            _builder.Append(' ');
            _builder.Append(age.ToString("0.00"));
            printed++;
        }
    }

    static string FormatBool(bool value)
    {
        return value ? "Y" : "N";
    }
}
