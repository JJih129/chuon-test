using System;
using System.Collections;
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

    /// <summary>
    /// 반복 14: 씬 로드 직전에 기다릴 코루틴 (예: 튜토리얼 VR 종료 연출). null 을 돌려주거나 지정하지 않으면 기존처럼 바로 로드.
    /// BeforeSceneLoad 이벤트 다음에 호출된다. 대기 중에는 다시 상호작용해도 무시한다.
    /// </summary>
    public Func<SceneLoadInteractable, object, IEnumerator> BeforeSceneLoadRoutine;

    bool _pendingLoad;

    public override bool TryInteract(object invoker = null)
    {
        if (_pendingLoad)
            return false;

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

        IEnumerator wait = BeforeSceneLoadRoutine != null ? BeforeSceneLoadRoutine(this, invoker) : null;
        if (wait != null && isActiveAndEnabled)
        {
            _pendingLoad = true;
            StartCoroutine(CoLoadAfter(wait, sceneName));
            return true;
        }

        LoadScene(sceneName);
        return true;
    }

    IEnumerator CoLoadAfter(IEnumerator wait, string targetSceneName)
    {
        yield return wait;
        LoadScene(targetSceneName);
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
