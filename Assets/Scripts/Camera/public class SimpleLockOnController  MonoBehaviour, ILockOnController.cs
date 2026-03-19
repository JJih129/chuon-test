// ?뚯씪紐? SimpleLockOnController.cs
// 紐⑹쟻: ?덇굅??李몄“(IntegratedBossUI ?? ?명솚???대뙌??
// ?숈옉: ?대??곸쑝濡?PlayerLockOn怨??숆린?뷀븯??currentTarget???쒓났
// ??젣 湲덉?: ?ㅻ옒??肄붾뱶媛 ????낆쓣 吏곸젒 李몄“?섎?濡??좎? ?꾩슂

using UnityEngine;

public class SimpleLockOnController : MonoBehaviour, ILockOnController
{
    // ===== 蹂???ㅻ뜑(?쒓? ?ㅻ챸) =====
    [Header("??李몄“")]
    [Tooltip("???쎌삩 ?쒖뒪?? 鍮꾩썙?먮㈃ GetComponent濡??먮룞 ?먯깋")]
    [SerializeField] private PlayerLockOn playerLockOn; // [議곗젅媛?

    [Header("???꾩옱 ?쎌삩 ????쎄린 ?꾩슜泥섎읆 ?ъ슜)")]
    [Tooltip("?덇굅???명솚?? ?대??곸쑝濡?PlayerLockOn.CurrentTarget怨??숆린?붾맖")]
    public Transform currentTarget; // [?명솚媛?

    [Header("????꾨씪??移대찓??沅뚰븳 ?뚮옒洹??명솚)")]
    [Tooltip("??꾨씪?몄씠 移대찓?쇰? 媛?멸?硫?true濡??ㅼ젙(?꾩슂 ???대깽???곌껐)")]
    public bool timelineOwnsCamera = false; // [?명솚媛?

    void Reset()
    {
        if (!playerLockOn) playerLockOn = GetComponent<PlayerLockOn>();
    }

    void Awake()
    {
        if (!playerLockOn) playerLockOn = GetComponent<PlayerLockOn>();
    }

    void LateUpdate()
    {
        // 留??꾨젅??PlayerLockOn怨??숆린??
        if (playerLockOn)
            currentTarget = playerLockOn.CurrentTarget;
    }

    // ===== ILockOnController ?명솚 援ы쁽 =====
    public Transform GetCurrentTarget()
    {
        return playerLockOn ? playerLockOn.CurrentTarget : currentTarget;
    }

    public bool IsLockedOn()
    {
        return playerLockOn && playerLockOn.IsLockedOn();
    }

    public void GiveCameraControlToTimeline(bool give)
    {
        timelineOwnsCamera = give;
        if (playerLockOn != null)
            playerLockOn.GiveCameraControlToTimeline(give);
    }

    // ?덇굅??肄붾뱶?먯꽌 IsLockOn(bool) ?뺥깭瑜??몄텧?????덉뼱 蹂댁“ API ?쒓났
    public bool IsLockOn()
    {
        return IsLockedOn();
    }
}

