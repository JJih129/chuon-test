using UnityEngine;
using UnityEngine.SceneManagement;

public class PauseManager : MonoBehaviour
{
    public static PauseManager Instance;

    [Header("■ 뷰(View) 연결")]
    public PauseMenuView uiView;

    [Header("■ 설정")]
    public string titleSceneName = "TitleScene";

    private bool isPaused = false;
    private bool isSettingsOpen = false;

    private void Awake()
    {
        if (Instance == null) Instance = this;
    }

    void Start()
    {
        // 시작 시 초기화
        ResumeGame();
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (isSettingsOpen) CloseSettings();
            else TogglePause();
        }
    }

    public void TogglePause()
    {
        if (isPaused) ResumeGame();
        else PauseGame();
    }

    public void PauseGame()
    {
        isPaused = true;
        Time.timeScale = 0f; // 시간 정지
        
        uiView.ShowMenu(); // 메뉴 보이기

        // ★ [핵심] 커서를 보이게 하고, 잠금을 풉니다.
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
    }

    public void ResumeGame()
    {
        isPaused = false;
        
        // 메뉴 숨기기 연출 후 실행
        uiView.HideMenu(() => 
        {
            Time.timeScale = 1f; // 시간 재개
            
            // ★ [핵심] 게임으로 돌아가면 커서를 다시 중앙에 고정하고 숨깁니다.
            Cursor.visible = false;
            Cursor.lockState = CursorLockMode.Locked;
        });
    }

    // ──────────────────────────────────────────────
    // 버튼 연결 함수
    // ──────────────────────────────────────────────
    public void OnClick_Resume() => ResumeGame();

    public void OnClick_Restart()
    {
        Time.timeScale = 1f;
        if (SceneFader.Instance) SceneFader.Instance.FadeOutAndLoadScene(SceneManager.GetActiveScene().name);
        else SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    public void OnClick_Settings() { isSettingsOpen = true; uiView.ToggleSettings(true); }
    public void CloseSettings() { isSettingsOpen = false; uiView.ToggleSettings(false); }

    public void OnClick_ToTitle()
    {
        Time.timeScale = 1f;
        if (SceneFader.Instance) SceneFader.Instance.FadeOutAndLoadScene(titleSceneName);
        else SceneManager.LoadScene(titleSceneName);
    }

    public void OnClick_Quit() 
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}