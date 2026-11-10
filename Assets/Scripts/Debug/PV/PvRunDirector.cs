#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// PV 촬영 전용(개발 전용) 진행 도우미. 스스로 실행되지 않는다 — MCP/에디터에서 StartRun 을 부를 때만.
/// 현재 씬(Tutorial 또는 Level_Facility)에서 QaCombatBot 으로 데모 흐름을 끝까지(튜토리얼 → 오프닝 → 로비 → 엘리베이터 →
/// Type-D → 엔딩) 진행시키고, 씬이 바뀔 때마다 봇을 다시 붙인다. 녹화 시작/정지는 에디터 쪽 PvRecorder 가 한다.
/// 연출은 건너뛰지 않으며, 본 연출 생략 기록은 시작할 때 초기화한다(한 회차처럼).
/// </summary>
public sealed class PvRunDirector : MonoBehaviour
{
    public static string Status { get; private set; } = "idle";
    public static bool Done { get; private set; }
    static PvRunDirector s_instance;

    float _skill;
    bool _stopAtBossDead;
    bool _hideObjectives, _muteMusic;
    float _musicBefore = -1f;
    Transform _questPanel;
    Vector3 _questScale;
    float _nextPanelScan;

    /// <param name="skill">봇 방어 정확도(0..1).</param>
    /// <param name="stopAtBossDead">true 면 보스 사망 후 엔딩까지 보고 끝, false 면 크레딧 씬까지.</param>
    /// <param name="hideObjectives">PV 촬영용: 오른쪽 위 퀘스트/목표 패널(Player_HUD/QuastBG)을 숨긴다(HP·보스 바는 유지).</param>
    /// <param name="muteMusic">PV 촬영용: 게임 BGM 을 꺼서 효과음만 녹음(편집에서 PV 음악을 깐다). 끝나면 원래 값 복원.</param>
    public static void StartRun(float skill = 0.95f, bool stopAtBossDead = true, bool hideObjectives = false, bool muteMusic = false)
    {
        if (s_instance != null)
            Destroy(s_instance.gameObject);
        var go = new GameObject("__PvRunDirector");
        go.hideFlags = HideFlags.HideInHierarchy;
        DontDestroyOnLoad(go);
        s_instance = go.AddComponent<PvRunDirector>();
        s_instance._skill = skill;
        s_instance._stopAtBossDead = stopAtBossDead;
        s_instance._hideObjectives = hideObjectives;
        s_instance._muteMusic = muteMusic;
        if (muteMusic)
        {
            s_instance._musicBefore = AudioOptionsRuntime.MusicVolume;
            AudioOptionsRuntime.SetMusicVolume(0f);
        }
        Done = false;
        s_instance.StartCoroutine(s_instance.Run());
    }

    public static void Stop()
    {
        if (s_instance != null)
            Destroy(s_instance.gameObject);
        s_instance = null;
        Status = "stopped";
    }

    static string Scene => SceneManager.GetActiveScene().name;

    void LateUpdate()
    {
        if (_muteMusic && AudioOptionsRuntime.MusicVolume > 0f)
            AudioOptionsRuntime.SetMusicVolume(0f);
        if (!_hideObjectives)
            return;
        if (_questPanel == null && Time.unscaledTime >= _nextPanelScan)
        {
            _nextPanelScan = Time.unscaledTime + 0.5f;
            GameObject hud = GameObject.Find("Player_HUD");
            _questPanel = hud != null ? hud.transform.Find("QuastBG") : null;
            if (_questPanel != null) _questScale = _questPanel.localScale;
        }
        if (_questPanel != null)
            _questPanel.localScale = Vector3.zero;
    }

    void OnDestroy()
    {
        if (_musicBefore >= 0f)
            AudioOptionsRuntime.SetMusicVolume(_musicBefore);
        if (_questPanel != null)
            _questPanel.localScale = _questScale;
    }

    void SetStatus(string s)
    {
        Status = s;
        PvTakeMarkers.Note("run: " + s);
        Debug.Log("[PV-RUN] " + s);
    }

    QaCombatBot AttachBot()
    {
        QaCombatBot bot = QaCombatBot.Attach(_skill);
        bot.captureScreenshots = false;
        bot.followTutorialRoute = Scene == "Tutorial";
        bot.followLobbyRoute = Scene == "Level_Facility";
        return bot;
    }

    IEnumerator Run()
    {
        CinematicSettings.ResetRun();
        if (DevTestMode.Current != DevTestMode.Mode.Off)
            DevTestMode.SetMode(DevTestMode.Mode.Off);

        if (Scene == "Tutorial")
        {
            yield return new WaitForSeconds(0.5f);
            AttachBot();
            SetStatus("tutorial bot attached");
            float end = Time.unscaledTime + 900f;
            while (Scene == "Tutorial" && Time.unscaledTime < end)
                yield return null;
            if (Scene == "Tutorial") { SetStatus("tutorial timeout"); Done = true; yield break; }
        }

        if (Scene == "Level_Facility")
        {
            yield return null;
            yield return new WaitForSeconds(1f);
            AttachBot();
            SetStatus("facility bot attached");
            BossHealth boss = null;
            float end = Time.unscaledTime + 900f;
            while (Time.unscaledTime < end)
            {
                if (boss == null) boss = FindObjectOfType<BossHealth>();
                if (boss != null && boss.IsDead) break;
                if (FindObjectOfType<QaCombatBot>() == null && Scene == "Level_Facility") AttachBot();
                yield return new WaitForSeconds(0.5f);
            }
            SetStatus("boss dead=" + (boss != null && boss.IsDead));
            float killAt = Time.realtimeSinceStartup;
            float endingEnd = Time.unscaledTime + 40f;
            while (Time.unscaledTime < endingEnd && !(CinematicDirector.LastFinishedId == "ending_tsukasa" && CinematicDirector.LastFinishedAt >= killAt))
                yield return null;
            SetStatus("ending finished=" + (CinematicDirector.LastFinishedId == "ending_tsukasa"));
            yield return new WaitForSeconds(3f);
            if (!_stopAtBossDead)
            {
                float clearEnd = Time.unscaledTime + 40f;
                while (Scene == "Level_Facility" && Time.unscaledTime < clearEnd)
                {
                    MainSceneClearExitBridge bridge = FindObjectOfType<MainSceneClearExitBridge>();
                    SceneLoadInteractable exit = bridge != null ? bridge.ActiveExitInteractable : null;
                    if (exit != null && exit.TryInteract(GameObject.FindWithTag("Player")))
                        break;
                    yield return new WaitForSeconds(1f);
                }
                yield return new WaitForSeconds(8f);
            }
        }

        SetStatus("done scene=" + Scene);
        Done = true;
    }
}
#endif
