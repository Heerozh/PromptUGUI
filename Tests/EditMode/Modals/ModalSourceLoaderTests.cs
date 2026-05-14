using System;
using System.IO;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Application.Modals;

namespace PromptUGUI.Tests.Modals
{
    public class ModalSourceLoaderTests
    {
        [TearDown]
        public void TearDown() => UI.ResetForTests();

        [Test]
        public void NonBuiltin_src_uses_UI_SourceResolver()
        {
            UI.SourceResolver = src => AwaitableHelpers.Completed(src == "my/Foo" ? "<xml/>" : null);
            var xml = ModalSourceLoader.LoadAsync("my/Foo").GetAwaiter().GetResult();
            Assert.AreEqual("<xml/>", xml);
        }

        [Test]
        public void NonBuiltin_src_with_no_resolver_throws()
        {
            UI.SourceResolver = null;
            Assert.Throws<InvalidOperationException>(() =>
                ModalSourceLoader.LoadAsync("my/Foo").GetAwaiter().GetResult());
        }

        [Test]
        public void Builtin_prefix_missing_resource_throws()
        {
            // No file at Resources/PromptUGUI/Modals/Nonexistent.ui.xml
            Assert.Throws<InvalidOperationException>(() =>
                ModalSourceLoader.LoadAsync("PromptUGUI/Modals/Nonexistent").GetAwaiter().GetResult());
        }

        [Test]
        public void Builtin_MessageBox_xml_is_loadable()
        {
            // Unity strips only `.xml` (the last dot), so the asset's lookup name is
            // `MessageBox.ui` for `MessageBox.ui.xml`. The src key must include `.ui`.
            var xml = ModalSourceLoader.LoadAsync("PromptUGUI/Modals/MessageBox.ui")
                .GetAwaiter().GetResult();
            StringAssert.Contains("<Screen name=\"PromptUGUI/Modals/MessageBox\"", xml);
            StringAssert.Contains("id=\"backdrop\"", xml);
            StringAssert.Contains("id=\"ok\"", xml);
        }

    }
}
