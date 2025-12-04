using UnityEngine;
using UnityEngine.AI;
using System.Collections.Generic;

public class GuidePathSystem : MonoBehaviour
{
    [Header("■ 설정")]
    public Transform player;       
    [Tooltip("LineRenderer가 붙은 프리팹을 넣으세요")]
    public GameObject linePrefab; // ★ 프리팹 연결 변수
    
    public float pathHeight = 0.5f; 
    public float textureScrollSpeed = 2.0f; 

    // 활성화된 라인들 관리
    private List<LineRenderer> activeLines = new List<LineRenderer>();
    private List<Transform> currentTargets = new List<Transform>();
    private NavMeshPath navMeshPath;

    void Awake()
    {
        navMeshPath = new NavMeshPath();
        
        if (player == null)
        {
            var p = GameObject.FindWithTag("Player");
            if (p) player = p.transform;
        }
    }

    void Update()
    {
        if (player == null || currentTargets.Count == 0) return;

        // 타겟 개수만큼 라인 그리기
        for (int i = 0; i < currentTargets.Count; i++)
        {
            // 라인이 모자라면 생성 안 함 (안전장치)
            if (i >= activeLines.Count) break;

            if (currentTargets[i] != null)
            {
                DrawPath(activeLines[i], currentTargets[i].position);
            }
        }

        // 텍스트 흐르는 애니메이션
        float offset = Time.time * -textureScrollSpeed;
        foreach (var line in activeLines)
        {
            if (line != null && line.material != null) 
                line.material.mainTextureOffset = new Vector2(offset, 0);
        }
    }

    void DrawPath(LineRenderer line, Vector3 targetPos)
    {
        // 플레이어 -> 타겟 경로 계산
        if (NavMesh.CalculatePath(player.position, targetPos, NavMesh.AllAreas, navMeshPath))
        {
            line.positionCount = navMeshPath.corners.Length;
            for (int j = 0; j < navMeshPath.corners.Length; j++)
            {
                Vector3 pos = navMeshPath.corners[j];
                pos.y += pathHeight;
                line.SetPosition(j, pos);
            }
            line.enabled = true;
        }
        else
        {
            line.enabled = false; // 길 없으면 숨김
        }
    }

    // ★ [핵심] 타겟을 여러 개 받아서 각각 라인을 생성함
    public void ShowPath(params Transform[] newTargets)
    {
        // 기존 라인 싹 지우기
        HidePath();

        if (newTargets == null || linePrefab == null) return;

        // 타겟 등록
        currentTargets.AddRange(newTargets);

        // 타겟 개수만큼 라인 프리팹 생성
        for (int i = 0; i < currentTargets.Count; i++)
        {
            GameObject lineObj = Instantiate(linePrefab, transform); // 자식으로 생성
            LineRenderer lr = lineObj.GetComponent<LineRenderer>();
            if (lr)
            {
                lr.enabled = true;
                activeLines.Add(lr);
            }
        }
    }

    public void HidePath()
    {
        // 생성했던 라인들 모두 삭제
        foreach (var line in activeLines)
        {
            if (line != null) Destroy(line.gameObject);
        }
        activeLines.Clear();
        currentTargets.Clear();
    }
}