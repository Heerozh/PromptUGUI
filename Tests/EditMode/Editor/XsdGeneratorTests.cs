using System.IO;
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
            StringAssert.Contains("targetNamespace=\"https://prompt-ugui/v1\"", xsd);
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
