using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace PromptUGUI.Editor.ConsoleLog
{
    internal enum ConsoleReadStatus
    {
        Ok,
        NoConsoleWindow,
        NoSelection,
        ReflectionFailed,
    }

    /// <summary>
    /// Reads the currently-selected Console rows via UnityEditor internals (reflection) and decomposes
    /// each into a <see cref="ConsoleLogEntry"/> (timestamp + severity + body + callstack).
    ///
    /// Unity exposes no public API for this, so everything here pokes <c>ConsoleWindow</c> /
    /// <c>LogEntries</c> by reflection. The implementation is best-effort: any failure yields an empty
    /// result with a status code rather than throwing. Key facts (verified against Unity 6):
    ///   • Selection lives in <c>ConsoleWindow.m_ListView.selectedItems</c> (bool[], one per row,
    ///     indexed the same as <c>LogEntries.GetEntryInternal</c>).
    ///   • Timestamps are stored natively but only surfaced when the <c>ShowTimestamp</c> console flag
    ///     is on, via <c>GetLinesAndModeFromEntryInternal</c> (prefix "[HH:MM:SS] "). We toggle the flag
    ///     on for the read and restore it, so even historical rows get a timestamp.
    ///   • <c>LogEntry.callstackTextStartUTF16</c> is the offset in <c>message</c> where the callstack
    ///     begins — the clean split point between body and stack.
    /// </summary>
    internal static class ConsoleLogReader
    {
        private const int ShowTimestampFlag = 1 << 10; // ConsoleWindow.ConsoleFlags.ShowTimestamp

        private static readonly Regex TimestampPrefix =
            new Regex(@"^\[(\d{1,2}:\d{2}:\d{2})\]", RegexOptions.Compiled);

        // Lazily-resolved reflection handles. Resolved once; _resolveOk gates every public call.
        private static bool _resolved;
        private static bool _resolveOk;
        private static Type _consoleWindowType;
        private static FieldInfo _listViewField;     // ConsoleWindow.m_ListView
        private static FieldInfo _selectedItemsField; // ListViewState.selectedItems (bool[])
        private static PropertyInfo _consoleFlagsProp; // LogEntries.consoleFlags (int)
        private static MethodInfo _startGetting;     // LogEntries.StartGettingEntries
        private static MethodInfo _endGetting;       // LogEntries.EndGettingEntries
        private static MethodInfo _getEntry;         // LogEntries.GetEntryInternal(int, out LogEntry)
        private static MethodInfo _getLines;         // LogEntries.GetLinesAndModeFromEntryInternal
        private static Type _logEntryType;
        private static FieldInfo _entryMessage;
        private static FieldInfo _entryMode;
        private static FieldInfo _entryCsStart;      // LogEntry.callstackTextStartUTF16
        private static int _errorMask;
        private static int _warnMask;

        /// <summary>Cheap check for the menu validate path: is at least one Console row selected?</summary>
        public static bool HasSelection()
        {
            if (!ResolveReflection())
                return false;
            var console = FindConsoleWindow();
            return console != null && GetSelectedRows(console).Count > 0;
        }

        /// <summary>Reads selected rows in top-to-bottom (chronological) order.</summary>
        public static List<ConsoleLogEntry> ReadSelected(out ConsoleReadStatus status)
        {
            var result = new List<ConsoleLogEntry>();

            if (!ResolveReflection())
            {
                status = ConsoleReadStatus.ReflectionFailed;
                return result;
            }

            var console = FindConsoleWindow();
            if (console == null)
            {
                status = ConsoleReadStatus.NoConsoleWindow;
                return result;
            }

            var rows = GetSelectedRows(console);
            if (rows.Count == 0)
            {
                status = ConsoleReadStatus.NoSelection;
                return result;
            }

            try
            {
                var origFlags = (int)_consoleFlagsProp.GetValue(null);
                _startGetting.Invoke(null, null);
                try
                {
                    _consoleFlagsProp.SetValue(null, origFlags | ShowTimestampFlag);
                    foreach (var row in rows)
                    {
                        var entry = ReadRow(row);
                        if (entry.HasValue)
                            result.Add(entry.Value);
                    }
                }
                finally
                {
                    _consoleFlagsProp.SetValue(null, origFlags); // never leave the user's console toggled
                    _endGetting.Invoke(null, null);
                }
            }
            catch
            {
                status = ConsoleReadStatus.ReflectionFailed;
                return new List<ConsoleLogEntry>();
            }

            status = result.Count > 0 ? ConsoleReadStatus.Ok : ConsoleReadStatus.NoSelection;
            return result;
        }

        private static ConsoleLogEntry? ReadRow(int row)
        {
            // GetEntryInternal fills a pre-allocated LogEntry in place (native [Out]); reuse the probe pattern.
            var entry = Activator.CreateInstance(_logEntryType);
            var args = new object[] { row, entry };
            if (!(bool)_getEntry.Invoke(null, args))
                return null;
            var e = args[1] ?? entry;

            var message = (string)_entryMessage.GetValue(e) ?? string.Empty;
            var mode = (int)_entryMode.GetValue(e);
            var csStart = (int)_entryCsStart.GetValue(e);

            string body, callstack;
            if (csStart > 0 && csStart <= message.Length)
            {
                body = message.Substring(0, csStart).TrimEnd();
                callstack = message.Substring(csStart);
            }
            else
            {
                body = message;
                callstack = string.Empty;
            }

            return new ConsoleLogEntry(ReadTimestamp(row), Classify(mode), body, callstack);
        }

        // Extract "[HH:MM:SS]" that the ShowTimestamp flag prepends to the formatted display line.
        private static string ReadTimestamp(int row)
        {
            try
            {
                var args = new object[] { row, 1, 0, null };
                _getLines.Invoke(null, args);
                var line = args[3] as string;
                if (string.IsNullOrEmpty(line))
                    return string.Empty;
                var m = TimestampPrefix.Match(line);
                return m.Success ? m.Groups[1].Value : string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string Classify(int mode)
        {
            if ((mode & _errorMask) != 0)
                return "ERROR";
            if ((mode & _warnMask) != 0)
                return "WARN";
            return "LOG";
        }

        private static List<int> GetSelectedRows(EditorWindow console)
        {
            var rows = new List<int>();
            try
            {
                var listView = _listViewField.GetValue(console);
                if (listView == null)
                    return rows;
                if (!(_selectedItemsField.GetValue(listView) is bool[] selected))
                    return rows;
                for (var i = 0; i < selected.Length; i++)
                    if (selected[i])
                        rows.Add(i); // ascending index == top-to-bottom == chronological
            }
            catch
            {
                // leave rows empty
            }
            return rows;
        }

        private static EditorWindow FindConsoleWindow()
        {
            var all = Resources.FindObjectsOfTypeAll(_consoleWindowType);
            return all.Length > 0 ? all[0] as EditorWindow : null;
        }

        private static bool ResolveReflection()
        {
            if (_resolved)
                return _resolveOk;
            _resolved = true;
            try
            {
                const BindingFlags bfStatic = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
                const BindingFlags bfInstance = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

                var asm = typeof(UnityEditor.Editor).Assembly;
                _consoleWindowType = asm.GetType("UnityEditor.ConsoleWindow");
                _logEntryType = asm.GetType("UnityEditor.LogEntry");
                var logEntries = asm.GetType("UnityEditor.LogEntries");
                if (_consoleWindowType == null || _logEntryType == null || logEntries == null)
                    return false;

                _listViewField = _consoleWindowType.GetField("m_ListView", bfInstance);
                _consoleFlagsProp = logEntries.GetProperty("consoleFlags", bfStatic);
                _startGetting = logEntries.GetMethod("StartGettingEntries", bfStatic);
                _endGetting = logEntries.GetMethod("EndGettingEntries", bfStatic);
                _getEntry = logEntries.GetMethod("GetEntryInternal", bfStatic);
                _getLines = logEntries.GetMethod("GetLinesAndModeFromEntryInternal", bfStatic);
                _entryMessage = _logEntryType.GetField("message", bfInstance);
                _entryMode = _logEntryType.GetField("mode", bfInstance);
                _entryCsStart = _logEntryType.GetField("callstackTextStartUTF16", bfInstance);

                if (_listViewField == null || _consoleFlagsProp == null || _startGetting == null ||
                    _endGetting == null || _getEntry == null || _getLines == null ||
                    _entryMessage == null || _entryMode == null || _entryCsStart == null)
                    return false;

                var listViewType = _listViewField.FieldType;
                _selectedItemsField = listViewType.GetField("selectedItems", bfInstance);
                if (_selectedItemsField == null)
                    return false;

                BuildModeMasks();
                _resolveOk = true;
            }
            catch
            {
                _resolveOk = false;
            }
            return _resolveOk;
        }

        // Classify by enum-name substring so it survives Unity adding/renaming Mode bits across versions.
        private static void BuildModeMasks()
        {
            var modeEnum = _consoleWindowType.GetNestedType(
                "Mode", BindingFlags.NonPublic | BindingFlags.Public);
            if (modeEnum == null)
            {
                // Fallback to known Unity 6 bit values.
                _errorMask = 1 | 2 | 16 | 64 | 256 | 2048 | 8192 | 131072 | 1048576 | 2097152 | 4194304;
                _warnMask = 128 | 512 | 4096;
                return;
            }
            foreach (var v in Enum.GetValues(modeEnum))
            {
                var bit = Convert.ToInt32(v);
                var name = Enum.GetName(modeEnum, v) ?? string.Empty;
                if (Contains(name, "Error") || Contains(name, "Fatal") ||
                    Contains(name, "Assert") || Contains(name, "Exception"))
                    _errorMask |= bit;
                else if (Contains(name, "Warning"))
                    _warnMask |= bit;
            }
        }

        private static bool Contains(string s, string sub) =>
            s.IndexOf(sub, StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
