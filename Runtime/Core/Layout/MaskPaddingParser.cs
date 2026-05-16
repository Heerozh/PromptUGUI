using System;
using System.Globalization;
using UnityEngine;

namespace PromptUGUI.Layout
{
    /// <summary>
    /// 解析作者层 "T,R,B,L" 1/2/4 分量字符串（"_" = 0 占位），翻转成 Unity 原生
    /// <see cref="UnityEngine.UI.RectMask2D.padding"/> 的 Vector4(L,B,R,T) 顺序。
    /// </summary>
    internal static class MaskPaddingParser
    {
        public static Vector4 Parse(string value)
        {
            if (string.IsNullOrEmpty(value)) return Vector4.zero;
            var parts = value.Split(',');
            float t, r, b, l;
            switch (parts.Length)
            {
                case 1:
                    t = r = b = l = ParseOne(parts[0]);
                    break;
                case 2:
                    t = b = ParseOne(parts[0]);
                    r = l = ParseOne(parts[1]);
                    break;
                case 4:
                    t = ParseOne(parts[0]);
                    r = ParseOne(parts[1]);
                    b = ParseOne(parts[2]);
                    l = ParseOne(parts[3]);
                    break;
                default:
                    throw new ArgumentException(
                        $"maskPadding: expected 1, 2, or 4 components, got {parts.Length} in '{value}'");
            }
            return new Vector4(l, b, r, t);
        }

        private static float ParseOne(string s)
        {
            s = s.Trim();
            if (s == "_") return 0f;
            return float.Parse(s, CultureInfo.InvariantCulture);
        }
    }
}
