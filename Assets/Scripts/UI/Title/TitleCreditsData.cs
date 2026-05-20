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

    public string title = "\uAC1C\uBC1C\uC9C4";

    [TextArea(2, 6)]
    public string subtitle =
        "\uD504\uB85C\uC81D\uD2B8 \uCD94\uC628 \uAC1C\uBC1C \uAE30\uB85D\n" +
        "\uC2E4\uC81C \uAC1C\uBC1C\uC9C4 \uC815\uBCF4\uC640 \uAC1C\uBC1C \uC0AC\uC9C4\uC744 \uC774 \uC5D0\uC14B\uC5D0\uC11C \uC9C1\uC811 \uD3B8\uC9D1\uD558\uBA74 \uD0C0\uC774\uD2C0 \uD06C\uB808\uB515\uC5D0 \uBC14\uB85C \uBC18\uC601\uB429\uB2C8\uB2E4.";

    [Min(0.001f)]
    public float autoScrollNormalizedPerSecond = 0.016f;

    public Section[] sections = Array.Empty<Section>();

    public static TitleCreditsData CreateRuntimeFallback()
    {
        TitleCreditsData data = CreateInstance<TitleCreditsData>();
        data.hideFlags = HideFlags.HideAndDontSave;
        data.title = "\uAC1C\uBC1C\uC9C4";
        data.subtitle =
            "\uD504\uB85C\uC81D\uD2B8 \uCD94\uC628 \uAC1C\uBC1C \uAE30\uB85D\n" +
            "\uC2E4\uC81C \uAC1C\uBC1C\uC9C4 \uC815\uBCF4\uC640 \uAC1C\uBC1C \uC0AC\uC9C4\uC744 \uCC44\uC6B0\uBA74 \uC774 \uAE30\uBCF8\uAC12 \uB300\uC2E0 \uBC14\uB85C \uBC18\uC601\uB429\uB2C8\uB2E4.";
        data.autoScrollNormalizedPerSecond = 0.015f;
        data.sections = new[]
        {
            new Section
            {
                heading = "\uCD1D\uAD04 \uB514\uB809\uC158",
                responsibilities = "\uD504\uB85C\uC81D\uD2B8 \uBE44\uC804, \uD0C0\uC774\uD2C0 \uACBD\uD5D8, \uC804\uCCB4 \uC5F0\uCD9C \uBC29\uD5A5\uC744 \uB2F4\uB2F9\uD569\uB2C8\uB2E4.",
                members = new[] { "\uC774\uB984 \uC785\uB825", "\uC774\uB984 \uC785\uB825" },
                photos = new[]
                {
                    new PhotoEntry { caption = "\uBA54\uC778 \uBB34\uB4DC\uBCF4\uB4DC / \uD0C0\uC774\uD2C0 \uBC29\uD5A5 \uBCF4\uB4DC" },
                    new PhotoEntry { caption = "\uC138\uACC4\uAD00 \uC815\uB9AC / \uD0A4 \uBE44\uC8FC\uC5BC \uB9AC\uBDF0" }
                }
            },
            new Section
            {
                heading = "\uC804\uD22C \uC2DC\uC2A4\uD15C",
                responsibilities = "\uD50C\uB808\uC774\uC5B4 \uC804\uD22C \uAC10\uAC01, \uBCF4\uC2A4 \uD328\uD134, \uB77D\uC628 \uCE74\uBA54\uB77C, \uC804\uD22C \uBC38\uB7F0\uC2F1\uC744 \uB2F4\uB2F9\uD569\uB2C8\uB2E4.",
                members = new[] { "\uC774\uB984 \uC785\uB825", "\uC774\uB984 \uC785\uB825" },
                photos = new[]
                {
                    new PhotoEntry { caption = "\uC804\uD22C \uD504\uB85C\uD1A0\uD0C0\uC785 \uBC18\uBCF5 \uC791\uC5C5" },
                    new PhotoEntry { caption = "\uBCF4\uC2A4 \uD328\uD134 \uB514\uBC84\uADF8 \uCEA1\uCC98" }
                }
            },
            new Section
            {
                heading = "\uCE90\uB9AD\uD130 \uC544\uD2B8 & UI",
                responsibilities = "\uCE90\uB9AD\uD130 \uD45C\uD604, \uD0C0\uC774\uD2C0 \uC544\uD2B8, \uBA54\uB274 \uAD6C\uC131, HUD\uC640 \uC2DC\uAC01 \uC5F0\uCD9C\uC744 \uB2F4\uB2F9\uD569\uB2C8\uB2E4.",
                members = new[] { "\uC774\uB984 \uC785\uB825", "\uC774\uB984 \uC785\uB825" },
                photos = new[]
                {
                    new PhotoEntry { caption = "\uCE90\uB9AD\uD130 \uD45C\uD604 / \uD45C\uC815 \uBCF4\uC815" },
                    new PhotoEntry { caption = "\uBA54\uB274 \uAD6C\uC131 / UI \uC2A4\uD130\uB514" }
                }
            },
            new Section
            {
                heading = "\uB808\uBCA8 / \uC2DC\uB124\uB9C8\uD2F1 / QA",
                responsibilities = "\uC804\uC7A5 \uAD6C\uC131, \uC2DC\uB124\uB9C8\uD2F1 \uD0C0\uC774\uBC0D, \uC52C \uC870\uB9BD, \uCD5C\uC885 \uAC80\uC218\uB97C \uB2F4\uB2F9\uD569\uB2C8\uB2E4.",
                members = new[] { "\uC774\uB984 \uC785\uB825", "\uC774\uB984 \uC785\uB825" },
                photos = new[]
                {
                    new PhotoEntry { caption = "\uC2DC\uB124\uB9C8\uD2F1 \uC0F7 \uAE30\uD68D" },
                    new PhotoEntry { caption = "\uD50C\uB808\uC774\uD14C\uC2A4\uD2B8 / \uBC38\uB7F0\uC2A4 \uB85C\uADF8" }
                }
            }
        };
        return data;
    }
}
