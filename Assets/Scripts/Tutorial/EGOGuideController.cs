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
    float _lineShownAt;
    float _lineEndAt;

    /// <summary>대사를 재생 중이거나 대기열이 남아 있으면 true.</summary>
    public bool IsSpeaking => _playRoutine != null;

    /// <summary>
    /// 반복 14A (튜토리얼 페이싱): 지금 보이는 대사를 최소 minShown 초 보여 준 뒤 끊는다 (빠른 플레이어에게 대사가 한 단계씩 뒤처지지 않게).
    /// 재생 중인 대사가 없으면 아무것도 안 한다.
    /// </summary>
    public void Preempt(float minShown)
    {
        if (_playRoutine == null)
            return;
        _lineEndAt = Mathf.Min(_lineEndAt, _lineShownAt + Mathf.Max(0f, minShown));
    }

    /// <summary>아직 시작하지 않은 일반 대사를 버린다 (긴급 대사는 유지).</summary>
    public void ClearPending()
    {
        _queue.Clear();
    }


    public void ConfigureRuntime(TutorialHintUIBridge bridge)
    {
        hintUIBridge = bridge;
    }

    public void QueueGuideLines(IReadOnlyList<TutorialGuideLine> lines, bool clearQueue, bool urgent)
    {
        if (!ExhibitionPrototypePresentationPolicy.DialogueEnabled)
        {
            _queue.Clear();
            _urgentQueue.Clear();
            if (_playRoutine != null)
            {
                StopCoroutine(_playRoutine);
                _playRoutine = null;
            }
            hintUIBridge?.HideGuideText();
            return;
        }

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
                voiceSource.PlayOneShot(line.clip, AudioOptionsRuntime.ScaleVoice(1f)); // 보이스 볼륨 옵션

            hintUIBridge?.ShowGuideText(line.text, line.speaker);
            _lineShownAt = Time.time;
            _lineEndAt = _lineShownAt + Mathf.Max(0.2f, line.duration > 0f ? line.duration : defaultLineDuration);
            while (Time.time < _lineEndAt) // Preempt 가 _lineEndAt 을 당길 수 있다
                yield return null;
        }

        hintUIBridge?.HideGuideText();
        _playRoutine = null;
    }
}
