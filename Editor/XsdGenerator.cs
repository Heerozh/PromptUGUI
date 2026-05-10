using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Xml;
using PromptUGUI.Parser;
using PromptUGUI.Registry;
using UnityEditor;

namespace PromptUGUI.Editor
{
    public static class XsdGenerator
    {
        /// <summary>Collect all `<Template name="...">` bare names from the given
        /// .ui.xml file paths. Unparseable files are silently skipped (regen runs
        /// on every save and partial edits should not throw).</summary>
        public static HashSet<string> ScanTemplates(IEnumerable<string> filePaths)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var path in filePaths)
            {
                string text;
                try { text = File.ReadAllText(path); }
                catch (IOException) { continue; }
                IR.UIDocument doc;
                // ParseException = semantic, XmlException = malformed XML.
                // Both can occur during in-progress edits; skip silently.
                try { doc = UIDocumentParser.Parse(text); }
                catch (ParseException) { continue; }
                catch (XmlException) { continue; }
                foreach (var key in doc.Templates.Keys) names.Add(key);
            }
            return names;
        }

        /// <summary>Scan all *.ui.xml under Assets/ for Template definitions.
        /// Editor-only; uses AssetDatabase.</summary>
        public static HashSet<string> ScanProjectTemplates()
        {
            var paths = AssetDatabase.FindAssets("t:TextAsset")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => p.EndsWith(".ui.xml", StringComparison.Ordinal));
            return ScanTemplates(paths);
        }

        // Schema is intentionally without targetNamespace: .ui.xml files use bare
        // element names + xsi:noNamespaceSchemaLocation. A targetNamespace would
        // require xmlns="..." on every <PromptUGUI> root and break authoring ergonomics.

        public static string Generate(ControlRegistry registry,
                                      IEnumerable<string> templateTags = null)
        {
            // Write to MemoryStream (not StringBuilder) so XmlWriter emits
            // encoding="utf-8" in the prolog matching the actual file bytes.
            // StringBuilder is UTF-16 internally → declaration would say utf-16.
            var ms = new MemoryStream();
            var settings = new XmlWriterSettings
            {
                Indent = true,
                IndentChars = "  ",
                Encoding = new UTF8Encoding(false),
                OmitXmlDeclaration = false,
            };
            using (var writer = XmlWriter.Create(ms, settings))
            {
                writer.WriteStartDocument();
                writer.WriteStartElement("xs", "schema", "http://www.w3.org/2001/XMLSchema");

                WriteCommonAttrGroup(writer);
                WritePromptUGUIRoot(writer);
                WriteImport(writer);
                WriteScreen(writer);
                WriteTemplate(writer);
                WriteParam(writer);
                WriteSlot(writer);
                WriteVariant(writer);
                WriteAdd(writer);

                // 8 primitives + their attributes
                WriteControl(writer, "Frame", Array.Empty<(string, string, string)>());
                WriteControl(writer, "Image", new[] { ("color", "xs:string", (string)null), ("sprite", "xs:string", (string)null), ("type", "xs:string", (string)null) });
                WriteControl(writer, "Text", new[] { ("align", "xs:string", (string)null), ("color", "xs:string", (string)null), ("fontSize", "xs:int", (string)null), ("text", "xs:string", (string)null), ("wrap", "xs:string", (string)null), ("raycastTarget", "xs:string", (string)null) }, textContent: true);
                WriteControl(writer, "VStack", Array.Empty<(string, string, string)>());
                WriteControl(writer, "HStack", Array.Empty<(string, string, string)>());
                WriteControl(writer, "Grid", new[] { ("columns", "xs:int", (string)null), ("cellSize", "xs:string", (string)null) });
                WriteControl(writer, "Btn", new[] { ("color", "xs:string", (string)null), ("sprite", "xs:string", (string)null) });
                // XSD patterns are implicitly anchored to the entire value — no ^/$.
                // Match runtime parser's check (UIDocumentParser.IsValidIconName):
                // set name stays strict alnum/_-, icon-name half mirrors the filesystem
                // (path-like, '/'-separated; may contain spaces, '&', parens, commas, …).
                // Only the ':' delimiter is forbidden.
                WriteControl(writer, "Icon", new[] { ("name", "xs:string", @"[A-Za-z0-9_\-]+:[^:]+"), ("color", "xs:string", (string)null) });

                // Registered custom controls — exclude primitives, sort by tag
                var primitives = new HashSet<string> {
                    "Frame","Image","Icon","Text","VStack","HStack","Grid","Btn" };
                var customs = registry.All
                    .Where(x => !primitives.Contains(x.Tag))
                    .OrderBy(x => x.Tag, StringComparer.Ordinal)
                    .ToArray();

                foreach (var (tag, entry) in customs)
                {
                    var attrs = ReflectControlAttrs(entry.ControlType);
                    WriteControl(writer, tag, attrs);
                }

                // Template tags from .ui.xml. Skip names already covered by primitives
                // or registered customs (would emit duplicate element declarations).
                // Templates have no schema-known attributes — Params come through
                // commonAttrs' xs:anyAttribute processContents="lax".
                var customTagSet = new HashSet<string>(customs.Select(x => x.Tag));
                var templates = (templateTags ?? Array.Empty<string>())
                    .Where(t => !string.IsNullOrEmpty(t)
                                && !primitives.Contains(t)
                                && !customTagSet.Contains(t))
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(t => t, StringComparer.Ordinal)
                    .ToArray();

                foreach (var tag in templates)
                    WriteControl(writer, tag, Array.Empty<(string, string, string)>());

                WriteControlGroup(writer,
                    customs.Select(x => x.Tag).Concat(templates).ToArray());

                writer.WriteEndElement();
                writer.WriteEndDocument();
            }
            return new UTF8Encoding(false).GetString(ms.ToArray());
        }

        public static void GenerateToFile(
            ControlRegistry registry,
            string assetPath = "Assets/PromptUGUI.gen.xsd",
            IEnumerable<string> templateTags = null)
        {
            // null means "scan project"; pass an explicit empty array to skip scanning.
            var tags = templateTags ?? ScanProjectTemplates();
            var xsd = Generate(registry, tags);
            System.IO.File.WriteAllText(assetPath, xsd, new UTF8Encoding(false));
            AssetDatabase.Refresh();
            UnityEngine.Debug.Log($"[PromptUGUI] XSD generated: {assetPath}");
        }

        // ---- Reflection helpers ----

        private static (string Name, string XsdType, string Pattern)[] ReflectControlAttrs(Type controlType)
        {
            var props = controlType.GetProperties(
                BindingFlags.Public | BindingFlags.Instance);
            var list = new List<(string, string, string)>();
            foreach (var p in props)
            {
                var ui = p.GetCustomAttribute<UIAttrAttribute>();
                if (ui == null || !p.CanWrite) continue;
                var name = ui.Name ?? CamelCase(p.Name);
                var xsdType = MapXsdType(p.PropertyType);
                if (xsdType == null)
                {
                    UnityEngine.Debug.LogWarning(
                        $"[PromptUGUI] XSD: skipping {controlType.Name}.{p.Name} — type {p.PropertyType.Name} not supported");
                    continue;
                }
                list.Add((name, xsdType, ui.Pattern));
            }
            list.Sort((a, b) => string.CompareOrdinal(a.Item1, b.Item1));
            return list.ToArray();
        }

        private static string MapXsdType(Type t)
        {
            if (t == typeof(string)) return "xs:string";
            if (t == typeof(int)) return "xs:int";
            if (t == typeof(float)) return "xs:float";
            if (t == typeof(bool)) return "xs:boolean";
            return null;
        }

        private static string CamelCase(string s) =>
            string.IsNullOrEmpty(s) ? s : char.ToLowerInvariant(s[0]) + s.Substring(1);

        // ---- Static schema fragments ----

        private static void WriteCommonAttrGroup(XmlWriter w)
        {
            w.WriteStartElement("xs", "attributeGroup", null);
            w.WriteAttributeString("name", "commonAttrs");
            string[] commons = {
                "id","anchor","size","width","height","margin","pivot",
                "padding","spacing","hidden","interactable" };
            foreach (var a in commons)
            {
                w.WriteStartElement("xs", "attribute", null);
                w.WriteAttributeString("name", a);
                w.WriteAttributeString("type", "xs:string");
                w.WriteEndElement();
            }
            w.WriteStartElement("xs", "anyAttribute", null);
            w.WriteAttributeString("processContents", "lax");
            w.WriteEndElement();
            w.WriteEndElement();
        }

        private static void WritePromptUGUIRoot(XmlWriter w)
        {
            w.WriteStartElement("xs", "element", null);
            w.WriteAttributeString("name", "PromptUGUI");
            w.WriteStartElement("xs", "complexType", null);
            w.WriteStartElement("xs", "choice", null);
            w.WriteAttributeString("maxOccurs", "unbounded");
            foreach (var name in new[] { "Import", "Screen", "Template" })
            {
                w.WriteStartElement("xs", "element", null);
                w.WriteAttributeString("ref", name);
                w.WriteEndElement();
            }
            w.WriteEndElement();
            w.WriteStartElement("xs", "attribute", null);
            w.WriteAttributeString("name", "version");
            w.WriteAttributeString("use", "required");
            w.WriteAttributeString("type", "xs:string");
            w.WriteEndElement();
            w.WriteEndElement();
            w.WriteEndElement();
        }

        private static void WriteImport(XmlWriter w)
        {
            w.WriteStartElement("xs", "element", null);
            w.WriteAttributeString("name", "Import");
            w.WriteStartElement("xs", "complexType", null);
            foreach (var (n, req) in new[] { ("src", "required"), ("as", "optional") })
            {
                w.WriteStartElement("xs", "attribute", null);
                w.WriteAttributeString("name", n);
                w.WriteAttributeString("use", req);
                w.WriteAttributeString("type", "xs:string");
                w.WriteEndElement();
            }
            w.WriteEndElement();
            w.WriteEndElement();
        }

        private static void WriteScreen(XmlWriter w)
        {
            w.WriteStartElement("xs", "element", null);
            w.WriteAttributeString("name", "Screen");
            w.WriteStartElement("xs", "complexType", null);
            w.WriteStartElement("xs", "choice", null);
            w.WriteAttributeString("maxOccurs", "unbounded");
            w.WriteAttributeString("minOccurs", "0");
            w.WriteStartElement("xs", "group", null);
            w.WriteAttributeString("ref", "controlGroup");
            w.WriteEndElement();
            w.WriteStartElement("xs", "element", null);
            w.WriteAttributeString("ref", "Variant");
            w.WriteEndElement();
            w.WriteEndElement();
            w.WriteStartElement("xs", "attribute", null);
            w.WriteAttributeString("name", "name");
            w.WriteAttributeString("use", "required");
            w.WriteAttributeString("type", "xs:string");
            w.WriteEndElement();

            // canvas="overlay|camera|world", optional, default overlay
            w.WriteStartElement("xs", "attribute", null);
            w.WriteAttributeString("name", "canvas");
            w.WriteAttributeString("use", "optional");
            w.WriteStartElement("xs", "simpleType", null);
            w.WriteStartElement("xs", "restriction", null);
            w.WriteAttributeString("base", "xs:string");
            foreach (var v in new[] { "overlay", "camera", "world" })
            {
                w.WriteStartElement("xs", "enumeration", null);
                w.WriteAttributeString("value", v);
                w.WriteEndElement();
            }
            w.WriteEndElement();
            w.WriteEndElement();
            w.WriteEndElement();

            w.WriteEndElement();
            w.WriteEndElement();
        }

        private static void WriteTemplate(XmlWriter w)
        {
            w.WriteStartElement("xs", "element", null);
            w.WriteAttributeString("name", "Template");
            w.WriteStartElement("xs", "complexType", null);
            w.WriteStartElement("xs", "sequence", null);
            w.WriteStartElement("xs", "element", null);
            w.WriteAttributeString("ref", "Param");
            w.WriteAttributeString("minOccurs", "0");
            w.WriteAttributeString("maxOccurs", "unbounded");
            w.WriteEndElement();
            // Body root: exactly one element from controlGroup. Spec §X: Template body
            // must have exactly one root. Was xs:any namespace="##local", but that
            // overlapped with <Param> and broke Unique Particle Attribution.
            w.WriteStartElement("xs", "group", null);
            w.WriteAttributeString("ref", "controlGroup");
            w.WriteEndElement();
            w.WriteEndElement();
            w.WriteStartElement("xs", "attribute", null);
            w.WriteAttributeString("name", "name");
            w.WriteAttributeString("use", "required");
            w.WriteAttributeString("type", "xs:string");
            w.WriteEndElement();
            w.WriteEndElement();
            w.WriteEndElement();
        }

        private static void WriteParam(XmlWriter w)
        {
            w.WriteStartElement("xs", "element", null);
            w.WriteAttributeString("name", "Param");
            w.WriteStartElement("xs", "complexType", null);
            foreach (var (n, req) in new[] { ("name", "required"), ("default", "optional") })
            {
                w.WriteStartElement("xs", "attribute", null);
                w.WriteAttributeString("name", n);
                w.WriteAttributeString("use", req);
                w.WriteAttributeString("type", "xs:string");
                w.WriteEndElement();
            }
            w.WriteEndElement();
            w.WriteEndElement();
        }

        private static void WriteSlot(XmlWriter w)
        {
            w.WriteStartElement("xs", "element", null);
            w.WriteAttributeString("name", "Slot");
            w.WriteStartElement("xs", "complexType", null);
            w.WriteEndElement();
            w.WriteEndElement();
        }

        private static void WriteVariant(XmlWriter w)
        {
            w.WriteStartElement("xs", "element", null);
            w.WriteAttributeString("name", "Variant");
            w.WriteStartElement("xs", "complexType", null);
            w.WriteStartElement("xs", "sequence", null);
            w.WriteStartElement("xs", "element", null);
            w.WriteAttributeString("ref", "Add");
            w.WriteAttributeString("minOccurs", "1");
            w.WriteAttributeString("maxOccurs", "unbounded");
            w.WriteEndElement();
            w.WriteEndElement();
            w.WriteStartElement("xs", "attribute", null);
            w.WriteAttributeString("name", "when");
            w.WriteAttributeString("use", "required");
            w.WriteAttributeString("type", "xs:string");
            w.WriteEndElement();
            w.WriteEndElement();
            w.WriteEndElement();
        }

        private static void WriteAdd(XmlWriter w)
        {
            w.WriteStartElement("xs", "element", null);
            w.WriteAttributeString("name", "Add");
            w.WriteStartElement("xs", "complexType", null);
            w.WriteStartElement("xs", "group", null);
            w.WriteAttributeString("ref", "controlGroup");
            w.WriteAttributeString("maxOccurs", "unbounded");
            w.WriteEndElement();
            foreach (var (n, req) in new[] { ("into", "required"), ("at", "optional") })
            {
                w.WriteStartElement("xs", "attribute", null);
                w.WriteAttributeString("name", n);
                w.WriteAttributeString("use", req);
                w.WriteAttributeString("type", "xs:string");
                w.WriteEndElement();
            }
            w.WriteEndElement();
            w.WriteEndElement();
        }

        private static void WriteControl(XmlWriter w, string tag,
                                 (string Name, string XsdType, string Pattern)[] attrs,
                                 bool textContent = false)
        {
            w.WriteStartElement("xs", "element", null);
            w.WriteAttributeString("name", tag);
            w.WriteStartElement("xs", "complexType", null);

            if (textContent)
            {
                // simpleContent extension: element body is text (no children allowed),
                // plus the usual attributes. Used for <Text> per spec text shorthand.
                w.WriteStartElement("xs", "simpleContent", null);
                w.WriteStartElement("xs", "extension", null);
                w.WriteAttributeString("base", "xs:string");
                WriteAttributes(w, attrs);
                w.WriteEndElement();
                w.WriteEndElement();
            }
            else
            {
                w.WriteStartElement("xs", "choice", null);
                w.WriteAttributeString("maxOccurs", "unbounded");
                w.WriteAttributeString("minOccurs", "0");
                w.WriteStartElement("xs", "group", null);
                w.WriteAttributeString("ref", "controlGroup");
                w.WriteEndElement();
                w.WriteEndElement();
                WriteAttributes(w, attrs);
            }

            w.WriteEndElement();
            w.WriteEndElement();
        }

        private static void WriteAttributes(XmlWriter w,
                                    (string Name, string XsdType, string Pattern)[] attrs)
        {
            w.WriteStartElement("xs", "attributeGroup", null);
            w.WriteAttributeString("ref", "commonAttrs");
            w.WriteEndElement();
            foreach (var (name, type, pattern) in attrs)
            {
                w.WriteStartElement("xs", "attribute", null);
                w.WriteAttributeString("name", name);
                if (string.IsNullOrEmpty(pattern))
                {
                    w.WriteAttributeString("type", type);
                }
                else
                {
                    w.WriteStartElement("xs", "simpleType", null);
                    w.WriteStartElement("xs", "restriction", null);
                    w.WriteAttributeString("base", type);
                    w.WriteStartElement("xs", "pattern", null);
                    w.WriteAttributeString("value", pattern);
                    w.WriteEndElement();
                    w.WriteEndElement();
                    w.WriteEndElement();
                }
                w.WriteEndElement();
            }
        }

        private static readonly string[] first = new[] {
                "Frame","Image","Icon","Text","VStack","HStack","Grid","Btn","Slot"
            };

        private static void WriteControlGroup(XmlWriter w, string[] customTags)
        {
            w.WriteStartElement("xs", "group", null);
            w.WriteAttributeString("name", "controlGroup");
            w.WriteStartElement("xs", "choice", null);
            var all = first.Concat(customTags).ToArray();
            foreach (var n in all)
            {
                w.WriteStartElement("xs", "element", null);
                w.WriteAttributeString("ref", n);
                w.WriteEndElement();
            }
            // No xs:any wildcard: with no targetNamespace, ##local would overlap
            // with the explicit refs above and trip XSD's Unique Particle Attribution
            // rule. So we enumerate Template tags explicitly via ScanProjectTemplates,
            // which the AssetPostprocessor invokes on every .ui.xml change. Custom C#
            // controls still need a manual "Generate XSD" run after registration.
            w.WriteEndElement();
            w.WriteEndElement();
        }
    }
}
