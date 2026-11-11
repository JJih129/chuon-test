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
    public int healAtHp = 50; // 반복 20: 40 -> 50 (후반 연타 피해 속도에서 사람은 더 일찍 회복한다)
    [Tooltip("Fight the nearest live DroneController (upper drone hall) before the boss.")]
    public bool fightDrones = true;
    public float droneEngageRange = 28f;
    [Tooltip("Fight EnemyBrain mobs (android / enforcer / turret) on the player's floor before the boss.")]
    public bool fightMobs = true;
    public float mobEngageRange = 30f;
    [Tooltip("Mobs are smaller than the boss: close in to this range before swinging.")]
    public float mobAttackRange = 2.3f;
    [Tooltip("Full-level run: with no enemy around, walk the Level_Facility upper-floor route (combat gate -> elevator approach -> board).")]
    public bool followLobbyRoute = false;
    [Tooltip("Infiltration tutorial run (Tutorial.unity): walk InfiltrationTutorialDirector goals and perform each step's taught action.")]
    public bool followTutorialRoute = false;

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
        $"attacks={_attacks} hitsTaken={HitsTaken} heals={Heals} healCancel={HealsCancelled} deaths={Deaths} respawns={Respawns} bossDeadAt={BossDeadAt:F0} minHP={(MinHp < 0 ? -1 : MinHp)} finishers={_finishers} ultimates={_ultimates} ts={Time.timeScale:F2} autoLock={LockAttempts - LockFailures}/{LockAttempts}" +
        $" mobKills={MobKills} mobHitsTaken={MobHitsTaken} mobParryPress={MobParryPresses} mobDodgePress={MobDodgePresses} maxMeleeTokens={MaxMeleeTokens} lockOnDeadFrames={LockOnDeadMobFrames} mobFight={MobFightStart:F0}-{MobFightEnd:F0}s";

    /// <summary>Per-mob QA line (hits both ways, parries, staggers, cue honesty).</summary>
    public static string MobReport()
    {
        var sb = new StringBuilder();
        foreach (EnemyBrain b in Resources.FindObjectsOfTypeAll<EnemyBrain>())
        {
            if (b == null || !b.gameObject.scene.IsValid())
                continue;
            string extra = b is ShieldEnforcerBrain se ? $" blocks={se.BlockCount} guardBreaks={se.GuardBreakCount}" : "";
            sb.Append($"{b.name} ({b.GetType().Name}) dead={b.IsDead} state={b.State} hitsRecv={b.HitsReceived} landed={b.HitsLanded} strikesDelivered={b.StrikesDelivered} parried={b.ParriedCount} staggers={b.StaggerCount} cues={b.CueCount} maxCueErr={b.MaxCueHonestyError:F3}{extra}\n");
        }
        return sb.ToString();
    }

    public string Log => _log.ToString();

    // ---- 반복 16: Type-D 개편 (봉쇄 노드 / 배기 회피 / 임계 폭주 엄폐) ----
    float _evadeDecidedFor = -1f;
    bool _evadeChoice;
    public int CoverRuns { get; private set; }
    public int VentEvades { get; private set; }

    Transform _nodeTarget, _skipNode;
    float _nodeTargetSince, _skipNodeUntil, _nodeTargetDist;

    Transform ResolveOverhaulNodeTarget()
    {
        TypeDOverhaul oh = TypeDOverhaul.Active;
        if (oh == null || oh.Lockdown == null || !oh.Lockdown.IsActive || _player == null)
            return null;
        // 같은 노드에 8 s 이상 못 닿으면(막힘) 다른 노드로 바꾼다
        if (_nodeTarget != null && (_nodeTarget.GetComponent<BossPowerNode>()?.IsDead ?? true))
            _nodeTarget = null;
        // 반복 17: 방어하느라 늦게 가는 것은 '막힘'이 아니다 — 8 s 동안 1 m 이상 가까워졌으면 같은 노드를 유지
        // (예전에는 시간만 보고 바꿔서 18 m 떨어진 노드 사이를 왕복하다 봉쇄 단계를 못 넘었다)
        if (_nodeTarget != null && Time.time - _nodeTargetSince > 8f && (_nodeTarget.position - _player.position).sqrMagnitude > 9f
            && Vector3.Distance(_nodeTarget.position, _player.position) < _nodeTargetDist - 1f)
        {
            _nodeTargetSince = Time.time;
            _nodeTargetDist = Vector3.Distance(_nodeTarget.position, _player.position);
        }
        if (_nodeTarget != null && Time.time - _nodeTargetSince > 8f && (_nodeTarget.position - _player.position).sqrMagnitude > 9f)
        {
            _skipNode = _nodeTarget;
            _skipNodeUntil = Time.time + 8f;
            Note($"node target switch (stuck) player={_player.position:F1} dist={Vector3.Distance(_nodeTarget.position, _player.position):F1} boss={(_boss != null ? Vector3.Distance(_boss.transform.position, _player.position).ToString("F1") : "-")}");
            _nodeTarget = null;
        }
        if (_nodeTarget != null)
            return _nodeTarget;
        Transform best = null;
        float bestD = float.PositiveInfinity;
        foreach (BossPowerNode n in oh.Lockdown.Nodes)
        {
            if (n == null || n.IsDead || (n.transform == _skipNode && Time.time < _skipNodeUntil && oh.Lockdown.NodesAlive > 1))
                continue;
            float d = (n.transform.position - _player.position).sqrMagnitude;
            if (d < bestD) { bestD = d; best = n.transform; }
        }
        if (best != null)
        {
            _nodeTarget = best;
            _nodeTargetSince = Time.time;
            _nodeTargetDist = Mathf.Sqrt(bestD);
            Note("node target " + best.position.ToString("F1"));
        }
        return best;
    }

    // 사람처럼: 큐를 본 뒤 한 번 결정 (defenseSkill 확률로 올바른 대응). true = 이번 프레임 이동/공격을 대신함.
    bool OverhaulEvade()
    {
        TypeDOverhaul oh = TypeDOverhaul.Active;
        if (oh == null || _boss == null || _player == null)
            return false;
        bool desperation = oh.IsDesperationActive && oh.Lockdown != null && oh.Lockdown.CoverRaised;
        bool vent = _boss.ActiveSpecialPattern == BossSpecialPatternKind.HeatVent && !oh.IsVentExposed;
        if (!desperation && !vent)
            return false;
        float key = desperation ? oh.QaLastPhaseChangeAt : _boss.QaVents;
        if (_evadeDecidedFor != key)
        {
            _evadeDecidedFor = key;
            _evadeChoice = _rng.NextDouble() < defenseSkill;
            if (_evadeChoice) { if (desperation) CoverRuns++; else VentEvades++; }
            Note((desperation ? "desperation" : "vent") + " evade=" + _evadeChoice);
            if (desperation) Capture("desperation");
        }
        if (!_evadeChoice)
            return false;
        Vector3 goal;
        if (desperation)
        {
            if (!oh.Lockdown.TryGetCoverSpot(_player.position, _boss.transform.position, out goal))
                return false;
        }
        else
        {
            Vector3 away = _player.position - _boss.transform.position;
            away.y = 0f;
            if (away.magnitude > oh.VentRadius + 1.2f)
            {
                PlayerMoveController.QaMoveInputOverride = null;
                return true;
            }
            goal = _boss.transform.position + (away.sqrMagnitude > 0.01f ? away.normalized : -_boss.transform.forward) * (oh.VentRadius + 2f);
        }
        Vector3 to = goal - _player.position;
        to.y = 0f;
        Camera cam = Camera.main;
        if (to.magnitude < 0.35f || cam == null)
        {
            PlayerMoveController.QaMoveInputOverride = null;
            return true;
        }
        Vector3 f = cam.transform.forward; f.y = 0f; f.Normalize();
        Vector3 r = cam.transform.right; r.y = 0f; r.Normalize();
        to.Normalize();
        PlayerMoveController.QaMoveInputOverride = new Vector2(Vector3.Dot(to, r), Vector3.Dot(to, f));
        return true;
    }

    public static string OverhaulReport()
    {
        TypeDOverhaul oh = TypeDOverhaul.Active;
        string s = $"exec front={ExecutionDirector.QaBossFront} back={ExecutionDirector.QaBossBack} final={ExecutionDirector.QaBossFinal} mob={ExecutionDirector.QaMob} takedown={ExecutionDirector.QaTakedowns} nodes={BossPowerNode.QaDestroyed}";
        if (oh != null)
        {
            BossImpactStagger im = oh.Impact;
            s += $" stage={oh.CurrentStage} lockdowns={oh.QaLockdowns} overloads={oh.QaOverloads} purges={oh.QaPurges} strongParry={oh.QaStrongParries} blinks={oh.QaBlinks} finalBreaks={oh.QaFinalBreaks} delayed={oh.QaDelayedAttacks}" +
                 $" grabs={oh.Boss.QaGrabs} vents={oh.Boss.QaVents} desp={oh.Boss.QaDesperations} visor={oh.Parts.VisorBroken} blade={oh.Parts.BladeBroken}" +
                 $" flinch={im.QaFlinches} stagger={im.QaStaggers} knock={im.QaKnockbacks} down={im.QaDowns} wall={im.QaWallSplats}";
        }
        PlayerOverhaulReactions pr = PlayerOverhaulReactions.Instance;
        if (pr != null)
            s += $" launches={pr.QaLaunches} guardPush={pr.QaGuardPushes}";
        return s + "\ncue " + OverhaulCueLedger.Report();
    }
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
        foreach (EnemyBrain b in _hookedMobs)
            if (b != null) { b.CueIssued -= HandleMobCue; b.Died -= HandleMobDied; }
        _hookedMobs.Clear();
    }

    // ---- Mobs (EnemyBrain): nearest live one on this floor; strikes are answered from the brain's own hit clock ----
    readonly HashSet<EnemyBrain> _hookedMobs = new HashSet<EnemyBrain>();
    readonly HashSet<long> _answeredStrikes = new HashSet<long>();
    EnemyBrain _mob;
    float _nextMobScanAt;
    Vector3 _lateralDir;
    float _lateralFrom, _lateralUntil;
    public int MobKills { get; private set; }
    public int MobHitsTaken { get; private set; }
    public int MaxMeleeTokens { get; private set; }
    public int MobParryPresses { get; private set; }
    public int MobDodgePresses { get; private set; }
    public float MobFightStart { get; private set; } = -1f;
    public float MobFightEnd { get; private set; } = -1f;

    // 반복 16: 봉쇄 프로토콜 터렛·드론(OH_Lockdown_*)은 벽 높이라 칼이 닿지 않는다 → 노드를 부수면 같이 꺼지므로 표적에서 뺀다
    static bool IsMobTargetable(EnemyBrain b) => b != null && !b.IsDead && b.isActiveAndEnabled && !b.name.StartsWith("OH_Lockdown_");

    void UpdateMobTarget()
    {
        if (!fightMobs || _player == null)
        {
            _mob = null;
            return;
        }

        if (_mob != null && !IsMobTargetable(_mob))
            _mob = null;
        if (Time.unscaledTime < _nextMobScanAt && _mob != null)
            return;

        _nextMobScanAt = Time.unscaledTime + 0.3f;
        EnemyBrain best = null;
        float bestScore = float.PositiveInfinity;
        IReadOnlyList<EnemyBrain> all = EnemyBrain.Active;
        for (int i = 0; i < all.Count; i++)
        {
            EnemyBrain b = all[i];
            if (!IsMobTargetable(b))
                continue;
            if (_hookedMobs.Add(b)) { b.CueIssued += HandleMobCue; b.Died += HandleMobDied; }
            Vector3 d = b.transform.position - _player.position;
            if (Mathf.Abs(d.y) > 6f || d.magnitude > mobEngageRange)
                continue;
            // Melee mobs first (they gate the flow); a turret only when nothing else is left.
            float score = d.magnitude + (b is SentryTurretBrain ? 25f : 0f);
            if (score < bestScore) { bestScore = score; best = b; }
        }

        if (best != _mob && best != null)
        {
            Note($"target mob {best.name} ({best.GetType().Name}) dist={Vector3.Distance(best.transform.position, _player.position):F1}");
            if (MobFightStart < 0f) MobFightStart = Time.unscaledTime - _startTime;
        }
        _mob = best;
    }

    void HandleMobDied(EnemyBrain b)
    {
        MobKills++;
        MobFightEnd = Time.unscaledTime - _startTime;
        Note($"MOB DEAD {b.name} hitsRecv={b.HitsReceived} landed={b.HitsLanded} strikes={b.StrikesDelivered} parried={b.ParriedCount} staggers={b.StaggerCount} cues={b.CueCount} maxCueErr={b.MaxCueHonestyError:F3}");
        Capture("mobdead_" + b.GetType().Name);
    }

    // Cued attacks (turret lock, android thrust/3rd hit, enforcer bash): turret bursts only come through here.
    void HandleMobCue(EnemyBrain b, EnemyCueType type, float lead)
    {
        // 9 m: 벽 터렛(높이 ~6.8 m)의 큐도 받는다 (층이 다른 적은 여전히 무시)
        if (b == null || _player == null || Mathf.Abs(b.transform.position.y - _player.position.y) > 9f)
            return;
        _cues++;
        _lastThreatTime = Time.time;
        Note($"mob cue {b.name} {type} in {lead:F2}s err={b.LastCueHonestyError:+0.000;-0.000}");
        Capture("mobcue_" + b.GetType().Name + "_" + type);
        if (b is SentryTurretBrain)
        {
            bool react = _rng.NextDouble() < defenseSkill;
            if (!react) return;
            // Step out of the locked line: dodge sideways relative to the turret.
            Vector3 toMe = _player.position - b.transform.position; toMe.y = 0f;
            Vector3 lateral = Vector3.Cross(Vector3.up, toMe.sqrMagnitude > 0.01f ? toMe.normalized : Vector3.forward);
            if (_rng.NextDouble() < 0.5) lateral = -lateral;
            float at = Time.time + Mathf.Max(0f, lead - dodgeLead);
            _lateralDir = lateral; _lateralFrom = at - 0.12f; _lateralUntil = at + 0.35f;
            Schedule(lead - dodgeLead, true);
            MobDodgePresses++;
        }
    }

    Transform _lastLockTarget;
    float _lockOnDeadSince = -1f;
    public int LockOnDeadMobFrames { get; private set; }
    public int LockSwitches { get; private set; }

    // Lock-on QA: log target changes and how long the lock stays on a dead mob.
    void TrackLockTarget()
    {
        if (_lockOn == null)
            return;
        Transform current = _lockOn.IsLockedOn() ? _lockOn.CurrentTarget : null;
        if (current != _lastLockTarget)
        {
            LockSwitches++;
            Note($"lock -> {(current != null ? current.root.name : "none")}");
            _lastLockTarget = current;
        }
        EnemyBrain lockedBrain = current != null ? current.GetComponentInParent<EnemyBrain>() : null;
        if (lockedBrain != null && lockedBrain.IsDead)
        {
            LockOnDeadMobFrames++;
            if (_lockOnDeadSince < 0f) _lockOnDeadSince = Time.unscaledTime;
            else if (Time.unscaledTime - _lockOnDeadSince > 0.6f) { Note($"LOCK STUCK on dead {lockedBrain.name}"); _lockOnDeadSince = Time.unscaledTime + 100f; }
        }
        else
            _lockOnDeadSince = -1f;
    }

    // A player stops pressing attack when a guard who holds an attack slot is closing in (otherwise the swing
    // animation blocks the guard/parry input and the incoming hit lands).
    bool MeleeMobAboutToSwing()
    {
        IReadOnlyList<EnemyBrain> all = EnemyBrain.Active;
        for (int i = 0; i < all.Count; i++)
        {
            EnemyBrain b = all[i];
            if (!IsMobTargetable(b) || b is SentryTurretBrain)
                continue;
            if (b.State != EnemyBrainState.Approach && b.State != EnemyBrainState.Attack)
                continue;
            if (!EnemyAttackTokenDirector.Holds(_player, b, EnemyTokenKind.Melee))
                continue;
            if (Vector3.Distance(b.transform.position, _player.position) < 3.6f)
                return true;
        }
        return false;
    }

    // Every melee strike (cued or not) has a known hit time: a player reads the wind-up animation.
    void AnswerMobStrikes()
    {
        IReadOnlyList<EnemyBrain> all = EnemyBrain.Active;
        for (int i = 0; i < all.Count; i++)
        {
            EnemyBrain b = all[i];
            if (!IsMobTargetable(b) || b is SentryTurretBrain)
                continue;
            float hitAt = b.PendingHitAt;
            if (hitAt < 0f)
                continue;
            EnemyStrike s = b.CurrentStrike;
            if (s == null)
                continue;
            long key = ((long)b.GetInstanceID() << 32) ^ (uint)b.AttackSequenceId;
            if (_answeredStrikes.Contains(key))
                continue;
            float dist = Vector3.Distance(b.transform.position, _player.position);
            if (dist > s.range + s.lungeDistance + 1.5f)
                continue;
            _answeredStrikes.Add(key);
            if (_answeredStrikes.Count > 512) _answeredStrikes.Clear();
            _lastThreatTime = Time.time;
            // Turn to face the swing (parry/guard only work from the front): retarget + relock right away.
            if (b != _mob && dist < 4.5f)
            {
                _mob = b;
                _nextMobScanAt = Time.unscaledTime + 1.2f;
                _lockMismatchSince = Time.unscaledTime - 1f;
            }
            bool dodge = s.cueType == EnemyCueType.DodgeOnly || s.cueType == EnemyCueType.Unblockable;
            // Parry/guard only work from the front: a swing from the side or back is dodged instead.
            Vector3 toAttacker = b.transform.position - _player.position; toAttacker.y = 0f;
            if (!dodge && toAttacker.sqrMagnitude > 0.01f && Vector3.Angle(_player.forward, toAttacker) > 65f)
                dodge = true;
            bool react = _rng.NextDouble() < defenseSkill;
            float remaining = hitAt - Time.time;
            // Screenshot sheet: wind-up (now), mid wind-up and the hit frame, once per mob type + strike.
            string sheet = b.GetType().Name.Replace("Brain", "") + "_" + s.name;
            Capture("mobwindup_" + sheet);
            if (_pendingCaptureKey == null && !_captured.Contains("mobhit_" + sheet))
            {
                _pendingCaptureKey = "mobhit_" + sheet;
                _pendingCaptureAt = hitAt + 0.02f;
            }
            Note($"mob strike {b.name} {s.name} ({s.cueType}) in {remaining:F2}s react={react}");
            if (!react)
                continue;
            if (dodge)
            {
                Vector3 toMe = _player.position - b.transform.position; toMe.y = 0f;
                Vector3 lateral = Vector3.Cross(Vector3.up, toMe.sqrMagnitude > 0.01f ? toMe.normalized : Vector3.forward);
                if (_rng.NextDouble() < 0.5) lateral = -lateral;
                float at = Time.time + Mathf.Max(0f, remaining - dodgeLead);
                _lateralDir = lateral; _lateralFrom = at - 0.12f; _lateralUntil = at + 0.35f;
                Schedule(remaining - dodgeLead, true, true);
                MobDodgePresses++;
            }
            else
            {
                Schedule(remaining - parryLead, false, true);
                MobParryPresses++;
            }
        }
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
            if (d.transform.root.name.StartsWith("OH_Lockdown_"))
                continue; // 반복 16: 봉쇄 드론은 노드를 끊으면 같이 꺼진다 (사격엔 반응만)
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
        // 탄은 총구(전방 0.65 m)에서 출발해 몸통 표면(~0.45 m)에 닿고, 봇은 접근 중이라 거리가 줄어든다 → 사람처럼 "탄이 보이는" 쪽으로 보정
        float dist = Mathf.Max(0f, Vector3.Distance(drone.transform.position, _player.position) - 1.1f);
        float travel = dist / Mathf.Max(1f, drone.projectileSpeed) * 0.85f;
        float t = drone.AttackTelegraphLeadTime + travel;
        Note($"drone shot in {t:F2}s react={react}");
        if (react)
            _scheduled.Add((Time.time + Mathf.Max(0f, t - dodgeLead - 0.08f), true));
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
    void Schedule(float delay, bool dodge, bool keepEarliest = false)
    {
        float jitter = timingJitter > 0f ? (float)(_rng.NextDouble() * 2.0 - 1.0) * timingJitter : 0f;
        float at = Time.time + Mathf.Max(reactionTime, delay + jitter);
        for (int i = _scheduled.Count - 1; i >= 0; i--)
        {
            if (_scheduled[i].dodge != dodge || Mathf.Abs(_scheduled[i].time - at) >= 0.35f)
                continue;
            // Mobs: two attackers swinging close together -> one press, timed for the earlier hit.
            if (keepEarliest)
                at = Mathf.Min(at, _scheduled[i].time);
            _scheduled.RemoveAt(i);
        }
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
            if (_lastHp >= 0 && _playerHealth.CurrentHP < _lastHp && _mob != null) MobHitsTaken++;
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
        UpdateMobTarget();
        if (_player != null)
        {
            int melee = EnemyAttackTokenDirector.ActiveCount(_player, EnemyTokenKind.Melee);
            if (melee > MaxMeleeTokens) { MaxMeleeTokens = melee; Note($"melee tokens active={melee}"); }
        }
        Transform target = null;
        if (_mob != null && (_drone == null || Vector3.Distance(_mob.transform.position, _player.position) <= Vector3.Distance(_drone.transform.position, _player.position) + 4f))
            target = _mob.transform;
        else if (_drone != null)
            target = _drone.transform;
        else if (_boss != null && !(followLobbyRoute && UpperFloorActive()))
            target = _boss.transform;
        // 반복 16: 봉쇄 프로토콜 중엔 전원 노드가 표적 (보스는 보호막)
        Transform nodeTarget = ResolveOverhaulNodeTarget();
        if (nodeTarget != null && _mob == null)
            target = nodeTarget;
        bool mobTarget = _mob != null && target == _mob.transform;
        bool nonBossTarget = _drone != null || mobTarget || nodeTarget != null && target == nodeTarget;

        if (_playerHealth != null && !_playerHealth.IsDead)
        {
            if (followTutorialRoute && TutorialStepActions())
                return;
            AnswerMobStrikes();
            if (target == null && followTutorialRoute && FollowTutorialRoute())
            {
                RunScheduledDefense();
                return;
            }
            if (target == null && followLobbyRoute && FollowLobbyRoute())
            {
                RunScheduledDefense();
                return;
            }
        }

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

        if (!nonBossTarget && _bossHealth != null && _bossHealth.IsDead)
        {
            if (BossDeadAt < 0f) { BossDeadAt = Time.unscaledTime - _startTime; Note("BOSS DEAD"); }
            Capture("boss_dead");
            return;
        }

        UpdateLockOn(target);
        TrackLockTarget();
        RunScheduledDefense();

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

        if (!nonBossTarget && _ultimate != null && _ultimate.IsGaugeReady && _ultimate.TryActivate())
        {
            if (_profileUltState == 1)
                _profileUltStopFrame = Time.frameCount + 90;
            _ultimates++;
            Note("ultimate activate");
            return;
        }

        float dist = Vector3.Distance(_player.position, target.position);
        // 반복 17: 전원 노드는 바닥 위 1.25 m 콘솔 — 3D 거리로는 콜라이더에 붙어도 사거리 밖이라 공격을 안 하고 맴돌았다
        if (target == _nodeTarget)
        {
            Vector3 flat = target.position - _player.position;
            flat.y = 0f;
            dist = flat.magnitude;
        }
        bool defensePending = _scheduled.Count > 0;
        bool inHit = _combat != null && _combat.IsInHit;

        if (healAtHp > 0 && _consumables != null && _consumables.CurrentAmpoule > 0 && _playerHealth.CurrentHP <= healAtHp
            && !defensePending && !inHit && Time.time >= _nextHealAt
            && !(mobTarget && (Time.time - _lastThreatTime < 1.0f || MeleeMobAboutToSwing())))
        {
            if (_consumables.TryUseAmpoule())
            {
                Heals++;
                Note($"heal start hp={_playerHealth.CurrentHP} left={_consumables.CurrentAmpoule}");
            }
            _nextHealAt = Time.time + 1.5f;
        }

        float range = mobTarget ? mobAttackRange : nodeTarget != null && target == nodeTarget ? 1.7f : attackRange;
        if (!nonBossTarget && OverhaulEvade())
            return;
        UpdateApproach(target, dist, defensePending, range);
        // Human-like: hold attacks while a boss string is running (unless it opened a punish window).
        bool bossBusy = !nonBossTarget && _boss != null && !inBreak && !_boss.IsPunishWindowActive && !string.IsNullOrEmpty(_boss.CurrentPatternName);
        bool mobOpen = mobTarget && (_mob.InRiposteWindow || _mob.State == EnemyBrainState.Recover);
        bool mobBusy = mobTarget && (_mob.PendingHitAt > 0f || MeleeMobAboutToSwing());
        bool nodeTargeted = nodeTarget != null && target == nodeTarget; // 반복 16: 노드는 보스 위협 직후에도 친다 (방어 예약만 기다림)
        // 반복 19: 노드를 칠 때도 '가까이서 진행 중인 보스 연타' 사이에는 휘두르지 않는다 (사람은 연타 중간에 노드를 치지 않음).
        //   예전엔 노드 표적이면 위협을 통째로 무시해 연타 도중 공격 → 다음 타격 큐의 패리가 공격 모션에 먹혀 봉쇄 중 피격의 절반이 이것이었다.
        //   (CurrentPatternName 기준은 너무 넓어 QA 에서 봉쇄가 안 끝났다 → 실제 공격 상태 + 4.5 m 이내만 본다)
        bool bossStringNear = nodeTargeted && _boss != null && !inBreak && !_boss.IsPunishWindowActive && _boss.IsAttackStateActive
            && (_boss.transform.position - _player.position).sqrMagnitude < 20.25f;
        bool threatened = _drone == null && !mobOpen && (nodeTargeted ? bossStringNear : (bossBusy || mobBusy || Time.time - _lastThreatTime < holdAttackAfterThreat));
        if (dist <= range && !defensePending && !threatened && Time.time >= _nextAttackAt)
        {
            _nextAttackAt = Time.time + attackInterval;
            if (inBreak && _break.IsFinisherAvailable)
            {
                _finishers++;
                Note("finisher attempt");
            }

            // Shield up in front -> a player swaps to heavy (guard break) or flanks; light hits only bounce.
            double heavyChance = mobTarget && _mob is ShieldEnforcerBrain se && se.ShieldUp ? 0.7 : 0.2;
            bool heavy = _rng.NextDouble() < heavyChance;
            if (heavy) _input.RecordAttackHeavyPress(); else _input.RecordAttackLightPress();
            _attacks++;
            if (_attacks == 6)
                Capture("combo");
        }
    }

    Vector3 _lastProgressPos;
    float _progressCheckAt, _detourUntil, _detourSign = 1f;

    void RunScheduledDefense()
    {
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
    }

    // ---- Infiltration tutorial route (Tutorial.unity, iteration 13) ----
    string _tutStep;
    float _tutStepAt, _nextTutActionAt;
    bool _tutGuardHeld;
    float _lastUltDiagAt;

    /// <summary>Per-step taught action, like a player following the prompt. Returns true when it used the frame.</summary>
    bool TutorialStepActions()
    {
        InfiltrationTutorialDirector d = InfiltrationTutorialDirector.Active;
        if (d == null || _player == null || _input == null)
            return false;
        string id = d.CurrentStepId;
        if (id != _tutStep)
        {
            if (_tutGuardHeld) { PlayerGuardController.QaReleaseGuard(); _tutGuardHeld = false; }
            _tutStep = id;
            _tutStepAt = Time.unscaledTime;
            Note($"tutorial step -> {id}");
            Capture("tut_" + id);
        }

        if (id == "guard")
        {
            // 가드 레슨: 막기만 한다 (패링/공격 안 함) — E 를 누르고 버티기
            _scheduled.Clear();
            if (!_tutGuardHeld) { PlayerGuardController.QaPressGuard(); _tutGuardHeld = true; Note("tutorial guard hold"); }
            if (_mob != null) UpdateLockOn(_mob.transform);
            PlayerMoveController.QaMoveInputOverride = null;
            return true;
        }

        if (id == "pd" || id == "red")
        {
            // 회피 레슨: 터렛을 때리지 않고(경직으로 사격을 끊지 않게) 목표 지점에서 큐에만 반응
            if (d.CurrentGoal != null && (d.CurrentGoal.position - _player.position).sqrMagnitude > 1.2f)
            {
                Vector3 to = d.CurrentGoal.position - _player.position; to.y = 0f;
                UpdateApproach(d.CurrentGoal, to.magnitude, false, 0f);
            }
            else
                PlayerMoveController.QaMoveInputOverride = null;
            RunScheduledDefense();
            return true;
        }

        if (id == "ultimate" && _ultimate != null && _ultimate.IsGaugeReady && !_ultimate.IsCinematic)
        {
            // 사람처럼: 게이지가 차 있으면 공격을 멈추고 궁극기 (공격 중엔 "Player is attacking" 으로 거부된다)
            PlayerMoveController.QaMoveInputOverride = null;
            if (Time.unscaledTime >= _nextTutActionAt)
            {
                _nextTutActionAt = Time.unscaledTime + 0.3f;
                if (_ultimate.TryActivate()) { _ultimates++; Note("tutorial ultimate activate"); }
                else if (Time.unscaledTime - _lastUltDiagAt > 2f)
                {
                    _lastUltDiagAt = Time.unscaledTime;
                    _ultimate.CanActivate(out string why);
                    Note("tutorial ultimate blocked: " + why);
                }
            }
            RunScheduledDefense(); // 막히는 동안에도 큐에는 반응
            return true;
        }
        // 반복 16: 잠입 암살 레슨 — 경계 전 경비의 등 뒤로 돌아가 공격 입력 (프롬프트가 '암살'일 때만)
        if (id == "takedown" && d.TakedownTarget != null && !d.TakedownTarget.IsDead && d.TakedownTarget.CanBeAmbushed)
        {
            Transform m = d.TakedownTarget.transform;
            Vector3 behind = m.position - m.forward * 1.3f;
            Vector3 to = behind - _player.position;
            to.y = 0f;
            Camera cam = Camera.main;
            if (ExecutionDirector.TryGetPrompt(_player, out ExecutionKind pk, out _) && pk == ExecutionKind.Takedown)
            {
                PlayerMoveController.QaMoveInputOverride = null;
                if (Time.unscaledTime >= _nextTutActionAt)
                {
                    _input.RecordAttackLightPress(); _attacks++;
                    _nextTutActionAt = Time.unscaledTime + 0.6f;
                    Note("tutorial takedown press");
                    Capture("takedown");
                }
            }
            else if (cam != null && to.sqrMagnitude > 0.04f)
            {
                // 측면으로 크게 돌아 들어간다 (정면 시야 콘을 피함)
                Vector3 side = Vector3.Cross(Vector3.up, m.forward);
                Vector3 toMob = m.position - _player.position; toMob.y = 0f;
                bool inFront = Vector3.Dot(m.forward, -toMob.normalized) > 0.2f;
                Vector3 goal = inFront ? m.position + side * (Vector3.Dot(side, -toMob) >= 0f ? 2.6f : -2.6f) : behind;
                Vector3 dir = goal - _player.position; dir.y = 0f; dir.Normalize();
                Vector3 f = cam.transform.forward; f.y = 0f; f.Normalize();
                Vector3 r = cam.transform.right; r.y = 0f; r.Normalize();
                PlayerMoveController.QaMoveInputOverride = new Vector2(Vector3.Dot(dir, r), Vector3.Dot(dir, f)) * 0.6f;
            }
            return true;
        }
        if (Time.unscaledTime < _nextTutActionAt)
            return false;
        switch (id)
        {
            case "look":
                if (SoulsCameraRig.Active != null && d.LookTarget != null)
                {
                    Vector3 to = d.LookTarget.position - _player.position;
                    SoulsCameraRig.Active.SnapToYaw(Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg, -8f);
                    Note("tutorial look: camera toward emblem");
                }
                _nextTutActionAt = Time.unscaledTime + 1.5f;
                break;
            case "dodge":
                _input.RecordDodgePress(); _dodges++;
                _nextTutActionAt = Time.unscaledTime + 0.9f;
                break;
            case "scan":
                if (d.Scanner != null)
                {
                    float dz = d.Scanner.transform.position.z - _player.position.z;
                    if (dz > 0.5f && dz < 1.8f)
                    {
                        _input.RecordDodgePress(); _dodges++;
                        _nextTutActionAt = Time.unscaledTime + 1.2f;
                        Note("tutorial scan: dodge through curtain");
                    }
                }
                break;
            case "heal":
                if (_consumables != null && _consumables.TryUseAmpoule()) { Heals++; Note("tutorial heal"); }
                _nextTutActionAt = Time.unscaledTime + 1.5f;
                break;
            case "ultimate":
                if (_ultimate != null && _ultimate.IsGaugeReady && !_ultimate.IsCinematic && _ultimate.TryActivate()) { _ultimates++; Note("tutorial ultimate activate"); }
                else if (Time.unscaledTime - _tutStepAt > 2f && Time.unscaledTime - _lastUltDiagAt > 3f)
                {
                    _lastUltDiagAt = Time.unscaledTime;
                    string why = "no controller";
                    if (_ultimate != null) _ultimate.CanActivate(out why);
                    Note($"tutorial ultimate blocked: ctrl={(_ultimate != null)} gauge={(_ultimate != null ? _ultimate.Gauge : -1f):F0} cine={(_ultimate != null && _ultimate.IsCinematic)} reason={why}");
                }
                _nextTutActionAt = Time.unscaledTime + 0.5f;
                break;
            case "exit":
                SceneLoadInteractable exit = d.ExitInteractable;
                if (exit != null && Vector3.Distance(exit.transform.position, _player.position) < 3.4f && Time.unscaledTime - _tutStepAt > 0.8f)
                {
                    Note("tutorial exit TryInteract -> " + exit.TryInteract(_player.gameObject));
                    _nextTutActionAt = Time.unscaledTime + 2f;
                }
                break;
        }
        return false;
    }

    /// <summary>No enemy around: walk to the current tutorial goal (route marker).</summary>
    bool FollowTutorialRoute()
    {
        InfiltrationTutorialDirector d = InfiltrationTutorialDirector.Active;
        if (d == null)
            return false;
        Transform goal = d.CurrentGoal;
        if (d.CurrentStepId == "exit" && d.ExitInteractable != null)
            goal = d.ExitInteractable.transform;
        if (goal == null)
        {
            PlayerMoveController.QaMoveInputOverride = null;
            return true;
        }
        Vector3 to = goal.position - _player.position;
        to.y = 0f;
        if (to.magnitude < 0.9f)
        {
            PlayerMoveController.QaMoveInputOverride = null;
            return true;
        }
        UpdateApproach(goal, to.magnitude, false, 0f);
        return true;
    }

    // ---- Level_Facility upper-floor route (full-run mode) ----
    LobbyManager _lobby;
    string _routeLeg;

    bool UpperFloorActive()
    {
        if (_lobby == null)
            _lobby = LobbyManager.Instance != null ? LobbyManager.Instance : FindObjectOfType<LobbyManager>();
        return _lobby != null && _lobby.ContinuesInSameScene && !_lobby.HasHandedOffToLowerFloor;
    }

    /// <summary>No enemy around: walk the next leg of the upper floor. Returns false when there is nothing to walk.</summary>
    bool FollowLobbyRoute()
    {
        if (!UpperFloorActive())
            return false;

        Vector3? goal = null;
        string leg = null;
        switch (_lobby.CurrentPhase)
        {
            case LobbyManager.LobbyFlowPhase.Arrival:
            case LobbyManager.LobbyFlowPhase.Combat:
                if (_lobby.CombatStartGate != null)
                {
                    goal = _lobby.CombatStartGate.position + _lobby.CombatStartGate.forward * 4f;
                    leg = "combat gate";
                }
                break;
            case LobbyManager.LobbyFlowPhase.Route:
                if (_lobby.triggerApproach != null) { goal = _lobby.triggerApproach.position; leg = "elevator approach"; }
                break;
            case LobbyManager.LobbyFlowPhase.Board:
                if (_lobby.triggerBoard != null) { goal = _lobby.triggerBoard.position; leg = "board"; }
                break;
        }

        if (leg != _routeLeg) { _routeLeg = leg; Note($"route leg -> {leg ?? "wait"} (phase {_lobby.CurrentPhase})"); }
        if (!goal.HasValue)
        {
            PlayerMoveController.QaMoveInputOverride = null;
            return true;
        }

        Vector3 to = goal.Value - _player.position;
        to.y = 0f;
        if (to.magnitude < 0.6f)
        {
            PlayerMoveController.QaMoveInputOverride = null;
            return true;
        }
        if (_routeGoal == null) { _routeGoal = new GameObject("__QaRouteGoal").transform; _routeGoal.SetParent(transform, false); }
        _routeGoal.position = goal.Value;
        UpdateApproach(_routeGoal, to.magnitude, false, 0f);
        return true;
    }
    Transform _routeGoal;

    void UpdateApproach(Transform target, float dist, bool defensePending, float range)
    {
        if (Time.time >= _lateralFrom && Time.time < _lateralUntil && Camera.main != null)
        {
            Vector3 f = Camera.main.transform.forward; f.y = 0f; f.Normalize();
            Vector3 r = Camera.main.transform.right; r.y = 0f; r.Normalize();
            PlayerMoveController.QaMoveInputOverride = new Vector2(Vector3.Dot(_lateralDir, r), Vector3.Dot(_lateralDir, f));
            return;
        }

        if (!approachBoss || defensePending || dist <= range * 0.85f)
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
        // 반복 19: 공격·피격 모션 중엔 원래 못 움직인다 — 이걸 '막힘'으로 세면 노드 앞에서 옆으로 빠져 사거리를 벗어났다
        if (_combat != null && (_combat.IsAttacking || _combat.IsInHit))
        {
            _lastProgressPos = _player.position;
            _progressCheckAt = Time.time + 0.8f;
        }
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
            {
                // 반복 19: NavMesh 는 보스·적 몸을 모른다 — 경로 방향을 몸이 막으면 막힘 판정(0.8 s)을 기다리지 않고
                //   한 방향으로 정해 0.8 s 돌아간다 (매 프레임 좌우를 다시 고르면 보스 앞에서 제자리 왕복했다)
                desired = toCorner.normalized;
                if (Time.time >= _detourUntil && IsBlockedByActor(desired, target))
                    _detourUntil = Time.time + 0.8f;
                return desired;
            }
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

    bool IsBlockedByActor(Vector3 direction, Transform target)
    {
        Vector3 origin = _player.position + Vector3.up * 0.9f;
        if (!Physics.SphereCast(origin, 0.35f, direction, out RaycastHit hit, 1.6f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            return false;
        Transform t = hit.collider.transform;
        if (t.IsChildOf(_player) || (target != null && t.IsChildOf(target.root)))
            return false;
        return hit.collider.GetComponentInParent<BossController>() != null || hit.collider.GetComponentInParent<EnemyBrain>() != null;
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
