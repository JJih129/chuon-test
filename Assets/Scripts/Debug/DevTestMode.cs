#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEngine;

/// <summary>
/// Dev/test-only damage mode for manual play-testing (never compiled into release players).
/// F10 cycles: Off → player takes 10% damage → player takes no damage (god).
/// The choice persists in PlayerPrefs so it survives scene loads and restarts.
/// QA bot measurements must run with the mode Off (the default).
/// </summary>
public sealed class DevTestMode : MonoBehaviour
{
    public enum Mode { Off = 0, ReducedDamage = 1, God = 2 }

    const string PrefKey = "dev_test_damage_mode";
    const float ReducedDamageMultiplier = 0.1f;
    const KeyCode ToggleKey = KeyCode.F10;

    static DevTestMode _instance;
    static GUIStyle _labelStyle;

    public static Mode Current { get; private set; }

    /// <summary>Scales damage the player receives. Returns 0 in god mode.</summary>
    public static int ScalePlayerDamage(int amount)
    {
        switch (Current)
        {
            case Mode.God: return 0;
            case Mode.ReducedDamage: return amount <= 0 ? amount : Mathf.Max(1, Mathf.RoundToInt(amount * ReducedDamageMultiplier));
            default: return amount;
        }
    }

    public static void SetMode(Mode mode)
    {
        Current = mode;
        PlayerPrefs.SetInt(PrefKey, (int)mode);
        PlayerPrefs.Save();
        Debug.Log("[DevTestMode] " + Describe(mode));
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        Current = (Mode)Mathf.Clamp(PlayerPrefs.GetInt(PrefKey, 0), 0, 2);
        if (_instance != null)
            return;

        var go = new GameObject("__DevTestMode");
        go.hideFlags = HideFlags.HideInHierarchy;
        DontDestroyOnLoad(go);
        _instance = go.AddComponent<DevTestMode>();
    }

    void Update()
    {
        if (Input.GetKeyDown(ToggleKey))
            SetMode((Mode)(((int)Current + 1) % 3));
    }

    void OnGUI()
    {
        if (Current == Mode.Off)
            return;

        if (_labelStyle == null)
        {
            _labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 18,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.UpperCenter
            };
            _labelStyle.normal.textColor = new Color(1f, 0.35f, 0.3f, 0.95f);
        }

        GUI.Label(new Rect(0f, 6f, Screen.width, 28f), "TEST MODE (F10): " + Describe(Current), _labelStyle);
    }

    static string Describe(Mode mode)
    {
        switch (mode)
        {
            case Mode.ReducedDamage: return "받는 피해 10%";
            case Mode.God: return "무적";
            default: return "끔";
        }
    }
}
#endif
