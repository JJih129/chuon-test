using UnityEditor;
using UnityEngine;

public sealed class EffectPreviewWindow : EditorWindow
{
    const string PreviewRootName = "__EffectPreview";

    GameObject sourcePrefab;
    GameObject previewInstance;
    Vector3 spawnPosition;
    bool useSceneViewPivot = true;
    bool loop = true;
    bool isPlaying;
    double lastEditorTime;
    float playbackTime;
    float previewDuration = 2f;

    [MenuItem("Tools/VFX/Effect Preview Window")]
    public static void Open()
    {
        GetWindow<EffectPreviewWindow>("Effect Preview");
    }

    [MenuItem("Tools/VFX/Preview Selected Effect %#e")]
    public static void PreviewSelected()
    {
        var window = GetWindow<EffectPreviewWindow>("Effect Preview");
        window.sourcePrefab = Selection.activeGameObject;
        window.SpawnPreview();
        window.RestartPreview();
    }

    void OnEnable()
    {
        EditorApplication.update += TickPreview;
        Selection.selectionChanged += Repaint;
        lastEditorTime = EditorApplication.timeSinceStartup;
    }

    void OnDisable()
    {
        EditorApplication.update -= TickPreview;
        Selection.selectionChanged -= Repaint;
    }

    void OnGUI()
    {
        EditorGUILayout.LabelField("Effect Preview", EditorStyles.boldLabel);
        sourcePrefab = (GameObject)EditorGUILayout.ObjectField("Effect Prefab", sourcePrefab, typeof(GameObject), false);
        useSceneViewPivot = EditorGUILayout.Toggle("Use Scene View Pivot", useSceneViewPivot);
        using (new EditorGUI.DisabledScope(useSceneViewPivot))
            spawnPosition = EditorGUILayout.Vector3Field("Spawn Position", spawnPosition);
        loop = EditorGUILayout.Toggle("Loop Preview", loop);

        EditorGUILayout.Space(6f);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Use Selection"))
                sourcePrefab = Selection.activeGameObject;
            if (GUILayout.Button("Spawn"))
                SpawnPreview();
            if (GUILayout.Button("Clear"))
                ClearPreview();
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUI.DisabledScope(previewInstance == null))
            {
                if (GUILayout.Button("Play"))
                    PlayPreview();
                if (GUILayout.Button("Restart"))
                    RestartPreview();
                if (GUILayout.Button("Stop"))
                    StopPreview();
                if (GUILayout.Button("Frame"))
                    FramePreview();
            }
        }

        EditorGUILayout.Space(6f);
        EditorGUILayout.LabelField("Runtime", previewInstance != null ? previewInstance.name : "None");
        EditorGUILayout.LabelField("Time", $"{playbackTime:0.00}s / {previewDuration:0.00}s");

        if (previewInstance != null)
            EditorGUILayout.HelpBox("Preview object is temporary and marked DontSave. Clear it before committing scene edits.", MessageType.Info);
    }

    void SpawnPreview()
    {
        if (sourcePrefab == null)
        {
            Debug.LogWarning("[EffectPreview] Select an effect prefab or scene object first.");
            return;
        }

        ClearPreview();

        Object prefabRoot = PrefabUtility.GetCorrespondingObjectFromSource(sourcePrefab);
        previewInstance = prefabRoot != null
            ? (GameObject)PrefabUtility.InstantiatePrefab(prefabRoot)
            : Instantiate(sourcePrefab);

        previewInstance.name = $"{PreviewRootName}_{sourcePrefab.name}";
        previewInstance.hideFlags = HideFlags.DontSave;
        previewInstance.transform.position = ResolveSpawnPosition();
        previewInstance.transform.rotation = Quaternion.identity;
        previewInstance.transform.localScale = sourcePrefab.transform.localScale;

        foreach (Transform child in previewInstance.GetComponentsInChildren<Transform>(true))
            child.gameObject.hideFlags = HideFlags.DontSave;

        Selection.activeGameObject = previewInstance;
        CachePreviewDuration();
        StopPreview();
        FramePreview();
    }

    void PlayPreview()
    {
        if (previewInstance == null)
            return;

        isPlaying = true;
        lastEditorTime = EditorApplication.timeSinceStartup;
    }

    void RestartPreview()
    {
        if (previewInstance == null)
            SpawnPreview();
        if (previewInstance == null)
            return;

        playbackTime = 0f;
        SimulatePreview(0f);
        PlayPreview();
    }

    void StopPreview()
    {
        isPlaying = false;
        playbackTime = 0f;
        SimulatePreview(0f);
    }

    void ClearPreview()
    {
        isPlaying = false;
        playbackTime = 0f;

        if (previewInstance == null)
            return;

        DestroyImmediate(previewInstance);
        previewInstance = null;
    }

    void FramePreview()
    {
        if (previewInstance == null)
            return;

        Selection.activeGameObject = previewInstance;
        SceneView.lastActiveSceneView?.FrameSelected();
    }

    void TickPreview()
    {
        if (!isPlaying || previewInstance == null)
            return;

        double now = EditorApplication.timeSinceStartup;
        float deltaTime = Mathf.Clamp((float)(now - lastEditorTime), 0f, 0.1f);
        lastEditorTime = now;

        playbackTime += deltaTime;
        if (playbackTime > previewDuration)
        {
            if (loop)
                playbackTime %= Mathf.Max(0.01f, previewDuration);
            else
                isPlaying = false;
        }

        SimulatePreview(playbackTime);
        Repaint();
        SceneView.RepaintAll();
    }

    void SimulatePreview(float time)
    {
        if (previewInstance == null)
            return;

        ParticleSystem[] systems = previewInstance.GetComponentsInChildren<ParticleSystem>(true);
        for (int i = 0; i < systems.Length; i++)
        {
            ParticleSystem system = systems[i];
            system.gameObject.SetActive(true);
            system.Simulate(time, true, true, false);
        }
    }

    void CachePreviewDuration()
    {
        previewDuration = 0.5f;
        if (previewInstance == null)
            return;

        ParticleSystem[] systems = previewInstance.GetComponentsInChildren<ParticleSystem>(true);
        for (int i = 0; i < systems.Length; i++)
        {
            ParticleSystem.MainModule main = systems[i].main;
            float lifetime = main.startLifetime.constantMax;
            previewDuration = Mathf.Max(previewDuration, main.duration + lifetime);
        }
    }

    Vector3 ResolveSpawnPosition()
    {
        if (!useSceneViewPivot)
            return spawnPosition;

        SceneView sceneView = SceneView.lastActiveSceneView;
        return sceneView != null ? sceneView.pivot : Vector3.zero;
    }
}
