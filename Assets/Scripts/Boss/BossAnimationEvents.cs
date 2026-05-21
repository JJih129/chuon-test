using UnityEngine;

[DisallowMultipleComponent]
public class BossAnimationEvents : MonoBehaviour
{
    [Header("필수 참조")]
    [Tooltip("보스 FSM 컨트롤러")]
    [SerializeField] private BossController bossController;

    [Header("무기 히트박스 (자동/수동)")]
    [Tooltip("비워두면 자식에서 AttackHitbox 전부 자동 검색")]
    [SerializeField] private AttackHitbox[] hitboxes;

    [Header("공격 VFX")]
    [Tooltip("패턴별 검기 이펙트 프레젠터. 비워두면 부모에서 자동 검색합니다.")]
    [SerializeField] private BossAttackVfxPresenter attackVfxPresenter;

    [Header("설정 / 디버그")]
    [SerializeField] private bool autoFindOnAwake = true;
    [SerializeField] private bool debugLog = false;

    void Reset()
    {
        if (!bossController)
            bossController = GetComponentInParent<BossController>();

        hitboxes = GetComponentsInChildren<AttackHitbox>(true);
        attackVfxPresenter = GetComponentInParent<BossAttackVfxPresenter>();
    }

    void Awake()
    {
        if (autoFindOnAwake)
        {
            if (!bossController)
                bossController = GetComponentInParent<BossController>();

            if (!attackVfxPresenter)
                attackVfxPresenter = GetComponentInParent<BossAttackVfxPresenter>();

            RefreshHitboxesIfNeeded(force: true);
        }
    }

    public void Anim_ActivateHitbox()
    {
        if (debugLog) Debug.Log("[BossAnimEvents] Anim_ActivateHitbox", this);

        if (!CanProcessAttackEvents())
        {
            SetResolvedHitboxesActive(false);
            return;
        }

        if (attackVfxPresenter != null)
            attackVfxPresenter.PlayCurrentPatternSlash();

        if (SetResolvedHitboxesActive(true))
            return;

        if (bossController != null && bossController.attackHitbox != null)
        {
            bossController.attackHitbox.ActivateWindow();
            return;
        }

        if (bossController != null)
            bossController.ActivateHitbox();
    }

    public void Anim_DeactivateHitbox()
    {
        if (debugLog) Debug.Log("[BossAnimEvents] Anim_DeactivateHitbox", this);

        if (attackVfxPresenter != null)
            attackVfxPresenter.StopCurrentPatternSlash();

        if (SetResolvedHitboxesActive(false))
            return;

        if (bossController != null && bossController.attackHitbox != null)
        {
            bossController.attackHitbox.DeactivateWindow();
            return;
        }

        if (bossController != null)
            bossController.DeactivateHitbox();
    }

    public void Anim_SignalPatternEnd()
    {
        if (debugLog) Debug.Log("[BossAnimEvents] Anim_SignalPatternEnd", this);

        if (bossController != null && bossController.CanProcessAttackAnimationEvents)
            bossController.OnAnimationPatternEnd();
    }

    bool CanProcessAttackEvents()
    {
        if (bossController == null)
            return true;

        return bossController.CanProcessAttackAnimationEvents;
    }

    bool SetResolvedHitboxesActive(bool active)
    {
        if (!TryResolveHitboxes(out AttackHitbox[] resolvedHitboxes))
            return false;

        for (int i = 0; i < resolvedHitboxes.Length; i++)
        {
            if (resolvedHitboxes[i] == null)
                continue;

            if (active)
                resolvedHitboxes[i].ActivateWindow();
            else
                resolvedHitboxes[i].DeactivateWindow();
        }

        return true;
    }

    bool TryResolveHitboxes(out AttackHitbox[] resolvedHitboxes)
    {
        if (bossController != null && bossController.IsUsingRuntimeForwardAttackHitbox && bossController.attackHitbox != null)
        {
            hitboxes = new[] { bossController.attackHitbox };
            resolvedHitboxes = hitboxes;
            return true;
        }

        RefreshHitboxesIfNeeded(force: false);

        if (hitboxes != null)
        {
            for (int i = 0; i < hitboxes.Length; i++)
            {
                if (hitboxes[i] != null)
                {
                    resolvedHitboxes = hitboxes;
                    return true;
                }
            }
        }

        if (bossController != null && bossController.attackHitbox != null)
        {
            hitboxes = new[] { bossController.attackHitbox };
            resolvedHitboxes = hitboxes;
            return true;
        }

        resolvedHitboxes = null;
        return false;
    }

    void RefreshHitboxesIfNeeded(bool force)
    {
        if (bossController != null && bossController.IsUsingRuntimeForwardAttackHitbox && bossController.attackHitbox != null)
        {
            hitboxes = new[] { bossController.attackHitbox };
            return;
        }

        if (!force && hitboxes != null)
        {
            for (int i = 0; i < hitboxes.Length; i++)
            {
                if (hitboxes[i] != null)
                    return;
            }
        }

        hitboxes = GetComponentsInChildren<AttackHitbox>(true);

        if ((hitboxes == null || hitboxes.Length == 0) && bossController != null && bossController.attackHitbox != null)
            hitboxes = new[] { bossController.attackHitbox };
    }
}
