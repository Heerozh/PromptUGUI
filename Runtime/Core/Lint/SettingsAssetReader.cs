using System;
using System.Collections.Generic;
using System.Text;
using PromptUGUI.IR;

namespace PromptUGUI.Lint
{
    /// <summary>
    /// Reads <c>commonLibraries</c> out of a text-serialized <c>PromptUGUISettings</c> asset
    /// without Unity — for the UIXmlLint CLI, which has no <c>PromptUGUISettings.Instance</c> to ask
    /// (2026-09-18 commons-settings spec §4.9). The Editor menu reads the live asset instead.
    ///
    /// <para>Deliberately not a YAML parser. Unity serializes a <c>List&lt;CommonLibraryEntry&gt;</c>
    /// in exactly one shape —</para>
    /// <code>
    ///   commonLibraries:
    ///   - src: UI/Templates/Theme.ui
    ///     as:
    /// </code>
    /// <para>(or <c>commonLibraries: []</c>) — with values single-quoted only when they need it and
    /// <c>'</c> doubled inside. That subset is what this reads, and the EditorOnly round-trip test
    /// (<c>SettingsAssetRoundTripTests</c>) pins that a real asset still serializes into it.</para>
    /// </summary>
    public static class SettingsAssetReader
    {
        /// <summary><c>Runtime/Application/PromptUGUISettings.cs.meta</c>'s guid — how an asset says what it is.</summary>
        public const string ScriptGuid = "ef72af4be0229a24cb2ab979147bdc01";

        /// <summary>The <c>[CreateAssetMenu(fileName)]</c> default; the first thing a discovery looks for.</summary>
        public const string DefaultFileName = "PromptUGUI_Settings.asset";

        private const string ListKey = "commonLibraries:";

        /// <summary>True when <paramref name="head"/> (the file's first lines are enough) names the settings script.</summary>
        public static bool IsPromptUGUISettings(string head) =>
            head != null && head.Contains("guid: " + ScriptGuid);

        /// <summary>
        /// The <c>commonLibraries</c> rows, in asset order. Empty for <c>[]</c>, for an asset saved
        /// before the field existed, and for anything that is not a settings asset at all. Blank
        /// rows are kept (they are inert everywhere), quoting is undone, <c>as</c> is left as
        /// written (<see cref="CommonLibraryEntry.ToImportRef"/> maps blank to no namespace).
        /// </summary>
        public static List<CommonLibraryEntry> ReadCommonLibraries(string yaml)
        {
            var entries = new List<CommonLibraryEntry>();
            if (string.IsNullOrEmpty(yaml)) return entries;

            var lines = yaml.Split('\n');
            var i = 0;
            var keyIndent = -1;
            for (; i < lines.Length; i++)
            {
                var line = lines[i].TrimEnd('\r');
                var indent = Indent(line);
                var body = line.Substring(indent);
                if (!body.StartsWith(ListKey, StringComparison.Ordinal)) continue;
                var rest = body.Substring(ListKey.Length).Trim();
                if (rest == "[]") return entries;           // empty list, inline
                if (rest.Length != 0) return entries;       // not the shape Unity writes
                keyIndent = indent;
                i++;
                break;
            }
            if (keyIndent < 0) return entries;

            CommonLibraryEntry current = null;
            for (; i < lines.Length; i++)
            {
                var line = lines[i].TrimEnd('\r');
                if (line.Trim().Length == 0) continue;
                var indent = Indent(line);
                var body = line.Substring(indent);

                if (indent == keyIndent && body.StartsWith("- ", StringComparison.Ordinal))
                {
                    current = new CommonLibraryEntry();
                    entries.Add(current);
                    Assign(current, body.Substring(2));
                    continue;
                }
                if (indent > keyIndent && current != null)
                {
                    Assign(current, body);
                    continue;
                }
                break;   // back at the parent's indent: the list is over
            }
            return entries;
        }

        private static int Indent(string line)
        {
            var n = 0;
            while (n < line.Length && line[n] == ' ') n++;
            return n;
        }

        private static void Assign(CommonLibraryEntry entry, string keyValue)
        {
            var colon = keyValue.IndexOf(':');
            if (colon < 0) return;
            var key = keyValue.Substring(0, colon).Trim();
            var value = Unquote(keyValue.Substring(colon + 1).Trim());
            switch (key)
            {
                case "src": entry.src = value; break;
                case "as": entry.@as = value; break;
            }
        }

        /// <summary>Unity single-quotes when it must (doubling an inner quote); double quotes are rare but legal.</summary>
        private static string Unquote(string value)
        {
            if (value.Length >= 2 && value[0] == '\'' && value[value.Length - 1] == '\'')
                return value.Substring(1, value.Length - 2).Replace("''", "'");
            if (value.Length >= 2 && value[0] == '"' && value[value.Length - 1] == '"')
            {
                var sb = new StringBuilder(value.Length);
                for (var k = 1; k < value.Length - 1; k++)
                {
                    var c = value[k];
                    if (c == '\\' && k + 1 < value.Length - 1) { sb.Append(value[++k]); continue; }
                    sb.Append(c);
                }
                return sb.ToString();
            }
            return value;
        }
    }
}
