// 파일명: ComboRouteCatalog.cs
// 콤보 루트 5종(A–E)의 단일 데이터 소스.
// 튜토리얼 공격 배치표(TutorialComboGuideView 본문)와 설정창 '콤보' 탭이 같은 데이터를 쓴다.
// 루트 순서/입력은 Assets/Attack_data/Combo_A..E 와 PlayerCombatController 콤보 트리 기준.
using System.Text;

public enum ComboInputKind
{
    Light,
    Heavy
}

public sealed class ComboRoute
{
    public readonly string Id;            // "A".."E"
    public readonly string Name;          // 설정창 표기 이름
    public readonly string TutorialName;  // 튜토리얼 배치표 줄머리 (빈 문자열이면 생략)
    public readonly ComboInputKind[] Inputs;
    public readonly string Note;

    public ComboRoute(string id, string name, string tutorialName, string note, params ComboInputKind[] inputs)
    {
        Id = id;
        Name = name;
        TutorialName = tutorialName ?? string.Empty;
        Note = note;
        Inputs = inputs ?? new ComboInputKind[0];
    }

    public int Length => Inputs.Length;
}

public static class ComboRouteCatalog
{
    public const string LightLabel = "약공";
    public const string HeavyLabel = "강공";
    public const string LightKey = "LMB";
    public const string HeavyKey = "RMB";
    public const string LightKeyDescription = "마우스 좌클릭";
    public const string HeavyKeyDescription = "마우스 우클릭";
    public const string TutorialGuideTitle = "공격 배치표";

    const ComboInputKind L = ComboInputKind.Light;
    const ComboInputKind H = ComboInputKind.Heavy;

    /// <summary>콤보 루트 A–E (순서 고정, 수정 시 튜토리얼 배치표도 같이 바뀐다).</summary>
    public static readonly ComboRoute[] Routes =
    {
        new ComboRoute("A", "기본", "기본",
            "약공 4연격. 빈틈이 짧아 견제와 마무리에 가장 안정적이다.",
            L, L, L, L),
        new ComboRoute("B", "확장", "확장",
            "약공 3타로 열고 강공 2연타로 마무리하는 장연계. 빈틈이 크니 확실할 때만.",
            L, L, L, H, H),
        new ComboRoute("C", "혼합", "혼합",
            "약·강을 번갈아 넣는 4연격. 리듬을 유지하면 끊기지 않는다.",
            L, H, L, H),
        new ComboRoute("D", "꼬리", "꼬리",
            "강공으로 열어 짧게 끊어 치는 3연격. 강공 시작이 필요할 때.",
            H, L, H),
        new ComboRoute("E", "강타", string.Empty,
            "강공 3연타. 느리지만 타격이 쌓일수록 한 방이 무겁다.",
            H, H, H),
    };

    /// <summary>현재 장치/리바인드 표기 (키보드 기본 배치면 LMB/RMB 그대로).</summary>
    public static string KeyOf(ComboInputKind input) => input == ComboInputKind.Heavy
        ? GlyphOr(ChuonAction.AttackHeavy, HeavyKey)
        : GlyphOr(ChuonAction.AttackLight, LightKey);

    static string GlyphOr(ChuonAction a, string keyboardDefault)
        => !ChuonInput.IsGamepadScheme && !ChuonInput.KeyboardOverridden(a) ? keyboardDefault : InputGlyphs.Get(a);

    /// <summary>한 글자 표기 (약 / 강).</summary>
    public static string ShortLabelOf(ComboInputKind input) => input == ComboInputKind.Heavy ? "강" : "약";

    /// <summary>"LMB > LMB > RMB" 형식.</summary>
    public static string FormatKeys(ComboRoute route)
    {
        if (route == null || route.Inputs.Length == 0)
            return string.Empty;

        StringBuilder builder = new StringBuilder(route.Inputs.Length * 6);
        AppendKeys(builder, route);
        return builder.ToString();
    }

    /// <summary>
    /// 튜토리얼 공격 배치표 본문. TutorialRuntimeBootstrap.DefaultComboGuideText 와 글자 단위로 같다.
    /// </summary>
    public static string BuildTutorialGuideBody()
    {
        StringBuilder builder = new StringBuilder(160);
        builder.Append(LightLabel).Append("  ").Append(KeyOf(ComboInputKind.Light)).Append('\n');
        builder.Append(HeavyLabel).Append("  ").Append(KeyOf(ComboInputKind.Heavy)).Append("\n\n");

        for (int i = 0; i < Routes.Length; i++)
        {
            ComboRoute route = Routes[i];
            if (i > 0)
                builder.Append('\n');
            if (!string.IsNullOrEmpty(route.TutorialName))
                builder.Append(route.TutorialName).Append("  ");
            AppendKeys(builder, route);
        }

        return builder.ToString();
    }

    static void AppendKeys(StringBuilder builder, ComboRoute route)
    {
        for (int i = 0; i < route.Inputs.Length; i++)
        {
            if (i > 0)
                builder.Append(" > ");
            builder.Append(KeyOf(route.Inputs[i]));
        }
    }
}
