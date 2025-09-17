using System.Collections.Generic;
using UnityEngine;

public class EnemyHPBarPool : MonoBehaviour
{
    public EnemyHPBar prefab;
    public int preload = 5;

    readonly Queue<EnemyHPBar> pool = new Queue<EnemyHPBar>();

    void Awake()
    {
        for (int i = 0; i < preload; i++)
            pool.Enqueue(Instantiate(prefab, transform));
    }

    public EnemyHPBar Get()
    {
        return pool.Count > 0 ? pool.Dequeue() : Instantiate(prefab, transform);
    }

    public void Return(EnemyHPBar bar)
    {
        bar.Hide();
        bar.Unbind();
        pool.Enqueue(bar);
    }
}
