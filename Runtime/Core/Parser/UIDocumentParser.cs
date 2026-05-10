using System.Xml;
using PromptUGUI.IR;

namespace PromptUGUI.Parser
{
    public static class UIDocumentParser
    {
        public static UIDocument Parse(string xml)
        {
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

            foreach (XmlNode child in root.ChildNodes)
            {
                if (child is not XmlElement el) continue;
                switch (el.Name)
                {
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

        private static void ParseImport(XmlElement el, UIDocument doc)
        {
            var src = el.GetAttribute("src");
            if (string.IsNullOrEmpty(src))
                throw new ParseException("<Import> requires src attribute");

            foreach (var existing in doc.Imports)
            {
                if (existing.Src == src)
                    throw new ParseException(
                        $"<Import>: duplicate src='{src}' in same file");
            }

            var ns = el.HasAttribute("as") ? el.GetAttribute("as") : null;
            if (ns != null)
            {
                if (string.IsNullOrEmpty(ns))
                    throw new ParseException(
                        $"<Import src='{src}'>: as attribute cannot be empty");
                if (ns.Contains('.'))
                    throw new ParseException(
                        $"<Import src='{src}'>: as='{ns}' must not contain '.'");
            }

            doc.Imports.Add(new IR.ImportRef(src, ns));
        }

        private static void ParseScreen(XmlElement el, UIDocument doc,
                                System.Collections.Generic.HashSet<string> screenNames)
        {
            var name = el.GetAttribute("name");
            if (string.IsNullOrEmpty(name))
                throw new ParseException("<Screen> requires name attribute");
            if (!screenNames.Add(name))
                throw new ParseException($"Duplicate <Screen name='{name}'>");

            var idsInScreen = new System.Collections.Generic.HashSet<string>();
            var rootNode = new ElementNode("__screen_root__");
            var screen = new ScreenDef(name, rootNode);

            var canvasAttr = el.GetAttribute("canvas");
            if (!string.IsNullOrEmpty(canvasAttr))
            {
                screen.CanvasMode = canvasAttr switch
                {
                    "overlay" => CanvasMode.Overlay,
                    "camera" => CanvasMode.Camera,
                    "world" => CanvasMode.World,
                    _ => throw new ParseException(
                        $"<Screen name='{name}'>: invalid canvas='{canvasAttr}' " +
                        $"(expected 'overlay', 'camera', or 'world')"),
                };
            }

            var seenWhen = new System.Collections.Generic.HashSet<string>();

            foreach (XmlNode c in el.ChildNodes)
            {
                if (c is not XmlElement child_el) continue;
                if (child_el.Name == "Import")
                    throw new ParseException(
                        $"<Screen name='{name}'>: <Import> only allowed as top-level element");
                if (child_el.Name == "Variant")
                {
                    var when = child_el.GetAttribute("when").Trim();
                    if (!string.IsNullOrEmpty(when) && !seenWhen.Add(when))
                        throw new ParseException(
                            $"<Screen name='{name}'>: duplicate <Variant when='{when}'>");
                    ParseVariantBlock(child_el, screen, idsInScreen);
                }
                else
                {
                    rootNode.Children.Add(ParseElement(child_el, idsInScreen));
                }
            }
            doc.Screens.Add(screen);
        }

        private static void ParseTemplate(XmlElement el, UIDocument doc)
        {
            var name = el.GetAttribute("name");
            if (string.IsNullOrEmpty(name))
                throw new ParseException("<Template> requires name attribute");
            if (doc.Templates.ContainsKey(name))
                throw new ParseException($"Duplicate <Template name='{name}'>");

            var tpl = new TemplateDef(name);
            var paramNames = new System.Collections.Generic.HashSet<string>();
            var sawBody = false;
            ElementNode body = null;

            foreach (XmlNode c in el.ChildNodes)
            {
                if (c is not XmlElement ce) continue;
                if (ce.Name == "Import")
                    throw new ParseException(
                        $"<Template name='{name}'>: <Import> only allowed as top-level element");
                if (ce.Name == "Param")
                {
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

                    foreach (XmlAttribute pa in ce.Attributes)
                    {
                        if (pa.Name == "name" || pa.Name == "default") continue;
                        if (pa.Name.StartsWith("default.") || pa.Name.StartsWith("name."))
                            throw new ParseException(
                                $"<Param name='{pname}'>: '{pa.Name}' cannot carry .variant suffix");
                        // 其他属性 M2 行为是隐式忽略，M3 维持
                    }

                    var def = ce.HasAttribute("default") ? ce.GetAttribute("default") : null;
                    tpl.Params.Add(new ParamDef(pname, def));
                }
                else
                {
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

        private static void ParseVariantBlock(XmlElement el, ScreenDef screen,
                                      System.Collections.Generic.HashSet<string> idsInScreen)
        {
            var when = el.GetAttribute("when").Trim();
            if (string.IsNullOrEmpty(when))
                throw new ParseException("<Variant> requires 'when' attribute");

            var block = new VariantBlock(when);

            foreach (XmlNode c in el.ChildNodes)
            {
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

        private static ElementNode ParseElement(XmlElement el,
                                        System.Collections.Generic.HashSet<string> idsInScope)
        {
            string ns = null;
            var tag = el.Name;
            var dot = tag.IndexOf('.');
            if (dot >= 0)
            {
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

            foreach (XmlAttribute attr in el.Attributes)
            {
                if (attr.Name == "id")
                {
                    if (!idsInScope.Add(attr.Value))
                        throw new ParseException(
                            $"Duplicate id='{attr.Value}' within scope");
                    node.Id = attr.Value;
                    continue;
                }

                var attrDot = attr.Name.IndexOf('.');
                if (attrDot < 0)
                {
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

                if (!node.VariantOverrides.TryGetValue(baseName, out var list))
                {
                    list = new System.Collections.Generic.List<(string, string)>();
                    node.VariantOverrides[baseName] = list;
                }
                list.Add((variant, attr.Value));
            }

            // Capture raw attribute values for attrs containing {{...}} (for runtime re-substitution on translated msgstr).
            foreach (var kv in node.Attributes)
            {
                if (kv.Value != null && kv.Value.Contains("{{"))
                    node.AttributesRaw[kv.Key] = kv.Value;
            }

            // 文本简写
            bool hasElement = false, hasText = false;
            foreach (XmlNode c in el.ChildNodes)
            {
                if (c is XmlElement) hasElement = true;
                else if (c is XmlText txt && !string.IsNullOrWhiteSpace(txt.Value)) hasText = true;
                else if (c is XmlCDataSection cdata && !string.IsNullOrWhiteSpace(cdata.Value)) hasText = true;
            }
            if (hasText && hasElement)
                throw new ParseException(
                    $"<{el.Name}> mixes text and child elements; not allowed");
            if (hasText && !hasElement)
            {
                node.TextContent = el.InnerText.Trim();
                node.TextContentRaw = el.InnerText;     // un-trimmed raw — preserves intentional whitespace inside CDATA
            }

            foreach (XmlNode c in el.ChildNodes)
                if (c is XmlElement child_el)
                    node.Children.Add(ParseElement(child_el, idsInScope));

            // <Icon> 校验：name 必填、必须匹配 ns:icon 形式（含 Variant 覆盖）。
            // Template Param 占位符 (`{{x}}`) 在 TemplateExpander 之后才替换；parse
            // 阶段还看不到最终值，跳过格式校验（IconAtlasSyncer 同样把 '{{' 视作 dynamic）。
            if (tag == "Icon" && ns == null)
            {
                if (!node.Attributes.TryGetValue("name", out var iconName) || string.IsNullOrEmpty(iconName))
                    throw new ParseException("Icon: 'name' is required");
                if (!iconName.Contains("{{") && !IsValidIconName(iconName))
                    throw new ParseException(
                        $"Icon: 'name' must be 'set:icon' (got '{iconName}')");
                if (node.VariantOverrides.TryGetValue("name", out var nameOverrides))
                {
                    foreach (var (variant, value) in nameOverrides)
                    {
                        if (string.IsNullOrEmpty(value))
                            throw new ParseException(
                                $"Icon: name.{variant} must be 'set:icon' (got '{value}')");
                        if (!value.Contains("{{") && !IsValidIconName(value))
                            throw new ParseException(
                                $"Icon: name.{variant} must be 'set:icon' (got '{value}')");
                    }
                }
            }

            // size/width/height == "native" 仅 <Icon> 允许（含 Variant 覆盖）
            if (!(tag == "Icon" && ns == null))
            {
                foreach (var key in new[] { "size", "width", "height" })
                {
                    if (node.Attributes.TryGetValue(key, out var v) && v == "native")
                        throw new ParseException(
                            $"<{tag}>: native size only allowed on <Icon> (attribute '{key}')");
                    if (node.VariantOverrides.TryGetValue(key, out var keyOverrides))
                    {
                        foreach (var (variant, value) in keyOverrides)
                        {
                            if (value == "native")
                                throw new ParseException(
                                    $"<{tag}>: native size only allowed on <Icon> (attribute '{key}.{variant}')");
                        }
                    }
                }
            }

            return node;
        }

        private static bool IsValidIconName(string name)
        {
            var colon = name.IndexOf(':');
            if (colon <= 0 || colon == name.Length - 1) return false;
            for (var i = 0; i < name.Length; i++)
            {
                if (i == colon) continue;
                var c = name[i];
                if (i < colon)
                {
                    // Set name is a reference key matching IconSet.setName — strict.
                    var alnum = (c >= 'a' && c <= 'z')
                                || (c >= 'A' && c <= 'Z')
                                || (c >= '0' && c <= '9')
                                || c == '-' || c == '_';
                    if (!alnum) return false;
                }
                else
                {
                    // Icon-name half mirrors the filesystem path (sans extension):
                    // '/'-separated, may contain spaces, '&', parens, commas, etc.
                    // Only forbid the ':' delimiter (a second one is ambiguous) and
                    // raw control chars.
                    if (c == ':' || char.IsControl(c)) return false;
                }
            }
            return true;
        }
    }
}
