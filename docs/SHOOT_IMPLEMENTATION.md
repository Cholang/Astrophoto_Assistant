# 촬영 · 마무리 구현 (2026-10-07)

화면 규칙은 DESIGN.md "촬영"·"마무리", 시안은 `mockups/aa-shoot-wrap-v10.html`. 사용자 요청으로 Codex 리뷰 없이 구현했다.

## 구조

- **촬영** — `src/Astro.Server/Shoot/`
  - `ShootSession`: 촬영 한 번(계획 하나)의 상태·순서. 끝 조건(계획 장수 = 쓸 사진 기준 · 대상 30° 아래 · 계획 끝 시각) → 자오선 반전(시간각 +1.25° = 자오선 5분 뒤, 한 번) → 초점 다시(기온 2°C 또는 최근 좋은 사진 3장 평균 별 크기가 초점 때의 1.3배) → 구름(PHD2가 가이딩 중이 아니면 멈추고 다시 켜 봄) → 한 장 → 등급 → F면 제외 폴더 → 쓸 사진 3장마다 디더링. 끝나면 가이딩 정지 확인. 그날 밤 결과 기록에 `NightShootResult`(대상별 쓸 사진·제외·노출·ISO·등급·폴더)
  - `ShotGrader`: 등급 규칙(순수 함수, 테스트 고정). 기준 = 초점 때 별 크기, 처음 좋은 사진 3장의 별 수·배경(중앙값), 주 카메라 한 픽셀
  - `IShootDevices` — 모의 `SimulatedShootDevices`(한 장 ≈ 4초, 자오선 12분 전에서 시작), 실제 `RealShootDevices`
  - API `/api/shoot/start · state · watch · stop`, `/api/night/summary · open-folder`
- **마무리** — `Prepare/Tasks/Wrap/`: `FlatTask` · `DarkTask` · `PackTask` (준비와 같은 `PrepareRunner`, 묶음 `RunnerSetup.Wrap`, `/api/prepare/wrap/…`). 마지막 작업이 AutoNext면 끝 버튼 없이 Ready → 화면이 요약으로
- **화면**: `ShootScreen`(+ `ShootPanels`: 계기판·등급·반전 단계), 마무리는 `PrepareScreen group="wrap"`(다크 장수 고르기 = `PrepCenter`의 `dark-count`), `SummaryScreen`, `ConfirmDialog`(AA 종료). 진행 표시 `RailExtra.complete`(모든 단계 완료)·`hideExit`
- 테스트: `ShootAndWrapTests` (등급, 계획 장수 끝, F 제외, 반전·초점·구름 자동, 대상 낮음·중단, 마무리 전체·다크 장수·여기까지만·건너뛰기·패널 너무 밝음)

## 실장비에서 확인할 것 (장비가 돌아오면)

1. 촬영: `capture?save=true&imageType=LIGHT&gain=ISO` 한 장씩 — 파일 이름(`image-history` Filename), 통계(HFR·Stars·Mean), 미리보기 이미지. (X-T5는 N.I.N.A. gain이 ISO로 동작 — 사용자 확인, 2026-10-07)
2. 등급 기준값: 실제 사진으로 배수 다듬기(별 흐름 가이딩 2.5픽셀, 구름 별 수 40%, 잡광 배경 2.5배, 초점 1.8배). 별 모양(이심률)은 지금 못 잼 → 사진 분석 코드 필요
3. 제외 폴더로 옮기기: 저장 직후 파일이 잠겨 있지 않은지(N.I.N.A.가 쓰는 중이면 실패 → 그대로 둠)
4. 디더링: PHD2 `dither`(5픽셀, 1.5픽셀 10초 안정) → `SettleDone`
5. 자오선 반전: N.I.N.A. `mount/flip` 응답·소요 시간, 반전 뒤 센터링(준비의 센터링 장비), PHD2 보정값 뒤집기(적도의 방향 정보), UMi17X 자오선 한계와 반전 시각(+5분)
6. 구름: PHD2 상태가 "LostLock"/"Looping"일 때 `guider/start`로 다시 잡는지
7. 플랫: 위쪽 이동(항성시·위도−10°), 카메라 비트 수(X-T5 14비트 — N.I.N.A. 평균이 16비트로 늘려 오는지), 노출 찾기, `imageType=FLAT/DARKFLAT/DARK` 저장 폴더
8. 장비 정리: `equipment/{종류}/disconnect` 순서, N.I.N.A.·PHD2 창 닫기(저장 확인 창이 뜨면 열린 채로 남음 — 요약에 표시)

**시뮬레이터로 확인 (2026-10-06, N.I.N.A. AstroAssistant 프로필 + Simulator Camera·Telescope Simulator for .NET·ASCOM Simulator Focuser, PHD2 "AA-Simulator" 프로필)** — 고칠 것은 ❗
- 저장: `capture?save=true` 결과를 받은 직후 `image-history`에 바로 생기고 파일도 잠겨 있지 않음(바로 옮길 수 있음). 폴더는 N.I.N.A. 이미지 경로 아래 `날짜\종류\`. ❗ `image-history`의 `Filename`은 **파일 이름만**(경로 없음) → `MoveToExcluded`가 늘 실패함. N.I.N.A. 이미지 경로(프로필 `ImageFileSettings.FilePath`)에서 찾아야 함
- ❗ `imageType`은 LIGHT·FLAT·DARK·BIAS만 폴더가 나뉨. DARKFLAT(·"DARK FLAT")은 SNAPSHOT 폴더로 감 → 다크플랫은 DARK(플랫 노출)로 저장하거나 따로 옮겨야 함
- 반전: `mount/flip`은 바로 "Flipping"으로 답하고 적도의는 2~5초 뒤 움직이기 시작, 약 14초에 끝(pierWest → pierEast). 반전이 필요 없는 쪽이면 "Flipping"이라 답하고 아무 일도 없음, 이벤트 기록에도 안 남음. ❗ `NinaRig.FlipAsync`의 `ConfirmStillAsync`는 움직이기 전에 "멈춤"으로 끝남 → SideOfPier가 바뀐 것(또는 Slewing을 본 뒤 멈춤)으로 확인해야 함
- `guider/start`: PHD2가 LostLock이어도 약 6초 만에 "Guiding started" — PHD2가 멈추고 별을 새로 골라 가이딩, 안정 5프레임
- PHD2 `stop_capture`는 LostLock에서도 바로 멈춤(GuidingStopped)
- 디더링: LostLock에서도 받음. `GuidingDithered` → `Settling`… → `SettleDone`. ❗ 별을 잃은 프레임이 하나라도 있으면 안정 시간이 0으로 돌아가 시간 초과(Status 1)가 쉽게 남. 지금 `ShootSession`은 실패해도 그냥 다음 장
- ❗ 시뮬레이터 별이 "HFD가 낮음"(StarLost ErrorCode 4)으로 프레임 절반가량 거부됨(핫픽셀·너무 작은 별에서 나는 오류). 지금 AA는 StarLost의 SNR 0·HFD 0을 평균에 넣음 → 구름·약해짐으로 잘못 보거나 가이드 초점 밀림을 놓칠 수 있음. StarLost는 평균에서 빼고 ErrorCode별로 따로 세야 함. 이런 동안 PHD2 앱 상태는 계속 "LostLock"
- `flip_calibration`: 가이딩 중·멈춤 모두 됨, RA 각도만 180° (DEC 그대로), `CalibrationDataFlipped` 이벤트. ASCOM 적도의면 PHD2가 방향을 알아 스스로 뒤집으므로 AA는 부르지 않는다(지금 코드 그대로)
- 캘리브레이션 끝에 "가이드 단계가 적어 정확도 의문" `Alert`가 옴 — AA는 Alert를 보지 않음
- 연결 해제: 장비마다 0.1~0.3초 "Disconnected". 연결 안 된 장비도 같은 답 → 답만으로 확인 불가. N.I.N.A.에서 가이더를 끊어도 PHD2의 장비 연결은 그대로
- 못 함: 자동초점·솔빙(시뮬레이터 카메라를 별 사진 방식으로 바꾸는 설정이 N.I.N.A. 화면에만 있음), N.I.N.A.·PHD2 창 닫기(사용 중인 프로그램이라 안 닫음)
- **❗ 모두 반영 (같은 날)**: 전체 경로 찾기(`NinaRig.FindSavedFileAsync`), 다크플랫은 DARK로 찍고 `DARKFLAT` 폴더로, 반전은 SideOfPier가 바뀐 뒤 멈출 때까지(`NinaRig.FlipAsync`·`WaitStillAsync`), StarLost는 평균에서 빼고 따로 셈(`GuideRaw.LostRecent`·`LowHfdRecent`), 디더링 안정화 실패는 30초 더 지켜보고(`WaitSettledAsync`) 연속 3번이면 멈춤 → 1분 안정되면 자동 재개, 15분 넘으면 "그대로 찍을까요?"(사용자 결정). 시뮬레이터 확인: `tests/Astro.Server.Tests/Real/SimulatorRigTests.cs`(AA_SIM=1) — 반전 14초 뒤 끝으로 판단, 디더링 13초 안정
- N.I.N.A. 반전은 대상으로 다시 이동하는 것: 대상이 아직 자오선 동쪽이면 같은 쪽이라 아무 일도 없다. 적도의가 알려 주는 좌표(현재 시점)는 보낸 좌표(J2000)와 약 0.02시간 다름

## 설계: 적정 노출 찾기 · 가이딩 중 별 잃음 대응 (2026-10-07)

**진행**: B(별 잃음)는 구현 — `Shoot/GuideWatch.cs`(원인 가르기, 순수 함수), `ShootSession`(찍기 전·찍는 동안 지켜보기, 원인별 대응, 원인별 멈춘 시간 기록), 장비 `GuideRawAsync`(PHD2 GuideStep·StarLost 이벤트의 SNR·HFD·튐 + N.I.N.A. 가이더 연결·적도의 추적). 아직: 이슬 여유(WandererBox 기온·습도)·열선 제어(지금은 알림만). A(노출)는 가이딩 중 노출 올리고 내리기만 구현, 시작 사다리·기억·SharpCap은 아직.

**PHD2 실기 확인 (2026-10-07, PHD2 2.6 + ASCOM 카메라 시뮬레이터)**: RPC에 게인 명령 없음(get/set_camera_gain·set_gain 없음) — `capture_single_frame`만 `gain`(0~100%)을 받음, 가이딩 게인은 PHD2 프로필(지금 4%). `set_exposure`는 목록 값만(1.2초 거절, 1·2초 됨), 루프 중에도 바뀜. PHD2 자체 자동 노출(목표 SNR 6, 1~5초)은 RPC로 고를 수 없음. ASCOM 카메라 시뮬레이터는 별이 없음. **PHD2 내장 Simulator + On-camera(새 프로필)로 확인**: 캘리브레이션 약 4분, GuideStep에 dx·dy·RADistanceRaw·StarMass·SNR·HFD·ErrorCode(코드가 읽는 이름과 같음), 가이딩 중 `set_exposure` 됨, 별을 잃으면 상태 LostLock + 프레임마다 StarLost(SNR 0, ErrorCode 2, Status는 한국어 문장), **별이 돌아오면 PHD2가 스스로 Guiding으로 돌아옴**, 가이딩 중에는 `find_star` 거절 → 별 다시 고르기는 멈춤 → 루프 → find_star → 가이딩. 그래서 구름·빛은 처음 3분 손대지 않고 그 뒤 2분마다 별 다시 고르기.

판정은 모두 규칙(코드), AI는 설명 문구만. 기준값은 첫 출사에서 실제 값을 보고 다듬는다. "확인 필요"는 API·장비 동작을 실기로 확인해야 하는 부분.

### A. 적정 노출 찾기

목표를 숫자로 정하고, 짧은 쪽부터 올리되 넘치면 내린다. 찾은 값은 장비(가이드 카메라·망원경 조합)별로 기억해 다음 밤의 시작값으로 쓴다.

**A-1. PHD2 가이드 노출** (캘리브레이션·가이딩 시작, 가이딩 중)
- 목표: 별 SNR 20~60, 포화 아님. 노출은 짧을수록 좋다(하모닉 적도의의 짧은 주기 흔들림 — 1~2초 선호)
- 사다리: 1 → 1.5 → 2 → 3 → 4초. 각 단계에서 2장 루프 → `find_star` → SNR(GuideStep·`get_star_image`) 확인
  - SNR < 20이면 다음 단계, SNR > 60이거나 포화면 PHD2가 다른 별을 고르게(`find_star` 다시, 확인 필요: 포화 별 제외 동작) → 그래도 포화면 한 단계 내림
  - 4초에서도 SNR < 10 → 별 없음(덮개·초점·구름) 안내
- 게인: PHD2 설정 그대로가 기본. 4초에서 SNR 10~20이면 "가이드 카메라 게인을 올리면 좋아요" 안내(PHD2 RPC로 게인을 바꿀 수 있는지 확인 필요 — 안 되면 안내만)
- 가이딩 중에도 1분 평균 SNR을 보고: 20 아래로 내려가면 한 단계 올리고, 80 넘게 회복하면 원래 단계로(`set_exposure`는 가이딩 중에도 됨 — 확인 필요). 바뀐 값은 상태 줄에 알림 없이 기록만
- 기억: 장비 프로필별 마지막 좋은 노출 → 다음 밤 사다리의 시작점

**A-2. SharpCap 극축 정렬 노출·게인**
- 목표: SharpCap이 극 위치를 찾음(첫 단계를 지남)·별이 하얗게 번지지 않음
- 시작: 게인 80%, 노출 1초(지금). 3초 안에 위치를 못 찾으면 노출 ×2 (최대 4초) → 그래도 못 찾으면 게인 최대 → 안내
- 너무 밝음(박명·광해로 배경이 높음): 스크립트가 프레임 통계(평균·최대, 확인 필요)를 보내면 배경 70% 넘을 때 노출을 반으로
- 기억: 성공한 노출·게인을 다음 밤 시작값으로

**A-3. 주 카메라(X-T5)**: 라이트는 계획·시험 사진(배경 밝기 → 노출 줄이기 제안)이 정하고, 플랫은 자동 노출(구현됨), 다크는 라이트와 같게. ISO는 N.I.N.A. gain으로(확인됨).

### B. 가이딩 중 가이드 별을 잃었을 때

**신호** (촬영 중 계속 받음)
- PHD2 이벤트: GuideStep(SNR · StarMass · HFD · 오차), StarLost(SNR · StarMass), Alert, 앱 상태(Guiding · LostLock · Looping), 연결 상태
- N.I.N.A.: 적도의 추적·한계(at park/limit), 장비 연결, 이벤트(`*-DISCONNECTED`)
- 주 카메라 사진: 별 수·배경(등급 계산에서 이미 잼)
- 온습도: WandererBox의 기온·습도 → 이슬점. **2026-10-08 실기**: `AscomWeather`가 ASCOM `WandererBoxEnvironment.ObservingConditions`를 직접 읽음(N.I.N.A.가 같은 상자 스위치를 잡고 있어도 됨, 이슬점은 드라이버가 계산). N.I.N.A. 날씨 장비는 사용자 프로필에 없어 쓰지 않음. 렌즈 프로브 온도는 이 드라이버에 없음(허브 상태 줄에만). 열선(DC3)은 Empire 자동 모드(이슬점 온도 차이, 사용자 5°C로 설정)에 맡김 — 자동 중에는 ASCOM·N.I.N.A.에서 DC3 읽기 전용(값 0~255는 읽힘, 설정을 15→5°C로 바꾸자 255→0). 설정값은 상자에 저장되는 듯(파일·레지스트리 없음). Empire 화면 확인(사용자): DC3 ON/OFF는 표시 — 자동이 데워야 하면 저절로 ON·255(기준 20°C로 올려 확인, 켜고 끄는 방식으로 보임), Empire 자동은 렌즈 프로브 온도 − 이슬점으로 판단(기준 19°C일 때 프로브 28.8°C·이슬점 9.9°C → 차이 18.9°C에서 0↔255 반복, 5°C로 바꾸자 0 유지), 자동 모드에서 DC3 슬라이더+OK는 Empire가 막음("Manual control is not available under Temperature Difference Mode…"). 다음: 이슬 여유·DC3 세기·자동 여부를 화면에 보여 주고 이상하면 알리기

**원인 가르기 → 대응**

| 원인 | 알아보는 법 | 대응 | 기다리는 상한 |
|---|---|---|---|
| 강한 빛 (차 불빛·손전등) | StarMass·배경이 몇 초 사이 크게 튐, 주 사진 배경 급증 | 별을 새로 고르지 않고 같은 자리에서 기다림. 찍던 사진은 끝나면 등급(대부분 F) | 2분 → 구름처럼 |
| 구름 | SNR·StarMass가 수십 초에 걸쳐 줄고 주 사진 별 수도 줄어듦 | 지금 노출을 멈춤(`abort-exposure`, 그 장 버림). PHD2는 같은 자리에서 루프. 30초마다 확인 → SNR이 연속 3번 기준 위면 가이딩 재개 → 멈춘 지 10분 넘었으면 솔빙으로 다시 가운데 → 이어서 찍기 | 30분 → 촬영 멈추고 알림(대상 바꾸기·마무리 고르기) |
| 이슬 (가이드 망원경) | 몇 분 이상 SNR이 천천히 줄고 HFD는 커짐, 주 사진은 멀쩡, 기온−이슬점 < 2°C | 열선 켜기·세기 올리기(WandererBox, 확인 필요) + 가이드 노출 한 단계 올림 → 안 되면 "가이드 망원경 렌즈에 이슬이 맺힌 것 같아요" 알림 | 10분 → 알림 (촬영은 계속 시도) |
| 기온 변화로 가이드 초점 밀림 | HFD가 천천히 커지고 SNR 감소, 습도는 정상 | 가이드 노출 올림 + "가이드 망원경 초점을 확인해 주세요" 알림 (손으로만 가능) | 알림만 |
| 바람·케이블 걸림 | 오차가 한두 걸음 만에 크게 튀고 StarLost, 적도의는 추적 중 | 바로 별 다시 고르기(`find_star`) → 큰 이동이면 솔빙으로 다시 가운데. 같은 일이 10분에 3번이면 "바람이나 케이블을 확인해 주세요" | 바로 |
| 적도의가 멈춤 (한계·전원) | 추적 꺼짐·한계, 별이 한 방향으로 흘러 나감 | 기다리지 않음: 노출 멈춤, 촬영 끝(이유 표시), 알림 | 0 |
| 장비 끊김 (가이드 카메라·PHD2) | 연결 끊김 이벤트, PHD2 응답 없음 | N.I.N.A.로 가이더 다시 연결 1~2번 → 되면 별 고르고 이어서, 안 되면 멈추고 알림 | 2분 |
| 새벽 박명 | 배경이 시간에 따라 계속 밝아짐 + 계획 끝 시각 근처 | 끝 조건(새벽)으로 촬영 끝 | — |

**공통**
- 별을 잃은 채로 찍힌 사진: 잃은 시간이 노출의 20%를 넘으면 그 장을 멈추고 버림(시간 절약), 아니면 끝까지 찍고 등급에 맡김
- 화면: 안내 제목을 원인별로("강한 빛이 들어왔어요" · "구름이 지나가고 있어요" · "가이드 망원경에 이슬이 맺히는 것 같아요" · "적도의가 멈췄어요" …), 상태 줄에 멈춘 시간. 지금처럼 모두 "구름"으로 쓰지 않는다
- 오늘 밤 요약: 원인별 멈춘 시간(예: 구름 24분 · 빛 2분)
- 나중에 휴대폰 알림이 생기면 "알림"은 휴대폰으로도
- 모의 장비: 원인마다 모의 실패(빛·구름·이슬·바람·적도의 멈춤·끊김)로 화면과 흐름을 시험
