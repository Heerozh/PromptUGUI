using System.Collections.Generic;
using PromptUGUI.Application;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PromptUGUI.Editor.Preview
{
    /// <summary>
    /// <c>Tools › PromptUGUI › UI Preview</c> (F8): plays the project in a preview scene and overlays
    /// a browser of every <c>.ui.xml</c> it owns — click one to see it, save it to see the change,
    /// no build, no Addressables packing, no scene of your own required (2026-09-18 ui-preview-tool
    /// spec). This class is the session: the menu, the Play-mode takeover and its restoration, and
    /// the public statics an automation client (Unity MCP) drives instead of reflecting into the
    /// overlay.
    ///
    /// <para><b>How Play is taken over.</b> <c>EditorSceneManager.playModeStartScene</c> is pointed
    /// at the preview scene inside <c>ExitingEditMode</c> — not at F8 time — because a host may set
    /// it there too (ssw's <c>DevPlayFromCurrentScene</c> pins Login). A multicast delegate runs its
    /// subscribers in subscription order, and this hook is subscribed at F8 time, after any hook a
    /// host subscribed at <c>[InitializeOnLoad]</c>, so the last write is ours. The editing scene set
    /// is untouched and comes back when Play stops; the previous start scene is restored from
    /// <see cref="SessionState"/>, which survives the domain reload on the way in.</para>
    ///
    /// <para><b>How the overlay gets in.</b> Injected at runtime on <c>sceneLoaded</c> /
    /// <c>EnteredPlayMode</c> (whichever comes first, idempotent) as a <c>DontDestroyOnLoad</c>
    /// GameObject carrying a <see cref="UIPreviewHost"/> — never serialized into anybody's scene
    /// asset. The host is a Runtime-assembly shell because Unity will not attach an Editor-assembly
    /// MonoBehaviour; the overlay logic (<see cref="UIPreviewOverlay"/>) stays here.</para>
    /// </summary>
    [InitializeOnLoad]
    public static class UIPreview
    {
        private const string MenuPath = "Tools/PromptUGUI/UI Preview _F8";

        internal const string BuiltInScenePath = "Packages/com.promptugui.core/Editor/Preview/UIPreview.unity";

        // Session keys — SessionState survives the domain reload Unity does when entering Play.
        private const string PendingKey = "PromptUGUI.Preview.Pending";        // F8 pressed, Play not entered yet
        private const string ActiveKey = "PromptUGUI.Preview.Active";          // this Play session is a preview
        private const string SceneKey = "PromptUGUI.Preview.Scene";            // asset path picked at launch
        private const string BuiltInKey = "PromptUGUI.Preview.BuiltIn";        // the picked scene is the package one
        private const string PrevStartSceneKey = "PromptUGUI.Preview.PrevStartScene"; // GUID; "" = none was set

        // How many editor ticks after EnterPlaymode() to wait before deciding it did not happen.
        private const int LaunchGraceTicks = 30;
        private static int _launchTicks;

        static UIPreview()
        {
            var pending = SessionState.GetBool(PendingKey, false);
            var active = SessionState.GetBool(ActiveKey, false);
            if (!pending && !active) return;

            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                // Domain reload inside the preview session (on the way into Play, or a script
                // change while previewing): re-arm the hooks the reload dropped.
                Subscribe();
                if (UnityEngine.Application.isPlaying) EditorApplication.delayCall += TryInject;
            }
            else
            {
                // Edit mode with flags left over — the reload on the way OUT of Play, or a session
                // that never reached Play. Either way: put the start scene back and forget it.
                ClearSession(restoreStartScene: true);
            }
        }

        // ── menu ───────────────────────────────────────────────────────────────────────────────

        [MenuItem(MenuPath)]
        private static void Menu()
        {
            switch (UIPreviewRules.Decide(EditorApplication.isPlayingOrWillChangePlaymode, IsActive,
                        EditorUtility.scriptCompilationFailed, out var reason))
            {
                case UIPreviewRules.MenuAction.ToggleCollapse:
                    PanelCollapsed = !PanelCollapsed;
                    break;
                case UIPreviewRules.MenuAction.Refuse:
                    Debug.LogWarning(reason);
                    break;
                default:
                    Launch();
                    break;
            }
        }

        [MenuItem(MenuPath, true)]
        private static bool MenuValidate() => !EditorApplication.isPlayingOrWillChangePlaymode || IsActive;

        // ── public statics (spec §4.13) ────────────────────────────────────────────────────────

        /// <summary>Play mode is running and it is a preview session with the overlay alive.</summary>
        public static bool IsActive =>
            UnityEngine.Application.isPlaying && SessionState.GetBool(ActiveKey, false) && Overlay != null && Overlay.Host != null;

        /// <summary>
        /// Enters Play in the preview scene (the built-in one, or the scene configured under
        /// Project Settings › PromptUGUI › UI Preview). Never shows a dialog. False, with a
        /// warning in the Console, while any Play session is running or compile errors are outstanding.
        /// </summary>
        public static bool Launch()
        {
            var action = UIPreviewRules.Decide(EditorApplication.isPlayingOrWillChangePlaymode, IsActive,
                EditorUtility.scriptCompilationFailed, out var reason);
            if (action != UIPreviewRules.MenuAction.Launch)
            {
                Debug.LogWarning(reason ?? "UI Preview: already previewing.");
                return false;
            }

            var pick = UIPreviewRules.PickScene(UIPreviewSettings.instance.sceneGuid,
                AssetDatabase.GUIDToAssetPath,
                path => AssetDatabase.LoadAssetAtPath<SceneAsset>(path) != null,
                BuiltInScenePath);
            if (pick.Warning != null) Debug.LogWarning(pick.Warning);
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(pick.Path) == null)
            {
                Debug.LogError($"[PromptUGUI] UI Preview: preview scene '{pick.Path}' is missing — is the package intact?");
                return false;
            }

            SessionState.SetString(SceneKey, pick.Path);
            SessionState.SetBool(BuiltInKey, pick.IsBuiltIn);
            SessionState.SetBool(PendingKey, true);
            Subscribe();   // now — later than any [InitializeOnLoad] subscriber, so our ExitingEditMode write wins

            _launchTicks = 0;
            EditorApplication.update -= WatchLaunch;
            EditorApplication.update += WatchLaunch;
            EditorApplication.EnterPlaymode();
            return true;
        }

        /// <summary>
        /// Loads and opens a file: an asset path (<c>Assets/UI/Planet.ui.xml</c>), an absolute path,
        /// or just enough of one to be unique (<c>Planet.ui.xml</c>). Fire-and-forget — poll
        /// <see cref="IsBusy"/>, then read <see cref="LastError"/>. No-op outside a preview session.
        /// </summary>
        public static void Load(string file)
        {
            if (!IsActive)
            {
                Debug.LogWarning("[PromptUGUI] UI Preview: not previewing — call Launch() first.");
                return;
            }
            var assetPath = Overlay.ResolveFileArgument(file);
            if (assetPath == null)
            {
                Debug.LogWarning($"[PromptUGUI] UI Preview: no project .ui.xml matches '{file}'.");
                return;
            }
            _ = Overlay.LoadAsync(assetPath);
        }

        /// <summary>A load is in flight.</summary>
        public static bool IsBusy => Overlay != null && Overlay.Busy;

        /// <summary>Why the last load failed; null when it succeeded.</summary>
        public static string LastError => Overlay?.Error;

        /// <summary>Asset path of the loaded file; null when nothing is loaded.</summary>
        public static string LoadedFile => Overlay?.LoadedFile;

        /// <summary>Name of the open Screen; null when nothing is loaded.</summary>
        public static string LoadedScreen => Overlay?.LoadedScreen;

        /// <summary>The Screens the loaded file declares.</summary>
        public static IReadOnlyList<string> Screens => Overlay != null ? Overlay.Screens : System.Array.Empty<string>();

        /// <summary>Switches to another Screen of the loaded file.</summary>
        public static void OpenScreen(string name) => Overlay?.OpenScreen(name);

        /// <summary>Shows a page of a <c>&lt;Pages&gt;</c> in the open Screen and remembers it; false when either id is unknown.</summary>
        public static bool Select(string pagesId, string pageId) => Overlay != null && Overlay.Select(pagesId, pageId);

        /// <summary>
        /// Resizes the Game view to the configured landscape / portrait size (Project Settings ›
        /// PromptUGUI › UI Preview); the <c>portrait</c> / <c>landscape</c> variants follow.
        /// </summary>
        public static void SetOrientation(bool portrait) => UIPreviewOverlay.SetOrientation(portrait);

        /// <summary>
        /// Runs the lint menu's rules over the loaded file (findings go to the Console, each line
        /// pinging the asset). The issue count; -1 when nothing is loaded or not previewing.
        /// </summary>
        public static int Lint() => Overlay != null ? Overlay.Lint() : -1;

        /// <summary>
        /// The scene the preview plays in, as an asset path — what Project Settings › PromptUGUI ›
        /// UI Preview shows; null = the package's built-in scene. Setting it (an asset path, or
        /// null / "" for built-in) stores the scene's GUID in <c>ProjectSettings/PromptUGUIPreview.asset</c>
        /// and throws when the path is not a scene. For automation that configures a project.
        /// </summary>
        public static string PreviewScenePath
        {
            get
            {
                var guid = UIPreviewSettings.instance.sceneGuid;
                if (string.IsNullOrEmpty(guid)) return null;
                var path = AssetDatabase.GUIDToAssetPath(guid);
                return string.IsNullOrEmpty(path) ? null : path;
            }
            set
            {
                var guid = "";
                if (!string.IsNullOrEmpty(value))
                {
                    if (AssetDatabase.LoadAssetAtPath<SceneAsset>(value) == null)
                        throw new System.ArgumentException($"'{value}' is not a scene asset", nameof(value));
                    guid = AssetDatabase.AssetPathToGUID(value);
                }
                UIPreviewSettings.instance.sceneGuid = guid;
                UIPreviewSettings.instance.SaveNow();
            }
        }

        /// <summary>The overlay's panel, collapsed to a single button or expanded. No-op outside a preview session.</summary>
        public static bool PanelCollapsed
        {
            get => Overlay != null && Overlay.Collapsed;
            set { if (Overlay != null) Overlay.Collapsed = value; }
        }

        internal static UIPreviewOverlay Overlay { get; private set; }

        internal static bool UsesBuiltInScene => SessionState.GetBool(BuiltInKey, false);

        // ── session ────────────────────────────────────────────────────────────────────────────

        private static void Subscribe()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private static void Unsubscribe()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            EditorApplication.update -= WatchLaunch;
        }

        // EnterPlaymode() that never happens (a modal in the way, a refused request) must not leave
        // Pending set: the next ordinary Play would land in the preview scene.
        private static void WatchLaunch()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || !SessionState.GetBool(PendingKey, false))
            {
                EditorApplication.update -= WatchLaunch;
                return;
            }
            if (++_launchTicks < LaunchGraceTicks) return;
            EditorApplication.update -= WatchLaunch;
            Debug.LogWarning("[PromptUGUI] UI Preview: Play mode did not start; launch cancelled.");
            ClearSession(restoreStartScene: false);
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            switch (state)
            {
                case PlayModeStateChange.ExitingEditMode:
                    if (!SessionState.GetBool(PendingKey, false)) return;
                    TakeOverStartScene();
                    break;
                case PlayModeStateChange.EnteredPlayMode:
                    TryInject();
                    break;
                case PlayModeStateChange.EnteredEditMode:
                    if (SessionState.GetBool(ActiveKey, false) || SessionState.GetBool(PendingKey, false))
                        ClearSession(restoreStartScene: true);
                    break;
            }
        }

        private static void TakeOverStartScene()
        {
            var scene = AssetDatabase.LoadAssetAtPath<SceneAsset>(SessionState.GetString(SceneKey, BuiltInScenePath));
            if (scene == null)
            {
                Debug.LogError("[PromptUGUI] UI Preview: preview scene vanished between F8 and Play; launching normally.");
                ClearSession(restoreStartScene: false);
                return;
            }

            var previous = EditorSceneManager.playModeStartScene;
            SessionState.SetString(PrevStartSceneKey,
                previous == null ? "" : AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(previous)));
            EditorSceneManager.playModeStartScene = scene;
            SessionState.SetBool(PendingKey, false);
            SessionState.SetBool(ActiveKey, true);
        }

        private static void ClearSession(bool restoreStartScene)
        {
            if (restoreStartScene && SessionState.GetBool(ActiveKey, false))
            {
                var guid = SessionState.GetString(PrevStartSceneKey, "");
                EditorSceneManager.playModeStartScene = string.IsNullOrEmpty(guid)
                    ? null
                    : AssetDatabase.LoadAssetAtPath<SceneAsset>(AssetDatabase.GUIDToAssetPath(guid));
            }
            SessionState.EraseBool(PendingKey);
            SessionState.EraseBool(ActiveKey);
            SessionState.EraseString(SceneKey);
            SessionState.EraseBool(BuiltInKey);
            SessionState.EraseString(PrevStartSceneKey);
            Unsubscribe();
        }

        // ── injection ──────────────────────────────────────────────────────────────────────────

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => TryInject();

        private static void TryInject()
        {
            if (!UnityEngine.Application.isPlaying || !SessionState.GetBool(ActiveKey, false)) return;
            if (Overlay != null && Overlay.Host != null) return;

            // A host left by an earlier domain (a script change while previewing) is re-attached,
            // not duplicated; its old overlay object died with that domain.
            var host = Object.FindAnyObjectByType<UIPreviewHost>();
            if (host == null)
            {
                var go = new GameObject("PromptUGUI UI Preview");
                Object.DontDestroyOnLoad(go);
                host = go.AddComponent<UIPreviewHost>();
            }
            Overlay = new UIPreviewOverlay();
            Overlay.Attach(host);
        }

        internal static void OnOverlayDestroyed(UIPreviewOverlay overlay)
        {
            if (Overlay == overlay) Overlay = null;
        }
    }
}
