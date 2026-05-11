using System;
using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Parser;
using PromptUGUI.Template;
using UnityEngine;

namespace PromptUGUI.Tests.Application
{
    public class DocumentLoaderTests
    {
        private sealed class FakeFiles
        {
            public Dictionary<string, string> Map = new();
            public Func<string, Awaitable<string>> Resolver => s =>
                AwaitableHelpers.Completed(Map.TryGetValue(s, out var v) ? v : null);
        }

        private const string Wrap = "<?xml version='1.0'?><PromptUGUI version='1'>{0}</PromptUGUI>";

        private static string W(string body) => string.Format(Wrap, body);

        [Test]
        public void Single_file_no_imports_loads()
        {
            var ff = new FakeFiles
            {
                Map = {
                ["main"] = W("<Template name='X'><Frame/></Template>"),
            }
            };
            var loaded = DocumentLoader.LoadAsync("main", ff.Resolver, allowScreens: true).GetAwaiter().GetResult();
            Assert.AreEqual(1, loaded.Templates.Count);
            CollectionAssert.AreEquivalent(new[] { "main" }, loaded.AllSrcs);
        }

        [Test]
        public void Nested_imports_resolved_recursively()
        {
            var ff = new FakeFiles
            {
                Map = {
                ["main"] = W("<Import src='a'/><Screen name='S'><Frame/></Screen>"),
                ["a"]    = W("<Import src='b'/><Template name='Ta'><Frame/></Template>"),
                ["b"]    = W("<Template name='Tb'><Frame/></Template>"),
            }
            };
            var loaded = DocumentLoader.LoadAsync("main", ff.Resolver, allowScreens: true).GetAwaiter().GetResult();
            CollectionAssert.AreEquivalent(new[] { "main", "a", "b" }, loaded.AllSrcs);
            Assert.AreEqual(2, loaded.Templates.Count);
        }

        [Test]
        public void Cycle_detected_with_path_in_message()
        {
            var ff = new FakeFiles
            {
                Map = {
                ["main"] = W("<Import src='a'/>"),
                ["a"]    = W("<Import src='b'/>"),
                ["b"]    = W("<Import src='a'/>"),
            }
            };
            var ex = Assert.Throws<ParseException>(() =>
                DocumentLoader.LoadAsync("main", ff.Resolver, allowScreens: true).GetAwaiter().GetResult());
            StringAssert.Contains("cyclic", ex.Message.ToLowerInvariant());
            StringAssert.Contains("a", ex.Message);
            StringAssert.Contains("b", ex.Message);
        }

        [Test]
        public void Allow_screens_false_rejects_Screen_in_entry()
        {
            var ff = new FakeFiles
            {
                Map = {
                ["common"] = W("<Screen name='S'><Frame/></Screen>"),
            }
            };
            Assert.Throws<ParseException>(() =>
                DocumentLoader.LoadAsync("common", ff.Resolver, allowScreens: false).GetAwaiter().GetResult());
        }

        [Test]
        public void Same_src_imported_by_multiple_files_loaded_once()
        {
            var ff = new FakeFiles
            {
                Map = {
                ["main"]   = W("<Import src='a'/><Import src='b'/>"),
                ["a"]      = W("<Import src='shared'/>"),
                ["b"]      = W("<Import src='shared'/>"),
                ["shared"] = W("<Template name='Sh'><Frame/></Template>"),
            }
            };
            var loaded = DocumentLoader.LoadAsync("main", ff.Resolver, allowScreens: false).GetAwaiter().GetResult();
            Assert.AreEqual(1, loaded.Templates.Count);   // Sh 不重复
        }

        [Test]
        public void Resolver_null_throws_InvalidOperation()
        {
            Assert.Throws<InvalidOperationException>(() =>
                DocumentLoader.LoadAsync("x", null, allowScreens: true).GetAwaiter().GetResult());
        }

        [Test]
        public void Resolver_returns_null_throws_IOException()
        {
            Assert.Throws<System.IO.IOException>(() =>
                DocumentLoader.LoadAsync("x", _ => AwaitableHelpers.Completed<string>(null), allowScreens: true).GetAwaiter().GetResult());
        }

        [Test]
        public void Two_imports_define_same_template_name_throws()
        {
            var ff = new FakeFiles
            {
                Map = {
                ["main"] = W(@"<Import src='a'/><Import src='b'/>"),
                ["a"]    = W(@"<Template name='Foo'><Frame/></Template>"),
                ["b"]    = W(@"<Template name='Foo'><Frame/></Template>"),
            }
            };
            var ex = Assert.Throws<TemplateException>(() =>
                DocumentLoader.LoadAsync("main", ff.Resolver, allowScreens: false).GetAwaiter().GetResult());
            StringAssert.Contains("Foo", ex.Message);
            StringAssert.Contains("duplicate", ex.Message.ToLowerInvariant());
        }

        [Test]
        public void Entry_doc_template_collides_with_import_throws()
        {
            var ff = new FakeFiles
            {
                Map = {
                ["main"] = W(@"<Import src='a'/><Template name='Foo'><Frame/></Template>"),
                ["a"]    = W(@"<Template name='Foo'><Frame/></Template>"),
            }
            };
            Assert.Throws<TemplateException>(() =>
                DocumentLoader.LoadAsync("main", ff.Resolver, allowScreens: false).GetAwaiter().GetResult());
        }

        [Test]
        public void Same_template_name_in_different_namespaces_OK()
        {
            var ff = new FakeFiles
            {
                Map = {
                ["main"] = W(@"<Import src='a'/><Import src='b' as='ns'/>"),
                ["a"]    = W(@"<Template name='Foo'><Frame/></Template>"),
                ["b"]    = W(@"<Template name='Foo'><Frame/></Template>"),
            }
            };
            var loaded = DocumentLoader.LoadAsync("main", ff.Resolver, allowScreens: false).GetAwaiter().GetResult();
            Assert.AreEqual(2, loaded.Templates.Count);
        }
    }
}
