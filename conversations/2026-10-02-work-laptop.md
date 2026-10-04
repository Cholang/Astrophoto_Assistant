# 대화 기록: 2026-10-02 회사 노트북

> 촬영 준비 화면을 원호 시안으로 7단계까지 만들고, Codex 리뷰(준비 화면 UI·절차)를 정리한 날.
> 이전 기록: [2026-10-01-home-pc.md](2026-10-01-home-pc.md) · Codex 쪽: [2026-10-02-codex-design.md](2026-10-02-codex-design.md)

---

## 다음 세션에서 이어서 하려면 (먼저 읽을 것)

1. **집에서 시작할 일: 최신 시안 [mockups/aa-prepare-arcs.html](../mockups/aa-prepare-arcs.html)을 보고 사용자가 정리해 올 피드백을 반영할 차례.** 사용자가 화면을 충분히 본 뒤 수정 요청을 따로 적어 온다 — 그 전에 시안을 먼저 고치지 않는다
2. [DESIGN.md](../DESIGN.md) 3장 "촬영 준비"(7단계·정보는 한 곳에만·캘리브레이션 기본값), 2장 "화면 비유: 항성계"(Z축 깊이감 = 패럴랙스, 마감 때), 1장 끝 "[마감 때] 스켈레톤 스크린"
3. [docs/codex/REVIEW_CODEX.md](../docs/codex/REVIEW_CODEX.md) 7절·9절 처리 표
4. 앱 코드의 준비 화면(`PrepareScreen.tsx` 쉐브론 7칸, `PrepareRunner` 옛 7단계)은 아직 옛 설계 — 시안이 정해지면 다시 만든다

---

## 1. 정리한 것
- Obsidian으로 한 번 열어 생긴 파일 정리: `.obsidian/`은 `.gitignore`, `천체 사진/` 삭제, 줄바꿈만 바뀐 문서 4개 되돌림
- 1/6 극축 정렬 절차 확정: 단계 끝은 모두 사용자가 누름(끝 버튼 = 다음 단계 시작), PHD2가 카메라를 놓을 때까지 다시 시도 → 시간이 다 되면 "PHD2의 장비 연결 창에서 해제" + 다음, 추적 속도는 N.I.N.A.로 항성 확인·설정(`/equipment/mount/tracking`, 실기 미확인), SharpCap에 적도의 연결 안내는 넣지 않음, "마쳤어요" 뒤 바로 PHD2 재연결(SharpCap이 열려 있으면 그 이유로 실패), "이미 정렬했어요" 버튼 없음
- 화면 비유: 항성계(AA = 항성, 레일 단계 = 행성, 단계 안 과정 = 위성) — 테마만 확정, 적용은 마감 때. Z축 깊이감(= 패럴랙스, 스크롤 아님)·스켈레톤 스크린도 마감 때

## 2. 촬영 준비 시안
- Codex 원호 시안 스타일로 Claude가 1~6단계 시안 → Codex가 비교안(`aa-prepare-arcs-codex.html`: 위쪽 세부 과정 표시, 수치 약하게, 시험 사진 크게)
- Codex 리뷰 8절(CX-PREP-FLOW-01~04) 검토 후 사용자 결정
  - **7단계**: 극축 정렬 · 캘리브레이션 · 대상 이동 · 초점 · 센터링 · 가이딩 · 시험 사진 (극축 정렬 직후 망원경이 극 쪽이라 캘리브레이션은 적위 0° 근처로 옮겨서)
  - 캘리브레이션 기본값: 준비 시작마다 새로, 같은 밤 대상 변경은 재사용
  - **정보는 한 곳에만**: 단계 안 진행은 오른쪽 맨 위 한 줄(점 + 지금 과정 이름 / "7 / 9 지점")
  - 원호를 왼쪽으로 붙여(가장 오른쪽 x≈210) 가운데·오른쪽 공간 확보
- 반영한 최신 시안: `mockups/aa-prepare-arcs.html` (7단계, 실패·대상 낮음·캘리브레이션 재사용 경우, 테마 3개)

## 3. 주말 (같은 노트북, 집)
- 사용자 피드백 1차: 가운데 사각 영역이 좁음 → **v2 시안** [mockups/aa-prepare-arcs-v2.html](../mockups/aa-prepare-arcs-v2.html): 원호 바깥 전체가 하늘 화면, 글은 왼쪽 아래에 얹기, 핵심 수치는 오른쪽 위. 사용자 평가 "괜찮다"
  - 라이브뷰 사실: 주 카메라(X-T5)는 찍을 때만 사진 → 이동은 하늘 지도(적도의 위치), 캘리브레이션·가이딩은 가이드 카메라 화면
  - ① 극축 정렬에 **위성 3개**(세부 과정) 실험: 비스듬한 궤도, 맨 앞 위성이 크게 나오고 그 안에 지금 과정의 정보만(오른쪽 위 계기판 대신), 지난·다음 위성은 과장되게 작고 흐리게. 사용자 "괜찮다"
- 결정: 자동초점 설정은 AA 추천값(N.I.N.A. 설정 불변의 예외, 확인 후 적용), 구도 회전은 로테이터 링이라 나중에 센터링 안에서, 시험 사진은 확인용(첫 장으로 안 씀), 이전 단계로 돌아가기는 결과 보기 + "다시 하기"(영향받는 뒤 단계만), 구도 조정(비켜 맞추기)은 다음 버전 — DESIGN.md "촬영 준비"
- 사용자 취향: 도형은 처음부터 크게(작으면 여백만 크고 지저분), 지난·올 정보는 과장되게 흐리게
- 이어서: 사용자가 Codex와 시안 작업을 더 한 뒤 가져온다. 남은 판단 — 위성 안 "카메라 연결됨"과 아래 상태 줄의 중복, 뒤쪽 위성 흐림 정도

## 4. 2026-10-04 — 준비 7단계를 하나씩 검토 중
- 사용자 요청: 준비 과정을 단계별로 맞는지 검토 (답은 간단히)
- ① 극축 정렬이 첫 단계 → **맞음** (축 자체를 맞추는 일이라 뒤 단계의 바탕. 조건: 북극성 근처가 보일 만큼 어두워야 함)
- 다음: ② 캘리브레이션부터 이어서 검토
- 같은 때 Codex 시안 `mockups/aa-prepare-arcs-v2-orbit-codex.html`, `mockups/aa-prepare-arcs-v3.html`이 새로 생김 (Claude는 아직 안 봄, 커밋 안 됨)

## 5. 2026-10-04 밤 — v4 시안
- [mockups/aa-prepare-arcs-v4.html](../mockups/aa-prepare-arcs-v4.html): Codex v3 바탕, 1단계만 새 흐름 — 위성 4개(넘겨받기 · 극 찾기 · 정렬 · 돌려주기), 극 찾기에서 적경축 90° 회전(SharpCap 필수 과정, 앞서 빠졌던 것), 정렬 중엔 위성이 SharpCap 화면을 담는 창, AA 평가(멀어요·아쉬워요·충분해요·완벽해요)는 왼쪽 아래. 위성 쉬는 자리를 오른쪽(1440, 490)으로 옮겨 글과 안 겹치게
- 사용자 결정: 앱은 시안에서 7단계를 다 정한 뒤에 만든다. 2~7단계는 v3 그대로, ②부터 차례로 검토

## 6. 2026-10-04 밤 — 실제 적도의 연결 (UMi17X, 허브 경유)
- 장비: 적도의 Proxisky UMi17X(OnStepX 10.20a), 허브 WandererBox Plus V3(Empire 앱, 스위치=COM4). 포트 용도: DC1 Always On(5525, 미니 PC), DC2 0~13.2V(적도의), DC3 PWM(열선), DC4~6 일반, USB 스위치. 적도의 전원 DC2, USB 스위치 켜야 적도의(CP210x)·가이드 카메라가 PC에 보임
- 적도의는 이 노트북에서 COM3 (직접·허브 모두). OnStep 드라이버에 COM12가 들어 있어 PHD2 연결이 실패했던 것 → COM3으로 바꿈
- **OnStep ASCOM 드라이버는 여러 프로그램 동시 연결 가능** (로컬 서버 `ASCOM.OnStep.exe`, 창 "OnStep Driver Server") → N.I.N.A.와 PHD2가 같은 적도의에 동시 연결 성공, Device Hub 불필요. N.I.N.A. 가이더 → PHD2 연결도 성공
- PHD2는 카메라를 USB 자리(경로)로 기억 → 허브로 옮기면 "선택한 ToupTek 카메라를 찾을 수 없습니다", 장비 창에서 다시 골라야 함 (AA가 이 오류를 알아보고 안내할 것)
- N.I.N.A. API로 스위치 USB 켜기(`switch/set?index=4&value=1`)는 "성공" 응답인데 실제로 안 바뀜 → AA가 전원을 다룰 때 다시 확인
- 스위치: N.I.N.A.가 스위치에 연결하면 Wanderer 드라이버가 Empire 앱을 스스로 띄움 (그대로 둠)
- 적도의 규칙: Set Home 금지, Go Home만 (UMi 디스코드 조언)
- **적도의 움직임 시험** (경통 없이, 사용자 지켜봄): N.I.N.A. goto로 RA 83° 의도 → 극을 3° 넘으며 Dec도 움직이고 RA는 60°만 (피어 동→서). Go Home(N.I.N.A. `/equipment/mount/home`) 22초, AtHome·적위 90° 정확. ASCOM 직접 연결(`New-Object -ComObject ASCOM.OnStep.Telescope`, 동시 연결 가능) `MoveAxis(0, ±3°/s)`(최대 3.55°/s): 한쪽은 홈에서 60°(시간각 22.0h, goto가 멈춘 곳과 같음)에서 멈춤, 반대쪽은 홈에서 멈춤. 소리 없음. 읽은 설정(`CommandString("GXE9")` 등, : # 빼고): 자오선 한계 동 4분·서 20분, 극 아래 한계 12, 수평 0°, 머리 위 90°, 펌웨어 OnStepX 10.20a(Apr 26 2026 빌드 — 디스코드에서 받은 V1.0.1A로 보임). 60° 한계 원인은 아직 모름(앱의 물리 RA 한계 설정일 수도)
- 앱 설정(사용자 스크린샷): UMi17X 1.0.1, Super Limits RA 좌우 95°·Dec 180/180, Meridian Limit E 1·W 5(도 — OnStep 명령으론 4·20분), Overhead 90·Horizon 0. Axes Monitor 홈에서 RA 0.00°·Dec 358.63°
- goto 시험(바닥, 경통 없음): 처음 결과(동쪽 목표가 자오선에서 멈춤, 서쪽 목표가 동쪽으로 감)는 **Claude의 단위 실수** — N.I.N.A. `mount/slew`의 `ra`는 도(°)인데 시간(h)으로 보냄. 적도의는 보낸 좌표 그대로 정확히 감. 그 사이 추적이 켜진 채 자동 자오선 반전 1번(정상 동작, 케이블 장력에 바닥의 적도의가 밀림). 디스코드 문안은 보내지 않기로
- **고친 뒤 캘리브레이션 후보 확정**: A 자오선 서쪽 1h·적위 0° (피어 동쪽) 통과, B 자오선 동쪽 1h·적위 0° (피어 서쪽) 통과 — 둘 다 오차 0.38°, 약 40초, Go Home 정상 → DESIGN.md ②에 A→B 규칙
- 남은 의문: 극(적위 90°)에서 RA축만 `MoveAxis`로 돌리면 한쪽 60°(시간각 −2h)에서 멈추고 반대쪽은 홈에서 멈춤 — goto 단위 실수와는 무관한 시험이라 원인 미확인 (극축 정렬 회전 방식은 이걸 풀거나 60°로)
- 디스코드(Proxisky 지원) 요점: Set Home 금지(홈 센서가 있음), OnStep RA 한계는 끌 수 없고 물리 RA/Dec 한계는 끄지 말 것(Dec 내부 배선 손상), 갑자기 멈출 때 소리는 서보 모터의 정상 소리, 홈에서 솔빙·싱크 하지 말 것(사진 촬영엔 싱크 불필요), N.I.N.A.를 쓰면 Proxisky 앱 필요 없음
- 할 일(AA 출발 전 점검): 적도의 COM 포트 자동 찾기(:GVP# → "On-Step"), PHD2 카메라 자리 오류 안내, 드라이버 서버 창 최소화

## 7. 2026-10-05 새벽 — 준비 ②~⑥ 검토 (v4 시안에 ②~⑤ 반영)
- ② 캘리브레이션 위치 A(자오선 서쪽 1h·적위 0°) → B(동쪽 1h) 확정, 실패·예외 10가지는 DESIGN만(시안엔 대표 1개 — AA가 안에서 재시도)
- ③ 낮은 대상·자오선 반전은 계획 단계에서 미리 알림, ③에선 기다리기 → 알림 → 기다리기 끝 → 이동, 30° 아래 이동 막음, 이동 중 멈춤. 음성 "멈춰"는 마감 때 보조 수단
- ④ 포커서 Oasis Rose 연결·이동 정상, 프로브는 연결할 때만 잡힘(26.4°C), 보드 온도 37°C대 — "Link board Temp" 꺼 둘 것, 멈춤 감지 켜짐, Max position 56000 기본값(다음 조립 때 사용자가 0·max 설정)
- ⑤ ASTAP + D50, N.I.N.A. 솔빙 설정 그대로(2초, 1′, 10번)
- ⑥ PHD2 펄스 가이드 4방향 정상(45″/3초), 주 카메라 한 픽셀 2.41″
- **맑은 날 할 일**: 극축 정렬 나사↔값 짝·AA 각도 오차 vs SharpCap, 캘리브레이션 실제(A/B), 가이딩(노출 1~2초, PPEC 주기), 극에서 RA 60° 멈춤 원인, 자동초점·센터링 실제 (메인 경통 없이 가이드 카메라만으로도 ①②⑥ 시험 가능)

## 지금 상태와 다음 할 일
- [ ] (집) 사용자 피드백으로 `aa-prepare-arcs.html` 다듬기
- [ ] 시안 확정 뒤: 준비 화면 앱 코드 다시 만들기(원호 + 7단계, `PrepareRunner` 단계 정의·다시 하기 규칙), (화면 내용은 DESIGN.md로 옮김, 그 파일은 `docs/PREPARE_IMPLEMENTATION.md` 구현 계획으로 바뀜 — Codex 리뷰용)
- [ ] **맑은 날 실기** (7절 목록): 극축 정렬 나사↔값 짝·AA 오차 vs SharpCap, 캘리브레이션 A/B, 가이딩(노출 1~2초·PPEC 주기), 극에서 RA 60° 멈춤, 자동초점·센터링·시험 사진 — 메인 경통 없이 가이드 카메라만으로 ①②⑥ 가능. 포커서 0·max 설정(사용자)
- [ ] v4 시안: 사용자가 Codex와 리뷰 → 의견 → Claude 피드백 → 앱 착수 여부 결정
- [ ] W4~W7 (SharpCap·PHD2 실제 연결 → 이동·초점·센터링 → 가이딩 → 촬영·종료)
- [ ] (나중) **SharpCap 극축 정렬 조절량을 AA로** — 가능성 높아짐 (2026-10-03, 집 PC 기록의 "우선순위 낮음" 항목을 대신함)
  - 근거: SharpCap 개발자 Robin 포럼 답변(2023-11-02, https://forums.sharpcap.co.uk/viewtopic.php?start=10&t=6406) — 스크립트로 `SharpCap.Transforms.SelectTransform("Polar Align")`, **`PolarAlignOffset`** 값 = 고도·방위(위아래·좌우) 조절량, "Skip Introduction"·"Auto Advance" 체크 시 단계 자동 진행, `BeforeFrameDisplay` 이벤트로 표시 직전 프레임 접근
  - 방식(제안): SharpCap 스크립트가 1초쯤마다 값을 AA 서버(localhost)로 HTTP로 보낸다 (Pyro4·중간 파이썬 불필요). AA는 ① 극축 정렬 "정렬 확인" 위성 안에 조절량을 크게, 휴대폰에도. 완료 확정은 그대로 사용자
  - 확인할 것: `PolarAlignOffset`이 붙은 객체·단위·부호, SharpCap 4.1(사용자 버전)에서 같은지, `BeforeFrameDisplay` 프레임에 안내선·표식 포함 여부(아니면 창 화면 비추기), SharpCap 사용 약관. 확인은 SharpCap 스크립트 콘솔(Pro) + 내장 테스트 카메라로 — 콘솔에 넣을 명령은 Claude가 준비
  - 화면 대안: Windows 창 화면 캡처로 SharpCap 창을 AA 큰 화면에 비추기 (SharpCap 기능과 무관, 이 노트북에서 시험 가능)
  - **2026-10-04 확인 (이 노트북 SharpCap 4.1.14155, 설치 폴더의 공식 인터페이스 `SharpCap.Interfaces.dll`·`.xml`을 읽음)**: 스크립트용 `SharpCap.PolarAlignment`(IPolarAlignment: `Stage`, `IsActive`, `CanAdvance`, `Advance()`, `AutoAdvance`, `Reset()`, `Offset` = PointF, **픽셀 단위, 마지막 단계에서만 유효**), `SharpCap.PolarAlignOffset`, `SharpCap.SelectedCamera.BeforeFrameDisplay`(ICamera 이벤트) 존재 → 공식 스크립트로 가능. 남은 것: x·y가 고도·방위 중 어느 쪽인지와 부호(실제 하늘에서 나사를 돌려 보며, 장비마다 한 번 — AA가 "방위 나사를 조금 돌려 보세요"로 스스로 짝을 맞출 수도), 프레임에 안내선 포함 여부. 각도 변환은 필요 없음(사용자: 돌릴 방향과 0으로 줄어드는 것만 보이면 됨, 픽셀 값 그대로 OK)
  - **2026-10-04 실기 시험 (가이드 카메라 G3M662M, SharpCap 4.1.14310, 실내·구름)**
    - SharpCap 스크립트(IronPython 3.4, .NET 8): **카메라가 열려 있어야** `SelectTransform("Polar Align")`이 먹힘(안 열려 있으면 오류 없이 무시, `SelectedTransform`=None). 열린 뒤: 선택됨, `IsActive`=True, `Stage`="First", `Offset`=0 (별이 없어 첫 단계)
    - **SharpCap → 로컬 HTTP 보내기 성공**: `clr.AddReference("System.Net.WebClient")` 후 `WebClient().UploadString(...)`, 또는 `System.Net.Http.HttpClient` — 시험 수신 서버에 `{"stage":"First","active":true,"x":0.0,"y":0.0}` 도착
    - 창 화면 가져오기: PrintWindow 방식은 메뉴만 찍히고 영상 부분은 하얗게(그래픽카드로 그림) → Windows Graphics Capture(OBS 방식) 또는 `BeforeFrameDisplay` 프레임으로 다시 시험할 것
    - PHD2 제어 통로(포트 4400, PHD2 2.6.14, 프로필 newbee): 읽기 명령 정상. **`stop_capture` 직후엔 아직 Looping → `set_connected false`가 "capture active"로 거절** → 상태가 Stopped가 될 때까지 기다린 뒤 끊으면 성공. 끊은 뒤 SharpCap이 바로 카메라를 여는지는 PHD2를 꺼 버려 미확인 → 다시 시험
    - PHD2 연결 메모(사용자): 카메라 연결 후 PHD2 실행 → Ctrl+C로 장비 연결 창 → 프로필 newbee(Camera: ToupTek Camera, Mount: OnStep Telescope (ASCOM)) → 이번엔 카메라만 연결. 마운트 연결이 안 돼 Telescope Simulator로 바꿈(원인 모름)
    - **카메라 넘겨주기 전 과정 성공 (사용자 손 안 댐)**: PHD2 `stop_capture` → Stopped(0.4s) → `set_connected false`(0.8s) → `SharpCap.exe /camera "G3M662M(USB2.0)" /runscript pa.py` → 카메라 즉시 열림, 스크립트가 극축 정렬 선택 후 1초마다 상태를 로컬 HTTP로 보냄 → SharpCap 창 닫기(`CloseMainWindow`, 0.7s, 확인 창 없음) → PHD2 `set_connected true`(3s, 드라이버 지연 없음) → `loop` 정상. 참고: [명령줄 옵션](https://docs.sharpcap.co.uk/4.0/34_CommandLineArguments.htm)(`/camera`, `/runscript`), 설정의 "마지막 카메라 다시 열기"도 있음
    - **화면 가져오기 성공**: 하얗던 원인은 캡처 방식 — `PrintWindow`에 `PW_RENDERFULLCONTENT`(2)를 주면 영상까지 찍힘. 스크립트 `cam.SaveAsViewed(경로)`도 보이는 그대로(늘이기 적용) PNG 저장(`_WithDisplayStretch` 붙음). 스크립트로 노출·게인 읽기/쓰기 가능(`Controls.Exposure.ExposureMs`, `Controls.Gain.Value`; 당시 125ms·게인 1268, 실내라 밝은 회색)
    - 창 캡처는 DPI 인식(`SetProcessDPIAware`)을 켜야 200% 화면에서 창 전체(아래 Polar Align 패널 포함)가 찍힘. 1단계 화면 안내선이 없던 건 실내라 별이 없어서(별을 찾으면 노랑·빨강 원, 풀리면 극 표시)
    - **1단계 설계 바꿈 (DESIGN.md ①)**: 준비 시작 = 바로 시작, AA가 넘겨받기·SharpCap 실행 → 화면 들어오면 "정렬하세요" → 사용자 "정렬 완료" → AA가 돌려주기 → "캘리브레이션 시작". 노출은 AA가, 정렬 평가는 AA 자체 기준(픽셀 × 화면 배율, 계획 기준)
    - 실행 파일: SharpCap `C:\Program Files\SharpCap 4.1 (64 bit)\SharpCap.exe`, PHD2 `C:\Program Files (x86)\PHDGuiding2\phd2.exe` — 다음엔 Claude가 직접 연다
