using System;
using System.Collections;
using Cinemachine;
using UnityEngine;
using UnityEngine.Playables;

public class PlayerUltimateController : MonoBehaviour
{
    [Header("Gauge")]
    public float gaugeMax = 100f;
    public float gaugePerH = 3f;
    public float gaugePerParry = 6f;
    public float gaugePerPerfectDodge = 16f;

    [Header("Activation")]
    public bool allowInAir = false;
    public bool blockWhenStaggered = true;

    [Header("Cinematic State")]
    public bool invulnerableDuringCinematic = true;
    public bool lockInputDuringCinematic = true;
    public bool restoreCameraAndLockOn = true;

    [Header("Damage")]
    public int finisherFixedDamage = 1200;
    public int multihitFixedDamage = 120;

    [Header("Timeline")]
    public PlayableDirector director;
    public CinemachineVirtualCamera[] vCams;

    [Header("Ultimate Camera Shots")]
    [SerializeField] private CinemachineVirtualCamera prepareCam;
    [SerializeField] private CinemachineVirtualCamera actionCam;
    [SerializeField] private CinemachineVirtualCamera finisherCam;
    [SerializeField] private bool overridePrepareShot = true;
    [SerializeField] private bool overrideFinisherShot = true;
    [SerializeField] private float introStageEndTime = 1f;
    [SerializeField] private float finisherStageStartTime = 2.53f;
    [SerializeField] private string introSwordObjectName = "Object002";
    [SerializeField] private Vector3 introCameraLocalOffset = new Vector3(1.28f, 1.2f, 0.58f);
    [SerializeField] private Vector3 introSwordLookLocalOffset = new Vector3(0.05f, 0.04f, 0.04f);
    [SerializeField] private float introShotFov = 29f;
    [SerializeField] private bool useSwordRelativeIntroShot = true;
    [SerializeField] private float introSwordRightOffset = 0.82f;
    [SerializeField] private float introSwordUpOffset = 0.56f;
    [SerializeField] private float introSwordBackOffset = 0.72f;
    [SerializeField] private float introSwordForwardOffset = 0.18f;
    [SerializeField] private float introSwordLookUpOffset = 0.08f;
    [SerializeField] private float introSwordLookForwardOffset = 0.10f;
    [SerializeField] private Vector3 finisherCameraLocalOffset = new Vector3(0.05f, 1.34f, 2.55f);
    [SerializeField] private Vector3 finisherLookLocalOffset = new Vector3(0f, 1.18f, 0.08f);
    [SerializeField] private float finisherShotFov = 32f;

    [Header("Screen FX")]
    public UltimateScreenFX screenFX;
    public GameObject glassCrackPrefab;

    [Header("Events")]
    public Action OnUltimateStarted;
    public Action OnUltimateEnded;

    public float Gauge { get; private set; }

    private bool _isCinematic;

    private IInputBlocker _input;
    private ILockOnController _lockOn;
    private IInvulnerabilityToggle _invul;
    private ICombatStateReader _combat;

    private Transform _cachedSwordTransform;

    private Transform _originalPrepareFollow;
    private Transform _originalPrepareLookAt;
    private float _originalPrepareFov;
    private bool _prepareOverrideCaptured;

    private Transform _originalFinisherFollow;
    private Transform _originalFinisherLookAt;
    private float _originalFinisherFov;
    private bool _finisherOverrideCaptured;

    private void Awake()
    {
        _input = GetComponent<IInputBlocker>();
        _lockOn = GetComponent<ILockOnController>();
        _invul = GetComponent<IInvulnerabilityToggle>();
        _combat = GetComponent<ICombatStateReader>();

        ResolveCinematicCameras();
        ResolveSwordTransform();
    }

    private void OnDisable()
    {
        RestoreCinematicCameraOverrides();
    }

    public void AddGauge(float amount)
    {
        if (_isCinematic) return;

        Gauge = Mathf.Clamp(Gauge + amount, 0f, gaugeMax);
        UI_UltimateGauge.UpdateValue(Gauge / gaugeMax);
    }

    public bool TryActivate()
    {
        if (_isCinematic) return false;
        if (Gauge < gaugeMax) return false;
        if (blockWhenStaggered && _combat != null && _combat.IsStaggered()) return false;
        if (!allowInAir && _combat != null && _combat.IsInAir()) return false;

        StartCoroutine(CoCinematic());
        return true;
    }

    private IEnumerator CoCinematic()
    {
        _isCinematic = true;
        Gauge = 0f;
        UI_UltimateGauge.UpdateValue(0f);

        ResolveCinematicCameras();
        ResolveSwordTransform();

        if (lockInputDuringCinematic) _input?.BlockAll(true);
        if (invulnerableDuringCinematic) _invul?.SetInvulnerable(true);
        _lockOn?.GiveCameraControlToTimeline(true);

        OnUltimateStarted?.Invoke();
        screenFX?.PlayChargeIn();

        if (director != null)
        {
            director.time = 0d;
            director.Evaluate();
            director.Play();
        }

        while (director != null && director.state == PlayState.Playing)
        {
            UpdateCinematicCameraOverrides((float)director.time);
            yield return null;
        }

        screenFX?.PlayChargeOut();
        RestoreCinematicCameraOverrides();

        if (restoreCameraAndLockOn) _lockOn?.GiveCameraControlToTimeline(false);
        if (invulnerableDuringCinematic) _invul?.SetInvulnerable(false);
        if (lockInputDuringCinematic) _input?.BlockAll(false);

        OnUltimateEnded?.Invoke();
        _isCinematic = false;
    }

    public void OnMultiHit()
    {
        ApplyBurstDamage(multihitFixedDamage);
        screenFX?.PulseMinor();
    }

    public void OnFinisher()
    {
        ApplyBurstDamage(finisherFixedDamage);
        if (glassCrackPrefab != null)
            Instantiate(glassCrackPrefab, Vector3.zero, Quaternion.identity);
        screenFX?.PulseMajor();
    }

    private void ApplyBurstDamage(int damage)
    {
        var target = _lockOn?.GetCurrentTarget();
        if (target != null && target.TryGetComponent<IUltimateTarget>(out var ultimateTarget))
        {
            ultimateTarget.ApplyUltimateDamage(damage);
            return;
        }

        var boss = GameObject.FindWithTag("Boss");
        if (boss != null && boss.TryGetComponent<IUltimateTarget>(out var fallbackTarget))
            fallbackTarget.ApplyUltimateDamage(damage);
    }

    private void ResolveCinematicCameras()
    {
        if (prepareCam == null && vCams != null && vCams.Length > 0)
            prepareCam = vCams[0];

        if (actionCam == null && vCams != null && vCams.Length > 1)
            actionCam = vCams[1];

        if (finisherCam == null && vCams != null && vCams.Length > 2)
            finisherCam = vCams[2];

        if (finisherCam == null)
        {
            var finisherObject = GameObject.Find("Vcam_Finisher");
            if (finisherObject != null)
                finisherCam = finisherObject.GetComponent<CinemachineVirtualCamera>();
        }
    }

    private void ResolveSwordTransform()
    {
        if (_cachedSwordTransform != null) return;

        _cachedSwordTransform = FindChildRecursive(transform, introSwordObjectName);
        if (_cachedSwordTransform == null)
            _cachedSwordTransform = FindChildRecursive(transform, "sword");
        if (_cachedSwordTransform == null)
            _cachedSwordTransform = FindChildRecursive(transform, "weapon_r");
    }

    private void UpdateCinematicCameraOverrides(float timelineTime)
    {
        if (overridePrepareShot && prepareCam != null && timelineTime <= introStageEndTime)
            ApplyPrepareCameraOverride();

        if (overrideFinisherShot && finisherCam != null && timelineTime >= finisherStageStartTime)
            ApplyFinisherCameraOverride();
    }

    private void ApplyPrepareCameraOverride()
    {
        if (!_prepareOverrideCaptured)
        {
            _originalPrepareFollow = prepareCam.Follow;
            _originalPrepareLookAt = prepareCam.LookAt;
            _originalPrepareFov = prepareCam.m_Lens.FieldOfView;
            _prepareOverrideCaptured = true;
        }

        prepareCam.Follow = null;
        prepareCam.LookAt = null;

        Vector3 cameraPos = transform.TransformPoint(introCameraLocalOffset);
        Vector3 lookTarget = _cachedSwordTransform != null
            ? _cachedSwordTransform.TransformPoint(introSwordLookLocalOffset)
            : transform.TransformPoint(new Vector3(0.55f, 1.12f, 0.72f));

        Vector3 lookDir = lookTarget - cameraPos;
        if (lookDir.sqrMagnitude < 0.0001f)
            lookDir = transform.forward;

        prepareCam.transform.position = cameraPos;
        prepareCam.transform.rotation = Quaternion.LookRotation(lookDir.normalized, Vector3.up);
        prepareCam.m_Lens.FieldOfView = introShotFov;
    }

    private void ApplyFinisherCameraOverride()
    {
        if (!_finisherOverrideCaptured)
        {
            _originalFinisherFollow = finisherCam.Follow;
            _originalFinisherLookAt = finisherCam.LookAt;
            _originalFinisherFov = finisherCam.m_Lens.FieldOfView;
            _finisherOverrideCaptured = true;
        }

        finisherCam.Follow = null;
        finisherCam.LookAt = null;

        Vector3 cameraPos = transform.TransformPoint(finisherCameraLocalOffset);
        Vector3 lookTarget = transform.TransformPoint(finisherLookLocalOffset);
        Vector3 lookDir = lookTarget - cameraPos;
        if (lookDir.sqrMagnitude < 0.0001f)
            lookDir = transform.forward;

        finisherCam.transform.position = cameraPos;
        finisherCam.transform.rotation = Quaternion.LookRotation(lookDir.normalized, Vector3.up);
        finisherCam.m_Lens.FieldOfView = finisherShotFov;
    }

    private void RestoreCinematicCameraOverrides()
    {
        if (_prepareOverrideCaptured && prepareCam != null)
        {
            prepareCam.Follow = _originalPrepareFollow;
            prepareCam.LookAt = _originalPrepareLookAt;
            prepareCam.m_Lens.FieldOfView = _originalPrepareFov;
            _prepareOverrideCaptured = false;
        }

        if (_finisherOverrideCaptured && finisherCam != null)
        {
            finisherCam.Follow = _originalFinisherFollow;
            finisherCam.LookAt = _originalFinisherLookAt;
            finisherCam.m_Lens.FieldOfView = _originalFinisherFov;
            _finisherOverrideCaptured = false;
        }
    }

    private static Transform FindChildRecursive(Transform root, string childName)
    {
        if (root == null || string.IsNullOrEmpty(childName))
            return null;

        int childCount = root.childCount;
        for (int i = 0; i < childCount; i++)
        {
            Transform child = root.GetChild(i);
            if (string.Equals(child.name, childName, StringComparison.OrdinalIgnoreCase))
                return child;

            Transform nested = FindChildRecursive(child, childName);
            if (nested != null)
                return nested;
        }

        return null;
    }
}
