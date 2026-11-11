using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// MainScene의 씬 배치 퀘스트 패널(Player_HUD/QuastBG)을 채우는 경량 프레젠터.
/// - 프로토타입 런타임 패널(MainSceneObjectivePanelController)은 정책상 비활성이라 QuastBG를 채우는 주체가 없었음
/// - 제목(QuestTitlePos)은 rect 높이(42) < 선호 높이(43.5) + Ellipsis 모드 때문에 글자 전체가 잘려 보이지 않던 문제를 보정
/// - 하단 4개 노드에 진행 단계(패턴 파악 → 반격 → 브레이크 → 마무리)를 표시
/// </summary>
[DisallowMultipleComponent]
public class MainSceneQuestPanelPresenter : MonoBehaviour
{
    const float RefreshInterval = 0.2f;
    const int NodeCount = 4;
    const string RuntimeNodePrefix = "_QuestNode_";

    // chuon_hud_quest.png(943x676) 기준 하단 노드 사각형 중심(정규화, 좌하단 원점)과 크기
    static readonly float[] NodeCentersX = { 0.2768f, 0.4258f, 0.5689f, 0.7110f };
    const float NodeCenterY = 0.1642f;
    const float NodeFillNormalizedWidth = 0.034f;
    const float NodeFillNormalizedHeight = 0.046f;

    [SerializeField, Range(0f, 1f)] float frameAlpha = 0.68f;
    [SerializeField] Color doneNodeColor = new Color(0.94f, 0.95f, 0.97f, 0.60f); // 반복 16: 시안 → 세라믹 화이트
    [SerializeField] Color currentNodeColor = new Color(1f, 0.76f, 0.28f, 1f);
    [SerializeField, Range(0.05f, 0.4f)] float finishPressureThreshold = 0.18f;

    PlayerHUD _hud;
    BossController _boss;
    BossHealth _bossHealth;
    BossBreakController _break;
    PlayerUltimateController _ultimate;

    RectTransform _panel;
    TextMeshProUGUI _title;
    TextMeshProUGUI _description;
    Image[] _nodes;

    bool _punishSeen;
    bool _breakSeen;
    int _stage = -1;
    string _lastTitle;
    string _lastDescription;
    float _nextRefreshAt;
    bool _subscribed;

    public void ConfigureRuntime(PlayerHUD hud, BossController boss, BossBreakController breakController, PlayerUltimateController ultimate)
    {
        ReleaseSubscriptions();
        _hud = hud;
        _boss = boss;
        _bossHealth = boss != null ? boss.bossHealth : null;
        _break = breakController;
        _ultimate = ultimate;

        if (!ResolvePanel())
        {
            enabled = false;
            return;
        }

        if (_boss != null)
        {
            _boss.OnPunishWindowOpened += HandlePunishWindowOpened;
            _subscribed = true;
        }

        enabled = true;
        Refresh(true);
    }

    void OnDestroy()
    {
        ReleaseSubscriptions();
    }

    void ReleaseSubscriptions()
    {
        if (_subscribed && _boss != null)
            _boss.OnPunishWindowOpened -= HandlePunishWindowOpened;
        _subscribed = false;
    }

    void HandlePunishWindowOpened(float duration, float damageMultiplier, string patternName)
    {
        _punishSeen = true;
        Refresh(true);
    }

    bool ResolvePanel()
    {
        if (_hud == null)
            return false;

        Transform panel = FindDeep(_hud.transform.root, "QuastBG");
        if (panel == null)
            return false;

        _panel = panel as RectTransform;
        Transform title = panel.Find("QuestTitlePos");
        Transform description = panel.Find("QuestDescription");
        _title = title != null ? title.GetComponent<TextMeshProUGUI>() : null;
        _description = description != null ? description.GetComponent<TextMeshProUGUI>() : null;

        Image frame = panel.GetComponent<Image>();
        if (GameplayHudStyler.ApplyFrame(frame))
        {
            // 아라사카 톤: 완료 노드 오프화이트, 현재 노드 레드 강조 (테마 한 곳에서)
            doneNodeColor = GameplayHudStyler.Theme.hudQuestNodeDoneColor;
            currentNodeColor = GameplayHudStyler.Theme.hudAccentColor;
        }
        else if (frame != null)
        {
            Color c = frame.color;
            c.a = frameAlpha;
            frame.color = c;
        }

        if (_title != null)
        {
            // Ellipsis + 높이 부족이면 TMP가 모든 글자를 제거함 → Overflow로 바꾸고 높이 확보
            _title.overflowMode = TextOverflowModes.Overflow;
            _title.enableWordWrapping = false;
        }

        if (_description != null)
        {
            _description.overflowMode = TextOverflowModes.Overflow;
            _description.enableWordWrapping = true;
        }

        // 음수 폭으로 보이지 않던 씬 QuestGauge 대신 노드별 런타임 표시 사용
        Transform gauge = panel.Find("QuestGauge");
        if (gauge != null)
            gauge.gameObject.SetActive(false);

        // 시안 스프라이트에 색을 곱하면 탁해지므로 흰 사각형(스프라이트 없음)으로 채움
        EnsureNodes(frame, null);
        return _title != null || _description != null;
    }

    void EnsureNodes(Image frame, Sprite sprite)
    {
        _nodes = new Image[NodeCount];
        Rect drawn = ResolveDrawnRect(frame);
        for (int i = 0; i < NodeCount; i++)
        {
            string nodeName = RuntimeNodePrefix + i;
            Transform existing = _panel.Find(nodeName);
            Image node = existing != null ? existing.GetComponent<Image>() : null;
            if (node == null)
            {
                GameObject go = new GameObject(nodeName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                go.layer = _panel.gameObject.layer;
                node = go.GetComponent<Image>();
                node.rectTransform.SetParent(_panel, false);
            }

            RectTransform rect = node.rectTransform;
            rect.anchorMin = rect.anchorMax = Vector2.zero;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(drawn.x + NodeCentersX[i] * drawn.width, drawn.y + NodeCenterY * drawn.height);
            rect.sizeDelta = new Vector2(NodeFillNormalizedWidth * drawn.width, NodeFillNormalizedHeight * drawn.height);
            rect.localScale = Vector3.one;
            node.sprite = sprite;
            node.raycastTarget = false;
            node.enabled = false;
            _nodes[i] = node;
        }
    }

    Rect ResolveDrawnRect(Image frame)
    {
        Rect r = _panel.rect;
        float w = r.width;
        float h = r.height;
        if (frame == null || !frame.preserveAspect || frame.sprite == null)
            return new Rect(0f, 0f, w, h);

        Vector2 spriteSize = frame.sprite.rect.size;
        if (spriteSize.x <= 0f || spriteSize.y <= 0f)
            return new Rect(0f, 0f, w, h);

        float scale = Mathf.Min(w / spriteSize.x, h / spriteSize.y);
        float dw = spriteSize.x * scale;
        float dh = spriteSize.y * scale;
        return new Rect((w - dw) * 0.5f, (h - dh) * 0.5f, dw, dh);
    }

    void Update()
    {
        float now = Time.unscaledTime;
        if (now >= _nextRefreshAt)
        {
            _nextRefreshAt = now + RefreshInterval;
            Refresh(false);
        }

        PulseCurrentNode(now);
    }

    bool _deferredForUpperFloor;

    void Refresh(bool force)
    {
        // Level_Facility: 상층(로비)에서는 LobbyManager가 퀘스트 패널을 사용하므로 하강 후에 넘겨받는다
        LobbyManager lobby = LobbyManager.Instance;
        if (lobby != null && lobby.ContinuesInSameScene && !lobby.HasHandedOffToLowerFloor)
        {
            _deferredForUpperFloor = true;
            return;
        }

        if (_deferredForUpperFloor)
        {
            _deferredForUpperFloor = false;
            force = true;
        }

        if (_break != null && _break.IsInBreak)
            _breakSeen = true;

        bool intro = BossUIController.IsBossInIntro(_boss);
        bool dead = _bossHealth != null && _bossHealth.CurrentHP <= 0;
        bool breakActive = _break != null && _break.IsInBreak;
        bool ultimateReady = _ultimate != null && _ultimate.IsGaugeReady && !_ultimate.IsCinematic;
        bool finishPressure = _bossHealth != null && _bossHealth.MaxHP > 0
            && (float)_bossHealth.CurrentHP / _bossHealth.MaxHP <= finishPressureThreshold;

        int stage = 0;
        if (_punishSeen) stage = 1;
        if (_breakSeen) stage = 2;
        if (finishPressure || ultimateReady) stage = 3;
        if (dead) stage = NodeCount;

        string title;
        string description;
        if (intro)
        {
            title = "격납고 진입";
            description = "엘리베이터에서 내려 전방의 고에너지 반응을 확인하세요.";
            stage = -1;
        }
        else if (dead)
        {
            title = "임무 완료";
            description = "실험체 Type-D를 제압했습니다.";
        }
        else
        {
            title = "실험체 Type-D 제압";
            if (breakActive)
                description = "브레이크! 무너진 지금 집중 공격하세요.";
            else if (ultimateReady)
                description = "궁극기 준비 완료. 결정타로 마무리하세요.";
            else if (finishPressure)
                description = "한계에 몰렸습니다. 끝까지 몰아붙이세요.";
            else if (stage == 2)
                description = "패링과 반격으로 브레이크 게이지를 다시 깎아내세요.";
            else if (stage == 1)
                description = "공격 직후 빈틈에 반격하고, 패링으로 브레이크 게이지를 깎으세요.";
            else
                description = "공격 패턴을 읽고 회피·패링으로 대응하세요.";
        }

        if (force || title != _lastTitle)
        {
            _lastTitle = title;
            if (_title != null)
            {
                _title.text = title;
                FitTitleHeight();
            }
        }

        if (force || description != _lastDescription)
        {
            _lastDescription = description;
            if (_description != null)
                _description.text = description;
        }

        if (force || stage != _stage)
        {
            _stage = stage;
            ApplyNodes();
        }
    }

    void FitTitleHeight()
    {
        RectTransform rect = _title.rectTransform;
        float preferred = _title.GetPreferredValues(_title.text, rect.rect.width, 0f).y;
        if (preferred > rect.rect.height)
            rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Mathf.Ceil(preferred + 2f));
    }

    void ApplyNodes()
    {
        if (_nodes == null)
            return;

        for (int i = 0; i < _nodes.Length; i++)
        {
            Image node = _nodes[i];
            if (node == null)
                continue;

            bool done = i < _stage;
            bool current = i == _stage;
            node.enabled = done || current;
            node.color = current ? currentNodeColor : doneNodeColor;
        }
    }

    void PulseCurrentNode(float now)
    {
        if (_nodes == null || _stage < 0 || _stage >= _nodes.Length)
            return;

        Image node = _nodes[_stage];
        if (node == null)
            return;

        Color c = currentNodeColor;
        c.a *= 0.65f + 0.35f * (0.5f + 0.5f * Mathf.Sin(now * 4.2f));
        node.color = c;
    }

    static Transform FindDeep(Transform root, string name)
    {
        if (root == null)
            return null;
        if (root.name == name)
            return root;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = FindDeep(root.GetChild(i), name);
            if (found != null)
                return found;
        }

        return null;
    }
}
