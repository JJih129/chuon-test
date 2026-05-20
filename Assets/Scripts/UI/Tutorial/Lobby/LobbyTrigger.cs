using UnityEngine;

public class LobbyTrigger : MonoBehaviour
{
    public enum TriggerType
    {
        Approach,
        Board
    }

    public TriggerType type;
    [SerializeField] bool debugLogs = false;

    bool _isTriggered;

    void OnTriggerEnter(Collider other)
    {
        CheckTrigger(other);
    }

    void OnTriggerStay(Collider other)
    {
        CheckTrigger(other);
    }

    void CheckTrigger(Collider other)
    {
        if (_isTriggered)
        {
            if (LobbyManager.Instance != null)
            {
                if (type == TriggerType.Board && !LobbyManager.Instance.IsBoarded)
                    _isTriggered = false;
                else if (type == TriggerType.Approach && !LobbyManager.Instance.IsDoorReached)
                    _isTriggered = false;
            }

            if (_isTriggered)
                return;
        }

        if (!other.CompareTag("Player"))
            return;

        if (debugLogs)
            Debug.Log($"[LobbyTrigger] Player trigger: {type}", this);

        if (LobbyManager.Instance == null)
            return;

        if (type == TriggerType.Approach)
        {
            LobbyManager.Instance.OnReachElevator();
            _isTriggered = true;
        }
        else if (type == TriggerType.Board)
        {
            LobbyManager.Instance.OnEnterElevator();
            _isTriggered = true;
        }
    }
}
