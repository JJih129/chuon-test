using UnityEngine;
using System.Collections;

// 파일명: PatternVisuals.cs
public class PatternVisuals : MonoBehaviour
{
    [Header("렌더러 설정")]
    public Renderer visualPartRenderer;
    public string emissionColorName = "_EmissionColor";

    [Header("색상 설정 (패링 식별)")]
    public Color parryColor = new Color(1f, 0.5f, 0f) * 5f;
    public Color nonParryColor = Color.red * 5f;

    [Header("연출 시간")]
    public float flashDuration = 0.5f;

    private Material _visualMaterial;
    private Coroutine _flashRoutine;

    void Awake()
    {
        if (visualPartRenderer != null)
        {
            _visualMaterial = visualPartRenderer.material;
            _visualMaterial.SetColor(emissionColorName, Color.black);
        }
        else
        {
            Debug.LogError("PatternVisuals: 발광 파츠 렌더러가 연결되지 않았습니다.");
            enabled = false;
        }
    }

    public void StartVisualCue(bool isParryable)
    {
        if (_flashRoutine != null)
        {
            StopCoroutine(_flashRoutine);
        }
        _flashRoutine = StartCoroutine(Co_FlashVisuals(isParryable));
    }

    private System.Collections.IEnumerator Co_FlashVisuals(bool isParryable)
    {
        Color targetColor = isParryable ? parryColor : nonParryColor;

        _visualMaterial.SetColor(emissionColorName, targetColor);

        yield return new WaitForSeconds(flashDuration);

        _visualMaterial.SetColor(emissionColorName, Color.black);

        _flashRoutine = null;
    }
}