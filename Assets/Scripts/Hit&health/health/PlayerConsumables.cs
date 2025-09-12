using System;
using UnityEngine;

[RequireComponent(typeof(PlayerHealth))]
public class PlayerConsumables : MonoBehaviour
{
    [Header("Ampoule")]
    public int maxAmpoule = 4;
    public int currentAmpoule = 4;
    [Range(0f, 1f)] public float healRatio = 0.4f;
    public bool allowUseAtFullHP = true;

    public int MaxAmpoule => maxAmpoule;
    public int CurrentAmpoule => currentAmpoule;

    public event Action<int, int> OnAmpouleChanged; // current, max

    PlayerHealth health;

    void Awake()
    {
        health = GetComponent<PlayerHealth>();
    }

    void Start()
    {
        OnAmpouleChanged?.Invoke(currentAmpoule, maxAmpoule);
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Q))
            TryUseAmpoule();
    }

    public void TryUseAmpoule()
    {
        if (currentAmpoule <= 0) { Debug.Log("[Consumables] No ampoule"); return; }
        if (health.IsDead) { Debug.Log("[Consumables] Player dead"); return; }
        if (!allowUseAtFullHP && health.CurrentHP >= health.MaxHP) { Debug.Log("[Consumables] HP full"); return; }

        currentAmpoule--;
        int amount = Mathf.RoundToInt(health.MaxHP * healRatio);
        health.Heal(amount);

        OnAmpouleChanged?.Invoke(currentAmpoule, maxAmpoule);
        // 유리 캡슐 파편/사운드 등은 여기서 트리거
    }

    public void Refill()
    {
        currentAmpoule = maxAmpoule;
        OnAmpouleChanged?.Invoke(currentAmpoule, maxAmpoule);
    }
}
