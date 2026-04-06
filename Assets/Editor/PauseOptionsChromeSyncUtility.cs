using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public static class PauseOptionsChromeSyncUtility
{
    const string PauseOptionsRootPrefabPath = "Assets/Prefabs/Generated/PauseOptionsRoot.prefab";
    const string ContentCanvasName = "UI Canvas";
    const string HeaderLineName = "_OverlayHeaderLine";
    const string TabsRootName = "_OverlayTabs";
    const string TabControlsName = "_TabControls";
    const string TabAudioName = "_TabAudio";
    const string TabVideoName = "_TabVideo";
    const string TabIndicatorName = "_Indicator";
    const string ControlsColumnsRootName = "_ControlsColumns";
    const string StandardColumnsRootName = "_StandardColumns";
    const string ActionColumnName = "_ActionColumn";
    const string CurrentColumnName = "_CurrentColumn";
    const string ChangeColumnName = "_ChangeColumn";
    const string OptionColumnName = "_OptionColumn";
    const string ValueColumnName = "_ValueColumn";
    const string ControlColumnName = "_ControlColumn";
    const string FooterHintName = "_FooterHint";
    const string RebindHintPanelName = "_RebindHintPanel";
    const string RebindHintTextName = "_RebindHintText";
    const string DropdownBlockerName = "_RuntimeDropdownBlocker";
    const string DropdownPopupName = "_RuntimeDropdownPopup";
    const string DropdownViewportName = "_Viewport";
    const string DropdownContentName = "_Content";
    const string DropdownScrollbarName = "_Scrollbar";
    const string DropdownSlidingAreaName = "_SlidingArea";
    const string DropdownHandleName = "_Handle";
    const string DropdownOptionTemplateName = "_OptionTemplate";
    const string DropdownOptionTextName = "Text";
    const string DropdownSelectedMarkerName = "SelectedMarker";
    const string RuntimeKeyValueName = "_RuntimeKeyValue";
    const string RuntimeKeyValueTextName = "_RuntimeKeyValueText";
    const string RuntimeRebindButtonName = "_RuntimeRebindButton";
    const string RuntimeRebindButtonTextName = "_RuntimeRebindButtonText";
    const string RuntimeValueTextName = "_RuntimeValueText";
    const string RuntimeSliderValueBoxName = "_RuntimeSliderValueBox";
    const string RuntimeToggleValueBoxName = "_RuntimeToggleValueBox";
    const string RuntimeDropdownValueBoxName = "_RuntimeDropdownValueBox";
    const string RuntimeSliderTrackName = "_RuntimeSliderTrack";
    const string RuntimeFillAreaName = "_RuntimeFillArea";
    const string RuntimeFillName = "_RuntimeFill";
    const string RuntimeHandleAreaName = "_RuntimeHandleArea";
    const string RuntimeHandleRootName = "_RuntimeHandle";
    const string RuntimeToggleBackgroundName = "_RuntimeToggleBackground";
    const string RuntimeToggleCheckName = "_RuntimeToggleCheck";
    const string RuntimeCaptionName = "_RuntimeCaption";
    const string RuntimeArrowName = "_RuntimeArrow";

    static readonly string[] OverlayLineNames =
    {
        "_OverlayTopGlow",
        "_OverlayBottomGlow",
        "_OverlayLeftBar",
        "_OverlayRightBar",
    };

    static readonly string[] OverlayCornerNames =
    {
        "_OverlayCornerTL",
        "_OverlayCornerTR",
        "_OverlayCornerBL",
        "_OverlayCornerBR",
    };

    static readonly string[] PanelNames =
    {
        "Panel_Controls",
        "Panel_Audio",
        "Panel_Video",
        "Panel_Bottom",
    };

    static readonly string[] BorderLineNames =
    {
        "_BorderTop",
        "_BorderBottom",
        "_BorderLeft",
        "_BorderRight",
    };

    static readonly string[] PanelCornerNames =
    {
        "_CornerTL",
        "_CornerTR",
        "_CornerBL",
        "_CornerBR",
    };

    [MenuItem("Tools/Validation/Sync Pause Options Chrome")]
    static void SyncPauseOptionsChrome()
    {
        RunInternal(showDialog: true);
    }

    public static SyncSummary RunFromFastMcp()
    {
        return RunInternal(showDialog: false);
    }

    static SyncSummary RunInternal(bool showDialog)
    {
        var report = new SyncReport();

        if (AssetDatabase.LoadAssetAtPath<GameObject>(PauseOptionsRootPrefabPath) == null)
        {
            report.Warn($"Pause options prefab missing: {PauseOptionsRootPrefabPath}");
            report.Emit(showDialog);
            return report.ToSummary();
        }

        var root = PrefabUtility.LoadPrefabContents(PauseOptionsRootPrefabPath);
        try
        {
            var changed = SyncChrome(root, report);
            if (changed)
            {
                PrefabUtility.SaveAsPrefabAsset(root, PauseOptionsRootPrefabPath);
                report.Info($"Synced prefab: {PauseOptionsRootPrefabPath}");
            }
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        report.Emit(showDialog);
        return report.ToSummary();
    }

    static bool SyncChrome(GameObject prefabRoot, SyncReport report)
    {
        var canvasRoot = FindContentCanvas(prefabRoot.transform);
        if (canvasRoot == null)
        {
            report.Warn($"Unable to find '{ContentCanvasName}' in {PauseOptionsRootPrefabPath}");
            return false;
        }

        var changed = false;
        EnsureHeaderLine(canvasRoot, ref changed);
        EnsureOverlayDecorations(canvasRoot, ref changed);

        var tabsRoot = EnsureRectChild(canvasRoot, TabsRootName, ref changed);
        EnsureTabButton(tabsRoot, TabControlsName, "CONTROLS", ref changed);
        EnsureTabButton(tabsRoot, TabAudioName, "AUDIO", ref changed);
        EnsureTabButton(tabsRoot, TabVideoName, "VIDEO", ref changed);

        EnsurePanelChrome(canvasRoot, ref changed);
        EnsureColumnChrome(canvasRoot, ref changed);
        EnsureRowChrome(canvasRoot, ref changed);
        EnsureFooterHint(canvasRoot, ref changed);
        EnsureRebindHint(canvasRoot, ref changed);
        EnsureDropdownChrome(canvasRoot, ref changed);
        return changed;
    }

    static RectTransform FindContentCanvas(Transform prefabRoot)
    {
        var child = FindDeepChild(prefabRoot, ContentCanvasName);
        if (child is RectTransform rectChild)
            return rectChild;

        return prefabRoot.GetComponentInChildren<Canvas>(true)?.GetComponent<RectTransform>();
    }

    static void EnsureHeaderLine(RectTransform canvasRoot, ref bool changed)
    {
        var headerLine = EnsureRectChild(canvasRoot, HeaderLineName, ref changed);
        EnsureCanvasRenderer(headerLine.gameObject, ref changed);
        EnsureImage(headerLine.gameObject, ref changed);
    }

    static void EnsureOverlayDecorations(RectTransform canvasRoot, ref bool changed)
    {
        foreach (var lineName in OverlayLineNames)
            EnsureDecorationLine(canvasRoot, lineName, ref changed);

        foreach (var cornerName in OverlayCornerNames)
            EnsureCornerBracket(canvasRoot, cornerName, ref changed);
    }

    static void EnsureTabButton(RectTransform tabsRoot, string buttonName, string label, ref bool changed)
    {
        var buttonRect = EnsureRectChild(tabsRoot, buttonName, ref changed);
        var buttonGo = buttonRect.gameObject;
        EnsureCanvasRenderer(buttonGo, ref changed);
        EnsureImage(buttonGo, ref changed);
        EnsureButton(buttonGo, ref changed);
        EnsureOutline(buttonRect, ref changed);

        var textRect = EnsureRectChild(buttonRect, "Text", ref changed);
        var textGo = textRect.gameObject;
        EnsureCanvasRenderer(textGo, ref changed);
        var text = EnsureText(textGo, ref changed);
        if (text.font == null)
        {
            text.font = GetBuiltinFont();
            changed = true;
        }

        if (text.text != label)
        {
            text.text = label;
            changed = true;
        }

        var indicatorRect = EnsureRectChild(buttonRect, TabIndicatorName, ref changed);
        EnsureCanvasRenderer(indicatorRect.gameObject, ref changed);
        EnsureImage(indicatorRect.gameObject, ref changed);
    }

    static void EnsurePanelChrome(RectTransform canvasRoot, ref bool changed)
    {
        foreach (var panelName in PanelNames)
        {
            var panel = FindDeepChild(canvasRoot, panelName) as RectTransform;
            if (panel == null)
                continue;

            EnsureOutline(panel, ref changed);
            EnsurePanelCorners(panel, ref changed);
        }
    }

    static void EnsureColumnChrome(RectTransform canvasRoot, ref bool changed)
    {
        var controlsPanel = FindDeepChild(canvasRoot, "Panel_Controls") as RectTransform;
        if (controlsPanel != null)
        {
            var controlsColumns = EnsureRectChild(controlsPanel, ControlsColumnsRootName, ref changed);
            EnsureColumnLabel(controlsColumns, ActionColumnName, "ACTION", ref changed);
            EnsureColumnLabel(controlsColumns, CurrentColumnName, "CURRENT KEY", ref changed);
            EnsureColumnLabel(controlsColumns, ChangeColumnName, "CHANGE", ref changed);
        }

        foreach (var panelName in new[] { "Panel_Audio", "Panel_Video" })
        {
            var panel = FindDeepChild(canvasRoot, panelName) as RectTransform;
            if (panel == null)
                continue;

            var standardColumns = EnsureRectChild(panel, StandardColumnsRootName, ref changed);
            EnsureColumnLabel(standardColumns, OptionColumnName, "OPTION", ref changed);
            EnsureColumnLabel(standardColumns, ValueColumnName, "VALUE", ref changed);
            EnsureColumnLabel(standardColumns, ControlColumnName, "CONTROL", ref changed);
        }
    }

    static void EnsureRowChrome(RectTransform canvasRoot, ref bool changed)
    {
        foreach (var panelName in PanelNames)
        {
            var panel = FindDeepChild(canvasRoot, panelName) as RectTransform;
            if (panel == null)
                continue;

            foreach (Transform child in panel)
            {
                if (!(child is RectTransform row) || !IsConfigRow(row))
                    continue;

                EnsureRowShell(row, ref changed);
            }
        }
    }

    static void EnsureFooterHint(RectTransform canvasRoot, ref bool changed)
    {
        var footerRect = EnsureRectChild(canvasRoot, FooterHintName, ref changed);
        var footerGo = footerRect.gameObject;
        EnsureCanvasRenderer(footerGo, ref changed);
        var text = EnsureText(footerGo, ref changed);
        if (text.font == null)
        {
            text.font = GetBuiltinFont();
            changed = true;
        }

        const string footerText = "TAB / Q,E SWITCH  -  ESC CLOSE";
        if (text.text != footerText)
        {
            text.text = footerText;
            changed = true;
        }
    }

    static void EnsureRebindHint(RectTransform canvasRoot, ref bool changed)
    {
        var panel = EnsureRectChild(canvasRoot, RebindHintPanelName, ref changed);
        EnsureCanvasRenderer(panel.gameObject, ref changed);
        EnsureImage(panel.gameObject, ref changed);
        EnsureOutline(panel, ref changed);

        var textRect = EnsureRectChild(panel, RebindHintTextName, ref changed);
        EnsureCanvasRenderer(textRect.gameObject, ref changed);
        var text = EnsureText(textRect.gameObject, ref changed);
        if (text.font == null)
        {
            text.font = GetBuiltinFont();
            changed = true;
        }
    }

    static void EnsureDropdownChrome(RectTransform canvasRoot, ref bool changed)
    {
        var blocker = EnsureRectChild(canvasRoot, DropdownBlockerName, ref changed);
        EnsureCanvasRenderer(blocker.gameObject, ref changed);
        EnsureImage(blocker.gameObject, ref changed);
        EnsureButton(blocker.gameObject, ref changed);

        var popup = EnsureRectChild(blocker, DropdownPopupName, ref changed);
        EnsureCanvasRenderer(popup.gameObject, ref changed);
        EnsureImage(popup.gameObject, ref changed);
        EnsureScrollRect(popup.gameObject, ref changed);
        EnsureOutline(popup, ref changed);

        var viewport = EnsureRectChild(popup, DropdownViewportName, ref changed);
        EnsureCanvasRenderer(viewport.gameObject, ref changed);
        EnsureImage(viewport.gameObject, ref changed);
        EnsureMask(viewport.gameObject, ref changed);

        var content = EnsureRectChild(viewport, DropdownContentName, ref changed);
        EnsureVerticalLayoutGroup(content.gameObject, ref changed);
        EnsureContentSizeFitter(content.gameObject, ref changed);

        var scrollbar = EnsureRectChild(popup, DropdownScrollbarName, ref changed);
        EnsureCanvasRenderer(scrollbar.gameObject, ref changed);
        EnsureImage(scrollbar.gameObject, ref changed);
        EnsureScrollbar(scrollbar.gameObject, ref changed);

        var slidingArea = EnsureRectChild(scrollbar, DropdownSlidingAreaName, ref changed);
        var handle = EnsureRectChild(slidingArea, DropdownHandleName, ref changed);
        EnsureCanvasRenderer(handle.gameObject, ref changed);
        EnsureImage(handle.gameObject, ref changed);

        var optionTemplate = EnsureRectChild(popup, DropdownOptionTemplateName, ref changed);
        EnsureCanvasRenderer(optionTemplate.gameObject, ref changed);
        EnsureImage(optionTemplate.gameObject, ref changed);
        EnsureButton(optionTemplate.gameObject, ref changed);

        var optionText = EnsureRectChild(optionTemplate, DropdownOptionTextName, ref changed);
        EnsureCanvasRenderer(optionText.gameObject, ref changed);
        var optionLabel = EnsureText(optionText.gameObject, ref changed);
        EnsureFont(optionLabel, ref changed);

        var marker = EnsureRectChild(optionTemplate, DropdownSelectedMarkerName, ref changed);
        EnsureCanvasRenderer(marker.gameObject, ref changed);
        EnsureImage(marker.gameObject, ref changed);
    }

    static void EnsureRowShell(RectTransform row, ref bool changed)
    {
        var slider = row.GetComponentInChildren<Slider>(true);
        var toggle = row.GetComponentInChildren<Toggle>(true);
        var dropdown = row.GetComponentInChildren<Dropdown>(true);
        var keyButton = FindKeybindSourceButton(row);

        if (keyButton != null)
            EnsureKeybindRowShell(row, ref changed);

        if (slider != null)
            EnsureSliderRowShell(row, slider, ref changed);

        if (toggle != null)
            EnsureToggleRowShell(row, toggle, ref changed);

        if (dropdown != null)
            EnsureDropdownRowShell(row, dropdown, ref changed);
    }

    static void EnsureKeybindRowShell(RectTransform row, ref bool changed)
    {
        var valueRoot = EnsureRectChild(row, RuntimeKeyValueName, ref changed);
        EnsureCanvasRenderer(valueRoot.gameObject, ref changed);
        EnsureImage(valueRoot.gameObject, ref changed);
        EnsureOutline(valueRoot, ref changed);

        var valueTextRect = EnsureRectChild(valueRoot, RuntimeKeyValueTextName, ref changed);
        EnsureCanvasRenderer(valueTextRect.gameObject, ref changed);
        var valueText = EnsureText(valueTextRect.gameObject, ref changed);
        EnsureFont(valueText, ref changed);

        var buttonRoot = EnsureRectChild(row, RuntimeRebindButtonName, ref changed);
        EnsureCanvasRenderer(buttonRoot.gameObject, ref changed);
        EnsureImage(buttonRoot.gameObject, ref changed);
        EnsureButton(buttonRoot.gameObject, ref changed);
        EnsureOutline(buttonRoot, ref changed);

        var buttonTextRect = EnsureRectChild(buttonRoot, RuntimeRebindButtonTextName, ref changed);
        EnsureCanvasRenderer(buttonTextRect.gameObject, ref changed);
        var buttonText = EnsureText(buttonTextRect.gameObject, ref changed);
        EnsureFont(buttonText, ref changed);
    }

    static void EnsureSliderRowShell(RectTransform row, Slider slider, ref bool changed)
    {
        EnsureValueBox(row, RuntimeSliderValueBoxName, ref changed);

        var sliderRect = slider.GetComponent<RectTransform>();
        if (sliderRect == null)
            return;

        var track = EnsureRectChild(sliderRect, RuntimeSliderTrackName, ref changed);
        EnsureCanvasRenderer(track.gameObject, ref changed);
        EnsureImage(track.gameObject, ref changed);

        var fillArea = EnsureRectChild(sliderRect, RuntimeFillAreaName, ref changed);
        var fill = EnsureRectChild(fillArea, RuntimeFillName, ref changed);
        EnsureCanvasRenderer(fill.gameObject, ref changed);
        EnsureImage(fill.gameObject, ref changed);

        var handleArea = EnsureRectChild(sliderRect, RuntimeHandleAreaName, ref changed);
        var handle = EnsureRectChild(handleArea, RuntimeHandleRootName, ref changed);
        EnsureCanvasRenderer(handle.gameObject, ref changed);
        EnsureImage(handle.gameObject, ref changed);
    }

    static void EnsureToggleRowShell(RectTransform row, Toggle toggle, ref bool changed)
    {
        EnsureValueBox(row, RuntimeToggleValueBoxName, ref changed);

        var toggleRect = toggle.GetComponent<RectTransform>();
        if (toggleRect == null)
            return;

        var background = EnsureRectChild(toggleRect, RuntimeToggleBackgroundName, ref changed);
        EnsureCanvasRenderer(background.gameObject, ref changed);
        EnsureImage(background.gameObject, ref changed);
        EnsureOutline(background, ref changed);

        var check = EnsureRectChild(toggleRect, RuntimeToggleCheckName, ref changed);
        EnsureCanvasRenderer(check.gameObject, ref changed);
        EnsureImage(check.gameObject, ref changed);
    }

    static void EnsureDropdownRowShell(RectTransform row, Dropdown dropdown, ref bool changed)
    {
        EnsureValueBox(row, RuntimeDropdownValueBoxName, ref changed);

        var dropdownRect = dropdown.GetComponent<RectTransform>();
        if (dropdownRect == null)
            return;

        var caption = EnsureRectChild(dropdownRect, RuntimeCaptionName, ref changed);
        EnsureCanvasRenderer(caption.gameObject, ref changed);
        var text = EnsureText(caption.gameObject, ref changed);
        EnsureFont(text, ref changed);

        var arrow = EnsureRectChild(dropdownRect, RuntimeArrowName, ref changed);
        EnsureCanvasRenderer(arrow.gameObject, ref changed);
        var arrowText = EnsureText(arrow.gameObject, ref changed);
        EnsureFont(arrowText, ref changed);
    }

    static void EnsureValueBox(RectTransform row, string boxName, ref bool changed)
    {
        var box = EnsureRectChild(row, boxName, ref changed);
        EnsureCanvasRenderer(box.gameObject, ref changed);
        EnsureImage(box.gameObject, ref changed);
        EnsureOutline(box, ref changed);

        var textRect = EnsureRectChild(box, RuntimeValueTextName, ref changed);
        EnsureCanvasRenderer(textRect.gameObject, ref changed);
        var text = EnsureText(textRect.gameObject, ref changed);
        EnsureFont(text, ref changed);
    }

    static void EnsureColumnLabel(RectTransform parent, string name, string label, ref bool changed)
    {
        var labelRect = EnsureRectChild(parent, name, ref changed);
        EnsureCanvasRenderer(labelRect.gameObject, ref changed);
        var text = EnsureText(labelRect.gameObject, ref changed);
        EnsureFont(text, ref changed);

        if (text.text != label)
        {
            text.text = label;
            changed = true;
        }
    }

    static void EnsureOutline(RectTransform parent, ref bool changed)
    {
        foreach (var lineName in BorderLineNames)
            EnsureDecorationLine(parent, lineName, ref changed);
    }

    static void EnsurePanelCorners(RectTransform parent, ref bool changed)
    {
        foreach (var cornerName in PanelCornerNames)
            EnsureCornerBracket(parent, cornerName, ref changed);
    }

    static void EnsureDecorationLine(RectTransform parent, string name, ref bool changed)
    {
        var line = EnsureRectChild(parent, name, ref changed);
        EnsureCanvasRenderer(line.gameObject, ref changed);
        EnsureImage(line.gameObject, ref changed);
    }

    static void EnsureCornerBracket(RectTransform parent, string cornerName, ref bool changed)
    {
        var cornerRoot = EnsureRectChild(parent, cornerName, ref changed);

        var horizontal = EnsureRectChild(cornerRoot, "_H", ref changed);
        EnsureCanvasRenderer(horizontal.gameObject, ref changed);
        EnsureImage(horizontal.gameObject, ref changed);

        var vertical = EnsureRectChild(cornerRoot, "_V", ref changed);
        EnsureCanvasRenderer(vertical.gameObject, ref changed);
        EnsureImage(vertical.gameObject, ref changed);
    }

    static RectTransform EnsureRectChild(Transform parent, string name, ref bool changed)
    {
        var existing = parent.Find(name) as RectTransform;
        if (existing != null)
            return existing;

        var go = new GameObject(name, typeof(RectTransform));
        go.layer = parent.gameObject.layer;
        go.transform.SetParent(parent, false);
        changed = true;
        return go.GetComponent<RectTransform>();
    }

    static void EnsureCanvasRenderer(GameObject go, ref bool changed)
    {
        if (go.GetComponent<CanvasRenderer>() != null)
            return;

        go.AddComponent<CanvasRenderer>();
        changed = true;
    }

    static void EnsureImage(GameObject go, ref bool changed)
    {
        if (go.GetComponent<Image>() != null)
            return;

        go.AddComponent<Image>();
        changed = true;
    }

    static void EnsureButton(GameObject go, ref bool changed)
    {
        if (go.GetComponent<Button>() != null)
            return;

        go.AddComponent<Button>();
        changed = true;
    }

    static void EnsureFont(Text text, ref bool changed)
    {
        if (text.font != null)
            return;

        text.font = GetBuiltinFont();
        changed = true;
    }

    static void EnsureMask(GameObject go, ref bool changed)
    {
        if (go.GetComponent<Mask>() != null)
            return;

        go.AddComponent<Mask>();
        changed = true;
    }

    static void EnsureScrollRect(GameObject go, ref bool changed)
    {
        if (go.GetComponent<ScrollRect>() != null)
            return;

        go.AddComponent<ScrollRect>();
        changed = true;
    }

    static void EnsureScrollbar(GameObject go, ref bool changed)
    {
        if (go.GetComponent<Scrollbar>() != null)
            return;

        go.AddComponent<Scrollbar>();
        changed = true;
    }

    static void EnsureVerticalLayoutGroup(GameObject go, ref bool changed)
    {
        if (go.GetComponent<VerticalLayoutGroup>() != null)
            return;

        go.AddComponent<VerticalLayoutGroup>();
        changed = true;
    }

    static void EnsureContentSizeFitter(GameObject go, ref bool changed)
    {
        if (go.GetComponent<ContentSizeFitter>() != null)
            return;

        go.AddComponent<ContentSizeFitter>();
        changed = true;
    }

    static Text EnsureText(GameObject go, ref bool changed)
    {
        var text = go.GetComponent<Text>();
        if (text != null)
            return text;

        changed = true;
        return go.AddComponent<Text>();
    }

    static Transform FindDeepChild(Transform root, string name)
    {
        if (root == null)
            return null;

        foreach (Transform child in root)
        {
            if (child.name == name)
                return child;

            var nested = FindDeepChild(child, name);
            if (nested != null)
                return nested;
        }

        return null;
    }

    static bool IsConfigRow(RectTransform row)
    {
        var name = row.name;
        if (string.IsNullOrEmpty(name) || name.StartsWith("_"))
            return false;

        return name.EndsWith("_Row")
            || name.StartsWith("Row_")
            || name.StartsWith("Key_")
            || name.StartsWith("Move")
            || name.StartsWith("Jump")
            || name.StartsWith("Attack")
            || name.StartsWith("Master")
            || name.StartsWith("Music")
            || name.StartsWith("SFX")
            || name.StartsWith("Mute")
            || name.StartsWith("Camera")
            || name.StartsWith("Resolution")
            || name.StartsWith("Fullscreen")
            || name.StartsWith("VSync")
            || name.StartsWith("Brightness")
            || name.StartsWith("Motion");
    }

    static Button FindKeybindSourceButton(RectTransform row)
    {
        foreach (var button in row.GetComponentsInChildren<Button>(true))
        {
            if (button == null)
                continue;

            if (button.name.StartsWith("Button_Key") || button.name.StartsWith("Button_Rebind") || button.name.Contains("Key"))
                return button;
        }

        return null;
    }

    static Font GetBuiltinFont()
    {
        return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    }

    public readonly struct SyncSummary
    {
        public SyncSummary(string details)
        {
            Details = details;
        }

        public string Details { get; }
        public override string ToString() => Details;
    }

    sealed class SyncReport
    {
        readonly List<string> infos = new List<string>();
        readonly List<string> warnings = new List<string>();

        public void Info(string message)
        {
            infos.Add("[INFO] " + message);
        }

        public void Warn(string message)
        {
            warnings.Add("[WARN] " + message);
        }

        public void Emit(bool showDialog)
        {
            var details = BuildDetails();
            if (warnings.Count > 0)
                Debug.LogWarning(details);
            else if (showDialog)
                Debug.Log(details);

            if (showDialog)
            {
                EditorUtility.DisplayDialog(
                    "Pause Options Chrome Sync",
                    $"Warnings: {warnings.Count}\n\nSee Console for details.",
                    "OK");
            }
        }

        public SyncSummary ToSummary()
        {
            return new SyncSummary(BuildDetails());
        }

        string BuildDetails()
        {
            var builder = new StringBuilder();
            builder.AppendLine("Pause options chrome sync completed.");
            builder.AppendLine($"Warnings: {warnings.Count}");

            foreach (var info in infos)
                builder.AppendLine(info);

            foreach (var warning in warnings)
                builder.AppendLine(warning);

            return builder.ToString();
        }
    }
}
