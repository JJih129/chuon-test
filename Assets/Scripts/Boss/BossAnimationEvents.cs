// 파일명: BossAnimationEvents.cs
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

    [Header("설정 / 디버그")]
    [SerializeField] private bool autoFindOnAwake = true;
    [SerializeField] private bool debugLog = false;

    void Reset()
    {
        if (!bossController)
            bossController = GetComponentInParent<BossController>();

        hitboxes = GetComponentsInChildren<AttackHitbox>(true);
    }

    void Awake()
    {
        if (autoFindOnAwake)
        {
            if (!bossController)
                bossController = GetComponentInParent<BossController>();

            if (hitboxes == null || hitboxes.Length == 0)
                hitboxes = GetComponentsInChildren<AttackHitbox>(true);
        }
    }

    // ------------------------------------------------------------------
    //  애니메이션 이벤트에서 직접 호출할 함수들
    //  이름을 Anim_XXX 로 해서 다른 컴포넌트와 절대 안 겹치게 만든다.
    // ------------------------------------------------------------------

    /// <summary>
    /// 히트박스 ON (공격 판정 시작 프레임)
    /// Animation Event: BossAnimationEvents.Anim_ActivateHitbox()
    /// </summary>
    public void Anim_ActivateHitbox()
    {
        if (debugLog) Debug.Log("[BossAnimEvents] Anim_ActivateHitbox", this);

        if (hitboxes != null && hitboxes.Length > 0)
        {
            for (int i = 0; i < hitboxes.Length; i++)
            {
                if (hitboxes[i] != null)
                    hitboxes[i].ActivateWindow();
            }
        }
        else if (bossController != null)
        {
            // 혹시 루트에 콜라이더로 때리는 구조를 쓸 때 대비한 백업 경로
            bossController.ActivateHitbox();
        }
    }

    /// <summary>
    /// 히트박스 OFF (공격 판정 끝 프레임)
    /// Animation Event: BossAnimationEvents.Anim_DeactivateHitbox()
    /// </summary>
    public void Anim_DeactivateHitbox()
    {
        if (debugLog) Debug.Log("[BossAnimEvents] Anim_DeactivateHitbox", this);

        if (hitboxes != null && hitboxes.Length > 0)
        {
            for (int i = 0; i < hitboxes.Length; i++)
            {
                if (hitboxes[i] != null)
                    hitboxes[i].DeactivateWindow();
            }
        }
        else if (bossController != null)
        {
            bossController.DeactivateHitbox();
        }
    }

    /// <summary>
    /// 공격 패턴 클립이 완전히 끝났을 때
    /// Animation Event: BossAnimationEvents.Anim_SignalPatternEnd()
    /// </summary>
    public void Anim_SignalPatternEnd()
    {
        if (debugLog) Debug.Log("[BossAnimEvents] Anim_SignalPatternEnd", this);

        if (bossController != null)
            bossController.OnAnimationPatternEnd();
    }
}
