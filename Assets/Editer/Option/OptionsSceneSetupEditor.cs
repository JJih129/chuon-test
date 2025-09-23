// Assets/Editor/OptionsSceneSetupEditor.cs
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.UI;

public static class OptionsSceneSetupEditor
{
    [MenuItem("Tools/추온/Create Options Scene (Create & Save)")]
    public static void CreateOptionsScene()
    {
        Directory.CreateDirectory("Assets/Scenes");
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var root = new GameObject("OptionsScene_Root");

        // Canvas
        var canvasGO = new GameObject("UI Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGO.transform.SetParent(root.transform, false);
        var canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasGO.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        // Panel_Back
        var panelBack = CreateUIFull("Panel_Back", canvasGO.transform);
        var imgBack = panelBack.AddComponent<Image>();
        imgBack.color = new Color(0.06f,0.06f,0.06f,1f);

        // Title
        var title = CreateUI("Text_Title", canvasGO.transform, new Vector2(800,80));
        var titleText = title.AddComponent<Text>();
        titleText.text = "Settings";
        titleText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        titleText.alignment = TextAnchor.MiddleCenter;
        titleText.fontSize = 42;
        var rtTitle = title.GetComponent<RectTransform>();
        rtTitle.anchorMin = new Vector2(0.5f,1f);
        rtTitle.anchorMax = new Vector2(0.5f,1f);
        rtTitle.pivot = new Vector2(0.5f,1f);
        rtTitle.anchoredPosition = new Vector2(0f,-24f);

        // Left: Audio Panel
        var audioPanel = CreateUI("Panel_Audio", canvasGO.transform, new Vector2(420,420));
        var audioRT = audioPanel.GetComponent<RectTransform>();
        audioRT.anchorMin = new Vector2(0f,0.5f);
        audioRT.anchorMax = new Vector2(0f,0.5f);
        audioRT.pivot = new Vector2(0f,0.5f);
        audioRT.anchoredPosition = new Vector2(40f,0f);
        AddPanelLabel(audioPanel.transform, "Audio");

        CreateSliderRow(audioPanel.transform, "Master", "MasterVolume");
        CreateSliderRow(audioPanel.transform, "Music", "MusicVolume");
        CreateSliderRow(audioPanel.transform, "SFX", "SfxVolume");
        CreateToggleRow(audioPanel.transform, "Mute All", "MuteAll");

        // Middle: Controls Panel
        var controlPanel = CreateUI("Panel_Controls", canvasGO.transform, new Vector2(520,420));
        var ctrlRT = controlPanel.GetComponent<RectTransform>();
        ctrlRT.anchorMin = new Vector2(0.5f,0.5f);
        ctrlRT.anchorMax = new Vector2(0.5f,0.5f);
        ctrlRT.pivot = new Vector2(0.5f,0.5f);
        ctrlRT.anchoredPosition = new Vector2(0f,0f);
        AddPanelLabel(controlPanel.transform, "Controls");

        // Example keybind rows
        CreateKeybindRow(controlPanel.transform, "Move Forward", "Key_MoveForward");
        CreateKeybindRow(controlPanel.transform, "Move Back", "Key_MoveBack");
        CreateKeybindRow(controlPanel.transform, "Jump", "Key_Jump");
        CreateKeybindRow(controlPanel.transform, "Attack", "Key_Attack");

        // Right: Video Panel
        var videoPanel = CreateUI("Panel_Video", canvasGO.transform, new Vector2(360,420));
        var vidRT = videoPanel.GetComponent<RectTransform>();
        vidRT.anchorMin = new Vector2(1f,0.5f);
        vidRT.anchorMax = new Vector2(1f,0.5f);
        vidRT.pivot = new Vector2(1f,0.5f);
        vidRT.anchoredPosition = new Vector2(-40f,0f);
        AddPanelLabel(videoPanel.transform, "Video");

        // Resolution Dropdown
        var resRow = CreateUI("Row_Resolution", videoPanel.transform, new Vector2(320,48));
        var resLabel = CreateText("Resolution", resRow.transform);
        var resDropGO = new GameObject("Dropdown_Resolution", typeof(RectTransform));
        resDropGO.transform.SetParent(resRow.transform, false);
        var resDrop = resDropGO.AddComponent<Dropdown>();
        resDrop.options.Add(new Dropdown.OptionData("1920x1080"));
        resDrop.options.Add(new Dropdown.OptionData("1280x720"));
        resDrop.options.Add(new Dropdown.OptionData("800x600"));
        var rrt = resDropGO.GetComponent<RectTransform>();
        rrt.anchorMin = new Vector2(0.5f,0.5f);
        rrt.anchorMax = new Vector2(1f,0.5f);
        rrt.anchoredPosition = new Vector2(-10f,0f);
        rrt.sizeDelta = new Vector2(150f,36f);

        // Fullscreen toggle
        CreateToggleRow(videoPanel.transform, "Fullscreen", "FullscreenToggle");
        // VSync / Brightness / MotionBlur UI placeholders
        CreateToggleRow(videoPanel.transform, "VSync", "VSync");
        CreateSliderRow(videoPanel.transform, "Brightness", "Brightness");
        CreateToggleRow(videoPanel.transform, "MotionBlur", "MotionBlur");

        // Quality dropdown
        var qRow = CreateUI("Row_Quality", videoPanel.transform, new Vector2(320,48));
        CreateText("Quality", qRow.transform);
        var qDropGO = new GameObject("Dropdown_Quality", typeof(RectTransform));
        qDropGO.transform.SetParent(qRow.transform, false);
        var qDrop = qDropGO.AddComponent<Dropdown>();
        qDrop.options.Add(new Dropdown.OptionData("Low"));
        qDrop.options.Add(new Dropdown.OptionData("Medium"));
        qDrop.options.Add(new Dropdown.OptionData("High"));
        var qrt = qDropGO.GetComponent<RectTransform>();
        qrt.anchorMin = new Vector2(0.5f,0.5f);
        qrt.anchorMax = new Vector2(1f,0.5f);
        qrt.anchoredPosition = new Vector2(-10f,0f);
        qrt.sizeDelta = new Vector2(150f,36f);

        // Bottom Buttons: Save / Cancel
        var bottom = CreateUI("Panel_Bottom", canvasGO.transform, new Vector2(900,72));
        var brt = bottom.GetComponent<RectTransform>();
        brt.anchorMin = new Vector2(0.5f,0f);
        brt.anchorMax = new Vector2(0.5f,0f);
        brt.pivot = new Vector2(0.5f,0f);
        brt.anchoredPosition = new Vector2(0f,24f);
        var saveBtn = CreateButton("Button_Save", bottom.transform, "SAVE");
        var cancelBtn = CreateButton("Button_Cancel", bottom.transform, "CANCEL");
        saveBtn.GetComponent<RectTransform>().anchoredPosition = new Vector2(-120,0);
        cancelBtn.GetComponent<RectTransform>().anchoredPosition = new Vector2(120,0);

        // Add OptionsManagerAdvanced and wire references where possible
        OptionsManagerAdvanced om = null;
        try { om = root.AddComponent<OptionsManagerAdvanced>(); } catch {}

        if (om != null)
        {
            om.masterSlider = GameObject.Find("Slider_Master")?.GetComponent<Slider>();
            om.musicSlider = GameObject.Find("Slider_Music")?.GetComponent<Slider>();
            om.sfxSlider = GameObject.Find("Slider_SFX")?.GetComponent<Slider>();
            om.voiceSlider = GameObject.Find("Slider_Voice")?.GetComponent<Slider>();

            om.resolutionDropdown = GameObject.Find("Dropdown_Resolution")?.GetComponent<Dropdown>();
            om.fullscreenToggle = GameObject.Find("Toggle_Fullscreen")?.GetComponent<Toggle>();
            om.vSyncToggle = GameObject.Find("Toggle_VSync")?.GetComponent<Toggle>();
            om.brightnessSlider = GameObject.Find("Slider_Brightness")?.GetComponent<Slider>();
            om.motionBlurToggle = GameObject.Find("Toggle_MotionBlur")?.GetComponent<Toggle>();
            om.qualityDropdown = GameObject.Find("Dropdown_Quality")?.GetComponent<Dropdown>();

            // auto-collect keybind buttons named "Button_Key_*"
            var allButtons = Object.FindObjectsOfType<Button>();
            foreach (var b in allButtons)
            {
                if (b.gameObject.name.StartsWith("Button_Key_"))
                    om.keybindButtons.Add(b);
            }

            Debug.Log("[OptionsSceneSetup] OptionsManagerAdvanced added and some fields auto-wired.");
            UnityEventTools.AddPersistentListener(saveBtn.onClick, om.SaveAll);
            UnityEventTools.AddPersistentListener(cancelBtn.onClick, om.LoadAll);
            EditorUtility.SetDirty(om);
        }

        // Save scene
        string path = "Assets/Scenes/OptionsScene.unity";
        var activeScene = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(activeScene);
        if (!EditorSceneManager.SaveScene(activeScene, path))
            Debug.LogError("[OptionsSceneSetup] 씬 저장 실패: " + path);
        else
        {
            AssetDatabase.Refresh();
            Debug.Log("[OptionsSceneSetup] 씬 생성 및 저장 완료: " + path);
        }

        Selection.activeGameObject = root;
    }

    static GameObject CreateUIFull(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.sizeDelta = Vector2.zero;
        rt.anchoredPosition = Vector2.zero;
        return go;
    }

    static GameObject CreateUI(string name, Transform parent, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = size;
        rt.anchoredPosition = Vector2.zero;
        return go;
    }

    static void AddPanelLabel(Transform parent, string label)
    {
        var lab = CreateUI("Label_"+label, parent, new Vector2(320,40));
        var t = lab.AddComponent<Text>();
        t.text = label;
        t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        t.alignment = TextAnchor.UpperLeft;
        var rt = lab.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f,1f);
        rt.anchorMax = new Vector2(1f,1f);
        rt.pivot = new Vector2(0.5f,1f);
        rt.anchoredPosition = new Vector2(0f,-8f);
    }

    static void CreateSliderRow(Transform parent, string label, string name)
    {
        var row = CreateUI(name+"_Row", parent, new Vector2(360,48));
        var txt = CreateText(label, row.transform);
        var sliderGO = new GameObject("Slider_"+label, typeof(RectTransform));
        sliderGO.transform.SetParent(row.transform, false);
        var s = sliderGO.AddComponent<Slider>();
        var rt = sliderGO.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f,0.5f);
        rt.anchorMax = new Vector2(1f,0.5f);
        rt.sizeDelta = new Vector2(180,24);
    }

    static void CreateToggleRow(Transform parent, string label, string name)
    {
        var row = CreateUI(name+"_Row", parent, new Vector2(360,36));
        var txt = CreateText(label, row.transform);
        var togGO = new GameObject("Toggle_"+label, typeof(RectTransform));
        togGO.transform.SetParent(row.transform, false);
        var t = togGO.AddComponent<Toggle>();
        var rt = togGO.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.6f,0.5f);
        rt.anchorMax = new Vector2(1f,0.5f);
        rt.sizeDelta = new Vector2(20,20);
    }

    static void CreateKeybindRow(Transform parent, string label, string name)
    {
        var row = CreateUI(name+"_Row", parent, new Vector2(460,46));
        CreateText(label, row.transform);
        var btnGO = CreateButtonObj("Button_Key_"+label, row.transform, "Rebind");
        btnGO.name = "Button_"+name;
        btnGO.GetComponent<RectTransform>().anchorMin = new Vector2(0.6f,0.5f);
        btnGO.GetComponent<RectTransform>().anchorMax = new Vector2(1f,0.5f);
        btnGO.GetComponent<RectTransform>().sizeDelta = new Vector2(120,34);
    }

    static GameObject CreateButtonObj(string name, Transform parent, string label)
    {
        var btn = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        btn.transform.SetParent(parent, false);
        var textGO = new GameObject("Label", typeof(RectTransform));
        textGO.transform.SetParent(btn.transform, false);
        var text = textGO.AddComponent<Text>();
        text.text = label;
        text.alignment = TextAnchor.MiddleCenter;
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.resizeTextForBestFit = true;
        var rt = btn.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(200,40);
        return btn;
    }

    static Button CreateButton(string name, Transform parent, string label)
    {
        var btnGO = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        btnGO.transform.SetParent(parent, false);
        var rt = btnGO.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(160,48);
        var textGO = new GameObject("Label", typeof(RectTransform));
        textGO.transform.SetParent(btnGO.transform, false);
        var text = textGO.AddComponent<Text>();
        text.text = label;
        text.alignment = TextAnchor.MiddleCenter;
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        return btnGO.GetComponent<Button>();
    }

    static Text CreateText(string content, Transform parent)
    {
        var go = new GameObject("Text", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<Text>();
        t.text = content;
        t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        t.alignment = TextAnchor.MiddleLeft;
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f,0.5f);
        rt.anchorMax = new Vector2(0.6f,0.5f);
        rt.sizeDelta = new Vector2(180,30);
        return t;
    }
}
