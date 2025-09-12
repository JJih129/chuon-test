using UnityEngine;

public class GameSceneBinder : MonoBehaviour
{
    void Start()
    {
        var hud = FindObjectOfType<PlayerHUD>(true);
        var ph = FindObjectOfType<PlayerHealth>(true);
        var pc = FindObjectOfType<PlayerConsumables>(true);
        if (hud && ph) hud.Bind(ph, pc);
    }
}
