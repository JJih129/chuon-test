// 처형 카메라 샷 리스트 평가기 (순수 계산 + 물리 질의만, MonoBehaviour 아님).
// 런타임(ExecutionCamChoreographer)과 에디터 미리보기(ExecutionCamPreviewTool)가 같은 코드를 쓴다.
// 프레이밍 규칙: 두 인물 머리·가슴이 화면 안, 가슴~머리 높이 시선, 카메라 최저 높이, 올려보기 상한, 더치 ≤ 8°,
//               지오메트리 관통 금지(구체 캐스트 + 대체 각도 → 안전 샷), 인물 몸 안으로 들어가지 않음.
using System.Collections.Generic;
using UnityEngine;

public struct ExecCamContext
{
    public Vector3 attackerFeet;
    public Vector3 victimFeet;
    public float attackerHeight;
    public float victimHeight;
    public float victimRadius;
    public Vector3 impactPoint;
    public float side;              // +1 / −1 (오른쪽/왼쪽)
    public float aspect;
    public bool victimSurvives;     // 복귀 시 락온 구도
    public bool bigVictim;
    public LayerMask collisionMask;
    public Vector3 axis;            // 시작 시 고정한 A→V 수평 축 (공격자가 움직여도 리그 공간이 돌지 않게). 0 이면 발 위치로 계산

    // 게임플레이 복귀 구도 (SoulsCameraProfile 값)
    public float gpPivotHeight;
    public float gpDistance;
    public float gpLockDistance;
    public float gpPitch;
    public float gpLockPitch;
    public float gpFov;
    public float gpShoulder;
    public float gpLookBias;

    public Vector3 Forward
    {
        get
        {
            if (axis.sqrMagnitude > 0.5f)
                return axis;
            Vector3 f = victimFeet - attackerFeet;
            f.y = 0f;
            return f.sqrMagnitude > 0.0001f ? f.normalized : Vector3.forward;
        }
    }

    public Vector3 Right => Vector3.Cross(Vector3.up, Forward) * (side >= 0f ? 1f : -1f);
    public Vector3 TrueRight => Vector3.Cross(Vector3.up, Forward);
    public float GroundY => Mathf.Min(attackerFeet.y, victimFeet.y);
    public Vector3 AttackerChest => attackerFeet + Vector3.up * Mathf.Clamp(attackerHeight * 0.74f, 1.0f, 1.6f);
    public Vector3 AttackerHead => attackerFeet + Vector3.up * Mathf.Max(1.2f, attackerHeight * 0.95f);
    public Vector3 VictimChest => victimFeet + Vector3.up * Mathf.Max(1.0f, victimHeight * 0.66f);
    public Vector3 VictimHead => victimFeet + Vector3.up * Mathf.Max(1.2f, victimHeight * 0.93f);

    public void ApplyProfile(SoulsCameraProfile p)
    {
        gpPivotHeight = p != null ? p.pivotHeight : 1.55f;
        gpDistance = p != null ? p.distance : 3.8f;
        gpLockDistance = p != null ? p.lockDistance : 3.8f;
        gpPitch = p != null ? p.defaultPitch : 10f;
        gpLockPitch = p != null ? p.lockPitch : 10f;
        gpFov = p != null ? p.fieldOfView : 50f;
        gpShoulder = p != null ? p.lockShoulderFar : 0.45f;
        gpLookBias = p != null ? p.lockLookTargetBias : 0.65f;
        if (bigVictim && p != null)
        {
            gpLockDistance += p.bigTargetExtraDistance;
            gpPivotHeight += p.bigTargetExtraPivot;
        }
    }
}

public struct ExecCamOutput
{
    public Vector3 position;
    public Quaternion rotation;
    public Vector3 look;
    public float fov;
    public float dutch;
    public bool valid;
}

public sealed class ExecCamTimeline
{
    ExecutionCamLibrary _lib;
    ExecCamContext _ctx;
    readonly List<ExecCamShot> _shots = new List<ExecCamShot>(8);
    int _current = -1;
    ExecCamOutput _shotStart;
    ExecCamOutput _last;
    float _shotAzOffset;
    float _shotAzSign = 1f;
    float _shotFit = 1f;
    bool _shotSafe;
    float _entryElapsed;
    float _allowedDistance = -1f;
    float _allowedVelocity;
    bool _cutThisFrame;

    // QA
    public string VariantName { get; private set; } = "";
    public string CurrentShotName => _current >= 0 && _current < _shots.Count ? _shots[_current].name : "";
    public int ShotIndex => _current;
    public ExecCamShot CurrentShot => _current >= 0 && _current < _shots.Count ? _shots[_current] : null;
    public int FallbackCount { get; private set; }
    public int SafeShotCount { get; private set; }
    public bool CutThisFrame => _cutThisFrame;
    public ExecCamContext Context => _ctx;

    static readonly RaycastHit[] s_hits = new RaycastHit[24];
    static readonly Collider[] s_overlaps = new Collider[12];
    static readonly Dictionary<int, bool> s_ignoreCache = new Dictionary<int, bool>(256);

    public void Begin(ExecutionCamLibrary lib, ExecCamVariant variant, ExecCamContext ctx, ExecCamOutput initial)
    {
        _lib = lib;
        _ctx = ctx;
        _shots.Clear();
        if (variant != null && variant.shots != null)
            for (int i = 0; i < variant.shots.Count; i++)
                if (variant.shots[i] != null)
                    _shots.Add(variant.shots[i]);
        _shots.Sort((a, b) => a.phase != b.phase ? a.phase.CompareTo(b.phase) : a.start.CompareTo(b.start));
        VariantName = variant != null ? variant.name : "";
        _current = -1;
        _last = initial;
        _shotStart = initial;
        _allowedDistance = -1f;
        _allowedVelocity = 0f;
        _rescue = 0f;
        FallbackCount = 0;
        SafeShotCount = 0;
    }

    /// <summary>피격 지점(코어 뼈)이 움직이면 갱신.</summary>
    public void UpdateImpactPoint(Vector3 p) => _ctx.impactPoint = p;

    /// <summary>공격자 발 위치(루트 모션) 갱신. 축(axis)은 고정이라 리그 공간은 돌지 않는다.</summary>
    public void UpdateAttackerFeet(Vector3 p) => _ctx.attackerFeet = p;

    public ExecCamOutput Evaluate(ExecCamPhase phase, float t01, float dt)
    {
        _cutThisFrame = false;
        if (_shots.Count == 0)
            return _last;
        int idx = FindShot(phase, t01);
        if (idx != _current)
            Activate(idx);
        else
            _entryElapsed += dt;

        ExecCamShot shot = _shots[_current];
        float k = shot.end > shot.start + 0.0001f ? Mathf.Clamp01((t01 - shot.start) / (shot.end - shot.start)) : 1f;
        if (shot.phase != phase)
            k = shot.phase < phase ? 1f : 0f;
        float e = Ease(shot.ease, k);

        ExecCamOutput raw = Raw(shot, e);

        // 진입 휩/블렌드
        if (shot.mode == ExecCamMode.Orbit && shot.entry != ExecCamEntry.Cut && shot.entryTime > 0f && _shotStart.valid)
        {
            float w = Mathf.Clamp01(_entryElapsed / shot.entryTime);
            w = shot.entry == ExecCamEntry.Whip ? SmootherStep(w) : Mathf.SmoothStep(0f, 1f, w);
            if (w < 1f)
                raw = Blend(_shotStart, raw, w);
        }

        ExecCamOutput o = Constrain(raw, dt);
        _last = o;
        return o;
    }

    int FindShot(ExecCamPhase phase, float t)
    {
        int best = -1;
        for (int i = 0; i < _shots.Count; i++)
        {
            ExecCamShot s = _shots[i];
            if (s.phase < phase)
            {
                best = i;   // 이전 단계의 마지막 샷 (단계에 샷이 없으면 유지)
                continue;
            }
            if (s.phase > phase)
                break;
            if (t >= s.start - 0.0001f)
                best = i;
        }
        if (best < 0)
            best = 0;
        // 현재 샷보다 뒤로 가지 않는다 (예상 시각 오차로 되감기 방지)
        return Mathf.Max(best, _current);
    }

    void Activate(int idx)
    {
        _current = idx;
        _entryElapsed = 0f;
        _shotStart = _last;
        ExecCamShot shot = _shots[idx];
        bool cut = shot.mode == ExecCamMode.Orbit && shot.entry == ExecCamEntry.Cut;
        _cutThisFrame = cut;
        if (cut)
        {
            _allowedDistance = -1f;  // 컷이면 충돌 거리 즉시 재설정
            _rescue = -1f;           // 화면 이탈 보정도 즉시 재계산
        }
        _shotAzOffset = 0f;
        _shotAzSign = 1f;
        _shotFit = 1f;
        _shotSafe = false;
        if (shot.mode == ExecCamMode.Orbit || shot.mode == ExecCamMode.FromCurrent)
            SolveShot(shot);
    }

    // ------------------------------------------------------------------ 샷별 대체 각도·프레이밍 맞춤

    static readonly float[] s_offsets = { 0f, 20f, -20f, 40f, -40f };

    void SolveShot(ExecCamShot shot)
    {
        int bestBlocked = int.MaxValue;
        float bestOffset = 0f, bestSign = 1f, bestFit = 1f;
        bool fromCurrent = shot.mode == ExecCamMode.FromCurrent;
        for (int pass = 0; pass < 2; pass++)
        {
            float sign = pass == 0 ? 1f : -1f;   // 2차: 좌우 반전 (축 넘기기 — 휩/컷일 때만 자연스러움)
            if (pass == 1 && fromCurrent)
                break;
            for (int i = 0; i < s_offsets.Length; i++)
            {
                float off = s_offsets[i];
                float fit = ComputeFit(shot, off, sign);
                int blocked = CountBlocked(shot, off, sign, fit, fromCurrent);
                if (blocked < bestBlocked)
                {
                    bestBlocked = blocked;
                    bestOffset = off;
                    bestSign = sign;
                    bestFit = fit;
                }
                if (blocked == 0)
                    goto done;
            }
        }
        done:
        _shotAzOffset = bestOffset;
        _shotAzSign = bestSign;
        _shotFit = bestFit;
        if (bestOffset != 0f || bestSign < 0f)
            FallbackCount++;
        if (bestBlocked >= 2 && shot.pivot == ExecCamPivot.Pair)
        {
            _shotSafe = true;
            SafeShotCount++;
        }
    }

    float ComputeFit(ExecCamShot shot, float off, float sign)
    {
        float maxFit = _lib != null ? Mathf.Max(1f, _lib.maxFitScale) : 1.7f;
        for (float s = 1f; s <= maxFit + 0.001f; s += 0.1f)
        {
            bool ok = true;
            for (int j = 0; j < 3 && ok; j++)
            {
                float e = j * 0.5f;
                if (shot.mode == ExecCamMode.FromCurrent && j < 2)
                    continue;
                ExecCamOutput p = OrbitPose(shot, shot.from, shot.to, e, off, sign, s);
                ok = InFrame(p, shot);
            }
            if (ok)
                return s;
        }
        return maxFit;
    }

    int CountBlocked(ExecCamShot shot, float off, float sign, float fit, bool targetOnly)
    {
        int n = 0;
        for (int j = 0; j < 3; j++)
        {
            if (targetOnly && j < 2)
                continue;
            ExecCamOutput p = OrbitPose(shot, shot.from, shot.to, j * 0.5f, off, sign, fit);
            p = ApplyStaticRules(p);
            if (IsBlocked(p, shot.keepBoth))
                n++;
        }
        return n;
    }

    /// <summary>변형 선택용: 이 변형의 궤도 샷들이 막힌 표본 수 (공간 점수, 낮을수록 좋음).</summary>
    public static int ScoreVariant(ExecutionCamLibrary lib, ExecCamVariant v, ExecCamContext ctx)
    {
        var tl = new ExecCamTimeline { _lib = lib, _ctx = ctx };
        int n = 0;
        if (v == null || v.shots == null)
            return 999;
        for (int i = 0; i < v.shots.Count; i++)
        {
            ExecCamShot s = v.shots[i];
            if (s == null || s.mode != ExecCamMode.Orbit)
                continue;
            float fit = tl.ComputeFit(s, 0f, 1f);
            n += tl.CountBlocked(s, 0f, 1f, fit, false);
        }
        return n;
    }

    // ------------------------------------------------------------------ 포즈

    ExecCamOutput Raw(ExecCamShot shot, float e)
    {
        switch (shot.mode)
        {
            case ExecCamMode.FromCurrent:
            {
                ExecCamOutput target = OrbitPose(shot, shot.to, shot.to, 1f, _shotAzOffset, _shotAzSign, _shotFit);
                return _shotStart.valid ? Blend(_shotStart, target, e) : target;
            }
            case ExecCamMode.PushInCurrent:
            {
                if (!_shotStart.valid)
                    return OrbitPose(shot, Fallback(), Fallback(), 0f, 0f, 1f, 1f);
                Vector3 startLook = _shotStart.look;
                Vector3 offs = _shotStart.position - startLook;
                float startDist = Mathf.Max(0.5f, offs.magnitude);
                Vector3 dir = offs / startDist;
                Vector3 newLook = LookPoint(shot, shot.to.lookBias);
                Vector3 look = Vector3.Lerp(startLook, newLook, e);
                float dist = startDist * Mathf.Lerp(1f, Mathf.Max(0.3f, shot.to.distance), e);
                Vector3 pos = look + dir * dist;
                return new ExecCamOutput
                {
                    position = pos,
                    look = look,
                    rotation = LookRot(look - pos),
                    fov = _shotStart.fov + shot.to.fov * e,
                    dutch = _shotStart.dutch + shot.to.dutch * e,
                    valid = true
                };
            }
            case ExecCamMode.ReturnToGameplay:
            {
                ExecCamOutput target = GameplayPose();
                return _shotStart.valid ? ArcBlendAroundAttacker(_shotStart, target, e) : target;
            }
            default:
                if (_shotSafe)
                {
                    ExecCamPose safe = Fallback();
                    return OrbitPose(shot, safe, safe, e, 0f, 1f, 1f, 1.5f);
                }
                return OrbitPose(shot, shot.from, shot.to, e, _shotAzOffset, _shotAzSign, _shotFit);
        }
    }

    static ExecCamPose Fallback() => new ExecCamPose(22f, 20f, 3.4f, 46f, 0f, 0.35f, 0.6f);

    Vector3 LookPoint(ExecCamShot shot, float lookBias)
    {
        Vector3 victimLook = shot.lookImpact ? _ctx.impactPoint : _ctx.VictimChest;
        Vector3 look = Vector3.Lerp(_ctx.AttackerChest, victimLook, Mathf.Clamp01(lookBias));
        float minY = _ctx.attackerFeet.y + (_lib != null ? _lib.minLookHeight : 1.0f);
        if (look.y < minY)
            look.y = minY;
        return look;
    }

    ExecCamOutput OrbitPose(ExecCamShot shot, ExecCamPose a, ExecCamPose b, float e, float azOffset, float azSign, float fit, float pivotHeightOverride = -1f)
    {
        float az = Mathf.LerpUnclamped(a.azimuth, b.azimuth, e) * azSign + azOffset;
        float el = Mathf.LerpUnclamped(a.elevation, b.elevation, e);
        if (_lib != null)
            el = Mathf.Clamp(el, _lib.minElevation, _lib.maxElevation);
        float dist = Mathf.Max(0.6f, Mathf.LerpUnclamped(a.distance, b.distance, e)) * fit;
        float pb = Mathf.Lerp(a.pivotBias, b.pivotBias, e);
        float lb = Mathf.Lerp(a.lookBias, b.lookBias, e);

        Vector3 fwd = _ctx.Forward;
        Vector3 right = _ctx.Right;
        Vector3 pivot;
        if (shot.pivot == ExecCamPivot.Impact)
            pivot = _ctx.impactPoint;
        else
        {
            float h = pivotHeightOverride > 0f ? pivotHeightOverride : shot.pivotHeight;
            pivot = Vector3.Lerp(_ctx.attackerFeet, _ctx.victimFeet, pb);
            pivot.y = _ctx.attackerFeet.y + h;
        }
        float azr = az * Mathf.Deg2Rad, elr = el * Mathf.Deg2Rad;
        Vector3 flat = -fwd * Mathf.Cos(azr) + right * Mathf.Sin(azr);
        Vector3 dir = flat * Mathf.Cos(elr) + Vector3.up * Mathf.Sin(elr);
        Vector3 pos = pivot + dir * dist;
        Vector3 look = LookPoint(shot, lb);
        float dutch = Mathf.LerpUnclamped(a.dutch, b.dutch, e) * azSign * (_ctx.side >= 0f ? 1f : -1f);
        return new ExecCamOutput
        {
            position = pos,
            look = look,
            rotation = LookRot(look - pos),
            fov = Mathf.LerpUnclamped(a.fov, b.fov, e),
            dutch = dutch,
            valid = true
        };
    }

    /// <summary>플레이어 등 뒤 게임플레이 구도 근사 (SoulsCameraRig 이 0.35 s 블렌드로 정확한 포즈로 이어받는다).</summary>
    public ExecCamOutput GameplayPose()
    {
        Vector3 fwd = _ctx.Forward;
        Vector3 right = _ctx.TrueRight;
        bool locked = _ctx.victimSurvives;
        float pitch = (locked ? _ctx.gpLockPitch : _ctx.gpPitch) * Mathf.Deg2Rad;
        float dist = locked ? _ctx.gpLockDistance : _ctx.gpDistance;
        Vector3 pivot = _ctx.attackerFeet + Vector3.up * _ctx.gpPivotHeight;
        Vector3 pos = pivot + (-fwd * Mathf.Cos(pitch) + Vector3.up * Mathf.Sin(pitch)) * dist + right * (locked ? _ctx.gpShoulder : 0.25f);
        Vector3 look = locked
            ? Vector3.Lerp(_ctx.AttackerChest, _ctx.VictimChest, _ctx.gpLookBias)
            : pivot + fwd * 6f;
        return new ExecCamOutput { position = pos, look = look, rotation = LookRot(look - pos), fov = _ctx.gpFov, dutch = 0f, valid = true };
    }

    static ExecCamOutput Blend(ExecCamOutput a, ExecCamOutput b, float w)
    {
        return new ExecCamOutput
        {
            position = Vector3.LerpUnclamped(a.position, b.position, w),
            look = Vector3.LerpUnclamped(a.look, b.look, w),
            rotation = Quaternion.SlerpUnclamped(a.rotation, b.rotation, w),
            fov = Mathf.LerpUnclamped(a.fov, b.fov, w),
            dutch = Mathf.LerpUnclamped(a.dutch, b.dutch, w),
            valid = true
        };
    }

    /// <summary>
    /// 반복 18: 게임플레이 복귀 경로를 플레이어 축을 도는 호로 보간 (직선 보간이 어깨·칼 바로 옆을 지나 한 프레임 칼날 안으로 들어가던 문제).
    /// 반경은 양 끝 중 작은 쪽 이상, 칼 리치(≈1.3 m) 이상을 유지한다.
    /// </summary>
    ExecCamOutput ArcBlendAroundAttacker(ExecCamOutput a, ExecCamOutput b, float w)
    {
        ExecCamOutput o = Blend(a, b, w);
        Vector3 axis = _ctx.attackerFeet;
        Vector3 da = a.position - axis, db = b.position - axis;
        da.y = 0f; db.y = 0f;
        float ra = da.magnitude, rb = db.magnitude;
        if (ra < 0.05f || rb < 0.05f)
            return o;
        float yaw = Mathf.LerpAngle(Mathf.Atan2(da.x, da.z) * Mathf.Rad2Deg, Mathf.Atan2(db.x, db.z) * Mathf.Rad2Deg, w) * Mathf.Deg2Rad;
        float lin = Mathf.Lerp(ra, rb, w);
        float r = lin + Mathf.Max(0f, 1.3f - lin) * Mathf.Sin(Mathf.PI * Mathf.Clamp01(w)); // 끝점은 그대로, 중간만 바깥으로
        o.position = new Vector3(axis.x + Mathf.Sin(yaw) * r, o.position.y, axis.z + Mathf.Cos(yaw) * r);
        o.rotation = LookRot(o.look - o.position);
        return o;
    }

    static Quaternion LookRot(Vector3 d) => d.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(d, Vector3.up) : Quaternion.identity;

    // ------------------------------------------------------------------ 규칙

    ExecCamOutput ApplyStaticRules(ExecCamOutput o)
    {
        float maxDutch = _lib != null ? _lib.maxDutch : 8f;
        o.dutch = Mathf.Clamp(o.dutch, -maxDutch, maxDutch);
        o.fov = Mathf.Clamp(o.fov, _lib != null ? _lib.minFov : 22f, _lib != null ? _lib.maxFov : 60f);

        // 최저 높이 (다리·바닥 프레이밍 금지)
        float minY = _ctx.GroundY + (_lib != null ? _lib.minCameraHeight : 0.6f);
        if (o.position.y < minY)
            o.position.y = minY;

        // 올려보기 상한
        Vector3 d = o.look - o.position;
        float horiz = new Vector2(d.x, d.z).magnitude;
        float maxUp = Mathf.Tan((_lib != null ? _lib.maxLookUp : 16f) * Mathf.Deg2Rad);
        if (horiz > 0.01f && d.y > horiz * maxUp)
            o.position.y = o.look.y - horiz * maxUp;

        // 인물 몸 안으로 들어가지 않기
        float clear = _lib != null ? _lib.characterClearance : 0.45f;
        o.position = PushOutOfCapsule(o.position, _ctx.attackerFeet, _ctx.attackerHeight, 0.3f + clear);
        o.position = PushOutOfCapsule(o.position, _ctx.victimFeet, _ctx.victimHeight, _ctx.victimRadius + clear);
        o.rotation = LookRot(o.look - o.position);
        return o;
    }

    static Vector3 PushOutOfCapsule(Vector3 p, Vector3 feet, float height, float radius)
    {
        if (p.y > feet.y + height + radius * 0.5f || p.y < feet.y - 0.2f)
            return p;
        Vector3 d = p - feet;
        d.y = 0f;
        float m = d.magnitude;
        if (m >= radius)
            return p;
        Vector3 n = m > 0.001f ? d / m : Vector3.right;
        Vector3 q = feet + n * radius;
        q.y = p.y;
        return q;
    }

    // 플레이어 화면 이탈 방지: 필요한 최소 보정 단계(0..1)를 찾고, 단계는 부드럽게 따라간다(올릴 땐 빠르게, 내릴 땐 천천히).
    // 보정 = 카메라를 시선선 뒤로 빼고, 시선점을 플레이어 가슴 쪽으로 당기고, FOV 를 조금 넓힌다. 이후 구체 캐스트가 벽을 처리.
    const int RescueSteps = 8;
    float _rescue;

    ExecCamOutput RescuePose(ExecCamOutput o, float r)
    {
        if (r <= 0.0001f)
            return o;
        Vector3 off = o.position - o.look;
        float d = Mathf.Max(0.5f, off.magnitude);
        Vector3 dir = off.sqrMagnitude > 0.0001f ? off.normalized : -(o.rotation * Vector3.forward);
        ExecCamOutput c = o;
        c.look = Vector3.Lerp(o.look, _ctx.AttackerChest, 0.55f * r);
        c.position = c.look + dir * d * (1f + 0.9f * r);
        c.fov = Mathf.Min(_lib != null ? _lib.maxFov : 60f, o.fov + 10f * r);
        c.dutch = o.dutch * (1f - r);
        c.rotation = LookRot(c.look - c.position);
        return c;
    }

    ExecCamOutput KeepAttackerInView(ExecCamOutput o, float dt)
    {
        const float margin = 0.04f;
        float need = 1f;
        for (int i = 0; i <= RescueSteps; i++)
        {
            float r = i / (float)RescueSteps;
            if (Visible(RescuePose(o, r), _ctx.AttackerChest, margin))
            {
                need = r;
                break;
            }
        }
        if (dt <= 0f || _rescue < 0f)
            _rescue = Mathf.Max(_rescue, need);
        else
            _rescue = Mathf.MoveTowards(_rescue, need, (need > _rescue ? 12f : 1.5f) * dt);
        return ApplyStaticRules(RescuePose(o, _rescue));
    }

    ExecCamOutput Constrain(ExecCamOutput o, float dt)
    {
        o = ApplyStaticRules(o);
        o = KeepAttackerInView(o, dt);

        // 지오메트리 관통 금지: 시선점 → 카메라 구체 캐스트로 당김 (당길 땐 0.04 s, 풀 땐 0.3 s)
        Vector3 off = o.position - o.look;
        float want = off.magnitude;
        if (want < 0.01f)
            return o;
        Vector3 dir = off / want;
        float hit = CastDistance(o.look, dir, want);
        float target = Mathf.Min(want, hit);
        if (_allowedDistance < 0f || dt <= 0f)
            _allowedDistance = target;
        else if (target < _allowedDistance)
            _allowedDistance = Mathf.SmoothDamp(_allowedDistance, target, ref _allowedVelocity, 0.04f, Mathf.Infinity, dt);
        else
            _allowedDistance = Mathf.SmoothDamp(_allowedDistance, target, ref _allowedVelocity, 0.3f, Mathf.Infinity, dt);
        // 막혔으면 절대 넘지 않는다
        _allowedDistance = Mathf.Min(_allowedDistance, hit);
        if (_allowedDistance < want - 0.001f)
        {
            o.position = o.look + dir * Mathf.Max(0.35f, _allowedDistance);
            o.rotation = LookRot(o.look - o.position);
        }
        return o;
    }

    float CastDistance(Vector3 origin, Vector3 dir, float dist)
    {
        float radius = _lib != null ? _lib.collisionRadius : 0.25f;
        int mask = _ctx.collisionMask.value != 0 ? _ctx.collisionMask.value : ((1 << 0) | (1 << 13));
        int n = Physics.SphereCastNonAlloc(origin, radius, dir, s_hits, dist, mask, QueryTriggerInteraction.Ignore);
        float best = dist;
        for (int i = 0; i < n; i++)
        {
            RaycastHit h = s_hits[i];
            if (h.distance <= 0f || h.collider == null || Ignore(h.collider))
                continue;
            if (h.distance < best)
                best = h.distance;
        }
        return best;
    }

    bool IsBlocked(ExecCamOutput p, bool keepBoth)
    {
        int mask = _ctx.collisionMask.value != 0 ? _ctx.collisionMask.value : ((1 << 0) | (1 << 13));
        float radius = _lib != null ? _lib.collisionRadius : 0.25f;
        Vector3 off = p.position - p.look;
        float dist = off.magnitude;
        if (dist > 0.01f && CastDistance(p.look, off / dist, dist) < dist * 0.92f)
            return true;
        int n = Physics.OverlapSphereNonAlloc(p.position, radius * 0.9f, s_overlaps, mask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
            if (!Ignore(s_overlaps[i]))
                return true;
        if (LineBlocked(p.position, _ctx.AttackerChest, mask) || LineBlocked(p.position, _ctx.VictimChest, mask))
            return true;
        if (keepBoth)
        {
            // 인물끼리 가림: 카메라→플레이어 가슴 선이 피격자 몸통을 지나가면 읽기 어려움
            if (SegmentAxisDistance(p.position, _ctx.AttackerChest, _ctx.victimFeet, _ctx.victimHeight) < _ctx.victimRadius * 0.85f)
                return true;
            if (SegmentAxisDistance(p.position, _ctx.VictimHead, _ctx.attackerFeet, _ctx.attackerHeight) < 0.22f)
                return true;
        }
        return false;
    }

    static float SegmentAxisDistance(Vector3 a, Vector3 b, Vector3 feet, float height)
    {
        // 선분 a→b 와 수직 축(feet..feet+height) 의 최소 수평 거리 근사 (표본 12)
        float best = float.PositiveInfinity;
        for (int i = 1; i < 12; i++)
        {
            Vector3 q = Vector3.Lerp(a, b, i / 12f);
            if (q.y < feet.y || q.y > feet.y + height)
                continue;
            Vector3 d = q - feet;
            d.y = 0f;
            best = Mathf.Min(best, d.magnitude);
        }
        return best;
    }

    bool LineBlocked(Vector3 a, Vector3 b, int mask)
    {
        Vector3 d = b - a;
        float m = d.magnitude;
        if (m < 0.01f)
            return false;
        int n = Physics.RaycastNonAlloc(a, d / m, s_hits, m, mask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
            if (s_hits[i].collider != null && !Ignore(s_hits[i].collider))
                return true;
        return false;
    }

    bool Ignore(Collider c)
    {
        if (c == null)
            return true;
        int key = c.GetInstanceID();
        if (s_ignoreCache.TryGetValue(key, out bool cached))
            return cached;
        bool ignore = SoulsCameraRig.IsCharacterCollider(c);
        if (!ignore)
        {
            Vector3 s = c.bounds.size;
            float a = s.x, b = s.y, d = s.z;
            float second = Mathf.Max(Mathf.Min(a, b), Mathf.Min(Mathf.Max(a, b), d));
            ignore = second < (_lib != null ? _lib.thinPropSize : 0.35f);
        }
        s_ignoreCache[key] = ignore;
        return ignore;
    }

    bool InFrame(ExecCamOutput p, ExecCamShot shot)
    {
        p = ApplyStaticRules(p);
        float margin = _lib != null ? _lib.frameMargin : 0.08f;
        if (!Visible(p, p.look, 0f))
            return false;
        if (!shot.keepBoth)   // 인서트도 플레이어(가슴)는 화면 안에 남긴다
            return Visible(p, shot.lookImpact ? _ctx.impactPoint : _ctx.VictimChest, margin) && Visible(p, _ctx.AttackerChest, 0f);
        return Visible(p, _ctx.AttackerHead, margin) && Visible(p, _ctx.AttackerChest, margin)
            && Visible(p, _ctx.VictimChest, margin) && Visible(p, _ctx.VictimHead, margin * 0.5f);
    }

    bool Visible(ExecCamOutput p, Vector3 world, float margin)
    {
        Vector3 v = Quaternion.Inverse(p.rotation) * (world - p.position);
        if (v.z < 0.2f)
            return false;
        float ty = Mathf.Tan(p.fov * 0.5f * Mathf.Deg2Rad);
        float tx = ty * Mathf.Max(0.5f, _ctx.aspect);
        float x = v.x / (v.z * tx), y = v.y / (v.z * ty);
        float r = -p.dutch * Mathf.Deg2Rad;
        float rx = x * Mathf.Cos(r) - y * Mathf.Sin(r), ry = x * Mathf.Sin(r) + y * Mathf.Cos(r);
        float lim = 1f - margin * 2f;
        return Mathf.Abs(rx) <= lim && Mathf.Abs(ry) <= lim;
    }

    // ------------------------------------------------------------------ 이징

    public static float Ease(ExecCamEase ease, float k)
    {
        k = Mathf.Clamp01(k);
        switch (ease)
        {
            case ExecCamEase.EaseIn: return k * k;
            case ExecCamEase.EaseOut: return 1f - (1f - k) * (1f - k);
            case ExecCamEase.EaseInOut: return Mathf.SmoothStep(0f, 1f, k);
            case ExecCamEase.Snap: return k >= 1f ? 1f : (1f - Mathf.Pow(2f, -10f * k)) / (1f - Mathf.Pow(2f, -10f));
            default: return k;
        }
    }

    static float SmootherStep(float x)
    {
        x = Mathf.Clamp01(x);
        return x * x * x * (x * (x * 6f - 15f) + 10f);
    }
}
