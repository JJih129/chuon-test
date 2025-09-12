using UnityEngine;

/// <summary>
/// 개발자 테스트용 핫키(피격/다운/사망 + 궁극기/브레이크 수급)
/// </summary>
public class DevPlayerHitHotkeys : MonoBehaviour
{
    // ── 참조 ─────────────────────────────────────────────────────────
    [Header("▶ 테스트 대상 (Player/보스)")]
    public PlayerHealth playerHealth;                   // 선택
    public PlayerCombatController combatController;     // 필수 권장
    public PlayerUltimateController ultimateController; // (추가) 궁극기 게이지용
    public BossBreakController bossBreak;               // (추가) 브레이크 게이지용

    // ── 키 바인딩 ───────────────────────────────────────────────────
    [Header("▶ 핫키: 피격/다운/사망 트리거")]
    public KeyCode normalHitKey   = KeyCode.J;
    public KeyCode heavyHitKey    = KeyCode.K;
    public KeyCode knockdownKey   = KeyCode.U;
    public KeyCode deathKey       = KeyCode.I;
    public KeyCode parryFailKey   = KeyCode.L;

    [Header("▶ 핫키: 수급(개발용)")]
    public KeyCode addUltimateKey = KeyCode.G; // 궁극기 +
    public KeyCode addBreakKey    = KeyCode.B; // 브레이크 +

    // ── 데미지/무적 옵션 ────────────────────────────────────────────
    [Header("▶ 데미지/무적 옵션")]
    public bool visualOnly = true;
    public bool bypassInvincible = true;
    public int  normalDamage = 8;
    public int  heavyDamage  = 20;

    // ── 히트스톱 옵션 ───────────────────────────────────────────────
    [Header("▶ 히트스톱(테스트)")]
    public bool enableHitStop = false;
    [Range(0.01f, 1f)]  public float hitStopScale = 0.1f;
    [Range(0.01f, 0.3f)] public float hitStopDuration = 0.06f;

    // ── (추가) 수급량 ───────────────────────────────────────────────
    [Header("▶ 수급량(%) | 디버그용")]
    [Tooltip("G 키 1회당 궁극기 게이지 증가량(%)")]
    public float addUltimatePercent = 12.5f;
    [Tooltip("B 키 1회당 브레이크 게이지 증가량(%)")]
    public float addBreakPercent    = 12.5f;

    void Awake()
    {
        if (!playerHealth)      playerHealth      = GetComponent<PlayerHealth>();
        if (!combatController)  combatController  = GetComponent<PlayerCombatController>();
        if (!ultimateController) ultimateController = FindObjectOfType<PlayerUltimateController>();
        if (!bossBreak)         bossBreak         = FindObjectOfType<BossBreakController>();
    }

    void Update()
    {
        if (combatController)
        {
            if (Input.GetKeyDown(normalHitKey)) FireHit(heavy:false, dmg: normalDamage);
            if (Input.GetKeyDown(heavyHitKey))  FireHit(heavy:true,  dmg: heavyDamage);
            if (Input.GetKeyDown(parryFailKey)) FireHit(heavy:true,  dmg: heavyDamage);

            if (Input.GetKeyDown(knockdownKey)) FireKnockdown();
            if (Input.GetKeyDown(deathKey))     FireDeath();
        }

        // (추가) 궁극기/브레이크 수급 디버그
        if (Input.GetKeyDown(addUltimateKey))
            ultimateController?.AddGauge(addUltimatePercent);

        if (Input.GetKeyDown(addBreakKey))
            bossBreak?.AddBreak(addBreakPercent);
    }

    void FireHit(bool heavy, int dmg)
    {
        if (playerHealth && !visualOnly)
        {
            bool prevInv = playerHealth.isInvincible;
            if (bypassInvincible) playerHealth.isInvincible = false;

            Vector3 hp = playerHealth.transform.position + Vector3.up * 1.2f;
            var hitType = heavy ? HitType.Strong : HitType.Normal;
            playerHealth.TakeDamage(dmg, hitType, hp);

            if (bypassInvincible) playerHealth.isInvincible = prevInv;
        }

        combatController?.ApplyHit(heavy);

        if (enableHitStop) StartCoroutine(HitStop());
    }

    void FireKnockdown()
    {
        combatController?.ApplyKnockdown();
        if (enableHitStop) StartCoroutine(HitStop());
    }

    void FireDeath()
    {
        combatController?.ApplyDeath();
        if (enableHitStop) StartCoroutine(HitStop());
    }

    System.Collections.IEnumerator HitStop()
    {
        float prev = Time.timeScale;
        Time.timeScale = hitStopScale;
        yield return new WaitForSecondsRealtime(hitStopDuration);
        Time.timeScale = prev;
    }
}
