using System;
using System.Collections.Generic;
using System.Text;

namespace PromptUGUI.Editor
{
    /// <summary>
    /// Takes the comments out of a <c>.ui.xml</c> without moving anything the parser sees: the
    /// elements, their attributes, their text and the line each element starts on all come out the
    /// same, so a <c>UILog</c> location in a Player still names the source line.
    /// <see cref="UiXmlImporter"/> runs every <c>.ui.xml</c> through it
    /// (spec 2026-09-27-ui-xml-comment-stripping §3.2).
    ///
    /// <para>Input it cannot vouch for comes back as the very same string, with the reason:
    /// malformed at the comment / CDATA / PI / tag level (the runtime has to fail on it exactly where
    /// it fails on the source), a <c>&lt;!</c> declaration, or an <c>xml:space</c> attribute — inside
    /// <c>preserve</c> whitespace is text, and working out that scope is a parser's job.</para>
    /// </summary>
    internal static class XmlCommentStripper
    {
        public static string Strip(string xml) => Strip(xml, out _);

        /// <param name="keptBecause">Why <paramref name="xml"/> came back as written; null when its
        /// comments were taken out, or it had none.</param>
        public static string Strip(string xml, out string keptBecause)
        {
            keptBecause = null;
            if (string.IsNullOrEmpty(xml) || xml.IndexOf("<!--", StringComparison.Ordinal) < 0) return xml;

            var segments = new List<Segment>();
            keptBecause = Tokenize(xml, segments);
            if (keptBecause != null) return xml;
            return segments.Exists(s => s.Kind == Kind.Comment) ? Emit(xml, segments) : xml;
        }

        private enum Kind { Markup, Text, Comment }

        private readonly struct Segment
        {
            public readonly Kind Kind;
            public readonly int Start;
            public readonly int End;

            public Segment(Kind kind, int start, int end)
            {
                Kind = kind;
                Start = start;
                End = end;
            }
        }

        /// <summary>Cuts <paramref name="xml"/> into markup, text and comments; returns why it gave up, or null.</summary>
        private static string Tokenize(string xml, List<Segment> segments)
        {
            var i = 0;
            while (i < xml.Length)
            {
                var lt = xml.IndexOf('<', i);
                if (lt < 0)
                {
                    segments.Add(new Segment(Kind.Text, i, xml.Length));
                    break;
                }
                if (lt > i) segments.Add(new Segment(Kind.Text, i, lt));

                int end;
                if (At(xml, lt, "<!--"))
                {
                    // XML allows no "--" inside a comment, so the first one has to be the "-->"
                    var dashes = xml.IndexOf("--", lt + 4, StringComparison.Ordinal);
                    if (dashes < 0 || dashes + 2 >= xml.Length || xml[dashes + 2] != '>')
                        return $"the comment at line {LineAt(xml, lt)} is not closed, or has '--' inside it";
                    end = dashes + 3;
                    segments.Add(new Segment(Kind.Comment, lt, end));
                }
                else if (At(xml, lt, "<![CDATA["))
                {
                    var close = xml.IndexOf("]]>", lt + 9, StringComparison.Ordinal);
                    if (close < 0) return $"the CDATA section at line {LineAt(xml, lt)} is not closed";
                    end = close + 3;
                    segments.Add(new Segment(Kind.Markup, lt, end));
                }
                else if (At(xml, lt, "<!"))
                {
                    return $"line {LineAt(xml, lt)} has a '<!' declaration (a DOCTYPE?)";
                }
                else if (At(xml, lt, "<?"))
                {
                    var close = xml.IndexOf("?>", lt + 2, StringComparison.Ordinal);
                    if (close < 0) return $"the processing instruction at line {LineAt(xml, lt)} is not closed";
                    // A comment in front of the declaration is a parse error; stripping it would hide that.
                    if (IsXmlDeclaration(xml, lt) && !(lt == 0 || (lt == 1 && xml[0] == '﻿')))
                        return $"the XML declaration at line {LineAt(xml, lt)} is not the first thing in the file";
                    end = close + 2;
                    segments.Add(new Segment(Kind.Markup, lt, end));
                }
                else
                {
                    end = TagEnd(xml, lt);
                    if (end < 0) return $"the tag at line {LineAt(xml, lt)} is not closed";
                    if (xml.IndexOf("xml:space", lt, end - lt, StringComparison.Ordinal) >= 0)
                        return $"line {LineAt(xml, lt)} uses xml:space";
                    segments.Add(new Segment(Kind.Markup, lt, end));
                }
                i = end;
            }
            return null;
        }

        /// <summary>
        /// Writes the segments back without the comments. A run — the text and comments between two
        /// pieces of markup — holds one reader node per text segment, and the ones that are all XML
        /// whitespace are dropped by the parser (<c>PreserveWhitespace = false</c>):
        /// <list type="bullet">
        /// <item>a run with no real text keeps each comment's line breaks in place — more dropped
        /// whitespace, and every later element stays on its line;</item>
        /// <item>a run with real text (a comment inside a <c>&lt;Text&gt;</c>) keeps only that text. Its
        /// whitespace segments go too: once the comment next to one is gone it would join the text and
        /// its whitespace would stop being dropped. The line breaks taken out are owed, and paid at the
        /// start of the next run that has no real text, before the next element.</item>
        /// </list>
        /// </summary>
        private static string Emit(string xml, List<Segment> segments)
        {
            var sb = new StringBuilder(xml.Length);
            var owed = new StringBuilder();
            var k = 0;
            while (k < segments.Count)
            {
                if (segments[k].Kind == Kind.Markup)
                {
                    Append(sb, xml, segments[k]);
                    k++;
                    continue;
                }

                var runEnd = k;
                var hasText = false;
                for (; runEnd < segments.Count && segments[runEnd].Kind != Kind.Markup; runEnd++)
                    if (segments[runEnd].Kind == Kind.Text && !IsXmlWhitespace(xml, segments[runEnd])) hasText = true;

                if (!hasText)
                {
                    sb.Append(owed);
                    owed.Clear();
                }
                for (var r = k; r < runEnd; r++)
                {
                    var segment = segments[r];
                    if (segment.Kind == Kind.Text && (!hasText || !IsXmlWhitespace(xml, segment)))
                        Append(sb, xml, segment);
                    else
                        AppendLineBreaks(hasText ? owed : sb, xml, segment);
                }
                k = runEnd;
            }
            sb.Append(owed);   // whitespace after the root is allowed; nothing follows it that has a line
            return sb.ToString();
        }

        private static bool At(string xml, int index, string token) =>
            string.CompareOrdinal(xml, index, token, 0, token.Length) == 0;

        private static bool IsXmlDeclaration(string xml, int lt)
        {
            if (string.Compare(xml, lt + 2, "xml", 0, 3, StringComparison.OrdinalIgnoreCase) != 0) return false;
            var next = lt + 5 < xml.Length ? xml[lt + 5] : '\0';
            return next == '?' || IsXmlWhitespace(next);
        }

        /// <summary>One past the <c>&gt;</c> that closes the tag at <paramref name="lt"/>, or -1. A quoted
        /// attribute value may hold a literal <c>&gt;</c>.</summary>
        private static int TagEnd(string xml, int lt)
        {
            var quote = '\0';
            for (var j = lt + 1; j < xml.Length; j++)
            {
                var c = xml[j];
                if (quote != '\0')
                {
                    if (c == quote) quote = '\0';
                }
                else if (c == '"' || c == '\'') quote = c;
                else if (c == '>') return j + 1;
            }
            return -1;
        }

        private static bool IsXmlWhitespace(char c) => c == ' ' || c == '\t' || c == '\r' || c == '\n';

        private static bool IsXmlWhitespace(string xml, Segment segment)
        {
            for (var j = segment.Start; j < segment.End; j++)
                if (!IsXmlWhitespace(xml[j])) return false;
            return true;
        }

        private static void Append(StringBuilder sb, string xml, Segment segment) =>
            sb.Append(xml, segment.Start, segment.End - segment.Start);

        /// <summary>The segment's line breaks, each spelled as in the source (CRLF stays CRLF).</summary>
        private static void AppendLineBreaks(StringBuilder sb, string xml, Segment segment)
        {
            for (var j = segment.Start; j < segment.End; j++)
            {
                var c = xml[j];
                if (c == '\r')
                {
                    if (j + 1 < segment.End && xml[j + 1] == '\n')
                    {
                        sb.Append("\r\n");
                        j++;
                    }
                    else sb.Append('\r');
                }
                else if (c == '\n') sb.Append('\n');
            }
        }

        private static int LineAt(string xml, int index)
        {
            var line = 1;
            for (var j = 0; j < index; j++)
                if (xml[j] == '\n') line++;
            return line;
        }
    }
}
