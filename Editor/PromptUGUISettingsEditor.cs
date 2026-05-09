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

            DrawLocales(serializedObject.FindProperty("locales"));

            serializedObject.ApplyModifiedProperties();
        }

        static void DrawLocales(SerializedProperty locales)
        {
            EditorGUILayout.LabelField("Locales", EditorStyles.boldLabel);

            int toRemove = -1;
            for (int i = 0; i < locales.arraySize; i++)
            {
                var lc = locales.GetArrayElementAtIndex(i);
                var localeProp = lc.FindPropertyRelative("locale");
                var fontsProp = lc.FindPropertyRelative("fonts");

                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.PropertyField(localeProp);
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
                            for (int j = 0; j < fontsProp.arraySize; j++)
                            {
                                var fe = fontsProp.GetArrayElementAtIndex(j);
                                var typeProp = fe.FindPropertyRelative("type");
                                var fontProp = fe.FindPropertyRelative("font");
                                EditorGUILayout.PropertyField(fontProp, new GUIContent(typeProp.stringValue));
                            }
                        }
                    }
                }
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
