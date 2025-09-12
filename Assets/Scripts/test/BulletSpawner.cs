using UnityEngine;

/// <summary>
/// Spawns bullet prefabs at regular intervals.
/// 일정한 간격으로 총알 프리팹을 생성합니다.
/// </summary>
public class BulletSpawner : MonoBehaviour
{
    // Prefab of bullet to spawn.
    // 생성할 총알 프리팹.
    [Header("▶ Bullet prefab to spawn / 생성할 총알 프리팹")]
    [SerializeField] private GameObject bulletPrefab;

    // Bullets per second to spawn.
    // 초당 발사 수.
    [Header("▶ Bullets per second / 초당 발사 수")]
    [SerializeField] private float fireRate = 1f;

    // Initial speed applied to spawned bullets.
    // 생성된 총알에 적용되는 초기 속도.
    [Header("▶ Initial speed of bullet / 생성된 총알의 초기 속도")]
    [SerializeField] private float bulletSpeed = 10f;

    // Lifetime of spawned bullets in seconds.
    // 생성된 총알의 수명(초).
    [Header("▶ Bullet lifetime in seconds / 생성된 총알의 수명(초)")]
    [SerializeField] private float bulletLifeTime = 5f;

    // Timer to track spawn intervals.
    // 발사 간격을 추적하는 타이머.
    private float fireTimer = 0f;

    private void Update()
    {
        fireTimer += Time.deltaTime;
        if (fireTimer >= 1f / fireRate)
        {
            fireTimer = 0f;
            SpawnBullet();
        }
    }

    // Instantiate a bullet and initialize its movement.
    // 총알을 생성하고 이동을 초기화합니다.
    private void SpawnBullet()
    {
        if (bulletPrefab == null) return;

        GameObject bulletObj = Instantiate(bulletPrefab, transform.position, transform.rotation);
        Bullet bullet = bulletObj.GetComponent<Bullet>();
        if (bullet != null)
        {
            bullet.Init(bulletSpeed, bulletLifeTime);
        }
        else
        {
            Destroy(bulletObj, bulletLifeTime);
        }
    }
}
