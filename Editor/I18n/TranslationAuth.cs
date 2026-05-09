using UnityEngine;

namespace PromptUGUI.Editor.I18n {
    /// <summary>
    /// Per-user secret. Lives at `UserSettings/PromptUGUI/Auth.asset` (Unity 2020+
    /// default .gitignore excludes UserSettings/).
    /// </summary>
    internal sealed class TranslationAuth : ScriptableObject {
        public string apiKey = "";
    }
}
