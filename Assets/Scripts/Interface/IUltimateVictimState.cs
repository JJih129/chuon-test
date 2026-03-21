using UnityEngine;

public interface IUltimateVictimState
{
    bool IsInUltimateVictimState { get; }

    void BeginUltimateVictimState(Transform attacker, float durationHint);

    void SetUltimateVictimAnchor(Vector3 worldPosition, Vector3 lookTarget);

    void EndUltimateVictimState();
}
