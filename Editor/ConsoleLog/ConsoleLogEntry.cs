namespace PromptUGUI.Editor.ConsoleLog
{
    /// <summary>
    /// One Console row, decomposed into the pieces an LLM-friendly dump needs.
    /// Produced by <c>ConsoleLogReader</c>; rendered by <c>LlmLogFormatter</c>.
    /// </summary>
    internal readonly struct ConsoleLogEntry
    {
        /// <summary>Time-of-day, e.g. "23:39:28" (no brackets). Empty when unavailable.</summary>
        public readonly string Timestamp;

        /// <summary>Severity label: "LOG", "WARN", or "ERROR".</summary>
        public readonly string Type;

        /// <summary>Log message body (may contain console rich-text tags; may be multi-line).</summary>
        public readonly string Message;

        /// <summary>Raw callstack block, frames separated by '\n'. Empty when the entry has no stack.</summary>
        public readonly string Callstack;

        public ConsoleLogEntry(string timestamp, string type, string message, string callstack)
        {
            Timestamp = timestamp;
            Type = type;
            Message = message;
            Callstack = callstack;
        }
    }
}
