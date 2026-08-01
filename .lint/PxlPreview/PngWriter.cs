using System;
using System.IO;
using System.IO.Compression;

namespace PromptUGUI.PxlPreview
{
    /// <summary>Minimal RGBA8 PNG encoder on pure BCL (DeflateStream + hand-rolled
    /// zlib wrapper). .NET has no cross-platform image encoder — System.Drawing is
    /// Windows-only — and this tool must run wherever `dotnet` runs, so the ~80
    /// lines below are the price of portability.</summary>
    internal static class PngWriter
    {
        private static readonly byte[] Signature = { 137, 80, 78, 71, 13, 10, 26, 10 };

        /// <param name="rgba">width*height*4 bytes, row-major, top-down, non-premultiplied.</param>
        public static void Write(string path, int width, int height, byte[] rgba)
        {
            using (var fs = File.Create(path))
            {
                fs.Write(Signature, 0, Signature.Length);

                var ihdr = new byte[13];
                WriteBe32(ihdr, 0, width);
                WriteBe32(ihdr, 4, height);
                ihdr[8] = 8;    // bit depth
                ihdr[9] = 6;    // color type: truecolour with alpha
                ihdr[10] = 0;   // compression: deflate
                ihdr[11] = 0;   // filter: adaptive
                ihdr[12] = 0;   // interlace: none
                WriteChunk(fs, "IHDR", ihdr);

                WriteChunk(fs, "IDAT", Zlib(Scanlines(width, height, rgba)));
                WriteChunk(fs, "IEND", Array.Empty<byte>());
            }
        }

        /// <summary>Prefix every row with filter byte 0 (None). Filtering would shrink
        /// the file further, but scaled-up pixel art is already ~all flat runs and
        /// deflate handles those well.</summary>
        private static byte[] Scanlines(int width, int height, byte[] rgba)
        {
            var stride = width * 4;
            var raw = new byte[height * (stride + 1)];
            for (var y = 0; y < height; y++)
            {
                raw[y * (stride + 1)] = 0;
                Buffer.BlockCopy(rgba, y * stride, raw, y * (stride + 1) + 1, stride);
            }
            return raw;
        }

        private static byte[] Zlib(byte[] raw)
        {
            using (var ms = new MemoryStream())
            {
                ms.WriteByte(0x78); // CMF: deflate, 32K window
                ms.WriteByte(0x9C); // FLG: default level; (0x78<<8|0x9C) % 31 == 0
                using (var deflate = new DeflateStream(ms, CompressionLevel.Optimal, leaveOpen: true))
                    deflate.Write(raw, 0, raw.Length);
                var adler = Adler32(raw);
                ms.WriteByte((byte)(adler >> 24));
                ms.WriteByte((byte)(adler >> 16));
                ms.WriteByte((byte)(adler >> 8));
                ms.WriteByte((byte)adler);
                return ms.ToArray();
            }
        }

        private static void WriteChunk(Stream s, string type, byte[] data)
        {
            var header = new byte[4];
            WriteBe32(header, 0, data.Length);
            s.Write(header, 0, 4);

            var typed = new byte[4 + data.Length];
            for (var i = 0; i < 4; i++) typed[i] = (byte)type[i];
            Buffer.BlockCopy(data, 0, typed, 4, data.Length);
            s.Write(typed, 0, typed.Length);

            var crc = new byte[4];
            WriteBe32(crc, 0, unchecked((int)Crc32(typed)));
            s.Write(crc, 0, 4);
        }

        private static void WriteBe32(byte[] buf, int offset, int value)
        {
            buf[offset] = (byte)(value >> 24);
            buf[offset + 1] = (byte)(value >> 16);
            buf[offset + 2] = (byte)(value >> 8);
            buf[offset + 3] = (byte)value;
        }

        private static readonly uint[] CrcTable = BuildCrcTable();

        private static uint[] BuildCrcTable()
        {
            var table = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                var c = n;
                for (var k = 0; k < 8; k++)
                    c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                table[n] = c;
            }
            return table;
        }

        private static uint Crc32(byte[] data)
        {
            var c = 0xFFFFFFFFu;
            foreach (var b in data)
                c = CrcTable[(c ^ b) & 0xFF] ^ (c >> 8);
            return c ^ 0xFFFFFFFFu;
        }

        private static uint Adler32(byte[] data)
        {
            uint a = 1, b = 0;
            foreach (var t in data)
            {
                a = (a + t) % 65521;
                b = (b + a) % 65521;
            }
            return (b << 16) | a;
        }
    }
}
