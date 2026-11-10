# 시네마틱 샷 리스트 (반복 14, 시네마틱 패스)

기준 문서: `Docs/Story_Reference.md`(설정·정사 대사), `Docs/Roadmap_Loop.md`(반복 14 장면 9개·기준), `Docs/Dialogue_Script_v2.md`(말투), `Docs/CameraDesign_Soulslike.md` §6(연출 → 게임플레이 0.35 s 블렌드), `Docs/LevelDesign_TsukasaFacility.md`, `Docs/LevelDesign_Infiltration.md`.
아트: 그래파이트·화이트 세라믹 + 츠카사 레드 라인. 네온 금지(시안은 VR 시스템 오버레이에만). 츠카사(과학자)는 추온을 "07호"로, EGO 는 항상 "추온"으로 부른다.

## 0. 공통 규칙

| 항목 | 규칙 |
|---|---|
| 재생 | `CinematicDirector.Play(id, onDone)` / `yield return CinematicDirector.PlayRoutine(id)`. 리그가 씬에 없으면 즉시 onDone (기존 흐름 그대로) |
| 시간 | PlayableDirector `UnscaledGameTime`. `Time.timeScale` 은 건드리지 않는다. 일시정지 메뉴(GameTimeScale 0) 동안 타임라인도 정지 |
| 조작 | 재생 중 플레이어 입력 차단(IInputBlocker), 무적(IInvulnerabilityToggle), 락온 카메라 양도, 보스 AI 정지(이미 다른 연출이 멈춰 뒀으면 건드리지 않음), HUD(`Player_HUD` 등) 숨김 |
| 건너뛰기 | 모든 연출. 상호작용 키(키보드 F / 패드 A, 리바인드 반영) 또는 Space·Enter **길게 0.7 s** (패드 Start 는 일시정지 메뉴 전용 — 14C). 연출을 발동한 키를 계속 누르고 있으면 한 번 뗀 뒤부터 센다. 첫 입력에 오른쪽 아래 프롬프트 `[F]  길게 눌러 건너뛰기`(InputGlyphs 표기) + 레드 진행 바 |
| 재도전 생략 | 같은 회차에서 본 연출은 재생 없이 끝 상태로(`chuon.cine.skipSeen`, 기본 켬). 회차 = 타이틀/팀 로고 씬 로드 시 초기화. QA `-qaSkipCinematics` 는 전부 생략 |
| 레터박스 | 2.35:1, 0.35 s 램프(`chuon.cine.letterbox`, 기본 켬). 끄면 그리지 않음 |
| 자막 | 기존 `IntroDialoguePresenter`(자막 크기·배경 옵션, 화자 색, 음악 더킹) 를 연출 오버레이 캔버스(정렬 870)에서 사용. 대사 옵션(`chuon.dialogue.enabled`) 끄면 대사 줄 미표시, 장소 카드는 표시 |
| 복귀 | 타임라인 정지 → 연출 카메라 비활성 → `SoulsCameraRig` 가 0.35 s EaseInOut 블렌드로 플레이어 등 뒤(락온 중이었으면 락온) 복귀. 끝에 남은 페이드/레터박스도 같은 0.35 s 로 걷힘 |
| 전투 | 새 연출은 전투 중 시작하지 않는다(`CinematicDirector.IsCombatActive()` 가드, 트리거는 최대 6 s 대기 후 포기). 전투 중 카메라 점유는 기존 궁극기/처형만 |
| 카메라 문법 | 컷마다 설정(Establish) → 반응(Reaction) → 인서트(Insert) 순서를 기본으로, 마지막 샷은 게임플레이 구도(등 뒤, FOV 48~50)로 끝내 복귀 블렌드가 짧게 보이게 |
| 성능 | 무대(세트·조명·퍼펫)는 재생 중에만 활성. 프레임당 할당 없음(값이 바뀔 때만 UI/MPB 갱신). 실시간 조명 무대당 ≤4, 그림자 ≤2 |

렌즈 표기: 수직 FOV(°) ≈ 풀프레임 환산 mm — 18°≈76, 20°≈68, 22°≈62, 24°≈56, 26°≈52, 28°≈48, 30°≈45, 34°≈39, 36°≈37, 38°≈35, 40°≈33, 42°≈31, 45°≈29, 48°≈27, 50°≈26, 55°≈23.

### 시청 시간 예산 (1회차, 전부 시청 시)

| # | 장면 | id | 길이 | 비고 |
|---|---|---|---|---|
| 1 | 오프닝: 은신처 회상 → 각성 → 본사 올려다보기 | `opening` | 30.0 s | 튜토리얼 → Level_Facility 진입 시 1회 |
| 2 | 튜토리얼 VR 부팅 / 종료 | `tut_vr_boot` / `tut_vr_exit` | 6.5 + 5.0 s | |
| 3 | 로비 경보 + 드론 리빌 | `lobby_alarm` | 9.5 s | 기존 경보 대사 4줄 대체 |
| 4 | 스카이덱 내려다보기 | `skydeck_lookdown` | 7.6 s | 기존 관측 비트(약 3.3 s) 대체 |
| 5 | 엘리베이터 하강 | `elevator_descent` | 13.0 s | 기존 검정 페이드 구간 대체 |
| 6 | Type-D 리빌 v2 | (기존 디렉터) | 17.6 s | **추가 시간 0** (기존과 같은 길이) |
| 7 | 페이즈 전환 과열 | (비점유) | 0 s | 1.6 s 월드 연출, 조작 유지 |
| 8 | 처형·궁극기 | (기존) | 0 s | 정리만 |
| 9 | 엔딩: Type-D 쓰러짐 → 츠카사 홀로그램 | `ending_tsukasa` | 24.0 s | 기존 오버레이(약 10 s) 대체 |
| | **새로 추가되는 시청 시간** | | **≈ 82 s** (교체분 제외) / 전체 연출 합 ≈ 113 s | ≤ 150 s 목표 충족 |

## 1. 런타임 구성 (Assets/Scripts/Cinematics)

| 파일 | 역할 |
|---|---|
| `CinematicDirector.cs` | 재생기(정적 API + 씬 러너). 잠금·건너뛰기·재도전 생략·일시정지·복귀 페이드. `IsPlaying`, `Started/Finished` 이벤트, `IsCombatActive()` |
| `CinematicSequence.cs` | 리그 루트(PlayableDirector 필수). id·건너뛰기·시청 범위·잠금 옵션, 씬 바인딩 표(트랙 이름 → 브레인/플레이어/보스/앵커), `onStarted/onSkipped/onFinished` |
| `CinematicTrigger.cs` | 볼륨/씬 시작/수동 발동, 전투 가드, 다른 연출 대기, 발동 시 옛 연출 비활성 |
| `CinematicAnchor.cs` | 이름 붙은 기준점 (아래 앵커 표) |
| `CineAttach.cs` | 리그 하위 루트를 앵커·플레이어·보스·머리 뼈에 맞춤(시작 시 1회 / follow), 대상 쪽으로 요 회전 |
| `CineAttachOnDirectorPlay.cs` | 다른 컨트롤러가 재생하는 디렉터(Type-D v2)에서 `played` 시점에 CineAttach 실행 |
| `CinematicOverlay.cs` | 레터박스·페이드·카드·건너뛰기 프롬프트·자막 호스트 (정렬 870) |
| `CinematicSettings.cs` | 옵션(레터박스·재도전 생략)·시청 기록·QA 생략 |
| `CineDialogue.cs` | 대사 조회: 메인 테이블 → `Resources/Cinematics/DialogueTable_Cine_KO` → 클립 폴백 |
| `Timeline/CineSubtitle*` | 자막 줄 / 타이틀 카드 트랙 |
| `Timeline/CineFade*` | 페이드(검정·흰 섬광, 섬광은 접근성 번쩍임 배율 적용) |
| `Timeline/CineLetterbox*` | 레터박스 덮어쓰기 (다른 컨트롤러 타임라인에서는 직접 그림) |
| `Timeline/CineLight*` | 조명 배율·결정적 점멸 (끝나면 원래 값 복원) |
| `Timeline/CineText*` | 월드 TMP 문구(층 표시, 모니터) + 깜빡임 |
| `CineHologramFigure.cs` + `Art/ChuonPolish/Cinematics/Shaders/CineHologram.shader` | 츠카사 홀로그램(발끝부터 맺힘, 레드 림·스캔라인·글리치) |
| `CineRenderSettingsOverride.cs` | 무대 전용 하늘·안개·앰비언트 (오프닝 새벽 외경) |
| `CinePhaseOverheatBeat.cs` | 장면 7 (비점유 과열 연출) |
| `Editor/CinematicsAssetBuilder.cs` | 메뉴 `Tools/Chuon/Cinematics/Rebuild Cinematic Assets` → 타임라인·리그 프리팹·재질·대사 테이블 |
| `Assets/Editor/CinematicsInstaller.cs` | 메뉴 `Tools/Chuon/Cinematics/Install Into Open Scene` 등 (아래 §11) |

에셋: `Assets/Cinematics/Chuon/Timelines/TL_Cine_*.playable`, `Assets/Cinematics/Chuon/Prefabs/CINE_*.prefab`, `Assets/Cinematics/Chuon/Resources/Cinematics/DialogueTable_Cine_KO.asset`, `Assets/Art/ChuonPolish/Cinematics/{Meshes,Materials,Audio,Shaders}`.
생성 스크립트: `Tools/ArtGen/cine_props_blender.py`(Blender 5.2, 소품·실루엣 14종 15.8k tris), `Tools/ArtGen/cine_audio.py`(numpy, 효과음·짧은 음악 10종).

### 앵커 표

| 앵커 | 씬 | 위치 / 방향 (forward) | 설치기 기본값 |
|---|---|---|---|
| `CineAnchor_OpeningStage` | Level_Facility | 레벨 밖 무대 원점 | (0, −400, 0), 요 0 |
| `CineAnchor_ElevatorStage` | Level_Facility | 레벨 밖 무대 원점 | (80, −400, 0), 요 0 |
| `CineAnchor_LobbyFrontDesk` | Level_Facility | 프론트 데스크 중앙 바닥. forward = 데스크 → 로비 입구(방문객 쪽). 드론 해치는 데스크 뒤 3 m(−Z) | 상층(Y>20) `FrontDesk_Reception`(없으면 이름에 FrontDesk/Desk, Lift·Holo 제외) 위치를 아래 6 m 레이캐스트로 바닥에 붙임. 없으면 LobbyManager 위치. **14C 확인: (0, 40, 0.1), 요 0** — `FrontDesk_Reception` 피벗이 원점이라 이름 검색이 실패했었음 → 설치기가 하위 렌더러 바닥 중심을 쓰도록 수정 |
| `CineAnchor_SkydeckGlass` | Level_Facility | 유리 바닥 중앙. forward = 보스 쪽 | `LevelFacilityObservationBeat` 위치 |
| `CineAnchor_TutStart` | Tutorial | 플레이어 시작점. forward = 북쪽(본사 타워) | `InfiltrationTutorialDirector.playerStart` |
| `CineAnchor_TutExitDoor` | Tutorial | 로비 유리문 패널. forward = 출구 구역(zoneExit) → 문 (14C) | `exitInteractable` 위치 |
| (보스/플레이어) | 공통 | `CineAttach` 가 재생 순간 위치·요를 잡음 (Player/Boss/PlayerHead/BossHead) | — |

## 2. 장면별 샷 리스트

표기: `t` = 타임라인 초, 위치는 해당 하위 루트 로컬(+Z = 앵커/대상 forward, 플레이어 루트는 보스·데스크 쪽을 보도록 회전).

### 장면 1. 오프닝 — 은신처 회상 → 추온 각성 → 본사 올려다보기 (`opening`, 30.0 s)
- 호스트: **Level_Facility** (레벨 밖 무대 프리팹 `CINE_Opening`, 앵커 `CineAnchor_OpeningStage`). 은신처 세트(Blender: 콘크리트 방·파손 출입구·뒤집힌 작전 탁자·모니터·간이침대·모래주머니·잔해·레지스탕스 깃발), 츠카사 진압조(안드로이드 2, 레드 조준선 3), 쓰러진 레지스탕스 3(GhostSamurai Die01/04/07 마지막 자세), 추온 퍼펫(PlayerVisual_Chuon + GhostSamurai Falldown/Idle/Walk). 외경은 z+600: 인필트레이션 키트 HQ 타워(엠블럼)·포디엄·도시 블록·가로등, 스카이박스 `M_INF_Sky`·새벽 안개(무대 전용 RenderSettings), 새벽 키 라이트.
- 트리거: 튜토리얼에서 넘어온 경우만 (`LobbyManager.SequenceArrival` 의 `_fromTutorialTransition` 분기 맨 앞). 건너뛰기 가능, 재도전 생략(같은 회차).
- 음악/소리: 0.2·3.6 s 벽 너머 경보+총성, 4.0 s 회상 패드(단조, 느린 심박), 18.9 s 저역 임팩트(검정 컷), 20.0 s 새벽 스웰.

| # | t | 문법 | 카메라 / 렌즈 | 블로킹·화면 | 대사 (KO) |
|---|---|---|---|---|---|
| C | 0.0–2.8 | 카드 | 검정 | "그날 밤" / "레지스탕스 은신처" | — |
| S1 | 2.8–6.6 | 설정 | 방 앞쪽 낮은 돌리 인 (−4.6,1.15,3.4)→(−3.9,1.05,2.5), 38→36° | 비상등 레드 맥동, 파손 출입구에 진압조 실루엣·레드 조준선, 쓰러진 동료 | System "거점 제압 완료. 생존자 없음. / 회수 대상… 확인 불가." |
| S2 | 6.6–9.0 | 인서트 | 모니터 클로즈 22→20° (약 62 mm), 핸드헬드 | 화면 문구 SECURE CHANNEL BREACHED → HIDEOUT LOCATION LEAKED(깜빡임) = 정보 유출 | — |
| S3 | 9.0–12.4 | 반응 | 엎드린 추온 하이앵글 푸시 30→28° | 레드 맥동 | EGO "추온. …추온, 눈 떠." |
| S4 | 12.4–16.2 | 액션 | 낮은 측면, 일어서는 동작 따라 틸트 업 34→32° | Falldown_F_End → Idle | 추온 "…다들." |
| S5 | 16.2–19.6 | 반응/POV | 쓰러진 동료 → 레지스탕스 깃발로 느린 팬 32° | 18.6 s 부터 검정 | EGO "정보가 샜어. / 은신처도, 내일 경로도 전부." |
| C | 19.8–22.6 | 카드 | (S6 위) | "새벽" / "츠카사 본사 앞" | — |
| S6 | 19.6–25.0 | 설정(올려다보기) | 추온 등 뒤 로우앵글 → 타워 크라운까지 틸트 업 40→34° | 추온 Idle, 타워 엠블럼 레드 | EGO "경로는 이미 털렸어. / 계획대로면 함정 한가운데야." |
| S7 | 25.0–30.0 | 설정(와이드) | 뒤쪽 높은 와이드 42°, 핸드헬드 미세 | 추온 본사로 걸어감(Walk Root), 29.2 s 부터 검정 | 추온 "상관없어. 정면으로 간다." / EGO "그 말 기다렸어. 가자, 추온." |

- 끝: 검정 → 복귀 페이드 0.35 s 로 로비 게임플레이. **통합 시 `lobby.fromtut.*` 2줄은 오프닝이 재생된 경우 생략**(내용 중복: "시뮬레이션은 끝났어").

### 장면 2. 튜토리얼 VR 부팅 / 종료 (`tut_vr_boot` 6.5 s, `tut_vr_exit` 5.0 s)
- 호스트: **Tutorial**. 리그 `CINE_TutVrBoot`, `CINE_TutVrExit` (자체 정렬, 위치 무관).
- 부팅 트리거: `InfiltrationTutorialDirector.Start()` 에서 체크포인트 재개가 아닐 때(`resume <= 0`) — 기존 `VrTransitionOverlay`(밴드 와이프) 와 동시. `tut.open.*` 대사는 기존 흐름이 그대로 낸다(연출엔 대사 없음).
- 종료 트리거: 출구 패널 상호작용 직후, 씬 로드 전(`HandleExit`). 재도전 생략 없음(매번 짧게).

| # | t | 문법 | 카메라 / 렌즈 | 화면 |
|---|---|---|---|---|
| B1 | 0–2.6 | 설정 | 시작점 앵커: 높은 곳에서 본사 타워를 보다가 골목으로 크레인 다운 40→44° | VR 부팅음, 0.6 s 검정 걷힘 |
| B2 | 2.6–4.4 | 인서트 | 추온 머리 클로즈 26° (머리 뼈 추적) | 흰 섬광 0.35(동기화) |
| B3 | 4.4–6.5 | 복귀 | 등 뒤 (0.25,1.75,−3.4), 50° = 게임플레이 구도 | → 0.35 s 블렌드 |
| X1 | 0–1.8 | 설정 | 문 앵커 뒤 높은 와이드 42° | 로비 유리문 |
| X2 | 1.8–3.4 | 인서트 | 문 패널 클로즈 24° | 흰 섬광 |
| X3 | 3.4–5.0 | 반응 | 추온 3/4 정면 클로즈 30° | 카드 "SIMULATION TERMINATED" / "VR LINK RELEASED // 현실 복귀", 4.2 s 부터 흰색 페이드 → 씬 로드(기존 VR 오버레이가 이어 받음) |

### 장면 3. 본사 로비 진입 — 경보와 드론 출격 리빌 (`lobby_alarm`, 9.5 s)
- 호스트: **Level_Facility** 상층. 리그 `CINE_LobbyAlarm`, 앵커 `CineAnchor_LobbyFrontDesk`. 소품: 데스크 뒤 바닥 드론 해치(Blender, 양쪽 날개가 열림) + 퍼펫 드론 3(인필트레이션 키트 Drone)이 솟아 천장 쪽으로 흩어짐(게임플레이 드론은 나중 웨이브에서 같은 '데스크 뒤'에서 등장 — 예고). 레드 회전 경보 스포트.
- 트리거: `LobbyManager.SequenceArrival` 의 경보 대사 자리(도착 대사 다음). 기존 `lobby.alarm.*`/`lobby.drones.*` 4줄을 연출이 같은 키로 표시하므로 연출이 재생되면 기존 4줄은 건너뛴다. `alertOverlay` 점멸은 유지.

| # | t | 문법 | 카메라 / 렌즈 | 블로킹 | 대사 |
|---|---|---|---|---|---|
| S1 | 0–2.4 | 설정 | 데스크 기준 높은 코너 와이드 (−8.5,6.2,9.5) 40° | 경보등 레드 점멸 | System `lobby.alarm.0` 침입자 감지. 방어 프로토콜을 가동합니다. |
| S2 | 2.4–4.2 | 반응 | 추온 얼굴 클로즈 30°(머리 추적, 데스크를 바라봄) | | System `lobby.alarm.1` 임직원 여러분은 업무를 계속해 주십시오. |
| S3 | 4.2–7.2 | 인서트/리빌 | 데스크 뒤 낮은 앵글, 해치 → 드론 상승 따라 틸트 업 36→40° | 해치 개방 0.8 s, 드론 3기 시차 상승 후 이탈, 7.2 s 닫힘 | EGO `lobby.drones.0` 프론트 데스크 뒤에서 드론이 나와. 손님 접대 한번 거칠다. |
| S4 | 7.2–9.5 | 복귀 | 추온 등 뒤 OTS 48° (데스크 방향) | | 추온 `lobby.drones.1` 전부 부순다. |

### 장면 4. 관측 브리지(스카이덱) — 격리된 Type-D 내려다보기 (`skydeck_lookdown`, 7.6 s)
- 호스트: **Level_Facility**. 리그 `CINE_SkydeckLookDown`, 앵커 `CineAnchor_SkydeckGlass` + 보스. 기존 `LevelFacilityObservationBeat`(유리 위 → 사선 크레인)를 대체: 설치기가 같은 크기 트리거 `CineTrigger_Skydeck` 을 만들고 옛 비트의 콜라이더를 끈다(옛 퀘스트 카드 "아트리움 스카이덱"은 표시되지 않음 — 자막이 대신).

| # | t | 문법 | 카메라 / 렌즈 | 화면 | 대사 |
|---|---|---|---|---|---|
| S1 | 0–2.2 | 설정 | 추온 앞 측면 로우앵글 42° (유리 바닥·깊이) | 유리 위에 올라선 추온 | — |
| S2 | 2.2–3.6 | 반응 | 머리 위 어깨너머로 유리 아래를 내려다봄 40° | | — |
| S3a | 3.6–5.0 | 인서트 | 유리 앵커 위 0.9 m, Type-D 가슴 추적 26° | 붉은 격리장, 저역 임팩트 | EGO `cine.sky.0` 유리 아래가 격납고야. / 붉은 격리장 안에… 실험체 하나. |
| S3b | 4.6–7.6 | 크레인 | 보스 기준 (4.5,14,5)→(3.6,11,4), 30→24° (4.6–5.0 블렌드 = 유리 위 → 사선 크레인) | | 추온 `cine.sky.1` …나랑 같은 데서 나왔어. |

### 장면 5. 엘리베이터 하강 — 압축 (`elevator_descent`, 13.0 s)
- 호스트: **Level_Facility** (레벨 밖 무대 프리팹 `CINE_ElevatorDescent`, 앵커 `CineAnchor_ElevatorStage`). Blender 캐빈 내부(그래파이트 패널·세라믹 트림·레드 헤어라인·조명 스트립·핸드레일·문짝) + 층 표시기(TMP B1→B4 HANGAR) + 추온 퍼펫.
- 트리거: `LobbyManager.CoContinueInSameScene` 에서 화면이 검정이 된 직후, 핸드오프(`ElevatorDescentFinished`) 전. 시작·끝이 검정이라 앞뒤 페이드와 이어진다. 끝나면 도착 연출(문 열림 "도착. 격납고 층이야, 추온.")이 이어진다.
- 소리: 하강 험 + 층 통과 클랭크(2.4 s 간격) + 6 s 조명 끊김 버즈, 12.4 s 저역 임팩트. 기존 `lobby.elev.*`(탑승 전) 와 겹치지 않는 새 대사 3줄.

| # | t | 문법 | 카메라 / 렌즈 | 화면 | 대사 |
|---|---|---|---|---|---|
| S1 | 0–3.4 | 설정 | 캐빈 코너 높은 와이드 55° (압축된 공간) | 0.7 s 검정 걷힘, 표시기 B1→B2 | EGO `cine.elev.0` 버튼 하나짜리 엘리베이터라. / 편도 티켓이네. |
| S2 | 3.4–6.0 | 인서트 | 층 표시기 클로즈 24° | B2→B3 | — |
| S3 | 6.0–9.0 | 반응 | 추온 MCU(문 쪽에서) 30° | 6.0 s 조명 끊김(점멸) → 레드 비상등, 이후 55 % 조도 | 추온 `cine.elev.1` …돌아갈 생각 없어. |
| S4 | 9.0–11.6 | 투샷/OTS | 추온 등 뒤 문 방향, 42→38° 푸시 인 | B4 | EGO `cine.elev.2` 알아. 그러니까 끝까지 / 같이 가는 거야, 추온. |
| S5 | 11.6–13.0 | 인서트 | 표시기 "B4 HANGAR" 깜빡임 20° | 12.3 s 부터 검정 | — |

### 장면 6. Type-D 리빌 v2 (기존 `TL_MainScene_Intro` 품질 업, 17.6 s 그대로)
- 호스트: **Level_Facility**(및 MainScene) 의 `MainSceneIntroDirector`(MainSceneArrivalController 가 재생). `TL_Cine_TypeDReveal_v2` 는 원본 신호 14개를 **같은 시각·같은 SignalAsset·같은 플래그**로 복사(IntroStep 0~9, 전투 시작 순서 불변). 원본 카메라 2개는 같은 exposed 이름으로 재사용하고 새 샷 5개(`CINE_TypeDReveal_v2_Shots` 프리팹, 재생 순간 플레이어/보스에 정렬)를 끼움. 레터박스 트랙(0.35 s 램프) + 4.35 s 저역 임팩트.
- 건너뛰기·재도전 생략은 기존 컨트롤러 규칙(`RequestSkipArrivalOnNextLoad`) 그대로.

| # | t | 문법 | 카메라 / 렌즈 | 신호·대사(기존) |
|---|---|---|---|---|
| 1 | 0.00–2.25 | 설정(POV) | 캐빈 안 추온 어깨너머, 문이 열리는 격납고 45° | DoorOpen 0.65, Line00 "도착. 격납고 층이야, 추온." Line01 |
| 2 | 2.25–4.35 | 액션 | 기존 VCam_MainIntro_ElevatorExit (측면 와이드) | PlayerWalkOut 2.25, Line02 |
| 3 | 4.35–5.55 | 설정 | 격납고 와이드, 보스 뒤 높은 곳 38° | BossReveal 4.35, Line03 "정정할게…" + 저역 임팩트 |
| 4 | 5.55–6.75 | 인서트 | Type-D 바이저 클로즈 18°(≈76 mm, 머리 뼈 추적) | (리빌 걸음 중) |
| 5 | 6.75–끝 | 복귀 | 등 뒤 50° = 게임플레이 구도 | 실제로는 약 7.0 s 에 리빌 걸음이 끝나면 컨트롤러가 타임라인을 멈추고 전투 시작(`endTimelineWhenBossRevealWalkEnds`), Line04~09 는 조작 중 자막으로 이어짐 → 0.35 s 블렌드 |

- 14C Play 검증: 원본 설계(17.6 s 전부 연출)와 달리 기존 컨트롤러가 약 7.0 s 에 끝내므로, 6.80 s 이후 샷(바이저·반응·투샷)은 재생되지 않고 바이저가 0.2 s 번쩍이던 문제가 있었다. 와이드·바이저를 앞으로 당기고 반응·투샷 클립은 삭제(`ChuonReaction`/`BossReveal` 카메라 오브젝트는 남아 있음). 17.6 s 전체를 보이려면 리드가 `endTimelineWhenBossRevealWalkEnds` 를 끄고 클립을 되살릴 것(전투 시작 +10 s).

### 장면 7. 페이즈 전환 — Type-D 과열·균열 (비점유, 1.6 s × 페이즈 2·3)
- 소울라이크 원칙상 전투 중 카메라·조작을 빼앗지 않는다. 대신 `CinePhaseOverheatBeat`(보스에 부착)가 `BossController.OnBossPhaseChanged` 에서 **월드 안 짧은 컷**처럼 읽히는 1.6 s 연출을 겹친다: 등 배기구 증기 분출(2연발) + 붉은 불티 + 가슴 앞 열광 점광원(0.12 s 치솟고 식음, 번쩍임 옵션 배율) + 카메라 리그 흔들림(흔들림 옵션 배율) + 증기 히스. 기존 `TypeDArmorRig` 코어 펄스·균열 텍스처와 `phase2.typed`/`phase3.typed` 시스템 음성 bark 는 그대로.
- 보스 공격은 원래 `phaseTransitionAttackLockDuration` 동안 멈춰 있으므로 정직성(경고 시간)에 영향 없음. 페이즈 3 은 1.35 배 크기.
- (선택, 리드 결정) 컷을 원하면 1.2 s 인서트를 `CinematicDirector` 로 재생할 수 있지만, 이번 반복에서는 넣지 않았다.

### 장면 8. 처형·궁극기(위상단절검) 컷 정비 (기존 유지)
- `TL_Ultimate_PlayerSword`(5.6 s): Intro 0.6(설정) → CloseUp 0.4(인서트) → SlashStorm 1.83 → Walkout 0.7(반응) → Explosion 2.0(설정) 로 문법은 이미 맞다. 기존 건너뛰기(`UltimateCinematicController.RequestSkip`) 유지.
- 정비 항목(리드/소유자 수정 필요, 각 1~2줄): ① `UltimateCinematicHudFrame` 레터박스 높이에 `CinematicSettings.LetterboxEnabled` 를 곱한다(옵션 일관성). ② 궁극기·처형 중 `CinematicDirector.IsCombatActive()` 는 true 이므로 새 연출이 끼어들지 않음(확인만). ③ `FinisherStaticCam` 복귀는 `SoulsCameraRig` 0.35 s 블렌드를 그대로 탄다(우선순위 100 → 10 으로 내리는 기존 방식 유지).

### 장면 9. 엔딩 — Type-D 쓰러짐 → 츠카사 등장(홀로그램 실루엣) → 크레딧 (`ending_tsukasa`, 24.0 s)
- 호스트: **Level_Facility** 하층 아레나. 리그 `CINE_EndingTsukasa` (보스·플레이어 기준 자동 정렬). Blender 홀로 프로젝터(그래파이트 링·레드 라이트 링·세라믹 렌즈)가 Type-D 뒤 3.4 m 바닥에서 솟고, Blender 츠카사 실루엣(긴 코트, 뒷짐, 턱을 든 자세, 1.15 배)이 `Chuon/CineHologram` 셰이더로 발끝부터 맺힌다(흰 세라믹 빛 + 레드 림·스캔라인·글리치, 시안 금지).
- 트리거: `MainSceneClearPresentationController.CoPlayClearSequence` 에서 `TsukasaEndingPresenter.Play` 대신(리그가 없으면 기존 오버레이로 폴백). 끝나면 기존 클리어 오버레이 → 출구 → CreditsScene 흐름 그대로("→ 크레딧").
- 소리: 0.1 s 저역 임팩트, 5.4 s 홀로 글리치(맺힘), 22.0 s 글리치(소멸). 음악은 MusicDirector 승리/엔딩 상태 + 자막 더킹.

| # | t | 문법 | 카메라 / 렌즈 | 화면 | 대사 |
|---|---|---|---|---|---|
| S1 | 0–3.0 | 설정 | 쓰러진 Type-D 낮은 와이드 36→34° 돌리 | | — |
| S2 | 3.0–5.2 | 반응 | 추온 클로즈 30° | | EGO `cine.end.0` 잠깐. 누가 회선에 / 끼어들었어. |
| S3 | 5.2–8.4 | 인서트 | 바닥 프로젝터 상승 → 빛기둥 틸트 업 30→32° | 프로젝터 상승 1.2 s, 빛 점멸 | — |
| S4a | 8.4–12.4 | 설정(로우 투샷) | 추온 어깨너머 로우앵글, 홀로그램 머리 추적 42° | 7.6–9.6 s 맺힘 | 츠카사 `ending.tsukasa` **결국 일어나 버렸나.. 하지만 넌 아직 내가 설계한 궤도 위를 걷고 있을 뿐이다.** (정사 원문) |
| S4b | 12.4–16.4 | 인서트 | 홀로그램 MCU 로우앵글 24° | | (계속) |
| S5 | 16.4–19.0 | 반응 | 추온 클로즈 26° | | 추온 `ending.reply.0` 그 궤도, 내가 끊어 주지. |
| S6 | 19.0–22.0 | 역방향 투샷 | 홀로그램 등 뒤에서 추온 36° | | EGO `ending.reply.1` 설계자 양반, 날 버린 거 후회할걸. / 난 예측 불가능하거든. |
| S7 | 22.0–24.0 | 설정 | S4a 재사용 | 글리치 아웃(맺힘 역재생) → 0.35 s 복귀 | — |

## 3. 새 대사 (리드가 `DialogueTable_KO` 에 병합할 원본)

연출은 메인 테이블에 키가 없으면 `Resources/Cinematics/DialogueTable_Cine_KO.asset` 에서 읽는다(같은 형식). 병합하면 메인 테이블이 우선한다.

| 키 | 화자 | 대사 |
|---|---|---|
| `cine.card.night` | — | 그날 밤 |
| `cine.card.dawn` | — | 새벽 |
| `cine.open.0` | System | 거점 제압 완료. 생존자 없음. / 회수 대상… 확인 불가. |
| `cine.open.1` | EGO | 추온. …추온, 눈 떠. |
| `cine.open.2` | 추온 | …다들. |
| `cine.open.3` | EGO | 정보가 샜어. / 은신처도, 내일 경로도 전부. |
| `cine.open.4` | EGO | 경로는 이미 털렸어. / 계획대로면 함정 한가운데야. |
| `cine.open.5` | 추온 | 상관없어. 정면으로 간다. |
| `cine.open.6` | EGO | 그 말 기다렸어. 가자, 추온. |
| `cine.sky.0` | EGO | 유리 아래가 격납고야. / 붉은 격리장 안에… 실험체 하나. |
| `cine.sky.1` | 추온 | …나랑 같은 데서 나왔어. |
| `cine.elev.0` | EGO | 버튼 하나짜리 엘리베이터라. / 편도 티켓이네. |
| `cine.elev.1` | 추온 | …돌아갈 생각 없어. |
| `cine.elev.2` | EGO | 알아. 그러니까 끝까지 / 같이 가는 거야, 추온. |
| `cine.end.0` | EGO | 잠깐. 누가 회선에 / 끼어들었어. |

재사용(기존 키): `lobby.alarm.0~1`, `lobby.drones.0~1`, `ending.tsukasa`, `ending.reply.0~1`, `boss.intro.0~9`(Type-D v2 신호). 카드 보조 문구(레지스탕스 은신처 / 츠카사 본사 앞 / VR LINK RELEASED)는 클립에 저장.

## 4. 통합 단계 (씬 담당자 / 리드)

1. 에셋 생성: `Tools/Chuon/Cinematics/Rebuild Cinematic Assets` (Blender·오디오를 다시 만든 경우에만 다시 실행).
2. **Level_Facility** 열고 `Tools/Chuon/Cinematics/Install Into Open Scene` → `CINEMATICS_iter14`(Anchors / Rigs / Triggers) 생성, 보스에 `CinePhaseOverheatBeat` 추가, 관측 브리지 트리거 교체. **`CineAnchor_LobbyFrontDesk` 를 실제 프론트 데스크 중앙(forward = 입구 쪽)으로 옮길 것.** 이어서 `Upgrade Type-D Reveal (v2)`. 씬 저장.
3. **Tutorial** 열고 `Install Into Open Scene` → 앵커 2 + 리그 2. 씬 저장.
4. 코드 연결 (소유 파일이 아니라 직접 수정하지 않았음 — 각 1~4줄):
   - `LobbyManager.SequenceArrival`: `if (_fromTutorialTransition) { if (CinematicDirector.Has("opening")) yield return CinematicDirector.PlayRoutine("opening"); else { 기존 lobby.fromtut 2줄 } }`
   - `LobbyManager.SequenceArrival` 경보 블록: `if (CinematicDirector.Has("lobby_alarm")) yield return CinematicDirector.PlayRoutine("lobby_alarm"); else { 기존 lobby.alarm/drones 4줄 }` (alertOverlay 점멸은 그대로 앞에)
   - `LobbyManager.CoContinueInSameScene`: 검정 페이드 직후 `yield return CinematicDirector.PlayRoutine("elevator_descent");` (그다음 기존 핸드오프·페이드 인)
   - `MainSceneClearPresentationController.CoPlayClearSequence`: `if (CinematicDirector.Has("ending_tsukasa")) CinematicDirector.Play("ending_tsukasa", () => endingDone = true); else TsukasaEndingPresenter.Play(...)` (20 s 가드는 30 s 로)
   - `InfiltrationTutorialDirector.Start`: `if (resume <= 0) CinematicDirector.Play("tut_vr_boot");` / `HandleExit`: 씬 로드 전에 `PlayRoutine("tut_vr_exit")` 를 기다리도록(현재 `BeforeSceneLoad` 동기 콜백 → 코루틴 대기 필요)
   - 잡몹 정지: `LobbyMobEncounter` 의 `LevelFacilityObservationBeat.IsPlaying` 검사 옆에 `|| CinematicDirector.IsPlaying`, `EnemyAttackTokenDirector.TryAcquire` 맨 앞 `if (CinematicDirector.IsPlaying) return false;`
   - `CombatBarkDirector`: 재생 금지 조건에 `CinematicDirector.IsPlaying` 추가. `MusicDirector` 더킹 조건(218행)에 `|| CinematicDirector.IsPlaying`
   - 옵션 화면: 접근성/표시 탭에 "레터박스"(`CinematicSettings.SetLetterboxEnabled`), "본 연출 다시 보지 않기"(`SetSkipSeenOnRetry`) 토글
   - 키오스크 리셋/타이틀 복귀 시 `CinematicSettings.ResetRun()` (타이틀 씬 로드 시 자동으로도 초기화됨)
5. QA: `-qaSkipCinematics` 로 봇 흐름이 연출에 막히지 않는지, 기존 `-qaFlowSmoke`/보스 봇 실행.

## 5. Play 모드 검증 목록 (14B 는 Play 모드 금지 → 통합 후 확인)

- 각 연출: 첫 프레임/마지막 프레임 화면, 컷 경계 튐 없음, 마지막 샷 → 게임플레이 0.35 s 블렌드(요 각속도 240°/s 이하), 락온 복원.
- 건너뛰기: 길게 누르기 0.7 s, 프롬프트 표기(KB+M F / Xbox A / PS ✕), 건너뛴 뒤 입력 잠금·무적·보스 정지·HUD 가 모두 풀리는지, 스킵 키가 게임플레이 상호작용으로 새지 않는지.
- 재도전(사망 → 체크포인트) 시 같은 연출 생략, 타이틀 복귀 후 새 회차에서는 다시 재생.
- 일시정지 메뉴 열기/닫기 중 타임라인 정지·재개, 자막 유지.
- 오프닝: 무대 RenderSettings(하늘·안개) 적용·복원, 퍼펫 애니메이션(GhostSamurai → 추온 아바타 리타깃) 자세, 쓰러진 동료 Die 마지막 자세 고정, Walk 루트 모션 방향.
- 로비: 앵커 위치/방향(드론 해치가 데스크 뒤 바닥에 보이는지), 실제 드론 웨이브와 모순 없는지.
- 스카이덱: 유리 위 → 사선 크레인 블렌드에서 바닥 관통 없음.
- 엘리베이터: 핸드오프 후 도착 연출과 이음매(검정 → 문 열림).
- Type-D v2: 신호 순서·대사 시각 동일(QA 봇 `boss.intro` 로그), 바이저 인서트 프레이밍(Type-D 키), 레터박스가 끝나고 0 으로 돌아오는지.
- 엔딩: 홀로그램 셰이더(URP 투명 가산) 표시, 맺힘/소멸, 츠카사 정사 대사 원문, 끝난 뒤 출구 상호작용 가능.
- 성능: 각 연출 중 1% 저점 60 FPS 이상, 오프닝 외경(타워 220 m + 블록 6 + 안개) 첫 프레임 히치(셰이더 예열 대상에 `Chuon/CineHologram`, URP Particles/Unlit 추가 권장).

## 6. 위험

- **퍼펫 리타깃**: GhostSamurai(A-포즈) 클립을 추온 휴머노이드에 적용 — 손목·어깨 각도 확인 필요. 문제면 CombatGirls(Normal/Die, Take 등)로 교체(빌더의 `Clip()` 한 줄).
- **프론트 데스크 앵커 추정**: 이름 검색 실패 시 LobbyManager 위치에 놓인다(설치 로그에 WARN). 반드시 수동 확인.
- **오프닝 무대 위치(Y −400)**: 레벨에 낙사/킬 볼륨이 Y 기준이면 무대 카메라만 가므로 영향 없음이 정상이나, 반사 프로브·라이트 프로브가 없어 실내 앰비언트가 어두울 수 있다(무대 조명으로 보정).
- **Type-D v2 카메라 정렬 시점**: `director.played` 순간의 플레이어 위치 기준 — 도착 연출이 플레이어를 옮긴 **뒤** 디렉터를 재생해야 한다(현재 코드 순서 확인 필요). 어긋나면 `CineAttachOnDirectorPlay` 대신 Sig_MainIntro_Begin 신호 반응으로 정렬.
- **튜토리얼 종료 연출**: `SceneLoadInteractable.BeforeSceneLoad` 가 동기라 대기 구조 변경이 필요(통합 단계 4).
- **HUD 숨김**: 이름(`Player_HUD` 등)으로 찾는다. 씬의 실제 HUD 루트 이름이 다르면 `CinematicSequence.hudObjectNames` 에 추가.
- **자막 중복**: 연출 자막은 별도 캔버스의 IntroDialoguePresenter 를 쓴다. 통합 전에는 전투 bark 가 게임플레이 캔버스에 동시에 뜰 수 있다(통합 단계 4의 bark 가드).

## 7. 반복 15 변경 (PV 리뷰 반영)

- **예열**: `CinematicSequence.prewarmOnLoad`(기본 켬) — 씬 로드 때 무대·비활성 소품을 한 프레임 켜서 Awake/TMP 를 끝내고, 타임라인 그래프를 미리 만들고, 숨은 카메라로 무대 전체 + 각 샷 카메라 자리에서 1회 렌더(무대 안개 FOG_EXP2·조명 변형 포함) 후 되돌린다. `opening`·`tut_vr_boot` 는 로드 프레임에 즉시, 나머지는 0.6 s 뒤 프레임당 1개. 엘리베이터: 상층 정리 프레임과 연출 첫 프레임 분리(`yield return null`).
- **로비 경보**: 전투 가드(IsCombatActive)로 생략하지 않는다. `LobbyManager.AlarmBeatPending` 동안 가르치기 경비가 깨지 않고(60 s 안전 시간), 다른 연출(스카이덱) 재생 중이면 끝날 때까지 기다렸다가 재생. `AtPlayer` 는 follow + 뒤 3.2 m 클리어(바닥 없는 방향 배제). S3 해치 리빌: 시선 0.3 m 에서 시작해 드론을 따라 틸트(`Rig.LookKeys`), 컷 전용 키(화이트)·림(레드) 스포트 2개(활성화 트랙 4.2–7.4 s).
- **오프닝**: S3 머리·어깨 부감, S4 가슴 높이 3/4 정면 + 머리 뼈를 따라가는 시선 키, S6 어깨 높이 어깨너머 → 타워 틸트. 스카이덱 S1 1.0 → 1.4 m.
- **Type-D v2**: `CineAttachOnDirectorPlay` 가 재생 중 follow 루트를 갱신, `AtBoss`/`BossHead` follow — 바이저 인서트가 리빌 걸음 뒤 빈 자리(바닥)를 비추던 문제. 바이저 18 → 24°. 붉은 사이렌 워시 최대 1.1 s(리빌 4.35 s → 5.45 s)로 인서트(5.55 s~)를 가리지 않음. 길이는 7 s(컨트롤러 종료) 유지 = 전시용 ≤10 s.
- **궁극기**: 인트로 컷 = 머리 높이 측면 프로필(`introFrontalCameraOffset`), 베기 컷 = 대상 기준 62° 돌린 3/4 미디엄(반경 3.7, 높이 1.6). 새 필드라 씬 직렬화 값과 무관하게 적용(끄면 이전 구도).
- **튜토리얼 VR 부팅**: 밴드 유지 0.8 → 0.15 s, 연출은 0.15 s 뒤 시작, 전투 VFX/JIT 예열은 부팅 연출 뒤로.
- **HUD 숨김**: 알파를 매 프레임 덮어쓰는 `Boss_HUD` 대비 캔버스 자체를 끈다.
- `CineAttach.Head()`: 휴머노이드 뼈가 없으면 루트(발) 대신 이름 검색 → 렌더러 높이 88 % 대리점.

## 8. 반복 16 변경 (플레이어 리뷰, `Docs/Player_Review_Iter16.md`)

- **튜토리얼 종료**: 앵커가 출구 패널 높이(Y 2.5)라 앵커 기준 샷이 문 위 허공을 비춤 → `CineAttach.groundToFloor`. X1 은 추온 어깨너머 로우 와이드(플레이어 기준, 문 향함, 뒤 3.4 m 클리어, 40°). 카드 = 하단 1/3(`CineSubtitleClip.cardPlacement`). 흰 페이드 → VR 디졸브(`CineFadeClip.style = VrDissolve`, 3.9–5.0 s, 그래파이트), 패널 섬광 0.22 세라믹 화이트(번쩍임 배율). 끝 덮개 유지(`CinematicSequence.holdEndCoverSeconds 3`) → 씬 로드로 바로.
- **엘리베이터 S3**: 비상등 천장 중앙 3.0/5 m → 뒤 왼쪽 천장 모서리 2.0/2.2 m (분홍 워시 제거, 레드는 림·모서리만).
- **자막**: 연출 자막은 하단 중앙 유지. 조작 중 오버레이 줄(bark·리빌 뒤 인트로 대사)은 오른쪽 아래 통신 패널.

## 9. 반복 17 카메라 워크 패스 (연출 전체, 길이·대사 시각 불변)

공통 도구(`CinematicsAssetBuilder.Rig`): 시선 대상 샷은 하드 룩앳(대상 = 화면 정중앙, 머리 위 여백 과다) 대신 **RotationComposer**(3분할·헤드룸: 얼굴 y −0.15, 시선 반대쪽 3분할 x ±0.1~0.17, 댐핑 0.25). `Shot(..., blendIn)` = 같은 공간 안 **휩 팬/틸트**(0.18~0.22 s 블렌드, 블렌드 길이를 클립에 명시). 샷마다 돌리/크레인/줌 크리프(`Move`), 핸드헬드 = Cinemachine 노이즈 `Handheld_normal_mild` 진폭 0.2~0.5(인서트·긴장 샷 높게, 게임플레이 구도 샷 0). Type-D v2 는 씬 디렉터가 재생해 애니메이션 트랙을 못 붙이므로 `CineCamDrift`(라이브 순간부터 unscaled 이징, 일시정지 시 정지). 랙 포커스(URP DoF)는 60 FPS 예산·카메라 포스트 설정 미확인으로 **넣지 않음** — 시선 키(LookKeys)로 주의 이동을 대신한다.

| 장면 | 변경 |
|---|---|
| 1 오프닝 | S1 크레인 다운+푸시 40→36° (2.8–5.4) → **S1b 문간 진압조 실루엣**(5.4–6.6, 24→22°, 바리케이드 너머, DoorSpot 역광·조준선, 핸드헬드 0.5) → S2 모니터 트랙 26→23° → **휩 팬 0.2 s** → S3 부감 푸시 30→27° → S4 일어서는 동작과 함께 크레인 업 0.95→1.4 m → S5 POV 돌리 팬 → S6 어깨너머 1.4 s 유지 후 로우 크레인(1.6→1.05 m)+크라운 틸트, 크라운 1 s 유지 → S7 풀백 크레인 업(−5→−7.6 m, 1.6→3.4 m) |
| 2 VR 부팅 | B1 타워 0.8 s 붙잡고 골목으로 크레인 틸트(시선 키) / B2 얼굴 왼쪽 3분할 + 푸시 26→23° / B3 2.9→3.4 m 풀백으로 게임플레이 구도에 '내려앉음' |
| 2 VR 종료 | X1 푸시 40→37° → **휩 푸시 0.18 s** → X2 패널 푸시 34→30° → X3 얼굴 3분할 + 푸시 30→27° |
| 3 로비 경보 | S1 크레인 다운+푸시 → S2 얼굴 오른쪽 3분할(시선 = 데스크) 푸시 → S3 핸드헬드 0.45 + 크레인 업 → S4 44→48° 풀백(2.5→3.2 m)으로 복귀 구도 |
| 4 스카이덱 | S1 얼굴 왼쪽 3분할 + 아크 돌리 / S2 유리 아래로 틸트 다운 / S3a 핸드헬드 / S3b 크레인 다운(4.6–6.9, 0.4 s 블렌드 명시) / **S4 복귀 6.9–7.6** 추온 등 뒤 50°(뒤 3.4 m 클리어) — 크레인(보스 위 11 m) → 다리로 바닥을 관통하던 복귀 블렌드 제거 |
| 5 엘리베이터 | S1 크레인 다운 + 55→50° / S2 표시기 푸시 / S3 얼굴 왼쪽 3분할 푸시 32→28° / S4 아크 OTS / **휩 틸트 0.22 s** → S5 'B4 HANGAR' 푸시 30→24° |
| 6 Type-D v2 | DoorPOV 0.35 m 푸시 / HangarWide 1.5 m 크레인 다운+푸시 / VisorInsert 머리 왼쪽 3분할(댐핑 0, 걸음 추적) + 0.2 m 푸시 24→21.5° |
| 9 엔딩 | **180° 축 정리**: 대화 샷 전부 추온 오른쪽 편(S4b·S5·S6 이 축을 넘었었음). S1 크레인 다운 / S2·S5 추온 왼쪽 3분할 푸시(30→27°, 26→24°) / **S3 (E4)** 0.6 m 가로 앵글 → 1.2 m 3/4 부감(배경 = 바닥, 프로젝터 왼쪽 3분할), 상승 1.3 s 붙잡고 맺히는 발끝→무릎 틸트 / S4a 홀로 머리 오른쪽 3분할, 내려앉는 푸시 40→36° / S4b 홀로 오른쪽 3분할 푸시 30→26° / **S6 (E5)** (−1.5, 2.3, −5.1): 추온 왼쪽 3분할, 홀로그램 가슴~머리 오른쪽(≈ +0.35, 가장자리와 하단 자막에서 떨어짐), FOV 37→35 / S7 S4a 풀백 |

- **E3 (오프닝 → 로비 흰 천장 프레임)**: 검정으로 끝나는 연출(오프닝·엘리베이터, 레벨 밖 무대)은 `CinematicDirector` 가 카메라 리그 복귀 블렌드(0.35 s, 무대 → 플레이어 사이를 지나가며 천장 조명을 비춤)가 끝날 때까지 검정을 유지(+0.05 s)한 뒤 0.35 s 로 걷는다. 다른 연출이 그 사이 시작하면 즉시 기존 경로.
- **E4 붉은 점**: 원인 미확정(아레나 붉은 조명/발광체로 추정). 새 S3 는 배경이 바닥이라 화면 왼쪽 위에 아레나가 들어오지 않는다 — 플레이테스트에서 확인.
- 건너뛰기 프롬프트: 평문 → `ChuonKeyHint` 글리프 상자 + 24 px 설명(궁극기·엔딩 오버레이와 동일).
