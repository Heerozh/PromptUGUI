using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Application.Modals;
using UnityEngine;

namespace PromptUGUI.Tests.Router
{
    public class RouterPromptTests
    {
        private static string PageXml(string name) =>
            $@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='{name}'><Image id='bg' anchor='stretch'/></Screen></PromptUGUI>";

        [SetUp]
        public void SetUp()
        {
            UI.ResetForTests();
            var files = new Dictionary<string, string>
            {
                ["home"] = PageXml("home"),
                ["shop"] = PageXml("shop"),
            };
            UI.SourceResolver = src =>
                AwaitableHelpers.Completed(files.TryGetValue(src, out var v) ? v : null);
            UI.Router.Map("home", "home");
            UI.Router.Map("shop", "shop", parent: "home");
        }

        [TearDown] public void TearDown() => UI.ResetForTests();

        [Test]
        public void Prompt_RunCompletes_AutoPops()
        {
            bool ran = false;
            UI.Router.MapPrompt("ask", parent: "home", run: (q, ct) =>
            {
                ran = true;
                return AwaitableHelpers.Completed();   // 立即完成
            });

            UI.Router.Open("ask").GetAwaiter().GetResult();

            Assert.IsTrue(ran);
            CollectionAssert.AreEqual(new[] { "home" }, UI.Router.Chain.ToList());   // 自动出栈
        }

        [Test]
        public void Prompt_ReceivesQuery()
        {
            string seen = null;
            UI.Router.MapPrompt("ask", parent: "home", run: (q, ct) =>
            {
                seen = q["reason"];
                return AwaitableHelpers.Completed();
            });
            UI.Router.Navigate("appid://ask?reason=illegal").GetAwaiter().GetResult();
            Assert.AreEqual("illegal", seen);
        }

        [Test]
        public void Prompt_NavigatedAway_Cancels()
        {
            // run 永不自完成 → 靠 ct 取消。canceled 在 ct.Register 回调里置位:
            // cts.Cancel() 同步触发该回调,不依赖 await 续体的恢复时机(EditMode 无 PlayerLoop)。
            bool canceled = false;
            UI.Router.MapPrompt("ask", parent: "home", run: async (q, ct) =>
            {
                var box = new AwaitableCompletionSource();
                ct.Register(() => { canceled = true; box.TrySetResult(); });
                await box.Awaitable;
            });

            UI.Router.Open("ask").GetAwaiter().GetResult();   // 起 prompt(挂起)
            CollectionAssert.AreEqual(new[] { "home", "ask" }, UI.Router.Chain.ToList());

            UI.Router.Open("shop").GetAwaiter().GetResult();  // 导航走 → 取消 prompt
            Assert.IsTrue(canceled);                          // 取消同步发生
            CollectionAssert.AreEqual(new[] { "home", "shop" }, UI.Router.Chain.ToList());
        }
    }
}
