using System.Text.Json.Serialization;

namespace Astro.Server.Prepare.Flow;

// 화면에 보내는 준비 상태 (DESIGN.md "촬영 준비" 화면 영역: 안내 · 중앙 정보 · 하늘 화면 · 진행 표시).
// 화면은 이것만 보고 그린다. 문장은 서버가 만들고, 숫자·단위·판정 근거는 따로 보존한다 (CX-PREP-IMPL-04).

[JsonConverter(typeof(JsonStringEnumConverter<PrepTaskStatus>))]
public enum PrepTaskStatus { Pending, Running, Waiting, Done, Failed, Skipped, NeedsRecheck }

[JsonConverter(typeof(JsonStringEnumConverter<Tone>))]
public enum Tone { None, Busy, Ok, Warn, Fail }

[JsonConverter(typeof(JsonStringEnumConverter<Freshness>))]
public enum Freshness { Fresh, Stale, Unverified }

/// <summary>버튼. Primary는 주 버튼(하나만)</summary>
public sealed record PrepAction(string Id, string Label, bool Primary = false);

/// <summary>세부 과정 하나 (진행 표시의 작업 아래 점)</summary>
public sealed record SubStepView(string Id, string Label, PrepTaskStatus Status);

/// <summary>안내: 제목 줄(작업 — 세부 과정 또는 결과 말) + 안내 문장. 숫자는 넣지 않는다</summary>
public sealed record GuideView(string Title, string Text);

/// <summary>
/// 측정값 칸. Kind로 작업별 그림을 고르고(polar-offset, steps, hfr, guiding …), 값은 Values에 숫자로 보존한다.
/// Big·Caption은 서버가 만든 표시 문구. ObservedAt·Freshness로 오래된 값을 정상 값처럼 보이지 않게 한다.
/// </summary>
public sealed record ReadoutView(
    string Kind, string Big, string Caption, Tone Tone,
    IReadOnlyDictionary<string, double> Values, DateTimeOffset ObservedAt, Freshness Freshness);

/// <summary>상태 줄: 진행·경고·실패 문구 (중앙 정보, 버튼 바로 위)</summary>
public sealed record StatusLine(string Text, Tone Tone);

/// <summary>중앙 정보: 측정값 → 상태 줄 → 버튼 (순서·자리 고정)</summary>
public sealed record CenterView(ReadoutView? Readout, StatusLine? Status, IReadOnlyList<PrepAction> Actions);

/// <summary>하늘 화면: none | sharpcap | guide-image | sky-map | focus-curve | solved-photo | test-photo. 이미지는 Url, 그림 데이터는 Data</summary>
public sealed record LiveView(string Kind, string? Url, object? Data, DateTimeOffset ObservedAt);

/// <summary>진행 표시의 작업 한 줄. Result = 끝난 작업의 결과 한 줄</summary>
public sealed record TaskRowView(string Id, string Title, PrepTaskStatus Status, string? Result);

/// <summary>지금 작업 (화면 가운데 전체)</summary>
public sealed record CurrentView(
    string TaskId, int RunId, IReadOnlyList<SubStepView> SubSteps,
    GuideView Guide, CenterView Center, LiveView? Live);

/// <summary>준비 화면 전체. Ready = 사용자가 "촬영 시작"을 눌렀다, Simulated = 모의 장비(화면은 그림으로 흉내, 아니면 Live.Url의 실제 이미지)</summary>
public sealed record PrepView(bool Started, IReadOnlyList<TaskRowView> Tasks, CurrentView? Current, bool Ready, int Version, bool Simulated = true);
