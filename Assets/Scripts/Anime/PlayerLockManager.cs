// PlayerLockManager.cs
// 전역 입력/이동 락 관리. 간단 static API.
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
