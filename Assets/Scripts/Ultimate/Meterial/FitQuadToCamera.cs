using UnityEngine;
[ExecuteAlways]
public class FitQuadToCamera : MonoBehaviour
{
    [SerializeField] Camera cam; [SerializeField] Transform quad; [SerializeField] float z = 1f;
    Transform _lastParent;
    float _lastZ = float.NaN;
    float _lastFov = float.NaN;
    float _lastAspect = float.NaN;

    void Reset() { if (!cam) cam = Camera.main; }
    void LateUpdate()
    {
        if (!cam || !quad) return;

        if (_lastParent != cam.transform || quad.parent != cam.transform)
        {
            quad.SetParent(cam.transform, true);
            _lastParent = cam.transform;
        }

        Vector3 targetLocalPosition = new Vector3(0f, 0f, z);
        if ((quad.localPosition - targetLocalPosition).sqrMagnitude > 1e-8f || !Mathf.Approximately(_lastZ, z))
        {
            quad.localPosition = targetLocalPosition;
            _lastZ = z;
        }

        if (quad.localRotation != Quaternion.identity)
            quad.localRotation = Quaternion.identity;

        float h = 2f * z * Mathf.Tan(0.5f * cam.fieldOfView * Mathf.Deg2Rad);
        float w = h * cam.aspect;
        Vector3 targetLocalScale = new Vector3(w, h, 1f);
        if (!Mathf.Approximately(_lastFov, cam.fieldOfView) ||
            !Mathf.Approximately(_lastAspect, cam.aspect) ||
            (quad.localScale - targetLocalScale).sqrMagnitude > 1e-8f)
        {
            quad.localScale = targetLocalScale;
            _lastFov = cam.fieldOfView;
            _lastAspect = cam.aspect;
        }
    }
}
