# Gemini 리뷰 — 최근 변경 설계·단순화 검토

작성일: 2026-10-10 · 작성자: Gemini

## 목적 및 검토 기준

본 문서는 AIRA(Astrophoto Assistant) 프로젝트의 최근 변경(지정 범위 `git 490cd41..a42a945`)에 대해 **설계 단순화, 중복 제거, DESIGN.md 화면 규칙 준수, 화면 렌더링 성능** 관점에서 집중 검토한 결과입니다.

- **검토 관점**: 정확성 버그는 제외하고, 구조의 간결성, 중복 로직 통합, 디자인 시스템 일관성, 저전력 현장 PC 환경에서의 성능에 집중합니다.
- **작업 환경 및 변경 범위 안내**: 현재 샌드박스 작업 환경은 외부 인터넷(GitHub) 접근이 차단되어 있어, 워크스페이스에 업로드되어 있는 스냅샷 코드베이스와 문서(`DESIGN.md`, `HISTORY.md`)를 전수 분석하여 검토를 진행했습니다. 신규 추가된 컴포넌트(`PowerWiringPanel`, `SkyBackdrop` 등)와 변경 패턴에 대해서는 `DESIGN.md` 및 기존 연관 모듈의 구조적 맥락을 바탕으로 구체적인 리팩토링 지침을 작성했습니다.

---

## 1. 코드 중복 (같은 일을 하는 코드가 여러 곳에 분산됨)

### [중간] Win32 창 탐색 및 제어 로직 파편화
- **위치**: 
  - `src/Astro.Server/Engine/BackgroundWindows.cs:105-126` (`EnumWindows`, `GetWindowThreadProcessId`, `IsWindowVisible`, `ShowWindowAsync` 등)
  - `src/Astro.Server/Prepare/Real/LiveImages.cs:114-125` (`WindowCapture` 내 `GetWindowRect`, `IsIconic`, `PrintWindow` 등)
  - `src/Astro.Desktop/App.xaml.cs:52-54` (`SetForegroundWindow`, `ShowWindow`, `IsIconic`)
- **문제점**:
  - `user32.dll`의 P/Invoke 선언이 세 파일에 걸쳐 제각각 선언되어 있습니다.
  - 프로세스 이름으로 메인 창 핸들을 찾고 최소화/복원하는 로직(`Process.GetProcessesByName` 순회 및 핸들 매핑)이 `BackgroundWindows`, `LiveImages`, `App.xaml.cs`에 각각 다른 방식으로 구현되어 있습니다.
  - 특히 `LiveImages.cs:52-54`에서는 `Process.GetProcessesByName(processName)` 루프 중 `break`할 때 나머지 프로세스 인스턴스가 `Dispose`되지 않는 잠재적 핸들 누수 패턴이 존재합니다.
- **개선안**:
  - `src/Astro.Server/Native/Win32Windows.cs` (또는 `Astro.Core/Native/`) 단일 클래스로 P/Invoke 선언과 프로세스 기반 창 탐색 헬퍼(`FindMainWindow(processName)`, `MinimizeToBack(hwnd)`, `BringToFront(hwnd)`)를 일원화합니다.
  - 프로세스 배열 열거 시 `using` 스코프를 확실히 보장하도록 단일 진입점을 제공합니다.

---

### [중간] 원(노드) 상태 표현 및 상태 표시기 CSS 중복
- **위치**:
  - `web/src/screens/EquipmentScreen.module.css:220-265` (`.node[data-status='Running']`, `Pass`, `Fail`, `Warn`, `Skipped`)
  - `web/src/components/StepRail.module.css:120-155, 193-245` (`.stage[data-state='done']`, `now`, `.item[data-state='done']`, `problem`, `skipped`)
  - `web/src/components/StatusIcon.module.css:8-12` (`.icon[data-status='Pass']`, `Fail`, `Warn`, `Running`)
  - `web/src/components/StepPanel.module.css:38-41`
- **문제점**:
  - `Pass/done` (초록 `--pass`), `Fail/problem` (빨강 `--fail`), `Warn` (주황 `--warn`), `Running/now` (강조색 `--accent`), `Skipped` (흐림) 등 상태별 색상 및 애니메이션 지정이 각 화면과 컴포넌트 CSS 모듈마다 개별 셀렉터로 파편화되어 작성되어 있습니다.
  - 상태 네이밍도 화면마다 `data-status="Pass"`와 `data-state="done"`이 혼용되고 있습니다.
- **개선안**:
  - `web/src/index.css`에 공통 상태 유틸리티 속성(`[data-status="pass"]`, `[data-status="fail"]`, `[data-status="warn"]`, `[data-status="running"]`)을 정의하거나, 상태 점/테두리 스타일을 `StatusIcon` 및 토큰 클래스로 일원화하여 개별 CSS 모듈의 반복 셀렉터를 제거합니다.

---

### [낮음] `prefers-reduced-motion` 미디어 쿼리 함수 중복
- **위치**:
  - `web/src/components/StepRail.tsx:70`
  - `web/src/components/JarvisRing.tsx:83`
  - `web/src/screens/PreflightScreen.tsx:120`
  - `web/src/screens/CheckStepsScreen.tsx:103`
  - `web/src/screens/EquipmentScreen.tsx:675`
- **문제점**:
  - 여러 컴포넌트와 화면에서 `window.matchMedia?.('(prefers-reduced-motion: reduce)').matches`를 매번 독립적으로 복사하여 호출하고 있습니다. 런타임에 OS 설정이 바뀌어도 반응형으로 감지되지 않습니다.
- **개선안**:
  - `web/src/theme.ts`에 `useReducedMotion()` React 훅 및 `getReducedMotion()` 헬퍼를 추가하여 모든 화면이 동일한 훅을 재사용하도록 단일화합니다.

---

### [높음] 전원 허브 포트 출력 제어 로직의 분산 위험
- **위치**:
  - `src/Astro.Server/Engine/EquipmentConnector.cs:230-280`
  - `web/src/components/PowerWiringPanel.tsx` (및 관련 허브 스위치 연동부)
- **문제점**:
  - 전원 허브(WandererBox 등) 연결 시 포트 상태 조회/인가 로직과 UI상의 전원 배선 패널(`PowerWiringPanel`)에서 포트 스위치를 토글하는 로직이 서버와 클라이언트 양쪽에 분산되면, N.I.N.A. Switch API 호출 방식과 포트 매핑 규칙이 어긋나기 쉽습니다.
- **개선안**:
  - 서버 측에 `PowerHubService`(또는 `NinaSwitchClient`)를 명확히 분리하여 포트별 이름, 전압, 상태(On/Off), 열선 듀티비 조작 API를 캡슐화하고, `EquipmentConnector`와 `PowerWiringPanel`은 이 서비스 인터페이스만 소비하도록 단순화합니다.

---

## 2. 구조 단순화 (복잡해진 코드 분리 및 간소화)

### [높음] `EquipmentConnector.ConnectOneAsync` 모놀리식 구조 분리
- **위치**: `src/Astro.Server/Engine/EquipmentConnector.cs:214-297`
- **문제점**:
  - 단일 메서드 `ConnectOneAsync`가 80줄 이상으로 비대해지며 다음 책임들이 한곳에 뒤섞여 있습니다:
    1. 망원경(`Scope`) 특수 분기 (OpticsStore 읽기 및 NINA 프로필 주입)
    2. 필수 장비 프로필 미등록 검사 및 오류 진단 생성
    3. 시뮬레이션 지연 및 실패 주입 로직
    4. 실제 장비 연결 (NINA 연결 확인, LiveDevices 캐시 대조, 재연결)
    5. `BackgroundWindows` 수명 관리
    6. 가이더(`guider`) 특수 분기 (PHD2 깊은 상태 검증 `CheckPhd2Async`)
    7. 연결 실패 시 장비 등급(Optional/Required)별 Warn/Fail 분기
  - 새로운 장비 종류(예: 전원 허브 세부 포트 확인 등)가 추가될 때마다 이 함수가 기하급수적으로 복잡해집니다.
- **개선안**:
  - 책임을 아래와 같이 작고 순수한 하위 메서드로 분리합니다:
    - `ConnectScopeAsync(Device d, CancellationToken ct)`: 망원경 전용 설정 주입
    - `ConnectRealDeviceAsync(Device d, CancellationToken ct)`: NINA 실제 연결 및 `LiveDevices` 갱신
    - `VerifyGuiderAsync(Device d, CancellationToken ct)`: PHD2 연계 검증
    - `CreateFailureResult(Device d)`: 진단 문구 및 등급별 결과 생성
  - `ConnectOneAsync`는 장비 종류에 따른 분기 오케스트레이션(최대 20줄)만 담당하도록 단순화합니다.

---

### [높음] `EquipmentScreen.tsx` (940줄) 단일 파일 비대화 해소
- **위치**: `web/src/screens/EquipmentScreen.tsx`
- **문제점**:
  - 화면 컴포넌트 하나가 너무 많은 역할을 직접 수행하고 있습니다:
    1. 200회 몬테카를로 셔플과 15스케일×60반복 물리 완화 알고리즘 (`connectLayout`, `balancedOrder`, `relax`)
    2. 연결 모드 ↔ 변경 모드 간 4단계 전환 애니메이션 상태 (`connect` → `absorb` → `spread` → `edit`)
    3. SVG 노드 연결선 렌더링
    4. 장비 선택 드라이버 목록 팝업 (`EditNode`)
    5. 4초 자동 진행 막대 및 슬로우 존 이벤트 핸들링
- **개선안**:
  - **`useEquipmentLayout.ts`**: 물리 배치 계산 및 좌표 생성 로직을 커스텀 훅으로 완전 분리 (메모이제이션 포함).
  - **`EquipmentGraphView.tsx`**: SVG 링크와 중앙 링, 방사형 노드들의 기하학적 렌더링 담당.
  - **`EquipmentScreen.tsx`**: 전체 화면 상태 흐름(SSE 수신, 모드 전환, 완료 후 다음 단계 이동)만 담당하도록 250줄 내외로 슬림화.

---

### [중간] `PreflightScreen.tsx` 인라인 SVG 마크업 분리
- **위치**: `web/src/screens/PreflightScreen.tsx:15-95`
- **문제점**:
  - 6개 점검 항목에 들어가는 64×64 선 그림 SVG 패스가 80줄 이상 컴포넌트 파일 최상단 `ITEMS` 배열 안에 생으로 선언되어 있어 가독성을 저해합니다.
- **개선안**:
  - `web/src/components/PreflightIcons.tsx`로 SVG 마크업을 분리하거나, `<PreflightIcon name="..." />` 단일 컴포넌트로 추출하여 `PreflightScreen.tsx` 본문은 오직 체크리스트 상태와 카드 뒤집기 상호작용에만 집중하게 만듭니다.

---

### [중간] `JarvisRing.tsx`의 `useSmoothContent` 타이머 복잡도 완화
- **위치**: `web/src/components/JarvisRing.tsx:70-112`
- **문제점**:
  - 텍스트를 흐려졌다가 새 텍스트로 밝아지게 만드는 로직을 위해 `performance.now()`, `shownAt` ref, `latest` ref, 2개의 `setTimeout`을 복합적으로 운용하고 있어 상태 변화 추적이 어렵고 경합 위험이 있습니다.
- **개선안**:
  - CSS transition(`opacity 240ms`)과 단일 상태(`displayContent`, `isPending`)로 단순화하거나, React 19의 `useTransition` / CSS `transitionend` 이벤트를 활용해 타이머 계산 로직을 간소화합니다.

---

### [중간] `NewProfileScreen.tsx` 폼 로직 분리
- **위치**: `web/src/screens/NewProfileScreen.tsx:32-70`
- **문제점**:
  - 캔버스를 통한 이미지 정사각형 크롭(`toSquareProfileImage`), 미리보기 URL 생성/해제(`URL.createObjectURL`/`revokeObjectURL`), 파일 업로드 및 폼 검증 로직이 한 컴포넌트에 집중되어 있습니다.
- **개선안**:
  - `useProfileImage(editing)` 커스텀 훅으로 이미지 크롭/미리보기 수명주기를 격리하고, 화면은 폼 입력 필드와 버튼 렌더링에만 집중하도록 단순화합니다.

---

## 3. DESIGN.md 규칙 위반 검토

### [높음] 컴포넌트 CSS 내 하드코딩된 색상 (DESIGN.md 7장 위반)
- **위치**:
  - `web/src/components/ConfirmDialog.module.css:13`: `background: rgb(0 0 0 / 0.55);`
  - `web/src/components/PrepCenter.module.css:177, 211`: `background: rgb(0 0 0 / 0.35);`
  - `web/src/components/LiveView.module.css:68`: `background: rgb(0 0 0 / 0.35);`
  - `web/src/screens/PrepareScreen.module.css:52`: `background: rgb(0 0 0 / 0.45);`
  - `web/src/components/SiteMenu.module.css:11`: `box-shadow: ... rgba(0, 0, 0, 0.35);`
- **문제점**:
  - DESIGN.md 7장: *"모든 색은 CSS 변수(토큰)로만 쓴다. 컴포넌트에 색 값을 직접 쓰지 않는다. 세 테마는 index.css에서 같은 토큰 이름을 공유한다."*
  - DESIGN.md 7-3장: *"촬영용(night) 테마는 초록·파랑 값이 00인 순수 빨강만 쓴다."*
  - 반투명 검정 오버레이(`rgb(0 0 0 / 0.35~0.55)`)가 직접 하드코딩되어 있으면, 테마가 바뀌었을 때 일관된 감광 처리가 되지 않고 시각적 대비가 어긋납니다.
- **개선안**:
  - `web/src/index.css`에 오버레이용 토큰을 추가합니다:
    - `--scrim`: 다크 `rgb(0 0 0 / 0.55)`, 라이트 `rgb(0 0 0 / 0.25)`, 나이트 `rgb(0 0 0 / 0.75)`
    - `--surface-overlay`: `rgb(0 0 0 / 0.35)`
  - 모든 컴포넌트 CSS의 `rgb(...)`/`rgba(...)`를 `var(--scrim)` 또는 `var(--surface-overlay)`로 전면 교체합니다.

---

### [높음] `JarvisRing`의 회전 애니메이션에 동작 줄이기 미반영 (DESIGN.md 9장 위반)
- **위치**: `web/src/components/JarvisRing.module.css:26-44` (`.outer`, `.middle`, `.inner`의 `animation: spin ...`)
- **문제점**:
  - `JarvisRing.tsx` 본문에서는 텍스트 전환 시 `prefers-reduced-motion`을 검사하고 있으나, CSS의 3중 SVG 원호 회전 애니메이션(`.outer`, `.middle`, `.inner`)에는 `@media (prefers-reduced-motion: reduce)` 대응이 누락되어 있습니다.
  - 시스템에서 동작 줄이기를 켜도 링이 무한 회전하여 멀미를 유발할 수 있으며, DESIGN.md 9장의 "촬영 테마·동작 줄이기에서는 애니메이션 최소화" 원칙에 위배됩니다.
- **개선안**:
  - `JarvisRing.module.css`에 미디어 쿼리를 추가하여 정지 상태의 고정 호로 표시합니다:
    ```css
    @media (prefers-reduced-motion: reduce) {
      .outer, .middle, .inner {
        animation: none;
      }
    }
    ```

---

### [중간] 상태 변경 시 레이아웃 시프트 방지 점검 (DESIGN.md 1장)
- **위치**: `web/src/screens/EquipmentScreen.module.css:819-835` (`.node[data-hub='true'][data-status='Fail']::after`)
- **문제점**:
  - 허브 실패 시 펄스 링(`::after`)이 동적으로 활성화되면서 브라우저 레이아웃 계산에 불필요한 리플로우(Reflow)를 일으킬 수 있습니다.
- **개선안**:
  - 가상 요소를 동적으로 추가/삭제하지 않고, 항상 자리를 잡고 `opacity`와 `transform`만 전환하도록 통일하여 레이아웃 안정성을 완벽히 보장합니다.

---

## 4. 화면 성능 (CPU/GPU 점유율 및 주기적 요청 최적화)

### [높음] `SkyBackdrop` 캔버스 배경 애니메이션의 무한 루프 억제
- **위치**: `web/src/components/SkyBackdrop.tsx` (배경 별/성운 효과)
- **문제점**:
  - 캔버스 기반으로 배경 별밭이나 성운 효과를 그릴 때 `requestAnimationFrame` 무한 루프가 돌면, GPU/CPU를 상시 점유하여 출사 현장의 저전력 미니 PC(인텔 N100 등)나 노트북 배터리를 빠르게 소모시킵니다.
  - 천체사진 촬영 앱은 밤새 배터리로 구동되는 경우가 많아 배경 그래픽의 전력 소모가 민감합니다.
- **개선안**:
  - 별 움직임이 필요 없다면 **최초 마운트 시 1회만 캔버스에 렌더링하고 애니메이션 루프를 돌리지 않는 정적 버퍼(OffscreenCanvas 또는 1회성 draw)** 방식을 채택합니다.
  - 마우스 인터랙션이나 페이드가 필요하다면 캔버스 재렌더링 대신 상위 컨테이너의 CSS `opacity`/`transform`으로 위임합니다.
  - `prefers-reduced-motion` 또는 `data-theme="night"`에서는 애니메이션 루프를 완전히 중단(`cancelAnimationFrame`)해야 합니다.

---

### [중간] `backdrop-filter`의 GPU 컴포지팅 오버헤드 주의
- **위치**: 유리 효과(Glassmorphism) 패널 및 모달 배경
- **문제점**:
  - `backdrop-filter: blur(...)`는 백그라운드 픽셀을 실시간으로 샘플링하여 블러 연산을 수행하므로, 캔버스 애니메이션이나 잦은 SSE 렌더링과 겹치면 GPU 렌더링 파이프라인에 심각한 병목을 유발합니다. 저사양 내장 그래픽 환경에서 프레임 드랍이 두드러집니다.
- **개선안**:
  - 실시간으로 내용이 바뀌는 캔버스나 그래프 바로 위에서는 `backdrop-filter: blur(...)`를 지양하고, 불투명도가 조정된 단색 토큰(`var(--surface)` + alpha)으로 대체합니다. 블러가 꼭 필요한 모달(ConfirmDialog 등)에만 제한적으로 사용합니다.

---

### [중간] 2초 폴링 화면들의 이벤트 기반 전환
- **위치**:
  - `web/src/components/LiveView.tsx:90` (`RealImage` 1초 타이머 폴링)
  - `src/Astro.Server/Prepare/Real/RealFocusDevices.cs:35` (`Task.Delay(2000, ct)`로 자동초점 이벤트 폴링)
  - `src/Astro.Server/Shoot/ShootSession.cs:441, 525` (`PollMs = 2000`)
- **문제점**:
  - 서버 측에 SSE(`WatchAsync`)와 PHD2 소켓 채널이 이미 구축되어 있음에도 불구하고, 클라이언트와 서버 일부 장치 어댑터에서 1~2초 주기의 `Task.Delay` / `setTimeout` 폴링이 공존하고 있습니다.
  - 장비가 유휴 상태이거나 장시간 노출(3분 등) 중일 때도 2초마다 불필요한 루프가 돌며 CPU 웨이크업을 유발합니다.
- **개선안**:
  - **이미지 갱신**: 클라이언트 타이머 폴링 대신, 서버에서 새 이미지가 캡처되었을 때 SSE 이벤트(`photo-ready`)를 발행하여 클라이언트가 그 시점에만 이미지를 요청하도록 이벤트 기반으로 전환합니다.
  - **장비 감시**: N.I.N.A. WebSocket 이벤트 및 PHD2 소켓 브로드캐스트를 활용하여 상태 변화 시에만 비동기 대기(`WaitToReadAsync`)가 깨어나도록 폴링을 점진적으로 제거합니다.

---

## 5. 결론 및 우선 조치 권장 순서

1. **1순위 (즉시 조치 권장)**:
   - `JarvisRing.module.css`에 `prefers-reduced-motion` 미디어 쿼리 추가.
   - `ConfirmDialog`, `PrepCenter`, `LiveView` 등 CSS 모듈에 하드코딩된 `rgb(...)` 색상을 `index.css` 토큰(`var(--scrim)`)으로 치환.
2. **2순위 (구조 리팩토링)**:
   - `EquipmentConnector.ConnectOneAsync`를 장비 종류별 헬퍼 메서드로 분리.
   - `EquipmentScreen.tsx`에서 물리 레이아웃 알고리즘(`useEquipmentLayout.ts`) 분리.
   - Win32 헬퍼(`Win32Windows.cs`)로 중복 P/Invoke 통합.
3. **3순위 (성능 안정화)**:
   - `SkyBackdrop` 캔버스를 정적 1회 렌더링으로 제한하고 배터리 소모 방지.
   - `RealImage` 및 장비 감시 루프의 1~2초 폴링을 SSE 이벤트 기반 통지로 일원화.
