# NINA 사전 준비 체크리스트

> 작성일: 2026-09-28
> 용도: 앱 첫 실행 시 "설치 안내" 화면과 "NINA 준비 확인" 단계의 기준

우리 앱은 NINA 위에서 동작한다. 아래 항목은 **NINA 쪽에 한 번 준비해 두는 것**이고, 우리 앱은 이걸 전제로 매 세션을 진행한다.

---

## 1. 설치 (앱 첫 화면에서 링크 제공)

| 순서 | 항목 | 구분 | 링크 | 비고 |
|---|---|---|---|---|
| 1 | ASCOM Platform 7 | 필수 | https://ascom-standards.org/Downloads/Index.htm | 대부분의 장비 드라이버가 의존. NINA보다 먼저 설치 |
| 2 | 장비 드라이버 | 필수 | 각 제조사 | 카메라(네이티브 드라이버), 마운트(ASCOM/Alpaca), 포커서, 필터휠 |
| 3 | N.I.N.A. | 필수 | https://nighttime-imaging.eu/download/ | |
| 4 | Advanced API 플러그인 | 필수 | NINA → Plugins 탭 → "Advanced API" 설치 | 설치 후 Options → Advanced API에서 활성화, 포트(기본 1888) 확인 |
| 5 | ASTAP + 별 데이터베이스 | 필수 | https://www.hnsky.org/astap.htm | 플레이트 솔버. 화각에 맞는 DB(D50 등) 함께 설치 |
| 6 | PHD2 | 사실상 필수 | https://openphdguiding.org/downloads/ | 가이딩·디더링·가이드 RMS 기록 |
| 7 | WandererEmpire | 내 장비 | https://www.wandererastro.com | Wanderer Box Plus V3의 ASCOM 드라이버 포함 |
| 8 | Three Point Polar Alignment (TPPA) | 권장 | NINA → Plugins 탭 | 극축 정렬. Advanced API가 TPPA 전용 채널을 지원 (TPPA 2.2.4.1 이상) |

**앱에서 자동 확인 가능한 것**: NINA 실행 여부와 API 응답(`/version`), 설치된 NINA 플러그인 목록(`/application/plugins`), 장비별 드라이버 목록(`/equipment/*/list-devices`).
**자동 확인이 어려운 것**: ASCOM Platform 설치 여부, ASTAP DB 설치 여부 → 첫 플레이트 솔빙 테스트로 간접 확인.

---

## 2. NINA 프로필 설정

`/profile/show?active=true`로 읽을 수 있고, `/profile/change-value`로 일부를 앱에서 바꿀 수 있다 (실제 동작은 API 점검에서 확인).

### 필수: 없으면 앱이 동작하지 않음
| 항목 | 이유 |
|---|---|
| 망원경 초점거리, 카메라 픽셀 크기 | 이미지 스케일 → 플레이트 솔빙 |
| 플레이트 솔버 = ASTAP, 실행 파일 경로 | goto 후 중심 맞추기, 플립 후 재정렬 |
| 관측지 위도·경도·고도 | 대상 고도, 자오선 통과 시각 계산 |
| 이미지 저장 경로·파일 이름 패턴 | 세션 기록이 FITS 파일을 읽음 |

### 사실상 필수: 없으면 무인 촬영이 불안정
| 항목 | 이유 |
|---|---|
| 가이더 = PHD2, 디더 설정(픽셀, settle 조건) | 가이딩·디더링 |
| 오토포커스 설정(스텝 크기, 노출, 측정 점 수) | 기본값으로는 실패하기 쉬움. 장비별 튜닝 필요 |
| 자오선 반전 설정(통과 후 대기 시간, 플립 후 재정렬) | 밤새 촬영 시 거의 반드시 발생 |
| 카메라 기본 gain·offset·냉각 온도 | 계획 카드 기본값 |
| 파킹 위치, 마운트 한계 | 종료 시 안전 정지 |

### 선택
필터 이름·필터별 초점 오프셋, 지평선 파일(`/profile/horizon`), 돔, 세이프티 모니터.

---

## 3. Wanderer Box Plus V3 (내 장비)

- **역할**: 전원 분배(DC 출력), 이슬 방지 히터(PWM), USB 허브, 온도·습도 센서
- **NINA 연결**: WandererEmpire 설치 시 ASCOM 드라이버가 함께 설치됨
  - 전원·히터 → NINA의 **Switch** 장비로 "WandererBoxes All-in-one" 드라이버 선택
  - 온도·습도 → NINA의 **Weather(Observing Conditions)** 장비로 연결
  - ASCOM으로 연결하면 NINA와 WandererEmpire의 제어 상태가 동기화됨
- **우리 앱에서 제어**: Advanced API로
  - 상태 읽기: `/equipment/switch/info` (포트별 이름·값)
  - 켜기/끄기·히터 세기: `/equipment/switch/set?index=<포트 번호>&value=<값>`
  - 온도·습도: `/equipment/weather/info`
- **플로우 연결**: 장비 선택 목록의 "허브"·"열선" 항목이 이 장비. 이슬점(온도·습도로 계산)에 따라 히터 세기를 코드가 조절하는 기능으로 확장 가능
- ⚠️ 포트 번호(index)와 실제 연결 장비의 대응은 API 점검에서 확인 필요
