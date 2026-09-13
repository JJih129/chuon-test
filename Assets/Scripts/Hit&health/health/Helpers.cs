using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public static class Helpers
{
    private static readonly Dictionary<float, WaitForSeconds> WaitCache = new Dictionary<float, WaitForSeconds>();

    // 사용 예:
    // StartCoroutine(Helpers.SetBoolFalseAfterSeconds(()=> someBool=false, 1.2f));
    public static IEnumerator SetBoolFalseAfterSeconds(Action setFalseAction, float seconds)
    {
        var wait = GetWait(seconds);
        if (wait != null) yield return wait;
        else yield return null;
        setFalseAction?.Invoke();
    }

    // 편의 래퍼 (MonoBehaviour에서 직접 시작용)
    public static Coroutine StartSetFalseCoroutine(MonoBehaviour host, Action setFalseAction, float seconds)
    {
        if (host == null) return null;
        return host.StartCoroutine(SetBoolFalseAfterSeconds(setFalseAction, seconds));
    }

    private static WaitForSeconds GetWait(float seconds)
    {
        float waitSeconds = Mathf.Max(0f, seconds);
        if (waitSeconds <= 0f) return null;

        if (!WaitCache.TryGetValue(waitSeconds, out var wait))
        {
            wait = new WaitForSeconds(waitSeconds);
            WaitCache[waitSeconds] = wait;
        }

        return wait;
    }
}
