# 촬영 준비 1~7단계 앱 구현 계획

작성: 2026-10-05 · [Claude] · **Codex 리뷰용 제안 (아직 착수 전)**
이 문서는 예전 `PREPARE_SCREEN_CONTENT.md`(화면에 보여 줄 내용, 옛 흐름)를 이어 쓴 것이다. 그 내용은 DESIGN.md 3장과 v4 시안으로 옮겨졌다.

| 무엇을 보면 되나 | 문서 |
|---|---|
| 단계마다 **무엇을** 하는지 (사용자 결정 전부) | [DESIGN.md](../DESIGN.md) 3장 "촬영 준비" ①~⑦ |
| 화면이 **어떻게 보이는지** | [mockups/aa-prepare-arcs-v4.html](../mockups/aa-prepare-arcs-v4.html) (7단계 모두, "경우" 버튼으로 실패·예외) |
| 쓰는 API와 **실기로 확인한 것** | [docs/api-coverage.md](api-coverage.md), [conversations/2026-10-02-work-laptop.md](../conversations/2026-10-02-work-laptop.md) 4~7절 |
| **어떻게 만들지** | 이 문서 |

## 0. 원칙 (바꾸지 않을 것)

- **서버가 상태를 갖고 화면은 그리기만** 한다 (지금 구조 유지). 화면을 다시 열어도 이어서.
- **장비 동작은 어댑터 뒤에** 둔다. 준비 흐름은 모의인지 실제인지 모른다 (지금 `IPrepareDevices` 유지·확장).
- **v1은 사용자 장비 기준**: Proxisky UMi17X(OnStepX) · ToupTek G3M662M(가이드) · Fujifilm X-T5(주) · Pleiades68(260mm F3.8) · Oasis Focuser Rose · WandererBox Plus V3 · N.I.N.A. 2.2 · PHD2 2.6.14 · SharpCap 4.1.
- **AI는 판정 고리 밖**: 등급·재시도·멈춤은 규칙이 정하고, AI는 설명 문구만.
- **실패는 AA가 먼저 처리**: 정해진 순서로 다시 시도하거나 대안으로 바꾸고, 끝내 안 될 때만 원인과 사용자가 할 일을 한 줄로.
- **단계 끝은 사용자 버튼**, 끝 버튼이 다음 단계 시작 (② "대상으로 이동"이 이동 승인).

## 1. 지금 코드와 무엇이 달라지나

| 항목 | 지금 (W3 뼈대) | 바뀔 것 |
|---|---|---|
| 단계 | 극축 정렬 → 가이드 연결 → 대상 이동 → 초점 → 센터링 → 가이딩(캘리브레이션 포함) → 시험 사진 | 극축 정렬(가이드 카메라 돌려주기 포함) → **캘리브레이션(독립)** → 대상 이동 → 초점 → 센터링 → 가이딩 → 시험 사진 |
| ① 사용자 일 | SharpCap을 직접 열고, 카메라 연결, 정렬, 닫기 | **나사 조절 + "정렬 완료"만**. 넘겨받기·SharpCap 실행·극 찾기·돌려주기는 AA |
| 단계 안 세부 과정 | 없음 (메시지 한 줄) | **세부 과정(위성)** 목록과 지금 과정, 과정마다 읽기 값 |
| 실시간 화면 | 없음 | SharpCap 창 캡처, 가이드 카메라 사진, 하늘 지도, 초점 곡선, 솔빙 사진, 시험 사진·9칸 |
| 판정 | 숫자만 | 규칙으로 등급("완벽해요/충분해요/…") + 이유 |
| 화면 | 쉐브론 7칸 (`PrepareScreen.tsx`) | v4 원호 + 위성 + 아래 읽기 자리 + 하늘 화면 |

## 2. 서버 구조

### 2.1 상태 모델 (화면에 보내는 것)

`PrepStepView`에 세 가지를 더한다. 화면은 이것만 보고 그린다.

```
PrepStepView
  + SubSteps: [{ id, label, status }]          // 위성. 없으면 빈 배열 (③⑤⑦은 없음)
  + Readout:  { label, big, kind, lines[] }    // 아래 읽기 자리 (예: "극축 오차 6.2′" / "아쉬워요" / 나사별 방향)
  + Live:     { kind, url?, data? }            // 하늘 화면: none | sharpcap | guideImage | skyMap | focusCurve | solvedPhoto | testPhoto
```

- `kind`는 등급 색 (ok / busy / warn / fail). 문구는 서버가 만든다 (화면에 문장 조립 없음).
- 이미지(창 캡처, 가이드 사진, 시험 사진)는 `/api/prepare/live/{id}`에서 받고 `Live.url`에 번호를 붙여 갱신을 알린다.

### 2.2 장비 어댑터

| 어댑터 | 경로 | 하는 일 | 상태 |
|---|---|---|---|
| `NinaApiClient` (확장) | N.I.N.A. Advanced API :1888 | slew(**RA는 도 단위**), home, tracking, focuser move·autofocus, camera capture, 솔빙·센터링(`slew?center=true`), guider connect·start·stop·info, profile 읽기 | 일부 있음 |
| `Phd2Client` (신규) | PHD2 JSON-RPC :4400 | 이벤트 스트림(AppState, Calibration*, GuideStep, StarLost, SettleDone), stop_capture → Stopped 기다림 → set_connected, loop, find_star, guide(recalibrate), get_calibration_data, set_exposure, save_image, get_pixel_scale | 시험 스크립트로 확인 |
| `SharpCapController` (신규) | 프로세스 + 스크립트 + 창 | `SharpCap.exe /camera "<이름>" /runscript aa_polar.py` 실행 → 스크립트가 노출·게인 설정, `SelectTransform("Polar Align")`, 1초마다 `{stage, active, x, y}`를 AA 서버로 POST, 필요할 때 `Advance()` → `CloseMainWindow`로 닫기. 창 캡처는 `PrintWindow(PW_RENDERFULLCONTENT)` + DPI 인식 | 실내 실기 확인 |
| `AscomDirect` (신규) | ASCOM COM (같은 장비에 N.I.N.A.와 동시 연결) | OnStep `MoveAxis`(RA만 회전), Wanderer `SetSwitch`/`GetSwitch`(**N.I.N.A. `switch/set`은 안 닿음**) | 실기 확인 |

- COM 객체는 STA 스레드 하나에서만 다룬다 (서버 전용 작업 큐).
- SharpCap 스크립트가 보내는 HTTP는 localhost만 받고, 실행할 때 넣은 일회용 토큰으로 확인한다.

### 2.3 `IPrepareDevices` 재정의

지금은 단계 단위 메서드다. **세부 과정 단위**로 나눠, 흐름(`PrepareRunner`)이 과정마다 화면을 갱신하고 실패 순서를 정할 수 있게 한다. 모의 구현(`SimulatedPrepareDevices`)도 같은 모양으로 바꾸고, 시안의 시간·실패 경우를 그대로 낸다(`/api/prepare/sim/fail-next/{과정}`).

| 단계 | 과정 (메서드) |
|---|---|
| ① | `SetSiderealTrackingAsync` · `HandOverGuideCameraAsync` · `StartSharpCapAsync` · `FindPoleAsync(onProgress)`(첫 사진 → RA 회전 → 둘째 사진, 노출 조정 포함) · `PolarOffsets`(스트림) · `GiveBackGuideCameraAsync`(SharpCap 닫기 → PHD2 재연결 → N.I.N.A.–PHD2 확인) |
| ② | `SlewAsync(위치 A/B)` · `SelectGuideStarAsync` · `CalibrateAsync(onStep)` · `GetCalibrationAsync` |
| ③ | `SlewAsync(대상)` · `StopMountAsync` |
| ④ | `MoveFocuserAsync` · `AutofocusAsync(onPoint)` · `FocuserLimitsAsync` |
| ⑤ | `CenterAsync(onIteration)` |
| ⑥ | `StartGuidingAsync(onPhase)` · `GuidingStatsAsync` |
| ⑦ | `CaptureAsync(노출, onProgress)` · `AnalyzeAsync`(HFR·별 모양·배경·포화) |

### 2.4 `PrepareRunner` 단계 정의

| 단계 | 세부 과정 | 자동/사용자 | 끝 버튼 | 실패 처리 순서 (DESIGN.md) |
|---|---|---|---|---|
| ① 극축 정렬 | 넘겨받기 · 극 찾기 · 정렬 · 돌려주기 | 정렬만 사용자 | 정렬 완료 → (돌려주기) → 캘리브레이션 시작 | PHD2 해제 3번 → 안내 / 별 부족 → 노출 늘림 → 안내 / 재연결 실패 → 안내 |
| ② 캘리브레이션 | 위치로 이동 · 별 선택 · 캘리브레이션 | 자동 | 대상으로 이동 | 2° 넘게 어긋남·별 없음 → A→B / 별 잃음 → 1번 재시작 / PHD2 경고 → "다시 하는 게 좋아요" / 8분 초과 → 멈춤 |
| ③ 대상 이동 | (없음) | 자동, 낮으면 기다리기 | 초점 맞추기 | 30° 아래 이동 막음 / 멈춤 버튼 / 응답 없음 → 재연결 → 안내 |
| ④ 초점 | (없음, 지점 수 표시) | 자동 | 센터링 시작 | 별 없음 → 범위 안에서 넓게 훑기 → 손 초점 안내 / 포커서 에러 → 멈춤 + 제조사 해제 안내 |
| ⑤ 센터링 | (없음, 회차 표시) | 자동 | 가이딩 시작 | 솔빙 실패 → 노출 늘림 → 전체 하늘 → 초점·구름 안내 / 10번 초과 → 멈춤 |
| ⑥ 가이딩 | 별 선택 · 안정화 · 측정 | 자동 | 시험 사진 찍기 | 별 없음 → 노출 늘림 → 안내 / 오차 커짐 → 원인 / 캘리브레이션 불일치 → ② 제안 |
| ⑦ 시험 사진 | (없음) | 자동 + 사용자 확인 | 촬영 시작 | 내려받기 1번 재시도 → 안내 / 노출 변경은 물어보고 계획에 반영 |

- **다시 하기**: DESIGN.md의 영향 규칙대로 뒤 단계만 "다시 확인 필요"로 (서버가 단계별 의존 조건을 가짐 — CX-PREP-FLOW-03·04).
- **재사용**: 같은 밤 대상만 바꾸면 ② 건너뜀(보정값 유효할 때), PHD2를 다시 켰으면 새로.

### 2.5 판정 규칙 (`PrepRules`, 순수 함수 — 단위 테스트)

| 단계 | 입력 | 등급 |
|---|---|---|
| ① | SharpCap 조절량(px) × 가이드 화면 배율(PHD2 `get_pixel_scale`), 계획의 주 망원경 초점거리·한 장 노출 | 멀어요 / 아쉬워요 / 충분해요 / 완벽해요 (기준은 계획에서 계산, 시안은 5′) |
| ② | 직교 오차, RA·Dec 속도 비, PHD2 경고 | 좋아요 / 다시 하는 게 좋아요 |
| ④ | 곡선 맞춤 정도, 최소 HFR | 좋아요 / 다시 |
| ⑥ | 전체 RMS ÷ 주 카메라 한 픽셀(N.I.N.A. 프로필로 계산, 사용자 장비 2.41″) | 충분해요(≤1) / 괜찮아요, 지켜볼게요(≤1.5) / 아쉬워요 |
| ⑦ | HFR(④ 대비), 이심률, 배경 밝기, 포화 비율, 가이딩 RMS | 좋아요 / 문제 + 할 일 |

### 2.6 저장 (AA 데이터 폴더)

지난 초점 위치와 그때 기온 · 대상별 카메라 방향 · 이번 밤 캘리브레이션 유효 여부 · **단계별 소요 시간 기록**(계획 단계의 "촬영 시작까지 예상 n분"에 씀) · 시험 사진은 같은 밤 폴더의 `시험/`.

## 3. 화면 구조 (web)

- 컴포넌트: `PrepareArc`(원호·레일 조각), `Moons`(위성, 맨 앞 위성이 창이 되어 `Live`를 담음), `Readout`(아래 읽기 자리), `LiveView`(하늘 화면 종류별), `PrepPanel`(제목·설명·상태 줄·버튼 — 공통 버튼·상태 줄 컴포넌트 재사용).
- 색·글꼴·간격은 토큰만, 세 테마(어둡게·밝게·촬영 적색). 촬영 테마에서 밝은 사진은 어둡게 눌러 보여 줌 (따로 정함).
- 자리 확보: 상태가 바뀌어도 다른 요소가 움직이지 않게 (DESIGN.md 1장).
- 애니메이션 타이밍은 마감 때 실제 장비 속도에 맞춰 조정.

## 4. 단계별 확인 상태

✅ 실기로 확인 · 🔍 아직 (맑은 날 또는 장비 연결 필요)

| 단계 | ✅ | 🔍 |
|---|---|---|
| ① | PHD2 해제 0.8초(Stopped 기다려야 함), SharpCap `/camera`·`/runscript`, 스크립트 → HTTP, 창 캡처, SharpCap 닫기 0.7초, PHD2 재연결 3초, 홈·Go Home | RA 회전(극에서 MoveAxis가 홈에서 60°까지만 — 원인 미확인, 지금은 60° + SharpCap "작은 회전 허용"), 나사↔값 짝, 노출 자동, 조절량 단위·배율 |
| ② | goto A·B 오차 0.38°(약 40초), PHD2 → 적도의 펄스 가이드 4방향 정상, N.I.N.A.·PHD2 적도의 동시 연결 | 실제 캘리브레이션 |
| ③ | goto(도 단위), 자동 자오선 반전 동작 | 낮은 대상 대기·알림 |
| ④ | Oasis 이동·온도 프로브(연결할 때만 잡힘), 멈춤 감지 켜짐 | N.I.N.A. 자동초점 API·추천값 계산·넓게 훑기 |
| ⑤ | ASTAP + D50, N.I.N.A. 솔빙 설정 | 실제 솔빙·센터링 |
| ⑥ | 펄스 가이드, 주 카메라 한 픽셀 2.41″ 계산 | 실제 가이딩, PPEC 주기 |
| ⑦ | — | X-T5 촬영·내려받기, 검사 지표 계산(N.I.N.A. 이미지 통계로 되는지) |

## 5. 진행 순서 (제안)

1. **P1 서버 모델 + 모의**: 새 7단계·세부 과정·Readout·Live를 모의 장비로 끝까지. 화면 없이 테스트(상태 전이, 실패 순서, 다시 하기).
2. **P2 화면**: v4 시안을 앱으로 이식, 모의로 처음부터 촬영 시작까지.
3. **P3 실장비 어댑터** (실내에서 되는 것부터): ① 넘겨받기·SharpCap·돌려주기 → `AscomDirect`(스위치·RA 회전) → ② ⑥ PHD2 → ③ ⑤ N.I.N.A. → ④ → ⑦.
4. **P4 맑은 날 실기**: 대화 기록 7절 목록. 메인 경통 없이 가이드 카메라만 올려도 ① ② ⑥은 시험 가능.

각 단계 끝에 HISTORY.md 기록, 화면은 헤드리스 스크린샷으로 확인.

## 6. Codex에게 특히 보고 싶은 점

1. 세부 과정·Readout·Live를 서버 모델에 넣는 방식 (화면이 문장을 만들지 않게 하는 것이 맞는지)
2. ASCOM 직접 연결(COM)을 N.I.N.A.와 함께 쓰는 범위 — 스위치·RA 회전만으로 제한하는 게 맞는지, N.I.N.A. 상태와 어긋날 위험
3. SharpCap 스크립트 → AA HTTP 경로의 안전 (localhost + 일회용 토큰이면 충분한지)
4. 창 캡처(`PrintWindow`) 대신 Windows Graphics Capture가 나은지
5. `IPrepareDevices`를 세부 과정 단위로 나누는 것이 모의·실제 교체와 테스트에 맞는지
6. 다시 하기·재사용 규칙을 "직전 단계 끝"이 아닌 의존 조건으로 관리하는 구현 (CX-PREP-FLOW-03·04)
