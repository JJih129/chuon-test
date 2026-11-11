// 파일명: KioskStaffPanel.cs
// 스태프 전용 패널 (숨김 콤보: 키보드 Ctrl+Shift+K / 패드 LB+RB+View 를 3초 길게). 키오스크가 꺼져 있어도 열 수 있다.
// 표시: 키오스크 상태, 세션 수·평균·최장 길이, 사유별 리셋 수, 마지막 리셋 사유, 통계 파일 경로.
// 조작: 1 키오스크 켜기/끄기 · 2 강제 리셋 · 3 세션 상한 켜기/끄기 · 4 통계 초기화 · 0 게임 종료 · ESC 닫기
//       패드: X 키오스크 토글 · Y 강제 리셋 · B 닫기
using System;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class KioskStaffPanel : MonoBehaviour
{
    const int SortingOrder = 9900;
    const float RefreshInterval = 0.25f;

    public enum Command
    {
        None,
        ToggleKiosk,
        ForceReset,
        ToggleSessionCap,
        ResetStats,
        QuitGame,
        Close,
    }

    Canvas _canvas;
    CanvasGroup _group;
    Text _body;
    Text _footer;
    float _nextRefreshAt;
    float _openedAt;
    readonly StringBuilder _sb = new StringBuilder(1024);
    string _message = string.Empty;
    float _messageUntil;
    bool _quitArmed;
    string _videoPath;

    public bool IsOpen { get; private set; }

    /// <summary>KioskDirector 가 상태 문구를 채운다 (현재 씬·세션 경과 등).</summary>
    public Func<string> StatusProvider;

    public static KioskStaffPanel Create(Transform parent)
    {
        GameObject go = new GameObject("KioskStaffPanel");
        go.transform.SetParent(parent, false);
        KioskStaffPanel panel = go.AddComponent<KioskStaffPanel>();
        panel.Build();
        return panel;
    }

    public void Open()
    {
        IsOpen = true;
        _openedAt = Time.unscaledTime;
        _quitArmed = false;
        _canvas.enabled = true;
        _group.alpha = 1f;
        _nextRefreshAt = 0f;
        _videoPath = KioskAttract.FindVideo(); // 디렉터리 탐색은 열 때 1회만
        Refresh();
    }

    public void Close()
    {
        IsOpen = false;
        _quitArmed = false;
        _canvas.enabled = false;
        _group.alpha = 0f;
    }

    public void Flash(string message)
    {
        _message = message;
        _messageUntil = Time.unscaledTime + 3f;
        _nextRefreshAt = 0f;
    }

    /// <summary>이번 프레임 스태프 명령 (열려 있을 때만). 콤보를 뗀 직후 0.3초는 무시.</summary>
    public Command ReadCommand()
    {
        if (!IsOpen || Time.unscaledTime - _openedAt < 0.3f)
            return Command.None;

        try
        {
            Keyboard kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.escapeKey.wasPressedThisFrame) return Command.Close;
                if (kb.digit1Key.wasPressedThisFrame || kb.numpad1Key.wasPressedThisFrame) return Command.ToggleKiosk;
                if (kb.digit2Key.wasPressedThisFrame || kb.numpad2Key.wasPressedThisFrame) return Command.ForceReset;
                if (kb.digit3Key.wasPressedThisFrame || kb.numpad3Key.wasPressedThisFrame) return Command.ToggleSessionCap;
                if (kb.digit4Key.wasPressedThisFrame || kb.numpad4Key.wasPressedThisFrame) return Command.ResetStats;
                if (kb.digit0Key.wasPressedThisFrame || kb.numpad0Key.wasPressedThisFrame)
                {
                    // 오조작 방지: 0 을 두 번
                    if (_quitArmed)
                        return Command.QuitGame;
                    _quitArmed = true;
                    Flash("게임을 종료하려면 0을 한 번 더 누르세요.");
                    return Command.None;
                }
            }

            var pads = Gamepad.all;
            for (int i = 0; i < pads.Count; i++)
            {
                Gamepad pad = pads[i];
                if (pad == null)
                    continue;
                if (pad.buttonEast.wasPressedThisFrame) return Command.Close;
                if (pad.buttonWest.wasPressedThisFrame) return Command.ToggleKiosk;
                if (pad.buttonNorth.wasPressedThisFrame) return Command.ForceReset;
            }
        }
        catch (Exception)
        {
        }

        return Command.None;
    }

    void Update()
    {
        if (!IsOpen || Time.unscaledTime < _nextRefreshAt)
            return;
        _nextRefreshAt = Time.unscaledTime + RefreshInterval;
        Refresh();
    }

    void Refresh()
    {
        KioskStatsData d = KioskStatsStore.Data;
        KioskSettings s = KioskConfig.Settings;
        _sb.Length = 0;

        bool on = KioskConfig.Enabled;
        _sb.Append("키오스크 모드      ").Append(on ? "<color=#E0303A><b>켜짐</b></color>" : "<b>꺼짐</b>");
        if (KioskConfig.EnabledByCommandLine) _sb.Append("  (명령줄 -kiosk)");
        _sb.Append('\n');

        _sb.Append("세션 상한          ");
        if (KioskConfig.SessionCapEnabled)
            _sb.Append(KioskStatsData.FormatDuration(s.SessionCapSeconds)).Append("  (1분 전 경고)");
        else
            _sb.Append("끔");
        _sb.Append('\n');

        _sb.Append("방치 타이머        게임 ").Append((int)s.GameplayIdleSeconds).Append("초 + 카운트다운 ").Append((int)s.CountdownSeconds)
           .Append("초 · 타이틀 ").Append((int)s.AttractIdleSeconds).Append("초 → 어트랙트\n");

        if (StatusProvider != null)
        {
            string status = null;
            try { status = StatusProvider(); } catch (Exception) { }
            if (!string.IsNullOrEmpty(status))
                _sb.Append(status).Append('\n');
        }

        _sb.Append('\n');
        _sb.Append("누적 세션          <b>").Append(d.sessionCount).Append("회</b>   평균 ").Append(KioskStatsData.FormatDuration(d.AverageSessionSeconds))
           .Append("   최장 ").Append(KioskStatsData.FormatDuration(d.longestSessionSeconds))
           .Append("   직전 ").Append(KioskStatsData.FormatDuration(d.lastSessionSeconds)).Append('\n');
        _sb.Append("종료 사유          완주 ").Append(d.completedCount).Append(" · 시간 상한 ").Append(d.sessionCapCount)
           .Append(" · 방치 ").Append(d.idleResetCount).Append(" · 메뉴 ").Append(d.titleMenuCount)
           .Append(" · 스태프 ").Append(d.staffResetCount).Append('\n');
        _sb.Append("어트랙트 재생      ").Append(d.attractCount).Append("회\n");
        _sb.Append("마지막 리셋        <b>").Append(KioskResetReason.Korean(d.lastResetReason)).Append("</b>");
        if (!string.IsNullOrEmpty(d.lastResetAt)) _sb.Append("  (").Append(d.lastResetAt).Append(')');
        _sb.Append('\n');
        if (!string.IsNullOrEmpty(d.firstSessionAt))
            _sb.Append("첫 세션            ").Append(d.firstSessionAt).Append('\n');
        _sb.Append("<size=14>통계 파일  ").Append(KioskStatsStore.StatsPath).Append("</size>\n");

        string video = _videoPath;
        _sb.Append("<size=14>어트랙트 영상  ").Append(string.IsNullOrEmpty(video) ? "없음 (타이틀 대체 연출)" : video).Append("</size>");

        if (!string.IsNullOrEmpty(_message) && Time.unscaledTime < _messageUntil)
            _sb.Append("\n\n<color=#E0303A><b>").Append(_message).Append("</b></color>");

        _body.text = _sb.ToString();
    }

    void Build()
    {
        _canvas = KioskUi.CreateCanvas("KioskStaffCanvas", transform, SortingOrder, out _group);
        Transform root = _canvas.transform;
        KioskUi.CreateFullscreen("Dim", root, new Color(0f, 0f, 0f, 0.55f));

        RectTransform card = KioskUi.CreateCard("Card", root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1100f, 640f));
        KioskUi.DecorateCard(card, "TSUKASA CORP.  //  STAFF CONSOLE  —  PLAYX4 2026 KIOSK");

        _body = KioskUi.CreateText("Body", card, string.Empty, 19, FontStyle.Normal, TextAnchor.UpperLeft, KioskUi.White,
            new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(28f, 96f), new Vector2(-28f, -62f));
        _body.lineSpacing = 1.15f;

        KioskUi.CreateImage("FooterRule", card, new Color(KioskUi.Line.r, KioskUi.Line.g, KioskUi.Line.b, 0.25f),
            new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(28f, 86f), new Vector2(-24f, 87f));
        _footer = KioskUi.CreateText("Footer", card,
            "[1] 키오스크 켜기/끄기   [2] 강제 리셋(타이틀)   [3] 세션 상한 켜기/끄기   [4] 통계 초기화   [0][0] 게임 종료   [ESC] 닫기\n" +
            "패드: X 키오스크 토글 · Y 강제 리셋 · B 닫기",
            16, FontStyle.Bold, TextAnchor.MiddleLeft, KioskUi.Muted,
            new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(28f, 14f), new Vector2(-24f, 82f));
    }
}
