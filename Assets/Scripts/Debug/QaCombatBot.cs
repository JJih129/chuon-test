#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Dev-only automated "player" for the boss fight. Never runs by itself: attach it in Play Mode with
/// QaCombatBot.Attach() (e.g. via Unity MCP execute_code). It feeds the same input command buffer the
/// keyboard/mouse path uses, reacts to boss timing cues (parry / dodge), attacks in range, uses the
/// break finisher and the ultimate, logs a timeline and captures screenshots of key moments.
/// </summary>
public sealed class QaCombatBot : MonoBehaviour
{
    [Tooltip("0..1 chance to react correctly to a telegraphed attack (1 = perfect player).")]
    public float defenseSkill = 0.8f;
    [Tooltip("Seconds before the hit to press guard on parryable / normal / heavy attacks.")]
    public float parryLead = 0.14f; // hits land ~0.03-0.05 s before the cue duration; parry has 0.045 s startup
    [Tooltip("Seconds before the hit to press dodge on dodge-only / unblockable attacks.")]
    public float dodgeLead = 0.2f;
    public float attackRange = 3.2f;
    public float attackInterval = 0.32f;
    public bool captureScreenshots = true;
    [Tooltip("Walk toward the boss when out of attack range (needed when the boss holds at the arena centre).")]
    public bool approachBoss = true;
    [Tooltip("Use a heal ampoule at or below this HP (0 = never).")]
    public int healAtHp = 40;
    [Tooltip("Fight the nearest live DroneController (upper drone hall) before the boss.")]
    public bool fightDrones = true;
    public float droneEngageRange = 28f;

    public static string OutputDir => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Temp", "qa"));

    PlayerInputCommandBuffer _input;
    PlayerHealth _playerHealth;
    PlayerUltimateController _ultimate;
    PlayerLockOn _lockOn;
    Transform _player;
    BossController _boss;
    BossHealth _bossHealth;
    BossBreakController _break;

    readonly List<(float time, bool dodge)> _scheduled = new List<(float, bool)>();
    readonly StringBuilder _log = new StringBuilder();
    readonly HashSet<string> _captured = new HashSet<string>();
    float _nextAttackAt;
    float _startTime;
    bool _wasInBreak;
    bool _wasCinematic;
    int _parryBaseline;
    int _parries, _dodges, _attacks, _finishers, _ultimates, _cues;
    static readonly bool QaProfileUltRequested = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-qaProfileUlt") >= 0;
    int _profileUltState, _profileUltStopFrame;
    System.Random _rng = new System.Random(1234);

    public string Summary =>
        $"t={Time.unscaledTime - _startTime:F1}s playerHP={(_playerHealth ? _playerHealth.CurrentHP : -1)} " +
        $"bossHP={(_bossHealth ? _bossHealth.CurrentHP : -1)}/{(_bossHealth ? _bossHealth.MaxHP : -1)} " +
        $"break={(_break ? _break.CurrentBreak : -1):F0} cues={_cues} parryPress={_parries} parried={PlayerGuardController.QaParrySuccessCount - _parryBaseline} dodgePress={_dodges} " +
        $"attacks={_attacks} hitsTaken={HitsTaken} heals={Heals} healCancel={HealsCancelled} deaths={Deaths} respawns={Respawns} bossDeadAt={BossDeadAt:F0} minHP={(MinHp < 0 ? -1 : MinHp)} finishers={_finishers} ultimates={_ultimates} ts={Time.timeScale:F2} autoLock={LockAttempts - LockFailures}/{LockAttempts}";

    public string Log => _log.ToString();
    public int AttackCount => _attacks;

    /// <summary>Clears Temp/qa/bot_log.txt (call before a new session).</summary>
    public static void ResetLog()
    {
        string path = Path.Combine(OutputDir, "bot_log.txt");
        if (File.Exists(path)) File.Delete(path);
    }

    /// <summary>Copies the current bot_log.txt to Temp/qa/&lt;name&gt;.txt.</summary>
    public static void SaveLogAs(string name)
    {
        string path = Path.Combine(OutputDir, "bot_log.txt");
        if (File.Exists(path)) File.Copy(path, Path.Combine(OutputDir, name + ".txt"), true);
    }

    public static QaCombatBot Attach(float skill = 0.8f)
    {
        var existing = FindObjectOfType<QaCombatBot>();
        if (existing != null)
            return existing;

        var go = new GameObject("__QaCombatBot");
        var bot = go.AddComponent<QaCombatBot>();
        bot.defenseSkill = Mathf.Clamp01(skill);
        return bot;
    }

    void Start()
    {
        _startTime = Time.unscaledTime;
        _parryBaseline = PlayerGuardController.QaParrySuccessCount;
        Directory.CreateDirectory(OutputDir);
        Resolve();
        AttackHitbox.QaWindowActivated += HandleHitboxActivated;
        Note("attached");
    }

    // ---- Cue honesty measurement: announced cue duration vs. when the boss hitbox actually opens ----
    static readonly Dictionary<string, List<(float announced, float actual)>> s_cueTiming = new Dictionary<string, List<(float, float)>>();
    static int s_unannouncedHits;
    // FIFO: a per-hit cue for hit N can be raised in the same frame hit N-1 opens, so match oldest-first.
    readonly List<(float at, float announced, string key)> _pendingCues = new List<(float, float, string)>();
    float _pendingPreAt = -1f, _pendingPreLead;
    string _pendingPreKey;

    void RecordCueTiming(string key, float announced, float actual)
    {
        if (!s_cueTiming.TryGetValue(key, out var list))
            s_cueTiming[key] = list = new List<(float, float)>();
        list.Add((announced, actual));
        Note($"hitbox ON {key} announced={announced:F3} actual={actual:F3}");
    }

    public static void ResetCueTiming() { s_cueTiming.Clear(); s_unannouncedHits = 0; }

    /// <summary>Per cue kind: n, mean announced, mean actual (cue to hitbox ON), mean and min slack (actual - announced).</summary>
    public static string CueTimingReport()
    {
        var sb = new StringBuilder();
        foreach (var kv in s_cueTiming)
        {
            float a = 0f, r = 0f, minSlack = float.MaxValue;
            foreach (var e in kv.Value) { a += e.announced; r += e.actual; minSlack = Mathf.Min(minSlack, e.actual - e.announced); }
            int n = kv.Value.Count;
            sb.Append($"{kv.Key}: n={n} announced={a / n:F3} actual={r / n:F3} slack={(r - a) / n:+0.000;-0.000} minSlack={minSlack:+0.000;-0.000}\n");
        }
        sb.Append($"unannounced hitbox activations={s_unannouncedHits}");
        return sb.ToString();
    }

    void HandleHitboxActivated(AttackHitbox hitbox)
    {
        if (_boss == null || hitbox == null)
            return;
        Transform root = hitbox.attackerRoot != null ? hitbox.attackerRoot : hitbox.transform;
        if (root != _boss.transform && !root.IsChildOf(_boss.transform))
            return;

        // Legacy pre-attack telegraph (wind-up before the attack trigger) -> first hitbox of that attack.
        if (_pendingPreKey != null)
        {
            if (Time.time - _pendingPreAt <= _pendingPreLead + 1.5f)
                RecordCueTiming(_pendingPreKey, _pendingPreLead, Time.time - _pendingPreAt);
            _pendingPreKey = null;
        }

        // Drop cues whose hit never came (pattern parried / interrupted).
        while (_pendingCues.Count > 0 && Time.time - _pendingCues[0].at > _pendingCues[0].announced + 0.6f)
            _pendingCues.RemoveAt(0);

        if (_pendingCues.Count == 0)
        {
            s_unannouncedHits++;
            Note($"hitbox ON without cue pat={_boss.CurrentPatternName}");
            return;
        }

        var cue = _pendingCues[0];
        _pendingCues.RemoveAt(0);
        RecordCueTiming(cue.key, cue.announced, Time.time - cue.at);
    }

    void OnDestroy()
    {
        PlayerMoveController.QaMoveInputOverride = null;
        PlayerGuardController.QaClearGuardInput();
        AttackHitbox.QaWindowActivated -= HandleHitboxActivated;
        if (_consumables != null) _consumables.OnHealCancelled -= HandleHealCancelled;
        if (_boss != null)
        {
            _boss.OnAttackTimingCue -= HandleCue;
            _boss.OnAttackTelegraph -= HandleTelegraph;
        }

        foreach (DroneController d in _hookedDrones)
            if (d != null) d.AttackTelegraphed -= HandleDroneTelegraph;
        _hookedDrones.Clear();
    }

    // Drones: pick the nearest live one in range (throttled scan) and hook its shot telegraph.
    readonly HashSet<DroneController> _hookedDrones = new HashSet<DroneController>();
    DroneController _drone;
    Transform _lockedTo;
    float _nextDroneScanAt, _nextResolveAt;
    float _nextLockTryAt, _lockMismatchSince = -1f;
    public int LockAttempts { get; private set; }
    public int LockFailures { get; private set; }

    // 플레이어와 같은 경로로 락온: 자동 락온(거리/시야 원뿔/가림 검사)을 쓰고, 실패하면 카메라를 타깃 쪽으로 돌린 뒤 다시 시도한다.
    void UpdateLockOn(Transform target)
    {
        if (_lockOn == null || target == null)
            return;

        float now = Time.unscaledTime;
        if (_lockOn.IsLockedOn())
        {
            Transform current = _lockOn.CurrentTarget;
            bool matches = current != null && current.root == target.root;
            if (matches) { _lockMismatchSince = -1f; _lockedTo = target; return; }
            if (_lockMismatchSince < 0f) _lockMismatchSince = now;
            if (now - _lockMismatchSince < 1.0f)
                return;
            _lockOn.Unlock(); // 다른 적에 고정된 채 1 s → 락온 키 탭(해제) 후 다시 잡는다
            _lockMismatchSince = -1f;
        }

        if (now < _nextLockTryAt)
            return;
        _nextLockTryAt = now + 0.25f;
        LockAttempts++;
        if (_lockOn.TryAutoLock() && _lockOn.CurrentTarget != null && _lockOn.CurrentTarget.root == target.root)
        {
            _lockedTo = target;
            return;
        }
        if (_lockOn.IsLockedOn() && _lockOn.CurrentTarget != null && _lockOn.CurrentTarget.root != target.root)
            _lockMismatchSince = now;

        LockFailures++;
        // 마우스로 시선을 돌리는 것에 해당: 다음 시도에서 시야 원뿔 안에 들어오게 한다.
        SoulsCameraRig rig = SoulsCameraRig.Active;
        if (rig != null && _player != null)
        {
            Vector3 d = target.position - _player.position;
            d.y = 0f;
            if (d.sqrMagnitude > 0.01f)
                rig.SnapToYaw(Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg);
        }
    }

    void UpdateDroneTarget()
    {
        if (!fightDrones || _player == null)
        {
            _drone = null;
            return;
        }

        if (_drone != null && _drone.IsDead)
            _drone = null;
        if (Time.unscaledTime < _nextDroneScanAt && _drone != null)
            return;

        _nextDroneScanAt = Time.unscaledTime + 0.3f;
        DroneController best = null;
        float bestSqr = droneEngageRange * droneEngageRange;
        foreach (DroneController d in FindObjectsOfType<DroneController>())
        {
            if (d == null || d.IsDead || !d.isActiveAndEnabled)
                continue;
            if (_hookedDrones.Add(d))
                d.AttackTelegraphed += HandleDroneTelegraph;
            float sqr = (d.transform.position - _player.position).sqrMagnitude;
            if (sqr < bestSqr) { bestSqr = sqr; best = d; }
        }

        if (best != _drone && best != null)
            Note($"target drone {best.name} dist={Mathf.Sqrt(bestSqr):F1}");
        _drone = best;
    }

    void HandleDroneTelegraph(DroneController drone)
    {
        if (drone == null || _player == null)
            return;
        // A human ignores a drone on another floor / far out of range (its shots can't reach through the floor).
        Vector3 toDrone = drone.transform.position - _player.position;
        if (Mathf.Abs(toDrone.y) > 8f || toDrone.magnitude > droneEngageRange * 1.5f)
            return;

        _cues++;
        bool react = _rng.NextDouble() < defenseSkill;
        float travel = Vector3.Distance(drone.transform.position, _player.position) / Mathf.Max(1f, drone.projectileSpeed);
        float t = drone.AttackTelegraphLeadTime + travel;
        Note($"drone shot in {t:F2}s react={react}");
        if (react)
            _scheduled.Add((Time.time + Mathf.Max(0f, t - dodgeLead), true));
    }

    void Resolve()
    {
        var refs = FindObjectOfType<PlayerReferences>();
        _player = refs != null ? refs.transform : null;
        if (_player != null)
        {
            _input = _player.GetComponentInChildren<PlayerInputCommandBuffer>(true);
            _playerHealth = _player.GetComponentInChildren<PlayerHealth>(true);
            _ultimate = _player.GetComponentInChildren<PlayerUltimateController>(true);
            _lockOn = _player.GetComponentInChildren<PlayerLockOn>(true);
            _combat = _player.GetComponentInChildren<PlayerCombatController>(true);
            PlayerConsumables consumables = _player.GetComponentInChildren<PlayerConsumables>(true);
            if (consumables != _consumables)
            {
                if (_consumables != null) _consumables.OnHealCancelled -= HandleHealCancelled;
                _consumables = consumables;
                if (_consumables != null) _consumables.OnHealCancelled += HandleHealCancelled;
            }
        }

        if (_boss == null)
        {
            _boss = FindObjectOfType<BossController>();
            if (_boss != null)
            {
                _boss.OnAttackTimingCue += HandleCue;
                _boss.OnAttackTelegraph += HandleTelegraph;
                _bossHealth = _boss.GetComponentInChildren<BossHealth>(true) ?? _boss.GetComponentInParent<BossHealth>();
                _break = _boss.GetComponentInChildren<BossBreakController>(true) ?? _boss.GetComponentInParent<BossBreakController>();
            }
        }
    }

    void HandleCue(BossAttackTelegraphType type, float duration, BossAttackTimingCueFlags flags)
    {
        _cues++;
        _lastCueTime = Time.time;
        _lastThreatTime = Time.time;
        _pendingCues.Add((Time.time, duration, (flags == BossAttackTimingCueFlags.WeaponFlash ? "perhit/" : "opener/") + type));
        bool dodge = type == BossAttackTelegraphType.DodgeOnly || type == BossAttackTelegraphType.Unblockable || type == BossAttackTelegraphType.Ranged;
        bool react = _rng.NextDouble() < defenseSkill;
        Note($"cue {type} in {duration:F2}s react={react} pat={_boss.CurrentPatternName}");
        if (!react)
            return;

        float lead = dodge ? dodgeLead : parryLead;
        Schedule(duration - lead, dodge);
        Capture("telegraph_" + type);
        // Per-hit follow-up cues (BossController.StringHitCues) are weapon-flash only: capture mid-flash.
        if (flags == BossAttackTimingCueFlags.WeaponFlash && _pendingCaptureKey == null && !_captured.Contains("followup_" + type))
        {
            _pendingCaptureKey = "followup_" + type;
            _pendingCaptureAt = Time.time + duration * 0.3f;
        }
    }

    string _pendingCaptureKey;

    [Tooltip("Human-like visual reaction time (s): a press can't happen sooner than this after the cue (0 = bot).")]
    public float reactionTime = 0f;
    [Tooltip("Human-like timing error (s, uniform +-).")]
    public float timingJitter = 0f;

    // One press per swing: a newer cue for the same swing (pre-telegraph -> opener timing cue) supersedes
    // a pending reaction of the same kind instead of stacking a second press (which would eat parry recovery).
    void Schedule(float delay, bool dodge)
    {
        float jitter = timingJitter > 0f ? (float)(_rng.NextDouble() * 2.0 - 1.0) * timingJitter : 0f;
        float at = Time.time + Mathf.Max(reactionTime, delay + jitter);
        for (int i = _scheduled.Count - 1; i >= 0; i--)
            if (_scheduled[i].dodge == dodge && Mathf.Abs(_scheduled[i].time - at) < 0.35f)
                _scheduled.RemoveAt(i);
        _scheduled.Add((at, dodge));
    }
    float _pendingCaptureAt;

    // Legacy telegraph path (patterns without timing data). Lead time is the wind-up before the swing.
    void HandleTelegraph(AttackTelegraphType type, float leadTime, string label)
    {
        if (Time.time - _lastCueTime < 0.2f)
            return; // a timing cue for the same swing already scheduled a reaction

        _cues++;
        _lastThreatTime = Time.time;
        _pendingPreAt = Time.time; _pendingPreLead = leadTime; _pendingPreKey = "pre/" + type;
        bool dodge = type == AttackTelegraphType.Dodge || type == AttackTelegraphType.Danger;
        bool react = _rng.NextDouble() < defenseSkill;
        Note($"telegraph {type} lead {leadTime:F2}s ({label}) react={react}");
        if (!react || leadTime <= 0f)
            return;

        float lead = dodge ? dodgeLead : parryLead;
        Schedule(leadTime - lead, dodge);
    }

    float _lastCueTime = -10f;
    float _lastThreatTime = -10f;
    [Tooltip("Like a human player: don't start a new attack this soon after a boss telegraph (keeps guard/parry available).")]
    public float holdAttackAfterThreat = 0.6f;
    float _releaseGuardAt;
    float _nextEscapeAt;
    PlayerCombatController _combat;
    PlayerConsumables _consumables;
    float _nextHealAt;
    public int Heals { get; private set; }
    public int HealsCancelled { get; private set; }
    public int Respawns { get; private set; }
    void HandleHealCancelled() { HealsCancelled++; Note("heal CANCELLED (hit during motion)"); }
    int _lastHp = -1;
    public int HitsTaken { get; private set; }
    public int Deaths { get; private set; }
    public float BossDeadAt { get; private set; } = -1f;
    public int MinHp { get; private set; } = -1;
    bool _wasDead;

    void Update()
    {
        if (_playerHealth != null)
        {
            if (_lastHp >= 0 && _playerHealth.CurrentHP < _lastHp) { HitsTaken++; Note($"HIT taken -{_lastHp - _playerHealth.CurrentHP} pat={(_drone != null ? "drone" : _boss != null ? _boss.CurrentPatternName : "?")} hp={_playerHealth.CurrentHP}{DefenseState()} abs={Time.unscaledTime:F2} res=({PlayerDefenseResolver.QaLastResolve})"); }
            _lastHp = _playerHealth.CurrentHP;
            if (MinHp < 0 || _lastHp < MinHp) MinHp = _lastHp;
            if (_playerHealth.IsDead && !_wasDead) { Deaths++; Note("PLAYER DEAD"); }
            if (!_playerHealth.IsDead && _wasDead) { Respawns++; Note($"RESPAWN hp={_playerHealth.CurrentHP}"); }
            _wasDead = _playerHealth.IsDead;
        }

        if (_input == null)
        {
            Resolve();
            return;
        }

        if (_boss == null && Time.unscaledTime >= _nextResolveAt)
        {
            _nextResolveAt = Time.unscaledTime + 1f;
            Resolve();
        }

        UpdateDroneTarget();
        Transform target = _drone != null ? _drone.transform : (_boss != null ? _boss.transform : null);
        if (target == null)
        {
            PlayerMoveController.QaMoveInputOverride = null;
            return;
        }

        if (_pendingCaptureKey != null && Time.time >= _pendingCaptureAt)
        {
            Capture(_pendingCaptureKey);
            _pendingCaptureKey = null;
        }

        if (_playerHealth != null && _playerHealth.IsDead)
        {
            PlayerMoveController.QaMoveInputOverride = null;
            Capture("player_dead");
            return;
        }

        if (_drone == null && _bossHealth != null && _bossHealth.IsDead)
        {
            if (BossDeadAt < 0f) { BossDeadAt = Time.unscaledTime - _startTime; Note("BOSS DEAD"); }
            Capture("boss_dead");
            return;
        }

        UpdateLockOn(target);

        // Scheduled defensive reactions (scaled time, like the attack timing they answer).
        for (int i = _scheduled.Count - 1; i >= 0; i--)
        {
            if (Time.time < _scheduled[i].time)
                continue;

            if (_scheduled[i].dodge) { _input.RecordDodgePress(); _dodges++; Note("dodge"); }
            else { PlayerGuardController.QaPressGuard(); _releaseGuardAt = Time.unscaledTime + 0.12f; _parries++; Note((_combat != null && _combat.IsInHit ? "parry (from hit-stun)" : "parry") + DefenseState()); }
            _scheduled.RemoveAt(i);
        }

        if (_releaseGuardAt > 0f && Time.unscaledTime >= _releaseGuardAt) { PlayerGuardController.QaReleaseGuard(); _releaseGuardAt = 0f; }

        // Escape multi-hit strings with a recovery dodge, like a real player would.
        if (_combat != null && _combat.IsInHit && _combat.HitStateElapsed > 0.32f && Time.time >= _nextEscapeAt)
        {
            _input.RecordDodgePress(); _dodges++; _nextEscapeAt = Time.time + 0.8f; Note("escape dodge");
        }

        bool inBreak = _break != null && _break.IsInBreak;
        if (inBreak && !_wasInBreak) { Note("BREAK start"); Capture("break"); }
        _wasInBreak = inBreak;

        if (_profileUltState == 1 && Time.frameCount >= _profileUltStopFrame)
        {
            _profileUltState = 2;
            UnityEngine.Profiling.Profiler.enabled = false;
            UnityEngine.Profiling.Profiler.enableBinaryLog = false;
            UnityEngine.Profiling.Profiler.logFile = "";
            Note("profiler off");
        }

        bool cinematic = _ultimate != null && _ultimate.IsCinematic;
        if (cinematic && !_wasCinematic) { Note("ULTIMATE start"); Capture("ultimate"); }
        if (!cinematic && _wasCinematic) Note("ULTIMATE end");
        _wasCinematic = cinematic;
        if (cinematic)
            return;

        // -qaProfileUlt: 궁극기 발동 직전부터 ~120프레임을 프로파일러 바이너리 로그(Temp/qa/ult_profile.raw)로 남긴다 (첫 발동 히치 분석용)
        if (_ultimate != null && _ultimate.IsGaugeReady && QaProfileUltRequested && _profileUltState == 0)
        {
            _profileUltState = 1;
            Directory.CreateDirectory(OutputDir);
            UnityEngine.Profiling.Profiler.logFile = Path.Combine(OutputDir, "ult_profile");
            UnityEngine.Profiling.Profiler.enableBinaryLog = true;
            UnityEngine.Profiling.Profiler.enabled = true;
            _profileUltStopFrame = Time.frameCount + 1200; // 발동 성공 시 +90 프레임으로 줄인다
            Note("profiler on");
            return;
        }
        if (_profileUltState == 1 && Time.frameCount < _profileUltStopFrame - 1190)
            return;

        if (_ultimate != null && _ultimate.IsGaugeReady && _ultimate.TryActivate())
        {
            if (_profileUltState == 1)
                _profileUltStopFrame = Time.frameCount + 90;
            _ultimates++;
            Note("ultimate activate");
            return;
        }

        float dist = Vector3.Distance(_player.position, target.position);
        bool defensePending = _scheduled.Count > 0;
        bool inHit = _combat != null && _combat.IsInHit;

        if (healAtHp > 0 && _consumables != null && _consumables.CurrentAmpoule > 0 && _playerHealth.CurrentHP <= healAtHp
            && !defensePending && !inHit && Time.time >= _nextHealAt)
        {
            if (_consumables.TryUseAmpoule())
            {
                Heals++;
                Note($"heal start hp={_playerHealth.CurrentHP} left={_consumables.CurrentAmpoule}");
            }
            _nextHealAt = Time.time + 1.5f;
        }

        UpdateApproach(target, dist, defensePending);
        // Human-like: hold attacks while a boss string is running (unless it opened a punish window).
        bool bossBusy = _drone == null && _boss != null && !inBreak && !_boss.IsPunishWindowActive && !string.IsNullOrEmpty(_boss.CurrentPatternName);
        bool threatened = _drone == null && (bossBusy || Time.time - _lastThreatTime < holdAttackAfterThreat);
        if (dist <= attackRange && !defensePending && !threatened && Time.time >= _nextAttackAt)
        {
            _nextAttackAt = Time.time + attackInterval;
            if (inBreak && _break.IsFinisherAvailable)
            {
                _finishers++;
                Note("finisher attempt");
            }

            bool heavy = _rng.NextDouble() < 0.2;
            if (heavy) _input.RecordAttackHeavyPress(); else _input.RecordAttackLightPress();
            _attacks++;
            if (_attacks == 6)
                Capture("combo");
        }
    }

    Vector3 _lastProgressPos;
    float _progressCheckAt, _detourUntil, _detourSign = 1f;

    void UpdateApproach(Transform target, float dist, bool defensePending)
    {
        if (!approachBoss || defensePending || dist <= attackRange * 0.85f)
        {
            PlayerMoveController.QaMoveInputOverride = null;
            return;
        }

        Camera cam = Camera.main;
        Vector3 toBoss = target.position - _player.position;
        toBoss.y = 0f;
        if (cam == null || toBoss.sqrMagnitude < 0.0001f)
        {
            PlayerMoveController.QaMoveInputOverride = null;
            return;
        }

        Vector3 fwd = cam.transform.forward; fwd.y = 0f; fwd.Normalize();
        Vector3 right = cam.transform.right; right.y = 0f; right.Normalize();
        toBoss.Normalize();

        // Crude obstacle handling: if we made no progress for 0.8 s, side-step (alternating) for 1.2 s.
        if (Time.time >= _progressCheckAt)
        {
            if ((_player.position - _lastProgressPos).sqrMagnitude < 0.09f && _progressCheckAt > 0f)
            {
                _detourUntil = Time.time + 1.2f;
                _detourSign = -_detourSign;
                Note("stuck -> detour");
            }
            _lastProgressPos = _player.position;
            _progressCheckAt = Time.time + 0.8f;
        }
        toBoss = SteerToward(target, toBoss);
        if (Time.time < _detourUntil)
            toBoss = (toBoss * 0.25f + Vector3.Cross(Vector3.up, toBoss) * _detourSign).normalized;
        PlayerMoveController.QaMoveInputOverride = new Vector2(Vector3.Dot(toBoss, right), Vector3.Dot(toBoss, fwd));
    }

    PlayerGuardController _guard;
    string DefenseState()
    {
        if (_guard == null && _player != null)
            _guard = _player.GetComponentInChildren<PlayerGuardController>(true);
        if (_guard == null || _combat == null)
            return string.Empty;
        return $" [atk={_combat.IsAttacking} hit={_combat.IsInHit} parryWin={_guard.GetParryWindowRemaining():F2} startup={_guard.GetParryStartupRemaining():F2} recov={_guard.IsParryRecovering}]";
    }

    // Obstacle-aware steering: follow a NavMesh path when the level has one, otherwise probe ahead with a
    // sphere cast and fan out (±20..120°) to the first clear direction. Drones fly, so the path goal is the
    // NavMesh point under the target.
    UnityEngine.AI.NavMeshPath _navPath; // created lazily (not allowed in a field initializer)
    float _nextPathAt;
    Vector3 _pathCorner;
    bool _hasPathCorner;

    Vector3 SteerToward(Transform target, Vector3 desired)
    {
        if (Time.time >= _nextPathAt)
        {
            _nextPathAt = Time.time + 0.3f;
            _hasPathCorner = false;
            if (UnityEngine.AI.NavMesh.SamplePosition(_player.position, out UnityEngine.AI.NavMeshHit from, 2f, UnityEngine.AI.NavMesh.AllAreas)
                && UnityEngine.AI.NavMesh.SamplePosition(target.position, out UnityEngine.AI.NavMeshHit to, 8f, UnityEngine.AI.NavMesh.AllAreas)
                && UnityEngine.AI.NavMesh.CalculatePath(from.position, to.position, UnityEngine.AI.NavMesh.AllAreas, _navPath ??= new UnityEngine.AI.NavMeshPath())
                && _navPath.status != UnityEngine.AI.NavMeshPathStatus.PathInvalid
                && _navPath.corners.Length >= 2)
            {
                _pathCorner = _navPath.corners[1];
                _hasPathCorner = true;
            }
        }

        if (_hasPathCorner)
        {
            Vector3 toCorner = _pathCorner - _player.position;
            toCorner.y = 0f;
            if (toCorner.sqrMagnitude > 0.04f)
                return toCorner.normalized;
        }

        if (!IsBlocked(desired, target))
            return desired;

        for (int step = 1; step <= 6; step++)
        {
            float angle = step * 20f;
            Vector3 a = Quaternion.Euler(0f, angle * _detourSign, 0f) * desired;
            if (!IsBlocked(a, target)) return a;
            Vector3 b = Quaternion.Euler(0f, -angle * _detourSign, 0f) * desired;
            if (!IsBlocked(b, target)) { _detourSign = -_detourSign; return b; }
        }

        return desired;
    }

    bool IsBlocked(Vector3 direction, Transform target)
    {
        Vector3 origin = _player.position + Vector3.up * 0.9f;
        if (!Physics.SphereCast(origin, 0.35f, direction, out RaycastHit hit, 1.6f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            return false;
        Transform t = hit.collider.transform;
        return !(t.IsChildOf(_player) || (target != null && t.IsChildOf(target.root)));
    }

    void Note(string message)
    {
        string line = $"[{Time.unscaledTime - _startTime:F2}] {message}{(_bossHealth != null && (message.StartsWith("ULTIMATE") || message.StartsWith("BREAK") || message.StartsWith("finisher")) ? $" bossHP={_bossHealth.CurrentHP}" : "")}\n";
        _log.Append(line);
        try { File.AppendAllText(Path.Combine(OutputDir, "bot_log.txt"), line); } catch (IOException) { }
    }

    void Capture(string key)
    {
        if (!captureScreenshots || !_captured.Add(key))
            return;

        string path = Path.Combine(OutputDir, $"qa_{_captured.Count:00}_{key}.png");
        ScreenCapture.CaptureScreenshot(path);
        Note("screenshot " + path);
    }
}
#endif
