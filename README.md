# AA (Astrophoto Assistant)

N.I.N.A. 위에서 동작하는 천체사진 촬영 비서. 개인용 프로토타입.

- 기획: [nina-ai-assistant-plan.md](nina-ai-assistant-plan.md)
- 앱 흐름: [flow.mmd](flow.mmd)
- 구조와 온보딩: [onboarding-and-architecture.md](onboarding-and-architecture.md)
- NINA 사전 준비: [nina-prerequisites.md](nina-prerequisites.md)
- API 대응표: [api-coverage.md](api-coverage.md)

## 구성

| 폴더 | 역할 |
|---|---|
| `src/Astro.Core` | 순수 로직. NINA 플러그인과 공유할 수 있게 .NET 8·10 동시 빌드 |
| `src/Astro.Nina` | NINA Advanced API 클라이언트 |
| `src/Astro.Server` | 코어 서버 (ASP.NET Core). 화면과 API를 `http://localhost:5210`에서 제공 |
| `src/Astro.Desktop` | 데스크톱 창 (WPF + WebView2). 코어 서버를 같은 프로세스에서 실행. 실행 파일 이름 `AA.exe` |
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
| `LaunchIfNotRunning` | `true` | 0단계 체크에서 NINA가 꺼져 있으면 AA가 켬 |
| `StartupTimeoutSeconds` | `90` | NINA를 켠 뒤 API 응답을 기다리는 시간 |

`Setup` 항목:

| 키 | 기본값 | 설명 |
|---|---|---|
| `SimulateMissing` | `true` **[임시]** | 화면 설계용. 실제 설치 여부와 관계없이 0단계 5개를 모두 "설치되어 있지 않음"으로 보고. 화면의 임시 "○○ 설치 완료하기" 버튼과 함께 쓴다. 설계가 끝나면 `false` |

`AA:DataDir`: 프로필 등 데이터 폴더 (기본 `%LOCALAPPDATA%\AA`). 테스트할 때 다른 폴더로 바꾼다.

환경 변수로도 바꿀 수 있다. 예: `Nina__LaunchIfNotRunning=false`, `AA__DataDir=D:\temp\aa-data`
