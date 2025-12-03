using UnityEngine;
using UnityEngine.AI; // 네비게이션 필수

[RequireComponent(typeof(LineRenderer))]
public class GuidePathSystem : MonoBehaviour
{
    [Header("■ 설정")]
    public Transform player;       // 플레이어 (출발지)
    public Transform target;       // 목표 (도착지)
    public float pathHeight = 0.5f; // 바닥에서 얼마나 띄울지
    public float textureScrollSpeed = 2.0f; // 무늬 이동 속도

    private NavMeshPath path;
    private LineRenderer line;
    private bool isActive = false;

    void Awake()
    {
        line = GetComponent<LineRenderer>();
        path = new NavMeshPath();
        
        // 플레이어 자동 찾기
        if (player == null)
        {
            var p = GameObject.FindWithTag("Player");
            if (p) player = p.transform;
        }

        // 처음엔 끄기
        HidePath();
    }

    void Update()
    {
        if (!isActive || player == null || target == null) return;

        // 1. 경로 계산 (플레이어 -> 타겟)
        if (NavMesh.CalculatePath(player.position, target.position, NavMesh.AllAreas, path))
        {
            // 2. LineRenderer에 점 찍기
            line.positionCount = path.corners.Length;
            
            for (int i = 0; i < path.corners.Length; i++)
            {
                // 바닥에 파묻히지 않게 살짝 위로 올림
                Vector3 pos = path.corners[i];
                pos.y += pathHeight;
                line.SetPosition(i, pos);
            }
        }

        // 3. 텍스처 흐르는 애니메이션 (화살표가 움직이는 느낌)
        if (line.material)
        {
            float offset = Time.time * -textureScrollSpeed;
            line.material.mainTextureOffset = new Vector2(offset, 0);
        }
    }

    // 외부에서 호출: 길 안내 시작
    public void ShowPath(Transform newTarget)
    {
        target = newTarget;
        isActive = true;
        line.enabled = true;
    }

    // 외부에서 호출: 길 안내 끄기
    public void HidePath()
    {
        isActive = false;
        line.enabled = false;
    }
}