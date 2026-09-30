# AA (Astrophoto Assistant) — 작업 지침

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

## 커밋 규칙

- 커밋할 때마다 [HISTORY.md](HISTORY.md) 맨 위에 항목을 하나 추가하고 같은 커밋에 넣을 것. 다른 AI(GPT Codex 등)가 리뷰할 때 먼저 읽는다.
- 형식: 날짜 · 커밋 제목 / **요청**(사용자가 무엇을 요구했나) / **변경**(무엇을 바꿨나 — 파일·함수 이름 위주) / **확인**(어떻게 확인했나, 확인 못 한 것은 그렇다고) / **남은 것**(있으면). 짧게, diff에 이미 보이는 세부는 쓰지 않는다.
- 커밋 해시는 커밋 뒤에야 알 수 있으므로, 다음 커밋 때 바로 앞 항목 제목에 해시를 붙인다.
