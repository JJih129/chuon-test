using System;
using System.Collections.Generic;
using UnityEngine;

// 침투 튜토리얼 조건 검사기 — 기존 TutorialConditionChecker 계약(Begin/Report/Complete) 그대로, 허수아비 대신 실제 잡몹(EnemyBrain)을 본다.

/// <summary>회피 입력 n회 (가르치기 단계).</summary>
[DisallowMultipleComponent]
public sealed class InfDodgeCountChecker : TutorialConditionChecker
{
    [SerializeField] TutorialPlayerRuntimeBridge playerBridge;
    [SerializeField, Min(1)] int required = 2;
    int _count;

    public void ConfigureRuntime(TutorialPlayerRuntimeBridge bridge, int count)
    {
        playerBridge = bridge;
        required = Mathf.Max(1, count);
    }

    protected override void OnBeginChecking(TutorialStepDefinition step)
    {
        _count = 0;
        if (playerBridge == null) { Complete(); return; }
        playerBridge.DodgeStarted += HandleDodge;
        ReportProgress(Text());
    }

    protected override void OnEndChecking()
    {
        if (playerBridge != null)
            playerBridge.DodgeStarted -= HandleDodge;
    }

    void HandleDodge()
    {
        _count = Mathf.Min(required, _count + 1);
        ReportProgress(Text());
        if (_count >= required)
            Complete(Text());
    }

    string Text() => $"회피 {_count}/{required}";
}

/// <summary>관찰 중인 잡몹에 약공/강공 적중 수 (+선택: 처치까지). 대상은 런타임에 교체 가능(재생성).</summary>
[DisallowMultipleComponent]
public sealed class InfMobHitChecker : TutorialConditionChecker
{
    [SerializeField] TutorialPlayerRuntimeBridge playerBridge;
    [SerializeField, Min(0)] int requiredLight = 3;
    [SerializeField, Min(0)] int requiredHeavy = 1;
    [SerializeField] bool requireDefeat = true;

    readonly List<EnemyBrain> _observed = new List<EnemyBrain>();
    readonly Dictionary<EnemyBrain, int> _lastHits = new Dictionary<EnemyBrain, int>();
    int _light, _heavy;
    bool _anyDefeated;

    public void ConfigureRuntime(TutorialPlayerRuntimeBridge bridge, int light, int heavy, bool defeat)
    {
        playerBridge = bridge;
        requiredLight = light;
        requiredHeavy = heavy;
        requireDefeat = defeat;
    }

    public void Observe(EnemyBrain mob)
    {
        if (mob == null || _observed.Contains(mob))
            return;
        _observed.Add(mob);
        _lastHits[mob] = mob.HitsReceived;
    }

    public bool HitsSatisfied => _light >= requiredLight && _heavy >= requiredHeavy;

    protected override bool ShouldEnableTick() => true;

    protected override void OnBeginChecking(TutorialStepDefinition step)
    {
        _light = _heavy = 0;
        _anyDefeated = false;
        for (int i = 0; i < _observed.Count; i++)
            if (_observed[i] != null) _lastHits[_observed[i]] = _observed[i].HitsReceived;
        ReportProgress(Text());
    }

    protected override void OnEndChecking()
    {
        _observed.Clear();
        _lastHits.Clear();
    }

    void Update()
    {
        if (!IsRunning)
            return;
        bool changed = false;
        for (int i = _observed.Count - 1; i >= 0; i--)
        {
            EnemyBrain b = _observed[i];
            if (b == null) { _observed.RemoveAt(i); continue; }
            int now = b.HitsReceived;
            int last = _lastHits.TryGetValue(b, out int l) ? l : now;
            if (now > last)
            {
                int n = now - last;
                TutorialAttackKind kind = TutorialAttackKind.Unknown;
                if (playerBridge != null)
                    playerBridge.ResolveAttackInfo(playerBridge.PlayerTransform, out kind, out _);
                if (kind == TutorialAttackKind.Heavy) _heavy += n; else _light += n;
                changed = true;
            }
            _lastHits[b] = now;
            if (b.IsDead) _anyDefeated = true;
        }

        if (changed)
            ReportProgress(Text());
        if (HitsSatisfied && (!requireDefeat || _anyDefeated))
            Complete(Text());
    }

    string Text()
    {
        string s = $"약공 {Mathf.Min(_light, requiredLight)}/{requiredLight}  강공 {Mathf.Min(_heavy, requiredHeavy)}/{requiredHeavy}";
        if (requireDefeat && HitsSatisfied)
            s += "  · 처치";
        return s;
    }
}

/// <summary>지정 잡몹 전부 처치 (게이팅). 목록은 런타임에 추가.</summary>
[DisallowMultipleComponent]
public sealed class InfMobsDefeatedChecker : TutorialConditionChecker
{
    [SerializeField] string label = "경비";
    readonly List<EnemyBrain> _mobs = new List<EnemyBrain>();
    int _lastDead = -1;

    public void ConfigureRuntime(string progressLabel) => label = progressLabel;

    public void Observe(EnemyBrain mob)
    {
        if (mob != null && !_mobs.Contains(mob))
            _mobs.Add(mob);
    }

    protected override bool ShouldEnableTick() => true;

    protected override void OnBeginChecking(TutorialStepDefinition step)
    {
        _lastDead = -1;
    }

    protected override void OnEndChecking() => _mobs.Clear();

    void Update()
    {
        if (!IsRunning)
            return;
        int total = 0, dead = 0;
        for (int i = 0; i < _mobs.Count; i++)
        {
            if (_mobs[i] == null) { total++; dead++; continue; }
            total++;
            if (_mobs[i].IsDead || !_mobs[i].gameObject.activeInHierarchy) dead++;
        }
        if (dead != _lastDead)
        {
            _lastDead = dead;
            ReportProgress($"{label} {dead}/{Mathf.Max(total, 1)}");
        }
        if (total > 0 && dead >= total)
            Complete($"{label} {dead}/{total}");
        else if (total == 0)
            Complete();
    }
}

/// <summary>빨강(회피 전용) 큐를 받은 공격을 피해 없이 넘긴 횟수. 큐 → 판정 + 연사 시간 동안 플레이어 피격이 없으면 성공.</summary>
[DisallowMultipleComponent]
public sealed class InfRedCueSurviveChecker : TutorialConditionChecker
{
    [SerializeField] TutorialPlayerRuntimeBridge playerBridge;
    [SerializeField, Min(1)] int required = 2;
    [SerializeField, Min(0f)] float burstWindow = 0.65f;

    readonly List<EnemyBrain> _observed = new List<EnemyBrain>();
    readonly List<(float resolveAt, int damageCount)> _pending = new List<(float, int)>();
    int _survived;

    public int Failures { get; private set; }
    public event Action<bool> CueResolved; // true = 회피 성공

    public void ConfigureRuntime(TutorialPlayerRuntimeBridge bridge, int count)
    {
        playerBridge = bridge;
        required = Mathf.Max(1, count);
    }

    public void Observe(EnemyBrain mob)
    {
        if (mob == null || _observed.Contains(mob))
            return;
        _observed.Add(mob);
        if (IsRunning)
            mob.CueIssued += HandleCue;
    }

    protected override bool ShouldEnableTick() => true;

    protected override void OnBeginChecking(TutorialStepDefinition step)
    {
        _survived = 0;
        Failures = 0;
        _pending.Clear();
        foreach (EnemyBrain b in _observed)
            if (b != null) b.CueIssued += HandleCue;
        ReportProgress(Text());
    }

    protected override void OnEndChecking()
    {
        foreach (EnemyBrain b in _observed)
            if (b != null) b.CueIssued -= HandleCue;
        _observed.Clear();
        _pending.Clear();
    }

    void HandleCue(EnemyBrain b, EnemyCueType type, float lead)
    {
        if (type != EnemyCueType.DodgeOnly || playerBridge == null)
            return;
        _pending.Add((Time.time + lead + burstWindow, playerBridge.PlayerDamageCount));
    }

    void Update()
    {
        if (!IsRunning || playerBridge == null)
            return;
        for (int i = _pending.Count - 1; i >= 0; i--)
        {
            if (Time.time < _pending[i].resolveAt)
                continue;
            bool ok = playerBridge.PlayerDamageCount == _pending[i].damageCount;
            _pending.RemoveAt(i);
            if (ok) _survived++; else Failures++;
            CueResolved?.Invoke(ok);
            ReportProgress(Text());
            if (_survived >= required)
            {
                Complete(Text());
                return;
            }
        }
    }

    string Text() => $"빨간 신호 회피 {Mathf.Min(_survived, required)}/{required}";
}

/// <summary>방패 집행관 가드 붕괴 n회.</summary>
[DisallowMultipleComponent]
public sealed class InfGuardBreakChecker : TutorialConditionChecker
{
    [SerializeField, Min(1)] int required = 1;
    readonly List<ShieldEnforcerBrain> _observed = new List<ShieldEnforcerBrain>();
    readonly Dictionary<ShieldEnforcerBrain, int> _base = new Dictionary<ShieldEnforcerBrain, int>();
    int _count = -1;

    public int BlockedHits { get; private set; }

    public void ConfigureRuntime(int count) => required = Mathf.Max(1, count);

    public void Observe(ShieldEnforcerBrain mob)
    {
        if (mob == null || _observed.Contains(mob))
            return;
        _observed.Add(mob);
        _base[mob] = mob.GuardBreakCount;
    }

    protected override bool ShouldEnableTick() => true;

    protected override void OnBeginChecking(TutorialStepDefinition step)
    {
        _count = -1;
        foreach (ShieldEnforcerBrain b in _observed)
            if (b != null) _base[b] = b.GuardBreakCount;
    }

    protected override void OnEndChecking()
    {
        _observed.Clear();
        _base.Clear();
    }

    void Update()
    {
        if (!IsRunning)
            return;
        int n = 0, blocked = 0;
        foreach (ShieldEnforcerBrain b in _observed)
        {
            if (b == null) continue;
            n += b.GuardBreakCount - (_base.TryGetValue(b, out int v) ? v : 0);
            blocked += b.BlockCount;
        }
        BlockedHits = blocked;
        if (n != _count)
        {
            _count = n;
            ReportProgress($"가드 붕괴 {Mathf.Min(n, required)}/{required}");
        }
        if (n >= required)
            Complete($"가드 붕괴 {required}/{required}");
    }
}
