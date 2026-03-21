using UnityEngine;
using Unity.Cinemachine;
#pragma warning disable CS0618

// ===== 변수 헤더(한글 설명) =====
// 목적: 타임라인(애니메이션 트랙)에서 offsetX / offsetY / offsetZ에 키프레임을 주면
//       Cinemachine Transposer의 m_FollowOffset(X/Y/Z)을 매 프레임 갱신한다.
// 사용처: VCam_Action(또는 원하는 VCam)에 붙여서, 카메라 구도를 XYZ로 연출 제어.
//
// [핵심 조절값]
//  - offsetX : 좌/우 오프셋(+는 카메라가 타겟 오른쪽으로 이동)
//  - offsetY : 높이 오프셋(+는 더 높은 시점)
//  - offsetZ : 전/후 오프셋(일반적으로 뒤는 음수 값, 앞으로 당길 땐 +)
//  - animateY : ON이면 offsetY를 사용해 Y축도 애니메이션, OFF면 현재 Transposer Y를 유지(고정)
//
// [보간 옵션(연출 부드러움)]
//  - smoothFollow : ON이면 목표 오프셋으로 서서히 수렴(Lerp 또는 SmoothDamp)
//  - lerpSpeed    : Lerp 속도(초당 비율). 값이 클수록 빠르게 목표로 붙음(권장 4~8)
//  - smoothTime   : SmoothDamp 목표 도달 시간(초). 0보다 크면 SmoothDamp 우선(말랑한 감속)
//
// [유틸]
//  - SnapNow()    : 현재 offsetX/Y/Z 목표값으로 '즉시' 스냅(컷 전환 시 호출용)
//  - applyEveryFrame : 매 프레임 반영할지 여부
//
// 주의: 대상 VCam의 Body는 반드시 "Transposer"여야 함.
//      Main Camera에는 Cinemachine Brain이 붙어 있어야 전환/블렌드가 동작함.
[ExecuteAlways]
public class VCamTransposerProxy : MonoBehaviour
{
    [Header("① 대상 VCam (비우면 자기 자신에서 찾음)")]
    public CinemachineVirtualCameraBase vcam;

    [Header("② 애니메이션용 프록시 오프셋(X/Y/Z) - 타임라인에서 여기에 키 주기")]
    public float offsetX = 0.0f;   // 예: 0.8 → -0.6 (좌우 스윕)
    public float offsetY = 1.8f;   // 예: 1.8 → 2.2  (상하 변환)
    public float offsetZ = -3.8f;  // 예: -3.8 → -3.2 (당겨 임팩트)

    [Header("③ Y축 애니메이션 사용 여부 (OFF=현 Transposer Y 유지)")]
    public bool animateY = false;

    [Header("④ 매 프레임 강제 반영")]
    public bool applyEveryFrame = true;
    [Tooltip("플레이 중에는 실제로 라이브인 시네머신 카메라거나 목표값이 바뀐 경우에만 반영.")]
    public bool skipWhenNotLiveAndUnchanged = true;

    [Header("⑤ 부드러운 보간 옵션 (연출 품질)")]
    public bool smoothFollow = true;
    [Tooltip("Lerp 속도(초당 비율). 값이 클수록 더 빨리 목표 오프셋에 붙음(권장 4~8).")]
    public float lerpSpeed = 6f;
    [Tooltip("SmoothDamp 시간(초). 0보다 크면 SmoothDamp 우선 적용(자연 감속).")]
    public float smoothTime = 0f;

    Vector3 _vel; // SmoothDamp 내부 속도 상태(유지용)
    Vector3 _lastRequestedTarget;
    bool _hasLastRequestedTarget;

    void Reset() { TryResolve(); }
    void OnEnable() { TryResolve(); Apply(forceSnap: true); }
#if UNITY_EDITOR
    void OnValidate() { TryResolve(); Apply(forceSnap: true); }
#endif
    void LateUpdate()
    {
        if (!applyEveryFrame)
            return;

        TryResolve();
        if (!TryBuildTarget(out var target, out var cur))
            return;

        bool targetChanged = !_hasLastRequestedTarget || (target - _lastRequestedTarget).sqrMagnitude > 0.000001f;
        _lastRequestedTarget = target;
        _hasLastRequestedTarget = true;

        if (Application.isPlaying && skipWhenNotLiveAndUnchanged && vcam != null && !CinemachineCore.IsLive(vcam) && !targetChanged)
            return;

        ApplyResolved(cur, target, forceSnap: false);
    }

    void TryResolve()
    {
        if (!vcam) vcam = GetComponent<CinemachineVirtualCameraBase>();
    }

    // === 외부에서 즉시 스냅시키고 싶을 때(컷 전환 타이밍 등) 호출 ===
    public void SnapNow() => Apply(forceSnap: true);

    void Apply(bool forceSnap)
    {
        if (!TryBuildTarget(out var target, out var cur))
            return;

        _lastRequestedTarget = target;
        _hasLastRequestedTarget = true;
        ApplyResolved(cur, target, forceSnap);
    }

    bool TryBuildTarget(out Vector3 target, out Vector3 current)
    {
        target = default;
        current = default;
        if (!CinemachineCompat.TryGetBodyFollowOffset(vcam, out current))
            return false;

        float targetY = animateY ? offsetY : current.y;
        target = new Vector3(offsetX, targetY, offsetZ);
        return true;
    }

    void ApplyResolved(Vector3 cur, Vector3 target, bool forceSnap)
    {
        // 에디터/스냅/보간 미사용 → 즉시 적용
        if (!Application.isPlaying || forceSnap || !smoothFollow || (lerpSpeed <= 0f && smoothTime <= 0f))
        {
            CinemachineCompat.TrySetBodyFollowOffset(vcam, target);
            return;
        }

        // 런타임 보간
        Vector3 next;
        if (smoothTime > 0f)
        {
            // SmoothDamp: 지정 시간에 근사 도달(끝에서 자연 감속)
            next = Vector3.SmoothDamp(cur, target, ref _vel, smoothTime);
        }
        else
        {
            // Lerp: 프레임당 비율 수렴
            float t = 1f - Mathf.Pow(1f - Mathf.Clamp01(lerpSpeed * Time.deltaTime), 60f);
            next = Vector3.Lerp(cur, target, t);
        }

        CinemachineCompat.TrySetBodyFollowOffset(vcam, next);
    }
}
