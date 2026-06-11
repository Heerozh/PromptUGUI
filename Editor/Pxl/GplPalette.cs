using System;
using System.Collections.Generic;
using UnityEngine;

namespace PromptUGUI.Editor
{
    /// <summary>GIMP Palette (.gpl) 文本解析。社区标准格式：Aseprite 原生读写、
    /// Lospec 全站可下载。条目 = "R G B [name]"，name 可缺省（缺省条目只能被 hex 命中）。
    /// 同名条目 last-wins（Entries 保留全部，按名查找取最后一个）。</summary>
    internal sealed class GplPalette
    {
        public readonly List<(Color32 color, string name)> Entries = new();
        private readonly Dictionary<string, Color32> _byName = new(StringComparer.Ordinal);

        public static GplPalette Parse(string text)
        {
            var palette = new GplPalette();
            var lines = text.Replace("\r\n", "\n").Split('\n');
            var headerSeen = false;
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (line.Length == 0) continue;
                if (!headerSeen)
                {
                    if (line != "GIMP Palette")
                        throw new FormatException(
                            $"line {i + 1}: not a GIMP Palette file (expected 'GIMP Palette' header)");
                    headerSeen = true;
                    continue;
                }
                if (line.StartsWith("#", StringComparison.Ordinal)) continue;
                if (line.StartsWith("Name:", StringComparison.Ordinal)) continue;
                if (line.StartsWith("Columns:", StringComparison.Ordinal)) continue;

                var parts = line.Split((char[])null, 4, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 3 ||
                    !byte.TryParse(parts[0], out var r) ||
                    !byte.TryParse(parts[1], out var g) ||
                    !byte.TryParse(parts[2], out var b))
                {
                    throw new FormatException($"line {i + 1}: expected 'R G B [name]', got '{line}'");
                }
                var name = parts.Length == 4 ? parts[3].Trim() : null;
                if (string.IsNullOrEmpty(name)) name = null;
                var color = new Color32(r, g, b, 255);
                palette.Entries.Add((color, name));
                if (name != null) palette._byName[Normalize(name)] = color;
            }
            if (!headerSeen)
                throw new FormatException("line 1: not a GIMP Palette file (expected 'GIMP Palette' header)");
            return palette;
        }

        public bool TryGetByName(string token, out Color32 color) =>
            _byName.TryGetValue(Normalize(token), out color);

        public bool ContainsRgb(Color32 c)
        {
            foreach (var (e, _) in Entries)
                if (e.r == c.r && e.g == c.g && e.b == c.b) return true;
            return false;
        }

        /// <summary>色名比较忽略大小写与空格/连字符/下划线差异（"Dark Blue" ≡ "dark-blue"）。</summary>
        public static string Normalize(string name) =>
            name.ToLowerInvariant().Replace(" ", "").Replace("-", "").Replace("_", "");
    }
}
