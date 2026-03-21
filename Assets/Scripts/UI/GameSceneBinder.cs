using UnityEngine;

public class GameSceneBinder : MonoBehaviour
{
    System.Collections.IEnumerator Start()
    {
        yield return null;

        var hud = FindObjectOfType<PlayerHUD>(true);
        var ph = FindObjectOfType<PlayerHealth>(true);
        var pc = FindObjectOfType<PlayerConsumables>(true);
        var pg = FindObjectOfType<PlayerGuardController>(true);
        var boss = FindObjectOfType<BossController>(true);
        if (hud && ph) hud.Bind(ph, pc, pg, boss);
    }
}
