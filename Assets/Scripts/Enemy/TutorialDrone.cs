using UnityEngine;
using System.Collections;
using DG.Tweening;

public class TutorialDrone : MonoBehaviour
{
    [Header("발사 설정")]
    public GameObject projectilePrefab; // 총알 프리팹 (필수 연결)
    public Transform firePoint;         // 발사 위치 (총구)
    public float attackInterval = 3.5f; // 공격 주기
    
    [Header("타겟")]
    public Transform playerTarget;

    [Header("연출")]
    public Renderer droneRenderer;
    public Color warningColor = Color.red;
    private Color normalColor;

    private Coroutine attackRoutine;

    void OnEnable()
    {
        if(droneRenderer) normalColor = droneRenderer.material.color;
        
        if (playerTarget == null)
        {
            GameObject p = GameObject.FindGameObjectWithTag("Player");
            if (p) playerTarget = p.transform;
        }

        attackRoutine = StartCoroutine(AttackLoop());
    }

    void OnDisable()
    {
        if (attackRoutine != null) StopCoroutine(attackRoutine);
    }

    void Update()
    {
        if (playerTarget != null)
        {
            Vector3 dir = (playerTarget.position - transform.position).normalized;
            dir.y = 0; 
            if (dir != Vector3.zero)
            {
                Quaternion lookRot = Quaternion.LookRotation(dir);
                transform.rotation = Quaternion.Slerp(transform.rotation, lookRot, Time.deltaTime * 5f);
            }
        }
    }

    IEnumerator AttackLoop()
    {
        yield return new WaitForSeconds(1.0f);

        while (true)
        {
            // 1. 대기
            yield return new WaitForSeconds(attackInterval - 1.5f);

            // 2. 경고 (깜빡임)
            if(droneRenderer)
            {
                droneRenderer.material.DOColor(warningColor, 0.5f).SetLoops(2, LoopType.Yoyo);
            }
            yield return new WaitForSeconds(1.0f);

            // 3. 발사
            if (projectilePrefab != null && firePoint != null)
            {
                GameObject bullet = Instantiate(projectilePrefab, firePoint.position, firePoint.rotation);
                TutorialProjectile bp = bullet.GetComponent<TutorialProjectile>();
                if (bp) bp.owner = this.transform;
            }
            
            // 반동 효과
            transform.DOPunchPosition(Vector3.back * 0.3f, 0.2f);
            
            if(droneRenderer) droneRenderer.material.color = normalColor;
        }
    }
}