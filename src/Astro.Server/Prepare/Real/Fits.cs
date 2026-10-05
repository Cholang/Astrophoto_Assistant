using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace Astro.Server.Prepare.Real;

/// <summary>가이드 카메라 사진(PHD2 save_image의 FITS, 16비트 회색)을 하늘 화면용 PNG로</summary>
public static class Fits
{
    public static byte[]? ToPng(string path, int maxWidth = 960)
    {
        try
        {
            var bytes = File.ReadAllBytes(path);
            var (w, h, bitpix, bzero, dataStart) = Header(bytes);
            if (w <= 0 || h <= 0 || bitpix != 16 && bitpix != 8) return null;
            var step = Math.Max(1, (w + maxWidth - 1) / maxWidth);
            int ow = w / step, oh = h / step;
            var px = new ushort[ow * oh];
            for (var y = 0; y < oh; y++)
                for (var x = 0; x < ow; x++)
                {
                    var i = (y * step) * w + x * step;
                    px[y * ow + x] = bitpix == 16
                        ? (ushort)Math.Clamp(BinaryPrimitives.ReadInt16BigEndian(bytes.AsSpan(dataStart + i * 2, 2)) + bzero, 0, 65535)
                        : (ushort)(bytes[dataStart + i] * 257);
                }
            return Png.FromGray16(ow, oh, px);
        }
        catch (Exception e) when (e is IOException or ArgumentOutOfRangeException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static (int W, int H, int Bitpix, double Bzero, int DataStart) Header(byte[] b)
    {
        int w = 0, h = 0, bitpix = 0;
        double bzero = 0;
        for (var block = 0; block * 2880 < b.Length; block++)
        {
            for (var card = 0; card < 36; card++)
            {
                var o = block * 2880 + card * 80;
                var line = Encoding.ASCII.GetString(b, o, 80);
                var key = line[..8].Trim();
                var value = line.Length > 10 ? line[10..].Split('/')[0].Trim() : "";
                switch (key)
                {
                    case "BITPIX": bitpix = int.Parse(value, CultureInfo.InvariantCulture); break;
                    case "NAXIS1": w = int.Parse(value, CultureInfo.InvariantCulture); break;
                    case "NAXIS2": h = int.Parse(value, CultureInfo.InvariantCulture); break;
                    case "BZERO": bzero = double.Parse(value, CultureInfo.InvariantCulture); break;
                    case "END": return (w, h, bitpix, bzero, (block + 1) * 2880);
                }
            }
        }
        return (0, 0, 0, 0, 0);
    }
}
