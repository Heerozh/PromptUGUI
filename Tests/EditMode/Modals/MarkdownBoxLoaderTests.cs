using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Application.Modals;
using UnityEngine;
using UnityEngine.TestTools;
using PBtn = PromptUGUI.Controls.Btn;
using PMarkdown = PromptUGUI.Controls.Markdown;

namespace PromptUGUI.Tests.Modals
{
    public class MarkdownBoxLoaderTests
    {
        private const string MdBoxXml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='test/MdBox2'>
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
            _files = new Dictionary<string, string> { ["test/MdBox2"] = MdBoxXml };
            UI.SourceResolver = src =>
                AwaitableHelpers.Completed(_files.TryGetValue(src, out var v) ? v : null);
            MarkdownBox.XmlSrc = "test/MdBox2";
        }

        [TearDown]
        public void TearDown() => UI.ResetForTests();

        private static PMarkdown Md()
            => UI.Modal.TopScreen.Get<PMarkdown>("markdown");

        [Test]
        public void Completed_loader_replaces_content_immediately()
        {
            UI.Modal.OpenAsync(new MarkdownBoxRequest
            {
                Loader = _ => AwaitableHelpers.Completed("# done"),
            });
            Assert.AreEqual("# done", Md().Text);
        }

        [Test]
        public void Pending_loader_shows_loading_placeholder_then_result()
        {
            var acs = new AwaitableCompletionSource<string>();
            UI.Modal.OpenAsync(new MarkdownBoxRequest { Loader = _ => acs.Awaitable });
            Assert.AreEqual("*Loading…*", Md().Text);
            acs.SetResult("# late");
            Assert.AreEqual("# late", Md().Text);
        }

        [Test]
        public void Custom_loading_text_used()
        {
            var acs = new AwaitableCompletionSource<string>();
            UI.Modal.OpenAsync(new MarkdownBoxRequest
            {
                Loader = _ => acs.Awaitable,
                LoadingText = "稍候…",
            });
            Assert.AreEqual("稍候…", Md().Text);
        }

        // 关窗 → loader 的 ct 取消;迟到的完成不得触碰已销毁控件(EditMode 是
        // DestroyImmediate,真触碰会抛)——本测试无异常即证明守卫生效。
        [Test]
        public void Close_cancels_loader_and_late_result_is_ignored()
        {
            CancellationToken seen = default;
            var acs = new AwaitableCompletionSource<string>();
            var task = UI.Modal.OpenAsync(new MarkdownBoxRequest
            {
                Loader = ct => { seen = ct; return acs.Awaitable; },
            });
            UI.Modal.TopScreen.Get<PBtn>("close").SimulateClick();
            task.GetAwaiter().GetResult();
            Assert.IsTrue(seen.IsCancellationRequested);
            acs.SetResult("# late");   // 恢复 FillAsync;ct 守卫使其直接 return
            Assert.IsFalse(UI.Modal.IsAnyOpen);
        }

        [Test]
        public void Loader_failure_shows_error_markdown()
        {
            LogAssert.Expect(LogType.Warning,
                new System.Text.RegularExpressions.Regex("MarkdownBox loader failed"));
            UI.Modal.OpenAsync(new MarkdownBoxRequest
            {
                Loader = _ => throw new System.InvalidOperationException("boom"),
            });
            StringAssert.StartsWith("**Failed to load.**", Md().Text);
            StringAssert.Contains("boom", Md().Text);
        }

        [Test]
        public void Loader_wins_over_text()
        {
            UI.Modal.OpenAsync(new MarkdownBoxRequest
            {
                Text = "ignored",
                Loader = _ => AwaitableHelpers.Completed("# loaded"),
            });
            Assert.AreEqual("# loaded", Md().Text);
        }

        [Test]
        public void Facade_open_loader_overload_works()
        {
            var task = MarkdownBox.Open(
                _ => AwaitableHelpers.Completed("# f"), title: "T");
            Assert.AreEqual("# f", Md().Text);
            UI.Modal.TopScreen.Get<PBtn>("close").SimulateClick();
            task.GetAwaiter().GetResult();
            Assert.IsFalse(UI.Modal.IsAnyOpen);
        }

        [Test]
        public void Facade_custom_loading_text_used()
        {
            var acs = new AwaitableCompletionSource<string>();
            MarkdownBox.Open(_ => acs.Awaitable, loadingText: "请稍候");
            Assert.AreEqual("请稍候", Md().Text);
        }
    }
}
