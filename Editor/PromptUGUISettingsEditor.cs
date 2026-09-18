using PromptUGUI.Application;
using UnityEditor;
using UnityEngine;

namespace PromptUGUI.Editor
{
    [CustomEditor(typeof(PromptUGUISettings))]
    public sealed class PromptUGUISettingsEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.PropertyField(serializedObject.FindProperty("fontTypes"), true);

            EditorGUILayout.Space();

            EditorGUILayout.PropertyField(
                serializedObject.FindProperty("externalPoRoots"), true);

            EditorGUILayout.Space();

            DrawCommonLibraries(serializedObject.FindProperty("commonLibraries"));

            EditorGUILayout.Space();

            DrawLocales(serializedObject.FindProperty("locales"));

            serializedObject.ApplyModifiedProperties();
        }

        /// <summary>
        /// The one declaration of the project's common libraries (2026-09-18 commons-settings spec
        /// §4.1). The default list drawer is right for two strings a row; what the section adds is
        /// the explanation of what a src IS — the field cannot validate it (a resolver key means
        /// whatever the host's SourceResolver says), so the lint menu is its validator.
        /// </summary>
        private static void DrawCommonLibraries(SerializedProperty commonLibraries)
        {
            EditorGUILayout.LabelField("Common Libraries", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Shared <Template> / <Style> / <Theme> files merged into every Screen — the <Import> " +
                "every document implicitly has. 'src' is a resolver key in the same shape as <Import src> " +
                "(e.g. 'UI/Templates/Theme.ui' for UseResourcesResolver(\"UI\"), the Address for " +
                "Addressables), not a file path. 'as' is an optional namespace: <ns.Name/>, class=\"ns:name\". " +
                "Loaded in this order, on demand, by UI.EnsureCommonLibrariesAsync — LoadDocumentAsync calls " +
                "it for you. Tools › PromptUGUI › Lint All UI XML checks that every src resolves.",
                MessageType.None);
            EditorGUILayout.PropertyField(commonLibraries, new GUIContent("Libraries"), true);
        }

        private void DrawLocales(SerializedProperty locales)
        {
            EditorGUILayout.LabelField("Locales", EditorStyles.boldLabel);

            var toRemove = -1;
            var copyFrom = -1;
            for (var i = 0; i < locales.arraySize; i++)
            {
                var lc = locales.GetArrayElementAtIndex(i);
                var localeProp = lc.FindPropertyRelative("locale");
                var fontsProp = lc.FindPropertyRelative("fonts");

                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.PropertyField(localeProp);
                        using (new EditorGUI.DisabledScope(locales.arraySize <= 1))
                        {
                            if (GUILayout.Button(new GUIContent("Copy to All",
                                    "Fill every other locale's empty font/material slots from this locale."),
                                    GUILayout.Width(80)))
                                copyFrom = i;
                        }
                        if (GUILayout.Button("Remove", GUILayout.Width(70)))
                            toRemove = i;
                    }

                    EditorGUILayout.LabelField("Fonts", EditorStyles.miniBoldLabel);
                    if (fontsProp.arraySize == 0)
                    {
                        EditorGUILayout.HelpBox(
                            "Add entries to 'Font Types' above first.",
                            MessageType.Info);
                    }
                    else
                    {
                        using (new EditorGUI.IndentLevelScope())
                        {
                            for (var j = 0; j < fontsProp.arraySize; j++)
                            {
                                var fe = fontsProp.GetArrayElementAtIndex(j);
                                var typeProp = fe.FindPropertyRelative("type");
                                var fontProp = fe.FindPropertyRelative("font");
                                var matProp = fe.FindPropertyRelative("material");
                                EditorGUILayout.PropertyField(fontProp, new GUIContent(typeProp.stringValue));
                                using (new EditorGUI.IndentLevelScope())
                                {
                                    EditorGUILayout.PropertyField(matProp, new GUIContent(
                                        "Material",
                                        "Optional TMP material preset (e.g. outline). " +
                                        "Empty = the font's default material."));
                                }
                            }
                        }
                    }
                }
            }

            if (copyFrom >= 0)
            {
                var settings = (PromptUGUISettings)target;
                serializedObject.ApplyModifiedProperties();
                Undo.RecordObject(settings, "Copy fonts to all locales");
                LocaleFontCopier.CopyToEmptySlots(settings.locales, settings.locales[copyFrom].locale);
                EditorUtility.SetDirty(settings);
                serializedObject.Update();
            }

            if (toRemove >= 0) locales.DeleteArrayElementAtIndex(toRemove);

            if (GUILayout.Button("+ Add Locale"))
            {
                locales.InsertArrayElementAtIndex(locales.arraySize);
                var newLc = locales.GetArrayElementAtIndex(locales.arraySize - 1);
                newLc.FindPropertyRelative("locale").stringValue = "";
            }
        }
    }
}
