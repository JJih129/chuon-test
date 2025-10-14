// PlayerFinisherMover.cs
using System.Collections;
using UnityEngine;

[DisallowMultipleComponent]
public class PlayerFinisherMover : MonoBehaviour
{
    // ===== 변수 헤더(한글 설명) =====
    [Header("플레이어 루트")]
    [Tooltip("이 스크립트는 플레이어 루트(이동될 Transform)에 붙이거나 그 Transform을 할당")]
    public Transform playerRoot;

    [Header("보간 튜닝")]
    [Tooltip("이동 보간 시간(초). Timeline 시그널과 맞춰 사용")]
    public float defaultMoveDuration = 0.6f;
    [Tooltip("이동 시 보간곡선 (0-1)")]
    public AnimationCurve moveCurve = AnimationCurve.EaseInOut(0,0,1,1);

    Coroutine _moveRoutine;

    public void StartMoveToAnchor(Transform anchor, float duration = -1f)
    {
        if (playerRoot == null) playerRoot = transform;
        if (anchor == null) return;
        if (duration <= 0f) duration = defaultMoveDuration;

        if (_moveRoutine != null) StopCoroutine(_moveRoutine);
        _moveRoutine = StartCoroutine(MoveRoutine(anchor.position, anchor.rotation, duration));
    }

    IEnumerator MoveRoutine(Vector3 targetPos, Quaternion targetRot, float dur)
    {
        float t = 0f;
        Vector3 startPos = playerRoot.position;
        Quaternion startRot = playerRoot.rotation;

        while (t < dur)
        {
            t += Time.deltaTime;
            float p = Mathf.Clamp01(t / dur);
            float c = moveCurve.Evaluate(p);
            playerRoot.position = Vector3.Lerp(startPos, targetPos, c);
            playerRoot.rotation = Quaternion.Slerp(startRot, targetRot, c);
            yield return null;
        }

        playerRoot.position = targetPos;
        playerRoot.rotation = targetRot;
        _moveRoutine = null;
    }
}
