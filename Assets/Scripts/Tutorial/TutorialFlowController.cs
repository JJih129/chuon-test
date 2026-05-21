using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class TutorialFlowController : MonoBehaviour
{
    [SerializeField] private TutorialPlayerRuntimeBridge playerBridge;
    [SerializeField] private EGOGuideController egoGuideController;
    [SerializeField] private TutorialHintUIBridge hintUIBridge;
    [SerializeField] private TutorialStepDefinition[] steps;
    [SerializeField] private TrainingDummyController[] managedDummies;
    [SerializeField] private bool debugLogs;

    readonly Dictionary<TutorialConditionChecker, bool> _checkerStates = new Dictionary<TutorialConditionChecker, bool>();
    readonly List<TutorialConditionChecker> _activeCheckers = new List<TutorialConditionChecker>();

    Coroutine _autoAdvanceRoutine;
    bool _flowStarted;
    bool _isCompletingStep;

    public event Action<TutorialStepDefinition> StepStarted;
    public event Action<TutorialStepDefinition> StepCompleted;
    public event Action<TutorialStepDefinition, string> StepProgressUpdated;

    public int CurrentStepIndex { get; private set; } = -1;
    public int StepCount => steps != null ? steps.Length : 0;
    public string CurrentStepProgressText { get; private set; } = string.Empty;
    public TutorialStepDefinition CurrentStep =>
        steps != null && CurrentStepIndex >= 0 && CurrentStepIndex < steps.Length
            ? steps[CurrentStepIndex]
            : null;

    public void ConfigureRuntime(
        TutorialPlayerRuntimeBridge bridge,
        EGOGuideController guideController,
        TutorialHintUIBridge uiBridge,
        TutorialStepDefinition[] runtimeSteps,
        TrainingDummyController[] runtimeDummies)
    {
        playerBridge = bridge;
        egoGuideController = guideController;
        hintUIBridge = uiBridge;
        steps = runtimeSteps;
        managedDummies = runtimeDummies;
    }

    public void StartFlow()
    {
        if (_flowStarted || steps == null || steps.Length == 0)
            return;

        _flowStarted = true;
        BeginStep(0);
    }

    public void ForceCompleteCurrentStep()
    {
        if (CurrentStep == null)
            return;

        CompleteCurrentStep();
    }

    public void GoToNextStep()
    {
        if (steps == null)
            return;

        BeginStep(Mathf.Clamp(CurrentStepIndex + 1, 0, steps.Length - 1));
    }

    public void RestartCurrentStep()
    {
        if (CurrentStepIndex < 0)
            return;

        BeginStep(CurrentStepIndex);
    }

    public void SkipToStep(int index)
    {
        if (steps == null || steps.Length == 0)
            return;

        BeginStep(Mathf.Clamp(index, 0, steps.Length - 1));
    }

    public void QueueGuideLines(IReadOnlyList<TutorialGuideLine> lines, bool clearQueue, bool urgent)
    {
        egoGuideController?.QueueGuideLines(lines, clearQueue, urgent);
    }

    void BeginStep(int stepIndex)
    {
        if (steps == null || stepIndex < 0 || stepIndex >= steps.Length)
            return;

        if (_autoAdvanceRoutine != null)
        {
            StopCoroutine(_autoAdvanceRoutine);
            _autoAdvanceRoutine = null;
        }

        CleanupActiveCheckers();
        _checkerStates.Clear();
        _activeCheckers.Clear();
        _isCompletingStep = false;

        CurrentStepIndex = stepIndex;
        CurrentStepProgressText = string.Empty;
        TutorialStepDefinition step = steps[stepIndex];

        // ?④퀎 ?꾪솚留덈떎 ?ㅻ툕?앺듃 ?쒖꽦 ?곹깭? ?붾? 紐⑤뱶瑜??④퍡 諛붽퓭??        // ??李몄“瑜?怨좎젙?쇰줈 臾띠? ?딄퀬???쒖감???쒗넗由ъ뼹???좎??쒕떎.
        ApplyActivation(step.deactivateOnStart, false);
        ApplyActivation(step.activateOnStart, true);

        if (managedDummies != null)
        {
            for (int i = 0; i < managedDummies.Length; i++)
            {
                if (managedDummies[i] != null)
                    managedDummies[i].ApplyStep(step.stepType);
            }
        }

        hintUIBridge?.ShowHint(step.hintLine1, step.hintLine2);
        if (step.showComboGuide)
        {
            hintUIBridge?.ShowComboGuide(step.comboGuideTitle, step.comboGuideBody);
            hintUIBridge?.ClearComboGuideStatus();
        }
        else
        {
            hintUIBridge?.HideComboGuide();
        }
        egoGuideController?.QueueGuideLines(step.introLines, true, false);

        if (step.requiredCheckers != null)
        {
            for (int i = 0; i < step.requiredCheckers.Length; i++)
            {
                TutorialConditionChecker checker = step.requiredCheckers[i];
                if (checker == null)
                    continue;

                checker.Bind(this);
                checker.Completed += HandleCheckerCompleted;
                checker.ProgressUpdated += HandleCheckerProgress;
                _checkerStates[checker] = false;
                _activeCheckers.Add(checker);
                checker.BeginChecking(step);
            }
        }

        step.onStepStarted?.Invoke();
        StepStarted?.Invoke(step);
        StepProgressUpdated?.Invoke(step, CurrentStepProgressText);

        if (debugLogs)
            Debug.Log($"[TutorialFlow] Step started: {step.stepId} ({step.stepType})", this);

        if (_activeCheckers.Count == 0)
            CompleteCurrentStep();
    }

    void HandleCheckerCompleted(TutorialConditionChecker checker)
    {
        if (!_checkerStates.ContainsKey(checker))
            return;

        _checkerStates[checker] = true;

        foreach (KeyValuePair<TutorialConditionChecker, bool> entry in _checkerStates)
        {
            if (!entry.Value)
                return;
        }

        CompleteCurrentStep();
    }

    void HandleCheckerProgress(TutorialConditionChecker checker, string progressText)
    {
        if (CurrentStep == null || string.IsNullOrWhiteSpace(progressText))
            return;

        CurrentStepProgressText = progressText;
        StepProgressUpdated?.Invoke(CurrentStep, progressText);
        hintUIBridge?.ShowHint(CurrentStep.hintLine1, BuildQuestBody(CurrentStep, progressText));
        if (CurrentStep.showComboGuide)
            hintUIBridge?.UpdateComboGuideStatus(progressText);
    }

    string BuildQuestBody(TutorialStepDefinition step, string progressText)
    {
        string guide = step != null ? step.hintLine2 : string.Empty;
        if (string.IsNullOrWhiteSpace(progressText) || progressText == "\uc644\ub8cc")
            return guide;

        if (string.IsNullOrWhiteSpace(guide))
            return progressText;

        return $"{guide}\n{progressText}";
    }

    void CompleteCurrentStep()
    {
        if (_isCompletingStep || CurrentStep == null)
            return;

        _isCompletingStep = true;

        TutorialStepDefinition step = CurrentStep;
        CleanupActiveCheckers();

        ApplyActivation(step.deactivateOnComplete, false);
        ApplyActivation(step.activateOnComplete, true);

        hintUIBridge?.ShowStepCompleted(string.IsNullOrWhiteSpace(step.hintLine1) ? step.stepId : step.hintLine1);
        egoGuideController?.QueueGuideLines(step.successLines, false, false);
        CurrentStepProgressText = "\uc644\ub8cc";
        StepProgressUpdated?.Invoke(step, CurrentStepProgressText);
        if (step.showComboGuide)
            hintUIBridge?.UpdateComboGuideStatus(CurrentStepProgressText);
        step.onStepCompleted?.Invoke();
        StepCompleted?.Invoke(step);

        if (debugLogs)
            Debug.Log($"[TutorialFlow] Step completed: {step.stepId}", this);

        if (!step.autoAdvance)
            return;

        int nextStepIndex = CurrentStepIndex + 1;
        if (steps == null || nextStepIndex >= steps.Length)
            return;

        _autoAdvanceRoutine = StartCoroutine(CoAdvanceAfterDelay(step.autoAdvanceDelay));
    }

    IEnumerator CoAdvanceAfterDelay(float delay)
    {
        yield return new WaitForSeconds(Mathf.Max(0f, delay));
        _autoAdvanceRoutine = null;
        BeginStep(CurrentStepIndex + 1);
    }

    void ApplyActivation(GameObject[] targets, bool state)
    {
        if (targets == null)
            return;

        for (int i = 0; i < targets.Length; i++)
        {
            if (targets[i] != null)
                targets[i].SetActive(state);
        }
    }

    void CleanupActiveCheckers()
    {
        for (int i = 0; i < _activeCheckers.Count; i++)
        {
            TutorialConditionChecker checker = _activeCheckers[i];
            if (checker == null)
                continue;

            checker.Completed -= HandleCheckerCompleted;
            checker.ProgressUpdated -= HandleCheckerProgress;
            checker.EndChecking();
        }
    }
}
