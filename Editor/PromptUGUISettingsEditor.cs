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

            DrawLocales(serializedObject.FindProperty("locales"));

            serializedObject.ApplyModifiedProperties();
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
