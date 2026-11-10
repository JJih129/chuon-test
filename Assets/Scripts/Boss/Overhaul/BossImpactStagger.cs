// 반복 16: 보스 충격 누적 → 4단계 반응 (움찔 → 경직 → 넉백(2–4 m, 벽 박힘) → 다운). Docs/Boss_TypeD_Phases.md §2
// 강공·콤보 끝타·패링 연쇄가 충격을 크게 쌓는다. 단계마다 히트스톱·카메라 임펄스가 커진다.
using System;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class BossImpactStagger : MonoBehaviour
{
    [Header("Impact per hit")]
    [SerializeField] float lightImpact = 5f;
    [SerializeField] float strongImpact = 10f;
    [SerializeField] float heavyImpact = 14f;
    [SerializeField] float comboFinisherMultiplier = 1.35f;
    [SerializeField] float punishWindowMultiplier = 1.2f;
    [SerializeField] float decayDelay = 1.0f;
    [SerializeField] float decayPerSecond = 12f;

    [Header("Tier thresholds")]
    [SerializeField] float flinchThreshold = 24f;
    [SerializeField] float staggerThreshold = 55f;
    [SerializeField] float knockbackThreshold = 90f;
    [SerializeField] float downThreshold = 130f;

    [Header("Tier durations / cooldowns")]
    [SerializeField] float flinchDuration = 0.45f;
    [SerializeField] float staggerDuration = 0.9f;
    [SerializeField] float knockbackDuration = 1.15f;
    [SerializeField] float knockbackPushTime = 0.38f;
    [SerializeField] float knockbackMinDistance = 2f;
    [SerializeField] float knockbackMaxDistance = 4f;
    [SerializeField] float wallSplatStun = 1.2f;
    [SerializeField] float downDuration = 2.8f;
    [SerializeField] float flinchCooldown = 1.5f;
    [SerializeField] float staggerCooldown = 4f;
    [SerializeField] float knockbackCooldown = 7f;
    [SerializeField] float downCooldown = 14f;

    public float Impact { get; private set; }
    public float Normalized => Mathf.Clamp01(Impact / downThreshold);
    public int QaFlinches { get; private set; }
    public int QaStaggers { get; private set; }
    public int QaKnockbacks { get; private set; }
    public int QaDowns { get; private set; }
    public int QaWallSplats { get; private set; }
    public float LastKnockbackDistance { get; private set; }

    public static event Action<BossController, BossOverhaulReaction, Vector3> ReactionStarted;

    BossController _boss;
    TypeDOverhaul _overhaul;
    float _lastImpactAt = float.NegativeInfinity;
    float _flinchReadyAt, _staggerReadyAt, _knockReadyAt, _downReadyAt;
    Coroutine _hitStop;
    float _lastHitStopAt = float.NegativeInfinity;

    public void Bind(BossController boss, TypeDOverhaul overhaul)
    {
        _boss = boss;
        _overhaul = overhaul;
    }

    void Update()
    {
        if (Impact <= 0f)
            return;
        if (Time.time - _lastImpactAt < decayDelay)
            return;
        Impact = Mathf.Max(0f, Impact - decayPerSecond * Time.deltaTime);
    }

    void OnDisable()
    {
        CombatFeelRuntimeUtility.ForceRestoreActiveHitStop(this, ref _hitStop);
    }

    public float ResolveHitImpact(HitPayload payload)
    {
        float v;
        switch (payload.hitType)
        {
            case HitType.Heavy:
            case HitType.Force:
                v = heavyImpact;
                break;
            case HitType.Strong:
            case HitType.Parried:
                v = strongImpact;
                break;
            default:
                v = lightImpact;
                break;
        }

        PlayerCombatController combat = payload.attacker != null ? payload.attacker.GetComponentInParent<PlayerCombatController>() : null;
        if (combat != null && combat.CurrentComboDepth >= 3)
            v *= comboFinisherMultiplier;
        if (_boss != null && _boss.IsPunishWindowActive)
            v *= punishWindowMultiplier;
        return v;
    }

    /// <summary>충격 추가. fromAttackerDir = 공격자 → 보스 방향(밀리는 방향). minTier 를 주면 그 단계 이상을 보장(강패링 = 넉백).</summary>
    public void AddImpact(float amount, Vector3 fromAttackerDir, BossOverhaulReaction minTier = BossOverhaulReaction.None)
    {
        if (_boss == null || amount <= 0f && minTier == BossOverhaulReaction.None)
            return;
        Impact += Mathf.Max(0f, amount);
        _lastImpactAt = Time.time;
        if (minTier == BossOverhaulReaction.Knockback)
            Impact = Mathf.Max(Impact, knockbackThreshold + 2f);
        else if (minTier == BossOverhaulReaction.Down)
            Impact = Mathf.Max(Impact, downThreshold + 2f);
        Evaluate(fromAttackerDir, minTier);
    }

    void Evaluate(Vector3 dir, BossOverhaulReaction minTier)
    {
        if (_overhaul != null && !_overhaul.ImpactReactionsAllowed)
            return;
        float now = Time.time;
        bool forced = minTier >= BossOverhaulReaction.Knockback;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f)
            dir = -_boss.transform.forward;
        dir.Normalize();

        if (Impact >= downThreshold && (now >= _downReadyAt || minTier == BossOverhaulReaction.Down))
        {
            if (Begin(BossOverhaulReaction.Down, downDuration, dir * 0.8f, 0.3f))
            {
                _downReadyAt = now + downCooldown;
                _knockReadyAt = Mathf.Max(_knockReadyAt, now + 3f);
                Impact = 0f;
                QaDowns++;
                Feel(0.05f, 0.12f, 0.42f, 0.24f, true);
                TypeDOverhaulAssets a = TypeDOverhaulAssets.Load();
                if (a != null) TypeDOverhaulAssets.PlayAt(a.impactHeavy, _boss.transform.position + Vector3.up, 1f, 0.82f);
            }
            return;
        }

        if (Impact >= knockbackThreshold && (now >= _knockReadyAt || forced))
        {
            float k = Mathf.Clamp01((Impact - knockbackThreshold) / Mathf.Max(1f, downThreshold - knockbackThreshold));
            float distance = Mathf.Lerp(knockbackMinDistance, knockbackMaxDistance, forced ? Mathf.Max(0.5f, k) : k);
            if (Begin(BossOverhaulReaction.Knockback, knockbackDuration, dir * distance, knockbackPushTime))
            {
                LastKnockbackDistance = distance;
                _knockReadyAt = now + knockbackCooldown;
                _staggerReadyAt = Mathf.Max(_staggerReadyAt, now + 2f);
                Impact = Mathf.Max(0f, Impact - 60f);
                QaKnockbacks++;
                Feel(0.06f, 0.09f, 0.32f, 0.2f, true);
                TypeDOverhaulAssets a = TypeDOverhaulAssets.Load();
                if (a != null) TypeDOverhaulAssets.PlayAt(a.impactHeavy, _boss.transform.position + Vector3.up, 0.9f, 1f);
            }
            return;
        }

        if (Impact >= staggerThreshold && now >= _staggerReadyAt)
        {
            if (Begin(BossOverhaulReaction.Stagger, staggerDuration, dir * 0.35f, 0.18f))
            {
                _staggerReadyAt = now + staggerCooldown;
                Impact = Mathf.Max(0f, Impact - 20f);
                QaStaggers++;
                Feel(0.08f, 0.07f, 0.22f, 0.16f, false);
            }
            return;
        }

        if (Impact >= flinchThreshold && now >= _flinchReadyAt && !_boss.IsAttackStateActive)
        {
            if (Begin(BossOverhaulReaction.Flinch, flinchDuration, dir * 0.15f, 0.12f))
            {
                _flinchReadyAt = now + flinchCooldown;
                QaFlinches++;
                Feel(0.12f, 0.045f, 0.12f, 0.1f, false);
            }
        }
    }

    bool Begin(BossOverhaulReaction kind, float duration, Vector3 push, float pushTime)
    {
        bool ok = _boss.TryBeginOverhaulReaction(kind, duration, push, pushTime, wallSplatStun, HandleWallSplat);
        if (ok)
            ReactionStarted?.Invoke(_boss, kind, push);
        return ok;
    }

    void HandleWallSplat(Vector3 point)
    {
        QaWallSplats++;
        Feel(0.05f, 0.1f, 0.45f, 0.26f, true);
        TypeDOverhaulAssets a = TypeDOverhaulAssets.Load();
        if (a != null)
        {
            TypeDOverhaulAssets.PlayAt(a.wallSplat, point, 1f, 1f);
            Quaternion rot = Quaternion.LookRotation(-_boss.transform.forward, Vector3.up);
            if (a.dustVfx != null) TransientVfxPool.Spawn(a.dustVfx, point, rot, null, 1.4f);
            if (a.sparkVfx != null) TransientVfxPool.Spawn(a.sparkVfx, point + Vector3.up * 0.3f, rot, null, 1.2f);
        }
        ActionColourDirector.PulseAt(point, 0.45f, 0.22f);
        ReactionStarted?.Invoke(_boss, BossOverhaulReaction.WallSplat, Vector3.zero);
        if (_overhaul != null)
            _overhaul.NotifyWallSplat(point);
    }

    void Feel(float timeScale, float stop, float shake, float shakeDuration, bool fovPunch)
    {
        CameraShake cs = GameplaySceneCache.ResolveMainCameraShake();
        if (cs != null)
            cs.Shake(shake, shakeDuration);
        if (fovPunch)
            SoulsCameraRig.NotifyParry();
        if (!CombatFeelRuntimeUtility.IsHitStopActive && GameTimeScale.Effective >= 0.999f)
            CombatFeelRuntimeUtility.StartHitStop(this, ref _hitStop, timeScale, stop, true, 0f, ref _lastHitStopAt);
    }
}
