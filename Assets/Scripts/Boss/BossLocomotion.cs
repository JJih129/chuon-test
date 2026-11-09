// 파일명: BossLocomotion.cs
// 보스 이동 애니메이션(BlendTree/런 시작·정지/턴/스트레이프) + 추적 우회 + 충돌 인지 이동.
// MonoBehaviour가 아닌 일반 클래스. 직렬화 설정/참조는 BossController에 그대로 두고 IBossLocomotionHost로 읽는다.

using UnityEngine;

public interface IBossLocomotionHost
{
    Rigidbody Body { get; }
    CapsuleCollider BodyCollider { get; }
    Animator BossAnimator { get; }
    Transform PlayerTarget { get; }
    bool HasMoveXParam { get; }
    bool HasMoveYParam { get; }
    bool HasCombatStrafingParam { get; }
    bool IsDead { get; }
    bool IsUltimateVictim { get; }
    bool IsChaseMovementActive { get; }
    LayerMask MovementCollisionMask { get; }
    float MovementSkin { get; }
    bool UseCollisionAwareMovement { get; }
    float ObstacleProbeDistance { get; }
    float ObstacleProbeAngle { get; }
    float ObstacleSideProbeAngle { get; }
    float DetourCommitTime { get; }
    float ChaseDirectionRefreshInterval { get; }
    float MoveAnimDamp { get; }
    bool UseRunStartMotion { get; }
    bool SuppressRunStartDuringChase { get; }
    string RunStartTriggerName { get; }
    float RunStartMinMoveBlend { get; }
    bool UseTurnStartMotion { get; }
    float TurnStartMinAngle { get; }
    float TurnStart180Angle { get; }
    float TurnStartCooldown { get; }
    bool UseRunStopMotion { get; }
    string RunStopTriggerName { get; }
    float RunStopMinMoveBlend { get; }
    float RunStopDuration { get; }
    float RunStopCooldown { get; }
}

public sealed class BossLocomotion
{
    internal static readonly int AnimParam_MoveSpeed = Animator.StringToHash("MoveSpeed");
    internal static readonly int AnimParam_MoveX = Animator.StringToHash("MoveX");
    internal static readonly int AnimParam_MoveY = Animator.StringToHash("MoveY");
    internal static readonly int AnimParam_IsCombatStrafing = Animator.StringToHash("IsCombatStrafing");
    internal static readonly int AnimParam_RunStart = Animator.StringToHash("RunStart");
    internal static readonly int AnimParam_RunStop = Animator.StringToHash("RunStop");
    internal static readonly int AnimParam_TurnL90 = Animator.StringToHash("TurnL90");
    internal static readonly int AnimParam_TurnR90 = Animator.StringToHash("TurnR90");
    internal static readonly int AnimParam_TurnL180 = Animator.StringToHash("TurnL180");
    internal static readonly int AnimParam_TurnR180 = Animator.StringToHash("TurnR180");

    readonly IBossLocomotionHost _host;
    readonly Transform _transform;
    readonly RaycastHit[] _movementSweepHits = new RaycastHit[16];

    float _moveBlend; // 0~1
    float _moveXBlend;
    float _moveYBlend;
    float _lastAppliedMoveBlend = float.NaN;
    float _lastAppliedMoveX = float.NaN;
    float _lastAppliedMoveY = float.NaN;
    Vector3 _cachedDetourDirection;
    float _cachedDetourUntil;
    Vector3 _cachedChaseDesiredDirection;
    Vector3 _cachedChaseDirection;
    float _nextChaseDirectionRefreshAt;
    bool _combatStrafeAnimActive;
    bool _runStopActive;
    bool _runStopCachedRootMotion;
    bool _runStopHasCachedRootMotion;
    float _runStopEndTime;
    float _nextRunStopAllowedTime;
    float _nextTurnStartAllowedTime;
    bool _wasMovingForRunStart;
    bool _wasMovingForRunStop;
    float _lastRunStopMoveBlend;

    public BossLocomotion(IBossLocomotionHost host, Transform ownerTransform)
    {
        _host = host;
        _transform = ownerTransform;
    }

    public bool IsRunStopActive => _runStopActive;

    public void UpdateMoveAnimation(float target01)
    {
        UpdateMoveAnimation(target01, Vector3.zero, false);
    }

    public void UpdateMoveAnimation(float target01, Vector3 worldMoveDirection)
    {
        UpdateMoveAnimation(target01, worldMoveDirection, true);
    }

    public void UpdateMoveAnimation(float target01, Vector3 worldMoveDirection, bool useDirectionalBlend)
    {
        if (_host.BossAnimator == null) return;

        target01 = Mathf.Clamp01(target01);
        bool useCombatStrafe = useDirectionalBlend && target01 > 0.0001f && worldMoveDirection.sqrMagnitude > 0.0001f;
        if (useCombatStrafe)
            _wasMovingForRunStart = target01 >= _host.RunStartMinMoveBlend;
        else
            UpdateRunStartMotion(target01, worldMoveDirection);
        SetCombatStrafeAnimation(useCombatStrafe);

        _moveBlend = Mathf.Lerp(
            _moveBlend,
            target01,
            Time.deltaTime / Mathf.Max(0.0001f, _host.MoveAnimDamp)
        );

        Vector2 targetDirectionalBlend = Vector2.zero;
        if (target01 > 0.0001f)
        {
            if (useCombatStrafe)
            {
                Vector3 localMoveDirection = _transform.InverseTransformDirection(worldMoveDirection.normalized);
                Vector2 planarDirection = new Vector2(localMoveDirection.x, localMoveDirection.z);
                if (planarDirection.sqrMagnitude > 0.0001f)
                    targetDirectionalBlend = planarDirection.normalized * target01;
            }
            else
            {
                // 일반 추적 이동은 플레이어 일반 이동처럼 정면 이동 위주로 보이게 유지한다.
                targetDirectionalBlend = new Vector2(0f, target01);
            }
        }

        _moveXBlend = Mathf.Lerp(
            _moveXBlend,
            targetDirectionalBlend.x,
            Time.deltaTime / Mathf.Max(0.0001f, _host.MoveAnimDamp)
        );

        _moveYBlend = Mathf.Lerp(
            _moveYBlend,
            targetDirectionalBlend.y,
            Time.deltaTime / Mathf.Max(0.0001f, _host.MoveAnimDamp)
        );

        if (float.IsNaN(_lastAppliedMoveBlend) || Mathf.Abs(_lastAppliedMoveBlend - _moveBlend) > 0.0025f)
        {
            _lastAppliedMoveBlend = _moveBlend;
            _host.BossAnimator.SetFloat(AnimParam_MoveSpeed, _moveBlend);
        }

        if (_host.HasMoveXParam && (float.IsNaN(_lastAppliedMoveX) || Mathf.Abs(_lastAppliedMoveX - _moveXBlend) > 0.0025f))
        {
            _lastAppliedMoveX = _moveXBlend;
            _host.BossAnimator.SetFloat(AnimParam_MoveX, _moveXBlend);
        }

        if (_host.HasMoveYParam && (float.IsNaN(_lastAppliedMoveY) || Mathf.Abs(_lastAppliedMoveY - _moveYBlend) > 0.0025f))
        {
            _lastAppliedMoveY = _moveYBlend;
            _host.BossAnimator.SetFloat(AnimParam_MoveY, _moveYBlend);
        }
    }

    public void UpdateRunStopMotion(float target01)
    {
        if (!_host.UseRunStopMotion || _host.BossAnimator == null || _host.IsDead || _host.IsUltimateVictim)
        {
            StopRunStopMotion();
            _wasMovingForRunStop = target01 >= _host.RunStopMinMoveBlend;
            _lastRunStopMoveBlend = target01;
            return;
        }

        if (_runStopActive)
        {
            if (Time.time >= _runStopEndTime || target01 >= _host.RunStopMinMoveBlend)
                StopRunStopMotion();
        }

        bool moving = target01 >= _host.RunStopMinMoveBlend;
        if (_wasMovingForRunStop && !moving && _lastRunStopMoveBlend >= _host.RunStopMinMoveBlend && Time.time >= _nextRunStopAllowedTime)
            PlayRunStopMotion();

        _wasMovingForRunStop = moving;
        _lastRunStopMoveBlend = target01;
    }

    void UpdateRunStartMotion(float target01, Vector3 worldMoveDirection)
    {
        bool moving = target01 >= _host.RunStartMinMoveBlend;
        if (_host.SuppressRunStartDuringChase && (_host.IsChaseMovementActive))
        {
            _wasMovingForRunStart = moving;
            return;
        }

        if (_host.UseRunStartMotion && _host.BossAnimator != null && !_host.IsDead && !_host.IsUltimateVictim && !_wasMovingForRunStart && moving)
            PlayRunStartMotion(worldMoveDirection);

        _wasMovingForRunStart = moving;
    }

    void PlayRunStartMotion(Vector3 worldMoveDirection)
    {
        if (_host.BossAnimator == null || string.IsNullOrEmpty(_host.RunStartTriggerName))
            return;

        if (TryPlayTurnStartMotion(worldMoveDirection))
            return;

        _host.BossAnimator.ResetTrigger(AnimParam_RunStart);
        _host.BossAnimator.SetTrigger(AnimParam_RunStart);
    }

    bool TryPlayTurnStartMotion(Vector3 worldMoveDirection)
    {
        if (!_host.UseTurnStartMotion || _host.BossAnimator == null || worldMoveDirection.sqrMagnitude <= 0.0004f || Time.time < _nextTurnStartAllowedTime)
            return false;

        float signedAngle = Vector3.SignedAngle(Flat(_transform.forward), Flat(worldMoveDirection), Vector3.up);
        float absAngle = Mathf.Abs(signedAngle);
        if (absAngle < _host.TurnStartMinAngle)
            return false;

        int trigger = absAngle >= _host.TurnStart180Angle
            ? (signedAngle < 0f ? AnimParam_TurnL180 : AnimParam_TurnR180)
            : (signedAngle < 0f ? AnimParam_TurnL90 : AnimParam_TurnR90);

        _host.BossAnimator.ResetTrigger(trigger);
        _host.BossAnimator.SetTrigger(trigger);
        _nextTurnStartAllowedTime = Time.time + _host.TurnStartCooldown;
        return true;
    }

    static Vector3 Flat(Vector3 v)
    {
        v.y = 0f;
        return v.sqrMagnitude > 0.0001f ? v.normalized : Vector3.forward;
    }

    void PlayRunStopMotion()
    {
        if (_host.BossAnimator == null || string.IsNullOrEmpty(_host.RunStopTriggerName))
            return;

        StopRunStopMotion();

        _runStopCachedRootMotion = _host.BossAnimator.applyRootMotion;
        _runStopHasCachedRootMotion = true;
        _host.BossAnimator.applyRootMotion = true;
        _host.BossAnimator.ResetTrigger(AnimParam_RunStop);
        _host.BossAnimator.SetTrigger(AnimParam_RunStop);

        _runStopActive = true;
        _runStopEndTime = Time.time + _host.RunStopDuration;
        _nextRunStopAllowedTime = Time.time + _host.RunStopCooldown;
    }

    public void StopRunStopMotion()
    {
        _runStopActive = false;
        if (_runStopHasCachedRootMotion && _host.BossAnimator != null)
            _host.BossAnimator.applyRootMotion = _runStopCachedRootMotion;
        _runStopHasCachedRootMotion = false;
    }

    public void SetCombatStrafeAnimation(bool active)
    {
        if (!_host.HasCombatStrafingParam || _host.BossAnimator == null || _combatStrafeAnimActive == active)
            return;

        _combatStrafeAnimActive = active;
        _host.BossAnimator.SetBool(AnimParam_IsCombatStrafing, active);
    }

    public Vector3 GetChaseDirection(Vector3 toPlayer)
    {
        Vector3 desired = toPlayer;
        desired.y = 0f;

        if (desired.sqrMagnitude <= 0.0001f)
            return Vector3.zero;

        desired.Normalize();

        float refreshInterval = Mathf.Max(0.02f, _host.ChaseDirectionRefreshInterval);
        if (_cachedChaseDesiredDirection.sqrMagnitude > 0.0001f &&
            Time.time < _nextChaseDirectionRefreshAt &&
            Vector3.Dot(_cachedChaseDesiredDirection, desired) > 0.985f)
        {
            return _cachedChaseDirection;
        }

        Vector3 resolvedDirection;
        if (_cachedDetourUntil > Time.time)
        {
            bool detourStillOpen = HasMovementClearance(_transform.position, _cachedDetourDirection, _host.ObstacleProbeDistance * 0.85f);
            bool directPathStillBlocked = !HasMovementClearance(_transform.position, desired, _host.ObstacleProbeDistance * 0.75f);
            if (detourStillOpen && directPathStillBlocked)
            {
                resolvedDirection = _cachedDetourDirection;
                CacheResolvedChaseDirection(desired, resolvedDirection, refreshInterval);
                return resolvedDirection;
            }
        }

        _cachedDetourUntil = 0f;

        if (HasMovementClearance(_transform.position, desired, _host.ObstacleProbeDistance))
        {
            resolvedDirection = desired;
            CacheResolvedChaseDirection(desired, resolvedDirection, refreshInterval);
            return resolvedDirection;
        }

        Vector3 detour = FindDetourDirection(desired);
        if (detour.sqrMagnitude > 0.0001f)
        {
            _cachedDetourDirection = detour;
            _cachedDetourUntil = Time.time + _host.DetourCommitTime;
            resolvedDirection = detour;
            CacheResolvedChaseDirection(desired, resolvedDirection, refreshInterval);
            return resolvedDirection;
        }

        resolvedDirection = desired;
        CacheResolvedChaseDirection(desired, resolvedDirection, refreshInterval);
        return resolvedDirection;
    }

    void CacheResolvedChaseDirection(Vector3 desired, Vector3 resolved, float refreshInterval)
    {
        _cachedChaseDesiredDirection = desired;
        _cachedChaseDirection = resolved;
        _nextChaseDirectionRefreshAt = Time.time + refreshInterval;
    }

    Vector3 FindDetourDirection(Vector3 desired)
    {
        Vector3 bestDirection = Vector3.zero;
        float bestScore = float.MinValue;

        EvaluateDetourCandidate(desired, _host.ObstacleProbeAngle, ref bestDirection, ref bestScore);
        EvaluateDetourCandidate(desired, -_host.ObstacleProbeAngle, ref bestDirection, ref bestScore);
        EvaluateDetourCandidate(desired, _host.ObstacleSideProbeAngle, ref bestDirection, ref bestScore);
        EvaluateDetourCandidate(desired, -_host.ObstacleSideProbeAngle, ref bestDirection, ref bestScore);
        EvaluateDetourCandidate(desired, 90f, ref bestDirection, ref bestScore);
        EvaluateDetourCandidate(desired, -90f, ref bestDirection, ref bestScore);

        return bestDirection;
    }

    void EvaluateDetourCandidate(Vector3 desired, float angle, ref Vector3 bestDirection, ref float bestScore)
    {
        Vector3 candidate = Quaternion.AngleAxis(angle, Vector3.up) * desired;
        float clearance = MeasureMovementClearance(_transform.position, candidate, _host.ObstacleProbeDistance);
        if (clearance <= _host.MovementSkin)
            return;

        float alignment = Vector3.Dot(candidate, desired);
        float score = alignment + (clearance / Mathf.Max(0.01f, _host.ObstacleProbeDistance));

        if (score > bestScore)
        {
            bestScore = score;
            bestDirection = candidate.normalized;
        }
    }

    public bool HasMovementClearance(Vector3 startPos, Vector3 direction, float distance)
    {
        return MeasureMovementClearance(startPos, direction, distance) >= distance - _host.MovementSkin;
    }

    public float MeasureMovementClearance(Vector3 startPos, Vector3 direction, float distance)
    {
        if (_host.BodyCollider == null || !_host.BodyCollider.enabled)
            return distance;

        if (distance <= 0.0001f || direction.sqrMagnitude <= 0.0001f)
            return 0f;

        direction.y = 0f;
        direction.Normalize();

        GetCapsuleWorld(startPos, out Vector3 point1, out Vector3 point2, out float radius);
        int hitCount = Physics.CapsuleCastNonAlloc(
            point1,
            point2,
            radius,
            direction,
            _movementSweepHits,
            distance + _host.MovementSkin,
            _host.MovementCollisionMask,
            QueryTriggerInteraction.Ignore);

        float nearestDistance = distance + _host.MovementSkin;
        bool found = false;

        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = _movementSweepHits[i];
            if (hit.collider == null)
                continue;
            if (hit.collider.transform.IsChildOf(_transform))
                continue;
            if (_host.PlayerTarget != null && hit.collider.transform.IsChildOf(_host.PlayerTarget))
                continue;

            found = true;
            if (hit.distance < nearestDistance)
                nearestDistance = hit.distance;
        }

        return found ? Mathf.Max(0f, nearestDistance - _host.MovementSkin) : distance;
    }

    public void ApplyPressureMovementDelta(Vector3 delta)
    {
        if (delta.sqrMagnitude <= 0.000001f)
            return;

        if (!_host.UseCollisionAwareMovement)
        {
            ApplyMovementDelta(delta);
            return;
        }

        Vector3 horizontalDelta = delta;
        horizontalDelta.y = 0f;
        float distance = horizontalDelta.magnitude;
        if (distance <= 0.0001f)
        {
            ApplyMovementDelta(delta);
            return;
        }

        Vector3 startPos = _host.Body != null ? _host.Body.position : _transform.position;
        Vector3 direction = horizontalDelta / distance;
        float probeDistance = Mathf.Min(Mathf.Max(distance + _host.MovementSkin, 0.01f), Mathf.Max(0.01f, _host.ObstacleProbeDistance));
        if (HasMovementClearance(startPos, direction, probeDistance))
        {
            ApplyMovementDelta(delta);
            return;
        }

        Vector3 detourDirection = FindDetourDirection(direction);
        if (detourDirection.sqrMagnitude > 0.0001f)
        {
            Vector3 detourDelta = detourDirection * distance;
            detourDelta.y = delta.y;
            ApplyMovementDelta(detourDelta);
            return;
        }

        ApplyMovementDelta(delta);
    }

    public void ApplyMovementDelta(Vector3 delta)
    {
        if (delta.sqrMagnitude <= 0.000001f)
            return;

        Vector3 startPos = _host.Body != null ? _host.Body.position : _transform.position;
        Vector3 resolved = _host.UseCollisionAwareMovement ? ResolveMovementDelta(startPos, delta) : delta;
        Vector3 targetPos = startPos + resolved;

        if (_host.Body != null)
            _host.Body.MovePosition(targetPos);
        else
            _transform.position = targetPos;
    }

    Vector3 ResolveMovementDelta(Vector3 startPos, Vector3 delta)
    {
        if (_host.BodyCollider == null || !_host.BodyCollider.enabled)
            return delta;

        Vector3 currentPos = startPos;
        Vector3 remaining = delta;
        Vector3 moved = Vector3.zero;

        for (int i = 0; i < 2; i++)
        {
            if (remaining.sqrMagnitude <= 0.000001f)
                break;

            if (!TrySweep(currentPos, remaining, out RaycastHit hit))
            {
                moved += remaining;
                break;
            }

            float distance = remaining.magnitude;
            Vector3 dir = remaining / distance;
            float safeDistance = Mathf.Max(0f, hit.distance - _host.MovementSkin);
            Vector3 safeMove = dir * safeDistance;

            moved += safeMove;
            currentPos += safeMove;

            Vector3 leftover = remaining - dir * safeDistance;
            remaining = Vector3.ProjectOnPlane(leftover, hit.normal);
            remaining.y = 0f;
        }

        return moved;
    }

    bool TrySweep(Vector3 currentPos, Vector3 delta, out RaycastHit nearestHit)
    {
        nearestHit = default;

        float distance = delta.magnitude;
        if (distance <= 0.000001f)
            return false;

        GetCapsuleWorld(currentPos, out Vector3 point1, out Vector3 point2, out float radius);

        int hitCount = Physics.CapsuleCastNonAlloc(
            point1,
            point2,
            radius,
            delta / distance,
            _movementSweepHits,
            distance + _host.MovementSkin,
            _host.MovementCollisionMask,
            QueryTriggerInteraction.Ignore);

        bool found = false;
        float nearestDistance = float.MaxValue;

        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = _movementSweepHits[i];
            if (hit.collider == null)
                continue;
            if (hit.collider.transform.IsChildOf(_transform))
                continue;

            if (!found || hit.distance < nearestDistance)
            {
                found = true;
                nearestDistance = hit.distance;
                nearestHit = hit;
            }
        }

        return found;
    }

    void GetCapsuleWorld(Vector3 currentPos, out Vector3 point1, out Vector3 point2, out float radius)
    {
        Vector3 lossy = _transform.lossyScale;
        Vector3 scaledCenter = Vector3.Scale(_host.BodyCollider.center, lossy);
        Vector3 center = currentPos + _transform.rotation * scaledCenter;

        int direction = _host.BodyCollider.direction;
        Vector3 axis = direction == 0 ? _transform.right : (direction == 2 ? _transform.forward : _transform.up);

        float axisScale = direction == 0 ? Mathf.Abs(lossy.x) : (direction == 2 ? Mathf.Abs(lossy.z) : Mathf.Abs(lossy.y));
        float radiusScaleA = direction == 0 ? Mathf.Abs(lossy.y) : Mathf.Abs(lossy.x);
        float radiusScaleB = direction == 2 ? Mathf.Abs(lossy.y) : Mathf.Abs(lossy.z);
        radius = Mathf.Max(0.01f, _host.BodyCollider.radius * Mathf.Max(radiusScaleA, radiusScaleB) - _host.MovementSkin);

        float height = Mathf.Max(_host.BodyCollider.height * axisScale, radius * 2f);
        float half = Mathf.Max(0f, (height * 0.5f) - radius);

        point1 = center + axis * half;
        point2 = center - axis * half;
    }
}
