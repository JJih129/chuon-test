#if UNITY_EDITOR
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class FastMCPUnityBridge
{
    private const string Host = "127.0.0.1";
    private const int Port = 18777;
    private const string SceneFolder = "Assets/Scenes";
    private const string PrefabFolder = "Assets/Prefabs/Generated";

    private static readonly ConcurrentQueue<WorkItem> Queue = new ConcurrentQueue<WorkItem>();
    private static readonly CancellationTokenSource Cancellation = new CancellationTokenSource();

    private static TcpListener _listener;
    private static Thread _thread;
    private static bool _initialized;

    static FastMCPUnityBridge()
    {
        Initialize();
    }

    private sealed class WorkItem
    {
        public Request request;
        public Action<Response> reply;
    }

    [Serializable]
    private sealed class Request
    {
        public string command;
        public string[] args;
    }

    [Serializable]
    private sealed class Response
    {
        public bool success;
        public string message;
        public string dataJson;
    }

    [Serializable]
    private sealed class HealthData
    {
        public string unityVersion;
        public string projectPath;
        public string activeScene;
        public bool isPlaying;
        public bool isCompiling;
        public bool isUpdating;
    }

    [Serializable]
    private sealed class StringArrayPayload
    {
        public string[] items;
    }

    [Serializable]
    private sealed class SceneGraphRoot
    {
        public string sceneName;
        public string scenePath;
        public List<SceneGraphNode> roots = new List<SceneGraphNode>();
    }

    [Serializable]
    private sealed class SceneGraphNode
    {
        public string name;
        public string path;
        public bool activeSelf;
        public string tag;
        public int layer;
        public string[] components;
        public Vector3 localPosition;
        public Vector3 localScale;
        public Quaternion localRotation;
        public List<SceneGraphNode> children = new List<SceneGraphNode>();
    }

    private static void Initialize()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        EditorApplication.update -= ProcessQueue;
        EditorApplication.update += ProcessQueue;
        AssemblyReloadEvents.beforeAssemblyReload -= Shutdown;
        AssemblyReloadEvents.beforeAssemblyReload += Shutdown;
        EditorApplication.quitting -= Shutdown;
        EditorApplication.quitting += Shutdown;
        StartServer();
    }

    private static void StartServer()
    {
        try
        {
            _listener = new TcpListener(IPAddress.Parse(Host), Port);
            _listener.Start();
            _thread = new Thread(ServerLoop);
            _thread.IsBackground = true;
            _thread.Name = "FastMCPUnityBridge";
            _thread.Start();
            Debug.Log(string.Format("[FastMCPBridge] Listening on {0}:{1}", Host, Port));
        }
        catch (Exception ex)
        {
            Debug.LogError("[FastMCPBridge] Failed to start: " + ex);
        }
    }

    private static void ServerLoop()
    {
        while (!Cancellation.IsCancellationRequested)
        {
            try
            {
                TcpClient client = _listener.AcceptTcpClient();
                ThreadPool.QueueUserWorkItem(delegate { HandleClient(client); });
            }
            catch (SocketException)
            {
                if (Cancellation.IsCancellationRequested)
                {
                    return;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("[FastMCPBridge] Server loop error: " + ex);
            }
        }
    }

    private static void HandleClient(TcpClient client)
    {
        using (client)
        using (NetworkStream stream = client.GetStream())
        using (StreamReader reader = new StreamReader(stream, Encoding.UTF8, false, 1024, true))
        using (StreamWriter writer = new StreamWriter(stream, new UTF8Encoding(false), 1024, true))
        using (ManualResetEventSlim signal = new ManualResetEventSlim(false))
        {
            writer.AutoFlush = true;
            Response response = null;

            try
            {
                string line = reader.ReadLine();
                Request request = string.IsNullOrWhiteSpace(line) ? null : JsonUtility.FromJson<Request>(line);
                if (request == null || string.IsNullOrWhiteSpace(request.command))
                {
                    Write(writer, Fail("Invalid request"));
                    return;
                }

                Queue.Enqueue(new WorkItem
                {
                    request = request,
                    reply = delegate(Response value)
                    {
                        response = value;
                        signal.Set();
                    }
                });

                if (!signal.Wait(TimeSpan.FromSeconds(20)))
                {
                    Write(writer, Fail("Timeout waiting for Unity main thread"));
                    return;
                }

                Write(writer, response ?? Fail("No response"));
            }
            catch (Exception ex)
            {
                Write(writer, Fail(ex.ToString()));
            }
        }
    }

    private static void ProcessQueue()
    {
        int processed = 0;
        while (processed < 16 && Queue.TryDequeue(out WorkItem item))
        {
            processed++;
            Response response;
            try
            {
                response = Execute(item.request);
            }
            catch (Exception ex)
            {
                response = Fail(ex.ToString());
            }

            if (item.reply != null)
            {
                item.reply(response);
            }
        }
    }

    internal static bool ExecuteEditorCommand(string command, string[] args, out string message, out string dataJson)
    {
        Response response = Execute(new Request { command = command, args = args ?? Array.Empty<string>() });
        message = response.message ?? string.Empty;
        dataJson = response.dataJson ?? string.Empty;
        return response.success;
    }

    private static Response Execute(Request request)
    {
        string[] args = request.args ?? Array.Empty<string>();
        switch ((request.command ?? string.Empty).Trim())
        {
            case "ping": return Ok("pong");
            case "health_check": return Health();
            case "open_scene": return OpenScene(args);
            case "create_scene": return CreateScene(args);
            case "save_scene": return SaveScene();
            case "get_scene_graph":
            case "get_scene_graph_json": return GetSceneGraph();
            case "create_gameobject": return CreateGameObject(args);
            case "delete_gameobject": return DeleteGameObject(args);
            case "set_parent": return SetParent(args);
            case "set_position": return SetPosition(args);
            case "set_local_scale": return SetLocalScale(args);
            case "add_component": return AddComponent(args);
            case "remove_component": return RemoveComponent(args);
            case "create_prefab": return CreatePrefab(args);
            case "instantiate_prefab": return InstantiatePrefab(args);
            case "search_assets": return SearchAssets(args);
            case "set_inspector_value": return SetInspectorValue(args);
            case "refresh_assets": return RefreshAssets();
            case "enter_playmode": return EnterPlayMode();
            case "exit_playmode": return ExitPlayMode();
            case "create_basic_3d_player_rig": return CreateBasic3DPlayerRig(args);
            default: return Fail("Unknown command: " + request.command);
        }
    }

    private static Response Health()
    {
        return Ok(
            "Unity editor bridge alive",
            JsonUtility.ToJson(
                new HealthData
                {
                    unityVersion = Application.unityVersion,
                    projectPath = Directory.GetCurrentDirectory(),
                    activeScene = SceneManager.GetActiveScene().path,
                    isPlaying = EditorApplication.isPlaying,
                    isCompiling = EditorApplication.isCompiling,
                    isUpdating = EditorApplication.isUpdating
                },
                true));
    }

    private static Response OpenScene(string[] args)
    {
        Require(args, 1, "open_scene requires scene name");
        string path = FindScenePath(args[0]);
        if (string.IsNullOrWhiteSpace(path))
        {
            return Fail("Scene not found: " + args[0]);
        }

        EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
        return Ok("Scene opened: " + path);
    }

    private static Response CreateScene(string[] args)
    {
        Require(args, 1, "create_scene requires scene name");
        EnsureFolder(SceneFolder);
        string path = SceneFolder + "/" + SafeName(args[0], "GeneratedScene") + ".unity";
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        if (!EditorSceneManager.SaveScene(scene, path))
        {
            return Fail("Failed to save scene: " + path);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        return Ok("Scene created: " + path);
    }

    private static Response SaveScene()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid())
        {
            return Fail("Active scene is invalid");
        }

        return EditorSceneManager.SaveScene(scene) ? Ok("Scene saved: " + scene.path) : Fail("Failed to save scene: " + scene.path);
    }

    private static Response GetSceneGraph()
    {
        if (!SceneManager.GetActiveScene().IsValid())
        {
            return Fail("Active scene is invalid");
        }

        SceneGraphRoot graph = BuildSceneGraph();
        return Ok("Scene graph collected", JsonUtility.ToJson(graph, true));
    }

    private static SceneGraphRoot BuildSceneGraph()
    {
        Scene scene = SceneManager.GetActiveScene();
        SceneGraphRoot root = new SceneGraphRoot();
        root.sceneName = scene.name;
        root.scenePath = scene.path;

        GameObject[] objects = scene.GetRootGameObjects();
        for (int i = 0; i < objects.Length; i++)
        {
            root.roots.Add(BuildNode(objects[i].transform));
        }

        return root;
    }

    private static SceneGraphNode BuildNode(Transform transform)
    {
        Component[] components = transform.GetComponents<Component>();
        List<string> names = new List<string>(components.Length);
        for (int i = 0; i < components.Length; i++)
        {
            if (components[i] != null)
            {
                names.Add(components[i].GetType().Name);
            }
        }

        SceneGraphNode node = new SceneGraphNode();
        node.name = transform.name;
        node.path = HierarchyPath(transform);
        node.activeSelf = transform.gameObject.activeSelf;
        node.tag = transform.gameObject.tag;
        node.layer = transform.gameObject.layer;
        node.components = names.ToArray();
        node.localPosition = transform.localPosition;
        node.localScale = transform.localScale;
        node.localRotation = transform.localRotation;

        for (int i = 0; i < transform.childCount; i++)
        {
            node.children.Add(BuildNode(transform.GetChild(i)));
        }

        return node;
    }

    private static Response CreateGameObject(string[] args)
    {
        Require(args, 1, "create_gameobject requires object name");
        GameObject go = new GameObject(args[0]);
        Undo.RegisterCreatedObjectUndo(go, "Create " + args[0]);
        Selection.activeGameObject = go;
        MarkDirty(go.scene);
        return Ok("GameObject created: " + HierarchyPath(go.transform));
    }

    private static Response DeleteGameObject(string[] args)
    {
        Require(args, 1, "delete_gameobject requires object name");
        GameObject go = FindGameObjectStrict(args[0]);
        string path = HierarchyPath(go.transform);
        Undo.DestroyObjectImmediate(go);
        MarkDirty(SceneManager.GetActiveScene());
        return Ok("GameObject deleted: " + path);
    }

    private static Response SetParent(string[] args)
    {
        Require(args, 2, "set_parent requires child and parent");
        GameObject child = FindGameObjectStrict(args[0]);
        GameObject parent = FindGameObjectStrict(args[1]);
        Undo.SetTransformParent(child.transform, parent.transform, "Set Parent");
        MarkDirty(child.scene);
        return Ok("Parent set: " + HierarchyPath(child.transform) + " -> " + HierarchyPath(parent.transform));
    }

    private static Response SetPosition(string[] args)
    {
        Require(args, 4, "set_position requires object name and xyz");
        GameObject go = FindGameObjectStrict(args[0]);
        Undo.RecordObject(go.transform, "Set Position");
        go.transform.position = new Vector3(ParseFloat(args[1]), ParseFloat(args[2]), ParseFloat(args[3]));
        EditorUtility.SetDirty(go.transform);
        MarkDirty(go.scene);
        return Ok("Position set: " + HierarchyPath(go.transform));
    }

    private static Response SetLocalScale(string[] args)
    {
        Require(args, 4, "set_local_scale requires object name and xyz");
        GameObject go = FindGameObjectStrict(args[0]);
        Undo.RecordObject(go.transform, "Set Local Scale");
        go.transform.localScale = new Vector3(ParseFloat(args[1]), ParseFloat(args[2]), ParseFloat(args[3]));
        EditorUtility.SetDirty(go.transform);
        MarkDirty(go.scene);
        return Ok("Local scale set: " + HierarchyPath(go.transform));
    }

    private static Response AddComponent(string[] args)
    {
        Require(args, 2, "add_component requires object name and component name");
        GameObject go = FindGameObjectStrict(args[0]);
        Type type = FindType(args[1]);
        if (type == null || !typeof(Component).IsAssignableFrom(type))
        {
            return Fail("Component type not found: " + args[1]);
        }

        if (go.GetComponent(type) != null)
        {
            return Ok("Component already exists: " + type.Name);
        }

        Undo.AddComponent(go, type);
        MarkDirty(go.scene);
        return Ok("Component added: " + type.Name);
    }

    private static Response RemoveComponent(string[] args)
    {
        Require(args, 2, "remove_component requires object name and component name");
        GameObject go = FindGameObjectStrict(args[0]);
        Type type = FindType(args[1]);
        if (type == null)
        {
            return Fail("Component type not found: " + args[1]);
        }

        Component component = go.GetComponent(type);
        if (component == null)
        {
            return Fail("Component not found on object: " + args[1]);
        }

        Undo.DestroyObjectImmediate(component);
        MarkDirty(go.scene);
        return Ok("Component removed: " + type.Name);
    }

    private static Response CreatePrefab(string[] args)
    {
        Require(args, 2, "create_prefab requires object name and prefab name");
        EnsureFolder(PrefabFolder);
        GameObject go = FindGameObjectStrict(args[0]);
        string path = PrefabFolder + "/" + SafeName(args[1], "GeneratedPrefab") + ".prefab";
        GameObject prefab = PrefabUtility.SaveAsPrefabAssetAndConnect(go, path, InteractionMode.AutomatedAction);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        return prefab != null ? Ok("Prefab created: " + path) : Fail("Failed to create prefab: " + path);
    }

    private static Response InstantiatePrefab(string[] args)
    {
        Require(args, 1, "instantiate_prefab requires prefab name");
        string path = FindPrefabPath(args[0]);
        if (string.IsNullOrWhiteSpace(path))
        {
            return Fail("Prefab not found: " + args[0]);
        }

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab == null)
        {
            return Fail("Failed to load prefab asset: " + path);
        }

        GameObject instance = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
        if (instance == null)
        {
            return Fail("Failed to instantiate prefab: " + path);
        }

        Undo.RegisterCreatedObjectUndo(instance, "Instantiate " + prefab.name);
        MarkDirty(instance.scene);
        return Ok("Prefab instantiated: " + path);
    }

    private static Response SearchAssets(string[] args)
    {
        Require(args, 1, "search_assets requires filter name");
        string searchType = args.Length > 1 ? args[1] : "t:Prefab";
        string query = string.IsNullOrWhiteSpace(searchType) ? args[0] : (args[0] + " " + searchType).Trim();
        string[] guids = AssetDatabase.FindAssets(query);
        string[] paths = new string[guids.Length];
        for (int i = 0; i < guids.Length; i++)
        {
            paths[i] = AssetDatabase.GUIDToAssetPath(guids[i]);
        }

        Array.Sort(paths, StringComparer.OrdinalIgnoreCase);
        return Ok("Assets found: " + paths.Length, JsonUtility.ToJson(new StringArrayPayload { items = paths }, true));
    }

    private static Response SetInspectorValue(string[] args)
    {
        Require(args, 4, "set_inspector_value requires object, component, field and value");
        GameObject go = FindGameObjectStrict(args[0]);
        Type type = FindType(args[1]);
        if (type == null)
        {
            return Fail("Component type not found: " + args[1]);
        }

        Component component = go.GetComponent(type);
        if (component == null)
        {
            return Fail("Component not found on object: " + type.Name);
        }

        if (!TrySetMember(component, args[2], args[3]))
        {
            return Fail("Writable field or property not found: " + args[2]);
        }

        EditorUtility.SetDirty(component);
        MarkDirty(go.scene);
        return Ok("Inspector value set: " + type.Name + "." + args[2]);
    }

    private static Response RefreshAssets()
    {
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        return Ok("Assets refreshed");
    }

    private static Response EnterPlayMode()
    {
        if (EditorApplication.isPlaying)
        {
            return Ok("Already in Play Mode");
        }

        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            return Fail("Editor is busy compiling or updating");
        }

        EditorApplication.isPlaying = true;
        return Ok("Entered Play Mode");
    }

    private static Response ExitPlayMode()
    {
        if (!EditorApplication.isPlaying)
        {
            return Ok("Already in Edit Mode");
        }

        EditorApplication.isPlaying = false;
        return Ok("Exited Play Mode");
    }

    private static Response CreateBasic3DPlayerRig(string[] args)
    {
        string playerName = args.Length > 0 && !string.IsNullOrWhiteSpace(args[0]) ? args[0] : "Player";
        GameObject player = new GameObject(playerName);
        Undo.RegisterCreatedObjectUndo(player, "Create " + playerName);

        CharacterController controller = Undo.AddComponent<CharacterController>(player);
        controller.height = 1.8f;
        controller.radius = 0.35f;
        controller.center = new Vector3(0f, 0.9f, 0f);

        GameObject cameraObject = new GameObject(Camera.main == null ? "Main Camera" : playerName + " Camera");
        Undo.RegisterCreatedObjectUndo(cameraObject, "Create Camera");
        Undo.SetTransformParent(cameraObject.transform, player.transform, "Parent Camera");
        cameraObject.transform.localPosition = new Vector3(0f, 1.6f, -3.5f);
        cameraObject.transform.localRotation = Quaternion.identity;
        Undo.AddComponent<Camera>(cameraObject);

        if (Camera.main == null)
        {
            cameraObject.tag = "MainCamera";
        }

        if (UnityEngine.Object.FindObjectOfType<AudioListener>() == null)
        {
            Undo.AddComponent<AudioListener>(cameraObject);
        }

        Selection.activeGameObject = player;
        MarkDirty(player.scene);
        return Ok("Basic 3D player rig created: " + HierarchyPath(player.transform));
    }

    private static string FindScenePath(string sceneName)
    {
        string[] guids = AssetDatabase.FindAssets(sceneName + " t:Scene");
        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            if (string.Equals(Path.GetFileNameWithoutExtension(path), sceneName, StringComparison.OrdinalIgnoreCase))
            {
                return path;
            }
        }

        return string.Empty;
    }

    private static string FindPrefabPath(string prefabName)
    {
        string[] guids = AssetDatabase.FindAssets(prefabName + " t:Prefab");
        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            if (string.Equals(Path.GetFileNameWithoutExtension(path), prefabName, StringComparison.OrdinalIgnoreCase))
            {
                return path;
            }
        }

        return string.Empty;
    }

    private static GameObject FindGameObjectStrict(string objectName)
    {
        GameObject exact = GameObject.Find(objectName);
        if (exact != null)
        {
            return exact;
        }

        Scene scene = SceneManager.GetActiveScene();
        List<GameObject> matches = new List<GameObject>();
        GameObject[] roots = scene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
        {
            CollectByName(roots[i].transform, objectName, matches);
        }

        if (matches.Count == 1)
        {
            return matches[0];
        }

        throw new InvalidOperationException(matches.Count > 1 ? "Multiple objects found: " + objectName : "GameObject not found: " + objectName);
    }

    private static void CollectByName(Transform transform, string name, List<GameObject> output)
    {
        if (string.Equals(transform.name, name, StringComparison.Ordinal))
        {
            output.Add(transform.gameObject);
        }

        for (int i = 0; i < transform.childCount; i++)
        {
            CollectByName(transform.GetChild(i), name, output);
        }
    }

    private static string HierarchyPath(Transform transform)
    {
        Stack<string> names = new Stack<string>();
        while (transform != null)
        {
            names.Push(transform.name);
            transform = transform.parent;
        }

        return string.Join("/", names.ToArray());
    }

    private static Type FindType(string typeName)
    {
        Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
        for (int i = 0; i < assemblies.Length; i++)
        {
            Type[] types;
            try
            {
                types = assemblies[i].GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types;
            }

            for (int j = 0; j < types.Length; j++)
            {
                Type type = types[j];
                if (type != null && (type.Name == typeName || type.FullName == typeName))
                {
                    return type;
                }
            }
        }

        return null;
    }

    private static bool TrySetMember(Component component, string memberName, string value)
    {
        BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        FieldInfo field = component.GetType().GetField(memberName, flags);
        if (field != null)
        {
            Undo.RecordObject(component, "Set " + memberName);
            field.SetValue(component, ConvertValue(field.FieldType, value));
            return true;
        }

        PropertyInfo property = component.GetType().GetProperty(memberName, flags);
        if (property != null && property.CanWrite && property.GetIndexParameters().Length == 0)
        {
            Undo.RecordObject(component, "Set " + memberName);
            property.SetValue(component, ConvertValue(property.PropertyType, value), null);
            return true;
        }

        return false;
    }

    private static object ConvertValue(Type type, string value)
    {
        if (type == typeof(string)) return value;
        if (type == typeof(int)) return int.Parse(value, CultureInfo.InvariantCulture);
        if (type == typeof(float)) return float.Parse(value, CultureInfo.InvariantCulture);
        if (type == typeof(double)) return double.Parse(value, CultureInfo.InvariantCulture);
        if (type == typeof(bool)) return bool.Parse(value);
        if (type == typeof(long)) return long.Parse(value, CultureInfo.InvariantCulture);
        if (type == typeof(short)) return short.Parse(value, CultureInfo.InvariantCulture);
        if (type == typeof(byte)) return byte.Parse(value, CultureInfo.InvariantCulture);
        if (type == typeof(Vector2))
        {
            string[] parts = SplitValues(value, 2);
            return new Vector2(float.Parse(parts[0], CultureInfo.InvariantCulture), float.Parse(parts[1], CultureInfo.InvariantCulture));
        }
        if (type == typeof(Vector3))
        {
            string[] parts = SplitValues(value, 3);
            return new Vector3(float.Parse(parts[0], CultureInfo.InvariantCulture), float.Parse(parts[1], CultureInfo.InvariantCulture), float.Parse(parts[2], CultureInfo.InvariantCulture));
        }
        if (type == typeof(Vector4))
        {
            string[] parts = SplitValues(value, 4);
            return new Vector4(float.Parse(parts[0], CultureInfo.InvariantCulture), float.Parse(parts[1], CultureInfo.InvariantCulture), float.Parse(parts[2], CultureInfo.InvariantCulture), float.Parse(parts[3], CultureInfo.InvariantCulture));
        }
        if (type == typeof(Color))
        {
            string[] parts = SplitValues(value, 4);
            return new Color(float.Parse(parts[0], CultureInfo.InvariantCulture), float.Parse(parts[1], CultureInfo.InvariantCulture), float.Parse(parts[2], CultureInfo.InvariantCulture), float.Parse(parts[3], CultureInfo.InvariantCulture));
        }
        if (type.IsEnum) return Enum.Parse(type, value, true);
        throw new NotSupportedException("Unsupported inspector value type: " + type.FullName);
    }

    private static string[] SplitValues(string value, int expected)
    {
        string[] parts = value.Split(',');
        List<string> cleaned = new List<string>(parts.Length);
        for (int i = 0; i < parts.Length; i++)
        {
            string part = parts[i].Trim();
            if (!string.IsNullOrWhiteSpace(part))
            {
                cleaned.Add(part);
            }
        }

        if (cleaned.Count != expected)
        {
            throw new FormatException("Invalid vector value: " + value);
        }

        return cleaned.ToArray();
    }

    private static void Require(string[] args, int expected, string message)
    {
        if (args == null || args.Length < expected)
        {
            throw new ArgumentException(message);
        }
    }

    private static float ParseFloat(string value)
    {
        return float.Parse(value, CultureInfo.InvariantCulture);
    }

    private static string SafeName(string raw, string fallback)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return fallback;
        }

        char[] invalid = Path.GetInvalidFileNameChars();
        string output = raw.Trim();
        for (int i = 0; i < invalid.Length; i++)
        {
            output = output.Replace(invalid[i], '_');
        }

        return string.IsNullOrWhiteSpace(output) ? fallback : output;
    }

    private static void EnsureFolder(string assetFolderPath)
    {
        if (AssetDatabase.IsValidFolder(assetFolderPath))
        {
            return;
        }

        string parent = Path.GetDirectoryName(assetFolderPath.Replace("\\", "/"));
        string folderName = Path.GetFileName(assetFolderPath);
        if (string.IsNullOrWhiteSpace(parent) || string.IsNullOrWhiteSpace(folderName))
        {
            throw new InvalidOperationException("Invalid asset folder path: " + assetFolderPath);
        }

        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, folderName);
    }

    private static void MarkDirty(Scene scene)
    {
        if (scene.IsValid())
        {
            EditorSceneManager.MarkSceneDirty(scene);
        }
    }

    private static Response Ok(string message)
    {
        return new Response { success = true, message = message, dataJson = string.Empty };
    }

    private static Response Ok(string message, string dataJson)
    {
        return new Response { success = true, message = message, dataJson = dataJson ?? string.Empty };
    }

    private static Response Fail(string message)
    {
        return new Response { success = false, message = message, dataJson = string.Empty };
    }

    private static void Write(StreamWriter writer, Response response)
    {
        writer.WriteLine(JsonUtility.ToJson(response));
    }

    private static void Shutdown()
    {
        try
        {
            Cancellation.Cancel();
            if (_listener != null)
            {
                _listener.Stop();
            }
        }
        catch
        {
        }
    }
}
#endif
