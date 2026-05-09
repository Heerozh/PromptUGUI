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
        const string I18nRoot = "Assets/Resources/PromptUGUI/i18n";
        const int BatchSize = 50;

        string[] _locales = Array.Empty<string>();
        int _selected;
        Vector2 _outputScroll;

        readonly object _lock = new();
        bool _running;
        int _batchTotal;
        int _batchDone;
        int _entriesTotal;
        int _entriesProcessed;
        int _entriesFilled;
        string _lastResponse = "";
        string _statusLine = "Idle.";
        bool _needsAssetRefresh;

        CancellationTokenSource _cts;
        Task _runTask;

        public static void Open(string[] locales)
        {
            var w = GetWindow<TranslateLocaleWindow>("Translate Locale");
            w._locales = locales ?? Array.Empty<string>();
            if (w._selected >= w._locales.Length) w._selected = 0;
            w.minSize = new Vector2(460, 360);
            w.Show();
        }

        void OnEnable() => EditorApplication.update += OnEditorUpdate;

        void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
            try { _cts?.Cancel(); } catch { /* cts may already be disposed */ }
        }

        void OnEditorUpdate()
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

        void OnGUI()
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
            float prog = batchTotal > 0 ? (float)batchDone / batchTotal : 0f;
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

        void StartTranslate()
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
            var localeDir = Path.Combine(I18nRoot, locale);
            var poFiles = Directory.Exists(localeDir)
                ? Directory.GetFiles(localeDir, "*.po", SearchOption.AllDirectories)
                : Array.Empty<string>();
            if (poFiles.Length == 0)
            {
                EditorUtility.DisplayDialog(
                    "PromptUGUI",
                    $"No .po files for locale '{locale}' under '{localeDir}'. Run Extract Strings first.",
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

        async Task RunAsync(
            List<(string poPath, PoEntry entry)> queue,
            string locale,
            string endpoint, string model, string apiKey, string systemPrompt,
            CancellationToken ct)
        {
            var client = new TranslationClient();
            try
            {
                for (int i = 0; i < queue.Count; i += BatchSize)
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
                    for (int retry = 0; retry < 3; retry++)
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

                    int filled = 0;
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

        static List<(string poPath, PoEntry entry)> CollectQueue(string[] poFiles)
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

        static void WriteMsgstr(string poPath, PoEntry target, string newMsgstr)
        {
            var entries = PoParser.Parse(File.ReadAllText(poPath)).ToList();
            var idx = entries.FindIndex(e => e.Msgctxt == target.Msgctxt && e.Msgid == target.Msgid);
            if (idx < 0) return;
            entries[idx].Msgstr = newMsgstr;
            File.WriteAllText(poPath, PoParser.Serialize(entries));
        }
    }
}
