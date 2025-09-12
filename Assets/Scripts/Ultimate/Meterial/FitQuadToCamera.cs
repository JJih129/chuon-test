using UnityEngine;
[ExecuteAlways]
public class FitQuadToCamera : MonoBehaviour
{
    [SerializeField] Camera cam; [SerializeField] Transform quad; [SerializeField] float z = 1f;
    void Reset() { if (!cam) cam = Camera.main; }
    void LateUpdate()
    {
        if (!cam || !quad) return;
        quad.SetParent(cam.transform, true);
        quad.localPosition = new Vector3(0, 0, z);
        quad.localRotation = Quaternion.identity;
        float h = 2f * z * Mathf.Tan(0.5f * cam.fieldOfView * Mathf.Deg2Rad);
        float w = h * cam.aspect;
        quad.localScale = new Vector3(w, h, 1f);
    }
}
