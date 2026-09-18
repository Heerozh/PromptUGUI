using System;
using System.Collections.Generic;
using System.IO;
using PromptUGUI.Lint;
using UnityEditor;
using UnityEditor.PackageManager;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;
#if PROMPTUGUI_HAS_ADDRESSABLES
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
#endif

namespace PromptUGUI.Editor
{
    /// <summary>
    /// The Editor's one answer to "which file is that" for <c>.ui.xml</c>: which files the project
    /// owns, asset path ↔ physical file, and — the part only the Editor can do — a resolver key
    /// (<c>&lt;Import src&gt;</c>, a common-library row, a Screen's src) → the asset that key names.
    /// <c>Tools › PromptUGUI › Lint All UI XML</c> and the UI Preview tool both go through here so a
    /// src lands on the same file in both (2026-09-18 ui-preview-tool spec §4.4).
    ///
    /// <para><see cref="Locate"/> asks Addressables first — exact: the address or GUID the runtime
    /// would load, whatever the file is called — then falls back to the CLI's on-disk guess, the
    /// <c>UseResourcesResolver</c> shape walked up from the anchoring file's real folder to its
    /// <c>Resources/</c>. Either way the answer is an asset path, so a finding inside an imported
    /// library pings that library and a preview reads the very file the author is editing.</para>
    /// </summary>
    internal static class UiXmlLocator
    {
        // ── which files ────────────────────────────────────────────────────────────────────────

        /// <summary>Every <c>.ui.xml</c> the user can edit, sorted so a list reads like a directory.</summary>
        internal static List<string> FindProjectUiXml()
        {
            var result = new List<string>();
            foreach (var guid in AssetDatabase.FindAssets("t:TextAsset"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.EndsWith(".ui.xml", StringComparison.Ordinal) && IsProjectOwned(path))
                    result.Add(path);
            }
            result.Sort(StringComparer.Ordinal);
            return result;
        }

        /// <summary>
        /// <c>Assets/</c> and embedded packages — what lives in the user's repository. A local /
        /// registry / git package is somebody else's (this one included): its findings are not
        /// theirs to fix, and its own lint runs in its own repo.
        /// </summary>
        internal static bool IsProjectOwned(string assetPath)
        {
            if (assetPath.StartsWith("Assets/", StringComparison.Ordinal)) return true;
            var package = PackageInfo.FindForAssetPath(assetPath);
            return package != null && package.source == PackageSource.Embedded;
        }

        // ── asset path ↔ file ──────────────────────────────────────────────────────────────────

        /// <summary>
        /// The file behind an asset path. <c>Packages/&lt;name&gt;/…</c> of a local or cached
        /// package is virtual — the folder is wherever the package manager resolved it — and
        /// <c>Assets/…</c> is relative to the project, not to whatever the working directory is.
        /// An absolute path is already the answer.
        /// </summary>
        internal static string Physical(string path)
        {
            if (Path.IsPathRooted(path)) return path;
            var physical = FileUtil.GetPhysicalPath(path);   // Assets/… comes back as is
            if (string.IsNullOrEmpty(physical)) physical = path;
            return Path.IsPathRooted(physical) ? physical : Path.Combine(ProjectRoot, physical);
        }

        /// <summary>
        /// The asset path of a file: <c>Assets/…</c> (or an embedded <c>Packages/…</c>) for one
        /// under the project, <c>Packages/&lt;name&gt;/…</c> for one inside any registered package,
        /// else the absolute path — always with forward slashes, the way <c>AssetDatabase</c> spells
        /// it and the way a Console line can be turned into a ping.
        /// </summary>
        internal static string ToAssetPath(string physical)
        {
            var full = Path.GetFullPath(physical).Replace('\\', '/');

            var root = ProjectRoot + "/";
            if (full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                return full.Substring(root.Length);

            foreach (var package in PackageInfo.GetAllRegisteredPackages())
            {
                if (string.IsNullOrEmpty(package.resolvedPath)) continue;
                var packageRoot = package.resolvedPath.Replace('\\', '/').TrimEnd('/') + "/";
                if (full.StartsWith(packageRoot, StringComparison.OrdinalIgnoreCase))
                    return package.assetPath + "/" + full.Substring(packageRoot.Length);
            }
            return full;
        }

        internal static string ProjectRoot =>
            Path.GetDirectoryName(UnityEngine.Application.dataPath).Replace('\\', '/').TrimEnd('/');

        // ── src → file ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// The asset path a resolver key names, or null when nothing in the project answers to it.
        /// Addressables first (exact: the address or GUID the runtime would load), then the CLI's
        /// on-disk guess started from <paramref name="anchorPath"/>'s real folder — the file that
        /// wrote the <c>&lt;Import&gt;</c>, or the entry document a common library is implied by.
        /// No anchor (null, or a file that is not on disk) means no Resources walk, only the map.
        /// </summary>
        internal static string Locate(string src, string anchorPath) => Locate(src, anchorPath, AddressableUiXml());

        /// <summary>
        /// <see cref="Locate"/> with the Addressables map read once and reused — what a run over
        /// many files wants (a lint over the whole project asks per src).
        /// </summary>
        internal static Func<string, string, string> MakeResolver()
        {
            var addressable = AddressableUiXml();
            return (src, anchorPath) => Locate(src, anchorPath, addressable);
        }

        private static string Locate(string src, string anchorPath, Dictionary<string, string> addressable)
        {
            if (string.IsNullOrEmpty(src)) return null;
            if (addressable.TryGetValue(src, out var asset)) return asset;
            if (string.IsNullOrEmpty(anchorPath)) return null;

            var anchorDir = Path.GetDirectoryName(Physical(anchorPath));
            if (string.IsNullOrEmpty(anchorDir) || !Directory.Exists(anchorDir)) return null;
            var hit = ImportClosure.ResolveInResources(src, anchorDir, File.Exists);
            return hit == null ? null : ToAssetPath(hit);
        }

#if PROMPTUGUI_HAS_ADDRESSABLES
        /// <summary>
        /// Address → asset path and GUID → asset path for every addressable <c>.ui.xml</c>, folder
        /// entries included. <c>LoadDocumentAsync(string)</c> keys by address,
        /// <c>LoadDocumentAsync(AssetReferenceT)</c> by GUID; an <c>&lt;Import src&gt;</c> may be
        /// either. Empty when the project has no Addressables settings. Rebuilt per call — the
        /// settings can change between two menu runs, and a run reads it once per src, cheaply.
        /// </summary>
        internal static Dictionary<string, string> AddressableUiXml()
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null) return map;

            var entries = new List<AddressableAssetEntry>();
            settings.GetAllAssets(entries, includeSubObjects: false);
            foreach (var entry in entries)
            {
                var path = entry?.AssetPath;
                if (string.IsNullOrEmpty(path) || !path.EndsWith(".ui.xml", StringComparison.Ordinal)) continue;
                if (!string.IsNullOrEmpty(entry.address)) map[entry.address] = path;
                if (!string.IsNullOrEmpty(entry.guid)) map[entry.guid] = path;
            }
            return map;
        }
#else
        internal static Dictionary<string, string> AddressableUiXml() => new Dictionary<string, string>();
#endif
    }
}
