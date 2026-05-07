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
            var screenNames = new System.Collections.Generic.HashSet<string>();

            foreach (XmlNode child in root.ChildNodes) {
                if (child is not XmlElement el) continue;
                if (el.Name == "Screen") {
                    var name = el.GetAttribute("name");
                    if (string.IsNullOrEmpty(name))
                        throw new ParseException("<Screen> requires name attribute");
                    if (!screenNames.Add(name))
                        throw new ParseException($"Duplicate <Screen name='{name}'>");

                    var idsInScreen = new System.Collections.Generic.HashSet<string>();
                    var rootNode = new ElementNode("__screen_root__");
                    foreach (XmlNode c in el.ChildNodes)
                        if (c is XmlElement child_el)
                            rootNode.Children.Add(ParseElement(child_el, idsInScreen));
                    doc.Screens.Add(new ScreenDef(name, rootNode));
                }
            }

            return doc;
        }

        static ElementNode ParseElement(XmlElement el,
                                        System.Collections.Generic.HashSet<string> idsInScope) {
            var node = new ElementNode(el.Name);

            foreach (XmlAttribute attr in el.Attributes) {
                if (attr.Name == "id") {
                    if (!idsInScope.Add(attr.Value))
                        throw new ParseException(
                            $"Duplicate id='{attr.Value}' within scope");
                    node.Id = attr.Value;
                } else {
                    node.Attributes[attr.Name] = attr.Value;
                }
            }

            // 文本简写：仅当所有非空白子节点都是 text 时生效
            bool hasElement = false, hasText = false;
            foreach (XmlNode c in el.ChildNodes) {
                if (c is XmlElement) hasElement = true;
                else if (c is XmlText txt && !string.IsNullOrWhiteSpace(txt.Value)) hasText = true;
            }
            if (hasText && hasElement)
                throw new ParseException(
                    $"<{el.Name}> mixes text and child elements; not allowed");
            if (hasText && !hasElement) {
                node.TextContent = el.InnerText.Trim();
            }

            foreach (XmlNode c in el.ChildNodes)
                if (c is XmlElement child_el)
                    node.Children.Add(ParseElement(child_el, idsInScope));

            return node;
        }
    }
}
