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
    [Tooltip("반복 14A: 완료 후 EGO 대사(오프닝·리빌 등)가 끝날 때까지 다음 단계를 최대 이 시간(초) 기다린다. 0 = 남은 소개 대사를 끊고 바로 진행.")]
    [Min(0f)] public float holdAdvanceForLines = 0f;
    public TutorialConditionChecker[] requiredCheckers;
    public GameObject[] activateOnStart;
    public GameObject[] deactivateOnStart;
    public GameObject[] activateOnComplete;
    public GameObject[] deactivateOnComplete;
    public UnityEvent onStepStarted;
    public UnityEvent onStepCompleted;
}
