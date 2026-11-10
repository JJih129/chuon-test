using UnityEngine;
using UnityEngine.Events;
using System;
using System.Collections.Generic;

[DisallowMultipleComponent]
public class BossBreakController : MonoBehaviour
{
    public enum BreakSource
    {
        Generic = 0,
        Parry = 1
    }

    [Header("01. Break Gauge")]
    [Tooltip("Maximum break gauge value.")]
    [Min(1f)] public float maxBreak = 100f;

    [Tooltip("Break damage applied by a regular successful player hit.")]
    [Min(0f)] public float basicAttackBreakDamage = 1f;

    [Tooltip("Break damage applied by a successful parry.")]
    [Min(0f)] public float baseBreakPerHit = 12.5f;

    [Tooltip("How long the boss stays in break state.")]
    [Min(0f)] public float breakDuration = 8f;

    [Tooltip("Passive break gauge regen per second (outside break) once the player stops dealing break damage.")]
    [Min(0f)] public float recoveryPerSecond = 8f;

    [Tooltip("Seconds without receiving break damage before passive regen starts.")]
    [Min(0f)] public float recoveryDelay = 4f;

    [Header("01b. Break Finisher")]
    [Tooltip("Allow the player to perform a finisher on attack input while the boss is broken.")]
    public bool enableFinisher = true;

    [Tooltip("Seconds after break enter during which the finisher can be started. <= 0 = whole break.")]
    [Min(0f)] public float finisherWindowSeconds = 6f;

    [Tooltip("Max planar distance from player to boss to start the finisher.")]
    [Min(0.5f)] public float finisherRange = 3.6f;

    [Tooltip("Finisher damage as a fraction of boss max HP.")]
    [Range(0f, 1f)] public float finisherDamagePercentOfMaxHP = 0.12f;

    [Tooltip("Scaled seconds from finisher start until the finisher damage lands (should roughly match the swing).")]
    [Min(0f)] public float finisherHitDelay = 0.3f;

    [Tooltip("Scaled seconds after the finisher hit before the break ends early.")]
    [Min(0f)] public float finisherBreakExitDelay = 0.7f;

    [Tooltip("Camera shake on finisher hit (amplitude, duration). 0 disables.")]
    public Vector2 finisherCameraShake = new Vector2(0.35f, 0.3f);

    [Tooltip("피니셔 임팩트 연출 (슬로우모션 / 강한 카메라 셰이크 / 패드 진동 / 전용 SFX).")]
    public FinisherFeedback finisherFeedback = new FinisherFeedback();

    [Header("01c. Break Audio")]
    [Tooltip("브레이크 진입 시 재생할 SFX. 비어 있으면 무음.")]
    public AudioClip breakEnterClip;
    [Range(0f, 1f)] public float breakEnterVolume = 0.9f;

    [Tooltip("브레이크 종료(회복) 시 재생할 SFX. 보스 사망 시에는 재생하지 않습니다.")]
    public AudioClip breakExitClip;
    [Range(0f, 1f)] public float breakExitVolume = 0.7f;

    [Tooltip("선택: 전용 AudioSource. 비어 있으면 공용 OneShotAudio 풀을 사용합니다.")]
    public AudioSource breakAudioSource;

    [Header("02. References")]
    [Tooltip("Boss health/stagger controller.")]
    [SerializeField] private BossHealth bossHealth;

    [Tooltip("Boss animator that uses the break bool.")]
    [SerializeField] private Animator bossAnimator;

    [Tooltip("Animator bool parameter name for break state.")]
    [SerializeField] private string breakBoolName = "IsBreak";

    [Header("03. Break Animation Hold")]
    [SerializeField] private string breakStateName = "Break";
    [SerializeField, Range(0.05f, 1f)] private float breakHoldNormalizedTime = 0.4f;

    [Header("04. Break Hologram")]
    [SerializeField] private bool useBreakHologram = true;
    [SerializeField] private Transform hologramRoot;
    [SerializeField] private Material breakHologramMaterial;
    [SerializeField] private string breakHologramResourcePath = "Tutorial/TutorialHologramSpawn";
    [SerializeField] private Color breakHologramColor = new Color(0f, 0.72f, 1f, 0.42f);

    [Header("05. Events")]
    [Tooltip("Invoked when break state starts.")]
    public UnityEvent OnBreakEnter = new UnityEvent();

    [Tooltip("Invoked when break state ends.")]
    public UnityEvent OnBreakExit = new UnityEvent();

    public event Action<float, float> OnBreakChanged;

    [Header("06. Debug")]
    [Tooltip("Enable debug logging for break flow.")]
    public bool logDebug = false;

    private float _currentBreak;
    private float _breakTimer;
    private bool _isInBreak;
    private int _breakBoolHash;
    private int _breakStateHash;
    private bool _breakAnimationHeld;
    private float _cachedAnimatorSpeed = 1f;
    private Renderer[] _hologramRenderers;
    private Material[][] _hologramOriginalMaterials;
    private Material _runtimeHologramMaterial;
    private PlayerUltimateController _ultimateController;
    private bool _hasUltimatePresentationSnapshot;
    private float _ultimatePresentationBreakSnapshot;
    private float _ultimatePresentationBreakTimerSnapshot;
    private bool _ultimatePresentationWasInBreak;
    private float _lastBreakDamageTime = float.NegativeInfinity;
    private float _breakEnteredTime = float.NegativeInfinity;
    private bool _finisherUsed;
    private Coroutine _finisherRoutine;
    private bool _finisherDamageApplied;

    static readonly List<BossBreakController> s_Active = new List<BossBreakController>();

    public bool IsFinisherAvailable =>
        enableFinisher &&
        _isInBreak &&
        !_finisherUsed &&
        (bossHealth == null || !bossHealth.IsDead) &&
        (finisherWindowSeconds <= 0f || Time.time - _breakEnteredTime <= finisherWindowSeconds);

    public float NormalizedBreak => Mathf.Clamp01(maxBreak > 0f ? _currentBreak / maxBreak : 0f);
    public float CurrentBreak => _currentBreak;

    public float Get01() => NormalizedBreak;

    public bool IsInBreak => _isInBreak;
    public float RemainingBreakTime => _breakTimer;

    static readonly int HologramColorId = Shader.PropertyToID("_Hologram_Color");
    static readonly int TextureTintColorId = Shader.PropertyToID("_Texture_Tint_Color");

    private void Reset()
    {
        if (!bossHealth) bossHealth = GetComponent<BossHealth>();
        if (!bossAnimator)
        {
            BossReferences bossReferences = GetComponent<BossReferences>();
            if (bossReferences != null && bossReferences.MainAnimator != null)
                bossAnimator = bossReferences.MainAnimator;
            if (!bossAnimator) bossAnimator = GetComponent<Animator>().OrNull() ?? GetComponentInChildren<Animator>();
        }
    }

    private void Awake()
    {
        if (!bossHealth) bossHealth = GetComponent<BossHealth>();
        if (!bossAnimator)
        {
            BossReferences bossReferences = GetComponent<BossReferences>();
            if (bossReferences != null && bossReferences.MainAnimator != null)
                bossAnimator = bossReferences.MainAnimator;
            if (!bossAnimator) bossAnimator = GetComponent<Animator>().OrNull() ?? GetComponentInChildren<Animator>();
        }

        if (bossHealth != null)
            bossHealth.OnDied += HandleBossDied;

        _ultimateController = GameplaySceneCache.ResolvePlayerUltimateController();
        if (_ultimateController != null)
        {
            _ultimateController.OnUltimateStarted += HandleUltimateStarted;
            _ultimateController.OnUltimateEnded += HandleUltimateEnded;
        }

        _breakBoolHash = Animator.StringToHash(breakBoolName);
        _breakStateHash = Animator.StringToHash(breakStateName);
        ResetGauge();
        enabled = false;

        if (!s_Active.Contains(this))
            s_Active.Add(this);
    }

    private void OnDisable()
    {
        // enabled 는 브레이크 흐름에서 자주 토글되므로, 오브젝트 비활성화(코루틴 중단)일 때만 연출을 정리한다.
        if (!gameObject.activeInHierarchy)
            finisherFeedback?.Stop();
    }

    private void OnDestroy()
    {
        s_Active.Remove(this);
        finisherFeedback?.Stop();

        ReleaseBreakAnimationHold();
        RestoreBreakHologram();

        if (bossHealth != null)
            bossHealth.OnDied -= HandleBossDied;

        if (_ultimateController != null)
        {
            _ultimateController.OnUltimateStarted -= HandleUltimateStarted;
            _ultimateController.OnUltimateEnded -= HandleUltimateEnded;
        }
    }

    private void Update()
    {
        if (!_isInBreak)
        {
            UpdatePassiveRecovery();
            return;
        }

        UpdateBreakAnimationHold();

        _breakTimer -= Time.deltaTime;
        if (_breakTimer <= 0f)
            ForceExitBreak();
    }

    public void NotifyBossDamaged(int damage)
    {
        if (damage <= 0) return;
        if (_isInBreak) return;

        float remove = basicAttackBreakDamage;
        _currentBreak = Mathf.Max(0f, _currentBreak - remove);
        MarkBreakDamaged();
        RaiseBreakChanged();

        if (logDebug)
            Debug.Log($"[Break] Damage={damage}, -{remove} -> {_currentBreak}/{maxBreak} ({NormalizedBreak:0.00})", this);

        if (_currentBreak <= 0f)
            ForceEnterBreak();
    }

    public void AddBreak(float amount, BreakSource source)
    {
        if (_isInBreak) return;

        float remove = amount > 0f
            ? amount
            : source == BreakSource.Parry
                ? baseBreakPerHit
                : basicAttackBreakDamage;
        if (remove <= 0f) return;

        _currentBreak = Mathf.Max(0f, _currentBreak - remove);
        MarkBreakDamaged();
        RaiseBreakChanged();

        if (logDebug)
            Debug.Log($"[Break] Source={source}, -{remove} -> {_currentBreak}/{maxBreak} ({NormalizedBreak:0.00})", this);

        if (_currentBreak <= 0f)
            ForceEnterBreak();
    }

    public void ForceEnterBreak()
    {
        if (_isInBreak) return;

        _currentBreak = 0f;
        RaiseBreakChanged();
        _isInBreak = true;
        _breakTimer = breakDuration;
        _breakEnteredTime = Time.time;
        ActionColourDirector.NotifyBreakStart(); // 반복 11: 채도 딥 + 로우패스 0.3 s
        _finisherUsed = false;
        enabled = true;
        ReleaseBreakAnimationHold();
        ApplyBreakHologram();

        if (bossHealth != null)
            bossHealth.SetStaggered(true);

        if (bossAnimator != null && _breakBoolHash != 0)
            bossAnimator.SetBool(_breakBoolHash, true);

        OnBreakEnter?.Invoke();
        PlayBreakSfx(breakEnterClip, breakEnterVolume);

        if (logDebug)
            Debug.Log("[Break] ENTER", this);
    }

    public void ForceExitBreak()
    {
        if (!_isInBreak) return;

        _isInBreak = false;
        _breakTimer = 0f;
        ReleaseBreakAnimationHold();
        RestoreBreakHologram();

        if (bossHealth != null)
            bossHealth.SetStaggered(false);

        if (bossAnimator != null && _breakBoolHash != 0)
            bossAnimator.SetBool(_breakBoolHash, false);

        _currentBreak = maxBreak;
        RaiseBreakChanged();
        OnBreakExit?.Invoke();
        enabled = false;

        if (bossHealth == null || !bossHealth.IsDead)
            PlayBreakSfx(breakExitClip, breakExitVolume);

        if (logDebug)
            Debug.Log("[Break] EXIT", this);
    }

    void PlayBreakSfx(AudioClip clip, float volume)
    {
        if (clip == null)
            return;

        if (breakAudioSource != null)
            breakAudioSource.PlayOneShot(clip, AudioOptionsRuntime.ScaleSfx(volume));
        else
            OneShotAudio.PlayAt(clip, transform.position + Vector3.up * 1.2f, volume, 1f, 0.35f);
    }

    public void RestoreStateAfterPresentation(float preservedBreak, bool preservedInBreak, float preservedBreakTimer)
    {
        if (bossHealth != null && bossHealth.IsDead)
            return;

        if (preservedInBreak)
        {
            bool wasInBreak = _isInBreak;
            _currentBreak = 0f;
            _isInBreak = true;
            _breakTimer = Mathf.Max(_breakTimer, Mathf.Max(0f, preservedBreakTimer));
            enabled = true;
            ReleaseBreakAnimationHold();
            ApplyBreakHologram();

            if (bossHealth != null)
                bossHealth.SetStaggered(true);

            if (bossAnimator != null && _breakBoolHash != 0)
                bossAnimator.SetBool(_breakBoolHash, true);

            RaiseBreakChanged();

            if (!wasInBreak)
                OnBreakEnter?.Invoke();

            return;
        }

        if (_isInBreak)
            return;

        float clampedBreak = Mathf.Clamp(preservedBreak, 0f, maxBreak);
        if (_currentBreak <= clampedBreak + 0.001f)
            return;

        _currentBreak = clampedBreak;
        enabled = true; // 패시브 회복 재개
        RaiseBreakChanged();

        if (logDebug)
            Debug.Log($"[Break] RestoreAfterPresentation -> {_currentBreak}/{maxBreak} ({NormalizedBreak:0.00})", this);
    }

    public void ResetGauge()
    {
        _currentBreak = maxBreak;
        RaiseBreakChanged();

        if (logDebug)
            Debug.Log("[Break] ResetGauge()", this);
    }

    private void HandleBossDied()
    {
        ReleaseBreakAnimationHold();
        RestoreBreakHologram();
        ForceExitBreak();
        ResetGauge();

        if (bossHealth != null)
            bossHealth.OnDied -= HandleBossDied;
    }

    private void HandleUltimateStarted()
    {
        _ultimatePresentationBreakSnapshot = _currentBreak;
        _ultimatePresentationBreakTimerSnapshot = _breakTimer;
        _ultimatePresentationWasInBreak = _isInBreak;
        _hasUltimatePresentationSnapshot = true;

        if (logDebug)
            Debug.Log($"[Break] SnapshotForUltimate -> {_currentBreak}/{maxBreak} ({NormalizedBreak:0.00}) inBreak={_isInBreak}", this);
    }

    private void HandleUltimateEnded()
    {
        if (!_hasUltimatePresentationSnapshot)
            return;

        _hasUltimatePresentationSnapshot = false;
        RestoreStateAfterPresentation(
            _ultimatePresentationBreakSnapshot,
            _ultimatePresentationWasInBreak,
            _ultimatePresentationBreakTimerSnapshot);
    }

    void RaiseBreakChanged()
    {
        OnBreakChanged?.Invoke(NormalizedBreak, _currentBreak);
    }

    void MarkBreakDamaged()
    {
        _lastBreakDamageTime = Time.time;
        enabled = true; // 패시브 회복 체크를 위해 Update 활성화
    }

    // 일정 시간 브레이크 피해가 없으면 게이지가 서서히 회복
    void UpdatePassiveRecovery()
    {
        if (_currentBreak >= maxBreak || recoveryPerSecond <= 0f || (bossHealth != null && bossHealth.IsDead))
        {
            enabled = false;
            return;
        }

        if (Time.time - _lastBreakDamageTime < recoveryDelay)
            return;

        _currentBreak = Mathf.Min(maxBreak, _currentBreak + recoveryPerSecond * Time.deltaTime);
        RaiseBreakChanged();

        if (_currentBreak >= maxBreak)
            enabled = false;
    }

    /// <summary>
    /// 플레이어 공격 입력 시 호출. 범위 내 브레이크 상태 보스가 있으면 피니셔를 시작한다.
    /// </summary>
    public static bool TryBeginFinisher(Transform player, out BossBreakController target)
    {
        target = null;
        if (player == null)
            return false;

        float bestSqr = float.PositiveInfinity;
        Vector3 playerPos = player.position;
        for (int i = s_Active.Count - 1; i >= 0; i--)
        {
            BossBreakController candidate = s_Active[i];
            if (candidate == null)
            {
                s_Active.RemoveAt(i);
                continue;
            }

            if (!candidate.gameObject.activeInHierarchy || !candidate.IsFinisherAvailable)
                continue;

            Vector3 delta = candidate.transform.position - playerPos;
            delta.y = 0f;
            float sqr = delta.sqrMagnitude;
            float range = candidate.finisherRange;
            if (sqr > range * range || sqr >= bestSqr)
                continue;

            bestSqr = sqr;
            target = candidate;
        }

        if (target == null)
            return false;

        target.BeginFinisher(player);
        return true;
    }

    /// <summary>
    /// 플레이어 공격이 판정 전에 끊기면(회피/가드 캔슬, 피격) 호출. 아직 데미지 전이면 피니셔를 취소하고 재시도를 허용한다.
    /// </summary>
    public void CancelPendingFinisher()
    {
        if (_finisherRoutine == null || _finisherDamageApplied)
            return;

        StopCoroutine(_finisherRoutine);
        _finisherRoutine = null;
        _finisherUsed = false;
    }

    void BeginFinisher(Transform player)
    {
        _finisherUsed = true;
        _finisherDamageApplied = false;
        // 피니셔 연출 동안 브레이크가 끝나지 않도록 타이머 보장
        _breakTimer = Mathf.Max(_breakTimer, finisherHitDelay + finisherBreakExitDelay + 0.1f);

        if (_finisherRoutine != null)
            StopCoroutine(_finisherRoutine);
        _finisherRoutine = StartCoroutine(CoFinisher(player));

        if (logDebug)
            Debug.Log("[Break] FINISHER start", this);
    }

    System.Collections.IEnumerator CoFinisher(Transform player)
    {
        if (finisherHitDelay > 0f)
            yield return new WaitForSeconds(finisherHitDelay);

        if (!_isInBreak || bossHealth == null || bossHealth.IsDead)
        {
            _finisherRoutine = null;
            yield break;
        }

        _finisherDamageApplied = true;
        ApplyFinisherDamage(player);

        if (finisherBreakExitDelay > 0f)
            yield return new WaitForSeconds(finisherBreakExitDelay);

        _finisherRoutine = null;
        if (_isInBreak && bossHealth != null && !bossHealth.IsDead)
            ForceExitBreak();
    }

    void ApplyFinisherDamage(Transform player)
    {
        int damage = Mathf.Max(1, Mathf.RoundToInt(bossHealth.MaxHP * Mathf.Clamp01(finisherDamagePercentOfMaxHP)));
        Vector3 hitPoint = transform.position + Vector3.up * 1.2f;

        // 히트 직후 자동 무적에 피니셔가 막히지 않도록 해제
        bossHealth.SetInvincible(0f);

        BossDamageReceiver receiver = GetComponentInChildren<BossDamageReceiver>();
        if (receiver != null)
        {
            Vector3 dir = player != null ? transform.position - player.position : -transform.forward;
            dir.y = 0f;
            receiver.ReceiveHit(new HitPayload
            {
                damage = damage,
                hitType = HitType.Heavy,
                hitPoint = hitPoint,
                hitDirection = dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector3.zero,
                attacker = player,
                canParry = false,
                canPerfectDodge = false,
                canGuard = false,
                causesGuardBreak = false,
                unblockable = true
            });
        }
        else
        {
            bossHealth.TakeDamage(damage, HitType.Heavy, hitPoint);
        }

        if (finisherFeedback != null && finisherFeedback.useFeedback)
        {
            // 슬로우모션 + 강화 셰이크 + 패드 진동 + 전용 SFX
            finisherFeedback.PlayImpact(this, hitPoint, finisherCameraShake);
        }
        else
        {
            CameraShake shake = GameplaySceneCache.ResolveMainCameraShake();
            if (shake != null && finisherCameraShake.x > 0f && finisherCameraShake.y > 0f)
                shake.Shake(finisherCameraShake.x, finisherCameraShake.y);
        }

        if (logDebug)
            Debug.Log($"[Break] FINISHER hit damage={damage}", this);
    }

    void UpdateBreakAnimationHold()
    {
        if (_breakAnimationHeld || bossAnimator == null || _breakStateHash == 0)
            return;

        AnimatorStateInfo state = bossAnimator.GetCurrentAnimatorStateInfo(0);
        if (state.shortNameHash != _breakStateHash)
            return;

        if (state.normalizedTime < Mathf.Clamp01(breakHoldNormalizedTime))
            return;

        _cachedAnimatorSpeed = bossAnimator.speed;
        bossAnimator.speed = 0f;
        _breakAnimationHeld = true;
    }

    void ReleaseBreakAnimationHold()
    {
        if (!_breakAnimationHeld)
            return;

        _breakAnimationHeld = false;
        if (bossAnimator != null)
            bossAnimator.speed = _cachedAnimatorSpeed;
    }

    void ApplyBreakHologram()
    {
        if (!useBreakHologram || _runtimeHologramMaterial != null)
            return;

        Material sourceMaterial = ResolveBreakHologramMaterial();
        if (sourceMaterial == null)
            return;

        List<Renderer> validRenderers = CollectBreakHologramRenderers();
        if (validRenderers.Count == 0)
            return;

        var originalMaterials = new List<Material[]>(validRenderers.Count);
        for (int i = 0; i < validRenderers.Count; i++)
            originalMaterials.Add(validRenderers[i].sharedMaterials);

        _runtimeHologramMaterial = new Material(sourceMaterial) { name = name + "_BreakHologram" };
        SetBreakHologramColor();

        _hologramRenderers = validRenderers.ToArray();
        _hologramOriginalMaterials = originalMaterials.ToArray();
        for (int i = 0; i < _hologramRenderers.Length; i++)
        {
            Renderer renderer = _hologramRenderers[i];
            Material[] original = _hologramOriginalMaterials[i];
            if (renderer == null || original == null)
                continue;

            Material[] hologramMaterials = new Material[original.Length];
            for (int slot = 0; slot < hologramMaterials.Length; slot++)
                hologramMaterials[slot] = _runtimeHologramMaterial;
            renderer.sharedMaterials = hologramMaterials;
        }
    }

    List<Renderer> CollectBreakHologramRenderers()
    {
        var validRenderers = new List<Renderer>(32);
        var seen = new HashSet<Renderer>();

        AddBreakHologramRenderers(hologramRoot, validRenderers, seen);
        BossReferences bossReferences = GetComponent<BossReferences>().OrNull() ?? GetComponentInParent<BossReferences>();
        if (bossReferences != null)
            AddBreakHologramRenderers(bossReferences.VisualRoot, validRenderers, seen);
        if (bossAnimator != null)
            AddBreakHologramRenderers(bossAnimator.transform, validRenderers, seen);
        AddBreakHologramRenderers(transform, validRenderers, seen);

        return validRenderers;
    }

    static void AddBreakHologramRenderers(Transform root, List<Renderer> validRenderers, HashSet<Renderer> seen)
    {
        if (root == null)
            return;

        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null || renderer is ParticleSystemRenderer || renderer is LineRenderer || renderer is TrailRenderer || !seen.Add(renderer))
                continue;

            Material[] materials = renderer.sharedMaterials;
            if (materials == null || materials.Length == 0)
                continue;

            validRenderers.Add(renderer);
        }
    }

    Material ResolveBreakHologramMaterial()
    {
        if (breakHologramMaterial != null)
            return breakHologramMaterial;

        if (!string.IsNullOrWhiteSpace(breakHologramResourcePath))
            breakHologramMaterial = Resources.Load<Material>(breakHologramResourcePath);

#if UNITY_EDITOR
        if (breakHologramMaterial == null)
            breakHologramMaterial = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/Holograms/Materials/Examples/Basic/Scanline_Hologram_Empty.mat");
#endif

        return breakHologramMaterial;
    }

    void SetBreakHologramColor()
    {
        if (_runtimeHologramMaterial == null)
            return;

        if (_runtimeHologramMaterial.HasProperty(HologramColorId))
            _runtimeHologramMaterial.SetColor(HologramColorId, TsukasaHologramColor.Resolve(breakHologramColor));

        if (_runtimeHologramMaterial.HasProperty(TextureTintColorId))
        {
            Color tint = TsukasaHologramColor.Resolve(breakHologramColor);
            tint.a *= 0.75f;
            _runtimeHologramMaterial.SetColor(TextureTintColorId, tint);
        }
    }

    void RestoreBreakHologram()
    {
        if (_hologramRenderers != null && _hologramOriginalMaterials != null)
        {
            int count = Mathf.Min(_hologramRenderers.Length, _hologramOriginalMaterials.Length);
            for (int i = 0; i < count; i++)
            {
                Renderer renderer = _hologramRenderers[i];
                Material[] materials = _hologramOriginalMaterials[i];
                if (renderer != null && materials != null)
                    renderer.sharedMaterials = materials;
            }
        }

        _hologramRenderers = null;
        _hologramOriginalMaterials = null;

        if (_runtimeHologramMaterial != null)
        {
            if (Application.isPlaying)
                Destroy(_runtimeHologramMaterial);
            else
                DestroyImmediate(_runtimeHologramMaterial);
        }
        _runtimeHologramMaterial = null;

        // 반복 15: 홀로그램 중에 HP 균열 단계가 바뀌었으면 위 스냅샷 복원으로 균열이 빠진다 → 현재 단계로 다시 덧그림.
        BossDamageCracks cracks = BossDamageCracks.Active;
        if (cracks != null)
            cracks.Reapply();
    }

    private void OnValidate()
    {
        maxBreak = Mathf.Max(1f, maxBreak);
        basicAttackBreakDamage = Mathf.Max(0f, basicAttackBreakDamage);
        baseBreakPerHit = Mathf.Max(0f, baseBreakPerHit);
        breakDuration = Mathf.Max(0f, breakDuration);
        recoveryPerSecond = Mathf.Max(0f, recoveryPerSecond);
        breakHoldNormalizedTime = Mathf.Clamp(breakHoldNormalizedTime, 0.05f, 1f);
    }
}
