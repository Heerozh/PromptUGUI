using System.Collections.Generic;
using PromptUGUI.Application;
using PromptUGUI.IR;
using PromptUGUI.Parser;
using UnityEditor;
using UnityEngine;

namespace PromptUGUI.Editor.Preview
{
    [CustomEditor(typeof(PromptUGUIDocumentHost))]
    internal sealed class PromptUGUIDocumentHostEditor : UnityEditor.Editor
    {
        private SerializedProperty _xmlAsset;
        private SerializedProperty _screenName;
        private SerializedProperty _activeVariants;
        private SerializedProperty _locale;
        private SerializedProperty _orientation;

        // Cache: avoid re-parsing on every OnInspectorGUI tick. Invalidate when
        // the asset reference changes or its text differs from last parse.
        private string _cachedXml;
        private TextAsset _cachedAsset;
        private List<string> _screenNames = new();
        private List<string> _declaredVariants = new();
        private string _parseError;

        private void OnEnable()
        {
            _xmlAsset = serializedObject.FindProperty(nameof(PromptUGUIDocumentHost.xmlAsset));
            _screenName = serializedObject.FindProperty(nameof(PromptUGUIDocumentHost.screenName));
            _activeVariants = serializedObject.FindProperty(nameof(PromptUGUIDocumentHost.activeVariants));
            _locale = serializedObject.FindProperty(nameof(PromptUGUIDocumentHost.locale));
            _orientation = serializedObject.FindProperty(nameof(PromptUGUIDocumentHost.orientation));
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUI.BeginChangeCheck();

            EditorGUILayout.PropertyField(_xmlAsset);

            var asset = _xmlAsset.objectReferenceValue as TextAsset;
            EnsureParsed(asset);

            if (_parseError != null)
                EditorGUILayout.HelpBox(_parseError, MessageType.Error);

            DrawScreenNameDropdown();
            DrawVariantsList();
            DrawLocaleDropdown();
            EditorGUILayout.PropertyField(_orientation);

            var changed = EditorGUI.EndChangeCheck();
            serializedObject.ApplyModifiedProperties();

            EditorGUILayout.Space();
            if (GUILayout.Button("Refresh") || changed)
            {
                foreach (var t in targets)
                {
                    if (t is PromptUGUIDocumentHost host) host.Refresh();
                }
            }
        }

        private void EnsureParsed(TextAsset asset)
        {
            if (asset == null)
            {
                _cachedAsset = null;
                _cachedXml = null;
                _screenNames.Clear();
                _declaredVariants.Clear();
                _parseError = null;
                return;
            }
            var xml = asset.text;
            if (asset == _cachedAsset && xml == _cachedXml) return;

            _cachedAsset = asset;
            _cachedXml = xml;
            _screenNames = new List<string>();
            _declaredVariants = new List<string>();
            _parseError = null;

            try
            {
                var doc = UIDocumentParser.Parse(xml);
                foreach (var s in doc.Screens)
                {
                    _screenNames.Add(s.Name);
                    foreach (var v in s.Variants)
                        if (!string.IsNullOrEmpty(v.When) && !_declaredVariants.Contains(v.When))
                            _declaredVariants.Add(v.When);
                }
            }
            catch (System.Exception e) { _parseError = e.Message; }
        }

        private void DrawScreenNameDropdown()
        {
            if (_screenNames.Count == 0)
            {
                EditorGUILayout.PropertyField(_screenName);
                return;
            }
            var current = _screenName.stringValue;
            var idx = _screenNames.IndexOf(current);
            // Index 0 = "(first declared)" — empty stringValue picks the first screen at runtime.
            var labels = new string[_screenNames.Count + 1];
            labels[0] = "(first declared)";
            for (var i = 0; i < _screenNames.Count; i++) labels[i + 1] = _screenNames[i];
            var selected = string.IsNullOrEmpty(current) ? 0 : idx + 1;
            if (selected < 0) selected = 0;
            var next = EditorGUILayout.Popup("Screen", selected, labels);
            _screenName.stringValue = next == 0 ? "" : _screenNames[next - 1];
        }

        private void DrawVariantsList()
        {
            EditorGUILayout.LabelField("Active Variants", EditorStyles.boldLabel);
            // Render declared variants as toggles for one-click activation; free-text
            // additions stay in the underlying list and round-trip through the array
            // property so authors can experiment with not-yet-declared variants.
            var current = new HashSet<string>();
            for (var i = 0; i < _activeVariants.arraySize; i++)
                current.Add(_activeVariants.GetArrayElementAtIndex(i).stringValue);

            foreach (var v in _declaredVariants)
            {
                var was = current.Contains(v);
                var now = EditorGUILayout.ToggleLeft(v, was);
                if (now == was) continue;
                if (now) current.Add(v); else current.Remove(v);
            }

            // Surface any extra variants the author manually added (not declared via
            // <Variant when="X">) so they don't silently vanish from the inspector.
            foreach (var v in current)
                if (!_declaredVariants.Contains(v) && !string.IsNullOrEmpty(v))
                    EditorGUILayout.LabelField("  (extra) " + v);

            // Sync HashSet → SerializedProperty array
            _activeVariants.ClearArray();
            var i2 = 0;
            foreach (var v in current)
            {
                _activeVariants.InsertArrayElementAtIndex(i2);
                _activeVariants.GetArrayElementAtIndex(i2).stringValue = v;
                i2++;
            }
        }

        private void DrawLocaleDropdown()
        {
            var settings = PromptUGUISettings.Instance;
            if (settings == null || settings.locales == null || settings.locales.Count == 0)
            {
                EditorGUILayout.PropertyField(_locale);
                return;
            }
            var labels = new List<string> { "(unchanged)" };
            foreach (var lc in settings.locales)
                if (!string.IsNullOrEmpty(lc.locale)) labels.Add(lc.locale);

            var current = _locale.stringValue;
            var idx = string.IsNullOrEmpty(current) ? 0 : labels.IndexOf(current);
            if (idx < 0) idx = 0;
            var next = EditorGUILayout.Popup("Locale", idx, labels.ToArray());
            _locale.stringValue = next == 0 ? "" : labels[next];
        }
    }
}
