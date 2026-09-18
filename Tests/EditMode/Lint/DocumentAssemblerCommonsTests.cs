using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PromptUGUI.IR;
using PromptUGUI.Parser;
using PromptUGUI.Template;

namespace PromptUGUI.Tests.EditMode.Lint
{
    /// <summary>
    /// <see cref="DocumentAssembler.AddCommonLibrary"/> is the one place a common library enters the
    /// commons pool — the namespace rebase, the same-name conflict and the origin stamp used to live
    /// in <c>UI.LoadCommonLibraryAsync</c>, out of the CLI's reach, so the linter could not merge
    /// commons the way the runtime does. Now the runtime, the Editor lint menu and the CLI all call
    /// this (2026-09-18 commons-settings spec §4.6).
    /// </summary>
    public class DocumentAssemblerCommonsTests
    {
        private static UIDocument Parse(string body, string src) =>
            UIDocumentParser.Parse(
                "<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'>" + body + "</PromptUGUI>", src);

        private static LoadedDoc Assemble(string src, params (string Src, UIDocument Doc)[] docs)
        {
            var bySrc = docs.ToDictionary(d => d.Src, d => d.Doc);
            return DocumentAssembler.Assemble(src, s => bySrc.TryGetValue(s, out var d) ? d : null,
                                              allowScreens: false);
        }

        private readonly Dictionary<TemplateKey, TemplateDef> _pool = new();
        private readonly Dictionary<StyleKey, StyleDef> _styles = new();

        [SetUp]
        public void SetUp()
        {
            _pool.Clear();
            _styles.Clear();
        }

        [Test]
        public void BareLibrary_KeepsBareKeys_AndStampsTheOrigin()
        {
            var lib = Assemble("lib.ui", ("lib.ui", Parse(
                "<Template name='Card'><Frame/></Template><Style name='badge' color='#fff'/>", "lib.ui")));

            DocumentAssembler.AddCommonLibrary(lib, ns: null, originSrc: "lib.ui", _pool, _styles);

            Assert.AreEqual("lib.ui", _pool[new TemplateKey(null, "Card")].OriginSrc);
            Assert.AreEqual("lib.ui", _styles[new StyleKey(null, "badge")].OriginSrc);
        }

        [Test]
        public void Namespace_RebasesTemplateAndStyleKeys()
        {
            var lib = Assemble("lib.ui", ("lib.ui", Parse(
                "<Template name='Card'><Frame/></Template><Style name='badge' color='#fff'/>", "lib.ui")));

            DocumentAssembler.AddCommonLibrary(lib, "ui", "lib.ui", _pool, _styles);

            Assert.IsTrue(_pool.ContainsKey(new TemplateKey("ui", "Card")), "<ui.Card/>");
            Assert.IsTrue(_styles.ContainsKey(new StyleKey("ui", "badge")), "class='ui:badge'");
            Assert.IsFalse(_pool.ContainsKey(new TemplateKey(null, "Card")), "the bare name is not visible");
        }

        // Kept from the original UI.LoadCommonLibraryAsync: the library's own <Import as='x'> namespace
        // is replaced by the one it is loaded under, not nested inside it (M4 spec §4.2-6).
        [Test]
        public void Namespace_OverridesTheLibrarysOwnImportNamespace()
        {
            var lib = Assemble("lib.ui",
                ("lib.ui", Parse("<Import src='inner.ui' as='x'/>", "lib.ui")),
                ("inner.ui", Parse("<Template name='Inner'><Frame/></Template>", "inner.ui")));

            DocumentAssembler.AddCommonLibrary(lib, "ui", "lib.ui", _pool, _styles);

            Assert.IsTrue(_pool.ContainsKey(new TemplateKey("ui", "Inner")));
            Assert.IsFalse(_pool.ContainsKey(new TemplateKey("x", "Inner")));
        }

        [Test]
        public void ConflictWithThePool_Throws_AndLeavesThePoolUntouched()
        {
            _pool[new TemplateKey(null, "Card")] = new TemplateDef("Card") { OriginSrc = "first.ui" };
            // 'Fresh' comes first in document order: all-or-nothing means it must NOT land either.
            var lib = Assemble("second.ui", ("second.ui", Parse(
                "<Template name='Fresh'><Frame/></Template><Template name='Card'><Frame/></Template>", "second.ui")));

            var ex = Assert.Throws<TemplateException>(() =>
                DocumentAssembler.AddCommonLibrary(lib, null, "second.ui", _pool, _styles));

            StringAssert.Contains("common library conflict: 'Card' already in commons pool", ex.Message);
            Assert.AreEqual(1, _pool.Count, "nothing from the failed library is committed");
            Assert.AreEqual("first.ui", _pool[new TemplateKey(null, "Card")].OriginSrc);
        }

        [Test]
        public void StyleConflictWithThePool_Throws()
        {
            _styles[new StyleKey("ui", "badge")] = new StyleDef("badge");
            var lib = Assemble("lib.ui", ("lib.ui", Parse("<Style name='badge' color='#fff'/>", "lib.ui")));

            var ex = Assert.Throws<TemplateException>(() =>
                DocumentAssembler.AddCommonLibrary(lib, "ui", "lib.ui", _pool, _styles));

            StringAssert.Contains("common library conflict: style 'ui:badge' already in commons pool", ex.Message);
        }

        // Two library entries collapsing onto one rebased key used to overwrite each other silently.
        [Test]
        public void TwoEntriesRebasingToTheSameKey_Throw()
        {
            var lib = Assemble("lib.ui",
                ("lib.ui", Parse("<Import src='inner.ui' as='x'/><Template name='T'><Frame/></Template>", "lib.ui")),
                ("inner.ui", Parse("<Template name='T'><Frame/></Template>", "inner.ui")));

            Assert.Throws<TemplateException>(() =>
                DocumentAssembler.AddCommonLibrary(lib, "ui", "lib.ui", _pool, _styles));
            Assert.IsEmpty(_pool);
        }

        [Test]
        public void Themes_AreAppendedWithTheirSrc_WhenAsked()
        {
            var lib = Assemble("theme.ui", ("theme.ui", Parse(
                "<Theme name='dark'><Color name='bg' value='#000'/></Theme>", "theme.ui")));
            var themes = new List<(ThemeBlock Theme, string Src)>();

            DocumentAssembler.AddCommonLibrary(lib, null, "theme.ui", _pool, _styles, themes);

            var (theme, src) = themes.Single();
            Assert.AreEqual("dark", theme.Name);
            Assert.AreEqual("theme.ui", src);
        }

        [Test]
        public void Themes_AreIgnored_WhenNoListIsPassed()
        {
            var lib = Assemble("theme.ui", ("theme.ui", Parse(
                "<Theme name='dark'><Color name='bg' value='#000'/></Theme>", "theme.ui")));

            Assert.DoesNotThrow(() =>
                DocumentAssembler.AddCommonLibrary(lib, null, "theme.ui", _pool, _styles));
        }
    }
}
