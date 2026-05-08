using System.IO;
using System.Linq;
using NUnit.Framework;
using PromptUGUI.Controls;
using PromptUGUI.Editor;
using PromptUGUI.Registry;

namespace PromptUGUI.Tests.Editor {
    public class XsdGeneratorTests {

        [Test]
        public void Empty_registry_produces_static_skeleton() {
            var r = new ControlRegistry();
            var xsd = XsdGenerator.Generate(r);
            StringAssert.Contains("<xs:schema", xsd);
            StringAssert.DoesNotContain("targetNamespace", xsd);  // bare-name .ui.xml needs none
            StringAssert.Contains("name=\"Frame\"", xsd);    // 7 primitives present
            StringAssert.Contains("name=\"Btn\"", xsd);
        }

        [Test]
        public void Custom_control_appears_with_UIAttr_attributes() {
            var r = new ControlRegistry();
            r.Register<TestPrimaryButton>("PrimaryButton", null);
            var xsd = XsdGenerator.Generate(r);
            StringAssert.Contains("name=\"PrimaryButton\"", xsd);
            StringAssert.Contains("name=\"label\"", xsd);   // [UIAttr] property
        }

        [Test]
        public void Generate_to_file_produces_readable_file() {
            var r = new ControlRegistry();
            r.Register<TestPrimaryButton>("PrimaryButton", null);
            var path = Path.Combine(UnityEngine.Application.temporaryCachePath, "test.gen.xsd");
            XsdGenerator.GenerateToFile(r, path);
            Assert.IsTrue(File.Exists(path));
            var content = File.ReadAllText(path);
            StringAssert.Contains("PrimaryButton", content);
        }

        [Test]
        public void Icon_element_present_in_xsd() {
            var r = new ControlRegistry();
            var xsd = XsdGenerator.Generate(r);
            StringAssert.Contains("name=\"Icon\"", xsd);
        }

        [Test]
        public void Icon_name_attribute_has_pattern() {
            var r = new ControlRegistry();
            var xsd = XsdGenerator.Generate(r);
            StringAssert.Contains("xs:pattern", xsd);
            StringAssert.Contains(":[\\w\\-]+", xsd);
        }

        [Test]
        public void Generated_file_loads_as_xml_without_encoding_mismatch() {
            // Regression: XmlWriter against StringBuilder declared encoding="utf-16",
            // but the file was written as UTF-8 bytes — causing parsers to choke at
            // (1, 40) "Content is not allowed in prolog".
            var r = new ControlRegistry();
            var path = Path.Combine(UnityEngine.Application.temporaryCachePath, "test.encoding.xsd");
            XsdGenerator.GenerateToFile(r, path);

            var doc = new System.Xml.XmlDocument();
            Assert.DoesNotThrow(() => doc.Load(path),
                "Generated XSD must be parseable; declaration encoding must match actual bytes.");

            var firstLine = File.ReadLines(path).First();
            StringAssert.Contains("encoding=\"utf-8\"", firstLine);
        }

        [Test]
        public void Sample_uiXml_validates_against_generated_schema() {
            // Regression: schema used to declare targetNamespace, but .ui.xml files
            // are written with bare element names (no namespace) + xsi:noNamespaceSchemaLocation.
            // Validation then failed with TargetNamespace.2 + cvc-elt.1.a.
            var r = new ControlRegistry();
            var xsd = XsdGenerator.Generate(r);

            const string sample = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' canvas='overlay'>
    <Frame anchor='stretch'/>
  </Screen>
</PromptUGUI>";

            var schemas = new System.Xml.Schema.XmlSchemaSet();
            schemas.Add(null, System.Xml.XmlReader.Create(new StringReader(xsd)));
            var settings = new System.Xml.XmlReaderSettings {
                ValidationType = System.Xml.ValidationType.Schema,
                Schemas = schemas,
            };
            var errors = new System.Collections.Generic.List<string>();
            settings.ValidationEventHandler += (_, e) => errors.Add(e.Message);
            using (var reader = System.Xml.XmlReader.Create(new StringReader(sample), settings)) {
                while (reader.Read()) { }
            }
            CollectionAssert.IsEmpty(errors,
                "Sample .ui.xml must validate against generated XSD (no namespace mismatch).");
        }

        [Test]
        public void UIAttr_Pattern_propagated_via_reflection() {
            var r = new ControlRegistry();
            r.Register<TestPatternedControl>("Patterned", null);
            var xsd = XsdGenerator.Generate(r);
            StringAssert.Contains("xs:pattern", xsd);
            StringAssert.Contains("^abc$", xsd);
        }
    }

    public class TestPrimaryButton : Control {
        [UIAttr] public string Label { get; set; }
    }

    public class TestPatternedControl : Control {
        [UIAttr(Pattern = "^abc$")] public string Code { get; set; }
    }
}
