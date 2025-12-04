// Assets/Scripts/UI/TitleSceneUIController.cs
using UnityEngine;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
public class TitleSceneUIController : MonoBehaviour
{
    [Header("게임 시작 시 로드할 씬 이름")]
    [SerializeField]
    private string gameSceneName = "BootScene"; // 실제 씬 이름으로 교체

    [Header("씬 로드 전에 Time.timeScale 초기화할지 여부")]
    [SerializeField]
    private bool resetTimeScaleOnLoad = true;

    // 버튼 OnClick에서 호출할 함수
    public void OnClick_StartGame()
    {
        if (resetTimeScaleOnLoad && Time.timeScale != 1f)
            Time.timeScale = 1f;

        if (string.IsNullOrEmpty(gameSceneName))
    {
        Debug.LogError("[TitleSceneUIController] gameSceneName 이 비어 있습니다.", this);
        return;
    }

    // ★ [핵심] SceneFader를 통해 "BootScene"으로 이동해야 합니다.
    if (SceneFader.Instance != null)
    {
        SceneFader.Instance.FadeOutAndLoadScene(gameSceneName); // gameSceneName == "BootScene"
    }
    else
    {
        // SceneFader가 없을 때 대비 (비상용)
        SceneManager.LoadScene(gameSceneName, LoadSceneMode.Single); 
    }
}}