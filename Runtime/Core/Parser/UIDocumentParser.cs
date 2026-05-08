using System.Xml;
using PromptUGUI.IR;

namespace PromptUGUI.Parser {
    public static class UIDocumentParser {
        public static UIDocument Parse(string xml) {
            var xdoc = new XmlDocument();
            xdoc.LoadXml(xml);

            var root = xdoc.DocumentElement;
            if (root == null || root.Name != "PromptUGUI")
                throw new ParseException("Root element must be <PromptUGUI>");

            var versionAttr = root.GetAttribute("version");
            if (string.IsNullOrEmpty(versionAttr))
                throw new ParseException("<PromptUGUI> requires version attribute");

            var doc = new UIDocument { Version = int.Parse(versionAttr) };
            var screenNames = new System.Collections.Generic.HashSet<string>();

            foreach (XmlNode child in root.ChildNodes) {
                if (child is not XmlElement el) continue;
                switch (el.Name) {
                    case "Screen":
                        ParseScreen(el, doc, screenNames);
                        break;
                    case "Template":
                        ParseTemplate(el, doc);
                        break;
                    case "Import":
                        ParseImport(el, doc);
                        break;
                    default:
                        throw new ParseException(
                            $"unexpected top-level element <{el.Name}>");
                }
            }

            return doc;
        }

        static void ParseImport(XmlElement el, UIDocument doc) {
            var src = el.GetAttribute("src");
            if (string.IsNullOrEmpty(src))
                throw new ParseException("<Import> requires src attribute");

            foreach (var existing in doc.Imports) {
                if (existing.Src == src)
                    throw new ParseException(
                        $"<Import>: duplicate src='{src}' in same file");
            }

            var ns = el.HasAttribute("as") ? el.GetAttribute("as") : null;
            if (ns != null && string.IsNullOrEmpty(ns))
                throw new ParseException(
                    $"<Import src='{src}'>: as attribute cannot be empty");

            doc.Imports.Add(new IR.ImportRef(src, ns));
        }

        static void ParseScreen(XmlElement el, UIDocument doc,
                                System.Collections.Generic.HashSet<string> screenNames) {
            var name = el.GetAttribute("name");
            if (string.IsNullOrEmpty(name))
                throw new ParseException("<Screen> requires name attribute");
            if (!screenNames.Add(name))
                throw new ParseException($"Duplicate <Screen name='{name}'>");

            var idsInScreen = new System.Collections.Generic.HashSet<string>();
            var rootNode = new ElementNode("__screen_root__");
            var screen = new ScreenDef(name, rootNode);
            var seenWhen = new System.Collections.Generic.HashSet<string>();

            foreach (XmlNode c in el.ChildNodes) {
                if (c is not XmlElement child_el) continue;
                if (child_el.Name == "Import")
                    throw new ParseException(
                        $"<Screen name='{name}'>: <Import> only allowed as top-level element");
                if (child_el.Name == "Variant") {
                    var when = child_el.GetAttribute("when").Trim();
                    if (!string.IsNullOrEmpty(when) && !seenWhen.Add(when))
                        throw new ParseException(
                            $"<Screen name='{name}'>: duplicate <Variant when='{when}'>");
                    ParseVariantBlock(child_el, screen, idsInScreen);
                } else {
                    rootNode.Children.Add(ParseElement(child_el, idsInScreen));
                }
            }
            doc.Screens.Add(screen);
        }

        static void ParseTemplate(XmlElement el, UIDocument doc) {
            var name = el.GetAttribute("name");
            if (string.IsNullOrEmpty(name))
                throw new ParseException("<Template> requires name attribute");
            if (doc.Templates.ContainsKey(name))
                throw new ParseException($"Duplicate <Template name='{name}'>");

            var tpl = new TemplateDef(name);
            var paramNames = new System.Collections.Generic.HashSet<string>();
            bool sawBody = false;
            ElementNode body = null;

            foreach (XmlNode c in el.ChildNodes) {
                if (c is not XmlElement ce) continue;
                if (ce.Name == "Import")
                    throw new ParseException(
                        $"<Template name='{name}'>: <Import> only allowed as top-level element");
                if (ce.Name == "Param") {
                    if (sawBody)
                        throw new ParseException(
                            $"<Template name='{name}'>: <Param> must appear before any body element");
                    var pname = ce.GetAttribute("name");
                    if (string.IsNullOrEmpty(pname))
                        throw new ParseException(
                            $"<Template name='{name}'>: <Param> requires name attribute");
                    if (!paramNames.Add(pname))
                        throw new ParseException(
                            $"<Template name='{name}'>: duplicate <Param name='{pname}'>");

                    foreach (XmlAttribute pa in ce.Attributes) {
                        if (pa.Name == "name" || pa.Name == "default") continue;
                        if (pa.Name.StartsWith("default.") || pa.Name.StartsWith("name."))
                            throw new ParseException(
                                $"<Param name='{pname}'>: '{pa.Name}' cannot carry .variant suffix");
                        // 其他属性 M2 行为是隐式忽略，M3 维持
                    }

                    string def = ce.HasAttribute("default") ? ce.GetAttribute("default") : null;
                    tpl.Params.Add(new ParamDef(pname, def));
                } else {
                    if (sawBody)
                        throw new ParseException(
                            $"<Template name='{name}'> must have exactly one root element");
                    sawBody = true;
                    var tplIds = new System.Collections.Generic.HashSet<string>();
                    body = ParseElement(ce, tplIds);
                }
            }
            if (!sawBody)
                throw new ParseException(
                    $"<Template name='{name}'> must have one root element after <Param>s");

            tpl.Body = body;
            doc.Templates[name] = tpl;
        }

        static void ParseVariantBlock(XmlElement el, ScreenDef screen,
                                      System.Collections.Generic.HashSet<string> idsInScreen) {
            var when = el.GetAttribute("when").Trim();
            if (string.IsNullOrEmpty(when))
                throw new ParseException("<Variant> requires 'when' attribute");

            var block = new VariantBlock(when);

            foreach (XmlNode c in el.ChildNodes) {
                if (c is not XmlElement ce) continue;
                if (ce.Name != "Add")
                    throw new ParseException(
                        $"<Variant when='{when}'>: only <Add> elements allowed (got <{ce.Name}>)");

                var add = new AddDirective();
                var into = ce.GetAttribute("into");
                if (string.IsNullOrEmpty(into))
                    throw new ParseException(
                        $"<Add> inside <Variant when='{when}'>: 'into' attribute is required");
                add.IntoPath = into;
                if (ce.HasAttribute("at")) add.At = ce.GetAttribute("at");

                foreach (XmlNode ac in ce.ChildNodes)
                    if (ac is XmlElement ace)
                        add.Children.Add(ParseElement(ace, idsInScreen));

                if (add.Children.Count == 0)
                    throw new ParseException(
                        $"<Add into='{into}'> inside <Variant when='{when}'>: must contain at least one child element");

                block.Adds.Add(add);
            }

            if (block.Adds.Count == 0)
                throw new ParseException(
                    $"<Variant when='{when}'>: must contain at least one <Add>");

            screen.Variants.Add(block);
        }

        static ElementNode ParseElement(XmlElement el,
                                        System.Collections.Generic.HashSet<string> idsInScope) {
            string ns = null;
            string tag = el.Name;
            int dot = tag.IndexOf('.');
            if (dot >= 0) {
                if (dot == 0 || dot == tag.Length - 1)
                    throw new ParseException(
                        $"malformed namespaced tag '{tag}'");
                if (tag.IndexOf('.', dot + 1) >= 0)
                    throw new ParseException(
                        $"tag '{tag}' has multiple dots; namespace tags must be 'ns.Name' (one dot)");
                ns = tag.Substring(0, dot);
                tag = tag.Substring(dot + 1);
            }
            var node = new ElementNode(tag, ns);

            foreach (XmlAttribute attr in el.Attributes) {
                if (attr.Name == "id") {
                    if (!idsInScope.Add(attr.Value))
                        throw new ParseException(
                            $"Duplicate id='{attr.Value}' within scope");
                    node.Id = attr.Value;
                    continue;
                }

                int attrDot = attr.Name.IndexOf('.');
                if (attrDot < 0) {
                    node.Attributes[attr.Name] = attr.Value;
                    continue;
                }

                if (attrDot == 0 || attrDot == attr.Name.Length - 1)
                    throw new ParseException(
                        $"<{el.Name}>: malformed attribute '{attr.Name}' (variant suffix must be 'name.variant')");

                var baseName = attr.Name.Substring(0, attrDot);
                var variant = attr.Name.Substring(attrDot + 1);

                if (variant.Contains('.'))
                    throw new ParseException(
                        $"<{el.Name}>: attribute '{attr.Name}' has '.' inside variant name " +
                        $"(use '-' for compound names like 'mobile-portrait')");

                if (baseName == "id")
                    throw new ParseException(
                        $"<{el.Name}>: 'id' cannot carry .variant suffix (id='{attr.Value}')");

                if (!node.VariantOverrides.TryGetValue(baseName, out var list)) {
                    list = new System.Collections.Generic.List<(string, string)>();
                    node.VariantOverrides[baseName] = list;
                }
                list.Add((variant, attr.Value));
            }

            // 文本简写
            bool hasElement = false, hasText = false;
            foreach (XmlNode c in el.ChildNodes) {
                if (c is XmlElement) hasElement = true;
                else if (c is XmlText txt && !string.IsNullOrWhiteSpace(txt.Value)) hasText = true;
            }
            if (hasText && hasElement)
                throw new ParseException(
                    $"<{el.Name}> mixes text and child elements; not allowed");
            if (hasText && !hasElement)
                node.TextContent = el.InnerText.Trim();

            foreach (XmlNode c in el.ChildNodes)
                if (c is XmlElement child_el)
                    node.Children.Add(ParseElement(child_el, idsInScope));

            return node;
        }
    }
}
