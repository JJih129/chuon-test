using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using DG.Tweening;

public class SceneFader : MonoBehaviour
{
    public static SceneFader Instance;

    [Header("UI 연결")]
    public CanvasGroup fadeCanvasGroup; // 검은색 패널
    public float fadeDuration = 1.0f;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject); // 씬 이동해도 파괴되지 않음
        }
        else
        {
            // 이미 페이더가 있다면, 지금 씬에 있는 건 중복이니 삭제
            // 단, 삭제하기 전에 만약 지금 씬의 페이더에만 캔버스가 연결되어 있다면 정보를 넘겨줌 (안전장치)
            if (Instance.fadeCanvasGroup == null && fadeCanvasGroup != null)
            {
                Instance.fadeCanvasGroup = fadeCanvasGroup;
            }
            Destroy(gameObject);
            return;
        }
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    // 1. 게임이 처음 켜졌을 때 (Start)
    void Start()
    {
        FadeIn();
    }

    // 2. 씬이 이동했을 때 (OnSceneLoaded)
    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        FadeIn();
    }

    // 공통: 검은 화면 -> 투명하게 (밝아짐)
    void FadeIn()
    {
        if (fadeCanvasGroup)
        {
            fadeCanvasGroup.alpha = 1; // 일단 검게 시작
            fadeCanvasGroup.blocksRaycasts = true; // 터치 방지

            fadeCanvasGroup.DOFade(0, fadeDuration)
                .SetEase(Ease.InOutQuad)
                .OnComplete(() => {
                    fadeCanvasGroup.blocksRaycasts = false; // 끝나면 조작 허용
                });
        }
    }

    // 외부 호출용: 투명 -> 검은 화면 (어두워짐) 후 이동
    public void FadeOutAndLoadScene(string sceneName)
    {
        if (fadeCanvasGroup)
        {
            fadeCanvasGroup.blocksRaycasts = true;
            fadeCanvasGroup.DOFade(1, fadeDuration)
                .SetEase(Ease.InOutQuad)
                .OnComplete(() => SceneManager.LoadScene(sceneName));
        }
        else
        {
            SceneManager.LoadScene(sceneName);
        }
    }
}