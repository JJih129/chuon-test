using System.Collections.Generic;
using UnityEngine;

public enum EnemyTokenKind
{
    Melee = 0,
    Ranged = 1
}

// 공격 토큰 디렉터 (소울라이크 / 스텔라 블레이드 '군중 예절')
//  - 대상(플레이어) 하나당 근접 공격 동시 2명, 원거리 연사 동시 1명.
//  - 토큰을 반납하면 그 슬롯은 잠시(쿨다운) 비어 있어 공격이 연달아 겹치지 않는다.
//  - 토큰은 보유 시간이 지나면 자동 만료 (사망/비활성화로 반납을 놓쳐도 새지 않음).
//  - 토큰이 없는 적은 중거리에서 원을 그리며 대기 (교전 목록으로 서로 간격을 벌린다).
// 정적 클래스: 씬 오브젝트 탐색 없음, 런타임 할당은 대상 첫 등록 시 1회.
public static class EnemyAttackTokenDirector
{
    public const int SlotCapacity = 4;

    /// <summary>대상당 동시 근접 공격자 수.</summary>
    public static int MaxMelee = 2;
    /// <summary>대상당 동시 원거리 연사 수.</summary>
    public static int MaxRanged = 1;
    /// <summary>근접 토큰 반납 후 슬롯 재사용까지 (초).</summary>
    public static float MeleeSlotCooldown = 0.35f;
    /// <summary>원거리 토큰 반납 후 슬롯 재사용까지 (초).</summary>
    public static float RangedSlotCooldown = 0.7f;

    sealed class Board
    {
        public readonly Object[][] holders = { new Object[SlotCapacity], new Object[SlotCapacity] };
        public readonly float[][] expiresAt = { new float[SlotCapacity], new float[SlotCapacity] };
        public readonly float[][] freeAt = { new float[SlotCapacity], new float[SlotCapacity] };
        public readonly List<Transform> engaged = new List<Transform>(8);
    }

    static readonly Dictionary<int, Board> s_boards = new Dictionary<int, Board>(4);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        s_boards.Clear();
        MaxMelee = 2;
        MaxRanged = 1;
        MeleeSlotCooldown = 0.35f;
        RangedSlotCooldown = 0.7f;
    }

    static Board GetBoard(Transform target, bool create)
    {
        if (target == null)
            return null;
        int key = target.GetInstanceID();
        if (s_boards.TryGetValue(key, out Board board))
            return board;
        if (!create)
            return null;
        board = new Board();
        s_boards[key] = board;
        return board;
    }

    static int MaxFor(EnemyTokenKind kind) => Mathf.Clamp(kind == EnemyTokenKind.Melee ? MaxMelee : MaxRanged, 0, SlotCapacity);
    static float CooldownFor(EnemyTokenKind kind) => kind == EnemyTokenKind.Melee ? MeleeSlotCooldown : RangedSlotCooldown;

    /// <summary>토큰 요청. 이미 보유 중이면 만료 시각만 연장하고 true.</summary>
    public static bool TryAcquire(Transform target, Object owner, EnemyTokenKind kind, float holdSeconds)
    {
        if (CinematicDirector.IsPlaying)
            return false; // 반복 14: 연출 재생 중에는 공격 토큰을 주지 않는다
        if (target == null || owner == null)
            return true; // 대상이 없으면 제한하지 않는다
        Board board = GetBoard(target, true);
        int k = (int)kind;
        Object[] holders = board.holders[k];
        float now = Time.time;
        float expire = now + Mathf.Max(0.1f, holdSeconds);

        for (int i = 0; i < SlotCapacity; i++)
        {
            if (holders[i] == owner)
            {
                board.expiresAt[k][i] = expire;
                return true;
            }
        }

        int max = MaxFor(kind);
        for (int i = 0; i < max; i++)
        {
            bool vacant = holders[i] == null || now >= board.expiresAt[k][i];
            if (!vacant)
                continue;
            if (holders[i] != null)
            {
                // 만료된 토큰은 반납으로 처리 (쿨다운은 만료 시각부터)
                holders[i] = null;
                board.freeAt[k][i] = board.expiresAt[k][i] + CooldownFor(kind);
            }
            if (now < board.freeAt[k][i])
                continue;

            holders[i] = owner;
            board.expiresAt[k][i] = expire;
            return true;
        }

        return false;
    }

    public static void Release(Transform target, Object owner, EnemyTokenKind kind)
    {
        Board board = GetBoard(target, false);
        if (board == null || owner == null)
            return;
        int k = (int)kind;
        Object[] holders = board.holders[k];
        for (int i = 0; i < SlotCapacity; i++)
        {
            if (holders[i] != owner)
                continue;
            holders[i] = null;
            board.freeAt[k][i] = Time.time + CooldownFor(kind);
        }
    }

    public static void ReleaseAll(Transform target, Object owner)
    {
        Release(target, owner, EnemyTokenKind.Melee);
        Release(target, owner, EnemyTokenKind.Ranged);
    }

    public static bool Holds(Transform target, Object owner, EnemyTokenKind kind)
    {
        Board board = GetBoard(target, false);
        if (board == null || owner == null)
            return false;
        int k = (int)kind;
        float now = Time.time;
        for (int i = 0; i < SlotCapacity; i++)
        {
            if (board.holders[k][i] == owner && now < board.expiresAt[k][i])
                return true;
        }
        return false;
    }

    /// <summary>QA: 현재 유효한 토큰 수.</summary>
    public static int ActiveCount(Transform target, EnemyTokenKind kind)
    {
        Board board = GetBoard(target, false);
        if (board == null)
            return 0;
        int k = (int)kind;
        int count = 0;
        float now = Time.time;
        for (int i = 0; i < SlotCapacity; i++)
        {
            if (board.holders[k][i] != null && now < board.expiresAt[k][i])
                count++;
        }
        return count;
    }

    // ---------------------------------------------------------------- 교전 목록 (원형 대기 간격)

    public static void Engage(Transform target, Transform enemy)
    {
        Board board = GetBoard(target, true);
        if (board == null || enemy == null || board.engaged.Contains(enemy))
            return;
        board.engaged.Add(enemy);
    }

    public static void Disengage(Transform target, Transform enemy)
    {
        Board board = GetBoard(target, false);
        if (board == null || enemy == null)
            return;
        board.engaged.Remove(enemy);
    }

    public static int EngagedCount(Transform target)
    {
        Board board = GetBoard(target, false);
        return board != null ? board.engaged.Count : 0;
    }

    /// <summary>
    /// 대상 기준으로 이 적과 가장 가까운 다른 교전 적까지의 방위 차(도, -180..180). 없으면 999.
    /// 양수 = 다른 적이 시계 방향(오른쪽)에 있다 → 반대(왼쪽)로 돌면 간격이 벌어진다.
    /// </summary>
    public static float NearestEngagedBearingDelta(Transform target, Transform enemy)
    {
        Board board = GetBoard(target, false);
        if (board == null || enemy == null || target == null)
            return 999f;

        Vector3 c = target.position;
        float mine = Bearing(c, enemy.position);
        float best = 999f;
        List<Transform> list = board.engaged;
        for (int i = list.Count - 1; i >= 0; i--)
        {
            Transform other = list[i];
            if (other == null || !other.gameObject.activeInHierarchy)
            {
                list.RemoveAt(i);
                continue;
            }
            if (other == enemy)
                continue;
            float d = Mathf.DeltaAngle(mine, Bearing(c, other.position));
            if (Mathf.Abs(d) < Mathf.Abs(best))
                best = d;
        }
        return best;
    }

    static float Bearing(Vector3 center, Vector3 p)
    {
        return Mathf.Atan2(p.x - center.x, p.z - center.z) * Mathf.Rad2Deg;
    }
}
