using System.Collections.Generic;
using UnityEngine;

namespace PromptUGUI.Application
{
    /// <summary>
    /// Editor-only preview host: drop a <c>.ui.xml</c> TextAsset on this
    /// component and the Screen renders as a live child hierarchy. Build
    /// payload is a no-op shell — see PromptUGUI.Editor's IProcessScene
    /// hook for stripping host nodes from shipped scenes.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("PromptUGUI/PromptUGUI Document Host")]
    public sealed class PromptUGUIDocumentHost : MonoBehaviour
    {
        public enum OrientationOverride { Auto, Portrait, Landscape }

#if UNITY_EDITOR
        public TextAsset xmlAsset;
        public string screenName;
        public List<string> activeVariants = new();
        public string locale;
        public OrientationOverride orientation = OrientationOverride.Auto;

        [System.NonSerialized] private readonly List<string> _loadedScreenNames = new();
        [System.NonSerialized] private GameObject _spawnedRoot;

        public void Refresh()
        {
            Clear();
            if (xmlAsset == null) return;
            var xml = xmlAsset.text;
            if (string.IsNullOrEmpty(xml)) return;

            var label = string.IsNullOrEmpty(xmlAsset.name) ? "preview" : xmlAsset.name;

            PromptUGUI.IR.UIDocument doc;
            try
            {
                doc = PromptUGUI.Parser.UIDocumentParser.Parse(xml);
            }
            catch (System.Exception e)
            {
                Debug.LogError(
                    $"[PromptUGUI Preview] parse failed for '{label}': {e.Message}", this);
                return;
            }

            try
            {
                UI.LoadDocument(label, xml);
                foreach (var s in doc.Screens) _loadedScreenNames.Add(s.Name);
            }
            catch (System.Exception e)
            {
                Debug.LogError(
                    $"[PromptUGUI Preview] load failed for '{label}': {e.Message}", this);
                return;
            }

            var target = string.IsNullOrEmpty(screenName)
                ? (doc.Screens.Count > 0 ? doc.Screens[0].Name : null)
                : screenName;
            if (string.IsNullOrEmpty(target))
            {
                Debug.LogError(
                    $"[PromptUGUI Preview] no screen found in '{label}'", this);
                return;
            }

            ApplyGlobalStateBeforeOpen(doc, target);

            Screen screen;
            try
            {
                screen = UI.Open(target);
            }
            catch (System.Exception e)
            {
                Debug.LogError(
                    $"[PromptUGUI Preview] Open '{target}' failed: {e.Message}", this);
                return;
            }

            _spawnedRoot = screen.RootGameObject;
            _spawnedRoot.transform.SetParent(transform, worldPositionStays: false);
            _spawnedRoot.hideFlags =
                HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild | HideFlags.NotEditable;
        }

        private void ApplyGlobalStateBeforeOpen(PromptUGUI.IR.UIDocument doc, string targetScreen)
        {
            // Variants are global state — clear every <Variant when="X"> declared
            // in this doc, then re-enable the user-checked ones. Other globals
            // (portrait/landscape, locale tags) are left alone unless explicitly
            // overridden below.
            foreach (var s in doc.Screens)
            {
                if (s.Name != targetScreen) continue;
                foreach (var vb in s.Variants)
                {
                    if (!string.IsNullOrEmpty(vb.When))
                        UI.Variants.Set(vb.When, false);
                }
            }
            if (activeVariants != null)
            {
                foreach (var v in activeVariants)
                    if (!string.IsNullOrEmpty(v)) UI.Variants.Set(v, true);
            }

            switch (orientation)
            {
                case OrientationOverride.Portrait:
                    UI.Orientation.AutoTrack = false;
                    UI.Orientation.Set(true);
                    break;
                case OrientationOverride.Landscape:
                    UI.Orientation.AutoTrack = false;
                    UI.Orientation.Set(false);
                    break;
                default:
                    UI.Orientation.AutoTrack = true;
                    break;
            }

            if (!string.IsNullOrEmpty(locale) && UI.Locale.Current != locale)
                UI.Locale.Set(locale);
        }

        public void Clear()
        {
            // UI.Close destroys the RootGameObject; UnloadDocument frees the
            // ScreenDef slot so another host (or a re-Refresh) can claim it.
            foreach (var n in _loadedScreenNames)
            {
                UI.Close(n);
                UI.UnloadDocument(n);
            }
            _loadedScreenNames.Clear();
            _spawnedRoot = null;
        }

        private void OnEnable() => Refresh();
        private void OnDisable() => Clear();
        private void OnDestroy() => Clear();
#endif
    }
}
