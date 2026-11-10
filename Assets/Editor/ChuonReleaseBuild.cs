using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 전시(PlayX4) 배포용 릴리스 빌드. 메뉴 또는 MCP(ChuonReleaseBuild.Build())에서만 실행되며 스스로 돌지 않는다.
/// 1) 열린 씬 저장 → 2) 비개발(Development 끔) Windows x64 플레이어를 현재 스크립팅 백엔드 그대로
///    Builds/Release_&lt;bundleVersion&gt;/ChuOn.exe 로 빌드 → 3) 키오스크 어트랙트용 PV 영상을 &lt;빌드&gt;/PV/ 로 복사
/// → 4) README_BOOTH.txt(한국어 부스 안내) 작성 → 5) 배포 금지 폴더(*DoNotShip*, *DontShip*)를 빌드 밖으로 옮기고
/// Builds/Release_&lt;버전&gt;.zip 으로 압축. 결과 요약은 Temp/qa/release_build_report.txt.
/// </summary>
public static class ChuonReleaseBuild
{
    const string MenuPath = "Tools/Chuon/Build/Release (PlayX4)";
    const string ExeName = "ChuOn.exe";
    const string PvRelative = "Builds/PV/upload/Chuon_PV_v2_web.mp4";

    [MenuItem(MenuPath)]
    static void BuildFromMenu()
    {
        string version = PlayerSettings.bundleVersion;
        if (!EditorUtility.DisplayDialog("CHUON 릴리스 빌드",
                "버전 " + version + " 비개발 빌드를 Builds/Release_" + SafeName(version) + "/ 에 만들고 zip 으로 묶습니다.\n열린 씬은 저장됩니다.",
                "빌드", "취소"))
            return;
        Debug.Log("[ChuonReleaseBuild] " + Build());
    }

    [MenuItem(MenuPath, true)]
    static bool BuildFromMenuValidate()
    {
        return !EditorApplication.isPlayingOrWillChangePlaymode && !BuildPipeline.isBuildingPlayer;
    }

    public static string Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return "PLAYING (exit Play mode first)";

        string version = PlayerSettings.bundleVersion;
        string folderName = "Release_" + SafeName(version);
        string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        string buildsDir = Path.Combine(projectRoot, "Builds");
        string buildDir = Path.Combine(buildsDir, folderName);
        var sb = new StringBuilder();
        sb.Append("version=").Append(version)
          .Append(" product=").Append(PlayerSettings.productName)
          .Append(" company=").Append(PlayerSettings.companyName)
          .Append(" backend=").Append(PlayerSettings.GetScriptingBackend(BuildTargetGroup.Standalone)).Append('\n');

        bool saved = EditorSceneManager.SaveOpenScenes();

        var scenes = new List<string>();
        foreach (EditorBuildSettingsScene s in EditorBuildSettings.scenes)
        {
            if (s.enabled)
                scenes.Add(s.path);
        }
        if (scenes.Count == 0)
            return Finish(projectRoot, sb.Append("ABORT: no enabled scenes in Build Settings\n"));

        // 이전 같은 버전 빌드가 남아 있으면 지운다 (이전 산출물이 섞여 zip 에 들어가지 않도록).
        if (Directory.Exists(buildDir))
            Directory.Delete(buildDir, true);

        var options = new BuildPlayerOptions
        {
            scenes = scenes.ToArray(),
            locationPathName = Path.Combine(buildDir, ExeName),
            target = BuildTarget.StandaloneWindows64,
            targetGroup = BuildTargetGroup.Standalone,
            options = BuildOptions.None // 비개발: DEVELOPMENT_BUILD 심볼·프로파일러·개발 콘솔 없음
        };

        // 릴리스에서는 Log/Warning 스택 트레이스 수집을 끈다(남은 Debug.Log 비용 절감). 에러·예외는 그대로 남긴다.
        // 개발/QA 빌드 설정을 바꾸지 않도록 빌드가 끝나면 원래 값으로 되돌린다.
        StackTraceLogType prevLog = PlayerSettings.GetStackTraceLogType(LogType.Log);
        StackTraceLogType prevWarn = PlayerSettings.GetStackTraceLogType(LogType.Warning);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        BuildReport report;
        try
        {
            PlayerSettings.SetStackTraceLogType(LogType.Log, StackTraceLogType.None);
            PlayerSettings.SetStackTraceLogType(LogType.Warning, StackTraceLogType.None);
            report = BuildPipeline.BuildPlayer(options);
        }
        finally
        {
            PlayerSettings.SetStackTraceLogType(LogType.Log, prevLog);
            PlayerSettings.SetStackTraceLogType(LogType.Warning, prevWarn);
        }
        watch.Stop();

        sb.Append("scenesSaved=").Append(saved)
          .Append(" scenes=").Append(scenes.Count)
          .Append(" result=").Append(report.summary.result)
          .Append(" size=").Append((report.summary.totalSize / 1048576.0).ToString("F1")).Append("MB")
          .Append(" time=").Append(watch.Elapsed.TotalSeconds.ToString("F0")).Append("s")
          .Append(" errors=").Append(report.summary.totalErrors)
          .Append(" warnings=").Append(report.summary.totalWarnings).Append('\n');
        AppendMessages(report, sb);

        if (report.summary.result != BuildResult.Succeeded)
            return Finish(projectRoot, sb.Append("ABORT: build did not succeed, no packaging\n"));

        try
        {
            // 키오스크 어트랙트 영상: KioskDirector 탐색 순서의 "<exe 폴더>/PV" 에 해당.
            string pvSrc = Path.Combine(projectRoot, PvRelative);
            if (File.Exists(pvSrc))
            {
                string pvDir = Path.Combine(buildDir, "PV");
                Directory.CreateDirectory(pvDir);
                File.Copy(pvSrc, Path.Combine(pvDir, Path.GetFileName(pvSrc)), true);
                sb.Append("pv=copied ").Append(Path.GetFileName(pvSrc)).Append('\n');
            }
            else
            {
                sb.Append("pv=MISSING ").Append(PvRelative).Append(" (attract loop falls back to title showcase)\n");
            }

            File.WriteAllText(Path.Combine(buildDir, "README_BOOTH.txt"),
                BoothReadme(version).Replace("\r\n", "\n").Replace("\n", "\r\n"), new UTF8Encoding(true));

            // 배포 금지 폴더(IL2CPP 백업, Burst 디버그 정보)는 zip 에 넣지 않고 옆으로 옮겨 보관한다.
            string symbolsDir = Path.Combine(buildsDir, folderName + "_symbols");
            foreach (string dir in Directory.GetDirectories(buildDir))
            {
                string name = Path.GetFileName(dir);
                if (name.IndexOf("DoNotShip", StringComparison.OrdinalIgnoreCase) < 0 &&
                    name.IndexOf("DontShip", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                Directory.CreateDirectory(symbolsDir);
                string dst = Path.Combine(symbolsDir, name);
                if (Directory.Exists(dst))
                    Directory.Delete(dst, true);
                Directory.Move(dir, dst);
                sb.Append("moved-out=").Append(name).Append('\n');
            }

            string zipPath = Path.Combine(buildsDir, folderName + ".zip");
            if (File.Exists(zipPath))
                File.Delete(zipPath);
            ZipFile.CreateFromDirectory(buildDir, zipPath, System.IO.Compression.CompressionLevel.Optimal, true);
            sb.Append("zip=").Append(zipPath).Append(" (")
              .Append((new FileInfo(zipPath).Length / 1048576.0).ToString("F1")).Append("MB)\n");
        }
        catch (Exception e)
        {
            sb.Append("PACKAGING ERROR: ").Append(e.GetType().Name).Append(": ").Append(e.Message).Append('\n');
        }

        sb.Append("output=").Append(buildDir).Append('\n');
        return Finish(projectRoot, sb);
    }

    static void AppendMessages(BuildReport report, StringBuilder sb)
    {
        int lines = 0;
        foreach (BuildStep step in report.steps)
        {
            foreach (BuildStepMessage m in step.messages)
            {
                if (m.type == LogType.Log || lines++ > 60)
                    continue;
                string content = m.content.Length > 300 ? m.content.Substring(0, 300) : m.content;
                sb.Append('[').Append(m.type).Append("] ").Append(content).Append('\n');
            }
        }
    }

    static string Finish(string projectRoot, StringBuilder sb)
    {
        string qaDir = Path.Combine(projectRoot, "Temp", "qa");
        Directory.CreateDirectory(qaDir);
        File.WriteAllText(Path.Combine(qaDir, "release_build_report.txt"), sb.ToString());
        return sb.ToString();
    }

    static string SafeName(string s)
    {
        if (string.IsNullOrEmpty(s))
            return "0";
        foreach (char c in Path.GetInvalidFileNameChars())
            s = s.Replace(c, '_');
        return s.Replace(' ', '_');
    }

    static string BoothReadme(string version)
    {
        return
"CHUON (추온) — PlayX4 2026 전시 데모 " + version + "\n" +
"====================================================\n" +
"\n" +
"[1] 실행\n" +
"- 이 폴더를 통째로 부스 PC 에 복사한다 (예: D:\\Chuon\\). ChuOn.exe 와 ChuOn_Data 폴더는 항상 같이 있어야 한다.\n" +
"- 전시용 실행: ChuOn.exe 바로가기를 만들고 대상 끝에 -kiosk 를 붙인다.\n" +
"    \"D:\\Chuon\\ChuOn.exe\" -kiosk\n" +
"- 재부팅 후 자동 실행: Win+R → shell:startup → 위 바로가기를 넣는다.\n" +
"- 처음 한 번은 -kiosk 없이 실행해 옵션에서 해상도·전체화면·품질·마스터 볼륨을 정한 뒤 종료한다.\n" +
"  (키오스크는 켜질 때의 화면 설정을 기준으로 지킨다.)\n" +
"- 어트랙트(타이틀 60초 방치 시 PV 반복 재생) 영상: PV\\Chuon_PV_v2_web.mp4 (자동 탐색).\n" +
"  다른 영상을 쓰려면 -kioskVideo \"경로\" 를 덧붙인다.\n" +
"- QR 이미지를 데모 종료 카드에 넣으려면 ChuOn_Data\\StreamingAssets\\Kiosk\\qr.png 로 복사한다.\n" +
"\n" +
"[2] 스태프 패널 (숨김)\n" +
"- 키보드: Ctrl + Shift + K 를 3초 누르고 있기\n" +
"- 게임패드: LB + RB + View(Select) 를 3초 누르고 있기\n" +
"- 열려 있는 동안 게임은 일시정지된다.\n" +
"    1 / 패드 X : 키오스크 켜기·끄기 (저장됨)\n" +
"    2 / 패드 Y : 강제 리셋 → 타이틀\n" +
"    3          : 세션 상한(10분) 켜기·끄기\n" +
"    4          : 누적 통계 초기화 (세션 CSV 는 유지)\n" +
"    0 두 번    : 게임 종료 (키오스크 중 유일한 종료 방법, Alt+F4 는 막혀 있음)\n" +
"    ESC / 패드 B : 닫기\n" +
"\n" +
"[3] 키오스크 동작 요약\n" +
"- 게임 중 90초 입력 없음 → \"계속하시겠습니까?\" 10초 → 타이틀로 리셋.\n" +
"- 세션 10분: 9분에 안내 토스트, 10분에 데모 종료 카드 → 타이틀 (보스전·연출 중이면 최대 90초 유예).\n" +
"- 명령줄 조정: -kioskIdle 초, -kioskCountdown 초, -kioskAttractIdle 초, -kioskSessionCap 초(0=끔),\n" +
"  -kioskAllowWindowed (창 모드 허용), -noKiosk (이번 실행만 끔).\n" +
"\n" +
"[4] 문제 해결\n" +
"- 게임이 멈춘 것 같다: 스태프 콤보 → 2 (강제 리셋). 그래도 안 되면 0 두 번으로 종료 후 다시 실행.\n" +
"- 종료가 안 된다: 키오스크 중에는 Alt+F4·창 닫기가 막혀 있다. 스태프 패널 0 두 번. 최후 수단은 작업 관리자.\n" +
"- 검은 화면 / 시작 안 됨: 그래픽 드라이버 최신화, ChuOn_Data 폴더가 exe 옆에 있는지 확인.\n" +
"  로그: %USERPROFILE%\\AppData\\LocalLow\\" + PlayerSettings.companyName + "\\" + PlayerSettings.productName + "\\Player.log\n" +
"- 어트랙트 영상이 안 나온다: 스태프 패널의 '어트랙트 영상 경로' 확인. 없으면 대체 연출(로고·기능 문구)이 나온다.\n" +
"- 패드가 안 먹는다: 유선 연결, 패드 하나만 연결, Steam 실행 중이면 Steam Input 끄기.\n" +
"- Shift 를 여러 번 눌러 고정 키 창이 뜬다: 설정 > 접근성 > 키보드 > 고정 키 '바로 가기 키' 끄기 (필터 키도).\n" +
"- 처음 실행 때 방화벽 창: 취소 (네트워크를 쓰지 않는다).\n" +
"- 소리가 작다/크다: 옵션 마스터 볼륨(키오스크 중 하한 35%) 또는 헤드폰·스피커 하드웨어 볼륨.\n" +
"- 통계: 같은 로그 폴더의 kiosk_stats.json, kiosk_sessions.csv. 폐장 후 Player.log 와 함께 백업.\n" +
"\n" +
"[5] 부스 PC 설정 (스태프가 직접)\n" +
"- 전원: 화면 끄기·절전 안 함 / 화면 보호기 없음 / 방해 금지 켜기 / Windows 업데이트 일시 중지\n" +
"- Xbox Game Bar 끄기, Steam·Discord 오버레이 끄기, 모니터 1920x1080 · 배율 100% 권장\n" +
"- 자세한 체크리스트: 프로젝트 문서 Docs/Exhibition_Kiosk.md 3장\n";
    }
}
