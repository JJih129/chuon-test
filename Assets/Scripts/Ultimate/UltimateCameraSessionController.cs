using Unity.Cinemachine;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class UltimateCameraSessionController : MonoBehaviour
{
    [SerializeField] private GameObject cameraRigRoot;
    [SerializeField] private CinemachineBrain brain;
    [SerializeField] private CinemachineVirtualCameraBase[] sequenceCameras;
    [SerializeField] private bool debugLog;

    PlayerUltimateController _owner;
    bool _sessionActive;

    public CinemachineBrain Brain => brain;
    public CinemachineVirtualCameraBase[] SequenceCameras => sequenceCameras;

    public void ConfigureRuntime(GameObject runtimeCameraRigRoot, CinemachineBrain runtimeBrain, CinemachineVirtualCameraBase[] runtimeSequenceCameras)
    {
        cameraRigRoot = runtimeCameraRigRoot;
        brain = runtimeBrain;
        sequenceCameras = runtimeSequenceCameras;
    }

    void Awake()
    {
        if (brain == null)
            brain = FindFirstObjectByType<CinemachineBrain>();
    }

    public void BeginSession(PlayerUltimateController owner)
    {
        _owner = owner;
        _sessionActive = true;
        SetRigActive(true);
    }

    public void OnCameraSessionBegin()
    {
        if (!_sessionActive)
            return;

        SetRigActive(true);
    }

    public void OnCameraSessionEnd()
    {
        if (!_sessionActive)
            return;

        SetRigActive(false);
    }

    public void CleanupIfNeeded()
    {
        if (!_sessionActive)
            return;

        SetRigActive(false);
        _owner = null;
        _sessionActive = false;
    }

    void SetRigActive(bool active)
    {
        if (cameraRigRoot != null)
            cameraRigRoot.SetActive(active);

        if (sequenceCameras == null)
            return;

        for (int i = 0; i < sequenceCameras.Length; i++)
        {
            CinemachineVirtualCameraBase camera = sequenceCameras[i];
            if (camera == null)
                continue;

            camera.gameObject.SetActive(active);
            camera.enabled = active;
        }

    }
}
