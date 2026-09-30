# 변경 기록

커밋마다 한 항목. **최신이 위.** 리뷰(GPT Codex 등)가 "무엇을 요청했고 그래서 무엇을 바꿨는지"를 빠르게 보기 위한 요약이다.
세부는 커밋 diff, 화면 규칙은 [DESIGN.md](DESIGN.md), 대화 맥락은 [conversations/](conversations/)에 있다.

항목 형식: 날짜 · 커밋 제목 / **요청** / **변경** / **확인** / **남은 것**(있으면). 짧게.

---

## 2026-10-01 · 기준 화면 1920×1080, 관측지·경통, 전환 중 조작 막기, 앱 아이콘

**요청** (집 PC)
- 사용자 4K 노트북(배율 200%) 기준으로 화면을 만들고 다른 해상도는 통째로 확대·축소, 울트라와이드는 좌우 여백
- 관측지: 프로필별 목록, 장비 연결 뒤 확인·변경, 관측지 고르기 화면(카카오맵, 두 번 눌러 설정, 이름만 수정), 상태 줄 관측지, 프로필 사진으로 프로필 전환, 편집은 시작 화면에서
- 경통: 연결 장비가 아니라 NINA가 모르는 광학 정보 → AA 경통 목록(수동 입력), 장비 연결 화면의 "경통" 원
- 화면 전환 중 클릭 막기(전체 규칙), 관측지 화면 진입 효과(카드 뒤집기·빛 효과), 1단계 → 장비 연결 원 크로스페이드, 앱 아이콘

**변경**
- `frame.ts`·`App.module.css` `.shell`: 1920×1080 틀 + scale, 크기 컨테이너. CSS의 `vw`/`vh` → `cqw`/`cqh`. `scaleOf`로 화면 좌표 보정. 장비 그래프 상한 1100×680 제거(높이 기준, 가로 1.6배까지)
- 서버: `ProfileStore`(Sites, Edit/AddSite/RenameSite/RemoveSite), `SiteService`(NINA 위치 읽기·쓰기 SSE, Open-Meteo 고도), `OpticsStore`(optics.json), `EquipmentConnector` 경통 슬롯(NINA에 초점거리·F값·이름 씀), `RigSetup` 경통 고르기, `TonightService` 경통 우선, API `/site/*`, `/profiles/{id}/sites`, `/optics`, `/config`(카카오 키)
- 화면: `SiteScreen`, `SiteMenu`, `KakaoMap`(틀 배율 되돌려 1:1), `UndoToast`, `ScopeList`, `StatusBar`(관측지·프로필 전환), `ProfileScreen`·`NewProfileScreen` 편집, `EquipmentScreen`(관측지 문장·가리키면 멈춤·버튼을 원 안으로·경통 원·크로스페이드), `JarvisRing` action, `screenReady.ts` + App `settled`(`inert`)
- `app.ico`(16~256px)·`favicon.png`, DESIGN.md(1·3·9·10장), README(카카오 키), flow.mmd(관측지 단계), 시안 `mockups/aa-sites.html`

**확인**
- 테스트 서버(임시 데이터): 관측지 없음 → 막대 → 관측지 고르기, 카카오 장소 검색·고도 자동·추가·저장 → NINA에 위도·경도·고도 저장 확인 후 0으로 되돌림, 상태 줄 목록·관측지 편집 이동
- 경통 API: 추가·배율 계산·지금 경통 삭제 거부·검증 문구, 연결 확인 시 NINA 초점거리 260·F3.8 저장 (사용자 NINA에 그대로 둠)
- 3440×1440·1920×1080·1280×720 틀 크기·배율 수치 확인. 사용자가 실제 앱에서 울트라와이드·관측지 흐름 확인
- 확인 못 함: 애니메이션(카드 뒤집기·빛·크로스페이드)과 경통 목록 모양은 테스트 창에서 멈춰 사용자 확인 필요. 실제 모드 적도의 위치 보내기 실기 미검증

**남은 것**: 모핑 효과(대화 기록 6장), ③ 준비 단계, 실기 확인

---

## 2026-10-01 · fcb0935 울트라와이드 화면 대응과 장비 연결 문장 사라짐 수정

**요청** (집 PC, 3440×1440 울트라와이드·배율 100%에서 처음 실행)
- 새 프로필 화면 요소가 세로로 벌어짐, 설치 확인에서 선택된 쉐브론이 옆 칸을 덮음, 장비 변경 모드 원들이 같은 x에 몰림
- 장비 연결에서 다시 연결이 실패하면 가운데 원 문장이 사라짐
- 울트라와이드에서는 억지로 늘리지 말고 표준 비율만큼 가운데에, 좌우는 여백으로 (사용자 결정)

**변경**
- `App.module.css` `.shell`: 최대 너비 = 화면 높이 × 16/9, 가운데 정렬. DESIGN.md 1장에 규칙
- `NewProfileScreen.module.css`: `align-content`로 위아래 가운데 (줄이 늘어나지 않게)
- `StepChevrons.module.css`: 칸 너비 상한 480px(확대 10% − 칸 사이 8px ≤ 화살표 홈 42px), 띠 가운데. `CheckStepsScreen.module.css`: 제목·아래 영역도 같은 최대 너비
- `EquipmentScreen.tsx` 변경 모드 가로 자리: 기준 너비를 min(창 너비, 그래프 너비 ÷ 0.8)로. 그래프(최대 1100px) 밖으로 나가 양 끝에 몰리던 문제
- `JarvisRing.tsx` `useSmoothContent`: 바뀌려던 글자가 원래 글자로 돌아오면 다시 밝게 (흐려진 채 남던 버그)

**확인**
- 임시 데이터 폴더·별도 서버(5211)로 3440×1440 화면 확인: 앱 영역 2560px 가운데, 새 프로필 모임, 쉐브론 확대 칸과 옆 칸 사이 약 3px, 카메라 다시 연결 실패 뒤 문장 유지
- 변경 모드 배치는 테스트 브라우저에서 펼침 애니메이션이 멈춰 좌표 계산으로만 확인 → 사용자가 실제 앱에서 확인함
- 사용자가 실제 데스크톱 앱에서 네 가지 모두 확인

**남은 것**
- 회사 노트북 창이 1375px보다 넓으면 변경 모드 원 간격이 이전보다 조금 좁아질 수 있음 → 노트북에서 확인
- 로컬 폴더 이름을 `AstrophotoAssistant`로 바꿈(집 PC). 코드 영향 없음

---

## 2026-09-30 · f75b786 Codex 리뷰 지침(AGENTS.md) 추가

**요청**
- GPT Codex가 리뷰 전에 HISTORY.md를 먼저 읽도록 사용자가 준 지침을 Codex가 `AGENTS.md`로 저장 → 다른 PC의 Codex도 같은 지침을 쓰게 저장소에 올리기

**변경**
- `AGENTS.md` 추가 (Codex 작성, 내용 그대로): 리뷰 시 HISTORY.md 먼저, 기록은 맥락·검증은 실제 코드와 diff, 시뮬레이션·미구현·실기 미검증은 결함으로 보지 않음, 리뷰만 요청받으면 수정하지 않음
- 앞 항목 제목에 커밋 해시 채움

**확인**
- 내용 검토: 개인 정보·키 없음, CLAUDE.md 지침과 충돌 없음. 코드 변경 없음

---

## 2026-09-30 · 6e747bc 0단계 전환 다듬기, N.I.N.A. 감시, 장비 변경 실패 처리

**요청**
- 0단계: 다시 확인(새로고침) 때 화면이 번쩍이며 갱신되지 않게, 새로고침 아이콘을 통과 체크 자리·크기로, "다음" 누르면 쉐브론이 왼쪽부터 차례로 사라지게
- N.I.N.A.가 실행 중 꺼지면 처리 (로컬 폴링 부담 질문 → 프로세스 종료 알림 + 느린 응답 확인으로)
- 커밋마다 요청·변경을 누적하는 기록 파일 (GPT Codex 리뷰용)
- Gemini 리뷰 반영: 실제 모드에서 장비 바꾸기·제거가 실패해도 선택이 저장되고, 연결 확인이 어떤 장비인지 보지 않아 "이름 B, 실제 A"가 연결 완료로 보이는 문제

**변경**
- `checks.ts` `recheckOne(quiet)`: 확인 중 표시 없이, 결과가 같으면 항목을 건드리지 않음. `CheckStepsScreen`: 재확인 중 아이콘만 회전(최소 0.7초), "다음" → 완료 문장·버튼 즉시 숨김 + 쉐브론 0.11초 간격 페이드 → 다음 단계. `StepChevrons`: 새로고침을 체크와 같은 그리드 칸에 겹쳐 배치. 1단계 화면 페이드인
- `NinaWatcher`(서버): 1단계 연결 뒤 `Process.Exited`로 종료 즉시 감지 + 10초마다 `/version`, 3회 무응답 = 멈춤. `GET /api/nina/watch`(SSE), `[임시] POST /api/nina/simulate-exit`
- 화면: 장비 연결 이후 단계에서 끊기면 `NinaLostCard`(모달 아님) + 상태 줄 장비 점 끔 → "다시 켜기" → 엔진 켜기 → 장비 연결 → 원래 화면으로 복귀(점검 생략, 계획 유지)
- `RigSetup.SelectAsync`(실제 모드): 바꾸기 = 지금 장비 끊기 → 새 장비 연결 → 확인될 때만 저장, 실패 시 원래 장비 재연결 + 오류 문장. 제거 = 해제 확인될 때만. 반환 `{ item, error }`
- `LiveDevices`: AA가 연결을 확인한 장비 Id. `EquipmentConnector` 연결 확인은 이 기록(또는 AA 선택이 없어 프로필 그대로인 경우)만 "이미 연결됨"으로 믿고, 아니면 끊고 새로 연결
- 변경 모드 원 아래 실패 문장 표시. DESIGN.md·onboarding 6장(실기 확인 항목)·대화 기록 갱신
- `HISTORY.md` 신설, CLAUDE.md에 "커밋 규칙"(커밋마다 이 파일에 항목 추가), README에서 연결

**확인**
- 헤드리스(시뮬레이션): 재확인 중 아래 영역 변화 없음, 아이콘 위치 = 체크 위치(55px), 쉐브론 순차 페이드, N.I.N.A. 종료 → 카드 12ms → 다시 켜기 → 계획 화면 복귀
- 모의 N.I.N.A.(가짜 HTTP) 12개 경우: 바꾸기 실패/성공, 재시작 뒤 다른 장비가 연결돼 있는 경우, 제거 실패/성공 모두 통과. 장비 변경 모드 흐름 회귀 통과

**남은 것**
- 실기 확인: `connect?to=`로 프로필 저장되는지, `profile/change-value` settingpath, `equipment/{종류}/info`에 장비 이름·Id가 나오는지(나오면 동일 장비 비교로 강화)
- 촬영 중 N.I.N.A. 복구, Advanced API WebSocket 이벤트 — 촬영 화면 만들 때

---

## 2026-09-30 · b11226d Planning screen with AI, equipment edit mode, step rail, preflight checklist

**요청** (집 PC 시안을 앱으로 + 장비 연결 화면 여러 차례 수정)
- 하단 단계 레일, 1단계 인터넷 확인, 출발 전 점검, 촬영 계획 화면(실제 AI, 나중에 다른 AI로 바꿀 수 있게)
- AI 한도가 없을 때도 시연 가능하게, 장비 등급(필수/준필수/선택/제외), AA 안에서 장비 등록·변경·제거
- 장비 연결 화면 다듬기(배치·크기·애니메이션·상태 표현 등), 전체화면 시작, 앱 끄기 버튼

**변경**
- `StepRail`, `InternetCheck`, `NinaApiClient` 요청별 대기 시간(조회 5초 / 연결 60초), `PreflightScreen`
- 계획: `Astro.Core/Sky`(천문 계산), `DsoCatalog`(NINA.sqlite), `TonightService`(프로필·Open-Meteo), `Astro.Core/Assistant`(회사 무관 대화 형식) + `GeminiChatModel` + `ScriptedChatModel`(연습 대화, 한도·키 문제 시 자동 전환), `PlanTools`/`PlanAssistant`, `PlanScreen`/`NightChart`. 키는 사용자 비밀 저장소·환경 변수만
- 장비: 등급·`Absent` 상태, "없이 진행", 장비 변경 모드(`RigSetup`, `rig-overrides.json`, list-devices), 4초 자동 진행 막대, 흡수·펼침, 균형 무작위 배치(앱 재시작까지 고정), 허브 깜빡임, 상호작용 상태 네 가지
- 데스크톱: 전체화면(F11), 단일 실행

**확인**: 헤드리스 흐름 테스트(시뮬레이션·연습 대화), 천문 계산은 서울 기준 공개 시각과 대조, Gemini 실제 대화 확인(이 과정에서 무료 한도 일부 소진 — 이후 AI 테스트는 사전 확인)

**남은 것**: 계획 화면 검토, ③ 준비 단계, 실기 테스트 목록(onboarding 6장)

---

이전 커밋(2026-09-29 이전)은 이 파일이 생기기 전이라 `git log`와 [conversations/](conversations/)를 본다.
