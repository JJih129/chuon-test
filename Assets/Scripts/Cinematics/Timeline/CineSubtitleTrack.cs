using UnityEngine.Timeline;

/// <summary>
/// 자막/타이틀 카드 트랙. 바인딩 없음 — CinematicOverlay(기존 IntroDialoguePresenter 사용)로 출력한다.
/// 대사 옵션(chuon.dialogue.enabled)이 꺼져 있으면 대사 줄은 표시하지 않는다 (타이틀 카드는 장소 정보라 표시).
/// </summary>
[TrackColor(0.95f, 0.95f, 0.97f)]
[TrackClipType(typeof(CineSubtitleClip))]
public sealed class CineSubtitleTrack : TrackAsset
{
}
