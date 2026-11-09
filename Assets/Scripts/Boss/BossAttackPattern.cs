// Boss attack data types (moved out of BossController.cs; no logic change).

using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System;

public enum BossState
{
    Recovery = -1,
    IntroIdle,   // 전투 시작 전 연출용 대기
    Detect,      // 플레이어 거리/상태 체크
    Move,        // 플레이어에게 이동
    Attack,      // 패턴 수행 중
    CombatIdle,  // 공격 후 잠깐 휴지
    Break,       // 브레이크(그로기)
    Dead         // 사망
}

public enum AttackTelegraphType
{
    Auto = 0,
    Parry = 1,
    Guard = 2,
    Dodge = 3,
    Danger = 4
}

public enum AttackTimingStyle
{
    Auto = 0,
    Standard = 1,
    Delayed = 2,
    FakeOut = 3
}

[System.Serializable]
public class AttackPattern
{
    [Header("기본 정보 (한글 설명)")]
    [Tooltip("패턴 이름(디버그/금지 패턴 조건에 사용)")]
    public string patternName;

    [Tooltip("애니메이터 트리거/스테이트 이름 (예: Attack_A, Attack_B 등)")]
    public string animTriggerName;

    [Header("Pattern Selector")]
    [Tooltip("Enum 기반 패턴 선택 데이터. None이면 기존 patternName/animTriggerName/range/weight/cooldown 값을 자동 미러링합니다.")]
    public BossPatternData selectorData = BossPatternData.CreateDefault(BossPatternId.None);

    [Header("Phase Modifiers")]
    [Tooltip("현재 페이즈에만 적용되는 패턴 변형값. patternId가 None이면 이 AttackPattern의 selectorData/자동 ID에 적용됩니다.")]
    public BossPhasePatternModifier[] phaseModifiers;

    [Tooltip("이 공격이 패링 가능한 공격인지 여부")]
    public bool isParryable;
    public bool canPerfectDodge = true;
    public bool canGuard = true;
    public bool isUnblockable = false;
    public bool causesGuardBreak = false;

    [Tooltip("이 패턴의 기본 데미지량")]
    public int damageAmount = 10;

    [Header("Telegraph")]
    [Tooltip("Auto면 기존 isParryable 값을 따라가고, 아니면 지정한 대응 타입을 사용합니다.")]
    public AttackTelegraphType telegraphType = AttackTelegraphType.Auto;

    [Tooltip("공격 시작 전에 읽을 수 있도록 주는 선행 신호 시간(초). 0이면 기본값 사용.")]
    public float telegraphLeadTime = 0.18f;

    [Tooltip("HUD에 표시할 라벨. 비워두면 타입 기반 기본 문구를 사용합니다.")]
    public string telegraphLabel;

    [Header("Timing Style")]
    [Tooltip("공격 타이밍 변주 방식. Auto면 대응 타입 기준 기본값 사용.")]
    public AttackTimingStyle timingStyle = AttackTimingStyle.Auto;

    [Tooltip("지연/페인트에 추가로 붙는 홀드 시간. 0 이하면 스타일 기본값 사용.")]
    public float extraHoldDelay = 0f;

    [Tooltip("두 번째 경고가 필요할 때 HUD에 붙는 접미사.")]
    public string secondaryTelegraphSuffix = "HOLD";

    [Header("Pre-Attack Pose")]
    [Tooltip("패링 불가 패턴일 때 선행 자세를 잠깐 잡은 뒤 공격합니다.")]
    public bool usePreAttackPoseWhenNotParryable = false;
    [Tooltip("선행 자세용 Animator 트리거. 비워두면 자세 없이 대기만 합니다.")]
    public string preAttackPoseTriggerName;
    [Tooltip("선행 자세 유지 시간(초). 0이면 사용하지 않습니다.")]
    public float preAttackPoseDuration = 0f;
    [Tooltip("선행 자세 중 HUD에 표시할 라벨. 비워두면 READY를 자동으로 붙입니다.")]
    public string preAttackPoseLabel;

    [Header("쿨타임 / 가중치 / 거리 조건 (한글 설명)")]
    [Tooltip("패턴 사용 후 다시 사용할 때까지의 쿨타임(초)")]
    public float cooldown = 2f;

    [HideInInspector] public float currentCooldown;
    [NonSerialized] public float cooldownReadyAt;

    [Tooltip("패턴 선택 시 랜덤 가중치 (값이 클수록 선택될 확률↑)")]
    public float weight = 1f;

    [Tooltip("플레이어와의 최소 거리 조건 (이보다 가까우면 사용 안 함)")]
    public float minRange = 0f;

    [Tooltip("플레이어와의 최대 거리 조건 (이보다 멀면 사용 안 함)")]
    public float maxRange = 5f;

    [Tooltip("이 패턴 직전에 금지할 패턴 이름 (없으면 빈 문자열)")]
    public string forbiddenAfter;

    [Header("Recovery / Punish")]
    [Tooltip("공격 후 빈틈 유지 시간. 0 이하면 텔레그래프 타입 기본값 사용.")]
    public float recoveryTime = 0f;

    [Tooltip("플레이어가 되받아칠 수 있는 유효 시간. 0 이하면 recoveryTime 사용.")]
    public float punishWindowDuration = 0f;

    [Tooltip("빈틈 시간 동안 받는 피해 배수. 1 이하면 텔레그래프 타입 기본값 사용.")]
    public float punishDamageMultiplier = 1f;

    [Header("Ranged Sword Wave")]
    public bool firesSwordWaveProjectile = false;
    [Min(0f)] public float swordWaveFireDelay = 0.03f;
    [Min(0.1f)] public float swordWaveSpeed = 12f;
    [Min(0.1f)] public float swordWaveLifeTime = 1.4f;
    [Min(0.1f)] public float swordWaveWidth = 1.15f;
    [Min(0.1f)] public float swordWaveHeight = 0.55f;
    [Min(0.2f)] public float swordWaveLength = 1.9f;

    [Header("Hitbox Tuning")]
    public float hitboxExpandedPadding = 0f;

    [Range(0f, 1f)]
    public float hitboxMeshPaddingScale = 0f;

    public float hitboxScanInterval = 0f;
    public float hitboxOneShotWindow = 0f;

    [Header("Follow Up")]
    [Tooltip("근거리에서 다음 공격으로 바로 잇는 연계를 허용")]
    public bool allowFollowUpChain = false;

    [Range(0f, 1f)]
    [Tooltip("연계 시도 확률. 0 이하면 타입 기준 기본값 사용.")]
    public float followUpChance = 0f;

    [Tooltip("이 거리 안에 플레이어가 있으면 연계 후보를 본다. 0 이하면 기본값 사용.")]
    public float followUpMaxDistance = 0f;

    [Tooltip("연계 공격 전 짧은 템포 지연. 0 이하면 기본값 사용.")]
    public float followUpDelay = 0f;

    [Tooltip("비워두면 자동 선택, 값이 있으면 해당 패턴명으로 고정 연계")]
    public string forcedFollowUpPattern = string.Empty;

    [Tooltip("후속타에서 우선적으로 노릴 대응 타입. Auto면 원본 패턴 기준 기본값 사용.")]
    public AttackTelegraphType preferredFollowUpTelegraph = AttackTelegraphType.Auto;

    [Header("Phase")]
    [Tooltip("0이면 자동, 1~3이면 해당 페이즈부터 사용 가능")]
    public int minPhase = 0;

    [Tooltip("0이면 제한 없음, 1~3이면 해당 페이즈까지만 사용 가능")]
    public int maxPhase = 0;

    public bool CanExecute(BossController boss, float distance, string lastPattern)
    {
        if (currentCooldown > 0f) return false;
        if (distance < minRange || distance > maxRange) return false;
        if (boss != null && !IsAvailableInPhase(boss.CurrentPhase)) return false;
        if (boss != null && !boss.IsPatternReliablyInHitRange(this, distance)) return false;

        if (!string.IsNullOrEmpty(forbiddenAfter) &&
            lastPattern.Equals(forbiddenAfter, StringComparison.Ordinal))
            return false;

        if (boss.playerTracker == null) return true;

        // 필요하면 이름 기준으로 세부 조건 추가 가능
        return true;
    }

    public void SyncCooldown(float now)
    {
        if (cooldownReadyAt <= 0f)
        {
            if (currentCooldown < 0f)
                currentCooldown = 0f;
            return;
        }

        currentCooldown = Mathf.Max(0f, cooldownReadyAt - now);
        if (currentCooldown <= 0f)
            cooldownReadyAt = 0f;
    }

    public void StartCooldown(float now)
    {
        currentCooldown = Mathf.Max(0f, cooldown);
        cooldownReadyAt = currentCooldown > 0f ? now + currentCooldown : 0f;
    }

    public void RestoreCooldownAnchor(float now)
    {
        cooldownReadyAt = currentCooldown > 0f ? now + currentCooldown : 0f;
    }

    public AttackTelegraphType ResolveTelegraphType()
    {
        return AttackTelegraphType.Parry;

#pragma warning disable CS0162
        if (telegraphType != AttackTelegraphType.Auto)
            return telegraphType;

        if (isUnblockable || (!canGuard && causesGuardBreak))
            return AttackTelegraphType.Danger;

        if (isParryable)
            return AttackTelegraphType.Parry;

        if (canGuard)
            return AttackTelegraphType.Guard;

        return AttackTelegraphType.Dodge;
#pragma warning restore CS0162
    }

    public float ResolveTelegraphLeadTime()
    {
        return telegraphLeadTime > 0.01f ? telegraphLeadTime : 0.18f;
    }

    public string ResolveTelegraphLabel()
    {
        if (!string.IsNullOrWhiteSpace(telegraphLabel))
            return telegraphLabel.Trim().ToUpperInvariant();

        switch (ResolveTelegraphType())
        {
            case AttackTelegraphType.Parry:
                return "PARRY";
            case AttackTelegraphType.Guard:
                return "GUARD";
            case AttackTelegraphType.Danger:
                return "DANGER";
            case AttackTelegraphType.Dodge:
            default:
                return "DODGE";
        }
    }

    public bool ResolveCanParry()
    {
        return true;
    }

    public bool ResolveCanPerfectDodge()
    {
        return canPerfectDodge;
    }

    public bool ResolveCanGuard()
    {
        return true;
    }

    public bool ResolveIsUnblockable()
    {
        return false;
    }

    public bool ResolveCausesGuardBreak()
    {
        return false;
    }

    public float ResolveRecoveryTime()
    {
        if (recoveryTime > 0.01f)
            return recoveryTime;

        switch (ResolveTelegraphType())
        {
            case AttackTelegraphType.Parry:
                return 0.38f;
            case AttackTelegraphType.Guard:
                return 0.46f;
            case AttackTelegraphType.Dodge:
                return 0.64f;
            case AttackTelegraphType.Danger:
                return 0.92f;
            case AttackTelegraphType.Auto:
            default:
                return 0.42f;
        }
    }

    public float ResolvePunishWindowDuration()
    {
        if (punishWindowDuration > 0.01f)
            return punishWindowDuration;

        return ResolveRecoveryTime();
    }

    public float ResolvePunishDamageMultiplier()
    {
        if (punishDamageMultiplier > 1.01f)
            return punishDamageMultiplier;

        switch (ResolveTelegraphType())
        {
            case AttackTelegraphType.Parry:
                return 1.12f;
            case AttackTelegraphType.Guard:
                return 1.05f;
            case AttackTelegraphType.Dodge:
                return 1.22f;
            case AttackTelegraphType.Danger:
                return 1.35f;
            case AttackTelegraphType.Auto:
            default:
                return 1.10f;
        }
    }

    public bool ResolveFiresSwordWaveProjectile()
    {
        return firesSwordWaveProjectile;
    }

    public float ResolveHitboxExpandedPadding(float fallback)
    {
        return hitboxExpandedPadding > 0.001f ? hitboxExpandedPadding : Mathf.Max(0f, fallback);
    }

    public float ResolveHitboxMeshPaddingScale(float fallback)
    {
        if (hitboxMeshPaddingScale > 0.001f)
            return Mathf.Clamp(hitboxMeshPaddingScale, 0.1f, 1f);

        return Mathf.Clamp(fallback, 0.1f, 1f);
    }

    public float ResolveHitboxScanInterval(float fallback)
    {
        return hitboxScanInterval > 0.001f ? hitboxScanInterval : Mathf.Max(0f, fallback);
    }

    public float ResolveHitboxOneShotWindow(float fallback)
    {
        return hitboxOneShotWindow > 0.001f ? hitboxOneShotWindow : Mathf.Max(0f, fallback);
    }

    public AttackTimingStyle ResolveTimingStyle()
    {
        if (timingStyle != AttackTimingStyle.Auto)
            return timingStyle;

        switch (ResolveTelegraphType())
        {
            case AttackTelegraphType.Danger:
                return AttackTimingStyle.FakeOut;
            case AttackTelegraphType.Dodge:
                return AttackTimingStyle.Delayed;
            case AttackTelegraphType.Parry:
            case AttackTelegraphType.Guard:
            case AttackTelegraphType.Auto:
            default:
                return AttackTimingStyle.Standard;
        }
    }

    public float ResolveExtraHoldDelay()
    {
        if (extraHoldDelay > 0.01f)
            return extraHoldDelay;

        switch (ResolveTimingStyle())
        {
            case AttackTimingStyle.Delayed:
                return 0.14f;
            case AttackTimingStyle.FakeOut:
                return 0.22f;
            case AttackTimingStyle.Standard:
            case AttackTimingStyle.Auto:
            default:
                return 0f;
        }
    }

    public string ResolveSecondaryTelegraphLabel()
    {
        string suffix = string.IsNullOrWhiteSpace(secondaryTelegraphSuffix) ? "HOLD" : secondaryTelegraphSuffix.Trim().ToUpperInvariant();
        return $"{ResolveTelegraphLabel()} {suffix}";
    }

    public bool ResolveUsePreAttackPose()
    {
        return usePreAttackPoseWhenNotParryable
            && !ResolveCanParry()
            && preAttackPoseDuration > 0.01f;
    }

    public float ResolvePreAttackPoseDuration()
    {
        return ResolveUsePreAttackPose() ? Mathf.Max(0f, preAttackPoseDuration) : 0f;
    }

    public string ResolvePreAttackPoseTriggerName()
    {
        return string.IsNullOrWhiteSpace(preAttackPoseTriggerName)
            ? string.Empty
            : preAttackPoseTriggerName.Trim();
    }

    public string ResolvePreAttackPoseLabel()
    {
        if (!string.IsNullOrWhiteSpace(preAttackPoseLabel))
            return preAttackPoseLabel.Trim().ToUpperInvariant();

        return $"{ResolveTelegraphLabel()} READY";
    }

    public bool ResolveCanChainFollowUp()
    {
        if (allowFollowUpChain)
            return true;

        return ResolveTelegraphType() == AttackTelegraphType.Parry
            && damageAmount <= 20
            && maxRange <= 4.5f;
    }

    public float ResolveFollowUpChance()
    {
        if (!ResolveCanChainFollowUp())
            return 0f;

        if (followUpChance > 0.001f)
            return Mathf.Clamp01(followUpChance);

        switch (ResolveTelegraphType())
        {
            case AttackTelegraphType.Parry:
                return 0.42f;
            case AttackTelegraphType.Guard:
                return 0.26f;
            default:
                return 0f;
        }
    }

    public float ResolveFollowUpMaxDistance()
    {
        if (followUpMaxDistance > 0.01f)
            return followUpMaxDistance;

        return 2.85f;
    }

    public float ResolveFollowUpDelay()
    {
        if (followUpDelay > 0.01f)
            return followUpDelay;

        return 0.10f;
    }

    public int ResolveMinPhase()
    {
        if (minPhase > 0)
            return Mathf.Clamp(minPhase, 1, 3);

        switch (ResolveTelegraphType())
        {
            case AttackTelegraphType.Danger:
                return 3;
            case AttackTelegraphType.Dodge:
                return 2;
            case AttackTelegraphType.Guard:
                return damageAmount >= 20 ? 2 : 1;
            default:
                return 1;
        }
    }

    public int ResolveMaxPhase()
    {
        if (maxPhase > 0)
            return Mathf.Clamp(Mathf.Max(ResolveMinPhase(), maxPhase), 1, 3);

        return 3;
    }

    public bool IsAvailableInPhase(int phase)
    {
        int resolvedPhase = Mathf.Clamp(phase, 1, 3);
        return resolvedPhase >= ResolveMinPhase() && resolvedPhase <= ResolveMaxPhase();
    }

    public AttackTelegraphType ResolvePreferredFollowUpTelegraph()
    {
        if (preferredFollowUpTelegraph != AttackTelegraphType.Auto)
            return preferredFollowUpTelegraph;

        switch (ResolveTelegraphType())
        {
            case AttackTelegraphType.Parry:
            case AttackTelegraphType.Guard:
                return AttackTelegraphType.Dodge;
            case AttackTelegraphType.Dodge:
                return AttackTelegraphType.Parry;
            case AttackTelegraphType.Danger:
            case AttackTelegraphType.Auto:
            default:
                return AttackTelegraphType.Auto;
        }
    }
}
