#if UNITY_EDITOR || DEVELOPMENT_BUILD
// 개발/QA 전용: 릴리스 빌드에서는 컴파일되지 않는다.
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Combat;
using UnityEngine;

// QA 전용 (반복 10): 플레이 모드에서 MCP 로 붙여 쓰는 시각 검증 도구. 게임 코드에서 참조하지 않는다.
//  - ComboRoute("LLLHH"): 플레이어 콤보를 입력하고 각 타의 히트 윈도우 동안 여러 프레임을 렌더 + 칼날/리본/검기 방향 수치 기록
//  - BossCues(n): 보스 큐마다 (큐 순간 / 예비동작 중 / 피격 순간) 게임 카메라 렌더 + 헬멧 발광 수준 기록
public sealed class QaVisualCapture : MonoBehaviour
{
    public string outDir;
    public string prefix = "qa";
    public int width = 1280;
    public int height = 720;
    public readonly StringBuilder Log = new StringBuilder();
    public bool Busy { get; private set; }
    public int Shots { get; private set; }

    public static QaVisualCapture Create(string dir, string prefix)
    {
        var go = new GameObject("QaVisualCapture");
        var c = go.AddComponent<QaVisualCapture>();
        c.outDir = dir;
        c.prefix = prefix;
        Directory.CreateDirectory(dir);
        return c;
    }

    // ───────── 플레이어 콤보 ─────────
    public void ComboRoute(string route, float pressInterval, bool sideCamera)
    {
        StartCoroutine(CoCombo(route, pressInterval, sideCamera));
    }

    IEnumerator CoCombo(string route, float pressInterval, bool sideCamera)
    {
        Busy = true;
        PlayerReferences refs = FindObjectOfType<PlayerReferences>();
        PlayerInputCommandBuffer input = refs != null ? refs.GetComponentInChildren<PlayerInputCommandBuffer>(true) : null;
        PlayerAttackVfxPresenter vfx = refs != null ? refs.GetComponentInChildren<PlayerAttackVfxPresenter>(true) : null;
        SwordTrailMeshRenderer trail = refs != null ? refs.GetComponentInChildren<SwordTrailMeshRenderer>(true) : null;
        SwordTrailPoints points = refs != null ? refs.GetComponentInChildren<SwordTrailPoints>(true) : null;
        if (input == null || vfx == null)
        {
            Log.AppendLine("combo: missing player refs");
            Busy = false;
            yield break;
        }

        int slashBefore = vfx.QaSlashSpawnCount;
        // 고정 카메라 (경로 시작 시 플레이어 기준) + 게임 카메라 둘 다
        Transform pr = refs.transform;
        Vector3 fixedFocus = pr.position + pr.forward * 0.8f + Vector3.up * 1.2f;
        Vector3 fixedEye = fixedFocus + pr.right * 3.4f + pr.forward * 1.2f + Vector3.up * 0.5f;
        var endOfFrame = new WaitForEndOfFrame();
        for (int i = 0; i < route.Length; i++)
        {
            if (route[i] == 'H') input.RecordAttackHeavyPress(); else input.RecordAttackLightPress();
            float until = Time.time + pressInterval;
            int framesThisHit = 0;
            bool wasEmitting = false;
            while (Time.time < until)
            {
                bool emitting = trail != null && trail.IsEmitting;
                if (emitting && framesThisHit < 3 && trail.SampleCount >= 3 + framesThisHit * 3)
                {
                    string tag = route + "_" + (i + 1) + route[i] + "_f" + framesThisHit;
                    yield return endOfFrame; // LateUpdate(리본 샘플) 이후 = 실제 화면과 같은 상태
                    if (sideCamera)
                        RenderCamera(fixedEye, fixedFocus, 46f, tag);
                    RenderMain(tag + "_game");
                    if (points != null && trail.TryGetNewestEdge(out Vector3 eb, out Vector3 et))
                    {
                        Vector3 blade = points.TrailTip.position - points.TrailBase.position;
                        float tipErr = Vector3.Distance(et, points.TrailTip.position + blade * 0f);
                        float onAxis = Vector3.Cross(blade.normalized, (et - points.TrailBase.position)).magnitude;
                        Log.Append(tag).Append(" weight=").Append(trail.StrikeWeight.ToString("F2"))
                           .Append(" ribbonTip->blade-axis offset=").Append(onAxis.ToString("F3"))
                           .Append(" samples=").Append(trail.SampleCount).AppendLine();
                    }
                    framesThisHit++;
                }
                if (wasEmitting && !emitting && vfx.QaSlashSpawnCount > slashBefore)
                {
                    slashBefore = vfx.QaSlashSpawnCount;
                    Vector3 v = vfx.QaLastSwingVelocity;
                    float align = v.sqrMagnitude > 1e-6f ? Vector3.Dot(vfx.QaLastSlashRight, v.normalized) : 0f;
                    Log.Append(route).Append(" hit").Append(i + 1).Append(route[i])
                       .Append(" slash: arcRight·tipVel=").Append(align.ToString("F2"))
                       .Append(" normal=").Append(vfx.QaLastSwingNormal.ToString("F2"))
                       .Append(" swing=").Append(DescribeSwing(v, refs.transform)).AppendLine();
                }
                wasEmitting = emitting;
                yield return null;
            }
        }
        Busy = false;
    }

    static string DescribeSwing(Vector3 v, Transform player)
    {
        if (v.sqrMagnitude < 1e-6f || player == null)
            return "static";
        Vector3 local = player.InverseTransformDirection(v.normalized);
        string h = local.x > 0.35f ? "L→R" : local.x < -0.35f ? "R→L" : "";
        string vtx = local.y < -0.45f ? "down" : local.y > 0.45f ? "up" : "";
        return (h + " " + vtx).Trim();
    }

    void RenderPlayer(Transform player, bool side, string tag)
    {
        Vector3 focus = player.position + Vector3.up * 1.15f;
        Vector3 eye = side
            ? focus + player.right * 2.6f + player.forward * 1.6f + Vector3.up * 0.4f
            : focus - player.forward * 3.0f + player.right * 0.9f + Vector3.up * 0.7f;
        RenderCamera(eye, focus, 42f, tag);
    }

    // ───────── 보스 큐 ─────────
    public void BossCues(int count)
    {
        StartCoroutine(CoBossCues(count));
    }

    IEnumerator CoBossCues(int count)
    {
        Busy = true;
        BossController boss = FindObjectOfType<BossController>();
        if (boss == null)
        {
            Busy = false;
            yield break;
        }
        var queue = new Queue<(BossAttackTelegraphType, float, float)>();
        System.Action<BossAttackTelegraphType, float, BossAttackTimingCueFlags> h = (t, d, f) =>
        {
            if ((f & (BossAttackTimingCueFlags.BodyFlash | BossAttackTimingCueFlags.WarningSound)) != 0 || d > 0.25f)
                queue.Enqueue((t, d, Time.time));
        };
        boss.OnAttackTimingCue += h;
        int done = 0;
        float giveUp = Time.time + 60f;
        BossTelegraphVfxPresenter presenter = boss.GetComponentInChildren<BossTelegraphVfxPresenter>(true);
        while (done < count && Time.time < giveUp)
        {
            if (queue.Count == 0)
            {
                yield return null;
                continue;
            }
            var (type, dur, at) = queue.Dequeue();
            string baseTag = "cue" + done + "_" + type;
            RenderMain(baseTag + "_a_cue");
            yield return null;
            yield return null;
            RenderOverview(boss.transform, baseTag + "_a2_flash_top");
            if (presenter == null) presenter = boss.GetComponentInChildren<BossTelegraphVfxPresenter>(true);
            Log.Append(baseTag).Append(" dur=").Append(dur.ToString("F2")).Append(" sfx=").Append(presenter != null ? presenter.LastCueSfx : "?");
            float mid = at + dur * 0.65f;
            while (Time.time < mid) yield return null;
            RenderMain(baseTag + "_b_anticipation");
            RenderOverview(boss.transform, baseTag + "_b2_top");
            Log.Append(" visor@65%=").Append(presenter != null ? presenter.CurrentVisorLevel.ToString("F2") : "?");
            while (Time.time < at + dur) yield return null;
            RenderMain(baseTag + "_c_impact");
            Log.Append(" visor@hit=").Append(presenter != null ? presenter.CurrentVisorLevel.ToString("F2") : "?").AppendLine();
            done++;
            queue.Clear();
        }
        boss.OnAttackTimingCue -= h;
        Busy = false;
    }

    // 바닥 데칼 확인용 높은 각도
    public void RenderOverview(Transform boss, string tag)
    {
        Vector3 focus = boss.position + boss.forward * 1.6f;
        Vector3 eye = focus + boss.right * 3.2f + boss.forward * 3.6f + Vector3.up * 5.2f;
        RenderCamera(eye, focus + Vector3.up * 0.6f, 50f, tag);
    }

    // ───────── 포즈 (장갑 관통 검사) ─────────
    public static string Pose(string state, float normalizedTime)
    {
        BossController boss = FindObjectOfType<BossController>();
        if (boss == null)
            return "no boss";
        boss.enabled = false;
        Animator an = boss.GetComponent<Animator>();
        an.speed = 0f;
        an.Play(state, 0, normalizedTime);
        an.Update(0f);
        return "pose " + state + " " + normalizedTime;
    }

    // ───────── 렌더 ─────────
    public void RenderMain(string tag)
    {
        Camera cam = Camera.main;
        if (cam == null)
            return;
        RenderTexture prev = cam.targetTexture;
        var rt = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.DefaultHDR);
        cam.targetTexture = rt;
        cam.Render();
        cam.targetTexture = prev;
        Save(rt, tag);
        RenderTexture.ReleaseTemporary(rt);
    }

    public void RenderCamera(Vector3 eye, Vector3 target, float fov, string tag)
    {
        Camera main = Camera.main;
        var go = new GameObject("_qaCam");
        var cam = go.AddComponent<Camera>();
        if (main != null)
        {
            cam.CopyFrom(main);
            var src = main.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            var dst = go.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>() ?? go.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            if (src != null) { dst.renderPostProcessing = src.renderPostProcessing; dst.antialiasing = src.antialiasing; }
        }
        cam.transform.position = eye;
        cam.transform.LookAt(target);
        cam.fieldOfView = fov;
        cam.nearClipPlane = 0.05f;
        var rt = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.DefaultHDR);
        cam.targetTexture = rt;
        cam.Render();
        cam.targetTexture = null;
        Save(rt, tag);
        RenderTexture.ReleaseTemporary(rt);
        Destroy(go);
    }

    void Save(RenderTexture hdr, string tag)
    {
        var ldr = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        Graphics.Blit(hdr, ldr);
        RenderTexture prevActive = RenderTexture.active;
        RenderTexture.active = ldr;
        var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        tex.Apply();
        RenderTexture.active = prevActive;
        RenderTexture.ReleaseTemporary(ldr);
        File.WriteAllBytes(Path.Combine(outDir, prefix + "_" + tag + ".png"), tex.EncodeToPNG());
        Destroy(tex);
        Shots++;
    }
}
#endif
