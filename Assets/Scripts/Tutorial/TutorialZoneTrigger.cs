using System;
using UnityEngine;

[RequireComponent(typeof(Collider))]
[DisallowMultipleComponent]
public class TutorialZoneTrigger : MonoBehaviour
{
    [SerializeField] private string requiredTag = "Player";
    [SerializeField] private LayerMask allowedLayers = ~0;
    [SerializeField] private bool oneShot;
    [SerializeField] private bool forceTriggerCollider = true;
    [SerializeField] private Transform requiredRoot;

    bool _hasTriggered;

    public event Action<TutorialZoneTrigger, Collider> TriggerEntered;
    public event Action<TutorialZoneTrigger, Collider> TriggerExited;

    void Awake()
    {
        Collider zoneCollider = GetComponent<Collider>();
        if (zoneCollider != null && forceTriggerCollider)
            zoneCollider.isTrigger = true;
    }

    public void ConfigureRuntime(Transform targetRoot, string tagFilter = "Player", bool oneshot = true)
    {
        requiredRoot = targetRoot;
        requiredTag = tagFilter;
        oneShot = oneshot;
    }

    public void ResetTriggerState()
    {
        _hasTriggered = false;
    }

    void OnTriggerEnter(Collider other)
    {
        if (!Matches(other))
            return;

        _hasTriggered |= oneShot;
        TriggerEntered?.Invoke(this, other);
    }

    void OnTriggerExit(Collider other)
    {
        if (!Matches(other))
            return;

        TriggerExited?.Invoke(this, other);
    }

    bool Matches(Collider other)
    {
        if (other == null)
            return false;

        if (oneShot && _hasTriggered)
            return false;

        if (((1 << other.gameObject.layer) & allowedLayers.value) == 0)
            return false;

        if (!string.IsNullOrWhiteSpace(requiredTag))
        {
            bool tagMatch = other.CompareTag(requiredTag) || other.transform.root.CompareTag(requiredTag);
            if (!tagMatch)
                return false;
        }

        if (requiredRoot == null)
            return true;

        return other.transform.root == requiredRoot.root;
    }
}
