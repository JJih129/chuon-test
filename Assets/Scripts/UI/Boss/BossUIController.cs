using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class BossUIController : MonoBehaviour
{
    const float AutoResolveInterval = 0.5f;

    // 상단 보스 HUD의 단일 권한자: 활성 BossUIController가 있으면 IntegratedBossUI는 상단 HUD를 건드리지 않음
    static readonly List<BossUIController> s_activeControllers = new List<BossUIController>(2);

    public static bool IsTopHudOwned(GameObject root, BossHUD hud)
    {
        for (int i = 0; i < s_activeControllers.Count; i++)
        {
            BossUIController controller = s_activeControllers[i];
            if (controller == null)
                continue;

            if ((root != null && controller.topHudRoot == root) || (hud != null && controller.hpHud == hud))
                return true;
        }

        return false;
    }

    [Header("References")]
    [Tooltip("Player transform used for distance checks.")]
    public Transform player;

    [Tooltip("Root object for the top boss HUD.")]
    public GameObject topHudRoot;

    [Tooltip("Top HP HUD component.")]
    public BossHUD hpHud;

    [Tooltip("Break HUD component.")]
    public BossBreakHUD breakHud;

    [Tooltip("Boss health component implementing IHealth.")]
    public MonoBehaviour healthBehaviour;

    [Tooltip("Boss break controller.")]
    public BossBreakController breakController;

    [Header("Presentation")]
    [Tooltip("Optional ultimate controller. Auto-resolved when empty.")]
    public PlayerUltimateController playerUltimateController;

    [Tooltip("Keep the boss HUD visible while the player's ultimate cinematic is active.")]
    public bool keepVisibleDuringUltimate = true;

    [Header("Distance / Timing")]
    [Tooltip("Maximum distance to show the boss HUD.")]
    public float showDistance = 18f;

    [Tooltip("Extra distance before hiding once shown.")]
    public float hideHysteresis = 2f;

    [Tooltip("Distance check interval in seconds. Set 0 to check every frame.")]
    public float pollInterval = 0.12f;

    [Tooltip("Delay before showing after entering range.")]
    public float showDelay = 0.05f;

    [Tooltip("Delay before hiding after leaving range.")]
    public float hideDelay = 0.12f;

    [Tooltip("Keep the boss HUD visible for the whole fight instead of hiding by distance.")]
    public bool alwaysVisibleWhileBossAlive = true;

    [Header("Intro Gate")]
    [Tooltip("보스 등장 연출(인트로 일시정지) 동안 상단 보스 HUD를 숨기고 전투 시작 시 페이드 인")]
    public bool hideDuringBossIntro = true;
    [Tooltip("씬 시작 직후 인트로 상태가 확정되기 전까지 HUD 표시를 미루는 시간")]
    public float introGateStartupGrace = 0.3f;
    [Tooltip("전투 시작 시 보스 HUD 페이드 인 시간 (unscaled)")]
    public float showFadeDuration = 0.45f;

    [Header("Readability Style")]
    [Tooltip("보스 HUD 색/가독성 스타일 적용 (레이아웃은 유지)")]
    public bool applyReadabilityStyle = true;
    [Tooltip("보스 HP 바 아래에 표시할 이름")]
    public string bossDisplayName = "실험체  TYPE-D";
    [Tooltip("보스 HP 채움 색 (플레이어 시안과 구분되는 따뜻한 색)")]
    public Color bossHpFillColor = new Color(1f, 0.50f, 0.34f, 1f);
    [Tooltip("브레이크(체간) 게이지 채움 색")]
    public Color breakFillColor = new Color(1f, 0.74f, 0.24f, 1f);
    [Tooltip("브레이크 게이지 빈 칸 색 (비어 있을 때 어둡게)")]
    public Color breakTrackColor = new Color(1f, 0.74f, 0.24f, 0.13f);
    [Tooltip("큰 시안 프레임 알파 (장면 가독성)")]
    [Range(0f, 1f)] public float frameAlpha = 0.65f;
    public Color bossNameColor = new Color(1f, 0.95f, 0.90f, 0.94f);
    public float bossNameFontSize = 24f;
    [Tooltip("보스 이름 위치 (Boss_HUD 중앙 기준). 프레임 바로 아래")]
    public Vector2 bossNameAnchoredPosition = new Vector2(0f, 332f);

    const string HpWhiteSpritePath = "UI/HUD/chuon_hud_boss_hpgauge_white";
    const string BreakWhiteSpritePath = "UI/HUD/chuon_hud_boss_breakgauge_white";
    const string RuntimeBreakTrackName = "_RuntimeBreakTrack";
    const string RuntimeBossNameName = "_RuntimeBossName";

    CanvasGroup _topHudGroup;
    Coroutine _fadeRoutine;
    bool _styleApplied;
    BossController _gateBoss;

    IHealth boundHealth;
    float showSqr;
    float hideSqr;
    bool isVisible;
    Coroutine pollRoutine;
    Coroutine pendingShowRoutine;
    Coroutine pendingHideRoutine;
    bool _presentationSubscribed;
    float _nextAutoResolveAt;
    WaitForSeconds _pollWait;
    float _pollWaitSeconds = -1f;

    void Awake()
    {
        TryAutoResolveReferences(true);
        NormalizeTopHudRootScale();

        showSqr = showDistance * showDistance;
        float hideDistance = showDistance + Mathf.Max(0f, hideHysteresis);
        hideSqr = hideDistance * hideDistance;

        if (topHudRoot != null)
            topHudRoot.SetActive(false);

        boundHealth = healthBehaviour as IHealth;
        if (breakController == null)
            breakController = GetComponent<BossBreakController>();
        if (hpHud == null && topHudRoot != null)
            hpHud = topHudRoot.GetComponentInChildren<BossHUD>(true);
        if (breakHud == null && topHudRoot != null)
            breakHud = topHudRoot.GetComponentInChildren<BossBreakHUD>(true);

        if (breakHud != null && breakController != null)
        {
            if (breakHud.hudRoot == topHudRoot)
            {
                breakHud.showOnBreakEnter = false;
                breakHud.hideOnBreakExit = false;
            }

            breakHud.BindBreakController(breakController);
        }

        if (boundHealth == null && healthBehaviour != null)
            Debug.LogWarning($"[BossUIController] healthBehaviour does not implement IHealth: {healthBehaviour.GetType().Name}", this);

        ApplyReadabilityStyle();
    }

    void OnEnable()
    {
        TryAutoResolveReferences(true);
        if (!s_activeControllers.Contains(this))
            s_activeControllers.Add(this);
        RefreshPresentationSubscriptions();
        if (pollRoutine != null)
            StopCoroutine(pollRoutine);
        pollRoutine = StartCoroutine(Poll());
    }

    void OnDisable()
    {
        if (pollRoutine != null)
            StopCoroutine(pollRoutine);
        pollRoutine = null;

        s_activeControllers.Remove(this);
        ReleasePresentationSubscriptions();
        CancelPendingShow();
        CancelPendingHide();
        ForceHideImmediate();
    }

    IEnumerator Poll()
    {
        while (true)
        {
            Evaluate();
            float effectivePollInterval = ResolvePollInterval();
            if (effectivePollInterval > 0f)
            {
                if (_pollWait == null || !Mathf.Approximately(_pollWaitSeconds, effectivePollInterval))
                {
                    _pollWaitSeconds = effectivePollInterval;
                    _pollWait = new WaitForSeconds(effectivePollInterval);
                }

                yield return _pollWait;
            }
            else
                yield return null;
        }
    }

    float ResolvePollInterval()
    {
        if (IsUltimateHudOverrideActive())
            return 0f;

        if (alwaysVisibleWhileBossAlive && IsBossAlive())
            return Mathf.Max(0.05f, pollInterval);

        float baseInterval = Mathf.Max(0.05f, pollInterval);
        if (player == null)
            return Mathf.Max(baseInterval, 1f);

        float sqr = (player.position - transform.position).sqrMagnitude;
        if (!isVisible && sqr > hideSqr)
            return Mathf.Max(baseInterval, 0.9f);

        return baseInterval;
    }

    void Evaluate()
    {
        if (keepVisibleDuringUltimate && playerUltimateController == null)
        {
            TryAutoResolveReferences();
            RefreshPresentationSubscriptions();
        }

        if (IsUltimateHudOverrideActive())
        {
            CancelPendingShow();
            CancelPendingHide();
            ShowHUD();
            return;
        }

        if (IsBossIntroGateActive())
        {
            CancelPendingShow();
            CancelPendingHide();
            if (isVisible || (topHudRoot != null && topHudRoot.activeSelf))
                ForceHideImmediate();
            return;
        }

        if (alwaysVisibleWhileBossAlive && IsBossAlive())
        {
            CancelPendingShow();
            CancelPendingHide();
            ShowHUD();
            return;
        }

        if (player == null)
        {
            TryAutoResolveReferences();
            if (player == null)
                return;
        }

        float sqr = (player.position - transform.position).sqrMagnitude;

        if (!isVisible && sqr <= showSqr)
        {
            CancelPendingHide();
            if (showDelay <= 0f)
                ShowHUD();
            else if (pendingShowRoutine == null)
                pendingShowRoutine = StartCoroutine(DelayedShow(showDelay));
            return;
        }

        if (isVisible && sqr > hideSqr)
        {
            CancelPendingShow();
            if (hideDelay <= 0f)
                HideHUD();
            else if (pendingHideRoutine == null)
                pendingHideRoutine = StartCoroutine(DelayedHide(hideDelay));
            return;
        }

        if (sqr > showSqr)
            CancelPendingShow();
        if (sqr <= hideSqr)
            CancelPendingHide();
    }

    IEnumerator DelayedShow(float delay)
    {
        yield return new WaitForSeconds(delay);
        pendingShowRoutine = null;

        if (IsUltimateHudOverrideActive())
        {
            ShowHUD();
            yield break;
        }

        if (player == null)
            yield break;

        if ((player.position - transform.position).sqrMagnitude <= showSqr)
            ShowHUD();
    }

    IEnumerator DelayedHide(float delay)
    {
        yield return new WaitForSeconds(delay);
        pendingHideRoutine = null;

        if (IsUltimateHudOverrideActive())
        {
            ShowHUD();
            yield break;
        }

        if (player == null)
        {
            HideHUD();
            yield break;
        }

        if ((player.position - transform.position).sqrMagnitude > hideSqr)
            HideHUD();
    }

    void ShowHUD()
    {
        NormalizeTopHudRootScale();
        if (hpHud == null || topHudRoot == null)
            return;

        // breakHud.hudRoot가 topHudRoot와 같은 경우가 있어 활성화 전에 숨김 여부를 기록
        bool wasHidden = !topHudRoot.activeSelf;

        if (boundHealth == null)
            boundHealth = healthBehaviour as IHealth;

        if (boundHealth != null)
            hpHud.Bind(boundHealth);
        else if (!hpHud.gameObject.activeSelf)
            hpHud.gameObject.SetActive(true);

        if (breakHud != null && breakHud.hudRoot != null && !breakHud.hudRoot.activeSelf)
            breakHud.hudRoot.SetActive(true);

        if (!topHudRoot.activeSelf)
            topHudRoot.SetActive(true);

        if (wasHidden)
        {
            ApplyReadabilityStyle();
            BeginShowFade();
        }

        isVisible = topHudRoot.activeSelf;
    }

    // 숨김 → 표시 전환 시 unscaled 페이드 인 (인트로 종료/전투 시작 시점)
    void BeginShowFade()
    {
        if (topHudRoot == null || showFadeDuration <= 0f || !isActiveAndEnabled)
        {
            if (_topHudGroup != null)
                _topHudGroup.alpha = 1f;
            return;
        }

        if (_topHudGroup == null)
        {
            _topHudGroup = topHudRoot.GetComponent<CanvasGroup>();
            if (_topHudGroup == null)
                _topHudGroup = topHudRoot.AddComponent<CanvasGroup>();
            _topHudGroup.interactable = false;
            _topHudGroup.blocksRaycasts = false;
        }

        if (_fadeRoutine != null)
            StopCoroutine(_fadeRoutine);
        _fadeRoutine = StartCoroutine(CoFadeIn());
    }

    IEnumerator CoFadeIn()
    {
        float duration = Mathf.Max(0.01f, showFadeDuration);
        float elapsed = 0f;
        _topHudGroup.alpha = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            _topHudGroup.alpha = t * t * (3f - 2f * t);
            yield return null;
        }

        _topHudGroup.alpha = 1f;
        _fadeRoutine = null;
    }

    // 보스 인트로 동안 BossController가 외부 인트로 일시정지 상태를 유지함 (IsExternalIntroPaused).
    bool IsBossIntroGateActive()
    {
        if (!hideDuringBossIntro)
            return false;

        if (Time.timeSinceLevelLoad < introGateStartupGrace)
            return true;

        if (_gateBoss == null)
        {
            _gateBoss = GetComponentInParent<BossController>();
            if (_gateBoss == null)
                _gateBoss = GameplaySceneCache.ResolveBossController();
            if (_gateBoss == null)
                return false;
        }

        return IsBossInIntro(_gateBoss);
    }

    public static bool IsBossInIntro(BossController boss)
    {
        if (boss == null)
            return false;

        return boss.IsExternalIntroPaused;
    }

    void ApplyReadabilityStyle()
    {
        if (_styleApplied || !applyReadabilityStyle || topHudRoot == null)
            return;

        _styleApplied = true;

        Image hpImage = hpHud != null ? (hpHud.hpFillImage != null ? hpHud.hpFillImage : hpHud.GetComponent<Image>()) : null;
        if (hpImage != null)
        {
            Sprite hpWhite = Resources.Load<Sprite>(HpWhiteSpritePath);
            if (hpWhite != null)
                hpImage.sprite = hpWhite;
            hpImage.color = bossHpFillColor;
        }

        Image breakImage = breakHud != null ? breakHud.breakFillImage : null;
        if (breakImage != null)
        {
            Sprite breakWhite = Resources.Load<Sprite>(BreakWhiteSpritePath);
            if (breakWhite != null)
                breakImage.sprite = breakWhite;
            breakImage.color = breakFillColor;
            EnsureBreakTrack(breakImage, breakWhite);
        }

        Transform frame = topHudRoot.transform.Find("BG");
        Image frameImage = frame != null ? frame.GetComponent<Image>() : null;
        if (frameImage != null && !GameplayHudStyler.ApplyFrame(frameImage))
        {
            Color c = frameImage.color;
            c.a = frameAlpha;
            frameImage.color = c;
        }

        EnsureBossNameLabel();
    }

    void EnsureBreakTrack(Image breakImage, Sprite sprite)
    {
        RectTransform source = breakImage.rectTransform;
        Transform parent = source.parent;
        if (parent == null)
            return;

        Transform existing = parent.Find(RuntimeBreakTrackName);
        Image track = existing != null ? existing.GetComponent<Image>() : null;
        if (track == null)
        {
            GameObject go = new GameObject(RuntimeBreakTrackName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.layer = source.gameObject.layer;
            track = go.GetComponent<Image>();
            track.rectTransform.SetParent(parent, false);
        }

        RectTransform rect = track.rectTransform;
        rect.anchorMin = source.anchorMin;
        rect.anchorMax = source.anchorMax;
        rect.pivot = source.pivot;
        rect.sizeDelta = source.sizeDelta;
        rect.anchoredPosition = source.anchoredPosition;
        rect.localScale = source.localScale;
        rect.localRotation = source.localRotation;
        rect.SetSiblingIndex(source.GetSiblingIndex());

        track.sprite = sprite != null ? sprite : breakImage.sprite;
        track.type = Image.Type.Simple;
        track.preserveAspect = breakImage.preserveAspect;
        track.color = breakTrackColor;
        track.raycastTarget = false;
    }

    void EnsureBossNameLabel()
    {
        if (string.IsNullOrWhiteSpace(bossDisplayName) || topHudRoot == null)
            return;

        Transform existing = topHudRoot.transform.Find(RuntimeBossNameName);
        TextMeshProUGUI label = existing != null ? existing.GetComponent<TextMeshProUGUI>() : null;
        if (label == null)
        {
            GameObject go = new GameObject(RuntimeBossNameName, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            go.layer = topHudRoot.layer;
            label = go.GetComponent<TextMeshProUGUI>();
            label.rectTransform.SetParent(topHudRoot.transform, false);
        }

        RectTransform rect = label.rectTransform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        // 반복 16 (E9): 이름이 작고(24, Thin 글꼴) 장면 위에서 묻혔다 → 최소 30 px, Noto Sans KR Medium, 어두운 언더레이
        float nameSize = Mathf.Max(bossNameFontSize, MinBossNameFontSize);
        rect.sizeDelta = new Vector2(640f, nameSize + 14f);
        rect.anchoredPosition = bossNameAnchoredPosition;
        rect.localScale = Vector3.one;

        TMP_FontAsset font = ResolveKoreanFont();
        if (font != null)
            label.font = font;
        label.text = bossDisplayName;
        label.fontSize = nameSize;
        label.fontStyle = FontStyles.Bold;
        label.characterSpacing = 4f;
        ApplyNameUnderlay(label);
        label.alignment = TextAlignmentOptions.Center;
        label.enableWordWrapping = false;
        label.overflowMode = TextOverflowModes.Overflow;
        label.color = bossNameColor;
        label.raycastTarget = false;
    }

    const float MinBossNameFontSize = 30f;
    Material _nameMaterial;

    /// <summary>밝은 배경(조명·홀로그램) 위에서도 읽히게 부드러운 그래파이트 언더레이 (머티리얼 인스턴스 1개).</summary>
    void ApplyNameUnderlay(TextMeshProUGUI label)
    {
        if (label == null || label.font == null || label.font.material == null)
            return;
        if (_nameMaterial == null || _nameMaterial.mainTexture != label.font.material.mainTexture)
        {
            _nameMaterial = new Material(label.font.material) { name = "BossName_Underlay" };
            _nameMaterial.EnableKeyword("UNDERLAY_ON");
            _nameMaterial.SetColor("_UnderlayColor", new Color(0.02f, 0.02f, 0.025f, 0.85f));
            _nameMaterial.SetFloat("_UnderlayOffsetX", 0.4f);
            _nameMaterial.SetFloat("_UnderlayOffsetY", -0.4f);
            _nameMaterial.SetFloat("_UnderlayDilate", 0.35f);
            _nameMaterial.SetFloat("_UnderlaySoftness", 0.45f);
        }
        label.fontSharedMaterial = _nameMaterial;
    }

    void OnDestroy()
    {
        if (_nameMaterial != null)
            Destroy(_nameMaterial);
    }

    static TMP_FontAsset ResolveKoreanFont()
    {
        // 반복 16 (E8): 씬의 Noto SDF(가변 글꼴 Thin) 대신 공용 Noto Sans KR Medium
        TMP_FontAsset shared = ChuonUiFonts.Tmp;
        if (shared != null)
            return shared;
        TMP_Text[] texts = FindObjectsOfType<TMP_Text>(true);
        for (int i = 0; i < texts.Length; i++)
        {
            TMP_FontAsset font = texts[i] != null ? texts[i].font : null;
            if (font != null && font.name.Contains("NotoSansKR"))
                return font;
        }

        return TMP_Settings.defaultFontAsset;
    }

    void HideHUD()
    {
        if (!isVisible)
            return;

        if (hpHud != null)
            hpHud.Unbind();
        if (breakHud != null && breakHud.hudRoot != null && breakHud.hudRoot.activeSelf)
            breakHud.hudRoot.SetActive(false);
        if (topHudRoot != null && topHudRoot.activeSelf)
            topHudRoot.SetActive(false);

        isVisible = false;
    }

    public void ForceHideImmediate()
    {
        if (hpHud != null)
            hpHud.Unbind();
        if (breakHud != null && breakHud.hudRoot != null && breakHud.hudRoot.activeSelf)
            breakHud.hudRoot.SetActive(false);
        if (topHudRoot != null && topHudRoot.activeSelf)
            topHudRoot.SetActive(false);
        isVisible = false;
    }

    bool NeedsAutoResolve()
    {
        return player == null
            || (topHudRoot == null && hpHud != null)
            || playerUltimateController == null;
    }

    void TryAutoResolveReferences(bool force = false)
    {
        if (!force && !NeedsAutoResolve())
            return;

        if (!force && Time.unscaledTime < _nextAutoResolveAt)
            return;

        AutoResolveReferences();
        _nextAutoResolveAt = Time.unscaledTime + AutoResolveInterval;
    }

    void AutoResolveReferences()
    {
        if (player == null)
        {
            PlayerLockOn playerLockOn = GameplaySceneCache.ResolvePlayerLockOn();
            if (playerLockOn != null)
                player = playerLockOn.transform;
            else
            {
                PlayerReferences playerReferences = GameplaySceneCache.ResolvePlayerReferences();
                if (playerReferences != null)
                    player = playerReferences.transform;
            }
        }

        if (topHudRoot == null && hpHud != null)
            topHudRoot = hpHud.transform.root.gameObject;

        if (playerUltimateController == null)
            playerUltimateController = GameplaySceneCache.ResolvePlayerUltimateController();
    }

    void NormalizeTopHudRootScale()
    {
        if (topHudRoot == null)
            return;

        Transform hudTransform = topHudRoot.transform;
        if (hudTransform.localScale.sqrMagnitude < 0.0001f)
            hudTransform.localScale = Vector3.one;
    }

    void CancelPendingShow()
    {
        if (pendingShowRoutine == null)
            return;

        StopCoroutine(pendingShowRoutine);
        pendingShowRoutine = null;
    }

    void CancelPendingHide()
    {
        if (pendingHideRoutine == null)
            return;

        StopCoroutine(pendingHideRoutine);
        pendingHideRoutine = null;
    }

    void RefreshPresentationSubscriptions()
    {
        ReleasePresentationSubscriptions();

        if (!keepVisibleDuringUltimate)
            return;

        if (playerUltimateController == null)
            TryAutoResolveReferences();

        if (playerUltimateController == null)
            return;

        playerUltimateController.OnUltimateStarted += HandleUltimateStarted;
        playerUltimateController.OnUltimateEnded += HandleUltimateEnded;
        _presentationSubscribed = true;
    }

    void ReleasePresentationSubscriptions()
    {
        if (!_presentationSubscribed || playerUltimateController == null)
            return;

        playerUltimateController.OnUltimateStarted -= HandleUltimateStarted;
        playerUltimateController.OnUltimateEnded -= HandleUltimateEnded;
        _presentationSubscribed = false;
    }

    void HandleUltimateStarted()
    {
        if (!keepVisibleDuringUltimate)
            return;

        CancelPendingShow();
        CancelPendingHide();
        ShowHUD();
    }

    void HandleUltimateEnded()
    {
        if (!keepVisibleDuringUltimate)
            return;

        CancelPendingShow();
        CancelPendingHide();
        Evaluate();
    }

    bool IsUltimateHudOverrideActive()
    {
        return keepVisibleDuringUltimate
            && playerUltimateController != null
            && playerUltimateController.IsCinematic;
    }

    bool IsBossAlive()
    {
        if (boundHealth == null)
            boundHealth = healthBehaviour as IHealth;

        return boundHealth == null || boundHealth.CurrentHP > 0;
    }
}
