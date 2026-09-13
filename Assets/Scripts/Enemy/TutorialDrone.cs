using System.Collections;
using DG.Tweening;
using UnityEngine;

public class TutorialDrone : MonoBehaviour
{
    static readonly WaitForSeconds WarmupWait = new WaitForSeconds(1.0f);
    static readonly WaitForSeconds WarningWait = new WaitForSeconds(1.0f);

    [Header("Attack")]
    public GameObject projectilePrefab;
    public Transform firePoint;
    public float attackInterval = 3.5f;

    [Header("Target")]
    public Transform playerTarget;

    [Header("Visual")]
    public Renderer droneRenderer;
    public Color warningColor = Color.red;

    Color normalColor;
    Material droneMaterial;
    Coroutine attackRoutine;
    WaitForSeconds cachedLoopWait;
    float cachedLoopWaitSeconds = float.NaN;

    void OnEnable()
    {
        CacheMaterial();

        if (playerTarget == null)
        {
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player) playerTarget = player.transform;
        }

        attackRoutine = StartCoroutine(AttackLoop());
    }

    void OnDisable()
    {
        if (attackRoutine != null)
        {
            StopCoroutine(attackRoutine);
            attackRoutine = null;
        }
    }

    void Update()
    {
        if (playerTarget == null) return;

        Vector3 dir = playerTarget.position - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude <= 0.000001f) return;

        Quaternion lookRot = Quaternion.LookRotation(dir.normalized);
        transform.rotation = Quaternion.Slerp(transform.rotation, lookRot, Time.deltaTime * 5f);
    }

    IEnumerator AttackLoop()
    {
        yield return WarmupWait;

        while (true)
        {
            yield return GetLoopWait();

            if (droneMaterial != null)
                droneMaterial.DOColor(warningColor, 0.5f).SetLoops(2, LoopType.Yoyo);

            yield return WarningWait;

            if (projectilePrefab != null && firePoint != null)
            {
                GameObject bullet = Instantiate(projectilePrefab, firePoint.position, firePoint.rotation);
                TutorialProjectile projectile = bullet.GetComponent<TutorialProjectile>();
                if (projectile) projectile.owner = transform;
            }

            transform.DOPunchPosition(Vector3.back * 0.3f, 0.2f);

            if (droneMaterial != null)
                droneMaterial.color = normalColor;
        }
    }

    WaitForSeconds GetLoopWait()
    {
        float waitSeconds = attackInterval - 1.5f;
        if (cachedLoopWait == null || !Mathf.Approximately(cachedLoopWaitSeconds, waitSeconds))
        {
            cachedLoopWaitSeconds = waitSeconds;
            cachedLoopWait = new WaitForSeconds(waitSeconds);
        }

        return cachedLoopWait;
    }

    void CacheMaterial()
    {
        if (!droneRenderer) return;
        if (droneMaterial != null) return;

        droneMaterial = droneRenderer.material;
        normalColor = droneMaterial.color;
    }
}
