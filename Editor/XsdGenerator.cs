using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Xml;
using PromptUGUI.Registry;

namespace PromptUGUI.Editor {
    public static class XsdGenerator {
        const string Ns = "https://prompt-ugui/v1";

        public static string Generate(ControlRegistry registry) {
            var sb = new StringBuilder();
            var settings = new XmlWriterSettings {
                Indent = true,
                IndentChars = "  ",
                Encoding = new UTF8Encoding(false),
                OmitXmlDeclaration = false,
            };
            using (var writer = XmlWriter.Create(sb, settings)) {
                writer.WriteStartDocument();
                writer.WriteStartElement("xs", "schema", "http://www.w3.org/2001/XMLSchema");
                writer.WriteAttributeString("targetNamespace", Ns);
                writer.WriteAttributeString("xmlns", Ns);
                writer.WriteAttributeString("elementFormDefault", "qualified");

                WriteCommonAttrGroup(writer);
                WritePromptUGUIRoot(writer);
                WriteImport(writer);
                WriteScreen(writer);
                WriteTemplate(writer);
                WriteParam(writer);
                WriteSlot(writer);
                WriteVariant(writer);
                WriteAdd(writer);

                // 7 primitives + their attributes
                WriteControl(writer, "Frame",  Array.Empty<(string,string)>());
                WriteControl(writer, "Image",  new[] {("color","xs:string"),("sprite","xs:string"),("type","xs:string")});
                WriteControl(writer, "Text",   new[] {("align","xs:string"),("color","xs:string"),("font","xs:string"),("size","xs:string"),("text","xs:string"),("wrap","xs:string")});
                WriteControl(writer, "VStack", Array.Empty<(string,string)>());
                WriteControl(writer, "HStack", Array.Empty<(string,string)>());
                WriteControl(writer, "Grid",   new[] {("columns","xs:int")});
                WriteControl(writer, "Btn",    new[] {("color","xs:string"),("sprite","xs:string"),("text","xs:string")});

                // Registered custom controls — exclude primitives, sort by tag
                var primitives = new HashSet<string> {
                    "Frame","Image","Text","VStack","HStack","Grid","Btn" };
                var customs = registry.All
                    .Where(x => !primitives.Contains(x.Tag))
                    .OrderBy(x => x.Tag, StringComparer.Ordinal)
                    .ToArray();

                foreach (var (tag, entry) in customs) {
                    var attrs = ReflectControlAttrs(entry.ControlType);
                    WriteControl(writer, tag, attrs);
                }

                WriteControlGroup(writer, customs.Select(x => x.Tag).ToArray());

                writer.WriteEndElement();
                writer.WriteEndDocument();
            }
            return sb.ToString();
        }

        public static void GenerateToFile(
            ControlRegistry registry,
            string assetPath = "Assets/PromptUGUI.gen.xsd") {
            var xsd = Generate(registry);
            System.IO.File.WriteAllText(assetPath, xsd, new UTF8Encoding(false));
            UnityEditor.AssetDatabase.Refresh();
            UnityEngine.Debug.Log($"[PromptUGUI] XSD generated: {assetPath}");
        }

        // ---- Reflection helpers ----

        static (string Name, string XsdType)[] ReflectControlAttrs(Type controlType) {
            var props = controlType.GetProperties(
                BindingFlags.Public | BindingFlags.Instance);
            var list = new List<(string, string)>();
            foreach (var p in props) {
                var ui = p.GetCustomAttribute<UIAttrAttribute>();
                if (ui == null || !p.CanWrite) continue;
                var name = ui.Name ?? CamelCase(p.Name);
                var xsdType = MapXsdType(p.PropertyType);
                if (xsdType == null) {
                    UnityEngine.Debug.LogWarning(
                        $"[PromptUGUI] XSD: skipping {controlType.Name}.{p.Name} — type {p.PropertyType.Name} not supported");
                    continue;
                }
                list.Add((name, xsdType));
            }
            list.Sort((a, b) => string.CompareOrdinal(a.Item1, b.Item1));
            return list.ToArray();
        }

        static string MapXsdType(Type t) {
            if (t == typeof(string)) return "xs:string";
            if (t == typeof(int))    return "xs:int";
            if (t == typeof(float))  return "xs:float";
            if (t == typeof(bool))   return "xs:boolean";
            return null;
        }

        static string CamelCase(string s) =>
            string.IsNullOrEmpty(s) ? s : char.ToLowerInvariant(s[0]) + s.Substring(1);

        // ---- Static schema fragments ----

        static void WriteCommonAttrGroup(XmlWriter w) {
            w.WriteStartElement("xs", "attributeGroup", null);
            w.WriteAttributeString("name", "commonAttrs");
            string[] commons = {
                "id","anchor","size","width","height","margin","pivot",
                "padding","spacing","hidden","interactable" };
            foreach (var a in commons) {
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

        static void WritePromptUGUIRoot(XmlWriter w) {
            w.WriteStartElement("xs", "element", null);
            w.WriteAttributeString("name", "PromptUGUI");
            w.WriteStartElement("xs", "complexType", null);
            w.WriteStartElement("xs", "choice", null);
            w.WriteAttributeString("maxOccurs", "unbounded");
            foreach (var name in new[] {"Import","Screen","Template"}) {
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

        static void WriteImport(XmlWriter w) {
            w.WriteStartElement("xs", "element", null);
            w.WriteAttributeString("name", "Import");
            w.WriteStartElement("xs", "complexType", null);
            foreach (var (n, req) in new[] {("src","required"),("as","optional")}) {
                w.WriteStartElement("xs", "attribute", null);
                w.WriteAttributeString("name", n);
                w.WriteAttributeString("use", req);
                w.WriteAttributeString("type", "xs:string");
                w.WriteEndElement();
            }
            w.WriteEndElement();
            w.WriteEndElement();
        }

        static void WriteScreen(XmlWriter w) {
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
            w.WriteEndElement();
            w.WriteEndElement();
        }

        static void WriteTemplate(XmlWriter w) {
            w.WriteStartElement("xs", "element", null);
            w.WriteAttributeString("name", "Template");
            w.WriteStartElement("xs", "complexType", null);
            w.WriteStartElement("xs", "sequence", null);
            w.WriteStartElement("xs", "element", null);
            w.WriteAttributeString("ref", "Param");
            w.WriteAttributeString("minOccurs", "0");
            w.WriteAttributeString("maxOccurs", "unbounded");
            w.WriteEndElement();
            w.WriteStartElement("xs", "any", null);
            w.WriteAttributeString("namespace", "##local");
            w.WriteAttributeString("processContents", "lax");
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

        static void WriteParam(XmlWriter w) {
            w.WriteStartElement("xs", "element", null);
            w.WriteAttributeString("name", "Param");
            w.WriteStartElement("xs", "complexType", null);
            foreach (var (n, req) in new[] {("name","required"),("default","optional")}) {
                w.WriteStartElement("xs", "attribute", null);
                w.WriteAttributeString("name", n);
                w.WriteAttributeString("use", req);
                w.WriteAttributeString("type", "xs:string");
                w.WriteEndElement();
            }
            w.WriteEndElement();
            w.WriteEndElement();
        }

        static void WriteSlot(XmlWriter w) {
            w.WriteStartElement("xs", "element", null);
            w.WriteAttributeString("name", "Slot");
            w.WriteStartElement("xs", "complexType", null);
            w.WriteEndElement();
            w.WriteEndElement();
        }

        static void WriteVariant(XmlWriter w) {
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

        static void WriteAdd(XmlWriter w) {
            w.WriteStartElement("xs", "element", null);
            w.WriteAttributeString("name", "Add");
            w.WriteStartElement("xs", "complexType", null);
            w.WriteStartElement("xs", "group", null);
            w.WriteAttributeString("ref", "controlGroup");
            w.WriteAttributeString("maxOccurs", "unbounded");
            w.WriteEndElement();
            foreach (var (n, req) in new[] {("into","required"),("at","optional")}) {
                w.WriteStartElement("xs", "attribute", null);
                w.WriteAttributeString("name", n);
                w.WriteAttributeString("use", req);
                w.WriteAttributeString("type", "xs:string");
                w.WriteEndElement();
            }
            w.WriteEndElement();
            w.WriteEndElement();
        }

        static void WriteControl(XmlWriter w, string tag,
                                 (string Name, string XsdType)[] attrs) {
            w.WriteStartElement("xs", "element", null);
            w.WriteAttributeString("name", tag);
            w.WriteStartElement("xs", "complexType", null);
            w.WriteStartElement("xs", "choice", null);
            w.WriteAttributeString("maxOccurs", "unbounded");
            w.WriteAttributeString("minOccurs", "0");
            w.WriteStartElement("xs", "group", null);
            w.WriteAttributeString("ref", "controlGroup");
            w.WriteEndElement();
            w.WriteEndElement();
            w.WriteStartElement("xs", "attributeGroup", null);
            w.WriteAttributeString("ref", "commonAttrs");
            w.WriteEndElement();
            foreach (var (name, type) in attrs) {
                w.WriteStartElement("xs", "attribute", null);
                w.WriteAttributeString("name", name);
                w.WriteAttributeString("type", type);
                w.WriteEndElement();
            }
            w.WriteEndElement();
            w.WriteEndElement();
        }

        static void WriteControlGroup(XmlWriter w, string[] customTags) {
            w.WriteStartElement("xs", "group", null);
            w.WriteAttributeString("name", "controlGroup");
            w.WriteStartElement("xs", "choice", null);
            string[] all = new[] {
                "Frame","Image","Text","VStack","HStack","Grid","Btn"
            }.Concat(customTags).ToArray();
            foreach (var n in all) {
                w.WriteStartElement("xs", "element", null);
                w.WriteAttributeString("ref", n);
                w.WriteEndElement();
            }
            // any-element fallback for Template invocations
            w.WriteStartElement("xs", "any", null);
            w.WriteAttributeString("namespace", "##local");
            w.WriteAttributeString("processContents", "lax");
            w.WriteEndElement();
            w.WriteEndElement();
            w.WriteEndElement();
        }
    }
}
