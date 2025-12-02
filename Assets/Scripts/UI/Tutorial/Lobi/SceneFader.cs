using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using DG.Tweening; // DOTween 필수

public class SceneFader : MonoBehaviour
{
    public static SceneFader Instance;

    [Header("UI 연결")]
    public CanvasGroup fadeCanvasGroup; // 검은색 패널의 CanvasGroup
    public float fadeDuration = 1.0f;

    private void Awake()
    {
        // 싱글톤 패턴 (어디서든 접근 가능하게 설정)
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject); // 씬이 바뀌어도 파괴되지 않음
        }
        else
        {
            Destroy(gameObject); // 중복 생성 방지
        }
    }

    void Start()
    {
        // 씬 시작 시 무조건 페이드 인 (검정 -> 투명)
        if (fadeCanvasGroup)
        {
            fadeCanvasGroup.alpha = 1; // 처음엔 검게
            fadeCanvasGroup.blocksRaycasts = true; // 조작 방지

            fadeCanvasGroup.DOFade(0, fadeDuration)
                .SetEase(Ease.InOutQuad)
                .OnComplete(() => {
                    fadeCanvasGroup.blocksRaycasts = false; // 페이드 끝나면 조작 허용
                });
        }
    }

    // 외부(LobbyManager 등)에서 호출: 페이드 아웃 후 씬 이동
    public void FadeOutAndLoadScene(string sceneName)
    {
        if (fadeCanvasGroup)
        {
            fadeCanvasGroup.blocksRaycasts = true; // 터치 방지
            fadeCanvasGroup.DOFade(1, fadeDuration)
                .SetEase(Ease.InOutQuad)
                .OnComplete(() => SceneManager.LoadScene(sceneName));
        }
        else
        {
            // 페이드 UI가 연결 안 되어 있으면 그냥 바로 이동
            SceneManager.LoadScene(sceneName);
        }
    }
}