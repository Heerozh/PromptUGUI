using UnityEditor;
using UnityEngine;

namespace PromptUGUI.Editor.ConsoleLog
{
    /// <summary>
    /// Tools-menu entry that copies the rows selected in the Console window to the clipboard in a
    /// compact, LLM-friendly form: per-entry timestamp, severity, message, and a stack trimmed to the
    /// first few frames. Fills the gap that Unity's built-in Ctrl+C copy leaves (no timestamps, full stacks).
    /// </summary>
    internal static class CopyConsoleLogMenu
    {
        private const string MenuPath = "Tools/PromptUGUI/Copy Selected Console Log (For LLM)";

        [MenuItem(MenuPath, priority = 2000)]
        private static void CopySelected()
        {
            var entries = ConsoleLogReader.ReadSelected(out var status);
            switch (status)
            {
                case ConsoleReadStatus.NoConsoleWindow:
                    Debug.LogWarning("[PromptUGUI] 请先打开 Console 窗口并选中日志。");
                    return;
                case ConsoleReadStatus.NoSelection:
                    Debug.LogWarning("[PromptUGUI] 请先在 Console 里选中要复制的日志（可多选）。");
                    return;
                case ConsoleReadStatus.ReflectionFailed:
                    Debug.LogWarning("[PromptUGUI] 无法读取 Console 日志（Unity 内部 API 可能已变化）。");
                    return;
            }

            var text = LlmLogFormatter.Format(entries, LlmLogFormatter.DefaultMaxFrames);
            EditorGUIUtility.systemCopyBuffer = text;
            Debug.Log($"[PromptUGUI] 已复制 {entries.Count} 条日志到剪贴板（大模型友好格式）。");
        }
    }
}
