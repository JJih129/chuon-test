// Assets/Editor/TitleSceneSetupEditor.cs
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.UI;

public static class TitleSceneSetupEditor
{
    [MenuItem("Tools/추온/Create Title Scene (Create & Save)")]
    public static void CreateTitleSceneHierarchy()
    {
        Directory.CreateDirectory("Assets/Scenes");
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var root = new GameObject("TitleScene_Root");

        var canvasGO = new GameObject("UI Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGO.transform.SetParent(root.transform, false);
        var canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasGO.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        var panelBack = CreateUIFull("Panel_Back", canvasGO.transform);
        panelBack.AddComponent<CanvasRenderer>();
        var imgBack = panelBack.AddComponent<Image>();
        imgBack.color = Color.black;

        var logo = CreateUI("Image_Logo", canvasGO.transform, new Vector2(600, 300));
        var logoImage = logo.AddComponent<Image>();
        var logoAnimator = logo.AddComponent<Animator>();

        var versionGO = new GameObject("TMP_Text_Version", typeof(RectTransform));
        versionGO.transform.SetParent(canvasGO.transform, false);
        var rtV = versionGO.GetComponent<RectTransform>();
        rtV.anchorMin = new Vector2(0f, 0f);
        rtV.anchorMax = new Vector2(0f, 0f);
        rtV.anchoredPosition = new Vector2(10f, 10f);
        rtV.sizeDelta = new Vector2(300f, 40f);
        var versionText = versionGO.AddComponent<UnityEngine.UI.Text>();
        versionText.text = "v" + Application.version;
        versionText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        versionText.alignment = TextAnchor.LowerLeft;

        var panelMenu = CreateUI("Panel_Menu", canvasGO.transform, new Vector2(420, 400));
        var layout = panelMenu.AddComponent<VerticalLayoutGroup>();
        layout.childControlHeight = false;
        layout.childControlWidth = false;
        layout.spacing = 8f;
        var contentSize = panelMenu.AddComponent<ContentSizeFitter>();
        contentSize.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        var menuRT = panelMenu.GetComponent<RectTransform>();
        menuRT.anchoredPosition = new Vector2(0f, -100f);

        var startBtn = CreateButton("Button_Start", panelMenu.transform, "START");
        var optionsBtn = CreateButton("Button_Options", panelMenu.transform, "OPTIONS");
        var creditsBtn = CreateButton("Button_Credits", panelMenu.transform, "CREDITS");
        var quitBtn = CreateButton("Button_Quit", panelMenu.transform, "QUIT");

        var audioGO = new GameObject("AudioSource_BGM");
        audioGO.transform.SetParent(root.transform, false);
        var audio = audioGO.AddComponent<AudioSource>();
        audio.playOnAwake = false;

        var fadeGO = CreateUIFull("FadeCanvas", canvasGO.transform);
        var cg = fadeGO.AddComponent<CanvasGroup>();
        cg.alpha = 0f;
        cg.blocksRaycasts = false; // 초기에는 클릭 허용
        var fadeImg = fadeGO.AddComponent<Image>();
        fadeImg.color = new Color(0f, 0f, 0f, 1f);
        fadeImg.raycastTarget = false; // 이미지 자체가 레이캐스트를 막지 않게

        // TitleManager 자동 추가 및 Persistent Listener 연결
        TitleManager tm = null;
        try
        {
            tm = root.AddComponent<TitleManager>();
        }
        catch { tm = root.GetComponent<TitleManager>(); }

        if (tm != null)
        {
            tm.startButton = startBtn;
            tm.optionsButton = optionsBtn;
            tm.creditsButton = creditsBtn;
            tm.quitButton = quitBtn;
            tm.logoAnimator = logoAnimator;
            tm.fadeCanvasGroup = cg;
            tm.bgmSource = audio;
            tm.versionText = versionText;

            UnityEventTools.AddPersistentListener(startBtn.onClick, tm.OnStartPressed);
            UnityEventTools.AddPersistentListener(optionsBtn.onClick, tm.OnOptionsPressed);
            UnityEventTools.AddPersistentListener(creditsBtn.onClick, tm.OnCreditsPressed);
            UnityEventTools.AddPersistentListener(quitBtn.onClick, tm.OnQuitPressed);

            EditorUtility.SetDirty(startBtn);
            EditorUtility.SetDirty(optionsBtn);
            EditorUtility.SetDirty(creditsBtn);
            EditorUtility.SetDirty(quitBtn);
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        }
        else
        {
            Debug.Log("[TitleSceneSetup] TitleManager 타입을 찾지 못했습니다. TitleManager.cs 저장 후 다시 실행하면 자동 연결됩니다.");
        }

        string path = "Assets/Scenes/TitleScene.unity";
        var activeScene = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(activeScene);
        if (!EditorSceneManager.SaveScene(activeScene, path))
        {
            Debug.LogError("[TitleSceneSetup] 씬 저장 실패: " + path);
        }
        else
        {
            AssetDatabase.Refresh();
            Debug.Log("[TitleSceneSetup] 씬 생성 및 저장 완료: " + path);
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

    static Button CreateButton(string name, Transform parent, string label)
    {
        var btnGO = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        btnGO.transform.SetParent(parent, false);
        var rt = btnGO.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(380, 70);

        var img = btnGO.GetComponent<Image>();
        img.color = new Color(1f, 1f, 1f, 0.9f);

        var labelGO = new GameObject("Label", typeof(RectTransform));
        labelGO.transform.SetParent(btnGO.transform, false);
        var lrt = labelGO.GetComponent<RectTransform>();
        lrt.anchorMin = Vector2.zero;
        lrt.anchorMax = Vector2.one;
        lrt.sizeDelta = Vector2.zero;

        var text = labelGO.AddComponent<UnityEngine.UI.Text>();
        text.text = label;
        text.alignment = TextAnchor.MiddleCenter;
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.resizeTextForBestFit = true;

        return btnGO.GetComponent<Button>();
    }
}
