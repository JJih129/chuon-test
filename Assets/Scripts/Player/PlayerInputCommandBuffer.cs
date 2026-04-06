using UnityEngine;

[DisallowMultipleComponent]
public sealed class PlayerInputCommandBuffer : MonoBehaviour
{
    public enum CommandType : byte
    {
        AttackLight = 0,
        AttackHeavy = 1,
        GuardPress = 2,
        GuardRelease = 3,
        DodgePress = 4,
        DodgeBuffered = 5
    }

    public struct CommandEntry
    {
        public CommandType Type;
        public float Realtime;
        public bool Buffered;
        public uint Sequence;
    }

    const int Capacity = 32;

    [SerializeField, Min(0.05f)] float recentWindowSeconds = 0.35f;

    readonly CommandEntry[] _entries = new CommandEntry[Capacity];
    int _head;
    int _count;
    uint _nextSequence = 1u;

    public float RecentWindowSeconds => recentWindowSeconds;
    public int Count => _count;

    public void RecordAttackLightPress() => Push(CommandType.AttackLight, false);
    public void RecordAttackHeavyPress() => Push(CommandType.AttackHeavy, false);
    public void RecordGuardPress() => Push(CommandType.GuardPress, false);
    public void RecordGuardRelease() => Push(CommandType.GuardRelease, false);
    public void RecordDodgePress() => Push(CommandType.DodgePress, false);
    public void RecordBufferedDodge() => Push(CommandType.DodgeBuffered, true);

    public bool WasCommandSeenRecently(CommandType type)
    {
        return WasCommandSeenRecently(type, recentWindowSeconds);
    }

    public bool WasCommandSeenRecently(CommandType type, float windowSeconds)
    {
        float now = Time.realtimeSinceStartup;
        float maxAge = Mathf.Max(0.01f, windowSeconds);
        for (int i = 0; i < _count; i++)
        {
            CommandEntry entry = GetEntryFromNewest(i);
            if (now - entry.Realtime > maxAge)
                return false;

            if (entry.Type == type)
                return true;
        }

        return false;
    }

    public bool TryGetRecent(int reverseIndex, out CommandEntry entry)
    {
        if (reverseIndex < 0 || reverseIndex >= _count)
        {
            entry = default;
            return false;
        }

        entry = GetEntryFromNewest(reverseIndex);
        return true;
    }

    public bool TryConsumeLatest(CommandType type, float maxAgeSeconds, ref uint lastConsumedSequence)
    {
        if (_count <= 0)
            return false;

        float now = Time.realtimeSinceStartup;
        float maxAge = Mathf.Max(0.01f, maxAgeSeconds);
        for (int i = 0; i < _count; i++)
        {
            CommandEntry entry = GetEntryFromNewest(i);
            if (now - entry.Realtime > maxAge)
                break;

            if (entry.Type != type)
                continue;

            if (entry.Sequence <= lastConsumedSequence)
                return false;

            lastConsumedSequence = entry.Sequence;
            return true;
        }

        return false;
    }

    void Push(CommandType type, bool buffered)
    {
        _entries[_head] = new CommandEntry
        {
            Type = type,
            Realtime = Time.realtimeSinceStartup,
            Buffered = buffered,
            Sequence = _nextSequence++
        };

        _head = (_head + 1) % Capacity;
        if (_count < Capacity)
            _count++;
    }

    CommandEntry GetEntryFromNewest(int reverseIndex)
    {
        int index = _head - 1 - reverseIndex;
        if (index < 0)
            index += Capacity;

        return _entries[index];
    }
}
