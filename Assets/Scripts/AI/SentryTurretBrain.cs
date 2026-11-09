using UnityEngine;

// 츠카사 센트리 터렛 (벽/바닥 고정)
//  Idle: 센서 헤드가 좌우로 스캔 (좁은 시야 원뿔). 발견 → 비프 + 빨간 레이저 조준선.
//  Track: 레이저가 플레이어를 추적 (선회 속도 제한) trackDuration 초 → 원거리 토큰(대상당 1) 획득 시 Lock.
//  Lock: 조준 고정 + 빨강 큐(회피 전용) → 정확히 lockLead 초 뒤 첫 발. 고정된 선을 따라 연사 (옆으로 비키면 전부 빗나감).
//  파괴 가능 (EnemyHealth), 강공/포이즈 붕괴 시 잠시 정지(헤드가 처짐, 레이저 꺼짐). 잔해는 남는다.
public sealed class SentryTurretBrain : EnemyBrain
{
    [Header("Turret Rig")]
    [SerializeField] Transform yawPivot;
    [SerializeField] Transform pitchPivot;
    [SerializeField] Transform muzzle;
    [SerializeField, Range(10f, 180f)] float yawLimit = 80f;
    [SerializeField, Min(10f)] float trackYawSpeed = 110f;
    [SerializeField, Min(10f)] float trackPitchSpeed = 80f;
    [SerializeField] float pitchMin = -40f;
    [SerializeField] float pitchMax = 25f;

    [Header("Idle Scan")]
    [SerializeField, Range(0f, 170f)] float scanHalfAngle = 55f;
    [SerializeField, Min(1f)] float scanSpeed = 24f;

    [Header("Burst (dodge-only)")]
    [SerializeField, Min(0.1f)] float trackDuration = 1.4f;
    [Tooltip("조준 고정(큐) → 첫 발까지 = 공지 시간")]
    [SerializeField, Min(0.2f)] float lockLead = 0.55f;
    [SerializeField, Min(1)] int roundsPerBurst = 5;
    [SerializeField, Min(0.02f)] float roundInterval = 0.09f;
    [SerializeField, Min(0f)] float roundDamage = 7f;
    [SerializeField, Min(0.05f)] float roundRadius = 0.28f;
    [SerializeField, Min(1f)] float range = 20f;
    [SerializeField, Min(0f)] float burstCooldown = 2.2f;
    [SerializeField, Min(0.3f)] float decalWidth = 1.1f;
    [SerializeField] LayerMask shotMask = ~0;

    [Header("Laser / FX")]
    [SerializeField] LineRenderer laser;
    [SerializeField] LineRenderer[] tracers;
    [SerializeField, Min(0.01f)] float tracerTime = 0.05f;
    [SerializeField] Color laserTrackColor = new Color(1f, 0.08f, 0.06f, 0.55f);
    [SerializeField] Color laserLockColor = new Color(1f, 0.15f, 0.1f, 1f);
    [SerializeField] GameObject muzzleVfx;
    [SerializeField] AudioClip fireSfx;
    [SerializeField] GameObject deathVfx;
    [SerializeField] AudioClip deathSfx;

    readonly RaycastHit[] _hits = new RaycastHit[12];
    float _yaw;
    float _pitch;
    float _scanDir = 1f;
    float _trackTime;
    bool _locked;
    float _firstRoundAt;
    int _roundsFired;
    float _cooldownUntil;
    Vector3 _lockDir;
    float[] _tracerOffAt;
    int _nextTracer;
    bool _laserOn;
    Quaternion _yawBase = Quaternion.identity;
    Quaternion _pitchBase = Quaternion.identity;

    public bool IsLocked => _locked;
    public bool IsPoweredDown => _poweredDown;
    bool _poweredDown;

    /// <summary>외부 정지(경비 해제 등): 폭발 없이 헤드가 처진 잔해로 남는다. 사망과 같은 처리 → 락온·토큰·HP 바 해제.</summary>
    public void PowerDown()
    {
        if (IsDead)
            return;
        _poweredDown = true;
        Die();
    }

    protected override void Awake()
    {
        base.Awake();
        Quaternion inv = Quaternion.Inverse(transform.rotation);
        if (yawPivot != null)
            _yawBase = inv * yawPivot.rotation;
        if (pitchPivot != null)
            _pitchBase = inv * pitchPivot.rotation;
        _tracerOffAt = new float[tracers != null ? tracers.Length : 0];
        if (shotMask.value == ~0)
            shotMask = ~LayerMask.GetMask("Enemy", "Ignore Raycast", "UI");
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        _poweredDown = false;
        _locked = false;
        _trackTime = 0f;
        _yaw = 0f;
        _pitch = 0f;
        SetLaser(false, laserTrackColor);
        for (int i = 0; tracers != null && i < tracers.Length; i++)
        {
            if (tracers[i] != null)
                tracers[i].enabled = false;
        }
    }

    protected override EnemyStrike[] ChooseAttack(float distance) => null;

    protected override void Update()
    {
        base.Update();
        float now = Now;
        for (int i = 0; i < _tracerOffAt.Length; i++)
        {
            if (_tracerOffAt[i] > 0f && now >= _tracerOffAt[i])
            {
                _tracerOffAt[i] = 0f;
                if (tracers[i] != null)
                    tracers[i].enabled = false;
            }
        }
        if (_laserOn)
            UpdateLaser();
    }

    // ---------------------------------------------------------------- states
    protected override void TickIdle(float now, float dt)
    {
        if (IsAlerted)
        {
            SetState(EnemyBrainState.Approach);
            return;
        }
        _yaw += _scanDir * scanSpeed * dt;
        if (Mathf.Abs(_yaw) >= scanHalfAngle)
        {
            _yaw = Mathf.Clamp(_yaw, -scanHalfAngle, scanHalfAngle);
            _scanDir = -_scanDir;
        }
        _pitch = Mathf.MoveTowards(_pitch, 8f, 30f * dt);
        ApplyAim();
    }

    protected override void TickPatrol(float now, float dt) => TickIdle(now, dt);

    protected override void TickAlert(float now, float dt)
    {
        SetLaser(true, laserTrackColor);
        AimAtTarget(dt);
        if (TimeInState >= alertDuration)
        {
            _trackTime = 0f;
            SetState(EnemyBrainState.Approach);
        }
    }

    protected override void TickApproach(float now, float dt) => TickTrack(now, dt);
    protected override void TickStrafe(float now, float dt) => TickTrack(now, dt);

    void TickTrack(float now, float dt)
    {
        if (!HasTarget)
        {
            SetLaser(false, laserTrackColor);
            return;
        }

        SetLaser(true, laserTrackColor);
        bool aimed = AimAtTarget(dt);
        bool sees = perception == null || perception.CanSeeTarget;
        bool inRange = (TargetAimPoint - Muzzle.position).sqrMagnitude <= range * range;
        if (sees && inRange && aimed)
            _trackTime += dt;
        else
            _trackTime = Mathf.Max(0f, _trackTime - dt * 0.5f);

        if (_trackTime >= trackDuration && now >= _cooldownUntil)
        {
            float hold = lockLead + roundsPerBurst * roundInterval + 0.5f;
            if (TryAcquireToken(EnemyTokenKind.Ranged, hold))
                BeginLock(now);
        }
    }

    void BeginLock(float now)
    {
        _locked = true;
        _roundsFired = 0;
        _lockDir = Muzzle.forward;
        _firstRoundAt = now + lockLead;
        SetState(EnemyBrainState.Attack);
        SetLaser(true, laserLockColor);

        // 큐: 지금부터 정확히 lockLead 뒤 첫 발 (정직) — 바닥 데칼은 고정 선 그대로 (바닥 투영)
        float yawDeg = Mathf.Atan2(_lockDir.x, _lockDir.z) * Mathf.Rad2Deg;
        Vector3 origin = transform.position;
        Vector3 m = Muzzle.position;
        origin.x = m.x;
        origin.z = m.z;
        CueCount++;
        if (cue != null)
            cue.IssueAt(EnemyCueType.DodgeOnly, lockLead, origin, yawDeg, range, decalWidth);
        RaiseCueIssued(EnemyCueType.DodgeOnly, lockLead);
    }

    protected override void TickAttack(float now, float dt)
    {
        if (!_locked)
        {
            SetState(EnemyBrainState.Approach);
            return;
        }

        // 고정 중 레이저 점멸
        bool blink = Mathf.Repeat(now * 14f, 1f) < 0.5f;
        if (laser != null)
            laser.startColor = laser.endColor = blink ? laserLockColor : laserTrackColor;

        // 첫 발 = 공지 시각에 가장 가까운 프레임 (반 프레임 반올림, |오차| ≤ dt/2)
        while (_roundsFired < roundsPerBurst && now + dt * 0.5f >= _firstRoundAt + _roundsFired * roundInterval)
        {
            if (_roundsFired == 0)
            {
                LastCueHonestyError = (now - _firstRoundAt);
                MaxCueHonestyError = Mathf.Max(MaxCueHonestyError, Mathf.Abs(LastCueHonestyError));
            }
            FireRound();
            _roundsFired++;
            if (State != EnemyBrainState.Attack)
                return;
        }

        if (_roundsFired >= roundsPerBurst)
        {
            _locked = false;
            _trackTime = 0f;
            _cooldownUntil = now + burstCooldown;
            ReleaseTokens();
            SetLaser(true, laserTrackColor);
            SetState(EnemyBrainState.Recover);
        }
    }

    protected override void TickRecover(float now, float dt)
    {
        AimAtTarget(dt * 0.5f);
        if (TimeInState >= 0.35f)
            SetState(IsAlerted ? EnemyBrainState.Approach : EnemyBrainState.Idle);
    }

    protected override void TickStagger(float now, float dt)
    {
        _locked = false;
        SetLaser(false, laserTrackColor);
        _pitch = Mathf.MoveTowards(_pitch, pitchMax, 90f * dt); // 헤드가 처진다 (+X = 아래)
        ApplyAim();
        base.TickStagger(now, dt);
    }

    protected override void OnStaggered(float duration, bool riposteWindow)
    {
        _locked = false;
        _trackTime = 0f;
        if (cue != null)
            cue.Cancel();
    }

    protected override void OnDied()
    {
        _locked = false;
        SetLaser(false, laserTrackColor);
        _pitch = pitchMax;
        ApplyAim();
        if (_poweredDown)
            return;
        if (deathVfx != null)
            TransientVfxPool.Spawn(deathVfx, Muzzle.position, Quaternion.identity, null, 2f);
        if (deathSfx != null)
            OneShotAudio.PlayAt(deathSfx, Muzzle.position, 1f, 1f, 0.8f);
    }

    protected override void OnStateEntered(EnemyBrainState previous, EnemyBrainState next)
    {
        if (next != EnemyBrainState.Attack && _locked)
        {
            _locked = false;
            ReleaseTokens();
        }
        if (next == EnemyBrainState.Idle)
            SetLaser(false, laserTrackColor);
    }

    protected override void OnAlerted()
    {
        _trackTime = 0f;
    }

    // ---------------------------------------------------------------- fire
    Transform Muzzle => muzzle != null ? muzzle : (pitchPivot != null ? pitchPivot : transform);

    void FireRound()
    {
        Vector3 origin = Muzzle.position;
        Vector3 end = origin + _lockDir * range;
        int count = Physics.SphereCastNonAlloc(origin, roundRadius, _lockDir, _hits, range, shotMask, QueryTriggerInteraction.Ignore);
        Transform selfRoot = transform.root;
        float best = float.PositiveInfinity;
        Collider bestCollider = null;
        for (int i = 0; i < count; i++)
        {
            Collider c = _hits[i].collider;
            if (c == null || c.transform.root == selfRoot || _hits[i].distance <= 0f)
                continue;
            if (_hits[i].distance < best)
            {
                best = _hits[i].distance;
                bestCollider = c;
            }
        }

        if (bestCollider != null)
        {
            end = origin + _lockDir * best;
            if (Target != null && bestCollider.transform.root == Target.root)
            {
                HitPayload payload = new HitPayload
                {
                    damage = roundDamage,
                    hitType = HitType.Normal,
                    hitPoint = end,
                    hitDirection = _lockDir,
                    attacker = transform
                };
                EnemyCuePalette.ApplyDefenseRules(ref payload, EnemyCueType.DodgeOnly);
                DeliverPayload(payload, null);
            }
        }

        ShowTracer(origin, end);
        if (muzzleVfx != null)
            TransientVfxPool.Spawn(muzzleVfx, origin, Quaternion.LookRotation(_lockDir), null, 0.5f);
        if (fireSfx != null)
            OneShotAudio.PlayAt(fireSfx, origin, 0.75f, Random.Range(0.95f, 1.1f), 0.8f);
    }

    void ShowTracer(Vector3 from, Vector3 to)
    {
        if (tracers == null || tracers.Length == 0)
            return;
        int i = _nextTracer;
        _nextTracer = (_nextTracer + 1) % tracers.Length;
        LineRenderer lr = tracers[i];
        if (lr == null)
            return;
        lr.SetPosition(0, from);
        lr.SetPosition(1, to);
        lr.enabled = true;
        _tracerOffAt[i] = Now + tracerTime;
    }

    // ---------------------------------------------------------------- aim
    bool AimAtTarget(float dt)
    {
        if (Target == null || yawPivot == null)
            return false;
        Vector3 pivot = pitchPivot != null ? pitchPivot.position : Muzzle.position;
        Vector3 to = transform.InverseTransformDirection(TargetAimPoint - pivot);
        float wantYaw = Mathf.Clamp(Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg, -yawLimit, yawLimit);
        float flat = Mathf.Sqrt(to.x * to.x + to.z * to.z);
        float wantPitch = Mathf.Clamp(-Mathf.Atan2(to.y, Mathf.Max(0.01f, flat)) * Mathf.Rad2Deg, pitchMin, pitchMax);
        _yaw = Mathf.MoveTowardsAngle(_yaw, wantYaw, trackYawSpeed * dt);
        _pitch = Mathf.MoveTowardsAngle(_pitch, wantPitch, trackPitchSpeed * dt);
        ApplyAim();
        return Mathf.Abs(Mathf.DeltaAngle(_yaw, wantYaw)) < 4f && Mathf.Abs(Mathf.DeltaAngle(_pitch, wantPitch)) < 6f;
    }

    void ApplyAim()
    {
        // 루트 기준 축으로 회전 (FBX 피벗의 임포트 회전과 무관): yaw = 루트 Up, pitch = yaw 후 루트 Right
        Quaternion r = transform.rotation;
        Quaternion yawQ = Quaternion.Euler(0f, _yaw, 0f);
        if (yawPivot != null)
            yawPivot.rotation = r * yawQ * _yawBase;
        if (pitchPivot != null)
            pitchPivot.rotation = r * yawQ * Quaternion.Euler(_pitch, 0f, 0f) * _pitchBase;
    }

    void SetLaser(bool on, Color color)
    {
        _laserOn = on && laser != null;
        if (laser == null)
            return;
        laser.enabled = on;
        laser.startColor = color;
        laser.endColor = new Color(color.r, color.g, color.b, color.a * 0.25f);
    }

    void UpdateLaser()
    {
        Vector3 origin = Muzzle.position;
        Vector3 dir = _locked ? _lockDir : Muzzle.forward;
        Vector3 end = origin + dir * range;
        if (Physics.Raycast(origin + dir * 0.15f, dir, out RaycastHit hit, range, shotMask, QueryTriggerInteraction.Ignore))
            end = hit.point;
        laser.SetPosition(0, origin);
        laser.SetPosition(1, end);
    }
}
