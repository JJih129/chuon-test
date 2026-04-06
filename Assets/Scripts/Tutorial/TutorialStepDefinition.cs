using UnityEngine;
using UnityEngine.Events;

[System.Serializable]
public sealed class TutorialStepDefinition
{
    public string stepId;
    public TutorialStepType stepType;
    [TextArea(1, 2)] public string hintLine1;
    [TextArea(1, 2)] public string hintLine2;
    public TutorialGuideLine[] introLines;
    public TutorialGuideLine[] successLines;
    public TutorialGuideLine[] failureAssistLines;
    public bool showComboGuide;
    public string comboGuideTitle;
    [TextArea(3, 8)] public string comboGuideBody;
    public bool allowFailure = true;
    public bool autoAdvance = true;
    [Min(0f)] public float autoAdvanceDelay = 0.35f;
    public TutorialConditionChecker[] requiredCheckers;
    public GameObject[] activateOnStart;
    public GameObject[] deactivateOnStart;
    public GameObject[] activateOnComplete;
    public GameObject[] deactivateOnComplete;
    public UnityEvent onStepStarted;
    public UnityEvent onStepCompleted;
}
