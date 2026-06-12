using UnityEngine;

namespace PromptUGUI.Editor.I18n
{
    /// <summary>
    /// Project-level translation provider config. Lives at
    /// `ProjectSettings/PromptUGUI.asset` (in repo, team-shared).
    /// </summary>
    internal sealed class TranslationProvider : ScriptableObject
    {
        public string endpoint = "https://api.deepseek.com/chat/completions";
        public string model = "deepseek-v4-flash";
        [TextArea(6, 20)]
        public string systemPrompt =
@"You are translating a game's UI strings into {{targetLocale}}.

Rules:
1. Keep all {{x}} template placeholders and C# format placeholders such as {0}, {1:C} unchanged
2. Keep TMP rich-text tags (<sprite>, <color>, <b>, <size>, <link>, etc.) and their attribute values literal and unchanged (in particular, values inside attributes like name=""..."" and color=""..."" are resource IDs, not text); their position may be reordered to match the target language's word order
3. Refer to the sibling strings to infer a consistent style
4. The source text may mix multiple languages; translate the overall meaning into the target locale
5. Keep it short and direct; UI space is limited";
    }
}
