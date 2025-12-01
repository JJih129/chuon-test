using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using System.Collections;

public class AutoTeamSplash : MonoBehaviour
{
    [Header("UI 설정")]
    public CanvasGroup logoCanvasGroup; // 로고 이미지의 Canvas Group
    public float fadeDuration = 1.0f;     // 나타나고/사라지는 시간
    public float displayDuration = 2.0f;  // 로고가 떠있는 시간
    public float startDelay = 0.5f;       // 유니티 로고 후 검은 화면 대기 시간

    [Header("다음 씬 설정")]
    public string titleSceneName = "TitleScene"; // 이동할 타이틀 씬 이름

    void Start()
    {
        // 시작하자마자 코루틴 실행
        StartCoroutine(PlaySplashSequence());
    }

    IEnumerator PlaySplashSequence()
    {
        // 0. 초기화 (로고 투명하게)
        logoCanvasGroup.alpha = 0f;

        // 1. 유니티 로고 직후 잠시 검은 화면 대기 (연출상 자연스러움)
        yield return new WaitForSeconds(startDelay);

        // 2. Fade In (로고 서서히 등장)
        float timer = 0f;
        while (timer < fadeDuration)
        {
            timer += Time.deltaTime;
            logoCanvasGroup.alpha = Mathf.Lerp(0f, 1f, timer / fadeDuration);
            yield return null;
        }
        logoCanvasGroup.alpha = 1f;

        // 3. 로고 유지 (보여주기)
        yield return new WaitForSeconds(displayDuration);

        // 4. Fade Out (로고 서서히 퇴장)
        timer = 0f;
        while (timer < fadeDuration)
        {
            timer += Time.deltaTime;
            logoCanvasGroup.alpha = Mathf.Lerp(1f, 0f, timer / fadeDuration);
            yield return null;
        }
        logoCanvasGroup.alpha = 0f;

        // 5. 타이틀 씬으로 이동
        // 이 시점에 씬이 넘어가면서 타이틀 화면의 BGM이 재생됩니다.
        SceneManager.LoadScene(titleSceneName);
    }
}