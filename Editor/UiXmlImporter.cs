using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEditor.PackageManager;
using UnityEngine;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace PromptUGUI.Editor
{
    /// <summary>
    /// Imports a <c>.ui.xml</c> as a <see cref="TextAsset"/> with its comments taken out
    /// (<see cref="XmlCommentStripper"/>): they stay in the source and out of the build. Resources,
    /// Addressables and any resolver reading <c>TextAsset.text</c> get the stripped text, in the Editor
    /// and in a Player alike, and every element keeps its line, so <c>UILog</c>'s <c>src:line</c> still
    /// names the source (spec 2026-09-27-ui-xml-comment-stripping).
    ///
    /// <para><c>.xml</c> belongs to Unity's native TextScriptImporter, so this can only be an override
    /// (<c>overrideExts</c>), set per file by <see cref="UiXmlImporterAssigner"/> — the same arrangement
    /// as <see cref="PoFileImporter"/>.</para>
    /// </summary>
    [ScriptedImporter(1, (string[])null, new[] { "xml" })]
    internal sealed class UiXmlImporter : ScriptedImporter
    {
        public override void OnImportAsset(AssetImportContext ctx)
        {
            var text = XmlCommentStripper.Strip(File.ReadAllText(ctx.assetPath), out var keptBecause);
            if (keptBecause != null)
                ctx.LogImportWarning(
                    $"[PromptUGUI] {ctx.assetPath} was imported as written, comments included: {keptBecause}.");

            // "Foo.ui", the name TextScriptImporter gives it
            var asset = new TextAsset(text) { name = Path.GetFileNameWithoutExtension(ctx.assetPath) };
            ctx.AddObjectToAsset("text", asset);
            ctx.SetMainObject(asset);
        }
    }

    /// <summary>
    /// Keeps <see cref="UiXmlImporter"/> on exactly the <c>*.ui.xml</c> files: new and renamed ones as
    /// they come through the import pipeline, and — once per domain reload — the ones imported before the
    /// importer existed, which adding an override-only importer does not reimport.
    /// </summary>
    [InitializeOnLoad]
    internal static class UiXmlImporterAssigner
    {
        /// <summary>Stops the postprocessor, so a test can put a file back the way an upgrade finds it.</summary>
        internal static bool PausedForTests;

        static UiXmlImporterAssigner()
        {
            EditorApplication.delayCall += Sweep;
        }

        internal static void Sweep()
        {
            var paths = new List<string>();
            foreach (var path in AssetDatabase.GetAllAssetPaths())
                if (path.EndsWith(".ui.xml", StringComparison.Ordinal)) paths.Add(path);
            Assign(paths);
        }

        internal static void Assign(IEnumerable<string> paths)
        {
            foreach (var path in paths)
            {
                if (!path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)) continue;
                var wanted = path.EndsWith(".ui.xml", StringComparison.Ordinal);
                var ours = AssetDatabase.GetImporterOverride(path) == typeof(UiXmlImporter);
                if (wanted == ours || !IsWritable(path)) continue;
                if (wanted) AssetDatabase.SetImporterOverride<UiXmlImporter>(path);
                else AssetDatabase.ClearImporterOverride(path);   // Foo.ui.xml renamed to Foo.xml
            }
        }

        // A git / registry package is read-only: its .meta cannot take the override. This package's own
        // .ui.xml.meta files ship with it.
        private static bool IsWritable(string path)
        {
            if (!path.StartsWith("Packages/", StringComparison.Ordinal)) return true;
            var package = PackageInfo.FindForAssetPath(path);
            return package == null || package.source == PackageSource.Embedded || package.source == PackageSource.Local;
        }

        private sealed class Postprocessor : AssetPostprocessor
        {
            private static void OnPostprocessAllAssets(
                string[] importedAssets, string[] deletedAssets,
                string[] movedAssets, string[] movedFromAssetPaths)
            {
                if (PausedForTests) return;
                Assign(importedAssets);
                Assign(movedAssets);
            }
        }
    }
}
