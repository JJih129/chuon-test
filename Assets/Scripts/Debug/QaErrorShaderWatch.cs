#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 반복 15 (개발 전용): '에러 셰이더(단색 자홍)'로 그려지는 렌더러 감시. PV 테이크에서 Type-D 가 페이즈 3 이후 자홍으로
/// 남은 버그(BossDamageCracks 가 파괴된 브레이크 홀로그램 재질을 다시 붙임 → null 슬롯)의 재발 감지용.
/// 스스로 실행되지 않는다 — QaCommandLine 이 QA 실행에서 Ensure() 를 부르거나 Play 모드에서 MCP 로 호출.
/// 씬 로드 때 렌더러 목록을 모으고(보스·플레이어는 1 s 마다 다시 모음) 프레임당 최대 48 개만 검사 → 히치 없음.
/// 검사: 활성 Mesh/SkinnedMesh 렌더러의 null 재질 슬롯, Hidden/InternalErrorShader, 지원되지 않는 셰이더.
/// 렌더러마다 처음 한 번 "[QA-SHADER] ERROR-SHADER ..." 경고, Report() 로 합계.
/// </summary>
public sealed class QaErrorShaderWatch : MonoBehaviour
{
    const int PerFrame = 48;
    static QaErrorShaderWatch s_instance;

    readonly List<Renderer> _renderers = new List<Renderer>(4096);
    readonly HashSet<Renderer> _known = new HashSet<Renderer>();
    readonly HashSet<int> _reported = new HashSet<int>();
    readonly List<Material> _mats = new List<Material>(8);
    readonly List<string> _hits = new List<string>();
    int _cursor;
    float _nextActorScan;
    long _checks;
    BossController _boss;

    public static int HitCount => s_instance != null ? s_instance._reported.Count : 0;

    public static void Ensure()
    {
        if (s_instance != null)
            return;
        var go = new GameObject("__QaErrorShaderWatch");
        DontDestroyOnLoad(go);
        s_instance = go.AddComponent<QaErrorShaderWatch>();
        SceneManager.sceneLoaded += s_instance.HandleSceneLoaded;
        s_instance.CollectAll();
    }

    public static string Report()
    {
        if (s_instance == null)
            return "[QA-SHADER] watch not running";
        return $"[QA-SHADER] error-shader renderers={s_instance._reported.Count} checks={s_instance._checks}" +
               (s_instance._hits.Count > 0 ? "\n  " + string.Join("\n  ", s_instance._hits) : "");
    }

    void OnDestroy()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        if (s_instance == this)
            s_instance = null;
    }

    void HandleSceneLoaded(Scene scene, LoadSceneMode mode) => CollectAll();

    void CollectAll()
    {
        _renderers.Clear();
        _known.Clear();
        _cursor = 0;
        _boss = null;
        foreach (Renderer r in FindObjectsOfType<Renderer>(true))
            Add(r);
    }

    void Add(Renderer r)
    {
        if (r == null || !(r is MeshRenderer || r is SkinnedMeshRenderer) || !_known.Add(r))
            return;
        _renderers.Add(r);
    }

    void Update()
    {
        // 보스·플레이어 계층은 런타임에 파츠·오버레이가 붙으므로 주기적으로 다시 모은다.
        if (Time.unscaledTime >= _nextActorScan)
        {
            _nextActorScan = Time.unscaledTime + 1f;
            if (_boss == null)
                _boss = FindObjectOfType<BossController>();
            if (_boss != null)
                foreach (Renderer r in _boss.transform.root.GetComponentsInChildren<Renderer>(true))
                    Add(r);
        }

        int n = Mathf.Min(PerFrame, _renderers.Count);
        for (int i = 0; i < n; i++)
        {
            if (_cursor >= _renderers.Count)
                _cursor = 0;
            Renderer r = _renderers[_cursor];
            if (r == null)
            {
                _renderers.RemoveAt(_cursor);
                continue;
            }
            _cursor++;
            Check(r);
        }
    }

    void Check(Renderer r)
    {
        if (!r.enabled || !r.gameObject.activeInHierarchy)
            return;
        _checks++;
        r.GetSharedMaterials(_mats);
        for (int m = 0; m < _mats.Count; m++)
        {
            Material mat = _mats[m];
            string why = null;
            if (mat == null)
                why = "null material slot " + m;
            else if (mat.shader == null || mat.shader.name == "Hidden/InternalErrorShader")
                why = $"error shader on '{mat.name}' slot {m}";
            else if (!mat.shader.isSupported)
                why = $"unsupported shader '{mat.shader.name}' ('{mat.name}') slot {m}";
            if (why == null)
                continue;
            if (_reported.Add(r.GetInstanceID()))
            {
                string line = $"{SceneManager.GetActiveScene().name}: {GetPath(r.transform)} — {why}";
                _hits.Add(line);
                Debug.LogWarning("[QA-SHADER] ERROR-SHADER " + line, r);
            }
            return;
        }
    }

    static string GetPath(Transform t)
    {
        string p = t.name;
        for (int i = 0; i < 3 && t.parent != null; i++)
        {
            t = t.parent;
            p = t.name + "/" + p;
        }
        return p;
    }
}
#endif
