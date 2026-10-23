using UnityEngine;

namespace Fortress.Game
{
    /// Surfaces painted in code: what isn't a scan.
    public static class ProceduralTextures
    {
        /// A canvas with the y axis pointing down, like the iOS drawing code it was ported from.
        class Canvas
        {
            readonly int width, height;
            readonly Color[] pixels;

            public Canvas(int width, int height, Color fill)
            {
                this.width = width;
                this.height = height;
                pixels = new Color[width * height];
                for (int i = 0; i < pixels.Length; i++) pixels[i] = fill;
            }

            /// Paints a rectangle, blending by `color.a` over what is already there.
            public void Fill(float x, float y, float w, float h, Color color)
            {
                int x0 = Mathf.Max(0, Mathf.FloorToInt(x)), x1 = Mathf.Min(width, Mathf.CeilToInt(x + w));
                int y0 = Mathf.Max(0, Mathf.FloorToInt(y)), y1 = Mathf.Min(height, Mathf.CeilToInt(y + h));
                for (int py = y0; py < y1; py++)
                for (int px = x0; px < x1; px++)
                {
                    int i = (height - 1 - py) * width + px;
                    var under = pixels[i];
                    pixels[i] = new Color(Mathf.Lerp(under.r, color.r, color.a), Mathf.Lerp(under.g, color.g, color.a),
                                          Mathf.Lerp(under.b, color.b, color.a), 1);
                }
            }

            public Texture2D Texture(string name)
            {
                var texture = new Texture2D(width, height, TextureFormat.RGBA32, true) { name = name, wrapMode = TextureWrapMode.Repeat };
                texture.SetPixels(pixels);
                texture.Apply(true);
                texture.anisoLevel = 4;
                return texture;
            }
        }

        static float R(float min, float max) => Random.Range(min, max);

        /// Blue drafting paper with a white grid.
        public static Texture2D Blueprint()
        {
            const int width = 128, height = 96;
            var canvas = new Canvas(width, height, new Color(0.12f, 0.32f, 0.62f));
            for (int x = 0; x < width; x += 12) canvas.Fill(x, 0, 1, height, new Color(1, 1, 1, 0.35f));
            for (int y = 0; y < height; y += 12) canvas.Fill(0, y, width, 1, new Color(1, 1, 1, 0.35f));
            var line = new Color(1, 1, 1, 0.9f);
            canvas.Fill(30, 22, 68, 2, line);
            canvas.Fill(30, 72, 68, 2, line);
            canvas.Fill(30, 22, 2, 52, line);
            canvas.Fill(96, 22, 2, 52, line);
            return canvas.Texture("Blueprint");
        }
    }
}
