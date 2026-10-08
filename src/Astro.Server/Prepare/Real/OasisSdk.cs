using System.Runtime.InteropServices;

namespace Astro.Server.Prepare.Real;

/// <summary>
/// Oasis 포커서 제조사 SDK (Oasis ASCOM 드라이버와 함께 설치되는 OasisFocuser64.dll) — 0점 잡기 전용.
/// ASCOM·N.I.N.A.에는 Set Zero 명령이 없어(드라이버 SupportedActions = 열선 켜기·끄기뿐) 제조사 SDK를 직접 부른다 (2026-10-09 확인).
/// 함수 모양은 드라이버의 AOFocuserWrapper에서 읽음: 모두 cdecl, 0 = 성공. N.I.N.A.가 포커서를 잡고 있지 않을 때 부른다
/// </summary>
public static class OasisSdk
{
    private static string DllPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFilesX86),
        "ASCOM", "Focuser", "Astroasis", "OasisFocuser64.dll");

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int ScanFn(ref int number, int[] ids);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int IdFn(int id);
    // AOFocuserStatus: int 9개(기온 3 · position · moving · stallDetection · heatingOn · heatingPower · dcPower) + 예비 int 20개
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int StatusFn(int id, int[] status);

    public static bool Installed => File.Exists(DllPath);

    /// <summary>첫 번째 Oasis 포커서의 지금 위치를 0으로 정한다 (움직이지 않음). 성공이면 null, 아니면 이유</summary>
    public static string? SetZero()
    {
        if (!Installed) return "Oasis 포커서 드라이버(SDK)를 찾지 못했습니다";
        nint lib;
        try { lib = NativeLibrary.Load(DllPath); }
        catch (Exception e) when (e is DllNotFoundException or BadImageFormatException) { return $"Oasis SDK를 열지 못했습니다 ({e.Message})"; }
        try
        {
            var scan = Get<ScanFn>(lib, "AOFocuserScan");
            var open = Get<IdFn>(lib, "AOFocuserOpen");
            var close = Get<IdFn>(lib, "AOFocuserClose");
            var zero = Get<IdFn>(lib, "AOFocuserSetZeroPosition");
            var status = Get<StatusFn>(lib, "AOFocuserGetStatus");

            var ids = new int[32];
            var count = 0;
            if (scan(ref count, ids) != 0 || count < 1) return "Oasis 포커서를 찾지 못했습니다. 포커서 USB 연결을 확인해 주세요";
            var id = ids[0];
            if (open(id) is var o && o != 0) return $"Oasis 포커서를 열지 못했습니다 (코드 {o}) — 다른 프로그램이 쓰고 있을 수 있습니다";
            try
            {
                var s = new int[29];
                if (status(id, s) == 0 && s[4] != 0) return "포커서가 움직이는 중이라 0점을 잡지 않았습니다";
                if (zero(id) is var z && z != 0) return $"Oasis 포커서가 0점 명령을 받지 않았습니다 (코드 {z})";
                Thread.Sleep(300);
                if (status(id, s) != 0) return "0점을 잡은 뒤 포커서 위치를 읽지 못했습니다";
                return s[3] == 0 ? null : $"0점을 잡았지만 위치가 {s[3]}입니다";
            }
            finally { close(id); }
        }
        catch (EntryPointNotFoundException e) { return $"Oasis SDK 버전이 달라 0점을 잡지 못했습니다 ({e.Message})"; }
        finally { NativeLibrary.Free(lib); }
    }

    private static T Get<T>(nint lib, string name) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(lib, name));
}
