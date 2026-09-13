using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public class GuidePathSystem : MonoBehaviour
{
    [Header("Path Settings")]
    public Transform player;
    [Tooltip("Prefab that contains a LineRenderer.")]
    public GameObject linePrefab;

    public float pathHeight = 0.5f;
    public float textureScrollSpeed = 2.0f;

    private readonly List<LineRenderer> linePool = new List<LineRenderer>();
    private readonly List<Transform> currentTargets = new List<Transform>();
    private NavMeshPath navMeshPath;
    private int activeLineCount;

    void Awake()
    {
        navMeshPath = new NavMeshPath();

        if (player == null)
        {
            var foundPlayer = GameObject.FindWithTag("Player");
            if (foundPlayer != null)
                player = foundPlayer.transform;
        }
    }

    void Update()
    {
        if (player == null || currentTargets.Count == 0 || activeLineCount == 0) return;

        for (int i = 0; i < currentTargets.Count && i < activeLineCount; i++)
        {
            var target = currentTargets[i];
            if (target != null)
                DrawPath(linePool[i], target.position);
        }

        float offset = Time.time * -textureScrollSpeed;
        for (int i = 0; i < activeLineCount; i++)
        {
            var line = linePool[i];
            if (line != null && line.material != null)
                line.material.mainTextureOffset = new Vector2(offset, 0f);
        }
    }

    void DrawPath(LineRenderer line, Vector3 targetPos)
    {
        if (line == null) return;

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
            line.enabled = false;
            line.positionCount = 0;
        }
    }

    public void ShowPath(IList<Transform> newTargets)
    {
        HidePath();

        if (newTargets == null || linePrefab == null) return;

        for (int i = 0; i < newTargets.Count; i++)
        {
            if (newTargets[i] != null)
                currentTargets.Add(newTargets[i]);
        }

        for (int i = 0; i < currentTargets.Count; i++)
        {
            var line = GetOrCreateLine(i);
            if (line == null) continue;

            line.gameObject.SetActive(true);
            line.enabled = true;
            activeLineCount++;
        }
    }

    public void ShowPath(params Transform[] newTargets)
    {
        ShowPath((IList<Transform>)newTargets);
    }

    public void HidePath()
    {
        for (int i = 0; i < activeLineCount; i++)
        {
            var line = linePool[i];
            if (line == null) continue;

            line.enabled = false;
            line.positionCount = 0;
            line.gameObject.SetActive(false);
        }

        activeLineCount = 0;
        currentTargets.Clear();
    }

    LineRenderer GetOrCreateLine(int index)
    {
        while (linePool.Count <= index)
        {
            GameObject lineObj = Instantiate(linePrefab, transform);
            LineRenderer line = lineObj.GetComponent<LineRenderer>();
            if (line != null)
            {
                line.enabled = false;
                line.positionCount = 0;
                line.gameObject.SetActive(false);
            }
            linePool.Add(line);
        }

        return linePool[index];
    }
}
