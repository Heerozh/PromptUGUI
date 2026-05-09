using UnityEngine;

namespace PromptUGUI.Editor.I18n {
    /// <summary>
    /// Project-level translation provider config. Lives at
    /// `ProjectSettings/PromptUGUI.asset` (in repo, team-shared).
    /// </summary>
    internal sealed class TranslationProvider : ScriptableObject {
        public string endpoint = "https://api.openai.com/v1/chat/completions";
        public string model = "gpt-4o-mini";
        [TextArea(6, 20)] public string systemPrompt =
@"你正在为一款像素风游戏翻译 UI 字符串到 {{targetLocale}}。

规则：
1. 保留所有 {{x}} 模板占位符与 {0} {1:C} 等 C# 格式占位符不变
2. 保留 TMP 富文本标签（<sprite>、<color>、<b>、<size>、<link> 等）的字面形式与属性值不变（特别是 name=""..."", color=""..."" 等属性内的值是资源 ID，不是文本）；位置可调以符合目标语言语序
3. 参考 sibling strings 推断风格一致性
4. 源文本可能混合多种语言；按目标 locale 翻译整体含义
5. 简短直接；UI 空间有限";
    }
}
