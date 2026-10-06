# AA (Astrophoto Assistant) — 작업 지침

**답변은 항상 한국어로 한다.** 코드·도구 출력이 영어여도, 대화가 압축된 직후에도 예외 없이 한국어로 답할 것.

프로젝트 개요와 구성은 README.md를 볼 것.

## 제품 이름

- 제품 이름(현재 가칭 "AA")은 `product.json` 한 곳에서만 정한다. 코드·화면 문구에 이름을 직접 쓰지 말 것.
- 웹은 `web/src/product.ts`의 `PRODUCT`, .NET은 `Astro.Core.Product`를 쓴다.
- 문장 속 조사는 `PRODUCT.ga/reul/neun/wa`, `Product.Ga/Reul/Neun/Wa`로 (이름이 바뀌면 조사도 맞춰 바뀜).

## UI 작업 규칙

- UI(`web/`, `src/Astro.Desktop`의 창 모양)를 만들거나 수정하기 전에 반드시 DESIGN.md를 읽을 것.
- DESIGN.md와 디자인 스킬(`.claude/skills/frontend-design` 등)의 지시가 충돌하면 DESIGN.md를 따를 것. 스킬은 AI 기본값을 피하는 데 쓰고, 방향은 DESIGN.md가 정한다.
- 색·글꼴·간격은 CSS 변수(토큰)로만 쓰고, 버튼·단계 레일·상태 줄·진단 카드는 공통 컴포넌트로 만들어 재사용할 것.
- 내가 요청한 수정이 다른 화면에도 적용될 성격이면, 작업 후 DESIGN.md에 추가할 문구를 제안할 것.
- `web/`을 고친 뒤 데스크톱 창에서 확인하려면 `web/`에서 `npm run build`가 필요하다.

## 실행 중인 앱

- 수정 요청을 받아 고치기 시작할 때, 실행 중인 AA(AA.exe·Astro.Server)를 먼저 종료한다 (빌드 파일 잠김 방지, 사용자가 새 빌드로 다시 켜게).
- 내가 테스트용으로 띄운 서버(포트 5211 등)는 테스트가 끝나면 바로 끈다.

## 커밋 규칙

- 커밋할 때마다 [HISTORY.md](HISTORY.md) 맨 위에 항목을 하나 추가하고 같은 커밋에 넣을 것. 다른 AI(GPT Codex 등)가 리뷰할 때 먼저 읽는다.
- 형식: 날짜 · 커밋 제목 / **요청**(사용자가 무엇을 요구했나) / **변경**(무엇을 바꿨나 — 파일·함수 이름 위주) / **확인**(어떻게 확인했나, 확인 못 한 것은 그렇다고) / **남은 것**(있으면). 짧게, diff에 이미 보이는 세부는 쓰지 않는다.
- 커밋 해시는 커밋 뒤에야 알 수 있으므로, 다음 커밋 때 바로 앞 항목 제목에 해시를 붙인다.
- HISTORY.md는 Claude와 Codex가 함께 쓴다. 내 항목 제목에는 `[Claude]`, Codex 항목은 `[Codex]`로 표시한다. 다른 쪽 항목은 바로 앞 항목에 해시를 붙이는 것 말고는 고치지 않는다.

## 문서 위치

- 저장소 맨 위에는 README.md · CLAUDE.md · AGENTS.md · DESIGN.md · HISTORY.md만 둔다.
- 기획·조사 자료는 `docs/`(기획서, 구조와 온보딩, NINA 사전 준비, API 대응표), Codex와 주고받는 문서는 `docs/codex/`, 대화 기록은 `conversations/`.
- 새 문서가 필요하면 만들어도 되지만 위 위치에 맞추고, 기존 문서에 절을 더하는 것으로 충분한지 먼저 본다. 검토가 끝난 임시 문서(예: `docs/PREPARE_SCREEN_CONTENT.md`)는 내용을 DESIGN.md 등에 합치고 지운다.

## 리뷰 규칙 (Claude 구현 → GPT Codex 리뷰)

- Codex의 리뷰 결과는 [docs/codex/REVIEW_CODEX.md](docs/codex/REVIEW_CODEX.md)에 있다. 사용자가 "리뷰 검토해 줘", "리뷰했어" 등으로 말하면 이 파일을 읽는다.
- 리뷰를 바로 반영하지 않는다. 항목마다 내 판단(동의·반대·이미 처리됨·사용자 결정 필요)과 근거를 사용자에게 먼저 말하고, 논의해서 정한 것만 적용한다.
- 적용한 뒤에는 HISTORY.md 항목에 어떤 리뷰(ID)를 어떻게 적용했는지, 적용하지 않은 것은 이유를 남긴다.
- REVIEW_CODEX.md에는 끝의 "처리 및 재리뷰 기록" 표만 채운다: 항목마다 한 줄(상태 — 반영 / 반영 안 함 / 후속 단계 / 결정 대기, 근거 한 문장, 커밋 해시). 자세한 변경은 HISTORY.md에만 쓰고, Codex가 쓴 리뷰 본문과 재리뷰 칸은 고치지 않는다.
