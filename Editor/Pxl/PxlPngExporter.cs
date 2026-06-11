using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace PromptUGUI.Editor
{
    /// <summary>工具 1：.pxl 节 → PNG（spec 2026-06-11-pxl-png-roundtrip §3）。
    /// 文件名是往返配对契约：显式节 = "&lt;basename&gt;.&lt;section&gt;.png"，
    /// 隐式单节 = "&lt;basename&gt;.png"。编码复用 PxlImporter.BuildTexture
    /// （同一像素构建路径），不依赖导入产物。</summary>
    internal static class PxlPngExporter
    {
        public static string FileNameFor(string baseName, PxlSection s) =>
            s.Name == null ? baseName + ".png" : $"{baseName}.{s.Name}.png";

        public static byte[] EncodeSection(PxlSection s,
            IReadOnlyDictionary<char, Color32> colors)
        {
            var tex = PxlImporter.BuildTexture(s, colors, s.Name ?? "section");
            try { return tex.EncodeToPNG(); }
            finally { UnityEngine.Object.DestroyImmediate(tex); }
        }

        /// <summary>导出目录落在任一 SpriteSet sourceFolder 之下时返回 true——
        /// 导出的 PNG 会被同步工具当作新 sprite 来源，产生重复 key/重复打包，
        /// UI 层据此弹确认警告。入参为 "Assets/..." 形式的项目相对路径。</summary>
        public static bool IsUnderAnySpriteSetSourceFolder(string assetsRelativeFolder)
        {
            if (string.IsNullOrEmpty(assetsRelativeFolder)) return false;
            var probe = assetsRelativeFolder.Replace('\\', '/').TrimEnd('/') + "/";
            foreach (var set in SpriteAtlasSyncer.FindAllSpriteSets())
            {
                if (set == null) continue;
                var folder = set.SourceFolderPath;
                if (string.IsNullOrEmpty(folder)) continue;
                var prefix = folder.TrimEnd('/') + "/";
                if (probe.StartsWith(prefix, StringComparison.Ordinal)) return true;
            }
            return false;
        }
    }
}
