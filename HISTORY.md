# 변경 기록

커밋마다 한 항목. **최신이 위.** 리뷰(GPT Codex 등)가 "무엇을 요청했고 그래서 무엇을 바꿨는지"를 빠르게 보기 위한 요약이다.
세부는 커밋 diff, 화면 규칙은 [DESIGN.md](DESIGN.md), 대화 맥락은 [conversations/](conversations/)에 있다.

항목 형식: 날짜 · 커밋 제목 / **요청** / **변경** / **확인** / **남은 것**(있으면). 짧게.

---

## 2026-09-30 · 0단계 전환 다듬기, N.I.N.A. 감시, 장비 변경 실패 처리

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
