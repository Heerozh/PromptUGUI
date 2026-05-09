using System.IO;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace PromptUGUI.Editor.I18n
{
    internal static class TranslationProviderSettingsProvider
    {
        private const string ProviderPath = "ProjectSettings/PromptUGUI.asset";
        private const string AuthPath = "UserSettings/PromptUGUI/Auth.asset";

        internal static TranslationProvider GetOrCreateProvider()
        {
            var loaded = InternalEditorUtility.LoadSerializedFileAndForget(ProviderPath);
            if (loaded != null && loaded.Length > 0 && loaded[0] is TranslationProvider tp) return tp;
            var fresh = ScriptableObject.CreateInstance<TranslationProvider>();
            Save(fresh, ProviderPath);
            return fresh;
        }

        internal static TranslationAuth GetOrCreateAuth()
        {
            var loaded = InternalEditorUtility.LoadSerializedFileAndForget(AuthPath);
            if (loaded != null && loaded.Length > 0 && loaded[0] is TranslationAuth a) return a;
            var fresh = ScriptableObject.CreateInstance<TranslationAuth>();
            Save(fresh, AuthPath);
            return fresh;
        }

        internal static void Save(Object obj, string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            InternalEditorUtility.SaveToSerializedFileAndForget(new[] { obj }, path, allowTextSerialization: true);
        }

        [SettingsProvider]
        public static SettingsProvider Create() => new("Project/PromptUGUI/Translation", SettingsScope.Project)
        {
            label = "Translation",
            guiHandler = _ =>
            {
                var tp = GetOrCreateProvider();
                var auth = GetOrCreateAuth();
                EditorGUI.BeginChangeCheck();
                tp.endpoint = EditorGUILayout.TextField("Endpoint", tp.endpoint);
                tp.model = EditorGUILayout.TextField("Model", tp.model);
                EditorGUILayout.LabelField("System Prompt");
                tp.systemPrompt = EditorGUILayout.TextArea(tp.systemPrompt, GUILayout.Height(140));
                EditorGUILayout.Space();
                auth.apiKey = EditorGUILayout.PasswordField("API Key (UserSettings)", auth.apiKey);
                if (EditorGUI.EndChangeCheck())
                {
                    Save(tp, ProviderPath);
                    Save(auth, AuthPath);
                }
            },
            keywords = new System.Collections.Generic.HashSet<string> {
                "PromptUGUI", "Translation", "OpenAI", "i18n",
            },
        };
    }
}
