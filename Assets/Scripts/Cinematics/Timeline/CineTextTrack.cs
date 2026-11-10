using TMPro;
using UnityEngine.Timeline;

/// <summary>월드 텍스트 트랙 (엘리베이터 층 표시, 은신처 모니터 문구). 바인딩: TMP_Text. 클립 시작에 한 번 문구를 바꾼다.</summary>
[TrackColor(1f, 0.2f, 0.16f)]
[TrackBindingType(typeof(TMP_Text))]
[TrackClipType(typeof(CineTextClip))]
public sealed class CineTextTrack : TrackAsset
{
}
