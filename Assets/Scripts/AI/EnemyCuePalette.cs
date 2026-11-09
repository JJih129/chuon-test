using UnityEngine;

// 잡몹 텔레그래프 색 언어 (보스와 동일)
//  패링 가능 = 금/주황, 회피 전용 = 빨강, 가드·패링 불가 = 자홍, 강공(가드 브레이크) = 호박.
//  색각 보정 팔레트(BossTelegraphVfxPresenter.UseColorblindSafePalette, Okabe-Ito)를 그대로 따른다.
public enum EnemyCueType
{
    None = 0,
    Normal = 1,
    Parryable = 2,
    DodgeOnly = 3,
    Unblockable = 4,
    Heavy = 5
}

public static class EnemyCuePalette
{
    // BossTelegraphVfxPresenter 기본값과 동일 (HDR)
    static readonly Color s_normal = new Color(0.75f, 0.95f, 1f) * 1.8f;
    static readonly Color s_parryable = new Color(1f, 0.78f, 0.22f) * 2.6f;
    static readonly Color s_dodgeOnly = new Color(1f, 0.1f, 0.08f) * 2.6f;
    static readonly Color s_unblockable = new Color(1f, 0.12f, 0.85f) * 3f;
    static readonly Color s_heavy = new Color(1f, 0.6f, 0.04f) * 2.6f;

    public static bool ColorblindSafe => BossTelegraphVfxPresenter.UseColorblindSafePalette;

    public static Color GetColor(EnemyCueType type)
    {
        if (ColorblindSafe)
        {
            switch (type)
            {
                case EnemyCueType.Parryable: return new Color(0.94f, 0.89f, 0.26f) * 2.4f;
                case EnemyCueType.DodgeOnly: return new Color(0.84f, 0.37f, 0f) * 2.6f;
                case EnemyCueType.Unblockable: return new Color(0.8f, 0.47f, 0.65f) * 3f;
                case EnemyCueType.Heavy: return new Color(0f, 0.45f, 0.7f) * 3f;
                default: return s_normal;
            }
        }

        switch (type)
        {
            case EnemyCueType.Parryable: return s_parryable;
            case EnemyCueType.DodgeOnly: return s_dodgeOnly;
            case EnemyCueType.Unblockable: return s_unblockable;
            case EnemyCueType.Heavy: return s_heavy;
            default: return s_normal;
        }
    }

    /// <summary>바닥 데칼(BossGroundTelegraph)용 타입. 강공은 패링 가능(금/주황) 데칼 + 호박 섬광.</summary>
    public static AttackTelegraphType ToGroundType(EnemyCueType type)
    {
        switch (type)
        {
            case EnemyCueType.Parryable:
            case EnemyCueType.Heavy:
                return AttackTelegraphType.Parry;
            case EnemyCueType.DodgeOnly:
                return AttackTelegraphType.Dodge;
            case EnemyCueType.Unblockable:
                return AttackTelegraphType.Danger;
            default:
                return AttackTelegraphType.Guard;
        }
    }

    /// <summary>큐 타입 → 플레이어 방어 규칙 (PlayerDamageReceiver 가 해석).</summary>
    public static void ApplyDefenseRules(ref HitPayload payload, EnemyCueType type)
    {
        switch (type)
        {
            case EnemyCueType.DodgeOnly:
            case EnemyCueType.Unblockable:
                payload.canParry = false;
                payload.canGuard = false;
                payload.unblockable = true;
                payload.causesGuardBreak = false;
                payload.canPerfectDodge = true;
                break;
            case EnemyCueType.Heavy:
                payload.canParry = true;
                payload.canGuard = true;
                payload.unblockable = false;
                payload.causesGuardBreak = true;
                payload.canPerfectDodge = true;
                break;
            default: // Normal / Parryable / None: 가드·패링·회피 모두 가능
                payload.canParry = true;
                payload.canGuard = true;
                payload.unblockable = false;
                payload.causesGuardBreak = false;
                payload.canPerfectDodge = true;
                break;
        }
    }
}
