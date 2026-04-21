using UnityEngine;

[DisallowMultipleComponent]
public sealed class UltimateCinematicBindings : MonoBehaviour
{
    [Header("Player")]
    [SerializeField] private Transform playerRoot;
    [SerializeField] private Animator playerAnimator;
    [SerializeField] private Transform playerVisualRoot;
    [SerializeField] private Transform swordCloseAnchor;
    [SerializeField] private Transform walkoutFacingAnchor;
    [SerializeField] private Transform slashStormCenter;

    [Header("Target")]
    [SerializeField] private Transform targetRoot;
    [SerializeField] private Transform targetCenter;
    [SerializeField] private Transform targetExplosionAnchor;
    [SerializeField] private Transform targetCineHoldAnchor;

    [Header("Shot Anchors")]
    [SerializeField] private Transform shot01Pos;
    [SerializeField] private Transform shot01LookAt;
    [SerializeField] private Transform shot02Pos;
    [SerializeField] private Transform shot02LookAt;
    [SerializeField] private Transform shot03Pos;
    [SerializeField] private Transform shot03LookAt;
    [SerializeField] private Transform shot04Pos;
    [SerializeField] private Transform shot04LookAt;
    [SerializeField] private Transform shot05Pos;
    [SerializeField] private Transform shot05LookAt;

    public Transform PlayerRoot => playerRoot;
    public Animator PlayerAnimator => playerAnimator;
    public Transform PlayerVisualRoot => playerVisualRoot;
    public Transform SwordCloseAnchor => swordCloseAnchor;
    public Transform WalkoutFacingAnchor => walkoutFacingAnchor;
    public Transform SlashStormCenter => slashStormCenter;
    public Transform TargetRoot => targetRoot;
    public Transform TargetCenter => targetCenter;
    public Transform TargetExplosionAnchor => targetExplosionAnchor;
    public Transform TargetCineHoldAnchor => targetCineHoldAnchor;
    public Transform Shot01Pos => shot01Pos;
    public Transform Shot01LookAt => shot01LookAt;
    public Transform Shot02Pos => shot02Pos;
    public Transform Shot02LookAt => shot02LookAt;
    public Transform Shot03Pos => shot03Pos;
    public Transform Shot03LookAt => shot03LookAt;
    public Transform Shot04Pos => shot04Pos;
    public Transform Shot04LookAt => shot04LookAt;
    public Transform Shot05Pos => shot05Pos;
    public Transform Shot05LookAt => shot05LookAt;

    public void ConfigureRuntime(
        Transform runtimePlayerRoot,
        Animator runtimePlayerAnimator,
        Transform runtimePlayerVisualRoot,
        Transform runtimeSwordCloseAnchor,
        Transform runtimeWalkoutFacingAnchor,
        Transform runtimeSlashStormCenter,
        Transform runtimeTargetRoot,
        Transform runtimeTargetCenter,
        Transform runtimeTargetExplosionAnchor,
        Transform runtimeTargetCineHoldAnchor,
        Transform runtimeShot01Pos,
        Transform runtimeShot01LookAt,
        Transform runtimeShot02Pos,
        Transform runtimeShot02LookAt,
        Transform runtimeShot03Pos,
        Transform runtimeShot03LookAt,
        Transform runtimeShot04Pos,
        Transform runtimeShot04LookAt,
        Transform runtimeShot05Pos,
        Transform runtimeShot05LookAt)
    {
        playerRoot = runtimePlayerRoot;
        playerAnimator = runtimePlayerAnimator;
        playerVisualRoot = runtimePlayerVisualRoot;
        swordCloseAnchor = runtimeSwordCloseAnchor;
        walkoutFacingAnchor = runtimeWalkoutFacingAnchor;
        slashStormCenter = runtimeSlashStormCenter;
        targetRoot = runtimeTargetRoot;
        targetCenter = runtimeTargetCenter;
        targetExplosionAnchor = runtimeTargetExplosionAnchor;
        targetCineHoldAnchor = runtimeTargetCineHoldAnchor;
        shot01Pos = runtimeShot01Pos;
        shot01LookAt = runtimeShot01LookAt;
        shot02Pos = runtimeShot02Pos;
        shot02LookAt = runtimeShot02LookAt;
        shot03Pos = runtimeShot03Pos;
        shot03LookAt = runtimeShot03LookAt;
        shot04Pos = runtimeShot04Pos;
        shot04LookAt = runtimeShot04LookAt;
        shot05Pos = runtimeShot05Pos;
        shot05LookAt = runtimeShot05LookAt;
    }
}
