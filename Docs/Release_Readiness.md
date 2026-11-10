# 출시 준비 점검 (PlayX4 2026 데모, 2026-11-10)

읽기 전용 점검이다. 기준은 `Docs/Roadmap_Loop.md` 출시 게이트, 반복 기록·커밋 메시지(`git log`), `Docs/PV_Features.md`, `Docs/Exhibition_Kiosk.md` 이다.
수치는 모두 **개발 빌드** QA 봇 측정값이고 측정 PC는 개발 노트북(RTX 2050)이다. 목표 사양 PC에서는 아직 재지 않았다.
지금 보스 오버홀(`Boss/**`, `Player/**`, `Combat/**`, `Tutorial/**`, `Cinematics/**`) 작업이 진행 중이다. 아래 근거 중 보스전 수치(난이도, 텔레그래프, FPS)는 오버홀이 합쳐지면 **다시 측정해야** 한다.

## 0. 이번 준비 작업으로 바뀐 것
| 항목 | 내용 |
|---|---|
| 플레이어 설정 | productName `CHUON`, companyName `Team Chuon`(기존 `DefaultCompany`는 사실상 미설정), 버전 `0.9.0-playx4`, 창 없는 전체화면 + 네이티브 해상도, **Alt+Enter 전환 끔**(키오스크 요청), 창 크기 조절 끔, 백그라운드 실행·표시 켬, 단일 화면 캡처 끔. 스크립팅 백엔드(Mono)·API 호환성(.NET Standard 2.0)·그래픽 API(D3D11, D3D12)는 그대로 둠 |
| 아이콘 | `Assets/Art/ChuonPolish/Branding/ChuonIcon_{1024,512,256,128,64,48,32,24,16}.png`, `ChuonIcon.ico`. 생성기 `Tools/ArtGen/chuon_icon.py` (흑연 바탕, 세라믹 화이트 카타나, 위상단절 베기로 어긋난 화면과 TSUKASA 레드 베기선) |
| 릴리스 빌드 | 메뉴 `Tools/Chuon/Build/Release (PlayX4)` (`Assets/Editor/ChuonReleaseBuild.cs`). 비개발 빌드 → `Builds/Release_<버전>/ChuOn.exe`, PV를 `<빌드>/PV/`로 복사, `README_BOOTH.txt`, 배포 금지 폴더는 `_symbols`로 분리, `Builds/Release_<버전>.zip`. 빌드 중에는 Log·Warning 스택 트레이스를 끄고 끝나면 원래 값으로 되돌림. **아직 실행 안 함** |
| 주의 | companyName이 바뀌어 PlayerPrefs와 `persistentDataPath`(`LocalLow\Team Chuon\CHUON`) 위치가 바뀐다. 기존 QA 빌드의 저장 옵션·키오스크 통계는 새 빌드로 넘어오지 않는다. 부스 PC는 새로 설치하므로 문제없다 |

## 1. 출시 게이트 현황
| # | 게이트 | 상태 | 근거 | 남은 것 |
|---|---|---|---|---|
| 1 | 전체 흐름(TeamLogo → Credits) 개발·릴리스 빌드 모두 에러·예외 0 | **절반** | 개발 빌드 플로 스모크(연출 포함) 에러 0 / 예외 0 / 에러 셰이더 렌더러 0 (QA_Iter15, 커밋 `d4cced90`). 반복 14C도 0/0 | **릴리스 빌드는 아직 만든 적 없음.** QA 자동화(`QaCommandLine`, `QaCombatBot`)가 `#if DEVELOPMENT_BUILD`로 감싸져 있어 릴리스 빌드에서는 `-qaFlowSmoke`가 동작하지 않는다. 사람이 끝까지 플레이하고 `Player.log`에서 `Exception`·`Error`를 확인하거나, 릴리스에서도 쓸 수 있는 최소 스모크 훅이 필요하다 (§3 P1) |
| 2 | 키보드·마우스와 게임패드로 처음부터 끝까지 플레이 가능 | **부분** | 게임패드 전체 지원, 메뉴 내비게이션, 글리프 전환, 리바인딩 (반복 13B `b528c312`), 패드 메뉴 내비게이션 (14A `a700acc8`). 봇 플레이는 키보드 경로만 탔다 | 실제 패드로 처음부터 끝까지 사람 플레이 (로드맵 "실제 패드 UI 검증 남음"). 키오스크의 패드 콤보(LB+RB+View)도 실기 확인 필요 |
| 3 | 보스전 1% 저점 60 FPS 이상(목표 사양), 100 ms 이상 히치 0 | **개발 PC에서 충족, 목표 PC 미측정** | 보스전 평균 120 / 1% 저점 104 (14S), 튜토리얼 113 / 85, 시네마틱 1% 저점 79–118. 최악 프레임: 풀런 42 ms (15), 궁극기 시작 34 ms, 시네마틱 첫 프레임 경보 78 → 9 ms, 엘리베이터 59 → 24 ms (15) | 목표 사양 PC + **릴리스 빌드**로 다시 측정. 보스 오버홀(새 패턴·VFX)이 합쳐진 뒤 재측정 |
| 4 | 난이도: 봇 0.9 승, 0.6 위협, 0.4 대부분 패배, 사람 반응 지연 봇 기준 공정 | **미충족** | 반복 15: 0.9 9/10, 0.6 2/3, 0.4 **2/3 (너무 관대)**. 반응 지연 봇 옵션 `-qaBotDelay` / `QaCombatBot.reactionTime` 있음 | 0.4 승률을 낮춰야 한다. 반응 지연 봇(예: 0.25 s)으로 공정성 측정 기록 없음. 오버홀 뒤 다시 측정 |
| 5 | 모든 텔레그래프의 시각·청각 신호 일치, 경고 시간 오차 ±0.03 s | **기존 패턴 충족, 신규 패턴 미검증** | 정직한 경고 최대 오차 0.005 s (12), 바이저·무기·바닥 데칼·타입별 큐 효과음 3레이어 (10), 드론 0.65 s 정직 경고 (12 후속) | 보스 오버홀의 새 패턴(`Docs/Boss_TypeD_Phases.md`)에 대한 경고 정직성·큐 사운드 측정 |
| 6 | 대사·UI 텍스트가 설정과 일치, 오탈자 없음, 자막 옵션 동작 | **부분** | 대사 재작성·인트로 대사 (9B), 자막 크기·배경·레터박스 옵션 (10, 13), 퀘스트 문구 줄바꿈 (15) | 전체 교정 패스 기록 없음. `DialogueTable_KO.asset` 이 지금 수정 중(작업 트리 변경). 키오스크 문구·데모 종료 카드 포함 최종 교정 필요 |
| 7 | 옵션 전부 저장·적용, 난이도 선택 동작 | **부분** | 옵션 버그 5건 수정 (6), 해상도 W×H@Hz 저장·리바인딩·타임라인 오디오 (13B), 흑백 끄기·번쩍임 0 (12A), 난이도 프로필 쉬움/보통/어려움 | 타임라인 오디오 볼륨 Play 검증 남음 (로드맵). 릴리스 빌드에서 저장 → 재실행 → 적용 확인. companyName 변경으로 저장 위치가 바뀌었으니 새 경로에서 확인 |
| 8 | 플레이스홀더 아트 0 | **미충족** | 엔딩 실루엣 → 츠카사 홀로그램 연출 (14C), 증기 흰 네모 수정 (15) | `Resources/Ultimate/UltimateSequence_Default.asset` 이 `Prefabs/Generated/UltimateTempVfx/UltimateTemp_*` (임시 VFX 7종)를 참조한다. Resources라 빌드에 들어간다. 도시 블록 아트 패스, 츠카사 홀로그램 디테일 미완 (로드맵 15) |
| 9 | 오디오 믹스·볼륨 일관성, 의도된 무음 | **부분** | ChuonMixer(리미터·더킹)·MusicDirector (11), 시네마틱 오디오, PV -13.9 LUFS | BGM은 절차 생성 임시 음원이다(정식 음원은 라이선스 확인 후 교체). 게임 내 라우드니스 측정 기록 없음 |
| 10 | 크래시 없이 30분 연속 플레이 (QA 봇 반복 루프) | **미충족** | 단일 풀런만 있다 (`-qaFullRun`). `QaCommandLine` 에 반복 루프 옵션이 없다 | `-qaLoop N` / `-qaDuration` 기반 소크 모드를 추가하고 30분 돌려 메모리·핸들·예외를 기록. 키오스크 리셋 루프(방치 → 타이틀 → 어트랙트 반복) 소크도 같이 |

추가 (게이트 밖, 전시 필수)
- **키오스크 모드**: 코드와 EditMode 로직 테스트 17개는 통과 (`460e26ae`). **Play·빌드 검증은 아직 안 함** (`Docs/Exhibition_Kiosk.md` §5 체크리스트 전체 미체크).
- **PV**: v2 완성 (`Builds/PV/upload/Chuon_PV_v2_web.mp4`, 64 MB). 릴리스 빌드 스크립트가 `<빌드>/PV/` 로 복사한다 (키오스크 탐색 순서의 `<exe 폴더>/PV`).
- **QR 이미지**: `StreamingAssets/Kiosk/qr.png` 가 없다 (`Assets/StreamingAssets` 폴더 없음). 넣지 않으면 데모 종료 카드에 "QR" 자리 표시가 나온다.

## 2. 정적 코드 점검 (성능·안정성)

방법: `Assets/Scripts` 런타임 코드(에디터 폴더 제외)를 스캔했다. 대상은 `Update`·`LateUpdate`·`FixedUpdate`·`OnGUI`·`OnTriggerStay` 본문의 Find·GetComponent·LINQ·문자열 결합·할당, 개발 가드 없는 `Debug.Log`, 씬 전환의 null 가드, 개발 도구의 `#if` 가드다. 가능한 곳은 문맥을 직접 읽어 스로틀·캐시 여부를 확인했다.
**소유** 열이 "보스"인 파일은 보스 오버홀 에이전트 담당 영역(`Boss/**`, `Player/**`, `Combat/**`, `Tutorial/**`, `Cinematics/**`, `LobbyManager`)이라 이번에는 고치지 않았다. 후속으로 넘긴다.

### 2.1 우선순위 목록
| 순위 | 위험 | 위치 | 내용 | 권장 | 소유 |
|---|---|---|---|---|---|
| P1 | 출시 검증 공백 | `Assets/Scripts/Debug/QaCommandLine.cs:1` (`#if DEVELOPMENT_BUILD && !UNITY_EDITOR`) | 릴리스 빌드에서는 플로 스모크·봇 자동화가 전부 빠진다. 그래서 게이트 1의 "릴리스 빌드 에러 0"을 자동으로 증명할 수 없다 | 릴리스에서도 동작하는 최소 스모크(예: `-releaseSmoke` → 씬 순회 + `Application.logMessageReceived` 에러 카운트 → exit code) 또는 수동 플레이 + `Player.log` 확인 절차 | Debug (작업 트리에서 수정 중) |
| P1 | 안정성 미검증 | `QaCommandLine.cs` 옵션 목록 (`-qaBot -qaBotDelay -qaDialogue -qaDifficulty -qaDuration -qaFlowSmoke -qaFullRun -qaNoFontWarmup -qaOcclusionShot -qaQuit -qaScene -qaSkipToBoss -qaTutorialRun`) | 30분 연속 루프 모드가 없다 (게이트 10) | 풀런을 반복하면서 매 회 `GC.GetTotalMemory`·`Profiler.GetTotalAllocatedMemoryLong`·오브젝트 수를 기록하는 소크 모드 추가 | Debug |
| P1 | 플레이스홀더 출하 | `Assets/Resources/Ultimate/UltimateSequence_Default.asset` → `Assets/Prefabs/Generated/UltimateTempVfx/*.prefab` | 이름이 "Temp"인 임시 궁극기 VFX 7종이 Resources로 빌드에 포함된다. 실제로 쓰이는지, 아니면 대체 연출로 덮이는지 확인 필요 | 궁극기 연출 경로에서 실제 사용 여부를 확인하고, 쓰이면 교체, 안 쓰이면 참조 제거 | 보스/궁극기 |
| P2 | 매 프레임 GetComponent | `Assets/Scripts/Interaction/InteractionManager.cs:213`, `:216` | 레이캐스트가 맞을 때마다 `GetComponentInParent<Transform>()` + `GetComponentInParent<IInteractable>()`. 213행은 `hit.collider.transform` 이면 충분하다 | 마지막 콜라이더 → IInteractable 캐시 (콜라이더가 같으면 생략) | 공용 (보스 아님) |
| P2 | 매 프레임 GetComponent | `Assets/Scripts/Tutorial/Infiltration/InfiltrationTutorialDirector.cs:398`, `:406`, `:423` | 레슨 체력 하한 루프에서 적마다 매 프레임 `GetComponent<EnemyHealth>()` | `EnemyBrain` 등록 시 `EnemyHealth` 를 같이 캐시 | 보스 (Tutorial) |
| P2 | 물리 스텝마다 GetComponent ×4 | `Assets/Scripts/Tutorial/Infiltration/InfScannerCurtain.cs:84`, `:88`, `:90`, `:114` | `OnTriggerStay` 안에서 Dodge·Health·IDamageReceiver·CharacterController 조회 (플레이어가 커튼 안에 있는 동안 계속) | 루트별로 한 번 캐시 (`OnTriggerEnter` 에서) | 보스 (Tutorial) |
| P3 | 매 프레임 GetComponent | `Assets/Scripts/Tutorial/TutorialRuntimeBootstrap.cs:324` | `LateUpdate` 에서 `_playerObject.GetComponent<PlayerMoveController>()` | 필드 캐시 | 보스 (Tutorial) |
| P3 | 매 프레임 문자열 할당 | `Assets/Scripts/Boss/Overhaul/ExecutionPromptHUD.cs:137` | 프롬프트가 떠 있는 동안 매 프레임 보간 문자열 + 결합 (`text` 비교 전에 생성) | (kind, glyph) 가 바뀔 때만 다시 만들기 | 보스 (신규 파일) |
| P3 | 매 프레임 문자열 할당 | `Assets/Scripts/Boss/Overhaul/BossOverhaulHUD.cs:146` | 균형 배율이 1 초과인 동안 매 프레임 `"균형 x" + ToString("0.#")` 생성. 색도 매 프레임 `new Color` 대입 | 배율 값(0.1 단위)이 바뀔 때만 갱신 | 보스 (신규 파일) |
| P3 | 단발 Find | `Assets/Scripts/Ultimate/UltimatePhaseSeveringBeats.cs:205` | 시간 정지 비트 진입 때 `FindObjectOfType<UltimateScreenCrackOverlay>()` 를 한 번 부른다. 궁극기 중 히치 예산(최악 34 ms)에 포함되는 비용 | 시작 시 캐시하거나 오버레이가 정적 Instance 를 노출 | 궁극기 |
| P3 | 씬 미포함 대체 경로 | `Assets/Scripts/UI/Tutorial/Lobby/LobbyManager.cs:82`, `:1659`, `:1663` | `nextSceneName` 기본값 `"MainScene"` 은 빌드 설정에 없는 씬이다. Level_Facility 에서는 `continueInSameScene: 1` 이라 이 경로를 타지 않는다. 플래그가 꺼지면 씬 로드 에러가 난다 | `Application.CanStreamedLevelBeLoaded` 로 가드하고 실패 시 Credits/Title 로 대체 | 보스 (LobbyManager) |
| P3 | 씬 미포함 기본값 | `Assets/Scripts/Interaction/SceneLoadInteractable.cs:10` | 기본값 `"Lobby"` 도 빌드에 없는 씬이다 (Tutorial 씬 인스턴스는 `Level_Facility` 로 덮어써서 정상) | 위와 같은 가드 | 공용 |
| P4 | 스로틀된 Find (정상) | `Boss/Overhaul/ExecutionPromptHUD.cs:127`, `Tutorial/Infiltration/InfSimBoundary.cs:48`, `UI/Dialogue/CombatBarkDirector.cs:132`, `UI/Option/TimelineAudioRouter.cs:78`, `Level/LobbyMobEncounter.cs:111` | 모두 1 s 간격 또는 단발로 스로틀되어 있다. `CombatBarkDirector:132` 는 보스가 없는 씬(튜토리얼)에서 재바인드 주기마다 `FindObjectOfType<BossController>()` 를 계속 부른다 | 지금은 문제없음. 튜토리얼에서는 보스 탐색을 끄는 정도 | – |
| P4 | Camera.main | `Boss/BossPatternHandTelegraph.cs:83`, `Boss/Overhaul/ExecutionPromptHUD.cs:146`, `UI/LockOnTargetIndicator.cs:82`, `VFX/ActionColour/ActionColourDirector.cs:534` | 2022.3 에서는 `Camera.main` 이 내부 캐시라 비용이 작다 | 그대로 둠 | – |
| P4 | 로그 | 런타임 `Debug.Log` 121개 (가드 없음) | 31개를 빼면 모두 직렬화된 bool(`debugLog`, `debugLogs`, `enableLogs`, `logDebug`, `log`) 뒤에 있다. 빌드 씬·프리팹에서 켜진 플래그는 Ultimate 4종 + `PauseCanvas.prefab` 2개뿐이고 모두 경고만 낸다. 가드 없는 31개는 에디터 전용 파일, 키오스크 이벤트 로그(의도됨), `OptionsManagerAdvanced.cs:690`·`:704`(저장·로드 1회), `PlayerHealth.cs:120`·`:128`(초기화 1회), `QuestTrigger.cs:15`, `TitleManager.cs:62` 로 매 프레임 호출은 없다. `GameLog` 는 `[Conditional("CHUON_DEBUG_LOGS"), Conditional("UNITY_EDITOR")]` 이고 정의 심볼은 `DOTWEEN` 뿐이라 릴리스에서 빠진다 | 릴리스 빌드 스크립트가 Log·Warning 스택 트레이스를 끈다. 나머지는 그대로 둠 | – |
| P4 | Shader.Find | 런타임 70여 곳 (`Sprites/Default` 30, `Unlit/Color` 18, URP Unlit/Lit, `Hidden/ChuOn/*`, `ChuOn/Telegraph*`) | 개발·릴리스 빌드의 셰이더 스트리핑은 같다. 개발 빌드 스모크의 에러 셰이더 0 (15) 결과가 그대로 적용된다. `Legacy Shaders/Particles/Additive`, `Standard`, `Particles/Standard Unlit` 은 URP 에서 마젠타가 될 수 있다 | 릴리스 빌드에서 `QaErrorShaderWatch` 같은 확인이 불가능하니(개발 전용) 수동 확인 | 혼합 |

### 2.2 개발 도구 가드 확인 (정상)
- `Assets/Scripts/Debug/*` 는 `GameLog.cs` 를 빼고 전부 파일 최상단이 `#if UNITY_EDITOR || DEVELOPMENT_BUILD` (또는 `DEVELOPMENT_BUILD && !UNITY_EDITOR`) 로 감싸져 있다. QA 봇, PV 캡처, F10 테스트 모드(`DevTestMode`), 카메라·프레임 샘플러가 해당된다.
- F12 전투 디버그 HUD (`UI/CombatDebugHudRuntime.cs:23`)와 F11 성능 진단 (`UI/Option/PerformanceDiagnosticsRuntime.cs:24`)은 부트스트랩이 `Debug.isDebugBuild` 로 막혀 있다. `PlayerHUD.cs:230` F11 도 `Debug.isDebugBuild` 로 막혀 있다. 코드는 빌드에 들어가지만 실행되지 않는다.
- 튜토리얼 F5–F9 (`Tutorial/TutorialDebugController.cs:18`)는 릴리스에서 `enabled = false` 가 된다.
- 키오스크의 개발 전용 분기는 `Exhibition/KioskSafety.cs:515` 에 있다.
- 이벤트 해제 점검: `sceneLoaded +=` 와 `-=` 짝은 맞다. 정적 이벤트(`GameTimeScale`, `InputGlyphs`)는 1회만 등록하는 정적 훅이라 누수가 아니다. 싱글턴 역참조는 거의 모두 null 가드가 있다 (`LobbyTrigger.cs:58` 도 50행에서 가드).

## 3. 다음 할 일 (우선순위)
1. 에디터에서 `Tools/Chuon/Build/Release (PlayX4)` 를 실행한다 (보스 오버홀이 합쳐진 뒤). 그다음 릴리스 빌드에서 사람이 TeamLogo → Credits 를 패드와 키보드로 각각 한 번씩 플레이하고 `Player.log` 에서 에러 0 을 확인한다 (게이트 1·2).
2. 같은 릴리스 빌드에서 `-kiosk` 로 `Exhibition_Kiosk.md` §5 체크리스트를 돌린다 (`-kioskSessionCap 120 -kioskIdle 15` 로 빠르게).
3. 소크 모드를 추가하고 30분 돌린다 (게이트 10).
4. 난이도 0.4 하향, 반응 지연 봇 측정 (게이트 4). 오버홀 패턴의 텔레그래프 정직성 측정 (게이트 5).
5. 임시 궁극기 VFX 참조 정리, 도시 블록·홀로그램 아트 (게이트 8).
6. 목표 사양 PC 에서 릴리스 빌드 FPS·히치 측정 (게이트 3).
7. 대사·UI 최종 교정, QR 이미지 준비.
8. 정적 점검 P2–P3 항목 수정 (보스 영역은 오버홀 머지 뒤).
