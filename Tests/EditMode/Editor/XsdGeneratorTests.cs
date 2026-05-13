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
            // Set name stays strict; icon-name half mirrors the filesystem, so the
            // pattern only forbids the ':' delimiter (any other path char is fine).
            // Alternation accepts Template Param placeholders ('{{iconName}}').
            StringAssert.Contains("[A-Za-z0-9_\\-]+:[^:]+|.*\\{\\{.*", xsd);
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
        public void Xsd_accepts_stretch_keyword_on_width_and_height()
        {
            // width=/height= now accept the 'stretch' keyword (parser-side flex). The XSD
            // declares these as xs:string with no enum, so any string passes. This test pins
            // that contract: if someone later tightens the type (e.g. to xs:float), this
            // breaks loud and reminds them to allow 'stretch' explicitly.
            var r = new ControlRegistry();
            var xsd = XsdGenerator.Generate(r);

            const string sample = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S'>
    <VStack id='stack' width='380' height='180'>
      <Btn id='b' width='stretch' height='46'/>
      <Btn id='c' width='100'     height='stretch'/>
    </VStack>
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
                "width='stretch' / height='stretch' must pass XSD validation (xs:string contract).");
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
        public void Icon_name_pattern_accepts_ampersand_and_punctuation_in_iconname()
        {
            // Solar Bold Duotone ships paths like 'Map & Location/Radar 2.png' and
            // 'Files (Group)/file 1.0,v2.png'. Parser allows them; XSD must too.
            var r = new ControlRegistry();
            var xsd = XsdGenerator.Generate(r);

            const string sample = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S'>
    <Icon name='solar:Map &amp; Location/Radar 2'/>
    <Icon name=""solar:Files (Group)/file 1.0,v2's""/>
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
                "Real icon-pack paths with '&', parens, commas, apostrophes must validate.");
        }

        [Test]
        public void Icon_name_pattern_accepts_template_placeholder()
        {
            // Templates use Param substitution: <Icon name="{{iconName}}"/>. The
            // final form is only determined at expansion time. Parser already skips
            // format validation when the value contains '{{'; XSD must do likewise
            // or the IDE will red-underline valid Template authoring.
            var r = new ControlRegistry();
            var xsd = XsdGenerator.Generate(r);

            const string sample = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Template name='IconBtn'>
    <Param name='iconName'/>
    <Icon name='{{iconName}}'/>
  </Template>
  <Screen name='S'>
    <Frame/>
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
                "Template Param placeholders in <Icon name> must validate against XSD.");
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
        public void Btn_element_accepts_inline_text_content()
        {
            // Spec: <Btn>开始</Btn> shorthand (BuiltinPrimitives registers Btn with
            // defaultTextAttr='text'). XSD previously declared Btn as element-only,
            // so xmllint rejected the shorthand as 'Character content other than
            // whitespace is not allowed'. Pinning mixed-content here.
            var r = new ControlRegistry();
            var xsd = XsdGenerator.Generate(r);

            const string sample = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S'>
    <Btn>开始</Btn>
  </Screen>
</PromptUGUI>";

            AssertValidates(xsd, sample, "<Btn>开始</Btn> text shorthand must validate.");
        }

        [Test]
        public void Btn_element_still_accepts_nested_child()
        {
            // Regression guard: making Btn mixed-content must not lose the existing
            // ability to nest child elements (template authoring uses
            // <Btn><Text>{{label}}</Text></Btn>).
            var r = new ControlRegistry();
            var xsd = XsdGenerator.Generate(r);

            const string sample = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S'>
    <Btn>
      <Text anchor='center'>Inside</Text>
    </Btn>
  </Screen>
</PromptUGUI>";

            AssertValidates(xsd, sample, "<Btn><Text>...</Text></Btn> must still validate.");
        }

        [Test]
        public void Custom_control_with_defaultTextAttr_accepts_inline_text()
        {
            // Toggle / InputField are registered through BuiltinPrimitives with
            // defaultTextAttr='text'; the XSD generator must honor Entry.DefaultTextAttr
            // and emit mixed-content for those tags. Use Toggle as the representative
            // case — same code path covers any custom control registered with
            // defaultTextAttr.
            var r = new ControlRegistry();
            r.Register<Toggle>("Toggle", null, defaultTextAttr: "text");
            var xsd = XsdGenerator.Generate(r);

            const string sample = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S'>
    <Toggle>静音</Toggle>
  </Screen>
</PromptUGUI>";

            AssertValidates(xsd, sample, "<Toggle>静音</Toggle> text shorthand must validate.");
        }

        [Test]
        public void Custom_control_without_defaultTextAttr_rejects_inline_text()
        {
            // Negative case: registering a control without defaultTextAttr means
            // runtime would reject text body, and XSD must too. Guards against the
            // opposite regression (everyone gets mixed='true').
            var r = new ControlRegistry();
            r.Register<TestPrimaryButton>("PrimaryButton", null);
            var xsd = XsdGenerator.Generate(r);

            const string sample = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S'>
    <PrimaryButton>nope</PrimaryButton>
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
                "Custom control without defaultTextAttr must remain element-only.");
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
        public void Custom_control_attr_colliding_with_commonAttrs_is_deduped()
        {
            // Regression: <ScrollList>'s [UIAttr] Padding/Spacing collided with
            // commonAttrs (which already declares padding/spacing). The reflected
            // attrs were appended after <attributeGroup ref="commonAttrs"/> without
            // dedup, so the same <complexType> contained two <xs:attribute name="padding"/>
            // — XSD §3.4.3 forbids duplicate attribute names in a complexType, and
            // XmlSchemaSet.Compile() rejects the schema with "Duplicate attribute".
            var r = new ControlRegistry();
            r.Register<TestScrollLike>("ScrollLike", null);
            var xsd = XsdGenerator.Generate(r);

            var schemas = new System.Xml.Schema.XmlSchemaSet();
            var errors = new System.Collections.Generic.List<string>();
            schemas.ValidationEventHandler += (_, e) => errors.Add(e.Message);
            schemas.Add(null, System.Xml.XmlReader.Create(new StringReader(xsd)));
            schemas.Compile();

            CollectionAssert.IsEmpty(errors,
                "Custom-control [UIAttr] names that collide with commonAttrs must be skipped, not re-emitted.");
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

        [Test]
        public void Screen_element_declares_reference_attribute()
        {
            var r = new ControlRegistry();
            var xsd = XsdGenerator.Generate(r);
            StringAssert.Contains("name=\"reference\"", xsd);
        }

        [Test]
        public void Screen_element_allows_variant_form_via_any_attribute()
        {
            // Validate reference.<variant> attribute on <Screen> against the generated
            // schema — covers both 'reference declared' and 'open variant namespace'.
            var r = new ControlRegistry();
            var xsd = XsdGenerator.Generate(r);

            const string sample = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' reference='1920x1080' reference.mobile='1080x1920'>
    <Frame/>
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
                "Screen reference= and reference.<variant>= must validate against generated XSD.");
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

    public class TestScrollLike : Control
    {
        [UIAttr] public string Padding { get; set; }
        [UIAttr] public float Spacing { get; set; }
    }
}
