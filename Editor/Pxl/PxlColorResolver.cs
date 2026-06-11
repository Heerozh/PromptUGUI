using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace PromptUGUI.Editor
{
    /// <summary>chars 映射 → 具体颜色。palette 模式（doc.PaletteRef != null）下
    /// hex 的 RGB 必须命中色板（忽略 alpha）——把全项目调色板一致性从 LLM 自觉
    /// 变成管线强制（spec §3）。错误用 PxlParseException(line=0)：此阶段无行号，
    /// 消息携带 char 键与值上下文足以定位。
    /// 调用方契约：palette 参数当且仅当 doc.PaletteRef != null 时非 null（由 PxlImporter 保证）。</summary>
    internal static class PxlColorResolver
    {
        public static Dictionary<char, Color32> Resolve(PxlDocument doc, GplPalette palette)
        {
            var map = new Dictionary<char, Color32> { ['.'] = new Color32(0, 0, 0, 0) };
            foreach (var kv in doc.Chars)
            {
                var key = kv.Key;
                var value = kv.Value;
                if (value == "transparent") { map[key] = new Color32(0, 0, 0, 0); continue; }
                if (value.StartsWith("#", StringComparison.Ordinal))
                {
                    var c = ParseHex(key, value);
                    if (palette != null && !palette.ContainsRgb(c))
                        throw new PxlParseException(0,
                            $"chars '{key}': {value} is not on palette '@{doc.PaletteRef}' " +
                            $"(off-palette color; pick a palette color or add it to the .gpl)");
                    map[key] = c;
                    continue;
                }
                // 色名
                if (palette == null)
                    throw new PxlParseException(0,
                        $"chars '{key}': color name '{value}' requires a 'palette: @<name>' declaration");
                if (!palette.TryGetByName(value, out var named))
                    throw new PxlParseException(0,
                        $"chars '{key}': color name '{value}' not found in palette '@{doc.PaletteRef}'");
                map[key] = named;
            }
            return map;
        }

        private static Color32 ParseHex(char key, string value)
        {
            var hex = value.Substring(1);
            if ((hex.Length != 6 && hex.Length != 8) ||
                !uint.TryParse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out _))
            {
                throw new PxlParseException(0,
                    $"chars '{key}': '{value}' is not #RRGGBB / #RRGGBBAA");
            }
            byte P(int i) => byte.Parse(hex.Substring(i, 2), NumberStyles.AllowHexSpecifier,
                CultureInfo.InvariantCulture);
            return new Color32(P(0), P(2), P(4), hex.Length == 8 ? P(6) : (byte)255);
        }
    }
}
