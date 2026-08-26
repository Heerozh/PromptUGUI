using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PromptUGUI.IR;
using PromptUGUI.Parser;
using PromptUGUI.Template;

namespace PromptUGUI.Tests.EditMode.Template
{
    /// <summary>
    /// Folding theme <c>&lt;Style&gt;</c> packs over the global ones (2026-08-26 spec §4.2). The
    /// global style acts as the implicit root of every theme chain, which is what makes a
    /// theme-less project cost nothing and lets a theme spell out only what differs.
    /// </summary>
    public class ThemeStyleResolverTests
    {
        private static UIDocument Parse(string body) =>
            UIDocumentParser.Parse(
                "<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'>" + body + "</PromptUGUI>");

        private static IReadOnlyDictionary<StyleKey, StyleDef> Global(UIDocument doc) =>
            doc.Styles.ToDictionary(kv => new StyleKey(null, kv.Key), kv => kv.Value);

        private static IReadOnlyDictionary<string, ThemeBlock> Themes(UIDocument doc) =>
            doc.Themes.ToDictionary(t => t.Name);

        private static StyleDef Fold(string body, string activeTheme, string styleName = "card")
        {
            var doc = Parse(body);
            var resolved = ThemeStyleResolver.Resolve(Global(doc), Themes(doc), activeTheme);
            return resolved[new StyleKey(null, styleName)];
        }

        [Test]
        public void NoActiveTheme_ReturnsTheGlobalTableItself()
        {
            var doc = Parse("<Style name='card' radius='16'/><Theme name='pixel'><Style name='card' radius='0'/></Theme>");
            var global = Global(doc);

            Assert.AreSame(global, ThemeStyleResolver.Resolve(global, Themes(doc), null),
                "no active theme must not even allocate — this is the path every theme-less project takes");
        }

        [Test]
        public void ThemeWithNoStyles_ReturnsTheGlobalTableItself()
        {
            var doc = Parse("<Style name='card' radius='16'/><Theme name='pixel'><Color name='surface' value='#fff'/></Theme>");
            var global = Global(doc);

            Assert.AreSame(global, ThemeStyleResolver.Resolve(global, Themes(doc), "pixel"));
        }

        [Test]
        public void ThemeOverridesOnlyWhatItDeclares_GlobalSuppliesTheRest()
        {
            var card = Fold(@"
                <Style name='card' sprite='ui:panel' radius='16' borderWidth='1'/>
                <Theme name='pixel'><Style name='card' sprite='px:panel' radius='0'/></Theme>", "pixel");

            Assert.AreEqual("px:panel", card.Attributes["sprite"], "overridden");
            Assert.AreEqual("0", card.Attributes["radius"], "overridden");
            Assert.AreEqual("1", card.Attributes["borderWidth"],
                "not declared by the theme -> falls back to the global baseline, which is what keeps "
                + "a theme switch from leaving a stale value behind");
        }

        [Test]
        public void StyleTheThemeNeverMentions_IsUntouched()
        {
            var doc = Parse(@"
                <Style name='card' radius='16'/>
                <Style name='chip' radius='4'/>
                <Theme name='pixel'><Style name='card' radius='0'/></Theme>");

            var resolved = ThemeStyleResolver.Resolve(Global(doc), Themes(doc), "pixel");
            Assert.AreEqual("4", resolved[new StyleKey(null, "chip")].Attributes["radius"]);
        }

        [Test]
        public void ThemeStyleWithNoGlobalCounterpart_IsAddedOutright()
        {
            var doc = Parse("<Theme name='pixel'><Style name='pixel-only' radius='0'/></Theme>");
            var resolved = ThemeStyleResolver.Resolve(Global(doc), Themes(doc), "pixel");

            Assert.AreEqual("0", resolved[new StyleKey(null, "pixel-only")].Attributes["radius"]);
        }

        [Test]
        public void BaseChain_FoldsRootFirst_SoTheActiveThemeWins()
        {
            var card = Fold(@"
                <Style name='card' sprite='ui:panel' radius='16' borderWidth='1'/>
                <Theme name='modern'><Style name='card' radius='12' borderWidth='2'/></Theme>
                <Theme name='pixel' base='modern'><Style name='card' radius='0'/></Theme>", "pixel");

            Assert.AreEqual("0", card.Attributes["radius"], "leaf beats base");
            Assert.AreEqual("2", card.Attributes["borderWidth"], "base beats global");
            Assert.AreEqual("ui:panel", card.Attributes["sprite"], "global survives both");
        }

        // Same atomic rule the multi-class fold uses: a name is claimed whole, in either form.
        [Test]
        public void VariantOnlyOverride_MasksTheLowerLayersBaseValue()
        {
            var card = Fold(@"
                <Style name='card' radius='16'/>
                <Theme name='pixel'><Style name='card' radius.mobile='4'/></Theme>", "pixel");

            Assert.IsFalse(card.Attributes.ContainsKey("radius"),
                "declaring radius.mobile claims the whole 'radius' slot — the global base must not "
                + "sneak in beside it");
            CollectionAssert.AreEqual(new[] { ("mobile", "4") }, card.VariantOverrides["radius"]);
        }

        [Test]
        public void BaseValueOverride_MasksTheLowerLayersVariantEntries()
        {
            var card = Fold(@"
                <Style name='card' radius='16' radius.mobile='8'/>
                <Theme name='pixel'><Style name='card' radius='0'/></Theme>", "pixel");

            Assert.AreEqual("0", card.Attributes["radius"]);
            Assert.IsFalse(card.VariantOverrides.ContainsKey("radius"),
                "the theme claimed 'radius'; the global radius.mobile goes with it");
        }

        [Test]
        public void Folding_DoesNotMutateTheGlobalPack()
        {
            var doc = Parse(@"
                <Style name='card' radius='16'/>
                <Theme name='pixel'><Style name='card' radius='0'/></Theme>");

            ThemeStyleResolver.Resolve(Global(doc), Themes(doc), "pixel");

            Assert.AreEqual("16", doc.Styles["card"].Attributes["radius"],
                "the global pack is shared by every theme — folding must copy, never write through");
        }

        [Test]
        public void UnknownActiveThemeName_FallsBackToTheGlobalTable()
        {
            var doc = Parse("<Style name='card' radius='16'/><Theme name='pixel'><Style name='card' radius='0'/></Theme>");
            var global = Global(doc);

            Assert.AreSame(global, ThemeStyleResolver.Resolve(global, Themes(doc), "typo"),
                "an unregistered name already warns elsewhere; the fold must not throw on it");
        }

        // ThemeStore.ResolveBases reports cycles properly at registration; this layer is also reached
        // by the lint CLI, which must survive the very markup it is diagnosing.
        [Test]
        public void BaseCycle_TerminatesInsteadOfHanging()
        {
            var a = new ThemeBlock { Name = "a", BaseName = "b" };
            var b = new ThemeBlock { Name = "b", BaseName = "a" };
            a.Styles["card"] = StyleWith("radius", "1");
            b.Styles["card"] = StyleWith("radius", "2");

            var resolved = ThemeStyleResolver.Resolve(
                new Dictionary<StyleKey, StyleDef>(),
                new Dictionary<string, ThemeBlock> { ["a"] = a, ["b"] = b },
                "a");

            Assert.AreEqual("1", resolved[new StyleKey(null, "card")].Attributes["radius"],
                "walking a -> b stops when 'a' repeats, and 'a' is still the leaf so it wins");
        }

        private static StyleDef StyleWith(string attr, string value)
        {
            var s = new StyleDef("card");
            s.Attributes[attr] = value;
            return s;
        }
    }
}
