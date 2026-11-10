#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// PV 촬영 전용(개발 전용). PvRecorder.StartTake 가 켜고 StopTake 가 끈다. 스스로 실행되지 않는다.
/// 촬영 중 일어난 이벤트(연출 시작/끝, 패링 성공, 회피, 슬로모, 궁극기, 보스 페이즈·브레이크·사망, 피격, 씬 로드)를
/// 영상 시각(촬영 시작 이후 프레임 / fps)과 함께 기록해 컷 편집 지점을 찾는 데 쓴다.
/// </summary>
public sealed class PvTakeMarkers : MonoBehaviour
{
    static PvTakeMarkers s_instance;

    string _path;
    float _fps = 60f;
    int _startFrame;
    float _startUnscaled;
    readonly StringBuilder _sb = new StringBuilder();

    int _parryCount;
    bool _slow, _ult, _break, _bossDead, _bot;
    int _lastHp = -1, _bossPhase = -1;
    PlayerUltimateController _ultCtrl;
    BossBreakController _breakCtrl;
    BossHealth _bossHealth;
    BossController _boss;
    PlayerHealth _playerHealth;
    float _nextResolve;

    System.Func<float> _clock;

    public static void Begin(string path, float fps, System.Func<float> clock = null)
    {
        End();
        var go = new GameObject("__PvTakeMarkers");
        go.hideFlags = HideFlags.HideInHierarchy;
        DontDestroyOnLoad(go);
        s_instance = go.AddComponent<PvTakeMarkers>();
        s_instance._path = path;
        s_instance._fps = fps;
        s_instance._clock = clock;
        s_instance._startFrame = Time.frameCount;
        s_instance._startUnscaled = Time.unscaledTime;
        s_instance._parryCount = PlayerGuardController.QaParrySuccessCount;
        s_instance.Mark($"take start scene={SceneManager.GetActiveScene().name} unscaled={Time.unscaledTime:F3}");
    }

    public static void End()
    {
        if (s_instance == null)
            return;
        s_instance.Mark("take end");
        s_instance.Flush();
        Destroy(s_instance.gameObject);
        s_instance = null;
    }

    /// <summary>외부(MCP 스크립트)에서 임의 메모를 남길 때.</summary>
    public static void Note(string text)
    {
        if (s_instance != null)
            s_instance.Mark(text);
    }

    public static float VideoTime => s_instance != null ? s_instance.Now : -1f;

    float Now => _clock != null ? _clock() : (Time.frameCount - _startFrame) / _fps;

    void OnEnable()
    {
        CinematicDirector.Started += OnCineStarted;
        CinematicDirector.Finished += OnCineFinished;
        PlayerDodgeController.AnyDodgeStarted += OnDodge;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    void OnDisable()
    {
        CinematicDirector.Started -= OnCineStarted;
        CinematicDirector.Finished -= OnCineFinished;
        PlayerDodgeController.AnyDodgeStarted -= OnDodge;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (_boss != null) _boss.OnBossPhaseChanged -= OnPhase;
    }

    void OnCineStarted(string id) => Mark("cine start " + id);
    void OnCineFinished(string id, bool skipped) => Mark($"cine end {id}{(skipped ? " (skipped)" : "")}");
    void OnDodge(PlayerDodgeController d) => Mark("dodge");
    void OnPhase(int phase, float hp) => Mark($"boss phase {phase} hp={hp:F2}");

    void OnSceneLoaded(Scene s, LoadSceneMode m)
    {
        Mark("scene loaded " + s.name);
        if (_boss != null) _boss.OnBossPhaseChanged -= OnPhase;
        _boss = null; _ultCtrl = null; _breakCtrl = null; _bossHealth = null; _playerHealth = null;
        _lastHp = -1;
        _nextResolve = 0f;
    }

    void Resolve()
    {
        if (Time.unscaledTime < _nextResolve)
            return;
        _nextResolve = Time.unscaledTime + 1f;
        if (_ultCtrl == null) _ultCtrl = FindObjectOfType<PlayerUltimateController>();
        if (_breakCtrl == null) _breakCtrl = FindObjectOfType<BossBreakController>();
        if (_bossHealth == null) _bossHealth = FindObjectOfType<BossHealth>();
        if (_playerHealth == null) _playerHealth = FindObjectOfType<PlayerHealth>();
        if (_boss == null)
        {
            _boss = FindObjectOfType<BossController>();
            if (_boss != null) _boss.OnBossPhaseChanged += OnPhase;
        }
        if (!_bot && FindObjectOfType<QaCombatBot>() != null) { _bot = true; Mark("bot attached"); }
    }

    void Update()
    {
        Resolve();
        if (PlayerGuardController.QaParrySuccessCount != _parryCount)
        {
            _parryCount = PlayerGuardController.QaParrySuccessCount;
            Mark("PARRY success");
        }
        bool slow = GameTimeScale.Effective < 0.6f && GameTimeScale.Effective > 0f;
        if (slow != _slow) { _slow = slow; Mark(slow ? $"slowmo on {GameTimeScale.Effective:F2}" : "slowmo off"); }
        bool ult = _ultCtrl != null && _ultCtrl.IsCinematic;
        if (ult != _ult) { _ult = ult; Mark(ult ? "ULTIMATE start" : "ULTIMATE end"); }
        bool brk = _breakCtrl != null && _breakCtrl.IsInBreak;
        if (brk != _break) { _break = brk; Mark(brk ? "BREAK start" : "break end"); }
        bool dead = _bossHealth != null && _bossHealth.IsDead;
        if (dead != _bossDead) { _bossDead = dead; if (dead) Mark("BOSS DEAD"); }
        if (_playerHealth != null)
        {
            int hp = _playerHealth.CurrentHP;
            if (_lastHp >= 0 && hp < _lastHp) Mark($"player hit -{_lastHp - hp} hp={hp}");
            _lastHp = hp;
        }
    }

    void Mark(string text)
    {
        float vt = Now;
        _sb.Append($"{vt:F2}\t{Time.unscaledTime - _startUnscaled:F2}\t{text}\n");
        if (_sb.Length > 2048)
            Flush();
    }

    void Flush()
    {
        if (string.IsNullOrEmpty(_path) || _sb.Length == 0)
            return;
        try { File.AppendAllText(_path, _sb.ToString()); } catch (IOException) { }
        _sb.Length = 0;
    }

    void OnDestroy() => Flush();
}
#endif
