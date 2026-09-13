// EnemyHPBarPool.cs
using UnityEngine;
using System.Collections.Generic;

public class EnemyHPBarPool : MonoBehaviour
{
    const string WorldContainerName = "EnemyHPBar_WorldContainer";
    public EnemyHPBar prefab;
    public int initialSize = 6;
    public Transform parent; // optional, 자동 할당 지원

    Queue<EnemyHPBar> _pool = new();
    bool _prefabIsUI;
    bool _parentIsUI;

    void Awake()
    {
        _prefabIsUI = prefab != null && prefab.GetComponent<RectTransform>() != null;
        // 자동 부모 할당
        if (parent == null)
        {
            if (_prefabIsUI)
            {
                // UI 프리팹이면 Canvas 찾아서 부모로 사용
                var c = FindObjectOfType<Canvas>();
                parent = c != null ? c.transform : null;
            }

            if (parent == null)
            {
                // World-space 용 컨테이너 생성
                var go = GameObject.Find(WorldContainerName);
                if (go == null) go = new GameObject(WorldContainerName);
                parent = go.transform;
            }
        }

        _parentIsUI = parent != null && parent.GetComponent<RectTransform>() != null;

        for (int i = 0; i < initialSize; i++)
        {
            var e = CreateInstance();
            e.gameObject.SetActive(false);
            _pool.Enqueue(e);
        }
    }

    EnemyHPBar CreateInstance()
    {
        var inst = Instantiate(prefab);
        // 부모가 RectTransform이면 worldPositionStays = false (UI)
        bool isUI = _prefabIsUI && _parentIsUI;
        inst.transform.SetParent(parent, !isUI); // UI: false(로컬 좌표), World: true(월드 유지)
        return inst;
    }

    public EnemyHPBar Get()
    {
        if (_pool.Count == 0) return CreateInstance();
        var e = _pool.Dequeue();
        if (!e.gameObject.activeSelf)
            e.gameObject.SetActive(true);
        return e;
    }

    public void Return(EnemyHPBar bar)
    {
        if (bar == null) return;
        if (bar.gameObject.activeSelf)
            bar.gameObject.SetActive(false);
        _pool.Enqueue(bar);
    }
}
