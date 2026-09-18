using System;
using System.Collections.Generic;
using System.IO;
using PromptUGUI.Application;
using PromptUGUI.IR;
using PromptUGUI.Lint;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;
#if PROMPTUGUI_HAS_ADDRESSABLES
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
#endif

namespace PromptUGUI.Editor
{
    /// <summary>
    /// <c>Tools › PromptUGUI › Lint All UI XML</c>: the UIXmlLint CLI, run inside the Editor over
    /// every <c>.ui.xml</c> the project owns, printed to the Console. Same rules, same two passes
    /// (raw + expanded), same spelling of a finding — <see cref="LintRun"/> owns all of that; this
    /// class only decides which files, how an <c>&lt;Import src&gt;</c> maps to one of them, and
    /// where a line goes.
    ///
    /// <para>Why a menu when the CLI exists: the CLI needs a dotnet SDK and a terminal, and its
    /// warnings compete with the runtime's, which only appear once a Screen is opened. Here every
    /// rule fires at once, across the whole project, each line pinging the offending asset.</para>
    ///
    /// <para>What the Editor can do that the CLI cannot: ask Addressables. The CLI guesses a src's
    /// file by walking up from the importing file to a <c>Resources/</c> folder (the
    /// <c>UseResourcesResolver</c> shape); an Addressables address has no such shape, so the CLI
    /// skips the expanded pass for those documents. The Editor reads the address → asset map
    /// straight from the Addressables settings, so <c>UseAddressableResolver</c> projects get the
    /// expanded pass too.</para>
    ///
    /// <para>Common libraries come from <see cref="PromptUGUISettings.commonLibraries"/> — the one
    /// declaration the runtime reads too — and are merged into every document's expanded pass the
    /// way <c>UI.EnsureCommonLibrariesAsync</c> merges them at load (2026-09-18 commons-settings
    /// spec §4.8), so a <c>class=</c> that lives only in the theme library is not an unknown name.</para>
    /// </summary>
    internal static class UIXmlLintMenu
    {
        private const string MenuPath = "Tools/PromptUGUI/Lint All UI XML";

        [MenuItem(MenuPath)]
        private static void LintAll()
        {
            var paths = FindProjectUiXml();
            if (paths.Count == 0)
            {
                Debug.Log("[PromptUGUI] Lint: no .ui.xml under Assets/ or an embedded package.");
                return;
            }

            var commons = ConfiguredCommonLibraries();
            var run = Lint(paths, Report, commons);

            if (run.Issues > 0)
                Debug.LogWarning($"[PromptUGUI] Lint: {run.Issues} issue(s) across {run.Files} file(s) — see the lines above.");
            else
                Debug.Log($"[PromptUGUI] Lint: no issues across {run.Files} file(s).");

            // A note is logged once per unresolved library; how much of the run it cost is said here.
            if (run.Skipped > 0)
                Debug.Log($"[PromptUGUI] Lint: {run.Skipped} of {run.Files} file(s) skipped the expanded pass over a src " +
                          "that neither Addressables nor a Resources/ walk could resolve (see the notes above).");

            // An unknown style / template in a project that declares no common library is most
            // often a library nobody listed — the same hint UI.LoadDocumentAsync gives (spec §4.10).
            // With libraries declared the name is genuinely unknown and the finding stands alone.
            if (run.SawUnknownName && commons.Count == 0)
                Debug.Log("[PromptUGUI] No common library is configured in PromptUGUISettings — if these " +
                          "names live in a shared library, list it under Common Libraries.");
        }

        /// <summary>
        /// Lints <paramref name="paths"/> (asset paths, or absolute files) in order, handing every
        /// finding to <paramref name="report"/>. The <see cref="LintRun"/> comes back for its counts.
        /// </summary>
        /// <param name="commons">
        /// The <c>&lt;Import&gt;</c> every document implicitly has — <see cref="ConfiguredCommonLibraries"/>
        /// for the menu; a test hands its own rows.
        /// </param>
        internal static LintRun Lint(IReadOnlyList<string> paths, Action<LintRun.Finding> report,
                                     IReadOnlyList<ImportRef> commons = null)
        {
            var run = new LintRun();
            var resolve = MakeResolver();
            try
            {
                for (var i = 0; i < paths.Count; i++)
                {
                    // The path becomes the document's origin, i.e. the "file:" of every line about
                    // it — spell it the AssetDatabase way whatever the caller did.
                    var path = paths[i].Replace('\\', '/');
                    EditorUtility.DisplayProgressBar("PromptUGUI Lint", path, (float)i / paths.Count);
                    foreach (var finding in run.Lint(path, Read, resolve, commons))
                        report(finding);
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
            return run;
        }

        /// <summary>
        /// The rows of <see cref="PromptUGUISettings.commonLibraries"/> as import refs — the one
        /// declaration the runtime, this menu and the CLI all read (2026-09-18 commons-settings
        /// spec). Blank rows dropped, whitespace trimmed, a blank <c>as</c> is no namespace. Empty
        /// when the project has no settings asset.
        /// </summary>
        internal static List<ImportRef> ConfiguredCommonLibraries()
        {
            var refs = new List<ImportRef>();
            var rows = PromptUGUISettings.Instance?.commonLibraries;
            if (rows == null) return refs;
            foreach (var row in rows)
            {
                if (row == null || row.IsBlank) continue;
                refs.Add(row.ToImportRef());
            }
            return refs;
        }

        // ── which files ────────────────────────────────────────────────────────────────────────

        /// <summary>Every <c>.ui.xml</c> the user can edit, sorted so the Console reads like a directory.</summary>
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

        private static string Read(string path) => File.ReadAllText(Physical(path));

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

        private static string ProjectRoot =>
            Path.GetDirectoryName(UnityEngine.Application.dataPath).Replace('\\', '/').TrimEnd('/');

        // ── <Import src> → file ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Addressables first (exact: the address or GUID the runtime would load), then the CLI's
        /// on-disk guess started from the importing file's real folder. Either way the answer is an
        /// asset path, so a finding inside the imported library pings that library.
        /// </summary>
        private static Func<string, string, string> MakeResolver()
        {
            var addressable = AddressableUiXml();
            return (src, importing) =>
            {
                if (addressable.TryGetValue(src, out var asset)) return asset;
                var hit = ImportClosure.ResolveInResources(
                    src, Path.GetDirectoryName(Physical(importing)), File.Exists);
                return hit == null ? null : ToAssetPath(hit);
            };
        }

#if PROMPTUGUI_HAS_ADDRESSABLES
        /// <summary>
        /// Address → asset path and GUID → asset path for every addressable <c>.ui.xml</c>, folder
        /// entries included. <c>LoadDocumentAsync(string)</c> keys by address,
        /// <c>LoadDocumentAsync(AssetReferenceT)</c> by GUID; an <c>&lt;Import src&gt;</c> may be
        /// either. Empty when the project has no Addressables settings.
        /// </summary>
        private static Dictionary<string, string> AddressableUiXml()
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
        private static Dictionary<string, string> AddressableUiXml() => new Dictionary<string, string>();
#endif

        // ── Console ────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// One Console line per finding, the asset as context so a click pings it. Severity follows
        /// what the runtime would do with the document: an unbuildable one (unreadable, unparseable,
        /// expansion failed) throws at <c>UI.Open</c> — an error; a rule finding is what
        /// <c>UILog.Warn</c> would say — a warning; a skipped expanded pass is information.
        /// </summary>
        private static void Report(LintRun.Finding finding)
        {
            // A configuration finding names no file; everything else pings the asset it is about.
            var context = finding.File == null ? null : AssetDatabase.LoadAssetAtPath<TextAsset>(finding.File);
            switch (finding.Kind)
            {
                case LintRun.Kind.Error:
                    Debug.LogError(finding.Text, context);
                    break;
                case LintRun.Kind.Issue:
                    Debug.LogWarning(finding.Text, context);
                    break;
                default:
                    Debug.Log("[PromptUGUI] " + finding.Text, context);
                    break;
            }
        }
    }
}
