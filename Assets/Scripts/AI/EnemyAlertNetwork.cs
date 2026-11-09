using System.Collections.Generic;
using UnityEngine;

/// <summary>경보를 받을 수 있는 적 (EnemyBrain, PatrolDroneBrain).</summary>
public interface IEnemyAlertable
{
    Vector3 AlertPosition { get; }
    bool IsAlertedOrDead { get; }
    void ReceiveAlert(Vector3 knownTargetPosition);
}

// 동료 경보: 한 적이 플레이어를 발견하면 반경 안의 다른 적도 깨운다 (종류 무관).
public static class EnemyAlertNetwork
{
    static readonly List<IEnemyAlertable> s_members = new List<IEnemyAlertable>(24);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => s_members.Clear();

    public static void Register(IEnemyAlertable member)
    {
        if (member != null && !s_members.Contains(member))
            s_members.Add(member);
    }

    public static void Unregister(IEnemyAlertable member) => s_members.Remove(member);

    public static void Broadcast(IEnemyAlertable source, Vector3 origin, float radius, Vector3 knownTargetPosition)
    {
        if (radius <= 0f)
            return;
        float r2 = radius * radius;
        for (int i = s_members.Count - 1; i >= 0; i--)
        {
            if (i >= s_members.Count)
                continue;
            IEnemyAlertable m = s_members[i];
            if (m == null || ReferenceEquals(m, source) || m.IsAlertedOrDead)
                continue;
            if ((m.AlertPosition - origin).sqrMagnitude > r2)
                continue;
            m.ReceiveAlert(knownTargetPosition);
        }
    }
}
