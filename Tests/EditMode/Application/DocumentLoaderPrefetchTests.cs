using System;
using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using UnityEngine;

namespace PromptUGUI.Tests.Application
{
    /// <summary>
    /// <c>DocumentLoader.PrefetchAsync</c> 的并行预取语义（2026-09-14 document-load spec §4）：
    /// 一份文档取到后，它的全部 <c>&lt;Import&gt;</c> 同层一起起飞；同一 src 在一次 Load 里只请求一次；
    /// 多个 Import 失败时按声明顺序报第一个，且所有请求都被等完。
    /// resolver 是手动完成的（<see cref="AwaitableCompletionSource{T}"/>），
    /// 靠 Unity <c>Awaitable</c> 在主线程同步跑续体这一点逐步驱动。
    /// </summary>
    public class DocumentLoaderPrefetchTests
    {
        private sealed class ManualFiles
        {
            public readonly Dictionary<string, string> Map = new();
            public readonly List<string> Requested = new();
            private readonly Dictionary<string, List<AwaitableCompletionSource<string>>> _pending = new();

            public Func<string, Awaitable<string>> Resolver => src =>
            {
                Requested.Add(src);
                var acs = new AwaitableCompletionSource<string>();
                if (!_pending.TryGetValue(src, out var list)) _pending[src] = list = new();
                list.Add(acs);
                return acs.Awaitable;
            };

            public void Complete(string src)
            {
                foreach (var acs in Take(src)) acs.SetResult(Map[src]);
            }

            public void Fail(string src, Exception e)
            {
                foreach (var acs in Take(src)) acs.SetException(e);
            }

            public int RequestCount(string src)
            {
                var n = 0;
                foreach (var r in Requested) if (r == src) n++;
                return n;
            }

            private List<AwaitableCompletionSource<string>> Take(string src)
            {
                if (!_pending.TryGetValue(src, out var list) || list.Count == 0)
                    throw new InvalidOperationException($"no pending request for '{src}'");
                _pending.Remove(src);
                return list;
            }
        }

        private const string Wrap = "<?xml version='1.0'?><PromptUGUI version='1'>{0}</PromptUGUI>";

        private static string W(string body) => string.Format(Wrap, body);

        private static string Tpl(string name) => W($"<Template name='{name}'><Frame/></Template>");

        [Test]
        public void Imports_of_one_document_are_requested_before_any_completes()
        {
            var mf = new ManualFiles
            {
                Map =
                {
                    ["main"] = W("<Import src='a'/><Import src='b'/><Import src='c'/><Screen name='S'><Frame/></Screen>"),
                    ["a"] = Tpl("Ta"),
                    ["b"] = Tpl("Tb"),
                    ["c"] = Tpl("Tc"),
                }
            };

            var task = DocumentLoader.LoadAsync("main", mf.Resolver, allowScreens: true);
            CollectionAssert.AreEqual(new[] { "main" }, mf.Requested);

            mf.Complete("main");
            // 入口一到手，三个 Import 就该全部在飞——不是取完 a 再取 b
            CollectionAssert.AreEqual(new[] { "main", "a", "b", "c" }, mf.Requested);

            mf.Complete("b");
            mf.Complete("a");
            mf.Complete("c");

            var loaded = task.GetAwaiter().GetResult();
            CollectionAssert.AreEquivalent(new[] { "main", "a", "b", "c" }, loaded.AllSrcs);
            Assert.AreEqual(3, loaded.Templates.Count);
            Assert.AreEqual(1, loaded.Screens.Count);
        }

        [Test]
        public void Nested_imports_fan_out_per_level()
        {
            var mf = new ManualFiles
            {
                Map =
                {
                    ["main"] = W("<Import src='a'/><Import src='b'/>"),
                    ["a"] = W("<Import src='a1'/><Import src='a2'/>"),
                    ["b"] = W("<Import src='b1'/>"),
                    ["a1"] = Tpl("A1"),
                    ["a2"] = Tpl("A2"),
                    ["b1"] = Tpl("B1"),
                }
            };

            var task = DocumentLoader.LoadAsync("main", mf.Resolver, allowScreens: false);
            mf.Complete("main");
            CollectionAssert.AreEqual(new[] { "main", "a", "b" }, mf.Requested);

            // a 到手：a 的两个孩子起飞；b 还没回来，b1 不能被请求
            mf.Complete("a");
            CollectionAssert.AreEqual(new[] { "main", "a", "b", "a1", "a2" }, mf.Requested);

            mf.Complete("b");
            CollectionAssert.AreEqual(new[] { "main", "a", "b", "a1", "a2", "b1" }, mf.Requested);

            mf.Complete("b1");
            mf.Complete("a2");
            mf.Complete("a1");

            var loaded = task.GetAwaiter().GetResult();
            CollectionAssert.AreEquivalent(new[] { "main", "a", "b", "a1", "a2", "b1" }, loaded.AllSrcs);
            Assert.AreEqual(3, loaded.Templates.Count);
        }

        [Test]
        public void Diamond_import_fetched_once()
        {
            var mf = new ManualFiles
            {
                Map =
                {
                    ["main"] = W("<Import src='a'/><Import src='b'/>"),
                    ["a"] = W("<Import src='d'/>"),
                    ["b"] = W("<Import src='d'/>"),
                    ["d"] = Tpl("D"),
                }
            };

            var task = DocumentLoader.LoadAsync("main", mf.Resolver, allowScreens: false);
            mf.Complete("main");
            mf.Complete("a");
            Assert.AreEqual(1, mf.RequestCount("d"));

            // b 也 Import d：d 已在飞，不能再请求一次
            mf.Complete("b");
            Assert.AreEqual(1, mf.RequestCount("d"));

            mf.Complete("d");
            var loaded = task.GetAwaiter().GetResult();
            Assert.AreEqual(1, loaded.Templates.Count);
            CollectionAssert.AreEquivalent(new[] { "main", "a", "b", "d" }, loaded.AllSrcs);
        }

        [Test]
        public void Failing_import_reports_first_in_declaration_order_after_all_settle()
        {
            var mf = new ManualFiles
            {
                Map =
                {
                    ["main"] = W("<Import src='a'/><Import src='b'/>"),
                }
            };

            var task = DocumentLoader.LoadAsync("main", mf.Resolver, allowScreens: false);
            mf.Complete("main");
            CollectionAssert.AreEqual(new[] { "main", "a", "b" }, mf.Requested);

            // 晚声明的 b 先失败，但报出来的必须是先声明的 a——错误顺序与顺序取源时代一致
            mf.Fail("b", new System.IO.IOException("b is broken"));
            mf.Fail("a", new System.IO.IOException("a is broken"));

            var ex = Assert.Throws<System.IO.IOException>(() => task.GetAwaiter().GetResult());
            StringAssert.Contains("a is broken", ex.Message);
        }

        [Test]
        public void Parse_error_in_parallel_import_is_wrapped_with_src()
        {
            var mf = new ManualFiles
            {
                Map =
                {
                    ["main"] = W("<Import src='a'/><Import src='b'/>"),
                    ["a"] = Tpl("A"),
                    ["b"] = "<PromptUGUI version='1'><Template name='B'><Frame/>",   // 没闭合
                }
            };

            var task = DocumentLoader.LoadAsync("main", mf.Resolver, allowScreens: false);
            mf.Complete("main");
            mf.Complete("b");
            mf.Complete("a");

            var ex = Assert.Throws<PromptUGUI.Parser.ParseException>(() => task.GetAwaiter().GetResult());
            StringAssert.Contains("src='b'", ex.Message);
        }
    }
}
