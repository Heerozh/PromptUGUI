using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.IR;
using UnityEngine;

namespace PromptUGUI.Tests.Application
{
    /// <summary>
    /// 加载文档时 <c>Theme.Changed</c> 只在当前主题真的变了才广播（2026-09-14 document-load spec §6）：
    /// (a) 当前主题从不可解析变可解析（先 Set 后加载）；(b) 当前主题链上有块被新增 / 替换。
    /// 加载一个没碰当前主题链的文档——没有 &lt;Theme&gt;、或带来的 &lt;Theme&gt; 与缓存里是同一份——不广播，
    /// 否则每次加载都让全部已开 Screen 整屏 ReSolve。
    /// </summary>
    public class ThemeChangedOnLoadTests
    {
        private Dictionary<string, string> _files;
        private int _fired;
        private string _lastFired;

        private const string Wrap = "<?xml version='1.0'?><PromptUGUI version='1'>{0}</PromptUGUI>";

        private static string W(string body) => string.Format(Wrap, body);

        private const string LightTheme = "<Theme name='light'><Color name='primary' value='#ff8800'/></Theme>";
        private const string DarkTheme = "<Theme name='dark' base='light'><Color name='primary' value='#cc6600'/></Theme>";

        [SetUp]
        public void SetUp()
        {
            UI.ResetForTests();
            _files = new Dictionary<string, string>();
            UI.SourceResolver = src => AwaitableHelpers.Completed(_files.TryGetValue(src, out var v) ? v : null);
            _fired = 0;
            _lastFired = null;
            UI.Theme.Changed += n => { _fired++; _lastFired = n; };
        }

        [TearDown] public void TearDown() => UI.ResetForTests();

        [Test]
        public void Loading_document_without_theme_change_does_not_fire_Changed()
        {
            _files["themes/main"] = W(LightTheme);
            UI.LoadCommonLibraryAsync("themes/main").GetAwaiter().GetResult();
            Assert.AreEqual("light", UI.Theme.Current, "单主题自动选中");
            Assert.AreEqual(1, _fired, "自动选中广播一次");

            _files["screen/a"] = W("<Screen name='A'><Frame color='primary'/></Screen>");
            UI.LoadDocumentAsync("screen/a").GetAwaiter().GetResult();
            Assert.AreEqual(1, _fired, "文档没有 <Theme>，当前主题没变，不该广播");

            _files["screen/b"] = W("<Screen name='B'><Frame color='primary'/></Screen>");
            UI.LoadDocumentAsync("screen/b").GetAwaiter().GetResult();
            Assert.AreEqual(1, _fired);
        }

        [Test]
        public void Loading_second_document_importing_same_theme_src_does_not_fire_Changed()
        {
            _files["themes/main"] = W(LightTheme);
            _files["screen/a"] = W("<Import src='themes/main'/><Screen name='A'><Frame/></Screen>");
            _files["screen/b"] = W("<Import src='themes/main'/><Screen name='B'><Frame/></Screen>");

            UI.LoadDocumentAsync("screen/a").GetAwaiter().GetResult();
            Assert.AreEqual("light", UI.Theme.Current);
            Assert.AreEqual(1, _fired);

            // 同一 src 命中 DocumentCache → 同一个 ThemeBlock 实例 → 没变
            UI.LoadDocumentAsync("screen/b").GetAwaiter().GetResult();
            Assert.AreEqual(1, _fired, "带来的 <Theme> 与已注册的是同一份，不该广播");
        }

        [Test]
        public void PreSet_theme_becoming_resolvable_still_fires_Changed()
        {
            UI.Theme.Set("dark");
            Assert.AreEqual(1, _fired);
            Assert.AreEqual(Color.white, UI.Theme.Resolve("primary"), "未注册前软失败成白");

            _files["themes/main"] = W(LightTheme + DarkTheme);
            UI.LoadCommonLibraryAsync("themes/main").GetAwaiter().GetResult();
            Assert.AreEqual(2, _fired, "(a) 当前主题从不可解析变可解析");
            Assert.AreEqual("dark", _lastFired);
            Assert.AreEqual(new Color32(0xcc, 0x66, 0x00, 0xff), (Color32)UI.Theme.Resolve("primary"));
        }

        [Test]
        public void Reloading_after_UnloadAll_replaces_blocks_and_fires_Changed()
        {
            // re-Play 场景：ThemeStore 常驻、源缓存被 UnloadAll 清掉 → 新解析实例 → Replaced → 广播（保守）
            _files["themes/main"] = W(LightTheme);
            UI.LoadCommonLibraryAsync("themes/main").GetAwaiter().GetResult();
            Assert.AreEqual(1, _fired);

            UI.UnloadAll();
            _files["themes/main"] = W("<Theme name='light'><Color name='primary' value='#00ff00'/></Theme>");
            UI.LoadCommonLibraryAsync("themes/main").GetAwaiter().GetResult();
            Assert.AreEqual(2, _fired, "同 src 换了实例（值可能不同）→ Replaced → 广播");
            Assert.AreEqual(new Color32(0x00, 0xff, 0x00, 0xff), (Color32)UI.Theme.Resolve("primary"));
        }

        [Test]
        public void Replacing_base_of_current_theme_fires_Changed()
        {
            // (b) 看整条链：当前是 dark（base=light），只重读 light 所在的 src，dark 本身没被碰
            _files["themes/light"] = W(LightTheme);
            _files["themes/dark"] = W(DarkTheme);
            UI.LoadCommonLibraryAsync("themes/light").GetAwaiter().GetResult();   // 自动选中 light，广播 1
            UI.LoadCommonLibraryAsync("themes/dark").GetAwaiter().GetResult();    // 链上新增 dark 但 Current 是 light：不广播
            Assert.AreEqual(1, _fired);
            UI.Theme.Set("dark");                                                  // 广播 2
            Assert.AreEqual(2, _fired);

            DocumentCache.Invalidate("themes/light");
            _files["themes/light"] = W("<Theme name='light'><Color name='primary' value='#00ff00'/><Color name='bg' value='#000000'/></Theme>");
            UI.UnloadAllCommonLibraries();
            UI.LoadCommonLibraryAsync("themes/light").GetAwaiter().GetResult();
            Assert.AreEqual(3, _fired, "祖先 light 被替换，dark 的解析结果变了，必须广播");
            Assert.AreEqual("dark", _lastFired);
            Assert.AreEqual(new Color32(0x00, 0x00, 0x00, 0xff), (Color32)UI.Theme.Resolve("bg"), "新加的 bg 经 dark 的链能查到");
        }

        [Test]
        public void Registering_unrelated_theme_does_not_fire_Changed()
        {
            _files["themes/light"] = W(LightTheme);
            UI.LoadCommonLibraryAsync("themes/light").GetAwaiter().GetResult();
            Assert.AreEqual(1, _fired);

            // 新增一个与当前主题链无关的主题：Current 的解析结果没变
            _files["themes/other"] = W("<Theme name='other'><Color name='primary' value='#000000'/></Theme>");
            UI.LoadCommonLibraryAsync("themes/other").GetAwaiter().GetResult();
            Assert.AreEqual(1, _fired);
        }

        [Test]
        public void ThemeStore_Register_reports_whether_anything_changed()
        {
            var block = new ThemeBlock { Name = "t" };
            var colors = new Dictionary<string, ColorSpec>();
            Assert.AreEqual(ThemeStore.RegisterOutcome.Added,
                ThemeStore.Instance.Register("t", null, colors, block.Styles, "src", block));
            Assert.AreEqual(ThemeStore.RegisterOutcome.Unchanged,
                ThemeStore.Instance.Register("t", null, colors, block.Styles, "src", block),
                "同一个解析块实例：没变");

            var again = new ThemeBlock { Name = "t" };
            Assert.AreEqual(ThemeStore.RegisterOutcome.Replaced,
                ThemeStore.Instance.Register("t", null, colors, again.Styles, "src", again),
                "同 src 新实例：保守当作变了");
            Assert.AreEqual(ThemeStore.RegisterOutcome.Replaced,
                ThemeStore.Instance.Register("t", null, colors, again.Styles, "src", null),
                "没有块引用（ReplaceFromSrc 路径 / 旧调用）：永远当作变了");
        }
    }
}
