# AA (Astrophoto Assistant) — 가칭

N.I.N.A. 위에서 동작하는 천체사진 촬영 비서. 개인용 프로토타입.

## 제품 이름 바꾸기

제품 이름은 맨 위의 [`product.json`](product.json) **한 곳에서만** 정한다. 코드에는 이름을 직접 쓰지 않는다.

| 키 | 쓰이는 곳 |
|---|---|
| `name` | 화면(부팅 로고, 상태 줄, 문장 속 이름), 브라우저 탭 제목, 창 제목, 실행 파일 이름(`<name>.exe`) |
| `fullName` | 영문 전체 이름 |
| `tagline` | 부팅 화면·창 제목의 한 줄 설명 |
| `dataFolder` | 데이터 폴더 `%LOCALAPPDATA%\<dataFolder>`. **이름을 바꿔도 이 값은 그대로 두기** (바꾸면 기존 프로필을 못 찾음) |

- 바꾼 뒤 `web/`에서 `npm run build`, 그리고 `dotnet build`를 다시 하면 적용된다
- 문장 속 조사(이/가, 을/를, 은/는, 과/와)는 이름의 마지막 글자에 맞춰 자동으로 바뀐다. 영문·숫자는 읽는 소리로 판단 (예: "AA가", "AL이")
- 코드에서는 웹 `PRODUCT`(`web/src/product.ts`), .NET `Astro.Core.Product`를 쓴다

- 기획: [nina-ai-assistant-plan.md](nina-ai-assistant-plan.md)
- 앱 흐름: [flow.mmd](flow.mmd)
- 구조와 온보딩: [onboarding-and-architecture.md](onboarding-and-architecture.md)
- NINA 사전 준비: [nina-prerequisites.md](nina-prerequisites.md)
- API 대응표: [api-coverage.md](api-coverage.md)
- 화면 시안: [mockups/aa-screens.html](mockups/aa-screens.html) (브라우저로 열기)
- 대화 기록: [conversations/](conversations/) — 새 세션은 가장 최근 파일의 "다음 세션에서 이어서 하려면"부터

## 구성

| 폴더 | 역할 |
|---|---|
| `src/Astro.Core` | 순수 로직. NINA 플러그인과 공유할 수 있게 .NET 8·10 동시 빌드 |
| `src/Astro.Nina` | NINA Advanced API 클라이언트 |
| `src/Astro.Server` | 코어 서버 (ASP.NET Core). 화면과 API를 `http://localhost:5210`에서 제공 |
| `src/Astro.Desktop` | 데스크톱 창 (WPF + WebView2). 코어 서버를 같은 프로세스에서 실행. 실행 파일 이름 `<product.json의 name>.exe` |
| `web/` | 화면 (React + TypeScript). 빌드 결과는 `src/Astro.Server/wwwroot`로 |
| `tools/api-check.mjs` | NINA API 읽기 전용 점검 스크립트 |

## 필요한 도구

- .NET 10 SDK
- Node.js LTS

## 실행

처음 한 번, 그리고 `web/`을 고친 뒤:

```
cd web
npm install
npm run build
```

데스크톱 창으로 실행:

```
dotnet run --project src/Astro.Desktop
```

또는 서버만 실행하고 브라우저에서 `http://localhost:5210` 열기:

```
dotnet run --project src/Astro.Server
```

화면을 고치면서 볼 때는 서버를 켜 둔 채로 `web/`에서 `npm run dev`를 실행한다 (`/api`는 서버로 넘어감).

## 설정

`src/Astro.Server/appsettings.json`의 `Nina` 항목:

| 키 | 기본값 | 설명 |
|---|---|---|
| `BaseUrl` | `http://localhost:1888/v2/api/` | Advanced API 주소 |
| `ExePath` | (비움) | NINA.exe 경로. 비우면 자동으로 찾음 |
| `LaunchIfNotRunning` | `true` | 1단계(엔진 켜기)에서 NINA가 꺼져 있으면 직접 켬 |
| `StartupTimeoutSeconds` | `90` | NINA를 켠 뒤 API 응답을 기다리는 시간 |

`Setup` 항목:

| 키 | 기본값 | 설명 |
|---|---|---|
| `SimulateMissing` | `true` **[임시]** | 화면 설계용. 실제 설치 여부와 관계없이 0단계 5개를 모두 "설치되어 있지 않음"으로 보고. 화면의 임시 "○○ 설치 완료하기" 버튼과 함께 쓴다. 설계가 끝나면 `false` |

`Equipment` 항목:

| 키 | 기본값 | 설명 |
|---|---|---|
| `Simulate` | `true` **[임시]** | 장비 없이 개발할 때. 실제 연결은 하지 않고 N.I.N.A. 프로필의 장비로 연결 과정을 흉내 낸다. 장비를 연결할 수 있으면 `false` — 실제 연결 경로는 아직 실기 미검증 |
| `SimulateFailing` | `["*"]` **[임시]** | 시뮬레이션에서 연결 실패로 시작할 장비 (`switch`, `mount`, `camera`, `focuser`, `guider`, `*`=전부). 화면에 들어올 때마다 이 상태로 시작하고, 장비별 "다시 연결"을 누르면 그 장비는 성공한다. `[]`이면 모두 바로 연결됨 |

`App:DataDir`: 프로필 등 데이터 폴더 (기본 `%LOCALAPPDATA%\<product.json의 dataFolder>`). 테스트할 때 다른 폴더로 바꾼다.

환경 변수로도 바꿀 수 있다. 예: `Nina__LaunchIfNotRunning=false`, `App__DataDir=D:\temp\test-data`
