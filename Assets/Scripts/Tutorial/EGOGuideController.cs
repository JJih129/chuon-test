using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class EGOGuideController : MonoBehaviour
{
    [SerializeField] private TutorialHintUIBridge hintUIBridge;
    [SerializeField] private AudioSource voiceSource;
    [SerializeField, Min(0.2f)] private float defaultLineDuration = 2.4f;
    [SerializeField] private bool suppressConsecutiveDuplicates = true;

    readonly Queue<TutorialGuideLine> _queue = new Queue<TutorialGuideLine>();
    readonly Queue<TutorialGuideLine> _urgentQueue = new Queue<TutorialGuideLine>();
    Coroutine _playRoutine;
    string _lastPlayedKey = string.Empty;

    public void ConfigureRuntime(TutorialHintUIBridge bridge)
    {
        hintUIBridge = bridge;
    }

    public void QueueGuideLines(IReadOnlyList<TutorialGuideLine> lines, bool clearQueue, bool urgent)
    {
        if (lines == null || lines.Count == 0)
            return;

        if (clearQueue)
        {
            _queue.Clear();
            _urgentQueue.Clear();
        }

        for (int i = 0; i < lines.Count; i++)
        {
            TutorialGuideLine line = lines[i];
            if (string.IsNullOrWhiteSpace(line.text))
                continue;

            if (suppressConsecutiveDuplicates && line.DeduplicationKey == _lastPlayedKey)
                continue;

            if (urgent || line.urgent)
                _urgentQueue.Enqueue(line);
            else
                _queue.Enqueue(line);
        }

        if (urgent && _playRoutine != null)
        {
            StopCoroutine(_playRoutine);
            _playRoutine = null;
        }

        if (_playRoutine == null)
            _playRoutine = StartCoroutine(CoPlayQueue());
    }

    IEnumerator CoPlayQueue()
    {
        while (_urgentQueue.Count > 0 || _queue.Count > 0)
        {
            TutorialGuideLine line = _urgentQueue.Count > 0
                ? _urgentQueue.Dequeue()
                : _queue.Dequeue();
            _lastPlayedKey = line.DeduplicationKey;

            if (voiceSource != null && line.clip != null)
                voiceSource.PlayOneShot(line.clip);

            hintUIBridge?.ShowGuideText(line.text, line.speaker);
            yield return new WaitForSeconds(Mathf.Max(0.2f, line.duration > 0f ? line.duration : defaultLineDuration));
        }

        hintUIBridge?.HideGuideText();
        _playRoutine = null;
    }
}
