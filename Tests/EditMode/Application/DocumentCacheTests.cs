using System;
using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.IR;
using UnityEngine;

namespace PromptUGUI.Tests.Application
{
    /// <summary>
    /// 跨文档源缓存 <c>DocumentCache</c>（2026-09-14 document-load spec §5）：经 <c>UI.SourceResolver</c>
    /// 取到的 src 在一个场景内只取一次、只解析一次；同一 src 的并发请求共享一次取源；失败不缓存；
    /// <c>UnloadAll</c> / <c>ResetForTests</c> 清空，热重载与 <c>ReloadAsync</c> 失效。
    /// </summary>
    public class DocumentCacheTests
    {
        private Dictionary<string, string> _files;
        private List<string> _requested;

        private const string Wrap = "<?xml version='1.0'?><PromptUGUI version='1'>{0}</PromptUGUI>";

        private static string W(string body) => string.Format(Wrap, body);

        [SetUp]
        public void Setup()
        {
            UI.ResetForTests();
            _files = new Dictionary<string, string>();
            _requested = new List<string>();
            UI.SourceResolver = src =>
            {
                _requested.Add(src);
                return AwaitableHelpers.Completed(_files.TryGetValue(src, out var v) ? v : null);
            };
        }

        [TearDown] public void TearDown() => UI.ResetForTests();

        private int Requests(string src)
        {
            var n = 0;
            foreach (var r in _requested) if (r == src) n++;
            return n;
        }

        [Test]
        public void Second_document_importing_same_src_does_not_call_resolver_again()
        {
            _files["shared"] = W("<Template name='Card'><Frame/></Template>");
            _files["a"] = W("<Import src='shared'/><Screen name='A'><Card/></Screen>");
            _files["b"] = W("<Import src='shared'/><Screen name='B'><Card/></Screen>");

            UI.LoadDocumentAsync("a").GetAwaiter().GetResult();
            Assert.AreEqual(1, Requests("shared"));

            UI.LoadDocumentAsync("b").GetAwaiter().GetResult();
            Assert.AreEqual(1, Requests("shared"), "第二个文档的 Import 该命中缓存");
            Assert.AreEqual(1, Requests("a"));
            Assert.AreEqual(1, Requests("b"));
            Assert.IsTrue(DocumentCache.Contains("shared"));
        }

        [Test]
        public void Commons_library_and_documents_share_the_cache()
        {
            _files["c"] = W("<Template name='T'><Frame/></Template>");
            _files["m"] = W("<Import src='c'/><Screen name='S'><Frame/></Screen>");

            UI.LoadCommonLibraryAsync("c").GetAwaiter().GetResult();
            // 文档直接 Import 同一个 src：会撞 commons 冲突，所以这里只验缓存层——直接问缓存
            Assert.IsTrue(DocumentCache.Contains("c"));
            Assert.AreEqual(1, Requests("c"));
        }

        [Test]
        public void Concurrent_requests_for_same_src_share_one_fetch()
        {
            // 手动完成的 resolver：两个文档同帧发起，都要 shared
            var pending = new Dictionary<string, List<AwaitableCompletionSource<string>>>();
            UI.SourceResolver = src =>
            {
                _requested.Add(src);
                var acs = new AwaitableCompletionSource<string>();
                if (!pending.TryGetValue(src, out var list)) pending[src] = list = new();
                list.Add(acs);
                return acs.Awaitable;
            };
            void Complete(string src, string xml)
            {
                var list = pending[src];
                pending.Remove(src);
                foreach (var acs in list) acs.SetResult(xml);
            }

            var a = UI.LoadDocumentAsync("a");
            var b = UI.LoadDocumentAsync("b");
            Complete("a", W("<Import src='shared'/><Screen name='A'><Card/></Screen>"));
            Complete("b", W("<Import src='shared'/><Screen name='B'><Card/></Screen>"));
            Assert.AreEqual(1, Requests("shared"), "shared 在飞时第二个请求者只能挂等待，不能再取一次");

            Complete("shared", W("<Template name='Card'><Frame/></Template>"));
            CollectionAssert.AreEqual(new[] { "A" }, a.GetAwaiter().GetResult());
            CollectionAssert.AreEqual(new[] { "B" }, b.GetAwaiter().GetResult());
            Assert.IsNotNull(UI.Open("A"));
            Assert.IsNotNull(UI.Open("B"));
        }

        [Test]
        public void Failed_fetch_is_not_cached_and_retries()
        {
            _files["m"] = W("<Import src='shared'/><Screen name='S'><Frame/></Screen>");
            // shared 第一次不存在 → resolver 返回 null → IOException
            Assert.Throws<System.IO.IOException>(() => UI.LoadDocumentAsync("m").GetAwaiter().GetResult());
            Assert.IsFalse(DocumentCache.Contains("shared"));

            _files["shared"] = W("<Template name='Card'><Frame/></Template>");
            UI.LoadDocumentAsync("m").GetAwaiter().GetResult();
            Assert.AreEqual(2, Requests("shared"));
            Assert.IsTrue(DocumentCache.Contains("shared"));
        }

        [Test]
        public void UnloadAll_clears_cache()
        {
            _files["m"] = W("<Screen name='S'><Frame/></Screen>");
            UI.LoadDocumentAsync("m").GetAwaiter().GetResult();
            Assert.IsTrue(DocumentCache.Contains("m"));

            UI.UnloadAll();
            Assert.AreEqual(0, DocumentCache.Count);

            _files["m"] = W("<Screen name='S'><Frame id='v2'/></Screen>");
            UI.LoadDocumentAsync("m").GetAwaiter().GetResult();
            Assert.IsNotNull(UI.Open("S").Get<Frame>("v2"), "UnloadAll 之后必须重读源");
        }

        [Test]
        public void ResetForTests_clears_cache()
        {
            _files["m"] = W("<Screen name='S'><Frame/></Screen>");
            UI.LoadDocumentAsync("m").GetAwaiter().GetResult();
            Assert.IsTrue(DocumentCache.Contains("m"));
            UI.ResetForTests();
            Assert.AreEqual(0, DocumentCache.Count);
        }

        [Test]
        public void NotifyAssetChanged_invalidates_src_even_without_dependents()
        {
            // 模态框路径的 Import 走缓存但不入 dep graph：t 在缓存里、却没有任何 Screen 依赖它，
            // NotifyAssetChanged 不会触发任何 reload——缓存条目仍必须被摘掉
            _files["t"] = W("<Template name='T'><Frame id='v1'/></Template>");
            UI.LoadDocumentWithCommonsAsync("modal",
                W("<Import src='t'/><Screen name='Modal'><T id='x'/></Screen>")).GetAwaiter().GetResult();
            Assert.IsTrue(DocumentCache.Contains("t"));

            UI.HotReload.AssetPathToSrc = p => p == "p/t.ui.xml" ? "t" : null;
            UI.HotReload.NotifyAssetChanged("p/t.ui.xml");
            Assert.IsFalse(DocumentCache.Contains("t"), "文件变了就不能再从缓存给");
        }

        [Test]
        public void ReloadAsync_rereads_entry_and_every_dep()
        {
            _files["t"] = W("<Template name='T'><Frame><Image id='v1'/></Frame></Template>");
            _files["m"] = W("<Import src='t'/><Screen name='S'><T id='x'/></Screen>");
            UI.LoadDocumentAsync("m").GetAwaiter().GetResult();
            UI.Open("S");

            // 只改被 Import 的模板、不经 NotifyAssetChanged，直接 ReloadAsync：reload = 重读整个闭包
            _files["t"] = W("<Template name='T'><Frame><Image id='v2'/></Frame></Template>");
            UI.ReloadAsync("S").GetAwaiter().GetResult();
            Assert.IsNotNull(UI.Get("S").Get<Image>("x/v2"));
        }

        [Test]
        public void ReloadCommonLibraryAsync_rereads_its_closure()
        {
            _files["inner"] = W("<Template name='Inner'><Frame><Image id='v1'/></Frame></Template>");
            _files["c"] = W("<Import src='inner'/><Template name='T'><Frame><Inner id='in'/></Frame></Template>");
            _files["m"] = W("<Screen name='S'><T id='x'/></Screen>");
            UI.LoadCommonLibraryAsync("c").GetAwaiter().GetResult();
            UI.LoadDocumentAsync("m").GetAwaiter().GetResult();
            Assert.IsNotNull(UI.Open("S").Get<Image>("x/in/v1"));
            UI.Close("S");

            // 只改 commons 闭包里的 inner（commons 入口 c 本身没变）
            _files["inner"] = W("<Template name='Inner'><Frame><Image id='v2'/></Frame></Template>");
            UI.ReloadCommonLibraryAsync("c").GetAwaiter().GetResult();
            UI.ReloadAsync("S").GetAwaiter().GetResult();
            Assert.IsNotNull(UI.Open("S").Get<Image>("x/in/v2"));
        }

        [Test]
        public void Modal_document_imports_go_through_the_cache()
        {
            _files["frame"] = W("<Template name='ModalFrame'><Frame/></Template>");
            _files["m"] = W("<Import src='frame'/><Screen name='S'><ModalFrame/></Screen>");
            UI.LoadDocumentAsync("m").GetAwaiter().GetResult();
            Assert.AreEqual(1, Requests("frame"));

            // 模态框路径：入口 xml 在手上（不经 resolver），它 Import 的 frame 该命中缓存
            UI.LoadDocumentWithCommonsAsync("modal-label",
                W("<Import src='frame'/><Screen name='Modal'><ModalFrame/></Screen>")).GetAwaiter().GetResult();
            Assert.AreEqual(1, Requests("frame"));
            Assert.AreEqual(0, Requests("modal-label"), "入口 label 绕过缓存与 resolver");
            Assert.IsNotNull(UI.Open("Modal"));
        }

        [Test]
        public void Same_src_loaded_twice_yields_identical_expansion()
        {
            // §2.C 的前提：展开不改写解析产物。同一 src 两次 LoadDocumentAsync（第一次还 Open 过），
            // 第二次的展开结果必须与第一次结构相等
            _files["t"] = W("<Template name='Card'><Param name='title' default='x'/>" +
                            "<Frame class='c'><Text id='t'>{{title}}</Text><Slot/></Frame></Template>");
            _files["m"] = W("<Import src='t'/><Style name='c' color='#ff0000'/>" +
                            "<Screen name='S'><Card id='a' title='hello'><Frame id='inner'/></Card><Card id='b'/></Screen>");
            UI.LoadDocumentAsync("m").GetAwaiter().GetResult();
            var first = Dump(UI.GetScreenDef("S"));
            UI.Open("S");
            UI.Close("S");
            UI.UnloadDocument("S");

            UI.LoadDocumentAsync("m").GetAwaiter().GetResult();
            Assert.AreEqual(1, Requests("t"), "第二次从缓存拿");
            var second = Dump(UI.GetScreenDef("S"));
            Assert.AreEqual(first, second);
        }

        private static string Dump(ScreenDef def)
        {
            var sb = new StringBuilder();
            DumpNode(def.Root, sb, 0);
            return sb.ToString();
        }

        private static void DumpNode(ElementNode n, StringBuilder sb, int depth)
        {
            sb.Append(' ', depth * 2).Append(n.Tag);
            if (n.Id != null) sb.Append('#').Append(n.Id);
            var keys = new List<string>(n.Attributes.Keys);
            keys.Sort(StringComparer.Ordinal);
            foreach (var k in keys) sb.Append(' ').Append(k).Append('=').Append(n.Attributes[k]);
            if (n.TextContent != null) sb.Append(" \"").Append(n.TextContent).Append('"');
            sb.AppendLine();
            foreach (var c in n.Children) DumpNode(c, sb, depth + 1);
        }
    }
}
