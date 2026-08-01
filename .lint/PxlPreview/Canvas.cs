namespace PromptUGUI.PxlPreview
{
    internal struct Rgba
    {
        public byte R, G, B, A;

        public Rgba(byte r, byte g, byte b, byte a = 255)
        {
            R = r; G = g; B = b; A = a;
        }
    }

    /// <summary>Top-down RGBA8 pixel buffer with the two primitives this tool needs.
    /// Out-of-bounds writes are clipped, not thrown — label text is allowed to run
    /// past a narrow sprite block.</summary>
    internal sealed class Canvas
    {
        public readonly int Width;
        public readonly int Height;
        public readonly byte[] Pixels;

        public Canvas(int width, int height, Rgba fill)
        {
            Width = width;
            Height = height;
            Pixels = new byte[width * height * 4];
            Fill(0, 0, width, height, fill);
        }

        public void Fill(int x, int y, int w, int h, Rgba color)
        {
            var x0 = x < 0 ? 0 : x;
            var y0 = y < 0 ? 0 : y;
            var x1 = x + w > Width ? Width : x + w;
            var y1 = y + h > Height ? Height : y + h;
            for (var py = y0; py < y1; py++)
            {
                for (var px = x0; px < x1; px++)
                {
                    var i = (py * Width + px) * 4;
                    Pixels[i] = color.R;
                    Pixels[i + 1] = color.G;
                    Pixels[i + 2] = color.B;
                    Pixels[i + 3] = color.A;
                }
            }
        }

        /// <summary>Source-over composite of an opaque-background rect. The preview
        /// always lands on the checkerboard, so the destination is opaque and the
        /// result stays opaque — semi-transparent .pxl pixels show as a tint of the
        /// checker underneath, which is exactly how they read in-game over a panel.</summary>
        public void Blend(int x, int y, int w, int h, Rgba color)
        {
            if (color.A == 0) return;
            if (color.A == 255) { Fill(x, y, w, h, color); return; }

            var x0 = x < 0 ? 0 : x;
            var y0 = y < 0 ? 0 : y;
            var x1 = x + w > Width ? Width : x + w;
            var y1 = y + h > Height ? Height : y + h;
            for (var py = y0; py < y1; py++)
            {
                for (var px = x0; px < x1; px++)
                {
                    var i = (py * Width + px) * 4;
                    Pixels[i] = Mix(Pixels[i], color.R, color.A);
                    Pixels[i + 1] = Mix(Pixels[i + 1], color.G, color.A);
                    Pixels[i + 2] = Mix(Pixels[i + 2], color.B, color.A);
                    Pixels[i + 3] = 255;
                }
            }
        }

        private static byte Mix(byte dst, byte src, byte alpha) =>
            (byte)((src * alpha + dst * (255 - alpha) + 127) / 255);
    }
}
