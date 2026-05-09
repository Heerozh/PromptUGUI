using System.IO;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace PromptUGUI.Editor {
    // Unity 6 + com.unity.localization registers .po as a native/high-priority importer.
    // The overrideFileExtensions attribute makes PoFileImporter available as an override
    // for per-path assignment via AssetDatabase.SetImporterOverride<PoFileImporter>(path).
    // PoFilePostprocessor below auto-applies this override to every .po asset.
    [ScriptedImporter(1, (string[])null, new[] { "po" })]
    internal sealed class PoFileImporter : ScriptedImporter {
        public override void OnImportAsset(AssetImportContext ctx) {
            var text = File.ReadAllText(ctx.assetPath);
            var asset = new TextAsset(text);
            ctx.AddObjectToAsset("text", asset);
            ctx.SetMainObject(asset);
        }
    }
}
