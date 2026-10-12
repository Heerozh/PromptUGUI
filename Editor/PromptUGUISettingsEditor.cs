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

            DrawFontFloor(serializedObject.FindProperty("fontFloor"), serializedObject.FindProperty("fontFloorTargets"));

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

        /// <summary>
        /// Common screens, as <c>physical pixels @ OS scale</c> in their natural orientation; phones and
        /// tablets also get a landscape row. The numbers are what a spec sheet says, so the preset is
        /// where the "which scale does a TV have" knowledge lives (2026-10-12 font-floor spec §5.9).
        /// </summary>
        private static readonly (string Name, string Screen, bool Rotates)[] FontFloorPresets =
        {
            ("iPhone SE", "750x1334@2x", true),
            ("iPhone 16", "1179x2556@3x", true),
            ("iPhone 16 Pro Max", "1320x2868@3x", true),
            ("Android 360dp", "1080x2400@3x", true),
            ("iPad mini", "1488x2266@2x", true),
            ("Laptop 1366x768", "1366x768@1x", false),
            ("Laptop 1080p @150%", "1920x1080@1.5x", false),
            ("Desktop 1080p", "1920x1080@1x", false),
            ("TV 1080p", "1920x1080@2x", false),
            ("TV 4K", "3840x2160@4x", false),
        };

        /// <summary>
        /// The floor in dp and the screens it is checked on. Lint-only — the runtime never reads
        /// either. The presets save looking up what scale a device runs at.
        /// </summary>
        private void DrawFontFloor(SerializedProperty floor, SerializedProperty targets)
        {
            EditorGUILayout.LabelField("Font Floor (lint)", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "PUI-FONT-FLOOR reports text that renders below the floor on any target screen: fontSize × " +
                "canvas factor × ancestor scale ÷ OS scale. The floor is in dp — iOS pt ≈ Android dp ≈ CSS px ≈ " +
                "Windows effective px, a unit tuned to each device class's viewing distance, so one number " +
                "holds on a phone, a monitor and a TV. 11 is Apple's iOS minimum; 0 turns the rule off. " +
                "A target is physical pixels @ OS scale (750x1334@2x); taller than wide is 'portrait'. " +
                "Tools › PromptUGUI › Lint All UI XML runs it.",
                MessageType.None);
            EditorGUILayout.PropertyField(floor, new GUIContent("Floor (dp)"));
            EditorGUILayout.PropertyField(targets, new GUIContent("Targets"), true);

            if (floor.floatValue > 0f && targets.arraySize == 0)
                EditorGUILayout.HelpBox("PUI-FONT-FLOOR is inactive until at least one target is listed.",
                                        MessageType.Warning);

            if (GUILayout.Button("Add preset ▾"))
            {
                var menu = new GenericMenu();
                foreach (var preset in FontFloorPresets)
                {
                    if (!preset.Rotates)
                    {
                        menu.AddItem(new GUIContent(preset.Name), false,
                            () => AddFontFloorTarget(preset.Name, preset.Screen));
                        continue;
                    }
                    menu.AddItem(new GUIContent(preset.Name + "/Portrait"), false,
                        () => AddFontFloorTarget(preset.Name, preset.Screen));
                    menu.AddItem(new GUIContent(preset.Name + "/Landscape"), false,
                        () => AddFontFloorTarget(preset.Name + " (landscape)", Rotate(preset.Screen)));
                }
                menu.ShowAsContext();
            }
        }

        // A menu callback runs after OnInspectorGUI has returned, so it opens its own edit.
        private void AddFontFloorTarget(string name, string screen)
        {
            serializedObject.Update();
            var targets = serializedObject.FindProperty("fontFloorTargets");
            targets.InsertArrayElementAtIndex(targets.arraySize);
            var row = targets.GetArrayElementAtIndex(targets.arraySize - 1);
            row.FindPropertyRelative("name").stringValue = name;
            row.FindPropertyRelative("screen").stringValue = screen;
            row.FindPropertyRelative("variants").stringValue = "";
            serializedObject.ApplyModifiedProperties();
        }

        // "750x1334@2x" → "1334x750@2x".
        private static string Rotate(string screen)
        {
            var at = screen.IndexOf('@');
            var size = at < 0 ? screen : screen.Substring(0, at);
            var scale = at < 0 ? "" : screen.Substring(at);
            var x = size.IndexOf('x');
            return size.Substring(x + 1) + "x" + size.Substring(0, x) + scale;
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
