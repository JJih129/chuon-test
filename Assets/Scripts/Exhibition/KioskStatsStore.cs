// 파일명: KioskStatsStore.cs
// 키오스크 통계를 Application.persistentDataPath 에 저장한다.
//  - kiosk_stats.json : 누적 통계 (스태프 패널 표시)
//  - kiosk_sessions.csv : 세션 1회당 한 줄 (종료 시각, 길이(초), 사유, 마지막 씬)
// 파일 IO 실패는 경고만 남기고 게임을 멈추지 않는다.
using System;
using System.Globalization;
using System.IO;
using UnityEngine;

public static class KioskStatsStore
{
    public const string StatsFileName = "kiosk_stats.json";
    public const string SessionLogFileName = "kiosk_sessions.csv";

    static KioskStatsData s_data;
    static bool s_warned;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        s_data = null;
        s_warned = false;
    }

    public static string Directory
    {
        get
        {
            try { return Application.persistentDataPath; }
            catch { return string.Empty; }
        }
    }

    public static string StatsPath => Path.Combine(Directory, StatsFileName);
    public static string SessionLogPath => Path.Combine(Directory, SessionLogFileName);

    public static KioskStatsData Data
    {
        get
        {
            if (s_data == null)
                s_data = Load();
            return s_data;
        }
    }

    static KioskStatsData Load()
    {
        try
        {
            string path = StatsPath;
            if (File.Exists(path))
            {
                KioskStatsData loaded = JsonUtility.FromJson<KioskStatsData>(File.ReadAllText(path));
                if (loaded != null)
                    return loaded;
            }
        }
        catch (Exception e)
        {
            Warn("load failed: " + e.Message);
        }

        return new KioskStatsData();
    }

    public static void Save()
    {
        if (s_data == null)
            return;

        try
        {
            string dir = Directory;
            if (string.IsNullOrEmpty(dir))
                return;
            System.IO.Directory.CreateDirectory(dir);
            string path = StatsPath;
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonUtility.ToJson(s_data, true));
            if (File.Exists(path))
                File.Delete(path);
            File.Move(tmp, path);
        }
        catch (Exception e)
        {
            Warn("save failed: " + e.Message);
        }
    }

    public static void RecordSession(double seconds, string reason, string lastScene)
    {
        DateTime now = DateTime.Now;
        Data.RecordSession(seconds, reason, now);
        Data.RecordReset(reason, now);
        Save();
        AppendSessionLog(now, seconds, reason, lastScene);
    }

    public static void RecordReset(string reason)
    {
        Data.RecordReset(reason, DateTime.Now);
        Save();
    }

    public static void RecordAttract()
    {
        Data.attractCount++;
        Save();
    }

    /// <summary>스태프 패널: 누적 통계 초기화 (세션 CSV 는 보존).</summary>
    public static void ResetStats()
    {
        s_data = new KioskStatsData();
        Save();
    }

    static void AppendSessionLog(DateTime now, double seconds, string reason, string lastScene)
    {
        try
        {
            string dir = Directory;
            if (string.IsNullOrEmpty(dir))
                return;
            string path = SessionLogPath;
            bool header = !File.Exists(path);
            string line = KioskStatsData.Stamp(now) + "," + seconds.ToString("0.0", CultureInfo.InvariantCulture) + ","
                + (reason ?? string.Empty) + "," + (lastScene ?? string.Empty).Replace(",", " ") + Environment.NewLine;
            File.AppendAllText(path, (header ? "ended_at,seconds,reason,last_scene" + Environment.NewLine : string.Empty) + line);
        }
        catch (Exception e)
        {
            Warn("session log failed: " + e.Message);
        }
    }

    static void Warn(string message)
    {
        if (s_warned)
            return;
        s_warned = true;
        Debug.LogWarning("[Kiosk] stats " + message);
    }
}
