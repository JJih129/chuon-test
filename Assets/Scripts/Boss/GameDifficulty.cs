// 파일명: GameDifficulty.cs
// 플레이어가 고르는 난이도(쉬움/보통/어려움). PlayerPrefs 에 저장되고, BossController 가 followGlobalDifficulty 일 때
// 이 값으로 BossDifficultyProfile(티어별 배율 세트)을 고른다. 기본값 = 보통(현재 튜닝).
using System;
using UnityEngine;

public static class GameDifficulty
{
    public const string PrefsKey = "chuon.difficulty";

    static bool s_loaded;
    static BossDifficultyTier s_current = BossDifficultyTier.Normal;

    /// <summary>값이 바뀔 때(Set) 알림. UI 동기화용.</summary>
    public static event Action<BossDifficultyTier> Changed;

    /// <summary>선택 가능한 티어 (Expert 는 노출하지 않음).</summary>
    public static readonly BossDifficultyTier[] Selectable = { BossDifficultyTier.Easy, BossDifficultyTier.Normal, BossDifficultyTier.Hard };

    public static BossDifficultyTier Current
    {
        get
        {
            EnsureLoaded();
            return s_current;
        }
    }

    public static void Set(BossDifficultyTier tier, bool save = true)
    {
        EnsureLoaded();
        tier = Sanitize(tier);
        bool changed = tier != s_current;
        s_current = tier;
        if (save)
        {
            try
            {
                PlayerPrefs.SetInt(PrefsKey, (int)tier);
                PlayerPrefs.Save();
            }
            catch (Exception) { }
        }

        if (changed)
            Changed?.Invoke(tier);
    }

    /// <summary>Selectable 안에서 한 칸 이동 (좌/우 버튼용).</summary>
    public static BossDifficultyTier Step(int direction)
    {
        int index = Array.IndexOf(Selectable, Current);
        if (index < 0) index = 1;
        index = Mathf.Clamp(index + (direction >= 0 ? 1 : -1), 0, Selectable.Length - 1);
        Set(Selectable[index]);
        return Current;
    }

    public static string Label(BossDifficultyTier tier)
    {
        switch (Sanitize(tier))
        {
            case BossDifficultyTier.Easy: return "쉬움";
            case BossDifficultyTier.Hard: return "어려움";
            default: return "보통";
        }
    }

    public static string Description(BossDifficultyTier tier)
    {
        switch (Sanitize(tier))
        {
            case BossDifficultyTier.Easy: return "Type-D 의 공격이 느리고 약해집니다. 패링 판정이 넉넉합니다.";
            case BossDifficultyTier.Hard: return "Type-D 가 더 빠르고 집요하게 몰아붙입니다. 연계가 늘어납니다.";
            default: return "기획 의도대로의 기본 난이도입니다.";
        }
    }

    public static string CurrentLabel => Label(Current);

    static BossDifficultyTier Sanitize(BossDifficultyTier tier)
    {
        return tier == BossDifficultyTier.Easy || tier == BossDifficultyTier.Hard ? tier : BossDifficultyTier.Normal;
    }

    static void EnsureLoaded()
    {
        if (s_loaded)
            return;
        s_loaded = true;
        try
        {
            s_current = Sanitize((BossDifficultyTier)PlayerPrefs.GetInt(PrefsKey, (int)BossDifficultyTier.Normal));
        }
        catch (Exception)
        {
            s_current = BossDifficultyTier.Normal;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        s_loaded = false;
        Changed = null;
    }
}
