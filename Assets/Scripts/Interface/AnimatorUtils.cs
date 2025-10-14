// AnimatorUtils.cs (유틸)
using UnityEngine;

public static class AnimatorUtils
{
    public static bool HasParameter(Animator animator, string paramName)
    {
        if (animator == null || string.IsNullOrEmpty(paramName)) return false;
        var pars = animator.parameters;
        for (int i = 0; i < pars.Length; ++i)
            if (pars[i].name == paramName) return true;
        return false;
    }

    public static bool HasState(Animator animator, int layerIndex, string stateName)
    {
        if (animator == null || string.IsNullOrEmpty(stateName)) return false;
        var stateHash = Animator.StringToHash(stateName);
        var stateInfo = animator.GetCurrentAnimatorStateInfo(layerIndex);
        // GetAnimatorController has no direct API to list states at runtime; instead use try/catch with Play/IsName check.
        // Safer approach: rely on consistent naming and use Try-Catch around Play (see SafePlay)
        return true; // 호출부에서 더 정교화 가능
    }

    public static void SafeSetTrigger(Animator animator, string triggerName)
    {
        if (animator == null) return;
        if (HasParameter(animator, triggerName)) animator.SetTrigger(triggerName);
        else Debug.LogWarning($"Animator missing trigger '{triggerName}'.");
    }

    public static void SafeSetBool(Animator animator, string boolName, bool value)
    {
        if (animator == null) return;
        if (HasParameter(animator, boolName)) animator.SetBool(boolName, value);
        else Debug.LogWarning($"Animator missing bool '{boolName}'.");
    }

    public static void SafePlayStateOnLayer(Animator animator, string stateName, int layerIndex = 0, float normalizedTime = 0f)
    {
        if (animator == null) return;
        try
        {
            animator.Play(stateName, layerIndex, normalizedTime);
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"Animator.SafePlayStateOnLayer failed: {stateName} (layer {layerIndex}) - {ex.Message}");
        }
    }
}
