using UnityEngine;

[DisallowMultipleComponent]
public class TutorialDebugController : MonoBehaviour
{
    [SerializeField] private TutorialFlowController flowController;
    [SerializeField] private KeyCode logCurrentStepKey = KeyCode.F7;
    [SerializeField] private KeyCode completeStepKey = KeyCode.F8;
    [SerializeField] private KeyCode nextStepKey = KeyCode.F9;
    [SerializeField] private KeyCode restartStepKey = KeyCode.F10;
    [SerializeField] private KeyCode firstStepKey = KeyCode.BackQuote;

    void Awake()
    {
        if (flowController == null)
            flowController = GetComponent<TutorialFlowController>();

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        enabled = true;
#else
        enabled = false;
#endif
    }

    void Update()
    {
        if (flowController == null)
            return;

        if (Input.GetKeyDown(logCurrentStepKey))
        {
            TutorialStepDefinition step = flowController.CurrentStep;
            Debug.Log(step == null
                ? "[TutorialDebug] No active step."
                : $"[TutorialDebug] Current Step: {flowController.CurrentStepIndex} | {step.stepId} | {step.stepType}",
                this);
        }

        if (Input.GetKeyDown(completeStepKey))
            flowController.ForceCompleteCurrentStep();

        if (Input.GetKeyDown(nextStepKey))
            flowController.GoToNextStep();

        if (Input.GetKeyDown(restartStepKey))
            flowController.RestartCurrentStep();

        if (Input.GetKeyDown(firstStepKey))
            flowController.SkipToStep(0);
    }
}
