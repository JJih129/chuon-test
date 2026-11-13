# 프로젝트 폴더 구조 (Assets)

2026-11 정리: 서드파티 팩은 `ThirdParty/`로, 흩어진 자체 폴더·루트 파일은 용도별 폴더로 옮겼다. 모든 이동은 `.meta`와 함께 해서 GUID/씬·프리팹 참조는 그대로다.

## 루트 맵

| 폴더 | 내용 |
|---|---|
| `Anime/` | 애니메이터 컨트롤러(`Animator/`), 애니메이션 팩(`Animation/`), 개별 클립(`Clips/`) |
| `Art/ChuonPolish/` | 폴리시 패스 결과물(ArtGen 출력 경로 — 이동 금지) |
| `Art/Models/` | 모델: 플레이어/보스 FBX, `Chon/`(주온 캐릭터), `Drone/`, `Map/`(맵 메시) |
| `Art/Materials/`, `Art/Textures/` | 자체 머티리얼(보스 ASPToon 출력 포함), 텍스처(psd 등) |
| `Art/Effects/`, `Art/Shaders/` | 자체 VFX(트레일 등), 셰이더 |
| `Art/Audio/` | 자체 원본 사운드(패링·타격·발소리) |
| `Art/Fonts/` | 폰트 + TMP SDF |
| `Art/UI/` | `Sprites/`(HUD·타이틀·키 아이콘), `PauseMenu/`, `Icons/` |
| `Cinematics/` | 타임라인/시네마틱 자산, `Signals/`(타임라인 시그널) |
| `Data/AttackData/` | 콤보 AttackData SO |
| `Editor/` | 프로젝트 에디터 스크립트 |
| `Plugins/` | DOTween, Roslyn (이동 금지) |
| `Prefabs/` | `Mobs/ UI/ VFX/ Tutorial/ Generated/ Misc/` |
| `Resources/` | `Resources.Load` 대상 (하위 경로 유지 필수) |
| `Scenes/` | 빌드 씬 |
| `Scripts/` | 런타임 코드(기능별 하위 폴더) |
| `Settings/` | URP 에셋, 카메라 프로필 |
| `TextMesh Pro/` | TMP 기본 리소스 (루트 유지) |
| `ThirdParty/<팩 이름>/` | 외부 에셋 팩 전부 (원본 폴더명 유지) |
| `_Legacy/` | 빌드에서 참조되지 않는 옛 실험물(삭제 전 보관용). 새로 참조하지 말 것 |

## 새 자산 규칙

- 서드파티 팩 임포트 → 임포트 후 바로 `ThirdParty/<팩>/`으로 이동(에디터 안에서). 팩 내부는 수정하지 않는다. 수정본은 `Art/` 아래에 복사해 쓴다.
- 자체 자산은 `Art/<종류>/`, 프리팹은 `Prefabs/<기능>/`, 코드는 `Scripts/<기능>/`, 에디터 툴은 `Editor/` 또는 `Scripts/<기능>/Editor/`.
- `Assets/` 루트에 파일을 직접 두지 않는다. `New Folder`, 숫자 이름(`111`, `231213213.prefab`) 금지.
- 런타임 로드는 `Resources/` 하위 경로로. 코드에 `"Assets/..."` 하드코딩은 에디터 전용 툴에서만, 폴더를 옮기면 해당 문자열도 함께 수정한다.
- 폴더 이동은 에디터 Project 창에서 하거나(에디터 종료 상태라면) `git mv`로 `.meta`까지 함께 옮긴다.

## Git 제외 항목

`_AssetQuarantine/`(정리 백업), `fastmcp/`, `.fastmcp_backups/`, `.idea/`, `.vscode/`, `Thry/`(에디터 캐시), 루트 `malgun.ttf`, `Assets/~UnityDirMonSyncFile~*` 는 `.gitignore` 처리(로컬 파일은 유지).
