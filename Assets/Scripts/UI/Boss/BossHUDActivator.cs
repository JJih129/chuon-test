// BossHUDActivator.cs (새 파일)
using UnityEngine;

public class BossHUDActivator : MonoBehaviour
{
    public Transform player;
    public Transform boss;
    public float showDistance = 25f;     // 이 거리 이내면 표시
    public GameObject bossHUDRoot;       // 상단 고정 Boss HUD (Slider 포함)

    bool isShown;

    void Start()
    {
        if (bossHUDRoot) bossHUDRoot.SetActive(false);
    }

    void Update()
    {
        if (!player || !boss || !bossHUDRoot) return;
        float d = Vector3.Distance(player.position, boss.position);
        bool shouldShow = d <= showDistance;

        if (shouldShow != isShown)
        {
            bossHUDRoot.SetActive(shouldShow);
            isShown = shouldShow;
        }
    }
}