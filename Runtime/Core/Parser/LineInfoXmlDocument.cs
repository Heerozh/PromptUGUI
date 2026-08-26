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
    /// </summary>
    internal sealed class LineInfoXmlDocument : XmlDocument
    {
        private IXmlLineInfo _lineInfo;

        public static XmlDocument Parse(string xml)
        {
            var doc = new LineInfoXmlDocument();
            using var reader = XmlReader.Create(new StringReader(xml));
            doc._lineInfo = reader as IXmlLineInfo;
            doc.Load(reader);
            doc._lineInfo = null;   // stale after the load; later CreateElement calls get 0
            return doc;
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
