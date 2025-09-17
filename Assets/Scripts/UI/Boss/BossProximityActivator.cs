using System.Collections;
using UnityEngine;

// BossProximityActivator.cs
// 설명: 플레이어와 보스 거리 기반으로 상단 Boss HUD를 바인딩/해제.
// 사용법: 보스 루트에 이 스크립트 붙이고 healthBehaviour에 IHealth 구현체 연결.
// 변수 헤더는 한글 설명으로 되어 있음.

public class BossProximityActivator : MonoBehaviour
{
    [Header("▶ 참조 (필수)")]
    [Tooltip("플레이어 Transform. 거리 계산 기준.")]
    public Transform player;

    [Tooltip("상단 Boss HUD가 포함된 루트 GameObject (초기에는 비활성화).")]
    public GameObject bossHUDRoot;

    [Tooltip("상단 BossHUD 컴포넌트 (bossHUDRoot에 붙어있음).")]
    public BossHUD bossHUD;

    [Tooltip("보스의 체력 컴포넌트. IHealth를 구현한 컴포넌트를 드래그하세요.")]
    public MonoBehaviour healthBehaviour;

    [Header("▶ 거리/타이밍 설정")]
    [Tooltip("UI가 표시되는 최대 거리(미터). 이 거리 이하일 때 노출 시도.")]
    public float showDistance = 18f;

    [Tooltip("숨김 히스테리시스(미터). showDistance + hideHysteresis 를 hide 기준으로 사용.")]
    public float hideHysteresis = 2f;

    [Tooltip("거리 체크 주기(초). 0이면 매 프레임(성능 주의).")]
    public float pollInterval = 0.12f;

    [Tooltip("근접 후 HUD가 켜지기까지 딜레이(초). 0이면 즉시).")]
    public float showDelay = 0.05f;

    [Tooltip("범위 이탈 후 HUD가 꺼지기까지 딜레이(초).")]
    public float hideDelay = 0.12f;

    // 내부 상태
    float showSqr;
    float hideSqr;
    Coroutine pollRoutine;
    bool isBound = false;
    IHealth boundHealthIface;

    void Awake()
    {
        showSqr = showDistance * showDistance;
        hideSqr = (showDistance + Mathf.Max(0f, hideHysteresis));
        hideSqr *= hideSqr;

        if (bossHUDRoot != null) bossHUDRoot.SetActive(false);

        // 인터페이스 캐스트 시도
        if (healthBehaviour != null)
        {
            boundHealthIface = healthBehaviour as IHealth;
            if (boundHealthIface == null)
                Debug.LogWarning($"[BossProximityActivator] healthBehaviour이 IHealth를 구현하지 않습니다: {healthBehaviour.GetType().Name}");
        }
    }

    void OnEnable()
    {
        if (pollRoutine != null) StopCoroutine(pollRoutine);
        pollRoutine = StartCoroutine(Poll());
    }

    void OnDisable()
    {
        if (pollRoutine != null) StopCoroutine(pollRoutine);
        UnbindImmediate();
    }

    IEnumerator Poll()
    {
        var wait = (pollInterval > 0f) ? new WaitForSeconds(pollInterval) : null;
        while (true)
        {
            Evaluate();
            if (wait != null) yield return wait;
            else yield return null;
        }
    }

    void Evaluate()
    {
        if (player == null) return;
        float sqr = (player.position - transform.position).sqrMagnitude;

        if (!isBound && sqr <= showSqr)
        {
            // 표시
            if (showDelay <= 0f) Bind();
            else StartCoroutine(DoBindDelayed(showDelay, sqr));
        }
        else if (isBound && sqr > hideSqr)
        {
            // 숨김
            if (hideDelay <= 0f) Unbind();
            else StartCoroutine(DoUnbindDelayed(hideDelay, sqr));
        }
    }

    IEnumerator DoBindDelayed(float delay, float snapshotSqr)
    {
        yield return new WaitForSeconds(delay);
        // 다른 상황으로 바뀌었으면 무시
        if ((player.position - transform.position).sqrMagnitude <= showSqr)
            Bind();
    }

    IEnumerator DoUnbindDelayed(float delay, float snapshotSqr)
    {
        yield return new WaitForSeconds(delay);
        if ((player.position - transform.position).sqrMagnitude > hideSqr)
            Unbind();
    }

    void Bind()
    {
        if (isBound) return;
        if (bossHUD == null || bossHUDRoot == null)
        {
            Debug.LogWarning("[BossProximityActivator] bossHUD 또는 bossHUDRoot 미할당.");
            return;
        }
        if (boundHealthIface == null)
        {
            // healthBehaviour가 IHealth가 아닐 경우 시도해봄
            if (healthBehaviour != null && healthBehaviour is IHealth ih2)
                boundHealthIface = ih2;
            else
            {
                Debug.LogWarning("[BossProximityActivator] 바인딩할 IHealth가 없습니다.");
                return;
            }
        }

        bossHUD.Bind(boundHealthIface);
        bossHUDRoot.SetActive(true);
        isBound = true;
    }

    void Unbind()
    {
        if (!isBound) return;
        bossHUD.Unbind();
        bossHUDRoot.SetActive(false);
        isBound = false;
    }

    void UnbindImmediate()
    {
        if (bossHUD != null) bossHUD.Unbind();
        if (bossHUDRoot != null) bossHUDRoot.SetActive(false);
        isBound = false;
    }
}
