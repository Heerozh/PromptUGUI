// Editor/I18n/TranslationMenu.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using PromptUGUI.Application;
using PromptUGUI.I18n;
using UnityEditor;
using UnityEngine;

namespace PromptUGUI.Editor.I18n {
    internal static class TranslationMenu {
        const string I18nRoot = "Assets/Resources/PromptUGUI/i18n";
        const int BatchSize = 50;

        [MenuItem("Tools/PromptUGUI/Translate Locale...")]
        public static void OpenDialog() {
            var settings = PromptUGUISettings.Instance;
            if (settings == null || settings.locales.Count == 0) {
                EditorUtility.DisplayDialog(
                    "PromptUGUI",
                    "No PromptUGUISettings or no locales configured.",
                    "OK");
                return;
            }
            TranslateLocaleWindow.Show(
                settings.locales.Select(l => l.locale).Where(s => !string.IsNullOrEmpty(s)).ToArray(),
                RunFor);
        }

        sealed class TranslateLocaleWindow : EditorWindow {
            string[] _locales;
            int _selected;
            System.Action<string> _onConfirm;

            public static void Show(string[] locales, System.Action<string> onConfirm) {
                var w = CreateInstance<TranslateLocaleWindow>();
                w.titleContent = new GUIContent("Translate Locale");
                w._locales = locales;
                w._onConfirm = onConfirm;
                w.minSize = new Vector2(320, 100);
                w.ShowUtility();
            }

            void OnGUI() {
                EditorGUILayout.LabelField("Target locale:");
                _selected = EditorGUILayout.Popup(_selected, _locales);
                EditorGUILayout.Space();
                using (new EditorGUILayout.HorizontalScope()) {
                    if (GUILayout.Button("Cancel")) Close();
                    if (GUILayout.Button("Translate")) {
                        var picked = _locales[_selected];
                        Close();
                        _onConfirm(picked);
                    }
                }
            }
        }

        static void RunFor(string locale) {
            var auth = TranslationProviderSettingsProvider.GetOrCreateAuth();
            var provider = TranslationProviderSettingsProvider.GetOrCreateProvider();
            if (string.IsNullOrEmpty(auth.apiKey)) {
                EditorUtility.DisplayDialog("PromptUGUI",
                    "No API key set. Edit at Project Settings → PromptUGUI → Translation.",
                    "OK");
                return;
            }

            var localeDir = Path.Combine(I18nRoot, locale);
            if (!Directory.Exists(localeDir)) {
                EditorUtility.DisplayDialog("PromptUGUI",
                    $"No .po files for locale '{locale}'. Run Extract Strings first.",
                    "OK");
                return;
            }

            // Collect empty-msgstr entries.
            var queue = new List<(string poPath, PoEntry entry)>();
            foreach (var po in Directory.GetFiles(localeDir, "*.po", SearchOption.AllDirectories)) {
                var text = File.ReadAllText(po);
                foreach (var e in PoParser.Parse(text)) {
                    if (string.IsNullOrEmpty(e.Msgstr)) queue.Add((po, e));
                }
            }
            if (queue.Count == 0) {
                EditorUtility.DisplayDialog("PromptUGUI",
                    "No empty msgstr entries to translate.", "OK");
                return;
            }
            if (!EditorUtility.DisplayDialog(
                "PromptUGUI",
                $"Translate {queue.Count} empty entries for locale '{locale}'?",
                "Translate", "Cancel")) return;

            var client = new TranslationClient();
            int done = 0;
            using var cts = new CancellationTokenSource();
            try {
                for (int i = 0; i < queue.Count; i += BatchSize) {
                    var slice = queue.Skip(i).Take(BatchSize).ToList();
                    if (EditorUtility.DisplayCancelableProgressBar(
                        "PromptUGUI Translate",
                        $"Batch {i / BatchSize + 1} / {(queue.Count + BatchSize - 1) / BatchSize}",
                        (float)i / queue.Count)) {
                        cts.Cancel();
                        break;
                    }
                    var items = slice.Select(p => new TranslationItem {
                        Msgid = p.entry.Msgid,
                        Msgctxt = p.entry.Msgctxt,
                        Comments = p.entry.TranslatorComments,
                    }).ToList();

                    Dictionary<string, string> map = null;
                    int retry = 0;
                    Exception lastEx = null;
                    while (retry < 3) {
                        try {
                            map = client.TranslateBatch(
                                items, locale,
                                provider.endpoint, provider.model, auth.apiKey,
                                provider.systemPrompt,
                                cts.Token).GetAwaiter().GetResult();
                            break;
                        } catch (Exception e) {
                            lastEx = e;
                            retry++;
                            System.Threading.Thread.Sleep(300 * retry);
                        }
                    }
                    if (map == null) {
                        Debug.LogWarning($"[PromptUGUI] batch failed after retries: {lastEx?.Message}");
                        continue;
                    }
                    // Write back.
                    foreach (var (poPath, entry) in slice) {
                        if (!map.TryGetValue(entry.Msgid, out var translated)) continue;
                        WriteMsgstr(poPath, entry, translated);
                        done++;
                    }
                }
            } finally {
                EditorUtility.ClearProgressBar();
                AssetDatabase.Refresh();
            }
            Debug.Log($"[PromptUGUI] Translate Locale '{locale}': filled {done} / {queue.Count} entries.");
        }

        static void WriteMsgstr(string poPath, PoEntry target, string newMsgstr) {
            var entries = PoParser.Parse(File.ReadAllText(poPath)).ToList();
            var idx = entries.FindIndex(e => e.Msgctxt == target.Msgctxt && e.Msgid == target.Msgid);
            if (idx < 0) return;
            entries[idx].Msgstr = newMsgstr;
            File.WriteAllText(poPath, PoParser.Serialize(entries));
        }
    }
}
