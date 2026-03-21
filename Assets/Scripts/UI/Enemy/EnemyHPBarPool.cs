// EnemyHPBarPool.cs
using System.Collections;
using UnityEngine;
using System.Collections.Generic;

public class EnemyHPBarPool : MonoBehaviour
{
    public EnemyHPBar prefab;
    public int initialSize = 6;
    public int prewarmPerFrame = 2;
    public Transform parent; // optional, 자동 할당 지원

    Queue<EnemyHPBar> _pool = new();

    void Awake()
    {
        // 자동 부모 할당
        if (parent == null)
        {
            if (prefab != null && prefab.GetComponent<RectTransform>() != null)
            {
                // UI 프리팹이면 Canvas 찾아서 부모로 사용
                var c = FindObjectOfType<Canvas>();
                parent = c != null ? c.transform : null;
            }

            if (parent == null)
            {
                // World-space 용 컨테이너 생성
                var go = GameObject.Find("EnemyHPBar_WorldContainer");
                if (go == null) go = new GameObject("EnemyHPBar_WorldContainer");
                parent = go.transform;
            }
        }

    }

    IEnumerator Start()
    {
        int targetCount = Mathf.Max(0, initialSize);
        int batchSize = Mathf.Max(1, prewarmPerFrame);

        for (int i = 0; i < targetCount; i++)
        {
            var e = CreateInstance();
            e.gameObject.SetActive(false);
            _pool.Enqueue(e);

            if ((i + 1) % batchSize == 0 && i + 1 < targetCount)
                yield return null;
        }
    }

    EnemyHPBar CreateInstance()
    {
        var inst = Instantiate(prefab);
        // 부모가 RectTransform이면 worldPositionStays = false (UI)
        bool isUI = inst.GetComponent<RectTransform>() != null && parent != null && parent.GetComponent<RectTransform>() != null;
        inst.transform.SetParent(parent, !isUI); // UI: false(로컬 좌표), World: true(월드 유지)
        return inst;
    }

    public EnemyHPBar Get()
    {
        if (_pool.Count == 0) return CreateInstance();
        var e = _pool.Dequeue();
        e.gameObject.SetActive(true);
        return e;
    }

    public void Return(EnemyHPBar bar)
    {
        if (bar == null) return;
        bar.gameObject.SetActive(false);
        _pool.Enqueue(bar);
    }
}
