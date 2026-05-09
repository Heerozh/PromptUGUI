using System.Collections.Generic;
using System.Text;

namespace PromptUGUI.I18n
{
    public static partial class PoParser
    {
        public static IEnumerable<PoEntry> Parse(string source)
        {
            if (string.IsNullOrEmpty(source)) yield break;
            var lines = source.Replace("\r\n", "\n").Split('\n');
            var i = 0;
            while (i < lines.Length)
            {
                // Skip blank lines.
                while (i < lines.Length && lines[i].Trim().Length == 0) i++;
                if (i >= lines.Length) yield break;

                // Collect leading comment lines; detect if this block is obsolete (#~).
                // An obsolete block consists entirely of #~ lines — skip them wholesale
                // without consuming any real msgid/msgstr that follow.
                if (lines[i].StartsWith("#"))
                {
                    // Peek ahead: is this an obsolete block?
                    var blockObsolete = lines[i].StartsWith("#~");
                    if (blockObsolete)
                    {
                        // Skip all consecutive #~ lines.
                        while (i < lines.Length && lines[i].StartsWith("#~")) i++;
                        continue; // restart outer loop; next content may be a real entry
                    }
                    // Non-obsolete comment block: will be collected inside the entry below.
                }

                var entry = new PoEntry();
                bool sawMsgid = false, sawMsgstr = false;

                // Collect translator comments (non-obsolete # lines).
                while (i < lines.Length && lines[i].StartsWith("#") && !lines[i].StartsWith("#~"))
                {
                    var c = lines[i];
                    var rest = c.Length > 1 ? c.Substring(1).TrimStart() : "";
                    entry.TranslatorComments.Add(rest);
                    i++;
                }

                // msgctxt (optional)
                if (i < lines.Length && lines[i].StartsWith("msgctxt "))
                {
                    entry.Msgctxt = ReadStringValue(lines, ref i, "msgctxt ");
                }

                // msgid (required)
                if (i < lines.Length && lines[i].StartsWith("msgid "))
                {
                    entry.Msgid = ReadStringValue(lines, ref i, "msgid ");
                    sawMsgid = true;
                }

                // msgstr (required)
                if (i < lines.Length && lines[i].StartsWith("msgstr "))
                {
                    entry.Msgstr = ReadStringValue(lines, ref i, "msgstr ");
                    sawMsgstr = true;
                }

                if (!sawMsgid)
                    throw new PoParseException($"line {i + 1}: expected msgid");
                if (!sawMsgstr)
                    throw new PoParseException($"line {i + 1}: expected msgstr after msgid \"{entry.Msgid}\"");

                yield return entry;
            }
        }

        private static string ReadStringValue(string[] lines, ref int i, string keyword)
        {
            var sb = new StringBuilder();
            sb.Append(Decode(ExtractQuoted(lines[i].Substring(keyword.Length))));
            i++;
            while (i < lines.Length && lines[i].Length > 0 && lines[i][0] == '"')
            {
                sb.Append(Decode(ExtractQuoted(lines[i])));
                i++;
            }
            return sb.ToString();
        }

        private static string ExtractQuoted(string s)
        {
            s = s.Trim();
            if (s.Length < 2 || s[0] != '"' || s[s.Length - 1] != '"')
                throw new PoParseException($"malformed quoted string: {s}");
            return s.Substring(1, s.Length - 2);
        }

        public static string Serialize(IEnumerable<PoEntry> entries)
        {
            var sb = new StringBuilder();
            foreach (var e in entries)
            {
                if (e.TranslatorComments != null)
                {
                    foreach (var line in e.TranslatorComments)
                        sb.Append("# ").Append(line).Append('\n');
                }
                if (e.Msgctxt != null)
                {
                    sb.Append("msgctxt \"").Append(Encode(e.Msgctxt)).Append("\"\n");
                }
                sb.Append("msgid \"").Append(Encode(e.Msgid ?? "")).Append("\"\n");
                sb.Append("msgstr \"").Append(Encode(e.Msgstr ?? "")).Append("\"\n");
                sb.Append('\n');
            }
            return sb.ToString();
        }

        private static string Encode(string s)
        {
            var sb = new StringBuilder(s.Length);
            foreach (var c in s)
            {
                switch (c)
                {
                    case '\n': sb.Append("\\n"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    default: sb.Append(c); break;
                }
            }
            return sb.ToString();
        }

        private static string Decode(string raw)
        {
            var sb = new StringBuilder(raw.Length);
            for (var j = 0; j < raw.Length; j++)
            {
                var c = raw[j];
                if (c == '\\' && j + 1 < raw.Length)
                {
                    var n = raw[++j];
                    switch (n)
                    {
                        case 'n': sb.Append('\n'); break;
                        case 't': sb.Append('\t'); break;
                        case 'r': sb.Append('\r'); break;
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        default: sb.Append(n); break;
                    }
                }
                else
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }
    }
}
