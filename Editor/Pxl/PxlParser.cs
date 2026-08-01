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

        // —— 以下为 Sync from PNG 文本手术用的源码定位信息（internal 用途，格式语义无关）——
        public int CharsHeaderLine;      // `chars:` 行号（1-based，last-wins）；0 = 无
        public int CharsLastEntryLine;   // 最后一条 chars 条目行号；0 = 无条目。
                                         // 多 chars: 块（罕见，last-wins）时本对仅指向最后一块；
                                         // sync 用它作"追加新条目"的锚点（append-only，不替换整块 → 不丢前块数据）。
        public readonly List<char> CharOrder = new(); // 声明顺序，跨所有 chars: 块全局（颜色→字符反查取先声明者）
    }

    /// <summary>一层像素（spec 2026-08-01-pxl-layers §4.1）。`grid:` 产生匿名底层
    /// （Name == null），其后每个 `layer: x` 追加一层，声明顺序即由下往上的叠放顺序。</summary>
    internal sealed class PxlLayer
    {
        public string Name;                        // null = 来自 `grid:`（匿名底层）
        public readonly List<string> Rows = new(); // top-down，已 trim
        public int HeaderLine;                     // `grid:` / `layer: x` 所在行（1-based）
        public int StartLine, EndLine;             // 像素行区间（含端点）；0 = 无行
    }

    internal sealed class PxlSection
    {
        public string Name;                  // null = 隐式单节
        public Vector4 Border;               // L,B,R,T（Unity Sprite border 序）
        public bool Tiled;                   // tiled: true — 运行时按 Image.Type.Tiled 渲染的提示
        public int Width, Height;
        public readonly List<PxlLayer> Layers = new(); // 底 → 顶，至少一层

        /// <summary>合成结果（top-down），FinishSection 时由 PxlFlattener 算出。
        /// **不回写文件**——.pxl 里只有层，这里是内存里的唯一消费形态。扁平文件
        /// （Layers.Count == 1）下它逐字节等于该层的行，所以下游全部无需感知图层。</summary>
        public readonly List<string> Rows = new();

        // 底层的像素行在源文本中的 1-based 行区间（含端点；含夹在中间的注释行——sync 替换时
        // 一并消失）；0 = 无行（FinishSection 已保证不会返回这种节）。多层节不参与 sync
        // （PxlPngSync 跳过），所以这一对只对单层节有意义。
        public int GridStartLine, GridEndLine;
    }

    /// <summary>.pxl 文本 → IR。网格语法沿 XPM 惯用法：单字符=色板项、'.'=透明、
    /// 一行=一行像素。结构校验（行宽、层高、border 越界、重名）在这里；颜色解析在
    /// PxlColorResolver（Task 3）；图层合成在 PxlFlattener。</summary>
    internal static class PxlParser
    {
        private static readonly Regex SectionHeader =
            new(@"^\[([A-Za-z0-9_-]+)\]$", RegexOptions.Compiled);
        private static readonly Regex CharsEntry =
            new(@"^(.): (.+)$", RegexOptions.Compiled);
        private static readonly Regex LayerName =
            new(@"^[A-Za-z0-9_-]+$", RegexOptions.Compiled);

        public static PxlDocument Parse(string text)
        {
            text = text.TrimStart('﻿'); // 容错裸 BOM（StreamReader 会剥，但字符串入口不保证）
            var doc = new PxlDocument();
            var lines = text.Replace("\r\n", "\n").Split('\n');

            PxlSection section = null;
            PxlLayer layer = null;
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
                    layer = null;
                    inChars = false; inGrid = false;
                    continue;
                }

                // 层头同样优先于 grid 行收集，所以层与层之间不需要空行分隔。这条歧义的另一半
                // 由 ':' 的保留身份封死：chars key 不可能是 ':'，故合法 grid 行拼不出 'layer:'。
                if (line.StartsWith("layer:", StringComparison.Ordinal))
                {
                    var layerName = line.Substring("layer:".Length).Trim();
                    if (!LayerName.IsMatch(layerName))
                        throw new PxlParseException(lineNo,
                            $"layer name '{layerName}' must be non-empty and match [A-Za-z0-9_-]");
                    section = EnsureSection(doc, section, ref sawImplicitContent);
                    foreach (var existing in section.Layers)
                    {
                        if (string.Equals(existing.Name, layerName, StringComparison.Ordinal))
                            throw new PxlParseException(lineNo,
                                $"duplicate layer name '{layerName}' in section '[{section.Name}]'");
                    }
                    layer = new PxlLayer { Name = layerName, HeaderLine = lineNo };
                    section.Layers.Add(layer);
                    inChars = false; inGrid = true;
                    continue;
                }

                // `grid:` is a block header too, so it must outrank grid-row collection for the
                // same reason `layer:` does — otherwise a `grid:` following a layer's rows would
                // be eaten as a (ragged) pixel row. Legal pixel rows can't spell it: ':' is
                // reserved, so no chars key can produce the trailing colon.
                if (line == "grid:")
                {
                    section = EnsureSection(doc, section, ref sawImplicitContent);
                    if (section.Layers.Count > 0)
                    {
                        throw new PxlParseException(lineNo, section.Layers[0].Name == null
                            ? "section already has a grid"
                            : "grid: must come before any layer: block (grid is the bottom layer)");
                    }
                    layer = new PxlLayer { Name = null, HeaderLine = lineNo };
                    section.Layers.Add(layer);
                    inChars = false; inGrid = true;
                    continue;
                }

                if (inGrid)
                {
                    ValidateRow(line, doc, section, layer, lineNo);
                    continue;
                }

                if (inChars)
                {
                    var cm = CharsEntry.Match(line);
                    if (cm.Success)
                    {
                        var key = cm.Groups[1].Value[0];
                        var value = cm.Groups[2].Value.Trim();
                        if (key == ':')
                            throw new PxlParseException(lineNo,
                                "':' cannot be a chars key (reserved: a grid row could then " +
                                "parse as a 'layer:' header)");
                        if (key == '.' && value != "transparent")
                            throw new PxlParseException(lineNo,
                                "'.' is reserved for transparent and cannot be redefined");
                        if (key != '.' && !doc.Chars.TryAdd(key, value))
                            throw new PxlParseException(lineNo, $"duplicate chars key '{key}'");
                        if (key != '.') doc.CharOrder.Add(key);
                        doc.CharsLastEntryLine = lineNo;
                        continue;
                    }
                    inChars = false; // 掉出 chars 块，按普通行继续解析
                }

                // palette:/ppu: 重复声明 last-wins；同节 border: 在首个像素块前重复声明同样 last-wins。
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
                if (line == "chars:") { inChars = true; doc.CharsHeaderLine = lineNo; continue; }

                if (line.StartsWith("border:", StringComparison.Ordinal))
                {
                    section = EnsureSection(doc, section, ref sawImplicitContent);
                    if (section.Layers.Count > 0)
                        throw new PxlParseException(lineNo, "border must come before grid:/layer:");
                    section.Border = ParseBorder(line.Substring("border:".Length).Trim(), lineNo);
                    continue;
                }
                if (line.StartsWith("tiled:", StringComparison.Ordinal))
                {
                    // 首个像素块之前可反复声明，last-wins，同 border:。
                    section = EnsureSection(doc, section, ref sawImplicitContent);
                    if (section.Layers.Count > 0)
                        throw new PxlParseException(lineNo, "tiled must come before grid:/layer:");
                    var tv = line.Substring("tiled:".Length).Trim();
                    section.Tiled = tv switch
                    {
                        "true" => true,
                        "false" => false,
                        _ => throw new PxlParseException(lineNo,
                            $"invalid tiled value '{tv}' (expected true|false)"),
                    };
                    continue;
                }
                throw new PxlParseException(lineNo, $"unrecognized line '{line}' (note: a blank line ends a grid: block)");
            }

            FinishSection(section, lines.Length);
            if (doc.Sections.Count == 0)
                throw new PxlParseException(lines.Length, "file declares no grid");
            return doc;
        }

        // 段头出现前的 border:/grid:/layer: → 隐式单节
        private static PxlSection EnsureSection(PxlDocument doc, PxlSection current,
            ref bool sawImplicit)
        {
            if (current != null) return current;
            var s = new PxlSection { Name = null };
            doc.Sections.Add(s);
            sawImplicit = true;
            return s;
        }

        private static void ValidateRow(string row, PxlDocument doc, PxlSection section,
            PxlLayer layer, int lineNo)
        {
            // 宽度由整节的第一行像素确定，跨层共享——各层必须同宽。
            if (section.Width != 0 && row.Length != section.Width)
                throw new PxlParseException(lineNo,
                    $"row width {row.Length} != first row width {section.Width}");
            foreach (var c in row)
            {
                if (c == '.') continue;
                if (!doc.Chars.ContainsKey(c))
                    throw new PxlParseException(lineNo, $"unknown grid char '{c}' (not in chars:)");
            }
            if (section.Width == 0) section.Width = row.Length;
            layer.Rows.Add(row);
            if (layer.StartLine == 0) layer.StartLine = lineNo;
            layer.EndLine = lineNo;
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

        private static string Describe(PxlLayer l) =>
            l.Name == null ? "grid:" : $"layer '{l.Name}'";

        private static void FinishSection(PxlSection s, int lineNo)
        {
            if (s == null) return;
            if (s.Layers.Count == 0)
                throw new PxlParseException(lineNo,
                    $"section '[{s.Name}]' has no grid: or layer: block");
            foreach (var l in s.Layers)
            {
                if (l.Rows.Count == 0)
                    throw new PxlParseException(l.HeaderLine,
                        $"{Describe(l)} in section '[{s.Name}]' has no rows");
            }

            var bottom = s.Layers[0];
            s.Height = bottom.Rows.Count;
            for (var i = 1; i < s.Layers.Count; i++)
            {
                var l = s.Layers[i];
                if (l.Rows.Count != s.Height)
                    throw new PxlParseException(l.HeaderLine,
                        $"{Describe(l)} has {l.Rows.Count} rows but {Describe(bottom)} has " +
                        $"{s.Height} (all layers in a section must be the same size)");
            }

            s.GridStartLine = bottom.StartLine;
            s.GridEndLine = bottom.EndLine;
            s.Rows.AddRange(PxlFlattener.Flatten(s.Layers, s.Width, s.Height));

            if (s.Border.x + s.Border.z > s.Width || s.Border.y + s.Border.w > s.Height)
                throw new PxlParseException(lineNo,
                    $"border ({s.Border.x},{s.Border.y},{s.Border.z},{s.Border.w}) exceeds " +
                    $"grid size {s.Width}x{s.Height}");
        }
    }
}
