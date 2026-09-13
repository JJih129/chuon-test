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

    CanvasGroup _menuCG;

    private void Awake()
    {
        if (menuContainer != null)
            _menuCG = menuContainer.GetComponent<CanvasGroup>();

        if (menuRoot != null && menuRoot.activeSelf)
            menuRoot.SetActive(false);
        if (settingsPanel && settingsPanel.activeSelf)
            settingsPanel.SetActive(false);
    }

    public void ShowMenu()
    {
        if (menuRoot != null && !menuRoot.activeSelf)
            menuRoot.SetActive(true);
        
        // 1. 배경: 서서히 어두워짐 (Fade In)
        backgroundGroup.alpha = 0;
        backgroundGroup.DOFade(1, 0.3f).SetUpdate(true); 

        // 2. 메뉴 컨테이너: 팝업 연출 (작았다가 커짐 + 투명도)
        menuContainer.localScale = Vector3.one * 0.8f; // 0.8배 크기에서 시작
        menuContainer.DOScale(1f, 0.3f).SetEase(Ease.OutBack).SetUpdate(true); // 띠용~ 하고 커짐
        
        if (_menuCG)
        {
            _menuCG.alpha = 0;
            _menuCG.DOFade(1, 0.3f).SetUpdate(true);
        }
    }

    public void HideMenu(System.Action onComplete = null)
    {
        // 1. 배경: 투명해짐
        backgroundGroup.DOFade(0, 0.2f).SetUpdate(true);

        // 2. 메뉴: 작아지면서 사라짐
        menuContainer.DOScale(0.8f, 0.2f).SetEase(Ease.InQuad).SetUpdate(true);

        if (_menuCG) _menuCG.DOFade(0, 0.2f).SetUpdate(true);

        // 애니메이션 끝나면 비활성화
        DOVirtual.DelayedCall(0.25f, () => 
        {
            if (menuRoot != null && menuRoot.activeSelf)
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
            if (settingsPanel != null && !settingsPanel.activeSelf)
                settingsPanel.SetActive(true);
            if (settingsGroup != null)
            {
                settingsGroup.alpha = 0;
                settingsGroup.DOFade(1, 0.3f).SetUpdate(true);
            }
            if (menuContainer != null && menuContainer.gameObject.activeSelf)
                menuContainer.gameObject.SetActive(false);
        }
        else
        {
            if (settingsGroup == null)
            {
                if (settingsPanel != null && settingsPanel.activeSelf)
                    settingsPanel.SetActive(false);
                if (menuContainer != null && !menuContainer.gameObject.activeSelf)
                    menuContainer.gameObject.SetActive(true);
                return;
            }

            settingsGroup.DOFade(0, 0.2f).SetUpdate(true).OnComplete(() =>
            {
                if (settingsPanel != null && settingsPanel.activeSelf)
                    settingsPanel.SetActive(false);
                if (menuContainer != null && !menuContainer.gameObject.activeSelf)
                    menuContainer.gameObject.SetActive(true);
                
                // 돌아올 때도 살짝 팝업 효과
                menuContainer.localScale = Vector3.one * 0.9f;
                menuContainer.DOScale(1f, 0.2f).SetEase(Ease.OutBack).SetUpdate(true);
            });
        }
    }
}
