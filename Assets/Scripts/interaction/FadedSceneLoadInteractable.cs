using UnityEngine;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
public class FadedSceneLoadInteractable : SceneLoadInteractable
{
    [Header("Fade Transition")]
    [SerializeField] private bool useSceneFaderWhenAvailable = true;
    [SerializeField] private bool fallbackToDirectLoad = true;

    protected override void LoadScene(string targetSceneName)
    {
        if (useSceneFaderWhenAvailable)
        {
            SceneFader sceneFader = RuntimeSceneFaderUtility.EnsureSceneFader();
            if (sceneFader != null)
            {
                sceneFader.FadeOutAndLoadScene(targetSceneName);
                return;
            }
        }

        if (fallbackToDirectLoad)
        {
            SceneManager.LoadScene(targetSceneName);
            return;
        }

        Debug.LogWarning($"[FadedSceneLoadInteractable] SceneFader unavailable for '{targetSceneName}'.", this);
    }
}
