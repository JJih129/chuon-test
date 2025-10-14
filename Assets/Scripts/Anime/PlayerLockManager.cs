// PlayerLockManager.cs
// 설명: 전역 애니/입력 락 카운트 관리. 다른 시스템은 IsLocked를 확인하거나 Lock/Unlock 사용.
public static class PlayerLockManager
{
    static int _lockCount = 0;
    public static bool IsLocked => _lockCount > 0;

    public static void Lock()
    {
        _lockCount++;
    }

    public static void Unlock()
    {
        if (_lockCount > 0) _lockCount--;
    }

    // 안전한 범위 락(추천 사용)
    public static System.IDisposable LockScope()
    {
        Lock();
        return new ScopeRelease();
    }

    class ScopeRelease : System.IDisposable
    {
        bool _disposed = false;
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Unlock();
        }
    }
}
