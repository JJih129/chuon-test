// 추온 BGM 디렉터 (반복 11)
// 상태: Title / Tutorial / Lobby / BossPhase1 / BossPhase2 / Victory / Ending / Credits (+ 궁극기·대사 덕킹).
// 씬 이름과 보스 상태를 0.25 초마다 보고 상태를 정하고, 두 덱(A/B) 크로스페이드로 전환한다.
// 보스 2페이즈는 같은 길이의 강도 레이어를 샘플 단위로 동기 재생해 볼륨만 올린다 (박자 어긋남 없음).
// 음원: Assets/Art/ChuonPolish/Audio/Resources/Audio/Music/*_GEN.wav — 라이선스 문제 없는 절차 생성 임시 곡
// (Tools/ArtGen/music_gen.py). 실제 곡이 확정되면 TrackTable 의 리소스 경로만 바꾸면 된다.
using UnityEngine;
using UnityEngine.SceneManagement;

[DefaultExecutionOrder(-40)]
public sealed class MusicDirector : MonoBehaviour
{
    public enum MusicState { None, Title, Tutorial, Lobby, BossPhase1, BossPhase2, Victory, Ending, Credits }

    struct Track
    {
        public string Resource;
        public float Gain;
        public bool Loop;
        public float FadeIn;
    }

    // 상태별 트랙 (리소스 경로는 Resources/ 기준)
    static Track TrackFor(MusicState state)
    {
        switch (state)
        {
            case MusicState.Title: return new Track { Resource = "Audio/Music/Title_Drone_GEN", Gain = 0.8f, Loop = true, FadeIn = 2.5f };
            case MusicState.Tutorial: return new Track { Resource = "Audio/Music/Tutorial_Ambient_GEN", Gain = 0.55f, Loop = true, FadeIn = 3f };
            case MusicState.Lobby: return new Track { Resource = "Audio/Music/Lobby_Pad_GEN", Gain = 0.7f, Loop = true, FadeIn = 2.5f };
            case MusicState.BossPhase1:
            case MusicState.BossPhase2: return new Track { Resource = "Audio/Music/Boss_Loop_GEN", Gain = 0.78f, Loop = true, FadeIn = 1.2f };
            case MusicState.Credits: return new Track { Resource = "Audio/Music/Credits_Pad_GEN", Gain = 0.75f, Loop = true, FadeIn = 2f };
            default: return default;
        }
    }

    const string BossLayerResource = "Audio/Music/Boss_P2Layer_GEN";
    const string VictoryResource = "Audio/Music/Victory_Sting_GEN";
    const float BossLayerGain = 0.8f;
    const float CrossfadeOut = 1.6f;
    const float UltimateDuckDb = -14f;
    const float DialogueDuckDb = -7f;

    static MusicDirector s_instance;

    public static MusicState CurrentState => s_instance != null ? s_instance._state : MusicState.None;
    public static string CurrentTrack => s_instance != null && s_instance._active != null && s_instance._active.clip != null ? s_instance._active.clip.name : "";
    public static float CurrentDuckDb => s_instance != null ? s_instance._duckDb : 0f;
    public static bool IsDucked => s_instance != null && s_instance._duckDb < -0.5f;
    public static float BossLayerLevel => s_instance != null ? s_instance._layerLevel : 0f;

    AudioSource _deckA;
    AudioSource _deckB;
    AudioSource _layer;
    AudioSource _sting;
    AudioSource _active;
    AudioSource _fading;
    float _activeLevel;
    float _activeFadeIn = 2f;
    float _activeGain = 1f;
    float _fadingLevel;
    float _fadingGain = 1f;
    float _fadingDuration = CrossfadeOut;
    float _layerLevel;
    float _layerTarget;
    float _duckDb;
    MusicState _state = MusicState.None;
    MusicState _forced = MusicState.None;
    float _nextEvalAt;
    bool _victoryPlayed;

    // 씬 캐시
    BossController _boss;
    BossHealth _bossHealth;
    PlayerUltimateController _ultimate;
    int _bossPhase = 1;
    string _sceneName = "";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (s_instance != null)
            return;
        var go = new GameObject("__MusicDirector");
        DontDestroyOnLoad(go);
        s_instance = go.AddComponent<MusicDirector>();
    }

    /// <summary>QA/연출: 상태 강제 (None 이면 자동 판단으로 복귀).</summary>
    public static void Force(MusicState state)
    {
        if (s_instance != null)
        {
            s_instance._forced = state;
            s_instance._nextEvalAt = 0f;
        }
    }

    void Awake()
    {
        _deckA = CreateSource("Music_DeckA");
        _deckB = CreateSource("Music_DeckB");
        _layer = CreateSource("Music_BossLayer");
        _sting = CreateSource("Music_Sting");
        _sting.loop = false;
    }

    AudioSource CreateSource(string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var s = go.AddComponent<AudioSource>();
        s.playOnAwake = false;
        s.loop = true;
        s.spatialBlend = 0f;
        s.priority = 0;
        s.volume = 0f;
        s.ignoreListenerPause = true;
        s.bypassReverbZones = true;
        ChuonAudioMix.Route(s, ChuonAudioMix.Bus.Music);
        return s;
    }

    void OnEnable()
    {
        SceneManager.sceneLoaded += HandleSceneLoaded;
        HandleSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
    }

    void OnDisable()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        UnbindBoss();
    }

    void OnDestroy()
    {
        if (s_instance == this)
            s_instance = null;
    }

    void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (mode != LoadSceneMode.Single)
            return;
        _sceneName = scene.name;
        UnbindBoss();
        _ultimate = null;
        _bossPhase = 1;
        _victoryPlayed = false;
        _nextEvalAt = 0f;
    }

    void UnbindBoss()
    {
        if (_boss != null)
            _boss.OnBossPhaseChanged -= HandleBossPhase;
        _boss = null;
        _bossHealth = null;
    }

    void HandleBossPhase(int phase, float hp)
    {
        _bossPhase = Mathf.Max(_bossPhase, phase);
    }

    void Update()
    {
        float now = Time.unscaledTime;
        float dt = Time.unscaledDeltaTime;
        if (now >= _nextEvalAt)
        {
            _nextEvalAt = now + 0.25f;
            MusicState next = _forced != MusicState.None ? _forced : Resolve();
            if (next != _state)
                Transition(next);
        }

        float musicVol = AudioOptionsRuntime.MusicVolume;

        // 활성 덱 페이드 인
        if (_active != null)
        {
            _activeLevel = Mathf.MoveTowards(_activeLevel, 1f, dt / Mathf.Max(0.05f, _activeFadeIn));
            _active.volume = Curve(_activeLevel) * _activeGain * musicVol;
        }

        // 이전 덱 페이드 아웃
        if (_fading != null)
        {
            _fadingLevel = Mathf.MoveTowards(_fadingLevel, 0f, dt / Mathf.Max(0.05f, _fadingDuration));
            _fading.volume = Curve(_fadingLevel) * _fadingGain * musicVol;
            if (_fadingLevel <= 0f)
            {
                _fading.Stop();
                _fading.clip = null;
                _fading = null;
            }
        }

        // 보스 2페이즈 레이어
        _layerLevel = Mathf.MoveTowards(_layerLevel, _layerTarget, dt / 2.2f);
        if (_layer.isPlaying)
        {
            _layer.volume = Curve(_layerLevel) * BossLayerGain * musicVol;
            if (_layerLevel <= 0f && _layerTarget <= 0f && _state != MusicState.BossPhase1 && _state != MusicState.BossPhase2)
                _layer.Stop();
        }

        if (_sting.isPlaying)
            _sting.volume = 0.85f * musicVol;

        // 덕킹 (궁극기 / 대사·엔딩 자막)
        float duckTarget = 0f;
        if (_ultimate != null && _ultimate.IsCinematic)
            duckTarget = UltimateDuckDb;
        else if (TsukasaEndingPresenter.IsPlaying || IntroDialoguePresenter.AnyVisible || CinematicDirector.IsPlaying)
            duckTarget = DialogueDuckDb;
        float rate = duckTarget < _duckDb ? 40f : 10f; // 빠르게 내려가고 천천히 복귀
        _duckDb = Mathf.MoveTowards(_duckDb, duckTarget, dt * rate);
        ChuonAudioMix.SetFloatSafe(ChuonAudioMix.MusicDuckParam, _duckDb);
    }

    static float Curve(float x)
    {
        // 등전력에 가까운 페이드 곡선
        x = Mathf.Clamp01(x);
        return Mathf.Sin(x * Mathf.PI * 0.5f);
    }

    MusicState Resolve()
    {
        string n = _sceneName;
        if (n == "TitleScene")
            return MusicState.Title;
        if (n == "TeamLogoScene")
            return MusicState.None;
        if (n == "BootScene")
            return _state;
        if (n == "Tutorial")
            return MusicState.Tutorial;
        if (n == "CreditsScene")
            return MusicState.Credits;

        // 게임플레이 (Level_Facility / MainScene / Lobby)
        if (_boss == null)
        {
            _boss = FindObjectOfType<BossController>();
            if (_boss != null)
            {
                _boss.OnBossPhaseChanged += HandleBossPhase;
                _bossHealth = _boss.GetComponent<BossHealth>();
            }
        }
        if (_ultimate == null)
            _ultimate = FindObjectOfType<PlayerUltimateController>();

        if (_boss == null)
            return MusicState.Lobby;

        bool dead = _boss.currentState == BossState.Dead || (_bossHealth != null && _bossHealth.CurrentHP <= 0);
        if (dead)
            return _victoryPlayed && !_sting.isPlaying ? MusicState.Ending : MusicState.Victory;

        if (_boss.currentState == BossState.IntroIdle)
            return MusicState.Lobby;

        return _bossPhase >= 2 ? MusicState.BossPhase2 : MusicState.BossPhase1;
    }

    void Transition(MusicState next)
    {
        MusicState prev = _state;
        _state = next;

        bool prevBoss = prev == MusicState.BossPhase1 || prev == MusicState.BossPhase2;
        bool nextBoss = next == MusicState.BossPhase1 || next == MusicState.BossPhase2;

        if (prevBoss && nextBoss)
        {
            // 같은 루프 위에서 강도 레이어만 조절
            _layerTarget = next == MusicState.BossPhase2 ? 1f : 0f;
            return;
        }

        _layerTarget = next == MusicState.BossPhase2 ? 1f : 0f;

        if (next == MusicState.Victory)
        {
            FadeOutActive(0.6f);
            _layerTarget = 0f;
            _layer.Stop();
            AudioClip sting = Resources.Load<AudioClip>(VictoryResource);
            if (sting != null)
            {
                _sting.clip = sting;
                _sting.volume = 0.85f * AudioOptionsRuntime.MusicVolume;
                _sting.Play();
            }
            _victoryPlayed = true;
            return;
        }

        if (next == MusicState.Ending || next == MusicState.None)
        {
            // 처치 후 정적 (기획: 처치 후 BGM 정지)
            FadeOutActive(next == MusicState.None ? 1.2f : 2f);
            _layerTarget = 0f;
            return;
        }

        Track track = TrackFor(next);
        if (string.IsNullOrEmpty(track.Resource))
        {
            FadeOutActive(CrossfadeOut);
            return;
        }

        AudioClip clip = Resources.Load<AudioClip>(track.Resource);
        if (clip == null)
        {
            FadeOutActive(CrossfadeOut);
            return;
        }

        if (_active != null && _active.clip == clip && _active.isPlaying)
        {
            _activeGain = track.Gain;
            return;
        }

        FadeOutActive(CrossfadeOut);
        AudioSource deck = _active == _deckA ? _deckB : _deckA;
        if (deck == _fading)
        {
            // 두 덱 모두 사용 중이면 오래된 페이드 덱을 끊는다
            _fading.Stop();
            _fading = null;
        }
        deck.clip = clip;
        deck.loop = track.Loop;
        deck.volume = 0f;
        double startAt = AudioSettings.dspTime + 0.06;
        deck.PlayScheduled(startAt);

        if (nextBoss)
        {
            AudioClip layerClip = Resources.Load<AudioClip>(BossLayerResource);
            if (layerClip != null)
            {
                _layer.clip = layerClip;
                _layer.loop = true;
                _layer.volume = 0f;
                _layer.PlayScheduled(startAt);
                _layerLevel = 0f;
            }
        }
        else if (_layer.isPlaying)
        {
            _layerTarget = 0f;
        }

        _active = deck;
        _activeLevel = 0f;
        _activeFadeIn = track.FadeIn;
        _activeGain = track.Gain;
    }

    void FadeOutActive(float duration)
    {
        if (_active == null)
            return;
        if (_fading != null && _fading != _active)
        {
            _fading.Stop();
            _fading.clip = null;
        }
        _fading = _active;
        _fadingLevel = _activeLevel;
        _fadingGain = _activeGain;
        _fadingDuration = duration;
        _active = null;
        _activeLevel = 0f;
    }
}
