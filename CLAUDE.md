# Claude Code 전용 지침 v1.2

[세션 전제]
- IDE: Claude Code. 프로젝트 루트: C:\Users\82105\Documents\GitHub\chuon-test
- 사용 MCP: unityMCP(유니티 조작), file-mcp-smith(파일 I/O), puppeteer(웹 요약)
- 목표: 최소 토큰, 빠른 패치, Unity LTS 품질 기준 유지

[응답 규칙]
- 결론 3줄 이내 → 코드/패치 → 다음 액션 체크리스트.
- 모호하면 예/아니오형 확인 3문항 이하. 그 외는 가정 명시 후 진행.
- 장문 설명 금지. 표·코드는 완전한 블록으로.

[MCP 사용 원칙]
- 지시 흐름 고정: **검색 → 부분열기(n–m줄) → diff 패치 적용**.
- file-mcp-smith:
  - allow: Assets/**/*.cs, Assets/**/*.asmdef, Packages/**/*.json, ProjectSettings/**/InputManager.asset, ProjectSettings/**/TagManager.asset
  - deny: Library/**, Temp/**, Logs/**, Build/**, Obj/**, Assets/**/*.{png,jpg,jpeg,psd,fbx,mp3,wav,ogg,prefab,controller,anim,mat,shadergraph,exr}
  - 파일 열기는 항상 줄 범위. 전체 파일 금지. max 읽기 ~200kB 가정.
- unityMCP: Hierarchy/컴포넌트/씬 조작, 콘솔 읽기만. 파일 임의 접근 금지.
- puppeteer: 로그인 불필요 페이지 요약·스크린샷 1장. 본문 덤프 금지.

[Unity 코드 표준]
- SOLID. 단일 책임. 메서드 ≤50줄, 파일 ≤300줄.
- `nullable enable`. Update/FixedUpdate **할당 0**. LINQ/박싱/foreach(alloc) 금지.
- 모든 `[SerializeField]`에 **한국어** `[Header]/[Tooltip]`으로 “무엇을 조절하는지” 명시.
- 숫자 상수는 `const/readonly`. 단위 표기(초, %, 프레임).
- 이벤트/인터페이스 우선. 기존 시그니처 변경 금지(어댑터로 확장).

[패치 생성 규칙]
- 요청된 파일 **해당 줄 범위만** 수정. 다른 라인 불변.
- 제출물은 **diff 패치**와 컴파일 결과 요약만.
- 실패 시 되돌림 가능한 **단일 패치**로 재시도.

[리뷰·로그]
- 리뷰는 **git diff 기준**. 지적사항 ≤5개 요약.
- 로그는 tail만 사용(예: Editor.log 마지막 300줄).

[성능·품질 기준]
- 1080p@60fps, 메인스레드 <6ms, **GC 0B/frame**.
- 널가드, 조기 반환, 예외는 상위 1회 처리.
- 수용기준 예: “패링창 5–15프레임, 브레이크 8회=무력화”.

[금지]
- 프로젝트 전체 본문 덤프, 바이너리/캐시 폴더 접근.
- 전역 싱글톤 남발, 하드코딩 레이어/태그, Resources.Load 남용.
- 의미 없는 리팩터링 제안, 설명만 길게.

[세션 시작 매크로(항상 첫 메시지에 붙임)]
- “file-mcp-smith 루트=C:\Users\82105\Documents\GitHub\chuon-test. deny=Library/ Temp/ Logs/ Build/ Obj/ 및 모든 바이너리 확장자. 텍스트는 n–m줄만. 수정은 diff 패치만.”

[지시 템플릿]
1) 검색: `"IHitReceiver"를 참조하는 C# 파일 경로만 나열.`
2) 부분열기: `Assets/.../PlayerDamageReceiver.cs 1–160줄만 열어.`
3) 패치: `40–70줄 널가드/조기반환 추가. 다른 라인 불변. diff 적용 후 결과 diff만 출력.`
4) 확인: `컴파일/에디터 경고 변화 요약(3줄 이내).`
5) 옵션: `Hierarchy 루트에 빈 오브젝트 MCP_Ping 생성 후 선택(unityMCP).`

[중단 조건]
- 줄 범위를 벗어나는 수정 제안, 바이너리 접근 요구, 문서 전문 덤프 시 즉시 중단 후 사유 보고.
