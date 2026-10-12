using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using PromptUGUI.IR;

namespace PromptUGUI.Lint
{
    /// <summary>
    /// Reads what the lint needs out of a text-serialized <c>PromptUGUISettings</c> asset without
    /// Unity — for the UIXmlLint CLI, which has no <c>PromptUGUISettings.Instance</c> to ask: the
    /// <c>commonLibraries</c> (2026-09-18 commons-settings spec §4.9) and the font floor
    /// (2026-10-12 font-floor spec §5.7). The Editor menu reads the live asset instead.
    ///
    /// <para>Deliberately not a YAML parser. Unity serializes a top-level field as
    /// <c>  key: value</c> and a list of flat objects in exactly one shape —</para>
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

        /// <summary>TextMesh Pro's settings asset, where the size an unsized <c>&lt;Text&gt;</c> builds at lives.</summary>
        public const string TmpSettingsFileName = "TMP Settings.asset";

        /// <summary>True when <paramref name="head"/> (the file's first lines are enough) names the settings script.</summary>
        public static bool IsPromptUGUISettings(string head) =>
            head != null && head.Contains("guid: " + ScriptGuid);

        /// <summary>
        /// The <c>commonLibraries</c> rows, in asset order. Empty for <c>[]</c>, for an asset saved
        /// before the field existed, and for anything that is not a settings asset at all. Blank
        /// rows are kept (they are inert everywhere), quoting is undone, <c>as</c> is left as
        /// written (<see cref="CommonLibraryEntry.ToImportRef"/> maps blank to no namespace).
        /// </summary>
        public static List<CommonLibraryEntry> ReadCommonLibraries(string yaml) =>
            ReadList(yaml, "commonLibraries", () => new CommonLibraryEntry(), (entry, key, value) =>
            {
                switch (key)
                {
                    case "src": entry.src = value; break;
                    case "as": entry.@as = value; break;
                }
            });

        /// <summary>
        /// <c>fontFloor</c> in dp. Absent (an asset saved before the field existed) reads as
        /// <see cref="FontFloorPolicy.DefaultFloor"/> — what Unity deserializes a missing field to.
        /// </summary>
        public static float ReadFontFloor(string yaml) => ReadFloat(yaml, "fontFloor") ?? FontFloorPolicy.DefaultFloor;

        /// <summary>The <c>fontFloorTargets</c> rows, in asset order, as written (blank rows kept).</summary>
        public static List<FontFloorTarget> ReadFontFloorTargets(string yaml) =>
            ReadList(yaml, "fontFloorTargets", () => new FontFloorTarget(), (target, key, value) =>
            {
                switch (key)
                {
                    case "name": target.name = value; break;
                    case "screen": target.screen = value; break;
                    case "variants": target.variants = value; break;
                }
            });

        /// <summary><c>m_defaultFontSize</c> of a <c>TMP Settings.asset</c>; null when it is not there.</summary>
        public static float? ReadTmpDefaultFontSize(string yaml) => ReadFloat(yaml, "m_defaultFontSize");

        /// <summary>The first <c>key: number</c> line; null when there is none or it is not a number.</summary>
        private static float? ReadFloat(string yaml, string key)
        {
            if (string.IsNullOrEmpty(yaml)) return null;
            var prefix = key + ":";
            foreach (var raw in yaml.Split('\n'))
            {
                var body = raw.TrimEnd('\r').TrimStart(' ');
                if (!body.StartsWith(prefix, StringComparison.Ordinal)) continue;
                return float.TryParse(Unquote(body.Substring(prefix.Length).Trim()), NumberStyles.Float,
                                      CultureInfo.InvariantCulture, out var value)
                    ? value
                    : (float?)null;
            }
            return null;
        }

        /// <summary>
        /// A list of flat objects under <c>key:</c> — every <c>- k: v</c> starts an item, deeper
        /// <c>k: v</c> lines fill it, a line back at the key's indent ends the list.
        /// </summary>
        private static List<T> ReadList<T>(string yaml, string key, Func<T> create, Action<T, string, string> assign)
            where T : class
        {
            var entries = new List<T>();
            if (string.IsNullOrEmpty(yaml)) return entries;

            var listKey = key + ":";
            var lines = yaml.Split('\n');
            var i = 0;
            var keyIndent = -1;
            for (; i < lines.Length; i++)
            {
                var line = lines[i].TrimEnd('\r');
                var indent = Indent(line);
                var body = line.Substring(indent);
                if (!body.StartsWith(listKey, StringComparison.Ordinal)) continue;
                var rest = body.Substring(listKey.Length).Trim();
                if (rest == "[]") return entries;           // empty list, inline
                if (rest.Length != 0) return entries;       // not the shape Unity writes
                keyIndent = indent;
                i++;
                break;
            }
            if (keyIndent < 0) return entries;

            T current = null;
            for (; i < lines.Length; i++)
            {
                var line = lines[i].TrimEnd('\r');
                if (line.Trim().Length == 0) continue;
                var indent = Indent(line);
                var body = line.Substring(indent);

                if (indent == keyIndent && body.StartsWith("- ", StringComparison.Ordinal))
                {
                    current = create();
                    entries.Add(current);
                    Assign(current, body.Substring(2), assign);
                    continue;
                }
                if (indent > keyIndent && current != null)
                {
                    Assign(current, body, assign);
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

        private static void Assign<T>(T entry, string keyValue, Action<T, string, string> assign)
        {
            var colon = keyValue.IndexOf(':');
            if (colon < 0) return;
            var key = keyValue.Substring(0, colon).Trim();
            var value = Unquote(keyValue.Substring(colon + 1).Trim());
            assign(entry, key, value);
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
