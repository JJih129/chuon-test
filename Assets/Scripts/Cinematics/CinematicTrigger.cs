using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 시네마틱 발동기. 모드:
/// - Volume: 플레이어가 트리거 콜라이더에 들어오면 (isTrigger 콜라이더 필요)
/// - SceneStart: 씬 시작 delay 초 뒤 (튜토리얼 경유 여부 같은 흐름 조건은 호출부 통합으로 — Docs/Cinematics_Shotlist.md)
/// - Manual: Fire() 호출 (UnityEvent·다른 스크립트)
/// 가드: 전투 중이면 기다렸다가(최대 waitForCombatEnd 초) 발동하거나 포기한다. 같은 회차에 이미 본 연출은 디렉터가 끝 상태로 넘긴다.
/// </summary>
public sealed class CinematicTrigger : MonoBehaviour
{
    public enum Mode : byte { Volume = 0, SceneStart = 1, Manual = 2 }

    [SerializeField] string sequenceId;
    [SerializeField] Mode mode = Mode.Volume;
    [SerializeField] bool once = true;
    [SerializeField, Min(0f)] float delay;
    [Tooltip("전투 중이면 이 시간(실시간)까지 끝나기를 기다린다. 0 = 전투 중이면 바로 포기.")]
    [SerializeField, Min(0f)] float waitForCombatEnd = 6f;
    [Tooltip("다른 연출이 재생 중이면 끝날 때까지 기다린다.")]
    [SerializeField] bool queueBehindOtherCinematic = true;
    [Tooltip("이 GameObject 들이 꺼져 있을 때만 발동 (예: 하층으로 이미 넘어간 경우 상층 연출 금지)")]
    [SerializeField] GameObject[] requireActive;
    [Tooltip("발동 시 끌 오브젝트 (대체되는 옛 연출 트리거 등)")]
    [SerializeField] Behaviour[] disableOnFire;

    public UnityEvent onFinished = new UnityEvent();

    bool _fired;
    bool _pending;

    public string SequenceId => sequenceId;
    public Mode TriggerMode => mode;

    public void Configure(string id, Mode triggerMode, float startDelay = 0f, bool fireOnce = true)
    {
        sequenceId = id;
        mode = triggerMode;
        delay = startDelay;
        once = fireOnce;
    }

    void Reset()
    {
        Collider c = GetComponent<Collider>();
        if (c != null)
            c.isTrigger = true;
    }

    void Start()
    {
        if (mode == Mode.SceneStart)
            Fire();
    }

    void OnTriggerEnter(Collider other)
    {
        if (mode != Mode.Volume || other == null)
            return;
        if (!IsPlayer(other))
            return;
        Fire();
    }

    public void Fire()
    {
        if ((_fired && once) || _pending || !isActiveAndEnabled)
            return;
        if (requireActive != null)
            for (int i = 0; i < requireActive.Length; i++)
                if (requireActive[i] != null && !requireActive[i].activeInHierarchy)
                    return;
        _pending = true;
        StartCoroutine(CoFire());
    }

    System.Collections.IEnumerator CoFire()
    {
        if (delay > 0f)
            yield return new WaitForSecondsRealtime(delay);

        float waitUntil = Time.unscaledTime + waitForCombatEnd;
        while (CinematicDirector.IsCombatActive())
        {
            if (Time.unscaledTime > waitUntil)
            {
                GameLog.Info($"[CinematicTrigger] '{sequenceId}' dropped: combat active", this);
                _pending = false;
                yield break;
            }
            yield return new WaitForSecondsRealtime(0.25f);
        }

        while (queueBehindOtherCinematic && CinematicDirector.IsPlaying)
            yield return null;

        if (disableOnFire != null)
            for (int i = 0; i < disableOnFire.Length; i++)
                if (disableOnFire[i] != null)
                    disableOnFire[i].enabled = false;

        _fired = true;
        _pending = false;
        CinematicDirector.Play(sequenceId, HandleFinished);
    }

    void HandleFinished()
    {
        onFinished?.Invoke();
    }

    static bool IsPlayer(Collider other)
    {
        if (other.CompareTag("Player"))
            return true;
        Rigidbody rb = other.attachedRigidbody;
        if (rb != null && rb.CompareTag("Player"))
            return true;
        return other.GetComponentInParent<PlayerHealth>() != null;
    }
}
