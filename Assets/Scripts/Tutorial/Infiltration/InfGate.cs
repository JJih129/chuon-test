using System.Collections;
using UnityEngine;

// 침투 튜토리얼 게이트: 차단기 팔(회전), 시뮬레이션 차단막(디졸브 후 비활성), 막는 콜라이더를 한 번에 연다.
// 닫힘 = 붉은 표시등, 열림 = 앰버 (색 언어: 빨강 = 금지/위험, 앰버 = 통로).
[DisallowMultipleComponent]
public sealed class InfGate : MonoBehaviour
{
    [SerializeField] Transform[] arms = System.Array.Empty<Transform>();
    [Tooltip("팔을 올릴 로컬 Z 회전 (도). 팔마다 부호를 바꿔 서로 반대로 올린다.")]
    [SerializeField] float[] armOpenAngles = System.Array.Empty<float>();
    [SerializeField] GameObject[] blockers = System.Array.Empty<GameObject>();
    [SerializeField] Renderer[] fadeOnOpen = System.Array.Empty<Renderer>();
    [SerializeField] Renderer[] statusLights = System.Array.Empty<Renderer>();
    [SerializeField] Color closedColor = new Color(1f, 0.08f, 0.06f) * 3f;
    [SerializeField] Color openColor = new Color(1f, 0.55f, 0.1f) * 3f;
    [SerializeField, Min(0.05f)] float openDuration = 1.2f;
    [SerializeField] AudioClip openSfx;

    static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    MaterialPropertyBlock _mpb;
    Quaternion[] _armClosed;
    public bool IsOpen { get; private set; }

    public void Configure(Transform[] armTransforms, float[] angles, GameObject[] blockerObjects, Renderer[] fades, Renderer[] lights)
    {
        arms = armTransforms ?? System.Array.Empty<Transform>();
        armOpenAngles = angles ?? System.Array.Empty<float>();
        blockers = blockerObjects ?? System.Array.Empty<GameObject>();
        fadeOnOpen = fades ?? System.Array.Empty<Renderer>();
        statusLights = lights ?? System.Array.Empty<Renderer>();
    }

    void Awake()
    {
        _armClosed = new Quaternion[arms.Length];
        for (int i = 0; i < arms.Length; i++)
            _armClosed[i] = arms[i] != null ? arms[i].localRotation : Quaternion.identity;
        SetLights(closedColor);
    }

    public void Close()
    {
        StopAllCoroutines();
        IsOpen = false;
        for (int i = 0; i < arms.Length; i++)
            if (arms[i] != null) arms[i].localRotation = _armClosed[i];
        foreach (GameObject b in blockers)
            if (b != null) b.SetActive(true);
        foreach (Renderer r in fadeOnOpen)
            if (r != null) { r.enabled = true; SetAlpha(r, 1f); }
        SetLights(closedColor);
    }

    public void Open()
    {
        if (IsOpen)
            return;
        IsOpen = true;
        foreach (GameObject b in blockers)
            if (b != null) b.SetActive(false);
        SetLights(openColor);
        if (openSfx != null)
            OneShotAudio.Play2D(openSfx, 0.6f);
        if (isActiveAndEnabled)
            StartCoroutine(CoOpen());
        else
            FinishOpen();
    }

    IEnumerator CoOpen()
    {
        float t = 0f;
        while (t < openDuration)
        {
            t += Time.deltaTime;
            float k = Mathf.SmoothStep(0f, 1f, t / openDuration);
            for (int i = 0; i < arms.Length; i++)
            {
                if (arms[i] == null) continue;
                float a = i < armOpenAngles.Length ? armOpenAngles[i] : 80f;
                arms[i].localRotation = _armClosed[i] * Quaternion.Euler(0f, 0f, a * k);
            }
            foreach (Renderer r in fadeOnOpen)
                if (r != null) SetAlpha(r, 1f - k);
            yield return null;
        }
        FinishOpen();
    }

    void FinishOpen()
    {
        for (int i = 0; i < arms.Length; i++)
        {
            if (arms[i] == null) continue;
            float a = i < armOpenAngles.Length ? armOpenAngles[i] : 80f;
            arms[i].localRotation = _armClosed[i] * Quaternion.Euler(0f, 0f, a);
        }
        foreach (Renderer r in fadeOnOpen)
            if (r != null) r.enabled = false;
    }

    void SetAlpha(Renderer r, float a)
    {
        _mpb ??= new MaterialPropertyBlock();
        r.GetPropertyBlock(_mpb);
        Material m = r.sharedMaterial;
        Color c = m != null && m.HasProperty(BaseColorId) ? m.GetColor(BaseColorId) : Color.white;
        c.a *= a;
        _mpb.SetColor(BaseColorId, c);
        r.SetPropertyBlock(_mpb);
    }

    void SetLights(Color c)
    {
        _mpb ??= new MaterialPropertyBlock();
        foreach (Renderer r in statusLights)
        {
            if (r == null) continue;
            r.GetPropertyBlock(_mpb);
            _mpb.SetColor(EmissionId, c);
            _mpb.SetColor(BaseColorId, new Color(Mathf.Clamp01(c.r), Mathf.Clamp01(c.g), Mathf.Clamp01(c.b), 1f));
            r.SetPropertyBlock(_mpb);
        }
    }
}
