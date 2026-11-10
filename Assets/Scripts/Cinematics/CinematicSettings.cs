using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 시네마틱 옵션·시청 기록 (반복 14, Docs/Cinematics_Shotlist.md).
/// - 레터박스 표시: PlayerPrefs "chuon.cine.letterbox" (1 = 켬, 기본).
/// - 본 연출 재도전 시 생략: PlayerPrefs "chuon.cine.skipSeen" (1 = 켬, 기본). 체크포인트 재시작/재도전에서 이미 본 연출은 바로 끝 상태로.
/// - 시청 기록은 "회차(run)" 단위 메모리 기록이다. 타이틀/팀 로고 씬이 로드되면 초기화된다 (PlayX4 10분 세션 = 1회차).
/// - QA: 명령줄 "-qaSkipCinematics" 면 모든 연출을 즉시 끝 상태로 (봇 흐름 테스트용).
/// </summary>
public static class CinematicSettings
{
    public const string LetterboxPrefKey = "chuon.cine.letterbox";
    public const string SkipSeenPrefKey = "chuon.cine.skipSeen";

    public enum SeenScope : byte
    {
        /// <summary>시청 기록을 남기지 않는다 (매번 재생).</summary>
        None = 0,
        /// <summary>체크포인트 재시작(같은 씬 재로드)에서 생략, 새 회차에서는 다시 재생.</summary>
        Run = 1,
    }

    static readonly HashSet<string> s_seen = new HashSet<string>(StringComparer.Ordinal);
    static int s_letterbox = -1;
    static int s_skipSeen = -1;
    static int s_qaSkipAll = -1;

    /// <summary>회차 초기화 대상 씬 (이 씬이 로드되면 시청 기록을 지운다).</summary>
    static readonly string[] RunResetScenes = { "TitleScene", "TeamLogoScene" };

    public static event Action Changed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        s_seen.Clear();
        s_letterbox = -1;
        s_skipSeen = -1;
        s_qaSkipAll = -1;
        Changed = null;
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (mode != LoadSceneMode.Single)
            return;
        for (int i = 0; i < RunResetScenes.Length; i++)
        {
            if (scene.name == RunResetScenes[i])
            {
                ResetRun();
                return;
            }
        }
    }

    public static bool LetterboxEnabled
    {
        get
        {
            if (s_letterbox < 0)
                s_letterbox = PlayerPrefs.GetInt(LetterboxPrefKey, 1);
            return s_letterbox != 0;
        }
    }

    public static void SetLetterboxEnabled(bool enabled)
    {
        s_letterbox = enabled ? 1 : 0;
        PlayerPrefs.SetInt(LetterboxPrefKey, s_letterbox);
        Changed?.Invoke();
    }

    public static bool SkipSeenOnRetry
    {
        get
        {
            if (s_skipSeen < 0)
                s_skipSeen = PlayerPrefs.GetInt(SkipSeenPrefKey, 1);
            return s_skipSeen != 0;
        }
    }

    public static void SetSkipSeenOnRetry(bool enabled)
    {
        s_skipSeen = enabled ? 1 : 0;
        PlayerPrefs.SetInt(SkipSeenPrefKey, s_skipSeen);
        Changed?.Invoke();
    }

    /// <summary>QA 봇: 모든 연출을 끝 상태로 바로 넘긴다.</summary>
    public static bool QaSkipAll
    {
        get
        {
            if (s_qaSkipAll < 0)
            {
                s_qaSkipAll = 0;
                string[] args = Environment.GetCommandLineArgs();
                for (int i = 0; i < args.Length; i++)
                {
                    if (string.Equals(args[i], "-qaSkipCinematics", StringComparison.OrdinalIgnoreCase))
                    {
                        s_qaSkipAll = 1;
                        break;
                    }
                }
            }
            return s_qaSkipAll != 0;
        }
        set => s_qaSkipAll = value ? 1 : 0;
    }

    /// <summary>자막 표시 여부 (기존 대사 옵션을 그대로 따른다).</summary>
    public static bool SubtitlesEnabled => ExhibitionPrototypePresentationPolicy.DialogueEnabled;

    public static bool WasSeen(string id) => !string.IsNullOrEmpty(id) && s_seen.Contains(id);

    public static void MarkSeen(string id, SeenScope scope)
    {
        if (scope == SeenScope.None || string.IsNullOrEmpty(id))
            return;
        s_seen.Add(id);
    }

    /// <summary>재생 전 판단: 이미 봤고 재도전 생략 옵션이 켜져 있으면 true.</summary>
    public static bool ShouldAutoSkip(string id, SeenScope scope)
    {
        if (QaSkipAll)
            return true;
        return scope != SeenScope.None && SkipSeenOnRetry && WasSeen(id);
    }

    /// <summary>새 회차 (타이틀 복귀, 키오스크 리셋).</summary>
    public static void ResetRun()
    {
        s_seen.Clear();
    }

    public static int SeenCount => s_seen.Count;
}
