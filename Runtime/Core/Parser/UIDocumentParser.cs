using System.Text.RegularExpressions;
using System.Xml;
using PromptUGUI.IR;

namespace PromptUGUI.Parser
{
    public static class UIDocumentParser
    {
        private static readonly Regex KebabRx =
            new Regex("^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.Compiled);

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
                    case "Theme":
                        var theme = ParseTheme(el);
                        foreach (var existing in doc.Themes)
                        {
                            if (existing.Name == theme.Name)
                                throw new ParseException(
                                    $"duplicate <Theme name=\"{theme.Name}\"> within document");
                        }
                        doc.Themes.Add(theme);
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

        private static ThemeBlock ParseTheme(XmlElement el)
        {
            var name = el.GetAttribute("name");
            if (string.IsNullOrEmpty(name))
                throw new ParseException("<Theme>: missing required attribute 'name'");
            var block = new ThemeBlock
            {
                Name = name,
                BaseName = el.HasAttribute("base") ? el.GetAttribute("base") : null
            };
            var seen = new System.Collections.Generic.HashSet<string>();
            foreach (XmlNode c in el.ChildNodes)
            {
                if (c is not XmlElement child) continue;
                if (child.Name != "Color")
                    throw new ParseException(
                        $"<Theme name=\"{name}\">: unexpected child <{child.Name}> " +
                        $"(only <Color> allowed)");
                var cn = child.GetAttribute("name");
                var hasValue = child.HasAttribute("value");
                var cv = child.GetAttribute("value");
                if (string.IsNullOrEmpty(cn))
                    throw new ParseException($"<Color value=\"{cv}\">: missing required attribute 'name'");
                if (!hasValue)
                    throw new ParseException($"<Color name=\"{cn}\">: missing required attribute 'value'");
                if (!KebabRx.IsMatch(cn))
                    throw new ParseException(
                        $"<Color name=\"{cn}\">: token name must be kebab-case [a-z0-9-]");
                if (!ColorParser.TryParseHtmlString(cv))
                    throw new ParseException(
                        $"<Color name=\"{cn}\" value=\"{cv}\">: invalid color literal");
                if (!seen.Add(cn))
                    throw new ParseException(
                        $"<Theme name=\"{name}\"> declares '{cn}' twice");
                block.Colors.Add(new ColorEntry { Name = cn, Value = cv });
            }
            return block;
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

            // <Screen reference="WxH"> stored on rootNode.Attributes so VariantResolver
            // can pick base + .variant overrides uniformly at runtime.
            if (el.HasAttribute("reference"))
            {
                var referenceAttr = el.GetAttribute("reference");
                if (!string.IsNullOrEmpty(referenceAttr))
                    ReferenceSyntax.Parse(
                        referenceAttr, $"<Screen name='{name}' reference>");
                rootNode.Attributes["reference"] = referenceAttr;
            }

            // <Screen reference.<variant>="..."> — same shape as ElementNode VariantOverrides.
            foreach (System.Xml.XmlAttribute a in el.Attributes)
            {
                if (!a.Name.StartsWith("reference.")) continue;
                var variant = a.Name.Substring("reference.".Length);
                if (string.IsNullOrEmpty(variant) || variant.Contains("."))
                    throw new ParseException(
                        $"<Screen name='{name}'>: malformed attribute '{a.Name}' " +
                        $"(variant suffix must be 'reference.variant' with no further dots)");
                if (!string.IsNullOrEmpty(a.Value))
                    ReferenceSyntax.Parse(
                        a.Value, $"<Screen name='{name}' {a.Name}>");
                if (!rootNode.VariantOverrides.TryGetValue("reference", out var list))
                {
                    list = new System.Collections.Generic.List<(string, string)>();
                    rootNode.VariantOverrides["reference"] = list;
                }
                list.Add((variant, a.Value));
            }

            // <Screen scale-mode="auto|pixel"> — parse-time validation only checks the
            // enum value. "Pixel requires reference=" is enforced at runtime instead,
            // because variant + DefaultScaleMode combinations can't be resolved here.
            if (el.HasAttribute("scale-mode"))
            {
                var scaleAttr = el.GetAttribute("scale-mode");
                ValidateScaleMode(scaleAttr, $"<Screen name='{name}' scale-mode>");
                rootNode.Attributes["scale-mode"] = scaleAttr;
            }

            // <Screen scale-mode.<variant>="..."> — same shape as ElementNode VariantOverrides.
            foreach (System.Xml.XmlAttribute a in el.Attributes)
            {
                if (!a.Name.StartsWith("scale-mode.")) continue;
                var variant = a.Name.Substring("scale-mode.".Length);
                if (string.IsNullOrEmpty(variant) || variant.Contains("."))
                    throw new ParseException(
                        $"<Screen name='{name}'>: malformed attribute '{a.Name}' " +
                        $"(variant suffix must be 'scale-mode.variant' with no further dots)");
                ValidateScaleMode(a.Value, $"<Screen name='{name}' {a.Name}>");
                if (!rootNode.VariantOverrides.TryGetValue("scale-mode", out var list))
                {
                    list = new System.Collections.Generic.List<(string, string)>();
                    rootNode.VariantOverrides["scale-mode"] = list;
                }
                list.Add((variant, a.Value));
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
                // Trim both: XML preserves formatting whitespace by default, but for
                // UI text content the leading/trailing whitespace from pretty-printed
                // source files (e.g. <Text>\n  {{label}}\n</Text>) is never wanted —
                // it would otherwise leak into TrResolver's lookup key and the final
                // rendered string (since runtime uses TextContentRaw as the format).
                node.TextContent = el.InnerText.Trim();
                node.TextContentRaw = el.InnerText.Trim();
            }

            foreach (XmlNode c in el.ChildNodes)
                if (c is XmlElement child_el)
                    node.Children.Add(ParseElement(child_el, idsInScope));

            // <Icon> 校验：name 必填、必须匹配 ns:icon 形式（含 Variant 覆盖）。
            // Template Param 占位符 (`{{x}}`) 在 TemplateExpander 之后才替换；parse
            // 阶段还看不到最终值，跳过格式校验（SpriteAtlasSyncer 同样把 '{{' 视作 dynamic）。
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

            // <SafeArea> 校验：仍禁止形状类 layout 属性（anchor/size/width/height/pivot），
            // 几何固定为"stretch + per-edge max(margin, deviceInset)"。
            // `margin` 在 v2 (2026-05-26-safearea-margin-absorb-v2) 已解禁 —— 它是 SafeArea
            // 自身的设计 margin，跟 device safe-area inset 取大。
            if (tag == "SafeArea" && ns == null)
            {
                foreach (var key in new[] { "anchor", "size", "width", "height", "pivot" })
                {
                    if (node.Attributes.ContainsKey(key))
                        throw new ParseException(
                            $"<SafeArea> does not accept attribute '{key}'; " +
                            $"SafeArea is always stretched to its parent. " +
                            $"Use <SafeArea margin=\"...\"> for inset (absorbed by device safe area).");
                    if (node.VariantOverrides.ContainsKey(key))
                        throw new ParseException(
                            $"<SafeArea> does not accept variant override for '{key}'; " +
                            $"SafeArea is always stretched to its parent.");
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

            // scale: positive float, applied at runtime as RectTransform.localScale.
            // Validate at parse so authors get errors at load, not silent runtime no-ops.
            // <Animation> is exempt — its `scale` is a 'from:to' keyframe spec parsed by
            // AnimationSpec.ParseScaleFromTo at runtime, not a static localScale value.
            if (!(tag == "Animation" && ns == null))
            {
                var nodeContext = $"<{tag}{(string.IsNullOrEmpty(node.Id) ? "" : $" id='{node.Id}'")}>";
                if (node.Attributes.TryGetValue("scale", out var psValue))
                    ValidateScale(psValue, $"{nodeContext} scale");
                if (node.VariantOverrides.TryGetValue("scale", out var psVariants))
                {
                    foreach (var (variant, value) in psVariants)
                        ValidateScale(value, $"{nodeContext} scale.{variant}");
                }
            }

            return node;
        }

        private static void ValidateScaleMode(string raw, string contextLabel)
        {
            // Empty string means "inherit UI.DefaultScaleMode"; runtime semantics decide.
            if (string.IsNullOrEmpty(raw)) return;
            if (raw == "auto" || raw == "pixel") return;
            throw new ParseException(
                $"{contextLabel}: invalid value '{raw}' (expected 'auto' or 'pixel')");
        }

        private static void ValidateScale(string raw, string contextLabel)
        {
            // scale="N" sets RectTransform.localScale = N (relative to layout box; works in
            // any scale-mode). Must be a positive float. N=1 is the no-op identity.
            // scale="Nx" (N positive integer) is the device-density form: localScale =
            // N / canvasFactor at runtime — locks the element to N physical pixels per
            // design-unit. See 2026-05-31-scale-device-density-design.md.
            if (string.IsNullOrEmpty(raw))
                throw new ParseException(
                    $"{contextLabel}: value cannot be empty " +
                    $"(expected a positive number like '0.5', or a device-density like '2x')");

            if (raw.Length >= 2 && raw[raw.Length - 1] == 'x')
            {
                var num = raw.Substring(0, raw.Length - 1);
                if (int.TryParse(num, System.Globalization.NumberStyles.None,
                                 System.Globalization.CultureInfo.InvariantCulture, out var n) && n >= 1)
                    return;
                throw new ParseException(
                    $"{contextLabel}: invalid device-density '{raw}' " +
                    $"(expected a positive integer before 'x', e.g. '1x' or '2x')");
            }

            if (!float.TryParse(raw, System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out var v) || v <= 0f)
                throw new ParseException(
                    $"{contextLabel}: invalid value '{raw}' " +
                    $"(expected a positive number like '0.5', or a device-density like '2x')");
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
                    // Set name is a reference key matching SpriteSet.setName — strict.
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
