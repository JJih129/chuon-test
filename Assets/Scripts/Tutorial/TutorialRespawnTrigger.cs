using UnityEngine;

[RequireComponent(typeof(Collider))]
[DisallowMultipleComponent]
public class TutorialRespawnTrigger : MonoBehaviour
{
    [SerializeField] private Transform respawnPoint;
    [SerializeField] private string requiredTag = "Player";
    [SerializeField] private bool zeroVelocity = true;

    public void ConfigureRuntime(Transform targetRespawnPoint, string tagFilter = "Player")
    {
        respawnPoint = targetRespawnPoint;
        requiredTag = tagFilter;
    }

    void Awake()
    {
        Collider zoneCollider = GetComponent<Collider>();
        if (zoneCollider != null)
            zoneCollider.isTrigger = true;
    }

    void OnTriggerEnter(Collider other)
    {
        if (respawnPoint == null || other == null)
            return;

        if (!string.IsNullOrWhiteSpace(requiredTag) &&
            !other.CompareTag(requiredTag) &&
            !other.transform.root.CompareTag(requiredTag))
        {
            return;
        }

        Transform targetRoot = other.transform.root;
        CharacterController controller = targetRoot.GetComponent<CharacterController>();
        Rigidbody body = targetRoot.GetComponent<Rigidbody>();

        if (controller != null && controller.enabled)
            controller.enabled = false;

        targetRoot.SetPositionAndRotation(respawnPoint.position, respawnPoint.rotation);

        if (controller != null)
            controller.enabled = true;

        if (zeroVelocity && body != null && !body.isKinematic)
            body.velocity = Vector3.zero;
    }
}
