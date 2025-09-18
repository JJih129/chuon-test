using UnityEngine;
using System.Collections;
using System.Collections.Generic;

// [헤더] 엘리베이터 플랫폼 이동 컨트롤러
// - platform: 승강기 플랫폼 Transform(필수)
// - stops: 정차 지점 리스트(월드 좌표 Transform)
// - moveSpeed: 이동 속도(m/s)
// - accelTime: 가속 시간(초)
// - doorObjects: 문 오브젝트 목록(선택)
// - doorOpenAngle: 문 열림 각도(Yaw 기준 예시)
// - doorAnimTime: 문 여닫이 시간(초)
// - holdTimeAtStop: 정차 후 문 열고 닫기까지 대기 시간(초)
// - allowPlayerRide: 플레이어를 자식으로 붙여 탑승 처리 여부
public class ElevatorController : MonoBehaviour
{
    public Transform platform;
    public List<Transform> stops = new List<Transform>();
    public float moveSpeed = 2.0f;
    public float accelTime = 0.4f;

    public Transform[] doorObjects;
    public float doorOpenAngle = 90f;
    public float doorAnimTime = 0.6f;
    public float holdTimeAtStop = 0.5f;

    public bool allowPlayerRide = true;

    int _currentIndex = 0;
    bool _busy = false;

    public bool IsBusy => _busy;
    public int CurrentStopIndex => _currentIndex;

    void Reset()
    {
        if (!platform) platform = transform;
    }

    // [헤더] 외부 호출: 특정 층으로 이동
    public void CallTo(int stopIndex, Transform player = null)
    {
        if (_busy) return;
        if (stopIndex < 0 || stopIndex >= stops.Count) return;
        if (stopIndex == _currentIndex) return;
        StartCoroutine(CoRun(stopIndex, player));
    }

    IEnumerator CoRun(int targetIndex, Transform rider)
    {
        _busy = true;

        if (allowPlayerRide && rider)
        {
            rider.SetParent(platform, true);
        }

        // 문 닫기
        yield return StartCoroutine(CoDoor(false));

        // 이동
        Vector3 start = platform.position;
        Vector3 end = stops[targetIndex].position;

        float dist = Vector3.Distance(start, end);
        float t = 0f;
        float accel = Mathf.Max(0.001f, accelTime);
        float cruiseDist = Mathf.Max(0f, dist - moveSpeed * accel); // 간단 가감속
        float traveled = 0f;

        while (traveled < dist - 0.01f)
        {
            float dt = Time.deltaTime;
            float curSpeed = moveSpeed;

            // 가속/감속 근사
            float remain = dist - traveled;
            if (traveled < moveSpeed * accel) curSpeed *= Mathf.InverseLerp(0f, moveSpeed * accel, traveled);
            if (remain < moveSpeed * accel) curSpeed *= Mathf.InverseLerp(0f, moveSpeed * accel, remain);

            float step = curSpeed * dt;
            traveled = Mathf.Min(dist, traveled + step);
            t = dist > 0.0001f ? traveled / dist : 1f;

            platform.position = Vector3.Lerp(start, end, t);
            yield return null;
        }

        platform.position = end;
        _currentIndex = targetIndex;

        // 문 열기 + 정차 대기
        yield return StartCoroutine(CoDoor(true));
        yield return new WaitForSeconds(holdTimeAtStop);

        if (allowPlayerRide && rider)
        {
            rider.SetParent(null, true);
        }

        _busy = false;
    }

    IEnumerator CoDoor(bool open)
    {
        if (doorObjects == null || doorObjects.Length == 0) yield break;

        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / Mathf.Max(0.001f, doorAnimTime);
            float a = open ? t : (1f - t);
            float angle = Mathf.Lerp(0f, doorOpenAngle, a);

            foreach (var d in doorObjects)
            {
                if (!d) continue;
                var rot = d.localRotation.eulerAngles;
                rot.y = angle;
                d.localRotation = Quaternion.Euler(rot);
            }
            yield return null;
        }
    }
}
