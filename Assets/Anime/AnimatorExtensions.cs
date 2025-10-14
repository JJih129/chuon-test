using UnityEngine;

public static class AnimatorExtensions
{
    public static bool HasParameterOfType(this Animator animator, string name, UnityEngine.AnimatorControllerParameterType type)
    {
        if (animator == null) return false;
        foreach (var p in animator.parameters)
            if (p.name == name && p.type == type) return true;
        return false;
    }

    public static bool HasTrigger(this Animator animator, string name) => animator.HasParameterOfType(name, UnityEngine.AnimatorControllerParameterType.Trigger);
    public static bool HasBool(this Animator animator, string name) => animator.HasParameterOfType(name, UnityEngine.AnimatorControllerParameterType.Bool);
}
