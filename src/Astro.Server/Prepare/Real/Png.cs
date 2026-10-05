using System.IO.Compression;

namespace Astro.Server.Prepare.Real;

/// <summary>작은 PNG 만들기 (하늘 화면용 창 캡처·가이드 사진). 이미지 라이브러리 없이 RGB 8비트만</summary>
public static class Png
{
    /// <summary>rgb = 한 줄씩 R,G,B 바이트</summary>
    public static byte[] Encode(int width, int height, ReadOnlySpan<byte> rgb)
    {
        using var raw = new MemoryStream();
        using (var z = new ZLibStream(raw, CompressionLevel.Fastest, leaveOpen: true))
        {
            var row = width * 3;
            for (var y = 0; y < height; y++)
            {
                z.WriteByte(0); // 필터 없음
                z.Write(rgb.Slice(y * row, row));
            }
        }
        using var png = new MemoryStream();
        png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        var ihdr = new byte[13];
        WriteBE(ihdr, 0, (uint)width);
        WriteBE(ihdr, 4, (uint)height);
        ihdr[8] = 8; // 비트 깊이
        ihdr[9] = 2; // RGB
        Chunk(png, "IHDR", ihdr);
        Chunk(png, "IDAT", raw.ToArray());
        Chunk(png, "IEND", []);
        return png.ToArray();
    }

    /// <summary>회색 16비트 값(예: 가이드 카메라 FITS)을 눈에 보이게 늘려(stretch) RGB로</summary>
    public static byte[] FromGray16(int width, int height, ReadOnlySpan<ushort> pixels)
    {
        // 어두운 쪽 중앙값 근처를 검게, 밝은 별은 하얗게 (자동 늘리기 흉내)
        var sample = new List<ushort>(4096);
        var step = Math.Max(1, pixels.Length / 4096);
        for (var i = 0; i < pixels.Length; i += step) sample.Add(pixels[i]);
        sample.Sort();
        double lo = sample[sample.Count / 2], hi = sample[Math.Min(sample.Count - 1, (int)(sample.Count * 0.999))];
        if (hi <= lo) hi = lo + 1;
        var rgb = new byte[width * height * 3];
        for (var i = 0; i < width * height; i++)
        {
            var t = Math.Clamp((pixels[i] - lo) / (hi - lo), 0, 1);
            var v = (byte)(Math.Sqrt(t) * 255);
            rgb[i * 3] = rgb[i * 3 + 1] = rgb[i * 3 + 2] = v;
        }
        return Encode(width, height, rgb);
    }

    private static void Chunk(Stream s, string type, byte[] data)
    {
        var len = new byte[4];
        WriteBE(len, 0, (uint)data.Length);
        s.Write(len);
        var td = new byte[4 + data.Length];
        System.Text.Encoding.ASCII.GetBytes(type).CopyTo(td, 0);
        data.CopyTo(td, 4);
        s.Write(td);
        var crc = new byte[4];
        WriteBE(crc, 0, Crc32(td));
        s.Write(crc);
    }

    private static readonly uint[] Table = Enumerable.Range(0, 256).Select(n =>
    {
        var c = (uint)n;
        for (var k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
        return c;
    }).ToArray();

    private static uint Crc32(byte[] data)
    {
        var c = 0xFFFFFFFFu;
        foreach (var b in data) c = Table[(c ^ b) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFFu;
    }

    private static void WriteBE(byte[] b, int o, uint v)
    {
        b[o] = (byte)(v >> 24);
        b[o + 1] = (byte)(v >> 16);
        b[o + 2] = (byte)(v >> 8);
        b[o + 3] = (byte)v;
    }
}
