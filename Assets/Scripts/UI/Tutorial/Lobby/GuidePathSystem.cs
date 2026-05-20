using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public class GuidePathSystem : MonoBehaviour
{
    [Header("Settings")]
    public Transform player;
    [Tooltip("LineRenderer prefab used to display the guide path.")]
    public GameObject linePrefab;

    public float pathHeight = 0.5f;
    public float textureScrollSpeed = 2f;
    [Min(0.02f)] public float pathRefreshInterval = 0.12f;
    [Min(0.01f)] public float repathDistanceThreshold = 0.35f;

    readonly List<LineRenderer> activeLines = new();
    readonly List<Transform> currentTargets = new();
    readonly List<Vector3> lastTargetPositions = new();
    readonly List<Material> lineMaterials = new();
    NavMeshPath navMeshPath;
    float nextRefreshTime;
    Vector3 lastPlayerPosition;
    int activeLineCount;

    void Awake()
    {
        navMeshPath = new NavMeshPath();

        if (player == null)
        {
            GameObject taggedPlayer = GameObject.FindWithTag("Player");
            if (taggedPlayer != null)
                player = taggedPlayer.transform;
        }

        enabled = false;
    }

    void Update()
    {
        if (player == null || currentTargets.Count == 0)
        {
            enabled = false;
            return;
        }

        if (Time.time >= nextRefreshTime && ShouldRefreshPaths())
        {
            RefreshPaths();
            nextRefreshTime = Time.time + pathRefreshInterval;
        }

        float offset = Time.time * -textureScrollSpeed;
        for (int i = 0; i < activeLineCount; i++)
        {
            Material material = lineMaterials[i];
            if (material == null)
                continue;

            material.mainTextureOffset = new Vector2(offset, 0f);
        }
    }

    bool ShouldRefreshPaths()
    {
        float thresholdSqr = repathDistanceThreshold * repathDistanceThreshold;
        if ((player.position - lastPlayerPosition).sqrMagnitude >= thresholdSqr)
            return true;

        for (int i = 0; i < currentTargets.Count; i++)
        {
            Transform target = currentTargets[i];
            if (target == null)
                return true;

            if ((target.position - lastTargetPositions[i]).sqrMagnitude >= thresholdSqr)
                return true;
        }

        return false;
    }

    void RefreshPaths()
    {
        lastPlayerPosition = player.position;

        for (int i = 0; i < currentTargets.Count; i++)
        {
            if (i >= activeLineCount)
                break;

            Transform target = currentTargets[i];
            LineRenderer line = activeLines[i];
            if (line == null)
                continue;

            if (target == null)
            {
                line.enabled = false;
                continue;
            }

            lastTargetPositions[i] = target.position;
            DrawPath(line, target.position);
        }
    }

    void DrawPath(LineRenderer line, Vector3 targetPos)
    {
        if (!NavMesh.CalculatePath(player.position, targetPos, NavMesh.AllAreas, navMeshPath))
        {
            line.enabled = false;
            return;
        }

        int cornerCount = navMeshPath.corners.Length;
        line.positionCount = cornerCount;

        for (int i = 0; i < cornerCount; i++)
        {
            Vector3 pos = navMeshPath.corners[i];
            pos.y += pathHeight;
            line.SetPosition(i, pos);
        }

        line.enabled = cornerCount > 0;
    }

    public void ShowPath(params Transform[] newTargets)
    {
        HidePath();

        if (newTargets == null || linePrefab == null)
            return;

        for (int i = 0; i < newTargets.Length; i++)
        {
            Transform target = newTargets[i];
            if (target == null)
                continue;

            currentTargets.Add(target);
            lastTargetPositions.Add(target.position);
        }

        int preparedLineCount = 0;
        for (int i = 0; i < currentTargets.Count; i++)
        {
            LineRenderer lineRenderer = GetOrCreateLineRenderer(i);
            if (lineRenderer == null)
                break;

            lineRenderer.gameObject.SetActive(true);
            lineRenderer.enabled = true;
            preparedLineCount++;
        }

        if (preparedLineCount < currentTargets.Count)
        {
            currentTargets.RemoveRange(preparedLineCount, currentTargets.Count - preparedLineCount);
            lastTargetPositions.RemoveRange(preparedLineCount, lastTargetPositions.Count - preparedLineCount);
        }

        activeLineCount = preparedLineCount;
        lastPlayerPosition = player != null ? player.position : Vector3.zero;
        nextRefreshTime = 0f;
        enabled = activeLineCount > 0;
        RefreshPaths();
    }

    public void HidePath()
    {
        for (int i = 0; i < activeLines.Count; i++)
        {
            if (activeLines[i] != null)
            {
                activeLines[i].enabled = false;
                activeLines[i].gameObject.SetActive(false);
            }
        }

        activeLineCount = 0;
        currentTargets.Clear();
        lastTargetPositions.Clear();
        enabled = false;
    }

    void OnDestroy()
    {
        for (int i = 0; i < lineMaterials.Count; i++)
        {
            if (lineMaterials[i] != null)
                Destroy(lineMaterials[i]);
        }
    }

    LineRenderer GetOrCreateLineRenderer(int index)
    {
        if (index < activeLines.Count)
            return activeLines[index];

        // Reuse line renderers across objectives to avoid repeated instantiate/destroy churn.
        GameObject lineObject = Instantiate(linePrefab, transform);
        if (lineObject == null)
            return null;

        LineRenderer lineRenderer = lineObject.GetComponent<LineRenderer>();
        if (lineRenderer == null)
        {
            Destroy(lineObject);
            return null;
        }

        lineObject.SetActive(false);
        activeLines.Add(lineRenderer);
        lineMaterials.Add(lineRenderer.material);
        return lineRenderer;
    }
}
