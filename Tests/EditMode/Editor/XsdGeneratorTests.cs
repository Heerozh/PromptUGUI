using System.IO;
using System.Linq;
using NUnit.Framework;
using PromptUGUI.Controls;
using PromptUGUI.Editor;
using PromptUGUI.Registry;

namespace PromptUGUI.Tests.Editor
{
    public class XsdGeneratorTests
    {

        [Test]
        public void Empty_registry_produces_static_skeleton()
        {
            var r = new ControlRegistry();
            var xsd = XsdGenerator.Generate(r);
            StringAssert.Contains("<xs:schema", xsd);
            StringAssert.DoesNotContain("targetNamespace", xsd);  // bare-name .ui.xml needs none
            StringAssert.Contains("name=\"Frame\"", xsd);    // 7 primitives present
            StringAssert.Contains("name=\"Btn\"", xsd);
        }

        [Test]
        public void Custom_control_appears_with_UIAttr_attributes()
        {
            var r = new ControlRegistry();
            r.Register<TestPrimaryButton>("PrimaryButton", null);
            var xsd = XsdGenerator.Generate(r);
            StringAssert.Contains("name=\"PrimaryButton\"", xsd);
            StringAssert.Contains("name=\"label\"", xsd);   // [UIAttr] property
        }

        [Test]
        public void Generate_to_file_produces_readable_file()
        {
            var r = new ControlRegistry();
            r.Register<TestPrimaryButton>("PrimaryButton", null);
            var path = Path.Combine(UnityEngine.Application.temporaryCachePath, "test.gen.xsd");
            XsdGenerator.GenerateToFile(r, path);
            Assert.IsTrue(File.Exists(path));
            var content = File.ReadAllText(path);
            StringAssert.Contains("PrimaryButton", content);
        }

        [Test]
        public void Icon_element_present_in_xsd()
        {
            var r = new ControlRegistry();
            var xsd = XsdGenerator.Generate(r);
            StringAssert.Contains("name=\"Icon\"", xsd);
        }

        [Test]
        public void Icon_name_attribute_has_pattern()
        {
            var r = new ControlRegistry();
            var xsd = XsdGenerator.Generate(r);
            StringAssert.Contains("xs:pattern", xsd);
            // Set name strict; icon name allows space (icon-pack PNG names) AND
            // '/' (subfolder path form, e.g. 'ui:Combat/heart').
            StringAssert.Contains(":[A-Za-z0-9_\\- /]+", xsd);
        }

        [Test]
        public void Icon_name_pattern_accepts_subfolder_slash()
        {
            // Subfolder disambiguation (`ui:Combat/heart`) is parser-valid; XSD must
            // accept it too, or IDE validators will flag valid authoring as broken.
            var r = new ControlRegistry();
            var xsd = XsdGenerator.Generate(r);

            const string sample = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S'>
    <Icon name='ui:Combat/heart'/>
  </Screen>
</PromptUGUI>";

            var schemas = new System.Xml.Schema.XmlSchemaSet();
            schemas.Add(null, System.Xml.XmlReader.Create(new StringReader(xsd)));
            var settings = new System.Xml.XmlReaderSettings
            {
                ValidationType = System.Xml.ValidationType.Schema,
                Schemas = schemas,
            };
            var errors = new System.Collections.Generic.List<string>();
            settings.ValidationEventHandler += (_, e) => errors.Add(e.Message);
            using (var reader = System.Xml.XmlReader.Create(new StringReader(sample), settings))
            {
                while (reader.Read()) { }
            }
            CollectionAssert.IsEmpty(errors,
                "'ui:Combat/heart' is parser-valid; XSD must validate it.");
        }

        [Test]
        public void Icon_name_pattern_accepts_space_in_iconname()
        {
            // Real-world icon packs ship PNGs with spaces ('Alt Arrow Right.png').
            // Parser allows this; XSD must too, otherwise IDE flags valid XML as invalid.
            var r = new ControlRegistry();
            var xsd = XsdGenerator.Generate(r);

            const string sample = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S'>
    <Icon name='solar:Alt Arrow Right'/>
  </Screen>
</PromptUGUI>";

            var schemas = new System.Xml.Schema.XmlSchemaSet();
            schemas.Add(null, System.Xml.XmlReader.Create(new StringReader(xsd)));
            var settings = new System.Xml.XmlReaderSettings
            {
                ValidationType = System.Xml.ValidationType.Schema,
                Schemas = schemas,
            };
            var errors = new System.Collections.Generic.List<string>();
            settings.ValidationEventHandler += (_, e) => errors.Add(e.Message);
            using (var reader = System.Xml.XmlReader.Create(new StringReader(sample), settings))
            {
                while (reader.Read()) { }
            }
            CollectionAssert.IsEmpty(errors,
                "'solar:Alt Arrow Right' is parser-valid; XSD must validate it.");
        }

        [Test]
        public void Icon_name_pattern_rejects_space_in_setname()
        {
            // Set name is strict (parser rejects 'my set:icon'); XSD must too.
            var r = new ControlRegistry();
            var xsd = XsdGenerator.Generate(r);

            const string sample = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S'>
    <Icon name='my set:Forward'/>
  </Screen>
</PromptUGUI>";

            var schemas = new System.Xml.Schema.XmlSchemaSet();
            schemas.Add(null, System.Xml.XmlReader.Create(new StringReader(xsd)));
            var settings = new System.Xml.XmlReaderSettings
            {
                ValidationType = System.Xml.ValidationType.Schema,
                Schemas = schemas,
            };
            var errors = new System.Collections.Generic.List<string>();
            settings.ValidationEventHandler += (_, e) => errors.Add(e.Message);
            using (var reader = System.Xml.XmlReader.Create(new StringReader(sample), settings))
            {
                while (reader.Read()) { }
            }
            CollectionAssert.IsNotEmpty(errors,
                "'my set:Forward' has space in set name and must fail XSD validation.");
        }

        [Test]
        public void Generated_file_loads_as_xml_without_encoding_mismatch()
        {
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
        public void Sample_uiXml_validates_against_generated_schema()
        {
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
            var settings = new System.Xml.XmlReaderSettings
            {
                ValidationType = System.Xml.ValidationType.Schema,
                Schemas = schemas,
            };
            var errors = new System.Collections.Generic.List<string>();
            settings.ValidationEventHandler += (_, e) => errors.Add(e.Message);
            using (var reader = System.Xml.XmlReader.Create(new StringReader(sample), settings))
            {
                while (reader.Read()) { }
            }
            CollectionAssert.IsEmpty(errors,
                "Sample .ui.xml must validate against generated XSD (no namespace mismatch).");
        }

        [Test]
        public void Icon_name_pattern_accepts_runtime_valid_values()
        {
            // Regression: pattern was '^[\w\-]+:[\w\-]+$' — XSD treats ^/$ literally,
            // so 'solar:Forward' (and any value not framed by literal ^/$) was rejected.
            var r = new ControlRegistry();
            var xsd = XsdGenerator.Generate(r);

            const string sample = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S'>
    <Icon name='solar:Forward'/>
  </Screen>
</PromptUGUI>";

            var schemas = new System.Xml.Schema.XmlSchemaSet();
            schemas.Add(null, System.Xml.XmlReader.Create(new StringReader(xsd)));
            var settings = new System.Xml.XmlReaderSettings
            {
                ValidationType = System.Xml.ValidationType.Schema,
                Schemas = schemas,
            };
            var errors = new System.Collections.Generic.List<string>();
            settings.ValidationEventHandler += (_, e) => errors.Add(e.Message);
            using (var reader = System.Xml.XmlReader.Create(new StringReader(sample), settings))
            {
                while (reader.Read()) { }
            }
            CollectionAssert.IsEmpty(errors,
                "'solar:Forward' is a valid runtime icon name and must validate against XSD.");
        }

        [Test]
        public void Text_element_accepts_inline_text_content()
        {
            // Spec: <Text>Hi</Text> ≡ <Text text="Hi"/>. XSD must allow text body
            // for <Text> (was rejected as element-only).
            var r = new ControlRegistry();
            var xsd = XsdGenerator.Generate(r);

            const string sample = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S'>
    <Text>Hello world</Text>
  </Screen>
</PromptUGUI>";

            var schemas = new System.Xml.Schema.XmlSchemaSet();
            schemas.Add(null, System.Xml.XmlReader.Create(new StringReader(xsd)));
            var settings = new System.Xml.XmlReaderSettings
            {
                ValidationType = System.Xml.ValidationType.Schema,
                Schemas = schemas,
            };
            var errors = new System.Collections.Generic.List<string>();
            settings.ValidationEventHandler += (_, e) => errors.Add(e.Message);
            using (var reader = System.Xml.XmlReader.Create(new StringReader(sample), settings))
            {
                while (reader.Read()) { }
            }
            CollectionAssert.IsEmpty(errors, "<Text>...</Text> shorthand must validate.");
        }

        [Test]
        public void UIAttr_Pattern_propagated_via_reflection()
        {
            var r = new ControlRegistry();
            r.Register<TestPatternedControl>("Patterned", null);
            var xsd = XsdGenerator.Generate(r);
            StringAssert.Contains("xs:pattern", xsd);
            StringAssert.Contains("^abc$", xsd);
        }

        [Test]
        public void Template_tags_appear_as_elements_in_xsd()
        {
            var r = new ControlRegistry();
            var xsd = XsdGenerator.Generate(r, new[] { "TitledPanel", "ItemRow" });
            StringAssert.Contains("name=\"TitledPanel\"", xsd);
            StringAssert.Contains("name=\"ItemRow\"", xsd);
        }

        [Test]
        public void Template_tags_added_to_controlGroup()
        {
            var r = new ControlRegistry();
            var xsd = XsdGenerator.Generate(r, new[] { "TitledPanel" });
            StringAssert.Contains("ref=\"TitledPanel\"", xsd);
        }

        [Test]
        public void Template_invocation_validates_against_xsd()
        {
            var r = new ControlRegistry();
            var xsd = XsdGenerator.Generate(r, new[] { "TitledPanel" });
            const string sample = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S'>
    <TitledPanel title='Settings'>
      <Frame/>
    </TitledPanel>
  </Screen>
</PromptUGUI>";
            AssertValidates(xsd, sample,
                "Template invocation with Param-as-attribute must validate.");
        }

        [Test]
        public void Template_with_no_extras_unchanged_baseline()
        {
            // Regression: passing null/empty templateTags must produce the exact
            // schema as the existing API (no spurious refs / elements).
            var r = new ControlRegistry();
            var withNull = XsdGenerator.Generate(r, null);
            var withEmpty = XsdGenerator.Generate(r, System.Array.Empty<string>());
            var legacy = XsdGenerator.Generate(r);
            Assert.AreEqual(legacy, withNull);
            Assert.AreEqual(legacy, withEmpty);
        }

        [Test]
        public void ScanTemplates_collects_template_names_from_files()
        {
            var dir = Path.Combine(UnityEngine.Application.temporaryCachePath,
                                   "xsd_scan_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var p1 = Path.Combine(dir, "a.ui.xml");
                var p2 = Path.Combine(dir, "b.ui.xml");
                File.WriteAllText(p1,
                    "<?xml version='1.0'?><PromptUGUI version='1'>" +
                    "<Template name='TitledPanel'><Frame/></Template></PromptUGUI>");
                File.WriteAllText(p2,
                    "<?xml version='1.0'?><PromptUGUI version='1'>" +
                    "<Template name='ItemRow'><Frame/></Template>" +
                    "<Template name='Footer'><Frame/></Template></PromptUGUI>");

                var names = XsdGenerator.ScanTemplates(new[] { p1, p2 });
                CollectionAssert.AreEquivalent(
                    new[] { "TitledPanel", "ItemRow", "Footer" }, names);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Test]
        public void ScanTemplates_skips_unparseable_files()
        {
            var dir = Path.Combine(UnityEngine.Application.temporaryCachePath,
                                   "xsd_scan_bad_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var bad = Path.Combine(dir, "bad.ui.xml");
                var good = Path.Combine(dir, "good.ui.xml");
                File.WriteAllText(bad, "<not even xml");
                File.WriteAllText(good,
                    "<?xml version='1.0'?><PromptUGUI version='1'>" +
                    "<Template name='Ok'><Frame/></Template></PromptUGUI>");

                var names = XsdGenerator.ScanTemplates(new[] { bad, good });
                CollectionAssert.Contains(names, "Ok");
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        private static void AssertValidates(string xsd, string sample, string message)
        {
            var schemas = new System.Xml.Schema.XmlSchemaSet();
            schemas.Add(null, System.Xml.XmlReader.Create(new StringReader(xsd)));
            var settings = new System.Xml.XmlReaderSettings
            {
                ValidationType = System.Xml.ValidationType.Schema,
                Schemas = schemas,
            };
            var errors = new System.Collections.Generic.List<string>();
            settings.ValidationEventHandler += (_, e) => errors.Add(e.Message);
            using (var reader = System.Xml.XmlReader.Create(new StringReader(sample), settings))
            {
                while (reader.Read()) { }
            }
            CollectionAssert.IsEmpty(errors, message);
        }
    }

    public class TestPrimaryButton : Control
    {
        [UIAttr] public string Label { get; set; }
    }

    public class TestPatternedControl : Control
    {
        [UIAttr(Pattern = "^abc$")] public string Code { get; set; }
    }
}
