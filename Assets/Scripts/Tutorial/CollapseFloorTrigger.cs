using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class CollapseFloorTrigger : MonoBehaviour
{
    [SerializeField] private Transform[] floorPieces;
    [SerializeField, Min(0f)] private float collapseDelay = 1.2f;
    [SerializeField, Min(0f)] private float shakeDuration = 0.4f;
    [SerializeField] private Vector3 shakeAmplitude = new Vector3(0.06f, 0.02f, 0.06f);
    [SerializeField] private bool enableRigidbodiesOnCollapse = true;
    [SerializeField, Min(0f)] private float colliderDisableDelay = 0.05f;
    [SerializeField] private bool autoRecover;
    [SerializeField, Min(0.5f)] private float recoverDelay = 4f;

    readonly List<Vector3> _cachedLocalPositions = new List<Vector3>();
    readonly List<Quaternion> _cachedLocalRotations = new List<Quaternion>();
    readonly List<Rigidbody> _rigidbodies = new List<Rigidbody>();
    readonly List<Collider> _colliders = new List<Collider>();

    Coroutine _collapseRoutine;

    void Awake()
    {
        CacheFloorPieces();
        enabled = false;
    }

    public void ConfigureRuntime(Transform[] runtimePieces, float delay, float shakeTime)
    {
        floorPieces = runtimePieces;
        collapseDelay = delay;
        shakeDuration = shakeTime;
        CacheFloorPieces();
    }

    public void TriggerCollapse()
    {
        if (_collapseRoutine != null)
            StopCoroutine(_collapseRoutine);

        _collapseRoutine = StartCoroutine(CoCollapse());
    }

    void CacheFloorPieces()
    {
        _cachedLocalPositions.Clear();
        _cachedLocalRotations.Clear();
        _rigidbodies.Clear();
        _colliders.Clear();

        Transform[] resolvedPieces = floorPieces != null && floorPieces.Length > 0
            ? floorPieces
            : new[] { transform };

        floorPieces = resolvedPieces;

        for (int i = 0; i < floorPieces.Length; i++)
        {
            Transform piece = floorPieces[i];
            if (piece == null)
                continue;

            _cachedLocalPositions.Add(piece.localPosition);
            _cachedLocalRotations.Add(piece.localRotation);

            Rigidbody body = piece.GetComponent<Rigidbody>();
            if (body != null)
            {
                body.isKinematic = true;
                body.useGravity = false;
                _rigidbodies.Add(body);
            }

            Collider collider = piece.GetComponent<Collider>();
            if (collider != null)
                _colliders.Add(collider);
        }
    }

    IEnumerator CoCollapse()
    {
        yield return new WaitForSeconds(collapseDelay);

        float elapsed = 0f;
        while (elapsed < shakeDuration)
        {
            elapsed += Time.deltaTime;

            for (int i = 0; i < floorPieces.Length; i++)
            {
                Transform piece = floorPieces[i];
                if (piece == null)
                    continue;

                Vector3 offset = new Vector3(
                    Random.Range(-shakeAmplitude.x, shakeAmplitude.x),
                    Random.Range(-shakeAmplitude.y, shakeAmplitude.y),
                    Random.Range(-shakeAmplitude.z, shakeAmplitude.z));

                piece.localPosition = _cachedLocalPositions[Mathf.Min(i, _cachedLocalPositions.Count - 1)] + offset;
            }

            yield return null;
        }

        for (int i = 0; i < floorPieces.Length; i++)
        {
            Transform piece = floorPieces[i];
            if (piece == null)
                continue;

            piece.localPosition = _cachedLocalPositions[Mathf.Min(i, _cachedLocalPositions.Count - 1)];
            piece.localRotation = _cachedLocalRotations[Mathf.Min(i, _cachedLocalRotations.Count - 1)];
        }

        if (colliderDisableDelay <= 0f)
        {
            DisableColliders();
        }
        else
        {
            yield return new WaitForSeconds(colliderDisableDelay);
            DisableColliders();
        }

        if (enableRigidbodiesOnCollapse)
        {
            for (int i = 0; i < _rigidbodies.Count; i++)
            {
                Rigidbody body = _rigidbodies[i];
                if (body == null)
                    continue;

                body.isKinematic = false;
                body.useGravity = true;
            }
        }

        if (autoRecover)
        {
            yield return new WaitForSeconds(recoverDelay);
            Recover();
        }

        _collapseRoutine = null;
    }

    void DisableColliders()
    {
        for (int i = 0; i < _colliders.Count; i++)
        {
            if (_colliders[i] != null)
                _colliders[i].enabled = false;
        }
    }

    void Recover()
    {
        for (int i = 0; i < floorPieces.Length; i++)
        {
            Transform piece = floorPieces[i];
            if (piece == null)
                continue;

            piece.localPosition = _cachedLocalPositions[Mathf.Min(i, _cachedLocalPositions.Count - 1)];
            piece.localRotation = _cachedLocalRotations[Mathf.Min(i, _cachedLocalRotations.Count - 1)];
        }

        for (int i = 0; i < _rigidbodies.Count; i++)
        {
            Rigidbody body = _rigidbodies[i];
            if (body == null)
                continue;

            body.velocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.useGravity = false;
            body.isKinematic = true;
        }

        for (int i = 0; i < _colliders.Count; i++)
        {
            if (_colliders[i] != null)
                _colliders[i].enabled = true;
        }
    }
}
