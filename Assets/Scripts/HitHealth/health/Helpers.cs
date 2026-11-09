using System;
using System.Collections;
using UnityEngine;

public static class Helpers
{
    // 사용 예:
    // StartCoroutine(Helpers.SetBoolFalseAfterSeconds(()=> someBool=false, 1.2f));
    public static IEnumerator SetBoolFalseAfterSeconds(Action setFalseAction, float seconds)
    {
        yield return new WaitForSeconds(seconds);
        setFalseAction?.Invoke();
    }

    // 편의 래퍼 (MonoBehaviour에서 직접 시작용)
    public static Coroutine StartSetFalseCoroutine(MonoBehaviour host, Action setFalseAction, float seconds)
    {
        if (host == null) return null;
        return host.StartCoroutine(SetBoolFalseAfterSeconds(setFalseAction, seconds));
    }
}
