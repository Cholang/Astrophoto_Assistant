using System.Text.Json.Serialization;
using Astro.Nina;
using Astro.Server.Assistant;
using Astro.Server.Engine;
using Astro.Server.Profiles;
using Astro.Server.Setup;
using Astro.Server.Sky;
using Microsoft.Extensions.Options;

namespace Astro.Server;

/// <summary>
/// 코어 서버. 단독 실행(Program.cs)과 데스크톱 창(Astro.Desktop) 안에서 똑같이 만들어 쓴다.
/// 화면(web/ 빌드 결과)은 wwwroot에서 제공한다.
/// </summary>
public static class AppServer
{
    public const string DefaultUrl = "http://localhost:5210";

    /// <param name="contentRoot">
    /// 설정 파일과 wwwroot를 찾을 폴더. 단독 실행(dotnet run)은 비워 두면 프로젝트 폴더를 쓰므로
    /// web/을 다시 빌드하면 바로 반영된다. 데스크톱 창은 자기 실행 폴더를 넘긴다.
    /// </param>
    public static WebApplication Build(string[] args, string? url = null, string? contentRoot = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            ContentRootPath = contentRoot,
        });
        if (url is not null) builder.WebHost.UseUrls(url);
        // AI API 키 등 비밀 값: 이 PC의 사용자 비밀 저장소에서 읽는다 (git·화면에 남지 않음).
        // 기본 설정은 개발 모드에서만 읽으므로 항상 읽도록 직접 추가. 환경 변수가 있으면 환경 변수가 이긴다.
        builder.Configuration.AddUserSecrets(typeof(AppServer).Assembly, optional: true);
        builder.Configuration.AddEnvironmentVariables();

        builder.Services.Configure<NinaOptions>(builder.Configuration.GetSection("Nina"));
        builder.Services.Configure<SetupOptions>(builder.Configuration.GetSection("Setup"));
        builder.Services.Configure<EquipmentOptions>(builder.Configuration.GetSection("Equipment"));
        builder.Services.AddHttpClient<NinaApiClient>((sp, http) =>
        {
            http.BaseAddress = new Uri(sp.GetRequiredService<IOptions<NinaOptions>>().Value.BaseUrl);
            // 요청별 대기 시간은 NinaApiClient가 정한다 (조회 5초, 장비 연결 60초). 여기는 그보다 넉넉한 안전선
            http.Timeout = NinaApiClient.ConnectTimeout + TimeSpan.FromSeconds(30);
        });
        builder.Services.Configure<NetworkOptions>(builder.Configuration.GetSection("Network"));
        builder.Services.AddHttpClient<InternetCheck>();
        builder.Services.AddTransient<SetupChecker>();
        builder.Services.AddTransient<EngineStarter>();
        builder.Services.AddSingleton<NinaWatcher>();
        builder.Services.AddTransient<EquipmentConnector>();
        builder.Services.AddSingleton<EquipmentSimulation>();
        builder.Services.AddSingleton<EquipmentChoices>();
        builder.Services.AddSingleton(new RigOverrides(builder.Configuration["App:DataDir"]));
        builder.Services.AddSingleton<LiveDevices>();
        builder.Services.AddSingleton(new OpticsStore(builder.Configuration["App:DataDir"]));
        builder.Services.AddTransient<RigSetup>();

        // 촬영 계획: 대상 목록(N.I.N.A. 데이터베이스), 오늘 밤 정보, AI 비서 (회사는 Assistant:Provider로 고름)
        builder.Services.Configure<AssistantOptions>(builder.Configuration.GetSection("Assistant"));
        builder.Services.AddHttpClient();
        builder.Services.AddSingleton(new DsoCatalog(builder.Configuration["Sky:NinaDatabase"] is { Length: > 0 } db ? db : null));
        builder.Services.AddSingleton<TonightService>();
        builder.Services.AddSingleton<PlanTools>();
        builder.Services.AddSingleton<ChatModelFactory>();
        builder.Services.AddSingleton<PlanAssistant>();
        // 촬영 준비: 지금은 모의 장비만 (실제 N.I.N.A.·PHD2·SharpCap 동작은 W4~W6에서 IPrepareDevices로 채운다)
        builder.Services.AddSingleton<Prepare.SimulatedPrepareDevices>();
        builder.Services.AddSingleton<Prepare.IPrepareDevices>(sp => sp.GetRequiredService<Prepare.SimulatedPrepareDevices>());
        builder.Services.AddSingleton<Prepare.PrepareRunner>();
        // 데이터 폴더(기본 %LOCALAPPDATA%\<product.json의 dataFolder>)는 설정 App:DataDir로 바꿀 수 있다 (테스트용).
        builder.Services.AddSingleton(new ProfileStore(builder.Configuration["App:DataDir"]));
        builder.Services.AddTransient<SiteService>();
        builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

        var app = builder.Build();
        app.UseDefaultFiles();
        app.UseStaticFiles();

        var api = app.MapGroup("/api");

        // 항목이 확인될 때마다 한 건씩 보낸다 (Server-Sent Events).
        api.MapGet("/setup/check", (SetupChecker checker, CancellationToken ct) =>
            TypedResults.ServerSentEvents(checker.RunAsync(ct), eventType: "check"));
        // N.I.N.A. 감시: 지금 상태를 먼저, 바뀔 때마다 (Running · Exited · NotResponding)
        api.MapGet("/nina/watch", (NinaWatcher watcher, CancellationToken ct) =>
            TypedResults.ServerSentEvents(watcher.WatchAsync(ct).Select(s => s.ToString()), eventType: "nina"));
        // [임시] 화면 설계용: N.I.N.A.가 꺼진 것처럼 (Equipment:Simulate일 때만)
        api.MapPost("/nina/simulate-exit", (NinaWatcher watcher, IOptions<EquipmentOptions> eq) =>
        {
            if (!eq.Value.Simulate) return Results.NotFound();
            watcher.SimulateExit();
            return Results.NoContent();
        });
        api.MapGet("/engine/start", (EngineStarter starter, CancellationToken ct) =>
            TypedResults.ServerSentEvents(starter.RunAsync(ct), eventType: "check"));

        // 전체를 한 번에 (프로필을 고른 직후: 모두 설치돼 있으면 0단계 화면을 건너뛴다)
        api.MapGet("/setup/status", (SetupChecker checker) => checker.CheckAll());

        // 한 항목만 다시 확인 (쉐브론 안의 새로고침)
        api.MapGet("/setup/check/{id}", (string id, SetupChecker checker) =>
            checker.CheckOne(id) is { } result ? Results.Ok(result) : Results.NotFound());

        // 장비 연결: 연결할 장비 목록(칸을 미리 그리기 용) → 차례로 연결 → 한 장비만 다시 연결
        api.MapGet("/equipment/plan", (EquipmentConnector connector, CancellationToken ct) => connector.PlanAsync(ct));
        api.MapGet("/equipment/connect", (EquipmentConnector connector, CancellationToken ct) =>
            TypedResults.ServerSentEvents(connector.RunAsync(ct), eventType: "check"));
        // simulateFail=true: [임시] 화면 설계용 실패 만들기 (Equipment:Simulate가 켜져 있을 때만 동작)
        api.MapGet("/equipment/connect/{id}", async (string id, bool? simulateFail, EquipmentConnector connector, CancellationToken ct) =>
            await connector.RetryAsync(id, simulateFail ?? false, ct) is { } result ? Results.Ok(result) : Results.NotFound());

        // 장비 변경 모드: 설치된 드라이버 목록, 장비 고르기·제거 (deviceId가 null이면 제거)
        api.MapGet("/equipment/devices/{kind}", async (string kind, RigSetup rig, CancellationToken ct) =>
            await rig.DevicesAsync(kind, ct) is { } list ? Results.Ok(list) : Results.NotFound());
        api.MapPost("/equipment/select", async (RigSelect input, RigSetup rig, CancellationToken ct) =>
            await rig.SelectAsync(input.Kind, input.DeviceId, input.Name, ct) is { } result ? Results.Ok(result) : Results.BadRequest());

        // 망원경 목록 (장비 변경 모드의 망원경 원). 고르기는 /equipment/select (kind=scope)
        api.MapGet("/optics", (OpticsStore optics) =>
        {
            var (scopes, currentId) = optics.List();
            return new { scopes = scopes.Select(ScopeView), currentId, max = OpticsStore.Max };
        });
        api.MapPost("/optics", (NewScope input, OpticsStore optics) =>
        {
            var (scope, error) = optics.Add(input);
            return scope is null ? Results.BadRequest(new { error }) : Results.Ok(ScopeView(scope));
        });
        api.MapPut("/optics/{id}", (string id, NewScope input, OpticsStore optics) =>
        {
            var (scope, error) = optics.Edit(id, input);
            return scope is null ? Results.BadRequest(new { error }) : Results.Ok(ScopeView(scope));
        });
        api.MapDelete("/optics/{id}", (string id, OpticsStore optics) =>
            optics.Remove(id) is { } error ? Results.BadRequest(new { error }) : Results.NoContent());

        // 준필수 장비(포커서·가이더)를 "없이 진행"
        api.MapPost("/equipment/skip/{id}", async (string id, EquipmentConnector connector, CancellationToken ct) =>
            await connector.GoWithoutAsync(id, ct) is { } result ? Results.Ok(result) : Results.NotFound());

        // 관측지: 지금 N.I.N.A. 값 / 적용(N.I.N.A. 저장 → 적도의에 보내기, 단계별) / 좌표의 고도
        api.MapGet("/site/current", async (SiteService sites, CancellationToken ct) =>
            await sites.CurrentAsync(ct) is { } site ? Results.Ok(site) : Results.NoContent());
        api.MapGet("/site/apply", (double lat, double lon, double? elev, SiteService sites, CancellationToken ct) =>
            TypedResults.ServerSentEvents(ApplySite(sites, lat, lon, elev, ct), eventType: "check"));
        api.MapGet("/site/elevation", async (double lat, double lon, SiteService sites, CancellationToken ct) =>
            Results.Ok(new { elevation = await sites.ElevationAsync(lat, lon, ct) }));

        // 화면 설정: 카카오맵 JavaScript 키 (사용자 비밀 저장소 Map:KakaoJavaScriptKey. 없으면 지도 없이)
        api.MapGet("/config", (IConfiguration config) => new { kakaoKey = config["Map:KakaoJavaScriptKey"] is { Length: > 0 } k ? k : null });

        MapPlan(api.MapGroup("/plan"));
        MapPrepare(api.MapGroup("/prepare"));
        MapProfiles(api.MapGroup("/profiles"));

        app.MapFallbackToFile("index.html");
        return app;
    }

    private static void MapPrepare(RouteGroupBuilder prepare)
    {
        // 확정 계획으로 준비 시작 (같은 계획이면 하던 곳에서 그대로)
        prepare.MapPost("/start", (Prepare.PrepareRunner runner) =>
            runner.Start() is { } problem ? Results.BadRequest(new { error = problem }) : Results.Ok(runner.View()));
        prepare.MapGet("/state", (Prepare.PrepareRunner runner) => runner.View());
        // 상태가 바뀔 때마다 (Server-Sent Events)
        prepare.MapGet("/watch", (Prepare.PrepareRunner runner, CancellationToken ct) =>
            TypedResults.ServerSentEvents(runner.WatchAsync(ct), eventType: "state"));
        // 칸의 버튼
        prepare.MapPost("/act", (PrepAct input, Prepare.PrepareRunner runner) =>
            runner.Act(input.Step, input.Action) is { } problem ? Results.BadRequest(new { error = problem }) : Results.NoContent());

        // [임시] 화면 확인용: 다음 동작 하나를 실패시키기, 대상이 보인다고 가정하기
        prepare.MapPost("/sim/fail-next/{step}", (string step, Prepare.SimulatedPrepareDevices sim) => { sim.FailNext(step); return Results.NoContent(); });
        prepare.MapPost("/sim/ignore-altitude", (Prepare.PrepareRunner runner) => { runner.IgnoreAltitude(); return Results.NoContent(); });
    }

    private sealed record PrepAct(string Step, string Action);

    private static void MapPlan(RouteGroupBuilder plan)
    {
        // 화면을 열 때: 대화·계획·그래프를 한 번에 (처음이면 오늘 밤 정보를 준비하고 인사를 만든다)
        plan.MapGet("/state", async (PlanAssistant assistant, DsoCatalog catalog, CancellationToken ct) =>
        {
            if (await assistant.EnsureStartedAsync(ct) is { } problem) return Results.Ok(new { error = problem });
            var night = assistant.Night!;
            return Results.Ok(new
            {
                messages = assistant.Display,
                plan = PlanView.From(assistant.Plan, night),
                chart = NightChart.Build(night, assistant.Plan, catalog),
                catalogAvailable = catalog.Available,
                cloudsAvailable = night.Clouds.Count > 0,
            });
        });

        plan.MapGet("/chart", (PlanAssistant assistant, DsoCatalog catalog) =>
            assistant.Night is { } night ? Results.Ok(NightChart.Build(night, assistant.Plan, catalog)) : Results.NotFound());

        // 사용자 말 하나 → 글 조각·계획 변화·선택지를 차례로 (Server-Sent Events)
        plan.MapPost("/chat", (ChatInput input, PlanAssistant assistant, CancellationToken ct) =>
            TypedResults.ServerSentEvents(assistant.SendAsync(input.Text, ct)));

        plan.MapPost("/reset", (PlanAssistant assistant) => { assistant.Reset(); return Results.NoContent(); });

        // "이 계획으로 준비 시작": 계획을 실행 값으로 확정한다 (CX-PLAN-07). 실행할 수 없는 계획이면 이유
        plan.MapPost("/confirm", (PlanAssistant assistant) =>
            assistant.Confirm() is { } problem ? Results.BadRequest(new { error = problem }) : Results.Ok(assistant.Confirmed));
        // 준비 단계가 읽는 확정 계획
        plan.MapGet("/confirmed", (PlanAssistant assistant) =>
            assistant.Confirmed is { } p ? Results.Ok(p) : Results.NotFound());
    }

    /// <summary>고도를 모르면(화면이 안 보냄) 먼저 조회한 뒤 적용</summary>
    private static async IAsyncEnumerable<Astro.Core.Setup.CheckResult> ApplySite(SiteService sites, double lat, double lon, double? elev,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var elevation = elev ?? await sites.ElevationAsync(lat, lon, ct);
        await foreach (var step in sites.ApplyAsync(lat, lon, elevation, ct)) yield return step;
    }

    private static object ScopeView(Scope s) => new
    {
        s.Id, s.Name, s.FocalLength, s.Aperture, s.FocalRatio, s.Reducer, s.Flattener,
        effectiveFocalLength = Math.Round(s.EffectiveFocalLength, 1),
        effectiveFocalRatio = Math.Round(s.EffectiveFocalRatio, 2),
        s.Optics,
    };

    private sealed record ChatInput(string Text);

    private sealed record RigSelect(string Kind, string? DeviceId, string? Name);

    private static void MapProfiles(RouteGroupBuilder profiles)
    {
        profiles.MapGet("/", (ProfileStore store) => store.List());

        profiles.MapPost("/", (NewProfile input, ProfileStore store) =>
        {
            var (profile, error) = store.Create(input);
            return profile is null
                ? Results.BadRequest(new { error })
                : Results.Created($"/api/profiles/{profile.Id}", profile);
        });

        profiles.MapPost("/{id}/select", (string id, ProfileStore store, PlanAssistant plan) =>
        {
            if (!store.Select(id)) return Results.NotFound();
            plan.ProfileSelected(id); // 다른 프로필이면 계획 대화를 새로 (CX-PLAN-06)
            return Results.NoContent();
        });

        // 별명·메모 고치기 (프로필 선택 화면)
        profiles.MapPut("/{id}", (string id, NewProfile input, ProfileStore store) =>
        {
            var (profile, error) = store.Edit(id, input);
            return profile is null ? Results.BadRequest(new { error }) : Results.Ok(profile);
        });

        // 관측지 추가(고도를 안 보내면 좌표로 조회) · 삭제. 바뀐 프로필을 돌려준다
        profiles.MapPost("/{id}/sites", async (string id, NewSite input, ProfileStore store, SiteService sites, CancellationToken ct) =>
        {
            var elevation = input.Elevation ?? await sites.ElevationAsync(input.Latitude, input.Longitude, ct);
            var (profile, error) = store.AddSite(id, input.Name ?? "", input.Latitude, input.Longitude, elevation);
            return profile is null ? Results.BadRequest(new { error }) : Results.Ok(profile);
        });
        profiles.MapPut("/{id}/sites/{siteId}", (string id, string siteId, NewSite input, ProfileStore store) =>
        {
            var (profile, error) = store.RenameSite(id, siteId, input.Name ?? "");
            return profile is null ? Results.BadRequest(new { error }) : Results.Ok(profile);
        });
        profiles.MapDelete("/{id}/sites/{siteId}",(string id, string siteId, ProfileStore store) =>
            store.RemoveSite(id, siteId) is { } profile ? Results.Ok(profile) : Results.NotFound());

        // 화면이 가운데를 정사각형으로 잘라 256×256 WebP로 줄여서 보낸다.
        profiles.MapPut("/{id}/image", async (string id, HttpRequest request, ProfileStore store) =>
        {
            if (request.ContentType != "image/webp") return Results.BadRequest(new { error = "WebP 이미지만 받습니다." });
            if (request.ContentLength > ProfileStore.ImageMaxBytes) return Results.BadRequest(new { error = "이미지가 너무 큽니다." });
            using var buffer = new MemoryStream();
            await request.Body.CopyToAsync(buffer);
            if (buffer.Length is 0 or > ProfileStore.ImageMaxBytes) return Results.BadRequest(new { error = "이미지를 읽지 못했습니다." });
            return store.SaveImage(id, buffer.ToArray()) ? Results.NoContent() : Results.NotFound();
        });

        profiles.MapGet("/{id}/image", (string id, ProfileStore store) =>
            store.ImageFile(id) is { } path ? Results.File(path, "image/webp") : Results.NotFound());
    }
}
