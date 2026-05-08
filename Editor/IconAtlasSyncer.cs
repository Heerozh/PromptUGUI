using System;
using System.Collections.Generic;
using System.IO;
using PromptUGUI.IR;
using PromptUGUI.Parser;
using UnityEditor;
using UnityEngine;

namespace PromptUGUI.Editor {
    public static class IconAtlasSyncer {
        const string DynamicMarker = "{{";

        /// <summary>(setName, iconName) pairs found across all .ui.xml in the project.</summary>
        public static HashSet<(string set, string name)> ScanXmlReferences() {
            var refs = new HashSet<(string, string)>();
            var guids = AssetDatabase.FindAssets("t:TextAsset");
            foreach (var guid in guids) {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith(".ui.xml", StringComparison.Ordinal)) continue;
                string text;
                try { text = File.ReadAllText(path); }
                catch (IOException ex) {
                    Debug.LogWarning($"[IconSync] cannot read {path}: {ex.Message}");
                    continue;
                }
                UIDocument doc;
                try { doc = UIDocumentParser.Parse(text); }
                catch (ParseException ex) {
                    Debug.LogWarning($"[IconSync] skipping malformed {path}: {ex.Message}");
                    continue;
                }
                foreach (var screen in doc.Screens)
                    CollectFromNode(screen.Root, refs, path);
                foreach (var tpl in doc.Templates.Values)
                    if (tpl.Body != null) CollectFromNode(tpl.Body, refs, path);
            }
            return refs;
        }

        static void CollectFromNode(ElementNode node,
                                    HashSet<(string, string)> refs, string path) {
            if (node == null) return;
            if (node.Tag == "Icon" && node.Namespace == null) {
                CollectFromAttr(node.Attributes.TryGetValue("name", out var n) ? n : null,
                                refs, path);
                if (node.VariantOverrides.TryGetValue("name", out var list))
                    foreach (var (_, v) in list) CollectFromAttr(v, refs, path);
            }
            foreach (var c in node.Children) CollectFromNode(c, refs, path);
        }

        static void CollectFromAttr(string value,
                                    HashSet<(string, string)> refs, string path) {
            if (string.IsNullOrEmpty(value)) return;
            int colon = value.IndexOf(':');
            if (colon <= 0 || colon == value.Length - 1) return;
            var ns = value.Substring(0, colon);
            var name = value.Substring(colon + 1);
            if (ns.Contains(DynamicMarker)) {
                Debug.LogWarning(
                    $"[IconSync] {path}: <Icon name='{value}'>: dynamic namespace " +
                    $"({DynamicMarker}...) is not analyzable; skipping");
                return;
            }
            if (name.Contains(DynamicMarker)) {
                Debug.LogWarning(
                    $"[IconSync] {path}: <Icon name='{value}'>: dynamic icon name " +
                    $"({DynamicMarker}...); list candidates in IconSet.alwaysInclude");
                return;
            }
            refs.Add((ns, name));
        }
    }
}
