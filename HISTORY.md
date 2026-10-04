# 변경 기록

커밋마다 한 항목. **최신이 위.** 리뷰(GPT Codex 등)가 "무엇을 요청했고 그래서 무엇을 바꿨는지"를 빠르게 보기 위한 요약이다.
세부는 커밋 diff, 화면 규칙은 [DESIGN.md](DESIGN.md), 대화 맥락은 [conversations/](conversations/)에 있다.

항목 형식: 날짜 · 커밋 제목 / **요청** / **변경** / **확인** / **남은 것**(있으면). 짧게.
작성자 표시: 제목에 `[Claude]` 또는 `[Codex]`. 리뷰(REVIEW_CODEX.md)를 반영한 커밋은 반영한 리뷰 ID와 반영하지 않은 이유를 적는다.

---

## 2026-10-05 · [Claude] 준비 시안 정리: Codex v3 추가, 수식어 붙은 시안 삭제

**요청**: `aa-prepare-arcs`로 시작하는 시안 중 v2·v3·v4만 남기고 뒤에 수식어가 붙은 것은 저장소에서도 삭제

**변경**: `mockups/aa-prepare-arcs-codex.html` 삭제, 미추적이던 `aa-prepare-arcs-v2-orbit-codex.html` 삭제, Codex의 `aa-prepare-arcs-v3.html`(v4의 바탕) 추가. v1 `aa-prepare-arcs.html`은 수식어가 없어 남김

**확인**: 남은 시안 v1·v2·v3·v4. 다른 문서에서 삭제한 파일을 가리키는 곳은 과거 기록(대화·HISTORY·REVIEW_CODEX)뿐

---

## 2026-10-05 · 0668730 [Claude] 준비 7단계 검토·실기 시험, 시안 v4(7단계 새 흐름), 준비 구현 계획(Codex 리뷰용)

**요청**
- 준비 7단계를 하나씩 검토하고 결정마다 v4 시안에 반영 (Codex v3 바탕)
- 실제 장비(가이드 카메라·적도의 UMi17X·허브·포커서)로 확인 가능한 것은 지금 확인
- Codex 리뷰용으로 1~7단계를 앱에서 어떻게 만들지 정리 (새 파일은 꼭 필요할 때만)

**변경**
- `mockups/aa-prepare-arcs-v4.html`: ① 위성 4개(넘겨받기·극 찾기·정렬·돌려주기), 정렬 중 위성 안 SharpCap 화면 + AA 평가 ② 위치 A→B, 가이드 사진 위성 ③ 기다리기·이동 막음·멈춤 ④ 지난 위치에서 시작·넓게 훑기→손 초점 ⑤ 결과 말·카메라 방향 기억 ⑥ 기준 2.41″·세 등급 ⑦ 자동 검사·9칸 보기·노출 변경 묻기. 경우 버튼 "시험 사진 배경이 밝음" 추가
- DESIGN.md 3장 "촬영 준비" ①~⑦: 사용자 결정·실패 처리·실기 결과 (① 90° 회전은 goto 아닌 RA만, ② 캘리브레이션 위치 A/B, ③ 낮은 대상·자오선 반전은 계획 단계에서 알림, ④ 포커서 한계는 제조사 기능, ⑥ 판정 기준 등)
- `docs/api-coverage.md`: N.I.N.A. slew `ra`는 도 단위, 스위치 `switch/set`이 안 닿음 → ASCOM 직접 `SetSwitch`, WandererBox 직렬 규격 요약
- `docs/PREPARE_SCREEN_CONTENT.md` → `docs/PREPARE_IMPLEMENTATION.md`로 이어 씀 (화면 내용은 DESIGN.md로 옮겨졌으므로 구현 계획으로 교체)
- 대화 기록 `conversations/2026-10-02-work-laptop.md` 4~7절, 맑은 날 할 일

**확인**
- 실기(실내, 별 없음): PHD2 카메라 넘겨주기·SharpCap 자동 실행·스크립트 HTTP·창 캡처·재연결, N.I.N.A.·PHD2 적도의 동시 연결, Go Home, goto A/B 오차 0.38°, 펄스 가이드 4방향, 포커서 이동·온도, 스위치 직접 제어
- 처음 goto 이상은 Claude의 단위 실수(시간으로 보냄)였음 — 바로잡고 기록
- v4 시안은 브라우저에서 단계마다 정상·실패 흐름 확인. 앱 코드 변경 없음
- Codex의 미추적 시안(`aa-prepare-arcs-v2-orbit-codex.html`, `aa-prepare-arcs-v3.html`)은 이 커밋에 넣지 않음

**남은 것**: Codex와 v4·구현 계획 리뷰 → 앱 착수 결정. 극에서 RA 60° 멈춤 원인, 맑은 날 실기 목록

---

## 2026-10-03 · 22cac17 [Claude] 준비 시안 v2(큰 하늘 화면 + 글 얹기, 극축 정렬 위성), 준비 절차 결정 추가

**요청**
- 가운데 화면을 더 넉넉하게: 화면을 오른쪽에 매우 크게, 글은 그 위에 얹기 (v2로 별도 파일)
- 라이브뷰가 없는 단계에 항성계 위성 실험: 세부 과정 3개가 비스듬한 궤도를 돌고, 맨 앞 위성 안에 그 과정 정보만, 지난·다음은 과장되게 흐리게, 도형은 크게
- 자동초점 설정은 AA 추천값, 구도 회전·이전 단계로 돌아가기·시험 사진 용도 정리

**변경**
- `mockups/aa-prepare-arcs-v2.html` (v1 `aa-prepare-arcs.html`은 그대로)
- DESIGN.md "촬영 준비": ④ 자동초점 설정 추천값(N.I.N.A. 설정 불변의 예외), ⑤ [나중] 구도 회전, ⑦ 시험 사진 용도·첫 장으로 안 씀, 이전 단계로 돌아가기(결과 보기 + 다시 하기, 영향받는 뒤 단계만)
- `docs/codex/REVIEW_CODEX.md` 처리 표 커밋 해시(01e0268), 대화 기록 `conversations/2026-10-02-work-laptop.md` 3장

**확인**: v2를 헤드리스로 7단계·위성 전환·다시 보기(원이 끝까지 돌아온 뒤 펼침)까지 오류 없이 확인. 앱 코드 변경 없음

**남은 것**: 사용자가 Codex와 시안을 더 다듬은 뒤 확정 → 준비 화면 앱 코드 다시 만들기

---

## 2026-10-02 · 01e0268 [Claude] 촬영 준비 7단계 원호 시안, Codex 준비 리뷰 처리, 항성계·깊이감·스켈레톤 방향

**요청**
- Codex 원호 시안 스타일로 준비 1~6단계 시안 → Codex 리뷰(8절 CX-PREP-FLOW) 반영해 7단계로, 정보는 한 곳에만, 원호를 왼쪽으로 밀어 공간 확보
- 극축 정렬 절차 확정(단계 끝은 모두 수동, PHD2 카메라 해제 시간 초과 처리, 추적 속도 항성), 캘리브레이션 기본값(준비 시작마다 새로·같은 밤 재사용)
- 항성계 화면 비유·Z축 깊이감(패럴랙스)·스켈레톤 스크린은 방향만 기록(마감 때)

**변경**
- `mockups/aa-prepare-arcs.html`(Claude 7단계 시안), `mockups/aa-prepare-arcs-codex.html`(Codex 비교안, 리뷰 8절이 참조)
- DESIGN.md: 2장 "화면 비유: 항성계"(+ Z축 깊이감), 1장 "[마감 때] 스켈레톤 스크린", 3장 "촬영 준비" 다시 씀(7단계·단계 끝 수동·극축 정렬 세부·캘리브레이션·정보는 한 곳에만·핵심 수치)
- flow.mmd ③ 준비 7단계. `docs/codex/REVIEW_CODEX.md` 7절·9절 처리 표(Codex가 추가한 8절 리뷰 본문 포함). `.gitignore`에 `.obsidian/`. 대화 기록 `conversations/2026-10-02-work-laptop.md`

**확인**: 시안을 헤드리스로 7단계·실패·대상 낮음·캘리브레이션 재사용·크게 보기까지 오류 없이 확인, flow.mmd 문법 검사. 앱 코드 변경 없음

**남은 것**: (집) 사용자가 최신 시안을 보고 정리해 올 피드백 반영 → 시안 확정 뒤 준비 화면 앱 코드 다시 만들기

---

## 2026-10-02 · 915e558 [Claude] 촬영 준비 시안 세 가지와 집 PC 논의 기록

**요청**: 오늘 집 PC에서 한 준비 화면 시안·논의를 커밋·push (회사에서 Codex 원호 시안 기준으로 이어서 작업 예정)

**변경**: 시안 `mockups/aa-prepare.html`(왼쪽 목록 + 작업 영역, 탈락), `aa-prepare-step.html`(한 화면 한 목표 1단계, 탈락), `aa-prepare-jarvis.html`(자비스 원 + 단계 원 움직이는 시안 — Codex 원호 시안의 출발점). `conversations/2026-10-01-home-pc.md` 7장: 준비 화면 방향 변화, 6단계로 합침(가이드 연결 → 극축 정렬 안), 완료 표시(체크 그리기 + 느린 빛), 준비 중단 위치, SharpCap·TPPA 조사, 할 일(낮음: SharpCap 값 가져오기, TPPA 속도 시험, 나중: HUD 마감)

**확인**: 시안·문서만. 앱 코드 변경 없음. 움직이는 시안은 테스트 창에서 애니메이션이 멈춰 직접 보지 못함. Codex가 만든 탈락 시안 두 개(`aa-prepare-asymmetric-codex.html`, `aa-prepare-jarvis-codex.html`)는 Codex 기록대로 커밋에서 제외(파일은 남김)

**남은 것**: Codex 원호 시안 기준으로 준비 화면 구현, 단계 안 세부 과정 표시 방식(사용자 검토 중)

---

## 2026-10-02 · 9904446 [Codex] 촬영 준비 원호 시안과 디자인 논의 기록

**요청**: 오늘의 유효한 마지막 시안과 대화 기록만 커밋·push. 탈락 시안과 Claude 작업은 제외.

**변경**: `mockups/aa-prepare-edge-arcs-codex.html` 추가. “촬영을 준비합니다” 원이 확대되어 왼쪽의 둥근 여섯 원호 조각으로 이어지며 현재 단계만 강조. 큰 원 윤곽과 안팎의 미세한 배경색 차이를 유지. `conversations/2026-10-02-codex-design.md`에 사용자 결정·탈락 이유·남은 논의 기록.

**확인**: 원호 시안의 첫 안내 화면은 브라우저에서 확인. 마지막 윤곽·배경색 변경은 JavaScript 문법과 파일 검토까지 확인했으며 시각 재확인은 못 함. 실제 앱 코드·DESIGN.md 변경과 장비 실행 없음.

**남은 것**: 단계 내 세부 과정의 수와 진행을 보여주는 방식은 사용자 검토 중. 목업은 최종 확정 또는 앱 적용 완료가 아님.

---

## 2026-10-01 · 4cc0ce7 [Codex] 준비 화면 UI 리뷰와 합의 기록

**요청**: 합의한 준비 화면 UI 리뷰를 기존 리뷰 파일에 추가하고 커밋·푸시.

**변경**: `docs/codex/REVIEW_CODEX.md`에 CX-PREP-UI-01~07 추가. 세로 진행 목록과 큰 작업 영역, 단계별 사진·그래프, 핵심 수치, 사용자 행동, 과거 결과 조회, 데이터·중단 동작, 대표 화면 목업 방향을 기록. 기존 계획 리뷰와 처리 표는 유지하고 새 항목의 구현·재리뷰 상태를 분리.

**확인**: 문서 변경 범위와 `git diff --check` 확인. 앱 코드 변경 없음; 실행·빌드·실장비 검증은 하지 않음.

**남은 것**: 합의한 UI 구현, 데이터 제공 범위와 중단 동작 구체화, 구현 후 Codex 재리뷰.

---

## 2026-10-01 · [Claude] 문서 폴더 정리, 준비 화면에 필요한 내용 정리

**요청**
- 저장소 맨 위 md 파일이 너무 많다 → 문서 관리 폴더와 위치 정리
- 준비 화면 구성을 다른 도구·GPT와 검토할 수 있게 필요한 내용을 단계별로 정리 ("○○ 중입니다"만 있어 피드백이 부족 → 실제 장비가 보는 것·줄어드는 숫자를 보여 주는 방향)

**변경**
- 맨 위에는 README·CLAUDE·AGENTS·DESIGN·HISTORY만. `docs/`(기획서, 구조와 온보딩, NINA 사전 준비, API 대응표), `docs/codex/`(REVIEW_CODEX.md, IMPLEMENTATION_REQUEST_CODEX.md)로 옮김
- 링크 고침: README, CLAUDE.md, 대화 기록, 문서 간 링크, 코드 주석(EquipmentConnector, SetupChecker). REVIEW_CODEX.md는 "근거 문서" 링크 경로만, 처리 표 해시(c95e24d)
- CLAUDE.md·AGENTS.md에 "문서 위치" 절
- `docs/PREPARE_SCREEN_CONTENT.md`(임시): 단계별 실시간 피드백·목표 숫자·결과 한 줄·버튼·데이터 출처. 화면 구성이 정해지면 DESIGN.md에 합치고 지움

**확인**: 문서·링크만 변경 (옮긴 파일을 가리키는 경로를 저장소 전체에서 찾아 고침). HISTORY.md 과거 항목의 옛 파일 이름은 기록이라 그대로

**남은 것**: 준비 화면 구성 검토(사용자) 뒤 화면 다시 만들기

---

## 2026-10-01 · c95e24d [Claude] 계획 확정 구조, 촬영 준비 화면 골격(모의), 시퀀스 재개 확인, 버전 표시, 장비 바뀐 계획 갱신

**요청**
- Codex 리뷰(REVIEW_CODEX.md) 검토 후 결정: CX-PLAN-06 지금 고치기, 01 미리보기 유지, 진행 순서 06 → 시퀀스 실험 → 계획 전달 구조 → 준비
- Codex 요청서(IMPLEMENTATION_REQUEST_CODEX.md) 검토 후 결정: 준비는 단계별 API 호출, 극축정렬 생략 안 함, 이동 승인 한 번, 대상이 30° 아래면 알리고 멈춤, 시험 사진 항상, 가이딩은 픽셀 기준 규칙 판정 + (온라인이면) AI 설명 + 사용자 확정, SharpCap 설치 확인은 V0.2로
- 부팅 화면·상태 줄에 빌드 버전 표시. 장비 없이 안 되는 것은 성공 가정으로 진행

**변경**
- CX-PLAN-06: `PlanAssistant.EnsureStartedAsync`가 같은 밤 장비 변경을 감지 → `ApplyRig`(화각 재계산, 노출에 영향 주는 장비면 촬영 설정 칸 비움 + 안내), `ProfileSelected`(다른 프로필이면 새로)
- W2 계획 확정: `Plan.cs` — `PlanTarget` J2000 좌표, `PlanFraming.RotationDegrees` null = 방향 유지(CX-PLAN-02), `EndRule`(CX-PLAN-04), `PlanAfter` 구조화, `PreparationPlan.From`(실행 가능 여부 확인) + `POST /api/plan/confirm`·`GET /confirmed`. 30° 위로 안 오는 대상은 예상 0장·촬영 띠 없음. 첫 인사 문장, AI 지시(정해 온 대상 바로 받기, 회전은 말할 때만)
- W3 준비: `Prepare/PrepareRunner.cs`(7칸 상태 기계, SSE `/api/prepare/watch`, `/act`), `Prepare/PrepareDevices.cs`(`IPrepareDevices` + `SimulatedPrepareDevices`, [임시] fail-next·고도 무시), `PrepareScreen.tsx`(쉐브론 7칸 + 공통 영역), App에 촬영 자리(빈 화면)
- 버전: `product.json` version + 커밋 수 → `vite.config.ts` `__VERSION__`, `StatusBar`·`BootScreen`
- 문서: DESIGN.md(계획 화면 규칙, "촬영 준비" 절), flow.mmd ③ 7단계, api-coverage.md(재개 확인), README PowerShell 예시, REVIEW_CODEX.md 처리 표(01~07), 요청서 파일 함께 커밋

**확인**
- 시퀀스 재개 실험 (이 노트북 N.I.N.A., 실험용 프로필 AstroAssistant + 시뮬레이터 카메라): stop → start가 끝난 항목은 건너뛰고 완료 장수 유지(5장 중 3장 뒤 중지 → 다시 시작 → 파일 정확히 5장)
- 연습 대화 API: 망원경 바꾸면 화각 5.17° → 1.5°·채움 61% → 211%·설정 칸 비움, 같은 프로필 재선택 유지, 다른 프로필 새로. M31 확정 값, NGC 104 확정 거부·0장
- 헤드리스(1920×1200): 준비 7칸을 극축정렬 → 대상 낮음 멈춤 → 이동 → 초점 실패·재시도 → 가이딩 판정 → 시험 사진 → 촬영 시작까지. 버전 v0.0.26 표시
- 실제 AI 호출·실장비 없음. 실제 앱 창 확인은 사용자가 쉐브론 화면까지 봄. 흐름도 검사 때 실수로 Edge 헤드리스가 한 번 실행됨(스크립트 고침)

**남은 것**: 준비 화면을 세로 진행 목록 + 큰 작업 영역으로 바꾸기(사용자 확인, 쉐브론 반복이 식상), W4(SharpCap·PHD2 실제 연결) ~ W7(촬영·종료), 시험 사진 실제 표시, 가이딩 AI 설명, N.I.N.A. 이동 명령이 J2000을 받는지 시뮬레이터 적도의로 확인

---

## 2026-10-01 · 86afaff [Claude] 장비 연결 화면 다듬기, 경통 → 망원경, 리프트 공통 규칙

**요청**
- 장비 연결: 진행 막대는 모두 연결됐을 때만(변경 모드에서는 숨김), 멈춤 대신 "변경" 버튼을 가리키는 동안 0.3배속, 다시 연결 아이콘 두 배, 허브 문구 줄바꿈, 망원경 원은 이름만, 등록 안 된 작은 원에 종류 이름표 + 떠오름, 연결된 원도 떠오름
- 화면 문구 "경통"을 모두 "망원경"으로. 떠오름(lift)을 공통 규칙으로 하되 나중의 앱 전체 공간감 작업과 부딪히지 않게
- 규칙: 수정을 시작하면 실행 중인 AA를 먼저 종료

**변경**
- `EquipmentScreen.tsx`: 진행 막대를 CSS 애니메이션 → `requestAnimationFrame`으로 직접 그림(`elapsed`·`slow`·`SLOW_RATE`), `slowProps`를 "장비 변경"(감싼 `slowZone`)·관측지 "변경"에. 작은 원 `nodeTip`, `hoverBody[data-without]`
- `EquipmentScreen.module.css`: `.progress[data-shown]`, `.retryIcon` 크기, 연결된 원·작은 원 hover lift, `.nodeTip`
- `index.css`: 공통 토큰 `--lift`·`--lift-ms`, 테마별 `--lift-shadow`
- `EquipmentConnector.cs`: 슬롯 이름 "망원경", 칸 글씨는 이름만. 그 밖의 "경통" 문구·주석을 "망원경"으로 (ScopeList·PreflightScreen·OpticsStore·ScriptedChatModel·flow.mmd 등)
- DESIGN.md 3장(장비 연결·관측지 막대·망원경), 6장 "업계 용어보다 일상어", 9장 lift 공통 규칙. CLAUDE.md "실행 중인 앱"

**확인**
- 헤드리스 1920×1200(연습 대화·시뮬레이션): 연결 중 막대 숨김, 모두 연결 뒤 600ms에 15%·"장비 변경" 가리키면 4.6%(≈0.3배), 연결된 원·작은 원 lift(transform 확인), 작은 원 이름표 "필터휠", 실패 시 아이콘 99~112px로 원 안에 들어감, 허브 문구 두 줄, 점검 화면 "망원경 캡"·"망원경 밸런스" 안 잘림, 전 화면 스크롤 없음
- 실제 앱 창에서는 아직 못 봄

**남은 것**: 앱 전체 공간감(z축) 검토 때 lift를 그 체계로 흡수

---

## 2026-10-01 · ad79424 [Claude] 리뷰 처리 결과 기록 규칙

**요청**
- Codex 리뷰를 반영한 결과를 REVIEW_CODEX.md에도 남길지 → 짧게 남기기로

**변경**
- `CLAUDE.md` 리뷰 규칙: REVIEW_CODEX.md는 "처리 및 재리뷰 기록" 표만 한 줄씩(상태·근거·커밋 해시), 자세한 변경은 HISTORY.md에. Codex 본문·재리뷰 칸은 고치지 않음

**확인**: 문서만 변경

---

## 2026-10-01 · 9000332 [Claude] 기준 화면 16:10 (1920×1200), 달 이름표 잘림 수정, 리뷰 규칙

**요청**
- 현장 노트북 3840×2400을 배율 200%로 쓰기로 함 → 16:10에서 최적, 다른 비율에서도 깨지지 않게
- HISTORY.md 작성자 표시(`[Claude]`/`[Codex]`), Claude 구현 → Codex 리뷰(REVIEW_CODEX.md) → 사용자와 논의 후 반영하는 절차를 규칙으로

**변경**
- `frame.ts` `FRAME_H` 1080 → 1200, `App.module.css` `.shell` 높이 1200px. 나머지는 이미 `cqw`·`cqh`라 그대로
- `NightChart.tsx`: "달 ○○ 짐"이 그래프 오른쪽 밖으로 나가면 막대 왼쪽 "달 ○○ 뜸"으로
- DESIGN.md 1장 "기준 화면" 값·예시, `CLAUDE.md` 커밋 규칙·리뷰 규칙, 대화 기록 `conversations/2026-10-01-work-laptop.md`(집 PC 기록의 1920×1080 문구에 정정 줄)

**확인**
- 헤드리스로 프로필 → 점검 → 장비 → 출발 전 점검 → 계획 순회, 모두 스크롤 없음: 1920×1200·1536×960·1280×800 여백 없음, 1920×1080 좌우 96px, 3440×1440 좌우 568px
- 달 이름표는 1920×1200 계획 화면 스크린샷으로 확인. 실제 앱(AA.exe)·실제 노트북 200%에서는 아직 못 봄

**남은 것**: 16:10에서 0단계 점검 화면 패널 아래 빈 자리 처리(사용자와 상의), REVIEW_CODEX.md CX-PLAN-01~07 검토

---

## 2026-10-01 · c2f3b47 [Codex] 촬영 계획 설계 리뷰 기록

**요청**
- 다른 AI의 리뷰와 구분할 `REVIEW_CODEX.md`에 계획 단계 검토 내용을 남기고, 작성자 라벨이 있는 히스토리와 함께 커밋

**변경**
- `REVIEW_CODEX.md`: 계획 단계의 설계 검토 7항목, 정책 미결정 사항, Claude 처리 결과와 Codex 재리뷰 기록표
- 문서 작성 승인과 제안의 구현 승인을 구분하고, 시뮬레이션 개발 범위와 실기 검증 과제를 명시

**확인**
- Codex가 문서 및 작성 대화 기록을 확인하고 커밋 범위를 검토. 설계 검토는 정적 확인이며 앱 빌드·AI 호출·장비 제어는 수행하지 않음

**남은 것**: Claude의 항목별 재검토, 사용자 정책 결정, 반영 후 Codex 재리뷰

---

## 2026-10-01 · 28a5c68 Codex 촬영 계획 상담 기록

**요청**
- Claude의 작업·커밋·push가 끝난 뒤, 오늘 Codex와 논의한 내용을 기존 작업과 섞이지 않게 문서로 기록하고 커밋·push

**변경**
- `conversations/2026-10-01-codex-planning.md`: 장비별 질문, 로테이터 없는 구도 변경, 촬영 시간·종료 정책, 예상 화각에 필요한 광학·센서 정보, API 전달 구조와 Git 상담 요약
- 사용자 의도·이해 확인과 Codex의 미확정 제안을 구분. 같은 날 Claude가 구현한 AA 경통 목록과의 관계를 명시
- 코드·DESIGN.md·기존 Claude 대화 기록은 변경하지 않음

**확인**
- 작업 전 로컬 main과 GitHub main의 `684f431` 일치 및 깨끗한 작업 트리 확인
- 문서 내용과 변경 범위를 검토. 문서만 변경하여 앱 빌드·실장비 검증은 수행하지 않음

**남은 것**: 제안의 채택 범위와 구체적 정책은 후속 논의에서 결정, 실제 구현 상태는 구현 작업 시 확인

---

## 2026-10-01 · 684f431 기준 화면 1920×1080, 관측지·경통, 전환 중 조작 막기, 앱 아이콘

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
