using UnityEngine;

public struct UltimateCinematicFrame
{
    public Vector3 Origin;
    public Vector3 Forward;
    public Vector3 Right;
    public Vector3 Up;
    public Vector3 TargetCenter;
    public Vector3 PlayerStartPoint;
    public Vector3 PlayerStrikePoint;
    public Vector3 PlayerFinishPoint;
    public Quaternion Rotation;

    public bool IsValid => Forward.sqrMagnitude > 0.0001f && Right.sqrMagnitude > 0.0001f;

    public static UltimateCinematicFrame Create(Vector3 playerPoint, Vector3 targetCenter, Vector3 fallbackForward)
    {
        Vector3 forward = targetCenter - playerPoint;
        forward.y = 0f;
        if (forward.sqrMagnitude <= 0.0001f)
        {
            forward = fallbackForward;
            forward.y = 0f;
        }

        if (forward.sqrMagnitude <= 0.0001f)
            forward = Vector3.forward;

        forward.Normalize();
        Vector3 up = Vector3.up;
        Vector3 right = Vector3.Cross(up, forward);
        if (right.sqrMagnitude <= 0.0001f)
            right = Vector3.right;
        right.Normalize();

        return new UltimateCinematicFrame
        {
            Origin = targetCenter,
            Forward = forward,
            Right = right,
            Up = up,
            TargetCenter = targetCenter,
            PlayerStartPoint = playerPoint,
            PlayerStrikePoint = playerPoint,
            PlayerFinishPoint = playerPoint,
            Rotation = Quaternion.LookRotation(forward, up)
        };
    }

    public Vector3 TransformOffset(Vector3 localOffset)
    {
        return Origin + Right * localOffset.x + Up * localOffset.y + Forward * localOffset.z;
    }

    public UltimateCinematicFrame WithPlayerPoints(Vector3 startPoint, Vector3 strikePoint, Vector3 finishPoint)
    {
        PlayerStartPoint = startPoint;
        PlayerStrikePoint = strikePoint;
        PlayerFinishPoint = finishPoint;
        return this;
    }
}
