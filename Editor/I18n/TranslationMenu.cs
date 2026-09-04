// Editor/I18n/TranslationMenu.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PromptUGUI.Application;
using PromptUGUI.I18n;
using UnityEditor;
using UnityEngine;

namespace PromptUGUI.Editor.I18n
{
    internal static class TranslationMenu
    {
        [MenuItem("Tools/PromptUGUI/I18n/2. AI Translate Locale...")]
        public static void OpenDialog()
        {
            var settings = PromptUGUISettings.Instance;
            if (settings == null || settings.locales.Count == 0)
            {
                EditorUtility.DisplayDialog(
                    "PromptUGUI",
                    "No PromptUGUISettings found, or it has no locales configured.\n\n" +
                    "Create one via 'Assets → Create → PromptUGUI/Settings', " +
                    "then select the asset and add at least one entry under 'Locales' in the Inspector.",
                    "OK");
                return;
            }
            var locales = settings.locales
                .Select(l => l.locale)
                .Where(s => !string.IsNullOrEmpty(s))
                .ToArray();
            TranslateLocaleWindow.Open(locales);
        }
    }

    internal sealed class TranslateLocaleWindow : EditorWindow
    {
        private const int BatchSize = 50;

        private string[] _locales = Array.Empty<string>();
        private int _selected;
        private Vector2 _outputScroll;

        private readonly object _lock = new();
        private bool _running;
        private int _batchTotal;
        private int _batchDone;
        private int _entriesTotal;
        private int _entriesProcessed;
        private int _entriesFilled;
        private string _lastResponse = "";
        private string _statusLine = "Idle.";
        private bool _needsAssetRefresh;

        private CancellationTokenSource _cts;
        private Task _runTask;

        public static void Open(string[] locales)
        {
            var w = GetWindow<TranslateLocaleWindow>("Translate Locale");
            w._locales = locales ?? Array.Empty<string>();
            if (w._selected >= w._locales.Length) w._selected = 0;
            w.minSize = new Vector2(460, 360);
            w.Show();
        }

        private void OnEnable() => EditorApplication.update += OnEditorUpdate;

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
            try { _cts?.Cancel(); } catch { /* cts may already be disposed */ }
        }

        private void OnEditorUpdate()
        {
            bool needRefresh;
            bool running;
            lock (_lock)
            {
                running = _running;
                needRefresh = _needsAssetRefresh;
                if (needRefresh) _needsAssetRefresh = false;
            }
            if (needRefresh) AssetDatabase.Refresh();
            if (running || needRefresh) Repaint();
        }

        private void OnGUI()
        {
            if (_locales.Length == 0)
            {
                EditorGUILayout.HelpBox(
                    "No locales available. Configure them in PromptUGUISettings.",
                    MessageType.Info);
                return;
            }

            bool running;
            int batchDone, batchTotal, entriesProcessed, entriesTotal, entriesFilled;
            string lastResponse, statusLine;
            lock (_lock)
            {
                running = _running;
                batchDone = _batchDone;
                batchTotal = _batchTotal;
                entriesProcessed = _entriesProcessed;
                entriesTotal = _entriesTotal;
                entriesFilled = _entriesFilled;
                lastResponse = _lastResponse;
                statusLine = _statusLine;
            }

            using (new EditorGUI.DisabledScope(running))
            {
                EditorGUILayout.LabelField("Target locale:");
                _selected = EditorGUILayout.Popup(_selected, _locales);
            }
            EditorGUILayout.Space();

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(running))
                {
                    if (GUILayout.Button("Translate", GUILayout.Height(24)))
                        StartTranslate();
                }
                using (new EditorGUI.DisabledScope(!running))
                {
                    if (GUILayout.Button("Cancel", GUILayout.Height(24)))
                    {
                        try { _cts?.Cancel(); } catch { }
                    }
                }
            }

            EditorGUILayout.Space();

            var rect = GUILayoutUtility.GetRect(0, 18, GUILayout.ExpandWidth(true));
            var prog = batchTotal > 0 ? (float)batchDone / batchTotal : 0f;
            EditorGUI.ProgressBar(rect, prog, statusLine);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Last API response:");
            _outputScroll = EditorGUILayout.BeginScrollView(
                _outputScroll, GUILayout.ExpandHeight(true));
            EditorGUILayout.TextArea(
                lastResponse ?? "",
                EditorStyles.textArea,
                GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
        }

        private void StartTranslate()
        {
            var locale = _locales[_selected];
            var auth = TranslationProviderSettingsProvider.GetOrCreateAuth();
            var provider = TranslationProviderSettingsProvider.GetOrCreateProvider();
            if (string.IsNullOrEmpty(auth.apiKey))
            {
                EditorUtility.DisplayDialog(
                    "PromptUGUI",
                    "No API key set. Edit at Project Settings → PromptUGUI → Translation.",
                    "OK");
                return;
            }
            var searchDirs = ResolveSearchDirs(locale);
            var poFiles = CollectPoFiles(searchDirs);
            if (poFiles.Length == 0)
            {
                EditorUtility.DisplayDialog(
                    "PromptUGUI",
                    $"No .po files for locale '{locale}' under: " +
                    $"{string.Join(", ", searchDirs)}.\n\nRun Extract Strings first, or add the " +
                    "folder holding them to PromptUGUISettings → External Po Roots.",
                    "OK");
                return;
            }

            var queue = CollectQueue(poFiles);
            if (queue.Count == 0)
            {
                EditorUtility.DisplayDialog(
                    "PromptUGUI",
                    $"All msgstr entries in '{locale}' are already filled.",
                    "OK");
                return;
            }

            // Capture provider/auth fields on the main thread; no UnityEngine.Object access on the bg task.
            var endpoint = provider.endpoint;
            var model = provider.model;
            var apiKey = auth.apiKey;
            var systemPrompt = provider.systemPrompt;

            lock (_lock)
            {
                _running = true;
                _batchTotal = (queue.Count + BatchSize - 1) / BatchSize;
                _batchDone = 0;
                _entriesTotal = queue.Count;
                _entriesProcessed = 0;
                _entriesFilled = 0;
                _lastResponse = "";
                _statusLine = $"Batch 0 / {_batchTotal} — 0 / {queue.Count}";
            }

            _cts = new CancellationTokenSource();
            var ct = _cts.Token;
            _runTask = Task.Run(() => RunAsync(
                queue, locale, endpoint, model, apiKey, systemPrompt, ct));
        }

        private async Task RunAsync(
            List<(string poPath, PoEntry entry)> queue,
            string locale,
            string endpoint, string model, string apiKey, string systemPrompt,
            CancellationToken ct)
        {
            var client = new TranslationClient();
            try
            {
                for (var i = 0; i < queue.Count; i += BatchSize)
                {
                    if (ct.IsCancellationRequested) break;
                    var slice = queue.Skip(i).Take(BatchSize).ToList();
                    var batchIdx = i / BatchSize + 1;
                    var items = slice.Select(p => new TranslationItem
                    {
                        Msgid = p.entry.Msgid,
                        Msgctxt = p.entry.Msgctxt,
                        Comments = p.entry.TranslatorComments,
                    }).ToList();

                    BatchResult br = null;
                    Exception lastEx = null;
                    for (var retry = 0; retry < 3; retry++)
                    {
                        if (ct.IsCancellationRequested) break;
                        try
                        {
                            br = await client.TranslateBatch(
                                items, locale, endpoint, model, apiKey, systemPrompt, ct);
                            break;
                        }
                        catch (OperationCanceledException) { throw; }
                        catch (Exception ex)
                        {
                            lastEx = ex;
                            lock (_lock)
                            {
                                _lastResponse = ex.Message;
                                _statusLine = $"Batch {batchIdx} / {_batchTotal} retry {retry + 1}/3...";
                            }
                            await Task.Delay(300 * (retry + 1), ct);
                        }
                    }

                    if (br == null)
                    {
                        lock (_lock)
                        {
                            _lastResponse = lastEx?.Message ?? "(batch failed)";
                            _batchDone = batchIdx;
                            _entriesProcessed += slice.Count;
                            _statusLine = $"Batch {batchIdx} / {_batchTotal} FAILED — continuing ({_entriesProcessed}/{_entriesTotal})";
                        }
                        continue;
                    }

                    var filled = 0;
                    foreach (var (poPath, entry) in slice)
                    {
                        if (!br.Translations.TryGetValue((entry.Msgid, entry.Msgctxt), out var translated))
                            continue;
                        WriteMsgstr(poPath, entry, translated);
                        filled++;
                    }
                    lock (_lock)
                    {
                        _lastResponse = br.RawResponse;
                        _batchDone = batchIdx;
                        _entriesProcessed += slice.Count;
                        _entriesFilled += filled;
                        _statusLine = $"Batch {batchIdx} / {_batchTotal} — {_entriesProcessed} / {_entriesTotal} ({_entriesFilled} filled)";
                    }
                }
            }
            catch (OperationCanceledException)
            {
                lock (_lock)
                {
                    _statusLine = $"Cancelled at batch {_batchDone} / {_batchTotal} — {_entriesFilled} filled.";
                }
            }
            catch (Exception ex)
            {
                lock (_lock)
                {
                    _lastResponse = ex.ToString();
                    _statusLine = $"Run aborted: {ex.GetType().Name}";
                }
            }
            finally
            {
                lock (_lock)
                {
                    _running = false;
                    _needsAssetRefresh = true;
                    if (!_statusLine.StartsWith("Cancelled") && !_statusLine.StartsWith("Run aborted"))
                        _statusLine = $"Done — {_entriesFilled} / {_entriesTotal} filled.";
                }
            }
        }

        /// <summary>
        /// Where this window looks for <paramref name="locale"/>'s .po files: the same
        /// folder <c>StringExtractor</c> writes to (resolved from Addressables
        /// <c>Locale:</c> labels, external roots excluded — so the election can't be
        /// hijacked), plus every <c>externalPoRoots</c>/&lt;locale&gt; folder. Ordinal
        /// distinct, '/'-normalized.
        /// </summary>
        internal static IReadOnlyList<string> ResolveSearchDirs(string locale)
        {
            var settings = PromptUGUISettings.Instance;
            var externalRoots = settings != null ? settings.externalPoRoots : null;

            var labelledByLocale =
                StringExtractor.CollectAddressablePoPathsByLocale(externalRoots);
            labelledByLocale.TryGetValue(locale, out var labelled);
            var dirs = new List<string>
            {
                AddressablePoLabelSyncer.ResolveOutputDirForLocale(
                    locale,
                    labelled ?? (IEnumerable<string>)Array.Empty<string>(),
                    StringExtractor.DefaultOutputRoot,
                    out _),
            };
            if (externalRoots != null)
            {
                foreach (var root in externalRoots)
                {
                    if (string.IsNullOrWhiteSpace(root)) continue;
                    dirs.Add(root.Replace('\\', '/').TrimEnd('/') + "/" + locale);
                }
            }
            return dirs.Distinct(StringComparer.Ordinal).ToList();
        }

        private static string[] CollectPoFiles(IReadOnlyList<string> searchDirs)
        {
            // Distinct by full path: an external root nested inside the extraction
            // folder would otherwise queue the same entry twice.
            var found = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var dir in searchDirs)
            {
                if (!Directory.Exists(dir)) continue;
                foreach (var po in Directory.GetFiles(dir, "*.po", SearchOption.AllDirectories))
                {
                    if (seen.Add(Path.GetFullPath(po))) found.Add(po);
                }
            }
            return found.ToArray();
        }

        private static List<(string poPath, PoEntry entry)> CollectQueue(string[] poFiles)
        {
            var queue = new List<(string, PoEntry)>();
            foreach (var po in poFiles)
            {
                var text = File.ReadAllText(po);
                foreach (var e in PoParser.Parse(text))
                {
                    if (string.IsNullOrEmpty(e.Msgstr)) queue.Add((po, e));
                }
            }
            return queue;
        }

        private static void WriteMsgstr(string poPath, PoEntry target, string newMsgstr)
        {
            var entries = PoParser.Parse(File.ReadAllText(poPath)).ToList();
            var idx = entries.FindIndex(e => e.Msgctxt == target.Msgctxt && e.Msgid == target.Msgid);
            if (idx < 0) return;
            entries[idx].Msgstr = newMsgstr;
            File.WriteAllText(poPath, PoParser.Serialize(entries));
        }
    }
}
