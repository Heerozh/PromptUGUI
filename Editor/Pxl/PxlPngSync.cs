using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace PromptUGUI.Editor
{
    /// <summary>工具 2：PNG 像素就地回写 .pxl（spec 2026-06-11-pxl-png-roundtrip §4）。
    /// BuildPlan 产出纯数据计划（不碰文件/UI），Apply（Task 4）做文本手术。
    /// .pxl 是元数据唯一事实来源：sync 只更新已有节的 grid 与追加 chars 条目。</summary>
    internal static class PxlPngSync
    {
        /// <summary>解码后的 PNG：Pixels 为 top-down 行主序（调用方负责把
        /// GetPixels32 的 bottom-up 翻转过来）。</summary>
        public readonly struct PngImage
        {
            public readonly int Width, Height;
            public readonly Color32[] Pixels;
            public PngImage(int width, int height, Color32[] pixels)
            { Width = width; Height = height; Pixels = pixels; }
        }

        public sealed class SectionUpdate
        {
            public PxlSection Section;
            public int NewWidth, NewHeight;
            public readonly List<string> Rows = new(); // 已映射为字符的新 grid 行（top-down）
        }

        public sealed class SyncPlan
        {
            public readonly List<SectionUpdate> Updates = new();
            public readonly List<string> MissingSections = new(); // 没找到 PNG 的节（显示名）
            public readonly List<string> ExtraPngs = new();       // 前缀匹配但无对应节
            public readonly List<(char ch, string value)> NewChars = new();
            public readonly List<string> Errors = new();          // 非空 = 不可执行
            public int CharsInsertAfterLine;                      // Apply 追加新 chars 条目的锚点行
        }

        // 新字符分配字母表：A-Z a-z 0-9，再排除保留字符的其余可打印 ASCII。
        private const string Alphabet =
            "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789" +
            "!\"$%&'()*+,-/:;<=>?@\\^_`{|}~";

        public static SyncPlan BuildPlan(string pxlText, string baseName,
            IReadOnlyDictionary<string, PngImage> pngs, GplPalette palette)
        {
            var plan = new SyncPlan();
            PxlDocument doc;
            try { doc = PxlParser.Parse(pxlText); }
            catch (PxlParseException ex)
            {
                plan.Errors.Add($"cannot parse .pxl: {ex.Message}");
                return plan;
            }
            plan.CharsInsertAfterLine =
                doc.CharsLastEntryLine != 0 ? doc.CharsLastEntryLine : doc.CharsHeaderLine;

            // 颜色→字符反查：按声明顺序，先声明者占据颜色（'.' 透明单独处理）。
            var resolved = PxlColorResolver.Resolve(doc, palette);
            var colorToChar = new Dictionary<Color32, char>();
            var usedChars = new HashSet<char>(doc.CharOrder) { '.', '#', '[', ']' };
            foreach (var ch in doc.CharOrder)
            {
                var c = resolved[ch];
                if (c.a == 0) continue; // 透明值一律走 '.'
                if (!colorToChar.ContainsKey(c)) colorToChar[c] = ch; // 先声明者占据颜色
            }

            var alphabetCursor = 0;
            var matchedFiles = new HashSet<string>(StringComparer.Ordinal);

            foreach (var section in doc.Sections)
            {
                var fileName = PxlPngExporter.FileNameFor(baseName, section);
                if (!pngs.TryGetValue(fileName, out var img))
                {
                    plan.MissingSections.Add(section.Name ?? baseName);
                    continue;
                }
                matchedFiles.Add(fileName);

                // 尺寸变化后 border 必须仍然成立（不静默改元数据）。
                if (section.Border.x + section.Border.z > img.Width ||
                    section.Border.y + section.Border.w > img.Height)
                {
                    plan.Errors.Add(
                        $"[{section.Name ?? baseName}]: border " +
                        $"({section.Border.x},{section.Border.y},{section.Border.z},{section.Border.w}) " +
                        $"exceeds new size {img.Width}x{img.Height}; fix the border: line first");
                    continue;
                }

                var update = new SectionUpdate
                { Section = section, NewWidth = img.Width, NewHeight = img.Height };
                var ok = true;
                var rowChars = new System.Text.StringBuilder(img.Width);
                for (var y = 0; y < img.Height && ok; y++)
                {
                    rowChars.Clear();
                    for (var x = 0; x < img.Width; x++)
                    {
                        var px = img.Pixels[y * img.Width + x];
                        if (px.a == 0) { rowChars.Append('.'); continue; }
                        if (colorToChar.TryGetValue(px, out var existing))
                        { rowChars.Append(existing); continue; }

                        // 新颜色
                        if (palette != null && !palette.ContainsRgb(px))
                        {
                            plan.Errors.Add(
                                $"[{section.Name ?? baseName}]({x},{y}): " +
                                $"#{px.r:x2}{px.g:x2}{px.b:x2} is not on the palette; " +
                                $"add it to the .gpl or fix it in the art tool");
                            ok = false;
                            break;
                        }
                        if (doc.CharsHeaderLine == 0)
                        {
                            plan.Errors.Add(
                                "new colors found but the file has no 'chars:' block; add one first");
                            ok = false;
                            break;
                        }
                        char newCh = default;
                        var found = false;
                        while (alphabetCursor < Alphabet.Length)
                        {
                            var cand = Alphabet[alphabetCursor++];
                            if (usedChars.Add(cand)) { newCh = cand; found = true; break; }
                        }
                        if (!found)
                        {
                            plan.Errors.Add(
                                "ran out of palette characters — this image is not " +
                                "limited-palette pixel art; quantize first");
                            ok = false;
                            break;
                        }
                        plan.NewChars.Add((newCh, ValueFor(px, palette)));
                        colorToChar[px] = newCh;
                        rowChars.Append(newCh);
                    }
                    if (ok) update.Rows.Add(rowChars.ToString());
                }
                if (ok) plan.Updates.Add(update);
            }

            foreach (var name in pngs.Keys.OrderBy(n => n, StringComparer.Ordinal))
            {
                if (matchedFiles.Contains(name)) continue;
                if (name.StartsWith(baseName + ".", StringComparison.Ordinal) ||
                    name == baseName + ".png")
                {
                    plan.ExtraPngs.Add(name);
                }
            }
            return plan;
        }

        // 新 chars 条目的值写法：palette 模式且整 alpha 且命中有名条目 → 色名；否则 hex。
        private static string ValueFor(Color32 px, GplPalette palette)
        {
            if (palette != null && px.a == 255)
            {
                foreach (var (color, name) in palette.Entries)
                {
                    if (name != null && color.r == px.r && color.g == px.g && color.b == px.b)
                        return name;
                }
            }
            return px.a == 255
                ? $"#{px.r:x2}{px.g:x2}{px.b:x2}"
                : $"#{px.r:x2}{px.g:x2}{px.b:x2}{px.a:x2}";
        }
    }
}
