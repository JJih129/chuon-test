using UnityEngine;

// 환경 스토리텔링: 거리 위를 도는 비전투 감시 드론 (탐조등이 바닥을 훑는다). 플레이어를 공격하지 않는다.
[DisallowMultipleComponent]
public sealed class InfAmbientDrone : MonoBehaviour
{
    [SerializeField] Vector3[] waypoints = System.Array.Empty<Vector3>();
    [SerializeField, Min(0.1f)] float speed = 4.5f;
    [SerializeField] Transform spot;
    [SerializeField, Min(0f)] float sweepDegrees = 22f;
    [SerializeField, Min(0f)] float bob = 0.25f;

    int _index;
    Vector3 _vel;
    float _phase;

    public void Configure(Vector3[] path, Transform spotlight, float moveSpeed)
    {
        waypoints = path ?? System.Array.Empty<Vector3>();
        spot = spotlight;
        speed = moveSpeed;
    }

    void Start()
    {
        _phase = Random.value * 10f;
        if (waypoints.Length > 0)
            transform.position = waypoints[0];
    }

    void Update()
    {
        if (waypoints.Length < 2)
            return;
        Vector3 goal = waypoints[_index];
        Vector3 to = goal - transform.position;
        if (to.magnitude < 1.2f)
        {
            _index = (_index + 1) % waypoints.Length;
            goal = waypoints[_index];
            to = goal - transform.position;
        }
        Vector3 desired = to.normalized * speed;
        _vel = Vector3.Lerp(_vel, desired, 1f - Mathf.Exp(-Time.deltaTime * 1.5f));
        Vector3 p = transform.position + _vel * Time.deltaTime;
        p.y = goal.y + Mathf.Sin((Time.time + _phase) * 1.3f) * bob;
        transform.position = p;
        Vector3 flat = new Vector3(_vel.x, 0f, _vel.z);
        if (flat.sqrMagnitude > 0.01f)
        {
            Quaternion yaw = Quaternion.LookRotation(flat.normalized, Vector3.up);
            float bank = Mathf.Clamp(Vector3.Dot(Vector3.Cross(transform.forward, flat.normalized), Vector3.up) * -25f, -18f, 18f);
            transform.rotation = Quaternion.Slerp(transform.rotation, yaw * Quaternion.Euler(6f, 0f, bank), 1f - Mathf.Exp(-Time.deltaTime * 2.5f));
        }
        if (spot != null)
        {
            float s = Mathf.Sin((Time.time + _phase) * 0.9f) * sweepDegrees;
            spot.localRotation = Quaternion.Euler(70f, s, 0f);
        }
    }
}
