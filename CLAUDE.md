# AA (Astrophoto Assistant) — 작업 지침

프로젝트 개요와 구성은 README.md를 볼 것.

## UI 작업 규칙

- UI(`web/`, `src/Astro.Desktop`의 창 모양)를 만들거나 수정하기 전에 반드시 DESIGN.md를 읽을 것.
- DESIGN.md와 디자인 스킬(`.claude/skills/frontend-design` 등)의 지시가 충돌하면 DESIGN.md를 따를 것. 스킬은 AI 기본값을 피하는 데 쓰고, 방향은 DESIGN.md가 정한다.
- 색·글꼴·간격은 CSS 변수(토큰)로만 쓰고, 버튼·단계 레일·상태 줄·진단 카드는 공통 컴포넌트로 만들어 재사용할 것.
- 내가 요청한 수정이 다른 화면에도 적용될 성격이면, 작업 후 DESIGN.md에 추가할 문구를 제안할 것.
- `web/`을 고친 뒤 데스크톱 창에서 확인하려면 `web/`에서 `npm run build`가 필요하다.
