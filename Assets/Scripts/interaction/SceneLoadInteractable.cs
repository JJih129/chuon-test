using System;
using UnityEngine;
using UnityEngine.SceneManagement;

public class SceneLoadInteractable : BaseInteractable
{
    [Header("Scene Load")]
    [Tooltip("Build Settings에 등록된 씬 이름")]
    public string sceneName = "Lobby";

    [Header("Options")]
    [Tooltip("상호작용 로그 출력")]
    public bool showLog = true;

    public event Func<object, bool> InteractionGuard;
    public event Action<SceneLoadInteractable, object> BeforeSceneLoad;

    public override bool TryInteract(object invoker = null)
    {
        if (showLog)
            Debug.Log($"[SceneLoad] '{sceneName}' 씬으로 이동합니다.");

        if (!CanLoadScene(invoker))
            return false;

        if (string.IsNullOrEmpty(sceneName))
        {
            Debug.LogError("[SceneLoad] 이동할 씬 이름이 설정되지 않았습니다.");
            return false;
        }

        NotifyBeforeSceneLoad(invoker);
        LoadScene(sceneName);
        return true;
    }

    protected bool CanLoadScene(object invoker)
    {
        if (InteractionGuard == null)
            return true;

        Delegate[] guards = InteractionGuard.GetInvocationList();
        for (int i = 0; i < guards.Length; i++)
        {
            if (guards[i] is not Func<object, bool> guard)
                continue;

            if (!guard.Invoke(invoker))
                return false;
        }

        return true;
    }

    protected void NotifyBeforeSceneLoad(object invoker)
    {
        BeforeSceneLoad?.Invoke(this, invoker);
    }

    protected virtual void LoadScene(string targetSceneName)
    {
        SceneManager.LoadScene(targetSceneName);
    }
}
