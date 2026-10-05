using System.Net;
using System.Net.Sockets;
using System.Text;
using Astro.Server.Engine;

namespace Astro.Server.Tests.Engine;

/// <summary>가이더 연결 확인: PHD2 안의 카메라·적도의 (가짜 PHD2 — 이벤트를 먼저 보내고 답한다)</summary>
public class Phd2CheckTests
{
    private static async Task<Phd2Check.Problem?> Run(string equipmentJson, string? ninaMount)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var server = Task.Run(async () =>
        {
            using var c = await listener.AcceptTcpClientAsync();
            await using var s = c.GetStream();
            var w = new StreamWriter(s, new UTF8Encoding(false)) { NewLine = "\r\n", AutoFlush = true };
            await w.WriteLineAsync("{\"Event\":\"Version\",\"PHDVersion\":\"2.6.14\"}");
            await w.WriteLineAsync("{\"Event\":\"AppState\",\"State\":\"Stopped\"}");
            await new StreamReader(s).ReadLineAsync();
            await w.WriteLineAsync("{\"jsonrpc\":\"2.0\",\"result\":" + equipmentJson + ",\"id\":1}");
        });
        try { return await Phd2Check.CheckAsync("127.0.0.1", port, ninaMount, CancellationToken.None); }
        finally { await server; listener.Stop(); }
    }

    private static string Eq(string cam, bool camOn, string mount, bool mountOn) =>
        $"{{\"camera\":{{\"name\":\"{cam}\",\"connected\":{camOn.ToString().ToLower()}}},\"mount\":{{\"name\":\"{mount}\",\"connected\":{mountOn.ToString().ToLower()}}}}}";

    [Fact]
    public async Task 정상() => Assert.Null(await Run(Eq("G3M662M(USB2.0)", true, "On-Step (ASCOM)", true), "On-Step"));

    [Fact]
    public async Task 카메라_연결_안_됨() =>
        Assert.Contains("가이드 카메라", (await Run(Eq("ToupTek Camera", false, "On-Step (ASCOM)", true), "On-Step"))!.Message);

    [Fact]
    public async Task 적도의_연결_안_됨() =>
        Assert.Contains("적도의가 연결되지", (await Run(Eq("G3M662M", true, "On-Step (ASCOM)", false), "On-Step"))!.Message);

    [Fact]
    public async Task 시뮬레이터_적도의() =>
        Assert.Contains("N.I.N.A.와 다릅니다", (await Run(Eq("G3M662M", true, "Alpaca Telescope Simulator (ASCOM)", true), "On-Step"))!.Message);

    [Fact]
    public async Task 다른_적도의() =>
        Assert.NotNull(await Run(Eq("G3M662M", true, "EQMOD ASCOM HEQ5/6", true), "On-Step"));

    [Fact]
    public async Task 카메라_ST4로_보정하면_이름이_달라도_통과() =>
        Assert.Null(await Run(Eq("G3M662M", true, "On-camera", true), "On-Step"));

    [Fact]
    public async Task PHD2에_닿지_못하면_막지_않는다()
    {
        var l = new TcpListener(IPAddress.Loopback, 0); l.Start(); var port = ((IPEndPoint)l.LocalEndpoint).Port; l.Stop();
        Assert.Null(await Phd2Check.CheckAsync("127.0.0.1", port, "On-Step", CancellationToken.None));
    }

    [Fact(Skip = "실기: PHD2가 켜져 있을 때만 수동으로")]
    public async Task 실제_PHD2() => Assert.Null(await Phd2Check.CheckAsync("localhost", 4400, "On-Step", CancellationToken.None));
}
