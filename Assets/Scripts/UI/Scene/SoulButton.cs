using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using DG.Tweening;

public class SoulButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
    [Header("■ UI 연결")]
    public TextMeshProUGUI buttonText;
    public Image iconImage;

    [Header("■ 연출 설정")]
    public Color normalColor = Color.white; // ★ 기본값 흰색으로 변경
    public Color hoverColor = new Color(1f, 0.8f, 0f, 1f); 
    public float scaleAmount = 1.1f;
    Vector3 baseScale;

    private void Awake()
    {
        baseScale = transform.localScale;
    }

    private void Start()
    {
        if (buttonText) buttonText.color = normalColor;
        if (iconImage) iconImage.DOFade(0, 0).SetUpdate(true);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        // ★ SetUpdate(true) 필수
        if (buttonText) buttonText.DOColor(hoverColor, 0.2f).SetUpdate(true);
        transform.DOScale(baseScale * scaleAmount, 0.2f).SetUpdate(true);
        if (iconImage) iconImage.DOFade(1, 0.2f).SetUpdate(true);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (buttonText) buttonText.DOColor(normalColor, 0.2f).SetUpdate(true);
        transform.DOScale(baseScale, 0.2f).SetUpdate(true);
        if (iconImage) iconImage.DOFade(0, 0.2f).SetUpdate(true);
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        transform.DOPunchScale(new Vector3(0.1f, 0.1f, 0.1f), 0.1f).SetUpdate(true);
    }
}
