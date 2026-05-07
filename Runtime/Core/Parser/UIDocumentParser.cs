using System.Xml;
using PromptUGUI.IR;

namespace PromptUGUI.Parser {
    public static class UIDocumentParser {
        public static UIDocument Parse(string xml) {
            var xdoc = new XmlDocument();
            xdoc.LoadXml(xml);

            var root = xdoc.DocumentElement;
            if (root == null || root.Name != "UI")
                throw new ParseException("Root element must be <UI>");

            var versionAttr = root.GetAttribute("version");
            if (string.IsNullOrEmpty(versionAttr))
                throw new ParseException("<UI> requires version attribute");

            var doc = new UIDocument { Version = int.Parse(versionAttr) };

            foreach (XmlNode child in root.ChildNodes) {
                if (child is not XmlElement el) continue;
                if (el.Name == "Screen") {
                    var name = el.GetAttribute("name");
                    if (string.IsNullOrEmpty(name))
                        throw new ParseException("<Screen> requires name attribute");
                    doc.Screens.Add(new ScreenDef(name, new ElementNode("__screen_root__")));
                }
            }

            return doc;
        }
    }
}
