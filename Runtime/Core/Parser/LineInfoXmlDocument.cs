using System.Collections.Generic;
using System.IO;
using System.Xml;

namespace PromptUGUI.Parser
{
    /// <summary>
    /// An <see cref="XmlElement"/> that remembers where it was in the source text.
    /// </summary>
    internal sealed class LineInfoElement : XmlElement
    {
        public int Line { get; }
        public int Column { get; }

        internal LineInfoElement(
            string prefix, string localName, string namespaceUri, XmlDocument doc, int line, int column)
            : base(prefix, localName, namespaceUri, doc)
        {
            Line = line;
            Column = column;
        }
    }

    /// <summary>
    /// An <see cref="XmlComment"/> that remembers where it was, and whether it opens its own line — the
    /// test that tells a comment written above an element from one trailing the markup before it
    /// (spec 2026-09-30-i18n-xml-comments §3.1).
    /// </summary>
    internal sealed class LineInfoComment : XmlComment
    {
        public int Line { get; }

        /// <summary>As the reader reports it: the first character after <c>&lt;!--</c>.</summary>
        public int Column { get; }

        /// <summary>Only whitespace, or complete comments, precede its <c>&lt;!--</c> on that line.</summary>
        public bool StartsLine { get; }

        internal LineInfoComment(string data, XmlDocument doc, int line, int column, bool startsLine)
            : base(data, doc)
        {
            Line = line;
            Column = column;
            StartsLine = startsLine;
        }
    }

    /// <summary>
    /// Gives the parser source positions without rewriting it against a different XML API.
    ///
    /// <para><see cref="XmlDocument"/> nodes carry no line information, and <c>LoadXml(string)</c>
    /// throws the reader away. But <c>Load(XmlReader)</c> calls <see cref="CreateElement"/> while the
    /// reader is still sitting on the element being created — so an override can read the position
    /// off it and hand back an element that keeps it. The alternative was porting ~700 lines of
    /// <c>XmlElement</c> code to <c>XDocument</c> for the same information.</para>
    ///
    /// <para>Positions reach lint findings through <c>ElementNode.Line</c>, which survives expansion
    /// the same way <c>OriginSrc</c> does.</para>
    ///
    /// <para><see cref="CreateComment"/> gets the same treatment, plus a look at the source line to
    /// decide <see cref="LineInfoComment.StartsLine"/>. The line table is built on the first comment, so
    /// comment-free sources — every TextAsset at runtime, stripped on import — pay nothing.</para>
    /// </summary>
    internal sealed class LineInfoXmlDocument : XmlDocument
    {
        private IXmlLineInfo _lineInfo;
        private string _source;
        private List<int> _lineStarts;

        /// <summary>
        /// Whether the source had any comment at all. Collecting leading comments walks
        /// <c>PreviousSibling</c>, which is O(siblings) on <see cref="XmlLinkedNode"/>; this lets the
        /// parser skip that for the common, comment-free case.
        /// </summary>
        internal bool HasComments { get; private set; }

        public static XmlDocument Parse(string xml)
        {
            var doc = new LineInfoXmlDocument();
            using var reader = XmlReader.Create(new StringReader(xml));
            doc._lineInfo = reader as IXmlLineInfo;
            doc._source = xml;
            doc.Load(reader);
            doc._lineInfo = null;   // stale after the load; later CreateElement calls get 0
            doc._source = null;
            doc._lineStarts = null;
            return doc;
        }

        public override XmlComment CreateComment(string data)
        {
            var line = 0;
            var column = 0;
            if (_lineInfo != null && _lineInfo.HasLineInfo())
            {
                line = _lineInfo.LineNumber;
                column = _lineInfo.LinePosition;
            }
            // Only comments read from the source are author comments; one built later has no place.
            var fromSource = _source != null && line > 0;
            if (fromSource) HasComments = true;
            return new LineInfoComment(data, this, line, column, fromSource && StartsLine(line, column));
        }

        private bool StartsLine(int line, int column)
        {
            _lineStarts ??= LineStarts(_source);
            if (line > _lineStarts.Count) return false;
            var start = _lineStarts[line - 1];
            var end = line < _lineStarts.Count ? _lineStarts[line] : _source.Length;

            // The reader places a comment on the first character after "<!--" (measured on Unity's
            // Mono), so "<!--" should start 4 characters earlier; search backwards should another
            // reader count differently.
            var open = -1;
            for (var i = System.Math.Min(start + column - 1, end - 4); i >= start; i--)
            {
                if (string.CompareOrdinal(_source, i, "<!--", 0, 4) == 0)
                {
                    open = i;
                    break;
                }
            }
            return open >= 0 && OnlyWhitespaceAndComments(_source, start, open);
        }

        /// <summary>
        /// <c>[from, to)</c> holds nothing but whitespace and complete comments — so the second of
        /// <c>&lt;!-- a --&gt; &lt;!-- b --&gt;</c> on one line still opens its line.
        /// </summary>
        private static bool OnlyWhitespaceAndComments(string s, int from, int to)
        {
            var i = from;
            while (i < to)
            {
                if (char.IsWhiteSpace(s[i]))
                {
                    i++;
                    continue;
                }
                if (i + 4 > to || string.CompareOrdinal(s, i, "<!--", 0, 4) != 0) return false;
                var close = s.IndexOf("-->", i + 4, System.StringComparison.Ordinal);
                if (close < 0 || close + 3 > to) return false;
                i = close + 3;
            }
            return true;
        }

        /// <summary>Offsets where each line begins, breaking on <c>\r\n</c>, <c>\r</c> and <c>\n</c> as XML does.</summary>
        private static List<int> LineStarts(string s)
        {
            var starts = new List<int> { 0 };
            for (var i = 0; i < s.Length; i++)
            {
                var c = s[i];
                if (c == '\r')
                {
                    if (i + 1 < s.Length && s[i + 1] == '\n') i++;
                    starts.Add(i + 1);
                }
                else if (c == '\n')
                {
                    starts.Add(i + 1);
                }
            }
            return starts;
        }

        public override XmlElement CreateElement(string prefix, string localName, string namespaceUri)
        {
            var line = 0;
            var column = 0;
            if (_lineInfo != null && _lineInfo.HasLineInfo())
            {
                line = _lineInfo.LineNumber;
                column = _lineInfo.LinePosition;
            }
            return new LineInfoElement(prefix, localName, namespaceUri, this, line, column);
        }
    }
}
