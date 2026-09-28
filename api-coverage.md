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
| Wanderer Box 전원·히터 | `GET /equipment/switch/info`, `/equipment/switch/set?index=&value=` | ✅🔍 | 포트 번호와 실제 장비 대응 확인 |
| 온도·습도 | `GET /equipment/weather/info` | ✅ | Wanderer의 Observing Conditions 드라이버 |
| 날씨 **예보** (구름, 시잉) | — | ❌ | 외부 예보 서비스 연동 필요 (NINA weather는 현재 센서값) |
| 과거 기록 불러오기 | — | ❌ | 우리 앱의 세션 기록 DB |

## ② 촬영 계획

| 플로우 단계 | API | 판정 | 메모 |
|---|---|---|---|
| 대상 이름 → 좌표 | — | ❌ | 우리 앱이 천체 카탈로그/조회 서비스로 처리 |
| 고도·자오선 통과·박명 계산 | — | ❌ | 우리 앱이 천문 계산 라이브러리로 처리. 지평선은 `/profile/horizon`에서 받음 |
| 달과의 거리 | `GET /astro-util/moon-separation` | ✅ | |
| 후보 goto·프레이밍 비교 | `/equipment/mount/slew?ra=&dec=&center=true&waitForResult=true` → `/equipment/camera/capture?solve=true&stream=true` | ✅ | 이동 전 승인은 우리 앱 UI에서 |
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
| 시험 촬영 | `/equipment/camera/capture?duration=&solve=true&stream=true` | ✅ | 미리보기 + 솔빙 결과 |

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
| **일시정지 + 진행상태 저장** | `/sequence/stop` | ⚠️🔍 | **pause 없음.** stop 후 `/sequence/start`가 이어서 진행하는지 확인 필수. 안 되면 진행 상태를 우리가 기록하고 남은 장수로 다시 로드 |
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
| 냉각/워밍 | `camera/cool`, `camera/warm` | 문제 없음 |
| 슬루·파킹 | `mount/slew`, `mount/park` | ⚠️ **낮에는 경통 캡을 반드시 닫기** (태양 방향 통과 위험). 케이블 걸림 확인하며 옆에서 지켜보기 |
| 시퀀스 로드·시작·중지·재시작 | `sequence/load` → `start` → `stop` → `start` | 다크 1장짜리 테스트 시퀀스로. **stop 후 이어서 진행되는지가 핵심 확인 항목** |

### 3단계: 야간 (별이 필요)
플레이트 솔빙 센터링, 오토포커스, 가이딩, TPPA 극축 정렬, `IMAGE-SAVE` 품질 값.
