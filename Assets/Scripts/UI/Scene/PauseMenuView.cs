using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

public class PauseMenuView : MonoBehaviour
{
    [Header("UI 연결")]
    public GameObject menuRoot;         
    public CanvasGroup backgroundGroup; 
    public RectTransform menuContainer; 
    
    [Header("설정창 연결")]
    public GameObject settingsPanel;
    public CanvasGroup settingsGroup;

    private void Awake()
    {
        menuRoot.SetActive(false);
        if (settingsPanel) settingsPanel.SetActive(false);
    }

    public void ShowMenu()
    {
        menuRoot.SetActive(true);
        
        // 1. 배경: 서서히 어두워짐 (Fade In)
        backgroundGroup.alpha = 0;
        backgroundGroup.DOFade(1, 0.3f).SetUpdate(true); 

        // 2. 메뉴 컨테이너: 팝업 연출 (작았다가 커짐 + 투명도)
        menuContainer.localScale = Vector3.one * 0.8f; // 0.8배 크기에서 시작
        menuContainer.DOScale(1f, 0.3f).SetEase(Ease.OutBack).SetUpdate(true); // 띠용~ 하고 커짐
        
        CanvasGroup menuCG = menuContainer.GetComponent<CanvasGroup>();
        if (menuCG)
        {
            menuCG.alpha = 0;
            menuCG.DOFade(1, 0.3f).SetUpdate(true);
        }
    }

    public void HideMenu(System.Action onComplete = null)
    {
        // 1. 배경: 투명해짐
        backgroundGroup.DOFade(0, 0.2f).SetUpdate(true);

        // 2. 메뉴: 작아지면서 사라짐
        menuContainer.DOScale(0.8f, 0.2f).SetEase(Ease.InQuad).SetUpdate(true);

        CanvasGroup menuCG = menuContainer.GetComponent<CanvasGroup>();
        if (menuCG) menuCG.DOFade(0, 0.2f).SetUpdate(true);

        // 애니메이션 끝나면 비활성화
        DOVirtual.DelayedCall(0.25f, () => 
        {
            menuRoot.SetActive(false);
            onComplete?.Invoke();
        }).SetUpdate(true);
    }

    // (설정창 토글 함수는 기존과 동일하게 사용하셔도 됩니다)
    public void ToggleSettings(bool isOpen)
    {
        // ... 기존 코드 유지 ...
        if (isOpen)
        {
            settingsPanel.SetActive(true);
            settingsGroup.alpha = 0;
            settingsGroup.DOFade(1, 0.3f).SetUpdate(true);
            menuContainer.gameObject.SetActive(false); 
        }
        else
        {
            settingsGroup.DOFade(0, 0.2f).SetUpdate(true).OnComplete(() =>
            {
                settingsPanel.SetActive(false);
                menuContainer.gameObject.SetActive(true);
                
                // 돌아올 때도 살짝 팝업 효과
                menuContainer.localScale = Vector3.one * 0.9f;
                menuContainer.DOScale(1f, 0.2f).SetEase(Ease.OutBack).SetUpdate(true);
            });
        }
    }
}