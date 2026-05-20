using UnityEngine;

public static class GameplaySceneCache
{
    const float RetryInterval = 0.5f;

    static Camera _mainCamera;
    static Transform _mainCameraTransform;
    static CameraShake _mainCameraShake;
    static PlayerReferences _playerReferences;
    static PlayerLockOn _playerLockOn;
    static LockOnCameraManager _lockOnCameraManager;
    static Canvas _canvas;
    static PlayerUltimateController _playerUltimateController;
    static BossBreakController _bossBreakController;
    static BossUIController _bossUiController;
    static BossHealth _bossHealth;
    static BossController _bossController;

    static float _nextMainCameraResolveAt;
    static float _nextPlayerReferencesResolveAt;
    static float _nextPlayerLockOnResolveAt;
    static float _nextLockOnCameraManagerResolveAt;
    static float _nextCanvasResolveAt;
    static float _nextPlayerUltimateResolveAt;
    static float _nextBossBreakResolveAt;
    static float _nextBossUiResolveAt;
    static float _nextBossHealthResolveAt;
    static float _nextBossControllerResolveAt;

    public static Camera ResolveMainCamera()
    {
        Camera taggedMainCamera = Camera.main;
        if (_mainCamera != null)
        {
            bool cachedInvalid =
                _mainCamera.transform == null ||
                !_mainCamera.isActiveAndEnabled ||
                !_mainCamera.gameObject.activeInHierarchy;

            if (cachedInvalid || (taggedMainCamera != null && taggedMainCamera != _mainCamera))
            {
                _mainCamera = null;
                _mainCameraTransform = null;
                _mainCameraShake = null;
            }
        }

        if (_mainCamera != null)
            return _mainCamera;

        if (!CanResolve(ref _nextMainCameraResolveAt))
            return null;

        _mainCamera = taggedMainCamera != null ? taggedMainCamera : Camera.main;
        _mainCameraTransform = _mainCamera != null ? _mainCamera.transform : null;
        _mainCameraShake = _mainCamera != null ? _mainCamera.GetComponent<CameraShake>() : null;
        return _mainCamera;
    }

    public static Transform ResolveMainCameraTransform()
    {
        if (_mainCameraTransform != null &&
            (_mainCameraTransform.gameObject == null || !_mainCameraTransform.gameObject.activeInHierarchy))
        {
            _mainCamera = null;
            _mainCameraTransform = null;
            _mainCameraShake = null;
        }

        if (_mainCameraTransform != null)
            return _mainCameraTransform;

        Camera camera = ResolveMainCamera();
        _mainCameraTransform = camera != null ? camera.transform : null;
        return _mainCameraTransform;
    }

    public static CameraShake ResolveMainCameraShake()
    {
        if (_mainCameraShake != null)
            return _mainCameraShake;

        Camera camera = ResolveMainCamera();
        if (camera == null)
            return null;

        _mainCameraShake = camera.GetComponent<CameraShake>();
        return _mainCameraShake;
    }

    public static PlayerReferences ResolvePlayerReferences()
    {
        if (_playerReferences != null)
            return _playerReferences;

        if (!CanResolve(ref _nextPlayerReferencesResolveAt))
            return null;

        _playerReferences = Object.FindAnyObjectByType<PlayerReferences>();
        return _playerReferences;
    }

    public static PlayerLockOn ResolvePlayerLockOn()
    {
        if (_playerLockOn != null)
            return _playerLockOn;

        if (!CanResolve(ref _nextPlayerLockOnResolveAt))
            return null;

        _playerLockOn = Object.FindAnyObjectByType<PlayerLockOn>();
        return _playerLockOn;
    }

    public static LockOnCameraManager ResolveLockOnCameraManager()
    {
        if (_lockOnCameraManager != null)
            return _lockOnCameraManager;

        if (!CanResolve(ref _nextLockOnCameraManagerResolveAt))
            return null;

        _lockOnCameraManager = Object.FindAnyObjectByType<LockOnCameraManager>();
        return _lockOnCameraManager;
    }

    public static Canvas ResolveCanvas()
    {
        if (_canvas != null)
            return _canvas;

        if (!CanResolve(ref _nextCanvasResolveAt))
            return null;

        _canvas = Object.FindAnyObjectByType<Canvas>();
        return _canvas;
    }

    public static PlayerUltimateController ResolvePlayerUltimateController()
    {
        if (_playerUltimateController != null)
            return _playerUltimateController;

        if (!CanResolve(ref _nextPlayerUltimateResolveAt))
            return null;

        _playerUltimateController = Object.FindObjectOfType<PlayerUltimateController>(true);
        return _playerUltimateController;
    }

    public static BossBreakController ResolveBossBreakController()
    {
        if (_bossBreakController != null)
            return _bossBreakController;

        if (!CanResolve(ref _nextBossBreakResolveAt))
            return null;

        _bossBreakController = Object.FindObjectOfType<BossBreakController>(true);
        return _bossBreakController;
    }

    public static BossUIController ResolveBossUIController()
    {
        if (_bossUiController != null)
            return _bossUiController;

        if (!CanResolve(ref _nextBossUiResolveAt))
            return null;

        _bossUiController = Object.FindObjectOfType<BossUIController>(true);
        return _bossUiController;
    }

    public static BossHealth ResolveBossHealth()
    {
        if (_bossHealth != null)
            return _bossHealth;

        if (!CanResolve(ref _nextBossHealthResolveAt))
            return null;

        _bossHealth = Object.FindObjectOfType<BossHealth>(true);
        return _bossHealth;
    }

    public static BossController ResolveBossController()
    {
        if (_bossController != null)
            return _bossController;

        if (!CanResolve(ref _nextBossControllerResolveAt))
            return null;

        _bossController = Object.FindObjectOfType<BossController>(true);
        return _bossController;
    }

    static bool CanResolve(ref float nextResolveAt)
    {
        if (!Application.isPlaying)
            return true;

        float now = Time.unscaledTime;
        if (now < nextResolveAt)
            return false;

        nextResolveAt = now + RetryInterval;
        return true;
    }
}
