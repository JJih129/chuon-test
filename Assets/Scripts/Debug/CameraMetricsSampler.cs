#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections.Generic;
using System.Text;
using Unity.Cinemachine;
using UnityEngine;

/// <summary>
/// 개발 전용 카메라 지표 수집기 (Docs/CameraDesign_Soulslike.md §7 검증 기준).
/// 게임플레이 카메라가 라이브인 프레임만 집계한다 (연출 카메라 priority >= 100 프레임 제외).
///  - 락온 중 보스가 화면 밖인 프레임 비율 (보스 렌더러 바운드 중심 기준)
///  - 카메라 요 각속도 최대값 / 240°/s 초과 프레임 수
///  - 근평면 관통 프레임 수 (근평면 반대각선 반경 CheckSphere, 캐릭터 콜라이더 제외)
///  - 회피/피격 구간(+0.3 s)의 카메라 수직 출렁임 (±0.15 s 이동평균 대비 최대 편차)
/// 사용: CameraMetricsSampler.Begin("label"); ... CameraMetricsSampler.Instance.Report()
/// </summary>
[DefaultExecutionOrder(32000)]
public sealed class CameraMetricsSampler : MonoBehaviour
{
    public static CameraMetricsSampler Instance { get; private set; }

    public string label = "camera";

    struct Sample
    {
        public float t;
        public float y;
        public float rigY;
        public bool window;
        public int cam;
    }

    readonly List<Sample> _samples = new List<Sample>(8192);
    readonly Collider[] _overlap = new Collider[16];

    Camera _cam;
    CinemachineBrain _brain;
    PlayerLockOn _lockOn;
    PlayerDodgeController _dodge;
    PlayerCombatController _combat;
    BossController _boss;
    BossHealth _bossHealth;
    Renderer _bossRenderer;
    Transform _playerRoot;
    LockOnCameraManager _legacyManager;

    int _gameplayFrames, _lockedFrames, _bossOffscreenFrames, _insideGeometryFrames, _yawOverFrames, _cinematicFrames;
    float _maxYawSpeed, _sumYawSpeed;
    int _yawSamples;
    float _lastYaw;
    bool _hasLastYaw;
    float _windowUntil;
    float _startTime;
    float _minBossDistance = float.MaxValue, _maxBossDistance;
    readonly float[] _bossDistanceBuckets = new float[3];

    public static CameraMetricsSampler Begin(string sampleLabel)
    {
        if (Instance != null)
            Destroy(Instance.gameObject);

        var go = new GameObject("__CameraMetricsSampler");
        DontDestroyOnLoad(go);
        Instance = go.AddComponent<CameraMetricsSampler>();
        Instance.label = sampleLabel;
        return Instance;
    }

    void Awake()
    {
        _startTime = Time.unscaledTime;
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    void Resolve()
    {
        if (_cam == null)
        {
            _cam = Camera.main;
            _brain = _cam != null ? _cam.GetComponent<CinemachineBrain>() : null;
        }

        if (_lockOn == null)
        {
            _lockOn = FindObjectOfType<PlayerLockOn>();
            if (_lockOn != null)
            {
                _playerRoot = _lockOn.transform;
                _dodge = _lockOn.GetComponentInChildren<PlayerDodgeController>(true) ?? _lockOn.GetComponentInParent<PlayerDodgeController>();
                _combat = _lockOn.GetComponentInChildren<PlayerCombatController>(true) ?? _lockOn.GetComponentInParent<PlayerCombatController>();
            }
        }

        if (_boss == null)
        {
            _boss = FindObjectOfType<BossController>();
            if (_boss != null)
            {
                _bossHealth = _boss.GetComponentInChildren<BossHealth>(true) ?? _boss.GetComponentInParent<BossHealth>();
                _bossRenderer = _boss.GetComponentInChildren<SkinnedMeshRenderer>() as Renderer ?? _boss.GetComponentInChildren<Renderer>();
            }
        }
    }

    readonly List<string> _spikes = new List<string>(8);
    readonly List<string> _camLog = new List<string>(40);
    string _lastCamName;
    public string CameraLog => string.Join("\n", _camLog);
    readonly Dictionary<int, string> _camNames = new Dictionary<int, string>();
    float _jitterAt;
    int _jitterCam;

    int CamId()
    {
        if (_brain == null || _brain.ActiveVirtualCamera == null)
            return 0;
        string n = ActiveCamName();
        int id = n.GetHashCode();
        if (!_camNames.ContainsKey(id))
            _camNames[id] = n;
        return id;
    }

    string ActiveCamName()
    {
        if (_brain == null || _brain.ActiveVirtualCamera == null)
            return "<none>";
        var v = _brain.ActiveVirtualCamera as CinemachineVirtualCameraBase;
        return _brain.ActiveVirtualCamera.Name + (v != null ? "(p" + v.Priority.Value + ")" : "") + (_brain.IsBlending ? "[blend]" : "");
    }

    bool IsGameplayCameraLive()
    {
        if (_brain == null)
            return true;

        if (_brain.IsBlending)
            return false;
        // 게임플레이 카메라 = 소울 리그 vcam / 기존 FreeLook / CM_LockOnCam. 그 외(타임라인 궁극기 샷 등 priority 0 포함)는 연출.
        ICinemachineCamera active = _brain.ActiveVirtualCamera;
        if (active == null)
            return false;
        if (SoulsCameraRig.Active != null && ReferenceEquals(active, SoulsCameraRig.Active.VirtualCamera))
            return !SoulsCameraRig.Active.IsBlendingFromCinematic;
        if (_legacyManager == null)
            _legacyManager = FindObjectOfType<LockOnCameraManager>();
        return _legacyManager != null && (ReferenceEquals(active, _legacyManager.freeLookCam) || ReferenceEquals(active, _legacyManager.lockOnCam));
    }

    void LateUpdate()
    {
        Resolve();
        if (_cam == null || Time.timeScale <= 0f)
            return;

        float dt = Time.unscaledDeltaTime;
        string camNow = ActiveCamName();
        if (camNow != _lastCamName)
        {
            if (_camLog.Count < 40)
                _camLog.Add($"{Time.unscaledTime - _startTime:F2}s {camNow}");
            _lastCamName = camNow;
        }
        if (SoulsCameraRig.Active != null && SoulsCameraRig.Active.LastBecameLiveAt > _startTime + 0.5f
            && _brain != null && ReferenceEquals(_brain.ActiveVirtualCamera, SoulsCameraRig.Active.VirtualCamera))
        {
            float since = Time.unscaledTime - SoulsCameraRig.Active.LastBecameLiveAt;
            if (since > 0.12f && since < 0.3f) Capture("return_mid_" + _captured.Count);
            if (since > 0.7f && since < 1.0f) Capture("return_end_" + _captured.Count);
        }
        if (!IsGameplayCameraLive())
        {
            _cinematicFrames++;
            _hasLastYaw = false;
            return;
        }

        _gameplayFrames++;
        Transform ct = _cam.transform;

        // yaw speed
        float yaw = ct.eulerAngles.y;
        if (_hasLastYaw && dt > 0.002f && dt < 0.1f)
        {
            float speed = Mathf.Abs(Mathf.DeltaAngle(_lastYaw, yaw)) / dt;
            if (speed > 240f && _spikes.Count < 6)
                _spikes.Add($"{Time.unscaledTime - _startTime:F1}s {speed:F0}deg/s cam={ActiveCamName()} locked={(_lockOn != null && _lockOn.IsLockedOn())}"
                    + (SoulsCameraRig.Active != null ? $" sinceLive={Time.unscaledTime - SoulsCameraRig.Active.LastBecameLiveAt:F2}s lockW={SoulsCameraRig.Active.LockWeight:F2}" : ""));
            _maxYawSpeed = Mathf.Max(_maxYawSpeed, speed);
            _sumYawSpeed += speed;
            _yawSamples++;
            if (speed > 240f)
                _yawOverFrames++;
        }
        _lastYaw = yaw;
        _hasLastYaw = true;

        // boss off-screen while locked
        bool bossAlive = _boss != null && _boss.isActiveAndEnabled && (_bossHealth == null || !_bossHealth.IsDead);
        if (bossAlive && _lockOn != null && _lockOn.IsLockedOn() && _lockOn.CurrentTarget != null
            && _lockOn.CurrentTarget.root == _boss.transform.root)
        {
            _lockedFrames++;
            Vector3 c = _bossRenderer != null ? _bossRenderer.bounds.center : _boss.transform.position + Vector3.up * 1.2f;
            Vector3 v = _cam.WorldToViewportPoint(c);
            if (v.z <= 0f || v.x < 0f || v.x > 1f || v.y < 0f || v.y > 1f)
                _bossOffscreenFrames++;

            if (_playerRoot != null)
            {
                Vector3 d = _boss.transform.position - _playerRoot.position;
                d.y = 0f;
                float dist = d.magnitude;
                _minBossDistance = Mathf.Min(_minBossDistance, dist);
                _maxBossDistance = Mathf.Max(_maxBossDistance, dist);
                _bossDistanceBuckets[dist < 2f ? 0 : dist < 4f ? 1 : 2] += 1f;
            }
        }

        // near plane overlap
        float near = _cam.nearClipPlane;
        float halfH = near * Mathf.Tan(_cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
        float radius = Mathf.Sqrt(halfH * halfH * (1f + _cam.aspect * _cam.aspect) + near * near);
        int n = Physics.OverlapSphereNonAlloc(ct.position, radius, _overlap, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            if (IsCharacterCollider(_overlap[i]))
                continue;
            _insideGeometryFrames++;
            break;
        }

        // vertical jitter window (dodge / hit + 0.3 s)
        bool active = (_dodge != null && _dodge.IsDodging) || (_combat != null && _combat.IsInHit);
        if (active)
            _windowUntil = Time.unscaledTime + 0.3f;

        TryAutoCapture();

        _samples.Add(new Sample
        {
            t = Time.unscaledTime,
            y = ct.position.y,
            rigY = SoulsCameraRig.Active != null ? SoulsCameraRig.Active.LastBasePosition.y : ct.position.y,
            window = Time.unscaledTime <= _windowUntil,
            cam = CamId()
        });
    }

    // ---- 상황별 자동 스크린샷 (captureDir 지정 시, 키마다 1회)
    public string captureDir;
    int _lastThreatCount = OffscreenThreatIndicator.ShownCount;
    float _threatCaptureAt;
    readonly HashSet<string> _captured = new HashSet<string>();
    float _nextCaptureAt;
    public string CapturedKeys => string.Join(",", _captured);

    void Capture(string key)
    {
        if (string.IsNullOrEmpty(captureDir) || _captured.Contains(key) || Time.unscaledTime < _nextCaptureAt)
            return;
        _captured.Add(key);
        _nextCaptureAt = Time.unscaledTime + 0.4f;
        System.IO.Directory.CreateDirectory(captureDir);
        ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(captureDir, label + "_" + key + ".png"));
    }

    void TryAutoCapture()
    {
        if (string.IsNullOrEmpty(captureDir) || _playerRoot == null)
            return;

        SoulsCameraRig rig = SoulsCameraRig.Active;
        if (OffscreenThreatIndicator.ShownCount > _lastThreatCount)
        {
            _lastThreatCount = OffscreenThreatIndicator.ShownCount;
            _threatCaptureAt = Time.unscaledTime + 0.12f; // 화살표 팝인 뒤
        }
        if (_threatCaptureAt > 0f && Time.unscaledTime >= _threatCaptureAt)
        {
            _threatCaptureAt = 0f;
            Capture("offscreen_threat");
        }
        bool lockedBoss = _boss != null && _lockOn != null && _lockOn.IsLockedOn() && _lockOn.CurrentTarget != null && _lockOn.CurrentTarget.root == _boss.transform.root;
        if (lockedBoss)
        {
            Vector3 d = _boss.transform.position - _playerRoot.position;
            d.y = 0f;
            float dist = d.magnitude;
            if (dist < 1.8f && Time.unscaledTime - _startTime > 4f) Capture("boss_close");
            if (dist > 4f && dist < 6f) Capture("boss_mid");
            if (rig != null && Mathf.Abs(rig.LockYawError) > 55f) Capture("boss_behind");
        }
        if (rig != null && rig.CollisionDistance > 0f && rig.CollisionDistance < 1.3f && Time.unscaledTime - _startTime > 2f) Capture("wall_pinned");
        if (_lockOn != null && _lockOn.IsLockedOn() && _lockOn.CurrentTarget != null && _lockOn.CurrentTarget.root.GetComponentInChildren<DroneController>() != null) Capture("lock_drone");
    }

    static bool IsCharacterCollider(Collider c)
    {
        if (c == null)
            return true;
        Transform root = c.transform.root;
        return root.GetComponentInChildren<CharacterController>() != null
            || root.GetComponent<IDamageReceiver>() != null
            || root.GetComponent<IHealth>() != null
            || root.GetComponent<PlayerReferences>() != null
            || root.GetComponent<BossController>() != null
            || root.GetComponent<DroneController>() != null;
    }

    float ComputeJitter(bool useRig, out int windowSamples)
    {
        float max = 0f;
        windowSamples = 0;
        int lo = 0, hi = 0;
        float sum = 0f;
        for (int i = 0; i < _samples.Count; i++)
        {
            float t = _samples[i].t;
            while (hi < _samples.Count && _samples[hi].t <= t + 0.15f) { sum += useRig ? _samples[hi].rigY : _samples[hi].y; hi++; }
            while (lo < hi && _samples[lo].t < t - 0.15f) { sum -= useRig ? _samples[lo].rigY : _samples[lo].y; lo++; }
            if (!_samples[i].window || hi - lo < 3)
                continue;
            windowSamples++;
            float mean = sum / (hi - lo);
            float y = useRig ? _samples[i].rigY : _samples[i].y;
            float dev = Mathf.Abs(y - mean);
            if (dev > max && !useRig)
            {
                _jitterAt = _samples[i].t - _startTime;
                _jitterCam = _samples[i].cam;
            }
            max = Mathf.Max(max, dev);
        }
        return max;
    }

    public string Report()
    {
        float jitter = ComputeJitter(false, out int windowSamples);
        float rigJitter = ComputeJitter(true, out _);
        var sb = new StringBuilder();
        sb.Append($"[CAM] {label}: {Time.unscaledTime - _startTime:F0}s gameplayFrames={_gameplayFrames} cinematicFrames={_cinematicFrames}\n");
        sb.Append($"[CAM]  bossOffscreen={(_lockedFrames > 0 ? 100f * _bossOffscreenFrames / _lockedFrames : 0f):F1}% ({_bossOffscreenFrames}/{_lockedFrames} locked frames)\n");
        sb.Append($"[CAM]  yawSpeed max={_maxYawSpeed:F0}deg/s mean={(_yawSamples > 0 ? _sumYawSpeed / _yawSamples : 0f):F1} framesOver240={_yawOverFrames}\n");
        sb.Append($"[CAM]  insideGeometryFrames={_insideGeometryFrames}\n");
        sb.Append($"[CAM]  verticalJitter(dodge/hit) max={jitter * 100f:F1}cm at {_jitterAt:F1}s cam={(_camNames.TryGetValue(_jitterCam, out string jn) ? jn : "?")} (rig base {rigJitter * 100f:F1}cm) over {windowSamples} frames\n");
        foreach (string s in _spikes)
            sb.Append("[CAM]  yaw spike ").Append(s).Append('\n');
        float total = _bossDistanceBuckets[0] + _bossDistanceBuckets[1] + _bossDistanceBuckets[2];
        if (total > 0f)
            sb.Append($"[CAM]  bossDist min={_minBossDistance:F1} max={_maxBossDistance:F1} <2m={100f * _bossDistanceBuckets[0] / total:F0}% 2-4m={100f * _bossDistanceBuckets[1] / total:F0}% >4m={100f * _bossDistanceBuckets[2] / total:F0}%");
        return sb.ToString();
    }
}
#endif
