using UnityEngine;

public static class CombatStateReaderResolver
{
    public static ICombatStateReader ResolveOrAttach(MonoBehaviour owner)
    {
        if (owner == null)
            return null;

        PlayerActionCoordinator coordinator = owner.GetComponent<PlayerActionCoordinator>();
        if (coordinator != null)
            return coordinator;

        ICombatStateReader reader = owner.GetComponent<ICombatStateReader>();
        if (reader != null)
            return reader;

        return owner.gameObject.AddComponent<PlayerActionCoordinator>();
    }

    public static ICombatStateReader ResolveExisting(Component owner)
    {
        if (owner == null)
            return null;

        PlayerActionCoordinator coordinator = owner.GetComponent<PlayerActionCoordinator>();
        if (coordinator != null)
            return coordinator;

        return owner.GetComponent<ICombatStateReader>();
    }
}
