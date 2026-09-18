using System;
using System.Collections.Generic;
using System.IO;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;

namespace PromptUGUI.Editor.Preview
{
    /// <summary>
    /// The in-Play half of the UI Preview tool (2026-09-18 ui-preview-tool spec §4.3–4.11): an
    /// IMGUI panel drawn through the <see cref="UIPreviewHost"/> that <see cref="UIPreview"/>
    /// injects into the preview scene. Lists every <c>.ui.xml</c> the project owns; a click
    /// <c>UnloadAll</c>s, loads the file from disk (through the disk-first resolver this class
    /// installs as <c>UI.SourceResolver</c>) and opens its Screen; each <c>&lt;Pages&gt;</c> gets a
    /// row of page buttons at the bottom; saving the file lets the library's hot reload reopen it,
    /// after which the page selection is put back. The header says what the host's boot did not
    /// provide (sprite resolver, theme, common libraries) rather than leaving white boxes to guess at.
    ///
    /// <para>A plain class, not a component — Unity will not attach an Editor-assembly
    /// MonoBehaviour to anything, so the Runtime-side host carries the messages.</para>
    /// </summary>
    internal sealed class UIPreviewOverlay : UIPreviewHost.IOverlay
    {
        private const float PanelWidth = 460f;
        private const float PanelMaxHeight = 720f;
        private const string RenderingSizeName = "PromptUGUI Preview";
        private static readonly Color Highlight = new Color(0.35f, 0.62f, 1f);
        private static readonly Color Red = new Color(1f, 0.5f, 0.45f);
        private static readonly Color Yellow = new Color(1f, 0.85f, 0.4f);
        private static readonly Color Grey = new Color(0.75f, 0.75f, 0.75f);

        internal UIPreviewHost Host { get; private set; }

        // ── loaded state (read by UIPreview's public statics) ──────────────────────────────────
        internal string LoadedFile { get; private set; }
        internal string LoadedScreen { get; private set; }
        internal string[] Screens { get; private set; } = Array.Empty<string>();
        internal bool Busy { get; private set; }
        internal string Error { get; private set; }

        private UiFile[] _files = Array.Empty<UiFile>();
        private int _previewableCount;
        private Vector2 _scroll;

        private readonly UIPreviewResolver _resolver;
        private readonly Func<string, Awaitable<string>> _ours;
        private readonly Func<string, string> _oursMap;
        private Func<string, Awaitable<string>> _hostResolver;
        private Func<string, string> _hostMap;
        private bool _canvasConfiguratorInstalled;

        private IReadOnlyList<Pages> _pages = Array.Empty<Pages>();
        private IScreen _pagesScreen;   // the Screen instance _pages came from; hot reload swaps it

        // ── diagnostics (spec §4.9) ────────────────────────────────────────────────────────────
        private readonly List<(string Text, Color Color)> _commonsRows = new List<(string, Color)>();
        private int _commonsDeclared;
        private string _ensureError;
        private bool _ready;   // the injection sequence (hooks → commons → Ensure) has run
        private string _lintResult;

        private GUIStyle _rowStyle;
        private GUIStyle _wrapStyle;
        private Texture2D _bgTex;

        internal UIPreviewOverlay()
        {
            _resolver = new UIPreviewResolver(UiXmlLocator.Physical, File.Exists, UiXmlLocator.Locate, UiXmlLocator.ToAssetPath);
            _ours = ResolveSrcAsync;
            _oursMap = assetPath => _resolver.AssetPathToSrc(assetPath, _hostMap);
        }

        private static UIPreviewUserState User => UIPreviewUserState.instance;

        internal bool Collapsed
        {
            get => User.collapsed;
            set
            {
                if (User.collapsed == value) return;
                User.collapsed = value;
                User.SaveNow();
            }
        }

        // ── lifecycle ──────────────────────────────────────────────────────────────────────────

        internal void Attach(UIPreviewHost host)
        {
            Host = host;
            host.Overlay = this;

            EnsureEventSystem();

            // The built-in scene's camera is ours: canvas="camera" Screens (glass needs them) fall
            // back to Overlay silently without a worldCamera, so wire it — but only when the host
            // did not, and only in our own scene (spec §4.2).
            if (UIPreview.UsesBuiltInScene && UI.CanvasConfigurator == null)
            {
                UI.CanvasConfigurator = (canvas, _) => canvas.worldCamera = Camera.main;
                _canvasConfiguratorInstalled = true;
            }

            RefreshFiles();
            _ = StartAsync();
        }

        /// <summary>
        /// The injection sequence (spec §4.11): hooks first, the declared common libraries
        /// pre-resolved (diagnostics + served table), then <c>EnsureCommonLibrariesAsync</c> — if
        /// the host's fire-and-forget boot load is still in flight this waits for it, so the
        /// auto-load below reads commons from disk instead of joining the host's fetch — and
        /// finally the last file, unless the user already clicked one.
        /// </summary>
        private async Awaitable StartAsync()
        {
            EnsureHooks();
            PreResolveCommons();
            try
            {
                await UI.EnsureCommonLibrariesAsync();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                _ensureError = e.GetBaseException().Message;
            }
            _ready = true;
            if (Host == null) return;   // Play stopped meanwhile

            var last = User.lastFile;
            if (User.autoLoadLast && !string.IsNullOrEmpty(last) && LoadedFile == null && !Busy && IsListed(last))
                _ = LoadAsync(last);
        }

        public void OnHostDestroyed()
        {
            if (_canvasConfiguratorInstalled) UI.CanvasConfigurator = null;
            // Give the host back what it had (matters with Domain Reload off: statics survive).
            if (UI.SourceResolver == _ours) UI.SourceResolver = _hostResolver;
            if (UI.HotReload.AssetPathToSrc == _oursMap) UI.HotReload.AssetPathToSrc = _hostMap;
            if (_bgTex != null) UnityEngine.Object.Destroy(_bgTex);
            Host = null;
            UIPreview.OnOverlayDestroyed(this);
        }

        /// <summary>
        /// The library's hot reload closes and reopens the Screen — new instance, new controls; the
        /// Pages handles in hand are dead and the strip would go blank. No reload event exists, so
        /// watch for the named Screen changing instance and re-collect (spec §4.8).
        /// </summary>
        public void OnUpdate()
        {
            if (string.IsNullOrEmpty(LoadedScreen) || Busy) return;
            var live = UI.Get(LoadedScreen);   // null between the reload's CloseImmediate and Open
            if (live == null || ReferenceEquals(live, _pagesScreen)) return;
            CollectPages(live);
        }

        // ── hooks: our resolver in front of the host's (spec §4.3–4.5) ─────────────────────────

        /// <summary>
        /// Installed lazily and re-checked before every load: a runner's <c>Start()</c> that calls
        /// <c>UseResourcesResolver</c> after we were attached would otherwise silently take the
        /// resolver back.
        /// </summary>
        private void EnsureHooks()
        {
            if (UI.SourceResolver != _ours)
            {
                _hostResolver = UI.SourceResolver;
                UI.SourceResolver = _ours;
            }
            if (UI.HotReload.AssetPathToSrc != _oursMap)
            {
                _hostMap = UI.HotReload.AssetPathToSrc;
                UI.HotReload.AssetPathToSrc = _oursMap;
            }
            _resolver.HasHost = _hostResolver != null;
        }

        private async Awaitable<string> ResolveSrcAsync(string src)
        {
            var r = _resolver.Resolve(src);
            switch (r.Source)
            {
                case UIPreviewResolver.Source.Disk:
                    _resolver.RecordServed(r.AssetPath, src);
                    return File.ReadAllText(r.Physical);
                case UIPreviewResolver.Source.Host:
                    return await _hostResolver(src);
                default:
                    throw new IOException(r.Message);
            }
        }

        // ── files ──────────────────────────────────────────────────────────────────────────────

        internal void RefreshFiles()
        {
            var list = new List<UiFile>();
            foreach (var assetPath in UiXmlLocator.FindProjectUiXml())
            {
                var physical = UiXmlLocator.Physical(assetPath);
                string text = null;
                try { text = File.ReadAllText(physical); }
                catch (Exception e) { Debug.LogWarning($"[PromptUGUI] UI Preview: cannot read {assetPath}: {e.Message}"); }
                list.Add(new UiFile(assetPath, physical, UIPreviewResolver.HasScreen(text)));
            }
            _files = list.ToArray();
            _previewableCount = 0;
            var index = new List<string>(_files.Length);
            foreach (var f in _files)
            {
                index.Add(f.AssetPath);
                if (f.HasScreen) _previewableCount++;
            }
            _resolver.Index = index;
        }

        private bool IsListed(string assetPath)
        {
            foreach (var f in _files)
                if (f.HasScreen && f.AssetPath == assetPath) return true;
            return false;
        }

        /// <summary>
        /// An asset path, an absolute path, or just enough of a path to be unique (<c>Planet.ui.xml</c>)
        /// — automation types the short form.
        /// </summary>
        internal string ResolveFileArgument(string file)
        {
            if (string.IsNullOrEmpty(file)) return null;
            file = file.Replace('\\', '/');
            if (Path.IsPathRooted(file)) return UiXmlLocator.ToAssetPath(file);
            if (File.Exists(UiXmlLocator.Physical(file))) return file;
            return _resolver.MatchSuffix(file);
        }

        // ── common libraries: what the settings declare, where each one is (spec §4.9) ─────────

        /// <summary>
        /// Every row of <c>PromptUGUISettings.commonLibraries</c> run through the resolver once:
        /// a diagnostics line each, and — for the ones found on disk — a served-table entry, so a
        /// save of that file hot-reloads even when the first load of it was the host's.
        /// </summary>
        private void PreResolveCommons()
        {
            _commonsRows.Clear();
            var rows = UIXmlLintMenu.ConfiguredCommonLibraries();
            _commonsDeclared = rows.Count;
            _resolver.Anchor = IsListed(User.lastFile) ? User.lastFile : null;
            foreach (var row in rows)
            {
                var r = _resolver.Resolve(row.Src);
                switch (r.Source)
                {
                    case UIPreviewResolver.Source.Disk:
                        _resolver.RecordServed(r.AssetPath, row.Src);
                        _commonsRows.Add(($"✓ {row.Src} → {r.AssetPath}", Grey));
                        break;
                    case UIPreviewResolver.Source.Host:
                        _commonsRows.Add(($"⚠ '{row.Src}' not found in the project — the host resolver will be asked (not the file on disk).", Yellow));
                        break;
                    default:
                        _commonsRows.Add(($"✗ '{row.Src}': {r.Message}", Red));
                        break;
                }
            }
        }

        // ── load (spec §4.7) ───────────────────────────────────────────────────────────────────

        internal async Awaitable LoadAsync(string assetPath)
        {
            if (Busy) return;
            Busy = true;
            Error = null;
            _lintResult = null;
            try
            {
                if (string.IsNullOrEmpty(assetPath) || !assetPath.EndsWith(".ui.xml", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"Only .ui.xml files can be previewed (got '{assetPath}')");
                if (!File.Exists(UiXmlLocator.Physical(assetPath)))
                    throw new FileNotFoundException(assetPath);

                EnsureHooks();
                _resolver.Anchor = assetPath;

                _pages = Array.Empty<Pages>();   // the old Screen dies now; the strip must not draw it
                _pagesScreen = null;
                UI.UnloadAll();                  // the host's Screens too — the preview scene runs no game logic
                LoadedFile = null;               // a failed load must not leave the previous name in the footer
                LoadedScreen = null;
                Screens = Array.Empty<string>();

                // Common libraries come back on their own: LoadDocumentAsync ensures the ones
                // declared in PromptUGUISettings, through our resolver — from disk (spec §4.6).
                var names = await UI.LoadDocumentAsync(assetPath);
                if (names.Count == 0)
                    throw new InvalidOperationException(
                        $"'{Path.GetFileName(assetPath)}' declares no <Screen> — a template-only file cannot be previewed on its own");

                var screens = new string[names.Count];
                for (var i = 0; i < names.Count; i++) screens[i] = names[i];
                Screens = screens;
                LoadedFile = assetPath;
                User.lastFile = assetPath;
                User.SaveNow();

                var remembered = User.GetScreen(assetPath);
                OpenScreen(Array.IndexOf(screens, remembered) >= 0 ? remembered : screens[0]);
            }
            catch (Exception e)
            {
                // Deliberately no MessageBox: it would sit behind this panel, and awaiting it would
                // keep the list locked (Busy). Red text in the footer; the full trace is in the Console.
                Debug.LogException(e);
                Error = e.GetBaseException().Message;
            }
            finally
            {
                Busy = false;
            }
        }

        internal void OpenScreen(string name)
        {
            if (string.IsNullOrEmpty(name) || Array.IndexOf(Screens, name) < 0) return;
            _pages = Array.Empty<Pages>();
            _pagesScreen = null;
            if (!string.IsNullOrEmpty(LoadedScreen)) UI.Close(LoadedScreen);
            var screen = UI.Open(name);
            LoadedScreen = name;
            User.SetScreen(LoadedFile, name);
            User.SaveNow();
            CollectPages(screen);
        }

        // ── Pages (spec §4.8) ──────────────────────────────────────────────────────────────────

        /// <summary>
        /// After opening (and after hot reload swapped the instance): collect the Screen's
        /// <c>&lt;Pages&gt;</c> and put back the page remembered for each (only ids that still exist).
        /// </summary>
        private void CollectPages(IScreen screen)
        {
            _pagesScreen = screen;
            _pages = screen.FindAll<Pages>();
            var index = 0;
            foreach (var pages in _pages)
            {
                var key = UIPreviewResolver.PagesKey(LoadedFile, pages.Id, index++);
                var remembered = User.GetPage(key);
                if (remembered == null) continue;
                foreach (var pageId in pages.PageIds)
                    if (pageId == remembered)
                    {
                        pages.Show(pageId);
                        break;
                    }
            }
        }

        internal bool Select(string pagesId, string pageId)
        {
            var index = 0;
            foreach (var pages in _pages)
            {
                var key = UIPreviewResolver.PagesKey(LoadedFile, pages.Id, index++);
                if (pages.GameObject == null || pages.Id != pagesId) continue;
                foreach (var id in pages.PageIds)
                {
                    if (id != pageId) continue;
                    ShowPage(pages, key, pageId);
                    return true;
                }
                return false;
            }
            return false;
        }

        private static void ShowPage(Pages pages, string key, string pageId)
        {
            pages.Show(pageId);
            if (key == null) return;
            User.SetPage(key, pageId);
            User.SaveNow();
        }

        // ── orientation, lint (spec §4.10, §4.9) ───────────────────────────────────────────────

        /// <summary>
        /// Resizes the Game view; the library's <c>portrait</c> / <c>landscape</c> variants follow
        /// on their own. Not restored on exit — an editor setting, like resizing it by hand.
        /// </summary>
        internal static void SetOrientation(bool portrait)
        {
            var size = portrait ? UIPreviewSettings.instance.portrait : UIPreviewSettings.instance.landscape;
            PlayModeWindow.SetCustomRenderingResolution((uint)Mathf.Max(1, size.x), (uint)Mathf.Max(1, size.y), RenderingSizeName);
        }

        /// <summary>The lint menu's run over the loaded file: same rules, same Console lines. -1 when nothing is loaded.</summary>
        internal int Lint()
        {
            if (string.IsNullOrEmpty(LoadedFile)) return -1;
            var run = UIXmlLintMenu.Lint(new[] { LoadedFile }, UIXmlLintMenu.Report, UIXmlLintMenu.ConfiguredCommonLibraries());
            _lintResult = run.Issues == 0 ? "Lint: no issues" : $"Lint: {run.Issues} issue(s) — see the Console";
            return run.Issues;
        }

        // ── drawing ────────────────────────────────────────────────────────────────────────────

        public void OnDraw()
        {
            var e = Event.current;
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.F1)
            {
                Collapsed = !Collapsed;
                e.Use();
            }

            // The page strip does not collapse with the panel: switching pages is for looking at
            // the result, which is when the panel is usually hidden.
            DrawPagesStrip();

            if (Collapsed)
            {
                if (GUI.Button(new Rect(10, 10, 140, 26), "UI Preview (F1)")) Collapsed = false;
                return;
            }

            EnsureStyles();

            var h = Mathf.Min(PanelMaxHeight, UnityEngine.Screen.height - 20f);
            var panel = new Rect(10, 10, PanelWidth, h);
            // The built-in box skin is translucent — unreadable over the previewed Screen.
            GUI.DrawTexture(panel, BackdropTex());
            GUI.Box(panel, GUIContent.none);

            GUILayout.BeginArea(new Rect(panel.x + 8, panel.y + 8, panel.width - 16, panel.height - 16));
            DrawHeader();
            DrawDiagnostics();
            DrawFileList();   // takes whatever height the header and footer leave
            DrawFooter();
            GUILayout.EndArea();
        }

        private void DrawHeader()
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Refresh", GUILayout.Width(64)))
            {
                AssetDatabase.Refresh();
                RefreshFiles();
                PreResolveCommons();
            }
            GUILayout.Space(8);
            var portraitNow = UnityEngine.Screen.height > UnityEngine.Screen.width;
            if (Toggle("Landscape", !portraitNow, 78)) SetOrientation(false);
            if (Toggle("Portrait", portraitNow, 66)) SetOrientation(true);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Hide (F1)", GUILayout.Width(80))) Collapsed = true;
            GUILayout.EndHorizontal();

            var hidden = _files.Length - _previewableCount;
            GUILayout.Label($"{_previewableCount} screen file(s)" + (hidden > 0 ? $", {hidden} template-only file(s) not listed" : ""));

            GUILayout.BeginHorizontal();
            GUILayout.Label("Filter", GUILayout.Width(36));
            var filter = GUILayout.TextField(User.filter ?? "");
            if (GUILayout.Button("x", GUILayout.Width(24)))
            {
                filter = "";
                GUI.FocusControl(null);
            }
            if (filter != User.filter)
            {
                User.filter = filter;
                User.SaveNow();
            }
            GUILayout.EndHorizontal();
        }

        /// <summary>
        /// What the host's boot did or did not provide (spec §4.9). Says, never installs — except
        /// the theme buttons, which are a preview choice, not a boot step.
        /// </summary>
        private void DrawDiagnostics()
        {
            if (!_ready)
            {
                Line("Loading common libraries…", Grey);
                return;
            }

            if (UIPreview.UsesBuiltInScene)
            {
                if (!string.IsNullOrEmpty(UIPreviewSettings.instance.sceneGuid))
                    Line("Preview scene not found (moved or deleted?) — using the built-in scene. Fix it under Project Settings › PromptUGUI › UI Preview.", Yellow);
                else
                    Line("Built-in preview scene — pick your own under Project Settings › PromptUGUI › UI Preview.", Grey);
            }

            var problems = 0;
            if (UI.SpriteResolver == null)
            {
                problems++;
                Line("Sprite resolver not set — <Icon> renders as white boxes. Call SpriteResolverHelpers.UseSpriteSetResolver(...) in a [RuntimeInitializeOnLoadMethod].", Red);
            }

            var themes = UI.Theme.Available;
            if (UI.Theme.Current == null)
            {
                problems++;
                Line(themes.Count > 0
                        ? "No theme selected — color tokens resolve to white. Pick one:"
                        : "No theme registered — no loaded library declares a <Theme>.", Yellow);
            }
            if (themes.Count > 0) DrawThemeRow(themes);

            foreach (var (text, color) in _commonsRows)
            {
                if (color != Grey) problems++;
                Line(text, color);
            }
            if (_commonsDeclared == 0 && themes.Count == 0)
            {
                problems++;
                Line("No common library declared — list your shared Template / Style / Theme files under PromptUGUI Settings → Common Libraries.", Yellow);
            }
            if (_ensureError != null)
            {
                problems++;
                Line("Common libraries failed to load: " + _ensureError, Red);
            }
            if (_hostResolver == null)
                Line("Host SourceResolver not set — only files the locator can find on disk resolve.", Grey);

            if (problems == 0)
                Line($"theme: {UI.Theme.Current} · sprites: ok · locale: {UI.Locale.Current ?? "(none)"} · commons: {_commonsDeclared}", Grey);
        }

        private void DrawThemeRow(IReadOnlyCollection<string> themes)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Theme", GUILayout.Width(44));
            foreach (var name in themes)
                if (Toggle(name, name == UI.Theme.Current, 0)) UI.Theme.Set(name);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        private void DrawFileList()
        {
            _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.ExpandHeight(true));
            var shown = 0;
            var filter = User.filter ?? "";
            foreach (var f in _files)
            {
                if (!f.HasScreen) continue;
                if (filter.Length > 0 && f.Display.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                shown++;

                var selected = f.AssetPath == LoadedFile;
                var bg = GUI.backgroundColor;
                if (selected) GUI.backgroundColor = Highlight;
                if (GUILayout.Button((selected ? "> " : "   ") + f.Display, _rowStyle) && !Busy)
                    _ = LoadAsync(f.AssetPath);
                GUI.backgroundColor = bg;
            }

            if (_previewableCount == 0)
                GUILayout.Label("No .ui.xml with a <Screen> found under Assets/.", _wrapStyle);
            else if (shown == 0)
                GUILayout.Label($"Nothing matches \"{filter}\".", _wrapStyle);
            GUILayout.EndScrollView();
        }

        private void DrawFooter()
        {
            if (Screens.Length > 1)
            {
                GUILayout.Label("This file declares several Screens:");
                GUILayout.BeginHorizontal();
                foreach (var name in Screens)
                    if (Toggle(name, name == LoadedScreen, 0)) OpenScreen(name);
                GUILayout.EndHorizontal();
            }

            if (!string.IsNullOrEmpty(LoadedFile))
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label($"Loaded: {Path.GetFileName(LoadedFile)}" +
                                (string.IsNullOrEmpty(LoadedScreen) ? "" : $"  /  {LoadedScreen}"));
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Lint", GUILayout.Width(48)) && !Busy) Lint();
                GUILayout.EndHorizontal();
                if (_lintResult != null) Line(_lintResult, Grey);
            }

            if (!string.IsNullOrEmpty(Error)) Line("Error: " + Error, Red);

            GUILayout.Label("Save the xml to hot-reload; click the same row to reload by hand.");
        }

        /// <summary>
        /// Bottom-left: one row per <c>&lt;Pages&gt;</c> in the open Screen — its id as the label, a
        /// button per page, the selected one highlighted. A click <c>Show</c>s the page and remembers
        /// it per (file, Pages), replayed the next time this file is loaded.
        /// </summary>
        private void DrawPagesStrip()
        {
            // During the frame or two of a hot reload the old handles are dead and the new instance
            // is not collected yet: draw nothing rather than an empty box.
            var alive = 0;
            foreach (var pages in _pages)
                if (pages.GameObject != null) alive++;
            if (alive == 0) return;
            EnsureStyles();

            const float rowH = 24f;
            var h = 8 + 18 + alive * rowH + 8;
            var w = Mathf.Min(PanelWidth + 200f, UnityEngine.Screen.width - 20f);
            var box = new Rect(10, UnityEngine.Screen.height - 10 - h, w, h);
            GUI.DrawTexture(box, BackdropTex());
            GUI.Box(box, GUIContent.none);

            GUILayout.BeginArea(new Rect(box.x + 8, box.y + 6, box.width - 16, box.height - 12));
            GUILayout.Label("Pages (views code switches between) — click a page to see only it; remembered per file");
            var index = 0;
            foreach (var pages in _pages)
            {
                var key = UIPreviewResolver.PagesKey(LoadedFile, pages.Id, index++);
                if (pages.GameObject == null) continue;   // closed / mid-reload; OnUpdate re-collects
                GUILayout.BeginHorizontal();
                GUILayout.Label(pages.Id ?? "(no id)", GUILayout.Width(120));
                foreach (var pageId in pages.PageIds)
                    if (Toggle(pageId, pageId == pages.Selected, 0, rowH - 2) && !Busy) ShowPage(pages, key, pageId);
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }
            GUILayout.EndArea();
        }

        /// <summary>A button drawn highlighted when selected; true when clicked while NOT selected.</summary>
        private static bool Toggle(string label, bool selected, float width, float height = 22)
        {
            var bg = GUI.backgroundColor;
            if (selected) GUI.backgroundColor = Highlight;
            var clicked = width > 0
                ? GUILayout.Button(label, GUILayout.Width(width), GUILayout.Height(height))
                : GUILayout.Button(label, GUILayout.Height(height));
            GUI.backgroundColor = bg;
            return clicked && !selected;
        }

        private void Line(string text, Color color)
        {
            var c = GUI.color;
            GUI.color = color;
            GUILayout.Label(text, _wrapStyle);
            GUI.color = c;
        }

        /// <summary>
        /// Loading a document triggers UnloadUnusedAssets, which drops the built-in skin textures;
        /// a cached GUIStyle keeps references to them and buttons turn into bare black text.
        /// Rebuild when the texture is gone.
        /// </summary>
        private void EnsureStyles()
        {
            if (_rowStyle != null && _rowStyle.normal.background != null) return;
            _rowStyle = new GUIStyle(GUI.skin.button)
            {
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(6, 6, 2, 2),
                fixedHeight = 22,
                clipping = TextClipping.Clip,
            };
            _wrapStyle = new GUIStyle(GUI.skin.label) { wordWrap = true };
        }

        private Texture2D BackdropTex()
        {
            if (_bgTex != null) return _bgTex;
            _bgTex = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            _bgTex.SetPixel(0, 0, new Color(0.07f, 0.08f, 0.11f, 0.97f));
            _bgTex.Apply();
            return _bgTex;
        }

        // Without one, runtime pointer events (Btn clicks, ScrollView drags) are silently dropped.
        // Same module choice as UI.Navigation.Enable: the project's active input backend decides.
        private static void EnsureEventSystem()
        {
            if (UnityEngine.Object.FindAnyObjectByType<EventSystem>() != null) return;
#if ENABLE_INPUT_SYSTEM
            new GameObject("EventSystem", typeof(EventSystem), typeof(UnityEngine.InputSystem.UI.InputSystemUIInputModule));
#else
            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
#endif
        }

        private readonly struct UiFile
        {
            public readonly string AssetPath;
            public readonly string Physical;
            public readonly string Display;   // list label: the asset path without its "Assets/" prefix

            // Template-only / theme files are not listed (clicking one could only fail) but stay in
            // the resolver's index so their on-disk edits reach the preview through imports.
            public readonly bool HasScreen;

            public UiFile(string assetPath, string physical, bool hasScreen)
            {
                AssetPath = assetPath;
                Physical = physical;
                HasScreen = hasScreen;
                Display = assetPath.StartsWith("Assets/", StringComparison.Ordinal)
                    ? assetPath.Substring("Assets/".Length)
                    : assetPath;
            }
        }
    }
}
