using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Application.Modals;
using UnityEngine.EventSystems;
using PBtn = PromptUGUI.Controls.Btn;
using PImage = PromptUGUI.Controls.Image;
using PMarkdown = PromptUGUI.Controls.Markdown;
using PText = PromptUGUI.Controls.Text;

namespace PromptUGUI.Tests.Modals
{
    public class MarkdownBoxTests
    {
        private const string MdBoxXml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='test/MdBox1'>
    <Image id='backdrop' anchor='stretch' color='#0000007F'/>
    <Frame id='dialog' anchor='center' size='600x400'>
      <VStack anchor='stretch' margin='16' spacing='8'>
        <Text id='title' fontSize='20'/>
        <Markdown id='markdown' width='stretch' height='stretch'/>
      </VStack>
      <Btn id='close' anchor='top-right' size='36x36'>×</Btn>
    </Frame>
  </Screen>
</PromptUGUI>";

        private Dictionary<string, string> _files;

        [SetUp]
        public void SetUp()
        {
            UI.ResetForTests();
            _files = new Dictionary<string, string> { ["test/MdBox1"] = MdBoxXml };
            UI.SourceResolver = src =>
                AwaitableHelpers.Completed(_files.TryGetValue(src, out var v) ? v : null);
            MarkdownBox.XmlSrc = "test/MdBox1";
        }

        [TearDown]
        public void TearDown() => UI.ResetForTests();

        private static PMarkdown Md()
            => UI.Modal.TopScreen.Get<PMarkdown>("markdown");

        [Test]
        public void Open_sets_markdown_source_text()
        {
            UI.Modal.OpenAsync(new MarkdownBoxRequest { Text = "# Hello\nworld" });
            Assert.AreEqual("# Hello\nworld", Md().Text);
        }

        [Test]
        public void Null_title_hides_title_node()
        {
            UI.Modal.OpenAsync(new MarkdownBoxRequest { Text = "body" });
            Assert.IsFalse(UI.Modal.TopScreen.Get<PText>("title").GameObject.activeSelf);
        }

        [Test]
        public void Title_present_shows_title_with_text()
        {
            UI.Modal.OpenAsync(new MarkdownBoxRequest { Text = "body", Title = "公告" });
            var title = UI.Modal.TopScreen.Get<PText>("title");
            Assert.IsTrue(title.GameObject.activeSelf);
            Assert.AreEqual("公告", title.TmpComponent.text);
        }

        [Test]
        public void Click_close_btn_completes_and_closes()
        {
            var task = UI.Modal.OpenAsync(new MarkdownBoxRequest { Text = "body" });
            UI.Modal.TopScreen.Get<PBtn>("close").SimulateClick();
            Assert.IsTrue(task.GetAwaiter().GetResult());
            Assert.IsFalse(UI.Modal.IsAnyOpen);
        }

        [Test]
        public void Backdrop_pointer_down_closes()
        {
            var task = UI.Modal.OpenAsync(new MarkdownBoxRequest { Text = "body" });
            var backdrop = UI.Modal.TopScreen.Get<PImage>("backdrop");
            ExecuteEvents.Execute(backdrop.GameObject,
                new PointerEventData(EventSystem.current),
                ExecuteEvents.pointerDownHandler);
            Assert.IsTrue(task.GetAwaiter().GetResult());
            Assert.IsFalse(UI.Modal.IsAnyOpen);
        }

        [Test]
        public void TryEscape_returns_true()
        {
            var req = new MarkdownBoxRequest { Text = "body" };
            Assert.IsTrue(req.TryEscape(out var r));
            Assert.IsTrue(r);
        }

        // ESC 走真实管线:pump → ModalEscapeListener → TryEscape → close。
        [Test]
        public void Escape_via_listener_closes()
        {
            var task = UI.Modal.OpenAsync(new MarkdownBoxRequest { Text = "body" });
            var listener = UI.Modal.TopScreen
                .RootGameObject.GetComponent<ModalEscapeListener>();
            Assert.IsNotNull(listener);
            listener.FireForTests();
            Assert.IsTrue(task.GetAwaiter().GetResult());
            Assert.IsFalse(UI.Modal.IsAnyOpen);
        }

        [Test]
        public void Custom_onLinkClicked_receives_url()
        {
            string captured = null;
            UI.Modal.OpenAsync(new MarkdownBoxRequest
            {
                Text = "[a](https://example.com)",
                OnLinkClicked = url => captured = url,
            });
            Md().RaiseLinkClickedForTests("https://example.com");
            Assert.AreEqual("https://example.com", captured);
        }

        [Test]
        public void Static_Open_completes_on_close()
        {
            var task = MarkdownBox.Open("body", title: "T");
            UI.Modal.TopScreen.Get<PBtn>("close").SimulateClick();
            task.GetAwaiter().GetResult();   // 非泛型 Awaitable,不抛即通过
            Assert.IsFalse(UI.Modal.IsAnyOpen);
        }

        [Test]
        public void Cancel_via_ct_throws_OperationCanceled()
        {
            var cts = new System.Threading.CancellationTokenSource();
            var task = MarkdownBox.Open("body", ct: cts.Token);
            cts.Cancel();
            Assert.Throws<System.OperationCanceledException>(
                () => task.GetAwaiter().GetResult());
            Assert.IsFalse(UI.Modal.IsAnyOpen);
        }

        [Test]
        public void Default_xml_src_loads_builtin_template()
        {
            MarkdownBox.XmlSrc = "PromptUGUI/Modals/MarkdownBox.ui";
            var task = UI.Modal.OpenAsync(new MarkdownBoxRequest { Text = "# T", Title = "公告" });
            Assert.AreEqual("# T", Md().Text);
            Assert.IsTrue(UI.Modal.TopScreen.Get<PText>("title").GameObject.activeSelf);
            UI.Modal.TopScreen.Get<PBtn>("close").SimulateClick();
            Assert.IsTrue(task.GetAwaiter().GetResult());
        }

        // 第一个带 margin.portrait 的内置模态:开着公告翻转横竖屏,ReSolve 不得动运行时状态。
        [Test]
        public void Variant_flip_keeps_text_and_hidden_title()
        {
            MarkdownBox.XmlSrc = "PromptUGUI/Modals/MarkdownBox.ui";
            UI.Modal.OpenAsync(new MarkdownBoxRequest { Text = "# T" });   // no Title
            UI.Variants.Set("portrait", true);   // triggers ReSolve
            Assert.AreEqual("# T", Md().Text);
            Assert.IsFalse(UI.Modal.TopScreen.Get<PText>("title").GameObject.activeSelf);
        }
    }
}
