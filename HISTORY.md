# 변경 기록

커밋마다 한 항목. **최신이 위.** 리뷰(GPT Codex 등)가 "무엇을 요청했고 그래서 무엇을 바꿨는지"를 빠르게 보기 위한 요약이다.
세부는 커밋 diff, 화면 규칙은 [DESIGN.md](DESIGN.md), 대화 맥락은 [conversations/](conversations/)에 있다.

항목 형식: 날짜 · 커밋 제목 / **요청** / **변경** / **확인** / **남은 것**(있으면). 짧게.
작성자 표시: 제목에 `[Claude]` 또는 `[Codex]`. 리뷰(REVIEW_CODEX.md)를 반영한 커밋은 반영한 리뷰 ID와 반영하지 않은 이유를 적는다.

---

## 2026-10-09 · [Claude] 사전 점검 마무리: 저장 폴더·솔버·시뮬레이터·관측지, 드라이버 없음·포트 안내·N.I.N.A. 여러 개, 포커서 일련번호, 캘리브레이션 버튼 옆 "가이딩 없이 진행"

**요청**: Codex 점검 항목과 사전 점검 설계 항목을 다 구현한 뒤 커밋(중간에 묻지 않기). 남쪽이 막힌 곳에서 캘리브레이션이 헛되이 움직이는 문제(G07)는 따로 묻지 말고 이미 있는 "캘리브레이션 시작" 버튼을 강화하자는 사용자 제안

**변경**: `DevicePrecheck.Storage`(저장 폴더 있음·쓰기·남은 공간 2GB 미만 막음·20GB 미만 경고·동기화 폴더 경고 — I01~I03), `Solver`(ASTAP 실행 파일과 별 목록 *.1476·*.290 — H01), `Simulator`(실장비 모드에서 시뮬레이터면 경고 — A06). `EquipmentConnector`: 망원경 칸에서 솔버, 연결된 장비마다 시뮬레이터, 카메라 칸에서 저장 폴더. docs/PRECHECK_DESIGN.md 진행 상황 표(C03은 연결 직후 알 수 없음 — N.I.N.A. 카메라 정보에 화질 없음). `IPrepTask.StartAlternatives`·`PrepContext.StartChoice`·러너 "next:선택" — 끝 버튼 옆에 다음 작업의 다른 시작을 둠. 캘리브레이션 시작 버튼을 "캘리브레이션 시작 · 남쪽 하늘로 이동"으로, 옆에 "가이딩 없이 진행"(움직이지 않고 그날 가이딩 없음). Codex 항목: **A05** `EngineStarter` N.I.N.A. 여러 개 경고(`CheckItem.Warn`), **B02** `IHostDevices.UsbSerialPorts` — COM 포트가 없으면 지금 보이는 USB 직렬 포트 안내, **B04** 포트 점유 알림 규칙, **B05** `WaitListedAsync`가 목록에 끝내 없으면 연결 시도 없이 원인 후보, **D02** `DevicePrecheck.MountSite` 적도의 관측지 비교, **E03** `OasisSdk.SetZero(serial)` 포커서가 여럿이면 드라이버 일련번호로만. C06(ISO 범위)은 남김 — 설계 문서 진행 상황 표

**확인**: PostConnectCheckTests 8개·포트 안내·가이딩 없이 진행 시 이동 0회 시험, 서버 테스트 177 통과·1 건너뜀. 실장비 장비 연결: 다섯 장비 통과(저장 D:\Photo\N.I.N.A, ASTAP·D50 있음 → 막지 않음)

---

## 2026-10-09 · f5dfb02 [Claude] 카메라 전원을 허브 출력으로 켜기, PHD2가 카메라를 못 찾을 때 정확한 안내

**요청**: 사전 점검 이어서 — 카메라가 허브 출력(DC2 8V)에서 전원을 받으면 아이라가 켜기(J05), PHD2 "선택한 ToupTek 카메라를 찾을 수 없습니다"를 정확히 짚기

**변경**
- `CameraPowerStore`(camera-power.json — 배터리 또는 허브 출력 이름), `GET/POST /api/equipment/camera-power`. 장비 변경의 카메라 드라이버 목록 아래 "카메라 전원: 배터리 / 허브 DC2…" — 출력을 고를 때 "Empire에서 그 출력 전압을 어댑터에 맞췄나요?" 확인(아이라는 전압을 읽거나 바꾸지 못함)
- `EquipmentConnector`: 카메라가 PC에 없고 카메라 전원이 허브 출력이면 그 출력을 켜고(`SetHubOutputAsync` — 목록 순서, 10초까지 다시 읽기, `EnsureHubUsbOnAsync`도 이것으로) 카메라를 30초 기다림. 못 켜거나 안 나타나면 할 일 안내
- `Phd2Check.ConnectEquipmentAsync`(set_connected + PHD2 Alert 받기)·`CameraNotFound`, 가이드 카메라가 꺼져 있으면 PHD2에 연결을 시켜 알림으로 원인 — 기억한 카메라를 못 찾으면 "다른 USB 자리에 꽂으면 PHD2가 다시 골라야 함" 안내, 다른 알림은 그 문장을 함께

**확인**: Phd2ConnectTests 3개, 서버 테스트 167 통과·1 건너뜀, 웹·데스크톱 빌드. 실장비: 카메라 전원 = DC2로 두고 N.I.N.A. 카메라 연결 끊기·DC2 끄기(14:56:41 카메라 USB 사라짐) → 장비 연결 실행 → 아이라가 DC2를 켜 14:57:05 카메라 나타남, 14:57:07 연결, 다섯 장비 모두 19초. 확인 못 함: 장비 변경 화면의 카메라 전원 고르기를 눈으로(시험은 API로 설정), PHD2 카메라 못 찾음 경로(사용자가 이미 다시 골라 재현 불가 — 가짜 시험만)

---

## 2026-10-09 · bd61eaa [Claude] 장비 연결 직전 점검 (사전 점검 1차)

**요청**: N.I.N.A.가 오류를 내기 전에 아이라가 먼저 확인해 막거나 준비시키기 — Codex 검증 목록과 docs/PRECHECK_DESIGN.md를 실장비가 연결된 지금 적용

**변경**: `Engine/DevicePrecheck` — 연결 직전에 드라이버(ASCOM 프로필)의 COM 포트가 PC에 있는지(B01), 아는 제조사 USB 장치가 있는지(카메라 후지·캐논·니콘·소니, Oasis 포커서 — C01·E01, 10초까지 기다림), Empire가 켜진 뒤 USB가 다시 꽂혔으면 Empire를 닫아 N.I.N.A. 연결이 새로 띄우게(J01·J02). 모르는 장비·확인 불가는 막지 않음. `EquipmentConnector`가 실패면 N.I.N.A.를 부르지 않고 할 일을 알림, 허브 연결 뒤 USB 출력을 켜고 다시 읽어 확인(`EnsureHubUsbOnAsync` — index는 목록 순서). `IHostDevices`(시험용 가짜). 출력을 바꾼 뒤 다시 읽기는 10초까지 1초마다(N.I.N.A.가 출력 값을 몇 초 늦게 갱신 — 켠 뒤 2초에는 아직 0). PHD2 연결 실패 알림은 connect 종류로(장비 연결 화면에서 겹치지 않게)·원인 문장 보강. System.Management 패키지

**확인**: DevicePrecheckTests 7개, 서버 테스트 164 통과·1 건너뜀, 데스크톱 빌드. 실장비(14:30, 시험 서버로 장비 연결): 허브·적도의·포커서 정상, 꺼진 카메라는 연결 전에 막혀 N.I.N.A. 오류 없음(로그 확인). 가이더는 PHD2가 USB 재연결로 기억한 카메라를 못 찾음("선택한 ToupTek 카메라를 찾을 수 없습니다") — 사용자가 다시 고른 뒤, DC2(8V)로 카메라를 켜고 다시 실행: 다섯 장비 모두 8초에 연결, 새 N.I.N.A. 오류 없음. 확인 못 함: COM 포트 없음·Empire 재시작 경로(실물 상황을 일부러 만들지 않음, 가짜 시험만)

**남은 것**: PHD2 Alert로 원인 짚기, 출력 전압–장비 범위 점검(J06+), 카메라 화질(C03), 점검 단계 항목(I·H)

---

## 2026-10-09 · 29d1541 [Codex] 오류 검증 목록의 읽기 조회·가짜 응답 검증

**요청**: Codex가 검증 가능한 부분을 실행하고 문서에 표시해 Claude와 중복 작업을 줄이기.

**변경**: `docs/codex/AIRA_ERROR_VERIFICATION.md` 120개 행에 검증 상태 열과 7절 증거/후속 항목 추가. 47개 행에 확인 범위 표시, 73개 미실시. API 응답 스키마·알림 폐기/중복 갱신 문제 입력과 정상 처리 범위를 구분.

**확인**: AA_REAL=0·AA_SIM=0 서버 테스트 155 통과·1 건너뜀(실장비/프로그램 시뮬레이터 opt-in 14개는 조기 반환). 실행 중 NINA Advanced API 2.2.15.2 읽기 조회: 장비 5종 미연결. 실제 로그 27개 재생, 별도 가짜 HTTP/로그/센서 테스트. 장비 연결·이동·촬영·설정 변경 및 앱 코드 수정 없음.

**남은 것**: 문서 7절의 수정 후보·UI 확인 및 항목별 남은 실기 조건. 기존 미커밋 변경 유지, 커밋·푸시하지 않음.

---

## 2026-10-09 · 29d1541 [Codex] AIRA 오류 예방·감지·복구 검증 목록

**요청**: 클로드가 N.I.N.A./Advanced API로 확인할 수 있도록 가능한 오류 사례와 사전 확인·복구 방법을 폭넓게 정리.

**변경**: `docs/codex/AIRA_ERROR_VERIFICATION.md` 추가. 장비 연결·촬영·마무리·알림 전달까지 12영역 120개 검증 시나리오, 읽기/가짜 응답/시뮬레이터/실기 구분, 조회 후보와 핵심 시험 12묶음·결과 기록 양식. 최신 미커밋 알림 처리도 검증 대상에 포함하되 확정 결함 목록과 구분.

**확인**: HEAD a652971과 현재 코드·HISTORY·API 대응표, 공식 Advanced API/PHD2/NINA 문서 대조. 문서 항목 수·ID 중복·diff 공백 검사. 실제 API 호출·장비 제어·앱 실행·코드 수정 없음.

**남은 것**: 항목별 실제 응답/이벤트/로그 및 복구 결과 검증. 원본 토스트 숨김 시 중요 경고 누락 여부 우선 확인. 기존 작업 트리 변경 유지, 커밋·푸시하지 않음.

---

## 2026-10-09 · 29d1541 [Claude] N.I.N.A. 오류를 아이라 말로, N.I.N.A. 알림 창 숨기기

**요청**: N.I.N.A. 오류·경고 알림(영어, "MISSING LABEL")을 보지 않게 아이라가 해석해서 알려 주기 — 2단계(N.I.N.A. 알림 숨기기)까지. 아이라 알림 자리는 N.I.N.A. 알림(오른쪽 아래)과 겹치지 않게

**변경**
- `Engine/NinaNotices.cs`: `NinaLogWatcher`(N.I.N.A. 로그를 1초마다 따라 읽음 — 켤 때는 파일 끝부터, 여러 줄 예외를 한 항목으로, 마지막 항목은 2초 기다림), `NinaLogRules.Interpret`(COM 포트 없음·Empire·적도의·카메라·포커서 연결, 적도의 통신, 솔빙, 가이드 별·PHD2 통신·안정화, 포커서 멈춤, 카메라 타임아웃 → 무슨 일/왜/이렇게, 소음은 버림), `NinaNotices`(1분 안 같은 알림은 횟수만), `NinaToastHider`(아이라 창이 앞일 때 N.I.N.A. "NotificationsWindow"만 숨김). `GET /api/nina/notices?after=`
- Codex 검증 목록(docs/codex/AIRA_ERROR_VERIFICATION.md) 반영: **A04** `NinaApiClient` — 객체가 아니거나 Success=true가 없으면 실패. **L04** "Sequence contains no matching element"는 Connection.cs DeviceConnect에서만 버림, "No switch found for index"는 버리지 않고 전원 허브 알림(아이라는 스위치를 바꾸지 않음 — 이전 주석이 틀림). **L06** 반복 알림은 30초마다 새 번호로 다시 전하고 화면은 같은 제목을 갱신. **L09·L03** N.I.N.A. 알림 창은 바로 숨기되 5초 안에 아이라 알림이 없으면 되돌려 보임(해석 못 한 알림이 사라지지 않게). **L05**(화면별 종류 숨김)는 실촬영에서 놓치는 경우를 본 뒤로 미룸
- docs/PRECHECK_DESIGN.md: 사전 점검 설계(사용자 요청 — N.I.N.A. 오류 전에 확인·막기·준비시키기), 낮 실장비 읽기 조사 기록(장치 식별, Empire 재연결, 출력 바꾸기 index는 목록 순서)
- 웹: 공통 `DiagnosticCard`(무슨 일/왜/이렇게/원본 접기, tone fail·warn) — `NinaLostCard`도 이것으로. `NinaNoticeCard`(2초마다, 화면이 스스로 보이는 종류는 받을 때 버림 — App `NOTICE_HIDE`). DESIGN.md "N.I.N.A. 오류 알림"

**확인**: 실제 N.I.N.A. 로그 전체(10월 1~9일)를 넣어 분류 — 일반 문장으로 빠지는 건 5건뿐. NinaLogRulesTests 8개, 서버 테스트 157 통과·1 건너뜀. 아이라를 켠 채 꺼진 카메라 연결로 N.I.N.A. 오류를 만들어 3초 안에 카드("카메라에 연결하지 못했습니다") 확인(창 캡처). 확인 못 함: N.I.N.A. 알림 창 숨기기 — 시험 중 아이라 창을 앞으로 가져올 수 없어(Windows가 백그라운드 프로그램의 창 전환을 막음) 사용자 확인 필요

---

## 2026-10-09 · a652971 [Claude] 제품 이름을 아이라(AIRA)로

**요청**: 가칭 AA 대신 정식 이름 — 사용자가 "아이라"로 정함(Claude·Codex·Gemini 후보 비교 뒤). 실행 파일·데이터 폴더 이름도 바꾸기

**변경**: product.json — name 아이라, id AIRA(새 키, 실행 파일 이름), fullName Astrophotography Imaging & Rig Assistant, tagline 천체사진 조수, dataFolder AIRA, previousDataFolder AA(새 키). Directory.Build.props가 id·previousDataFolder를 읽고 Astro.Desktop AssemblyName = id. `Product.Id`, `Product.MoveDataFromPreviousName`/`MoveData`(새 폴더가 없을 때만 예전 폴더를 통째로 옮김, 안 되면 복사) — 데스크톱 시작·서버 Build에서. README·CLAUDE.md 이름. 부팅 화면에 영어 풀이(낱말 첫 글자 강조 — AIRA가 되는 것이 보이게, 사용자 요청). `MainWindow` 시작 때 WebView2 디스크 캐시 비우기 — 예전에 받아 둔 index.html(no-cache 전)이 남아 새 빌드 대신 "AA v0.1.58" 화면이 뜸. `BackgroundWindows`: Alt·Windows 키가 눌리면 지키기(항상 위·포커스 되돌리기)를 그만두고 2분 동안 새로 시작하지 않음 — 장비 연결 중 종료 확인 창에서 Alt+Tab으로 고른 창이 다시 끌려 내려가던 것

**확인**: ProductDataTests 3개(옮김·새 폴더 있으면 그대로·예전 없음) 포함 서버 테스트 149 통과·1 건너뜀, 웹·데스크톱 빌드 — AIRA.exe, 창 제목 "아이라 · 천체사진 조수", 페이지 제목 아이라, 캐시 비운 뒤 상태 줄 "아이라 v0.1.59"(창 캡처). 확인 못 함: 사용자 실제 데이터 폴더 옮기기 — Claude 도구로 켠 앱은 Claude 앱의 가상 AppData(Packages\Claude_…\LocalCache)를 봐서 실제 AA 폴더는 건드리지 않았음. 사용자가 직접 AIRA.exe를 켤 때 옮겨짐

---

## 2026-10-09 · 9ace5dc [Claude] 시험 사진에 히스토그램

**요청**: 시험 사진 화면에서 사진 위 오른쪽 세로 가운데에 그 사진의 히스토그램 (N.I.N.A.에 있는 것처럼)

**변경**: N.I.N.A. API는 히스토그램을 주지 않아(통계만 — api_spec 확인) `Fits.Histogram`이 저장된 FITS를 읽어 256칸(약 200만 점 표본, 홀수 간격). `ShotStats.Histogram`, `RealTestShotDevices`가 저장 파일에서 계산, 모의는 `SimHistogram`. `TestShotTask`가 `run.Live("test-photo", url, { histogram, background })`. 웹 `LiveView`의 `Histogram`(배경 위치 강조선, "배경 N% · 포화 N%"). DESIGN.md 시험 사진 항목. 덤: `NightSessionTests.이어서_찍으면…`이 고정 날짜 계획 끝 + 실제 시계라 10-09 02:40 뒤 실패 → 끝 시각을 지금 기준으로

**확인**: 어제 180초 시험 사진 FITS(7704×5184)로 113ms, 마지막 칸 77.6%(완전 포화 — 140초도 같음). 모의 서버(5211)·브라우저로 시험 사진까지 진행해 1920×1200 배치에서 위치 확인. 서버 테스트 146 통과·1 건너뜀, 웹·데스크톱 빌드. 확인 못 함: 실장비에서 새 시험 사진으로(촬영 화면의 사진에는 아직 없음)

---

## 2026-10-09 · 9ee07cb [Claude] 장비 연결 중 종료가 실제로 동작하게 고침

**요청**: 지난 커밋의 실장비 확인을 Claude가 직접 하기

**변경**: `EquipmentConnector`는 요청마다 새로 만들어져(Transient) 종료 요청이 연결 중인 작업을 보지 못했음 → 진행 상태를 싱글턴 `EquipmentRun`으로. `AbortAsync`가 장비마다 차례로 3초씩 지켜보던 것(17초)을 모든 장비를 함께 1초마다 보도록(허브는 다른 장비가 끊긴 뒤) → 4초

**확인**: 시험 서버(5211)로 실장비(N.I.N.A.·WandererBox·Oasis, 적도의·카메라는 전원 꺼짐) — 연결 시작 2~4초 뒤 중단: 허브·포커서·가이더 모두 끊김, 10초 뒤에도 다시 붙지 않음. 포커서 0점 전체 순서: Oasis SDK로 위치를 1234로 바꿔 둔 뒤 `ZeroIfRequestedAsync`(N.I.N.A. 연결 끊기 → 0점 → 다시 연결) → N.I.N.A. 위치 0. 서버 테스트 146 통과·1 건너뜀, 데스크톱 빌드. 남음: 중단 때 N.I.N.A.가 켠 PHD2 프로그램은 열린 채로 둠

---

## 2026-10-09 · 0d72f9e [Claude] 장비 연결 중 종료하면 연결 끊고 닫기, 포커서 0점

**요청**: ① 장비 연결 중에 AA를 끄면 연결을 바로 멈추고 끊긴 것을 확인한 뒤 종료 (실기: AA가 꺼진 뒤 N.I.N.A.가 "스위치 연결됨") ② 포커서를 다시 달면 기어가 맞물리며 노브가 돌아감 → 출발 전 점검의 이슬 방지 열선 타일을 "포커서 0점"으로 바꾸고, AA가 0점을 잡기

**변경**
- `EquipmentConnector.AbortAsync`(연결 취소 → 이번에 시도한 장비를 허브 맨 나중으로 끊기 → 3초 동안 끊긴 채인지, 늦게 붙으면 다시 끊기, 장비마다 최대 30초), `POST /api/equipment/abort`. `StepRail` 종료 확인이 abort 뒤에 닫고, 못 끊은 장비가 있으면 알리고 "그래도 종료". `ConfirmDialog`에 `busy`
- `OasisSdk.SetZero`(드라이버와 함께 설치된 OasisFocuser64.dll — Scan·Open·SetZeroPosition·GetStatus, ASCOM·N.I.N.A.에는 0점 명령이 없음), `RealFocusDevices.ZeroIfRequestedAsync`(N.I.N.A. 포커서 끊기 → 0점 → 다시 연결 → 위치 0 확인), `FocuserZeroRequest`, `POST /api/prepare/focuser-zero`(점검을 마치면). `FocusTask.ZeroAsync` — 잡으면 지난 초점 기억을 버림, 실패면 다시 시도·직접 했음·0점 없이, Oasis가 아니면 직접 하라고 묻기
- `PreflightScreen` 타일 교체, DESIGN.md 점검 항목

**확인**: 서버 테스트 146 통과·1 건너뜀(0점 성공·실패 후 다시 시도·지원 안 됨 3개 추가), lint·웹·데스크톱 빌드. Oasis SDK를 64비트에서 불러 버전(2.0.2)·장치 찾기(1개) 확인 — 실제 0점 잡기는 하지 않음. 확인 못 함: 실장비에서 연결 중 종료, N.I.N.A.가 포커서를 끊은 뒤 SDK로 열리는지·0점 뒤 N.I.N.A. 위치가 0인지

---

## 2026-10-09 · 06332fb [Claude] 극 찾기 실패 안내를 조준부터

**요청**: SharpCap이 위치를 못 찾은 원인이 조준(북극에서 너무 멀리 향함)이었음 — 실패하면 망원경을 북극 쪽으로 더 가깝게 향하게 하라고 보여 주기

**변경**: `RealPolarDevices.SolveStageAsync` 실패 문구(배경 적당·최대 노출·직접 정한 노출·여러 번 바꿈)와 `PolarTask` 기본 실패 문구 맨 앞에 "삼각대 방향과 고도 나사로 북극 쪽으로 더 가깝게" 안내. 너무 밝을 때는 그대로(조준 탓이 아님)

**확인**: 서버 테스트 143 통과·1 건너뜀, 데스크톱 빌드. 실기: 사용자가 조준을 고치자 SharpCap 직접 실행(게인 800·노출 1.2초)에서 별 17개 검출·Solved. AA로 다시 해 보지는 못함

**남은 것**: 극축 정렬에 성공해도 캘리브레이션 전에 항상 물을지(남쪽이 막힌 곳) — 사용자 결정 대기. SharpCap 위치를 집 좌표로·굴절 보정 켜기(사용자)

---

## 2026-10-09 · 45b3148 [Claude] 첫 실장비 실내 한 바퀴에서 찾은 문제 고치기

**요청**: 2026-10-08 집에서 실장비로 처음 끝까지 돌려 본 뒤 나온 수정 목록을 고치기

**변경**
- PHD2·SharpCap: `Phd2Check` — 가이드 카메라가 PHD2 시뮬레이터면(N.I.N.A. 적도의는 실제) 연결 단계에서 막음. `RealPolarDevices.Phd2Words` — PHD2 장비 연결 창이 열려 있어 카메라를 못 놓을 때 할 일을 AA 말로. SharpCap 시작 전 카메라가 Simulator면 바로 안내
- SharpCap 극 찾기: 스크립트 처음 게인을 80% → 넓은 범위면 로그 눈금 25%(G3M662M 136500 → 약 640), 배경 밝기(히스토그램)·게인 보고. `SolveStageAsync` — 밝으면 노출 줄이고 어두우면 늘리고, 적당한데 못 찾으면 멈추고 안내. 사용자가 SharpCap에서 노출을 바꾸면(`UserChangedExposure`) AA는 바꾸지 않음
- SharpCap 중계: 창 캡처 대신 스크립트가 `SaveAsViewed`로 저장한 영상(`SharpCapBridge.LatestView`)을 보냄. 같은 이름이면 SharpCap이 덮어쓰기 확인 창을 띄우고 멈춰서 매번 새 이름 + 두 번 전 파일 삭제
- 캘리브레이션: 극축 정렬을 건너뛰었으면 움직이기 전에 "캘리브레이션 시작 / 가이딩 없이 진행" 묻기(`NoGuideAsync`), 이어서 하기 확인에서 추적 상태를 요구하지 않음
- 초점: 자동초점 측정점이 오기 전에도 "자동초점 중" 상태, 포커서 멈춤 판정 2분
- 리그 끝: 계획 화면으로 갈 때 `POST /api/prepare/rig/rest` — 적도의 홈 + 추적 끔 (캘리브레이션 위치에 머물지 않게)
- 시험 사진은 `imageType: SNAPSHOT` (LIGHT 폴더에 섞이지 않게)
- AI 대화: 도구 실행 전 "지금 하는 일" 상태 이벤트(`PlanAssistant.Doing`) → 계획 화면 입력 점 옆에 표시. 호출마다 보낸 글자 수·첫 응답·전체 시간 로그
- 데스크톱: 상태 줄에 창 내리기(최소화) 버튼, 전체화면을 최대화 대신 모니터 크기에 정확히 맞춤(`FitMonitor` — 오른쪽·아래가 화면 밖으로 밀리던 것)
- 촬영 화면 가운데 카운터: 쓸 사진 / 계획 옆에 "· 제외 N"을 작게 (있으면 경고색, 0이어도 자리 유지)
- 단계 레일: 마무리 작업이 펼쳐지면 마무리 점 아래 선을 잇고 마지막 작업 아래 선을 숨김
- AI 계획의 한 장당 쉬는 시간 `PlanTools.FrameOverheadSeconds` 6초 → 40초 (실측 약 38초 — 예상 장수가 너무 많이 나옴)
- docs/SHOOT_IMPLEMENTATION.md: 극 찾기 노출 규칙, 한 장마다 약 38초 추가(내려받기·저장)

**확인**: 서버 테스트 143 통과·1 건너뜀, lint(경고만, 바꾼 파일 아님)·웹·데스크톱 빌드. SharpCap 테스트 카메라로 새 스크립트 실행 — 보고 30번·노출 명령 반영·quit, 영상 파일 돌려 쓰기(2개만 남음), 영상은 화면 영상만(별 표시 포함). AA 창 0,0–1536×960 = 화면 크기. 확인 못 함: 실제 하늘에서 배경 밝기 기준(40%·2%)이 맞는지, 실장비 SharpCap에서 사용자 노출 변경 감지, 레일 선은 화면으로 직접 보지 못함, 캘리브레이션 질문·홈 복귀는 실장비에서

**남은 것**: 사용자가 N.I.N.A. 사진 저장 폴더를 OneDrive 밖(`D:\Photo\N.I.N.A`)으로 옮김 — 다음 촬영에서 한 장당 추가 시간을 다시 재서 40초를 맞출 것

---

## 2026-10-08 · c473900 [Claude] 이어서 하기 보강: 장비 멈춤 확인·끝낸 작업 다시 확인 후 건너뛰기·플랫/다크 이어 찍기

**요청**: 이어서 하기 빈틈 — ① AA만 꺼졌으면 노출·가이딩·이동이 계속 돌 수 있음 → 이어서 전에 멈춤 확인 ② 마무리 중 꺼지면 플랫·다크를 처음부터 다시 찍음 ③ 레일의 단계·작업 중 끝낸 것은 다시 켠 뒤 건너뛰기, 단 장비가 살아 있는지 확인이 필요한 것은 생략하지 않기

**변경**
- `PrepareStarter.StopForResumeAsync`(노출 중지·자동초점/포커서 멈춤·적도의 이동 멈춤 확인, 못 하면 이유), `ResumeAsync`(장비 준비를 먼저 이어서 — 다 확인되지 않으면 장비 준비로, 대상·촬영이면 대상 묶음, 마무리면 마무리)
- `IPrepTask.CheckResumeAsync`(기본 = 끝 상태 약속), `PrepareRunner.StartResumedAsync`(하던 작업·가이딩을 멈춘 뒤, 끝낸 작업은 결과가 있고 다시 확인이 통과하면 "이어서 · 전에 마침", 아니면 다시), `DoneIds`, `PrepResults.Has`, `PrepareFlow.ResumeRig/Target/WrapAsync`·`DoneIds`
- `WrapProgress`(플랫 장수·노출, 플랫 다크, 다크 장수·묶음) — 한 장마다 기록, 이어서 하면 플랫은 노출 찾기 없이 찍은 장수부터, 다크는 고른 장수·찍은 만큼부터. 마무리를 새로 시작하면 지움
- 기록(`SessionSnapshot`)에 끝낸 작업 목록과 이동·센터링·초점 확인·가이딩·시험 사진·플랫·다크·마무리 진행 결과. `/api/session/resume`이 멈춤 확인 → 되살림 → 다시 확인 순서
- 화면: 이어서 하는 동안 "확인하는 중", 장비를 멈추지 못하면 이유와 "다시 시도"

**확인**: 서버 테스트 143 통과·1 건너뜀 — 장비 준비 이어서(같은 장비면 모두 건너뜀, PHD2가 다시 켜져 보정값이 없으면 캘리브레이션만 다시), 플랫 25장→5장 더, 끝낸 플랫 건너뛰고 다크 15장→5장 더. 모의 서버로 기록→강제 종료→pending·resume 오류 없음. lint 0, 웹·데스크톱 빌드. 확인 못 함: 실장비에서 촬영 중 크래시 후 이어서(가이딩은 항상 다시 시작됨 — 멈춘 뒤 끝 상태가 맞지 않으므로)

---

## 2026-10-08 · 569c9f2 [Claude] AA가 중간에 꺼졌다 다시 켜면 이어서 하기

**요청**: 직접 종료·크래시·컴퓨터 꺼짐으로 AA가 중간에 꺼졌을 때 — 장비 연결 뒤 "이어서 할까요?", 이어서 하면 출발 전 점검을 건너뜀. 컴퓨터가 꺼졌던 경우는 이어서 하지 않고 중단된 촬영이 있었다고만 알림. 며칠 뒤 새 촬영에는 묻지 않기 (기준은 시간 대신 더 나은 것이 있으면 그것으로)

**변경**
- `Session/NightSession.cs`: `NightSessionStore`(session.json — 단계·프로필·확정 계획·계획 칸·촬영 기록·극축·캘리브레이션·초점·가이딩 없음·그날 밤 기억, 컴퓨터 켜진 시각), `NightSessionRecorder`(진행 단계 동안 20초마다, 단계가 바뀔 때 바로). 요약에 닿으면 끝난 밤
- 판정 `Pending`: 이어서 = 같은 밤 + 같은 프로필 + 2시간 안 + 그 사이 컴퓨터가 안 꺼짐(켜진 시각 비교) + 계획 끝 시각 전. 그 밖에 18시간 안의 끝나지 않은 기록은 알리기만. 갈 곳: 대상·촬영 중이었으면 대상(이동부터), 계획·마무리·장비 준비는 그 단계
- `PlanAssistant.RestoreAsync`, `TargetShots.PlanKey` + `ShootSession.Start`가 같은 계획의 줄에 이어서 셈. API `/api/session/phase·pending·resume·dismiss`
- 화면: `session.ts`, `App`이 단계를 알리고 장비 연결 뒤 기록을 물음(`goAfterEquipment`), `ConfirmDialog`에 `single`·`dismissable`(이어서 질문은 Esc로 넘어가지 않음)

**확인**: 서버 테스트 140 통과·1 건너뜀(`NightSessionTests` 6 — 이어서·컴퓨터 꺼짐·2시간/며칠·끝낸 밤·다른 프로필·새로 시작·이어서 세기). 서버를 임시 데이터 폴더로 켜서 단계 알림 → 강제 종료 → 다시 켜 pending(resume)·resume·dismiss 확인. lint 0, 웹·데스크톱 빌드. 확인 못 함: 화면에서 질문 창, 실제 대상·촬영 중 재개(실장비)

---

## 2026-10-08 · ce4c688 [Claude] 실행 시 창 깜빡임·전원 허브 연결 타이밍·× 버튼 확인

**요청**: 첫 실행에서 본 것 — N.I.N.A. 로고·PHD2가 최대화됐다 사라짐(문제처럼 보임) → 켜는 동안 AA를 잠깐 항상 위로, 최소화는 하지 않기. 전원 허브가 연결 안 됨. 창 × 버튼도 진행 중이면 확인. 빌드 경고 CS9124

**변경**
- `BackgroundWindows.Watch`: 최소화 대신 지켜보는 동안 AA 창 "항상 위"(`HoldTop`, 겹치면 마지막에 풂), 새 창이 포커스를 가져가면 AA로. SharpCap은 그대로 뒤로만
- `EquipmentConnector.WaitListedAsync`: 연결 전에 N.I.N.A. 장비 목록에 그 장비가 나타날 때까지(최대 30초) — 실기: N.I.N.A. 켜고 9초 뒤 연결이 "Sequence contains no matching element"로 실패, 목록은 11초쯤 완성
- `MainWindow.OnClosing`: 화면이 확인하기 전에는 "confirm-close"를 보내고 기다림, `host.ts` `onCloseRequest`, `StepRail`이 진행 중이면 종료 확인 창(요약 화면은 바로 닫음)
- `PrepContext.HasGuider`가 생성자 매개변수 대신 `Results` 속성 사용(CS9124)

**확인**: 서버·데스크톱 빌드 경고 0·오류 0, lint 0. 확인 못 함: 창이 실제로 AA 뒤에서 열리는지, × 확인 창, 전원 허브 재실행 연결(다음 실행 때)

---

## 2026-10-08 · 87284dc [Claude] 작업 실패 시 건너뛰기·제외 사진 연속 멈춤·핫픽셀 별 다시 고르기 (제미나이 의견 반영)

**요청**: 제미나이에게 물은 결정 3가지 — Claude가 항목별로 조정한 안(지난밤 보정값 안 씀, 노출 자동 변경 안 함, 재개 기준은 확인 사진, 30분 뒤 홈·파킹 안 함, find_star 대신 기존 별 다시 고르기)대로 사용자 승인

**변경**
- 결과 기록 `Skipped`(극축·캘리브레이션·센터링·가이딩·시험 사진), `SlewResult.AcceptedHere`, `GuiderSkipped` + `PrepContext.HasGuider`가 그것을 봄(캘리브레이션을 다시 하면 지움)
- `PolarTask.SkipAsync`(넘겨받기·극 찾기 실패 시, 카메라 돌려준 뒤), `CalibrationTask` "가이딩 없이 진행", `SlewTask` "지금 위치를 목표로" → `CenterTask`도 건너뜀(확정 질문은 남김), `CenterTask` "센터링 건너뛰기", `FocusTask` "지금 초점으로 진행"(포커서 오류 포함), `GuidingTask.SkipAsync`(노출 60초 넘으면 줄일지), `TestShotTask` "시험 사진 건너뛰기". 건너뛴 작업은 끝 상태 검사에서 멈춤만 봄
- `ShootSession`: 제외 사진 5장 연속 → 멈춤("frames"), 3분마다 한 장 확인·쓸 사진이면 재개, 30분이면 끝("frames"). `GuideLoss.HotPixel`(최근 "HFD가 낮음" 3번 이상·잃은 것의 절반 이상) → 바로 `ReselectStarAsync`, 10분에 두 번째면 가이드 노출 +1
- 화면: 촬영 멈춤 안내 frames·hotpixel, 끝 이유 frames, 요약 멈춤 이름·다크 라이브러리 팁, 모의 실패 shoot.hotpixel
- 모의 극축 정렬 장비의 멈춤이 실제처럼 SharpCap 닫기·카메라 돌려주기

**확인**: 서버 테스트 134 통과·1 건너뜀(두 번) — 건너뛰기 4개, 핫픽셀 1개, 제외 사진 멈춤·재개 1개, 결과 기록 모양 갱신. lint 오류 0, 웹·데스크톱 빌드. 확인 못 함: 화면에서 눌러 보기, 실장비

**남은 것**: 다크플랫 못 옮김 요약 표시 (DESIGN.md 촬영 준비 절에 건너뛰기 규칙 추가함)

---

## 2026-10-08 · 490cd41 [Claude] Codex 리뷰 18절 반영 (CX-NIGHT-01~06)

**요청**: Codex 18절 6건 모두 동의 — 판단을 말한 뒤 사용자 승인으로 적용 (02는 움직이는 동안 최대 4분 기다리기를 더하고, 05의 다크플랫 못 옮김 요약 표시는 다음으로)

**변경**
- 01·02: `NinaRig.FlipAsync` → `FlipResult(Flipped, Still)` — pierEast로 바뀌어야 반전(pierUnknown은 다시 조회), 이미 pierEast여도 멈춤 확인, 60초에 방향이 그대로여도 움직이는 중이면 최대 4분, 실패하면 `StopAfterFailAsync`(멈춤 명령·확인). `FlipOutcome.MountStill`, `ShootSession` `_mountStill`·`ShootView.MountStopped`, `BlocksNext`·`RecheckStopAsync`가 적도의 정지도, `IShootDevices.ConfirmMountStillAsync`. 화면 문구(적도의 정지 확인)
- 03: `ShootSession.EndReason`(중단·완료·고도·새벽) — 다음 사진이 필요할 때만 디더링, `WaitUnstableAsync`가 끝 조건·반전 시각이면 그만둠, 안정 관찰을 `_wake`(중단·답이 취소)로
- 04: `AscomWeather.ReadAsync`가 기다리지 않음 — 뒤에서 하나만 읽고 최근 값(5분 안)·null을 바로
- 05: `NinaRig.FindSavedFile` — 이미지 폴더 전체에서 이름 + 저장 시각 ±2분, 후보가 하나일 때만. 못 찾으면 빈 경로(이름만 돌려주지 않음) → 촬영은 `File = null`(옮기지 않고 "직접 옮겨 주세요")
- 06: `RealFocusDevices.CancelAndConfirmAsync`(카메라 쉼·포커서 멈춤 두 번), `AutofocusRun.StopUnconfirmed`·`RefocusOutcome.StopUnconfirmed` → 촬영은 "focus"로 끝냄, 준비 단계 초점은 "장비 상태 다시 확인"
- 시험: NIGHT02·NIGHT03 2개, `FindSavedFileTests` 3개, 모의 실패 shoot.flipmoving

**확인**: 서버 테스트 129 통과·1 건너뜀(두 번). N.I.N.A. 시뮬레이터(AA_SIM=1): 반전 15초 뒤 끝·pierEast, 저장 경로, DARKFLAT 폴더 통과. 처음엔 저장 경로가 실패 — 다크플랫 시험과 같은 초에 찍혀 같은 이름 파일이 두 폴더에 생겨 후보 둘 → null(의도대로), 시험에 1초 간격. N.I.N.A.가 켜지며 자체 오류 창(SimpleSequenceVM.get_Targets NullReference, 3.2 개발판) — AA 명령 전, API는 계속 동작. 확인 못 함: 실제 OnStepX 반전 시간·방향 갱신 시점, 자동초점 취소 실패 경로(코드·테스트로만), 센서 멈춤

**남은 것**: 다크플랫을 못 옮겼을 때 요약에 표시, 실장비 확인

---

## 2026-10-08 · [Codex] 931c64a 실장비 첫 촬영 전 재리뷰

**요청**: `ed4d353..931c64a`를 실제 장비에서 사고·촬영 중단으로 이어지는 경로 중심으로 검토. 코드 수정 없이 리뷰 18절과 이 기록만 작성.

**변경**: `docs/codex/REVIEW_CODEX.md` 18절 추가. CX-NIGHT-01~06(P1 5건·P2 1건): 반전 성공 판정, 반전 실패 후 정지 확인, 디더링 대기의 종료 조건, 환경 COM 조회 정체, 저장 파일 동일성, AF 취소 확인. 16절 수정의 재검토와 나머지 요청 범위·실기 한계도 기록.

**확인**: HEAD `931c64a`. `AA_REAL=0`, `AA_SIM=0` 서버 테스트 123 통과·1 건너뜀. 저장소 밖 가짜 장비/HTTP·임시 파일로 센서 취소 불응, 반전 판정 2개, 파일 검색 2개, 마지막 장 이후 대기 재현. 실제 장비 접근·앱 실행·실제 사진 이동 및 코드 변경 없음.

**남은 것**: 리뷰 지적 수정과 해당 실패 경계 회귀 검증, 실제 장비 프로필·반전·AF·별 있는 하늘에서의 검수. 커밋·푸시하지 않음.

---

## 2026-10-08 · 931c64a [Claude] AA 전체를 시뮬레이터에 붙여 찾은 문제 고치기

**요청**: 사무실에서 AA 전체를 실장비 모드로 시뮬레이터에 붙여 돌려 보고, 중대한 결정이 아닌 고칠 것·답이 분명한 것은 바로 고치기 (사용자 회의 중)

**변경**
- `Engine/Phd2Check`: "On Camera"(PHD2가 실제로 주는 이름)도 ST-4로 인정(`IsOnCamera` 띄어쓰기·하이픈 무시), N.I.N.A.·PHD2 적도의가 둘 다 시뮬레이터면 통과
- `GET /api/equipment/simulated` + 장비 화면 `TempFailButtons`는 모의일 때만
- `SharpCapBridge.LaunchAsync`: 실패하면 AA가 켠 SharpCap을 닫음
- `RealFocusDevices.AutofocusAsync`: 자동초점 진행 이벤트가 3분(`Stall`) 없으면 취소·실패 (`NinaRig.LastEventAtAsync`)
- `ShootSession`: 제외 사진 5장 이어질 때마다 알림(`ExcludedRunAlert`), 모의 실패 shoot.nostars
- 점검 화면 타일 `aria-label`
- 시험: `SimulatorRigTests`에 촬영 세션·마무리, `Phd2CheckTests` 3개, `ShootAndWrapTests` 1개

**확인**: 서버 테스트 122 통과·1 건너뜀(두 번), 시뮬레이터 시험(AA_SIM=1) 촬영 세션·마무리 실행, lint 오류 0, 웹·데스크톱 빌드. 화면은 장비 연결·점검·극축 정렬(실패·중단)까지 브라우저로. 확인 못 함: 극축 정렬 뒤 화면 흐름(사무실에 가이드 카메라 없음), 자동초점·솔빙 성공 경로(시뮬레이터 카메라 별 사진 설정 안 함)

**남은 것**: 결정 필요 — 작업 실패 시 건너뛰기, 제외 사진 연속 시 멈출지, "HFD가 낮음" 별 잃음 판정. 사용자 설정 `rig-overrides.json` 카메라가 시뮬레이터(X-T5로 바꿔야 함). 시험 중 바꾼 것: PHD2 AA-Simulator 프로필 적도의 On-camera로 되돌림, N.I.N.A. AstroAssistant 프로필 스위치는 없음으로 둠(실험용)

---

## 2026-10-08 · be79263 [Claude] 촬영 화면에 이슬 여유·열선 상태

**요청**: 열선은 Empire 자동에 맡기고 AA는 이슬 여유·열선 세기·자동 여부를 보여 주고 이상하면 알리기 (사용자 결정)

**변경**: `NinaRig.DewHeaterAsync`(N.I.N.A. 스위치의 PWM DC3 — 세기 0~1, 설명 "Automatic control"이면 자동), `IShootDevices.DewAsync`·`DewStatus`, `ShootSession.CheckDewAsync`(한 장마다, 여유 2°C 아래인데 열선 꺼짐 · 3°C 아래인데 자동 아님이면 10분에 한 번 알림), `ShootView.Dew`, 화면 `ShootPanels` 계기판 설명 줄 끝에 "이슬 여유 18°C · 열선 0% (자동)"

**확인**: 서버 테스트 117 통과·1 건너뜀(모의 이슬 표시 1개), 실기 `실장비_열선_상태` 통과. 실기에서 Empire 기준이 19°C로 남아 있어 0↔255 반복 → 5°C로 바꾸자 0 유지 — Empire 자동은 렌즈 프로브 − 이슬점으로 판단, 켜고 끄는 방식. lint 오류 0, 웹·데스크톱 빌드. 확인 못 함: 화면을 눈으로(실촬영·모의 모드에서)

**남은 것**: 장비 준비 화면에도 보일지(지금은 촬영만)

---

## 2026-10-08 · 4274927 [Claude] WandererBox 기온·습도·이슬점 읽기 (실기)

**요청**: WandererBox Plus V3 + 온도 프로브 + DHT22를 연결 — 이슬점·열선 확인. 열선은 Empire 자동에 맡기기로(사용자 결정, Empire 이슬점 온도 차이를 15→5°C로 사용자가 바꿈)

**변경**: `Prepare/Real/AscomWeather` 신규 — ASCOM ObservingConditions를 직접 읽어 기온·습도·이슬점(1분 캐시, 실패 시 5분 쉼), `RealShootDevices.GuideRawAsync`의 `DewMarginC` = 기온 − 이슬점(전에는 null). 문서: SHOOT_IMPLEMENTATION·api-coverage

**확인**: COM4 상태 줄 듣기(명령 없음), N.I.N.A. 스위치(유성철 프로필에 설정된 것) 연결해 DC3 읽기 — "Automatic control in progress", 설정 변경 뒤 255→0. 실기 시험 `실장비_기온_습도_이슬점`(AA_REAL=1) 통과(25.3°C·36.4%·이슬점 9.3°C), 서버 테스트 115 통과·1 건너뜀. 확인 못 함: 실제 열선 발열(열선 미장착), 이슬 판단이 촬영 화면에 뜨는지(실촬영 필요)

**남은 것**: 이슬 여유·DC3 세기·자동 여부를 화면에 보여 주고 이상하면 알리기, `DewHeaterBoostAsync`는 자동 모드면 알림만(지금도 false). Empire 자동이 상자에서 도는지(Empire 꺼도 유지되는지) 확인

---

## 2026-10-06 · 9c7dd2a [Claude] 제미나이 최적화 리뷰 반영

**요청**: 제미나이(GitHub 읽기)가 낸 최적화·안정성 제안을 검토해 동의하는 것만 고치고, 반대는 이유를 정리

**변경** (동의)
- `Phd2Client.CallAsync`: 보내기를 `_writeGate`(SemaphoreSlim)로 한 번에 하나 — 촬영 감시·디더링·조회가 겹쳐도 명령 줄이 섞이지 않게
- `MainWindow.OnClosing`: 닫기를 미루고 내부 서버 정지(최대 10초)를 기다린 뒤 닫음, 멈추는 중 다시 눌러도 먼저 닫히지 않음. 제미나이가 말한 "파킹·가이딩 정지"는 서버 정지에 없음 — 백그라운드 작업 정리가 잘리지 않게 하는 것
- `EquipmentScreen` 장비 그래프: `connectLayout`·`editLayout`을 `useMemo`로 (seed 난수라 결과 같음)
- 촬영·센터링·시험 사진 미리보기: `Convert.FromBase64String(GetString())` → `GetBytesFromBase64()`
- `UpdateChecker.FindUtf16Version`: phd2.exe를 1MB씩 읽어 UTF-16 글자를 찾음(전체 파일을 문자열로 만들지 않음, 예전 방식이 놓치던 홀수 자리도 찾음)
- `WindowCapture.Capture`: 찾은 뒤 남은 Process까지 Dispose

**반대·보류**: LST 미리 계산(계산 비용이 삼각함수 몇 번이라 이득 없음, 구조만 복잡), LiveView RealImage(16절 최적화 3에서 이미 처리), Fits·Png ArrayPool(제안도 조건부 — 측정 뒤)

**확인**: 서버 테스트 114 통과·1 건너뜀(새 `Setup/Phd2VersionTests` — 처음·홀수 자리·조각 경계·세 번째 조각·설치된 PHD2), lint 오류 0, 웹·데스크톱 빌드. 확인 못 함: 장비 화면·창 닫기를 눈으로(사용자 전체 확인 때)

---

## 2026-10-06 · d0d7327 [Claude] 시뮬레이터로 미리 확인한 실장비 코드 고치기 (저장 경로·반전·별 잃은 프레임·다크플랫·디더링 안정화)

**요청**: 실장비 없이 N.I.N.A.·PHD2 시뮬레이터로 실장비 연결 전에 확인할 수 있는 것을 확인하고, 찾은 문제 1~5와 6(디더링 안정화 실패: 30초 더 → 그대로 찍고 등급으로 → 연속 3번이면 멈추고 안정되면 자동 재개, 15분 넘으면 묻기 — 제미나이 의견 반영, 사용자 결정)을 고치기

**변경**
- `NinaRig`: `LastSavedAsync`가 전체 경로(`FindSavedFileAsync`·`ImageFolderAsync` — image-history는 이름만 줌), `MountState.Pier`, `FlipAsync`는 SideOfPier가 바뀐 뒤 `WaitStillAsync`(이미 pierEast면 성공)
- `RealWrapDevices.CaptureAsync`: DARKFLAT은 DARK로 찍고 `DARKFLAT` 폴더로 (API는 DARKFLAT을 SNAPSHOT 폴더에 저장)
- `RealShootDevices`: StarLost는 SNR·HFD 평균에서 빼고 `GuideRaw.LostRecent`·`LowHfdRecent`(ErrorCode 4)로 따로, `DitherAsync` 시간 초과면 `WaitSettledAsync`(10초 안정, 최대 30초)
- `ShootSession`: 디더링 실패 연속 3번 → `WaitUnstableAsync`(멈춤 원인 unstable, 1분 안정되면 재개, 15분 → `ShootView.Ask`), `Answer`(shoot·wait), "HFD가 낮음"이 잦으면 알림. `POST /api/shoot/answer`
- 화면: `ShootScreen` 멈춤 안내 unstable·"그대로 찍기 / 더 기다리기", `shoot.ts` `answerShoot`, 모의 실패 shoot.unstable·shoot.unstablelong
- 캘리브레이션 경고(5)는 이미 `RealCalibrationDevices`가 Alert를 모아 보여 줌 — 변경 없음

**확인**: 서버 테스트 104 통과·1 건너뜀(디더링 불안정 자동 재개·질문 2개 추가, 두 번 연속). 실장비 코드를 시뮬레이터에 붙인 `Real/SimulatorRigTests`(AA_SIM=1) 4개 통과 — 저장 경로, DARKFLAT 폴더, 반전(14초 뒤 끝), PHD2 디더링(13초 안정). lint 오류 0, 웹·데스크톱 빌드. 확인 못 함: 별 잃은 프레임 따로 세기는 이번 시뮬레이터 별이 밝아 잃은 프레임이 없었음(코드·단위 확인만), 불안정 질문 화면은 눈으로 안 봄, 자동초점·솔빙(시뮬레이터 카메라 별 사진 설정이 N.I.N.A. 화면에만 있음)

**남은 것**: 실장비 확인(남반구·OnStep의 SideOfPier 규약), 안정화 기준값(1.5픽셀·10초)은 실제 하늘로, AA 전체를 실장비 모드로 시뮬레이터에 붙여 끝까지

---

## 2026-10-07 · d3fb05c [Claude] Codex 리뷰 16절 반영 (CX-APP-R1~R5, 최적화 1~5)

**요청**: Codex 16절(재리뷰·앱 전체 검토) 검토 — R1~R5 모두 동의, 최적화 1~5 반영, 6(AI 입력량)·7(구조 분리)은 측정·실장비 확인 뒤. R4의 예기치 않은 N.I.N.A. 종료는 "촬영 중이면 대상 단계를 이동부터 다시, 마무리 중이면 마무리로" (사용자 동의)

**변경**
- R1: `PackTask` — 홈 실패 뒤 정지(`StopAsync`) 결과를 보고, 정지를 확인하기 전에는 다시 시도가 홈이 아니라 정지 확인부터. 안내 문구도 정지 미확인을 구분
- R2: `ShootSession.MoveExcluded` — 제외 폴더 이동 실패(null·예외)를 기록(`TargetShots.NotMoved`), 알림은 "옮기지 못했어요 (직접 옮겨 주세요)", 등급 F는 유지. 요약에 못 옮긴 장수
- R3: 가이더 없으면 `IShootDevices.MountTrackingAsync`만(PHD2 조회 없음), 실제 `FlipAsync`도 `ctx.HasGuider`일 때만 PHD2 정지·재개
- R4: `NinaWatcher.ExpectExit`·`NinaState.Closed` — 마무리에서 AA가 닫으면 경고 없음. 촬영 중 꺼지면 화면이 `POST /api/shoot/abort-for-restart`(`ShootSession.AbortForRestartAsync` + `PrepareFlow.RecheckTargetAsync`) → 다시 켠 뒤 대상 단계(이동부터), 마무리 중이면 마무리로 돌아감. 실제 가이딩 정지는 N.I.N.A.가 안 되면 PHD2 `stop_capture` 직접
- R5: 실장비 모드에서 "9칸 확대 보기" 숨김 (모의 그림만 있음)
- 최적화: PHD2 메시지 `JsonDocument` 닫기, `NinaWatcher` 대기자 정리, `LiveView.RealImage` 한 번에 하나·늦은 응답 버림, `RealShootDevices` 이벤트 수신을 촬영 끝에 정리(`EndSession`), 안 쓰는 `Term` 컴포넌트 삭제, SiteScreen 함수 이름(`use` → `applyChosen`)으로 lint 오류 해소
- 버전 `product.json` 0.0 → 0.1 (촬영·마무리까지 한 밤 흐름이 갖춰져서. 셋째 자리는 계속 커밋 수)
- 모의 실패 `shoot.movefail`, 시험용 `SimulatedWrapDevices.HomeCalls`·`SimulatedShootDevices.GuideRawCalls`

**확인**: 서버 테스트 102 통과·1 건너뜀(새로 R1·R2·R4, CX07에 무가이드 반전·PHD2 조회 0회, 세 번 연속 통과). lint 오류 0(경고만), 웹·데스크톱 빌드. R4 화면 전환·실제 N.I.N.A. 종료, 실제 PHD2 직접 정지는 실기 미확인

**남은 것**: 최적화 6(AI 입력량 측정)·7(App·촬영 세션 구조) 보류, 실제 사진 9칸 자르기(저장 형식 확인 뒤), 실장비 확인 목록, WandererBox 이슬점·열선, 틸트 점검·정비(냉각 카메라 대비, Hocus Focus 확인)

---

## 2026-10-06 · [Codex] ed4d353 재리뷰·앱 전체 사이클 최적화 검토

**요청**: CX-SHOOT-01~09 수정 재검토와 기본 사용 사이클을 완성한 앱 전체의 최적화·불필요한 요소 확인. 구현 수정 없이 리뷰.

**변경**: `docs/codex/REVIEW_CODEX.md` 15절 재검토 결과 갱신, 16절 추가. 기존 수정 대부분 확인. CX-APP-R1~R5(P1 1건·P2 4건): 홈 정지 실패 후 재이동, 제외 파일 이동 실패 누락, 무가이드 반전의 가이더 의존, NINA 종료/복구 흐름, 실제 9칸 확대의 모의 그림 표시. JSON·SSE 자원, 이미지 요청, 리스너 수명, 미사용 Term, lint, AI 대화 입력량, 상태 구조의 개선 순서 기록.

**확인**: HEAD `ed4d353`, 시작 시 작업 트리 깨끗함. `AA_REAL=0` 테스트 99 통과·1 건너뜀, 웹 빌드 통과. lint는 SiteScreen 로컬 `use` 함수의 Hook 이름 오인 오류로 실패(별도 React 경고 있음). 저장소 밖 임시 프로그램에서 가짜 장비 6개·가짜 HTTP 2개 사례 확인. 앱 실행·실장비 조회/명령·실제 파일 이동·AI 요청 없음. 실제 장시간 성능 측정은 하지 않음.

**남은 것**: R1~R5 처리와 단계별 회귀 검증. 최적화는 작은 자원/미사용 코드 정리부터, 성능 변경은 측정 후 진행. 구현 코드 수정·커밋·푸시 없음. 아래 기존 작성일은 유지.

---

## 2026-10-07 · ed4d353 [Claude] Codex 리뷰 14절 반영 (CX-SHOOT-01~09 + 동시성·표본 중복)

**요청**: Codex 리뷰(REVIEW_CODEX.md 14절) 검토 — 9건 모두 동의, 추가 의견(이전 실행 기다리기·가이딩 조회 하나씩·기준 표본 중복)도 반영

**변경**
- CX-SHOOT-01: `IShootDevices.StopGuidingAsync`는 PHD2 Stopped·Looping일 때만 true(LostLock·조회 실패 아님). `ShootSession` 끝 상태 `GuideStopped`, `RecheckStopAsync`(`POST /api/shoot/recheck-stop`), `BlocksNext` — `PrepareStarter.StartWrapAsync`·`StartTarget`이 막음. 화면: 확인 전 "장비 상태 다시 확인"만, 고른 곳(마무리·다른 대상)으로는 확인 뒤에 이동
- CX-SHOOT-02: `FlipOutcome(Flipped, Centered, Guiding)`, 반전 실패면 `EndAsync("flip")`, 반전은 `MountLock` 안에서. 실제 어댑터는 가이딩 정지 확인 뒤에만 반전
- CX-SHOOT-03: `PackTask` — 홈·추적 끄기 확인 전에는 연결 끊기·프로그램 닫기 없음, 홈 실패면 `StopAsync` 후 "다시 시도 / 직접 확인했어요". `PackResult`에 TrackingOff·UserConfirmed·SafeToPowerOff, 요약의 전원 안내는 SafeToPowerOff일 때만
- CX-SHOOT-04: `RealShootDevices.ExposeAsync` — 촬영 시작 뒤 저장 기록만(10초까지 다시 조회), 없으면 실패
- CX-SHOOT-05: `RecenterSafelyAsync`(가이딩 멈춤 확인 → 잠금 → 센터링 → 가이딩 재개, 실패면 촬영 끝), 바람·구름 경로가 사용
- CX-SHOOT-06: `CheckGuideAsync` — MountStopped·Disconnected는 Guiding보다 먼저. 실제 어댑터는 PHD2를 못 읽어도 적도의 추적은 따로
- CX-SHOOT-07: 가이더 없으면(`HasGuider=false`) 가이드 감시·디더링·정지 확인 건너뜀, 적도의 추적만
- CX-SHOOT-08: `DarkTask` 플랫 다크·다크 연속 3번 실패 → 이유 + 다시 시도 / 지금까지만 쓰기 / 다크 건너뛰기
- CX-SHOOT-09: 노출을 멈추지 못해 저장된 사진은 `ExcludeFrame`(제외 폴더 + F)
- 추가: 새 촬영은 이전 실행이 끝난 뒤 시작, `PollGuideAsync` 한 번에 하나, `GuideRaw.Steps`로 새 걸음만 기준 표본에
- 모의 실패: shoot.stopfail·flipfail·abortfail·reselectfail, wrap.home·tracking·dark (촬영 화면 모의 실패 버튼에도 일부)

**확인**: 서버 테스트 99 통과·1 건너뜀(새로 CX01·02·03·05·06·07·08·09, 세 번 연속 통과). 웹·데스크톱 빌드. CX-SHOOT-04는 실제 어댑터라 단위 테스트 없음(빌드·코드 확인). 실장비 미확인

**남은 것**: 실장비 확인(docs/SHOOT_IMPLEMENTATION.md), WandererBox 이슬점·열선, 노출 설계 A, 다크 "여기까지만"과 촬영 완료가 겹치는 경계 테스트

---

## 2026-10-06 · [Codex] 단계 재구성·촬영·마무리 리뷰 (`a20a1b5..73063b4`)

**요청**: `f52a805`·`73063b4`의 HISTORY와 최신 리뷰 요청 범위를 중심으로 코드 리뷰. 결과를 `docs/codex/REVIEW_CODEX.md`에 새 절로 기록.

**변경**: 리뷰 14절에 CX-SHOOT-01~09 기록(P1 6건, P2 3건). 가이딩 정지·반전·장비 정리 실패의 진행 차단 누락, 저장 파일 동일성 미확인, 가이딩 중 재센터링, 추적 중단 판정 소실, 무가이드 구성, 다크 무한 재시도, 중단 실패 후 저장 사진 누락. 동시성·Ask 취소 검토와 최적화 방향 포함. 구현 코드는 수정하지 않음.

**확인**: HEAD `73063b4`, 시작 시 작업 트리 깨끗함. `AA_REAL=0` 서버 테스트 91 통과·1 건너뜀, 웹 빌드 통과. 저장소 밖 임시 프로그램에서 현재 DLL과 가짜 장비로 6개 핵심 경로 재현. 앱 실행·실장비 조회/명령·실제 사진 이동 없음. 테스트 수는 실기 검증을 뜻하지 않음.

**남은 것**: 구현 담당의 리뷰 처리와 실패/경쟁 조건 회귀 테스트. 리뷰·HISTORY만 기록, 커밋·푸시하지 않음. 아래 구현 항목의 날짜(2026-10-07)는 원문 유지.

---

## 2026-10-07 · 73063b4 [Claude] 촬영 · 마무리 구현 (시안 v10), 가이드 별 잃음 원인별 대응, AA 종료 확인 창

**요청**: 촬영·마무리 흐름 합의 → 시안 v10 → 앱 구현(코덱스 리뷰 없이 먼저, 장비 연결 전제). 사진 등급 A+·A·B·C·F, F는 제외 폴더(지우지 않음), 등급은 왼쪽 아래(지금 등급 + 등급별 누적, 기준 툴팁). 디더링·자오선 반전·초점 다시·구름은 자동. "촬영 중단" → 끝내고 마무리 / 다른 대상으로 변경 / 계속 찍기. 마무리 = 플랫(수동 패널, 30장) → 뚜껑 → 플랫 다크·다크(기본 20, 5장씩 조절, 여기까지만) → 장비 정리(N.I.N.A.·PHD2 닫기) → 오늘 밤 요약. 레일 "앱 끄기" → "AA 종료" + 진행 중이면 확인 창, 요약에서는 숨김. 가이딩 중 별 잃음은 원인(빛·구름·이슬·가이드 초점·바람/케이블·적도의 멈춤·끊김)별 대응. X-T5는 N.I.N.A. gain = ISO(사용자 확인). 답변은 항상 한국어(CLAUDE.md)

**변경**
- 서버 `Shoot/`: `ShootSession`(끝 조건·반전·재초점·디더링·등급·제외 폴더·`NightShootResult` 기록, 찍기 전·찍는 동안 가이드 별 지켜보기 → `HandleLossAsync`·`WaitForStarAsync`, 원인별 멈춘 시간), `ShotGrader`(등급 규칙), `GuideWatch`(원인 가르기 규칙·이슬점), `IShootDevices` + `Simulated/RealShootDevices`(PHD2 이벤트 수신·디더·별 다시 고르기·가이더 다시 연결·다시 가운데). API `/api/shoot/*`, `/api/night/summary·open-folder`
- 서버 마무리 `Prepare/Tasks/Wrap/`: `FlatTask`·`DarkTask`·`PackTask` + `IWrapDevices`(모의·실제), `RunnerSetup.Wrap`, `PrepareFlow.Wrap`, `/api/prepare/wrap/start`. 러너: 마지막 작업이 AutoNext면 끝 버튼 없이 Ready. `NinaRig`: imageType·gain(ISO)·Home·Flip·Connect/Disconnect·BitDepth
- 화면: `ShootScreen`(+`ShootPanels` 계기판·등급·반전 단계, 원인별 멈춤 안내, 모의 실패), 마무리 = `PrepareScreen group="wrap"`(`PrepCenter` 다크 장수 고르기), `SummaryScreen`, `ConfirmDialog`, `StepRail`(`complete`·`hideExit`, "AA 종료"), App 흐름 target → shoot → wrap → summary, 촬영 들어가면 적색 테마
- 시안 `mockups/aa-shoot-wrap-v10.html`(새 파일), v9·v10 레일 세로 가운데 CSS 고침
- 문서: DESIGN.md "촬영"·"마무리" 절, `docs/SHOOT_IMPLEMENTATION.md`(구조·실장비 확인 목록·노출/별 잃음 설계와 PHD2 실기 결과), CLAUDE.md 한국어 규칙

**확인**: 서버 테스트 91 통과·1 건너뜀(새로 — 등급, 끝 조건, F 제외, 반전·재초점·멈춤 자동, 중단, 마무리 전체·다크 장수·여기까지만·건너뛰기·패널 밝음, 별 잃음 원인 가르기·원인별 흐름·적도의 멈춤). 화면은 서버 응답을 흉내 낸 임시 페이지에서 캡처(촬영·중단 질문·반전·재초점·멈춤·끝·다크 장수·요약, 적색 픽셀 검사). PHD2 실기(내장 Simulator + On-camera): GuideStep 필드, 가이딩 중 노출 바꾸기, LostLock·StarLost, 별이 돌아오면 PHD2가 스스로 재개, 가이딩 중 find_star 거절 → 멈춤·루프·고르기·가이딩 순서로 7초 재개. RPC에 게인 명령 없음. 실장비(N.I.N.A.·X-T5·적도의)로는 촬영·마무리 미확인, N.I.N.A.를 켠 앱 전체 흐름도 이 세션에서 직접 못 봄

**남은 것**: 실장비 확인 8가지(docs/SHOOT_IMPLEMENTATION.md), WandererBox 기온·습도(이슬점)·열선 제어, 노출 설계 A(시작 사다리·기억·SharpCap), 별 모양(이심률) 분석, 등급·SNR 기준값 실제 하늘로, AA 장비 확인에서 PHD2가 시뮬레이터 프로필이면 알리기

**리뷰 요청 범위(Codex)**: `src/Astro.Server/Shoot/*`, `Prepare/Tasks/Wrap/*`, `Prepare/Flow/PrepareRunner.cs`(AutoNext 끝·Wrap)·`PrepareFlow.cs`, `Prepare/Real/NinaRig.cs` 추가분, `AppServer.cs`(shoot·night·wrap 경로), web `screens/ShootScreen·SummaryScreen`, `components/ShootPanels·ConfirmDialog·StepRail·PrepCenter`, `App.tsx`. 특히: 촬영 루프의 동시성(찍는 동안 지켜보기·노출 멈춤·중단 요청), 끝날 때 가이딩 정지 확인, 다크 중 "여기까지만" Ask 취소 처리, 원인 판정 규칙의 오판 가능성, 실장비 경로의 위험(적도의 이동·프로그램 닫기·파일 옮기기)

---

## 2026-10-07 · f52a805 [Claude] 단계 재구성(장비 준비 · 대상) — 러너 둘, 초점 확인, 레일 움직임, 시안 v9

**요청**: 단계를 연결 → 점검 → 장비 준비(극축·캘리브레이션·초점) → 대상(계획·이동·센터링·초점 확인·가이딩·시험 사진) → 촬영 → 마무리로 재구성(Codex 의견 반영). v9 시안으로 흐름 확정 — 단계 끝 수동 확인이 곧 다음 단계로, 레일은 부드럽게 전환·초록 선이 한쪽 끝부터 자라게, 작업 간격 75px. 초점 확인은 기온 변화 + 센터링 사진 별 크기 두 근거. 그 뒤 앱 구현. 업데이트 알림은 완성 전까지 설치 프로그램 4개 모두 새 버전이 있는 것으로

**변경**
- 시안 `mockups/aa-prepare-arcs-v9.html`(새 파일): 장비 준비·대상 장면, "장비 준비 끝" 장면 없앰(초점 결과 → "대상 고르기"), 레일을 한 번 만들고 상태만 바꿈(반쪽 선 채움·묶음 접기/펴기), 초점 확인 두 근거
- 서버: `PrepareFlow`(러너 둘 + 그날 밤 결과 + 묶음 사이 멈춤·넘김 `BeforeRun`·`ForeignRedo`·`SuspendAsync`), `RunnerSetup.Rig/Target/Single`(의존표·끝 버튼), `Completed.AutoNext`, `PrepContext` 계획 없이 시작(`HasPlan`, `RefocusRequested`), 새 작업 `FocusCheckTask`, `CenterResult`·`CenterAttempt`에 HFR·별 수, `FocusCheckResult`. 센터링 끝 "이 대상으로 확정 / 다른 대상(flow:replan)", 센터링 실패·시험 사진 초점 문제는 초점 확인에서 다시 맞춤. 가이딩 → 캘리브레이션 다시는 대상을 멈추고 장비 준비로 넘긴 뒤 이어서. API `/api/prepare/{rig|target}/…`. 모의 실패 `focus.temp`·`center.hfr`·`guiding.calibration`
- 화면: 단계 이름(장비 준비·대상), 흐름 점검 → rig → plan → target → 촬영(`App.tsx`), `PrepareScreen`을 묶음 겸용으로, 계획 화면 버튼 "이 대상으로 이동"·레일에 대상 작업, `StepRail` 움직임(v9 방식, 화면이 넘긴 작업 목록은 `stage`가 맞을 때만 — 촬영으로 넘어갈 때 앞 단계 작업이 펼쳐지던 오류 고침)
- 문서: DESIGN.md "단계 재구성" 절, PREPARE_IMPLEMENTATION.md 재구성 계획·진행. appsettings `Updates:Pretend` 4개

**확인**: 서버 테스트 76 통과·1 건너뜀(두 묶음 흐름 13개 새로 — 대상을 바꿔도 장비 준비 결과 유지, 극축 다시 → 대상 전체 다시 확인, 가이딩 → 캘리브레이션 넘김·이어서, 초점 확인 두 근거). 레일은 임시 페이지에서 전환 중간 캡처로 확인. 웹·데스크톱 빌드. 앱 전체 흐름은 사용자가 모의 모드로 실행해 레일 오류를 찾음 → 고침(재확인은 임시 페이지). N.I.N.A.를 켠 상태의 전체 흐름은 이 세션에서 직접 확인 못 함

**남은 것**: 계획 화면 첫 인사에 장비 준비 요약(v9), 계획을 미리 정해 온 경우의 흐름, 계획 화면 구성 개편(사용자 예정), 실장비 센터링 사진 HFR·별 수, 출시 전 `Updates:Pretend` 비우기. DESIGN.md에 레일 움직임·단계 끝 버튼 규칙 문구 추가는 제안만 함

---

## 2026-10-06 · a20a1b5 [Claude] 준비 단계 실장비 구현(P3) — 7작업, 실내 실기 확인

**요청**: 어제 실기로 확인한 장비 동작이 앱에 들어가 있지 않았음 — 실내에서 더 확인할 것을 먼저 시험하고, 그 결과로 준비 단계 1~7작업을 실제 장비로 구현

**실내 실기 시험(구현 전)**: SharpCap 스크립트 → AA 통로(1초마다 단계·조절량, 위치를 못 찾으면 0,0 → 단계로 구분), X-T5 촬영(카메라가 JPEG로 설정돼 전부 0 → 사용자가 RAW로 바꿔 정상, 실패해도 이전 사진이 옴), 추적 켜고 끄기, N.I.N.A. 가이딩 시작·중지(별 없이도 "started"), 이동 중 멈춤(slew/stop). 결과는 docs/api-coverage.md

**변경**
- `Prepare/Real/`: `NinaRig`(이동·멈춤 확인·추적·촬영(이벤트로 새 사진 확인)·통계·동기화·포커서·자동초점·가이딩), `Phd2Client`(상시 JSON-RPC + 이벤트), `SharpCapBridge`(실행·스크립트 보고/명령·닫기), `AscomAxis`(RA MoveAxis), `LiveImages`·`WindowCapture`(PrintWindow, DPI 맞춤)·`Fits`·`Png`, 작업별 `Real{Polar,Calibration,Slew,Focus,Center,Guiding,TestShot}Devices`
- `PrepareSetup.AddPrepare(simulate)` — `Equipment:Simulate`에 따라 모의/실제, `PrepView.Simulated`. 서버 `GET /api/prepare/live/{kind}`, `POST /api/prepare/sharpcap/report`. `NinaApiClient.RequestAsync`(오류 문장 포함)
- `BackgroundWindows.KeepBehind` — SharpCap은 최소화하지 않고 AA 뒤로. 극축 정렬 중간 멈춤이면 SharpCap 닫고 카메라를 PHD2에 돌려줌
- 화면: 실장비면 하늘 화면에 실제 이미지(1초마다 새로, 다 받은 뒤 교체), [임시] 모의 실패는 모의일 때만. 극축 상태 줄에서 별 개수 뺌(SharpCap은 안 줌), 캘리브레이션 SNR 없으면 "별을 골랐습니다", 시험 사진 실패 안내에 RAW 확인
- 테스트 `Real/RealDeviceTests`(AA_REAL=1일 때만 장비를 움직임)

**확인**: 빌드 경고·오류 0, 테스트 66 통과·1 건너뜀(평소엔 실장비 시험이 아무것도 안 함). 사용자 허락 받고 실장비 시험 7개 — ⑦ 촬영·통계·저장, ④ 포커서(처음엔 백래시 중간 멈춤을 실패로 봐서 고침), ③ 이동 0.29°·멈춤 확인, ② 위치 A 0.37°·별 없음(PHD2 멈춤 확인을 기다리게 고침), ⑥ 별 없음 판정, ⑤ 솔빙 실패 판정, ① 앱 화면으로 넘겨주기 → SharpCap → 실패 → 중단하면 원래대로(창 캡처가 왼쪽 위만 확대되던 DPI 문제 고침). 적도의는 홈·추적 끔, 시험 서버 종료

**남은 것**: 맑은 날 별로 확인(docs/PREPARE_IMPLEMENTATION.md P3 목록 — SharpCap 조절 단계·단위, RA 회전 방향, 캘리브레이션·가이딩·자동초점·솔빙 결과), SharpCap "Allow smaller rotation angles" 켜기, 하늘 화면에 SharpCap 영상 부분만, 시험 사진의 별 모양·포화 분석

---

## 2026-10-06 · bd817f9 [Claude] Codex 코드 리뷰 반영(CX-PREP-CODE-01~06), 새 버전 확인 앞당김·동시 요청

**요청**: Codex 리뷰(REVIEW_CODEX.md 12절) 검토 — 6개 모두 동의, 최적화 제안 중 업데이트 동시 요청·방향 값도 Claude가 반영. 확인 중 프로필을 눌러 새 버전 소식을 놓치지 않게

**변경**
- CODE-01: `IPrepTask.KeepsRunning`(가이딩 true). 러너 `StopLingeringAsync` — 다시 하기(버튼·작업 요청 모두)로 무효가 되는 작업 중 계속 도는 것을 먼저 멈추고 확인, 그 작업·뒤 작업은 다시 확인 필요. 확인 못 하면 "장비 상태 다시 확인"
- CODE-02: `SlewTask` 멈춤 — 정지 명령을 이동이 끝나기 전에 바로, 정지 확인 전에는 "장비 상태 다시 확인"만(다시 이동 없음). 러너 예외 뒤 "다시 시도"도 정지 확인을 거침(`RetryAsync`), 정지 미확인 표시는 `ShowStopUnconfirmed` 하나로
- CODE-03: `AbortAsync` — 끝난 작업이어도 중단 상태(다음·촬영 시작 지움, Ready 해제), 계속 도는 가이딩도 멈춤. "다시 시작"(`restart`)은 다시 확인이 필요한 첫 작업부터
- CODE-04: `PolarTask` — 조절량을 못 받았거나 5초 넘게 갱신 없으면 "정렬 완료"를 받지 않고 기다리게(0·좋은 등급 저장 안 함). 측정값 관측 시각은 장비 시각(`ITaskRun.Readout`의 `observedAt`)
- CODE-05: `TestShotTask` — 노출 실패면 내려받기로 가지 않고 "다시 찍기". `DownloadAndAnalyzeAsync(since)`로 이번 노출 뒤 파일만. 모의 실패 `test.expose` 추가
- CODE-06: `IPrepMemory.LastPolarAlignedAt` — 극축 정렬을 마치면 기록, 그 뒤에 만든 캘리브레이션만 재사용
- 최적화: 캘리브레이션 측정값에 방향 값(`north`), `LiveView`가 문장 대신 값으로 그림. `UpdateChecker` 네 곳 동시 요청(`Task.WhenAll`), 앱이 켜질 때 바로 확인 시작(`AppServer`) — 프로필 화면에서 결과가 대부분 준비됨
- 테스트 `ReviewFixTests` 8개(Codex 후속 검증 기준), `FakeTask.KeepsRunning`

**확인**: 서버 테스트 60 통과·1 건너뜀(실기용), 15번 연속 통과(새 테스트의 경쟁 조건 하나는 기록을 기다리게 고침). 웹 빌드 통과, 시험 서버로 준비 7작업 처음부터 촬영 단계까지 JS 오류 없음, 시험 서버 종료, 데스크톱 앱 다시 빌드. 실장비 어댑터가 없어 실제 장비 정지 확인은 모의로만

**리뷰 처리**: CODE-01~06 반영. 최적화 2(화면 계산 재사용)·3(배경 분리)은 측정 뒤 판단 — 보류. 가이딩 그래프 데이터 상한·한 샘플 원자적 갱신은 실장비 어댑터(P3) 때. 실기 응답의 "NaN" 문자열·빈 축 속도·RA 시간/도는 P3 어댑터에 반영

---

## 2026-10-06 · [Codex] 99a494e 연결~준비 구현 리뷰

**요청**: 연결부터 준비까지 구현한 `99a494e` 리뷰와 코드 최적화 검토. 텍스트 폴리싱 제외, 구현 수정 없이 리뷰만.

**변경**: `docs/codex/REVIEW_CODEX.md` 12절에 CX-PREP-CODE-01~06과 최적화 의견 기록. 완료 후 중단, 재센터링 전 가이딩 정지, 정지 실패 후 재이동, 극축 측정 누락, 노출 실패 무시, 극축 재정렬 후 보정값 재사용을 지적. 앱·테스트 소스는 수정하지 않음.

**확인**: HEAD `99a494e`, 시작 시 작업 트리 깨끗함. 서버 테스트 53 통과·실기 1 건너뜀, 웹 빌드 통과. 저장소 밖 임시 C# 검증 프로그램에서 기존 빌드 DLL과 모의 장비로 6개 경로 재현. 앱 실행·실장비 명령 없음. 준비 모의 어댑터·P3 미구현·요청된 업데이트 알림 가짜 버전은 결함에서 제외.

**남은 것**: 리뷰 처리 판단과 구현은 후속 작업. 특히 정지·재실행 경계를 실장비 어댑터 연결 전에 보강. 커밋·푸시하지 않음.

**후속 실기 조회**: 사용자 요청으로 실제 NINA Advanced API 2.2.15.2의 version/장비 info/프로필 PHD2 설정과 PHD2 get_current_equipment만 읽음. On-Step·X-T5·Oasis·WandererBox 및 PHD2 내부 G3M662M/On-Step 연결 응답 확인. 실제 NinaApiClient·Phd2Check로도 정상 파싱 확인. 추적·가이딩·노출·포커서 이동은 정지로 보고됨. `"NaN"` 문자열, 비어 있는 축 속도 객체, RA 시간/도 구분을 리뷰 파일에 기록. 연결 변경·설정 변경·장비 동작 명령은 없음.

**후속 최적화 제안**: 사용자 요청으로 Codex의 담당 후보와 검증 방법을 리뷰에 추가. 업데이트 조회 병렬화를 우선하고, 화면 계산 재사용·정적 배경 분리는 측정 후 판단. 중단·재시도 등 제어 로직 수정과 분리하고 정상/실패/캐시의 전후 동작 비교 및 동시 편집 회피를 명시. 제안 기록만 했으며 구현 미착수.

---

## 2026-10-06 · 99a494e [Claude] 가이더 연결 때 PHD2 안의 카메라·적도의까지 확인

**요청**: N.I.N.A.는 "가이더가 연결됨"인데 PHD2는 카메라 연결 실패·적도의 시뮬레이터였던 일 — 가이더를 연결할 때 AA가 PHD2에 직접 물어 확인

**변경**: 새 `Engine/Phd2Check`(PHD2 JSON-RPC `get_current_equipment`, 이벤트 줄은 건너뛰고 같은 id의 답만): 카메라 연결 안 됨 / 적도의 연결 안 됨 / 적도의가 시뮬레이터이거나 N.I.N.A. 적도의와 다름(On-camera·ST4는 통과) → 문제 문장 + 할 일. PHD2에 닿지 못하면 막지 않음. `EquipmentConnector`가 실제 연결에서 가이더 연결 뒤 부름(주소·포트는 N.I.N.A. 프로필 GuiderSettings), 문제면 실패 칸(준필수라 "없이 진행" 가능). `NinaApiClient.GetInfoAsync`. DESIGN.md "장비 연결"에 가이딩 원 규칙

**확인**: 가짜 PHD2 서버 테스트 7개(정상·카메라 꺼짐·적도의 꺼짐·시뮬레이터·다른 적도의·On-camera·닿지 못함) 통과, 실제 PHD2(사용자가 고친 뒤 G3M662M·On-Step)에 읽기만 하는 확인 → 문제 없음(이 테스트는 평소 건너뜀). 전체 53개 통과·1개 건너뜀, 빌드 경고·오류 0. 앱 화면에서 실패 칸으로 보이는 것은 실기 확인 못 함

---

## 2026-10-06 · 99a494e [Claude] 진행 표시: 작업은 제목만, 지금 작업은 글로우

**요청**: 펄스는 선이 아니라 원 뒤에서 빛이 퍼지는 효과(글로우)로. 진행 표시에 작업 결과는 보이지 말고 제목만, 끝난 작업도 제목을 가리고 마우스를 올릴 때만 (처음 "꺼지지 않게"를 끝난 이름을 늘 보이게로 잘못 알아들어 되돌림)

**변경**: `StepRail` — 작업 줄에서 결과(`RailItem.result`)를 없앰(PrepareScreen도 넘기지 않음), 이름은 지금·문제 작업만 늘, 나머지는 올리거나 초점일 때. 지금 작업 점의 `::before` 테두리 펄스 → 점의 box-shadow 글로우. DESIGN.md 진행 표시 규칙

**확인**: 웹 빌드 통과, 시험 서버로 준비 ③ 진행 중 진행 표시 캡처(글로우·올렸을 때 제목), 시험 서버 종료. 데스크톱 앱 다시 빌드. PHD2 장비 경고 건: 사용자가 PHD2 장비 설정을 고친 뒤 PHD2에 물어 카메라 G3M662M·적도의 On-Step 연결, 배율 4.98″/px 확인(AA 코드 변경 없음)

---

## 2026-10-06 · 99a494e [Claude] 새 버전 서랍이 화면을 흔들던 것 고침, 다른 프로그램 창이 AA 위로 뜨지 않게

**요청**: 새 버전 서랍을 열면 화면이 갱신되는 것처럼 보임 — 화면은 그대로, 서랍만 올라오게. N.I.N.A.·PHD2 등이 켜질 때 AA 위로 떠서 Alt+Tab을 해야 함 — 뒤에서(최소화로) 켜지게

**변경**
- `UpdateNotice`: 서랍·버튼으로 초점을 옮길 때 `preventScroll` (화면 영역이 132px 스크롤됐다 돌아오던 것), `ProfileScreen` 무대 `overflow: clip`
- 새 `Engine/BackgroundWindows`: 지켜보는 동안 그 프로세스의 새 창을 활성화 없이 최소화(SW_SHOWMINNOACTIVE, 로딩 끝에 다시 뜨는 창도). `EngineStarter`(N.I.N.A. 켤 때 ~ 응답 + 8초, 시작 옵션도 최소화), `EquipmentConnector`·`RigSetup`(가이더 연결 → PHD2, 전원 허브 → Wanderer Empire, 연결 + 5초). 미리 열린 창·목록 밖 창은 건드리지 않음
- DESIGN.md 3장 "다른 프로그램 창이 AA 위로 뜨지 않게"

**확인**: 시험 서버에서 서랍을 열며 0.03~0.6초 동안 제목 위치·스크롤을 재어 움직임 없음(고치기 전엔 화면 영역이 132px 스크롤). 창 최소화는 임시 테스트로 메모장을 켜서 활성화 없이 최소화되는 것 확인(테스트는 지움). **N.I.N.A.·PHD2·Wanderer Empire로는 확인 못 함**(사용 중인 N.I.N.A.를 껐다 켜지 않음). 빌드 경고·오류 0, 테스트 46개 통과

**남은 것**: 실기에서 N.I.N.A. 시작·가이더·허브 연결 때 창이 내려가는지 확인

---

## 2026-10-06 · 99a494e [Claude] 프로필 화면에 새 버전 알림

**요청**: 설치된 프로그램의 최신 버전 확인 — 확인 단계를 따로 두지 말고(지금 업데이트하라는 느낌), 프로필 화면 구석의 버튼 → 아래에서 올라오는 서랍에 프로그램별 알림과 링크

**변경**
- 서버 `Setup/UpdateChecker.cs`: 설치된 버전(N.I.N.A.·ninaAPI.dll 파일 버전, ASCOM 레지스트리, PHD2는 실행 파일 안 글자) vs 최신 버전(N.I.N.A. 정식판 정보, N.I.N.A. 플러그인 목록의 Advanced API — 설치된 N.I.N.A.에서 쓸 수 있는 것만, GitHub 정식 릴리스 ASCOM·PHD2). 최신 버전은 하루 한 번 `updates.json`, 다시 알리지 않기는 `updates-dismissed.json`. 실패하면 빈 목록. `GET /api/updates`, `POST /api/updates/dismiss`
- [임시] `Updates:Pretend`(appsettings.json, 지금 {"nina": "3.3.0.1001"}): 인터넷에서 받은 최신 대신 이 버전이 나온 것으로 본다 — 이 PC가 모두 최신이라 화면을 보려고(사용자 요청). 확인이 끝나면 비운다
- 웹 `components/UpdateNotice`(왼쪽 아래 "새 버전 n개", 한 번 지나가는 빛, 아래 서랍), `ProfileScreen`에 붙임
- DESIGN.md "프로필 화면"에 새 버전 알림 규칙
- 테스트: 버전 비교 5개(`Setup/UpdateCheckerTests`). 준비 러너 테스트 하나가 가끔 실패하던 것(다시 시작 기록 전에 순서를 검사) — 기록을 기다리게 고침, 15번 연속 통과

**확인**: 이 PC는 네 프로그램 모두 최신이라 실제로는 버튼이 안 보임(N.I.N.A. 3.2.0.9001, Advanced API 2.2.15.2, ASCOM 7.1.3, PHD2 2.6.14 — 인터넷에서 받은 최신과 같음). 테스트 데이터 폴더에 더 새 버전 기록을 넣고 시험 서버로: 설치된 버전 읽기·ASCOM 같은 버전 제외·버튼·서랍·다시 알리지 않기(파일 기록, 개수 줄어듦)·Esc 닫기, JS 오류 없음. 시험 서버 종료, 테스트 46개 통과

**남은 것**: SharpCap 버전 확인. AA가 쓸 수 없는 옛 버전을 설치 확인에서 막는 기준(최소 버전)

---

## 2026-10-06 · 99a494e [Claude] 진행 표시 정리(작업만·폭 줄임), 장비 실제 연결로 전환

**요청**: 진행 표시에는 작업만(세부 과정 빼기), 진행 표시 왼쪽 여백을 1/3로(글자가 넘쳐도 됨). 어제 파워박스로 직접 연결해 본 장비로 앱에서 실제 연결해 진행(메인 카메라 제외)

**변경**
- `PrepareScreen`: 진행 표시 작업 줄에 세부 과정 이름을 넣지 않음(끝난 작업의 결과 한 줄은 그대로)
- `--rail-w` 240→150px, `StepRail` `overflow: visible`, App `.rail`을 화면 영역 위로(z-index) — 긴 작업 이름이 왼쪽으로 넘쳐 보임
- `appsettings.json`: `Equipment:Simulate` false(N.I.N.A. 활성 프로필의 장비를 실제 연결), `Setup:SimulateMissing` false(설치 확인도 실제). 메인 카메라 대신 사용자가 N.I.N.A.에서 N.I.N.A. Simulator Camera를 고름 — 처음 넣었던 `Equipment:Skip`(카메라를 "없이 진행"으로)은 필수 장비라 화면이 통과로 보지 않아 장비 연결에서 멈춰서 뺌
- DESIGN.md 진행 표시 규칙(폭·작업만)

**확인**: 솔루션 빌드 경고·오류 0, 테스트 41개 통과, 웹 빌드 통과. 모의 설정의 시험 서버(5211)로 준비 끝까지 둘러보기 — 진행 표시에 세부 과정 없음, 긴 이름이 왼쪽으로 넘침, JS 오류 없음, 시험 서버 종료. **실제 장비 연결은 돌려 보지 않음**(사용자가 직접 실행). 준비 7작업은 여전히 모의 장비(실장비 어댑터는 P3)

**남은 것**: 실기 연결 결과 확인(N.I.N.A. 활성 프로필 "유성철"로 연결됨, 적도의는 전원을 켜야 COM3이 잡힘). P3 실장비 어댑터

---

## 2026-10-05 · 99a494e [Claude] 촬영 준비 화면 v8 이식(P2)

**요청**: 앱의 준비 단계가 옛 쉐브론 화면 — 첫 실행부터 준비 단계까지 최신으로, 기능까지 붙여서

**변경**
- 새 공통 부품 `PrepGuide`(안내)·`PrepCenter`(중앙 정보: 측정값 → 상태 줄 → 버튼, ① 등급·나사 방향, 주 버튼 가운데 고정·보조 버튼 오른쪽)·`LiveView`(하늘 화면: SharpCap·가이드 카메라·하늘 지도·초점 곡선·솔빙 사진·시험 사진·9칸, 가이딩 그래프) — 모의 장비라 측정값·live.data로 그림을 흉내
- `PrepareScreen` 새로 씀: 하늘 화면 위에 안내·중앙 정보, 사진만 보기·9칸 확대 보기, [임시] 모의 실패는 왼쪽 아래 접힌 줄. 진행 표시에 지금 작업의 세부 과정 이름, 실패 상태면 문제 표시
- 토큰 `--sky`·`--star`·`--sky-muted`·`--sky-line`(세 테마), `RailExtra.sky`로 준비 단계에서 진행 표시를 하늘 위에(App `.body[data-sky]`, 점 속 `--rail-bg`)
- DESIGN.md "촬영 준비"에 하늘 화면 색·사진 보기 규칙, 구현 계획 P2 끝남 표시

**확인**: 웹 빌드 통과. 시험 서버(5211, 모의 장비)에서 헤드리스로 계획 → 준비 7작업을 주 버튼만 눌러 "촬영 시작" → 촬영 단계까지(장면 33장), 넘겨받기 실패 → 다시 시도, 같은 밤 캘리브레이션 재사용 질문, 밝게·촬영 테마, 9칸·사진만 보기 — JS 오류 없음. 시험 서버 종료, 데스크톱 앱 다시 빌드. 데스크톱 창에서 직접 보지는 못함

**남은 것**: 실장비 사진·창 캡처 엔드포인트(`/api/prepare/live/*`)와 어댑터(P3). 진행 표시에서 끝난 작업을 눌러 결과 보기·다시 하기(DESIGN.md "이전 단계로 돌아가기")는 아직 없음(마우스를 올리면 결과 한 줄만)

---

## 2026-10-05 · 99a494e [Claude] 촬영 준비 P1(작업별 서버 구조·모의 장비·테스트), 임시 준비 화면, 진행 표시 오른쪽 세로 열

**요청**: 시안 확정 후 개발 시작. 7작업을 잘 분리해 한 작업을 고쳐도 나머지가 정상 동작하는 구조로 (계획 문서 2.3). 이어서 "계획에서 다음 단계로 안 넘어간다" → 고침, "단계 레일을 우측으로 전부 옮겨 줘"

**변경**
- 옛 `Prepare/PrepareRunner.cs`·`PrepareDevices.cs`(한 클래스에 7단계, 장비 인터페이스 하나) 삭제
- `Prepare/Flow/`: `PrepareRunner`(순서·끝 버튼·다시 하기 의존표·끝 상태 약속 확인·멈춤 확인·실행 번호·오래된 값), `IPrepTask`·`ITaskRun`·`PrepContext`·`MountLock`·`IPrepMemory`, 결과 기록 7개(`PrepResults.cs`), 화면 모델(`PrepView.cs`: 안내·중앙 정보·하늘 화면·진행 표시)
- `Prepare/Tasks/{Polar,Calibration,Slew,Focus,Center,Guiding,TestShot}/`: 작업마다 클래스 + 좁은 장비 인터페이스 + 판정 규칙 + 모의 장비 (DESIGN.md ①~⑦의 흐름·실패 처리)
- `Prepare/Sim/`: 모의 속도·실패 걸기(`SimFaults.Known`), `Prepare/PrepareSetup.cs`(등록 순서 = 작업 순서, `PrepareStarter`)
- `AppServer`: `/api/prepare/start·state·watch·act·abort`, `/sim/fail-next/{작업.과정}`·`/sim/ignore-altitude`·`/sim/speed/{x}`
- `tests/Astro.Server.Tests`(xUnit, 솔루션에 추가), 서버에 `InternalsVisibleTo`, `.gitignore`에 `TestResults/`
- 웹 `prepare.ts`를 새 상태 모양으로, `PrepareScreen.tsx`는 [임시] 화면(옛 쉐브론 틀 + 안내·세부 과정·측정값·상태 줄·버튼 + 작업별 모의 실패 버튼) — 옛 화면이 `view.steps`를 읽다 죽어 계획 → 준비로 못 넘어가던 문제
- 진행 표시를 아래 가로 줄 → 오른쪽 세로 열(모든 단계): `StepRail` 다시 씀(단계 점·지금 단계 아래 작업 점·맨 아래 화면 버튼·앱 끄기, `RailProvider`·`useRailExtra`), `App.tsx`·`App.module.css`(`.body` 가로 배치, `.screen`이 크기 기준 틀), `--rail-w`. DESIGN.md 3장 규칙 갱신
- `AppServer`: `.html`에 `Cache-Control: no-cache` — 웹을 다시 빌드해도 데스크톱 창(WebView2)이 캐시된 옛 index.html로 아래 가로 레일을 보여 주던 문제

**확인**: 테스트 41개 통과 — 작업 단독 13(자기 모의 장비 + 끝 상태 약속), 러너 8(가짜 작업: 순서, 약속 어김이면 멈춤, 의존표, 정지 확인 후 재시작, 정지 확인 실패, 늦은 갱신 버림, 오래된 값 표시), 흐름 5(끝까지·가이더 없음·위치 B·센터링 실패→초점부터·낮은 대상), 규칙·결과 모양 15. 솔루션 빌드 경고 0. 시험 서버(5211)로 `/api/prepare/state`·`/start`(계획 없음 거절)·`/sim/fail-next` 확인 후 종료. 웹 빌드 통과, 헤드리스 둘러보기(설치 확인 → 장비 → 점검 → 계획 → 준비 극축 정렬, 작업 이름 올리기)에서 JS 오류 없음·화면 겹침 없음 확인 후 시험 서버 종료. 데스크톱 창(AA.exe)에서는 확인 못 함. 테스트 중 찾아 고친 버그 2개: 약속 어김 "다시 확인"이 안 되던 것, 정지 재확인 뒤 표시가 안 풀리던 것

**남은 것**: P2 화면(v8 이식: 안내·중앙 정보·하늘 화면, 임시 화면 대체). 실제 장비 어댑터(P3)

---

## 2026-10-05 · [Codex] v7 별자리 제거·버튼과 정보 배치 정리

**요청**: 별자리는 이후로 미루고 v7 그대로 배치 최적화. 주 버튼은 중앙, 재시도 등 보조 버튼은 오른쪽에 작게

**변경**: 별자리 표시·설명·확대 전환 제거. 버튼을 중앙 주 버튼 영역과 오른쪽 보조 버튼 영역으로 분리해 버튼 수에 따라 주 버튼이 밀리지 않게 함. 극축 조절 화살표 묶음 중앙 정렬. 버튼 아래 측정 영역 고정, 가이딩 그래프·이동 지도·9칸 사진이 버튼 영역과 겹치지 않도록 위치·크기 조정

**확인**: 문법·diff 검사, 모의 DOM/타이머로 7개 정상·7개 실패 흐름과 버튼 역할, 다음 작업 이동, 밝은 사진 재촬영 확인. 브라우저 도구가 file:// 접근을 보안 정책으로 차단하여 실제 렌더링 확인은 하지 못함. 다른 경로로 우회하지 않음

**남은 것**: 사용자 화면 확인 후 필요한 시각 보정. 커밋·푸시 없음

---

## 2026-10-05 · [Codex] 준비 7개 작업 공통 시안 v7

**요청**: v6 극축 정렬 화면의 구성과 방향을 이후 작업에도 적용. 모든 작업의 진행 버튼 중앙 배치, 세부 과정 수에 맞는 별자리 선정

**변경**: `mockups/aa-prepare-arcs-v7.html` 추가. 좌상단 작업·세부 과정명과 통합 설명, 중앙 하단 버튼·측정값, 넓은 배경과 좌우 페이드, 우측 레일·현재 작업 왼쪽 별자리를 공통 적용. 캘리브레이션 원형 가이드 화면 제거. 이동·초점·센터링 각 3과정, 시험 사진 4과정으로 시안 표시를 명시. 완료 확대 전환을 후속 작업에도 적용. 모호한 수동 대략 초점 건너뛰기 버튼 제거, 밝은 시험 사진 그대로 진행 시 경고 기록 유지

**별자리**: 극축 정렬=남십자성(4), 캘리브레이션=삼각형자리(3), 대상 이동=겨울 대삼각형(3), 초점=여름 대삼각형(3), 센터링=남쪽삼각형자리의 주요 세 별(3), 가이딩=오리온 허리띠(3), 시험 사진=페가수스 대사각형(4). 정식 별자리와 성군의 차이·단순화한 UI 도식임을 시안 설명에 명시. IAU·NASA 자료 링크 포함

**확인**: JavaScript 문법 검사와 모의 DOM·가상 타이머로 7개 정상/7개 실패 시나리오, 보정값 재사용, 밝은 사진 재촬영, 낮은 대상 대기 확인. 이후 짧은 과정 표시 시간 보완 및 문법 재확인. 브라우저 렌더링·실장비 검증은 미실시

**남은 것**: 사용자 시각 검토와 화면별 배치 조정. 앱 코드 변경·커밋·푸시 없음

---

## 2026-10-05 · [Codex] 극축 정렬 별자리 시안 v6

**요청**: 작업 화면 구성은 유지하고, 준비 하위 단계를 하단 레일에 통합. 극축 정렬 네 과정은 우측 남십자성으로 표시

**변경**: `mockups/aa-prepare-arcs-v6.html` 추가(v5 기반). 왼쪽 원호·상단 과정 표시 제거, 준비–촬영 사이 7개 점과 현재 과정의 점·캡슐 표시, 별 점등·완료 연결선 및 사용자 다음 버튼 후 확대 전환. 1단계 궤도·접근·수평 이동 제거. 2~7단계 본문은 기존 시안 유지

**확인**: JavaScript 문법 검사 통과. 실제 브라우저 렌더링·장비 검증은 하지 않음

**남은 것**: 사용자 화면 확인 후 크기·간격·전환 조정. 커밋·푸시하지 않음

---

## 2026-10-05 · c263e53 [Claude] 준비 구현 계획에 Codex 리뷰 반영·작업 분리 설계, 시안 v8 확정, 화면 영역 이름

**요청**: Codex 리뷰를 검토해 추천대로 반영하고 처리 표를 채우기. 극축 정렬 등급은 사용자 결정 — 실제 모드는 환산 확인 전까지 방향·픽셀 값만. 7단계를 잘 분리해 한 단계를 고쳐도 나머지가 정상 동작하는 구조로

**변경**: `docs/PREPARE_IMPLEMENTATION.md` — 2.1 실행 번호·측정 시각·유효 상태(IMPL-04), 2.2 어댑터 계약·멈춤 확인·적도의 명령 잠금(IMPL-01·02), 2.4 다시 하기 순서·서버 재시작 경계(IMPL-05), 2.5 ① 등급은 환산 확인 후(IMPL-03), 5절 P1 추가 검증 네 경우, **2.3 단계 분리 설계**(사용자 요청 — 한 단계를 고쳐도 다른 단계가 그대로: 단계마다 클래스·좁은 장비 인터페이스·모의·테스트, 단계 사이는 결과 기록만, 단계마다 끝 상태 약속(러너가 장비에서 확인), 러너는 순서·의존표·공통 규칙만, 화면도 단계별 조각). `docs/codex/REVIEW_CODEX.md` 11절 처리 표

**확인**: 다섯 항목 모두 동의·반영(반대·보류 없음). 문서만, 앱 코드·장비 실행 없음

- 시안: Codex와 사용자가 만든 v5~v7(오른쪽 세로 진행 표시 등)을 커밋해 남김. v7 직접 실행 검토 후 사용자 피드백으로 **`mockups/aa-prepare-arcs-v8.html`**: 같은 숫자는 중앙 정보에만, 끝난 작업 제목은 결과 말, 측정값 위·상태 줄·버튼 아래, 보조 버튼 높이 통일, ⑥ 그래프 위치, ① 등급 크게, 진행 표시 아래 "준비 중단·앱 끄기", 촬영 테마 흐린 글씨 밝게, 진행 상태 줄을 왼쪽 위 → 가운데로
- DESIGN.md "촬영 준비": v8 확정, 용어(단계·작업·세부 과정), 화면 영역 이름(안내·중앙 정보·하늘 화면·진행 표시·상태 바), 오른쪽 진행 표시 유지(개발 후 이전 단계에도 반영). 구현 계획 3절 이름 맞춤

**확인(시안)**: v7·v8을 1920×1200 헤드리스로 7작업·세 테마 캡처, 스크립트 오류 없음

**남은 것**: P1(서버 모델 + 모의 장비, 작업 분리) 착수. Codex 재리뷰

---

## 2026-10-05 · [Codex] 준비 구현 계획 리뷰 기록

**요청**: PREPARE_IMPLEMENTATION.md 검토 결과를 합의된 리뷰 파일에 기록하고 작성자 라벨을 포함해 히스토리 남기기

**변경**: `docs/codex/REVIEW_CODEX.md` 10~11절 추가(CX-PREP-IMPL-01~05). 장비 중단 확인, 제어 명령 충돌 방지, 극축 오차 환산 검증, 측정값 유효성·이전 실행 차단, 재실행·서버 재시작 복구와 구현 계획의 여섯 질문 답변을 기록. 복귀 흐름은 DESIGN.md에 이미 있다는 정정 포함

**확인**: 최신 HISTORY·DESIGN·구현 계획·준비 서버 코드 대조 및 ASCOM·Microsoft 공식 문서 확인. 문서 변경만, 장비 실행·앱 테스트 없음

**남은 것**: Claude의 검토·처리 결과 기록, P1 모의 구현에서 취소·지연 결과·갱신 중단·정지 확인 실패 검증. 커밋·푸시하지 않음

---

## 2026-10-05 · 612a5c5 [Claude] 준비 시안 정리: Codex v3 추가, 수식어 붙은 시안 삭제

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
