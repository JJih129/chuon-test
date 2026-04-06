using System.Collections;
using UnityEngine;

[DisallowMultipleComponent]
public class TutorialExitInteractableBridge : MonoBehaviour
{
    [SerializeField] private TutorialFlowController flowController;
    [SerializeField] private EGOGuideController egoGuideController;
    [SerializeField] private TutorialHintUIBridge hintUIBridge;
    [SerializeField] private TutorialZoneTrigger exitZone;
    [SerializeField] private SceneLoadInteractable sceneLoadInteractable;

    [Header("Prompt")]
    [SerializeField] private string exitStepPromptText = "F: \ud604\uc2e4 \ubcf5\uadc0 \uc900\ube44";
    [SerializeField] private string exitReadyPromptText = "F: \ub85c\ube44 \uc9c4\uc785";

    [Header("Guide")]
    [SerializeField] private string exitReadyHintTitle = "\ud604\uc2e4 \ubcf5\uadc0";
    [SerializeField] private string exitReadyHintBody = "\uc0c1\ud638\uc791\uc6a9\uc73c\ub85c \ub85c\ube44\uc5d0 \uc9c4\uc785\ud574";
    [SerializeField] private string exitBlockedHintTitle = "\ubcf5\uadc0 \ub3d9\uae30\ud654";
    [SerializeField] private string exitBlockedHintBody = "\ucd9c\uad6c \uc5f0\uacb0\uc744 \uba3c\uc800 \uc548\uc815\ud654\ud574";
    [SerializeField] private string exitBlockedGuideText = "\uba3c\uc800 \ucd9c\uad6c \uc5f0\uacb0\uc744 \uc548\uc815\ud654\ud574. \uc544\uc9c1 \ubc14\ub85c \ub118\uc5b4\uac08 \uc218\ub294 \uc5c6\uc5b4.";
    [SerializeField] private string exitReadyGuideText = "\ub9c1\ud06c \uc548\uc815\ud654 \uc644\ub8cc. \uc0c1\ud638\uc791\uc6a9\uc73c\ub85c \ub85c\ube44\uc5d0 \uc9c4\uc785\ud574.";
    [SerializeField, Min(0.1f)] private float exitReadyGuideDuration = 2.2f;
    [SerializeField, Min(0.1f)] private float exitBlockedGuideDuration = 2f;

    [Header("Highlight")]
    [SerializeField, Min(0.05f)] private float highlightPulseOnDuration = 0.28f;
    [SerializeField, Min(0.05f)] private float highlightPulseOffDuration = 0.12f;
    [SerializeField, Min(1)] private int highlightPulseCount = 2;

    string _originalPromptText;
    bool _subscribed;
    bool _exitReady;
    Coroutine _highlightRoutine;
    float _lastBlockedCueRealtime = float.NegativeInfinity;

    public void ConfigureRuntime(
        TutorialFlowController runtimeFlowController,
        EGOGuideController runtimeGuideController,
        TutorialHintUIBridge runtimeHintBridge,
        TutorialZoneTrigger runtimeExitZone,
        SceneLoadInteractable runtimeSceneLoadInteractable)
    {
        flowController = runtimeFlowController;
        egoGuideController = runtimeGuideController;
        hintUIBridge = runtimeHintBridge;
        exitZone = runtimeExitZone;
        sceneLoadInteractable = runtimeSceneLoadInteractable;
        CacheOriginalPrompt();
        RefreshSubscriptions();
    }

    void OnEnable()
    {
        CacheOriginalPrompt();
        RefreshSubscriptions();
    }

    void OnDisable()
    {
        ReleaseSubscriptions();
        RestorePrompt();
        StopHighlightRoutine();
        SetHighlight(false);
    }

    void OnDestroy()
    {
        ReleaseSubscriptions();
        RestorePrompt();
        StopHighlightRoutine();
        SetHighlight(false);
    }

    void CacheOriginalPrompt()
    {
        if (sceneLoadInteractable != null && string.IsNullOrEmpty(_originalPromptText))
            _originalPromptText = sceneLoadInteractable.promptText;
    }

    void RefreshSubscriptions()
    {
        ReleaseSubscriptions();

        if (flowController != null)
        {
            flowController.StepStarted += HandleStepStarted;
            flowController.StepCompleted += HandleStepCompleted;
            _subscribed = true;
        }

        if (exitZone != null)
            exitZone.TriggerEntered += HandleExitZoneEntered;

        if (sceneLoadInteractable != null)
        {
            sceneLoadInteractable.InteractionGuard += HandleInteractionGuard;
            sceneLoadInteractable.BeforeSceneLoad += HandleBeforeSceneLoad;
        }
    }

    void ReleaseSubscriptions()
    {
        if (_subscribed && flowController != null)
        {
            flowController.StepStarted -= HandleStepStarted;
            flowController.StepCompleted -= HandleStepCompleted;
        }

        if (exitZone != null)
            exitZone.TriggerEntered -= HandleExitZoneEntered;

        if (sceneLoadInteractable != null)
        {
            sceneLoadInteractable.InteractionGuard -= HandleInteractionGuard;
            sceneLoadInteractable.BeforeSceneLoad -= HandleBeforeSceneLoad;
        }

        _subscribed = false;
    }

    void HandleStepStarted(TutorialStepDefinition step)
    {
        _exitReady = false;
        StopHighlightRoutine();
        SetHighlight(false);
        _lastBlockedCueRealtime = float.NegativeInfinity;

        if (step != null && step.stepType == TutorialStepType.Exit)
            SetPrompt(exitStepPromptText);
        else
            RestorePrompt();
    }

    void HandleStepCompleted(TutorialStepDefinition step)
    {
        if (step == null || step.stepType != TutorialStepType.Exit)
            return;

        SetExitReadyState();
    }

    void HandleExitZoneEntered(TutorialZoneTrigger zone, Collider other)
    {
        if (zone == null || exitZone == null || zone != exitZone)
            return;

        if (flowController == null || flowController.CurrentStep == null)
            return;

        if (flowController.CurrentStep.stepType != TutorialStepType.Exit)
            return;

        SetExitReadyState();
    }

    void SetExitReadyState()
    {
        if (_exitReady)
            return;

        _exitReady = true;
        SetPrompt(exitReadyPromptText);
        hintUIBridge?.ShowHint(exitReadyHintTitle, exitReadyHintBody);

        if (egoGuideController != null)
        {
            egoGuideController.QueueGuideLines(
                new[]
                {
                    new TutorialGuideLine
                    {
                        messageType = EGOGuideMessageType.Success,
                        speaker = "EGO",
                        text = exitReadyGuideText,
                        duration = exitReadyGuideDuration,
                        urgent = true
                    }
                },
                false,
                true);
        }

        StopHighlightRoutine();
        if (sceneLoadInteractable != null && sceneLoadInteractable.highlight != null)
            _highlightRoutine = StartCoroutine(CoPulseHighlight());
    }

    bool HandleInteractionGuard(object invoker)
    {
        if (_exitReady)
            return true;

        if (flowController == null || flowController.CurrentStep == null || flowController.CurrentStep.stepType != TutorialStepType.Exit)
            return false;

        float now = Time.realtimeSinceStartup;
        if (now - _lastBlockedCueRealtime >= 1.8f)
        {
            _lastBlockedCueRealtime = now;
            hintUIBridge?.ShowHint(exitBlockedHintTitle, exitBlockedHintBody);

            if (egoGuideController != null)
            {
                egoGuideController.QueueGuideLines(
                    new[]
                    {
                        new TutorialGuideLine
                        {
                            messageType = EGOGuideMessageType.FailureAssist,
                            speaker = "EGO",
                            text = exitBlockedGuideText,
                            duration = exitBlockedGuideDuration,
                            urgent = true
                        }
                    },
                    false,
                    true);
            }
        }

        return false;
    }

    void HandleBeforeSceneLoad(SceneLoadInteractable interactable, object invoker)
    {
        if (!ReferenceEquals(interactable, sceneLoadInteractable) || !_exitReady)
            return;

        TutorialSceneTransitionState.MarkTutorialToLobby();
    }

    IEnumerator CoPulseHighlight()
    {
        int pulseCount = Mathf.Max(1, highlightPulseCount);
        for (int i = 0; i < pulseCount; i++)
        {
            SetHighlight(true);
            yield return new WaitForSecondsRealtime(Mathf.Max(0.05f, highlightPulseOnDuration));
            SetHighlight(false);

            if (i < pulseCount - 1)
                yield return new WaitForSecondsRealtime(Mathf.Max(0.05f, highlightPulseOffDuration));
        }

        _highlightRoutine = null;
    }

    void StopHighlightRoutine()
    {
        if (_highlightRoutine == null)
            return;

        StopCoroutine(_highlightRoutine);
        _highlightRoutine = null;
    }

    void SetPrompt(string promptText)
    {
        if (sceneLoadInteractable == null || string.IsNullOrWhiteSpace(promptText))
            return;

        sceneLoadInteractable.promptText = promptText;
    }

    void RestorePrompt()
    {
        if (sceneLoadInteractable == null)
            return;

        sceneLoadInteractable.promptText = string.IsNullOrWhiteSpace(_originalPromptText)
            ? sceneLoadInteractable.promptText
            : _originalPromptText;
    }

    void SetHighlight(bool active)
    {
        if (sceneLoadInteractable == null || sceneLoadInteractable.highlight == null)
            return;

        sceneLoadInteractable.highlight.SetActive(active);
    }
}
