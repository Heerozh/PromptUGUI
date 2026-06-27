using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace PromptUGUI.Editor.ConsoleLog
{
    /// <summary>
    /// Pure renderer: turns <see cref="ConsoleLogEntry"/> values into compact, LLM-friendly text.
    /// No Unity dependencies, so it is fully unit-testable.
    /// </summary>
    internal static class LlmLogFormatter
    {
        public const int DefaultMaxFrames = 5;

        // Console rich-text presentation tags (color / bold / italic) — pure noise for an LLM.
        private static readonly Regex RichTextTag = new Regex(
            @"</?(?:color|b|i)(?:=[^>]*)?>",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static string FormatEntry(ConsoleLogEntry entry, int maxFrames)
        {
            var sb = new StringBuilder();

            // Header line: "[ts] TYPE message" — the bracket is dropped when no timestamp is available.
            if (!string.IsNullOrEmpty(entry.Timestamp))
                sb.Append('[').Append(entry.Timestamp).Append("] ");
            sb.Append(entry.Type);

            var message = StripTags(entry.Message);
            if (!string.IsNullOrEmpty(message))
                sb.Append(' ').Append(message);

            // Stack: keep the first maxFrames non-blank frames, then note how many were dropped.
            var frames = SplitFrames(entry.Callstack);
            var kept = frames.Count < maxFrames ? frames.Count : maxFrames;
            if (kept < 0)
                kept = 0;
            for (var i = 0; i < kept; i++)
                sb.Append("\n  ").Append(StripTags(frames[i]));

            var more = frames.Count - kept;
            if (more > 0)
                sb.Append("\n  ... (+").Append(more).Append(" more frames)");

            return sb.ToString();
        }

        public static string Format(IReadOnlyList<ConsoleLogEntry> entries, int maxFrames)
        {
            var sb = new StringBuilder();
            for (var i = 0; i < entries.Count; i++)
            {
                if (i > 0)
                    sb.Append("\n\n");
                sb.Append(FormatEntry(entries[i], maxFrames));
            }
            return sb.ToString();
        }

        private static string StripTags(string s) =>
            string.IsNullOrEmpty(s) ? s : RichTextTag.Replace(s, string.Empty);

        private static List<string> SplitFrames(string callstack)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(callstack))
                return result;
            foreach (var raw in callstack.Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length > 0)
                    result.Add(line);
            }
            return result;
        }
    }
}
