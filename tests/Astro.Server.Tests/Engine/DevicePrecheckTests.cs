using Astro.Server.Engine;

namespace Astro.Server.Tests.Engine;

/// <summary>장비 연결 직전 점검 (docs/PRECHECK_DESIGN.md ③) — 가짜 PC 장치로</summary>
public class DevicePrecheckTests
{
    private sealed class FakeHost : DevicePrecheck.IHostDevices
    {
        public List<string> Ports = ["COM3", "COM4"];
        public HashSet<string> Usb = ["10C4", "1A86", "0547", "338F"];
        public Dictionary<string, string> DriverPorts = new() { ["ASCOM.OnStep.Telescope"] = "COM3" };
        public DateTimeOffset? Empire, Inserted;
        public bool Closed;
        public List<(string Port, string Name)> Serial = [];
        public IReadOnlyList<string> ComPorts() => Ports;
        public IReadOnlyList<(string Port, string Name)> UsbSerialPorts() => Serial;
        public bool UsbPresent(string vid) => Usb.Contains(vid);
        public string? DriverComPort(string kind, string id) => DriverPorts.GetValueOrDefault(id);
        public DateTimeOffset? EmpireStartedAt() => Empire;
        public DateTimeOffset? LastUsbInsertedAt() => Inserted;
        public void CloseEmpire() => Closed = true;
        public string? Phd2Camera = "ToupTek Camera";
        public string? Phd2CameraName() => Phd2Camera;
    }

    private static Task<DevicePrecheck.Result> Check(FakeHost h, string kind, string id, string role) =>
        DevicePrecheck.CheckAsync(h, kind, id, role, TimeSpan.Zero, CancellationToken.None);

    [Fact]
    public async Task 가이드_카메라가_PC에_없으면_PHD2를_부르기_전에_막는다()
    {
        Assert.True((await Check(new FakeHost(), "guider", "PHD2_Guider", "가이드 카메라")).Ok);
        var off = new FakeHost { Usb = ["10C4", "1A86"] }; // 장비 전원을 다 끈 상태 (10/09)
        var r = await Check(off, "guider", "PHD2_Guider", "가이드 카메라");
        Assert.False(r.Ok);
        Assert.Contains("USB", r.Fix);
        // PHD2 카메라를 모르면(시뮬레이터·처음 설정) 막지 않는다
        Assert.True((await Check(new FakeHost { Usb = [], Phd2Camera = "Simulator" }, "guider", "PHD2_Guider", "가이드 카메라")).Ok);
    }

    [Theory]
    [InlineData("Input Voltage", true)]
    [InlineData("입력 전압", true)]
    [InlineData("Voltage (input)", true)]
    [InlineData("DC2 voltage", false)]
    [InlineData("Input Current", false)]
    public void 허브_입력_전압_칸_이름(string name, bool expected) =>
        Assert.Equal(expected, EquipmentConnector.IsInputVoltage(name));

    [Fact]
    public async Task 적도의_포트가_있으면_통과()
    {
        Assert.True((await Check(new FakeHost(), "mount", "ASCOM.OnStep.Telescope", "적도의")).Ok);
    }

    [Fact]
    public async Task 적도의_포트가_없으면_전원과_케이블을_먼저_안내()
    {
        var h = new FakeHost { Ports = ["COM4"] };
        var r = await Check(h, "mount", "ASCOM.OnStep.Telescope", "적도의");
        Assert.False(r.Ok);
        Assert.Contains("COM3", r.Message);
        Assert.Contains("적도의 전원", r.Fix);
    }

    [Fact]
    public async Task 포트_번호가_바뀌었으면_지금_보이는_포트를_알려_준다_CodexB02()
    {
        var h = new FakeHost { Ports = ["COM4", "COM5"], Serial = [("COM4", "USB-SERIAL CH340"), ("COM5", "Silicon Labs CP210x USB to UART Bridge")] };
        var r = await Check(h, "mount", "ASCOM.OnStep.Telescope", "적도의");
        Assert.False(r.Ok);
        Assert.Contains("COM5(Silicon Labs", r.Fix);
    }

    [Fact]
    public async Task 후지필름_카메라가_USB에_없으면_막는다()
    {
        var r = await Check(new FakeHost(), "camera", "Fujifilm Camera Plugin_X-T5", "카메라");
        Assert.False(r.Ok);
        Assert.Equal("카메라가 PC에 보이지 않습니다", r.Message);
    }

    [Fact]
    public async Task 후지필름_카메라가_보이면_통과()
    {
        var h = new FakeHost();
        h.Usb.Add("04CB");
        Assert.True((await Check(h, "camera", "Fujifilm Camera Plugin_X-T5", "카메라")).Ok);
    }

    [Fact]
    public async Task 모르는_장비는_막지_않는다()
    {
        Assert.True((await Check(new FakeHost(), "camera", "ASCOM.Simulator.Camera", "카메라")).Ok);
        Assert.True((await Check(new FakeHost(), "guider", "PHD2_Single", "가이딩")).Ok);
    }

    [Fact]
    public async Task 오아시스_포커서가_없으면_막는다()
    {
        var h = new FakeHost();
        h.Usb.Remove("338F");
        var r = await Check(h, "focuser", "ASCOM.AOFocuser.Focuser", "포커서");
        Assert.False(r.Ok);
        Assert.Equal("포커서가 PC에 보이지 않습니다", r.Message);
    }

    [Fact]
    public void Empire가_켜진_뒤_USB가_꽂히면_다시_켜야_한다()
    {
        var t = DateTimeOffset.Now;
        Assert.True(DevicePrecheck.EmpireStale(new FakeHost { Empire = t, Inserted = t.AddMinutes(5) }));
        Assert.False(DevicePrecheck.EmpireStale(new FakeHost { Empire = t, Inserted = t.AddMinutes(-5) }));
        Assert.False(DevicePrecheck.EmpireStale(new FakeHost { Empire = null, Inserted = t }));
    }
}
