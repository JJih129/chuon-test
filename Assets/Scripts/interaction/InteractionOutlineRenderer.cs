using System;
using UnityEngine;

/// <summary>
/// InteractionOutlineRenderer
/// - QuickOutline(Outline.cs) 우선 사용
/// - 없으면 material 키워드 + MPB 방식으로 외곽선 토글
/// - Awake에서 초기 상태를 강제 비활성화
/// </summary>
[DisallowMultipleComponent]
public class InteractionOutlineRenderer : MonoBehaviour
{
    [Header("QuickOutline 우선 사용")]
    public Outline quickOutline;
    public bool preferQuickOutline = true;

    [Header("Material 키워드 방식 (Fallback)")]
    public bool useSharedMaterial = false;
    public string outlineKeyword = "_OUTLINE_ON";
    public string colorProperty = "_OutlineColor";
    public string widthProperty = "_OutlineWidth";
    public Color outlineColor = Color.cyan;
    [Range(0f, 10f)] public float outlineWidth = 1.5f;
    [Tooltip("비워두면 자식 Renderer 자동 수집")]
    public Renderer[] renderers;

    MaterialPropertyBlock _mpb;
    bool _active = false;

    void Reset()
    {
        if (renderers == null || renderers.Length == 0)
            renderers = GetComponentsInChildren<Renderer>(true);
        if (quickOutline == null)
            quickOutline = GetComponentInChildren<Outline>(true);
    }

    void Awake()
    {
        if (renderers == null || renderers.Length == 0)
            renderers = GetComponentsInChildren<Renderer>(true);

        if (quickOutline == null)
            quickOutline = GetComponentInChildren<Outline>(true);

        _mpb = new MaterialPropertyBlock();

        // 강제 초기 비활성화: 시작 시 외곽선이 켜져있지 않도록 보장
        if (quickOutline != null)
        {
            quickOutline.enabled = false;
            try { quickOutline.OutlineWidth = 0f; } catch { }
        }

        Apply(false);
    }

    /// <summary>
    /// 외곽선 켜기/끄기
    /// </summary>
    public void SetActive(bool on)
    {
        if (_active == on) return;
        _active = on;
        Apply(_active);
    }

    void Apply(bool on)
    {
        // QuickOutline 우선 처리
        if (preferQuickOutline && quickOutline != null)
        {
            quickOutline.enabled = on;
            try { quickOutline.OutlineColor = outlineColor; } catch { }
            try { quickOutline.OutlineWidth = outlineWidth; } catch { }
            return;
        }

        // Fallback: 머티리얼 키워드 + MPB 방식
        if (renderers == null) return;

        foreach (var r in renderers)
        {
            if (!r) continue;

            if (!string.IsNullOrEmpty(outlineKeyword))
            {
                if (useSharedMaterial)
                {
                    foreach (var m in r.sharedMaterials) if (m) ToggleKeyword(m, on);
                }
                else
                {
                    var mats = r.materials;
                    for (int i = 0; i < mats.Length; i++) if (mats[i]) ToggleKeyword(mats[i], on);
                    r.materials = mats;
                }
            }

            r.GetPropertyBlock(_mpb);
            if (!string.IsNullOrEmpty(colorProperty)) _mpb.SetColor(colorProperty, outlineColor);
            if (!string.IsNullOrEmpty(widthProperty)) _mpb.SetFloat(widthProperty, on ? outlineWidth : 0f);
            r.SetPropertyBlock(_mpb);
        }
    }

    void ToggleKeyword(Material m, bool on)
    {
        if (m == null) return;
        if (on) m.EnableKeyword(outlineKeyword);
        else m.DisableKeyword(outlineKeyword);
    }
}
