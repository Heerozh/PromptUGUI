using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace PromptUGUI.Editor
{
    /// <summary>Assets ▸ Create ▸ PromptUGUI ▸ Pxl Sprite from PNG: pixel-reads the
    /// selected .png, quantizes to a color budget, and writes a sibling .pxl. The
    /// validator greys the item out unless a .png asset is selected. Pixel logic and
    /// .pxl text generation live in PxlFromPng; this file is only the Editor plumbing
    /// (selection, PNG decode, file write).</summary>
    internal static class CreatePxlFromPngMenu
    {
        internal const int DefaultMaxColors = 8;

        [MenuItem("Assets/Create/PromptUGUI/Pxl Sprite from PNG", false, 85)]
        private static void CreateFromPng()
        {
            var path = SelectedPngPath();
            if (path == null)
            {
                EditorUtility.DisplayDialog("Pxl Sprite from PNG",
                    "Select a .png asset in the Project window first.", "OK");
                return;
            }
            PxlFromPngWindow.Open(path, DefaultMaxColors);
        }

        [MenuItem("Assets/Create/PromptUGUI/Pxl Sprite from PNG", true)]
        private static bool CreateFromPngValidate() => SelectedPngPath() != null;

        // Project-relative path of the selected .png, or null if the selection is not one.
        internal static string SelectedPngPath()
        {
            var obj = Selection.activeObject;
            if (obj == null) return null;
            var p = AssetDatabase.GetAssetPath(obj);
            return !string.IsNullOrEmpty(p) &&
                   p.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                ? p
                : null;
        }

        /// <summary>Decode + convert + write. Static so the window can defer it past the
        /// IMGUI frame (asset writes mid-OnGUI are unsafe — same delayCall convention as
        /// PxlImporterEditor). Returns the created asset path, or null on any failure
        /// (a dialog has already been shown).</summary>
        internal static string Generate(string pngPath, int maxColors)
        {
            byte[] bytes;
            try { bytes = File.ReadAllBytes(pngPath); }
            catch (IOException ex)
            {
                EditorUtility.DisplayDialog("Pxl Sprite from PNG", $"Cannot read PNG:\n{ex.Message}", "OK");
                return null;
            }

            // Decode raw bytes so the source PNG's readable/compression import settings
            // don't matter (mirrors PxlImporterEditor.SyncFromPng). GetPixels32 is
            // bottom-up; flip to the top-down order .pxl grids use.
            int w, h;
            Color32[] topDown;
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!ImageConversion.LoadImage(tex, bytes))
                {
                    EditorUtility.DisplayDialog("Pxl Sprite from PNG", "Not a valid PNG file.", "OK");
                    return null;
                }
                w = tex.width;
                h = tex.height;
                var bottomUp = tex.GetPixels32();
                topDown = new Color32[w * h];
                for (var y = 0; y < h; y++)
                    Array.Copy(bottomUp, (h - 1 - y) * w, topDown, y * w, w);
            }
            finally { UnityEngine.Object.DestroyImmediate(tex); }

            // .pxl grids are text meant for small pixel art; confirm before dumping a huge one.
            if (w > 128 || h > 128)
            {
                if (!EditorUtility.DisplayDialog("Large image",
                        $"This PNG is {w}×{h}. .pxl is a text grid meant for small pixel art — " +
                        $"the file will have {h} rows of {w} characters.\n\nGenerate anyway?",
                        "Generate", "Cancel"))
                    return null;
            }

            var text = PxlFromPng.Convert(topDown, w, h, maxColors);

            var dir = Path.GetDirectoryName(pngPath);
            var candidate = Path.Combine(dir, Path.GetFileNameWithoutExtension(pngPath) + ".pxl")
                .Replace('\\', '/');
            var outPath = AssetDatabase.GenerateUniqueAssetPath(candidate);
            try { File.WriteAllText(outPath, text); }
            catch (IOException ex)
            {
                EditorUtility.DisplayDialog("Pxl Sprite from PNG", $"Cannot write .pxl:\n{ex.Message}", "OK");
                return null;
            }
            AssetDatabase.ImportAsset(outPath);
            var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(outPath);
            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
            return outPath;
        }
    }

    /// <summary>Tiny utility window asking for the color budget before generating.
    /// Unity has no built-in integer prompt; a modal-ish utility window is the norm.</summary>
    internal sealed class PxlFromPngWindow : EditorWindow
    {
        private string _pngPath;
        private int _maxColors;

        public static void Open(string pngPath, int defaultMaxColors)
        {
            var w = CreateInstance<PxlFromPngWindow>();
            w._pngPath = pngPath;
            w._maxColors = defaultMaxColors;
            w.titleContent = new GUIContent("Pxl from PNG");
            w.minSize = w.maxSize = new Vector2(340, 116);
            w.ShowUtility();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Source", Path.GetFileName(_pngPath ?? ""));
            _maxColors = EditorGUILayout.IntField(
                new GUIContent("Max colors",
                    "Palette size budget. If the PNG has more distinct colors it is " +
                    "quantized down to this many; fewer is left untouched."),
                _maxColors);
            _maxColors = Mathf.Clamp(_maxColors, 2, PxlChars.Alphabet.Length);

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Cancel")) Close();
                if (GUILayout.Button("Create"))
                {
                    var path = _pngPath;
                    var max = _maxColors;
                    Close();
                    // Defer past this IMGUI frame before touching the AssetDatabase.
                    EditorApplication.delayCall += () => CreatePxlFromPngMenu.Generate(path, max);
                }
            }
        }
    }
}
