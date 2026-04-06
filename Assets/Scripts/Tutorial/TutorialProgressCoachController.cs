using System.Collections;
using UnityEngine;

[DisallowMultipleComponent]
public class TutorialProgressCoachController : MonoBehaviour
{
    [SerializeField] private TutorialFlowController flowController;
    [SerializeField] private EGOGuideController egoGuideController;
    [SerializeField] private TutorialPlayerRuntimeBridge playerBridge;
    [SerializeField] private Transform focusTarget;
    [SerializeField] private TutorialZoneTrigger movementGoalZone;
    [SerializeField] private TutorialZoneTrigger exitZone;

    [Header("Timing")]
    [SerializeField, Min(0.1f)] private float pollIntervalRealtime = 0.25f;
    [SerializeField, Min(0.1f)] private float lineDuration = 1.9f;
    [SerializeField, Min(0.1f)] private float failureCooldownRealtime = 2.6f;
    [SerializeField, Min(0.1f)] private float progressCooldownRealtime = 1.2f;

    [Header("Exit Coaching")]
    [SerializeField, Min(0.5f)] private float exitMidDistance = 7f;
    [SerializeField, Min(0.5f)] private float exitNearDistance = 3.2f;
    [SerializeField, Min(0.05f)] private float exitStallDistanceThreshold = 0.3f;
    [SerializeField, Min(0.1f)] private float exitStallCueDelayRealtime = 2.8f;

    Coroutine _stepRoutine;
    bool _subscribed;
    float _lastFailureCueRealtime = float.NegativeInfinity;
    float _lastProgressCueRealtime = float.NegativeInfinity;

    public void ConfigureRuntime(
        TutorialFlowController runtimeFlowController,
        EGOGuideController runtimeGuideController,
        TutorialPlayerRuntimeBridge runtimePlayerBridge,
        Transform runtimeFocusTarget,
        TutorialZoneTrigger runtimeMovementGoalZone,
        TutorialZoneTrigger runtimeExitZone)
    {
        flowController = runtimeFlowController;
        egoGuideController = runtimeGuideController;
        playerBridge = runtimePlayerBridge;
        focusTarget = runtimeFocusTarget;
        movementGoalZone = runtimeMovementGoalZone;
        exitZone = runtimeExitZone;
        RefreshSubscriptions();
    }

    void OnEnable()
    {
        RefreshSubscriptions();
    }

    void OnDisable()
    {
        ReleaseSubscriptions();
    }

    void OnDestroy()
    {
        ReleaseSubscriptions();
    }

    void RefreshSubscriptions()
    {
        ReleaseSubscriptions();

        if (flowController == null)
            return;

        flowController.StepStarted += HandleStepStarted;
        _subscribed = true;
    }

    void ReleaseSubscriptions()
    {
        if (_subscribed && flowController != null)
            flowController.StepStarted -= HandleStepStarted;

        _subscribed = false;
        StopStepRoutine();
    }

    void HandleStepStarted(TutorialStepDefinition step)
    {
        _lastFailureCueRealtime = float.NegativeInfinity;
        _lastProgressCueRealtime = float.NegativeInfinity;

        StopStepRoutine();
        if (step == null)
            return;

        switch (step.stepType)
        {
            case TutorialStepType.Movement:
                _stepRoutine = StartCoroutine(CoCoachMovement(step));
                break;

            case TutorialStepType.CameraFocus:
                _stepRoutine = StartCoroutine(CoCoachCameraFocus(step));
                break;

            case TutorialStepType.LockOn:
                _stepRoutine = StartCoroutine(CoCoachLockOn(step));
                break;

            case TutorialStepType.Heal:
                _stepRoutine = StartCoroutine(CoCoachHeal(step));
                break;

            case TutorialStepType.Ultimate:
                _stepRoutine = StartCoroutine(CoCoachUltimate(step));
                break;

            case TutorialStepType.Exit:
                _stepRoutine = StartCoroutine(CoCoachExit(step));
                break;
        }
    }

    IEnumerator CoCoachMovement(TutorialStepDefinition step)
    {
        Transform player = playerBridge != null ? playerBridge.PlayerTransform : null;
        if (player == null)
            yield break;

        Vector3 startPosition = player.position;
        bool progressQueued = false;

        yield return new WaitForSecondsRealtime(1.2f);
        while (IsCurrentStep(step))
        {
            float movedDistance = FlatDistance(startPosition, player.position);
            if (!progressQueued && movedDistance >= 1.2f)
            {
                progressQueued = true;
                QueueProgressCue("\uc88b\uc544. \uadf8\ub300\ub85c \uccb4\ud06c \uc9c0\uc810\uae4c\uc9c0 \uac00.");
            }
            else if (movedDistance < 0.8f)
            {
                QueueFailureCue("\uccb4\ud06c \uc9c0\uc810\uae4c\uc9c0 \uc774\ub3d9\ud574. \ubab8\uc744 \uba3c\uc800 \ud480\uc5b4.");
            }

            yield return new WaitForSecondsRealtime(pollIntervalRealtime * 4f);
        }
    }

    IEnumerator CoCoachCameraFocus(TutorialStepDefinition step)
    {
        yield return new WaitForSecondsRealtime(1.1f);

        bool progressQueued = false;
        while (IsCurrentStep(step))
        {
            bool visible = IsFocusTargetVisible();
            if (visible && !progressQueued)
            {
                progressQueued = true;
                QueueProgressCue("\uc88b\uc544. \uc2dc\uc57c\uac00 \uc7a1\ud614\uc5b4.");
            }
            else if (!visible)
            {
                QueueFailureCue("\ud45c\uc801\uc740 \uc804\ubc29 \uce21\uba74\uc5d0 \uc788\ub2e4. \uc2dc\uc57c\ub97c \ub3cc\ub824.");
            }

            yield return new WaitForSecondsRealtime(pollIntervalRealtime * 3f);
        }
    }

    IEnumerator CoCoachLockOn(TutorialStepDefinition step)
    {
        yield return new WaitForSecondsRealtime(1.1f);

        bool progressQueued = false;
        while (IsCurrentStep(step))
        {
            bool visible = IsFocusTargetVisible();
            bool locked = playerBridge != null && playerBridge.IsLockedOnTarget(focusTarget);

            if (locked && !progressQueued)
            {
                progressQueued = true;
                QueueProgressCue("\uc88b\ub2e4. \uadf8 \uc0c1\ud0dc\ub85c \ub193\uce58\uc9c0 \ub9c8.");
            }
            else if (!locked)
            {
                QueueFailureCue(visible
                    ? "\uc2dc\uc120\uc744 \ubd99\uc7a1\uc558\ub2e4\uba74 \ub77d\uc628\ud574."
                    : "\uba3c\uc800 \ud45c\uc801\uc744 \uc2dc\uc57c\uc5d0 \ub2f4\uc544.");
            }

            yield return new WaitForSecondsRealtime(pollIntervalRealtime * 3f);
        }
    }

    IEnumerator CoCoachHeal(TutorialStepDefinition step)
    {
        yield return new WaitForSecondsRealtime(1.4f);
        while (IsCurrentStep(step))
        {
            QueueFailureCue("\ube48\ud2c8\uc774\ub2e4. \uc9c0\uae08 \uc570\ud50c\ub85c \ubcf5\uad6c\ud574.");
            yield return new WaitForSecondsRealtime(2.8f);
        }
    }

    IEnumerator CoCoachUltimate(TutorialStepDefinition step)
    {
        yield return new WaitForSecondsRealtime(1.4f);
        while (IsCurrentStep(step))
        {
            QueueFailureCue("\uac8c\uc774\uc9c0\ub294 \ucda9\ubd84\ud558\ub2e4. \uc9c0\uae08 \ud310\uc744 \ub4a4\uc9d1\uc5b4.");
            yield return new WaitForSecondsRealtime(2.8f);
        }
    }

    IEnumerator CoCoachExit(TutorialStepDefinition step)
    {
        Transform player = playerBridge != null ? playerBridge.PlayerTransform : null;
        Collider exitCollider = exitZone != null ? exitZone.GetComponent<Collider>() : null;
        if (player == null || exitCollider == null)
        {
            yield return new WaitForSecondsRealtime(1.5f);
            while (IsCurrentStep(step))
            {
                QueueFailureCue("\uac00\uc774\ub4dc \ub77c\uc778\uc744 \ub530\ub77c \ucd9c\uad6c\ub85c \uc774\ub3d9\ud574. \ub2e4\uc74c \uad6c\uac04\uc774 \uae30\ub2e4\ub9b0\ub2e4.");
                yield return new WaitForSecondsRealtime(3.4f);
            }

            yield break;
        }

        bool midQueued = false;
        bool nearQueued = false;
        float waitDuration = pollIntervalRealtime * 4f;
        float stalledRealtime = 0f;
        float previousDistance = GetDistanceToZone(player.position, exitCollider);

        yield return new WaitForSecondsRealtime(1.5f);
        while (IsCurrentStep(step))
        {
            float distance = GetDistanceToZone(player.position, exitCollider);

            if (!nearQueued && distance <= exitNearDistance)
            {
                nearQueued = true;
                QueueProgressCue("\uac70\uc758 \ub2e4 \uc654\ub2e4. \ucd9c\uad6c\ub85c \uc9c4\uc785\ud574.");
            }
            else if (!midQueued && distance <= exitMidDistance)
            {
                midQueued = true;
                QueueProgressCue("\uc88b\uc544. \ubcf5\uadc0 \uacbd\ub85c\ub97c \uc7a1\uc558\uc5b4.");
            }

            float delta = Mathf.Abs(previousDistance - distance);
            stalledRealtime = delta <= exitStallDistanceThreshold ? stalledRealtime + waitDuration : 0f;
            previousDistance = distance;

            if (distance <= exitNearDistance)
            {
                QueueFailureCue("\uc55e\uc73c\ub85c \ud55c \uac78\uc74c \ub354. \ucd9c\uad6c\ub85c \ub4e4\uc5b4\uac00.");
            }
            else if (stalledRealtime >= exitStallCueDelayRealtime)
            {
                stalledRealtime = 0f;
                QueueFailureCue("\uac00\uc774\ub4dc \ub77c\uc778\uc744 \ub530\ub77c \uacc4\uc18d \uc804\uc9c4\ud574.");
            }

            yield return new WaitForSecondsRealtime(waitDuration);
        }
    }

    bool IsCurrentStep(TutorialStepDefinition step)
    {
        return flowController != null && ReferenceEquals(flowController.CurrentStep, step);
    }

    bool IsFocusTargetVisible()
    {
        if (playerBridge == null || playerBridge.GameplayCamera == null || focusTarget == null)
            return false;

        Camera targetCamera = playerBridge.GameplayCamera;
        Vector3 viewport = targetCamera.WorldToViewportPoint(focusTarget.position);
        if (viewport.z <= 0f)
            return false;

        const float margin = 0.08f;
        return viewport.x >= margin &&
               viewport.x <= 1f - margin &&
               viewport.y >= margin &&
               viewport.y <= 1f - margin;
    }

    void QueueProgressCue(string text)
    {
        if (egoGuideController == null || string.IsNullOrWhiteSpace(text))
            return;

        float now = Time.realtimeSinceStartup;
        if (now - _lastProgressCueRealtime < progressCooldownRealtime)
            return;

        _lastProgressCueRealtime = now;
        egoGuideController.QueueGuideLines(
            new[]
            {
                BuildLine(EGOGuideMessageType.Tactical, text, false)
            },
            false,
            false);
    }

    void QueueFailureCue(string text)
    {
        if (egoGuideController == null || string.IsNullOrWhiteSpace(text))
            return;

        float now = Time.realtimeSinceStartup;
        if (now - _lastFailureCueRealtime < failureCooldownRealtime)
            return;

        _lastFailureCueRealtime = now;
        egoGuideController.QueueGuideLines(
            new[]
            {
                BuildLine(EGOGuideMessageType.FailureAssist, text, true)
            },
            false,
            true);
    }

    TutorialGuideLine BuildLine(EGOGuideMessageType type, string text, bool urgent)
    {
        return new TutorialGuideLine
        {
            messageType = type,
            speaker = "EGO",
            text = text,
            duration = lineDuration,
            urgent = urgent
        };
    }

    void StopStepRoutine()
    {
        if (_stepRoutine == null)
            return;

        StopCoroutine(_stepRoutine);
        _stepRoutine = null;
    }

    static float FlatDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b);
    }

    static float GetDistanceToZone(Vector3 playerPosition, Collider zoneCollider)
    {
        if (zoneCollider == null)
            return float.PositiveInfinity;

        Vector3 closestPoint = zoneCollider.bounds.ClosestPoint(playerPosition);
        return FlatDistance(playerPosition, closestPoint);
    }
}
