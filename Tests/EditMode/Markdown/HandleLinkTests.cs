using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PromptUGUI.Application;
using UnityEngine.TestTools;

namespace PromptUGUI.Tests.Markdown
{
    public class HandleLinkTests
    {
        private const string PageXml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='p1'>
  <Image id='backdrop' anchor='stretch' color='#0000007F'/>
</Screen></PromptUGUI>";

        private List<string> _opened;

        [SetUp]
        public void SetUp()
        {
            UI.ResetForTests();
            _opened = new List<string>();
            UI.Markdown.OpenUrlHookForTests = url => _opened.Add(url);
            var files = new Dictionary<string, string> { ["p1"] = PageXml };
            UI.SourceResolver = src =>
                AwaitableHelpers.Completed(files.TryGetValue(src, out var v) ? v : null);
        }

        [TearDown]
        public void TearDown() => UI.ResetForTests();

        [Test]
        public void Scheme_match_navigates_via_router()
        {
            UI.Router.Scheme = "app";
            UI.Router.Map("p1", "p1");
            UI.Markdown.HandleLink("app://p1");
            // EditMode 假 resolver 下 Navigate 同步完成,链路立即可断言
            CollectionAssert.AreEqual(new[] { "p1" }, UI.Router.Chain.ToList());
            Assert.IsEmpty(_opened);
        }

        [Test]
        public void Other_urls_fall_back_to_open_url()
        {
            UI.Router.Scheme = "app";
            UI.Markdown.HandleLink("https://example.com");
            CollectionAssert.AreEqual(new[] { "https://example.com" }, _opened);
        }

        [Test]
        public void No_scheme_configured_everything_falls_back()
        {
            UI.Router.Scheme = null;
            UI.Markdown.HandleLink("app://p1");
            CollectionAssert.AreEqual(new[] { "app://p1" }, _opened);
        }

        // scheme 命中但路由失败 → LogError,不回落系统浏览器(spec §4)
        [Test]
        public void Failed_navigation_logs_error_no_browser_fallback()
        {
            UI.Router.Scheme = "app";   // 不注册任何路由
            LogAssert.Expect(UnityEngine.LogType.Error,
                new System.Text.RegularExpressions.Regex("HandleLink"));
            UI.Markdown.HandleLink("app://nope");
            Assert.IsEmpty(_opened);
        }
    }
}
