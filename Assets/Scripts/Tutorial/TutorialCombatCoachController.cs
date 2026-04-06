using UnityEngine;

[DisallowMultipleComponent]
public class TutorialCombatCoachController : MonoBehaviour
{
    [SerializeField] private TutorialFlowController flowController;
    [SerializeField] private EGOGuideController egoGuideController;
    [SerializeField] private TrainingDummyController attackDummy;
    [SerializeField] private TrainingDummyController defenseDummy;

    [Header("Timing")]
    [SerializeField, Min(0.1f)] private float failureCooldownRealtime = 2.4f;
    [SerializeField, Min(0.1f)] private float progressCooldownRealtime = 1.2f;
    [SerializeField, Min(0.1f)] private float lineDuration = 1.8f;

    bool _subscribedToFlow;
    bool _subscribedToAttackDummy;
    bool _subscribedToDefenseDummy;
    float _lastFailureCueRealtime = float.NegativeInfinity;
    float _lastProgressCueRealtime = float.NegativeInfinity;
    bool _basicLightPrompted;
    bool _basicHeavyPrompted;
    bool _comboPrompted;

    public void ConfigureRuntime(
        TutorialFlowController runtimeFlowController,
        EGOGuideController runtimeGuideController,
        TrainingDummyController runtimeAttackDummy,
        TrainingDummyController runtimeDefenseDummy)
    {
        flowController = runtimeFlowController;
        egoGuideController = runtimeGuideController;
        attackDummy = runtimeAttackDummy;
        defenseDummy = runtimeDefenseDummy;
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

        if (flowController != null)
        {
            flowController.StepStarted += HandleStepStarted;
            _subscribedToFlow = true;
        }

        if (attackDummy != null)
        {
            attackDummy.PlayerHitByPlayer += HandleAttackDummyHit;
            _subscribedToAttackDummy = true;
        }

        if (defenseDummy != null)
        {
            defenseDummy.AttackResolved += HandleDefenseResolved;
            _subscribedToDefenseDummy = true;
        }
    }

    void ReleaseSubscriptions()
    {
        if (_subscribedToFlow && flowController != null)
            flowController.StepStarted -= HandleStepStarted;
        if (_subscribedToAttackDummy && attackDummy != null)
            attackDummy.PlayerHitByPlayer -= HandleAttackDummyHit;
        if (_subscribedToDefenseDummy && defenseDummy != null)
            defenseDummy.AttackResolved -= HandleDefenseResolved;

        _subscribedToFlow = false;
        _subscribedToAttackDummy = false;
        _subscribedToDefenseDummy = false;
    }

    void HandleStepStarted(TutorialStepDefinition step)
    {
        _lastFailureCueRealtime = float.NegativeInfinity;
        _lastProgressCueRealtime = float.NegativeInfinity;
        _basicLightPrompted = false;
        _basicHeavyPrompted = false;
        _comboPrompted = false;
    }

    void HandleAttackDummyHit(TrainingDummyController dummy, TutorialCombatHitInfo hitInfo)
    {
        if (flowController == null || flowController.CurrentStep == null)
            return;

        switch (flowController.CurrentStep.stepType)
        {
            case TutorialStepType.BasicAttack:
                HandleBasicAttackCue(hitInfo);
                break;

            case TutorialStepType.Combo:
                HandleComboCue(hitInfo);
                break;
        }
    }

    void HandleBasicAttackCue(TutorialCombatHitInfo hitInfo)
    {
        if (hitInfo.attackKind == TutorialAttackKind.Light && !_basicLightPrompted)
        {
            _basicLightPrompted = true;
            QueueProgressCue("약공 좋다. 이번엔 강공을 섞어.");
            return;
        }

        if (hitInfo.attackKind == TutorialAttackKind.Heavy && !_basicHeavyPrompted)
        {
            _basicHeavyPrompted = true;
            QueueProgressCue("무게감 좋다. 이번엔 약공으로 이어.");
        }
    }

    void HandleComboCue(TutorialCombatHitInfo hitInfo)
    {
        if (_comboPrompted || hitInfo.comboDepth < 2)
            return;

        _comboPrompted = true;
        QueueProgressCue("좋아. 한 번 더 이어가.");
    }

    void HandleDefenseResolved(TrainingDummyController dummy, TrainingDummyAttackResult result)
    {
        if (flowController == null || flowController.CurrentStep == null)
            return;

        switch (flowController.CurrentStep.stepType)
        {
            case TutorialStepType.Guard:
                HandleGuardCue(result);
                break;

            case TutorialStepType.Parry:
                HandleParryCue(result);
                break;

            case TutorialStepType.Dodge:
                HandleDodgeCue(result);
                break;

            case TutorialStepType.PerfectDodge:
                HandlePerfectDodgeCue(result);
                break;
        }
    }

    void HandleGuardCue(TrainingDummyAttackResult result)
    {
        if (result == TrainingDummyAttackResult.Hit)
            QueueFailureCue("정면으로 받아내. 이번 단계는 회피보다 가드다.");
    }

    void HandleParryCue(TrainingDummyAttackResult result)
    {
        if (result == TrainingDummyAttackResult.Guarded)
            QueueFailureCue("막는 데까진 좋다. 이번엔 끝나는 순간에 맞춰.");
        else if (result == TrainingDummyAttackResult.Hit)
            QueueFailureCue("조금만 더 늦춰. 경고가 닿을 때 끊어.");
    }

    void HandleDodgeCue(TrainingDummyAttackResult result)
    {
        if (result == TrainingDummyAttackResult.Guarded || result == TrainingDummyAttackResult.Hit)
            QueueFailureCue("받지 말고 선 바깥으로 흘려.");
    }

    void HandlePerfectDodgeCue(TrainingDummyAttackResult result)
    {
        if (result == TrainingDummyAttackResult.Dodged)
            QueueFailureCue("회피는 됐어. 이번엔 한 박자만 더 끌어.");
        else if (result == TrainingDummyAttackResult.Hit)
            QueueFailureCue("발사 직전까지 기다렸다가 빠져.");
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
                BuildLine(EGOGuideMessageType.Tactical, text)
            },
            false,
            true);
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
                BuildLine(EGOGuideMessageType.FailureAssist, text)
            },
            false,
            true);
    }

    TutorialGuideLine BuildLine(EGOGuideMessageType type, string text)
    {
        return new TutorialGuideLine
        {
            messageType = type,
            speaker = "EGO",
            text = text,
            duration = lineDuration,
            urgent = true
        };
    }
}
