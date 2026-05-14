using System;
using UnityEngine;

[CreateAssetMenu(fileName = "TitleCreditsData", menuName = "Project ChuOn/UI/Title Credits Data")]
public sealed class TitleCreditsData : ScriptableObject
{
    [Serializable]
    public sealed class PhotoEntry
    {
        public Sprite sprite;
        public string caption = string.Empty;
    }

    [Serializable]
    public sealed class Section
    {
        public string heading = string.Empty;

        [TextArea(2, 6)]
        public string responsibilities = string.Empty;

        public string[] members = Array.Empty<string>();
        public PhotoEntry[] photos = Array.Empty<PhotoEntry>();
    }

    public string title = "PROJECT CHUON";

    [TextArea(2, 6)]
    public string subtitle =
        "DEVELOPMENT CREDITS\n" +
        "함께 만든 사람들";

    [Min(0.001f)]
    public float autoScrollNormalizedPerSecond = 0.016f;

    public Section[] sections = Array.Empty<Section>();

    public static TitleCreditsData CreateRuntimeFallback()
    {
        TitleCreditsData data = CreateInstance<TitleCreditsData>();
        data.hideFlags = HideFlags.HideAndDontSave;
        data.title = "PROJECT CHUON";
        data.subtitle =
            "DEVELOPMENT CREDITS\n" +
            "함께 만든 사람들";
        data.autoScrollNormalizedPerSecond = 0.012f;
        data.sections = new[]
        {
            new Section
            {
                heading = "Game Design",
                responsibilities = "게임 구조, 진행 흐름, 플레이 목표 설계",
                members = new[] { "박서진" }
            },
            new Section
            {
                heading = "Programming",
                responsibilities = "플레이어, 전투, UI, 씬 진행 시스템 구현",
                members = new[] { "손지훈" }
            },
            new Section
            {
                heading = "Art Director",
                responsibilities = "비주얼 방향성, 화면 톤, 아트 품질 관리",
                members = new[] { "박준우" }
            },
            new Section
            {
                heading = "2D Art",
                responsibilities = "UI 그래픽, 아이콘, 2D 비주얼 리소스",
                members = new[] { "강은비 · 이기요" }
            },
            new Section
            {
                heading = "Character Modeling",
                responsibilities = "캐릭터 모델링 및 비주얼 리소스 제작",
                members = new[] { "조예원" }
            },
            new Section
            {
                heading = "Environment Modeling",
                responsibilities = "맵 모델링 및 환경 오브젝트 제작",
                members = new[] { "김기문" }
            }
        };
        return data;
    }
}
