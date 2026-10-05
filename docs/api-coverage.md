# 플로우 × Advanced API 대응표

> 작성일: 2026-09-28
> 기준: Advanced API v2.2.15 명세 (github.com/christian-photo/ninaAPI `api_spec.yaml`, `websocket_spec.yaml`)
> 베이스 URL: `http://localhost:1888/v2/api` · 이벤트: `ws://localhost:1888/v2/socket`
> 플로우 원본: `flow.mmd`

표기: ✅ API로 가능 · ⚠️ 가능하지만 조건·우회 필요 · ❌ API에 없음(우리 앱이 직접 구현) · 🔍 실기 확인 필요

---

## 한눈에 보는 결론

- **촬영 루프의 거의 전부가 API로 가능하다.** 장비 연결, 슬루+센터링, 오토포커스, 가이딩, 시퀀스 로드·시작·중지, 파킹까지.
- **프레임마다 HFR·별 개수·배경값이 이벤트로 온다** (`IMAGE-SAVE`). 중간 분석과 세션 기록의 핵심 데이터를 파일을 뒤지지 않고 받을 수 있다.
- **API에 없는 것 = 우리 앱의 몫**: 날씨 *예보*, 대상 이름→좌표 검색, 고도·자오선·박명 계산, 구름 판단 로직, NINA 실행.
- **가장 큰 확인 과제는 "일시정지"다.** 시퀀스에 pause가 없고 start/stop/skip/reset만 있다. stop 후 start가 이어서 진행되는지 반드시 실기로 확인해야 한다.
  - **확인함 (2026-10-01, Advanced API 2.2.15, 시뮬레이터 카메라)**: stop 후 start는 **이어서 진행**한다. 끝난 항목은 건너뛰고, 반복 횟수(LoopCondition의 완료 횟수)도 유지된다. 중단된 노출 1장만 버려지고 다시 찍는다 (5장 중 3장 뒤 중지 → 다시 시작 → 저장된 파일 정확히 5장)

---

## ① 시작 · 연결

| 플로우 단계 | API | 판정 | 메모 |
|---|---|---|---|
| NINA 실행 중인지 | `GET /version`, `/version/nina` | ✅ | 응답 없으면 NINA 꺼짐 또는 API 비활성 |
| NINA 켜기 | — | ❌ | 같은 PC라면 우리 앱이 NINA 실행 파일을 직접 실행 |
| 필요한 플러그인 설치 여부 | `GET /application/plugins` | ✅ | Advanced API, TPPA 설치 확인 |
| 필수 설정 점검 | `GET /profile/show?active=true` | ✅ | 초점거리, 위치, 솔버 경로 등 읽기 |
| 설정 고치기 | `GET /profile/change-value?settingpath=…&newValue=…` | ⚠️🔍 | 어떤 항목까지 바뀌는지 확인 필요 |
| 프로필 전환 | `GET /profile/switch` | ✅ | 장비 구성별 프로필 운용 가능 |
| 장비 목록 | `GET /equipment/{종류}/list-devices` | ✅ | camera, mount, focuser, filterwheel, guider, switch, weather, rotator, flatdevice, safetymonitor, dome |
| 장비 연결 | `GET /equipment/{종류}/connect?to=<id>` | ✅ | 장비별 개별 연결 → "장비별 상태 개별 표시"와 일치 |
| 전체 연결 상태 | `GET /equipment/info` | ✅ | 한 번에 전체 상태 |
| 연결 끊김 감지 | 이벤트 `*-CONNECTED`, `*-DISCONNECTED` | ✅ | 상시 처리의 "연결 끊김 → 이상 감지" |
| Wanderer Box 전원·히터 | `GET /equipment/switch/info`, `/equipment/switch/set?index=&value=` | ✅🔍 | 포트 번호와 실제 장비 대응 확인. 2026-10-05 사용자 장비(Plus V3): index 1 = DC2(적도의), 3 = DC4-6, 4 = USB(적도의·가이드 카메라·포커서가 여기 뒤), 읽기 전용 0 = DC1 Always On, 2 = DC3 PWM(열선). **N.I.N.A. `switch/set`은 "성공" 응답인데 실제로 안 바뀜** (Empire 2.4.0·2.4.3 모두, DC4-6로 확인). Wanderer ASCOM 드라이버(`ASCOM.WandererBox1.Switch`)는 켜고 끄는 출력에 `GetSwitchValue`를 지원하지 않음 → N.I.N.A.의 값 방식 명령이 안 닿는 것으로 보임. **드라이버에 직접 `SetSwitch(i, bool)` / `GetSwitch(i)`는 동작**(반영까지 몇 초, N.I.N.A.와 동시 연결 가능) → AA는 전원 켜고 끄기를 ASCOM 직접으로 |
| 온도·습도 | `GET /equipment/weather/info` | ✅ | Wanderer의 Observing Conditions 드라이버 |
| 날씨 **예보** (구름, 시잉) | — | ❌ | 외부 예보 서비스 연동 필요 (NINA weather는 현재 센서값) |
| 과거 기록 불러오기 | — | ❌ | 우리 앱의 세션 기록 DB |

## ② 촬영 계획

| 플로우 단계 | API | 판정 | 메모 |
|---|---|---|---|
| 대상 이름 → 좌표 | — | ❌ | 우리 앱이 천체 카탈로그/조회 서비스로 처리 |
| 고도·자오선 통과·박명 계산 | — | ❌ | 우리 앱이 천문 계산 라이브러리로 처리. 지평선은 `/profile/horizon`에서 받음 |
| 달과의 거리 | `GET /astro-util/moon-separation` | ✅ | |
| 후보 goto·프레이밍 비교 | `/equipment/mount/slew?ra=&dec=&center=true&waitForResult=true` → `/equipment/camera/capture?solve=true&stream=true` | ✅ | 이동 전 승인은 우리 앱 UI에서. **`ra`는 도(°) 단위** (시간으로 보내면 엉뚱한 곳으로 감, 2026-10-05 실기 확인) |
| 프레이밍 도우미 활용 | `/framing/set-coordinates`, `/framing/slew?slew_option=Center` | ⚠️ | NINA에서 프레이밍 도우미를 한 번 열어 둬야 동작 |
| 계획 → 시퀀스 | `/sequence/list-available` → `/sequence/load?sequenceName=` → `/sequence/set-target` | ✅ | 기획서의 "템플릿 + 대상" 방식과 정확히 맞음 |
| 시퀀스 세부 수정 (노출, 장수, 종료 시각) | `GET /sequence/edit`, `POST /sequence/load` (JSON) | ⚠️🔍 | edit의 경로 문법 확인 필요. 안 되면 템플릿 JSON을 우리가 고쳐서 POST |

## ③ 준비

| 플로우 단계 | API | 판정 | 메모 |
|---|---|---|---|
| 슬루 승인 후 goto | `/equipment/mount/slew?…&center=true` | ✅ | center=true면 플레이트 솔빙으로 중심까지 맞춤 |
| 회전각 맞추기 | `/equipment/mount/slew?…&rotate=true&rotationAngle=` | ✅ | 로테이터 있을 때 |
| 극축 정렬 | WebSocket `/tppa` 채널 (start/stop/pause/resume) | ⚠️ | TPPA 플러그인 2.2.4.1 이상 필요. 결과 수치가 이벤트로 옴 |
| 냉각 | `/equipment/camera/cool?temperature=&minutes=` | ✅ | |
| 오토포커스 | `/equipment/focuser/auto-focus`, `/equipment/focuser/last-af` | ✅ | 이벤트 `AUTOFOCUS-FINISHED`, `ERROR-AF` |
| 가이딩 시작 | `/equipment/guider/connect`, `/equipment/guider/start?calibrate=` | ✅ | |
| 가이딩 품질 | `/equipment/guider/graph` | ✅ | RMS 판정은 우리 코드 |
| 준비상태 평가 | 위 결과 조합 | ❌ | 판정 규칙은 우리 코드 (기획서: 수치 판정은 코드) |
| 시험 촬영 | `/equipment/camera/capture?duration=&solve=true&stream=true` | ✅ | 미리보기 + 솔빙 결과. **2026-10-06 실기(X-T5)**: 시작(`capture?duration=` → "Capture started")과 결과(`capture?getResult=true` — 끝날 때까지 "Capture already in progress")를 따로 요청. 1초 노출에 결과까지 약 25초(RAW 84MB). **실패해도 이전 사진을 돌려줌** → `event-history`의 `API-CAPTURE-FINISHED`(성공)·`CAMERA-DOWNLOAD-TIMEOUT`(실패)로 이번 사진인지 확인. 카메라 화질이 JPEG면 모든 값 0. 통계는 `capture/statistics`, 저장은 `save=true` → `image-history` |
| 이동 멈춤 | `/equipment/mount/slew/stop` | ✅ | 2026-10-06 실기: 감속 후 정지. 정보는 약 2초마다 갱신 → "이동 중 아님" + 좌표 연속 두 번 같음으로 확인. 홈(`mount/home`)으로 가는 동안 Slewing은 false, 끝나면 AtHome true |
| 추적 | `/equipment/mount/tracking?mode=0(항성)/4(멈춤)` | ✅ | 반영까지 약 2초 |
| 가이딩 시작·중지 | `/equipment/guider/start`, `/stop` | ⚠️ | 2026-10-06 실기: 별이 없어도 약 2분 뒤 "Guiding started"(PHD2는 Looping) → **PHD2 상태로 확인**. stop은 가이딩만 멈추고 PHD2는 Looping으로 남음 |
| 포커서 이동 | `/equipment/focuser/move?position=` | ✅ | 2026-10-06 실기: 백래시 보정으로 목표를 지나쳤다 돌아옴(중간에 잠깐 멈춤) |
| 이벤트 기록 | `/event-history` | ✅ | Time·Event — API-CAPTURE-FINISHED, CAMERA-DOWNLOAD-TIMEOUT, ERROR-PLATESOLVE, MOUNT-HOMED … |

## ④ 촬영 실행

| 플로우 단계 | API | 판정 | 메모 |
|---|---|---|---|
| 시퀀스 시작 | `/sequence/start` | ✅ | |
| 진행 상황 | `/sequence/state` | ✅ | |
| 프레임별 품질 (중간 분석) | 이벤트 `IMAGE-SAVE` | ✅ | HFR, HFRStDev, Stars, Mean, Median, 가이드 RMS, 필터, 노출, 온도 포함 |
| 썸네일 | `/image/thumbnail/{index}`, `/image-history` | ✅ | |
| 완료 조건 | `/sequence/state` + 우리 계산 | ⚠️ | 종료 시각 조건은 시퀀스 템플릿에 넣는 게 안전 (AI 없이 끝까지) |
| 디더링 | 시퀀스 트리거 + 이벤트 `GUIDER-DITHER` | ✅ | 템플릿에 포함 |
| 자오선 반전 | 시퀀스 트리거 + 이벤트 `MOUNT-BEFORE-FLIP`, `MOUNT-AFTER-FLIP` | ✅ | 수동 `/equipment/mount/flip`도 있음 |

## ⑥ 이상 복구

| 플로우 단계 | API | 판정 | 메모 |
|---|---|---|---|
| 이상 감지 | 이벤트 `SEQUENCE-ENTITY-FAILED`, `ERROR-PLATESOLVE`, `ERROR-AF`, `CAMERA-DOWNLOAD-TIMEOUT`, `*-DISCONNECTED`, `SAFETY-CHANGED` | ✅ | |
| 구름 감지 | `IMAGE-SAVE`의 Stars·HFR·Mean 추세 | ❌ | 판정 로직은 우리 코드 |
| **일시정지 + 진행상태 저장** | `/sequence/stop` → `/sequence/start` | ✅ | pause는 없지만 stop 후 start가 이어서 진행함 (2026-10-01 확인: 끝난 항목·완료 장수 유지, 중단된 노출 1장만 다시) |
| 원인 진단 자료 | `/application/logs?lineCount=&level=`, `/event-history` | ✅ | AI에는 요약된 특징만 전달 |
| 재연결 | `/equipment/{종류}/connect` | ✅ | |
| 준비상태 재확인 | ③과 동일 | ✅ | |
| 대기 | — | ❌ | 대기·재확인 타이머는 우리 코드 |

## ⑤ 종료

| 플로우 단계 | API | 판정 | 메모 |
|---|---|---|---|
| 시퀀스 종료 | `/sequence/stop` | ✅ | |
| 플랫 | `/flats/auto-exposure`, `/flats/auto-brightness`, `/flats/skyflat`, `/flats/status` | ✅ | 플랫 패널 유무에 따라 선택 |
| 다크 | `/equipment/camera/capture?imageType=dark&save=true` 또는 다크 시퀀스 | ✅ | 커버 닫힘 확인 필요 |
| 파킹 | `/equipment/mount/park` | ✅ | 이벤트 `MOUNT-PARKED` |
| 카메라 온도 올리기 | `/equipment/camera/warm` | ✅ | |
| 전원 끄기 | `/equipment/switch/set` | ✅ | 파킹 확인 후에만 |
| 연결 해제 | `/equipment/{종류}/disconnect` | ✅ | |
| 세션 기록 저장 | `IMAGE-SAVE` 누적 + `/equipment/guider/graph` + `/equipment/focuser/last-af` + 로그 | ✅ | PHD2 GuideLog 파일은 API 밖 → 파일 직접 읽기 |

---

## 실기 점검 순서

### 1단계: 읽기 전용 (안전, 낮에 가능)
장비를 전부 NINA에서 연결해 둔 상태로:
```
node tools/api-check.mjs --listen 120
```
- 조회 엔드포인트만 호출하고 응답을 `api-check-results/`에 저장한다
- 대기하는 120초 동안 NINA에서 장비 하나를 끊었다 다시 연결해 보면 연결 이벤트도 기록된다
- 결과 폴더를 Claude에게 보여주면 프로필 구조, Switch 포트 대응 등을 분석한다

### 2단계: 동작 확인 (주의 필요)
| 확인 항목 | 방법 | 안전 조건 |
|---|---|---|
| 장비 연결/해제 | `connect`, `disconnect` | 문제 없음 |
| Wanderer 포트 on/off | `switch/set` | 어떤 포트가 어떤 장비인지 먼저 확인. 카메라·마운트 전원 포트는 건드리지 않기 |

**WandererBox Plus V3 직렬 규격** (제조사 문서 https://28647633.s21i.faiusr.com/61/ABUIABA9GAAg7ZPcqwYowafj6gU.pdf, 2023-12): 19200bps 8N1. 명령은 숫자 하나 — DC2 켜기 121/끄기 120, DC2 전압 20000+V×10(20132 = 13.2V), DC3 3000+세기(0~255), DC4-6 101/100, USB 111/110 (문서 표가 깨져 짝은 추정 — 쓰기 전 확인). 허브가 상태 줄을 계속 보냄: `ZXWBPlusV3A펌웨어A프로브온도(-127=없음)A습도A기온A입력전류A입력전압AUSBADC2ADC3세기ADC4-6ADC2설정전압×10A`. COM 포트는 한 프로그램만 → N.I.N.A.(Empire)가 잡고 있으면 AA는 직접 못 씀. 쓸모: N.I.N.A. 스위치 명령이 안 먹을 때 대안, 입력 전압(배터리)·습도 감시
| 냉각/워밍 | `camera/cool`, `camera/warm` | 문제 없음 |
| 슬루·파킹 | `mount/slew`, `mount/park` | ⚠️ **낮에는 경통 캡을 반드시 닫기** (태양 방향 통과 위험). 케이블 걸림 확인하며 옆에서 지켜보기 |
| 시퀀스 로드·시작·중지·재시작 | `sequence/load` → `start` → `stop` → `start` | ✅ 시뮬레이터로 확인함 (2026-10-01). JSON으로 만든 시퀀스를 `POST /sequence/load`로 넣을 수 있음. 실장비에서는 노출 도중 중지했을 때 카메라 상태만 다시 볼 것 |

### 3단계: 야간 (별이 필요)
플레이트 솔빙 센터링, 오토포커스, 가이딩, TPPA 극축 정렬, `IMAGE-SAVE` 품질 값.
