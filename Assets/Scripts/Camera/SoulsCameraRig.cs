using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 소울라이크 게임플레이 카메라 (Docs/CameraDesign_Soulslike.md).
/// 자유 시점과 락온을 하나의 솔버로 계산해 패스스루 CinemachineCamera(바디/에임 없음)의 Transform 에 직접 쓴다.
/// - 기존 FreeLook / CM_LockOnCam 은 그대로 두되 우선순위를 이 카메라 아래로 내린다 (연출 카메라는 priority >= 100 으로 위에 뜬다).
/// - 이 컴포넌트(+프로필)가 있는 씬에서만 동작한다. MainScene/Lobby 는 기존 카메라 그대로.
/// 실행 순서 -40: CinemachineBrain(LateUpdate, 0) 보다 먼저 포즈를 쓴다.
/// </summary>
[DefaultExecutionOrder(-40)]
[DisallowMultipleComponent]
public sealed class SoulsCameraRig : MonoBehaviour
{
    public static SoulsCameraRig Active { get; private set; }
    public static SoulsCameraProfile ActiveProfile => Active != null && Active.isActiveAndEnabled ? Active.profile : null;

    [SerializeField] SoulsCameraProfile profile;
    [SerializeField] CinemachineCamera vcam;
    [Tooltip("게임플레이 카메라 우선순위. 연출 카메라(100+)보다 낮고 기존 FreeLook/락온 카메라(5~20)보다 높아야 한다.")]
    [SerializeField] int gameplayPriority = 95;
    [Tooltip("기존 FreeLook/CM_LockOnCam 우선순위가 이 카메라 이상으로 올라가면 20 으로 되돌린다.")]
    [SerializeField] bool demoteLegacyGameplayCameras = true;

    public SoulsCameraProfile Profile => profile;
    public CinemachineCamera VirtualCamera => vcam;
    /// <summary>흔들림을 더하기 전 카메라 위치 (지표 수집용).</summary>
    public Vector3 LastBasePosition { get; private set; }
    public float LockWeight => _lockWeight;
    /// <summary>피벗→카메라 충돌 보정 거리 (m, 지표/QA용).</summary>
    public float CollisionDistance => _collisionDistance;
    /// <summary>락온 요 추종 오차 (deg, 지표/QA용): 플레이어→타깃 방향과 현재 카메라 요의 차.</summary>
    public float LockYawError { get; private set; }

    Transform _player;
    Transform _facing;
    PlayerLockOn _lockOn;
    CinemachineBrain _brain;
    CameraOcclusionFader _fader;
    LockOnCameraManager _legacyManager;
    float _nextResolveAt;

    float _yaw;
    float _pitch = 10f;
    float _yawVelocity;
    float _pitchVelocity;
    float _lockViewPitch;
    float _lockViewPitchVelocity;
    bool _lockViewValid;
    float _zoomDistance;
    Vector3 _pivot;
    Vector3 _pivotVelocity;
    Vector3 _lastRawPivot;
    bool _initialized;
    float _lockWeight;
    float _collisionDistance = -1f;
    float _collisionVelocity;
    float _lastLookInputAt;
    float _padRamp;
    bool _wasLive;
    float _explicitSnapAt = -10f;
    Transform _lockRootBeforeCinematic;

    bool _recentering;
    float _recenterStartAt;
    float _recenterFromYaw;
    float _recenterFromPitch;

    float _shakeAmplitude;
    float _shakeUntil;
    float _shakeDuration;
    float _parryPunchAt = -10f;
    float _dodgeFovAt = -10f;
    float _lastPlayerFade = 1f;

    // 반복 16: 좁은 공간 (튜토리얼 골목에서 카메라가 벽에 눌려 화면 대부분이 검은 벽이던 문제)
    float _tight;
    float _shoulderSign = 1f;
    float _leftClear = 10f;
    float _rightClear = 10f;
    float _frontClear = 10f;
    /// <summary>좁은 공간 가중치 0..1 (QA/지표용).</summary>
    public float Tightness => _tight;

    readonly RaycastHit[] _hits = new RaycastHit[24];
    readonly Collider[] _overlaps = new Collider[16];
    static readonly Dictionary<int, bool> s_characterColliderCache = new Dictionary<int, bool>(256);
    static readonly Dictionary<int, bool> s_thinPropCache = new Dictionary<int, bool>(256);
    static readonly Dictionary<int, Renderer> s_targetRendererCache = new Dictionary<int, Renderer>(32);

    // ------------------------------------------------------------------ lifecycle

    void Awake()
    {
        if (vcam == null)
            vcam = GetComponent<CinemachineCamera>();
        if (vcam == null)
            vcam = gameObject.AddComponent<CinemachineCamera>();
    }

    void OnEnable()
    {
        Active = this;
        vcam.Priority = gameplayPriority;
    }

    void OnDisable()
    {
        if (Active == this)
            Active = null;
        if (_fader != null)
            _fader.SetPlayerProximityFade(1f);
        RestoreAllHiddenCharacters();
    }

    void Start()
    {
        InstallCinematicBlend();
        if (profile != null && profile.enableOffscreenThreats && GetComponent<OffscreenThreatIndicator>() == null)
            gameObject.AddComponent<OffscreenThreatIndicator>();
    }

    void InstallCinematicBlend()
    {
        _brain = ResolveBrain();
        if (_brain == null || profile == null)
            return;

        // 히트스톱/슬로모션(timeScale < 1) 중에도 연출 → 게임플레이 복귀 블렌드가 0.35 s 로 끝나도록 실시간 기준으로 갱신.
        _brain.IgnoreTimeScale = true;

        var settings = ScriptableObject.CreateInstance<CinemachineBlenderSettings>();
        settings.name = "SoulsCameraRig_RuntimeBlends";
        var list = new List<CinemachineBlenderSettings.CustomBlend>();
        if (_brain.CustomBlends != null && _brain.CustomBlends.CustomBlends != null)
            list.AddRange(_brain.CustomBlends.CustomBlends);
        list.Add(new CinemachineBlenderSettings.CustomBlend
        {
            From = "**ANY CAMERA**", // CinemachineBlenderSettings.kBlendFromAnyCameraLabel (internal)
            To = vcam.Name,
            Blend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.EaseInOut, Mathf.Max(0f, profile.cinematicBlendTime))
        });
        settings.CustomBlends = list.ToArray();
        _brain.CustomBlends = settings;
    }

    CinemachineBrain ResolveBrain()
    {
        Camera main = GameplaySceneCache.ResolveMainCamera();
        CinemachineBrain brain = main != null ? main.GetComponent<CinemachineBrain>() : null;
        return brain != null ? brain : FindObjectOfType<CinemachineBrain>();
    }

    void Resolve()
    {
        if (_player != null && _lockOn != null && Time.unscaledTime < _nextResolveAt)
            return;
        if (_player != null && _lockOn != null)
            return;
        _nextResolveAt = Time.unscaledTime + 0.5f;

        PlayerReferences refs = GameplaySceneCache.ResolvePlayerReferences();
        if (refs != null)
        {
            _player = refs.PlayerRoot != null ? refs.PlayerRoot : refs.transform;
            PlayerMoveController move = _player.GetComponentInChildren<PlayerMoveController>(true);
            _facing = move != null && move.FacingRoot != null ? move.FacingRoot : (refs.VisualRoot != null ? refs.VisualRoot : _player);
        }

        _lockOn = GameplaySceneCache.ResolvePlayerLockOn();
        if (_brain == null)
            _brain = ResolveBrain();
        if (_fader == null)
            _fader = FindObjectOfType<CameraOcclusionFader>();
        if (_legacyManager == null)
            _legacyManager = GameplaySceneCache.ResolveLockOnCameraManager();
    }

    // ------------------------------------------------------------------ public API

    /// <summary>락온 키 탭(타깃 없음): profile.recenterDuration 동안 플레이어 등 뒤로.</summary>
    public void RequestRecenter()
    {
        _recentering = true;
        _recenterStartAt = Time.unscaledTime;
        _recenterFromYaw = _yaw;
        _recenterFromPitch = _pitch;
    }

    /// <summary>즉시 플레이어 등 뒤로 (연출 복귀용). FreeLookCamera.SnapBehindPlayer 가 이쪽으로 위임한다.</summary>
    public void SnapBehindPlayer(float? pitch = null, float yawOffset = 0f)
    {
        Resolve();
        if (_player == null)
            return;
        SnapToYaw(PlayerYaw() + yawOffset, pitch);
    }

    /// <summary>즉시 지정 요/피치로 (월드 요, deg).</summary>
    public void SnapToYaw(float yaw, float? pitch = null)
    {
        Resolve();
        if (_player == null)
            return;

        if (!_initialized)
            _zoomDistance = profile != null ? profile.distance : 3.8f;
        _yaw = yaw;
        _pitch = Mathf.Clamp(pitch ?? (profile != null ? profile.defaultPitch : 10f), MinPitch, MaxPitch);
        _yawVelocity = 0f;
        _pitchVelocity = 0f;
        _recentering = false;
        _explicitSnapAt = Time.unscaledTime;
        _explicitSnapPending = !CinemachineCore.IsLive(vcam);
        SnapPivot();
    }

    public static bool TryRouteShake(float amplitude, float duration)
    {
        SoulsCameraRig rig = Active;
        if (rig == null || !rig.isActiveAndEnabled || rig.profile == null)
            return false;
        rig.AddShake(amplitude, duration);
        return true;
    }

    public static void NotifyParry()
    {
        if (Active != null)
            Active._parryPunchAt = Time.unscaledTime;
    }

    public static void NotifyPerfectDodge()
    {
        if (Active != null)
            Active._dodgeFovAt = Time.unscaledTime;
    }

    /// <summary>타깃 가슴(약점) 위치: 렌더러 바운드 바닥 + 높이 * chestHeightRatio.</summary>
    public static Vector3 GetTargetChestPoint(Transform target, float ratio)
    {
        if (target == null)
            return Vector3.zero;
        Renderer r = ResolveTargetRenderer(target.root);
        if (r == null)
            return target.position;
        Bounds b = r.bounds;
        return new Vector3(b.center.x, b.min.y + b.size.y * ratio, b.center.z);
    }

    public static bool IsBigTarget(Transform target, SoulsCameraProfile p)
    {
        if (target == null)
            return false;
        Transform root = target.root;
        if (root.GetComponentInChildren<BossController>() != null)
            return true;
        Renderer r = ResolveTargetRenderer(root);
        return r != null && p != null && r.bounds.size.y >= p.bigTargetHeight;
    }

    static Renderer ResolveTargetRenderer(Transform root)
    {
        if (root == null)
            return null;
        int key = root.GetInstanceID();
        if (s_targetRendererCache.TryGetValue(key, out Renderer cached) && cached != null)
            return cached;

        Renderer best = null;
        float bestVolume = -1f;
        foreach (Renderer r in root.GetComponentsInChildren<Renderer>())
        {
            if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer)
                continue;
            Vector3 s = r.bounds.size;
            float volume = s.x * s.y * s.z + (r is SkinnedMeshRenderer ? 1000f : 0f);
            if (volume > bestVolume) { bestVolume = volume; best = r; }
        }
        s_targetRendererCache[key] = best;
        return best;
    }

    // ------------------------------------------------------------------ main update

    float MinPitch => profile != null ? profile.minPitch : -35f;
    float MaxPitch => profile != null ? profile.maxPitch : 55f;

    float PlayerYaw()
    {
        Transform src = _facing != null ? _facing : _player;
        Vector3 f = Vector3.ProjectOnPlane(src.forward, Vector3.up);
        if (f.sqrMagnitude < 0.0001f)
            return _yaw;
        return Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
    }

    void SnapPivot()
    {
        if (_player == null || profile == null)
            return;
        _pivot = _player.position + Vector3.up * profile.pivotHeight;
        _lastRawPivot = _pivot;
        _pivotVelocity = Vector3.zero;
        _collisionDistance = -1f;
        _collisionVelocity = 0f;
        _initialized = true;
    }

    void LateUpdate()
    {
        if (profile == null || vcam == null)
            return;

        Resolve();
        if (_player == null)
            return;

        MaintainPriorities();

        float dt = Time.timeScale <= 0f ? 0f : Mathf.Min(Time.unscaledDeltaTime, 0.1f);
        float now = Time.unscaledTime;

        if (!_initialized)
        {
            _yaw = PlayerYaw();
            _pitch = profile.defaultPitch;
            _zoomDistance = profile.distance;
            SnapPivot();
        }

        // 브레인은 이 뒤(LateUpdate 0)에 갱신되므로 지금 메인 카메라 Transform 은 직전 프레임 출력이다.
        Camera outputCamera = _brain != null ? _brain.OutputCamera : null;
        bool live = CinemachineCore.IsLive(vcam);
        if (live && !_wasLive)
            OnBecameLive();
        else if (!live && _wasLive)
            _lockRootBeforeCinematic = _lockOn != null && _lockOn.IsLockedOn() && _lockOn.CurrentTarget != null ? _lockOn.CurrentTarget.root : null;
        _wasLive = live;

        Transform target = null;
        bool locked = _lockOn != null && _lockOn.IsLockedOn() && (target = _lockOn.CurrentTarget) != null && !_lockOn.IsTimelineOwningCamera;
        bool big = locked && IsBigTarget(target, profile);
        if (!locked && _lockWeight <= 0f)
            _lockViewValid = false;

        if (!live)
        {
            // 연출 중: 복귀 포즈(플레이어 등 뒤 / 락온 축)를 미리 맞춰 두고, vcam 은 현재 화면(연출 카메라 출력)을 그대로 따라간다.
            // → 타임라인이 컷으로 끝나도 첫 프레임은 연출 마지막 화면과 같고, 이후 내부 블렌드로 0.35 s 동안 게임플레이 포즈로 간다.
            PrepareReturnPose(locked, target);
            if (outputCamera != null)
            {
                vcam.transform.SetPositionAndRotation(outputCamera.transform.position, outputCamera.transform.rotation);
                vcam.Lens.FieldOfView = outputCamera.fieldOfView;
                LastBasePosition = outputCamera.transform.position;
            }
            if (_fader != null && _lastPlayerFade < 1f)
            {
                _lastPlayerFade = 1f;
                _fader.SetPlayerProximityFade(1f);
            }
            RestoreAllHiddenCharacters();
            return;
        }

        float transition = Mathf.Max(0.01f, profile.lockTransitionTime);
        _lockWeight = Mathf.MoveTowards(_lockWeight, locked ? 1f : 0f, dt / transition);
        float w = Mathf.SmoothStep(0f, 1f, _lockWeight);

        // ---- pivot (수평/수직 분리 감쇠)
        float narrowLift = profile.narrowPivotLift * _tight * (1f - 0.5f * w);
        Vector3 rawPivot = _player.position + Vector3.up * (profile.pivotHeight + narrowLift + (big ? profile.bigTargetExtraPivot * w : 0f));
        if ((rawPivot - _lastRawPivot).sqrMagnitude > 25f)
            SnapPivot();
        _lastRawPivot = rawPivot;
        if (dt > 0f)
        {
            _pivot.x = Mathf.SmoothDamp(_pivot.x, rawPivot.x, ref _pivotVelocity.x, profile.followDampingHorizontal, Mathf.Infinity, dt);
            _pivot.z = Mathf.SmoothDamp(_pivot.z, rawPivot.z, ref _pivotVelocity.z, profile.followDampingHorizontal, Mathf.Infinity, dt);
            _pivot.y = Mathf.SmoothDamp(_pivot.y, rawPivot.y, ref _pivotVelocity.y, profile.followDampingVertical, Mathf.Infinity, dt);
        }

        // ---- input
        Vector2 look = live && dt > 0f ? ReadLookInput(dt) : Vector2.zero;
        bool hasLook = look.sqrMagnitude > 0.000001f;
        if (hasLook)
            _lastLookInputAt = now;

        float planarTargetDistance = 0f;
        if (locked)
        {
            Vector3 toTarget = target.position - _player.position;
            toTarget.y = 0f;
            planarTargetDistance = toTarget.magnitude;
            float desiredYaw = planarTargetDistance > 0.05f ? Mathf.Atan2(toTarget.x, toTarget.z) * Mathf.Rad2Deg : _yaw;
            float damping = profile.lockYawDamping;
            float maxSpeed = profile.lockMaxYawSpeed;
            if (planarTargetDistance < profile.closeDeadZone)
            {
                damping /= Mathf.Max(0.1f, profile.closeDeadZoneFollow);
                maxSpeed *= profile.closeDeadZoneFollow;
            }

            LockYawError = Mathf.DeltaAngle(_yaw, desiredYaw);
            if (dt > 0f)
            {
                float next = Mathf.SmoothDampAngle(_yaw, desiredYaw, ref _yawVelocity, damping, maxSpeed, dt);
                float step = Mathf.Clamp(Mathf.DeltaAngle(_yaw, next), -maxSpeed * dt, maxSpeed * dt);
                _yaw += step;
                _pitch = Mathf.SmoothDamp(_pitch, profile.lockPitch, ref _pitchVelocity, profile.lockPitchDamping, Mathf.Infinity, dt);
            }
            _recentering = false;
        }
        else
        {
            _yawVelocity = 0f;
            _pitchVelocity = 0f;
            if (hasLook)
            {
                _recentering = false;
                _yaw += look.x;
                _pitch = Mathf.Clamp(_pitch - look.y, MinPitch, MaxPitch);
            }

            if (_recentering)
            {
                float t = Mathf.Clamp01((now - _recenterStartAt) / Mathf.Max(0.01f, profile.recenterDuration));
                float s = t * t * (3f - 2f * t);
                _yaw = Mathf.LerpAngle(_recenterFromYaw, PlayerYaw(), s);
                _pitch = Mathf.Lerp(_recenterFromPitch, profile.defaultPitch, s);
                if (t >= 1f)
                    _recentering = false;
            }
            else if (CameraOptionsRuntime.AutoRecenter && dt > 0f && now - _lastLookInputAt >= profile.autoRecenterDelay)
            {
                Vector3 v = _pivotVelocity;
                v.y = 0f;
                if (v.sqrMagnitude > 0.25f)
                    _yaw = Mathf.MoveTowardsAngle(_yaw, PlayerYaw(), profile.autoRecenterSpeed * dt);
            }
            else if (profile.narrowAutoAlign && dt > 0f && _tight > 0.35f && _frontClear < profile.narrowFacingWall
                     && now - _lastLookInputAt >= profile.narrowAlignDelay)
            {
                // 골목에서 벽을 정면으로 본 채 옆으로 달리는 경우: 이동 방향(트인 쪽)으로만 천천히 돈다.
                // 뒤로 달려오는 경우(>120°)·이동 방향도 막힌 경우는 돌지 않는다 (플레이어를 놀라게 하지 않기).
                Vector3 v = _pivotVelocity;
                v.y = 0f;
                float minSpeed = profile.narrowAlignMinSpeed;
                if (v.sqrMagnitude > minSpeed * minSpeed)
                {
                    float moveYaw = Mathf.Atan2(v.x, v.z) * Mathf.Rad2Deg;
                    float delta = Mathf.Abs(Mathf.DeltaAngle(_yaw, moveYaw));
                    if (delta > 20f && delta < 120f
                        && ProbeClearance(_pivot, Quaternion.Euler(0f, moveYaw, 0f) * Vector3.forward, 4.5f) >= 4f)
                        _yaw = Mathf.MoveTowardsAngle(_yaw, moveYaw, profile.narrowAlignSpeed * _tight * dt);
                }
            }
        }

        _yaw = Mathf.Repeat(_yaw + 180f, 360f) - 180f;

        if (profile.allowWheelZoom && live && Mouse.current != null)
        {
            float scroll = Mouse.current.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > 0.01f)
                _zoomDistance = Mathf.Clamp(_zoomDistance - Mathf.Sign(scroll) * 0.25f, profile.minDistance, profile.maxDistance);
        }
        else
        {
            _zoomDistance = profile.distance;
        }

        // ---- framing
        float shoulderLock = profile.lockShoulderFar;
        if (locked)
        {
            float shoulderT = Mathf.InverseLerp(profile.lockShoulderCloseDistance, profile.lockShoulderFarDistance, planarTargetDistance);
            shoulderLock = Mathf.Lerp(profile.lockShoulderClose, profile.lockShoulderFar, shoulderT);
        }
        float lockDistance = (profile.lockDistance > 0f ? profile.lockDistance : profile.distance) + (big ? profile.bigTargetExtraDistance : 0f);
        UpdateNarrowSpace(dt, Mathf.Lerp(_zoomDistance, lockDistance, w));
        // 좁은 공간: 자유 시점만 숄더를 트인 쪽으로, 거리는 처음부터 가깝게 (락온은 '플레이어 왼쪽·보스 오른쪽' 규칙 유지)
        float shoulder = Mathf.Lerp(profile.freeShoulderOffset * _shoulderSign, shoulderLock, w);
        float freeDistance = Mathf.Lerp(_zoomDistance, Mathf.Min(_zoomDistance, profile.narrowDistance), _tight);
        float distance = Mathf.Lerp(freeDistance, lockDistance, w);

        Quaternion yawRot = Quaternion.Euler(0f, _yaw, 0f);
        Vector3 right = yawRot * Vector3.right;
        Vector3 desired = _pivot + Quaternion.Euler(_pitch, _yaw, 0f) * new Vector3(0f, 0f, -distance) + right * shoulder;

        Vector3 position = ResolveCollision(_pivot, desired, dt);

        // 벽에 바짝 당겨지면 머리 뒤를 정면으로 보지 않도록 어깨 너머(위·오른쪽)로 빼고 시선을 조금 숙인다.
        float pulledIn = 0f;
        float pulledDistance = Vector3.Distance(_pivot, position);
        if (pulledDistance < distance - 0.05f && profile.pulledInStartDistance > profile.pulledInFullDistance)
        {
            pulledIn = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(profile.pulledInStartDistance, profile.pulledInFullDistance, pulledDistance));
            if (pulledIn > 0f)
                position = ApplyPulledInOffset(position, right, pulledIn);
        }

        // view rotation
        float viewYaw = _yaw;
        float viewPitch = _pitch - profile.freeFramingTilt + profile.narrowPitchAdd * _tight;
        if (locked || w > 0f)
        {
            float lockYaw = _yaw;
            float lockPitchView = _lockViewPitch;
            if (locked)
            {
                Vector3 playerChest = _player.position + Vector3.up * profile.playerChestHeight;
                Vector3 targetChest = GetTargetChestPoint(target, profile.chestHeightRatio);
                // 반복 16: 공중 타깃(Type-D 도약)은 시선을 일정 높이까지만 따라 올린다 — 예전엔 가슴점이 그대로 솟아 카메라가 천장을 봤다.
                Renderer targetRenderer = ResolveTargetRenderer(target.root);
                float targetBottom = targetRenderer != null ? targetRenderer.bounds.min.y : target.position.y;
                float rise = targetBottom - _player.position.y;
                if (rise > profile.lockAirborneLookRise)
                    targetChest.y -= rise - profile.lockAirborneLookRise;
                Vector3 lookPoint = Vector3.Lerp(playerChest, targetChest, profile.lockLookTargetBias);
                Vector3 forward = yawRot * Vector3.forward;
                float ahead = Mathf.Max(1.5f, Vector3.Dot(lookPoint - _player.position, forward));
                Vector3 axisPoint = new Vector3(_pivot.x, lookPoint.y, _pivot.z) + forward * ahead;
                // 충돌로 당겨진 위치가 아니라 원래 오빗 위치 기준으로 시선을 잡는다 (벽 당김 순간 요/피치가 튀지 않게)
                Vector3 toLook = axisPoint - desired;
                Vector3 planar = new Vector3(toLook.x, 0f, toLook.z);
                lockYaw = Mathf.Atan2(planar.x, planar.z) * Mathf.Rad2Deg;
                float desiredViewPitch = -Mathf.Atan2(toLook.y, Mathf.Max(0.5f, planar.magnitude)) * Mathf.Rad2Deg;
                if (big)
                {
                    float closeT = Mathf.InverseLerp(profile.bigTargetCloseDistance, profile.bigTargetCloseDistance - 1f, planarTargetDistance);
                    desiredViewPitch -= profile.bigTargetClosePitchUp * closeT;
                }
                // 올려다보기 상한 + 플레이어 몸 중앙이 화면 아래 가장자리 안에 남도록 (피치 + = 내려다봄)
                desiredViewPitch = Mathf.Max(desiredViewPitch, -profile.lockMaxLookUp);
                Vector3 toPlayer = _player.position + Vector3.up * 1.0f - desired;
                float playerPlanar = new Vector2(toPlayer.x, toPlayer.z).magnitude;
                float pitchToPlayer = -Mathf.Atan2(toPlayer.y, Mathf.Max(0.5f, playerPlanar)) * Mathf.Rad2Deg;
                float halfFov = vcam.Lens.FieldOfView * 0.5f;
                desiredViewPitch = Mathf.Max(desiredViewPitch, pitchToPlayer - halfFov * profile.lockPlayerFrameLimit);

                if (!_lockViewValid)
                {
                    // 자유 시점 시선 피치에서 출발해 블렌드가 튀지 않게 한다.
                    _lockViewPitch = viewPitch;
                    _lockViewPitchVelocity = 0f;
                    _lockViewValid = true;
                }
                if (dt > 0f)
                    _lockViewPitch = Mathf.SmoothDamp(_lockViewPitch, desiredViewPitch, ref _lockViewPitchVelocity, 0.15f, Mathf.Infinity, dt);
                lockPitchView = _lockViewPitch;
            }

            viewYaw = Mathf.LerpAngle(_yaw, lockYaw, w);
            viewPitch = Mathf.Lerp(viewPitch, lockPitchView, w);
        }
        viewPitch += profile.pulledInPitch * pulledIn;

        Quaternion rotation = Quaternion.Euler(viewPitch, viewYaw, 0f);
        LastBasePosition = position;

        // ---- shake (위치 노이즈 위주)
        float roll = 0f;
        if (now < _shakeUntil && _shakeDuration > 0f)
        {
            float k = Mathf.Clamp01((_shakeUntil - now) / _shakeDuration);
            float amp = _shakeAmplitude * k;
            float f = now * profile.shakeFrequency;
            float nx = Mathf.PerlinNoise(f, 0.17f) * 2f - 1f;
            float ny = Mathf.PerlinNoise(0.53f, f) * 2f - 1f;
            float nz = Mathf.PerlinNoise(f + 7.3f, 3.1f) * 2f - 1f;
            position += rotation * new Vector3(nx * amp, ny * amp * profile.shakeVerticalScale, nz * amp * 0.5f);
            roll = nx * amp * profile.shakeRotationScale;
        }
        if (Mathf.Abs(roll) > 0.0001f)
            rotation *= Quaternion.Euler(0f, 0f, roll);

        float fov = EvaluateFieldOfView(now);
        if (_returnBlendActive)
        {
            float t = Mathf.Clamp01((now - _returnBlendStartAt) / Mathf.Max(0.01f, profile.cinematicBlendTime));
            float s = t * t * (3f - 2f * t);
            position = Vector3.Lerp(_returnFromPosition, position, s);
            rotation = Quaternion.Slerp(_returnFromRotation, rotation, s);
            fov = Mathf.Lerp(_returnFromFov, fov, s);
            if (t >= 1f)
                _returnBlendActive = false;
        }

        vcam.transform.SetPositionAndRotation(position, rotation);
        vcam.Lens.FieldOfView = fov;

        UpdatePlayerFade(position);
        UpdateCharacterOverlapHide(position);
    }

    /// <summary>어깨 너머 보정 오프셋을 벽에 막히지 않는 만큼만 적용한다.</summary>
    Vector3 ApplyPulledInOffset(Vector3 position, Vector3 right, float weight)
    {
        Vector3 offset = Vector3.up * (profile.pulledInLift * weight) + right * (profile.pulledInSide * weight);
        float length = offset.magnitude;
        if (length < 0.001f)
            return position;
        Vector3 dir = offset / length;
        float radius = Mathf.Max(0.05f, NearPlaneRadius() + 0.02f);
        float allowed = length;
        int n = Physics.SphereCastNonAlloc(position, radius, dir, _hits, length, profile.collisionMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            RaycastHit hit = _hits[i];
            if (hit.collider == null || IsCharacterCollider(hit.collider) || IsThinProp(hit.collider, profile.thinPropMaxSize))
                continue;
            if (hit.distance <= 0f && hit.point == Vector3.zero)
                return position; // 시작부터 겹침: 보정하지 않는다
            allowed = Mathf.Min(allowed, hit.distance);
        }
        return position + dir * allowed;
    }

    // ------------------------------------------------------------------ character overlap (보스 몸 안으로 들어간 카메라)

    readonly List<Transform> _hiddenCharacters = new List<Transform>(4);
    readonly Dictionary<Transform, Renderer[]> _hiddenRenderers = new Dictionary<Transform, Renderer[]>(4);
    readonly List<Transform> _overlapCandidates = new List<Transform>(8);
    float _nextCandidateRefreshAt;
    static readonly List<Transform> s_tmpRestore = new List<Transform>(4);

    /// <summary>카메라가 보스/적 몸통(캐릭터 충돌은 무시하므로 들어갈 수 있음) 안이나 바로 앞이면 그 캐릭터 렌더링을 끈다.</summary>
    void UpdateCharacterOverlapHide(Vector3 cameraPosition)
    {
        if (Time.unscaledTime >= _nextCandidateRefreshAt)
        {
            _nextCandidateRefreshAt = Time.unscaledTime + 0.5f;
            _overlapCandidates.Clear();
            foreach (BossController boss in FindObjectsOfType<BossController>())
                if (boss != null && !_overlapCandidates.Contains(boss.transform))
                    _overlapCandidates.Add(boss.transform);
            foreach (DroneController drone in FindObjectsOfType<DroneController>())
                if (drone != null && !_overlapCandidates.Contains(drone.transform))
                    _overlapCandidates.Add(drone.transform);
        }

        float near = NearPlaneRadius();
        for (int i = 0; i < _overlapCandidates.Count; i++)
        {
            Transform c = _overlapCandidates[i];
            if (c == null)
                continue;
            bool hidden = _hiddenRenderers.ContainsKey(c);
            if (!c.gameObject.activeInHierarchy)
            {
                if (hidden) SetCharacterHidden(c, false);
                continue;
            }

            Renderer main = ResolveTargetRenderer(c);
            if (main == null)
                continue;
            Bounds b = main.bounds;
            float height = Mathf.Clamp(b.size.y, 0.4f, 6f);
            float radius = Mathf.Clamp(Mathf.Min(b.size.x, b.size.z) * 0.32f, 0.25f, 1.1f);
            Vector3 bottom = new Vector3(b.center.x, b.min.y + radius, b.center.z);
            Vector3 top = new Vector3(b.center.x, Mathf.Max(bottom.y, b.max.y - radius), b.center.z);
            float dist = DistanceToSegment(cameraPosition, bottom, top) - radius;
            float enter = near + profile.characterHideMargin;
            if (!hidden && dist < enter)
                SetCharacterHidden(c, true);
            else if (hidden && dist > enter + 0.2f)
                SetCharacterHidden(c, false);
        }

        // 사라진 후보 정리
        s_tmpRestore.Clear();
        foreach (KeyValuePair<Transform, Renderer[]> kv in _hiddenRenderers)
            if (kv.Key == null || !_overlapCandidates.Contains(kv.Key))
                s_tmpRestore.Add(kv.Key);
        for (int i = 0; i < s_tmpRestore.Count; i++)
            SetCharacterHidden(s_tmpRestore[i], false);
    }

    void SetCharacterHidden(Transform character, bool hide)
    {
        if (hide)
        {
            if (character == null || _hiddenRenderers.ContainsKey(character))
                return;
            var list = new List<Renderer>();
            foreach (Renderer r in character.GetComponentsInChildren<Renderer>())
            {
                if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer || r.forceRenderingOff)
                    continue;
                r.forceRenderingOff = true;
                list.Add(r);
            }
            _hiddenRenderers[character] = list.ToArray();
            return;
        }

        if (ReferenceEquals(character, null) || !_hiddenRenderers.TryGetValue(character, out Renderer[] renderers))
            return;
        for (int i = 0; i < renderers.Length; i++)
            if (renderers[i] != null)
                renderers[i].forceRenderingOff = false;
        _hiddenRenderers.Remove(character);
    }

    void RestoreAllHiddenCharacters()
    {
        if (_hiddenRenderers.Count == 0)
            return;
        s_tmpRestore.Clear();
        s_tmpRestore.AddRange(_hiddenRenderers.Keys);
        for (int i = 0; i < s_tmpRestore.Count; i++)
            SetCharacterHidden(s_tmpRestore[i], false);
    }

    /// <summary>QA/지표용: 카메라 겹침으로 숨겨진 캐릭터 수.</summary>
    public int HiddenCharacterCount => _hiddenRenderers.Count;

    static float DistanceToSegment(Vector3 p, Vector3 a, Vector3 b)
    {
        Vector3 ab = b - a;
        float len2 = ab.sqrMagnitude;
        float t = len2 > 1e-6f ? Mathf.Clamp01(Vector3.Dot(p - a, ab) / len2) : 0f;
        return Vector3.Distance(p, a + ab * t);
    }

    /// <summary>마지막으로 연출 → 게임플레이 복귀한 시각 (unscaled).</summary>
    public float LastBecameLiveAt { get; private set; } = -10f;

    /// <summary>연출 → 게임플레이 내부 블렌드 중 (지표에서는 연출 프레임으로 본다).</summary>
    public bool IsBlendingFromCinematic => _returnBlendActive && Time.unscaledTime - _returnBlendStartAt < Mathf.Max(0.01f, profile != null ? profile.cinematicBlendTime : 0.35f);

    bool _returnBlendActive;
    float _returnBlendStartAt;
    Vector3 _returnFromPosition;
    Quaternion _returnFromRotation;
    float _returnFromFov;
    bool _explicitSnapPending;

    // 연출 중 매 프레임: 복귀 시 쓸 포즈를 플레이어 등 뒤(락온이면 플레이어→타깃 축)로 맞춰 둔다.
    void PrepareReturnPose(bool locked, Transform target)
    {
        if (!_explicitSnapPending)
        {
            _yaw = PlayerYaw();
            if (locked)
            {
                Vector3 d = target.position - _player.position;
                d.y = 0f;
                if (d.sqrMagnitude > 0.01f)
                    _yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            }
            _pitch = locked ? profile.lockPitch : profile.defaultPitch;
        }

        _yawVelocity = 0f;
        _pitchVelocity = 0f;
        _recentering = false;
        _lockWeight = locked ? 1f : 0f;
        _lockViewPitchVelocity = 0f;
        _lockViewValid = false;
        SnapPivot();
    }

    void OnBecameLive()
    {
        LastBecameLiveAt = Time.unscaledTime;
        _explicitSnapPending = false;

        // vcam 은 연출 중 화면을 따라가고 있었으므로 그 포즈에서 게임플레이 포즈로 내부 블렌드
        _returnBlendActive = profile.cinematicBlendTime > 0f;
        _returnBlendStartAt = Time.unscaledTime;
        _returnFromPosition = vcam.transform.position;
        _returnFromRotation = vcam.transform.rotation;
        _returnFromFov = vcam.Lens.FieldOfView;

        // 연출 전 락온 상태 복원
        bool locked = _lockOn != null && _lockOn.IsLockedOn() && _lockOn.CurrentTarget != null;
        Transform root = _lockRootBeforeCinematic;
        _lockRootBeforeCinematic = null;
        if (!locked && root != null && root.gameObject.activeInHierarchy && _lockOn != null && !PlayerLockOn.IsRootDead(root)
            && Vector3.Distance(root.position, _player.position) <= profile.lockBreakDistance)
        {
            _lockOn.LockTo(root);
            _lockWeight = 1f;
        }
    }

    void MaintainPriorities()
    {
        if (vcam.Priority.Value != gameplayPriority)
            vcam.Priority = gameplayPriority;
        if (!demoteLegacyGameplayCameras || _legacyManager == null)
            return;
        Demote(_legacyManager.freeLookCam);
        Demote(_legacyManager.lockOnCam);
    }

    void Demote(CinemachineVirtualCameraBase cam)
    {
        if (cam != null && cam.Priority.Value >= gameplayPriority)
            cam.Priority = 20;
    }

    Vector2 ReadLookInput(float dt)
    {
        if (Cursor.lockState != CursorLockMode.Locked && Gamepad.current == null)
            return Vector2.zero;

        Vector2 look = Vector2.zero;
        if (Mouse.current != null && Cursor.lockState == CursorLockMode.Locked)
            look += Mouse.current.delta.ReadValue() * (profile.mouseDegreesPerPixel * CameraOptionsRuntime.MouseSensitivity);

        Gamepad pad = Gamepad.current;
        if (pad != null)
        {
            Vector2 stick = pad.rightStick.ReadValue();
            float m = stick.magnitude;
            float dz = profile.padDeadZone;
            if (m > dz)
            {
                float norm = Mathf.Clamp01((m - dz) / Mathf.Max(0.01f, 1f - dz));
                float curve = Mathf.Pow(norm, Mathf.Max(0.5f, profile.padResponseExponent));
                _padRamp = norm > 0.9f ? Mathf.MoveTowards(_padRamp, 1f, dt / Mathf.Max(0.01f, profile.padAccelRampTime)) : 0f;
                float accel = Mathf.Lerp(profile.padAccelStartMultiplier, 1f, _padRamp);
                Vector2 dir = stick / m;
                float sens = CameraOptionsRuntime.PadSensitivity;
                look.x += dir.x * curve * accel * profile.padYawSpeed * sens * dt;
                look.y += dir.y * curve * accel * profile.padPitchSpeed * sens * dt;
            }
            else
            {
                _padRamp = 0f;
            }
        }

        if (CameraOptionsRuntime.InvertY)
            look.y = -look.y;
        return look;
    }

    // ------------------------------------------------------------------ narrow space (반복 16)

    /// <summary>
    /// 좌우/정면 여유(피벗 기준 구체 캐스트 3개)와 뒤쪽 충돌 당김 비율로 '좁음' 가중치를 갱신한다.
    /// 들어갈 때 0.3 s, 풀릴 때 0.8 s 로 움직여 잡동사니 많은 곳에서 거리/피벗이 떨리지 않게 한다.
    /// </summary>
    void UpdateNarrowSpace(float dt, float referenceDistance)
    {
        Quaternion yawRot = Quaternion.Euler(0f, _yaw, 0f);
        Vector3 right = yawRot * Vector3.right;
        float probe = Mathf.Max(1f, profile.narrowWidthStart);
        _rightClear = ProbeClearance(_pivot, right, probe);
        _leftClear = ProbeClearance(_pivot, -right, probe);
        _frontClear = ProbeClearance(_pivot, yawRot * Vector3.forward, profile.narrowFacingWall + 0.5f);

        float widthT = Mathf.InverseLerp(profile.narrowWidthStart, profile.narrowWidthFull, _rightClear + _leftClear);
        float pullT = 0f;
        if (_collisionDistance > 0f && referenceDistance > 0.1f)
            pullT = Mathf.InverseLerp(profile.narrowPullRatioStart, profile.narrowPullRatioFull, _collisionDistance / referenceDistance);
        float target = Mathf.Max(widthT, pullT);
        if (dt > 0f)
            _tight = Mathf.MoveTowards(_tight, target, dt / (target > _tight ? 0.3f : 0.8f));

        float sign = profile.narrowSideShift && _rightClear < 1.3f && _leftClear > _rightClear + 0.6f ? -1f : 1f;
        if (dt > 0f)
            _shoulderSign = Mathf.MoveTowards(_shoulderSign, sign, dt * 2.5f);
    }

    float ProbeClearance(Vector3 origin, Vector3 dir, float maxDistance)
    {
        float clear = maxDistance;
        int n = Physics.SphereCastNonAlloc(origin, 0.2f, dir, _hits, maxDistance, profile.collisionMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            RaycastHit hit = _hits[i];
            if (hit.collider == null || hit.distance <= 0f && hit.point == Vector3.zero)
                continue;
            if (IsCharacterCollider(hit.collider) || IsThinProp(hit.collider, profile.thinPropMaxSize))
                continue;
            clear = Mathf.Min(clear, hit.distance);
        }
        return clear;
    }

    // ------------------------------------------------------------------ collision

    Vector3 ResolveCollision(Vector3 origin, Vector3 desired, float dt)
    {
        Vector3 delta = desired - origin;
        float maxDistance = delta.magnitude;
        if (maxDistance < 0.001f)
            return desired;
        Vector3 dir = delta / maxDistance;

        float radius = profile.collisionRadius;
        float allowed = maxDistance;
        int n = Physics.SphereCastNonAlloc(origin, radius, dir, _hits, maxDistance, profile.collisionMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            RaycastHit hit = _hits[i];
            if (hit.collider == null || hit.distance <= 0f && hit.point == Vector3.zero)
                continue;
            if (IsCharacterCollider(hit.collider))
                continue;
            if (IsThinProp(hit.collider, profile.thinPropMaxSize))
                continue;
            allowed = Mathf.Min(allowed, hit.distance);
        }
        allowed = Mathf.Max(Mathf.Min(profile.minCollisionDistance, maxDistance), allowed);

        if (_collisionDistance < 0f || dt <= 0f)
        {
            _collisionDistance = allowed;
            _collisionVelocity = 0f;
        }
        else
        {
            float smooth = allowed < _collisionDistance ? profile.collisionPullInTime : profile.collisionReleaseTime;
            _collisionDistance = Mathf.SmoothDamp(_collisionDistance, allowed, ref _collisionVelocity, Mathf.Max(0.001f, smooth), Mathf.Infinity, dt);
            // 당김 중에도 근평면이 벽을 넘지 않도록: 구 반경과 근평면 반경 차이만큼만 여유를 둔다.
            float hardLimit = allowed + Mathf.Max(0f, radius - NearPlaneRadius() - 0.02f);
            if (_collisionDistance > hardLimit)
            {
                _collisionDistance = hardLimit;
                _collisionVelocity = Mathf.Min(0f, _collisionVelocity);
            }
        }

        float d = Mathf.Min(_collisionDistance, maxDistance);
        Vector3 position = origin + dir * d;

        // 무시한 얇은 소품 내부에 카메라가 들어간 경우: 겹침이 없을 때까지 피벗 쪽으로 당긴다.
        float nearRadius = NearPlaneRadius() + 0.02f;
        for (int step = 0; step < 12 && d > profile.minCollisionDistance; step++)
        {
            if (!OverlapsEnvironment(position, nearRadius))
                break;
            d = Mathf.Max(profile.minCollisionDistance, d - 0.1f);
            position = origin + dir * d;
            _collisionDistance = Mathf.Min(_collisionDistance, d);
        }

        return position;
    }

    float NearPlaneRadius()
    {
        Camera cam = _brain != null ? _brain.OutputCamera : GameplaySceneCache.ResolveMainCamera();
        float near = cam != null ? cam.nearClipPlane : 0.1f;
        float aspect = cam != null ? cam.aspect : 16f / 9f;
        float fov = vcam != null ? vcam.Lens.FieldOfView : 50f;
        float halfH = near * Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad);
        return Mathf.Sqrt(halfH * halfH * (1f + aspect * aspect) + near * near);
    }

    bool OverlapsEnvironment(Vector3 position, float radius)
    {
        int n = Physics.OverlapSphereNonAlloc(position, radius, _overlaps, profile.collisionMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
            if (!IsCharacterCollider(_overlaps[i]))
                return true;
        return false;
    }

    public static bool IsCharacterCollider(Collider c)
    {
        if (c == null)
            return true;
        int key = c.GetInstanceID();
        if (s_characterColliderCache.TryGetValue(key, out bool cached))
            return cached;

        bool character = c is CharacterController
            || c.GetComponentInParent<PlayerReferences>() != null
            || c.GetComponentInParent<IDamageReceiver>() != null
            || c.GetComponentInParent<IHealth>() != null
            || c.GetComponentInParent<BossController>() != null
            || c.GetComponentInParent<DroneController>() != null
            || c.attachedRigidbody != null && !c.attachedRigidbody.isKinematic;
        s_characterColliderCache[key] = character;
        return character;
    }

    static bool IsThinProp(Collider c, float maxSize)
    {
        int key = c.GetInstanceID();
        if (s_thinPropCache.TryGetValue(key, out bool cached))
            return cached;
        Vector3 s = c.bounds.size;
        float a = s.x, b = s.y, d = s.z;
        // 두 번째로 큰 변
        float second = Mathf.Max(Mathf.Min(a, b), Mathf.Min(Mathf.Max(a, b), d));
        bool thin = second < maxSize;
        s_thinPropCache[key] = thin;
        return thin;
    }

    // ------------------------------------------------------------------ feedback

    void AddShake(float amplitude, float duration)
    {
        if (amplitude <= 0f || duration <= 0f)
            return;
        float now = Time.unscaledTime;
        float current = now < _shakeUntil && _shakeDuration > 0f ? _shakeAmplitude * Mathf.Clamp01((_shakeUntil - now) / _shakeDuration) : 0f;
        float stacked = Mathf.Max(current, amplitude) + 0.25f * Mathf.Min(current, amplitude);
        _shakeAmplitude = Mathf.Min(profile.maxStackedShake, stacked);
        float remaining = Mathf.Max(0f, _shakeUntil - now);
        _shakeDuration = Mathf.Max(remaining, duration);
        _shakeUntil = now + _shakeDuration;
    }

    float EvaluateFieldOfView(float now)
    {
        float fov = PlayerPrefs.HasKey(CameraOptionsRuntime.FieldOfViewKey) ? CameraOptionsRuntime.FieldOfView : profile.fieldOfView;

        float parryT = now - _parryPunchAt;
        float parryDuration = Mathf.Max(0.01f, profile.parryFovPunchDuration);
        if (parryT >= 0f && parryT < parryDuration)
            fov += profile.parryFovPunch * Mathf.Sin(Mathf.PI * parryT / parryDuration);

        const float dodgeAttack = 0.05f;
        float dodgeT = now - _dodgeFovAt;
        float release = Mathf.Max(0.01f, profile.perfectDodgeFovRelease);
        if (dodgeT >= 0f && dodgeT < dodgeAttack + release)
        {
            float k = dodgeT < dodgeAttack ? dodgeT / dodgeAttack : 1f - Mathf.SmoothStep(0f, 1f, (dodgeT - dodgeAttack) / release);
            fov += profile.perfectDodgeFov * k;
        }

        return fov;
    }

    void UpdatePlayerFade(Vector3 cameraPosition)
    {
        if (_fader == null)
            return;
        Vector3 chest = _player.position + Vector3.up * profile.playerChestHeight;
        float dist = Vector3.Distance(cameraPosition, chest);
        float t = Mathf.InverseLerp(profile.playerFadeEndDistance, profile.playerFadeStartDistance, dist);
        float alpha = Mathf.Lerp(profile.playerFadeMinAlpha, 1f, t);
        if (Mathf.Abs(alpha - _lastPlayerFade) < 0.02f && !(alpha >= 1f && _lastPlayerFade < 1f))
            return;
        _lastPlayerFade = alpha;
        _fader.SetPlayerProximityFade(alpha);
    }
}
