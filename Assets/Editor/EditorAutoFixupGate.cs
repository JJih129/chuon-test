using UnityEditor;

/// <summary>
/// Gate for editor scripts that used to rewrite prefabs/scenes/materials automatically on every
/// editor load (player model swap, boss textures, tutorial enemy, temp VFX, scene PlayerRoot sync).
/// Those silent rewrites replaced the committed player model and dirtied scenes, so they are now OFF
/// by default. Run them explicitly from their Tools/ menu items, or turn this toggle on.
/// </summary>
public static class EditorAutoFixupGate
{
    const string PrefKey = "ProjectChuOn.EnableAutoAssetFixupsOnLoad";
    const string MenuPath = "Tools/Project/Auto Asset Fixups On Editor Load";

    public static bool Enabled => EditorPrefs.GetBool(PrefKey, false);

    [MenuItem(MenuPath)]
    static void Toggle()
    {
        EditorPrefs.SetBool(PrefKey, !Enabled);
    }

    [MenuItem(MenuPath, true)]
    static bool ToggleValidate()
    {
        Menu.SetChecked(MenuPath, Enabled);
        return true;
    }
}
