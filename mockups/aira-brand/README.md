# AIRA 벡터 로고 시안

- `aira-logo.svg`: 투명 배경 부팅 로고. 한글 이름·그림은 경로, 영문 설명은 Segoe UI/Arial 텍스트.
- `aira-icon.svg`: 정사각형 앱 아이콘 원본. 작은 크기 비교 후 ICO 변환 예정.
- `preview.html`: 라이트·다크·촬영용 적색 및 아이콘 크기 비교.

SVG 루트의 `data-theme`을 `light`, `dark`, `night`로 바꾸면 색상이 바뀐다. 기본은 dark.
앱에서 동적으로 바꾸려면 SVG를 인라인으로 넣고 속성을 변경한다. img src로 불러온 SVG 내부에는 부모 문서의 CSS 변수가 전파되지 않는다.

PNG 시안을 기반으로 직접 재구성한 벡터이므로 손글씨와 도형 비율에 차이가 있다. 원본 PNG는 변경하지 않았다. 실제 앱에는 아직 연결하지 않았다.
