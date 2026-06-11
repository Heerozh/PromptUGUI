using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace PromptUGUI.Editor
{
    /// <summary>.pxl（像素网格文本，spec 2026-06-11-pxl-pixel-sprite-importer）→
    /// 每节一张 point-filter Texture2D + Sprite sub-asset。main asset = 首节
    /// Texture2D，保证 SpriteAtlasSyncer 的 FindAssets("t:Texture2D") 能发现。
    /// Texture 保持 readable：InlineSpriteAssetBuilder 烘焙图文混排时要读像素。</summary>
    [ScriptedImporter(1, "pxl")]
    internal sealed class PxlImporter : ScriptedImporter
    {
        public override void OnImportAsset(AssetImportContext ctx)
        {
            string text;
            try { text = File.ReadAllText(ctx.assetPath); }
            catch (IOException ex)
            {
                ctx.LogImportError($"{ctx.assetPath}: cannot read: {ex.Message}");
                return;
            }

            PxlDocument doc;
            try { doc = PxlParser.Parse(text); }
            catch (PxlParseException ex)
            {
                ctx.LogImportError($"{ctx.assetPath}: {ex.Message}");
                return;
            }

            GplPalette palette = null;
            if (doc.PaletteRef != null)
            {
                var gplPath = FindPalettePath(doc.PaletteRef, out var error);
                if (gplPath == null)
                {
                    ctx.LogImportError($"{ctx.assetPath}: {error}");
                    return;
                }
                // 色板改动 → 所有引用它的 .pxl 自动重导入（全项目换色一次完成）。
                ctx.DependsOnSourceAsset(gplPath);
                try { palette = GplPalette.Parse(File.ReadAllText(gplPath)); }
                catch (System.FormatException ex)
                {
                    ctx.LogImportError($"{gplPath}: {ex.Message}");
                    return;
                }
            }

            System.Collections.Generic.Dictionary<char, Color32> colors;
            try { colors = PxlColorResolver.Resolve(doc, palette); }
            catch (PxlParseException ex)
            {
                ctx.LogImportError($"{ctx.assetPath}: {ex.Message}");
                return;
            }

            var basename = Path.GetFileNameWithoutExtension(ctx.assetPath);
            Texture2D main = null;
            foreach (var section in doc.Sections)
            {
                var name = section.Name ?? basename;
                var tex = BuildTexture(section, colors, name);
                var sprite = Sprite.Create(tex,
                    new Rect(0, 0, section.Width, section.Height),
                    new Vector2(0.5f, 0.5f), doc.Ppu, 0,
                    SpriteMeshType.FullRect, section.Border);
                sprite.name = name;
                ctx.AddObjectToAsset($"tex:{name}", tex);
                ctx.AddObjectToAsset($"sprite:{name}", sprite);
                if (main == null) main = tex;
            }
            ctx.SetMainObject(main);
        }

        private static Texture2D BuildTexture(PxlSection section,
            System.Collections.Generic.IReadOnlyDictionary<char, Color32> colors, string name)
        {
            var w = section.Width;
            var h = section.Height;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                name = name,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                alphaIsTransparency = true,
            };
            var px = new Color32[w * h];
            for (var row = 0; row < h; row++)        // grid top-down → texture bottom-up
                for (var col = 0; col < w; col++)
                    px[(h - 1 - row) * w + col] = colors[section.Rows[row][col]];
            tex.SetPixels32(px);
            tex.Apply(updateMipmaps: false, makeNoLongerReadable: false);
            return tex;
        }

        /// <summary>按文件名（去扩展名）全项目找 &lt;name&gt;.gpl。0 个或多个都报错
        /// （error out 参数带候选列表）。</summary>
        private static string FindPalettePath(string paletteRef, out string error)
        {
            var matches = AssetDatabase.FindAssets(paletteRef)
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => string.Equals(Path.GetFileName(p), paletteRef + ".gpl",
                    System.StringComparison.Ordinal))
                .Distinct()
                .OrderBy(p => p, System.StringComparer.Ordinal)
                .ToList();
            if (matches.Count == 1) { error = null; return matches[0]; }
            error = matches.Count == 0
                ? $"palette '@{paletteRef}' not found (no '{paletteRef}.gpl' in project)"
                : $"palette '@{paletteRef}' is ambiguous: {string.Join(", ", matches)}";
            return null;
        }
    }
}
