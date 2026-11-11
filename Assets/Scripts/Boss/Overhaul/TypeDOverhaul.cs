// 반복 16: Type-D 보스 개편 오케스트레이터 (Docs/Boss_TypeD_Phases.md). FacilityCombatRules 가 Level_Facility 보스에 붙인다.
//  HP 문턱: 70 % 봉쇄 프로토콜(기믹) → 66 % 장갑 해제(페이즈 2) → 40 % 부위 파괴 → 30 % 과열(페이즈 3) → 15 % 임계 폭주 → 0 % 최종 처형.
//  HP 바닥(BossHealth.HpFloor)으로 기믹·임계 폭주·최종 처형을 반드시 거치게 한다 (궁극기 큰 피해로 건너뛰지 않음).
//  피해·균형 배율(실명 측면, 과부하, 블링크 창, 과열 코어 ×2), 충격 4단계(BossImpactStagger), 처형 훅(ExecutionDirector),
//  페이즈 연출(조명·음악 레이어·흑백 흡수·EGO 대사·HUD 배너)을 한곳에서 관리한다.
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class TypeDOverhaul : MonoBehaviour
{
    public enum Stage
    {
        Phase1 = 0,
        Lockdown = 1,
        Phase1b = 2,
        Phase2 = 3,
        Phase3 = 4,
        Desperation = 5,
        Overdrive = 6,
        Final = 7,
        Dead = 8
    }

    public static TypeDOverhaul Active { get; private set; }
    /// <summary>QA/스크린샷: 재도전(도착 생략) 로드에서도 장갑 해제 컷을 보여 준다.</summary>
    public static bool QaForceTransitionCut;

    [Header("HP thresholds (normalized)")]
    [SerializeField] float lockdownHp = 0.70f;
    [SerializeField] float phaseTwoHp = 0.66f;
    [SerializeField] float partsHp = 0.40f;
    [SerializeField] float phaseThreeHp = 0.30f;
    [SerializeField] float desperationHp = 0.15f;

    [Header("Phase 2")]
    [SerializeField, Range(0f, 1f)] float delayedAttackChance = 0.35f;
    [SerializeField, Min(0f)] float delayedAttackHold = 0.28f;
    [SerializeField, Range(0f, 0.5f)] float phaseTwoFollowUpBonus = 0.22f;
    [SerializeField] float purgeDuration = 2.4f;
    [SerializeField] string[] purgePlates = { "ChestPlate", "Collar", "AbPlate0", "AbPlate1", "LeftPauldron", "RightPauldron", "LeftTasset", "RightTasset", "LeftThighGuard", "RightThighGuard", "LeftShinGuard", "RightShinGuard", "Spine_Spine", "Fauld" };

    [Header("Magenta grab (phase 2+)")]
    [SerializeField] float grabLead = 0.95f;
    [SerializeField] int grabDamage = 50;
    [SerializeField] float grabChance = 0.22f;
    [SerializeField] float grabCooldown = 11f;
    [SerializeField] float grabMaxStartDistance = 5.5f;
    [SerializeField] float grabRecovery = 1.1f;

    [Header("Heat vent (phase 3)")]
    [SerializeField] float ventInterval = 13f;
    [SerializeField] float ventFirstDelay = 4f;
    [SerializeField] float ventLead = 1.3f;
    [SerializeField] float ventRadius = 4.6f;
    [SerializeField] int ventDamage = 43;
    [SerializeField] float ventPunishWindow = 3.2f;
    [SerializeField] float ventPunishMultiplier = 1.35f;
    [SerializeField] float ventMaxStartDistance = 7f;

    [Header("Desperation (15 %)")]
    [SerializeField] float desperationLead = 5.0f;
    [SerializeField] int desperationDamage = 117;
    [SerializeField] float overdriveBreakSeconds = 6f;

    [Header("Multipliers")]
    [SerializeField] float overheatBalanceMultiplier = 2f;
    [SerializeField] float overloadDamageMultiplier = 1.2f;
    [SerializeField] float overloadBalanceMultiplier = 1.5f;
    [SerializeField] float overloadDuration = 5f;
    [SerializeField, Range(0.3f, 1f)] float lockdownBossDamageScale = 0.7f; // 반복 18: 봉쇄(노드 단계) 중 보스 공격력 배율
    [SerializeField] float blinkWindow = 2.2f;
    [SerializeField] float blinkDamageMultiplier = 1.5f;
    [SerializeField] float strongParryBalance = 35f;

    [Header("Execution")]
    [SerializeField] float frontExecutionPercent = 0.10f;
    [SerializeField] float backExecutionPercent = 0.13f;
    [SerializeField] float executionRange = 3.4f;
    [SerializeField] float executionDistanceBonus = 0.3f;
    [SerializeField] float finalBreakAutoSeconds = 20f;

    // ---- 부분 클래스(BossController.Overhaul.cs)가 읽는 값
    public float DelayedAttackChance => delayedAttackChance;
    public float DelayedAttackHold => delayedAttackHold;
    [System.NonSerialized] public int QaDelayedAttacks;
    public float GrabLead => grabLead;
    public int GrabDamage => grabDamage;
    public float GrabChance => grabChance;
    public float GrabCooldown => grabCooldown;
    public float GrabMaxStartDistance => grabMaxStartDistance;
    public float GrabRecovery => grabRecovery;
    public float VentInterval => ventInterval;
    public float VentLead => ventLead;
    public float VentRadius => ventRadius;
    public int VentDamage => ventDamage;
    public float VentPunishWindow => ventPunishWindow;
    public float VentPunishMultiplier => ventPunishMultiplier;
    public float VentMaxStartDistance => ventMaxStartDistance;
    public float DesperationLead => desperationLead;
    public int DesperationDamage => desperationDamage;
    public Vector3 ArenaCenter { get; private set; }
    public float ArenaRadius { get; private set; } = 14f;

    public BossController Boss { get; private set; }
    public BossHealth Health { get; private set; }
    public BossBreakController Break { get; private set; }
    public TypeDArmorRig Armor { get; private set; }
    public BossImpactStagger Impact { get; private set; }
    public BossLockdownGimmick Lockdown { get; private set; }
    public BossBreakableParts Parts { get; private set; }
    public BossOverhaulHUD Hud { get; private set; }
    public Stage CurrentStage => _stage;
    public float[] HudTicks => new[] { lockdownHp, phaseTwoHp, partsHp, phaseThreeHp, desperationHp };

    public bool AllowsBossSpecials => !_transitionActive && !ExecutionDirector.IsExecuting && _stage != Stage.Final;
    public bool AllowsGrab => _purgeDone && !_desperationActive && Boss != null && Boss.CurrentPhase >= 2;
    public bool ImpactReactionsAllowed => !_transitionActive && !_desperationActive && _stage != Stage.Final && !ExecutionDirector.IsExecuting;
    public bool IsVentDue(float readyAt) => _overheatDone && !_desperationActive && !_desperationRequested && Time.time >= Mathf.Max(readyAt, _ventFirstAt);
    public bool IsOverloaded => Time.time < _overloadUntil;
    public bool IsVentExposed => _ventStallUntil > Time.time;
    public bool IsBlinkWindowOpen => Time.time < _blinkUntil;
    public bool IsDesperationActive => _desperationActive;
    public float BalanceMultiplier => (_overheatDone ? overheatBalanceMultiplier : 1f) * (IsOverloaded ? overloadBalanceMultiplier : 1f);

    public bool IsFinalExecutionPending => _stage == Stage.Final && !_dead;
    public bool IsExecutionAvailable =>
        !_dead && Boss != null && Break != null && Break.IsInBreak && !ExecutionDirector.IsExecuting &&
        (IsFinalExecutionPending || Break.IsFinisherAvailable);
    public float ExecutionRange => executionRange;
    public float ExecutionDistanceBonus => executionDistanceBonus;
    public Transform CoreAnchor { get; private set; }
    public Vector3 MarkerAnchorPosition => _head != null ? _head.position + Vector3.up * 0.75f : Boss.transform.position + Vector3.up * 3f;

    // QA
    public int QaLockdowns { get; private set; }
    public int QaPurges { get; private set; }
    public int QaStrongParries { get; private set; }
    public int QaBlinks { get; private set; }
    public int QaOverloads { get; private set; }
    public int QaFinalBreaks { get; private set; }
    public float QaLastPhaseChangeAt { get; private set; }
    public string QaLastEvent { get; private set; } = "";

    Stage _stage;
    bool _lockdownStarted, _lockdownDone, _purgeDone, _partsUnlocked, _overheatDone;
    bool _desperationRequested, _desperationActive, _desperationDone;
    float _desperationRequestedAt;
    bool _transitionActive;
    bool _dead;
    float _overloadUntil;
    float _ventFirstAt = float.PositiveInfinity;
    float _ventStallUntil;
    float _blinkUntil;
    float _finalSince;
    float _nextLockdownThrottleAt;
    float _nextBreakKeepAliveAt;
    Transform _head;
    Transform _player;
    readonly List<Collider> _ignoreForDebris = new List<Collider>();

    void Awake()
    {
        Boss = GetComponent<BossController>() ?? GetComponentInParent<BossController>();
        if (Boss == null)
        {
            enabled = false;
            return;
        }
        Health = Boss.bossHealth != null ? Boss.bossHealth : Boss.GetComponentInChildren<BossHealth>(true);
        Break = Boss.breakController != null ? Boss.breakController : Boss.GetComponentInChildren<BossBreakController>(true);
        Armor = TypeDArmorRig.EnsureInstalled(Boss);
        Boss.Overhaul = this;
        Active = this;

        Impact = gameObject.AddComponent<BossImpactStagger>();
        Impact.Bind(Boss, this);
        Lockdown = gameObject.AddComponent<BossLockdownGimmick>();
        Lockdown.Bind(this);
        Parts = gameObject.AddComponent<BossBreakableParts>();
        Parts.Bind(this);
        Hud = gameObject.AddComponent<BossOverhaulHUD>();
        Hud.Bind(this);
    }

    void Start()
    {
        if (Boss == null)
            return;
        Boss.ConfigureOverhaulPhases(phaseTwoHp, phaseThreeHp, phaseTwoFollowUpBonus);
        MeasureArena();
        ResolveAnchors();
        CacheArenaLights();
        if (Health != null)
        {
            Health.HpFloor = Mathf.CeilToInt(Health.MaxHP * lockdownHp);
            Health.OnFloorReached += HandleFloorReached;
            Health.OnDied += HandleDied;
        }
        if (Break != null)
            Break.OnBreakEnter.AddListener(HandleBreakEnter);
        BossDamageReceiver.HitFilter = FilterHit;
        BossDamageReceiver.HitApplied += HandleHitApplied;
        Collider[] own = Boss.GetComponentsInChildren<Collider>(true);
        _ignoreForDebris.AddRange(own);
    }

    void OnDestroy()
    {
        if (Active == this)
            Active = null;
        if (BossDamageReceiver.HitFilter == (System.Func<BossDamageReceiver, HitPayload, ResolvedHitResult, ResolvedHitResult>)FilterHit)
            BossDamageReceiver.HitFilter = null;
        BossDamageReceiver.HitApplied -= HandleHitApplied;
        if (Health != null)
        {
            Health.OnFloorReached -= HandleFloorReached;
            Health.OnDied -= HandleDied;
        }
        if (Break != null)
            Break.OnBreakEnter.RemoveListener(HandleBreakEnter);
        MusicDirector.SetBossLayerOverride(-1f);
        RestoreLights();
    }

    // ======================================================================= arena

    void MeasureArena()
    {
        Vector3 origin = Boss.transform.position + Vector3.up * 1.5f;
        ArenaCenter = Boss.transform.position;
        var dists = new List<float>(24);
        Vector3 sum = Vector3.zero;
        int hits = 0;
        for (int i = 0; i < 24; i++)
        {
            Vector3 dir = Quaternion.Euler(0f, i * 15f, 0f) * Vector3.forward;
            if (Physics.Raycast(origin, dir, out RaycastHit hit, 40f, ~0, QueryTriggerInteraction.Ignore) && !IsCharacter(hit.collider))
            {
                dists.Add(hit.distance);
                sum += hit.point;
                hits++;
            }
        }
        if (hits >= 8)
        {
            dists.Sort();
            ArenaRadius = Mathf.Clamp(dists[dists.Count / 2] - 1.2f, 8f, 24f);
            Vector3 c = sum / hits;
            c.y = Boss.transform.position.y;
            // 벽 평균점이 너무 치우치면(문·복도) 보스 위치 쪽으로 섞는다
            ArenaCenter = Vector3.Lerp(Boss.transform.position, c, 0.5f);
        }
    }

    bool IsCharacter(Collider c)
    {
        if (c == null)
            return true;
        return c.GetComponentInParent<BossController>() != null || c.GetComponentInParent<PlayerReferences>() != null || c.GetComponentInParent<EnemyBrain>() != null;
    }

    void ResolveAnchors()
    {
        Animator humanoid = null;
        foreach (Animator a in Boss.GetComponentsInChildren<Animator>(true))
            if (a != null && a.isHuman) { humanoid = a; break; }
        _head = humanoid != null ? humanoid.GetBoneTransform(HumanBodyBones.Head) : null;
        GameObject core = Armor != null ? Armor.FindPart("ChestCore") : null;
        CoreAnchor = core != null ? core.transform : humanoid != null ? humanoid.GetBoneTransform(HumanBodyBones.Chest) : Boss.transform;
    }

    Transform ResolvePlayer()
    {
        if (_player == null)
        {
            PlayerReferences refs = GameplaySceneCache.ResolvePlayerReferences();
            _player = refs != null ? refs.PlayerRoot : null;
        }
        return _player;
    }

    // ======================================================================= update / sequencing

    bool BossFree =>
        Boss != null && !_dead && !Boss.IsDeadForOverhaul && !Boss.IsUltimateVictimForOverhaul && !Boss.IsOverhaulHeld &&
        !Boss.IsExternalIntroPaused && !ExecutionDirector.IsExecuting && !CinematicDirector.IsPlaying &&
        (Break == null || !Break.IsInBreak) && !Boss.IsOverhaulReactionActive && !_transitionActive;

    void Update()
    {
        if (Boss == null || Health == null || _dead)
            return;

        float hp = Boss.OverhaulHpNormalized;
        if (BossFree && Boss.ActiveSpecialPattern == BossSpecialPatternKind.None)
        {
            if (!_lockdownStarted && hp <= lockdownHp + 0.0005f)
                StartLockdown();
            else if (_lockdownDone && !_purgeDone && hp <= phaseTwoHp + 0.0005f)
                StartCoroutine(Co_Purge());
            else if (_purgeDone && !_partsUnlocked && hp <= partsHp + 0.0005f)
                UnlockParts();
            else if (_partsUnlocked && !_overheatDone && hp <= phaseThreeHp + 0.0005f)
                StartOverheat();
        }

        if (_overheatDone && !_desperationDone && !_desperationActive && hp <= desperationHp + 0.0005f)
        {
            if (!_desperationRequested || Time.time - _desperationRequestedAt > 8f)
            {
                _desperationRequested = true;
                _desperationRequestedAt = Time.time;
                Boss.RequestOverhaulDesperation();
            }
        }

        // HP 바닥(1) 도달 → 최종 처형 (OnFloorReached 는 HP 갱신 전에 오므로 여기서 판정)
        if (_desperationDone && _stage != Stage.Final && Health.CurrentHP <= Mathf.Max(1, Health.HpFloor))
            EnterFinal();

        if (_stage == Stage.Final)
            TickFinal();
        else if (Break != null && Break.IsInBreak && _stage == Stage.Overdrive && Time.time >= _nextBreakKeepAliveAt)
        {
            // 임계 폭주 생존 보상: 확정 처형 창 (overdriveBreakSeconds 동안 브레이크 유지)
        }

        // 봉쇄 중 보스는 코어를 지키며 공세를 늦춘다 (노드를 부술 틈): 4.0 s 마다 3.3 s 공격 선택 보류 (iter18: 2.4 → 3.3)
        if (_stage == Stage.Lockdown && Time.time >= _nextLockdownThrottleAt && !Boss.IsAttackStateActive)
        {
            _nextLockdownThrottleAt = Time.time + 4.0f;
            Boss.ThrottleAttacksForOverhaul(3.3f);
        }
        // 반복 18: 봉쇄 중 보스 근접 공격력 축소 — 기믹은 "노드를 부숴라"로 읽혀야 한다 (QA: 0.9 패배 대부분이 봉쇄 중 연속기 사망)
        float dmgScale = _stage == Stage.Lockdown ? lockdownBossDamageScale : 1f;
        if (!Mathf.Approximately(Boss.OverhaulDamageScale, dmgScale))
            Boss.SetOverhaulDamageScale(dmgScale);

        TickLights();
        DownLoopWatchdog();
    }

    void DownLoopWatchdog()
    {
        if (Boss.IsOverhaulReactionActive || Boss.bossAnimator == null || ExecutionDirector.IsExecuting)
            return;
        // 반복 22: 전환 중에는 '현재 상태'가 여전히 DownLoop 로 보고된다. 여기서 매 프레임 CrossFade 를 다시 걸면
        // 전환이 매번 처음부터 재시작돼 영원히 끝나지 않는다 (QA I21 b_0.9_6: 50 s 동안 Falldown_F_End 전환에 고정,
        // 이후 공격 트리거까지 전부 덮어써 패턴·큐만 돌고 휘두르기/히트박스가 없었다). 전환이 없을 때만 한 번 건다.
        if (Boss.bossAnimator.IsInTransition(0))
            return;
        AnimatorStateInfo s = Boss.bossAnimator.GetCurrentAnimatorStateInfo(0);
        if (s.IsName(BossController.OverhaulStateDownLoop) && !(Break != null && Break.IsInBreak))
            Boss.PlayOverhaulState(BossController.OverhaulStateGetUp, 0.15f);
    }

    void HandleFloorReached(int blocked)
    {
        if (_dead || Boss == null)
            return;
        if (Lockdown != null && Lockdown.IsActive)
            Lockdown.NotifyShieldHit();
    }

    void HandleDied()
    {
        _dead = true;
        _stage = Stage.Dead;
        MusicDirector.SetBossLayerOverride(-1f);
        SetLightTarget(LightMood.Base, 1.5f);
        if (Lockdown != null)
            Lockdown.ForceEnd();
    }

    void HandleBreakEnter()
    {
        if (_dead)
            return;
        CombatBarkDirector.TryBark("oh.execute_ready", false);
    }

    // ======================================================================= hits

    ResolvedHitResult FilterHit(BossDamageReceiver receiver, HitPayload payload, ResolvedHitResult hit)
    {
        if (receiver == null || receiver.bossController != Boss || !hit.IsDamage)
            return hit;
        float m = 1f;
        if (IsOverloaded)
            m *= overloadDamageMultiplier;
        if (IsBlinkWindowOpen)
            m *= blinkDamageMultiplier;
        if (Parts != null)
            m *= Parts.ResolveDamageMultiplier(payload);
        if (Mathf.Approximately(m, 1f))
            return hit;
        return ResolvedHitResult.Damage(hit.HealthDamage * m, hit.HitType, hit.BreakBonus);
    }

    void HandleHitApplied(BossDamageReceiver receiver, HitPayload payload, ResolvedHitResult hit, int hpLost)
    {
        if (receiver == null || receiver.bossController != Boss || !hit.IsDamage || _dead)
            return;

        Vector3 dir = payload.attacker != null ? Boss.transform.position - payload.attacker.position : payload.hitDirection;
        float impactMul = IsBlinkWindowOpen ? 1.5f : 1f;
        if (Parts != null)
            impactMul *= Parts.ResolveImpactMultiplier(payload);
        Impact.AddImpact(Impact.ResolveHitImpact(payload) * impactMul, dir);

        if (hpLost > 0 && Break != null && !Break.IsInBreak)
        {
            float extra = BalanceMultiplier * (Parts != null ? Parts.ResolveBalanceMultiplier(payload) : 1f) - 1f;
            if (extra > 0.01f)
                Break.AddBreak(Break.basicAttackBreakDamage * extra, BossBreakController.BreakSource.Generic);
        }

        if (Parts != null)
            Parts.NotifyHit(payload, hpLost);
    }

    /// <summary>PlayerOverhaulReactions: 보스 공격을 패링함 (chain = 연쇄 번째, 1부터).</summary>
    public void NotifyParry(int chain, Transform player)
    {
        if (_dead || Boss == null)
            return;
        Vector3 dir = player != null ? Boss.transform.position - player.position : -Boss.transform.forward;
        if (Parts != null)
            Parts.NotifyParry();
        if (_overheatDone && Break != null && !Break.IsInBreak)
            Break.AddBreak(Break.baseBreakPerHit * (overheatBalanceMultiplier - 1f), BossBreakController.BreakSource.Parry);
        Impact.AddImpact(chain >= 2 ? 10f : 8f, dir);
    }

    /// <summary>강패링: 연쇄 3회째 패링. 넉백 + 큰 균형 피해.</summary>
    public void NotifyStrongParry(Transform player)
    {
        if (_dead || Boss == null)
            return;
        QaStrongParries++;
        QaLastEvent = "strongParry";
        Vector3 dir = player != null ? Boss.transform.position - player.position : -Boss.transform.forward;
        if (Break != null && !Break.IsInBreak)
            Break.AddBreak(strongParryBalance * BalanceMultiplier, BossBreakController.BreakSource.Parry);
        if (Break == null || !Break.IsInBreak)
            Impact.AddImpact(60f, dir, BossOverhaulReaction.Knockback);
        CombatBarkDirector.TryBark("oh.strong_parry", false);
    }

    /// <summary>블링크: 자홍 공격을 퍼펙트 회피해 등 뒤로 순간이동. 반격 창을 연다.</summary>
    public void NotifyBlink()
    {
        if (_dead || Boss == null)
            return;
        QaBlinks++;
        QaLastEvent = "blink";
        _blinkUntil = Time.time + blinkWindow;
        Boss.OpenOverhaulPunishWindow("BLINK", blinkWindow, 1.25f, 2.0f);
        CombatBarkDirector.TryBark("oh.blink", false);
    }

    public void NotifyWallSplat(Vector3 point)
    {
        QaLastEvent = "wallSplat";
    }

    // ======================================================================= stages

    void StartLockdown()
    {
        _lockdownStarted = true;
        _stage = Stage.Lockdown;
        QaLockdowns++;
        QaLastEvent = "lockdown";
        QaLastPhaseChangeAt = Time.time;
        PhaseChangeFx("봉쇄 프로토콜", "oh.lockdown", LightMood.Alarm, 0.6f);
        Lockdown.Begin();
    }

    /// <summary>BossLockdownGimmick: 전원 노드 3개 파괴.</summary>
    public void NotifyNodesDestroyed()
    {
        _lockdownDone = true;
        _stage = Stage.Phase1b;
        QaOverloads++;
        QaLastEvent = "overload";
        Health.HpFloor = Mathf.CeilToInt(Health.MaxHP * desperationHp);
        _overloadUntil = Time.time + overloadDuration;
        Boss.TryBeginOverhaulReaction(BossOverhaulReaction.Overload, overloadDuration, Vector3.zero, 0.1f);
        Hud.ShowBanner("과부하", 1.4f);
        CombatBarkDirector.TryBark("oh.overload", true);
        ActionColourDirector.PulseAt(CoreAnchor != null ? CoreAnchor.position : Boss.transform.position, 0.8f, 0.5f);
        SetLightTarget(LightMood.Base, 1.2f);
        MusicDirector.SetBossLayerOverride(-1f);
        StartCoroutine(Co_EndLockdownAfter(overloadDuration));
    }

    IEnumerator Co_EndLockdownAfter(float seconds)
    {
        yield return new WaitForSeconds(seconds);
        Lockdown.End();
    }

    IEnumerator Co_Purge()
    {
        _purgeDone = true;
        _transitionActive = true;
        _stage = Stage.Phase2;
        QaPurges++;
        QaLastEvent = "purge";
        QaLastPhaseChangeAt = Time.time;
        Transform player = ResolvePlayer();
        bool cut = QaForceTransitionCut || !MainSceneArrivalController.LastLoadWasRetry;
        IInputBlocker blocker = player != null ? player.GetComponent<IInputBlocker>() : null;
        PlayerHealth playerHealth = player != null ? player.GetComponentInChildren<PlayerHealth>() : null;
        if (playerHealth != null)
            playerHealth.SetInvincibleRealtime(purgeDuration + 0.5f);
        if (cut && blocker != null)
            blocker.BlockAll(true);

        Boss.TryBeginOverhaulReaction(BossOverhaulReaction.Transition, purgeDuration, Vector3.zero, 0.1f);
        if (cut)
            ExecutionCameraShot.BeginTransition(this, Boss.transform, CoreAnchor, player, purgeDuration - 0.2f);
        PhaseChangeFx("장갑 해제", "oh.purge", LightMood.Phase2, -1f);

        yield return new WaitForSeconds(0.55f);
        if (Armor != null)
        {
            CharacterController cc = player != null ? player.GetComponentInChildren<CharacterController>() : null;
            if (cc != null && !_ignoreForDebris.Contains(cc))
                _ignoreForDebris.Add(cc);
            Armor.PurgePlates(purgePlates, CoreAnchor != null ? CoreAnchor.position : Boss.transform.position + Vector3.up * 1.4f, 6.5f, 9f, _ignoreForDebris);
        }
        TypeDOverhaulAssets a = TypeDOverhaulAssets.Load();
        if (a != null)
        {
            TypeDOverhaulAssets.PlayAt(a.armourPurge, Boss.transform.position + Vector3.up * 1.4f, 1f, 1f);
            if (a.steamVfx != null) TransientVfxPool.Spawn(a.steamVfx, Boss.transform.position + Vector3.up * 1.5f, Quaternion.identity, null, 1.6f);
        }
        CameraShake cs = GameplaySceneCache.ResolveMainCameraShake();
        if (cs != null)
            cs.Shake(0.35f, 0.35f);

        yield return new WaitForSeconds(Mathf.Max(0.1f, purgeDuration - 0.75f));
        ExecutionCameraShot.End(this);
        if (cut && blocker != null && (blocker as Object) != null)
            blocker.BlockAll(false);
        _transitionActive = false;
    }

    void UnlockParts()
    {
        _partsUnlocked = true;
        QaLastEvent = "parts";
        QaLastPhaseChangeAt = Time.time;
        Parts.Unlock();
        PhaseChangeFx("부위 파괴", "oh.parts", LightMood.Phase2, -1f, pulse: false);
    }

    void StartOverheat()
    {
        _overheatDone = true;
        _stage = Stage.Phase3;
        QaLastEvent = "overheat";
        QaLastPhaseChangeAt = Time.time;
        _ventFirstAt = Time.time + ventFirstDelay;
        if (Armor != null)
            Armor.SetCoreOverheat(1f);
        PhaseChangeFx("과열", "oh.overheat", LightMood.Overheat, -1f);
    }

    public void NotifyGrabWindup(float lead)
    {
        TypeDOverhaulAssets a = TypeDOverhaulAssets.Load();
        if (a != null) TypeDOverhaulAssets.PlayAt(a.grabWhoosh, Boss.transform.position + Vector3.up * 1.4f, 0.75f, 1f);
    }

    public void NotifyGrabResolved(bool caught) => QaLastEvent = caught ? "grabHit" : "grabMiss";

    public void NotifyVentCharge(float lead)
    {
        TypeDOverhaulAssets a = TypeDOverhaulAssets.Load();
        if (a != null) TypeDOverhaulAssets.PlayAt(a.ventCharge, Boss.transform.position + Vector3.up * 1.4f, 0.9f, 1f);
    }

    public void NotifyVentAborted() { }

    public void NotifyVentBlast(Vector3 center, float radius)
    {
        TypeDOverhaulAssets a = TypeDOverhaulAssets.Load();
        if (a != null)
        {
            TypeDOverhaulAssets.PlayAt(a.ventBlast, center + Vector3.up, 1f, 1f);
            if (a.steamVfx != null) TransientVfxPool.Spawn(a.steamVfx, center + Vector3.up * 1.2f, Quaternion.identity, null, 2f);
        }
        QaLastEvent = "vent";
    }

    public void NotifyVentStall(float seconds)
    {
        _ventStallUntil = Time.time + seconds;
        if (Parts != null)
            Parts.SetExposed(true);
    }

    public void NotifyVentStallEnded()
    {
        _ventStallUntil = 0f;
        if (Parts != null)
            Parts.SetExposed(false);
    }

    public void NotifyDesperationBegin()
    {
        _desperationActive = true;
        _stage = Stage.Desperation;
        QaLastEvent = "desperation";
        QaLastPhaseChangeAt = Time.time;
        PhaseChangeFx("임계 폭주", "oh.desperation", LightMood.Desperation, 1f);
        Lockdown.RaiseCover(ArenaCenter, ArenaRadius, ResolvePlayer());
    }

    public void NotifyDesperationCharge(float lead)
    {
        TypeDOverhaulAssets a = TypeDOverhaulAssets.Load();
        if (a != null) TypeDOverhaulAssets.PlayAt(a.despCharge, Boss.transform.position + Vector3.up * 1.4f, 1f, 1f);
    }

    public bool IsPlayerCoveredFrom(Vector3 origin)
    {
        Transform p = ResolvePlayer();
        return p != null && Lockdown != null && Lockdown.IsCovered(origin, p.position + Vector3.up * 1.1f);
    }

    public void NotifyDesperationResolved(bool completed, bool survived)
    {
        _desperationActive = false;
        _desperationRequested = false;
        if (!completed)
        {
            Lockdown.LowerCover(0.5f);
            _stage = Stage.Phase3;
            return;
        }
        _desperationDone = true;
        Health.HpFloor = 1;
        MusicDirector.SetBossLayerOverride(-1f);
        TypeDOverhaulAssets a = TypeDOverhaulAssets.Load();
        if (a != null) TypeDOverhaulAssets.PlayAt(a.despBlast, Boss.transform.position + Vector3.up, 1f, 1f);
        ActionColourDirector.PulseAt(Boss.transform.position + Vector3.up * 1.4f, 1f, 0.7f);
        Lockdown.LowerCover(2.5f);
        SetLightTarget(LightMood.Overheat, 1.5f);
        PlayerHealth ph = ResolvePlayer() != null ? ResolvePlayer().GetComponentInChildren<PlayerHealth>() : null;
        if (survived && (ph == null || !ph.IsDead))
        {
            _stage = Stage.Overdrive;
            QaLastEvent = "overdrive";
            StartCoroutine(Co_OverdriveBreak());
        }
        else
            _stage = Stage.Phase3;
    }

    IEnumerator Co_OverdriveBreak()
    {
        // 임계 폭주 생존 → 과부하 → 확정 처형
        yield return new WaitForSeconds(0.25f);
        if (_dead || Break == null)
            yield break;
        Hud.ShowBanner("과부하 — 처형", 1.4f);
        CombatBarkDirector.TryBark("oh.overdrive", true);
        Break.ForceEnterBreak();
        Break.RestoreStateAfterPresentation(0f, true, overdriveBreakSeconds);
        yield return new WaitForSeconds(overdriveBreakSeconds + 0.5f);
        if (_stage == Stage.Overdrive)
            _stage = Stage.Phase3;
    }

    void EnterFinal()
    {
        _stage = Stage.Final;
        QaFinalBreaks++;
        QaLastEvent = "final";
        QaLastPhaseChangeAt = Time.time;
        _finalSince = Time.time;
        if (Lockdown != null)
            Lockdown.ForceEnd();
        Hud.ShowBanner("최종 처형", 1.6f);
        CombatBarkDirector.TryBark("oh.final", true);
        ActionColourDirector.NotifyBossPhaseChange(CoreAnchor != null ? CoreAnchor.position : Boss.transform.position);
        StartCoroutine(Co_EnterFinalBreak());
    }

    IEnumerator Co_EnterFinalBreak()
    {
        // 진행 중 반응/특수 패턴/궁극기가 끝난 뒤 쓰러진다
        float wait = 0f;
        while (wait < 6f && (Boss.IsUltimateVictimForOverhaul || Boss.IsOverhaulReactionActive || Boss.ActiveSpecialPattern != BossSpecialPatternKind.None))
        {
            wait += Time.deltaTime;
            yield return null;
        }
        if (_dead || Break == null)
            yield break;
        if (!Break.IsInBreak)
            Break.ForceEnterBreak();
        Break.RestoreStateAfterPresentation(0f, true, 5f);
        _nextBreakKeepAliveAt = Time.time + 1f;
    }

    void TickFinal()
    {
        if (Break != null && Time.time >= _nextBreakKeepAliveAt && !ExecutionDirector.IsExecuting)
        {
            _nextBreakKeepAliveAt = Time.time + 1f;
            if (!Break.IsInBreak)
                Break.ForceEnterBreak();
            Break.RestoreStateAfterPresentation(0f, true, 5f);
        }
        if (Time.time - _finalSince > finalBreakAutoSeconds && !ExecutionDirector.IsExecuting)
            KillNow();
    }

    void KillNow()
    {
        if (_dead || Health == null)
            return;
        Health.HpFloor = 0;
        Health.Kill();
    }

    // ======================================================================= execution hooks (ExecutionDirector)

    public void BeginExecution(ExecutionKind kind, object owner)
    {
        Boss.BeginOverhaulHold(owner);
        if (Break != null)
            Break.RestoreStateAfterPresentation(0f, true, 6f);
        if (Health != null)
            Health.SetInvincible(0f);
    }

    public void ApplyExecutionStrike(ExecutionKind kind)
    {
        if (Health == null || _dead)
            return;
        if (kind == ExecutionKind.BossFinal)
        {
            StartCoroutine(Co_FinalKill());
            return;
        }
        float pct = kind == ExecutionKind.BossBack ? backExecutionPercent : frontExecutionPercent;
        int damage = Mathf.Max(1, Mathf.RoundToInt(Health.MaxHP * pct));
        Vector3 point = CoreAnchor != null ? CoreAnchor.position : Boss.transform.position + Vector3.up * 1.4f;
        Health.TakeDamage(damage, HitType.Force, point);
        CombatRewardUtility.TryGrantBasicAttackGauge(ResolvePlayer());
    }

    IEnumerator Co_FinalKill()
    {
        // 코어 관통 → 화면 베기 분리(약 0.9 s) 뒤 사망 → 기존 사망·엔딩 흐름
        if (Armor != null)
            Armor.SetCoreOverheat(1f);
        yield return new WaitForSecondsRealtime(0.95f);
        KillNow();
    }

    public void EndExecution(ExecutionKind kind, object owner)
    {
        if (Boss == null)
            return;
        if (kind == ExecutionKind.BossFinal || _dead)
        {
            Boss.EndOverhaulHold(owner);
            return;
        }
        Boss.EndOverhaulHold(owner);
        if (Break != null && Break.IsInBreak)
            Break.ForceExitBreak();
        if (_stage == Stage.Overdrive)
            _stage = Stage.Phase3;
    }

    // ======================================================================= presentation

    void PhaseChangeFx(string banner, string bark, LightMood mood, float musicLayer, bool pulse = true)
    {
        Hud.ShowBanner(banner, 1.2f);
        Hud.RefreshTicks();
        CombatBarkDirector.TryBark(bark, true);
        if (pulse)
            ActionColourDirector.NotifyBossPhaseChange(CoreAnchor != null ? CoreAnchor.position : Boss.transform.position + Vector3.up * 1.4f);
        SetLightTarget(mood, 1.2f);
        MusicDirector.SetBossLayerOverride(musicLayer);
        if (Armor != null)
            Armor.PulsePhaseChange();
    }

    public enum LightMood { Base, Alarm, Phase2, Overheat, Desperation }

    struct LitEntry
    {
        public Light Light;
        public Color BaseColor;
        public float BaseIntensity;
    }

    readonly List<LitEntry> _lights = new List<LitEntry>();
    LightMood _mood = LightMood.Base;
    float _moodBlend = 0f;
    float _moodSpeed = 1f;

    void CacheArenaLights()
    {
        _lights.Clear();
        foreach (Light l in FindObjectsOfType<Light>())
        {
            if (l == null || l.type == LightType.Directional)
                continue;
            if (l.GetComponentInParent<BossController>() != null || l.GetComponentInParent<PlayerReferences>() != null)
                continue;
            Vector3 d = l.transform.position - ArenaCenter;
            if (Mathf.Abs(d.y) > 18f)
                continue;
            d.y = 0f;
            if (d.magnitude > ArenaRadius * 1.8f + 6f)
                continue;
            _lights.Add(new LitEntry { Light = l, BaseColor = l.color, BaseIntensity = l.intensity });
            if (_lights.Count >= 48)
                break;
        }
    }

    void SetLightTarget(LightMood mood, float seconds)
    {
        _mood = mood;
        _moodSpeed = 1f / Mathf.Max(0.1f, seconds);
    }

    void TickLights()
    {
        if (_lights.Count == 0)
            return;
        float target = _mood == LightMood.Base ? 0f : 1f;
        _moodBlend = Mathf.MoveTowards(_moodBlend, target, Time.deltaTime * _moodSpeed);
        if (_moodBlend <= 0.0001f && target <= 0f && _lastAppliedBlend <= 0.0001f)
            return;
        Color tint;
        float mul;
        float t = Time.time;
        switch (_mood)
        {
            case LightMood.Alarm:
                // 봉쇄: 1.1 Hz 레드 경보 맥동 (깜빡임이 아니라 부드러운 맥동, 섬광 옵션과 무관한 수준)
                tint = new Color(1f, 0.16f, 0.12f);
                mul = 0.75f + 0.35f * (0.5f + 0.5f * Mathf.Sin(t * Mathf.PI * 2.2f));
                break;
            case LightMood.Phase2:
                tint = new Color(1f, 0.55f, 0.5f);
                mul = 1f;
                break;
            case LightMood.Overheat:
                tint = new Color(1f, 0.72f, 0.58f);
                mul = 1.12f;
                break;
            case LightMood.Desperation:
                tint = new Color(1f, 0.2f, 0.18f);
                mul = 0.55f + 0.2f * (0.5f + 0.5f * Mathf.Sin(t * Mathf.PI * 3f));
                break;
            default:
                tint = Color.white;
                mul = 1f;
                break;
        }
        float k = _mood == LightMood.Base ? 0f : _moodBlend;
        for (int i = 0; i < _lights.Count; i++)
        {
            LitEntry e = _lights[i];
            if (e.Light == null)
                continue;
            float strength = _mood == LightMood.Phase2 ? 0.35f : _mood == LightMood.Overheat ? 0.4f : 0.75f;
            e.Light.color = Color.Lerp(e.BaseColor, e.BaseColor * tint, k * strength);
            e.Light.intensity = Mathf.Lerp(e.BaseIntensity, e.BaseIntensity * mul, k);
        }
        _lastAppliedBlend = k;
    }

    float _lastAppliedBlend;

    void RestoreLights()
    {
        for (int i = 0; i < _lights.Count; i++)
        {
            LitEntry e = _lights[i];
            if (e.Light == null)
                continue;
            e.Light.color = e.BaseColor;
            e.Light.intensity = e.BaseIntensity;
        }
    }

    /// <summary>QA/스크린샷: 단계를 바로 시작 (HP 를 그 문턱으로 맞춘 뒤).</summary>
    public void QaJumpToHp(float normalized)
    {
        if (Health == null)
            return;
        int target = Mathf.Clamp(Mathf.RoundToInt(Health.MaxHP * normalized), 1, Health.MaxHP);
        int prevFloor = Health.HpFloor;
        Health.HpFloor = 0;
        if (Health.CurrentHP > target)
            Health.TakeDamage(Health.CurrentHP - target, HitType.Force, Boss.transform.position);
        Health.HpFloor = Mathf.Min(prevFloor, target);
        if (normalized < lockdownHp - 0.001f && !_lockdownDone)
        {
            _lockdownStarted = true;
            _lockdownDone = true;
            Lockdown.ForceEnd();
            Health.HpFloor = Mathf.Min(Mathf.CeilToInt(Health.MaxHP * desperationHp), target);
        }
        if (normalized < desperationHp - 0.001f)
        {
            _purgeDone = _partsUnlocked = _overheatDone = _desperationDone = true;
            Health.HpFloor = 1;
        }
    }
}
