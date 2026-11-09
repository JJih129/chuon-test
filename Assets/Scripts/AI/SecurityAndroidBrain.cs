using UnityEngine;

// 츠카사 경비 안드로이드 (고주파 블레이드)
//  - 3연격: 1·2타 일반(가드/패링 가능, 큐 없음), 3타 금색 큐(패링 가능) → 패링 시 2.2s 경직(리포스트).
//  - 찌르기: 빨강 큐(회피 전용), 2.4m 돌진. 큐 시점 직후 방향 고정 → 옆으로 피하면 빗나감. 후딜 1.1s(반격 기회).
//  - 백스텝: 연격 후 확률적으로, 연속 경직 한도 도달 시, 플레이어가 너무 붙으면.
public sealed class SecurityAndroidBrain : EnemyBrain
{
    [Header("Security Android — 3-hit string")]
    [SerializeField] EnemyStrike[] comboString =
    {
        new EnemyStrike { name = "Slash1", animState = "Atk1", windup = 0.5f, cueType = EnemyCueType.Normal, damage = 9f, hitType = HitType.Normal, range = 2.1f, width = 2.4f, trackLockBeforeHit = 0.18f, lungeDistance = 0.5f, lungeDuration = 0.2f, recovery = 0.1f },
        new EnemyStrike { name = "Slash2", animState = "Atk2", windup = 0.42f, cueType = EnemyCueType.Normal, damage = 9f, hitType = HitType.Normal, range = 2.1f, width = 2.4f, trackLockBeforeHit = 0.16f, lungeDistance = 0.4f, lungeDuration = 0.2f, recovery = 0.12f },
        new EnemyStrike { name = "Slash3_Parryable", animState = "Atk3", windup = 0.8f, cueLead = 0.5f, cueType = EnemyCueType.Parryable, damage = 15f, hitType = HitType.Strong, range = 2.4f, width = 2.2f, trackLockBeforeHit = 0.22f, lungeDistance = 0.9f, lungeDuration = 0.25f, recovery = 0.9f, hyperArmor = true },
    };

    [Header("Dodge-only thrust")]
    [SerializeField] EnemyStrike thrust = new EnemyStrike
    {
        name = "Thrust_DodgeOnly", animState = "Thrust", windup = 0.95f, cueLead = 0.6f, cueType = EnemyCueType.DodgeOnly,
        damage = 20f, hitType = HitType.Heavy, range = 1.9f, width = 1.1f, trackLockBeforeHit = 0.5f, trackTurnRate = 260f,
        lungeDistance = 2.4f, lungeDuration = 0.3f, recovery = 1.1f, hyperArmor = true
    };
    [SerializeField, Min(0f)] float thrustMinDistance = 2.8f;
    [SerializeField, Min(0f)] float thrustMaxDistance = 4.8f;
    [SerializeField, Range(0f, 1f)] float thrustChance = 0.6f;

    [Header("Backstep")]
    [SerializeField] string backstepState = "Backstep";
    [SerializeField, Min(0f)] float backstepDistance = 2.4f;
    [SerializeField, Min(0.1f)] float backstepDuration = 0.45f;
    [SerializeField, Range(0f, 1f)] float backstepAfterStringChance = 0.35f;
    [SerializeField, Min(0f)] float crowdedDistance = 1.3f;
    [SerializeField, Min(0f)] float crowdedBackstepCooldown = 3f;

    EnemyStrike[] _stringSeq;
    EnemyStrike[] _thrustSeq;
    float _backstepUntil;
    float _nextCrowdBackstepAt;
    bool _thrustRolled;
    bool _preferThrust;
    int _backstepHash;

    protected override void Awake()
    {
        base.Awake();
        _stringSeq = comboString;
        _thrustSeq = new[] { thrust };
        _backstepHash = HashOf(backstepState);
    }

    protected override void OnStateEntered(EnemyBrainState previous, EnemyBrainState next)
    {
        if (next == EnemyBrainState.Approach)
            _thrustRolled = false;
    }

    protected override EnemyStrike[] ChooseAttack(float distance)
    {
        if (!_thrustRolled)
        {
            _thrustRolled = true;
            _preferThrust = Random.value < thrustChance;
        }

        if (_preferThrust && distance >= thrustMinDistance && distance <= thrustMaxDistance)
            return _thrustSeq;

        if (_stringSeq != null && _stringSeq.Length > 0 && distance <= _stringSeq[0].range + 0.5f)
            return _stringSeq;

        return null;
    }

    protected override void OnSequenceFinished()
    {
        if (Target != null && FlatDistanceToTarget() < 2.6f && Random.value < backstepAfterStringChance)
            StartBackstep();
    }

    protected override bool OnFlinchLockout()
    {
        StartBackstep();
        return true;
    }

    void StartBackstep()
    {
        if (IsDead)
            return;
        if (State == EnemyBrainState.Attack)
            AbortSequence();
        _backstepUntil = Now + backstepDuration;
        PlayAction(_backstepHash, 1f);
        SetState(EnemyBrainState.Recover);
    }

    protected override void TickRecover(float now, float dt)
    {
        if (now < _backstepUntil)
        {
            float speed = backstepDistance / Mathf.Max(0.05f, backstepDuration);
            MoveDirect(-transform.forward * (speed * dt));
            return;
        }
        base.TickRecover(now, dt);
    }

    protected override void TickStrafe(float now, float dt)
    {
        if (Target != null && now >= _nextCrowdBackstepAt && FlatDistanceToTarget() < crowdedDistance
            && !EnemyAttackTokenDirector.Holds(Target, this, EnemyTokenKind.Melee))
        {
            _nextCrowdBackstepAt = now + crowdedBackstepCooldown;
            StartBackstep();
            return;
        }
        base.TickStrafe(now, dt);
    }
}
