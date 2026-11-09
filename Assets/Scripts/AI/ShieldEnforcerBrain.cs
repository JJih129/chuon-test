using UnityEngine;

// 츠카사 방패 집행관 (라이엇 실드 + 스턴 배턴)
//  방패(전방 140°)가 약공격을 막는다: 불꽃 + 금속음, 피해 0, 방패 안정도 감소 (0 → 가드 붕괴).
//  가드 붕괴 조건: 강공/차지 공격(HitType Heavy/Strong/Force 또는 플레이어 강공 입력), 방패 밀치기 패링, 안정도 소진.
//  측면/후방(방패 각 밖) 공격은 막지 못하고 1.25배 피해 → 측면 돌기가 정답.
//  공격: 방패 밀치기(호박 큐, 강공 = 가드 브레이크, 패링 가능) / 배턴 내려치기(일반, 이때는 방패가 내려가 정면이 빈다).
public sealed class ShieldEnforcerBrain : EnemyBrain
{
    [Header("Shield")]
    [SerializeField, Range(30f, 200f)] float shieldArcDegrees = 140f;
    [SerializeField, Min(1f)] float shieldStability = 60f;
    [SerializeField, Min(0f)] float blockedStabilityCost = 14f;
    [SerializeField, Min(0f)] float stabilityRegenDelay = 1.6f;
    [SerializeField, Min(0f)] float stabilityRegenPerSecond = 20f;
    [SerializeField, Min(0.1f)] float guardBrokenDuration = 2.4f;
    [SerializeField, Min(1f)] float flankDamageMultiplier = 1.25f;
    [SerializeField] Transform shieldTransform;
    [SerializeField] GameObject blockSparkVfx;
    [SerializeField] AudioClip blockClang;
    [SerializeField] AudioClip guardBreakSfx;
    [SerializeField] string shieldHitState = "ShieldHit";
    [SerializeField] string guardBrokenState = "GuardBroken";

    [Header("Attacks")]
    [SerializeField] EnemyStrike shieldBash = new EnemyStrike
    {
        name = "ShieldBash_Heavy", animState = "Bash", windup = 1.0f, cueLead = 0.62f, cueType = EnemyCueType.Heavy,
        damage = 20f, hitType = HitType.Heavy, range = 1.8f, width = 1.9f, trackLockBeforeHit = 0.3f, trackTurnRate = 200f,
        lungeDistance = 1.4f, lungeDuration = 0.28f, recovery = 1.15f, hyperArmor = true
    };
    [SerializeField] EnemyStrike batonStrike = new EnemyStrike
    {
        name = "Baton", animState = "Baton", windup = 0.6f, cueType = EnemyCueType.Normal,
        damage = 12f, hitType = HitType.Normal, range = 2.0f, width = 2.2f, trackLockBeforeHit = 0.2f, trackTurnRate = 240f,
        lungeDistance = 0.3f, lungeDuration = 0.2f, recovery = 0.75f
    };
    [SerializeField, Range(0f, 1f)] float bashChance = 0.55f;

    EnemyStrike[] _bashSeq;
    EnemyStrike[] _batonSeq;
    float _stability;
    float _lastBlockAt = float.NegativeInfinity;
    float _guardDownUntil;
    bool _rolled;
    bool _preferBash;
    int _shieldHitHash;
    int _guardBrokenHash;
    Transform _cachedAttacker;
    PlayerCombatController _cachedAttackerCombat;

    /// <summary>QA: 막은 횟수 / 가드 붕괴 횟수.</summary>
    public int BlockCount { get; private set; }
    public int GuardBreakCount { get; private set; }
    public float StabilityNormalized => _stability / Mathf.Max(1f, shieldStability);

    public bool ShieldUp
    {
        get
        {
            if (IsDead || State == EnemyBrainState.Stagger || Now < _guardDownUntil)
                return false;
            return !(State == EnemyBrainState.Attack && CurrentStrike == batonStrike);
        }
    }

    protected override void Awake()
    {
        base.Awake();
        _bashSeq = new[] { shieldBash };
        _batonSeq = new[] { batonStrike };
        _shieldHitHash = HashOf(shieldHitState);
        _guardBrokenHash = HashOf(guardBrokenState);
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        _stability = shieldStability;
        _guardDownUntil = 0f;
    }

    protected override void Update()
    {
        base.Update();
        if (_stability < shieldStability && Now - _lastBlockAt > stabilityRegenDelay)
            _stability = Mathf.Min(shieldStability, _stability + stabilityRegenPerSecond * Time.deltaTime);
    }

    protected override void OnStateEntered(EnemyBrainState previous, EnemyBrainState next)
    {
        if (next == EnemyBrainState.Approach)
            _rolled = false;
    }

    protected override EnemyStrike[] ChooseAttack(float distance)
    {
        if (!_rolled)
        {
            _rolled = true;
            _preferBash = Random.value < bashChance;
        }

        float bashReach = shieldBash.range + shieldBash.lungeDistance;
        if (_preferBash && distance <= bashReach && distance >= 1.2f)
            return _bashSeq;
        if (distance <= batonStrike.range + 0.3f)
            return _batonSeq;
        if (distance <= bashReach)
            return _bashSeq;
        return null;
    }

    protected override bool ModifyIncomingHit(ref HitPayload payload, ref float damageMultiplier, ref float poiseMultiplier)
    {
        if (!ShieldUp)
            return true;

        Vector3 from = (payload.attacker != null ? payload.attacker.position : payload.hitPoint - payload.hitDirection) - transform.position;
        from.y = 0f;
        if (from.sqrMagnitude > 0.0001f && Vector3.Angle(transform.forward, from) > shieldArcDegrees * 0.5f)
        {
            damageMultiplier *= flankDamageMultiplier; // 측면/후방
            return true;
        }

        if (IsGuardBreakingHit(payload))
        {
            BreakGuard(); // 강공: 방패를 쳐내고 피해도 들어간다
            return true;
        }

        // 막기
        BlockCount++;
        _lastBlockAt = Now;
        _stability -= blockedStabilityCost;
        Vector3 sparkAt = shieldTransform != null ? shieldTransform.position : payload.hitPoint;
        if (blockSparkVfx != null)
            TransientVfxPool.Spawn(blockSparkVfx, sparkAt, Quaternion.LookRotation(from.sqrMagnitude > 0.0001f ? from.normalized : transform.forward, Vector3.up), null, 1f);
        if (blockClang != null)
            OneShotAudio.PlayAt(blockClang, sparkAt, 0.9f, Random.Range(0.9f, 1.05f), 0.7f);

        if (_stability <= 0f)
        {
            BreakGuard();
            return false;
        }

        if (State != EnemyBrainState.Attack)
            PlayAction(_shieldHitHash, 1f);
        return false;
    }

    bool IsGuardBreakingHit(HitPayload payload)
    {
        switch (payload.hitType)
        {
            case HitType.Heavy:
            case HitType.Strong:
            case HitType.Force:
            case HitType.Parried:
                return true;
        }

        PlayerCombatController combat = ResolveAttackerCombat(payload.attacker);
        return combat != null && combat.CurrentAttackInput == Combat.AttackInput.Heavy;
    }

    PlayerCombatController ResolveAttackerCombat(Transform attacker)
    {
        if (attacker == null)
            return null;
        if (attacker == _cachedAttacker)
            return _cachedAttackerCombat;
        _cachedAttacker = attacker;
        _cachedAttackerCombat = Target != null && attacker.root == Target.root && targetCombat != null
            ? targetCombat
            : attacker.GetComponentInParent<PlayerCombatController>();
        return _cachedAttackerCombat;
    }

    void BreakGuard()
    {
        GuardBreakCount++;
        _guardDownUntil = Now + guardBrokenDuration;
        _stability = shieldStability;
        if (guardBreakSfx != null)
            OneShotAudio.PlayAt(guardBreakSfx, transform.position + Vector3.up * 1.2f, 1f, 0.85f, 0.7f);
        EnterStagger(guardBrokenDuration, true, _guardBrokenHash);
    }

    protected override void HandleParried(EnemyStrike s)
    {
        if (s == shieldBash)
        {
            AbortSequence();
            GuardBreakCount++;
            _guardDownUntil = Now + parryStaggerDuration;
            _stability = shieldStability;
            EnterStagger(parryStaggerDuration, true, _guardBrokenHash);
            return;
        }
        base.HandleParried(s);
    }

    protected override float PoiseDamageFor(HitType type)
    {
        // 방패가 내려간 동안만 포이즈가 깎인다 (막힌 공격은 ModifyIncomingHit 에서 걸러짐)
        return base.PoiseDamageFor(type) * 0.8f;
    }
}
