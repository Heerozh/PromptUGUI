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
                    var rootNode = new ElementNode("__screen_root__");
                    foreach (XmlNode c in el.ChildNodes)
                        if (c is XmlElement child_el)
                            rootNode.Children.Add(ParseElement(child_el));
                    doc.Screens.Add(new ScreenDef(name, rootNode));
                }
            }

            return doc;
        }

        static ElementNode ParseElement(XmlElement el) {
            var node = new ElementNode(el.Name);

            foreach (XmlAttribute attr in el.Attributes) {
                if (attr.Name == "id") node.Id = attr.Value;
                else node.Attributes[attr.Name] = attr.Value;
            }

            // 文本简写：仅当唯一子节点是 text node 时算
            if (el.ChildNodes.Count == 1 && el.FirstChild is XmlText t)
                node.TextContent = t.Value;

            foreach (XmlNode c in el.ChildNodes)
                if (c is XmlElement child_el)
                    node.Children.Add(ParseElement(child_el));

            return node;
        }
    }
}
