using System.Net;
using System.Net.Sockets;
using System.Text;
using Astro.Server.Engine;

namespace Astro.Server.Tests.Engine;

/// <summary>PHD2에 장비 연결을 시키고 알림으로 원인 받기 (10/09 실기: "선택한 ToupTek 카메라를 찾을 수 없습니다")</summary>
public class Phd2ConnectTests
{
    private static async Task<(bool Ok, string? Alert)> Run(params string[] replies)
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
            await new StreamReader(s).ReadLineAsync();
            foreach (var r in replies) await w.WriteLineAsync(r);
        });
        try { return await Phd2Check.ConnectEquipmentAsync("127.0.0.1", port, CancellationToken.None); }
        finally { await server; listener.Stop(); }
    }

    [Fact]
    public async Task 카메라를_못_찾으면_알림_문장을_돌려준다()
    {
        var (ok, alert) = await Run(
            "{\"Event\":\"Alert\",\"Msg\":\"선택한 ToupTek 카메라를 찾을 수 없습니다.\",\"Type\":\"warning\"}",
            "{\"jsonrpc\":\"2.0\",\"error\":{\"code\":1,\"message\":\"equipment failed to connect: camera\"},\"id\":2}");
        Assert.False(ok);
        Assert.True(Phd2Check.CameraNotFound(alert));
    }

    [Fact]
    public async Task 연결되면_성공()
    {
        var (ok, alert) = await Run("{\"jsonrpc\":\"2.0\",\"result\":0,\"id\":2}");
        Assert.True(ok);
        Assert.Null(alert);
    }

    [Fact]
    public void 다른_알림은_카메라_못_찾음이_아니다() => Assert.False(Phd2Check.CameraNotFound("Camera is busy"));
}
