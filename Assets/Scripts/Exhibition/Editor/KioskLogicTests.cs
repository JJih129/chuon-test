// 파일명: KioskLogicTests.cs
// 키오스크 모드 순수 로직 EditMode 테스트 (Window > General > Test Runner > EditMode).
using System;
using NUnit.Framework;

public sealed class KioskLogicTests
{
    // ------------------------------------------------------------------
    // 방치 타이머
    // ------------------------------------------------------------------

    static void Advance(KioskIdleTracker t, float seconds, bool frozen = false, float step = 0.1f)
    {
        int ticks = (int)Math.Round(seconds / step);
        for (int i = 0; i < ticks; i++)
            t.Tick(step, activity: false, frozen: frozen);
    }

    [Test]
    public void Idle_StaysActiveBeforeThreshold()
    {
        var t = new KioskIdleTracker(90f, 10f);
        Advance(t, 89f);
        Assert.AreEqual(KioskIdlePhase.Active, t.Phase);
    }

    [Test]
    public void Idle_CountdownThenExpire()
    {
        var t = new KioskIdleTracker(90f, 10f);
        Advance(t, 90.2f);
        Assert.AreEqual(KioskIdlePhase.Countdown, t.Phase);
        Assert.AreEqual(10, t.CountdownDisplay);
        Advance(t, 5f);
        Assert.AreEqual(KioskIdlePhase.Countdown, t.Phase);
        Assert.AreEqual(5, t.CountdownDisplay);
        Advance(t, 5.1f);
        Assert.AreEqual(KioskIdlePhase.Expired, t.Phase);
        Assert.AreEqual(0, t.CountdownDisplay);
    }

    [Test]
    public void Idle_InputDuringCountdownContinues()
    {
        var t = new KioskIdleTracker(90f, 10f);
        Advance(t, 93f);
        Assert.AreEqual(KioskIdlePhase.Countdown, t.Phase);
        t.Tick(0.016f, activity: true, frozen: false);
        Assert.AreEqual(KioskIdlePhase.Active, t.Phase);
        Assert.AreEqual(0f, t.IdleSeconds);
    }

    [Test]
    public void Idle_FrozenDoesNotAccumulate()
    {
        var t = new KioskIdleTracker(90f, 10f);
        Advance(t, 80f);
        Advance(t, 60f, frozen: true); // 시네마틱 1분
        Assert.AreEqual(KioskIdlePhase.Active, t.Phase);
        Assert.AreEqual(80f, t.IdleSeconds, 0.05f);
    }

    [Test]
    public void Idle_HitchClampedPerFrame()
    {
        var t = new KioskIdleTracker(90f, 10f);
        t.Tick(30f, activity: false, frozen: false); // 로딩 히치 30 s
        Assert.AreEqual(KioskIdleTracker.MaxStep, t.IdleSeconds, 1e-4f);
    }

    [Test]
    public void Idle_ZeroCountdownExpiresAtThreshold()
    {
        var t = new KioskIdleTracker(60f, 0f); // 타이틀 → 어트랙트
        Advance(t, 60.2f);
        Assert.AreEqual(KioskIdlePhase.Expired, t.Phase);
    }

    // ------------------------------------------------------------------
    // 세션 시계
    // ------------------------------------------------------------------

    [Test]
    public void Session_WarnsOnceThenCaps()
    {
        var c = new KioskSessionClock();
        c.Configure(600f, 60f, 90f);
        c.Start();

        int warnings = 0, caps = 0;
        for (int i = 0; i < 6100; i++)
        {
            KioskSessionEvent e = c.Tick(0.1f, deferCap: false);
            if (e == KioskSessionEvent.Warning) { warnings++; Assert.AreEqual(540f, c.Elapsed, 0.2f); }
            if (e == KioskSessionEvent.CapReached) { caps++; Assert.AreEqual(600f, c.Elapsed, 0.2f); }
        }

        Assert.AreEqual(1, warnings);
        Assert.AreEqual(1, caps);
    }

    [Test]
    public void Session_DeferIsBounded()
    {
        var c = new KioskSessionClock();
        c.Configure(600f, 60f, 90f);
        c.Start();
        float cappedAt = -1f;
        for (int i = 0; i < 8000 && cappedAt < 0f; i++)
        {
            if (c.Tick(0.1f, deferCap: true) == KioskSessionEvent.CapReached)
                cappedAt = c.Elapsed;
        }
        Assert.AreEqual(690f, cappedAt, 0.2f);
    }

    [Test]
    public void Session_CapZeroNeverFires()
    {
        var c = new KioskSessionClock();
        c.Configure(0f, 60f, 90f);
        c.Start();
        for (int i = 0; i < 1000; i++)
            Assert.AreEqual(KioskSessionEvent.None, c.Tick(1f, false));
        Assert.AreEqual(1000f, c.Stop(), 0.01f);
        Assert.IsFalse(c.Running);
    }

    // ------------------------------------------------------------------
    // 명령줄
    // ------------------------------------------------------------------

    [Test]
    public void Parse_DefaultsAreOff()
    {
        KioskSettings s = KioskSettings.Parse(new[] { "ChuOn.exe" });
        Assert.IsFalse(s.CommandLineEnabled.HasValue);
        Assert.IsFalse(s.AutomationRun);
        Assert.AreEqual(90f, s.GameplayIdleSeconds);
        Assert.AreEqual(10f, s.CountdownSeconds);
        Assert.AreEqual(60f, s.AttractIdleSeconds);
        Assert.AreEqual(600f, s.SessionCapSeconds);
    }

    [Test]
    public void Parse_KioskFlagsAndOverrides()
    {
        KioskSettings s = KioskSettings.Parse(new[]
        {
            "ChuOn.exe", "-KIOSK", "-kioskIdle", "45", "-kioskAttractIdle", "30.5", "-kioskSessionCap", "0",
            "-kioskVideo", "D:/PV/a.mp4", "-kioskAllowWindowed",
        });
        Assert.AreEqual(true, s.CommandLineEnabled);
        Assert.AreEqual(45f, s.GameplayIdleSeconds);
        Assert.AreEqual(30.5f, s.AttractIdleSeconds);
        Assert.AreEqual(0f, s.SessionCapSeconds);
        Assert.AreEqual("D:/PV/a.mp4", s.VideoPath);
        Assert.IsTrue(s.AllowWindowed);
    }

    [Test]
    public void Parse_NoKioskAndAutomation()
    {
        KioskSettings s = KioskSettings.Parse(new[] { "x", "-noKiosk", "-qaFullRun", "-kioskIdle", "abc" });
        Assert.AreEqual(false, s.CommandLineEnabled);
        Assert.IsTrue(s.AutomationRun);
        Assert.AreEqual(90f, s.GameplayIdleSeconds); // 잘못된 값은 무시
    }

    [Test]
    public void Parse_ClampsTinyIdle()
    {
        KioskSettings s = KioskSettings.Parse(new[] { "x", "-kioskIdle", "1", "-kioskCountdown", "999" });
        Assert.AreEqual(5f, s.GameplayIdleSeconds);
        Assert.AreEqual(60f, s.CountdownSeconds);
    }

    [Test]
    public void SceneClassification()
    {
        Assert.IsTrue(KioskConfig.IsGameplayScene("Tutorial"));
        Assert.IsTrue(KioskConfig.IsGameplayScene("Level_Facility"));
        Assert.IsFalse(KioskConfig.IsGameplayScene("TitleScene"));
        Assert.IsFalse(KioskConfig.IsGameplayScene("BootScene"));
        Assert.IsFalse(KioskConfig.IsGameplayScene("CreditsScene"));
        Assert.IsFalse(KioskConfig.IsGameplayScene("TeamLogoScene"));
        Assert.IsTrue(KioskConfig.StartsSession("BootScene"));
        Assert.IsFalse(KioskConfig.StartsSession("CreditsScene"));
    }

    // ------------------------------------------------------------------
    // 통계
    // ------------------------------------------------------------------

    [Test]
    public void Stats_AverageAndReasons()
    {
        var d = new KioskStatsData();
        var now = new DateTime(2026, 11, 20, 10, 0, 0);
        d.RecordSession(600, KioskResetReason.SessionCap, now);
        d.RecordSession(300, KioskResetReason.Idle, now.AddMinutes(11));
        d.RecordSession(450, KioskResetReason.Completed, now.AddMinutes(20));
        d.RecordReset(KioskResetReason.Completed, now.AddMinutes(20));

        Assert.AreEqual(3, d.sessionCount);
        Assert.AreEqual(450d, d.AverageSessionSeconds, 1e-6);
        Assert.AreEqual(600d, d.longestSessionSeconds, 1e-6);
        Assert.AreEqual(1, d.sessionCapCount);
        Assert.AreEqual(1, d.idleResetCount);
        Assert.AreEqual(1, d.completedCount);
        Assert.AreEqual("2026-11-20 10:00:00", d.firstSessionAt);
        Assert.AreEqual(KioskResetReason.Completed, d.lastResetReason);
    }

    [Test]
    public void Stats_JsonRoundTrip()
    {
        var d = new KioskStatsData();
        d.RecordSession(123.4, KioskResetReason.Staff, new DateTime(2026, 11, 20, 9, 30, 0));
        string json = UnityEngine.JsonUtility.ToJson(d);
        KioskStatsData back = UnityEngine.JsonUtility.FromJson<KioskStatsData>(json);
        Assert.AreEqual(1, back.sessionCount);
        Assert.AreEqual(1, back.staffResetCount);
        Assert.AreEqual(123.4d, back.totalSessionSeconds, 1e-6);
    }

    [Test]
    public void FormatDuration()
    {
        Assert.AreEqual("0:00", KioskStatsData.FormatDuration(0));
        Assert.AreEqual("1:05", KioskStatsData.FormatDuration(65.9));
        Assert.AreEqual("10:00", KioskStatsData.FormatDuration(600));
        Assert.AreEqual("0:00", KioskStatsData.FormatDuration(double.NaN));
    }
}
