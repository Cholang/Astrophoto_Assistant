using System.Text.Json.Serialization;
using Astro.Nina;
using Astro.Server.Engine;
using Astro.Server.Profiles;
using Astro.Server.Setup;
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

        builder.Services.Configure<NinaOptions>(builder.Configuration.GetSection("Nina"));
        builder.Services.Configure<SetupOptions>(builder.Configuration.GetSection("Setup"));
        builder.Services.Configure<EquipmentOptions>(builder.Configuration.GetSection("Equipment"));
        builder.Services.AddHttpClient<NinaApiClient>((sp, http) =>
        {
            http.BaseAddress = new Uri(sp.GetRequiredService<IOptions<NinaOptions>>().Value.BaseUrl);
            http.Timeout = TimeSpan.FromSeconds(5);
        });
        builder.Services.AddTransient<SetupChecker>();
        builder.Services.AddTransient<EngineStarter>();
        builder.Services.AddTransient<EquipmentConnector>();
        builder.Services.AddSingleton<EquipmentSimulation>();
        // 데이터 폴더(기본 %LOCALAPPDATA%\<product.json의 dataFolder>)는 설정 App:DataDir로 바꿀 수 있다 (테스트용).
        builder.Services.AddSingleton(new ProfileStore(builder.Configuration["App:DataDir"]));
        builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

        var app = builder.Build();
        app.UseDefaultFiles();
        app.UseStaticFiles();

        var api = app.MapGroup("/api");

        // 항목이 확인될 때마다 한 건씩 보낸다 (Server-Sent Events).
        api.MapGet("/setup/check", (SetupChecker checker, CancellationToken ct) =>
            TypedResults.ServerSentEvents(checker.RunAsync(ct), eventType: "check"));
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

        MapProfiles(api.MapGroup("/profiles"));

        app.MapFallbackToFile("index.html");
        return app;
    }

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

        profiles.MapPost("/{id}/select", (string id, ProfileStore store) =>
            store.Select(id) ? Results.NoContent() : Results.NotFound());

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
