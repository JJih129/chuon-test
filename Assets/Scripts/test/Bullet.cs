using UnityEngine;

/// <summary>
/// Moves forward and destroys after a set lifetime.
/// 앞으로 이동하며 지정된 시간 후 파괴됩니다.
/// </summary>
public class Bullet : MonoBehaviour
{
    // Movement speed of the bullet.
    // 총알의 이동 속도.
    [Header("▶ Movement speed of bullet / 총알의 이동 속도")]
    [SerializeField] private float speed = 10f;

    // Lifetime of the bullet in seconds.
    // 총알의 수명(초).
    [Header("▶ Lifetime in seconds / 총알의 수명(초)")]
    [SerializeField] private float lifeTime = 5f;

    // Initialize speed and lifetime from spawner.
    // 스포너에서 속도와 수명을 초기화합니다.
    public void Init(float newSpeed, float newLifeTime)
    {
        speed = newSpeed;
        lifeTime = newLifeTime;
        Destroy(gameObject, lifeTime);
    }

    private void Update()
    {
        transform.position += transform.forward * speed * Time.deltaTime;
    }
}
