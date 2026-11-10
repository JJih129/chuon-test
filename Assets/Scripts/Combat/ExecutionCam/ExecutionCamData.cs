// 처형 전용 카메라 연출(ExecutionCamChoreographer) 데이터.
// 게임플레이 카메라(SoulsCameraRig)는 그대로 차분하게 두고, 처형 동안만 짧은 샷 리스트를 재생한다.
//
// 좌표 규약 (리그 공간):
//  A = 공격자(플레이어) 발, V = 피격자 발, fwd = A→V 수평 방향, right = up × fwd × side(±1, 공간에 따라 좌우 반전)
//  azimuth   0° = 플레이어 등 뒤(어깨 너머), +90° = 두 인물 옆 프로필, 180° = 피격자 등 뒤에서 플레이어를 봄
//  elevation +  = 카메라가 피벗보다 위(내려봄),  − = 낮은 앵글(올려봄, '영웅 저각'). 하한은 라이브러리에서 제한
//  distance  피벗 → 카메라 (m). 프레이밍 검사로 두 인물이 다 보이도록 자동으로 늘어난다
//  pivotBias / lookBias  0 = 공격자 가슴, 1 = 피격자 가슴(lookImpact 면 타격점)
using System.Collections.Generic;
using UnityEngine;

public enum ExecCamType
{
    BossFront = 0,
    BossBack = 1,
    BossFinal = 2,
    Android = 3,
    Enforcer = 4,
    Takedown = 5
}

public enum ExecCamPhase
{
    PreImpact = 0,   // 재생 시작 ~ 타격 (예상 타격 시각에 맞춰 늘거나 줄어든다)
    PostImpact = 1   // 타격 ~ 끝 (슬로모 포함 실시간 길이에 맞춰 늘거나 줄어든다)
}

public enum ExecCamMode
{
    Orbit = 0,            // from → to 를 리그 공간에서 보간 (궤도·돌리·스냅 줌)
    FromCurrent = 1,      // 샷 시작 때 실제 화면 → to (연속 이동, 컷 없음)
    PushInCurrent = 2,    // 현재 방향 유지, to.distance = 거리 배율, to.fov/to.dutch = 증감(델타)
    ReturnToGameplay = 3  // 현재 화면 → 플레이어 등 뒤 게임플레이 구도 (SoulsCameraRig 0.35 s 블렌드가 이어받는다)
}

public enum ExecCamEase
{
    Linear = 0,
    EaseIn = 1,
    EaseOut = 2,
    EaseInOut = 3,
    Snap = 4    // 지수 감속: 처음 25 % 안에 대부분 이동 (스냅 줌)
}

public enum ExecCamEntry
{
    Cut = 0,
    Whip = 1,   // entryTime 동안 빠른 회전 (휩 팬)
    Blend = 2   // entryTime 동안 부드러운 이동
}

public enum ExecCamPivot
{
    Pair = 0,     // 공격자 발 ~ 피격자 발 사이(pivotBias) + pivotHeight
    Impact = 1    // 타격점(코어/가슴 뼈) — 인서트용
}

[System.Serializable]
public struct ExecCamPose
{
    public float azimuth;
    public float elevation;
    public float distance;
    public float fov;
    public float dutch;
    [Range(0f, 1f)] public float pivotBias;
    [Range(0f, 1f)] public float lookBias;

    public ExecCamPose(float azimuth, float elevation, float distance, float fov, float dutch = 0f, float pivotBias = 0.5f, float lookBias = 0.55f)
    {
        this.azimuth = azimuth;
        this.elevation = elevation;
        this.distance = distance;
        this.fov = fov;
        this.dutch = dutch;
        this.pivotBias = pivotBias;
        this.lookBias = lookBias;
    }
}

[System.Serializable]
public sealed class ExecCamShot
{
    public string name = "shot";
    public ExecCamPhase phase = ExecCamPhase.PreImpact;
    [Tooltip("단계(타격 전/후) 안에서 시작·끝 비율 0..1")]
    [Range(0f, 1f)] public float start;
    [Range(0f, 1f)] public float end = 1f;
    public ExecCamMode mode = ExecCamMode.Orbit;
    public ExecCamPivot pivot = ExecCamPivot.Pair;
    [Tooltip("Pair 피벗 높이 (공격자 발 기준 m)")]
    public float pivotHeight = 1.35f;
    [Tooltip("시선점을 피격자 가슴 대신 타격점(코어)으로")]
    public bool lookImpact;
    public ExecCamPose from;
    public ExecCamPose to;
    public ExecCamEase ease = ExecCamEase.EaseInOut;
    public ExecCamEntry entry = ExecCamEntry.Cut;
    [Min(0f)] public float entryTime;
    [Tooltip("두 인물의 머리·가슴이 화면 안에 있어야 함 (인서트는 끔)")]
    public bool keepBoth = true;
    [Tooltip("샷 시작 시 위치 흔들림 (m, 옵션 흔들림 강도 곱)")]
    [Min(0f)] public float jolt;
}

[System.Serializable]
public sealed class ExecCamVariant
{
    public string name = "variant";
    public List<ExecCamShot> shots = new List<ExecCamShot>();
}

[System.Serializable]
public sealed class ExecCamTypeEntry
{
    public ExecCamType type;
    [Header("속도 램프 (GameTimeScale, 타격 후 실시간)")]
    [Range(0.05f, 1f)] public float rampScale = 0.35f;
    [Min(0f)] public float rampHold = 0.3f;
    [Min(0f)] public float rampOut = 0.22f;
    [Header("임팩트 프레임")]
    [Range(0, 4)] public int impactFreezeFrames = 2;
    [Min(0f)] public float impactJolt = 0.06f;
    [Tooltip("타격 순간 FOV 펀치 (°, 음수 = 좁힘)")]
    public float impactFovKick = -2.5f;
    [Tooltip("임팩트 프레임 흑백 펄스 (이미 흡수/펄스가 있는 종류는 0)")]
    [Range(0f, 1f)] public float impactBwPulse;
    public List<ExecCamVariant> variants = new List<ExecCamVariant>();
    [Tooltip("움직임 줄이기: 고정된 차분한 샷 1개")]
    public ExecCamVariant calm = new ExecCamVariant();
}
