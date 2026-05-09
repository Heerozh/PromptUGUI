using System;
using System.Globalization;
using PromptUGUI.IR;
using UnityEngine;

namespace PromptUGUI.Layout
{
    public readonly struct LayoutResult
    {
        public Vector2 AnchoredPosition { get; }
        public Vector2 SizeDelta { get; }
        public LayoutResult(Vector2 pos, Vector2 size)
        {
            AnchoredPosition = pos; SizeDelta = size;
        }
    }

    public static class MarginResolver
    {

        public static LayoutResult Resolve(AnchorPreset anchor, SizeSpec size, string margin)
        {
            ParseMargin(margin, out var t, out var r, out var b, out var l);

            float anchorX, anchorY;
            float sizeX, sizeY;

            // X 轴
            if (anchor.StretchX)
            {
                sizeX = -(l + r);
                anchorX = (l - r) * 0.5f;
            }
            else
            {
                sizeX = size.HasWidth ? size.Width : 0f;
                anchorX = anchor.H switch
                {
                    AnchorHorizontal.Left => l,
                    AnchorHorizontal.Right => -r,
                    AnchorHorizontal.Center => 0f,
                    _ => 0f,
                };
            }

            // Y 轴
            if (anchor.StretchY)
            {
                sizeY = -(t + b);
                anchorY = (b - t) * 0.5f;
            }
            else
            {
                sizeY = size.HasHeight ? size.Height : 0f;
                anchorY = anchor.V switch
                {
                    AnchorVertical.Bottom => b,
                    AnchorVertical.Top => -t,
                    AnchorVertical.Center => 0f,
                    _ => 0f,
                };
            }

            return new LayoutResult(
                new Vector2(anchorX, anchorY),
                new Vector2(sizeX, sizeY));
        }

        private static void ParseMargin(string s, out float t, out float r, out float b, out float l)
        {
            t = r = b = l = 0f;
            if (string.IsNullOrEmpty(s)) return;

            var parts = s.Split(',');
            var vals = new float[parts.Length];
            for (var i = 0; i < parts.Length; i++)
            {
                var p = parts[i].Trim();
                vals[i] = (p == "_" || p == "") ? 0f
                    : float.Parse(p, CultureInfo.InvariantCulture);
            }

            switch (parts.Length)
            {
                case 1: t = r = b = l = vals[0]; return;
                case 2: t = b = vals[0]; r = l = vals[1]; return;
                case 4: t = vals[0]; r = vals[1]; b = vals[2]; l = vals[3]; return;
                default:
                    throw new ArgumentException(
                        $"margin '{s}' must have 1, 2, or 4 components");
            }
        }
    }
}
