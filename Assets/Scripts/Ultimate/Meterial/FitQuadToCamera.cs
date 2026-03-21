using UnityEngine;
[ExecuteAlways]
public class FitQuadToCamera : MonoBehaviour
{
    [SerializeField] Camera cam; [SerializeField] Transform quad; [SerializeField] float z = 1f;
    Vector3 _lastCamPosition;
    Quaternion _lastCamRotation;
    float _lastFieldOfView = -1f;
    float _lastAspect = -1f;
    float _lastZ = float.NaN;
    bool _cacheValid;
    void Reset() { if (!cam) cam = Camera.main; }
    void LateUpdate()
    {
        if (!cam || !quad) return;
        if (!quad.gameObject.activeInHierarchy || !cam.isActiveAndEnabled) return;

        bool unchanged = _cacheValid
            && _lastCamPosition == cam.transform.position
            && _lastCamRotation == cam.transform.rotation
            && Mathf.Approximately(_lastFieldOfView, cam.fieldOfView)
            && Mathf.Approximately(_lastAspect, cam.aspect)
            && Mathf.Approximately(_lastZ, z)
            && quad.parent == cam.transform;
        if (unchanged)
            return;

        quad.SetParent(cam.transform, true);
        quad.localPosition = new Vector3(0, 0, z);
        quad.localRotation = Quaternion.identity;
        float h = 2f * z * Mathf.Tan(0.5f * cam.fieldOfView * Mathf.Deg2Rad);
        float w = h * cam.aspect;
        quad.localScale = new Vector3(w, h, 1f);

        _lastCamPosition = cam.transform.position;
        _lastCamRotation = cam.transform.rotation;
        _lastFieldOfView = cam.fieldOfView;
        _lastAspect = cam.aspect;
        _lastZ = z;
        _cacheValid = true;
    }
}
