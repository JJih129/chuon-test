using UnityEngine;

public static class AnimatorExtensions
{
    // Animator에 파라미터 존재 여부를 타입까지 확인
    public static bool HasParameterOfType(this Animator self, string name, AnimatorControllerParameterType type)
    {
        if (!self || string.IsNullOrEmpty(name)) return false;
        foreach (var p in self.parameters)
            if (p.name == name && p.type == type)
                return true;
        return false;
    }
}