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
        public void Registered_TabMenu_reaches_the_schema_with_its_attributes()
        {
            // Reflected, unlike Frame's hand-written list — this guards the registration, so a
            // <TabMenu> that authoring tools would flag as unknown fails here first.
            var xsd = XsdGenerator.Generate(PromptUGUI.Application.UI.Registry);
            StringAssert.Contains("name=\"TabMenu\"", xsd);
            StringAssert.Contains("name=\"popupWidth\"", xsd);
            StringAssert.Contains("name=\"transition\"", xsd);
        }

        [Test]
        public void Frame_lists_its_inner_glow_attributes()
        {
            // Frame's attribute list is hand-written rather than reflected, so every attribute
            // added to it has to be added there too or authoring tools flag valid XML as invalid.
            var xsd = XsdGenerator.Generate(new ControlRegistry());
            StringAssert.Contains("name=\"innerGlow\"", xsd);
            StringAssert.Contains("name=\"innerGlowColor\"", xsd);
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
        public void RawImage_element_appears_with_reflected_attrs()
        {
            // RawImage isn't a hardcoded primitive — it flows through the reflected
            // `customs` path, so its element + [UIAttr] attrs appear automatically.
            var r = new ControlRegistry();
            r.Register<RawImage>("RawImage", null);
            var xsd = XsdGenerator.Generate(r);
            StringAssert.Contains("name=\"RawImage\"", xsd);   // element emitted
            StringAssert.Contains("name=\"showMask\"", xsd);   // [UIAttr] reflected (camelCase ShowMask)
        }

        [Test]
        public void Carousel_peek_attributes_appear_in_xsd()
        {
            // peek-mode attrs are [UIAttr] on Carousel → reflected through the customs path.
            var r = new ControlRegistry();
            r.Register<Carousel>("Carousel", null);
            var xsd = XsdGenerator.Generate(r);
            StringAssert.Contains("name=\"fill\"", xsd);
            StringAssert.Contains("name=\"spacing\"", xsd);
            StringAssert.Contains("name=\"edgeScale\"", xsd);
            StringAssert.Contains("name=\"edgeAlpha\"", xsd);
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

        private static void AssertValidationFails(string xsd, string sample, string message)
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
            CollectionAssert.IsNotEmpty(errors, message);
        }

        [Test]
        public void Duplicate_id_within_same_Screen_fails_validation()
        {
            // Mirrors parser's idsInScreen check (UIDocumentParser.cs:83) — XSD
            // xs:unique should catch this before xmllint hands off to Unity.
            var r = new ControlRegistry();
            var xsd = XsdGenerator.Generate(r);
            const string sample = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S'>
    <Frame id='btn'/>
    <Frame id='btn'/>
  </Screen>
</PromptUGUI>";
            AssertValidationFails(xsd, sample,
                "Duplicate id within same Screen must fail XSD validation.");
        }

        [Test]
        public void Duplicate_id_nested_within_same_Screen_fails_validation()
        {
            // Same-scope check must reach descendants, not just direct children.
            var r = new ControlRegistry();
            var xsd = XsdGenerator.Generate(r);
            const string sample = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S'>
    <VStack id='outer'>
      <Frame id='x'/>
    </VStack>
    <HStack>
      <Frame id='x'/>
    </HStack>
  </Screen>
</PromptUGUI>";
            AssertValidationFails(xsd, sample,
                "Duplicate id at any depth inside one Screen must fail XSD validation.");
        }

        [Test]
        public void Duplicate_id_within_same_Template_fails_validation()
        {
            // Mirrors parser's tplIds scope (UIDocumentParser.cs:206).
            var r = new ControlRegistry();
            var xsd = XsdGenerator.Generate(r);
            const string sample = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Template name='Dup'>
    <VStack id='root'>
      <Frame id='child'/>
      <Frame id='child'/>
    </VStack>
  </Template>
  <Screen name='S'><Frame/></Screen>
</PromptUGUI>";
            AssertValidationFails(xsd, sample,
                "Duplicate id within same Template body must fail XSD validation.");
        }

        [Test]
        public void Same_id_in_different_Screens_validates()
        {
            // Each Screen is its own scope — same id text across two Screens is fine.
            var r = new ControlRegistry();
            var xsd = XsdGenerator.Generate(r);
            const string sample = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='A'><Frame id='root'/></Screen>
  <Screen name='B'><Frame id='root'/></Screen>
</PromptUGUI>";
            AssertValidates(xsd, sample,
                "Same id across different Screens is fine — each Screen scopes ids independently.");
        }

        [Test]
        public void Same_id_in_Screen_and_Template_body_validates()
        {
            // Template body and Screen are separate scopes (parser uses two distinct
            // HashSets) — reusing 'root' in both must NOT trip uniqueness.
            var r = new ControlRegistry();
            var xsd = XsdGenerator.Generate(r);
            const string sample = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Template name='P'><Frame id='root'/></Template>
  <Screen name='S'><Frame id='root'/></Screen>
</PromptUGUI>";
            AssertValidates(xsd, sample,
                "Template body and Screen have separate id scopes.");
        }

        [Test]
        public void Duplicate_id_across_Screen_and_Variant_Add_fails_validation()
        {
            // Parser routes Variant/Add descendants through idsInScreen
            // (UIDocumentParser.cs:244) — XSD scope must match by reaching every
            // descendant of <Screen>, including nested <Add> children.
            var r = new ControlRegistry();
            var xsd = XsdGenerator.Generate(r);
            const string sample = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S'>
    <Frame id='dup'/>
    <Variant when='dark'>
      <Add into='/'>
        <Frame id='dup'/>
      </Add>
    </Variant>
  </Screen>
</PromptUGUI>";
            AssertValidationFails(xsd, sample,
                "Variant/Add children share Screen's id scope — duplicate must fail.");
        }

        [Test]
        public void Xsd_includes_Show_element()
        {
            // <Show> is a registered builtin (BuiltinPrimitives) and not in the hardcoded
            // primitives set, so it surfaces via reflection. It inherits [UIAttr("on")]
            // from Trigger, so 'on' must appear too.
            var r = new ControlRegistry();
            r.Register<Show>("Show", null);
            var xsd = XsdGenerator.Generate(r);

            StringAssert.Contains("name=\"Show\"", xsd);
            StringAssert.Contains("name=\"on\"", xsd);   // inherited Trigger.[UIAttr("on")]
        }

        [Test]
        public void Xsd_Btn_declares_state_color_attributes()
        {
            // Btn is hardcoded in the generator (not reflection-driven), so the new
            // Btn-specific state-tint [UIAttr]s (hoverColor/pressedColor/disabledColor)
            // must be added to the hardcoded Btn attr list or IDEs flag valid authoring.
            var r = new ControlRegistry();
            var xsd = XsdGenerator.Generate(r);
            StringAssert.Contains("name=\"hoverColor\"", xsd);
            StringAssert.Contains("name=\"pressedColor\"", xsd);
            StringAssert.Contains("name=\"disabledColor\"", xsd);
            StringAssert.Contains("name=\"pressedSprite\"", xsd);
            // *Modulate family — also hardcoded in the Btn block (same as *Color above, since Btn
            // is not reflection-driven); guard all three so a future refactor can't drop one silently.
            StringAssert.Contains("name=\"hoverModulate\"", xsd);
            StringAssert.Contains("name=\"pressedModulate\"", xsd);
            StringAssert.Contains("name=\"disabledModulate\"", xsd);
        }

        [Test]
        public void Xsd_Tab_and_Toggle_declare_selectedColor_attribute()
        {
            // Tab and Toggle expose [UIAttr] selectedColor (reflection-driven, not hardcoded).
            // This pins that the attribute surfaces in the generated schema so IDEs don't
            // flag valid authoring as unknown.
            var r = new ControlRegistry();
            r.Register<Tab>("Tab", null, defaultTextAttr: "text");
            r.Register<Toggle>("Toggle", null, defaultTextAttr: "text");
            var xsd = XsdGenerator.Generate(r);
            StringAssert.Contains("name=\"selectedColor\"", xsd);
            // *Modulate family — reflection-driven [UIAttr]s added in Task 2; guard that
            // the XSD still emits them after any future refactor.
            StringAssert.Contains("name=\"selectedModulate\"", xsd);
        }

        [Test]
        public void Xsd_commonAttrs_declares_stateReact()
        {
            // stateReact is [UIAttr] on the Control base — it applies to every control,
            // so it belongs in the shared commonAttrs group (not per-element).
            var r = new ControlRegistry();
            var xsd = XsdGenerator.Generate(r);
            StringAssert.Contains("name=\"stateReact\"", xsd);
        }

        [Test]
        public void Screen_element_declares_reference_attribute()
        {
            var r = new ControlRegistry();
            var xsd = XsdGenerator.Generate(r);
            StringAssert.Contains("name=\"reference\"", xsd);
        }

        [Test]
        public void Screen_element_declares_scale_mode_enum_attribute()
        {
            var r = new ControlRegistry();
            var xsd = XsdGenerator.Generate(r);
            // The Screen element should carry an explicit attribute name="scale-mode"
            // restricted to {auto, pixel}.
            StringAssert.Contains("name=\"scale-mode\"", xsd);
            StringAssert.Contains("value=\"auto\"", xsd);
            StringAssert.Contains("value=\"pixel\"", xsd);
        }

        [Test]
        public void Xsd_includes_Trigger_and_Animation()
        {
            // Regression lock (ANIM-D28): XSD generator is reflection-driven; this
            // test pins that <Trigger> and <Animation> (and their [UIAttr] attributes)
            // appear in the generated schema after being registered in the registry.
            var r = new ControlRegistry();
            r.Register<Trigger>("Trigger", null);
            r.Register<Animation>("Animation", null);
            var xsd = XsdGenerator.Generate(r);

            StringAssert.Contains("name=\"Trigger\"", xsd);
            StringAssert.Contains("name=\"Animation\"", xsd);
            StringAssert.Contains("name=\"on\"", xsd);   // Trigger.[UIAttr("on")]
            StringAssert.Contains("name=\"type\"", xsd);   // Animation.[UIAttr("type")]
            StringAssert.Contains("name=\"translate\"", xsd);   // Animation.[UIAttr("translate")]
            StringAssert.Contains("name=\"count\"", xsd);   // Animation.[UIAttr("count")]
            StringAssert.Contains("name=\"char-color\"", xsd);   // Animation.[UIAttr("char-color")]
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

        [Test]
        public void Generated_Xsd_Contains_Theme_Element()
        {
            var xsd = XsdGenerator.Generate(new ControlRegistry());
            StringAssert.Contains("name=\"Theme\"", xsd);
        }

        [Test]
        public void Generated_Xsd_Contains_Color_Element()
        {
            var xsd = XsdGenerator.Generate(new ControlRegistry());
            StringAssert.Contains("name=\"Color\"", xsd);
        }

        [Test]
        public void Generated_Xsd_Theme_Has_Base_Attribute()
        {
            var xsd = XsdGenerator.Generate(new ControlRegistry());
            // base is optional on <Theme>
            StringAssert.Contains("name=\"base\"", xsd);
        }

        [Test]
        public void Carousel_element_and_dot_attrs_present_in_xsd()
        {
            var r = new ControlRegistry();
            r.Register<Carousel>("Carousel", null);
            var xsd = XsdGenerator.Generate(r);
            StringAssert.Contains("name=\"Carousel\"", xsd);
            StringAssert.Contains("name=\"itemTemplate\"", xsd);
            StringAssert.Contains("name=\"dotSelectedColor\"", xsd);
        }

        [Test]
        public void Markdown_element_and_attrs_present_in_xsd()
        {
            // Markdown is registered via reflection (not hardcoded), so its element
            // and [UIAttr] attributes appear automatically in the generated schema.
            var r = new ControlRegistry();
            r.Register<Markdown>("Markdown", null, defaultTextAttr: "text");
            var xsd = XsdGenerator.Generate(r);
            StringAssert.Contains("name=\"Markdown\"", xsd);
            StringAssert.Contains("name=\"bodyFont\"", xsd);
            StringAssert.Contains("name=\"codeFont\"", xsd);
            StringAssert.Contains("name=\"linkColor\"", xsd);
        }

        [Test]
        public void ScrollList_and_Dropdown_skin_attrs_in_schema()
        {
            // Tasks 3-5 added skin attrs via [UIAttr]; the reflected `customs` path emits them
            // automatically — this is the regression anchor proving they reach the schema.
            var r = new ControlRegistry();
            r.Register<ScrollList>("ScrollList", null);
            r.Register<Dropdown>("Dropdown", null);
            var xsd = XsdGenerator.Generate(r);
            StringAssert.Contains("name=\"frame\"", xsd);
            StringAssert.Contains("name=\"frameColor\"", xsd);
            StringAssert.Contains("name=\"mask\"", xsd);
            StringAssert.Contains("name=\"popupSprite\"", xsd);
            StringAssert.Contains("name=\"popupColor\"", xsd);
            StringAssert.Contains("name=\"popupMask\"", xsd);
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
