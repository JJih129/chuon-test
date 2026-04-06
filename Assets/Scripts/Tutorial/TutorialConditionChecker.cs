using System;
using System.Collections;
using UnityEngine;

public abstract class TutorialConditionChecker : MonoBehaviour
{
    [Header("Checker")]
    [SerializeField] private string checkerId = "Checker";
    [SerializeField] private TutorialGuideLine[] reminderLines;
    [SerializeField, Min(0f)] private float reminderDelay = 0f;
    [SerializeField] private bool repeatReminders;
    [SerializeField, Min(0.5f)] private float reminderInterval = 6f;

    protected TutorialFlowController flowController;
    protected TutorialStepDefinition activeStep;

    Coroutine _reminderRoutine;

    public event Action<TutorialConditionChecker> Completed;
    public event Action<TutorialConditionChecker, string> ProgressUpdated;

    public string CheckerId => string.IsNullOrWhiteSpace(checkerId) ? GetType().Name : checkerId;
    public bool IsRunning { get; private set; }
    public bool IsCompleted { get; private set; }

    public virtual void Bind(TutorialFlowController flow)
    {
        flowController = flow;
    }

    public void ConfigureRuntimeReminders(TutorialGuideLine[] lines, float delay, bool repeat, float interval)
    {
        reminderLines = lines;
        reminderDelay = Mathf.Max(0f, delay);
        repeatReminders = repeat;
        reminderInterval = Mathf.Max(0.5f, interval);
    }

    public void BeginChecking(TutorialStepDefinition step)
    {
        EndChecking();

        activeStep = step;
        IsRunning = true;
        IsCompleted = false;
        enabled = ShouldEnableTick();

        OnBeginChecking(step);

        if (!IsCompleted && reminderDelay > 0f && reminderLines != null && reminderLines.Length > 0)
            _reminderRoutine = StartCoroutine(CoReminder());
    }

    public void EndChecking()
    {
        if (_reminderRoutine != null)
        {
            StopCoroutine(_reminderRoutine);
            _reminderRoutine = null;
        }

        if (IsRunning)
            OnEndChecking();

        IsRunning = false;
        IsCompleted = false;
        activeStep = null;
        enabled = false;
    }

    protected virtual bool ShouldEnableTick() => false;

    protected virtual void OnBeginChecking(TutorialStepDefinition step) { }

    protected virtual void OnEndChecking() { }

    protected void ReportProgress(string progressText)
    {
        if (!IsRunning)
            return;

        ProgressUpdated?.Invoke(this, progressText);
    }

    protected void Complete(string progressText = null)
    {
        if (!IsRunning || IsCompleted)
            return;

        IsCompleted = true;
        IsRunning = false;
        enabled = false;

        if (_reminderRoutine != null)
        {
            StopCoroutine(_reminderRoutine);
            _reminderRoutine = null;
        }

        if (!string.IsNullOrWhiteSpace(progressText))
            ProgressUpdated?.Invoke(this, progressText);

        OnEndChecking();
        Completed?.Invoke(this);
    }

    IEnumerator CoReminder()
    {
        yield return new WaitForSeconds(reminderDelay);

        while (IsRunning && !IsCompleted)
        {
            flowController?.QueueGuideLines(reminderLines, false, false);

            if (!repeatReminders)
                yield break;

            yield return new WaitForSeconds(reminderInterval);
        }
    }
}
