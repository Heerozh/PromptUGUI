using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace PromptUGUI.Editor
{
    internal sealed class PxlParseException : Exception
    {
        public readonly int Line;
        public PxlParseException(int line, string message)
            : base(line > 0 ? $"line {line}: {message}" : message) { Line = line; }
    }

    internal sealed class PxlDocument
    {
        public string PaletteRef;                              // "main" ← `palette: @main`; null = 纯内联
        public float Ppu = 100f;
        public readonly Dictionary<char, string> Chars = new(); // char → "transparent" | 色名 | #hex
        public readonly List<PxlSection> Sections = new();
    }

    internal sealed class PxlSection
    {
        public string Name;                  // null = 隐式单节
        public Vector4 Border;               // L,B,R,T（Unity Sprite border 序）
        public int Width, Height;
        public readonly List<string> Rows = new(); // top-down，已 trim
    }

    /// <summary>.pxl 文本 → IR。网格语法沿 XPM 惯用法：单字符=色板项、'.'=透明、
    /// 一行=一行像素。结构校验（行宽、border 越界、重名）在这里；颜色解析在
    /// PxlColorResolver（Task 3）。</summary>
    internal static class PxlParser
    {
        private static readonly Regex SectionHeader =
            new(@"^\[([A-Za-z0-9_-]+)\]$", RegexOptions.Compiled);
        private static readonly Regex CharsEntry =
            new(@"^(.): (.+)$", RegexOptions.Compiled);

        public static PxlDocument Parse(string text)
        {
            text = text.TrimStart('﻿'); // 容错裸 BOM（StreamReader 会剥，但字符串入口不保证）
            var doc = new PxlDocument();
            var lines = text.Replace("\r\n", "\n").Split('\n');

            PxlSection section = null;
            var inChars = false;
            var inGrid = false;
            var sawImplicitContent = false;
            var names = new HashSet<string>(StringComparer.Ordinal);

            for (var i = 0; i < lines.Length; i++)
            {
                var lineNo = i + 1;
                var line = lines[i].Trim();
                if (line.Length == 0) { inGrid = false; continue; }
                if (line[0] == '#') continue; // 整行注释（含 chars 块内：'#: x' 整行被跳过，'#' 不可能成为 chars key）

                // 段头匹配优先于 grid 行收集：grid 内的 '[x]' 行会结束当前节并开新节（'['/']' 不应用作 chars key）。
                var m = SectionHeader.Match(line);
                if (m.Success)
                {
                    if (sawImplicitContent)
                        throw new PxlParseException(lineNo,
                            "cannot mix implicit (headerless) content with [section] headers");
                    FinishSection(section, lineNo);
                    var name = m.Groups[1].Value;
                    if (!names.Add(name))
                        throw new PxlParseException(lineNo, $"duplicate section name '[{name}]'");
                    section = new PxlSection { Name = name };
                    doc.Sections.Add(section);
                    inChars = false; inGrid = false;
                    continue;
                }

                if (inGrid)
                {
                    ValidateRow(line, doc, section, lineNo);
                    continue;
                }

                if (inChars)
                {
                    var cm = CharsEntry.Match(line);
                    if (cm.Success)
                    {
                        var key = cm.Groups[1].Value[0];
                        var value = cm.Groups[2].Value.Trim();
                        if (key == '.' && value != "transparent")
                            throw new PxlParseException(lineNo,
                                "'.' is reserved for transparent and cannot be redefined");
                        if (key != '.' && !doc.Chars.TryAdd(key, value))
                            throw new PxlParseException(lineNo, $"duplicate chars key '{key}'");
                        continue;
                    }
                    inChars = false; // 掉出 chars 块，按普通行继续解析
                }

                // palette:/ppu: 重复声明 last-wins；同节 border: 在 grid 前重复声明同样 last-wins。
                if (line.StartsWith("palette:", StringComparison.Ordinal))
                {
                    var v = line.Substring("palette:".Length).Trim();
                    if (!v.StartsWith("@", StringComparison.Ordinal) || v.Length < 2)
                        throw new PxlParseException(lineNo,
                            $"palette must be '@<name>' (a project .gpl reference), got '{v}'");
                    var name = v.Substring(1);
                    foreach (var c in name)
                    {
                        if (!((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') ||
                              (c >= '0' && c <= '9') || c == '_' || c == '-'))
                            throw new PxlParseException(lineNo,
                                $"palette name '@{name}' may only contain [A-Za-z0-9_-]");
                    }
                    doc.PaletteRef = name;
                    continue;
                }
                if (line.StartsWith("ppu:", StringComparison.Ordinal))
                {
                    var v = line.Substring("ppu:".Length).Trim();
                    if (!float.TryParse(v, System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out var ppu) || ppu <= 0)
                        throw new PxlParseException(lineNo, $"ppu must be a positive number, got '{v}'");
                    doc.Ppu = ppu;
                    continue;
                }
                if (line == "chars:") { inChars = true; continue; }

                if (line.StartsWith("border:", StringComparison.Ordinal))
                {
                    section = EnsureSection(doc, section, ref sawImplicitContent);
                    if (section.Rows.Count > 0)
                        throw new PxlParseException(lineNo, "border must come before grid");
                    section.Border = ParseBorder(line.Substring("border:".Length).Trim(), lineNo);
                    continue;
                }
                if (line == "grid:")
                {
                    section = EnsureSection(doc, section, ref sawImplicitContent);
                    if (section.Rows.Count > 0)
                        throw new PxlParseException(lineNo, "section already has a grid");
                    inGrid = true;
                    continue;
                }

                throw new PxlParseException(lineNo, $"unrecognized line '{line}' (note: a blank line ends a grid: block)");
            }

            FinishSection(section, lines.Length);
            if (doc.Sections.Count == 0)
                throw new PxlParseException(lines.Length, "file declares no grid");
            return doc;
        }

        // 段头出现前的 border:/grid: → 隐式单节
        private static PxlSection EnsureSection(PxlDocument doc, PxlSection current,
            ref bool sawImplicit)
        {
            if (current != null) return current;
            var s = new PxlSection { Name = null };
            doc.Sections.Add(s);
            sawImplicit = true;
            return s;
        }

        private static void ValidateRow(string row, PxlDocument doc, PxlSection section, int lineNo)
        {
            if (section.Rows.Count > 0 && row.Length != section.Width)
                throw new PxlParseException(lineNo,
                    $"row width {row.Length} != first row width {section.Width}");
            foreach (var c in row)
            {
                if (c == '.') continue;
                if (!doc.Chars.ContainsKey(c))
                    throw new PxlParseException(lineNo, $"unknown grid char '{c}' (not in chars:)");
            }
            if (section.Rows.Count == 0) section.Width = row.Length;
            section.Rows.Add(row);
            section.Height = section.Rows.Count;
        }

        private static Vector4 ParseBorder(string v, int lineNo)
        {
            var parts = v.Split(',');
            if (parts.Length != 4)
                throw new PxlParseException(lineNo, $"border must be 'L,B,R,T' (4 ints), got '{v}'");
            var n = new int[4];
            for (var i = 0; i < 4; i++)
            {
                if (!int.TryParse(parts[i].Trim(), out n[i]) || n[i] < 0)
                    throw new PxlParseException(lineNo, $"border component '{parts[i].Trim()}' must be a non-negative int");
            }
            return new Vector4(n[0], n[1], n[2], n[3]);
        }

        private static void FinishSection(PxlSection s, int lineNo)
        {
            if (s == null) return;
            if (s.Rows.Count == 0)
                throw new PxlParseException(lineNo, $"section '[{s.Name}]' has no grid");
            if (s.Border.x + s.Border.z > s.Width || s.Border.y + s.Border.w > s.Height)
                throw new PxlParseException(lineNo,
                    $"border ({s.Border.x},{s.Border.y},{s.Border.z},{s.Border.w}) exceeds " +
                    $"grid size {s.Width}x{s.Height}");
        }
    }
}
