using UnityEngine;

public enum HudFocusCueId
{
    None = 0,
    Ampoule = 1,
    UltimateGauge = 2,
    Quest = 3,
    Guard = 4
}

[DisallowMultipleComponent]
public sealed class HudFocusTarget : MonoBehaviour
{
    [SerializeField] private HudFocusCueId cueId;

    public HudFocusCueId CueId => cueId;
    public RectTransform RectTransform => transform as RectTransform;
}
