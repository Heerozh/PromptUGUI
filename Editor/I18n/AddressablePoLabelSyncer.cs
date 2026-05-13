using System.Collections.Generic;
#if PROMPTUGUI_HAS_ADDRESSABLES
using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
#endif

namespace PromptUGUI.Editor.I18n
{
    /// <summary>
    /// Wires .po assets up for shipping via Addressables: places them in the AA
    /// default group when needed, applies the <c>Locale:&lt;locale&gt;</c> label the
    /// runtime resolver loads by, and scrubs any stale <c>Locale:*</c> label left
    /// over from a previous folder location. Driven explicitly by the menu item
    /// <c>Tools → PromptUGUI → I18n → Setup Addressables for Locale PO Files</c>
    /// (see <see cref="AddressablePoMenu"/>); never runs automatically on import.
    ///
    /// Pure helpers are unconditional and unit-tested in
    /// <c>AddressablePoLabelSyncerTests</c>; the Addressables-touching
    /// <c>MakeLocalePoFilesAddressable</c> sits behind
    /// <c>#if PROMPTUGUI_HAS_ADDRESSABLES</c> and is covered by integration tests
    /// under <c>PromptUGUI.Tests.EditMode.Addressables</c>.
    /// </summary>
    public static class AddressablePoLabelSyncer
    {
        /// <summary>
        /// Label prefix the runtime resolver expects. Keep this in lockstep with
        /// <c>UI.Locale.BuildLocaleLabel</c> in <c>LocaleAddressableResolverHelper.cs</c>.
        /// </summary>
        public const string LabelPrefix = "Locale:";

        /// <summary>
        /// Inspects a project-relative asset path and returns the locale string whose
        /// folder appears closest to the file, or null if no path segment matches one
        /// of <paramref name="knownLocales"/>. "Closest to the file" wins so a structure
        /// like <c>.../zh-Hans/sub/en/foo.po</c> resolves to <c>en</c>.
        /// </summary>
        public static string ComputeDesiredLocale(
            string assetPath, IReadOnlyCollection<string> knownLocales)
        {
            if (string.IsNullOrEmpty(assetPath) || knownLocales == null || knownLocales.Count == 0)
                return null;

            var localeSet = new HashSet<string>(knownLocales);
            var segments = assetPath.Replace('\\', '/').Split('/');
            // Skip the last segment (file name); walk parent folders from leaf upward.
            for (var i = segments.Length - 2; i >= 0; i--)
            {
                if (localeSet.Contains(segments[i])) return segments[i];
            }
            return null;
        }

        /// <summary>
        /// True when <paramref name="label"/> starts with <see cref="LabelPrefix"/>
        /// but doesn't match the <paramref name="currentLocale"/>'s expected label.
        /// Used to scrub stale <c>Locale:*</c> labels when a .po asset moves between
        /// locale folders. Non-Locale labels (e.g. authors' own grouping labels)
        /// are left untouched.
        /// </summary>
        public static bool IsStaleLocaleLabel(string label, string currentLocale)
        {
            if (string.IsNullOrEmpty(label)) return false;
            if (!label.StartsWith(LabelPrefix)) return false;
            var expected = LabelPrefix + (currentLocale ?? "");
            return label != expected;
        }

#if PROMPTUGUI_HAS_ADDRESSABLES
        /// <summary>
        /// For each .po asset path in <paramref name="assetPaths"/> that lives under a
        /// known-locale folder: ensure it's in an Addressables group (creating the
        /// entry in <c>settings.DefaultGroup</c> if not already grouped), then apply
        /// <c>Locale:&lt;locale&gt;</c> and scrub any stale <c>Locale:*</c> labels.
        /// Non-Locale labels are preserved.
        ///
        /// Paths that don't match a known locale are skipped (we don't guess which
        /// locale they belong to). Idempotent — repeated calls converge.
        /// </summary>
        public static void MakeLocalePoFilesAddressable(
            IReadOnlyList<string> assetPaths, IReadOnlyCollection<string> knownLocales)
        {
            if (assetPaths == null || assetPaths.Count == 0) return;
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null) return; // no AA configured; caller should handle UX

            var existingLabels = new HashSet<string>(settings.GetLabels());

            foreach (var path in assetPaths)
            {
                if (string.IsNullOrEmpty(path)) continue;
                var locale = ComputeDesiredLocale(path, knownLocales);
                if (locale == null) continue;

                var guid = AssetDatabase.AssetPathToGUID(path);
                if (string.IsNullOrEmpty(guid)) continue;

                var entry = settings.FindAssetEntry(guid)
                            ?? settings.CreateOrMoveEntry(guid, settings.DefaultGroup);

                var desired = LabelPrefix + locale;
                if (existingLabels.Add(desired))
                {
                    settings.AddLabel(desired, postEvent: false);
                }

                // Drop stale Locale:* labels (file moved between locale folders).
                foreach (var stale in entry.labels.Where(l => IsStaleLocaleLabel(l, locale)).ToList())
                {
                    entry.SetLabel(stale, enable: false, force: false, postEvent: false);
                }
                if (!entry.labels.Contains(desired))
                {
                    entry.SetLabel(desired, enable: true, force: true, postEvent: false);
                }
            }
        }
#endif
    }
}
